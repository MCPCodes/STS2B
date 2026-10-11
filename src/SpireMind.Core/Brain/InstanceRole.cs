// The mod compiles Core with global usings for Godot and the game, and Godot has its own Environment.
// This alias keeps the .NET one there.
using Environment = System.Environment;

namespace SpireMind.Core.Brain;

/// <summary>Which kind of copy of the game this is: Mark's own, or a bot copy he plays co-op with.</summary>
public enum InstanceKind { Human, Bot }

/// <summary>
/// What this copy of the game is in co-op, and where it keeps its files. Mark plays in his own
/// copy (the human copy, port 17772); each bot is a second copy on the same machine, started
/// with <c>--sts2bot=1</c> and its own port. Only a bot copy runs a brain in a co-op run.
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

    public bool IsBot => Kind == InstanceKind.Bot;

    /// <summary>"bot-17773" for a bot copy, "main" for the human copy. Names this copy's
    /// in-flight request in spend.json.</summary>
    public string InstanceId => IsBot ? $"bot-{Port}" : "main";

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
    public static InstanceRole Detect(IReadOnlyList<string> args, int port,
        Func<string, string?>? getEnv = null, string? home = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var warnings = new List<string>();
        var kind = HasBotFlag(args) ? InstanceKind.Bot : InstanceKind.Human;
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
        if (IsBot || !inCoopRun)
            return null;
        return botsExist ? HumanCoopLine : HumanCoopNoBotsLine;
    }

    /// <summary>The role in one line for the log.</summary>
    public override string ToString() =>
        $"{(IsBot ? "bot copy" : "human copy")} {InstanceId} on port {Port}; config in {ConfigDir}; shared {SharedConfigDir}";

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
