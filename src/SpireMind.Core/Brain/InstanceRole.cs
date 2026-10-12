// The mod compiles Core with global usings for Godot and the game, and Godot has its own Environment.
// This alias keeps the .NET one there.
using Environment = System.Environment;
using GuestArgs = SpireMind.Core.Coop.GuestArgs;

namespace SpireMind.Core.Brain;

/// <summary>Which kind of copy of the game this is: Mark's own, a bot copy he plays co-op with,
/// or a friend's game on another computer that joins his lobby as a guest.</summary>
public enum InstanceKind { Human, Bot, Guest }

/// <summary>
/// What this copy of the game is in co-op, and where it keeps its files. Mark plays in his own
/// copy (the human copy, port 17772); each bot is a second copy on the same machine, started
/// with <c>--sts2bot=1</c>, without Steam, and on its own port. Only a bot copy runs a brain in
/// a co-op run. A copy that runs with Steam is always the human copy, whatever its command line
/// says, so a stray <c>--sts2bot=1</c> in Steam's launch options can't turn Mark's own game into
/// a bot copy.
///
/// A third kind is the guest copy: a friend's own game on another computer, started with
/// <c>--spiremind-host=&lt;address&gt;</c>, that joins the host's lobby (docs/coop-guest.md). It
/// runs no brain, no bridge and no bot copies and never touches the folders below. Only that
/// launch option makes one, and never in a copy that hosts (<c>--fastmp host...</c>), so the
/// host's own game cannot turn into a guest.
/// <list type="bullet">
/// <item><see cref="ConfigDir"/> holds this copy's config.json, transcripts and autostart flag.
/// <see cref="SharedConfigDir"/> (<c>~/.config/spiremind</c>) holds what every copy shares:
/// spend.json and its one monthly cap, the shared api_key and config.json, and the locks.</item>
/// <item>A bot keeps its own run journal (<see cref="JournalFile"/>); strategy/learned.md and
/// the wiki stay shared, guarded by <see cref="NotesLockPath"/>.</item>
/// </list>
/// Immutable, worked out once at mod load by <see cref="Detect"/>. Never holds the key.
/// </summary>
public sealed record InstanceRole
{
    /// <summary>The command-line flag that makes a bot copy: <c>--sts2bot=1</c>.</summary>
    public const string BotArg = "--sts2bot";

    /// <summary>Picks this copy's config folder; unset means the default for its kind.</summary>
    public const string ConfigDirEnvVar = "SPIREMIND_CONFIG_DIR";

    /// <summary>The human copy's bridge port; bots use 17773 to 17775.</summary>
    public const int HumanPort = 17772;

    /// <summary>Why the human copy's brain stays off in a co-op run with bot copies around.</summary>
    public const string HumanCoopLine =
        "In co-op the bot copies play, so this copy's brain stays off. /start, /stop and the other commands go to the bots.";

    /// <summary>Why the human copy's brain stays off in a co-op run or lobby with no bot copy.</summary>
    public const string HumanCoopNoBotsLine =
        "This is co-op, where only bot copies play, so this copy's brain stays off. /bot add starts one.";

    public required InstanceKind Kind { get; init; }

    /// <summary>The port this copy's bridge listens on.</summary>
    public required int Port { get; init; }

    /// <summary>This copy's folder: config.json, transcripts, autostart.</summary>
    public required string ConfigDir { get; init; }

    /// <summary>The folder every copy shares (<c>~/.config/spiremind</c>): spend.json, the shared
    /// api_key and config.json, and the locks.</summary>
    public required string SharedConfigDir { get; init; }

    /// <summary>Problems found by <see cref="Detect"/>, one sentence each, for the log.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>The launch options of a guest copy; null in any other copy.</summary>
    public GuestArgs? Guest { get; init; }

    public bool IsBot => Kind == InstanceKind.Bot;

    /// <summary>True for a friend's game that joins the host's lobby from another computer.
    /// Such a copy starts nothing of the mod but the join and the names.</summary>
    public bool IsGuest => Kind == InstanceKind.Guest;

    /// <summary>"bot-17773" for a bot copy, "main" for the human copy, "guest" for a guest
    /// copy. Names this copy's in-flight request in spend.json (a guest copy has none).</summary>
    public string InstanceId => IsBot ? $"bot-{Port}" : IsGuest ? "guest" : "main";

    /// <summary>Why no brain ever plays in a guest copy.</summary>
    public const string GuestLine = "This copy is a guest in someone else's game: no brain runs here.";

    /// <summary>Where this copy keeps its run journal under the notes root. The notes tools
    /// still call it runs/current.md (see <see cref="NotesOptions.JournalFile"/>).</summary>
    public string JournalFile => IsBot ? $"runs/bot-{Port}/current.md" : NotesTools.Journal;

    /// <summary>The lock every copy takes around a read or write of the shared notes.</summary>
    public string NotesLockPath => Path.Combine(SharedConfigDir, "locks", "notes.lock");

    /// <summary>
    /// The prompt that starts a Claude Code session for the subscription fallback. Claude Code
    /// uses its own file tools, so a bot copy is told where its journal is.
    /// </summary>
    public string SubscriptionPrompt => IsBot
        ? $"{SubscriptionBrainOptions.DefaultPrompt}. You are a bot copy in co-op, so your run journal is {JournalFile} " +
          $"instead of runs/current.md: read, write and archive it (archives go in runs/bot-{Port}/) wherever the prompt says runs/current.md."
        : SubscriptionBrainOptions.DefaultPrompt;

    /// <summary>
    /// Works out the role from the command line and the environment. Never throws: a value that
    /// can't be used falls back to the default and adds a warning.
    /// </summary>
    /// <param name="args">The command line, as Godot's OS.GetCmdlineArgs gives it. A bot copy has
    /// <c>--sts2bot=1</c> or <c>--sts2bot 1</c> (1 or true, any case). The flag's name is
    /// case-sensitive, as the game's own command-line reader is, and nothing after a bare
    /// <c>--</c> counts.</param>
    /// <param name="port">This copy's bridge port.</param>
    /// <param name="getEnv">Environment lookup for tests; defaults to <see cref="Environment.GetEnvironmentVariable(string)"/>.</param>
    /// <param name="home">The home folder; defaults to the user profile.</param>
    /// <param name="steamRunning">True when this copy runs with Steam (the game brings Steam up,
    /// or skips it, before it loads mods). Asked only when the bot flag is set. A copy with Steam
    /// is the human copy whatever its flag says, and so is one where this throws, since then
    /// nobody can tell; both add a warning. Null means no Steam (tests, the dev host).</param>
    public static InstanceRole Detect(IReadOnlyList<string> args, int port,
        Func<string, string?>? getEnv = null, string? home = null, Func<bool>? steamRunning = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var warnings = new List<string>();
        var kind = HasBotFlag(args) ? InstanceKind.Bot : InstanceKind.Human;
        if (kind == InstanceKind.Bot && SteamBlocksBot(steamRunning) is { } why)
        {
            kind = InstanceKind.Human;
            warnings.Add($"{BotArg}=1 is on the command line, but {why}, so the flag is ignored and this copy runs as the human copy. " +
                         "A bot copy is started without Steam (--force-steam=off).");
        }

        // A guest copy: only by its own launch option, never a bot copy, never a copy that hosts.
        GuestArgs? guest = null;
        var asked = GuestArgs.Parse(args);
        if (asked.Asked)
        {
            if (kind == InstanceKind.Bot)
                warnings.Add($"--{GuestArgs.HostFlag} is on the command line of a bot copy and is ignored: a bot copy joins the lobby on its own machine.");
            else if (asked.Hosting)
                warnings.Add($"--{GuestArgs.HostFlag} (or another --{GuestArgs.OwnPrefix} option) is on the command line, but so is --{GuestArgs.FastMpFlag} for hosting, " +
                             "so it is ignored and this copy runs as the human copy. A guest copy joins: --fastmp=join.");
            else
            {
                kind = InstanceKind.Guest;
                guest = asked;
            }
        }
        var shared = Path.Combine(home, ".config", "spiremind");
        var fallback = kind == InstanceKind.Bot ? Path.Combine(shared, $"bot-{port}") : shared;

        var configDir = fallback;
        string? env = null;
        try { env = getEnv(ConfigDirEnvVar)?.Trim(); }
        catch (Exception) { /* an environment that can't be read counts as unset */ }
        if (!string.IsNullOrEmpty(env))
        {
            if (FullPath(env, home) is { } dir)
                configDir = dir;
            else
                warnings.Add($"{ConfigDirEnvVar} ({env}) can't be a folder path; using {fallback}.");
        }

        if (kind == InstanceKind.Bot)
        {
            if (port == HumanPort)
                warnings.Add($"This bot copy is on port {HumanPort}, the human copy's, so it clashes with the human copy; set SPIREMIND_PORT (17773 to 17775).");
            if (SamePath(configDir, shared))
                warnings.Add($"This bot copy's config folder is the shared one ({shared}), so it shares autostart and transcripts with the human copy; " +
                             $"leave {ConfigDirEnvVar} unset or point it at a folder of its own.");
        }

        return new InstanceRole
        {
            Kind = kind,
            Port = port,
            ConfigDir = configDir,
            SharedConfigDir = shared,
            Warnings = warnings,
            Guest = guest,
        };
    }

    /// <summary>
    /// Why no brain may play in this copy right now, or null when one may. A bot copy may always
    /// play. The human copy may not in a co-op run or lobby, since there the bots play and two
    /// brains would act for the same player. Outside co-op it plays as in solo play, also while
    /// bot copies are still running.
    /// </summary>
    /// <param name="inCoopRun">The game is in a co-op run (or lobby).</param>
    /// <param name="botsExist">This copy has found a bot copy on this machine; picks the line.</param>
    public string? BrainBlockedReason(bool inCoopRun, bool botsExist)
    {
        if (IsGuest)
            return GuestLine;
        if (IsBot || !inCoopRun)
            return null;
        return botsExist ? HumanCoopLine : HumanCoopNoBotsLine;
    }

    /// <summary>The role in one line for the log.</summary>
    public override string ToString() => IsGuest
        ? Guest is { CanDial: true } guest
            ? $"guest copy; joins {guest.Address}:{guest.Port} as player {guest.ClientId}; no brain, no bridge, no bot copies, no files"
            : $"guest copy; NOT joining, the launch options have {Guest?.Problems.Count ?? 0} problem(s); no brain, no bridge, no bot copies, no files"
        : $"{(IsBot ? "bot copy" : "human copy")} {InstanceId} on port {Port}; config in {ConfigDir}; shared {SharedConfigDir}";

    // Why the bot flag can't count (this copy runs with Steam, or that can't be told), or null
    // when it can.
    private static string? SteamBlocksBot(Func<bool>? steamRunning)
    {
        if (steamRunning is null)
            return null;
        try
        {
            return steamRunning() ? "this copy runs with Steam" : null;
        }
        catch (Exception ex)
        {
            return $"whether this copy runs with Steam couldn't be told ({ex.GetType().Name}: {ex.Message})";
        }
    }

    // True when args hold --sts2bot=<v> or --sts2bot <v> with v 1 or true. The first occurrence decides.
    private static bool HasBotFlag(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i] ?? "";
            if (arg == "--")
                return false;
            string? value = null;
            if (arg == BotArg)
                value = i + 1 < args.Count ? args[i + 1] : null;
            else if (arg.StartsWith(BotArg + "=", StringComparison.Ordinal))
                value = arg[(BotArg.Length + 1)..];
            else
                continue;
            var v = value?.Trim() ?? "";
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    // An absolute path without a trailing separator, with a leading "~" expanded; null when the
    // text can't be a path.
    private static string? FullPath(string text, string home)
    {
        if (text == "~" || text.StartsWith("~/", StringComparison.Ordinal))
            text = home + text[1..];
        if (text.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || text.Contains('\0'))
            return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(text)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException) { return null; }
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(a, b, StringComparison.Ordinal);
        }
    }
}
