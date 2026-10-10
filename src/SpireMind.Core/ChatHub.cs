using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpireMind.Core;

public enum ChatLineKind { Human, Bot, System }

/// <summary>One line of the chat log as the overlay shows it. System lines have an empty From.</summary>
public sealed record ChatLine(long Seq, ChatLineKind Kind, string From, string Text, DateTime At);

/// <summary>A human message addressed to this bot that the model hasn't read yet.</summary>
public sealed record ChatMessage(string From, string Text, DateTime At);

public enum PostOutcome { Queued, Command, NotForMe, Empty }

/// <summary>What <see cref="ChatHub.PostFromHuman"/> did. Reply is the system line a command produced.</summary>
public sealed record PostResult(PostOutcome Outcome, string? Reply = null);

/// <summary>
/// The in-game chat between the human and the bot: the line log the overlay shows, the
/// messages the model hasn't read yet, the pause switch, the chat commands, who the bot is
/// playing and whether it is deciding. The overlay posts on the main thread and the tools read
/// on HTTP worker threads, so one lock guards everything, and <see cref="LineAdded"/> and
/// command handlers run only after it is released. See docs/chat.md.
/// </summary>
public sealed partial class ChatHub
{
    private const int MaxLines = 200;
    private const int BurstLines = 6;
    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(10);
    private const int MaxFailureLength = 160;
    private static readonly string[] BuiltIns = ["pause", "resume", "help"];

    private readonly object _gate = new();
    private readonly Func<DateTime> _clock;
    private readonly List<ChatLine> _lines = [];
    private readonly List<ChatMessage> _unread = [];
    private readonly Queue<DateTime> _recentSays = new();
    private readonly List<Command> _commands = [];
    private string? _botName;
    private string? _pausedBy;
    private JsonObject? _persona;
    private DateTime? _thinkingSince;
    private DateTime _speechHoldUntil = DateTime.MinValue;
    private long _seq;
    private long _stops;

    /// <param name="clock">Time source for tests; defaults to <see cref="DateTime.UtcNow"/>.</param>
    public ChatHub(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.UtcNow);

    /// <summary>The character this bot plays, without a leading "The " ("Ironclad"). Null until
    /// the game side knows it (and between runs), and then every message is for this bot. The
    /// setter strips "The " so callers can pass the game's title.</summary>
    public string? BotName
    {
        get { lock (_gate) return _botName; }
        set
        {
            var name = value?.Trim();
            if (name is not null && name.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                name = name[4..].TrimStart();
            lock (_gate) _botName = string.IsNullOrEmpty(name) ? null : name;
        }
    }

    /// <summary>Every character name, so a message for another bot can be told apart from an
    /// unaddressed one.</summary>
    public IReadOnlyList<string> KnownNames { get; init; } = ["Ironclad", "Silent", "Defect", "Regent", "Necrobinder"];

    public int MaxSayLength { get; init; } = 280;

    /// <summary>The character this bot plays as the game describes it (name, title, pronouns,
    /// tone, some of its own lines), for get_persona. The game side replaces it whole when the
    /// character changes and sets it to null when there is none. Both ways it is a deep copy,
    /// so neither side can change the other's.</summary>
    public JsonObject? Persona
    {
        get { lock (_gate) return _persona?.DeepClone().AsObject(); }
        set
        {
            var copy = value?.DeepClone().AsObject();
            lock (_gate) _persona = copy;
        }
    }

    /// <summary>When (UTC) the bot was handed a decision it hasn't acted on yet, or null. The
    /// speech bubbles show a thought cloud from it. BotTools keeps it through
    /// <see cref="MarkThinking"/>, and /pause clears it. A mark older than
    /// <see cref="ThinkingStaleAfter"/> reads as null: the session ended or the model stopped
    /// mid-decision, and nothing else would ever clear it.</summary>
    public DateTime? ThinkingSince
    {
        get { lock (_gate) return FreshThinking(); }
    }

    /// <summary>How long a decision can go unanswered before it no longer counts as being
    /// decided. Above BotTools' longest wait, since a model that hasn't called a tool in that
    /// long isn't deciding.</summary>
    public TimeSpan ThinkingStaleAfter { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The shortest and longest a spoken line holds back the bot's next move off
    /// combat, so its bubble can be read before the screen changes. See <see cref="SpeechHold"/>.</summary>
    public static readonly TimeSpan MinSpeechHold = TimeSpan.FromSeconds(1.5);

    /// <inheritdoc cref="MinSpeechHold"/>
    public static readonly TimeSpan MaxSpeechHold = TimeSpan.FromSeconds(3);

    public bool Paused
    {
        get { lock (_gate) return _pausedBy is not null; }
    }

    /// <summary>Who typed /pause, or null when the bot isn't paused.</summary>
    public string? PausedBy
    {
        get { lock (_gate) return _pausedBy; }
    }

    public bool HasUnread
    {
        get { lock (_gate) return _unread.Count > 0; }
    }

    public long LastSeq
    {
        get { lock (_gate) return _seq; }
    }

    /// <summary>How many times the bot was told to stop (/stop). A tool call that may outlive its
    /// client (Claude Code told to quit in the middle of an act_sequence, or a wait_for_decision
    /// it left behind) remembers the count when it starts and ends early once it has moved.</summary>
    public long Stops
    {
        get { lock (_gate) return _stops; }
    }

    /// <summary>Tells running tool calls that the bot was stopped; see <see cref="Stops"/>.</summary>
    public void NoteStop()
    {
        lock (_gate) _stops++;
    }

    /// <summary>Raised for every new line after the lock is released, on the thread that added
    /// it. Lines added from two threads at once may arrive out of order; Seq is the truth.</summary>
    public event Action<ChatLine>? LineAdded;

    /// <summary>
    /// A line typed by a human. A message whose first word is "/name" (any case) is a command,
    /// handled here at once and answered with a system line: /pause, /resume and /help are
    /// built in (anything after them is ignored), the rest come from <see cref="AddCommand"/>,
    /// and an unknown one gets a line pointing at /help. Commands are never queued for the
    /// model. Anything else is logged, and queued for the model unless it starts with another
    /// character's name.
    /// </summary>
    public PostResult PostFromHuman(string from, string text)
    {
        var who = string.IsNullOrWhiteSpace(from) ? "Player" : from.Trim();
        var said = text?.Trim() ?? "";
        if (said.Length == 0)
            return new PostResult(PostOutcome.Empty);

        if (SplitCommand(said) is { } command)
            return RunCommand(who, command.Name, command.Args);

        ChatLine line;
        PostResult result;
        lock (_gate)
        {
            line = AddLine(ChatLineKind.Human, who, said);
            var forMe = IsForThisBot(said);
            if (forMe) _unread.Add(new ChatMessage(who, said, line.At));
            result = new PostResult(forMe ? PostOutcome.Queued : PostOutcome.NotForMe);
        }
        LineAdded?.Invoke(line);
        return result;
    }

    /// <summary>
    /// Adds a chat command, or replaces the one with the same name. <paramref name="name"/> is
    /// the word after the slash ("model"; a leading slash is dropped), matched in any case.
    /// <paramref name="usage"/> is the argument hint /help shows after the name ("[low|high]",
    /// or empty), and <paramref name="help"/> a short phrase saying what it does ("shows or
    /// sets the effort"). The built-in /pause, /resume and /help can't be replaced.
    /// <para><paramref name="handler"/> gets who typed it and the rest of the message, trimmed,
    /// and returns the system line to show (empty for none). It runs on the posting thread,
    /// often Godot's main thread, outside the hub's lock, so it may use the hub but must return
    /// quickly and never wait for the main thread. If it throws, a short system line says so.</para>
    /// </summary>
    public void AddCommand(string name, string usage, string help, Func<string, string, string> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var bare = (name ?? "").Trim();
        if (bare.StartsWith('/')) bare = bare[1..];
        if (bare.Length == 0 || bare.Any(char.IsWhiteSpace) || !char.IsLetterOrDigit(bare[0]))
            throw new ArgumentException($"'{name}' can't be a command name: use one word that starts with a letter or digit.", nameof(name));
        if (BuiltIns.Contains(bare, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"/{bare} is built in and can't be replaced.", nameof(name));

        var command = new Command(bare, usage?.Trim() ?? "", help?.Trim() ?? "", handler);
        lock (_gate)
        {
            var index = _commands.FindIndex(c => c.Name.Equals(bare, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) _commands[index] = command;
            else _commands.Add(command);
        }
    }

    /// <summary>
    /// A system line from the mod itself ("Switching to your Claude subscription..."), shown in
    /// the log like a command's reply. Whitespace is collapsed; there is no length cap or burst
    /// limit. Throws <see cref="ArgumentException"/> on empty text.
    /// </summary>
    public ChatLine Announce(string text)
    {
        var said = Whitespace().Replace(text ?? "", " ").Trim();
        if (said.Length == 0)
            throw new ArgumentException("Nothing to announce: text is empty.");

        ChatLine line;
        lock (_gate) line = AddLine(ChatLineKind.System, "", said);
        LineAdded?.Invoke(line);
        return line;
    }

    /// <summary>
    /// The bot speaks. Whitespace is collapsed and long text cut to <see cref="MaxSayLength"/>
    /// with "...". The line holds back the next move off combat for <see cref="SpeechHold"/>
    /// (see <see cref="SpeechHoldLeft"/>). Throws <see cref="ArgumentException"/> on empty text
    /// and <see cref="InvalidOperationException"/> past the burst limit, so the model slows down.
    /// </summary>
    public ChatLine Say(string text)
    {
        var said = Whitespace().Replace(text ?? "", " ").Trim();
        if (said.Length == 0)
            throw new ArgumentException("Nothing to say: text is empty.");
        if (said.Length > MaxSayLength)
        {
            var cut = Math.Max(0, MaxSayLength - 3);
            if (cut > 0 && char.IsHighSurrogate(said[cut - 1])) cut--;
            said = said[..cut].TrimEnd() + "...";
        }

        ChatLine line;
        lock (_gate)
        {
            var now = _clock();
            while (_recentSays.Count > 0 && now - _recentSays.Peek() >= BurstWindow)
                _recentSays.Dequeue();
            if (_recentSays.Count >= BurstLines)
                throw new InvalidOperationException(
                    $"Too many chat lines: at most {BurstLines} in {BurstWindow.TotalSeconds:0} seconds. Slow down: wait a little and keep it brief.");
            _recentSays.Enqueue(now);
            line = AddLine(ChatLineKind.Bot, _botName ?? "Bot", said);
            var holdUntil = now + SpeechHold(said);
            if (holdUntil > _speechHoldUntil) _speechHoldUntil = holdUntil;
        }
        LineAdded?.Invoke(line);
        return line;
    }

    /// <summary>
    /// How long a spoken line holds back the bot's next move off combat: half a second plus
    /// 0.05 s per character that isn't whitespace, between <see cref="MinSpeechHold"/> and
    /// <see cref="MaxSpeechHold"/>. The bot often says something and moves on in the same
    /// breath, and outside a fight the move changes the room under the bubble.
    /// </summary>
    public static TimeSpan SpeechHold(string text)
    {
        var letters = (text ?? "").Count(c => !char.IsWhiteSpace(c));
        var hold = TimeSpan.FromMilliseconds(500 + 50 * letters);
        return hold < MinSpeechHold ? MinSpeechHold : hold > MaxSpeechHold ? MaxSpeechHold : hold;
    }

    /// <summary>What is left of the hold set by the bot's spoken lines (<see cref="SpeechHold"/>
    /// from each), or zero. BotTools waits it out before an action off combat.</summary>
    public TimeSpan SpeechHoldLeft()
    {
        lock (_gate)
        {
            var left = _speechHoldUntil - _clock();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// True when a tool result hands the model a decision. An earlier time is kept, so a
    /// get_state after wait_for_decision doesn't restart the clock, unless it has gone stale.
    /// False once it acts, while it is paused, and when a result has nothing to decide.
    /// </summary>
    public void MarkThinking(bool thinking)
    {
        lock (_gate) _thinkingSince = thinking ? FreshThinking() ?? _clock() : null;
    }

    /// <summary>Hands over the unread messages addressed to this bot and marks them read.</summary>
    public IReadOnlyList<ChatMessage> TakeUnread()
    {
        lock (_gate)
        {
            if (_unread.Count == 0) return [];
            var taken = _unread.ToArray();
            _unread.Clear();
            return taken;
        }
    }

    /// <summary>The unread messages without marking them read, for debugging routes that
    /// shouldn't take them from the model.</summary>
    public IReadOnlyList<ChatMessage> PeekUnread()
    {
        lock (_gate) return _unread.Count == 0 ? [] : _unread.ToArray();
    }

    /// <summary>The log after <paramref name="seq"/>, oldest first. Only the last 200 lines are kept.</summary>
    public IReadOnlyList<ChatLine> LinesSince(long seq)
    {
        lock (_gate) return _lines.Where(l => l.Seq > seq).ToArray();
    }

    // Caller holds _gate.
    private ChatLine AddLine(ChatLineKind kind, string from, string text)
    {
        var line = new ChatLine(++_seq, kind, from, text, _clock());
        _lines.Add(line);
        if (_lines.Count > MaxLines)
            _lines.RemoveRange(0, _lines.Count - MaxLines);
        return line;
    }

    // Caller holds _gate. The thinking mark, or null once it is older than ThinkingStaleAfter.
    private DateTime? FreshThinking() =>
        _thinkingSince is { } since && _clock() - since < ThinkingStaleAfter ? since : null;

    // A built-in /pause, /resume or /help, a command from AddCommand, or an unknown one. The
    // built-ins change state and log their line under one lock; an added command's handler runs
    // with the lock released, so it can use the hub (and take its time) without blocking it.
    private PostResult RunCommand(string from, string name, string args)
    {
        // Added commands can't take a built-in's name, so the order doesn't matter.
        Command? added;
        lock (_gate) added = _commands.Find(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        ChatLine line;
        if (added is null)
        {
            lock (_gate)
                line = AddLine(ChatLineKind.System, "", BuiltInReply(from, name) ?? $"There's no /{name} command. Type /help to see them.");
        }
        else
        {
            var reply = RunHandler(added, from, args);
            if (reply.Length == 0)
                return new PostResult(PostOutcome.Command);
            lock (_gate) line = AddLine(ChatLineKind.System, "", reply);
        }

        LineAdded?.Invoke(line);
        return new PostResult(PostOutcome.Command, line.Text);
    }

    // An added command's reply, trimmed, or a short line saying it failed. Never throws.
    private static string RunHandler(Command command, string from, string args)
    {
        try
        {
            return command.Handler(from, args)?.Trim() ?? "";
        }
        catch (Exception ex)
        {
            var why = Whitespace().Replace(ex.Message, " ").Trim();
            if (why.Length > MaxFailureLength)
                why = why[..(MaxFailureLength - 3)].TrimEnd() + "...";
            return why.Length > 0 ? $"That didn't work: {why}" : $"That didn't work ({ex.GetType().Name}).";
        }
    }

    // Caller holds _gate. The reply of a built-in command, or null when name isn't one.
    private string? BuiltInReply(string from, string name)
    {
        var bot = _botName ?? "The bot";
        switch (name.ToLowerInvariant())
        {
            case "pause":
                if (_pausedBy is not null)
                    return $"{bot} is already paused by {_pausedBy}. Type /resume to hand back control.";
                _pausedBy = from;
                _thinkingSince = null; // it can't act on the decision it was handed
                return $"{bot} paused. Type /resume to hand back control.";

            case "resume":
                if (_pausedBy is null)
                    return $"{bot} isn't paused.";
                _pausedBy = null;
                return $"{bot} has control again.";

            case "help":
                var entries = new List<string> { "/pause holds the bot so you can play by hand", "/resume hands control back" };
                foreach (var c in _commands)
                {
                    var entry = "/" + c.Name;
                    if (c.Usage.Length > 0) entry += " " + c.Usage;
                    var help = c.Help.TrimEnd('.', ' ');
                    if (help.Length > 0) entry += " " + help;
                    entries.Add(entry);
                }
                entries.Add("/help shows this");
                return $"Commands: {string.Join(", ", entries)}. Start a message with a character's name to talk to that bot only.";

            default:
                return null;
        }
    }

    // The command name and arguments when the first word is "/name", else null. A lone "/" or
    // a slash before a space or punctuation is ordinary text.
    private static (string Name, string Args)? SplitCommand(string text)
    {
        if (text.Length < 2 || text[0] != '/' || !char.IsLetterOrDigit(text[1]))
            return null;
        var end = 1;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        return (text[1..end], text[end..].Trim());
    }

    // Caller holds _gate. With no bot name (between runs, before a character is picked) every
    // message counts, so "Silent, go poison" at character select still reaches the one bot.
    // Co-op will need more than this.
    private bool IsForThisBot(string text)
    {
        if (_botName is null) return true;
        if (IsAddressedTo(text, _botName)) return true;
        return !KnownNames.Any(n => IsAddressedTo(text, n));
    }

    // True when the text starts with the name or "The name", optionally after "hey", "hi",
    // "ok" or "@", followed by a comma, colon, space, "!", "?" or the end.
    private static bool IsAddressedTo(string text, string name) =>
        Regex.IsMatch(text, $@"^\s*(?:(?:hey|hi|ok)\b[\s,]*)?@?(?:the\s+)?{Regex.Escape(name)}(?:$|[,:\s!?])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private sealed record Command(string Name, string Usage, string Help, Func<string, string, string> Handler);
}
