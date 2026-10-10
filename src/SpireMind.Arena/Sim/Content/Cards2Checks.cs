namespace SpireMind.Arena;

/// <summary>
/// Scripted checks of the 29 cards of Cards2 (cards-2.json) and their powers. Every expected
/// number was worked out by hand from the spec and the game's classes, not taken from a run.
/// The usual board is <see cref="CheckKit.VsDummy"/>: 80 HP, 3 energy, the hand Strike, Strike,
/// Defend, Defend, Bash, the draw pile Strike, Strike, Strike, Defend, Defend from the top, and
/// one idle dummy with 100 HP.
/// </summary>
public sealed class Cards2Checks : IArenaChecks
{
    /// <summary>1 Block: under Frail the gain is 0.75, which adds nothing and still counts as a gain.</summary>
    private static readonly CardDef Chip = new("CheckChip", "Chip", CardType.Skill, CardRarity.Common, 0, TargetType.Self)
    {
        Vars = { ["Block"] = 1 },
        Play = c => c.GainBlock(c.V("Block")),
    };

    /// <summary>Makes its owner impossible to hit, so that no enemy is hittable (Hellraiser's cap, Havoc without a target).</summary>
    private sealed class Untouchable : Power
    {
        public override string Name => "Untouchable";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";
        public override bool ShouldAllowHitting(Fight f, Creature creature) => !ReferenceEquals(creature, Owner);
    }

    /// <summary>Played Skills go to the exhaust pile (the result-pile half of the game's Corruption).</summary>
    private sealed class SkillsExhaust : Power
    {
        public override string Name => "Skills Exhaust";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";

        public override (PileType Pile, PilePosition Position) ModifyCardPlayResultPile(Fight f, CardInstance card, bool isAutoPlay, PileType pile, PilePosition position) =>
            card.Type == CardType.Skill ? (PileType.Exhaust, position) : (pile, position);
    }

    /// <summary>Draws a card whenever a card is exhausted (the shape of the game's Dark Embrace).</summary>
    private sealed class DrawOnExhaust : Power
    {
        public override string Name => "Draw On Exhaust";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";
        public override void AfterCardExhausted(Fight f, CardInstance card, bool causedByEthereal) => f.Draw(1);
    }

    /// <summary>Forbids plays by effects, and every draw that is not the hand draw (the shapes of a play-blocking hook and of No Draw).</summary>
    private sealed class Lockdown : Power
    {
        public override string Name => "Lockdown";
        public override PowerType Type => PowerType.Debuff;
        public override string Describe() => "";
        public override bool ShouldPlay(Fight f, CardInstance card, AutoPlayType autoPlay) => autoPlay == AutoPlayType.None;
        public override bool ShouldDraw(Fight f, bool fromHandDraw) => fromHandDraw;
    }

    /// <summary>Counts attack commands and power changes it hears.</summary>
    private sealed class Tally : Power
    {
        public int Attacks, PowerChanges;
        public override string Name => "Tally";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";
        public override void BeforeAttack(Fight f, AttackCommand attack) => Attacks++;
        public override void AfterPowerAmountChanged(Fight f, Power power, decimal amount, Creature? applier, CardInstance? cardSource) => PowerChanges++;
    }

    private static CardInstance InHand(Fight f, string card) => f.Put(card, PileType.Hand);

    private static CardInstance OnDraw(Fight f, string card) => f.Put(card, PileType.Draw, PilePosition.Top);

    private static string Text(Fight f, CardInstance card) => Fight.CardText(f, card, PileType.Hand);

    /// <summary>Where a card is now, found by its id: answering a card choice replays the action
    /// on a copy of the fight, so a card object held from before the choice is no longer the live one.</summary>
    private static PileType PileOf(Fight f, CardInstance card) => f.AllCards.FirstOrDefault(x => x.Id == card.Id)?.Pile ?? PileType.None;

    private static CheckDummy Hitter(int damage, int hits = 1, int hp = 100) =>
        new() { Hp = hp, Steps = [new DummyStep("HIT", Damage: damage, Hits: hits)] };

    public void Run(Check c)
    {
        Table(c);
        DrumOfBattle(c);
        EvilEye(c);
        ExpectAFight(c);
        Feed(c);
        FeelNoPain(c);
        FiendFire(c);
        FightMe(c);
        FlameBarrier(c);
        ForgottenRitual(c);
        Havoc(c);
        Headbutt(c);
        Hellraiser(c);
        Hemokinesis(c);
        HowlFromBeyond(c);
        Impervious(c);
        InfernalBlade(c);
        Inferno(c);
        Inflame(c);
        IronWave(c);
        Juggernaut(c);
        Juggling(c);
        Mangle(c);
        MoltenFist(c);
        NotYet(c);
        Offering(c);
        OneTwoPunch(c);
        PactsEnd(c);
        PerfectedStrike(c);
        Pillage(c);
        PowersAsShown(c);
        Copies(c);
    }

    // ---------- what the registry holds ----------

    private static void Table(Check c)
    {
        c.Group("cards-2.json: class, title, type, rarity, cost, target, keywords, upgraded cost", () =>
        {
            const CardKeywords none = CardKeywords.None, exhaust = CardKeywords.Exhaust;
            (string Class, string Title, CardType Type, CardRarity Rarity, int Cost, TargetType Target, CardKeywords Keywords, int UpgradedCost)[] rows =
            [
                ("DrumOfBattle", "Drum of Battle", CardType.Skill, CardRarity.Uncommon, 1, TargetType.Self, none, 1),
                ("EvilEye", "Evil Eye", CardType.Skill, CardRarity.Uncommon, 1, TargetType.Self, none, 1),
                ("ExpectAFight", "Expect a Fight", CardType.Skill, CardRarity.Uncommon, 2, TargetType.Self, none, 1),
                ("Feed", "Feed", CardType.Attack, CardRarity.Rare, 1, TargetType.AnyEnemy, exhaust, 1),
                ("FeelNoPain", "Feel No Pain", CardType.Power, CardRarity.Uncommon, 1, TargetType.Self, none, 1),
                ("FiendFire", "Fiend Fire", CardType.Attack, CardRarity.Rare, 2, TargetType.AnyEnemy, exhaust, 2),
                ("FightMe", "Fight Me!", CardType.Attack, CardRarity.Uncommon, 2, TargetType.AnyEnemy, none, 2),
                ("FlameBarrier", "Flame Barrier", CardType.Skill, CardRarity.Uncommon, 2, TargetType.Self, none, 2),
                ("ForgottenRitual", "Forgotten Ritual", CardType.Skill, CardRarity.Uncommon, 1, TargetType.Self, exhaust, 1),
                ("Havoc", "Havoc", CardType.Skill, CardRarity.Common, 1, TargetType.Self, none, 0),
                ("Headbutt", "Headbutt", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy, none, 1),
                ("Hellraiser", "Hellraiser", CardType.Power, CardRarity.Rare, 2, TargetType.Self, none, 1),
                ("Hemokinesis", "Hemokinesis", CardType.Attack, CardRarity.Uncommon, 1, TargetType.AnyEnemy, none, 1),
                ("HowlFromBeyond", "Howl from Beyond", CardType.Attack, CardRarity.Uncommon, 3, TargetType.AllEnemies, none, 3),
                ("Impervious", "Impervious", CardType.Skill, CardRarity.Rare, 2, TargetType.Self, exhaust, 2),
                ("InfernalBlade", "Infernal Blade", CardType.Skill, CardRarity.Uncommon, 1, TargetType.Self, exhaust, 0),
                ("Inferno", "Inferno", CardType.Power, CardRarity.Uncommon, 1, TargetType.Self, none, 1),
                ("Inflame", "Inflame", CardType.Power, CardRarity.Uncommon, 1, TargetType.Self, none, 1),
                ("IronWave", "Iron Wave", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy, none, 1),
                ("Juggernaut", "Juggernaut", CardType.Power, CardRarity.Rare, 2, TargetType.Self, none, 2),
                ("Juggling", "Juggling", CardType.Power, CardRarity.Uncommon, 1, TargetType.Self, none, 1),
                ("Mangle", "Mangle", CardType.Attack, CardRarity.Rare, 3, TargetType.AnyEnemy, none, 3),
                ("MoltenFist", "Molten Fist", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy, exhaust, 1),
                ("NotYet", "Not Yet", CardType.Skill, CardRarity.Rare, 2, TargetType.Self, exhaust, 2),
                ("Offering", "Offering", CardType.Skill, CardRarity.Rare, 0, TargetType.Self, exhaust, 0),
                ("OneTwoPunch", "One-Two Punch", CardType.Skill, CardRarity.Rare, 1, TargetType.Self, none, 1),
                ("PactsEnd", "Pact's End", CardType.Attack, CardRarity.Rare, 0, TargetType.AllEnemies, none, 0),
                ("PerfectedStrike", "Perfected Strike", CardType.Attack, CardRarity.Common, 2, TargetType.AnyEnemy, none, 2),
                ("Pillage", "Pillage", CardType.Attack, CardRarity.Uncommon, 1, TargetType.AnyEnemy, none, 1),
            ];
            c.Equal(29, rows.Length, "29 cards in cards-2.json");
            var f = CheckKit.VsDummy();
            foreach (var row in rows)
            {
                var def = Content.Registry.Card(row.Class);
                c.Equal(row.Title, def.Name, $"{row.Class}: title");
                c.Equal(row.Type, def.Type, $"{row.Class}: type");
                c.Equal(row.Rarity, def.Rarity, $"{row.Class}: rarity");
                c.Equal(row.Cost, def.Cost, $"{row.Class}: cost");
                c.That(!def.CostsX, $"{row.Class}: not an X card");
                c.Equal(row.Target, def.Target, $"{row.Class}: target");
                c.Equal(row.Keywords, def.Keywords, $"{row.Class}: keywords");
                c.Equal(1, def.MaxUpgradeLevel, $"{row.Class}: upgradable once");
                var plus = f.CreateCard(row.Class, upgraded: true);
                c.Equal(row.UpgradedCost, plus.BaseCost, $"{row.Class}+: cost");
                c.Equal(row.Title + "+", plus.Title, $"{row.Class}+: title");
                c.Equal(row.Class == "Juggling" ? CardKeywords.Innate : row.Keywords, plus.Keywords, $"{row.Class}+: keywords");
                c.Equal(row.Class is "Feed" or "NotYet" ? false : true, def.IsGeneratableInCombat, $"{row.Class}: random generation");
                c.Equal(row.Class == "PerfectedStrike", plus.HasTag(CardTags.Strike), $"{row.Class}: Strike tag");
            }
            foreach (var power in new[] { "FeelNoPainPower", "FlameBarrierPower", "HellraiserPower", "InfernoPower", "JuggernautPower", "JugglingPower", "ManglePower", "NoEnergyGainPower", "OneTwoPunchPower" })
                c.That(Content.Registry.Has(ContentKind.Power, power), $"{power} is registered");

            (string Class, string Name, PowerType Type, PowerStackType Stack)[] powers =
            [
                ("FeelNoPainPower", "Feel No Pain", PowerType.Buff, PowerStackType.Counter),
                ("FlameBarrierPower", "Flame Barrier", PowerType.Buff, PowerStackType.Counter),
                ("HellraiserPower", "Hellraiser", PowerType.Buff, PowerStackType.Single),
                ("InfernoPower", "Inferno", PowerType.Buff, PowerStackType.Counter),
                ("JuggernautPower", "Juggernaut", PowerType.Buff, PowerStackType.Counter),
                ("JugglingPower", "Juggling", PowerType.Buff, PowerStackType.Counter),
                ("ManglePower", "Mangle", PowerType.Debuff, PowerStackType.Counter),
                ("NoEnergyGainPower", "No Energy Gain", PowerType.Debuff, PowerStackType.Single),
                ("OneTwoPunchPower", "One-Two Punch", PowerType.Buff, PowerStackType.Counter),
            ];
            foreach (var row in powers)
            {
                var power = Content.Registry.NewPower(row.Class);
                c.Equal(row.Name, power.Name, $"{row.Class}: name");
                c.Equal(row.Type, power.Type, $"{row.Class}: type");
                c.Equal(row.Stack, power.StackType, $"{row.Class}: stack type");
                c.Equal(PowerInstanceType.None, power.InstanceType, $"{row.Class}: one instance, amounts add");
            }
        });
    }

    // ---------- Drum of Battle ----------

    private static void DrumOfBattle(Check c)
    {
        c.Group("Drum of Battle: draws 2 and goes to the discard pile; no energy from being played", () =>
        {
            var f = CheckKit.VsDummy();
            var drum = InHand(f, "DrumOfBattle");
            c.Equal("Draw 2 cards. Whenever this card is Exhausted, gain 2 energy.", Text(f, drum), "text");
            f.Play("DrumOfBattle");
            c.Equal(2, f.Energy, "costs 1, gives nothing back");
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash", "Strike", "Strike"], f.HandTitles(), "the two top cards were drawn");
            c.Equal(PileType.Discard, drum.Pile, "no Exhaust keyword: to the discard pile");
            c.Equal(0, f.ExhaustPile.Count, "nothing was exhausted");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "DrumOfBattle+");
            c.Equal("Draw 2 cards. Whenever this card is Exhausted, gain 3 energy.", Text(g, plus), "upgraded text: only the energy changes");
            g.Play("DrumOfBattle");
            c.Equal(7, g.Hand.Count, "upgraded: still draws 2");
            c.Equal(2, g.Energy, "upgraded: still costs 1");
        });

        c.Group("Drum of Battle: energy when another effect exhausts it", () =>
        {
            // Fiend Fire exhausts the six hand cards, Drum among them: 3 - 2 + 2 energy, 6 x 7 damage.
            var f = CheckKit.VsDummy();
            InHand(f, "DrumOfBattle");
            InHand(f, "FiendFire");
            f.Play("FiendFire", 0);
            c.Equal(3, f.Energy, "base: +2 energy when exhausted");
            c.Equal(58, f.Enemy(0).Hp, "Fiend Fire hit once per exhausted card");
            c.Equal(7, f.ExhaustPile.Count, "six hand cards and Fiend Fire");

            var g = CheckKit.VsDummy();
            InHand(g, "DrumOfBattle+");
            InHand(g, "FiendFire");
            g.Play("FiendFire", 0);
            c.Equal(4, g.Energy, "upgraded: +3 energy when exhausted");

            // Havoc plays it from the top of the draw pile (draw 2) and then exhausts it.
            var h = CheckKit.VsDummy();
            InHand(h, "Havoc");
            var drum = OnDraw(h, "DrumOfBattle");
            h.Play("Havoc");
            c.Equal(PileType.Exhaust, drum.Pile, "Havoc exhausted it");
            c.Equal(7, h.Hand.Count, "its own play drew 2");
            c.Equal(4, h.Energy, "3 - 1 for Havoc + 2 from the exhaust");
        });

        c.Group("Drum of Battle: the gain goes through the energy modifiers, once per play count", () =>
        {
            // Expect a Fight first: Strike, Strike, Bash and Fiend Fire are 4 Attacks: 3 - 2 + 4 = 5.
            var f = CheckKit.VsDummy();
            InHand(f, "ExpectAFight");
            InHand(f, "DrumOfBattle");
            InHand(f, "FiendFire");
            f.Play("ExpectAFight");
            c.Equal(5, f.Energy, "Expect a Fight's own gain is not blocked");
            f.Play("FiendFire", 0);
            c.Equal(3, f.Energy, "No Energy Gain turns Drum's energy into 0");
            c.Equal(58, f.Enemy(0).Hp, "six cards were exhausted");

            // A card that is played one extra time gives its energy twice.
            var g = CheckKit.VsDummy();
            var drum = InHand(g, "DrumOfBattle");
            drum.ReplayCount = 1;
            InHand(g, "FiendFire");
            g.Play("FiendFire", 0);
            c.Equal(5, g.Energy, "3 - 2 + 2 x 2");

            // Its own play sends it to the exhaust pile (as under Corruption): draw 2, then the energy.
            var k = CheckKit.VsDummy();
            k.ApplyPower<SkillsExhaust>(k.Player, 1, k.Player);
            var own = InHand(k, "DrumOfBattle");
            k.Play("DrumOfBattle");
            c.Equal(PileType.Exhaust, own.Pile, "exhausted by its own play");
            c.Equal(7, k.Hand.Count, "drew 2 first");
            c.Equal(4, k.Energy, "3 - 1 + 2");

            // One-Two Punch only changes Attacks: no extra energy, and its charge is not spent.
            var h = CheckKit.VsDummy();
            h.ApplyPower<OneTwoPunchPower>(h.Player, 1, h.Player);
            var d = InHand(h, "DrumOfBattle");
            h.RunAsAction(x => x.Exhaust(d));
            c.Equal(5, h.Energy, "3 + 2, once");
            c.Equal(1, h.Player.PowerAmount<OneTwoPunchPower>(), "One-Two Punch keeps its charge");
        });
    }

    // ---------- Evil Eye ----------

    private static void EvilEye(Check c)
    {
        c.Group("Evil Eye: 8 Block (11), once", () =>
        {
            var f = CheckKit.VsDummy();
            var eye = InHand(f, "EvilEye");
            c.Equal("Gain 8 Block. If a card was Exhausted this turn, gain 8 Block again.", Text(f, eye), "text");
            f.Play("EvilEye");
            c.Equal(8, f.Player.Block, "8 Block");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(PileType.Discard, eye.Pile, "to the discard pile");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "EvilEye+");
            c.Equal("Gain 11 Block. If a card was Exhausted this turn, gain 11 Block again.", Text(g, plus), "upgraded text");
            g.Play("EvilEye");
            c.Equal(11, g.Player.Block, "upgraded: 11 Block");
        });

        c.Group("Evil Eye: a second, separate gain after an exhaust this turn", () =>
        {
            var f = CheckKit.VsDummy();
            InHand(f, "Slimed");
            InHand(f, "EvilEye");
            f.Play("Slimed");
            f.Play("EvilEye");
            c.Equal(16, f.Player.Block, "8 + 8");
            c.Equal(1, f.Energy, "1 for Slimed, 1 for Evil Eye");

            var g = CheckKit.VsDummy();
            InHand(g, "Slimed");
            InHand(g, "EvilEye+");
            g.Play("Slimed");
            g.Play("EvilEye");
            c.Equal(22, g.Player.Block, "upgraded: 11 + 11");

            // Dexterity 2 and Frail: each gain is (8 + 2) x 0.75 = 7.5 and is truncated on its own: 7, then 14 (one gain of 15 would be 15).
            var h = CheckKit.VsDummy();
            h.ApplyPower<DexterityPower>(h.Player, 2, h.Player);
            h.ApplyPower<FrailPower>(h.Player, 1, h.Enemy(0));
            InHand(h, "Slimed");
            var eye = InHand(h, "EvilEye");
            c.Equal("Gain 7 Block. If a card was Exhausted this turn, gain 7 Block again.", Text(h, eye), "the text carries Dexterity and Frail");
            h.Play("Slimed");
            h.Play("EvilEye");
            c.Equal(14, h.Player.Block, "two gains of 7.5");

            // Two gains are two Juggernaut hits.
            var j = CheckKit.VsDummy();
            j.ApplyPower<JuggernautPower>(j.Player, 6, j.Player);
            InHand(j, "Slimed");
            InHand(j, "EvilEye");
            j.Play("Slimed");
            j.Play("EvilEye");
            c.Equal(88, j.Enemy(0).Hp, "Juggernaut answered each gain");
            c.Equal(2, j.Rng.CombatTargets.Counter, "one target draw per gain");
        });

        c.Group("Evil Eye: what does not count as exhausted this turn", () =>
        {
            // An exhaust of the turn before.
            var f = CheckKit.VsDummy();
            InHand(f, "Slimed");
            f.Play("Slimed");
            f.EndTurn();
            InHand(f, "EvilEye");
            f.Play("EvilEye");
            c.Equal(8, f.Player.Block, "last turn's exhaust does not count");

            // An Ethereal card exhausted at the end of the turn before.
            var g = CheckKit.VsDummy();
            InHand(g, "Dazed");
            g.EndTurn();
            c.Equal(1, g.ExhaustPile.Count, "Dazed was exhausted at end of turn");
            InHand(g, "EvilEye");
            g.Play("EvilEye");
            c.Equal(8, g.Player.Block, "an Ethereal exhaust of the last turn does not count");

            // A played Power card leaves the fight; it is not exhausted.
            var h = CheckKit.VsDummy();
            InHand(h, "Inflame");
            InHand(h, "EvilEye");
            h.Play("Inflame");
            h.Play("EvilEye");
            c.Equal(8, h.Player.Block, "a played Power is not an exhaust");

            // When its own play exhausts it, that comes after the effect: once for the first, twice for the second.
            var k = CheckKit.VsDummy();
            k.ApplyPower<SkillsExhaust>(k.Player, 1, k.Player);
            InHand(k, "EvilEye");
            InHand(k, "EvilEye");
            k.Play("EvilEye");
            c.Equal(8, k.Player.Block, "its own exhaust does not count for itself");
            k.Play("EvilEye");
            c.Equal(24, k.Player.Block, "but it counts for the next one: 8 + 16");
        });
    }

    // ---------- Expect a Fight ----------

    private static void ExpectAFight(Check c)
    {
        c.Group("Expect a Fight: 1 energy per Attack in hand, then No Energy Gain", () =>
        {
            var f = CheckKit.VsDummy();
            var card = InHand(f, "ExpectAFight");
            c.Equal("Gain 1 energy for each Attack in your hand (now 3). You cannot gain energy for the rest of this turn.", Text(f, card), "text in hand, with the live count");
            c.Equal("Gain 1 energy for each Attack in your hand. You cannot gain energy for the rest of this turn.", Fight.CardText(null, card, PileType.Deck), "text outside a fight");
            c.Equal(2, f.CostOf(card), "costs 2");
            f.Play("ExpectAFight");
            c.Equal(4, f.Energy, "3 - 2 + 3 (Strike, Strike, Bash)");
            c.Equal(1, f.Player.PowerAmount<NoEnergyGainPower>(), "No Energy Gain 1");
            c.Equal(PowerType.Debuff, f.Player.GetPower<NoEnergyGainPower>()!.Type, "a Debuff");
            c.Equal(PileType.Discard, card.Pile, "to the discard pile");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "ExpectAFight+");
            c.Equal(1, g.CostOf(plus), "upgraded: costs 1");
            g.Play("ExpectAFight");
            c.Equal(5, g.Energy, "upgraded: 3 - 1 + 3");

            // No Attack in hand: nothing is gained, the power still lands.
            var h = CheckKit.VsDummy();
            h.Clear(PileType.Hand);
            InHand(h, "ExpectAFight");
            InHand(h, "DefendIronclad");
            h.Play("ExpectAFight");
            c.Equal(1, h.Energy, "no Attack, no energy");
            c.That(h.Player.HasPower<NoEnergyGainPower>(), "the power is applied all the same");
        });

        c.Group("Expect a Fight: later gains this turn are 0; the next turn is untouched", () =>
        {
            var f = CheckKit.VsDummy();
            InHand(f, "ExpectAFight");
            InHand(f, "Offering");
            f.Play("ExpectAFight");
            f.Play("Offering");
            c.Equal(4, f.Energy, "Offering's 2 energy became 0");
            c.Equal(74, f.Player.Hp, "Offering still cost 6 HP");
            c.Equal(8, f.Hand.Count, "and still drew 3");
            f.GainEnergy(2);
            c.Equal(4, f.Energy, "any gain through the command is 0");

            f.EndTurn();
            c.That(!f.Player.HasPower<NoEnergyGainPower>(), "removed at the end of the player's own turn");
            c.Equal(3, f.Energy, "the turn's energy is the usual 3");
            f.GainEnergy(2);
            c.Equal(5, f.Energy, "gains work again");
        });

        c.Group("Expect a Fight: Artifact on the player stops the debuff", () =>
        {
            var f = CheckKit.VsDummy();
            f.ApplyPower<ArtifactPower>(f.Player, 1, f.Player);
            InHand(f, "ExpectAFight");
            InHand(f, "Offering");
            f.Play("ExpectAFight");
            c.Equal(4, f.Energy, "the gain itself");
            c.That(!f.Player.HasPower<NoEnergyGainPower>(), "blocked");
            c.That(!f.Player.HasPower<ArtifactPower>(), "Artifact is used up");
            f.Play("Offering");
            c.Equal(6, f.Energy, "so Offering's energy arrives");
        });
    }

    // ---------- Feed ----------

    private static void Feed(Check c)
    {
        c.Group("Feed: 10 damage (12); no kill, no max HP", () =>
        {
            var f = CheckKit.VsDummy();
            var feed = InHand(f, "Feed");
            c.Equal("Deal 10 damage. If this kills an enemy that is not a Minion, gain 3 Max HP. / Exhaust.", Text(f, feed), "text");
            f.Play("Feed", 0);
            c.Equal(90, f.Enemy(0).Hp, "10 damage");
            c.Equal(80, f.Player.MaxHp, "no kill: max HP unchanged");
            c.Equal(PileType.Exhaust, feed.Pile, "exhausted");
            c.Equal(2, f.Energy, "costs 1");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "Feed+");
            c.Equal("Deal 12 damage. If this kills an enemy that is not a Minion, gain 4 Max HP. / Exhaust.", Text(g, plus), "upgraded text");
            g.Play("Feed", 0);
            c.Equal(88, g.Enemy(0).Hp, "upgraded: 12 damage");
        });

        c.Group("Feed: a kill raises max HP and heals the same", () =>
        {
            var f = CheckKit.VsDummies(new CheckDummy { Hp = 10 }, new CheckDummy());
            f.SetHp(f.Player, 70);
            InHand(f, "Feed");
            f.Play("Feed", 0);
            c.That(f.Enemy(0).IsDead, "10 damage kills 10 HP");
            c.Equal(83, f.Player.MaxHp, "+3 max HP");
            c.Equal(73, f.Player.Hp, "and 3 HP");

            var g = CheckKit.VsDummies(new CheckDummy { Hp = 12 }, new CheckDummy());
            g.SetHp(g.Player, 70);
            InHand(g, "Feed+");
            g.Play("Feed", 0);
            c.Equal(84, g.Player.MaxHp, "upgraded: +4 max HP");
            c.Equal(74, g.Player.Hp, "upgraded: and 4 HP");

            // At full HP the heal follows the new maximum.
            var h = CheckKit.VsDummies(new CheckDummy { Hp = 10 }, new CheckDummy());
            InHand(h, "Feed");
            h.Play("Feed", 0);
            c.Equal(83, h.Player.Hp, "80/80 becomes 83/83");

            // 10 x 1.5 = 15 kills 15 HP through Vulnerable.
            var v = CheckKit.VsDummies(new CheckDummy { Hp = 15 }, new CheckDummy());
            v.ApplyPower<VulnerablePower>(v.Enemy(0), 1, v.Player);
            InHand(v, "Feed");
            v.Play("Feed", 0);
            c.Equal(83, v.Player.MaxHp, "a kill through Vulnerable counts");

            // Block keeps the target alive: 5 of the 10 are absorbed.
            var b = CheckKit.VsDummies(new CheckDummy { Hp = 10 }, new CheckDummy());
            b.SetBlock(b.Enemy(0), 5);
            InHand(b, "Feed");
            b.Play("Feed", 0);
            c.Equal(5, b.Enemy(0).Hp, "5 HP left");
            c.Equal(80, b.Player.MaxHp, "no kill, no max HP");
        });

        c.Group("Feed: the kill that ends the fight still counts; a Minion never does", () =>
        {
            // 70/80: +3 max HP and +3 HP from Feed, then Burning Blood's 6.
            var f = CheckKit.VsDummy(new CheckDummy { Hp = 10 });
            f.SetHp(f.Player, 70);
            InHand(f, "Feed");
            f.Play("Feed", 0);
            c.That(f.IsOver && f.Won, "the fight is won");
            c.Equal(83, f.Player.MaxHp, "max HP 83 after the fight");
            c.Equal(79, f.Player.Hp, "70 + 3 + 6");

            var g = CheckKit.VsDummies(new CheckDummy(), new CheckDummy { Hp = 5, OnAdded = (x, me) => x.ApplyPower<MinionPower>(me, 1, me) });
            InHand(g, "Feed");
            g.Play("Feed", 1);
            c.That(g.Enemy(1).IsDead, "the Minion died");
            c.Equal(80, g.Player.MaxHp, "a Minion's death is not Fatal");
            c.Equal(80, g.Player.Hp, "no heal either");
        });
    }

    // ---------- Feel No Pain ----------

    private static void FeelNoPain(Check c)
    {
        c.Group("Feel No Pain: 3 Block (4) whenever a card is exhausted", () =>
        {
            var f = CheckKit.VsDummy();
            var card = InHand(f, "FeelNoPain");
            InHand(f, "Slimed");
            c.Equal("Whenever a card is Exhausted, gain 3 Block.", Text(f, card), "text");
            f.Play("FeelNoPain");
            c.Equal(3, f.Player.PowerAmount<FeelNoPainPower>(), "the power, 3");
            c.Equal(PileType.None, card.Pile, "a played Power leaves the fight");
            c.Equal(0, f.Player.Block, "playing a Power is not an exhaust");
            f.Play("Slimed");
            c.Equal(3, f.Player.Block, "Slimed was exhausted: 3 Block");
            c.Equal(1, f.Energy, "1 + 1");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "FeelNoPain+");
            InHand(g, "Slimed");
            c.Equal("Whenever a card is Exhausted, gain 4 Block.", Text(g, plus), "upgraded text");
            g.Play("FeelNoPain");
            g.Play("Slimed");
            c.Equal(4, g.Player.Block, "upgraded: 4 Block");

            // Two plays add up.
            var h = CheckKit.VsDummy();
            InHand(h, "FeelNoPain");
            InHand(h, "FeelNoPain+");
            InHand(h, "Slimed");
            h.Play("FeelNoPain");
            h.Play("FeelNoPain");
            c.Equal(7, h.Player.PowerAmount<FeelNoPainPower>(), "3 + 4");
            h.Play("Slimed");
            c.Equal(7, h.Player.Block, "7 Block per exhaust");
        });

        c.Group("Feel No Pain: unpowered Block, every cause of exhaust", () =>
        {
            // Dexterity and Frail do not touch it.
            var f = CheckKit.VsDummy();
            f.ApplyPower<FeelNoPainPower>(f.Player, 3, f.Player);
            f.ApplyPower<DexterityPower>(f.Player, 2, f.Player);
            f.ApplyPower<FrailPower>(f.Player, 1, f.Enemy(0));
            InHand(f, "Slimed");
            f.Play("Slimed");
            c.Equal(3, f.Player.Block, "not 5, not 3.75");

            // An Ethereal card exhausted at end of turn: the Block is there for the enemy's hit of 5.
            var g = CheckKit.VsDummy(Hitter(5));
            g.ApplyPower<FeelNoPainPower>(g.Player, 3, g.Player);
            InHand(g, "Dazed");
            g.EndTurn();
            c.Equal(78, g.Player.Hp, "3 of the 5 were blocked");

            // Fiend Fire: five hand cards, then itself: 6 x 3 Block; 5 hits of 7.
            var h = CheckKit.VsDummy();
            InHand(h, "FeelNoPain");
            InHand(h, "FiendFire");
            h.Play("FeelNoPain");
            h.Play("FiendFire", 0);
            c.Equal(18, h.Player.Block, "six exhausts");
            c.Equal(65, h.Enemy(0).Hp, "five hits");

            // Havoc playing a Power card: the Power leaves the fight, nothing is exhausted.
            var p = CheckKit.VsDummy();
            p.ApplyPower<FeelNoPainPower>(p.Player, 3, p.Player);
            InHand(p, "Havoc");
            OnDraw(p, "Inflame");
            p.Play("Havoc");
            c.Equal(0, p.Player.Block, "no exhaust, no Block");
            c.Equal(2, p.Player.PowerAmount<StrengthPower>(), "Inflame was played");
        });
    }

    // ---------- Fiend Fire ----------

    private static void FiendFire(Check c)
    {
        c.Group("Fiend Fire: exhausts the hand, then one hit of 7 (10) per card", () =>
        {
            var f = CheckKit.VsDummy();
            var fire = InHand(f, "FiendFire");
            c.Equal("Exhaust all cards in your hand. Deal 7 damage for each of them. / Exhaust.", Text(f, fire), "text");
            f.Play("FiendFire", 0);
            c.Equal(65, f.Enemy(0).Hp, "5 x 7");
            c.Equal(0, f.Hand.Count, "the hand is gone");
            c.Equal(6, f.ExhaustPile.Count, "five cards and Fiend Fire itself");
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash", "Fiend Fire"], f.ExhaustPile.Select(x => x.Title), "exhausted in hand order, Fiend Fire last");
            c.Equal(1, f.Energy, "costs 2");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "FiendFire+");
            c.Equal("Exhaust all cards in your hand. Deal 10 damage for each of them. / Exhaust.", Text(g, plus), "upgraded text");
            g.Play("FiendFire", 0);
            c.Equal(50, g.Enemy(0).Hp, "upgraded: 5 x 10");
        });

        c.Group("Fiend Fire: every hit is its own damage instance", () =>
        {
            // (7 + 2) x 1.5 = 13.5 -> 13 per hit.
            var f = CheckKit.VsDummy();
            f.ApplyPower<StrengthPower>(f.Player, 2, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            var fire = InHand(f, "FiendFire");
            c.Equal("Exhaust all cards in your hand. Deal 9 damage for each of them. / Exhaust.", Text(f, fire), "the text has Strength, not the target's Vulnerable");
            f.Play("FiendFire", 0);
            c.Equal(35, f.Enemy(0).Hp, "5 x 13");

            // Weak: 7 x 0.75 = 5.25 -> 5 per hit.
            var w = CheckKit.VsDummy();
            w.ApplyPower<WeakPower>(w.Player, 1, w.Enemy(0));
            InHand(w, "FiendFire");
            w.Play("FiendFire", 0);
            c.Equal(75, w.Enemy(0).Hp, "5 x 5");

            // 10 Block absorbs 7, then 3.
            var b = CheckKit.VsDummy();
            b.SetBlock(b.Enemy(0), 10);
            InHand(b, "FiendFire");
            b.Play("FiendFire", 0);
            c.Equal(75, b.Enemy(0).Hp, "35 - 10");
            c.Equal(0, b.Enemy(0).Block, "Block is gone");
        });

        c.Group("Fiend Fire: an empty hand, a target that dies early", () =>
        {
            var f = CheckKit.VsDummy();
            var tally = f.ApplyPower<Tally>(f.Player, 1, f.Player)!;
            f.Clear(PileType.Hand);
            var fire = InHand(f, "FiendFire");
            f.Play("FiendFire", 0);
            c.Equal(100, f.Enemy(0).Hp, "no card, no hit");
            c.Equal(PileType.Exhaust, fire.Pile, "it is exhausted all the same");
            c.Equal(1, f.ExhaustPile.Count, "alone in the exhaust pile");
            c.Equal(1, tally.Attacks, "the attack command still ran, with no hit");

            // Cards that enter the hand while it is being exhausted are neither exhausted nor counted:
            // each of the five exhausts draws a card; five hits; the five new cards stay.
            var d = CheckKit.VsDummy();
            d.ApplyPower<DrawOnExhaust>(d.Player, 1, d.Player);
            InHand(d, "FiendFire");
            d.Play("FiendFire", 0);
            c.Equal(65, d.Enemy(0).Hp, "5 x 7, not more");
            c.Sequence(["Strike", "Strike", "Strike", "Defend", "Defend"], d.HandTitles(), "the cards drawn meanwhile stay in hand");
            c.Equal(6, d.ExhaustPile.Count, "the five of the first hand and Fiend Fire");

            // The target dies on the second hit: the other three are lost, the hand is exhausted anyway.
            var g = CheckKit.VsDummies(new CheckDummy { Hp = 10 }, new CheckDummy());
            InHand(g, "FiendFire");
            g.Play("FiendFire", 0);
            c.That(g.Enemy(0).IsDead, "the target died");
            c.Equal(100, g.Enemy(1).Hp, "no hit is redirected");
            c.Equal(6, g.ExhaustPile.Count, "all five cards and Fiend Fire");
        });
    }

    // ---------- Fight Me! ----------

    private static void FightMe(Check c)
    {
        c.Group("Fight Me!: 5 x 2 (6 x 2), then 3 Strength (4), then 1 Strength for the enemy", () =>
        {
            var f = CheckKit.VsDummy(Hitter(7));
            var card = InHand(f, "FightMe");
            c.Equal("Deal 5 damage 2 times. Gain 3 Strength. The enemy gains 1 Strength.", Text(f, card), "text");
            f.Play("FightMe", 0);
            c.Equal(90, f.Enemy(0).Hp, "2 x 5: the new Strength came after the hits");
            c.Equal(3, f.Player.PowerAmount<StrengthPower>(), "3 Strength");
            c.Equal(1, f.Enemy(0).PowerAmount<StrengthPower>(), "the enemy has 1 Strength");
            c.Sequence(["attack 8"], f.IntentTexts(f.Enemy(0)), "which shows in its intent");
            c.Equal(1, f.Energy, "costs 2");
            c.Equal(PileType.Discard, card.Pile, "to the discard pile");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "FightMe+");
            c.Equal("Deal 6 damage 2 times. Gain 4 Strength. The enemy gains 1 Strength.", Text(g, plus), "upgraded text: hits and the enemy's Strength stay");
            g.Play("FightMe", 0);
            c.Equal(88, g.Enemy(0).Hp, "upgraded: 2 x 6");
            c.Equal(4, g.Player.PowerAmount<StrengthPower>(), "upgraded: 4 Strength");
            c.Equal(1, g.Enemy(0).PowerAmount<StrengthPower>(), "upgraded: still 1 for the enemy");

            // Strength already there is added to each hit.
            var s = CheckKit.VsDummy();
            s.ApplyPower<StrengthPower>(s.Player, 2, s.Player);
            InHand(s, "FightMe");
            s.Play("FightMe", 0);
            c.Equal(86, s.Enemy(0).Hp, "2 x (5 + 2)");
            c.Equal(5, s.Player.PowerAmount<StrengthPower>(), "2 + 3");
        });

        c.Group("Fight Me!: Artifact does not stop a Buff; a dead target gets nothing", () =>
        {
            var f = CheckKit.VsDummy(new CheckDummy { OnAdded = (x, me) => x.ApplyPower<ArtifactPower>(me, 1, me) });
            InHand(f, "FightMe");
            f.Play("FightMe", 0);
            c.Equal(1, f.Enemy(0).PowerAmount<StrengthPower>(), "the Strength landed");
            c.Equal(1, f.Enemy(0).PowerAmount<ArtifactPower>(), "Artifact was not used");

            var g = CheckKit.VsDummies(new CheckDummy { Hp = 5 }, new CheckDummy());
            InHand(g, "FightMe");
            g.Play("FightMe", 0);
            c.That(g.Enemy(0).IsDead, "the first hit killed it");
            c.Equal(3, g.Player.PowerAmount<StrengthPower>(), "the player's Strength still arrives");
            c.Equal(0, g.Enemy(0).PowerAmount<StrengthPower>(), "nothing for the dead target");
            c.Equal(0, g.Enemy(1).PowerAmount<StrengthPower>(), "nothing for the other enemy");
            c.Equal(100, g.Enemy(1).Hp, "the second hit is lost");

            // The kill ends the fight: neither Strength is applied any more.
            var k = CheckKit.VsDummy(new CheckDummy { Hp = 5 });
            var tally = k.ApplyPower<Tally>(k.Player, 1, k.Player)!;
            tally.PowerChanges = 0;
            InHand(k, "FightMe");
            k.Play("FightMe", 0);
            c.That(k.IsOver && k.Won, "won");
            c.Equal(0, tally.PowerChanges, "no power changed after the killing hit");
        });
    }

    // ---------- Flame Barrier ----------

    private static void FlameBarrier(Check c)
    {
        c.Group("Flame Barrier: 12 Block (16) and 4 back per attack hit (6), for one enemy turn", () =>
        {
            // Two hits of 7 into 12 Block: 2 get through; each hit, blocked or not, is answered with 4.
            var f = CheckKit.VsDummy(Hitter(7, 2));
            var card = InHand(f, "FlameBarrier");
            c.Equal("Gain 12 Block. Until your next turn, whenever an enemy hits you with an attack, deal 4 damage back to it.", Text(f, card), "text");
            f.Play("FlameBarrier");
            c.Equal(12, f.Player.Block, "12 Block");
            c.Equal(4, f.Player.PowerAmount<FlameBarrierPower>(), "the power, 4");
            c.Equal(1, f.Energy, "costs 2");
            f.EndTurn();
            c.Equal(78, f.Player.Hp, "14 - 12");
            c.Equal(92, f.Enemy(0).Hp, "4 back per hit, the fully blocked one included");
            c.That(!f.Player.HasPower<FlameBarrierPower>(), "gone after the enemy turn");

            var g = CheckKit.VsDummy(Hitter(7, 2));
            var plus = InHand(g, "FlameBarrier+");
            c.Equal("Gain 16 Block. Until your next turn, whenever an enemy hits you with an attack, deal 6 damage back to it.", Text(g, plus), "upgraded text");
            g.Play("FlameBarrier");
            c.Equal(16, g.Player.Block, "upgraded: 16 Block");
            g.EndTurn();
            c.Equal(80, g.Player.Hp, "both hits blocked");
            c.Equal(88, g.Enemy(0).Hp, "upgraded: 6 back per hit");

            // Two plays add up; Dexterity shows in the text.
            var h = CheckKit.VsDummy(Hitter(3));
            h.SetEnergy(4);
            h.ApplyPower<DexterityPower>(h.Player, 2, h.Player);
            var first = InHand(h, "FlameBarrier");
            InHand(h, "FlameBarrier");
            c.Equal("Gain 14 Block. Until your next turn, whenever an enemy hits you with an attack, deal 4 damage back to it.", Text(h, first), "text with Dexterity 2");
            h.Play("FlameBarrier");
            h.Play("FlameBarrier");
            c.Equal(28, h.Player.Block, "14 + 14");
            c.Equal(8, h.Player.PowerAmount<FlameBarrierPower>(), "4 + 4");
            h.EndTurn();
            c.Equal(92, h.Enemy(0).Hp, "8 back for the one hit");
        });

        c.Group("Flame Barrier: the answer is unpowered and blockable, and only for powered attacks with a dealer", () =>
        {
            // The first dummy gives the second 3 Block during the enemy turn; the second then hits for 5.
            // 4 back, never raised by the player's Strength or the attacker's Vulnerable: 3 blocked, 1 through.
            var f = CheckKit.VsDummies(
                new CheckDummy { Steps = [new DummyStep("AID", Then: (x, me) => x.GainBlock(x.Enemies[1], 3, Props.NonCardUnpowered))] },
                Hitter(5));
            f.ApplyPower<StrengthPower>(f.Player, 3, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(1), 2, f.Player);
            InHand(f, "FlameBarrier");
            f.Play("FlameBarrier");
            f.EndTurn();
            c.Equal(99, f.Enemy(1).Hp, "4 - 3 Block");
            c.Equal(100, f.Enemy(0).Hp, "only the attacker is answered");
            c.Equal(80, f.Player.Hp, "the 5 was blocked");

            var g = CheckKit.VsDummy();
            InHand(g, "FlameBarrier");
            g.Play("FlameBarrier");
            g.Damage(g.Player, 5, Props.NonCardUnpowered, g.Enemy(0));
            c.Equal(100, g.Enemy(0).Hp, "unpowered damage is not answered");
            g.Damage(g.Player, 5, Props.MonsterMove, null);
            c.Equal(100, g.Enemy(0).Hp, "damage without a dealer is not answered");
            g.Damage(g.Player, 5, Props.MonsterMove, g.Enemy(0));
            c.Equal(96, g.Enemy(0).Hp, "a powered attack hit is");
            c.Equal(77, g.Player.Hp, "12 Block took 12 of the 15");
        });

        c.Group("Flame Barrier: an attacker killed by the answer loses its remaining hits", () =>
        {
            var f = CheckKit.VsDummies(Hitter(9, 3, hp: 4), new CheckDummy());
            InHand(f, "FlameBarrier");
            f.Play("FlameBarrier");
            f.EndTurn();
            c.That(f.Enemy(0).IsDead, "4 damage killed the 4 HP attacker");
            c.Equal(80, f.Player.Hp, "the second and third hit never came (they would have cost 15 HP)");
            c.Equal(2, f.Turn, "the fight goes on");
        });
    }

    // ---------- Forgotten Ritual ----------

    private static void ForgottenRitual(Check c)
    {
        c.Group("Forgotten Ritual: 3 energy (4) only after an exhaust this turn", () =>
        {
            var f = CheckKit.VsDummy();
            var card = InHand(f, "ForgottenRitual");
            c.Equal("If a card was Exhausted this turn, gain 3 energy. / Exhaust.", Text(f, card), "text");
            f.Play("ForgottenRitual");
            c.Equal(2, f.Energy, "nothing exhausted yet: it just costs 1");
            c.Equal(PileType.Exhaust, card.Pile, "exhausted (after its effect, so never for itself)");

            var g = CheckKit.VsDummy();
            InHand(g, "Slimed");
            InHand(g, "ForgottenRitual");
            g.Play("Slimed");
            g.Play("ForgottenRitual");
            c.Equal(4, g.Energy, "3 - 1 - 1 + 3");

            var h = CheckKit.VsDummy();
            InHand(h, "Slimed");
            var plus = InHand(h, "ForgottenRitual+");
            c.Equal("If a card was Exhausted this turn, gain 4 energy. / Exhaust.", Text(h, plus), "upgraded text");
            h.Play("Slimed");
            h.Play("ForgottenRitual");
            c.Equal(5, h.Energy, "upgraded: 3 - 1 - 1 + 4");

            // The first Ritual's own exhaust enables the second.
            var two = CheckKit.VsDummy();
            InHand(two, "ForgottenRitual");
            InHand(two, "ForgottenRitual");
            two.Play("ForgottenRitual");
            c.Equal(2, two.Energy, "the first gains nothing");
            two.Play("ForgottenRitual");
            c.Equal(4, two.Energy, "the second: 2 - 1 + 3");
        });

        c.Group("Forgotten Ritual: what does not count, and No Energy Gain", () =>
        {
            var f = CheckKit.VsDummy();
            InHand(f, "Slimed");
            f.Play("Slimed");
            f.EndTurn();
            InHand(f, "ForgottenRitual");
            f.Play("ForgottenRitual");
            c.Equal(2, f.Energy, "last turn's exhaust does not count");

            var g = CheckKit.VsDummy();
            InHand(g, "Dazed");
            g.EndTurn();
            InHand(g, "ForgottenRitual");
            g.Play("ForgottenRitual");
            c.Equal(2, g.Energy, "nor does an Ethereal exhaust at the end of last turn");

            var h = CheckKit.VsDummy();
            InHand(h, "Inflame");
            InHand(h, "ForgottenRitual");
            h.Play("Inflame");
            h.Play("ForgottenRitual");
            c.Equal(1, h.Energy, "a played Power is not an exhaust");

            // Slimed (draws a Strike), Expect a Fight with 4 Attacks in hand, then the Ritual: its 3 become 0.
            var n = CheckKit.VsDummy();
            n.SetEnergy(10);
            InHand(n, "Slimed");
            InHand(n, "ExpectAFight");
            InHand(n, "ForgottenRitual");
            n.Play("Slimed");
            n.Play("ExpectAFight");
            c.Equal(11, n.Energy, "10 - 1 - 2 + 4");
            n.Play("ForgottenRitual");
            c.Equal(10, n.Energy, "No Energy Gain: only the cost");
        });
    }

    // ---------- Havoc ----------

    private static void Havoc(Check c)
    {
        c.Group("Havoc: plays the top card of the draw pile for free and exhausts it", () =>
        {
            var f = CheckKit.VsDummy();
            var havoc = InHand(f, "Havoc");
            var top = f.DrawPile[0];
            c.Equal("Play the top card of your draw pile for free and Exhaust it.", Text(f, havoc), "text");
            f.Play("Havoc");
            c.Equal(94, f.Enemy(0).Hp, "the Strike on top hit for 6");
            c.Equal(PileType.Exhaust, top.Pile, "and was exhausted");
            c.Equal(PileType.Discard, havoc.Pile, "Havoc goes to the discard pile");
            c.Equal(2, f.Energy, "only Havoc's 1 was paid");
            c.Equal(1, f.Rng.CombatTargets.Counter, "the target is rolled, also with one enemy");
            c.Equal(4, f.DrawPile.Count, "one card left the draw pile");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "Havoc+");
            c.Equal(0, g.CostOf(plus), "upgraded: costs 0");
            g.Play("Havoc");
            c.Equal(3, g.Energy, "upgraded: free");
            c.Equal(94, g.Enemy(0).Hp, "upgraded: the same effect");
        });

        c.Group("Havoc: what the top card is", () =>
        {
            // A Skill.
            var f = CheckKit.VsDummy();
            InHand(f, "Havoc");
            var defend = OnDraw(f, "DefendIronclad");
            f.Play("Havoc");
            c.Equal(5, f.Player.Block, "Defend was played");
            c.Equal(PileType.Exhaust, defend.Pile, "and exhausted");

            // A card that costs more than the energy left: nothing is paid or checked.
            var g = CheckKit.VsDummy();
            g.SetEnergy(1);
            InHand(g, "Havoc");
            OnDraw(g, "Bash+");
            g.Play("Havoc");
            c.Equal(90, g.Enemy(0).Hp, "Bash+ hit for 10");
            c.Equal(3, g.Enemy(0).PowerAmount<VulnerablePower>(), "and applied 3 Vulnerable");
            c.Equal(0, g.Energy, "only Havoc's cost");

            // An Unplayable card is exhausted without being played.
            var w = CheckKit.VsDummy();
            InHand(w, "Havoc");
            var wound = OnDraw(w, "Wound");
            w.Play("Havoc");
            c.Equal(PileType.Exhaust, wound.Pile, "the Wound was exhausted");
            c.Equal(0, w.Rng.CombatTargets.Counter, "nothing was played");

            // A Power card is played and leaves the fight; it is not exhausted.
            var p = CheckKit.VsDummy();
            InHand(p, "Havoc");
            var inflame = OnDraw(p, "Inflame");
            p.Play("Havoc");
            c.Equal(2, p.Player.PowerAmount<StrengthPower>(), "Inflame was played");
            c.Equal(PileType.None, inflame.Pile, "it left the fight");
            c.Equal(0, p.ExhaustPile.Count, "nothing in the exhaust pile");

            // An enemy-targeting card with no enemy that can be hit: exhausted unplayed, no target roll.
            var u = CheckKit.VsDummy();
            u.ApplyPower<Untouchable>(u.Enemy(0), 1, u.Enemy(0));
            InHand(u, "Havoc");
            var strike = u.DrawPile[0];
            u.Play("Havoc");
            c.Equal(PileType.Exhaust, strike.Pile, "exhausted without being played");
            c.Equal(100, u.Enemy(0).Hp, "no damage");
            c.Equal(0, u.Rng.CombatTargets.Counter, "no draw from an empty list");

            // A hook that forbids the play: the same.
            var l = CheckKit.VsDummy();
            l.ApplyPower<Lockdown>(l.Player, 1, l.Enemy(0));
            InHand(l, "Havoc");
            var blocked = l.DrawPile[0];
            l.Play("Havoc");
            c.Equal(PileType.Exhaust, blocked.Pile, "a forbidden card is exhausted without being played");
            c.Equal(100, l.Enemy(0).Hp, "no damage");
        });

        c.Group("Havoc: an empty draw pile", () =>
        {
            // The discard pile is shuffled in first (Havoc itself is in the Play pile).
            var f = CheckKit.VsDummy();
            f.Clear(PileType.Draw);
            f.Put("StrikeIronclad", PileType.Discard);
            f.Put("StrikeIronclad", PileType.Discard);
            var havoc = InHand(f, "Havoc");
            f.Play("Havoc");
            c.Equal(1, f.Rng.Shuffle.Counter, "two cards were shuffled: one draw");
            c.Equal(94, f.Enemy(0).Hp, "a Strike was played");
            c.Equal(1, f.DrawPile.Count, "the other Strike is the draw pile");
            c.Sequence([havoc], f.DiscardPile, "Havoc was not shuffled in; it is the discard pile now");

            // Nothing anywhere: nothing happens.
            var g = CheckKit.VsDummy();
            g.Clear(PileType.Draw);
            InHand(g, "Havoc");
            g.Play("Havoc");
            c.Equal(100, g.Enemy(0).Hp, "nothing to play");
            c.Equal(0, g.ExhaustPile.Count, "nothing exhausted");
            c.Equal(2, g.Energy, "the cost is paid all the same");
        });

        c.Group("Havoc: a random target among two; a card that asks for a choice", () =>
        {
            var f = CheckKit.VsDummies(new CheckDummy(), new CheckDummy());
            InHand(f, "Havoc");
            f.Play("Havoc");
            c.Equal(194, f.Enemy(0).Hp + f.Enemy(1).Hp, "one of the two took the Strike");
            c.Equal(1, f.Rng.CombatTargets.Counter, "one draw");

            // Headbutt from the top: 9 damage, then the player picks from the discard pile.
            var g = CheckKit.VsDummy();
            var havoc = InHand(g, "Havoc");
            var headbutt = OnDraw(g, "Headbutt");
            g.Put("DefendIronclad", PileType.Discard);
            var bash = g.Put("Bash+", PileType.Discard);
            g.Play("Havoc");
            var s = g.Snapshot();
            c.Equal("card_select", s.Screen, "the choice is shown");
            c.Equal("combat_pile_select", s.State["kind"]!.GetValue<string>(), "as a pile selection");
            c.Sequence(["card:0", "card:1"], s.Actions.Select(a => a.Id), "both discard-pile cards are offered (Havoc and Headbutt are in the Play pile)");
            g.Do("card:1");
            c.That(!g.HasPendingChoice, "the choice is answered");
            c.Equal(91, g.Enemy(0).Hp, "Headbutt hit once");
            c.Equal(bash.Id, g.DrawPile[0].Id, "Bash+ is on top of the draw pile");
            c.Equal(PileType.Exhaust, PileOf(g, headbutt), "Headbutt was exhausted");
            c.Equal(PileType.Discard, PileOf(g, havoc), "Havoc is in the discard pile");
            c.Equal(2, g.Energy, "one energy in all");
        });
    }

    // ---------- Headbutt ----------

    private static void Headbutt(Check c)
    {
        c.Group("Headbutt: 9 damage (12); nothing to pick from an empty discard pile", () =>
        {
            var f = CheckKit.VsDummy();
            var card = InHand(f, "Headbutt");
            var top = f.DrawPile[0];
            c.Equal("Deal 9 damage. Put a card from your discard pile on top of your draw pile.", Text(f, card), "text");
            f.Play("Headbutt", 0);
            c.Equal(91, f.Enemy(0).Hp, "9 damage");
            c.That(!f.HasPendingChoice, "no question");
            c.That(ReferenceEquals(f.DrawPile[0], top), "the draw pile is untouched: it cannot pick itself");
            c.Sequence([card], f.DiscardPile, "Headbutt is in the discard pile");
            c.Equal(2, f.Energy, "costs 1");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "Headbutt+");
            c.Equal("Deal 12 damage. Put a card from your discard pile on top of your draw pile.", Text(g, plus), "upgraded text");
            g.Play("Headbutt", 0);
            c.Equal(88, g.Enemy(0).Hp, "upgraded: 12 damage");

            // (9 + 2) x 1.5 = 16.5 -> 16.
            var h = CheckKit.VsDummy();
            h.ApplyPower<StrengthPower>(h.Player, 2, h.Player);
            h.ApplyPower<VulnerablePower>(h.Enemy(0), 1, h.Player);
            InHand(h, "Headbutt");
            h.Play("Headbutt", 0);
            c.Equal(84, h.Enemy(0).Hp, "Strength and Vulnerable");
        });

        c.Group("Headbutt: one card is taken without asking; two are a choice", () =>
        {
            var f = CheckKit.VsDummy();
            InHand(f, "Headbutt");
            var strike = f.Hand[0];
            f.Play("Strike", 0);
            f.Play("Headbutt", 0);
            c.That(!f.HasPendingChoice, "one candidate: no question");
            c.That(ReferenceEquals(f.DrawPile[0], strike), "the Strike is on top of the draw pile");
            c.Equal(6, f.DrawPile.Count, "five and the Strike");
            c.Equal(1, f.DiscardPile.Count, "only Headbutt is left in the discard pile");

            var g = CheckKit.VsDummy();
            InHand(g, "Headbutt");
            var defend = g.Hand[2];
            g.Play("Strike", 0);
            g.Play("Defend");
            g.Play("Headbutt", 0);
            var s = g.Snapshot();
            c.Equal("card_select", s.Screen, "the screen of a pile choice");
            c.Equal("combat_pile_select", s.State["kind"]!.GetValue<string>(), "kind");
            c.Equal("Discard", s.State["pile"]!.GetValue<string>(), "the pile");
            c.Equal(1, s.State["min"]!.GetValue<int>(), "min 1");
            c.Equal(1, s.State["max"]!.GetValue<int>(), "max 1");
            c.Sequence(["card:0", "card:1"], s.Actions.Select(a => a.Id), "Strike and Defend; no confirm, no skip");
            c.Sequence(["Select Strike", "Select Defend"], s.Actions.Select(a => a.Label), "labels");
            c.Equal(85, g.Enemy(0).Hp, "the damage came before the question: 6 + 9");
            c.That(!g.Execute("end_turn").Ok, "nothing else is accepted meanwhile");
            g.Do("card:1");
            c.Equal(defend.Id, g.DrawPile[0].Id, "the chosen Defend is on top of the draw pile");
            c.Sequence(["Strike", "Headbutt"], g.DiscardPile.Select(x => x.Title), "the Strike stayed; Headbutt joined it");
            c.Equal(0, g.Energy, "three cards of 1");
            g.EndTurn();
            c.Sequence(["Defend", "Strike", "Strike", "Strike", "Defend"], g.HandTitles(), "it is the first card drawn next turn");
            c.Equal(defend.Id, g.Hand[0].Id, "the very same card");
        });

        c.Group("Headbutt: no choice when the hit ends the fight", () =>
        {
            var f = CheckKit.VsDummy(new CheckDummy { Hp = 9 });
            f.Put("DefendIronclad", PileType.Discard);
            f.Put("Bash", PileType.Discard);
            InHand(f, "Headbutt");
            f.Play("Headbutt", 0);
            c.That(f.IsOver && f.Won, "won");
            c.That(!f.HasPendingChoice, "nothing was asked");
        });
    }

    // ---------- Hellraiser ----------

    private static void Hellraiser(Check c)
    {
        c.Group("Hellraiser: Strikes of the hand draw are played as they are drawn", () =>
        {
            var f = CheckKit.VsDummy();
            var card = InHand(f, "Hellraiser");
            c.Equal("Whenever you draw a card with Strike in its name, it is played for free at a random enemy.", Text(f, card), "text");
            f.Play("Hellraiser");
            c.Equal(1, f.Energy, "costs 2");
            c.Equal(1, f.Player.PowerAmount<HellraiserPower>(), "the power");
            c.Equal(PileType.None, card.Pile, "a played Power leaves the fight");
            f.EndTurn();
            // Turn 2 draws Strike, Strike, Strike, Defend, Defend: three free Strikes, not replaced.
            c.Equal(82, f.Enemy(0).Hp, "3 x 6");
            c.Sequence(["Defend", "Defend"], f.HandTitles(), "the Strikes are not replaced");
            c.Equal(3, f.Energy, "nothing was paid");
            c.Equal(8, f.DiscardPile.Count, "last turn's five cards and the three Strikes");
            c.Equal(3, f.Rng.CombatTargets.Counter, "one target draw per Strike");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "Hellraiser+");
            c.Equal(1, g.CostOf(plus), "upgraded: costs 1");
            g.Play("Hellraiser");
            c.Equal(2, g.Energy, "upgraded: 1 energy");
            c.That(g.Player.HasPower<HellraiserPower>(), "upgraded: the same power");
        });

        c.Group("Hellraiser: later draws, the Strike tag, a second copy", () =>
        {
            // Offering draws the three Strikes on top: each is played before the next is drawn.
            var f = CheckKit.VsDummy();
            f.SetEnergy(4);
            InHand(f, "Hellraiser");
            InHand(f, "Hellraiser");
            InHand(f, "Offering");
            f.Play("Hellraiser");
            f.Play("Hellraiser");
            c.Equal(1, f.Player.Powers.Count(p => p is HellraiserPower), "a second play adds no second instance");
            f.Play("Offering");
            c.Equal(82, f.Enemy(0).Hp, "three Strikes, each played once");
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash"], f.HandTitles(), "none of them stays in hand");
            c.Equal(74, f.Player.Hp, "Offering's 6");

            // Perfected Strike carries the tag: 6 + 2 x 6 Strike cards = 18.
            var g = CheckKit.VsDummy();
            g.ApplyPower<HellraiserPower>(g.Player, 1, g.Player);
            var perfected = OnDraw(g, "PerfectedStrike");
            g.Draw(1);
            c.Equal(82, g.Enemy(0).Hp, "Perfected Strike was played");
            c.Equal(PileType.Discard, perfected.Pile, "and discarded");

            // An Attack without the tag is just drawn.
            var h = CheckKit.VsDummy();
            h.ApplyPower<HellraiserPower>(h.Player, 1, h.Player);
            OnDraw(h, "IronWave");
            h.Draw(1);
            c.Equal(100, h.Enemy(0).Hp, "Iron Wave was not played");
            c.Equal(6, h.Hand.Count, "it is in the hand");

            // Two enemies: one of them takes it.
            var two = CheckKit.VsDummies(new CheckDummy(), new CheckDummy());
            two.ApplyPower<HellraiserPower>(two.Player, 1, two.Player);
            two.Draw(1);
            c.Equal(194, two.Enemy(0).Hp + two.Enemy(1).Hp, "one Strike on one of them");
            c.Equal(1, two.Rng.CombatTargets.Counter, "one draw");
        });

        c.Group("Hellraiser: with nobody to hit, nine attempts, then Strikes stay in hand", () =>
        {
            var f = CheckKit.VsDummy();
            f.ApplyPower<Untouchable>(f.Enemy(0), 1, f.Enemy(0));
            f.ApplyPower<HellraiserPower>(f.Player, 1, f.Player);
            f.Clear(PileType.Hand);
            f.Clear(PileType.Draw);
            for (var i = 0; i < 12; i++) f.Put("StrikeIronclad", PileType.Draw);
            f.Draw(12);
            c.Equal(9, f.DiscardPile.Count, "nine found no target and went to the discard pile unplayed");
            c.Equal(3, f.Hand.Count, "from the tenth on they stay in hand");
            c.Equal(100, f.Enemy(0).Hp, "nothing was hit");
            c.Equal(0, f.Rng.CombatTargets.Counter, "no target was rolled");

            // The count starts again at the end of the turn: the five Strikes of the next hand draw are tried again.
            f.EndTurn();
            c.Equal(0, f.Hand.Count, "all five went to the discard pile unplayed");

            // An enemy that can be hit again resets it at once.
            var g = CheckKit.VsDummy();
            g.ApplyPower<Untouchable>(g.Enemy(0), 1, g.Enemy(0));
            g.ApplyPower<HellraiserPower>(g.Player, 1, g.Player);
            g.Clear(PileType.Hand);
            g.Clear(PileType.Draw);
            for (var i = 0; i < 12; i++) g.Put("StrikeIronclad", PileType.Draw);
            g.Draw(10);
            c.Equal(1, g.Hand.Count, "the tenth stayed");
            g.RemovePower<Untouchable>(g.Enemy(0));
            g.Draw(2);
            c.Equal(88, g.Enemy(0).Hp, "both were played");
            c.Equal(1, g.Hand.Count, "and did not stay");
        });
    }

    // ---------- Hemokinesis ----------

    private static void Hemokinesis(Check c)
    {
        c.Group("Hemokinesis: lose 2 HP, then 15 damage (20)", () =>
        {
            var f = CheckKit.VsDummy();
            var card = InHand(f, "Hemokinesis");
            c.Equal("Lose 2 HP. Deal 15 damage.", Text(f, card), "text");
            f.Play("Hemokinesis", 0);
            c.Equal(78, f.Player.Hp, "2 HP lost");
            c.Equal(85, f.Enemy(0).Hp, "15 damage");
            c.Equal(2, f.Energy, "costs 1");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "Hemokinesis+");
            c.Equal("Lose 2 HP. Deal 20 damage.", Text(g, plus), "upgraded text: the HP loss stays 2");
            g.Play("Hemokinesis", 0);
            c.Equal(78, g.Player.Hp, "upgraded: 2 HP lost");
            c.Equal(80, g.Enemy(0).Hp, "upgraded: 20 damage");
        });

        c.Group("Hemokinesis: the HP loss ignores Block and every modifier; the hit does not", () =>
        {
            var f = CheckKit.VsDummy();
            f.SetBlock(f.Player, 10);
            f.ApplyPower<StrengthPower>(f.Player, 3, f.Player);
            f.ApplyPower<VulnerablePower>(f.Player, 1, f.Enemy(0));
            var card = InHand(f, "Hemokinesis");
            c.Equal("Lose 2 HP. Deal 18 damage.", Text(f, card), "the HP loss is shown plain, the damage with Strength");
            f.Play("Hemokinesis", 0);
            c.Equal(78, f.Player.Hp, "exactly 2, through 10 Block, with Vulnerable and Strength on the player");
            c.Equal(10, f.Player.Block, "Block untouched");
            c.Equal(82, f.Enemy(0).Hp, "15 + 3");

            // The loss comes first: at 2 HP the player dies and the hit never happens.
            var g = CheckKit.VsDummy();
            g.SetHp(g.Player, 2);
            InHand(g, "Hemokinesis");
            g.Play("Hemokinesis", 0);
            c.That(g.IsOver && !g.Won, "the fight is lost");
            c.Equal(100, g.Enemy(0).Hp, "no hit");
        });
    }

    // ---------- Howl from Beyond ----------

    private static void HowlFromBeyond(Check c)
    {
        c.Group("Howl from Beyond: 16 to every enemy (21), to the discard pile", () =>
        {
            var f = CheckKit.VsDummies(new CheckDummy(), new CheckDummy());
            var howl = InHand(f, "HowlFromBeyond");
            c.Equal("Deal 16 damage to ALL enemies. At the end of your turn, if this card is in your exhaust pile, it is played for free and goes to your discard pile.", Text(f, howl), "text");
            f.Play("HowlFromBeyond");
            c.Equal(84, f.Enemy(0).Hp, "16 to the first");
            c.Equal(84, f.Enemy(1).Hp, "16 to the second");
            c.Equal(0, f.Energy, "costs 3");
            c.Equal(PileType.Discard, howl.Pile, "no Exhaust keyword");

            var g = CheckKit.VsDummies(new CheckDummy(), new CheckDummy());
            var plus = InHand(g, "HowlFromBeyond+");
            c.That(Text(g, plus).StartsWith("Deal 21 damage to ALL enemies.", StringComparison.Ordinal), "upgraded text");
            g.Play("HowlFromBeyond");
            c.Equal(79, g.Enemy(0).Hp, "upgraded: 21");
            c.Equal(79, g.Enemy(1).Hp, "upgraded: 21 to both");

            // Worked out per enemy: 16 x 1.5 = 24 into Vulnerable; 16 - 5 Block = 11.
            var h = CheckKit.VsDummies(new CheckDummy(), new CheckDummy());
            h.ApplyPower<VulnerablePower>(h.Enemy(0), 1, h.Player);
            h.SetBlock(h.Enemy(1), 5);
            InHand(h, "HowlFromBeyond");
            h.Play("HowlFromBeyond");
            c.Equal(76, h.Enemy(0).Hp, "Vulnerable on the first only");
            c.Equal(89, h.Enemy(1).Hp, "Block on the second only");
        });

        c.Group("Howl from Beyond: from the exhaust pile it plays itself when the turn ends", () =>
        {
            var f = CheckKit.VsDummies(new CheckDummy(), new CheckDummy());
            var howl = f.Put("HowlFromBeyond", PileType.Exhaust);
            f.EndTurn();
            c.Equal(84, f.Enemy(0).Hp, "16 at the end of the turn");
            c.Equal(84, f.Enemy(1).Hp, "to both");
            c.Equal(PileType.Discard, howl.Pile, "it is in the discard pile now");
            c.Equal(0, f.ExhaustPile.Count, "and no longer exhausted");
            f.EndTurn();
            c.Equal(84, f.Enemy(0).Hp, "it does not play again from the discard pile");

            // Havoc: hits, is exhausted, hits again at end of turn.
            var g = CheckKit.VsDummy();
            InHand(g, "Havoc");
            var top = OnDraw(g, "HowlFromBeyond");
            g.Play("Havoc");
            c.Equal(84, g.Enemy(0).Hp, "Havoc's play");
            c.Equal(PileType.Exhaust, top.Pile, "exhausted by Havoc");
            g.EndTurn();
            c.Equal(68, g.Enemy(0).Hp, "and once more at end of turn");
            c.Equal(PileType.Discard, top.Pile, "then to the discard pile");

            // One-Two Punch still unused at end of turn doubles the free play.
            var h = CheckKit.VsDummy();
            InHand(h, "OneTwoPunch");
            h.Put("HowlFromBeyond", PileType.Exhaust);
            h.Play("OneTwoPunch");
            h.EndTurn();
            c.Equal(68, h.Enemy(0).Hp, "2 x 16");
            c.That(!h.Player.HasPower<OneTwoPunchPower>(), "the charge was spent");

            // The free play can win the fight.
            var k = CheckKit.VsDummy(new CheckDummy { Hp = 16 });
            k.Put("HowlFromBeyond", PileType.Exhaust);
            k.EndTurn();
            c.That(k.IsOver && k.Won, "won at end of turn");
        });
    }

    // ---------- Impervious ----------

    private static void Impervious(Check c)
    {
        c.Group("Impervious: 30 Block (40), exhausted", () =>
        {
            var f = CheckKit.VsDummy();
            var card = InHand(f, "Impervious");
            c.Equal("Gain 30 Block. / Exhaust.", Text(f, card), "text");
            f.Play("Impervious");
            c.Equal(30, f.Player.Block, "30 Block");
            c.Equal(1, f.Energy, "costs 2");
            c.Equal(PileType.Exhaust, card.Pile, "exhausted");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "Impervious+");
            c.Equal("Gain 40 Block. / Exhaust.", Text(g, plus), "upgraded text");
            g.Play("Impervious");
            c.Equal(40, g.Player.Block, "upgraded: 40 Block");

            // 30 x 0.75 = 22.5 -> 22 under Frail; (30 + 2) = 32 with Dexterity 2.
            var h = CheckKit.VsDummy();
            h.ApplyPower<FrailPower>(h.Player, 1, h.Enemy(0));
            var frail = InHand(h, "Impervious");
            c.Equal("Gain 22 Block. / Exhaust.", Text(h, frail), "text under Frail");
            h.Play("Impervious");
            c.Equal(22, h.Player.Block, "Frail");

            var d = CheckKit.VsDummy();
            d.ApplyPower<DexterityPower>(d.Player, 2, d.Player);
            InHand(d, "Impervious");
            d.Play("Impervious");
            c.Equal(32, d.Player.Block, "Dexterity");
        });
    }

    // ---------- Infernal Blade ----------

    /// <summary>The 33 Attacks the game's Ironclad pool offers to random generation, in pool order (cards-2.json).</summary>
    private static readonly string[] GeneratableAttacks =
    [
        "Anger", "AshenStrike", "Bludgeon", "BodySlam", "Breakthrough", "Bully", "Cinder", "Conflagration", "Dismantle", "FiendFire", "FightMe",
        "Headbutt", "Hemokinesis", "HowlFromBeyond", "IronWave", "Mangle", "MoltenFist", "PactsEnd", "PerfectedStrike", "Pillage", "PommelStrike",
        "Rampage", "SetupStrike", "Spite", "Stomp", "SwordBoomerang", "TearAsunder", "Thrash", "Thunderclap", "TwinStrike", "Unrelenting", "Uppercut", "Whirlwind",
    ];

    private static void InfernalBlade(Check c)
    {
        c.Group("Infernal Blade: the candidates are the pool's generatable Attacks, in pool order", () =>
        {
            var registered = GeneratableAttacks.Where(name => Content.Registry.Has(ContentKind.Card, name)).ToList();
            var candidates = Content.Registry.IroncladPool
                .Where(d => d.Type == CardType.Attack && d.Rarity is not (CardRarity.Basic or CardRarity.Ancient or CardRarity.Event) && d.CanBeGeneratedInCombat)
                .Select(d => d.ClassName).ToList();
            c.Sequence(registered, candidates, "exactly the spec's list, as far as the cards are registered");
            foreach (var mine in new[] { "FiendFire", "FightMe", "Headbutt", "Hemokinesis", "HowlFromBeyond", "IronWave", "Mangle", "MoltenFist", "PactsEnd", "PerfectedStrike", "Pillage" })
                c.That(candidates.Contains(mine), $"{mine} can be generated");
            c.That(!candidates.Contains("Feed"), "Feed is never generated");
            c.That(!candidates.Contains("StrikeIronclad") && !candidates.Contains("Bash"), "Basic cards are never generated");
        });

        c.Group("Infernal Blade: a random Attack, free this turn, into the hand", () =>
        {
            var f0 = CheckKit.VsDummy();
            var shown = InHand(f0, "InfernalBlade");
            c.Equal("Add a random Attack to your hand. It costs 0 this turn. / Exhaust.", Text(f0, shown), "text");

            for (var seed = 1; seed <= 12; seed++)
            {
                // The game's pick, written out: shuffle the candidates from the last index down on the
                // card-generation stream, then take the first.
                var pool = Content.Registry.IroncladPool
                    .Where(d => d.Type == CardType.Attack && d.Rarity is not (CardRarity.Basic or CardRarity.Ancient or CardRarity.Event) && d.CanBeGeneratedInCombat)
                    .ToList();
                var rng = RngSet.For(seed, "combat_card_generation");
                for (var i = pool.Count - 1; i > 0; i--)
                {
                    var j = rng.NextInt(i + 1);
                    (pool[i], pool[j]) = (pool[j], pool[i]);
                }
                var expected = pool[0];

                var upgraded = seed % 2 == 0;
                var f = CheckKit.VsDummy(seed: seed);
                var blade = InHand(f, upgraded ? "InfernalBlade+" : "InfernalBlade");
                f.Play("InfernalBlade");
                c.Equal(upgraded ? 3 : 2, f.Energy, $"seed {seed}: costs {(upgraded ? 0 : 1)}");
                c.Equal(pool.Count - 1, f.Rng.CombatCardGeneration.Counter, $"seed {seed}: one draw per candidate but the first");
                c.Equal(6, f.Hand.Count, $"seed {seed}: one card was added");
                var made = f.Hand[^1];
                c.Equal(expected.ClassName, made.Def.ClassName, $"seed {seed}: the card the game's shuffle leaves first");
                c.Equal(CardType.Attack, made.Type, $"seed {seed}: an Attack");
                c.That(!made.IsUpgraded, $"seed {seed}: not upgraded");
                c.Equal(0, f.CostOf(made), $"seed {seed}: free this turn");
                c.Equal(PileType.Exhaust, blade.Pile, $"seed {seed}: Infernal Blade is exhausted");
                f.EndTurn();
                c.Equal(made.Def.Cost, f.CostOf(made), $"seed {seed}: its own cost again after the turn");
                c.Equal(PileType.Discard, made.Pile, $"seed {seed}: it was discarded with the hand");
            }
        });

        c.Group("Infernal Blade: a full hand sends the new card to the discard pile", () =>
        {
            // Havoc plays it from the draw pile while ten cards are in hand.
            var f = CheckKit.VsDummy();
            InHand(f, "Havoc");
            for (var i = 0; i < 5; i++) InHand(f, "Wound");
            var blade = OnDraw(f, "InfernalBlade");
            f.Play("Havoc");
            c.Equal(10, f.Hand.Count, "the hand stayed at ten");
            c.Equal(2, f.DiscardPile.Count, "the new card and Havoc");
            c.Equal(CardType.Attack, f.DiscardPile[0].Type, "the new Attack went to the discard pile");
            c.Equal(0, f.CostOf(f.DiscardPile[0]), "still free this turn");
            c.Equal(PileType.Exhaust, blade.Pile, "Infernal Blade was exhausted");
        });
    }

    // ---------- Inferno ----------

    private static void Inferno(Check c)
    {
        c.Group("Inferno: lose 1 HP at the start of the turn, and 6 (9) to every enemy whenever HP is lost on the player's turn", () =>
        {
            var f = CheckKit.VsDummies(new CheckDummy(), new CheckDummy());
            var card = InHand(f, "Inferno");
            c.Equal("At the start of your turn, lose 1 HP. Whenever you lose HP on your turn, deal 6 damage to ALL enemies.", Text(f, card), "text");
            f.Play("Inferno");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(6, f.Player.PowerAmount<InfernoPower>(), "the power, 6");
            c.Equal(1, f.Player.GetPower<InfernoPower>()!.SelfDamage, "self-damage 1");
            c.Equal(80, f.Player.Hp, "nothing yet");
            f.EndTurn();
            c.Equal(79, f.Player.Hp, "1 HP at the start of turn 2");
            c.Equal(94, f.Enemy(0).Hp, "which deals 6 to the first enemy");
            c.Equal(94, f.Enemy(1).Hp, "and to the second");
            c.Equal(5, f.Hand.Count, "after the hand was drawn");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "Inferno+");
            c.Equal("At the start of your turn, lose 1 HP. Whenever you lose HP on your turn, deal 9 damage to ALL enemies.", Text(g, plus), "upgraded text");
            g.Play("Inferno");
            g.EndTurn();
            c.Equal(79, g.Player.Hp, "upgraded: still 1 HP");
            c.Equal(91, g.Enemy(0).Hp, "upgraded: 9 damage");

            // Two plays: 12, and 2 HP each turn.
            var h = CheckKit.VsDummy();
            InHand(h, "Inferno");
            InHand(h, "Inferno");
            h.Play("Inferno");
            h.Play("Inferno");
            c.Equal(12, h.Player.PowerAmount<InfernoPower>(), "6 + 6");
            c.Equal(2, h.Player.GetPower<InfernoPower>()!.SelfDamage, "self-damage 2");
            h.EndTurn();
            c.Equal(78, h.Player.Hp, "2 HP");
            c.Equal(88, h.Enemy(0).Hp, "12 damage, once");
        });

        c.Group("Inferno: only HP actually lost, only on the player's turn", () =>
        {
            // The enemy's hit of 5 does not trigger it; the turn start then does.
            var f = CheckKit.VsDummy(Hitter(5));
            InHand(f, "Inferno");
            f.Play("Inferno");
            f.EndTurn();
            c.Equal(74, f.Player.Hp, "5 from the enemy, 1 from Inferno");
            c.Equal(94, f.Enemy(0).Hp, "6 once: nothing for the hit taken on the enemy's turn");

            // A hit that Block absorbs whole is no HP loss.
            var g = CheckKit.VsDummy();
            g.ApplyPower<InfernoPower>(g.Player, 6, g.Player);
            g.SetBlock(g.Player, 5);
            g.Damage(g.Player, 5, Props.MonsterMove, g.Enemy(0));
            c.Equal(100, g.Enemy(0).Hp, "fully blocked: nothing");
            g.Damage(g.Player, 3, Props.MonsterMove, g.Enemy(0));
            c.Equal(94, g.Enemy(0).Hp, "3 HP lost on the player's turn: 6");
            c.Equal(77, g.Player.Hp, "the 3 HP");

            // Hemokinesis: its 2 HP trigger the 6 before its own 15.
            var h = CheckKit.VsDummy();
            InHand(h, "Inferno");
            InHand(h, "Hemokinesis");
            h.Play("Inferno");
            h.Play("Hemokinesis", 0);
            c.Equal(79, h.Enemy(0).Hp, "6 + 15");
            c.Equal(78, h.Player.Hp, "2 HP");

            // Offering's 6 HP.
            var o = CheckKit.VsDummy();
            InHand(o, "Inferno");
            InHand(o, "Offering");
            o.Play("Inferno");
            o.Play("Offering");
            c.Equal(94, o.Enemy(0).Hp, "6");

            // A Burn in hand at the end of the turn: still the player's side.
            var b = CheckKit.VsDummy();
            InHand(b, "Inferno");
            InHand(b, "Burn");
            b.Play("Inferno");
            b.EndTurn();
            c.Equal(77, b.Player.Hp, "2 from Burn, 1 at the start of turn 2");
            c.Equal(88, b.Enemy(0).Hp, "6 for each of the two losses");
        });

        c.Group("Inferno: the damage is unpowered and blockable; it can win the fight", () =>
        {
            // The dummy has 4 Block from its own turn when turn 2 starts; Strength does not add.
            var f = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("GUARD", Block: 4)] });
            f.ApplyPower<StrengthPower>(f.Player, 3, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 3, f.Player);
            InHand(f, "Inferno");
            f.Play("Inferno");
            f.EndTurn();
            c.Equal(98, f.Enemy(0).Hp, "6 - 4 Block, no Strength, no Vulnerable");
            c.Equal(0, f.Enemy(0).Block, "the Block is used up");

            var g = CheckKit.VsDummy(new CheckDummy { Hp = 6 });
            InHand(g, "Inferno");
            g.Play("Inferno");
            g.EndTurn();
            c.That(g.IsOver && g.Won, "the start-of-turn damage killed the last enemy");
        });
    }

    // ---------- Inflame ----------

    private static void Inflame(Check c)
    {
        c.Group("Inflame: 2 Strength (3)", () =>
        {
            var f = CheckKit.VsDummy();
            var card = InHand(f, "Inflame");
            c.Equal("Gain 2 Strength.", Text(f, card), "text");
            f.Play("Inflame");
            c.Equal(2, f.Player.PowerAmount<StrengthPower>(), "2 Strength");
            c.Equal(2, f.Energy, "costs 1");
            c.Equal(PileType.None, card.Pile, "a played Power leaves the fight");
            c.Equal(0, f.ExhaustPile.Count + f.DiscardPile.Count, "it is in no pile");
            f.Play("Strike", 0);
            c.Equal(92, f.Enemy(0).Hp, "Strike for 6 + 2");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "Inflame+");
            c.Equal("Gain 3 Strength.", Text(g, plus), "upgraded text");
            g.Play("Inflame");
            c.Equal(3, g.Player.PowerAmount<StrengthPower>(), "upgraded: 3 Strength");
            g.Play("Strike", 0);
            c.Equal(91, g.Enemy(0).Hp, "Strike for 6 + 3");
            g.EndTurn();
            c.Equal(3, g.Player.PowerAmount<StrengthPower>(), "it does not wear off");
        });
    }

    // ---------- Iron Wave ----------

    private static void IronWave(Check c)
    {
        c.Group("Iron Wave: 5 Block, then 5 damage (7 and 7)", () =>
        {
            var f = CheckKit.VsDummy();
            var card = InHand(f, "IronWave");
            c.Equal("Gain 5 Block. Deal 5 damage.", Text(f, card), "text");
            var before = f.History.EntryCount;
            f.Play("IronWave", 0);
            c.Equal(5, f.Player.Block, "5 Block");
            c.Equal(95, f.Enemy(0).Hp, "5 damage");
            c.Equal(2, f.Energy, "costs 1");
            var entries = f.History.Entries.Skip(before).Where(e => e.Kind is HistoryKind.BlockGained or HistoryKind.DamageReceived).Select(e => e.Kind).ToList();
            c.Sequence([HistoryKind.BlockGained, HistoryKind.DamageReceived], entries, "the Block comes before the hit");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "IronWave+");
            c.Equal("Gain 7 Block. Deal 7 damage.", Text(g, plus), "upgraded text");
            g.Play("IronWave", 0);
            c.Equal(7, g.Player.Block, "upgraded: 7 Block");
            c.Equal(93, g.Enemy(0).Hp, "upgraded: 7 damage");

            // Block: (5 + 2) x 0.75 = 5.25 -> 5. Damage: (5 + 2) x 1.5 = 10.5 -> 10.
            var h = CheckKit.VsDummy();
            h.ApplyPower<DexterityPower>(h.Player, 2, h.Player);
            h.ApplyPower<FrailPower>(h.Player, 1, h.Enemy(0));
            h.ApplyPower<StrengthPower>(h.Player, 2, h.Player);
            h.ApplyPower<VulnerablePower>(h.Enemy(0), 1, h.Player);
            var modified = InHand(h, "IronWave");
            c.Equal("Gain 5 Block. Deal 7 damage.", Text(h, modified), "text with the player's own modifiers");
            h.Play("IronWave", 0);
            c.Equal(5, h.Player.Block, "Dexterity and Frail");
            c.Equal(90, h.Enemy(0).Hp, "Strength and Vulnerable");

            // With Juggernaut the Block gain hits first: 6, then the 5.
            var j = CheckKit.VsDummy();
            j.ApplyPower<JuggernautPower>(j.Player, 6, j.Player);
            InHand(j, "IronWave");
            j.Play("IronWave", 0);
            c.Equal(89, j.Enemy(0).Hp, "6 + 5");
        });
    }

    // ---------- Juggernaut ----------

    private static void Juggernaut(Check c)
    {
        c.Group("Juggernaut: 6 damage (8) to a random enemy after every Block gain", () =>
        {
            var f = CheckKit.VsDummy();
            f.SetEnergy(4);
            var card = InHand(f, "Juggernaut");
            c.Equal("Whenever you gain Block, deal 6 damage to a random enemy.", Text(f, card), "text");
            f.Play("Juggernaut");
            c.Equal(2, f.Energy, "costs 2");
            c.Equal(6, f.Player.PowerAmount<JuggernautPower>(), "the power, 6");
            f.Play("Defend");
            c.Equal(94, f.Enemy(0).Hp, "6 for the Block gain");
            c.Equal(5, f.Player.Block, "the Block itself");
            c.Equal(1, f.Rng.CombatTargets.Counter, "the target is rolled, also with one enemy");
            f.Play("Strike", 0);
            c.Equal(88, f.Enemy(0).Hp, "an Attack without Block does not trigger it");
            c.Equal(1, f.Rng.CombatTargets.Counter, "no further roll");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "Juggernaut+");
            c.Equal("Whenever you gain Block, deal 8 damage to a random enemy.", Text(g, plus), "upgraded text");
            g.Play("Juggernaut");
            g.Play("Defend");
            c.Equal(92, g.Enemy(0).Hp, "upgraded: 8");

            // Two plays add up.
            var h = CheckKit.VsDummy();
            h.SetEnergy(5);
            InHand(h, "Juggernaut");
            InHand(h, "Juggernaut+");
            h.Play("Juggernaut");
            h.Play("Juggernaut");
            h.Play("Defend");
            c.Equal(86, h.Enemy(0).Hp, "6 + 8");
        });

        c.Group("Juggernaut: unpowered and blockable; any source of Block; a fraction still counts", () =>
        {
            // Strength and the target's Vulnerable change nothing; 4 Block absorbs 4 of the 6.
            var f = CheckKit.VsDummy();
            f.ApplyPower<JuggernautPower>(f.Player, 6, f.Player);
            f.ApplyPower<StrengthPower>(f.Player, 5, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 2, f.Player);
            f.SetBlock(f.Enemy(0), 4);
            f.Play("Defend");
            c.Equal(98, f.Enemy(0).Hp, "6 - 4");

            // Feel No Pain's Block triggers it.
            var g = CheckKit.VsDummy();
            g.ApplyPower<FeelNoPainPower>(g.Player, 3, g.Player);
            g.ApplyPower<JuggernautPower>(g.Player, 6, g.Player);
            InHand(g, "Slimed");
            g.Play("Slimed");
            c.Equal(3, g.Player.Block, "Feel No Pain's 3");
            c.Equal(94, g.Enemy(0).Hp, "answered with 6");

            // 1 Block under Frail is 0.75: no Block is added, but the gain is above 0.
            var h = CheckKit.VsDummy();
            h.ApplyPower<JuggernautPower>(h.Player, 6, h.Player);
            h.ApplyPower<FrailPower>(h.Player, 1, h.Enemy(0));
            h.Put(Chip, PileType.Hand);
            h.Play("Chip");
            c.Equal(0, h.Player.Block, "0.75 adds nothing");
            c.Equal(94, h.Enemy(0).Hp, "and still fires");

            // A gain brought down to 0 does not.
            var z = CheckKit.VsDummy();
            z.ApplyPower<JuggernautPower>(z.Player, 6, z.Player);
            z.ApplyPower<DexterityPower>(z.Player, -5, z.Enemy(0));
            z.Play("Defend");
            c.Equal(100, z.Enemy(0).Hp, "5 - 5 = 0: nothing");
            c.Equal(0, z.Rng.CombatTargets.Counter, "no roll");

            // Two enemies: one of them, one roll.
            var two = CheckKit.VsDummies(new CheckDummy(), new CheckDummy());
            two.ApplyPower<JuggernautPower>(two.Player, 6, two.Player);
            two.Play("Defend");
            c.Equal(194, two.Enemy(0).Hp + two.Enemy(1).Hp, "6 on one of the two");
            c.Equal(1, two.Rng.CombatTargets.Counter, "one roll");
        });
    }

    // ---------- Juggling ----------

    private static void Juggling(Check c)
    {
        c.Group("Juggling: the third Attack of the turn is copied into the hand", () =>
        {
            var f = CheckKit.VsDummy();
            f.SetEnergy(10);
            var card = InHand(f, "Juggling");
            var bash = f.Hand[4];
            c.Equal("Whenever you play your third Attack in a turn, add a copy of it to your hand.", Text(f, card), "text");
            f.Play("Juggling");
            c.Equal(9, f.Energy, "costs 1");
            c.Equal(1, f.Player.PowerAmount<JugglingPower>(), "the power, 1");
            f.Play("Strike", 0);
            f.Play("Strike", 0);
            c.Sequence(["Defend", "Defend", "Bash"], f.HandTitles(), "two Attacks: no copy yet");
            f.Play("Bash", 0);
            c.Sequence(["Defend", "Defend", "Bash"], f.HandTitles(), "the third Attack left a copy at the end of the hand");
            var copy = f.Hand[2];
            c.That(!ReferenceEquals(copy, bash), "a new card");
            c.Equal(PileType.Discard, bash.Pile, "the played Bash is in the discard pile");
            c.Equal(2, f.CostOf(copy), "the copy has Bash's cost");
            c.Equal(80, f.Enemy(0).Hp, "6 + 6 + 8");
            f.Play("Bash", 0);
            c.Sequence(["Defend", "Defend"], f.HandTitles(), "the fourth Attack is not copied");
            c.Equal(PileType.Discard, copy.Pile, "the copy is an ordinary card: to the discard pile");
        });

        c.Group("Juggling: upgraded it is Innate, nothing else", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Add("Juggling+").Enemy(new CheckDummy()).Start();
            c.Sequence(["Juggling+", "Strike", "Strike", "Defend", "Defend"], f.HandTitles(), "the last card of the deck is drawn first");
            c.Equal("Innate. / Whenever you play your third Attack in a turn, add a copy of it to your hand.", Text(f, f.Hand[0]), "upgraded text");
            c.Equal(1, f.CostOf(f.Hand[0]), "still costs 1");
            f.Play("Juggling");
            c.Equal(1, f.Player.PowerAmount<JugglingPower>(), "the same power");

            var g = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Add("Juggling").Enemy(new CheckDummy()).Start();
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash"], g.HandTitles(), "the plain card is not Innate");
        });

        c.Group("Juggling: the count starts from the Attacks already played, and again every turn", () =>
        {
            var f = CheckKit.VsDummy();
            f.SetEnergy(10);
            InHand(f, "Juggling");
            f.Play("Strike", 0);
            f.Play("Strike", 0);
            f.Play("Juggling");
            f.Play("Bash", 0);
            c.Sequence(["Defend", "Defend", "Bash"], f.HandTitles(), "two Attacks before the Power count");

            // Two Attacks on turn 1; on turn 2 the first Attack is number one again.
            var g = CheckKit.VsDummy();
            g.SetEnergy(10);
            InHand(g, "Juggling");
            g.Play("Juggling");
            g.Play("Strike", 0);
            g.Play("Strike", 0);
            g.EndTurn();
            g.Play("Strike", 0);
            c.Sequence(["Strike", "Strike", "Defend", "Defend"], g.HandTitles(), "turn 2, first Attack: no copy");
            g.Play("Strike", 0);
            g.Play("Strike", 0);
            c.Sequence(["Defend", "Defend", "Strike"], g.HandTitles(), "turn 2, third Attack: a copy");
        });

        c.Group("Juggling: what the copy is", () =>
        {
            // Upgraded stays upgraded.
            var f = CheckKit.VsDummy();
            f.SetEnergy(10);
            f.ApplyPower<JugglingPower>(f.Player, 1, f.Player);
            InHand(f, "StrikeIronclad+");
            f.Play("Strike", 0);
            f.Play("Strike", 0);
            f.Play("Strike+", 0);
            c.Sequence(["Defend", "Defend", "Bash", "Strike+"], f.HandTitles(), "the copy is upgraded");
            c.Equal(79, f.Enemy(0).Hp, "6 + 6 + 9");

            // A card that is free this turn gives a copy that is free this turn; the played one has its cost back.
            var g = CheckKit.VsDummy();
            g.ApplyPower<JugglingPower>(g.Player, 1, g.Player);
            var bash = g.Hand[4];
            bash.SetCostThisTurnOrUntilPlayed(0);
            g.Play("Strike", 0);
            g.Play("Strike", 0);
            g.Play("Bash", 0);
            var copy = g.Hand[2];
            c.Equal(1, g.Energy, "3 - 1 - 1 - 0: the free Bash cost nothing");
            c.Equal(0, g.CostOf(copy), "the copy is free this turn too");
            c.Equal(2, g.CostOf(bash), "the played card's free cost ended when it was played");
            g.EndTurn();
            c.Equal(2, g.CostOf(copy), "the copy's free cost ends with the turn");

            // Two stacks: two copies.
            var h = CheckKit.VsDummy();
            h.SetEnergy(10);
            InHand(h, "Juggling");
            InHand(h, "Juggling");
            h.Play("Juggling");
            h.Play("Juggling");
            c.Equal(2, h.Player.PowerAmount<JugglingPower>(), "1 + 1");
            h.Play("Strike", 0);
            h.Play("Strike", 0);
            h.Play("Bash", 0);
            c.Sequence(["Defend", "Defend", "Bash", "Bash"], h.HandTitles(), "two copies");

            // A full hand: the copy goes to the discard pile.
            var full = CheckKit.VsDummy();
            full.SetEnergy(10);
            full.ApplyPower<JugglingPower>(full.Player, 1, full.Player);
            full.Play("Strike", 0);
            full.Play("Strike", 0);
            for (var i = 0; i < 8; i++) InHand(full, "Wound");
            full.Play("Bash", 0);
            c.Equal(10, full.Hand.Count, "ten cards stay in hand");
            c.Sequence(["Strike", "Strike", "Bash", "Bash"], full.DiscardPile.Select(x => x.Title), "the copy, then the played Bash");
        });

        c.Group("Juggling: every run of a doubled Attack counts", () =>
        {
            // One-Two Punch: the first Strike runs twice (1, 2), the second Strike is the third.
            var f = CheckKit.VsDummy();
            f.SetEnergy(10);
            InHand(f, "Juggling");
            InHand(f, "OneTwoPunch");
            f.Play("Juggling");
            f.Play("OneTwoPunch");
            f.Play("Strike", 0);
            c.Sequence(["Strike", "Defend", "Defend", "Bash"], f.HandTitles(), "two runs: no copy yet");
            f.Play("Strike", 0);
            c.Sequence(["Defend", "Defend", "Bash", "Strike"], f.HandTitles(), "the next Attack is the third");
            c.Equal(82, f.Enemy(0).Hp, "three hits of 6");

            // Hellraiser's free Strikes count: the third of the hand draw is copied.
            var g = CheckKit.VsDummy();
            g.ApplyPower<HellraiserPower>(g.Player, 1, g.Player);
            g.ApplyPower<JugglingPower>(g.Player, 1, g.Player);
            g.EndTurn();
            c.Sequence(["Strike", "Defend", "Defend"], g.HandTitles(), "the copy arrived before the two Defends were drawn");
            c.Equal(82, g.Enemy(0).Hp, "three free Strikes");
        });
    }

    // ---------- Mangle ----------

    private static void Mangle(Check c)
    {
        c.Group("Mangle: 15 damage (20), then the target loses 10 Strength (15) until the end of its turn", () =>
        {
            var f = CheckKit.VsDummy(Hitter(12));
            var card = InHand(f, "Mangle");
            c.Equal("Deal 15 damage. The enemy loses 10 Strength until the end of its turn.", Text(f, card), "text");
            f.Play("Mangle", 0);
            c.Equal(85, f.Enemy(0).Hp, "15 damage");
            c.Equal(0, f.Energy, "costs 3");
            c.Equal(10, f.Enemy(0).PowerAmount<ManglePower>(), "Mangle 10");
            c.Equal(-10, f.Enemy(0).PowerAmount<StrengthPower>(), "Strength -10");
            c.Sequence(["attack 2"], f.IntentTexts(f.Enemy(0)), "12 - 10");
            f.EndTurn();
            c.Equal(78, f.Player.Hp, "it hit for 2");
            c.That(!f.Enemy(0).HasPower<ManglePower>(), "Mangle ended with the enemy's turn");
            c.That(!f.Enemy(0).HasPower<StrengthPower>(), "and the Strength is back at 0");
            c.Sequence(["attack 12"], f.IntentTexts(f.Enemy(0)), "the next hit is whole again");

            // Upgraded: 12 - 15 is below 0: no damage at all.
            var g = CheckKit.VsDummy(Hitter(12));
            var plus = InHand(g, "Mangle+");
            c.Equal("Deal 20 damage. The enemy loses 15 Strength until the end of its turn.", Text(g, plus), "upgraded text");
            g.Play("Mangle", 0);
            c.Equal(80, g.Enemy(0).Hp, "upgraded: 20 damage");
            c.Equal(-15, g.Enemy(0).PowerAmount<StrengthPower>(), "upgraded: Strength -15");
            c.Sequence(["attack 0"], g.IntentTexts(g.Enemy(0)), "never below 0");
            g.EndTurn();
            c.Equal(80, g.Player.Hp, "no damage");
        });

        c.Group("Mangle: Strength the enemy has, a second Mangle, Artifact, a dead target", () =>
        {
            var f = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("HIT", Damage: 12)], OnAdded = (x, me) => x.ApplyPower<StrengthPower>(me, 3, me) });
            InHand(f, "Mangle");
            f.Play("Mangle", 0);
            c.Equal(-7, f.Enemy(0).PowerAmount<StrengthPower>(), "3 - 10");
            c.Sequence(["attack 5"], f.IntentTexts(f.Enemy(0)), "12 + 3 - 10");
            f.EndTurn();
            c.Equal(75, f.Player.Hp, "it hit for 5");
            c.Equal(3, f.Enemy(0).PowerAmount<StrengthPower>(), "its own 3 Strength is back");

            var g = CheckKit.VsDummy();
            g.SetEnergy(6);
            InHand(g, "Mangle");
            InHand(g, "Mangle");
            g.Play("Mangle", 0);
            g.Play("Mangle", 0);
            c.Equal(70, g.Enemy(0).Hp, "15 + 15: Strength loss does not change the damage it takes");
            c.Equal(20, g.Enemy(0).PowerAmount<ManglePower>(), "10 + 10");
            c.Equal(-20, g.Enemy(0).PowerAmount<StrengthPower>(), "Strength -20");
            g.EndTurn();
            c.That(!g.Enemy(0).HasPower<StrengthPower>() && !g.Enemy(0).HasPower<ManglePower>(), "all of it is given back");

            var a = CheckKit.VsDummy(new CheckDummy { OnAdded = (x, me) => x.ApplyPower<ArtifactPower>(me, 1, me) });
            InHand(a, "Mangle");
            a.Play("Mangle", 0);
            c.Equal(85, a.Enemy(0).Hp, "the damage lands");
            c.That(!a.Enemy(0).HasPower<ManglePower>() && !a.Enemy(0).HasPower<StrengthPower>(), "Artifact stops the whole Strength loss");
            c.That(!a.Enemy(0).HasPower<ArtifactPower>(), "and loses its one charge");

            var d = CheckKit.VsDummies(new CheckDummy { Hp = 15 }, new CheckDummy());
            InHand(d, "Mangle");
            d.Play("Mangle", 0);
            c.That(d.Enemy(0).IsDead, "the hit killed it");
            c.That(!d.Enemy(1).HasPower<ManglePower>() && !d.Enemy(1).HasPower<StrengthPower>(), "nothing lands elsewhere");
        });
    }

    // ---------- Molten Fist ----------

    private static void MoltenFist(Check c)
    {
        c.Group("Molten Fist: 10 damage (14), then the target's Vulnerable is doubled", () =>
        {
            var f = CheckKit.VsDummy();
            var card = InHand(f, "MoltenFist");
            c.Equal("Deal 10 damage. Double the enemy's Vulnerable. / Exhaust.", Text(f, card), "text");
            f.Play("MoltenFist", 0);
            c.Equal(90, f.Enemy(0).Hp, "10 damage");
            c.That(!f.Enemy(0).HasPower<VulnerablePower>(), "no Vulnerable, none added");
            c.Equal(PileType.Exhaust, card.Pile, "exhausted");
            c.Equal(2, f.Energy, "costs 1");

            // After Bash: 8, Vulnerable 2; then 10 x 1.5 = 15 and Vulnerable 4.
            var g = CheckKit.VsDummy();
            InHand(g, "MoltenFist");
            g.Play("Bash", 0);
            g.Play("MoltenFist", 0);
            c.Equal(77, g.Enemy(0).Hp, "8 + 15: the Vulnerable already there counts for the hit");
            c.Equal(4, g.Enemy(0).PowerAmount<VulnerablePower>(), "2 doubled");

            // Upgraded: 14 x 1.5 = 21; Vulnerable 1 becomes 2.
            var h = CheckKit.VsDummy();
            h.ApplyPower<VulnerablePower>(h.Enemy(0), 1, h.Player);
            var plus = InHand(h, "MoltenFist+");
            c.Equal("Deal 14 damage. Double the enemy's Vulnerable. / Exhaust.", Text(h, plus), "upgraded text");
            h.Play("MoltenFist", 0);
            c.Equal(79, h.Enemy(0).Hp, "upgraded: 21");
            c.Equal(2, h.Enemy(0).PowerAmount<VulnerablePower>(), "1 doubled");
        });

        c.Group("Molten Fist: Artifact stops the added stacks; a dead target gets none", () =>
        {
            var f = CheckKit.VsDummy();
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 2, f.Player);
            f.ApplyPower<ArtifactPower>(f.Enemy(0), 1, f.Enemy(0));
            InHand(f, "MoltenFist");
            f.Play("MoltenFist", 0);
            c.Equal(85, f.Enemy(0).Hp, "15");
            c.Equal(2, f.Enemy(0).PowerAmount<VulnerablePower>(), "still 2");
            c.That(!f.Enemy(0).HasPower<ArtifactPower>(), "Artifact is used up");

            var g = CheckKit.VsDummies(new CheckDummy { Hp = 15 }, new CheckDummy());
            g.ApplyPower<VulnerablePower>(g.Enemy(0), 2, g.Player);
            InHand(g, "MoltenFist");
            g.Play("MoltenFist", 0);
            c.That(g.Enemy(0).IsDead, "15 killed it");
            c.That(!g.Enemy(1).HasPower<VulnerablePower>(), "nothing lands elsewhere");
        });
    }

    // ---------- Not Yet ----------

    private static void NotYet(Check c)
    {
        c.Group("Not Yet: heal 10 (13), up to max HP", () =>
        {
            var f = CheckKit.VsDummy();
            f.SetHp(f.Player, 60);
            var card = InHand(f, "NotYet");
            c.Equal("Heal 10 HP. / Exhaust.", Text(f, card), "text");
            f.Play("NotYet");
            c.Equal(70, f.Player.Hp, "10 HP");
            c.Equal(1, f.Energy, "costs 2");
            c.Equal(PileType.Exhaust, card.Pile, "exhausted");

            var g = CheckKit.VsDummy();
            g.SetHp(g.Player, 60);
            var plus = InHand(g, "NotYet+");
            c.Equal("Heal 13 HP. / Exhaust.", Text(g, plus), "upgraded text");
            g.Play("NotYet");
            c.Equal(73, g.Player.Hp, "upgraded: 13 HP");

            var h = CheckKit.VsDummy();
            h.SetHp(h.Player, 75);
            InHand(h, "NotYet");
            h.Play("NotYet");
            c.Equal(80, h.Player.Hp, "never above max HP");

            var full = CheckKit.VsDummy();
            InHand(full, "NotYet");
            c.That(full.LegalActions().Any(a => a.Id == "play:5"), "playable at full HP: no play condition");
        });
    }

    // ---------- Offering ----------

    private static void Offering(Check c)
    {
        c.Group("Offering: lose 6 HP, gain 2 energy, draw 3 (5)", () =>
        {
            var f = CheckKit.VsDummy();
            var card = InHand(f, "Offering");
            c.Equal("Lose 6 HP. Gain 2 energy. Draw 3 cards. / Exhaust.", Text(f, card), "text");
            f.Play("Offering");
            c.Equal(74, f.Player.Hp, "6 HP");
            c.Equal(5, f.Energy, "costs 0, gives 2");
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash", "Strike", "Strike", "Strike"], f.HandTitles(), "three cards drawn");
            c.Equal(PileType.Exhaust, card.Pile, "exhausted");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "Offering+");
            c.Equal("Lose 6 HP. Gain 2 energy. Draw 5 cards. / Exhaust.", Text(g, plus), "upgraded text");
            g.Play("Offering");
            c.Equal(74, g.Player.Hp, "upgraded: still 6 HP");
            c.Equal(5, g.Energy, "upgraded: still 2 energy");
            c.Equal(10, g.Hand.Count, "upgraded: five cards drawn");
            c.Equal(0, g.DrawPile.Count, "the draw pile is empty");
        });

        c.Group("Offering: the HP loss ignores Block; the hand limit; death stops the rest", () =>
        {
            var f = CheckKit.VsDummy();
            f.SetBlock(f.Player, 10);
            f.ApplyPower<VulnerablePower>(f.Player, 1, f.Enemy(0));
            InHand(f, "Offering");
            f.Play("Offering");
            c.Equal(74, f.Player.Hp, "exactly 6 through 10 Block");
            c.Equal(10, f.Player.Block, "Block untouched");

            // Nine cards in hand after it leaves: one draw only.
            var g = CheckKit.VsDummy();
            InHand(g, "Offering");
            for (var i = 0; i < 4; i++) InHand(g, "Wound");
            g.Play("Offering");
            c.Equal(10, g.Hand.Count, "the draw stops at ten");
            c.Equal(4, g.DrawPile.Count, "one card was drawn");

            var d = CheckKit.VsDummy();
            d.SetHp(d.Player, 6);
            InHand(d, "Offering");
            d.Play("Offering");
            c.That(d.IsOver && !d.Won, "6 HP lost at 6 HP: the fight is lost");
            c.Equal(3, d.Energy, "no energy");
            c.Equal(5, d.Hand.Count, "no draw");
        });
    }

    // ---------- One-Two Punch ----------

    private static void OneTwoPunch(Check c)
    {
        c.Group("One-Two Punch: the next Attack runs twice (the next two, upgraded)", () =>
        {
            var f = CheckKit.VsDummy();
            var card = InHand(f, "OneTwoPunch");
            c.Equal("The next Attack you play this turn is played twice.", Text(f, card), "text");
            f.Play("OneTwoPunch");
            c.Equal(1, f.Player.PowerAmount<OneTwoPunchPower>(), "the power, 1");
            c.Equal(PileType.Discard, card.Pile, "to the discard pile");
            f.Play("Strike", 0);
            c.Equal(88, f.Enemy(0).Hp, "the Strike hit twice");
            c.Equal(1, f.Energy, "1 + 1: the Attack is paid for once");
            c.That(!f.Player.HasPower<OneTwoPunchPower>(), "the charge is spent");
            c.Equal(2, f.DiscardPile.Count, "One-Two Punch and one Strike: it moves once");
            f.Play("Strike", 0);
            c.Equal(82, f.Enemy(0).Hp, "the next Attack runs once");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "OneTwoPunch+");
            c.Equal("The next 2 Attacks you play this turn are each played twice.", Text(g, plus), "upgraded text");
            g.Play("OneTwoPunch");
            c.Equal(2, g.Player.PowerAmount<OneTwoPunchPower>(), "upgraded: 2");
            g.Play("Strike", 0);
            c.Equal(88, g.Enemy(0).Hp, "first Attack twice, not three times");
            c.Equal(1, g.Player.PowerAmount<OneTwoPunchPower>(), "one charge left");
            g.Play("Strike", 0);
            c.Equal(76, g.Enemy(0).Hp, "second Attack twice");
            c.That(!g.Player.HasPower<OneTwoPunchPower>(), "both charges spent");
        });

        c.Group("One-Two Punch: the whole effect runs again on the same target", () =>
        {
            // Bash: 8 and Vulnerable 2, then 8 x 1.5 = 12 and Vulnerable 4.
            var f = CheckKit.VsDummy();
            InHand(f, "OneTwoPunch");
            f.Play("OneTwoPunch");
            f.Play("Bash", 0);
            c.Equal(80, f.Enemy(0).Hp, "8 + 12");
            c.Equal(4, f.Enemy(0).PowerAmount<VulnerablePower>(), "2 + 2");
            c.Equal(0, f.Energy, "1 + 2");

            // The target dies in the first run: the second finds nobody and does nothing.
            var g = CheckKit.VsDummies(new CheckDummy { Hp = 6 }, new CheckDummy());
            InHand(g, "OneTwoPunch");
            g.Play("OneTwoPunch");
            g.Play("Strike", 0);
            c.That(g.Enemy(0).IsDead, "the first run killed it");
            c.Equal(100, g.Enemy(1).Hp, "the second run is not redirected");
            c.That(!g.Player.HasPower<OneTwoPunchPower>(), "the charge is spent all the same");

            // Feed: the kill of the first run counts once.
            var h = CheckKit.VsDummies(new CheckDummy { Hp = 10 }, new CheckDummy());
            h.SetHp(h.Player, 70);
            InHand(h, "OneTwoPunch");
            InHand(h, "Feed");
            h.Play("OneTwoPunch");
            h.Play("Feed", 0);
            c.Equal(83, h.Player.MaxHp, "+3, once");

            // An exhausting Attack is exhausted once, after both runs.
            var m = CheckKit.VsDummy();
            InHand(m, "OneTwoPunch");
            InHand(m, "MoltenFist");
            m.Play("OneTwoPunch");
            m.Play("MoltenFist", 0);
            c.Equal(80, m.Enemy(0).Hp, "10 + 10");
            c.Equal(1, m.ExhaustPile.Count, "one card in the exhaust pile");
        });

        c.Group("One-Two Punch: Skills do not use it; free plays do; it ends with the turn", () =>
        {
            var f = CheckKit.VsDummy();
            InHand(f, "OneTwoPunch");
            f.Play("OneTwoPunch");
            f.Play("Defend");
            c.Equal(5, f.Player.Block, "Defend ran once");
            c.Equal(1, f.Player.PowerAmount<OneTwoPunchPower>(), "the charge is still there");
            f.EndTurn();
            c.That(!f.Player.HasPower<OneTwoPunchPower>(), "unused charges end with the turn");
            f.Play("Strike", 0);
            c.Equal(94, f.Enemy(0).Hp, "next turn's Attack runs once");

            // Havoc is a Skill; the Strike it plays is an Attack.
            var g = CheckKit.VsDummy();
            InHand(g, "OneTwoPunch");
            InHand(g, "Havoc");
            g.Play("OneTwoPunch");
            g.Play("Havoc");
            c.Equal(88, g.Enemy(0).Hp, "the free Strike ran twice");
            c.Equal(1, g.ExhaustPile.Count, "and was exhausted once");
            c.Equal(1, g.Rng.CombatTargets.Counter, "one target for both runs");
            c.That(!g.Player.HasPower<OneTwoPunchPower>(), "the charge is spent");
        });
    }

    // ---------- Pact's End ----------

    private static void PactsEnd(Check c)
    {
        c.Group("Pact's End: 17 to every enemy (23) with 3 or more cards in the exhaust pile", () =>
        {
            var f = CheckKit.VsDummies(new CheckDummy(), new CheckDummy());
            var card = InHand(f, "PactsEnd");
            c.Equal("If your exhaust pile has 3 or more cards, deal 17 damage to ALL enemies.", Text(f, card), "text");
            c.That(f.LegalActions().Any(a => a.Id == "play:5"), "always playable");
            f.Play("PactsEnd");
            c.Equal(200, f.Enemy(0).Hp + f.Enemy(1).Hp, "an empty exhaust pile: nothing");
            c.Equal(3, f.Energy, "costs 0");
            c.Equal(PileType.Discard, card.Pile, "to the discard pile either way");

            var two = CheckKit.VsDummies(new CheckDummy(), new CheckDummy());
            two.Put("Wound", PileType.Exhaust);
            two.Put("Wound", PileType.Exhaust);
            InHand(two, "PactsEnd");
            two.Play("PactsEnd");
            c.Equal(200, two.Enemy(0).Hp + two.Enemy(1).Hp, "two exhausted cards: nothing");

            var g = CheckKit.VsDummies(new CheckDummy(), new CheckDummy());
            for (var i = 0; i < 3; i++) g.Put("Wound", PileType.Exhaust);
            InHand(g, "PactsEnd");
            g.Play("PactsEnd");
            c.Equal(83, g.Enemy(0).Hp, "17 to the first");
            c.Equal(83, g.Enemy(1).Hp, "17 to the second");

            var h = CheckKit.VsDummies(new CheckDummy(), new CheckDummy());
            for (var i = 0; i < 3; i++) h.Put("Wound", PileType.Exhaust);
            var plus = InHand(h, "PactsEnd+");
            c.Equal("If your exhaust pile has 3 or more cards, deal 23 damage to ALL enemies.", Text(h, plus), "upgraded text: the threshold stays 3");
            h.Play("PactsEnd");
            c.Equal(77, h.Enemy(0).Hp, "upgraded: 23");
            c.Equal(77, h.Enemy(1).Hp, "upgraded: 23 to both");

            var up2 = CheckKit.VsDummies(new CheckDummy(), new CheckDummy());
            up2.Put("Wound", PileType.Exhaust);
            up2.Put("Wound", PileType.Exhaust);
            InHand(up2, "PactsEnd+");
            up2.Play("PactsEnd");
            c.Equal(200, up2.Enemy(0).Hp + up2.Enemy(1).Hp, "upgraded with two exhausted cards: nothing");
        });

        c.Group("Pact's End: worked out per enemy; played Powers are not in the exhaust pile", () =>
        {
            // 17 x 1.5 = 25.5 -> 25 into Vulnerable; 17 - 5 Block = 12; Strength 2 would make it 19.
            var f = CheckKit.VsDummies(new CheckDummy(), new CheckDummy());
            for (var i = 0; i < 4; i++) f.Put("Wound", PileType.Exhaust);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            f.SetBlock(f.Enemy(1), 5);
            InHand(f, "PactsEnd");
            f.Play("PactsEnd");
            c.Equal(75, f.Enemy(0).Hp, "Vulnerable on the first");
            c.Equal(88, f.Enemy(1).Hp, "Block on the second");

            // A Howl from Beyond that played itself out of the exhaust pile no longer counts.
            var h = CheckKit.VsDummy();
            h.Put("Wound", PileType.Exhaust);
            h.Put("Wound", PileType.Exhaust);
            h.Put("HowlFromBeyond", PileType.Exhaust);
            h.EndTurn();
            c.Equal(84, h.Enemy(0).Hp, "Howl's free play");
            c.Equal(2, h.ExhaustPile.Count, "two cards are left in the exhaust pile");
            InHand(h, "PactsEnd");
            h.Play("PactsEnd");
            c.Equal(84, h.Enemy(0).Hp, "so Pact's End does nothing");

            // Two exhausted cards and a played Power: still two.
            var g = CheckKit.VsDummy();
            g.Put("Wound", PileType.Exhaust);
            g.Put("Wound", PileType.Exhaust);
            InHand(g, "Inflame");
            InHand(g, "PactsEnd");
            g.Play("Inflame");
            g.Play("PactsEnd");
            c.Equal(100, g.Enemy(0).Hp, "a played Power does not count");
        });
    }

    // ---------- Perfected Strike ----------

    private static void PerfectedStrike(Check c)
    {
        c.Group("Perfected Strike: 6 + 2 (3) per Strike card in any pile, itself included", () =>
        {
            // The starter's five Strikes and this card: 6 + 2 x 6 = 18.
            var f = CheckKit.VsDummy();
            var card = InHand(f, "PerfectedStrike");
            c.Equal("Deal 18 damage (6, plus 2 for each of your cards with Strike in its name, in any pile).", Text(f, card), "text in hand");
            c.Equal("Deal 6 damage (6, plus 2 for each of your cards with Strike in its name, in any pile).", Fight.CardText(null, card, PileType.Deck), "text outside a fight: the base alone");
            c.Equal(18m, f.DamageNumberOf(card), "its damage number as another effect reads it");
            f.Play("PerfectedStrike", 0);
            c.Equal(82, f.Enemy(0).Hp, "18 damage: it counted itself in the Play pile");
            c.Equal(1, f.Energy, "costs 2");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "PerfectedStrike+");
            c.Equal("Deal 24 damage (6, plus 3 for each of your cards with Strike in its name, in any pile).", Text(g, plus), "upgraded text");
            g.Play("PerfectedStrike", 0);
            c.Equal(76, g.Enemy(0).Hp, "upgraded: 6 + 3 x 6");
        });

        c.Group("Perfected Strike: every pile counts; Strength is added once", () =>
        {
            // Two more Strikes in the exhaust pile, one in the discard pile, a second Perfected Strike in the draw pile:
            // 5 + 3 + 2 = 10 Strike cards: 6 + 2 x 10 = 26.
            var f = CheckKit.VsDummy();
            f.Put("StrikeIronclad", PileType.Exhaust);
            f.Put("StrikeIronclad+", PileType.Exhaust);
            f.Put("StrikeIronclad", PileType.Discard);
            f.Put("PerfectedStrike", PileType.Draw);
            InHand(f, "PerfectedStrike");
            f.Play("PerfectedStrike", 0);
            c.Equal(74, f.Enemy(0).Hp, "26 damage");

            // (18 + 2) x 1.5 = 30; the text shows 20.
            var g = CheckKit.VsDummy();
            g.ApplyPower<StrengthPower>(g.Player, 2, g.Player);
            g.ApplyPower<VulnerablePower>(g.Enemy(0), 1, g.Player);
            var card = InHand(g, "PerfectedStrike");
            c.That(Text(g, card).StartsWith("Deal 20 damage (6, plus 2", StringComparison.Ordinal), "the text adds Strength once");
            g.Play("PerfectedStrike", 0);
            c.Equal(70, g.Enemy(0).Hp, "30 damage");

            // Weak: 18 x 0.75 = 13.5 -> 13.
            var w = CheckKit.VsDummy();
            w.ApplyPower<WeakPower>(w.Player, 1, w.Enemy(0));
            InHand(w, "PerfectedStrike");
            w.Play("PerfectedStrike", 0);
            c.Equal(87, w.Enemy(0).Hp, "13 damage");

            // Without any other Strike: 6 + 2 x 1 = 8.
            var alone = new FightBuilder().DrawOrder("PerfectedStrike", "DefendIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad", "Bash").Enemy(new CheckDummy()).Start();
            alone.Play("PerfectedStrike", 0);
            c.Equal(92, alone.Enemy(0).Hp, "8 damage");
        });
    }

    // ---------- Pillage ----------

    private static void Pillage(Check c)
    {
        c.Group("Pillage: 6 damage (9), then draws until the card drawn is not an Attack", () =>
        {
            // The draw pile from the top: Strike, Strike, Strike, Defend, Defend.
            var f = CheckKit.VsDummy();
            var card = InHand(f, "Pillage");
            c.Equal("Deal 6 damage. Draw a card; if it is an Attack, draw another, and so on.", Text(f, card), "text");
            f.Play("Pillage", 0);
            c.Equal(94, f.Enemy(0).Hp, "6 damage");
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash", "Strike", "Strike", "Strike", "Defend"], f.HandTitles(), "three Attacks and the Defend that stopped it");
            c.Equal(1, f.DrawPile.Count, "one card left");
            c.Equal(2, f.Energy, "costs 1");

            var g = CheckKit.VsDummy();
            var plus = InHand(g, "Pillage+");
            c.Equal("Deal 9 damage. Draw a card; if it is an Attack, draw another, and so on.", Text(g, plus), "upgraded text");
            g.Play("Pillage", 0);
            c.Equal(91, g.Enemy(0).Hp, "upgraded: 9 damage");
            c.Equal(9, g.Hand.Count, "upgraded: the same drawing");

            // A Skill on top: one draw.
            var h = CheckKit.VsDummy();
            InHand(h, "Pillage");
            OnDraw(h, "DefendIronclad");
            h.Play("Pillage", 0);
            c.Equal(6, h.Hand.Count, "one card drawn");
        });

        c.Group("Pillage: the hand limit and empty piles stop it; a reshuffle does not", () =>
        {
            // Nine cards in hand after it leaves: one Strike makes ten, and the drawing stops on an Attack.
            var f = CheckKit.VsDummy();
            InHand(f, "Pillage");
            for (var i = 0; i < 4; i++) InHand(f, "Wound");
            f.Play("Pillage", 0);
            c.Equal(10, f.Hand.Count, "ten cards");
            c.Equal(4, f.DrawPile.Count, "one card was drawn");

            // Ten cards already: no draw at all.
            var full = CheckKit.VsDummy();
            InHand(full, "Pillage");
            for (var i = 0; i < 5; i++) InHand(full, "Wound");
            full.Play("Pillage", 0);
            c.Equal(10, full.Hand.Count, "still ten");
            c.Equal(5, full.DrawPile.Count, "nothing was drawn");
            c.Equal(94, full.Enemy(0).Hp, "the damage still lands");

            // Nothing to draw.
            var none = CheckKit.VsDummy();
            none.Clear(PileType.Draw);
            InHand(none, "Pillage");
            none.Play("Pillage", 0);
            c.Equal(5, none.Hand.Count, "no card");

            // A draw-blocking effect: one attempt, nothing drawn.
            var locked = CheckKit.VsDummy();
            locked.ApplyPower<Lockdown>(locked.Player, 1, locked.Enemy(0));
            InHand(locked, "Pillage");
            locked.Play("Pillage", 0);
            c.Equal(5, locked.Hand.Count, "no card through a blocked draw");
            c.Equal(94, locked.Enemy(0).Hp, "the damage still lands");

            // An empty draw pile and three Strikes in the discard pile: they are shuffled in and all drawn.
            var g = CheckKit.VsDummy();
            g.Clear(PileType.Draw);
            for (var i = 0; i < 3; i++) g.Put("StrikeIronclad", PileType.Discard);
            var pillage = InHand(g, "Pillage");
            g.Play("Pillage", 0);
            c.Equal(8, g.Hand.Count, "three Strikes drawn through the reshuffle");
            c.Equal(2, g.Rng.Shuffle.Counter, "three cards were shuffled: two draws");
            c.Sequence([pillage], g.DiscardPile, "Pillage was in the Play pile meanwhile, so it was not shuffled in");
        });

        c.Group("Pillage: with Hellraiser a drawn Strike is played and the drawing goes on", () =>
        {
            var f = CheckKit.VsDummy();
            f.ApplyPower<HellraiserPower>(f.Player, 1, f.Player);
            InHand(f, "Pillage");
            f.Play("Pillage", 0);
            c.Equal(76, f.Enemy(0).Hp, "6, then three free Strikes");
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash", "Defend"], f.HandTitles(), "only the Defend that stopped it stays");
        });
    }

    // ---------- the powers as the snapshot shows them, and copies of a fight ----------

    private static void PowersAsShown(Check c)
    {
        c.Group("Powers of Cards2: texts with the live numbers, kinds and amounts", () =>
        {
            var f = CheckKit.VsDummy();
            var me = f.Player;
            c.Equal("Whenever a card is Exhausted, gain 3 Block.", f.ApplyPower<FeelNoPainPower>(me, 3, me)!.Describe(), "Feel No Pain");
            c.Equal("Whenever an attack hits you, deal 4 damage back to the attacker. Ends after the enemy turn.", f.ApplyPower<FlameBarrierPower>(me, 4, me)!.Describe(), "Flame Barrier");
            c.Equal("Whenever you draw a card with Strike in its name, it is played for free at a random enemy.", f.ApplyPower<HellraiserPower>(me, 1, me)!.Describe(), "Hellraiser");
            c.Equal("Whenever you gain Block, deal 6 damage to a random enemy.", f.ApplyPower<JuggernautPower>(me, 6, me)!.Describe(), "Juggernaut");
            c.Equal("Whenever you play your third Attack in a turn, add a copy of it to your hand.", f.ApplyPower<JugglingPower>(me, 1, me)!.Describe(), "Juggling 1");
            c.Equal("Whenever you play your third Attack in a turn, add 2 copies of it to your hand.", f.ApplyPower<JugglingPower>(me, 1, me)!.Describe(), "Juggling 2");
            c.Equal("You cannot gain energy this turn.", f.ApplyPower<NoEnergyGainPower>(me, 1, me)!.Describe(), "No Energy Gain");
            c.Equal("The next Attack you play this turn is played twice.", f.ApplyPower<OneTwoPunchPower>(me, 1, me)!.Describe(), "One-Two Punch 1");
            c.Equal("The next 2 Attacks you play this turn are each played twice.", f.ApplyPower<OneTwoPunchPower>(me, 1, me)!.Describe(), "One-Two Punch 2");
            c.Equal("Has 10 less Strength until the end of its turn.", f.ApplyPower<ManglePower>(f.Enemy(0), 10, me)!.Describe(), "Mangle");

            InHand(f, "Inferno+");
            f.Play("Inferno");
            c.Equal("At the start of your turn, lose 1 HP. Whenever you lose HP on your turn, deal 9 damage to ALL enemies.", me.GetPower<InfernoPower>()!.Describe(), "Inferno");

            var shown = f.Snapshot().State["me"]!["powers"]!.AsArray().ToDictionary(p => p!["name"]!.GetValue<string>(), p => p!);
            c.Equal("Debuff", shown["No Energy Gain"]["kind"]!.GetValue<string>(), "No Energy Gain is listed as a Debuff");
            c.Equal("Buff", shown["Inferno"]["kind"]!.GetValue<string>(), "Inferno is listed as a Buff");
            c.Equal(9, shown["Inferno"]["amount"]!.GetValue<int>(), "with its amount");
            c.Equal(2, shown["One-Two Punch"]["amount"]!.GetValue<int>(), "One-Two Punch shows its charges");
            var enemy = f.Snapshot().State["enemies"]![0]!["powers"]!.AsArray().ToDictionary(p => p!["name"]!.GetValue<string>(), p => p!);
            c.Equal("Debuff", enemy["Mangle"]["kind"]!.GetValue<string>(), "Mangle is listed as a Debuff on the enemy");
            c.Equal(-10, enemy["Strength"]["amount"]!.GetValue<int>(), "next to its Strength of -10");
        });
    }

    private static void Copies(Check c)
    {
        c.Group("Cards2: what the powers count is copied with the fight, not shared", () =>
        {
            // Juggling's count: two Attacks played, then the third in a copy only.
            var f = CheckKit.VsDummy();
            f.SetEnergy(10);
            f.ApplyPower<JugglingPower>(f.Player, 1, f.Player);
            f.Play("Strike", 0);
            f.Play("Strike", 0);
            var copy = f.Clone();
            copy.Play("Bash", 0);
            c.Sequence(["Defend", "Defend", "Bash"], copy.HandTitles(), "the copy counted to three");
            c.Sequence(["Defend", "Defend", "Bash"], f.HandTitles(), "the original still holds its own Bash");
            c.Equal(88, f.Enemy(0).Hp, "the original was not hit");
            f.Play("Bash", 0);
            c.Sequence(["Defend", "Defend", "Bash"], f.HandTitles(), "and counts to three by itself");
            c.Equal(80, f.Enemy(0).Hp, "the original's own Bash");

            // Inferno's self-damage.
            var g = CheckKit.VsDummy();
            InHand(g, "Inferno");
            InHand(g, "Inferno");
            g.Play("Inferno");
            var before = g.Clone();
            g.Play("Inferno");
            c.Equal(2, g.Player.GetPower<InfernoPower>()!.SelfDamage, "the original: two plays");
            c.Equal(1, before.Player.GetPower<InfernoPower>()!.SelfDamage, "the copy taken after one play");
            before.EndTurn();
            c.Equal(79, before.Player.Hp, "the copy loses 1 HP");
            c.Equal(94, before.Enemy(0).Hp, "and deals 6");
            c.Equal(80, g.Player.Hp, "the original is untouched");

            // A fight with these cards replays identically from its seed.
            string Run()
            {
                var x = new FightBuilder().Deck("StrikeIronclad", "StrikeIronclad", "DefendIronclad", "Bash", "Havoc", "Juggernaut", "FeelNoPain", "InfernalBlade", "Pillage",
                    "Hellraiser", "PerfectedStrike", "IronWave", "Offering", "FiendFire", "Inferno", "OneTwoPunch", "Headbutt", "HowlFromBeyond").Seed(5).Encounter("NibbitsNormal").Start();
                return CheckKit.PlayOut(x);
            }
            c.That(Run() == Run(), "the same seed and the same actions show the same fight");
        });
    }
}
