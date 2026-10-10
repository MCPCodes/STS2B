namespace SpireMind.Arena;

/// <summary>
/// The worked examples of rules-damage.md (R27's table, R1's fractions, R15's tick timing,
/// R22 and R23's intents) played through the engine, with the starter cards, the core powers
/// and a scripted dummy.
/// </summary>
public sealed class CoreDamageChecks : IArenaChecks
{
    /// <summary>A two-hit attack card for the multi-hit examples (5 x 2, like the game's Twin Strike).</summary>
    private static readonly CardDef TwoHits = new("CheckTwoHits", "Two Hits", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy)
    {
        Vars = { ["Damage"] = 5 },
        Text = t => $"Deal {t.Damage()} damage twice.",
        Play = c => c.Attack(c.V("Damage")).Hits(2).Execute(),
    };

    /// <summary>Calculated damage: 0 + 1 x the player's Block (the shape of the game's Body Slam).</summary>
    private static readonly CardDef Slam = new("CheckSlam", "Slam", CardType.Attack, CardRarity.Common, 0, TargetType.AnyEnemy)
    {
        Vars = { ["CalculationBase"] = 0, ["ExtraDamage"] = 1 },
        DamageMultiplier = (f, _, _) => f.Player.Block,
        Text = t => $"Deal {t.CalculatedDamage()} damage.",
        Play = c => c.CalculatedAttack().Execute(),
    };

    public void Run(Check c)
    {
        c.Group("R24: a calculated damage number in the text, in the play and as other effects read it", () =>
        {
            var f = CheckKit.VsDummy();
            var slam = f.Put(Slam, PileType.Hand);
            f.SetBlock(f.Player, 7);
            f.ApplyPower<StrengthPower>(f.Player, 2, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            c.Equal("Deal 9 damage.", Fight.CardText(f, slam, PileType.Hand), "in hand: Block read live, Strength added once, no Vulnerable");
            c.Equal("Deal 7 damage.", Fight.CardText(f, slam, PileType.Discard), "in another pile: the calculated number without the modifiers");
            c.Equal("Deal 0 damage.", Fight.CardText(null, slam, PileType.Deck), "outside a fight: the base alone");
            c.Equal(7m, f.DamageNumberOf(slam), "its damage number as another effect reads it");
            c.Equal(6m, f.DamageNumberOf(f.Hand[0]), "a plain card's damage number is its Damage");
            f.Play("Slam", 0);
            c.Equal(87, f.Enemy(0).Hp, "(7 + 2) x 1.5 = 13.5 -> 13");
        });

        c.Group("R27 rows 1-10: one hit through the modifier chain", () =>
        {
            // Row 1: (6 + 2) x 0.75 x 1.5 = 9.000, as an exact decimal.
            var f = CheckKit.VsDummy(new CheckDummy { Hp = 300 });
            var me = f.Player;
            var e = f.Enemy(0);
            f.ApplyPower<StrengthPower>(me, 2, me);
            f.ApplyPower<WeakPower>(me, 1, e);
            f.ApplyPower<VulnerablePower>(e, 1, me);
            c.Equal(9.000m, f.ModifyDamage(e, me, 6m, Props.Card, null), "row 1: 6 base, Strength 2, Weak, Vulnerable");
            // Row 6: (6 + 1) x 0.75 x 1.5 = 7.875 stays a fraction until it meets HP.
            f.ApplyPower<StrengthPower>(me, -1, me);
            c.Equal(7.875m, f.ModifyDamage(e, me, 6m, Props.Card, null), "row 6 before truncation");
            var hp = e.Hp;
            f.Play("Strike", 0);
            c.Equal(7, hp - e.Hp, "row 6: Strike with Strength 1, Weak, into Vulnerable");
            // Unpowered damage ignores all of it (R2).
            c.Equal(6m, f.ModifyDamage(e, me, 6m, Props.NonCardUnpowered, null), "unpowered damage is not modified");
            c.Equal(6m, f.ModifyDamage(e, me, 6m, Props.CardHpLoss, null), "card HP loss is not modified");

            // Row 2: Strike, player Weak: 4.5 -> 4.
            c.Equal(4, StrikeFor(x => x.ApplyPower<WeakPower>(x.Player, 1, x.Enemy(0))), "row 2: Strike while Weak");
            // Row 3: Strike into Vulnerable: 9.
            c.Equal(9, StrikeFor(x => x.ApplyPower<VulnerablePower>(x.Enemy(0), 1, x.Player)), "row 3: Strike into Vulnerable");
            // Row 7: Shrink: 6 x 0.7 = 4.2 -> 4.  Row 8: Shrink and Weak: 3.15 -> 3.
            c.Equal(4, StrikeFor(x => x.ApplyPower<ShrinkPower>(x.Player, -1, x.Enemy(0))), "row 7: Strike with Shrink");
            c.Equal(3, StrikeFor(x =>
            {
                x.ApplyPower<ShrinkPower>(x.Player, -1, x.Enemy(0));
                x.ApplyPower<WeakPower>(x.Player, 1, x.Enemy(0));
            }), "row 8: Strike with Shrink and Weak");
            // Row 10: (2 - 3) x 1.5 is negative: clamped at 0, there is no minimum of 1.
            var g = CheckKit.VsDummy();
            g.ApplyPower<StrengthPower>(g.Player, -3, g.Enemy(0));
            g.ApplyPower<VulnerablePower>(g.Enemy(0), 1, g.Player);
            c.Equal(0m, g.ModifyDamage(g.Enemy(0), g.Player, 2m, Props.Card, null), "row 10: negative total clamps at 0");
            c.Equal(PowerType.Debuff, g.Player.GetPower<StrengthPower>()!.TypeForCurrentAmount, "negative Strength reads as a Debuff");
        });

        c.Group("R27 rows 4, 5, 30: Bash", () =>
        {
            // Row 5: 8, then Vulnerable 2; the new Vulnerable does not raise its own hit.
            var f = CheckKit.VsDummy();
            var e = f.Enemy(0);
            f.Play("Bash", 0);
            c.Equal(92, e.Hp, "row 5: Bash does 8");
            c.Equal(2, e.PowerAmount<VulnerablePower>(), "row 5: then 2 Vulnerable");
            c.Equal(1, f.Energy, "Bash costs 2");

            // Row 4: 8 x 0.75 x 1.5 = 9.
            var g = CheckKit.VsDummy();
            g.ApplyPower<WeakPower>(g.Player, 1, g.Enemy(0));
            g.ApplyPower<VulnerablePower>(g.Enemy(0), 1, g.Player);
            g.Play("Bash", 0);
            c.Equal(91, g.Enemy(0).Hp, "row 4: Bash while Weak into Vulnerable");
            c.Equal(3, g.Enemy(0).PowerAmount<VulnerablePower>(), "Vulnerable stacks by adding");

            // Row 30: Artifact 1 stops the Vulnerable, not the damage, and is used up.
            var h = CheckKit.VsDummy(new CheckDummy { OnAdded = (x, me) => x.ApplyPower<ArtifactPower>(me, 1, me) });
            c.Equal(1, h.Enemy(0).PowerAmount<ArtifactPower>(), "a starting power lands during set-up");
            h.Play("Bash", 0);
            c.Equal(92, h.Enemy(0).Hp, "row 30: the damage lands");
            c.That(!h.Enemy(0).HasPower<VulnerablePower>(), "row 30: no Vulnerable");
            c.That(!h.Enemy(0).HasPower<ArtifactPower>(), "row 30: Artifact is used up");
        });

        c.Group("R27 rows 11, 31, 32: multi-hit attacks truncate and meet Block per hit", () =>
        {
            // Row 11: each hit (5 + 2) x 1.5 = 10.5. Hit 1 into 12 Block leaves 2; hit 2: 2 blocked, 8.5 through, 8 HP.
            var f = CheckKit.VsDummy(new CheckDummy { Hp = 40 });
            var e = f.Enemy(0);
            f.ApplyPower<StrengthPower>(f.Player, 2, f.Player);
            f.ApplyPower<VulnerablePower>(e, 1, f.Player);
            f.SetBlock(e, 12);
            f.Put(TwoHits, PileType.Hand);
            f.Play("Two Hits", 0);
            c.Equal(0, e.Block, "row 11: Block is gone");
            c.Equal(32, e.Hp, "row 11: 40 HP down to 32");

            // Row 31: 5 x 0.75 x 1.5 = 5.625 per hit: the card reads 3 at rest, each hit does 5, total 10 (not 8).
            var g = CheckKit.VsDummy();
            g.ApplyPower<WeakPower>(g.Player, 1, g.Enemy(0));
            g.ApplyPower<VulnerablePower>(g.Enemy(0), 1, g.Player);
            var card = g.Put(TwoHits, PileType.Hand);
            c.Equal("Deal 3 damage twice.", Fight.CardText(g, card, PileType.Hand), "row 31: the hand text has the player's Weak and not the target's Vulnerable");
            g.Play("Two Hits", 0);
            c.Equal(90, g.Enemy(0).Hp, "row 31: 5 per hit, one truncation at the end of the chain");

            // Row 32: 11 per hit into Vulnerable is 16.5: 16 + 16 = 32, not 33.
            var h = CheckKit.VsDummy();
            h.ApplyPower<StrengthPower>(h.Player, 6, h.Player);
            h.ApplyPower<VulnerablePower>(h.Enemy(0), 1, h.Player);
            h.Put(TwoHits, PileType.Hand);
            h.Play("Two Hits", 0);
            c.Equal(68, h.Enemy(0).Hp, "row 32: each hit truncated on its own");

            // R27 of the turn rules: when the single target dies, the remaining hits are lost.
            var k = CheckKit.VsDummies(new CheckDummy { Hp = 5 }, new CheckDummy { Hp = 50 });
            k.Put(TwoHits, PileType.Hand);
            k.Play("Two Hits", 0);
            c.Equal(50, k.Enemy(1).Hp, "the second hit is not redirected to another enemy");
            c.That(!k.Enemies.Contains(k.Enemy(0)), "a dead monster leaves the enemy list at once");
        });

        c.Group("R1, R27 row 12: 13.5 into 13, 14 and 5 Block", () =>
        {
            foreach (var (block, blockLeft, hpLost) in new[] { (13, 0, 0), (14, 1, 0), (5, 0, 8) })
            {
                var f = CheckKit.VsDummy();
                var e = f.Enemy(0);
                f.SetBlock(e, block);
                var result = f.Damage(e, 13.5m, Props.Card, f.Player);
                c.Equal(blockLeft, e.Block, $"13.5 into {block} Block: Block left");
                c.Equal(hpLost, 100 - e.Hp, $"13.5 into {block} Block: HP lost");
                c.Equal(13, result.TotalDamage, $"13.5 into {block} Block: always 13 in total");
            }
        });

        c.Group("R27 rows 13-17, R22, R23: intents are the hit function against the player, per hit, before Block", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(new Nibbit { IsAlone = true }, hp: 44).Start();
            var nibbit = f.Enemy(0);
            c.Sequence(["attack 12"], f.IntentTexts(nibbit), "Nibbit alone opens with Butt");
            f.ApplyPower<StrengthPower>(nibbit, 2, nibbit);
            f.SetBlock(f.Player, 50);
            c.Sequence(["attack 14"], f.IntentTexts(nibbit), "Strength shows; the player's Block does not");
            c.Equal(14, f.IncomingDamage(), "incoming_damage is before Block");
            f.ApplyPower<VulnerablePower>(f.Player, 1, nibbit);
            c.Sequence(["attack 21"], f.IntentTexts(nibbit), "row 13: (12 + 2) x 1.5");
            f.ApplyPower<WeakPower>(nibbit, 1, f.Player);
            c.Sequence(["attack 15"], f.IntentTexts(nibbit), "row 15: 14 x 0.75 x 1.5 = 15.75");
            f.RemovePower<VulnerablePower>(f.Player);
            c.Sequence(["attack 10"], f.IntentTexts(nibbit), "row 14: 14 x 0.75 = 10.5");

            // Rows 16 and 17: a 2 x 3 attack. Vulnerable: 3 per hit, incoming 9. Weak: 1.5 -> 1 per hit, incoming 3 (not 4).
            var g = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("WHIRL", Damage: 2, Hits: 3)] });
            c.Sequence(["attack 2x3"], g.IntentTexts(g.Enemy(0)), "a multi-hit intent");
            g.ApplyPower<VulnerablePower>(g.Player, 1, g.Enemy(0));
            c.Sequence(["attack 3x3"], g.IntentTexts(g.Enemy(0)), "row 16");
            c.Equal(9, g.IncomingDamage(), "row 16: incoming");
            g.RemovePower<VulnerablePower>(g.Player);
            g.ApplyPower<WeakPower>(g.Enemy(0), 1, g.Player);
            c.Sequence(["attack 1x3"], g.IntentTexts(g.Enemy(0)), "row 17: truncated per hit");
            c.Equal(3, g.IncomingDamage(), "row 17: incoming");
            g.EndTurn();
            c.Equal(77, g.Player.Hp, "what lands is what was shown");
        });

        c.Group("R27 rows 18-21: Slippery", () =>
        {
            Fight Slippery(int block)
            {
                var f = CheckKit.VsDummy(new CheckDummy { Hp = 30, OnAdded = (x, me) => x.ApplyPower<SlipperyPower>(me, 1, me) });
                f.SetBlock(f.Enemy(0), block);
                return f;
            }

            // Row 18: Strike: 6 unblocked becomes 1; the stack is used.
            var a = Slippery(0);
            a.Play("Strike", 0);
            c.Equal(29, a.Enemy(0).Hp, "row 18: the loss is 1");
            c.That(!a.Enemy(0).HasPower<SlipperyPower>(), "row 18: Slippery is used up");

            // Row 19: two hits of 5: 1, then 5.
            var b = Slippery(0);
            b.Put(TwoHits, PileType.Hand);
            b.Play("Two Hits", 0);
            c.Equal(24, b.Enemy(0).Hp, "row 19: each hit is its own instance");

            // Row 20: 6 into 4 Block: 4 blocked, 2 through becomes 1.
            var d = Slippery(4);
            d.Play("Strike", 0);
            c.Equal(0, d.Enemy(0).Block, "row 20: Block 0");
            c.Equal(29, d.Enemy(0).Hp, "row 20: HP -1");
            c.That(!d.Enemy(0).HasPower<SlipperyPower>(), "row 20: stack used");

            // Row 21: 4.5 into 4 Block: 0.5 through is below 1, unchanged, truncates to 0; the stack is kept.
            var e = Slippery(4);
            e.ApplyPower<WeakPower>(e.Player, 1, e.Enemy(0));
            e.Play("Strike", 0);
            c.Equal(30, e.Enemy(0).Hp, "row 21: HP unchanged");
            c.Equal(1, e.Enemy(0).PowerAmount<SlipperyPower>(), "row 21: stack kept");

            // It also caps unpowered and unblockable loss (no "powered" test).
            var g = Slippery(0);
            g.Damage(g.Enemy(0), 20m, Props.NonCardHpLoss, g.Player);
            c.Equal(29, g.Enemy(0).Hp, "Slippery caps HP loss of any kind");
        });

        c.Group("R27 rows 22-25, R8: Block gain with Dexterity and Frail", () =>
        {
            int DefendWith(int dexterity, bool frail)
            {
                var f = CheckKit.VsDummy();
                if (dexterity != 0) f.ApplyPower<DexterityPower>(f.Player, dexterity, f.Player);
                if (frail) f.ApplyPower<FrailPower>(f.Player, 1, f.Enemy(0));
                f.Play("Defend");
                return f.Player.Block;
            }
            c.Equal(5, DefendWith(0, false), "Defend");
            c.Equal(3, DefendWith(0, true), "row 22: Frail, 3.75 -> 3");
            c.Equal(5, DefendWith(2, true), "row 23: (5 + 2) x 0.75 = 5.25");
            c.Equal(3, DefendWith(-1, true), "row 24: (5 - 1) x 0.75 = 3");
            c.Equal(6, DefendWith(1, false), "Dexterity 1");

            // The hand text shows the same number.
            var t = CheckKit.VsDummy();
            t.ApplyPower<DexterityPower>(t.Player, 2, t.Player);
            t.ApplyPower<FrailPower>(t.Player, 1, t.Enemy(0));
            c.Equal("Gain 5 Block.", Fight.CardText(t, t.Hand[t.HandIndex("Defend")], PileType.Hand), "the Block preview has Dexterity and Frail");

            // Row 25: a monster's move Block is cut by its own Frail. Power Block is not.
            var m = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("GUARD", Block: 5)] });
            m.ApplyPower<FrailPower>(m.Enemy(0), 2, m.Player);
            m.EndTurn();
            c.Equal(3, m.Enemy(0).Block, "row 25: monster Block under Frail");
            var p = CheckKit.VsDummy();
            p.ApplyPower<FrailPower>(p.Player, 1, p.Enemy(0));
            p.ApplyPower<DexterityPower>(p.Player, 3, p.Player);
            p.GainBlock(p.Player, 7m, Props.NonCardUnpowered);
            c.Equal(7, p.Player.Block, "Block from a power ignores Dexterity and Frail");
        });

        c.Group("R27 rows 26, 27, R19: HP loss that is not an attack", () =>
        {
            // Row 26: "lose 3 HP" from a card with 10 Block up and Vulnerable: HP -3, Block untouched.
            var f = CheckKit.VsDummy();
            f.SetBlock(f.Player, 10);
            f.ApplyPower<VulnerablePower>(f.Player, 2, f.Enemy(0));
            f.Damage(f.Player, 3m, Props.CardHpLoss, f.Player);
            c.Equal(77, f.Player.Hp, "row 26: HP -3");
            c.Equal(10, f.Player.Block, "row 26: Block stays");

            // Row 27: Infection at end of turn with 5 Block and Vulnerable: 3, blockable, not modified.
            var g = CheckKit.VsDummy();
            g.Put("Infection", PileType.Hand);
            g.SetBlock(g.Player, 5);
            g.ApplyPower<VulnerablePower>(g.Player, 2, g.Enemy(0));
            g.EndTurn();
            c.Equal(80, g.Player.Hp, "row 27: absorbed by Block");
            // Two of them with 5 Block: one instance per card, 3 + 3 - 5 = 1 HP.
            var h = CheckKit.VsDummy();
            h.Put("Infection", PileType.Hand);
            h.Put("Infection", PileType.Hand);
            h.SetBlock(h.Player, 5);
            h.EndTurn();
            c.Equal(79, h.Player.Hp, "each status card is its own damage instance against the Block that is left");
            c.Equal(2, h.DiscardPile.Count(x => x.Def.ClassName == "Infection"), "the Infections go to the discard pile");
        });

        c.Group("R15, R27 rows 35, 36: debuff durations and the skipped first tick", () =>
        {
            // Row 35: a monster puts Frail 2 on a player without Frail during enemy turn 1.
            var f = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("SPORES", Then: (x, me) => x.ApplyPower<FrailPower>(x.Player, 2, me)), new DummyStep("IDLE"), new DummyStep("IDLE2"), new DummyStep("IDLE3")] });
            f.EndTurn();
            c.Equal(2, f.Player.PowerAmount<FrailPower>(), "row 35: 2 on turn 2 (first tick skipped)");
            f.EndTurn();
            c.Equal(1, f.Player.PowerAmount<FrailPower>(), "row 35: 1 on turn 3");
            f.EndTurn();
            c.Equal(0, f.Player.PowerAmount<FrailPower>(), "row 35: gone on turn 4");

            // Row 36: the player still has Weak 1 (no skip pending); a monster adds 1: 2, then the normal tick: 1.
            var g = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("HEX", Then: (x, me) => x.ApplyPower<WeakPower>(x.Player, 1, me))] });
            g.ApplyPower<WeakPower>(g.Player, 1, g.Enemy(0));
            g.Player.GetPower<WeakPower>()!.SkipNextDurationTick = false;
            g.EndTurn();
            c.Equal(1, g.Player.PowerAmount<WeakPower>(), "row 36: adding to an instance does not skip");

            // The player puts 2 Vulnerable on a monster on turn N: 1 after enemy turn N, gone after N+1.
            var h = CheckKit.VsDummy();
            h.Play("Bash", 0);
            c.Equal(2, h.Enemy(0).PowerAmount<VulnerablePower>(), "Bash: 2");
            c.That(!h.Enemy(0).GetPower<VulnerablePower>()!.SkipNextDurationTick, "no skip flag on a monster's debuff");
            h.EndTurn();
            c.Equal(1, h.Enemy(0).PowerAmount<VulnerablePower>(), "1 on the next player turn");
            h.EndTurn();
            c.Equal(0, h.Enemy(0).PowerAmount<VulnerablePower>(), "gone after the second enemy turn");

            // R53: the flag is for any NEW debuff on the player's side, also one received on the player's own turn.
            var k = CheckKit.VsDummy();
            k.ApplyPower<VulnerablePower>(k.Player, 1, k.Player);
            c.That(k.Player.GetPower<VulnerablePower>()!.SkipNextDurationTick, "a new debuff on the player is flagged whoever applied it");
            k.EndTurn();
            c.Equal(1, k.Player.PowerAmount<VulnerablePower>(), "it skips its first tick");
            k.EndTurn();
            c.Equal(0, k.Player.PowerAmount<VulnerablePower>(), "and goes at the next");

            // R53: two monsters in one enemy turn: A applies 1 (new, flagged), B adds 2: 3 at the start of the next turn.
            var two = CheckKit.VsDummies(
                new CheckDummy { Steps = [new DummyStep("A", Then: (x, me) => x.ApplyPower<VulnerablePower>(x.Player, 1, me)), new DummyStep("REST")] },
                new CheckDummy { Steps = [new DummyStep("B", Then: (x, me) => x.ApplyPower<VulnerablePower>(x.Player, 2, me)), new DummyStep("REST")] });
            two.EndTurn();
            c.Equal(3, two.Player.PowerAmount<VulnerablePower>(), "stacks added before the first tick are kept in full");
        });

        c.Group("R41: a debuff a monster applies raises the hits of monsters that act later in the same turn", () =>
        {
            var f = CheckKit.VsDummies(
                new CheckDummy { Steps = [new DummyStep("ROAR", Then: (x, me) => x.ApplyPower<VulnerablePower>(x.Player, 2, me))] },
                new CheckDummy { Steps = [new DummyStep("HIT", Damage: 10)] });
            c.Equal(10, f.IncomingDamage(), "shown before the turn: 10");
            f.EndTurn();
            c.Equal(65, f.Player.Hp, "the second monster hits for 15, not 10");
        });

        c.Group("R10, R51: Strength stacking, removal at exactly 0, Artifact against negative Strength", () =>
        {
            var f = CheckKit.VsDummy();
            var me = f.Player;
            f.ApplyPower<StrengthPower>(me, 2, me);
            f.ApplyPower<StrengthPower>(me, -5, me);
            c.Equal(-3, me.PowerAmount<StrengthPower>(), "Strength may go negative");
            f.ApplyPower<StrengthPower>(me, 3, me);
            c.That(!me.HasPower<StrengthPower>(), "removed at exactly 0");

            var g = CheckKit.VsDummy();
            g.ApplyPower<ArtifactPower>(g.Enemy(0), 2, g.Enemy(0));
            g.ApplyPower<StrengthPower>(g.Enemy(0), -4, g.Player);
            c.That(!g.Enemy(0).HasPower<StrengthPower>(), "Artifact stops negative Strength");
            c.Equal(1, g.Enemy(0).PowerAmount<ArtifactPower>(), "and loses one stack");
            g.ApplyPower<StrengthPower>(g.Enemy(0), 3, g.Player);
            c.Equal(3, g.Enemy(0).PowerAmount<StrengthPower>(), "positive Strength is a buff: not stopped");
            g.ApplyPower<VulnerablePower>(g.Enemy(0), 2, g.Player);
            c.That(!g.Enemy(0).HasPower<VulnerablePower>() && !g.Enemy(0).HasPower<ArtifactPower>(), "the second stack stops a Vulnerable");
            g.ApplyPower<VulnerablePower>(g.Enemy(0), 2, g.Player);
            g.ApplyPower<ArtifactPower>(g.Enemy(0), 1, g.Enemy(0));
            g.EndTurn();
            c.Equal(1, g.Enemy(0).PowerAmount<VulnerablePower>(), "a tick-down is not a debuff application: Artifact lets it through");
            c.Equal(1, g.Enemy(0).PowerAmount<ArtifactPower>(), "and keeps its stack");
        });

        c.Group("R17: Shrink ends when its applier dies; Plating", () =>
        {
            var f = CheckKit.VsDummies(new CheckDummy { Hp = 6, DisplayName = "Beetle" }, new CheckDummy { Hp = 50 });
            f.ApplyPower<ShrinkPower>(f.Player, -1, f.Enemy(0));
            c.Equal(-1, f.Player.GetPower<ShrinkPower>()!.DisplayAmount, "the endless Shrink shows amount -1");
            c.Equal(PowerType.Debuff, f.Player.GetPower<ShrinkPower>()!.TypeForCurrentAmount, "and reads as a Debuff");
            f.EndTurn();
            c.Equal(-1, f.Player.PowerAmount<ShrinkPower>(), "a negative amount never ticks");
            f.Play("Strike", 1);
            c.Equal(46, f.Enemy(1).Hp, "6 x 0.7 = 4.2 -> 4");
            f.Play("Strike", 0);
            c.That(f.Enemy(0).IsAlive, "4 damage leaves the 6 HP beetle alive");
            f.Play("Strike", 0);
            c.That(!f.Player.HasPower<ShrinkPower>(), "killing the applier removes Shrink at once");

            var g = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("HIT", Damage: 10)] });
            g.ApplyPower<PlatingPower>(g.Player, 4, g.Player);
            g.EndTurn();
            c.Equal(74, g.Player.Hp, "Plating's 4 Block arrives before the enemy attacks");
            c.Equal(3, g.Player.PowerAmount<PlatingPower>(), "and it goes down by 1 at the start of the next turn");
        });
    }

    /// <summary>Damage one Strike does to a 100 HP dummy after <paramref name="setup"/>.</summary>
    private static int StrikeFor(Action<Fight> setup)
    {
        var f = CheckKit.VsDummy();
        setup(f);
        f.Play("Strike", 0);
        return 100 - f.Enemy(0).Hp;
    }
}
