using System.Globalization;
using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// The rules review: a second, independent set of scripted checks on the engine. Each group
/// takes one rule of rules-turns.md ("turns Rn") or rules-damage.md ("damage Rn") and a case
/// worked out by hand from the rule or from the game code, never from running the engine. The
/// cards, powers and monsters used here exist only for these checks.
/// </summary>
public sealed class EngineReviewChecks : IArenaChecks
{
    public void Run(Check c)
    {
        DamageMaths(c);
        AttacksAndDeaths(c);
        ListenerOrder(c);
        Durations(c);
        BlockAndTurnStart(c);
        TurnEnd(c);
        DrawingAndShuffling(c);
        GeneratedCardsAndAutoPlay(c);
        CardPlays(c);
        MonsterChoices(c);
        Powers(c);
        Snapshots(c);
    }

    // ---------- recorders ----------

    /// <summary>A relic that writes down the hooks it hears. The list is shared with the check on purpose.</summary>
    private sealed class Tap : Relic
    {
        public List<string> Log = new();

        /// <summary>Which hooks to write down (by the first word of the entry); null for all.</summary>
        public HashSet<string>? Only;

        /// <summary>Appended to every entry.</summary>
        public Func<Fight, string>? State;

        /// <summary>Entries are only written while this holds.</summary>
        public Func<Fight, bool>? When;

        public Action<Fight, Side>? OnBeforeSideTurnStart;
        public Action<Fight, Creature>? OnAfterBlockCleared;

        public override string Name => "Tap";
        public override string Describe() => "Writes down what it hears.";
        public override void OnCloned(CloneMap map) { }

        private void Note(Fight f, string what)
        {
            if (When is not null && !When(f)) return;
            var space = what.IndexOf(' ');
            if (Only is not null && !Only.Contains(space < 0 ? what : what[..space])) return;
            Log.Add(State is null ? what : $"{what} {State(f)}");
        }

        public override void BeforeSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants)
        {
            Note(f, $"BeforeSideTurnStart {side}");
            OnBeforeSideTurnStart?.Invoke(f, side);
        }

        public override void AfterBlockCleared(Fight f, Creature creature)
        {
            Note(f, $"AfterBlockCleared {creature.Name}");
            OnAfterBlockCleared?.Invoke(f, creature);
        }

        public override void AfterEnergyReset(Fight f) => Note(f, "AfterEnergyReset");
        public override void BeforeHandDraw(Fight f) => Note(f, "BeforeHandDraw");
        public override void AfterPlayerTurnStart(Fight f) => Note(f, "AfterPlayerTurnStart");
        public override void AfterSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants) => Note(f, $"AfterSideTurnStart {side}");
        public override void AfterAutoPrePlayPhaseEntered(Fight f) => Note(f, "AfterAutoPrePlayPhaseEntered");
        public override void BeforeFlush(Fight f) => Note(f, "BeforeFlush");

        public override void AfterFlush(Fight f, IReadOnlyList<CardInstance> flushed, IReadOnlyList<CardInstance> retained) =>
            Note(f, $"AfterFlush [{string.Join(",", flushed.Select(x => x.Title))}] kept [{string.Join(",", retained.Select(x => x.Title))}]");

        public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants) => Note(f, $"AfterSideTurnEnd {side}");
        public override void BeforeCardPlayed(Fight f, CardPlay play) => Note(f, "BeforeCardPlayed Tap");
        public override void AfterCardPlayed(Fight f, CardPlay play) => Note(f, $"AfterCardPlayed {play.Card.Title}");
        public override void AfterEnergySpent(Fight f, CardInstance card, int amount) => Note(f, $"AfterEnergySpent {card.Title} {amount}");

        public override void AfterCardExhausted(Fight f, CardInstance card, bool causedByEthereal) =>
            Note(f, $"AfterCardExhausted {card.Title} ethereal={causedByEthereal}");

        public override void AfterCardChangedPiles(Fight f, CardInstance card, PileType oldPile) =>
            Note(f, $"AfterCardChangedPiles {card.Title} {oldPile}->{card.Pile}");

        public override void AfterDamageGiven(Fight f, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardInstance? cardSource) =>
            Note(f, $"AfterDamageGiven {target.Name}");

        public override void AfterDamageReceived(Fight f, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardInstance? cardSource) =>
            Note(f, $"AfterDamageReceived {target.Name} {result.UnblockedDamage}");

        public override void AfterCurrentHpChanged(Fight f, Creature creature, decimal delta) =>
            Note(f, string.Create(CultureInfo.InvariantCulture, $"AfterCurrentHpChanged {creature.Name} {delta}"));

        public override void BeforeDeath(Fight f, Creature creature) => Note(f, $"BeforeDeath {creature.Name}");
        public override void AfterDeath(Fight f, Creature creature, bool wasRemovalPrevented) => Note(f, $"AfterDeath {creature.Name}");
    }

    /// <summary>A power for checks: it writes its label when a card is about to be played and when it
    /// is removed, and can be given something to do at a few hooks. Every application is its own instance.</summary>
    private sealed class Probe : Power
    {
        public string Label = "";
        public List<string>? Log;
        public bool Fired, BlocksTargeting, KeepsBuffs;
        public decimal Cap = decimal.MaxValue;
        public Action<Fight, Probe>? OnPlay;
        public Action<Fight, Probe, Side>? OnSideStart;
        public Action<Fight, Probe, Creature, Creature?, ValueProp>? OnHit;

        public override string Name => "Probe";
        public override PowerType Type => PowerType.Buff;
        public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
        public override string Describe() => Label;
        public override void OnCloned(CloneMap map) { }

        public override void BeforeCardPlayed(Fight f, CardPlay play)
        {
            Log?.Add(Label);
            OnPlay?.Invoke(f, this);
        }

        public override void AfterRemoved(Fight f, Creature oldOwner) => Log?.Add($"{Label} removed, {oldOwner.Powers.Count} left");
        public override bool ShouldAllowTargeting(Fight f, Creature creature) => !(BlocksTargeting && ReferenceEquals(creature, Owner));
        public override bool ShouldPowerBeRemovedOnDeath(Fight f, Power power) => !(KeepsBuffs && power.Type == PowerType.Buff);
        public override void AfterSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants) => OnSideStart?.Invoke(f, this, side);

        public override decimal ModifyDamageCap(Fight f, Creature? target, ValueProp props, Creature? dealer, CardInstance? cardSource) =>
            ReferenceEquals(target, Owner) ? Cap : decimal.MaxValue;

        public override void AfterDamageReceived(Fight f, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardInstance? cardSource) =>
            OnHit?.Invoke(f, this, target, dealer, props);
    }

    /// <summary>Hits back before a powered attack on its owner lands (the game's Thorns shape).</summary>
    private sealed class Thorny : Power
    {
        public override string Name => "Thorny";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "Hits back first.";

        public override void BeforeDamageReceived(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource)
        {
            if (ReferenceEquals(target, Owner) && dealer is not null && props.IsPoweredAttack())
                f.Damage(dealer, Amount, Props.NonCardUnpowered, Owner);
        }
    }

    /// <summary>A debuff with a plain amount, for the checks on stacking and removal.</summary>
    private sealed class Hex : Power
    {
        public override string Name => "Hex";
        public override PowerType Type => PowerType.Debuff;
        public override string Describe() => "A plain debuff.";
    }

    /// <summary>A monster whose machine the check writes; it logs its label when a card is about to be played.</summary>
    private sealed class Puppet : Monster
    {
        public string Label = "Puppet";
        public int Low = 50, High = 50;
        public List<string>? Log;
        public Func<Puppet, MachineBuilder, MonsterState>? Build;

        public override string Name => Label;
        public override int MinHp => Low;
        public override int MaxHp => High;
        public override void OnCloned(CloneMap map) { }

        protected override MonsterState BuildMachine(MachineBuilder m) =>
            Build is not null ? Build(this, m) : Idle(m);

        private static MonsterState Idle(MachineBuilder m)
        {
            var idle = m.Move("IDLE", _ => { }, Intent.Sleep);
            idle.FollowUp = idle;
            return idle;
        }

        public override void BeforeCardPlayed(Fight f, CardPlay play) => Log?.Add(Label);
        public override void AfterCardPlayed(Fight f, CardPlay play) => Log?.Add($"{Label} after {play.Card.Title}");
    }

    /// <summary>INIT, then for ever a 50/50 choice between X and Y; or, with <paramref name="startRandom"/>, the choice at once.</summary>
    private static MonsterState CoinFlip(MachineBuilder m, bool startRandom)
    {
        var x = m.Move("X", _ => { }, Intent.Buff);
        var y = m.Move("Y", _ => { }, Intent.Defend);
        var flip = m.Random("FLIP").Add(x).Add(y);
        x.FollowUp = flip;
        y.FollowUp = flip;
        if (startRandom) return flip;
        var init = m.Move("INIT", _ => { }, Intent.Sleep);
        init.FollowUp = flip;
        return init;
    }

    private static string CoinPick(GameRng ai) => ai.NextFloat(2f) <= 1f ? "X" : "Y";

    // ---------- cards that exist only here ----------

    private static readonly CardDef Sweep = new("CheckSweep", "Sweep", CardType.Attack, CardRarity.Common, 1, TargetType.AllEnemies)
    {
        Vars = { ["Damage"] = 4 },
        Text = t => $"Deal {t.Damage()} damage to ALL enemies twice.",
        Play = c => c.Attack(c.V("Damage")).Hits(2).Execute(),
    };

    private static readonly CardDef Flurry = new("CheckFlurry", "Flurry", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy)
    {
        Vars = { ["Damage"] = 5 },
        Text = t => $"Deal {t.Damage()} damage 3 times.",
        Play = c => c.Attack(c.V("Damage")).Hits(3).Execute(),
    };

    private static readonly CardDef Scatter = new("CheckScatter", "Scatter", CardType.Attack, CardRarity.Common, 0, TargetType.RandomEnemy)
    {
        Vars = { ["Damage"] = 1 },
        Text = t => $"Deal {t.Damage()} damage to a random enemy 3 times.",
        Play = c => c.Attack(c.V("Damage")).Hits(3).Execute(),
    };

    private static readonly CardDef Grudge = new("CheckGrudge", "Grudge", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy)
    {
        Vars = { ["CalculationBase"] = 4, ["ExtraDamage"] = 2 },
        DamageMultiplier = (_, _, target) => target?.PowerAmount<VulnerablePower>() ?? 0,
        Text = t => $"Deal {t.CalculatedDamage()} damage.",
        Play = c => c.CalculatedAttack().Execute(),
    };

    private static readonly CardDef Surge = new("CheckSurge", "Surge", CardType.Skill, CardRarity.Common, CardDef.CostX, TargetType.Self)
    {
        Vars = { ["Seen"] = -1 },
        Play = c => c.Card.Set("Seen", c.X),
    };

    private static readonly CardDef Aura = new("CheckAura", "Aura", CardType.Power, CardRarity.Common, 1, TargetType.Self)
    {
        Play = c => c.ApplyToSelf<StrengthPower>(1),
    };

    private static readonly CardDef Doom = new("CheckDoom", "Doom", CardType.Skill, CardRarity.Common, 0, TargetType.Self)
    {
        Play = c =>
        {
            c.LoseHp(999);
            c.GainBlock(5);
        },
    };

    private static readonly CardDef Free = new("CheckFree", "Free", CardType.Skill, CardRarity.Common, 0, TargetType.Self);

    /// <summary>Kills its target, then tries everything a card can do afterwards.</summary>
    private static readonly CardDef Finisher = new("CheckFinisher", "Finisher", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy)
    {
        Play = c =>
        {
            c.Attack(999).Execute();
            c.GainBlock(5);
            c.ApplyToSelf<StrengthPower>(2);
            c.Draw(1);
            c.GainEnergy(1);
            c.LoseHp(3);
            c.F.Heal(c.Owner, 2);
        },
    };

    private static readonly CardDef Hollow = new("CheckHollow", "Hollow", CardType.Skill, CardRarity.Common, 1, TargetType.Self)
    {
        Keywords = CardKeywords.Retain | CardKeywords.Ethereal,
    };

    private static readonly CardDef Ember = new("CheckEmber", "Ember", CardType.Status, CardRarity.Status, CardDef.NoCost, TargetType.None)
    {
        MaxUpgradeLevel = 0,
        Keywords = CardKeywords.Unplayable | CardKeywords.Ethereal,
        TurnEndInHand = (f, card) => f.Damage(f.Player, 2, Props.CardUnpowered, f.Player, card),
    };

    private static readonly CardDef Keepsake = new("CheckKeepsake", "Keepsake", CardType.Skill, CardRarity.Common, 1, TargetType.Self)
    {
        Keywords = CardKeywords.Retain,
    };

    private static readonly CardDef ExpectAFightNamed = new("ExpectAFight", "Name check", CardType.Skill, CardRarity.Common, 1, TargetType.Self);

    private static int LastHand(Fight f) => f.Hand.Count - 1;

    /// <summary>The log without the puppets' "after card played" lines.</summary>
    private static IEnumerable<string> Before(List<string> log) => log.Where(x => !x.Contains(" after ", StringComparison.Ordinal));

    private static Creature Mob(Fight f, int j) => f.Enemy(j);

    // ---------- damage R1 to R4, R7 ----------

    private static void DamageMaths(Check c)
    {
        c.Group("damage R1, R3, R4: the chain is never rounded; Block takes its whole part, HP takes the rest's whole part", () =>
        {
            var f = CheckKit.VsDummy();
            var dummy = Mob(f, 0);
            f.ApplyPower<StrengthPower>(f.Player, 3, f.Player);
            f.ApplyPower<WeakPower>(f.Player, 1, null);
            f.ApplyPower<VulnerablePower>(dummy, 1, f.Player);

            // (6 + 3) x 0.75 x 1.5 = 10.125
            c.Equal(10.125m, f.ModifyDamage(dummy, f.Player, 6m, Props.Card, null), "the modified amount of a Strike");
            c.Equal(6.75m, f.ModifyDamage(null, f.Player, 6m, Props.Card, null), "the same with no target (the hand text)");

            f.SetBlock(dummy, 4);
            f.Play("Strike", 0);
            c.Equal(0, dummy.Block, "10.125 into 4 Block: Block gone");
            c.Equal(94, dummy.Hp, "10.125 into 4 Block: 6.125 unblocked takes 6 HP");

            f.SetBlock(dummy, 10);
            f.Play("Strike", 0);
            c.Equal(0, dummy.Block, "10.125 into 10 Block: Block gone");
            c.Equal(94, dummy.Hp, "10.125 into 10 Block: 0.125 unblocked takes no HP");

            f.SetBlock(dummy, 11);
            var hit = f.Damage(dummy, 10.125m, Props.NonCardUnpowered, f.Player);
            c.Equal(10, hit.BlockedDamage, "10.125 into 11 Block: 10 blocked");
            c.Equal(1, dummy.Block, "10.125 into 11 Block: 1 Block left");
            c.That(hit is { UnblockedDamage: 0, WasFullyBlocked: true, WasBlockBroken: false, WasTargetKilled: false }, "fully blocked, Block not broken");

            f.SetBlock(dummy, 0);
            var graze = f.Damage(dummy, 0.5m, Props.NonCardUnpowered, f.Player);
            c.That(graze is { UnblockedDamage: 0, BlockedDamage: 0, WasFullyBlocked: false }, "half a point into no Block: nothing lost, and not 'fully blocked'");
            c.Equal(94, dummy.Hp, "half a point takes no HP");

            // Unpowered and unblockable flags switch the chain and the Block off.
            f.SetBlock(dummy, 7);
            f.Damage(dummy, 5m, Props.CardHpLoss, f.Player);
            c.Equal(7, dummy.Block, "unblockable: Block untouched");
            c.Equal(89, dummy.Hp, "unblockable, unpowered: exactly 5, no Strength, no Vulnerable");
        });

        c.Group("damage R4: additions, then factors, then the lowest cap, then never below 0", () =>
        {
            var f = CheckKit.VsDummy();
            var dummy = Mob(f, 0);
            var cap = f.ApplyPower<Probe>(dummy, 1, null)!;
            cap.Cap = 5m;
            c.Equal(5m, f.ModifyDamage(dummy, f.Player, 6m, Props.Card, null), "a cap of 5 on the target");
            var lower = f.ApplyPower<Probe>(dummy, 1, null)!;
            lower.Cap = 2m;
            c.Equal(2m, f.ModifyDamage(dummy, f.Player, 6m, Props.Card, null), "the lowest cap wins");
            c.Equal(1m, f.ModifyDamage(dummy, f.Player, 1m, Props.Card, null), "an amount under the cap is left alone");

            f.ApplyPower<StrengthPower>(f.Player, -9, null);
            f.ApplyPower<VulnerablePower>(dummy, 1, null);
            c.Equal(0m, f.ModifyDamage(dummy, f.Player, 6m, Props.Card, null), "(6 - 9) x 1.5 is clamped at 0");
            f.Play("Strike", 0);
            c.Equal(100, dummy.Hp, "a hit of 0 takes nothing");
        });

        c.Group("damage R3: reactions run for blocked hits and hits of 0, and never for a dead dealer", () =>
        {
            var tap = new Tap { Only = ["AfterDamageReceived", "AfterDamageGiven", "AfterCurrentHpChanged"] };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(tap)
                .Enemy(new CheckDummy { DisplayName = "A" }).Enemy(new CheckDummy { DisplayName = "B" }).Start();
            var a = Mob(f, 0);
            f.SetBlock(a, 20);
            f.Damage(a, 6m, Props.Card, f.Player);
            f.Damage(a, 0m, Props.Card, f.Player);
            c.Sequence(["AfterDamageGiven A", "AfterDamageReceived A 0", "AfterDamageGiven A", "AfterDamageReceived A 0"], tap.Log,
                "a fully blocked hit and a hit of 0 both reach 'given' and 'received', and neither 'HP changed'");

            tap.Log.Clear();
            f.Kill([a]);
            f.Damage(f.Player, 9m, Props.MonsterMove, a);
            c.Equal(80, f.Player.Hp, "a dead dealer deals nothing");
            c.That(!tap.Log.Any(x => x.Contains("Ironclad", StringComparison.Ordinal)), "and nothing reacts to it");
        });

        c.Group("damage R20: healing and max HP", () =>
        {
            var tap = new Tap { Only = ["AfterCurrentHpChanged"] };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(70).Relic(tap).Enemy(new CheckDummy()).Start();
            f.Heal(f.Player, 0m);
            c.Equal(0, tap.Log.Count, "a heal of 0 changes nothing and tells nobody");
            f.Heal(f.Player, 4.9m);
            c.Equal(74, f.Player.Hp, "70 + 4.9 is written as 74");
            f.Heal(f.Player, 50m);
            c.Equal(80, f.Player.Hp, "capped at max HP");
            c.Sequence(["AfterCurrentHpChanged The Ironclad 4.9", "AfterCurrentHpChanged The Ironclad 50"], tap.Log, "the hook carries the amount asked for");

            f.SetHp(f.Player, 70);
            f.GainMaxHp(f.Player, 5m);
            c.That(f.Player is { MaxHp: 85, Hp: 75 }, "gaining 5 max HP heals 5");
            f.SetBlock(f.Player, 9);
            f.SetHp(f.Player, 85);
            f.LoseMaxHp(f.Player, 10m, isFromCard: false);
            c.That(f.Player is { MaxHp: 75, Hp: 75, Block: 9 }, "losing 10 max HP at full HP costs 10 HP past the Block");
        });
    }

    // ---------- damage R3, R6; turns R27, R45 ----------

    private static void AttacksAndDeaths(Check c)
    {
        c.Group("turns R27, damage R3: an attack on all enemies goes hit by hit; all lose HP, then reactions, then deaths", () =>
        {
            var tap = new Tap { Only = ["AfterDamageGiven", "AfterDamageReceived", "BeforeDeath", "AfterDeath"] };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(tap)
                .Enemy(new CheckDummy { DisplayName = "A", Hp = 5 }).Enemy(new CheckDummy { DisplayName = "B", Hp = 30 }).Start();
            tap.State = x => $"[{x.Roster[0].Hp},{x.Roster[1].Hp}]";
            f.Put(Sweep, PileType.Hand);
            f.Do($"play:{LastHand(f)}");

            // 4 per hit, two hits. Hit 1: A 5 to 1, B 30 to 26. Hit 2: A dies, B 26 to 22.
            c.Sequence(
            [
                "AfterDamageGiven A [1,26]", "AfterDamageReceived A 4 [1,26]", "AfterDamageGiven B [1,26]", "AfterDamageReceived B 4 [1,26]",
                "AfterDamageGiven A [0,22]", "AfterDamageGiven B [0,22]", "AfterDamageReceived B 4 [0,22]",
                "BeforeDeath A [0,22]", "AfterDeath A [0,22]",
            ], tap.Log, "the order of one area attack");
            c.Equal(1, f.Enemies.Count, "the dead enemy has left the list");
            c.Equal(22, Mob(f, 1).Hp, "the survivor took both hits");
        });

        c.Group("damage R6: a single target that dies takes the remaining hits with it", () =>
        {
            var tap = new Tap { Only = ["AfterDamageGiven"] };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(tap)
                .Enemy(new CheckDummy { DisplayName = "A", Hp = 8 }).Enemy(new CheckDummy { DisplayName = "B", Hp = 30 }).Start();
            f.Put(Flurry, PileType.Hand);
            f.Do($"play:{LastHand(f)}>e0");
            c.Sequence(["AfterDamageGiven A", "AfterDamageGiven A"], tap.Log, "5 x 3 into 8 HP: two hits happen, the third is lost");
            c.Equal(30, Mob(f, 1).Hp, "no retargeting");
        });

        c.Group("damage R6: a random target is drawn per hit from CombatTargets, also with one enemy", () =>
        {
            var f = CheckKit.VsDummy();
            f.Put(Scatter, PileType.Hand);
            var before = f.Rng.CombatTargets.Counter;
            f.Do($"play:{LastHand(f)}");
            c.Equal(3, f.Rng.CombatTargets.Counter - before, "three hits, three draws, with a single enemy");
            c.Equal(97, Mob(f, 0).Hp, "three hits of 1");

            var two = CheckKit.VsDummies(new CheckDummy { DisplayName = "A", Hp = 50 }, new CheckDummy { DisplayName = "B", Hp = 50 });
            two.Put(Scatter, PileType.Hand);
            var rng = two.Rng.CombatTargets.Clone();
            var expected = new[] { 50, 50 };
            for (var hit = 0; hit < 3; hit++) expected[rng.NextInt(0, 2)]--;
            two.Do($"play:{LastHand(two)}");
            c.Sequence(expected, new[] { Mob(two, 0).Hp, Mob(two, 1).Hp }, "each hit lands on the enemy the stream names");
            c.Equal(0, two.Rng.Shuffle.Counter + two.Rng.MonsterAi.Counter, "and no other stream moves");
        });

        c.Group("turns R45: a monster killed before it acts is skipped; killing the last one there wins at once", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder)
                .Enemy(new CheckDummy { DisplayName = "A", Hp = 20, Steps = [new DummyStep("HIT", Damage: 9)] })
                .Enemy(new CheckDummy { DisplayName = "B", Hp = 20, Steps = [new DummyStep("HIT", Damage: 4)] }).Start();
            var bomb = f.ApplyPower<Probe>(Mob(f, 0), 1, null)!;
            bomb.OnSideStart = (x, me, side) =>
            {
                if (side == Side.Enemy) x.Damage(me.Owner, 999m, Props.NonCardHpLoss, null);
            };
            f.EndTurn();
            c.Equal(76, f.Player.Hp, "only B's 4 landed: A died at the start of the enemy turn");
            c.That(!f.IsOver && f.Turn == 2, "the fight goes on");

            var solo = CheckKit.VsDummy(new CheckDummy { Hp = 20, Steps = [new DummyStep("HIT", Damage: 9)] });
            solo.SetHp(solo.Player, 50);
            var fuse = solo.ApplyPower<Probe>(Mob(solo, 0), 1, null)!;
            fuse.OnSideStart = (x, me, side) =>
            {
                if (side == Side.Enemy) x.Damage(me.Owner, 999m, Props.NonCardHpLoss, null);
            };
            solo.EndTurn();
            c.That(solo is { IsOver: true, Won: true }, "the last enemy dying before it acts is a win");
            c.Equal(56, solo.Player.Hp, "no hit landed, and Burning Blood healed 6");
        });

        c.Group("turns R41, R45: a reaction before the hit kills the attacker: that hit lands, the rest are lost, the move goes on", () =>
        {
            DummyStep Bite() => new("BITE", Damage: 5, Hits: 3, Then: (x, me) => x.ApplyPower<WeakPower>(x.Player, 1, me));

            var f = CheckKit.VsDummies(new CheckDummy { DisplayName = "A", Hp = 3, Steps = [Bite()] }, new CheckDummy { DisplayName = "B" });
            f.ApplyPower<Thorny>(f.Player, 3, f.Player);
            f.EndTurn();
            c.Equal(75, f.Player.Hp, "the first hit of 5 still landed; hits two and three never happened");
            c.Equal(1, f.Player.PowerAmount<WeakPower>(), "the dead monster finished its move: the Weak landed, while another enemy lives");
            c.Equal(1, f.Enemies.Count, "and it left the list when its move was done");

            var solo = CheckKit.VsDummy(new CheckDummy { Hp = 3, Steps = [Bite()] });
            solo.SetHp(solo.Player, 50);
            solo.ApplyPower<Thorny>(solo.Player, 3, solo.Player);
            solo.EndTurn();
            c.That(solo is { IsOver: true, Won: true }, "the last enemy dying on its own attack is a win");
            c.Equal(51, solo.Player.Hp, "the hit that provoked it still cost 5; then Burning Blood");
        });

        c.Group("turns R3, R27: after the killing blow commands do nothing, but damage, HP loss and healing still do", () =>
        {
            var seen = "";
            var tap = new Tap { Only = ["AfterCardPlayed"] };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(50).Relic(tap).Enemy(new CheckDummy { Hp = 30 }).Start();
            tap.State = x => seen = $"block {x.Player.Block}, strength {x.Player.PowerAmount<StrengthPower>()}, hand {x.Hand.Count}, energy {x.Energy}, hp {x.Player.Hp}";
            f.Put(Finisher, PileType.Hand);
            f.Do($"play:{LastHand(f)}>e0");
            c.Equal("block 0, strength 0, hand 5, energy 2, hp 49", seen, "what the card left behind when 'after card played' ran");
            c.That(f is { IsOver: true, Won: true }, "the fight is won after the card");
            c.Equal(55, f.Player.Hp, "49, then Burning Blood");
        });

        c.Group("turns R24, R47: a player who dies in the middle of a card: the card stays where it is and nothing after runs", () =>
        {
            var log = new List<string>();
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(new Puppet { Log = log }).Start();
            var doom = f.Put(Doom, PileType.Hand);
            f.Do($"play:{LastHand(f)}");
            c.That(f is { IsOver: true, Won: false }, "the fight is lost");
            c.Equal(PileType.Play, doom.Pile, "the card never left the Play pile");
            c.Equal(0, f.Player.Block, "the Block after the death was not gained");
            c.Sequence(["Puppet"], log, "'before card played' ran, 'after card played' did not");
            c.Equal(0, f.VictoryHeal, "no heal on a loss");
        });
    }

    // ---------- turns R2 ----------

    private static void ListenerOrder(Check c)
    {
        c.Group("turns R2: who hears a hook first", () =>
        {
            var log = new List<string>();
            var marked = new CardDef("CheckMarked", "Marked", CardType.Skill, CardRarity.Common, 0, TargetType.Self)
            {
                Behaviour = () => new Mark { Log = log },
            };
            var tap = new Tap { Log = log, Only = ["BeforeCardPlayed"] };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(tap)
                .Enemy(new Puppet { Label = "M0", Log = log }).Enemy(new Puppet { Label = "M1", Log = log }).Start();

            Probe Add(Creature who, string label)
            {
                var p = f.ApplyPower<Probe>(who, 1, null)!;
                p.Label = label;
                p.Log = log;
                return p;
            }

            var p1 = Add(f.Player, "P1");
            Add(f.Player, "P2");
            Add(Mob(f, 1), "E1p");
            Add(Mob(f, 0), "E0p");
            f.Put(marked, PileType.Draw);
            f.Put(marked, PileType.Discard);
            f.Put(marked, PileType.Exhaust);
            f.Put(marked, PileType.Hand);
            f.Put(marked, PileType.Hand);
            log.Clear();
            f.Do($"play:{LastHand(f) - 1}");

            c.Sequence(
                ["P1", "P2", "BeforeCardPlayed Tap", "card in Hand", "card in Draw", "card in Discard", "card in Exhaust", "card in Play", "E0p", "M0", "E1p", "M1"],
                Before(log), "player powers in applied order, relics, cards by pile, then each enemy's powers and the monster");

            // A power that is removed and applied again goes to the end.
            f.RemovePower(p1);
            Add(f.Player, "P1");
            log.Clear();
            f.Do($"play:{LastHand(f)}");
            c.Sequence(["P2", "P1"], log.Take(2), "re-applied: now after the power that stayed");
        });

        c.Group("turns R2: the list is a snapshot: who joins is not called, who was only removed still is, who left the fight is not", () =>
        {
            var log = new List<string>();
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder)
                .Enemy(new Puppet { Label = "M0", Log = log }).Enemy(new Puppet { Label = "M1", Log = log }).Start();

            Probe Add(Creature who, string label)
            {
                var p = f.ApplyPower<Probe>(who, 1, null)!;
                p.Label = label;
                p.Log = log;
                return p;
            }

            var adder = Add(f.Player, "Adder");
            adder.OnPlay = (x, me) =>
            {
                if (me.Fired) return;
                me.Fired = true;
                var late = x.ApplyPower<Probe>(me.Owner, 1, null)!;
                late.Label = "Late";
                late.Log = me.Log;
            };
            f.Put(Free, PileType.Hand);
            f.Put(Free, PileType.Hand);
            f.Put(Free, PileType.Hand);
            f.Put(Free, PileType.Hand);
            f.Do($"play:{LastHand(f)}");
            c.Sequence(["Adder", "M0", "M1"], Before(log), "a power applied during the dispatch does not hear it");
            log.Clear();
            f.Do($"play:{LastHand(f)}");
            c.Sequence(["Adder", "Late", "M0", "M1"], Before(log), "it hears the next one");

            // Removed from a living owner earlier in the same dispatch: still called.
            var late2 = f.Player.Powers.OfType<Probe>().First(p => p.Label == "Late");
            adder.OnPlay = (x, _) => x.RemovePower(late2);
            log.Clear();
            f.Do($"play:{LastHand(f)}");
            c.Sequence(["Adder", "Late removed, 1 left", "Late", "M0", "M1"], Before(log), "a power removed during the dispatch still gets that call");
            c.Equal(1, f.Player.Powers.Count, "and it is gone from its owner");

            // A monster that left the fight during the dispatch, and its powers, are skipped.
            Add(Mob(f, 0), "E0p");
            adder.OnPlay = (x, _) => x.Kill([x.Roster[0]]);
            log.Clear();
            f.Do($"play:{LastHand(f)}");
            c.Sequence(["Adder", "E0p removed, 0 left", "M1"], Before(log), "the dead monster and its power hear nothing more");
        });
    }

    private sealed class Mark : CardBehaviour
    {
        public List<string>? Log;
        public override void OnCloned(CloneMap map) { }
        public override void BeforeCardPlayed(Fight f, CardPlay play) => Log?.Add($"card in {Card.Pile}");
    }

    // ---------- turns R52, R53; damage R15 ----------

    private static void Durations(Check c)
    {
        c.Group("turns R53: a debuff the player gives themself on their own turn also skips its first tick", () =>
        {
            var f = CheckKit.VsDummy();
            f.ApplyPower<VulnerablePower>(f.Player, 1, f.Player);
            f.EndTurn();
            c.Equal(1, f.Player.PowerAmount<VulnerablePower>(), "turn 2: still 1");
            f.EndTurn();
            c.Equal(0, f.Player.PowerAmount<VulnerablePower>(), "turn 3: gone");
        });

        c.Group("turns R53: stacks added before the first tick are kept in full; adding to an instance never skips again", () =>
        {
            var f = CheckKit.VsDummies(
                new CheckDummy { DisplayName = "A", Steps = [new DummyStep("HEX", Then: (x, me) => x.ApplyPower<VulnerablePower>(x.Player, 1, me)), new DummyStep("IDLE")] },
                new CheckDummy { DisplayName = "B", Steps = [new DummyStep("HEX", Then: (x, me) => x.ApplyPower<VulnerablePower>(x.Player, 2, me)), new DummyStep("IDLE")] });
            f.EndTurn();
            c.Equal(3, f.Player.PowerAmount<VulnerablePower>(), "A applies 1, B adds 2 in the same enemy turn: 3 on turn 2");
            f.EndTurn();
            c.Equal(2, f.Player.PowerAmount<VulnerablePower>(), "turn 3: one tick");
            f.EndTurn();
            c.Equal(4, f.Player.PowerAmount<VulnerablePower>(), "turn 4: 2 + 1 + 2 landed on the instance, then the normal tick");
        });

        c.Group("turns R52, R53: debuffs on monsters never skip, whoever applied them", () =>
        {
            var f = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("SELF", Then: (x, me) => x.ApplyPower<WeakPower>(me, 2, me)), new DummyStep("IDLE"), new DummyStep("IDLE2")] });
            f.Play("Bash", 0);
            c.Equal(2, Mob(f, 0).PowerAmount<VulnerablePower>(), "Bash: 2 Vulnerable");
            f.EndTurn();
            c.Equal(1, Mob(f, 0).PowerAmount<VulnerablePower>(), "turn 2: the player's 2 Vulnerable is 1");
            c.Equal(1, Mob(f, 0).PowerAmount<WeakPower>(), "turn 2: the 2 Weak the monster gave itself during its turn is already 1");
            f.EndTurn();
            c.Equal(0, Mob(f, 0).PowerAmount<VulnerablePower>(), "turn 3: gone");
            c.Equal(0, Mob(f, 0).PowerAmount<WeakPower>(), "turn 3: gone");
        });

        c.Group("turns R53: only the duration tick honours the skip flag: Shrink with a duration counts down plainly", () =>
        {
            var f = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("SHRINK", Then: (x, me) => x.ApplyPower<ShrinkPower>(x.Player, 2, me)), new DummyStep("IDLE"), new DummyStep("IDLE2")] });
            f.EndTurn();
            c.Equal(2, f.Player.PowerAmount<ShrinkPower>(), "turn 2: 2");
            f.EndTurn();
            c.Equal(1, f.Player.PowerAmount<ShrinkPower>(), "turn 3: it lost 1 at the end of the player's turn 2, flag or no flag");
            f.EndTurn();
            c.Equal(0, f.Player.PowerAmount<ShrinkPower>(), "turn 4: gone");
        });
    }

    // ---------- turns R11, R12, R15, R40, R42 ----------

    private static void BlockAndTurnStart(Check c)
    {
        c.Group("turns R12: Block gained in 'before side turn start' is wiped (not on turn 1); 'after block cleared' Block stays", () =>
        {
            var guard = new Tap
            {
                Only = [],
                OnBeforeSideTurnStart = (x, side) =>
                {
                    if (side == Side.Player) x.GainBlock(x.Player, 7, Props.NonCardUnpowered);
                },
                OnAfterBlockCleared = (x, creature) =>
                {
                    if (creature.IsPlayer) x.GainBlock(x.Player, 3, Props.NonCardUnpowered);
                },
            };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(guard).Enemy(new CheckDummy()).Start();
            c.Equal(10, f.Player.Block, "turn 1: nothing is cleared, and 'after block cleared' still runs: 7 + 3");
            f.EndTurn();
            c.Equal(3, f.Player.Block, "turn 2: 10 + 7 wiped, then 3");
        });

        c.Group("turns R40: every monster's Block falls at the start of the enemy turn, before the start hooks", () =>
        {
            var log = new List<string>();
            var f = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("GUARD", Block: 5)] });
            var watch = f.ApplyPower<Probe>(Mob(f, 0), 1, null)!;
            watch.OnSideStart = (x, me, side) =>
            {
                if (side == Side.Enemy) log.Add(string.Create(CultureInfo.InvariantCulture, $"{me.Owner.Block}"));
            };
            f.EndTurn();
            c.Equal(5, Mob(f, 0).Block, "turn 2: the Block from its move is still up");
            f.EndTurn();
            c.Equal(5, Mob(f, 0).Block, "turn 3: 5 again, not 10");
            c.Sequence(["0", "0"], log, "'after side turn start' of the enemy side sees no Block");
        });

        c.Group("turns R42, R12: monsters choose their move while the player's Block from last turn is still up", () =>
        {
            var puppet = new Puppet
            {
                Build = (self, m) =>
                {
                    var init = m.Move("INIT", _ => { }, Intent.Sleep);
                    var guarded = m.Move("SAW_BLOCK", _ => { }, Intent.Buff);
                    var open = m.Move("SAW_NONE", _ => { }, Intent.Debuff);
                    var look = m.Conditional("LOOK").When(x => x.Player.Block > 0, guarded).Otherwise(open);
                    init.FollowUp = look;
                    guarded.FollowUp = look;
                    open.FollowUp = look;
                    return init;
                },
            };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(puppet).Start();
            f.Play("Defend");
            f.EndTurn();
            c.Equal("SAW_BLOCK", puppet.NextMove!.Id, "the condition was read before the Block fell");
            c.Equal(0, f.Player.Block, "and the Block is gone when the player may act");
            f.EndTurn();
            c.Equal("SAW_NONE", puppet.NextMove!.Id, "next turn there was none");
            c.Sequence(["INIT", "SAW_BLOCK", "SAW_NONE"], puppet.MoveLog, "the log holds chosen moves, never the branch");
        });

        c.Group("turns R11, R13, R15: the start of a player turn, step by step", () =>
        {
            var tap = new Tap
            {
                When = x => x is { Turn: 2, CurrentSide: Side.Player },
                Only = ["BeforeSideTurnStart", "AfterBlockCleared", "AfterEnergyReset", "BeforeHandDraw", "AfterPlayerTurnStart", "AfterSideTurnStart", "AfterAutoPrePlayPhaseEntered"],
                State = x => $"hand {x.Hand.Count}, energy {x.Energy}, block {x.Player.Block}",
            };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(tap).Enemy(new CheckDummy()).Start();
            f.Play("Defend");
            f.EndTurn();
            c.Sequence(
            [
                "BeforeSideTurnStart Player hand 0, energy 2, block 5",
                "AfterBlockCleared The Ironclad hand 0, energy 2, block 0",
                "AfterEnergyReset hand 0, energy 3, block 0",
                "BeforeHandDraw hand 0, energy 3, block 0",
                "AfterPlayerTurnStart hand 5, energy 3, block 0",
                "AfterSideTurnStart Player hand 5, energy 3, block 0",
                "AfterAutoPrePlayPhaseEntered hand 5, energy 3, block 0",
            ], tap.Log, "turn 2");
        });
    }

    // ---------- turns R32 to R37 ----------

    private static void TurnEnd(Check c)
    {
        c.Group("turns R32 to R35: Ethereal first (Retain or not), then end-of-turn cards left to right, then the flush", () =>
        {
            var tap = new Tap
            {
                When = x => x.CurrentSide == Side.Player,
                Only = ["AfterCardExhausted", "AfterDamageReceived", "BeforeFlush", "AfterFlush", "AfterSideTurnEnd"],
            };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(tap).Enemy(new CheckDummy()).Start();
            f.Clear(PileType.Hand);
            f.Put("Dazed", PileType.Hand);
            f.Put("Burn", PileType.Hand);
            f.Put(Hollow, PileType.Hand);
            f.Put(Ember, PileType.Hand);
            f.Put("StrikeIronclad", PileType.Hand);
            f.Put(Keepsake, PileType.Hand);
            tap.Log.Clear();
            f.EndTurn();

            c.Sequence(
            [
                "AfterCardExhausted Dazed ethereal=True",
                "AfterCardExhausted Hollow ethereal=True",
                "AfterDamageReceived The Ironclad 2",
                "AfterDamageReceived The Ironclad 2",
                "AfterCardExhausted Ember ethereal=True",
                "BeforeFlush",
                "AfterFlush [Strike] kept [Keepsake]",
                "AfterSideTurnEnd Player",
            ], tap.Log, "the player's turn end");
            c.Equal(76, f.Player.Hp, "Burn 2 and the Ethereal end-of-turn card 2");
            c.Sequence(["Dazed", "Hollow", "Ember"], f.ExhaustPile.Select(x => x.Title), "the exhaust pile, in order");
            c.Sequence(["Burn", "Strike"], f.DiscardPile.Select(x => x.Title), "Burn went to the discard pile before the flush");
            c.Equal("Keepsake", f.Hand[0].Title, "a retained card keeps its place; the new hand is appended");
            c.Equal(6, f.Hand.Count, "1 kept + 5 drawn");
        });

        c.Group("turns R34, damage R18: Block from the early pass of the turn end is there to absorb a Burn", () =>
        {
            var seen = "";
            var tap = new Tap { When = x => x.CurrentSide == Side.Player, Only = ["AfterFlush"] };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(tap).Enemy(new CheckDummy()).Start();
            tap.State = x => seen = $"block {x.Player.Block}, hp {x.Player.Hp}";
            f.ApplyPower<PlatingPower>(f.Player, 3, f.Player);
            f.Clear(PileType.Hand);
            f.Put("Burn", PileType.Hand);
            f.EndTurn();
            c.Equal("block 1, hp 80", seen, "Plating 3 arrived first; Burn's 2 went into it");
        });

        c.Group("turns R37: one-turn flags end with the player's turn, and again with the enemy turn", () =>
        {
            var tap = new Tap { Only = ["AfterFlush"] };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(tap)
                .Enemy(new CheckDummy { Steps = [new DummyStep("MARK", Then: (x, _) =>
                {
                    if (x.DrawPile.Count > 0) x.DrawPile[0].ExhaustOnNextPlay = true;
                })] }).Start();
            var kept = f.Hand[4];
            kept.RetainThisTurn = true;
            f.EndTurn();
            c.Equal("AfterFlush [Strike,Strike,Defend,Defend] kept [Bash]", tap.Log[0], "turn 1: the card kept for one turn stays");
            c.That(ReferenceEquals(f.Hand[0], kept), "it is the leftmost card of the next hand");
            c.That(!kept.RetainThisTurn, "and the flag is gone");
            c.That(f.Hand.All(x => !x.ExhaustOnNextPlay) && f.DrawPile.All(x => !x.ExhaustOnNextPlay), "a flag set during the enemy turn ended with that turn");
            f.EndTurn();
            c.Equal("AfterFlush [Bash,Strike,Strike,Strike,Defend,Defend] kept []", tap.Log[1], "turn 2: it is flushed like any card");
        });
    }

    // ---------- turns R17 to R21 ----------

    private static void DrawingAndShuffling(Check c)
    {
        c.Group("turns R17: 'card drawn' effects run card by card, in the middle of the draw", () =>
        {
            var log = new List<string>();
            var tally = new CardDef("CheckTally", "Tally", CardType.Skill, CardRarity.Common, 1, TargetType.Self)
            {
                Behaviour = () => new Tally { Log = log },
            };
            var builder = new FightBuilder().DrawOrder().Add(tally).Add("StrikeIronclad").Add(tally);
            for (var i = 0; i < 7; i++) builder.Add("StrikeIronclad");
            builder.Enemy(new CheckDummy()).Start();
            c.Sequence(["1", "3"], log, "the hand's size when the first and the third card were drawn");
        });

        c.Group("turns R19: a full hand stops a draw and leaves the card on top; other ways into a full hand end in the discard pile", () =>
        {
            var f = CheckKit.VsDummy();
            for (var i = 0; i < 4; i++) f.Put("Wound", PileType.Hand);
            c.Equal(1, f.Draw(3).Count, "9 in hand: a draw of 3 draws 1");
            c.Equal(10, f.Hand.Count, "the hand is full");
            var top = f.DrawPile[0];
            c.Equal(0, f.Draw(2).Count, "10 in hand: nothing is drawn");
            c.That(ReferenceEquals(top, f.DrawPile[0]), "the card stays on top of the draw pile");
            var discards = f.DiscardPile.Count;
            f.MoveCard(top, PileType.Hand);
            c.Equal(PileType.Discard, top.Pile, "a card moved to a full hand lands in the discard pile");
            c.Equal(discards + 1, f.DiscardPile.Count, "one more card there");
        });

        c.Group("turns R18: the pre-shuffle sort is the game's own: by model id, then upgrade level", () =>
        {
            c.Equal("STRIKE_IRONCLAD", Fight.SortKey(Content.Registry.Card("StrikeIronclad")), "StrikeIronclad");
            c.Equal("EXPECT_A_FIGHT", Fight.SortKey(ExpectAFightNamed), "an underscore before every capital that follows a letter, also after a capital");

            var f = CheckKit.VsDummy();
            f.Clear(PileType.Draw);
            f.Clear(PileType.Discard);
            var plus = f.Put("Bash+", PileType.Discard);
            var plain = f.Put("Bash", PileType.Discard);
            var defend = f.Put("DefendIronclad", PileType.Discard);
            var strike = f.Put("StrikeIronclad", PileType.Discard);
            var rng = f.Rng.Shuffle.Clone();
            var expected = new List<CardInstance> { plain, plus, defend, strike };
            rng.Shuffle(expected);
            f.Shuffle();
            c.Sequence(expected.Select(x => x.Id), f.DrawPile.Select(x => x.Id), "BASH, BASH+, DEFEND_IRONCLAD, STRIKE_IRONCLAD, then the game's shuffle");
            c.Equal(3, f.Rng.Shuffle.Counter, "four cards: three draws from the Shuffle stream");
        });

        c.Group("turns R18: three cards are sorted by the runtime's fixed swaps, which turn two identical copies around", () =>
        {
            var f = CheckKit.VsDummy();
            f.Clear(PileType.Draw);
            f.Clear(PileType.Discard);
            var first = f.Put("StrikeIronclad", PileType.Discard);
            first.SetCostThisCombat(0);
            var second = f.Put("StrikeIronclad", PileType.Discard);
            var bash = f.Put("Bash", PileType.Discard);

            // [first, second, Bash]: compare 0-1 equal, keep; compare 0-2 STRIKE above BASH, swap:
            // [Bash, second, first]; compare 1-2 equal, keep. The cheaper copy is now behind its twin.
            var rng = f.Rng.Shuffle.Clone();
            var expected = new List<CardInstance> { bash, second, first };
            rng.Shuffle(expected);
            f.Shuffle();
            c.Sequence(expected.Select(x => x.Id), f.DrawPile.Select(x => x.Id), "the order the game's sort and shuffle give");
        });
    }

    private sealed class Tally : CardBehaviour
    {
        public List<string>? Log;
        public override void OnCloned(CloneMap map) { }

        public override void AfterCardDrawn(Fight f, CardInstance card, bool fromHandDraw)
        {
            if (ReferenceEquals(card, Card)) Log?.Add(string.Create(CultureInfo.InvariantCulture, $"{f.Hand.Count}"));
        }
    }

    // ---------- turns R21, R23, R29 ----------

    private static void GeneratedCardsAndAutoPlay(Check c)
    {
        c.Group("turns R21: where a generated card lands", () =>
        {
            var f = CheckKit.VsDummy();
            var slimed = f.CreateCard("Slimed");
            f.AddGeneratedCard(slimed, PileType.Draw);
            c.That(ReferenceEquals(f.DrawPile[^1], slimed), "into the draw pile: the bottom by default, drawn last");

            var rng = f.Rng.Shuffle.Clone();
            var index = rng.NextInt(f.DrawPile.Count + 1);
            var wound = f.CreateCard("Wound");
            f.AddGeneratedCard(wound, PileType.Draw, PilePosition.Random);
            c.Equal(index, f.DrawPile.ToList().IndexOf(wound), "a random position: one Shuffle draw over 0 to the pile's size");

            f.AddStatusCards("Dazed", 2, PileType.Discard);
            c.Sequence(["Dazed", "Dazed"], f.DiscardPile.Select(x => x.Title), "status cards are appended to the discard pile");

            for (var i = 0; i < 5; i++) f.Put("Wound", PileType.Hand);
            var extra = f.CreateCard("Slimed");
            f.AddGeneratedCard(extra, PileType.Hand);
            c.Equal(PileType.Discard, extra.Pile, "a card generated into a full hand goes to the discard pile");
        });

        c.Group("turns R21, damage R8: before the fight is in progress no card is added and no Block gained, but powers land", () =>
        {
            var f = CheckKit.VsDummy(new CheckDummy
            {
                OnAdded = (x, me) =>
                {
                    x.AddStatusCards("Wound", 2, PileType.Discard, PilePosition.Bottom, me);
                    x.GainBlock(me, 13, Props.MonsterMove);
                    x.ApplyPower<ArtifactPower>(me, 1, me);
                },
            });
            c.Equal(10, f.AllCards.Count(), "no Wound was added");
            c.Equal(0, Mob(f, 0).Block, "no Block");
            c.Equal(1, Mob(f, 0).PowerAmount<ArtifactPower>(), "the Artifact is there");
        });

        c.Group("turns R23, R29: X takes all energy when played; auto-played it reads the energy without spending it", () =>
        {
            var f = CheckKit.VsDummy();
            var surge = f.Put(Surge, PileType.Hand);
            f.Do($"play:{LastHand(f)}");
            c.Equal(3m, surge.Get("Seen"), "played with 3 energy: X is 3");
            c.Equal(0, f.Energy, "and the energy is spent");

            var auto = CheckKit.VsDummy();
            var free = auto.Put(Surge, PileType.Hand);
            auto.SetEnergy(2);
            auto.AutoPlay(free);
            c.Equal(2m, free.Get("Seen"), "auto-played with 2 energy: X is 2");
            c.Equal(2, auto.Energy, "and nothing is spent");
            c.Equal(PileType.Discard, free.Pile, "it went to its pile");
        });

        c.Group("turns R29: an auto-played card rolls its target once from CombatTargets; an Unplayable one just moves on", () =>
        {
            var tap = new Tap { Only = ["BeforeCardPlayed", "AfterEnergySpent"] };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(tap).Enemy(new CheckDummy()).Start();
            f.Put("Wound", PileType.Draw, PilePosition.Top);
            f.AutoPlayFromDrawPile(1);
            c.Equal(0, tap.Log.Count, "the Wound was never played");
            c.Equal("Wound", f.DiscardPile[^1].Title, "it went to the discard pile");
            c.Equal(0, f.Rng.CombatTargets.Counter, "no target was rolled for it");

            f.Put("StrikeIronclad", PileType.Draw, PilePosition.Top);
            f.AutoPlayFromDrawPile(1);
            c.Equal(1, f.Rng.CombatTargets.Counter, "a Strike: one draw for its target, with a single enemy");
            c.Equal(94, Mob(f, 0).Hp, "it hit");
            c.Equal(3, f.Energy, "nothing was paid");
            c.Sequence(["BeforeCardPlayed Tap"], tap.Log, "played, and no energy was spent");
        });
    }

    // ---------- turns R23 to R26 ----------

    private static void CardPlays(Check c)
    {
        c.Group("turns R25, R26: a Power card leaves the fight after its play, and that is a change of piles like any other", () =>
        {
            var tap = new Tap { Only = ["AfterCardChangedPiles", "AfterEnergySpent"] };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(tap).Enemy(new CheckDummy()).Start();
            var aura = f.Put(Aura, PileType.Hand);
            tap.Log.Clear();
            f.Do($"play:{LastHand(f)}");
            c.Sequence(["AfterEnergySpent Aura 1", "AfterCardChangedPiles Aura Hand->Play", "AfterCardChangedPiles Aura Play->None"], tap.Log,
                "paid, to the Play pile, out of the fight");
            c.That(!f.AllCards.Contains(aura), "the card is in no pile");
            c.Equal(1, f.Player.PowerAmount<StrengthPower>(), "its effect ran");
        });

        c.Group("turns R23: 'after energy spent' runs for a card that costs nothing, with 0", () =>
        {
            var tap = new Tap { Only = ["AfterEnergySpent"] };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(tap).Enemy(new CheckDummy()).Start();
            f.Put(Free, PileType.Hand);
            f.Do($"play:{LastHand(f)}");
            c.Sequence(["AfterEnergySpent Free 0"], tap.Log, "the hook");
        });
    }

    // ---------- turns R5, R6, R8, R42 ----------

    private static void MonsterChoices(Check c)
    {
        c.Group("turns R42: all monsters choose at the start of the next player turn, in list order, one MonsterAi draw each", () =>
        {
            var m0 = new Puppet { Label = "M0", Build = (self, m) => CoinFlip(m, startRandom: false) };
            var m1 = new Puppet { Label = "M1", Build = (self, m) => CoinFlip(m, startRandom: false) };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Seed(31).Enemy(m0).Enemy(m1).Start();
            c.Equal(0, f.Rng.MonsterAi.Counter, "a first move that is fixed uses no number, also not at the start of turn 1");
            c.Equal("INIT", m0.NextMove!.Id, "the first intent stands on turn 1");

            for (var turn = 2; turn <= 4; turn++)
            {
                var ai = f.Rng.MonsterAi.Clone();
                var first = CoinPick(ai);
                var second = CoinPick(ai);
                f.EndTurn();
                c.Equal(first, m0.NextMove!.Id, $"turn {turn}: the first draw is the first monster's");
                c.Equal(second, m1.NextMove!.Id, $"turn {turn}: the second draw is the second monster's");
                c.Equal((turn - 1) * 2, f.Rng.MonsterAi.Counter, $"turn {turn}: two draws per round");
            }
        });

        c.Group("turns R5, R6, R8: HP is rolled in creation order, first intents in list order", () =>
        {
            var back = new Puppet { Label = "back", Low = 10, High = 12, Build = (self, m) => CoinFlip(m, startRandom: true) };
            var front = new Puppet { Label = "front", Low = 20, High = 22, Build = (self, m) => CoinFlip(m, startRandom: true) };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Seed(77).Slots("front", "back")
                .Enemy(back, slot: "back").Enemy(front, slot: "front").Start();

            var streams = new RngSet(77);
            var backHp = 10 + streams.Niche.NextInt(0, 3);
            var frontHp = 20 + streams.Niche.NextInt(0, 3);
            var frontMove = CoinPick(streams.MonsterAi);
            var backMove = CoinPick(streams.MonsterAi);

            c.That(ReferenceEquals(f.Enemies[0].Monster, front) && ReferenceEquals(f.Enemies[1].Monster, back), "the list is in slot order");
            c.That(ReferenceEquals(f.Enemy(0).Monster, front), "e0 is the first of the list");
            c.Equal(backHp, back.Creature.Hp, "the first Niche draw is the monster created first");
            c.Equal(frontHp, front.Creature.Hp, "the second is the one created second");
            c.Equal(frontMove, front.NextMove!.Id, "the first MonsterAi draw is the monster first in the list");
            c.Equal(backMove, back.NextMove!.Id, "the second is the next in the list");
            c.Equal(2, f.Rng.MonsterAi.Counter, "one draw per random first intent");
            c.Equal(2, f.Rng.Niche.Counter, "one draw per HP roll");
            c.Equal(0, f.Rng.MonsterAi.Counter - 2 + f.Rng.CombatTargets.Counter, "nothing else was drawn at the start of turn 1");
        });

        c.Group("turns R39, R42: a monster summoned in the enemy turn waits, gets its first intent with the others, and keeps the next e-index", () =>
        {
            var summoner = new Puppet
            {
                Label = "Caller",
                Build = (self, m) =>
                {
                    var x = m.Move("X", _ => { }, Intent.Buff);
                    var y = m.Move("Y", _ => { }, Intent.Defend);
                    var flip = m.Random("FLIP").Add(x).Add(y);
                    x.FollowUp = flip;
                    y.FollowUp = flip;
                    var call = m.Move("CALL", f => f.AddMonster(new Puppet { Label = "Summon", Low = 30, High = 30, Build = (other, mm) => CoinFlip(mm, startRandom: true) }, "first"), Intent.Summon);
                    call.FollowUp = flip;
                    return call;
                },
            };
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Seed(12).Slots("first", "second").Enemy(summoner, slot: "second").Start();
            var ai = f.Rng.MonsterAi.Clone();
            var summonMove = CoinPick(ai);
            var callerMove = CoinPick(ai);
            f.EndTurn();

            c.Equal(2, f.Enemies.Count, "the summon joined");
            var summon = (Puppet)f.Enemies[0].Monster!;
            c.Equal("Summon", summon.Label, "its slot puts it first in the list");
            c.That(ReferenceEquals(f.Enemy(1), summon.Creature), "but it is e1: an index is given on arrival and never moves");
            c.Equal(30, summon.Creature.Hp, "its HP was rolled");
            c.That(!summon.HasPerformedAMove, "it did not act in the turn it arrived");
            c.Equal(summonMove, summon.NextMove!.Id, "turn 2: the first MonsterAi draw is the summon's first intent (it is first in the list)");
            c.Equal(callerMove, summoner.NextMove!.Id, "the second is the summoner's next move");
            c.Equal(2, f.Rng.MonsterAi.Counter, "two draws, none when it was added");
            c.Sequence([1, 0], f.Snapshot().State["enemies"]!.AsArray().Select(e => e!["i"]!.GetValue<int>()).OrderByDescending(i => i), "the snapshot lists both by e-index");
            f.EndTurn();
            c.That(summon.HasPerformedAMove, "it acts from the next enemy turn on");
        });

        c.Group("turns R39, R8: a monster added during the player's turn has its intent at once and acts in the coming enemy turn", () =>
        {
            var f = CheckKit.VsDummy();
            var late = new Puppet { Label = "Late", Low = 20, High = 20, Build = (self, m) => CoinFlip(m, startRandom: true) };
            var ai = f.Rng.MonsterAi.Clone();
            var first = CoinPick(ai);
            f.AddMonster(late);
            c.Equal(first, late.NextMove!.Id, "its first intent is settled when it joins: one MonsterAi draw");
            c.Equal(1, f.Rng.MonsterAi.Counter, "one draw");
            c.Sequence(first == "X" ? ["buff"] : ["defend"], f.IntentTexts(late.Creature), "and it shows");
            f.EndTurn();
            c.That(late.HasPerformedAMove, "it acted in the enemy turn that followed");
        });

        c.Group("turns R6: an HP roll avoids the values taken; with none left it is a plain roll; one draw either way", () =>
        {
            var builder = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Seed(5);
            var puppets = new[] { new Puppet { Low = 10, High = 11 }, new Puppet { Low = 10, High = 11 }, new Puppet { Low = 10, High = 11 } };
            foreach (var p in puppets) builder.Enemy(p);
            var f = builder.Start();

            var niche = new RngSet(5).Niche;
            var first = 10 + niche.NextInt(0, 2);
            niche.NextInt(0, 1);
            var second = first == 10 ? 11 : 10;
            var third = niche.NextInt(10, 12);
            c.Sequence([first, second, third], puppets.Select(p => p.Creature.MaxHp), "the three rolls");
            c.Equal(3, f.Rng.Niche.Counter, "three draws");
        });
    }

    // ---------- turns R51, R55, R56; damage R15, R21 ----------

    private static void Powers(Check c)
    {
        c.Group("turns R51, R55: applying, stacking, removal and caps", () =>
        {
            var f = CheckKit.VsDummy();
            var dummy = Mob(f, 0);
            c.That(f.ApplyPower<Hex>(dummy, 0, f.Player) is null && dummy.Powers.Count == 0, "applying 0 does nothing");
            f.ApplyPower<Hex>(dummy, 2.9m, f.Player);
            c.Equal(2, dummy.PowerAmount<Hex>(), "an amount is truncated when it lands");
            f.ApplyPower<Hex>(dummy, 3, f.Player);
            c.Equal(5, dummy.PowerAmount<Hex>(), "applying again adds to the instance");
            c.Equal(1, dummy.Powers.Count, "one instance");
            f.ApplyPower<Hex>(dummy, -7, f.Player);
            c.That(!dummy.HasPower<Hex>(), "at 0 or below a plain power is removed");

            f.ApplyPower<StrengthPower>(dummy, -2, f.Player);
            c.Equal(-2, dummy.PowerAmount<StrengthPower>(), "Strength is kept below 0");
            f.ApplyPower<StrengthPower>(dummy, 2, f.Player);
            c.That(!dummy.HasPower<StrengthPower>(), "and removed at exactly 0");

            f.ApplyPower<StrengthPower>(dummy, 2_000_000_000m, f.Player);
            c.Equal(999_999_999, dummy.PowerAmount<StrengthPower>(), "amounts are capped");

            f.ApplyPower<Probe>(dummy, 1, null);
            f.ApplyPower<Probe>(dummy, 1, null);
            c.Equal(2, dummy.Powers.Count(p => p is Probe), "an instanced power: every application is its own instance");

            var two = CheckKit.VsDummies(new CheckDummy { DisplayName = "A" }, new CheckDummy { DisplayName = "B" });
            var gone = Mob(two, 0);
            two.Kill([gone]);
            c.That(two.ApplyPower<Hex>(gone, 3, two.Player) is null && gone.Powers.Count == 0, "a monster that left the fight cannot receive powers");
        });

        c.Group("turns R56, damage R21: at death the powers all leave first, then each is told", () =>
        {
            var log = new List<string>();
            var f = CheckKit.VsDummies(new CheckDummy { DisplayName = "A", Hp = 6 }, new CheckDummy { DisplayName = "B" });
            foreach (var label in new[] { "one", "two" })
            {
                var p = f.ApplyPower<Probe>(Mob(f, 0), 1, null)!;
                p.Label = label;
                p.Log = log;
            }
            f.Play("Strike", 0);
            c.Sequence(["one", "two", "one removed, 0 left", "two removed, 0 left"], log, "both were off the monster before either callback ran");
        });

        c.Group("turns R56: a power that keeps powers on the dead is only asked while the dead creature is still in the fight", () =>
        {
            // On the player's turn a dead monster leaves the fight first: its powers go, nobody is asked.
            var log = new List<string>();
            var f = CheckKit.VsDummies(new CheckDummy { DisplayName = "A", Hp = 6 }, new CheckDummy { DisplayName = "B" });
            var buff = f.ApplyPower<Probe>(Mob(f, 0), 1, null)!;
            buff.Label = "buff";
            buff.Log = log;
            f.ApplyPower<Probe>(Mob(f, 1), 1, null)!.KeepsBuffs = true;
            f.Play("Strike", 0);
            c.Sequence(["buff", "buff removed, 0 left"], log, "killed by a card: the keeper on the other monster has no say");

            // A monster that dies during its own move is still in the fight when its powers are sorted out.
            var log2 = new List<string>();
            var g = CheckKit.VsDummies(new CheckDummy { DisplayName = "A", Hp = 10, Steps = [new DummyStep("HIT", Damage: 2)] }, new CheckDummy { DisplayName = "B" });
            var attacker = Mob(g, 0);
            var buff2 = g.ApplyPower<Probe>(attacker, 1, null)!;
            buff2.Label = "buff";
            buff2.Log = log2;
            g.ApplyPower<Probe>(Mob(g, 1), 1, null)!.KeepsBuffs = true;
            var spikes = g.ApplyPower<Probe>(g.Player, 1, null)!;
            spikes.OnHit = (x, me, target, dealer, props) =>
            {
                if (ReferenceEquals(target, me.Owner) && dealer is { IsEnemy: true } && props.IsPoweredAttack())
                    x.Damage(dealer, 50m, Props.NonCardUnpowered, me.Owner);
            };
            g.EndTurn();
            c.Equal(78, g.Player.Hp, "the hit landed");
            c.That(!g.Enemies.Contains(attacker), "the attacker is dead and gone after its move");
            c.Equal(0, log2.Count, "its buff was kept: no removal callback");
            c.Equal(1, attacker.Powers.Count, "it is still on the corpse");
        });

        c.Group("the game's remove command tells a power it was removed even when it was already off its owner", () =>
        {
            var log = new List<string>();
            var f = CheckKit.VsDummy();
            var p = f.ApplyPower<Probe>(f.Player, 1, null)!;
            p.Label = "p";
            p.Log = log;
            f.RemovePower(p);
            f.RemovePower(p);
            c.Sequence(["p removed, 0 left", "p removed, 0 left"], log, "two removals, two callbacks");
            f.RemovePower<StrengthPower>(f.Player);
            c.Equal(2, log.Count, "removing a power that was never there does nothing");
        });
    }

    // ---------- damage R22 to R25: what the model sees ----------

    private static void Snapshots(Check c)
    {
        c.Group("damage R22, R23: intents are the hit, per hit, truncated per hit, before Block", () =>
        {
            var f = CheckKit.VsDummies(
                new CheckDummy { DisplayName = "A", Steps = [new DummyStep("HIT", Damage: 12)] },
                new CheckDummy { DisplayName = "B", Steps = [new DummyStep("HITS", Damage: 5, Hits: 3)] });
            c.Sequence(["attack 12"], f.IntentTexts(Mob(f, 0)), "a single attack");
            c.Sequence(["attack 5x3"], f.IntentTexts(Mob(f, 1)), "a multi attack");
            c.Equal(27, f.IncomingDamage(), "12 + 5 x 3");

            f.ApplyPower<WeakPower>(Mob(f, 0), 1, f.Player);
            f.ApplyPower<WeakPower>(Mob(f, 1), 1, f.Player);
            c.Sequence(["attack 9"], f.IntentTexts(Mob(f, 0)), "12 x 0.75");
            c.Sequence(["attack 3x3"], f.IntentTexts(Mob(f, 1)), "5 x 0.75 = 3.75 per hit");
            c.Equal(18, f.IncomingDamage(), "9 + 3 x 3, not 9 + 11");

            f.ApplyPower<VulnerablePower>(f.Player, 1, f.Player);
            c.Sequence(["attack 13"], f.IntentTexts(Mob(f, 0)), "12 x 0.75 x 1.5 = 13.5");
            c.Sequence(["attack 5x3"], f.IntentTexts(Mob(f, 1)), "5 x 0.75 x 1.5 = 5.625");
            f.SetBlock(f.Player, 20);
            c.Equal(28, f.IncomingDamage(), "13 + 15, and Block is not taken off");

            // 13.5 into 20 Block leaves 7; 5.625 leaves 2; the next 5.625 takes 2 Block and 3 HP; the last takes 5 HP.
            f.EndTurn();
            c.Equal(72, f.Player.Hp, "what landed");
        });

        c.Group("the mod's hand cards: 'vs' is the text against each enemy the card can be aimed at, where it differs", () =>
        {
            var f = CheckKit.VsDummy();
            JsonObject Hand(int i) => f.Snapshot().State["hand"]![i]!.AsObject();
            c.That(f.Snapshot().State["hand"]!.AsArray().All(card => !card!.AsObject().ContainsKey("vs")), "nothing differs: no card has 'vs'");

            f.ApplyPower<VulnerablePower>(Mob(f, 0), 2, f.Player);
            c.Equal("""{"i":0,"name":"Strike","cost":"1","type":"Attack","text":"Deal 6 damage.","vs":{"e0":"Deal 9 damage."},"playable":true}""",
                Hand(0).ToJsonString(), "a Strike against a Vulnerable enemy: the text at rest stays, 'vs' follows the text");
            c.That(!Hand(2).ContainsKey("vs"), "Defend aims at nobody: no 'vs'");
            c.Equal("""{"e0":"Deal 12 damage. Apply 2 Vulnerable."}""", Hand(4)["vs"]!.ToJsonString(), "Bash");

            f.ApplyPower<WeakPower>(f.Player, 1, null);
            c.Equal("Deal 4 damage.", Hand(0)["text"]!.GetValue<string>(), "at rest with Weak: 6 x 0.75 = 4.5");
            c.Equal("Deal 6 damage.", Hand(0)["vs"]!["e0"]!.GetValue<string>(), "against the enemy: 6 x 0.75 x 1.5 = 6.75, truncated once");

            f.SetBlock(Mob(f, 0), 50);
            f.ApplyPower<SlipperyPower>(Mob(f, 0), 1, null);
            c.Equal("Deal 6 damage.", Hand(0)["vs"]!["e0"]!.GetValue<string>(), "the enemy's Block and Slippery are not in the preview");
        });

        c.Group("'vs': only the enemies that differ, by their e-index; all-enemy cards too; random-enemy cards never", () =>
        {
            var f = CheckKit.VsDummies(new CheckDummy { DisplayName = "A" }, new CheckDummy { DisplayName = "B" }, new CheckDummy { DisplayName = "C" });
            f.Kill([Mob(f, 0)]);
            f.ApplyPower<VulnerablePower>(Mob(f, 1), 1, f.Player);
            f.ApplyPower<VulnerablePower>(Mob(f, 2), 1, f.Player);
            f.ApplyPower<Probe>(Mob(f, 2), 1, null)!.BlocksTargeting = true;
            f.Put(Sweep, PileType.Hand);
            f.Put(Scatter, PileType.Hand);
            f.Put(Grudge, PileType.Hand);
            var hand = f.Snapshot().State["hand"]!.AsArray();

            c.Equal("""{"e1":"Deal 9 damage."}""", hand[0]!["vs"]!.ToJsonString(), "the dead enemy and the one that cannot be targeted have no entry");
            c.Equal("Deal 4 damage to ALL enemies twice.", hand[5]!["text"]!.GetValue<string>(), "an all-enemy card at rest");
            c.Equal("""{"e1":"Deal 6 damage to ALL enemies twice."}""", hand[5]!["vs"]!.ToJsonString(), "an all-enemy card against one enemy");
            c.That(!hand[6]!.AsObject().ContainsKey("vs"), "a random-enemy card has no 'vs'");
            c.Equal("Deal 4 damage.", hand[7]!["text"]!.GetValue<string>(), "a calculated number at rest: the multiplier sees no target");
            c.Equal("""{"e1":"Deal 9 damage."}""", hand[7]!["vs"]!.ToJsonString(), "against a target with 1 Vulnerable: (4 + 2 x 1) x 1.5");
            c.That(f.LegalActions().All(a => !a.Id.EndsWith(">e2", StringComparison.Ordinal) && !a.Id.EndsWith(">e0", StringComparison.Ordinal)), "and no action aims at them");
        });

        c.Group("'vs' is a combat hand thing: the hand selection lists cards without it", () =>
        {
            var f = new FightBuilder().DrawOrder("Armaments", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "Bash", "StrikeIronclad", "StrikeIronclad").Enemy(new CheckDummy()).Start();
            f.ApplyPower<VulnerablePower>(Mob(f, 0), 2, f.Player);
            c.That(f.Snapshot().State["hand"]![1]!.AsObject().ContainsKey("vs"), "in combat the Strike has it");
            f.Play("Armaments");
            var s = f.Snapshot();
            c.Equal("hand_select", s.Screen, "Armaments asks");
            c.That(s.State["hand"]!.AsArray().All(card => !card!.AsObject().ContainsKey("vs")), "no 'vs' while choosing");
        });

        // Creature.Name of a player in single player is the character's title (Creature.cs 129-141,
        // CharacterModel.Title = the loc entry IRONCLAD.title); its English text in the game's own
        // table is "The Ironclad". The mod prints it as state.me.name and as get_deck's character.
        c.Group("the player is named by the character's title, 'The Ironclad', on every screen and in get_deck", () =>
        {
            var f = new FightBuilder().DrawOrder("Armaments", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "Bash", "StrikeIronclad", "StrikeIronclad").Enemy(new CheckDummy { Hp = 6 }).Start();
            c.Equal("The Ironclad", (string?)f.Snapshot().State["me"]!["name"], "combat: state.me.name");
            c.Equal("The Ironclad", (string?)f.DescribeDeck()["character"], "get_deck: character");
            f.Play("Armaments");
            c.Equal("hand_select", f.Snapshot().Screen, "Armaments asks");
            c.Equal("The Ironclad", (string?)f.Snapshot().State["me"]!["name"], "hand_select: state.me.name");
            f.Do("select:0", "confirm");
            f.Play("Strike", 0);
            c.That(f.IsOver && f.Won, "the dummy is dead");
            c.Equal("The Ironclad", (string?)f.Snapshot().State["me"]!["name"], "the end snapshot: state.me.name");
        });
    }
}
