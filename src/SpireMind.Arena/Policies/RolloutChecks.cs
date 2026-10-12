using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The Rollout player on positions built by hand, each with an answer worked out on paper
/// before it was run: a Power goes down on a quiet turn and waits on a dangerous one, the card
/// that doubles a Block card is played in front of the one Block card when a big hit is
/// coming, a race that must start a turn early is started, a potion waits for the turn that
/// needs it, and none of it leans on what a player cannot know.
/// <para>
/// Most positions use a deck of five cards, all of them in the hand: the next hands are then
/// the same cards whatever the shuffle, the enemy is a dummy with a fixed cycle, and every
/// number below can be checked by hand.
/// </para>
/// </summary>
public sealed class RolloutChecks : IArenaChecks
{
    private static readonly PotionDef Guard = new("CheckRolloutGuard", "Guard Flask", PotionRarity.Common, TargetType.Self)
    {
        Text = _ => "Gain 12 Block.",
        Use = p => p.F.GainBlock(p.Owner, 12, Props.NonCardUnpowered),
    };

    private static readonly CardDef Gamble = new("CheckRolloutGamble", "Gamble", CardType.Skill, CardRarity.Common, 1, TargetType.Self)
    {
        Text = _ => "Draw 1 card.",
        Play = c => c.Draw(1),
    };

    private static readonly CardDef Finisher = new("CheckRolloutFinisher", "Finisher", CardType.Attack, CardRarity.Rare, 0, TargetType.AnyEnemy)
    {
        Vars = { ["Damage"] = 60 },
        Text = t => $"Deal {t.Damage()} damage.",
        Play = c => c.Attack(c.V("Damage")).Execute(),
    };

    private static readonly CardDef Scatter = new("CheckRolloutScatter", "Scatter", CardType.Attack, CardRarity.Common, 1, TargetType.RandomEnemy)
    {
        Vars = { ["Damage"] = 10 },
        Text = t => $"Deal {t.Damage()} damage to a random enemy.",
        Play = c => c.Attack(c.V("Damage")).Execute(),
    };

    /// <summary>The settings a position is checked with: fast and normal always, strong where the
    /// fight is short enough that its play-outs cost a second and not ten.</summary>
    private static IEnumerable<RolloutPolicy> Players(bool strong = false) =>
        RolloutSetting.All.Where(s => strong || s != RolloutSetting.Strong).Select(s => new RolloutPolicy(s));

    /// <summary>A cheap setting for whole generated fights, where only "it finishes, and the same way twice" is asked.</summary>
    private static readonly RolloutSetting Small = new("check", Playouts: 8, Lines: 4, InnerBudget: 20);

    public void Run(Check c)
    {
        Listed(c);
        PowerOnAQuietTurn(c);
        BarricadeBeforeTheBigHit(c);
        BlockBeforeThePower(c);
        UnmovableThenTheOneBlockCard(c);
        TheRaceThatStartsATurnEarly(c);
        ThePotionWaits(c);
        ThePotionWaitsForTheBiggerHit(c);
        NothingHidden(c);
        TheEnemysCoinIsNotSeen(c);
        TheNextHandIsNotSeen(c);
        Bounded(c);
        WholeFights(c);
    }

    // ---------- driving a player ----------

    /// <summary>Lets <paramref name="player"/> play until it ends the turn or the fight is over; the labels of what it did.</summary>
    private static List<string> Turn(Fight f, IEnginePolicy player, int most = 40)
    {
        var labels = new List<string>();
        for (var n = 0; n < most && !f.IsOver; n++)
        {
            var snapshot = f.Snapshot();
            var id = player.Choose(snapshot, f);
            var action = snapshot.Actions.FirstOrDefault(a => a.Id == id)
                         ?? throw new InvalidOperationException($"{player.Name} chose '{id}', which is not offered");
            labels.Add(action.Label);
            f.Do(id);
            if (id == "end_turn") break;
        }
        return labels;
    }

    private static void Finish(Fight f, IEnginePolicy player)
    {
        for (var n = 0; n < 200 && !f.IsOver; n++) Turn(f, player);
    }

    /// <summary>A fight whose whole deck is the opening hand, against one dummy.</summary>
    private static Fight FiveCards(string[] deck, int hp, CheckDummy dummy) =>
        new FightBuilder().DrawOrder(deck).Hp(hp).Enemy(dummy).Start();

    private static DummyStep Wait(string id) => new(id);

    private static DummyStep Hit(string id, int damage) => new(id, Damage: damage);

    // ---------- the list ----------

    private static void Listed(Check c)
    {
        c.Group("Rollout plays only where it is asked for by name", () =>
        {
            c.That(Policies.All.All(p => p is not RolloutPolicy), "it is not one of Policies.All, so baseline and selfcheck play the three old players only");
            c.Equal("Rollout", Policies.ByName("rollout")?.Name, "by name, whatever the case");
            c.Equal("Rollout", Policies.ByName("Rollout:normal")?.Name, "Rollout is the normal setting");
            c.Equal("Rollout:fast", Policies.ByName("Rollout:fast")?.Name, "the fast setting");
            c.Equal("Rollout:strong", Policies.ByName("Rollout:strong")?.Name, "the strong setting");
            c.Equal("Lookahead", Policies.ByName("lookahead")?.Name, "the old players by name");
            c.That(Policies.ByName("Rollout:") is null && Policies.ByName("Rollout:huge") is null && Policies.ByName("Nobody") is null, "a name nobody has");
            c.That(Policies.ByName("Rollout") is IEnginePolicy, "it gets the fight to copy");
            c.That(RolloutSetting.Fast.Playouts < RolloutSetting.Normal.Playouts && RolloutSetting.Normal.Playouts < RolloutSetting.Strong.Playouts,
                "fast, normal, strong: each may play more fights out than the one before");
        });
    }

    // ---------- (a) a quiet enemy turn: the Power is played ----------

    private static void PowerOnAQuietTurn(Check c)
    {
        c.Group("Rollout: on a quiet turn Inflame goes down first", () =>
        {
            // Deck: Inflame, Strike, Strike, Defend, Defend; 40 HP. Enemy: 48 HP, idle on turn 1, then 15
            // on three turns of every four. A turn is two Strikes and a Defend (5 Block) or two Defends
            // (10 Block) and a Strike.
            // Inflame now: 8 + 8 = 16 (32 left). Then two Strikes and a Defend (16 left, 10 through) and
            // two Strikes that kill; or twice two Defends and a Strike (5 through each) and the kill a turn
            // later. Either way 10 HP lost: 30 HP, and Burning Blood's 6: 36.
            // No Inflame on turn 1 (12 damage, 36 left): the least that gets through before 36 more damage
            // is dealt is 15, with Inflame on a later turn or without it: 31 at best.
            foreach (var player in Players())
            {
                var f = FiveCards(["Inflame", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad"], 40,
                    new CheckDummy { Hp = 48, Steps = [Wait("WAIT"), Hit("HIT_A", 15), Hit("HIT_B", 15), Hit("HIT_C", 15)] });
                var turn = Turn(f, player);
                c.Sequence(["Play Inflame", "Play Strike on Dummy", "Play Strike on Dummy", "End turn"], turn, $"{player.Name}: turn 1");
                Finish(f, player);
                c.That(f.Won, $"{player.Name}: the fight is won");
                c.Equal(36, f.Player.Hp, $"{player.Name}: with 30 HP and Burning Blood's 6");
            }
        });

        c.Group("Rollout: on a quiet turn Demon Form goes down", () =>
        {
            // Hand: Demon Form, Strike, Strike, Defend, Defend, and the other five starter cards to
            // draw. Enemy: 100 HP, idle now, then 12 on three turns of every four. Three energy are
            // Demon Form or 12 damage. Without Demon Form the deck deals 12 to 18 a turn: about six
            // more turns. With it the Strikes hit for 8, 10, 12, 14... from turn 2 on: 16 + 20 + 24 +
            // 28 with two Strikes a turn, dead around turn 5 or 6. Each turn saved is some 7 HP kept.
            foreach (var player in Players())
            {
                var f = CheckKit.VsDummy(new CheckDummy { Hp = 100, Steps = [Wait("WAIT"), Hit("HIT_A", 12), Hit("HIT_B", 12), Hit("HIT_C", 12)] });
                f.Clear(PileType.Hand);
                foreach (var card in new[] { "DemonForm", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad" }) f.Put(card, PileType.Hand);
                c.Sequence(["Play Demon Form", "End turn"], Turn(f, player), $"{player.Name}: turn 1");
            }
        });
    }

    private static void BarricadeBeforeTheBigHit(Check c)
    {
        Fight Make() => FiveCards(["Barricade", "DefendIronclad", "DefendIronclad", "StrikeIronclad", "StrikeIronclad"], 15,
            new CheckDummy { Hp = 40, Steps = [Wait("WAIT_A"), Wait("WAIT_B"), Hit("HIT", 30)] });

        c.Group("Rollout: two quiet turns before a 30: Barricade now is the only way to be alive after it", () =>
        {
            // Deck: Barricade, Defend, Defend, Strike, Strike; 15 HP. Enemy: 40 HP, idle on turns 1 and 2,
            // 30 on turn 3, and so on round.
            // Barricade now. Turn 2: Defend, Defend, Strike: 10 Block that stays, 6 damage. Turn 3: the
            // same: 20 Block, 10 through, 5 HP, 12 damage. Turns 4 and 5: two Strikes and a Defend: 36 damage.
            // Turn 6: a Strike kills before it hits again. 5 HP and Burning Blood's 6: 11.
            // No Barricade by turn 2: at most 36 damage before the 30 lands (three turns of two Strikes), 10
            // Block against it, 20 through. Dead. Barricade on turn 2: 12 damage, and the same 10 Block. Dead.
            // The one-turn search has no rule for Barricade (2.4 points here) and takes 12 damage instead.
            var search = new LookaheadPolicy();
            var without = Make();
            c.That(!Turn(without, search).Contains("Play Barricade"), "the one-turn search leaves Barricade in the hand on turn 1");
            for (var n = 0; n < 10 && !without.IsOver; n++) Turn(without, search);
            c.That(without.IsOver && !without.Won, "and is dead after the 30");

            foreach (var player in Players(strong: true))
            {
                var f = Make();
                c.Sequence(["Play Barricade", "End turn"], Turn(f, player), $"{player.Name}: turn 1");
                Finish(f, player);
                c.That(f.Won, $"{player.Name}: the fight is won");
                c.Equal(11, f.Player.Hp, $"{player.Name}: with 5 HP and Burning Blood's 6");
            }
        });
    }

    // ---------- (b) the same Power on a dangerous turn: Block first ----------

    private static void BlockBeforeThePower(Check c)
    {
        c.Group("Rollout: with a big hit coming it blocks, and plays Demon Form on the quiet turn after", () =>
        {
            // Deck: Demon Form, Defend, Defend, Strike, Strike; 25 HP. Enemy: 60 HP, hits for 20 on
            // turns 1, 5, 9 and idles in between.
            // Block first: Defend, Defend, Strike (6): 10 through, 15 HP. Turn 2: Demon Form. Turns 3, 4, 5:
            // two Strikes at 8, 10, 12 each: 16 + 20 + 24, with the 6 that is 66: dead on turn 5 before it
            // hits. 15 HP and 6: 21.
            // Demon Form first: 20 through, 5 HP. Two Strikes at 8, 10, 12 on turns 2, 3, 4: 60: dead on turn
            // 4. 5 HP and 6: 11. No Demon Form at all: at best Strike, Strike, Defend on turn 1 (15 through,
            // 10 HP), then two Strikes a turn: 60 on turn 5 before it hits: 10 HP and 6: 16. With both
            // Defends on turn 1 it is 6 + 12 + 12 + 12 = 42 by turn 4 and the turn 5 hit costs 10 more:
            // 5 HP and 6: 11.
            foreach (var player in Players())
            {
                var f = FiveCards(["DemonForm", "DefendIronclad", "DefendIronclad", "StrikeIronclad", "StrikeIronclad"], 25,
                    new CheckDummy { Hp = 60, Steps = [Hit("HIT", 20), Wait("WAIT_A"), Wait("WAIT_B"), Wait("WAIT_C")] });
                var first = Turn(f, player);
                c.Sequence(["End turn", "Play Defend", "Play Defend", "Play Strike on Dummy"], first.Order(StringComparer.Ordinal), $"{player.Name}: turn 1, 20 coming at 25 HP");
                c.Equal(15, f.Player.Hp, $"{player.Name}: 10 of the 20 blocked");
                c.Sequence(["Play Demon Form", "End turn"], Turn(f, player), $"{player.Name}: turn 2, nothing coming");
                Finish(f, player);
                c.That(f.Won, $"{player.Name}: the fight is won");
                c.Equal(5, f.Turn, $"{player.Name}: on turn 5, before the second hit");
                c.Equal(21, f.Player.Hp, $"{player.Name}: with 15 HP and Burning Blood's 6");
            }
        });

        c.Group("Rollout: a hit that kills is blocked whatever else is in the hand", () =>
        {
            // 22 coming at 15 HP: two Defends leave 3 HP. Demon Form takes all three energy and the fight with them.
            foreach (var player in Players())
            {
                var f = CheckKit.VsDummy(new CheckDummy { Hp = 100, Steps = [Hit("HIT", 22), Wait("WAIT")] });
                f.Clear(PileType.Hand);
                foreach (var card in new[] { "DemonForm", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad" }) f.Put(card, PileType.Hand);
                f.SetHp(f.Player, 15);
                var turn = Turn(f, player);
                c.Equal(2, turn.Count(label => label == "Play Defend"), $"{player.Name}: both Defends ({string.Join("; ", turn)})");
                c.That(!turn.Contains("Play Demon Form"), $"{player.Name}: no Demon Form");
                c.That(!f.IsOver, $"{player.Name}: it lives");
                c.Equal(3, f.Player.Hp, $"{player.Name}: on 3 HP");
            }
        });
    }

    // ---------- (c) Unmovable in front of the one Block card ----------

    private static void UnmovableThenTheOneBlockCard(Check c)
    {
        c.Group("Rollout: a big hit and one Block card: Unmovable first, then the Defend", () =>
        {
            // Deck: Unmovable, Defend, Strike, Strike, Strike; 20 HP. Enemy: 48 HP, 14 every turn.
            // Unmovable, then Defend: 10 Block, 4 through. Turns 2, 3, 4: Defend for 10 and two Strikes:
            // 4 through each, 36 damage. Turn 5: two Strikes kill. 16 lost: 4 HP and 6: 10.
            // Defend first and Unmovable after it: 5 Block, 9 through, and the same later turns: 21 lost. Dead.
            // No Unmovable: the least that gets through before 48 damage is dealt is 23 (Defend and two
            // Strikes, then three Strikes twice). Dead. Unmovable a turn later: 9 + 4 + 4 + 4 = 21. Dead.
            foreach (var player in Players(strong: true))
            {
                var f = FiveCards(["Unmovable", "DefendIronclad", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad"], 20,
                    new CheckDummy { Hp = 48, Steps = [Hit("HIT", 14)] });
                c.Sequence(["Play Unmovable", "Play Defend", "End turn"], Turn(f, player), $"{player.Name}: turn 1");
                c.Equal(16, f.Player.Hp, $"{player.Name}: 10 of the 14 blocked");
                Finish(f, player);
                c.That(f.Won, $"{player.Name}: the fight is won");
                c.Equal(5, f.Turn, $"{player.Name}: on turn 5");
                c.Equal(10, f.Player.Hp, $"{player.Name}: with 4 HP and Burning Blood's 6");
            }
        });
    }

    // ---------- (d) a kill over two turns that one turn's search cannot see ----------

    private static void TheRaceThatStartsATurnEarly(Check c)
    {
        Fight Make() => new FightBuilder()
            .DrawOrder("StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad",
                "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad")
            .Enemy(new CheckDummy { Hp = 36, Steps = [Hit("JAB", 10), Hit("EXECUTE", 99)] }).Start();

        c.Group("Rollout: three Strikes now and three next turn kill before the 99 lands; blocking the 10 loses the fight", () =>
        {
            // Hand: Strike x3, Defend x2; the five cards left to draw are the same, so turn 2's hand is
            // known. Enemy: 36 HP, 10 now and 99 on turn 2. 18 + 18 = 36: dead on turn 2, 10 through:
            // 70 HP and 6: 76. Any Defend on turn 1 leaves at least 6 of its HP standing when the 99 lands.
            // The one-turn search sees 10 coming and blocks: 12 damage on this enemy is worth less to it than 10 HP.
            var search = new LookaheadPolicy();
            var blocked = Make();
            var first = Turn(blocked, search);
            c.That(first.Contains("Play Defend"), $"the one-turn search blocks on turn 1 ({string.Join("; ", first)})");
            for (var n = 0; n < 10 && !blocked.IsOver; n++) Turn(blocked, search);
            c.That(blocked.IsOver && !blocked.Won, "and loses the fight on turn 2");

            foreach (var player in Players(strong: true))
            {
                var f = Make();
                c.Sequence(["End turn", "Play Strike on Dummy", "Play Strike on Dummy", "Play Strike on Dummy"],
                    Turn(f, player).Order(StringComparer.Ordinal), $"{player.Name}: turn 1");
                Finish(f, player);
                c.That(f.Won, $"{player.Name}: the fight is won");
                c.Equal(2, f.Turn, $"{player.Name}: on turn 2");
                c.Equal(76, f.Player.Hp, $"{player.Name}: with 70 HP and Burning Blood's 6");
            }
        });
    }

    // ---------- (e) a potion kept on a harmless turn and used on the turn it decides the fight ----------

    private static void ThePotionWaits(Check c)
    {
        c.Group("Rollout: the Guard Flask stays in the belt on the idle turn and is drunk under the hit that would kill", () =>
        {
            // Starter deck in the kit's order, 15 HP, a flask of 12 Block. Enemy: 60 HP, idle on turn 1,
            // 26 on turn 2, idle for six turns after that. It cannot be dead by turn 2 (Bash and a Strike,
            // then three Strikes on a Vulnerable enemy: 17 + 27 = 44).
            // Turn 2's hand is Strike x3, Defend x2: 10 Block, 16 through, dead. With the flask: 22 Block,
            // 4 through. So the flask on turn 1 is the fight thrown away, and on turn 2 it is the fight.
            foreach (var player in Players())
            {
                var f = CheckKit.VsDummy(new CheckDummy
                {
                    Hp = 60,
                    Steps = [Wait("WAIT_A"), Hit("HIT", 26), Wait("WAIT_B"), Wait("WAIT_C"), Wait("WAIT_D"), Wait("WAIT_E"), Wait("WAIT_F"), Wait("WAIT_G")],
                });
                f.SetHp(f.Player, 15);
                f.SetPotion(0, Guard);

                var quiet = Turn(f, player);
                c.That(quiet.All(label => !label.Contains("Flask", StringComparison.Ordinal)), $"{player.Name}: turn 1, nothing coming: no flask ({string.Join("; ", quiet)})");
                c.That(f.Potions[0] is not null, $"{player.Name}: it is still in the belt");

                var hit = Turn(f, player);
                c.That(hit.Contains("Use Guard Flask"), $"{player.Name}: turn 2, 26 coming at 15 HP: the flask ({string.Join("; ", hit)})");
                c.That(!f.IsOver, $"{player.Name}: it lives");
                c.Equal(11, f.Player.Hp, $"{player.Name}: 22 of the 26 blocked");
                Finish(f, player);
                c.That(f.Won, $"{player.Name}: and wins");
            }
        });
    }

    private static void ThePotionWaitsForTheBiggerHit(Check c)
    {
        Fight Make()
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Room(RoomType.Elite).Enemy(new CheckDummy
            {
                Hp = 60,
                Steps = [Hit("HIT_A", 20), Hit("HIT_B", 40), Wait("WAIT_A"), Wait("WAIT_B"), Wait("WAIT_C"), Wait("WAIT_D"), Wait("WAIT_E"), Wait("WAIT_F")],
            }).Start();
            f.SetHp(f.Player, 30);
            f.SetPotion(0, Guard);
            return f;
        }

        c.Group("Rollout: an elite hits for 20 and then for 40: the flask that would save 10 HP now is kept for the hit it must stop", () =>
        {
            // Starter deck in the kit's order, 30 HP, a flask of 12 Block. Enemy: 60 HP, 20 on turn 1, 40
            // on turn 2, then idle for six turns. Each hand has two Defends: 10 Block.
            // Flask on turn 1: nothing through, 30 HP; turn 2: 30 through. Dead.
            // Flask on turn 2: 10 through on turn 1 (both Defends), 20 HP; turn 2: 22 Block, 18 through, 2 HP.
            // Six idle turns are more than the 60 HP take. 2 HP and Burning Blood's 6: 8.
            // The one-turn search drinks it on turn 1: against an elite a potion that saves 10 HP at 30 HP is "worth a lot".
            var search = new LookaheadPolicy();
            var early = Make();
            c.That(Turn(early, search).Contains("Use Guard Flask"), "the one-turn search drinks the flask on turn 1");
            for (var n = 0; n < 10 && !early.IsOver; n++) Turn(early, search);
            c.That(early.IsOver && !early.Won, "and is dead after the 40");

            foreach (var player in Players())
            {
                var f = Make();
                var first = Turn(f, player);
                c.That(first.All(label => !label.Contains("Flask", StringComparison.Ordinal)), $"{player.Name}: turn 1: no flask ({string.Join("; ", first)})");
                c.Equal(20, f.Player.Hp, $"{player.Name}: both Defends, 10 through");
                var second = Turn(f, player);
                c.That(second.Contains("Use Guard Flask"), $"{player.Name}: turn 2: the flask ({string.Join("; ", second)})");
                c.Equal(2, f.Player.Hp, $"{player.Name}: 22 of the 40 blocked");
                Finish(f, player);
                c.That(f.Won, $"{player.Name}: the fight is won");
                c.Equal(8, f.Player.Hp, $"{player.Name}: with 2 HP and Burning Blood's 6");
            }
        });
    }

    // ---------- (f) what it cannot see ----------

    private static void NothingHidden(Check c)
    {
        c.Group("Rollout: the real draw order does not reach its choice", () =>
        {
            // As for Lookahead: one energy, Gamble (draw 1) or Defend. In fight A the card on top really
            // is the Finisher, which wins at once; in fight B it is at the bottom. Both show the same.
            var a = new FightBuilder()
                .DrawOrder("DefendIronclad", "DefendIronclad", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound")
                .Enemy(new CheckDummy { Hp = 50, Steps = [Hit("HIT", 12)] }).Start();
            a.Put(Gamble, PileType.Hand);
            a.Put(Finisher, PileType.Draw, position: PilePosition.Top);
            a.SetEnergy(1);
            a.SetHp(a.Player, 30);
            var b = a.Clone();
            b.ReverseDrawPile();

            c.Equal(a.Seen(), b.Seen(), "the two fights show the same snapshot");
            c.That(a.Clone().Play("Gamble").Play("Finisher", 0).Won, "in A, Gamble really draws the Finisher and wins");
            c.That(b.Clone().Play("Gamble").HandTitles().All(title => title != "Finisher"), "in B, Gamble really draws a Wound");

            foreach (var (setting, seeds) in new[] { (RolloutSetting.Fast, 4), (RolloutSetting.Normal, 1) })
            {
                for (var seed = 1; seed <= seeds; seed++)
                {
                    // A new player for each fight: nothing it remembers of A can tell it about B.
                    var inA = new RolloutPolicy(setting, seed).Choose(a.Snapshot(), a);
                    c.Equal(inA, new RolloutPolicy(setting, seed).Choose(b.Snapshot(), b), $"{setting.Name}, seed {seed}: the same choice in both fights");
                    var both = new RolloutPolicy(setting, seed);
                    c.Equal(inA, both.Choose(a.Snapshot(), a), $"{setting.Name}, seed {seed}: the same again in A");
                    c.Equal(inA, both.Choose(b.Snapshot(), b), $"{setting.Name}, seed {seed}: and in B from the player that has just seen A");
                }
            }
        });

        c.Group("Rollout: the random numbers to come do not reach its choice", () =>
        {
            // Scatter hits one of two enemies at random; only a hit on the Killer (10 HP) stops its 12 damage.
            Fight Make(int seed)
            {
                var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder)
                    .Enemy(new CheckDummy { Hp = 10, DisplayName = "Killer", Steps = [Hit("HIT", 12)] })
                    .Enemy(new CheckDummy { Hp = 100, DisplayName = "Bystander" }).Seed(seed).Start();
                f.Clear(PileType.Hand);
                f.Put(Scatter, PileType.Hand);
                f.Put("DefendIronclad", PileType.Hand);
                f.Put("StrikeIronclad", PileType.Hand);
                f.SetEnergy(1);
                f.SetHp(f.Player, 30);
                return f;
            }
            bool Kills(int seed) => Make(seed).Play("Scatter").Enemy(0).IsDead;

            var hit = Enumerable.Range(1, 40).FirstOrDefault(Kills);
            var miss = Enumerable.Range(1, 40).FirstOrDefault(seed => !Kills(seed));
            c.That(hit > 0 && miss > 0, "one seed's Scatter really kills the Killer and another's really misses it");
            if (hit == 0 || miss == 0) return;

            var a = Make(hit);
            var b = Make(miss);
            c.Equal(a.Seen(), b.Seen(), "the two fights show the same snapshot");
            for (var seed = 1; seed <= 4; seed++)
                c.Equal(new RolloutPolicy(RolloutSetting.Fast, seed).Choose(a.Snapshot(), a), new RolloutPolicy(RolloutSetting.Fast, seed).Choose(b.Snapshot(), b),
                    $"seed {seed}: the same choice in both fights");
        });

        c.Group("Rollout: leaves the running fight alone", () =>
        {
            var f = CheckKit.VsDummies(new CheckDummy { Hp = 60, DisplayName = "Left", Steps = [Hit("HIT", 9)] }, new CheckDummy { Hp = 50, DisplayName = "Right", Steps = [Hit("HIT", 7)] });
            f.Put(Gamble, PileType.Hand);
            var seen = f.Seen();
            var rolls = f.Rng.Shuffle.Counter + f.Rng.CombatTargets.Counter + f.Rng.MonsterAi.Counter;
            var order = f.DrawPile.Select(card => card.Id).ToList();
            var player = new RolloutPolicy(RolloutSetting.Fast);
            var first = player.Choose(f.Snapshot(), f);
            c.Equal(seen, f.Seen(), "the snapshot is unchanged");
            c.Equal(rolls, f.Rng.Shuffle.Counter + f.Rng.CombatTargets.Counter + f.Rng.MonsterAi.Counter, "no random number was used");
            c.Sequence(order, f.DrawPile.Select(card => card.Id), "the draw pile is as it was");
            c.Equal(first, player.Choose(f.Snapshot(), f), "the same answer again");
            c.Equal(first, new RolloutPolicy(RolloutSetting.Fast).Choose(f.Snapshot(), f), "and from another instance");

            var over = CheckKit.VsDummy(new CheckDummy { Steps = [Hit("HIT", 200)] });
            over.EndTurn();
            c.Equal(Fight.DoneAction, player.Choose(over.Snapshot(), over), "after the fight");
        });
    }

    /// <summary>
    /// A check monster with one random step: it jabs for 12, then a fair coin picks between a
    /// hit for 30 and a rest, and after that it rests for good. The coin is tossed at the start
    /// of turn 2, on the fight's own monster stream.
    /// </summary>
    private sealed class CheckCoinFlipper : Monster
    {
        public const string BigId = "BIG", RestId = "REST";

        public override string Name => "Coin Flipper";
        public override int MinHp => 60;
        public override int MaxHp => 60;

        protected override MonsterState BuildMachine(MachineBuilder m)
        {
            var jab = m.Move("JAB", f => Attack(f, 12), Intent.Attack(12));
            var big = m.Move(BigId, f => Attack(f, 30), Intent.Attack(30));
            var rest = m.Move(RestId, _ => { }, Intent.Sleep);
            var idle = m.Move("IDLE", _ => { }, Intent.Sleep);
            jab.FollowUp = m.Random("COIN").Add(big).Add(rest);
            big.FollowUp = idle;
            rest.FollowUp = idle;
            idle.FollowUp = idle;
            return jab;
        }
    }

    /// <summary>What the player does until it ends the turn, on a copy: the labels in order.</summary>
    private static string FirstTurn(Fight f, IEnginePolicy player) => string.Join("; ", Turn(f.Clone(), player));

    private static void TheEnemysCoinIsNotSeen(Check c)
    {
        c.Group("Rollout: the enemy's coin for next turn does not reach its choice", () =>
        {
            // Kit hand (Strike, Strike, Defend, Defend, Bash), 20 HP, a flask of 12 Block. The enemy jabs
            // for 12 now; next turn a coin decides between a hit for 30 and a rest; then it rests for good.
            // If the 30 comes: only the flask on turn 2 (22 Block with two Defends) keeps the player alive.
            // If the rest comes: the flask is best drunk now (nothing through instead of 2, and two attacks more).
            // A player that could see the coin would drink it now in the fight where the rest is coming,
            // and keep it in the other. The two fights show the same snapshot, so an honest player plays
            // both the same way, and with half the copies holding the 30 it keeps the flask.
            Fight Make(int seed)
            {
                var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(new CheckCoinFlipper(), hp: 60).Seed(seed).Start();
                f.SetHp(f.Player, 20);
                f.SetPotion(0, Guard);
                return f;
            }
            string Coin(int seed)
            {
                var f = Make(seed);
                f.EndTurn();
                return f.Enemy(0).Monster!.NextMove!.Id;
            }

            var big = Enumerable.Range(1, 40).FirstOrDefault(seed => Coin(seed) == CheckCoinFlipper.BigId);
            var rest = Enumerable.Range(1, 40).FirstOrDefault(seed => Coin(seed) == CheckCoinFlipper.RestId);
            c.That(big > 0 && rest > 0, "one seed's coin really shows the 30 and another's the rest");
            if (big == 0 || rest == 0) return;

            var a = Make(big);
            var b = Make(rest);
            c.Equal(a.Seen(), b.Seen(), "the two fights show the same snapshot");
            foreach (var setting in new[] { RolloutSetting.Fast, RolloutSetting.Normal })
            {
                for (var seed = 1; seed <= 3; seed++)
                {
                    var inA = FirstTurn(a, new RolloutPolicy(setting, seed));
                    var inB = FirstTurn(b, new RolloutPolicy(setting, seed));
                    c.Equal(inA, inB, $"{setting.Name}, seed {seed}: the same turn in both fights");
                    c.That(!inA.Contains("Flask", StringComparison.Ordinal), $"{setting.Name}, seed {seed}: the flask is kept for the 30 that may come ({inA})");
                }
            }

            // And the whole fight where the 30 really comes is won with the flask on turn 2.
            var player = new RolloutPolicy(RolloutSetting.Fast);
            Turn(a, player);
            var second = Turn(a, player);
            c.That(second.Contains("Use Guard Flask"), $"where the 30 comes, the flask is drunk under it ({string.Join("; ", second)})");
            Finish(a, player);
            c.That(a.Won, "and the fight is won");
        });
    }

    private static void TheNextHandIsNotSeen(Check c)
    {
        c.Group("Rollout: the hand it will really draw next turn does not reach its choice", () =>
        {
            // Kit hand, 20 HP, a flask of 12 Block, and ten cards to draw: five Defends and five Strikes.
            // The enemy hits for 12 now and for 20 next turn, then idles. In fight A the five Defends
            // really are on top: next turn blocks 15 of the 20, so the flask is best drunk now (nothing
            // through now, 15 HP after the 20; kept, 2 come through now). In fight B the five Strikes
            // are: next turn has no Block, and the 20 kills a player at 20 HP who has drunk the flask.
            // Both show the same snapshot. Whatever an honest player does, it does the same in both.
            // (A build of the player that copies the fight exactly, and so sees the order, was tried:
            // it drinks the flask in A and keeps it in B, and this check fails.)
            var a = new FightBuilder()
                .DrawOrder("StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad", "Bash",
                    "DefendIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad",
                    "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad")
                .Enemy(new CheckDummy
                {
                    Hp = 70,
                    Steps = [Hit("JAB", 12), Hit("HIT", 20), Wait("W1"), Wait("W2"), Wait("W3"), Wait("W4"), Wait("W5"), Wait("W6"), Wait("W7"), Wait("W8")],
                }).Start();
            a.SetHp(a.Player, 20);
            a.SetPotion(0, Guard);
            var b = a.Clone();
            b.ReverseDrawPile();

            c.Equal(a.Seen(), b.Seen(), "the two fights show the same snapshot");
            var nextA = a.Clone();
            nextA.EndTurn();
            var nextB = b.Clone();
            nextB.EndTurn();
            c.That(nextA.HandTitles().All(title => title == "Defend"), "in A the next hand really is five Defends");
            c.That(nextB.HandTitles().All(title => title == "Strike"), "in B the next hand really is five Strikes");

            foreach (var setting in new[] { RolloutSetting.Fast, RolloutSetting.Normal })
                for (var seed = 1; seed <= 3; seed++)
                    c.Equal(FirstTurn(a, new RolloutPolicy(setting, seed)), FirstTurn(b, new RolloutPolicy(setting, seed)), $"{setting.Name}, seed {seed}: the same turn in both fights");

            // The same from the inside of a card choice: Headbutt asks which discarded card goes on top
            // of the draw pile, with the fight's real order still underneath. (The build that sees the
            // order picks one card in A and the other in B here too.)
            var pickA = a.Clone();
            pickA.Put("Headbutt", PileType.Hand);
            pickA.Put("DefendIronclad", PileType.Discard);
            pickA.Put("StrikeIronclad", PileType.Discard);
            var pickB = pickA.Clone();
            pickB.ReverseDrawPile();
            var headbutt = pickA.LegalActions().First(action => action.Label.StartsWith("Play Headbutt", StringComparison.Ordinal)).Id;
            pickA.Do(headbutt);
            pickB.Do(headbutt);
            c.That(pickA.HasPendingChoice && pickB.HasPendingChoice, "both fights are in Headbutt's card choice");
            c.Equal(pickA.Seen(), pickB.Seen(), "and show the same choice");
            for (var seed = 1; seed <= 3; seed++)
                c.Equal(new RolloutPolicy(RolloutSetting.Fast, seed).Choose(pickA.Snapshot(), pickA), new RolloutPolicy(RolloutSetting.Fast, seed).Choose(pickB.Snapshot(), pickB),
                    $"seed {seed}: the same pick in both fights");
        });
    }

    // ---------- what a decision costs ----------

    private static void Bounded(Check c)
    {
        c.Group("Rollout: a turn is worked out once, and costs no more play-outs than the setting allows", () =>
        {
            foreach (var setting in RolloutSetting.All)
            {
                // Two enemies and a full starter hand: many lines, none of them shows anything hidden before the turn ends.
                var f = CheckKit.VsDummies(new CheckDummy { Hp = 30, DisplayName = "Left", Steps = [Hit("HIT", 9)] }, new CheckDummy { Hp = 24, DisplayName = "Right", Steps = [Hit("HIT", 7)] });
                var player = new RolloutPolicy(setting);
                var turn = Turn(f, player);
                c.Equal("End turn", turn[^1], $"{setting.Name}: the turn ends");
                c.Equal(1L, player.PlansMade, $"{setting.Name}: {turn.Count} actions from one decision");
                c.That(player.PlayoutsRun > 0 && player.PlayoutsRun <= setting.Playouts, $"{setting.Name}: {player.PlayoutsRun} play-outs, at most {setting.Playouts}");
            }

            // A card that draws ends the line: what comes after it is decided when the card is seen.
            var draws = CheckKit.VsDummy(new CheckDummy { Hp = 100, Steps = [Hit("HIT", 6)] });
            draws.Clear(PileType.Hand);
            draws.Put(Gamble, PileType.Hand);
            draws.Put("StrikeIronclad", PileType.Hand);
            var again = new RolloutPolicy(RolloutSetting.Fast);
            var labels = Turn(draws, again);
            if (labels.Contains("Play Gamble"))
                c.That(again.PlansMade >= 2, $"it decided again after the card it drew ({again.PlansMade} decisions for {string.Join("; ", labels)})");
        });
    }

    // ---------- whole fights ----------

    private static void WholeFights(Check c)
    {
        c.Group("generated scenarios: Rollout finishes them with no action turned down, and the same way twice", () =>
        {
            foreach (var inner in new[] { RolloutInner.Lookahead, RolloutInner.Planner, RolloutInner.Greedy })
            {
                var player = new RolloutPolicy(Small with { Inner = inner });
                foreach (var scenario in Scenarios.Generate(3, 20261011))
                {
                    var row = Baseline.Play(scenario, player);
                    c.Equal(row, Baseline.Play(scenario, player), $"{inner} inside, {scenario.Id}: the replay");
                    c.Equal(0, row.Refused, $"{inner} inside, {scenario.Id}: actions turned down");
                    c.Equal(0L, player.PlayoutsFailed, $"{inner} inside, {scenario.Id}: play-outs that could not start");
                    c.Equal(0L, player.FellBack, $"{inner} inside, {scenario.Id}: decisions left to the one-turn search");
                    c.That(row.Turns is >= 1 and <= Tuning.MaxTurns, $"{inner} inside, {scenario.Id}: {row.Turns} turns");
                }
            }
        });

        c.Group("Rollout: a fight with every kind of card choice and a potion that offers cards runs to its end", () =>
        {
            var f = new FightBuilder().Encounter("NibbitsNormal").Seed(77)
                .Add("Armaments", "BurningPact", "Headbutt", "TrueGrit+", "PommelStrike", "Havoc", "Cinder")
                .Potions("AttackPotion", "GamblersBrew", "FirePotion").Start();
            var player = new RolloutPolicy(Small);
            for (var n = 0; n < 3000 && !f.IsOver; n++)
            {
                var snapshot = f.Snapshot();
                var id = player.Choose(snapshot, f);
                c.That(snapshot.Actions.Any(a => a.Id == id), $"'{id}' is offered on turn {f.Turn}");
                if (!f.Execute(id).Ok) break;
            }
            c.That(f.IsOver, "the fight ended");
            c.Equal(0L, player.PlayoutsFailed, "play-outs that could not start");
            c.That(player.PlayoutsRun > 0, "it played fights out");
        });
    }
}
