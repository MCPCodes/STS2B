using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>What the checks of the deck edits, the card choices and the card pools share: a run that
/// stands outside a fight, a screen whose actions are scripted, and cards and relics of their own.</summary>
internal static class DeckCheckKit
{
    private static readonly JsonSerializerOptions Plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>JSON as compact text with "+" and quotes written as they are, as the tools send it.</summary>
    public static string Text(JsonNode? node) => node is null ? "null" : node.ToJsonString(Plain);

    public static string Keys(JsonNode? node) => node is JsonObject o ? string.Join(",", o.Select(kv => kv.Key)) : "(not an object)";

    public static string Ids(GameSnapshot snapshot) => string.Join(" ", snapshot.Actions.Select(a => a.Id));

    public static string Labels(GameSnapshot snapshot) => string.Join(" | ", snapshot.Actions.Select(a => $"{a.Id}={a.Label}"));

    public static void Do(Run run, params string[] ids)
    {
        foreach (var id in ids)
        {
            var result = run.Execute(id);
            if (!result.Ok)
                throw new InvalidOperationException($"'{id}' was refused: {result.Error} Legal now: {string.Join(", ", run.LegalActions().Select(a => a.Id))}");
        }
    }

    /// <summary>The class names of the deck in deck order, an upgraded card with a "+".</summary>
    public static string DeckNames(Run run) => string.Join(" ", run.State.Deck.Select(c => c.Def.ClassName + (c.IsUpgraded ? "+" : "")));

    /// <summary>A curse of the check's own under one of the game's class names: Unplayable, never upgradable.</summary>
    public static CardDef Curse(string className, string name, CardKeywords more = CardKeywords.None) =>
        new(className, name, CardType.Curse, CardRarity.Curse, CardDef.NoCost, TargetType.None)
        {
            Keywords = CardKeywords.Unplayable | more, MaxUpgradeLevel = 0, Text = _ => "A curse of the checks.",
        };

    /// <summary>
    /// A screen outside a fight whose actions are a script: each id runs a function that is handed
    /// the run and the screen it runs on, and writes what it finds to <see cref="Notes"/>. The
    /// functions capture nothing, so an action that asks the player runs again cleanly.
    /// </summary>
    public sealed class Desk(IReadOnlyDictionary<string, Action<Run, Desk>> script) : RunScreen
    {
        public List<string> Notes = new();

        public override string Name => "event";

        public override GameSnapshot Snapshot(Run run) => new()
        {
            Screen = Name, AwaitingDecision = true,
            State = new JsonObject { ["notes"] = new JsonArray(Notes.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()) },
            Actions = script.Keys.Select(id => new LegalAction(id, id)).ToList(),
        };

        public override ActionResult Execute(Run run, string actionId)
        {
            if (!script.TryGetValue(actionId, out var act)) return ActionResult.Fail($"Unknown event action '{actionId}'.");
            act(run, this);
            return ActionResult.Success;
        }

        public override RunScreen Clone(CloneMap map) => new Desk(script) { Notes = Notes.ToList() };
    }

    /// <summary>Puts up one <see cref="Desk"/> and nothing else; a won fight ends the run.</summary>
    public sealed class DeskDirector(IReadOnlyDictionary<string, Action<Run, Desk>> script) : RunDirector
    {
        public override void Begin(Run run) => run.Push(new Desk(script));
        public override void AfterFightWon(Run run, FightOutcome fight) => run.End(RunResult.Cleared);
    }

    private static readonly Dictionary<string, Action<Run, Desk>> NoScript = new();

    /// <summary>A run that stands outside a fight with nothing going on.</summary>
    public static Run Outside(RunSetup? setup = null) => new(setup ?? new RunSetup(), new DeskDirector(NoScript));

    /// <summary>The notes of the run's desk.</summary>
    public static List<string> Notes(Run run) => ((Desk)run.Top!).Notes;
}

/// <summary>
/// The card pools as objects and the pick that is drawn again. The numbers are the game's own:
/// the length of each pool's list in MegaCrit.Sts2.Core.Models.CardPools, the cards of the six
/// Ironclad and colorless epochs, the eleven multiplayer-only colorless cards, and the counts
/// of plan file cards-extra.json (53 colorless cards in a single-player run: 32 Uncommon and
/// 21 Rare; 15 Attacks, 26 Skills, 12 Powers). The Ironclad and status lists are held against
/// the lists the single-fight arena already had (<see cref="Completeness"/>), which were read
/// separately. A pick is held against a second stream of the same seed walked by the rule in
/// words: one index into the game's full list, and again while it lands on an unbuilt card.
/// </summary>
public sealed class CardPoolsChecks : IArenaChecks
{
    public void Run(Check c)
    {
        Lists(c);
        PoolOfACard(c);
        Unlocks(c);
        TransformOptions(c);
        DrawnAgain(c);
        LeftOut(c);
        RealRun(c);
    }

    private static CardInstance Card(CardDef def) => new RunState(new RunSetup()).NewCard(def);

    private static CardInstance Card(string className) => Card(Content.Registry.Card(className));

    private static void Lists(Check c)
    {
        c.Group("the seven pools hold the game's lists", () =>
        {
            c.Equal(87, CardPools.Ironclad.All.Count, "the Ironclad's pool");
            c.Equal(64, CardPools.Colorless.All.Count, "the colorless pool");
            c.Equal(18, CardPools.Curse.All.Count, "the curses");
            c.Equal(27, CardPools.Event.All.Count, "the event cards");
            c.Equal(3, CardPools.Quest.All.Count, "the quest cards");
            c.Equal(12, CardPools.Status.All.Count, "the status cards");
            c.Equal(14, CardPools.Token.All.Count, "the token cards");
            c.Sequence(["Ironclad", "Colorless", "Curse", "Event", "Quest", "Status", "Token"], CardPools.All.Select(p => p.Name), "in the order the game looks a card's pool up");
            c.Sequence(Completeness.IroncladPool, CardPools.Ironclad.All.Select(card => card.ClassName), "the Ironclad's list is the one the fights already had, in the game's order");
            c.Sequence(Completeness.StatusCards, CardPools.Status.All.Select(card => card.ClassName), "and so is the status list (Wither sits between Infection and Slimed)");
            c.Sequence(
                ["AscendersBane", "BadLuck", "Clumsy", "CurseOfTheBell", "Debt", "Decay", "Doubt", "Enthralled", "Folly", "Greed", "Guilty", "Injury", "Normality", "PoorSleep", "Regret", "Shame", "SporeMind", "Writhe"],
                CardPools.Curse.All.Select(card => card.ClassName), "the eighteen curses in the game's order");
            c.Sequence(["ByrdonisEgg", "LanternKey", "SpoilsMap"], CardPools.Quest.All.Select(card => card.ClassName), "the three quest cards");
            c.Equal("Alchemize", CardPools.Colorless.All[0].ClassName, "the colorless list starts with Alchemize");
            c.Equal("Volley", CardPools.Colorless.All[^1].ClassName, "and ends with Volley");
            c.Equal("Apotheosis", CardPools.Event.All[0].ClassName, "the event list starts with Apotheosis");
            c.Equal("Whistle", CardPools.Event.All[^1].ClassName, "and ends with Whistle, after Wish");
            c.That(CardPools.All.SelectMany(p => p.All).All(card => card.Rarity != CardRarity.None && card.Type != CardType.None), "every card has a type and a rarity");
        });

        c.Group("what the game's filters ask of a card is the card's own", () =>
        {
            // The game's Ironclad pool: 3 Basic cards, 2 Ancient ones, 2 multiplayer-only ones, 2 a fight never
            // generates. What is left is what random generation in a fight picks from, counted for the single fights.
            var ironclad = CardPools.Ironclad.All;
            c.Sequence(["Bash", "DefendIronclad", "StrikeIronclad"], ironclad.Where(card => card.Rarity == CardRarity.Basic).Select(card => card.ClassName), "the three Basic cards");
            c.Sequence(["Break", "Corruption"], ironclad.Where(card => card.Rarity == CardRarity.Ancient).Select(card => card.ClassName), "the two Ancient-rarity cards");
            c.Sequence(["DemonicShield", "Tank"], ironclad.Where(card => card.MultiplayerOnly).Select(card => card.ClassName), "the two multiplayer-only cards");
            c.Sequence(["Feed", "NotYet"], ironclad.Where(card => !card.GeneratedInCombat).Select(card => card.ClassName), "the two cards a fight never generates");
            var inFights = ironclad.Where(card => card.GeneratedInCombat && !card.MultiplayerOnly && card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare).ToList();
            c.Equal(Completeness.GeneratableCards, inFights.Count, "78 cards for random generation in a fight");
            c.Equal(Completeness.GeneratableAttacks, inFights.Count(card => card.Type == CardType.Attack), "33 Attacks among them");
            c.Equal(Completeness.GeneratableSkills, inFights.Count(card => card.Type == CardType.Skill), "27 Skills");
            c.Equal(Completeness.GeneratablePowers, inFights.Count(card => card.Type == CardType.Power), "18 Powers");

            // Every built Ironclad card says the same of itself as the game's list says of it.
            foreach (var card in ironclad.Where(card => CardPools.IsBuilt(card.ClassName)))
            {
                var def = Content.Registry.Card(card.ClassName);
                c.That(def.Type == card.Type && def.Rarity == card.Rarity, $"{card.ClassName}: type and rarity of the built card ({def.Type}, {def.Rarity}) and of the list ({card.Type}, {card.Rarity})");
                c.Equal(def.CanBeGeneratedInCombat, card.GeneratedInCombat, $"{card.ClassName}: may a fight generate it");
            }

            c.Sequence(
                ["BeaconOfHope", "BelieveInYou", "Coordinate", "GangUp", "HuddleUp", "Intercept", "Knockdown", "Lift", "Mimic", "Rally", "TagTeam"],
                CardPools.Colorless.All.Where(card => card.MultiplayerOnly).Select(card => card.ClassName), "the eleven multiplayer-only colorless cards");
            c.That(CardPools.Curse.All.All(card => card is { Type: CardType.Curse, Rarity: CardRarity.Curse }), "every curse is of type and rarity Curse");
            // Eight curses say of themselves that a random pick outside a fight never makes them; the other ten
            // are the ones Neow's Bones picks among.
            c.Sequence(["AscendersBane", "BadLuck", "CurseOfTheBell", "Enthralled", "Folly", "Greed", "PoorSleep", "SporeMind"],
                CardPools.Curse.All.Where(card => !card.GeneratedByModifiers).Select(card => card.ClassName), "the eight curses a random pick never makes");
            c.Sequence(["Clumsy", "Debt", "Decay", "Doubt", "Guilty", "Injury", "Normality", "Regret", "Shame", "Writhe"],
                CardPools.Curse.All.Where(card => card.GeneratedByModifiers).Select(card => card.ClassName), "the ten it can make");
            c.That(CardPools.All.Where(p => p != CardPools.Curse).SelectMany(p => p.All).All(card => card.GeneratedByModifiers), "no card of another pool says so");
            c.Equal(CardRarity.Ancient, CardPools.Event.Find("NeowsFury")!.Rarity, "Neow's Fury is of Ancient rarity");
            c.Equal(CardRarity.Event, CardPools.Event.Find("Peck")!.Rarity, "Peck of Event rarity");
            c.That(CardPools.Quest.All.All(card => card is { Type: CardType.Quest, Rarity: CardRarity.Quest }), "the quest cards are of type and rarity Quest");
            c.Equal(CardRarity.Token, CardPools.Token.Find("GiantRock")!.Rarity, "Giant Rock is a token");
        });
    }

    private static void PoolOfACard(Check c)
    {
        c.Group("a card belongs to the first pool that lists it", () =>
        {
            c.Equal(CardPools.Ironclad, CardPools.Of("Bash"), "Bash");
            c.Equal(CardPools.Colorless, CardPools.Of("Finesse"), "Finesse");
            c.Equal(CardPools.Curse, CardPools.Of("Clumsy"), "Clumsy");
            c.Equal(CardPools.Event, CardPools.Of("NeowsFury"), "Neow's Fury");
            c.Equal(CardPools.Quest, CardPools.Of("SpoilsMap"), "Spoils Map");
            c.Equal(CardPools.Status, CardPools.Of("Wound"), "Wound");
            c.Equal(CardPools.Token, CardPools.Of("GiantRock"), "Giant Rock");
            c.Equal(null, CardPools.Of("NoSuchCard"), "a card of no pool");
            c.Equal(CardPools.Ironclad, CardPools.Character, "the character's pool is the Ironclad's");
            var all = CardPools.All.SelectMany(p => p.All).Select(card => card.ClassName).ToList();
            c.Equal(87 + 64 + 18 + 27 + 3 + 12 + 14, all.Count, "225 cards in the seven pools");
            c.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count(), "and none of them twice");
            c.Equal(CardRarity.Uncommon, CardPools.Find("Finesse")!.Rarity, "an entry is found through its pool");
            c.Equal(null, CardPools.Find("NoSuchCard"), "or not at all");
            c.That(CardPools.IsBuilt("Bash") && !CardPools.IsBuilt("NoSuchCard"), "built means: in the registry");
        });
    }

    private static void Unlocks(Check c)
    {
        c.Group("what a single-player run of a profile can meet", () =>
        {
            var everything = RunUnlocks.All;
            // 87 less the two multiplayer-only cards.
            c.Equal(85, CardPools.Ironclad.Unlocked(everything).Count, "the Ironclad's pool with everything unlocked");
            var colorless = CardPools.Colorless.Unlocked(everything);
            c.Equal(53, colorless.Count, "the colorless pool: 64 less 11 multiplayer-only");
            c.Equal(32, colorless.Count(card => card.Rarity == CardRarity.Uncommon), "32 Uncommon");
            c.Equal(21, colorless.Count(card => card.Rarity == CardRarity.Rare), "21 Rare");
            c.Equal(15, colorless.Count(card => card.Type == CardType.Attack), "15 Attacks");
            c.Equal(26, colorless.Count(card => card.Type == CardType.Skill), "26 Skills");
            c.Equal(12, colorless.Count(card => card.Type == CardType.Power), "12 Powers");
            c.Equal(18, CardPools.Curse.Unlocked(everything).Count, "all curses");

            // A profile that has not revealed the Ironclad's second epoch misses its three cards, and only those.
            var without2 = new RunUnlocks(GameLists.Epochs.Where(e => e != "IRONCLAD2_EPOCH"), [], 0, []);
            var missing = CardPools.Ironclad.Unlocked(everything).Select(card => card.ClassName).Except(CardPools.Ironclad.Unlocked(without2).Select(card => card.ClassName)).ToList();
            c.Sequence(["Cruelty", "Dominate", "MoltenFist"], missing, "without the Ironclad's second epoch: Cruelty, Dominate and Molten Fist are gone");
            string[] Gated(CardPool pool, string epoch) => pool.All.Where(card => card.Epoch == epoch).Select(card => card.ClassName).ToArray();
            c.Sequence(["Cinder", "DrumOfBattle", "PactsEnd"], Gated(CardPools.Ironclad, "IRONCLAD5_EPOCH"), "the fifth epoch's cards");
            c.Sequence(["BloodWall", "Inferno", "TearAsunder"], Gated(CardPools.Ironclad, "IRONCLAD7_EPOCH"), "the seventh epoch's cards");
            c.Sequence(["Automation", "Catastrophe", "Entropy"], Gated(CardPools.Colorless, "COLORLESS1_EPOCH"), "the first colorless epoch");
            c.Sequence(["EternalArmor", "Jackpot", "PrepTime"], Gated(CardPools.Colorless, "COLORLESS2_EPOCH"), "the second");
            c.Sequence(["BeatDown", "Prowess", "Rend"], Gated(CardPools.Colorless, "COLORLESS3_EPOCH"), "the third");
            c.Sequence(["Alchemize", "Nostalgia", "Scrawl"], Gated(CardPools.Colorless, "COLORLESS4_EPOCH"), "the fourth");
            c.Sequence(["Anointed", "Calamity", "Splash"], Gated(CardPools.Colorless, "COLORLESS5_EPOCH"), "the fifth");
            c.Equal(24, CardPools.All.SelectMany(p => p.All).Count(card => card.Epoch is not null), "24 cards behind an epoch in all: three for each of the Ironclad's three epochs and of the five colorless ones");
            var nothing = new RunUnlocks([], [], 0, []);
            c.Equal(85 - 9, CardPools.Ironclad.Unlocked(nothing).Count, "a new profile: nine Ironclad cards fewer");
            c.Equal(53 - 15, CardPools.Colorless.Unlocked(nothing).Count, "and fifteen colorless cards fewer");
        });
    }

    private static void TransformOptions(Check c)
    {
        c.Group("what a card can be transformed into", () =>
        {
            var all = RunUnlocks.All;
            // The Ironclad's pool less Basic (3), Ancient (2) and multiplayer-only (2) cards: 80. In the game's order.
            string[] never = ["Bash", "DefendIronclad", "StrikeIronclad", "Break", "Corruption", "DemonicShield", "Tank"];
            var expected = Completeness.IroncladPool.Where(name => !never.Contains(name)).ToList();
            c.Equal(80, expected.Count, "80 Common, Uncommon and Rare Ironclad cards of a single-player run");

            var strike = CardPools.TransformOptions(Card("StrikeIronclad"), all);
            c.Sequence(expected, strike.Select(card => card.ClassName), "a Strike: any of them, in the pool's order");
            var anger = CardPools.TransformOptions(Card("Anger"), all);
            c.Sequence(expected.Where(name => name != "Anger"), anger.Select(card => card.ClassName), "Anger: the same without Anger itself (79)");
            var upgradedStrike = Card("StrikeIronclad");
            upgradedStrike.ApplyUpgrade();
            c.Sequence(expected, CardPools.TransformOptions(upgradedStrike, all).Select(card => card.ClassName), "an upgraded Strike: the same list");

            // A curse becomes any other curse: no rarity filter, the five transform-only curses included.
            var clumsy = CardPools.TransformOptions(Card(DeckCheckKit.Curse("Clumsy", "Clumsy")), all);
            c.Equal(17, clumsy.Count, "a curse: the other seventeen");
            c.That(clumsy.All(card => card.ClassName != "Clumsy"), "never itself");
            c.That(clumsy.Any(card => card.ClassName == "AscendersBane") && clumsy.Any(card => card.ClassName == "Folly"), "Ascender's Bane and Folly are among them");
            c.Equal("AscendersBane", clumsy[0].ClassName, "in the pool's order");

            // Quest cards and cards of Event, Ancient or Token rarity become colorless cards.
            var colorless = CardPools.Colorless.All.Where(card => !card.MultiplayerOnly).Select(card => card.ClassName).ToList();
            c.Equal(53, colorless.Count, "53 colorless cards, all Uncommon or Rare");
            CardDef Make(string className, CardType type, CardRarity rarity) => new(className, className, type, rarity, 1, TargetType.Self);
            c.Sequence(colorless, CardPools.TransformOptions(Card(Make("NeowsFury", CardType.Attack, CardRarity.Ancient)), all).Select(card => card.ClassName), "Neow's Fury (Ancient rarity): a colorless card");
            c.Sequence(colorless, CardPools.TransformOptions(Card(Make("Peck", CardType.Attack, CardRarity.Event)), all).Select(card => card.ClassName), "Peck (Event rarity): a colorless card");
            c.Sequence(colorless, CardPools.TransformOptions(Card(Make("SpoilsMap", CardType.Quest, CardRarity.Quest)), all).Select(card => card.ClassName), "Spoils Map (a Quest card): a colorless card");
            c.Sequence(colorless, CardPools.TransformOptions(Card("GiantRock"), all).Select(card => card.ClassName), "Giant Rock (Token rarity): a colorless card");
            c.Sequence(colorless, CardPools.TransformOptions(Card("Break"), all).Select(card => card.ClassName), "Break (an Ironclad card of Ancient rarity): a colorless card too");
            c.Sequence(colorless.Where(name => name != "Finesse"), CardPools.TransformOptions(Card(Make("Finesse", CardType.Skill, CardRarity.Uncommon)), all).Select(card => card.ClassName),
                "a colorless card: another colorless card");

            // A card of Status rarity keeps no rarity filter either: any other card of its own pool.
            c.Sequence(Completeness.StatusCards.Where(name => name != "Wound"), CardPools.TransformOptions(Card("Wound"), all).Select(card => card.ClassName), "a Wound: any other status card");

            // The profile counts: without the fifth colorless epoch Anointed, Calamity and Splash cannot come.
            var without5 = new RunUnlocks(GameLists.Epochs.Where(e => e != "COLORLESS5_EPOCH"), [], 0, []);
            c.Equal(50, CardPools.TransformOptions(Card("GiantRock"), without5).Count, "fewer colorless cards for a profile that has not revealed them all");

            var threw = "";
            try { CardPools.TransformOptions(Card(Make("CheckOnly", CardType.Skill, CardRarity.Common)), all); }
            catch (InvalidOperationException ex) { threw = ex.Message; }
            c.Equal("CheckOnly is in no card pool, so it cannot be transformed at random.", threw, "a card of no pool cannot be transformed at random");
        });
    }

    private static void RealRun(Check c)
    {
        c.Group("the Ironclad's list in its order gives the cards the real game gave (seed 1BUSLN0FY5, Mark's saves and run history of 2026-10-11)", () =>
        {
            // The player's rewards stream of that seed, number by number. A pick is an index into the game's list,
            // so one card too many, too few or out of place in the list would give other cards than the game showed.
            var rewards = new GameRng(RunStreams.LongSeed(new RunSeed("1BUSLN0FY5"), RunStream.Rewards));
            var pool = CardPools.Ironclad.Unlocked(RunUnlocks.All);

            // Neow's Arcane Scroll: one pick among the Rares and nothing else. The save after the pick has
            // Barricade in the deck and the rewards stream at 1.
            var rares = pool.Where(card => card.Rarity == CardRarity.Rare).ToList();
            c.Equal(25, rares.Count, "25 Rare Ironclad cards in a single-player run (26 less Tank)");
            c.Equal("Barricade", rares[rewards.NextInt(0, rares.Count)].ClassName, "Arcane Scroll gave Barricade");
            c.Equal(1, rewards.Counter, "with one number");

            // A hallway fight's rewards in the game's order (plan 1.6): the potion-chance roll, the gold (10 to 20),
            // two numbers for the potion if one dropped, then per card a rarity roll, a pick among the cards of
            // that rarity not offered yet, and an upgrade roll.
            var state = new RunState(new RunSetup { Seed = "1BUSLN0FY5" });
            (bool Potion, int Gold, string Cards) Fight()
            {
                var potion = RunWorld.RollPotionDrop(state, rewards, elite: false);
                var gold = rewards.NextInt(10, 21);
                if (potion)
                {
                    rewards.NextDouble();
                    rewards.NextDouble();
                }
                var offered = new List<string>();
                for (var n = 0; n < 3; n++)
                {
                    var rarity = RunWorld.RollCardRarity(state, rewards, CardOddsType.Regular);
                    var options = pool.Where(card => card.Rarity == rarity && !offered.Contains(card.ClassName)).ToList();
                    offered.Add(options[rewards.NextInt(0, options.Count)].ClassName);
                    rewards.NextDouble();
                }
                return (potion, gold, string.Join(" / ", offered));
            }

            c.Equal((false, 11, "Colossus / Unrelenting / Havoc"), Fight(), "floor 2: no potion, 11 gold, Colossus, Unrelenting and Havoc were offered");
            c.Equal(12, rewards.Counter, "the save after floor 2 has the rewards stream at 12");
            c.Equal((true, 10, "FightMe / StoneArmor / SetupStrike"), Fight(), "floor 3: a potion, 10 gold, Fight Me!, Stone Armor and Setup Strike were offered");
            c.Equal(25, rewards.Counter, "the save after floor 3 has the rewards stream at 25");
        });
    }

    /// <summary>The rule in words, on a second stream: an index into the full list; again while it lands on an unbuilt card.</summary>
    private static (string? Landed, List<string> PassedOver, int Draws) Walk(ulong seed, IReadOnlyList<GameCard> options)
    {
        var passed = new List<string>();
        if (!options.Any(card => Content.Registry.Has(ContentKind.Card, card.ClassName))) return (null, passed, 0);
        var twin = new GameRng(seed);
        while (true)
        {
            var index = (int)(twin.NextDouble() * options.Count);
            var name = options[index].ClassName;
            if (Content.Registry.Has(ContentKind.Card, name)) return (name, passed, twin.Counter);
            passed.Add(name);
        }
    }

    private static readonly IReadOnlyList<GameCard> Mixed =
    [
        new("NeverBuiltA", CardType.Skill, CardRarity.Common), new("Bash", CardType.Attack, CardRarity.Basic),
        new("NeverBuiltB", CardType.Skill, CardRarity.Common), new("Anger", CardType.Attack, CardRarity.Common),
        new("NeverBuiltC", CardType.Skill, CardRarity.Common), new("NeverBuiltD", CardType.Skill, CardRarity.Common),
    ];

    private static void DrawnAgain(Check c)
    {
        c.Group("a pick runs over the game's full list and is drawn again when it lands on a card that is not built", () =>
        {
            var redraws = 0;
            var straight = 0;
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var (landed, passedOver, draws) = Walk(seed, Mixed);
                var rng = new GameRng(seed);
                var passed = new List<string>();
                var def = CardPools.NextBuilt(rng, Mixed, passed);
                c.Equal(landed, def?.ClassName, $"seed {seed}: the card");
                c.Sequence(passedOver, passed, $"seed {seed}: what was passed over");
                c.Equal(draws, rng.Counter, $"seed {seed}: one number per try");
                if (passed.Count > 0) redraws++;
                else straight++;
            }
            c.That(redraws >= 10 && straight >= 5, $"both ways came up: {redraws} seeds drew again, {straight} landed at once (four of six cards are not built)");

            // A list of built cards only takes exactly one number, as in the game.
            var options = CardPools.TransformOptions(Card("StrikeIronclad"), RunUnlocks.All);
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var rng = new GameRng(seed);
                var twin = new GameRng(seed);
                var def = CardPools.NextBuilt(rng, options);
                c.Equal(options[(int)(twin.NextDouble() * 80)].ClassName, def!.ClassName, $"seed {seed}: the Strike's replacement is entry [fraction x 80] of the 80");
                c.Equal(1, rng.Counter, $"seed {seed}: one number");
            }

            IReadOnlyList<GameCard> none = [new("NeverBuiltA", CardType.Skill, CardRarity.Common), new("NeverBuiltB", CardType.Skill, CardRarity.Common)];
            var untouched = new GameRng(7);
            c.Equal(null, CardPools.NextBuilt(untouched, none), "a list with nothing built gives nothing");
            c.Equal(0, untouched.Counter, "and takes no number");
            var threw = false;
            try { CardPools.NextBuilt(new GameRng(7), []); }
            catch (ArgumentException) { threw = true; }
            c.That(threw, "an empty list is refused: the game has nothing to pick from either");
        });
    }

    private static void LeftOut(Check c)
    {
        c.Group("a run writes down what it passed over, and stops at once in strict mode", () =>
        {
            // Seed by seed, until one lands on an unbuilt card first.
            ulong seed = 1;
            while (Walk(seed, Mixed).PassedOver.Count == 0) seed++;
            var (landed, passedOver, draws) = Walk(seed, Mixed);

            var run = DeckCheckKit.Outside();
            run.State.TotalFloor = 4;
            c.Equal(null, run.State.FindPart<LeftOutLog>(), "a new run has no log");
            var rng = new GameRng(seed);
            var def = CardPools.Pick(run, rng, Mixed, "a check's pick");
            c.Equal(landed, def.ClassName, "the pick of a run lands where the rule says");
            c.Equal(draws, rng.Counter, "with the redraws from the same stream");
            var log = run.State.FindPart<LeftOutLog>();
            c.That(log is not null, "the log is there now");
            c.Sequence(passedOver.Select(name => $"4:card:{name}:a check's pick"), log!.Entries.Select(e => $"{e.Floor}:{e.Kind}:{e.ClassName}:{e.What}"), "one entry per card passed over, with the floor");
            c.Equal(4, log.FirstFloor, "the first floor on which the run drew again");

            var copy = run.Clone();
            run.State.TotalFloor = 9;
            CardPools.Pick(run, new GameRng(seed), Mixed, "another pick");
            c.Equal(passedOver.Count, copy.State.FindPart<LeftOutLog>()!.Entries.Count, "a copy of the run keeps its own log");
            c.Equal(2 * passedOver.Count, log.Entries.Count, "while the run's grows");
            c.Equal(4, log.FirstFloor, "the first floor stays");
            c.That(run.Dump() != copy.Dump(), "and the log is in the run's dump");

            // A list that is all built never touches the log.
            var clean = DeckCheckKit.Outside();
            CardPools.Pick(clean, new GameRng(3), CardPools.TransformOptions(Card("StrikeIronclad"), RunUnlocks.All), "a transform of Strike");
            c.Equal(null, clean.State.FindPart<LeftOutLog>(), "a pick among built cards leaves no log");

            // Strict: the game's streams with nothing drawn again.
            var strict = DeckCheckKit.Outside();
            strict.State.TotalFloor = 6;
            strict.State.Part<LeftOutLog>().Strict = true;
            var reason = "";
            try { CardPools.Pick(strict, new GameRng(seed), Mixed, "a check's pick"); }
            catch (RunUnrepresentableException ex) { reason = ex.Message; }
            c.Equal($"a check's pick on floor 6 landed on the card {passedOver[0]}, which the arena leaves out", reason, "strict mode ends the run on the first unbuilt card, with the reason");
            c.Equal(0, strict.State.Part<LeftOutLog>().Entries.Count, "and writes nothing down");
            c.That(strict.Clone().State.Part<LeftOutLog>().Strict, "a copy stays strict");

            // Nothing built at all: no redraw can help, in either mode.
            IReadOnlyList<GameCard> none = [new("NeverBuiltA", CardType.Skill, CardRarity.Common), new("NeverBuiltB", CardType.Skill, CardRarity.Common)];
            var stuck = DeckCheckKit.Outside();
            stuck.State.TotalFloor = 2;
            var stream = new GameRng(1);
            reason = "";
            try { CardPools.Pick(stuck, stream, none, "a transform of Clumsy"); }
            catch (RunUnrepresentableException ex) { reason = ex.Message; }
            c.Equal("a transform of Clumsy on floor 2 can only land on cards the arena leaves out (NeverBuiltA, NeverBuiltB)", reason, "a pick that can only land on unbuilt cards ends the run as unrepresentable");
            c.Equal(0, stream.Counter, "before any number is drawn");

            c.Equal(RunUnlocks.All, CardPools.UnlocksOf(run), "a run without a world rolls with everything unlocked");
            var withWorld = DeckCheckKit.Outside(new RunSetup { Seed = "1BUSLN0FY5" });
            var profile = new RunUnlocks(GameLists.Epochs.Where(e => e != "COLORLESS5_EPOCH"), [], 0, ["Overgrowth"]);
            RunWorld.Begin(withWorld.State, profile, acts: ["Overgrowth", "Hive", "Glory"]);
            c.Equal(profile, CardPools.UnlocksOf(withWorld), "a run with a world rolls with the world's profile");
        });
    }
}
