using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core;
using static SpireMind.Arena.DeckCheckKit;

namespace SpireMind.Arena;

/// <summary>
/// The seven events of <see cref="EventsA"/> and the two swords, each through the event screen
/// as a player meets it.
/// <para>
/// What the expected values rest on. Prices, amounts and their order: each event's class in the
/// game (MegaCrit.Sts2.Core.Models.Events, v0.107.1), worked out by hand here. Titles: the
/// game's English text table. What an event rolls on a seed: a reading of the game's generator
/// and string hash that is not the arena's (Tools/event_rolls.py next to this file), which also
/// reads the option lists of a transform out of the game's card pool files; its results are
/// typed in below. Three things are the real game's own: the event This or That? of Mark's run
/// on seed 1BUSLN0FY5 gave the relic Stone Cracker and the curse Clumsy (its record and saves,
/// sts2-notes/golden-saves: the relic's rarity was the 26th number of <c>rewards</c>, the relic
/// the front of the player's Uncommon list); and the record of a co-op run of v0.108.0 on seed
/// B1GYAHUBLESSTH1SRUNTHANKY0U#BLESSED has both players dive at The Sunken Statue for 112 and
/// 121 gold, which is what the seed gives for the first and the second player (the event's
/// code is the same in v0.107.1 and in v0.111.0).
/// </para>
/// </summary>
public sealed class EventsAChecks : IArenaChecks
{
    // The entry point is the interface's own, so that inside this class "Run" is the run.
    void IArenaChecks.Run(Check c)
    {
        Registered(c);
        ThisOrThatChecks(c);
        TheRealRunsRelic(c);
        RelicPulls(c);
        SunkenStatueChecks(c);
        TheRealRecordOfTheStatue(c);
        JungleMaze(c);
        UnrestSiteChecks(c);
        TabletOfTruthChecks(c);
        AromaOfChaosChecks(c);
        MorphicGroveChecks(c);
        Swords(c);
        OrderOfEffects(c);
        DeathsAndRelics(c);
        StreamsAndModes(c);
        CopiesAndReplays(c);
        TheActsList(c);
        ManyRooms(c);
        GoldenFolder(c);
    }

    private const string RealSeed = "1BUSLN0FY5";

    /// <summary>The seed of the co-op record with The Sunken Statue, as the record has it.</summary>
    private const string StatueSeed = "B1GYAHUBLESSTH1SRUNTHANKY0U#BLESSED";

    private static readonly string[] OvergrowthActs = ["Overgrowth", "Hive", "Glory"];

    /// <summary>Seeds of Mark's saves and records, for values that depend on the seed alone.</summary>
    private static readonly string[] Seeds = [RealSeed, "KMEHXZW0R5", "EEXRY5WA1R", "E9U400ERJ4", "3FBTGQ63TM", "AG5XPLQQNY", "75F277YN2G"];

    // The player's relic bag of seed 1BUSLN0FY5 as the game saved it at the start of the run
    // (golden-saves/run-164514-abe3bfe862ef.save, players[0].relic_grab_bag): the whole Uncommon
    // list, and the front of the Common list.
    private static readonly string[] SavedUncommon =
    [
        "StoneCracker", "ParryingShield", "OrnamentalFan", "Kusarigama", "PenNib", "Pantograph", "SelfFormingClay", "Nunchaku", "MiniatureCannon",
        "MercuryHourglass", "Vambrace", "LetterOpener", "TuningFork", "PetrifiedToad", "RippleBasin", "JossPaper", "TinyMailbox", "Planisphere", "HornCleat",
        "LastingCandy", "Akabeko", "GremlinHorn", "ReptileTrinket", "Candelabra", "Permafrost", "Orichalcum", "EternalFeather", "Pear", "SparklingRouge",
        "PaperPhrog", "LuckyFysh", "BowlerHat",
    ];

    private static readonly string[] SavedCommonFront = ["RegalPillow", "Strawberry", "RedSkull", "BronzeScales", "Gorget", "FestivePopper"];

    // ====================================================================================
    // helpers
    // ====================================================================================

    private static RunWorld World(Run run) => run.State.Part<RunWorld>();

    private static RunEvent Ev(Run run) => EventScreen.Current(run) ?? throw new InvalidOperationException("The run is in no event.");

    private static int Rewards(Run run) => World(run).Streams.Counter(RunStream.Rewards);

    private static string Options(Run run) => Text(run.Snapshot().State["options"]);

    private static string? PageText(Run run) => run.Snapshot().State["text"]?.GetValue<string>();

    private static string Q(string json) => json.Replace('\'', '"');

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

    private static IReadOnlyList<LeftOutLog.Entry> Logged(Run run) => run.State.FindPart<LeftOutLog>()?.Entries ?? [];

    private static List<string> LoggedAs(Run run, string kind) => Logged(run).Where(entry => entry.Kind == kind).Select(entry => entry.ClassName).ToList();

    /// <summary>The relic a pull gives from a list in the game's order: the first one the arena holds; the ones before it are passed over.</summary>
    private static (string Given, string[] PassedOver) FirstBuilt(IReadOnlyList<string> gameOrder)
    {
        var given = gameOrder.First(RunRelics.IsBuilt);
        return (given, gameOrder.TakeWhile(relic => relic != given).ToArray());
    }

    private static string TitleOfRelic(string className) => Content.Registry.NewRelic(className).Name;

    /// <summary>The finished page of every event: one option, the room's own.</summary>
    private const string ProceedOnly = """[{"n":0,"title":"Proceed","leave":true}]""";

    /// <summary>The stream of a player's own copy of an event: the run's seed number plus the player's slot plus the hash of the event's id.</summary>
    private static GameRng EventStream(string seed, string eventId, int slot = 0) =>
        new(unchecked((uint)((int)new RunSeed(seed).Hash + slot + RngSet.NameHash(eventId))));

    /// <summary>The screen the checks' director puts up when an event's room is left.</summary>
    private sealed class Outside : RunScreen
    {
        public override string Name => "outside";

        public override GameSnapshot Snapshot(Run run) => new()
        {
            Screen = Name, AwaitingDecision = true, State = new JsonObject { ["run"] = run.RunJson() }, Actions = [new LegalAction("wait", "Wait")],
        };

        public override ActionResult Execute(Run run, string actionId) => actionId == "wait" ? ActionResult.Success : ActionResult.Fail($"Unknown action '{actionId}'.");

        public override RunScreen Clone(CloneMap map) => new Outside();
    }

    /// <summary>
    /// A run of one event room: the world of the seed (its first act set to Overgrowth, whatever
    /// the seed rolls), the floor, then the event by its class name. When the room is left, the
    /// fights named in <see cref="Fights"/> follow, one floor each, against a dummy of 18 HP that
    /// does nothing; without any, a screen of the checks is put up.
    /// </summary>
    private sealed class Room : RunDirector
    {
        public string? Event { get; init; }
        public int Floor { get; init; } = 4;
        public StreamMode Mode { get; init; } = StreamMode.Game;
        public bool Strict { get; init; }

        /// <summary>Numbers of the player's <c>rewards</c> stream that were drawn before the room.</summary>
        public int RewardsBefore { get; init; }

        /// <summary>Numbers drawn from other streams of the run before the event starts.</summary>
        public int Noise { get; init; }

        /// <summary>Every relic of this rarity that the arena holds leaves the player's bag before the room.</summary>
        public RelicRarity Without { get; init; } = RelicRarity.None;

        /// <summary>A relic of the registry picked up when the room is left.</summary>
        public string? ThenObtain { get; init; }

        /// <summary>A relic of the checks picked up when the room is left (a function that holds nothing, so a copy may share it).</summary>
        public Func<Relic>? ThenGive { get; init; }

        /// <summary>The fights after the room, one letter each: m a hallway fight, e an Elite, b a boss.</summary>
        public string Fights { get; init; } = "";

        public int Fought;
        public int Left;

        public override void Begin(Run run)
        {
            var world = RunWorld.Begin(run.State, mode: Mode, acts: OvergrowthActs);
            if (Strict) run.State.Part<LeftOutLog>().Strict = true;
            if (RewardsBefore > 0) world.Streams.FastForward(RunStream.Rewards, RewardsBefore);
            for (var i = 0; i < Noise; i++)
            {
                world.Streams.Draw(RunStream.Shops, Purpose.ShopSale).NextInt(100);
                world.Streams.Draw(RunStream.Niche, Purpose.Of("noise")).NextInt(100);
            }
            if (Without != RelicRarity.None)
                foreach (var relic in world.PlayerBag.Relics(Without).Where(RunRelics.IsBuilt).ToList()) world.PlayerBag.Remove(relic);
            run.State.TotalFloor = Floor;
            if (Event is null) run.Push(new Outside());
            else EventScreen.Open(run, Event);
        }

        public override void AfterRoom(Run run)
        {
            Left++;
            if (ThenObtain is not null) run.ObtainRelic(ThenObtain);
            if (ThenGive is not null) run.ObtainRelic(ThenGive());
            Next(run);
        }

        public override void AfterFightWon(Run run, FightOutcome fight) => Next(run);

        private void Next(Run run)
        {
            if (Fought >= Fights.Length)
            {
                if (Fights.Length > 0) run.End(RunResult.Cleared);
                else run.Push(new Outside());
                return;
            }
            var kind = Fights[Fought++] switch { 'e' => RoomType.Elite, 'b' => RoomType.Boss, _ => RoomType.Monster };
            run.State.TotalFloor++;
            run.StartFight(new FightRequest { Monsters = static () => [new PlacedMonster(new CheckDummy { Hp = 18 })], RoomType = kind, ShuffleDeck = false });
        }
    }

    private static Run Open(string ev, RunSetup? setup = null, Room? room = null)
    {
        room ??= new Room();
        return new Run(setup ?? new RunSetup { Seed = RealSeed }, new Room
        {
            Event = ev, Floor = room.Floor, Mode = room.Mode, Strict = room.Strict, RewardsBefore = room.RewardsBefore, Noise = room.Noise,
            Without = room.Without, ThenObtain = room.ThenObtain, ThenGive = room.ThenGive, Fights = room.Fights,
        });
    }

    private static Run Open(string ev, int hp, int maxHp = 80, int gold = 99, string seed = RealSeed) =>
        Open(ev, new RunSetup { Seed = seed, Hp = hp, MaxHp = maxHp, Gold = gold });

    /// <summary>
    /// A relic of the checks that writes down what it hears outside a fight, with the state at
    /// that moment: gold gained, HP lost, a card arriving in the deck. It shows in which order an
    /// option does its things. In a fight it notes the relics as they stand at the hook that
    /// comes just before the victory hook, and who gave the player Strength.
    /// </summary>
    private sealed class Watcher : Relic, IDeckListener
    {
        public string Seen = "";
        public string AtVictoryEarly = "";
        public string Strength = "";

        public override void AfterPowerAmountChanged(Fight f, Power power, decimal amount, Creature? applier, CardInstance? cardSource)
        {
            if (power is StrengthPower) Strength += $"{(int)amount} Strength from {(applier is null ? "nobody" : applier.IsPlayer ? "the player" : "an enemy")}; ";
        }

        public override void AfterCombatVictoryEarly(Fight f) =>
            AtVictoryEarly = string.Join(" ", f.Relics.Select(relic => relic is SwordOfStone sword ? $"SwordOfStone:{sword.ElitesDefeated}" : relic.ClassName));

        public override string Name => "Watcher";
        public override string Describe() => "Writes down what it sees.";

        private void Note(FormattableString what) => Seen += (Seen.Length > 0 ? " | " : "") + what.ToString(CultureInfo.InvariantCulture);

        public override void AfterGoldGained(RunContext c) => Note($"gold is {c.Gold} at {c.Hp} HP");

        public override void AfterCurrentHpChanged(Fight f, Creature creature, decimal delta) =>
            Note($"{(int)delta} HP to {creature.Hp}/{creature.MaxHp} with {f.Relics.Count} relics");

        void IDeckListener.AfterCardChangedPiles(RunContext c, CardInstance card, PileType oldPile) =>
            Note($"{card.Def.ClassName} from {oldPile} into a deck of {c.Deck.Count} at {c.Hp}/{c.MaxHp} HP, {c.Gold} gold, {c.Relics.Count} relics");
    }

    /// <summary>A relic of the checks that costs 10 HP whenever an Event room is entered: after the event's start, so after what the event noted there.</summary>
    private sealed class Thorn : Relic
    {
        public override string Name => "Thorn";
        public override string Describe() => "Lose 10 HP on entering an event.";

        public override void AfterRoomEntered(RunContext c, RoomKind room)
        {
            if (room == RoomKind.Event) c.Damage(10);
        }
    }

    private static RunSetup Watched(int hp = 80, int maxHp = 80, int gold = 99, string seed = RealSeed) =>
        new() { Seed = seed, Hp = hp, MaxHp = maxHp, Gold = gold, ExtraRelics = [static () => new Watcher()] };

    private static string Seen(Run run) => run.State.Relics.OfType<Watcher>().Single().Seen;

    /// <summary>With $ARENA_EVENT_SOAK set, the two counting groups also say what they counted (on the error stream, like a log line).</summary>
    private static void Report(string line)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ARENA_EVENT_SOAK"))) Console.Error.WriteLine($"EventsAChecks: {line}");
    }

    /// <summary>Plays the fight that is on to its end: Strikes while there is energy, then the turn ends.</summary>
    private static void WinFight(Run run)
    {
        var fight = run.Fight;
        for (var n = 0; n < 200 && run.Fight is not null && ReferenceEquals(run.Fight, fight); n++)
            Do(run, run.LegalActions().FirstOrDefault(a => a.Id.StartsWith("play:", StringComparison.Ordinal))?.Id ?? "end_turn");
    }

    // ====================================================================================
    // the registry
    // ====================================================================================

    private static void Registered(Check c)
    {
        c.Group("the seven events and the two swords are content of the registry, under the game's names", () =>
        {
            // The chunk's list (chunks.json, chunk 15) and the game's English titles (its events and relics text tables).
            (string Class, string Title)[] events =
            [
                ("AromaOfChaos", "Aroma of Chaos"), ("JungleMazeAdventure", "Jungle Maze Adventure"), ("MorphicGrove", "Morphic Grove"),
                ("SunkenStatue", "The Sunken Statue"), ("TabletOfTruth", "Tablet of Truth"), ("ThisOrThat", "This or That?"), ("UnrestSite", "Unrest Site"),
            ];
            c.Sequence(events.Select(e => e.Class), EventsA.Events, "the chunk's seven events");
            var registry = Content.Registry;
            foreach (var (name, title) in events)
            {
                c.That(registry.Has(ContentKind.Event, name) && EventList.IsBuilt(name), $"{name} is registered");
                c.That(!registry.IsExcluded(ContentKind.Event, name, out _), $"{name} is no longer left out");
                c.That(EventList.InAnOvergrowthAct.Contains(name), $"{name} is an event an Overgrowth act can hold");
                var ev = registry.NewEvent(name);
                c.Equal(name, ev.ClassName, $"{name}: the class name, which seeds its stream");
                c.Equal(title, ev.Title, $"{name}: the game's title");
                c.That(!ev.IsAncient, $"{name} is no Ancient");
            }

            // An option's key is its name in the game's text table, which is what the game's run records write
            // ("THIS_OR_THAT.pages.INITIAL.options.ORNATE.title").
            (string Class, string Keys)[] firstPages =
            [
                ("AromaOfChaos", "LET_GO MAINTAIN_CONTROL"), ("JungleMazeAdventure", "SOLO_QUEST JOIN_FORCES"), ("MorphicGrove", "GROUP LONER"),
                ("SunkenStatue", "GRAB_SWORD DIVE_INTO_WATER"), ("TabletOfTruth", "DECIPHER_1 SMASH"), ("ThisOrThat", "PLAIN ORNATE"), ("UnrestSite", "REST KILL"),
            ];
            foreach (var (name, keys) in firstPages)
                c.Equal(keys, string.Join(" ", Ev(Open(name, hp: 50, gold: 120)).Options.Select(option => option.Key)), $"{name}: the keys of its first page's options, in the game's order");
            var tablet = Open("TabletOfTruth");
            Do(tablet, "option:0");
            c.Equal("DECIPHER GIVE_UP", string.Join(" ", Ev(tablet).Options.Select(option => option.Key)), "TabletOfTruth: the keys of its later pages");

            (string Class, string Title)[] relics = [("SwordOfStone", "Sword of Stone"), ("SwordOfJade", "Sword of Jade")];
            c.Sequence(relics.Select(r => r.Class), EventsA.Relics, "the chunk's two relics");
            foreach (var (name, title) in relics)
            {
                c.That(RunRelics.IsBuilt(name), $"{name} is registered");
                c.Equal(title, registry.NewRelic(name).Name, $"{name}: the game's title");
                c.Equal(RelicSource.Event, ReachableRelics.Find(name)?.Source, $"{name} is on the list of reachable relics as an event's");
                c.Equal(15, ReachableRelics.Find(name)?.Chunk, $"{name} is this chunk's");
                c.That(!GameLists.IsBagRelic(name), $"{name} is in no relic bag: no pull, chest or shop gives it");
            }
            c.Sequence([], registry.Problems.Where(problem => EventsA.Events.Concat(EventsA.Relics).Any(name => problem.Contains(name, StringComparison.Ordinal))),
                "the registry has no complaint about any of them (a name both registered and left out, a field a copy would share)");

            // Five of the seven are always allowed; Morphic Grove needs 100 gold, Unrest Site a player at 70% HP or less (EventList has the conditions).
            var fresh = new Run(new RunSetup { Seed = RealSeed }, new Room());
            c.Sequence(["AromaOfChaos", "JungleMazeAdventure", "SunkenStatue", "TabletOfTruth", "ThisOrThat"], EventsA.Events.Where(name => EventList.IsAllowed(fresh, name)),
                "allowed for a new player with 99 gold at full HP");
            var hurtAndRich = new Run(new RunSetup { Seed = RealSeed, Hp = 56, Gold = 100 }, new Room());
            c.Sequence(EventsA.Events, EventsA.Events.Where(name => EventList.IsAllowed(hurtAndRich, name)), "all seven are allowed at 56 of 80 HP with 100 gold");
        });
    }

    // ====================================================================================
    // This or That?
    // ====================================================================================

    private static void ThisOrThatChecks(Check c)
    {
        c.Group("This or That?: the page, and the gold it rolls", () =>
        {
            var run = Open("ThisOrThat");
            var s = run.Snapshot();
            c.Equal("event", s.Screen, "the event's screen");
            c.Equal("run,event,text,options,player,potions", Keys(s.State), "the keys of the mod's event snapshot, in its order");
            c.Equal("This or That?", s.State["event"]!.GetValue<string>(), "the title");
            // 61: the first number of the event's stream on this seed, as a whole number from 41 to 68.
            c.Equal(Q("[{'n':0,'title':'This','text':'Lose 6 HP. Gain 61 Gold.'},{'n':1,'title':'That','text':'Obtain a random Relic. Add Clumsy to your Deck.','cards':[") + CardJson("Clumsy") + "]}]",
                Options(run), "two options: This with its price and the rolled gold, That with the curse in its tooltip");
            c.Equal("option:0=Choose This | option:1=Choose That", Labels(s), "one action each");
            c.Equal(1, Ev(run).Rng.Counter, "the start took one number of the event's stream");
            c.Equal(0, Rewards(run), "and none of rewards");

            int[] gold = [61, 62, 59, 51, 44, 59, 63];
            for (var i = 0; i < Seeds.Length; i++)
                c.Equal(gold[i], ((ThisOrThat)Ev(Open("ThisOrThat", 80, seed: Seeds[i]))).Gold, $"seed {Seeds[i]}: the gold");

            var seen = new HashSet<int>();
            var maker = new GameRng(15);
            for (var n = 0; n < 3000; n++) seen.Add(((ThisOrThat)Ev(Open("ThisOrThat", 80, seed: RunSeed.Random(maker).Text))).Gold);
            c.Equal((41, 68, 28), (seen.Min(), seen.Max(), seen.Count), "over 3,000 seeds the gold is every whole number from 41 to 68 and nothing else");
        });

        c.Group("This or That?: This costs 6 HP and pays the gold", () =>
        {
            var run = Open("ThisOrThat");
            Do(run, "option:0");
            c.Equal((74, 80, 160), (run.Hp, run.MaxHp, run.Gold), "80 - 6 HP, 99 + 61 gold");
            var s = run.Snapshot();
            c.Equal("run,event,text,finished,options,player,potions", Keys(s.State), "the finished page");
            c.Equal(ProceedOnly, Options(run), "its one option is the room's own");
            c.Equal("option:0=Leave", Labels(s), "labelled Leave");
            c.Equal("You took the plain one: you lost 6 HP and gained 61 Gold.", PageText(run), "the page says what happened");
            c.Equal(0, Rewards(run), "no number of rewards");
            c.Equal(10, run.State.Deck.Count, "the deck is as it was");
            c.Sequence(["BurningBlood"], run.State.Relics.Select(relic => relic.ClassName), "no relic");
            Do(run, "option:0");
            c.Equal("outside", run.Snapshot().Screen, "leaving finishes the room");
            c.Equal(1, ((Room)run.Director).Left, "once");
            c.That(EventScreen.Current(run) is null, "and the run is in no event any more");
        });

        c.Group("This or That?: This is listed as deadly at 6 HP or less, and not offered", () =>
        {
            var run = Open("ThisOrThat", hp: 6);
            c.Equal(Q("{'n':0,'title':'This','text':'Lose 6 HP. Gain 61 Gold.','will_kill_player':true,'note':'would kill you; not offered'}"),
                Text(run.Snapshot().State["options"]![0]), "at 6 HP");
            c.Equal("option:1", Ids(run.Snapshot()), "only That is offered");
            c.Equal("This would kill you.", Refusal(run, "option:0"), "the refusal");
            c.Equal((6, 99), (run.Hp, run.Gold), "nothing happened");

            run = Open("ThisOrThat", hp: 7);
            c.Equal("option:0 option:1", Ids(run.Snapshot()), "at 7 HP both are offered");
            Do(run, "option:0");
            c.Equal((1, 160), (run.Hp, run.Gold), "7 - 6 = 1 HP");
        });

        c.Group("This or That?: That gives a relic of a rolled rarity from the front of the player's bag, then the curse Clumsy", () =>
        {
            // A new run on the seed: the first number of rewards rolls Common (an independent reading of the stream: 0.0585),
            // and the front of the saved Common list is Regal Pillow, then Strawberry.
            var run = Open("ThisOrThat");
            Do(run, "option:1");
            c.Equal(1, Rewards(run), "one number of rewards, for the rarity");
            var (relic, passedOver) = FirstBuilt(SavedCommonFront);
            c.Sequence(["BurningBlood", relic], run.State.Relics.Select(held => held.ClassName), "the relic: the first of the saved Common list that the arena holds");
            c.Sequence(passedOver, LoggedAs(run, "relic"), "the game's own relics before it are in the run's log");
            c.Sequence(passedOver.Select(_ => "the relic of This or That?"), Logged(run).Select(entry => entry.What), "each named as this event's relic");
            c.That(!World(run).PlayerBag.Contains(relic) && !World(run).SharedBag.Contains(relic), "the relic has left both bags");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash Clumsy",
                DeckNames(run), "Clumsy is the deck's last card");
            c.Equal(99, run.Gold, "no gold");
            c.Equal(ProceedOnly, Options(run), "the event is finished");
            c.Equal($"You took the ornate one: you obtained {TitleOfRelic(relic)}. Clumsy was added to your Deck.", PageText(run), "the page names the relic and the curse");
            if (relic == "Strawberry") c.Equal((87, 87), (run.Hp, run.MaxHp), "Strawberry's pickup effect ran: 7 max HP, and 7 HP with it");
        });
    }

    private static void TheRealRunsRelic(Check c)
    {
        c.Group("the real run's event on seed 1BUSLN0FY5: That gave Stone Cracker, the front of the player's Uncommon list, on the 26th number of rewards", () =>
        {
            // The run reached the event on floor 4 with rewards at 25 (two fights' rewards) and nothing pulled from the bag before.
            var run = Open("ThisOrThat", room: new Room { RewardsBefore = 25 });
            c.Equal("Lose 6 HP. Gain 61 Gold.", run.Snapshot().State["options"]![0]!["text"]!.GetValue<string>(), "the event rolls the same whatever was drawn before: its stream is its own");
            Do(run, "option:1");
            c.Equal(26, Rewards(run), "the rarity is the 26th number of rewards, as the save made on entering floor 5 has it");
            var (relic, passedOver) = FirstBuilt(SavedUncommon);
            c.Sequence(["BurningBlood", relic], run.State.Relics.Select(held => held.ClassName),
                "an Uncommon relic: Stone Cracker itself once the arena holds it, until then the first relic of the saved list that it holds");
            c.Sequence(passedOver, LoggedAs(run, "relic"), "and every relic of the game's list before it is logged as passed over, Stone Cracker first");
            c.Equal(passedOver.Length > 0 ? 4 : null, run.State.FindPart<LeftOutLog>()?.FirstFloor, "which makes floor 4 the first floor on which the run is not the game's own");
            c.Equal("Clumsy", run.State.Deck[^1].Def.ClassName, "the curse of the record's cards_gained");
            c.Equal(11, run.State.Deck.Count, "eleven cards");
            c.Equal((80, 99), (run.Hp, run.Gold), "HP and gold as the record has them: nothing lost, nothing gained");
        });

        c.Group("the same in a strict run: the game's relic or the end of the run", () =>
        {
            var setup = new RunSetup { Seed = RealSeed };
            var run = Open("ThisOrThat", setup, new Room { RewardsBefore = 25, Strict = true });
            var before = run.State.Dump().ToJsonString();
            Do(run, "option:1");
            if (RunRelics.IsBuilt("StoneCracker"))
            {
                c.Sequence(["BurningBlood", "StoneCracker"], run.State.Relics.Select(held => held.ClassName), "Stone Cracker is built: the strict run has the game's relic");
                c.Equal("Clumsy", run.State.Deck[^1].Def.ClassName, "and the curse");
                return;
            }
            c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, "Stone Cracker is not built: the run ends as unrepresentable");
            c.That(run.ResultReason is { } reason && reason.StartsWith("the relic of This or That? on floor 4: the game gives the relic StoneCracker here, which the arena does not hold", StringComparison.Ordinal),
                $"the reason names where the relic was to come from, the game's relic, and that the pull itself gave nothing ({run.ResultReason})");
            c.Equal(before, run.State.Dump().ToJsonString(), "standing as before the action: no number drawn, no curse added");
            c.Equal(run.Dump(), Run.Replay(setup, new Room { Event = "ThisOrThat", RewardsBefore = 25, Strict = true }, run.Actions).Dump(), "and the save replays to the same end");
        });
    }

    private static void RelicPulls(Check c)
    {
        c.Group("a random relic whose rolled rarity holds nothing the arena has: the rarity is rolled again, and the game's relic is logged", () =>
        {
            // With every built Uncommon relic out of the bag, the 26th number (Uncommon) lands on a list the arena holds nothing of.
            // The 27th number of rewards on this seed rolls Common (an independent reading of the stream: 0.0703).
            var gameRelic = SavedUncommon.FirstOrDefault(relic => !RunRelics.IsBuilt(relic));
            if (gameRelic is null)
            {
                c.That(true, "every Uncommon relic of the saved list is built: nothing is rolled again any more");
                return;
            }
            var run = Open("UnrestSite", new RunSetup { Seed = RealSeed, Hp = 50 }, new Room { RewardsBefore = 25, Without = RelicRarity.Uncommon });
            Do(run, "option:1");
            c.Equal(27, Rewards(run), "two numbers of rewards: the rarity, and the rarity again");
            var (relic, passedOver) = FirstBuilt(SavedCommonFront);
            c.Sequence(["BurningBlood", relic], run.State.Relics.Select(held => held.ClassName), "a Common relic: the first of the saved Common list that the arena holds");
            c.Sequence(new[] { gameRelic }.Concat(passedOver), LoggedAs(run, "relic"), "the log: the game's Uncommon relic first, then the Common ones passed over");
            c.Equal("the relic of Unrest Site, whose rarity was then rolled again", Logged(run)[0].What, "the first entry says the rarity was rolled again");
            c.That(Logged(run).Skip(1).All(entry => entry.What == "the relic of Unrest Site"), "the others are plain pass-overs");
        });

        c.Group("a random relic in keyed mode: the rarity has a stream of its place, and the pull works the same", () =>
        {
            var run = Open("ThisOrThat", room: new Room { Mode = StreamMode.Keyed });
            Do(run, "option:1");
            c.Equal(0, Rewards(run), "the long rewards stream is not touched in keyed mode");
            c.Equal(2, run.State.Relics.Count, "a relic was obtained");
            c.That(RunRelics.IsBuilt(run.State.Relics[1].ClassName) && GameLists.IsBagRelic(run.State.Relics[1].ClassName), "one of the bag that the arena holds");
            c.Equal("Clumsy", run.State.Deck[^1].Def.ClassName, "and the curse");
        });
    }

    // ====================================================================================
    // The Sunken Statue
    // ====================================================================================

    private static void SunkenStatueChecks(Check c)
    {
        c.Group("The Sunken Statue: the page, and the gold it rolls", () =>
        {
            var run = Open("SunkenStatue");
            var s = run.Snapshot();
            c.Equal("The Sunken Statue", s.State["event"]!.GetValue<string>(), "the title");
            // 107 on this seed: 111 plus a whole number from -10 to 10.
            c.Equal(Q("[{'n':0,'title':'Grab the Sword','text':'Obtain Sword of Stone.','relic':{'name':'Sword of Stone','text':'After 5 won Elite fights it turns into Sword of Jade (at the start of every fight: gain 3 Strength). Won so far: 0.'}},") +
                    Q("{'n':1,'title':'Dive into the Water','text':'Gain 107 Gold. Lose 7 HP.'}]"),
                Options(run), "two options: the sword with the relic in its tooltip, the dive with the rolled gold and its price");
            c.Equal("option:0=Choose Grab the Sword | option:1=Choose Dive into the Water", Labels(s), "one action each");
            c.Equal(1, Ev(run).Rng.Counter, "the start took one number of the event's stream");

            int[] gold = [107, 120, 104, 107, 114, 117, 121];
            for (var i = 0; i < Seeds.Length; i++)
                c.Equal(gold[i], ((SunkenStatue)Ev(Open("SunkenStatue", 80, seed: Seeds[i]))).Gold, $"seed {Seeds[i]}: the gold");

            var seen = new HashSet<int>();
            var maker = new GameRng(16);
            for (var n = 0; n < 3000; n++) seen.Add(((SunkenStatue)Ev(Open("SunkenStatue", 80, seed: RunSeed.Random(maker).Text))).Gold);
            c.Equal((101, 121, 21), (seen.Min(), seen.Max(), seen.Count), "over 3,000 seeds the gold is every whole number from 101 to 121 and nothing else");
        });

        c.Group("The Sunken Statue: Grab the Sword gives Sword of Stone", () =>
        {
            var run = Open("SunkenStatue");
            Do(run, "option:0");
            c.Sequence(["BurningBlood", "SwordOfStone"], run.State.Relics.Select(relic => relic.ClassName), "the sword is the player's second relic");
            c.Equal((80, 99, 0), (run.Hp, run.Gold, Rewards(run)), "no HP, no gold, no number of rewards");
            c.Equal(ProceedOnly, Options(run), "the event is finished");
            c.Equal("You took the sword: you obtained Sword of Stone.", PageText(run), "the page says so");
            c.Sequence(["Burning Blood", "Sword of Stone"], run.Snapshot().State["player"]!["relics"]!.AsArray().Select(name => name!.GetValue<string>()), "the player block lists it");
        });

        c.Group("The Sunken Statue: Dive into the Water pays the gold and costs 7 HP", () =>
        {
            var run = Open("SunkenStatue");
            Do(run, "option:1");
            c.Equal((73, 206), (run.Hp, run.Gold), "80 - 7 HP, 99 + 107 gold");
            c.Equal("You dived for the coins: you gained 107 Gold and lost 7 HP.", PageText(run), "the page says what happened");
            c.Sequence(["BurningBlood"], run.State.Relics.Select(relic => relic.ClassName), "no sword");

            run = Open("SunkenStatue", hp: 7);
            c.Equal(Q("{'n':1,'title':'Dive into the Water','text':'Gain 107 Gold. Lose 7 HP.','will_kill_player':true,'note':'would kill you; not offered'}"),
                Text(run.Snapshot().State["options"]![1]), "at 7 HP the dive is listed as deadly");
            c.Equal("option:0", Ids(run.Snapshot()), "and only the sword is offered");
            c.Equal("Dive into the Water would kill you.", Refusal(run, "option:1"), "the refusal");

            run = Open("SunkenStatue", hp: 8);
            Do(run, "option:1");
            c.Equal((1, 206), (run.Hp, run.Gold), "at 8 HP it is offered and leaves 1 HP");
        });
    }

    private static void TheRealRecordOfTheStatue(Check c)
    {
        c.Group("the game's record of a co-op run at The Sunken Statue: both players dived, for 112 and 121 gold, which is what the seed gives each", () =>
        {
            // history/1783973586.run (v0.108.0, two players): floor 3 of act 1, EVENT.SUNKEN_STATUE, DIVE_INTO_WATER for both;
            // gold_gained 112 and 121, damage_taken 7 each. Every player has an own copy of the event on a stream of the own slot.
            c.Equal(112, SunkenStatue.RollGold(EventStream(StatueSeed, "SUNKEN_STATUE", slot: 0)), "the first player's gold");
            c.Equal(121, SunkenStatue.RollGold(EventStream(StatueSeed, "SUNKEN_STATUE", slot: 1)), "the second player's gold");

            // The first player's event, through the screen: a single-player run is the player of slot 0.
            var run = Open("SunkenStatue", new RunSetup { Seed = StatueSeed, Hp = 59, MaxHp = 83, Gold = 106 });
            c.Equal("Gain 112 Gold. Lose 7 HP.", run.Snapshot().State["options"]![1]!["text"]!.GetValue<string>(), "the dive as the first player saw it");
            Do(run, "option:1");
            c.Equal((52, 83, 218), (run.Hp, run.MaxHp, run.Gold), "the record's numbers after the room: 52 of 83 HP, 218 gold");
        });
    }

    // ====================================================================================
    // Jungle Maze Adventure
    // ====================================================================================

    private static void JungleMaze(Check c)
    {
        c.Group("Jungle Maze Adventure: the page, and the two amounts of gold it rolls", () =>
        {
            var run = Open("JungleMazeAdventure");
            var s = run.Snapshot();
            c.Equal("Jungle Maze Adventure", s.State["event"]!.GetValue<string>(), "the title");
            c.Equal(Q("[{'n':0,'title':'Solo Quest','text':'Gain 164 Gold. Lose 18 HP.'},{'n':1,'title':'Join Forces','text':'Gain 56 Gold.'}]"), Options(run),
                "two options with the gold each pays: the whole-number part of what was rolled");
            c.Equal("option:0=Choose Solo Quest | option:1=Choose Join Forces", Labels(s), "one action each");
            var ev = (JungleMazeAdventure)Ev(run);
            // 150 and 50, each plus a 32-bit fraction from -15 up to 15 that the game turns into a decimal (seven digits).
            c.Equal((164.66355m, 56.706297m), (ev.SoloGoldRolled, ev.JoinForcesGoldRolled), "the amounts as rolled on this seed: the first two numbers of the event's stream");
            c.Equal(2, ev.Rng.Counter, "two numbers");
            // The game scales the number in 64 bits and narrows it to 32 afterwards. On this seed a fraction scaled in 32 bits would
            // come out one digit off (156.585594 and 57.213600).
            var fine = (JungleMazeAdventure)Ev(Open("JungleMazeAdventure", 80, seed: "A000000003"));
            c.Equal((156.585593m, 57.213601m), (fine.SoloGoldRolled, fine.JoinForcesGoldRolled), "the amounts on a seed that tells the two ways of scaling apart");

            (int Solo, int Join)[] paid = [(164, 56), (157, 38), (149, 50), (141, 57), (140, 53), (152, 64), (142, 51)];
            for (var i = 0; i < Seeds.Length; i++)
            {
                var other = (JungleMazeAdventure)Ev(Open("JungleMazeAdventure", 80, seed: Seeds[i]));
                c.Equal(paid[i], ((int)other.SoloGoldRolled, (int)other.JoinForcesGoldRolled), $"seed {Seeds[i]}: what Solo Quest and Join Forces pay");
            }

            var solo = new HashSet<int>();
            var join = new HashSet<int>();
            var maker = new GameRng(17);
            for (var n = 0; n < 3000; n++)
            {
                var other = (JungleMazeAdventure)Ev(Open("JungleMazeAdventure", 80, seed: RunSeed.Random(maker).Text));
                solo.Add((int)other.SoloGoldRolled);
                join.Add((int)other.JoinForcesGoldRolled);
            }
            c.Equal((135, 164, 30), (solo.Min(), solo.Max(), solo.Count), "over 3,000 seeds Solo Quest pays every whole number from 135 to 164 and nothing else");
            c.Equal((35, 64, 30), (join.Min(), join.Max(), join.Count), "and Join Forces every one from 35 to 64");
        });

        c.Group("Jungle Maze Adventure: Solo Quest costs 18 HP and pays its gold; Join Forces pays less for nothing", () =>
        {
            var run = Open("JungleMazeAdventure");
            Do(run, "option:0");
            c.Equal((62, 263), (run.Hp, run.Gold), "80 - 18 HP, 99 + 164 gold");
            c.Equal("You went in alone: you lost 18 HP and gained 164 Gold.", PageText(run), "the page says what happened");
            c.Equal(ProceedOnly, Options(run), "the event is finished");
            c.Equal(4, Ev(run).Rng.Counter, "the game's shuffle of its three sound effects took two more numbers of the event's stream");

            run = Open("JungleMazeAdventure");
            Do(run, "option:1");
            c.Equal((80, 155), (run.Hp, run.Gold), "99 + 56 gold, no HP lost");
            c.Equal("You went in with the others and gained 56 Gold.", PageText(run), "the page says what happened");
            c.Equal(2, Ev(run).Rng.Counter, "no further number");
            c.Equal(0, Rewards(run), "and none of rewards");

            run = Open("JungleMazeAdventure", hp: 18);
            c.Equal(Q("{'n':0,'title':'Solo Quest','text':'Gain 164 Gold. Lose 18 HP.','will_kill_player':true,'note':'would kill you; not offered'}"),
                Text(run.Snapshot().State["options"]![0]), "at 18 HP Solo Quest is listed as deadly");
            c.Equal("option:1", Ids(run.Snapshot()), "and only Join Forces is offered");

            run = Open("JungleMazeAdventure", hp: 19);
            Do(run, "option:0");
            c.Equal((1, 263), (run.Hp, run.Gold), "at 19 HP it is offered and leaves 1 HP");
        });
    }

    // ====================================================================================
    // Unrest Site
    // ====================================================================================

    private static void UnrestSiteChecks(Check c)
    {
        c.Group("Unrest Site: the page, with the HP that is missing", () =>
        {
            var run = Open("UnrestSite", hp: 50);
            var s = run.Snapshot();
            c.Equal("Unrest Site", s.State["event"]!.GetValue<string>(), "the title");
            c.Equal(Q("[{'n':0,'title':'Rest Anyways','text':'Heal 30 HP. Add Poor Sleep to your Deck.','cards':[") + CardJson("PoorSleep") +
                    Q("]},{'n':1,'title':'Kill the Trees','text':'Lose 8 Max HP. Obtain a random Relic.'}]"),
                Options(run), "two options: the rest with what it heals (80 - 50) and the curse in its tooltip, the trees with their price");
            c.Equal("option:0=Choose Rest Anyways | option:1=Choose Kill the Trees", Labels(s), "one action each");
            c.Equal(0, Ev(run).Rng.Counter, "the event rolls nothing");
        });

        c.Group("Unrest Site: Rest Anyways heals what was missing and adds Poor Sleep", () =>
        {
            var run = Open("UnrestSite", hp: 50);
            Do(run, "option:0");
            c.Equal((80, 80), (run.Hp, run.MaxHp), "healed to 80");
            c.Equal("PoorSleep", run.State.Deck[^1].Def.ClassName, "Poor Sleep is the deck's last card");
            c.Equal(11, run.State.Deck.Count, "eleven cards");
            c.Equal("You rested anyway and healed 30 HP. Poor Sleep was added to your Deck.", PageText(run), "the page says what happened");
            c.Equal(ProceedOnly, Options(run), "the event is finished");
            c.Equal(0, Rewards(run), "no number of rewards");

            // The amount is noted when the event starts: a Blood Potion drunk on the page (16 HP) leaves 14 to heal.
            run = Open("UnrestSite", new RunSetup { Seed = RealSeed, Hp = 50, Potions = ["BloodPotion"] });
            c.Equal("option:0 option:1 potion:0 discard_potion:0", Ids(run.Snapshot()), "a potion can be used on the page");
            Do(run, "potion:0");
            c.Equal(66, run.Hp, "the Blood Potion heals 20% of 80");
            c.Equal("Heal 30 HP. Add Poor Sleep to your Deck.", run.Snapshot().State["options"]![0]!["text"]!.GetValue<string>(), "the option still names the amount noted at the start");
            Do(run, "option:0");
            c.Equal(80, run.Hp, "the heal stops at max HP");
            c.Equal("You rested anyway and healed 14 HP. Poor Sleep was added to your Deck.", PageText(run), "and the page says what was really healed");

            // HP lost after the start is not healed: a relic of the checks costs 10 HP when the room is entered, which is after
            // the event noted 30. 50 - 10 + 30 = 70, not 80.
            run = Open("UnrestSite", new RunSetup { Seed = RealSeed, Hp = 50, ExtraRelics = [static () => new Thorn()] });
            c.Equal(40, run.Hp, "the relic acted on entering: 40 HP");
            c.Equal("Heal 30 HP. Add Poor Sleep to your Deck.", run.Snapshot().State["options"]![0]!["text"]!.GetValue<string>(), "the page was made before: it names the 30 that were missing at the start");
            Do(run, "option:0");
            c.Equal(70, run.Hp, "Rest heals the noted 30, not what is missing now");
        });

        c.Group("Unrest Site: Kill the Trees costs 8 max HP and gives a random relic", () =>
        {
            // A new run on the seed: the first number of rewards rolls Common; the front of the saved Common list is Regal Pillow, then Strawberry.
            var run = Open("UnrestSite", hp: 50);
            Do(run, "option:1");
            var (relic, passedOver) = FirstBuilt(SavedCommonFront);
            c.Sequence(["BurningBlood", relic], run.State.Relics.Select(held => held.ClassName), "the relic: the first of the saved Common list that the arena holds");
            c.Sequence(passedOver, LoggedAs(run, "relic"), "the ones before it are logged");
            c.Equal(1, Rewards(run), "one number of rewards");
            // 80 - 8 = 72 max HP; 50 HP is below it, so no HP is lost. Strawberry then gives 7 max HP and 7 HP.
            var strawberry = relic == "Strawberry" ? 7 : 0;
            c.Equal((50 + strawberry, 72 + strawberry), (run.Hp, run.MaxHp), "max HP 72 and HP untouched, before what the relic's own pickup gives");
            c.Equal($"You cut the trees down: you lost 8 Max HP and obtained {TitleOfRelic(relic)}.", PageText(run), "the page says what happened");
            c.Equal(10, run.State.Deck.Count, "no curse");

            // At full HP the HP above the new maximum is lost with it.
            run = Open("UnrestSite", new RunSetup { Seed = RealSeed }, new Room { RewardsBefore = 25 });
            Do(run, "option:1");
            c.Equal((72, 72), (run.Hp, run.MaxHp), "80 of 80 becomes 72 of 72 (the 26th number gives an Uncommon relic; the first ones of the saved list change no HP at pickup)");
            c.Equal(FirstBuilt(SavedUncommon).Given, run.State.Relics[^1].ClassName, "the relic of the saved Uncommon list");
        });

        c.Group("Unrest Site: Kill the Trees is listed as deadly at 8 max HP or less", () =>
        {
            var run = Open("UnrestSite", hp: 5, maxHp: 8);
            c.Equal(Q("{'n':1,'title':'Kill the Trees','text':'Lose 8 Max HP. Obtain a random Relic.','will_kill_player':true,'note':'would kill you; not offered'}"),
                Text(run.Snapshot().State["options"]![1]), "at 8 max HP");
            c.Equal("option:0", Ids(run.Snapshot()), "only the rest is offered");
            c.Equal("Kill the Trees would kill you.", Refusal(run, "option:1"), "the refusal");

            run = Open("UnrestSite", new RunSetup { Seed = RealSeed, Hp = 5, MaxHp = 9 }, new Room { RewardsBefore = 25 });
            c.Equal("option:0 option:1", Ids(run.Snapshot()), "at 9 max HP both are offered");
            Do(run, "option:1");
            c.Equal((1, 1), (run.Hp, run.MaxHp), "9 - 8 = 1 max HP, and the 5 HP are cut to it");
        });
    }

    // ====================================================================================
    // Tablet of Truth
    // ====================================================================================

    private static void TabletOfTruthChecks(Check c)
    {
        c.Group("Tablet of Truth: the first page, and Smash", () =>
        {
            var run = Open("TabletOfTruth", hp: 50);
            var s = run.Snapshot();
            c.Equal("Tablet of Truth", s.State["event"]!.GetValue<string>(), "the title");
            c.Equal(Q("[{'n':0,'title':'Decipher','text':'Lose 3 Max HP. Upgrade a random card of your Deck.'},{'n':1,'title':'Smash','text':'Heal 20 HP.'}]"), Options(run), "two options");
            c.Equal("option:0=Choose Decipher | option:1=Choose Smash", Labels(s), "one action each");
            c.Equal(0, Ev(run).Rng.Counter, "the event rolls nothing at its start");
            Do(run, "option:1");
            c.Equal((70, 80), (run.Hp, run.MaxHp), "Smash heals 20");
            c.Equal("You smashed the tablet and healed 20 HP.", PageText(run), "the page says so");
            c.Equal(ProceedOnly, Options(run), "the event is finished");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash", DeckNames(run), "no card upgraded");

            run = Open("TabletOfTruth", hp: 75);
            Do(run, "option:1");
            c.Equal(80, run.Hp, "the heal stops at max HP");
            c.Equal("You smashed the tablet and healed 5 HP.", PageText(run), "and the page says what was really healed");
        });

        c.Group("Tablet of Truth: five times Decipher, for 3, 6, 12, 24 and all max HP but 1", () =>
        {
            var setup = new RunSetup { Seed = RealSeed };
            var run = Open("TabletOfTruth", setup);

            // The cards the four random upgrades land on, on this seed: an independent reading of the event's stream, each number a pick
            // among the cards of the deck that can still be upgraded, in deck order (10, 9, 8, then 7 of them). By place in the deck: 5, 3, 1, 0.
            string[] decks =
            [
                "StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad+ DefendIronclad DefendIronclad DefendIronclad Bash",
                "StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad+ StrikeIronclad DefendIronclad+ DefendIronclad DefendIronclad DefendIronclad Bash",
                "StrikeIronclad StrikeIronclad+ StrikeIronclad StrikeIronclad+ StrikeIronclad DefendIronclad+ DefendIronclad DefendIronclad DefendIronclad Bash",
                "StrikeIronclad+ StrikeIronclad+ StrikeIronclad StrikeIronclad+ StrikeIronclad DefendIronclad+ DefendIronclad DefendIronclad DefendIronclad Bash",
            ];
            int[] maxHp = [77, 71, 59, 35];
            int[] cost = [3, 6, 12, 24];
            int[] next = [6, 12, 24, 34];
            string[] upgraded = ["Defend", "Strike", "Strike", "Strike"];
            string[] titles = ["Continue Deciphering", "Keep Deciphering", "KEEP DECIPHERING", "Lose Everything"];
            for (var n = 0; n < 4; n++)
            {
                Do(run, "option:0");
                c.Equal((maxHp[n], maxHp[n]), (run.Hp, run.MaxHp), $"Decipher {n + 1}: {cost[n]} max HP less, and the HP above the new maximum with it");
                c.Equal(decks[n], DeckNames(run), $"Decipher {n + 1}: one card upgraded");
                c.Equal(n + 1, Ev(run).Rng.Counter, $"Decipher {n + 1}: one number of the event's stream");
                c.Equal($"You read on and lost {cost[n]} Max HP. {upgraded[n]} was upgraded to {upgraded[n]}+.", PageText(run), $"Decipher {n + 1}: the page says what happened");
                var effect = n == 3 ? "Upgrade ALL cards of your Deck." : "Upgrade a random card of your Deck.";
                c.Equal(Q($"[{{'n':0,'title':'{titles[n]}','text':'Lose {next[n]} Max HP. {effect}'}},{{'n':1,'title':'Give Up','text':'Stop reading and leave.'}}]"), Options(run),
                    $"page {n + 2}: Decipher under its next title at its next price, and Give Up");
                c.Equal($"option:0=Choose {titles[n]} | option:1=Choose Give Up", Labels(run.Snapshot()), $"page {n + 2}: one action each");
                c.That(run.Snapshot().State["finished"] is null, $"page {n + 2}: the event is not finished");
            }

            // The fifth: all max HP but 1 (35 - 1 = 34), every card that can be upgraded, no number drawn, and the event is over.
            Do(run, "option:0");
            c.Equal((1, 1), (run.Hp, run.MaxHp), "Decipher 5: 1 max HP is left");
            c.Equal("StrikeIronclad+ StrikeIronclad+ StrikeIronclad+ StrikeIronclad+ StrikeIronclad+ DefendIronclad+ DefendIronclad+ DefendIronclad+ DefendIronclad+ Bash+", DeckNames(run), "Decipher 5: the whole deck is upgraded");
            c.Equal(4, Ev(run).Rng.Counter, "Decipher 5: no number of the event's stream");
            c.Equal("You read on and lost 34 Max HP. Every card of your Deck that could be was upgraded (6).", PageText(run), "Decipher 5: the page says what happened");
            c.Equal(ProceedOnly, Options(run), "the event is finished");
            c.Equal((99, 0), (run.Gold, Rewards(run)), "no gold moved and no number of rewards was drawn");
            Do(run, "option:0");
            c.Equal(1, ((Room)run.Director).Left, "leaving finishes the room");
            c.Equal(run.Dump(), Run.Replay(setup, new Room { Event = "TabletOfTruth" }, run.Actions).Dump(), "the whole reading replays from its action list");
        });

        c.Group("Tablet of Truth: Give Up, and a player below max HP", () =>
        {
            var run = Open("TabletOfTruth", hp: 50);
            Do(run, "option:0");
            c.Equal((50, 77), (run.Hp, run.MaxHp), "50 HP is below the new maximum of 77: no HP is lost");
            Do(run, "option:1");
            c.Equal("You stopped reading and left the tablet.", PageText(run), "Give Up");
            c.Equal(ProceedOnly, Options(run), "finishes the event");
            c.Equal((50, 77, 1), (run.Hp, run.MaxHp, Ev(run).Rng.Counter), "and changes nothing");
        });

        c.Group("Tablet of Truth: Decipher is listed as deadly while max HP is at or below its price", () =>
        {
            var run = Open("TabletOfTruth", hp: 3, maxHp: 3);
            c.Equal(Q("{'n':0,'title':'Decipher','text':'Lose 3 Max HP. Upgrade a random card of your Deck.','will_kill_player':true,'note':'would kill you; not offered'}"),
                Text(run.Snapshot().State["options"]![0]), "at 3 max HP the first Decipher would kill");
            c.Equal("option:1", Ids(run.Snapshot()), "only Smash is offered");
            c.Equal("Decipher would kill you.", Refusal(run, "option:0"), "the refusal");

            // It is the max HP that counts, not the HP: at 2 HP of 80 Decipher is offered and costs no HP.
            run = Open("TabletOfTruth", hp: 2);
            c.Equal("option:0 option:1", Ids(run.Snapshot()), "at 2 HP of 80 max HP the first Decipher is offered");
            Do(run, "option:0");
            c.Equal((2, 77), (run.Hp, run.MaxHp), "and leaves 2 HP of 77");

            run = Open("TabletOfTruth", hp: 9, maxHp: 9);
            c.Equal("option:0 option:1", Ids(run.Snapshot()), "at 9 max HP the first Decipher is offered");
            Do(run, "option:0");
            c.Equal((6, 6), (run.Hp, run.MaxHp), "9 - 3 = 6 max HP");
            c.Equal(Q("{'n':0,'title':'Continue Deciphering','text':'Lose 6 Max HP. Upgrade a random card of your Deck.','will_kill_player':true,'note':'would kill you; not offered'}"),
                Text(run.Snapshot().State["options"]![0]), "the second, at 6 max HP for a price of 6, would kill");
            c.Equal("option:1=Choose Give Up", Labels(run.Snapshot()), "only Give Up is offered");
            c.Equal("Continue Deciphering would kill you.", Refusal(run, "option:0"), "the refusal");

            // Asked each time the page is read: with 5 more max HP (Fruit Juice) the same option can be chosen.
            run = Open("TabletOfTruth", new RunSetup { Seed = RealSeed, Hp = 9, MaxHp = 9, Potions = ["FruitJuice"] });
            Do(run, "option:0");
            c.Equal("option:1 potion:0 discard_potion:0", Ids(run.Snapshot()), "deadly at 6 max HP; the potion rides along with Give Up");
            Do(run, "potion:0");
            c.Equal("option:0 option:1", Ids(run.Snapshot()), "at 11 max HP the second Decipher is offered");
            Do(run, "option:0");
            c.Equal((5, 5), (run.Hp, run.MaxHp), "11 - 6 = 5 max HP");
        });

        c.Group("Tablet of Truth: the last price is counted from the max HP the player had when its page was made", () =>
        {
            var run = Open("TabletOfTruth", new RunSetup { Seed = RealSeed, Potions = ["FruitJuice"] });
            Do(run, "option:0", "option:0", "option:0", "option:0");
            c.Equal("Lose 34 Max HP. Upgrade ALL cards of your Deck.", run.Snapshot().State["options"]![0]!["text"]!.GetValue<string>(), "at 35 max HP the last Decipher costs 34");
            Do(run, "potion:0");
            c.Equal((40, 40), (run.Hp, run.MaxHp), "Fruit Juice: 5 max HP more");
            c.Equal("Lose 34 Max HP. Upgrade ALL cards of your Deck.", run.Snapshot().State["options"]![0]!["text"]!.GetValue<string>(), "the price stays 34");
            Do(run, "option:0");
            c.Equal((6, 6), (run.Hp, run.MaxHp), "40 - 34 = 6 max HP are left, not 1");
        });

        c.Group("Tablet of Truth: decks with little or nothing to upgrade", () =>
        {
            // One card that can be upgraded: the pick still takes a number. None: no number.
            var run = Open("TabletOfTruth", new RunSetup { Seed = RealSeed, Deck = [new DeckCard("Bash"), new DeckCard("Clumsy")] });
            Do(run, "option:0");
            c.Equal("Bash+ Clumsy", DeckNames(run), "the one card that can be upgraded is");
            c.Equal(1, Ev(run).Rng.Counter, "with one number of the event's stream, though the pick is forced");
            Do(run, "option:0");
            c.Equal("Bash+ Clumsy", DeckNames(run), "nothing is left to upgrade");
            c.Equal(1, Ev(run).Rng.Counter, "and no number is drawn for nothing");
            c.Equal("You read on and lost 6 Max HP. No card of your Deck could be upgraded.", PageText(run), "the page says so");
            c.Equal((71, 71), (run.Hp, run.MaxHp), "the max HP is lost all the same");
            Do(run, "option:0", "option:0", "option:0");
            c.Equal((1, 1, 1), (run.Hp, run.MaxHp, Ev(run).Rng.Counter), "read to the end: 1 max HP, and still one number");
            c.Equal("You read on and lost 34 Max HP. No card of your Deck could be upgraded.", PageText(run), "the last Decipher with nothing left to upgrade");
            c.Equal(ProceedOnly, Options(run), "finishes the event all the same");
        });

        c.Group("Tablet of Truth: the game's branch for a Decipher that costs all the max HP there is (the screen never offers it)", () =>
        {
            var run = Open("TabletOfTruth", hp: 3, maxHp: 3);
            var ev = Ev(run);
            ev.Choose(run, ev.Options[0]);
            c.That(run.IsOver && run.Result == RunResult.Died, "max HP goes to 1 and the player is killed: the run is over");
            c.Equal((0, 1), (run.State.Hp, run.State.MaxHp), "0 HP of 1 max HP");
            c.Equal("game_over", run.Snapshot().Screen, "the mod's game over screen is up");
            c.Equal("Event", run.Snapshot().State["room"]!.GetValue<string>(), "in an Event room");

            // With a Fairy in a Bottle the death is prevented (it heals 30% of max HP, at least 1), and the game's method goes on as
            // after any Decipher: the count goes up and the next page is made.
            run = Open("TabletOfTruth", new RunSetup { Seed = RealSeed, Hp = 3, MaxHp = 3, Potions = ["FairyInABottle"] });
            ev = Ev(run);
            ev.Choose(run, ev.Options[0]);
            c.That(!run.IsOver, "with a Fairy in a Bottle the player lives");
            c.Equal((1, 1), (run.State.Hp, run.State.MaxHp), "at 1 HP of 1 max HP");
            c.Sequence(new string?[] { null, null, null }, run.State.Belt.Slots.Select(potion => potion?.ClassName), "the fairy is used up");
            c.Equal((1, 6m), (((TabletOfTruth)ev).DecipherCount, ((TabletOfTruth)ev).DecipherCost), "the Decipher counts, and the next one costs 6");
            c.Equal("DECIPHER GIVE_UP", string.Join(" ", ev.Options.Select(option => option.Key)), "the second page is up");
            c.Equal(0, ev.Rng.Counter, "no card was upgraded and no number drawn");
        });
    }

    // ====================================================================================
    // Aroma of Chaos
    // ====================================================================================

    private static void AromaOfChaosChecks(Check c)
    {
        c.Group("Aroma of Chaos: the page", () =>
        {
            var run = Open("AromaOfChaos");
            var s = run.Snapshot();
            c.Equal("Aroma of Chaos", s.State["event"]!.GetValue<string>(), "the title");
            c.Equal(Q("[{'n':0,'title':'Let Go','text':'Transform a card of your Deck: you choose it, and it becomes a random card.'},") +
                    Q("{'n':1,'title':'Maintain Control','text':'Upgrade a card of your Deck: you choose it.'}]"), Options(run), "two options");
            c.Equal("option:0=Choose Let Go | option:1=Choose Maintain Control", Labels(s), "one action each");
            c.Equal(0, Ev(run).Rng.Counter, "the event rolls nothing at its start");
        });

        c.Group("Aroma of Chaos: Let Go asks for a card on the transform screen and turns it into a random card on the event's stream", () =>
        {
            var setup = new RunSetup { Seed = RealSeed };
            var run = Open("AromaOfChaos", setup);
            Do(run, "option:0");
            var s = run.Snapshot();
            c.Equal("card_select", s.Screen, "the choice is up");
            c.Equal(("transform", "Choose a card to transform.", 1, 1), (s.State["kind"]!.GetValue<string>(), s.State["prompt"]!.GetValue<string>(), s.State["min"]!.GetValue<int>(), s.State["max"]!.GetValue<int>()),
                "the transform screen, for exactly one card");
            c.Equal("card:0 card:5 card:9", Ids(s), "the deck's cards, equal ones once; no way to back out of the choice");
            Do(run, "card:0");
            c.Equal("confirm=Confirm: transform Strike | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "the preview: confirm, or back to the cards");
            Do(run, "confirm");

            // The first number of the event's stream on this seed picks place 18 of the 80 cards a Strike can become
            // (the Ironclad's Common, Uncommon and Rare cards a single-player run can hold, in the pool's order): Crimson Mantle.
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash CrimsonMantle", DeckNames(run),
                "the Strike is gone and Crimson Mantle is the deck's last card");
            c.Equal("You let go. Strike became Crimson Mantle.", PageText(run), "the page names both");
            c.Equal(ProceedOnly, Options(run), "the event is finished");
            c.Equal(1, Ev(run).Rng.Counter, "one number of the event's stream");
            c.Equal((80, 99, 0), (run.Hp, run.Gold, Rewards(run)), "nothing else moved");
            c.Sequence([], Logged(run), "nothing was drawn again: every card an Ironclad card can become is built");
            c.Equal(run.Dump(), Run.Replay(setup, new Room { Event = "AromaOfChaos" }, run.Actions).Dump(), "the choice replays from the action list");

            string[] becomes = ["CrimsonMantle", "Vicious", "PrimalForce", "FiendFire", "PommelStrike", "CrimsonMantle", "Rupture"];
            for (var i = 0; i < Seeds.Length; i++)
            {
                var other = Open("AromaOfChaos", 80, seed: Seeds[i]);
                Do(other, "option:0", "card:9", "confirm");
                c.Equal(becomes[i], other.State.Deck[^1].Def.ClassName, $"seed {Seeds[i]}: what Bash becomes (the same place of the same 80 cards as for a Strike)");
            }
        });

        c.Group("Aroma of Chaos: a curse becomes another curse, and a card of Ancient rarity a colorless card", () =>
        {
            var run = Open("AromaOfChaos", new RunSetup { Seed = RealSeed, Deck = [.. RunSetup.IroncladDeck, new DeckCard("Clumsy"), new DeckCard("Greed"), new DeckCard("NeowsFury")] });
            Do(run, "option:0");
            c.Equal("card:0 card:5 card:9 card:10 card:11", Ids(run.Snapshot()), "Greed is Eternal: it is in the deck (place 11) and not on the screen, so Neow's Fury is the screen's card 11");
            c.Sequence(["Strike", "Defend", "Bash", "Clumsy", "Neow's Fury"], run.Snapshot().State["cards"]!.AsArray().Select(card => card!["name"]!.GetValue<string>()), "the cards listed");
            Do(run, "card:10", "confirm");
            // Place 3 of the 17 other curses, in the curse pool's order: Ascender's Bane, Bad Luck, Curse of the Bell, Debt.
            c.Equal("Debt", run.State.Deck[^1].Def.ClassName, "Clumsy became Debt");
            c.Equal("Greed NeowsFury Debt", string.Join(" ", run.State.Deck.TakeLast(3).Select(card => card.Def.ClassName)), "at the deck's end");

            // Neow's Fury becomes a colorless card. The game's picks on this seed, among the 53 colorless cards a single-player run can
            // hold: Eternal Armor, then (were it drawn again) Hand of Greed, Fisticuffs, Mind Blast, Production, ...
            string[] picks = ["EternalArmor", "HandOfGreed", "Fisticuffs", "MindBlast", "Production", "HandOfGreed", "Prolong", "UltimateDefend"];
            var given = picks.First(CardPools.IsBuilt);
            var passedOver = picks.TakeWhile(card => card != given).ToArray();
            run = Open("AromaOfChaos", new RunSetup { Seed = RealSeed, Deck = [.. RunSetup.IroncladDeck, new DeckCard("NeowsFury")] });
            Do(run, "option:0", "card:10", "confirm");
            c.Equal(given, run.State.Deck[^1].Def.ClassName, "Neow's Fury became the first card of the game's picks that the arena holds (Eternal Armor itself once it is built)");
            c.Sequence(passedOver, LoggedAs(run, "card"), "the picks before it are in the run's log as passed over");
            c.Equal(passedOver.Length + 1, Ev(run).Rng.Counter, "one number of the event's stream for each");
            c.That(Logged(run).All(entry => entry.What == "a transform of Neow's Fury" && entry.Floor == 4), "each logged as this transform, on this floor");

            if (passedOver.Length > 0)
            {
                run = Open("AromaOfChaos", new RunSetup { Seed = RealSeed, Deck = [.. RunSetup.IroncladDeck, new DeckCard("NeowsFury")] }, new Room { Strict = true });
                Do(run, "option:0", "card:10", "confirm");
                c.That(run.AtEnd && run.Result == RunResult.Unrepresentable && run.ResultReason!.Contains(picks[0], StringComparison.Ordinal),
                    $"a strict run ends there as unrepresentable, naming the game's card ({run.ResultReason})");
                c.Equal("NeowsFury", run.State.Deck[^1].Def.ClassName, "standing as before the choice");
            }
        });

        c.Group("Aroma of Chaos: Maintain Control asks for a card on the upgrade screen and upgrades it", () =>
        {
            var run = Open("AromaOfChaos");
            Do(run, "option:1");
            var s = run.Snapshot();
            c.Equal(("upgrade", "Choose a card to upgrade.", 1, 1), (s.State["kind"]!.GetValue<string>(), s.State["prompt"]!.GetValue<string>(), s.State["min"]!.GetValue<int>(), s.State["max"]!.GetValue<int>()),
                "the upgrade screen, for exactly one card");
            c.Equal("card:0 card:5 card:9", Ids(s), "the deck's cards; no way to back out");
            Do(run, "card:9");
            c.Sequence(["Bash", "Bash+"], run.Snapshot().State["preview"]!.AsArray().Select(card => card!["name"]!.GetValue<string>()), "the preview shows the card and what it becomes");
            Do(run, "confirm");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash+", DeckNames(run), "Bash is upgraded, in its place");
            c.Equal("You hold on. Bash was upgraded to Bash+.", PageText(run), "the page says so");
            c.Equal(ProceedOnly, Options(run), "the event is finished");
            c.Equal(0, Ev(run).Rng.Counter, "no number of the event's stream");
        });

        c.Group("Aroma of Chaos: one candidate is taken without a screen, and no candidate changes nothing", () =>
        {
            var run = Open("AromaOfChaos", new RunSetup { Seed = RealSeed, Deck = [new DeckCard("Greed"), new DeckCard("StrikeIronclad")] });
            Do(run, "option:0");
            c.Equal("event", run.Snapshot().Screen, "Let Go with one card that can be transformed: no choice is shown");
            c.Equal("Greed CrimsonMantle", DeckNames(run), "the Strike became Crimson Mantle");

            run = Open("AromaOfChaos", new RunSetup { Seed = RealSeed, Deck = [new DeckCard("Greed"), new DeckCard("StrikeIronclad")] });
            Do(run, "option:1");
            c.Equal("event", run.Snapshot().Screen, "Maintain Control with one card that can be upgraded: no choice is shown");
            c.Equal("Greed StrikeIronclad+", DeckNames(run), "the Strike is upgraded");

            run = Open("AromaOfChaos", new RunSetup { Seed = RealSeed, Deck = [new DeckCard("Greed")] });
            Do(run, "option:0");
            c.Equal("Greed", DeckNames(run), "Let Go with nothing to transform");
            c.Equal("You let go. No card of your Deck could be transformed.", PageText(run), "the page says so");
            c.Equal((ProceedOnly, 0), (Options(run), Ev(run).Rng.Counter), "the event is finished all the same, and no number was drawn");

            run = Open("AromaOfChaos", new RunSetup { Seed = RealSeed, Deck = [new DeckCard("Greed")] });
            Do(run, "option:1");
            c.Equal("You hold on. No card of your Deck could be upgraded.", PageText(run), "Maintain Control with nothing to upgrade");
            c.Equal(ProceedOnly, Options(run), "finishes too");
        });
    }

    // ====================================================================================
    // Morphic Grove
    // ====================================================================================

    private static void MorphicGroveChecks(Check c)
    {
        c.Group("Morphic Grove: the page, and Loner", () =>
        {
            var run = Open("MorphicGrove", hp: 50, gold: 120);
            var s = run.Snapshot();
            c.Equal("Morphic Grove", s.State["event"]!.GetValue<string>(), "the title");
            c.Equal(Q("[{'n':0,'title':'Group','text':'Lose ALL of your Gold. Transform 2 cards of your Deck: you choose them, and each becomes a random card.'},") +
                    Q("{'n':1,'title':'Loner','text':'Gain 5 Max HP.'}]"), Options(run), "two options");
            c.Equal("option:0=Choose Group | option:1=Choose Loner", Labels(s), "one action each");
            c.Equal(0, Ev(run).Rng.Counter, "the event rolls nothing at its start");
            Do(run, "option:1");
            c.Equal((55, 85, 120), (run.Hp, run.MaxHp, run.Gold), "Loner: 5 max HP and 5 HP with it; the gold stays");
            c.Equal("You kept to yourself and gained 5 Max HP.", PageText(run), "the page says so");
            c.Equal(ProceedOnly, Options(run), "the event is finished");
        });

        c.Group("Morphic Grove: Group takes all gold, then two chosen cards become random cards one after the other", () =>
        {
            var setup = new RunSetup { Seed = RealSeed, Gold = 120 };
            var run = Open("MorphicGrove", setup);
            Do(run, "option:0");
            var s = run.Snapshot();
            c.Equal(("transform", "Choose 2 cards to transform.", 2, 2), (s.State["kind"]!.GetValue<string>(), s.State["prompt"]!.GetValue<string>(), s.State["min"]!.GetValue<int>(), s.State["max"]!.GetValue<int>()),
                "the transform screen, for exactly two cards");
            c.Equal(0, s.State["player"]!["gold"]!.GetValue<int>(), "the gold is gone before the cards are chosen");
            c.Equal("card:0 card:5 card:9", Ids(s), "the deck's cards; no confirm before two are picked, no way to back out");
            Do(run, "card:0");
            c.Equal("card:0 card:1 card:5 card:9", Ids(run.Snapshot()), "one picked: it can be put back, and another Strike picked");
            Do(run, "card:5");
            c.Equal("confirm=Confirm: transform Strike, Defend | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "two picked: the preview");
            Do(run, "confirm");

            // The first two numbers of the event's stream on this seed, each a pick among the 80 cards a Basic card can become: Body Slam, Unmovable.
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad Bash BodySlam Unmovable", DeckNames(run),
                "the Strike became Body Slam and the Defend Unmovable, at the deck's end in that order");
            c.Equal("You joined the group and lost 120 Gold. Strike became Body Slam, Defend became Unmovable.", PageText(run), "the page says what happened");
            c.Equal((0, 80, 80), (run.Gold, run.Hp, run.MaxHp), "no gold is left; HP untouched");
            c.Equal(ProceedOnly, Options(run), "the event is finished");
            c.Equal(2, Ev(run).Rng.Counter, "two numbers of the event's stream");
            c.Equal(0, Rewards(run), "none of rewards");
            c.Equal(run.Dump(), Run.Replay(setup, new Room { Event = "MorphicGrove" }, run.Actions).Dump(), "the choice replays from the action list");

            // Picked the other way round, the Defend is transformed first and gets the first number.
            run = Open("MorphicGrove", setup);
            Do(run, "option:0", "card:5", "card:0", "confirm");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad Bash BodySlam Unmovable", DeckNames(run), "the same two new cards");
            c.Equal("You joined the group and lost 120 Gold. Defend became Body Slam, Strike became Unmovable.", PageText(run), "but the Defend became Body Slam: the cards are transformed in the order they were picked");

            // Backing out of the preview drops the picks; the gold stays lost.
            run = Open("MorphicGrove", setup);
            Do(run, "option:0", "card:0", "card:5", "cancel");
            c.Equal("card:0 card:5 card:9", Ids(run.Snapshot()), "back on the cards with nothing picked");
            c.Equal(0, run.Gold, "the gold is gone all the same");
            Do(run, "card:9", "card:0", "confirm");
            c.Equal("You joined the group and lost 120 Gold. Bash became Body Slam, Strike became Unmovable.", PageText(run), "the new picks are transformed");
        });

        c.Group("Morphic Grove: with no more than two cards that can be transformed, they are taken without a screen", () =>
        {
            var run = Open("MorphicGrove", new RunSetup { Seed = RealSeed, Gold = 250, Deck = [new DeckCard("StrikeIronclad"), new DeckCard("Greed"), new DeckCard("Bash")] });
            Do(run, "option:0");
            c.Equal("event", run.Snapshot().Screen, "no choice is shown");
            c.Equal("Greed BodySlam Unmovable", DeckNames(run), "both became random cards, in deck order; Greed is Eternal and stays");
            c.Equal("You joined the group and lost 250 Gold. Strike became Body Slam, Bash became Unmovable.", PageText(run), "the page says what happened");

            run = Open("MorphicGrove", new RunSetup { Seed = RealSeed, Gold = 250, Deck = [new DeckCard("Greed"), new DeckCard("Bash")] });
            Do(run, "option:0");
            c.Equal("Greed BodySlam", DeckNames(run), "one card that can be transformed: it alone is");
            c.Equal(1, Ev(run).Rng.Counter, "with one number");

            run = Open("MorphicGrove", new RunSetup { Seed = RealSeed, Gold = 250, Deck = [new DeckCard("Greed")] });
            Do(run, "option:0");
            c.Equal(("Greed", 0), (DeckNames(run), run.Gold), "none: the gold is lost for nothing");
            c.Equal("You joined the group and lost 250 Gold. No card of your Deck could be transformed.", PageText(run), "the page says so");
        });
    }

    // ====================================================================================
    // the two swords
    // ====================================================================================

    private static void Swords(Check c)
    {
        c.Group("Sword of Stone counts won Elite fights; at the fifth Sword of Jade stands in its place and gives 3 Strength in every fight", () =>
        {
            // The Sunken Statue, the sword, then Strawberry (so that a relic stands behind the sword), then fights against a dummy of
            // 18 HP that does nothing: hallway, Elite, Elite, boss, Elite, Elite, Elite, hallway, Elite, boss.
            var setup = new RunSetup { Seed = RealSeed };
            var room = new Room { Event = "SunkenStatue", ThenObtain = "Strawberry", Fights = "meebeeemeb" };
            var run = new Run(setup, room);
            Do(run, "option:0", "option:0");
            c.Sequence(["BurningBlood", "SwordOfStone", "Strawberry"], run.State.Relics.Select(relic => relic.ClassName), "the relics before the first fight");
            c.Equal((87, 87), (run.State.Hp, run.State.MaxHp), "87 of 87 HP with Strawberry");

            int[] elites = [0, 1, 2, 2, 3, 4];
            string[] kinds = ["a hallway fight", "an Elite", "an Elite", "the boss", "an Elite", "an Elite"];
            for (var fight = 0; fight < 6; fight++)
            {
                c.Equal(0, run.Fight!.Player.PowerAmount<StrengthPower>(), $"fight {fight + 1}: no Strength from Sword of Stone");
                WinFight(run);
                c.Equal(elites[fight], run.State.Relics.OfType<SwordOfStone>().Single().ElitesDefeated, $"after fight {fight + 1} ({kinds[fight]}): the Elite fights counted");
            }
            c.That(run.DescribeDeck()["relics"]!.AsArray().Any(relic => relic!["text"]!.GetValue<string>() ==
                    "After 5 won Elite fights it turns into Sword of Jade (at the start of every fight: gain 3 Strength). Won so far: 4."),
                "get_deck shows the count in the relic's text");

            // Fight 7 is the fifth Elite: in its victory hook the sword is replaced, in place.
            c.Equal(RoomType.Elite, run.Fight!.RoomType, "fight 7 is an Elite");
            WinFight(run);
            c.Sequence(["BurningBlood", "SwordOfJade", "Strawberry"], run.State.Relics.Select(relic => relic.ClassName), "after the fifth Elite, Sword of Jade stands where Sword of Stone stood");
            c.Sequence(["Burning Blood", "Sword of Jade", "Strawberry"], run.PlayerJson()["relics"]!.AsArray().Select(name => name!.GetValue<string>()), "the player block lists it by title");
            c.That(run.DescribeDeck()["relics"]!.AsArray().Any(relic => relic!["text"]!.GetValue<string>() == "At the start of every fight: gain 3 Strength."), "get_deck shows its text");

            // Fights 8, 9 and 10: a hallway fight, an Elite and a boss, each with 3 Strength from the start. A Strike deals 6 + 3.
            foreach (var kind in new[] { RoomType.Monster, RoomType.Elite, RoomType.Boss })
            {
                c.Equal(kind, run.Fight!.RoomType, $"the next fight is a {kind} room");
                c.Equal(3, run.Fight.Player.PowerAmount<StrengthPower>(), $"{kind}: 3 Strength on turn 1");
                c.Equal(1, run.Fight.Turn, $"{kind}: it is turn 1");
                Do(run, run.LegalActions().First(a => a.Id.StartsWith("play:0", StringComparison.Ordinal)).Id);
                c.Equal(9, run.Fight!.Enemies[0].Hp, $"{kind}: a Strike with 3 Strength deals 9 of 18");
                WinFight(run);
            }
            c.Equal(RunResult.Cleared, run.Result, "the run of the check is over");
            c.Sequence(["BurningBlood", "SwordOfJade", "Strawberry"], run.State.Relics.Select(relic => relic.ClassName), "and Sword of Jade is still there: it turns into nothing more");
            c.Equal(87, run.State.Hp, "no HP was lost on the way");
            c.Equal(run.Dump(), Run.Replay(setup, new Room { Event = "SunkenStatue", ThenObtain = "Strawberry", Fights = "meebeeemeb" }, run.Actions).Dump(), "the whole way replays from its action list");
        });

        c.Group("Sword of Stone counts and turns in the victory hook itself, not in a hook before it", () =>
        {
            // The Watcher is picked up after the sword, so at every hook it is asked after it. At the hook just before the victory
            // hook it must still see Sword of Stone with the count of the fights before: the sword acts in the victory hook.
            var run = new Run(new RunSetup { Seed = RealSeed }, new Room { Event = "SunkenStatue", ThenGive = static () => new Watcher(), Fights = "eeeeem" });
            Do(run, "option:0", "option:0");
            for (var fight = 1; fight <= 5; fight++)
            {
                WinFight(run);
                c.Equal($"BurningBlood SwordOfStone:{fight - 1} Watcher", run.State.Relics.OfType<Watcher>().Single().AtVictoryEarly,
                    $"Elite {fight}: the relics as the hook before the victory hook saw them");
            }
            c.Sequence(["BurningBlood", "SwordOfJade", "Watcher"], run.State.Relics.Select(relic => relic.ClassName), "after the fifth Elite the sword has turned");
            // The sixth fight has begun: Sword of Jade's Strength is applied by nobody (Vajra's is applied by the player).
            c.Equal("3 Strength from nobody; ", run.Relics.OfType<Watcher>().Single().Strength, "Sword of Jade gives its Strength with no applier, once, when the fight's room is entered");
        });

        c.Group("the swords as the game's classes have them", () =>
        {
            c.Equal(5, SwordOfStone.Elites, "Sword of Stone: five Elite fights");
            c.Equal(3, SwordOfJade.Strength, "Sword of Jade: 3 Strength");
            c.Equal("After 5 won Elite fights it turns into Sword of Jade (at the start of every fight: gain 3 Strength). Won so far: 0.", new SwordOfStone().Describe(), "a new Sword of Stone has counted nothing");

            // A lost fight counts nothing: an Elite fight in which the player dies.
            var f = new FightBuilder().Relics("BurningBlood", "SwordOfStone").Room(RoomType.Elite).Hp(1)
                .Enemy(new CheckDummy { Hp = 50, Steps = [new DummyStep("HIT", Damage: 5)] }).Start();
            f.EndTurn();
            c.That(f.IsOver && f.Player.IsDead, "an Elite fight that is lost");
            c.Equal(0, f.Relics.OfType<SwordOfStone>().Single().ElitesDefeated, "is not counted");

            // In a single fight of the arena the sword hears the victory as it does in a run.
            f = new FightBuilder().Relics("BurningBlood", "SwordOfStone").Room(RoomType.Elite).DrawOrder("StrikeIronclad").Enemy(new CheckDummy { Hp = 6 }).Start();
            f.Play("StrikeIronclad", 0);
            c.That(f.IsOver && !f.Player.IsDead, "an Elite fight that is won");
            c.Equal(1, f.Relics.OfType<SwordOfStone>().Single().ElitesDefeated, "is counted once");
        });
    }

    // ====================================================================================
    // the order in which an option does its things
    // ====================================================================================

    private static void OrderOfEffects(Check c)
    {
        c.Group("each option does its things in the game's order", () =>
        {
            // The Watcher is the player's second relic: it writes down every gold gain, HP loss and card arriving in the deck, with the state at that moment.
            var run = Open("ThisOrThat", Watched());
            Do(run, "option:0");
            c.Equal("-6 HP to 74/80 with 2 relics | gold is 160 at 74 HP", Seen(run), "This or That?, This: the HP is lost first, then the gold is paid");

            run = Open("ThisOrThat", Watched(), new Room { RewardsBefore = 25 });
            Do(run, "option:1");
            c.Equal("Clumsy from None into a deck of 11 at 80/80 HP, 99 gold, 3 relics", Seen(run), "This or That?, That: the relic is held when the curse arrives");

            run = Open("SunkenStatue", Watched());
            Do(run, "option:1");
            c.Equal("gold is 206 at 80 HP | -7 HP to 73/80 with 2 relics", Seen(run), "The Sunken Statue, Dive: the gold is paid first, then the HP is lost");

            run = Open("JungleMazeAdventure", Watched());
            Do(run, "option:0");
            c.Equal("-18 HP to 62/80 with 2 relics | gold is 263 at 62 HP", Seen(run), "Jungle Maze Adventure, Solo Quest: the HP is lost first, then the gold is paid");

            run = Open("UnrestSite", Watched(hp: 50));
            Do(run, "option:0");
            c.Equal("PoorSleep from None into a deck of 11 at 80/80 HP, 99 gold, 2 relics", Seen(run), "Unrest Site, Rest: healed before the curse arrives");

            run = Open("UnrestSite", Watched(), new Room { RewardsBefore = 25 });
            Do(run, "option:1");
            c.Equal("-8 HP to 72/80 with 2 relics", Seen(run), "Unrest Site, Kill: the max HP is lost (at full HP: 8 HP with it) before the relic is pulled");

            run = Open("MorphicGrove", Watched(gold: 120));
            Do(run, "option:0", "card:0", "card:5", "confirm");
            c.Equal("BodySlam from Deck into a deck of 10 at 80/80 HP, 0 gold, 2 relics | Unmovable from Deck into a deck of 10 at 80/80 HP, 0 gold, 2 relics", Seen(run),
                "Morphic Grove, Group: the gold is gone first; then one card after the other (the second original is still in the deck when the first new card arrives)");

            run = Open("TabletOfTruth", Watched());
            Do(run, "option:0");
            c.Equal("-3 HP to 77/80 with 2 relics", Seen(run), "Tablet of Truth, Decipher: the HP above the new maximum is lost as damage, before the maximum falls");
        });
    }

    // ====================================================================================
    // deaths, and relics that change a loss
    // ====================================================================================

    private static void DeathsAndRelics(Check c)
    {
        c.Group("a relic that takes HP off a loss: the option says the game's number, the page what was really lost", () =>
        {
            var run = Open("ThisOrThat", new RunSetup { Seed = RealSeed, Relics = ["BurningBlood", "TungstenRod"] });
            c.Equal("Lose 6 HP. Gain 61 Gold.", run.Snapshot().State["options"]![0]!["text"]!.GetValue<string>(), "with Tungsten Rod the option reads as without");
            Do(run, "option:0");
            c.Equal((75, 160), (run.Hp, run.Gold), "5 HP are lost");
            c.Equal("You took the plain one: you lost 5 HP and gained 61 Gold.", PageText(run), "and the page says 5");

            // The game's own test for a deadly option does not ask the relics: at 6 HP it stays unoffered though the rod would save the player.
            run = Open("ThisOrThat", new RunSetup { Seed = RealSeed, Hp = 6, Relics = ["BurningBlood", "TungstenRod"] });
            c.Equal("option:1", Ids(run.Snapshot()), "at 6 HP This is not offered, rod or no rod");

            run = Open("SunkenStatue", new RunSetup { Seed = RealSeed, Relics = ["BurningBlood", "TungstenRod"] });
            Do(run, "option:1");
            c.Equal("You dived for the coins: you gained 107 Gold and lost 6 HP.", PageText(run), "The Sunken Statue with the rod: 6 HP");

            run = Open("JungleMazeAdventure", new RunSetup { Seed = RealSeed, Relics = ["BurningBlood", "TungstenRod"] });
            Do(run, "option:0");
            c.Equal((63, "You went in alone: you lost 17 HP and gained 164 Gold."), (run.Hp, PageText(run)), "Jungle Maze Adventure with the rod: 17 HP");
        });

        c.Group("an option that kills ends the run where the game's order puts the death (the screen never offers one)", () =>
        {
            // The checks reach past the screen, as the game's own UI would let a human click a red option.
            var run = Open("ThisOrThat", hp: 6);
            Ev(run).Choose(run, Ev(run).Options[0]);
            c.That(run.IsOver && run.Result == RunResult.Died, "This at 6 HP: the player dies");
            c.Equal(99, run.State.Gold, "before the gold was paid");
            c.Equal("game_over", run.Snapshot().Screen, "the mod's game over screen is up");

            run = Open("SunkenStatue", hp: 7);
            Ev(run).Choose(run, Ev(run).Options[1]);
            c.That(run.IsOver && run.Result == RunResult.Died, "the dive at 7 HP: the player dies");
            c.Equal(206, run.State.Gold, "after the gold was paid");

            run = Open("JungleMazeAdventure", hp: 18);
            Ev(run).Choose(run, Ev(run).Options[0]);
            c.That(run.IsOver && run.Result == RunResult.Died, "Solo Quest at 18 HP: the player dies");
            c.Equal(99, run.State.Gold, "before the gold was paid");

            run = Open("UnrestSite", hp: 5, maxHp: 8);
            Ev(run).Choose(run, Ev(run).Options[1]);
            c.That(run.IsOver && run.Result == RunResult.Died, "Kill the Trees at 8 max HP: the player dies");
            c.Equal((1, 0), (run.State.Relics.Count, Rewards(run)), "before a relic was pulled");
        });
    }

    // ====================================================================================
    // streams and modes
    // ====================================================================================

    private static void StreamsAndModes(Check c)
    {
        c.Group("an event's rolls are its own: the same whatever was drawn before, on whatever floor, in both stream modes", () =>
        {
            foreach (var room in new[] { new Room(), new Room { Noise = 7, Floor = 9, RewardsBefore = 40 }, new Room { Mode = StreamMode.Keyed, Floor = 2 } })
            {
                var how = $"mode {room.Mode}, floor {room.Floor}, {room.Noise} numbers of other streams before";
                c.Equal(61, ((ThisOrThat)Ev(Open("ThisOrThat", room: room))).Gold, $"This or That? ({how})");
                c.Equal(107, ((SunkenStatue)Ev(Open("SunkenStatue", room: room))).Gold, $"The Sunken Statue ({how})");
                c.Equal(164.66355m, ((JungleMazeAdventure)Ev(Open("JungleMazeAdventure", room: room))).SoloGoldRolled, $"Jungle Maze Adventure ({how})");

                var run = Open("AromaOfChaos", room: room);
                Do(run, "option:0", "card:0", "confirm");
                c.Equal("CrimsonMantle", run.State.Deck[^1].Def.ClassName, $"Aroma of Chaos, Let Go on a Strike ({how})");

                run = Open("TabletOfTruth", room: room);
                Do(run, "option:0");
                c.Equal("DefendIronclad+", run.State.Deck[5].Def.ClassName + (run.State.Deck[5].IsUpgraded ? "+" : ""), $"Tablet of Truth, the first upgrade ({how})");

                run = Open("MorphicGrove", new RunSetup { Seed = RealSeed, Gold = 100 }, room);
                Do(run, "option:0", "card:0", "card:5", "confirm");
                c.Equal("BodySlam Unmovable", string.Join(" ", run.State.Deck.TakeLast(2).Select(card => card.Def.ClassName)), $"Morphic Grove, Group ({how})");
            }

            // Met a second time in a run of the seed (which the game's list never does), an event rolls the same again.
            var first = Open("SunkenStatue");
            var streamAgain = World(first).Streams.Event("SunkenStatue");
            c.Equal(107, SunkenStatue.RollGold(streamAgain), "the stream of an event starts anew whenever it is asked for");
        });
    }

    // ====================================================================================
    // copies and replays
    // ====================================================================================

    private static void CopiesAndReplays(Check c)
    {
        c.Group("every way through the seven events replays from its action list, and a copy taken on the way goes on the same", () =>
        {
            (string Event, RunSetup Setup, string[] Actions)[] ways =
            [
                ("ThisOrThat", new RunSetup { Seed = RealSeed }, ["option:0", "option:0"]),
                ("ThisOrThat", new RunSetup { Seed = RealSeed }, ["option:1", "option:0"]),
                ("SunkenStatue", new RunSetup { Seed = RealSeed }, ["option:0", "option:0"]),
                ("SunkenStatue", new RunSetup { Seed = RealSeed }, ["option:1", "option:0"]),
                ("JungleMazeAdventure", new RunSetup { Seed = RealSeed }, ["option:0", "option:0"]),
                ("JungleMazeAdventure", new RunSetup { Seed = RealSeed }, ["option:1", "option:0"]),
                ("UnrestSite", new RunSetup { Seed = RealSeed, Hp = 40, Potions = ["BloodPotion"] }, ["potion:0", "option:0", "option:0"]),
                ("UnrestSite", new RunSetup { Seed = RealSeed, Hp = 40 }, ["option:1", "option:0"]),
                ("TabletOfTruth", new RunSetup { Seed = RealSeed }, ["option:1", "option:0"]),
                ("TabletOfTruth", new RunSetup { Seed = RealSeed }, ["option:0", "option:0", "option:1", "option:0"]),
                ("AromaOfChaos", new RunSetup { Seed = RealSeed }, ["option:0", "card:5", "cancel", "card:9", "confirm", "option:0"]),
                ("AromaOfChaos", new RunSetup { Seed = RealSeed }, ["option:1", "card:0", "confirm", "option:0"]),
                ("MorphicGrove", new RunSetup { Seed = RealSeed, Gold = 100 }, ["option:0", "card:9", "card:9", "card:1", "card:6", "confirm", "option:0"]),
                ("MorphicGrove", new RunSetup { Seed = RealSeed, Gold = 100 }, ["option:1", "option:0"]),
            ];
            foreach (var (ev, setup, actions) in ways)
            {
                var what = $"{ev}: {string.Join(" ", actions)}";
                var run = Open(ev, setup);
                Run? copy = null;
                var copiedAt = 0;
                for (var i = 0; i < actions.Length; i++)
                {
                    // A copy in the middle: after the first action, which for the card choices is while the question is open.
                    if (i == 1)
                    {
                        copy = run.Clone();
                        copiedAt = i;
                    }
                    Do(run, actions[i]);
                }
                c.Equal("outside", run.Snapshot().Screen, $"{what}: the room is left");
                c.Equal(run.Dump(), Run.Replay(setup, new Room { Event = ev }, run.Actions).Dump(), $"{what}: replays from its action list");
                c.Equal(run.Dump(), run.Clone().Dump(), $"{what}: a copy is in the same state");
                foreach (var id in actions.Skip(copiedAt)) Do(copy!, id);
                c.Equal(run.Dump(), copy!.Dump(), $"{what}: a copy taken after the first action and played on ends in the same state");
            }
        });
    }

    // ====================================================================================
    // the act's list with the real registry
    // ====================================================================================

    private static void TheActsList(Check c)
    {
        c.Group("the act's event list with these events built: the real run's first Event room holds This or That?, and a third of all first Event rooms hold their game's event", () =>
        {
            // Seed 1BUSLN0FY5 after Neow's room (which takes the act's event counter to 1): entry 1 of the list is This or That?.
            var run = new Run(new RunSetup { Seed = RealSeed }, new Room());
            World(run).Rooms[0].MarkEventVisited();
            var pull = EventList.Peek(run);
            c.Equal("ThisOrThat", pull.Event, "the event of the real run's first Event room");
            c.That(pull.IsTheGames, "is the game's own, with nothing passed over");

            // A new player (99 gold, full HP, no potion, before floor 8) is allowed fifteen of the 21 events: the thirteen that are
            // always allowed, Whispering Hollow (44 gold) and The Legends Were True. Five of the fifteen are built here, and the list is
            // one shuffle, so the first allowed entry is a built one for a third of all seeds.
            var maker = new GameRng(1500);
            var seeds = 0;
            var games = 0;
            var found = 0;
            var held = new Dictionary<string, int>(StringComparer.Ordinal);
            while (seeds < 600)
            {
                var seed = RunSeed.Random(maker);
                if (RunGeneration.RollActs(seed, RunUnlocks.All)[0] != GameLists.Overgrowth) continue;
                seeds++;
                var fresh = new Run(new RunSetup { Seed = seed.Text }, new Room());
                World(fresh).Rooms[0].MarkEventVisited();
                var first = EventList.Peek(fresh);
                if (first.Found) found++;
                if (first.IsTheGames) games++;
                if (first.Event is { } name) held[name] = held.GetValueOrDefault(name) + 1;
            }
            // 600 seeds at a chance of 1/3: 200, with a standard deviation of 11.5.
            Report($"first Event rooms of 600 Overgrowth seeds: {games} hold the game's own event; held: {string.Join(", ", held.OrderBy(h => h.Key, StringComparer.Ordinal).Select(h => $"{h.Key} {h.Value}"))}");
            var alsoBuilt = EventList.InAnOvergrowthAct.Count(EventList.IsBuilt) > EventsA.Events.Count;
            c.That(alsoBuilt ? games >= 150 : games is >= 150 and <= 250,
                $"of 600 Overgrowth seeds, the first Event room of {games} holds the game's own event (about 200 are expected with these seven events alone)");
            c.Equal(600, found, "and every one of them holds some built event: five built events are always allowed");
            c.That(held.Keys.All(EventList.IsBuilt), $"only built events are held ({string.Join(", ", held.OrderBy(h => h.Key, StringComparer.Ordinal).Select(h => $"{h.Key} {h.Value}"))})");
            foreach (var name in new[] { "AromaOfChaos", "JungleMazeAdventure", "SunkenStatue", "TabletOfTruth", "ThisOrThat" })
                c.That(held.GetValueOrDefault(name) > 60, $"{name} is the first event of a fair share of the seeds ({held.GetValueOrDefault(name)} of 600)");
            c.That(!held.ContainsKey("MorphicGrove") && !held.ContainsKey("UnrestSite"), "Morphic Grove and Unrest Site are never the first event of a new player with 99 gold at full HP");
        });
    }

    // ====================================================================================
    // many rooms
    // ====================================================================================

    private static readonly DeckCard[][] Decks =
    [
        [.. RunSetup.IroncladDeck],
        [.. RunSetup.IroncladDeck, new DeckCard("Clumsy"), new DeckCard("Greed"), new DeckCard("NeowsFury"), new DeckCard("Anger", true), new DeckCard("Injury")],
        [new DeckCard("Bash")],
        [new DeckCard("Greed")],
        [new DeckCard("Bash", true), new DeckCard("Guilty")],
        [new DeckCard("StrikeIronclad"), new DeckCard("DefendIronclad", true), new DeckCard("Inflame"), new DeckCard("FightMe"), new DeckCard("Barricade", true), new DeckCard("PoorSleep")],
    ];

    /// <summary>A scripted visitor: which of the offered actions it takes depends on the step count alone.</summary>
    private static string Visit(GameSnapshot s, int step)
    {
        var ids = s.Actions.Select(a => a.Id).ToList();
        if (s.Screen == "event")
        {
            var options = ids.Where(id => id.StartsWith("option:", StringComparison.Ordinal)).ToList();
            var potions = ids.Where(id => id.Contains("potion:", StringComparison.Ordinal)).ToList();
            if (potions.Count > 0 && step % 3 == 0) return potions[step % potions.Count];
            return options[step % options.Count];
        }
        if (s.Screen == "card_select" && ids.Contains("confirm") && step % 4 != 0) return "confirm";
        return ids[step % ids.Count];
    }

    private static void ManyRooms(Check c)
    {
        c.Group("many rooms: every event with every kind of player, walked by a scripted visitor", () =>
        {
            // $ARENA_EVENT_SOAK asks for more rooms per event than the 150 of every self-check.
            var perEvent = int.TryParse(Environment.GetEnvironmentVariable("ARENA_EVENT_SOAK"), out var asked) && asked > 0 ? asked : 150;
            var problems = new List<string>();
            var ends = new Dictionary<string, int>(StringComparer.Ordinal);
            var screens = new HashSet<string>(StringComparer.Ordinal);
            var pagesSeen = 0;
            var deadlySeen = 0;
            var maker = new GameRng(20261011);
            int[] maxHps = [80, 80, 80, 60, 30, 12, 9, 8, 6, 3, 1];
            int[] golds = [99, 0, 100, 250, 43];
            string?[][] belts = [[], ["BloodPotion"], ["FruitJuice", "BloodPotion"], [null, "FirePotion", "FruitJuice"]];
            foreach (var ev in EventsA.Events)
            {
                for (var n = 0; n < perEvent; n++)
                {
                    var seed = RunSeed.Random(maker).Text;
                    var maxHp = maxHps[maker.NextInt(maxHps.Length)];
                    var setup = new RunSetup
                    {
                        Seed = seed, MaxHp = maxHp, Hp = 1 + maker.NextInt(maxHp), Gold = golds[maker.NextInt(golds.Length)],
                        Deck = Decks[maker.NextInt(Decks.Length)], Potions = belts[maker.NextInt(belts.Length)],
                        Relics = maker.NextInt(4) == 0 ? ["BurningBlood", "TungstenRod"] : ["BurningBlood"],
                    };
                    var room = new Room { Event = ev, Floor = 2 + maker.NextInt(14), Mode = maker.NextInt(3) == 0 ? StreamMode.Keyed : StreamMode.Game };
                    var offset = maker.NextInt(1000);
                    var where = $"{ev} on seed {seed} ({setup.Hp}/{setup.MaxHp} HP, {setup.Gold} gold, {setup.Deck.Count} cards, {room.Mode})";
                    try
                    {
                        var run = new Run(setup, room);
                        var deckBefore = run.State.Deck.Count;
                        for (var step = 0; step < 300 && !run.AtEnd && run.Snapshot().Screen != "outside"; step++)
                        {
                            var s = run.Snapshot();
                            screens.Add(s.Screen);
                            if (s.Screen == "event")
                            {
                                pagesSeen++;
                                // A deadly option is listed and never offered.
                                foreach (var option in s.State["options"]!.AsArray())
                                {
                                    if (option!["will_kill_player"] is null) continue;
                                    deadlySeen++;
                                    if (s.Actions.Any(a => a.Id == $"option:{option["n"]}")) problems.Add($"{where}: a deadly option is offered");
                                }
                            }
                            if (s.Actions.Count == 0)
                            {
                                problems.Add($"{where}: the screen {s.Screen} offers nothing");
                                break;
                            }
                            var id = Visit(s, step + offset);
                            var result = run.Execute(id);
                            if (!result.Ok)
                            {
                                problems.Add($"{where}: '{id}' was refused on {s.Screen}: {result.Error}");
                                break;
                            }
                            if (run.Hp < 0 || run.Hp > run.MaxHp || run.MaxHp < 1 || run.Gold < 0) problems.Add($"{where}: {run.Hp}/{run.MaxHp} HP, {run.Gold} gold after {id}");
                        }

                        var end = run.AtEnd || run.IsOver ? run.Result ?? "over" : run.Snapshot().Screen == "outside" ? "left" : "unfinished";
                        ends[end] = ends.GetValueOrDefault(end) + 1;
                        if (end != "left") problems.Add($"{where}: the visit ended as {end} ({run.ResultReason})");
                        if (end == "left" && ((Room)run.Director).Left != 1) problems.Add($"{where}: the room was left {((Room)run.Director).Left} times");
                        if (run.State.Deck.Count != deckBefore && run.State.Deck.Count != deckBefore + 1) problems.Add($"{where}: the deck went from {deckBefore} to {run.State.Deck.Count} cards");
                        foreach (var card in run.State.Deck)
                            if (!CardPools.IsBuilt(card.Def.ClassName)) problems.Add($"{where}: the deck holds {card.Def.ClassName}, which is not built");
                        foreach (var relic in run.State.Relics)
                            if (!RunRelics.IsBuilt(relic.ClassName)) problems.Add($"{where}: the player holds {relic.ClassName}, which is not built");
                        if (run.State.Relics.Select(relic => relic.ClassName).Distinct().Count() != run.State.Relics.Count) problems.Add($"{where}: a relic is held twice");
                        foreach (var entry in Logged(run))
                        {
                            var built = entry.Kind == "relic" ? RunRelics.IsBuilt(entry.ClassName) : CardPools.IsBuilt(entry.ClassName);
                            if (built) problems.Add($"{where}: the log says {entry.Kind} {entry.ClassName} was passed over, but it is built");
                        }
                        var replayed = Run.Replay(setup, new Room { Event = ev, Floor = room.Floor, Mode = room.Mode }, run.Actions).Dump();
                        if (replayed != run.Dump()) problems.Add($"{where}: the replay of the action list ends in another state");
                    }
                    catch (Exception ex)
                    {
                        problems.Add($"{where}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            var rooms = perEvent * EventsA.Events.Count;
            Report($"{rooms} event rooms: {string.Join(", ", ends.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => $"{e.Key} {e.Value}"))}; {pagesSeen} pages read, {deadlySeen} deadly options listed; {problems.Count} problems");
            c.Equal("", string.Join(" | ", problems.Take(5)),
                $"{rooms} rooms: every action the visitor chose was accepted, every room was left alive, nothing unbuilt was ever held, every visit replays ({problems.Count} problems)");
            c.Equal(rooms, ends.GetValueOrDefault("left"), $"every room was left ({string.Join(", ", ends.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => $"{e.Key} {e.Value}"))})");
            c.That(screens.SetEquals(["event", "card_select"]), $"the visits met the event screen and the card choices ({string.Join(", ", screens.Order(StringComparer.Ordinal))})");
            c.That(deadlySeen > 0, $"and deadly options among {pagesSeen} pages read ({deadlySeen})");
        });
    }

    // ====================================================================================
    // the real records on this machine
    // ====================================================================================

    private static void GoldenFolder(Check c)
    {
        c.Group("the game's own run records on this machine, when there are any: every recorded choice at one of these events", () =>
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

            var compared = 0;
            foreach (var file in Directory.GetFiles(history, "*.run").OrderBy(f => f, StringComparer.Ordinal))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                // Records of this build, and of v0.108.0, the one other build a record of these events exists from (the events' code is
                // the same in v0.107.1 and in v0.111.0). Standard runs only.
                if (!root.TryGetProperty("build_id", out var build) || build.GetString() is not ("v0.107.1" or "v0.108.0")) continue;
                if (!root.TryGetProperty("game_mode", out var mode) || mode.GetString() != "standard") continue;
                if (!root.TryGetProperty("map_point_history", out var acts)) continue;
                var seed = root.GetProperty("seed").GetString()!;
                var players = root.GetProperty("players");
                foreach (var act in acts.EnumerateArray())
                {
                    foreach (var point in act.EnumerateArray())
                    {
                        if (!point.TryGetProperty("rooms", out var rooms) || rooms.GetArrayLength() == 0) continue;
                        var model = rooms[0].TryGetProperty("model_id", out var id) ? id.GetString() : null;
                        var stats = point.GetProperty("player_stats");
                        for (var slot = 0; slot < stats.GetArrayLength(); slot++)
                        {
                            var stat = stats[slot];
                            if (!stat.TryGetProperty("event_choices", out var choices) || choices.GetArrayLength() != 1) continue;
                            var key = choices[0].GetProperty("title").GetProperty("key").GetString()!;
                            // Bowler Hat and Ectoplasm are the game's two relics that change gold gained: a player who ends the run with one is skipped.
                            var relics = players[slot].TryGetProperty("relics", out var held) ? held.GetRawText() : "";
                            var goldChanged = relics.Contains("BOWLER_HAT", StringComparison.Ordinal) || relics.Contains("ECTOPLASM", StringComparison.Ordinal);
                            var gained = stat.GetProperty("gold_gained").GetInt32();
                            var what = $"{Path.GetFileName(file)}: seed {seed}, player {slot + 1}, {key}";
                            switch (model, key)
                            {
                                case ("EVENT.SUNKEN_STATUE", "SUNKEN_STATUE.pages.INITIAL.options.DIVE_INTO_WATER.title") when !goldChanged:
                                    c.Equal(SunkenStatue.RollGold(EventStream(seed, "SUNKEN_STATUE", slot)), gained, what + ": the gold");
                                    compared++;
                                    break;
                                case ("EVENT.THIS_OR_THAT", "THIS_OR_THAT.pages.INITIAL.options.PLAIN.title") when !goldChanged:
                                    c.Equal(EventStream(seed, "THIS_OR_THAT", slot).NextInt(ThisOrThat.MinGold, ThisOrThat.MaxGold + 1), gained, what + ": the gold");
                                    compared++;
                                    break;
                                case ("EVENT.THIS_OR_THAT", "THIS_OR_THAT.pages.INITIAL.options.ORNATE.title"):
                                    c.That(stat.TryGetProperty("cards_gained", out var cards) && cards.GetRawText().Contains("CARD.CLUMSY", StringComparison.Ordinal), what + ": the curse Clumsy");
                                    c.That(stat.TryGetProperty("relic_choices", out var picked) && picked.GetArrayLength() == 1, what + ": one relic");
                                    compared++;
                                    break;
                                case ("EVENT.UNREST_SITE", "UNREST_SITE.pages.INITIAL.options.KILL.title"):
                                    c.Equal(UnrestSite.MaxHpLoss, stat.GetProperty("max_hp_lost").GetInt32(), what + ": the max HP lost");
                                    compared++;
                                    break;
                                case ("EVENT.MORPHIC_GROVE", "MORPHIC_GROVE.pages.INITIAL.options.LONER.title"):
                                    c.Equal(MorphicGrove.MaxHpGain, stat.GetProperty("max_hp_gained").GetInt32(), what + ": the max HP gained");
                                    compared++;
                                    break;
                                case ("EVENT.MORPHIC_GROVE", "MORPHIC_GROVE.pages.INITIAL.options.GROUP.title"):
                                    c.Equal(0, stat.GetProperty("current_gold").GetInt32(), what + ": no gold is left");
                                    compared++;
                                    break;
                            }
                        }
                    }
                }
            }
            c.That(true, $"{compared} recorded choices compared");
        });
    }
}
