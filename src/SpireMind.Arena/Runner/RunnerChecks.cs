using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

        private void Write(string file, string text)
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
