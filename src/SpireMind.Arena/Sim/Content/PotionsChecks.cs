namespace SpireMind.Arena;

/// <summary>
/// Every potion of <see cref="Potions"/> used through its action id against scripted dummies,
/// with the numbers worked out by hand from the potion's class in the game (v0.107.1) and
/// run-generation.md 5.1 and 5.2.
/// </summary>
public sealed class PotionsChecks : IArenaChecks
{
    /// <summary>What the game's potion classes say: title, rarity, target, usage, and whether random generation in a fight may make it.</summary>
    private static readonly (string ClassName, string Title, PotionRarity Rarity, TargetType Target, PotionUsage Usage, bool Generated)[] Expected =
    [
        ("FirePotion", "Fire Potion", PotionRarity.Common, TargetType.AnyEnemy, PotionUsage.CombatOnly, true),
        ("ExplosiveAmpoule", "Explosive Ampoule", PotionRarity.Common, TargetType.AllEnemies, PotionUsage.CombatOnly, true),
        ("BlockPotion", "Block Potion", PotionRarity.Common, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("StrengthPotion", "Strength Potion", PotionRarity.Common, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("FlexPotion", "Flex Potion", PotionRarity.Common, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("DexterityPotion", "Dexterity Potion", PotionRarity.Common, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("SpeedPotion", "Speed Potion", PotionRarity.Common, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("EnergyPotion", "Energy Potion", PotionRarity.Common, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("SwiftPotion", "Swift Potion", PotionRarity.Common, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("WeakPotion", "Weak Potion", PotionRarity.Common, TargetType.AnyEnemy, PotionUsage.CombatOnly, true),
        ("VulnerablePotion", "Vulnerable Potion", PotionRarity.Common, TargetType.AnyEnemy, PotionUsage.CombatOnly, true),
        ("BloodPotion", "Blood Potion", PotionRarity.Common, TargetType.AnyPlayer, PotionUsage.AnyTime, true),
        ("AttackPotion", "Attack Potion", PotionRarity.Common, TargetType.Self, PotionUsage.CombatOnly, true),
        ("SkillPotion", "Skill Potion", PotionRarity.Common, TargetType.Self, PotionUsage.CombatOnly, true),
        ("PowerPotion", "Power Potion", PotionRarity.Common, TargetType.Self, PotionUsage.CombatOnly, true),
        ("FyshOil", "Fysh Oil", PotionRarity.Uncommon, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("CureAll", "Cure All", PotionRarity.Uncommon, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("PotionOfBinding", "Potion of Binding", PotionRarity.Uncommon, TargetType.AllEnemies, PotionUsage.CombatOnly, true),
        ("HeartOfIron", "Heart of Iron", PotionRarity.Uncommon, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("Fortifier", "Fortifier", PotionRarity.Uncommon, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("BlessingOfTheForge", "Blessing of the Forge", PotionRarity.Uncommon, TargetType.Self, PotionUsage.CombatOnly, true),
        ("Ashwater", "Ashwater", PotionRarity.Uncommon, TargetType.Self, PotionUsage.CombatOnly, true),
        ("BeetleJuice", "Beetle Juice", PotionRarity.Rare, TargetType.AnyEnemy, PotionUsage.CombatOnly, true),
        ("FruitJuice", "Fruit Juice", PotionRarity.Rare, TargetType.AnyPlayer, PotionUsage.AnyTime, false),
        ("ShacklingPotion", "Shackling Potion", PotionRarity.Rare, TargetType.AllEnemies, PotionUsage.CombatOnly, true),
        ("SoldiersStew", "Soldier's Stew", PotionRarity.Rare, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("DistilledChaos", "Distilled Chaos", PotionRarity.Rare, TargetType.Self, PotionUsage.CombatOnly, true),
        ("BottledPotential", "Bottled Potential", PotionRarity.Rare, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("FairyInABottle", "Fairy in a Bottle", PotionRarity.Rare, TargetType.Self, PotionUsage.Automatic, false),
        ("LiquidMemories", "Liquid Memories", PotionRarity.Rare, TargetType.Self, PotionUsage.CombatOnly, true),
        ("DropletOfPrecognition", "Droplet of Precognition", PotionRarity.Rare, TargetType.Self, PotionUsage.CombatOnly, true),
        ("OrobicAcid", "Orobic Acid", PotionRarity.Rare, TargetType.Self, PotionUsage.CombatOnly, true),
        ("SneckoOil", "Snecko Oil", PotionRarity.Rare, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("RegenPotion", "Regen Potion", PotionRarity.Uncommon, TargetType.AnyPlayer, PotionUsage.CombatOnly, false),
        ("LiquidBronze", "Liquid Bronze", PotionRarity.Uncommon, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("Clarity", "Clarity Extract", PotionRarity.Uncommon, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("Duplicator", "Duplicator", PotionRarity.Uncommon, TargetType.Self, PotionUsage.CombatOnly, true),
        ("RadiantTincture", "Radiant Tincture", PotionRarity.Uncommon, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("StableSerum", "Stable Serum", PotionRarity.Uncommon, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("PowderedDemise", "Powdered Demise", PotionRarity.Uncommon, TargetType.AnyEnemy, PotionUsage.CombatOnly, true),
        ("GamblersBrew", "Gambler's Brew", PotionRarity.Uncommon, TargetType.Self, PotionUsage.CombatOnly, true),
        ("TouchOfInsanity", "Touch of Insanity", PotionRarity.Uncommon, TargetType.Self, PotionUsage.CombatOnly, true),
        ("GigantificationPotion", "Gigantification Potion", PotionRarity.Rare, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("LuckyTonic", "Lucky Tonic", PotionRarity.Rare, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("MazalethsGift", "Mazaleth's Gift", PotionRarity.Rare, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
        ("ShipInABottle", "Ship in a Bottle", PotionRarity.Rare, TargetType.AnyPlayer, PotionUsage.CombatOnly, true),
    ];

    /// <summary>The powers only potions use: the game's title, kind and stack type.</summary>
    private static readonly (string ClassName, string Title, PowerType Type)[] ExpectedPowers =
    [
        ("RegenPower", "Regen", PowerType.Buff), ("ThornsPower", "Thorns", PowerType.Buff), ("ClarityPower", "Clarity", PowerType.Buff),
        ("DuplicationPower", "Duplication", PowerType.Buff), ("RadiancePower", "Radiance", PowerType.Buff),
        ("RetainHandPower", "Retain Hand", PowerType.Buff), ("DemisePower", "Demise", PowerType.Debuff),
        ("GigantificationPower", "Gigantification", PowerType.Buff), ("BufferPower", "Buffer", PowerType.Buff),
        ("RitualPower", "Ritual", PowerType.Buff), ("BlockNextTurnPower", "Block Next Turn", PowerType.Buff),
    ];

    /// <summary>Check-only cards: a free card, a card that costs HP, a two-hit Attack, and a Skill that attacks.</summary>
    private static readonly CardDef Free = new("CheckPotionFree", "Trinket", CardType.Skill, CardRarity.Common, 0, TargetType.Self);

    private static readonly CardDef Bleed = new("CheckPotionBleed", "Bleed", CardType.Skill, CardRarity.Common, 0, TargetType.Self)
    {
        Play = c => c.LoseHp(3),
    };

    private static readonly CardDef Flurry = new("CheckPotionFlurry", "Flurry", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy)
    {
        Vars = { ["Damage"] = 3 },
        Text = t => $"Deal {t.Damage()} damage 2 times.",
        Play = c => c.Attack(c.V("Damage")).Hits(2).Execute(),
    };

    private static readonly CardDef Jab = new("CheckPotionJab", "Jab", CardType.Skill, CardRarity.Common, 0, TargetType.AnyEnemy)
    {
        Vars = { ["Damage"] = 5 },
        Text = t => $"Deal {t.Damage()} damage.",
        Play = c => c.Attack(c.V("Damage")).Execute(),
    };

    private static string PotionText(Fight f, int slot = 0) => f.Snapshot().State["potions"]![slot]!["text"]!.GetValue<string>();

    private static string HandText(Fight f, int i) => f.Snapshot().State["hand"]![i]!["text"]!.GetValue<string>();

    /// <summary>An X-cost card, for the potions that leave X costs alone.</summary>
    private static readonly CardDef XCard = new("CheckPotionX", "Whirl", CardType.Attack, CardRarity.Uncommon, CardDef.CostX, TargetType.AllEnemies)
    {
        Play = c => c.Attack(1).Hits(c.X).Execute(),
    };

    /// <summary>A dummy that hits for 10 every turn.</summary>
    private static CheckDummy Hitter(int hp = 300) => new() { Hp = hp, Steps = [new DummyStep("HIT", Damage: 10)] };

    /// <summary>The starter deck in its fixed order against <paramref name="dummy"/>, the potion in slot 0.</summary>
    private static Fight With(string potion, CheckDummy? dummy = null)
    {
        var f = CheckKit.VsDummy(dummy);
        f.SetPotion(0, potion);
        return f;
    }

    private static Fight WithTwo(string potion, CheckDummy left, CheckDummy right)
    {
        var f = CheckKit.VsDummies(left, right);
        f.SetPotion(0, potion);
        return f;
    }

    public void Run(Check c)
    {
        Table(c);
        DamageAndBlock(c);
        StrengthAndDexterity(c);
        EnergyAndCards(c);
        Debuffs(c);
        Healing(c);
        CardPotions(c);
        OtherRarities(c);
        PowerPotions(c);
        ChoicePotions(c);
        LeftOut(c);
    }

    private static void Table(Check c)
    {
        c.Group("the registry's potions carry the game's title, rarity, target and usage", () =>
        {
            var registry = Content.Registry;
            foreach (var e in Expected)
            {
                c.That(registry.Has(ContentKind.Potion, e.ClassName), $"{e.ClassName} is registered");
                if (!registry.Has(ContentKind.Potion, e.ClassName)) continue;
                var p = registry.Potion(e.ClassName);
                c.Equal(e.Title, p.Name, $"{e.ClassName} title");
                c.Equal(e.Rarity, p.Rarity, $"{e.ClassName} rarity");
                c.Equal(e.Target, p.Target, $"{e.ClassName} target");
                c.Equal(e.Usage, p.Usage, $"{e.ClassName} usage");
                c.Equal(e.Generated, p.CanBeGeneratedInCombat, $"{e.ClassName} can be generated in a fight");
                c.That(p.Text(null).Length > 0, $"{e.ClassName} has a text outside a fight");
            }
            // Potion-Shaped Rock is the game's one Token-rarity potion: in no pool, made by the relic Petrified Toad alone (Relics1).
            c.Equal(Expected.Length, registry.Potions.Keys.Count(name => name != Relics1.PotionShapedRock), "no other potion of the pools is registered");
        });

        c.Group("every potion is offered under the action id its target kind calls for", () =>
        {
            foreach (var e in Expected)
            {
                var f = WithTwo(e.ClassName, new CheckDummy { DisplayName = "Left" }, new CheckDummy { DisplayName = "Right" });
                var ids = f.LegalActions().Select(a => a.Id).Where(id => id.Contains("potion", StringComparison.Ordinal)).ToList();
                // An automatic potion cannot be used by hand, only thrown away.
                string[] want = e.Usage == PotionUsage.Automatic ? ["discard_potion:0"]
                    : e.Target == TargetType.AnyEnemy ? ["potion:0>e0", "potion:0>e1", "discard_potion:0"]
                    : ["potion:0", "discard_potion:0"];
                c.Sequence(want, ids, $"{e.ClassName} actions");
                c.That(f.Snapshot().State["potions"]![0]!["text"]!.GetValue<string>().Length > 0, $"{e.ClassName} has a text in the fight");
            }
        });
    }

    // ---------- Fire Potion, Explosive Ampoule, Block Potion ----------

    private static void DamageAndBlock(Check c)
    {
        c.Group("Fire Potion: 20 unpowered damage to the chosen enemy", () =>
        {
            var f = With("FirePotion");
            f.Do("potion:0>e0");
            c.Equal(80, f.Enemy(0).Hp, "100 - 20");
            c.Equal(3, f.Energy, "a potion costs no energy");
            c.That(f.Potions[0] is null, "the slot is empty afterwards");
            c.Equal(5, f.Hand.Count, "the hand is untouched");

            // Strength 3 and Weak on the user, Vulnerable on the target: none of them asks about unpowered damage.
            var g = With("FirePotion");
            g.ApplyPower<StrengthPower>(g.Player, 3, g.Player);
            g.ApplyPower<WeakPower>(g.Player, 2, g.Enemy(0));
            g.ApplyPower<VulnerablePower>(g.Enemy(0), 2, g.Player);
            g.Do("potion:0>e0");
            c.Equal(80, g.Enemy(0).Hp, "still 20 with Strength, Weak and Vulnerable in play");

            // Block absorbs potion damage.
            var h = With("FirePotion");
            h.SetBlock(h.Enemy(0), 5);
            h.Do("potion:0>e0");
            c.Equal(85, h.Enemy(0).Hp, "5 Block absorbs 5 of the 20");
            c.Equal(0, h.Enemy(0).Block, "the Block is gone");
            var i = With("FirePotion");
            i.SetBlock(i.Enemy(0), 30);
            i.Do("potion:0>e0");
            c.Equal(100, i.Enemy(0).Hp, "30 Block absorbs all of it");
            c.Equal(10, i.Enemy(0).Block, "30 - 20 Block left");

            // Slippery does not ask what kind of damage it is: the 20 become 1 and a stack is used.
            var s = With("FirePotion", new CheckDummy { OnAdded = (x, me) => x.ApplyPower<SlipperyPower>(me, 2, me) });
            s.Do("potion:0>e0");
            c.Equal(99, s.Enemy(0).Hp, "a Slippery enemy loses exactly 1");
            c.Equal(1, s.Enemy(0).PowerAmount<SlipperyPower>(), "and one Slippery stack");

            var k = With("FirePotion", new CheckDummy { Hp = 20 });
            k.Do("potion:0>e0");
            c.That(k.IsOver && k.Won, "20 damage kills a 20 HP enemy and wins the fight");
        });

        c.Group("Explosive Ampoule: 10 unpowered damage to every enemy", () =>
        {
            var f = WithTwo("ExplosiveAmpoule", new CheckDummy { Hp = 40 }, new CheckDummy { Hp = 8 });
            f.SetBlock(f.Enemy(0), 4);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            f.ApplyPower<StrengthPower>(f.Player, 5, f.Player);
            f.Do("potion:0");
            c.Equal(34, f.Enemy(0).Hp, "40 - (10 - 4 Block); Vulnerable and Strength change nothing");
            c.Equal(0, f.Enemy(0).Block, "its Block is gone");
            c.That(f.Enemy(1).IsDead, "the 8 HP enemy dies");
            c.That(!f.IsOver, "the fight goes on against the other");
            c.Sequence(new[] { f.Enemy(0) }, f.Enemies, "only the survivor is left in the enemy list");

            var g = WithTwo("ExplosiveAmpoule", new CheckDummy { Hp = 10 }, new CheckDummy { Hp = 9 });
            g.Do("potion:0");
            c.That(g.IsOver && g.Won, "killing every enemy wins");
        });

        c.Group("Block Potion: 12 unpowered Block", () =>
        {
            var f = With("BlockPotion");
            f.Do("potion:0");
            c.Equal(12, f.Player.Block, "12 Block");

            // Frail and Dexterity only touch powered (card) Block.
            var g = With("BlockPotion");
            g.ApplyPower<FrailPower>(g.Player, 2, g.Enemy(0));
            g.ApplyPower<DexterityPower>(g.Player, 3, g.Player);
            g.SetBlock(g.Player, 5);
            g.Do("potion:0");
            c.Equal(17, g.Player.Block, "5 + 12, with Frail and Dexterity in play");

            // It works as Block does: a 10 damage hit takes 10 of it.
            var h = With("BlockPotion", Hitter());
            h.Do("potion:0", "end_turn");
            c.Equal(80, h.Player.Hp, "the 10 damage hit is absorbed");
        });
    }

    // ---------- Strength, Flex, Dexterity, Speed ----------

    private static void StrengthAndDexterity(Check c)
    {
        c.Group("Strength Potion: 2 Strength for the rest of the fight", () =>
        {
            var f = With("StrengthPotion");
            f.Do("potion:0");
            c.Equal(2, f.Player.PowerAmount<StrengthPower>(), "2 Strength");
            c.Equal("Deal 8 damage.", Fight.CardText(f, f.Hand[0], PileType.Hand), "Strike shows 6 + 2");
            f.Play("Strike", 0);
            c.Equal(92, f.Enemy(0).Hp, "Strike deals 8");
            f.EndTurn();
            c.Equal(2, f.Player.PowerAmount<StrengthPower>(), "still there next turn");
        });

        c.Group("Flex Potion: 5 Strength now, taken back at the end of the turn", () =>
        {
            var f = With("FlexPotion");
            f.Do("potion:0");
            c.Equal(5, f.Player.PowerAmount<StrengthPower>(), "5 Strength at once");
            c.Equal(5, f.Player.PowerAmount<FlexPotionPower>(), "and the potion's own power with 5");
            c.Equal("Flex Potion", f.Player.GetPower<FlexPotionPower>()!.Name, "it carries the potion's name");
            f.Play("Strike", 0);
            c.Equal(89, f.Enemy(0).Hp, "Strike deals 6 + 5 = 11");
            f.EndTurn();
            c.Equal(0, f.Player.PowerAmount<StrengthPower>(), "the Strength is gone next turn");
            c.That(!f.Player.HasPower<StrengthPower>() && !f.Player.HasPower<FlexPotionPower>(), "and so are both powers");
            f.Play("Strike", 0);
            c.Equal(83, f.Enemy(0).Hp, "Strike deals 6 again");

            // On top of lasting Strength, and two in one turn.
            var g = With("FlexPotion");
            g.SetPotion(1, "FlexPotion");
            g.SetPotion(2, "StrengthPotion");
            g.Do("potion:2", "potion:0", "potion:1");
            c.Equal(12, g.Player.PowerAmount<StrengthPower>(), "2 + 5 + 5");
            c.Equal(10, g.Player.PowerAmount<FlexPotionPower>(), "the two Flex Potions stack in one power");
            g.EndTurn();
            c.Equal(2, g.Player.PowerAmount<StrengthPower>(), "only the Strength Potion's 2 is left");

            // Artifact stops the taking back, because that is a Strength loss.
            var h = With("FlexPotion");
            h.ApplyPower<ArtifactPower>(h.Player, 1, h.Player);
            h.Do("potion:0", "end_turn");
            c.Equal(5, h.Player.PowerAmount<StrengthPower>(), "with Artifact the 5 Strength stays");
            c.That(!h.Player.HasPower<ArtifactPower>() && !h.Player.HasPower<FlexPotionPower>(), "Artifact and the potion's power are used up");
        });

        c.Group("Dexterity Potion: 2 Dexterity for the rest of the fight", () =>
        {
            var f = With("DexterityPotion");
            f.Do("potion:0");
            c.Equal(2, f.Player.PowerAmount<DexterityPower>(), "2 Dexterity");
            f.Play("Defend");
            c.Equal(7, f.Player.Block, "Defend gives 5 + 2");
            f.EndTurn();
            c.Equal(2, f.Player.PowerAmount<DexterityPower>(), "still there next turn");
        });

        c.Group("Speed Potion: 5 Dexterity now, taken back at the end of the turn", () =>
        {
            var f = With("SpeedPotion");
            f.Do("potion:0");
            c.Equal(5, f.Player.PowerAmount<DexterityPower>(), "5 Dexterity at once");
            c.Equal(5, f.Player.PowerAmount<SpeedPotionPower>(), "and the potion's own power with 5");
            c.Equal("Gain 10 Block.", Fight.CardText(f, f.Hand[2], PileType.Hand), "Defend shows 5 + 5");
            f.Play("Defend");
            c.Equal(10, f.Player.Block, "Defend gives 10");
            f.EndTurn();
            c.That(!f.Player.HasPower<DexterityPower>() && !f.Player.HasPower<SpeedPotionPower>(), "both powers are gone next turn");
            f.Play("Defend");
            c.Equal(5, f.Player.Block, "Defend gives 5 again");

            var g = With("SpeedPotion");
            g.SetPotion(1, "SpeedPotion");
            g.SetPotion(2, "DexterityPotion");
            g.Do("potion:2", "potion:0", "potion:1");
            c.Equal(12, g.Player.PowerAmount<DexterityPower>(), "2 + 5 + 5");
            c.Equal(10, g.Player.PowerAmount<SpeedPotionPower>(), "the two Speed Potions stack in one power");
            g.EndTurn();
            c.Equal(2, g.Player.PowerAmount<DexterityPower>(), "only the Dexterity Potion's 2 is left");
        });
    }

    // ---------- Energy, Swift ----------

    private static void EnergyAndCards(Check c)
    {
        c.Group("Energy Potion: 2 energy, above the maximum too", () =>
        {
            var f = With("EnergyPotion");
            f.Do("potion:0");
            c.Equal(5, f.Energy, "3 + 2");
            var g = With("EnergyPotion");
            g.Play("Bash", 0);
            g.Do("potion:0");
            c.Equal(3, g.Energy, "1 left after Bash, + 2");
        });

        c.Group("Swift Potion: draw 3", () =>
        {
            var f = With("SwiftPotion");
            f.Do("potion:0");
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash", "Strike", "Strike", "Strike"], f.HandTitles(), "the next three cards of the draw pile join the hand");
            c.Equal(2, f.DrawPile.Count, "5 - 3 left to draw");

            // The hand stops at 10.
            var g = With("SwiftPotion");
            for (var n = 0; n < 4; n++) g.Put("DefendIronclad", PileType.Hand);
            g.Do("potion:0");
            c.Equal(10, g.Hand.Count, "with 9 in hand only 1 is drawn");
            c.Equal(4, g.DrawPile.Count, "the other two stay in the draw pile");

            // An empty draw pile is refilled from the discard pile.
            var h = With("SwiftPotion");
            h.Clear(PileType.Draw);
            h.Play("Defend").Play("Defend");
            h.Do("potion:0");
            c.Equal(5, h.Hand.Count, "3 left in hand, and the 2 discarded cards are shuffled back and drawn");
            c.Equal(0, h.DiscardPile.Count + h.DrawPile.Count, "nothing is left to draw");
        });
    }

    // ---------- Weak, Vulnerable ----------

    private static void Debuffs(Check c)
    {
        c.Group("Weak Potion: 3 Weak covers the enemy's next three turns", () =>
        {
            var f = With("WeakPotion", Hitter());
            f.Do("potion:0>e0");
            c.Equal(3, f.Enemy(0).PowerAmount<WeakPower>(), "3 Weak");
            c.Equal(7, f.IncomingDamage(), "its 10 damage intent shows 7 (7.5 cut down)");
            f.EndTurn();
            c.Equal(73, f.Player.Hp, "turn 1: 7");
            c.Equal(2, f.Enemy(0).PowerAmount<WeakPower>(), "Weak ticks at the end of the enemy turn");
            f.EndTurn();
            c.Equal(66, f.Player.Hp, "turn 2: 7");
            f.EndTurn();
            c.Equal(59, f.Player.Hp, "turn 3: 7");
            c.That(!f.Enemy(0).HasPower<WeakPower>(), "then Weak is gone");
            f.EndTurn();
            c.Equal(49, f.Player.Hp, "turn 4: 10");
        });

        c.Group("Vulnerable Potion: 3 Vulnerable covers this turn and the next two", () =>
        {
            var strikes = Enumerable.Repeat("StrikeIronclad", 20).ToArray();
            var f = new FightBuilder().DrawOrder(strikes).Enemy(new CheckDummy { Hp = 300 }).Start();
            f.SetPotion(0, "VulnerablePotion");
            f.Do("potion:0>e0");
            c.Equal(3, f.Enemy(0).PowerAmount<VulnerablePower>(), "3 Vulnerable");
            f.Play("Strike", 0);
            c.Equal(291, f.Enemy(0).Hp, "turn 1: Strike deals 9");
            f.EndTurn();
            f.Play("Strike", 0);
            c.Equal(282, f.Enemy(0).Hp, "turn 2: 9");
            f.EndTurn();
            f.Play("Strike", 0);
            c.Equal(273, f.Enemy(0).Hp, "turn 3: 9");
            f.EndTurn();
            c.That(!f.Enemy(0).HasPower<VulnerablePower>(), "gone by turn 4");
            f.Play("Strike", 0);
            c.Equal(267, f.Enemy(0).Hp, "turn 4: 6");

            // Artifact stops a potion's debuff like any other.
            var g = With("VulnerablePotion", new CheckDummy { OnAdded = (x, me) => x.ApplyPower<ArtifactPower>(me, 1, me) });
            g.SetPotion(1, "WeakPotion");
            g.Do("potion:0>e0");
            c.That(!g.Enemy(0).HasPower<VulnerablePower>() && !g.Enemy(0).HasPower<ArtifactPower>(), "Artifact negates it and is used up");
            g.Do("potion:1>e0");
            c.Equal(3, g.Enemy(0).PowerAmount<WeakPower>(), "the next debuff lands");
        });
    }

    // ---------- Blood Potion ----------

    private static void Healing(Check c)
    {
        c.Group("Blood Potion: heal 20% of max HP", () =>
        {
            Fight At(int hp, int maxHp)
            {
                var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(new CheckDummy()).Hp(hp, maxHp).Start();
                f.SetPotion(0, "BloodPotion");
                return f;
            }

            var f = At(50, 80);
            c.Equal("Heal 20% of your Max HP (16 HP).", f.Snapshot().State["potions"]![0]!["text"]!.GetValue<string>(), "the text names the amount");
            f.Do("potion:0");
            c.Equal(66, f.Player.Hp, "50 + 16");
            var g = At(70, 80);
            g.Do("potion:0");
            c.Equal(80, g.Player.Hp, "never above max HP");
            // 20% of 91 is 18.2: the resulting HP is cut down to a whole number.
            var h = At(50, 91);
            h.Do("potion:0");
            c.Equal(68, h.Player.Hp, "50 + 18.2 -> 68");
        });
    }

    // ---------- Attack, Skill and Power Potion ----------

    private static void CardPotions(Check c)
    {
        foreach (var (potion, type) in new[] { ("AttackPotion", CardType.Attack), ("SkillPotion", CardType.Skill), ("PowerPotion", CardType.Power) })
        {
            c.Group($"{potion}: one of three different random {type} cards, free this turn", () =>
            {
                const int seed = 7;
                var pool = Potions.CardPotionPool(type);
                c.That(pool.All(d => d.Type == type), "the pool holds only this type");
                c.That(pool.All(d => d.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare), "never a Basic, Ancient or Event card");
                c.That(pool.All(d => d.CanBeGeneratedInCombat && d.ClassName is not ("Feed" or "NotYet" or "Tank" or "DemonicShield")), "never Feed, Not Yet or a multiplayer-only card");
                c.Sequence(pool.Select(d => d.ClassName).OrderBy(n => n, StringComparer.Ordinal), pool.Select(d => d.ClassName), "in pool order");

                // The game shuffles the whole list on the card-generation stream and takes the first three.
                var shuffled = pool.ToList();
                RngSet.For(seed, "combat_card_generation").Shuffle(shuffled);
                var expected = shuffled.Take(3).Select(d => d.Name).ToList();

                Fight Open()
                {
                    var f = CheckKit.VsDummy(seed: seed);
                    f.SetPotion(0, potion);
                    f.Do("potion:0");
                    return f;
                }

                var f = Open();
                c.Equal(Math.Max(0, pool.Count - 1), f.Rng.CombatCardGeneration.Counter, "one draw per card of the list but the first");
                if (pool.Count == 0)
                {
                    c.That(!f.HasPendingChoice && f.Potions[0] is null && f.Hand.Count == 5, "with no card to offer the potion is simply used up");
                    return;
                }

                var s = f.Snapshot();
                c.Equal("card_select", s.Screen, "the choose-a-card screen opens");
                c.Equal("choose", s.State["kind"]!.GetValue<string>(), "of the kind 'choose'");
                var ids = Enumerable.Range(0, expected.Count).Select(n => $"card:{n}").Append("skip").ToList();
                c.Sequence(ids, s.Actions.Select(a => a.Id), "one action per card, and skip");
                c.Sequence(expected.Select(n => $"Take {n}"), s.Actions.Take(expected.Count).Select(a => a.Label), "the offered cards");
                c.Equal(expected.Count, expected.Distinct().Count(), "all different");
                c.Equal(Math.Min(3, pool.Count), expected.Count, "three, or all there are");

                f.Do("card:0");
                c.That(!f.HasPendingChoice && f.Potions[0] is null, "the choice is made and the potion is gone");
                c.Equal(6, f.Hand.Count, "the card joins the hand");
                var card = f.Hand[^1];
                c.Equal(expected[0], card.Title, "it is the chosen card, not upgraded, at the end of the hand");
                c.Equal(3, f.Energy, "nothing was paid");
                if (card.CostsX || card.BaseCost < 0)
                {
                    c.Equal(card.BaseCost, f.CostOf(card), "an X or cost-less card keeps its cost");
                }
                else
                {
                    c.Equal(0, f.CostOf(card), "it costs 0 this turn");
                    f.EndTurn();
                    c.Equal(card.BaseCost, f.CostOf(card), "and its own cost again from the next turn, if it was not played");
                }

                var skipped = Open();
                skipped.Do("skip");
                c.That(!skipped.HasPendingChoice && skipped.Potions[0] is null && skipped.Hand.Count == 5, "skipping adds nothing and still uses the potion");

                // With a full hand the card goes to the discard pile.
                var full = CheckKit.VsDummy(seed: seed);
                full.SetPotion(0, potion);
                for (var n = 0; n < 5; n++) full.Put("DefendIronclad", PileType.Hand);
                full.Do("potion:0", "card:0");
                c.Equal(10, full.Hand.Count, "a full hand stays at 10");
                c.Equal(expected[0], full.DiscardPile[^1].Title, "the card lands in the discard pile");

                // The same fight replays to the same offer.
                c.Sequence(s.Actions.Select(a => a.Label), Open().Snapshot().Actions.Select(a => a.Label), "the same seed offers the same cards");
            });
        }
    }

    // ---------- the uncommon and rare ones ----------

    private static void OtherRarities(Check c)
    {
        c.Group("Fysh Oil: 1 Strength, then 1 Dexterity", () =>
        {
            var f = With("FyshOil");
            f.Do("potion:0");
            c.Sequence(["Strength", "Dexterity"], f.Player.Powers.Select(p => p.Name), "Strength is applied first");
            c.Equal(1, f.Player.PowerAmount<StrengthPower>(), "1 Strength");
            c.Equal(1, f.Player.PowerAmount<DexterityPower>(), "1 Dexterity");
            f.Play("Strike", 0).Play("Defend");
            c.Equal(93, f.Enemy(0).Hp, "Strike deals 7");
            c.Equal(6, f.Player.Block, "Defend gives 6");
        });

        c.Group("Cure All: 1 energy and 2 cards", () =>
        {
            var f = With("CureAll");
            f.Do("potion:0");
            c.Equal(4, f.Energy, "3 + 1");
            c.Equal(7, f.Hand.Count, "5 + 2");
            c.Equal(3, f.DrawPile.Count, "5 - 2 left to draw");
        });

        c.Group("Potion of Binding: 1 Weak, then 1 Vulnerable, to every enemy", () =>
        {
            var f = WithTwo("PotionOfBinding", Hitter(100), new CheckDummy { Hp = 100, OnAdded = (x, me) => x.ApplyPower<ArtifactPower>(me, 1, me) });
            f.Do("potion:0");
            c.Sequence(["Weak", "Vulnerable"], f.Enemy(0).Powers.Select(p => p.Name), "Weak is applied before Vulnerable");
            c.Equal(1, f.Enemy(0).PowerAmount<WeakPower>(), "1 Weak");
            c.Equal(1, f.Enemy(0).PowerAmount<VulnerablePower>(), "1 Vulnerable");
            // The second enemy's Artifact takes the first debuff, which is the Weak.
            c.Sequence(["Vulnerable"], f.Enemy(1).Powers.Select(p => p.Name), "Artifact negates the Weak; the Vulnerable lands");
            f.Play("Strike", 0);
            c.Equal(91, f.Enemy(0).Hp, "Strike into Vulnerable deals 9");
            f.EndTurn();
            c.Equal(73, f.Player.Hp, "the Weak enemy hits for 7");
            c.That(!f.Enemy(0).HasPower<WeakPower>() && !f.Enemy(0).HasPower<VulnerablePower>(), "both wear off after one enemy turn");
        });

        c.Group("Heart of Iron: 7 Plating, 28 Block over seven turns", () =>
        {
            var f = With("HeartOfIron", Hitter(9999));
            f.Do("potion:0");
            c.Equal(7, f.Player.PowerAmount<PlatingPower>(), "7 Plating");
            c.Equal(0, f.Player.Block, "no Block yet");
            // Each end of turn gives Block equal to the Plating, which then drops by 1 at the next turn start;
            // the dummy hits for 10 every turn.
            int[] hpAfterTurn = [77, 73, 68, 62, 55, 47, 38, 28];
            int[] platingAfterTurn = [6, 5, 4, 3, 2, 1, 0, 0];
            for (var turn = 0; turn < hpAfterTurn.Length; turn++)
            {
                f.EndTurn();
                c.Equal(hpAfterTurn[turn], f.Player.Hp, $"HP after enemy turn {turn + 1}");
                c.Equal(platingAfterTurn[turn], f.Player.PowerAmount<PlatingPower>(), $"Plating at the start of turn {turn + 2}");
            }
        });

        c.Group("Fortifier: gain twice the Block held, so it triples", () =>
        {
            var f = With("Fortifier");
            f.Play("Defend");
            c.Equal("Triple your Block (gain 10 Block now).", f.Snapshot().State["potions"]![0]!["text"]!.GetValue<string>(), "the text names the gain");
            f.Do("potion:0");
            c.Equal(15, f.Player.Block, "5 + 10");
            var g = With("Fortifier");
            g.Do("potion:0");
            c.Equal(0, g.Player.Block, "three times nothing is nothing");
            // Unpowered: Frail does not shrink the gain.
            var h = With("Fortifier");
            h.SetBlock(h.Player, 7);
            h.ApplyPower<FrailPower>(h.Player, 2, h.Enemy(0));
            h.Do("potion:0");
            c.Equal(21, h.Player.Block, "7 + 14 with Frail");
        });

        c.Group("Blessing of the Forge: every card in hand is upgraded for the fight", () =>
        {
            var f = With("BlessingOfTheForge");
            f.Do("potion:0");
            c.Sequence(["Strike+", "Strike+", "Defend+", "Defend+", "Bash+"], f.HandTitles(), "the whole hand");
            c.That(f.DrawPile.All(card => !card.IsUpgraded), "the draw pile is not touched");
            f.Play("Strike+", 0);
            c.Equal(91, f.Enemy(0).Hp, "Strike+ deals 9");
        });

        c.Group("Beetle Juice: 4 Shrink covers the enemy's next four turns", () =>
        {
            var f = With("BeetleJuice", Hitter());
            f.Do("potion:0>e0");
            c.Equal(4, f.Enemy(0).PowerAmount<ShrinkPower>(), "4 Shrink");
            c.Equal(7, f.IncomingDamage(), "its 10 damage intent shows 7");
            int[] hpAfterTurn = [73, 66, 59, 52, 42];
            for (var turn = 0; turn < hpAfterTurn.Length; turn++)
            {
                f.EndTurn();
                c.Equal(hpAfterTurn[turn], f.Player.Hp, $"HP after enemy turn {turn + 1}");
            }
            c.That(!f.Enemy(0).HasPower<ShrinkPower>(), "Shrink is gone after four enemy turns");
        });

        c.Group("Fruit Juice: 5 max HP, and 5 HP with it", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(new CheckDummy()).Hp(50, 80).Start();
            f.SetPotion(0, "FruitJuice");
            f.Do("potion:0");
            c.Equal(85, f.Player.MaxHp, "80 + 5 max HP");
            c.Equal(55, f.Player.Hp, "50 + 5 HP");
            var g = With("FruitJuice");
            g.Do("potion:0");
            c.Equal(85, g.Player.Hp, "at full HP it stays full: 85 of 85");
        });

        c.Group("Shackling Potion: every enemy loses 7 Strength until the end of its turn", () =>
        {
            var f = WithTwo("ShacklingPotion", Hitter(), new CheckDummy { Hp = 300, Steps = [new DummyStep("HIT", Damage: 5)] });
            f.Do("potion:0");
            c.Equal(-7, f.Enemy(0).PowerAmount<StrengthPower>(), "7 Strength less");
            c.Equal(7, f.Enemy(0).PowerAmount<ShacklingPotionPower>(), "and the potion's own power with 7");
            c.Equal(-7, f.Enemy(1).PowerAmount<StrengthPower>(), "on the other enemy too");
            c.Equal(3, f.IncomingDamage(), "10 - 7 = 3, and 5 - 7 is no damage at all");
            f.EndTurn();
            c.Equal(77, f.Player.Hp, "3 + 0");
            c.That(f.Enemies.All(e => !e.HasPower<StrengthPower>() && !e.HasPower<ShacklingPotionPower>()), "the Strength is back after the enemy turn");
            f.EndTurn();
            c.Equal(62, f.Player.Hp, "then 10 + 5 again");

            // A Debuff: Artifact stops all of it.
            var g = With("ShacklingPotion", new CheckDummy { Hp = 300, Steps = [new DummyStep("HIT", Damage: 10)], OnAdded = (x, me) => x.ApplyPower<ArtifactPower>(me, 1, me) });
            g.Do("potion:0");
            c.That(!g.Enemy(0).HasPower<StrengthPower>() && !g.Enemy(0).HasPower<ShacklingPotionPower>() && !g.Enemy(0).HasPower<ArtifactPower>(), "Artifact negates it and is used up");
            g.EndTurn();
            c.Equal(70, g.Player.Hp, "the full 10");
        });

        c.Group("Soldier's Stew: Strike cards in the fight now are played twice", () =>
        {
            var f = With("SoldiersStew");
            f.Do("potion:0");
            var later = f.Put("StrikeIronclad", PileType.Hand);
            f.Play("Strike", 0);
            c.Equal(88, f.Enemy(0).Hp, "a Strike from the hand hits twice: 6 + 6");
            c.Equal(2, f.Energy, "for one payment");
            c.Equal(2, f.CardsPlayed == 1 ? f.History.CardPlaysFinishedThisTurn(f) : -1, "one card played, two runs of its effect");
            f.Play("Bash", 0);
            c.Equal(80, f.Enemy(0).Hp, "Bash has no Strike tag: once, 8");
            c.Equal(0, later.ReplayCount, "a Strike that enters the fight afterwards is not touched");
            c.That(f.DrawPile.Where(card => card.HasTag(CardTags.Strike)).All(card => card.ReplayCount == 1), "Strikes in the draw pile are");
            f.EndTurn();
            // Turn 2: Strike, Strike, Strike, Defend, Defend, all from the draw pile; Bash's Vulnerable has 1 turn left.
            f.Play("Strike", 0);
            c.Equal(62, f.Enemy(0).Hp, "next turn a Strike from the draw pile hits twice into Vulnerable: 9 + 9");
        });

        c.Group("Distilled Chaos: the top three cards of the draw pile are played for free", () =>
        {
            var f = With("DistilledChaos");
            f.Do("potion:0");
            c.Equal(82, f.Enemy(0).Hp, "the three Strikes on top deal 6 each");
            c.Equal(3, f.Energy, "no energy is spent");
            c.Equal(5, f.Hand.Count, "the hand is untouched");
            c.Equal(2, f.DrawPile.Count, "5 - 3 left to draw");
            c.Sequence(["Strike", "Strike", "Strike"], f.DiscardPile.Select(card => card.Title), "the played cards are discarded");
            c.Equal(0, f.CardsPlayed, "they do not count as played from the hand");
        });

        c.Group("Ashwater: any number of hand cards are exhausted, in the order picked", () =>
        {
            var f = With("Ashwater");
            f.Do("potion:0");
            var s = f.Snapshot();
            c.Equal("hand_select", s.Screen, "the hand selection opens");
            c.Equal(0, s.State["selection"]!["min"]!.GetValue<int>(), "none is allowed");
            c.Equal(999_999_999, s.State["selection"]!["max"]!.GetValue<int>(), "and as many as there are");
            c.Sequence(["select:0", "select:1", "select:2", "select:3", "select:4", "confirm"], s.Actions.Select(a => a.Id), "every hand card can be picked, and nothing can be confirmed");
            f.Do("select:4", "select:0", "confirm");
            c.Sequence(["Strike", "Defend", "Defend"], f.HandTitles(), "the two picked cards left the hand");
            c.Sequence(["Bash", "Strike"], f.ExhaustPile.Select(card => card.Title), "exhausted in the order they were picked");
            c.Equal(0, f.DiscardPile.Count, "not discarded");
            c.That(f.Potions[0] is null && f.Energy == 3, "the potion is used, no energy spent");

            var none = With("Ashwater");
            none.Do("potion:0", "confirm");
            c.That(none.Hand.Count == 5 && none.ExhaustPile.Count == 0 && none.Potions[0] is null, "confirming with nothing picked exhausts nothing and still uses the potion");

            var empty = With("Ashwater");
            empty.Clear(PileType.Hand);
            empty.Do("potion:0");
            c.That(!empty.HasPendingChoice && empty.Potions[0] is null, "with an empty hand nothing is asked");
        });

        c.Group("Liquid Memories: a card of the discard pile returns to the hand, free this turn", () =>
        {
            var f = With("LiquidMemories");
            f.Play("Strike", 0).Play("Defend");
            f.Do("potion:0");
            var s = f.Snapshot();
            c.Equal("card_select", s.Screen, "the pile selection opens");
            c.Equal("combat_pile_select", s.State["kind"]!.GetValue<string>(), "of the kind 'combat_pile_select'");
            c.Sequence(["Select Strike", "Select Defend"], s.Actions.Select(a => a.Label), "the two discarded cards, in the pile's order");
            f.Do("card:0");
            c.Sequence(["Strike", "Defend", "Bash", "Strike"], f.HandTitles(), "the Strike is back, at the end of the hand");
            c.Sequence(["Defend"], f.DiscardPile.Select(card => card.Title), "the other card stays discarded");
            c.Equal(0, f.CostOf(f.Hand[^1]), "it costs 0");
            c.Equal(1, f.CostOf(f.Hand[0]), "the other Strike costs 1 as before");
            f.Do("play:3>e0");
            c.Equal(1, f.Energy, "playing it spends nothing: 1 energy left after Strike and Defend");
            c.Equal(88, f.Enemy(0).Hp, "two Strikes: 12");

            // Unplayed, it costs 1 again from the next turn.
            var g = With("LiquidMemories");
            g.Play("Strike", 0).Play("Defend");
            g.Do("potion:0", "card:0");
            var returned = g.Hand[^1];
            g.EndTurn();
            c.Equal(1, g.CostOf(returned), "free only this turn");

            // One card in the pile is taken without a question; an empty pile gives nothing.
            var one = With("LiquidMemories");
            one.Play("Defend");
            one.Do("potion:0");
            c.That(!one.HasPendingChoice && one.DiscardPile.Count == 0 && one.Hand.Count == 5 && one.CostOf(one.Hand[^1]) == 0, "a single discarded card comes back without asking");
            var empty = With("LiquidMemories");
            empty.Do("potion:0");
            c.That(!empty.HasPendingChoice && empty.Hand.Count == 5 && empty.Potions[0] is null, "with an empty discard pile the potion is simply used up");
        });

        c.Group("Droplet of Precognition: a card of the draw pile goes to the hand", () =>
        {
            var f = With("DropletOfPrecognition");
            f.Do("potion:0");
            var s = f.Snapshot();
            c.Equal("combat_pile_select", s.State["kind"]!.GetValue<string>(), "the pile selection opens");
            // Draw pile: Strike x3, Defend x2. Equal cards are offered once, and the order gives the draw order away to nobody.
            c.Sequence(["Select Defend", "Select Strike"], s.Actions.Select(a => a.Label), "one action per different card");
            f.Do(s.Actions.Single(a => a.Label == "Select Defend").Id);
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash", "Defend"], f.HandTitles(), "the Defend joins the hand");
            c.Sequence(["Strike", "Strike", "Strike", "Defend"], f.DrawPile.Select(card => card.Title), "the rest of the draw pile keeps its order");
            c.Equal(1, f.CostOf(f.Hand[^1]), "its cost is unchanged");
            c.That(f.Potions[0] is null && f.Energy == 3, "the potion is used, no energy spent");

            var empty = With("DropletOfPrecognition");
            empty.Clear(PileType.Draw);
            empty.Do("potion:0");
            c.That(!empty.HasPendingChoice && empty.Hand.Count == 5 && empty.Potions[0] is null, "with an empty draw pile the potion is simply used up");
        });

        c.Group("Orobic Acid: a random Attack, Skill and Power, free this turn", () =>
        {
            const int seed = 11;
            // One shuffle of the whole list per type, Attack, Skill, Power, on the card-generation stream.
            var stream = RngSet.For(seed, "combat_card_generation");
            var expected = new List<CardDef>();
            var draws = 0;
            foreach (var type in new[] { CardType.Attack, CardType.Skill, CardType.Power })
            {
                var pool = Potions.CardPotionPool(type);
                stream.Shuffle(pool);
                draws += Math.Max(0, pool.Count - 1);
                if (pool.Count > 0) expected.Add(pool[0]);
            }

            var f = CheckKit.VsDummy(seed: seed);
            f.SetPotion(0, "OrobicAcid");
            f.Do("potion:0");
            c.That(!f.HasPendingChoice && f.Potions[0] is null, "nothing is asked; the potion is used");
            c.Equal(draws, f.Rng.CombatCardGeneration.Counter, "three shuffles on the card-generation stream");
            c.Sequence(expected.Select(d => d.Name), f.Hand.Skip(5).Select(card => card.Title), "the cards join the hand in the order Attack, Skill, Power");
            c.Sequence(expected.Select(d => d.Type), f.Hand.Skip(5).Select(card => card.Type), "one of each type the pool has");
            foreach (var card in f.Hand.Skip(5))
                c.Equal(card.CostsX || card.BaseCost < 0 ? card.BaseCost : 0, f.CostOf(card), $"{card.Title} is free (an X or cost-less card keeps its cost)");
            c.Equal(3, f.Energy, "no energy spent");
        });

        c.Group("Snecko Oil: draw 7, then every card with a cost gets a random cost of 0 to 3 for the turn", () =>
        {
            const int seed = 13;
            var f = CheckKit.VsDummy(seed: seed);
            f.SetPotion(0, "SneckoOil");
            var x = f.Put(XCard, PileType.Hand);
            var wound = f.Put("Wound", PileType.Hand);
            f.Do("potion:0");
            // 7 in hand, so only 3 of the 7 are drawn: the hand stops at 10.
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash", "Whirl", "Wound", "Strike", "Strike", "Strike"], f.HandTitles(), "the hand after the draw");
            c.Equal(2, f.DrawPile.Count, "5 - 3 left to draw");

            // One roll per card in hand order, skipping the X card and the card without a cost.
            var stream = RngSet.For(seed, "combat_energy_costs");
            var rolled = f.Hand.Where(card => card != x && card != wound).ToList();
            var costs = rolled.Select(_ => stream.NextInt(4)).ToList();
            c.Sequence(costs, rolled.Select(f.CostOf), "the new costs, in hand order");
            c.Equal(8, f.Rng.CombatEnergyCosts.Counter, "eight rolls on the energy-cost stream");
            c.That(costs.All(cost => cost is >= 0 and <= 3), "every cost is 0 to 3");
            c.Equal("X", f.CostText(x), "an X card stays an X card");
            c.Equal("-", f.CostText(wound), "a card without a cost stays without one");

            var bash = f.Hand[4];
            f.EndTurn();
            c.Equal(2, f.CostOf(bash), "the next turn the cards cost what they did before");
        });

        c.Group("Fairy in a Bottle: a death is prevented once, and the potion heals 30% of max HP", () =>
        {
            var f = With("FairyInABottle", Hitter());
            f.SetHp(f.Player, 5);
            c.Equal("When you would die, heal to 30% of your Max HP instead (24 HP). Works by itself, once.",
                f.Snapshot().State["potions"]![0]!["text"]!.GetValue<string>(), "the text names the amount");
            c.Equal(false, f.Snapshot().State["potions"]![0]!["usable"]!.GetValue<bool>(), "it cannot be drunk by hand");
            f.EndTurn();
            c.That(!f.IsOver, "the 10 damage hit at 5 HP does not end the fight");
            c.Equal(24, f.Player.Hp, "30% of 80 from 0");
            c.That(f.Potions[0] is null, "the potion is used up");
            c.Equal(1, f.History.Count(e => e.Kind == HistoryKind.PotionUsed && e.Name == "FairyInABottle"), "the use is in the fight's history, like a use by hand");
            f.SetHp(f.Player, 5);
            f.EndTurn();
            c.That(f.IsOver && !f.Won, "the next death is final");

            // 30% of 91 is 27.3: the resulting HP is cut down to a whole number.
            var g = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(Hitter()).Hp(5, 91).Start();
            g.SetPotion(0, "FairyInABottle");
            g.SetPotion(2, "FairyInABottle");
            g.EndTurn();
            c.Equal(27, g.Player.Hp, "30% of 91 -> 27");
            c.That(g.Potions[0] is null && g.Potions[2] is not null, "of two, only the first is used");
            g.SetHp(g.Player, 1);
            g.EndTurn();
            c.That(!g.IsOver && g.Player.Hp == 27 && g.Potions[2] is null, "the second one saves the next death");

            // A hit that does not kill leaves it alone.
            var h = With("FairyInABottle", Hitter());
            h.EndTurn();
            c.That(h.Player.Hp == 70 && h.Potions[0] is not null, "no death, no fairy");
        });

        c.Group("Bottled Potential: the hand is shuffled into the draw pile, then 5 are drawn", () =>
        {
            var f = With("BottledPotential");
            f.Play("Defend");
            var before = f.Rng.Shuffle.Counter;
            f.Do("potion:0");
            c.Equal(5, f.Hand.Count, "5 new cards in hand");
            c.Equal(5, f.DrawPile.Count, "4 from the hand, 5 from the draw pile and the discarded Defend made 10; 5 are left");
            c.Equal(0, f.DiscardPile.Count, "the discard pile was shuffled in with them");
            c.Equal(9, f.Rng.Shuffle.Counter - before, "one shuffle of 10 cards: 9 draws on the shuffle stream");
            c.Equal(5, f.Player.Block, "nothing else changed: the Defend's Block is still there");
        });
    }

    // ---------- the potions that work through a power of their own ----------

    private static void PowerPotions(Check c)
    {
        c.Group("the powers only potions use carry the game's title and kind", () =>
        {
            foreach (var (className, title, type) in ExpectedPowers)
            {
                var power = Content.Registry.NewPower(className);
                c.Equal(title, power.Name, $"{className} title");
                c.Equal(type, power.Type, $"{className} kind");
                c.Equal(PowerStackType.Counter, power.StackType, $"{className} shows an amount");
            }
        });

        c.Group("Regen Potion: 5 Regen heals 5, 4, 3, 2, 1 at the ends of the next five turns", () =>
        {
            var f = With("RegenPotion", Hitter());
            f.SetHp(f.Player, 50);
            f.Do("potion:0");
            c.Equal(5, f.Player.PowerAmount<RegenPower>(), "5 Regen");
            c.Equal(50, f.Player.Hp, "nothing is healed when it is drunk");

            // Each round: the heal at the end of the player's turn, then the dummy's 10.
            (int Hp, int Regen)[] rounds = [(45, 4), (39, 3), (32, 2), (24, 1), (15, 0), (5, 0)];
            for (var n = 0; n < rounds.Length; n++)
            {
                f.EndTurn();
                c.Equal(rounds[n].Hp, f.Player.Hp, $"HP after round {n + 1}");
                c.Equal(rounds[n].Regen, f.Player.PowerAmount<RegenPower>(), $"Regen after round {n + 1}");
            }
            c.That(!f.Player.HasPower<RegenPower>(), "Regen is gone after its fifth heal");

            var g = With("RegenPotion", Hitter());
            g.Do("potion:0");
            g.SetHp(g.Player, 78);
            g.EndTurn();
            c.Equal(70, g.Player.Hp, "the heal stops at max HP: 78 + 5 is 80, then the 10");
        });

        c.Group("Liquid Bronze: 3 Thorns answers every hit of an attack with 3 unpowered damage, before the hit", () =>
        {
            var f = With("LiquidBronze", new CheckDummy { Steps = [new DummyStep("HIT", Damage: 7, Hits: 2)] });
            f.Do("potion:0");
            c.Equal(3, f.Player.PowerAmount<ThornsPower>(), "3 Thorns");
            // Strength on the player and Vulnerable on the attacker change nothing: the answer is unpowered.
            f.ApplyPower<StrengthPower>(f.Player, 2, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 2, f.Player);
            f.EndTurn();
            c.Equal(94, f.Enemy(0).Hp, "two hits, 3 each");
            c.Equal(66, f.Player.Hp, "both hits still land: 80 - 14");
            c.Equal(3, f.Player.PowerAmount<ThornsPower>(), "Thorns does not wear off");

            // The attacker dies of the answer to its second hit: that hit still lands, the third is lost.
            var g = WithTwo("LiquidBronze", new CheckDummy { Hp = 5, Steps = [new DummyStep("HIT", Damage: 7, Hits: 3)] }, new CheckDummy());
            g.Do("potion:0");
            g.EndTurn();
            c.That(g.Enemy(0).IsDead, "5 HP: dead after two answers");
            c.Equal(66, g.Player.Hp, "two of the three hits landed");

            // A potion thrown at the owner's side is no attack; neither is damage with no dealer.
            var h = With("LiquidBronze");
            h.Do("potion:0");
            h.Damage(h.Player, 5, Props.NonCardUnpowered, h.Enemy(0));
            c.Equal(100, h.Enemy(0).Hp, "unpowered damage draws no answer");
            h.Damage(h.Player, 5, Props.MonsterMove, null);
            c.Equal(100, h.Enemy(0).Hp, "a hit with no dealer has nobody to answer");
            h.Damage(h.Player, 5, Props.MonsterMove, h.Enemy(0));
            c.Equal(97, h.Enemy(0).Hp, "a powered hit by a dealer does");
        });

        c.Group("Clarity Extract: draw 1 now, and 6 cards at the start of the next three turns", () =>
        {
            var f = With("Clarity");
            f.Do("potion:0");
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash", "Strike"], f.HandTitles(), "one card drawn at once");
            c.Equal(3, f.Player.PowerAmount<ClarityPower>(), "3 Clarity");
            (int Hand, int Clarity)[] turns = [(6, 2), (6, 1), (6, 0), (5, 0)];
            for (var n = 0; n < turns.Length; n++)
            {
                f.EndTurn();
                c.Equal(turns[n].Hand, f.Hand.Count, $"cards in hand on turn {n + 2}");
                c.Equal(turns[n].Clarity, f.Player.PowerAmount<ClarityPower>(), $"Clarity on turn {n + 2}, after its draw");
            }
        });

        c.Group("Duplicator: the next card played this turn runs twice", () =>
        {
            var f = With("Duplicator");
            f.Do("potion:0");
            c.Equal(1, f.Player.PowerAmount<DuplicationPower>(), "1 Duplication");
            f.Play("Bash", 0);
            // 8, then 2 Vulnerable; the second run hits a Vulnerable enemy for 12 and adds 2 more.
            c.Equal(80, f.Enemy(0).Hp, "8 + 12");
            c.Equal(4, f.Enemy(0).PowerAmount<VulnerablePower>(), "Vulnerable applied twice");
            c.Equal(1, f.Energy, "the cost is paid once");
            c.Equal(1, f.DiscardPile.Count(card => card.Title == "Bash"), "one Bash in the discard pile");
            c.That(!f.Player.HasPower<DuplicationPower>(), "the stack is spent");
            f.Play("Strike", 0);
            c.Equal(71, f.Enemy(0).Hp, "the next card runs once: 9 against Vulnerable");

            var g = With("Duplicator");
            g.Do("potion:0");
            g.EndTurn();
            c.That(!g.Player.HasPower<DuplicationPower>(), "unused, it is gone at the end of the turn");
            g.Play("Strike", 0);
            c.Equal(94, g.Enemy(0).Hp, "and the next turn's card runs once");

            var h = With("Duplicator");
            h.SetPotion(1, "Duplicator");
            h.Do("potion:0", "potion:1");
            c.Equal(2, h.Player.PowerAmount<DuplicationPower>(), "two potions stack");
            h.Play("Strike", 0);
            h.Play("Strike", 0);
            c.Equal(76, h.Enemy(0).Hp, "the next two cards each run twice");
            h.Play("Defend");
            c.Equal(5, h.Player.Block, "the third does not");
        });

        c.Group("Radiant Tincture: 1 energy now, and 1 more at the start of the next three turns", () =>
        {
            var f = With("RadiantTincture");
            f.Do("potion:0");
            c.Equal(4, f.Energy, "3 + 1");
            c.Equal(3, f.Player.PowerAmount<RadiancePower>(), "3 Radiance");
            (int Energy, int Radiance)[] turns = [(4, 2), (4, 1), (4, 0), (3, 0)];
            for (var n = 0; n < turns.Length; n++)
            {
                f.EndTurn();
                c.Equal(turns[n].Energy, f.Energy, $"energy on turn {n + 2}");
                c.Equal(turns[n].Radiance, f.Player.PowerAmount<RadiancePower>(), $"Radiance on turn {n + 2}");
            }
        });

        c.Group("Stable Serum: the hand is kept at the end of this turn and the next", () =>
        {
            var f = With("StableSerum");
            f.Do("potion:0");
            c.Equal(2, f.Player.PowerAmount<RetainHandPower>(), "2 Retain Hand");
            f.Play("Strike", 0);
            f.Play("Strike", 0);
            f.Put("Dazed", PileType.Hand);
            f.Put("Burn", PileType.Hand);
            f.EndTurn();
            // Kept: Defend, Defend, Bash. Dazed is Ethereal and Burn acts at the end of the turn: both are dealt with before the flush.
            c.Equal(1, f.ExhaustPile.Count, "the Ethereal Dazed is exhausted all the same");
            c.Equal(78, f.Player.Hp, "Burn still burns for 2");
            c.That(f.DiscardPile.Any(card => card.Title == "Burn"), "and goes to the discard pile");
            c.Sequence(["Defend", "Defend", "Bash", "Strike", "Strike", "Strike", "Defend", "Defend"], f.HandTitles(), "turn 2: the three kept cards, then the five drawn");
            c.Equal(1, f.Player.PowerAmount<RetainHandPower>(), "1 left");
            f.EndTurn();
            c.Equal(10, f.Hand.Count, "turn 3: eight kept, and the draw stops at ten");
            c.That(!f.Player.HasPower<RetainHandPower>(), "the power is gone");
            f.EndTurn();
            c.Equal(5, f.Hand.Count, "turn 4: the hand was discarded as usual");
        });

        c.Group("Powdered Demise: 9 Demise takes 9 HP at the end of each enemy turn, past Block, for good", () =>
        {
            var f = With("PowderedDemise", new CheckDummy { Steps = [new DummyStep("GUARD", Block: 20)] });
            f.Do("potion:0>e0");
            c.Equal(9, f.Enemy(0).PowerAmount<DemisePower>(), "9 Demise");
            c.Equal(100, f.Enemy(0).Hp, "nothing at once");
            f.EndTurn();
            c.Equal(91, f.Enemy(0).Hp, "9 at the end of its turn");
            c.Equal(20, f.Enemy(0).Block, "its Block is untouched");
            f.EndTurn();
            c.Equal(82, f.Enemy(0).Hp, "9 again");
            c.Equal(9, f.Enemy(0).PowerAmount<DemisePower>(), "it never wears off");

            var g = With("PowderedDemise", new CheckDummy { OnAdded = (x, me) => x.ApplyPower<ArtifactPower>(me, 1, me) });
            g.Do("potion:0>e0");
            c.That(!g.Enemy(0).HasPower<DemisePower>() && !g.Enemy(0).HasPower<ArtifactPower>(), "it is a debuff: Artifact stops it and is used up");

            // The last enemy dies of it after its own move: the fight is won at the end of the enemy turn.
            var h = With("PowderedDemise", new CheckDummy { Hp = 9, Steps = [new DummyStep("HIT", Damage: 10)] });
            h.Do("potion:0>e0");
            h.EndTurn();
            c.That(h.IsOver && h.Won, "the fight is won");
            c.Equal(76, h.Player.Hp, "after its last hit: 80 - 10, and Burning Blood's 6");
        });

        c.Group("Gigantification Potion: the next Attack card deals triple damage", () =>
        {
            var f = With("GigantificationPotion", Hitter());
            f.Do("potion:0");
            c.Equal(1, f.Player.PowerAmount<GigantificationPower>(), "1 Gigantification");
            c.Equal("Deal 18 damage.", HandText(f, 0), "the hand shows Strike tripled");
            c.Equal("Gain 5 Block.", HandText(f, 2), "Defend reads as before");
            f.Play("Strike", 0);
            c.Equal(282, f.Enemy(0).Hp, "18");
            c.That(!f.Player.HasPower<GigantificationPower>(), "the stack is spent");
            c.Equal("Deal 6 damage.", HandText(f, 0), "the other Strike reads 6 again");
            f.Play("Strike", 0);
            c.Equal(276, f.Enemy(0).Hp, "6");
            f.EndTurn();
            c.Equal(70, f.Player.Hp, "the enemy's hit was never tripled");

            // Strength is added first, then the tripling: (6 + 2) x 3.
            var g = With("GigantificationPotion");
            g.ApplyPower<StrengthPower>(g.Player, 2, g.Player);
            g.Do("potion:0");
            g.Play("Strike", 0);
            c.Equal(76, g.Enemy(0).Hp, "24");

            // One attack of several hits is tripled hit by hit.
            var h = With("GigantificationPotion");
            h.Put(Flurry, PileType.Hand);
            h.Do("potion:0");
            h.Play("Flurry", 0);
            c.Equal(82, h.Enemy(0).Hp, "9 twice");
            c.That(!h.Player.HasPower<GigantificationPower>(), "one stack for the whole attack");

            // A Skill that attacks is tripled while nothing has claimed the power, and does not use it up.
            var k = With("GigantificationPotion");
            k.Put(Jab, PileType.Hand);
            k.Do("potion:0");
            k.Play("Jab", 0);
            c.Equal(85, k.Enemy(0).Hp, "the Skill's 5 became 15");
            c.Equal(1, k.Player.PowerAmount<GigantificationPower>(), "and the stack is still there");
            k.Play("Strike", 0);
            c.Equal(67, k.Enemy(0).Hp, "for the Attack card that follows: 18");
            c.That(!k.Player.HasPower<GigantificationPower>(), "which spends it");

            // The power's own bookkeeping survives a copy of the fight taken in between.
            var m = With("GigantificationPotion");
            m.Do("potion:0");
            var copy = m.Clone();
            copy.Play("Strike", 0);
            c.Equal(82, copy.Enemy(0).Hp, "the copy plays its tripled Strike");
            c.Equal(1, m.Player.PowerAmount<GigantificationPower>(), "and the original still has its stack");
        });

        c.Group("Lucky Tonic: 1 Buffer turns the next HP loss into none", () =>
        {
            var f = With("LuckyTonic", Hitter());
            f.Do("potion:0");
            c.Equal(1, f.Player.PowerAmount<BufferPower>(), "1 Buffer");
            f.EndTurn();
            c.Equal(80, f.Player.Hp, "the 10 is not lost");
            c.That(!f.Player.HasPower<BufferPower>(), "Buffer is used up");
            f.EndTurn();
            c.Equal(70, f.Player.Hp, "the next hit lands");

            // A hit that Block takes whole costs no Buffer.
            var g = With("LuckyTonic", Hitter());
            g.Do("potion:0");
            g.Play("Defend");
            g.Play("Defend");
            g.EndTurn();
            c.Equal(1, g.Player.PowerAmount<BufferPower>(), "10 Block against 10: Buffer stays");
            g.EndTurn();
            c.That(g.Player.Hp == 80 && !g.Player.HasPower<BufferPower>(), "and stops the next one");

            // HP a card costs is HP lost too.
            var h = With("LuckyTonic");
            h.Put(Bleed, PileType.Hand);
            h.Do("potion:0");
            h.Play("Bleed");
            c.That(h.Player.Hp == 80 && !h.Player.HasPower<BufferPower>(), "Lose 3 HP: none lost, Buffer gone");

            // Of two hits it stops the first.
            var k = With("LuckyTonic", new CheckDummy { Steps = [new DummyStep("HIT", Damage: 7, Hits: 2)] });
            k.Do("potion:0");
            k.EndTurn();
            c.Equal(73, k.Player.Hp, "7 of 14");
        });

        c.Group("Mazaleth's Gift: 1 Ritual gives 1 Strength at the end of each of the owner's turns", () =>
        {
            var f = With("MazalethsGift");
            f.Do("potion:0");
            c.Equal(1, f.Player.PowerAmount<RitualPower>(), "1 Ritual");
            f.Play("Strike", 0);
            c.Equal(94, f.Enemy(0).Hp, "no Strength yet on the turn it is drunk");
            f.EndTurn();
            c.Equal(1, f.Player.PowerAmount<StrengthPower>(), "1 Strength after the first turn");
            f.Play("Strike", 0);
            c.Equal(87, f.Enemy(0).Hp, "7");
            f.EndTurn();
            c.Equal(2, f.Player.PowerAmount<StrengthPower>(), "2 after the second");
            c.Equal(1, f.Player.PowerAmount<RitualPower>(), "Ritual itself stays");

            // On an enemy, the end of the turn it arrived in is skipped.
            var g = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("RITE", Then: (x, me) => x.ApplyPower<RitualPower>(me, 2, me)), new DummyStep("WAIT"), new DummyStep("REST")] });
            g.EndTurn();
            c.Equal(0, g.Enemy(0).PowerAmount<StrengthPower>(), "an enemy's Ritual gives nothing at the end of the turn it was applied in");
            g.EndTurn();
            c.Equal(2, g.Enemy(0).PowerAmount<StrengthPower>(), "2 at the end of its next turn");
            g.EndTurn();
            c.Equal(4, g.Enemy(0).PowerAmount<StrengthPower>(), "and 2 more after that");
        });

        c.Group("Ship in a Bottle: 10 unpowered Block now and 10 more when Block is next cleared", () =>
        {
            var f = With("ShipInABottle", Hitter());
            f.ApplyPower<FrailPower>(f.Player, 3, f.Enemy(0));
            f.ApplyPower<DexterityPower>(f.Player, 2, f.Player);
            f.Do("potion:0");
            c.Equal(10, f.Player.Block, "10 now, with Frail and Dexterity in play");
            c.Equal(10, f.Player.PowerAmount<BlockNextTurnPower>(), "and 10 waiting");
            f.EndTurn();
            c.Equal(80, f.Player.Hp, "the dummy's 10 was blocked");
            c.Equal(10, f.Player.Block, "turn 2: the old Block fell, the waiting 10 arrived");
            c.That(!f.Player.HasPower<BlockNextTurnPower>(), "the power is gone");
            f.EndTurn();
            c.Equal(80, f.Player.Hp, "which blocks the next 10 as well");
            c.Equal(0, f.Player.Block, "turn 3: nothing more");
        });
    }

    // ---------- the potions that ask for cards ----------

    private static void ChoicePotions(Check c)
    {
        c.Group("Gambler's Brew: discard any number of hand cards, then draw as many", () =>
        {
            var f = With("GamblersBrew");
            f.Do("potion:0");
            var asked = f.Snapshot();
            c.Equal("hand_select", asked.Screen, "it asks for hand cards");
            c.Equal(0, asked.State["selection"]!["min"]!.GetValue<int>(), "none is allowed");
            c.That(asked.Actions.Any(a => a.Id == "confirm"), "so the choice can be confirmed at once");
            f.Do("select:0", "select:2", "confirm");
            c.Sequence(["Strike", "Defend"], f.DiscardPile.Select(card => card.Title), "the two picked cards are discarded, in the order picked");
            c.Sequence(["Strike", "Defend", "Bash", "Strike", "Strike"], f.HandTitles(), "and two cards are drawn");
            c.Equal(2, f.History.Count(e => e.Kind == HistoryKind.CardDiscarded), "two real discards");
            c.That(f.Potions[0] is null, "the potion is used up");

            var g = With("GamblersBrew");
            g.Do("potion:0", "confirm");
            c.Equal(5, g.Hand.Count, "with nothing picked nothing is discarded");
            c.Equal(5, g.DrawPile.Count, "and nothing is drawn");
            c.That(g.Potions[0] is null && g.Snapshot().Screen == "combat", "the potion is spent all the same");

            var h = With("GamblersBrew");
            h.Clear(PileType.Hand);
            h.Do("potion:0");
            c.That(h.Snapshot().Screen == "combat" && h.Hand.Count == 0, "with an empty hand nothing is asked and nothing drawn");
        });

        c.Group("Touch of Insanity: one hand card that costs energy costs 0 for the rest of the fight", () =>
        {
            var f = With("TouchOfInsanity");
            f.Put(Free, PileType.Hand);
            f.Put(XCard, PileType.Hand);
            f.Put("Wound", PileType.Hand);
            f.Do("potion:0");
            var asked = f.Snapshot();
            c.Equal("hand_select", asked.Screen, "it asks for a hand card");
            c.Sequence(["select:0", "select:1", "select:2", "select:3", "select:4"], asked.Actions.Select(a => a.Id),
                "only the cards that cost energy can be picked: not the free card, the X card or the Wound");
            f.Do("select:4", "confirm");
            var bash = f.Hand[4];
            c.Equal(0, f.CostOf(bash), "Bash costs 0");
            c.Equal(1, f.CostOf(f.Hand[0]), "the other cards cost what they did");
            f.Play("Bash", 0);
            c.Equal(3, f.Energy, "playing it takes no energy");
            f.EndTurn();
            c.Equal(0, f.CostOf(bash), "and it still costs 0 the next turn, wherever it is");

            // One candidate is taken without a question.
            var g = With("TouchOfInsanity");
            g.Clear(PileType.Hand);
            var strike = g.Put("StrikeIronclad", PileType.Hand);
            g.Put(Free, PileType.Hand);
            g.Do("potion:0");
            c.That(g.Snapshot().Screen == "combat" && g.CostOf(strike) == 0, "the only card that costs energy is made free at once");

            // No candidate: nothing is asked; the potion is spent.
            var h = With("TouchOfInsanity");
            h.Clear(PileType.Hand);
            h.Put(Free, PileType.Hand);
            h.Do("potion:0");
            c.That(h.Snapshot().Screen == "combat" && h.Potions[0] is null, "with no card that costs energy it does nothing");

            // A card whose own cost is 0 still counts while a power makes it cost energy; the power's part stays afterwards.
            var k = CheckKit.VsDummy();
            k.SetPotion(0, "TouchOfInsanity");
            k.Clear(PileType.Hand);
            var marked = k.Put("StrikeIronclad", PileType.Hand);
            marked.SetCostThisTurn(0);
            k.Put(Free, PileType.Hand);
            k.ApplyPower("TangledPower", k.Player, 1, k.Enemy(0));
            c.Equal(0, k.LocalCostOf(marked), "the Strike's own cost is 0");
            c.Equal(1, k.CostOf(marked), "Tangled makes it cost 1");
            k.Do("potion:0");
            c.Equal(1, k.CostOf(marked), "it was the one candidate; Tangled's 1 is still added to its 0");
            k.EndTurn();
            c.Equal(0, k.CostOf(marked), "once Tangled is gone it costs 0, although the this-turn change has ended");
        });
    }

    // ---------- what is left out ----------

    private static void LeftOut(Check c)
    {
        c.Group("the two potions the arena cannot make are left out by name, and every other potion of the pool is there", () =>
        {
            var registry = Content.Registry;
            foreach (var name in new[] { "ColorlessPotion", "EntropicBrew" })
                c.That(registry.IsExcluded(ContentKind.Potion, name, out var reason) && reason.Length > 0, $"{name} is left out with a reason");
            c.Equal(2, registry.Excluded.Keys.Count(k => k.Kind == ContentKind.Potion), "no other potion is left out");
            c.Equal(48, Scenarios.PotionPool.Count, "the Ironclad's potion pool has 48 potions");
            c.Equal(46, Scenarios.PotionPool.Count(p => registry.Has(ContentKind.Potion, p.ClassName)), "46 of them are in the registry");
            foreach (var (name, rarity) in Scenarios.PotionPool)
                if (registry.Has(ContentKind.Potion, name)) c.Equal(rarity, registry.Potion(name).Rarity, $"{name} has the pool's rarity");
        });
    }
}
