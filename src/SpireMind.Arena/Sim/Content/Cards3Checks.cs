namespace SpireMind.Arena;

/// <summary>
/// Scripted checks for Cards3: every card of the chunk in its base and its upgraded form, with
/// the numbers worked out by hand from the game's classes, and the edge cases its entry in
/// cards-3.json names. Random picks are checked against a fresh copy of the stream the game
/// uses, so the expected card or target does not come from the code under test.
/// </summary>
public sealed class Cards3Checks : IArenaChecks
{
    // ---------- cards that exist only for these checks ----------

    /// <summary>Lose 2 HP, then deal 5 (the shape of the game's Hemokinesis).</summary>
    private static readonly CardDef Bleed = new("CheckBleed", "Bleed", CardType.Attack, CardRarity.Common, 0, TargetType.AnyEnemy)
    {
        Vars = { ["Damage"] = 5 },
        Play = c =>
        {
            c.LoseHp(2);
            c.Attack(c.V("Damage")).Execute();
        },
    };

    /// <summary>Lose 1 HP twice, then deal 5: two damage events from one card.</summary>
    private static readonly CardDef Gash = new("CheckGash", "Gash", CardType.Attack, CardRarity.Common, 0, TargetType.AnyEnemy)
    {
        Vars = { ["Damage"] = 5 },
        Play = c =>
        {
            c.LoseHp(1);
            c.LoseHp(1);
            c.Attack(c.V("Damage")).Execute();
        },
    };

    /// <summary>4 damage to its own player that Block can absorb, with the card as the source.</summary>
    private static readonly CardDef Scratch = new("CheckScratch", "Scratch", CardType.Skill, CardRarity.Common, 0, TargetType.Self)
    {
        Play = c => c.F.Damage(c.Owner, 4, Props.CardUnpowered, c.Owner, c.Card),
    };

    /// <summary>Lose 2 HP, then exhaust a chosen hand card: a card that hurts and then asks.</summary>
    private static readonly CardDef BleedPick = new("CheckBleedPick", "Bleed Pick", CardType.Skill, CardRarity.Common, 0, TargetType.Self)
    {
        Play = c =>
        {
            c.LoseHp(2);
            if (c.F.ChooseHandCardToExhaust() is { } card) c.F.Exhaust(card);
        },
    };

    /// <summary>An Attack that cannot be played and has no damage number.</summary>
    private static readonly CardDef Dud = new("CheckDud", "Dud", CardType.Attack, CardRarity.Common, CardDef.NoCost, TargetType.AnyEnemy)
    {
        Keywords = CardKeywords.Unplayable,
    };

    /// <summary>Calculated damage: 3 plus 2 times a multiplier that is 4 with no target and 100 against one.</summary>
    private static readonly CardDef Slam = new("CheckSlam", "Slam", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy)
    {
        Vars = { ["CalculationBase"] = 3, ["ExtraDamage"] = 2 },
        DamageMultiplier = (_, _, target) => target is null ? 4 : 100,
        Play = c => c.CalculatedAttack().Execute(),
    };

    // ---------- powers that exist only for these checks ----------

    /// <summary>Writes down what happens, in order.</summary>
    private sealed class Log : Power
    {
        public List<string> Seen = new();

        public override string Name => "Log";
        public override PowerType Type => PowerType.Buff;
        public override bool IsVisible => false;
        public override string Describe() => "";
        public override void OnCloned(CloneMap map) => Seen = new List<string>(Seen);

        public override void AfterDamageGiven(Fight f, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardInstance? cardSource)
        {
            if (target.IsEnemy) Seen.Add($"hit {result.UnblockedDamage}");
        }

        public override void AfterBlockGained(Fight f, Creature creature, decimal amount, ValueProp props, CardInstance? cardSource)
        {
            if (creature.IsPlayer) Seen.Add($"block {(int)amount}");
        }

        public override void AfterCardExhausted(Fight f, CardInstance card, bool causedByEthereal) => Seen.Add($"exhausted {card.Title}");
        public override void AfterCardDrawn(Fight f, CardInstance card, bool fromHandDraw) => Seen.Add($"drawn {card.Title}");
        public override void AfterCardPlayed(Fight f, CardPlay play) => Seen.Add($"played {play.Card.Title}");
        public override void AfterCardEnteredCombat(Fight f, CardInstance card) => Seen.Add($"entered {card.Title}");

        public override void AfterPowerAmountChanged(Fight f, Power power, decimal amount, Creature? applier, CardInstance? cardSource) =>
            Seen.Add($"power {power.ClassName} {(int)amount}");
    }

    /// <summary>The next card played runs twice.</summary>
    private sealed class Echo : Power
    {
        public override string Name => "Echo";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";
        public override int ModifyCardPlayCount(Fight f, CardInstance card, Creature? target, int playCount) => playCount + 1;
        public override void AfterModifyingCardPlayCount(Fight f, CardInstance card) => f.Decrement(this);
    }

    /// <summary>Draws a card whenever a card is exhausted (the shape of the game's Dark Embrace).</summary>
    private sealed class DrawOnExhaust : Power
    {
        public override string Name => "Draw On Exhaust";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";
        public override void AfterCardExhausted(Fight f, CardInstance card, bool causedByEthereal) => f.Draw(1);
    }

    // ---------- short forms ----------

    private static CheckDummy Idle(int hp = 100, string name = "Dummy") => new() { Hp = hp, DisplayName = name };

    private static CheckDummy Hitter(int damage, int hits = 1) => new() { Steps = [new DummyStep("HIT", Damage: damage, Hits: hits)] };

    private static CheckDummy Warded(string name = "Dummy") => new() { DisplayName = name, OnAdded = (f, me) => f.ApplyPower<ArtifactPower>(me, 1, me) };

    /// <summary>A fight with exactly this hand. The draw pile is Strike, Strike, Strike, Defend, Defend from the top;
    /// the other piles are empty; 3 energy; 80 HP. Without dummies: one idle dummy with 100 HP.</summary>
    private static Fight With(string[] hand, params CheckDummy[] dummies)
    {
        var f = dummies.Length == 0 ? CheckKit.VsDummy() : CheckKit.VsDummies(dummies);
        f.Clear(PileType.Hand);
        foreach (var card in hand) f.Put(card, PileType.Hand);
        return f;
    }

    private static string Text(Fight f, CardInstance card) => Fight.CardText(f, card, PileType.Hand);

    private static List<string> Titles(IEnumerable<CardInstance> cards) => cards.Select(card => card.Title).ToList();

    private static List<string> Ids(Fight f) => f.LegalActions().Select(a => a.Id).ToList();

    /// <summary>Brings a played card back to the hand with fresh energy, to play the same copy again.</summary>
    private static void Again(Fight f, CardInstance card)
    {
        f.MoveCard(card, PileType.Hand);
        f.SetEnergy(3);
    }

    private static Log Watch(Fight f)
    {
        var log = f.ApplyPower<Log>(f.Player, 1, f.Player)!;
        log.Seen.Clear();
        return log;
    }

    private static void Strength(Fight f, int amount) => f.ApplyPower<StrengthPower>(f.Player, amount, f.Player);
    private static void Dexterity(Fight f, int amount) => f.ApplyPower<DexterityPower>(f.Player, amount, f.Player);
    private static void Weak(Fight f) => f.ApplyPower<WeakPower>(f.Player, 1, f.Enemy(0));
    private static void Frail(Fight f) => f.ApplyPower<FrailPower>(f.Player, 1, f.Enemy(0));

    /// <summary>Vulnerable on an enemy from nobody, so that it is not the player who applied it.</summary>
    private static void Vulnerable(Fight f, int enemy, int amount) => f.ApplyPower<VulnerablePower>(f.Enemy(enemy), amount, null);

    private static void LoseHp(Fight f, int amount) => f.Damage(f.Player, amount, Props.NonCardHpLoss, f.Player);

    private static int EventsOn(Fight f, Creature creature) =>
        f.History.Count(e => e.Kind == HistoryKind.DamageReceived && e.ActorId == creature.Id);

    // ---------- the table ----------

    private sealed record Row(string Class, string Title, CardType Type, CardRarity Rarity, string Cost, string PlusCost, TargetType Target);

    /// <summary>cards-3.json, in pool order; the token last.</summary>
    private static readonly Row[] Rows =
    [
        new("PommelStrike", "Pommel Strike", CardType.Attack, CardRarity.Common, "1", "1", TargetType.AnyEnemy),
        new("PrimalForce", "Primal Force", CardType.Skill, CardRarity.Rare, "0", "0", TargetType.Self),
        new("Pyre", "Pyre", CardType.Power, CardRarity.Rare, "2", "2", TargetType.Self),
        new("Rage", "Rage", CardType.Skill, CardRarity.Uncommon, "0", "0", TargetType.Self),
        new("Rampage", "Rampage", CardType.Attack, CardRarity.Uncommon, "1", "1", TargetType.AnyEnemy),
        new("Rupture", "Rupture", CardType.Power, CardRarity.Uncommon, "1", "1", TargetType.Self),
        new("SecondWind", "Second Wind", CardType.Skill, CardRarity.Uncommon, "1", "1", TargetType.Self),
        new("SetupStrike", "Setup Strike", CardType.Attack, CardRarity.Common, "1", "1", TargetType.AnyEnemy),
        new("ShrugItOff", "Shrug It Off", CardType.Skill, CardRarity.Common, "1", "1", TargetType.Self),
        new("Spite", "Spite", CardType.Attack, CardRarity.Uncommon, "0", "0", TargetType.AnyEnemy),
        new("Stampede", "Stampede", CardType.Power, CardRarity.Uncommon, "2", "1", TargetType.Self),
        new("Stoke", "Stoke", CardType.Skill, CardRarity.Rare, "1", "1", TargetType.Self),
        new("Stomp", "Stomp", CardType.Attack, CardRarity.Uncommon, "3", "3", TargetType.AllEnemies),
        new("StoneArmor", "Stone Armor", CardType.Power, CardRarity.Uncommon, "1", "1", TargetType.Self),
        new("SwordBoomerang", "Sword Boomerang", CardType.Attack, CardRarity.Common, "1", "1", TargetType.RandomEnemy),
        new("Taunt", "Taunt", CardType.Skill, CardRarity.Uncommon, "1", "1", TargetType.AnyEnemy),
        new("TearAsunder", "Tear Asunder", CardType.Attack, CardRarity.Rare, "2", "2", TargetType.AnyEnemy),
        new("Thrash", "Thrash", CardType.Attack, CardRarity.Rare, "1", "1", TargetType.AnyEnemy),
        new("Thunderclap", "Thunderclap", CardType.Attack, CardRarity.Common, "1", "1", TargetType.AllEnemies),
        new("Tremble", "Tremble", CardType.Skill, CardRarity.Common, "1", "1", TargetType.AnyEnemy),
        new("TrueGrit", "True Grit", CardType.Skill, CardRarity.Common, "1", "1", TargetType.Self),
        new("TwinStrike", "Twin Strike", CardType.Attack, CardRarity.Common, "1", "1", TargetType.AnyEnemy),
        new("Unmovable", "Unmovable", CardType.Power, CardRarity.Rare, "2", "1", TargetType.Self),
        new("Unrelenting", "Unrelenting", CardType.Attack, CardRarity.Uncommon, "2", "2", TargetType.AnyEnemy),
        new("Uppercut", "Uppercut", CardType.Attack, CardRarity.Uncommon, "2", "2", TargetType.AnyEnemy),
        new("Vicious", "Vicious", CardType.Power, CardRarity.Uncommon, "1", "1", TargetType.Self),
        new("Whirlwind", "Whirlwind", CardType.Attack, CardRarity.Uncommon, "X", "X", TargetType.AllEnemies),
        new("GiantRock", "Giant Rock", CardType.Attack, CardRarity.Token, "1", "1", TargetType.AnyEnemy),
    ];

    /// <summary>The pool cards of the other card files that a fight may generate, in pool order: with the 27 of
    /// this chunk they are the 78 cards the game's random generation picks from (the pool's 87 without the
    /// Basic and Ancient cards, the two multiplayer-only cards, Feed and Not Yet).</summary>
    private static readonly string[] OtherGeneratable =
    [
        "Aggression", "Anger", "Armaments", "AshenStrike", "Barricade", "BattleTrance", "BloodWall", "Bloodletting", "Bludgeon", "BodySlam",
        "Brand", "Breakthrough", "Bully", "BurningPact", "Cascade", "Cinder", "Colossus", "Conflagration", "CrimsonMantle", "Cruelty",
        "DarkEmbrace", "DemonForm", "Dismantle", "Dominate", "DrumOfBattle", "EvilEye", "ExpectAFight", "FeelNoPain", "FiendFire", "FightMe",
        "FlameBarrier", "ForgottenRitual", "Havoc", "Headbutt", "Hellraiser", "Hemokinesis", "HowlFromBeyond", "Impervious", "InfernalBlade", "Inferno",
        "Inflame", "IronWave", "Juggernaut", "Juggling", "Mangle", "MoltenFist", "Offering", "OneTwoPunch", "PactsEnd", "PerfectedStrike", "Pillage",
    ];

    public void Run(Check c)
    {
        Definitions(c);
        PommelStrike(c);
        GiantRockAndPrimalForce(c);
        Pyre(c);
        Rage(c);
        Rampage(c);
        Rupture(c);
        SecondWind(c);
        SetupStrike(c);
        ShrugItOff(c);
        Spite(c);
        Stampede(c);
        Stoke(c);
        Stomp(c);
        StoneArmor(c);
        SwordBoomerang(c);
        Taunt(c);
        TearAsunder(c);
        Thrash(c);
        Thunderclap(c);
        Tremble(c);
        TrueGrit(c);
        TwinStrike(c);
        Unmovable(c);
        Unrelenting(c);
        Uppercut(c);
        Vicious(c);
        Whirlwind(c);
        Copies(c);
        WithTheOtherCardFiles(c);
    }

    private static bool HasCards(params string[] cards) => cards.All(card => Content.Registry.Has(ContentKind.Card, card));

    /// <summary>
    /// What these cards do together with cards and powers of Cards1 and Cards2, worked out from
    /// the game's classes of those. Each case runs only when what it needs is registered, so
    /// this file also passes on its own.
    /// </summary>
    private static void WithTheOtherCardFiles(Check c)
    {
        c.Group("Thrash and the calculated damage of other cards: worked out with no target", () =>
        {
            if (HasCards("BodySlam"))
            {
                var f = With(["Thrash", "BodySlam"]);
                f.SetBlock(f.Player, 7);
                f.Play("Thrash", 0);
                c.Equal(11m, f.DiscardPile[0].Get("Damage"), "Body Slam counts as the player's Block: 4 + 7");
                c.Sequence(["Body Slam"], Titles(f.ExhaustPile), "and is exhausted");
            }
            if (HasCards("PerfectedStrike"))
            {
                // Cards with the Strike tag: the three Strikes of the draw pile and Perfected Strike itself.
                var f = With(["Thrash", "PerfectedStrike"]);
                f.Play("Thrash", 0);
                c.Equal(18m, f.DiscardPile[0].Get("Damage"), "Perfected Strike counts as 6 + 2 x 4 = 14");
            }
            if (HasCards("Bully"))
            {
                var f = With(["Thrash", "Bully"]);
                Vulnerable(f, 0, 2);
                f.Play("Thrash", 0);
                c.Equal(88, f.Enemy(0).Hp, "Thrash itself hits the Vulnerable enemy for 6 twice");
                c.Equal(8m, f.DiscardPile[0].Get("Damage"), "Bully counts as its base 4: with no target there is no Vulnerable to count");
            }
            if (HasCards("AshenStrike"))
            {
                var f = With(["Thrash", "AshenStrike"]);
                f.Put("Wound", PileType.Exhaust);
                f.Play("Thrash", 0);
                c.Equal(13m, f.DiscardPile[0].Get("Damage"), "Ashen Strike counts as 6 + 3 x 1 card in the exhaust pile, read before it is exhausted itself");
            }
        });

        c.Group("Rupture, Spite and Tear Asunder with the cards that cost HP", () =>
        {
            if (HasCards("Hemokinesis"))
            {
                var f = With(["Rupture", "Hemokinesis", "Spite"]);
                f.Play("Rupture");
                f.Play("Hemokinesis", 0);
                c.Equal(78, f.Player.Hp, "Hemokinesis takes 2 HP");
                c.Equal(85, f.Enemy(0).Hp, "and hits for 15: Rupture's Strength comes after the card");
                c.Equal(1, f.Player.PowerAmount<StrengthPower>(), "1 Strength");
                f.Play("Spite", 0);
                c.Equal(73, f.Enemy(0).Hp, "Spite hits twice after the loss: 6 + 6");
            }
            if (HasCards("Bloodletting"))
            {
                var f = With(["Rupture", "Bloodletting", "TearAsunder"]);
                f.Play("Rupture");
                f.Play("Bloodletting");
                c.Equal(77, f.Player.Hp, "Bloodletting takes 3 HP");
                c.Equal(4, f.Energy, "and gives 2 energy");
                c.Equal(1, f.Player.PowerAmount<StrengthPower>(), "1 Strength from Rupture");
                f.Play("TearAsunder", 0);
                c.Equal(88, f.Enemy(0).Hp, "Tear Asunder hits twice after one loss: 6 + 6");
            }
        });

        c.Group("Stampede plays a card that asks a question while the turn is ending", () =>
        {
            if (!HasCards("Headbutt")) return;
            var f = With(["Stampede", "Headbutt"]);
            f.Put("DefendIronclad", PileType.Discard);
            f.Put("Bash", PileType.Discard);
            f.Play("Stampede");
            f.Do("end_turn");
            var s = f.Snapshot();
            c.Equal("card_select", s.Screen, "Headbutt's choice comes up in the middle of the turn's end");
            c.Equal(91, f.Enemy(0).Hp, "after its 9 damage");
            c.Equal(1, f.Turn, "still on turn 1");
            c.Sequence(["card:0", "card:1"], s.Actions.Select(a => a.Id), "one of the two cards of the discard pile");
            f.Do("card:1");
            c.Equal("combat", f.Snapshot().Screen, "the answer lets the turn end");
            c.Equal(2, f.Turn, "turn 2");
            c.Equal(91, f.Enemy(0).Hp, "Headbutt hit once");
            c.Equal("Bash", f.Hand[0].Title, "the chosen card went on top of the draw pile and is drawn first");
            c.Equal(1, f.Rng.Shuffle.Counter, "one Shuffle draw in all: Stampede's pick");
        });

        c.Group("Second Wind with the powers that answer an exhaust", () =>
        {
            if (Content.Registry.Has(ContentKind.Power, "FeelNoPainPower"))
            {
                var f = With(["SecondWind", "DefendIronclad", "DefendIronclad"]);
                // The log is applied first, so it hears each exhaust before Feel No Pain answers it.
                var log = Watch(f);
                f.ApplyPower("FeelNoPainPower", f.Player, 3, f.Player);
                log.Seen.Clear();
                f.Play("SecondWind");
                c.Equal(16, f.Player.Block, "3 from Feel No Pain and 5 from the card for each of two cards");
                c.Sequence(["exhausted Defend", "block 3", "block 5", "exhausted Defend", "block 3", "block 5", "played Second Wind"], log.Seen,
                    "each exhaust is answered in full before the card's own Block");
            }
            if (Content.Registry.Has(ContentKind.Power, "DarkEmbracePower"))
            {
                var f = With(["SecondWind", "DefendIronclad", "DefendIronclad"]);
                f.Put("DefendIronclad", PileType.Draw, PilePosition.Top);
                f.ApplyPower("DarkEmbracePower", f.Player, 1, f.Player);
                f.Play("SecondWind");
                c.Sequence(["Defend", "Strike"], f.HandTitles(), "the cards Dark Embrace draws during the effect stay, also a Skill");
                c.Equal(2, f.ExhaustPile.Count, "only the two that were in hand at the start are exhausted");
                c.Equal(10, f.Player.Block, "two gains");
            }
        });
    }

    private static void Copies(Check c)
    {
        c.Group("Cards3: a copy of a fight with these cards and powers goes on exactly like the original", () =>
        {
            var f = CheckKit.VsDummy(Hitter(5, 2));
            f.Clear(PileType.Hand);
            foreach (var card in new[] { "Rupture", "Unmovable+", "Rage", "Vicious", "Stomp", "TwinStrike", "Rampage", "ShrugItOff", "Thunderclap" }) f.Put(card, PileType.Hand);
            f.Put(Bleed, PileType.Hand);
            f.SetEnergy(9);
            f.ApplyPower<StampedePower>(f.Player, 1, f.Player);
            f.ApplyPower<FreeAttackPower>(f.Player, 2, f.Player);
            f.ApplyPower<PlatingPower>(f.Player, 3, f.Player);
            f.Play("Rupture");
            f.Play("Unmovable");
            f.Play("Rage");
            f.Play("Vicious");

            var copy = f.Clone();
            c.Equal(f.Seen(), copy.Seen(), "the copy shows the same fight");
            var untouched = f.Seen();
            copy.Do("end_turn");
            c.Equal(untouched, f.Seen(), "playing the copy does not change the original");

            copy = f.Clone();
            foreach (var name in new[] { "Bleed", "Twin Strike", "Rampage" })
            {
                var id = $"play:{f.HandIndex(name)}>e0";
                f.Do(id);
                copy.Do(id);
            }
            foreach (var name in new[] { "Shrug It Off", "Thunderclap" })
            {
                var id = $"play:{f.HandIndex(name)}";
                f.Do(id);
                copy.Do(id);
            }
            c.Equal(f.Seen(), copy.Seen(), "the same cards give the same fight");
            f.EndTurn();
            copy.EndTurn();
            c.Equal(f.Seen(), copy.Seen(), "also across the end of the turn, with Stampede's random pick");
            c.Equal(1, f.Player.PowerAmount<StrengthPower>(), "and things did happen: 1 Strength from Rupture for the 2 HP Bleed took");
            c.Equal(78, f.Player.Hp, "the enemy's 5 x 2 met the Block of Rage, Unmovable and Plating");
            c.That(!f.Player.HasPower<FreeAttackPower>() && !f.Player.HasPower<RagePower>(), "the two free Attacks and Rage are used up");
        });
    }

    private static void Definitions(Check c)
    {
        c.Group("Cards3: type, rarity, cost, target, title, keywords and tags of every card", () =>
        {
            var registry = Content.Registry;
            var f = CheckKit.VsDummy();
            foreach (var row in Rows)
            {
                c.Equal("Cards3", registry.SourceOf(ContentKind.Card, row.Class), $"{row.Class} is registered by Cards3");
                var card = f.Put(row.Class, PileType.Hand);
                var plus = f.Put(row.Class + "+", PileType.Hand);
                c.Equal(row.Title, card.Title, $"{row.Class}: title");
                c.Equal(row.Title + "+", plus.Title, $"{row.Class}: upgraded title");
                c.Equal(row.Type, card.Def.Type, $"{row.Class}: type");
                c.Equal(row.Rarity, card.Def.Rarity, $"{row.Class}: rarity");
                c.Equal(row.Target, card.Def.Target, $"{row.Class}: target");
                c.Equal(row.Cost, f.CostText(card), $"{row.Class}: cost");
                c.Equal(row.PlusCost, f.CostText(plus), $"{row.Class}: upgraded cost");
                c.That(plus.IsUpgraded && !plus.IsUpgradable && card.IsUpgradable, $"{row.Class}: one upgrade level");
                c.Equal(row.Class == "Tremble" ? CardKeywords.Exhaust : CardKeywords.None, card.Keywords, $"{row.Class}: keywords");
                c.Equal(row.Class == "Tremble" ? CardKeywords.Exhaust : CardKeywords.None, plus.Keywords, $"{row.Class}: keywords when upgraded");
                c.Equal(row.Class is "PommelStrike" or "SetupStrike" or "TwinStrike" ? CardTags.Strike : CardTags.None, card.Def.Tags, $"{row.Class}: tags");
                c.That(card.Def.CanBeGeneratedInCombat, $"{row.Class}: may be generated in a fight");
                c.That(Fight.CardText(null, card, PileType.Deck).Length > 0 && Fight.CardText(null, plus, PileType.Deck).Length > 0, $"{row.Class}: has a text outside a fight");
                c.That(Text(f, card).Length > 0 && Text(f, plus).Length > 0, $"{row.Class}: has a text in hand");
            }
        });

        c.Group("Cards3: the powers, the pool and what is left out", () =>
        {
            var registry = Content.Registry;
            (string Class, string Title)[] powers =
            [
                ("PyrePower", "Pyre"), ("RagePower", "Rage"), ("RupturePower", "Rupture"), ("SetupStrikePower", "Setup Strike"),
                ("StampedePower", "Stampede"), ("UnmovablePower", "Unmovable"), ("FreeAttackPower", "Free Attack"), ("ViciousPower", "Vicious"),
            ];
            foreach (var (name, title) in powers)
            {
                c.Equal("Cards3", registry.SourceOf(ContentKind.Power, name), $"{name} is registered by Cards3");
                var power = registry.NewPower(name);
                c.Equal(title, power.Name, $"{name}: the game's English title");
                c.Equal(PowerType.Buff, power.Type, $"{name}: a Buff");
                c.Equal(PowerStackType.Counter, power.StackType, $"{name}: a counter");
            }

            var pool = registry.IroncladPool.Where(d => d.IsGeneratableInCombat).Select(d => d.ClassName).ToList();
            var mine = Rows.Where(row => row.Rarity != CardRarity.Token).Select(row => row.Class).ToList();
            c.Equal(27, mine.Count, "27 pool cards in this chunk");
            c.Sequence(mine, pool.Where(mine.Contains), "all of them can be generated in a fight, in pool order");
            c.That(!pool.Contains("GiantRock"), "the token is not in the pool");
            c.That(!pool.Contains("StrikeIronclad") && !pool.Contains("DefendIronclad") && !pool.Contains("Bash"), "Basic cards are never generated");

            // Stoke picks by index from the whole list, so one card missing or one too many shifts every pick.
            // This can only be judged once the other two card files are in.
            var sources = registry.Cards.Keys.Select(name => registry.SourceOf(ContentKind.Card, name)).ToHashSet();
            if (sources.Contains("Cards1") && sources.Contains("Cards2"))
            {
                c.Equal(51, OtherGeneratable.Length, "51 generatable pool cards outside this chunk");
                c.Sequence(OtherGeneratable.Concat(mine), pool,
                    "the cards a fight may generate are the game's 78 in pool order (Stoke picks from this list by index: with a card missing or extra its picks differ from the game, so register the card or leave Stoke out)");
            }

            c.That(!registry.Has(ContentKind.Card, "Tank"), "Tank is not a card of the arena");
            c.That(registry.IsExcluded(ContentKind.Card, "Tank", out var why) && why.Contains("multiplayer only", StringComparison.Ordinal), "it is left out as multiplayer only");
            c.That(!pool.Contains("Tank"), "and is not in the pool");
        });
    }

    private static void PommelStrike(Check c)
    {
        c.Group("Pommel Strike: 9 damage, then draw 1", () =>
        {
            var f = With(["PommelStrike"]);
            var log = Watch(f);
            c.Equal("Deal 9 damage. Draw 1 card.", Text(f, f.Hand[0]), "text");
            f.Play("PommelStrike", 0);
            c.Equal(91, f.Enemy(0).Hp, "9 damage");
            c.Equal(2, f.Energy, "costs 1");
            c.Sequence(["Strike"], f.HandTitles(), "the top card of the draw pile was drawn");
            c.Equal(4, f.DrawPile.Count, "one card left the draw pile");
            c.Sequence(["Pommel Strike"], Titles(f.DiscardPile), "it goes to the discard pile");
            c.Sequence(["hit 9", "drawn Strike", "played Pommel Strike"], log.Seen, "the hit comes before the draw");
        });

        c.Group("Pommel Strike+: 10 damage, then draw 2", () =>
        {
            var f = With(["PommelStrike+"]);
            c.Equal("Deal 10 damage. Draw 2 cards.", Text(f, f.Hand[0]), "text");
            f.Play("PommelStrike", 0);
            c.Equal(90, f.Enemy(0).Hp, "10 damage");
            c.Sequence(["Strike", "Strike"], f.HandTitles(), "two cards drawn");
            c.Equal(3, f.DrawPile.Count, "two cards left the draw pile");
        });

        c.Group("Pommel Strike: Strength, Weak, Vulnerable, Block", () =>
        {
            var strong = With(["PommelStrike"]);
            Strength(strong, 3);
            c.Equal("Deal 12 damage. Draw 1 card.", Text(strong, strong.Hand[0]), "the text shows Strength");
            strong.Play("PommelStrike", 0);
            c.Equal(88, strong.Enemy(0).Hp, "9 + 3");

            c.Equal("Deal 9 damage. Draw 1 card.", Fight.CardText(strong, strong.Put("PommelStrike", PileType.Draw), PileType.Draw), "a copy in the draw pile shows its plain number");

            var weak = With(["PommelStrike"]);
            Weak(weak);
            c.Equal("Deal 6 damage. Draw 1 card.", Text(weak, weak.Hand[0]), "the text shows Weak: 6.75 cut to 6");
            weak.Play("PommelStrike", 0);
            c.Equal(94, weak.Enemy(0).Hp, "9 x 0.75 = 6.75, 6 HP lost");

            var vulnerable = With(["PommelStrike"]);
            Vulnerable(vulnerable, 0, 1);
            c.Equal("Deal 9 damage. Draw 1 card.", Text(vulnerable, vulnerable.Hand[0]), "the text never shows the enemy's Vulnerable");
            vulnerable.Play("PommelStrike", 0);
            c.Equal(87, vulnerable.Enemy(0).Hp, "9 x 1.5 = 13.5, 13 HP lost");

            var all = With(["PommelStrike"]);
            Strength(all, 3);
            Weak(all);
            Vulnerable(all, 0, 1);
            all.Play("PommelStrike", 0);
            c.Equal(87, all.Enemy(0).Hp, "(9 + 3) x 0.75 x 1.5 = 13.5, rounded down once");

            var blocked = With(["PommelStrike"]);
            blocked.SetBlock(blocked.Enemy(0), 5);
            blocked.Play("PommelStrike", 0);
            c.Equal(96, blocked.Enemy(0).Hp, "5 absorbed, 4 through");
            c.Equal(0, blocked.Enemy(0).Block, "the Block is gone");
        });

        c.Group("Pommel Strike: a full hand, an empty draw pile, a dead target", () =>
        {
            var full = With(["PommelStrike", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound"]);
            full.Play("PommelStrike", 0);
            c.Equal(10, full.Hand.Count, "with 10 cards in hand nothing is drawn");
            c.Equal(5, full.DrawPile.Count, "the draw pile is untouched");

            var nearly = With(["PommelStrike+", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound"]);
            nearly.Play("PommelStrike", 0);
            c.Equal(10, nearly.Hand.Count, "the upgraded card draws only the one card that fits");
            c.Equal(4, nearly.DrawPile.Count, "one card left the draw pile");

            var empty = With(["PommelStrike"]);
            empty.Clear(PileType.Draw);
            empty.Put("DefendIronclad", PileType.Discard);
            empty.Play("PommelStrike", 0);
            c.Sequence(["Defend"], empty.HandTitles(), "the discard pile is shuffled in and its card drawn");
            c.Sequence(["Pommel Strike"], Titles(empty.DiscardPile), "the card itself was in the Play pile and is not shuffled in");

            var nothing = With(["PommelStrike"]);
            nothing.Clear(PileType.Draw);
            nothing.Play("PommelStrike", 0);
            c.Equal(0, nothing.Hand.Count, "with both piles empty nothing is drawn");
            c.Equal(91, nothing.Enemy(0).Hp, "the hit still lands");

            var two = With(["PommelStrike"], Idle(5, "A"), Idle(100, "B"));
            two.Play("PommelStrike", 0);
            c.That(two.Enemy(0).IsDead, "the target dies");
            c.Equal(1, two.Hand.Count, "the draw still happens while another enemy lives");

            var last = With(["PommelStrike"], Idle(9));
            last.Play("PommelStrike", 0);
            c.That(last.IsOver && last.Won, "killing the last enemy wins the fight");
        });
    }

    private static void GiantRockAndPrimalForce(Check c)
    {
        c.Group("Giant Rock: 16 damage; Giant Rock+: 20", () =>
        {
            var f = With(["GiantRock", "GiantRock+"]);
            c.Equal("Deal 16 damage.", Text(f, f.Hand[0]), "text");
            c.Equal("Deal 20 damage.", Text(f, f.Hand[1]), "upgraded text");
            f.Play("Giant Rock", 0);
            c.Equal(84, f.Enemy(0).Hp, "16 damage");
            c.Equal(2, f.Energy, "costs 1");
            f.Play("Giant Rock+", 0);
            c.Equal(64, f.Enemy(0).Hp, "20 damage");
            c.Sequence(["Giant Rock", "Giant Rock+"], Titles(f.DiscardPile), "no Exhaust: both go to the discard pile");

            var strong = With(["GiantRock"]);
            Strength(strong, 2);
            Vulnerable(strong, 0, 1);
            c.Equal("Deal 18 damage.", Text(strong, strong.Hand[0]), "the text shows Strength");
            strong.Play("Giant Rock", 0);
            c.Equal(73, strong.Enemy(0).Hp, "(16 + 2) x 1.5 = 27");
        });

        c.Group("Primal Force: every Attack in hand becomes a Giant Rock in its place", () =>
        {
            var f = CheckKit.VsDummy();
            var primal = f.Put("PrimalForce", PileType.Hand);
            // Hand: Strike, Strike, Defend, Defend, Bash, Primal Force.
            c.Equal("Transform every Attack in your hand into a Giant Rock (cost 1: deal 16 damage).", Text(f, primal), "text");
            var before = f.Hand.Take(5).ToList();
            var log = Watch(f);
            f.Play("PrimalForce");
            c.Equal(3, f.Energy, "costs 0");
            c.Sequence(["Giant Rock", "Giant Rock", "Defend", "Defend", "Giant Rock"], f.HandTitles(), "the rocks take the places of the Attacks");
            c.That(ReferenceEquals(f.Hand[2], before[2]) && ReferenceEquals(f.Hand[3], before[3]), "the other cards are untouched");
            c.That(before[0].Pile == PileType.None && before[1].Pile == PileType.None && before[4].Pile == PileType.None, "the originals left the fight");
            c.Equal(0, f.ExhaustPile.Count, "nothing was exhausted");
            c.Sequence(["Primal Force"], Titles(f.DiscardPile), "the card itself goes to the discard pile");
            c.Equal(11, f.AllCards.Count(), "the fight holds as many cards as before");
            c.Sequence(["entered Giant Rock", "entered Giant Rock", "entered Giant Rock", "played Primal Force"], log.Seen, "each rock enters the fight as a new card; no exhaust, no discard");
            c.Equal(3, f.History.Count(e => e.Kind == HistoryKind.CardGenerated), "and is recorded as generated");
            c.Equal(1, f.CostOf(f.Hand[0]), "a rock costs 1");
            f.Play("Giant Rock", 0);
            c.Equal(84, f.Enemy(0).Hp, "and deals 16");
        });

        c.Group("Primal Force+: the rocks are upgraded", () =>
        {
            var f = With(["PrimalForce+", "StrikeIronclad", "DefendIronclad"]);
            c.Equal("Transform every Attack in your hand into a Giant Rock+ (cost 1: deal 20 damage).", Text(f, f.Hand[0]), "text");
            f.Play("PrimalForce");
            c.Equal(3, f.Energy, "still costs 0");
            c.Sequence(["Giant Rock+", "Defend"], f.HandTitles(), "an upgraded rock");
            c.Equal("Deal 20 damage.", Text(f, f.Hand[0]), "which deals 20");
            f.Play("Giant Rock+", 0);
            c.Equal(80, f.Enemy(0).Hp, "20 damage");
        });

        c.Group("Primal Force: no Attack in hand; the original's state is lost; a rock is an Attack too", () =>
        {
            var none = With(["PrimalForce", "DefendIronclad"]);
            none.Play("PrimalForce");
            c.Sequence(["Defend"], none.HandTitles(), "without an Attack nothing happens");
            c.Equal(0, none.History.Count(e => e.Kind == HistoryKind.CardGenerated), "nothing is made");

            var lost = With(["PrimalForce", "StrikeIronclad+", "StrikeIronclad"]);
            lost.Hand[2].SetCostThisCombat(0);
            c.Equal(0, lost.CostOf(lost.Hand[2]), "a Strike made free for the fight");
            lost.Play("PrimalForce");
            c.Sequence(["Giant Rock", "Giant Rock"], lost.HandTitles(), "an upgraded original gives a plain rock");
            c.That(lost.Hand.All(card => lost.CostOf(card) == 1), "and a cost change on the original is gone");

            var rock = With(["PrimalForce", "GiantRock+"]);
            var old = rock.Hand[1];
            rock.Play("PrimalForce");
            c.Sequence(["Giant Rock"], rock.HandTitles(), "the plain card turns an upgraded rock into a plain one");
            c.That(!ReferenceEquals(rock.Hand[0], old) && old.Pile == PileType.None, "it is a new card");
        });
    }

    private static void Pyre(Check c)
    {
        c.Group("Pyre: 1 more maximum energy, felt from the next turn", () =>
        {
            var f = With(["Pyre", "StrikeIronclad"]);
            c.Equal("Raise your maximum energy by 1. Energy refills to the new maximum from your next turn on.", Text(f, f.Hand[0]), "text");
            f.Play("Pyre");
            c.Equal(1, f.Energy, "costs 2; nothing is gained when it is played");
            c.Equal(4, f.MaxEnergy, "the maximum is 4 at once");
            c.Equal(4, f.Snapshot().State["max_energy"]!.GetValue<int>(), "and the snapshot says so");
            c.Equal(1, f.Player.PowerAmount<PyrePower>(), "1 Pyre");
            c.That(!f.AllCards.Any(card => card.Def.ClassName == "Pyre"), "a Power card leaves the fight");
            f.EndTurn();
            c.Equal(4, f.Energy, "4 energy on the next turn");
            f.EndTurn();
            c.Equal(4, f.Energy, "and on every turn after");
            c.Equal(1, f.Player.PowerAmount<PyrePower>(), "it never ticks down");
        });

        c.Group("Pyre+: 2 more; Pyre stacks", () =>
        {
            var f = With(["Pyre+"]);
            c.Equal("Raise your maximum energy by 2. Energy refills to the new maximum from your next turn on.", Text(f, f.Hand[0]), "text");
            f.Play("Pyre");
            c.Equal(1, f.Energy, "still costs 2");
            c.Equal(2, f.Player.PowerAmount<PyrePower>(), "2 Pyre");
            f.EndTurn();
            c.Equal(5, f.Energy, "5 energy on the next turn");

            var two = With(["Pyre", "Pyre+"]);
            two.SetEnergy(4);
            two.Play("Pyre");
            two.Play("Pyre");
            c.Equal(3, two.Player.PowerAmount<PyrePower>(), "1 + 2 on one power");
            c.Equal(6, two.MaxEnergy, "maximum 6");
            two.EndTurn();
            c.Equal(6, two.Energy, "6 energy on the next turn");
        });
    }

    private static void Rage(Check c)
    {
        c.Group("Rage: 3 Block after each Attack played this turn", () =>
        {
            var f = With(["Rage", "StrikeIronclad", "StrikeIronclad", "DefendIronclad"]);
            c.Equal("This turn, gain 3 Block whenever you play an Attack.", Text(f, f.Hand[0]), "text");
            f.Play("StrikeIronclad", 0);
            c.Equal(0, f.Player.Block, "an Attack played before Rage gives nothing");
            f.Play("Rage");
            c.Equal(2, f.Energy, "Rage costs 0");
            c.Equal(3, f.Player.PowerAmount<RagePower>(), "3 Rage");
            c.Equal(0, f.Player.Block, "Rage itself is a Skill");
            f.Play("StrikeIronclad", 0);
            c.Equal(3, f.Player.Block, "3 Block after the Strike");
            c.Equal(88, f.Enemy(0).Hp, "the Strikes hit as usual");
            f.Play("DefendIronclad");
            c.Equal(8, f.Player.Block, "a Skill adds only its own 5");
            c.Sequence(["Strike", "Rage", "Strike", "Defend"], Titles(f.DiscardPile), "Rage goes to the discard pile");
            f.EndTurn();
            c.That(!f.Player.HasPower<RagePower>(), "Rage ends with the turn");
            f.Play("StrikeIronclad", 0);
            c.Equal(0, f.Player.Block, "no Block from an Attack on the next turn");
        });

        c.Group("Rage+: 5 Block; two Rages add up", () =>
        {
            var f = With(["Rage+", "StrikeIronclad"]);
            c.Equal("This turn, gain 5 Block whenever you play an Attack.", Text(f, f.Hand[0]), "text");
            f.Play("Rage");
            c.Equal(3, f.Energy, "still costs 0");
            f.Play("StrikeIronclad", 0);
            c.Equal(5, f.Player.Block, "5 Block");

            var two = With(["Rage", "Rage+", "StrikeIronclad"]);
            two.Play("Rage");
            two.Play("Rage");
            c.Equal(8, two.Player.PowerAmount<RagePower>(), "3 + 5 on one power");
            two.Play("StrikeIronclad", 0);
            c.Equal(8, two.Player.Block, "8 Block per Attack");
        });

        c.Group("Rage: the Block is unpowered, once per run of an Attack, also for X = 0", () =>
        {
            var nimble = With(["Rage", "StrikeIronclad"]);
            Dexterity(nimble, 2);
            nimble.Play("Rage");
            nimble.Play("StrikeIronclad", 0);
            c.Equal(3, nimble.Player.Block, "Dexterity does not touch it");

            var frail = With(["Rage", "StrikeIronclad"]);
            Frail(frail);
            frail.Play("Rage");
            frail.Play("StrikeIronclad", 0);
            c.Equal(3, frail.Player.Block, "neither does Frail");

            var twin = With(["Rage", "TwinStrike"]);
            twin.Play("Rage");
            twin.Play("TwinStrike", 0);
            c.Equal(3, twin.Player.Block, "one gain for a card with two hits");
            c.Equal(1, twin.History.Count(e => e.Kind == HistoryKind.BlockGained), "one Block gain in the history");

            var zero = With(["Rage", "Whirlwind"]);
            zero.Play("Rage");
            zero.SetEnergy(0);
            zero.Play("Whirlwind");
            c.Equal(100, zero.Enemy(0).Hp, "Whirlwind for X = 0 hits nothing");
            c.Equal(3, zero.Player.Block, "and still counts as an Attack played");

            var echo = With(["Rage", "StrikeIronclad"]);
            echo.Play("Rage");
            echo.ApplyPower<Echo>(echo.Player, 1, echo.Player);
            echo.Play("StrikeIronclad", 0);
            c.Equal(88, echo.Enemy(0).Hp, "a Strike played twice by an effect");
            c.Equal(6, echo.Player.Block, "gives the Block for each run");
        });
    }

    private static void Rampage(Check c)
    {
        c.Group("Rampage: 9 damage, then 5 more on this copy for the rest of the fight", () =>
        {
            var f = With(["Rampage"]);
            var card = f.Hand[0];
            c.Equal("Deal 9 damage. This card's damage goes up by 5 for the rest of combat.", Text(f, card), "text");
            f.Play("Rampage", 0);
            c.Equal(91, f.Enemy(0).Hp, "9 damage");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(14m, card.Get("Damage"), "its damage is 14 now");
            c.Equal(PileType.Discard, card.Pile, "it goes to the discard pile");
            Again(f, card);
            c.Equal("Deal 14 damage. This card's damage goes up by 5 for the rest of combat.", Text(f, card), "the text follows");
            f.Play("Rampage", 0);
            c.Equal(77, f.Enemy(0).Hp, "14 damage");
            Again(f, card);
            f.Play("Rampage", 0);
            c.Equal(58, f.Enemy(0).Hp, "19 damage");
            c.Equal(24m, card.Get("Damage"), "then 24");

            var other = f.Put("Rampage", PileType.Hand);
            c.Equal(9m, other.Get("Damage"), "another copy is not changed");
        });

        c.Group("Rampage+: still 9 damage, then 9 more each time", () =>
        {
            var f = With(["Rampage+"]);
            var card = f.Hand[0];
            c.Equal("Deal 9 damage. This card's damage goes up by 9 for the rest of combat.", Text(f, card), "text");
            f.Play("Rampage", 0);
            c.Equal(91, f.Enemy(0).Hp, "9 damage: the upgrade does not change the first hit");
            Again(f, card);
            f.Play("Rampage", 0);
            c.Equal(73, f.Enemy(0).Hp, "18 damage");
            Again(f, card);
            f.Play("Rampage", 0);
            c.Equal(46, f.Enemy(0).Hp, "27 damage");
            c.Equal(36m, card.Get("Damage"), "then 36");
        });

        c.Group("Rampage: the rise does not depend on the hit; Strength is not part of it", () =>
        {
            var blocked = With(["Rampage"]);
            blocked.SetBlock(blocked.Enemy(0), 20);
            blocked.Play("Rampage", 0);
            c.Equal(100, blocked.Enemy(0).Hp, "a fully blocked hit");
            c.Equal(11, blocked.Enemy(0).Block, "9 Block gone");
            c.Equal(14m, blocked.DiscardPile[0].Get("Damage"), "still raises the damage");

            var dead = With(["Rampage"], Idle(5, "A"), Idle(100, "B"));
            dead.Play("Rampage", 0);
            c.That(dead.Enemy(0).IsDead, "the target dies");
            c.Equal(14m, dead.DiscardPile[0].Get("Damage"), "and the damage is raised");

            var strong = With(["Rampage"]);
            var card = strong.Hand[0];
            Strength(strong, 2);
            c.Equal("Deal 11 damage. This card's damage goes up by 5 for the rest of combat.", Text(strong, card), "the text shows Strength");
            strong.Play("Rampage", 0);
            c.Equal(89, strong.Enemy(0).Hp, "9 + 2");
            c.Equal(14m, card.Get("Damage"), "the card's own number rises by 5 only");
            Again(strong, card);
            c.Equal("Deal 16 damage. This card's damage goes up by 5 for the rest of combat.", Text(strong, card), "14 + 2 in the text");

            var grown = With(["Rampage"]);
            var late = grown.Hand[0];
            grown.Play("Rampage", 0);
            grown.Upgrade(late);
            c.Equal("Rampage+", late.Title, "upgraded in the middle of the fight");
            c.Equal(14m, late.Get("Damage"), "it keeps what it gained");
            c.Equal(9m, late.Get("Increase"), "and rises by 9 from now on");
        });
    }

    private static void Rupture(Check c)
    {
        c.Group("Rupture: 1 Strength per HP loss on your turn; a card's own loss pays after the card", () =>
        {
            var f = With(["Rupture"]);
            f.Put(Bleed, PileType.Hand);
            f.Put(Bleed, PileType.Hand);
            c.Equal("Whenever you lose HP during your turn, gain 1 Strength.", Text(f, f.Hand[0]), "text");
            f.Play("Rupture");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(1, f.Player.PowerAmount<RupturePower>(), "1 Rupture");
            c.That(!f.AllCards.Any(card => card.Def.ClassName == "Rupture"), "a Power card leaves the fight");
            f.Play("Bleed", 0);
            c.Equal(78, f.Player.Hp, "the card took 2 HP");
            c.Equal(95, f.Enemy(0).Hp, "its own hit came before the Strength: 5");
            c.Equal(1, f.Player.PowerAmount<StrengthPower>(), "1 Strength once the card is done");
            f.Play("Bleed", 0);
            c.Equal(89, f.Enemy(0).Hp, "the next one hits for 6");
            c.Equal(2, f.Player.PowerAmount<StrengthPower>(), "2 Strength");
        });

        c.Group("Rupture+: 2 Strength; Rupture stacks", () =>
        {
            var f = With(["Rupture+"]);
            f.Put(Bleed, PileType.Hand);
            c.Equal("Whenever you lose HP during your turn, gain 2 Strength.", Text(f, f.Hand[0]), "text");
            f.Play("Rupture");
            c.Equal(2, f.Energy, "still costs 1");
            c.Equal(2, f.Player.PowerAmount<RupturePower>(), "2 Rupture");
            f.Play("Bleed", 0);
            c.Equal(95, f.Enemy(0).Hp, "5 damage");
            c.Equal(2, f.Player.PowerAmount<StrengthPower>(), "2 Strength");

            var two = With(["Rupture", "Rupture+"]);
            two.Play("Rupture");
            two.Play("Rupture");
            c.Equal(3, two.Player.PowerAmount<RupturePower>(), "1 + 2 on one power");
            c.That(!two.Player.HasPower<StrengthPower>(), "playing a Rupture with one already up gives no Strength");
        });

        c.Group("Rupture: once per damage event, only for HP really lost, only on your own turn", () =>
        {
            var twice = With(["Rupture"]);
            twice.Put(Gash, PileType.Hand);
            twice.Play("Rupture");
            twice.Play("Gash", 0);
            c.Equal(78, twice.Player.Hp, "two losses of 1");
            c.Equal(95, twice.Enemy(0).Hp, "the card's hit has no Strength yet");
            c.Equal(2, twice.Player.PowerAmount<StrengthPower>(), "1 Strength per loss, paid after the card");

            var blocked = With(["Rupture"]);
            blocked.Put(Scratch, PileType.Hand);
            blocked.Put(Scratch, PileType.Hand);
            blocked.Play("Rupture");
            blocked.SetBlock(blocked.Player, 4);
            blocked.Play("Scratch");
            c.Equal(80, blocked.Player.Hp, "4 damage into 4 Block");
            c.That(!blocked.Player.HasPower<StrengthPower>(), "no HP lost, no Strength");
            blocked.SetBlock(blocked.Player, 3);
            blocked.Play("Scratch");
            c.Equal(79, blocked.Player.Hp, "1 of 4 gets through");
            c.Equal(1, blocked.Player.PowerAmount<StrengthPower>(), "1 Strength");

            var burn = With(["Rupture", "Burn"]);
            burn.Play("Rupture");
            burn.EndTurn();
            c.Equal(78, burn.Player.Hp, "a Burn in hand takes 2 HP at the end of the turn");
            c.Equal(1, burn.Player.PowerAmount<StrengthPower>(), "that is still your turn: 1 Strength");

            var guarded = With(["Rupture", "Burn", "DefendIronclad"]);
            guarded.Play("Rupture");
            guarded.Play("DefendIronclad");
            guarded.EndTurn();
            c.Equal(80, guarded.Player.Hp, "the Burn is absorbed by Block");
            c.That(!guarded.Player.HasPower<StrengthPower>(), "no Strength");

            var enemy = CheckKit.VsDummy(Hitter(7));
            enemy.Clear(PileType.Hand);
            enemy.Put("Rupture", PileType.Hand);
            enemy.Play("Rupture");
            enemy.EndTurn();
            c.Equal(73, enemy.Player.Hp, "7 HP lost in the enemy turn");
            c.That(!enemy.Player.HasPower<StrengthPower>(), "gives nothing");

            var bare = With(["Rupture"]);
            bare.Play("Rupture");
            LoseHp(bare, 3);
            c.Equal(1, bare.Player.PowerAmount<StrengthPower>(), "HP lost with no card behind it pays at once");
        });

        c.Group("Rupture: a card that hurts and then asks; a copy of the fight in the middle of it", () =>
        {
            var f = With(["Rupture", "StrikeIronclad", "DefendIronclad"]);
            f.Put(BleedPick, PileType.Hand);
            f.Play("Rupture");
            f.Play("Bleed Pick");
            c.Equal("hand_select", f.Snapshot().Screen, "the card is waiting for an answer");
            c.Equal(78, f.Player.Hp, "2 HP are lost already");
            c.That(!f.Player.HasPower<StrengthPower>(), "the Strength is owed, not paid");
            var copy = f.Clone();
            f.Do("select:0", "confirm");
            copy.Do("select:1", "confirm");
            c.Equal(1, f.Player.PowerAmount<StrengthPower>(), "1 Strength when the card is done");
            c.Sequence(["Strike"], Titles(f.ExhaustPile), "the chosen card is exhausted");
            c.Equal(78, f.Player.Hp, "the HP was lost once");
            c.Equal(1, copy.Player.PowerAmount<StrengthPower>(), "the copy pays the same");
            c.Sequence(["Defend"], Titles(copy.ExhaustPile), "and goes its own way");
        });
    }

    private static void SecondWind(Check c)
    {
        c.Group("Second Wind: exhaust every non-Attack in hand, 5 Block for each", () =>
        {
            var f = CheckKit.VsDummy();
            var card = f.Put("SecondWind", PileType.Hand);
            // Hand: Strike, Strike, Defend, Defend, Bash, Second Wind.
            c.Equal("Exhaust every card in your hand that is not an Attack. Gain 5 Block for each card exhausted.", Text(f, card), "text");
            var log = Watch(f);
            f.Play("SecondWind");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(10, f.Player.Block, "two cards, 10 Block");
            c.Sequence(["Strike", "Strike", "Bash"], f.HandTitles(), "the Attacks stay");
            c.Sequence(["Defend", "Defend"], Titles(f.ExhaustPile), "the Defends are exhausted");
            c.Sequence(["Second Wind"], Titles(f.DiscardPile), "the card itself is not");
            c.Sequence(["exhausted Defend", "block 5", "exhausted Defend", "block 5", "played Second Wind"], log.Seen, "exhaust, then Block, card by card");
        });

        c.Group("Second Wind+: 7 Block for each", () =>
        {
            var f = With(["SecondWind+", "DefendIronclad", "DefendIronclad", "StrikeIronclad"]);
            c.Equal("Exhaust every card in your hand that is not an Attack. Gain 7 Block for each card exhausted.", Text(f, f.Hand[0]), "text");
            f.Play("SecondWind");
            c.Equal(2, f.Energy, "still costs 1");
            c.Equal(14, f.Player.Block, "two cards, 14 Block");
            c.Sequence(["Strike"], f.HandTitles(), "the Attack stays");
        });

        c.Group("Second Wind: statuses and Powers count; each gain is its own; cards that arrive stay", () =>
        {
            var mixed = With(["SecondWind", "Wound", "Dazed", "Pyre", "StrikeIronclad"]);
            mixed.Play("SecondWind");
            c.Equal(15, mixed.Player.Block, "three cards that are not Attacks");
            c.Sequence(["Wound", "Dazed", "Pyre"], Titles(mixed.ExhaustPile), "exhausted in hand order");
            c.Sequence(["Strike"], mixed.HandTitles(), "the Attack stays");

            var none = With(["SecondWind", "StrikeIronclad"]);
            none.Play("SecondWind");
            c.Equal(0, none.Player.Block, "only Attacks in hand: no Block");
            c.Equal(0, none.ExhaustPile.Count, "nothing exhausted");

            var nimble = With(["SecondWind", "DefendIronclad", "DefendIronclad"]);
            Dexterity(nimble, 2);
            c.Equal("Exhaust every card in your hand that is not an Attack. Gain 7 Block for each card exhausted.", Text(nimble, nimble.Hand[0]), "the text shows Dexterity");
            nimble.Play("SecondWind");
            c.Equal(14, nimble.Player.Block, "Dexterity on each gain: 7 + 7");

            var frail = With(["SecondWind", "DefendIronclad", "DefendIronclad"]);
            Frail(frail);
            frail.Play("SecondWind");
            c.Equal(6, frail.Player.Block, "Frail on each gain: 3.75 is cut to 3 twice, not 7.5 to 7");

            var drawing = With(["SecondWind", "DefendIronclad"]);
            drawing.Put("DefendIronclad", PileType.Draw, PilePosition.Top);
            drawing.ApplyPower<DrawOnExhaust>(drawing.Player, 1, drawing.Player);
            drawing.Play("SecondWind");
            c.Sequence(["Defend"], drawing.HandTitles(), "a Skill drawn during the effect is not exhausted");
            c.Equal(1, drawing.ExhaustPile.Count, "one card exhausted");
            c.Equal(5, drawing.Player.Block, "one gain");
        });
    }

    private static void SetupStrike(Check c)
    {
        c.Group("Setup Strike: 7 damage, then 2 Strength for this turn", () =>
        {
            var f = With(["SetupStrike", "StrikeIronclad"]);
            var log = Watch(f);
            c.Equal("Deal 7 damage. Gain 2 Strength until the end of your turn.", Text(f, f.Hand[0]), "text");
            f.Play("SetupStrike", 0);
            c.Equal(93, f.Enemy(0).Hp, "7 damage: the Strength comes after the hit");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(2, f.Player.PowerAmount<StrengthPower>(), "2 Strength");
            c.Equal(2, f.Player.PowerAmount<SetupStrikePower>(), "2 Setup Strike");
            c.Sequence(["hit 7", "power StrengthPower 2", "power SetupStrikePower 2", "played Setup Strike"], log.Seen, "hit first, then the Strength");
            c.Equal("Deal 8 damage.", Text(f, f.Hand[0]), "a Strike in hand shows it");
            f.Play("StrikeIronclad", 0);
            c.Equal(85, f.Enemy(0).Hp, "6 + 2");
            f.EndTurn();
            c.That(!f.Player.HasPower<StrengthPower>() && !f.Player.HasPower<SetupStrikePower>(), "both are gone after the turn");
        });

        c.Group("Setup Strike+: 9 damage, 3 Strength", () =>
        {
            var f = With(["SetupStrike+", "StrikeIronclad"]);
            c.Equal("Deal 9 damage. Gain 3 Strength until the end of your turn.", Text(f, f.Hand[0]), "text");
            f.Play("SetupStrike", 0);
            c.Equal(91, f.Enemy(0).Hp, "9 damage");
            c.Equal(3, f.Player.PowerAmount<StrengthPower>(), "3 Strength");
            f.Play("StrikeIronclad", 0);
            c.Equal(82, f.Enemy(0).Hp, "6 + 3");
        });

        c.Group("Setup Strike: two in one turn; Strength that was there before stays", () =>
        {
            var two = With(["SetupStrike", "SetupStrike"]);
            two.Play("SetupStrike", 0);
            two.Play("SetupStrike", 0);
            c.Equal(84, two.Enemy(0).Hp, "7, then 7 + 2");
            c.Equal(4, two.Player.PowerAmount<StrengthPower>(), "4 Strength");
            c.Equal(4, two.Player.PowerAmount<SetupStrikePower>(), "on one power of 4");
            two.EndTurn();
            c.That(!two.Player.HasPower<StrengthPower>(), "all of it is taken back");

            var kept = With(["SetupStrike"]);
            Strength(kept, 3);
            kept.Play("SetupStrike", 0);
            c.Equal(90, kept.Enemy(0).Hp, "7 + 3");
            c.Equal(5, kept.Player.PowerAmount<StrengthPower>(), "5 Strength this turn");
            kept.EndTurn();
            c.Equal(3, kept.Player.PowerAmount<StrengthPower>(), "3 again afterwards");
        });
    }

    private static void ShrugItOff(Check c)
    {
        c.Group("Shrug It Off: 8 Block, then draw 1", () =>
        {
            var f = With(["ShrugItOff"]);
            var log = Watch(f);
            c.Equal("Gain 8 Block. Draw 1 card.", Text(f, f.Hand[0]), "text");
            f.Play("ShrugItOff");
            c.Equal(8, f.Player.Block, "8 Block");
            c.Equal(2, f.Energy, "costs 1");
            c.Sequence(["Strike"], f.HandTitles(), "one card drawn");
            c.Sequence(["block 8", "drawn Strike", "played Shrug It Off"], log.Seen, "Block first, then the draw");
        });

        c.Group("Shrug It Off+: 11 Block, still 1 card", () =>
        {
            var f = With(["ShrugItOff+"]);
            c.Equal("Gain 11 Block. Draw 1 card.", Text(f, f.Hand[0]), "text");
            f.Play("ShrugItOff");
            c.Equal(11, f.Player.Block, "11 Block");
            c.Sequence(["Strike"], f.HandTitles(), "one card drawn");
        });

        c.Group("Shrug It Off: Dexterity, Frail, a full hand", () =>
        {
            var nimble = With(["ShrugItOff"]);
            Dexterity(nimble, 2);
            c.Equal("Gain 10 Block. Draw 1 card.", Text(nimble, nimble.Hand[0]), "the text shows Dexterity");
            nimble.Play("ShrugItOff");
            c.Equal(10, nimble.Player.Block, "8 + 2");

            var frail = With(["ShrugItOff"]);
            Frail(frail);
            frail.Play("ShrugItOff");
            c.Equal(6, frail.Player.Block, "8 x 0.75");

            var both = With(["ShrugItOff"]);
            Dexterity(both, 2);
            Frail(both);
            c.Equal("Gain 7 Block. Draw 1 card.", Text(both, both.Hand[0]), "the text shows both");
            both.Play("ShrugItOff");
            c.Equal(7, both.Player.Block, "(8 + 2) x 0.75 = 7.5");

            var full = With(["ShrugItOff", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound"]);
            full.Play("ShrugItOff");
            c.Equal(8, full.Player.Block, "with a full hand the Block still comes");
            c.Equal(5, full.DrawPile.Count, "and nothing is drawn");
        });
    }

    private static void Spite(Check c)
    {
        c.Group("Spite: 5 damage once, twice after losing HP this turn", () =>
        {
            var f = With(["Spite"]);
            c.Equal("Deal 5 damage. If you lost HP this turn, deal it 2 times instead.", Text(f, f.Hand[0]), "text");
            f.Play("Spite", 0);
            c.Equal(95, f.Enemy(0).Hp, "one hit");
            c.Equal(3, f.Energy, "costs 0");

            var hurt = With(["Spite"]);
            LoseHp(hurt, 1);
            hurt.Play("Spite", 0);
            c.Equal(90, hurt.Enemy(0).Hp, "two hits");
            c.Equal(2, EventsOn(hurt, hurt.Enemy(0)), "as two damage events");
        });

        c.Group("Spite+: three hits after losing HP this turn", () =>
        {
            var f = With(["Spite+"]);
            c.Equal("Deal 5 damage. If you lost HP this turn, deal it 3 times instead.", Text(f, f.Hand[0]), "text");
            f.Play("Spite", 0);
            c.Equal(95, f.Enemy(0).Hp, "one hit of 5 without a loss");

            var hurt = With(["Spite+"]);
            LoseHp(hurt, 1);
            hurt.Play("Spite", 0);
            c.Equal(85, hurt.Enemy(0).Hp, "three hits of 5");
        });

        c.Group("Spite: what counts as HP lost this turn", () =>
        {
            var enemyTurn = CheckKit.VsDummy(Hitter(7));
            enemyTurn.EndTurn();
            c.Equal(73, enemyTurn.Player.Hp, "7 HP lost in the enemy turn");
            enemyTurn.Clear(PileType.Hand);
            enemyTurn.Put("Spite", PileType.Hand);
            enemyTurn.Play("Spite", 0);
            c.Equal(95, enemyTurn.Enemy(0).Hp, "does not count on the next turn: one hit");

            var blocked = With(["Spite"]);
            blocked.SetBlock(blocked.Player, 5);
            blocked.Damage(blocked.Player, 3, Props.NonCardUnpowered, blocked.Player);
            c.Equal(80, blocked.Player.Hp, "damage that Block absorbed");
            blocked.Play("Spite", 0);
            c.Equal(95, blocked.Enemy(0).Hp, "does not count: one hit");

            var card = With(["Spite"]);
            card.Put(Bleed, PileType.Hand);
            card.Play("Bleed", 0);
            card.Play("Spite", 0);
            c.Equal(85, card.Enemy(0).Hp, "HP lost to a card this turn counts: 5 + 5 + 5");

            var strong = With(["Spite"]);
            Strength(strong, 2);
            LoseHp(strong, 1);
            c.Equal("Deal 7 damage. If you lost HP this turn, deal it 2 times instead.", Text(strong, strong.Hand[0]), "the text shows Strength");
            strong.Play("Spite", 0);
            c.Equal(86, strong.Enemy(0).Hp, "Strength on each hit: 7 + 7");

            var dead = With(["Spite"], Idle(5, "A"), Idle(100, "B"));
            LoseHp(dead, 1);
            dead.Play("Spite", 0);
            c.That(dead.Enemy(0).IsDead, "the first hit kills the target");
            c.Equal(100, dead.Enemy(1).Hp, "the second hit is lost, not moved to another enemy");
        });
    }

    private static void Stampede(Check c)
    {
        c.Group("Stampede: at the end of the turn a random Attack from the hand is played for free", () =>
        {
            var f = With(["Stampede", "StrikeIronclad", "DefendIronclad"]);
            c.Equal("At the end of your turn, play 1 random Attack from your hand for free.", Text(f, f.Hand[0]), "text");
            f.Play("Stampede");
            c.Equal(1, f.Energy, "costs 2");
            c.Equal(1, f.Player.PowerAmount<StampedePower>(), "1 Stampede");
            c.That(!f.AllCards.Any(card => card.Def.ClassName == "Stampede"), "a Power card leaves the fight");
            c.Equal(0, f.Rng.Shuffle.Counter, "no random number used so far");
            f.EndTurn();
            c.Equal(94, f.Enemy(0).Hp, "the Strike was played");
            c.Equal(1, f.Rng.Shuffle.Counter, "the pick is one Shuffle draw, also with one candidate");
            c.Equal(1, f.Rng.CombatTargets.Counter, "the target is one CombatTargets draw, also with one enemy");
            c.Sequence(["Strike", "Defend"], Titles(f.DiscardPile), "the Strike went to the discard pile before the hand did");
            c.Equal(1, f.CardsPlayed, "only the card played by hand counts as played by the player");
        });

        c.Group("Stampede+: costs 1", () =>
        {
            var f = With(["Stampede+", "StrikeIronclad"]);
            c.Equal("At the end of your turn, play 1 random Attack from your hand for free.", Text(f, f.Hand[0]), "text");
            f.Play("Stampede");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(1, f.Player.PowerAmount<StampedePower>(), "1 Stampede");
            f.EndTurn();
            c.Equal(94, f.Enemy(0).Hp, "the Strike was played");
        });

        c.Group("Stampede: which card, how often, and what it ignores", () =>
        {
            var none = With(["Stampede", "DefendIronclad"]);
            none.Play("Stampede");
            none.EndTurn();
            c.Equal(100, none.Enemy(0).Hp, "no Attack in hand: nothing is played");
            c.Equal(0, none.Rng.Shuffle.Counter, "and no random number is used");

            var dud = With(["Stampede"]);
            dud.Put(Dud, PileType.Hand);
            dud.Play("Stampede");
            dud.EndTurn();
            c.Equal(0, dud.Rng.Shuffle.Counter, "an Unplayable Attack is not a candidate");

            var poor = With(["Stampede", "Bash"]);
            poor.Play("Stampede");
            c.Equal(1, poor.Energy, "1 energy left, Bash costs 2");
            poor.EndTurn();
            c.Equal(92, poor.Enemy(0).Hp, "energy is not looked at: Bash is played");

            var x = With(["Stampede", "Whirlwind"]);
            x.Play("Stampede");
            x.EndTurn();
            c.Equal(95, x.Enemy(0).Hp, "an X card uses the energy left as X: one hit of 5");

            var pick = With(["Stampede", "StrikeIronclad", "Bash", "TwinStrike"]);
            pick.Play("Stampede");
            var attacks = pick.Hand.ToList();
            var index = RngSet.For(1, "shuffle").NextInt(0, 3);
            pick.EndTurn();
            c.That(ReferenceEquals(pick.DiscardPile[0], attacks[index]), $"the Shuffle stream picks among the three Attacks (here number {index})");
            c.Equal(100 - new[] { 6, 8, 10 }[index], pick.Enemy(0).Hp, "and only that one is played");

            var two = With(["Stampede", "Stampede", "StrikeIronclad", "StrikeIronclad", "DefendIronclad"]);
            two.SetEnergy(4);
            two.Play("Stampede");
            two.Play("Stampede");
            c.Equal(2, two.Player.PowerAmount<StampedePower>(), "two stacks");
            two.EndTurn();
            c.Equal(88, two.Enemy(0).Hp, "two Attacks are played");
            c.Equal(2, two.Rng.Shuffle.Counter, "one draw per pick");

            var spare = With(["Stampede", "Stampede", "StrikeIronclad"]);
            spare.SetEnergy(4);
            spare.Play("Stampede");
            spare.Play("Stampede");
            spare.EndTurn();
            c.Equal(94, spare.Enemy(0).Hp, "two stacks, one Attack");
            c.Equal(1, spare.Rng.Shuffle.Counter, "the second pick finds nothing and draws nothing");

            var aim = With(["Stampede", "StrikeIronclad"], Idle(100, "A"), Idle(100, "B"));
            aim.Play("Stampede");
            var target = RngSet.For(1, "combat_targets").NextInt(0, 2);
            aim.EndTurn();
            c.Equal(94, aim.Enemy(target).Hp, $"the CombatTargets stream picks the enemy (here e{target})");
            c.Equal(100, aim.Enemy(1 - target).Hp, "the other is not hit");

            var kill = With(["Stampede", "StrikeIronclad"], Idle(6));
            kill.Play("Stampede");
            kill.EndTurn();
            c.That(kill.IsOver && kill.Won, "a kill by the auto-played card wins the fight");
        });

        c.Group("Stampede: the auto-played Attack is a real play, at the very start of the turn's end", () =>
        {
            var f = CheckKit.VsDummy(Hitter(7));
            f.Clear(PileType.Hand);
            f.Put("Rage", PileType.Hand);
            f.Put("Stampede", PileType.Hand);
            f.Put("StrikeIronclad", PileType.Hand);
            f.Play("Rage");
            f.Play("Stampede");
            f.EndTurn();
            c.Equal(94, f.Enemy(0).Hp, "the Strike was played");
            c.Equal(76, f.Player.Hp, "Rage was still up and gave 3 Block against the 7");
            c.That(!f.Player.HasPower<RagePower>(), "Rage ended afterwards");
        });
    }

    private static void Stoke(Check c)
    {
        c.Group("Stoke: exhaust the hand, one random pool card for each card", () =>
        {
            var f = CheckKit.VsDummy(null, 7);
            var stoke = f.Put("Stoke", PileType.Hand);
            // Hand: Strike, Strike, Defend, Defend, Bash, Stoke.
            c.Equal("Exhaust your hand. Add a random card to your hand for each card that was in it.", Text(f, stoke), "text");
            var pool = Content.Registry.IroncladPool.Where(d => d.IsGeneratableInCombat).ToList();
            var stream = RngSet.For(7, "combat_card_generation");
            var expected = Enumerable.Range(0, 5).Select(_ => pool[stream.NextInt(0, pool.Count)].ClassName).ToList();
            f.Play("Stoke");
            c.Equal(2, f.Energy, "costs 1");
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash"], Titles(f.ExhaustPile), "the hand is exhausted in hand order");
            c.Sequence(expected, f.Hand.Select(card => card.Def.ClassName), "five uniform picks from the pool, in order, on the CombatCardGeneration stream");
            c.Equal(5, f.Rng.CombatCardGeneration.Counter, "one draw per card");
            c.That(f.Hand.All(card => !card.IsUpgraded), "none is upgraded");
            c.That(f.Hand.All(card => card.BaseCost == card.Def.Cost), "they cost what they normally cost");
            c.Sequence(["Stoke"], Titles(f.DiscardPile), "Stoke itself goes to the discard pile");
            c.Equal(5, f.History.Count(e => e.Kind == HistoryKind.CardGenerated), "five cards recorded as generated");
        });

        c.Group("Stoke+: the new cards are upgraded", () =>
        {
            var f = CheckKit.VsDummy(null, 7);
            var stoke = f.Put("Stoke+", PileType.Hand);
            c.Equal("Exhaust your hand. Add a random upgraded card to your hand for each card that was in it.", Text(f, stoke), "text");
            var pool = Content.Registry.IroncladPool.Where(d => d.IsGeneratableInCombat).ToList();
            var stream = RngSet.For(7, "combat_card_generation");
            var expected = Enumerable.Range(0, 5).Select(_ => pool[stream.NextInt(0, pool.Count)].ClassName).ToList();
            f.Play("Stoke");
            c.Equal(2, f.Energy, "still costs 1");
            c.Sequence(expected, f.Hand.Select(card => card.Def.ClassName), "the same picks");
            c.That(f.Hand.All(card => card.IsUpgraded), "every one upgraded");
        });

        c.Group("Stoke: an empty hand; a hand that fills up", () =>
        {
            var empty = With(["Stoke"]);
            empty.Play("Stoke");
            c.Equal(0, empty.Hand.Count, "nothing to exhaust, nothing made");
            c.Equal(0, empty.Rng.CombatCardGeneration.Counter, "and no random number used");

            // Nine cards to exhaust; each exhaust draws a card while the draw pile has one (five).
            var full = With(["Stoke", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound"]);
            full.ApplyPower<DrawOnExhaust>(full.Player, 1, full.Player);
            var pool = Content.Registry.IroncladPool.Where(d => d.IsGeneratableInCombat).ToList();
            var stream = RngSet.For(1, "combat_card_generation");
            var expected = Enumerable.Range(0, 9).Select(_ => pool[stream.NextInt(0, pool.Count)].ClassName).ToList();
            full.Play("Stoke");
            c.Equal(9, full.ExhaustPile.Count, "nine cards exhausted");
            c.Equal(9, full.Rng.CombatCardGeneration.Counter, "nine cards made, however many fit");
            c.Sequence(
                new[] { "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad" }.Concat(expected.Take(5)),
                full.Hand.Select(card => card.Def.ClassName), "the cards drawn meanwhile stay; the first five new cards fill the hand to 10");
            c.Sequence(expected.Skip(5).Append("Stoke"), full.DiscardPile.Select(card => card.Def.ClassName), "the other four go to the discard pile, before Stoke itself");
        });
    }

    private static void Stomp(Check c)
    {
        c.Group("Stomp: 12 damage to every enemy for 3", () =>
        {
            var f = With(["Stomp"], Idle(100, "A"), Idle(100, "B"));
            var stomp = f.Hand[0];
            c.Equal("Deal 12 damage to ALL enemies. Costs 1 less this turn for each Attack played this turn.", Text(f, stomp), "text");
            c.Equal(3, f.CostOf(stomp), "costs 3");
            c.Sequence(["play:0", "end_turn"], Ids(f), "played without a target");
            f.Play("Stomp");
            c.Equal(88, f.Enemy(0).Hp, "12 to the first");
            c.Equal(88, f.Enemy(1).Hp, "12 to the second");
            c.Equal(0, f.Energy, "3 energy spent");
            c.Equal(2, f.CostOf(stomp), "its own play counts too: 2 for the rest of the turn");
        });

        c.Group("Stomp+: 15 damage", () =>
        {
            var f = With(["Stomp+"], Idle(100, "A"), Idle(100, "B"));
            c.Equal("Deal 15 damage to ALL enemies. Costs 1 less this turn for each Attack played this turn.", Text(f, f.Hand[0]), "text");
            c.Equal(3, f.CostOf(f.Hand[0]), "still costs 3");
            f.Play("Stomp");
            c.Equal(85, f.Enemy(0).Hp, "15 to the first");
            c.Equal(85, f.Enemy(1).Hp, "15 to the second");
        });

        c.Group("Stomp: 1 less for each Attack played this turn, never below 0, full price next turn", () =>
        {
            var f = With(["Stomp", "StrikeIronclad", "StrikeIronclad", "DefendIronclad"]);
            var stomp = f.Hand[0];
            f.SetEnergy(4);
            f.Play("DefendIronclad");
            c.Equal(3, f.CostOf(stomp), "a Skill changes nothing");
            f.Play("StrikeIronclad", 0);
            c.Equal(2, f.CostOf(stomp), "one Attack: 2");
            f.Play("StrikeIronclad", 0);
            c.Equal(1, f.CostOf(stomp), "two Attacks: 1");
            c.Equal("1", f.CostText(stomp), "the snapshot shows the cost of the moment");
            c.Equal(1, f.Energy, "1 energy left");
            f.Play("Stomp");
            c.Equal(76, f.Enemy(0).Hp, "6 + 6 + 12");
            c.Equal(0, f.Energy, "it cost 1");
            f.EndTurn();
            c.Equal(3, f.CostOf(stomp), "the reductions end with the turn");

            var floor = With(["Stomp", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad"]);
            floor.SetEnergy(4);
            for (var i = 0; i < 4; i++) floor.Play("StrikeIronclad", 0);
            c.Equal(0, floor.CostOf(floor.Hand[0]), "four Attacks: 0, not below");
            floor.Play("Stomp");
            c.Equal(64, floor.Enemy(0).Hp, "playable with no energy: 24 + 12");

            var vulnerable = With(["Stomp"], Idle(100, "A"), Idle(100, "B"));
            Vulnerable(vulnerable, 0, 1);
            Strength(vulnerable, 2);
            vulnerable.Play("Stomp");
            c.Equal(79, vulnerable.Enemy(0).Hp, "Vulnerable is per target: (12 + 2) x 1.5 = 21");
            c.Equal(86, vulnerable.Enemy(1).Hp, "the other takes 14");
        });

        c.Group("Stomp: it counts from every pile; one made mid-turn; a copy; an Attack played twice", () =>
        {
            var f = With(["StrikeIronclad", "StrikeIronclad"]);
            var inDraw = f.Put("Stomp", PileType.Draw, PilePosition.Top);
            var inDiscard = f.Put("Stomp", PileType.Discard);
            var inExhaust = f.Put("Stomp", PileType.Exhaust);
            f.Play("StrikeIronclad", 0);
            f.Play("StrikeIronclad", 0);
            c.Equal(1, f.CostOf(inDraw), "in the draw pile: 1 after two Attacks");
            c.Equal(1, f.CostOf(inDiscard), "in the discard pile too");
            c.Equal(1, f.CostOf(inExhaust), "and in the exhaust pile");

            var made = f.CreateCard("Stomp");
            f.AddGeneratedCard(made, PileType.Hand);
            c.Equal(1, f.CostOf(made), "one made now starts 2 lower: two Attacks were already finished this turn");

            var copy = f.CloneCard(inDraw);
            f.AddGeneratedCard(copy, PileType.Hand);
            c.Equal(1, f.CostOf(copy), "a copy carries the original's reductions and gets none of its own for arriving");

            var echo = With(["Stomp", "StrikeIronclad"]);
            echo.ApplyPower<Echo>(echo.Player, 1, echo.Player);
            echo.Play("StrikeIronclad", 0);
            c.Equal(1, echo.CostOf(echo.Hand[0]), "an Attack that runs twice counts twice");
        });
    }

    private static void StoneArmor(Check c)
    {
        c.Group("Stone Armor: 4 Plating, giving 4, 3, 2 and 1 Block at the end of four turns", () =>
        {
            var f = CheckKit.VsDummy(Hitter(7));
            f.Clear(PileType.Hand);
            f.Put("StoneArmor", PileType.Hand);
            c.Equal("Gain 4 Plating: at the end of your turn gain that much Block, and Plating drops by 1 at the start of your turn.", Text(f, f.Hand[0]), "text");
            f.Play("StoneArmor");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(4, f.Player.PowerAmount<PlatingPower>(), "4 Plating");
            c.Equal(0, f.Player.Block, "no Block when it is played");
            c.That(!f.AllCards.Any(card => card.Def.ClassName == "StoneArmor"), "a Power card leaves the fight");
            f.EndTurn();
            c.Equal(77, f.Player.Hp, "4 Block against 7");
            c.Equal(3, f.Player.PowerAmount<PlatingPower>(), "3 Plating on turn 2: the hit did not lower it, the new turn did");
            f.EndTurn();
            c.Equal(73, f.Player.Hp, "3 Block against 7");
            f.EndTurn();
            c.Equal(68, f.Player.Hp, "2 Block against 7");
            c.Equal(1, f.Player.PowerAmount<PlatingPower>(), "1 Plating on turn 4");
            f.EndTurn();
            c.Equal(62, f.Player.Hp, "1 Block against 7");
            c.That(!f.Player.HasPower<PlatingPower>(), "gone on turn 5");
            f.EndTurn();
            c.Equal(55, f.Player.Hp, "no Block any more");
        });

        c.Group("Stone Armor+: 6 Plating", () =>
        {
            var f = CheckKit.VsDummy(Hitter(7));
            f.Clear(PileType.Hand);
            f.Put("StoneArmor+", PileType.Hand);
            c.Equal("Gain 6 Plating: at the end of your turn gain that much Block, and Plating drops by 1 at the start of your turn.", Text(f, f.Hand[0]), "text");
            f.Play("StoneArmor");
            c.Equal(2, f.Energy, "still costs 1");
            c.Equal(6, f.Player.PowerAmount<PlatingPower>(), "6 Plating");
            f.EndTurn();
            c.Equal(79, f.Player.Hp, "6 Block against 7");
            c.Equal(5, f.Player.PowerAmount<PlatingPower>(), "5 Plating on turn 2");
        });

        c.Group("Stone Armor: the Block is unpowered and comes before a Burn; Plating stacks", () =>
        {
            var frail = CheckKit.VsDummy(Hitter(7));
            frail.Clear(PileType.Hand);
            frail.Put("StoneArmor", PileType.Hand);
            Frail(frail);
            frail.Play("StoneArmor");
            frail.EndTurn();
            c.Equal(77, frail.Player.Hp, "Frail changes nothing: 4 Block");

            var nimble = CheckKit.VsDummy(Hitter(7));
            nimble.Clear(PileType.Hand);
            nimble.Put("StoneArmor", PileType.Hand);
            Dexterity(nimble, 2);
            nimble.Play("StoneArmor");
            nimble.EndTurn();
            c.Equal(77, nimble.Player.Hp, "neither does Dexterity");

            var burn = With(["StoneArmor", "Burn"]);
            burn.Play("StoneArmor");
            burn.EndTurn();
            c.Equal(80, burn.Player.Hp, "the Block is there before the Burn's 2 damage");

            var two = With(["StoneArmor", "StoneArmor+"]);
            two.Play("StoneArmor");
            two.Play("StoneArmor");
            c.Equal(10, two.Player.PowerAmount<PlatingPower>(), "4 + 6 on one power");
        });
    }

    private static void SwordBoomerang(Check c)
    {
        c.Group("Sword Boomerang: 3 damage to a random enemy, 3 times", () =>
        {
            var f = With(["SwordBoomerang"]);
            c.Equal("Deal 3 damage to a random enemy 3 times.", Text(f, f.Hand[0]), "text");
            c.Sequence(["play:0", "end_turn"], Ids(f), "played without a target");
            f.Play("SwordBoomerang");
            c.Equal(91, f.Enemy(0).Hp, "9 damage in all");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(3, f.Rng.CombatTargets.Counter, "one CombatTargets draw per hit, also with one enemy");
            c.Equal(3, EventsOn(f, f.Enemy(0)), "three damage events");
        });

        c.Group("Sword Boomerang+: 4 times", () =>
        {
            var f = With(["SwordBoomerang+"]);
            c.Equal("Deal 3 damage to a random enemy 4 times.", Text(f, f.Hand[0]), "text");
            f.Play("SwordBoomerang");
            c.Equal(88, f.Enemy(0).Hp, "12 damage in all");
            c.Equal(4, f.Rng.CombatTargets.Counter, "four draws");
        });

        c.Group("Sword Boomerang: modifiers per hit; several enemies; an enemy that dies", () =>
        {
            var strong = With(["SwordBoomerang"]);
            Strength(strong, 2);
            c.Equal("Deal 5 damage to a random enemy 3 times.", Text(strong, strong.Hand[0]), "the text shows Strength");
            strong.Play("SwordBoomerang");
            c.Equal(85, strong.Enemy(0).Hp, "Strength on each hit: 3 x 5");

            var weak = With(["SwordBoomerang"]);
            Weak(weak);
            weak.Play("SwordBoomerang");
            c.Equal(94, weak.Enemy(0).Hp, "Weak: 2.25 per hit, 2 HP each");

            var vulnerable = With(["SwordBoomerang"]);
            Vulnerable(vulnerable, 0, 1);
            vulnerable.Play("SwordBoomerang");
            c.Equal(88, vulnerable.Enemy(0).Hp, "Vulnerable: 4.5 per hit, 4 HP each");

            var two = With(["SwordBoomerang"], Idle(100, "A"), Idle(100, "B"));
            var stream = RngSet.For(1, "combat_targets");
            var hits = new int[2];
            for (var i = 0; i < 3; i++) hits[stream.NextInt(0, 2)]++;
            two.Play("SwordBoomerang");
            c.Equal(100 - 3 * hits[0], two.Enemy(0).Hp, $"the stream sends {hits[0]} hits to the first enemy");
            c.Equal(100 - 3 * hits[1], two.Enemy(1).Hp, $"and {hits[1]} to the second");
            c.Equal(3, two.Rng.CombatTargets.Counter, "three draws");

            // The first enemy dies from one hit; later hits are rolled among who is left.
            var dying = With(["SwordBoomerang+"], Idle(3, "A"), Idle(100, "B"));
            stream = RngSet.For(1, "combat_targets");
            var alive = new List<int> { 0, 1 };
            var onSecond = 0;
            for (var i = 0; i < 4; i++)
            {
                var target = alive[stream.NextInt(0, alive.Count)];
                if (target == 0) alive.Remove(0);
                else onSecond++;
            }
            dying.Play("SwordBoomerang");
            c.Equal(alive.Contains(0), dying.Enemy(0).IsAlive, "the first enemy dies if a hit finds it");
            c.Equal(100 - 3 * onSecond, dying.Enemy(1).Hp, "every other hit lands on the second");
            c.Equal(4, dying.Rng.CombatTargets.Counter, "four draws, also once only one enemy is left");

            var last = With(["SwordBoomerang"], Idle(3));
            last.Play("SwordBoomerang");
            c.That(last.IsOver && last.Won, "the hits stop when nobody is left");
            c.Equal(1, last.Rng.CombatTargets.Counter, "and no further number is drawn");
        });
    }

    private static void Taunt(Check c)
    {
        c.Group("Taunt: 7 Block, then 1 Vulnerable on the chosen enemy", () =>
        {
            var f = With(["Taunt", "StrikeIronclad"]);
            var log = Watch(f);
            c.Equal("Gain 7 Block. Apply 1 Vulnerable.", Text(f, f.Hand[0]), "text");
            c.Sequence(["play:0>e0", "play:1>e0", "end_turn"], Ids(f), "a Skill that needs a target");
            f.Play("Taunt", 0);
            c.Equal(7, f.Player.Block, "7 Block");
            c.Equal(1, f.Enemy(0).PowerAmount<VulnerablePower>(), "1 Vulnerable");
            c.Equal(2, f.Energy, "costs 1");
            c.Sequence(["block 7", "power VulnerablePower 1", "played Taunt"], log.Seen, "Block first, then Vulnerable");
            f.Play("StrikeIronclad", 0);
            c.Equal(91, f.Enemy(0).Hp, "a Strike into Vulnerable: 9");
            f.EndTurn();
            c.That(!f.Enemy(0).HasPower<VulnerablePower>(), "1 Vulnerable covers only the turn it was applied in");
        });

        c.Group("Taunt+: 8 Block, 2 Vulnerable", () =>
        {
            var f = With(["Taunt+"]);
            c.Equal("Gain 8 Block. Apply 2 Vulnerable.", Text(f, f.Hand[0]), "text");
            f.Play("Taunt", 0);
            c.Equal(8, f.Player.Block, "8 Block");
            c.Equal(2, f.Enemy(0).PowerAmount<VulnerablePower>(), "2 Vulnerable");
            c.Equal(2, f.Energy, "still costs 1");
            f.EndTurn();
            c.Equal(1, f.Enemy(0).PowerAmount<VulnerablePower>(), "1 left on the next turn");
        });

        c.Group("Taunt: Artifact, Dexterity, Frail", () =>
        {
            var warded = With(["Taunt"], Warded());
            warded.Play("Taunt", 0);
            c.That(!warded.Enemy(0).HasPower<VulnerablePower>(), "Artifact stops the Vulnerable");
            c.That(!warded.Enemy(0).HasPower<ArtifactPower>(), "and is used up");
            c.Equal(7, warded.Player.Block, "the Block is gained all the same");

            var frail = With(["Taunt"]);
            Frail(frail);
            Dexterity(frail, 2);
            c.Equal("Gain 6 Block. Apply 1 Vulnerable.", Text(frail, frail.Hand[0]), "the text shows Dexterity and Frail");
            frail.Play("Taunt", 0);
            c.Equal(6, frail.Player.Block, "(7 + 2) x 0.75 = 6.75");
        });
    }

    private static void TearAsunder(Check c)
    {
        c.Group("Tear Asunder: 5 damage once when no HP was lost in the fight", () =>
        {
            var f = With(["TearAsunder"]);
            var card = f.Hand[0];
            c.Equal("Deal 5 damage once, plus once more for each time you lost HP this combat (now 1 hit).", Text(f, card), "text in a fight");
            c.Equal("Deal 5 damage once, plus once more for each time you lost HP this combat.", Fight.CardText(null, card, PileType.Deck), "text outside a fight");
            f.Play("TearAsunder", 0);
            c.Equal(95, f.Enemy(0).Hp, "one hit of 5");
            c.Equal(1, f.Energy, "costs 2");
        });

        c.Group("Tear Asunder+: 7 damage per hit", () =>
        {
            var f = With(["TearAsunder+"]);
            c.Equal("Deal 7 damage once, plus once more for each time you lost HP this combat (now 1 hit).", Text(f, f.Hand[0]), "text");
            f.Play("TearAsunder", 0);
            c.Equal(93, f.Enemy(0).Hp, "one hit of 7");
            c.Equal(1, f.Energy, "still costs 2");

            var hurt = With(["TearAsunder+"]);
            LoseHp(hurt, 1);
            LoseHp(hurt, 1);
            hurt.Play("TearAsunder", 0);
            c.Equal(79, hurt.Enemy(0).Hp, "three hits of 7 after two losses");
        });

        c.Group("Tear Asunder: one more hit for every time HP was lost in the fight", () =>
        {
            var f = CheckKit.VsDummy(Hitter(3, 2));
            f.EndTurn();
            c.Equal(74, f.Player.Hp, "an enemy attack of 3 x 2 got through twice");
            f.Clear(PileType.Hand);
            var card = f.Put("TearAsunder", PileType.Hand);
            c.Equal("Deal 5 damage once, plus once more for each time you lost HP this combat (now 3 hits).", Text(f, card), "each hit that got through counts, on a later turn too");
            f.Play("TearAsunder", 0);
            c.Equal(85, f.Enemy(0).Hp, "three hits of 5");
            c.Equal(3, EventsOn(f, f.Enemy(0)), "as three damage events");

            var blocked = CheckKit.VsDummy(Hitter(3, 2));
            blocked.SetBlock(blocked.Player, 4);
            blocked.EndTurn();
            c.Equal(78, blocked.Player.Hp, "with 4 Block the first hit is absorbed, the second costs 2 HP");
            blocked.Clear(PileType.Hand);
            blocked.Put("TearAsunder", PileType.Hand);
            blocked.Play("TearAsunder", 0);
            c.Equal(90, blocked.Enemy(0).Hp, "a fully blocked hit does not count: two hits");

            var self = With(["TearAsunder"]);
            self.Put(Bleed, PileType.Hand);
            self.Play("Bleed", 0);
            LoseHp(self, 1);
            Strength(self, 2);
            c.Equal("Deal 7 damage once, plus once more for each time you lost HP this combat (now 3 hits).", Text(self, self.Hand[0]), "the text shows Strength and the count");
            self.Play("TearAsunder", 0);
            c.Equal(74, self.Enemy(0).Hp, "HP lost to a card and without one both count: 5, then 3 x 7");

            var dead = With(["TearAsunder"], Idle(6, "A"), Idle(100, "B"));
            LoseHp(dead, 1);
            LoseHp(dead, 1);
            dead.Play("TearAsunder", 0);
            c.That(dead.Enemy(0).IsDead, "the target dies on the second of three hits");
            c.Equal(100, dead.Enemy(1).Hp, "the third is lost");
        });
    }

    private static void Thrash(Check c)
    {
        c.Group("Thrash: 4 damage twice, then it takes the damage of a random Attack in hand and exhausts that card", () =>
        {
            var f = With(["Thrash", "StrikeIronclad", "DefendIronclad"]);
            var thrash = f.Hand[0];
            var log = Watch(f);
            c.Equal("Deal 4 damage twice. Exhaust a random Attack in your hand and add its damage to this card's damage for the rest of combat.", Text(f, thrash), "text");
            f.Play("Thrash", 0);
            c.Equal(92, f.Enemy(0).Hp, "4 + 4");
            c.Equal(2, f.Energy, "costs 1");
            c.Sequence(["Strike"], Titles(f.ExhaustPile), "the only Attack in hand is exhausted");
            c.Sequence(["Defend"], f.HandTitles(), "the Skill stays");
            c.Equal(1, f.Rng.CombatCardSelection.Counter, "one CombatCardSelection draw, also with one candidate");
            c.Equal(10m, thrash.Get("Damage"), "4 + the Strike's 6");
            c.Sequence(["hit 4", "hit 4", "exhausted Strike", "played Thrash"], log.Seen, "both hits first, then the exhaust");
            Again(f, thrash);
            c.Equal("Deal 10 damage twice. Exhaust a random Attack in your hand and add its damage to this card's damage for the rest of combat.", Text(f, thrash), "the text follows");
            f.Play("Thrash", 0);
            c.Equal(72, f.Enemy(0).Hp, "10 + 10");
            c.Equal(1, f.Rng.CombatCardSelection.Counter, "no Attack in hand: no draw");
            c.Equal(10m, thrash.Get("Damage"), "and nothing is added");
            c.Equal(1, f.ExhaustPile.Count, "nothing more is exhausted");
        });

        c.Group("Thrash+: 6 damage twice", () =>
        {
            var f = With(["Thrash+", "StrikeIronclad"]);
            var thrash = f.Hand[0];
            c.Equal("Deal 6 damage twice. Exhaust a random Attack in your hand and add its damage to this card's damage for the rest of combat.", Text(f, thrash), "text");
            f.Play("Thrash", 0);
            c.Equal(88, f.Enemy(0).Hp, "6 + 6");
            c.Equal(2, f.Energy, "still costs 1");
            c.Equal(12m, thrash.Get("Damage"), "6 + the Strike's 6");
        });

        c.Group("Thrash: the number it takes has the player's Strength and Weak in it, never the enemy's Vulnerable", () =>
        {
            var strong = With(["Thrash", "StrikeIronclad"]);
            var thrash = strong.Hand[0];
            Strength(strong, 2);
            strong.Play("Thrash", 0);
            c.Equal(88, strong.Enemy(0).Hp, "(4 + 2) twice");
            c.Equal(12m, thrash.Get("Damage"), "4 + (6 + 2): the Strength is baked in");
            Again(strong, thrash);
            c.Equal("Deal 14 damage twice. Exhaust a random Attack in your hand and add its damage to this card's damage for the rest of combat.", Text(strong, thrash), "and counts again on every later hit");
            strong.Play("Thrash", 0);
            c.Equal(60, strong.Enemy(0).Hp, "(12 + 2) twice");

            var weak = With(["Thrash", "StrikeIronclad"]);
            thrash = weak.Hand[0];
            Weak(weak);
            weak.Play("Thrash", 0);
            c.Equal(94, weak.Enemy(0).Hp, "4 x 0.75 = 3, twice");
            c.Equal(8.5m, thrash.Get("Damage"), "4 + 6 x 0.75: the fraction is kept");
            Again(weak, thrash);
            c.Equal("Deal 6 damage twice. Exhaust a random Attack in your hand and add its damage to this card's damage for the rest of combat.", Text(weak, thrash), "8.5 x 0.75 = 6.375 while Weak");
            weak.RemovePower<WeakPower>(weak.Player);
            c.Equal("Deal 8 damage twice. Exhaust a random Attack in your hand and add its damage to this card's damage for the rest of combat.", Text(weak, thrash), "8.5 shows as 8");
            weak.Play("Thrash", 0);
            c.Equal(78, weak.Enemy(0).Hp, "each hit of 8.5 takes 8 HP");

            var vulnerable = With(["Thrash", "StrikeIronclad"]);
            thrash = vulnerable.Hand[0];
            Vulnerable(vulnerable, 0, 2);
            vulnerable.Play("Thrash", 0);
            c.Equal(88, vulnerable.Enemy(0).Hp, "4 x 1.5 = 6, twice");
            c.Equal(10m, thrash.Get("Damage"), "the Strike counts as 6, not 9");
        });

        c.Group("Thrash: which number of the other card it reads", () =>
        {
            var twin = With(["Thrash", "TwinStrike"]);
            twin.Play("Thrash", 0);
            c.Equal(9m, twin.DiscardPile[0].Get("Damage"), "Twin Strike gives its per-hit 5, not 10");

            var tear = With(["Thrash", "TearAsunder"]);
            LoseHp(tear, 1);
            LoseHp(tear, 1);
            tear.Play("Thrash", 0);
            c.Equal(9m, tear.DiscardPile[0].Get("Damage"), "Tear Asunder gives its 5, whatever its hit count");

            var grown = With(["Rampage", "Thrash"]);
            var rampage = grown.Hand[0];
            grown.Play("Rampage", 0);
            Again(grown, rampage);
            grown.Play("Thrash", 0);
            c.Equal(83, grown.Enemy(0).Hp, "9 from Rampage, 4 + 4 from Thrash");
            c.Equal(18m, grown.DiscardPile[0].Get("Damage"), "a Rampage that grew to 14 gives 14");
            c.Sequence(["Rampage"], Titles(grown.ExhaustPile), "and is exhausted");

            var calculated = With(["Thrash"]);
            calculated.Put(Slam, PileType.Hand);
            calculated.Play("Thrash", 0);
            c.Equal(15m, calculated.DiscardPile[0].Get("Damage"), "a calculated number is worked out with no target: 3 + 2 x 4 = 11");

            var numberless = With(["Thrash"]);
            numberless.Put(Dud, PileType.Hand);
            Strength(numberless, 2);
            numberless.Play("Thrash", 0);
            c.Equal(6m, numberless.DiscardPile[0].Get("Damage"), "an Attack with no damage number counts as 0 before Strength");
            c.Sequence(["Dud"], Titles(numberless.ExhaustPile), "and is exhausted like any other");
        });

        c.Group("Thrash: the pick among several Attacks; no Attack; a target that dies", () =>
        {
            var f = With(["Thrash", "StrikeIronclad", "Bash", "TwinStrike"]);
            var thrash = f.Hand[0];
            var candidates = f.Hand.Skip(1).ToList();
            var index = RngSet.For(1, "combat_card_selection").NextInt(0, 3);
            f.Play("Thrash", 0);
            c.That(ReferenceEquals(f.ExhaustPile[0], candidates[index]), $"the CombatCardSelection stream picks among the three (here number {index})");
            c.Equal(4m + new[] { 6m, 8m, 5m }[index], thrash.Get("Damage"), "and that card's number is added");
            c.Equal(2, f.Hand.Count, "the other two stay");

            var none = With(["Thrash", "DefendIronclad"]);
            none.Play("Thrash", 0);
            c.Equal(0, none.Rng.CombatCardSelection.Counter, "no Attack in hand: no draw");
            c.Equal(4m, none.DiscardPile[0].Get("Damage"), "nothing added");
            c.Equal(0, none.ExhaustPile.Count, "nothing exhausted");

            var dead = With(["Thrash", "StrikeIronclad"], Idle(4, "A"), Idle(100, "B"));
            var a = dead.Enemy(0);
            dead.Play("Thrash", 0);
            c.That(a.IsDead, "the first hit kills the target");
            c.Equal(1, EventsOn(dead, a), "the second hit is lost");
            c.Equal(100, dead.Enemy(1).Hp, "not moved to the other enemy");
            c.Sequence(["Strike"], Titles(dead.ExhaustPile), "the rest of the card still happens");
            c.Equal(10m, dead.DiscardPile[0].Get("Damage"), "the damage is still added");
        });
    }

    private static void Thunderclap(Check c)
    {
        c.Group("Thunderclap: 4 damage to every enemy, then 1 Vulnerable to each", () =>
        {
            var f = With(["Thunderclap", "StrikeIronclad"], Idle(100, "A"), Idle(100, "B"));
            var log = Watch(f);
            c.Equal("Deal 4 damage to ALL enemies. Apply 1 Vulnerable to ALL enemies.", Text(f, f.Hand[0]), "text");
            f.Play("Thunderclap");
            c.Equal(96, f.Enemy(0).Hp, "4 to the first");
            c.Equal(96, f.Enemy(1).Hp, "4 to the second");
            c.Equal(1, f.Enemy(0).PowerAmount<VulnerablePower>(), "1 Vulnerable on the first");
            c.Equal(1, f.Enemy(1).PowerAmount<VulnerablePower>(), "1 Vulnerable on the second");
            c.Equal(2, f.Energy, "costs 1");
            c.Sequence(["hit 4", "hit 4", "power VulnerablePower 1", "power VulnerablePower 1", "played Thunderclap"], log.Seen, "all the damage first, then one application per enemy");
            f.Play("StrikeIronclad", 1);
            c.Equal(87, f.Enemy(1).Hp, "a Strike into Vulnerable: 9");
        });

        c.Group("Thunderclap+: 7 damage, still 1 Vulnerable", () =>
        {
            var f = With(["Thunderclap+"], Idle(100, "A"), Idle(100, "B"));
            c.Equal("Deal 7 damage to ALL enemies. Apply 1 Vulnerable to ALL enemies.", Text(f, f.Hand[0]), "text");
            f.Play("Thunderclap");
            c.Equal(93, f.Enemy(0).Hp, "7 to the first");
            c.Equal(93, f.Enemy(1).Hp, "7 to the second");
            c.Equal(1, f.Enemy(0).PowerAmount<VulnerablePower>(), "1 Vulnerable");
            c.Equal(2, f.Energy, "still costs 1");
        });

        c.Group("Thunderclap: Vulnerable after the hit, per enemy, not on the dead; Vicious draws per enemy", () =>
        {
            var already = With(["Thunderclap"], Idle(100, "A"), Idle(100, "B"));
            Vulnerable(already, 0, 1);
            Strength(already, 2);
            already.Play("Thunderclap");
            c.Equal(91, already.Enemy(0).Hp, "Vulnerable that was there before counts: (4 + 2) x 1.5 = 9");
            c.Equal(94, already.Enemy(1).Hp, "the card's own Vulnerable does not: 6");
            c.Equal(2, already.Enemy(0).PowerAmount<VulnerablePower>(), "the stack grows to 2");

            var dead = With(["Thunderclap"], Idle(4, "A"), Idle(100, "B"));
            dead.Play("Thunderclap");
            c.That(dead.Enemy(0).IsDead, "an enemy killed by the hit");
            c.Equal(1, dead.Enemy(1).PowerAmount<VulnerablePower>(), "does not stop the other from getting Vulnerable");

            var warded = With(["Thunderclap"], Warded("A"), Idle(100, "B"));
            warded.ApplyPower<ViciousPower>(warded.Player, 1, warded.Player);
            warded.Play("Thunderclap");
            c.That(!warded.Enemy(0).HasPower<VulnerablePower>() && !warded.Enemy(0).HasPower<ArtifactPower>(), "Artifact stops it on that enemy only");
            c.Equal(1, warded.Enemy(1).PowerAmount<VulnerablePower>(), "the other gets it");
            c.Sequence(["Strike"], warded.HandTitles(), "Vicious draws for the one application that landed");

            var vicious = With(["Thunderclap"], Idle(100, "A"), Idle(100, "B"));
            vicious.ApplyPower<ViciousPower>(vicious.Player, 1, vicious.Player);
            vicious.Play("Thunderclap");
            c.Sequence(["Strike", "Strike"], vicious.HandTitles(), "Vicious draws once per enemy");
        });
    }

    private static void Tremble(Check c)
    {
        c.Group("Tremble: 3 Vulnerable, Exhaust", () =>
        {
            var f = With(["Tremble", "StrikeIronclad"]);
            c.Equal("Apply 3 Vulnerable. / Exhaust.", Text(f, f.Hand[0]), "text");
            c.Sequence(["play:0>e0", "play:1>e0", "end_turn"], Ids(f), "a Skill that needs a target");
            f.Play("Tremble", 0);
            c.Equal(3, f.Enemy(0).PowerAmount<VulnerablePower>(), "3 Vulnerable");
            c.Equal(2, f.Energy, "costs 1");
            c.Sequence(["Tremble"], Titles(f.ExhaustPile), "it is exhausted");
            c.Equal(0, f.DiscardPile.Count, "not discarded");
            f.Play("StrikeIronclad", 0);
            c.Equal(91, f.Enemy(0).Hp, "a Strike into Vulnerable: 9");
            f.EndTurn();
            c.Equal(2, f.Enemy(0).PowerAmount<VulnerablePower>(), "2 on turn 2");
            f.EndTurn();
            c.Equal(1, f.Enemy(0).PowerAmount<VulnerablePower>(), "1 on turn 3");
            f.EndTurn();
            c.That(!f.Enemy(0).HasPower<VulnerablePower>(), "gone on turn 4");
        });

        c.Group("Tremble+: 4 Vulnerable", () =>
        {
            var f = With(["Tremble+"]);
            c.Equal("Apply 4 Vulnerable. / Exhaust.", Text(f, f.Hand[0]), "text");
            f.Play("Tremble", 0);
            c.Equal(4, f.Enemy(0).PowerAmount<VulnerablePower>(), "4 Vulnerable");
            c.Equal(2, f.Energy, "still costs 1");
            c.Sequence(["Tremble+"], Titles(f.ExhaustPile), "it is exhausted");
        });

        c.Group("Tremble: Artifact; adding to a stack", () =>
        {
            var warded = With(["Tremble"], Warded());
            warded.Play("Tremble", 0);
            c.That(!warded.Enemy(0).HasPower<VulnerablePower>() && !warded.Enemy(0).HasPower<ArtifactPower>(), "Artifact stops it and is used up");
            c.Sequence(["Tremble"], Titles(warded.ExhaustPile), "the card is exhausted all the same");

            var stack = With(["Bash", "Tremble"]);
            stack.Play("Bash", 0);
            stack.Play("Tremble", 0);
            c.Equal(5, stack.Enemy(0).PowerAmount<VulnerablePower>(), "2 + 3 on one stack");
        });
    }

    private static void TrueGrit(Check c)
    {
        c.Group("True Grit: 7 Block, then a random card of the hand is exhausted", () =>
        {
            var f = With(["TrueGrit", "StrikeIronclad"]);
            var log = Watch(f);
            c.Equal("Gain 7 Block. Exhaust a random card in your hand.", Text(f, f.Hand[0]), "text");
            f.Play("TrueGrit");
            c.Equal(7, f.Player.Block, "7 Block");
            c.Equal(2, f.Energy, "costs 1");
            c.Sequence(["Strike"], Titles(f.ExhaustPile), "the only other card is exhausted");
            c.Equal(1, f.Rng.CombatCardSelection.Counter, "one CombatCardSelection draw, also with one candidate");
            c.Sequence(["True Grit"], Titles(f.DiscardPile), "the card itself goes to the discard pile");
            c.Sequence(["block 7", "exhausted Strike", "played True Grit"], log.Seen, "Block first, then the exhaust");

            var several = With(["TrueGrit", "StrikeIronclad", "DefendIronclad", "Bash"]);
            var candidates = several.Hand.Skip(1).ToList();
            var index = RngSet.For(1, "combat_card_selection").NextInt(0, 3);
            several.Play("TrueGrit");
            c.That(ReferenceEquals(several.ExhaustPile[0], candidates[index]), $"the stream picks among all cards in hand (here number {index})");
            c.Equal(2, several.Hand.Count, "the other two stay");

            var status = With(["TrueGrit", "Wound"]);
            status.Play("TrueGrit");
            c.Sequence(["Wound"], Titles(status.ExhaustPile), "a status card can be the one");

            var empty = With(["TrueGrit"]);
            empty.Play("TrueGrit");
            c.Equal(7, empty.Player.Block, "with an empty hand the Block still comes");
            c.Equal(0, empty.Rng.CombatCardSelection.Counter, "and no number is drawn");
        });

        c.Group("True Grit+: 9 Block, then the player picks the card to exhaust", () =>
        {
            var f = With(["TrueGrit+", "StrikeIronclad", "DefendIronclad", "Bash"]);
            c.Equal("Gain 9 Block. Exhaust a card in your hand.", Text(f, f.Hand[0]), "text");
            f.Play("TrueGrit");
            var s = f.Snapshot();
            c.Equal("hand_select", s.Screen, "the choice is a hand selection");
            c.Equal(9, f.Player.Block, "the Block came before the question");
            c.Sequence(["select:0", "select:1", "select:2"], s.Actions.Select(a => a.Id), "any card of the hand; it cannot be skipped");
            c.Equal(1, s.State["selection"]!["min"]!.GetValue<int>(), "exactly one");
            c.Equal(1, s.State["selection"]!["max"]!.GetValue<int>(), "exactly one");
            f.Do("select:1");
            c.Sequence(["confirm"], Ids(f), "then confirm");
            f.Do("confirm");
            c.Equal("combat", f.Snapshot().Screen, "back in the fight");
            c.Sequence(["Defend"], Titles(f.ExhaustPile), "the chosen card is exhausted");
            c.Sequence(["Strike", "Bash"], f.HandTitles(), "the others stay");
            c.Equal(9, f.Player.Block, "the Block was gained once");
            c.Equal(2, f.Energy, "costs 1, paid once");
            c.Sequence(["True Grit+"], Titles(f.DiscardPile), "the card itself goes to the discard pile");
            c.Equal(0, f.Rng.CombatCardSelection.Counter, "nothing random about it");
        });

        c.Group("True Grit+: one card is taken without asking; an empty hand; Dexterity and Frail", () =>
        {
            var one = With(["TrueGrit+", "Wound"]);
            one.Play("TrueGrit");
            c.Equal("combat", one.Snapshot().Screen, "with one card in hand nothing is asked");
            c.Sequence(["Wound"], Titles(one.ExhaustPile), "it is exhausted");

            var empty = With(["TrueGrit+"]);
            empty.Play("TrueGrit");
            c.Equal("combat", empty.Snapshot().Screen, "with an empty hand nothing is asked");
            c.Equal(9, empty.Player.Block, "the Block still comes");
            c.Equal(0, empty.ExhaustPile.Count, "nothing is exhausted");

            var frail = With(["TrueGrit"]);
            Dexterity(frail, 2);
            Frail(frail);
            c.Equal("Gain 6 Block. Exhaust a random card in your hand.", Text(frail, frail.Hand[0]), "the text shows Dexterity and Frail");
            frail.Play("TrueGrit");
            c.Equal(6, frail.Player.Block, "(7 + 2) x 0.75 = 6.75");
        });
    }

    private static void TwinStrike(Check c)
    {
        c.Group("Twin Strike: 5 damage twice", () =>
        {
            var f = With(["TwinStrike"]);
            c.Equal("Deal 5 damage twice.", Text(f, f.Hand[0]), "text");
            f.Play("TwinStrike", 0);
            c.Equal(90, f.Enemy(0).Hp, "5 + 5");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(2, EventsOn(f, f.Enemy(0)), "two damage events");
        });

        c.Group("Twin Strike+: 7 damage twice", () =>
        {
            var f = With(["TwinStrike+"]);
            c.Equal("Deal 7 damage twice.", Text(f, f.Hand[0]), "text");
            f.Play("TwinStrike", 0);
            c.Equal(86, f.Enemy(0).Hp, "7 + 7");
            c.Equal(2, f.Energy, "still costs 1");
        });

        c.Group("Twin Strike: modifiers and Block per hit; a target that dies", () =>
        {
            var strong = With(["TwinStrike"]);
            Strength(strong, 2);
            c.Equal("Deal 7 damage twice.", Text(strong, strong.Hand[0]), "the text shows Strength");
            strong.Play("TwinStrike", 0);
            c.Equal(86, strong.Enemy(0).Hp, "Strength on each hit: 7 + 7");

            var weak = With(["TwinStrike"]);
            Weak(weak);
            c.Equal("Deal 3 damage twice.", Text(weak, weak.Hand[0]), "the text shows Weak");
            weak.Play("TwinStrike", 0);
            c.Equal(94, weak.Enemy(0).Hp, "3.75 per hit is 3 HP each, not 7.5 in all");

            var vulnerable = With(["TwinStrike"]);
            Vulnerable(vulnerable, 0, 2);
            vulnerable.Play("TwinStrike", 0);
            c.Equal(86, vulnerable.Enemy(0).Hp, "7.5 per hit is 7 HP each");

            var blocked = With(["TwinStrike"]);
            blocked.SetBlock(blocked.Enemy(0), 7);
            blocked.Play("TwinStrike", 0);
            c.Equal(97, blocked.Enemy(0).Hp, "7 Block takes the first hit and 2 of the second");
            c.Equal(0, blocked.Enemy(0).Block, "and is gone");

            var dead = With(["TwinStrike"], Idle(5, "A"), Idle(100, "B"));
            var a = dead.Enemy(0);
            dead.Play("TwinStrike", 0);
            c.That(a.IsDead, "the first hit kills the target");
            c.Equal(1, EventsOn(dead, a), "the second hit is lost");
            c.Equal(100, dead.Enemy(1).Hp, "not moved to the other enemy");
        });
    }

    private static void Unmovable(Check c)
    {
        c.Group("Unmovable: the first card that gives Block each turn gives double", () =>
        {
            var f = With(["Unmovable", "DefendIronclad", "DefendIronclad"]);
            f.SetEnergy(4);
            c.Equal("Each turn, the Block from the first card that gives you Block is doubled.", Text(f, f.Hand[0]), "text");
            c.Equal("Gain 5 Block.", Text(f, f.Hand[1]), "a Defend before");
            f.Play("Unmovable");
            c.Equal(2, f.Energy, "costs 2");
            c.Equal(1, f.Player.PowerAmount<UnmovablePower>(), "1 Unmovable");
            c.That(!f.AllCards.Any(card => card.Def.ClassName == "Unmovable"), "a Power card leaves the fight");
            c.Equal("Gain 10 Block.", Text(f, f.Hand[0]), "a Defend in hand shows the doubled number");
            f.Play("DefendIronclad");
            c.Equal(10, f.Player.Block, "5 x 2");
            c.Equal("Gain 5 Block.", Text(f, f.Hand[0]), "the next Defend shows 5 again");
            f.Play("DefendIronclad");
            c.Equal(15, f.Player.Block, "the second card is not doubled");
            f.EndTurn();
            c.Equal("Gain 10 Block.", Text(f, f.Hand[f.HandIndex("Defend")]), "doubled again on the next turn");
            f.Play("DefendIronclad");
            c.Equal(10, f.Player.Block, "5 x 2 on turn 2");
        });

        c.Group("Unmovable+: costs 1", () =>
        {
            var f = With(["Unmovable+", "DefendIronclad"]);
            c.Equal("Each turn, the Block from the first card that gives you Block is doubled.", Text(f, f.Hand[0]), "text");
            f.Play("Unmovable");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(1, f.Player.PowerAmount<UnmovablePower>(), "1 Unmovable");
            f.Play("DefendIronclad");
            c.Equal(10, f.Player.Block, "5 x 2");
        });

        c.Group("Unmovable: with Dexterity and Frail; what uses it up and what does not", () =>
        {
            var nimble = With(["DefendIronclad"]);
            nimble.ApplyPower<UnmovablePower>(nimble.Player, 1, nimble.Player);
            Dexterity(nimble, 2);
            nimble.Play("DefendIronclad");
            c.Equal(14, nimble.Player.Block, "(5 + 2) x 2");

            var frail = With(["DefendIronclad"]);
            frail.ApplyPower<UnmovablePower>(frail.Player, 1, frail.Player);
            Frail(frail);
            c.Equal("Gain 7 Block.", Text(frail, frail.Hand[0]), "the text shows 7");
            frail.Play("DefendIronclad");
            c.Equal(7, frail.Player.Block, "5 x 0.75 x 2 = 7.5, rounded down once");

            var early = With(["DefendIronclad", "Unmovable", "DefendIronclad"]);
            early.SetEnergy(4);
            early.Play("DefendIronclad");
            early.Play("Unmovable");
            early.Play("DefendIronclad");
            c.Equal(10, early.Player.Block, "a Block card played earlier the same turn already used it: 5 + 5");

            var rage = With(["Rage", "StrikeIronclad", "DefendIronclad"]);
            rage.ApplyPower<UnmovablePower>(rage.Player, 1, rage.Player);
            rage.Play("Rage");
            rage.Play("StrikeIronclad", 0);
            c.Equal(3, rage.Player.Block, "Rage's Block is not doubled");
            rage.Play("DefendIronclad");
            c.Equal(13, rage.Player.Block, "and does not use it up: 3 + 10");

            var zero = With(["DefendIronclad", "DefendIronclad"]);
            zero.ApplyPower<UnmovablePower>(zero.Player, 1, zero.Player);
            Dexterity(zero, -5);
            zero.Play("DefendIronclad");
            c.Equal(0, zero.Player.Block, "a card that gives no Block in the end");
            zero.RemovePower<DexterityPower>(zero.Player);
            zero.Play("DefendIronclad");
            c.Equal(10, zero.Player.Block, "does not use it up");
        });

        c.Group("Unmovable: every gain of the first card is doubled; with 2 stacks it counts gains, not cards", () =>
        {
            var wind = With(["SecondWind", "DefendIronclad", "DefendIronclad"]);
            wind.ApplyPower<UnmovablePower>(wind.Player, 1, wind.Player);
            wind.Play("SecondWind");
            c.Equal(20, wind.Player.Block, "both gains of Second Wind are doubled");
            wind.Put("DefendIronclad", PileType.Hand);
            wind.Play("DefendIronclad");
            c.Equal(25, wind.Player.Block, "the next card is not");

            var two = With(["DefendIronclad", "DefendIronclad", "DefendIronclad"]);
            two.ApplyPower<UnmovablePower>(two.Player, 2, two.Player);
            two.Play("DefendIronclad");
            two.Play("DefendIronclad");
            c.Equal(20, two.Player.Block, "2 stacks: the first two single gains are doubled");
            two.Play("DefendIronclad");
            c.Equal(25, two.Player.Block, "the third is not");

            var both = With(["SecondWind", "DefendIronclad", "DefendIronclad"]);
            both.ApplyPower<UnmovablePower>(both.Player, 2, both.Player);
            both.Play("SecondWind");
            c.Equal(20, both.Player.Block, "2 stacks: both gains of Second Wind doubled");
            both.Put("DefendIronclad", PileType.Hand);
            both.Play("DefendIronclad");
            c.Equal(25, both.Player.Block, "its two gains used both stacks: the next card is not doubled");
        });
    }

    private static void Unrelenting(Check c)
    {
        c.Group("Unrelenting: 14 damage; the next Attack costs 0", () =>
        {
            var f = With(["Unrelenting", "StrikeIronclad", "StrikeIronclad", "DefendIronclad"]);
            var log = Watch(f);
            c.Equal("Deal 14 damage. The next Attack you play costs 0.", Text(f, f.Hand[0]), "text");
            f.Play("Unrelenting", 0);
            c.Equal(86, f.Enemy(0).Hp, "14 damage");
            c.Equal(1, f.Energy, "costs 2");
            c.Equal(1, f.Player.PowerAmount<FreeAttackPower>(), "1 Free Attack: its own play did not use it");
            c.Sequence(["hit 14", "power FreeAttackPower 1", "played Unrelenting"], log.Seen, "hit first, then the power");
            c.Equal(0, f.CostOf(f.Hand[0]), "a Strike in hand costs 0");
            c.Equal("0", f.CostText(f.Hand[1]), "so does the other, as the snapshot shows it");
            c.Equal(1, f.CostOf(f.Hand[2]), "a Skill keeps its cost");
            c.Equal(1, f.CostOf(f.DrawPile[0]), "an Attack in the draw pile keeps its cost");
            f.Play("StrikeIronclad", 0);
            c.Equal(80, f.Enemy(0).Hp, "the Strike hits");
            c.Equal(1, f.Energy, "for free");
            c.That(!f.Player.HasPower<FreeAttackPower>(), "the charge is used");
            c.Equal(1, f.CostOf(f.Hand[0]), "the other Strike costs 1 again");
        });

        c.Group("Unrelenting+: 20 damage", () =>
        {
            var f = With(["Unrelenting+"]);
            c.Equal("Deal 20 damage. The next Attack you play costs 0.", Text(f, f.Hand[0]), "text");
            f.Play("Unrelenting", 0);
            c.Equal(80, f.Enemy(0).Hp, "20 damage");
            c.Equal(1, f.Energy, "still costs 2");
            c.Equal(1, f.Player.PowerAmount<FreeAttackPower>(), "1 Free Attack");
        });

        c.Group("Unrelenting: the charge waits, and what uses it", () =>
        {
            var waits = With(["Unrelenting"]);
            waits.Play("Unrelenting", 0);
            waits.EndTurn();
            c.Equal(1, waits.Player.PowerAmount<FreeAttackPower>(), "it is still there on the next turn");
            c.Equal(0, waits.CostOf(waits.Hand[waits.HandIndex("Strike")]), "a Strike costs 0");
            c.Equal(1, waits.CostOf(waits.Hand[waits.HandIndex("Defend")]), "a Defend costs 1");

            var chain = With(["Unrelenting", "Unrelenting"]);
            chain.Play("Unrelenting", 0);
            c.Equal(0, chain.CostOf(chain.Hand[0]), "a second Unrelenting is free");
            chain.Play("Unrelenting", 0);
            c.Equal(72, chain.Enemy(0).Hp, "14 + 14");
            c.Equal(1, chain.Energy, "only the first was paid for");
            c.Equal(1, chain.Player.PowerAmount<FreeAttackPower>(), "it used the charge and left a new one");

            var x = With(["Unrelenting", "Whirlwind"]);
            x.Play("Unrelenting", 0);
            c.Equal("X", x.CostText(x.Hand[0]), "an X card stays X");
            x.Play("Whirlwind");
            c.Equal(81, x.Enemy(0).Hp, "it spends the 1 energy left: one hit of 5");
            c.Equal(0, x.Energy, "all energy is gone");
            c.That(!x.Player.HasPower<FreeAttackPower>(), "and the charge is used");

            var skill = With(["Unrelenting", "DefendIronclad"]);
            skill.Play("Unrelenting", 0);
            skill.Play("DefendIronclad");
            c.Equal(0, skill.Energy, "a Skill is paid for");
            c.Equal(1, skill.Player.PowerAmount<FreeAttackPower>(), "and leaves the charge");

            var stacks = With(["StrikeIronclad", "StrikeIronclad", "StrikeIronclad"]);
            stacks.ApplyPower<FreeAttackPower>(stacks.Player, 2, stacks.Player);
            stacks.Play("StrikeIronclad", 0);
            stacks.Play("StrikeIronclad", 0);
            c.Equal(3, stacks.Energy, "two charges, two free Attacks");
            stacks.Play("StrikeIronclad", 0);
            c.Equal(2, stacks.Energy, "the third costs 1");

            var echo = With(["StrikeIronclad"]);
            echo.ApplyPower<FreeAttackPower>(echo.Player, 2, echo.Player);
            echo.ApplyPower<Echo>(echo.Player, 1, echo.Player);
            echo.Play("StrikeIronclad", 0);
            c.Equal(88, echo.Enemy(0).Hp, "an Attack that runs twice");
            c.That(!echo.Player.HasPower<FreeAttackPower>(), "uses two charges");

            var stomp = With(["Stomp"]);
            stomp.ApplyPower<FreeAttackPower>(stomp.Player, 1, stomp.Player);
            stomp.SetEnergy(0);
            c.Equal(0, stomp.CostOf(stomp.Hand[0]), "a 3-cost Attack costs 0 as well");
            stomp.Play("Stomp");
            c.Equal(88, stomp.Enemy(0).Hp, "and can be played with no energy");
            c.That(!stomp.Player.HasPower<FreeAttackPower>(), "for the charge");

            var auto = With(["Stampede", "StrikeIronclad"]);
            auto.ApplyPower<FreeAttackPower>(auto.Player, 1, auto.Player);
            auto.Play("Stampede");
            c.Equal(1, auto.Player.PowerAmount<FreeAttackPower>(), "a Power card does not use it");
            auto.EndTurn();
            c.Equal(94, auto.Enemy(0).Hp, "an Attack played by Stampede");
            c.That(!auto.Player.HasPower<FreeAttackPower>(), "uses the charge");
        });
    }

    private static void Uppercut(Check c)
    {
        c.Group("Uppercut: 13 damage, then 1 Weak, then 1 Vulnerable", () =>
        {
            var f = With(["Uppercut", "StrikeIronclad"]);
            var log = Watch(f);
            c.Equal("Deal 13 damage. Apply 1 Weak. Apply 1 Vulnerable.", Text(f, f.Hand[0]), "text");
            f.Play("Uppercut", 0);
            c.Equal(87, f.Enemy(0).Hp, "13 damage");
            c.Equal(1, f.Energy, "costs 2");
            c.Equal(1, f.Enemy(0).PowerAmount<WeakPower>(), "1 Weak");
            c.Equal(1, f.Enemy(0).PowerAmount<VulnerablePower>(), "1 Vulnerable");
            c.Sequence(["hit 13", "power WeakPower 1", "power VulnerablePower 1", "played Uppercut"], log.Seen, "hit, Weak, Vulnerable");
            f.Play("StrikeIronclad", 0);
            c.Equal(78, f.Enemy(0).Hp, "a Strike into Vulnerable: 9");
        });

        c.Group("Uppercut+: still 13 damage, 2 Weak and 2 Vulnerable", () =>
        {
            var f = With(["Uppercut+"]);
            c.Equal("Deal 13 damage. Apply 2 Weak. Apply 2 Vulnerable.", Text(f, f.Hand[0]), "text");
            f.Play("Uppercut", 0);
            c.Equal(87, f.Enemy(0).Hp, "13 damage");
            c.Equal(1, f.Energy, "still costs 2");
            c.Equal(2, f.Enemy(0).PowerAmount<WeakPower>(), "2 Weak");
            c.Equal(2, f.Enemy(0).PowerAmount<VulnerablePower>(), "2 Vulnerable");
        });

        c.Group("Uppercut: Artifact takes the Weak; the Weak works on the enemy's attack", () =>
        {
            var warded = With(["Uppercut"], Warded());
            warded.Play("Uppercut", 0);
            c.That(!warded.Enemy(0).HasPower<WeakPower>(), "Artifact stops the Weak, which comes first");
            c.Equal(1, warded.Enemy(0).PowerAmount<VulnerablePower>(), "the Vulnerable lands");
            c.That(!warded.Enemy(0).HasPower<ArtifactPower>(), "Artifact is used up");

            var hit = CheckKit.VsDummy(Hitter(8));
            hit.Clear(PileType.Hand);
            hit.Put("Uppercut", PileType.Hand);
            hit.Play("Uppercut", 0);
            hit.EndTurn();
            c.Equal(74, hit.Player.Hp, "the Weak enemy hits for 8 x 0.75 = 6");
            c.That(!hit.Enemy(0).HasPower<WeakPower>() && !hit.Enemy(0).HasPower<VulnerablePower>(), "1 of each is gone after its turn");

            var already = With(["Uppercut"]);
            Vulnerable(already, 0, 1);
            Strength(already, 1);
            already.Play("Uppercut", 0);
            c.Equal(79, already.Enemy(0).Hp, "(13 + 1) x 1.5 = 21 into Vulnerable that was there before");
            c.Equal(2, already.Enemy(0).PowerAmount<VulnerablePower>(), "the stack grows to 2");
        });
    }

    private static void Vicious(Check c)
    {
        c.Group("Vicious: draw 1 whenever you apply Vulnerable", () =>
        {
            var f = With(["Vicious", "Bash"]);
            var log = Watch(f);
            c.Equal("Whenever you apply Vulnerable, draw 1 card.", Text(f, f.Hand[0]), "text");
            f.Play("Vicious");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(1, f.Player.PowerAmount<ViciousPower>(), "1 Vicious");
            c.That(!f.AllCards.Any(card => card.Def.ClassName == "Vicious"), "a Power card leaves the fight");
            log.Seen.Clear();
            f.Play("Bash", 0);
            c.Equal(92, f.Enemy(0).Hp, "Bash hits for 8");
            c.Sequence(["Strike"], f.HandTitles(), "one card drawn");
            c.Sequence(["hit 8", "power VulnerablePower 2", "drawn Strike", "played Bash"], log.Seen, "the draw happens inside the card that applied the Vulnerable");
        });

        c.Group("Vicious+: draw 2", () =>
        {
            var f = With(["Vicious+", "Bash"]);
            c.Equal("Whenever you apply Vulnerable, draw 2 cards.", Text(f, f.Hand[0]), "text");
            f.Play("Vicious");
            c.Equal(2, f.Energy, "still costs 1");
            c.Equal(2, f.Player.PowerAmount<ViciousPower>(), "2 Vicious");
            f.Play("Bash", 0);
            c.Sequence(["Strike", "Strike"], f.HandTitles(), "two cards drawn");
        });

        c.Group("Vicious: what counts as applying Vulnerable", () =>
        {
            var stack = With(["Vicious", "Bash", "Tremble"]);
            stack.SetEnergy(4);
            stack.Play("Vicious");
            stack.Play("Bash", 0);
            stack.Play("Tremble", 0);
            c.Equal(5, stack.Enemy(0).PowerAmount<VulnerablePower>(), "more on a stack that is there");
            c.Sequence(["Strike", "Strike"], stack.HandTitles(), "draws again");

            var warded = With(["Vicious", "Bash"], Warded());
            warded.Play("Vicious");
            warded.Play("Bash", 0);
            c.Equal(0, warded.Hand.Count, "an application Artifact stopped draws nothing");

            var onMe = With(["Vicious"]);
            onMe.Play("Vicious");
            onMe.ApplyPower<VulnerablePower>(onMe.Player, 1, onMe.Enemy(0));
            c.Equal(0, onMe.Hand.Count, "Vulnerable an enemy puts on the player draws nothing");

            var tick = With(["Vicious", "Bash"]);
            tick.Play("Vicious");
            tick.Play("Bash", 0);
            tick.EndTurn();
            c.Equal(1, tick.Enemy(0).PowerAmount<VulnerablePower>(), "the Vulnerable ticked down");
            c.Equal(5, tick.Hand.Count, "which draws nothing: a hand of 5 on turn 2");
        });
    }

    private static void Whirlwind(Check c)
    {
        c.Group("Whirlwind: 5 damage to every enemy, once per energy", () =>
        {
            var f = With(["Whirlwind"], Idle(100, "A"), Idle(100, "B"));
            c.Equal("Deal 5 damage to ALL enemies X times.", Text(f, f.Hand[0]), "text");
            c.Equal("X", f.CostText(f.Hand[0]), "the cost reads X");
            c.Sequence(["play:0", "end_turn"], Ids(f), "played without a target");
            f.Play("Whirlwind");
            c.Equal(0, f.Energy, "all 3 energy spent");
            c.Equal(85, f.Enemy(0).Hp, "3 x 5 to the first");
            c.Equal(85, f.Enemy(1).Hp, "3 x 5 to the second");
            c.Equal(3, EventsOn(f, f.Enemy(0)), "three damage events each");
        });

        c.Group("Whirlwind+: 8 damage", () =>
        {
            var f = With(["Whirlwind+"], Idle(100, "A"), Idle(100, "B"));
            c.Equal("Deal 8 damage to ALL enemies X times.", Text(f, f.Hand[0]), "text");
            c.Equal("X", f.CostText(f.Hand[0]), "still X");
            f.Play("Whirlwind");
            c.Equal(76, f.Enemy(0).Hp, "3 x 8 to the first");
            c.Equal(76, f.Enemy(1).Hp, "3 x 8 to the second");
        });

        c.Group("Whirlwind: X = 0, more energy, modifiers per hit, enemies that die", () =>
        {
            var zero = With(["Whirlwind"]);
            zero.SetEnergy(0);
            c.That(Ids(zero).Contains("play:0"), "playable with no energy");
            zero.Play("Whirlwind");
            c.Equal(100, zero.Enemy(0).Hp, "X = 0 hits nothing");
            c.Equal(1, zero.History.CardPlaysFinishedThisTurn(zero, CardType.Attack), "but it is an Attack played");
            c.Sequence(["Whirlwind"], Titles(zero.DiscardPile), "and goes to the discard pile");

            var five = With(["Whirlwind"]);
            five.SetEnergy(5);
            five.Play("Whirlwind");
            c.Equal(75, five.Enemy(0).Hp, "5 energy: 5 hits");

            var strong = With(["Whirlwind"], Idle(100, "A"), Idle(100, "B"));
            Strength(strong, 2);
            Vulnerable(strong, 0, 1);
            c.Equal("Deal 7 damage to ALL enemies X times.", Text(strong, strong.Hand[0]), "the text shows Strength");
            strong.Play("Whirlwind");
            c.Equal(70, strong.Enemy(0).Hp, "(5 + 2) x 1.5 = 10.5 per hit, 10 HP each");
            c.Equal(79, strong.Enemy(1).Hp, "7 per hit on the other");

            var dying = With(["Whirlwind"], Idle(8, "A"), Idle(100, "B"));
            var a = dying.Enemy(0);
            dying.Play("Whirlwind");
            c.That(a.IsDead, "the first enemy dies on the second hit");
            c.Equal(2, EventsOn(dying, a), "and is not hit again");
            c.Equal(85, dying.Enemy(1).Hp, "the other takes all three");

            var last = With(["Whirlwind"], Idle(10));
            last.Play("Whirlwind");
            c.That(last.IsOver && last.Won, "the hits stop when nobody is left");
        });
    }
}
