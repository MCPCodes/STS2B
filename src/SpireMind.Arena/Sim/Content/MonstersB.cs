using System.Globalization;

namespace SpireMind.Arena;

// Overgrowth monsters, part B: the four slimes, Flyconid, Snapping Jaxfruit and Slithering
// Strangler, with the five encounters built from them (encounters-1.json and encounters-2.json
// of the spec, re-read against the v0.107.1 classes of the same names). Every number is the
// ascension 0 one. None of these monsters has a starting power, a summon, a split, an escape
// or a reaction to being hit; none of the encounters names slots, so the line-up order is the
// acting order.

/// <summary>Registers the monsters and encounters of this file, and Constrict, which only the Strangler uses.</summary>
public sealed class MonstersB : IContentModule
{
    public void Register(Registry r)
    {
        r.Power<ConstrictPower>();

        r.Monster<LeafSlimeM>();
        r.Monster<LeafSlimeS>();
        r.Monster<TwigSlimeM>();
        r.Monster<TwigSlimeS>();
        r.Monster<Flyconid>();
        r.Monster<SnappingJaxfruit>();
        r.Monster<SlitheringStrangler>();

        r.Encounter<SlimesWeak>();
        r.Encounter<SlimesNormal>();
        r.Encounter<FlyconidNormal>();
        r.Encounter<SnappingJaxfruitNormal>();
        r.Encounter<SlitheringStranglerNormal>();
    }

    /// <summary>The game's pool of small slimes, in its order: an encounter's pick indexes this list.</summary>
    internal static List<string> SmallSlimes() => ["LeafSlimeS", "TwigSlimeS"];

    /// <summary>The game's pool of medium slimes, in its order.</summary>
    internal static List<string> MediumSlimes() => ["LeafSlimeM", "TwigSlimeM"];

    /// <summary>A new slime of the kind an encounter picked from one of the pools.</summary>
    internal static Monster Slime(string className) => className switch
    {
        "LeafSlimeS" => new LeafSlimeS(),
        "TwigSlimeS" => new TwigSlimeS(),
        "LeafSlimeM" => new LeafSlimeM(),
        "TwigSlimeM" => new TwigSlimeM(),
        _ => throw new ArgumentException($"{className} is not a slime.", nameof(className)),
    };
}

// ---------- the power ----------

/// <summary>
/// Constrict (Slithering Strangler puts 3 on the player per use). At the end of the turn of its
/// owner's side the owner takes damage equal to the amount, with the owner itself as dealer:
/// not modified by Strength, Weak or Vulnerable, but Block absorbs it, and at the end of the
/// player's turn the player still holds that turn's Block. It never counts down; applying it
/// again adds to the one instance. It ends when the creature that first applied it dies.
/// </summary>
public sealed class ConstrictPower : Power
{
    public override string Name => "Constrict";
    public override PowerType Type => PowerType.Debuff;

    public override string Describe()
    {
        var damage = string.Create(CultureInfo.InvariantCulture, $"At the end of its turn, takes {Amount} damage (Block absorbs it). Does not wear off");
        return Applier is { IsPlayer: false } a ? $"{damage}; ends when {a.Name} dies." : $"{damage}.";
    }

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        // For the player this is after the hand was flushed. The landing turn does nothing:
        // the Strangler applies it on the enemy turn, whose participants are the enemies.
        if (participants.Contains(Owner)) f.Damage(Owner, Amount, Props.NonCardUnpowered, Owner);
    }

    public override void AfterDeath(Fight f, Creature creature, bool wasRemovalPrevented)
    {
        // Only a real death of the first applier; later stacks from someone else do not change who that is.
        if (!wasRemovalPrevented && ReferenceEquals(creature, Applier)) f.RemovePower(this);
    }
}

// ---------- the slimes ----------

/// <summary>
/// Leaf Slime (M), 32 to 35 HP. Alternates Sticky Shot (2 Slimed into the discard pile) and
/// Clump Shot (8), Sticky Shot first. No random branch, so it never draws a MonsterAi number.
/// </summary>
public sealed class LeafSlimeM : Monster
{
    private const int ClumpDamage = 8;
    private const int StickyCards = 2;

    public override string Name => "Leaf Slime (M)";
    public override int MinHp => 32;
    public override int MaxHp => 35;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var clump = m.Move("CLUMP_SHOT", ClumpShot, Intent.Attack(ClumpDamage));
        var sticky = m.Move("STICKY_SHOT", StickyShot, Intent.Status(StickyCards));
        clump.Then(sticky).Then(clump);
        return sticky;
    }

    private void ClumpShot(Fight f) => Attack(f, ClumpDamage);

    // Created one at a time at the bottom of the discard pile.
    private void StickyShot(Fight f) => AddStatusCards(f, "Slimed", StickyCards, PileType.Discard, PilePosition.Bottom);
}

/// <summary>
/// Leaf Slime (S), 11 to 15 HP. Tackle (3) or Goop (1 Slimed into the discard pile). Its machine
/// starts in the random branch, where neither move may follow itself: the first move is 50/50
/// and the two then alternate, with one MonsterAi draw per pick even though the pick is forced.
/// </summary>
public sealed class LeafSlimeS : Monster
{
    private const int TackleDamage = 3;
    private const int GoopCards = 1;

    public override string Name => "Leaf Slime (S)";
    public override int MinHp => 11;
    public override int MaxHp => 15;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var tackle = m.Move("TACKLE_MOVE", Tackle, Intent.Attack(TackleDamage));
        var goop = m.Move("GOOP_MOVE", Goop, Intent.Status(GoopCards));
        var rand = m.Random("RAND")
            .Add(tackle, 1f, MoveRepeat.CannotRepeat)
            .Add(goop, 1f, MoveRepeat.CannotRepeat);
        tackle.FollowUp = rand;
        goop.FollowUp = rand;
        return rand;
    }

    private void Tackle(Fight f) => Attack(f, TackleDamage);

    private void Goop(Fight f) => AddStatusCards(f, "Slimed", GoopCards, PileType.Discard, PilePosition.Bottom);
}

/// <summary>
/// Twig Slime (M), 26 to 28 HP. Opens with Sticky Shot (1 Slimed into the discard pile); after
/// that a random branch between Pokey Pounce (11, at most twice in a row) and Sticky Shot (never
/// twice in a row), one MonsterAi draw per pick.
/// </summary>
public sealed class TwigSlimeM : Monster
{
    private const int PounceDamage = 11;
    private const int StickyCards = 1;

    public override string Name => "Twig Slime (M)";
    public override int MinHp => 26;
    public override int MaxHp => 28;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var pounce = m.Move("POKEY_POUNCE_MOVE", PokeyPounce, Intent.Attack(PounceDamage));
        var sticky = m.Move("STICKY_SHOT_MOVE", StickyShot, Intent.Status(StickyCards));
        var rand = m.Random("RAND")
            .Add(pounce, 1f, MoveRepeat.MaxInARow, maxInARow: 2)
            .Add(sticky, 1f, MoveRepeat.CannotRepeat);
        pounce.FollowUp = rand;
        sticky.FollowUp = rand;
        return sticky;
    }

    private void PokeyPounce(Fight f) => Attack(f, PounceDamage);

    private void StickyShot(Fight f) => AddStatusCards(f, "Slimed", StickyCards, PileType.Discard, PilePosition.Bottom);
}

/// <summary>Twig Slime (S), 7 to 11 HP. Tackle (4) every turn; no random branch.</summary>
public sealed class TwigSlimeS : Monster
{
    private const int TackleDamage = 4;

    public override string Name => "Twig Slime (S)";
    public override int MinHp => 7;
    public override int MaxHp => 11;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var tackle = m.Move("TACKLE_MOVE", Tackle, Intent.Attack(TackleDamage));
        tackle.FollowUp = tackle;
        return tackle;
    }

    private void Tackle(Fight f) => Attack(f, TackleDamage);
}

// ---------- Flyconid ----------

/// <summary>
/// Flyconid, 47 to 49 HP. Vulnerable Spores (2 Vulnerable on the player), Frail Spores (8, then
/// 2 Frail on the player) and Smash (11). The first move comes from a branch of its own that
/// holds only Frail Spores and Smash; every later one from a branch where Vulnerable Spores is
/// shut while it is among the last 3 chosen moves, Frail Spores while among the last 2, and
/// Smash right after itself. After Vulnerable Spores, Frail Spores, Smash all three are shut and
/// the first listed, Vulnerable Spores, is taken.
/// </summary>
public sealed class Flyconid : Monster
{
    private const int SmashDamage = 11;
    private const int SporeDamage = 8;
    private const int VulnerableAmount = 2;
    private const int FrailAmount = 2;

    public override string Name => "Flyconid";
    public override int MinHp => 47;
    public override int MaxHp => 49;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var vulnerable = m.Move("VULNERABLE_SPORES_MOVE", VulnerableSpores, Intent.Debuff);
        var frail = m.Move("FRAIL_SPORES_MOVE", FrailSpores, Intent.Attack(SporeDamage), Intent.Debuff);
        var smash = m.Move("SMASH_MOVE", Smash, Intent.Attack(SmashDamage));

        // The 3 and the 2 are cooldowns, not weights: every weight is 1.
        var rand = m.Random("RAND")
            .Add(vulnerable, 1f, MoveRepeat.CannotRepeat, cooldown: 3)
            .Add(frail, 1f, MoveRepeat.CannotRepeat, cooldown: 2)
            .Add(smash, 1f, MoveRepeat.CannotRepeat);
        var initial = m.Random("INITIAL")
            .Add(frail, 1f, MoveRepeat.CannotRepeat, cooldown: 2)
            .Add(smash, 1f, MoveRepeat.CannotRepeat);

        vulnerable.FollowUp = rand;
        frail.FollowUp = rand;
        smash.FollowUp = rand;
        return initial;
    }

    private void VulnerableSpores(Fight f) => ApplyToPlayer<VulnerablePower>(f, VulnerableAmount);

    private void FrailSpores(Fight f)
    {
        // The hit first: this move's own damage never meets its Frail.
        Attack(f, SporeDamage);
        ApplyToPlayer<FrailPower>(f, FrailAmount);
    }

    private void Smash(Fight f) => Attack(f, SmashDamage);
}

// ---------- Snapping Jaxfruit ----------

/// <summary>
/// Snapping Jaxfruit, 31 to 33 HP. One move every turn: Energy Orb hits for 3 and then gives
/// itself 2 Strength, so the hits run 3, 5, 7, 9... No random branch.
/// </summary>
public sealed class SnappingJaxfruit : Monster
{
    private const int EnergyDamage = 3;
    private const int StrengthGain = 2;

    public override string Name => "Snapping Jaxfruit";
    public override int MinHp => 31;
    public override int MaxHp => 33;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var orb = m.Move("ENERGY_ORB_MOVE", EnergyOrb, Intent.Attack(EnergyDamage), Intent.Buff);
        orb.FollowUp = orb;
        return orb;
    }

    private void EnergyOrb(Fight f)
    {
        // The hit comes before the Strength, so it does not include this turn's gain.
        Attack(f, EnergyDamage);
        ApplyToSelf<StrengthPower>(f, StrengthGain);
    }
}

// ---------- Slithering Strangler ----------

/// <summary>
/// Slithering Strangler, 53 to 55 HP. Constrict (3 Constrict on the player) on every odd turn;
/// on every even turn Thwack (7, then 5 Block for itself) or Lash (12), an even pick with no
/// memory. One MonsterAi draw at the start of each player turn that follows a Constrict.
/// </summary>
public sealed class SlitheringStrangler : Monster
{
    private const int ConstrictAmount = 3;
    private const int ThwackDamage = 7;
    private const int ThwackBlock = 5;
    private const int LashDamage = 12;

    public override string Name => "Slithering Strangler";
    public override int MinHp => 53;
    public override int MaxHp => 55;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var constrict = m.Move("CONSTRICT", Constrict, Intent.Debuff);
        var thwack = m.Move("THWACK", Thwack, Intent.Attack(ThwackDamage), Intent.Defend);
        var lash = m.Move("LASH", Lash, Intent.Attack(LashDamage));
        var rand = m.Random("rand")
            .Add(thwack, 1f, MoveRepeat.Forever)
            .Add(lash, 1f, MoveRepeat.Forever);
        constrict.FollowUp = rand;
        thwack.FollowUp = constrict;
        lash.FollowUp = constrict;
        return constrict;
    }

    private void Constrict(Fight f) => ApplyToPlayer<ConstrictPower>(f, ConstrictAmount);

    private void Thwack(Fight f)
    {
        Attack(f, ThwackDamage);
        // Move Block: it stays through the player's next turn and falls when the enemies' turn starts.
        GainBlock(f, ThwackBlock);
    }

    private void Lash(Fight f) => Attack(f, LashDamage);
}

// ---------- the encounters ----------

/// <summary>
/// Three slimes: a small one, a medium one, the other small one. Three draws on the encounter's
/// stream: which small slime stands first; a pick among the one small slime left (forced, but
/// drawn); Leaf or Twig for the medium one. One of the act's weak encounters.
/// </summary>
public sealed class SlimesWeak : Encounter
{
    public override bool IsWeak => true;

    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng)
    {
        var smalls = MonstersB.SmallSlimes();
        var first = rng.NextItem(smalls)!;
        smalls.Remove(first);
        var last = rng.NextItem(smalls)!;
        var medium = rng.NextItem(MonstersB.MediumSlimes())!;
        return [(MonstersB.Slime(first), null), (MonstersB.Slime(medium), null), (MonstersB.Slime(last), null)];
    }
}

/// <summary>
/// One of each slime: Twig Slime (M), Leaf Slime (M), then the two small ones in an order set by
/// one coin flip of the encounter's stream (heads: Leaf before Twig).
/// </summary>
public sealed class SlimesNormal : Encounter
{
    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng)
    {
        var leafFirst = rng.NextBool();
        Monster third = leafFirst ? new LeafSlimeS() : new TwigSlimeS();
        Monster fourth = leafFirst ? new TwigSlimeS() : new LeafSlimeS();
        return [(new TwigSlimeM(), null), (new LeafSlimeM(), null), (third, null), (fourth, null)];
    }
}

/// <summary>A medium slime (Leaf or Twig, one pick of the encounter's stream), then a Flyconid. The slime acts first.</summary>
public sealed class FlyconidNormal : Encounter
{
    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(MonstersB.Slime(rng.NextItem(MonstersB.MediumSlimes())!), null), (new Flyconid(), null)];
}

/// <summary>A Snapping Jaxfruit, then a Flyconid. Nothing random in the line-up.</summary>
public sealed class SnappingJaxfruitNormal : Encounter
{
    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new SnappingJaxfruit(), null), (new Flyconid(), null)];
}

/// <summary>
/// A Slithering Strangler behind one of three sets of companions, one pick of the encounter's
/// stream: a Snapping Jaxfruit; one medium slime (one more pick: Leaf or Twig); or two small
/// slimes, each picked on its own, so both may be the same kind. The Strangler is always last.
/// </summary>
public sealed class SlitheringStranglerNormal : Encounter
{
    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng)
    {
        var lineUp = new List<(Monster Monster, string? Slot)>();
        // The game picks among its three kinds of company in this order.
        switch (rng.NextInt(0, 3))
        {
            case 0:
                lineUp.Add((new SnappingJaxfruit(), null));
                break;
            case 1:
                lineUp.Add((MonstersB.Slime(rng.NextItem(MonstersB.MediumSlimes())!), null));
                break;
            default:
                lineUp.Add((MonstersB.Slime(rng.NextItem(MonstersB.SmallSlimes())!), null));
                lineUp.Add((MonstersB.Slime(rng.NextItem(MonstersB.SmallSlimes())!), null));
                break;
        }
        lineUp.Add((new SlitheringStrangler(), null));
        return lineUp;
    }
}
