namespace SpireMind.Arena;

/// <summary>
/// Scripted checks for Bosses.cs. Every expected number is worked out by hand, turn by turn,
/// from the game's classes (v0.107.1) and encounters-3.json; none is taken from running the code.
/// </summary>
public sealed class BossesChecks : IArenaChecks
{
    private const string Beast = "CeremonialBeastBoss";
    private const string Kin = "TheKinBoss";
    private const string VantomFight = "VantomBoss";
    private const string RingingLine = "Ringing: cannot be played after another card this turn.";

    /// <summary>A potion for the checks: 3 Block, unpowered.</summary>
    private static readonly PotionDef Tonic = new("CheckTonic", "Check Tonic", PotionRarity.Common, TargetType.Self)
    {
        Text = _ => "Gain 3 Block.",
        Use = p => p.F.GainBlock(p.Owner, 3, Props.NonCardUnpowered),
    };

    public void Run(Check c)
    {
        Registered(c);
        Compositions(c);
        BeastPhaseOne(c);
        BeastPhaseTwo(c);
        PlowRules(c);
        Ringing(c);
        KinCycle(c);
        KinRules(c);
        VantomCycle(c);
        VantomRules(c);
        TenEnemyTurns(c);
        Copies(c);
        RandomPlay(c);
    }

    // ---------- helpers ----------

    /// <summary>A boss fight with the starter deck in a fixed order and 300 HP, so that a walk through many enemy turns is survived.</summary>
    private static Fight Boss(string encounter, int seed = 1) =>
        new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(300, 300).Encounter(encounter).Seed(seed).Start();

    /// <summary>Puts a free attack card in the hand and plays it at once, so that a check does not
    /// depend on what was drawn: <paramref name="hits"/> powered hits of <paramref name="damage"/>.
    /// The card is taken out of the fight again, so it never comes back in a later hand.</summary>
    private static void Hit(Fight f, int enemy, int damage, int hits = 1)
    {
        var def = new CardDef("CheckHit", "Check Hit", CardType.Attack, CardRarity.Token, 0, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = damage, ["Hits"] = hits },
            Play = c => c.Attack(c.V("Damage")).Hits((int)c.V("Hits")).Execute(),
        };
        var card = f.Put(def, PileType.Hand);
        f.Do($"play:{f.Hand.Count - 1}>e{enemy}");
        f.RemoveFromCombat(card);
    }

    /// <summary>The ids of the moves performed so far, in the order they were performed.</summary>
    private static List<string> Performed(Fight f) =>
        f.History.Entries.Where(e => e.Kind == HistoryKind.MonsterPerformedMove).Select(e => e.Name!).ToList();

    private static List<string> Ids(Fight f) => f.LegalActions().Select(a => a.Id).ToList();

    private static int Wounds(Fight f) => f.AllCards.Count(card => card.Def.ClassName == "Wound");

    private static int Count(string text, string part)
    {
        var n = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>Runs <paramref name="problem"/> for seeds 1 to <paramref name="seeds"/> and reports the seeds it found something wrong for as one check.</summary>
    private static void OverSeeds(Check c, int seeds, string what, Func<int, string?> problem)
    {
        var problems = new List<string>();
        for (var seed = 1; seed <= seeds; seed++)
            if (problem(seed) is { } p) problems.Add($"seed {seed}: {p}");
        c.That(problems.Count == 0, $"{what}: {problems.Count} of {seeds} seeds wrong, first: {string.Join("; ", problems.Take(3))}");
    }

    /// <summary>A negative temporary Strength, as Mangle and the Shackling Potion put on a monster.</summary>
    private sealed class CheckShackles : TemporaryStrengthPower
    {
        public override string Name => "Shackles";
        protected override bool IsPositive => false;
    }

    /// <summary>A positive temporary Strength.</summary>
    private sealed class CheckFlex : TemporaryStrengthPower
    {
        public override string Name => "Flex";
    }

    /// <summary>Answers every powered attack on its owner with unpowered damage after the hit landed, as the game's Flame Barrier does.</summary>
    private sealed class CheckReflect : Power
    {
        public override string Name => "Reflect";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";

        public override void AfterDamageReceived(Fight f, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardInstance? cardSource)
        {
            if (ReferenceEquals(target, Owner) && dealer is not null && props.IsPoweredAttack())
                f.Damage(dealer, Amount, Props.NonCardUnpowered, Owner);
        }
    }

    // ---------- what is registered ----------

    private static void Registered(Check c)
    {
        c.Group("Bosses: names, HP ranges and room type as registered", () =>
        {
            var r = Content.Registry;
            (string Class, string Name, int Min, int Max)[] monsters =
            [
                ("CeremonialBeast", "Ceremonial Beast", 252, 252),
                ("KinFollower", "Kin Follower", 58, 59),
                ("KinPriest", "Kin Priest", 190, 190),
                ("Vantom", "Vantom", 173, 173),
            ];
            foreach (var (cls, name, min, max) in monsters)
            {
                var m = r.NewMonster(cls);
                c.Equal(name, m.Name, $"{cls}: name");
                c.Equal(min, m.MinHp, $"{cls}: lowest HP");
                c.Equal(max, m.MaxHp, $"{cls}: highest HP");
            }
            foreach (var cls in new[] { Beast, Kin, VantomFight })
            {
                var e = r.NewEncounter(cls);
                c.Equal(RoomType.Boss, e.RoomType, $"{cls}: a boss room");
                c.That(!e.IsWeak, $"{cls}: not a weak encounter");
            }
            c.Sequence(["slot1", "slot2", "leaderSlot"], r.NewEncounter(Kin).Slots, "The Kin's slots in acting order");
            c.Equal(0, r.NewEncounter(Beast).Slots.Count, "the Beast has no slot");
            c.Equal(0, r.NewEncounter(VantomFight).Slots.Count, "Vantom has no slot");

            var plow = r.NewPower("PlowPower");
            c.That(plow is { Name: "Plow", Type: PowerType.Debuff, StackType: PowerStackType.Counter }, "Plow: a Debuff with a counter");
            var ringing = r.NewPower("RingingPower");
            c.That(ringing is { Name: "Ringing", Type: PowerType.Debuff, StackType: PowerStackType.Single }, "Ringing: a Debuff without a counter");
            c.Equal("Ringing", r.Affliction("Ringing").Name, "the Ringing affliction");
            c.Equal(RingingLine, r.Affliction("Ringing").ExtraCardText?.Invoke(1), "it adds a line to its card's text");
        });
    }

    // ---------- encounter compositions ----------

    private static void Compositions(Check c)
    {
        c.Group("CeremonialBeastBoss: one Beast with 252 HP and no slot, whatever the seed", () =>
        {
            OverSeeds(c, 200, "line-up", seed =>
            {
                var f = new FightBuilder().Encounter(Beast).Seed(seed).Start();
                if (f.RoomType != RoomType.Boss) return "not a boss room";
                if (f.Enemies.Count != 1 || f.Roster.Count != 1) return "not exactly one enemy";
                var beast = f.Enemy(0);
                if (beast.Monster is not CeremonialBeast || beast.Name != "Ceremonial Beast") return "not the Beast";
                if (beast.Slot is not null) return "it has a slot";
                if (beast.Hp != 252 || beast.MaxHp != 252) return $"HP {beast.Hp}/{beast.MaxHp}";
                if (beast.Powers.Count != 0) return "it has a starting power";
                if (!f.IntentTexts(beast).SequenceEqual(["buff"])) return "it does not open with Stamp";
                if (f.Rng.Encounter.Counter != 0) return "the line-up drew from the encounter stream";
                if (f.Rng.MonsterAi.Counter != 0) return "a fixed machine drew a move number";
                // The game's unique-HP roll picks among the free values even when there is one.
                if (f.Rng.Niche.Counter != 1) return $"{f.Rng.Niche.Counter} Niche draws for one monster";
                return null;
            });
        });

        c.Group("TheKinBoss: dance follower, follower, priest in slot order; follower HP 58 and 59, never equal", () =>
        {
            var firstGot58 = 0;
            OverSeeds(c, 200, "line-up", seed =>
            {
                var f = new FightBuilder().Encounter(Kin).Seed(seed).Start();
                if (f.RoomType != RoomType.Boss) return "not a boss room";
                if (f.Enemies.Count != 3 || f.Roster.Count != 3) return "not exactly three enemies";
                if (!f.Enemies.SequenceEqual(f.Roster)) return "acting order and e-index differ";
                if (!f.Enemies.Select(e => e.Slot).SequenceEqual(["slot1", "slot2", "leaderSlot"])) return "slot order";
                var (first, second, priest) = (f.Enemy(0), f.Enemy(1), f.Enemy(2));
                if (first.Monster is not KinFollower { StartsWithDance: true }) return "e0 is not the follower that opens with the dance";
                if (second.Monster is not KinFollower { StartsWithDance: false }) return "e1 is not the plain follower";
                if (priest.Monster is not KinPriest) return "e2 is not the priest";
                if (first.Name != "Kin Follower" || second.Name != "Kin Follower" || priest.Name != "Kin Priest") return "names";
                if (first.MaxHp is not (58 or 59) || second.MaxHp is not (58 or 59)) return $"follower HP {first.MaxHp}, {second.MaxHp}";
                if (first.MaxHp == second.MaxHp) return $"both followers have {first.MaxHp} HP";
                if (first.Hp != first.MaxHp || second.Hp != second.MaxHp) return "a follower does not start at full HP";
                if (priest.Hp != 190 || priest.MaxHp != 190) return $"priest HP {priest.Hp}/{priest.MaxHp}";
                if (first.PowerAmount<MinionPower>() != 1 || second.PowerAmount<MinionPower>() != 1) return "a follower is no Minion";
                if (first.Powers.Count != 1 || second.Powers.Count != 1 || priest.Powers.Count != 0) return "starting powers";
                if (!f.IntentTexts(first).SequenceEqual(["buff"])) return "e0 does not open with Power Dance";
                if (!f.IntentTexts(second).SequenceEqual(["attack 5"])) return "e1 does not open with Quick Slash";
                if (!f.IntentTexts(priest).SequenceEqual(["attack 8", "debuff"])) return "the priest does not open with Orb of Frailty";
                if (f.IncomingDamage() != 13) return $"incoming {f.IncomingDamage()}";
                if (f.Rng.Encounter.Counter != 0) return "the line-up drew from the encounter stream";
                if (f.Rng.MonsterAi.Counter != 0) return "a fixed machine drew a move number";
                if (f.Rng.Niche.Counter != 3) return $"{f.Rng.Niche.Counter} Niche draws for three monsters";
                if (first.MaxHp == 58) firstGot58++;
                return null;
            });
            c.That(firstGot58 is > 70 and < 130, $"the first follower's HP is a fair roll: 58 came {firstGot58} times in 200");
        });

        c.Group("VantomBoss: one Vantom with 173 HP, Slippery 8 and no slot, whatever the seed", () =>
        {
            OverSeeds(c, 200, "line-up", seed =>
            {
                var f = new FightBuilder().Encounter(VantomFight).Seed(seed).Start();
                if (f.RoomType != RoomType.Boss) return "not a boss room";
                if (f.Enemies.Count != 1 || f.Roster.Count != 1) return "not exactly one enemy";
                var vantom = f.Enemy(0);
                if (vantom.Monster is not Vantom || vantom.Name != "Vantom") return "not Vantom";
                if (vantom.Slot is not null) return "it has a slot";
                if (vantom.Hp != 173 || vantom.MaxHp != 173) return $"HP {vantom.Hp}/{vantom.MaxHp}";
                if (vantom.Powers.Count != 1 || vantom.PowerAmount<SlipperyPower>() != 8) return "not exactly Slippery 8";
                if (!f.IntentTexts(vantom).SequenceEqual(["attack 7"])) return "it does not open with Ink Blot";
                if (f.Rng.Encounter.Counter != 0) return "the line-up drew from the encounter stream";
                if (f.Rng.MonsterAi.Counter != 0) return "a fixed machine drew a move number";
                if (f.Rng.Niche.Counter != 1) return $"{f.Rng.Niche.Counter} Niche draws for one monster";
                return null;
            });
        });
    }

    // ---------- Ceremonial Beast ----------

    private static void BeastPhaseOne(Check c)
    {
        c.Group("CeremonialBeast: Stamp, then Plow every turn for 18 plus 2 more each time", () =>
        {
            var f = Boss(Beast);
            var beast = f.Enemy(0);
            c.Sequence(["buff"], f.IntentTexts(beast), "turn 1: Stamp");
            c.Equal(0, f.IncomingDamage(), "turn 1: nothing incoming");
            c.That(!beast.HasPower<PlowPower>(), "Plow is not there during the player's first turn");

            f.EndTurn();
            c.Equal(150, beast.PowerAmount<PlowPower>(), "Stamp: Plow 150 on itself");
            c.That(beast.GetPower<PlowPower>()!.Describe().Contains("150", StringComparison.Ordinal), "its text names the threshold");
            c.Equal(300, f.Player.Hp, "Stamp deals no damage");
            c.Equal(0, beast.PowerAmount<StrengthPower>(), "Stamp gives no Strength");

            // Enemy turns 2 to 7. Block 10 leaves 10 of the 20 through; Block 30 stops the 24 and the Strength still comes.
            (int Shown, int Block, int Hp, int Strength)[] plows =
            [
                (18, 0, 282, 2), (20, 10, 272, 4), (22, 0, 250, 6), (24, 30, 250, 8), (26, 0, 224, 10), (28, 0, 196, 12),
            ];
            var turn = 2;
            foreach (var (shown, block, hp, strength) in plows)
            {
                c.Sequence([$"attack {shown}", "buff"], f.IntentTexts(beast), $"turn {turn}: Plow shows");
                c.Equal(shown, f.IncomingDamage(), $"turn {turn}: incoming");
                f.SetBlock(f.Player, block);
                f.EndTurn();
                c.Equal(hp, f.Player.Hp, $"enemy turn {turn}: HP after {shown} into {block} Block");
                c.Equal(strength, beast.PowerAmount<StrengthPower>(), $"enemy turn {turn}: Strength after the hit");
                turn++;
            }

            c.Equal(150, beast.PowerAmount<PlowPower>(), "Plow stays while its HP is above 150");
            c.Equal(252, beast.Hp, "the Beast was never hit");
            c.Equal(0, beast.Block, "it never gains Block");
            c.Sequence(["STAMP_MOVE", "PLOW_MOVE", "PLOW_MOVE", "PLOW_MOVE", "PLOW_MOVE", "PLOW_MOVE", "PLOW_MOVE", "PLOW_MOVE"],
                beast.Monster!.MoveLog, "the chosen moves, turn 1 to 8");
            c.Equal(0, f.Rng.MonsterAi.Counter, "no move number was drawn");
        });
    }

    private static void BeastPhaseTwo(Check c)
    {
        c.Group("CeremonialBeast: at 150 HP it loses its Strength and is stunned, then Beast Cry, Stomp, Crush for good", () =>
        {
            var f = Boss(Beast);
            var beast = f.Enemy(0);
            f.EndTurn();
            f.EndTurn();
            f.EndTurn();
            c.Equal(262, f.Player.Hp, "after Stamp, Plow 18 and Plow 20");
            c.Equal(4, beast.PowerAmount<StrengthPower>(), "two Plows: 4 Strength");
            c.Sequence(["attack 22", "buff"], f.IntentTexts(beast), "turn 4: a third Plow is shown");

            f.SetHp(beast, 156);
            Hit(f, 0, 6);
            c.Equal(150, beast.Hp, "156 - 6");
            c.That(!beast.HasPower<StrengthPower>(), "exactly 150 trips Plow: all Strength is gone");
            c.That(!beast.HasPower<PlowPower>(), "the Plow power removed itself");
            c.Sequence(["stunned"], f.IntentTexts(beast), "the shown Plow is replaced by the stun at once");
            c.Equal(0, f.IncomingDamage(), "nothing incoming from a stunned Beast");
            c.Sequence(["STAMP_MOVE", "PLOW_MOVE", "PLOW_MOVE", "PLOW_MOVE"], beast.Monster!.MoveLog, "the stun is not a chosen move");

            f.EndTurn();
            c.Equal(262, f.Player.Hp, "enemy turn 4: the stunned Beast does nothing");
            c.That(!f.Player.HasPower<RingingPower>(), "no Ringing yet");

            // Turns 5 to 13: what is shown, incoming, the player's Block, HP after the enemy turn, the
            // Beast's Strength after it, and whether the player then has Ringing.
            (string[] Shown, int Incoming, int Block, int Hp, int Strength, bool Ringing)[] turns =
            [
                (["debuff"], 0, 0, 262, 0, true),
                (["attack 15"], 15, 0, 247, 0, false),
                (["attack 17", "buff"], 17, 0, 230, 3, false),
                (["debuff"], 0, 0, 230, 3, true),
                (["attack 18"], 18, 5, 217, 3, false),
                (["attack 20", "buff"], 20, 0, 197, 6, false),
                (["debuff"], 0, 0, 197, 6, true),
                (["attack 21"], 21, 0, 176, 6, false),
                (["attack 23", "buff"], 23, 0, 153, 9, false),
            ];
            var turn = 5;
            var ringingNow = false;
            foreach (var (shown, incoming, block, hp, strength, ringing) in turns)
            {
                c.Sequence(shown, f.IntentTexts(beast), $"turn {turn}: shown");
                c.Equal(incoming, f.IncomingDamage(), $"turn {turn}: incoming");
                c.Equal(ringingNow, f.Player.HasPower<RingingPower>(), $"turn {turn}: Ringing covers exactly the turn after a Beast Cry");
                c.Equal(3, f.Energy, $"turn {turn}: energy is not touched");
                c.Equal(5, f.Hand.Count, $"turn {turn}: the draw is not touched");
                f.SetBlock(f.Player, block);
                f.EndTurn();
                c.Equal(hp, f.Player.Hp, $"enemy turn {turn}: HP");
                c.Equal(strength, beast.PowerAmount<StrengthPower>(), $"enemy turn {turn}: Strength");
                ringingNow = ringing;
                turn++;
            }

            c.That(!beast.HasPower<PlowPower>(), "Plow never comes back");
            c.Equal(150, beast.Hp, "the Beast was not hit again");
            c.Sequence(
                [
                    "STAMP_MOVE", "PLOW_MOVE", "PLOW_MOVE", "PLOW_MOVE",
                    "BEAST_CRY_MOVE", "STOMP_MOVE", "CRUSH_MOVE", "BEAST_CRY_MOVE", "STOMP_MOVE", "CRUSH_MOVE",
                    "BEAST_CRY_MOVE", "STOMP_MOVE", "CRUSH_MOVE", "BEAST_CRY_MOVE",
                ],
                beast.Monster!.MoveLog, "the chosen moves, turn 1 to 14");
            c.Sequence(
                [
                    "STAMP_MOVE", "PLOW_MOVE", "PLOW_MOVE", "STUNNED",
                    "BEAST_CRY_MOVE", "STOMP_MOVE", "CRUSH_MOVE", "BEAST_CRY_MOVE", "STOMP_MOVE", "CRUSH_MOVE",
                    "BEAST_CRY_MOVE", "STOMP_MOVE", "CRUSH_MOVE",
                ],
                Performed(f), "the moves performed, enemy turn 1 to 13");
            c.Equal(0, f.Rng.MonsterAi.Counter, "no move number was drawn");
        });
    }

    private static void PlowRules(Check c)
    {
        c.Group("Plow: 151 HP does not trip it, 150 does", () =>
        {
            var f = Boss(Beast);
            var beast = f.Enemy(0);
            f.EndTurn();
            f.SetHp(beast, 157);
            Hit(f, 0, 6);
            c.Equal(151, beast.Hp, "157 - 6");
            c.Equal(150, beast.PowerAmount<PlowPower>(), "151 is above the threshold");
            c.Sequence(["attack 18", "buff"], f.IntentTexts(beast), "it still Plows");
            Hit(f, 0, 1);
            c.Equal(150, beast.Hp, "151 - 1");
            c.That(!beast.HasPower<PlowPower>(), "150 is at the threshold");
            c.Sequence(["stunned"], f.IntentTexts(beast), "stunned");
        });

        c.Group("Plow: HP lost before Stamp counts; a fully blocked hit cannot trip it; the next HP loss does", () =>
        {
            var f = Boss(Beast);
            var beast = f.Enemy(0);
            f.SetHp(beast, 140);
            Hit(f, 0, 6);
            c.Equal(134, beast.Hp, "turn 1: 140 - 6");
            c.Sequence(["buff"], f.IntentTexts(beast), "turn 1: there is no Plow power yet, so nothing happens below 150");
            f.EndTurn();
            c.Equal(150, beast.PowerAmount<PlowPower>(), "Stamp puts Plow on a Beast that is already below the threshold");
            c.Sequence(["attack 18", "buff"], f.IntentTexts(beast), "applying it trips nothing");

            f.SetBlock(beast, 10);
            Hit(f, 0, 6);
            c.Equal(134, beast.Hp, "6 into 10 Block: no HP lost");
            c.Equal(4, beast.Block, "Block left");
            c.Equal(150, beast.PowerAmount<PlowPower>(), "a fully blocked hit does not trip Plow");
            Hit(f, 0, 6);
            c.Equal(132, beast.Hp, "6 into 4 Block: 2 through");
            c.That(!beast.HasPower<PlowPower>(), "the first hit that takes HP trips it");
            c.Sequence(["stunned"], f.IntentTexts(beast), "stunned");
        });

        c.Group("Plow: HP loss that is not an attack trips it too", () =>
        {
            var f = Boss(Beast);
            var beast = f.Enemy(0);
            f.EndTurn();
            f.SetHp(beast, 152);
            f.Damage(beast, 3, Props.NonCardHpLoss, f.Player);
            c.Equal(149, beast.Hp, "152 - 3");
            c.Sequence(["stunned"], f.IntentTexts(beast), "plain HP loss from a power");

            var g = Boss(Beast);
            g.EndTurn();
            g.SetHp(g.Enemy(0), 152);
            g.Damage(g.Enemy(0), 20, Props.NonCardUnpowered, g.Player);
            c.Equal(132, g.Enemy(0).Hp, "152 - 20");
            c.Sequence(["stunned"], g.IntentTexts(g.Enemy(0)), "unpowered damage, as from a potion");
        });

        c.Group("Plow: it can trip between the hits of one attack; the rest still land and it trips only once", () =>
        {
            var f = Boss(Beast);
            var beast = f.Enemy(0);
            f.EndTurn();
            f.EndTurn();
            c.Equal(2, beast.PowerAmount<StrengthPower>(), "one Plow: 2 Strength");
            f.SetHp(beast, 155);
            Hit(f, 0, 4, hits: 3);
            c.Equal(143, beast.Hp, "155 - 4 - 4 - 4: the third hit landed after the second tripped Plow");
            c.That(!beast.HasPower<StrengthPower>(), "Strength gone");
            c.That(!beast.HasPower<PlowPower>(), "Plow gone");
            c.Sequence(["stunned"], f.IntentTexts(beast), "stunned");
            f.EndTurn();
            c.Equal(282, f.Player.Hp, "the stunned turn: only the 18 of enemy turn 2 was ever taken");
            c.Sequence(["debuff"], f.IntentTexts(beast), "one stunned turn, then Beast Cry");
        });

        c.Group("Plow: temporary Strength goes with it and is never given back", () =>
        {
            var f = Boss(Beast);
            var beast = f.Enemy(0);
            f.EndTurn();
            f.EndTurn();
            f.EndTurn();
            c.Equal(4, beast.PowerAmount<StrengthPower>(), "two Plows: 4 Strength");
            f.ApplyPower<CheckShackles>(beast, 3, f.Player);
            f.ApplyPower<CheckFlex>(beast, 5, beast);
            c.Equal(6, beast.PowerAmount<StrengthPower>(), "4 - 3 + 5");
            c.Sequence(["attack 24", "buff"], f.IntentTexts(beast), "Plow 18 + 6");

            f.SetHp(beast, 155);
            Hit(f, 0, 6);
            c.Equal(149, beast.Hp, "155 - 6");
            c.That(!beast.HasPower<StrengthPower>(), "Strength is removed whatever its amount");
            c.That(!beast.HasPower<CheckShackles>() && !beast.HasPower<CheckFlex>(), "both temporary Strength powers are removed");
            f.EndTurn();
            // Their end-of-turn give-back (+3 and -5) would have left -2.
            c.That(!beast.HasPower<StrengthPower>(), "nothing is given back or taken at the end of that enemy turn");
            c.Equal(262, f.Player.Hp, "the stunned turn");
        });

        c.Group("Plow: tripped during its own Plow, the +2 Strength of that Plow comes after the wipe and the stun is the next turn", () =>
        {
            var f = Boss(Beast);
            var beast = f.Enemy(0);
            f.EndTurn();
            f.EndTurn();
            c.Equal(2, beast.PowerAmount<StrengthPower>(), "one Plow: 2 Strength");
            f.ApplyPower<CheckReflect>(f.Player, 4, f.Player);
            f.SetHp(beast, 153);
            c.Sequence(["attack 20", "buff"], f.IntentTexts(beast), "turn 3: Plow");

            f.EndTurn();
            c.Equal(262, f.Player.Hp, "enemy turn 3: the Plow hit for 20");
            c.Equal(149, beast.Hp, "153 - 4 answered to the hit");
            c.That(!beast.HasPower<PlowPower>(), "that tripped Plow in the middle of the move");
            c.Equal(2, beast.PowerAmount<StrengthPower>(), "the 2 it had were wiped, then the move's own 2 were added");
            c.Sequence(["stunned"], f.IntentTexts(beast), "turn 4: the stun waits, the move pick does not leave it");
            c.Equal(0, f.IncomingDamage(), "turn 4: nothing incoming");
            c.Sequence(["STAMP_MOVE", "PLOW_MOVE", "PLOW_MOVE"], beast.Monster!.MoveLog, "no move was chosen at the start of turn 4");

            f.EndTurn();
            c.Equal(262, f.Player.Hp, "enemy turn 4 is the stunned one");
            c.Sequence(["debuff"], f.IntentTexts(beast), "turn 5: Beast Cry");
            f.EndTurn();
            c.That(f.Player.HasPower<RingingPower>(), "enemy turn 5: Ringing");
            c.Sequence(["attack 17"], f.IntentTexts(beast), "turn 6: Stomp 15 + 2");
            f.EndTurn();
            c.Equal(245, f.Player.Hp, "enemy turn 6: 17");
            c.Equal(145, beast.Hp, "and 4 answered again");
            c.Sequence(["attack 19", "buff"], f.IntentTexts(beast), "turn 7: Crush 17 + 2");
            c.Sequence(["STAMP_MOVE", "PLOW_MOVE", "PLOW_MOVE", "STUNNED", "BEAST_CRY_MOVE", "STOMP_MOVE"], Performed(f), "the moves performed");
        });

        c.Group("CeremonialBeast: the machine's own STUN_MOVE, which no fight enters, shows a stun, does nothing and leads to Beast Cry", () =>
        {
            var f = Boss(Beast);
            var beast = f.Enemy(0);
            beast.Monster!.ForceNextMove("STUN_MOVE");
            c.Sequence(["stun"], f.IntentTexts(beast), "its intent");
            c.Equal(0, f.IncomingDamage(), "nothing incoming");
            f.EndTurn();
            c.Equal(300, f.Player.Hp, "it does nothing");
            c.Equal(0, beast.Powers.Count, "and applies nothing");
            c.Sequence(["debuff"], f.IntentTexts(beast), "Beast Cry follows");
            f.EndTurn();
            c.Sequence(["attack 15"], f.IntentTexts(beast), "then Stomp");
        });

        c.Group("Plow: the killing blow asks nothing", () =>
        {
            var f = Boss(Beast);
            f.EndTurn();
            f.SetHp(f.Enemy(0), 6);
            Hit(f, 0, 6);
            c.That(f.IsOver && f.Won, "the fight is won");
            c.Equal(0, f.Enemy(0).Hp, "the Beast is dead");
        });
    }

    /// <summary>
    /// A Beast fight on turn 3 with the stunned turn behind it: the Beast shows Beast Cry. The
    /// piles are arranged so that the cry finds cards in every pile and turn 4 draws Strike,
    /// Defend, Bash, Strike, Defend, with a Wound left in the draw pile. <paramref name="first"/>
    /// replaces the first of those five.
    /// </summary>
    private static Fight BeforeTheCry(string first = "StrikeIronclad")
    {
        var f = Boss(Beast);
        f.EndTurn();
        f.SetHp(f.Enemy(0), 151);
        Hit(f, 0, 1);
        f.EndTurn();
        f.Clear(PileType.Draw);
        f.Clear(PileType.Discard);
        foreach (var card in new[] { first, "DefendIronclad", "Bash", "StrikeIronclad", "DefendIronclad", "Wound" })
            f.Put(card, PileType.Draw);
        f.Put("StrikeIronclad", PileType.Exhaust);
        return f;
    }

    private static void Ringing(Check c)
    {
        c.Group("Ringing: every card in every pile is marked, and the turn is a one-card turn", () =>
        {
            var f = BeforeTheCry();
            var beast = f.Enemy(0);
            c.Sequence(["debuff"], f.IntentTexts(beast), "turn 3: Beast Cry is shown");
            c.That(f.AllCards.All(card => card.Affliction is null), "no card is marked before the cry");
            var plainStrike = Fight.CardText(f, f.DrawPile[0], PileType.Hand);
            f.EndTurn();

            c.Equal(1, f.Player.PowerAmount<RingingPower>(), "the cry: Ringing 1, still there after the enemy turn it came in");
            c.Equal(300, f.Player.Hp, "the cry deals no damage");
            c.Equal(12, f.AllCards.Count(), "5 in hand, 1 in the draw pile, 5 discarded, 1 exhausted");
            c.That(f.AllCards.All(card => card.Affliction == "Ringing"), "all of them carry Ringing, the Wound and the exhausted card too");
            c.Sequence(["Strike", "Defend", "Bash", "Strike", "Defend"], f.HandTitles(), "turn 4: the hand");
            c.Equal(3, f.Energy, "energy is not reduced");
            c.Sequence(["attack 15"], f.IntentTexts(beast), "turn 4: Stomp");
            c.Equal("Deal 6 damage.", plainStrike, "a Strike's text without the mark");
            c.Equal($"Deal 6 damage. / {RingingLine}", Fight.CardText(f, f.Hand[0], PileType.Hand), "a marked Strike says so after its own text");
            c.Equal($"Unplayable. / {RingingLine}", Fight.CardText(f, f.DrawPile[0], PileType.Draw), "a marked Wound");
            c.Sequence(["play:0>e0", "play:1", "play:2>e0", "play:3>e0", "play:4", "end_turn"], Ids(f), "before the first card, every card can be played");

            f.Do("play:1");
            c.Equal(5, f.Player.Block, "the one card: Defend");
            c.Equal(2, f.Energy, "it cost 1");
            c.Sequence(["end_turn"], Ids(f), "after it, no card can be played");
            foreach (var card in f.Hand)
            {
                c.That(!f.CanPlay(card, out var reason), $"{card.Title} cannot be played");
                c.Equal(UnplayableReason.BlockedByHook, reason, $"{card.Title}: stopped by the power, not by energy");
            }
            c.That(!f.Execute("play:0>e0").Ok, "a second card is refused");
            c.Equal(4, Count(f.Seen(), "\"unplayable\":\"BlockedByHook\""), "the four cards left read BlockedByHook");

            f.SetPotion(0, Tonic);
            c.Sequence(["potion:0", "discard_potion:0", "end_turn"], Ids(f), "potions are not restricted");
            f.Do("potion:0");
            c.Equal(8, f.Player.Block, "the potion worked: 5 + 3");

            var made = f.CreateCard("StrikeIronclad");
            f.AddGeneratedCard(made, PileType.Hand);
            c.Equal("Ringing", made.Affliction, "a card that enters the fight during the turn is marked too");
            c.Sequence(["end_turn"], Ids(f), "and cannot be played either");

            var strike = f.Hand[0];
            f.AutoPlay(strike, beast);
            c.Equal(150, beast.Hp, "an automatic play of a marked card is stopped the same way");
            c.Equal(PileType.Discard, strike.Pile, "the card just moves on to the discard pile");

            f.EndTurn();
            c.That(!f.Player.HasPower<RingingPower>(), "Ringing left at the end of the player's own turn");
            c.That(f.AllCards.All(card => card.Affliction is null), "and took the mark off every card");
            c.Equal(293, f.Player.Hp, "enemy turn 4: Stomp 15 into 8 Block");
            Hit(f, 0, 6);
            Hit(f, 0, 6);
            c.Equal(138, beast.Hp, "turn 5: two cards in a row again");
        });

        c.Group("Ringing: an automatic play counts as the turn's card", () =>
        {
            var f = BeforeTheCry();
            f.EndTurn();
            f.AutoPlay(f.Hand[0], f.Enemy(0));
            c.Equal(144, f.Enemy(0).Hp, "nothing was played yet, so the automatic Strike goes through: 150 - 6");
            c.Sequence(["end_turn"], Ids(f), "and after it the hand is shut");
        });

        c.Group("Ringing: a card that carries another affliction is left alone and stays playable", () =>
        {
            var f = BeforeTheCry();
            // The second card of turn 4, a Defend. No other affliction is in reach of this file, so the
            // mark is set by hand under a made-up name; this fight must not be rendered while it is on.
            var other = f.DrawPile[1];
            other.Affliction = "CheckOther";
            f.EndTurn();
            c.Equal("CheckOther", other.Affliction, "the cry does not replace another affliction");
            c.Equal("Ringing", f.Hand[0].Affliction, "the cards around it are marked");
            f.Do("play:0>e0");
            c.Sequence(["play:0", "end_turn"], Ids(f), "after the first card only the card without Ringing can be played");
            f.Do("play:0");
            c.Equal(5, f.Player.Block, "and it works: Defend");
            f.EndTurn();
            c.Equal("CheckOther", other.Affliction, "Ringing's end clears only its own mark");
            other.Affliction = null;
        });

        c.Group("Ringing: a card that asks for a choice is the turn's one card, and its choice still works", () =>
        {
            var f = BeforeTheCry(first: "Armaments");
            f.EndTurn();
            c.Sequence(["Armaments", "Defend", "Bash", "Strike", "Defend"], f.HandTitles(), "turn 4: the hand");
            f.Do("play:0");
            c.Equal("hand_select", f.Snapshot().Screen, "Armaments asks which card to upgrade");
            f.Do("select:1");
            f.Do("confirm");
            c.Equal(5, f.Player.Block, "its Block, gained once");
            c.Sequence(["Defend", "Bash+", "Strike", "Defend"], f.HandTitles(), "the chosen Bash was upgraded");
            c.That(f.Hand.All(card => card.Affliction == "Ringing"), "the upgraded card still carries the mark");
            c.Sequence(["end_turn"], Ids(f), "Armaments was the turn's card");
        });

        c.Group("Ringing: Artifact stops the cry, and then no card is marked", () =>
        {
            var f = BeforeTheCry();
            f.ApplyPower<ArtifactPower>(f.Player, 1, f.Player);
            f.EndTurn();
            c.That(!f.Player.HasPower<RingingPower>(), "no Ringing");
            c.That(!f.Player.HasPower<ArtifactPower>(), "Artifact was used up");
            c.That(f.AllCards.All(card => card.Affliction is null), "no card is marked");
            f.Do("play:1");
            c.Sequence(["play:0>e0", "play:1>e0", "play:2>e0", "play:3", "end_turn"], Ids(f), "the turn is an ordinary one");
        });
    }

    // ---------- The Kin ----------

    private static void KinCycle(Check c)
    {
        c.Group("The Kin: eight enemy turns of the two follower cycles and the priest's", () =>
        {
            var f = Boss(Kin);
            var (first, second, priest) = (f.Enemy(0), f.Enemy(1), f.Enemy(2));

            // Per turn: what e0, e1 and e2 show, the incoming total, the player's Block, HP after the
            // enemy turn, the three Strength amounts after it, and the player's Frail and Weak then.
            (string[] E0, string[] E1, string[] E2, int Incoming, int Block, int Hp, int[] Strength, int Frail, int Weak)[] turns =
            [
                (["buff"], ["attack 5"], ["attack 8", "debuff"], 13, 0, 287, [2, 0, 0], 1, 0),
                (["attack 7"], ["attack 2x2"], ["attack 8", "debuff"], 19, 0, 268, [2, 0, 0], 0, 1),
                // Block 10: the boomerang's 4 and 4 and 2 of the beam's first 3 are stopped; 1 + 3 + 3 get through.
                (["attack 4x2"], ["buff"], ["attack 3x3"], 17, 10, 261, [2, 2, 0], 0, 0),
                (["buff"], ["attack 7"], ["buff"], 7, 0, 254, [4, 2, 2], 0, 0),
                (["attack 9"], ["attack 4x2"], ["attack 10", "debuff"], 27, 0, 227, [4, 2, 2], 1, 0),
                // Block 5: 5 of the boomerang's first 6 are stopped; 1 + 6 + 10 get through.
                (["attack 6x2"], ["buff"], ["attack 10", "debuff"], 22, 5, 210, [4, 4, 2], 0, 1),
                (["buff"], ["attack 9"], ["attack 5x3"], 24, 0, 186, [6, 4, 2], 0, 0),
                (["attack 11"], ["attack 6x2"], ["buff"], 23, 0, 163, [6, 4, 4], 0, 0),
            ];
            var turn = 1;
            foreach (var t in turns)
            {
                c.Sequence(t.E0, f.IntentTexts(first), $"turn {turn}: the dance follower shows");
                c.Sequence(t.E1, f.IntentTexts(second), $"turn {turn}: the second follower shows");
                c.Sequence(t.E2, f.IntentTexts(priest), $"turn {turn}: the priest shows");
                c.Equal(t.Incoming, f.IncomingDamage(), $"turn {turn}: incoming");
                f.SetBlock(f.Player, t.Block);
                f.EndTurn();
                c.Equal(t.Hp, f.Player.Hp, $"enemy turn {turn}: HP");
                c.Sequence(t.Strength, new[] { first, second, priest }.Select(e => e.PowerAmount<StrengthPower>()), $"enemy turn {turn}: Strength of e0, e1, e2");
                c.Equal(t.Frail, f.Player.PowerAmount<FrailPower>(), $"after enemy turn {turn}: Frail");
                c.Equal(t.Weak, f.Player.PowerAmount<WeakPower>(), $"after enemy turn {turn}: Weak");
                turn++;
            }

            c.Sequence(
                ["POWER_DANCE_MOVE", "QUICK_SLASH_MOVE", "BOOMERANG_MOVE", "POWER_DANCE_MOVE", "QUICK_SLASH_MOVE", "BOOMERANG_MOVE", "POWER_DANCE_MOVE", "QUICK_SLASH_MOVE", "BOOMERANG_MOVE"],
                first.Monster!.MoveLog, "the dance follower's moves, turn 1 to 9");
            c.Sequence(
                ["QUICK_SLASH_MOVE", "BOOMERANG_MOVE", "POWER_DANCE_MOVE", "QUICK_SLASH_MOVE", "BOOMERANG_MOVE", "POWER_DANCE_MOVE", "QUICK_SLASH_MOVE", "BOOMERANG_MOVE", "POWER_DANCE_MOVE"],
                second.Monster!.MoveLog, "the second follower's moves, one step behind");
            c.Sequence(
                ["ORB_OF_FRAILTY_MOVE", "ORB_OF_WEAKNESS_MOVE", "BEAM_MOVE", "RITUAL_MOVE", "ORB_OF_FRAILTY_MOVE", "ORB_OF_WEAKNESS_MOVE", "BEAM_MOVE", "RITUAL_MOVE", "ORB_OF_FRAILTY_MOVE"],
                priest.Monster!.MoveLog, "the priest's moves");
            c.Equal(0, f.Rng.MonsterAi.Counter, "no move number was drawn");
            c.That(first.Hp == first.MaxHp && second.Hp == second.MaxHp && priest.Hp == 190, "none of them was hit");
        });

        c.Group("The Kin: they act in slot order", () =>
        {
            var f = Boss(Kin);
            f.EndTurn();
            c.Sequence(["POWER_DANCE_MOVE", "QUICK_SLASH_MOVE", "ORB_OF_FRAILTY_MOVE"], Performed(f), "enemy turn 1: e0, e1, then the priest");
            var actors = f.History.Entries.Where(e => e.Kind == HistoryKind.MonsterPerformedMove).Select(e => e.ActorId);
            c.Sequence([f.Enemy(0).Id, f.Enemy(1).Id, f.Enemy(2).Id], actors, "by creature");
        });

        c.Group("Kin Priest: the orbs' Frail and Weak, also through Block, each lasting exactly the player's next turn", () =>
        {
            var f = Boss(Kin);
            var priest = f.Enemy(2);
            f.Play("Defend");
            c.Equal(5, f.Player.Block, "turn 1: a Defend without Frail");
            f.EndTurn();
            c.Equal(292, f.Player.Hp, "enemy turn 1: the slash's 5 is blocked, the orb's 8 lands");
            c.Equal(1, f.Player.PowerAmount<FrailPower>(), "Orb of Frailty: 1 Frail, kept through the end of that enemy turn");

            f.Play("Defend");
            c.Equal(3, f.Player.Block, "turn 2, Frail: 5 x 0.75 = 3.75, so 3");
            f.Play("Defend");
            c.Equal(6, f.Player.Block, "3 + 3.75 = 6.75, so 6");
            f.SetBlock(f.Player, 50);
            f.EndTurn();
            c.Equal(292, f.Player.Hp, "enemy turn 2: 7 + 4 + 8 all blocked");
            c.Equal(1, f.Player.PowerAmount<WeakPower>(), "Orb of Weakness: 1 Weak although the hit was blocked");
            c.Equal(0, f.Player.PowerAmount<FrailPower>(), "Frail is gone after one turn");

            f.Put("StrikeIronclad", PileType.Hand);
            f.Play("StrikeIronclad", 2);
            c.Equal(186, priest.Hp, "turn 3, Weak: 6 x 0.75 = 4.5, so 4");
            f.Put("DefendIronclad", PileType.Hand);
            f.Play("DefendIronclad");
            c.Equal(5, f.Player.Block, "Weak does not touch Block, and Frail is gone");
            f.EndTurn();
            c.Equal(280, f.Player.Hp, "enemy turn 3: boomerang 4 + 4 and beam 3 + 3 + 3 into 5 Block");
            c.Equal(0, f.Player.PowerAmount<WeakPower>(), "Weak is gone after one turn");

            f.Put("StrikeIronclad", PileType.Hand);
            f.Play("StrikeIronclad", 2);
            c.Equal(180, priest.Hp, "turn 4: a full 6 again");
        });
    }

    private static void KinRules(Check c)
    {
        c.Group("The Kin: the followers are Minions, the priest is the only primary enemy", () =>
        {
            var f = Boss(Kin);
            var (first, second, priest) = (f.Enemy(0), f.Enemy(1), f.Enemy(2));
            c.That(first.IsSecondaryEnemy && second.IsSecondaryEnemy, "both followers are secondary enemies");
            c.That(priest.IsPrimaryEnemy, "the priest is primary");
            c.That(first.Powers.Any(p => !p.ShouldOwnerDeathTriggerFatal) && second.Powers.Any(p => !p.ShouldOwnerDeathTriggerFatal), "a follower's death is not Fatal");
            c.That(priest.Powers.All(p => p.ShouldOwnerDeathTriggerFatal), "the priest's is");
            c.Equal(1, Count(f.Seen(), "\"primary\":true"), "the snapshot marks the priest as primary while a follower lives");
        });

        c.Group("Kin Priest: when it dies the followers die with it and the fight is won", () =>
        {
            var f = Boss(Kin);
            f.SetHp(f.Enemy(2), 6);
            Hit(f, 2, 6);
            c.That(f.IsOver && f.Won, "won with both followers untouched");
            c.Equal(0, f.Enemy(0).Hp, "the dance follower was killed");
            c.Equal(0, f.Enemy(1).Hp, "the second follower was killed");
            c.Equal(0, f.Enemies.Count(e => e.IsAlive), "nobody is left");
        });

        c.Group("Kin Follower: its death changes nothing for the others; the priest alone keeps the fight going", () =>
        {
            var f = Boss(Kin);
            var (first, second, priest) = (f.Enemy(0), f.Enemy(1), f.Enemy(2));
            f.SetHp(first, 6);
            Hit(f, 0, 6);
            c.That(first.IsDead && !f.Enemies.Contains(first), "the dance follower is dead and gone");
            c.That(first.HasPower<MinionPower>(), "Minion stays on the dead follower");
            c.That(f.InProgress && !f.IsEnding, "the fight goes on");
            c.Sequence(["attack 5"], f.IntentTexts(second), "the other follower is unchanged");
            c.Sequence(["attack 8", "debuff"], f.IntentTexts(priest), "so is the priest");
            c.Equal(13, f.IncomingDamage(), "5 + 8");
            c.Equal(1, Count(f.Seen(), "\"primary\":true"), "one follower still lives: the priest is still marked");
            f.EndTurn();
            c.Equal(287, f.Player.Hp, "enemy turn 1: 5 + 8");
            c.Sequence(["QUICK_SLASH_MOVE", "ORB_OF_FRAILTY_MOVE"], Performed(f), "the dead follower's dance did not happen");

            c.Sequence(["attack 2x2"], f.IntentTexts(second), "turn 2: the follower's cycle goes on");
            f.SetHp(second, 6);
            Hit(f, 1, 6);
            c.That(second.IsDead, "the second follower is dead");
            c.That(f.InProgress && !f.IsEnding, "the priest alone keeps the fight going");
            c.Equal(0, priest.PowerAmount<StrengthPower>(), "the priest gains nothing from their deaths");
            c.Equal(190, priest.Hp, "and loses nothing");
            c.Equal(0, Count(f.Seen(), "\"primary\":true"), "with only primary enemies left the mark is dropped");
            f.EndTurn();
            c.Equal(279, f.Player.Hp, "enemy turn 2: the orb's 8");
            c.Sequence(["attack 3x3"], f.IntentTexts(priest), "turn 3: the priest's cycle goes on");

            f.SetHp(priest, 6);
            Hit(f, 2, 8);
            c.That(f.IsOver && f.Won, "Weak: 8 x 0.75 = 6 kills the priest at 6 HP, and that wins");
        });
    }

    // ---------- Vantom ----------

    private static void VantomCycle(Check c)
    {
        c.Group("Vantom: eleven enemy turns of Ink Blot, Inky Lance, Dismember, Prepare", () =>
        {
            var f = Boss(VantomFight);
            var vantom = f.Enemy(0);

            // Per turn: what is shown, incoming, the player's Block, HP after the enemy turn, Vantom's
            // Strength after it, and how many Wounds the player then has in all piles.
            (string[] Shown, int Incoming, int Block, int Hp, int Strength, int Wounds)[] turns =
            [
                (["attack 7"], 7, 0, 293, 0, 0),
                // Block 8: 6 and 2 are stopped, 4 get through.
                (["attack 6x2"], 12, 8, 289, 0, 0),
                // Block 30 stops the 26; the Wounds come all the same.
                (["attack 26", "statuscard: 3"], 26, 30, 289, 0, 3),
                (["buff"], 0, 0, 289, 2, 3),
                (["attack 9"], 9, 0, 280, 2, 3),
                (["attack 8x2"], 16, 0, 264, 2, 3),
                (["attack 28", "statuscard: 3"], 28, 0, 236, 2, 6),
                (["buff"], 0, 0, 236, 4, 6),
                (["attack 11"], 11, 0, 225, 4, 6),
                (["attack 10x2"], 20, 0, 205, 4, 6),
                (["attack 30", "statuscard: 3"], 30, 0, 175, 4, 9),
            ];
            var turn = 1;
            foreach (var (shown, incoming, block, hp, strength, wounds) in turns)
            {
                c.Sequence(shown, f.IntentTexts(vantom), $"turn {turn}: shown");
                c.Equal(incoming, f.IncomingDamage(), $"turn {turn}: incoming");
                f.SetBlock(f.Player, block);
                f.EndTurn();
                c.Equal(hp, f.Player.Hp, $"enemy turn {turn}: HP");
                c.Equal(strength, vantom.PowerAmount<StrengthPower>(), $"enemy turn {turn}: Strength");
                c.Equal(wounds, Wounds(f), $"enemy turn {turn}: Wounds in the player's piles");
                turn++;
            }

            c.Sequence(
                [
                    "INK_BLOT_MOVE", "INKY_LANCE_MOVE", "DISMEMBER_MOVE", "PREPARE_MOVE", "INK_BLOT_MOVE", "INKY_LANCE_MOVE", "DISMEMBER_MOVE", "PREPARE_MOVE",
                    "INK_BLOT_MOVE", "INKY_LANCE_MOVE", "DISMEMBER_MOVE", "PREPARE_MOVE",
                ],
                vantom.Monster!.MoveLog, "the chosen moves, turn 1 to 12");
            c.Equal(0, f.Rng.MonsterAi.Counter, "no move number was drawn");
            c.Equal(173, vantom.Hp, "Vantom was never hit");
            c.Equal(8, vantom.PowerAmount<SlipperyPower>(), "so Slippery is untouched");
            c.Equal(0, vantom.Block, "it never gains Block");
        });

        c.Group("Vantom: Dismember's three Wounds go to the bottom of the discard pile", () =>
        {
            var f = Boss(VantomFight);
            f.EndTurn();
            f.EndTurn();
            c.Equal(0, Wounds(f), "none before Dismember");
            f.EndTurn();
            // Turn 3 drew 5 of the reshuffled 10, turn 4 the other 5: the discard pile is turn 3's hand and the Wounds.
            c.Equal(8, f.DiscardPile.Count, "turn 4: 5 cards of turn 3 and 3 Wounds");
            c.That(f.DiscardPile.Skip(5).All(card => card.Def.ClassName == "Wound"), "the Wounds are the last three");
            c.That(f.DiscardPile.Take(5).All(card => card.Def.ClassName != "Wound"), "after the hand that was discarded before the enemy turn");
            var wound = f.DiscardPile[7];
            c.Equal("Wound", wound.Title, "its title");
            c.Equal(CardType.Status, wound.Type, "a Status card");
            c.Equal("-", f.CostText(wound), "without a cost");
            c.That(!f.CanPlay(wound, out var reason) && reason == UnplayableReason.HasUnplayableKeyword, "Unplayable");
            c.That(!wound.IsUpgradable, "cannot be upgraded");
        });
    }

    private static void VantomRules(Check c)
    {
        c.Group("Vantom: Slippery cuts each of the first 8 losses of HP to 1, whatever causes them", () =>
        {
            var f = Boss(VantomFight);
            var vantom = f.Enemy(0);

            f.Play("Strike", 0);
            c.Equal(172, vantom.Hp, "a Strike for 6 takes 1");
            c.Equal(7, vantom.PowerAmount<SlipperyPower>(), "and one stack");
            c.Equal(6m, f.ModifyDamage(vantom, f.Player, 6m, Props.Card, null), "the damage number itself is not changed, only the HP loss");

            f.Play("Bash", 0);
            c.Equal(171, vantom.Hp, "a Bash for 8 takes 1");
            c.Equal(6, vantom.PowerAmount<SlipperyPower>(), "and one stack");
            c.Equal(2, vantom.PowerAmount<VulnerablePower>(), "its Vulnerable lands as usual");

            Hit(f, 0, 4, hits: 3);
            c.Equal(168, vantom.Hp, "three hits take 1 each");
            c.Equal(3, vantom.PowerAmount<SlipperyPower>(), "and a stack each");

            f.Damage(vantom, 20, Props.NonCardUnpowered, f.Player);
            c.Equal(167, vantom.Hp, "unpowered damage, as from a potion: 1");
            c.Equal(2, vantom.PowerAmount<SlipperyPower>(), "and one stack");

            f.Damage(vantom, 3, Props.NonCardHpLoss, f.Player);
            c.Equal(166, vantom.Hp, "plain HP loss: 1");
            c.Equal(1, vantom.PowerAmount<SlipperyPower>(), "and one stack");

            f.SetBlock(vantom, 10);
            f.Damage(vantom, 9, Props.NonCardUnpowered, f.Player);
            c.Equal(166, vantom.Hp, "a fully blocked hit takes nothing");
            c.Equal(1, vantom.PowerAmount<SlipperyPower>(), "and no stack");
            c.Equal(1, vantom.Block, "10 - 9 Block left");

            f.Damage(vantom, 1.5m, Props.NonCardUnpowered, f.Player);
            c.Equal(166, vantom.Hp, "half a point past the Block is below 1: left alone, and lost in the truncation");
            c.Equal(1, vantom.PowerAmount<SlipperyPower>(), "no stack");

            f.Damage(vantom, 50, Props.NonCardUnpowered, f.Player);
            c.Equal(165, vantom.Hp, "the eighth loss: 1");
            c.That(!vantom.HasPower<SlipperyPower>(), "Slippery is used up: 8 HP for 8 stacks");

            f.Damage(vantom, 10, Props.NonCardUnpowered, f.Player);
            c.Equal(155, vantom.Hp, "from then on damage is whole");
            Hit(f, 0, 6);
            c.Equal(146, vantom.Hp, "6 x 1.5 for Vulnerable = 9");
            c.Sequence(["attack 7"], f.IntentTexts(vantom), "none of it changes what Vantom does");
        });

        c.Group("Vantom: answered damage uses a stack per hit received", () =>
        {
            var f = Boss(VantomFight);
            var vantom = f.Enemy(0);
            f.ApplyPower<CheckReflect>(f.Player, 4, f.Player);
            f.EndTurn();
            c.Equal(172, vantom.Hp, "Ink Blot's one hit is answered once: 1 HP");
            c.Equal(7, vantom.PowerAmount<SlipperyPower>(), "one stack");
            f.EndTurn();
            c.Equal(170, vantom.Hp, "Inky Lance's two hits are answered twice: 2 HP");
            c.Equal(5, vantom.PowerAmount<SlipperyPower>(), "two stacks");
            c.Equal(281, f.Player.Hp, "the player took 7 + 6 + 6");
        });
    }

    // ---------- random play ----------

    /// <summary>
    /// What must hold at every decision of a boss fight played with the starter deck and
    /// Armaments (nothing in that deck changes a monster's Strength or removes a Wound).
    /// Returns what is wrong, or null.
    /// </summary>
    private static string? Broken(Fight f)
    {
        // Every chosen move but the last has been performed by the time the player decides.
        static int Done(Creature enemy, string move) => enemy.Monster!.MoveLog.SkipLast(1).Count(id => id == move);

        if (f.Player.HasPower<RingingPower>())
        {
            var started = f.History.CardPlaysStartedThisTurn(f);
            if (started > 1) return $"{started} cards were played on a Ringing turn";
            if (started == 1 && Ids(f).Any(id => id.StartsWith("play:", StringComparison.Ordinal))) return "a second card is offered on a Ringing turn";
            if (f.AllCards.Any(card => card.Affliction != "Ringing")) return "a card has no Ringing mark on a Ringing turn";
        }
        else if (f.AllCards.Any(card => card.Affliction is not null))
        {
            return "a card is marked without Ringing on the player";
        }

        foreach (var enemy in f.Enemies)
        {
            if (enemy.IsDead) continue;
            var log = enemy.Monster!.MoveLog;
            switch (enemy.Monster)
            {
                case CeremonialBeast:
                    // The starter deck cannot take 102 HP on turn 1, so from turn 2 on Plow is there exactly while HP is above 150.
                    if (f.Turn >= 2 && enemy.HasPower<PlowPower>() != enemy.Hp > 150) return $"Plow and {enemy.Hp} HP do not fit";
                    var cry = log.ToList().IndexOf("BEAST_CRY_MOVE");
                    var phaseOne = cry < 0 ? log.Count : cry;
                    string[] cycle = ["BEAST_CRY_MOVE", "STOMP_MOVE", "CRUSH_MOVE"];
                    for (var i = 0; i < log.Count; i++)
                    {
                        var expected = i == 0 ? "STAMP_MOVE" : i < phaseOne ? "PLOW_MOVE" : cycle[(i - phaseOne) % 3];
                        if (log[i] != expected) return $"move {i} is {log[i]}, not {expected}";
                    }
                    if (cry >= 0 && enemy.PowerAmount<StrengthPower>() != 3 * Done(enemy, "CRUSH_MOVE")) return "phase two Strength is not 3 per Crush";
                    break;

                case KinFollower:
                    if (!enemy.IsSecondaryEnemy) return "a follower is not a Minion";
                    if (enemy.PowerAmount<StrengthPower>() != 2 * Done(enemy, "POWER_DANCE_MOVE")) return "a follower's Strength is not 2 per dance";
                    break;

                case KinPriest:
                    if (enemy.PowerAmount<StrengthPower>() != 2 * Done(enemy, "RITUAL_MOVE")) return "the priest's Strength is not 2 per Ritual";
                    break;

                case Vantom:
                    var slippery = enemy.PowerAmount<SlipperyPower>();
                    if (slippery > 0 && enemy.Hp != 173 - (8 - slippery)) return $"Slippery {slippery} with {enemy.Hp} HP";
                    if (enemy.PowerAmount<StrengthPower>() != 2 * Done(enemy, "PREPARE_MOVE")) return "Strength is not 2 per Prepare";
                    if (Wounds(f) != 3 * Done(enemy, "DISMEMBER_MOVE")) return "the Wounds are not 3 per Dismember";
                    break;
            }
        }
        return null;
    }

    private static void RandomPlay(Check c)
    {
        c.Group("Bosses: played at random, no offered action is refused and the rules hold at every decision", () =>
        {
            foreach (var encounter in new[] { Beast, Kin, VantomFight })
            {
                // 80 HP is a real fight; 999 HP lets the long patterns run (the Beast's second phase, many Wounds).
                foreach (var hp in new[] { 80, 999 })
                {
                    var stunnedFights = 0;
                    var ringingTurns = 0;
                    OverSeeds(c, 30, $"{encounter} at {hp} HP", seed =>
                    {
                        string[] deck = [.. CheckKit.StarterInOrder, "Armaments", "Armaments"];
                        var f = new FightBuilder().Deck(deck).Hp(hp, hp).Encounter(encounter).Seed(seed).Start();
                        var dice = new GameRng((ulong)(seed * 7919 + hp));
                        var lastTurn = 0;
                        var sawStun = false;
                        for (var n = 0; n < 6000 && !f.IsOver; n++)
                        {
                            if (Broken(f) is { } problem) return $"turn {f.Turn}: {problem}";
                            if (!sawStun && f.Enemies.Any(e => e.Monster!.IsStunned))
                            {
                                sawStun = true;
                                stunnedFights++;
                            }
                            if (f.Turn != lastTurn)
                            {
                                lastTurn = f.Turn;
                                if (f.Player.HasPower<RingingPower>()) ringingTurns++;
                            }
                            var ids = Ids(f);
                            var useful = ids.Where(id => id != "end_turn").ToList();
                            // One time in six the turn is ended early, so that not every turn is played out.
                            var id = useful.Count == 0 || (ids.Contains("end_turn") && dice.NextInt(6) == 0) ? ids[^1] : useful[dice.NextInt(useful.Count)];
                            if (!f.Execute(id).Ok) return $"turn {f.Turn}: {id} was offered and then refused";
                        }
                        return f.IsOver ? null : "the fight did not end";
                    });
                    if (encounter == Beast && hp == 999)
                        c.That(stunnedFights >= 20 && ringingTurns >= 60, $"the long Beast fights reach the second phase: the stun was seen in {stunnedFights} of 30 fights, with {ringingTurns} Ringing turns");
                }
            }
        });
    }

    // ---------- ten enemy turns of each boss against real cards and potions ----------
    //
    // A second, independent walk through each fight: the Ironclad's own cards (Strike 6, Defend 5,
    // Bash 8 and 2 Vulnerable, Twin Strike 5 twice, Thunderclap 4 to all and 1 Vulnerable, Mangle 15
    // and 10 Strength away until the end of the enemy's turn, Whirlwind 5 to all per energy, Flame
    // Barrier 12 Block and 4 back per hit) and three potions (Fire 20 and Explosive Ampoule 10 to
    // all, neither modified; Shackling takes 7 Strength from all until the end of their turn). The
    // decks are drawn in the order written, five cards a turn, and are never reshuffled. Every
    // number is worked out by hand from the game's classes.

    private const string S = "StrikeIronclad", D = "DefendIronclad";

    private static readonly string[] FiveDefends = [D, D, D, D, D];

    private static string[] DrawnAs(params string[][] hands) => hands.SelectMany(h => h).ToArray();

    private static void Play(Fight f, string card, int? enemy = null, int times = 1)
    {
        for (var i = 0; i < times; i++) f.Play(card, enemy);
    }

    private static int Strength(Creature creature) => creature.PowerAmount<StrengthPower>();

    private static void TenEnemyTurns(Check c)
    {
        c.Group("CeremonialBeast: ten enemy turns against real cards and potions, through the phase change", () =>
        {
            var f = new FightBuilder()
                .DrawOrder(DrawnAs(
                    ["Bash", S, S, D, D],
                    ["TwinStrike", S, S, D, D],
                    ["Mangle", D, D, D, D],
                    [S, S, D, D, D],
                    ["Thunderclap", "TwinStrike", D, D, D],
                    [S, S, D, D, D],
                    [S, S, D, D, D],
                    FiveDefends, FiveDefends, FiveDefends, FiveDefends, FiveDefends))
                .Hp(300, 300).Potions("FirePotion", "ExplosiveAmpoule", "ShacklingPotion").Encounter(Beast).Seed(1).Start();
            var beast = f.Enemy(0);

            // Turn 1: Stamp. No Plow power yet, so nothing watches its HP.
            c.Sequence(["buff"], f.IntentTexts(beast), "turn 1: intent");
            Play(f, "Bash", 0);
            Play(f, S, 0);
            c.Equal(235, beast.Hp, "turn 1: Bash 8, then Strike into Vulnerable 9");
            f.EndTurn();
            c.Equal(300, f.Player.Hp, "enemy turn 1: Stamp deals nothing");
            c.Equal(150, beast.PowerAmount<PlowPower>(), "enemy turn 1: Plow 150");

            c.Sequence(["attack 18", "buff"], f.IntentTexts(beast), "turn 2: intent");
            Play(f, "TwinStrike", 0);
            Play(f, S, 0, times: 2);
            c.Equal(203, beast.Hp, "turn 2: 7 twice and 9 twice into Vulnerable");
            f.EndTurn();
            c.Equal(282, f.Player.Hp, "enemy turn 2: Plow 18");
            c.Equal(2, Strength(beast), "enemy turn 2: 2 Strength after the hit");

            // Turn 3: Mangle. The Plow's own 2 Strength arrives while the 10 are away.
            c.Sequence(["attack 20", "buff"], f.IntentTexts(beast), "turn 3: intent");
            Play(f, "Mangle", 0);
            c.Equal(188, beast.Hp, "turn 3: Mangle 15, Vulnerable is gone");
            c.Sequence(["attack 10", "buff"], f.IntentTexts(beast), "turn 3: 18 + 2 - 10");
            f.EndTurn();
            c.Equal(272, f.Player.Hp, "enemy turn 3: Plow 10");
            c.Equal(4, Strength(beast), "enemy turn 3: -8 + 2 + 10");

            c.Sequence(["attack 22", "buff"], f.IntentTexts(beast), "turn 4: intent");
            f.Do("potion:0>e0");
            Play(f, S, 0, times: 2);
            c.Equal(156, beast.Hp, "turn 4: Fire Potion 20 and two Strikes");
            Play(f, D);
            f.EndTurn();
            c.Equal(255, f.Player.Hp, "enemy turn 4: Plow 22 into 5 Block");
            c.Equal(6, Strength(beast), "enemy turn 4: Strength");

            // Turn 5: Thunderclap stops 2 short of the threshold; Twin Strike's first hit crosses it and its second still lands.
            c.Sequence(["attack 24", "buff"], f.IntentTexts(beast), "turn 5: intent");
            Play(f, "Thunderclap");
            c.Equal(152, beast.Hp, "turn 5: Thunderclap 4");
            c.That(beast.HasPower<PlowPower>() && Strength(beast) == 6, "turn 5: 152 is above the threshold");
            Play(f, "TwinStrike", 0);
            c.Equal(138, beast.Hp, "turn 5: Twin Strike, 7 twice into Vulnerable");
            c.That(!beast.HasPower<PlowPower>(), "turn 5: Plow tripped at 145 and is gone");
            c.That(!beast.HasPower<StrengthPower>(), "turn 5: all 6 Strength are gone");
            c.Sequence(["stunned"], f.IntentTexts(beast), "turn 5: the Plow it showed is replaced by the stun");
            c.Equal(0, f.IncomingDamage(), "turn 5: nothing incoming");
            Play(f, D);
            f.EndTurn();
            c.Equal(255, f.Player.Hp, "enemy turn 5: stunned");
            c.Equal(0, Strength(beast), "enemy turn 5: no Strength comes back");

            c.Sequence(["debuff"], f.IntentTexts(beast), "turn 6: intent");
            Play(f, S, 0);
            c.Equal(132, beast.Hp, "turn 6: Strike 6");
            f.EndTurn();
            c.Equal(255, f.Player.Hp, "enemy turn 6: Beast Cry deals nothing");
            c.That(f.Player.HasPower<RingingPower>(), "enemy turn 6: Ringing");

            // Turn 7: one card, and the potion on top of it.
            c.Sequence(["attack 15"], f.IntentTexts(beast), "turn 7: intent");
            c.That(f.AllCards.All(card => card.Affliction == "Ringing"), "turn 7: every card is marked");
            c.Equal(5, Ids(f).Count(id => id.StartsWith("play:", StringComparison.Ordinal)), "turn 7: any of the five cards may be the one");
            Play(f, D);
            c.Equal(0, Ids(f).Count(id => id.StartsWith("play:", StringComparison.Ordinal)), "turn 7: after it no card can be played");
            c.Equal(2, f.Energy, "turn 7: with 2 energy left");
            f.Do("potion:1");
            c.Equal(122, beast.Hp, "turn 7: the Explosive Ampoule is not stopped, 10");
            f.EndTurn();
            c.Equal(245, f.Player.Hp, "enemy turn 7: Stomp 15 into 5 Block");
            c.That(!f.Player.HasPower<RingingPower>(), "enemy turn 7: Ringing is gone");

            c.Sequence(["attack 17", "buff"], f.IntentTexts(beast), "turn 8: intent");
            c.That(f.AllCards.All(card => card.Affliction is null), "turn 8: no card is marked");
            Play(f, D, times: 3);
            f.EndTurn();
            c.Equal(243, f.Player.Hp, "enemy turn 8: Crush 17 into 15 Block");
            c.Equal(3, Strength(beast), "enemy turn 8: 3 Strength");

            c.Sequence(["debuff"], f.IntentTexts(beast), "turn 9: intent");
            f.EndTurn();
            c.That(f.Player.HasPower<RingingPower>(), "enemy turn 9: Ringing again");

            // Turn 10: the Shackling Potion on the Ringing turn.
            c.Sequence(["attack 18"], f.IntentTexts(beast), "turn 10: intent");
            f.Do("potion:2");
            c.Sequence(["attack 11"], f.IntentTexts(beast), "turn 10: 15 + 3 - 7");
            Play(f, D);
            f.EndTurn();
            c.Equal(237, f.Player.Hp, "enemy turn 10: Stomp 11 into 5 Block");
            c.Equal(3, Strength(beast), "enemy turn 10: the 7 Strength are back");

            c.Equal(11, f.Turn, "turn 11");
            c.Sequence(["attack 20", "buff"], f.IntentTexts(beast), "turn 11: intent");
            c.Equal(122, beast.Hp, "its HP");
            c.Sequence(
                ["STAMP_MOVE", "PLOW_MOVE", "PLOW_MOVE", "PLOW_MOVE", "STUNNED", "BEAST_CRY_MOVE", "STOMP_MOVE", "CRUSH_MOVE", "BEAST_CRY_MOVE", "STOMP_MOVE"],
                Performed(f), "the moves performed, enemy turn 1 to 10");
        });

        c.Group("The Kin: ten enemy turns against real cards and potions", () =>
        {
            var f = new FightBuilder()
                .DrawOrder(DrawnAs(
                    ["Bash", S, S, D, D],
                    ["TwinStrike", D, D, D, D],
                    [S, S, "Thunderclap", D, D],
                    ["Whirlwind", D, D, D, D],
                    ["Mangle", D, D, D, D],
                    [S, S, S, D, D],
                    FiveDefends, FiveDefends, FiveDefends, FiveDefends, FiveDefends, FiveDefends))
                .Hp(300, 300).Potions("FirePotion", "ShacklingPotion").Encounter(Kin).Seed(1).Start();
            var (first, second, priest) = (f.Enemy(0), f.Enemy(1), f.Enemy(2));
            f.SetHp(first, 58);
            f.SetHp(second, 58);

            void Shown(int turn, string[]? e0, string[]? e1, string[] e2, int incoming)
            {
                if (e0 is not null) c.Sequence(e0, f.IntentTexts(first), $"turn {turn}: the first follower shows");
                if (e1 is not null) c.Sequence(e1, f.IntentTexts(second), $"turn {turn}: the second follower shows");
                c.Sequence(e2, f.IntentTexts(priest), $"turn {turn}: the priest shows");
                c.Equal(incoming, f.IncomingDamage(), $"turn {turn}: incoming damage");
            }

            Shown(1, ["buff"], ["attack 5"], ["attack 8", "debuff"], 13);
            Play(f, "Bash", 2);
            c.Equal(182, priest.Hp, "turn 1: Bash 8");
            Play(f, D);
            f.EndTurn();
            c.Equal(292, f.Player.Hp, "enemy turn 1: 5 into 5 Block, then the orb's 8");
            c.Equal(1, f.Player.PowerAmount<FrailPower>(), "enemy turn 1: Frail");

            // Turn 2, Frail: a Defend gives 3.75 Block, and two of them 6.
            Shown(2, ["attack 7"], ["attack 2x2"], ["attack 8", "debuff"], 19);
            Play(f, "TwinStrike", 2);
            c.Equal(168, priest.Hp, "turn 2: Twin Strike, 7 twice into Vulnerable");
            Play(f, D, times: 2);
            c.Equal(6, f.Player.Block, "turn 2: two Frail Defends, 3.75 + 3.75 kept as 6");
            f.EndTurn();
            c.Equal(279, f.Player.Hp, "enemy turn 2: 7 into 6 Block, 2 twice, 8");
            c.That(!f.Player.HasPower<FrailPower>(), "enemy turn 2: Frail wore off");
            c.Equal(1, f.Player.PowerAmount<WeakPower>(), "enemy turn 2: Weak");

            // Turn 3, Weak: Strike deals 4.5, Thunderclap 3.
            Shown(3, ["attack 4x2"], ["buff"], ["attack 3x3"], 17);
            Play(f, S, 0);
            c.Equal(54, first.Hp, "turn 3: a Weak Strike, 4.5, so 4");
            Play(f, "Thunderclap");
            c.Sequence([51, 55, 165], new[] { first.Hp, second.Hp, priest.Hp }, "turn 3: a Weak Thunderclap, 3 to each");
            Play(f, D);
            c.Equal(5, f.Player.Block, "turn 3: Defend is whole again");
            f.EndTurn();
            c.Equal(267, f.Player.Hp, "enemy turn 3: 4 twice into 5 Block, then 3 three times");
            c.That(!f.Player.HasPower<WeakPower>(), "enemy turn 3: Weak wore off");
            c.Sequence([2, 2, 0], new[] { Strength(first), Strength(second), Strength(priest) }, "enemy turn 3: Strength");

            // Turn 4: Whirlwind for 3 on all three.
            Shown(4, ["buff"], ["attack 7"], ["buff"], 7);
            Play(f, "Whirlwind");
            c.Sequence([36, 40, 150], new[] { first.Hp, second.Hp, priest.Hp }, "turn 4: Whirlwind, 5 three times to each");
            f.EndTurn();
            c.Equal(260, f.Player.Hp, "enemy turn 4: 7");
            c.Sequence([4, 2, 2], new[] { Strength(first), Strength(second), Strength(priest) }, "enemy turn 4: Strength");

            // Turn 5: Mangle on the first follower.
            Shown(5, ["attack 9"], ["attack 4x2"], ["attack 10", "debuff"], 27);
            Play(f, "Mangle", 0);
            c.Equal(21, first.Hp, "turn 5: Mangle 15");
            Shown(5, ["attack 0"], ["attack 4x2"], ["attack 10", "debuff"], 18);
            f.EndTurn();
            c.Equal(242, f.Player.Hp, "enemy turn 5: 0, 4 twice, 10");
            c.Equal(4, Strength(first), "enemy turn 5: the follower's Strength is back");
            c.Equal(1, f.Player.PowerAmount<FrailPower>(), "enemy turn 5: Frail");

            // Turn 6: the first follower dies. The priest lives, so the fight goes on.
            Shown(6, ["attack 6x2"], ["buff"], ["attack 10", "debuff"], 22);
            f.Do("potion:0>e0");
            c.Equal(1, first.Hp, "turn 6: Fire Potion 20");
            Play(f, S, 0);
            c.That(first.IsDead && !f.IsOver, "turn 6: the follower is dead and the fight goes on");
            Play(f, S, 2, times: 2);
            c.Equal(138, priest.Hp, "turn 6: two Strikes on the priest");
            Shown(6, null, ["buff"], ["attack 10", "debuff"], 10);
            f.EndTurn();
            c.Equal(232, f.Player.Hp, "enemy turn 6: the orb's 10");
            c.Equal(1, f.Player.PowerAmount<WeakPower>(), "enemy turn 6: Weak");
            c.That(!f.Player.HasPower<FrailPower>(), "enemy turn 6: Frail wore off");

            Shown(7, null, ["attack 9"], ["attack 5x3"], 24);
            Play(f, D, times: 3);
            c.Equal(15, f.Player.Block, "turn 7: three whole Defends");
            f.EndTurn();
            c.Equal(223, f.Player.Hp, "enemy turn 7: 9 and 5 three times into 15 Block");

            // Turn 8: the Shackling Potion on both. The priest's Ritual arrives while its Strength is down.
            Shown(8, null, ["attack 6x2"], ["buff"], 12);
            f.Do("potion:1");
            Shown(8, null, ["attack 0x2"], ["buff"], 0);
            c.Sequence([-3, -5], new[] { Strength(second), Strength(priest) }, "turn 8: 4 - 7 and 2 - 7 Strength");
            f.EndTurn();
            c.Equal(223, f.Player.Hp, "enemy turn 8: two hits of 0");
            c.Sequence([4, 4], new[] { Strength(second), Strength(priest) }, "enemy turn 8: back, and the priest's 2 more");

            Shown(9, null, ["buff"], ["attack 12", "debuff"], 12);
            Play(f, D, times: 2);
            f.EndTurn();
            c.Equal(221, f.Player.Hp, "enemy turn 9: 12 into 10 Block");
            c.Equal(6, Strength(second), "enemy turn 9: the follower's dance");

            // Turn 10, Frail again: three Defends are 9 Block.
            Shown(10, null, ["attack 11"], ["attack 12", "debuff"], 23);
            Play(f, D, times: 3);
            c.Equal(9, f.Player.Block, "turn 10: three Frail Defends");
            f.EndTurn();
            c.Equal(207, f.Player.Hp, "enemy turn 10: 11 into 9 Block, then 12");

            c.Equal(11, f.Turn, "turn 11");
            Shown(11, null, ["attack 8x2"], ["attack 7x3"], 37);
            c.Sequence([58 - 15 - 3, 138], new[] { second.Hp, priest.Hp }, "their HP");

            f.RunAsAction(x => x.Damage(priest, 999, Props.NonCardUnpowered, x.Player));
            c.That(second.IsDead, "the priest's death takes the last follower with it");
            c.That(f.IsOver && f.Won, "and the fight is won");
            c.Equal(213, f.Player.Hp, "Burning Blood heals 6");
        });

        c.Group("Vantom: ten enemy turns against real cards and potions, through all of Slippery", () =>
        {
            var f = new FightBuilder()
                .DrawOrder(DrawnAs(
                    ["TwinStrike", S, S, D, D],
                    ["Whirlwind", D, D, D, D],
                    ["FlameBarrier", S, D, D, D],
                    ["Bash", S, D, D, D],
                    ["Mangle", D, D, D, D],
                    [S, S, S, D, D],
                    FiveDefends,
                    ["TwinStrike", D, D, D, D],
                    FiveDefends, FiveDefends, FiveDefends, FiveDefends))
                .Hp(300, 300).Potions("ExplosiveAmpoule", "FirePotion", "ShacklingPotion").Encounter(VantomFight).Seed(1).Start();
            var vantom = f.Enemy(0);

            // Turn 1: three hits, three stacks, 1 HP each.
            c.Sequence(["attack 7"], f.IntentTexts(vantom), "turn 1: intent");
            Play(f, "TwinStrike", 0);
            Play(f, S, 0);
            c.Equal(170, vantom.Hp, "turn 1: Twin Strike and Strike, 1 HP per hit");
            c.Equal(5, vantom.PowerAmount<SlipperyPower>(), "turn 1: three stacks used");
            Play(f, D);
            f.EndTurn();
            c.Equal(298, f.Player.Hp, "enemy turn 1: Ink Blot 7 into 5 Block");

            // Turn 2: the potion uses a stack like any hit; Whirlwind uses one per hit.
            c.Sequence(["attack 6x2"], f.IntentTexts(vantom), "turn 2: intent");
            f.Do("potion:0");
            c.Equal(169, vantom.Hp, "turn 2: the Explosive Ampoule's 10 is cut to 1");
            c.Equal(4, vantom.PowerAmount<SlipperyPower>(), "turn 2: and uses a stack");
            Play(f, "Whirlwind");
            c.Equal(166, vantom.Hp, "turn 2: Whirlwind for 3, 1 HP per hit");
            c.Equal(1, vantom.PowerAmount<SlipperyPower>(), "turn 2: one stack left");
            f.EndTurn();
            c.Equal(286, f.Player.Hp, "enemy turn 2: Inky Lance 6 twice");

            // Turn 3: the last stack goes to a Strike, so Flame Barrier's answer to Dismember lands whole.
            c.Sequence(["attack 26", "statuscard: 3"], f.IntentTexts(vantom), "turn 3: intent");
            Play(f, "FlameBarrier");
            Play(f, S, 0);
            c.Equal(165, vantom.Hp, "turn 3: Strike, 1 HP");
            c.That(!vantom.HasPower<SlipperyPower>(), "turn 3: Slippery is used up");
            f.EndTurn();
            c.Equal(272, f.Player.Hp, "enemy turn 3: Dismember 26 into 12 Block");
            c.Equal(161, vantom.Hp, "enemy turn 3: 4 back from Flame Barrier");
            c.Equal(3, Wounds(f), "enemy turn 3: three Wounds");
            c.Equal(3, f.DiscardPile.Count(card => card.Def.ClassName == "Wound"), "enemy turn 3: in the discard pile");

            c.Sequence(["buff"], f.IntentTexts(vantom), "turn 4: intent");
            Play(f, "Bash", 0);
            Play(f, S, 0);
            c.Equal(144, vantom.Hp, "turn 4: Bash 8, then Strike into Vulnerable 9");
            f.EndTurn();
            c.Equal(272, f.Player.Hp, "enemy turn 4: Prepare deals nothing");
            c.Equal(2, Strength(vantom), "enemy turn 4: 2 Strength");

            c.Sequence(["attack 9"], f.IntentTexts(vantom), "turn 5: intent");
            Play(f, "Mangle", 0);
            c.Equal(122, vantom.Hp, "turn 5: Mangle into Vulnerable, 22.5, so 22");
            c.Sequence(["attack 0"], f.IntentTexts(vantom), "turn 5: 7 + 2 - 10 is no damage");
            f.EndTurn();
            c.Equal(272, f.Player.Hp, "enemy turn 5: Ink Blot for 0");
            c.Equal(2, Strength(vantom), "enemy turn 5: the Strength is back");

            c.Sequence(["attack 8x2"], f.IntentTexts(vantom), "turn 6: intent");
            f.Do("potion:1>e0");
            Play(f, S, 0, times: 3);
            c.Equal(84, vantom.Hp, "turn 6: Fire Potion 20 and three Strikes");
            f.EndTurn();
            c.Equal(256, f.Player.Hp, "enemy turn 6: Inky Lance 8 twice");

            c.Sequence(["attack 28", "statuscard: 3"], f.IntentTexts(vantom), "turn 7: intent");
            Play(f, D, times: 3);
            f.EndTurn();
            c.Equal(243, f.Player.Hp, "enemy turn 7: Dismember 28 into 15 Block");
            c.Equal(6, Wounds(f), "enemy turn 7: three more Wounds");

            c.Sequence(["buff"], f.IntentTexts(vantom), "turn 8: intent");
            Play(f, "TwinStrike", 0);
            c.Equal(74, vantom.Hp, "turn 8: Twin Strike, 5 twice");
            f.EndTurn();
            c.Equal(4, Strength(vantom), "enemy turn 8: 4 Strength");

            c.Sequence(["attack 11"], f.IntentTexts(vantom), "turn 9: intent");
            f.Do("potion:2");
            c.Sequence(["attack 4"], f.IntentTexts(vantom), "turn 9: 7 + 4 - 7");
            f.EndTurn();
            c.Equal(239, f.Player.Hp, "enemy turn 9: Ink Blot 4");
            c.Equal(4, Strength(vantom), "enemy turn 9: the Strength is back");

            c.Sequence(["attack 10x2"], f.IntentTexts(vantom), "turn 10: intent");
            Play(f, D, times: 3);
            f.EndTurn();
            c.Equal(234, f.Player.Hp, "enemy turn 10: Inky Lance 10 twice into 15 Block");

            c.Equal(11, f.Turn, "turn 11");
            c.Sequence(["attack 30", "statuscard: 3"], f.IntentTexts(vantom), "turn 11: intent");
            c.Equal(74, vantom.Hp, "its HP");
            c.Sequence(
                ["INK_BLOT_MOVE", "INKY_LANCE_MOVE", "DISMEMBER_MOVE", "PREPARE_MOVE", "INK_BLOT_MOVE", "INKY_LANCE_MOVE", "DISMEMBER_MOVE", "PREPARE_MOVE", "INK_BLOT_MOVE", "INKY_LANCE_MOVE"],
                Performed(f), "the moves performed, enemy turn 1 to 10");
        });
    }

    // ---------- copies ----------

    private static void Copies(Check c)
    {
        c.Group("Bosses: the same seed plays the same fight, and a copy taken on the way continues like the original", () =>
        {
            foreach (var encounter in new[] { Beast, Kin, VantomFight })
            {
                foreach (var seed in new[] { 3, 11 })
                {
                    var whole = CheckKit.PlayOut(Boss(encounter, seed));
                    c.That(whole == CheckKit.PlayOut(Boss(encounter, seed)), $"{encounter}, seed {seed}: two runs differ");
                    foreach (var after in new[] { 9, 40, 90 })
                    {
                        var f = Boss(encounter, seed);
                        CheckKit.PlayOut(f, after);
                        var copy = f.Clone();
                        c.That(CheckKit.PlayOut(f) == CheckKit.PlayOut(copy), $"{encounter}, seed {seed}: a copy after {after} actions went another way");
                    }
                }
            }
        });

        c.Group("CeremonialBeast: copies taken while stunned and on the Ringing turn behave like the original", () =>
        {
            var f = Boss(Beast);
            f.EndTurn();
            f.SetHp(f.Enemy(0), 151);
            Hit(f, 0, 1);
            var stunned = f.Clone();
            c.Sequence(["stunned"], stunned.IntentTexts(stunned.Enemy(0)), "the copy is stunned too");
            f.EndTurn();
            stunned.EndTurn();
            c.Sequence(["debuff"], stunned.IntentTexts(stunned.Enemy(0)), "the copy's stun leads to Beast Cry");
            c.Equal(f.Seen(), stunned.Seen(), "turn 3 looks the same in both");
            f.EndTurn();
            stunned.EndTurn();
            c.Equal(f.Seen(), stunned.Seen(), "the Ringing turn looks the same in both");

            f.Do(Ids(f)[0]);
            var ringing = f.Clone();
            c.Sequence(["end_turn"], Ids(ringing), "a copy taken after the turn's one card is shut as well");
            c.That(ringing.AllCards.All(card => card.Affliction == "Ringing"), "its cards carry the mark");
            f.EndTurn();
            ringing.EndTurn();
            c.That(ringing.AllCards.All(card => card.Affliction is null), "and lose it when its Ringing ends");
            c.Equal(f.Seen(), ringing.Seen(), "the next turn looks the same in both");
            c.Equal(1, Count(stunned.Seen(), "\"name\":\"Ringing\""), "the first copy, left on its Ringing turn, was not touched by any of this");
        });
    }
}
