// The mod compiles Core with global usings for Godot and the game, and Godot has its own Environment.
// This alias keeps the .NET one there.
using Environment = System.Environment;

namespace SpireMind.Core.Brain;

/// <summary>Where <see cref="BrainPaths.Repo"/> came from.</summary>
public enum RepoSource
{
    /// <summary>No repo with a play prompt was found.</summary>
    None,

    /// <summary><c>repo</c> in config.json.</summary>
    Settings,

    /// <summary>The <c>brain/repo_path</c> file <c>scripts/build.sh</c> puts in the mod folder.</summary>
    ModFolder,
}

/// <summary>
/// The folders the brains use, worked out from the settings and the mod's own folder. See
/// docs/brain.md.
/// <list type="bullet">
/// <item>The repo: <c>repo</c> in config.json when that folder has the play prompt, else the
/// folder named in the mod's <c>brain/repo_path</c> (written at build time) when it has one,
/// else none.</item>
/// <item>The prompt folder, which <see cref="PromptBuilder"/> reads: the repo, else the mod's
/// <c>brain</c> folder with its staged copies of the play prompt and strategy notes.</item>
/// <item>The notes root, where the run journal and strategy/learned.md live: the repo, else the
/// config folder, so the journal survives reinstalling the mod.</item>
/// <item>The subscription brain's working folder: the repo only, since Claude Code needs the
/// real checkout for /play. Without one it says why it can't start.</item>
/// </list>
/// Reading the folders only checks that files exist, so it is cheap enough to do for every
/// brain start. Never throws.
/// </summary>
public sealed record BrainPaths
{
    /// <summary>The folder inside the mod's folder that holds the staged prompt and repo_path.</summary>
    public const string ModBrainFolder = "brain";

    /// <summary>The file in <see cref="ModBrainFolder"/> holding the repo's path, one line.</summary>
    public const string RepoPathFile = "repo_path";

    /// <summary>The STS2B checkout, or null when none with a play prompt was found.</summary>
    public string? Repo { get; init; }

    public RepoSource RepoSource { get; init; }

    /// <summary>The folder the API brain's system prompt is built from, or null when there is
    /// neither a repo nor a known mod folder.</summary>
    public string? PromptFolder { get; init; }

    /// <summary>True when <see cref="PromptFolder"/> holds the play prompt.</summary>
    public bool HasPlayPrompt { get; init; }

    /// <summary>The folder runs/current.md and strategy/learned.md are kept under.</summary>
    public required string NotesRoot { get; init; }

    /// <summary>The folder Claude Code runs in for the subscription brain: the repo, or null.</summary>
    public string? SubscriptionDirectory => Repo;

    /// <summary>Why a configured folder wasn't used, one sentence each, for the log.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Works out the folders for <paramref name="settings"/>.
    /// </summary>
    /// <param name="modDir">The folder the mod's DLL was loaded from, or null when unknown.</param>
    /// <param name="home">The home folder, for a "~" in repo_path; defaults to the user profile.</param>
    public static BrainPaths Resolve(BrainSettings settings, string? modDir, string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var warnings = new List<string>();
        string? repo = null;
        var source = RepoSource.None;

        if (!string.IsNullOrWhiteSpace(settings.Repo))
        {
            var configured = Full(settings.Repo, home);
            if (configured is not null && HasPrompt(configured))
                (repo, source) = (configured, RepoSource.Settings);
            else
                warnings.Add($"repo {settings.Repo} in {BrainSettings.ConfigFileName} has no {PromptBuilder.PlayPrompt}; ignored.");
        }

        var modBrain = string.IsNullOrWhiteSpace(modDir) ? null : Full(Path.Combine(modDir, ModBrainFolder), home);
        if (repo is null && modBrain is not null && ReadRepoPath(Path.Combine(modBrain, RepoPathFile), warnings) is { } staged)
        {
            var built = Full(staged, home);
            if (built is not null && HasPrompt(built))
                (repo, source) = (built, RepoSource.ModFolder);
            else
                warnings.Add($"The repo the mod was built from ({staged}) has no {PromptBuilder.PlayPrompt}; ignored.");
        }

        var prompt = repo ?? modBrain;
        return new BrainPaths
        {
            Repo = repo,
            RepoSource = source,
            PromptFolder = prompt,
            HasPlayPrompt = prompt is not null && HasPrompt(prompt),
            NotesRoot = repo ?? settings.ConfigDir,
            Warnings = warnings,
        };
    }

    /// <summary>True when <paramref name="folder"/> holds <c>.claude/commands/play.md</c>.</summary>
    public static bool HasPrompt(string folder)
    {
        try { return File.Exists(Path.Combine(folder, PromptBuilder.PlayPrompt)); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The folders in one line for the log.</summary>
    public override string ToString()
    {
        var repo = RepoSource switch
        {
            RepoSource.Settings => $"{Repo} (from {BrainSettings.ConfigFileName})",
            RepoSource.ModFolder => $"{Repo} (from the mod's {ModBrainFolder}/{RepoPathFile})",
            _ => "none",
        };
        var prompt = PromptFolder is null ? "none" : HasPlayPrompt ? PromptFolder : $"{PromptFolder} (no play prompt there)";
        return $"repo {repo}; prompt from {prompt}; notes in {NotesRoot}";
    }

    // The first non-empty line of repo_path, or null when the file is missing, empty or unreadable.
    private static string? ReadRepoPath(string path, List<string> warnings)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            foreach (var line in File.ReadLines(path))
                if (line.Trim() is { Length: > 0 } text)
                    return text;
            warnings.Add($"The mod's {ModBrainFolder}/{RepoPathFile} is empty.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Couldn't read the mod's {ModBrainFolder}/{RepoPathFile} ({ex.GetType().Name}).");
        }
        return null;
    }

    // An absolute path without a trailing separator, with a leading "~" expanded; null when the
    // path can't be one.
    private static string? Full(string path, string home)
    {
        var text = path.Trim();
        if (text == "~" || text.StartsWith("~/", StringComparison.Ordinal))
            text = home + text[1..];
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(text)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
