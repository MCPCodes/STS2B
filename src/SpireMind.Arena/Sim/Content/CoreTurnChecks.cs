namespace SpireMind.Arena;

/// <summary>
/// The turn-flow rules of rules-turns.md, one group per rule or small family of rules, played
/// through the engine with the starter cards, a scripted dummy, and a few cards, powers and
/// monsters that exist only here.
/// </summary>
public sealed class CoreTurnChecks : IArenaChecks
{
    // ---------- cards that exist only for these checks ----------

    private static CardDef Skill(string name, int cost, Action<PlayContext>? play = null, CardKeywords keywords = CardKeywords.None) =>
        new("Check" + name.Replace(" ", ""), name, CardType.Skill, CardRarity.Common, cost, TargetType.Self) { Keywords = keywords, Play = play };

    private static readonly CardDef DrawTwo = Skill("Draw Two", 0, c => c.Draw(2));
    private static readonly CardDef Blank = Skill("Blank", 0);
    private static readonly CardDef Costly = Skill("Costly", 3);
    private static readonly CardDef Burner = Skill("Burner", 0, keywords: CardKeywords.Exhaust);
    private static readonly CardDef Born = Skill("Born", 0, keywords: CardKeywords.Innate);
    private static readonly CardDef Keeper = Skill("Keeper", 1, keywords: CardKeywords.Retain);
    private static readonly CardDef Ghost = Skill("Ghost", 1, keywords: CardKeywords.Ethereal | CardKeywords.Retain);

    private static readonly CardDef Form = new("CheckForm", "Form", CardType.Power, CardRarity.Rare, 1, TargetType.Self)
    {
        Play = c => c.ApplyToSelf<StrengthPower>(2),
    };

    /// <summary>X cost: 5 damage to all enemies, X times.</summary>
    private static readonly CardDef Whirl = new("CheckWhirl", "Whirl", CardType.Attack, CardRarity.Uncommon, CardDef.CostX, TargetType.AllEnemies)
    {
        Vars = { ["Damage"] = 5 },
        Play = c => c.Attack(c.V("Damage")).Hits(c.X).Execute(),
    };

    /// <summary>Kills a 50 HP enemy, then costs the player 999 HP.</summary>
    private static readonly CardDef Pyrrhic = new("CheckPyrrhic", "Pyrrhic", CardType.Attack, CardRarity.Rare, 0, TargetType.AnyEnemy)
    {
        Play = c =>
        {
            c.Attack(50).Execute();
            c.LoseHp(999);
        },
    };

    /// <summary>Plays the top card of the draw pile for free.</summary>
    private static readonly CardDef TopDeck = Skill("Top Deck", 0, c => c.F.AutoPlayFromDrawPile(1));

    // ---------- powers that exist only for these checks ----------

    /// <summary>Records the order in which the turn's hooks reach it.</summary>
    private sealed class Recorder : Power
    {
        public List<string> Seen = new();

        public override string Name => "Recorder";
        public override PowerType Type => PowerType.Buff;
        public override bool IsVisible => false;
        public override string Describe() => "";
        public override void OnCloned(CloneMap map) => Seen = new List<string>(Seen);

        private void Note(Fight f, string what) => Seen.Add($"{what}@{f.Turn}:hand{f.Hand.Count}:energy{f.Energy}:block{f.Player.Block}");

        public override void BeforeSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants) => Note(f, $"BeforeSideTurnStart({side})");
        public override void AfterBlockCleared(Fight f, Creature creature) => Note(f, $"AfterBlockCleared({creature.Name})");
        public override void AfterEnergyReset(Fight f) => Note(f, "AfterEnergyReset");
        public override void BeforeHandDraw(Fight f) => Note(f, "BeforeHandDraw");
        public override void AfterPlayerTurnStart(Fight f) => Note(f, "AfterPlayerTurnStart");
        public override void AfterSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants) => Note(f, $"AfterSideTurnStart({side})");
        public override void AfterAutoPrePlayPhaseEntered(Fight f) => Note(f, "AfterAutoPrePlayPhaseEntered");
        public override void AfterAutoPostPlayPhaseEntered(Fight f) => Note(f, "AfterAutoPostPlayPhaseEntered");
        public override void BeforeSideTurnEndEarly(Fight f, Side side, IReadOnlyList<Creature> participants) => Note(f, $"BeforeSideTurnEndEarly({side})");
        public override void BeforeSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants) => Note(f, $"BeforeSideTurnEnd({side})");
        public override void BeforeFlush(Fight f) => Note(f, "BeforeFlush");
        public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants) => Note(f, $"AfterSideTurnEnd({side})");
        public override void BeforeCardPlayed(Fight f, CardPlay play) => Seen.Add($"BeforeCardPlayed({play.Card.Title},{play.PlayIndex + 1}/{play.PlayCount},pile {play.Card.Pile})");
        public override void AfterCardPlayed(Fight f, CardPlay play) => Seen.Add($"AfterCardPlayed({play.Card.Title},{play.PlayIndex + 1}/{play.PlayCount})");
        public override void AfterCardDrawn(Fight f, CardInstance card, bool fromHandDraw) => Seen.Add($"AfterCardDrawn({card.Title},hand{f.Hand.Count})");
        public override void AfterCardExhausted(Fight f, CardInstance card, bool causedByEthereal) => Seen.Add($"AfterCardExhausted({card.Title},{causedByEthereal})");
        public override void AfterCardDiscarded(Fight f, CardInstance card) => Seen.Add($"AfterCardDiscarded({card.Title})");
        public override void AfterShuffle(Fight f) => Seen.Add("AfterShuffle");
        public override void AfterHandEmptied(Fight f) => Seen.Add("AfterHandEmptied");
    }

    /// <summary>The next card played runs twice (the shape of the game's one-shot doubling powers).</summary>
    private sealed class Echo : Power
    {
        public override string Name => "Echo";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "The next card is played twice.";
        public override int ModifyCardPlayCount(Fight f, CardInstance card, Creature? target, int playCount) => playCount + 1;
        public override void AfterModifyingCardPlayCount(Fight f, CardInstance card) => f.Decrement(this);
    }

    /// <summary>Answers a powered attack before it lands, like the game's Thorns.</summary>
    private sealed class Spikes : Power
    {
        public override string Name => "Spikes";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";

        public override void BeforeDamageReceived(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource)
        {
            if (ReferenceEquals(target, Owner) && dealer is not null && props.IsPoweredAttack())
                f.Damage(dealer, Amount, Props.NonCardUnpowered, Owner);
        }
    }

    /// <summary>Keeps the fight alive while its owner's list of enemies is empty (the shape of Infested).</summary>
    private sealed class Hatch : Power
    {
        public override string Name => "Hatch";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";
        public override bool ShouldStopCombatFromEnding(Fight f) => true;

        public override void AfterDeath(Fight f, Creature creature, bool wasRemovalPrevented)
        {
            if (!ReferenceEquals(creature, Owner)) return;
            f.AddMonster(new CheckDummy { Hp = 5, DisplayName = "Hatchling", Steps = [new DummyStep("NIP", Damage: 1)] });
        }
    }

    private sealed class StartBlock : Relic
    {
        public override string Name => "Start Block";
        public override string Describe() => "";
        public override void BeforeCombatStart(Fight f) => f.GainBlock(f.Player, 10, Props.NonCardUnpowered);
    }

    // ---------- monsters that exist only for these checks ----------

    /// <summary>After its first move it picks by what it sees when the move is chosen.</summary>
    private sealed class Watcher : Monster
    {
        public override string Name => "Watcher";
        public override int MinHp => 60;
        public override int MaxHp => 60;

        protected override MonsterState BuildMachine(MachineBuilder m)
        {
            var open = m.Move("OPEN", _ => { }, Intent.Sleep);
            var sawWeak = m.Move("SAW_WEAK", _ => { }, Intent.Buff);
            var sawNone = m.Move("SAW_NONE", _ => { }, Intent.Debuff);
            var look = m.Conditional("LOOK").When(f => f.Player.HasPower<WeakPower>(), sawWeak).Otherwise(sawNone);
            open.FollowUp = look;
            sawWeak.FollowUp = look;
            sawNone.FollowUp = look;
            return open;
        }
    }

    /// <summary>A random branch with every repeat rule, to check the weights against the move log.</summary>
    private sealed class Roller : Monster
    {
        public string Mode = "cannot";

        public override string Name => "Roller";
        public override int MinHp => 500;
        public override int MaxHp => 500;

        protected override MonsterState BuildMachine(MachineBuilder m)
        {
            var a = m.Move("A", _ => { }, Intent.Buff);
            var b = m.Move("B", _ => { }, Intent.Debuff);
            var c = m.Move("C", _ => { }, Intent.Defend);
            var rand = m.Random("RAND");
            switch (Mode)
            {
                case "cannot":
                    rand.Add(a, repeat: MoveRepeat.CannotRepeat).Add(b, repeat: MoveRepeat.CannotRepeat);
                    break;
                case "twice":
                    rand.Add(a, repeat: MoveRepeat.MaxInARow, maxInARow: 2).Add(b, repeat: MoveRepeat.CannotRepeat);
                    break;
                case "once":
                    rand.Add(a, repeat: MoveRepeat.CannotRepeat).Add(b, repeat: MoveRepeat.UseOnlyOnce).Add(c, repeat: MoveRepeat.CannotRepeat);
                    break;
                case "cooldown":
                    rand.Add(a, cooldown: 3).Add(b, cooldown: 2).Add(c, repeat: MoveRepeat.CannotRepeat);
                    break;
                case "weights":
                    rand.Add(a, 0.4f).Add(b, 0.6f);
                    break;
            }
            a.FollowUp = rand;
            b.FollowUp = rand;
            c.FollowUp = rand;
            return Mode == "weights" ? rand : a;
        }
    }

    public void Run(Check c)
    {
        Start(c);
        PlayerTurnStart(c);
        Drawing(c);
        PlayingCards(c);
        EndOfTurn(c);
        EnemyTurn(c);
        MoveSelection(c);
        EndOfCombat(c);
    }

    // ---------- section 1: start of combat ----------

    private static void Start(Check c)
    {
        c.Group("R5, R6, R8: line-up, HP rolls and first intents", () =>
        {
            for (var seed = 1; seed <= 40; seed++)
            {
                var f = new FightBuilder().Encounter("NibbitsNormal").Seed(seed).Start();
                var front = f.Enemy(0);
                var back = f.Enemy(1);
                c.That(front.MaxHp is >= 42 and <= 46 && back.MaxHp is >= 42 and <= 46, $"seed {seed}: Nibbit HP out of 42..46");
                c.That(front.MaxHp != back.MaxHp, $"seed {seed}: two monsters share a max HP");
                c.Equal(2, f.Rng.Niche.Counter, $"seed {seed}: one Niche draw per monster");
                c.Sequence(["attack 6", "defend"], f.IntentTexts(front), $"seed {seed}: the front Nibbit opens with Slice");
                c.Sequence(["buff"], f.IntentTexts(back), $"seed {seed}: the back Nibbit opens with Hiss");
                c.Equal(0, f.Rng.MonsterAi.Counter, $"seed {seed}: fixed machines draw no MonsterAi number");
                c.Equal(0, f.Player.Id, "the player is creature 0");
            }

            // With every value of the range taken, the roll falls back to the whole range (still one draw).
            var crowded = new FightBuilder().Enemy(new CheckDummy { Hp = 42 }).Enemy(new CheckDummy { Hp = 43 }).Enemy(new CheckDummy { Hp = 44 })
                .Enemy(new CheckDummy { Hp = 45 }).Enemy(new CheckDummy { Hp = 46 }).Enemy(new Nibbit()).Start();
            c.That(crowded.Enemy(5).MaxHp is >= 42 and <= 46, "the fallback roll stays in range");
            c.Equal(6, crowded.Rng.Niche.Counter, "one draw per monster, also when min equals max and for the fallback roll");

            var weak = new FightBuilder().Encounter("NibbitsWeak").Seed(3).Start();
            c.Sequence(["attack 12"], weak.IntentTexts(weak.Enemy(0)), "the lone Nibbit opens with Butt");
            c.Equal("Monster", weak.Snapshot().State["run"]!["room"]!.GetValue<string>(), "the run block names the room");
        });

        c.Group("R7: the draw pile is the deck, shuffled once on the Shuffle stream; index 0 is the top", () =>
        {
            var f = new FightBuilder().Seed(11).Start();
            c.Equal(9, f.Rng.Shuffle.Counter, "a 10 card deck takes 9 shuffle draws");
            c.Equal(5, f.Hand.Count, "five cards drawn");
            c.Equal(5, f.DrawPile.Count, "five left");
            var g = new FightBuilder().Seed(11).Start();
            c.Sequence(f.HandTitles(), g.HandTitles(), "the same seed deals the same hand");
            var ordered = CheckKit.VsDummy();
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash"], ordered.HandTitles(), "DrawOrder deals from the front of the list");
            c.Equal(0, ordered.Rng.Shuffle.Counter, "and does not shuffle");
        });

        c.Group("R9, R12: Block from a fight-start relic survives turn 1; Block cannot be gained before the fight is in progress", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(new StartBlock())
                .Enemy(new CheckDummy { OnAdded = (x, me) => x.GainBlock(me, 13, Props.MonsterMove), Steps = [new DummyStep("HIT", Damage: 4)] }).Start();
            c.Equal(10, f.Player.Block, "the relic's Block is there on turn 1 (the player's Block is not cleared on turn 1)");
            c.Equal(0, f.Enemy(0).Block, "a monster's Block request during set-up does nothing (Cubex Construct)");
            f.EndTurn();
            c.Equal(80, f.Player.Hp, "and it absorbs the first enemy turn");
            c.Equal(0, f.Player.Block, "it falls at the start of turn 2");
        });

        c.Group("R10: Innate cards are drawn on turn 1, and raise the draw", () =>
        {
            // The pile from the top: four Strikes, Born, Defend, Born+. Each Innate card is moved to the top
            // in turn, walking down the pile, so the lower one ends up above the higher one.
            var g = new FightBuilder().DrawOrder("StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad")
                .Add(Born).Add("DefendIronclad").Add(Born, upgraded: true).Enemy(new CheckDummy()).Start();
            c.Sequence(["Born+", "Born", "Strike", "Strike", "Strike"], g.HandTitles(), "Innate cards are drawn first, in the reverse of their pile order");

            var many = new FightBuilder().DrawOrder(CheckKit.StarterInOrder);
            for (var i = 0; i < 7; i++) many.Add(Born);
            c.Equal(7, many.Enemy(new CheckDummy()).Start().Hand.Count, "seven Innate cards raise the first draw to seven");

            var tooMany = new FightBuilder().DrawOrder(CheckKit.StarterInOrder);
            for (var i = 0; i < 12; i++) tooMany.Add(Born);
            var crowded = tooMany.Enemy(new CheckDummy()).Start();
            c.Equal(10, crowded.Hand.Count, "capped at ten");
            c.That(crowded.Hand.All(x => x.Title == "Born"), "all of them Innate");
            crowded.EndTurn();
            c.Equal(5, crowded.Hand.Count, "Innate does nothing on later turns");
        });
    }

    // ---------- section 2: start of the player's turn ----------

    private static void PlayerTurnStart(Check c)
    {
        c.Group("R11, R15, R32, R38: the order of a round's hooks", () =>
        {
            var f = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("HIT", Damage: 3)] });
            var rec = f.ApplyPower<Recorder>(f.Player, 1, f.Player)!;
            f.Play("Defend");
            rec.Seen.Clear();
            f.EndTurn();
            string[] expected =
            [
                "AfterAutoPostPlayPhaseEntered@1:hand4:energy2:block5",
                "BeforeSideTurnEndEarly(Player)@1:hand4:energy2:block5",
                "BeforeSideTurnEnd(Player)@1:hand4:energy2:block5",
                "BeforeFlush@1:hand4:energy2:block5",
                // After the flush: the hand is gone, Block still up.
                "AfterSideTurnEnd(Player)@1:hand0:energy2:block5",
                "BeforeSideTurnStart(Enemy)@1:hand0:energy2:block5",
                "AfterBlockCleared(Dummy)@1:hand0:energy2:block5",
                "AfterSideTurnStart(Enemy)@1:hand0:energy2:block5",
                // The monster hit for 3 into 5 Block.
                "BeforeSideTurnEndEarly(Enemy)@1:hand0:energy2:block2",
                "BeforeSideTurnEnd(Enemy)@1:hand0:energy2:block2",
                "AfterSideTurnEnd(Enemy)@1:hand0:energy2:block2",
                // Turn 2: counters are already raised; Block is still there in the first hook, gone in the next.
                "BeforeSideTurnStart(Player)@2:hand0:energy2:block2",
                "AfterBlockCleared(The Ironclad)@2:hand0:energy2:block0",
                "AfterEnergyReset@2:hand0:energy3:block0",
                "BeforeHandDraw@2:hand0:energy3:block0",
                "AfterCardDrawn(Strike,hand1)", "AfterCardDrawn(Strike,hand2)", "AfterCardDrawn(Strike,hand3)", "AfterCardDrawn(Defend,hand4)", "AfterCardDrawn(Defend,hand5)",
                "AfterPlayerTurnStart@2:hand5:energy3:block0",
                "AfterSideTurnStart(Player)@2:hand5:energy3:block0",
                "AfterAutoPrePlayPhaseEntered@2:hand5:energy3:block0",
            ];
            c.Sequence(expected, rec.Seen, "hooks of one round");
        });

        c.Group("R13, R16: energy is set to the maximum; turn and round go up together", () =>
        {
            var f = CheckKit.VsDummy();
            c.Equal(3, f.Energy, "3 energy on turn 1");
            f.Play("Strike", 0);
            f.EndTurn();
            c.Equal(3, f.Energy, "unspent energy is not carried over");
            c.Equal(2, f.Turn, "turn 2");
            c.Equal(2, f.Round, "round 2");
            f.GainEnergy(2);
            c.Equal(5, f.Energy, "a gain is not capped at the maximum");
            f.LoseEnergy(9);
            c.Equal(0, f.Energy, "a loss stops at 0");
        });
    }

    // ---------- section 3: drawing ----------

    private static void Drawing(Check c)
    {
        c.Group("R17, R18, R19: reshuffle only when a card is needed, the played card stays out, the hand stops at 10", () =>
        {
            var f = CheckKit.VsDummy();
            // Turn 1 hand: Strike, Strike, Defend, Defend, Bash. Draw pile: 5 cards. Discard: empty.
            f.Put(DrawTwo, PileType.Hand);
            f.Play("Strike", 0);
            f.Play("Draw Two");
            c.Equal(3, f.DrawPile.Count, "two cards drawn, no reshuffle yet");
            f.Put(DrawTwo, PileType.Hand);
            f.Put(DrawTwo, PileType.Hand);
            f.Play("Draw Two");
            c.Equal(1, f.DrawPile.Count, "one card left in the draw pile");
            c.Equal(0, f.Rng.Shuffle.Counter, "the pile is not reshuffled while it still has cards");
            var discardBefore = f.DiscardPile.Count;
            f.Play("Draw Two");
            // One card came off the old pile; then the discard pile (the Strike and the spent Draw Twos,
            // but not the Draw Two being played) was shuffled in, and one more card was drawn.
            c.That(f.Rng.Shuffle.Counter > 0, "the reshuffle uses the Shuffle stream");
            c.Equal(discardBefore - 1, f.DrawPile.Count, "the discard pile became the draw pile, minus the card drawn");
            c.Equal(1, f.DiscardPile.Count, "only the card that was being played is in the discard pile");
            c.That(ReferenceEquals(f.DiscardPile[0], f.DiscardPile.Single(x => x.Def == DrawTwo)), "and it is the played card");

            // The hand limit: an eleventh card stays on top of the draw pile; a created card goes to the discard pile.
            var g = CheckKit.VsDummy();
            for (var i = 0; i < 4; i++) g.Put(Blank, PileType.Hand);
            g.Put(DrawTwo, PileType.Hand);
            c.Equal(10, g.Hand.Count, "ten cards in hand");
            var top = g.DrawPile[0];
            g.Play("Draw Two");
            c.Equal(10, g.Hand.Count, "the hand stops at ten: one card drawn into the freed slot");
            c.That(!ReferenceEquals(g.DrawPile[0], top) && g.DrawPile.Count == 4, "the second card stayed on the draw pile");
            var made = g.CreateCard("Slimed");
            g.AddGeneratedCard(made, PileType.Hand);
            c.Equal(PileType.Discard, made.Pile, "a card added to a full hand goes to the discard pile");

            // Both piles empty: the draw just stops.
            var h = CheckKit.VsDummy();
            h.Clear(PileType.Draw);
            h.Put(DrawTwo, PileType.Hand);
            h.Play("Draw Two");
            c.Equal(5, h.Hand.Count, "nothing to draw, nothing happens");
        });

        c.Group("R18: the reshuffle sorts first, so the result does not depend on discard order", () =>
        {
            List<string> After(bool reversed)
            {
                var f = CheckKit.VsDummy(seed: 5);
                f.Clear(PileType.Draw);
                string[] cards = ["Bash", "StrikeIronclad", "DefendIronclad", "Slimed", "Wound", "StrikeIronclad+"];
                foreach (var card in reversed ? Enumerable.Reverse(cards) : cards) f.Put(card, PileType.Discard);
                f.Shuffle();
                return f.DrawPile.Select(x => x.Title).ToList();
            }
            c.Sequence(After(false), After(true), "same cards, other discard order, same seed");
        });

        c.Group("R21: where cards land in a pile", () =>
        {
            var f = CheckKit.VsDummy();
            var bottom = f.CreateCard("Wound");
            f.AddGeneratedCard(bottom, PileType.Draw);
            c.That(ReferenceEquals(f.DrawPile[^1], bottom), "the default is the bottom of the draw pile, not a random place");
            var top = f.CreateCard("Wound");
            f.AddGeneratedCard(top, PileType.Draw, PilePosition.Top);
            c.That(ReferenceEquals(f.DrawPile[0], top), "Top is index 0, the next card drawn");
            var before = f.Rng.Shuffle.Counter;
            f.AddGeneratedCard(f.CreateCard("Wound"), PileType.Draw, PilePosition.Random);
            c.Equal(before + 1, f.Rng.Shuffle.Counter, "a random position takes one Shuffle draw");
            var toHand = f.CreateCard("Slimed");
            f.AddGeneratedCard(toHand, PileType.Hand);
            c.That(ReferenceEquals(f.Hand[^1], toHand), "cards enter the hand at the right end");
        });

        c.Group("status cards: Slimed, Dazed, Void, Wound, Burn, Toxic, Beckon", () =>
        {
            var f = CheckKit.VsDummy();
            f.Put("Slimed", PileType.Hand);
            c.Equal("Draw 1 card. / Exhaust.", Fight.CardText(f, f.Hand[^1], PileType.Hand), "Slimed's text");
            var top = f.DrawPile[0];
            f.Play("Slimed");
            c.Equal(2, f.Energy, "Slimed costs 1");
            c.That(ReferenceEquals(f.Hand[^1], top), "and draws a card");
            c.Equal(1, f.ExhaustPile.Count, "and is exhausted");

            var g = CheckKit.VsDummy();
            g.Put("Dazed", PileType.Hand);
            g.Put("Wound", PileType.Hand);
            var snapshot = g.Snapshot();
            var dazed = snapshot.State["hand"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == "Dazed")!;
            c.Equal("-", dazed["cost"]!.GetValue<string>(), "an unplayable status shows cost -");
            c.Equal("Unplayable. / Ethereal.", dazed["text"]!.GetValue<string>(), "Dazed's text, keywords in the game's order");
            c.Equal(false, dazed["playable"]!.GetValue<bool>(), "not playable");
            c.Equal("HasUnplayableKeyword", dazed["unplayable"]!.GetValue<string>(), "the reason as the mod prints it");
            g.EndTurn();
            c.That(g.ExhaustPile.Any(x => x.Def.ClassName == "Dazed"), "Dazed is exhausted at end of turn");
            c.That(g.DrawPile.Concat(g.DiscardPile).Concat(g.Hand).Any(x => x.Def.ClassName == "Wound"), "Wound stays in the deck");

            // Void: lose 1 energy when drawn, also in the turn-start draw (after the reset: 2 left).
            var v = CheckKit.VsDummy();
            v.Put("Void", PileType.Draw, PilePosition.Top);
            v.EndTurn();
            c.Equal(2, v.Energy, "a Void in the opening five leaves 2 energy");
            var moved = v.CreateCard("Void");
            v.AddGeneratedCard(moved, PileType.Hand);
            c.Equal(2, v.Energy, "a Void put in hand by another route costs nothing");

            // Burn 2, Toxic 5 (blockable), Beckon 6 (not blockable): one instance each, in hand order.
            var b = CheckKit.VsDummy();
            b.Put("Burn", PileType.Hand);
            b.Put("Toxic", PileType.Hand);
            b.Put("Beckon", PileType.Hand);
            b.SetBlock(b.Player, 4);
            b.EndTurn();
            c.Equal(80 - 3 - 6, b.Player.Hp, "Burn 2 and Toxic 5 into 4 Block lose 3; Beckon's 6 ignores Block");
            c.Equal(3, b.DiscardPile.Count(x => x.Type == CardType.Status), "all three go to the discard pile");
            var t = CheckKit.VsDummy();
            t.Put("Toxic", PileType.Hand);
            t.Play("Toxic");
            c.Equal(1, t.ExhaustPile.Count, "playing Toxic exhausts it");
            t.EndTurn();
            c.Equal(80, t.Player.Hp, "and then it does no damage");

            var w = CheckKit.VsDummy();
            var wither = w.Put("Wither", PileType.Hand);
            CoreStatuses.FakeUpgradeWither(wither);
            c.Equal("Wither+1", wither.Title, "a fake-upgraded Wither's title");
            w.EndTurn();
            c.Equal(74, w.Player.Hp, "and its damage: 3 + 3");

            var fe = CheckKit.VsDummy();
            fe.Put("FranticEscape", PileType.Hand);
            fe.Play("Frantic Escape");
            fe.EndTurn();
            var again = fe.AllCards.Single(x => x.Def.ClassName == "FranticEscape");
            c.Equal(2, fe.CostOf(again), "Frantic Escape costs 1 more each time it was played");
        });
    }

    // ---------- section 4: playing a card ----------

    private static void PlayingCards(Check c)
    {
        c.Group("R22: play conditions and the reasons the snapshot gives", () =>
        {
            var f = CheckKit.VsDummy();
            f.Put(Costly, PileType.Hand);
            f.Play("Strike", 0);
            var snapshot = f.Snapshot();
            var costly = snapshot.State["hand"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == "Costly")!;
            c.Equal("not enough energy", costly["unplayable"]!.GetValue<string>(), "too expensive");
            c.That(snapshot.Actions.All(a => !a.Label.Contains("Costly", StringComparison.Ordinal)), "no action for it");
            c.That(!f.Execute($"play:{f.HandIndex("Costly")}").Ok, "and playing it is refused");
            c.Equal(2, f.Energy, "a refused play costs nothing");
            c.That(!f.Execute("play:0").Ok, "an enemy card without a target is refused");
            c.That(!f.Execute("play:0>e7").Ok, "a target that does not exist is refused");
        });

        c.Group("R23: X costs take all energy; X can be 0", () =>
        {
            var f = CheckKit.VsDummies(new CheckDummy { Hp = 50 }, new CheckDummy { Hp = 50 });
            f.Put(Whirl, PileType.Hand);
            c.Equal("X", f.CostText(f.Hand[^1]), "the cost reads X");
            f.Play("Strike", 0);
            f.Play("Whirl");
            c.Equal(0, f.Energy, "all energy spent");
            c.Equal(50 - 6 - 10, f.Enemy(0).Hp, "X = 2: two hits on the first enemy");
            c.Equal(40, f.Enemy(1).Hp, "and on the second");
            f.Put(Whirl, PileType.Hand);
            c.That(f.Snapshot().Actions.Any(a => a.Label == "Play Whirl"), "an X card is playable with 0 energy");
            f.Play("Whirl");
            c.Equal(40, f.Enemy(1).Hp, "X = 0 does nothing");
        });

        c.Group("R24, R25, R26: where the card sits during the play and where it goes", () =>
        {
            var f = CheckKit.VsDummy();
            var rec = f.ApplyPower<Recorder>(f.Player, 1, f.Player)!;
            f.Play("Strike", 0);
            c.Sequence(["BeforeCardPlayed(Strike,1/1,pile Play)", "AfterCardPlayed(Strike,1/1)"], rec.Seen, "the card is in the Play pile while it resolves");
            c.That(f.DiscardPile.Count == 1 && f.DiscardPile[0].Title == "Strike", "an ordinary card goes to the discard pile");
            c.Equal(1, f.CardsPlayed, "one card played");

            f.Put(Form, PileType.Hand);
            f.Play("Form");
            c.Equal(2, f.Player.PowerAmount<StrengthPower>(), "the Power card's effect");
            c.That(f.AllCards.All(x => x.Def != Form), "a Power card leaves the fight: it is in no pile");
            c.Equal(0, f.ExhaustPile.Count, "and was not exhausted");

            rec.Seen.Clear();
            f.Put(Burner, PileType.Hand);
            f.Play("Burner");
            c.Equal(1, f.ExhaustPile.Count, "a card with Exhaust goes to the exhaust pile");
            c.That(rec.Seen.Contains("AfterCardExhausted(Burner,False)"), "and the exhaust hook runs");
            c.That(f.History.WasCardExhaustedThisTurn(f), "the history knows a card was exhausted this turn");
        });

        c.Group("R28: a card that is played twice runs the whole sequence twice", () =>
        {
            var f = CheckKit.VsDummy();
            f.ApplyPower<Echo>(f.Player, 1, f.Player);
            var rec = f.ApplyPower<Recorder>(f.Player, 1, f.Player)!;
            f.Play("Strike", 0);
            c.Equal(88, f.Enemy(0).Hp, "two runs of 6");
            c.Equal(2, f.Energy, "paid once");
            c.Sequence(
                ["BeforeCardPlayed(Strike,1/2,pile Play)", "AfterCardPlayed(Strike,1/2)", "BeforeCardPlayed(Strike,2/2,pile Play)", "AfterCardPlayed(Strike,2/2)"],
                rec.Seen, "before and after hooks per run");
            c.That(!f.Player.HasPower<Echo>(), "the charge is spent");
            c.Equal(2, f.History.CardPlaysFinishedThisTurn(f, CardType.Attack), "each run counts as a play in the history");
            c.Equal(1, f.DiscardPile.Count, "the card moves to the discard pile once");
        });

        c.Group("R29: auto-play", () =>
        {
            // The top card is played for free; an X card uses the energy at hand without spending it.
            var f = CheckKit.VsDummy(new CheckDummy { Hp = 60 });
            f.Put(Whirl, PileType.Draw, position: PilePosition.Top);
            f.Put(TopDeck, PileType.Hand);
            f.Play("Top Deck");
            c.Equal(45, f.Enemy(0).Hp, "X is the 3 energy at hand");
            c.Equal(3, f.Energy, "and none of it is spent");
            c.Equal(1, f.CardsPlayed, "only the card played from the hand counts as played");

            // An unplayable card moves on without resolving; a targeted card gets a random target (one CombatTargets draw).
            var g = CheckKit.VsDummy();
            g.Put("Wound", PileType.Draw, PilePosition.Top);
            g.Put(TopDeck, PileType.Hand);
            g.Play("Top Deck");
            c.That(g.DiscardPile.Any(x => x.Def.ClassName == "Wound"), "an unplayable card goes to the discard pile unplayed");
            g.Put("StrikeIronclad", PileType.Draw, PilePosition.Top);
            g.Put(TopDeck, PileType.Hand);
            var before = g.Rng.CombatTargets.Counter;
            g.Play("Top Deck");
            c.Equal(94, g.Enemy(0).Hp, "the Strike was played on the only enemy");
            c.Equal(before + 1, g.Rng.CombatTargets.Counter, "the target roll is made even with one enemy");

            // An empty draw pile is refilled from the discard pile first.
            var h = CheckKit.VsDummy();
            h.Clear(PileType.Draw);
            h.Put("DefendIronclad", PileType.Discard);
            h.Put(TopDeck, PileType.Hand);
            h.Play("Top Deck");
            c.Equal(5, h.Player.Block, "the discard pile was shuffled in and its card played");
        });

        c.Group("R30: hand order after plays and draws", () =>
        {
            var f = CheckKit.VsDummy();
            f.Clear(PileType.Hand);
            f.Put("StrikeIronclad", PileType.Hand);
            f.Put("Bash", PileType.Hand);
            f.Put(DrawTwo, PileType.Hand);
            f.Put("DefendIronclad", PileType.Hand);
            f.Put(Blank, PileType.Hand);
            f.Play("Draw Two");
            c.Sequence(["Strike", "Bash", "Defend", "Blank", "Strike", "Strike"], f.HandTitles(), "the played card leaves, later cards shift left, drawn cards arrive on the right");
        });

        c.Group("R27, R3, R47: the fight does not stop in the middle of a card; a player who dies on the killing blow has lost", () =>
        {
            var f = CheckKit.VsDummy(new CheckDummy { Hp = 50 });
            f.Put(Pyrrhic, PileType.Hand);
            f.Play("Pyrrhic", 0);
            c.That(f.IsOver, "the fight is over");
            c.That(!f.Won, "both dead is a loss");
            c.Equal(0, f.Player.Hp, "the self-damage after the kill still landed");
            c.Equal("lost", f.Snapshot().State["result"]!.GetValue<string>(), "the end snapshot says lost");

            // After the last enemy died, draws and Block do nothing, but the card finishes and the win is processed.
            var g = CheckKit.VsDummy(new CheckDummy { Hp = 5 });
            var finisher = new CardDef("CheckFinisher", "Finisher", CardType.Attack, CardRarity.Common, 0, TargetType.AnyEnemy)
            {
                Play = x =>
                {
                    x.Attack(10).Execute();
                    x.GainBlock(7);
                    x.Draw(2);
                    x.Apply<StrengthPower>(x.Owner, 3);
                },
            };
            g.Put(finisher, PileType.Hand);
            var rec = g.ApplyPower<Recorder>(g.Player, 1, g.Player)!;
            g.Play("Finisher", 0);
            c.That(g.IsOver && g.Won, "won");
            c.That(rec.Seen.Contains("AfterCardPlayed(Finisher,1/1)"), "the after-play hook still ran");
            c.That(!rec.Seen.Any(s => s.StartsWith("AfterCardDrawn", StringComparison.Ordinal)), "no card was drawn after the kill");
        });
    }

    // ---------- section 5: end of the player's turn ----------

    private static void EndOfTurn(Check c)
    {
        c.Group("R33, R35: Ethereal beats Retain; Retain keeps its place; the flush is not a discard", () =>
        {
            var f = CheckKit.VsDummy();
            f.Clear(PileType.Hand);
            var keeper = f.Put(Keeper, PileType.Hand);
            f.Put(Ghost, PileType.Hand);
            f.Put("StrikeIronclad", PileType.Hand);
            f.Put("DefendIronclad", PileType.Hand);
            var rec = f.ApplyPower<Recorder>(f.Player, 1, f.Player)!;
            f.EndTurn();
            c.That(rec.Seen.Contains("AfterCardExhausted(Ghost,True)"), "an Ethereal card is exhausted even with Retain");
            c.That(!rec.Seen.Any(s => s.StartsWith("AfterCardDiscarded", StringComparison.Ordinal)), "the end-of-turn flush fires no discard hook");
            c.That(ReferenceEquals(f.Hand[0], keeper), "the retained card stays at the left");
            c.Equal(6, f.Hand.Count, "and the new draw is added after it");
            c.Sequence(["Strike", "Defend"], f.DiscardPile.Select(x => x.Title), "the rest was flushed in hand order");
        });

        c.Group("R34: end-of-turn cards resolve after the Ethereal exhausts, left to right, and stop when the player dies", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(5).Enemy(new CheckDummy()).Start();
            f.Clear(PileType.Hand);
            f.Put("Burn", PileType.Hand);
            f.Put("Dazed", PileType.Hand);
            f.Put("Infection", PileType.Hand);
            f.Put("Burn", PileType.Hand);
            var rec = f.ApplyPower<Recorder>(f.Player, 1, f.Player)!;
            f.EndTurn();
            c.That(f.IsOver && !f.Won, "2 + 3 kills a player on 5 HP");
            c.Equal("AfterCardExhausted(Dazed,True)", rec.Seen.FirstOrDefault(s => s.StartsWith("AfterCardExhausted", StringComparison.Ordinal)), "Dazed went first");
            c.Equal(0, f.Player.Hp, "dead");
        });

        c.Group("R36: temporary Strength ends after the flush, and Artifact stops it being taken back", () =>
        {
            var f = CheckKit.VsDummy();
            f.ApplyPower<CheckFlex>(f.Player, 5, f.Player);
            c.Equal(5, f.Player.PowerAmount<StrengthPower>(), "Strength now");
            f.ApplyPower<CheckFlex>(f.Player, 2, f.Player);
            c.Equal(7, f.Player.PowerAmount<StrengthPower>(), "more stacks give more Strength");
            f.EndTurn();
            c.That(!f.Player.HasPower<StrengthPower>() && !f.Player.HasPower<CheckFlex>(), "gone at the end of the turn");

            var g = CheckKit.VsDummy();
            g.ApplyPower<CheckFlex>(g.Player, 5, g.Player);
            g.ApplyPower<ArtifactPower>(g.Player, 1, g.Player);
            g.EndTurn();
            c.Equal(5, g.Player.PowerAmount<StrengthPower>(), "Artifact blocks the negative Strength: it stays for good");
            c.That(!g.Player.HasPower<ArtifactPower>(), "and Artifact is used up");

            // The negative kind on a monster ends at the end of the enemy turn; Artifact stops all of it.
            var h = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("HIT", Damage: 12)] });
            h.ApplyPower<CheckMangle>(h.Enemy(0), 10, h.Player);
            c.Equal(-10, h.Enemy(0).PowerAmount<StrengthPower>(), "Strength down at once");
            c.Sequence(["attack 2"], h.IntentTexts(h.Enemy(0)), "and the intent shows it");
            h.EndTurn();
            c.Equal(78, h.Player.Hp, "it covered that enemy turn");
            c.That(!h.Enemy(0).HasPower<StrengthPower>(), "and was given back after it");
            var k = CheckKit.VsDummy(new CheckDummy { OnAdded = (x, me) => x.ApplyPower<ArtifactPower>(me, 1, me) });
            k.ApplyPower<CheckMangle>(k.Enemy(0), 10, k.Player);
            c.That(!k.Enemy(0).HasPower<StrengthPower>() && !k.Enemy(0).HasPower<CheckMangle>(), "row 38: Artifact stops the whole effect");
            c.That(!k.Enemy(0).HasPower<ArtifactPower>(), "and loses its stack");
        });

        c.Group("R37: this-turn cost changes end with the turn; until-played ones with the play", () =>
        {
            var f = CheckKit.VsDummy();
            var bash = f.Hand[f.HandIndex("Bash")];
            bash.SetCostThisTurn(0);
            c.Equal(0, f.CostOf(bash), "free this turn");
            var strike = f.Hand[f.HandIndex("Strike")];
            strike.SetCostUntilPlayed(0);
            f.EndTurn();
            c.Equal(2, f.CostOf(bash), "back to 2 next turn");
            c.Equal(0, f.CostOf(strike), "an until-played change survives the turn");
        });
    }

    // ---------- section 6: the enemy turn ----------

    private static void EnemyTurn(Check c)
    {
        c.Group("R40, R9 of the damage rules: when Block falls", () =>
        {
            var f = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("GUARD", Damage: 3, Block: 5), new DummyStep("WAIT")] });
            f.Play("Defend");
            f.EndTurn();
            c.Equal(80, f.Player.Hp, "the player's Block lasted through the enemy turn");
            c.Equal(0, f.Player.Block, "and fell at the start of the player's turn");
            c.Equal(5, f.Enemy(0).Block, "a monster's Block from its move is up through the player's next turn");
            f.Play("Strike", 0);
            c.Equal(99, f.Enemy(0).Hp, "it absorbs 5 of the Strike's 6");
            f.EndTurn();
            c.Equal(0, f.Enemy(0).Block, "and falls at the start of the next enemy turn");
        });

        c.Group("R39: a monster added during the enemy turn waits; one added during the player turn acts", () =>
        {
            var summoner = new CheckDummy
            {
                DisplayName = "Summoner",
                Steps = [new DummyStep("CALL", Then: (x, _) => x.AddMonster(new CheckDummy { DisplayName = "Pup", Hp = 8, Steps = [new DummyStep("NIP", Damage: 2)] })), new DummyStep("WAIT")],
            };
            var f = CheckKit.VsDummy(summoner);
            f.EndTurn();
            c.Equal(80, f.Player.Hp, "the pup did not act on the turn it arrived");
            c.Equal(1, f.Enemy(1).RosterIndex, "it is e1");
            c.Sequence(["attack 2"], f.IntentTexts(f.Enemy(1)), "and shows its intent on the next player turn");
            f.EndTurn();
            c.Equal(78, f.Player.Hp, "then it acts");

            var g = CheckKit.VsDummy();
            g.AddMonster(new CheckDummy { DisplayName = "Pup", Hp = 8, Steps = [new DummyStep("NIP", Damage: 2)] });
            c.Sequence(["attack 2"], g.IntentTexts(g.Enemy(1)), "added on the player's turn: the intent is settled at once");
            g.EndTurn();
            c.Equal(78, g.Player.Hp, "and it acts in the coming enemy turn");
        });

        c.Group("R41, R45: a monster that dies during its own move stops attacking but finishes the move", () =>
        {
            var biter = new CheckDummy
            {
                DisplayName = "Biter", Hp = 3,
                Steps = [new DummyStep("BITE", Damage: 2, Hits: 3, Then: (x, me) => x.ApplyPower<VulnerablePower>(x.Player, 1, me))],
            };
            var f = CheckKit.VsDummies(biter, new CheckDummy { Hp = 50 });
            f.ApplyPower<Spikes>(f.Player, 3, f.Player);
            f.EndTurn();
            c.Equal(78, f.Player.Hp, "the first hit landed although the answer to it killed the attacker; hits 2 and 3 did not happen");
            c.Equal(1, f.Player.PowerAmount<VulnerablePower>(), "the debuff after the attack was still applied");
            c.That(!f.Enemies.Contains(f.Enemy(0)), "the dead monster left the list when its move was done");
            c.That(f.InProgress, "the other enemy keeps the fight going");
        });
    }

    private static void MoveSelection(Check c)
    {
        c.Group("R42: the next move is chosen at the start of the next player turn, after the end-of-turn ticks", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(new Watcher()).Start();
            // Weak 1 with no skip pending: it is still there while the Watcher acts, and gone after the ticks.
            f.ApplyPower<WeakPower>(f.Player, 1, f.Enemy(0));
            f.Player.GetPower<WeakPower>()!.SkipNextDurationTick = false;
            c.Sequence(["sleep"], f.IntentTexts(f.Enemy(0)), "the first intent stands on turn 1");
            f.EndTurn();
            c.Sequence(["debuff"], f.IntentTexts(f.Enemy(0)), "it chose after Weak had ticked away");
            c.Sequence(["OPEN", "SAW_NONE"], f.Enemy(0).Monster!.MoveLog, "the log holds the chosen moves");
        });

        c.Group("R42: random branches, their repeat rules and the MonsterAi stream", () =>
        {
            List<string> Moves(string mode, int seed, int turns, out int draws)
            {
                var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(new Roller { Mode = mode }).Seed(seed).MaxTurns(200).Start();
                for (var t = 0; t < turns; t++) f.EndTurn();
                draws = f.Rng.MonsterAi.Counter;
                return f.Enemy(0).Monster!.MoveLog.ToList();
            }

            for (var seed = 1; seed <= 8; seed++)
            {
                var cannot = Moves("cannot", seed, 12, out var draws);
                c.Equal(12, draws, $"seed {seed}: one draw per choice, also when the choice is forced");
                c.That(cannot.Zip(cannot.Skip(1)).All(p => p.First != p.Second), $"seed {seed}: 'cannot repeat' never repeats");

                var twice = Moves("twice", seed, 40, out _);
                c.That(!Enumerable.Range(0, twice.Count - 2).Any(i => twice[i] == "A" && twice[i + 1] == "A" && twice[i + 2] == "A"), $"seed {seed}: at most two A in a row");
                c.That(!twice.Zip(twice.Skip(1)).Any(p => p.First == "B" && p.Second == "B"), $"seed {seed}: B never twice running");

                var once = Moves("once", seed, 30, out _);
                c.That(once.Count(m => m == "B") <= 1, $"seed {seed}: 'use only once' is used once");

                var cooldown = Moves("cooldown", seed, 40, out _);
                var ok = true;
                for (var i = 1; i < cooldown.Count; i++)
                {
                    var last3 = cooldown.Skip(Math.Max(0, i - 3)).Take(Math.Min(3, i)).ToList();
                    var last2 = cooldown.Skip(Math.Max(0, i - 2)).Take(Math.Min(2, i)).ToList();
                    var allBlocked = last3.Contains("A") && last2.Contains("B") && cooldown[i - 1] == "C";
                    // With every option blocked the first listed one is taken.
                    if (allBlocked) ok &= cooldown[i] == "A";
                    else ok &= !(cooldown[i] == "A" && last3.Contains("A")) && !(cooldown[i] == "B" && last2.Contains("B")) && !(cooldown[i] == "C" && cooldown[i - 1] == "C");
                }
                c.That(ok, $"seed {seed}: cooldowns hold: {string.Join("", cooldown)}");
            }

            // A branch as the initial state is resolved when the monster joins: one draw, and the move is logged.
            var first = new Dictionary<string, int> { ["A"] = 0, ["B"] = 0 };
            for (var seed = 1; seed <= 400; seed++)
            {
                var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(new Roller { Mode = "weights" }).Seed(seed).Start();
                if (seed == 1) c.Equal(1, f.Rng.MonsterAi.Counter, "a random first move takes one draw at set-up");
                first[f.Enemy(0).Monster!.MoveLog[0]]++;
            }
            c.That(first["A"] is > 120 and < 200, $"weights 0.4 / 0.6: A came first {first["A"]} times in 400");
        });

        c.Group("R44, R43: stun", () =>
        {
            var f = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("HIT", Damage: 9), new DummyStep("GUARD", Block: 4)] });
            f.Stun(f.Enemy(0));
            c.Sequence(["stunned"], f.IntentTexts(f.Enemy(0)), "the intent is replaced at once");
            c.Equal(0, f.IncomingDamage(), "a stunned monster adds no incoming damage");
            f.EndTurn();
            c.Equal(80, f.Player.Hp, "it does nothing on its turn");
            c.Sequence(["attack 9"], f.IntentTexts(f.Enemy(0)), "then it makes the move it was about to make");
            c.Sequence(["HIT", "HIT"], f.Enemy(0).Monster!.MoveLog, "the delayed move is logged again");
            f.EndTurn();
            c.Equal(71, f.Player.Hp, "the delayed hit");
            c.Sequence(["defend"], f.IntentTexts(f.Enemy(0)), "and the cycle goes on");

            var g = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("HIT", Damage: 9), new DummyStep("GUARD", Block: 4)] });
            g.Stun(g.Enemy(0), "GUARD");
            g.Stun(g.Enemy(0), "HIT");
            g.EndTurn();
            c.Sequence(["defend"], g.IntentTexts(g.Enemy(0)), "a named follow-up is used; a second stun on a waiting stun is ignored");
        });
    }

    // ---------- section 7: end of combat ----------

    private static void EndOfCombat(Check c)
    {
        c.Group("R46, R48: a win, and Burning Blood", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(70).Enemy(new CheckDummy { Hp = 6 }).Start();
            f.ApplyPower<StrengthPower>(f.Player, 1, f.Player);
            f.Play("Defend");
            f.Play("Strike", 0);
            c.That(f.IsOver && f.Won, "won when the last primary enemy died");
            c.Equal(76, f.Player.Hp, "Burning Blood heals 6");
            c.Equal(6, f.VictoryHeal, "the heal is recorded");
            c.That(f.Player.Powers.Count == 0 && f.Player.Block == 0, "powers and Block are gone after a win");
            var over = f.Snapshot();
            c.Equal(Fight.OverScreen, over.Screen, "the end screen");
            c.Equal("The fight is over: you won. Nothing is left to do; stop here.", over.Actions.Single().Label, "the one action's label");
            c.That(f.Execute(Fight.DoneAction).Ok && !f.Execute("end_turn").Ok, "done runs, nothing else does");

            var full = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(78).Enemy(new CheckDummy { Hp = 6 }).Start();
            full.Play("Strike", 0);
            c.Equal(80, full.Player.Hp, "the heal stops at max HP");
        });

        c.Group("R47: a loss, and no heal", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(10)
                .Enemy(new CheckDummy { Steps = [new DummyStep("HIT", Damage: 6, Hits: 3)] }).Enemy(new CheckDummy { Steps = [new DummyStep("HIT", Damage: 50)] }).Start();
            f.EndTurn();
            c.That(f.IsOver && !f.Won && !f.TimedOut, "lost");
            c.Equal(0, f.Player.Hp, "no heal on a loss");
            c.Equal(10, f.DamageTaken, "the third hit and the second monster did not happen");
        });

        c.Group("R45, R46, R49: minions die with the last primary enemy", () =>
        {
            var f = CheckKit.VsDummies(
                new CheckDummy { DisplayName = "Leader", Hp = 6 },
                new CheckDummy { DisplayName = "Follower", Hp = 40, OnAdded = (x, me) => x.ApplyPower<MinionPower>(me, 1, me) });
            var enemies = f.Snapshot().State["enemies"]!.AsArray();
            c.Equal(true, enemies[0]!["primary"]?.GetValue<bool>(), "with minions about, the snapshot marks the primary enemy");
            c.That(enemies[1]!["primary"] is null, "and not the minion");
            f.Play("Strike", 0);
            c.That(f.IsOver && f.Won, "killing the leader wins");
            c.Equal(0, f.Enemy(1).Hp, "the follower was killed with it");
        });

        c.Group("R46: a power can keep the fight alive; R50: an escape is a win", () =>
        {
            var f = CheckKit.VsDummy(new CheckDummy { Hp = 6, OnAdded = (x, me) => x.ApplyPower<Hatch>(me, 1, me) });
            f.Play("Strike", 0);
            c.That(!f.IsOver, "the fight goes on");
            c.Equal(1, f.Enemies.Count, "with the hatchling");
            c.Equal(1, f.Enemy(1).RosterIndex, "which is e1: indexes are never reused");
            c.That(f.Snapshot().Actions.Any(a => a.Id.EndsWith(">e1", StringComparison.Ordinal)), "and can be targeted");
            f.Play("Strike", 1);
            c.That(f.IsOver && f.Won, "won when it is dead too");

            var g = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("FLEE", Then: (x, me) => x.Escape(me))] });
            g.EndTurn();
            c.That(g.IsOver && g.Won, "the last enemy escaping is a win");
            c.Equal(80, g.Player.Hp, "at full HP nothing to heal");
        });

        c.Group("the arena's house rule: a fight not won by the end of the last turn is lost", () =>
        {
            var f = new FightBuilder().Enemy(new CheckDummy { Hp = 9999 }).MaxTurns(3).Start();
            f.EndTurn().EndTurn();
            c.That(!f.IsOver && f.Turn == 3, "turn 3 is still played");
            f.EndTurn();
            c.That(f.IsOver && !f.Won && f.TimedOut, "then it is lost by time-out");
            c.Equal("not won within 3 turns", f.Snapshot().State["reason"]!.GetValue<string>(), "the end snapshot says why");
            c.Equal(40, Tuning.MaxTurns, "the real limit");
        });
    }

    /// <summary>A positive temporary Strength, as Setup Strike and the Flex Potion have.</summary>
    private sealed class CheckFlex : TemporaryStrengthPower
    {
        public override string Name => "Flex";
    }

    /// <summary>A negative temporary Strength, as Mangle has.</summary>
    private sealed class CheckMangle : TemporaryStrengthPower
    {
        public override string Name => "Mangle";
        protected override bool IsPositive => false;
    }
}
