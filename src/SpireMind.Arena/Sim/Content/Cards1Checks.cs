namespace SpireMind.Arena;

/// <summary>
/// Scripted checks for the cards of Cards1 (cards-1.json): every card plain and upgraded, with
/// the numbers worked out by hand from the game's card and power classes, and the edge cases
/// the spec notes name. Unless a check says otherwise the fight is the starter deck in a fixed
/// order against one idle 100 HP dummy, with the hand replaced by the cards the check names;
/// the draw pile is then Strike, Strike, Strike, Defend, Defend from the top.
/// </summary>
public sealed class Cards1Checks : IArenaChecks
{
    /// <summary>A card that stays in hand at end of turn, to fill the hand across a turn boundary.</summary>
    private static readonly CardDef Kept = new("CheckKept", "Kept", CardType.Skill, CardRarity.Common, 0, TargetType.Self)
    {
        Keywords = CardKeywords.Retain,
    };

    /// <summary>A Skill without a cost, to show that Corruption leaves "no cost" alone.</summary>
    private static readonly CardDef Costless = new("CheckCostless", "Costless", CardType.Skill, CardRarity.Common, CardDef.NoCost, TargetType.Self)
    {
        Keywords = CardKeywords.Unplayable,
    };

    /// <summary>Every card costs 1 more, in the ordinary cost pass.</summary>
    private sealed class Tax : Power
    {
        public override string Name => "Tax";
        public override PowerType Type => PowerType.Debuff;
        public override string Describe() => "";
        public override decimal ModifyEnergyCost(Fight f, CardInstance card, decimal cost) => cost + 1m;
    }

    /// <summary>Forbids every play except Cascade's own (the shape of the game's play-forbidding powers).</summary>
    private sealed class Forbid : Power
    {
        public override string Name => "Forbid";
        public override PowerType Type => PowerType.Debuff;
        public override string Describe() => "";
        public override bool ShouldPlay(Fight f, CardInstance card, AutoPlayType autoPlay) => card.Def.ClassName == "Cascade";
    }

    // ---------- set-up helpers ----------

    private static void SetHand(Fight f, string[] hand)
    {
        f.Clear(PileType.Hand);
        foreach (var card in hand) f.Put(card, PileType.Hand);
    }

    /// <summary>One idle 100 HP dummy; the hand is exactly these cards.</summary>
    private static Fight Solo(params string[] hand) => Vs(new CheckDummy(), hand);

    private static Fight Vs(CheckDummy dummy, params string[] hand)
    {
        var f = CheckKit.VsDummy(dummy);
        SetHand(f, hand);
        return f;
    }

    /// <summary>Two idle dummies, e0 "Left" and e1 "Right".</summary>
    private static Fight Duo(int leftHp, int rightHp, params string[] hand)
    {
        var f = CheckKit.VsDummies(new CheckDummy { Hp = leftHp, DisplayName = "Left" }, new CheckDummy { Hp = rightHp, DisplayName = "Right" });
        SetHand(f, hand);
        return f;
    }

    /// <summary>One idle 100 HP dummy, the player on <paramref name="hp"/> HP.</summary>
    private static Fight AtHp(int hp, params string[] hand)
    {
        var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(hp).Enemy(new CheckDummy()).Start();
        SetHand(f, hand);
        return f;
    }

    /// <summary>A dummy that hits for <paramref name="damage"/> every turn.</summary>
    private static CheckDummy Hitter(int damage, int hits = 1) => new() { Steps = [new DummyStep("HIT", Damage: damage, Hits: hits)] };

    /// <summary>A dummy that, on its turn, notes how many cards the player holds as its own Strength
    /// (none for an empty hand): the only way to see the hand between the player's turns.</summary>
    private static CheckDummy HandCounter() => new()
    {
        Steps = [new DummyStep("COUNT", Then: (f, me) => f.ApplyPower<StrengthPower>(me, f.Hand.Count, me))],
    };

    private static IEnumerable<string> Titles(IReadOnlyList<CardInstance> pile) => pile.Select(card => card.Title);

    private static string HandText(Fight f, int index = 0) => Fight.CardText(f, f.Hand[index], PileType.Hand);

    private static int FirstIndex(Fight f, Func<HistoryEntry, bool> match)
    {
        var i = 0;
        foreach (var e in f.History.Entries)
        {
            if (match(e)) return i;
            i++;
        }
        return -1;
    }

    private static int LastIndex(Fight f, Func<HistoryEntry, bool> match)
    {
        var i = 0;
        var found = -1;
        foreach (var e in f.History.Entries)
        {
            if (match(e)) found = i;
            i++;
        }
        return found;
    }

    public void Run(Check c)
    {
        Registered(c);
        Aggression(c);
        Anger(c);
        AshenStrike(c);
        Barricade(c);
        BattleTrance(c);
        BloodWall(c);
        Bloodletting(c);
        Bludgeon(c);
        BodySlam(c);
        Brand(c);
        Break(c);
        Breakthrough(c);
        Bully(c);
        BurningPact(c);
        Cascade(c);
        Cinder(c);
        Colossus(c);
        Conflagration(c);
        Corruption(c);
        CrimsonMantle(c);
        Cruelty(c);
        DarkEmbrace(c);
        DemonForm(c);
        Dismantle(c);
        Dominate(c);
        LiveFights(c);
    }

    // ---------- what is registered ----------

    private sealed record Row(string Class, string Title, CardType Type, CardRarity Rarity, int Cost, int UpgradedCost, TargetType Target, string Text, string UpgradedText);

    private const int X = CardDef.CostX;

    private static readonly Row[] Rows =
    [
        new("Aggression", "Aggression", CardType.Power, CardRarity.Rare, 1, 1, TargetType.Self,
            "At the start of your turn, a random Attack from your discard pile returns to your hand and is upgraded for the rest of combat.",
            "Innate. / At the start of your turn, a random Attack from your discard pile returns to your hand and is upgraded for the rest of combat."),
        new("Anger", "Anger", CardType.Attack, CardRarity.Common, 0, 0, TargetType.AnyEnemy,
            "Deal 6 damage. Add a copy of this card to your discard pile.", "Deal 8 damage. Add a copy of this card to your discard pile."),
        new("AshenStrike", "Ashen Strike", CardType.Attack, CardRarity.Uncommon, 1, 1, TargetType.AnyEnemy,
            "Deal 6 damage (6 plus 3 for each card in your exhaust pile).", "Deal 6 damage (6 plus 4 for each card in your exhaust pile)."),
        new("Barricade", "Barricade", CardType.Power, CardRarity.Rare, 3, 2, TargetType.Self,
            "Your Block is no longer removed at the start of your turn.", "Your Block is no longer removed at the start of your turn."),
        new("BattleTrance", "Battle Trance", CardType.Skill, CardRarity.Uncommon, 0, 0, TargetType.Self,
            "Draw 3 cards. You cannot draw any more cards this turn.", "Draw 4 cards. You cannot draw any more cards this turn."),
        new("BloodWall", "Blood Wall", CardType.Skill, CardRarity.Common, 2, 2, TargetType.Self,
            "Lose 2 HP. Gain 16 Block.", "Lose 2 HP. Gain 20 Block."),
        new("Bloodletting", "Bloodletting", CardType.Skill, CardRarity.Common, 0, 0, TargetType.Self,
            "Lose 3 HP. Gain 2 energy.", "Lose 3 HP. Gain 3 energy."),
        new("Bludgeon", "Bludgeon", CardType.Attack, CardRarity.Uncommon, 3, 3, TargetType.AnyEnemy,
            "Deal 32 damage.", "Deal 42 damage."),
        new("BodySlam", "Body Slam", CardType.Attack, CardRarity.Common, 1, 0, TargetType.AnyEnemy,
            "Deal damage equal to your Block.", "Deal damage equal to your Block."),
        new("Brand", "Brand", CardType.Skill, CardRarity.Rare, 0, 0, TargetType.Self,
            "Lose 1 HP. Exhaust a card in your hand. Gain 1 Strength.", "Lose 1 HP. Exhaust a card in your hand. Gain 2 Strength."),
        new("Break", "Break", CardType.Attack, CardRarity.Ancient, 1, 1, TargetType.AnyEnemy,
            "Deal 20 damage. Apply 5 Vulnerable.", "Deal 30 damage. Apply 7 Vulnerable."),
        new("Breakthrough", "Breakthrough", CardType.Attack, CardRarity.Common, 1, 1, TargetType.AllEnemies,
            "Lose 1 HP. Deal 9 damage to ALL enemies.", "Lose 1 HP. Deal 13 damage to ALL enemies."),
        new("Bully", "Bully", CardType.Attack, CardRarity.Uncommon, 0, 0, TargetType.AnyEnemy,
            "Deal 4 damage, plus 2 for each Vulnerable on the enemy.", "Deal 4 damage, plus 3 for each Vulnerable on the enemy."),
        new("BurningPact", "Burning Pact", CardType.Skill, CardRarity.Uncommon, 1, 1, TargetType.Self,
            "Exhaust a card in your hand. Draw 2 cards.", "Exhaust a card in your hand. Draw 3 cards."),
        new("Cascade", "Cascade", CardType.Skill, CardRarity.Rare, X, X, TargetType.Self,
            "Play the top X cards of your draw pile.", "Play the top X+1 cards of your draw pile."),
        new("Cinder", "Cinder", CardType.Attack, CardRarity.Common, 2, 2, TargetType.AnyEnemy,
            "Deal 18 damage. Exhaust a random card in your hand.", "Deal 24 damage. Exhaust a random card in your hand."),
        new("Colossus", "Colossus", CardType.Skill, CardRarity.Uncommon, 1, 1, TargetType.Self,
            "Gain 5 Block. For 1 turn, attacks by Vulnerable enemies deal half damage to you.", "Gain 8 Block. For 1 turn, attacks by Vulnerable enemies deal half damage to you."),
        new("Conflagration", "Conflagration", CardType.Attack, CardRarity.Rare, 1, 1, TargetType.AllEnemies,
            "Deal 2 damage to ALL enemies 4 times.", "Deal 2 damage to ALL enemies 5 times."),
        new("Corruption", "Corruption", CardType.Power, CardRarity.Ancient, 3, 2, TargetType.Self,
            "Skills cost 0. Whenever you play a Skill, Exhaust it.", "Skills cost 0. Whenever you play a Skill, Exhaust it."),
        new("CrimsonMantle", "Crimson Mantle", CardType.Power, CardRarity.Rare, 1, 1, TargetType.Self,
            "At the start of your turn, lose 1 HP and gain 8 Block.", "At the start of your turn, lose 1 HP and gain 10 Block."),
        new("Cruelty", "Cruelty", CardType.Power, CardRarity.Rare, 1, 1, TargetType.Self,
            "Your attacks deal 25% more damage to Vulnerable enemies (x1.75 instead of x1.5).", "Your attacks deal 50% more damage to Vulnerable enemies (x2 instead of x1.5)."),
        new("DarkEmbrace", "Dark Embrace", CardType.Power, CardRarity.Rare, 2, 1, TargetType.Self,
            "Whenever a card is Exhausted, draw 1 card.", "Whenever a card is Exhausted, draw 1 card."),
        new("DemonForm", "Demon Form", CardType.Power, CardRarity.Rare, 3, 3, TargetType.Self,
            "At the start of your turn, gain 2 Strength.", "At the start of your turn, gain 3 Strength."),
        new("Dismantle", "Dismantle", CardType.Attack, CardRarity.Uncommon, 1, 1, TargetType.AnyEnemy,
            "Deal 8 damage. If the enemy is Vulnerable, hit it twice.", "Deal 10 damage. If the enemy is Vulnerable, hit it twice."),
        new("Dominate", "Dominate", CardType.Skill, CardRarity.Uncommon, 1, 1, TargetType.AnyEnemy,
            "Apply 1 Vulnerable. Gain 1 Strength for each Vulnerable on the enemy. / Exhaust.", "Apply 2 Vulnerable. Gain 1 Strength for each Vulnerable on the enemy. / Exhaust."),
    ];

    private static void Registered(Check c)
    {
        c.Group("cards-1.json: every card is registered with the game's type, rarity, cost and target", () =>
        {
            var registry = Content.Registry;
            var f = Solo();
            foreach (var row in Rows)
            {
                var def = registry.Card(row.Class);
                c.Equal("Cards1", registry.SourceOf(ContentKind.Card, row.Class), $"{row.Class}: registered by this file");
                c.Equal(row.Title, def.Name, $"{row.Class}: title");
                c.Equal(row.Type, def.Type, $"{row.Class}: type");
                c.Equal(row.Rarity, def.Rarity, $"{row.Class}: rarity");
                c.Equal(row.Target, def.Target, $"{row.Class}: target");
                c.Equal(row.Cost == X, def.CostsX, $"{row.Class}: X cost");
                if (row.Cost != X) c.Equal(row.Cost, def.Cost, $"{row.Class}: cost");
                c.Equal(1, def.MaxUpgradeLevel, $"{row.Class}: can be upgraded once");
                c.That(registry.IroncladPool.Contains(def), $"{row.Class}: in the Ironclad pool");
                c.Equal(row.Rarity != CardRarity.Ancient, def.IsGeneratableInCombat, $"{row.Class}: random generation in a fight (never for Ancient)");

                var plain = f.CreateCard(row.Class);
                var upgraded = f.CreateCard(row.Class, upgraded: true);
                c.Equal(row.Text, Fight.CardText(null, plain, PileType.Deck), $"{row.Class}: text");
                c.Equal(row.UpgradedText, Fight.CardText(null, upgraded, PileType.Deck), $"{row.Class}+: text");
                c.Equal(row.Title + "+", upgraded.Title, $"{row.Class}+: title");
                if (row.Cost != X)
                {
                    c.Equal(row.Cost, plain.BaseCost, $"{row.Class}: cost of an instance");
                    c.Equal(row.UpgradedCost, upgraded.BaseCost, $"{row.Class}+: cost");
                }
                // Dominate keeps Exhaust when upgraded; Aggression gains Innate; nothing else has a keyword.
                var keywords = row.Class == "Dominate" ? CardKeywords.Exhaust : CardKeywords.None;
                c.Equal(keywords, plain.Keywords, $"{row.Class}: keywords");
                c.Equal(row.Class == "Aggression" ? CardKeywords.Innate : keywords, upgraded.Keywords, $"{row.Class}+: keywords");
            }
            c.Equal(25, Rows.Length, "the 29 cards of the spec file less Bash, Defend and Armaments (core) and Demonic Shield (left out)");

            foreach (var core in new[] { "Bash", "DefendIronclad", "Armaments" })
                c.Equal("CoreContent", registry.SourceOf(ContentKind.Card, core), $"{core} of the same spec file is the engine core's");

            foreach (var power in new[] { "AggressionPower", "BarricadePower", "NoDrawPower", "ColossusPower", "CorruptionPower", "CrimsonMantlePower", "CrueltyPower", "DarkEmbracePower", "DemonFormPower" })
                c.Equal("Cards1", registry.SourceOf(ContentKind.Power, power), $"{power}: registered by this file");
        });

        c.Group("the nine powers of this file: titles, kinds and how they stack", () =>
        {
            (string Class, string Name, PowerType Type, PowerStackType Stack)[] powers =
            [
                ("AggressionPower", "Aggression", PowerType.Buff, PowerStackType.Counter),
                ("BarricadePower", "Barricade", PowerType.Buff, PowerStackType.Single),
                ("NoDrawPower", "No Draw", PowerType.Debuff, PowerStackType.Single),
                ("ColossusPower", "Colossus", PowerType.Buff, PowerStackType.Counter),
                ("CorruptionPower", "Corruption", PowerType.Buff, PowerStackType.Single),
                ("CrimsonMantlePower", "Crimson Mantle", PowerType.Buff, PowerStackType.Counter),
                ("CrueltyPower", "Cruelty", PowerType.Buff, PowerStackType.Counter),
                ("DarkEmbracePower", "Dark Embrace", PowerType.Buff, PowerStackType.Counter),
                ("DemonFormPower", "Demon Form", PowerType.Buff, PowerStackType.Counter),
            ];
            foreach (var (className, name, type, stack) in powers)
            {
                var power = Content.Registry.NewPower(className);
                c.Equal(name, power.Name, $"{className}: title");
                c.Equal(type, power.Type, $"{className}: kind");
                c.Equal(stack, power.StackType, $"{className}: stack type");
                c.That(power.InstanceType == PowerInstanceType.None && !power.AllowNegative && power.IsVisible, $"{className}: one visible instance per creature, never negative");
            }

            var f = Solo("BattleTrance");
            f.Play("Battle Trance");
            c.Equal("""[{"name":"No Draw","amount":1,"kind":"Debuff","text":"Cannot draw cards for the rest of this turn."}]""",
                f.Snapshot().State["me"]!["powers"]!.ToJsonString(), "a power as the snapshot shows it");
        });

        c.Group("Demonic Shield: left out as multiplayer only", () =>
        {
            var registry = Content.Registry;
            c.That(!registry.Has(ContentKind.Card, "DemonicShield"), "not a card of the arena");
            c.That(registry.IsExcluded(ContentKind.Card, "DemonicShield", out var reason) && reason.Contains("multiplayer", StringComparison.Ordinal), "excluded, and the reason says multiplayer");
            c.That(registry.IroncladPool.All(x => x.ClassName != "DemonicShield"), "not in the pool");
        });
    }

    // ---------- Aggression ----------

    private static void Aggression(Check c)
    {
        c.Group("Aggression: next turn the one Attack of the discard pile returns, upgraded, before the draw", () =>
        {
            var f = Solo("Aggression", "Bash", "DefendIronclad", "DefendIronclad");
            var bash = f.Hand[1].Id;
            f.Play("Aggression");
            c.Equal(1, f.Player.PowerAmount<AggressionPower>(), "Aggression 1");
            c.Equal(2, f.Energy, "costs 1");
            c.That(f.DiscardPile.Count == 0 && f.ExhaustPile.Count == 0 && f.PlayPile.Count == 0, "a played Power card is in no pile");
            c.Sequence(["Bash", "Defend", "Defend"], f.HandTitles(), "nothing returns on the turn it is played");
            c.Equal("At the start of its turn, a random Attack from the discard pile returns to the hand and is upgraded.", f.Player.GetPower<AggressionPower>()!.Describe(), "the power's text");

            f.EndTurn();
            c.Sequence(["Bash+", "Strike", "Strike", "Strike", "Defend", "Defend"], f.HandTitles(), "Bash came back first and upgraded, then the 5 drawn cards");
            c.Equal(bash, f.Hand[0].Id, "it is the Bash that was discarded");
            c.Equal("Deal 10 damage. Apply 3 Vulnerable.", HandText(f), "upgraded for the rest of the fight");
            c.Sequence(["Defend", "Defend"], Titles(f.DiscardPile), "the Skills stay in the discard pile");
            c.Equal(0, f.Rng.CombatCardSelection.Counter, "one Attack: the shuffle of a one-card list draws no number");
            c.Equal(0, f.History.Count(e => e.Kind == HistoryKind.CardDrawn && e.CardId == bash), "a pile move, not a draw");
            c.Equal(1, f.Player.PowerAmount<AggressionPower>(), "it never expires");
        });

        c.Group("Aggression: with three Attacks the whole list is shuffled on the CombatCardSelection stream and the first is taken", () =>
        {
            var f = Solo("Aggression", "StrikeIronclad", "Bash", "Anger", "DefendIronclad");
            f.Play("Aggression");
            // The game's shuffle, by hand: from the last index down to 1, swap with a position at or below it.
            var rng = f.Rng.CombatCardSelection.Clone();
            var order = new List<string> { "Strike", "Bash", "Anger" };
            for (var n = order.Count; n > 1;)
            {
                n--;
                var j = rng.NextInt(n + 1);
                (order[n], order[j]) = (order[j], order[n]);
            }

            f.EndTurn();
            c.Equal(2, f.Rng.CombatCardSelection.Counter, "three Attacks cost two numbers, whatever the amount");
            c.Equal(6, f.Hand.Count, "one card returned, five drawn");
            c.Equal(order[0] + "+", f.Hand[0].Title, "the first of the shuffled list returned, upgraded");
            c.Sequence(new[] { "Strike", "Bash", "Anger", "Defend" }.Where(t => t != order[0]), Titles(f.DiscardPile), "the others stay where they were, not upgraded");
        });

        c.Group("Aggression: two stacks return two Attacks; without an Attack nothing happens", () =>
        {
            var f = Solo("Aggression", "Aggression", "StrikeIronclad", "Bash");
            f.Play("Aggression");
            f.Play("Aggression");
            c.Equal(2, f.Player.PowerAmount<AggressionPower>(), "each play adds 1");
            c.Equal("At the start of its turn, 2 random Attacks from the discard pile return to the hand and are upgraded.", f.Player.GetPower<AggressionPower>()!.Describe(), "the power's text with 2");
            var rng = f.Rng.CombatCardSelection.Clone();
            string[] order = rng.NextInt(2) == 0 ? ["Bash+", "Strike+"] : ["Strike+", "Bash+"];
            f.EndTurn();
            c.Sequence([.. order, "Strike", "Strike", "Strike", "Defend", "Defend"], f.HandTitles(), "both came back upgraded, in the shuffled order");
            c.Equal(1, f.Rng.CombatCardSelection.Counter, "two Attacks cost one number");
            c.Equal(0, f.DiscardPile.Count, "nothing is left in the discard pile");

            var g = Solo("Aggression", "DefendIronclad");
            g.Play("Aggression");
            g.EndTurn();
            c.Sequence(["Strike", "Strike", "Strike", "Defend", "Defend"], g.HandTitles(), "no Attack in the discard pile: only the draw");
            c.Equal(0, g.Rng.CombatCardSelection.Counter, "and no random number");
        });

        c.Group("Aggression: a full hand sends the card back to the discard pile, still upgraded; an upgraded Attack stays as it is", () =>
        {
            var f = Solo("Aggression", "StrikeIronclad");
            for (var i = 0; i < 10; i++) f.Put(Kept, PileType.Hand);
            f.Play("Aggression");
            f.Play("Strike", 0);
            f.EndTurn();
            c.Equal(10, f.Hand.Count, "the ten retained cards fill the hand");
            c.That(f.Hand.All(card => card.Title == "Kept"), "the Attack did not get in, and nothing was drawn");
            c.Sequence(["Strike+"], Titles(f.DiscardPile), "it is back in the discard pile, upgraded all the same");
            c.Equal(5, f.DrawPile.Count, "the draw pile is untouched");

            var g = Solo("Aggression", "Bash+");
            g.Play("Aggression");
            g.EndTurn();
            c.Equal("Bash+", g.Hand[0].Title, "an already upgraded Attack returns as it is");
            c.Equal(1, g.Hand[0].UpgradeLevel, "not upgraded twice");
        });

        c.Group("Aggression+: Innate, same cost", () =>
        {
            var f = Solo("Aggression+");
            c.That(f.Hand[0].Has(CardKeywords.Innate), "the upgrade adds Innate");
            c.That(!f.CreateCard("Aggression").Has(CardKeywords.Innate), "the plain card is not Innate");
            f.Play("Aggression+");
            c.Equal(2, f.Energy, "still costs 1");
            c.Equal(1, f.Player.PowerAmount<AggressionPower>(), "still Aggression 1");

            // Innate: in the opening hand whatever the shuffle (5 of 11 cards by chance).
            for (var seed = 1; seed <= 6; seed++)
            {
                var g = new FightBuilder().Deck([.. CheckKit.StarterInOrder, "Aggression+"]).Enemy(new CheckDummy()).Seed(seed).Start();
                c.That(g.HandTitles().Contains("Aggression+"), $"seed {seed}: Aggression+ is in the opening hand");
                c.Equal(5, g.Hand.Count, $"seed {seed}: the opening hand is still 5 cards");
            }
        });
    }

    // ---------- Anger ----------

    private static void Anger(Check c)
    {
        c.Group("Anger: 6 damage, then a copy to the discard pile, then itself", () =>
        {
            var f = Solo("Anger");
            var anger = f.Hand[0].Id;
            f.Play("Anger", 0);
            c.Equal(94, f.Enemy(0).Hp, "6 damage");
            c.Equal(3, f.Energy, "costs 0");
            c.Sequence(["Anger", "Anger"], Titles(f.DiscardPile), "two Angers in the discard pile");
            c.That(f.DiscardPile[0].IsClone && f.DiscardPile[0].Id != anger, "the copy went in first");
            c.Equal(anger, f.DiscardPile[1].Id, "then the played card");
            c.Equal(1, f.History.Count(e => e.Kind == HistoryKind.CardGenerated && e.CardId == f.DiscardPile[0].Id), "the copy counts as a generated card");
            c.Equal(0, f.Hand.Count, "nothing goes to the hand");

            var g = Solo("Anger");
            g.Put("DefendIronclad", PileType.Discard);
            g.Play("Anger", 0);
            c.Sequence(["Defend", "Anger", "Anger"], Titles(g.DiscardPile), "the copy is added at the bottom of the discard pile, after what is there");
            c.That(g.DiscardPile[1].IsClone && !g.DiscardPile[2].IsClone, "copy, then card");
        });

        c.Group("Anger+: 8 damage, and the copy is upgraded too", () =>
        {
            var f = Solo("Anger+");
            f.Play("Anger+", 0);
            c.Equal(92, f.Enemy(0).Hp, "8 damage");
            c.Sequence(["Anger+", "Anger+"], Titles(f.DiscardPile), "the copy has the same upgrade");
            c.Equal("Deal 8 damage. Add a copy of this card to your discard pile.", Fight.CardText(f, f.DiscardPile[0], PileType.Discard), "the copy's text");
        });

        c.Group("Anger: Strength, Weak and Vulnerable; the copy keeps cost changes", () =>
        {
            var f = Solo("Anger", "Anger");
            f.ApplyPower<StrengthPower>(f.Player, 2, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            c.Equal("Deal 8 damage. Add a copy of this card to your discard pile.", HandText(f), "the hand text has Strength and not the target's Vulnerable");
            f.Play("Anger", 0);
            c.Equal(88, f.Enemy(0).Hp, "(6 + 2) x 1.5 = 12");
            f.ApplyPower<WeakPower>(f.Player, 1, f.Enemy(0));
            f.Play("Anger", 0);
            c.Equal(79, f.Enemy(0).Hp, "(6 + 2) x 0.75 x 1.5 = 9");

            var b = Solo("Anger");
            b.SetBlock(b.Enemy(0), 4);
            b.Play("Anger", 0);
            c.That(b.Enemy(0).Hp == 98 && b.Enemy(0).Block == 0, "4 of the 6 on Block");

            var g = Solo("Anger");
            g.Hand[0].SetCostThisCombat(2);
            g.Play("Anger", 0);
            c.Equal(1, g.Energy, "the changed cost was paid");
            c.Equal(2, g.CostOf(g.DiscardPile[0]), "the copy costs 2 as well");

            // "Until played" changes are still on the card while it resolves, so the copy has them; the original loses them afterwards.
            var h = Solo("Anger");
            h.Hand[0].SetCostUntilPlayed(1);
            h.Play("Anger", 0);
            c.Equal(1, h.CostOf(h.DiscardPile[0]), "the copy carries the until-played cost");
            c.Equal(0, h.CostOf(h.DiscardPile[1]), "the played card is back to 0");
        });

        c.Group("Anger: the copy is still made when the target died, unless that ended the fight", () =>
        {
            var f = Duo(5, 100, "Anger");
            f.Play("Anger", 0);
            c.That(f.Enemy(0).IsDead && !f.IsOver, "the target died, the fight goes on");
            c.Equal(2, f.DiscardPile.Count, "copy and card are in the discard pile");

            var g = Vs(new CheckDummy { Hp = 6 }, "Anger");
            g.Play("Anger", 0);
            c.That(g.IsOver && g.Won, "6 damage kills a 6 HP enemy and wins");
        });
    }

    // ---------- Ashen Strike ----------

    private static void AshenStrike(Check c)
    {
        c.Group("Ashen Strike: 6, plus 3 for each card in the exhaust pile", () =>
        {
            var f = Solo("AshenStrike", "AshenStrike");
            c.That(f.Hand[0].HasTag(CardTags.Strike), "it is a Strike");
            c.Equal("Deal 6 damage (6 plus 3 for each card in your exhaust pile).", HandText(f), "text with an empty exhaust pile");
            f.Play("Ashen Strike", 0);
            c.Equal(94, f.Enemy(0).Hp, "6 damage with nothing exhausted");
            c.Equal(2, f.Energy, "costs 1");
            c.Sequence(["Ashen Strike"], Titles(f.DiscardPile), "it goes to the discard pile");

            f.Put("Wound", PileType.Exhaust);
            f.Put("Wound", PileType.Exhaust);
            c.Equal("Deal 12 damage (6 plus 3 for each card in your exhaust pile).", HandText(f), "text with two exhausted cards");
            c.Equal(12m, f.DamageNumberOf(f.Hand[0]), "its damage number as other effects read it");
            f.Play("Ashen Strike", 0);
            c.Equal(82, f.Enemy(0).Hp, "6 + 3 x 2 = 12");
        });

        c.Group("Ashen Strike+: 4 per exhausted card, the base stays 6", () =>
        {
            var f = Solo("AshenStrike+", "AshenStrike+");
            f.Play("Ashen Strike+", 0);
            c.Equal(94, f.Enemy(0).Hp, "6 with nothing exhausted");
            f.Put("Wound", PileType.Exhaust);
            f.Put("Wound", PileType.Exhaust);
            f.Play("Ashen Strike+", 0);
            c.Equal(80, f.Enemy(0).Hp, "6 + 4 x 2 = 14");
        });

        c.Group("Ashen Strike: Strength is added once; a played Power card is not exhausted; the pile is counted at the hit", () =>
        {
            var f = Solo("AshenStrike");
            f.Put("Wound", PileType.Exhaust);
            f.Put("Wound", PileType.Exhaust);
            f.ApplyPower<StrengthPower>(f.Player, 2, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            c.Equal("Deal 14 damage (6 plus 3 for each card in your exhaust pile).", HandText(f), "the hand text adds Strength once");
            f.Play("Ashen Strike", 0);
            c.Equal(79, f.Enemy(0).Hp, "(6 + 6 + 2) x 1.5 = 21");

            var g = Solo("Cruelty", "AshenStrike");
            g.Play("Cruelty");
            g.Play("Ashen Strike", 0);
            c.Equal(94, g.Enemy(0).Hp, "a played Power card left the fight and does not count");

            // Dominate: 1 Vulnerable, 1 Strength, and it is exhausted: (6 + 3 + 1) x 1.5 = 15.
            var h = Solo("Dominate", "AshenStrike");
            h.Play("Dominate", 0);
            h.Play("Ashen Strike", 0);
            c.Equal(85, h.Enemy(0).Hp, "a card exhausted this turn counts");
        });
    }

    // ---------- Barricade ----------

    private static void Barricade(Check c)
    {
        c.Group("Barricade: Block carries over from turn to turn", () =>
        {
            var f = Vs(Hitter(4), "Barricade", "DefendIronclad");
            f.SetEnergy(4);
            f.Play("Barricade");
            c.Equal(1, f.Energy, "costs 3");
            c.That(f.Player.HasPower<BarricadePower>(), "Barricade is on");
            c.That(f.DiscardPile.Count == 0 && f.ExhaustPile.Count == 0, "the Power card is in no pile");
            c.Equal(0, f.History.CardsExhaustedThisTurn(f), "and was not exhausted");
            f.Play("Defend");
            f.EndTurn();
            c.Equal(1, f.Player.Block, "5 Block, 4 taken by the hit, 1 left at the start of turn 2");
            c.Equal(80, f.Player.Hp, "no HP lost");
            f.Play("Defend");
            f.Play("Defend");
            f.EndTurn();
            c.Equal(7, f.Player.Block, "1 + 5 + 5 - 4 = 7 at the start of turn 3");

            // The same without Barricade.
            var g = Vs(Hitter(4), "DefendIronclad");
            g.Play("Defend");
            g.EndTurn();
            c.Equal(0, g.Player.Block, "without Barricade the Block falls at the start of the turn");
        });

        c.Group("Barricade: the enemies still lose their Block; Barricade+ costs 2", () =>
        {
            var f = Vs(new CheckDummy { Steps = [new DummyStep("GUARD", Block: 7)] }, "Barricade+");
            f.Play("Barricade+");
            c.Equal(1, f.Energy, "the upgrade costs 2");
            f.EndTurn();
            c.Equal(7, f.Enemy(0).Block, "the enemy gained 7 Block");
            f.EndTurn();
            c.Equal(7, f.Enemy(0).Block, "its Block fell at the start of its turn before it gained 7 again");

            var g = Solo("Barricade", "Barricade");
            g.SetEnergy(6);
            g.Play("Barricade");
            g.Play("Barricade");
            g.SetBlock(g.Player, 9);
            g.EndTurn();
            c.Equal(9, g.Player.Block, "a second copy changes nothing");
            c.Equal("Block is not removed at the start of its turn.", g.Player.GetPower<BarricadePower>()!.Describe(), "the power's text");
        });
    }

    // ---------- Battle Trance ----------

    private static void BattleTrance(Check c)
    {
        c.Group("Battle Trance: draw 3, then No Draw", () =>
        {
            var f = Solo("BattleTrance");
            f.Play("Battle Trance");
            c.Sequence(["Strike", "Strike", "Strike"], f.HandTitles(), "the top three cards");
            c.Equal(2, f.DrawPile.Count, "two left");
            c.Equal(3, f.Energy, "costs 0");
            c.Equal(1, f.Player.PowerAmount<NoDrawPower>(), "No Draw 1");
            c.Equal(PowerType.Debuff, f.Player.GetPower<NoDrawPower>()!.Type, "a debuff");
            c.Sequence(["Battle Trance"], Titles(f.DiscardPile), "it goes to the discard pile");
        });

        c.Group("Battle Trance+: draw 4", () =>
        {
            var f = Solo("BattleTrance+");
            f.Play("Battle Trance+");
            c.Sequence(["Strike", "Strike", "Strike", "Defend"], f.HandTitles(), "the top four cards");
            c.That(f.Player.HasPower<NoDrawPower>(), "No Draw");
        });

        c.Group("No Draw: no other draw works this turn; it ends with the turn", () =>
        {
            var f = Solo("BattleTrance", "BattleTrance", "Slimed");
            f.Play("Battle Trance");
            c.Equal(5, f.Hand.Count, "Battle Trance, Slimed and three Strikes");
            f.Play("Battle Trance");
            c.Sequence(["Slimed", "Strike", "Strike", "Strike"], f.HandTitles(), "the second Battle Trance draws nothing");
            f.Play("Slimed");
            c.Sequence(["Strike", "Strike", "Strike"], f.HandTitles(), "nor does Slimed");
            c.Equal(2, f.DrawPile.Count, "the draw pile is untouched");
            f.EndTurn();
            c.That(!f.Player.HasPower<NoDrawPower>(), "No Draw is gone at the start of the next turn");
            c.Equal(5, f.Hand.Count, "and the hand draw is the usual five");
        });

        c.Group("No Draw: the hand draw at the start of the turn is never blocked", () =>
        {
            var hex = new CheckDummy { Steps = [new DummyStep("HEX", Then: (x, me) => x.ApplyPower<NoDrawPower>(x.Player, 1, me)), new DummyStep("IDLE")] };
            var f = CheckKit.VsDummy(hex);
            f.EndTurn();
            c.That(f.Player.HasPower<NoDrawPower>(), "No Draw from the enemy turn is still there");
            c.Sequence(["Strike", "Strike", "Strike", "Defend", "Defend"], f.HandTitles(), "the hand draw happened all the same");
            f.Put("Slimed", PileType.Hand);
            f.Play("Slimed");
            c.Equal(5, f.Hand.Count, "a draw by a card is blocked");
            c.Equal(0, f.DrawPile.Count, "and the discard pile was not shuffled in for it");
            f.EndTurn();
            c.That(!f.Player.HasPower<NoDrawPower>(), "it is removed at the end of its owner's turn");
        });

        c.Group("Battle Trance: the hand limit, the reshuffle and Artifact", () =>
        {
            var f = Solo("BattleTrance", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound");
            f.Play("Battle Trance");
            c.Equal(10, f.Hand.Count, "nine cards in hand: only one more fits");
            c.Equal(4, f.DrawPile.Count, "the other two stay on the draw pile");

            var g = Solo("BattleTrance");
            g.Clear(PileType.Draw);
            g.Put("DefendIronclad", PileType.Discard);
            g.Put("DefendIronclad", PileType.Discard);
            var shuffles = g.Rng.Shuffle.Counter;
            g.Play("Battle Trance");
            c.Sequence(["Defend", "Defend"], g.HandTitles(), "the discard pile was shuffled in and both cards drawn");
            c.Equal(shuffles + 1, g.Rng.Shuffle.Counter, "one reshuffle of two cards");
            c.Sequence(["Battle Trance"], Titles(g.DiscardPile), "the card being played was not part of the reshuffle");

            var h = Solo("BattleTrance", "BattleTrance");
            h.ApplyPower<ArtifactPower>(h.Player, 1, h.Player);
            h.Play("Battle Trance");
            c.Equal(4, h.Hand.Count, "three cards drawn");
            c.That(!h.Player.HasPower<NoDrawPower>() && !h.Player.HasPower<ArtifactPower>(), "Artifact stopped No Draw and is used up");
            h.Play("Battle Trance");
            c.Equal(6, h.Hand.Count, "so the second one draws three more (two from the draw pile, one after a reshuffle)");
            c.That(h.Player.HasPower<NoDrawPower>(), "and this time No Draw lands");
        });
    }

    // ---------- Blood Wall ----------

    private static void BloodWall(Check c)
    {
        c.Group("Blood Wall: lose 2 HP, then 16 Block", () =>
        {
            var f = Solo("BloodWall");
            f.Play("Blood Wall");
            c.Equal(78, f.Player.Hp, "2 HP lost");
            c.Equal(16, f.Player.Block, "16 Block");
            c.Equal(1, f.Energy, "costs 2");
            var loss = FirstIndex(f, e => e.Kind == HistoryKind.DamageReceived && e.ActorId == f.Player.Id);
            var block = FirstIndex(f, e => e.Kind == HistoryKind.BlockGained && e.ActorId == f.Player.Id);
            c.That(loss >= 0 && block > loss, "the HP loss comes before the Block");
            c.Equal(Props.CardHpLoss, f.History.Entries.ElementAt(loss).Props, "card HP loss: unblockable, unpowered, from a card");
        });

        c.Group("Blood Wall+: 20 Block, still 2 HP", () =>
        {
            var f = Solo("BloodWall+");
            f.Play("Blood Wall+");
            c.Equal(78, f.Player.Hp, "2 HP lost");
            c.Equal(20, f.Player.Block, "20 Block");
            c.Equal(1, f.Energy, "costs 2");
        });

        c.Group("Blood Wall: Block does not absorb the loss; Frail and Dexterity change the Block only", () =>
        {
            var f = Solo("BloodWall");
            f.SetBlock(f.Player, 10);
            f.ApplyPower<VulnerablePower>(f.Player, 1, f.Enemy(0));
            f.ApplyPower<StrengthPower>(f.Player, 5, f.Player);
            f.Play("Blood Wall");
            c.Equal(78, f.Player.Hp, "2 HP lost through 10 Block, with Vulnerable and Strength on the player");
            c.Equal(26, f.Player.Block, "10 + 16");

            var g = Solo("BloodWall", "BloodWall");
            g.SetEnergy(4);
            g.ApplyPower<FrailPower>(g.Player, 1, g.Enemy(0));
            c.Equal("Lose 2 HP. Gain 12 Block.", HandText(g), "the hand text has Frail");
            g.Play("Blood Wall");
            c.Equal(12, g.Player.Block, "16 x 0.75 = 12");
            g.ApplyPower<DexterityPower>(g.Player, 2, g.Player);
            g.Play("Blood Wall");
            c.Equal(25, g.Player.Block, "(16 + 2) x 0.75 = 13.5, 13 more");
            c.Equal(76, g.Player.Hp, "2 HP each");
        });

        c.Group("Blood Wall: at 2 HP it kills, and no Block is gained", () =>
        {
            var f = AtHp(2, "BloodWall");
            f.Play("Blood Wall");
            c.That(f.IsOver && !f.Won, "the fight is lost");
            c.Equal(0, f.Player.Block, "nothing after the death");
        });
    }

    // ---------- Bloodletting ----------

    private static void Bloodletting(Check c)
    {
        c.Group("Bloodletting: lose 3 HP, gain 2 energy", () =>
        {
            var f = Solo("Bloodletting");
            f.Play("Bloodletting");
            c.Equal(77, f.Player.Hp, "3 HP lost");
            c.Equal(5, f.Energy, "3 + 2 energy, above the maximum");
            c.Equal(3, f.MaxEnergy, "the maximum is unchanged");
            c.Sequence(["Bloodletting"], Titles(f.DiscardPile), "it goes to the discard pile");
        });

        c.Group("Bloodletting+: 3 energy", () =>
        {
            var f = Solo("Bloodletting+");
            f.Play("Bloodletting+");
            c.Equal(77, f.Player.Hp, "3 HP lost");
            c.Equal(6, f.Energy, "3 + 3 energy");
        });

        c.Group("Bloodletting: 10 Block and Vulnerable change nothing (rules-damage row 26); at 3 HP it kills", () =>
        {
            var f = Solo("Bloodletting");
            f.SetBlock(f.Player, 10);
            f.ApplyPower<VulnerablePower>(f.Player, 1, f.Enemy(0));
            f.Play("Bloodletting");
            c.Equal(77, f.Player.Hp, "HP -3");
            c.Equal(10, f.Player.Block, "Block stays 10");

            var g = AtHp(3, "Bloodletting");
            g.Play("Bloodletting");
            c.That(g.IsOver && !g.Won, "the fight is lost");
            c.Equal(3, g.Energy, "no energy after the death");
        });
    }

    // ---------- Bludgeon ----------

    private static void Bludgeon(Check c)
    {
        c.Group("Bludgeon: 32 damage for 3 energy", () =>
        {
            var f = Solo("Bludgeon");
            f.Play("Bludgeon", 0);
            c.Equal(68, f.Enemy(0).Hp, "32 damage");
            c.Equal(0, f.Energy, "costs 3");

            var g = Solo("Bludgeon");
            g.SetEnergy(2);
            c.That(!g.Execute("play:0>e0").Ok, "not playable with 2 energy");
        });

        c.Group("Bludgeon+: 42 damage", () =>
        {
            var f = Solo("Bludgeon+");
            f.Play("Bludgeon+", 0);
            c.Equal(58, f.Enemy(0).Hp, "42 damage");
            c.Equal(0, f.Energy, "still costs 3");
        });

        c.Group("Bludgeon: Strength, Weak, Vulnerable and Block", () =>
        {
            var f = Solo("Bludgeon");
            f.ApplyPower<StrengthPower>(f.Player, 2, f.Player);
            f.ApplyPower<WeakPower>(f.Player, 1, f.Enemy(0));
            c.Equal("Deal 25 damage.", HandText(f), "the hand text: (32 + 2) x 0.75 = 25.5");
            f.Play("Bludgeon", 0);
            c.Equal(75, f.Enemy(0).Hp, "25.5 is 25 HP");

            var g = Solo("Bludgeon");
            g.ApplyPower<VulnerablePower>(g.Enemy(0), 1, g.Player);
            g.SetBlock(g.Enemy(0), 10);
            g.Play("Bludgeon", 0);
            c.Equal(62, g.Enemy(0).Hp, "32 x 1.5 = 48, 10 of it on Block");
            c.Equal(0, g.Enemy(0).Block, "the Block is gone");
        });
    }

    // ---------- Body Slam ----------

    private static void BodySlam(Check c)
    {
        c.Group("Body Slam: damage equal to the player's Block, which is kept", () =>
        {
            var f = Solo("BodySlam", "BodySlam");
            c.Equal("Deal damage equal to your Block (0 right now).", HandText(f), "text with no Block");
            f.Play("Body Slam", 0);
            c.Equal(100, f.Enemy(0).Hp, "0 Block: 0 damage");
            c.Equal(2, f.Energy, "costs 1");
            f.SetBlock(f.Player, 12);
            c.Equal("Deal damage equal to your Block (12 right now).", HandText(f), "text with 12 Block");
            f.Play("Body Slam", 0);
            c.Equal(88, f.Enemy(0).Hp, "12 damage");
            c.Equal(12, f.Player.Block, "the Block is not spent");
        });

        c.Group("Body Slam+: costs 0", () =>
        {
            var f = Solo("BodySlam+");
            f.SetBlock(f.Player, 12);
            f.Play("Body Slam+", 0);
            c.Equal(88, f.Enemy(0).Hp, "12 damage");
            c.Equal(3, f.Energy, "costs 0");
        });

        c.Group("Body Slam: Strength on top of the Block, Weak, Vulnerable; the Block is read at the hit", () =>
        {
            var f = Solo("BodySlam");
            f.ApplyPower<StrengthPower>(f.Player, 3, f.Player);
            f.Play("Body Slam", 0);
            c.Equal(97, f.Enemy(0).Hp, "0 Block still attacks for Strength: 3");

            var g = Solo("BodySlam");
            g.SetBlock(g.Player, 7);
            g.ApplyPower<StrengthPower>(g.Player, 2, g.Player);
            g.ApplyPower<VulnerablePower>(g.Enemy(0), 1, g.Player);
            c.Equal("Deal damage equal to your Block (9 right now).", HandText(g), "the hand text: Block plus Strength, no Vulnerable");
            g.Play("Body Slam", 0);
            c.Equal(87, g.Enemy(0).Hp, "(7 + 2) x 1.5 = 13.5");

            var h = Solo("BodySlam");
            h.SetBlock(h.Player, 12);
            h.ApplyPower<WeakPower>(h.Player, 1, h.Enemy(0));
            h.Play("Body Slam", 0);
            c.Equal(91, h.Enemy(0).Hp, "12 x 0.75 = 9");

            var k = Solo("DefendIronclad", "BodySlam");
            k.Play("Defend");
            k.Play("Body Slam", 0);
            c.Equal(95, k.Enemy(0).Hp, "Block gained this turn counts: 5");
        });
    }

    // ---------- Brand ----------

    private static void Brand(Check c)
    {
        c.Group("Brand: lose 1 HP, exhaust a chosen card, gain 1 Strength", () =>
        {
            var f = Solo("Brand", "StrikeIronclad", "DefendIronclad");
            f.Play("Brand");
            c.Equal("hand_select", f.Snapshot().Screen, "the player chooses the card");
            c.Equal(79, f.Player.Hp, "the HP is lost before the question");
            c.Equal(0, f.Player.PowerAmount<StrengthPower>(), "the Strength comes after it");
            c.Sequence(["select:0", "select:1"], f.LegalActions().Select(a => a.Id), "either hand card; Brand itself is not in the hand");
            f.Do("select:1", "confirm");
            c.Equal("combat", f.Snapshot().Screen, "back in the fight");
            c.Sequence(["Defend"], Titles(f.ExhaustPile), "the chosen card is exhausted");
            c.Sequence(["Strike"], f.HandTitles(), "the other stays");
            c.Equal(1, f.Player.PowerAmount<StrengthPower>(), "1 Strength");
            c.Equal(79, f.Player.Hp, "1 HP lost, once");
            c.Equal(3, f.Energy, "costs 0");
            c.Sequence(["Brand"], Titles(f.DiscardPile), "Brand itself is discarded, not exhausted");
        });

        c.Group("Brand+: 2 Strength", () =>
        {
            var f = Solo("Brand+", "StrikeIronclad", "DefendIronclad");
            f.Play("Brand+");
            f.Do("select:0", "confirm");
            c.Sequence(["Strike"], Titles(f.ExhaustPile), "the chosen card is exhausted");
            c.Equal(2, f.Player.PowerAmount<StrengthPower>(), "2 Strength");
            c.Equal(79, f.Player.Hp, "1 HP lost");
        });

        c.Group("Brand: one card is taken without asking, an empty hand skips the exhaust, Block does not absorb the loss", () =>
        {
            var f = Solo("Brand", "StrikeIronclad");
            f.SetBlock(f.Player, 5);
            f.Play("Brand");
            c.Equal("combat", f.Snapshot().Screen, "no question for a single card");
            c.Sequence(["Strike"], Titles(f.ExhaustPile), "it is exhausted");
            c.Equal(1, f.Player.PowerAmount<StrengthPower>(), "1 Strength");
            c.That(f.Player.Hp == 79 && f.Player.Block == 5, "1 HP lost, the 5 Block untouched");

            var g = Solo("Brand", "Brand");
            g.Play("Brand");
            c.Equal("combat", g.Snapshot().Screen, "the other Brand is the only candidate");
            c.Sequence(["Brand"], Titles(g.ExhaustPile), "and is exhausted");

            var h = Solo("Brand");
            h.Play("Brand");
            c.Equal(0, h.ExhaustPile.Count, "an empty hand: nothing to exhaust");
            c.Equal(1, h.Player.PowerAmount<StrengthPower>(), "the Strength is gained anyway");
            c.Equal(79, h.Player.Hp, "and the HP is lost");
        });

        c.Group("Brand: the exhaust hooks run before the Strength; at 1 HP it kills", () =>
        {
            var f = Solo("Brand", "Wound");
            f.ApplyPower<DarkEmbracePower>(f.Player, 1, f.Player);
            f.Play("Brand");
            c.Sequence(["Strike"], f.HandTitles(), "Dark Embrace drew a card for the exhaust");
            var draw = FirstIndex(f, e => e.Kind == HistoryKind.CardDrawn);
            var strength = FirstIndex(f, e => e.Kind == HistoryKind.PowerReceived && e.Name == "StrengthPower");
            c.That(draw >= 0 && strength > draw, "and it did so before the Strength was applied");

            var g = AtHp(1, "Brand", "StrikeIronclad", "DefendIronclad");
            g.Play("Brand");
            c.That(g.IsOver && !g.Won, "the fight is lost");
            c.Equal("arena_over", g.Snapshot().Screen, "no card choice is shown");
        });
    }

    // ---------- Break ----------

    private static void Break(Check c)
    {
        c.Group("Break: 20 damage, then 5 Vulnerable", () =>
        {
            var f = Solo("Break");
            f.Play("Break", 0);
            c.Equal(80, f.Enemy(0).Hp, "20 damage: its own Vulnerable does not raise the hit");
            c.Equal(5, f.Enemy(0).PowerAmount<VulnerablePower>(), "5 Vulnerable");
            c.Equal(2, f.Energy, "costs 1");
        });

        c.Group("Break+: 30 damage, 7 Vulnerable", () =>
        {
            var f = Solo("Break+");
            f.Play("Break+", 0);
            c.Equal(70, f.Enemy(0).Hp, "30 damage");
            c.Equal(7, f.Enemy(0).PowerAmount<VulnerablePower>(), "7 Vulnerable");
            c.Equal(2, f.Energy, "costs 1");
        });

        c.Group("Break: into Vulnerable, into Artifact, onto a target that dies", () =>
        {
            var f = Solo("Break");
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            f.Play("Break", 0);
            c.Equal(70, f.Enemy(0).Hp, "20 x 1.5 = 30");
            c.Equal(6, f.Enemy(0).PowerAmount<VulnerablePower>(), "1 + 5 Vulnerable");

            var w = Solo("Break");
            w.ApplyPower<StrengthPower>(w.Player, 1, w.Player);
            w.ApplyPower<WeakPower>(w.Player, 1, w.Enemy(0));
            c.Equal("Deal 15 damage. Apply 5 Vulnerable.", HandText(w), "the hand text: (20 + 1) x 0.75 = 15.75");
            w.Play("Break", 0);
            c.Equal(85, w.Enemy(0).Hp, "15.75 is 15 HP");

            var g = Vs(new CheckDummy { OnAdded = (x, me) => x.ApplyPower<ArtifactPower>(me, 1, me) }, "Break");
            g.Play("Break", 0);
            c.Equal(80, g.Enemy(0).Hp, "the damage lands");
            c.That(!g.Enemy(0).HasPower<VulnerablePower>() && !g.Enemy(0).HasPower<ArtifactPower>(), "Artifact stops the Vulnerable and is used up");

            var h = Duo(15, 100, "Break");
            h.Play("Break", 0);
            c.That(h.Enemy(0).IsDead && !h.IsOver, "the target died, the fight goes on");
            c.That(!h.Enemy(1).HasPower<VulnerablePower>(), "nobody else gets the Vulnerable");
        });
    }

    // ---------- Breakthrough ----------

    private static void Breakthrough(Check c)
    {
        c.Group("Breakthrough: lose 1 HP, then 9 damage to every enemy", () =>
        {
            var f = Duo(100, 100, "Breakthrough");
            c.Sequence(["play:0", "end_turn"], f.LegalActions().Select(a => a.Id), "no target is chosen");
            f.Play("Breakthrough");
            c.Equal(79, f.Player.Hp, "1 HP lost");
            c.That(f.Enemy(0).Hp == 91 && f.Enemy(1).Hp == 91, "9 damage to each enemy");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(f.Player.Id, f.History.Entries.First(e => e.Kind == HistoryKind.DamageReceived).ActorId, "the HP loss comes first");
        });

        c.Group("Breakthrough+: 13 damage", () =>
        {
            var f = Duo(100, 100, "Breakthrough+");
            f.Play("Breakthrough+");
            c.Equal(79, f.Player.Hp, "1 HP lost");
            c.That(f.Enemy(0).Hp == 87 && f.Enemy(1).Hp == 87, "13 damage to each enemy");
        });

        c.Group("Breakthrough: each enemy is modified and blocked on its own; Block does not absorb the HP loss", () =>
        {
            var f = Duo(100, 100, "Breakthrough");
            f.ApplyPower<StrengthPower>(f.Player, 2, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            f.SetBlock(f.Enemy(1), 5);
            f.SetBlock(f.Player, 5);
            c.Equal("Lose 1 HP. Deal 11 damage to ALL enemies.", HandText(f), "the hand text has Strength");
            f.Play("Breakthrough");
            c.Equal(84, f.Enemy(0).Hp, "(9 + 2) x 1.5 = 16.5 on the Vulnerable one");
            c.Equal(94, f.Enemy(1).Hp, "11 on the other, 5 of it on Block");
            c.Equal(0, f.Enemy(1).Block, "its Block is gone");
            c.That(f.Player.Hp == 79 && f.Player.Block == 5, "1 HP lost, the player's 5 Block untouched");
        });

        c.Group("Breakthrough: if the HP loss kills the player the attack does not happen", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(1).Enemy(new CheckDummy { Hp = 5 }).Enemy(new CheckDummy { Hp = 5 }).Start();
            SetHand(f, ["Breakthrough"]);
            f.Play("Breakthrough");
            c.That(f.IsOver && !f.Won, "the fight is lost");
            c.That(f.Enemy(0).Hp == 5 && f.Enemy(1).Hp == 5, "no enemy was hit");

            var g = Duo(9, 9, "Breakthrough");
            g.Play("Breakthrough");
            c.That(g.IsOver && g.Won, "9 damage kills both 9 HP enemies and wins");
        });
    }

    // ---------- Bully ----------

    private static void Bully(Check c)
    {
        c.Group("Bully: 4, plus 2 for each Vulnerable on the target", () =>
        {
            var f = Solo("Bully", "Bully");
            f.Play("Bully", 0);
            c.Equal(96, f.Enemy(0).Hp, "4 damage without Vulnerable");
            c.Equal(3, f.Energy, "costs 0");
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 2, f.Player);
            c.Equal("Deal 4 damage, plus 2 for each Vulnerable on the enemy.", HandText(f), "at rest the text shows the base only, as in the game");
            c.Equal(4m, f.DamageNumberOf(f.Hand[0]), "and so does its damage number as other effects read it");
            f.Play("Bully", 0);
            c.Equal(84, f.Enemy(0).Hp, "(4 + 2 x 2) x 1.5 = 12: the stacks count twice");
            c.Equal(2, f.Enemy(0).PowerAmount<VulnerablePower>(), "it applies no Vulnerable itself");
        });

        c.Group("Bully+: 3 per Vulnerable, the base stays 4", () =>
        {
            var f = Solo("Bully+", "Bully+");
            f.Play("Bully+", 0);
            c.Equal(96, f.Enemy(0).Hp, "4 damage without Vulnerable");
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 2, f.Player);
            f.Play("Bully+", 0);
            c.Equal(81, f.Enemy(0).Hp, "(4 + 3 x 2) x 1.5 = 15");
        });

        c.Group("Bully: Strength and Weak; the target's own Vulnerable is what counts", () =>
        {
            var f = Solo("Bully");
            f.ApplyPower<StrengthPower>(f.Player, 1, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 3, f.Player);
            c.Equal("Deal 5 damage, plus 2 for each Vulnerable on the enemy.", HandText(f), "the hand text: base plus Strength");
            f.Play("Bully", 0);
            c.Equal(84, f.Enemy(0).Hp, "(4 + 6 + 1) x 1.5 = 16.5");

            var g = Solo("Bully");
            g.ApplyPower<WeakPower>(g.Player, 1, g.Enemy(0));
            g.ApplyPower<VulnerablePower>(g.Enemy(0), 2, g.Player);
            g.Play("Bully", 0);
            c.Equal(91, g.Enemy(0).Hp, "(4 + 4) x 0.75 x 1.5 = 9");

            var h = Duo(100, 100, "Bully", "Bully");
            h.ApplyPower<VulnerablePower>(h.Enemy(0), 1, h.Player);
            h.Play("Bully", 1);
            c.Equal(96, h.Enemy(1).Hp, "4 on the enemy without Vulnerable");
            h.Play("Bully", 0);
            c.Equal(91, h.Enemy(0).Hp, "(4 + 2) x 1.5 = 9 on the Vulnerable one");
        });
    }

    // ---------- Burning Pact ----------

    private static void BurningPact(Check c)
    {
        c.Group("Burning Pact: exhaust a chosen card, then draw 2", () =>
        {
            var f = Solo("BurningPact", "StrikeIronclad", "DefendIronclad");
            f.Play("Burning Pact");
            c.Equal("hand_select", f.Snapshot().Screen, "the player chooses the card");
            c.Equal(5, f.DrawPile.Count, "nothing is drawn before the answer");
            c.Equal(2, f.Energy, "costs 1");
            f.Do("select:1", "confirm");
            c.Sequence(["Defend"], Titles(f.ExhaustPile), "the chosen card is exhausted");
            c.Sequence(["Strike", "Strike", "Strike"], f.HandTitles(), "two cards drawn");
            c.Equal(3, f.DrawPile.Count, "exactly two");
            c.Sequence(["Burning Pact"], Titles(f.DiscardPile), "it is discarded itself");
        });

        c.Group("Burning Pact+: draw 3", () =>
        {
            var f = Solo("BurningPact+", "Bash", "DefendIronclad");
            f.Play("Burning Pact+");
            f.Do("select:0", "confirm");
            c.Sequence(["Bash"], Titles(f.ExhaustPile), "the chosen card is exhausted");
            c.Sequence(["Defend", "Strike", "Strike", "Strike"], f.HandTitles(), "three cards drawn");
            c.Equal(2, f.Energy, "costs 1");
        });

        c.Group("Burning Pact: one card is taken without asking; an empty hand still draws; No Draw only stops the draw", () =>
        {
            var f = Solo("BurningPact", "Wound");
            f.Play("Burning Pact");
            c.Equal("combat", f.Snapshot().Screen, "no question for a single card");
            c.Sequence(["Wound"], Titles(f.ExhaustPile), "it is exhausted");
            c.Sequence(["Strike", "Strike"], f.HandTitles(), "two cards drawn");

            var g = Solo("BurningPact");
            g.Play("Burning Pact");
            c.Equal(0, g.ExhaustPile.Count, "an empty hand: nothing to exhaust");
            c.Sequence(["Strike", "Strike"], g.HandTitles(), "two cards drawn all the same");

            var h = Solo("BurningPact", "Wound");
            h.ApplyPower<NoDrawPower>(h.Player, 1, h.Player);
            h.Play("Burning Pact");
            c.Sequence(["Wound"], Titles(h.ExhaustPile), "with No Draw the exhaust still happens");
            c.Equal(0, h.Hand.Count, "and nothing is drawn");
        });

        c.Group("Burning Pact: the exhaust hooks finish before the draw", () =>
        {
            var f = Solo("BurningPact", "Wound");
            f.ApplyPower<DarkEmbracePower>(f.Player, 1, f.Player);
            f.Play("Burning Pact");
            c.Sequence(["Strike", "Strike", "Strike"], f.HandTitles(), "Dark Embrace's card and then the two of Burning Pact");
            c.Equal(2, f.DrawPile.Count, "three cards drawn in all");
        });
    }

    // ---------- Cascade ----------

    private static void Cascade(Check c)
    {
        c.Group("Cascade: X = all energy; the top X cards are played for free", () =>
        {
            var f = Solo("Cascade");
            c.Equal("X", f.Snapshot().State["hand"]![0]!["cost"]!.GetValue<string>(), "the cost reads X");
            f.Play("Cascade");
            c.Equal(0, f.Energy, "all 3 energy spent");
            c.Equal(82, f.Enemy(0).Hp, "three Strikes: 18 damage");
            c.Sequence(["Strike", "Strike", "Strike", "Cascade"], Titles(f.DiscardPile), "each card goes to its usual pile, Cascade last");
            c.Sequence(["Defend", "Defend"], Titles(f.DrawPile), "the rest of the draw pile");
            c.Equal(3, f.Rng.CombatTargets.Counter, "one target roll per Strike, also with a single enemy");
            c.Equal(1, f.CardsPlayed, "one card played by hand");
        });

        c.Group("Cascade+: X + 1 cards", () =>
        {
            var f = Solo("Cascade+");
            f.Play("Cascade+");
            c.Equal(0, f.Energy, "all 3 energy spent");
            c.Equal(82, f.Enemy(0).Hp, "three Strikes");
            c.Equal(5, f.Player.Block, "and a Defend: four cards");
            c.Sequence(["Defend"], Titles(f.DrawPile), "one card left");
        });

        c.Group("Cascade: playable at 0 energy; X can be 0 or 1", () =>
        {
            var f = Solo("Cascade");
            f.SetEnergy(0);
            f.Play("Cascade");
            c.Equal(100, f.Enemy(0).Hp, "X = 0 plays nothing");
            c.Equal(5, f.DrawPile.Count, "the draw pile is untouched");
            c.Sequence(["Cascade"], Titles(f.DiscardPile), "Cascade is discarded");

            var g = Solo("Cascade+");
            g.SetEnergy(0);
            g.Play("Cascade+");
            c.Equal(94, g.Enemy(0).Hp, "upgraded, X = 0 still plays one card");

            var h = Solo("Cascade");
            h.SetEnergy(1);
            h.Play("Cascade");
            c.Equal(94, h.Enemy(0).Hp, "X = 1: one Strike");
            c.Equal(0, h.Energy, "the energy is spent");
        });

        c.Group("Cascade: the discard pile is shuffled in when the draw pile runs dry, without Cascade; it stops when both are empty", () =>
        {
            var f = Solo("Cascade");
            f.Clear(PileType.Draw);
            f.Put("StrikeIronclad", PileType.Draw);
            f.Put("DefendIronclad", PileType.Discard);
            f.Put("DefendIronclad", PileType.Discard);
            f.Play("Cascade");
            c.Equal(94, f.Enemy(0).Hp, "the Strike");
            c.Equal(10, f.Player.Block, "and both Defends from the reshuffled discard pile");
            c.Sequence(["Strike", "Defend", "Defend", "Cascade"], Titles(f.DiscardPile), "played in the order taken; Cascade was not reshuffled");

            var g = Solo("Cascade");
            g.Clear(PileType.Draw);
            g.Put("StrikeIronclad", PileType.Draw);
            g.Play("Cascade");
            c.Equal(94, g.Enemy(0).Hp, "one card to take: one card played");
            c.Sequence(["Strike", "Cascade"], Titles(g.DiscardPile), "and no more");
        });

        c.Group("Cascade: an Unplayable card moves on unplayed; all cards leave the draw pile before the first resolves", () =>
        {
            var f = Solo("Cascade");
            f.Put("Wound", PileType.Draw, PilePosition.Top);
            f.SetEnergy(1);
            f.Play("Cascade");
            c.Sequence(["Wound", "Cascade"], Titles(f.DiscardPile), "the Wound goes to the discard pile");
            c.Equal(100, f.Enemy(0).Hp, "nothing else happened");

            var g = Solo("Cascade");
            g.Clear(PileType.Draw);
            foreach (var card in new[] { "Slimed", "StrikeIronclad", "DefendIronclad", "Bash" }) g.Put(card, PileType.Draw);
            g.SetEnergy(2);
            g.Play("Cascade");
            c.Sequence(["Defend"], g.HandTitles(), "Slimed drew the card below the two that were taken");
            c.Sequence(["Slimed"], Titles(g.ExhaustPile), "Slimed was exhausted by its own keyword");
            c.Equal(94, g.Enemy(0).Hp, "and the Strike was played");
            c.Sequence(["Bash"], Titles(g.DrawPile), "Bash is left");
        });

        c.Group("Cascade: a card a hook forbids moves on unplayed; the loop stops when the player dies", () =>
        {
            var f = Solo("Cascade");
            f.ApplyPower<Forbid>(f.Player, 1, f.Enemy(0));
            f.Put("Dominate", PileType.Draw, PilePosition.Top);
            f.SetEnergy(2);
            f.Play("Cascade");
            c.Equal(100, f.Enemy(0).Hp, "the Strike was not played");
            c.That(!f.Enemy(0).HasPower<VulnerablePower>(), "nor was Dominate");
            c.Sequence(["Dominate"], Titles(f.ExhaustPile), "an Exhaust card goes to the exhaust pile unplayed");
            c.Sequence(["Strike", "Cascade"], Titles(f.DiscardPile), "the other to the discard pile");
            c.Equal(0, f.Rng.CombatTargets.Counter, "no target is rolled for a forbidden card");

            var g = AtHp(3, "Cascade");
            g.Put("Bloodletting", PileType.Draw, PilePosition.Top);
            g.Play("Cascade");
            c.That(g.IsOver && !g.Won, "Bloodletting killed the player");
            c.Equal(100, g.Enemy(0).Hp, "the Strikes after it were not played");
        });

        c.Group("Cascade: a random target per enemy-targeted card, from the CombatTargets stream", () =>
        {
            var f = Duo(100, 100, "Cascade");
            var rng = f.Rng.CombatTargets.Clone();
            var before = f.Rng.CombatTargets.Counter;
            var expected = new[] { 100, 100 };
            for (var k = 0; k < 3; k++) expected[rng.NextInt(0, 2)] -= 6;
            f.Play("Cascade");
            c.Equal(expected[0], f.Enemy(0).Hp, "Left took the Strikes rolled onto it");
            c.Equal(expected[1], f.Enemy(1).Hp, "Right took the others");
            c.Equal(before + 3, f.Rng.CombatTargets.Counter, "three rolls");

            var g = Vs(new CheckDummy { Hp = 6 }, "Cascade");
            g.Play("Cascade");
            c.That(g.IsOver && g.Won, "the first Strike kills the only enemy and the fight is won");
        });

        c.Group("Cascade: an X card played by it uses the energy at hand as X without spending it", () =>
        {
            var f = Solo("Cascade");
            f.Put("Cascade", PileType.Draw, PilePosition.Top);
            f.Put("Bloodletting", PileType.Draw, PilePosition.Top);
            // Outer X = 3: Bloodletting (3 HP for 2 energy), the inner Cascade (X = 2: two Strikes), a Strike.
            f.Play("Cascade");
            c.Equal(77, f.Player.Hp, "Bloodletting was played");
            c.Equal(82, f.Enemy(0).Hp, "three Strikes in all");
            c.Equal(2, f.Energy, "Bloodletting's energy is still there: the inner Cascade spent nothing");
            c.Sequence(["Bloodletting", "Strike", "Strike", "Cascade", "Strike", "Cascade"], Titles(f.DiscardPile), "the order of resolution");
            c.Sequence(["Defend", "Defend"], Titles(f.DrawPile), "what is left");
        });

        c.Group("Cascade: a card it plays can ask the player", () =>
        {
            var f = Solo("Cascade", "StrikeIronclad", "DefendIronclad");
            f.Put("BurningPact", PileType.Draw, PilePosition.Top);
            f.SetEnergy(1);
            f.Play("Cascade");
            c.Equal("hand_select", f.Snapshot().Screen, "Burning Pact asks for a card");
            f.Do("select:0", "confirm");
            c.Sequence(["Strike"], Titles(f.ExhaustPile), "the chosen card is exhausted");
            c.Sequence(["Defend", "Strike", "Strike"], f.HandTitles(), "and two cards are drawn");
            c.Sequence(["Burning Pact", "Cascade"], Titles(f.DiscardPile), "both end in the discard pile");
            c.Equal(0, f.Energy, "the energy was spent once");
        });
    }

    // ---------- Cinder ----------

    private static void Cinder(Check c)
    {
        c.Group("Cinder: 18 damage, then a random hand card is exhausted", () =>
        {
            var f = Solo("Cinder", "StrikeIronclad");
            f.Play("Cinder", 0);
            c.Equal(82, f.Enemy(0).Hp, "18 damage");
            c.Equal(1, f.Energy, "costs 2");
            c.Sequence(["Strike"], Titles(f.ExhaustPile), "the only hand card is exhausted");
            c.Sequence(["Cinder"], Titles(f.DiscardPile), "Cinder itself is discarded");
            c.Equal(1, f.Rng.CombatCardSelection.Counter, "one number, also for a single candidate");
            var hit = FirstIndex(f, e => e.Kind == HistoryKind.DamageReceived);
            var exhaust = FirstIndex(f, e => e.Kind == HistoryKind.CardExhausted);
            c.That(hit >= 0 && exhaust > hit, "the hit comes before the exhaust");

            var g = Solo("Cinder");
            g.Play("Cinder", 0);
            c.Equal(82, g.Enemy(0).Hp, "18 damage");
            c.Equal(0, g.ExhaustPile.Count, "an empty hand: nothing is exhausted");
            c.Equal(0, g.Rng.CombatCardSelection.Counter, "and no number is drawn");
        });

        c.Group("Cinder+: 24 damage", () =>
        {
            var f = Solo("Cinder+", "Wound");
            f.Play("Cinder+", 0);
            c.Equal(76, f.Enemy(0).Hp, "24 damage");
            c.Equal(1, f.Energy, "costs 2");
            c.Sequence(["Wound"], Titles(f.ExhaustPile), "a hand card is exhausted");
        });

        c.Group("Cinder: the card is picked uniformly from the hand on the CombatCardSelection stream", () =>
        {
            var f = Solo("Cinder", "StrikeIronclad", "DefendIronclad", "Bash", "Wound");
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            var rng = f.Rng.CombatCardSelection.Clone();
            var expected = new[] { "Strike", "Defend", "Bash", "Wound" }[rng.NextInt(0, 4)];
            f.Play("Cinder", 0);
            c.Equal(73, f.Enemy(0).Hp, "18 x 1.5 = 27");
            c.Sequence([expected], Titles(f.ExhaustPile), "the card the stream names, of the four left in hand");
            c.Equal(3, f.Hand.Count, "three stay");
            c.Equal(1, f.Rng.CombatCardSelection.Counter, "one number");
        });

        c.Group("Cinder: the exhaust still happens when the target died, and its hooks run", () =>
        {
            var w = Solo("Cinder");
            w.ApplyPower<StrengthPower>(w.Player, 2, w.Player);
            w.ApplyPower<WeakPower>(w.Player, 1, w.Enemy(0));
            c.Equal("Deal 15 damage. Exhaust a random card in your hand.", HandText(w), "the hand text: (18 + 2) x 0.75 = 15");
            w.Play("Cinder", 0);
            c.Equal(85, w.Enemy(0).Hp, "15 damage");

            var f = Duo(10, 100, "Cinder", "Wound");
            f.ApplyPower<DarkEmbracePower>(f.Player, 1, f.Player);
            f.Play("Cinder", 0);
            c.That(f.Enemy(0).IsDead && !f.IsOver, "the target died, the fight goes on");
            c.Sequence(["Wound"], Titles(f.ExhaustPile), "the hand card is exhausted");
            c.Sequence(["Strike"], f.HandTitles(), "and Dark Embrace drew for it");

            var g = Vs(new CheckDummy { Hp = 18 }, "Cinder", "Wound");
            g.Play("Cinder", 0);
            c.That(g.IsOver && g.Won, "18 damage kills an 18 HP enemy and wins");
        });
    }

    // ---------- Colossus ----------

    private static void Colossus(Check c)
    {
        c.Group("Colossus: 5 Block, and for one enemy turn attacks by Vulnerable enemies are halved", () =>
        {
            var f = Vs(Hitter(20), "Colossus");
            f.Play("Colossus");
            c.Equal(5, f.Player.Block, "5 Block");
            c.Equal(1, f.Player.PowerAmount<ColossusPower>(), "Colossus 1");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal("Takes half damage from attacks by Vulnerable enemies. Wears off in 1 turn.", f.Player.GetPower<ColossusPower>()!.Describe(), "the power's text");
            c.Sequence(["attack 20"], f.IntentTexts(f.Enemy(0)), "an attacker without Vulnerable is not affected");
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 2, f.Player);
            c.Sequence(["attack 10"], f.IntentTexts(f.Enemy(0)), "the intent of a Vulnerable attacker is halved");
            c.Equal(10, f.IncomingDamage(), "and so is the incoming damage");
            f.EndTurn();
            c.Equal(75, f.Player.Hp, "20 x 0.5 = 10, 5 of it on Block");
            c.That(!f.Player.HasPower<ColossusPower>(), "Colossus wore off at the end of the enemy turn");
            c.Equal(1, f.Enemy(0).PowerAmount<VulnerablePower>(), "the enemy is still Vulnerable");
            c.Sequence(["attack 20"], f.IntentTexts(f.Enemy(0)), "and hits in full again");
            f.EndTurn();
            c.Equal(55, f.Player.Hp, "20 the turn after");
        });

        c.Group("Colossus+: 8 Block, still 1 turn", () =>
        {
            var f = Vs(Hitter(20), "Colossus+");
            f.Play("Colossus+");
            c.Equal(8, f.Player.Block, "8 Block");
            c.Equal(1, f.Player.PowerAmount<ColossusPower>(), "Colossus 1");
            c.Equal(2, f.Energy, "costs 1");
            f.EndTurn();
            c.Equal(68, f.Player.Hp, "the attacker is not Vulnerable: 20 - 8 = 12");
        });

        c.Group("Colossus: two plays last two enemy turns", () =>
        {
            var f = Vs(Hitter(20), "Colossus", "Colossus");
            f.Play("Colossus");
            f.Play("Colossus");
            c.Equal(2, f.Player.PowerAmount<ColossusPower>(), "the turns add up");
            c.Equal(10, f.Player.Block, "5 + 5 Block");
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 3, f.Player);
            f.EndTurn();
            c.Equal(80, f.Player.Hp, "10 damage, all on Block");
            c.Equal(1, f.Player.PowerAmount<ColossusPower>(), "one turn left");
            f.EndTurn();
            c.Equal(70, f.Player.Hp, "10 damage, no Block left");
            c.That(!f.Player.HasPower<ColossusPower>(), "gone after the second enemy turn");
            f.EndTurn();
            c.Equal(50, f.Player.Hp, "20 once it is gone");
        });

        c.Group("Colossus: with Weak, per hit, with Frail; unpowered damage is not halved", () =>
        {
            var f = Vs(Hitter(20), "Colossus");
            f.Play("Colossus");
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            f.ApplyPower<WeakPower>(f.Enemy(0), 1, f.Player);
            f.EndTurn();
            c.Equal(78, f.Player.Hp, "20 x 0.75 x 0.5 = 7.5: 5 on Block, 2.5 is 2 HP");

            var g = Vs(Hitter(7, hits: 2), "Colossus");
            g.Play("Colossus");
            g.SetBlock(g.Player, 0);
            g.ApplyPower<VulnerablePower>(g.Enemy(0), 1, g.Player);
            c.Sequence(["attack 3x2"], g.IntentTexts(g.Enemy(0)), "7 x 0.5 = 3.5 shows as 3 per hit");
            g.EndTurn();
            c.Equal(74, g.Player.Hp, "each hit is 3.5, 3 HP: 6 in all");

            var h = Solo("Colossus");
            h.ApplyPower<FrailPower>(h.Player, 1, h.Enemy(0));
            c.Equal("Gain 3 Block. For 1 turn, attacks by Vulnerable enemies deal half damage to you.", HandText(h), "the hand text has Frail");
            h.Play("Colossus");
            c.Equal(3, h.Player.Block, "5 x 0.75 = 3.75");

            var zap = new CheckDummy { Steps = [new DummyStep("ZAP", Then: (x, me) => x.Damage(x.Player, 10, Props.NonCardUnpowered, me))] };
            var k = Vs(zap, "Colossus");
            k.Play("Colossus");
            k.ApplyPower<VulnerablePower>(k.Enemy(0), 1, k.Player);
            k.EndTurn();
            c.Equal(75, k.Player.Hp, "unpowered damage from a Vulnerable enemy stays 10: 5 on Block, 5 HP");
        });
    }

    // ---------- Conflagration ----------

    private static void Conflagration(Check c)
    {
        c.Group("Conflagration: 2 damage to every enemy, 4 times, as one attack", () =>
        {
            var f = Duo(100, 100, "Conflagration");
            f.Play("Conflagration");
            c.That(f.Enemy(0).Hp == 92 && f.Enemy(1).Hp == 92, "4 x 2 = 8 to each enemy");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(1, f.History.Count(e => e.Kind == HistoryKind.CreatureAttacked), "one attack");
            c.Equal(8, f.History.Count(e => e.Kind == HistoryKind.DamageReceived), "of four hits on two enemies");
        });

        c.Group("Conflagration+: 5 times", () =>
        {
            var f = Duo(100, 100, "Conflagration+");
            f.Play("Conflagration+");
            c.That(f.Enemy(0).Hp == 90 && f.Enemy(1).Hp == 90, "5 x 2 = 10 to each enemy");
            c.Equal(2, f.Energy, "costs 1");
        });

        c.Group("Conflagration: Strength, Weak, Vulnerable and Block apply to every hit", () =>
        {
            var f = Duo(100, 100, "Conflagration");
            f.ApplyPower<StrengthPower>(f.Player, 1, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            c.Equal("Deal 3 damage to ALL enemies 4 times.", HandText(f), "the hand text has Strength");
            f.Play("Conflagration");
            c.Equal(84, f.Enemy(0).Hp, "(2 + 1) x 1.5 = 4.5 is 4 per hit: 16");
            c.Equal(88, f.Enemy(1).Hp, "3 per hit: 12");

            var g = Duo(100, 100, "Conflagration");
            g.ApplyPower<WeakPower>(g.Player, 1, g.Enemy(0));
            g.Play("Conflagration");
            c.That(g.Enemy(0).Hp == 96 && g.Enemy(1).Hp == 96, "2 x 0.75 = 1.5 is 1 HP per hit: 4 to each enemy");
        });

        c.Group("Conflagration: a dead enemy is skipped by later hits; the loop ends with the last enemy", () =>
        {
            var f = Duo(5, 100, "Conflagration");
            f.Play("Conflagration");
            c.That(f.Enemy(0).IsDead, "Left died on the third hit");
            c.Equal(92, f.Enemy(1).Hp, "Right took all four");
            c.Equal(7, f.History.Count(e => e.Kind == HistoryKind.DamageReceived), "three hits on Left, four on Right");

            var g = Vs(new CheckDummy { Hp = 4 }, "Conflagration");
            g.Play("Conflagration");
            c.That(g.IsOver && g.Won, "two hits kill a 4 HP enemy and win");

            var h = Solo("Conflagration");
            h.SetBlock(h.Enemy(0), 5);
            h.Play("Conflagration");
            c.Equal(97, h.Enemy(0).Hp, "5 Block takes the first two hits and half of the third: 3 HP lost");
            c.Equal(0, h.Enemy(0).Block, "the Block is gone");
        });
    }

    // ---------- Corruption ----------

    private static void Corruption(Check c)
    {
        c.Group("Corruption: Skills cost 0 and are exhausted when played", () =>
        {
            var f = Solo("Corruption", "DefendIronclad", "StrikeIronclad", "Cruelty", "Slimed");
            c.Equal(1, f.CostOf(f.Hand[1]), "Defend costs 1 before");
            f.Play("Corruption");
            c.Equal(0, f.Energy, "costs 3");
            c.Equal(1, f.Player.PowerAmount<CorruptionPower>(), "Corruption is on");
            c.That(f.DiscardPile.Count == 0 && f.ExhaustPile.Count == 0, "the Power card is in no pile");
            c.Sequence([0, 1, 1, 1], f.Hand.Select(card => f.CostOf(card)), "Defend costs 0; the Attack, the Power and the Status card are unchanged");
            c.Equal("0", f.Snapshot().State["hand"]![0]!["cost"]!.GetValue<string>(), "the snapshot shows the 0");
            f.Play("Defend");
            c.Equal(5, f.Player.Block, "Defend was played with no energy");
            c.Sequence(["Defend"], Titles(f.ExhaustPile), "and exhausted");
            c.Equal(1, f.History.CardsExhaustedThisTurn(f), "as a real exhaust");
            c.That(!f.Execute("play:0>e0").Ok, "the Strike still needs its energy");
            f.SetEnergy(3);
            f.Play("Strike", 0);
            f.Play("Cruelty");
            f.Play("Slimed");
            c.Sequence(["Strike"], Titles(f.DiscardPile), "an Attack goes to the discard pile");
            c.Sequence(["Defend", "Slimed"], Titles(f.ExhaustPile), "the Power card left the fight; Slimed exhausted by its own keyword");
            c.Equal(0, f.Energy, "Strike, Cruelty and Slimed cost 1 each");
        });

        c.Group("Corruption+: costs 2", () =>
        {
            var f = Solo("Corruption+", "DefendIronclad");
            f.Play("Corruption+");
            c.Equal(1, f.Energy, "costs 2");
            c.Equal(1, f.Player.PowerAmount<CorruptionPower>(), "Corruption is on");
            c.Equal(0, f.CostOf(f.Hand[0]), "Defend costs 0");
        });

        c.Group("Corruption: it beats cost changes on the card; an X-cost Skill still takes all energy", () =>
        {
            var f = Solo("DefendIronclad", "Dominate", "Cascade");
            f.ApplyPower<CorruptionPower>(f.Player, 1, f.Player);
            f.Hand[0].AddCostThisCombat(2);
            c.Equal(0, f.CostOf(f.Hand[0]), "a Defend made to cost 3 costs 0");
            f.Play("Dominate", 0);
            c.Equal(3, f.Energy, "Dominate cost nothing");
            c.Sequence(["Dominate"], Titles(f.ExhaustPile), "and is exhausted once");
            f.SetEnergy(2);
            f.Play("Cascade");
            c.Equal(0, f.Energy, "Cascade took all the energy");
            c.Equal(80, f.Enemy(0).Hp, "X = 2: two Strikes, with Dominate's 1 Strength into Vulnerable: (6 + 1) x 1.5 = 10.5, 10 each");
            c.Sequence(["Dominate", "Cascade"], Titles(f.ExhaustPile), "and Cascade is exhausted as a Skill");
        });

        c.Group("Corruption: set in the late cost pass, after other cost changes; a card without a cost keeps none", () =>
        {
            var f = Solo("DefendIronclad", "StrikeIronclad");
            f.ApplyPower<CorruptionPower>(f.Player, 1, f.Player);
            f.ApplyPower<Tax>(f.Player, 1, f.Enemy(0));
            c.Equal(0, f.CostOf(f.Hand[0]), "a power that makes cards cost more, applied later, does not beat it");
            c.Equal(2, f.CostOf(f.Hand[1]), "the Attack pays the extra");
            var dead = f.Put(Costless, PileType.Hand);
            c.Equal(-1, f.CostOf(dead), "a Skill without a cost stays without one");
            c.Equal("-", f.Snapshot().State["hand"]![2]!["cost"]!.GetValue<string>(), "and reads so");
            c.Equal("Skills cost 0 and are Exhausted when played.", f.Player.GetPower<CorruptionPower>()!.Describe(), "the power's text");
        });

        c.Group("Corruption: a Skill played by an effect is exhausted too; one already resolving is not", () =>
        {
            var f = Solo("Cascade");
            f.ApplyPower<CorruptionPower>(f.Player, 1, f.Player);
            f.Put("DefendIronclad", PileType.Draw, PilePosition.Top);
            f.SetEnergy(1);
            f.Play("Cascade");
            c.Equal(5, f.Player.Block, "the Defend was played");
            c.Sequence(["Defend", "Cascade"], Titles(f.ExhaustPile), "both Skills are exhausted");

            var g = Solo("Cascade");
            g.Put("Corruption", PileType.Draw, PilePosition.Top);
            g.SetEnergy(1);
            g.Play("Cascade");
            c.That(g.Player.HasPower<CorruptionPower>(), "Cascade played Corruption");
            c.Sequence(["Cascade"], Titles(g.DiscardPile), "but Cascade's own destination was decided before: the discard pile");
            c.Equal(0, g.ExhaustPile.Count, "nothing was exhausted");
        });

        c.Group("Corruption with Dark Embrace: every Skill draws a card", () =>
        {
            var f = Solo("DefendIronclad", "DefendIronclad");
            f.ApplyPower<CorruptionPower>(f.Player, 1, f.Player);
            f.ApplyPower<DarkEmbracePower>(f.Player, 1, f.Player);
            f.Play("Defend");
            c.Sequence(["Defend", "Strike"], f.HandTitles(), "one card drawn for the exhausted Defend");
            c.Equal(3, f.Energy, "for no energy");
        });
    }

    // ---------- Crimson Mantle ----------

    private static void CrimsonMantle(Check c)
    {
        c.Group("Crimson Mantle: from the next turn on, lose 1 HP and gain 8 Block after the hand draw", () =>
        {
            var f = Vs(Hitter(6), "CrimsonMantle");
            f.Play("Crimson Mantle");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(8, f.Player.PowerAmount<CrimsonMantlePower>(), "the amount is the Block");
            c.Equal(1, f.Player.GetPower<CrimsonMantlePower>()!.SelfDamage, "the HP cost is 1 after the first play");
            c.Equal("At the start of its turn, loses 1 HP and gains 8 Block.", f.Player.GetPower<CrimsonMantlePower>()!.Describe(), "the power's text");
            c.That(f.Player.Hp == 80 && f.Player.Block == 0, "nothing on the turn it is played");
            f.EndTurn();
            c.Equal(73, f.Player.Hp, "6 from the enemy, then 1 at the start of turn 2");
            c.Equal(8, f.Player.Block, "8 Block");

            var lastDraw = LastIndex(f, e => e.Kind == HistoryKind.CardDrawn && e.Turn == 2 && e.FromHandDraw);
            var loss = FirstIndex(f, e => e.Kind == HistoryKind.DamageReceived && e.Turn == 2 && e.ActorId == f.Player.Id);
            var block = FirstIndex(f, e => e.Kind == HistoryKind.BlockGained && e.Turn == 2 && e.ActorId == f.Player.Id);
            c.That(lastDraw >= 0 && loss > lastDraw && block > loss, "the order is: hand draw, HP loss, Block");
            c.Equal(Props.NonCardHpLoss, f.History.Entries.ElementAt(loss).Props, "the HP loss is unblockable and unpowered, without the card-or-move flag");
            c.Equal(Props.NonCardUnpowered, f.History.Entries.ElementAt(block).Props, "the Block is unpowered");

            f.EndTurn();
            c.Equal(72, f.Player.Hp, "the Block took the enemy's 6; 1 more HP at the start of turn 3");
            c.Equal(8, f.Player.Block, "the old Block fell, 8 new");
        });

        c.Group("Crimson Mantle+: 10 Block, still 1 HP", () =>
        {
            var f = Solo("CrimsonMantle+");
            f.Play("Crimson Mantle+");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(10, f.Player.PowerAmount<CrimsonMantlePower>(), "10 Block per turn");
            f.EndTurn();
            c.Equal(79, f.Player.Hp, "1 HP");
            c.Equal(10, f.Player.Block, "10 Block");
        });

        c.Group("Crimson Mantle: every play adds its Block and 1 HP", () =>
        {
            var f = Solo("CrimsonMantle", "CrimsonMantle+");
            f.Play("Crimson Mantle");
            f.Play("Crimson Mantle+");
            c.Equal(18, f.Player.PowerAmount<CrimsonMantlePower>(), "8 + 10");
            c.Equal(2, f.Player.GetPower<CrimsonMantlePower>()!.SelfDamage, "2 HP per turn");
            f.EndTurn();
            c.Equal(78, f.Player.Hp, "2 HP");
            c.Equal(18, f.Player.Block, "18 Block");
        });

        c.Group("Crimson Mantle: Frail and Dexterity do not change its Block; Block does not absorb its cost; at 1 HP it kills", () =>
        {
            var f = Solo("CrimsonMantle");
            f.Play("Crimson Mantle");
            f.ApplyPower<FrailPower>(f.Player, 3, f.Enemy(0));
            f.ApplyPower<DexterityPower>(f.Player, 2, f.Player);
            f.EndTurn();
            c.That(f.Player.HasPower<FrailPower>(), "the player is still Frail");
            c.Equal(8, f.Player.Block, "8 Block all the same");

            var g = Solo("CrimsonMantle");
            g.Play("Crimson Mantle");
            g.ApplyPower<BarricadePower>(g.Player, 1, g.Player);
            g.SetBlock(g.Player, 20);
            g.EndTurn();
            c.Equal(79, g.Player.Hp, "1 HP lost through 20 Block");
            c.Equal(28, g.Player.Block, "20 kept by Barricade, plus 8");

            var h = AtHp(1, "CrimsonMantle");
            h.Play("Crimson Mantle");
            h.EndTurn();
            c.That(h.IsOver && !h.Won, "the fight is lost at the start of the next turn");
        });
    }

    // ---------- Cruelty ----------

    private static void Cruelty(Check c)
    {
        c.Group("Cruelty: the Vulnerable factor of the player's attacks becomes 1.75", () =>
        {
            var f = Solo("Cruelty", "StrikeIronclad", "StrikeIronclad");
            f.Play("Cruelty");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(25, f.Player.PowerAmount<CrueltyPower>(), "Cruelty 25");
            c.Equal("Its attacks deal 25% more damage to Vulnerable enemies (x1.75 instead of x1.5).", f.Player.GetPower<CrueltyPower>()!.Describe(), "the power's text");
            f.Play("Strike", 0);
            c.Equal(94, f.Enemy(0).Hp, "6 on an enemy without Vulnerable");
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            c.Equal("Deal 6 damage.", HandText(f), "the hand text has no target, so no Vulnerable and no Cruelty");
            f.Play("Strike", 0);
            c.Equal(84, f.Enemy(0).Hp, "6 x 1.75 = 10.5 is 10");
        });

        c.Group("Cruelty+: 50, so the factor is 2", () =>
        {
            var f = Solo("Cruelty+", "StrikeIronclad");
            f.Play("Cruelty+");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(50, f.Player.PowerAmount<CrueltyPower>(), "Cruelty 50");
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            f.Play("Strike", 0);
            c.Equal(88, f.Enemy(0).Hp, "6 x 2 = 12");
        });

        c.Group("Cruelty: plays add up; it does nothing for hits its owner takes", () =>
        {
            var f = Solo("Cruelty", "Cruelty+", "StrikeIronclad");
            f.Play("Cruelty");
            f.Play("Cruelty+");
            c.Equal(75, f.Player.PowerAmount<CrueltyPower>(), "25 + 50");
            c.Equal("Its attacks deal 75% more damage to Vulnerable enemies (x2.25 instead of x1.5).", f.Player.GetPower<CrueltyPower>()!.Describe(), "the power's text at 75");
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            f.Play("Strike", 0);
            c.Equal(87, f.Enemy(0).Hp, "6 x 2.25 = 13.5 is 13");

            var g = Vs(Hitter(10), "Cruelty");
            g.Play("Cruelty");
            g.ApplyPower<VulnerablePower>(g.Player, 2, g.Enemy(0));
            g.EndTurn();
            c.Equal(65, g.Player.Hp, "a Vulnerable player with Cruelty takes 10 x 1.5 = 15");

            var h = Solo("Cruelty", "Bully");
            h.Play("Cruelty");
            h.ApplyPower<VulnerablePower>(h.Enemy(0), 2, h.Player);
            h.Play("Bully", 0);
            c.Equal(86, h.Enemy(0).Hp, "Bully: (4 + 2 x 2) x 1.75 = 14");
        });
    }

    // ---------- Dark Embrace ----------

    private static void DarkEmbrace(Check c)
    {
        c.Group("Dark Embrace: draw 1 whenever a card is exhausted", () =>
        {
            var f = Solo("DarkEmbrace", "Slimed", "Cruelty");
            f.Play("Dark Embrace");
            c.Equal(1, f.Energy, "costs 2");
            c.Equal(1, f.Player.PowerAmount<DarkEmbracePower>(), "Dark Embrace 1");
            c.Equal("Whenever a card is Exhausted, draws 1 card. For a card exhausted by Ethereal the draw comes after the hand is discarded.", f.Player.GetPower<DarkEmbracePower>()!.Describe(), "the power's text");
            c.That(f.DiscardPile.Count == 0 && f.ExhaustPile.Count == 0, "the Power card is in no pile");
            c.Equal(2, f.Hand.Count, "and playing it drew nothing");
            f.Play("Slimed");
            c.Sequence(["Cruelty", "Strike", "Strike"], f.HandTitles(), "Slimed drew one card, its exhaust another");
            c.Equal(3, f.DrawPile.Count, "two cards drawn");
            f.SetEnergy(1);
            f.Play("Cruelty");
            c.Sequence(["Strike", "Strike"], f.HandTitles(), "a played Power card is not exhausted: no draw");
        });

        c.Group("Dark Embrace+: costs 1", () =>
        {
            var f = Solo("DarkEmbrace+", "Slimed");
            f.Play("Dark Embrace+");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(1, f.Player.PowerAmount<DarkEmbracePower>(), "Dark Embrace 1");
            f.Play("Slimed");
            c.Sequence(["Strike", "Strike"], f.HandTitles(), "one card from Slimed, one from Dark Embrace");
        });

        c.Group("Dark Embrace: two stacks draw two; No Draw blocks it", () =>
        {
            var f = Solo("DarkEmbrace", "DarkEmbrace", "Slimed");
            f.SetEnergy(5);
            f.Play("Dark Embrace");
            f.Play("Dark Embrace");
            c.Equal(2, f.Player.PowerAmount<DarkEmbracePower>(), "each play adds 1");
            f.Play("Slimed");
            c.Sequence(["Strike", "Strike", "Strike"], f.HandTitles(), "one card from Slimed, two from Dark Embrace");

            var g = Solo("Slimed");
            g.ApplyPower<DarkEmbracePower>(g.Player, 1, g.Player);
            g.ApplyPower<NoDrawPower>(g.Player, 1, g.Player);
            g.Play("Slimed");
            c.Equal(0, g.Hand.Count, "with No Draw neither Slimed nor Dark Embrace draws");
            c.Sequence(["Slimed"], Titles(g.ExhaustPile), "the exhaust happened");

            var h = Solo("Slimed", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound");
            h.ApplyPower<DarkEmbracePower>(h.Player, 1, h.Player);
            h.Play("Slimed");
            c.Equal(10, h.Hand.Count, "Slimed's own draw filled the hand");
            c.Equal(4, h.DrawPile.Count, "so Dark Embrace's card stayed on the draw pile");
        });

        c.Group("Dark Embrace: a card exhausted by Ethereal is paid for after the hand is discarded", () =>
        {
            var f = CheckKit.VsDummy(HandCounter());
            f.Put("DarkEmbrace", PileType.Hand);
            f.Put("Dazed", PileType.Hand);
            f.Play("Dark Embrace");
            f.EndTurn();
            c.Sequence(["Dazed"], Titles(f.ExhaustPile), "Dazed was exhausted at the end of the turn");
            c.Equal(1, f.Enemy(0).PowerAmount<StrengthPower>(), "during the enemy turn the hand held exactly the one card drawn for it");
            c.Equal(6, f.Hand.Count, "and it stays for the next turn, on top of the 5 drawn");
            c.Equal("Strike", f.Hand[0].Title, "it was the top card of the draw pile");
        });

        c.Group("Dark Embrace and Battle Trance: the end-of-turn order of the two powers decides", () =>
        {
            // Dark Embrace first: its deferred draw runs while No Draw is still there, and is lost.
            var f = CheckKit.VsDummy(HandCounter());
            foreach (var card in new[] { "DarkEmbrace", "BattleTrance", "Dazed" }) f.Put(card, PileType.Hand);
            f.Play("Dark Embrace");
            f.Play("Battle Trance");
            c.Equal(9, f.Hand.Count, "five starters, Dazed and the three cards drawn");
            f.EndTurn();
            c.Sequence(["Dazed"], Titles(f.ExhaustPile), "Dazed was exhausted");
            c.Equal(0, f.Enemy(0).PowerAmount<StrengthPower>(), "the hand was empty during the enemy turn: the draw was blocked");
            c.Equal(5, f.Hand.Count, "an ordinary hand next turn");
            c.That(!f.Player.HasPower<NoDrawPower>(), "No Draw is gone");
            f.EndTurn();
            c.Equal(0, f.Enemy(0).PowerAmount<StrengthPower>(), "the lost draw is not made up for a turn later: the count was forgotten");
            c.Equal(5, f.Hand.Count, "five cards again");

            // Battle Trance first: No Draw is removed before Dark Embrace asks, and the card is drawn.
            var g = CheckKit.VsDummy(HandCounter());
            foreach (var card in new[] { "DarkEmbrace", "BattleTrance", "Dazed" }) g.Put(card, PileType.Hand);
            g.Play("Battle Trance");
            g.Play("Dark Embrace");
            g.EndTurn();
            c.Equal(1, g.Enemy(0).PowerAmount<StrengthPower>(), "one card in hand during the enemy turn");
            c.Equal(6, g.Hand.Count, "six cards next turn");
        });
    }

    // ---------- Demon Form ----------

    private static void DemonForm(Check c)
    {
        c.Group("Demon Form: 2 Strength at the start of every turn, from the next one on", () =>
        {
            var f = Solo("DemonForm");
            f.Play("Demon Form");
            c.Equal(0, f.Energy, "costs 3");
            c.Equal(2, f.Player.PowerAmount<DemonFormPower>(), "Demon Form 2");
            c.Equal("At the start of its turn, gains 2 Strength.", f.Player.GetPower<DemonFormPower>()!.Describe(), "the power's text");
            c.Equal(0, f.Player.PowerAmount<StrengthPower>(), "no Strength on the turn it is played");
            f.EndTurn();
            c.Equal(2, f.Player.PowerAmount<StrengthPower>(), "2 Strength on turn 2");
            var lastDraw = LastIndex(f, e => e.Kind == HistoryKind.CardDrawn && e.Turn == 2 && e.FromHandDraw);
            var strength = FirstIndex(f, e => e.Kind == HistoryKind.PowerReceived && e.Name == "StrengthPower");
            c.That(lastDraw >= 0 && strength > lastDraw, "it arrives after the hand draw");
            c.Equal("Deal 8 damage.", HandText(f), "Strike shows 8");
            f.Play("Strike", 0);
            c.Equal(92, f.Enemy(0).Hp, "and deals 8");
            f.EndTurn();
            c.Equal(4, f.Player.PowerAmount<StrengthPower>(), "4 Strength on turn 3");
            c.Equal(2, f.Player.PowerAmount<DemonFormPower>(), "Demon Form does not change");
        });

        c.Group("Demon Form+: 3 Strength; plays add up", () =>
        {
            var f = Solo("DemonForm+");
            f.Play("Demon Form+");
            c.Equal(0, f.Energy, "costs 3");
            c.Equal(3, f.Player.PowerAmount<DemonFormPower>(), "Demon Form 3");
            f.EndTurn();
            c.Equal(3, f.Player.PowerAmount<StrengthPower>(), "3 Strength on turn 2");

            var g = Solo("DemonForm", "DemonForm+");
            g.SetEnergy(6);
            g.Play("Demon Form");
            g.Play("Demon Form+");
            c.Equal(5, g.Player.PowerAmount<DemonFormPower>(), "2 + 3");
            g.EndTurn();
            c.Equal(5, g.Player.PowerAmount<StrengthPower>(), "5 Strength on turn 2");
            g.EndTurn();
            c.Equal(10, g.Player.PowerAmount<StrengthPower>(), "10 on turn 3");
        });
    }

    // ---------- Dismantle ----------

    private static void Dismantle(Check c)
    {
        c.Group("Dismantle: 8 damage; twice into Vulnerable", () =>
        {
            var f = Solo("Dismantle", "Dismantle");
            f.Play("Dismantle", 0);
            c.Equal(92, f.Enemy(0).Hp, "one hit of 8");
            c.Equal(2, f.Energy, "costs 1");
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            f.Play("Dismantle", 0);
            c.Equal(68, f.Enemy(0).Hp, "two hits of 8 x 1.5 = 12");
            c.Equal(1, f.Enemy(0).PowerAmount<VulnerablePower>(), "the Vulnerable is not used up");
        });

        c.Group("Dismantle+: 10 damage", () =>
        {
            var f = Solo("Dismantle+", "Dismantle+");
            f.Play("Dismantle+", 0);
            c.Equal(90, f.Enemy(0).Hp, "one hit of 10");
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            f.Play("Dismantle+", 0);
            c.Equal(60, f.Enemy(0).Hp, "two hits of 15");
            c.Equal(1, f.Energy, "costs 1 each");
        });

        c.Group("Dismantle: Strength and Block per hit; after Bash; the second hit is lost when the first kills", () =>
        {
            var f = Solo("Dismantle");
            f.ApplyPower<StrengthPower>(f.Player, 2, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            f.SetBlock(f.Enemy(0), 10);
            f.Play("Dismantle", 0);
            c.Equal(80, f.Enemy(0).Hp, "two hits of (8 + 2) x 1.5 = 15, 10 of it on Block");

            var g = Solo("Bash", "Dismantle");
            g.Play("Bash", 0);
            g.Play("Dismantle", 0);
            c.Equal(68, g.Enemy(0).Hp, "Bash 8, then two hits of 12");
            c.Equal(0, g.Energy, "2 + 1 energy");

            var h = Duo(10, 100, "Dismantle");
            h.ApplyPower<VulnerablePower>(h.Enemy(0), 1, h.Player);
            h.Play("Dismantle", 0);
            c.That(h.Enemy(0).IsDead && h.Enemy(1).Hp == 100, "the first hit killed; the second went nowhere");
            c.Equal(1, h.History.Count(e => e.Kind == HistoryKind.DamageReceived), "one hit landed");
        });
    }

    // ---------- Dominate ----------

    private static void Dominate(Check c)
    {
        c.Group("Dominate: 1 Vulnerable, then Strength equal to the enemy's Vulnerable; Exhaust", () =>
        {
            var f = Solo("Dominate");
            c.Sequence(["play:0>e0", "end_turn"], f.LegalActions().Select(a => a.Id), "a Skill aimed at an enemy");
            f.Play("Dominate", 0);
            c.Equal(1, f.Enemy(0).PowerAmount<VulnerablePower>(), "1 Vulnerable");
            c.Equal(1, f.Player.PowerAmount<StrengthPower>(), "1 Strength");
            c.Equal(100, f.Enemy(0).Hp, "no damage");
            c.Equal(2, f.Energy, "costs 1");
            c.Sequence(["Dominate"], Titles(f.ExhaustPile), "exhausted");
            c.Equal(0, f.DiscardPile.Count, "not discarded");
        });

        c.Group("Dominate+: 2 Vulnerable, still Exhaust", () =>
        {
            var f = Solo("Dominate+");
            f.Play("Dominate+", 0);
            c.Equal(2, f.Enemy(0).PowerAmount<VulnerablePower>(), "2 Vulnerable");
            c.Equal(2, f.Player.PowerAmount<StrengthPower>(), "2 Strength");
            c.Equal(2, f.Energy, "costs 1");
            c.Sequence(["Dominate+"], Titles(f.ExhaustPile), "the upgrade keeps Exhaust");
        });

        c.Group("Dominate: Vulnerable the enemy already had counts", () =>
        {
            var f = Solo("Dominate", "Dominate+");
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 3, f.Player);
            f.Play("Dominate", 0);
            c.Equal(4, f.Enemy(0).PowerAmount<VulnerablePower>(), "3 + 1 Vulnerable");
            c.Equal(4, f.Player.PowerAmount<StrengthPower>(), "4 Strength");
            f.Play("Dominate+", 0);
            c.Equal(6, f.Enemy(0).PowerAmount<VulnerablePower>(), "4 + 2 Vulnerable");
            c.Equal(10, f.Player.PowerAmount<StrengthPower>(), "4 + 6 Strength");
        });

        c.Group("Dominate: Artifact stops the Vulnerable; the Strength follows what the enemy ends up with", () =>
        {
            var f = Vs(new CheckDummy { OnAdded = (x, me) => x.ApplyPower<ArtifactPower>(me, 1, me) }, "Dominate");
            f.Play("Dominate", 0);
            c.That(!f.Enemy(0).HasPower<VulnerablePower>() && !f.Enemy(0).HasPower<ArtifactPower>(), "no Vulnerable; Artifact is used up");
            c.That(!f.Player.HasPower<StrengthPower>(), "no Strength");
            c.Sequence(["Dominate"], Titles(f.ExhaustPile), "exhausted all the same");

            var g = Solo("Dominate");
            g.ApplyPower<VulnerablePower>(g.Enemy(0), 2, g.Player);
            g.ApplyPower<ArtifactPower>(g.Enemy(0), 1, g.Enemy(0));
            g.Play("Dominate", 0);
            c.Equal(2, g.Enemy(0).PowerAmount<VulnerablePower>(), "Artifact stopped the new stack; the old 2 remain");
            c.Equal(2, g.Player.PowerAmount<StrengthPower>(), "2 Strength for them");
        });
    }

    // ---------- all of them together ----------

    private static readonly string[] LiveDeck =
    [
        "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad", "Bash",
        .. Rows.Select(row => row.Class),
        "Aggression+", "Anger+", "BattleTrance+", "Brand+", "BurningPact+", "Cascade+", "Colossus+", "CrimsonMantle+", "Dominate+",
        "Dazed", "Slimed",
    ];

    /// <summary>Seeds 1 to 30: two Nibbits. Above: two 250 HP dummies that hit for 3, so the powers get many turns to work together.</summary>
    private static Fight LiveFight(int seed)
    {
        var builder = new FightBuilder().Deck(LiveDeck).Seed(seed);
        if (seed <= 30) return builder.Encounter("NibbitsNormal").Start();
        return builder
            .Enemy(new CheckDummy { Hp = 250, DisplayName = "Left", Steps = [new DummyStep("HIT", Damage: 3)] })
            .Enemy(new CheckDummy { Hp = 250, DisplayName = "Right", Steps = [new DummyStep("HIT", Damage: 3)] })
            .Start();
    }

    /// <summary>
    /// Every card of this file in real fights, picked by a fixed rule. Nothing may throw, a fight
    /// and its copy must show the same after every action (the powers keep their state in the
    /// fight, also across the card choices of Brand and Burning Pact), and the same ids must lead
    /// to the same end.
    /// </summary>
    private static void LiveFights(Check c)
    {
        c.Group("every card in live fights: a fight and its copy stay in step, and the ids replay to the same end", () =>
        {
            for (var seed = 1; seed <= 36; seed++)
            {
                var f = LiveFight(seed);
                var ids = new List<string>();
                for (var step = 0; step < 5000 && !f.IsOver; step++)
                {
                    var actions = f.LegalActions();
                    var pick = actions[(seed * 31 + step * 7) % actions.Count].Id;
                    var twin = f.Clone();
                    f.Do(pick);
                    twin.Do(pick);
                    ids.Add(pick);
                    if (f.Seen() == twin.Seen()) continue;
                    c.That(false, $"seed {seed}: after {string.Join(" ", ids)} the copy shows something else");
                    break;
                }
                c.That(f.IsOver, $"seed {seed}: the fight ended");

                var again = LiveFight(seed);
                foreach (var id in ids) again.Do(id);
                c.That(again.Seen() == f.Seen(), $"seed {seed}: replaying the ids ends the same way");
            }
        });
    }
}
