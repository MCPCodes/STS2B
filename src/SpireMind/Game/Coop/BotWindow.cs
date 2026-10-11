using System.Runtime.InteropServices;

namespace SpireMind.Game.Coop;

/// <summary>
/// Keeps a bot copy's window out of the human's way: small, in a corner, minimised, silent,
/// capped at a modest frame rate and unable to take the keyboard. Nothing here runs unless
/// <see cref="CoopBots.IsBotCopy"/>: the human's own game is never touched.
///
/// Why minimised is safe (sts2-notes/coop-api-v0.107.1/spawn.md, section 3): a window macOS
/// reports as not visible (minimised, hidden, covered, on another Space) is not drawn, but the
/// engine keeps running frames at the frame limit, and nothing in the game waits for a drawn
/// frame. The game's own background throttle (30 fps once the window loses focus) hangs off the
/// "limit fps in background" setting, which is turned off here. That is read from the engine's
/// code, not measured, so this class also counts its own frames: if a hidden copy drops under
/// <see cref="SlowRate"/> frames a second it first asks macOS not to nap it and then puts the
/// window back as a small tile, and says so in the log.
///
/// Everything is local to this copy: no game action, nothing sent to other players. Main
/// thread only, except <see cref="TileFor"/>, <see cref="Fps"/>, <see cref="FramesPerSecond"/>
/// and <see cref="FrameAgeMs"/>.
/// </summary>
internal static class BotWindow
{
    /// <summary>The window's size in pixels: a ninth of 1080p to draw, when it is drawn at all.</summary>
    public const int Width = 640;

    /// <inheritdoc cref="Width"/>
    public const int Height = 360;

    /// <summary>The top-left corner the game itself falls back to for a window that was off screen.</summary>
    private const int Left = 8;

    /// <inheritdoc cref="Left"/>
    private const int Top = 48;

    /// <summary>60 for now, as the notes ask for the first test; SPIREMIND_BOT_FPS tries another (30 is the game's own background cap).</summary>
    private const int DefaultFps = 60;

    /// <summary>The action executor and many waits advance one step per frame, and the heartbeat answer waits for the next one: never slower than this.</summary>
    private const int LowestFps = 20;

    private const int HighestFps = 240;

    /// <summary>Under this many frames a second a hidden copy counts as throttled: its heartbeat would show as lag on the host.</summary>
    private const int SlowRate = 10;

    /// <summary>How long it has to stay that slow. A scene load can stall for a second or two; the host drops a silent peer after 20.</summary>
    private const long SlowForMs = 6000;

    /// <summary>The frame counter isn't judged while the game is still starting up.</summary>
    private const long WatchAfterMs = 20000;

    /// <summary>After a scene appears, the game may still be applying its own display settings; minimising before that is undone.</summary>
    private const long SettleMs = 500;

    private const long KeepEveryMs = 2000;

    /// <summary>How long after the window is put away the measured frame rate is logged, so a first test can read from the log whether a hidden copy keeps its speed.</summary>
    private const long ReportAfterMs = 10000;

    private enum Mode { Minimized, Tile, Hide }

    private static readonly long StartedAt = System.Environment.TickCount64;

    private static bool _broken;
    private static bool _settingsForced;
    private static bool _settingsFailed;
    private static bool _noFocus;
    private static bool _noFocusFailed;
    private static string? _scene;
    private static long _applyAt;
    private static long _reportAt;
    private static long _nextKeep;
    private static Mode? _mode;
    private static bool _gaveUpHiding;
    private static bool _appHidden;
    private static IntPtr _activity;
    private static bool _activityTried;
    private static int _fps;

    // The frame counter. The rate and the last tick may be read from other threads.
    private static long _windowStart = System.Environment.TickCount64;
    private static int _frames;
    private static long _slowMs;
    private static bool _wasSlow;
    private static int _fallbackStep;
    private static int _rate = -1;
    private static long _lastTick;

    /// <summary>
    /// The mod's frame ticks in the last full second, or -1 before the first second is over
    /// (and always in the human's copy). Shows that a hidden copy is still running at speed.
    /// </summary>
    public static int FramesPerSecond => Volatile.Read(ref _rate);

    /// <summary>Milliseconds since the last frame tick, or -1 before the first. Large while the
    /// bridge still answers means this copy's main thread is frozen or throttled.</summary>
    public static long FrameAgeMs
    {
        get
        {
            var last = Interlocked.Read(ref _lastTick);
            return last == 0 ? -1 : System.Environment.TickCount64 - last;
        }
    }

    /// <summary>The frame limit a bot copy runs at.</summary>
    public static int Fps
    {
        get
        {
            if (_fps != 0) return _fps;
            var fps = int.TryParse(System.Environment.GetEnvironmentVariable("SPIREMIND_BOT_FPS"), out var asked) ? asked : DefaultFps;
            return _fps = Math.Clamp(fps, LowestFps, HighestFps);
        }
    }

    /// <summary>
    /// Where the window of the copy with this client id sits: 640x360 from the top-left corner,
    /// the second and third copy (1001, 1002) beside the first so three tiles don't cover each
    /// other. The profile, the launch arguments and the running copy all use this, so the
    /// window never jumps.
    /// </summary>
    public static (Vector2I Size, Vector2I Position) TileFor(ulong clientId)
    {
        var slot = clientId is >= CoopBots.FirstClientId and < CoopBots.FirstClientId + CoopBots.MaxCopies
            ? (int)(clientId - CoopBots.FirstClientId)
            : 0;
        return (new Vector2I(Width, Height), new Vector2I(Left + slot * (Width + Left), Top));
    }

    /// <summary>
    /// In a bot copy, at mod load: sets the loaded settings to a small silent window, in memory,
    /// and makes the window unable to take the keyboard. The mod's initializer runs after
    /// settings.save was read and before the game applies any of it (volumes, display mode, the
    /// logo and the early-access notice), so the game then does the rest itself. Called later it
    /// still corrects what the game saves at quit, and <see cref="Tick"/> corrects the window
    /// directly. Safe to call again; does nothing in the human's copy.
    /// </summary>
    public static void Start()
    {
        if (!CoopBots.IsBotCopy) return;
        if (!_settingsForced)
        {
            try
            {
                ForceSettings();
                _settingsForced = true;
                ModEntry.Log($"coop: window: settings forced in memory: windowed {Width}x{Height} at {TileFor(BotArgs.NetId).Position}, volumes 0, {Fps} fps, no background fps cap");
            }
            catch (Exception ex)
            {
                // The settings aren't loaded yet, or the class changed in a game update. Tick tries again.
                if (!_settingsFailed)
                {
                    _settingsFailed = true;
                    ModEntry.LogError($"coop: window: forcing the settings failed (trying again every two seconds): {ex.Message}");
                }
            }
        }
        RefuseFocus();
    }

    /// <summary>
    /// Called every frame on the main thread (from <see cref="CoopBots"/>' tick). Inert unless
    /// this is a bot copy. A failure is logged once and leaves the window alone from then on;
    /// the frame counter keeps running.
    /// </summary>
    public static void Tick()
    {
        if (!CoopBots.IsBotCopy) return;
        var now = System.Environment.TickCount64;
        CountFrame(now);
        if (_broken) return;
        try
        {
            Step(now);
        }
        catch (Exception ex)
        {
            _broken = true;
            ModEntry.LogError($"coop: window: failed, leaving the window alone from here on: {ex}");
        }
    }

    private static void Step(long now)
    {
        var keep = now >= _nextKeep;
        if (keep) _nextKeep = now + KeepEveryMs;
        if (keep && (!_settingsForced || !_noFocus)) Start();

        var game = NGame.Instance;
        if (game is null || !GodotObject.IsInstanceValid(game)) return;

        // The first main menu is the sign that the game has applied its own display settings.
        // After that, each new menu, lobby or run scene gets the window put away again.
        var scene = SceneKey(game);
        if (scene is not null && scene != _scene)
        {
            _scene = scene;
            _applyAt = now + SettleMs;
        }
        if (_applyAt != 0 && now >= _applyAt)
        {
            _applyAt = 0;
            PutAway(game, _scene ?? "?");
            _reportAt = now + ReportAfterMs;
        }
        if (_reportAt != 0 && now >= _reportAt)
        {
            _reportAt = 0;
            ModEntry.Log($"coop: window: {FramesPerSecond} frames a second {ReportAfterMs / 1000} s after being put away " +
                         $"(mode {DisplayServer.WindowGetMode()}, focused {DisplayServer.WindowIsFocused()}, max fps {Engine.MaxFps})");
        }

        if (keep && _scene is not null) Keep(game);
    }

    /// <summary>
    /// A window with the no-focus flag refuses to become the key window, so the copy never
    /// takes the keyboard later, and the game's own hotkey and controller handling stay off in
    /// it (they check for focus). Mouse clicks on it still work. It does not take key status
    /// away from a window macOS made key at start; minimising does that. Never set in the
    /// human's game.
    /// </summary>
    private static void RefuseFocus()
    {
        if (_noFocus) return;
        try
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
            _noFocus = true;
            ModEntry.Log($"coop: window: set the no-focus flag (never takes the keyboard); focused now: {DisplayServer.WindowIsFocused()}");
        }
        catch (Exception ex)
        {
            if (_noFocusFailed) return;
            _noFocusFailed = true;
            ModEntry.LogError($"coop: window: setting the no-focus flag failed (trying again every two seconds): {ex.Message}");
        }
    }

    /// <summary>"Lobby:123" and the like: changes whenever a menu, lobby or run scene is new. Null while the game starts up.</summary>
    private static string? SceneKey(NGame game)
    {
        CoopBots.Sight sight;
        try { sight = CoopBots.Look(); }
        catch { return null; } // a run loading or tearing down
        Node? node = sight.Place switch
        {
            CoopBots.Place.Run => game.CurrentRunNode, // null while the run is still set up behind the menu
            CoopBots.Place.Lobby or CoopBots.Place.LoadedLobby => sight.Screen,
            CoopBots.Place.Menu => game.MainMenu,
            _ => null,
        };
        return node is not null && GodotObject.IsInstanceValid(node) ? $"{sight.Place}:{node.GetInstanceId()}" : null;
    }

    /// <summary>The whole treatment, at the first main menu and at every new lobby or run scene.</summary>
    private static void PutAway(NGame game, string why)
    {
        ForceSettings();
        _settingsForced = true;
        RefuseFocus();
        var (size, position) = TileFor(BotArgs.NetId);
        var before = DisplayServer.WindowGetMode();

        // A profile that was fullscreen (an old one, or one the game made itself) goes back to
        // a window through the game's own routine, which reads the settings forced above.
        if (before is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen or DisplayServer.WindowMode.Maximized)
            game.ApplyDisplaySettings();
        else if (before == DisplayServer.WindowMode.Windowed && DisplayServer.WindowGetSize() != size)
        {
            DisplayServer.WindowSetSize(size);
            DisplayServer.WindowSetPosition(DisplayServer.ScreenGetPosition(DisplayServer.WindowGetCurrentScreen()) + position);
        }

        var mode = _gaveUpHiding ? Mode.Tile : WantedMode();
        switch (mode)
        {
            case Mode.Minimized:
                // The copy keeps running at the frame limit while minimised; only drawing stops.
                // Losing key focus by this makes the game's handlers fade the volume (already 0)
                // and would cap the fps at 30, which the forced setting turns off.
                if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Minimized)
                    DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
                break;
            case Mode.Hide:
                HideApp();
                break;
        }

        Mute(game);
        Engine.MaxFps = Fps;
        SetTitle();
        if (System.Environment.GetEnvironmentVariable("SPIREMIND_BOT_NONAP") == "1") BeginActivity();

        ModEntry.Log($"coop: window: put away at {why}: mode {before} -> {DisplayServer.WindowGetMode()} (wanted {mode}), " +
                     $"size {DisplayServer.WindowGetSize()}, position {DisplayServer.WindowGetPosition()}, max fps {Engine.MaxFps}, muted, focused {DisplayServer.WindowIsFocused()}");
    }

    /// <summary>
    /// Every couple of seconds: what the game may have changed back. The join sets its own
    /// window title, a regained focus re-applies the saved volume, the settings screen sets
    /// the fps. The window mode is left alone here, so the human can open a bot's window from
    /// the Dock to look at it; it is put away again at the next lobby or run scene.
    /// </summary>
    private static void Keep(NGame game)
    {
        SetTitle();
        if (Engine.MaxFps != Fps) Engine.MaxFps = Fps;
        var settings = SaveManager.Instance.SettingsSave;
        if (settings.VolumeMaster != 0f || settings.VolumeBgm != 0f || settings.VolumeSfx != 0f || settings.VolumeAmbience != 0f
            || settings.LimitFpsInBackground)
        {
            ForceSettings();
            Mute(game);
            ModEntry.Log("coop: window: volume or background fps setting had changed; muted again");
        }
    }

    /// <summary>The settings the game applies by itself and saves at quit. In memory only: nothing is written here.</summary>
    private static void ForceSettings()
    {
        var settings = SaveManager.Instance.SettingsSave;
        var (size, position) = TileFor(BotArgs.NetId);
        settings.Fullscreen = false;
        settings.WindowSize = size;
        settings.WindowPosition = position;
        settings.FpsLimit = Fps;
        // Otherwise a copy that loses focus in the menu or lobby is capped at 30 fps until it regains it, which it never does.
        settings.LimitFpsInBackground = false;
        settings.VolumeMaster = 0f;
        settings.VolumeBgm = 0f;
        settings.VolumeSfx = 0f;
        settings.VolumeAmbience = 0f;
        settings.SkipIntroLogo = true;
        settings.SeenEaDisclaimer = true;
    }

    /// <summary>
    /// Silence at run time: FMOD (music, effects, ambience) through the game's audio manager,
    /// and the engine's own bus, which a few sounds use. The saved volumes are 0 as well,
    /// because the game re-applies them when a window regains focus.
    /// </summary>
    private static void Mute(NGame game)
    {
        game.AudioManager?.SetMasterVol(0f);
        game.DebugAudio?.SetMasterAudioVolume(0f);
        AudioServer.SetBusMute(0, true);
    }

    private static void SetTitle()
    {
        var who = BotArgs.Character is { } character ? $" ({character})" : "";
        DisplayServer.WindowSetTitle($"Slay the Spire 2 - bot {BotArgs.NetId}{who}");
    }

    private static Mode WantedMode()
    {
        if (_mode is { } known) return known;
        var mode = BotArgs.WindowMode switch
        {
            null or "minimized" or "minimised" => Mode.Minimized,
            "tile" or "window" or "windowed" => Mode.Tile,
            "hide" or "hidden" => Mode.Hide,
            _ => (Mode?)null,
        };
        if (mode is null)
            ModEntry.LogError($"coop: window: unknown mode '{BotArgs.WindowMode}' (minimized, tile or hide); using minimized");
        _mode = mode ?? Mode.Minimized;
        return _mode.Value;
    }

    // ---------- is the hidden copy still running at speed? ----------

    private static void CountFrame(long now)
    {
        Interlocked.Exchange(ref _lastTick, now);
        _frames++;
        var elapsed = now - _windowStart;
        if (elapsed < 1000) return;

        var rate = (int)(_frames * 1000L / elapsed);
        Volatile.Write(ref _rate, rate);
        _frames = 0;
        _windowStart = now;

        if (now - StartedAt < WatchAfterMs) return;
        if (rate >= SlowRate)
        {
            if (_wasSlow) ModEntry.Log($"coop: window: back to {rate} frames a second");
            _wasSlow = false;
            _slowMs = 0;
            return;
        }

        _slowMs += elapsed;
        if (_slowMs < SlowForMs) return;
        _slowMs = 0; // each step gets its own six seconds to show an effect
        _wasSlow = true;
        try { Unthrottle(rate); }
        catch (Exception ex) { ModEntry.LogError($"coop: window: the throttle fallback failed: {ex.Message}"); }
    }

    /// <summary>
    /// The copy has been slow for a while. One step per call: first ask macOS not to nap the
    /// process, then give up hiding and show the window as a small tile, then only report.
    /// </summary>
    private static void Unthrottle(int rate)
    {
        switch (_fallbackStep++)
        {
            case 0:
                ModEntry.LogError($"coop: window: only {rate} frames a second for {SlowForMs / 1000} s (throttled while hidden?); asking macOS not to nap this copy");
                BeginActivity();
                break;
            case 1:
                ModEntry.LogError($"coop: window: still only {rate} frames a second; showing the window as a small tile instead of hiding it");
                _gaveUpHiding = true;
                if (_appHidden) UnhideApp();
                if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Minimized)
                    DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                break;
            default:
                if (_fallbackStep <= 6)
                    ModEntry.LogError($"coop: window: still only {rate} frames a second with the window shown; nothing more to try from in here");
                break;
        }
    }

    // ---------- macOS, through the Objective-C runtime (unverified until a live test; every call is logged first) ----------

    /// <summary>
    /// Hides the whole app, as Cmd+H does. Unlike minimising, hiding the active app hands
    /// activation back to the app that was active before, which is the human's game. Only
    /// with <c>--sts2bot-window=hide</c>.
    /// </summary>
    private static void HideApp()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            ModEntry.Log("coop: window: hiding the app (NSApplication hide:)");
            var app = ObjcSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            if (app == IntPtr.Zero) throw new InvalidOperationException("no NSApplication");
            ObjcSendVoid(app, sel_registerName("hide:"), IntPtr.Zero);
            _appHidden = true;
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: window: hiding the app failed, minimising instead: {ex.Message}");
            _mode = Mode.Minimized;
            if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Minimized)
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
        }
    }

    private static void UnhideApp()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            ModEntry.Log("coop: window: showing the app again (NSApplication unhideWithoutActivation)");
            var app = ObjcSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            if (app != IntPtr.Zero) ObjcSend(app, sel_registerName("unhideWithoutActivation"));
            _appHidden = false;
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: window: showing the app again failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Takes a "user initiated" activity token from NSProcessInfo and keeps it for the life of
    /// the process: the documented way for an app to opt out of App Nap and timer throttling
    /// (idle system sleep stays allowed).
    /// </summary>
    private static void BeginActivity()
    {
        if (_activityTried || !OperatingSystem.IsMacOS()) return;
        _activityTried = true;
        try
        {
            ModEntry.Log("coop: window: taking an activity token (NSProcessInfo beginActivityWithOptions:reason:)");
            // NSActivityUserInitiatedAllowingIdleSystemSleep: user initiated (0x00FFFFFF) without the idle-system-sleep bit (1 << 20).
            const ulong options = 0x00FFFFFFUL & ~(1UL << 20);
            var info = ObjcSend(objc_getClass("NSProcessInfo"), sel_registerName("processInfo"));
            var reason = ObjcSendText(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), "SpireMind bot copy playing in the background");
            if (info == IntPtr.Zero || reason == IntPtr.Zero) throw new InvalidOperationException("no NSProcessInfo or NSString");
            var token = ObjcSendActivity(info, sel_registerName("beginActivityWithOptions:reason:"), options, reason);
            if (token == IntPtr.Zero) throw new InvalidOperationException("no token came back");
            // The token comes back autoreleased; the activity lasts only while something holds it.
            _activity = ObjcSend(token, sel_registerName("retain"));
            ModEntry.Log($"coop: window: activity token held ({(_activity != IntPtr.Zero ? "ok" : "null")})");
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: window: taking an activity token failed: {ex.Message}");
        }
    }

    private const string Objc = "/usr/lib/libobjc.A.dylib";

    [DllImport(Objc)]
    private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Objc)]
    private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcSend(IntPtr receiver, IntPtr selector);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern void ObjcSendVoid(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcSendText(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcSendActivity(IntPtr receiver, IntPtr selector, ulong options, IntPtr reason);
}
