namespace SpireMind.Game.Coop;

/// <summary>
/// The launch arguments of a bot copy that <see cref="SpireMind.Core.Brain.InstanceRole"/> does
/// not cover: its client id, the character it was asked to play, the process that started it
/// and how it should keep its window. Whether a copy IS a bot is the role's business
/// (<c>--sts2bot=1</c>), never decided here; in the human's copy these simply read as "not given".
///
/// The values are read once through the game's own parser (CommandLineHelper: leading dashes
/// stripped, the value after "=" or in the next token, keys case-sensitive, the last one wins)
/// and kept in plain fields. The first read touches the game's table, so it belongs on the main
/// thread: <see cref="CoopBots.Start"/> does it at mod load, and a frame tick is fine too.
/// After that every member is safe from any thread.
/// </summary>
internal static class BotArgs
{
    /// <summary>The game's own flag: the NetId a joining copy has over ENet, and its profile folder default/&lt;id&gt;.</summary>
    public const string ClientIdFlag = "clientId";

    /// <summary><c>--sts2bot-character=SILENT</c>: the character the bot was asked to play, by Character.Id.Entry.</summary>
    public const string CharacterFlag = "sts2bot-character";

    /// <summary><c>--sts2bot-hostpid=1234</c>: the game that started this copy; when it is gone the copy quits.</summary>
    public const string HostPidFlag = "sts2bot-hostpid";

    /// <summary><c>--sts2bot-window=minimized|tile|hide</c>: how the copy keeps its window out of the way.</summary>
    public const string WindowFlag = "sts2bot-window";

    /// <summary>The NetId the game's fast join uses when <c>--clientId</c> is missing (NJoinFriendScreen).</summary>
    public const ulong DefaultNetId = 1000;

    private static readonly Lazy<Values> Loaded = new(Read, LazyThreadSafetyMode.ExecutionAndPublication);

    private sealed record Values(ulong? ClientId, string? Character, int? HostPid, string? Window);

    /// <summary>The value of <c>--clientId</c> when it is a plain number, else null.</summary>
    public static ulong? ClientId => Loaded.Value.ClientId;

    /// <summary>The NetId a copy started with these arguments has in a local (ENet) lobby or run:
    /// its <c>--clientId</c>, or 1000 without one, as the game's own join has it.</summary>
    public static ulong NetId => Loaded.Value.ClientId ?? DefaultNetId;

    /// <summary>
    /// The character this copy was asked to play, as an upper-case Character.Id.Entry
    /// ("SILENT", or the Random pick's id), or null when none was asked for or the value can't
    /// be an id. Only its shape is checked here: the game's models aren't loaded yet when the
    /// mod starts, so whoever picks the character looks the id up in ModelDb.
    /// </summary>
    public static string? Character => Loaded.Value.Character;

    /// <summary>The process id of the game that started this copy, when it said so.</summary>
    public static int? HostPid => Loaded.Value.HostPid;

    /// <summary>The window mode asked for with <c>--sts2bot-window</c> or SPIREMIND_BOT_WINDOW, lower case; null for the default.</summary>
    public static string? WindowMode => Loaded.Value.Window;

    /// <summary>Reads the arguments now if nothing has yet. Main thread.</summary>
    public static void Load() => _ = Loaded.Value;

    /// <summary>The launch token for a flag and its value, in the "=" form, so no value can be
    /// taken for another flag's.</summary>
    public static string Token(string flag, string value) => $"--{flag}={value}";

    /// <summary>
    /// Whether <paramref name="id"/> has the shape of a model id entry: upper-case letters,
    /// digits and underscores, starting with a letter, at most 40 characters.
    /// </summary>
    public static bool IsIdShaped(string id) =>
        id.Length is > 0 and <= 40 && id[0] is >= 'A' and <= 'Z'
        && id.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_');

    // Never throws: a table that can't be read means "nothing given", which is what the
    // human's copy has anyway.
    private static Values Read()
    {
        try
        {
            ulong? clientId = ulong.TryParse(CommandLineHelper.GetValue(ClientIdFlag), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : null;

            string? character = null;
            var raw = CommandLineHelper.GetValue(CharacterFlag)?.Trim();
            if (!string.IsNullOrEmpty(raw))
            {
                var upper = raw.ToUpperInvariant();
                if (IsIdShaped(upper)) character = upper;
                else ModEntry.LogError($"coop: --{CharacterFlag} must be a character id such as SILENT; ignored a value of {raw.Length} characters");
            }

            int? hostPid = int.TryParse(CommandLineHelper.GetValue(HostPidFlag), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var pid) && pid > 1 ? pid : null;

            var window = (CommandLineHelper.GetValue(WindowFlag) ?? System.Environment.GetEnvironmentVariable("SPIREMIND_BOT_WINDOW"))
                ?.Trim().ToLowerInvariant();
            return new Values(clientId, character, hostPid, string.IsNullOrEmpty(window) ? null : window);
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: reading the launch arguments failed, so none count as given: {ex.Message}");
            return new Values(null, null, null, null);
        }
    }
}
