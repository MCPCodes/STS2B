using System.Collections.Concurrent;
using System.Text;

namespace SpireMind.Game;

/// <summary>
/// The in-game chat box (docs/chat.md): a short log at the bottom left and an input line that
/// opens on a hotkey. Everything is built from stock Godot nodes owned by this plain class. A
/// mod assembly loaded at runtime gets no script registration, so _Process or _Input overrides
/// on our own Node subclasses might never be called. Per-frame work happens in
/// <see cref="Tick"/>, which ModEntry calls from SceneTree.ProcessFrame, and signals connect
/// through the stock nodes' C# events.
///
/// Never touches game types. Everything runs on the main thread except the
/// <see cref="ChatHub.LineAdded"/> handler, which only queues the line for the next tick.
/// </summary>
internal sealed class ChatOverlay
{
    private const int MaxShown = 8;
    private const float Width = 520;
    private const float MarginLeft = 24;
    private const float MarginBottom = 24;
    private const int FontSize = 20;
    private const float BackgroundAlpha = 0.55f;
    private const int MaxInputLength = 300;

    /// <summary>How long a line stays up while the box is closed; it fades over the last <see cref="FadeTime"/>.</summary>
    private static readonly TimeSpan LineLife = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(2);

    private static readonly Dictionary<string, Color> CharacterColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ironclad"] = Color.FromHtml("#e05a47"),
        ["Silent"] = Color.FromHtml("#6fbf73"),
        ["Defect"] = Color.FromHtml("#5aa9e6"),
        ["Regent"] = Color.FromHtml("#e6c15a"),
        ["Necrobinder"] = Color.FromHtml("#b07ce6"),
    };
    private static readonly Color BotTextColor = Colors.White;
    private static readonly Color HumanColor = Color.FromHtml("#d8d2c8");
    private static readonly Color SystemColor = Color.FromHtml("#9a9a9a");

    /// <summary>
    /// True while the chat box is open and has keyboard focus. Whether the game's own hotkeys
    /// (number keys, end turn) still fire while typing depends on how the game reads input; this
    /// is the seam a Harmony prefix on those handlers can check to swallow them.
    /// </summary>
    public static bool IsTyping { get; private set; }

    /// <summary>The key that opens the box: T, or the Godot Key named by SPIREMIND_CHAT_KEY.</summary>
    public Key Hotkey { get; }

    private readonly ChatHub _hub;
    private readonly Func<string?> _botName;
    private readonly string _humanName;
    private readonly Action<string>? _showBubble;
    private readonly Action<string>? _logError;

    /// <summary>Lines raised by the hub on any thread, waiting for the next <see cref="Tick"/>.</summary>
    private readonly ConcurrentQueue<ChatLine> _incoming = new();

    /// <summary>The last <see cref="MaxShown"/> lines in Seq order, each with when it reached the overlay.</summary>
    private readonly List<(ChatLine Line, DateTime Arrived)> _lines = new();

    private readonly CanvasLayer _layer;
    private readonly PanelContainer _logPanel;
    private readonly StyleBoxFlat _logBackground;
    private readonly RichTextLabel _log;
    private readonly LineEdit _input;

    private bool _open;
    private string _draft = "";
    private bool _hotkeyWasDown = true; // a key held since before the first frame isn't a press
    private bool _mouseWasDown;
    private bool _dirty = true;
    private int _renderedKey;
    private bool _tickFailed;

    /// <param name="hub">Where lines come from and typed messages go.</param>
    /// <param name="botName">Fallback for the bot's name when the hub doesn't know it yet.</param>
    /// <param name="humanName">Who the typed messages are from ("Mark").</param>
    /// <param name="showBubble">Optional speech bubble over the bot's character, called on the main thread for every bot line.</param>
    /// <param name="logError">Where overlay errors go.</param>
    public ChatOverlay(ChatHub hub, Func<string?> botName, string humanName, Action<string>? showBubble = null, Action<string>? logError = null)
    {
        _hub = hub;
        _botName = botName;
        _humanName = humanName;
        _showBubble = showBubble;
        _logError = logError;
        Hotkey = ResolveHotkey();

        // Above the game's UI, and still running while the game is paused.
        _layer = new CanvasLayer { Name = "SpireMindChat", Layer = 100, ProcessMode = Node.ProcessModeEnum.Always };

        // Pinned to the bottom-left corner with a fixed width, growing upwards as lines arrive,
        // so it stays put when the window is resized.
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 4);
        column.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        column.OffsetLeft = MarginLeft;
        column.OffsetRight = MarginLeft + Width;
        column.OffsetTop = -MarginBottom;
        column.OffsetBottom = -MarginBottom;
        column.GrowHorizontal = Control.GrowDirection.End;
        column.GrowVertical = Control.GrowDirection.Begin;
        _layer.AddChild(column);

        _logBackground = new StyleBoxFlat { BgColor = new Color(0, 0, 0, BackgroundAlpha) };
        _logBackground.SetCornerRadiusAll(6);
        SetPadding(_logBackground, 10, 6);
        _logPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _logPanel.AddThemeStyleboxOverride("panel", _logBackground);
        column.AddChild(_logPanel);

        _log = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SelectionEnabled = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
        };
        _log.AddThemeFontSizeOverride("normal_font_size", FontSize);
        _log.AddThemeFontSizeOverride("italics_font_size", FontSize);
        _log.AddThemeColorOverride("default_color", Colors.White);
        _logPanel.AddChild(_log);

        var inputStyle = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.75f),
            BorderColor = new Color(1, 1, 1, 0.3f),
        };
        inputStyle.SetBorderWidthAll(1);
        inputStyle.SetCornerRadiusAll(6);
        SetPadding(inputStyle, 10, 6);

        // Hidden, unfocusable and transparent to the mouse until opened.
        _input = new LineEdit
        {
            Visible = false,
            MaxLength = MaxInputLength,
            ContextMenuEnabled = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
        };
        _input.AddThemeStyleboxOverride("normal", inputStyle);
        _input.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _input.AddThemeFontSizeOverride("font_size", FontSize);
        _input.AddThemeColorOverride("font_color", Colors.White);
        _input.AddThemeColorOverride("font_placeholder_color", new Color(1, 1, 1, 0.45f));
        _input.AddThemeColorOverride("caret_color", Colors.White);
        _input.TextSubmitted += OnSubmitted;
        _input.GuiInput += OnInputGui;
        _input.FocusEntered += () => IsTyping = _open;
        _input.FocusExited += () => Close(keepDraft: true);
        column.AddChild(_input);

        _hub.LineAdded += line => _incoming.Enqueue(line);
    }

    /// <summary>
    /// Adds the overlay under <paramref name="root"/> (the SceneTree's root window) so it
    /// outlives scene changes. Deferred, because the root may be busy setting up its own
    /// children while mods initialize.
    /// </summary>
    public void Attach(Node root) => root.CallDeferred(Node.MethodName.AddChild, _layer);

    /// <summary>Once per frame on the main thread: new lines, the hotkey, click-away, fading.</summary>
    public void Tick()
    {
        try
        {
            if (!GodotObject.IsInstanceValid(_layer) || !_layer.IsInsideTree()) return;
            var now = DateTime.UtcNow;
            Drain(now);
            PollHotkey();
            PollClickAway();
            IsTyping = _open && _input.HasFocus();
            Render(now);
        }
        catch (Exception ex)
        {
            // Report once; a broken overlay must not flood the log every frame.
            if (_tickFailed) return;
            _tickFailed = true;
            _logError?.Invoke($"chat overlay: {ex}");
        }
    }

    // ---------- input ----------

    private void PollHotkey()
    {
        var down = Input.IsKeyPressed(Hotkey);
        var pressed = down && !_hotkeyWasDown;
        _hotkeyWasDown = down;
        if (!pressed || _open) return;

        // Leave shortcuts like Cmd+T alone, and don't steal focus from the game's own text
        // fields (seed entry, the dev console).
        if (Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Meta) || Input.IsKeyPressed(Key.Alt)) return;
        if (_layer.GetViewport()?.GuiGetFocusOwner() is LineEdit or TextEdit) return;

        Open();
    }

    /// <summary>A click anywhere outside the box closes it, even where nothing takes focus. What was typed is kept for next time.</summary>
    private void PollClickAway()
    {
        var down = Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Right);
        var clicked = down && !_mouseWasDown;
        _mouseWasDown = down;
        if (!clicked || !_open) return;

        if (!new Rect2(Vector2.Zero, _input.Size).HasPoint(_input.GetLocalMousePosition()))
            Close(keepDraft: true);
    }

    private void Open()
    {
        _open = true;
        _dirty = true;
        var bot = _hub.BotName ?? _botName();
        _input.PlaceholderText = string.IsNullOrEmpty(bot)
            ? "Say something. /help lists the commands."
            : $"Talk to {bot}. /help lists the commands.";
        _input.Text = _draft;
        _input.CaretColumn = _draft.Length;
        _draft = "";
        // Takes clicks while open. It also matters for typing: Godot versions differ on whether
        // a focused control that ignores the mouse still gets key events.
        _input.MouseFilter = Control.MouseFilterEnum.Stop;
        _input.FocusMode = Control.FocusModeEnum.All;
        _input.Visible = true;
        // Deferred, so the keystroke that opened the box isn't typed into it.
        Callable.From(() =>
        {
            if (_open) _input.GrabFocus();
        }).CallDeferred();
    }

    /// <summary>
    /// Hides the box and gives focus back to the game. Safe to call when already closed. Enter
    /// and Escape clear the text; losing focus or a click elsewhere keeps it as a draft, since
    /// that can happen mid-sentence when a screen changes under the box.
    /// </summary>
    private void Close(bool keepDraft = false)
    {
        if (!_open) return;
        // First, so the FocusExited raised by ReleaseFocus finds the box already closed.
        _open = false;
        _dirty = true;
        IsTyping = false;
        _draft = keepDraft ? _input.Text : "";
        _input.Text = "";
        if (_input.HasFocus()) _input.ReleaseFocus();
        _input.Visible = false;
        _input.MouseFilter = Control.MouseFilterEnum.Ignore;
        _input.FocusMode = Control.FocusModeEnum.None;
    }

    private void OnSubmitted(string text)
    {
        var said = text.Trim();
        if (said.Length > 0)
        {
            // The hub echoes the line (or a command's reply) back through LineAdded.
            try { _hub.PostFromHuman(_humanName, said); }
            catch (Exception ex) { _logError?.Invoke($"chat: posting failed: {ex.Message}"); }
        }
        // Like most game chats, sending closes the box; the hotkey opens it again.
        Close();
    }

    private void OnInputGui(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Keycode: Key.Escape }) return;
        // Handled here, so listeners for unhandled input (a pause menu, say) don't also get it.
        _input.AcceptEvent();
        Close();
    }

    // ---------- log ----------

    private void Drain(DateTime now)
    {
        while (_incoming.TryDequeue(out var line))
        {
            // Lines raised on two threads at once can arrive out of order; Seq is the truth.
            var at = _lines.Count;
            while (at > 0 && _lines[at - 1].Line.Seq > line.Seq) at--;
            if (at > 0 && _lines[at - 1].Line.Seq == line.Seq) continue;
            _lines.Insert(at, (line, now));
            if (_lines.Count > MaxShown) _lines.RemoveAt(0);
            _dirty = true;

            if (line.Kind == ChatLineKind.Bot && _showBubble is not null)
            {
                try { _showBubble(line.Text); }
                catch (Exception ex) { _logError?.Invoke($"chat: speech bubble failed: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// Redraws the log when what it would show has changed: a new line, the box opening or
    /// closing, a line expiring, or a fading line's alpha moving a step.
    /// </summary>
    private void Render(DateTime now)
    {
        var fingerprint = new HashCode();
        fingerprint.Add(_open);
        var strongest = 0f;
        foreach (var (line, arrived) in _lines)
        {
            var alpha = AlphaFor(now - arrived);
            fingerprint.Add(line.Seq);
            fingerprint.Add(alpha);
            strongest = Math.Max(strongest, alpha);
        }
        var key = fingerprint.ToHashCode();
        if (!_dirty && key == _renderedKey) return;
        _dirty = false;
        _renderedKey = key;

        var text = new StringBuilder();
        foreach (var (line, arrived) in _lines)
        {
            var alpha = AlphaFor(now - arrived);
            if (alpha <= 0f) continue;
            if (text.Length > 0) text.Append('\n');
            AppendLine(text, line, alpha);
        }
        _log.Text = text.ToString();

        // The backdrop fades with the newest line and goes away with it. With the box open but
        // no lines yet, the input line's own background is enough.
        _logPanel.Visible = strongest > 0f;
        _logBackground.BgColor = new Color(0, 0, 0, BackgroundAlpha * strongest);
    }

    /// <summary>1 while the box is open; otherwise full until the last two seconds of a line's life, then down to 0.</summary>
    private float AlphaFor(TimeSpan age)
    {
        if (_open) return 1f;
        var left = (float)((LineLife - age).TotalSeconds / FadeTime.TotalSeconds);
        if (left <= 0f) return 0f;
        if (left >= 1f) return 1f;
        // 5% steps, so a fade redraws the label about 20 times instead of every frame.
        return MathF.Ceiling(left * 20f) / 20f;
    }

    private static void AppendLine(StringBuilder text, ChatLine line, float alpha)
    {
        switch (line.Kind)
        {
            case ChatLineKind.Bot:
                text.Append("[color=").Append(Hex(NameColor(line.From), alpha)).Append(']')
                    .Append(Escape(line.From)).Append(":[/color] ")
                    .Append("[color=").Append(Hex(BotTextColor, alpha)).Append(']')
                    .Append(Escape(line.Text)).Append("[/color]");
                break;
            case ChatLineKind.Human:
                text.Append("[color=").Append(Hex(HumanColor, alpha)).Append(']')
                    .Append(Escape(line.From)).Append(": ").Append(Escape(line.Text)).Append("[/color]");
                break;
            default:
                text.Append("[i][color=").Append(Hex(SystemColor, alpha)).Append(']')
                    .Append(Escape(line.Text)).Append("[/color][/i]");
                break;
        }
    }

    /// <summary>The character's colour for a bot's name, white for anyone else.</summary>
    private static Color NameColor(string name)
    {
        var bare = name.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? name[4..] : name;
        return CharacterColors.TryGetValue(bare.Trim(), out var color) ? color : Colors.White;
    }

    private static string Hex(Color color, float alpha) => "#" + new Color(color, alpha).ToHtml(includeAlpha: true);

    /// <summary>Typed and spoken text is shown as is: "[" is the only character BBCode needs escaped.</summary>
    private static string Escape(string text) => text.Replace("[", "[lb]");

    private static void SetPadding(StyleBox style, float horizontal, float vertical)
    {
        style.ContentMarginLeft = horizontal;
        style.ContentMarginRight = horizontal;
        style.ContentMarginTop = vertical;
        style.ContentMarginBottom = vertical;
    }

    /// <summary>
    /// T, or the Godot Key named by SPIREMIND_CHAT_KEY ("Y", "Quoteleft"), any case. Enter and
    /// Escape fall back to T: they close the box, and the same frame's poll would open it again.
    /// </summary>
    private static Key ResolveHotkey()
    {
        var name = System.Environment.GetEnvironmentVariable("SPIREMIND_CHAT_KEY")?.Trim();
        return !string.IsNullOrEmpty(name)
               && Enum.TryParse<Key>(name, ignoreCase: true, out var key)
               && key is not (Key.None or Key.Enter or Key.KpEnter or Key.Escape)
               && Enum.IsDefined(key)
            ? key
            : Key.T;
    }
}
