namespace SpireMind.Arena;

// Overgrowth monsters, part A (encounters-1.json): Cubex Construct, Fogmog with its Eye with
// Teeth, Inklet and Mawler, the one power only they use (Illusion), and their four encounters.
// Every number is the ascension-0 value of the game class of the same name (v0.107.1); move ids
// and the order of intents, steps and branch options are the game's.

/// <summary>Registers the monsters, the power and the encounters of this file.</summary>
public sealed class MonstersA : IContentModule
{
    public void Register(Registry r)
    {
        r.Power<IllusionPower>();

        r.Monster<CubexConstruct>();
        r.Monster<Fogmog>();
        r.Monster<EyeWithTeeth>();
        r.Monster<Inklet>();
        r.Monster<Mawler>();

        r.Encounter<CubexConstructNormal>();
        r.Encounter<FogmogNormal>();
        r.Encounter<InkletsNormal>();
        r.Encounter<MawlerNormal>();
    }
}

// ---------- Illusion ----------

/// <summary>
/// Illusion (the Eye with Teeth): its owner is a minion that cannot be killed for good. At 0 HP
/// it stays in the enemy list, cannot be hit, targeted or given powers, and its next move is
/// replaced by a revive that heals it to full on its next turn; afterwards it goes back to the
/// move it had chosen last.
/// </summary>
public sealed class IllusionPower : Power, IForcedMovePerformer
{
    public const string ReviveMoveId = "REVIVE_MOVE";

    private bool _isReviving;

    public override string Name => "Illusion";
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>True from the moment its owner reached 0 HP until the revive move was performed.</summary>
    public bool IsReviving => _isReviving;

    public override string Describe() =>
        "Cannot be killed for good: at 0 HP it cannot be hit until its next turn, when it returns at full HP.";

    /// <summary>The owner becomes a minion the moment Illusion lands, unless it already is one. Nobody is the applier.</summary>
    public override void AfterApplied(Fight f, Creature? applier, CardInstance? cardSource)
    {
        if (!Owner.HasPower<MinionPower>()) f.ApplyPower<MinionPower>(Owner, 1, null);
    }

    /// <summary>
    /// What a dying creature keeps while an Illusion is in the fight: every Buff, and the
    /// temporary Strength-down kinds (they still have to hand their Strength back). Other
    /// Debuffs go. The game has no owner test here, so this is asked of every power of whoever
    /// dies while still in the fight: the owner itself, and a monster that dies during its own
    /// move. A monster killed at any other time has left the fight before its powers are sorted
    /// out, and the engine then asks nobody, as the game does.
    /// </summary>
    public override bool ShouldPowerBeRemovedOnDeath(Fight f, Power power) =>
        // The game's test is its "temporary power" interface; every Debuff that has it is a temporary Strength kind.
        power.Type == PowerType.Debuff && power is not TemporaryStrengthPower;

    public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(Fight f, Creature creature) =>
        !ReferenceEquals(creature, Owner);

    public override void AfterDeath(Fight f, Creature creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || !ReferenceEquals(creature, Owner)) return;
        _isReviving = true;
        var monster = Owner.Monster!;
        // A forced move is never logged, so the last log entry is the move the owner chose last.
        monster.SetMoveImmediate(new ForcedMove(ReviveMoveId, [Intent.Heal], monster.MoveLog[^1], MustPerformOnce: true, Performer: this));
    }

    /// <summary>While reviving the owner cannot be hit, and so cannot receive powers either.</summary>
    public override bool ShouldAllowHitting(Fight f, Creature creature) =>
        !ReferenceEquals(creature, Owner) || !_isReviving;

    /// <summary>The revive move: the owner can be hit again, then heals what it is missing.</summary>
    public void PerformForcedMove(Fight f, Creature monster, string moveId)
    {
        _isReviving = false;
        f.Heal(Owner, Owner.MaxHp - Owner.Hp);
    }
}

// ---------- Cubex Construct ----------

/// <summary>
/// Cubex Construct: 65 HP, Artifact 1. Charges up once (2 Strength), then repeats Repeater Blast,
/// Repeater Blast, Expel for ever; each Blast adds 2 Strength after its hit. No random number.
/// </summary>
public sealed class CubexConstruct : Monster
{
    private const int StartBlock = 13;
    private const int StartArtifact = 1;
    private const int ChargeStrength = 2;
    private const int BlastDamage = 7;
    private const int BlastStrength = 2;
    private const int ExpelDamage = 5;
    private const int ExpelHits = 2;

    public override string Name => "Cubex Construct";
    public override int MinHp => 65;
    public override int MaxHp => 65;

    public override void AfterAddedToRoom(Fight f)
    {
        // The game asks for the Block first. At the start of a fight the request does nothing,
        // because the fight is not in progress yet; only a Cubex Construct that joins a running
        // fight gets it.
        GainBlock(f, StartBlock);
        ApplyToSelf<ArtifactPower>(f, StartArtifact);
    }

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var chargeUp = m.Move("CHARGE_UP_MOVE", ChargeUp, Intent.Buff);
        // The same move under two ids, so that the cycle can tell the first Blast from the second.
        var blast = m.Move("REPEATER_BLAST_MOVE", RepeaterBlast, Intent.Attack(BlastDamage), Intent.Buff);
        var blast2 = m.Move("REPEATER_BLAST_MOVE_2", RepeaterBlast, Intent.Attack(BlastDamage), Intent.Buff);
        var expel = m.Move("EXPEL_MOVE", Expel, Intent.Attack(ExpelDamage, ExpelHits));
        chargeUp.Then(blast).Then(blast2).Then(expel).Then(blast);
        return chargeUp;
    }

    private void ChargeUp(Fight f) => ApplyToSelf<StrengthPower>(f, ChargeStrength);

    private void RepeaterBlast(Fight f)
    {
        Attack(f, BlastDamage);
        // After the hit: this Blast does not profit from its own Strength.
        ApplyToSelf<StrengthPower>(f, BlastStrength);
    }

    private void Expel(Fight f) => Attack(f, ExpelDamage, ExpelHits);
}

/// <summary>One Cubex Construct, no slot.</summary>
public sealed class CubexConstructNormal : Encounter
{
    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new CubexConstruct(), null)];
}

// ---------- Fogmog and the Eye with Teeth ----------

/// <summary>
/// Fogmog: 74 HP. Turn 1 summons one Eye with Teeth and never again. Then Swipe (8, +1 Strength),
/// after which it either swipes once more and then headbutts (40%) or headbutts at once (60%),
/// and back to Swipe.
/// </summary>
public sealed class Fogmog : Monster
{
    private const int SwipeDamage = 8;
    private const int SwipeStrength = 1;
    private const int HeadbuttDamage = 14;

    public override string Name => "Fogmog";
    public override int MinHp => 74;
    public override int MaxHp => 74;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var illusion = m.Move("ILLUSION_MOVE", SummonIllusion, Intent.Summon);
        var swipe = m.Move("SWIPE_MOVE", Swipe, Intent.Attack(SwipeDamage), Intent.Buff);
        // The same Swipe under a second id: it differs only in what follows it.
        var swipeRandom = m.Move("SWIPE_RANDOM_MOVE", Swipe, Intent.Attack(SwipeDamage), Intent.Buff);
        var headbutt = m.Move("HEADBUTT_MOVE", Headbutt, Intent.Attack(HeadbuttDamage));

        // "Cannot repeat" never bites: the branch is only reached from SWIPE_MOVE.
        var branch = m.Random("BRANCH")
            .Add(swipeRandom, 0.4f, MoveRepeat.CannotRepeat)
            .Add(headbutt, 0.6f, MoveRepeat.CannotRepeat);

        illusion.FollowUp = swipe;
        swipe.FollowUp = branch;
        swipeRandom.FollowUp = headbutt;
        headbutt.FollowUp = swipe;
        return illusion;
    }

    /// <summary>The Eye joins during the enemy turn: it does not act this turn, and the slot order puts it before Fogmog.</summary>
    private void SummonIllusion(Fight f) => f.AddMonster(new EyeWithTeeth(), FogmogNormal.IllusionSlot);

    private void Swipe(Fight f)
    {
        Attack(f, SwipeDamage);
        ApplyToSelf<StrengthPower>(f, SwipeStrength);
    }

    private void Headbutt(Fight f) => Attack(f, HeadbuttDamage);
}

/// <summary>
/// Eye with Teeth: 6 HP, Illusion (which also makes it a minion). Every turn it puts 3 Dazed in
/// the player's discard pile. Killing it only costs it that one turn.
/// </summary>
public sealed class EyeWithTeeth : Monster
{
    private const int DistractCards = 3;

    public override string Name => "Eye with Teeth";
    public override int MinHp => 6;
    public override int MaxHp => 6;

    public override void AfterAddedToRoom(Fight f) => ApplyToSelf<IllusionPower>(f, 1);

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var distract = m.Move("DISTRACT_MOVE", Distract, Intent.Status(DistractCards));
        distract.FollowUp = distract;
        return distract;
    }

    /// <summary>The cards are created one by one at the bottom of the discard pile. The game names no creator for them.</summary>
    private void Distract(Fight f) => f.AddStatusCards("Dazed", DistractCards, PileType.Discard, PilePosition.Bottom, creator: null);
}

/// <summary>
/// Fogmog alone in the slot "fogmog". The slot "illusion" comes first in the slot order and is
/// filled by Fogmog's first move, so the Eye acts before Fogmog from the turn after.
/// </summary>
public sealed class FogmogNormal : Encounter
{
    public const string IllusionSlot = "illusion";
    public const string FogmogSlot = "fogmog";

    public override IReadOnlyList<string> Slots => [IllusionSlot, FogmogSlot];

    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new Fogmog(), FogmogSlot)];
}

// ---------- Inklet ----------

/// <summary>
/// Inklet: 11 to 17 HP, Slippery 1. Jab (3), then an even pick between Piercing Gaze (10) and
/// Whirlwind (2 three times), then Jab again. The middle one of the three opens with Whirlwind.
/// </summary>
public sealed class Inklet : Monster
{
    private const int StartSlippery = 1;
    private const int JabDamage = 3;
    private const int WhirlwindDamage = 2;
    private const int WhirlwindHits = 3;
    private const int PiercingGazeDamage = 10;

    /// <summary>The second Inklet of InkletsNormal: it enters the cycle at Whirlwind.</summary>
    public bool MiddleInklet { get; set; }

    public override string Name => "Inklet";
    public override int MinHp => 11;
    public override int MaxHp => 17;

    public override void AfterAddedToRoom(Fight f) => ApplyToSelf<SlipperyPower>(f, StartSlippery);

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var jab = m.Move("JAB_MOVE", Jab, Intent.Attack(JabDamage));
        var whirlwind = m.Move("WHIRLWIND_MOVE", Whirlwind, Intent.Attack(WhirlwindDamage, WhirlwindHits));
        var piercingGaze = m.Move("PIERCING_GAZE_MOVE", PiercingGaze, Intent.Attack(PiercingGazeDamage));

        // Piercing Gaze is listed first. "Cannot repeat" never bites: the branch is only reached from Jab.
        // (The game also builds a branch called INIT_RAND and never uses it.)
        var rand = m.Random("RAND")
            .Add(piercingGaze, 1f, MoveRepeat.CannotRepeat)
            .Add(whirlwind, 1f, MoveRepeat.CannotRepeat);

        jab.FollowUp = rand;
        whirlwind.FollowUp = jab;
        piercingGaze.FollowUp = jab;
        return MiddleInklet ? whirlwind : jab;
    }

    private void Jab(Fight f) => Attack(f, JabDamage);

    private void Whirlwind(Fight f) => Attack(f, WhirlwindDamage, WhirlwindHits);

    private void PiercingGaze(Fight f) => Attack(f, PiercingGazeDamage);
}

/// <summary>Three Inklets without slots; the second is the middle one. They act in this order.</summary>
public sealed class InkletsNormal : Encounter
{
    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new Inklet(), null), (new Inklet { MiddleInklet = true }, null), (new Inklet(), null)];
}

// ---------- Mawler ----------

/// <summary>
/// Mawler: 72 HP. Opens with Claw (4 twice). Every later move is an even pick among Rip and
/// Tear (14), Roar (3 Vulnerable on the player) and Claw, never the attack it just made, and
/// Roar only once in the fight.
/// </summary>
public sealed class Mawler : Monster
{
    private const int RipAndTearDamage = 14;
    private const int ClawDamage = 4;
    private const int ClawHits = 2;
    private const int RoarVulnerable = 3;

    public override string Name => "Mawler";
    public override int MinHp => 72;
    public override int MaxHp => 72;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var ripAndTear = m.Move("RIP_AND_TEAR_MOVE", RipAndTear, Intent.Attack(RipAndTearDamage));
        var roar = m.Move("ROAR_MOVE", Roar, Intent.Debuff);
        var claw = m.Move("CLAW_MOVE", Claw, Intent.Attack(ClawDamage, ClawHits));

        var rand = m.Random("RAND")
            .Add(ripAndTear, 1f, MoveRepeat.CannotRepeat)
            .Add(roar, 1f, MoveRepeat.UseOnlyOnce)
            .Add(claw, 1f, MoveRepeat.CannotRepeat);

        ripAndTear.FollowUp = rand;
        roar.FollowUp = rand;
        claw.FollowUp = rand;
        return claw;
    }

    private void RipAndTear(Fight f) => Attack(f, RipAndTearDamage);

    private void Roar(Fight f) => ApplyToPlayer<VulnerablePower>(f, RoarVulnerable);

    private void Claw(Fight f) => Attack(f, ClawDamage, ClawHits);
}

/// <summary>One Mawler, no slot.</summary>
public sealed class MawlerNormal : Encounter
{
    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new Mawler(), null)];
}
