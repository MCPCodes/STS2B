using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// A scripted player of a whole run: the yardstick a model's run is measured against, and what
/// walks thousands of acts for the self-check. It decides from the snapshot, as a model does,
/// and returns one of the snapshot's action ids. It keeps nothing between calls: what it needs
/// beyond the snapshot it reads off the run (how many actions were taken, the fight to search
/// on), so the same run gets the same answers again, also after it was rebuilt from its save.
/// <para>
/// The run it is handed is the live one: it looks, and copies when it wants to try something
/// (<see cref="Run.Clone"/>, <see cref="Fight.CloneDeterminized"/>); it never acts on it.
/// </para>
/// </summary>
public interface IRunPlayer
{
    string Name { get; }

    /// <summary>The id of one action of <paramref name="snapshot"/>, the run's snapshot as it stands. The snapshot offers at least one action.</summary>
    string Choose(Run run, GameSnapshot snapshot);
}

/// <summary>
/// The random walker: outside a fight it takes any offered action, each as likely as the other,
/// so that over many runs every screen and every kind of action is walked: paths, rewards taken
/// and left, cards taken and skipped, purchases, removals, rest and smith, card choices picked,
/// taken back and cancelled, event options. A potion is used or thrown away outside a fight one
/// time in eight when one is held. Fights are played by a scripted fight player: Greedy unless
/// another is named, so that the walk gets deep into the act; with Random it is the floor.
/// <para>
/// Its choice is a hash of its seed, the number of actions the run has taken so far and the
/// screen, so a walk is the same every time, and a screen it comes back to unchanged (a shop
/// closed again, a card choice cancelled) gets another answer the next time.
/// </para>
/// </summary>
public sealed class RunWalker : IRunPlayer
{
    private readonly int _seed;
    private readonly IPolicy _fights;

    /// <param name="seed">Seeds the walk: another seed, another walk of the same run.</param>
    /// <param name="fights">Who plays the fights; Greedy when null.</param>
    public RunWalker(int seed = 1, IPolicy? fights = null)
    {
        _seed = seed;
        _fights = fights ?? Policies.ByName("Greedy")!;
        Name = _fights.Name == "Greedy" ? "Walker" : $"Walker:{_fights.Name.ToLowerInvariant()}";
    }

    public string Name { get; }

    public string Choose(Run run, GameSnapshot snapshot)
    {
        if (run.Fight is not null) return RunPlayers.InFight(_fights, run, snapshot);
        return Pick(snapshot, run.Actions.Count);
    }

    /// <summary>The walker's answer outside a fight to a snapshot met after <paramref name="step"/> actions of the run.</summary>
    internal string Pick(GameSnapshot snapshot, int step)
    {
        if (snapshot.Screen == "game_over") return "continue";

        var own = snapshot.Actions.Where(action => !RunPlayers.IsPotionAction(action.Id)).ToList();
        var potions = snapshot.Actions.Where(action => RunPlayers.IsPotionAction(action.Id)).ToList();
        var rng = new GameRng(RunPlayers.Hash(_seed, step, snapshot.Screen));
        if (potions.Count > 0 && (own.Count == 0 || rng.NextInt(PotionOneIn) == 0)) return potions[rng.NextInt(potions.Count)].Id;
        return own[rng.NextInt(own.Count)].Id;
    }

    /// <summary>Outside a fight a potion action is taken one time in this many while the screen offers anything else.</summary>
    internal const int PotionOneIn = 8;
}

/// <summary>
/// The player with plain rules (plan 5.5): outside a fight it takes the first path, every
/// reward and the first card of every card reward, the first option of an event (Neow's first
/// relic), opens every chest, rests below half HP and smiths otherwise, and buys nothing.
/// Fights are played by Lookahead unless another fight player is named. It never uses or throws
/// away a potion outside a fight (inside one the fight player decides), except to make room
/// when a rewards screen will not let it go on otherwise.
/// <para>
/// The rules are plain on purpose: it is a baseline anyone can follow by hand, not a good
/// player of the game outside fights.
/// </para>
/// </summary>
public sealed class PlainPlayer : IRunPlayer
{
    private readonly IPolicy _fights;

    /// <param name="fights">Who plays the fights; Lookahead when null.</param>
    public PlainPlayer(IPolicy? fights = null)
    {
        _fights = fights ?? Policies.ByName("Lookahead")!;
        Name = _fights.Name == "Lookahead" ? "Plain" : $"Plain:{_fights.Name.ToLowerInvariant()}";
    }

    public string Name { get; }

    public string Choose(Run run, GameSnapshot snapshot)
    {
        if (run.Fight is not null) return RunPlayers.InFight(_fights, run, snapshot);

        var ids = snapshot.Actions.Select(action => action.Id).ToList();
        string? First(string prefix) => ids.FirstOrDefault(id => id.StartsWith(prefix, StringComparison.Ordinal));
        bool Has(string id) => ids.Contains(id);
        // What is left when a screen offers nothing of its own: never a potion while anything else is there.
        string Any() => ids.FirstOrDefault(id => !RunPlayers.IsPotionAction(id)) ?? ids[0];

        switch (snapshot.Screen)
        {
            case "game_over":
                return "continue";
            case "map":
                return First("map:") ?? Any();
            case "event":
                return FirstOption(snapshot) ?? Any();
            case "rewards":
                // Every reward in turn; a potion the belt has no room for is left behind; then on.
                // Only when the screen holds the player (rewards that may not be skipped) a potion is thrown away for it.
                return First("reward:") ?? (Has("proceed") ? "proceed" : First("discard_potion:") ?? Any());
            case "card_reward":
                return First("card:") ?? (Has("skip") ? "skip" : Any());
            case "card_select":
                // Confirm as soon as it is offered (a preview, or a choice that asks for no more); else the first card
                // that is not picked yet (a picked card's id would take the pick back).
                return Has("confirm") ? "confirm" : FirstUnpicked(snapshot) ?? (Has("skip") ? "skip" : Any());
            case "rest":
                return Rest(snapshot) ?? (Has("proceed") ? "proceed" : Any());
            case "shop":
                // Buys nothing.
                return Has("proceed") ? "proceed" : Has("close") ? "close" : Any();
            case "treasure":
                return Has("open") ? "open" : First("relic:") ?? (Has("proceed") ? "proceed" : Any());
            default:
                return Any();
        }
    }

    /// <summary>The first card on offer that the choice does not list as selected.</summary>
    private static string? FirstUnpicked(GameSnapshot snapshot)
    {
        var picked = new HashSet<string>(StringComparer.Ordinal);
        if (snapshot.State["cards"] is JsonArray cards)
        {
            foreach (var card in cards.OfType<JsonObject>())
                if (card["selected"] is JsonValue selected && selected.TryGetValue<bool>(out var yes) && yes) picked.Add("card:" + card["i"]);
        }
        return snapshot.Actions.Select(action => action.Id).FirstOrDefault(id => id.StartsWith("card:", StringComparison.Ordinal) && !picked.Contains(id));
    }

    /// <summary>The first option on offer that is not the way out; the way out when it is all there is.</summary>
    private static string? FirstOption(GameSnapshot snapshot)
    {
        var offered = snapshot.Actions.Select(action => action.Id).Where(id => id.StartsWith("option:", StringComparison.Ordinal)).ToList();
        if (snapshot.State["options"] is JsonArray options)
        {
            foreach (var id in offered)
            {
                var n = id["option:".Length..];
                var option = options.OfType<JsonObject>().FirstOrDefault(o => o["n"]?.ToString() == n);
                if (option is not null && option["leave"] is null) return id;
            }
        }
        return offered.FirstOrDefault();
    }

    /// <summary>Rest below half HP, else Smith, else whatever option is offered; null once an option was used or none is offered.</summary>
    private static string? Rest(GameSnapshot snapshot)
    {
        var state = snapshot.State;
        if (state["chosen"] is JsonArray { Count: > 0 }) return null;
        var offered = snapshot.Actions.Select(action => action.Id).Where(id => id.StartsWith("option:", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        if (offered.Count == 0) return null;

        string? Option(string optionId)
        {
            if (state["options"] is not JsonArray options) return null;
            foreach (var option in options.OfType<JsonObject>())
            {
                if (option["id"]?.ToString() != optionId) continue;
                var id = "option:" + option["n"];
                if (offered.Contains(id)) return id;
            }
            return null;
        }

        var hp = Int(state["hp"]);
        var maxHp = Int(state["max_hp"]);
        var heal = Option("HEAL");
        var smith = Option("SMITH");
        if (hp * 2 < maxHp && heal is not null) return heal;
        return smith ?? heal ?? offered.Order(StringComparer.Ordinal).First();
    }

    private static int Int(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var n) ? n : 0;
}

/// <summary>How a scripted player's run of the act went, for a table and a result line.</summary>
/// <param name="Seed">The run's seed.</param>
/// <param name="Player">The player's name.</param>
/// <param name="Result">One of <see cref="RunResult"/>, or "unfinished" for a run that did not come to its end.</param>
/// <param name="Reason">What the result needs said (<see cref="Run.ResultReason"/>).</param>
/// <param name="Floor">The total floor the run ended on.</param>
/// <param name="Fights">Fights won.</param>
/// <param name="Turns">Turns of all fights, the lost one included.</param>
/// <param name="Actions">Accepted actions: the length of the save.</param>
/// <param name="Refused">Actions that were turned down. 0 in a sound arena with a sound player.</param>
/// <param name="FirstRefusal">The first of them, with the screen and the reason.</param>
/// <param name="Stalled">The run stopped before its end: no action was on offer, or the player ran out of steps.</param>
/// <param name="FirstRedrawFloor">The floor of the first roll that was drawn again; null for a run that is the game's own for its seed.</param>
/// <param name="DiedTo">The encounter a run died in.</param>
/// <param name="MetFloor">For an unrepresentable run: the floor on which it met what the arena could not show. The run itself stands
/// as it stood before that action (<paramref name="Floor"/>), which is one floor lower when the action was a path choice; 0 for a
/// seed that was refused before Neow's room (another first act, the screen).</param>
/// <param name="Seconds">How long the player took.</param>
public sealed record ActResult(
    string Seed, string Player, string Result, string? Reason, int Floor, int Hp, int MaxHp, int Gold, int DeckSize, int Relics,
    int Fights, int Turns, int Actions, int Refused, string? FirstRefusal, bool Stalled, int? FirstRedrawFloor, string? DiedTo, int? MetFloor, double Seconds)
{
    public bool Cleared => Result == RunResult.Cleared;
    public bool Died => Result == RunResult.Died;
    public bool Unrepresentable => Result == RunResult.Unrepresentable;

    /// <summary>The run came to an end the game could have come to: nothing unbuilt stopped it and nothing failed.</summary>
    public bool Finished => Cleared || Died;

    /// <summary>The result line as JSON, with the floor log when the run is handed along.</summary>
    public JsonObject ToJson(Run? run = null)
    {
        var json = new JsonObject
        {
            ["seed"] = Seed, ["player"] = Player, ["result"] = Result, ["reason"] = Reason, ["floor"] = Floor,
            ["hp"] = Hp, ["max_hp"] = MaxHp, ["gold"] = Gold, ["deck_size"] = DeckSize, ["relics"] = Relics,
            ["fights_won"] = Fights, ["turns"] = Turns, ["actions"] = Actions, ["refused"] = Refused, ["first_refusal"] = FirstRefusal,
            ["stalled"] = Stalled, ["first_redraw_floor"] = FirstRedrawFloor, ["died_to"] = DiedTo, ["met_floor"] = MetFloor, ["seconds"] = Math.Round(Seconds, 2),
        };
        if (run is not null)
        {
            json["summary"] = run.Summary();
            json["floors"] = FloorLog.ToJson(run);
            json["end"] = run.DescribeDeck();
        }
        return json;
    }

    /// <summary>The result of a run as it stands.</summary>
    public static ActResult Of(Run run, string player, int refused = 0, string? firstRefusal = null, bool stalled = false, double seconds = 0)
    {
        var state = run.State;
        var floors = FloorLog.Of(run);
        int? met = null;
        if (run.Result == RunResult.Unrepresentable)
        {
            // The run stands as before the action that met what is missing: a path choice would have led one floor on.
            var missing = ActRun.MissingKind(run.ResultReason);
            met = run.Actions.Count == 0
                ? missing is "act" or "screen" ? 0 : 1
                : run.Actions[^1].StartsWith("map:", StringComparison.Ordinal) ? state.TotalFloor + 1 : state.TotalFloor;
        }
        return new ActResult(
            state.Seed, player, run.AtEnd ? run.Result ?? "unfinished" : "unfinished", run.ResultReason, state.TotalFloor,
            run.Hp, run.MaxHp, run.Gold, run.Deck.Count, run.Relics.Count,
            floors.Sum(floor => floor.Fights.Count(fight => fight.Won)), floors.Sum(floor => floor.Turns), run.Actions.Count,
            refused, firstRefusal, stalled, state.FindPart<LeftOutLog>()?.FirstFloor,
            run.Result == RunResult.Died && run.LastFight is { Won: false } fight ? fight.Encounter : null, met, seconds);
    }
}

/// <summary>
/// The share of real runs the build can represent, measured from strict runs on seeds as they
/// come (plan 5.1 and 5.3): a strict run is the game's own run until it meets something the
/// arena has not built, and ends there as unrepresentable.
/// <para>
/// Two numbers. <see cref="Finished"/> of <see cref="Started"/> is the plain count: runs that
/// ended cleared or dead. It is the player's own number, since a player that dies on floor 6
/// never meets what waits on floor 10. <see cref="WholeAct"/> answers the plan's question, "what
/// share of real runs can the arena play from Neow to the boss with nothing drawn again", with
/// the deaths taken out of it: floor by floor, of the runs that were still alive and still the
/// game's own on entering the floor, the share that met nothing unbuilt on it; the product
/// over the seventeen floors, times the share of seeds whose act is Overgrowth. A run that died
/// on a floor counts for the floors before it. (It is the survival estimate of Kaplan and
/// Meier with deaths as the runs that left early; it assumes that dying and meeting something
/// unbuilt do not depend on each other.) <see cref="AtRisk"/> says how many runs each floor's
/// factor rests on: few runs, a rough factor.
/// </para>
/// </summary>
/// <param name="Started">Runs started.</param>
/// <param name="Overgrowth">Of them, on seeds whose first act is Overgrowth.</param>
/// <param name="Cleared">Runs that cleared the act.</param>
/// <param name="Died">Runs that died.</param>
/// <param name="AtRisk">For each floor 1 to 17 (index 0 is floor 1): runs that entered it alive and as the game's own run.</param>
/// <param name="Met">For each floor: of those, the runs that met something unbuilt on it.</param>
/// <param name="Through">For each floor: the estimated share of Overgrowth runs that are still the game's own after it.</param>
public sealed record RealRunShare(int Started, int Overgrowth, int Cleared, int Died, IReadOnlyList<int> AtRisk, IReadOnlyList<int> Met, IReadOnlyList<double> Through)
{
    /// <summary>The floors of a first act: Neow's room, fifteen rows, the boss.</summary>
    public const int Floors = 17;

    /// <summary>Runs that ended cleared or dead: they met nothing the arena has not built.</summary>
    public int Finished => Cleared + Died;

    /// <summary>The plain share: finished runs of started runs.</summary>
    public double Plain => Started == 0 ? 0 : (double)Finished / Started;

    /// <summary>The estimated share of all real runs that the arena can play as a whole act: the share of Overgrowth seeds times
    /// the share of Overgrowth runs that meet nothing unbuilt through the boss's floor.</summary>
    public double WholeAct => Started == 0 ? 0 : (double)Overgrowth / Started * Through[Floors - 1];

    /// <summary>The measurement from the results of strict runs of one player.</summary>
    public static RealRunShare Of(IEnumerable<ActResult> strictRuns)
    {
        var rows = strictRuns.ToList();
        var atRisk = new int[Floors];
        var met = new int[Floors];
        var overgrowth = 0;
        foreach (var row in rows)
        {
            if (row.Unrepresentable && row.MetFloor == 0) continue;
            overgrowth++;
            // The last floor the run counts for: the floor it met something on, the floor before the one it died on, the boss's.
            var last = row.Unrepresentable ? row.MetFloor ?? row.Floor : row.Died ? row.Floor - 1 : row.Cleared ? Floors : row.Floor;
            for (var floor = 1; floor <= Math.Min(last, Floors); floor++) atRisk[floor - 1]++;
            if (row.Unrepresentable && last is >= 1 and <= Floors) met[last - 1]++;
        }

        var through = new double[Floors];
        var share = 1.0;
        for (var floor = 0; floor < Floors; floor++)
        {
            // A floor nobody reached says nothing; the estimate stops falling there, and AtRisk shows it.
            if (atRisk[floor] > 0) share *= 1.0 - (double)met[floor] / atRisk[floor];
            through[floor] = share;
        }
        return new RealRunShare(rows.Count, overgrowth, rows.Count(r => r.Cleared), rows.Count(r => r.Died), atRisk, met, through);
    }

    /// <summary>The last floor after which some run was still alive and the game's own; 0 when there is none.</summary>
    public int Reached
    {
        get
        {
            var last = 0;
            for (var floor = 1; floor <= Floors; floor++)
                if (AtRisk[floor - 1] > Met[floor - 1]) last = floor;
            return last;
        }
    }

    /// <summary>True when some run was still the game's own after the boss's floor, so that <see cref="WholeAct"/> is a measurement.
    /// False means the share of whole acts is too small for this many runs to show: <see cref="WholeAct"/> is then 0.</summary>
    public bool ReachedTheEnd => Reached == Floors;

    /// <summary>The measurement in two lines.</summary>
    public string Describe(string player)
    {
        static string Percent(double share) => (share * 100).ToString(share is > 0 and < 0.1 ? "0.###" : "0.0", CultureInfo.InvariantCulture) + "%";
        var inv = CultureInfo.InvariantCulture;
        var shown = new[] { 1, 5, 9, 13, 17 }.Where(floor => floor <= Reached).ToList();
        var byFloor = shown.Count == 0
            ? "no floor"
            : string.Join(", ", shown.Select(floor => string.Create(inv, $"floor {floor}: {Percent(Through[floor - 1])} ({AtRisk[floor - 1]} runs)")));
        var first = string.Create(inv,
            $"Share of real runs, {player}: {Finished} of {Started} strict runs ({Percent(Plain)}) ended cleared or dead ({Cleared} cleared, {Died} died); {Overgrowth} of the seeds start in Overgrowth.");
        var whole = ReachedTheEnd
            ? string.Create(inv, $"{Percent(WholeAct)} of all real runs ({Percent(Through[Floors - 1])} of the Overgrowth ones)")
            : string.Create(inv, $"too small for {Started} runs to show: no run was still alive and the game's own after floor {Reached + 1}");
        return $"{first}\n  As whole acts, deaths taken out: {whole}. Of the Overgrowth runs, still the game's own after {byFloor}.";
    }
}

/// <summary>The scripted run players by name, what they share, and the loop that lets one play a run to its end.</summary>
public static class RunPlayers
{
    /// <summary>The names <see cref="ByName"/> knows, the ones a baseline plays by default first.</summary>
    public static readonly IReadOnlyList<string> Names = ["Walker", "Plain", "Walker:random", "Plain:greedy"];

    /// <summary>The two players of a baseline: the random walker and the player with plain rules (plan 5.5).</summary>
    public static readonly IReadOnlyList<string> Default = ["Walker", "Plain"];

    /// <summary>More actions than any run of an act takes: about fifty rooms' worth of the longest fights.</summary>
    public const int MostActions = 60_000;

    /// <summary>
    /// A run player by name, whatever the case: "Walker" (random choices, Greedy in fights),
    /// "Plain" (plain rules, Lookahead in fights), or either with the fight player named after a
    /// colon: "Walker:random", "Plain:greedy", "Walker:lookahead". Null for a name nobody has.
    /// </summary>
    /// <param name="seed">Seeds the walker's walk and the fight player's own dice.</param>
    public static IRunPlayer? ByName(string name, int seed = 1)
    {
        var parts = name.Split(':', 2);
        IPolicy? fights = null;
        if (parts.Length == 2)
        {
            fights = Fighter(parts[1], seed);
            if (fights is null) return null;
        }
        return parts[0].ToLowerInvariant() switch
        {
            "walker" => new RunWalker(seed, fights ?? Fighter("Greedy", seed)),
            "plain" => new PlainPlayer(fights ?? Fighter("Lookahead", seed)),
            _ => null,
        };
    }

    private static IPolicy? Fighter(string name, int seed) => name.ToLowerInvariant() switch
    {
        "random" => new RandomPolicy(seed),
        "lookahead" => new LookaheadPolicy(seed),
        _ => Policies.ByName(name),
    };

    public static bool IsPotionAction(string id) =>
        id.StartsWith("potion:", StringComparison.Ordinal) || id.StartsWith("discard_potion:", StringComparison.Ordinal);

    /// <summary>A number from a seed, a step and a word, for a player that must answer the same way every time it is asked the same thing.</summary>
    public static ulong Hash(int seed, int step, string word)
    {
        var hash = 14695981039346656037UL ^ (uint)seed;
        hash = unchecked((hash ^ (uint)step) * 1099511628211UL);
        foreach (var c in word) hash = unchecked((hash ^ c) * 1099511628211UL);
        return hash;
    }

    /// <summary>Picks in a row after which a card choice inside a fight is confirmed, whatever the fight player says.</summary>
    internal const int MostPicksInARow = 12;

    /// <summary>
    /// A fight player's answer to a snapshot of the run's fight: the searching ones get the fight
    /// itself to search on. One guard: a card choice that stays open at its maximum (Neow's Fury
    /// offers "confirm" from the start and lets every pick be taken back) is confirmed after
    /// twelve picks in a row, so that a player who answers the same screen the same way cannot
    /// pick and take back for ever.
    /// </summary>
    public static string InFight(IPolicy policy, Run run, GameSnapshot snapshot)
    {
        var fight = run.Fight ?? throw new InvalidOperationException("No fight is on.");
        var id = policy is IEnginePolicy searching ? searching.Choose(snapshot, fight) : policy.Choose(snapshot);
        return Guard(id, snapshot, run.Actions);
    }

    /// <summary>The guard of <see cref="InFight"/>: the fight player's answer, or "confirm" in its place after too many picks in a row.</summary>
    internal static string Guard(string id, GameSnapshot snapshot, IReadOnlyList<string> actions)
    {
        if (snapshot.Screen == "combat" || !IsPick(id) || snapshot.Actions.All(action => action.Id != "confirm")) return id;

        var picks = 0;
        for (var i = actions.Count - 1; i >= 0 && IsPick(actions[i]); i--) picks++;
        return picks >= MostPicksInARow ? "confirm" : id;
    }

    private static bool IsPick(string id) => id.StartsWith("card:", StringComparison.Ordinal) || id.StartsWith("select:", StringComparison.Ordinal);

    /// <summary>
    /// Plays the fight that is on in a run to its end with a fight player and returns how it
    /// ended (<see cref="Run.LastFight"/>); null when no fight is on. For a copy of a run
    /// (<see cref="Run.Clone"/>): the same fight from the same state by another player is the
    /// yardstick for a fight a model played (plan 5.6). What follows a won fight (its rewards)
    /// is put up as in any run; nothing of it is taken.
    /// </summary>
    public static FightOutcome? PlayFight(Run run, IPolicy policy, int mostActions = 6000)
    {
        if (run.Fight is null) return null;
        for (var n = 0; n < mostActions && run.Fight is not null; n++)
        {
            var snapshot = run.Snapshot();
            var choice = InFight(policy, run, snapshot);
            if (run.Execute(choice).Ok) continue;
            // A fight player that names an action that is not on offer gives up its turn, as in a single fight.
            var fallback = snapshot.Actions.Any(action => action.Id == "end_turn") ? "end_turn" : snapshot.Actions[0].Id;
            if (!run.Execute(fallback).Ok) break;
        }
        return run.Fight is null ? run.LastFight : null;
    }

    /// <summary>
    /// Lets a player play a run to its end, without any waiting: snapshot, choice, action, until
    /// the end snapshot is up. An action that is turned down is counted (it should never happen)
    /// and the first action on offer is taken in its place, so that one bad answer does not hang
    /// the run. The game is driven through its bridge, so a save file it writes
    /// (<see cref="RunGame.ActionLogPath"/>) grows as it would under a model.
    /// </summary>
    /// <param name="game">A game with no busy spell (<see cref="RunGame.ActionDelay"/> zero); a run already under way is carried on.</param>
    /// <param name="mostActions">Where the loop gives up (<see cref="ActResult.Stalled"/>).</param>
    public static ActResult Play(RunGame game, IRunPlayer player, int mostActions = MostActions)
    {
        var run = game.Run;
        var clock = Stopwatch.StartNew();
        var refused = 0;
        string? firstRefusal = null;
        var stalled = false;
        for (var step = 0; !run.AtEnd; step++)
        {
            if (step >= mostActions)
            {
                stalled = true;
                break;
            }
            var snapshot = game.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (snapshot.Actions.Count == 0)
            {
                // Nothing to choose and nobody else to act: the run would wait for ever.
                stalled = true;
                firstRefusal ??= $"{snapshot.Screen} on floor {run.State.TotalFloor} offers no action ({snapshot.Waiting})";
                break;
            }

            string choice;
            try
            {
                choice = player.Choose(run, snapshot);
            }
            catch (Exception ex)
            {
                refused++;
                firstRefusal ??= $"{player.Name} threw on {snapshot.Screen} on floor {run.State.TotalFloor}: {ex.GetType().Name}: {ex.Message}";
                choice = snapshot.Actions[0].Id;
            }

            var result = game.ExecuteAsync(choice, CancellationToken.None).GetAwaiter().GetResult();
            if (result.Ok) continue;

            refused++;
            firstRefusal ??= string.Create(CultureInfo.InvariantCulture, $"'{choice}' on {snapshot.Screen} on floor {run.State.TotalFloor}: {result.Error}");
            var fallback = snapshot.Actions.FirstOrDefault(action => !IsPotionAction(action.Id) && action.Id != choice) ?? snapshot.Actions[0];
            if (!game.ExecuteAsync(fallback.Id, CancellationToken.None).GetAwaiter().GetResult().Ok)
            {
                stalled = true;
                break;
            }
        }
        return ActResult.Of(run, player.Name, refused, firstRefusal, stalled, clock.Elapsed.TotalSeconds);
    }

    /// <summary>The same for a new run of the act on a seed.</summary>
    public static ActResult Play(string seed, IRunPlayer player, RunMode mode = RunMode.DrawAgain, StreamMode streams = StreamMode.Game, SeedScreen? screen = null) =>
        Play(ActRun.StartGame(seed, mode, streams, screen, actionDelay: TimeSpan.Zero), player);
}
