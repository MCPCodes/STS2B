namespace SpireMind.Arena;

/// <summary>
/// Scripted fights against the monsters of MonstersA.cs. The expected numbers are worked out by
/// hand from the game's classes (CubexConstruct, Fogmog, EyeWithTeeth, IllusionPower, Inklet,
/// Mawler and their encounters, v0.107.1), not read off the simulator. Where a monster chooses
/// at random, the seed of the fight is picked by reading the MonsterAi stream directly and
/// walking the game's branch rule over it, so every script runs a known path.
/// </summary>
public sealed class MonstersAChecks : IArenaChecks
{
    private const string Rip = "RIP_AND_TEAR_MOVE";
    private const string Roar = "ROAR_MOVE";
    private const string Claw = "CLAW_MOVE";

    // ---------- cards and powers that exist only for these checks ----------

    /// <summary>3 damage four times to one enemy, free.</summary>
    private static readonly CardDef Flurry = new("CheckFlurryA", "Flurry", CardType.Attack, CardRarity.Common, 0, TargetType.AnyEnemy)
    {
        Play = c => c.Attack(3).Hits(4).Execute(),
    };

    /// <summary>4 damage to every enemy, free.</summary>
    private static readonly CardDef Sweep = new("CheckSweepA", "Sweep", CardType.Attack, CardRarity.Common, 0, TargetType.AllEnemies)
    {
        Play = c => c.Attack(4).Execute(),
    };

    /// <summary>A temporary Strength-down, the kind of Debuff an Illusion's owner keeps through its death.</summary>
    private sealed class CheckShackle : TemporaryStrengthPower
    {
        public override string Name => "Shackle";
        protected override bool IsPositive => false;
    }

    /// <summary>Once, just before the enemy turn ends, deals its amount to every minion that can be
    /// hit: a way to bring the Eye to 0 HP after it has acted.</summary>
    private sealed class LateBlow : Power
    {
        public override string Name => "Late Blow";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";

        public override void BeforeSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
        {
            if (side != Side.Enemy) return;
            foreach (var enemy in f.HittableEnemies.Where(e => e.IsSecondaryEnemy)) f.Damage(enemy, Amount, Props.NonCardUnpowered, Owner);
            f.RemovePower(this);
        }
    }

    // ---------- helpers ----------

    /// <summary>The starter order six times over: twelve turns without a reshuffle, so the hand of
    /// an odd turn is Strike, Strike, Defend, Defend, Bash and of an even turn Strike, Strike,
    /// Strike, Defend, Defend, and cards a monster adds stay where it put them.</summary>
    private static readonly string[] LongDeck = Enumerable.Repeat(CheckKit.StarterInOrder, 6).SelectMany(x => x).ToArray();

    /// <summary>A fight against a registered encounter with <see cref="LongDeck"/>. The default 300 HP
    /// outlasts every walk; a lower value keeps the usual maximum of 80, so Burning Blood shows.</summary>
    private static Fight Versus(string encounter, int seed = 1, int hp = 300) =>
        new FightBuilder().DrawOrder(LongDeck).Hp(hp, Math.Max(hp, 80)).Encounter(encounter).Seed(seed).Start();

    /// <summary>The MonsterAi stream a fight with this seed starts with.</summary>
    private static GameRng Ai(int seed) => RngSet.For(seed, "monster_ai");

    /// <summary>The first seed, counting from 1, whose MonsterAi stream makes <paramref name="wanted"/> true.</summary>
    private static int SeedWhere(Func<GameRng, bool> wanted)
    {
        for (var seed = 1; seed <= 500_000; seed++)
            if (wanted(Ai(seed))) return seed;
        throw new InvalidOperationException("No seed gives the wanted moves.");
    }

    /// <summary>Fogmog's branch by the game's rule: one float below 0.4 + 0.6; Swipe-again is listed
    /// first with 0.4, so it is taken when the float is at or below that.</summary>
    private static bool FogmogSwipesAgain(GameRng ai) => ai.NextFloat(0.4f + 0.6f) - 0.4f <= 0f;

    /// <summary>Inklet's branch: one float below 2; Piercing Gaze is listed first with weight 1.</summary>
    private static bool InkletGazes(GameRng ai) => ai.NextFloat(2f) - 1f <= 0f;

    /// <summary>Mawler's next move by the game's rule, from its log so far: Rip and Tear, Roar and
    /// Claw in that order, weight 1 each; an attack is shut out right after itself, Roar once it is
    /// in the log. One float below the sum of the open weights, then the walk down the list.</summary>
    private static string MawlerNext(IReadOnlyList<string> log, GameRng ai)
    {
        var rip = log[^1] == Rip ? 0f : 1f;
        var roar = log.Contains(Roar) ? 0f : 1f;
        var claw = log[^1] == Claw ? 0f : 1f;
        var roll = ai.NextFloat(rip + roar + claw);
        if ((roll -= rip) <= 0f) return Rip;
        if ((roll -= roar) <= 0f) return Roar;
        return Claw;
    }

    /// <summary>Mawler's log after <paramref name="choices"/> choices: it always opens with Claw.</summary>
    private static List<string> MawlerLog(GameRng ai, int choices)
    {
        var log = new List<string> { Claw };
        for (var i = 0; i < choices; i++) log.Add(MawlerNext(log, ai));
        return log;
    }

    private static int Count(Fight f, string cardClassName) => f.AllCards.Count(card => card.Def.ClassName == cardClassName);

    private static List<string> MovesPerformed(Fight f, int round) =>
        f.History.Entries.Where(e => e.Kind == HistoryKind.MonsterPerformedMove && e.Round == round).Select(e => e.Name!).ToList();

    private static string NextMove(Creature enemy) => enemy.Monster!.NextMove!.Id;

    /// <summary>Plays a fight to its end by a fixed rule and returns every action and the last snapshot.
    /// The rule takes the last play offered, which aims at the highest enemy id (the Eye whenever it
    /// can be hit), or the first, which aims at e0. With <paramref name="throughCopies"/> every
    /// action is made on a fresh copy of the fight, so anything a copy loses shows as a difference.</summary>
    private static string PlayOut(Fight f, bool lastPlay, bool throughCopies)
    {
        var log = new System.Text.StringBuilder();
        for (var n = 0; n < 5000 && !f.IsOver; n++)
        {
            if (throughCopies) f = f.Clone();
            var plays = f.LegalActions().Where(a => a.Id.StartsWith("play:", StringComparison.Ordinal)).ToList();
            var pick = plays.Count == 0 ? "end_turn" : lastPlay ? plays[^1].Id : plays[0].Id;
            log.Append(pick).Append('|');
            f.Do(pick);
        }
        return log.Append(f.IsOver ? "over|" : "not over|").Append(f.TimedOut ? "timed out|" : "").Append(f.Seen()).ToString();
    }

    public void Run(Check c)
    {
        Registered(c);
        CubexConstructChecks(c);
        FogmogChecks(c);
        EyeWithTeethChecks(c);
        InkletChecks(c);
        MawlerChecks(c);
        Compositions(c);
    }

    private static void Registered(Check c)
    {
        c.Group("the registry holds this file's content under the game's class names", () =>
        {
            var r = Content.Registry;
            (string Class, string Name, int Min, int Max)[] monsters =
            [
                ("CubexConstruct", "Cubex Construct", 65, 65), ("Fogmog", "Fogmog", 74, 74), ("EyeWithTeeth", "Eye with Teeth", 6, 6),
                ("Inklet", "Inklet", 11, 17), ("Mawler", "Mawler", 72, 72),
            ];
            foreach (var (cls, name, min, max) in monsters)
            {
                c.That(r.Has(ContentKind.Monster, cls), $"monster {cls}");
                var monster = r.NewMonster(cls);
                c.Equal(name, monster.Name, $"{cls}: the game's English name");
                c.Equal(min, monster.MinHp, $"{cls}: lowest HP");
                c.Equal(max, monster.MaxHp, $"{cls}: highest HP");
            }
            foreach (var cls in new[] { "CubexConstructNormal", "FogmogNormal", "InkletsNormal", "MawlerNormal" })
            {
                c.That(r.Has(ContentKind.Encounter, cls), $"encounter {cls}");
                var encounter = r.NewEncounter(cls);
                c.Equal(RoomType.Monster, encounter.RoomType, $"{cls}: a monster room");
                c.That(!encounter.IsWeak, $"{cls}: not one of the weak encounters");
            }
            c.That(r.Has(ContentKind.Power, "IllusionPower"), "power IllusionPower");
            var illusion = (IllusionPower)r.NewPower("IllusionPower");
            c.Equal("Illusion", illusion.Name, "Illusion: name");
            c.Equal(PowerType.Buff, illusion.Type, "Illusion: a Buff");
            c.Equal(PowerStackType.Single, illusion.StackType, "Illusion: does not stack");
        });
    }

    // ---------- Cubex Construct ----------

    private static void CubexConstructChecks(Check c)
    {
        c.Group("Cubex Construct: start of the fight", () =>
        {
            var f = Versus("CubexConstructNormal");
            var cubex = f.Enemy(0);
            c.Equal("Cubex Construct", cubex.Name, "name");
            c.Equal(65, cubex.Hp, "65 HP");
            c.Equal(65, cubex.MaxHp, "65 max HP");
            c.Equal(0, cubex.Block, "the 13 Block asked for during set-up never arrives");
            c.Sequence(["ArtifactPower"], cubex.Powers.Select(p => p.ClassName), "its only starting power");
            c.Equal(1, cubex.PowerAmount<ArtifactPower>(), "Artifact 1");
            c.That(ReferenceEquals(cubex.GetPower<ArtifactPower>()!.Applier, cubex), "applied by itself");
            c.Sequence(["buff"], f.IntentTexts(cubex), "opens with Charge Up");
            c.Equal(0, f.IncomingDamage(), "nothing incoming on turn 1");
            c.Equal(0, f.Rng.MonsterAi.Counter, "no MonsterAi draw");
        });

        c.Group("Cubex Construct: eight turns of the fixed cycle, no cards played", () =>
        {
            var f = Versus("CubexConstructNormal");
            var cubex = f.Enemy(0);
            // Blast: 7 + Strength, then +2 Strength. Expel: (5 + Strength) twice, no Strength.
            (string Move, string[] Intents, int Incoming, int HpAfter, int StrengthAfter)[] turns =
            [
                ("CHARGE_UP_MOVE", ["buff"], 0, 300, 2),
                ("REPEATER_BLAST_MOVE", ["attack 9", "buff"], 9, 291, 4),
                ("REPEATER_BLAST_MOVE_2", ["attack 11", "buff"], 11, 280, 6),
                ("EXPEL_MOVE", ["attack 11x2"], 22, 258, 6),
                ("REPEATER_BLAST_MOVE", ["attack 13", "buff"], 13, 245, 8),
                ("REPEATER_BLAST_MOVE_2", ["attack 15", "buff"], 15, 230, 10),
                ("EXPEL_MOVE", ["attack 15x2"], 30, 200, 10),
                ("REPEATER_BLAST_MOVE", ["attack 17", "buff"], 17, 183, 12),
            ];
            for (var t = 0; t < turns.Length; t++)
            {
                var (move, intents, incoming, hpAfter, strengthAfter) = turns[t];
                var at = $"turn {t + 1}";
                c.Equal(t + 1, f.Turn, $"{at}: turn number");
                c.Equal(move, NextMove(cubex), $"{at}: move");
                c.Sequence(intents, f.IntentTexts(cubex), $"{at}: intents");
                c.Equal(incoming, f.IncomingDamage(), $"{at}: incoming damage");
                f.EndTurn();
                c.Equal(hpAfter, f.Player.Hp, $"{at}: player HP after the enemy turn");
                c.Equal(strengthAfter, cubex.PowerAmount<StrengthPower>(), $"{at}: Strength after the enemy turn");
                c.Equal(0, cubex.Block, $"{at}: it never gains Block");
            }
            c.Equal(1, cubex.PowerAmount<ArtifactPower>(), "its own Strength gains do not use up Artifact");
            c.Equal(65, cubex.Hp, "untouched");
            c.Equal(0, f.Player.Powers.Count, "it puts no power on the player");
            c.Equal(0, f.DiscardPile.Count(card => card.Type == CardType.Status), "and adds no cards");
            c.Equal(0, f.Rng.MonsterAi.Counter, "the whole cycle draws no MonsterAi number");
            c.Sequence(
                ["CHARGE_UP_MOVE", "REPEATER_BLAST_MOVE", "REPEATER_BLAST_MOVE_2", "EXPEL_MOVE", "REPEATER_BLAST_MOVE", "REPEATER_BLAST_MOVE_2", "EXPEL_MOVE",
                    "REPEATER_BLAST_MOVE", "REPEATER_BLAST_MOVE_2"],
                cubex.Monster!.MoveLog, "Charge Up is used once; then Blast, Blast, Expel");
        });

        c.Group("Cubex Construct: its hits against Block, one damage event per hit", () =>
        {
            var f = Versus("CubexConstructNormal");
            var cubex = f.Enemy(0);
            f.Play("Defend").Play("Defend").EndTurn();
            c.Equal(300, f.Player.Hp, "turn 1: Charge Up does no damage");
            c.Equal(0, f.Player.Block, "the Block of turn 1 is gone on turn 2");

            f.Play("Defend").Play("Defend");
            c.Sequence(["attack 9", "buff"], f.IntentTexts(cubex), "turn 2: the intent does not count the player's Block");
            f.EndTurn();
            c.Equal(300, f.Player.Hp, "turn 2: 9 into 10 Block");
            c.Equal(4, cubex.PowerAmount<StrengthPower>(), "turn 2: Strength still rises when the hit was blocked");

            f.Play("Defend").EndTurn();
            c.Equal(294, f.Player.Hp, "turn 3: 11 into 5 Block");

            f.Play("Defend").Play("Defend").EndTurn();
            c.Equal(282, f.Player.Hp, "turn 4: 11 twice into 10 Block: the first hit leaves 1 through, the second lands whole");
        });

        c.Group("Cubex Construct: Artifact stops the first debuff, whoever applies it", () =>
        {
            var f = Versus("CubexConstructNormal");
            var cubex = f.Enemy(0);
            f.Play("Bash", 0);
            c.Equal(57, cubex.Hp, "Bash: 8 damage");
            c.That(!cubex.HasPower<VulnerablePower>(), "its Vulnerable is negated");
            c.That(!cubex.HasPower<ArtifactPower>(), "and Artifact is used up");
            f.Play("Strike", 0);
            c.Equal(51, cubex.Hp, "Strike: a plain 6");
            f.EndTurn().EndTurn();
            c.Equal(291, f.Player.Hp, "turn 2: 9 damage");
            f.Play("Bash", 0);
            c.Equal(43, cubex.Hp, "second Bash: 8 damage");
            c.Equal(2, cubex.PowerAmount<VulnerablePower>(), "now Vulnerable lands");
            f.Play("Strike", 0);
            c.Equal(34, cubex.Hp, "Strike into Vulnerable: 9");
        });

        c.Group("Cubex Construct: live intent numbers under Weak and Vulnerable, each hit rounded down on its own", () =>
        {
            var f = Versus("CubexConstructNormal");
            var cubex = f.Enemy(0);
            f.ApplyPower<WeakPower>(cubex, 2, f.Player);
            c.That(!cubex.HasPower<WeakPower>() && !cubex.HasPower<ArtifactPower>(), "the first Weak only removes Artifact");
            f.ApplyPower<WeakPower>(cubex, 2, f.Player);
            c.Equal(2, cubex.PowerAmount<WeakPower>(), "the second lands");
            f.EndTurn();
            // Weak on a monster ticks at the end of every enemy turn: 1 left on turn 2.
            c.Equal(1, cubex.PowerAmount<WeakPower>(), "Weak 1 on turn 2");
            c.Sequence(["attack 6", "buff"], f.IntentTexts(cubex), "turn 2: (7 + 2) x 0.75 = 6.75");
            f.EndTurn();
            c.Equal(294, f.Player.Hp, "turn 2: 6 damage");
            c.That(!cubex.HasPower<WeakPower>(), "Weak is gone on turn 3");
            c.Sequence(["attack 11", "buff"], f.IntentTexts(cubex), "turn 3: back to 7 + 4");
            f.EndTurn();
            c.Equal(283, f.Player.Hp, "turn 3: 11 damage");

            f.ApplyPower<VulnerablePower>(f.Player, 1, cubex);
            c.Sequence(["attack 16x2"], f.IntentTexts(cubex), "turn 4: (5 + 6) x 1.5 = 16.5 per hit");
            c.Equal(32, f.IncomingDamage(), "turn 4: 16 twice, not 33");
            f.EndTurn();
            c.Equal(251, f.Player.Hp, "turn 4: 32 damage");
        });

        c.Group("Cubex Construct: one that joins a running fight does get its 13 Block", () =>
        {
            var f = Versus("CubexConstructNormal");
            var second = f.AddMonster("CubexConstruct");
            c.Equal(13, second.Block, "13 Block");
            c.Equal(1, second.PowerAmount<ArtifactPower>(), "Artifact 1");
            c.Equal(65, second.Hp, "65 HP again: the range has one value, so the roll falls back to it");
            c.Equal(2, f.Rng.Niche.Counter, "one Niche draw for it all the same");
            c.Equal(1, second.RosterIndex, "it is e1");
            c.Sequence(["buff"], f.IntentTexts(second), "its first move is settled at once on the player's turn");
            f.EndTurn();
            c.Equal(0, second.Block, "the Block falls at the start of the enemy turn");
            c.Equal(2, second.PowerAmount<StrengthPower>(), "and it acts in the turn it joined, having joined on the player's side of it");
            c.Sequence(["attack 9", "buff"], f.IntentTexts(second), "then the cycle");
        });
    }

    // ---------- Fogmog ----------

    private static void FogmogChecks(Check c)
    {
        c.Group("Fogmog: start of the fight", () =>
        {
            var f = Versus("FogmogNormal");
            c.Equal(1, f.Enemies.Count, "Fogmog is alone on turn 1");
            var fogmog = f.Enemy(0);
            c.Equal("Fogmog", fogmog.Name, "name");
            c.Equal(74, fogmog.Hp, "74 HP");
            c.Equal(0, fogmog.Powers.Count, "no starting power");
            c.Equal(0, fogmog.Block, "no Block");
            c.Sequence(["summon"], f.IntentTexts(fogmog), "opens with the summon");
            c.Equal(0, f.IncomingDamage(), "nothing incoming on turn 1");
            c.Equal(0, f.Rng.MonsterAi.Counter, "no MonsterAi draw");
        });

        c.Group("Fogmog: nine turns; the first branch swipes again, the second headbutts, the third swipes again", () =>
        {
            var seed = SeedWhere(ai => FogmogSwipesAgain(ai) && !FogmogSwipesAgain(ai) && FogmogSwipesAgain(ai));
            var f = Versus("FogmogNormal", seed);
            var fogmog = f.Enemy(0);

            c.Sequence(["summon"], f.IntentTexts(fogmog), "turn 1: intent");
            f.EndTurn();
            c.Equal(300, f.Player.Hp, "turn 1: the summon does no damage");
            c.Equal(2, f.Enemies.Count, "turn 1: the Eye has joined");
            var eye = f.Enemy(1);
            c.Equal("Eye with Teeth", eye.Name, "the new enemy is e1");
            c.Equal(0, Count(f, "Dazed"), "the Eye does not act in the turn it arrives");

            // Swipe: 8 + Strength, then +1 Strength. Headbutt: 14 + Strength. The Eye adds 3 Dazed a turn.
            // MonsterAi counts the draws so far: one per visit to the branch, which follows SWIPE_MOVE only.
            (string Move, string[] Intents, int Incoming, int HpAfter, int StrengthAfter, int DrawsAfter)[] turns =
            [
                ("SWIPE_MOVE", ["attack 8", "buff"], 8, 292, 1, 1),
                ("SWIPE_RANDOM_MOVE", ["attack 9", "buff"], 9, 283, 2, 1),
                ("HEADBUTT_MOVE", ["attack 16"], 16, 267, 2, 1),
                ("SWIPE_MOVE", ["attack 10", "buff"], 10, 257, 3, 2),
                ("HEADBUTT_MOVE", ["attack 17"], 17, 240, 3, 2),
                ("SWIPE_MOVE", ["attack 11", "buff"], 11, 229, 4, 3),
                ("SWIPE_RANDOM_MOVE", ["attack 12", "buff"], 12, 217, 5, 3),
                ("HEADBUTT_MOVE", ["attack 19"], 19, 198, 5, 3),
            ];
            for (var t = 0; t < turns.Length; t++)
            {
                var (move, intents, incoming, hpAfter, strengthAfter, drawsAfter) = turns[t];
                var turn = t + 2;
                var at = $"turn {turn}";
                c.Equal(turn, f.Turn, $"{at}: turn number");
                c.Equal(move, NextMove(fogmog), $"{at}: Fogmog's move");
                c.Sequence(intents, f.IntentTexts(fogmog), $"{at}: Fogmog's intents");
                c.Sequence(["statuscard: 3"], f.IntentTexts(eye), $"{at}: the Eye's intent");
                c.Equal(incoming, f.IncomingDamage(), $"{at}: incoming damage (the Eye adds none)");
                f.EndTurn();
                c.Equal(hpAfter, f.Player.Hp, $"{at}: player HP after the enemy turn");
                c.Equal(strengthAfter, fogmog.PowerAmount<StrengthPower>(), $"{at}: Fogmog's Strength after");
                c.Equal(3 * (turn - 1), Count(f, "Dazed"), $"{at}: Dazed in the deck after");
                // The discard pile: every hand so far, and after each hand but the first the Eye's three cards.
                c.Equal(5 * turn + 3 * (turn - 1), f.DiscardPile.Count, $"{at}: the Dazed went to the discard pile");
                c.Sequence(["Dazed", "Dazed", "Dazed"], f.DiscardPile.Skip(f.DiscardPile.Count - 3).Select(card => card.Def.ClassName), $"{at}: at its bottom");
                c.Equal(drawsAfter, f.Rng.MonsterAi.Counter, $"{at}: MonsterAi draws so far");
                c.Equal(0, fogmog.Block + eye.Block, $"{at}: neither gains Block");
            }
            c.Sequence(
                ["ILLUSION_MOVE", "SWIPE_MOVE", "SWIPE_RANDOM_MOVE", "HEADBUTT_MOVE", "SWIPE_MOVE", "HEADBUTT_MOVE", "SWIPE_MOVE", "SWIPE_RANDOM_MOVE", "HEADBUTT_MOVE",
                    "SWIPE_MOVE"],
                fogmog.Monster!.MoveLog, "Fogmog's log");
            c.Equal(2, f.Roster.Count, "only one Eye is ever summoned");
            c.Equal(74, fogmog.Hp, "Fogmog untouched");
            c.Equal(0, f.Player.Powers.Count, "no power on the player");
        });

        c.Group("Fogmog: the other first branch, a headbutt on turn 3", () =>
        {
            var seed = SeedWhere(ai => !FogmogSwipesAgain(ai));
            var f = Versus("FogmogNormal", seed);
            var fogmog = f.Enemy(0);
            f.EndTurn().EndTurn();
            c.Equal(292, f.Player.Hp, "turn 2: the Swipe");
            c.Equal("HEADBUTT_MOVE", NextMove(fogmog), "turn 3: Headbutt");
            c.Sequence(["attack 15"], f.IntentTexts(fogmog), "turn 3: 14 + 1 Strength");
            f.EndTurn();
            c.Equal(277, f.Player.Hp, "turn 3: 15 damage");
            c.Equal(1, fogmog.PowerAmount<StrengthPower>(), "Headbutt adds no Strength");
            c.Equal("SWIPE_MOVE", NextMove(fogmog), "turn 4: Swipe, with no draw");
            c.Sequence(["attack 9", "buff"], f.IntentTexts(fogmog), "turn 4: 8 + 1");
            c.Equal(1, f.Rng.MonsterAi.Counter, "one draw so far");
            f.EndTurn();
            c.Equal(268, f.Player.Hp, "turn 4: 9 damage");
            c.Equal(2, f.Rng.MonsterAi.Counter, "the branch after the Swipe drew the second");
        });

        c.Group("Fogmog: the branch follows the MonsterAi stream, 40 to 60", () =>
        {
            var swipes = 0;
            for (var seed = 1; seed <= 400; seed++)
            {
                var f = Versus("FogmogNormal", seed);
                f.EndTurn().EndTurn();
                var again = NextMove(f.Enemy(0)) == "SWIPE_RANDOM_MOVE";
                if (again) swipes++;
                else c.Equal("HEADBUTT_MOVE", NextMove(f.Enemy(0)), $"seed {seed}: the branch has two options");
                c.Equal(FogmogSwipesAgain(Ai(seed)), again, $"seed {seed}: the first draw decides the first branch");
                c.Equal(1, f.Rng.MonsterAi.Counter, $"seed {seed}: one draw, and none for the Eye");
            }
            c.That(swipes is > 120 and < 200, $"Swipe-again came {swipes} times in 400 (expected about 160)");
        });

        c.Group("Fogmog: hits against Block", () =>
        {
            var seed = SeedWhere(ai => !FogmogSwipesAgain(ai));
            var f = Versus("FogmogNormal", seed);
            var fogmog = f.Enemy(0);
            f.EndTurn();
            f.Play("Defend").EndTurn();
            c.Equal(297, f.Player.Hp, "turn 2: 8 into 5 Block");
            c.Equal(1, fogmog.PowerAmount<StrengthPower>(), "the Strength comes after the hit");
            f.Play("Defend").Play("Defend").EndTurn();
            c.Equal(292, f.Player.Hp, "turn 3: 15 into 10 Block");
        });

        c.Group("Fogmog: killed before its first turn, it summons nothing", () =>
        {
            var f = Versus("FogmogNormal", hp: 50);
            f.SetHp(f.Enemy(0), 6);
            f.Play("Strike", 0);
            c.That(f.IsOver && f.Won, "won");
            c.Equal(1, f.Roster.Count, "no Eye ever joined");
        });
    }

    // ---------- Eye with Teeth and Illusion ----------

    /// <summary>FogmogNormal on player turn 2, the Eye just summoned. Fogmog swipes again on turn 3.</summary>
    private static Fight WithEye(out Creature fogmog, out Creature eye, int hp = 300)
    {
        var seed = SeedWhere(FogmogSwipesAgain);
        var f = Versus("FogmogNormal", seed, hp);
        f.EndTurn();
        fogmog = f.Enemy(0);
        eye = f.Enemy(1);
        return f;
    }

    private static void EyeWithTeethChecks(Check c)
    {
        c.Group("Eye with Teeth: how it joins", () =>
        {
            var f = Versus("FogmogNormal");
            c.Equal(1, f.Rng.Niche.Counter, "one HP roll at the start");
            f.EndTurn();
            var fogmog = f.Enemy(0);
            var eye = f.Enemy(1);
            c.Equal(2, f.Rng.Niche.Counter, "the Eye's HP roll takes a Niche draw when it is summoned");
            c.Equal(6, eye.Hp, "6 HP");
            c.Equal(6, eye.MaxHp, "6 max HP");
            c.Equal("illusion", eye.Slot, "in the slot 'illusion'");
            c.That(ReferenceEquals(f.Enemies[0], eye) && ReferenceEquals(f.Enemies[1], fogmog), "the slot order puts it before Fogmog in the list");
            c.Equal(1, eye.RosterIndex, "but it is e1, Fogmog stays e0");
            c.Sequence(["IllusionPower", "MinionPower"], eye.Powers.Select(p => p.ClassName), "Illusion, and through it Minion, in that order");
            c.Equal(1, eye.PowerAmount<IllusionPower>(), "Illusion 1");
            c.Equal(1, eye.PowerAmount<MinionPower>(), "Minion 1");
            c.That(ReferenceEquals(eye.GetPower<IllusionPower>()!.Applier, eye), "Illusion is applied by the Eye itself");
            c.That(eye.GetPower<MinionPower>()!.Applier is null, "Minion has no applier");
            c.That(eye.IsSecondaryEnemy && fogmog.IsPrimaryEnemy, "the Eye is a secondary enemy, Fogmog the primary one");
            c.That(!eye.GetPower<MinionPower>()!.ShouldOwnerDeathTriggerFatal, "killing it is not a Fatal kill");
            c.Equal("DISTRACT_MOVE", NextMove(eye), "its move");
            c.Sequence(["statuscard: 3"], f.IntentTexts(eye), "its intent on the next player turn");
            c.Sequence(["DISTRACT_MOVE"], eye.Monster!.MoveLog, "the first move is logged once");
            c.Sequence(["ILLUSION_MOVE"], MovesPerformed(f, 1), "only Fogmog acted in round 1");
            c.Equal(0, f.Rng.MonsterAi.Counter, "no MonsterAi draw for it");

            var enemies = f.Snapshot().State["enemies"]!.AsArray();
            c.Equal(2, enemies.Count, "the snapshot lists both");
            c.Equal("Fogmog", enemies[0]!["name"]!.GetValue<string>(), "Fogmog first, as e0");
            c.Equal(true, enemies[0]!["primary"]?.GetValue<bool>(), "marked as the primary enemy");
            c.Equal(1, enemies[1]!["i"]!.GetValue<int>(), "the Eye as e1");
            c.That(enemies[1]!["primary"] is null, "the Eye is not primary");
            c.Sequence(["Illusion", "Minion"], enemies[1]!["powers"]!.AsArray().Select(p => p!["name"]!.GetValue<string>()), "its powers by name");
        });

        c.Group("Eye with Teeth: it acts before Fogmog, and its Dazed are plain status cards", () =>
        {
            var f = WithEye(out var fogmog, out var eye);
            f.EndTurn();
            c.Sequence(["DISTRACT_MOVE", "SWIPE_MOVE"], MovesPerformed(f, 2), "round 2: the Eye, then Fogmog");
            c.Equal(3, Count(f, "Dazed"), "three Dazed");
            var dazed = f.DiscardPile[^1];
            c.Equal("Dazed", dazed.Title, "title");
            c.Equal(CardType.Status, dazed.Type, "a Status card");
            c.That(dazed.Has(CardKeywords.Unplayable) && dazed.Has(CardKeywords.Ethereal), "Unplayable and Ethereal");
            c.Sequence([-1, -1, -1], f.History.Entries.Where(e => e.Kind == HistoryKind.CardGenerated).Select(e => e.OtherId), "created one by one, with no creator named");
            c.Sequence(["DISTRACT_MOVE", "DISTRACT_MOVE"], eye.Monster!.MoveLog, "the same move is chosen and logged again");
            c.Equal(292, f.Player.Hp, "only Fogmog's Swipe hurt");
            c.Equal(6, eye.Hp, "the Eye is unharmed");
            c.Equal(74, fogmog.Hp, "so is Fogmog");
        });

        c.Group("Eye with Teeth: its Dazed come up after the next shuffle, cannot be played, and are exhausted at the end of the turn", () =>
        {
            var drawn = 0;
            for (var seed = 1; seed <= 12; seed++)
            {
                // Ten cards: turn 2 empties the draw pile, so turn 3 shuffles the first three Dazed in.
                var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(300, 300).Encounter("FogmogNormal").Seed(seed).Start();
                f.EndTurn().EndTurn();
                c.Equal(5, f.Hand.Count, $"seed {seed}: five cards drawn on turn 3");
                c.Equal(8, f.DrawPile.Count, $"seed {seed}: of the thirteen shuffled");
                c.Equal(3, f.Hand.Concat(f.DrawPile).Count(card => card.Def.ClassName == "Dazed"), $"seed {seed}: the three Dazed among them");
                var inHand = 0;
                for (var i = 0; i < f.Hand.Count; i++)
                {
                    if (f.Hand[i].Def.ClassName != "Dazed") continue;
                    inHand++;
                    var prefix = $"play:{i}";
                    c.That(!f.LegalActions().Any(a => a.Id == prefix || a.Id.StartsWith(prefix + ">", StringComparison.Ordinal)), $"seed {seed}: a Dazed in hand cannot be played");
                }
                drawn += inHand;
                f.EndTurn();
                c.Equal(inHand, f.ExhaustPile.Count, $"seed {seed}: every Dazed that was in hand is exhausted");
                c.That(f.ExhaustPile.All(card => card.Def.ClassName == "Dazed"), $"seed {seed}: and nothing else");
                c.Equal(6, Count(f, "Dazed"), $"seed {seed}: three more arrived meanwhile");
            }
            c.That(drawn > 0, "at least one of these fights drew a Dazed");
        });

        c.Group("Illusion: at 0 HP the Eye stays in the fight, out of reach, and revives on its next turn", () =>
        {
            var f = WithEye(out var fogmog, out var eye);
            c.That(f.LegalActions().Any(a => a.Id == "play:0>e1"), "alive, it can be targeted");
            f.Play("Strike", 1);

            c.Equal(0, eye.Hp, "Strike takes its 6 HP");
            c.That(f.Enemies.Contains(eye), "it stays in the enemy list");
            c.That(ReferenceEquals(f.Enemies[0], eye), "at its place before Fogmog");
            c.That(!f.IsOver && f.InProgress, "the fight goes on");
            c.That(eye.GetPower<IllusionPower>()!.IsReviving, "it is reviving");
            c.Sequence(["IllusionPower", "MinionPower"], eye.Powers.Select(p => p.ClassName), "it keeps Illusion and Minion");
            c.Equal(IllusionPower.ReviveMoveId, NextMove(eye), "its move is replaced at once by the revive");
            c.Sequence(["heal"], f.IntentTexts(eye), "which shows a heal intent");
            c.Sequence(["DISTRACT_MOVE"], eye.Monster!.MoveLog, "a forced move is not logged");
            c.That(!f.IsHittable(eye) && !f.IsTargetableEnemy(eye) && !f.CanReceivePowers(eye), "it cannot be hit, targeted or given powers");
            c.That(!f.LegalActions().Any(a => a.Id.EndsWith(">e1", StringComparison.Ordinal)), "no action aims at e1");
            c.That(f.LegalActions().Any(a => a.Id == "play:0>e0"), "Fogmog can still be aimed at");
            c.That(!f.Execute("play:0>e1").Ok, "and an id that does is refused");
            c.That(f.ApplyPower<VulnerablePower>(eye, 2, f.Player) is null && !eye.HasPower<VulnerablePower>(), "a power applied to it does not land");
            c.Equal(8, f.IncomingDamage(), "incoming damage is Fogmog's Swipe");

            var enemies = f.Snapshot().State["enemies"]!.AsArray();
            c.Equal(1, enemies.Count, "the snapshot leaves the Eye out");
            c.Equal(0, enemies[0]!["i"]!.GetValue<int>(), "Fogmog is still e0");
            c.That(enemies[0]!["primary"] is null, "with no minion shown, nothing is marked primary");

            f.EndTurn();
            c.Sequence([IllusionPower.ReviveMoveId, "SWIPE_MOVE"], MovesPerformed(f, 2), "round 2: the Eye takes its turn at 0 HP and revives, then Fogmog swipes");
            c.Equal(6, eye.Hp, "back at full HP");
            c.That(!eye.GetPower<IllusionPower>()!.IsReviving, "no longer reviving");
            c.Equal(0, Count(f, "Dazed"), "no Dazed in the turn it revived");
            c.Equal(292, f.Player.Hp, "Fogmog's Swipe landed as usual");
            c.Equal("DISTRACT_MOVE", NextMove(eye), "next it does what it had chosen last");
            c.Sequence(["statuscard: 3"], f.IntentTexts(eye), "and shows it");
            c.Sequence(["DISTRACT_MOVE", "DISTRACT_MOVE"], eye.Monster!.MoveLog, "the follow-up is logged");
            c.That(f.IsHittable(eye) && f.IsTargetableEnemy(eye), "it can be hit again");
            c.Equal(2, f.Snapshot().State["enemies"]!.AsArray().Count, "and is listed again");
            c.Equal(2, f.Rng.Niche.Counter, "reviving rolls no new HP");
            c.Equal(1, f.Rng.MonsterAi.Counter, "and draws no MonsterAi number: the one draw is Fogmog's branch");

            f.EndTurn();
            c.Equal(3, Count(f, "Dazed"), "left alone, it distracts again");
            c.Equal(283, f.Player.Hp, "turn 3: Fogmog's second Swipe, 9");
            c.Equal(74, fogmog.Hp, "Fogmog untouched");
        });

        c.Group("Illusion: killed every turn, it never gets to act", () =>
        {
            var f = WithEye(out _, out var eye);
            f.Play("Strike", 1).EndTurn();
            f.Play("Strike", 1);
            c.Equal(0, eye.Hp, "killed again on turn 3");
            f.EndTurn();
            c.Equal(6, eye.Hp, "revived again");
            c.Equal(0, Count(f, "Dazed"), "still no Dazed");
            f.EndTurn();
            c.Equal(3, Count(f, "Dazed"), "the first turn it is left alone, three arrive");
            c.Sequence(["DISTRACT_MOVE", "DISTRACT_MOVE", "DISTRACT_MOVE", "DISTRACT_MOVE"], eye.Monster!.MoveLog, "one entry per choice, none for the revives");
        });

        c.Group("Illusion: brought to 0 HP after it acted, the Eye waits a whole round on its revive", () =>
        {
            var f = WithEye(out _, out var eye);
            f.ApplyPower<LateBlow>(f.Player, 6, f.Player);
            f.EndTurn();
            c.Sequence(["DISTRACT_MOVE", "SWIPE_MOVE"], MovesPerformed(f, 2), "round 2: both acted before the blow");
            c.Equal(3, Count(f, "Dazed"), "its three Dazed arrived");
            c.Equal(0, eye.Hp, "then it was brought to 0 HP");
            // The roll at the start of turn 3 cannot leave a forced move that was not performed yet.
            c.Equal(IllusionPower.ReviveMoveId, NextMove(eye), "turn 3: the revive is still its move");
            c.Sequence(["DISTRACT_MOVE"], eye.Monster!.MoveLog, "nothing was chosen for it");
            c.Equal(1, f.Rng.MonsterAi.Counter, "only Fogmog's branch drew");
            c.That(!f.IsTargetableEnemy(eye) && f.Enemies.Contains(eye), "all of turn 3 it is out of reach");
            c.Equal(1, f.Snapshot().State["enemies"]!.AsArray().Count, "and not listed");
            f.EndTurn();
            c.Sequence([IllusionPower.ReviveMoveId, "SWIPE_RANDOM_MOVE"], MovesPerformed(f, 3), "round 3: it revives");
            c.Equal(6, eye.Hp, "at full HP");
            c.Equal(3, Count(f, "Dazed"), "no Dazed in round 3");
            c.Equal("DISTRACT_MOVE", NextMove(eye), "turn 4: back to its move");
            f.EndTurn();
            c.Equal(6, Count(f, "Dazed"), "three more in round 4");
        });

        c.Group("Illusion: its say over a dying creature's powers reaches only a creature that is still in the fight", () =>
        {
            // The game sorts out a dead creature's powers after it has left the fight, and then asks
            // nobody: everything goes. Only a creature that is still attached is put to the Illusions:
            // the Eye itself, and a monster that dies in the middle of its own move.
            Fight WithDummy(out Creature dummy, bool diesInItsOwnMove = false)
            {
                var steps = diesInItsOwnMove
                    ? new[] { new DummyStep("IDLE"), new DummyStep("BURST", Then: (x, me) => x.Damage(me, 999, Props.NonCardHpLoss, me)) }
                    : new[] { new DummyStep("IDLE") };
                var f = new FightBuilder().DrawOrder(LongDeck).Hp(300, 300).Slots(FogmogNormal.IllusionSlot, FogmogNormal.FogmogSlot)
                    .Enemy(new Fogmog(), slot: FogmogNormal.FogmogSlot)
                    .Enemy(new CheckDummy { Hp = 6, Steps = steps, OnAdded = (x, me) => x.ApplyPower<StrengthPower>(me, 2, me) })
                    .Start();
                dummy = f.Enemies.Single(e => e.Monster is CheckDummy);
                return f;
            }

            var before = WithDummy(out var dummy);
            before.ApplyPower<VulnerablePower>(dummy, 1, before.Player);
            before.Play("Strike", dummy.RosterIndex);
            c.That(dummy.IsDead && !before.Enemies.Contains(dummy), "without an Illusion about, the dummy dies and leaves");
            c.Equal(0, dummy.Powers.Count, "and loses every power");

            var after = WithDummy(out dummy);
            after.EndTurn();
            c.Equal(3, after.Enemies.Count, "the Eye has joined");
            after.ApplyPower<VulnerablePower>(dummy, 1, after.Player);
            after.Play("Strike", dummy.RosterIndex);
            c.That(dummy.IsDead && !after.Enemies.Contains(dummy), "with the Eye about, a dummy killed by a card still dies and leaves");
            c.Equal(0, dummy.Powers.Count, "and still loses every power: it had left the fight before its powers were sorted out, so no Illusion was asked");

            var own = WithDummy(out dummy, diesInItsOwnMove: true);
            own.EndTurn();
            c.Equal(3, own.Enemies.Count, "the Eye has joined (third fight)");
            own.ApplyPower<VulnerablePower>(dummy, 2, own.Player);
            own.EndTurn();
            c.That(dummy.IsDead && !own.Enemies.Contains(dummy), "a dummy that kills itself in its own move is gone once the move is done");
            c.Sequence(["StrengthPower"], dummy.Powers.Select(p => p.ClassName), "it was still in the fight when it died: the Eye's Illusion keeps its buff and lets its debuff go");

            var alone = new FightBuilder().DrawOrder(LongDeck).Hp(300, 300)
                .Enemy(new CheckDummy { Hp = 6, Steps = [new DummyStep("IDLE"), new DummyStep("BURST", Then: (x, me) => x.Damage(me, 999, Props.NonCardHpLoss, me))], OnAdded = (x, me) => x.ApplyPower<StrengthPower>(me, 2, me) })
                .Enemy(new CheckDummy())
                .Start();
            var lone = alone.Enemy(0);
            alone.EndTurn();
            alone.EndTurn();
            c.That(lone.IsDead && !alone.Enemies.Contains(lone), "the same death with no Illusion in the fight");
            c.Equal(0, lone.Powers.Count, "takes every power off");
        });

        c.Group("Illusion: attacks and the Eye at 0 HP", () =>
        {
            var f = WithEye(out var fogmog, out var eye);
            f.Put(Flurry, PileType.Hand);
            f.Put(Sweep, PileType.Hand);
            f.Put(Sweep, PileType.Hand);
            int HitsOnEye() => f.History.Count(e => e.Kind == HistoryKind.DamageReceived && e.ActorId == eye.Id);

            f.Play("Sweep");
            c.Equal(2, eye.Hp, "alive, it is hit by an attack on all enemies");
            c.Equal(70, fogmog.Hp, "as is Fogmog");
            c.Equal(1, HitsOnEye(), "one hit so far");

            f.Play("Flurry", 1);
            c.Equal(0, eye.Hp, "3 four times at 2 HP: the first hit kills");
            c.Equal(2, HitsOnEye(), "the other three hits are lost");
            c.Equal(70, fogmog.Hp, "not passed on to Fogmog");

            f.Play("Sweep");
            c.Equal(66, fogmog.Hp, "an attack on all enemies hits Fogmog");
            c.Equal(2, HitsOnEye(), "and skips the Eye");
            c.Equal(0, eye.Hp, "which stays at 0");
            c.That(f.Enemies.Contains(eye) && !f.IsOver, "and in the fight");
        });

        c.Group("Illusion: on its death the Eye keeps its buffs and temporary Strength-down, and loses other debuffs", () =>
        {
            var f = WithEye(out _, out var eye);
            f.ApplyPower<VulnerablePower>(eye, 2, f.Player);
            f.ApplyPower<WeakPower>(eye, 1, f.Player);
            f.ApplyPower<StrengthPower>(eye, 3, f.Player);
            f.ApplyPower<CheckShackle>(eye, 2, f.Player);
            c.Equal(1, eye.PowerAmount<StrengthPower>(), "3 Strength less the 2 shackled");
            f.Play("Strike", 1);
            c.Equal(0, eye.Hp, "killed");
            c.Sequence(["IllusionPower", "MinionPower", "StrengthPower", "CheckShackle"], eye.Powers.Select(p => p.ClassName), "Vulnerable and Weak are gone");
            f.EndTurn();
            c.Equal(6, eye.Hp, "revived");
            c.Sequence(["IllusionPower", "MinionPower", "StrengthPower"], eye.Powers.Select(p => p.ClassName), "the shackle ended with the enemy turn");
            c.Equal(3, eye.PowerAmount<StrengthPower>(), "and gave the Strength back, the Eye being revived by then");
        });

        c.Group("Illusion: the Eye is a minion; the fight is Fogmog's", () =>
        {
            var alive = WithEye(out var fogmog, out var eye, hp: 50);
            alive.SetHp(fogmog, 6);
            alive.Play("Strike", 0);
            c.That(alive.IsOver && alive.Won, "killing Fogmog wins with the Eye alive");
            c.Equal(0, eye.Hp, "the Eye was killed with it");
            c.Equal(56, alive.Player.Hp, "Burning Blood: 50 + 6");

            var reviving = WithEye(out fogmog, out eye, hp: 50);
            reviving.Play("Strike", 1);
            c.That(!reviving.IsOver, "killing the Eye does not end the fight");
            reviving.SetHp(fogmog, 6);
            reviving.Play("Strike", 0);
            c.That(reviving.IsOver && reviving.Won, "killing Fogmog wins while the Eye is reviving");
            c.Equal(56, reviving.Player.Hp, "Burning Blood again");
        });

        c.Group("Illusion: an attack on all that kills Fogmog takes the Eye with it, whether or not the hit left the Eye alive", () =>
        {
            // The death of the last primary enemy kills its living minions; the Eye then only starts
            // to revive, which no longer matters with no primary enemy left.
            var alive = WithEye(out var fogmog, out var eye, hp: 50);
            alive.SetHp(fogmog, 4);
            alive.Put(Sweep, PileType.Hand);
            alive.Play("Sweep");
            c.That(alive.IsOver && alive.Won, "4 to all: Fogmog on 4 HP dies, the Eye is left on 2 and dies as its minion");
            c.Equal(0, eye.Hp, "the Eye is at 0 HP");
            c.Equal(56, alive.Player.Hp, "Burning Blood: 50 + 6");

            var both = WithEye(out fogmog, out eye, hp: 50);
            both.SetHp(fogmog, 4);
            both.SetHp(eye, 3);
            both.Put(Sweep, PileType.Hand);
            both.Play("Sweep");
            c.That(both.IsOver && both.Won, "the same hit killing both at once wins too");
        });

        c.Group("Fogmog: Thorns kills it in the middle of its Swipe", () =>
        {
            // Thorns answers before the hit lands; the hit that is under way still lands; the Strength
            // that follows is refused, the fight being decided; the win is settled when the move is done.
            var f = WithEye(out var fogmog, out var eye, hp: 50);
            f.SetHp(fogmog, 3);
            f.ApplyPower("ThornsPower", f.Player, 3, f.Player);
            f.EndTurn();
            c.That(f.IsOver && f.Won, "won in the enemy turn");
            c.Equal(0, fogmog.Hp, "3 Thorns against 3 HP");
            c.Equal(0, fogmog.PowerAmount<StrengthPower>(), "no Strength after its death");
            c.Equal(0, eye.Hp, "the Eye went with it");
            c.Equal(48, f.Player.Hp, "50, less 8 for the Swipe that still landed, plus 6 from Burning Blood");
        });

        c.Group("Illusion: a copy of the fight taken while the Eye revives goes on the same way", () =>
        {
            var f = WithEye(out _, out _);
            f.Play("Strike", 1);
            var copy = f.Clone();
            f.EndTurn().EndTurn();
            copy.EndTurn();
            c.Equal(6, copy.Enemy(1).Hp, "the copy's Eye revives");
            c.That(!ReferenceEquals(copy.Enemy(1), f.Enemy(1)), "it is the copy's own Eye");
            c.Equal(0, Count(copy, "Dazed"), "no Dazed in the copy's revive turn");
            copy.EndTurn();
            c.Equal(f.Seen(), copy.Seen(), "both fights show the same after the same actions");
        });
    }

    // ---------- Inklet ----------

    private static void InkletChecks(Check c)
    {
        c.Group("Inklets: start of the fight", () =>
        {
            var f = Versus("InkletsNormal");
            c.Equal(3, f.Enemies.Count, "three Inklets");
            foreach (var inklet in f.Enemies)
            {
                c.Equal("Inklet", inklet.Name, "name");
                c.Sequence(["SlipperyPower"], inklet.Powers.Select(p => p.ClassName), "its only starting power");
                c.Equal(1, inklet.PowerAmount<SlipperyPower>(), "Slippery 1");
                c.That(ReferenceEquals(inklet.GetPower<SlipperyPower>()!.Applier, inklet), "applied by itself");
                c.Equal(0, inklet.Block, "no Block");
            }
            c.Sequence(["attack 3"], f.IntentTexts(f.Enemy(0)), "the first opens with Jab");
            c.Sequence(["attack 2x3"], f.IntentTexts(f.Enemy(1)), "the middle one with Whirlwind");
            c.Sequence(["attack 3"], f.IntentTexts(f.Enemy(2)), "the third with Jab");
            c.Equal(12, f.IncomingDamage(), "3 + 6 + 3 incoming");
            c.Equal(0, f.Rng.MonsterAi.Counter, "the first moves take no MonsterAi draw");
        });

        c.Group("Inklets: seven turns on a known path", () =>
        {
            // The draws, in the order they are made: turn 2 the first and the third Inklet, turn 3 the
            // middle one, turn 4 the outer two, and so on. True is Piercing Gaze, false Whirlwind.
            bool[] gazes = [true, false, true, false, true, false, true, true, true];
            var seed = SeedWhere(ai => gazes.All(g => InkletGazes(ai) == g));
            var f = Versus("InkletsNormal", seed);
            string[] jab = ["attack 3"], gaze = ["attack 10"], whirl = ["attack 2x3"];
            (string[][] Intents, int Incoming, int HpAfter, int DrawsAfter)[] turns =
            [
                ([jab, whirl, jab], 12, 288, 2),
                ([gaze, jab, whirl], 19, 269, 3),
                ([jab, gaze, jab], 16, 253, 5),
                ([whirl, jab, gaze], 19, 234, 6),
                ([jab, whirl, jab], 12, 222, 8),
                ([gaze, jab, gaze], 23, 199, 9),
                ([jab, gaze, jab], 16, 183, 11),
            ];
            for (var t = 0; t < turns.Length; t++)
            {
                var (intents, incoming, hpAfter, drawsAfter) = turns[t];
                var at = $"turn {t + 1}";
                for (var j = 0; j < 3; j++) c.Sequence(intents[j], f.IntentTexts(f.Enemy(j)), $"{at}: intent of e{j}");
                c.Equal(incoming, f.IncomingDamage(), $"{at}: incoming damage");
                f.EndTurn();
                c.Equal(hpAfter, f.Player.Hp, $"{at}: player HP after the enemy turn");
                c.Equal(drawsAfter, f.Rng.MonsterAi.Counter, $"{at}: MonsterAi draws so far, only by Inklets that had just jabbed");
            }
            c.Sequence(["JAB_MOVE", "PIERCING_GAZE_MOVE", "JAB_MOVE", "WHIRLWIND_MOVE", "JAB_MOVE", "PIERCING_GAZE_MOVE", "JAB_MOVE"],
                f.Enemy(0).Monster!.MoveLog.Take(7), "the first Inklet's moves");
            c.Sequence(["WHIRLWIND_MOVE", "JAB_MOVE", "PIERCING_GAZE_MOVE", "JAB_MOVE", "WHIRLWIND_MOVE", "JAB_MOVE", "PIERCING_GAZE_MOVE"],
                f.Enemy(1).Monster!.MoveLog.Take(7), "the middle Inklet's moves");
            c.Sequence(["JAB_MOVE", "WHIRLWIND_MOVE", "JAB_MOVE", "PIERCING_GAZE_MOVE", "JAB_MOVE", "PIERCING_GAZE_MOVE", "JAB_MOVE"],
                f.Enemy(2).Monster!.MoveLog.Take(7), "the third Inklet's moves");
            c.That(f.Enemies.All(e => e.PowerAmount<SlipperyPower>() == 1 && e.Block == 0 && !e.HasPower<StrengthPower>()), "nothing changes on the Inklets themselves");
            c.Equal(0, f.Player.Powers.Count, "no power on the player");
            c.Equal(0, f.DiscardPile.Count(card => card.Type == CardType.Status), "and no cards added");
        });

        c.Group("Inklets: each picks for itself after a Jab, evenly, by the MonsterAi stream", () =>
        {
            var firstGazes = 0;
            for (var seed = 1; seed <= 300; seed++)
            {
                var f = Versus("InkletsNormal", seed);
                f.EndTurn();
                var ai = Ai(seed);
                var first = InkletGazes(ai);
                var third = InkletGazes(ai);
                if (first) firstGazes++;
                c.Equal(first ? "PIERCING_GAZE_MOVE" : "WHIRLWIND_MOVE", NextMove(f.Enemy(0)), $"seed {seed}: the first draw is the first Inklet's");
                c.Equal("JAB_MOVE", NextMove(f.Enemy(1)), $"seed {seed}: the middle one jabs after its Whirlwind, with no draw");
                c.Equal(third ? "PIERCING_GAZE_MOVE" : "WHIRLWIND_MOVE", NextMove(f.Enemy(2)), $"seed {seed}: the second draw is the third Inklet's");
                c.Equal(2, f.Rng.MonsterAi.Counter, $"seed {seed}: two draws");
                f.EndTurn();
                var middle = InkletGazes(ai);
                c.Equal(middle ? "PIERCING_GAZE_MOVE" : "WHIRLWIND_MOVE", NextMove(f.Enemy(1)), $"seed {seed}: the third draw is the middle Inklet's");
                c.That(NextMove(f.Enemy(0)) == "JAB_MOVE" && NextMove(f.Enemy(2)) == "JAB_MOVE", $"seed {seed}: the outer two jab on turn 3");
                c.Equal(3, f.Rng.MonsterAi.Counter, $"seed {seed}: three draws");
            }
            c.That(firstGazes is > 110 and < 190, $"Piercing Gaze came {firstGazes} times in 300 (expected about 150)");
        });

        c.Group("Inklets: hits against Block and under Weak, Vulnerable and Strength, each hit on its own", () =>
        {
            var blocked = Versus("InkletsNormal");
            blocked.Play("Defend").EndTurn();
            c.Equal(293, blocked.Player.Hp, "3, then 2 three times, then 3 into 5 Block: 7 get through");

            var weak = Versus("InkletsNormal");
            foreach (var inklet in weak.Enemies) weak.ApplyPower<WeakPower>(inklet, 1, weak.Player);
            c.Sequence(["attack 2"], weak.IntentTexts(weak.Enemy(0)), "Jab under Weak: 2.25");
            c.Sequence(["attack 1x3"], weak.IntentTexts(weak.Enemy(1)), "Whirlwind under Weak: 1.5 a hit");
            c.Equal(7, weak.IncomingDamage(), "2 + 1 three times + 2");
            weak.EndTurn();
            c.Equal(293, weak.Player.Hp, "7 damage: every hit of the Whirlwind is rounded down by itself");

            var vulnerable = Versus("InkletsNormal");
            vulnerable.ApplyPower<VulnerablePower>(vulnerable.Player, 1, vulnerable.Enemy(0));
            c.Sequence(["attack 4"], vulnerable.IntentTexts(vulnerable.Enemy(0)), "Jab into Vulnerable: 4.5");
            c.Sequence(["attack 3x3"], vulnerable.IntentTexts(vulnerable.Enemy(1)), "Whirlwind into Vulnerable: 3 a hit");
            vulnerable.EndTurn();
            c.Equal(283, vulnerable.Player.Hp, "4 + 9 + 4");

            var strong = Versus("InkletsNormal", SeedWhere(InkletGazes));
            strong.ApplyPower<StrengthPower>(strong.Enemy(0), 2, strong.Enemy(0));
            strong.ApplyPower<StrengthPower>(strong.Enemy(1), 2, strong.Enemy(1));
            c.Sequence(["attack 5"], strong.IntentTexts(strong.Enemy(0)), "Jab with 2 Strength");
            c.Sequence(["attack 4x3"], strong.IntentTexts(strong.Enemy(1)), "Whirlwind with 2 Strength: on every hit");
            strong.EndTurn();
            c.Equal(280, strong.Player.Hp, "5 + 12 + 3");
            c.Sequence(["attack 12"], strong.IntentTexts(strong.Enemy(0)), "Piercing Gaze with 2 Strength");
        });

        c.Group("Inklets: Slippery makes the first loss 1 HP, on each Inklet", () =>
        {
            var f = Versus("InkletsNormal");
            var first = f.Enemy(0);
            var middle = f.Enemy(1);
            var hp = first.Hp;
            f.Play("Strike", 0);
            c.Equal(hp - 1, first.Hp, "the first Strike takes 1 HP");
            c.That(!first.HasPower<SlipperyPower>(), "and uses Slippery up");
            c.Equal(1, middle.PowerAmount<SlipperyPower>(), "the others keep theirs");
            f.Play("Strike", 0);
            c.Equal(hp - 7, first.Hp, "the second takes the full 6");

            var g = Versus("InkletsNormal");
            var target = g.Enemy(1);
            hp = target.Hp;
            g.Play("Bash", 1);
            c.Equal(hp - 1, target.Hp, "Bash takes 1 HP");
            c.Equal(2, target.PowerAmount<VulnerablePower>(), "its Vulnerable lands all the same");
            g.Put(Flurry, PileType.Hand);
            var other = g.Enemy(2);
            hp = other.Hp;
            g.Play("Flurry", 2);
            // 3 four times: 1, then 3, 3, 3 while it lives.
            c.Equal(Math.Max(hp - 10, 0), other.Hp, "of four hits only the first is cut to 1");
        });

        c.Group("Inklets: an attack on all of them takes 1 HP from each and uses up all three Slippery", () =>
        {
            var f = Versus("InkletsNormal");
            var before = f.Enemies.Select(e => e.Hp).ToList();
            f.Put(Sweep, PileType.Hand);
            f.Play("Sweep");
            c.Sequence(before.Select(hp => hp - 1), f.Enemies.Select(e => e.Hp), "4 damage becomes 1 on each");
            c.That(f.Enemies.All(e => !e.HasPower<SlipperyPower>()), "no Slippery left");
            f.Put(Sweep, PileType.Hand);
            f.Play("Sweep");
            c.Sequence(before.Select(hp => hp - 5), f.Enemies.Select(e => e.Hp), "the next one lands whole");
        });

        c.Group("Inklets: a dead Inklet leaves the list and draws nothing", () =>
        {
            var f = Versus("InkletsNormal");
            var first = f.Enemy(0);
            f.SetHp(first, 1);
            f.Play("Strike", 0);
            c.That(!f.Enemies.Contains(first), "it left the enemy list");
            c.That(!f.IsOver, "the other two fight on");
            f.EndTurn();
            c.Equal(291, f.Player.Hp, "Whirlwind 6 and Jab 3");
            c.Equal(1, f.Rng.MonsterAi.Counter, "one draw on turn 2: only the third Inklet had jabbed");
            c.Equal(InkletGazes(Ai(1)) ? "PIERCING_GAZE_MOVE" : "WHIRLWIND_MOVE", NextMove(f.Enemy(2)), "and that draw is the first of the stream");
            c.Sequence([1, 2], f.Snapshot().State["enemies"]!.AsArray().Select(e => e!["i"]!.GetValue<int>()), "the living keep their ids e1 and e2");
        });
    }

    // ---------- Mawler ----------

    private static void WalkMawler(Check c, int seed, (string Move, string[] Intents, int Incoming, int HpAfter, int VulnerableAfter)[] turns)
    {
        var f = Versus("MawlerNormal", seed);
        var mawler = f.Enemy(0);
        for (var t = 0; t < turns.Length; t++)
        {
            var (move, intents, incoming, hpAfter, vulnerableAfter) = turns[t];
            var at = $"turn {t + 1}";
            c.Equal(t + 1, f.Turn, $"{at}: turn number");
            c.Equal(move, NextMove(mawler), $"{at}: move");
            c.Sequence(intents, f.IntentTexts(mawler), $"{at}: intents");
            c.Equal(incoming, f.IncomingDamage(), $"{at}: incoming damage");
            f.EndTurn();
            c.Equal(hpAfter, f.Player.Hp, $"{at}: player HP after the enemy turn");
            c.Equal(vulnerableAfter, f.Player.PowerAmount<VulnerablePower>(), $"{at}: the player's Vulnerable on the next turn");
            c.Equal(t + 1, f.Rng.MonsterAi.Counter, $"{at}: one MonsterAi draw per choice, also when only one move is open");
            c.Equal(0, mawler.Block, $"{at}: it never gains Block");
        }
        c.Equal(0, mawler.Powers.Count, "it never gains a power");
        c.Equal(72, mawler.Hp, "untouched");
        c.Equal(0, f.DiscardPile.Count(card => card.Type == CardType.Status), "it adds no cards");
    }

    private static void MawlerChecks(Check c)
    {
        c.Group("Mawler: start of the fight", () =>
        {
            var f = Versus("MawlerNormal");
            var mawler = f.Enemy(0);
            c.Equal("Mawler", mawler.Name, "name");
            c.Equal(72, mawler.Hp, "72 HP");
            c.Equal(0, mawler.Powers.Count, "no starting power");
            c.Equal(0, mawler.Block, "no Block");
            c.Equal(Claw, NextMove(mawler), "opens with Claw");
            c.Sequence(["attack 4x2"], f.IntentTexts(mawler), "4 twice");
            c.Equal(8, f.IncomingDamage(), "8 incoming");
            c.Equal(0, f.Rng.MonsterAi.Counter, "the first move takes no MonsterAi draw");
        });

        c.Group("Mawler: eight turns, Roar on turn 2", () =>
        {
            string[] log = [Claw, Roar, Rip, Claw, Rip, Claw, Rip, Claw, Rip];
            var seed = SeedWhere(ai => MawlerLog(ai, 8).SequenceEqual(log));
            // Roar: 3 Vulnerable, new on the player, so its first tick is skipped: it covers the
            // next three enemy turns (21 for Rip and Tear, 6 twice for Claw), then is gone.
            WalkMawler(c, seed,
            [
                (Claw, ["attack 4x2"], 8, 292, 0),
                (Roar, ["debuff"], 0, 292, 3),
                (Rip, ["attack 21"], 21, 271, 2),
                (Claw, ["attack 6x2"], 12, 259, 1),
                (Rip, ["attack 21"], 21, 238, 0),
                (Claw, ["attack 4x2"], 8, 230, 0),
                (Rip, ["attack 14"], 14, 216, 0),
                (Claw, ["attack 4x2"], 8, 208, 0),
            ]);
        });

        c.Group("Mawler: nine turns, Roar on turn 5 and Claw right after it", () =>
        {
            string[] log = [Claw, Rip, Claw, Rip, Roar, Claw, Rip, Claw, Rip, Claw];
            var seed = SeedWhere(ai => MawlerLog(ai, 9).SequenceEqual(log));
            WalkMawler(c, seed,
            [
                (Claw, ["attack 4x2"], 8, 292, 0),
                (Rip, ["attack 14"], 14, 278, 0),
                (Claw, ["attack 4x2"], 8, 270, 0),
                (Rip, ["attack 14"], 14, 256, 0),
                (Roar, ["debuff"], 0, 256, 3),
                (Claw, ["attack 6x2"], 12, 244, 2),
                (Rip, ["attack 21"], 21, 223, 1),
                (Claw, ["attack 6x2"], 12, 211, 0),
                (Rip, ["attack 14"], 14, 197, 0),
            ]);
        });

        c.Group("Mawler: every choice follows the MonsterAi stream and the repeat rules", () =>
        {
            int ripOnTurn2 = 0, roarOnTurn2 = 0, ripAfterRoar = 0;
            for (var seed = 1; seed <= 400; seed++)
            {
                var f = Versus("MawlerNormal", seed, hp: 9999);
                for (var t = 0; t < 14; t++) f.EndTurn();
                var log = f.Enemy(0).Monster!.MoveLog;
                c.Sequence(MawlerLog(Ai(seed), 14), log, $"seed {seed}: the moves the game's rule gives for this stream");
                c.Equal(14, f.Rng.MonsterAi.Counter, $"seed {seed}: one draw per turn");
                c.Equal(Claw, log[0], $"seed {seed}: Claw first");
                c.That(log[1] != Claw, $"seed {seed}: turn 2 is never Claw");
                c.That(log.Count(m => m == Roar) <= 1, $"seed {seed}: Roar at most once");
                c.That(!log.Zip(log.Skip(1)).Any(p => p.First == p.Second), $"seed {seed}: no move twice running");
                var roarAt = log.ToList().IndexOf(Roar);
                // From the second move after Roar the two attacks can only alternate.
                for (var i = roarAt + 2; roarAt >= 0 && i < log.Count; i++)
                    c.That(log[i] != log[i - 1] && log[i] != Roar, $"seed {seed}: after Roar the attacks alternate");
                if (log[1] == Rip) ripOnTurn2++;
                if (log[1] == Roar)
                {
                    roarOnTurn2++;
                    if (log[2] == Rip) ripAfterRoar++;
                }
            }
            c.That(ripOnTurn2 is > 160 and < 240, $"turn 2 was Rip and Tear {ripOnTurn2} times in 400 (expected about 200)");
            c.Equal(400, ripOnTurn2 + roarOnTurn2, "and Roar otherwise");
            c.That(ripAfterRoar > roarOnTurn2 * 0.35 && ripAfterRoar < roarOnTurn2 * 0.65,
                $"right after a turn-2 Roar it was Rip and Tear {ripAfterRoar} times in {roarOnTurn2}: an even pick, not an alternation");
        });

        c.Group("Mawler: live intent numbers under Weak and Vulnerable, each hit rounded down on its own", () =>
        {
            string[] log = [Claw, Rip];
            var seed = SeedWhere(ai => MawlerLog(ai, 1).SequenceEqual(log));

            var weak = Versus("MawlerNormal", seed);
            var mawler = weak.Enemy(0);
            weak.ApplyPower<WeakPower>(mawler, 2, weak.Player);
            c.Sequence(["attack 3x2"], weak.IntentTexts(mawler), "Claw under Weak: 4 x 0.75 = 3 a hit");
            weak.EndTurn();
            c.Equal(294, weak.Player.Hp, "turn 1: 6 damage");
            c.Sequence(["attack 10"], weak.IntentTexts(mawler), "Rip and Tear under Weak: 10.5");
            weak.EndTurn();
            c.Equal(284, weak.Player.Hp, "turn 2: 10 damage");

            var both = Versus("MawlerNormal", seed);
            mawler = both.Enemy(0);
            both.ApplyPower<WeakPower>(mawler, 2, both.Player);
            both.ApplyPower<VulnerablePower>(both.Player, 1, mawler);
            c.Sequence(["attack 4x2"], both.IntentTexts(mawler), "Claw, Weak into Vulnerable: 4 x 0.75 x 1.5 = 4.5 a hit");
            c.Equal(8, both.IncomingDamage(), "4 twice, not 9");
            both.EndTurn();
            c.Equal(292, both.Player.Hp, "turn 1: 8 damage");
            // The Vulnerable was new on the player and skipped its tick; the monster's Weak went from 2 to 1.
            c.Sequence(["attack 15"], both.IntentTexts(mawler), "Rip and Tear, Weak into Vulnerable: 15.75");
            both.EndTurn();
            c.Equal(277, both.Player.Hp, "turn 2: 15 damage");
        });

        c.Group("Mawler: hits against Block; Roar adds to Vulnerable the player already has", () =>
        {
            var f = Versus("MawlerNormal");
            f.Play("Defend").EndTurn();
            c.Equal(297, f.Player.Hp, "Claw into 5 Block: the first hit leaves 1 Block, the second gets 3 through");

            string[] log = [Claw, Roar];
            var seed = SeedWhere(ai => MawlerLog(ai, 1).SequenceEqual(log));
            var g = Versus("MawlerNormal", seed);
            var mawler = g.Enemy(0);
            g.EndTurn();
            c.Sequence(["debuff"], g.IntentTexts(mawler), "Roar shows a plain debuff intent");
            g.EndTurn();
            c.Equal(3, g.Player.PowerAmount<VulnerablePower>(), "Roar: 3 Vulnerable");
            c.That(ReferenceEquals(g.Player.GetPower<VulnerablePower>()!.Applier, mawler), "applied by the Mawler");
            c.Equal(292, g.Player.Hp, "Roar does no damage");
            c.Sequence(["Vulnerable"], g.Player.Powers.Select(p => p.Name), "and puts nothing else on the player");

            var h = Versus("MawlerNormal", seed);
            h.EndTurn();
            h.ApplyPower<VulnerablePower>(h.Player, 1, h.Player);
            h.EndTurn();
            // The 1 Vulnerable is new this turn and skips its tick; Roar's 3 are added to it.
            c.Equal(4, h.Player.PowerAmount<VulnerablePower>(), "1 + 3 Vulnerable");
        });
    }

    // ---------- encounters ----------

    private static void Compositions(Check c)
    {
        c.Group("every encounter plays to an end, the same way twice, and the same through copies", () =>
        {
            foreach (var encounter in new[] { "CubexConstructNormal", "FogmogNormal", "InkletsNormal", "MawlerNormal" })
            {
                for (var seed = 1; seed <= 20; seed++)
                {
                    foreach (var lastPlay in new[] { false, true })
                    {
                        var what = $"{encounter}, seed {seed}, {(lastPlay ? "last" : "first")} play offered";
                        Fight New() => new FightBuilder().Encounter(encounter).Seed(seed).Start();
                        var straight = PlayOut(New(), lastPlay, throughCopies: false);
                        c.That(straight.Contains("|over|", StringComparison.Ordinal) && !straight.Contains("|timed out|", StringComparison.Ordinal), $"{what}: won or lost before the turn limit");
                        c.That(straight == PlayOut(New(), lastPlay, throughCopies: false), $"{what}: the same fight twice");
                        c.That(straight == PlayOut(New(), lastPlay, throughCopies: true), $"{what}: the same when every action is made on a copy");
                    }
                }
            }
        });

        c.Group("CubexConstructNormal: one Cubex Construct, nothing random", () =>
        {
            c.Equal(0, Content.Registry.NewEncounter("CubexConstructNormal").Slots.Count, "no slots");
            for (var seed = 1; seed <= 60; seed++)
            {
                var f = new FightBuilder().Encounter("CubexConstructNormal").Seed(seed).Start();
                c.Equal(1, f.Enemies.Count, $"seed {seed}: one enemy");
                var cubex = f.Enemy(0);
                c.That(cubex.Monster is CubexConstruct, $"seed {seed}: a Cubex Construct");
                c.That(cubex.Slot is null, $"seed {seed}: without a slot");
                c.That(cubex.Hp == 65 && cubex.MaxHp == 65, $"seed {seed}: 65 HP, got {cubex.Hp}/{cubex.MaxHp}");
                c.Equal(1, f.Rng.Niche.Counter, $"seed {seed}: one Niche draw");
                c.Equal(0, f.Rng.Encounter.Counter, $"seed {seed}: no draw on the encounter's stream");
                c.Equal(0, f.Rng.MonsterAi.Counter, $"seed {seed}: no MonsterAi draw");
            }
        });

        c.Group("FogmogNormal: Fogmog in 'fogmog'; its first move fills 'illusion'", () =>
        {
            c.Sequence(["illusion", "fogmog"], Content.Registry.NewEncounter("FogmogNormal").Slots, "the slot order");
            for (var seed = 1; seed <= 60; seed++)
            {
                var f = new FightBuilder().Encounter("FogmogNormal").Seed(seed).Start();
                c.Equal(1, f.Enemies.Count, $"seed {seed}: one enemy at the start");
                var fogmog = f.Enemy(0);
                c.That(fogmog.Monster is Fogmog, $"seed {seed}: Fogmog");
                c.Equal("fogmog", fogmog.Slot, $"seed {seed}: its slot");
                c.That(fogmog.Hp == 74 && fogmog.MaxHp == 74, $"seed {seed}: 74 HP, got {fogmog.Hp}/{fogmog.MaxHp}");
                c.Equal(1, f.Rng.Niche.Counter, $"seed {seed}: one Niche draw");
                c.Equal(0, f.Rng.Encounter.Counter, $"seed {seed}: no draw on the encounter's stream");
                f.EndTurn();
                if (f.IsOver) continue;
                c.Equal(2, f.Enemies.Count, $"seed {seed}: two enemies after turn 1");
                c.That(f.Enemies[0].Monster is EyeWithTeeth && f.Enemies[0].Slot == "illusion", $"seed {seed}: the Eye first, in 'illusion'");
                c.That(ReferenceEquals(f.Enemies[1], fogmog), $"seed {seed}: Fogmog second");
                c.That(f.Enemies[0].Hp == 6 && f.Enemies[0].MaxHp == 6, $"seed {seed}: the Eye has 6 HP");
                c.Equal(2, f.Rng.Niche.Counter, $"seed {seed}: a second Niche draw");
                c.Equal(0, f.Rng.MonsterAi.Counter, $"seed {seed}: no MonsterAi draw yet");
            }
        });

        c.Group("InkletsNormal: three Inklets, the second the middle one, three different HP values", () =>
        {
            c.Equal(0, Content.Registry.NewEncounter("InkletsNormal").Slots.Count, "no slots");
            var seen = new HashSet<int>();
            for (var seed = 1; seed <= 200; seed++)
            {
                var f = new FightBuilder().Encounter("InkletsNormal").Seed(seed).Start();
                c.Equal(3, f.Enemies.Count, $"seed {seed}: three enemies");
                c.That(f.Enemies.All(e => e.Monster is Inklet && e.Slot is null), $"seed {seed}: all Inklets, no slots");
                c.Sequence([false, true, false], f.Enemies.Select(e => ((Inklet)e.Monster!).MiddleInklet), $"seed {seed}: only the second is the middle Inklet");
                c.Sequence(["JAB_MOVE", "WHIRLWIND_MOVE", "JAB_MOVE"], f.Enemies.Select(NextMove), $"seed {seed}: first moves in list order");
                c.Sequence([0, 1, 2], f.Enemies.Select(e => e.RosterIndex), $"seed {seed}: e0, e1, e2 in list order");
                c.That(f.Enemies.All(e => e.MaxHp is >= 11 and <= 17 && e.Hp == e.MaxHp), $"seed {seed}: HP in 11..17: {string.Join(", ", f.Enemies.Select(e => e.MaxHp))}");
                c.Equal(3, f.Enemies.Select(e => e.MaxHp).Distinct().Count(), $"seed {seed}: no two share an HP value");
                c.Equal(3, f.Rng.Niche.Counter, $"seed {seed}: one Niche draw each");
                c.Equal(0, f.Rng.Encounter.Counter, $"seed {seed}: no draw on the encounter's stream");
                c.Equal(0, f.Rng.MonsterAi.Counter, $"seed {seed}: no MonsterAi draw");
                foreach (var e in f.Enemies) seen.Add(e.MaxHp);
            }
            c.Sequence([11, 12, 13, 14, 15, 16, 17], seen.Order(), "every value of the range turns up");
        });

        c.Group("MawlerNormal: one Mawler, nothing random", () =>
        {
            c.Equal(0, Content.Registry.NewEncounter("MawlerNormal").Slots.Count, "no slots");
            for (var seed = 1; seed <= 60; seed++)
            {
                var f = new FightBuilder().Encounter("MawlerNormal").Seed(seed).Start();
                c.Equal(1, f.Enemies.Count, $"seed {seed}: one enemy");
                var mawler = f.Enemy(0);
                c.That(mawler.Monster is Mawler, $"seed {seed}: a Mawler");
                c.That(mawler.Slot is null, $"seed {seed}: without a slot");
                c.That(mawler.Hp == 72 && mawler.MaxHp == 72, $"seed {seed}: 72 HP, got {mawler.Hp}/{mawler.MaxHp}");
                c.Equal(1, f.Rng.Niche.Counter, $"seed {seed}: one Niche draw");
                c.Equal(0, f.Rng.Encounter.Counter, $"seed {seed}: no draw on the encounter's stream");
                c.Equal(0, f.Rng.MonsterAi.Counter, $"seed {seed}: no MonsterAi draw");
            }
        });
    }
}
