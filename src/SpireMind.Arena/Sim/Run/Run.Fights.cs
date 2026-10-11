using System.Globalization;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The fight a room asks the run to start. The player (HP, deck, relics, belt, gold) is the
/// run's; this names the enemies and the fight's random streams.
/// </summary>
public sealed record FightRequest
{
    /// <summary>A registered encounter by class name ("NibbitsWeak"): its own line-up, room type and HP rolls.</summary>
    public string? Encounter { get; init; }

    /// <summary>
    /// For checks: monsters placed by hand instead of an encounter. A function, because a monster
    /// object serves one fight: it is asked again when the run is replayed.
    /// </summary>
    internal Func<List<PlacedMonster>>? Monsters { get; init; }

    /// <summary>With <see cref="Monsters"/>: the slot order, and what kind of room the fight is.</summary>
    public IReadOnlyList<string> Slots { get; init; } = [];
    public RoomType RoomType { get; init; } = RoomType.Monster;

    /// <summary>
    /// A seed of the fight's own: its streams start at the fight's start, as a single arena fight's
    /// do, and the run's streams are left alone. For checks. Left out (and with no <see cref="Rng"/>),
    /// the fight runs on the run's streams when the run has them (<see cref="Run.StartFight"/>),
    /// and otherwise on the seed <see cref="Run.FightSeed"/> gives.
    /// </summary>
    public int? Seed { get; init; }

    /// <summary>
    /// The fight's streams themselves, when the caller makes them. The fight owns the object and
    /// works on copies of it: read the streams back from <see cref="Run.LastFightRng"/> when the
    /// fight is over, never from the object that was lent. A run whose world is begun needs
    /// neither this nor <see cref="Seed"/>: <see cref="Run.StartFight"/> lends its streams and
    /// takes them back by itself.
    /// </summary>
    public RngSet? Rng { get; init; }

    /// <summary>For checks: false keeps the deck in deck order, its first card drawn first.</summary>
    public bool ShuffleDeck { get; init; } = true;
}

/// <summary>How a fight of the run ended, for whoever decides what follows and for the result line.</summary>
/// <param name="Won">False for a death and for the turn limit.</param>
/// <param name="TimedOut">Lost to the arena's turn limit, not by dying.</param>
/// <param name="Encounter">The encounter's class name, "Custom" for monsters placed by hand.</param>
/// <param name="RoomType">Monster, Elite or Boss.</param>
/// <param name="Floor">The total floor the fight was on.</param>
/// <param name="Turns">The player's turn number when it ended.</param>
/// <param name="HpBefore">HP on entering.</param>
/// <param name="HpAfter">HP on leaving, with what the victory hooks healed (Burning Blood).</param>
/// <param name="MaxHpAfter">Max HP on leaving.</param>
/// <param name="DamageTaken">All HP lost in the fight, whatever took it.</param>
/// <param name="Monsters">How many monsters the fight had in all, summoned ones included.</param>
/// <param name="Escaped">How many of them left without dying.</param>
/// <param name="Enemies">The monsters' names, joined by " + ", in the order they joined.</param>
public sealed record FightOutcome(
    bool Won, bool TimedOut, string Encounter, RoomType RoomType, int Floor, int Turns,
    int HpBefore, int HpAfter, int MaxHpAfter, int DamageTaken, int Monsters, int Escaped, string Enemies);

/// <summary>
/// A fight as one of the run's screens: "combat", and "hand_select" or "card_select" while the
/// fight asks for cards. Its snapshots and action ids are the fight's own. When the fight is
/// over the run takes the player back and this screen is gone; the fight's own end snapshot is
/// never shown in a run.
/// </summary>
public sealed class FightScreen : RunScreen
{
    internal FightScreen(Fight fight, int hpBefore, bool onRunStreams = false)
    {
        Fight = fight;
        HpBefore = hpBefore;
        OnRunStreams = onRunStreams;
    }

    public Fight Fight { get; }

    /// <summary>HP the player entered with.</summary>
    public int HpBefore { get; }

    /// <summary>
    /// True when the run lent this fight its own streams (<see cref="Run.StartFight"/>): they go
    /// back to the run when the fight is over. False for a fight with a seed or streams of its own.
    /// </summary>
    public bool OnRunStreams { get; }

    public override string Name => "combat";

    public override bool CopiesItself => true;

    public override GameSnapshot Snapshot(Run run) => Fight.Snapshot();

    public override IReadOnlyList<LegalAction> LegalActions(Run run) => Fight.LegalActions();

    public override ActionResult Execute(Run run, string actionId)
    {
        var result = Fight.Execute(actionId);
        if (result.Ok && Fight.IsOver) run.FinishFight(this);
        return result;
    }

    public override RunScreen Clone(CloneMap map) => new FightScreen(Fight.Clone(), HpBefore, OnRunStreams);
}

/// <summary>
/// A run that is nothing but fights in a fixed order, each on the next floor, and is cleared
/// after the last: no map, no rewards. For checks, and for anything that wants the player
/// carried from fight to fight without the rooms in between.
/// </summary>
public sealed class FixedFights : RunDirector
{
    private readonly IReadOnlyList<FightRequest> _fights;
    private int _next;

    public FixedFights(params FightRequest[] fights) => _fights = fights.ToList();

    public override void Begin(Run run) => Next(run);

    public override void AfterFightWon(Run run, FightOutcome fight) => Next(run);

    private void Next(Run run)
    {
        if (_next >= _fights.Count)
        {
            run.End(RunResult.Cleared);
            return;
        }
        run.State.ActFloor++;
        run.State.TotalFloor++;
        run.StartFight(_fights[_next++]);
    }
}

// The hand-over to a fight and back. In the game nothing is handed anywhere: the player is one
// object for the whole run, and a fight borrows it (CombatManager.SetUpCombat clones the deck
// into the draw pile; when the fight is won the player's powers and Block are taken off and
// the rest simply stays). Here the fight gets copies and the run takes them back.
public sealed partial class Run
{
    /// <summary>
    /// Starts a fight with the run's player and puts it on top of the screens. What goes in: HP
    /// and max HP, the deck (every card with its id, upgrade and numbers), the relic objects with
    /// what they have counted, the belt with all its slots, the gold, and the total floor for the
    /// snapshot's run block. What does not: Block, powers, energy, anything of an earlier fight.
    /// <para>
    /// The fight's random streams are the run's, as in the game, once the run has them: when the
    /// run's world is begun (<see cref="RunWorld.Begin(RunState, RunUnlocks?, StreamMode, string, IReadOnlyList{string}?)"/>)
    /// and the request names neither a seed nor streams of its own, the fight is lent the run's
    /// streams as they stand (<see cref="RunStreams.LendToFight"/>: in game mode the long streams,
    /// so this fight shuffles on where the last one stopped; the encounter's own stream by seed,
    /// total floor and encounter) and the run takes them back when the fight is over. Set
    /// <see cref="RunState.TotalFloor"/> before the call: the encounter's stream is seeded with it.
    /// A run without a world gives the fight the arena's own seed (<see cref="FightSeed"/>).
    /// </para>
    /// </summary>
    public Fight StartFight(FightRequest request)
    {
        var state = _c.State;
        if (state.IsDead) throw new InvalidOperationException("A dead player cannot start a fight.");
        if (request.Encounter is null && request.Monsters is null) throw new ArgumentException("A fight needs an encounter or monsters.", nameof(request));

        var rng = request.Rng;
        var onRunStreams = false;
        if (rng is null && request.Seed is null && state.FindPart<RunWorld>() is { IsBegun: true } world)
        {
            // Monsters placed by hand have no encounter of the game's: their stream is named as the fight is.
            rng = world.Streams.LendToFight(request.Encounter ?? CustomEncounter, state.TotalFloor);
            onRunStreams = true;
        }

        var config = new FightConfig
        {
            Run = state.HandOver(),
            Hp = state.Hp,
            MaxHp = state.MaxHp,
            Encounter = request.Monsters is null ? request.Encounter : null,
            Monsters = request.Monsters?.Invoke(),
            Slots = request.Slots,
            RoomType = request.RoomType,
            Floor = state.TotalFloor,
            Seed = request.Seed ?? FightSeed(),
            Rng = rng,
            ShuffleDeck = request.ShuffleDeck,
            MaxTurns = state.MaxTurns,
        };
        _c.FightsStarted++;
        var fight = new Fight(config);
        var screen = new FightScreen(fight, state.Hp, onRunStreams);
        Push(screen);
        // A fight can be decided before the player's first decision (everything died in the set-up).
        if (fight.IsOver) FinishFight(screen);
        return fight;
    }

    /// <summary>The encounter name of a fight whose monsters were placed by hand (<see cref="Fight.EncounterName"/>).</summary>
    private const string CustomEncounter = "Custom";

    /// <summary>
    /// The seed a fight's streams get when the request names none and the run has no world to
    /// lend streams from: a hash of the run's seed, the act, the total floor and how many fights
    /// the run has started, taken as one text (sums of the parts would collide). So every fight
    /// of such a run has streams of its own that start at the fight's start, as a single arena
    /// fight has. A house rule, for runs of fights without the rest of a run around them.
    /// </summary>
    public int FightSeed() => RngSet.NameHash(string.Create(CultureInfo.InvariantCulture,
        $"{_c.State.Seed}|act {_c.State.ActIndex}|floor {_c.State.TotalFloor}|fight {_c.FightsStarted}"));

    /// <summary>
    /// The fight on top is over. The run takes the player back: HP and max HP as the fight left
    /// them (after a win that includes what the victory hooks healed), the gold, the belt, the
    /// relic objects, and the deck as it stands. Cards made in the fight, upgrades made in the
    /// fight, Block and powers end with it. Then a won fight goes to the director; a lost one
    /// ends the run: the mod's game over screen first, as in the game, then the end snapshot.
    /// </summary>
    internal void FinishFight(FightScreen screen)
    {
        if (!ReferenceEquals(Top, screen)) throw new InvalidOperationException("The fight that ended is not the top screen.");
        var fight = screen.Fight;
        if (_running)
        {
            // Inside an action of the run (a room started a fight that was decided at once): what
            // follows is part of that action, and a rule that throws undoes the whole action.
            if (TakeBack(screen)) _c.Director.AfterFightWon(this, _c.LastFight!);
            return;
        }

        bool won;
        try
        {
            won = TakeBack(screen);
        }
        catch (Exception ex)
        {
            // The fight is over and cannot be put back; a run that cannot go on says so and stops.
            End(RunResult.Error, $"after the fight on floor {fight.Floor}: {ex.GetType().Name}: {ex.Message}");
            return;
        }
        // What follows a won fight is an action of its own, so that it can ask the player something
        // and be run again with the answer. If it throws, the run ends as an error (Perform).
        if (won) RunAction(AfterFightAction);
    }

    /// <summary>Hands the player back and closes the fight's screen. False when the fight was lost: the run is over.</summary>
    private bool TakeBack(FightScreen screen)
    {
        var fight = screen.Fight;
        var state = _c.State;
        state.TakeBack(fight.Player.Hp, fight.Player.MaxHp, fight.HandBack());
        Pop();
        _c.LastFightRng = fight.Rng.Clone();
        // The run's long streams go on from where the fight left the ones it was lent.
        if (screen.OnRunStreams) state.FindPart<RunWorld>()?.Streams.TakeBack(fight.Rng);
        _c.LastFight = new FightOutcome(
            fight.Won, fight.TimedOut, fight.EncounterName, fight.RoomType, fight.Floor, fight.Turn,
            screen.HpBefore, state.Hp, state.MaxHp, fight.DamageTaken, fight.Roster.Count, fight.EscapedCount,
            string.Join(" + ", fight.Roster.Select(enemy => enemy.Name)));
        if (fight.Won) return true;

        _c.Result = RunResult.Died;
        _c.Reason = fight.TimedOut ? string.Create(CultureInfo.InvariantCulture, $"a fight was not won within {fight.MaxTurns} turns") : null;
        _c.Screens.Clear();
        _c.Screens.Add(new GameOverScreen(fight.RoomType.ToString()));
        return false;
    }
}
