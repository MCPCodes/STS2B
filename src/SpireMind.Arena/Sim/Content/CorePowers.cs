using System.Globalization;

namespace SpireMind.Arena;

// The powers more than one content file needs. Each follows its game class of the same name
// (v0.107.1); the rule numbers are those of rules-damage.md.

/// <summary>Registers the shared powers, the Ironclad's relic, the basic cards, the status cards and the Nibbit.</summary>
public sealed class CoreContent : IContentModule
{
    public void Register(Registry r)
    {
        r.Power<StrengthPower>();
        r.Power<DexterityPower>();
        r.Power<VulnerablePower>();
        r.Power<WeakPower>();
        r.Power<FrailPower>();
        r.Power<ArtifactPower>();
        r.Power<MinionPower>();
        r.Power<SlipperyPower>();
        r.Power<ShrinkPower>();
        r.Power<PlatingPower>();

        r.Relic<BurningBlood>();

        CoreCards.Register(r);
        CoreStatuses.Register(r);

        r.Monster<Nibbit>();
        r.Encounter<NibbitsWeak>();
        r.Encounter<NibbitsNormal>();
    }
}

/// <summary>R10: adds its amount to every hit of a powered attack its owner deals. May be negative; removed only at exactly 0.</summary>
public sealed class StrengthPower : Power
{
    public override string Name => "Strength";
    public override PowerType Type => PowerType.Buff;
    public override bool AllowNegative => true;

    public override string Describe() => Amount >= 0
        ? string.Create(CultureInfo.InvariantCulture, $"Attacks deal {Amount} more damage per hit.")
        : string.Create(CultureInfo.InvariantCulture, $"Attacks deal {-Amount} less damage per hit.");

    public override decimal ModifyDamageAdditive(Fight f, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) =>
        ReferenceEquals(Owner, dealer) && props.IsPoweredAttack() ? Amount : 0m;
}

/// <summary>R11: adds its amount to powered card-or-move Block. With a card source the card's
/// owner must be the power's owner; without one, the creature gaining the Block must be.</summary>
public sealed class DexterityPower : Power
{
    public override string Name => "Dexterity";
    public override PowerType Type => PowerType.Buff;
    public override bool AllowNegative => true;

    public override string Describe() => Amount >= 0
        ? string.Create(CultureInfo.InvariantCulture, $"Block gained from cards is increased by {Amount}.")
        : string.Create(CultureInfo.InvariantCulture, $"Block gained from cards is reduced by {-Amount}.");

    public override decimal ModifyBlockAdditive(Fight f, Creature target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
    {
        // Every card belongs to the player.
        if (cardSource is not null ? !Owner.IsPlayer : !ReferenceEquals(Owner, target)) return 0m;
        return props.IsPoweredBlock() ? Amount : 0m;
    }
}

/// <summary>
/// R12: powered attacks received by its owner are multiplied by 1.5. The amount is a duration:
/// it ticks at the end of every enemy turn, whoever owns it (R15). The dealer's Cruelty raises
/// the factor by its amount in hundredths (looked up by name; the card file defines it).
/// </summary>
public sealed class VulnerablePower : Power
{
    public const decimal Factor = 1.5m;

    public override string Name => "Vulnerable";
    public override PowerType Type => PowerType.Debuff;

    public override string Describe() => $"Takes 50% more damage from attacks. Wears off in {Turns(Amount)}.";

    public override decimal ModifyDamageMultiplicative(Fight f, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner) || !props.IsPoweredAttack()) return 1m;
        var factor = Factor;
        // Cruelty: not when its owner is the one being hit.
        if (dealer?.GetPower("CrueltyPower") is { } cruelty && !ReferenceEquals(cruelty.Owner, target)) factor += cruelty.Amount / 100m;
        return factor;
    }

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (side == Side.Enemy) f.TickDownDuration(this);
    }
}

/// <summary>R13: powered attacks dealt by its owner are multiplied by 0.75. Ticks like Vulnerable.</summary>
public sealed class WeakPower : Power
{
    public const decimal Factor = 0.75m;

    public override string Name => "Weak";
    public override PowerType Type => PowerType.Debuff;

    public override string Describe() => $"Deals 25% less damage with attacks. Wears off in {Turns(Amount)}.";

    public override decimal ModifyDamageMultiplicative(Fight f, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) =>
        ReferenceEquals(dealer, Owner) && props.IsPoweredAttack() ? Factor : 1m;

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (side == Side.Enemy) f.TickDownDuration(this);
    }
}

/// <summary>R14: powered card-or-move Block gained by its owner is multiplied by 0.75, monsters included. Ticks like Vulnerable.</summary>
public sealed class FrailPower : Power
{
    public const decimal Factor = 0.75m;

    public override string Name => "Frail";
    public override PowerType Type => PowerType.Debuff;

    public override string Describe() => $"Gains 25% less Block from cards. Wears off in {Turns(Amount)}.";

    public override decimal ModifyBlockMultiplicative(Fight f, Creature target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay) =>
        ReferenceEquals(Owner, target) && props.IsPoweredBlock() ? Factor : 1m;

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (side == Side.Enemy) f.TickDownDuration(this);
    }
}

/// <summary>R17: a visible power whose type for the applied amount is Debuff becomes 0 on its
/// owner, and Artifact loses 1. Negative Strength counts; a tick-down of a debuff does not.</summary>
public sealed class ArtifactPower : Power
{
    public override string Name => "Artifact";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Negates the next {(Amount == 1 ? "debuff" : $"{Amount} debuffs")} it would receive.");

    public override bool TryModifyPowerAmountReceived(Fight f, Power power, Creature target, decimal amount, Creature? applier, out decimal modified)
    {
        modified = amount;
        if (!ReferenceEquals(target, Owner) || power.TypeForAmount(amount) != PowerType.Debuff || !power.IsVisible) return false;
        modified = 0m;
        return true;
    }

    public override void AfterModifyingPowerAmountReceived(Fight f, Power power) => f.Decrement(this);
}

/// <summary>R17: marks a secondary enemy. When no primary enemy is left alive the minions die and
/// the fight ends. It stays on its owner through death, and that death is not Fatal.</summary>
public sealed class MinionPower : Power
{
    public override string Name => "Minion";
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override bool OwnerIsSecondaryEnemy => true;
    public override bool ShouldBeRemovedAfterOwnerDeath => false;
    public override bool ShouldOwnerDeathTriggerFatal => false;

    public override string Describe() => "A minion: it dies when its leader dies, and the fight ends then.";
}

/// <summary>R17: any HP loss of 1 or more from one damage instance becomes exactly 1 (after Block,
/// whatever the kind of damage); a stack is used whenever at least 1 HP was lost.</summary>
public sealed class SlipperyPower : Power
{
    public override string Name => "Slippery";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"The next {(Amount == 1 ? "time" : $"{Amount} times")} it would lose HP, it loses only 1.");

    public override decimal ModifyHpLostAfterOsty(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) =>
        ReferenceEquals(target, Owner) && amount >= 1m ? 1m : amount;

    public override void AfterDamageReceived(Fight f, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardInstance? cardSource)
    {
        if (ReferenceEquals(target, Owner) && result.UnblockedDamage >= 1) f.Decrement(this);
    }
}

/// <summary>
/// R17: powered attacks dealt by its owner are multiplied by 0.70. A negative amount (Shrinker
/// Beetle applies -1) means no duration: it never ticks and is shown without a counter. A
/// positive amount loses 1 at the end of its owner's side's turn. It is removed when the
/// creature that applied it dies.
/// </summary>
public sealed class ShrinkPower : Power
{
    public const decimal Factor = 0.70m;

    public override string Name => "Shrink";
    public override PowerType Type => PowerType.Debuff;
    public override bool AllowNegative => true;
    public override PowerStackType StackType => IsInfinite ? PowerStackType.Single : PowerStackType.Counter;

    private bool IsInfinite => Amount < 0;

    public override string Describe() => IsInfinite
        ? Applier is { IsPlayer: false } a ? $"Deals 30% less damage with attacks until {a.Name} dies." : "Deals 30% less damage with attacks."
        : $"Deals 30% less damage with attacks. Wears off in {Turns(Amount)}.";

    public override decimal ModifyDamageMultiplicative(Fight f, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) =>
        ReferenceEquals(Owner, dealer) && props.IsPoweredAttack() ? Factor : 1m;

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (!IsInfinite && participants.Contains(Owner)) f.Decrement(this);
    }

    public override void AfterDeath(Fight f, Creature creature, bool wasRemovalPrevented)
    {
        if (!wasRemovalPrevented && ReferenceEquals(creature, Applier)) f.RemovePower(this);
    }
}

/// <summary>
/// R18 (Stone Armor, Heart of Iron): unpowered Block equal to its amount in the early pass
/// before its owner's side ends its turn; loses 1 at the start of each of its owner's turns
/// except the first of the fight. On a monster it also gives its Block just before the
/// player's first turn.
/// </summary>
public sealed class PlatingPower : Power
{
    public override string Name => "Plating";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"At the end of its turn, gains {Amount} Block. Plating goes down by 1 at the start of its turn.");

    public override void BeforeSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (side != Side.Player || Owner.IsPlayer || f.Round > 1) return;
        f.GainBlock(Owner, Amount, Props.NonCardUnpowered);
    }

    public override void BeforeSideTurnEndEarly(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(Owner)) f.GainBlock(Owner, Amount, Props.NonCardUnpowered);
    }

    public override void AfterSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (!participants.Contains(Owner)) return;
        if (Owner.IsPlayer ? f.Turn == 1 : f.Round == 1) return;
        // On a monster the game subtracts the number of players, which is 1 here.
        f.Decrement(this);
    }
}

/// <summary>
/// R10: the family behind Setup Strike, Mangle and the Flex and Shackling potions. Applying it
/// gives (or, for a negative kind, takes) the same amount of Strength at once; at the end of
/// its owner's side's turn it removes itself and gives the Strength back with a plain Strength
/// application of the opposite sign. A negative kind is a Debuff, so Artifact stops all of it;
/// and Artifact also stops the taking back of a positive one.
/// Derive one class per source and name it after the game's class (SetupStrikePower).
/// </summary>
public abstract class TemporaryStrengthPower : Power
{
    private bool _ignoreNextInstance;

    /// <summary>False for the kinds that lower Strength (Mangle, Shackling Potion).</summary>
    protected virtual bool IsPositive => true;

    public override PowerType Type => IsPositive ? PowerType.Buff : PowerType.Debuff;

    private int Sign => IsPositive ? 1 : -1;

    public override string Describe() => IsPositive
        ? string.Create(CultureInfo.InvariantCulture, $"Has {Amount} extra Strength until the end of its turn.")
        : string.Create(CultureInfo.InvariantCulture, $"Has {Amount} less Strength until the end of its turn.");

    /// <summary>The next application changes only this power's amount, not Strength.</summary>
    public void IgnoreNextInstance() => _ignoreNextInstance = true;

    public override void BeforeApplied(Fight f, Creature target, decimal amount, Creature? applier, CardInstance? cardSource)
    {
        if (_ignoreNextInstance) _ignoreNextInstance = false;
        else f.ApplyPower<StrengthPower>(target, Sign * amount, applier, cardSource);
    }

    public override void AfterPowerAmountChanged(Fight f, Power power, decimal amount, Creature? applier, CardInstance? cardSource)
    {
        // More stacks on an instance that is already there (a first application is handled above).
        if (!ReferenceEquals(power, this) || amount == Amount) return;
        if (_ignoreNextInstance) _ignoreNextInstance = false;
        else f.ApplyPower<StrengthPower>(Owner, Sign * amount, applier, cardSource);
    }

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (!participants.Contains(Owner)) return;
        f.RemovePower(this);
        f.ApplyPower<StrengthPower>(Owner, -Sign * Amount, Owner);
    }
}

/// <summary>A power that is tied to one creature it counts against (the game's Sandpit). Nothing in
/// Overgrowth has one; Frantic Escape looks for it.</summary>
public interface ITargetedPower
{
    Creature Target { get; }
}

/// <summary>The Ironclad's starting relic: after a won fight, heal 6 HP if alive (R-turns R48). Nothing during the fight.</summary>
public sealed class BurningBlood : Relic
{
    public const int HealAmount = 6;

    public override string Name => "Burning Blood";

    public override string Describe() => "At the end of combat, heal 6 HP.";

    public override void AfterCombatVictory(Fight f)
    {
        if (!f.Player.IsDead) f.Heal(f.Player, HealAmount);
    }
}
