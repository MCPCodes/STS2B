namespace SpireMind.Core.Coop;

/// <summary>
/// Who sits where in a local (ENet) co-op session, by player id alone, so that every copy
/// works out the same answer with no help from another copy:
/// <list type="bullet">
/// <item>the host's id is the person who hosts (always 1 over ENet);</item>
/// <item>1000 to 1002 are the bot copies the host's game starts (<c>/bot add</c>);</item>
/// <item>any other id is a guest: a person who joined from another computer
/// (docs/coop-guest.md). The instructions give a guest 2000.</item>
/// </list>
/// The game never sends names, so each copy names a guest itself. The host's copy and the bot
/// copies take the name from the host's own settings (<see cref="ReadNames"/>): the file
/// <see cref="GuestNameFile"/> in the folder every copy on the host's machine shares, or
/// <see cref="GuestNameEnvVar"/>, which a bot copy inherits from the game that started it.
/// Pure, but for that one small file read.
/// </summary>
public static class CoopSeats
{
    /// <summary>The first bot copy's player id; the next ones count up.</summary>
    public const ulong FirstBotId = 1000;

    /// <summary>How many ids the bots have: 1000, 1001, 1002.</summary>
    public const int BotIds = 3;

    /// <summary>
    /// What the host calls the guests, set where the host's game starts (Steam launch options,
    /// before <c>%command%</c>): <c>SPIREMIND_GUEST_NAME=Amir</c>. Several guests: a comma list
    /// in the order they sit in the lobby.
    /// </summary>
    public const string GuestNameEnvVar = "SPIREMIND_GUEST_NAME";

    /// <summary>
    /// The file in the shared config folder (<c>~/.config/spiremind/guest_name</c>) that holds
    /// what the host calls the guests: one name, or several on one line with commas or on a
    /// line each, in the order they sit in the lobby. It needs no launch option, and the bot
    /// copies read the same file.
    /// </summary>
    public const string GuestNameFile = "guest_name";

    /// <summary>No more than this much of <see cref="GuestNameFile"/> is read.</summary>
    private const int MaxFileChars = 400;

    /// <summary>A guest nobody named.</summary>
    public const string DefaultGuestName = "Guest";

    /// <summary>Names are cut to this many characters: a nameplate is narrow.</summary>
    public const int MaxNameLength = 24;

    /// <summary>True for a player id the host's game gives its bot copies.</summary>
    public static bool IsBotId(ulong netId) => netId >= FirstBotId && netId < FirstBotId + BotIds;

    /// <summary>
    /// True for a player of a local session who is neither its host nor a bot copy: a person on
    /// another computer.
    /// </summary>
    public static bool IsGuest(ulong netId, ulong hostNetId) => netId != hostNetId && !IsBotId(netId);

    /// <summary>The names in a setting's text, cleaned, in order: separated by commas or line ends. Empty for none.</summary>
    public static IReadOnlyList<string> ParseNames(string? text)
    {
        var names = new List<string>();
        foreach (var part in (text ?? "").Split(',', '\n', '\r'))
            if (CleanName(part) is { } name)
                names.Add(name);
        return names;
    }

    /// <summary>
    /// What the host calls the guests: <see cref="GuestNameEnvVar"/> when it names anyone, else
    /// the file <see cref="GuestNameFile"/> in <paramref name="sharedConfigDir"/>. Never throws:
    /// whatever can't be read counts as not set. Only that one file is opened.
    /// </summary>
    /// <param name="getEnv">Environment lookup.</param>
    /// <param name="sharedConfigDir">The folder every copy on the host's machine shares
    /// (<c>~/.config/spiremind</c>); null to look at the environment only.</param>
    public static IReadOnlyList<string> ReadNames(Func<string, string?>? getEnv, string? sharedConfigDir)
    {
        try
        {
            if (getEnv is not null && ParseNames(getEnv(GuestNameEnvVar)) is { Count: > 0 } fromEnv)
                return fromEnv;
        }
        catch (Exception)
        {
            // An environment that can't be read counts as unset.
        }

        try
        {
            if (string.IsNullOrEmpty(sharedConfigDir)) return [];
            var path = Path.Combine(sharedConfigDir, GuestNameFile);
            if (!File.Exists(path)) return [];
            using var reader = new StreamReader(path);
            var buffer = new char[MaxFileChars];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            return ParseNames(new string(buffer, 0, read));
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// The name of the nth guest (from 0, in the session's seat order): the nth given name,
    /// else "Guest", "Guest 2" and so on.
    /// </summary>
    public static string GuestName(IReadOnlyList<string>? names, int nth)
    {
        if (nth < 0) nth = 0;
        if (names is not null && nth < names.Count) return names[nth];
        return nth == 0 ? DefaultGuestName : $"{DefaultGuestName} {nth + 1}";
    }

    /// <summary>
    /// A name fit for a nameplate and a chat line: trimmed, no control characters, no square
    /// brackets (the game draws some names as BBCode), at most <see cref="MaxNameLength"/>
    /// characters. Null when nothing is left.
    /// </summary>
    public static string? CleanName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var kept = new string(raw.Where(c => !char.IsControl(c) && c is not ('[' or ']')).ToArray()).Trim();
        if (kept.Length > MaxNameLength) kept = kept[..MaxNameLength].TrimEnd();
        return kept.Length == 0 ? null : kept;
    }
}
