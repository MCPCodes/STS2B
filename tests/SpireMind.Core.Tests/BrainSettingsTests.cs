using System.Text.Json.Nodes;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

public class BrainSettingsTests : IDisposable
{
    private const string FileKey = "sk-ant-api03-FILEKEY-abcdef0123456789";
    private const string EnvKey = "sk-ant-api03-ENVKEY-9876543210fedcba";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spiremind-settings-" + Guid.NewGuid().ToString("N"));

    public BrainSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private static Func<string, string?> Env(string? key = null) =>
        name => name == "ANTHROPIC_API_KEY" ? key : null;

    private BrainSettings Load(string? envKey = null) => BrainSettings.Load(_dir, Env(envKey));

    private void WriteConfig(string json) => File.WriteAllText(Path.Combine(_dir, "config.json"), json);

    private void WriteKey(string text, UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite)
    {
        var path = Path.Combine(_dir, "api_key");
        File.WriteAllText(path, text);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, mode);
    }

    [Fact]
    public void Defaults_without_any_file()
    {
        var s = Load();
        Assert.Equal(_dir, s.ConfigDir);
        Assert.Equal("claude-opus-5-5", s.Model);
        Assert.Equal("medium", s.Effort);
        Assert.Equal(200m, s.MonthlyBudgetUsd);
        Assert.False(s.Autostart);
        Assert.Null(s.Repo);
        Assert.Null(s.ClaudePath);
        Assert.Null(s.MaxTokens);
        Assert.Null(s.ApiKey);
        Assert.False(s.HasApiKey);
        Assert.Equal(ApiKeySource.None, s.ApiKeySource);
        Assert.Empty(s.Warnings);
        Assert.Equal(Path.Combine(_dir, "config.json"), s.ConfigPath);
    }

    [Fact]
    public void Default_folder_is_dot_config_spiremind()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, ".config", "spiremind"), BrainSettings.DefaultConfigDir);
        Assert.Equal(BrainSettings.DefaultConfigDir, new BrainSettings().ConfigDir);
    }

    [Theory]
    [InlineData("opus", "claude-opus-5-5")]
    [InlineData("sonnet", "claude-sonnet-5-5")]
    [InlineData("haiku", "claude-haiku-5-5")]
    [InlineData("fable", "claude-fable-5-1")]
    [InlineData("  Sonnet ", "claude-sonnet-5-5")]
    [InlineData("HAIKU", "claude-haiku-5-5")]
    [InlineData("claude-sonnet-5-5", "claude-sonnet-5-5")]
    [InlineData("Claude-Opus-6", "claude-opus-6")]
    [InlineData("", "claude-opus-5-5")]
    [InlineData(null, "claude-opus-5-5")]
    public void Model_aliases_resolve_to_full_ids(string? given, string expected)
    {
        Assert.Equal(expected, BrainSettings.ResolveModel(given));
        Assert.Equal(expected, new BrainSettings { Model = given! }.Model);
    }

    [Fact]
    public void Try_resolve_model_refuses_models_without_prices()
    {
        Assert.True(BrainSettings.TryResolveModel("sonnet", out var sonnet));
        Assert.Equal("claude-sonnet-5-5", sonnet);
        Assert.True(BrainSettings.TryResolveModel("claude-haiku-5-5", out _));
        Assert.False(BrainSettings.TryResolveModel("gpt-5", out _));
        Assert.False(BrainSettings.TryResolveModel(" ", out _));
    }

    [Fact]
    public void Effort_accepts_only_api_values()
    {
        Assert.True(BrainSettings.TryParseEffort(" HIGH ", out var high));
        Assert.Equal("high", high);
        Assert.True(BrainSettings.TryParseEffort("xhigh", out _));
        Assert.True(BrainSettings.TryParseEffort("max", out _));
        Assert.False(BrainSettings.TryParseEffort("extreme", out var fallback));
        Assert.Equal("medium", fallback);

        var s = new BrainSettings();
        Assert.Equal("low", (s with { Effort = "Low" }).Effort);
        Assert.Throws<ArgumentException>(() => s with { Effort = "extreme" });
        Assert.Throws<ArgumentOutOfRangeException>(() => s with { MonthlyBudgetUsd = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => s with { MaxTokens = 1000 });
        Assert.Throws<ArgumentOutOfRangeException>(() => s with { MaxTokens = 128_001 });
        Assert.Equal(128_000, (s with { MaxTokens = 128_000 }).MaxTokens);
    }

    [Fact]
    public void Loads_every_field_from_config_json()
    {
        WriteConfig("""
            {
              // comments and trailing commas are tolerated
              "repo": "/Users/mark/Projects/STS2B",
              "model": "sonnet",
              "effort": "high",
              "monthly_budget_usd": 42.5,
              "max_tokens": 48000,
              "autostart": true,
              "claude_path": "/opt/homebrew/bin/claude",
            }
            """);

        var s = Load();

        Assert.Equal("/Users/mark/Projects/STS2B", s.Repo);
        Assert.Equal("claude-sonnet-5-5", s.Model);
        Assert.Equal("high", s.Effort);
        Assert.Equal(42.5m, s.MonthlyBudgetUsd);
        Assert.Equal(48000, s.MaxTokens);
        Assert.True(s.Autostart);
        Assert.Equal("/opt/homebrew/bin/claude", s.ClaudePath);
        Assert.Empty(s.Warnings);
    }

    [Fact]
    public void A_leading_tilde_means_home()
    {
        WriteConfig("""{ "repo": "~/Projects/STS2B", "claude_path": "~/.local/bin/claude" }""");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var s = Load();
        Assert.Equal(home + "/Projects/STS2B", s.Repo);
        Assert.Equal(home + "/.local/bin/claude", s.ClaudePath);
    }

    [Fact]
    public void Bad_values_fall_back_to_defaults_with_warnings()
    {
        WriteConfig("""
            { "model": 5, "effort": "extreme", "monthly_budget_usd": -10, "autostart": "yes", "modle": "haiku", "repo": "  ", "max_tokens": 500 }
            """);

        var s = Load();

        Assert.Equal("claude-opus-5-5", s.Model);
        Assert.Equal("medium", s.Effort);
        Assert.Equal(200m, s.MonthlyBudgetUsd);
        Assert.False(s.Autostart);
        Assert.Null(s.Repo);
        Assert.Null(s.MaxTokens);
        Assert.Equal(6, s.Warnings.Count);
        Assert.Contains(s.Warnings, w => w.Contains("model must be a string"));
        Assert.Contains(s.Warnings, w => w.Contains("effort 'extreme'"));
        Assert.Contains(s.Warnings, w => w.Contains("monthly_budget_usd"));
        Assert.Contains(s.Warnings, w => w.Contains("autostart"));
        Assert.Contains(s.Warnings, w => w.Contains("'modle'"));
        Assert.Contains(s.Warnings, w => w.Contains("max_tokens must be a whole number from 1024 to 128000"));
    }

    [Fact]
    public void Fight_efforts_default_to_high_but_never_below_the_normal_effort()
    {
        var s = Load();
        Assert.Null(s.EliteEffort);
        Assert.Null(s.BossEffort);
        Assert.Equal("medium", s.EffortFor(null));
        Assert.Equal("medium", s.EffortFor("Monster"));
        Assert.Equal("high", s.EffortFor("Elite"));
        Assert.Equal("high", s.EffortFor("BOSS"));
        Assert.Equal("xhigh", (s with { Effort = "xhigh" }).EffortFor("Boss"));
        Assert.Equal("high", (s with { Effort = "low" }).EffortFor("Elite"));
    }

    [Fact]
    public void Fight_efforts_from_config_are_used_as_chosen()
    {
        WriteConfig("""{ "effort": "high", "elite_effort": "Medium", "boss_effort": null }""");
        var s = Load();
        Assert.Empty(s.Warnings);
        Assert.Equal("medium", s.EliteEffort);
        Assert.Equal("same", s.BossEffort); // null means the normal effort
        Assert.Equal("medium", s.EffortFor("Elite")); // below the normal effort, as chosen
        Assert.Equal("high", s.EffortFor("Boss"));
        Assert.Equal("low", (s with { Effort = "low" }).EffortFor("Boss"));
        Assert.Equal("max", (s with { BossEffort = "max" }).EffortFor("Boss"));
        Assert.Contains("EliteEffort = medium, BossEffort = same", s.ToString());
    }

    [Fact]
    public void Bad_fight_efforts_fall_back_to_the_default_with_a_warning()
    {
        WriteConfig("""{ "elite_effort": "extreme", "boss_effort": 3 }""");
        var s = Load();
        Assert.Null(s.EliteEffort);
        Assert.Null(s.BossEffort);
        Assert.Equal(2, s.Warnings.Count);
        Assert.Contains(s.Warnings, w => w.StartsWith("elite_effort must be one of"));
        Assert.Contains(s.Warnings, w => w.StartsWith("boss_effort must be one of"));
        Assert.Throws<ArgumentException>(() => s with { BossEffort = "extreme" });
        Assert.True(BrainSettings.TryParseFightEffort(" Default ", out var cleared));
        Assert.Null(cleared);
        Assert.True(BrainSettings.TryParseFightEffort("SAME", out var same));
        Assert.Equal("same", same);
        Assert.False(BrainSettings.TryParseFightEffort("loud", out _));
    }

    [Fact]
    public void Fight_efforts_are_saved_only_when_chosen()
    {
        var chosen = Load() with { EliteEffort = "xhigh", BossEffort = "same" };
        chosen.Save();
        var json = JsonNode.Parse(File.ReadAllText(chosen.ConfigPath))!.AsObject();
        Assert.Equal("xhigh", json["elite_effort"]!.GetValue<string>());
        Assert.Equal("same", json["boss_effort"]!.GetValue<string>());
        var again = Load();
        Assert.Equal("xhigh", again.EliteEffort);
        Assert.Equal("same", again.BossEffort);

        (again with { EliteEffort = null, BossEffort = null }).Save();
        json = JsonNode.Parse(File.ReadAllText(chosen.ConfigPath))!.AsObject();
        Assert.False(json.ContainsKey("elite_effort"));
        Assert.False(json.ContainsKey("boss_effort"));
    }

    [Fact]
    public void Turn_plan_switches_are_off_by_default_and_round_trip_through_config_json()
    {
        var s = Load();
        Assert.False(s.TurnPlanNote);
        Assert.Null(s.TurnPlanEffort);
        Assert.Contains("TurnPlanNote = False, TurnPlanEffort = off", s.ToString());

        // Off, they are left out of the file.
        s.Save();
        var json = JsonNode.Parse(File.ReadAllText(s.ConfigPath))!.AsObject();
        Assert.False(json.ContainsKey("turn_plan_note"));
        Assert.False(json.ContainsKey("turn_plan_effort"));

        var on = s with { TurnPlanNote = true, TurnPlanEffort = " XHigh " };
        Assert.Equal("xhigh", on.TurnPlanEffort);
        on.Save();
        json = JsonNode.Parse(File.ReadAllText(s.ConfigPath))!.AsObject();
        Assert.True(json["turn_plan_note"]!.GetValue<bool>());
        Assert.Equal("xhigh", json["turn_plan_effort"]!.GetValue<string>());
        var again = Load();
        Assert.Empty(again.Warnings);
        Assert.True(again.TurnPlanNote);
        Assert.Equal("xhigh", again.TurnPlanEffort);
        Assert.Contains("TurnPlanNote = True, TurnPlanEffort = xhigh", again.ToString());

        (again with { TurnPlanNote = false, TurnPlanEffort = null }).Save();
        json = JsonNode.Parse(File.ReadAllText(s.ConfigPath))!.AsObject();
        Assert.False(json.ContainsKey("turn_plan_note"));
        Assert.False(json.ContainsKey("turn_plan_effort"));
        Assert.False(Load().TurnPlanNote);
        Assert.Null(Load().TurnPlanEffort);
    }

    [Fact]
    public void Turn_plan_switches_load_from_config_json()
    {
        WriteConfig("""{ "turn_plan_note": true, "turn_plan_effort": " Max " }""");
        var s = Load();
        Assert.Empty(s.Warnings);
        Assert.True(s.TurnPlanNote);
        Assert.Equal("max", s.TurnPlanEffort);

        // JSON null is the same as leaving the key out.
        WriteConfig("""{ "turn_plan_note": false, "turn_plan_effort": null }""");
        s = Load();
        Assert.Empty(s.Warnings);
        Assert.False(s.TurnPlanNote);
        Assert.Null(s.TurnPlanEffort);
    }

    [Fact]
    public void Bad_turn_plan_values_leave_the_switches_off_with_a_warning()
    {
        WriteConfig("""{ "turn_plan_note": "yes", "turn_plan_effort": "extreme" }""");
        var s = Load();
        Assert.False(s.TurnPlanNote);
        Assert.Null(s.TurnPlanEffort);
        Assert.Equal(2, s.Warnings.Count);
        Assert.Contains("turn_plan_note must be true or false; using false.", s.Warnings);
        Assert.Contains("turn_plan_effort must be one of low, medium, high, xhigh, max or null; leaving it off.", s.Warnings);

        WriteConfig("""{ "turn_plan_effort": 3 }""");
        Assert.StartsWith("turn_plan_effort must be one of", Assert.Single(Load().Warnings));
        WriteConfig("""{ "turn_plan_effort": "same" }""");
        Assert.Null(Load().TurnPlanEffort);

        Assert.Throws<ArgumentException>(() => s with { TurnPlanEffort = "extreme" });
        Assert.Throws<ArgumentException>(() => s with { TurnPlanEffort = "same" });
        Assert.Throws<ArgumentException>(() => s with { TurnPlanEffort = "" });
    }

    [Fact]
    public void A_fight_room_holds_through_its_own_screens_until_the_bot_leaves()
    {
        static JsonObject Snap(string screen, string? room = null, bool inState = false)
        {
            var state = new JsonObject();
            if (room is not null && inState) state["room"] = room;
            else if (room is not null) state["run"] = new JsonObject { ["room"] = room };
            return new JsonObject { ["screen"] = screen, ["state"] = state };
        }

        Assert.Equal("Elite", RunScreens.FightRoom(null, Snap("combat", "Elite")));
        Assert.Equal("Boss", RunScreens.FightRoom(null, Snap("combat", "boss")));
        Assert.Null(RunScreens.FightRoom(null, Snap("combat", "Monster")));
        Assert.Null(RunScreens.FightRoom("Elite", Snap("combat", "Monster")));
        foreach (var screen in new[] { "combat", "hand_select", "rewards", "card_reward", "card_select", "modal", "unknown" })
            Assert.Equal("Elite", RunScreens.FightRoom("Elite", Snap(screen)));
        // The treasure screen is a Treasure room's (in co-op the bot may not see the map in between).
        foreach (var screen in new[] { "map", "shop", "rest", "event", "treasure", "crystal_sphere", "menu", "game_over" })
            Assert.Null(RunScreens.FightRoom("Boss", Snap(screen)));
        Assert.Null(RunScreens.FightRoom("Boss", Snap("game_over", "Boss", inState: true)));
        Assert.Null(RunScreens.FightRoom(null, Snap("rewards")));
        var flagged = Snap("modal");
        flagged["in_run"] = false;
        Assert.Null(RunScreens.FightRoom("Elite", flagged));
    }

    [Fact]
    public void A_model_without_prices_is_kept_with_a_warning()
    {
        WriteConfig("""{ "model": "claude-opus-6" }""");
        var s = Load();
        Assert.Equal("claude-opus-6", s.Model);
        Assert.Contains("price table", Assert.Single(s.Warnings));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    public void Unreadable_config_gives_defaults_and_a_warning(string text)
    {
        WriteConfig(text);
        var s = Load();
        Assert.Equal("claude-opus-5-5", s.Model);
        Assert.Equal(200m, s.MonthlyBudgetUsd);
        Assert.Contains("config.json", Assert.Single(s.Warnings));
    }

    [Fact]
    public void Save_round_trips_and_keeps_unknown_keys()
    {
        WriteConfig("""{ "repo": "/old", "theme": "dark", "model": "haiku" }""");
        var loaded = Load();
        Assert.Single(loaded.Warnings); // the unknown "theme" key

        var changed = loaded with { Repo = null, Model = "fable", Effort = "low", MonthlyBudgetUsd = 12.34m, MaxTokens = 40000, Autostart = true, ClaudePath = "/usr/local/bin/claude" };
        changed.Save();

        var json = JsonNode.Parse(File.ReadAllText(changed.ConfigPath))!.AsObject();
        Assert.False(json.ContainsKey("repo"));
        Assert.Equal("dark", json["theme"]!.GetValue<string>());
        Assert.Equal("claude-fable-5-1", json["model"]!.GetValue<string>());
        Assert.Equal(12.34m, json["monthly_budget_usd"]!.GetValue<decimal>());
        Assert.Equal(40000, json["max_tokens"]!.GetValue<int>());

        var again = Load();
        Assert.Equal(40000, again.MaxTokens);
        Assert.Equal(changed.Model, again.Model);
        Assert.Equal(changed.Effort, again.Effort);
        Assert.Equal(changed.MonthlyBudgetUsd, again.MonthlyBudgetUsd);
        Assert.Equal(changed.Autostart, again.Autostart);
        Assert.Equal(changed.ClaudePath, again.ClaudePath);
        Assert.Null(again.Repo);
        Assert.Equal(["config.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));

        // Back to sizing it from the effort: the key goes.
        (again with { MaxTokens = null }).Save();
        Assert.False(JsonNode.Parse(File.ReadAllText(changed.ConfigPath))!.AsObject().ContainsKey("max_tokens"));
        Assert.Null(Load().MaxTokens);
    }

    [Fact]
    public void Save_creates_the_folder_and_never_writes_the_key()
    {
        var dir = Path.Combine(_dir, "fresh", "spiremind");
        var s = new BrainSettings { ConfigDir = dir, ApiKey = FileKey, ApiKeySource = ApiKeySource.File, Autostart = true };

        s.Save();

        var text = File.ReadAllText(Path.Combine(dir, "config.json"));
        Assert.DoesNotContain(FileKey, text);
        Assert.DoesNotContain("key", text);
        Assert.False(File.Exists(Path.Combine(dir, "api_key")));
        Assert.True(BrainSettings.Load(dir, Env()).Autostart);
    }

    [Fact]
    public void Save_over_a_corrupt_config_keeps_a_backup()
    {
        WriteConfig("{ \"model\": \"sonnet\", oops");
        var s = Load() with { Autostart = true };

        s.Save();

        Assert.Equal("{ \"model\": \"sonnet\", oops", File.ReadAllText(Path.Combine(_dir, "config.json.bak")));
        Assert.True(Load().Autostart);
    }

    [Fact]
    public void Key_comes_from_the_file_first_line_trimmed()
    {
        WriteKey($"\n  {FileKey}  \r\nsecond line\n");
        var s = Load(envKey: EnvKey);
        Assert.Equal(FileKey, s.ApiKey);
        Assert.Equal(ApiKeySource.File, s.ApiKeySource);
        Assert.True(s.HasApiKey);
        Assert.Empty(s.Warnings);
    }

    [Fact]
    public void Key_comes_from_the_environment_without_a_file()
    {
        var s = Load(envKey: $"  {EnvKey}\n");
        Assert.Equal(EnvKey, s.ApiKey);
        Assert.Equal(ApiKeySource.Environment, s.ApiKeySource);
        Assert.True(s.HasApiKey);
    }

    [Fact]
    public void An_empty_key_file_falls_back_to_the_environment()
    {
        WriteKey("  \n\n");
        var s = Load(envKey: EnvKey);
        Assert.Equal(EnvKey, s.ApiKey);
        Assert.Equal(ApiKeySource.Environment, s.ApiKeySource);
        Assert.Contains("empty", Assert.Single(s.Warnings));
    }

    [Theory]
    [InlineData("sk-ant api03 with spaces")]
    [InlineData("\"sk-ant-api03-quoted\"")]
    public void A_malformed_key_is_ignored_without_repeating_it(string key)
    {
        WriteKey(key);
        var s = Load();
        Assert.False(s.HasApiKey);
        Assert.Equal(ApiKeySource.None, s.ApiKeySource);
        var warning = Assert.Single(s.Warnings);
        Assert.DoesNotContain(key, warning);
        Assert.DoesNotContain("api03", warning);
    }

    [Fact]
    public void A_key_file_others_can_read_gets_a_warning()
    {
        if (OperatingSystem.IsWindows())
            return;
        WriteKey(FileKey, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        var s = Load();
        Assert.Equal(FileKey, s.ApiKey);
        var warning = Assert.Single(s.Warnings);
        Assert.Contains("chmod 600", warning);
        Assert.DoesNotContain(FileKey, warning);
    }

    [Fact]
    public void Fast_mode_is_off_by_default_and_never_read_from_or_written_to_config_json()
    {
        Assert.False(new BrainSettings().Fast);
        Assert.False(Load().Fast);

        // Saving with it on writes nothing about it, so the next start is at normal speed.
        var on = Load() with { Fast = true, Model = "opus", Autostart = true };
        Assert.True(on.Fast);
        on.Save();
        var text = File.ReadAllText(on.ConfigPath);
        Assert.DoesNotContain("fast", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("speed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["model", "effort", "monthly_budget_usd", "autostart"], JsonNode.Parse(text)!.AsObject().Select(p => p.Key));
        var again = Load();
        Assert.False(again.Fast);
        Assert.True(again.Autostart);
        Assert.Empty(again.Warnings);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("\"on\"")]
    [InlineData("1")]
    [InlineData("false")]
    public void A_fast_key_typed_into_config_json_is_ignored_with_a_warning(string value)
    {
        WriteConfig($$"""{ "model": "opus", "fast": {{value}} }""");

        var s = Load();

        Assert.False(s.Fast);
        Assert.Equal("'fast' in config.json is ignored: fast mode is off whenever the game starts; type /fast on in the chat.", Assert.Single(s.Warnings));
        // Saving leaves the hand-typed key where it is, as with any key the mod doesn't use, and it still means nothing.
        (s with { Fast = true }).Save();
        Assert.Equal(value, JsonNode.Parse(File.ReadAllText(s.ConfigPath))!["fast"]!.ToJsonString());
        Assert.False(Load().Fast);
    }

    [Fact]
    public void Fast_mode_shows_in_to_string_only_while_on()
    {
        Assert.DoesNotContain("Fast", Load().ToString());
        Assert.Contains("Effort = medium, Fast = True, ", (Load() with { Fast = true }).ToString());
    }

    [Fact]
    public void The_key_never_shows_in_to_string()
    {
        WriteKey(FileKey);
        var fromFile = Load();
        Assert.DoesNotContain(FileKey, fromFile.ToString());
        Assert.Contains("ApiKey = from api_key", fromFile.ToString());
        Assert.Contains("Model = claude-opus-5-5", fromFile.ToString());

        File.Delete(Path.Combine(_dir, "api_key"));
        var fromEnv = Load(envKey: EnvKey);
        Assert.DoesNotContain(EnvKey, fromEnv.ToString());
        Assert.Contains("ApiKey = from ANTHROPIC_API_KEY", fromEnv.ToString());

        Assert.DoesNotContain(EnvKey, $"{fromEnv with { Autostart = true }}");
        Assert.Contains("ApiKey = none", Load().ToString());
    }
}
