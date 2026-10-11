using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The first half of <c>arena selfcheck</c>: the simulator's own proof that it holds together,
/// with no test project to lean on (the repo's tests don't reference the arena). It reports
/// what the registry holds and what it leaves out against the game's own lists
/// (<see cref="Completeness"/>), runs every scripted check class (<see cref="IArenaChecks"/>:
/// the rules, then each content file's), and then the checks of the arena as a whole: every
/// scripted player finishes every generated fight with no action turned down, a fight replays
/// identically, ids round-trip, snapshots have the game's keys and keep what they promise
/// (<see cref="Honesty"/>), bad actions are refused instead of thrown, and the game survives
/// being called from several threads. <see cref="ToolPathCheck"/> is the other half.
/// </summary>
public static class SelfCheck
{
    /// <summary>Runs every check and writes what it found. True when all of them passed.</summary>
    /// <param name="only">Check class names: when given, only these scripted check classes run and nothing else.</param>
    public static bool Run(TextWriter output, int count = 30, int seed = 1, IReadOnlyList<string>? only = null)
    {
        var failures = new List<string>();
        void Check(bool ok, string what)
        {
            if (!ok) failures.Add(what);
        }

        // ---------- the registry ----------
        var registry = Content.Registry;
        output.WriteLine($"Arena content: {registry.Cards.Count} cards, {registry.PowerNames.Count()} powers, {registry.MonsterNames.Count()} monsters, " +
                         $"{registry.EncounterNames.Count()} encounters, {registry.Potions.Count} potions, {registry.RelicNames.Count()} relics, " +
                         $"{registry.Afflictions.Count} afflictions; {registry.Excluded.Count} left out on purpose");
        foreach (var ((kind, name), reason) in registry.Excluded.OrderBy(e => e.Key.Kind).ThenBy(e => e.Key.ClassName, StringComparer.Ordinal))
            output.WriteLine($"  left out: {kind} {name}: {reason}");
        foreach (var problem in registry.Problems) failures.Add($"registry: {problem}");

        // Every card, encounter and potion of the game's lists is registered or left out with a reason.
        Completeness.Write(output, names: false);
        foreach (var problem in Completeness.Problems()) failures.Add($"content: {problem}");

        // ---------- scripted checks, class by class ----------
        var checkTypes = typeof(SelfCheck).Assembly.GetTypes()
            .Where(t => typeof(IArenaChecks).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
        if (only is { Count: > 0 })
        {
            foreach (var name in only.Where(n => checkTypes.All(t => t.Name != n)))
                failures.Add($"there is no check class called {name} ({string.Join(", ", checkTypes.Select(t => t.Name))})");
            checkTypes = checkTypes.Where(t => only.Contains(t.Name)).ToList();
        }
        output.WriteLine($"Scripted checks: {checkTypes.Count} {(checkTypes.Count == 1 ? "class" : "classes")}");
        foreach (var type in checkTypes)
        {
            var check = new Check();
            try
            {
                ((IArenaChecks)Activator.CreateInstance(type)!).Run(check);
            }
            catch (Exception ex)
            {
                failures.Add($"{type.Name} threw {ex.GetType().Name}: {ex.Message}");
            }
            output.WriteLine($"  {type.Name,-24} {check.Passed} passed, {check.Failures.Count} failed");
            foreach (var failure in check.Failures) failures.Add($"{type.Name}: {failure}");
        }

        if (only is { Count: > 0 })
        {
            foreach (var failure in failures) output.WriteLine($"FAILED: {failure}");
            output.WriteLine(failures.Count == 0 ? "PASSED (only the named check classes ran)" : $"FAILED ({failures.Count})");
            return failures.Count == 0;
        }

        // ---------- the arena as a whole ----------
        var scenarios = Scenarios.Generate(count, seed);
        Check(scenarios.Count == count, $"Generate({count}) gave {scenarios.Count} scenarios");
        Check(scenarios.Select(s => s.Id).Distinct().Count() == scenarios.Count, "scenario ids repeat");
        Check(scenarios.SequenceEqual(Scenarios.Generate(count, seed)), "Generate is not deterministic");
        foreach (var s in scenarios)
            Check(Scenarios.FromId(s.Id) == s, $"FromId does not round-trip {s.Id}");

        // Every policy on every fight, twice: no exceptions, and the replay must match.
        var rows = new List<(Scenario Scenario, BaselineRow Row)>();
        var turnLimit = new List<string>();
        foreach (var policy in Policies.All)
        {
            foreach (var s in scenarios)
            {
                try
                {
                    var row = Baseline.Play(s, policy);
                    rows.Add((s, row));
                    Check(row == Baseline.Play(s, policy), $"{policy.Name} on {s.Id} replays differently");
                    Check(row.Turns is >= 1 and <= Tuning.MaxTurns, $"{policy.Name} on {s.Id} took {row.Turns} turns");
                    Check(row.Won ? row.HpLeft > 0 : row.HpLeft >= 0, $"{policy.Name} on {s.Id} ended on {row.HpLeft} HP");
                    Check(row.Refused == 0, $"{policy.Name} on {s.Id}: {row.Refused} actions were turned down (an id that was not on offer, or a rule that threw)");
                    if (row.TimedOut) turnLimit.Add($"{policy.Name} on {s.Id}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{policy.Name} on {s.Id} threw {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        if (scenarios.Count > 0)
        {
            try
            {
                CheckSnapshot(scenarios[0], Check);
                CheckRefusals(scenarios[0], Check);
                CheckBusy(scenarios[0], Check);
                CheckThreads(scenarios[^1], Check);
            }
            catch (Exception ex)
            {
                failures.Add($"a check threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        // What the snapshots promised against what happened, on the same fights (arena honesty does this at scale).
        try
        {
            var honesty = Honesty.Run(scenarios, Policies.All);
            output.WriteLine($"Honesty: {honesty.Decisions} decisions in {honesty.Fights} fights; {honesty.Explained.Values.Sum(v => v.Count)} differences between " +
                             $"snapshot and outcome have a known cause, {honesty.Unexplained.Count} have none");
            // The forecast experiment's promises were judged with the rest, whether or not a fight shows them.
            var forecast = honesty.Forecast;
            output.WriteLine($"Forecast: with the player passing {forecast.PassMovesKept} of {forecast.PassMoves} forecast moves came and {forecast.PassTextsKept} of {forecast.PassTexts} read word for word; " +
                             $"as played {forecast.MovesKept} of {forecast.Moves} and {forecast.TextsKept} of {forecast.Texts}; {forecast.PassOddsExact} of {forecast.PassOdds} chance entries are the roll's own weights " +
                             "(every difference is among the honesty check's)");
            Honesty.WriteForecast(honesty, output);
            foreach (var line in honesty.Unexplained.Take(20)) failures.Add($"honesty: {line}");
            foreach (var line in honesty.Errors) failures.Add($"honesty: {line}");
        }
        catch (Exception ex)
        {
            failures.Add($"the honesty check threw {ex.GetType().Name}: {ex.Message}");
        }

        output.WriteLine($"Arena self-check: {scenarios.Count} scenarios from seed {seed}");
        if (turnLimit.Count > 0) output.WriteLine($"Lost to the turn limit ({Tuning.MaxTurns} turns), worth a look: {string.Join("; ", turnLimit)}");
        var tiers = scenarios.Select(s => s.Tier).Distinct().ToList();
        output.WriteLine($"{"policy",-10} {"wins",-10} {string.Concat(tiers.Select(t => $"{t,-9}"))}{"HP left when won",-17} turns");
        foreach (var policy in Policies.All)
        {
            var mine = rows.Where(r => r.Row.Policy == policy.Name).ToList();
            var won = mine.Where(r => r.Row.Won).ToList();
            string Tier(string tier)
            {
                var of = mine.Where(r => r.Scenario.Tier == tier).ToList();
                return $"{of.Count(r => r.Row.Won)}/{of.Count}";
            }
            var percent = mine.Count == 0 ? 0 : 100.0 * won.Count / mine.Count;
            var hp = won.Count == 0 ? 0 : won.Average(r => r.Row.HpLeft);
            var turns = mine.Count == 0 ? 0 : mine.Average(r => r.Row.Turns);
            output.WriteLine($"{policy.Name,-10} {$"{won.Count}/{mine.Count} {percent:0}%",-10} {string.Concat(tiers.Select(t => $"{Tier(t),-9}"))}{hp,-17:0.0} {turns:0.0}");
        }

        foreach (var failure in failures) output.WriteLine($"FAILED: {failure}");
        output.WriteLine(failures.Count == 0 ? "PASSED" : $"FAILED ({failures.Count})");
        return failures.Count == 0;
    }

    private static void CheckSnapshot(Scenario scenario, Action<bool, string> check)
    {
        var game = new ArenaGame(scenario) { ActionDelay = TimeSpan.Zero };
        var snapshot = game.CaptureAsync(CancellationToken.None).Result;
        check(snapshot is { Screen: "combat", AwaitingDecision: true }, "the first snapshot is not a combat decision");
        string[] keys =
        [
            "run", "turn", "round", "energy", "max_energy", "me", "enemies", "incoming_damage", "hand",
            "draw_pile", "discard_pile", "exhaust_count", "potions", "relics",
        ];
        check(snapshot.State.Select(kv => kv.Key).SequenceEqual(keys), $"the combat snapshot's keys are {string.Join(", ", snapshot.State.Select(kv => kv.Key))}");
        check(snapshot.State["hand"] is JsonArray { Count: Fight.BaseHandDraw }, "the first hand is not five cards");
        check(snapshot.Actions.Count > 0 && snapshot.Actions[^1] == new LegalAction("end_turn", "End turn"), "end_turn is not the last action");
        check(snapshot.State["hand"]!.AsArray().All(c => c!.AsObject().ContainsKey("playable")), "a hand card lacks 'playable'");
        // "vs" is only there where a card reads differently against some enemy, and then it names living enemies.
        var living = snapshot.State["enemies"]!.AsArray().Select(e => $"e{e!["i"]!.GetValue<int>()}").ToHashSet(StringComparer.Ordinal);
        check(snapshot.State["hand"]!.AsArray().All(c => c!["vs"] is not JsonObject vs
                || (vs.Count > 0 && vs.All(kv => living.Contains(kv.Key) && kv.Value!.GetValue<string>() != c["text"]?.GetValue<string>()))),
            "a hand card's 'vs' names an enemy that is not there, or repeats the card's own text");

        var deck = game.DescribeDeckAsync(CancellationToken.None).Result;
        check(deck["deck"] is JsonArray { Count: > 0 } cards && deck["deck_size"]?.GetValue<int>() == cards.Count, "get_deck has no deck");

        // Give up every turn: the fight must end in a decision with nothing left to do but "done".
        for (var n = 0; n < 200 && !game.IsOver; n++)
            game.ExecuteAsync("end_turn", CancellationToken.None).Wait();
        var over = game.CaptureAsync(CancellationToken.None).Result;
        check(game.IsOver && !game.Won, "passing every turn did not lose the fight");
        check(over is { Screen: ArenaGame.OverScreen, AwaitingDecision: true, Waiting: null, Actions.Count: 1 }
              && over.Actions[0] == new LegalAction(ArenaGame.DoneAction, "The fight is over: you lost. Nothing is left to do; stop here.")
              && over.State["result"]?.GetValue<string>() == "lost" && over.State["turn"] is not null && over.State["me"] is JsonObject,
            "the snapshot after a loss is not the agreed end decision");
        check(game.DamageTaken > 0, "a lost fight shows no damage taken");

        // "done" runs, as often as it is sent, and leaves everything as it was; nothing else runs any more.
        var before = over.State.ToJsonString();
        var refused = game.RefusedActions;
        check(game.ExecuteAsync(ArenaGame.DoneAction, CancellationToken.None).Result.Ok
              && game.ExecuteAsync(ArenaGame.DoneAction, CancellationToken.None).Result.Ok, "done was refused after the end");
        var after = game.CaptureAsync(CancellationToken.None).Result;
        check(after is { Screen: ArenaGame.OverScreen, AwaitingDecision: true, Actions.Count: 1 } && after.State.ToJsonString() == before
              && game.RefusedActions == refused, "done changed the finished fight");
        check(!game.ExecuteAsync("end_turn", CancellationToken.None).Result.Ok, "end_turn was accepted after the end");

        // And while the fight runs, "done" is no action at all.
        var running = new ArenaGame(scenario) { ActionDelay = TimeSpan.Zero };
        check(!running.ExecuteAsync(ArenaGame.DoneAction, CancellationToken.None).Result.Ok
              && running.CaptureAsync(CancellationToken.None).Result.Actions.All(a => a.Id != ArenaGame.DoneAction), "done is offered or accepted before the end");

        // With the clock on, "done" leaves no busy spell: the end snapshot is there at once.
        var timed = new ArenaGame(scenario) { ActionDelay = TimeSpan.FromMilliseconds(40) };
        for (var n = 0; n < 2000 && !timed.IsOver; n++)
        {
            timed.ExecuteAsync("end_turn", CancellationToken.None).Wait();
            Thread.Sleep(5);
        }
        Thread.Sleep(120);
        check(timed.IsOver && timed.ExecuteAsync(ArenaGame.DoneAction, CancellationToken.None).Result.Ok
              && timed.CaptureAsync(CancellationToken.None).Result is { Screen: ArenaGame.OverScreen, AwaitingDecision: true },
            "done left the finished fight busy");
    }

    private static void CheckRefusals(Scenario scenario, Action<bool, string> check)
    {
        var game = new ArenaGame(scenario) { ActionDelay = TimeSpan.Zero };
        List<string?> bad =
        [
            null, "", " ", "play", "play:", "play:x", "play:99", "play:-1", "play:0>", "play:0>e", "play:0>e99", "play:0>p0",
            "play:0>e0>e0", "end_turn ", "END_TURN", "potion:3", "discard_potion:3", "potion:-1", "potion:0>e99", "potion:x", "discard_potion:",
            "confirm", "select:0", "card:0", "skip", "play:1e9", "play:99999999999999999999",
        ];
        // The belt has three slots; a slot that is empty in this fight offers nothing either.
        var first = game.CaptureAsync(CancellationToken.None).Result;
        var belt = first.State["potions"]!.AsArray();
        for (var slot = 0; slot < belt.Count; slot++)
        {
            if (belt[slot] is not null) continue;
            bad.Add($"potion:{slot}");
            bad.Add($"discard_potion:{slot}");
        }
        var before = first.State.ToJsonString();
        foreach (var id in bad)
        {
            var result = game.ExecuteAsync(id!, CancellationToken.None).Result;
            check(!result.Ok && !string.IsNullOrWhiteSpace(result.Error), $"the bad action '{id}' was not refused with a reason");
        }
        check(game.RefusedActions == bad.Count, $"{bad.Count} bad actions counted as {game.RefusedActions} refusals");
        check(game.CaptureAsync(CancellationToken.None).Result.State.ToJsonString() == before, "a refused action changed the fight");
    }

    private static void CheckBusy(Scenario scenario, Action<bool, string> check)
    {
        var game = new ArenaGame(scenario) { ActionDelay = TimeSpan.FromMilliseconds(250) };
        check(game.CaptureAsync(CancellationToken.None).Result.AwaitingDecision, "a new fight starts busy");
        check(game.ExecuteAsync("end_turn", CancellationToken.None).Result.Ok, "end_turn was refused");
        var during = game.CaptureAsync(CancellationToken.None).Result;
        check(during is { Screen: "combat", AwaitingDecision: false, Waiting: "resolving", Actions.Count: 0 }, "the snapshot right after an action is not busy");
        foreach (var key in new[] { "turn", "round", "hp", "block" })
            check(during.State.ContainsKey(key), $"the busy snapshot has no '{key}'");
        check(!game.ExecuteAsync("end_turn", CancellationToken.None).Result.Ok, "an action was accepted while busy");
        Thread.Sleep(650);
        check(game.CaptureAsync(CancellationToken.None).Result.AwaitingDecision || game.IsOver, "the fight stayed busy");
    }

    /// <summary>Snapshots and actions from eight threads at once: nothing may throw, and the
    /// fight must still be in one piece (every action either ran or was refused).</summary>
    private static void CheckThreads(Scenario scenario, Action<bool, string> check)
    {
        var game = new ArenaGame(scenario) { ActionDelay = TimeSpan.Zero };
        var accepted = 0;
        var attempts = 0;
        var errors = 0;
        Parallel.For(0, 8, worker =>
        {
            for (var n = 0; n < 400; n++)
            {
                try
                {
                    var snapshot = game.CaptureAsync(CancellationToken.None).Result;
                    game.DescribeDeckAsync(CancellationToken.None).Wait();
                    if (snapshot.Actions.Count == 0) continue;
                    var id = snapshot.Actions[(n + worker) % snapshot.Actions.Count].Id;
                    Interlocked.Increment(ref attempts);
                    if (game.ExecuteAsync(id, CancellationToken.None).Result.Ok) Interlocked.Increment(ref accepted);
                }
                catch
                {
                    Interlocked.Increment(ref errors);
                }
            }
        });
        check(errors == 0, $"{errors} calls threw under concurrent use");
        check(attempts - accepted == game.RefusedActions, "refusals under concurrent use don't add up");
        check(game.CardsPlayed <= accepted, "more cards played than actions accepted");
    }
}
