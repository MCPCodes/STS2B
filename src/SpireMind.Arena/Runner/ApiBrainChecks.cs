using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SpireMind.Core;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>
/// <c>--brain api</c>, checked without one call to the real API and without reading a key: the
/// mod's real brain (<see cref="ApiBrain"/>) and Core's real client (<see cref="ClaudeClient"/>)
/// play whole fights against a scripted transport (<see cref="FakeApi"/>) that reaches no
/// network. Every folder is a temp folder made here: a small checkout, and a config folder
/// that stands for Mark's.
/// <list type="bullet">
/// <item>The options, and that the brain is part of a run's label.</item>
/// <item>The settings: read by Core's own code from a config folder without opening its key
/// file, and what the command line overrides.</item>
/// <item>A fight of each kind of room: the system prompt is the one the game builds, byte for
/// byte; the arena's facts are one line of the mod in the first message; the requests carry
/// the room's effort and max_tokens (an elite and a boss at their own, and at overridden
/// ones); the tools are the game's and the notes tools; the notes written land in the
/// throwaway copy and in the result, and the checkout is untouched; the cost is the brain's
/// own spend tracker's; the brain is stopped cleanly once the fight is over.</item>
/// <item>A model that stops playing: brought back by the brain's own "Keep playing", or given
/// up on as the game gives up on it.</item>
/// <item>The batch: nothing starts without --yes-spend; the budget stops it between fights and
/// inside one; a missing key and a refused key say so and start nothing more; the same
/// command again plays only what is missing.</item>
/// <item>The report and compare, which name the brain and never pool the two.</item>
/// </list>
/// </summary>
public sealed class ApiBrainChecks : IArenaChecks
{
    private static readonly ApiTimings Fast = new(
        ActionDelay: TimeSpan.FromMilliseconds(2), WaiterPoll: TimeSpan.FromMilliseconds(2), WaiterSettle: TimeSpan.FromMilliseconds(6),
        PollInterval: TimeSpan.FromMilliseconds(5), WaitHold: TimeSpan.FromMilliseconds(20));

    // What the stand-in's fixed usage costs at the price of a model the table doesn't know.
    private static readonly decimal FirstCost = Price(FakeApi.Notes, first: true), LaterCost = Price(FakeApi.Notes, first: false);

    public void Run(Check check)
    {
        check.Group("api options", () => Options(check));
        check.Group("api label", () => Labels(check));
        check.Group("api note", () => Note(check));
        check.Group("api settings", () => Settings(check));
        check.Group("api rooms", () => Rooms(check));
        check.Group("api fight", () => Fights(check));
        check.Group("api efforts overridden", () => Overrides(check));
        check.Group("api real model id", () => RealModel(check));
        check.Group("api after the fight", () => AfterTheFight(check));
        check.Group("api keep playing", () => KeepPlaying(check));
        check.Group("api batch", () => Batch(check));
        check.Group("api same as the game's wiring", () => SameAsTheGame(check));
        check.Group("api budget", () => Budget(check));
        check.Group("api budget of the folder", () => FolderBudget(check));
        check.Group("api monthly cap setting", () => MonthlyCap(check));
        check.Group("api only", () => OnlyNamed(check));
        check.Group("api key", () => Keys(check));
        check.Group("api report", () => Reports(check));
    }

    // ---------- the command line ----------

    private static void Options(Check check)
    {
        var plain = ArenaOptions.Parse(["run", "--models", "claude-x"]);
        check.Equal(Brains.ClaudeCode, plain.Brain, "the default brain");
        check.Equal("base", plain.Experiment, "a plain run's label is what it was");
        check.That(!plain.YesSpend && !plain.FakeApiTransport, "nothing that spends is on by default");

        var api = ArenaOptions.Parse(["run", "--brain", "api"]);
        check.Equal(Brains.Api, api.Brain, "--brain api");
        check.Equal(0, api.Models.Count, "the API brain needs no --models: it plays with the game's own model");
        check.Equal(5m, api.MaxUsd, "the default budget");
        check.Equal(1, api.AfterFight, "the default number of requests after a fight");
        check.That(!api.YesSpend, "--brain api alone doesn't say yes");

        var full = ArenaOptions.Parse(["run", "--brain", "api", "--yes-spend", "--model", "opus", "--effort", "low", "--elite-effort", "same", "--boss-effort", "max",
            "--max-usd", "0.50", "--after-fight", "0", "--scenarios", "4"]);
        check.That(full.YesSpend, "--yes-spend");
        check.Sequence(["opus"], full.Models, "--model names the one model");
        check.Equal(0.50m, full.MaxUsd, "--max-usd");
        check.Equal("same", full.EliteEffort, "--elite-effort");
        check.Equal("max", full.BossEffort, "--boss-effort");
        check.Equal(0, full.AfterFight, "--after-fight");
        check.That(full.Given.Contains("--effort") && !api.Given.Contains("--effort"), "what was typed is remembered");

        var position = ArenaOptions.Parse(["position", "/runs/x", "boss-1", "claude-x", "--turn", "3", "--brain", "api", "--label", "new"]);
        check.Equal(Brains.Api, position.Brain, "position takes --brain");
        check.Equal("new+api", position.Experiment, "a position test's name, then the brain");

        foreach (var bad in new[]
                 {
                     new[] { "run", "--brain", "api", "--brief" }, ["run", "--brain", "api", "--effort-by-tier", "elite=high"], ["run", "--brain", "api", "--claude", "/bin/x"],
                     ["run", "--brain", "api", "--stop-at-weekly", "0.5"], ["run", "--brain", "api", "--grace-seconds", "30"],
                     ["run", "--models", "claude-x", "--yes-spend"], ["run", "--models", "claude-x", "--max-usd", "3"], ["run", "--models", "claude-x", "--elite-effort", "high"],
                     ["run", "--models", "claude-x", "--fake-api"], ["run", "--models", "claude-x", "--after-fight", "1"],
                     ["run", "--brain", "gpt"], ["run", "--brain", "api", "--max-usd", "0"], ["run", "--brain", "api", "--max-usd", "1000"], ["run", "--brain", "api", "--max-usd", "five"],
                     ["run", "--brain", "api", "--elite-effort", "huge"], ["run", "--brain", "api", "--model", "a", "--models", "b"], ["run", "--brain", "api", "--after-fight", "9"],
                     ["baseline", "--brain", "api"], ["report", "/x", "--yes-spend"],
                 })
        {
            var refused = false;
            try { ArenaOptions.Parse(bad); }
            catch (UsageException) { refused = true; }
            check.That(refused, $"'{string.Join(' ', bad)}' is refused");
        }
        check.That(ArenaOptions.Usage.Contains("--brain api --yes-spend", StringComparison.Ordinal), "the usage names the API brain and its yes");
    }

    // The one text a model gets here that the game doesn't give it, written out so that a change to it is a decision.
    private static void Note(Check check)
    {
        const string note =
            "SpireMind arena: this is one practice fight in a simulator of the game's combat (v0.107.1), with the game's rules, snapshots and tools. " +
            "There is no map, reward or run around it: win it, with as much HP left as you can. " +
            "Card, power and potion texts are worded by the arena, so go by the snapshot where it and your memory differ. " +
            "The fight counts as lost if it isn't won by the end of turn 40. " +
            "When it is over the screen is `arena_over` and nothing follows.";
        check.Equal(note, ApiFight.ArenaNote(), "the arena's line");
        check.Equal(note + " This fight is already under way: you take it over at the start of turn 4, and the turns before it cannot be changed.",
            ApiFight.ArenaNote(4), "the arena's line for a position run");
        // The chat hands it to the brain as the mod's own line, the way BrainWiring hands over the game's chat.
        var chat = ClaudeFight.NewChat();
        chat.Announce(ApiFight.ArenaNote());
        chat.Say("For the fallen.");
        check.Sequence(["(mod) " + note, "Ironclad: For the fallen."], ApiFight.RecentChat(chat), "the recent chat as the brain gets it");

        // In the repo's own checkout the config's repo comes first, as in the game.
        using var world = new World();
        using var other = new World();
        world.Config("config.json", "{\"repo\": \"" + other.Repo + "\"}");
        var (code, said) = world.Batch(["run", "--brain", "api", "--model", FakeApi.Play, "--scenarios", "1", "--out", world.Out], transport: null);
        check.That(code == 2 && said.Contains($"built from {other.Repo} (", StringComparison.Ordinal), "without --repo the prompt is built from config.json's repo, as the game builds it");
        (code, said) = world.Batch(["run", "--brain", "api", "--model", FakeApi.Play, "--scenarios", "1", "--out", world.Out, "--repo", world.Repo], transport: null);
        check.That(code == 2 && said.Contains($"built from {world.Repo} (", StringComparison.Ordinal), "--repo wins over it");
        (code, said) = world.Batch(["run", "--brain", "api", "--scenarios", "1", "--out", world.Out, "--repo", Path.Combine(world.Repo, "strategy")], transport: null);
        check.That(code == 2 && said.Contains("has no .claude/commands/play.md", StringComparison.Ordinal), "a --repo without the play prompt is refused");
    }

    private static void Labels(Check check)
    {
        var off = new ExperimentOptions();
        var none = new Dictionary<string, string>();
        check.Equal("base", ExperimentLabel.Of(false, off, none, null, Brains.ClaudeCode), "Claude Code adds nothing to a label");
        check.Equal("api", ExperimentLabel.Of(false, off, none, null, Brains.Api), "the API brain is a part of the label");
        check.Equal("api+forecast+elite=high+boss=high",
            ExperimentLabel.Of(false, new ExperimentOptions(true), new Dictionary<string, string> { ["boss"] = "high", ["elite"] = "high" }, null, Brains.Api),
            "the brain first, then the switches and the rooms' efforts");
        check.Equal("old+api", ExperimentLabel.Of(false, off, none, "old", Brains.Api), "a batch's own name comes before the brain");
        check.Equal(Brains.Api, ExperimentLabel.BrainOf("old+api+elite=high"), "the brain read back from a label");
        check.Equal(Brains.ClaudeCode, ExperimentLabel.BrainOf("brief+elite=high"), "a label without it is Claude Code's");
        check.Equal(Brains.ClaudeCode, ExperimentLabel.BrainOf("base"), "base is Claude Code's");
        check.That(ExperimentLabel.NameProblem("api") is not null, "'api' can't be a batch's own name");
        check.Equal("high", ExperimentLabel.TierEffort("api+elite=high+boss=xhigh", "elite"), "a room's effort read back from an API label");

        // The same fight under the other brain is another fight: neither batch skips the other's.
        var weak = new Scenario("weak-1", 1, "weak", "a weak fight");
        var elite = new Scenario("elite-2", 2, "elite", "an elite");
        var apiOptions = ApiOptions(new BrainSettings(), ["run", "--brain", "api", "--model", "claude-x"]);
        var claudeOptions = ArenaOptions.Parse(["run", "--models", "claude-x"]);
        FightResult[] byClaude = [Line(weak, "medium", "base", Brains.ClaudeCode), Line(elite, "medium", "base", Brains.ClaudeCode)];
        FightResult[] byApi = [Line(weak, "medium", apiOptions.Experiment, Brains.Api), Line(elite, "high", apiOptions.Experiment, Brains.Api)];
        check.Equal("api+elite=high+boss=high", apiOptions.Experiment, "the label of an API batch with the game's default efforts");
        check.That(!RunCommand.AlreadyPlayed(byClaude, apiOptions)(weak, "claude-x") && !RunCommand.AlreadyPlayed(byClaude, apiOptions)(elite, "claude-x"),
            "an API batch plays the fights Claude Code already played");
        check.That(!RunCommand.AlreadyPlayed(byApi, claudeOptions)(weak, "claude-x"), "and Claude Code's batch plays the ones the API brain played");
        check.That(RunCommand.AlreadyPlayed(byApi, apiOptions)(weak, "claude-x") && RunCommand.AlreadyPlayed(byApi, apiOptions)(elite, "claude-x"),
            "the same API command again skips its own fights, the elite at its own effort");
        check.That(!RunCommand.AlreadyPlayed(byApi, ApiOptions(new BrainSettings { EliteEffort = "max" }, ["run", "--brain", "api", "--model", "claude-x"]))(elite, "claude-x"),
            "another elite effort plays the elite fight again");

        check.Equal("elite-2__claude-x__api_elite_high_boss_high.jsonl", RunCommand.TranscriptName("elite-2", "claude-x", apiOptions.Experiment),
            "an API fight's transcript has its own name, never the plain run's");
    }

    // ---------- the settings ----------

    private static void Settings(Check check)
    {
        using var world = new World();
        // The config folder holds a key file. Reading the settings must not open it.
        world.Config("config.json", "{\"model\": \"sonnet\", \"effort\": \"low\", \"elite_effort\": \"same\", \"monthly_budget_usd\": 77}");
        world.Config("api_key", "sk-check-this-is-not-a-key\n");
        var settings = ApiSettings.WithoutKey(world.Places);
        check.That(!settings.HasApiKey && settings.ApiKey is null, "the settings a batch plans with hold no key");
        check.Equal("claude-sonnet-5-5", settings.Model, "the model, read by Core's own parser");
        check.Equal("low", settings.Effort, "the effort");
        check.Equal("low", settings.EffortFor("Elite"), "elite_effort same: the normal effort");
        check.Equal("high", settings.EffortFor("Boss"), "boss_effort left out: high");
        check.That(!settings.ToString().Contains("sk-check", StringComparison.Ordinal), "nothing of the key file is in the settings");
        check.Sequence(["boss=high"], ApiSettings.EffortByTier(settings).Select(e => $"{e.Key}={e.Value}"), "the rooms whose effort differs from the normal one");
        check.Equal("effort low, elite rooms low, boss rooms high, max_tokens by effort (16000 at low, 32000 at high)", ApiSettings.Describe(settings), "the settings in words");

        var typed = ApiSettings.Apply(settings, ArenaOptions.Parse(["run", "--brain", "api", "--effort", "medium", "--elite-effort", "default", "--boss-effort", "max"]));
        check.Equal("medium", typed.Effort, "--effort overrides config.json");
        check.Equal("high", typed.EffortFor("Elite"), "--elite-effort default puts the mod's default back");
        check.Equal("max", typed.EffortFor("Boss"), "--boss-effort");
        check.Equal(64_000, ApiBrain.MaxTokensFor(typed.EffortFor("Boss")), "max_tokens at max");
        var untyped = ApiSettings.Apply(settings, ArenaOptions.Parse(["run", "--brain", "api"]));
        check.That(!ApiSettings.Differ(settings, untyped), "with nothing typed the settings are the game's");
        check.That(ApiSettings.Differ(settings, typed), "and typed ones differ");

        // No config.json at all: the mod's defaults, which is what Mark's game runs with today.
        using var bare = new World();
        var defaults = ApiSettings.WithoutKey(bare.Places);
        check.Equal(BrainSettings.DefaultModel, defaults.Model, "the default model");
        check.Equal("medium", defaults.Effort, "the default effort");
        check.Sequence(["elite=high", "boss=high"], ApiSettings.EffortByTier(defaults).Select(e => $"{e.Key}={e.Value}"), "the default room efforts");
        check.Equal(null, defaults.MaxTokens, "max_tokens by effort");
        // A normal effort above high lifts the rooms with it, so no room differs.
        check.Equal(0, ApiSettings.EffortByTier(defaults with { Effort = "xhigh" }).Count, "at xhigh every room is xhigh");
    }

    // The room a tier's fights are in is the one the simulator's snapshots name, which is what the brain reads.
    private static void Rooms(Check check)
    {
        foreach (var scenario in Scenarios.Generate(20, 3))
        {
            var snapshot = new ArenaGame(scenario).CaptureAsync(CancellationToken.None).GetAwaiter().GetResult();
            check.Equal(Brains.RoomOf(scenario.Tier), snapshot.State["run"]?["room"]?.GetValue<string>(), $"the room of {scenario.Id}'s first snapshot");
            check.Equal(RunScreens.FightRoom(null, snapshot.ToJson()) is { } room ? new BrainSettings().EffortFor(room) : "medium",
                new BrainSettings().EffortFor(Brains.RoomOf(scenario.Tier)), $"the effort the brain gives {scenario.Id}");
        }
    }

    // ---------- whole fights ----------

    private static void Fights(Check check)
    {
        var scenarios = Scenarios.Generate(10, 7);
        foreach (var (tier, effort, tokens) in new[] { ("weak", "medium", 16_000), ("elite", "high", 32_000), ("boss", "high", 32_000) })
        {
            var scenario = scenarios.First(s => s.Tier == tier);
            using var world = new World(scenario);
            var before = world.RepoHash();
            var fake = new FakeApi { Keep = true };
            var result = world.Play(scenario, new BrainSettings(), fake, FakeApi.Notes);
            var requests = fake.Requests;
            var what = $"{tier} fight";

            check.That(result.Finished, $"{what}: played to the end ({result.Outcome}: {result.Error})");
            check.Equal(Brains.Api, result.Brain, $"{what}: the result's brain");
            check.Equal(effort, result.Effort, $"{what}: the result's effort");
            check.That(requests.Count > 5, $"{what}: the brain sent requests ({requests.Count})");
            check.That(fake.OnlyTheFakeKey, $"{what}: no request carried anything but the stand-in's key");

            // The requests, as the game's brain sends them for this room.
            check.That(requests.All(r => Str(r["output_config"]?["effort"]) == effort), $"{what}: every request at effort {effort}");
            check.That(requests.All(r => r["max_tokens"]?.GetValue<int>() == tokens), $"{what}: every request with max_tokens {tokens}");
            check.That(requests.All(r => Str(r["model"]) == FakeApi.Notes), $"{what}: every request for the model asked");
            check.That(requests.All(r => r["temperature"] is null && r["tool_choice"] is null), $"{what}: no temperature and no tool_choice, as in the game");
            check.That(fake.Betas.All(beta => beta is null), $"{what}: no beta header without an effort message");

            // The system prompt is the one the game's brain builds from the checkout, and nothing else.
            var system = (requests[0]["system"] as JsonArray)![0]!;
            var game = PromptBuilder.Build(world.Repo, ArenaPrompt.Character, multiplayer: false, world.Repo).Text;
            check.Equal(game, Str(system["text"]), $"{what}: the system prompt is PromptBuilder's own, byte for byte");
            check.That(game.EndsWith(PromptBuilder.HarnessNotes + "\n", StringComparison.Ordinal), $"{what}: it ends with the brain's own appendix");
            check.That(!game.Contains("Playing in the arena", StringComparison.Ordinal) && !game.Contains("SpireMind arena", StringComparison.Ordinal),
                $"{what}: neither Claude Code's arena appendix nor the arena's note is in it");
            check.Equal("1h", Str(system["cache_control"]?["ttl"]), $"{what}: the system prompt is cached for an hour");
            check.That(requests.All(r => Str((r["system"] as JsonArray)![0]!["text"]) == game), $"{what}: the same system prompt on every request");
            check.Equal(ArenaPrompt.Hash(game), result.PromptHash, $"{what}: the result's prompt hash is that prompt's");

            // The tools: the game's, then the brain's notes tools, all strict.
            var tools = (requests[0]["tools"] as JsonArray)!.Select(t => Str(t!["name"])).ToList();
            check.Sequence(["get_state", "wait_for_decision", "act", "act_sequence", "say", "get_deck", "get_persona", "notes_read", "notes_write", "notes_append", "notes_list"],
                tools, $"{what}: the tools offered");
            check.That((requests[0]["tools"] as JsonArray)!.All(t => t!["strict"]?.GetValue<bool>() == true), $"{what}: strict tools");

            // The first message: the brain's own opening, with the arena's facts as a line of the mod.
            var first = Str(((requests[0]["messages"] as JsonArray)![0]!["content"] as JsonArray)![0]!["text"])!;
            check.That(first.StartsWith(ApiBrain.Starting + "\n\nToday is ", StringComparison.Ordinal), $"{what}: the brain's own opening");
            check.That(first.Contains("## Run journal (runs/current.md)\n\n(empty: the file doesn't exist yet)", StringComparison.Ordinal), $"{what}: no journal to start with");
            check.That(first.Contains("## Recent chat\n\n(mod) " + ApiFight.ArenaNote() + "\n\n", StringComparison.Ordinal), $"{what}: the arena's note is the one chat line");
            check.That(first.Contains($"\"room\":\"{Brains.RoomOf(tier)}\"", StringComparison.Ordinal), $"{what}: the first snapshot names the room");
            check.That(first.EndsWith(ApiBrain.CarryOn, StringComparison.Ordinal), $"{what}: it ends as the brain ends it");
            check.Equal(1, requests.Count(r => (r["messages"] as JsonArray)!.Count == 1), $"{what}: one conversation");

            // What the brain did, as the result tells it.
            var api = result.Api!;
            check.Equal(requests.Count, (int)api["requests"]!.GetValue<int>(), $"{what}: the requests counted");
            check.Equal($"{{\"{effort}\":{requests.Count}}}", api["efforts"]!.ToJsonString(), $"{what}: the efforts used, per request");
            check.Equal($"{{\"{tokens}\":{requests.Count}}}", api["max_tokens"]!.ToJsonString(), $"{what}: the max_tokens used, per request");
            check.Equal("UserStop", Str(api["stop"]), $"{what}: the brain stopped cleanly");
            check.Equal(ApiRecorder.FightOver, Str(api["stopped_for"]), $"{what}: because the fight was over");
            check.Equal(1, api["after_fight_requests"]!.GetValue<int>(), $"{what}: one request after the end");
            check.Equal(Brains.RoomOf(tier), Str(api["room"]), $"{what}: the room");
            check.Equal(ApiFight.ArenaNote(), Str(api["arena_note"]), $"{what}: the note it was given");
            check.Equal("fake", Str(api["key_source"]), $"{what}: where the key came from");

            // The cost is the brain's own spend tracker's, and it is what the usage comes to.
            var expected = FirstCost + (requests.Count - 1) * LaterCost;
            check.Equal(expected, result.TotalCostUsd, $"{what}: the cost, from the brain's spend tracker");
            check.Equal(expected, api["spend"]!["usd"]!.GetValue<decimal>(), $"{what}: the same in the api object");
            check.Equal(requests.Count, api["spend"]!["requests"]!.GetValue<int>(), $"{what}: the tracker's request count");
            check.Equal(requests.Count, result.NumTurns, $"{what}: num_turns is the request count");
            check.Equal(new TokenCounts(1000 + 300 * (requests.Count - 1), 10_000, 10_000 * (requests.Count - 1), 200 * requests.Count), result.Tokens, $"{what}: the tokens");
            check.Equal("api", result.TokensSource, $"{what}: where the tokens are from");
            check.Sequence([FakeApi.Notes], result.ModelsUsed, $"{what}: the model billed");

            // The brain's own fight line, closed by the arena.
            var line = api["fight_log"] as JsonObject;
            check.That(line is not null, $"{what}: the brain's fight log wrote its line");
            check.Equal(result.Won ? "won" : "lost", Str(line?["result"]), $"{what}: the fight log's result");
            check.Equal(Brains.RoomOf(tier), Str(line?["room"]), $"{what}: the fight log's room");
            check.Equal($"[\"{effort}\"]", line?["effort"]?.ToJsonString(), $"{what}: the fight log's efforts");
            check.Equal(requests.Count, line?["requests"]?.GetValue<int>(), $"{what}: the fight log's requests");

            // The wiki was read as in the game, the notes were written, and none of it reached the checkout.
            check.Equal(1, result.ReadCalls, $"{what}: one notes_read");
            check.Sequence([world.EnemyPage], result.WikiPages, $"{what}: the enemy's page was opened");
            var written = (api["notes_written"] as JsonArray)!.Select(n => $"{Str(n!["file"])} {Str(n["change"])} {Str(n["kind"])}").ToList();
            check.Sequence(
            [
                "runs/current.md new journal",
                "strategy/pending/learned.md new held lesson",
                $"strategy/pending/{world.EnemyPage["strategy/".Length..]} new held lesson",
            ], written, $"{what}: the notes written");
            var journal = Str((api["notes_written"] as JsonArray)![0]!["text"])!;
            check.That(journal.Contains("started.", StringComparison.Ordinal) && journal.TrimEnd().EndsWith(result.Won ? "won." : "lost.", StringComparison.Ordinal),
                $"{what}: the journal's text is in the result");
            check.Equal(before, world.RepoHash(), $"{what}: the checkout is as it was");
            check.That(!Directory.Exists(Path.Combine(world.Repo, "runs")) && !Directory.Exists(Path.Combine(world.Repo, "strategy", "pending")),
                $"{what}: no journal and no held lesson in the checkout");
            check.That(!Directory.EnumerateFileSystemEntries(world.ConfigDir).Any(), $"{what}: nothing was written to the config folder (no spend.json, no transcript)");
            check.Equal(2, result.ToolCalls.GetValueOrDefault("notes_append") - 2, $"{what}: four notes_append calls");
            check.That(result.ToolCalls.GetValueOrDefault("act") > 3 && result.ToolCalls.GetValueOrDefault("get_persona") == 1, $"{what}: the tool calls by name");

            // The transcript is the brain's own, under one line of the arena's, and never holds the system prompt.
            var transcript = File.ReadAllLines(world.TranscriptPath);
            check.Equal(requests.Count + 1, transcript.Length, $"{what}: one transcript line per exchange, and the arena's");
            check.That(transcript[0].Contains("\"type\":\"arena\"", StringComparison.Ordinal), $"{what}: the arena's line comes first");
            check.That(!transcript.Any(l => l.Contains("Play through the tools", StringComparison.Ordinal)), $"{what}: the transcript doesn't hold the system prompt");

            // The line reads back as it was written.
            var back = FightResult.FromJson((JsonObject)JsonNode.Parse(result.ToLine())!)!;
            check.Equal(Brains.Api, back.Brain, $"{what}: the brain reads back");
            check.Equal(api.ToJsonString(), back.Api?.ToJsonString(), $"{what}: the api object reads back");
            check.Equal(result.Identity, back.Identity, $"{what}: and so does what makes it the same fight");
        }
    }

    // An elite at a chosen lower effort and a boss at a chosen higher one: the brain uses each as it is.
    private static void Overrides(Check check)
    {
        var scenarios = Scenarios.Generate(10, 7);
        var settings = new BrainSettings { Effort = "medium", EliteEffort = "low", BossEffort = "max" };
        foreach (var (tier, effort, tokens) in new[] { ("normal", "medium", 16_000), ("elite", "low", 16_000), ("boss", "max", 64_000) })
        {
            var scenario = scenarios.First(s => s.Tier == tier);
            using var world = new World(scenario);
            var fake = new FakeApi { Keep = true };
            var result = world.Play(scenario, settings, fake, FakeApi.Play);
            check.That(result.Finished, $"{tier}: played to the end ({result.Outcome}: {result.Error})");
            check.Equal(effort, result.Effort, $"{tier}: the planned effort");
            check.That(fake.Requests.All(r => Str(r["output_config"]?["effort"]) == effort && r["max_tokens"]?.GetValue<int>() == tokens),
                $"{tier}: every request at {effort} with max_tokens {tokens}");
            check.Equal(0, result.Notes.Count(n => n.StartsWith("The requests were sent at effort", StringComparison.Ordinal)), $"{tier}: the requests' effort is the planned one");
            // A player that only plays writes nothing and reads nothing.
            check.Equal(0, (result.Api!["notes_written"] as JsonArray)!.Count, $"{tier}: nothing written");
            check.Equal(0, result.ReadCalls, $"{tier}: nothing read");
        }

        // A fixed max_tokens in the settings wins over the effort's, as in the game.
        var boss = scenarios.First(s => s.Tier == "boss");
        using var fixedWorld = new World(boss);
        var capped = new FakeApi { Keep = true };
        fixedWorld.Play(boss, new BrainSettings { MaxTokens = 20_000 }, capped, FakeApi.Play);
        check.That(capped.Requests.All(r => r["max_tokens"]?.GetValue<int>() == 20_000), "max_tokens from the settings wins");
    }

    // A real model id: the thinking field the game sends, and the game's price for it.
    private static void RealModel(Check check)
    {
        var scenario = Scenarios.Generate(10, 7).First(s => s.Tier == "weak");
        using var world = new World(scenario);
        var fake = new FakeApi { Keep = true };
        var result = world.Play(scenario, new BrainSettings(), fake, BrainSettings.ResolveModel("opus"));
        var requests = fake.Requests;
        check.That(result.Finished, $"played to the end ({result.Outcome}: {result.Error})");
        check.That(requests.All(r => Str(r["model"]) == "claude-opus-5-5"), "the alias was resolved to the full id");
        check.That(requests.All(r => r["thinking"]?.ToJsonString() == "{\"type\":\"adaptive\",\"display\":\"summarized\"}"), "adaptive thinking with summaries, as the game asks for it");
        check.Equal("[\"{\\u0022type\\u0022:\\u0022adaptive\\u0022,\\u0022display\\u0022:\\u0022summarized\\u0022}\"]", result.Api!["thinking"]!.ToJsonString(), "the thinking field in the result");
        // The reply's thinking block goes back exactly as it came.
        var second = (requests[1]["messages"] as JsonArray)!;
        check.Equal("{\"type\":\"thinking\",\"thinking\":\"The stand-in picks the first card it may play.\",\"signature\":\"fake-signature\"}",
            (second[1]!["content"] as JsonArray)![0]!.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            "the thinking block is sent back untouched");
        check.Equal(Price("claude-opus-5-5", first: true) + (requests.Count - 1) * Price("claude-opus-5-5", first: false), result.TotalCostUsd, "the cost at Opus's prices");
        check.That(result.ListPriceUsd is not null, "the report can price it from the repo's table too");
    }

    // --after-fight 0 stops the brain before it can answer the end; nothing is written then.
    private static void AfterTheFight(Check check)
    {
        var scenario = Scenarios.Generate(10, 7).First(s => s.Tier == "weak");
        using var world = new World(scenario);
        var fake = new FakeApi { Keep = true };
        var result = world.Play(scenario, new BrainSettings(), fake, FakeApi.Notes, afterFight: 0);
        check.That(result.Finished, $"played to the end ({result.Outcome}: {result.Error})");
        check.Equal(0, result.Api!["after_fight_requests"]!.GetValue<int>(), "no request after the end");
        check.Equal(ApiRecorder.FightOver, Str(result.Api["stopped_for"]), "stopped because the fight was over");
        check.Sequence(["runs/current.md"], (result.Api["notes_written"] as JsonArray)!.Select(n => Str(n!["file"])), "only what was written during the fight");
        // No request shows the end screen: the reply to the last one is the move that ended the fight.
        check.That(!fake.Requests.Any(ShowsTheEnd), "the model never saw the end screen");

        using var world2 = new World(scenario);
        var twice = new FakeApi { Keep = true };
        var two = world2.Play(scenario, new BrainSettings(), twice, FakeApi.Notes, afterFight: 2);
        check.Equal(2, twice.Requests.Count(ShowsTheEnd), "the end screen is in the requests after the fight");
        check.Equal(2, two.Api!["after_fight_requests"]!.GetValue<int>(), "two requests after the end when two are allowed");
        check.Equal("The fight is over: " + (two.Won ? "won" : "lost") + ".", two.LastText, "the model's last words");
    }

    // A model that ends its turn mid-fight: the brain's own message brings it back, or the brain gives up.
    private static void KeepPlaying(Check check)
    {
        var scenario = Scenarios.Generate(10, 7).First(s => s.Tier == "weak");
        using var world = new World(scenario);
        var fake = new FakeApi { Keep = true };
        var paused = world.Play(scenario, new BrainSettings(), fake, FakeApi.Pause);
        check.That(paused.Finished, $"a model that pauses once still finishes ({paused.Outcome}: {paused.Error})");
        check.Equal(1, paused.Resumes, "resumes counts the brain's own Keep playing");
        check.Equal(1, paused.Api!["keep_playing"]!.GetValue<int>(), "and so does the api object");
        check.That(fake.Requests.Any(r => ((r["messages"] as JsonArray)![^1]!["content"] as JsonArray)!.Any(b => Str(b!["text"]) == ApiBrain.KeepPlaying)),
            "the brain answered with its own words, in the same conversation");
        check.Equal(1, fake.Requests.Count(r => (r["messages"] as JsonArray)!.Count == 1), "no new conversation was started for it");

        using var world2 = new World(scenario);
        var quit = world2.Play(scenario, new BrainSettings(), new FakeApi(), FakeApi.Quit);
        check.Equal(Outcomes.Abandoned, quit.Outcome, $"a model that stops playing has abandoned the fight ({quit.Error})");
        check.Equal("Error", Str(quit.Api!["stop"]), "the brain stopped by itself");
        check.Equal("the brain itself", Str(quit.Api["stopped_for"]), "not the arena");
        check.That(Str(quit.Api["stop_message"])!.Contains("turns in a row without playing", StringComparison.Ordinal), "with the game's own reason");
        check.Equal(5, quit.Resumes, "after its five Keep playing messages");
        check.Equal("I'll leave it here.", quit.LastText, "the model's last words say why");
        check.That(quit.Counts && !quit.Won, "an abandoned fight counts, as not won");
    }

    // ---------- the batch ----------

    private static void Batch(Check check)
    {
        using var world = new World();
        string[] command = ["run", "--brain", "api", "--model", FakeApi.Play, "--scenarios", "3", "--seed", "7", "--repo", world.Repo, "--out", world.Out];

        // Without --yes-spend against the real API: the plan, and nothing else.
        var (code, said) = world.Batch(command, transport: null);
        check.Equal(2, code, "without --yes-spend the exit code is 2");
        check.That(said.Contains("at most 3 fights on a budget of $5.00", StringComparison.Ordinal), "it says how many fights at most and the budget");
        check.That(said.Contains("effort medium, elite rooms high, boss rooms high", StringComparison.Ordinal), "and the brain's settings");
        check.That(said.TrimEnd().EndsWith(ApiRun.NeedsYes, StringComparison.Ordinal), "and that it needs --yes-spend");
        check.That(!Directory.Exists(world.Out), "nothing was started and nothing written");

        // Against the stand-in: every fight, one line each.
        var fake = new FakeApi();
        (code, said) = world.Batch(command, fake);
        var lines = FightResult.ReadFile(Path.Combine(world.Out, RunCommand.ResultsFile), out var unreadable);
        check.Equal(0, code, "a whole batch ends with 0");
        check.Equal(3, lines.Count, "one line per fight");
        check.Equal(0, unreadable, "every line reads back");
        check.That(lines.All(l => l.Brain == Brains.Api && l.Experiment == "api+elite=high+boss=high" && l.Finished), "each played by the API brain to the end, under the batch's label");
        check.Sequence(["medium", "high", "medium"], lines.Select(l => l.Effort), "each at its room's effort (normal, elite, weak)");
        check.That(lines.All(l => l.Model == FakeApi.Play), "the model asked");
        var spent = lines.Sum(l => l.TotalCostUsd ?? 0);
        check.That(said.Contains(string.Create(CultureInfo.InvariantCulture, $"API spend of this batch: ${spent:0.00} of the $5.00 budget"), StringComparison.Ordinal), "the batch says what it cost");
        check.That(said.Contains("Nothing was billed", StringComparison.Ordinal), "and that the stand-in billed nothing");
        check.Equal(FirstCost * 3 + (fake.Count - 3) * LaterCost, spent, "the fights' costs add up to the requests'");
        check.That(said.Contains("Arena report: 3 fights on 3 scenarios by 1 model, effort medium (elite high, boss high), experiment api+elite=high+boss=high", StringComparison.Ordinal),
            "the report's title names the label and the efforts by room");
        check.That(said.Contains("played by the mod's own brain on the API (brain api)", StringComparison.Ordinal), "and the report says which brain played");

        var run = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(world.Out, RunCommand.RunFile)))!;
        check.Equal(Brains.Api, Str(run["brain"]), "run.json names the brain");
        check.Equal(5m, run["max_usd"]!.GetValue<decimal>(), "and the budget");
        check.Equal(ApiFight.ArenaNote(), Str(run["arena_note"]), "and the note every fight was given");
        check.That(!run.ToJsonString().Contains(FakeApi.Key, StringComparison.Ordinal), "and no key");
        check.Equal(3, Directory.GetFiles(Path.Combine(world.Out, RunCommand.TranscriptFolder)).Length, "a transcript per fight");

        // The same command again plays nothing.
        var again = new FakeApi();
        (code, said) = world.Batch(command, again);
        check.Equal(0, code, "the same command again ends with 0");
        check.Equal(0, again.Count, "and sends no request");
        check.That(said.Contains("Skipping 3 fights the results already hold", StringComparison.Ordinal), "because the results hold every fight");
        // Another effort is another batch.
        var other = new FakeApi();
        (code, _) = world.Batch([.. command, "--effort", "low", "--scenarios", "1"], other);
        check.That(code == 0 && other.Count > 0, "a batch at another effort plays its own fights");
        check.Equal(4, FightResult.ReadFile(Path.Combine(world.Out, RunCommand.ResultsFile), out _).Count, "into the same file, beside the others");
    }

    private static void Budget(Check check)
    {
        // What the three fights cost, from a batch with room to spare.
        using var world = new World();
        string[] command = ["run", "--brain", "api", "--model", FakeApi.Play, "--scenarios", "3", "--seed", "7", "--repo", world.Repo];
        world.Batch([.. command, "--out", world.Out], new FakeApi());
        var costs = FightResult.ReadFile(Path.Combine(world.Out, RunCommand.ResultsFile), out _).Select(l => l.TotalCostUsd!.Value).ToList();

        // Between fights: after the first, less is left than it cost, so the second isn't started.
        var between = Path.Combine(world.Out, "between");
        var budget = costs[0] * 1.5m;
        var (code, said) = world.Batch([.. command, "--out", between, "--max-usd", budget.ToString(CultureInfo.InvariantCulture)], new FakeApi());
        var lines = FightResult.ReadFile(Path.Combine(between, RunCommand.ResultsFile), out _);
        check.Equal(1, code, "a batch stopped by its budget ends with 1");
        check.Equal(1, lines.Count, "only the first fight was played");
        check.That(lines[0].Finished, "and it was played to the end");
        check.That(said.Contains("less than the dearest fight so far cost", StringComparison.Ordinal), "the batch says why it stopped");
        check.That(said.Contains("1 of 3 fights have a line", StringComparison.Ordinal), "and how far it got");

        // Inside a fight: the brain's own cap is what is left of the budget, and it stops there.
        var inside = Path.Combine(world.Out, "inside");
        var fake = new FakeApi();
        (code, said) = world.Batch([.. command, "--out", inside, "--max-usd", "0.30"], fake);
        lines = FightResult.ReadFile(Path.Combine(inside, RunCommand.ResultsFile), out _);
        check.Equal(1, code, "a budget reached inside a fight ends the batch with 1");
        check.Equal(1, lines.Count, "one line: the fight that reached it");
        check.Equal(Outcomes.Error, lines[0].Outcome, "which counts for nothing and is played again");
        check.Equal("MonthlyCap", Str(lines[0].Api!["stop"]), "the brain's own cap stopped it");
        check.That(lines[0].Error!.Contains("budget (--max-usd) was reached during this fight", StringComparison.Ordinal), "and the line says it was the batch's budget");
        // 0.22 for the first request and 0.023 for each after it: the fifth brings the total past 0.30.
        check.Equal(5, fake.Count, "no request was sent once the budget was reached");
        check.Equal(FirstCost + 4 * LaterCost, lines[0].TotalCostUsd, "the fight cost what its five requests cost");
        check.That(said.Contains("No more fights are started: the batch's budget (--max-usd) was reached during a fight", StringComparison.Ordinal), "the batch says so");

        // The rule by itself.
        var rule = new ApiBudget(1.00m);
        check.Equal(null, rule.WhyNotStart(0, out _), "a new batch may start");
        rule.Add(0.40m);
        rule.FightEnded(0.40m);
        check.Equal(null, rule.WhyNotStart(0, out _), "0.60 left covers a fight like the dearest (0.40)");
        check.That(rule.WhyNotStart(1, out var forGood) is not null && !forGood, "but not two at once: the second waits");
        rule.Add(0.30m);
        check.That(rule.WhyNotStart(0, out forGood) is not null && forGood, "0.30 left is less than the dearest fight: the batch stops");
        rule.Add(0.30m);
        check.That(rule.WhyNotStart(0, out forGood)!.Contains("used up", StringComparison.Ordinal) && forGood, "and at the budget nothing starts");
        check.Equal(0m, rule.Left, "what is left is never below zero");

        // The recorder refuses a request once the budget is reached, whoever asks.
        var recorder = new ApiRecorder(new ClaudeClient(FakeApi.Key, new FakeApi()), () => false, 1, rule);
        var stopped = false;
        recorder.Stop = () => stopped = true;
        var refused = false;
        try { recorder.SendAsync(new JsonObject { ["model"] = "x", ["messages"] = new JsonArray() }, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (ClaudeApiException) { refused = true; }
        check.That(refused && stopped && recorder.StoppedFor == ApiRecorder.Budget && recorder.Responses == 0, "a request over the budget is never sent");
    }

    // The request the arena's brain sends, next to the one the game's own wiring sends for the same
    // snapshot (BrainWiring.Start's createApi: the same BotTools with the game's own waits, the notes
    // in the checkout itself, no arena line in the chat): every field of the request is the same,
    // and the first message differs by the arena's one chat line and nothing else.
    private static void SameAsTheGame(Check check)
    {
        var scenarios = Scenarios.Generate(10, 7);
        foreach (var (tier, effort, tokens) in new[] { ("normal", "medium", 16_000), ("elite", "high", 32_000), ("boss", "high", 32_000) })
        {
            var scenario = scenarios.First(s => s.Tier == tier);
            using var world = new World(scenario);
            var fake = new FakeApi { Keep = true };
            world.Play(scenario, new BrainSettings(), fake, BrainSettings.DefaultModel);
            var arena = fake.Requests[0];
            var game = world.FirstRequestAsTheGameWiresIt(scenario, new BrainSettings());
            check.That(game is not null, $"{tier}: the brain wired as the game wires it sent a request");
            if (game is null)
                continue;

            check.Sequence(game.Select(field => field.Key).OrderBy(k => k, StringComparer.Ordinal), arena.Select(field => field.Key).OrderBy(k => k, StringComparer.Ordinal),
                $"{tier}: the request has the fields the game's has, no more and no fewer");
            foreach (var field in new[] { "model", "max_tokens", "system", "tools", "output_config", "thinking", "cache_control" })
                check.Equal(game[field]?.ToJsonString(), arena[field]?.ToJsonString(), $"{tier}: {field} is what the game's wiring sends");
            check.Equal(BrainSettings.DefaultModel, Str(arena["model"]), $"{tier}: the model");
            check.Equal(tokens, arena["max_tokens"]!.GetValue<int>(), $"{tier}: max_tokens");
            check.Equal(effort, Str(arena["output_config"]?["effort"]), $"{tier}: the effort");

            var (ours, theirs) = (FirstText(arena), FirstText(game));
            check.That(theirs.Contains("## Recent chat\n\n(none)\n\n", StringComparison.Ordinal), $"{tier}: the game's first message has no chat line here");
            check.Equal(theirs.Replace("## Recent chat\n\n(none)\n\n", "## Recent chat\n\n(mod) " + ApiFight.ArenaNote() + "\n\n", StringComparison.Ordinal), ours,
                $"{tier}: the first message is the game's but for the arena's one chat line");
            check.Equal(1, (arena["messages"] as JsonArray)!.Count, $"{tier}: one message, as in the game's");
        }
    }

    private static string FirstText(JsonObject request) =>
        Str(((request["messages"] as JsonArray)![0]!["content"] as JsonArray)![0]!["text"]) ?? "";

    // The budget is the results folder's: the same command again gets no new one, and the ledger says what was spent.
    private static void FolderBudget(Check check)
    {
        using var world = new World();
        string[] command = ["run", "--brain", "api", "--model", FakeApi.Play, "--scenarios", "3", "--seed", "7", "--repo", world.Repo, "--out", world.Out];
        var first = new FakeApi();
        var (code, _) = world.Batch([.. command, "--max-usd", "0.30"], first);
        var results = Path.Combine(world.Out, RunCommand.ResultsFile);
        var ledger = Path.Combine(world.Out, ApiBudget.LedgerFile);
        check.That(code == 1 && first.Count == 5, "the first run reaches its budget inside the first fight, after five requests");
        check.Equal(first.Count, File.ReadAllLines(ledger).Length, "the ledger has one line per answered request");
        check.Equal(FirstCost + 4 * LaterCost, ApiBudget.SpentIn(ledger, standIn: true, out var unreadable), "and adds up to what they cost");
        check.Equal(0, unreadable, "every line of it reads back");
        check.Equal(0m, ApiBudget.SpentIn(ledger, standIn: false, out _), "the stand-in's lines never count as spend on the real API");
        check.That(!File.ReadAllText(ledger).Contains(FakeApi.Key, StringComparison.Ordinal), "the ledger holds no key");

        // The same command again: nothing is sent, because the folder's budget is used up.
        var again = new FakeApi();
        (code, var said) = world.Batch([.. command, "--max-usd", "0.30"], again);
        check.Equal(1, code, "the same command again ends with 1");
        check.Equal(0, again.Count, "and sends no request: it gets no new budget");
        check.That(said.Contains("Nothing was started: the budget is used up: $0.31 of $0.30 (--max-usd), $0.31 of it by earlier runs in this folder", StringComparison.Ordinal),
            "it says the folder's budget is used up, and by what");
        check.Equal(1, FightResult.ReadFile(results, out _).Count, "no line was added");

        // A budget a little above what was spent: less is left than a fight like the dearest... there is none finished yet, so one request more would go; not even that when a first request doesn't fit.
        var rule = new ApiBudget(1.00m, earlierUsd: 0.90m);
        check.Equal(0.10m, rule.Left, "what earlier runs spent counts against the budget");
        check.That(rule.WhyNotStart(0, out var forGood, firstRequestUsd: 0.18m) is { } why && forGood && why.Contains("less than a fight's first request costs (about $0.18", StringComparison.Ordinal),
            "no fight starts on less than its first request costs");
        check.Equal(null, rule.WhyNotStart(0, out _, firstRequestUsd: 0.05m), "one starts when it fits");
        check.Equal(0.16m, ApiRun.FirstRequestUsd(80_000, "claude-opus-5-5"), "a first request on Opus 5.5: 80,000 characters written to the one-hour cache");

        // A higher --max-usd, typed by hand, goes on from what was spent.
        var more = new FakeApi();
        (code, said) = world.Batch([.. command, "--max-usd", "5"], more);
        check.That(code == 0 && more.Count > 0, "a higher --max-usd plays on");
        check.That(said.Contains("Earlier runs in this folder have already cost $0.31 of it (api-spend.jsonl), so $4.69 is left.", StringComparison.Ordinal), "and says first what was already spent");
        check.That(said.Contains("; with the earlier runs in this folder $", StringComparison.Ordinal), "and at the end what the folder has cost in all");
        check.Equal(first.Count + more.Count, File.ReadAllLines(ledger).Length, "the ledger went on, one line per request");
        var lines = FightResult.ReadFile(results, out _);
        check.Equal(lines.Sum(l => l.TotalCostUsd ?? 0), ApiBudget.SpentIn(ledger, standIn: true, out _), "the ledger and the result lines agree on the folder's spend");
        var run = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(world.Out, RunCommand.RunFile)))!;
        check.Equal(FirstCost + 4 * LaterCost, run["spent_before_usd"]!.GetValue<decimal>(), "run.json says what the folder had cost before this run");

        // A ledger line that can't be read is counted as such, not as nothing.
        File.AppendAllText(ledger, "not json\n{\"usd\": \"a lot\"}\n");
        ApiBudget.SpentIn(ledger, standIn: true, out unreadable);
        check.Equal(2, unreadable, "lines of the ledger that can't be read are counted");

        // The recorder puts the money in the budget before anything else, and nothing it counts can undo that.
        var budget = new ApiBudget(5m);
        var recorder = new ApiRecorder(new ClaudeClient(FakeApi.Key, new FakeApi()), () => false, 1, budget) { Fight = "weak-1" };
        recorder.SendAsync(new JsonObject { ["model"] = FakeApi.Play, ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "hello" }) }, CancellationToken.None)
            .GetAwaiter().GetResult();
        check.That(budget.SpentNow == FirstCost && recorder.Usd == FirstCost && recorder.CountingProblems.Count == 0, "an answered request is in the budget at once");
    }

    // The game's monthly cap is a setting about the same key: 0 means never, and a test may not be allowed more than a month of play.
    private static void MonthlyCap(Check check)
    {
        using var world = new World();
        string[] command = ["run", "--brain", "api", "--yes-spend", "--model", FakeApi.Play, "--scenarios", "1", "--seed", "7", "--repo", world.Repo, "--out", world.Out];
        world.Config("config.json", "{\"monthly_budget_usd\": 0}");
        var (code, said) = world.Batch(command, transport: null);
        check.That(code == 2 && said.Contains("sets monthly_budget_usd to 0", StringComparison.Ordinal) && said.Contains("Nothing was started", StringComparison.Ordinal),
            "a monthly budget of 0 (never use the API) starts nothing, --yes-spend or not");
        world.Config("config.json", "{\"monthly_budget_usd\": 3}");
        (code, said) = world.Batch([.. command, "--max-usd", "4"], transport: null);
        check.That(code == 2 && said.Contains("--max-usd 4.00 is more than the game's own monthly budget ($3.00", StringComparison.Ordinal), "a budget above the game's monthly one is refused");
        (code, said) = world.Batch([.. command, "--max-usd", "3"], transport: null);
        check.That(code == 1 && said.Contains("There is no API key", StringComparison.Ordinal), "one within it goes on (to the key, which this folder doesn't have)");
        check.That(!Directory.Exists(world.Out) || !File.Exists(Path.Combine(world.Out, RunCommand.ResultsFile)), "and none of the three played a fight");
    }

    // --only: the named fights and no others, the same fights a batch of their seed would play.
    private static void OnlyNamed(Check check)
    {
        var seven = Scenarios.Generate(2, 7);
        var elite = seven.First(s => s.Tier == "elite");
        check.Sequence([elite.Id], ArenaOptions.Parse(["run", "--brain", "api", "--only", elite.Id]).OnlyScenarios, "--only names a fight by its id");
        foreach (var bad in new[]
                 {
                     new[] { "run", "--models", "claude-x", "--only", elite.Id }, ["run", "--brain", "api", "--only", elite.Id, "--scenarios", "2"],
                     ["run", "--brain", "api", "--only", elite.Id, "--seed", "7"], ["run", "--brain", "api", "--only", "elite-0"], ["run", "--brain", "api", "--only", "big-12"],
                     ["run", "--brain", "api", "--only", ","],
                 })
        {
            var refused = false;
            try { ArenaOptions.Parse(bad); }
            catch (UsageException) { refused = true; }
            check.That(refused, $"'{string.Join(' ', bad)}' is refused");
        }

        using var world = new World();
        var fake = new FakeApi();
        var (code, said) = world.Batch(["run", "--brain", "api", "--model", FakeApi.Play, "--only", elite.Id, "--repo", world.Repo, "--out", world.Out], fake);
        var lines = FightResult.ReadFile(Path.Combine(world.Out, RunCommand.ResultsFile), out _);
        check.That(code == 0 && lines.Count == 1, "one fight was played");
        check.That(said.Contains("1 fight named with --only", StringComparison.Ordinal) && said.Contains("at most 1 fight on a budget", StringComparison.Ordinal), "and the batch said so first");
        check.Equal(elite.Id, lines[0].ScenarioId, "the one that was named");
        check.Equal("high", lines[0].Effort, "at its room's effort");
        check.Equal(elite.Summary, lines[0].Summary, "the same fight the seed's batch holds");
        // It is the same fight to a batch of its seed, which then plays only the other.
        (code, said) = world.Batch(["run", "--brain", "api", "--model", FakeApi.Play, "--scenarios", "2", "--seed", "7", "--repo", world.Repo, "--out", world.Out], new FakeApi());
        check.That(code == 0 && said.Contains("Skipping 1 fight the results already hold", StringComparison.Ordinal), "a batch of that seed skips it");
        check.Equal(2, FightResult.ReadFile(Path.Combine(world.Out, RunCommand.ResultsFile), out _).Count, "and plays the other");
    }

    private static void Keys(Check check)
    {
        using var world = new World();
        string[] command = ["run", "--brain", "api", "--yes-spend", "--model", FakeApi.Play, "--scenarios", "2", "--seed", "7", "--repo", world.Repo, "--out", world.Out];

        // No key where the game reads it, and none in the environment the check hands over: a clear word, and no fight.
        var (code, said) = world.Batch(command, transport: null);
        check.Equal(1, code, "without a key the batch ends with 1");
        check.That(said.Contains("There is no API key: the game reads it from " + Path.Combine(world.ConfigDir, "api_key"), StringComparison.Ordinal), "it says where the game reads the key");
        check.That(said.Contains(BrainSettings.ApiKeyEnvVar, StringComparison.Ordinal), "and names the environment variable");
        check.That(!File.Exists(Path.Combine(world.Out, RunCommand.ResultsFile)), "no fight was started");

        // A key the API refuses: the first fight says so and the batch stops.
        var fake = new FakeApi();
        (code, said) = world.Batch(["run", "--brain", "api", "--model", FakeApi.BadKey, "--scenarios", "2", "--seed", "7", "--repo", world.Repo, "--out", world.Out], fake);
        var lines = FightResult.ReadFile(Path.Combine(world.Out, RunCommand.ResultsFile), out _);
        check.Equal(1, code, "a refused key ends the batch with 1");
        check.Equal(1, lines.Count, "after one fight");
        check.Equal(Outcomes.Error, lines[0].Outcome, "which is an error");
        check.Equal("Unauthorized", Str(lines[0].Api!["stop"]), "the brain stopped as the game's does for a bad key");
        check.Equal(1, fake.Count, "after one request, not retried");
        check.Equal(0m, lines[0].TotalCostUsd, "that cost nothing");
        check.That(said.Contains("No more fights are started: the API turned the brain away (Unauthorized", StringComparison.Ordinal), "and the batch says why it stopped");
    }

    // ---------- the report and compare ----------

    private static void Reports(Check check)
    {
        var weak = new Scenario("weak-1", 1, "weak", "a weak fight");
        var elite = new Scenario("elite-2", 2, "elite", "an elite");
        FightResult[] byClaude = [Line(weak, "medium", "base", Brains.ClaudeCode), Line(elite, "medium", "base", Brains.ClaudeCode)];
        FightResult[] byApi = [Line(weak, "medium", "api+elite=high+boss=high", Brains.Api, 0.25m), Line(elite, "high", "api+elite=high+boss=high", Brains.Api, 0.50m)];

        // One player: the label names the elite's own effort, so the batch isn't split in two.
        var players = Report.PlayersOf(byApi);
        check.Equal(1, players.Count, "an API batch is one row of the report");
        check.Equal("experiment api+elite=high+boss=high, effort medium (elite high, boss high)", players[0].Describe(), "described by its label and efforts");

        // The two brains are never pooled into one table.
        check.That(Report.MixedLabels([.. byClaude, .. byApi]) is { } refusal && refusal.Contains("api+elite=high+boss=high", StringComparison.Ordinal),
            "the plain report refuses a file that holds both brains' fights");
        var text = new StringWriter();
        Report.PrintByLabel([.. byClaude, .. byApi], text);
        check.That(text.ToString().Contains("claude-x [api+elite=high+boss=high]", StringComparison.Ordinal), "by label, the API brain's fights are a row of their own");
        check.That(text.ToString().Contains("Brain: 2 of these fights were played by the mod's own brain on the API (brain api)", StringComparison.Ordinal), "and the report says how many it played");
        check.That(text.ToString().Contains("$0.75 in all", StringComparison.Ordinal), "and what they cost");
        check.That(text.ToString().Contains("The others were played by Claude Code on the subscription.", StringComparison.Ordinal), "and who played the others");

        var plain = new StringWriter();
        Report.PrintByLabel(byClaude, plain);
        check.That(!plain.ToString().Contains("Brain:", StringComparison.Ordinal) && !plain.ToString().Contains("notes_read", StringComparison.Ordinal),
            "a report of Claude Code's fights alone says nothing new");

        // Compare names the two brains when they differ, and only then.
        check.That(CompareCommand.BrainNote(byClaude, byApi) is { } note && note.Contains("A claude-code; B api", StringComparison.Ordinal), "compare says when the two sides were driven differently");
        check.Equal(null, CompareCommand.BrainNote(byApi, byApi), "and nothing when they were not");
        check.Equal(null, CompareCommand.BrainNote(byClaude, byClaude), "nor for two Claude Code runs");

        // A line from before there were two brains is Claude Code's.
        var old = (JsonObject)JsonNode.Parse(byClaude[0].ToLine())!;
        old.Remove("brain");
        check.Equal(Brains.ClaudeCode, FightResult.FromJson(old)!.Brain, "a line without a brain is Claude Code's");
        check.That(byClaude[0].ToLine().Contains("\"brain\":\"claude-code\"", StringComparison.Ordinal) && !byClaude[0].ToLine().Contains("\"api\"", StringComparison.Ordinal),
            "Claude Code's line names its brain and has no api object");
    }

    // ---------- helpers ----------

    private static FightResult Line(Scenario scenario, string effort, string label, string brain, decimal? cost = null) => new()
    {
        ScenarioId = scenario.Id, Tier = scenario.Tier, Summary = scenario.Summary, Seed = scenario.Seed, Model = "claude-x",
        Effort = effort, Experiment = label, Brain = brain, Outcome = Outcomes.Won, HpLeft = 40, MaxHp = 80, Turns = 4, TotalCostUsd = cost,
        Api = brain == Brains.Api ? new JsonObject { ["requests"] = 10 } : null,
    };

    // The options of an API batch as ApiRun derives them from the settings: the planned efforts by tier.
    private static ArenaOptions ApiOptions(BrainSettings settings, string[] args)
    {
        var options = ArenaOptions.Parse(args);
        return options with { Effort = settings.Effort, EffortByTier = ApiSettings.EffortByTier(settings) };
    }

    private static decimal Price(string model, bool first) => ModelPrices.Cost(model, new JsonObject
    {
        ["input_tokens"] = first ? 1000 : 300,
        ["output_tokens"] = 200,
        ["cache_read_input_tokens"] = first ? 0 : 10_000,
        ["cache_creation_input_tokens"] = first ? 10_000 : 0,
        ["cache_creation"] = new JsonObject { ["ephemeral_5m_input_tokens"] = 0, ["ephemeral_1h_input_tokens"] = first ? 10_000 : 0 },
    }).Usd;

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    // True when a request's messages hold a snapshot of the end screen (in a tool result's JSON text).
    private static bool ShowsTheEnd(JsonObject request) =>
        request["messages"]!.ToJsonString().Contains("screen\\u0022:\\u0022" + ArenaGame.OverScreen, StringComparison.Ordinal);

    // An API that takes the brain's first request and never answers it: nothing is sent anywhere.
    private sealed class HeldApi : IClaudeApi
    {
        public TaskCompletionSource<JsonObject> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ClaudeResponse> SendAsync(JsonObject request, CancellationToken ct)
        {
            First.TrySetResult((JsonObject)request.DeepClone());
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new OperationCanceledException(ct);
        }
    }

    /// <summary>
    /// Everything a check touches, in one temp folder that is deleted afterwards: a small
    /// checkout (play prompt, notes, a wiki with a page for the scenario's first enemy), a
    /// config folder standing for <c>~/.config/spiremind</c>, and a folder for results.
    /// </summary>
    private sealed class World : IDisposable
    {
        private readonly string _root;

        public World(Scenario? scenario = null)
        {
            _root = ClaudeCli.RealPath(Directory.CreateTempSubdirectory("spiremind-arena-api-check-").FullName);
            Repo = Path.Combine(_root, "repo");
            ConfigDir = Path.Combine(_root, "config");
            Out = Path.Combine(_root, "out");
            Directory.CreateDirectory(ConfigDir);
            Write(PromptBuilder.PlayPrompt, "---\ndescription: a play prompt\n---\nPlay through the tools. Scope: $ARGUMENTS.\n");
            Write(PromptBuilder.GeneralNotes, "Block first.\n");
            Write("strategy/characters/ironclad.md", "Strength scales.\n");
            Write(NotesTools.Learned, "# Learned\n\n- (Ironclad, 2026-10-01, floor 3) Count the damage first.\n");
            Write("strategy/wiki/README.md", "# The wiki\nHow pages are laid out.\n");
            Write("strategy/wiki/potions.md", "# Potions\nWhat each potion does.\n\n## Lessons\n");
            var enemy = scenario is null ? "Nibbit" : WikiBrief.Enemies(new ArenaGame(scenario))[0];
            EnemyPage = $"strategy/wiki/enemies/{enemy.ToLowerInvariant().Replace(' ', '-')}.md";
            Write(EnemyPage, $"# {enemy}\nAn enemy.\n\n## Facts (game files, v0.107.1)\n\nIt hits.\n\n## Lessons\n");
        }

        public string Repo { get; }
        public string ConfigDir { get; }
        public string Out { get; }
        public string EnemyPage { get; }
        public string TranscriptPath => Path.Combine(Out, "transcript.jsonl");
        public ApiPlaces Places => new(ConfigDir, ConfigDir, _ => null);

        public void Write(string file, string text)
        {
            var path = Path.Combine(Repo, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        public void Config(string file, string text) => File.WriteAllText(Path.Combine(ConfigDir, file), text);

        /// <summary>One fight, played by the real brain through Core's real client over <paramref name="fake"/>.</summary>
        public FightResult Play(Scenario scenario, BrainSettings settings, FakeApi fake, string model, int afterFight = 1)
        {
            using var client = new ClaudeClient(FakeApi.Key, fake);
            return new ApiFight
            {
                Scenario = scenario,
                Model = model,
                Settings = settings,
                Api = client,
                Repo = Repo,
                Budget = new ApiBudget(50m),
                Effort = settings.EffortFor(Brains.RoomOf(scenario.Tier)),
                Experiment = ExperimentLabel.Of(false, new ExperimentOptions(), ApiSettings.EffortByTier(settings), null, Brains.Api),
                AfterFight = afterFight,
                Timeout = TimeSpan.FromSeconds(60),
                KeySource = "fake",
                TranscriptPath = TranscriptPath,
                ActionDelay = Fast.ActionDelay,
                WaiterPoll = Fast.WaiterPoll,
                WaiterSettle = Fast.WaiterSettle,
                PollInterval = Fast.PollInterval,
                WaitHold = Fast.WaitHold,
            }.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// The first request of the brain wired as the game wires it (BrainWiring.Start's createApi),
        /// against the same fight: the same <see cref="BotTools"/> with the game's own waits, the notes
        /// tools on the checkout itself, the chat as it is (no arena line), the settings as loaded.
        /// The request is held, never answered, and the brain is stopped; null when none came.
        /// </summary>
        public JsonObject? FirstRequestAsTheGameWiresIt(Scenario scenario, BrainSettings settings)
        {
            var config = Path.Combine(_root, "game-config");
            var game = new ArenaGame(scenario);
            var versioner = new SnapshotVersioner();
            var waiter = new DecisionWaiter(game, versioner);
            var chat = ClaudeFight.NewChat();
            var tools = new BotTools(game, waiter, versioner, chat);
            var held = new HeldApi();
            var brain = new ApiBrain(
                held,
                new BotToolsHost(tools),
                new NotesTools(Repo, options: new NotesOptions { LockFile = Path.Combine(config, "locks", "notes.lock") }),
                Repo,
                () => settings,
                new SpendTracker(config),
                character: () => chat.BotName,
                multiplayer: () => false,
                recentChat: () => ApiFight.RecentChat(chat));
            brain.Start();
            try
            {
                return held.First.Task.Wait(TimeSpan.FromSeconds(30)) ? (JsonObject)JsonNode.Parse(held.First.Task.Result.ToJsonString())! : null;
            }
            finally
            {
                brain.StopAsync().Wait(TimeSpan.FromSeconds(15));
            }
        }

        /// <summary>A batch from the command line's arguments, with this world's config folder
        /// in the place of Mark's and an empty environment. Returns the exit code and what it printed.</summary>
        public (int Code, string Said) Batch(string[] args, FakeApi? transport)
        {
            var output = new StringWriter();
            var code = ApiRun.RunAsync(ArenaOptions.Parse(args), output, Places, transport, Fast).GetAwaiter().GetResult();
            return (code, output.ToString());
        }

        /// <summary>A hash over every file of the checkout, names and contents.</summary>
        public string RepoHash()
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var path in Directory.EnumerateFiles(Repo, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(Repo, path) + "\n"));
                hash.AppendData(File.ReadAllBytes(path));
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the system clears its temp folder itself */ }
        }
    }
}
