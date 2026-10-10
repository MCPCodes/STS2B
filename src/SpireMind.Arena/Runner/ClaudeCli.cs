using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>What <c>claude auth status</c> says, without the account's email or organisation.</summary>
public sealed record AuthStatus(bool LoggedIn, string? AuthMethod, string? SubscriptionType);

/// <summary>
/// How the arena starts Claude Code: where the binary is, the arguments of one fight's session
/// and the environment it runs in. Everything here is about making the session play on the
/// subscription login with the fight's own tools, the Read tool for the checkout's strategy
/// folder (the wiki's pages, as the in-game brain can open them) and nothing else: no tool that
/// writes, edits or runs anything.
/// </summary>
public static class ClaudeCli
{
    /// <summary>The auth method <c>claude auth status</c> names for a Claude subscription login.</summary>
    public const string SubscriptionAuth = "claude.ai";

    /// <summary>The one built-in tool a fight's session gets (<c>--tools</c>): reading files.
    /// The in-game brain's session also gets Glob, Edit, Write and ToolSearch
    /// (<see cref="SubscriptionBrain.BuiltInTools"/>); a benchmark must leave the wiki as it
    /// found it, so none of those.</summary>
    public const string ReadTool = "Read";

    /// <summary>The built-in tools that could change a file, denied by name
    /// (<c>--disallowedTools</c>) although <c>--tools</c> already leaves them out: a deny rule
    /// beats every allow rule, the user's own settings included.</summary>
    public static readonly IReadOnlyList<string> WritingTools = ["Edit", "Write", "NotebookEdit", "Bash"];

    // Characters a path can't have inside a permission rule without changing what the rule
    // means: the brackets that end the rule, and what a gitignore pattern reads as a wildcard
    // or an escape.
    private static readonly char[] RuleBreakers = ['(', ')', '*', '?', '[', ']', '\\'];

    // Folders put first on the child's PATH after the binary's own, for node and helpers.
    private static readonly string[] SystemPath = ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin"];

    // The two CLAUDE_ variables that say where the login lives rather than describing a session.
    private static readonly string[] KeptVariables = ["CLAUDE_CONFIG_DIR", "CLAUDE_CODE_OAUTH_TOKEN"];

    /// <summary>Where the binary is looked for when <c>--claude</c> doesn't name it, in order:
    /// Homebrew on Apple silicon, the native installer, Homebrew on Intel and npm.</summary>
    public static IReadOnlyList<string> SearchPaths(string home) =>
    [
        "/opt/homebrew/bin/claude",
        Path.Combine(home, ".local", "bin", "claude"),
        "/usr/local/bin/claude",
    ];

    /// <summary>The first of <see cref="SearchPaths"/> that exists, or null.</summary>
    public static string? Find(string home) => SearchPaths(home).FirstOrDefault(File.Exists);

    /// <summary>The <c>--mcp-config</c> JSON: the fight's bridge on <paramref name="port"/> as the only server.</summary>
    public static string McpConfig(int port) =>
        "{\"mcpServers\":{\"" + StreamWatch.McpServerName + "\":{\"type\":\"http\",\"url\":\"http://127.0.0.1:"
        + port.ToString(CultureInfo.InvariantCulture) + "/mcp\"}}}";

    /// <summary>The folder of the checkout <paramref name="repo"/> a session may read: the
    /// strategy notes and, under <c>wiki/</c>, the wiki's pages.</summary>
    public static string StrategyFolder(string repo) => Path.Combine(repo, "strategy");

    /// <summary>
    /// The permission rule that lets the Read tool open every file under
    /// <paramref name="folder"/> (an absolute path) and nothing else, in the form the in-game
    /// brain's edit rules have: <c>Read(//Users/mark/Projects/STS2B/strategy/**)</c>. "//" starts
    /// an absolute path in Claude Code's rules. Null when the path holds a character that would
    /// change what the rule means (a bracket, a wildcard, a backslash), or isn't absolute.
    /// </summary>
    public static string? ReadRule(string folder)
    {
        var path = Path.TrimEndingDirectorySeparator(folder).Replace(Path.DirectorySeparatorChar, '/');
        if (!path.StartsWith('/') || path.Length < 2 || path.IndexOfAny(RuleBreakers) >= 0
            || path.Any(char.IsControl) || path.Split('/').Any(part => part != part.Trim()))
            return null;
        return $"{ReadTool}(//{path.TrimStart('/')}/**)";
    }

    /// <summary>
    /// The arguments of one fight's session: headless with the prompt, the session's id (new
    /// after <c>--session-id</c>, or after <c>--resume</c> to carry on the one that ended), the
    /// model and effort as given, the play prompt appended to Claude Code's own system prompt,
    /// the fight's bridge as the only MCP server, Read as the only built-in tool, permissions
    /// that allow the game tools and reading under the strategy folder of <paramref name="repo"/>
    /// and refuse anything else instead of asking, and stream-json output. This is how the
    /// in-game brain passes its own tools and rules (<see cref="SubscriptionBrain"/>), without
    /// Glob, Edit and Write and with a Read rule where that has Edit rules, so a session can open
    /// the wiki's pages and change nothing. A resumed session gets the same flags as the first,
    /// as the in-game brain's does. The session is kept on disk so it can be resumed;
    /// <see cref="DeleteSessionStore"/> removes it after the fight.
    /// </summary>
    /// <param name="repo">The checkout the system prompt was built from, by its real path
    /// (<see cref="ArenaPrompt.FindRepo"/>): the prompt names its wiki folder, so this must be
    /// the same one.</param>
    /// <exception cref="ArgumentException">No permission rule can spell the checkout's path.</exception>
    public static List<string> Arguments(string prompt, string sessionId, bool resume, string model, string effort, string systemPrompt, int port, string repo)
    {
        var readRule = ReadRule(StrategyFolder(repo))
                       ?? throw new ArgumentException($"A Claude Code permission rule can't spell the path {StrategyFolder(repo)}.", nameof(repo));
        return
        [
            "-p", prompt,
            resume ? "--resume" : "--session-id", sessionId,
            "--model", model,
            "--effort", effort,
            "--append-system-prompt", systemPrompt,
            "--strict-mcp-config",
            "--mcp-config", McpConfig(port),
            "--tools", ReadTool,
            "--permission-mode", "dontAsk",
            "--allowedTools", "mcp__" + StreamWatch.McpServerName, StreamWatch.ToolPrefix + "*", readRule,
            "--disallowedTools", .. WritingTools,
            "--output-format", "stream-json",
            "--verbose",
        ];
    }

    /// <summary>
    /// The changes to the inherited environment for every child (a null value removes the
    /// variable), so the session plays on the subscription login, with the model and effort the
    /// arena asked for:
    /// <list type="bullet">
    /// <item>every <c>ANTHROPIC_*</c> goes: the API key, the auth token, another endpoint, and
    /// model pins that would quietly swap the model under test;</item>
    /// <item>every <c>CLAUDE_*</c> and <c>CLAUDECODE</c> goes, except the two that say where the
    /// login is (<c>CLAUDE_CONFIG_DIR</c>, <c>CLAUDE_CODE_OAUTH_TOKEN</c>): the provider switches
    /// (<c>CLAUDE_CODE_USE_*</c>), and what a Claude Code session leaves for its own children when
    /// the arena is started from one (its session id and socket, <c>CLAUDE_EFFORT</c>, host auth);</item>
    /// <item>PATH starts with the binary's folder and the usual system folders.</item>
    /// </list>
    /// </summary>
    public static Dictionary<string, string?> ChildEnvironment(string claude, IReadOnlyDictionary<string, string>? inherited = null)
    {
        inherited ??= CurrentEnvironment();
        var overrides = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in inherited.Keys)
            if (IsRemoved(name))
                overrides[name] = null;
        // Named even when unset here, so the rule can be read off the result.
        overrides["ANTHROPIC_API_KEY"] = null;
        overrides["ANTHROPIC_AUTH_TOKEN"] = null;

        var folders = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inheritedPath = inherited.TryGetValue("PATH", out var path) ? path.Split(Path.PathSeparator) : [];
        foreach (var folder in new[] { Path.GetDirectoryName(claude) ?? "" }.Concat(SystemPath).Concat(inheritedPath))
            if (folder.Length > 0 && seen.Add(folder))
                folders.Add(folder);
        overrides["PATH"] = string.Join(Path.PathSeparator, folders);
        return overrides;
    }

    /// <summary>The folder Claude Code keeps its sessions in, one sub-folder per working folder:
    /// <c>projects</c> under <c>CLAUDE_CONFIG_DIR</c> when that is set (the child inherits it),
    /// else under <c>~/.claude</c>.</summary>
    public static string ProjectsFolder(string home, string? configDir = null)
    {
        configDir ??= Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return Path.Combine(string.IsNullOrWhiteSpace(configDir) ? Path.Combine(home, ".claude") : Path.GetFullPath(configDir), "projects");
    }

    /// <summary>
    /// The name of the sub-folder of <see cref="ProjectsFolder"/> Claude Code keeps the sessions
    /// of a working folder in: the folder's path with every character that isn't an ASCII letter
    /// or digit turned into "-" (Claude Code 2.1.296: <c>cwd.replace(/[^a-zA-Z0-9]/g, "-")</c>).
    /// Null for a path so long (over 200 characters) that Claude Code cuts it and adds a hash of its own.
    /// </summary>
    public static string? SessionStoreName(string workingFolder)
    {
        var path = Path.TrimEndingDirectorySeparator(workingFolder);
        var name = string.Concat(path.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-'));
        return name.Length is > 0 and <= 200 ? name : null;
    }

    /// <summary>
    /// Deletes the one folder Claude Code kept a fight's sessions in, and nothing else:
    /// <see cref="ProjectsFolder"/>/<see cref="SessionStoreName"/> of the fight's temp folder,
    /// as the child saw it (macOS hands a process started in /var/folders/... the path
    /// /private/var/folders/..., so both spellings are looked for). Nothing is looked for unless
    /// <paramref name="workingFolder"/> is what <c>Directory.CreateTempSubdirectory(marker)</c>
    /// makes: an absolute path whose last part starts with <paramref name="marker"/> (the temp
    /// folders' own prefix). A folder is only deleted when its name was derived from that path
    /// here and ends with that last part, random letters and all, when it lies directly in the
    /// projects folder, exists and is a real folder, not a link. Returns what there is to say
    /// about it for the fight's notes, or null when the folder was found and deleted.
    /// </summary>
    /// <param name="found">True when a folder of that name was there, deleted or not.</param>
    public static string? DeleteSessionStore(string workingFolder, string marker, string home, string? configDir, out bool found)
    {
        found = false;
        // The temp folder's own name, as it ends the store's name: "spiremind-arena-Ab12Cd".
        var own = SessionStoreName(Path.GetFileName(Path.TrimEndingDirectorySeparator(workingFolder)));
        if (marker.Length < 8 || own is null || !Path.IsPathFullyQualified(workingFolder)
            || !own.StartsWith(SessionStoreName(marker) ?? marker, StringComparison.Ordinal))
            return $"'{StreamWatch.Cut(workingFolder, 120)}' isn't one of the arena's temp folders, so no sessions were looked for";

        string projects;
        try
        {
            projects = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ProjectsFolder(home, configDir)));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return $"couldn't work out where Claude Code keeps its sessions ({ex.Message}), so this fight's were left there";
        }

        // Only names that end with the temp folder's own: a temp folder that turned out to be a
        // link to somewhere else gives that other place's name, which is nobody's to delete here.
        var names = new[] { RealPath(workingFolder), workingFolder }
            .Select(SessionStoreName).OfType<string>().Distinct(StringComparer.Ordinal)
            .Where(name => name.EndsWith("-" + own, StringComparison.Ordinal)).ToList();
        string? problem = null;
        var deleted = false;
        foreach (var name in names)
        {
            var folder = Path.Combine(projects, name);
            try
            {
                var info = new DirectoryInfo(folder);
                if (!info.Exists)
                    continue;
                found = true;
                // Directly in the projects folder, and the folder itself, not something a link points to.
                if (!string.Equals(Path.TrimEndingDirectorySeparator(info.Parent?.FullName ?? ""), projects, StringComparison.Ordinal)
                    || info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    problem = $"{folder} isn't a plain folder in {projects}, so it was left alone";
                    continue;
                }
                info.Delete(recursive: true);
                deleted = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                problem = $"couldn't delete this fight's sessions in {folder}: {ex.Message}";
            }
        }
        if (problem is not null)
            return problem;
        if (names.Count == 0)
            return "this fight's temp folder gives no name to look for its sessions under (its path is over 200 characters, or it is a link), so they were left where Claude Code keeps them";
        return deleted ? null : $"found no sessions of this fight to delete in {projects} (looked for {string.Join(" and ", names)})";
    }

    /// <summary>
    /// <paramref name="path"/> with the links on the way to it followed, as a process started
    /// there is told its working folder is (/var/folders/x on macOS is /private/var/folders/x).
    /// The path itself when nothing on the way is a link, or when following them fails.
    /// </summary>
    public static string RealPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? "/";
            var resolved = root;
            var hops = 0;
            var parts = new Stack<string>(full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Reverse());
            while (parts.Count > 0)
            {
                var next = Path.Combine(resolved, parts.Pop());
                FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
                if (info.LinkTarget is not { } target)
                {
                    resolved = next;
                    continue;
                }
                if (++hops > 40)
                    return path;
                // A link's target is read from the folder the link is in; its parts go back on the pile.
                var landed = Path.GetFullPath(target, resolved);
                var landedRoot = Path.GetPathRoot(landed) ?? "/";
                resolved = landedRoot;
                foreach (var part in landed[landedRoot.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Reverse())
                    parts.Push(part);
            }
            return resolved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return path;
        }
    }

    /// <summary>Whether <see cref="ChildEnvironment"/> removes a variable of this name.</summary>
    public static bool IsRemoved(string name) =>
        name.StartsWith("ANTHROPIC_", StringComparison.Ordinal)
        || name == "CLAUDECODE"
        || (name.StartsWith("CLAUDE_", StringComparison.Ordinal) && !KeptVariables.Contains(name));

    /// <summary>
    /// Runs <c>claude auth status</c> in <paramref name="environment"/> and reads whether Claude
    /// Code is signed in and how. Its output can hold the account's email, so only the three
    /// fields of <see cref="AuthStatus"/> are ever taken from it and stderr isn't read. Null when
    /// it didn't answer in time, didn't exit with 0 or didn't print JSON.
    /// </summary>
    public static async Task<AuthStatus?> AuthStatusAsync(string claude, IReadOnlyDictionary<string, string?> environment,
        IProcessLauncher launcher, TimeSpan timeout, CancellationToken ct)
    {
        using var child = launcher.Start(new ChildProcessSpec(claude, ["auth", "status"], null, environment));
        var lines = new List<string>();
        var reading = Task.Run(async () =>
        {
            await foreach (var line in child.StandardOutput.ReadAllAsync(CancellationToken.None))
                lines.Add(line);
        }, CancellationToken.None);
        try
        {
            var code = await child.Exited.WaitAsync(timeout, ct);
            await reading.WaitAsync(TimeSpan.FromSeconds(3), ct);
            return code == 0 ? ParseAuthStatus(string.Join("\n", lines)) : null;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    /// <summary>The fields of <see cref="AuthStatus"/> from the JSON object in <paramref name="text"/>, or null.</summary>
    public static AuthStatus? ParseAuthStatus(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < start)
            return null;
        try
        {
            if (JsonNode.Parse(text[start..(end + 1)]) is not JsonObject json)
                return null;
            var loggedIn = json["loggedIn"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
            return new AuthStatus(loggedIn, Short(json["authMethod"]), Short(json["subscriptionType"]));
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Short(JsonNode? node) => FightResult.Str(node) is { } s ? StreamWatch.Cut(s, 40) : null;
    }

    private static Dictionary<string, string> CurrentEnvironment()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key is string name && entry.Value is string value)
                variables[name] = value;
        return variables;
    }
}
