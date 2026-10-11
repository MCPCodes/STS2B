using System.Text.Json.Nodes;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

/// <summary>A bot copy's settings: its own folder layered over the shared one.</summary>
public class CoopSettingsTests : IDisposable
{
    private const string SharedKey = "sk-ant-api03-SHAREDKEY-0123456789abcdef";
    private const string BotKey = "sk-ant-api03-BOTKEY-fedcba9876543210";
    private const string EnvKey = "sk-ant-api03-ENVKEY-00112233445566778";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "spiremind-coop-settings-" + Guid.NewGuid().ToString("N"));

    public CoopSettingsTests()
    {
        Directory.CreateDirectory(Shared);
        Directory.CreateDirectory(Bot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private string Shared => Path.Combine(_root, "spiremind");
    private string Bot => Path.Combine(_root, "spiremind", "bot-17773");

    private static Func<string, string?> Env(string? key = null) =>
        name => name == BrainSettings.ApiKeyEnvVar ? key : null;

    private BrainSettings LoadBot(string? envKey = null) => BrainSettings.Load(Bot, Env(envKey), Shared);

    private static void Write(string dir, string name, string text, bool privateFile = false)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        if (privateFile && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static JsonObject ReadJson(string dir) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "config.json")))!.AsObject();

    [Fact]
    public void One_folder_is_not_an_instance()
    {
        var s = BrainSettings.Load(Shared, Env());
        Assert.False(s.IsInstance);
        Assert.Equal(Shared, s.SharedConfigDir);
        Assert.False(BrainSettings.Load(Shared, Env(), Shared).IsInstance);
        Assert.False(BrainSettings.Load(Shared, Env(), Shared + Path.DirectorySeparatorChar).IsInstance);
        Assert.False(new BrainSettings { ConfigDir = Bot }.IsInstance);
        Assert.Equal(Bot, new BrainSettings { ConfigDir = Bot }.SharedConfigDir);
        Assert.Equal(BrainSettings.DefaultConfigDir, new BrainSettings().SharedConfigDir);
    }

    [Fact]
    public void An_instance_layers_its_own_config_over_the_shared_one()
    {
        Write(Shared, "config.json", """{ "model": "sonnet", "effort": "high", "repo": "/tmp/sts2b", "claude_path": "/opt/claude" }""");
        Write(Bot, "config.json", """{ "effort": "low", "autostart": true }""");

        var s = LoadBot();

        Assert.True(s.IsInstance);
        Assert.Equal(Bot, s.ConfigDir);
        Assert.Equal(Shared, s.SharedConfigDir);
        Assert.Equal("claude-sonnet-5-5", s.Model);
        Assert.Equal("low", s.Effort);
        Assert.True(s.Autostart);
        Assert.Equal("/tmp/sts2b", s.Repo);
        Assert.Equal("/opt/claude", s.ClaudePath);
        Assert.Empty(s.Warnings);
    }

    [Fact]
    public void Without_files_an_instance_has_the_defaults()
    {
        var s = LoadBot();
        Assert.True(s.IsInstance);
        Assert.Equal("claude-opus-5-5", s.Model);
        Assert.Equal(200m, s.MonthlyBudgetUsd);
        Assert.False(s.Autostart);
        Assert.False(s.HasApiKey);
        Assert.Null(s.ApiKeyFile);
        Assert.Empty(s.Warnings);
    }

    [Fact]
    public void The_budget_comes_only_from_the_shared_file()
    {
        Write(Shared, "config.json", """{ "monthly_budget_usd": 50 }""");
        Write(Bot, "config.json", """{ "monthly_budget_usd": 500, "model": "haiku" }""");

        var s = LoadBot();

        Assert.Equal(50m, s.MonthlyBudgetUsd);
        Assert.Equal("claude-haiku-5-5", s.Model);
        var warning = Assert.Single(s.Warnings);
        Assert.Equal($"monthly_budget_usd is shared by every copy; set it in {Path.Combine(Shared, "config.json")}.", warning);
    }

    [Fact]
    public void Unknown_keys_name_the_file_they_came_from()
    {
        Write(Shared, "config.json", """{ "colour": "red" }""");
        Write(Bot, "config.json", """{ "flavour": "mint" }""");

        var warnings = LoadBot().Warnings;

        Assert.Contains($"Unknown key 'colour' in {Path.Combine(Shared, "config.json")}; ignored.", warnings);
        Assert.Contains($"Unknown key 'flavour' in {Path.Combine(Bot, "config.json")}; ignored.", warnings);
    }

    [Fact]
    public void A_broken_instance_file_keeps_the_shared_settings()
    {
        Write(Shared, "config.json", """{ "model": "sonnet" }""");
        Write(Bot, "config.json", "{ nope");

        var s = LoadBot();

        Assert.Equal("claude-sonnet-5-5", s.Model);
        Assert.Contains(s.Warnings, w => w.StartsWith(Path.Combine(Bot, "config.json") + " isn't valid JSON"));
    }

    [Fact]
    public void The_key_falls_back_to_the_shared_api_key()
    {
        Write(Shared, "api_key", SharedKey + "\n", privateFile: true);

        var s = LoadBot(EnvKey);

        Assert.Equal(SharedKey, s.ApiKey);
        Assert.Equal(ApiKeySource.File, s.ApiKeySource);
        Assert.Equal(Path.Combine(Shared, "api_key"), s.ApiKeyFile);
        Assert.Contains("ApiKey = from the shared api_key", s.ToString());
        Assert.DoesNotContain(SharedKey, s.ToString());
        Assert.Empty(s.Warnings);
    }

    [Fact]
    public void The_instances_own_api_key_wins()
    {
        Write(Shared, "api_key", SharedKey, privateFile: true);
        Write(Bot, "api_key", BotKey, privateFile: true);

        var s = LoadBot(EnvKey);

        Assert.Equal(BotKey, s.ApiKey);
        Assert.Equal(ApiKeySource.File, s.ApiKeySource);
        Assert.Equal(Path.Combine(Bot, "api_key"), s.ApiKeyFile);
        Assert.Contains("ApiKey = from api_key", s.ToString());
        Assert.DoesNotContain(BotKey, s.ToString());
    }

    [Fact]
    public void Without_either_file_the_environment_key_is_used()
    {
        var s = LoadBot(EnvKey);
        Assert.Equal(EnvKey, s.ApiKey);
        Assert.Equal(ApiKeySource.Environment, s.ApiKeySource);
        Assert.Null(s.ApiKeyFile);
    }

    [Fact]
    public void An_unusable_instance_key_falls_back_to_the_shared_one_with_a_warning()
    {
        Write(Bot, "api_key", "not a key", privateFile: true);
        Write(Shared, "api_key", SharedKey, privateFile: true);

        var s = LoadBot();

        Assert.Equal(SharedKey, s.ApiKey);
        Assert.Equal(Path.Combine(Shared, "api_key"), s.ApiKeyFile);
        var warning = Assert.Single(s.Warnings);
        Assert.Contains(Path.Combine(Bot, "api_key"), warning);
        Assert.DoesNotContain("not a key", warning);
    }

    [Fact]
    public void A_single_folder_reports_its_key_file_too()
    {
        Write(Shared, "api_key", SharedKey, privateFile: true);
        var s = BrainSettings.Load(Shared, Env());
        Assert.Equal(Path.Combine(Shared, "api_key"), s.ApiKeyFile);
        Assert.Contains("ApiKey = from api_key", s.ToString());
        Assert.DoesNotContain("SharedConfigDir", s.ToString());
    }

    [Fact]
    public void ToString_names_the_shared_folder_of_an_instance()
    {
        Assert.Contains($"SharedConfigDir = {Shared}", LoadBot().ToString());
    }

    [Fact]
    public void Saving_an_instance_writes_only_what_the_commands_change_to_its_own_file()
    {
        var sharedText = """{ "model": "sonnet", "monthly_budget_usd": 50, "repo": "/tmp/sts2b" }""";
        Write(Shared, "config.json", sharedText);
        Write(Bot, "config.json", """{ "effort": "low", "my_note": "keep me", "claude_path": "/opt/claude" }""");
        var s = LoadBot();

        (s with { Model = "haiku", Effort = "max", Autostart = true, MonthlyBudgetUsd = 999m, Repo = null }).Save();

        Assert.Equal(sharedText, File.ReadAllText(Path.Combine(Shared, "config.json")));
        var own = ReadJson(Bot);
        Assert.Equal("claude-haiku-5-5", own["model"]!.GetValue<string>());
        Assert.Equal("max", own["effort"]!.GetValue<string>());
        Assert.True(own["autostart"]!.GetValue<bool>());
        Assert.Equal("keep me", own["my_note"]!.GetValue<string>());
        Assert.Equal("/opt/claude", own["claude_path"]!.GetValue<string>());
        Assert.False(own.ContainsKey("monthly_budget_usd"));
        Assert.False(own.ContainsKey("repo"));

        var again = LoadBot();
        Assert.Equal(50m, again.MonthlyBudgetUsd);
        Assert.Equal("/tmp/sts2b", again.Repo);
        Assert.Equal("claude-haiku-5-5", again.Model);
        Assert.Equal($"Unknown key 'my_note' in {Path.Combine(Bot, "config.json")}; ignored.", Assert.Single(again.Warnings));
    }

    [Fact]
    public void Saving_an_instance_creates_its_file_and_folder()
    {
        var fresh = Path.Combine(_root, "spiremind", "bot-17775");
        var s = BrainSettings.Load(fresh, Env(), Shared) with { Autostart = true };

        s.Save();

        var own = ReadJson(fresh);
        Assert.Equal(["model", "effort", "autostart"], own.Select(p => p.Key));
        Assert.False(File.Exists(Path.Combine(Shared, "config.json")));
    }

    [Fact]
    public void Saving_a_single_folder_is_unchanged()
    {
        Write(Shared, "config.json", """{ "my_note": "keep me" }""");
        var s = BrainSettings.Load(Shared, Env()) with { Model = "sonnet", Autostart = true, MonthlyBudgetUsd = 75m };

        s.Save();

        var json = ReadJson(Shared);
        Assert.Equal("claude-sonnet-5-5", json["model"]!.GetValue<string>());
        Assert.Equal(75m, json["monthly_budget_usd"]!.GetValue<decimal>());
        Assert.True(json["autostart"]!.GetValue<bool>());
        Assert.Equal("keep me", json["my_note"]!.GetValue<string>());
    }

    [Fact]
    public void The_notes_stay_in_the_shared_folder_without_a_repo()
    {
        var bot = LoadBot();
        var paths = BrainPaths.Resolve(bot, modDir: null);
        Assert.Equal(Shared, paths.NotesRoot);

        var single = BrainSettings.Load(Bot, Env());
        Assert.Equal(Bot, BrainPaths.Resolve(single, modDir: null).NotesRoot);
    }

    [Fact]
    public void An_instance_keeps_a_fight_effort_set_in_the_chat_across_a_reload()
    {
        Write(Shared, "config.json", """{ "model": "sonnet" }""");
        var chat = new ChatHub { BotName = "Silent" };
        var controller = NewController(chat);

        var reply = chat.PostFromHuman("Mark", "/effort elite max").Reply!;
        Assert.DoesNotContain("Couldn't save", reply);
        chat.PostFromHuman("Mark", "/effort boss same");

        // /model reads config.json again before it changes anything.
        chat.PostFromHuman("Mark", "/model haiku");
        Assert.Equal("max", controller.Settings.EliteEffort);
        Assert.Equal(BrainSettings.SameEffort, controller.Settings.BossEffort);
        var again = LoadBot();
        Assert.Equal("max", again.EliteEffort);
        Assert.Equal(BrainSettings.SameEffort, again.BossEffort);
        Assert.Equal("""{ "model": "sonnet" }""", File.ReadAllText(Path.Combine(Shared, "config.json")));
    }

    [Fact]
    public void An_instance_fight_effort_back_at_its_default_overrides_the_shared_one()
    {
        Write(Shared, "config.json", """{ "elite_effort": "low" }""");
        var chat = new ChatHub { BotName = "Silent" };
        var controller = NewController(chat);
        Assert.Equal("low", controller.Settings.EliteEffort);

        chat.PostFromHuman("Mark", "/effort elite default");

        Assert.Equal(BrainSettings.DefaultWord, ReadJson(Bot)["elite_effort"]!.GetValue<string>());
        Assert.Null(LoadBot().EliteEffort);
        Assert.False(ReadJson(Bot).ContainsKey("boss_effort"));
    }

    private BrainController NewController(ChatHub chat) => new(
        loadSettings: () => LoadBot(),
        saveSettings: s => s.Save(),
        createApi: _ => throw new InvalidOperationException("no brain in this test"),
        createSubscription: _ => throw new InvalidOperationException("no brain in this test"),
        currentScreen: _ => Task.FromResult<string?>("menu"),
        spend: new SpendTracker(Shared),
        chat: chat);
}
