using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// The 18 curses and Neow's Fury (<see cref="Curses"/>), and the two engine pieces they brought:
/// a deck card removed from inside a fight (<see cref="Fight.RemoveFromDeck"/>) and the order in
/// which a fight's grid choice hands its picks back (<see cref="ChoicePicks"/>).
/// <para>
/// Every expected value is worked out by hand from the card's class in the game
/// (MegaCrit.Sts2.Core.Models.Cards) and the rules the checked content already stands on:
/// Strike 6, Defend 5, Bash 8 and 2 Vulnerable, Weak and Frail 0.75 and truncated, a new debuff
/// on the player skips its first tick, Ethereal cards are exhausted before the end-of-turn
/// cards resolve, deck cards are asked before relics. The titles are the game's English names;
/// the texts are the arena's own wording and are held here so that a change shows.
/// </para>
/// </summary>
public sealed class CursesChecks : IArenaChecks
{
    public void Run(Check c)
    {
        TheCards(c);
        Pools(c);
        KeywordCurses(c);
        EndOfTurnCurses(c);
        DoubtAndShame(c);
        Regret(c);
        Normality(c);
        Enthralled(c);
        GuiltyOverARun(c);
        GuiltyBesideOthers(c);
        RemoveFromDeckInAFight(c);
        NeowsFury(c);
        NeowsFuryChoice(c);
        PickOrder(c);
        EternalAndTransforms(c);
        RealGame(c);
        EveryCardInRealFights(c);
    }

    // ---------- helpers ----------

    private const string S = "StrikeIronclad";
    private const string D = "DefendIronclad";

    private static readonly JsonSerializerOptions Plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>JSON as compact text with quotes, apostrophes and "+" written as they are.</summary>
    private static string Text(JsonNode? node) => node is null ? "null" : node.ToJsonString(Plain);

    private static string[] Cards(params object[] parts)
    {
        var list = new List<string>();
        foreach (var part in parts)
        {
            if (part is string one) list.Add(one);
            else if (part is (string name, int count)) list.AddRange(Enumerable.Repeat(name, count));
        }
        return list.ToArray();
    }

    /// <summary>A fight against one idle 100 HP dummy, the cards drawn in exactly this order.</summary>
    private static Fight Vs(params string[] drawOrder) => new FightBuilder().DrawOrder(drawOrder).Enemy(new CheckDummy()).Start();

    private static string HandEntry(Fight f, int i) => Text(f.Snapshot().State["hand"]![i]);

    private static string Ids(Fight f) => string.Join(" ", f.LegalActions().Select(a => a.Id));

    private static string Labels(Fight f) => string.Join(" | ", f.LegalActions().Select(a => $"{a.Id}={a.Label}"));

    private static List<string> Titles(IEnumerable<CardInstance> cards) => cards.Select(card => card.Title).ToList();

    private static string DeckText(string className, bool upgraded = false)
    {
        var card = new CardInstance(0, Content.Registry.Card(className));
        if (upgraded) card.ApplyUpgrade();
        return Fight.CardText(null, card, PileType.Deck);
    }

    private static bool Throws<T>(Action body) where T : Exception
    {
        try
        {
            body();
        }
        catch (T)
        {
            return true;
        }
        return false;
    }

    private static void Do(Arena.Run run, params string[] ids)
    {
        foreach (var id in ids)
        {
            var result = run.Execute(id);
            if (!result.Ok)
                throw new InvalidOperationException($"'{id}' was refused: {result.Error} Legal now: {string.Join(", ", run.LegalActions().Select(a => a.Id))}");
        }
    }

    /// <summary>A fight of a run against one dummy that a single Strike kills; the deck is drawn in deck order.</summary>
    private static FightRequest OneStrikeFight() => new()
    {
        Monsters = () => [new PlacedMonster(new CheckDummy { Hp = 6 })],
        ShuffleDeck = false,
    };

    /// <summary>Wins a <see cref="OneStrikeFight"/>: plays the first Strike of the hand.</summary>
    private static void WinWithAStrike(Arena.Run run)
    {
        var fight = run.Fight ?? throw new InvalidOperationException("No fight is on.");
        Do(run, $"play:{fight.HandIndex("Strike")}>e0");
    }

    private static CardInstance? DeckCardOf(Arena.Run run, string className) => run.Deck.FirstOrDefault(card => card.Def.ClassName == className);

    /// <summary>A run that is a list of steps: each does something to the run and may name a fight to start;
    /// the next step follows the fight (or follows at once). Cleared after the last.</summary>
    private sealed class Steps(params Func<Arena.Run, FightRequest?>[] steps) : RunDirector
    {
        private int _next;

        public override void Begin(Arena.Run run) => Next(run);

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => Next(run);

        private void Next(Arena.Run run)
        {
            while (_next < steps.Length)
            {
                var fight = steps[_next++](run);
                if (run.IsOver) return;
                if (fight is null) continue;
                run.State.ActFloor++;
                run.State.TotalFloor++;
                run.StartFight(fight);
                return;
            }
            run.End(RunResult.Cleared);
        }
    }

    /// <summary>A relic of the checks that writes down what it hears of a fight's end and of a card leaving the deck.</summary>
    private sealed class Watcher : Relic, IDeckListener
    {
        public string Log = "";

        public override string Name => "Watcher";
        public override string Describe() => "Writes down what it hears.";

        public override void AfterCombatEnd(Fight f) => Log += $"end:{f.DeckCards.Count} ";

        void IDeckListener.BeforeCardRemoved(RunContext c, CardInstance card) =>
            Log += $"removing:{card.Def.ClassName}:{c.Deck.Count}:{(c.InFight ? "fight" : "run")} ";
    }

    // ---------- the cards as the game's classes state them ----------

    private sealed record Row(string Class, string Title, int Cost, CardKeywords Keywords, string Text);

    private const CardKeywords Un = CardKeywords.Unplayable;

    /// <summary>Read off the eighteen classes of the curse pool: constructor, CanonicalKeywords, CanonicalVars.</summary>
    private static readonly Row[] CurseTable =
    [
        new("AscendersBane", "Ascender's Bane", -1, Un | CardKeywords.Eternal | CardKeywords.Ethereal, "Unplayable. / Ethereal. / Eternal."),
        new("BadLuck", "Bad Luck", -1, Un | CardKeywords.Eternal, "Unplayable. / At the end of your turn, if this is in your hand, lose 13 HP. / Eternal."),
        new("Clumsy", "Clumsy", -1, Un | CardKeywords.Ethereal, "Unplayable. / Ethereal."),
        new("CurseOfTheBell", "Curse of the Bell", -1, Un | CardKeywords.Eternal, "Unplayable. / Eternal."),
        new("Debt", "Debt", -1, Un, "Unplayable. / At the end of your turn, if this is in your hand, lose 10 gold (all of it if you have less)."),
        new("Decay", "Decay", -1, Un, "Unplayable. / At the end of your turn, if this is in your hand, take 2 damage."),
        new("Doubt", "Doubt", -1, Un, "Unplayable. / At the end of your turn, if this is in your hand, gain 1 Weak."),
        new("Enthralled", "Enthralled", 2, CardKeywords.Eternal, "While this is in your hand, you cannot play any other card until you have played this one. / Eternal."),
        new("Folly", "Folly", -1, Un | CardKeywords.Eternal | CardKeywords.Innate | CardKeywords.Ethereal, "Unplayable. / Innate. / Ethereal. / Eternal."),
        new("Greed", "Greed", -1, Un | CardKeywords.Eternal, "Unplayable. / Eternal."),
        new("Guilty", "Guilty", -1, Un, "Unplayable. / Leaves your deck for good after 5 more fights."),
        new("Injury", "Injury", -1, Un, "Unplayable."),
        new("Normality", "Normality", -1, Un, "Unplayable. / While this is in your hand, no card can be played once 3 cards have been played this turn."),
        new("PoorSleep", "Poor Sleep", -1, Un | CardKeywords.Retain, "Unplayable. / Retain."),
        new("Regret", "Regret", -1, Un, "Unplayable. / At the end of your turn, if this is in your hand, lose 1 HP for each card in your hand (this one counts; counted before any card leaves the hand)."),
        new("Shame", "Shame", -1, Un, "Unplayable. / At the end of your turn, if this is in your hand, gain 1 Frail."),
        new("SporeMind", "Spore Mind", 1, CardKeywords.Exhaust, "Exhaust."),
        new("Writhe", "Writhe", -1, Un | CardKeywords.Innate, "Unplayable. / Innate."),
    ];

    private static void TheCards(Check c)
    {
        c.Group("the eighteen curses, as their classes in the game state them", () =>
        {
            c.Sequence(CardPools.Curse.All.Select(card => card.ClassName), CurseTable.Select(row => row.Class), "the table is the game's curse pool, in its order");
            foreach (var row in CurseTable)
            {
                var def = Content.Registry.Card(row.Class);
                c.Equal(row.Title, def.Name, $"{row.Class}: the game's English title");
                c.Equal(CardType.Curse, def.Type, $"{row.Class}: a Curse");
                c.Equal(CardRarity.Curse, def.Rarity, $"{row.Class}: of Curse rarity");
                c.Equal(row.Cost, def.Cost, $"{row.Class}: cost");
                c.Equal(TargetType.None, def.Target, $"{row.Class}: no target");
                c.Equal(row.Keywords, def.Keywords, $"{row.Class}: keywords");
                c.Equal(0, def.MaxUpgradeLevel, $"{row.Class}: cannot be upgraded");
                c.That(def.Play is null, $"{row.Class}: playing it does nothing");
                c.Equal(row.Text, DeckText(row.Class), $"{row.Class}: its text in the deck");
            }

            // The six that act at the end of the turn, and no other.
            c.Sequence(["BadLuck", "Debt", "Decay", "Doubt", "Regret", "Shame"],
                CurseTable.Where(row => Content.Registry.Card(row.Class).HasTurnEndInHandEffect).Select(row => row.Class), "the six with an end-of-turn effect");
            // The numbers the classes declare.
            c.Equal(13m, new CardInstance(0, Content.Registry.Card("BadLuck")).Get("HpLoss"), "Bad Luck: HpLoss 13");
            c.Equal(10m, new CardInstance(0, Content.Registry.Card("Debt")).Get("Gold"), "Debt: Gold 10");
            c.Equal(2m, new CardInstance(0, Content.Registry.Card("Decay")).Get("Damage"), "Decay: Damage 2");
            c.Equal(Props.CardUnpowered, Content.Registry.Card("Decay").Vars["Damage"].Props, "Decay: its damage is Unpowered and Move");
            c.Equal(1m, new CardInstance(0, Content.Registry.Card("Doubt")).Get("WeakPower"), "Doubt: 1 Weak");
            c.Equal(1m, new CardInstance(0, Content.Registry.Card("Shame")).Get("Frail"), "Shame: 1 Frail");
            c.Equal(5m, new CardInstance(0, Content.Registry.Card("Guilty")).Get("Combats"), "Guilty: 5 fights");
            c.Equal(5, Curses.GuiltyCombats, "Guilty's five is the game's maxCombats");
            c.Equal(3, Curses.NormalityCardsPerTurn, "Normality's three is the game's constant");
        });

        c.Group("Neow's Fury, as its class states it", () =>
        {
            var def = Content.Registry.Card("NeowsFury");
            c.Equal("Neow's Fury", def.Name, "the game's English title");
            c.Equal(CardType.Attack, def.Type, "an Attack");
            c.Equal(CardRarity.Ancient, def.Rarity, "of Ancient rarity");
            c.Equal(1, def.Cost, "cost 1");
            c.Equal(TargetType.AnyEnemy, def.Target, "aimed at one enemy");
            c.Equal(CardKeywords.Exhaust, def.Keywords, "Exhaust");
            c.Equal(1, def.MaxUpgradeLevel, "can be upgraded once");
            c.That(!def.CanBeGeneratedInCombat && !def.IsGeneratableInCombat, "never made by random generation in a fight");
            var card = new CardInstance(0, def);
            c.Equal(10m, card.Get("Damage"), "10 damage");
            c.Equal(2m, card.Get("Cards"), "2 cards");
            c.Equal("Deal 10 damage. Then put up to 2 cards of your choice from your discard pile into your hand. / Exhaust.", DeckText("NeowsFury"), "its text");
            card.ApplyUpgrade();
            c.Equal("Neow's Fury+", card.Title, "the upgraded title");
            c.Equal(14m, card.Get("Damage"), "upgraded: 4 more damage");
            c.Equal(3m, card.Get("Cards"), "upgraded: 1 more card");
            c.Equal(1, card.BaseCost, "the upgrade leaves the cost");
            c.That(card.Has(CardKeywords.Exhaust), "and the Exhaust");
            c.Equal("Deal 14 damage. Then put up to 3 cards of your choice from your discard pile into your hand. / Exhaust.", DeckText("NeowsFury", upgraded: true), "the upgraded text");
        });
    }

    private static void Pools(Check c)
    {
        c.Group("the pools and the completeness lists hold the new cards", () =>
        {
            foreach (var row in CurseTable)
            {
                var listed = CardPools.Curse.Find(row.Class);
                c.That(listed is { Type: CardType.Curse, Rarity: CardRarity.Curse }, $"{row.Class}: the curse pool lists it as a Curse of Curse rarity");
            }
            c.Equal(18, CardPools.Curse.Built.Count, "all eighteen curses are built");
            c.Equal(CardPools.Event, CardPools.Of("NeowsFury"), "Neow's Fury is in the event pool");
            c.That(CardPools.Event.Find("NeowsFury") is { Type: CardType.Attack, Rarity: CardRarity.Ancient, GeneratedInCombat: false }, "listed there as an Ancient-rarity Attack that fights never generate");
            c.That(CardPools.IsBuilt("NeowsFury"), "and built");

            // Eight curses a random pick outside a fight never makes (CanBeGeneratedByModifiers false in their classes).
            c.Sequence(["AscendersBane", "BadLuck", "CurseOfTheBell", "Enthralled", "Folly", "Greed", "PoorSleep", "SporeMind"],
                CardPools.Curse.All.Where(card => !card.GeneratedByModifiers).Select(card => card.ClassName), "the eight curses Neow's Bones cannot give");

            var rows = Completeness.Rows();
            var curses = rows.Single(row => row.Group == "curses");
            c.Equal(18, curses.Wanted.Count, "the curse row wants the whole pool");
            c.Equal(18, curses.Registered.Count, "and all of it is registered");
            var events = rows.Single(row => row.Group == "event and quest cards an act 1 run can meet");
            c.Sequence(["ByrdSwoop", "NeowsFury", "Peck", "ToricToughness", "ByrdonisEgg", "SpoilsMap"], events.Wanted, "the six event and quest cards in reach");
            c.Sequence(["NeowsFury"], events.Registered, "one is built");
            c.Sequence(["ByrdSwoop", "Peck", "ToricToughness", "ByrdonisEgg", "SpoilsMap"], events.LeftOut.Select(left => left.Name), "the other five are left out by name");
            c.That(events.LeftOut.All(left => left.Reason.StartsWith("not built yet", StringComparison.Ordinal)), "each with the reason that it is not built yet");
            c.Equal(0, Completeness.Problems().Count, "nothing is missing and nothing is astray");

            // Random generation in a fight picks from the Ironclad's own cards only.
            var pool = Content.Registry.IroncladPool.Select(def => def.ClassName).ToHashSet(StringComparer.Ordinal);
            c.That(!pool.Contains("NeowsFury") && CurseTable.All(row => !pool.Contains(row.Class)), "none of the new cards is in the Ironclad's pool");
            c.Equal(0, DeckListening.Problems().Count, "no card that listens in the deck overrides a hook of the fight alone");
            c.That(Content.Registry.Card("Guilty").Behaviour!() is IListensInDeck, "Guilty listens as the card in the deck");
            foreach (var name in new[] { "Regret", "Normality", "Enthralled" })
                c.That(Content.Registry.Card(name).Behaviour!() is not IListensInDeck, $"{name} acts only as a card in a pile");
        });
    }

    // ---------- keywords only ----------

    private static void KeywordCurses(Check c)
    {
        c.Group("Clumsy: unplayable, and exhausted at the end of the turn", () =>
        {
            var f = Vs(Cards("Clumsy", (S, 4), (D, 4), "Bash"));
            c.Equal("""{"i":0,"name":"Clumsy","cost":"-","type":"Curse","text":"Unplayable. / Ethereal.","playable":false,"unplayable":"HasUnplayableKeyword"}""",
                HandEntry(f, 0), "the hand entry");
            c.Equal("play:1>e0 play:2>e0 play:3>e0 play:4>e0 end_turn", Ids(f), "no action plays it");
            c.That(!f.Execute("play:0").Ok, "and playing it is refused");
            f.EndTurn();
            c.Sequence(["Clumsy"], Titles(f.ExhaustPile), "Ethereal: exhausted");
            c.Sequence(["Strike", "Strike", "Strike", "Strike"], Titles(f.DiscardPile), "the rest of the hand was discarded");
        });

        c.Group("Ascender's Bane and Folly are Ethereal too; Folly is Innate", () =>
        {
            var bane = Vs(Cards("AscendersBane", (S, 4), (D, 4), "Bash"));
            bane.EndTurn();
            c.Sequence(["Ascender's Bane"], Titles(bane.ExhaustPile), "Ascender's Bane is exhausted at the end of the turn");

            var folly = Vs(Cards((S, 5), (D, 2), "Folly", (D, 2), "Bash"));
            c.Sequence(["Folly", "Strike", "Strike", "Strike", "Strike"], folly.HandTitles(), "Folly was the eighth card and is in the first hand");
            folly.EndTurn();
            c.Sequence(["Folly"], Titles(folly.ExhaustPile), "and is exhausted at the end of the turn");
        });

        c.Group("Injury, Greed and Curse of the Bell: unplayable, discarded with the hand", () =>
        {
            foreach (var name in new[] { "Injury", "Greed", "CurseOfTheBell" })
            {
                var f = Vs(Cards(name, (S, 4), (D, 4), "Bash"));
                c.Equal("play:1>e0 play:2>e0 play:3>e0 play:4>e0 end_turn", Ids(f), $"{name}: no action plays it");
                f.EndTurn();
                c.Equal(0, f.ExhaustPile.Count, $"{name}: not exhausted");
                c.Equal(Content.Registry.Card(name).Name, f.DiscardPile[0].Title, $"{name}: first into the discard pile");
                c.Equal(80, f.Player.Hp, $"{name}: nothing else happens");
            }
        });

        c.Group("Poor Sleep: stays in the hand from turn to turn", () =>
        {
            var f = Vs(Cards("PoorSleep", (S, 4), (D, 4), "Bash"));
            c.Equal("""{"i":0,"name":"Poor Sleep","cost":"-","type":"Curse","text":"Unplayable. / Retain.","playable":false,"unplayable":"HasUnplayableKeyword"}""",
                HandEntry(f, 0), "the hand entry");
            f.EndTurn();
            c.Sequence(["Poor Sleep", "Defend", "Defend", "Defend", "Defend", "Bash"], f.HandTitles(), "retained, with five new cards beside it");
            c.Sequence(["Strike", "Strike", "Strike", "Strike"], Titles(f.DiscardPile), "only the Strikes were discarded");
            f.EndTurn();
            c.Equal("Poor Sleep", f.Hand[0].Title, "and again on the third turn");
        });

        c.Group("Writhe: Innate, so it is in the first hand wherever it was shuffled", () =>
        {
            var f = Vs(Cards((S, 5), (D, 2), "Writhe", (D, 2), "Bash"));
            c.Sequence(["Writhe", "Strike", "Strike", "Strike", "Strike"], f.HandTitles(), "the eighth card of the pile is drawn first");
            f.EndTurn();
            c.Sequence(["Writhe", "Strike", "Strike", "Strike", "Strike"], Titles(f.DiscardPile), "discarded like any card");
        });

        c.Group("Spore Mind: costs 1, does nothing, Exhaust", () =>
        {
            var f = Vs(Cards("SporeMind", (S, 4), (D, 4), "Bash"));
            c.Equal("""{"i":0,"name":"Spore Mind","cost":"1","type":"Curse","text":"Exhaust.","playable":true}""", HandEntry(f, 0), "the hand entry");
            c.Equal("play:0=Play Spore Mind", Labels(f).Split(" | ")[0], "it is played without a target");
            f.Do("play:0");
            c.Equal(2, f.Energy, "1 energy paid");
            c.Sequence(["Spore Mind"], Titles(f.ExhaustPile), "exhausted");
            c.Equal(100, f.Enemy(0).Hp, "the enemy is untouched");
            c.Equal(80, f.Player.Hp, "and so is the player");
            c.Equal(0, f.Player.Block, "no Block");
            c.Equal(4, f.Hand.Count, "four cards left");

            var kept = Vs(Cards("SporeMind", (S, 4), (D, 4), "Bash"));
            kept.EndTurn();
            c.Equal("Spore Mind", kept.DiscardPile[0].Title, "left in the hand it is discarded, not exhausted");
        });
    }

    // ---------- end of turn in hand ----------

    private static void EndOfTurnCurses(Check c)
    {
        c.Group("Decay: 2 damage at the end of the turn, blockable, never modified", () =>
        {
            var plain = Vs(Cards("Decay", D, (S, 3), (S, 5)));
            c.Equal("""{"i":0,"name":"Decay","cost":"-","type":"Curse","text":"Unplayable. / At the end of your turn, if this is in your hand, take 2 damage.","playable":false,"unplayable":"HasUnplayableKeyword"}""",
                HandEntry(plain, 0), "the hand entry");
            plain.EndTurn();
            c.Equal(78, plain.Player.Hp, "2 HP lost with no Block");
            c.Equal("Decay", plain.DiscardPile[0].Title, "then it goes to the discard pile");

            var blocked = Vs(Cards("Decay", D, (S, 3), (S, 5)));
            blocked.Play("Defend");
            blocked.EndTurn();
            c.Equal(80, blocked.Player.Hp, "5 Block takes the 2");

            var modified = Vs(Cards("Decay", D, (S, 3), (S, 5)));
            modified.ApplyPower<VulnerablePower>(modified.Player, 2, null);
            modified.ApplyPower<StrengthPower>(modified.Player, 5, modified.Player);
            c.That(HandEntry(modified, 0).Contains("take 2 damage"), "the text still says 2 with Strength on the dealer");
            modified.EndTurn();
            c.Equal(78, modified.Player.Hp, "Vulnerable on the player and Strength on the player change nothing: still 2");

            var away = Vs(Cards((S, 5), "Decay", (S, 4)));
            away.EndTurn();
            c.Equal(80, away.Player.Hp, "in the draw pile it does nothing");
        });

        c.Group("Bad Luck: lose 13 HP at the end of the turn, through Block", () =>
        {
            var f = Vs(Cards("BadLuck", D, (S, 3), (S, 5)));
            f.Play("Defend");
            c.Equal(5, f.Player.Block, "5 Block is up");
            f.EndTurn();
            c.Equal(67, f.Player.Hp, "13 HP lost all the same");
            c.Equal("Bad Luck", f.DiscardPile.Single(card => card.Type == CardType.Curse).Title, "then it goes to the discard pile");
        });

        c.Group("Debt: lose 10 gold at the end of the turn, or what is left", () =>
        {
            var one = Vs(Cards("Debt", (S, 4), (S, 5)));
            one.Gold = 25;
            one.EndTurn();
            c.Equal(15, one.Gold, "25 gold: 10 lost");
            c.Equal(80, one.Player.Hp, "no HP");

            var three = Vs(Cards(("Debt", 3), (S, 2), (S, 5)));
            three.Gold = 25;
            three.EndTurn();
            c.Equal(0, three.Gold, "three Debts take 10, 10 and the last 5");

            var broke = Vs(Cards("Debt", (S, 4), (S, 5)));
            c.Equal(0, broke.Gold, "a single fight has no gold");
            broke.EndTurn();
            c.Equal(0, broke.Gold, "nothing to lose");
        });

        c.Group("Debt in a run: the gold is the run's", () =>
        {
            var setup = new RunSetup { Deck = [new DeckCard("Debt"), .. RunSetup.IroncladDeck] };
            var run = new Arena.Run(setup, new FixedFights(new FightRequest { Monsters = () => [new PlacedMonster(new CheckDummy { Hp = 12 })], ShuffleDeck = false }));
            c.Sequence(["Debt", "Strike", "Strike", "Strike", "Strike"], run.Fight!.HandTitles(), "Debt is drawn first");
            Do(run, "play:1>e0", "end_turn");
            c.Equal(89, run.Gold, "99 gold, 10 lost at the end of the first turn");
            Do(run, "play:0>e0");
            c.That(run.IsOver && run.Result == RunResult.Cleared, "the second Strike wins the fight and the run");
            c.Equal(89, run.State.Gold, "the run keeps what the fight left");
        });
    }

    private static void DoubtAndShame(Check c)
    {
        c.Group("Doubt: 1 Weak at the end of the turn, which lasts through the next turn", () =>
        {
            var f = Vs(Cards("Doubt", (S, 14)));
            f.Play("Strike", 0);
            c.Equal(94, f.Enemy(0).Hp, "turn 1: a Strike deals 6");
            f.EndTurn();
            c.Equal(1, f.Player.PowerAmount<WeakPower>(), "turn 2: 1 Weak (new on the player, so the enemy turn did not tick it)");
            f.Play("Strike", 0);
            c.Equal(90, f.Enemy(0).Hp, "a Weak Strike deals 4");
            f.EndTurn();
            c.Equal(0, f.Player.PowerAmount<WeakPower>(), "turn 3: gone");
            f.Play("Strike", 0);
            c.Equal(84, f.Enemy(0).Hp, "6 again");
        });

        c.Group("Doubt on a player who is already Weak adds 1 and sets no skip", () =>
        {
            var f = Vs(Cards("Doubt", (S, 4), "Doubt", (S, 4), (S, 10)));
            f.EndTurn();
            c.Equal(1, f.Player.PowerAmount<WeakPower>(), "turn 2: 1 Weak from the first Doubt");
            c.Equal("Doubt", f.Hand[0].Title, "the second Doubt is in the hand");
            f.EndTurn();
            c.Equal(1, f.Player.PowerAmount<WeakPower>(), "turn 3: 1 plus 1, ticked once by the enemy turn");
            f.EndTurn();
            c.Equal(0, f.Player.PowerAmount<WeakPower>(), "turn 4: gone");
        });

        c.Group("Doubt goes through Artifact", () =>
        {
            var f = Vs(Cards("Doubt", (S, 14)));
            f.ApplyPower<ArtifactPower>(f.Player, 1, null);
            f.EndTurn();
            c.Equal(0, f.Player.PowerAmount<WeakPower>(), "Artifact blocked the Weak");
            c.That(!f.Player.HasPower<ArtifactPower>(), "and was used up");
        });

        c.Group("Shame: 1 Frail at the end of the turn, which lasts through the next turn", () =>
        {
            var f = Vs(Cards("Shame", (S, 4), D, (S, 4), D, (S, 4)));
            f.EndTurn();
            c.Equal(1, f.Player.PowerAmount<FrailPower>(), "turn 2: 1 Frail");
            f.Play("Defend");
            c.Equal(3, f.Player.Block, "a Frail Defend gives 3");
            f.EndTurn();
            c.Equal(0, f.Player.PowerAmount<FrailPower>(), "turn 3: gone");
            f.Play("Defend");
            c.Equal(5, f.Player.Block, "5 again");
        });
    }

    private static void Regret(Check c)
    {
        c.Group("Regret: 1 HP per card in the hand when the turn's end begins", () =>
        {
            var five = Vs(Cards("Regret", (S, 9)));
            five.EndTurn();
            c.Equal(75, five.Player.Hp, "five cards in hand: 5 HP");
            c.Equal("Regret", five.DiscardPile[0].Title, "then it goes to the discard pile");

            var three = Vs(Cards("Regret", (S, 9)));
            three.Play("Strike", 0).Play("Strike", 0);
            three.EndTurn();
            c.Equal(77, three.Player.Hp, "three cards in hand: 3 HP");

            var blocked = Vs(Cards("Regret", D, (S, 8)));
            blocked.Play("Defend");
            blocked.EndTurn();
            c.Equal(76, blocked.Player.Hp, "four cards in hand, 5 Block: 4 HP all the same");

            var away = Vs(Cards((S, 5), "Regret", (S, 4)));
            away.EndTurn();
            c.Equal(80, away.Player.Hp, "in the draw pile it does nothing");
            away.EndTurn();
            c.Equal(75, away.Player.Hp, "drawn on turn 2 it does");
        });

        c.Group("Regret counts the hand before anything leaves it", () =>
        {
            var withEthereal = Vs(Cards("Regret", "Clumsy", (S, 8)));
            withEthereal.EndTurn();
            c.Sequence(["Clumsy"], Titles(withEthereal.ExhaustPile), "Clumsy was exhausted first");
            c.Equal(75, withEthereal.Player.Hp, "and still counted: 5 HP");

            var several = Vs(Cards("Decay", "Regret", "Regret", (S, 7)));
            several.EndTurn();
            c.Equal(68, several.Player.Hp, "Decay 2, then each Regret 5 although the hand had shrunk: 80 - 2 - 5 - 5");
            c.Sequence(["Decay", "Regret", "Regret", "Strike", "Strike"], Titles(several.DiscardPile), "resolved left to right, then the rest of the hand");
        });
    }

    // ---------- Normality and Enthralled ----------

    private static void Normality(Check c)
    {
        const string rule = "Unplayable. / While this is in your hand, no card can be played once 3 cards have been played this turn";

        c.Group("Normality: no fourth card in a turn while it is in the hand", () =>
        {
            var f = Vs(Cards("Normality", (S, 4), "Normality", (S, 4), (S, 5)));
            f.SetEnergy(5);
            c.Equal($$$"""{"i":0,"name":"Normality","cost":"-","type":"Curse","text":"{{{rule}}} (3 cards left).","playable":false,"unplayable":"HasUnplayableKeyword"}""",
                HandEntry(f, 0), "the hand entry before anything is played");
            f.Play("Strike", 0);
            c.That(HandEntry(f, 0).Contains("(2 cards left)"), "one played: 2 left");
            f.Play("Strike", 0);
            c.That(HandEntry(f, 0).Contains("(1 card left)"), "two played: 1 left");
            f.Play("Strike", 0);
            c.Equal(82, f.Enemy(0).Hp, "three Strikes landed");
            c.Equal(2, f.Energy, "2 energy is left");
            c.Equal($$$"""{"i":0,"name":"Normality","cost":"-","type":"Curse","text":"{{{rule}}} (0 cards left).","playable":false,"unplayable":"HasUnplayableKeyword, BlockedByHook"}""",
                HandEntry(f, 0), "Normality itself after three plays");
            c.Equal("""{"i":1,"name":"Strike","cost":"1","type":"Attack","text":"Deal 6 damage.","playable":false,"unplayable":"BlockedByHook"}""",
                HandEntry(f, 1), "the fourth Strike is blocked, with energy to spare");
            c.Equal("end_turn", Ids(f), "only ending the turn is left");
            c.Equal("'play:1>e0' is not playable now.", f.Execute("play:1>e0").Error, "and the play is refused");

            f.EndTurn();
            c.Equal("Normality", f.Hand[0].Title, "turn 2: the second Normality is in the hand");
            c.That(HandEntry(f, 0).Contains("(3 cards left)"), "the count starts again every turn");
            c.Equal(4, f.LegalActions().Count(a => a.Id.StartsWith("play:", StringComparison.Ordinal)), "and the four Strikes can be played");
        });

        c.Group("Normality only binds from the hand", () =>
        {
            var f = Vs(Cards((S, 5), "Normality", (S, 4)));
            f.SetEnergy(5);
            f.Play("Strike", 0).Play("Strike", 0).Play("Strike", 0).Play("Strike", 0);
            c.Equal(76, f.Enemy(0).Hp, "four Strikes in one turn while Normality sits in the draw pile");
            var inDraw = f.DrawPile.Single(card => card.Def.ClassName == "Normality");
            c.Equal(rule + " (0 cards left).", Fight.CardText(f, inDraw, PileType.Draw), "its text counts in any pile of a fight: 3 less 3, never below 0");
            c.Equal(rule + ".", DeckText("Normality"), "and has no count outside a fight");
        });

        c.Group("Normality counts and stops cards an effect plays", () =>
        {
            // Strike, Strike, then Havoc as the third card: the top card of the draw pile (Bash) may not be played any more.
            var third = Vs(Cards("Normality", "Havoc", (S, 3), "Bash", (S, 4)));
            third.Play("Strike", 0).Play("Strike", 0).Play("Havoc");
            c.Equal(88, third.Enemy(0).Hp, "only the two Strikes hit");
            c.Equal(0, third.Enemy(0).PowerAmount<VulnerablePower>(), "Bash was not played");
            c.Sequence(["Bash"], Titles(third.ExhaustPile), "it was exhausted unplayed, as Havoc exhausts what it plays");

            // Strike, then Havoc as the second card: Bash is the third play and runs; after it nothing can be played.
            var second = Vs(Cards("Normality", "Havoc", (S, 3), "Bash", (S, 4)));
            second.Play("Strike", 0).Play("Havoc");
            c.Equal(86, second.Enemy(0).Hp, "the Strike and Bash hit: 6 and 8");
            c.Equal(2, second.Enemy(0).PowerAmount<VulnerablePower>(), "Bash applied its Vulnerable");
            c.Equal(1, second.Energy, "1 energy is left");
            c.Equal("end_turn", Ids(second), "but three cards were played: the Strikes in hand are blocked");

            // The count is of runs of card effects (the game counts its "card play started" entries, one per
            // run): a Strike that runs twice and one more Strike are three.
            var twice = Vs(Cards("Normality", (S, 4), (S, 5)));
            twice.Hand[1].ReplayCount = 1;
            twice.Play("Strike", 0);
            c.Equal(88, twice.Enemy(0).Hp, "a Strike that runs twice deals 12");
            twice.Play("Strike", 0);
            c.Equal(82, twice.Enemy(0).Hp, "a second Strike deals 6");
            c.Equal(1, twice.Energy, "two cards were paid for");
            c.Equal("end_turn", Ids(twice), "but three runs have started: nothing more this turn");
        });
    }

    private static void Enthralled(Check c)
    {
        c.Group("Enthralled: nothing else can be played by hand until it is", () =>
        {
            var f = Vs(Cards("Enthralled", (S, 4), (S, 5)));
            c.Equal("""{"i":0,"name":"Enthralled","cost":"2","type":"Curse","text":"While this is in your hand, you cannot play any other card until you have played this one. / Eternal.","playable":true}""",
                HandEntry(f, 0), "the hand entry");
            c.Equal("""{"i":1,"name":"Strike","cost":"1","type":"Attack","text":"Deal 6 damage.","playable":false,"unplayable":"BlockedByHook"}""",
                HandEntry(f, 1), "a Strike beside it");
            c.Equal("play:0 end_turn", Ids(f), "only Enthralled can be played");
            f.Do("play:0");
            c.Equal(1, f.Energy, "it costs 2");
            c.Sequence(["Enthralled"], Titles(f.DiscardPile), "and goes to the discard pile");
            c.Equal(100, f.Enemy(0).Hp, "it does nothing else");
            c.Equal("play:0>e0 play:1>e0 play:2>e0 play:3>e0 end_turn", Ids(f), "now the Strikes can be played");
        });

        c.Group("two Enthralled: either may be played, and the other still binds", () =>
        {
            var f = Vs(Cards(("Enthralled", 2), (S, 3), (S, 5)));
            c.Equal("play:0 play:1 end_turn", Ids(f), "both can be played");
            f.Do("play:1");
            c.That(HandEntry(f, 0).Contains("\"unplayable\":\"not enough energy\""), "the other costs 2 and 1 energy is left");
            c.That(HandEntry(f, 1).Contains("\"unplayable\":\"BlockedByHook\""), "and it still holds the Strikes back");
            c.Equal("end_turn", Ids(f), "nothing can be played");
        });

        c.Group("Enthralled lets cards through that an effect plays, and only binds from the hand", () =>
        {
            var f = Vs(Cards("Enthralled", (S, 4), (S, 5)));
            f.SetPotion(0, "DistilledChaos");
            f.Do("potion:0");
            c.Equal(82, f.Enemy(0).Hp, "Distilled Chaos played the three Strikes on top of the draw pile");
            c.Equal("play:0 end_turn", Ids(f), "the hand is still bound");

            var away = Vs(Cards((S, 5), "Enthralled", (S, 4)));
            c.Equal(5, away.LegalActions().Count(a => a.Id.StartsWith("play:", StringComparison.Ordinal)), "in the draw pile it binds nothing");
        });
    }

    // ---------- Guilty ----------

    private static RunSetup GuiltyFirst(params Func<Relic>[] relics) => new()
    {
        Deck = [new DeckCard("Guilty"), .. RunSetup.IroncladDeck],
        ExtraRelics = relics,
    };

    private static FixedFights SixFights() => new(Enumerable.Range(0, 6).Select(_ => OneStrikeFight()).ToArray());

    private static void GuiltyOverARun(Check c)
    {
        c.Group("Guilty counts the fights that end while it is in the deck and leaves after the fifth", () =>
        {
            var run = new Arena.Run(GuiltyFirst(), SixFights());
            for (var fight = 1; fight <= 5; fight++)
            {
                var left = 6 - fight;
                var words = left == 1 ? "1 more fight" : $"{left} more fights";
                c.Equal(11, run.Deck.Count, $"fight {fight}: eleven cards in the deck");
                c.Equal((decimal)left, DeckCardOf(run, "Guilty")!.Get("Combats"), $"fight {fight}: the deck card has {left} to go");
                c.Equal($$$"""{"name":"Guilty","cost":"-","type":"Curse","text":"Unplayable. / Leaves your deck for good after {{{words}}}.","rarity":"Curse"}""",
                    Text(run.DescribeDeck()["deck"]![0]), $"fight {fight}: get_deck shows it");
                c.Sequence(["Guilty", "Strike", "Strike", "Strike", "Strike"], run.Fight!.HandTitles(), $"fight {fight}: its copy is drawn first");
                c.Equal($$$"""{"i":0,"name":"Guilty","cost":"-","type":"Curse","text":"Unplayable. / Leaves your deck for good after {{{words}}}.","playable":false,"unplayable":"HasUnplayableKeyword"}""",
                    HandEntry(run.Fight!, 0), $"fight {fight}: the copy in the hand says the same");
                WinWithAStrike(run);
            }

            c.That(run.Fight is not null, "the sixth fight is on");
            c.Equal(10, run.Deck.Count, "after the fifth fight Guilty has left the deck");
            c.That(DeckCardOf(run, "Guilty") is null, "it is not in it any more");
            c.Equal(10, run.State.Deck.Count, "and the run was handed the deck without it");
            c.Sequence(["Strike", "Strike", "Strike", "Strike", "Strike"], run.Fight!.HandTitles(), "the sixth fight is drawn from ten cards");
            c.Equal(10, run.Fight!.Hand.Count + run.Fight.DrawPile.Count, "ten cards in the fight");
            c.Equal(80, run.Hp, "HP is untouched all along");
            WinWithAStrike(run);
            c.That(run.IsOver && run.Result == RunResult.Cleared, "the run is cleared");
            c.Equal(10, run.State.Deck.Count, "with the ten starting cards");

            var replay = Arena.Run.Replay(GuiltyFirst(), SixFights(), run.Actions);
            c.Equal(run.Dump(), replay.Dump(), "a replay of the actions ends in the same state");
        });

        c.Group("a copy of a run counts on by itself", () =>
        {
            var run = new Arena.Run(GuiltyFirst(), SixFights());
            WinWithAStrike(run);
            WinWithAStrike(run);
            var copy = run.Clone();
            WinWithAStrike(copy);
            WinWithAStrike(copy);
            WinWithAStrike(copy);
            c.That(DeckCardOf(copy, "Guilty") is null, "the copy played three more fights: Guilty is gone there");
            c.Equal(3m, DeckCardOf(run, "Guilty")!.Get("Combats"), "the run it was copied from still has 3 to go");
        });

        c.Group("only a fight that ends counts", () =>
        {
            var run = new Arena.Run(GuiltyFirst(), new Steps(
                r => { r.Damage(3); return null; },
                r => { r.Heal(3); return null; },
                _ => OneStrikeFight()));
            c.Equal(5m, DeckCardOf(run, "Guilty")!.Get("Combats"), "HP lost and healed outside a fight is no fight");
            Do(run, "end_turn");
            c.Equal(5m, DeckCardOf(run, "Guilty")!.Get("Combats"), "a turn that ends is no fight either");
            c.Equal(5m, run.Fight!.DiscardPile.Single(card => card.Def.ClassName == "Guilty").Get("Combats"), "nor for its copy in the discard pile");
        });
    }

    private static void GuiltyBesideOthers(Check c)
    {
        c.Group("Guilty is asked before the relics, and its leaving is heard while it is still in the deck", () =>
        {
            var run = new Arena.Run(GuiltyFirst(() => new Watcher()), SixFights());
            for (var fight = 1; fight <= 5; fight++) WinWithAStrike(run);
            c.Equal("end:11 end:11 end:11 end:11 removing:Guilty:11:fight end:10 ", run.State.FindRelic<Watcher>()!.Log,
                "four ends with eleven cards; at the fifth the removal is announced inside the fight with Guilty still in the deck, and the relic, asked after the deck card, sees ten");
        });

        c.Group("a Guilty added later counts from then on", () =>
        {
            Func<Arena.Run, FightRequest?> fight = _ => OneStrikeFight();
            var run = new Arena.Run(GuiltyFirst(), new Steps(
                fight, fight,
                r => { DeckEdits.AddCurses(r, [Content.Registry.Card("Guilty")]); return null; },
                fight, fight, fight, fight, fight, fight));
            WinWithAStrike(run);
            WinWithAStrike(run);
            c.Equal(12, run.Deck.Count, "after two fights a second Guilty was added at the end of the deck");
            c.Sequence([3m, 5m], run.Deck.Where(card => card.Def.ClassName == "Guilty").Select(card => card.Get("Combats")), "the first has 3 to go, the new one 5");
            WinWithAStrike(run);
            WinWithAStrike(run);
            WinWithAStrike(run);
            c.Equal(11, run.Deck.Count, "after the fifth fight the first one left");
            c.Equal("Guilty", run.Deck[^1].Def.ClassName, "the later one is still the last card");
            c.Equal(2m, run.Deck[^1].Get("Combats"), "with 2 to go");
            WinWithAStrike(run);
            c.Equal(11, run.Deck.Count, "after the sixth it is still there");
            WinWithAStrike(run);
            c.Equal(10, run.Deck.Count, "after the seventh fight, its fifth, it left too");
            WinWithAStrike(run);
            c.That(run.IsOver && run.Result == RunResult.Cleared, "the eighth fight ends the run");
        });
    }

    private static void RemoveFromDeckInAFight(Check c)
    {
        c.Group("a card is removed from the deck inside a fight as the game removes it", () =>
        {
            var setup = new RunSetup { Deck = [new DeckCard("Injury"), .. RunSetup.IroncladDeck], ExtraRelics = [() => new Watcher()] };
            var run = new Arena.Run(setup, new FixedFights(OneStrikeFight()));
            var f = run.Fight!;
            var injury = f.DeckCards[0];
            var copy = f.Hand[0];
            c.That(ReferenceEquals(f.DeckVersionOf(copy), injury), "the hand card was made from the deck card");
            f.RemoveFromDeck(injury);
            c.Equal(10, f.DeckCards.Count, "the deck card is gone from the fight's deck");
            c.Equal(PileType.None, injury.Pile, "and is in no pile");
            c.Equal("Injury", f.Hand[0].Title, "its copy stays in the hand");
            c.That(f.DeckVersionOf(copy) is null, "with no deck card behind it any more");
            c.Equal("removing:Injury:11:fight ", ((Watcher)f.Relics.Single(relic => relic is Watcher)).Log, "the relics heard it while the card was still in the deck");
            c.That(Throws<InvalidOperationException>(() => f.RemoveFromDeck(injury)), "a card that is not in the deck cannot be removed from it");
            c.That(Throws<InvalidOperationException>(() => f.RemoveFromDeck(copy)), "nor can a card of a pile");
            WinWithAStrike(run);
            c.Equal(10, run.State.Deck.Count, "the run takes the deck back without the card");
            c.That(run.State.Deck.All(card => card.Def.ClassName != "Injury"), "Injury is gone for good");
        });
    }

    // ---------- Neow's Fury ----------

    private static Fight FuryWithDiscard(string fury = "NeowsFury", int enemyHp = 100)
    {
        var f = new FightBuilder().DrawOrder(Cards(fury, (S, 4), (D, 4), "Bash")).Enemy(new CheckDummy { Hp = enemyHp }).Start();
        f.Put("Bash", PileType.Discard);
        f.Put(D, PileType.Discard);
        f.Put("Anger", PileType.Discard);
        return f;
    }

    private static void NeowsFury(Check c)
    {
        c.Group("Neow's Fury: 10 damage, Exhaust; nothing is asked with an empty discard pile", () =>
        {
            var f = Vs(Cards("NeowsFury", (S, 4), (D, 4), "Bash"));
            c.Equal("""{"i":0,"name":"Neow's Fury","cost":"1","type":"Attack","text":"Deal 10 damage. Then put up to 2 cards of your choice from your discard pile into your hand. / Exhaust.","playable":true}""",
                HandEntry(f, 0), "the hand entry");
            c.Equal("play:0>e0=Play Neow's Fury on Dummy", Labels(f).Split(" | ")[0], "it is aimed at an enemy");
            f.Do("play:0>e0");
            c.Equal(90, f.Enemy(0).Hp, "10 damage");
            c.Equal(2, f.Energy, "1 energy");
            c.That(!f.HasPendingChoice, "the discard pile is empty: no choice");
            c.Sequence(["Neow's Fury"], Titles(f.ExhaustPile), "exhausted");
            c.Equal(4, f.Hand.Count, "four cards left in hand");
        });

        c.Group("Neow's Fury: its damage is a card's attack", () =>
        {
            var f = Vs(Cards("NeowsFury", (S, 4), (D, 4), "Bash"));
            f.ApplyPower<StrengthPower>(f.Player, 2, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            var entry = f.Snapshot().State["hand"]![0]!;
            c.Equal("Deal 12 damage. Then put up to 2 cards of your choice from your discard pile into your hand. / Exhaust.", (string?)entry["text"], "2 Strength shows in the text");
            c.Equal("""{"e0":"Deal 18 damage. Then put up to 2 cards of your choice from your discard pile into your hand. / Exhaust."}""", Text(entry["vs"]), "and Vulnerable in the preview against the enemy");
            f.Do("play:0>e0");
            c.Equal(82, f.Enemy(0).Hp, "12 times 1.5");
        });

        c.Group("Neow's Fury+: 14 damage and up to 3 cards", () =>
        {
            var f = FuryWithDiscard("NeowsFury+");
            c.That(HandEntry(f, 0).Contains("\"name\":\"Neow's Fury+\"") && HandEntry(f, 0).Contains("Deal 14 damage. Then put up to 3 cards"), "the hand entry");
            f.Do("play:0>e0");
            c.Equal(86, f.Enemy(0).Hp, "14 damage");
            c.Equal(3, (int?)f.Snapshot().State["max"], "up to 3 cards");
            c.Equal("Choose up to 3 cards to put into your hand.", (string?)f.Snapshot().State["prompt"], "the prompt");
            f.Do("card:0", "card:1", "card:2", "confirm");
            c.Sequence(["Strike", "Strike", "Strike", "Strike", "Bash", "Defend", "Anger"], f.HandTitles(), "all three came to the hand");
            c.Equal(0, f.DiscardPile.Count, "the discard pile is empty");
        });

        c.Group("Neow's Fury that wins the fight asks nothing", () =>
        {
            var f = FuryWithDiscard(enemyHp: 10);
            f.Do("play:0>e0");
            c.That(f.IsOver && f.Won, "the fight is won");
            c.That(!f.HasPendingChoice, "no choice was opened");
        });
    }

    private static void NeowsFuryChoice(Check c)
    {
        c.Group("Neow's Fury: up to 2 cards of the discard pile, picked on the pile screen and confirmed", () =>
        {
            var f = FuryWithDiscard();
            f.Do("play:0>e0");
            c.Equal(90, f.Enemy(0).Hp, "the hit lands before the choice");
            var snapshot = f.Snapshot();
            c.Equal("card_select", snapshot.Screen, "the card selection screen");
            c.Equal("kind,prompt,min,max,pile,cards,run,combat", string.Join(",", snapshot.State.Select(kv => kv.Key)), "its keys");
            c.Equal("combat_pile_select", (string?)snapshot.State["kind"], "a pile selection");
            c.Equal("Choose up to 2 cards to put into your hand.", (string?)snapshot.State["prompt"], "the prompt");
            c.Equal(0, (int?)snapshot.State["min"], "none is fine");
            c.Equal(2, (int?)snapshot.State["max"], "two at most");
            c.Equal("Discard", (string?)snapshot.State["pile"], "from the discard pile");
            c.Sequence(["Bash", "Defend", "Anger"], snapshot.State["cards"]!.AsArray().Select(card => (string?)card!["name"] ?? ""), "its three cards; Neow's Fury itself is not among them");
            c.Equal("card:0=Select Bash | card:1=Select Defend | card:2=Select Anger | confirm=Confirm with nothing selected", Labels(f), "pick, or confirm with nothing");

            f.Do("card:0");
            c.Equal("card:0=Deselect Bash | card:1=Select Defend | card:2=Select Anger | confirm=Confirm selection: Bash", Labels(f), "one picked: the screen stays open");
            f.Do("card:1");
            c.Equal("card:0=Deselect Bash | card:1=Deselect Defend | confirm=Confirm selection: Bash, Defend", Labels(f), "two picked: no third, and still a confirm");
            c.Equal("The maximum is already selected; deselect a card first.", f.Execute("card:2").Error, "a third pick is refused");
            f.Do("confirm");
            c.That(!f.HasPendingChoice, "the choice is answered");
            c.Sequence(["Strike", "Strike", "Strike", "Strike", "Bash", "Defend"], f.HandTitles(), "the two cards came to the end of the hand, in the order picked");
            c.Sequence(["Anger"], Titles(f.DiscardPile), "the third stays in the discard pile");
            c.Sequence(["Neow's Fury"], Titles(f.ExhaustPile), "Neow's Fury is exhausted");
            c.Equal(90, f.Enemy(0).Hp, "the attack ran once");
            c.Equal(2, f.Energy, "and was paid for once");
        });

        c.Group("Neow's Fury: taking nothing, and a single card", () =>
        {
            var none = FuryWithDiscard();
            none.Do("play:0>e0", "confirm");
            c.Equal(4, none.Hand.Count, "nothing came to the hand");
            c.Equal(3, none.DiscardPile.Count, "the discard pile is as it was");

            var one = Vs(Cards("NeowsFury", (S, 4), (D, 4), "Bash"));
            one.Put("Bash", PileType.Discard);
            one.Do("play:0>e0");
            c.That(one.HasPendingChoice, "a single card in the discard pile is still asked about");
            c.Equal("card:0=Select Bash | confirm=Confirm with nothing selected", Labels(one), "take it or not");
            one.Do("card:0");
            c.That(one.HasPendingChoice, "picking it does not close the screen");
            one.Do("confirm");
            c.Sequence(["Strike", "Strike", "Strike", "Strike", "Bash"], one.HandTitles(), "confirmed: it is in the hand");
        });

        c.Group("Neow's Fury: no more cards than the hand has room for", () =>
        {
            var f = FuryWithDiscard();
            for (var i = 0; i < 5; i++) f.Put(S, PileType.Hand);
            c.Equal(10, f.Hand.Count, "a full hand of ten with Neow's Fury in it");
            f.Do("play:0>e0");
            c.Equal(1, (int?)f.Snapshot().State["max"], "nine cards are left in hand: room for one");
            c.Equal("Choose up to 1 card to put into your hand.", (string?)f.Snapshot().State["prompt"], "the prompt says one");
            f.Do("card:2");
            c.Equal("card:2=Deselect Anger | confirm=Confirm selection: Anger", Labels(f), "one picked is the most");
            f.Do("confirm");
            c.Equal(10, f.Hand.Count, "the hand is full again");
            c.Equal("Anger", f.Hand[^1].Title, "with the picked card last");

            // Played by an effect while the hand holds ten cards, there is no room at all: nothing is asked.
            var full = new FightBuilder().DrawOrder(Cards((S, 5), "NeowsFury", (S, 2), (D, 2))).Enemy(new CheckDummy()).Start();
            for (var i = 0; i < 5; i++) full.Put(S, PileType.Hand);
            full.Put("Bash", PileType.Discard);
            full.SetPotion(0, "DistilledChaos");
            full.Do("potion:0");
            c.That(!full.HasPendingChoice, "Distilled Chaos played Neow's Fury into a full hand: no choice");
            c.Equal(78, full.Enemy(0).Hp, "Neow's Fury and the two Strikes under it hit: 10, 6 and 6");
            c.Equal(10, full.Hand.Count, "the hand is as it was");
            c.Sequence(["Bash", "Strike", "Strike"], Titles(full.DiscardPile), "Bash stays in the discard pile, joined by the two Strikes");
            c.Sequence(["Neow's Fury"], Titles(full.ExhaustPile), "Neow's Fury is exhausted");
        });
    }

    // ---------- the order of the picks ----------

    private static void PickOrder(Check c)
    {
        c.Group("a pile choice hands its picks back in the order of the game's set", () =>
        {
            // Bash and Defend are picked, Bash is taken back, Anger is picked: in the game's HashSet Anger takes
            // the place Bash left, in front of Defend.
            var f = FuryWithDiscard();
            f.Do("play:0>e0", "card:0", "card:1", "card:0", "card:2");
            c.Equal("""["Anger","Defend"]""", Text(f.Snapshot().State["selected"]), "the snapshot lists the picks in the set's order");
            c.Equal("card:1=Deselect Defend | card:2=Deselect Anger | confirm=Confirm selection: Anger, Defend", Labels(f), "and so does the confirm label");
            f.Do("confirm");
            c.Sequence(["Strike", "Strike", "Strike", "Strike", "Anger", "Defend"], f.HandTitles(), "the cards come to the hand in that order");

            var plain = FuryWithDiscard();
            plain.Do("play:0>e0", "card:1", "card:0", "confirm");
            c.Sequence(["Strike", "Strike", "Strike", "Strike", "Defend", "Bash"], plain.HandTitles(), "with nothing taken back the order is the order picked");

            // Three picked, the first two taken back (Bash, then Defend), then Bash and Defend picked again: Bash takes
            // the place freed last (Defend's), Defend the one freed before (Bash's).
            var three = FuryWithDiscard("NeowsFury+");
            three.Do("play:0>e0", "card:0", "card:1", "card:2", "card:0", "card:1", "card:0", "card:1");
            c.Equal("""["Defend","Bash","Anger"]""", Text(three.Snapshot().State["selected"]), "the place freed last is taken first");
            three.Do("confirm");
            c.Sequence(["Strike", "Strike", "Strike", "Strike", "Defend", "Bash", "Anger"], three.HandTitles(), "and the hand gets them so");
        });

        c.Group("the picks of a grid screen are a .NET HashSet's, the picks of the hand a list", () =>
        {
            // Against the real thing: a set of numbers enumerates in the same order after any adds and removes.
            var rng = new GameRng(11);
            for (var round = 0; round < 200; round++)
            {
                var real = new HashSet<int>();
                var picks = new ChoicePicks(asSet: true);
                for (var step = 0; step < 30; step++)
                {
                    var option = rng.NextInt(0, 6);
                    if (real.Contains(option))
                    {
                        real.Remove(option);
                        if (!picks.Remove(option)) throw new InvalidOperationException("a pick that was made could not be taken back");
                    }
                    else
                    {
                        real.Add(option);
                        picks.Add(option);
                    }
                    if (!real.SequenceEqual(picks) || real.Count != picks.Count)
                        throw new InvalidOperationException($"round {round}, step {step}: the set has [{string.Join(",", real)}], the picks [{string.Join(",", picks)}]");
                }
                var copy = picks.Clone();
                copy.Add(6);
                real.Add(6);
                if (!real.SequenceEqual(copy)) throw new InvalidOperationException($"round {round}: a copy of the picks went another way");
                if (picks.Contains(6)) throw new InvalidOperationException($"round {round}: a pick on the copy showed in the original");
            }
            c.That(true, "200 rounds of 30 picks and take-backs enumerate as the set does");

            var hand = new ChoicePicks(asSet: false);
            hand.Add(0);
            hand.Add(1);
            hand.Remove(0);
            hand.Add(2);
            c.Sequence([1, 2], hand, "a hand selection keeps the order picked");
            c.That(!hand.Remove(5), "taking back what was not picked says so");
        });

        c.Group("a hand selection of a fight still keeps the order picked", () =>
        {
            var f = CheckKit.VsDummy();
            c.That(f.RunAsAction(x => x.MoveCards(x.ChooseFromHand("Pick two.", 2, 2), PileType.Discard)).Ok, "the choice opens");
            f.Do("select:3", "select:0", "confirm");
            c.Sequence(["Defend", "Strike"], Titles(f.DiscardPile), "the fourth card, then the first, as picked");
        });
    }

    // ---------- Eternal, and what a curse is transformed into ----------

    private static void EternalAndTransforms(Check c)
    {
        c.Group("the six Eternal curses can be neither removed nor transformed in the deck", () =>
        {
            string[] eternal = ["AscendersBane", "BadLuck", "CurseOfTheBell", "Enthralled", "Folly", "Greed"];
            c.Sequence(eternal, CurseTable.Where(row => row.Keywords.HasFlag(CardKeywords.Eternal)).Select(row => row.Class), "the six");
            var setup = new RunSetup { Deck = [.. RunSetup.IroncladDeck, .. CurseTable.Select(row => new DeckCard(row.Class)), new DeckCard("NeowsFury")] };
            var run = DeckCheckKit.Outside(setup);
            foreach (var card in run.State.Deck.Skip(10))
            {
                var isEternal = eternal.Contains(card.Def.ClassName);
                c.Equal(!isEternal, DeckEdits.IsRemovable(card), $"{card.Def.ClassName}: may be removed");
                c.Equal(!isEternal, DeckEdits.IsTransformable(run, card), $"{card.Def.ClassName}: may be transformed");
            }
            var greed = run.State.Deck.Single(card => card.Def.ClassName == "Greed");
            c.That(Throws<InvalidOperationException>(() => DeckEdits.TransformToRandom(run, greed, new GameRng(1))), "transforming Greed is refused");
            c.That(run.State.Deck.Contains(greed), "and it stays in the deck");
        });

        c.Group("a transformed curse becomes one of the other seventeen with one number, never drawn again", () =>
        {
            var others = CurseTable.Select(row => row.Class).Where(name => name != "Clumsy").ToList();
            c.Equal(17, others.Count, "seventeen others");
            var came = new HashSet<string>(StringComparer.Ordinal);
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var run = DeckCheckKit.Outside(new RunSetup { Deck = [.. RunSetup.IroncladDeck, new DeckCard("Clumsy")] });
                var rng = new GameRng(seed);
                var result = DeckEdits.TransformToRandom(run, run.State.Deck[^1], rng).Card;
                var expected = others[(int)(new GameRng(seed).NextDouble() * 17)];
                if (result.Def.ClassName != expected || rng.Counter != 1 || run.State.FindPart<LeftOutLog>() is not null || !ReferenceEquals(run.State.Deck[^1], result))
                    throw new InvalidOperationException($"seed {seed}: expected {expected} from one number, got {result.Def.ClassName} after {rng.Counter} with {(run.State.FindPart<LeftOutLog>() is null ? "no" : "a")} log");
                came.Add(expected);
            }
            c.That(true, "sixty streams: the curse at the index of the first number, one number each, nothing passed over");
            c.That(came.Count >= 15, $"and most of the seventeen came up ({came.Count})");
            c.That(came.Any(name => name is "AscendersBane" or "BadLuck" or "CurseOfTheBell" or "Enthralled" or "Folly"), "the five that only a transform gives among them");
        });

        c.Group("a transformed Neow's Fury becomes a colorless card", () =>
        {
            var run = DeckCheckKit.Outside(new RunSetup { Deck = [.. RunSetup.IroncladDeck, new DeckCard("NeowsFury")] });
            var options = CardPools.TransformOptions(run.State.Deck[^1], RunUnlocks.All);
            c.Equal(53, options.Count, "the 53 colorless cards of a single-player run");
            c.That(options.All(card => CardPools.Colorless.Contains(card.ClassName)), "all of the colorless pool");
        });
    }

    // ---------- all of them together, in the game's own fights ----------

    /// <summary>A fixed rule for playing on: the first card that can be played; in a choice the first card that can
    /// still be picked, else confirm; else end the turn.</summary>
    private static string NextAction(Arena.Run run)
    {
        var actions = run.LegalActions();
        var play = actions.FirstOrDefault(a => a.Id.StartsWith("play:", StringComparison.Ordinal));
        if (play is not null) return play.Id;
        var pick = actions.FirstOrDefault(a => (a.Id.StartsWith("card:", StringComparison.Ordinal) || a.Id.StartsWith("select:", StringComparison.Ordinal))
                                               && !a.Label.StartsWith("Deselect", StringComparison.Ordinal));
        if (pick is not null) return pick.Id;
        foreach (var id in new[] { "confirm", "skip", "end_turn" })
            if (actions.Any(a => a.Id == id)) return id;
        return actions[0].Id;
    }

    private static readonly string[] SixEncounters = ["NibbitsWeak", "ShrinkerBeetleWeak", "FuzzyWurmCrawlerWeak", "SlimesWeak", "NibbitsWeak", "ShrinkerBeetleWeak"];

    private static void EveryCardInRealFights(Check c)
    {
        c.Group("a run of six of the game's fights with every new card in the deck holds together", () =>
        {
            // Nothing here is a number of the game's: the run must go through without an action being turned
            // down or a rule throwing, replay to the same state, and keep what the rules above say.
            RunSetup Setup() => new()
            {
                Seed = "CURSES",
                Hp = 3000, MaxHp = 3000, Gold = 500,
                Deck = [.. RunSetup.IroncladDeck, .. CurseTable.Select(row => new DeckCard(row.Class)), new DeckCard("NeowsFury")],
            };
            FixedFights Fights() => new(SixEncounters.Select(name => new FightRequest { Encounter = name }).ToArray());

            var run = new Arena.Run(Setup(), Fights());
            Arena.Run? halfway = null;
            var fights = 0;
            var guiltyLeftAfter = 0;
            var sawChoice = false;
            for (var step = 0; step < 20000 && !run.IsOver; step++)
            {
                var floor = run.State.TotalFloor;
                if (run.Fight is { HasPendingChoice: true }) sawChoice = true;
                if (halfway is null && floor == 4) halfway = run.Clone();
                Do(run, NextAction(run));
                if (run.State.TotalFloor == floor && !run.IsOver) continue;
                fights++;
                if (guiltyLeftAfter == 0 && run.State.Deck.All(card => card.Def.ClassName != "Guilty")) guiltyLeftAfter = fights;
            }

            c.That(run.IsOver, "the run comes to an end");
            c.Equal(RunResult.Cleared, run.Result, "all six fights are won");
            c.Equal(6, fights, "six fights");
            c.Equal(5, guiltyLeftAfter, "Guilty left the deck after the fifth of them");
            c.Equal(28, run.State.Deck.Count, "the other 28 cards are still there");
            c.That(run.State.Gold is >= 0 and <= 500, "Debt took gold and never more than there was");
            c.That(run.State.Gold < 500, "it was in the hand at the end of some turn");
            c.That(sawChoice, "Neow's Fury opened its choice at least once");

            var replay = Arena.Run.Replay(Setup(), Fights(), run.Actions);
            c.Equal(run.Dump(), replay.Dump(), "a replay of the actions ends in the same state");

            c.That(halfway is not null, "a copy was taken in the fourth fight");
            for (var step = 0; step < 20000 && !halfway!.IsOver; step++) Do(halfway, NextAction(halfway));
            c.Equal(run.Dump(), halfway!.Dump(), "and played on by the same rule it ends in the same state");
        });
    }

    // ---------- the real game ----------

    private static void RealGame(Check c)
    {
        c.Group("the game's own records name these cards as the arena does (Mark's runs, v0.107.1)", () =>
        {
            // history/1791755111.run, seed 1BUSLN0FY5, floor 4, the event This or That: the option taken
            // carried the curse's title as its variable, and the card gained has this id; the save made on
            // the next floor (later-1011-164738) holds it as the deck's last card, added on floor 4.
            const string titleInTheRunRecord = "Clumsy";
            const string idInTheRunRecord = "CARD.CLUMSY";
            c.Equal(titleInTheRunRecord, Content.Registry.Card("Clumsy").Name, "the title the event showed");
            c.Equal(idInTheRunRecord, "CARD." + GameNames.Slug("Clumsy"), "the id of the card gained");

            // Ids of curses in the game's other run records of the folder (history/1783968626.run, 1783973586.run).
            string[] idsSeen = ["CARD.GREED", "CARD.DEBT", "CARD.WRITHE", "CARD.REGRET", "CARD.DOUBT", "CARD.CURSE_OF_THE_BELL"];
            string[] classes = ["Greed", "Debt", "Writhe", "Regret", "Doubt", "CurseOfTheBell"];
            c.Sequence(idsSeen, classes.Select(name => "CARD." + GameNames.Slug(name)), "six more curses under the ids the game writes");
            c.That(classes.All(name => Content.Registry.Has(ContentKind.Card, name)), "all of them built");

            // The deck of that run after the event, with the real card: the starter, Barricade, Colossus, Fight Me!, Clumsy.
            var run = DeckCheckKit.Outside(new RunSetup { Seed = "1BUSLN0FY5" });
            DeckEdits.AddNew(run, "Barricade");
            DeckEdits.AddNew(run, "Colossus");
            DeckEdits.AddNew(run, "FightMe");
            var added = DeckEdits.AddCurses(run, [Content.Registry.Card("Clumsy")]);
            c.That(added is [{ Success: true }], "the curse went in");
            c.Equal(14, run.State.Deck.Count, "fourteen cards, as the save has it");
            c.Equal("""{"name":"Clumsy","cost":"-","type":"Curse","text":"Unplayable. / Ethereal.","rarity":"Curse"}""", Text(run.DescribeDeck()["deck"]![13]), "the last of them Clumsy");
            c.Equal("""{"Barricade":1,"Bash":1,"Clumsy":1,"Colossus":1,"Defend":4,"Fight Me!":1,"Strike":5}""", Text(run.PlayerJson()["deck"]), "and the player block counts it");
        });
    }
}
