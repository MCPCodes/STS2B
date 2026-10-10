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
/// messages the model hasn't read yet, the pause switch, who the bot is playing and whether
/// it is deciding. The overlay posts on the main thread and the tools read on HTTP worker
/// threads, so one lock guards everything and <see cref="LineAdded"/> is raised only after
/// it is released. See docs/chat.md.
/// </summary>
public sealed partial class ChatHub
{
    private const int MaxLines = 200;
    private const int BurstLines = 6;
    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly Func<DateTime> _clock;
    private readonly List<ChatLine> _lines = [];
    private readonly List<ChatMessage> _unread = [];
    private readonly Queue<DateTime> _recentSays = new();
    private string? _botName;
    private string? _pausedBy;
    private JsonObject? _persona;
    private DateTime? _thinkingSince;
    private long _seq;

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

    /// <summary>Raised for every new line after the lock is released, on the thread that added
    /// it. Lines added from two threads at once may arrive out of order; Seq is the truth.</summary>
    public event Action<ChatLine>? LineAdded;

    /// <summary>
    /// A line typed by a human. /pause, /resume and /help (the whole message, any case) are
    /// handled here at once and answered with a system line. Anything else is logged, and
    /// queued for the model unless it starts with another character's name.
    /// </summary>
    public PostResult PostFromHuman(string from, string text)
    {
        var who = string.IsNullOrWhiteSpace(from) ? "Player" : from.Trim();
        var said = text?.Trim() ?? "";
        if (said.Length == 0)
            return new PostResult(PostOutcome.Empty);

        ChatLine line;
        PostResult result;
        lock (_gate)
        {
            if (CommandReply(who, said) is { } reply)
            {
                line = AddLine(ChatLineKind.System, "", reply);
                result = new PostResult(PostOutcome.Command, reply);
            }
            else
            {
                line = AddLine(ChatLineKind.Human, who, said);
                var forMe = IsForThisBot(said);
                if (forMe) _unread.Add(new ChatMessage(who, said, line.At));
                result = new PostResult(forMe ? PostOutcome.Queued : PostOutcome.NotForMe);
            }
        }
        LineAdded?.Invoke(line);
        return result;
    }

    /// <summary>
    /// The bot speaks. Whitespace is collapsed and long text cut to <see cref="MaxSayLength"/>
    /// with "...". Throws <see cref="ArgumentException"/> on empty text and
    /// <see cref="InvalidOperationException"/> past the burst limit, so the model slows down.
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
        }
        LineAdded?.Invoke(line);
        return line;
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

    // Caller holds _gate. Null when the text isn't a command.
    private string? CommandReply(string from, string text)
    {
        var bot = _botName ?? "The bot";
        switch (text.ToLowerInvariant())
        {
            case "/pause":
                if (_pausedBy is not null)
                    return $"{bot} is already paused by {_pausedBy}. Type /resume to hand back control.";
                _pausedBy = from;
                _thinkingSince = null; // it can't act on the decision it was handed
                return $"{bot} paused. Type /resume to hand back control.";

            case "/resume":
                if (_pausedBy is null)
                    return $"{bot} isn't paused.";
                _pausedBy = null;
                return $"{bot} has control again.";

            case "/help":
                return "Commands: /pause stops the bot so you can play by hand, /resume hands control back, /help shows this. " +
                       "Start a message with a character's name to talk to that bot only.";

            default:
                return null;
        }
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
}
