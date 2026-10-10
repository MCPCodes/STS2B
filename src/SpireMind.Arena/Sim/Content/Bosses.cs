using System.Globalization;

namespace SpireMind.Arena;

// The three bosses of Overgrowth (encounters-3.json), each following its game class of the
// same name (v0.107.1): the Ceremonial Beast with the two powers only it uses, the Kin (a
// priest led by two minion followers) and Vantom. All numbers are the ascension 0 ones.

/// <summary>Registers the act's bosses, their encounters, the Beast's Plow and Ringing powers and the Ringing affliction.</summary>
public sealed class Bosses : IContentModule
{
    public void Register(Registry r)
    {
        r.Affliction(new AfflictionDef(RingingPower.AfflictionName, "Ringing", _ => RingingPower.CardLine));
        r.Power<PlowPower>();
        r.Power<RingingPower>();

        r.Monster<CeremonialBeast>();
        r.Monster<KinFollower>();
        r.Monster<KinPriest>();
        r.Monster<Vantom>();

        r.Encounter<CeremonialBeastBoss>();
        r.Encounter<TheKinBoss>();
        r.Encounter<VantomBoss>();
    }
}

// ---------- Ceremonial Beast ----------

/// <summary>
/// The Ceremonial Beast's phase switch. The amount is an HP threshold: after any damage
/// instance in which its owner lost HP and lived, with HP at or below the threshold, the owner
/// loses every temporary Strength power and all of its Strength, is stunned, and this power
/// goes. Taking temporary Strength away here skips its end-of-turn give-back, so Strength is
/// exactly 0 afterwards. The killing blow never asks (no "damage received" for it).
/// </summary>
public sealed class PlowPower : Power
{
    public override string Name => "Plow";
    public override PowerType Type => PowerType.Debuff;

    public override string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"When losing HP leaves it at {Amount} HP or less, it loses all Strength and is stunned for a turn.");

    public override void AfterDamageReceived(Fight f, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner) || result.UnblockedDamage <= 0 || target.Hp > Amount) return;

        foreach (var temporary in Owner.Powers.OfType<TemporaryStrengthPower>().ToList()) f.RemovePower(temporary);
        f.RemovePower<StrengthPower>(Owner);
        // The Beast's stun leads into its second cycle; on any other owner it is the plain stun.
        // The stun's own body only resets an animation in the game, so the engine's empty one is exact.
        f.Stun(Owner, Owner.Monster is CeremonialBeast ? CeremonialBeast.BeastCryId : null);
        f.RemovePower(this);
    }
}

/// <summary>
/// What the Beast's cry leaves on the player. Every card of theirs without an affliction gets
/// the Ringing affliction, also cards that enter the fight later, and a card carrying it cannot
/// be played once any card play of the turn has started (auto-plays count, and are stopped the
/// same way). So the turn it covers is a one-card turn; potions are free. It leaves at the end
/// of its owner's own turn, not of the enemy turn it arrived in, and takes the affliction off
/// every card.
/// </summary>
public sealed class RingingPower : Power
{
    /// <summary>The game's class name of the affliction this power hands out.</summary>
    public const string AfflictionName = "Ringing";

    /// <summary>The line a card carrying the affliction shows after its own text.</summary>
    public const string CardLine = "Ringing: cannot be played after another card this turn.";

    public override string Name => "Ringing";
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override string Describe() =>
        "Your cards are marked Ringing: once you have played a card this turn, marked cards cannot be played. Wears off at the end of your turn.";

    public override void AfterApplied(Fight f, Creature? applier, CardInstance? cardSource)
    {
        if (!Owner.IsPlayer) return;
        foreach (var card in f.AllCards.ToList())
            if (card.Affliction is null) f.Afflict(card, AfflictionName);
    }

    public override void AfterCardEnteredCombat(Fight f, CardInstance card)
    {
        // Every card is the player's. One that already carries another affliction keeps it and stays free.
        if (Owner.IsPlayer && card.Affliction is null) f.Afflict(card, AfflictionName);
    }

    public override bool ShouldPlay(Fight f, CardInstance card, AutoPlayType autoPlay)
    {
        if (!Owner.IsPlayer || card.Affliction != AfflictionName) return true;
        return f.History.CardPlaysStartedThisTurn(f) == 0;
    }

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(Owner)) f.RemovePower(this);
    }

    public override void AfterRemoved(Fight f, Creature oldOwner)
    {
        if (!oldOwner.IsPlayer) return;
        foreach (var card in f.AllCards.ToList())
            if (card.Affliction == AfflictionName) f.ClearAffliction(card);
    }
}

/// <summary>
/// Ceremonial Beast, 252 HP. Phase one: Stamp puts Plow 150 on itself, then it Plows every
/// turn, 18 damage and 2 more Strength each time. When Plow triggers it loses its Strength, is
/// stunned for a turn and from then on cycles Beast Cry (Ringing on the player), Stomp 15 and
/// Crush 17 with 3 more Strength; it never Plows again. No random or conditional branch.
/// </summary>
public sealed class CeremonialBeast : Monster
{
    /// <summary>The move that follows the stun; the Plow power names it.</summary>
    public const string BeastCryId = "BEAST_CRY_MOVE";

    private const int PlowThreshold = 150;
    private const int PlowDamage = 18;
    private const int PlowStrength = 2;
    private const int StompDamage = 15;
    private const int CrushDamage = 17;
    private const int CrushStrength = 3;

    public override string Name => "Ceremonial Beast";
    public override int MinHp => 252;
    public override int MaxHp => 252;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var stamp = m.Move("STAMP_MOVE", Stamp, Intent.Buff);
        var plow = m.Move("PLOW_MOVE", Plow, Intent.Attack(PlowDamage), Intent.Buff);
        // Part of the game's machine but never entered: the Plow power stuns through the engine's own stun state.
        var stun = m.Move("STUN_MOVE", Stunned, Intent.Stun);
        stun.MustPerformOnce = true;
        var beastCry = m.Move(BeastCryId, BeastCry, Intent.Debuff);
        var stomp = m.Move("STOMP_MOVE", Stomp, Intent.Attack(StompDamage));
        var crush = m.Move("CRUSH_MOVE", Crush, Intent.Attack(CrushDamage), Intent.Buff);

        stamp.Then(plow).Then(plow);
        stun.Then(beastCry).Then(stomp).Then(crush).Then(beastCry);
        return stamp;
    }

    private void Stamp(Fight f) => ApplyToSelf<PlowPower>(f, PlowThreshold);

    private void Plow(Fight f)
    {
        Attack(f, PlowDamage);
        // Also when damage answered to this very hit tripped Plow and wiped its Strength: it then
        // enters phase two with these 2.
        ApplyToSelf<StrengthPower>(f, PlowStrength);
    }

    /// <summary>The game's body only ends the stun animation.</summary>
    private void Stunned(Fight f) { }

    private void BeastCry(Fight f) => ApplyToPlayer<RingingPower>(f, 1);

    private void Stomp(Fight f) => Attack(f, StompDamage);

    private void Crush(Fight f)
    {
        Attack(f, CrushDamage);
        ApplyToSelf<StrengthPower>(f, CrushStrength);
    }
}

/// <summary>The Ceremonial Beast alone, no slot, nothing drawn from the encounter's stream.</summary>
public sealed class CeremonialBeastBoss : Encounter
{
    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new CeremonialBeast(), null)];
}

// ---------- The Kin ----------

/// <summary>
/// Kin Follower, 58 to 59 HP, a Minion: it does not keep the fight alive and dies with the
/// Kin Priest. A fixed cycle of Quick Slash 5, Boomerang 2x2 and Power Dance (2 Strength for
/// itself); the encounter decides where it enters the cycle.
/// </summary>
public sealed class KinFollower : Monster
{
    private const int QuickSlashDamage = 5;
    private const int BoomerangDamage = 2;
    private const int BoomerangHits = 2;
    private const int DanceStrength = 2;

    /// <summary>The slot1 follower of the boss fight opens with Power Dance; the other opens with Quick Slash.</summary>
    public bool StartsWithDance { get; set; }

    public override string Name => "Kin Follower";
    public override int MinHp => 58;
    public override int MaxHp => 59;

    public override void AfterAddedToRoom(Fight f) => ApplyToSelf<MinionPower>(f, 1);

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var quickSlash = m.Move("QUICK_SLASH_MOVE", QuickSlash, Intent.Attack(QuickSlashDamage));
        var boomerang = m.Move("BOOMERANG_MOVE", Boomerang, Intent.Attack(BoomerangDamage, BoomerangHits));
        var powerDance = m.Move("POWER_DANCE_MOVE", PowerDance, Intent.Buff);
        quickSlash.Then(boomerang).Then(powerDance).Then(quickSlash);
        return StartsWithDance ? powerDance : quickSlash;
    }

    private void QuickSlash(Fight f) => Attack(f, QuickSlashDamage);

    private void Boomerang(Fight f) => Attack(f, BoomerangDamage, BoomerangHits);

    private void PowerDance(Fight f) => ApplyToSelf<StrengthPower>(f, DanceStrength);
}

/// <summary>
/// Kin Priest, 190 HP, the fight's only primary enemy: when it dies the followers die with it
/// and the fight is won. A fixed cycle of Orb of Frailty (8, then 1 Frail), Orb of Weakness
/// (8, then 1 Weak), Beam 3x3 and Ritual (2 Strength for itself). The debuff of an orb is
/// applied whether or not the hit was blocked. Its reactions to deaths are speech and music only.
/// </summary>
public sealed class KinPriest : Monster
{
    private const int OrbOfFrailtyDamage = 8;
    private const int OrbOfWeaknessDamage = 8;
    private const int BeamDamage = 3;
    private const int BeamHits = 3;
    private const int RitualStrength = 2;

    public override string Name => "Kin Priest";
    public override int MinHp => 190;
    public override int MaxHp => 190;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var orbOfFrailty = m.Move("ORB_OF_FRAILTY_MOVE", OrbOfFrailty, Intent.Attack(OrbOfFrailtyDamage), Intent.Debuff);
        var orbOfWeakness = m.Move("ORB_OF_WEAKNESS_MOVE", OrbOfWeakness, Intent.Attack(OrbOfWeaknessDamage), Intent.Debuff);
        var beam = m.Move("BEAM_MOVE", Beam, Intent.Attack(BeamDamage, BeamHits));
        var ritual = m.Move("RITUAL_MOVE", Ritual, Intent.Buff);
        orbOfFrailty.Then(orbOfWeakness).Then(beam).Then(ritual).Then(orbOfFrailty);
        return orbOfFrailty;
    }

    private void OrbOfFrailty(Fight f)
    {
        Attack(f, OrbOfFrailtyDamage);
        ApplyToPlayer<FrailPower>(f, 1);
    }

    private void OrbOfWeakness(Fight f)
    {
        Attack(f, OrbOfWeaknessDamage);
        ApplyToPlayer<WeakPower>(f, 1);
    }

    private void Beam(Fight f) => Attack(f, BeamDamage, BeamHits);

    private void Ritual(Fight f) => ApplyToSelf<StrengthPower>(f, RitualStrength);
}

/// <summary>
/// The Kin: a follower that opens with the dance (slot1), a second follower (slot2) and the
/// priest (leaderSlot), created and acting in that order. Nothing is drawn from the encounter's
/// stream; only the followers' HP is rolled, and the two never get the same value.
/// </summary>
public sealed class TheKinBoss : Encounter
{
    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<string> Slots => ["slot1", "slot2", "leaderSlot"];

    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
    [
        (new KinFollower { StartsWithDance = true }, "slot1"),
        (new KinFollower(), "slot2"),
        (new KinPriest(), "leaderSlot"),
    ];
}

// ---------- Vantom ----------

/// <summary>
/// Vantom, 173 HP, starts with Slippery 8 (each of the first 8 losses of HP is cut to 1). A
/// fixed cycle of Ink Blot 7, Inky Lance 6x2, Dismember (26, then 3 Wounds into the player's
/// discard pile whether or not the hit was blocked) and Prepare (2 Strength). No phase change.
/// </summary>
public sealed class Vantom : Monster
{
    private const int SlipperyStacks = 8;
    private const int InkBlotDamage = 7;
    private const int InkyLanceDamage = 6;
    private const int InkyLanceHits = 2;
    private const int DismemberDamage = 26;
    private const int DismemberWounds = 3;
    private const int PrepareStrength = 2;

    public override string Name => "Vantom";
    public override int MinHp => 173;
    public override int MaxHp => 173;

    public override void AfterAddedToRoom(Fight f) => ApplyToSelf<SlipperyPower>(f, SlipperyStacks);

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var inkBlot = m.Move("INK_BLOT_MOVE", InkBlot, Intent.Attack(InkBlotDamage));
        var inkyLance = m.Move("INKY_LANCE_MOVE", InkyLance, Intent.Attack(InkyLanceDamage, InkyLanceHits));
        var dismember = m.Move("DISMEMBER_MOVE", Dismember, Intent.Attack(DismemberDamage), Intent.Status(DismemberWounds));
        var prepare = m.Move("PREPARE_MOVE", Prepare, Intent.Buff);
        inkBlot.Then(inkyLance).Then(dismember).Then(prepare).Then(inkBlot);
        return inkBlot;
    }

    private void InkBlot(Fight f) => Attack(f, InkBlotDamage);

    private void InkyLance(Fight f) => Attack(f, InkyLanceDamage, InkyLanceHits);

    private void Dismember(Fight f)
    {
        Attack(f, DismemberDamage);
        AddStatusCards(f, "Wound", DismemberWounds, PileType.Discard);
    }

    private void Prepare(Fight f) => ApplyToSelf<StrengthPower>(f, PrepareStrength);
}

/// <summary>Vantom alone, no slot, nothing drawn from the encounter's stream.</summary>
public sealed class VantomBoss : Encounter
{
    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new Vantom(), null)];
}
