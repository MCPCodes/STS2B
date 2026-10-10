using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The second half of <c>arena selfcheck</c>: one fight played the way a model plays it, over
/// HTTP through the fight's own bridge (the MCP handshake, then get_state, wait_for_decision,
/// act and act_sequence), with a scripted player choosing (Greedy when there is one). The tools live in SpireMind.Core,
/// which changes with the mod, so this says in half a minute whether a fight can still be
/// played to the end, before a batch spends the subscription on finding out.
/// <para>
/// A second game with no bridge gets the same actions, and every snapshot the tools hand back
/// must equal that game's. Turns go out as one act_sequence planned from one snapshot, as the
/// play prompt asks, so later ids have to be found again by label once the hand has shifted.
/// Where the fight allows it, two sequences carry an id that can't run, to see them stop: a
/// second card on an enemy the first one kills, and an end_turn after the last blow.
/// </para>
/// <para>
/// The end of the fight gets a closer look, since a model that is told to wait there waits for
/// ever: the fight's last action is played again by act, by act_sequence and by a sequence
/// that runs past it, each on a new game one action short of the end, and each must hand over
/// the end snapshot (a decision whose one action is "done") without a wait or a warning.
/// </para>
/// <para>
/// A generated fight need not hold a card that asks for a choice or a potion that gets used,
/// so a second fight is built by hand that is sure to: Armaments in the deck (its upgrade
/// opens the hand selection) and a Fire Potion in the belt, with a player that throws the
/// potion and plays Armaments first and leaves the rest to Greedy.
/// </para>
/// </summary>
public static class ToolPathCheck
{
    private const string GoneStop = "is no longer offered";
    private const string ScreenStop = "The screen changed to";
    private const string OverStop = ScreenStop + " " + ArenaGame.OverScreen + ".";

    /// <summary>Longer than handing over a decision that is already there takes (the waiter's
    /// settle time and a poll or two), and shorter than any timeout the tools could be waiting out.</summary>
    private static readonly TimeSpan NoWait = TimeSpan.FromSeconds(2);

    /// <summary>Plays <paramref name="scenario"/>, then the hand-built fight with a card choice and a
    /// potion, and writes what it found. True when everything held in both.</summary>
    public static async Task<bool> RunAsync(Scenario scenario, TextWriter output)
    {
        var greedy = Policies.All.FirstOrDefault(p => p.Name == "Greedy") ?? Policies.All[0];
        var generated = await RunOneAsync(scenario.Id, scenario.Summary, delay => new ArenaGame(scenario) { ActionDelay = delay }, greedy, wantChoiceAndPotion: false, output);
        output.WriteLine();

        var (built, setup) = ChoiceFight();
        var chosen = await RunOneAsync(built.Id, built.Summary, delay => new ArenaGame(built, setup) { ActionDelay = delay }, new PotionAndChoiceFirst(greedy), wantChoiceAndPotion: true, output);
        return generated && chosen;
    }

    /// <summary>
    /// A fight that is sure to open a card choice and to use a potion: the starter deck with
    /// three Armaments against two Nibbits, a Fire Potion in the belt. It is no scenario of the
    /// generator's, so its id rebuilds nothing; the tool path only needs the fight.
    /// </summary>
    private static (Scenario Scenario, FightSetup Setup) ChoiceFight()
    {
        const int seed = 20261010;
        var deck = Enumerable.Repeat("StrikeIronclad", 5).Concat(Enumerable.Repeat("DefendIronclad", 4)).Append("Bash")
            .Concat(Enumerable.Repeat("Armaments", 3)).Select(name => new DeckCard(name)).ToList();
        var setup = new FightSetup(deck, 80, 80, ["FirePotion", null, null], "NibbitsNormal", 5, seed);
        var scenario = new Scenario("toolpath-choice", seed, Scenarios.NormalTier,
            "built by hand: 80/80 HP, the starter and 3 Armaments, potions Fire Potion, vs Nibbit + Nibbit (NibbitsNormal)");
        return (scenario, setup);
    }

    /// <summary>Greedy, except that a potion on offer is used first and a playable Armaments comes before any other card.</summary>
    private sealed class PotionAndChoiceFirst(IPolicy rest) : IPolicy
    {
        public string Name => $"{rest.Name} (potion and Armaments first)";

        public string Choose(GameSnapshot snapshot)
        {
            if (snapshot.Screen == "combat")
            {
                if (snapshot.Actions.FirstOrDefault(a => a.Id.StartsWith("potion:", StringComparison.Ordinal)) is { } potion) return potion.Id;
                if (snapshot.Actions.FirstOrDefault(a => a.Label.StartsWith("Play Armaments", StringComparison.Ordinal)) is { } armaments) return armaments.Id;
            }
            return rest.Choose(snapshot);
        }
    }

    /// <summary>Plays one fight over HTTP and writes what it found. True when everything held.</summary>
    /// <param name="make">A new game of the fight with the given busy time after an action.</param>
    /// <param name="wantChoiceAndPotion">The fight must have passed through a hand selection and used a potion.</param>
    private static async Task<bool> RunOneAsync(string id, string summary, Func<TimeSpan, ArenaGame> make, IPolicy player, bool wantChoiceAndPotion, TextWriter output)
    {
        output.WriteLine($"Tool path: {id} over HTTP, {player.Name} choosing ({summary})");
        var failures = new List<string>();
        var bridgeNotes = new List<string>();
        var clock = Stopwatch.StartNew();
        try
        {
            var game = make(ArenaGame.DefaultActionDelay);
            using var server = ClaudeFight.StartBridge(game, ClaudeFight.DefaultSettleTimeout, ClaudeFight.DefaultMaxWait,
                line => { lock (bridgeNotes) bridgeNotes.Add(line); });
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var session = new Session(make, player, game, new Client(http, $"http://127.0.0.1:{server.Port}/mcp"), failures);
            await session.PlayAsync();

            var calls = string.Join(", ", session.Mcp.Calls.OrderByDescending(c => c.Value).Select(c => $"{c.Key} {c.Value}"));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {(game.Won ? $"won with {game.PlayerHp}/{game.PlayerMaxHp} HP" : game.IsOver ? "lost" : "not finished")} on turn {game.Turn}, " +
                $"{game.CardsPlayed} cards; {session.Mcp.Calls.Values.Sum()} tool calls in {clock.Elapsed.TotalSeconds:0} s ({calls})"));
            output.WriteLine($"  act_sequence: longest {session.Longest} ids, {session.FoundAgain} found again at another hand position; " +
                             $"stopped on a dead target: {Seen(session.GoneStops)}, after the last blow: {Seen(session.ScreenStops)}");
            output.WriteLine($"  card choices answered through act: {session.ChoiceSteps} steps on {string.Join(", ", session.ChoiceScreens.DefaultIfEmpty("no choice screen"))}; " +
                             $"potions used: {session.PotionsUsed}");
            if (wantChoiceAndPotion)
            {
                if (!session.ChoiceScreens.Contains("hand_select")) failures.Add("the fight never opened a hand selection, though Armaments was played");
                if (session.PotionsUsed == 0) failures.Add("no potion was used, though one was in the belt");
                if (!session.Ran.Contains("confirm")) failures.Add("no hand selection was confirmed");
            }

            if (game.IsOver && session.Ran.Count > 0)
            {
                var endings = await EndingsAsync(make, session.Ran, http, failures, bridgeNotes);
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  the end ({ArenaGame.OverScreen}, one action \"{ArenaGame.DoneAction}\"): {endings}; act done then took {session.DoneTook.TotalSeconds:0.0} s and changed nothing"));
            }
        }
        catch (Exception ex)
        {
            failures.Add($"it threw {ex.GetType().Name}: {ex.Message}");
        }

        lock (bridgeNotes)
            foreach (var note in bridgeNotes) failures.Add(note);
        foreach (var failure in failures) output.WriteLine($"  FAILED: {failure}");
        output.WriteLine(failures.Count == 0 ? "  PASSED" : $"  FAILED ({failures.Count})");
        return failures.Count == 0;

        static string Seen(int times) => times > 0 ? "yes" : "no chance in this fight";
    }

    /// <summary>
    /// The fight's last action once more through each tool that can end a fight, each time on a
    /// new game brought to one action before the end: act, act_sequence ending on it, and (when
    /// the last action is a card) act_sequence with an end_turn behind it. Every one must hand
    /// over the end snapshot at once, with nothing that tells the model to wait, and a
    /// wait_for_decision after it must come straight back with the same. Returns how long each took.
    /// </summary>
    private static async Task<string> EndingsAsync(Func<TimeSpan, ArenaGame> make, IReadOnlyList<string> ran, HttpClient http,
        List<string> failures, List<string> bridgeNotes)
    {
        var lastId = ran[^1];
        var took = new List<string>();
        foreach (var how in new[] { "act", "act_sequence", "act_sequence past the end" })
        {
            var pastTheEnd = how == "act_sequence past the end";
            if (pastTheEnd && lastId == "end_turn")
                continue; // nothing can follow an end_turn in a sequence

            // The same fight on a short clock, so playing it up to its last action takes a moment, not a minute.
            var game = make(TimeSpan.FromMilliseconds(20));
            foreach (var id in ran.Take(ran.Count - 1))
                if (!await RunWhenFreeAsync(game, id))
                {
                    failures.Add($"the end by {how}: the fight couldn't be played again up to its last action ('{id}' was refused)");
                    return "not checked";
                }

            using var server = ClaudeFight.StartBridge(game, ClaudeFight.DefaultSettleTimeout, ClaudeFight.DefaultMaxWait,
                line => { lock (bridgeNotes) bridgeNotes.Add(line); });
            var mcp = new Client(http, $"http://127.0.0.1:{server.Port}/mcp");
            var (before, _) = await mcp.ToolAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 5 });
            var version = before["version"]?.GetValue<long>();

            var clock = Stopwatch.StartNew();
            var (reply, failed) = how == "act"
                ? await mcp.ToolAsync("act", new JsonObject { ["action_id"] = lastId, ["version"] = version })
                : await mcp.ToolAsync("act_sequence", new JsonObject
                {
                    ["version"] = version,
                    ["actions"] = new JsonArray(pastTheEnd ? [JsonValue.Create(lastId), JsonValue.Create("end_turn")] : [JsonValue.Create(lastId)]),
                });
            var elapsed = clock.Elapsed;
            took.Add(string.Create(CultureInfo.InvariantCulture, $"by {how} in {elapsed.TotalSeconds:0.0} s"));

            void Check(bool ok, string what)
            {
                if (!ok) failures.Add($"the end by {how}: {what}");
            }
            Check(game.IsOver, "the fight isn't over after its last action");
            Check(!failed && IsEnd(reply["next"], game.Won), $"the result's next isn't the end snapshot: {reply.ToJsonString()}");
            Check(!TellsToWait(reply), $"the result tells the model to wait: {reply.ToJsonString()}");
            Check(reply["warning"] is null, $"the result carries a warning: {Text(reply["warning"])}");
            Check(elapsed < NoWait, string.Create(CultureInfo.InvariantCulture, $"it took {elapsed.TotalSeconds:0.0} s, so something waited"));
            if (pastTheEnd)
                Check(Text(reply["stopped"]) == OverStop && reply["not_run"] is JsonArray { Count: 1 } left && Text(left[0]) == "end_turn"
                      && reply["done"] is JsonArray { Count: 1 },
                    $"the sequence should stop with '{OverStop}' and leave end_turn not run: {reply.ToJsonString()}");
            else
                Check(reply["stopped"] is null, $"it stopped with: {Text(reply["stopped"])}");

            clock.Restart();
            var (wait, waitFailed) = await mcp.ToolAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 60 });
            Check(!waitFailed && IsEnd(wait, game.Won) && !TellsToWait(wait), $"wait_for_decision after it doesn't hand over the end snapshot: {wait.ToJsonString()}");
            Check(clock.Elapsed < NoWait, string.Create(CultureInfo.InvariantCulture, $"wait_for_decision after it waited {clock.Elapsed.TotalSeconds:0.0} s"));
        }
        return string.Join(", ", took);
    }

    // Runs one action on a game that may still be busy with the one before, waiting that out.
    private static async Task<bool> RunWhenFreeAsync(ArenaGame game, string id)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            if ((await game.CaptureAsync(CancellationToken.None)).AwaitingDecision)
                return (await game.ExecuteAsync(id, CancellationToken.None)).Ok;
            await Task.Delay(5);
        }
        return false;
    }

    /// <summary>
    /// Whether <paramref name="node"/> is the snapshot a finished fight shows: a decision on the
    /// <see cref="ArenaGame.OverScreen"/> screen with nothing to wait for, whose one action is
    /// <see cref="ArenaGame.DoneAction"/> with the label that says how it went, and the result in its state.
    /// </summary>
    private static bool IsEnd(JsonNode? node, bool won)
    {
        var result = won ? "won" : "lost";
        return node is JsonObject s && Text(s["screen"]) == ArenaGame.OverScreen && s["waiting"] is null
               && s["awaiting_decision"] is JsonValue awaiting && awaiting.TryGetValue<bool>(out var yes) && yes
               && s["actions"] is JsonArray { Count: 1 } actions
               && Text(actions[0]?["id"]) == ArenaGame.DoneAction
               && Text(actions[0]?["label"]) == $"The fight is over: you {result}. Nothing is left to do; stop here."
               && Text(s["state"]?["result"]) == result && s["state"]?["turn"] is not null && s["state"]?["me"] is JsonObject;
    }

    /// <summary>Whether a tool result says what the tools say when no decision is ready ("The game
    /// is still busy. Call wait_for_decision..."). After the end that would be a loop for a model that obeys.</summary>
    private static bool TellsToWait(JsonObject? reply) =>
        reply is null
        || reply.ToJsonString().Contains("still busy", StringComparison.OrdinalIgnoreCase)
        || reply.ToJsonString().Contains("wait_for_decision", StringComparison.Ordinal);

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>The fight's MCP endpoint, spoken to in plain JSON-RPC over HTTP as Claude Code does.</summary>
    private sealed class Client(HttpClient http, string url)
    {
        private int _id;

        /// <summary>Tool calls made, by tool.</summary>
        public Dictionary<string, int> Calls { get; } = new(StringComparer.Ordinal);

        public async Task<(HttpStatusCode Status, JsonNode? Body)> PostAsync(JsonObject message)
        {
            using var response = await http.PostAsync(url, new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json"));
            var text = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, text.Length > 0 ? JsonNode.Parse(text) : null);
        }

        public async Task<JsonObject> RpcAsync(string method, JsonObject? parameters = null)
        {
            var (status, body) = await PostAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = ++_id, ["method"] = method, ["params"] = parameters ?? new JsonObject(),
            });
            return status == HttpStatusCode.OK && body?["result"] is JsonObject result
                ? result
                : throw new InvalidOperationException($"{method} answered HTTP {(int)status}: {body?.ToJsonString() ?? "(nothing)"}");
        }

        /// <summary>One tools/call: the tool's payload, parsed back from the text it travels as, and its error flag.</summary>
        public async Task<(JsonObject Payload, bool IsError)> ToolAsync(string name, JsonObject arguments)
        {
            Calls[name] = Calls.GetValueOrDefault(name) + 1;
            var result = await RpcAsync("tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments });
            var text = result["content"]?[0]?["text"]?.GetValue<string>() ?? "{}";
            var isError = result["isError"] is JsonValue flag && flag.TryGetValue<bool>(out var failed) && failed;
            return (JsonNode.Parse(text) as JsonObject ?? new JsonObject(), isError);
        }
    }

    /// <summary>A turn planned from one snapshot, and the stop it must run into, if any.</summary>
    private sealed record Plan(List<string> Ids, string? ExpectedStop);

    private sealed class Session(Func<TimeSpan, ArenaGame> make, IPolicy player, ArenaGame game, Client mcp, List<string> failures)
    {
        private string Choose(GameSnapshot snapshot, ArenaGame fight) =>
            player is IEnginePolicy searching ? searching.Choose(snapshot, fight.Fight) : player.Choose(snapshot);

        /// <summary>Every action that ran, by the id the tools say they ran it under.</summary>
        private readonly List<string> _ran = [];
        private GameSnapshot _snap = GameSnapshot.Busy("combat", "not started");
        private JsonObject? _lastReply;
        private bool _triedGone;
        private int _acts, _sequences;

        public Client Mcp => mcp;

        /// <summary>Every action that ran, in order; the last one ended the fight.</summary>
        public IReadOnlyList<string> Ran => _ran;

        /// <summary>How long act took to run "done" on the finished fight.</summary>
        public TimeSpan DoneTook { get; private set; }

        /// <summary>Actions that answered a card choice (a select, a pick, a confirm), and the screens they were on.</summary>
        public int ChoiceSteps { get; private set; }
        public SortedSet<string> ChoiceScreens { get; } = new(StringComparer.Ordinal);

        public int PotionsUsed => _ran.Count(id => id.StartsWith("potion:", StringComparison.Ordinal));

        public int Longest { get; private set; }
        public int FoundAgain { get; private set; }
        public int GoneStops { get; private set; }
        public int ScreenStops { get; private set; }

        public async Task PlayAsync()
        {
            await HandshakeAsync();
            if (!_snap.AwaitingDecision) return;
            await RefusalsAsync();
            // The end is a decision too (its one action is "done"), so the screen says when to stop.
            while (_snap.AwaitingDecision && _snap.Screen != ArenaGame.OverScreen && mcp.Calls.Values.Sum() < 600)
                if (!await TurnAsync()) return;
            await EndAsync();
        }

        private void Check(bool ok, string what)
        {
            if (!ok) failures.Add(what);
        }

        /// <summary>The same fight with no bridge and no waiting, brought to where the bridged one is.</summary>
        private ArenaGame Direct()
        {
            var direct = make(TimeSpan.Zero);
            foreach (var id in _ran)
                if (!direct.ExecuteAsync(id, CancellationToken.None).GetAwaiter().GetResult().Ok)
                    throw new InvalidOperationException($"The tools report '{id}' as run, but the same fight refuses it.");
            return direct;
        }

        private static GameSnapshot Capture(ArenaGame direct) => direct.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult();

        // What an MCP client does first, then the first decision by every tool that shows one.
        private async Task HandshakeAsync()
        {
            var init = await mcp.RpcAsync("initialize", new JsonObject
            {
                ["protocolVersion"] = McpEndpoint.LatestProtocol,
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "arena-selfcheck", ["version"] = "0" },
            });
            Check(Text(init["serverInfo"]?["name"]) == StreamWatch.McpServerName, "initialize doesn't name the server spiremind");
            var (noted, _) = await mcp.PostAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
            Check(noted == HttpStatusCode.Accepted, $"notifications/initialized answered {(int)noted}, not 202");

            var listed = ((await mcp.RpcAsync("tools/list"))["tools"] as JsonArray ?? []).Select(t => Text(t?["name"])).ToList();
            foreach (var tool in new[] { "get_state", "wait_for_decision", "act", "act_sequence", "say", "get_deck", "get_persona" })
                Check(listed.Contains(tool), $"tools/list has no {tool}");

            var (first, firstFailed) = await mcp.ToolAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 5 });
            _snap = FromJson(first);
            Check(!firstFailed && _snap is { Screen: "combat", AwaitingDecision: true }, "the first wait_for_decision hands over no combat decision");
            Check(JsonNode.DeepEquals(_snap.State, Capture(Direct()).State), "the first snapshot over HTTP differs from the game's own");
            var (state, _) = await mcp.ToolAsync("get_state", new JsonObject());
            Check(JsonNode.DeepEquals(state, first), "get_state differs from the snapshot wait_for_decision gave");
            var (deck, deckFailed) = await mcp.ToolAsync("get_deck", new JsonObject());
            Check(!deckFailed && deck["deck"] is JsonArray { Count: > 0 }, "get_deck lists no deck");
            var (persona, personaFailed) = await mcp.ToolAsync("get_persona", new JsonObject());
            Check(!personaFailed && Text(persona["name"]) == ArenaPrompt.Character, "get_persona doesn't name the Ironclad");
        }

        // Calls the tools must turn down without anything reaching the game.
        private async Task RefusalsAsync()
        {
            var legal = _snap.Actions[0].Id;
            await RefusedAsync("act", new JsonObject { ["action_id"] = legal, ["version"] = _snap.Version - 1 }, "Snapshot changed");
            await RefusedAsync("act", new JsonObject { ["action_id"] = "play:99", ["version"] = _snap.Version }, "is not a legal action now");
            await RefusedAsync("act_sequence", new JsonObject { ["version"] = _snap.Version, ["actions"] = Ids(["end_turn", legal]) }, "end_turn can only be the last id");
            await RefusedAsync("act_sequence", new JsonObject { ["version"] = _snap.Version, ["actions"] = Ids([legal, "play:99>e7", "end_turn"]) }, "so nothing was played");
            await RefusedAsync("act_sequence", new JsonObject { ["actions"] = Ids([legal]) }, "version is required");
            Check(game is { RefusedActions: 0, CardsPlayed: 0, Turn: 1 }, "a call the tools refused reached the game");
        }

        private async Task RefusedAsync(string tool, JsonObject arguments, string expected)
        {
            var (reply, isError) = await mcp.ToolAsync(tool, arguments);
            Check(isError && Text(reply["error"])?.Contains(expected, StringComparison.Ordinal) == true,
                $"{tool} {arguments.ToJsonString()} should be refused with '{expected}', got: {Text(reply["error"]) ?? reply.ToJsonString()}");
            var (now, _) = await mcp.ToolAsync("get_state", new JsonObject());
            Check(now["version"]?.GetValue<long>() == _snap.Version && JsonNode.DeepEquals(now["state"], _snap.State),
                $"the snapshot changed after a refused {tool}");
        }

        /// <summary>One call: a turn's plan as an act_sequence (or act, for some single ids), and
        /// the result against the game without a bridge. False when it can't go on.</summary>
        private async Task<bool> TurnAsync()
        {
            // A card choice (hand_select, card_select) is answered one action at a time.
            var plan = _snap.Screen == "combat" ? PlanFrom(_snap) : new Plan([Choose(_snap, Direct())], null);
            if (_snap.Screen != "combat")
            {
                ChoiceSteps++;
                ChoiceScreens.Add(_snap.Screen);
            }
            JsonObject reply;
            bool isError;
            List<string> done;
            // Single ids go by act and by act_sequence in turn, so both tools play.
            if (plan.Ids.Count == 1 && (_acts <= _sequences || _snap.Screen != "combat"))
            {
                _acts++;
                (reply, isError) = await mcp.ToolAsync("act", new JsonObject { ["action_id"] = plan.Ids[0], ["version"] = _snap.Version });
                done = isError || Text(reply["done"]) is not { } id ? [] : [id];
            }
            else
            {
                _sequences++;
                Longest = Math.Max(Longest, plan.Ids.Count);
                (reply, isError) = await mcp.ToolAsync("act_sequence", new JsonObject { ["version"] = _snap.Version, ["actions"] = Ids(plan.Ids) });
                done = (reply["done"] as JsonArray ?? []).Select(d => Text(d?["id"]) ?? "").ToList();
                CheckSequence(plan, reply, done);
            }
            _lastReply = reply;
            if (isError || reply["next"] is not JsonObject nextJson)
            {
                Check(false, $"[{string.Join(", ", plan.Ids)}] failed: {reply.ToJsonString()}");
                return false;
            }

            _ran.AddRange(done);
            var next = FromJson(nextJson);
            var direct = Capture(Direct());
            Check(next.Version > _snap.Version, "the version didn't move on after an action");
            Check(next.Screen == direct.Screen && next.AwaitingDecision == direct.AwaitingDecision && next.Waiting == direct.Waiting,
                $"after [{string.Join(", ", done)}] the tools show {next.Screen} ({next.Waiting ?? "a decision"}), the game {direct.Screen} ({direct.Waiting ?? "a decision"})");
            Check(JsonNode.DeepEquals(next.State, direct.State), $"after [{string.Join(", ", done)}] next.state differs from the game's own state");
            Check(next.Actions.SequenceEqual(direct.Actions), $"after [{string.Join(", ", done)}] next.actions differ from the game's own");
            _snap = next;
            return true;
        }

        private void CheckSequence(Plan plan, JsonObject reply, List<string> done)
        {
            var stopped = Text(reply["stopped"]);
            var notRun = (reply["not_run"] as JsonArray ?? []).Select(Text).ToList();
            var sent = string.Join(", ", plan.Ids);
            if (plan.ExpectedStop is { } expected)
            {
                Check(stopped?.Contains(expected, StringComparison.Ordinal) == true, $"[{sent}] should stop with '{expected}', got: {stopped ?? "no stop"}");
                Check(done.Count == plan.Ids.Count - 1 && notRun.SequenceEqual(plan.Ids.Skip(done.Count)),
                    $"[{sent}] should run all but its last id: ran {done.Count}, not_run [{string.Join(", ", notRun)}]");
                if (expected == GoneStop) GoneStops++;
                else ScreenStops++;
            }
            else
            {
                Check(stopped is null && done.Count == plan.Ids.Count, $"[{sent}] ran {done.Count} of {plan.Ids.Count} ids: {stopped ?? "no stop given"}");
            }
            FoundAgain += done.Where((id, i) => i < plan.Ids.Count && id != plan.Ids[i]).Count();
        }

        /// <summary>
        /// The turn as one list of ids, all from <paramref name="start"/>: the player picks the first
        /// card from that snapshot and each following one from the game without a bridge, played
        /// ahead. It ends where a model's plan ends: at end_turn, after a card that drew, or at a
        /// card that snapshot doesn't offer yet (energy it only gets on the way).
        /// </summary>
        private Plan PlanFrom(GameSnapshot start)
        {
            var ids = new List<string>();
            var ahead = Direct();
            // Where each card of the hand as it is now sat in the start snapshot.
            var startIndex = Enumerable.Range(0, (start.State["hand"] as JsonArray)?.Count ?? 0).ToList();
            var current = start;
            while (ids.Count < BotTools.MaxSequence)
            {
                var choice = Choose(current, ahead);
                if (choice == "end_turn")
                {
                    ids.Add("end_turn");
                    break;
                }
                if (!TryParse(choice, out var hand, out var target))
                {
                    // A potion: its id doesn't move with the hand, so it can only open a plan.
                    if (ids.Count == 0) ids.Add(choice);
                    break;
                }
                if (hand >= startIndex.Count) break;
                var id = string.Create(CultureInfo.InvariantCulture, $"play:{startIndex[hand]}{target}");
                if (start.Actions.All(a => a.Id != id)) break;
                ids.Add(id);

                ahead.ExecuteAsync(choice, CancellationToken.None).GetAwaiter().GetResult();
                startIndex.RemoveAt(hand);
                if (ahead.IsOver)
                {
                    if (ids.Count == BotTools.MaxSequence) break;
                    ids.Add("end_turn");
                    return new Plan(ids, ScreenStop);
                }

                var after = Capture(ahead);
                if (after.Screen != "combat") break; // the card opened a choice: the plan ends on it
                if (!_triedGone && target.Length > 0 && ids.Count < BotTools.MaxSequence && !Living(after, target)
                    && start.Actions.FirstOrDefault(a => a.Id.EndsWith(target, StringComparison.Ordinal) && !ids.Contains(a.Id)
                                                         && TryParse(a.Id, out var other, out _) && startIndex.Contains(other)) is { } onTheDead)
                {
                    _triedGone = true;
                    ids.Add(onTheDead.Id);
                    return new Plan(ids, GoneStop);
                }
                if ((after.State["hand"] as JsonArray)?.Count > startIndex.Count) break;
                // A card that changed its name on the way (Armaments made a Defend a Defend+) cannot be
                // found again by the label the plan's snapshot gave it: the plan ends before it.
                if (Renamed(start, after, startIndex)) break;
                current = after;
            }
            // Nothing to send can only mean the first pick isn't in the snapshot it was picked from.
            return ids.Count > 0 ? new Plan(ids, null) : new Plan(["end_turn"], null);
        }

        // The end of the fight as each tool shows it, and against the game without a bridge.
        private async Task EndAsync()
        {
            var direct = Direct();
            Check(game.IsOver, "the calls ran out before the fight was over");
            Check(game.Won == direct.Won && game.PlayerHp == direct.PlayerHp && game.Turn == direct.Turn
                  && game.CardsPlayed == direct.CardsPlayed && game.DamageTaken == direct.DamageTaken,
                "the fight behind the bridge ended differently from the same actions without it");

            // The end is a decision with nothing left to do: one action, "done", and nothing to wait for.
            var last = _lastReply;
            Check(IsEnd(last?["next"], game.Won), $"the last result's next isn't the end snapshot: {last?["next"]?.ToJsonString()}");
            Check(!TellsToWait(last), $"the result that ended the fight tells the model to wait: {last?.ToJsonString()}");
            Check(last?["warning"] is null, $"the result that ended the fight carries a warning: {Text(last?["warning"])}");
            Check(last?["stopped"] is null || Text(last["stopped"]) == OverStop, $"the sequence that ended the fight stopped with: {Text(last?["stopped"])}");

            var (state, _) = await mcp.ToolAsync("get_state", new JsonObject());
            Check(IsEnd(state, game.Won), "get_state doesn't show the end snapshot");
            var endVersion = state["version"]?.GetValue<long>();

            var waited = Stopwatch.StartNew();
            var (wait, waitFailed) = await mcp.ToolAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 60 });
            Check(!waitFailed && IsEnd(wait, game.Won) && !TellsToWait(wait), $"wait_for_decision after the end doesn't hand over the end snapshot: {wait.ToJsonString()}");
            Check(waited.Elapsed < NoWait, $"wait_for_decision after the end waited {waited.Elapsed.TotalSeconds:0.0} s");

            var (act, actFailed) = await mcp.ToolAsync("act", new JsonObject { ["action_id"] = "end_turn", ["version"] = endVersion });
            Check(actFailed && Text(act["error"])?.Contains("is not a legal action now", StringComparison.Ordinal) == true
                  && IsEnd(act["current"], game.Won) && !TellsToWait(act),
                $"act end_turn after the end isn't refused against the end snapshot: {act.ToJsonString()}");

            // "done" runs and changes nothing: the same snapshot at the same version. The tools
            // wait their settle time for a newer one and say that nothing changed, which is true.
            waited.Restart();
            var (done, doneFailed) = await mcp.ToolAsync("act", new JsonObject { ["action_id"] = ArenaGame.DoneAction, ["version"] = endVersion });
            DoneTook = waited.Elapsed;
            Check(!doneFailed && Text(done["done"]) == ArenaGame.DoneAction && IsEnd(done["next"], game.Won)
                  && done["next"]?["version"]?.GetValue<long>() == endVersion && !TellsToWait(done),
                $"act done after the end doesn't hand back the unchanged end snapshot: {done.ToJsonString()}");

            var (stateAfter, _) = await mcp.ToolAsync("get_state", new JsonObject());
            Check(IsEnd(stateAfter, game.Won) && stateAfter["version"]?.GetValue<long>() == endVersion, "the end snapshot changed after done");
            Check(game.RefusedActions == 0, $"the game itself refused {game.RefusedActions} actions");
        }

        /// <summary>Whether a card still in hand reads another name now than in the snapshot the plan was made from.</summary>
        private static bool Renamed(GameSnapshot start, GameSnapshot now, List<int> startIndex)
        {
            var before = start.State["hand"] as JsonArray;
            var hand = now.State["hand"] as JsonArray;
            if (before is null || hand is null) return false;
            for (var i = 0; i < hand.Count && i < startIndex.Count; i++)
                if (Text(hand[i]?["name"]) != Text(before[startIndex[i]]?["name"])) return true;
            return false;
        }

        private static bool Living(GameSnapshot snapshot, string target) =>
            (snapshot.State["enemies"] as JsonArray ?? []).Any(e => e?["i"] is JsonValue i && i.TryGetValue<int>(out var index)
                                                                    && string.Create(CultureInfo.InvariantCulture, $">e{index}") == target);

        /// <summary>"play:3>e1" as hand index 3 and target ">e1" ("" when it has none).</summary>
        private static bool TryParse(string id, out int hand, out string target)
        {
            hand = 0;
            target = "";
            if (!id.StartsWith("play:", StringComparison.Ordinal)) return false;
            var arrow = id.IndexOf('>');
            if (arrow >= 0) target = id[arrow..];
            return int.TryParse(arrow < 0 ? id.AsSpan(5) : id.AsSpan(5, arrow - 5), NumberStyles.None, CultureInfo.InvariantCulture, out hand);
        }

        private static JsonArray Ids(IEnumerable<string> ids) => new(ids.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray());

        /// <summary>A snapshot back from the JSON a tool sent, as a client has it.</summary>
        private static GameSnapshot FromJson(JsonObject json) => new()
        {
            Version = json["version"] is JsonValue v && v.TryGetValue<long>(out var version) ? version : 0,
            Screen = Text(json["screen"]) ?? "unknown",
            AwaitingDecision = json["awaiting_decision"] is JsonValue a && a.TryGetValue<bool>(out var awaiting) && awaiting,
            Waiting = Text(json["waiting"]),
            State = json["state"]?.DeepClone() as JsonObject ?? new JsonObject(),
            Actions = (json["actions"] as JsonArray ?? [])
                .Select(action => new LegalAction(Text(action?["id"]) ?? "", Text(action?["label"]) ?? "")).ToList(),
        };
    }
}
