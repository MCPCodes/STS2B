using System.Collections.Concurrent;
using System.Text;

namespace SpireMind.Game;

/// <summary>
/// The chat box's look as plain Godot values, so this file still compiles against GodotSharp
/// alone. <see cref="ChatTheme"/> fills it from the game's own fonts and colours; a null member
/// keeps the overlay's built-in look.
/// </summary>
/// <param name="Body">Typed and spoken text (the game's Kreon regular).</param>
/// <param name="Bold">Speaker names (Kreon bold).</param>
/// <param name="Text">Text colour (the game's cream).</param>
/// <param name="Highlight">The human's name, the caret and the box's border (the game's gold).</param>
internal sealed record ChatStyle(Font? Body, Font? Bold, Color? Text, Color? Highlight);

/// <summary>
/// What the overlay needs from the game, handed in by ModEntry as delegates so ChatOverlay never
/// touches game types. Every member is called on the main thread; any of them may throw, which
/// counts as no answer.
/// </summary>
internal sealed class ChatGameHooks
{
    /// <summary>The node the box lives under (NGame), or null while the game isn't ready for it.</summary>
    public required Func<Node?> Parent { get; init; }

    /// <summary>Adds the box under <see cref="Parent"/>. Defaults to a deferred add_child.</summary>
    public Action<Node, Node>? AddChild { get; init; }

    /// <summary>The game's fonts and colours, read once when the box is first attached.</summary>
    public Func<ChatStyle?>? Style { get; init; }

    /// <summary>The name colour of the character the bot plays, or null when unknown.</summary>
    public Func<Color?>? BotColor { get; init; }

    /// <summary>True while the game is in controller mode. The box is keyboard-only and closes then.</summary>
    public Func<bool>? UsingController { get; init; }

    /// <summary>A speech bubble over the bot's character, called for every bot line.</summary>
    public Action<string>? ShowBubble { get; init; }
}

/// <summary>
/// The in-game chat box (docs/chat.md, docs/chat-and-persona.md): a short log at the bottom
/// left and an input line that opens on a hotkey. Everything is built from stock Godot nodes
/// owned by this plain class. A mod assembly loaded at runtime gets no script registration, so
/// _Process or _Input overrides on our own Node subclasses might never be called. Per-frame work
/// happens in <see cref="Tick"/>, which ModEntry calls from SceneTree.ProcessFrame, and signals
/// connect through the stock nodes' C# events.
///
/// Keys reach it two ways. Normally a Harmony prefix on the game's input manager hands over
/// every key the GUI didn't use (<see cref="TakeUnhandledKey"/>): it opens the box and, while
/// the box is open, keeps those keys from the game. If that patch didn't apply, the hotkey is
/// polled on the frame tick instead (<see cref="KeysFromPatch"/> false).
///
/// Deliberately not a game screen: it never registers with the game's screen context, modal
/// container or overlay stack, whose "is current" checks would disable the combat UI and
/// confuse SpireMind's own screen router. The bot keeps playing while the box is open.
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
    private const int OutlineSize = 4;
    private const float BackgroundAlpha = 0.55f;
    private const int MaxInputLength = 300;

    /// <summary>Game UI draws on canvas layer 0 and the dev console on 10: above the one, under the other.</summary>
    private const int CanvasLayerIndex = 6;

    /// <summary>How long a line stays up while the box is closed; it fades over the last <see cref="FadeTime"/>.</summary>
    private static readonly TimeSpan LineLife = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(2);

    /// <summary>How often to retry parenting the box while the game isn't ready, or a deferred add is still pending.</summary>
    private static readonly TimeSpan AttachRetry = TimeSpan.FromSeconds(1);

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
    /// Fake italics for system lines once the log uses the game's Kreon, which comes as a
    /// regular and a bold font only. The slant Godot's FontVariation docs give.
    /// </summary>
    private static readonly Transform2D Slant = new(1f, 0.2f, 0f, 1f, 0f, 0f);

    /// <summary>
    /// True while the chat box is open. The input patches check it to keep keys from the game,
    /// even in the moment after a game screen took focus away and before the next key takes it
    /// back.
    /// </summary>
    public static bool IsTyping { get; private set; }

    /// <summary>The key that opens the box: T, or the Godot Key named by SPIREMIND_CHAT_KEY.</summary>
    public Key Hotkey { get; }

    /// <summary>
    /// True once the Harmony prefix on the game's input manager is known to be in place: keys
    /// then come through <see cref="TakeUnhandledKey"/> and the hotkey isn't polled.
    /// </summary>
    public bool KeysFromPatch { get; set; }

    private readonly ChatHub _hub;
    private readonly Func<string?> _botName;
    private readonly string _humanName;
    private readonly ChatGameHooks _game;
    private readonly Action<string>? _logError;

    /// <summary>Lines raised by the hub on any thread, waiting for the next <see cref="Tick"/>.</summary>
    private readonly ConcurrentQueue<ChatLine> _incoming = new();

    /// <summary>The last <see cref="MaxShown"/> lines in Seq order, each with when it reached the overlay.</summary>
    private readonly List<(ChatLine Line, DateTime Arrived)> _lines = new();

    private readonly CanvasLayer _layer;
    private readonly PanelContainer _logPanel;
    private readonly StyleBoxFlat _logBackground;
    private readonly RichTextLabel _log;
    private readonly StyleBoxFlat _inputStyle;
    private readonly LineEdit _input;

    private Color _botTextColor = BotTextColor;
    private Color _humanNameColor = HumanColor;
    private Color _humanTextColor = HumanColor;

    private bool _open;
    private string _draft = "";
    private string _lastSent = "";
    private bool _hotkeyWasDown = true; // a key held since before the first frame isn't a press
    private bool _mouseWasDown;

    /// <summary>
    /// Keys pressed while the box was open, by when. Game buttons click on a hotkey's release,
    /// and the release of the Enter or Escape that closed the box (or of a letter still held)
    /// would otherwise reach the game as a bare release: Enter is select, E is accept.
    /// </summary>
    private readonly Dictionary<Key, DateTime> _pressedWhileTyping = new();

    /// <summary>A release later than this after its press belongs to a fresh press the game saw.</summary>
    private static readonly TimeSpan ReleaseGuard = TimeSpan.FromSeconds(3);

    private bool _dirty = true;
    private int _renderedKey;
    private DateTime _nextAttach;
    private bool _styled;
    private bool _windowHooked;
    private bool _tickFailed;
    private bool _hookFailed;

    /// <param name="hub">Where lines come from and typed messages go.</param>
    /// <param name="botName">Fallback for the bot's name when the hub doesn't know it yet.</param>
    /// <param name="humanName">Who the typed messages are from ("Mark").</param>
    /// <param name="game">Where the box lives and what it borrows from the game.</param>
    /// <param name="logError">Where overlay errors go.</param>
    public ChatOverlay(ChatHub hub, Func<string?> botName, string humanName, ChatGameHooks game, Action<string>? logError = null)
    {
        _hub = hub;
        _botName = botName;
        _humanName = humanName;
        _game = game;
        _logError = logError;
        Hotkey = ResolveHotkey();

        // Above the game's UI, under its dev console, and still running while the game is paused.
        _layer = new CanvasLayer { Name = "SpireMindChat", Layer = CanvasLayerIndex, ProcessMode = Node.ProcessModeEnum.Always };

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
        _log.AddThemeFontSizeOverride("bold_font_size", FontSize);
        _log.AddThemeFontSizeOverride("italics_font_size", FontSize);
        _log.AddThemeColorOverride("default_color", Colors.White);
        // A black outline, as the game's own labels have.
        _log.AddThemeConstantOverride("outline_size", OutlineSize);
        _log.AddThemeColorOverride("font_outline_color", Colors.Black);
        _logPanel.AddChild(_log);

        _inputStyle = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.75f),
            BorderColor = new Color(1, 1, 1, 0.3f),
        };
        _inputStyle.SetBorderWidthAll(1);
        _inputStyle.SetCornerRadiusAll(6);
        SetPadding(_inputStyle, 10, 6);

        // Hidden, unfocusable and transparent to the mouse until opened. It stays in edit mode
        // across Enter: the game's hotkey manager stands down only while the focused LineEdit
        // is editing.
        _input = new LineEdit
        {
            Visible = false,
            MaxLength = MaxInputLength,
            ContextMenuEnabled = false,
            KeepEditingOnTextSubmit = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
        };
        _input.AddThemeStyleboxOverride("normal", _inputStyle);
        _input.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _input.AddThemeFontSizeOverride("font_size", FontSize);
        _input.AddThemeColorOverride("font_color", Colors.White);
        _input.AddThemeColorOverride("font_placeholder_color", new Color(1, 1, 1, 0.45f));
        _input.AddThemeColorOverride("caret_color", Colors.White);
        _input.AddThemeConstantOverride("outline_size", OutlineSize);
        _input.AddThemeColorOverride("font_outline_color", Colors.Black);
        _input.TextSubmitted += OnSubmitted;
        _input.GuiInput += OnInputGui;
        // No FocusExited handler: the game takes focus away under an open box (the deck
        // upgrade, transform, enchant and remove screens release it, and the bot's own picks
        // open those), and OnWindowInput takes it back before the next key.
        column.AddChild(_input);

        _hub.LineAdded += line => _incoming.Enqueue(line);
    }

    /// <summary>Once per frame on the main thread: attaching, new lines, the hotkey, click-away, fading.</summary>
    public void Tick()
    {
        try
        {
            if (!GodotObject.IsInstanceValid(_layer)) return;
            var now = DateTime.UtcNow;
            if (!_layer.IsInsideTree())
            {
                TryAttach(now);
                return;
            }
            Drain(now);
            if (!KeysFromPatch) PollHotkey();
            PollClickAway();
            if (_open && Ask(_game.UsingController)) Close(keepDraft: true);
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

    // ---------- attaching ----------

    /// <summary>
    /// Parents the box under the game's root node once that node is ready. Not from the
    /// constructor: mods initialize inside the game's own _EnterTree, too early to add children
    /// there. The game's root node outlives the menu, runs and new runs, so this normally
    /// happens once. Throttled, so a deferred add has time to land before anything is retried.
    /// </summary>
    private void TryAttach(DateTime now)
    {
        if (now < _nextAttach || _layer.GetParent() is not null) return;
        _nextAttach = now + AttachRetry;

        var parent = Ask(_game.Parent);
        if (parent is null || !GodotObject.IsInstanceValid(parent) || !parent.IsInsideTree() || !parent.IsNodeReady()) return;

        if (!_styled)
        {
            _styled = true;
            if (Ask(_game.Style) is { } style) ApplyStyle(style);
        }
        if (!_windowHooked)
        {
            _windowHooked = true;
            parent.GetTree().Root.WindowInput += OnWindowInput;
        }

        if (_game.AddChild is { } add)
        {
            try
            {
                add(parent, _layer);
                return;
            }
            catch (Exception ex)
            {
                ReportHookFailure($"chat: the game's add failed, adding the box deferred: {ex.Message}");
            }
        }
        parent.CallDeferred(Node.MethodName.AddChild, _layer);
    }

    /// <summary>The game's fonts and colours over the built-in look; anything null stays as it was.</summary>
    private void ApplyStyle(ChatStyle style)
    {
        if (style.Body is { } body)
        {
            _log.AddThemeFontOverride("normal_font", body);
            _log.AddThemeFontOverride("italics_font", new FontVariation { BaseFont = body, VariationTransform = Slant });
            _input.AddThemeFontOverride("font", body);
        }
        if (style.Bold is { } bold) _log.AddThemeFontOverride("bold_font", bold);
        if (style.Text is { } text)
        {
            _botTextColor = text;
            _humanTextColor = text;
            _input.AddThemeColorOverride("font_color", text);
            _input.AddThemeColorOverride("font_placeholder_color", new Color(text, 0.45f));
        }
        if (style.Highlight is { } highlight)
        {
            _humanNameColor = highlight;
            _input.AddThemeColorOverride("caret_color", highlight);
            _inputStyle.BorderColor = new Color(highlight, 0.6f);
        }
        _dirty = true;
    }

    // ---------- input ----------

    /// <summary>
    /// A key event the GUI didn't use, from the Harmony prefix on the game's input manager.
    /// Returns true when the overlay takes it; the caller then marks it handled and skips the
    /// game's handler. While the box is open that is every key: Cmd or Ctrl plus a digit
    /// (which picks cards whatever has focus), F-keys and key releases. While closed, only a
    /// fresh press of the hotkey with no modifiers, which opens the box and so isn't typed.
    /// </summary>
    public bool TakeUnhandledKey(InputEvent e)
    {
        if (!GodotObject.IsInstanceValid(_layer) || !_layer.IsInsideTree()) return false;
        if (TakeStrayRelease(e)) return true;
        if (_open)
        {
            NotePress(e);
            // Escape normally never gets here (OnInputGui takes it). It does if the box lost
            // focus and couldn't get it back, and must still close it.
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) Close();
            return true;
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } key || key.Keycode != Hotkey) return false;
        if (key.CtrlPressed || key.MetaPressed || key.AltPressed || key.ShiftPressed || !CanOpen()) return false;
        Open(deferFocus: false);
        return true;
    }

    /// <summary>Remembers a key pressed while typing, so its release can be kept from the game.</summary>
    private void NotePress(InputEvent e)
    {
        if (_open && e is InputEventKey { Pressed: true, Echo: false } key)
            _pressedWhileTyping[key.Keycode] = DateTime.UtcNow;
    }

    /// <summary>The release of a key pressed while typing, arriving after the box closed: swallow it.</summary>
    private bool TakeStrayRelease(InputEvent e)
    {
        if (_open || e is not InputEventKey { Pressed: false } key) return false;
        if (!_pressedWhileTyping.Remove(key.Keycode, out var at)) return false;
        return DateTime.UtcNow - at < ReleaseGuard;
    }

    /// <summary>The fallback when the input patch didn't apply: the hotkey is polled, edge-triggered.</summary>
    private void PollHotkey()
    {
        var down = Input.IsKeyPressed(Hotkey);
        var pressed = down && !_hotkeyWasDown;
        _hotkeyWasDown = down;
        if (!pressed || _open) return;

        // Leave shortcuts like Cmd+T alone.
        if (Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Meta) || Input.IsKeyPressed(Key.Alt) || Input.IsKeyPressed(Key.Shift)) return;
        if (!CanOpen()) return;

        // Deferred, so the keystroke that opened the box isn't typed into it.
        Open(deferFocus: true);
    }

    /// <summary>Not while another text field has focus (seed entry, the game's console).</summary>
    private bool CanOpen() => _layer.GetViewport()?.GuiGetFocusOwner() is not (LineEdit or TextEdit);

    /// <summary>
    /// Every input event the window receives, before the game sees it. While the box is open a
    /// key takes focus back first, since game screens release focus under it, and goes back
    /// into edit mode, so the game's hotkey manager keeps standing down.
    /// </summary>
    private void OnWindowInput(InputEvent e)
    {
        try
        {
            if (!_open || e is not InputEventKey || !_input.IsInsideTree()) return;
            // The window sees every key before the GUI does, including the Enter or Escape
            // that closes the box, which the box then consumes.
            NotePress(e);
            if (!_input.HasFocus()) _input.GrabFocus();
            if (!_input.IsEditing()) _input.Edit();
        }
        catch (Exception ex)
        {
            ReportHookFailure($"chat: taking focus back failed: {ex.Message}");
        }
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

    private void Open(bool deferFocus)
    {
        _pressedWhileTyping.Clear();
        _open = true;
        IsTyping = true;
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
        if (deferFocus) Callable.From(Focus).CallDeferred();
        else Focus();
    }

    /// <summary>Focus alone isn't enough: the game's hotkey manager checks that the focused LineEdit is editing.</summary>
    private void Focus()
    {
        if (!_open) return;
        _input.GrabFocus();
        _input.Edit();
    }

    /// <summary>
    /// Hides the box and gives focus back to the game. Safe to call when already closed. Enter
    /// and Escape clear the text; a click elsewhere or a switch to the controller keeps it as
    /// a draft.
    /// </summary>
    private void Close(bool keepDraft = false)
    {
        if (!_open) return;
        _open = false;
        IsTyping = false;
        _dirty = true;
        _draft = keepDraft ? _input.Text : "";
        _input.Text = "";
        if (_input.IsEditing()) _input.Unedit();
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
            _lastSent = said;
            // The hub echoes the line (or a command's reply) back through LineAdded.
            try { _hub.PostFromHuman(_humanName, said); }
            catch (Exception ex) { _logError?.Invoke($"chat: posting failed: {ex.Message}"); }
        }
        // Like most game chats, sending closes the box; the hotkey opens it again.
        Close();
    }

    /// <summary>
    /// Keys the LineEdit doesn't use itself. Accepted here, before the LineEdit and the game see
    /// them: Escape would reach the pause menu, and Up, Down and Tab would move focus to the
    /// game's controls.
    /// </summary>
    private void OnInputGui(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key) return;
        switch (key.Keycode)
        {
            case Key.Escape:
                _input.AcceptEvent();
                Close();
                break;
            case Key.Up:
                _input.AcceptEvent();
                _input.Text = _lastSent;
                _input.CaretColumn = _lastSent.Length;
                break;
            case Key.Down:
                _input.AcceptEvent();
                _input.Text = "";
                break;
            case Key.Tab:
                _input.AcceptEvent();
                break;
        }
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

            if (line.Kind == ChatLineKind.Bot && _game.ShowBubble is { } showBubble)
            {
                try { showBubble(line.Text); }
                catch (Exception ex) { _logError?.Invoke($"chat: speech bubble failed: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// Redraws the log when what it would show has changed: a new line, the box opening or
    /// closing, a line expiring, a fading line's alpha moving a step, or the bot's colour.
    /// </summary>
    private void Render(DateTime now)
    {
        var botColor = Ask(_game.BotColor);
        var current = _hub.BotName;
        var fingerprint = new HashCode();
        fingerprint.Add(_open);
        fingerprint.Add(botColor);
        fingerprint.Add(current);
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
            AppendLine(text, line, alpha, botColor, current);
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

    private void AppendLine(StringBuilder text, ChatLine line, float alpha, Color? botColor, string? current)
    {
        switch (line.Kind)
        {
            case ChatLineKind.Bot:
                AppendName(text, line.From, BotNameColor(line.From, botColor, current), alpha);
                AppendText(text, line.Text, _botTextColor, alpha);
                break;
            case ChatLineKind.Human:
                AppendName(text, line.From, _humanNameColor, alpha);
                AppendText(text, line.Text, _humanTextColor, alpha);
                break;
            default:
                text.Append("[i]");
                AppendText(text, line.Text, SystemColor, alpha);
                text.Append("[/i]");
                break;
        }
    }

    /// <summary>"Name: " in bold, the game's Kreon bold once the style is in.</summary>
    private static void AppendName(StringBuilder text, string name, Color color, float alpha) =>
        text.Append("[b][color=").Append(Hex(color, alpha)).Append(']')
            .Append(Escape(name)).Append(":[/color][/b] ");

    private static void AppendText(StringBuilder text, string said, Color color, float alpha) =>
        text.Append("[color=").Append(Hex(color, alpha)).Append(']')
            .Append(Escape(said)).Append("[/color]");

    /// <summary>
    /// A bot's name colour: the game's own colour for the character the bot plays now, else the
    /// built-in colour for that character's name, else white. Between runs the bot has no name
    /// and speaks as a name that is no character's, which counts as the current one.
    /// </summary>
    private static Color BotNameColor(string name, Color? botColor, string? current)
    {
        var bare = Bare(name);
        var isCurrent = current is null
            ? !CharacterColors.ContainsKey(bare)
            : string.Equals(bare, Bare(current), StringComparison.OrdinalIgnoreCase);
        if (isCurrent && botColor is { } game) return game;
        return CharacterColors.TryGetValue(bare, out var color) ? color : Colors.White;
    }

    private static string Bare(string name) =>
        (name.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? name[4..] : name).Trim();

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

    // ---------- game hooks ----------

    /// <summary>Calls a game hook; a missing hook or a failure counts as no answer (the first failure is logged).</summary>
    private T? Ask<T>(Func<T>? hook)
    {
        if (hook is null) return default;
        try { return hook(); }
        catch (Exception ex)
        {
            ReportHookFailure($"chat: game hook failed: {ex.Message}");
            return default;
        }
    }

    private void ReportHookFailure(string message)
    {
        if (_hookFailed) return;
        _hookFailed = true;
        _logError?.Invoke(message);
    }

    /// <summary>
    /// T, or the Godot Key named by SPIREMIND_CHAT_KEY ("Y", "Quoteleft"), any case. Enter and
    /// Escape fall back to T: they close the box, and the same key would open it again.
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
