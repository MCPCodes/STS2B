using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core;
using static SpireMind.Arena.DeckCheckKit;

namespace SpireMind.Arena;

/// <summary>
/// The event room, the walk of an act's event list, the Ancient's heal and Neow's offer.
/// <para>
/// What the expected values rest on. Neow's offer: the game's own record of nine offers
/// (sts2-notes/golden-saves/history: five single-player runs of v0.107.1 on Mark's profile, and
/// both players of two co-op runs), typed in below; the draw counts are worked out from the
/// lengths of the lists in Neow.cs. The event list: the list of seed 1BUSLN0FY5 as the game
/// saved it, walked by hand with each event's own condition from its class; the real run met
/// This or That there as its first event, with the counters the saves show. The heal: every
/// record of a new run shows 80 HP healed at Neow. The screen: docs/screens.md ("Event") and
/// the mod's EventReader, key for key and label for label, on events of the checks' own whose
/// numbers are set by hand (two of them shaped like This or That and Tablet of Truth).
/// </para>
/// </summary>
public sealed class EventsChecks : IArenaChecks
{
    // The entry point is the interface's own, so that inside this class "Run" is the run.
    void IArenaChecks.Run(Check c)
    {
        NeowsRealOffers(c);
        NeowsOfferRules(c);
        NeowsOfferWithRelicsLeftOut(c);
        NeowsRelicLists(c);
        WhenAnEventIsAllowed(c);
        TheWalk(c);
        TheWalkWithEventsLeftOut(c);
        OwnStream(c);
        ThePage(c);
        DeadlyOptionsAndPotions(c);
        LockedOptionsAndTips(c);
        Pages(c);
        ChoicesAndRewards(c);
        OrderOnEntering(c);
        CleanUp(c);
        DeathInAnEvent(c);
        Guards(c);
        CopiesAndReplays(c);
        NeowsRoom(c);
        TheRealRunsFirstEvent(c);
        RegistryAndCompleteness(c);
        GoldenFolder(c);
    }

    private const string RealSeed = "1BUSLN0FY5";

    private static readonly string[] OvergrowthActs = ["Overgrowth", "Hive", "Glory"];

    // ====================================================================================
    // helpers
    // ====================================================================================

    private static RunWorld World(Run run) => run.State.Part<RunWorld>();

    private static RunEvent Ev(Run run) => EventScreen.Current(run) ?? throw new InvalidOperationException("The run is in no event.");

    private static string State(Run run) => Text(run.Snapshot().State);

    private static string Q(string json) => json.Replace('\'', '"');

    private static string Player(int hp = 80, int maxHp = 80, int gold = 99, string relics = "'Burning Blood'", string deck = "'deck_size':10,'deck':{'Bash':1,'Defend':4,'Strike':5}") =>
        Q($"'player':{{'character':'The Ironclad','hp':{hp},'max_hp':{maxHp},'gold':{gold},'relics':[{relics}],{deck}}}");

    private static readonly string NoPotions = Q("'potions':[null,null,null]");

    /// <summary>A card as the screens list it outside a fight, by class name ("Bash+").</summary>
    private static string CardJson(string name)
    {
        var entry = DeckCard.Parse(name);
        var card = new RunState(new RunSetup()).NewCard(Content.Registry.Card(entry.ClassName), entry.Upgraded);
        return Text(RunDescribe.Card(card));
    }

    private static string? Refusal(Run run, string id)
    {
        var result = run.Execute(id);
        return result.Ok ? null : result.Error;
    }

    private static string Thrown<T>(Action body) where T : Exception
    {
        try
        {
            body();
        }
        catch (T ex)
        {
            return ex.Message;
        }
        return "(nothing was thrown)";
    }

    /// <summary>The stream of a player's own copy of Neow: the run's seed number plus the player's slot plus the hash of the event's id.</summary>
    private static GameRng NeowStream(string seed, int slot = 0) =>
        new(unchecked((uint)((int)new RunSeed(seed).Hash + slot + RngSet.NameHash("NEOW"))));

    // ---------- directors ----------

    /// <summary>The screen a check's director puts up when an event's room is left.</summary>
    private sealed class After : RunScreen
    {
        public override string Name => "after";

        public override GameSnapshot Snapshot(Run run) => new()
        {
            Screen = Name, AwaitingDecision = true, State = new JsonObject { ["run"] = run.RunJson() }, Actions = [new LegalAction("wait", "Wait")],
        };

        public override ActionResult Execute(Run run, string actionId) => actionId == "wait" ? ActionResult.Success : ActionResult.Fail($"Unknown action '{actionId}'.");

        public override RunScreen Clone(CloneMap map) => new After();
    }

    /// <summary>
    /// A run of one event room: the world of the seed (its first act set to Overgrowth, whatever
    /// the seed rolls), the floor, then the event. Leaving the room is counted.
    /// </summary>
    private sealed class OneRoom : RunDirector
    {
        /// <summary>Makes the event; null opens none (the checks of the list walk).</summary>
        public Func<RunEvent>? Event { get; init; }
        public int Floor { get; init; } = 1;
        public StreamMode Mode { get; init; } = StreamMode.Game;
        public bool Strict { get; init; }

        /// <summary>Numbers drawn from other streams of the run before the event starts.</summary>
        public int Noise { get; init; }

        public int Left;

        public override void Begin(Run run)
        {
            var world = RunWorld.Begin(run.State, mode: Mode, acts: OvergrowthActs);
            if (Strict) run.State.Part<LeftOutLog>().Strict = true;
            for (var i = 0; i < Noise; i++)
            {
                world.Streams.Draw(RunStream.Rewards, Purpose.Gold).NextInt(100);
                world.Streams.Draw(RunStream.Niche, Purpose.Of("noise")).NextInt(100);
            }
            run.State.TotalFloor = Floor;
            if (Event is null) run.Push(new After());
            else EventScreen.Open(run, Event());
        }

        public override void AfterFightWon(Run run, FightOutcome fight) => run.End(RunResult.Cleared);

        public override void AfterRoom(Run run)
        {
            Left++;
            run.Push(new After());
        }
    }

    private static Run InEvent(Func<RunEvent> make, RunSetup? setup = null, int floor = 1) =>
        new(setup ?? new RunSetup { Seed = RealSeed }, new OneRoom { Event = make, Floor = floor });

    /// <summary>A run with a world and no room, for asking the list.</summary>
    private static Run Listed(RunSetup? setup = null, int floor = 1, bool strict = false) =>
        new(setup ?? new RunSetup { Seed = RealSeed }, new OneRoom { Floor = floor, Strict = strict });

    // ---------- events of the checks ----------

    /// <summary>
    /// Shaped like This or That (ThisOrThat.cs), under its name, so that it rolls on that event's
    /// stream: gold rolled as one whole number from 41 to 68 when the event starts; Plain costs 6
    /// HP for it; Ornate adds a card, which its tooltip shows.
    /// </summary>
    private sealed class Chests : RunEvent
    {
        private int _gold;

        public override string ClassName => "ThisOrThat";
        public override string Title => "This or That";

        protected override void CalculateVars(Run run) => _gold = Rng.NextInt(41, 69);

        protected override string InitialDescription(Run run) => "Two chests stand here.";

        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) =>
        [
            Option("PLAIN", "Plain", $"Lose 6 HP. Gain {_gold} Gold.", Plain).ThatDoesDamage(6),
            Option("ORNATE", "Ornate", "Add Inflame to your Deck.", Ornate).WithCards("Inflame"),
        ];

        private void Plain(Run run)
        {
            run.Damage(6);
            if (run.IsOver) return;
            run.GainGold(_gold);
            SetEventFinished("You take the gold.");
        }

        private void Ornate(Run run)
        {
            DeckEdits.AddNew(run, "Inflame");
            SetEventFinished("You take the card.");
        }
    }

    /// <summary>
    /// Shaped like Tablet of Truth (TabletOfTruth.cs): Decipher costs 3 max HP, then 6, then 12,
    /// on a new page each time, and would kill when max HP is at or below the cost; Smash heals
    /// 20; from the second page on there is Give Up. The third Decipher finishes the event.
    /// </summary>
    private sealed class Tablet : RunEvent
    {
        private int _count;
        private decimal _cost = 3m;

        public override string Title => "Tablet of Truth";

        protected override string InitialDescription(Run run) => "A stone tablet.";

        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) =>
        [
            Option("DECIPHER_1", "Decipher", $"Lose {_cost} Max HP.", Decipher).ThatWillKillPlayerIf(TooCostly),
            Option("SMASH", "Smash", "Heal 20 HP.", Smash),
        ];

        private bool TooCostly(Run run) => run.MaxHp <= _cost;

        private void Smash(Run run)
        {
            run.Heal(20);
            SetEventFinished("You smash it.");
        }

        private void Decipher(Run run)
        {
            run.LoseMaxHp(_cost);
            if (run.IsOver) return;
            _count++;
            if (_count == 3)
            {
                SetEventFinished("You have read enough.");
                return;
            }
            _cost *= 2;
            SetEventState($"You read line {_count}.",
                Option("DECIPHER", "Decipher", $"Lose {_cost} Max HP.", Decipher).ThatWillKillPlayerIf(TooCostly),
                Option("GIVE_UP", "Give Up", "", GiveUp));
        }

        private void GiveUp(Run run) => SetEventFinished("You give up.");
    }

    /// <summary>A relic of the checks whose tooltip shows five cards, two of them the same.</summary>
    private sealed class GateKey : Relic, ICardTips
    {
        public override string Name => "Gate Key";
        public override string Describe() => "Opens gates.";
        public IReadOnlyList<string> CardTips => ["Bash+", "Bash+", "Bash", "Anger", "Inflame"];
    }

    /// <summary>
    /// An event with a lock, a card choice, a rewards screen and a relic: Pay is locked under 100
    /// gold (decided when the page is made); Cut rolls a number and then asks for a card to
    /// remove; Loot opens a rewards screen with 25 gold and finishes when it has closed; Take the
    /// Key gives a relic that its tooltip shows.
    /// </summary>
    private sealed class Gate : RunEvent
    {
        private const int Toll = 100;
        private int _rolled = -1;

        public override string Title => "The Gate";

        protected override string InitialDescription(Run run) => "A gate bars the way.";

        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) =>
        [
            run.Gold >= Toll ? Option("PAY", "Pay", $"Pay {Toll} Gold.", Pay) : LockedOption("PAY_LOCKED", "Pay", $"Requires {Toll} Gold."),
            Option("CUT", "Cut", "Remove a card from your Deck.", Cut),
            Option("LOOT", "Loot", "Find 25 Gold.", Loot),
            Option("KEY", "Take the Key", "Obtain Gate Key.", TakeKey).WithRelicTip(new GateKey()),
        ];

        private void Pay(Run run)
        {
            run.LoseGold(Toll);
            SetEventFinished("The gate opens.");
        }

        private void Cut(Run run)
        {
            _rolled = Rng.NextInt(100);
            DeckEdits.Remove(run, CardSelect.FromDeckForRemoval(run, CardSelectPrefs.Remove(1)));
            SetEventFinished($"You cut your way through ({_rolled}).");
        }

        private void Loot(Run run) => RunRewards.OfferCustom(run, [new GoldReward(25)]);

        protected override void AfterRewardsClosed(Run run, RewardsSet rewards) => SetEventFinished("You climb over with what you found.");

        private void TakeKey(Run run)
        {
            run.ObtainRelic(new GateKey());
            SetEventFinished("The gate opens.");
        }
    }

    /// <summary>An option that is a relic's own name and text and shows what the relic's tooltip shows besides the relic, as Tea Master's do.</summary>
    private sealed class Shelf : RunEvent
    {
        public override string Title => "The Shelf";
        protected override string InitialDescription(Run run) => "A shelf.";

        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run)
        {
            var key = new GateKey();
            return [Option("KEY", key.Name, key.Describe(), Take).WithCardTipsOf(key)];
        }

        private void Take(Run run)
        {
            run.ObtainRelic(new GateKey());
            SetEventFinished("Taken.");
        }
    }

    /// <summary>Options made in a loop, each with its number: slot n pays 10 gold times n plus 1.</summary>
    private sealed class Slots : RunEvent
    {
        public override string Title => "Slots";

        protected override string InitialDescription(Run run) => "Three slots.";

        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run)
        {
            var options = new List<EventOption>();
            for (var slot = 0; slot < 3; slot++) options.Add(Option("SLOT", $"Slot {slot}", $"Gain {10 * (slot + 1)} Gold.", Take).WithArgument(slot));
            return options;
        }

        private void Take(Run run, EventOption option)
        {
            run.GainGold(10 * (option.Argument + 1));
            SetEventFinished($"You took slot {option.Argument}.");
        }
    }

    /// <summary>An option that costs 10 HP and does not say so: the one way to die in an event room.</summary>
    private sealed class Pit : RunEvent
    {
        public override string Title => "The Pit";
        protected override string InitialDescription(Run run) => "A pit.";
        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) => [Option("FALL", "Fall", "Lose 10 HP.", Fall)];

        private void Fall(Run run)
        {
            run.Damage(10);
            if (run.IsOver) return;
            SetEventFinished("You climb out.");
        }
    }

    /// <summary>
    /// Holds the belt while it is open, as The Future of Potions does (BeforeEventStarted and
    /// OnEventFinished in TheFutureOfPotions.cs), and pays 1 gold each time its clean-up runs, so
    /// that the checks can count the clean-ups.
    /// </summary>
    private sealed class Holder : RunEvent
    {
        public override string Title => "The Holder";
        protected override void BeforeEventStarted(Run run) => run.State.Part<RewardsState>().CanRemovePotions = false;

        protected override void OnEventFinished(Run run)
        {
            run.State.Part<RewardsState>().CanRemovePotions = true;
            run.GainGold(1);
        }

        protected override string InitialDescription(Run run) => "Your potions are on the table.";

        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) =>
        [
            Option("TRADE", "Trade", "Finish.", Trade),
            Option("STAY", "Stay", "A last page with nothing on it.", Stay),
        ];

        private void Trade(Run run) => SetEventFinished("Done.");

        // A page without options: finished, but not through SetEventFinished.
        private void Stay(Run run) => SetEventState("Nothing more.");
    }

    /// <summary>Writes down the order its own methods are called in, as the first page's text.</summary>
    private sealed class Ordered : RunEvent
    {
        private string _order = "";
        public string Order => _order;
        public override string Title => "Ordered";
        protected override void BeforeEventStarted(Run run) => _order += "before ";
        protected override void CalculateVars(Run run) => _order += "vars ";

        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run)
        {
            _order += "options ";
            return [Option("GO", "Go", "", Go)];
        }

        protected override string InitialDescription(Run run) => _order += "text";
        protected override void AfterEventStarted(Run run) => _order += " started";
        private void Go(Run run) => SetEventFinished("");
    }

    /// <summary>
    /// Shaped like Unrest Site's second option (UnrestSite.cs): Kill costs 8 max HP and says so
    /// (ThatDecreasesMaxHp). Its other option, Look, pays 5 gold and leaves the page as it is.
    /// </summary>
    private sealed class Site : RunEvent
    {
        public override string Title => "Unrest Site";
        protected override string InitialDescription(Run run) => "A camp.";

        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) =>
        [
            Option("KILL", "Kill", "Lose 8 Max HP.", Kill).ThatDecreasesMaxHp(8),
            Option("LOOK", "Look", "Gain 5 Gold.", Look),
            Option("REST", "Rest", "", Rest),
        ];

        private void Kill(Run run)
        {
            run.LoseMaxHp(8);
            if (run.IsOver) return;
            SetEventFinished("Done.");
        }

        private void Look(Run run) => run.GainGold(5);

        private void Rest(Run run) => SetEventFinished("Rested.");
    }

    /// <summary>A relic of the checks under the class name of a relic of the bags.</summary>
    private sealed class Vajra : Relic
    {
        public override string Name => "Vajra";
        public override string Describe() => "A relic of the checks.";
    }

    /// <summary>Its one option would kill at any HP: a page nobody can leave.</summary>
    private sealed class Wall : RunEvent
    {
        public override string Title => "The Wall";
        protected override string InitialDescription(Run run) => "A wall.";
        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) => [Option("CLIMB", "Climb", "Lose 999 HP.", Climb).ThatDoesDamage(999)];
        private void Climb(Run run) => SetEventFinished("Over.");
    }

    /// <summary>Its option leads to a page with nothing but a locked option.</summary>
    private sealed class DeadEnd : RunEvent
    {
        public override string Title => "Dead End";
        protected override string InitialDescription(Run run) => "A corridor.";
        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) => [Option("ON", "Go On", "", GoOn)];

        private void GoOn(Run run)
        {
            run.GainGold(5);
            SetEventState("A locked door.", LockedOption("DOOR", "Door", "Requires a key."));
        }
    }

    /// <summary>What an option runs is a function that caught a local variable: refused when the option is made.</summary>
    private sealed class Sloppy : RunEvent
    {
        public override string Title => "Sloppy";
        protected override string InitialDescription(Run run) => "";

        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run)
        {
            var caught = run.Gold;
            return [Option("X", "X", "", _ => SetEventFinished($"{caught}"))];
        }
    }

    /// <summary>A function that uses nothing but the event itself is a method of the event: fine, also on a copy.</summary>
    private sealed class Tidy : RunEvent
    {
        public override string Title => "Tidy";
        protected override string InitialDescription(Run run) => "";
        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) => [Option("X", "X", "", _ => SetEventFinished(Title))];
    }

    /// <summary>Keeps a deck card in a field and shows it in an option's tooltip, as Slippery Bridge shows the card at risk;
    /// the tooltip also shows the first relic the player holds.</summary>
    private sealed class Keeper : RunEvent
    {
        private CardInstance? _card;

        public CardInstance? Card => _card;
        public override string Title => "The Keeper";
        protected override string InitialDescription(Run run) => "";

        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run)
        {
            _card = run.State.Deck[^1];
            return [Option("GIVE", "Give", $"Lose {_card.Title}.", Give).WithCard(_card).WithRelicTip(run.State.Relics[0])];
        }

        private void Give(Run run)
        {
            DeckEdits.Remove(run, _card!);
            SetEventFinished("Gone.");
        }
    }

    /// <summary>A list in a field and nothing that copies it.</summary>
    private sealed class Careless : RunEvent
    {
        private readonly List<int> _seen = new();
        public int Seen => _seen.Count;
        public override string Title => "Careless";
        protected override string InitialDescription(Run run) => "";
        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) => [];
    }

    /// <summary>The same list, copied.</summary>
    private sealed class Careful : RunEvent
    {
        private List<int> _seen = new();
        public int Seen => _seen.Count;
        public override string Title => "Careful";
        protected override string InitialDescription(Run run) => "";
        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) => [];
        protected override void OnCloned(CloneMap map) => _seen = _seen.ToList();
    }

    /// <summary>
    /// A relic of the checks that hears "after room entered" in an event room: it notes what the
    /// event has done by then (numbers drawn from its stream, options on its page) and the
    /// player's HP, and pays 50 gold.
    /// </summary>
    private sealed class Tollkeeper : Relic
    {
        public int SawDraws = -1, SawOptions = -1, SawHp = -1;
        public override string Name => "Tollkeeper";
        public override string Describe() => "A relic of the checks.";

        public override void AfterRoomEntered(RunContext c, RoomKind room)
        {
            if (room != RoomKind.Event || c.Run is not { } run) return;
            var ev = EventScreen.Current(run)!;
            SawDraws = ev.Rng.Counter;
            SawOptions = ev.Options.Count;
            SawHp = run.Hp;
            c.GainGold(50);
        }
    }

    // ---------- Neow with relics of the checks ----------

    /// <summary>
    /// Relics of the checks under the class names of Neow's own, so that Neow's room can be
    /// played before those relics are built. Arcane Scroll does what the game's does (one Rare
    /// card of the character's pool: one pick, no rarity roll, no upgrade roll), since the real
    /// run took it and its card is known; the others do one plain thing each.
    /// </summary>
    private static class StandIns
    {
        public sealed class ArcaneScroll : Relic
        {
            public override string Name => "Arcane Scroll";
            public override string Describe() => "At pickup: add a random Rare card to your Deck.";

            public override void AfterObtained(RunContext c)
            {
                var run = c.NeedRun("Arcane Scroll's card");
                var options = new CardCreationOptions([CardPools.Character], CardCreationSource.Other, CardOddsType.Uniform, static card => card.Rarity == CardRarity.Rare)
                    .WithFlags(CardCreationFlags.NoUpgradeRoll);
                DeckEdits.Add(run, CardFactory.CreateForReward(run, 1, options)[0].Card);
            }
        }

        public sealed class PreciseScissors : Relic
        {
            public override string Name => "Precise Scissors";
            public override string Describe() => "At pickup: remove a card from your Deck.";

            public override void AfterObtained(RunContext c)
            {
                var run = c.NeedRun("Precise Scissors' choice");
                DeckEdits.Remove(run, CardSelect.FromDeckForRemoval(run, CardSelectPrefs.Remove(1)));
            }
        }

        public sealed class LeafyPoultice : Relic
        {
            public override string Name => "Leafy Poultice";
            public override string Describe() => "At pickup: lose 12 Max HP.";
            public override void AfterObtained(RunContext c) => c.LoseMaxHp(12);
        }

        public sealed class NeowsTorment : Relic, ICardTips
        {
            public override string Name => "Neow's Torment";
            public override string Describe() => "At pickup: add a Bash to your Deck.";
            public IReadOnlyList<string> CardTips => ["Bash"];
            public override void AfterObtained(RunContext c) => DeckEdits.AddNew(c.NeedRun("Neow's Torment's card"), "Bash");
        }

        public sealed class LavaRock : Relic
        {
            public override string Name => "Lava Rock";
            public override string Describe() => "A relic of the checks.";
        }

        public sealed class CursedPearl : Relic
        {
            public override string Name => "Cursed Pearl";
            public override string Describe() => "At pickup: gain 333 Gold.";
            public override void AfterObtained(RunContext c) => c.GainGold(333);
        }

        public static readonly IReadOnlyDictionary<string, Func<Relic>> Make = new Dictionary<string, Func<Relic>>(StringComparer.Ordinal)
        {
            ["ArcaneScroll"] = static () => new ArcaneScroll(), ["PreciseScissors"] = static () => new PreciseScissors(),
            ["LeafyPoultice"] = static () => new LeafyPoultice(), ["NeowsTorment"] = static () => new NeowsTorment(),
            ["LavaRock"] = static () => new LavaRock(), ["CursedPearl"] = static () => new CursedPearl(),
        };
    }

    /// <summary>Neow herself, on her own stream, with the relics of <see cref="StandIns"/> as all that is built.</summary>
    private sealed class StandInNeow : Neow
    {
        public override string ClassName => "Neow";
        protected override bool IsBuilt(string relicClassName) => StandIns.Make.ContainsKey(relicClassName);
        protected override Relic NewRelic(string relicClassName) => StandIns.Make[relicClassName]();
    }

    /// <summary>A room of the checks' act that is not an event: it can be left.</summary>
    private sealed class StandInRoom : RunScreen
    {
        public override string Name => "room";

        public override GameSnapshot Snapshot(Run run) => new()
        {
            Screen = Name, AwaitingDecision = true, State = new JsonObject { ["run"] = run.RunJson() }, Actions = [new LegalAction("proceed", "Proceed")],
        };

        public override ActionResult Execute(Run run, string actionId)
        {
            if (actionId != "proceed") return ActionResult.Fail($"Unknown action '{actionId}'.");
            run.RoomFinished();
            return ActionResult.Success;
        }

        public override RunScreen Clone(CloneMap map) => new StandInRoom();
    }

    /// <summary>
    /// The start of an act as the game lays it out, as far as events go: the map, the start
    /// point entered at once, Neow, then travel; an Event room takes its event from the act's
    /// list and opens the checks' own event in its place; every other room can only be left.
    /// </summary>
    private sealed class ActStart : RunDirector, IMapDirector
    {
        /// <summary>Which events count as built: "all", "none", or names.</summary>
        public string Built { get; init; } = "all";
        public bool Strict { get; init; }
        public bool ForceOvergrowth { get; init; }

        public List<string> Rooms = new();
        public List<string> Pulled = new();
        public List<string> Asked = new();

        private bool IsBuilt(string name) => Built == "all" || (Built != "none" && Built.Split(' ').Contains(name));

        public override void Begin(Run run)
        {
            RunWorld.Begin(run.State, acts: ForceOvergrowth ? OvergrowthActs : null);
            if (Strict) run.State.Part<LeftOutLog>().Strict = true;
            var map = MapState.BeginAct(run, 0);
            var arrival = map.Enter(run, map.Map.Start.Coord);
            EventScreen.Open(run, new StandInNeow());
            MapState.MarkRoomVisited(run, arrival.Room);
        }

        public bool CanHold(Run run, RolledRoom room)
        {
            if (room != RolledRoom.Event) return true;
            Asked.Add($"floor {run.State.TotalFloor}, {EventList.FloorsBehind(run)} behind");
            return EventList.CanHold(run, IsBuilt);
        }

        public void EnterRoom(Run run, MapArrival arrival)
        {
            Rooms.Add(arrival.Room.ToString());
            if (arrival.Room == RoomKind.Event)
            {
                Asked.Add($"entered on floor {run.State.TotalFloor}, {EventList.FloorsBehind(run)} behind");
                Pulled.Add(EventList.Pull(run, IsBuilt));
                EventScreen.Open(run, new Chests());
            }
            else
            {
                run.Push(new StandInRoom());
            }
            MapState.MarkRoomVisited(run, arrival.Room);
        }

        public override void AfterFightWon(Run run, FightOutcome fight) => run.End(RunResult.Cleared);

        public override void AfterRoom(Run run)
        {
            run.CloseAll();
            run.Push(new MapScreen());
        }

        public override RunDirector Clone(CloneMap map) => new ActStart
        {
            Built = Built, Strict = Strict, ForceOvergrowth = ForceOvergrowth, Rooms = Rooms.ToList(), Pulled = Pulled.ToList(), Asked = Asked.ToList(),
        };
    }

    // ====================================================================================
    // Neow's offer
    // ====================================================================================

    /// <summary>The game's own record of what Neow offered (golden-saves/history/&lt;file&gt;.run, map_point_history[0][0].player_stats[slot].ancient_choice).</summary>
    private static readonly (string Seed, int Players, int Slot, string[] Offer, int Draws, string File)[] RealOffers =
    [
        // Single player, Mark's profile. Draws: 1 pick, 3 coins, and a shuffle of 15 relics (14 draws) when the
        // cursed relic has a twin (14 - 1 twin + 3 coins - Massive Scroll), of 16 (15 draws) when it has none.
        ("1BUSLN0FY5", 1, 0, ["PreciseScissors", "ArcaneScroll", "LeafyPoultice"], 18, "1791755111"),
        ("1CJXGGA4E5", 1, 0, ["NeowsTorment", "LavaRock", "CursedPearl"], 18, "1791585653"),
        ("75F277YN2G", 1, 0, ["Pomander", "NutritiousOyster", "SilverCrucible"], 19, "1791592972"),
        ("3FBTGQ63TM", 1, 0, ["StoneHumidifier", "NewLeaf", "CursedPearl"], 18, "1791596381"),
        ("AG5XPLQQNY", 1, 0, ["PhialHolster", "GoldenPearl", "SilkenTress"], 19, "1791597100"),
        // Two players: each has an offer on the stream of their slot. Massive Scroll is in, Winged Boots is out,
        // so the list is as long as alone: 15 with a twin gone.
        ("FS5LGNDZZ7", 2, 0, ["LostCoffer", "MassiveScroll", "CursedPearl"], 18, "1791745191"),
        ("FS5LGNDZZ7", 2, 1, ["GoldenPearl", "LavaRock", "PrecariousShears"], 18, "1791745191"),
        ("BQ4CE2FRRJ", 2, 0, ["MassiveScroll", "NeowsTorment", "HeftyTablet"], 18, "1783968626"),
        ("BQ4CE2FRRJ", 2, 1, ["LostCoffer", "ArcaneScroll", "CursedPearl"], 18, "1783968626"),
    ];

    private static void NeowsRealOffers(Check c)
    {
        c.Group("Neow's offer comes out of the seed as the real game made it: nine recorded offers", () =>
        {
            foreach (var (seed, players, slot, expected, draws, file) in RealOffers)
            {
                var rng = NeowStream(seed, slot);
                var offer = NeowOffer.Roll(rng, RunUnlocks.All, isBuilt: static _ => true, players: players);
                c.Sequence(expected, offer.Relics, $"seed {seed}, {(players == 1 ? "alone" : $"player {slot + 1} of {players}")} (history/{file}.run)");
                c.Sequence(expected, offer.GameRelics, $"seed {seed}: and that is the game's own offer");
                c.That(offer.IsTheGames && offer.PassedOver.Count == 0, $"seed {seed}: nothing was passed over");
                c.Equal(draws, rng.Counter, $"seed {seed}: numbers taken from Neow's stream");
            }
        });

        c.Group("the offer of a seed is the same for the run's own streams, for Mark's unlock state and in strict mode", () =>
        {
            // The save of seed 1BUSLN0FY5 taken on Neow's screen carries the profile's unlock state.
            var save = GoldenSave.Parse(RunGoldenData.Saves.First(s => s.File == "run-164514-abe3bfe862ef.save").Json);
            c.Equal(RealSeed, save.Seed.Text, "the save is that seed's");
            c.Sequence(["PreciseScissors", "ArcaneScroll", "LeafyPoultice"], NeowOffer.OfTheGame(new RunStreams(save.Seed), save.Unlocks), "with the unlock state of the save");
            foreach (var (seed, players, _, expected, _, _) in RealOffers.Where(o => o.Players == 1))
            {
                var streams = new RunStreams(seed);
                c.Sequence(expected, NeowOffer.OfTheGame(streams, RunUnlocks.All), $"seed {seed}: from the run's streams, as a seed screen asks");
                c.Sequence(expected, NeowOffer.OfTheGame(new RunStreams(seed, StreamMode.Keyed), RunUnlocks.All), $"seed {seed}: the same with keyed streams");
                var strict = NeowOffer.Roll(streams.Event("Neow"), RunUnlocks.All, isBuilt: static _ => true, strict: true, players: players);
                c.Sequence(expected, strict.Relics, $"seed {seed}: strict, with everything built");
            }
            // Asking twice gives the same: the event's stream is made anew each time.
            var twice = new RunStreams(RealSeed);
            c.Sequence(NeowOffer.OfTheGame(twice, RunUnlocks.All), NeowOffer.OfTheGame(twice, RunUnlocks.All), "asked twice");
        });
    }

    /// <summary>
    /// The rule of Neow.cs once more, written straight down with nothing of <see cref="NeowOffer"/>
    /// or <see cref="NeowRelics"/>: the cursed pick (drawn again while it is not built), the twin,
    /// the coins, the relics the player may not be offered, the shuffle from the last place down,
    /// then the first two built relics.
    /// </summary>
    private static (List<string> Offer, List<string> Passed) SecondReading(GameRng rng, Func<string, bool> built, bool kaleidoscope, int players)
    {
        var cursed = new List<string> { "CursedPearl", "HeftyTablet", "LargeCapsule", "LeafyPoultice", "NeowsBones", "PrecariousShears", "SilkenTress" };
        if (players == 1) cursed.Add("SilverCrucible");
        var passed = new List<string>();
        string pick;
        while (true)
        {
            pick = cursed[(int)(rng.NextDouble() * cursed.Count)];
            if (built(pick)) break;
            passed.Add(pick);
        }

        var list = new List<string>
        {
            "ArcaneScroll", "BoomingConch", "FishingRod", "GoldenPearl", "Kaleidoscope", "LeadPaperweight", "LostCoffer", "MassiveScroll",
            "NeowsTorment", "NewLeaf", "PhialHolster", "PreciseScissors", "ScrollBoxes", "WingedBoots",
        };
        switch (pick)
        {
            case "CursedPearl": list.Remove("GoldenPearl"); break;
            case "HeftyTablet": list.Remove("ArcaneScroll"); break;
            case "LeafyPoultice": list.Remove("NewLeaf"); break;
            case "PrecariousShears": list.Remove("PreciseScissors"); break;
        }
        if (pick != "LargeCapsule") list.Add(rng.NextInt(2) == 0 ? "LavaRock" : "SmallCapsule");
        list.Add(rng.NextInt(2) == 0 ? "NutritiousOyster" : "StoneHumidifier");
        list.Add(rng.NextInt(2) == 0 ? "NeowsTalisman" : "Pomander");
        list.Remove(players == 1 ? "MassiveScroll" : "WingedBoots");
        if (!kaleidoscope) list.Remove("Kaleidoscope");
        for (var last = list.Count - 1; last >= 1; last--)
        {
            var at = rng.NextInt(last + 1);
            var held = list[last];
            list[last] = list[at];
            list[at] = held;
        }

        var offer = new List<string>();
        foreach (var relic in list)
        {
            if (offer.Count == 2) break;
            if (built(relic)) offer.Add(relic);
            else passed.Add(relic);
        }
        offer.Add(pick);
        return (offer, passed);
    }

    private static void NeowsOfferRules(Check c)
    {
        c.Group("what every offer keeps, on 2,000 seeds", () =>
        {
            var seeds = new GameRng(20261011);
            var cursedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var bad = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var n = 0; n < 2000; n++)
            {
                var seed = RunSeed.Random(seeds).Text;
                var rng = NeowStream(seed);
                var offer = NeowOffer.Roll(rng, RunUnlocks.All, isBuilt: static _ => true).Relics;
                var (a, b, cursed) = (offer[0], offer[1], offer[2]);
                cursedCounts[cursed] = cursedCounts.GetValueOrDefault(cursed) + 1;
                seen.Add(a);
                seen.Add(b);
                seen.Add(cursed);

                void Must(bool ok, string what)
                {
                    if (!ok) bad.Add($"{seed}: {what} ({string.Join(", ", offer)})");
                }
                Must(offer.Count == 3 && a != b, "three relics, the first two different");
                Must(NeowRelics.Cursed.Contains(cursed), "the third is a cursed one");
                Must(!NeowRelics.Cursed.Contains(a) && !NeowRelics.Cursed.Contains(b), "the first two are not");
                Must(a != "MassiveScroll" && b != "MassiveScroll", "Massive Scroll is never offered to one player");
                Must(!NeowRelics.Twins.TryGetValue(cursed, out var twin) || (a != twin && b != twin), "a cursed relic never comes with its twin");
                Must(cursed != "LargeCapsule" || (a is not ("LavaRock" or "SmallCapsule") && b is not ("LavaRock" or "SmallCapsule")), "Large Capsule never comes with Lava Rock or Small Capsule");
                foreach (var (heads, tails) in NeowRelics.Coins)
                    Must(!(new[] { a, b }.Contains(heads) && new[] { a, b }.Contains(tails)), "never both relics of a coin");
                // 1 pick; 3 coins (2 with Large Capsule); a shuffle of the 14, less a twin, plus the coins, less Massive Scroll.
                var expected = cursed == "LargeCapsule" ? 1 + 2 + 14 : NeowRelics.Twins.ContainsKey(cursed) ? 1 + 3 + 14 : 1 + 3 + 15;
                Must(rng.Counter == expected, $"{expected} numbers, not {rng.Counter}");
            }
            c.Sequence([], bad.Take(5), "offers that break a rule");
            c.Equal(8, cursedCounts.Count, "all eight cursed relics come up");
            // One in eight each: 250 of 2,000, give or take (a standard deviation is 15).
            c.That(cursedCounts.Values.All(count => count is > 190 and < 310), $"each about one time in eight ({string.Join(", ", cursedCounts.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key} {k.Value}"))})");
            c.Equal(27, seen.Count, "all 27 relics a single player can be offered come up, and no other");
            c.That(!seen.Contains("MassiveScroll"), "the 28th, Massive Scroll, does not");
        });

        c.Group("a profile without all five characters is not offered Kaleidoscope, and its list is one shorter", () =>
        {
            var unlocks = new RunUnlocks(GameLists.Epochs.Where(epoch => epoch != "DEFECT1_EPOCH"), [], 5, ["Overgrowth"]);
            c.That(!NeowRelics.IsAllowed("Kaleidoscope", unlocks), "one character locked: not allowed");
            c.That(NeowRelics.IsAllowed("Kaleidoscope", RunUnlocks.All), "all unlocked: allowed");
            foreach (var missing in new[] { "SILENT1_EPOCH", "REGENT1_EPOCH", "NECROBINDER1_EPOCH" })
                c.That(!NeowRelics.IsAllowed("Kaleidoscope", new RunUnlocks(GameLists.Epochs.Where(epoch => epoch != missing), [], 5, [])), $"without {missing}: not allowed");
            var rng = NeowStream(RealSeed);
            var offer = NeowOffer.Roll(rng, unlocks, isBuilt: static _ => true);
            // The cursed pick is the same number; the shuffle is of 14, so one draw less: 1 + 3 + 13.
            c.Equal("LeafyPoultice", offer.Relics[2], "the same cursed relic: the first number does not change");
            c.Equal(17, rng.Counter, "one number less");
            var seeds = new GameRng(7);
            var any = Enumerable.Range(0, 300).Select(_ => NeowOffer.Roll(NeowStream(RunSeed.Random(seeds).Text), unlocks, isBuilt: static _ => true).Relics).Any(o => o.Contains("Kaleidoscope"));
            c.That(!any, "and Kaleidoscope is in none of 300 offers");
        });

        c.Group("who may be offered what", () =>
        {
            var nothing = new RunUnlocks([], [], 0, []);
            c.That(!NeowRelics.IsAllowed("MassiveScroll", RunUnlocks.All) && NeowRelics.IsAllowed("MassiveScroll", RunUnlocks.All, players: 2), "Massive Scroll: only with a second player");
            c.That(NeowRelics.IsAllowed("WingedBoots", RunUnlocks.All) && !NeowRelics.IsAllowed("WingedBoots", RunUnlocks.All, players: 2), "Winged Boots: only alone");
            c.That(NeowRelics.IsAllowed("SilverCrucible", RunUnlocks.All) && !NeowRelics.IsAllowed("SilverCrucible", RunUnlocks.All, players: 2), "Silver Crucible: only alone");
            // Scroll Boxes needs 4 Commons and 2 Uncommons unlocked. The Ironclad's pool has 20 and 36; 17 and 32 of them need no epoch.
            c.Equal(20, CardPools.Ironclad.All.Count(card => card.Rarity == CardRarity.Common), "Commons in the Ironclad's pool");
            c.Equal(36, CardPools.Ironclad.All.Count(card => card.Rarity == CardRarity.Uncommon), "Uncommons");
            c.Equal(17, CardPools.Ironclad.Unlocked(nothing).Count(card => card.Rarity == CardRarity.Common), "Commons a new profile has");
            c.Equal(32, CardPools.Ironclad.Unlocked(nothing).Count(card => card.Rarity == CardRarity.Uncommon), "Uncommons a new profile has");
            c.That(NeowRelics.IsAllowed("ScrollBoxes", nothing) && NeowRelics.IsAllowed("ScrollBoxes", RunUnlocks.All), "so Scroll Boxes is always allowed for the Ironclad");
            c.That(NeowRelics.All.Where(relic => relic is not ("MassiveScroll" or "Kaleidoscope")).All(relic => NeowRelics.IsAllowed(relic, nothing)), "everything else needs nothing");
        });
    }

    private static void NeowsOfferWithRelicsLeftOut(Check c)
    {
        // Neow's stream of seed 1BUSLN0FY5, read off the stream itself: as picks among eight its first numbers are
        // 3, 1, 1, 6, 6, 7. The game's offer takes the first (Leafy Poultice), three coins (Lava Rock, Nutritious
        // Oyster, Pomander) and fourteen numbers for the shuffle, which leaves the positive relics in this order
        // (the swaps were followed by hand from the list in Neow.cs):
        string[] shuffled =
        [
            "PreciseScissors", "ArcaneScroll", "BoomingConch", "LostCoffer", "LeadPaperweight", "PhialHolster", "GoldenPearl", "Kaleidoscope",
            "NeowsTorment", "ScrollBoxes", "Pomander", "WingedBoots", "FishingRod", "NutritiousOyster", "LavaRock",
        ];

        c.Group("the numbers the hand-worked cases rest on are the stream's", () =>
        {
            var raw = NeowStream(RealSeed);
            c.Sequence([3, 1, 1, 6, 6, 7], Enumerable.Range(0, 6).Select(_ => raw.NextInt(0, 8)), "the first six numbers of Neow's stream as picks among eight");
            c.Sequence(shuffled.Take(2).Append("LeafyPoultice"), NeowOffer.Roll(NeowStream(RealSeed), RunUnlocks.All, isBuilt: static _ => true).Relics,
                "the first two of the shuffled list and the first pick are the offer the real game made");
            c.Equal(15, shuffled.Length, "fifteen relics were shuffled: the fourteen, less New Leaf and Massive Scroll, plus three");
        });

        c.Group("a positive relic that is not built is passed over: the next of the shuffled list is shown", () =>
        {
            var rng = NeowStream(RealSeed);
            var offer = NeowOffer.Roll(rng, RunUnlocks.All, isBuilt: static relic => relic != "PreciseScissors");
            c.Sequence(["ArcaneScroll", "BoomingConch", "LeafyPoultice"], offer.Relics, "the second and third of the shuffled list");
            c.Sequence(["PreciseScissors"], offer.PassedOver, "Precise Scissors was passed over");
            c.Sequence(["PreciseScissors", "ArcaneScroll", "LeafyPoultice"], offer.GameRelics, "the game's own offer is still known");
            c.That(offer.Found && !offer.IsTheGames, "an offer, but not the game's");
            c.Equal(18, rng.Counter, "the same eighteen numbers: nothing is drawn again for a positive relic");

            // Only what stands at places 4 and 9 of the shuffled list is built (Lead Paperweight is at 4, Neow's Torment at 8, Scroll Boxes at 9).
            var late = NeowOffer.Roll(NeowStream(RealSeed), RunUnlocks.All, isBuilt: static relic => relic is "LeadPaperweight" or "ScrollBoxes" or "LeafyPoultice");
            c.Sequence(["LeadPaperweight", "ScrollBoxes", "LeafyPoultice"], late.Relics, "the first two built ones, in the list's order");
            c.Sequence(shuffled.Take(9).Where(relic => relic != "LeadPaperweight"), late.PassedOver, "everything before them was passed over, in order");
        });

        c.Group("the 14 relics of the first playable version hold this seed's whole offer", () =>
        {
            string[] first = ["ArcaneScroll", "BoomingConch", "GoldenPearl", "LostCoffer", "NeowsTorment", "NewLeaf", "PreciseScissors", "NutritiousOyster",
                "NeowsTalisman", "Pomander", "CursedPearl", "HeftyTablet", "LeafyPoultice", "PrecariousShears"];
            var offer = NeowOffer.Roll(NeowStream(RealSeed), RunUnlocks.All, isBuilt: first.Contains);
            c.Sequence(["PreciseScissors", "ArcaneScroll", "LeafyPoultice"], offer.Relics, "all three are among them");
            c.That(offer.IsTheGames, "so the offer is the game's");
            var strict = NeowOffer.Roll(NeowStream(RealSeed), RunUnlocks.All, isBuilt: first.Contains, strict: true);
            c.That(strict.Found && strict.IsTheGames, "also in strict mode");
            var stream = NeowStream(RealSeed);
            NeowOffer.Roll(stream, RunUnlocks.All, isBuilt: first.Contains, strict: true);
            c.Equal(18, stream.Counter, "a strict offer takes its eighteen numbers from the stream like any other");
        });

        c.Group("a cursed relic that is not built is drawn again from the same stream", () =>
        {
            var rng = NeowStream(RealSeed);
            var offer = NeowOffer.Roll(rng, RunUnlocks.All, isBuilt: static relic => relic != "LeafyPoultice");
            // The second number is 1: Hefty Tablet, whose twin Arcane Scroll leaves the list.
            c.Equal("HeftyTablet", offer.Relics[2], "the second number of the stream picks Hefty Tablet");
            c.Sequence(["LeafyPoultice"], offer.PassedOver, "Leafy Poultice was passed over");
            c.That(!offer.Relics.Contains("ArcaneScroll"), "and Hefty Tablet's twin is not offered");
            c.Equal(2 + 3 + 14, rng.Counter, "one number more than the game's eighteen");

            // Neither Leafy Poultice nor Hefty Tablet: the numbers 3, 1, 1 are passed over, the fourth is 6, Silken Tress.
            rng = NeowStream(RealSeed);
            offer = NeowOffer.Roll(rng, RunUnlocks.All, isBuilt: static relic => relic is not ("LeafyPoultice" or "HeftyTablet"));
            c.Equal("SilkenTress", offer.Relics[2], "the fourth number picks Silken Tress");
            c.Sequence(["LeafyPoultice", "HeftyTablet", "HeftyTablet"], offer.PassedOver, "three picks were passed over");
            c.Equal(4 + 3 + 15, rng.Counter, "four picks, three coins, a shuffle of sixteen");
        });

        c.Group("strict mode passes nothing over: an offer with a relic that is not built is no offer", () =>
        {
            var rng = NeowStream(RealSeed);
            var offer = NeowOffer.Roll(rng, RunUnlocks.All, isBuilt: static relic => relic != "ArcaneScroll", strict: true);
            c.That(!offer.Found && offer.Relics.Count == 0, "no offer");
            c.That(offer.Reason is { } reason && reason.Contains("ArcaneScroll", StringComparison.Ordinal) && reason.Contains("PreciseScissors, ArcaneScroll, LeafyPoultice", StringComparison.Ordinal), $"the reason names the offer and the relic ({offer.Reason})");
            c.Equal(0, rng.Counter, "and the stream is left as it was");
        });

        c.Group("when nothing built is left to land on there is no offer", () =>
        {
            var noCursed = NeowOffer.Roll(NeowStream(RealSeed), RunUnlocks.All, isBuilt: static relic => !NeowRelics.Cursed.Contains(relic));
            c.That(!noCursed.Found && noCursed.Reason!.StartsWith("Neow's cursed relic can only be one the arena leaves out", StringComparison.Ordinal), $"no cursed relic is built ({noCursed.Reason})");
            var onePositive = NeowOffer.Roll(NeowStream(RealSeed), RunUnlocks.All, isBuilt: static relic => relic is "LeafyPoultice" or "BoomingConch");
            c.That(!onePositive.Found && onePositive.Reason!.StartsWith("Neow's positive relics hold fewer than two the arena has built", StringComparison.Ordinal), $"one positive relic is built ({onePositive.Reason})");
            // New Leaf is built, but Leafy Poultice takes it off the list: still only one.
            var twinGone = NeowOffer.Roll(NeowStream(RealSeed), RunUnlocks.All, isBuilt: static relic => relic is "LeafyPoultice" or "BoomingConch" or "NewLeaf");
            c.That(!twinGone.Found, "a built twin does not count when its cursed relic is the pick");
        });

        c.Group("the offer against a second reading of the rule, on 600 seeds with random sets of built relics", () =>
        {
            var dice = new GameRng(99);
            var differences = new List<string>();
            var compared = 0;
            for (var n = 0; n < 600; n++)
            {
                var seed = RunSeed.Random(dice).Text;
                var players = n % 5 == 4 ? 2 : 1;
                var kaleidoscope = n % 3 != 0;
                var unlocks = kaleidoscope ? RunUnlocks.All : new RunUnlocks(GameLists.Epochs.Where(epoch => epoch != "REGENT1_EPOCH"), [], 5, []);
                // Every relic is built with a chance of two in three (all of them every fourth time).
                var built = new HashSet<string>(NeowRelics.All.Where(_ => n % 4 == 0 || dice.NextInt(3) > 0), StringComparer.Ordinal);
                if (!NeowRelics.Cursed.Any(relic => built.Contains(relic) && NeowRelics.IsAllowed(relic, unlocks, players))) continue;

                var mine = NeowStream(seed);
                var theirs = NeowStream(seed);
                var offer = NeowOffer.Roll(mine, unlocks, built.Contains, players: players);
                var (expected, passed) = SecondReading(theirs, built.Contains, kaleidoscope, players);
                compared++;
                if (expected.Count < 3)
                {
                    if (offer.Found) differences.Add($"{seed}: an offer where fewer than two positive relics are built");
                    continue;
                }
                if (!offer.Relics.SequenceEqual(expected)) differences.Add($"{seed}: {string.Join(",", offer.Relics)} against {string.Join(",", expected)}");
                if (!offer.PassedOver.SequenceEqual(passed)) differences.Add($"{seed}: passed over {string.Join(",", offer.PassedOver)} against {string.Join(",", passed)}");
                if (mine.Counter != theirs.Counter) differences.Add($"{seed}: {mine.Counter} numbers against {theirs.Counter}");
            }
            c.Sequence([], differences.Take(5), "differences");
            c.That(compared > 500, $"{compared} offers were compared");
        });
    }

    private static void NeowsRelicLists(Check c)
    {
        c.Group("the lists are the game's, in its order", () =>
        {
            c.Equal(8, NeowRelics.Cursed.Count, "eight cursed relics");
            c.Equal(14, NeowRelics.Positive.Count, "fourteen positive relics that are always on the list");
            c.Equal(3, NeowRelics.Coins.Count, "three coins");
            c.Equal(28, NeowRelics.All.Count, "28 in all");
            c.Equal(28, NeowRelics.All.Distinct().Count(), "each once");
            c.Sequence(NeowRelics.Cursed.Order(StringComparer.Ordinal), NeowRelics.Cursed, "the cursed list is in the order of the class (which is alphabetical)");
            c.Sequence(NeowRelics.Positive.Order(StringComparer.Ordinal), NeowRelics.Positive, "so is the positive list");
            c.That(NeowRelics.Twins.Keys.All(NeowRelics.Cursed.Contains) && NeowRelics.Twins.Values.All(NeowRelics.Positive.Contains), "a twin pair is a cursed relic and a positive one");
            c.Equal(4, NeowRelics.Twins.Count, "four pairs");
            c.That(NeowRelics.All.All(relic => !GameLists.IsBagRelic(relic)), "none of Neow's relics is in a relic bag");
        });
    }

    // ====================================================================================
    // the list
    // ====================================================================================

    /// <summary>The event list of seed 1BUSLN0FY5's first act, as the game saved it (golden-saves/run-164514-abe3bfe862ef.save, acts[0].rooms.event_ids).</summary>
    private static readonly string[] RealList =
    [
        "TheFutureOfPotions", "ThisOrThat", "SapphireSeed", "UnrestSite", "RanwidTheElder", "WarHistorianRepy", "SelfHelpBook", "WoodCarvings",
        "MorphicGrove", "StoneOfAllTime", "SunkenStatue", "RelicTrader", "Symbiote", "AromaOfChaos", "JungleMazeAdventure", "LuminousChoir",
        "TabletOfTruth", "PotionCourier", "SlipperyBridge", "DollRoom", "FakeMerchant", "DenseVegetation", "WelcomeToWongos", "TheLegendsWereTrue",
        "WhisperingHollow", "CrystalSphere", "ByrdonisNest", "TeaMaster", "Wellspring", "BrainLeech", "RoomFullOfCheese",
    ];

    private static readonly Func<string, bool> AllBuilt = static _ => true;

    private static void WhenAnEventIsAllowed(Check c)
    {
        bool Allowed(string name, RunSetup? setup = null, int floor = 5, int act = 0)
        {
            var run = Listed(setup is null ? null : new RunSetup
            {
                Seed = RealSeed, Hp = setup.Hp, MaxHp = setup.MaxHp, Gold = setup.Gold, Deck = setup.Deck, ExtraCards = setup.ExtraCards,
                ExtraRelics = setup.ExtraRelics, Potions = setup.Potions,
            }, floor);
            run.State.ActIndex = act;
            return EventList.IsAllowed(run, name);
        }

        c.Group("the events with no condition of their own are always allowed", () =>
        {
            foreach (var name in new[] { "AromaOfChaos", "JungleMazeAdventure", "SapphireSeed", "SunkenStatue", "TabletOfTruth", "Wellspring", "SelfHelpBook", "ThisOrThat", "DenseVegetation" })
                c.That(Allowed(name, new RunSetup { Hp = 1, Gold = 0, Deck = [] }), name);
        });

        c.Group("gold", () =>
        {
            c.That(!Allowed("LuminousChoir", new RunSetup { Gold = 148 }) && Allowed("LuminousChoir", new RunSetup { Gold = 149 }), "Luminous Choir: 149 gold (and a relic left in the bag)");
            c.That(!Allowed("MorphicGrove", new RunSetup { Gold = 99 }) && Allowed("MorphicGrove", new RunSetup { Gold = 100 }), "Morphic Grove: 100 gold (and two cards to transform)");
            c.That(!Allowed("WhisperingHollow", new RunSetup { Gold = 43 }) && Allowed("WhisperingHollow", new RunSetup { Gold = 44 }), "Whispering Hollow: 44 gold");
            c.That(!Allowed("TeaMaster", new RunSetup { Gold = 149 }) && Allowed("TeaMaster", new RunSetup { Gold = 150 }), "Tea Master: 150 gold");
        });

        c.Group("HP", () =>
        {
            // At or below 70% of max HP: 56 of 80 exactly; 55.3 of 79.
            c.That(Allowed("UnrestSite", new RunSetup { Hp = 56 }) && !Allowed("UnrestSite", new RunSetup { Hp = 57 }), "Unrest Site: 56 of 80 is allowed, 57 is not");
            c.That(Allowed("UnrestSite", new RunSetup { Hp = 55, MaxHp = 79 }) && !Allowed("UnrestSite", new RunSetup { Hp = 56, MaxHp = 79 }), "Unrest Site: 55 of 79 is allowed, 56 is not");
            c.That(!Allowed("TheLegendsWereTrue", new RunSetup { Hp = 9 }) && Allowed("TheLegendsWereTrue", new RunSetup { Hp = 10 }), "The Legends Were True: 10 HP");
        });

        c.Group("the deck", () =>
        {
            var eternal = Curse("Greed", "Greed", CardKeywords.Eternal);
            var eternalBasic = new CardDef("Heirloom", "Heirloom", CardType.Skill, CardRarity.Basic, 1, TargetType.None) { Keywords = CardKeywords.Eternal, Text = _ => "A card of the checks." };
            var egg = new CardDef("ByrdonisEgg", "Byrdonis Egg", CardType.Quest, CardRarity.Quest, CardDef.NoCost, TargetType.None) { Keywords = CardKeywords.Unplayable, MaxUpgradeLevel = 0, Text = _ => "A card of the checks." };

            c.That(!Allowed("MorphicGrove", new RunSetup { Gold = 100, Deck = [new("Bash")] }), "Morphic Grove: one card is not enough");
            c.That(!Allowed("MorphicGrove", new RunSetup { Gold = 100, Deck = [new("Bash")], ExtraCards = [(eternal, false)] }), "Morphic Grove: an Eternal card cannot be transformed");
            c.That(Allowed("MorphicGrove", new RunSetup { Gold = 100, Deck = [new("Bash"), new("Anger")], ExtraCards = [(eternal, false)] }), "Morphic Grove: two that can");

            c.That(Allowed("WoodCarvings"), "Wood Carvings: the starter deck has Basic cards");
            c.That(!Allowed("WoodCarvings", new RunSetup { Deck = [new("Anger"), new("Inflame")] }), "Wood Carvings: not without a Basic card");
            c.That(!Allowed("WoodCarvings", new RunSetup { Deck = [new("Anger")], ExtraCards = [(eternalBasic, false)] }), "Wood Carvings: an Eternal Basic card cannot be removed");

            c.That(!Allowed("TheLegendsWereTrue", new RunSetup { Deck = [] }), "The Legends Were True: not with an empty deck");

            c.That(Allowed("ByrdonisNest"), "Byrdonis Nest: allowed without a pet");
            c.That(!Allowed("ByrdonisNest", new RunSetup { ExtraCards = [(egg, false)] }), "Byrdonis Nest: not with the egg in the deck");
            c.That(!Allowed("ByrdonisNest", new RunSetup { ExtraRelics = [static () => new Byrdpip()] }), "Byrdonis Nest: not with a relic that brings a pet");
            c.Sequence(["Byrdpip", "PaelsLegion"], EventList.PetRelics.Order(StringComparer.Ordinal), "the two relics that bring a pet");
        });

        c.Group("potions", () =>
        {
            c.That(!Allowed("TheFutureOfPotions", new RunSetup { Potions = ["BloodPotion"] }), "The Future of Potions: one potion is not enough");
            c.That(Allowed("TheFutureOfPotions", new RunSetup { Potions = ["BloodPotion", null, "FirePotion"] }), "The Future of Potions: two");
        });

        c.Group("Slippery Bridge asks for a total floor above 6 before its own room is counted: floor 8 is the first", () =>
        {
            c.That(!Allowed("SlipperyBridge", floor: 7), "a room on floor 7: the game counts 6 floors, which is not above 6");
            c.That(Allowed("SlipperyBridge", floor: 8), "a room on floor 8: 7 floors are counted");
            c.That(!Allowed("SlipperyBridge", new RunSetup { Deck = [], ExtraCards = [(Curse("Greed", "Greed", CardKeywords.Eternal), false)] }, floor: 12), "and it needs a card that can be removed");
            var run = Listed(floor: 8);
            c.Equal(7, EventList.FloorsBehind(run), "a run without a map: the floors behind are the total floor less the room's own");
        });

        c.Group("the act", () =>
        {
            foreach (var name in new[] { "BrainLeech", "RoomFullOfCheese" })
                c.That(Allowed(name) && Allowed(name, act: 1) && !Allowed(name, act: 2), $"{name}: the first two acts");
            c.That(Allowed("TeaMaster", new RunSetup { Gold = 150 }, act: 1) && !Allowed("TeaMaster", new RunSetup { Gold = 150 }, act: 2), "Tea Master: the first two acts");
            c.That(Allowed("TheLegendsWereTrue") && !Allowed("TheLegendsWereTrue", act: 1), "The Legends Were True: the first act only");

            // The ten shared events that a first act never holds, asked with all the gold and potions they could want.
            var rich = new RunSetup { Gold = 999, Potions = ["BloodPotion", "FirePotion", "BlockPotion"] };
            string[] never = ["CrystalSphere", "PotionCourier", "Symbiote", "RanwidTheElder", "RelicTrader", "FakeMerchant", "DollRoom", "StoneOfAllTime", "WelcomeToWongos", "WarHistorianRepy"];
            foreach (var name in never) c.That(!Allowed(name, rich), $"{name} is not allowed in a first act");
            c.That(Allowed("PotionCourier", act: 1) && Allowed("Symbiote", act: 2), "Potion Courier and Symbiote: any later act");
            c.That(Allowed("CrystalSphere", rich, act: 1) && !Allowed("CrystalSphere", new RunSetup { Gold = 99 }, act: 1), "Crystal Sphere: a later act and 100 gold");
            c.That(Allowed("DollRoom", act: 1) && !Allowed("DollRoom", act: 2), "Doll Room: the second act only");
            c.That(Allowed("StoneOfAllTime", rich, act: 1) && !Allowed("StoneOfAllTime", act: 1), "Stone of All Time: the second act and a potion");
            c.That(Allowed("WelcomeToWongos", rich, act: 1) && !Allowed("WelcomeToWongos", new RunSetup { Gold = 99 }, act: 1), "Welcome to Wongo's: the second act and 100 gold");
            c.That(!Allowed("WarHistorianRepy", rich, act: 1) && !Allowed("WarHistorianRepy", rich, act: 2), "War Historian Repy: never");
        });

        c.Group("every event of an Overgrowth act's list has a rule, and nothing else has", () =>
        {
            var overgrowth = GameLists.Act("Overgrowth").Events;
            c.Sequence(overgrowth.Order(StringComparer.Ordinal), EventList.Overgrowth.Order(StringComparer.Ordinal), "the thirteen events of the act are the game's list");
            c.Equal(13, EventList.Overgrowth.Count, "thirteen");
            c.Equal(18, GameLists.SharedEvents.Count, "eighteen shared events");
            c.That(EventList.SharedInAFirstAct.All(GameLists.SharedEvents.Contains), "the eight are shared events");
            c.Equal(21, EventList.InAnOvergrowthAct.Count, "21 in all");
            c.Sequence(overgrowth.Concat(GameLists.SharedEvents).Order(StringComparer.Ordinal), RealList.Order(StringComparer.Ordinal), "the saved list of the real run is the act's 13 and the 18 shared ones");

            var run = Listed();
            foreach (var name in RealList) EventList.IsAllowed(run, name);
            c.That(true, "none of the 31 is refused");
            // What can be allowed in a first act, of the shared ones, are exactly the eight.
            var rich = Listed(new RunSetup { Seed = RealSeed, Gold = 999, Hp = 40, Potions = ["BloodPotion", "FirePotion"] }, floor: 9);
            c.Sequence(EventList.InAnOvergrowthAct.Order(StringComparer.Ordinal), RealList.Where(name => EventList.IsAllowed(rich, name)).Order(StringComparer.Ordinal),
                "with gold, potions, wounds and floors enough, exactly the 21 are allowed");

            c.That(Thrown<NotSupportedException>(() => EventList.IsAllowed(run, "TrashHeap")).Contains("TrashHeap", StringComparison.Ordinal), "an event of another act is refused by name");
            run.State.ActIndex = 1;
            c.That(Thrown<NotSupportedException>(() => EventList.IsAllowed(run, "RelicTrader")).Contains("after the first act", StringComparison.Ordinal), "Relic Trader's own rule for a later act is not built");
        });
    }

    /// <summary>A relic of the checks under the class name of the game's pet relic.</summary>
    private sealed class Byrdpip : Relic
    {
        public override string Name => "Byrdpip";
        public override string Describe() => "A relic of the checks.";
    }

    private static void TheWalk(Check c)
    {
        c.Group("the list of the real run is the generated one", () =>
        {
            var run = Listed();
            c.Sequence(RealList, World(run).Rooms[0].Events, "the 31 events of seed 1BUSLN0FY5's first act, in the saved order");
            c.Equal(0, World(run).Rooms[0].EventsVisited, "the counter starts at 0; Neow's room takes it to 1");
        });

        c.Group("the walk through the real list, room after room, with the starter deck at full HP and 99 gold", () =>
        {
            var run = Listed();
            var rooms = World(run).Rooms[0];
            rooms.MarkEventVisited(); // Neow's room

            // Entry 1, This or That, is always allowed: the real run met it as its first event.
            var first = EventList.Peek(run, AllBuilt);
            c.Equal("ThisOrThat/ThisOrThat/1", $"{first.Event}/{first.GameEvent}/{first.Counter}", "the first Event room holds entry 1");
            c.That(first.IsTheGames && first.SteppedOver.Count == 0 && !first.Repeats, "the game's own, nothing stepped over");
            c.Equal(1, rooms.EventsVisited, "a look moves nothing");
            c.Equal("ThisOrThat", EventList.Pull(run, AllBuilt), "taking it gives the same");
            c.Equal(1, rooms.EventsVisited, "the walk found it where the counter stood");
            c.Sequence(["ThisOrThat"], run.State.Part<EventsState>().Visited, "and it is met now, as the later save has it (events_seen)");
            rooms.MarkEventVisited();
            c.Equal(2, rooms.EventsVisited, "the room itself counts once more: 2, as the later save has it");

            // Entry 2, Sapphire Seed: always allowed.
            c.Equal("SapphireSeed", EventList.Pull(run, AllBuilt), "the second Event room");
            rooms.MarkEventVisited();

            // Entry 3, Unrest Site, needs HP at or below 70%; entries 4 and 5 are never in a first act; entry 6 is Self Help Book.
            var third = EventList.Peek(run, AllBuilt);
            c.Equal("SelfHelpBook", third.Event, "the third Event room at full HP");
            c.Sequence(["UnrestSite", "RanwidTheElder", "WarHistorianRepy"], third.SteppedOver, "three entries are stepped over");
            c.Equal(6, third.Counter, "the counter would stand on entry 6");
            c.Equal("SelfHelpBook", EventList.Pull(run, AllBuilt), "taken");
            c.Equal(6, rooms.EventsVisited, "the counter moved past them for good");
            rooms.MarkEventVisited();

            // Entry 7, Wood Carvings: the starter deck has Basic cards.
            c.Equal("WoodCarvings", EventList.Pull(run, AllBuilt), "the fourth Event room");
            rooms.MarkEventVisited();

            // Entry 8, Morphic Grove, needs 100 gold; entry 9 is of the second act; entry 10 is Sunken Statue.
            var fifth = EventList.Peek(run, AllBuilt);
            c.Equal("SunkenStatue", fifth.Event, "the fifth Event room with 99 gold");
            c.Sequence(["MorphicGrove", "StoneOfAllTime"], fifth.SteppedOver, "Morphic Grove is stepped over for one gold piece");
            run.State.Gold = 100;
            c.Equal("MorphicGrove", EventList.Peek(run, AllBuilt).Event, "with 100 gold it is Morphic Grove");
            run.State.Gold = 99;
            c.Equal("SunkenStatue", EventList.Pull(run, AllBuilt), "taken with 99");
            rooms.MarkEventVisited();
            c.Equal(11, rooms.EventsVisited, "the counter after five Event rooms");
            c.Sequence(["ThisOrThat", "SapphireSeed", "SelfHelpBook", "WoodCarvings", "SunkenStatue"], run.State.Part<EventsState>().Visited, "the events met");

            // Entries 11 and 12 are not of a first act; 13 is Aroma of Chaos.
            c.Equal("AromaOfChaos", EventList.Pull(run, AllBuilt), "the sixth");
            c.Equal(13, rooms.EventsVisited, "on entry 13");

            var copy = run.Clone();
            // Aroma of Chaos is met and its room not counted yet, so the next walk steps over it.
            c.Equal("JungleMazeAdventure", EventList.Pull(copy, AllBuilt), "a copy of the run goes on by itself: its next pull gives entry 14");
            c.Equal((14, 13), (World(copy).Rooms[0].EventsVisited, rooms.EventsVisited), "the copy's counter moved, the original's did not");
            c.Equal((7, 6), (copy.State.Part<EventsState>().Visited.Count, run.State.Part<EventsState>().Visited.Count), "the copy has met seven events, the original six");
        });

        c.Group("what the player holds decides at the moment of the walk", () =>
        {
            var hurt = Listed(new RunSetup { Seed = RealSeed, Hp = 56 });
            World(hurt).Rooms[0].EventsVisited = 3;
            c.Equal("UnrestSite", EventList.Pull(hurt, AllBuilt), "at 56 of 80 HP entry 3 is Unrest Site");

            // Entry 15 is Luminous Choir (149 gold and a relic in the bag), entry 16 Tablet of Truth.
            var rich = Listed(new RunSetup { Seed = RealSeed, Gold = 149 });
            World(rich).Rooms[0].EventsVisited = 15;
            var before = rich.Dump();
            c.Equal("LuminousChoir", EventList.Peek(rich, AllBuilt).Event, "with 149 gold entry 15 is Luminous Choir");
            c.Equal(before, rich.Dump(), "a look leaves the run as it was, the relic bag included");
            var poor = Listed(new RunSetup { Seed = RealSeed, Gold = 148 });
            World(poor).Rooms[0].EventsVisited = 15;
            c.Equal("TabletOfTruth", EventList.Pull(poor, AllBuilt), "with 148 it is Tablet of Truth");

            // The same gold with an empty relic bag: Luminous Choir has no relic to give.
            var bare = Listed(new RunSetup { Seed = RealSeed, Gold = 149 });
            World(bare).Rooms[0].EventsVisited = 15;
            var bag = World(bare).PlayerBag;
            foreach (var rarity in bag.Rarities.ToList())
                foreach (var relic in bag.Relics(rarity).ToList()) bag.Remove(relic);
            c.Equal("TabletOfTruth", EventList.Peek(bare, AllBuilt).Event, "with 149 gold and no relic left in the player's bag it is Tablet of Truth");

            // A relic the player holds that the bag has not caught up with (one obtained in a fight): a look
            // reckons without it and still changes nothing; the walk itself brings the bag up to date.
            var holding = Listed(new RunSetup { Seed = RealSeed, Gold = 149, ExtraRelics = [static () => new Vajra()] });
            World(holding).Rooms[0].EventsVisited = 15;
            c.That(World(holding).PlayerBag.Contains("Vajra"), "(the bag still lists the held relic)");
            before = holding.Dump();
            c.Equal("LuminousChoir", EventList.Peek(holding, AllBuilt).Event, "a look");
            c.Equal(before, holding.Dump(), "leaves the bag as it was");
            c.Equal("LuminousChoir", EventList.Pull(holding, AllBuilt), "the walk");
            c.That(!World(holding).PlayerBag.Contains("Vajra"), "takes the held relic out of the bag, as every pull does first");
        });

        c.Group("the list wraps at its end", () =>
        {
            var run = Listed();
            var rooms = World(run).Rooms[0];
            rooms.EventsVisited = 30;
            run.State.Part<EventsState>().MarkVisited("RoomFullOfCheese");
            // Entry 30 was met; entry 0, The Future of Potions, needs two potions; entry 1 is This or That.
            var pull = EventList.Peek(run, AllBuilt);
            c.Equal("ThisOrThat", pull.Event, "past the end the walk goes on at entry 0, then 1");
            c.Sequence(["RoomFullOfCheese", "TheFutureOfPotions"], pull.SteppedOver, "the met event and the one that is not allowed are stepped over");
            c.Equal(32, pull.Counter, "the counter runs on: 32 stands on entry 1 of 31");

            var potions = Listed(new RunSetup { Seed = RealSeed, Potions = ["BloodPotion", "FirePotion"] });
            World(potions).Rooms[0].EventsVisited = 31;
            c.Equal("TheFutureOfPotions", EventList.Pull(potions, AllBuilt), "with two potions entry 0 is taken when the walk wraps to it");
            c.Equal(31, World(potions).Rooms[0].EventsVisited, "counter 31 is entry 0");
        });

        c.Group("when a whole lap finds nothing the game takes the entry it stood on, allowed or not", () =>
        {
            var run = Listed();
            var rooms = World(run).Rooms[0];
            rooms.Events.Clear();
            rooms.Events.AddRange(["WarHistorianRepy", "ThisOrThat", "PotionCourier"]);
            rooms.EventsVisited = 1;
            run.State.Part<EventsState>().MarkVisited("ThisOrThat");
            var pull = EventList.Peek(run, AllBuilt);
            c.Equal("ThisOrThat", pull.Event, "the entry the walk started on, though it was met before");
            c.That(pull.Repeats, "a repetition");
            c.Sequence(["ThisOrThat", "PotionCourier", "WarHistorianRepy"], pull.SteppedOver, "every entry was stepped over once");
            c.Equal(4, pull.Counter, "the counter went once round: 1 + 3");
            c.Equal("ThisOrThat", EventList.Pull(run, AllBuilt), "taken");
            c.Sequence(["ThisOrThat"], run.State.Part<EventsState>().Visited, "met once, as before");

            c.That(!EventList.Peek(run, static _ => false).Found, "a repeated event that is not built is no event the arena can open");
            c.That(EventList.Peek(run, static _ => false).Reason!.StartsWith("no event the arena holds", StringComparison.Ordinal), "and the look says so");

            // The same when the entry is not allowed at all.
            rooms.EventsVisited = 0;
            var notAllowed = EventList.Peek(run, AllBuilt);
            c.Equal("WarHistorianRepy", notAllowed.Event, "from entry 0: War Historian Repy, which is never allowed");
            c.That(notAllowed.Repeats, "the game takes it all the same");
        });
    }

    private static void TheWalkWithEventsLeftOut(Check c)
    {
        static bool Two(string name) => name is "SelfHelpBook" or "SunkenStatue";

        c.Group("an event that is not built is passed over: the walk goes on for one that is allowed, unmet and built", () =>
        {
            var run = Listed(floor: 4);
            var rooms = World(run).Rooms[0];
            rooms.MarkEventVisited();
            // From entry 1: This or That and Sapphire Seed are allowed and not built; Unrest Site is not allowed
            // at full HP, entries 4 and 5 never; entry 6, Self Help Book, is built.
            var pull = EventList.Peek(run, Two);
            c.Equal("SelfHelpBook/ThisOrThat/6", $"{pull.Event}/{pull.GameEvent}/{pull.Counter}", "Self Help Book is opened; the game would open This or That");
            c.Sequence(["ThisOrThat", "SapphireSeed"], pull.PassedOver, "two events were passed over");
            c.Sequence([], pull.SteppedOver, "the game's own walk stepped over nothing");
            c.That(pull.Found && !pull.IsTheGames, "found, but not the game's");
            c.That(EventList.CanHold(run, Two), "so the room can be an Event room");
            c.Equal(1, rooms.EventsVisited, "the look moved nothing");
            c.That(run.State.FindPart<LeftOutLog>() is null, "and wrote nothing down");

            c.Equal("SelfHelpBook", EventList.Pull(run, Two), "taken");
            c.Equal(6, rooms.EventsVisited, "the counter stands on it");
            c.Sequence(["SelfHelpBook"], run.State.Part<EventsState>().Visited, "only the event that was opened is met");
            var log = run.State.Part<LeftOutLog>();
            c.Sequence(["4:event:ThisOrThat:the event of an Event room", "4:event:SapphireSeed:the event of an Event room"],
                log.Entries.Select(e => $"{e.Floor}:{e.Kind}:{e.ClassName}:{e.What}"), "both are in the run's log, with the floor");
            c.Equal(4, log.FirstFloor, "from floor 4 on the run is not the game's own");

            // Next room: Wood Carvings (7) is passed over, Morphic Grove (8) is not allowed, 9 never, Sunken Statue (10) is built.
            rooms.MarkEventVisited();
            var next = EventList.Peek(run, Two);
            c.Equal("SunkenStatue/WoodCarvings/10", $"{next.Event}/{next.GameEvent}/{next.Counter}", "the next room");
            c.Sequence(["WoodCarvings"], next.PassedOver, "Wood Carvings is passed over; Morphic Grove was not allowed anyway");
        });

        c.Group("when no built event is allowed and unmet the room cannot be an Event room", () =>
        {
            var run = Listed(floor: 4);
            World(run).Rooms[0].MarkEventVisited();
            var pull = EventList.Peek(run, static _ => false);
            c.That(!pull.Found && pull.Event is null, "nothing to open");
            c.Equal("ThisOrThat", pull.GameEvent, "the game's event is still known");
            c.Equal("no event the arena holds is allowed and unmet for the Event room on floor 4 (the game takes ThisOrThat)", pull.Reason, "the reason");
            c.That(!EventList.CanHold(run, static _ => false), "CanHold says no");
            c.That(Thrown<RunUnrepresentableException>(() => EventList.Pull(run, static _ => false)).StartsWith("no event the arena holds", StringComparison.Ordinal), "and a pull all the same ends the run as unrepresentable");

            // The two built events were both met: again nothing.
            var met = Listed(floor: 4);
            World(met).Rooms[0].MarkEventVisited();
            met.State.Part<EventsState>().MarkVisited("SelfHelpBook");
            met.State.Part<EventsState>().MarkVisited("SunkenStatue");
            c.That(!EventList.CanHold(met, Two), "built events that were met do not count");
        });

        c.Group("strict mode passes nothing over", () =>
        {
            var run = Listed(floor: 4, strict: true);
            World(run).Rooms[0].MarkEventVisited();
            c.That(EventList.CanHold(run, static _ => false), "an Event room is always possible: the run is the game's own or it ends");
            var pull = EventList.Peek(run, Two);
            c.That(!pull.Found && pull.GameEvent == "ThisOrThat", "the game's event is not built: nothing to open");
            c.That(pull.Reason!.StartsWith("the Event room on floor 4 holds ThisOrThat, which the arena leaves out", StringComparison.Ordinal), $"the reason ({pull.Reason})");
            var thrown = Thrown<RunUnrepresentableException>(() => EventList.Pull(run, Two));
            c.That(thrown.Contains("ThisOrThat", StringComparison.Ordinal) && thrown.Contains("floor 4", StringComparison.Ordinal), $"a pull ends the run as unrepresentable, naming the event and the floor ({thrown})");
            var builtRun = Listed(floor: 4, strict: true);
            World(builtRun).Rooms[0].MarkEventVisited();
            c.Equal("ThisOrThat", EventList.Pull(builtRun, AllBuilt), "with the game's event built, strict mode takes it");
        });
    }

    // ====================================================================================
    // the room and its screen
    // ====================================================================================

    private static void OwnStream(Check c)
    {
        c.Group("an event rolls on its own stream: the same numbers whenever it is met", () =>
        {
            // The first number of This or That's stream for this seed, as a whole number from 41 to 68, read off the stream itself.
            var first = new RunStreams(RealSeed).Event("ThisOrThat").NextInt(41, 69);
            c.Equal(61, first, "the first number of the event's stream");

            var run = InEvent(static () => new Chests(), floor: 4);
            c.That(State(run).Contains("Gain 61 Gold.", StringComparison.Ordinal), "the event's gold is that number");
            c.Equal(1, Ev(run).Rng.Counter, "one number was taken from the event's stream");

            var later = new Run(new RunSetup { Seed = RealSeed }, new OneRoom { Event = static () => new Chests(), Floor = 12, Noise = 9 });
            c.That(State(later).Contains("Gain 61 Gold.", StringComparison.Ordinal), "the same on another floor, after other streams of the run have moved");
            c.Equal(9, World(later).Streams.Counter(RunStream.Rewards), "(the rewards stream did move)");

            var keyed = new Run(new RunSetup { Seed = RealSeed }, new OneRoom { Event = static () => new Chests(), Floor = 4, Mode = StreamMode.Keyed });
            c.That(State(keyed).Contains("Gain 61 Gold.", StringComparison.Ordinal), "and with keyed streams: the game ties an event's rolls to the seed already");

            var other = new Run(new RunSetup { Seed = "KMEHXZW0R5" }, new OneRoom { Event = static () => new Chests(), Floor = 4 });
            var gold = new RunStreams("KMEHXZW0R5").Event("ThisOrThat").NextInt(41, 69);
            c.That(State(other).Contains($"Gain {gold} Gold.", StringComparison.Ordinal) && gold is >= 41 and <= 68, $"another seed has its own number ({gold})");
        });
    }

    private static void ThePage(Check c)
    {
        c.Group("an event page as the mod shows it, and the finished page with the room's leave option", () =>
        {
            var run = InEvent(static () => new Chests(), floor: 4);
            var s = run.Snapshot();
            c.Equal("event", s.Screen, "the screen");
            c.That(s.AwaitingDecision && s.Waiting is null, "a decision");
            c.Equal($$"""{"run":{"act":1,"floor":4,"ascension":0},"event":"This or That","text":"Two chests stand here.","options":[{"n":0,"title":"Plain","text":"Lose 6 HP. Gain 61 Gold."},{"n":1,"title":"Ornate","text":"Add Inflame to your Deck.","cards":[{{CardJson("Inflame")}}]}],{{Player()}},{{NoPotions}}}""",
                Text(s.State), "the state: run, event, text, options, player without potions, potions");
            c.Equal("option:0=Choose Plain | option:1=Choose Ornate", Labels(s), "one action per option; no potion is held");
            c.Equal(RoomKind.Event, run.Room, "the hooks were told an Event room");

            Do(run, "option:0");
            // 6 HP for 61 gold.
            c.Equal((74, 160), (run.Hp, run.Gold), "Plain: 80 - 6 HP, 99 + 61 gold");
            s = run.Snapshot();
            c.Equal($$"""{"run":{"act":1,"floor":4,"ascension":0},"event":"This or That","text":"You take the gold.","finished":true,"options":[{"n":0,"title":"Proceed","leave":true}],{{Player(hp: 74, gold: 160)}},{{NoPotions}}}""",
                Text(s.State), "the finished page: the room's own option, under the game's title");
            c.Equal("option:0=Leave", Labels(s), "labelled Leave");
            c.That(Ev(run).IsFinished && Ev(run).Options.Count == 0, "the event itself has no options left");

            Do(run, "option:0");
            c.Equal("after", run.Snapshot().Screen, "leaving hands the run to its director");
            c.Equal(1, ((OneRoom)run.Director).Left, "who was told once that the room is finished");
            c.That(EventScreen.Current(run) is null, "and the run is in no event");
            c.Sequence(["after"], run.Screens.Select(screen => screen.Name), "the event's screen is gone: only the director's is open");
            c.Sequence(["option:0", "option:0"], run.Actions, "two actions in the save");
        });

        c.Group("the other option adds its card", () =>
        {
            var run = InEvent(static () => new Chests(), floor: 4);
            Do(run, "option:1");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash Inflame", DeckNames(run), "Inflame is the deck's last card");
            c.That(State(run).Contains(Q("'text':'You take the card.','finished':true"), StringComparison.Ordinal), "finished");
        });

        c.Group("refusals, in the mod's words", () =>
        {
            var run = InEvent(static () => new Chests(), floor: 4);
            var before = run.Dump();
            c.Equal("There is no event option 2.", Refusal(run, "option:2"), "a number past the page");
            c.Equal("There is no event option -1.", Refusal(run, "option:-1"), "a negative number");
            c.Equal("Unknown event action 'option:x'.", Refusal(run, "option:x"), "not a number");
            c.Equal("Unknown event action 'proceed'.", Refusal(run, "proceed"), "leaving is never 'proceed' in an event");
            c.Equal("Unknown event action 'map:0'.", Refusal(run, "map:0"), "another screen's id");
            c.Equal("'potion:0' is not available now.", Refusal(run, "potion:0"), "a potion id with an empty slot");
            c.Equal("'discard_potion:7' is not available now.", Refusal(run, "discard_potion:7"), "a slot that is not there");
            c.Equal(before, run.Dump(), "nothing changed");
            c.Equal(0, run.Actions.Count, "and nothing is in the save");
            Do(run, "option:0");
            c.Equal("There is no event option 1.", Refusal(run, "option:1"), "the finished page has one option");
        });

        c.Group("an option without a title is named by its text, cut to 60 characters, or by its number", () =>
        {
            var run = InEvent(static () => new Untitled());
            var options = run.Snapshot().State["options"]!.AsArray();
            // 70 characters: the first 57 and three dots.
            c.Equal("A very long line of text that goes on and on, well past t...", options[0]!["title"]!.GetValue<string>(), "cut to 57 characters and three dots");
            c.Equal(60, options[0]!["title"]!.GetValue<string>().Length, "60 in all");
            c.That(options[0]!["text"] is not null, "the whole text is still there");
            c.Equal("Short text", options[1]!["title"]!.GetValue<string>(), "a short text is the name as it is");
            c.That(options[1]!["text"] is null, "and is then not repeated as text");
            c.Equal("option 2", options[2]!["title"]!.GetValue<string>(), "neither title nor text: the number");
            c.Equal("option:0=Choose A very long line of text that goes on and on, well past t... | option:1=Choose Short text | option:2=Choose option 2", Labels(run.Snapshot()), "the labels use the names");
            c.That(run.Snapshot().State["text"] is null, "a page without text has no text key");
        });
    }

    /// <summary>One option that costs 10 HP and says so.</summary>
    private sealed class Pit2 : RunEvent
    {
        public override string Title => "The Ledge";
        protected override string InitialDescription(Run run) => "A ledge.";
        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) => [Option("JUMP", "Jump", "Lose 10 HP.", Jump).ThatDoesDamage(10)];

        private void Jump(Run run)
        {
            run.Damage(10);
            if (run.IsOver) return;
            SetEventFinished("Down.");
        }
    }

    /// <summary>Options without titles.</summary>
    private sealed class Untitled : RunEvent
    {
        public override string Title => "Untitled";
        protected override string InitialDescription(Run run) => "";

        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) =>
        [
            Option("A", "", "A very long line of text that goes on and on, well past the sixtieth.", Done),
            Option("B", "", "Short text", Done),
            Option("C", "", "", Done),
        ];

        private void Done(Run run) => SetEventFinished("");
    }

    private static void DeadlyOptionsAndPotions(Check c)
    {
        c.Group("an option that would kill is listed with its note and not offered; potions ride along", () =>
        {
            var run = InEvent(static () => new Chests(), new RunSetup { Seed = RealSeed, Hp = 6, Potions = ["BloodPotion", null, "FirePotion"] }, floor: 4);
            var s = run.Snapshot();
            c.Equal(Q("{'n':0,'title':'Plain','text':'Lose 6 HP. Gain 61 Gold.','will_kill_player':true,'note':'would kill you; not offered'}"), Text(s.State["options"]![0]), "at 6 HP an option that costs 6 would kill");
            c.Equal("option:1=Choose Ornate | potion:0=Use Blood Potion | discard_potion:0=Discard Blood Potion | discard_potion:2=Discard Fire Potion", Labels(s),
                "the other option, then the potion ids: use for the one that works outside a fight, discard for both");
            c.Equal(Text(RunPotions.Describe(run)), Text(s.State["potions"]), "the potions block is the one every screen outside a fight shows");
            c.That(Text(s.State["potions"]).Contains(Q("'slot':0,'name':'Blood Potion'"), StringComparison.Ordinal) && Text(s.State["potions"]).Contains(Q("'usable':true"), StringComparison.Ordinal), "with the Blood Potion usable");
            c.That(s.State["player"]!["potions"] is null, "and the player block has no potions");
            c.Equal("Plain would kill you.", Refusal(run, "option:0"), "the refusal");

            // The Blood Potion heals 20% of 80: 16.
            Do(run, "potion:0");
            c.Equal(22, run.Hp, "6 + 16 HP");
            s = run.Snapshot();
            c.Equal(Q("{'n':0,'title':'Plain','text':'Lose 6 HP. Gain 61 Gold.'}"), Text(s.State["options"]![0]), "the option is asked anew: it would not kill any more");
            c.Equal("option:0=Choose Plain | option:1=Choose Ornate | discard_potion:2=Discard Fire Potion", Labels(s), "and is offered");
            Do(run, "discard_potion:2", "option:0");
            c.Equal((16, 160), (run.Hp, run.Gold), "22 - 6 HP, 99 + 61 gold");
            c.Sequence(["potion:0", "discard_potion:2", "option:0"], run.Actions, "the potion actions are in the save");
        });

        c.Group("an option that costs max HP would kill at or below its cost", () =>
        {
            var at = InEvent(static () => new Site(), new RunSetup { Seed = RealSeed, Hp = 8, MaxHp = 8 });
            c.Equal(Q("{'n':0,'title':'Kill','text':'Lose 8 Max HP.','will_kill_player':true,'note':'would kill you; not offered'}"), Text(at.Snapshot().State["options"]![0]), "8 max HP against a cost of 8");
            var above = InEvent(static () => new Site(), new RunSetup { Seed = RealSeed, Hp = 9, MaxHp = 9 });
            c.Equal("option:0=Choose Kill | option:1=Choose Look | option:2=Choose Rest", Labels(above.Snapshot()), "9 max HP: offered");
            Do(above, "option:0");
            c.Equal((1, 1), (above.Hp, above.MaxHp), "and it leaves 1 max HP");
        });

        c.Group("an option that was chosen is switched off, as the game's button is", () =>
        {
            var run = InEvent(static () => new Site());
            Do(run, "option:1");
            c.Equal(104, run.Gold, "Look paid its 5 gold and left the page as it was");
            var s = run.Snapshot();
            c.Equal(Q("{'n':1,'title':'Look','text':'Gain 5 Gold.'}"), Text(s.State["options"]![1]), "it is still listed, with no flag");
            c.Equal("option:0=Choose Kill | option:2=Choose Rest", Labels(s), "and not offered again");
            c.Equal("Look is not clickable right now.", Refusal(run, "option:1"), "the refusal");
            c.Equal(104, run.Gold, "nothing was paid twice");
        });

        c.Group("a page on which nothing can be chosen would show as the mod's waiting snapshot, with no potion action either", () =>
        {
            // Such a page cannot come about by play (see the guards); here the player's HP is set by hand under an open page.
            var run = InEvent(static () => new Pit2(), new RunSetup { Seed = RealSeed, Hp = 20, Potions = ["BloodPotion"] });
            c.Equal("option:0=Choose Jump | potion:0=Use Blood Potion | discard_potion:0=Discard Blood Potion", Labels(run.Snapshot()), "at 20 HP the page is a decision");
            run.State.Hp = 10;
            var s = run.Snapshot();
            c.That(!s.AwaitingDecision && s.Actions.Count == 0, "at 10 HP its one option would kill: no decision, no action");
            c.Equal("no event option the bot can take (all locked, deadly or special); a human has to choose", s.Waiting, "the mod's waiting text");
            c.That(s.State["options"]!.AsArray().Count == 1 && s.State["potions"] is not null, "the state is still whole");
            c.Equal("'potion:0' is not available now.", Refusal(run, "potion:0"), "potion actions ride along with an option; alone they are not offered");
            c.Equal("Jump would kill you.", Refusal(run, "option:0"), "the option's own refusal");
        });

        c.Group("one HP more and the option is offered", () =>
        {
            var run = InEvent(static () => new Chests(), new RunSetup { Seed = RealSeed, Hp = 7 }, floor: 4);
            c.Equal("option:0=Choose Plain | option:1=Choose Ornate", Labels(run.Snapshot()), "at 7 HP");
            Do(run, "option:0");
            c.Equal(1, run.Hp, "7 - 6");
        });
    }

    private static void LockedOptionsAndTips(Check c)
    {
        c.Group("a locked option, a relic in a tooltip, and at most three cards of a tooltip", () =>
        {
            var run = InEvent(static () => new Gate());
            var s = run.Snapshot();
            var expected = Q("{'run':{'act':1,'floor':1,'ascension':0},'event':'The Gate','text':'A gate bars the way.','options':["
                             + "{'n':0,'title':'Pay','text':'Requires 100 Gold.','locked':true},"
                             + "{'n':1,'title':'Cut','text':'Remove a card from your Deck.'},"
                             + "{'n':2,'title':'Loot','text':'Find 25 Gold.'},"
                             + "{'n':3,'title':'Take the Key','text':'Obtain Gate Key.','relic':{'name':'Gate Key','text':'Opens gates.'},'cards':[")
                           + $"{CardJson("Bash+")},{CardJson("Bash")},{CardJson("Anger")}]}}],{Player()},{NoPotions}}}";
            c.Equal(expected, Text(s.State), "Pay is locked with 99 gold; the key's tooltip shows the relic and its first three different cards");
            c.Equal("option:1=Choose Cut | option:2=Choose Loot | option:3=Choose Take the Key", Labels(s), "the locked option is not offered");
            c.Equal("Pay is locked.", Refusal(run, "option:0"), "the refusal");

            Do(run, "option:3");
            c.Sequence(["BurningBlood", "GateKey"], run.State.Relics.Select(relic => relic.ClassName), "the relic is the player's");
            c.That(State(run).Contains(Q("'finished':true"), StringComparison.Ordinal), "finished");
        });

        c.Group("an option that is a relic's own name and text shows the relic's cards and no relic key", () =>
        {
            var run = InEvent(static () => new Shelf());
            c.Equal(Q("[{'n':0,'title':'Gate Key','text':'Opens gates.','cards':[") + $"{CardJson("Bash+")},{CardJson("Bash")},{CardJson("Anger")}]}}]", Text(run.Snapshot().State["options"]), "the option");
            c.Equal("option:0=Choose Gate Key", Labels(run.Snapshot()), "outside an Ancient's room the label says Choose");
        });

        c.Group("with the gold the option is not locked", () =>
        {
            var run = InEvent(static () => new Gate(), new RunSetup { Seed = RealSeed, Gold = 100 });
            c.Equal(Q("{'n':0,'title':'Pay','text':'Pay 100 Gold.'}"), Text(run.Snapshot().State["options"]![0]), "100 gold: Pay is open");
            Do(run, "option:0");
            c.Equal(0, run.Gold, "100 gold paid");
        });

        c.Group("a card of the deck in a tooltip is shown as it is", () =>
        {
            var run = InEvent(static () => new Keeper(), new RunSetup { Seed = RealSeed, Deck = [new("StrikeIronclad"), new("Bash", true)] });
            c.Equal(Q("{'n':0,'title':'Give','text':'Lose Bash+.','relic':") + Text(RunDescribe.Relic(run.State.Relics[0])) + Q(",'cards':[") + CardJson("Bash+") + "]}",
                Text(run.Snapshot().State["options"]![0]), "the upgraded Bash of the deck, and the relic the player holds");
            c.That(Text(run.Snapshot().State["options"]![0]).Contains(Q("'relic':{'name':'Burning Blood'"), StringComparison.Ordinal), "(Burning Blood)");
            Do(run, "option:0");
            c.Equal("StrikeIronclad", DeckNames(run), "and it is the card the option takes");
        });
    }

    private static void Pages(Check c)
    {
        c.Group("pages: an option leads to a new page, whose options are numbered from 0 again", () =>
        {
            var run = InEvent(static () => new Tablet());
            c.Equal(Q("[{'n':0,'title':'Decipher','text':'Lose 3 Max HP.'},{'n':1,'title':'Smash','text':'Heal 20 HP.'}]"), Text(run.Snapshot().State["options"]), "the first page");

            Do(run, "option:0");
            // 3 max HP gone; HP above the new maximum goes with it.
            c.Equal((77, 77), (run.Hp, run.MaxHp), "80 - 3 max HP");
            var s = run.Snapshot();
            c.Equal(Q("{'run':{'act':1,'floor':1,'ascension':0},'event':'Tablet of Truth','text':'You read line 1.','options':[{'n':0,'title':'Decipher','text':'Lose 6 Max HP.'},{'n':1,'title':'Give Up'}],")
                    + $"{Player(hp: 77, maxHp: 77)},{NoPotions}}}", Text(s.State), "the second page: new text, new options; an option without text has no text key");
            c.Equal("option:0=Choose Decipher | option:1=Choose Give Up", Labels(s), "its actions");

            Do(run, "option:0");
            c.Equal((71, 71), (run.Hp, run.MaxHp), "77 - 6");
            c.That(State(run).Contains(Q("'text':'You read line 2.'"), StringComparison.Ordinal) && State(run).Contains("Lose 12 Max HP.", StringComparison.Ordinal), "the third page");
            Do(run, "option:0");
            c.Equal((59, 59), (run.Hp, run.MaxHp), "71 - 12");
            c.Equal("option:0=Leave", Labels(run.Snapshot()), "the third time finishes the event");
            c.That(State(run).Contains(Q("'text':'You have read enough.','finished':true"), StringComparison.Ordinal), "with its last text");
        });

        c.Group("an option that would kill by its own rule is asked on every page", () =>
        {
            var run = InEvent(static () => new Tablet(), new RunSetup { Seed = RealSeed, Hp = 6, MaxHp = 6 });
            c.Equal("option:0=Choose Decipher | option:1=Choose Smash", Labels(run.Snapshot()), "6 max HP against a cost of 3: offered");
            Do(run, "option:0");
            c.Equal((3, 3), (run.Hp, run.MaxHp), "3 max HP left");
            // 3 max HP against a cost of 6.
            c.Equal(Q("[{'n':0,'title':'Decipher','text':'Lose 6 Max HP.','will_kill_player':true,'note':'would kill you; not offered'},{'n':1,'title':'Give Up'}]"), Text(run.Snapshot().State["options"]), "the next cost would kill");
            c.Equal("option:1=Choose Give Up", Labels(run.Snapshot()), "only Give Up is offered");
            c.Equal("Decipher would kill you.", Refusal(run, "option:0"), "the refusal");
            Do(run, "option:1", "option:0");
            c.Equal(1, ((OneRoom)run.Director).Left, "given up and left");
        });

        c.Group("the other first option heals", () =>
        {
            var run = InEvent(static () => new Tablet(), new RunSetup { Seed = RealSeed, Hp = 50 });
            Do(run, "option:1");
            c.Equal(70, run.Hp, "50 + 20");
        });

        c.Group("options made in a loop carry their number", () =>
        {
            var run = InEvent(static () => new Slots());
            c.Equal("option:0=Choose Slot 0 | option:1=Choose Slot 1 | option:2=Choose Slot 2", Labels(run.Snapshot()), "three options from one method");
            Do(run, "option:2");
            c.Equal(129, run.Gold, "slot 2 pays 30");
            c.That(State(run).Contains("You took slot 2.", StringComparison.Ordinal), "and the method knew which");
        });
    }

    private static void ChoicesAndRewards(Check c)
    {
        c.Group("an option that asks for a card: the choice is a question, and what came before it happens once", () =>
        {
            var rolled = new RunStreams(RealSeed).Event("Gate").NextInt(100);
            var run = InEvent(static () => new Gate());
            Do(run, "option:1");
            var s = run.Snapshot();
            c.Equal("card_select", s.Screen, "the card choice shows");
            c.Equal("remove", s.State["kind"]!.GetValue<string>(), "a removal");
            c.That(run.Question is not null && run.Top is EventScreen, "as a question over the event's screen");
            c.Equal(10, run.State.Deck.Count, "nothing is removed yet");

            Do(run, "card:9", "confirm");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad", DeckNames(run), "Bash is removed");
            s = run.Snapshot();
            c.Equal("event", s.Screen, "back at the event");
            c.Equal($"You cut your way through ({rolled}).", s.State["text"]!.GetValue<string>(), "finished, with the number the option rolled before it asked");
            c.Equal(1, Ev(run).Rng.Counter, "the option ran twice (before and after the answer) and its roll was made once");
            c.Sequence(["option:1", "card:9", "confirm"], run.Actions, "the save holds the option and the steps of its question");
        });

        c.Group("an option that opens a rewards screen: the event goes on when the screen has closed", () =>
        {
            var run = InEvent(static () => new Gate());
            Do(run, "option:2");
            var s = run.Snapshot();
            c.Equal("rewards", s.Screen, "the rewards screen is over the event");
            c.Equal(Q("[{'n':0,'kind':'gold','amount':25}]"), Text(s.State["rewards"]), "one row: 25 gold");
            c.That(!Ev(run).IsFinished, "the event is not finished yet");
            Do(run, "reward:0");
            c.Equal(124, run.Gold, "99 + 25");
            s = run.Snapshot();
            c.Equal("event", s.Screen, "the last row closes the rewards screen");
            c.Equal("You climb over with what you found.", s.State["text"]!.GetValue<string>(), "and the event heard of it and finished");
            c.Equal("option:0=Leave", Labels(s), "only leaving is left");

            var skipped = InEvent(static () => new Gate());
            Do(skipped, "option:2", "proceed");
            c.Equal(99, skipped.Gold, "skipping the rewards leaves the gold");
            c.Equal("option:0=Leave", Labels(skipped.Snapshot()), "and finishes the event the same way");
        });
    }

    private static void OrderOnEntering(Check c)
    {
        c.Group("on entering, an event's own methods run in the game's order: the options are made before the text is read", () =>
        {
            var run = InEvent(static () => new Ordered());
            c.Equal("before vars options text started", ((Ordered)Ev(run)).Order, "BeforeEventStarted, CalculateVars, the options, the text, AfterEventStarted");
            c.Equal("before vars options text", run.Snapshot().State["text"]!.GetValue<string>(), "the page's text was read once the options stood");
        });

        c.Group("on entering: the event's start and its first page come before the relics hear of the room", () =>
        {
            var setup = new RunSetup { Seed = RealSeed, ExtraRelics = [static () => new Tollkeeper()] };
            var run = InEvent(static () => new Chests(), setup, floor: 4);
            var relic = run.State.FindRelic<Tollkeeper>()!;
            c.Equal(1, relic.SawDraws, "when the relic heard 'after room entered' the event had made its start roll");
            c.Equal(2, relic.SawOptions, "and its first page stood");
            c.Equal(149, run.Gold, "the relic's 50 gold");

            // Pay needs 100 gold. The page was made with 99, so Pay is locked, though the player has 149 when it shows.
            var gate = InEvent(static () => new Gate(), setup);
            c.Equal(Q("{'n':0,'title':'Pay','text':'Requires 100 Gold.','locked':true}"), Text(gate.Snapshot().State["options"]![0]), "a lock made with the page is not undone by what a relic does on entering");
            c.Equal(149, gate.Gold, "(the gold is there)");
        });
    }

    private static void CleanUp(Check c)
    {
        c.Group("an event that holds the belt gives it back when it is finished, once", () =>
        {
            var setup = new RunSetup { Seed = RealSeed, Potions = ["BloodPotion"] };
            var run = InEvent(static () => new Holder(), setup);
            var s = run.Snapshot();
            c.Equal("option:0=Choose Trade | option:1=Choose Stay", Labels(s), "while the event holds the belt no potion action is offered");
            c.That(Text(s.State["potions"]).Contains(Q("'usable':false"), StringComparison.Ordinal), "and the potion is not usable");
            c.Equal("'discard_potion:0' is not available now.", Refusal(run, "discard_potion:0"), "the refusal");

            Do(run, "option:0");
            c.Equal(100, run.Gold, "the clean-up ran when the event finished (it pays 1 gold)");
            c.Equal("option:0=Leave | potion:0=Use Blood Potion | discard_potion:0=Discard Blood Potion", Labels(run.Snapshot()), "the belt is free again on the finished page");
            Do(run, "option:0");
            c.Equal(100, run.Gold, "leaving does not clean up a second time");
        });

        c.Group("a page without options is a finished event whose clean-up waits until the room is left", () =>
        {
            var run = InEvent(static () => new Holder(), new RunSetup { Seed = RealSeed, Potions = ["BloodPotion"] });
            Do(run, "option:1");
            c.That(Ev(run).IsFinished, "finished");
            c.Equal(99, run.Gold, "no clean-up yet: the game cleans up at once only when an event says it is finished");
            c.Equal("option:0=Leave", Labels(run.Snapshot()), "the belt is still held; only leaving is offered");
            Do(run, "option:0");
            c.Equal(100, run.Gold, "leaving the room cleans up");
            c.That(run.State.Part<RewardsState>().CanRemovePotions, "and the belt is free");
        });
    }

    private static void DeathInAnEvent(Check c)
    {
        c.Group("a death in an event room ends the run: game over with the room's kind, then the end", () =>
        {
            var run = InEvent(static () => new Pit(), new RunSetup { Seed = RealSeed, Hp = 5 }, floor: 3);
            c.Equal("option:0=Choose Fall", Labels(run.Snapshot()), "an option that does not say what it costs is offered");
            Do(run, "option:0");
            c.That(run.IsOver && run.Result == RunResult.Died, "the run is over: died");
            var s = run.Snapshot();
            c.Equal("game_over", s.Screen, "the mod's game over screen first");
            c.Equal("Event", s.State["room"]!.GetValue<string>(), "in an Event room");
            Do(run, "continue");
            c.That(run.AtEnd, "then the end snapshot");
            c.Equal("Died on floor 3.", run.Summary(), "its summary");
        });
    }

    private static void Guards(Check c)
    {
        c.Group("a page nobody can leave is refused by name", () =>
        {
            var entering = Thrown<InvalidOperationException>(() => InEvent(static () => new Wall()));
            c.That(entering.Contains("Wall came to a page on which nothing can be chosen", StringComparison.Ordinal), $"an event whose only option would kill cannot be entered ({entering})");

            var run = InEvent(static () => new DeadEnd());
            var before = run.Dump();
            var going = Thrown<InvalidOperationException>(() => run.Execute("option:0"));
            c.That(going.Contains("DeadEnd came to a page on which nothing can be chosen (DOOR:locked)", StringComparison.Ordinal), $"an option that leads to such a page throws ({going})");
            c.Equal(before, run.Dump(), "and the run stands as before: the 5 gold are not paid");
        });

        c.Group("what an option runs must be a method of its event", () =>
        {
            var thrown = Thrown<InvalidOperationException>(() => InEvent(static () => new Sloppy()));
            c.That(thrown.Contains("Sloppy: what the option X runs is a function that caught a variable", StringComparison.Ordinal), $"a function that caught a local is refused when the option is made ({thrown})");

            var run = InEvent(static () => new Tidy());
            var copy = run.Clone();
            Do(copy, "option:0");
            c.That(Ev(copy).IsFinished && !Ev(run).IsFinished, "a function that uses only the event works, and on a copy it finishes the copy alone");
        });

        c.Group("a field a copy of the event would share is named", () =>
        {
            c.Sequence(["Careless._seen (List`1) is a field a copy of the event would share: copy it in OnCloned"], EventFields.UnsafeFields(typeof(Careless)), "a list with nothing that copies it");
            c.Sequence([], EventFields.UnsafeFields(typeof(Careful)), "a class that copies its fields is trusted");
            c.Sequence([], EventFields.UnsafeFields(typeof(Chests)).Concat(EventFields.UnsafeFields(typeof(Tablet))).Concat(EventFields.UnsafeFields(typeof(Keeper))), "numbers and deck cards are fine");
            c.Sequence([], EventFields.UnsafeFields(typeof(Neow)), "Neow has none");
        });

        c.Group("an event cannot be opened twice, read before it is opened, or shown without one", () =>
        {
            var run = InEvent(static () => new Chests());
            var ev = Ev(run);
            c.That(Thrown<InvalidOperationException>(() => EventScreen.Open(run, ev)).Contains("was begun before", StringComparison.Ordinal), "the same event object twice");
            c.That(Thrown<InvalidOperationException>(() => _ = new Chests().Rng).Contains("was not begun", StringComparison.Ordinal), "the stream of an event that was not opened");
        });
    }

    private static void CopiesAndReplays(Check c)
    {
        c.Group("a copy of a run in an event is a run of its own", () =>
        {
            var run = InEvent(static () => new Chests(), floor: 4);
            var before = run.Dump();
            var copy = run.Clone();
            c.Equal(before, copy.Dump(), "the copy is in the same state");
            c.That(!ReferenceEquals(Ev(run), Ev(copy)), "with an event object of its own");
            Do(copy, "option:0", "option:0");
            c.Equal(before, run.Dump(), "playing the copy does not move the original");
            c.Equal(Q("{'visited':'','current':{'event':'ThisOrThat','rng':1,'finished':false,'cleaned_up':false,'text':'Two chests stand here.','options':'PLAIN ORNATE','fields':{'_gold':'61'}}}"),
                Text(run.State.Dump()["parts"]!["EventsState"]), "the run's dump holds the event: its stream, its page, its own fields");
        });

        c.Group("a deck card an event keeps is the copy's own card on a copy", () =>
        {
            var run = InEvent(static () => new Keeper());
            var copy = run.Clone();
            var kept = ((Keeper)Ev(copy)).Card;
            c.That(ReferenceEquals(kept, copy.State.Deck[^1]) && !ReferenceEquals(kept, run.State.Deck[^1]), "the field points at the copy's deck");
            c.That(ReferenceEquals(Ev(copy).Options[0].Tips[0], copy.State.Deck[^1]), "and so does the option's tooltip");
            c.That(ReferenceEquals(Ev(copy).Options[0].Tips[1], copy.State.Relics[0]) && !ReferenceEquals(Ev(copy).Options[0].Tips[1], run.State.Relics[0]), "a held relic in the tooltip is the copy's own relic too");
            Do(copy, "option:0");
            c.Equal((9, 10), (copy.State.Deck.Count, run.State.Deck.Count), "the copy lost its card, the original did not");
        });

        c.Group("a run through an event replays from its save, also across a question and a rewards screen", () =>
        {
            var setup = new RunSetup { Seed = RealSeed };
            var director = new OneRoom { Event = static () => new Gate() };
            var run = new Run(setup, director);
            Do(run, "option:1");
            var mid = run.Clone();
            Do(mid, "card:9", "confirm");
            c.Equal("card_select", run.Snapshot().Screen, "a copy taken in the middle of the question was answered; the original still asks");
            Do(run, "card:0", "confirm", "option:0");
            c.Equal(9, run.State.Deck.Count, "the original removed another card");
            c.Sequence(["option:1", "card:0", "confirm", "option:0"], run.Actions, "its save");
            c.Equal(run.Dump(), Run.Replay(setup, director, run.Actions).Dump(), "the replay is in the same state");

            var loot = new Run(setup, director);
            Do(loot, "option:2", "reward:0", "option:0");
            c.Equal(loot.Dump(), Run.Replay(setup, director, loot.Actions).Dump(), "and so is the replay of the run that took the rewards");
            c.Equal(loot.Dump(), loot.Clone().Dump(), "and a copy");
        });
    }

    // ====================================================================================
    // Neow's room
    // ====================================================================================

    private static void NeowsRoom(Check c)
    {
        c.Group("Neow's room on the real seed: the heal, the three relics the game offered, and the pick the real run made", () =>
        {
            // The player starts hurt here, to show the heal: the game puts Neow's visitor at 0 and heals all of it.
            var setup = new RunSetup { Seed = RealSeed, Hp = 37, ExtraRelics = [static () => new Tollkeeper()] };
            var run = new Run(setup, new ActStart());
            c.Equal("Overgrowth", World(run).Acts[0], "the seed's first act");
            var s = run.Snapshot();
            c.Equal("event", s.Screen, "the run begins in Neow's room");
            var expected = Q("{'run':{'act':1,'floor':1,'ascension':0},'event':'Neow','ancient':true,'text':'Neow offers three relics: two blessings, and a third that comes with a price. Take one.','options':["
                             + "{'n':0,'title':'Precise Scissors','text':'At pickup: remove a card from your Deck.'},"
                             + "{'n':1,'title':'Arcane Scroll','text':'At pickup: add a random Rare card to your Deck.'},"
                             + "{'n':2,'title':'Leafy Poultice','text':'At pickup: lose 12 Max HP.'}],")
                           + $"{Player(gold: 149, relics: "'Burning Blood','Tollkeeper'")},{NoPotions}}}";
            c.Equal(expected, Text(s.State), "the page as docs/screens.md has it: the options are the relics' own names and texts, and no relic key repeats them");
            c.Equal("option:0=Take Precise Scissors | option:1=Take Arcane Scroll | option:2=Take Leafy Poultice", Labels(s), "the labels name the relics");

            var neow = (Neow)Ev(run);
            c.Equal(80, run.Hp, "healed to full");
            c.Equal(80, neow.HealedAmount, "80 HP healed, as every record of a new run shows (hp_healed), whatever the HP was before");
            var relic = run.State.FindRelic<Tollkeeper>()!;
            c.Equal(80, relic.SawHp, "the heal came before the relics heard of the room");
            c.Equal((18, 3), (relic.SawDraws, relic.SawOptions), "and so did the offer: eighteen numbers of Neow's stream, three options");
            c.Equal(1, World(run).Rooms[0].EventsVisited, "Neow's room moved the act's event counter to 1, as the save after the pick has it");
            c.Sequence([], run.State.Part<EventsState>().Visited, "but Neow is not an event of the list: nothing is met");
            c.Equal(0, World(run).Streams.Counter(RunStream.Rewards), "nothing on Neow's screen draws from rewards");

            // The real run took Arcane Scroll, which gave Barricade from one number of rewards.
            Do(run, "option:1");
            c.Equal("Barricade", run.State.Deck[^1].Def.ClassName, "Arcane Scroll's card is Barricade");
            c.Equal(1, World(run).Streams.Counter(RunStream.Rewards), "from one number of rewards");
            s = run.Snapshot();
            c.Equal(Q("{'run':{'act':1,'floor':1,'ascension':0},'event':'Neow','ancient':true,'finished':true,'options':[{'n':0,'title':'Proceed','leave':true}],")
                    + $"{Player(gold: 149, relics: "'Burning Blood','Tollkeeper','Arcane Scroll'", deck: "'deck_size':11,'deck':{'Barricade':1,'Bash':1,'Defend':4,'Strike':5}")},{NoPotions}}}",
                Text(s.State), "the finished page: no text, the room's leave option");
            c.Equal("option:0=Leave", Labels(s), "labelled Leave");

            Do(run, "option:0");
            s = run.Snapshot();
            c.Equal("map", s.Screen, "leaving opens the map");
            c.Equal("(3,0) ancient", s.State["at"]!.GetValue<string>(), "standing on the start point");
            c.Equal(run.Dump(), Run.Replay(setup, new ActStart(), run.Actions).Dump(), "the run replays from its two actions");
        });

        c.Group("a relic whose pickup asks for a card: the question comes first, then Neow's finished page", () =>
        {
            var run = new Run(new RunSetup { Seed = RealSeed }, new ActStart());
            Do(run, "option:0");
            c.Equal("card_select", run.Snapshot().Screen, "Precise Scissors asks which card to remove");
            c.Equal(10, run.State.Deck.Count, "the action waits for the answer: no card is removed yet");
            Do(run, "card:5", "confirm");
            c.Sequence(["BurningBlood", "PreciseScissors"], run.State.Relics.Select(relic => relic.ClassName), "the relic is the player's");
            c.Equal(9, run.State.Deck.Count, "a Defend is removed");
            c.Equal("option:0=Leave", Labels(run.Snapshot()), "and Neow is finished");
        });

        c.Group("the cursed relic's effect runs at pickup", () =>
        {
            var run = new Run(new RunSetup { Seed = RealSeed }, new ActStart());
            Do(run, "option:2");
            c.Equal((68, 68), (run.Hp, run.MaxHp), "Leafy Poultice: 80 - 12 max HP");
        });

        c.Group("a relic's own tooltip cards are listed with its option", () =>
        {
            // Seed 1CJXGGA4E5 offered Neow's Torment, Lava Rock and Cursed Pearl. Its first act is Underdocks; Neow does not care.
            var run = new Run(new RunSetup { Seed = "1CJXGGA4E5" }, new ActStart { ForceOvergrowth = true });
            var s = run.Snapshot();
            c.Equal(Q("[{'n':0,'title':'Neow\\u0027s Torment','text':'At pickup: add a Bash to your Deck.','cards':[").Replace("\\u0027", "'") + CardJson("Bash")
                    + Q("]},{'n':1,'title':'Lava Rock','text':'A relic of the checks.'},{'n':2,'title':'Cursed Pearl','text':'At pickup: gain 333 Gold.'}]"),
                Text(s.State["options"]), "the three relics of that seed; the first shows its card");
            c.Equal("option:0=Take Neow's Torment | option:1=Take Lava Rock | option:2=Take Cursed Pearl", Labels(s), "the labels");
            Do(run, "option:2");
            c.Equal(432, run.Gold, "Cursed Pearl: 99 + 333");
        });

        c.Group("strict mode: with the seed's three relics built the offer is the game's and nothing is noted", () =>
        {
            var run = new Run(new RunSetup { Seed = RealSeed }, new ActStart { Strict = true });
            c.Equal("option:0=Take Precise Scissors | option:1=Take Arcane Scroll | option:2=Take Leafy Poultice", Labels(run.Snapshot()), "the same offer");
            c.Equal(0, run.State.Part<LeftOutLog>().Entries.Count, "nothing was passed over");
        });

        c.Group("a seed whose offer is not built: drawn again and noted, or the end of a strict run", () =>
        {
            // Seed 75F277YN2G offered Pomander, Nutritious Oyster and Silver Crucible; none of the six stand-ins is among them.
            var run = new Run(new RunSetup { Seed = "75F277YN2G" }, new ActStart { ForceOvergrowth = true });
            var titles = run.Snapshot().State["options"]!.AsArray().Select(o => o!["title"]!.GetValue<string>()).ToList();
            c.That(titles.Count == 3 && titles.All(title => StandIns.Make.Values.Any(make => make().Name == title)), $"three built relics are offered instead ({string.Join(", ", titles)})");
            var log = run.State.Part<LeftOutLog>();
            c.That(log.Entries.Count > 0 && log.Entries.All(e => e is { Floor: 1, Kind: "relic", What: "Neow's offer" }), "what was passed over is in the run's log, on floor 1");
            c.Equal("SilverCrucible", log.Entries[0].ClassName, "first the game's cursed relic");
            c.Equal(1, log.FirstFloor, "the run is not the game's own from floor 1");

            var strict = new Run(new RunSetup { Seed = "75F277YN2G" }, new ActStart { ForceOvergrowth = true, Strict = true });
            c.That(strict.AtEnd && strict.Result == RunResult.Unrepresentable, "a strict run ends as unrepresentable at its start");
            c.That(strict.ResultReason is { } reason && reason.StartsWith("Neow offers Pomander, NutritiousOyster, SilverCrucible on this seed, and the arena leaves out Pomander", StringComparison.Ordinal), $"the reason names the offer ({strict.ResultReason})");
        });

        c.Group("Neow of the registry: her room opens when her relics are built, and the run is unrepresentable when they are not", () =>
        {
            var offer = NeowOffer.Roll(new RunStreams(RealSeed).Event("Neow"), RunUnlocks.All);
            var run = new Run(new RunSetup { Seed = RealSeed }, new RegistryNeow());
            if (offer.Found)
            {
                var titles = run.Snapshot().State["options"]!.AsArray().Select(o => o!["title"]!.GetValue<string>()).ToList();
                c.Sequence(offer.Relics.Select(relic => Content.Registry.NewRelic(relic).Name), titles, "the offer of the built relics, by their titles");
                c.That(run.Snapshot().State["ancient"]!.GetValue<bool>(), "at an Ancient");
            }
            else
            {
                c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, "no relic of Neow is built in this tree: the run ends as unrepresentable at its start");
                c.Equal(offer.Reason, run.ResultReason, "with the offer's reason");
            }
        });
    }

    /// <summary>Opens the registry's own Neow, as the act's director will.</summary>
    private sealed class RegistryNeow : RunDirector
    {
        public override void Begin(Run run)
        {
            var world = RunWorld.Begin(run.State);
            run.State.TotalFloor = 1;
            EventScreen.Open(run, world.Rooms[0].Ancient!);
        }

        public override void AfterFightWon(Run run, FightOutcome fight) => run.End(RunResult.Cleared);
    }

    private static void TheRealRunsFirstEvent(Check c)
    {
        c.Group("the real run's path to its first event: the Unknown point of floor 4 is an Event room and holds This or That", () =>
        {
            var setup = new RunSetup { Seed = RealSeed };
            var run = new Run(setup, new ActStart());
            Do(run, "option:1", "option:0");                  // Neow: Arcane Scroll, leave
            Do(run, "map:2", "proceed", "map:0", "proceed");  // (5,1) and (6,2), the two hallway fights of the real run
            c.Equal("(6,3) unknown", run.Snapshot().State["next"]![0]!["at"] + " " + run.Snapshot().State["next"]![0]!["type"], "the next point is the Unknown one");
            Do(run, "map:0");

            var director = (ActStart)run.Director;
            c.Sequence(["Monster", "Monster", "Event"], director.Rooms, "the Unknown point rolled an Event room, as in the game");
            c.Sequence(["ThisOrThat"], director.Pulled, "and the list gave This or That: entry 1, since Neow's room took the counter to 1");
            // While the point is being entered the run's floor is still 3; once it is entered it is 4. Either way three floors are behind.
            c.Sequence(["floor 3, 3 behind", "entered on floor 4, 3 behind"], director.Asked, "the floors behind the room are the same when the map asks and when the room is made");
            c.Equal(2, World(run).Rooms[0].EventsVisited, "the event counter is 2, as the game saved it");
            c.Sequence(["ThisOrThat"], run.State.Part<EventsState>().Visited, "This or That is met (the save's events_seen)");
            c.Equal(1, World(run).Streams.Counter(RunStream.UnknownMapPoint), "one number of unknown_map_point");

            var s = run.Snapshot();
            c.Equal("event", s.Screen, "the event's screen");
            c.Equal(Q("{'act':1,'floor':4,'ascension':0}"), Text(s.State["run"]), "on floor 4");
            c.Equal("This or That", s.State["event"]!.GetValue<string>(), "its title");
            c.That(s.State["ancient"] is null, "not an Ancient");

            Do(run, "option:1", "option:0");
            s = run.Snapshot();
            c.Equal("map", s.Screen, "leaving the event opens the map");
            c.Equal("(6,3) unknown", s.State["at"]!.GetValue<string>(), "standing on the event's point");
            c.Equal(run.Dump(), Run.Replay(setup, new ActStart(), run.Actions).Dump(), "the whole way replays");
        });

        c.Group("the same path with no event built: the map rolls the point again with the Event barred", () =>
        {
            var run = new Run(new RunSetup { Seed = RealSeed }, new ActStart { Built = "none" });
            Do(run, "option:1", "option:0", "map:2", "proceed", "map:0", "proceed", "map:0");
            var director = (ActStart)run.Director;
            c.Sequence(["Monster", "Monster", "Monster"], director.Rooms, "the Unknown point is a Monster room: with the Event out, the game's roll gives the lowest kind left");
            c.Sequence([], director.Pulled, "no event was taken from the list");
            c.Equal(1, World(run).Rooms[0].EventsVisited, "and the event counter did not move");
            c.Equal(2, World(run).Streams.Counter(RunStream.UnknownMapPoint), "the refused number is used up: two numbers");
        });

        c.Group("the same path in a strict run with the event not built: the run ends as unrepresentable in the room", () =>
        {
            var run = new Run(new RunSetup { Seed = RealSeed }, new ActStart { Built = "SelfHelpBook", Strict = true });
            Do(run, "option:1", "option:0", "map:2", "proceed", "map:0", "proceed", "map:0");
            c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, "ended as unrepresentable");
            c.That(run.ResultReason is { } reason && reason.Contains("ThisOrThat", StringComparison.Ordinal), $"the reason names the event ({run.ResultReason})");
            c.Equal(3, run.State.TotalFloor, "standing as before the step onto the point");
        });
    }

    // ====================================================================================
    // the registry
    // ====================================================================================

    private static void RegistryAndCompleteness(Check c)
    {
        c.Group("events are content of the registry: built, or left out by name with a reason", () =>
        {
            var registry = Content.Registry;
            c.That(registry.Has(ContentKind.Event, "Neow") && registry.NewEvent("Neow") is Neow, "Neow is registered and can be made");
            c.That(!ReferenceEquals(registry.NewEvent("Neow"), registry.NewEvent("Neow")), "a new object every time");
            c.That(EventList.IsBuilt("Neow") && !EventList.IsBuilt("WarHistorianRepy"), "IsBuilt reads the registry");

            foreach (var name in EventList.InAnOvergrowthAct)
            {
                var built = registry.Has(ContentKind.Event, name);
                var leftOut = registry.IsExcluded(ContentKind.Event, name, out var reason);
                c.That(built != leftOut, $"{name} is either registered or left out with a reason");
                if (leftOut) c.That(reason.Contains("chunk", StringComparison.Ordinal), $"{name}: the reason names the part of the plan that builds it ({reason})");
                if (leftOut) c.Equal(reason, EventList.WhyNotBuilt(name), $"{name}: WhyNotBuilt gives the reason");
            }
            var known = EventList.InAnOvergrowthAct.Append("Neow").ToHashSet(StringComparer.Ordinal);
            c.Sequence([], registry.EventNames.Where(name => !known.Contains(name)), "nothing is registered as an event that an Overgrowth act cannot hold");

            var row = Completeness.Rows().Single(r => r.Kind == ContentKind.Event);
            c.Equal(22, row.Wanted.Count, "the completeness table wants the 21 events and Neow");
            c.Sequence([], row.Missing, "none is missing: each is registered or left out");
            c.Sequence([], Completeness.Problems().Where(problem => problem.Contains("Event", StringComparison.OrdinalIgnoreCase)), "and the table has no complaint about events");

            // Whichever event of the act is still left out (This or That was the example until it was built).
            if (EventList.InAnOvergrowthAct.FirstOrDefault(name => registry.IsExcluded(ContentKind.Event, name, out _)) is { } leftOutEvent)
            {
                registry.IsExcluded(ContentKind.Event, leftOutEvent, out var why);
                c.That(Thrown<KeyNotFoundException>(() => registry.NewEvent(leftOutEvent)).Contains(why, StringComparison.Ordinal), $"making an event that is left out says why ({leftOutEvent})");
            }
            c.That(Thrown<KeyNotFoundException>(() => registry.NewEvent("NoSuchEvent")).Contains("no Event called 'NoSuchEvent'", StringComparison.Ordinal), "an unknown name is refused");
        });
    }

    // ====================================================================================
    // the real records on this machine
    // ====================================================================================

    private static void GoldenFolder(Check c)
    {
        c.Group("the game's own run records on this machine, when there are any: every recorded offer of Neow", () =>
        {
            var folder = Environment.GetEnvironmentVariable("ARENA_GOLDEN_SAVES");
            if (string.IsNullOrEmpty(folder))
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects", "sts2-notes", "golden-saves");
            var history = Path.Combine(folder, "history");
            if (!Directory.Exists(history))
            {
                c.That(true, "no folder of run records here");
                return;
            }

            var bySlug = NeowRelics.All.ToDictionary(GameNames.Slug, StringComparer.Ordinal);
            var compared = 0;
            foreach (var file in Directory.GetFiles(history, "*.run").OrderBy(f => f, StringComparer.Ordinal))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                // Only records of this build of standard runs: another build may offer otherwise.
                if (!root.TryGetProperty("build_id", out var build) || build.GetString() != "v0.107.1") continue;
                if (!root.TryGetProperty("game_mode", out var mode) || mode.GetString() != "standard") continue;
                if (!root.TryGetProperty("map_point_history", out var acts) || acts.GetArrayLength() == 0 || acts[0].GetArrayLength() == 0) continue;
                var seed = root.GetProperty("seed").GetString()!;
                var stats = acts[0][0].GetProperty("player_stats");
                var players = root.GetProperty("players").GetArrayLength();
                for (var slot = 0; slot < stats.GetArrayLength(); slot++)
                {
                    if (!stats[slot].TryGetProperty("ancient_choice", out var choices)) continue;
                    var recorded = choices.EnumerateArray().Select(choice => choice.GetProperty("TextKey").GetString()!).ToList();
                    if (recorded.Any(key => !bySlug.ContainsKey(key))) continue;
                    var offer = NeowOffer.Roll(NeowStream(seed, slot), RunUnlocks.All, isBuilt: static _ => true, players: players);
                    c.Sequence(recorded.Select(key => bySlug[key]), offer.Relics, $"{Path.GetFileName(file)}: seed {seed}, player {slot + 1} of {players}");
                    compared++;
                }
            }
            c.That(true, $"{compared} recorded offers compared");
        });
    }
}
