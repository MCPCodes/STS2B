using static SpireMind.Arena.DeckCheckKit;

namespace SpireMind.Arena;

/// <summary>
/// The deck commands of a run: add, remove, upgrade and transform, with the hooks in the
/// game's order. Every expected sequence is worked out by hand from the game's code
/// (CardPileCmd.Add and RemoveFromDeck, CardCmd.Upgrade and Transform, Hook's dispatch loops
/// and RunState.IterateHookListeners) and written down as a line of marks:
/// <c>A?Anger@10</c> is listener A asked ShouldAddToDeck about Anger with 10 cards in the deck;
/// <c>~</c> is TryModifyCardBeingAddedToDeck and <c>~~</c> its late pass; <c>+</c> is
/// AfterCardChangedPiles (with the old pile) and <c>++</c> its late pass; <c>-</c> is
/// BeforeCardRemoved; <c>!</c> is AfterAddToDeckPrevented. A random replacement is held against
/// a second stream of the same seed and a list built here from the fights' own list of the
/// Ironclad's cards. The real deck is that of Mark's run on seed 1BUSLN0FY5.
/// </summary>
public sealed class DeckEditsChecks : IArenaChecks
{
    public void Run(Check c)
    {
        Add(c);
        AddSeveral(c);
        Prevented(c);
        Replaced(c);
        ListenerOrder(c);
        StillThere(c);
        Remove(c);
        Upgrade(c);
        TransformAtRandom(c);
        TransformOrder(c);
        TransformRules(c);
        Curses(c);
        OnlyOutsideAFight(c);
        InsideAnAction(c);
        RealDeck(c);
    }

    // ---------- listeners of the checks ----------

    /// <summary>What a listener of the checks does besides writing down what it hears.</summary>
    private sealed record Habits
    {
        /// <summary>The class name it keeps out of the deck.</summary>
        public string? Refuses { get; init; }

        /// <summary>Hands back an upgraded copy of an Attack that is not upgraded, as the Molten Egg does.</summary>
        public bool UpgradesAttacks { get; init; }

        /// <summary>Run when it hears AfterCardChangedPiles; captures nothing.</summary>
        public Action<Run, CardInstance>? After { get; init; }
    }

    private static string Mark(string tag, string mark, RunContext c, CardInstance card, string more = "") => $"{tag}{mark}{card.Title}{more}@{c.Deck.Count}";

    /// <summary>A relic that writes down every deck hook it hears.</summary>
    private sealed class Ear(string tag, List<string> log, Habits? habits = null) : Relic, IDeckListener
    {
        private readonly Habits _habits = habits ?? new Habits();
        public override string Name => tag;
        public override string Describe() => "Writes down every deck hook it hears.";

        public bool ShouldAddToDeck(RunContext c, CardInstance card)
        {
            log.Add(Mark(tag, "?", c, card));
            return card.Def.ClassName != _habits.Refuses;
        }

        public void AfterAddToDeckPrevented(RunContext c, CardInstance card) => log.Add(Mark(tag, "!", c, card));

        public override bool TryModifyCardBeingAddedToDeck(RunContext c, CardInstance card, out CardInstance? newCard)
        {
            log.Add(Mark(tag, "~", c, card));
            newCard = null;
            if (!_habits.UpgradesAttacks || card.Type != CardType.Attack || !card.IsUpgradable || card.IsUpgraded) return false;
            newCard = c.NeedRun("an upgraded copy").State.NewCard(card.Def, upgraded: true);
            return true;
        }

        public override bool TryModifyCardBeingAddedToDeckLate(RunContext c, CardInstance card, out CardInstance? newCard)
        {
            log.Add(Mark(tag, "~~", c, card));
            newCard = null;
            return false;
        }

        public void AfterCardChangedPiles(RunContext c, CardInstance card, PileType oldPile)
        {
            log.Add(Mark(tag, "+", c, card, $":{oldPile}"));
            _habits.After?.Invoke(c.NeedRun("a check's habit"), card);
        }

        public void AfterCardChangedPilesLate(RunContext c, CardInstance card, PileType oldPile) => log.Add(Mark(tag, "++", c, card));
        public void BeforeCardRemoved(RunContext c, CardInstance card) => log.Add(Mark(tag, "-", c, card));
    }

    /// <summary>The hook object of a deck card that writes down what it hears, under its card's title. It is heard as
    /// the deck card because IDeckListener carries the marker IListensInDeck.</summary>
    private sealed class CardEar(List<string> log) : CardBehaviour, IDeckListener
    {
        private string Tag => Card.Def.ClassName;
        public bool ShouldAddToDeck(RunContext c, CardInstance card)
        {
            log.Add(Mark(Tag, "?", c, card));
            return true;
        }

        public override bool TryModifyCardBeingAddedToDeck(RunContext c, CardInstance card, out CardInstance? newCard)
        {
            log.Add(Mark(Tag, "~", c, card));
            newCard = null;
            return false;
        }

        public void AfterCardChangedPiles(RunContext c, CardInstance card, PileType oldPile) => log.Add(Mark(Tag, "+", c, card, $":{oldPile}"));
        public void AfterCardChangedPilesLate(RunContext c, CardInstance card, PileType oldPile) => log.Add(Mark(Tag, "++", c, card));
        public void BeforeCardRemoved(RunContext c, CardInstance card) => log.Add(Mark(Tag, "-", c, card));
    }

    /// <summary>The hooks of a potion in the belt.</summary>
    private sealed class PotionEar(List<string> log) : Listener, IDeckListener
    {
        public bool ShouldAddToDeck(RunContext c, CardInstance card)
        {
            log.Add(Mark("P", "?", c, card));
            return true;
        }

        public void AfterCardChangedPiles(RunContext c, CardInstance card, PileType oldPile) => log.Add(Mark("P", "+", c, card, $":{oldPile}"));
    }

    /// <summary>A card with a hook object of the checks: a 1-cost Skill that does nothing.</summary>
    private static CardDef Whisper(string className, List<string> log) =>
        new(className, className, CardType.Skill, CardRarity.Common, 1, TargetType.Self) { Text = _ => "Does nothing.", Behaviour = () => new CardEar(log) };

    private static Run With(List<string> log, params (string Tag, Habits? Habits)[] ears) =>
        Outside(new RunSetup { ExtraRelics = ears.Select(e => (Func<Relic>)(() => new Ear(e.Tag, log, e.Habits))).ToList() });

    private static string Line(List<string> log) => string.Join(" ", log);

    private static CardDef Def(string className) => Content.Registry.Card(className);

    // ---------- add ----------

    private static void Add(Check c)
    {
        c.Group("a card is added at the end of the deck, with the hooks in the game's order", () =>
        {
            var log = new List<string>();
            var run = With(log, ("A", null), ("B", null));
            var anger = run.State.NewCard(Def("Anger"));
            var result = DeckEdits.Add(run, anger);

            // ShouldAddToDeck; ModifyCardBeingAddedToDeck and its late pass, each over every listener; the card goes in;
            // AfterCardChangedPiles and its late pass. Burning Blood hears nothing of this.
            c.Equal("A?Anger@10 B?Anger@10 A~Anger@10 B~Anger@10 A~~Anger@10 B~~Anger@10 A+Anger:None@11 B+Anger:None@11 A++Anger@11 B++Anger@11",
                Line(log), "the hooks, with the deck's size as each is heard");
            c.That(result.Success && ReferenceEquals(result.Card, anger) && result.ModifiedBy.Count == 0, "the card went in as it was");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash Anger",
                DeckNames(run), "at the end of the deck");
            c.Equal(PileType.Deck, anger.Pile, "its pile is the deck");
            c.Equal(anger, run.State.CardById(anger.Id), "and the deck finds it by its id");

            var threw = false;
            try { DeckEdits.Add(run, anger); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "a card that is in the deck cannot be added again");
            c.Equal(11, run.State.Deck.Count, "and the deck is as it was");

            log.Clear();
            var made = DeckEdits.AddNew(run, Def("Inflame"), upgraded: true);
            c.Equal("Inflame+", made.Card.Title, "AddNew makes the card and adds it");
            c.Equal("Inflame+", run.State.Deck[^1].Title, "at the end");
            c.Equal(10, log.Count, "with the same ten hook calls");
            c.Equal("Anger", DeckEdits.AddNew(run, "Anger").Card.Title, "by class name too");
        });

        c.Group("nothing goes into the deck of a dead player", () =>
        {
            var log = new List<string>();
            var run = With(log, ("A", null));
            run.State.Hp = 0;
            var result = DeckEdits.AddNew(run, "Anger");
            c.That(!result.Success, "the add fails");
            c.Equal("", Line(log), "no hook is heard");
            c.Equal(10, run.State.Deck.Count, "the deck is as it was");
        });
    }

    private static void AddSeveral(Check c)
    {
        c.Group("several cards added at once: all are asked, then each goes in, then each is announced", () =>
        {
            // The shape of Hefty Tablet: the chosen card and an Injury in one call.
            var log = new List<string>();
            var run = With(log, ("A", null), ("B", null));
            var results = DeckEdits.Add(run, [run.State.NewCard(Def("Anger")), run.State.NewCard(Curse("Injury", "Injury"))]);
            c.Equal(
                "A?Anger@10 B?Anger@10 A?Injury@10 B?Injury@10 " +
                "A~Anger@10 B~Anger@10 A~~Anger@10 B~~Anger@10 " +
                "A~Injury@11 B~Injury@11 A~~Injury@11 B~~Injury@11 " +
                "A+Anger:None@12 B+Anger:None@12 A++Anger@12 B++Anger@12 " +
                "A+Injury:None@12 B+Injury:None@12 A++Injury@12 B++Injury@12",
                Line(log), "ShouldAddToDeck for both; then modify and append card by card; AfterCardChangedPiles only when both are in");
            c.Sequence(["Anger", "Injury"], results.Select(r => r.Card.Title), "one result per card, in order");
            c.Sequence(["Bash", "Anger", "Injury"], run.State.Deck.Skip(9).Select(card => card.Title), "both at the end, in the order given");
            c.Equal(0, DeckEdits.Add(run, []).Count, "adding nothing does nothing");
        });
    }

    private static void Prevented(Check c)
    {
        c.Group("a listener that says no keeps a card out; the rest of the cards still go in", () =>
        {
            var log = new List<string>();
            var run = With(log, ("A", null), ("B", new Habits { Refuses = "Injury" }), ("C", null));
            var results = DeckEdits.Add(run, [run.State.NewCard(Def("Anger")), run.State.NewCard(Curse("Injury", "Injury")), run.State.NewCard(Def("Inflame"))]);
            c.Equal(
                // The asking stops at the first no (C is not asked about Injury), and the one that said no is told.
                "A?Anger@10 B?Anger@10 C?Anger@10 A?Injury@10 B?Injury@10 B!Injury@10 A?Inflame@10 B?Inflame@10 C?Inflame@10 " +
                "A~Anger@10 B~Anger@10 C~Anger@10 A~~Anger@10 B~~Anger@10 C~~Anger@10 " +
                "A~Inflame@11 B~Inflame@11 C~Inflame@11 A~~Inflame@11 B~~Inflame@11 C~~Inflame@11 " +
                "A+Anger:None@12 B+Anger:None@12 C+Anger:None@12 A++Anger@12 B++Anger@12 C++Anger@12 " +
                "A+Inflame:None@12 B+Inflame:None@12 C+Inflame:None@12 A++Inflame@12 B++Inflame@12 C++Inflame@12",
                Line(log), "the hooks");
            c.Sequence([true, false, true], results.Select(r => r.Success), "the middle card failed");
            c.Sequence(["Bash", "Anger", "Inflame"], run.State.Deck.Skip(9).Select(card => card.Title), "the other two are in");

            log.Clear();
            var alone = DeckEdits.Add(run, run.State.NewCard(Curse("Injury", "Injury")));
            c.That(!alone.Success, "alone it fails too");
            c.Equal("A?Injury@12 B?Injury@12 B!Injury@12", Line(log), "and nothing after the no is heard");
        });
    }

    private static void Replaced(Check c)
    {
        c.Group("a listener can put another card into the deck in the card's place (the Eggs)", () =>
        {
            var log = new List<string>();
            var run = With(log, ("E", new Habits { UpgradesAttacks = true }), ("A", null));
            var anger = run.State.NewCard(Def("Anger"));
            var result = DeckEdits.Add(run, anger);
            c.Equal("E?Anger@10 A?Anger@10 E~Anger@10 A~Anger+@10 E~~Anger+@10 A~~Anger+@10 E+Anger+:None@11 A+Anger+:None@11 E++Anger+@11 A++Anger+@11",
                Line(log), "whoever is asked next is asked about the new card, and it is the new card that is announced");
            c.That(result.Success && !ReferenceEquals(result.Card, anger), "the result names the card that went in, another object");
            c.Equal("Anger+", result.Card.Title, "the upgraded copy");
            c.Sequence(["E"], result.ModifiedBy.Select(l => ((Relic)l).Name), "and who changed it");
            c.Equal(result.Card, run.State.Deck[^1], "the copy is in the deck");
            c.That(run.State.Deck.All(card => !ReferenceEquals(card, anger)), "the card that was handed over is not");

            var skill = DeckEdits.AddNew(run, "ShrugItOff");
            c.That(skill.ModifiedBy.Count == 0 && skill.Card.Title == "Shrug It Off", "a Skill is left alone");
            var already = DeckEdits.AddNew(run, Def("Anger"), upgraded: true);
            c.That(already.ModifiedBy.Count == 0, "and so is an Attack that is upgraded already");
        });
    }

    private static void ListenerOrder(Check c)
    {
        c.Group("who hears a deck hook outside a fight: the deck's cards, then the relics, then the potions", () =>
        {
            var log = new List<string>();
            var tonic = new PotionDef("CheckTonic", "Check Tonic", PotionRarity.Common, TargetType.Self) { Text = _ => "Listens.", Use = _ => { }, Hooks = new PotionEar(log) };
            var run = Outside(new RunSetup { ExtraCards = [(Whisper("W", log), false)], ExtraRelics = [() => new Ear("A", log)] });
            run.State.Belt.Add(tonic, slot: 1);
            c.Sequence(["CardEar", "BurningBlood", "Ear", "PotionEar"], run.Listeners().Select(l => l.GetType().Name), "the listeners: the card's hook object, both relics in pickup order, the potion's hooks");

            DeckEdits.AddNew(run, "Anger");
            c.Equal("W?Anger@11 A?Anger@11 P?Anger@11 W~Anger@11 A~Anger@11 A~~Anger@11 W+Anger:None@12 A+Anger:None@12 P+Anger:None@12 W++Anger@12 A++Anger@12",
                Line(log), "in that order at every hook (the card and the potion of this check do not answer every hook)");

            // A card with a hook object of its own: it is not in the deck while it is asked about, and it is
            // when it is announced, so it hears its own arrival, at its place in the deck.
            log.Clear();
            DeckEdits.Add(run, run.State.NewCard(Whisper("W2", log)));
            c.Equal("W?W2@12 A?W2@12 P?W2@12 W~W2@12 A~W2@12 A~~W2@12 W+W2:None@13 W2+W2:None@13 A+W2:None@13 P+W2:None@13 W++W2@13 W2++W2@13 A++W2@13",
                Line(log), "a new card hears AfterCardChangedPiles for itself, after the cards before it in the deck");

            // A potion that is gone is not asked any more.
            log.Clear();
            run.State.Belt.Remove(1);
            DeckEdits.AddNew(run, "Inflame");
            c.That(!Line(log).Contains('P'), "a potion that left the belt hears nothing");
        });
    }

    private static void StillThere(Check c)
    {
        c.Group("the listeners are listed first and each is asked only if it is still there", () =>
        {
            // A's AfterCardChangedPiles removes relic C: C is in the list already, and is passed over.
            var log = new List<string>();
            var run = With(log,
                ("A", new Habits { After = static (run, _) => { if (run.State.Relics.FirstOrDefault(r => r.Name == "C") is { } gone) run.State.RemoveRelic(gone); } }),
                ("B", null), ("C", null));
            DeckEdits.AddNew(run, "Anger");
            c.Equal("A?Anger@10 B?Anger@10 C?Anger@10 A~Anger@10 B~Anger@10 C~Anger@10 A~~Anger@10 B~~Anger@10 C~~Anger@10 A+Anger:None@11 B+Anger:None@11 A++Anger@11 B++Anger@11",
                Line(log), "C hears the hooks before A removed it and none after");
        });

        c.Group("a card that a hook adds is not asked in the pass that was under way, and is in the next", () =>
        {
            // A's AfterCardChangedPiles for Anger adds a card W that listens. The pass over [A, B] for Anger goes
            // on with B; the late pass is a new list, and W, a deck card, is first in it.
            var log = new List<string>();
            var run = With(log,
                ("A", new Habits { After = static (run, card) => { if (card.Def.ClassName == "Anger") DeckEdits.Add(run, run.State.NewCard(((Stash)run.State.Relics[^1]).Card)); } }),
                ("B", null));
            run.State.AddRelic(new Stash(Whisper("W", log)));
            DeckEdits.AddNew(run, "Anger");
            c.Equal(
                "A?Anger@10 B?Anger@10 A~Anger@10 B~Anger@10 A~~Anger@10 B~~Anger@10 " +
                "A+Anger:None@11 " +
                    // the add inside A's hook, from start to end
                    "A?W@11 B?W@11 A~W@11 B~W@11 A~~W@11 B~~W@11 W+W:None@12 A+W:None@12 B+W:None@12 W++W@12 A++W@12 B++W@12 " +
                "B+Anger:None@12 " +
                "W++Anger@12 A++Anger@12 B++Anger@12",
                Line(log), "the hooks");
            c.Sequence(["Bash", "Anger", "W"], run.State.Deck.Skip(9).Select(card => card.Title), "both cards are in, the outer one first");
        });
    }

    /// <summary>Holds a card definition for a check whose hook has to make a card without capturing anything.</summary>
    private sealed class Stash(CardDef card) : Relic
    {
        public CardDef Card => card;
        public override string Name => "Stash";
        public override string Describe() => "Holds a card for a check.";
    }

    // ---------- remove ----------

    private static void Remove(Check c)
    {
        c.Group("a card is removed: BeforeCardRemoved while it is still in the deck, then it is gone", () =>
        {
            var log = new List<string>();
            var run = Outside(new RunSetup { ExtraCards = [(Whisper("W", log), false)], ExtraRelics = [() => new Ear("A", log), () => new Ear("B", log)] });
            var whisper = run.State.Deck[^1];
            var strike = run.State.Deck[0];

            DeckEdits.Remove(run, [strike, whisper]);
            c.Equal("W-Strike@11 A-Strike@11 B-Strike@11 W-W@10 A-W@10 B-W@10", Line(log),
                "card by card: the Strike is out before the second card's hook is heard, and a card hears its own removal");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash", DeckNames(run), "both are gone");
            c.Equal(PileType.None, strike.Pile, "a removed card is in no pile");
            c.Equal(null, run.State.CardById(whisper.Id), "and the deck does not find it any more");

            log.Clear();
            DeckEdits.Remove(run, run.State.Deck[^1]);
            c.Equal("A-Bash@9 B-Bash@9", Line(log), "a card that left the deck does not listen any more");

            var threw = false;
            try { DeckEdits.Remove(run, strike); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "a card that is not in the deck cannot be removed");
            c.Equal(8, run.State.Deck.Count, "eight cards are left");
        });
    }

    // ---------- upgrade ----------

    private static void Upgrade(Check c)
    {
        c.Group("an upgrade changes the card and is heard by nobody", () =>
        {
            var log = new List<string>();
            var run = With(log, ("A", null));
            var bash = run.State.Deck[9];
            DeckEdits.Upgrade(run, bash);
            c.Equal("Bash+", bash.Title, "Bash is Bash+");
            // Bash: 8 damage and 2 Vulnerable, 10 and 3 upgraded.
            c.Equal("""{"name":"Bash+","cost":"2","type":"Attack","text":"Deal 10 damage. Apply 3 Vulnerable.","rarity":"Basic"}""", Text(RunDescribe.CardOffer(bash)), "with the upgraded numbers");
            c.Equal("", Line(log), "no hook");
            c.Equal(10, run.State.Deck.Count, "the deck keeps its ten cards");
            c.Equal(9, IndexOf(run, bash), "and the card its place");

            DeckEdits.Upgrade(run, bash);
            c.That(bash.UpgradeLevel == 1 && bash.Get("Damage") == 10m, "a card that cannot be upgraded further is passed over");

            var curse = DeckEdits.AddNew(run, Curse("Injury", "Injury")).Card;
            DeckEdits.Upgrade(run, [run.State.Deck[0], bash, curse, run.State.Deck[5]]);
            c.Equal("StrikeIronclad+ StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad+ DefendIronclad DefendIronclad DefendIronclad Bash+ Injury",
                DeckNames(run), "several at once: those that can be are upgraded");

            // A card on offer is upgraded the same way.
            var offer = run.State.NewCard(Def("Anger"));
            DeckEdits.Upgrade(run, offer);
            c.Equal("Anger+", offer.Title, "a card that is not in the deck can be upgraded too");
        });
    }

    private static int IndexOf(Run run, CardInstance card)
    {
        for (var i = 0; i < run.State.Deck.Count; i++)
            if (ReferenceEquals(run.State.Deck[i], card)) return i;
        return -1;
    }

    // ---------- transform ----------

    /// <summary>What an Ironclad card that is Common, Uncommon, Rare or Basic can become: the Ironclad's cards in the
    /// game's order less the Basic, Ancient and multiplayer-only ones, and less the card's own class.</summary>
    private static List<string> IroncladOptions(string original)
    {
        string[] never = ["Bash", "DefendIronclad", "StrikeIronclad", "Break", "Corruption", "DemonicShield", "Tank"];
        return Completeness.IroncladPool.Where(name => !never.Contains(name) && name != original).ToList();
    }

    private static void TransformAtRandom(Check c)
    {
        c.Group("a random transform: one pick, the original out, the new card at the end of the deck", () =>
        {
            c.Equal(80, IroncladOptions("StrikeIronclad").Count, "80 cards a Strike can become");
            foreach (ulong seed in new ulong[] { 11, 12, 13, 14, 15 })
            {
                var log = new List<string>();
                var run = With(log, ("A", null), ("B", null));
                var strike = run.State.Deck[0];
                var rng = new GameRng(seed);
                var result = DeckEdits.TransformToRandom(run, strike, rng);

                var expected = Def(IroncladOptions("StrikeIronclad")[(int)(new GameRng(seed).NextDouble() * 80)]);
                c.Equal(expected.ClassName, result.Card.Def.ClassName, $"seed {seed}: entry [fraction x 80] of the 80");
                c.Equal(1, rng.Counter, $"seed {seed}: one number");
                c.That(result.Success && !result.Card.IsUpgraded, $"seed {seed}: a fresh card, not upgraded");
                c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash " + expected.ClassName,
                    DeckNames(run), $"seed {seed}: the Strike is gone and the new card is last, not where the Strike was");
                c.Equal(PileType.None, strike.Pile, $"seed {seed}: the original is in no pile");
                var x = expected.Name;
                // No ShouldAddToDeck and no BeforeCardRemoved; the old pile of the announcement is the deck.
                c.Equal($"A~{x}@9 B~{x}@9 A~~{x}@9 B~~{x}@9 A+{x}:Deck@10 B+{x}:Deck@10 A++{x}@10 B++{x}@10", Line(log), $"seed {seed}: the hooks of a transform");
            }
        });

        c.Group("two cards transformed in one call (Leafy Poultice): both rolls first, then the cards go in", () =>
        {
            var log = new List<string>();
            var run = With(log, ("A", null));
            var rng = new GameRng(77);
            var strike = run.State.Deck.First(card => card.HasTag(CardTags.Strike));
            var defend = run.State.Deck.First(card => card.HasTag(CardTags.Defend));
            var results = DeckEdits.Transform(run, [new CardTransformation(strike), new CardTransformation(defend)], rng);

            // The Strike is rolled first, then the Defend; both are Basic, so both rolls run over the same 80.
            var twin = new GameRng(77);
            var x = Def(IroncladOptions("StrikeIronclad")[(int)(twin.NextDouble() * 80)]);
            var y = Def(IroncladOptions("DefendIronclad")[(int)(twin.NextDouble() * 80)]);
            c.Equal(2, rng.Counter, "two numbers");
            c.Sequence([x.ClassName, y.ClassName], results.Select(r => r.Card.Def.ClassName), "the Strike's replacement, then the Defend's");
            c.Equal($"StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad Bash {x.ClassName} {y.ClassName}",
                DeckNames(run), "both at the end, in the order of their originals' places");
            c.Equal($"A~{x.Name}@8 A~~{x.Name}@8 A+{x.Name}:Deck@9 A++{x.Name}@9 A~{y.Name}@9 A~~{y.Name}@9 A+{y.Name}:Deck@10 A++{y.Name}@10", Line(log),
                "both originals are out before the first replacement is heard of; then card by card");
        });

        c.Group("each roll of a transform can have a stream of its own (keyed streams)", () =>
        {
            var run = Outside();
            var streams = new[] { new GameRng(501), new GameRng(502) };
            var results = DeckEdits.Transform(run, [new CardTransformation(run.State.Deck[0]), new CardTransformation(run.State.Deck[5])], n => streams[n]);
            var x = IroncladOptions("StrikeIronclad")[(int)(new GameRng(501).NextDouble() * 80)];
            var y = IroncladOptions("DefendIronclad")[(int)(new GameRng(502).NextDouble() * 80)];
            c.Sequence([x, y], results.Select(r => r.Card.Def.ClassName), "the n-th roll is the first number of the n-th stream");
            c.That(streams[0].Counter == 1 && streams[1].Counter == 1, "one number from each");
        });

        c.Group("a card that is not Basic cannot become itself", () =>
        {
            var run = Outside(new RunSetup { Deck = [new DeckCard("Anger")] });
            c.Equal(79, IroncladOptions("Anger").Count, "79 cards Anger can become");
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var anger = run.State.Deck.First(card => card.Def.ClassName == "Anger");
                var result = DeckEdits.TransformToRandom(run, anger, new GameRng(seed));
                c.Equal(IroncladOptions("Anger")[(int)(new GameRng(seed).NextDouble() * 79)], result.Card.Def.ClassName, $"seed {seed}: entry [fraction x 79] of the 79");
                DeckEdits.TransformTo(run, result.Card, Def("Anger"));
            }
        });
    }

    private static void TransformOrder(Check c)
    {
        // Named replacements, so that only the order is in question. The starter deck: Strikes at 0 to 4,
        // Defends at 5 to 8, Bash at 9. An original leaves the deck when its turn comes, so the place noted for a
        // later one is its place among the cards that are left. Then the game sorts by that place with .NET's
        // List.Sort: two items by one compare-and-swap; three by three (first with second, first with third, second
        // with third); four to sixteen by insertion.
        (string Order, string DeckEnd) Order(params int[] deckIndexes)
        {
            string[] names = ["Anger", "Inflame", "ShrugItOff", "IronWave"];
            var run = Outside();
            var originals = deckIndexes.Select(i => run.State.Deck[i]).ToList();
            var list = originals.Select((original, n) => new CardTransformation(original, run.State.NewCard(Def(names[n])))).ToList();
            var results = DeckEdits.Transform(run, list);
            return (string.Join(" ", results.Select(r => r.Card.Def.ClassName)), string.Join(" ", run.State.Deck.Skip(10 - deckIndexes.Length).Select(card => card.Def.ClassName)));
        }

        c.Group("the order replacements go into the deck: by the place of their originals, as the game's sort leaves it", () =>
        {
            c.Equal(("Anger Inflame ShrugItOff", "Anger Inflame ShrugItOff"), Order(0, 5, 9), "places 0, 4, 7: in that order");
            c.Equal(("Inflame Anger", "Inflame Anger"), Order(6, 5), "the later card first: places 6 and 5, so the second replacement goes in first");
            c.Equal(("Anger Inflame", "Anger Inflame"), Order(5, 6), "two neighbours in deck order: both note place 5 and keep their order");
            c.Equal(("Anger Inflame ShrugItOff", "Anger Inflame ShrugItOff"), Order(2, 6, 7), "places 2, 5, 5: as given");
            // Places 5, 5, 2. First with second: equal, stay. First with third: 5 over 2, swap: the third is first now
            // and the FIRST is last. Second with third: equal, stay. So the two that noted place 5 have changed places.
            c.Equal(("ShrugItOff Inflame Anger", "ShrugItOff Inflame Anger"), Order(5, 6, 2), "places 5, 5, 2: the three-item sort turns the two equal ones around");
            // Four items are sorted by insertion, which keeps equal ones in order.
            c.Equal(("IronWave Anger Inflame ShrugItOff", "IronWave Anger Inflame ShrugItOff"), Order(5, 6, 7, 2), "places 5, 5, 5, 2 with four items: the last first, the rest as given");
        });
    }

    private static void TransformRules(Check c)
    {
        c.Group("a card is transformed into a named card without a roll", () =>
        {
            var log = new List<string>();
            var run = With(log, ("A", null));
            var result = DeckEdits.TransformTo(run, run.State.Deck[5], Def("Anger"));
            c.Equal("Anger", result.Card.Title, "the Defend is an Anger now");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad Bash Anger", DeckNames(run), "at the end of the deck");
            c.Equal("A~Anger@9 A~~Anger@9 A+Anger:Deck@10 A++Anger@10", Line(log), "with the hooks of a transform");

            var made = run.State.NewCard(Def("Inflame"), upgraded: true);
            var same = DeckEdits.Transform(run, run.State.Deck[0], made);
            c.That(ReferenceEquals(same.Card, made) && ReferenceEquals(run.State.Deck[^1], made), "a replacement that exists already goes in as it is");

            var replaced = Outside(new RunSetup { ExtraRelics = [() => new Ear("E", log, new Habits { UpgradesAttacks = true })] });
            c.Equal("Anger+", DeckEdits.TransformTo(replaced, replaced.State.Deck[0], Def("Anger")).Card.Title, "a listener can change the card a transform puts in, as with any card added");
        });

        c.Group("an Eternal card cannot be removed or transformed while it is in the deck", () =>
        {
            var greed = Curse("Greed", "Greed", CardKeywords.Eternal);
            var run = Outside(new RunSetup { ExtraCards = [(greed, false)] });
            var inDeck = run.State.Deck[^1];
            c.That(!DeckEdits.IsRemovable(inDeck), "not removable");
            c.That(!DeckEdits.IsTransformable(run, inDeck), "not transformable in the deck");
            c.That(DeckEdits.IsTransformable(run, run.State.NewCard(greed)), "outside the deck the game counts it as transformable");
            c.That(DeckEdits.IsRemovable(run.State.Deck[0]) && DeckEdits.IsTransformable(run, run.State.Deck[0]), "a Strike is both");

            var threw = "";
            try { DeckEdits.TransformToRandom(run, inDeck, new GameRng(1)); }
            catch (InvalidOperationException ex) { threw = ex.Message; }
            c.That(threw.Contains("Eternal", StringComparison.Ordinal), $"the transform refuses ({threw})");
            c.Equal(11, run.State.Deck.Count, "and the deck is as it was");
        });

        c.Group("what a transform refuses", () =>
        {
            var run = Outside();
            bool Throws<T>(Action act) where T : Exception
            {
                try { act(); }
                catch (T) { return true; }
                return false;
            }
            c.That(Throws<InvalidOperationException>(() => DeckEdits.TransformToRandom(run, run.State.NewCard(Def("Anger")), new GameRng(1))), "a card that is not in the deck");
            c.That(Throws<ArgumentException>(() => DeckEdits.Transform(run, [new CardTransformation(run.State.Deck[0])])), "a random transform without a stream");
            c.Equal(10, run.State.Deck.Count, "neither changed the deck");
            c.Equal(0, DeckEdits.Transform(run, []).Count, "no transformation at all is fine");

            var many = Outside(new RunSetup { Deck = Enumerable.Repeat(new DeckCard("StrikeIronclad"), 17).ToList() });
            c.That(Throws<NotSupportedException>(() => DeckEdits.Transform(many, many.State.Deck.Select(card => new CardTransformation(card)).ToList(), new GameRng(1))),
                "more than sixteen at once: the game's sort is not rebuilt beyond that");
            var sixteen = Outside(new RunSetup { Deck = Enumerable.Repeat(new DeckCard("StrikeIronclad"), 16).ToList() });
            c.Equal(16, DeckEdits.Transform(sixteen, sixteen.State.Deck.Select(card => new CardTransformation(card)).ToList(), new GameRng(1)).Count, "sixteen at once are fine");
            c.That(sixteen.State.Deck.All(card => card.Def.ClassName != "StrikeIronclad"), "every Strike became something else");
        });
    }

    private static void Curses(Check c)
    {
        c.Group("curses are added one by one, each with all its hooks", () =>
        {
            var log = new List<string>();
            var run = With(log, ("A", null));
            var results = DeckEdits.AddCurses(run, [Curse("Clumsy", "Clumsy"), Curse("Doubt", "Doubt")]);
            c.Equal("A?Clumsy@10 A~Clumsy@10 A~~Clumsy@10 A+Clumsy:None@11 A++Clumsy@11 A?Doubt@11 A~Doubt@11 A~~Doubt@11 A+Doubt:None@12 A++Doubt@12", Line(log),
                "the first curse is in, hooks and all, before the second is asked about");
            c.Sequence(["Clumsy", "Doubt"], results.Select(r => r.Card.Title), "both went in");
            var threw = false;
            try { DeckEdits.AddCurses(run, [Def("Anger")]); }
            catch (ArgumentException) { threw = true; }
            c.That(threw, "a card that is not a curse is refused");
        });

        c.Group("a curse is transformed into another curse, or the run cannot go on while none is built", () =>
        {
            // The seventeen other curses, in the game's order. Which of them the arena has built decides what
            // happens: a built one comes out after the unbuilt ones were passed over, or the run is unrepresentable.
            string[] others = ["AscendersBane", "BadLuck", "CurseOfTheBell", "Debt", "Decay", "Doubt", "Enthralled", "Folly", "Greed", "Guilty", "Injury", "Normality", "PoorSleep", "Regret", "Shame", "SporeMind", "Writhe"];
            var built = others.Where(name => Content.Registry.Has(ContentKind.Card, name)).ToList();
            var clumsy = Content.Registry.Has(ContentKind.Card, "Clumsy") ? Def("Clumsy") : Curse("Clumsy", "Clumsy");
            var run = Outside(new RunSetup { ExtraCards = [(clumsy, false)] });
            run.State.TotalFloor = 4;
            var rng = new GameRng(9);
            string? came = null;
            var reason = "";
            try { came = DeckEdits.TransformToRandom(run, run.State.Deck[^1], rng).Card.Def.ClassName; }
            catch (RunUnrepresentableException ex) { reason = ex.Message; }

            if (built.Count == 0)
            {
                c.Equal("a transform of Clumsy on floor 4 can only land on cards the arena leaves out (AscendersBane, BadLuck, CurseOfTheBell, Debt, ...)", reason,
                    "no other curse is built: the run ends as unrepresentable, with the reason");
                c.Equal(0, rng.Counter, "nothing was drawn");
                c.Equal("Clumsy", run.State.Deck[^1].Title, "and the curse is still in the deck");
            }
            else
            {
                var twin = new GameRng(9);
                var passed = new List<string>();
                string landed;
                while (!built.Contains(landed = others[(int)(twin.NextDouble() * 17)])) passed.Add(landed);
                c.Equal(landed, came, "a built curse came out: the first one the stream lands on");
                c.Equal(twin.Counter, rng.Counter, "with one number per try");
                c.Sequence(passed, run.State.FindPart<LeftOutLog>()?.Entries.Select(e => e.ClassName) ?? [], "and the unbuilt ones it passed over are written down");
            }
        });
    }

    // ---------- where the commands may be used ----------

    private static void OnlyOutsideAFight(Check c)
    {
        c.Group("the deck commands of a run are for outside a fight", () =>
        {
            var run = new Run(new RunSetup(), new FixedFights(new FightRequest { Monsters = () => [new PlacedMonster(new CheckDummy { Hp = 50 })], ShuffleDeck = false }));
            c.That(run.Fight is not null, "a fight is on");
            int Refused(params Action[] acts)
            {
                var n = 0;
                foreach (var act in acts)
                {
                    try { act(); }
                    catch (InvalidOperationException) { n++; }
                }
                return n;
            }
            var card = run.State.Deck[0];
            c.Equal(6, Refused(
                () => DeckEdits.AddNew(run, "Anger"),
                () => DeckEdits.Remove(run, card),
                () => DeckEdits.Upgrade(run, card),
                () => DeckEdits.TransformToRandom(run, card, new GameRng(1)),
                () => DeckEdits.TransformTo(run, card, Def("Anger")),
                () => DeckEdits.AddCurses(run, [Curse("Clumsy", "Clumsy")])), "all six refuse");
            c.Equal(10, run.State.Deck.Count, "the deck the fight began with is untouched");
            c.That(!run.State.Deck[0].IsUpgraded, "and so is its first card");
        });
    }

    /// <summary>Begins the run's world (so that the run has streams) and puts up a desk with a script.</summary>
    private sealed class WithWorld(IReadOnlyDictionary<string, Action<Run, Desk>> script) : RunDirector
    {
        public override void Begin(Run run)
        {
            RunWorld.Begin(run.State, acts: ["Overgrowth", "Hive", "Glory"]);
            run.Push(new Desk(script));
        }

        public override void AfterFightWon(Run run, FightOutcome fight) => run.End(RunResult.Cleared);
    }

    private static readonly Dictionary<string, Action<Run, Desk>> Script = new()
    {
        ["gain"] = static (run, _) => DeckEdits.AddNew(run, "Anger"),
        ["smith"] = static (run, _) => DeckEdits.Upgrade(run, run.State.Deck.First(card => card.IsUpgradable)),
        ["purge"] = static (run, _) => DeckEdits.Remove(run, run.State.Deck[0]),
        // As Leafy Poultice rolls: on the player's transformations stream, the first Strike and then the first Defend.
        ["poultice"] = static (run, desk) =>
        {
            var streams = run.State.Part<RunWorld>().Streams;
            var list = new List<CardTransformation>
            {
                new(run.State.Deck.First(card => card.HasTag(CardTags.Strike))), new(run.State.Deck.First(card => card.HasTag(CardTags.Defend))),
            };
            foreach (var result in DeckEdits.Transform(run, list, n => streams.Draw(RunStream.Transformations, Purpose.Transform(n))))
                desk.Notes.Add(result.Card.Def.ClassName);
        },
    };

    private static void InsideAnAction(Check c)
    {
        c.Group("deck commands inside the actions of a run: the run's streams, its copies and its save", () =>
        {
            var setup = new RunSetup { Seed = "1BUSLN0FY5" };
            var run = new Run(setup, new WithWorld(Script));
            var streams = run.State.Part<RunWorld>().Streams;
            c.Equal(0, streams.Counter(RunStream.Transformations), "nothing drawn from transformations yet");

            Do(run, "poultice");
            // The player's transformations stream of this seed, walked here: two picks among the 80.
            var twin = new GameRng(RunStreams.LongSeed(new RunSeed("1BUSLN0FY5"), RunStream.Transformations));
            var x = IroncladOptions("StrikeIronclad")[(int)(twin.NextDouble() * 80)];
            var y = IroncladOptions("DefendIronclad")[(int)(twin.NextDouble() * 80)];
            c.Sequence([x, y], Notes(run), "the two replacements are the stream's first two picks");
            c.Equal(2, run.State.Part<RunWorld>().Streams.Counter(RunStream.Transformations), "two numbers of the transformations stream are used");
            c.Equal($"StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad Bash {x} {y}", DeckNames(run), "the deck after the poultice");

            var copy = run.Clone();
            var before = copy.Dump();
            Do(run, "gain", "smith", "purge");
            c.Equal($"StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad Bash {x} {y} Anger", DeckNames(run), "an Anger more, the first Strike upgraded and then removed");
            c.Equal(before, copy.Dump(), "a copy taken before is untouched");
            Do(copy, "gain", "smith", "purge");
            c.Equal(run.Dump(), copy.Dump(), "and ends in the same state from the same actions");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, new WithWorld(Script), run.Actions).Dump(), "the save replays");
            c.Sequence(["poultice", "gain", "smith", "purge"], run.Actions, "four actions in the save");

            // In keyed mode each of the two rolls has its own stream of the place and the purpose: a second set of
            // streams of the same seed, asked for the same two purposes at the same place, gives the same two.
            var keyed = new Run(new RunSetup { Seed = "1BUSLN0FY5" }, new KeyedWorld(Script));
            Do(keyed, "poultice");
            var fresh = new RunStreams("1BUSLN0FY5", StreamMode.Keyed);
            var kx = IroncladOptions("StrikeIronclad")[(int)(fresh.Draw(RunStream.Transformations, Purpose.Transform(0)).NextDouble() * 80)];
            var ky = IroncladOptions("DefendIronclad")[(int)(fresh.Draw(RunStream.Transformations, Purpose.Transform(1)).NextDouble() * 80)];
            c.Sequence([kx, ky], Notes(keyed), "keyed streams: the first roll on the stream of purpose transform/0, the second on that of transform/1");
            c.Equal(0, keyed.State.Part<RunWorld>().Streams.Counter(RunStream.Transformations), "without touching the long stream");
        });
    }

    private sealed class KeyedWorld(IReadOnlyDictionary<string, Action<Run, Desk>> script) : RunDirector
    {
        public override void Begin(Run run)
        {
            RunWorld.Begin(run.State, mode: StreamMode.Keyed, acts: ["Overgrowth", "Hive", "Glory"]);
            run.Push(new Desk(script));
        }

        public override void AfterFightWon(Run run, FightOutcome fight) => run.End(RunResult.Cleared);
    }

    // ---------- the real game ----------

    private static void RealDeck(Check c)
    {
        c.Group("the deck of a real run is rebuilt card by card (seed 1BUSLN0FY5, Mark's saves of 2026-10-11)", () =>
        {
            // The game's own saves of that run show the deck after Neow (Arcane Scroll gave Barricade), after each of
            // the two fights (Colossus and Fight Me! were picked), after the event This or That (the curse Clumsy)
            // and, in the run's history, after the shop (Tear Asunder was bought): every card at the end, in the
            // order it was obtained.
            var run = Outside(new RunSetup { Seed = "1BUSLN0FY5" });
            const string starter = "StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash";
            c.Equal(starter, DeckNames(run), "the save at Neow's screen: the ten starting cards");
            DeckEdits.AddNew(run, "Barricade");
            c.Equal(starter + " Barricade", DeckNames(run), "the save after Neow's pick");
            DeckEdits.AddNew(run, "Colossus");
            c.Equal(starter + " Barricade Colossus", DeckNames(run), "the save after floor 2");
            DeckEdits.AddNew(run, "FightMe");
            c.Equal(starter + " Barricade Colossus FightMe", DeckNames(run), "the save after floor 3");
            DeckEdits.AddCurses(run, [Content.Registry.Has(ContentKind.Card, "Clumsy") ? Def("Clumsy") : Curse("Clumsy", "Clumsy")]);
            c.Equal(starter + " Barricade Colossus FightMe Clumsy", DeckNames(run), "the save after floor 4");
            DeckEdits.AddNew(run, "TearAsunder");
            c.Equal(starter + " Barricade Colossus FightMe Clumsy TearAsunder", DeckNames(run), "the run's history after the shop on floor 5");
            c.Equal(15, run.State.Deck.Count, "fifteen cards");
        });
    }
}
