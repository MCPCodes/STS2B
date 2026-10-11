using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using SpireMind.Core;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>
/// The runner's own promises about what a benchmark session can open and what it is told,
/// checked without starting Claude Code: the flags a session is started with (Read as the only
/// built-in tool, a Read rule for the checkout's strategy folder, nothing that edits or writes),
/// the appendix the prompt ends with (where the wiki's pages are, and that nothing can be
/// written), that the prompt and the Read rule name the same checkout when it is a frozen copy,
/// what the init line must list for a fight to go on, and how Read calls are counted into the
/// result line and the report. The expected values are written out by hand.
/// <para>
/// The experiments of <c>arena run</c> are held to the same: what a run's label is and how it
/// reads back, what the switches parse to and that none of them is on by default, which pages
/// <c>--brief</c> puts into a fight's first message (found by the name in a page's first line)
/// and that nothing else changes, what each session of a fight is started with (through a
/// launcher that starts nothing), when a fight counts as already played, and the numbers of
/// <c>report --by-label</c> and <c>arena compare</c> on fights whose HP was chosen by hand.
/// </para>
/// <para>
/// Position tests (<c>arena position</c>) are checked in the file beside this one
/// (RunnerChecks.Positions.cs): how a transcript is read, that a recorded fight is rebuilt to a
/// turn and every difference from the recording is caught, that a game put at a position plays
/// on exactly as the recorded fight did, what a reshuffle changes and what it leaves, what a
/// run's line holds, and the numbers of the position report and of compare.
/// </para>
/// <para>
/// What these can't show is Claude Code's own permission check: that needs a real session. The
/// flags' meaning was read from Claude Code 2.1.296 (docs/arena.md says how), and every
/// session's init line is held to it while a batch runs.
/// </para>
/// </summary>
public sealed partial class RunnerChecks : IArenaChecks
{
    private const string Repo = "/Users/mark/Projects/STS2B";

    public void Run(Check check)
    {
        check.Group("session flags", () => Flags(check));
        check.Group("read rule", () => Rules(check));
        check.Group("prompt", () => Prompt(check));
        check.Group("frozen copy", () => FrozenCopy(check));
        check.Group("init line", () => InitLine(check));
        check.Group("read counting", () => Reads(check));
        check.Group("result line", () => ResultLine(check));
        check.Group("report", () => ReportColumns(check));
        check.Group("experiment label", () => Labels(check));
        check.Group("experiment switches", () => Switches(check));
        check.Group("brief", () => Brief(check));
        check.Group("brief in a fight", () => BriefFight(check));
        check.Group("same fight", () => SameFight(check));
        check.Group("experiment in the result line", () => ExperimentLine(check));
        check.Group("effort by tier in the report", () => EffortGroups(check));
        check.Group("labels in the report", () => LabelReport(check));
        check.Group("compare", () => Compare(check));
        Positions(check);
    }

    // ---------- the flags ----------

    private static void Flags(Check check)
    {
        var args = ClaudeCli.Arguments("the prompt", "id-1", resume: false, "claude-x", "medium", "SYSTEM", 4242, Repo);
        check.Sequence(
        [
            "-p", "the prompt",
            "--session-id", "id-1",
            "--model", "claude-x",
            "--effort", "medium",
            "--append-system-prompt", "SYSTEM",
            "--strict-mcp-config",
            "--mcp-config", "{\"mcpServers\":{\"spiremind\":{\"type\":\"http\",\"url\":\"http://127.0.0.1:4242/mcp\"}}}",
            "--tools", "Read",
            "--permission-mode", "dontAsk",
            "--allowedTools", "mcp__spiremind", "mcp__spiremind__*", "Read(//Users/mark/Projects/STS2B/strategy/**)",
            "--disallowedTools", "Edit", "Write", "NotebookEdit", "Bash",
            "--output-format", "stream-json",
            "--verbose",
        ], args, "the arguments of a new session");

        var resumed = ClaudeCli.Arguments("go on", "id-1", resume: true, "claude-x", "medium", "SYSTEM", 4242, Repo);
        check.Sequence(["-p", "go on", "--resume", "id-1"], resumed.Take(4), "a resumed session starts with --resume and the same id");
        check.Sequence(args.Skip(4), resumed.Skip(4), "a resumed session gets every other flag as the first did");

        // What must hold whatever else changes: one built-in tool, and it only reads.
        check.Sequence(["Read"], Values(args, "--tools"), "--tools names Read and nothing else");
        check.Sequence(["dontAsk"], Values(args, "--permission-mode"), "what isn't allowed is refused, not asked");
        check.That(Values(args, "--allowedTools").All(rule => rule is "mcp__spiremind" or "mcp__spiremind__*" || rule.StartsWith("Read(//", StringComparison.Ordinal)),
            "every allow rule is the fight's MCP server, its tools, or a Read rule");
        check.Equal(1, Values(args, "--allowedTools").Count(rule => rule.StartsWith("Read(", StringComparison.Ordinal)), "Read rules");
        foreach (var writer in new[] { "Edit", "Write", "NotebookEdit", "Bash" })
        {
            check.That(Values(args, "--disallowedTools").Contains(writer), $"{writer} is denied by name");
            check.That(!Values(args, "--allowedTools").Any(rule => rule.StartsWith(writer, StringComparison.Ordinal)), $"no allow rule names {writer}");
        }
        foreach (var never in new[] { "--add-dir", "--dangerously-skip-permissions", "--allow-dangerously-skip-permissions", "bypassPermissions", "acceptEdits", "auto", "default" })
            check.That(!args.Contains(never), $"{never} is not among the arguments");
        check.That(args.Contains("--strict-mcp-config"), "only the fight's MCP server is used");

        // The same tools and rules as the in-game brain's session, less everything that writes.
        var brain = SubscriptionBrain.BuiltInTools.Split(',');
        check.That(brain.Contains(ClaudeCli.ReadTool), "the in-game brain's session has Read too");
        check.That(brain.Contains("Edit") && brain.Contains("Write"), "the in-game brain's session can edit and write, which is what the arena's leaves out");

        var thrown = false;
        try { ClaudeCli.Arguments("p", "id", false, "m", "medium", "S", 1, "/Users/mark/Projects/STS2B (copy)"); }
        catch (ArgumentException) { thrown = true; }
        check.That(thrown, "a checkout whose path no rule can spell is refused, not started with a rule that means something else");
    }

    private static void Rules(Check check)
    {
        check.Equal("Read(//Users/mark/Projects/STS2B/strategy/**)", ClaudeCli.ReadRule(ClaudeCli.StrategyFolder(Repo)), "the rule for the checkout's strategy folder");
        check.Equal("Read(//a/b/**)", ClaudeCli.ReadRule("/a/b/"), "a trailing slash changes nothing");
        check.Equal("Read(//Users/m k/x y/strategy/**)", ClaudeCli.ReadRule("/Users/m k/x y/strategy"), "a space inside a name is carried as it is");
        check.Equal("Read(//private/tmp/claude-501/arena-pilot/strategy/**)", ClaudeCli.ReadRule("/private/tmp/claude-501/arena-pilot/strategy"), "a frozen copy's folder");
        foreach (var bad in new[]
                 {
                     "/a/b (1)/strategy", "/a/b)/strategy", "/a/b*/strategy", "/a/b?/strategy", "/a/[b]/strategy", "/a/b\\c/strategy",
                     "/a/b /strategy", "/a/ b/strategy", "/a/b\n/strategy", "relative/strategy", "strategy", "/",
                 })
            check.Equal(null, ClaudeCli.ReadRule(bad), $"no rule for '{bad.Replace("\n", "\\n")}'");
    }

    // ---------- the prompt ----------

    private static void Prompt(Check check)
    {
        using var checkout = new Checkout("# Vantom\nA boss.\n\n## Facts\n\n## Lessons\n");
        var repo = checkout.Path;
        var text = ArenaPrompt.Build(repo).Text;
        var appendix = ArenaPrompt.ArenaNotes(repo);

        check.That(text.EndsWith(appendix + "\n", StringComparison.Ordinal), "the system text ends with the arena's appendix");
        check.That(!text.Contains("## Playing from inside the game", StringComparison.Ordinal), "the in-game brain's own appendix is gone");
        foreach (var gone in new[] { "There are no files to read", "no files to read", "notes_read", "notes_append", "notes_write" })
            check.That(!text.Contains(gone, StringComparison.Ordinal), $"the system text doesn't say '{gone}'");
        foreach (var gone in new[] { "don't look for files", "there are none here", "no files" })
            check.That(!ArenaPrompt.UserPrompt.Contains(gone, StringComparison.Ordinal), $"the user prompt doesn't say '{gone}'");

        // The index PromptBuilder puts in front of the appendix, and where its pages are.
        check.That(text.Contains("## Wiki index", StringComparison.Ordinal), "the system text has the wiki index");
        check.That(text.Contains("strategy/wiki/enemies/vantom.md: A boss.", StringComparison.Ordinal), "the index lists the checkout's page with its summary");
        check.That(appendix.Contains($"They are files under `{repo}/strategy/wiki/`", StringComparison.Ordinal), "the appendix names the wiki folder by its absolute path");
        check.That(appendix.Contains($"`strategy/wiki/enemies/<name>.md` is `{repo}/strategy/wiki/enemies/<name>.md`", StringComparison.Ordinal),
            "the appendix says how an index line becomes a file");
        check.That(appendix.Contains("Read one with the `Read` tool when the prompt says to", StringComparison.Ordinal), "the appendix says how a page is read");
        check.That(appendix.Contains("the `notes_` tools the prompt names don't exist here", StringComparison.Ordinal), "the appendix says the notes tools aren't there");
        check.That(appendix.Contains("`Read` opens no file outside that `strategy/` folder", StringComparison.Ordinal), "the appendix says how far Read reaches");
        check.Equal(2, Regex.Matches(appendix, Regex.Escape(repo)).Count, "how often the appendix spells the checkout's path");
        check.That(appendix.Contains("Nothing can be written here: no lesson on a wiki page, no `strategy/learned.md`, no journal", StringComparison.Ordinal),
            "the appendix says nothing can be written");
        check.That(ArenaPrompt.UserPrompt.Contains("can be read with `Read`", StringComparison.Ordinal)
                   && ArenaPrompt.UserPrompt.Contains("Nothing can be written", StringComparison.Ordinal), "the user prompt says the same in a line");

        // The index names a tool the arena doesn't have; the appendix is what makes that honest.
        check.That(text.Contains("notes_list", StringComparison.Ordinal), "the index itself still names notes_list, which is why the appendix speaks of it");
        // Every tool the appendix tells the model to use is one a session is started with.
        check.Sequence([ClaudeCli.ReadTool], Values(ClaudeCli.Arguments("p", "id", false, "m", "medium", text, 1, repo), "--tools"), "the tool the appendix names is the one --tools passes");
        check.That(appendix.Length < PromptBuilder.HarnessNotes.Length, $"the appendix ({appendix.Length} characters) is shorter than the in-game brain's ({PromptBuilder.HarnessNotes.Length})");
    }

    private static void FrozenCopy(Check check)
    {
        const string page = "# Vantom\nA boss.\n\n## Facts\n\n## Lessons\n";
        using var live = new Checkout(page);
        using var frozen = new Checkout(page);
        using var moved = new Checkout(page + "- (Ironclad, 2026-10-10) A lesson a real run added.\n");

        // One path goes everywhere: the prompt names the wiki of the checkout it was built from,
        // and the session's Read rule opens that checkout and no other.
        foreach (var (mine, other) in new[] { (frozen, live), (live, frozen) })
        {
            var text = ArenaPrompt.Build(mine.Path).Text;
            var rule = Values(ClaudeCli.Arguments("p", "id", false, "m", "medium", text, 1, mine.Path), "--allowedTools").Single(r => r.StartsWith("Read(", StringComparison.Ordinal));
            check.That(text.Contains($"`{mine.Path}/strategy/wiki/`", StringComparison.Ordinal), "the prompt names its own checkout's wiki");
            check.That(!text.Contains(other.Path, StringComparison.Ordinal), "the prompt names no other checkout");
            check.Equal($"Read(//{mine.Path.TrimStart('/')}/strategy/**)", rule, "the Read rule is for the same checkout");
        }

        // The same content in another place is the same prompt and the same wiki, by their hashes.
        var liveText = ArenaPrompt.Build(live.Path).Text;
        var frozenText = ArenaPrompt.Build(frozen.Path).Text;
        check.That(liveText != frozenText, "two checkouts' prompts differ, by the path in the appendix");
        check.Equal(ArenaPrompt.Hash(liveText, live.Path), ArenaPrompt.Hash(frozenText, frozen.Path), "the prompt hash of the same content in another place");
        check.Equal(ArenaPrompt.WikiHash(live.Path), ArenaPrompt.WikiHash(frozen.Path), "the wiki hash of the same pages in another place");
        // A lesson added to a page changes no line of the index, so only the wiki's hash sees it.
        check.Equal(ArenaPrompt.Hash(liveText, live.Path), ArenaPrompt.Hash(ArenaPrompt.Build(moved.Path).Text, moved.Path), "the prompt hash after a lesson was added to a page");
        check.That(ArenaPrompt.WikiHash(live.Path) != ArenaPrompt.WikiHash(moved.Path), "the wiki hash changes when a page's text does");
        check.That(ArenaPrompt.WikiHash(live.Path) is { Length: 12 }, "a wiki hash is 12 hex digits");
        File.WriteAllText(System.IO.Path.Combine(frozen.Path, "strategy", "wiki", "lessons.md"), "# Lessons\nA page that wasn't there.\n");
        check.That(ArenaPrompt.WikiHash(live.Path) != ArenaPrompt.WikiHash(frozen.Path), "the wiki hash changes when a page is added");

        // The checkout comes back by its real path, whichever way it was named.
        check.Equal(live.Path, ArenaPrompt.FindRepo(live.Path), "FindRepo of a checkout");
        check.Equal(live.Path, ArenaPrompt.FindRepo(live.Path + System.IO.Path.DirectorySeparatorChar), "FindRepo of a checkout named with a trailing slash");
        check.Equal(live.Path, ArenaPrompt.FindRepo(live.Link()), "FindRepo of a link to a checkout is the checkout itself");
        check.Equal(null, ArenaPrompt.FindRepo(System.IO.Path.Combine(live.Path, "strategy")), "FindRepo of a folder without the play prompt");
    }

    // ---------- the init line ----------

    private static void InitLine(Check check)
    {
        string[] fight = ["mcp__spiremind__act", "mcp__spiremind__act_sequence", "mcp__spiremind__get_deck", "mcp__spiremind__get_persona",
            "mcp__spiremind__get_state", "mcp__spiremind__say", "mcp__spiremind__wait_for_decision"];

        check.Equal(null, Started("connected", ["Read", .. fight]).Failure, "Read and the fight's tools");
        check.Equal(null, Started("connected", ["Read", .. fight]).ToolsProblem, "Read and the fight's tools are no reason to stop a batch");
        check.Equal(null, Started("connected", [.. fight, "Read"]).Failure, "the same in another order");

        foreach (var extra in new[] { "Write", "Edit", "NotebookEdit", "Bash", "Glob", "ToolSearch", "mcp__other__tool" })
        {
            var failure = Started("connected", ["Read", extra, .. fight]).Failure;
            check.That(failure is not null && failure.Contains("tools the arena didn't ask for: " + extra, StringComparison.Ordinal),
                $"a session that also lists {extra} is stopped ({failure ?? "it wasn't"})");
        }
        var both = Started("connected", ["Read", "Edit", "Write", .. fight]);
        check.That(both.Failure is not null && both.Failure.Contains("Edit, Write", StringComparison.Ordinal), "every unexpected tool is named");
        // The same would happen to every session, so it is also what stops a batch.
        check.Equal(both.Failure, both.ToolsProblem, "a tool that wasn't asked for is a problem of the tools");
        check.That(Started("connected", fight).ToolsProblem is not null, "so is a missing Read");
        check.That(Started("connected", ["Read"]).ToolsProblem is not null, "and none of the fight's tools");
        check.Equal(null, Started("failed", ["Read"]).ToolsProblem, "a dead MCP server is one fight's trouble, not the tools'");
        check.Equal(null, Started("pending", ["Read"]).ToolsProblem, "a server still pending isn't either");

        var blind = Started("connected", fight).Failure;
        check.That(blind is not null && blind.Contains("doesn't offer the model the Read tool", StringComparison.Ordinal),
            $"a session without Read is stopped ({blind ?? "it wasn't"})");

        var noTools = Started("connected", ["Read"]).Failure;
        check.That(noTools is not null && noTools.Contains("none of the fight's tools", StringComparison.Ordinal) && noTools.Contains("--tools Read", StringComparison.Ordinal),
            "a session with Read and none of the fight's tools is stopped");

        var pending = Started("pending", ["Read"]);
        check.Equal(null, pending.Failure, "a server still pending, with Read listed, is let through");
        check.That(pending.Notes.Any(note => note.Contains("still pending", StringComparison.Ordinal)), "and noted");
        var pendingWriter = Started("pending", ["Read", "Write"]).Failure;
        check.That(pendingWriter is not null && pendingWriter.Contains("Write", StringComparison.Ordinal), "a pending server doesn't excuse a tool that writes");

        var dead = Started("failed", ["Read"]).Failure;
        check.That(dead is not null && dead.Contains("MCP server is failed", StringComparison.Ordinal), "a dead MCP server is still the first thing said");
    }

    private static StreamWatch Started(string mcpStatus, string[] tools)
    {
        var watch = new StreamWatch { StrategyRoot = Repo + "/strategy" };
        watch.OnOutput(new JsonObject
        {
            ["type"] = "system", ["subtype"] = "init", ["model"] = "claude-x", ["claude_code_version"] = "2.1.296", ["apiKeySource"] = "none",
            ["permissionMode"] = "dontAsk",
            ["tools"] = new JsonArray(tools.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray()),
            ["mcp_servers"] = new JsonArray(new JsonObject { ["name"] = "spiremind", ["status"] = mcpStatus }),
        }.ToJsonString());
        return watch;
    }

    // ---------- Read calls ----------

    private static void Reads(Check check)
    {
        var watch = Started("connected", ["Read", "mcp__spiremind__act"]);
        const string wiki = Repo + "/strategy/wiki/";

        Call(watch, "t1", "Read", wiki + "enemies/vantom.md");
        check.Equal(1, watch.ReadCalls, "Read calls once one was made");
        check.Sequence([], watch.WikiPages, "a page isn't opened before its result says so");
        Answer(watch, "t1", failed: false);
        check.Sequence(["strategy/wiki/enemies/vantom.md"], watch.WikiPages, "the page that was opened");

        Call(watch, "t2", "Read", wiki + "enemies/vantom.md");
        Answer(watch, "t2", failed: false);
        Call(watch, "t3", "Read", wiki + "encounters/act1-overgrowth.md");
        Answer(watch, "t3", failed: false);
        check.Equal(3, watch.ReadCalls, "Read calls after three");
        check.Sequence(["strategy/wiki/enemies/vantom.md", "strategy/wiki/encounters/act1-overgrowth.md"], watch.WikiPages,
            "a page read twice is listed once, in the order of first opening");

        // What comes back as an error wasn't opened: a page that isn't there, a file outside the strategy folder.
        Call(watch, "t4", "Read", wiki + "enemies/jaw-worm.md");
        Answer(watch, "t4", failed: true);
        Call(watch, "t5", "Read", Repo + "/CLAUDE.md");
        Answer(watch, "t5", failed: true);
        Call(watch, "t6", "Read", Repo + "/strategy/../runs/current.md");
        Answer(watch, "t6", failed: true);
        Call(watch, "t7", "Read", "strategy/wiki/enemies/vantom.md");
        Answer(watch, "t7", failed: true);
        check.Equal(4, watch.ReadErrors, "Read calls that failed");
        check.Equal(2, watch.WikiPages.Count, "a failed Read opens no page");

        // Readable, but not a page of the wiki.
        Call(watch, "t8", "Read", wiki + "README.md");
        Answer(watch, "t8", failed: false);
        Call(watch, "t9", "Read", Repo + "/strategy/general.md");
        Answer(watch, "t9", failed: false);
        // A page however its path is spelled.
        Call(watch, "t10", "Read", Repo + "/strategy/wiki/../wiki/potions.md");
        Answer(watch, "t10", failed: false);

        check.Sequence(
        [
            "strategy/wiki/enemies/jaw-worm.md (failed)",
            Repo + "/CLAUDE.md (failed)",
            Repo + "/strategy/../runs/current.md (failed)",
            "strategy/wiki/enemies/vantom.md (failed)",
            "strategy/wiki/README.md",
            "strategy/general.md",
        ], watch.OtherReads, "every other file that was asked for");
        check.Sequence(["strategy/wiki/enemies/vantom.md", "strategy/wiki/encounters/act1-overgrowth.md", "strategy/wiki/potions.md"], watch.WikiPages, "the pages opened in the end");
        check.Equal(10, watch.ReadCalls, "Read calls in all");
        check.Equal(4, watch.ReadErrors, "Read errors in all");
        check.Equal(0, watch.ToolErrors, "a failed Read isn't one of the fight's tool errors");
        check.Equal(0, watch.GameToolCalls, "a Read isn't a call to the fight's tools");

        // The fight's own tools are counted as before, beside the reads.
        Call(watch, "g1", "mcp__spiremind__act", null);
        Answer(watch, "g1", failed: true);
        check.Equal(1, watch.GameToolCalls, "calls to the fight's tools");
        check.Equal(1, watch.ToolErrors, "the fight's tool errors");
        check.Equal(4, watch.ReadErrors, "Read errors after a game tool failed");

        // A tool the session was never given: its name is on record, and nothing was opened or written.
        Call(watch, "w1", "Write", wiki + "lessons/new.md");
        Answer(watch, "w1", failed: true);
        check.Equal(1, watch.ToolCalls.GetValueOrDefault("Write"), "a call to a tool that isn't there is on record by name");
        check.Equal(10, watch.ToolCalls.GetValueOrDefault("Read"), "tool_calls counts Read like any tool");
        check.Equal(1, watch.ToolCalls.GetValueOrDefault("act"), "tool_calls counts the fight's tools without their prefix");
        check.Equal(10, watch.ReadCalls, "a Write isn't a Read");
        check.Equal(3, watch.WikiPages.Count, "and opens no page");

        // Two calls in one message, answered in one line; and a call the session never answered.
        var two = Started("connected", ["Read", "mcp__spiremind__act"]);
        two.OnOutput(Assistant(("a", "Read", wiki + "enemies/nibbit.md"), ("b", "Read", wiki + "enemies/mawler.md")));
        two.OnOutput(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["role"] = "user", ["content"] = new JsonArray(Result("b", false), Result("a", true)) },
        }.ToJsonString());
        Call(two, "c", "Read", wiki + "enemies/byrdonis.md");
        check.Equal(3, two.ReadCalls, "three Read calls, two in one message");
        check.Sequence(["strategy/wiki/enemies/mawler.md"], two.WikiPages, "each result is matched to its own call");
        check.Sequence(["strategy/wiki/enemies/nibbit.md (failed)"], two.OtherReads, "the one that failed");
        two.NextSession();
        Answer(two, "c", failed: false);
        check.Sequence(["strategy/wiki/enemies/mawler.md"], two.WikiPages, "a call that was never answered opened nothing");
        check.Equal(3, two.ReadCalls, "the counters run on over a resumed session");

        // Without a strategy folder to go by, files are reported as they were asked for.
        var rootless = new StreamWatch();
        Call(rootless, "r1", "Read", wiki + "enemies/vantom.md");
        Answer(rootless, "r1", failed: false);
        check.Sequence([], rootless.WikiPages, "no strategy folder, no pages");
        check.Sequence([wiki + "enemies/vantom.md"], rootless.OtherReads, "the file as it was asked for");
    }

    private static void Call(StreamWatch watch, string id, string tool, string? file) => watch.OnOutput(Assistant((id, tool, file)));

    // An assistant line as Claude Code prints it, with one tool_use block per call.
    private static string Assistant(params (string Id, string Tool, string? File)[] calls) => new JsonObject
    {
        ["type"] = "assistant",
        ["message"] = new JsonObject
        {
            ["id"] = "msg_" + calls[0].Id,
            ["role"] = "assistant",
            ["content"] = new JsonArray(calls.Select(call => (JsonNode)new JsonObject
            {
                ["type"] = "tool_use", ["id"] = call.Id, ["name"] = call.Tool,
                ["input"] = call.File is null ? new JsonObject { ["action_id"] = "end_turn" } : new JsonObject { ["file_path"] = call.File },
            }).ToArray()),
            ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 1 },
        },
    }.ToJsonString();

    private static void Answer(StreamWatch watch, string id, bool failed) => watch.OnOutput(new JsonObject
    {
        ["type"] = "user",
        ["message"] = new JsonObject { ["role"] = "user", ["content"] = new JsonArray(Result(id, failed)) },
    }.ToJsonString());

    // A tool result as Claude Code prints it: is_error only when it is one.
    private static JsonNode Result(string id, bool failed)
    {
        var block = new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = id, ["content"] = failed ? "File does not exist." : "     1→# A page" };
        if (failed) block["is_error"] = true;
        return block;
    }

    // ---------- the result line and the report ----------

    private static FightResult Line(Scenario scenario, string outcome, int reads, int readErrors, string[] pages, string[] others, string? wikiHash) => new()
    {
        ScenarioId = scenario.Id, Tier = scenario.Tier, Summary = scenario.Summary, Seed = scenario.Seed, Model = "claude-x", Effort = "medium",
        Outcome = outcome, HpLeft = 40, MaxHp = 80, Turns = 5, CardsPlayed = 12,
        ToolCalls = new Dictionary<string, int> { ["wait_for_decision"] = 1, ["act_sequence"] = 5, ["Read"] = reads, ["Write"] = 1 },
        ToolErrors = 1, ReadCalls = reads, ReadErrors = readErrors, WikiPages = pages, OtherReads = others, PromptHash = "0123456789ab", WikiHash = wikiHash,
    };

    private static void ResultLine(Check check)
    {
        var scenario = new Scenario("elite-1", 1, "elite", "an elite");
        var result = Line(scenario, Outcomes.Won, 3, 1, ["strategy/wiki/enemies/byrdonis.md", "strategy/wiki/encounters/act1-overgrowth.md"],
            ["strategy/wiki/enemies/jaw-worm.md (failed)"], "ba9876543210");
        var json = result.ToJson();
        check.Equal(3, (int)(FightResult.Long(json["read_calls"]) ?? -1), "read_calls in the line");
        check.Equal(1, (int)(FightResult.Long(json["read_errors"]) ?? -1), "read_errors in the line");
        check.Equal("[\"strategy/wiki/enemies/byrdonis.md\",\"strategy/wiki/encounters/act1-overgrowth.md\"]", json["wiki_pages"]!.ToJsonString(), "wiki_pages in the line");
        check.Equal("[\"strategy/wiki/enemies/jaw-worm.md (failed)\"]", json["other_reads"]!.ToJsonString(), "other_reads in the line");
        check.Equal("ba9876543210", FightResult.Str(json["wiki_hash"]), "wiki_hash in the line");
        check.Equal(3, (int)(FightResult.Long((json["tool_calls"] as JsonObject)?["Read"]) ?? -1), "tool_calls still counts Read by name");

        var back = FightResult.FromJson((JsonObject)JsonNode.Parse(result.ToLine())!)!;
        check.Equal(3, back.ReadCalls, "read_calls read back");
        check.Equal(1, back.ReadErrors, "read_errors read back");
        check.Sequence(result.WikiPages, back.WikiPages, "wiki_pages read back");
        check.Sequence(result.OtherReads, back.OtherReads, "other_reads read back");
        check.Equal("ba9876543210", back.WikiHash, "wiki_hash read back");
        check.That(json["tools_problem"] is null && back.ToolsProblem is null, "a fight that was played has no tools_problem");
        var stopped = FightResult.FromJson((JsonObject)JsonNode.Parse((result with { Outcome = Outcomes.Error, ToolsProblem = "lists Write" }).ToLine())!)!;
        check.Equal("lists Write", stopped.ToolsProblem, "tools_problem read back");

        // A line from before the sessions could read (the pilot's): nothing was read, and it says so.
        var old = FightResult.FromJson(new JsonObject { ["scenario"] = "elite-1", ["model"] = "claude-x", ["outcome"] = "won" })!;
        check.Equal(0, old.ReadCalls, "an older line has no Read calls");
        check.Sequence([], old.WikiPages, "and no pages");
        check.Equal(null, old.WikiHash, "and no wiki hash");
    }

    private static void ReportColumns(Check check)
    {
        var scenarios = Scenarios.Generate(3, 1);
        var elite = scenarios.Single(s => s.Tier == Scenarios.EliteTier);
        var weak = scenarios.Single(s => s.Tier == Scenarios.WeakTier);
        FightResult[] results =
        [
            Line(elite, Outcomes.Won, 3, 1, ["strategy/wiki/enemies/byrdonis.md", "strategy/wiki/encounters/act1-overgrowth.md"], ["/etc/hosts (failed)"], "aaaaaaaaaaaa"),
            Line(weak, Outcomes.Won, 0, 0, [], [], "aaaaaaaaaaaa"),
        ];
        var output = new StringWriter();
        Report.Print(results, output);
        var lines = output.ToString().Split('\n').Select(line => line.TrimEnd('\r')).ToList();

        // The tool table: mean Read calls (3 and 0) and mean pages (2 and 0) per fight; the Write
        // that isn't a main tool is "other" (1 in each fight), and Read is no longer counted there.
        var header = lines.FindIndex(line => line.StartsWith("model", StringComparison.Ordinal) && line.Contains("wiki pages", StringComparison.Ordinal));
        check.That(header >= 0, "the tool table has a wiki pages column");
        if (header >= 0)
        {
            var names = Columns().Split(lines[header]);
            var row = Columns().Split(lines[header + 1].Trim());
            check.Sequence(["model", "wait_for_decision", "act_sequence", "act", "get_state", "say", "Read", "wiki pages", "other", "tool errors", "Claude Code turns"], names, "the tool table's columns");
            check.Equal("claude-x", row[0], "the tool table's row");
            check.Equal("1.5", row[Array.IndexOf(names, "Read")], "mean Read calls per fight");
            check.Equal("1.0", row[Array.IndexOf(names, "wiki pages")], "mean wiki pages per fight");
            check.Equal("1.0", row[Array.IndexOf(names, "other")], "other tools per fight, without Read");
        }
        check.That(lines.Contains("  claude-x: in 1 of 2 fights (1 of 1 elite and boss fights): encounters/act1-overgrowth.md 1, enemies/byrdonis.md 1; " +
                                  "1 of 3 Read calls failed (a file that isn't there, or one outside the strategy folder)."),
            "the line that says which pages were opened: " + string.Join(" | ", lines.Where(line => line.StartsWith("  claude-x: in", StringComparison.Ordinal))));
        check.That(!lines.Any(line => line.Contains("versions of strategy/wiki", StringComparison.Ordinal)), "one wiki, no note");

        // Fights that read different wikis are said to.
        var mixed = new StringWriter();
        Report.Print([results[0], results[1] with { WikiHash = "bbbbbbbbbbbb" }], mixed);
        check.That(mixed.ToString().Contains("Note: the wiki's pages changed between these fights (2 versions of strategy/wiki)", StringComparison.Ordinal), "two wikis are noted");
    }

    // ---------- experiments: the label and the switches ----------

    private static readonly Dictionary<string, string> NoTiers = [];

    private static void Labels(Check check)
    {
        var off = new ExperimentOptions();
        check.Equal("base", ExperimentLabel.Of(false, off, NoTiers), "no switch on");
        check.Equal("brief", ExperimentLabel.Of(true, off, NoTiers), "--brief");
        check.Equal("forecast", ExperimentLabel.Of(false, new ExperimentOptions(Forecast: true), NoTiers), "--forecast");
        check.Equal("elite=high+boss=high", ExperimentLabel.Of(false, off, new Dictionary<string, string> { ["boss"] = "high", ["elite"] = "high" }),
            "--effort-by-tier boss=high,elite=high: the tiers in their own order, however they were typed");
        check.Equal("brief+forecast+weak=low+normal=medium+elite=high+boss=max",
            ExperimentLabel.Of(true, new ExperimentOptions(true), new Dictionary<string, string> { ["boss"] = "max", ["weak"] = "low", ["elite"] = "high", ["normal"] = "medium" }),
            "every switch at once");

        check.Equal("high", ExperimentLabel.TierEffort("brief+elite=high+boss=xhigh", "elite"), "the elite effort read back from a label");
        check.Equal("xhigh", ExperimentLabel.TierEffort("brief+elite=high+boss=xhigh", "boss"), "the boss effort read back");
        check.Equal(null, ExperimentLabel.TierEffort("brief+elite=high+boss=xhigh", "weak"), "a tier the label doesn't name");
        check.Equal(null, ExperimentLabel.TierEffort("base", "elite"), "base names no tier");
        check.Equal(null, ExperimentLabel.TierEffort("brief", "boss"), "brief names no tier");
        check.Equal("elite high, boss xhigh", ExperimentLabel.TierEfforts("brief+elite=high+boss=xhigh"), "the tier efforts in words");
        check.Equal(null, ExperimentLabel.TierEfforts("brief+forecast"), "no tier efforts, no words");
        check.Sequence(["base", "brief", "forecast"], ExperimentLabel.Ordered(["forecast", "brief", "base", "brief"]), "labels in the reports' order: base first, each once");

        check.Equal(new ExperimentOptions(false), new ExperimentOptions(), "the simulator's switch is off unless set");
        check.Sequence([], ExperimentLabel.Switches(new ExperimentOptions()), "no simulator switch on");
        check.Sequence(["--forecast"], ExperimentLabel.Switches(new ExperimentOptions(true)), "the simulator's switch as it is typed");

        // A position test's own name comes first, and never hides a switch.
        check.Equal("old", ExperimentLabel.Of(false, off, NoTiers, "old"), "a batch's own name is its label");
        check.Equal("new+brief+forecast", ExperimentLabel.Of(true, new ExperimentOptions(true), NoTiers, "new"), "a name, then the switches that are on");
        check.Equal("base", ExperimentLabel.Of(false, off, NoTiers, "base"), "the name base is no name");
        check.Equal("forecast", ExperimentLabel.Of(false, new ExperimentOptions(true), NoTiers, "base"), "nor is it beside a switch");
        check.Equal(null, ExperimentLabel.TierEffort("old", "elite"), "a name names no tier's effort");
        foreach (var good in new[] { "old", "new", "prompt-2026.10.11", "rules_1-4", "A" })
            check.Equal(null, ExperimentLabel.NameProblem(good), $"'{good}' can be a batch's name");
        foreach (var bad in new[] { "", "a+b", "elite=high", "two words", "a/b", "brief", "forecast", new string('x', 41) })
            check.That(ExperimentLabel.NameProblem(bad) is not null, $"'{bad}' can't be a batch's name");
    }

    private static void Switches(Check check)
    {
        // With no switch a batch is the batch it always was.
        var plain = ArenaOptions.Parse(["run", "--models", "claude-x"]);
        check.Equal("base", plain.Experiment, "the label of a run without a switch");
        check.That(!plain.Brief && plain.Experiments == new ExperimentOptions() && plain.EffortByTier.Count == 0, "every switch is off by default");
        foreach (var tier in Scenarios.Tiers)
            check.Equal("medium", plain.EffortFor(tier), $"the effort of a {tier} fight without --effort-by-tier");

        var all = ArenaOptions.Parse(["run", "--models", "claude-x", "--effort", "low", "--brief", "--effort-by-tier", "boss=high, elite=xhigh", "--forecast"]);
        check.Equal("brief+forecast+elite=xhigh+boss=high", all.Experiment, "the label with every switch on");
        check.Sequence(["low", "low", "xhigh", "high"], Scenarios.Tiers.Select(all.EffortFor), "the effort of weak, normal, elite and boss fights");
        check.Equal(new ExperimentOptions(Forecast: true), all.Experiments, "the simulator's switch as parsed");
        // The suggested-line helper was ruled out and is no switch any more.
        check.That(Refused(["run", "--models", "claude-x", "--advisor"]) && Refused(["snapshot", "elite-1", "--advisor"]) && Refused(["position", "a", "elite-1", "claude-x", "--turn", "2", "--advisor"]),
            "there is no --advisor");
        check.That(all.Brief, "--brief as parsed");
        check.Equal("elite=high", ArenaOptions.Parse(["run", "--models", "claude-x", "--effort", "high", "--effort-by-tier", "elite=high"]).Experiment,
            "a tier named at the batch's own effort is still in the label: the label is what was asked for");

        foreach (var bad in new[] { "elite", "elite=", "=high", "elite=huge", "mini=high", "elite=high,elite=low", "elite=high=low", "", "," })
            check.That(Refused(["run", "--models", "claude-x", "--effort-by-tier", bad]), $"--effort-by-tier '{bad}' is refused");
        check.That(Refused(["run", "--models", "claude-x", "--effort-by-tier"]), "--effort-by-tier without a value is refused");
        foreach (var (command, option) in new[] { ("baseline", "--brief"), ("report", "--brief"), ("baseline", "--forecast"), ("report", "--forecast"), ("run", "--by-label"), ("compare", "--by-label") })
            check.That(Refused(command == "run" ? ["run", "--models", "claude-x", option] : command == "compare" ? ["compare", "a", "b", option] : [command, "a", option]),
                $"{option} doesn't go with {command}");
        check.That(Refused(["report", "a", "--model", "claude-x"]) && Refused(["run", "--models", "claude-x", "--model", "claude-x"]), "--model only goes with compare");
        check.That(Refused(["run", "--models", "claude-x", "--effort-by-tier", "elite=high", "--effort-by-tier"]), "a second --effort-by-tier needs its value too");

        check.That(ArenaOptions.Parse(["report", "a", "--by-label"]).ByLabel && !ArenaOptions.Parse(["report", "a"]).ByLabel, "report --by-label");
        var compare = ArenaOptions.Parse(["compare", "a", "--model", "claude-x", "b"]);
        check.Equal(ArenaCommand.Compare, compare.Command, "the compare command");
        check.Sequence(["a", "b"], compare.ComparePaths, "compare's two folders, in the order typed");
        check.Equal("claude-x", compare.CompareModel, "compare --model");
        check.Equal(null, ArenaOptions.Parse(["compare", "a", "b"]).CompareModel, "compare without --model takes every model");
        check.That(Refused(["compare", "a"]) && Refused(["compare"]) && Refused(["compare", "a", "b", "c"]), "compare takes exactly two folders");
        check.Equal(new ExperimentOptions(Forecast: true), ArenaOptions.Parse(["snapshot", "elite-1", "--forecast"]).Experiments, "snapshot takes the simulator's switches");

        static bool Refused(string[] args)
        {
            try { ArenaOptions.Parse(args); }
            catch (UsageException) { return true; }
            return false;
        }
    }

    // ---------- --brief ----------

    private const string VantomPage = "# Vantom\nA boss.\n\n## Facts\n\n## Lessons\n";

    // A checkout whose enemy pages are named so that only the first line can find them.
    private static Checkout BriefCheckout()
    {
        var checkout = new Checkout(VantomPage);
        checkout.Write("strategy/wiki/enemies/some-other-name.md", "# Leaf Slime (M)\nA slime.\n\n## Facts\nSplits.\n");
        checkout.Write("strategy/wiki/enemies/nibbit.md", "\n# Nibbit\nSmall.\n\n");
        checkout.Write("strategy/wiki/enemies/the-priest.md", "# Kin Priest\nLeads the Kin.\n");
        // Not an enemy page, whatever its first line says.
        checkout.Write("strategy/wiki/cards/byrdonis.md", "# Byrdonis\nA card group that happens to be called so.\n");
        return checkout;
    }

    private static void Brief(Check check)
    {
        using var checkout = BriefCheckout();
        var repo = checkout.Path;

        var pages = WikiBrief.PagesByName(repo);
        check.Sequence(["Kin Priest", "Leaf Slime (M)", "Nibbit", "Vantom"], pages.Keys.OrderBy(name => name, StringComparer.Ordinal), "the enemy pages by the name in their first line");
        check.Equal("strategy/wiki/enemies/some-other-name.md", pages["Leaf Slime (M)"], "a page is found by its first line, whatever the file is called");
        check.That(!pages.ContainsKey("Byrdonis") && !pages.ContainsKey("Potions"), "pages outside enemies/ are not enemy pages");

        // Each different enemy once, in the order they stand; capitals don't count; one line for the one without a page.
        var brief = WikiBrief.For(repo, ["Leaf Slime (M)", "Vantom", "Leaf Slime (M)", "Byrdonis", "nibbit"]);
        check.Sequence(["strategy/wiki/enemies/some-other-name.md", "strategy/wiki/enemies/vantom.md", "strategy/wiki/enemies/nibbit.md"], brief.Pages, "the pages given");
        check.Sequence(["Byrdonis"], brief.Missing, "the enemy without a page");
        // 43 characters of the slime's page, 38 of Vantom's, 15 of the Nibbit's, each without the blank lines around it.
        check.Equal(96, brief.Chars, "the pages' length together");
        check.Equal(
            "## What the wiki says about this fight\n\n" +
            "<file path=\"strategy/wiki/enemies/some-other-name.md\">\n# Leaf Slime (M)\nA slime.\n\n## Facts\nSplits.\n</file>\n\n" +
            "<file path=\"strategy/wiki/enemies/vantom.md\">\n# Vantom\nA boss.\n\n## Facts\n\n## Lessons\n</file>\n\n" +
            "The wiki has no page for Byrdonis.\n\n" +
            "<file path=\"strategy/wiki/enemies/nibbit.md\">\n# Nibbit\nSmall.\n</file>",
            brief.Section, "the section: its heading, each page whole, one line for the enemy without one, and nothing else");

        var none = WikiBrief.For(repo, ["Byrdonis"]);
        check.Equal("## What the wiki says about this fight\n\nThe wiki has no page for Byrdonis.", none.Section, "the section of a fight whose enemy has no page");
        check.That(none.Pages.Count == 0 && none.Chars == 0, "no page, no length");

        // The first message: the plain one, then the section, and the end.
        check.Equal(ArenaPrompt.UserPrompt + "\n\n" + brief.Section, ArenaPrompt.UserPromptWith(brief), "the first message under --brief");
        check.That(!ArenaPrompt.UserPrompt.Contains(WikiBrief.Heading, StringComparison.Ordinal) && !ArenaPrompt.ResumePrompt.Contains(WikiBrief.Heading, StringComparison.Ordinal),
            "neither the plain first message nor the resume prompt speaks of the section");

        // The appendix: one more line under --brief, and without the switch not a letter moved.
        var plainNotes = ArenaPrompt.ArenaNotes(repo);
        var briefNotes = ArenaPrompt.ArenaNotes(repo, brief: true);
        check.Equal(plainNotes, ArenaPrompt.ArenaNotes(repo, brief: false), "the appendix without the switch is the appendix as it was");
        var added = briefNotes.Split('\n').Except(plainNotes.Split('\n')).ToList();
        check.Equal(1, added.Count, "lines --brief adds to the appendix");
        if (added.Count == 1)
        {
            check.That(added[0].StartsWith("- The pages of this fight's enemies are the exception: their full text is already in your first message, under `What the wiki says about this fight`", StringComparison.Ordinal),
                "the added line says the pages are already there: " + added[0]);
            check.That(added[0].Contains("Don't open those again", StringComparison.Ordinal), "and that they need not be opened again");
            check.Equal(plainNotes, briefNotes.Replace(added[0] + "\n", ""), "the appendix under --brief without that line");
            check.That(!added[0].Contains(repo, StringComparison.Ordinal), "the added line doesn't spell the checkout's path");
        }
        var plain = ArenaPrompt.Build(repo).Text;
        var briefed = ArenaPrompt.Build(repo, brief: true).Text;
        check.That(briefed.EndsWith(briefNotes + "\n", StringComparison.Ordinal) && plain.EndsWith(plainNotes + "\n", StringComparison.Ordinal), "each system text ends with its own appendix");
        check.Equal(plain[..^(plainNotes.Length + 1)], briefed[..^(briefNotes.Length + 1)], "above the appendix the two system texts are the same");
        check.That(!plain.Contains(WikiBrief.Heading, StringComparison.Ordinal), "the plain system text never speaks of the section");
        // One hash for both, so a hash that differs between two runs means the notes changed, not the switch.
        check.Equal(ArenaPrompt.Hash(plain, repo), ArenaPrompt.Hash(briefed, repo), "the prompt hash under --brief is the plain run's");

        // The enemies are read off a snapshot's state.enemies[].name, each different one once.
        var snapshot = new GameSnapshot
        {
            Screen = "combat", AwaitingDecision = true,
            State = new JsonObject
            {
                ["enemies"] = new JsonArray(
                    new JsonObject { ["i"] = 0, ["name"] = "Kin Follower" }, new JsonObject { ["i"] = 1, ["name"] = "Kin Follower" },
                    new JsonObject { ["i"] = 2, ["name"] = "Kin Priest" }, new JsonObject { ["i"] = 3 }),
            },
        };
        check.Sequence(["Kin Follower", "Kin Priest"], WikiBrief.Enemies(snapshot), "the enemies of a snapshot");
        check.Sequence([], WikiBrief.Enemies(new GameSnapshot { Screen = "combat" }), "a snapshot without enemies");

        // A fight of the batch Opus played (seed 7): two Kin Followers and the Kin Priest.
        check.Sequence(["Kin Follower", "Kin Priest"], WikiBrief.Enemies(new ArenaGame(Scenarios.FromId("boss-878005"))), "the enemies boss-878005 starts with");
        // And whatever the generator deals: the first snapshot names the line-up its summary ends with.
        foreach (var scenario in Scenarios.Generate(10, 1))
        {
            var lineUp = scenario.Summary[(scenario.Summary.LastIndexOf(" vs ", StringComparison.Ordinal) + 4)..];
            lineUp = lineUp[..lineUp.LastIndexOf(" (", StringComparison.Ordinal)];
            check.Sequence(lineUp.Split(" + ").Distinct(), WikiBrief.Enemies(new ArenaGame(scenario)), $"the enemies {scenario.Id} starts with");
        }

        // Looking at a fight's first snapshot changes nothing about it: the same first snapshot,
        // and the same one after the same first action.
        var fight = Scenarios.Generate(1, 1)[0];
        var looked = new ArenaGame(fight) { ActionDelay = TimeSpan.Zero };
        var untouched = new ArenaGame(fight) { ActionDelay = TimeSpan.Zero };
        WikiBrief.Enemies(looked);
        check.Equal(Shown(untouched), Shown(looked), "the first snapshot of a fight whose enemies were read off it");
        var action = untouched.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult().Actions[0].Id;
        looked.ExecuteAsync(action, CancellationToken.None).GetAwaiter().GetResult();
        untouched.ExecuteAsync(action, CancellationToken.None).GetAwaiter().GetResult();
        check.Equal(Shown(untouched), Shown(looked), $"the snapshot after {action}");

        static string Shown(ArenaGame game) => game.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult().ToJson().ToJsonString();
    }

    // What a fight's sessions are started with, through a launcher that starts nothing: every
    // session "ends" at once without a tool call, so the fight is resumed until the runner gives up.
    private static void BriefFight(Check check)
    {
        using var checkout = BriefCheckout();
        var repo = checkout.Path;
        var scenario = Scenarios.FromId("boss-878005"); // Kin Follower + Kin Follower + Kin Priest
        var section = WikiBrief.For(repo, ["Kin Follower", "Kin Priest"]).Section;
        check.Equal(
            "## What the wiki says about this fight\n\nThe wiki has no page for Kin Follower.\n\n" +
            "<file path=\"strategy/wiki/enemies/the-priest.md\">\n# Kin Priest\nLeads the Kin.\n</file>", section, "the section for this fight");

        var launcher = new NoLauncher();
        var fight = new ClaudeFight
        {
            Scenario = scenario, Model = "claude-x", Effort = "high", Brief = true,
            Experiments = new ExperimentOptions(Forecast: true), Experiment = "brief+forecast+boss=high",
            ClaudePath = "/nowhere/claude", SystemPrompt = ArenaPrompt.Build(repo, brief: true).Text, Repo = repo,
            Environment = new Dictionary<string, string?>(), Launcher = launcher,
        };
        var result = fight.RunAsync(CancellationToken.None).GetAwaiter().GetResult();

        check.Equal(1 + ClaudeFight.MaxResumes, launcher.Started.Count, "sessions started: the first and every resume");
        if (launcher.Started.Count == 1 + ClaudeFight.MaxResumes)
        {
            var first = launcher.Started[0].Arguments.ToList();
            check.Equal(ArenaPrompt.UserPrompt + "\n\n" + section, Values(first, "-p").Single(), "the first session's prompt ends with the section");
            check.That(first.Contains("--session-id") && !first.Contains("--resume"), "the first session is a new one");
            var id = Values(first, "--session-id").Single();
            foreach (var (spec, n) in launcher.Started.Skip(1).Select((spec, n) => (spec, n + 1)))
            {
                var args = spec.Arguments.ToList();
                check.Equal(ArenaPrompt.ResumePrompt, args[args.IndexOf("-p") + 1], $"resume {n} gets the resume prompt and no pages");
                check.Sequence([id], Values(args, "--resume"), $"resume {n} carries on the same session");
            }
            check.That(launcher.Started.All(spec => Values(spec.Arguments.ToList(), "--effort").SequenceEqual(["high"])), "every session is started at the fight's effort");
            check.That(launcher.Started.All(spec => Values(spec.Arguments.ToList(), "--append-system-prompt").Single() == fight.SystemPrompt), "every session gets the same system prompt");
        }

        check.Equal("high", result.Effort, "the effort in the result is the one the sessions were started with");
        check.Equal("brief+forecast+boss=high", result.Experiment, "the label in the result");
        check.Sequence(["strategy/wiki/enemies/the-priest.md"], result.BriefPages ?? ["(none)"], "the pages given, in the result");
        check.Equal(27, result.BriefChars ?? -1, "their length: \"# Kin Priest\" and \"Leads the Kin.\" with the line break between");
        check.Sequence(["Kin Follower"], result.BriefMissing, "the enemy without a page, in the result");
        check.That(result.Notes.Contains("--brief: the wiki has no page for Kin Follower"), "and in its notes: " + string.Join(" | ", result.Notes));
        check.Equal(ClaudeFight.MaxResumes, result.Resumes, "resumes");
        check.Equal(Outcomes.Error, result.Outcome, "a fight no session made a call in wasn't played");

        check.Equal(new ExperimentOptions(Forecast: true), fight.NewGame().Experiments, "the simulator's switches reach the fight");

        // Without the switch: the plain prompt, no pages in the result, the switches off.
        var plainLauncher = new NoLauncher();
        var plain = new ClaudeFight
        {
            Scenario = scenario, Model = "claude-x", Effort = "medium",
            ClaudePath = "/nowhere/claude", SystemPrompt = ArenaPrompt.Build(repo).Text, Repo = repo,
            Environment = new Dictionary<string, string?>(), Launcher = plainLauncher,
        };
        var plainResult = plain.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        check.Equal(ArenaPrompt.UserPrompt, plainLauncher.Started.Count > 0 ? Values(plainLauncher.Started[0].Arguments.ToList(), "-p").Single() : null, "without --brief the first prompt is the plain one");
        check.That(plainResult.BriefPages is null && plainResult.BriefChars is null && plainResult.BriefMissing.Count == 0, "and the result holds nothing about pages given");
        check.That(plainResult.ToJson()["brief_pages"] is null && plainResult.ToJson()["brief_chars"] is null && plainResult.ToJson()["brief_missing"] is null, "nor does its line");
        check.Equal("base", plainResult.Experiment, "the label of a fight without a switch");
        check.Equal(new ExperimentOptions(), plain.NewGame().Experiments, "and its fight has the simulator's switches off");
    }

    /// <summary>A launcher that starts nothing: each "session" prints an init line that lists Read
    /// and the fight's tools and a result line, and has exited with 0, as Claude Code does when the
    /// model answers without a tool call. It keeps what it was asked to start.</summary>
    private sealed class NoLauncher : IProcessLauncher
    {
        public List<ChildProcessSpec> Started { get; } = [];

        public IChildProcess Start(ChildProcessSpec spec)
        {
            Started.Add(spec);
            return new Ended(
                new JsonObject
                {
                    ["type"] = "system", ["subtype"] = "init", ["model"] = "claude-x", ["claude_code_version"] = "0.0.0-check", ["apiKeySource"] = "none",
                    ["tools"] = new JsonArray("Read", "mcp__spiremind__act", "mcp__spiremind__wait_for_decision"),
                    ["mcp_servers"] = new JsonArray(new JsonObject { ["name"] = "spiremind", ["status"] = "connected" }),
                }.ToJsonString(),
                new JsonObject
                {
                    ["type"] = "result", ["subtype"] = "success", ["is_error"] = false, ["result"] = "Done for now.", ["num_turns"] = 1, ["duration_ms"] = 1,
                    ["total_cost_usd"] = 0.01, ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 1 },
                }.ToJsonString());
        }

        private sealed class Ended : IChildProcess
        {
            private readonly Channel<string> _output = Channel.CreateUnbounded<string>();
            private readonly Channel<string> _error = Channel.CreateUnbounded<string>();

            public Ended(params string[] lines)
            {
                foreach (var line in lines)
                    _output.Writer.TryWrite(line);
                _output.Writer.Complete();
                _error.Writer.Complete();
            }

            public int Id => 0;
            public ChannelReader<string> StandardOutput => _output.Reader;
            public ChannelReader<string> StandardError => _error.Reader;
            public Task<int> Exited { get; } = Task.FromResult(0);
            public bool Interrupt() => true;
            public void Kill() { }
            public void Dispose() { }
        }
    }

    // ---------- when a fight is already played ----------

    private static FightResult Played(Scenario scenario, string effort, string label, string outcome = Outcomes.Won, string model = "claude-x") => new()
    {
        ScenarioId = scenario.Id, Tier = scenario.Tier, Summary = scenario.Summary, Seed = scenario.Seed, Model = model,
        Effort = effort, Experiment = label, Outcome = outcome,
    };

    private static void SameFight(Check check)
    {
        var weak = new Scenario("weak-1", 1, "weak", "a weak fight");
        var elite = new Scenario("elite-2", 2, "elite", "an elite");
        FightResult[] earlier =
        [
            Played(weak, "medium", "base"), Played(elite, "medium", "base"),
            Played(elite, "high", "elite=high", Outcomes.Error),
        ];
        bool Skips(string[] switches, Scenario scenario, IReadOnlyList<FightResult>? lines = null, string model = "claude-x") =>
            RunCommand.AlreadyPlayed(lines ?? earlier, ArenaOptions.Parse(["run", "--models", "claude-x", .. switches]))(scenario, model);

        check.That(Skips([], weak) && Skips([], elite), "the same command again skips what it has played");
        check.That(!Skips([], weak, model: "claude-y"), "another model's fight isn't skipped");
        check.That(!Skips(["--effort", "high"], weak) && !Skips(["--effort", "high"], elite), "another effort plays every fight again");
        check.That(!Skips(["--brief"], weak) && !Skips(["--brief"], elite), "another label plays every fight again, at the same effort too");
        check.That(!Skips(["--forecast"], weak) && !Skips(["--forecast"], elite) && !Skips(["--brief", "--forecast"], elite), "so does the simulator's switch");
        // --effort-by-tier elite=high: the weak fight is at medium as in the base run, and still its own fight.
        check.That(!Skips(["--effort-by-tier", "elite=high"], weak), "a weak fight under elite=high isn't the base run's weak fight");
        check.That(!Skips(["--effort-by-tier", "elite=high"], elite), "an error is played again");

        FightResult[] more = [.. earlier, Played(weak, "medium", "elite=high"), Played(elite, "high", "elite=high", Outcomes.Lost)];
        check.That(Skips(["--effort-by-tier", "elite=high"], weak, more) && Skips(["--effort-by-tier", "elite=high"], elite, more), "the tiered batch again skips its own fights");
        check.That(Skips([], weak, more) && Skips([], elite, more), "and the base run still skips its own");
        // The same label under another --effort: the elite fight is played at high either way, so it is the same fight.
        check.That(Skips(["--effort", "low", "--effort-by-tier", "elite=high"], elite, more), "an elite fight at high under elite=high is the same fight whatever --effort is");
        check.That(!Skips(["--effort", "low", "--effort-by-tier", "elite=high"], weak, more), "the weak fight at low is not the one played at medium");
        check.That(!Skips(["--effort", "high"], elite, more), "an elite fight at high under base is not the one played under elite=high");

        check.Equal(("elite-2", "claude-x", "high", "elite=high"), more[^1].Identity, "what makes a line the same fight");

        // A line from before there were labels is a base fight.
        var old = FightResult.FromJson(new JsonObject { ["scenario"] = "weak-1", ["tier"] = "weak", ["model"] = "claude-x", ["effort"] = "medium", ["outcome"] = "won" })!;
        check.Equal("base", old.Experiment, "the label of a line without one");
        check.That(Skips([], weak, [old]) && !Skips(["--brief"], weak, [old]), "so the base run skips it and an experiment doesn't");

        // Transcripts: the plain run's name as ever, an experiment's with its label.
        check.Equal("elite-2__claude-opus-5-5.jsonl", RunCommand.TranscriptName("elite-2", "claude-opus-5-5", "base"), "a plain run's transcript");
        check.Equal("elite-2__claude-opus-5-5__brief_elite_high_boss_high.jsonl", RunCommand.TranscriptName("elite-2", "claude-opus-5-5", "brief+elite=high+boss=high"), "an experiment's transcript");
    }

    private static void ExperimentLine(Check check)
    {
        var scenario = new Scenario("elite-1", 1, "elite", "an elite");
        var plain = Line(scenario, Outcomes.Won, 0, 0, [], [], null);
        var json = plain.ToJson();
        check.Equal("base", FightResult.Str(json["experiment"]), "a line without a switch says base");
        var keys = json.Select(pair => pair.Key).ToList();
        check.Equal(keys.IndexOf("effort") + 1, keys.IndexOf("experiment"), "experiment stands right after effort");
        check.That(!keys.Any(key => key.StartsWith("brief", StringComparison.Ordinal)), "and has no brief_ fields");
        check.Equal("medium", plain.BaseEffort, "the --effort of a base line is its effort");

        var switched = plain with
        {
            Effort = "high", Experiment = "brief+elite=high",
            BriefPages = ["strategy/wiki/enemies/byrdonis.md"], BriefChars = 2300, BriefMissing = ["Wriggler"],
        };
        var line = switched.ToJson();
        check.Equal("brief+elite=high", FightResult.Str(line["experiment"]), "experiment in the line");
        check.Equal("high", FightResult.Str(line["effort"]), "effort in the line");
        check.Equal("[\"strategy/wiki/enemies/byrdonis.md\"]", line["brief_pages"]!.ToJsonString(), "brief_pages in the line");
        check.Equal(2300, (int)(FightResult.Long(line["brief_chars"]) ?? -1), "brief_chars in the line");
        check.Equal("[\"Wriggler\"]", line["brief_missing"]!.ToJsonString(), "brief_missing in the line");
        var back = FightResult.FromJson((JsonObject)JsonNode.Parse(switched.ToLine())!)!;
        check.Equal("brief+elite=high", back.Experiment, "experiment read back");
        check.Sequence(switched.BriefPages!, back.BriefPages ?? [], "brief_pages read back");
        check.Equal(2300, back.BriefChars ?? -1, "brief_chars read back");
        check.Sequence(["Wriggler"], back.BriefMissing, "brief_missing read back");
        // An elite fight under elite=high would have been played at high under any --effort.
        check.Equal(null, back.BaseEffort, "an elite line under elite=high can't tell the batch's --effort");
        check.Equal("medium", (back with { Tier = "weak", Effort = "medium" }).BaseEffort, "a weak line under elite=high can");

        // A fight given no page at all still says that it was briefed.
        var empty = FightResult.FromJson((JsonObject)JsonNode.Parse((plain with { Experiment = "brief", BriefPages = [], BriefChars = 0, BriefMissing = ["Mawler"] }).ToLine())!)!;
        check.That(empty.BriefPages is { Count: 0 } && empty.BriefChars == 0, "a briefed fight with no page reads back as briefed, with none");
        check.That(FightResult.FromJson((JsonObject)JsonNode.Parse(plain.ToLine())!)!.BriefPages is null, "and a plain one as not briefed");
    }

    // ---------- the report, by effort and by label ----------

    // A fight on a scenario no build can generate again (so the report doesn't replay the policies on it).
    private static FightResult Fought(string id, string tier, string label, string effort, int? hp, string model = "claude-x", string? outcome = null, string? prompt = "0123456789ab") => new()
    {
        ScenarioId = id, Tier = tier, Summary = $"a {tier} fight, vs Something ({id})", Model = model, Effort = effort, Experiment = label,
        Outcome = outcome ?? (hp is null ? Outcomes.Lost : Outcomes.Won), HpLeft = hp ?? 0, MaxHp = 80, Turns = 5, CardsPlayed = 10, PromptHash = prompt, WikiHash = "aaaaaaaaaaaa",
    };

    private static void EffortGroups(Check check)
    {
        // One batch run with --effort medium --effort-by-tier elite=high.
        FightResult[] tiered =
        [
            Fought("weak-x1", "weak", "elite=high", "medium", 70), Fought("elite-x2", "elite", "elite=high", "high", 20),
        ];
        check.Sequence([new Report.Player("claude-x", "medium", "elite=high")], Report.PlayersOf(tiered), "a batch with --effort-by-tier is one player, not one per effort");
        var output = new StringWriter();
        Report.Print(tiered, output);
        var header = output.ToString().Split('\n')[0];
        check.Equal("Arena report: 2 fights on 2 scenarios by 1 model, effort medium (elite high), experiment elite=high.", header.TrimEnd('\r'), "the header of a tiered run's report");
        var row = output.ToString().Split('\n').Select(line => Columns().Split(line.Trim())).FirstOrDefault(cells => cells[0] == "claude-x" && cells.Length > 10);
        check.Sequence(["claude-x", "2", "2"], row?.Take(3) ?? [], "its one row: both fights, both won");

        // The same label under two --efforts: two players, and the elite fight (high either way) is in both.
        FightResult[] twice = [.. tiered, Fought("weak-x1", "weak", "elite=high", "low", 60)];
        var players = Report.PlayersOf(twice);
        check.Sequence([new Report.Player("claude-x", "low", "elite=high"), new Report.Player("claude-x", "medium", "elite=high")], players, "the same label under two --efforts");
        check.That(players.All(p => p.Played(tiered[1])), "the elite fight at high belongs to both");
        check.That(players[1].Played(tiered[0]) && !players[0].Played(tiered[0]), "the weak fight at medium only to the one at medium");
        var sheet = new Report.Sheet(twice, null, withLabel: true);
        check.Sequence(["claude-x (low) [elite=high]", "claude-x (medium) [elite=high]"], sheet.Names, "their names in a table by label");
        check.Equal(60, sheet.Fight("weak-x1", sheet.Names[0])?.HpLeft ?? -1, "the weak fight of the player at low");
        check.Equal(70, sheet.Fight("weak-x1", sheet.Names[1])?.HpLeft ?? -1, "the weak fight of the player at medium");
        check.Equal(3, sheet.All.Count, "every fight once, the shared one too");

        // A label that names every tier played: no line can tell the batch's --effort, and none is needed.
        check.Sequence([new Report.Player("claude-x", null, "elite=high")], Report.PlayersOf([tiered[1]]), "only fights of a tier the label names");
        check.Equal("experiment elite=high, effort by tier (elite high)", new Report.Player("claude-x", null, "elite=high").Describe(), "such a player in words");
        check.Equal("experiment brief, effort medium", new Report.Player("claude-x", "medium", "brief").Describe(), "a player in words");

        // A plain run at two efforts is two players, as it always was.
        FightResult[] efforts = [Fought("weak-x1", "weak", "base", "high", 70), Fought("weak-x1", "weak", "base", "medium", 65)];
        check.Sequence(["claude-x (medium)", "claude-x (high)"], new Report.Sheet(efforts, null, withLabel: false).Names, "a model at two efforts, from low to high");
        check.Sequence(["claude-x"], new Report.Sheet([efforts[0]], null, withLabel: false).Names, "a model at one effort has no effort in its name");
        // Models keep the order of --models; under each, base comes first.
        FightResult[] mixed = [Fought("weak-x1", "weak", "brief", "medium", 70, "claude-b"), Fought("weak-x1", "weak", "base", "medium", 70, "claude-b"), Fought("weak-x1", "weak", "brief", "medium", 70, "claude-a")];
        check.Sequence(["claude-b [base]", "claude-b [brief]", "claude-a [brief]"], new Report.Sheet(mixed, ["claude-b", "claude-a"], withLabel: true).Names, "rows by model, then label");
    }

    // Six scenarios, played once without a switch and once under --brief. HP left (L is a loss):
    //            weak-x1  normal-x2  normal-x3  elite-x4  elite-x5  boss-x6
    //   base       70        40         30         L         12        L
    //   brief      75        44         30         9         L         3
    //   brief-base +5        +4          0        +9        -12       +3
    private static FightResult[] SixFights(string label, int?[] hp, string effort = "medium", string? prompt = "0123456789ab")
    {
        (string Id, string Tier)[] scenarios = [("weak-x1", "weak"), ("normal-x2", "normal"), ("normal-x3", "normal"), ("elite-x4", "elite"), ("elite-x5", "elite"), ("boss-x6", "boss")];
        return scenarios.Select((s, i) => Fought(s.Id, s.Tier, label, effort, hp[i], prompt: prompt)).ToArray();
    }

    private static readonly int?[] BaseHp = [70, 40, 30, null, 12, null];
    private static readonly int?[] BriefHp = [75, 44, 30, 9, null, 3];

    // The rows both tables must show for brief against base, after the pair's name. All six:
    // brief won 5 and base 4; brief had more HP in 4 fights, the same in 1, less in 1; the mean of
    // +5, +4, 0, +9, -12, +3 is 9/6 = +1.5; the sign test for 4 against 1 is 2 x (1 + 5)/32 = 0.375.
    private static readonly string[][] BriefAgainstBase =
    [
        ["all", "6", "5 vs 4", "4", "1", "1", "+1.5 HP", "0.38"],
        ["weak", "1", "1 vs 1", "1", "0", "0", "+5.0 HP", "1.00"],     // +5; 1 against 0 is 2 x 1/2
        ["normal", "2", "2 vs 2", "1", "1", "0", "+2.0 HP", "1.00"],   // +4 and 0
        ["elite", "2", "1 vs 1", "1", "0", "1", "-1.5 HP", "1.00"],    // +9 and -12; 1 against 1
        ["boss", "1", "1 vs 0", "1", "0", "0", "+3.0 HP", "1.00"],     // +3
    ];

    private static void LabelReport(Check check)
    {
        var plain = SixFights("base", BaseHp);
        var brief = SixFights("brief", BriefHp);
        FightResult[] both = [.. plain, .. brief];

        // The plain report refuses to pool two labels, and says how to see them.
        var refusal = Report.MixedLabels(both, "results.jsonl");
        check.Equal("results.jsonl holds fights of 2 experiments (base 6, brief 6). Fights played under different switches are not pooled into one table. " +
                    "Run report with --by-label to see each model once per label and the labels compared fight by fight.", refusal, "what the plain report says of two labels");
        check.Equal(null, Report.MixedLabels(plain), "one label is nothing to refuse");
        var refused = new StringWriter();
        Report.Print(both, refused);
        check.Equal(Report.MixedLabels(both) + Environment.NewLine, refused.ToString(), "Print of two labels prints the refusal and no table");

        var one = new StringWriter();
        Report.Print(brief, one);
        check.Equal("Arena report: 6 fights on 6 scenarios by 1 model, effort medium, experiment brief.", one.ToString().Split('\n')[0].TrimEnd('\r'), "the header names the experiment");
        var baseOnly = new StringWriter();
        Report.Print(plain, baseOnly);
        check.Equal("Arena report: 6 fights on 6 scenarios by 1 model, effort medium, experiment base.", baseOnly.ToString().Split('\n')[0].TrimEnd('\r'), "also when it is base");

        // The numbers themselves.
        var pairs = brief.Zip(plain, (first, second) => (first, second)).ToList();
        check.Equal(new Report.PairStats(6, 5, 4, 4, 1, 1, 1.5, 0.375), Report.PairStats.Of(pairs), "brief against base over the six fights");
        check.Equal(new Report.PairStats(2, 1, 1, 1, 0, 1, -1.5, 1.0), Report.PairStats.Of(pairs.Where(p => p.first.Tier == "elite").ToList()), "over the two elites");
        check.Equal(new Report.PairStats(0, 0, 0, 0, 0, 0, 0, 1.0), Report.PairStats.Of([]), "over nothing");

        // report --by-label: a row per label, and the labels compared.
        var output = new StringWriter();
        Report.PrintByLabel(both, output);
        var lines = output.ToString().Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        check.Equal("Arena report by experiment: 12 fights on 6 scenarios by 1 model under 2 experiments (base, brief).", lines[0], "the header by label");
        var rows = lines.Select(line => Columns().Split(line.Trim())).ToList();
        check.Sequence(["claude-x [base]", "6", "4"], rows.FirstOrDefault(cells => cells[0] == "claude-x [base]" && cells.Length > 10)?.Take(3) ?? [], "the base row: 6 fights, 4 won");
        check.Sequence(["claude-x [brief]", "6", "5"], rows.FirstOrDefault(cells => cells[0] == "claude-x [brief]" && cells.Length > 10)?.Take(3) ?? [], "the brief row: 6 fights, 5 won");
        PairTable(check, lines, "claude-x [brief] vs claude-x [base]", "by label");
        check.That(lines.Any(line => line.StartsWith("Paired comparison between experiments:", StringComparison.Ordinal)), "the comparison is announced");
        check.That(!lines.Any(line => line.StartsWith("Paired comparison: who had more HP", StringComparison.Ordinal)), "and the comparison between models is not printed in its place");

        // One label only: nothing to compare, and it says so.
        var alone = new StringWriter();
        Report.PrintByLabel(brief, alone);
        check.That(alone.ToString().Contains("nothing to compare: every model is here under one label and effort.", StringComparison.Ordinal), "by label with one label");

        // A fight one side gave up on is no pair, and is counted as dropped.
        FightResult[] gaveUp = [.. plain, .. brief.Select(r => r.ScenarioId == "boss-x6" ? r with { Outcome = Outcomes.Abandoned } : r)];
        var dropped = new StringWriter();
        Report.PrintByLabel(gaveUp, dropped);
        check.That(dropped.ToString().Contains("Pairs dropped: claude-x [brief] vs claude-x [base] 1.", StringComparison.Ordinal), "a pair with an abandoned fight is dropped and counted");
    }

    // The table of a pair of variants: the pair's own row and one per tier, cell by cell.
    private static void PairTable(Check check, List<string> lines, string pair, string where)
    {
        var at = lines.FindIndex(line => line.Trim().StartsWith(pair + "  ", StringComparison.Ordinal));
        check.That(at > 0, $"{where}: the table has a row for {pair}");
        if (at <= 0)
            return;
        check.Sequence(["pair", "tier", "fights", "won", "better", "same", "worse", "HP left difference", "sign test p"], Columns().Split(lines[at - 1].Trim()), $"{where}: the table's columns");
        check.Sequence([pair, .. BriefAgainstBase[0]], Columns().Split(lines[at].Trim()), $"{where}: all six fights");
        for (var tier = 1; tier < BriefAgainstBase.Length; tier++)
            check.Sequence(BriefAgainstBase[tier], Columns().Split(lines[at + tier].Trim()), $"{where}: the {BriefAgainstBase[tier][0]} row");
    }

    private static void Compare(Check check)
    {
        var plain = SixFights("base", BaseHp);
        var brief = SixFights("brief", BriefHp);

        var output = new StringWriter();
        check.Equal(0, CompareCommand.Print(plain, brief, output, "runs/a", "runs/b"), "compare's exit code");
        var lines = output.ToString().Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        check.That(lines.Contains("  A: runs/a") && lines.Contains("     claude-x: experiment base, effort medium, 6 fights that count on 6 scenarios"), "it says what A is");
        check.That(lines.Contains("  B: runs/b") && lines.Contains("     claude-x: experiment brief, effort medium, 6 fights that count on 6 scenarios"), "and what B is");
        // B against A is brief against base: the same numbers as by label.
        PairTable(check, lines, "claude-x: B vs A", "compare");
        // Won in one and lost in the other: elite-x4 (lost, then won with 9), elite-x5 (won with 12, then lost), boss-x6 (lost, then won with 3).
        check.That(lines.Contains("3 scenarios' outcomes flipped (won in one run, lost in the other; W and the HP left, L for a loss):"), "it counts the flips");
        check.Sequence(
        [
            "  elite-x4  elite  claude-x  A L -> B W 9  vs Something (elite-x4)",
            "  elite-x5  elite  claude-x  A W 12 -> B L  vs Something (elite-x5)",
            "  boss-x6  boss  claude-x  A L -> B W 3  vs Something (boss-x6)",
        ], lines.Where(line => line.Contains(" -> B ", StringComparison.Ordinal)), "the scenarios whose outcome flipped");
        check.That(!lines.Any(line => line.StartsWith("Note:", StringComparison.Ordinal)), "same notes, same wiki, two variants: nothing to warn of");
        check.That(!lines.Any(line => line.Contains("Scenarios left out", StringComparison.Ordinal)), "and nothing left out");

        // The other way round every sign turns.
        var turned = new StringWriter();
        CompareCommand.Print(brief, plain, turned);
        var all = turned.ToString().Split('\n').Select(line => Columns().Split(line.Trim())).FirstOrDefault(cells => cells[0] == "claude-x: B vs A");
        check.Sequence(["claude-x: B vs A", "all", "6", "4 vs 5", "1", "1", "4", "-1.5 HP", "0.38"], all ?? [], "base against brief");

        // What a pair needs: both fights played to the end. And what only one run has is counted.
        FightResult[] shorter = [.. brief.Take(4), brief[4] with { Outcome = Outcomes.Timeout }];
        var partial = new StringWriter();
        CompareCommand.Print(plain, shorter, partial);
        check.That(partial.ToString().Contains("claude-x: 1 only A has, 0 only B has, 1 that a side didn't play to the end", StringComparison.Ordinal), "what was left out of the pairs: " +
            partial.ToString().Split('\n').FirstOrDefault(line => line.Contains("left out", StringComparison.Ordinal)));
        var four = partial.ToString().Split('\n').Select(line => Columns().Split(line.Trim())).FirstOrDefault(cells => cells[0] == "claude-x: B vs A");
        // weak +5, normal +4 and 0, elite-x4 +9: 18/4 = +4.5; 3 better, 1 the same; 3 against 0 is 2 x 1/8.
        check.Sequence(["claude-x: B vs A", "all", "4", "4 vs 3", "3", "1", "0", "+4.5 HP", "0.25"], four ?? [], "the four pairs that are left");

        // Two runs of the same variant show how much a fight varies, not what a switch does; other notes are a warning.
        var again = new StringWriter();
        CompareCommand.Print(plain, SixFights("base", BriefHp, prompt: "ba9876543210"), again);
        check.That(again.ToString().Contains("Note: A and B hold claude-x as the same variant (experiment base, effort medium)", StringComparison.Ordinal), "the same variant twice is said to be");
        check.That(again.ToString().Contains("Note: the two runs were played with different system prompts (A 0123456789ab; B ba9876543210)", StringComparison.Ordinal), "and so are different prompts");

        // Refusals: no model in common, a model that isn't in both, a folder with two variants of a model.
        var none = new StringWriter();
        check.Equal(1, CompareCommand.Print(plain, brief.Select(r => r with { Model = "claude-y" }).ToList(), none), "no model in common");
        check.That(none.ToString().StartsWith("The two runs share no model (A: claude-x; B: claude-y)", StringComparison.Ordinal), "and it says so");
        check.Equal(1, CompareCommand.Print(plain, brief, new StringWriter(), model: "claude-y"), "--model names a model that isn't there");
        check.Equal(0, CompareCommand.Print(plain, brief, new StringWriter(), model: "claude-x"), "--model names the one that is");
        var two = new StringWriter();
        check.Equal(1, CompareCommand.Print([.. plain, .. brief], brief, two, "runs/a", "runs/b"), "a folder with two variants of the model");
        check.That(two.ToString().StartsWith("A (runs/a) holds claude-x in 2 variants (experiment base, effort medium; experiment brief, effort medium)", StringComparison.Ordinal),
            "and it says which: " + two.ToString().Split('\n')[0]);
    }

    // ---------- helpers ----------

    // The values after a flag that takes several, up to the next flag.
    private static List<string> Values(List<string> args, string flag)
    {
        var at = args.IndexOf(flag);
        return at < 0 ? [] : args.Skip(at + 1).TakeWhile(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToList();
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Columns();

    /// <summary>A checkout in the temp folder with a play prompt, one strategy file and a small
    /// wiki, by its real path. Deleted when disposed, with the link <see cref="Link"/> made.</summary>
    private sealed class Checkout : IDisposable
    {
        private string? _link;

        public Checkout(string vantomPage)
        {
            Path = ClaudeCli.RealPath(Directory.CreateTempSubdirectory("spiremind-arena-check-").FullName);
            Write(PromptBuilder.PlayPrompt, "---\ndescription: a play prompt\n---\nPlay through the tools. Scope: $ARGUMENTS.\n");
            Write(PromptBuilder.GeneralNotes, "Block first.\n");
            Write("strategy/wiki/README.md", "# The wiki\nHow pages are laid out.\n");
            Write("strategy/wiki/enemies/vantom.md", vantomPage);
            Write("strategy/wiki/potions.md", "# Potions\nWhat each potion does.\n");
        }

        public string Path { get; }

        /// <summary>A link to the checkout, beside it.</summary>
        public string Link()
        {
            _link ??= Path + "-link";
            if (!Directory.Exists(_link))
                Directory.CreateSymbolicLink(_link, Path);
            return _link;
        }

        /// <summary>Writes a file of the checkout, by its path from the checkout's root.</summary>
        public void Write(string file, string text)
        {
            var path = System.IO.Path.Combine(Path, file.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        public void Dispose()
        {
            try
            {
                if (_link is not null) Directory.Delete(_link);
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The system clears its temp folder itself.
            }
        }
    }
}
