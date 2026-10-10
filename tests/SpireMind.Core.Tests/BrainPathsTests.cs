using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

public class BrainPathsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spiremind-paths-" + Guid.NewGuid().ToString("N"));

    public BrainPathsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private string ConfigDir => Path.Combine(_dir, "config");

    private BrainSettings Settings(string? repo = null) => new() { ConfigDir = ConfigDir, Repo = repo };

    // A folder with the play prompt in it, like the repo or the staged brain folder.
    private string WithPrompt(string name)
    {
        var folder = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.Combine(folder, ".claude", "commands"));
        File.WriteAllText(Path.Combine(folder, ".claude", "commands", "play.md"), "# Play\n");
        return folder;
    }

    // A mod folder whose brain/repo_path holds the given text; the brain folder gets a staged
    // play prompt unless staged is false.
    private string ModDir(string? repoPath, bool staged = true)
    {
        var mod = Path.Combine(_dir, "mods", "SpireMind");
        var brain = staged ? WithPrompt(Path.Combine("mods", "SpireMind", "brain")) : Path.Combine(mod, "brain");
        Directory.CreateDirectory(brain);
        if (repoPath is not null)
            File.WriteAllText(Path.Combine(brain, "repo_path"), repoPath);
        return mod;
    }

    [Fact]
    public void Configured_repo_with_a_play_prompt_is_used_for_everything()
    {
        var repo = WithPrompt("repo");
        var mod = ModDir(WithPrompt("other"));

        var paths = BrainPaths.Resolve(Settings(repo), mod);

        Assert.Equal(repo, paths.Repo);
        Assert.Equal(RepoSource.Settings, paths.RepoSource);
        Assert.Equal(repo, paths.PromptFolder);
        Assert.True(paths.HasPlayPrompt);
        Assert.Equal(repo, paths.NotesRoot);
        Assert.Equal(repo, paths.SubscriptionDirectory);
        Assert.Empty(paths.Warnings);
    }

    [Fact]
    public void Trailing_separator_is_dropped()
    {
        var repo = WithPrompt("repo");

        var paths = BrainPaths.Resolve(Settings(repo + Path.DirectorySeparatorChar), modDir: null);

        Assert.Equal(repo, paths.Repo);
    }

    [Fact]
    public void Repo_path_from_the_mod_folder_when_the_settings_have_none()
    {
        var repo = WithPrompt("repo");
        var mod = ModDir(repo + "\n");

        var paths = BrainPaths.Resolve(Settings(), mod);

        Assert.Equal(repo, paths.Repo);
        Assert.Equal(RepoSource.ModFolder, paths.RepoSource);
        Assert.Equal(repo, paths.PromptFolder);
        Assert.Equal(repo, paths.NotesRoot);
        Assert.Equal(repo, paths.SubscriptionDirectory);
        Assert.Empty(paths.Warnings);
    }

    [Fact]
    public void Repo_path_takes_the_first_non_empty_line_trimmed()
    {
        var repo = WithPrompt("repo");
        var mod = ModDir($"\n   \n  {repo}  \r\n/somewhere/else\n");

        Assert.Equal(repo, BrainPaths.Resolve(Settings(), mod).Repo);
    }

    [Fact]
    public void Repo_path_expands_a_leading_tilde()
    {
        WithPrompt(Path.Combine("home", "Projects", "STS2B"));
        var mod = ModDir("~/Projects/STS2B");

        var paths = BrainPaths.Resolve(Settings(), mod, home: Path.Combine(_dir, "home"));

        Assert.Equal(Path.Combine(_dir, "home", "Projects", "STS2B"), paths.Repo);
    }

    [Fact]
    public void Configured_repo_without_a_play_prompt_falls_back_to_repo_path_with_a_warning()
    {
        var empty = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(empty);
        var repo = WithPrompt("repo");
        var mod = ModDir(repo);

        var paths = BrainPaths.Resolve(Settings(empty), mod);

        Assert.Equal(repo, paths.Repo);
        Assert.Equal(RepoSource.ModFolder, paths.RepoSource);
        var warning = Assert.Single(paths.Warnings);
        Assert.Contains(empty, warning);
        Assert.Contains(".claude/commands/play.md", warning);
    }

    [Fact]
    public void Missing_configured_repo_is_ignored_with_a_warning()
    {
        var missing = Path.Combine(_dir, "nowhere");

        var paths = BrainPaths.Resolve(Settings(missing), modDir: null);

        Assert.Null(paths.Repo);
        Assert.Equal(RepoSource.None, paths.RepoSource);
        Assert.Contains(missing, Assert.Single(paths.Warnings));
    }

    [Fact]
    public void Without_a_repo_the_prompt_comes_from_the_mod_and_the_notes_go_to_the_config_folder()
    {
        var mod = ModDir(repoPath: null);

        var paths = BrainPaths.Resolve(Settings(), mod);

        Assert.Null(paths.Repo);
        Assert.Equal(RepoSource.None, paths.RepoSource);
        Assert.Equal(Path.Combine(mod, "brain"), paths.PromptFolder);
        Assert.True(paths.HasPlayPrompt);
        Assert.Equal(ConfigDir, paths.NotesRoot);
        Assert.Null(paths.SubscriptionDirectory);
        Assert.Empty(paths.Warnings);
    }

    [Fact]
    public void Repo_path_pointing_at_a_folder_without_a_play_prompt_is_ignored_with_a_warning()
    {
        var moved = Path.Combine(_dir, "moved");
        var mod = ModDir(moved);

        var paths = BrainPaths.Resolve(Settings(), mod);

        Assert.Null(paths.Repo);
        Assert.Equal(Path.Combine(mod, "brain"), paths.PromptFolder);
        Assert.Equal(ConfigDir, paths.NotesRoot);
        Assert.Contains(moved, Assert.Single(paths.Warnings));
    }

    [Fact]
    public void Empty_repo_path_is_reported()
    {
        var mod = ModDir(" \n\n");

        var paths = BrainPaths.Resolve(Settings(), mod);

        Assert.Null(paths.Repo);
        Assert.Contains("repo_path is empty", Assert.Single(paths.Warnings));
    }

    [Fact]
    public void Mod_folder_without_staged_files_still_names_the_prompt_folder()
    {
        var mod = ModDir(repoPath: null, staged: false);

        var paths = BrainPaths.Resolve(Settings(), mod);

        Assert.Equal(Path.Combine(mod, "brain"), paths.PromptFolder);
        Assert.False(paths.HasPlayPrompt);
        Assert.Contains("no play prompt there", paths.ToString());
    }

    [Fact]
    public void Without_a_repo_or_a_mod_folder_only_the_notes_root_is_known()
    {
        var paths = BrainPaths.Resolve(Settings(), modDir: null);

        Assert.Null(paths.Repo);
        Assert.Null(paths.PromptFolder);
        Assert.False(paths.HasPlayPrompt);
        Assert.Equal(ConfigDir, paths.NotesRoot);
        Assert.Null(paths.SubscriptionDirectory);
        Assert.Equal($"repo none; prompt from none; notes in {ConfigDir}", paths.ToString());
    }

    [Fact]
    public void ToString_says_where_the_repo_came_from()
    {
        var repo = WithPrompt("repo");

        Assert.Equal($"repo {repo} (from config.json); prompt from {repo}; notes in {repo}",
            BrainPaths.Resolve(Settings(repo), modDir: null).ToString());
        Assert.Contains("(from the mod's brain/repo_path)", BrainPaths.Resolve(Settings(), ModDir(repo)).ToString());
    }
}
