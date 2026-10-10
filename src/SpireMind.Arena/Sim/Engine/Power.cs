namespace SpireMind.Arena;

/// <summary>
/// A power on a creature. The class name is the game's class name ("StrengthPower"); content
/// looks powers up by it. A power hears every hook (it is a <see cref="Listener"/>) and
/// filters for itself: "is my owner the dealer", "did my owner's side just end its turn".
/// </summary>
public abstract class Power : Listener
{
    /// <summary>The creature that has this power. Stays set after the power is removed, as in the game.</summary>
    public Creature Owner { get; internal set; } = null!;

    /// <summary>Who applied the instance (set once, when it is created).</summary>
    public Creature? Applier { get; internal set; }

    /// <summary>Stored amount, a whole number between plus and minus 999,999,999. Change it only
    /// through <see cref="Fight.ApplyPower{T}"/>, <see cref="Fight.ModifyPowerAmount"/> and friends.</summary>
    public int Amount { get; internal set; }

    /// <summary>The amount when the owner's side last started a turn.</summary>
    public int AmountOnTurnStart { get; internal set; }

    /// <summary>Set on a new Debuff on the player's side; <see cref="Fight.TickDownDuration"/> clears it instead of ticking once.</summary>
    public bool SkipNextDurationTick { get; set; }

    /// <summary>The game's class name; the registry key.</summary>
    public string ClassName => GetType().Name;

    /// <summary>The game's English title, as the snapshot's power "name".</summary>
    public abstract string Name { get; }

    /// <summary>Buff or Debuff. The fixed type: the skip-first-tick flag tests this, not the sign of the amount.</summary>
    public abstract PowerType Type { get; }

    /// <summary>Counter (a visible amount) or Single (presence only).</summary>
    public virtual PowerStackType StackType => PowerStackType.Counter;

    /// <summary>None: one instance per creature, applying again adds to it. Instanced: every application is its own instance.</summary>
    public virtual PowerInstanceType InstanceType => PowerInstanceType.None;

    /// <summary>True for Strength, Dexterity, Shrink: negative amounts are kept and the power is removed only at exactly 0.</summary>
    public virtual bool AllowNegative => false;

    /// <summary>Hidden powers are not listed in snapshots, and Artifact does not block them.</summary>
    public virtual bool IsVisible => true;

    /// <summary>What the snapshot prints as "amount" (left out when 0). Slow shows its counter times 10.</summary>
    public virtual int DisplayAmount => Amount;

    /// <summary>What the power does, in our own plain words with the live numbers. The snapshot's power "text".</summary>
    public abstract string Describe();

    /// <summary>True on Minion: the owner does not keep the fight alive.</summary>
    public virtual bool OwnerIsSecondaryEnemy => false;

    /// <summary>False keeps the power on its owner through death (Minion, the revive powers).</summary>
    public virtual bool ShouldBeRemovedAfterOwnerDeath => true;

    /// <summary>False on Minion: killing the owner does not count as Fatal (Feed).</summary>
    public virtual bool ShouldOwnerDeathTriggerFatal => true;

    /// <summary>
    /// Runs for a new instance just before it is attached, with the amount after Artifact and
    /// the other modifiers (0 when it was blocked). Temporary Strength hands out its Strength here.
    /// </summary>
    public virtual void BeforeApplied(Fight f, Creature target, decimal amount, Creature? applier, CardInstance? cardSource) { }

    /// <summary>Runs once for a new instance after it was attached with a non-zero amount.</summary>
    public virtual void AfterApplied(Fight f, Creature? applier, CardInstance? cardSource) { }

    /// <summary>Runs after the power was removed through <see cref="Fight.RemovePower"/> or by its owner's death.</summary>
    public virtual void AfterRemoved(Fight f, Creature oldOwner) { }

    /// <summary>Buff or Debuff for an amount: a negative amount of a power that allows negatives is a Debuff
    /// (what Artifact and the snapshot's "kind" look at); a tick-down of a plain Debuff counts as a Buff.</summary>
    public PowerType TypeForAmount(decimal amount)
    {
        if (StackType == PowerStackType.Counter && AllowNegative && amount < 0m) return PowerType.Debuff;
        if (!AllowNegative && Type == PowerType.Debuff && amount < 0m) return PowerType.Buff;
        return Type;
    }

    public PowerType TypeForCurrentAmount => TypeForAmount(Amount);

    /// <summary>Removed at 0 or below, or for a power that allows negatives only at exactly 0.</summary>
    public bool ShouldRemoveDueToAmount => AllowNegative ? Amount == 0 : Amount <= 0;

    internal Power CloneFor(CloneMap map)
    {
        var copy = (Power)MemberwiseClone();
        map.Add(this, copy);
        return copy;
    }

    /// <summary>"1 turn" / "3 turns", for descriptions of duration powers.</summary>
    protected static string Turns(int n) => n == 1 ? "1 turn" : $"{n} turns";
}

/// <summary>What one damage instance did to one creature.</summary>
public sealed class DamageResult(Creature receiver, ValueProp props)
{
    public Creature Receiver { get; } = receiver;
    public ValueProp Props { get; } = props;

    /// <summary>Block removed (whole number).</summary>
    public int BlockedDamage { get; internal set; }

    /// <summary>HP actually removed.</summary>
    public int UnblockedDamage { get; internal set; }

    /// <summary>Damage beyond the HP the target had.</summary>
    public int OverkillDamage { get; internal set; }

    public int TotalDamage => BlockedDamage + UnblockedDamage;

    public bool WasBlockBroken { get; internal set; }
    public bool WasFullyBlocked { get; internal set; }

    /// <summary>The hit's amount was at least the target's HP at that moment.</summary>
    public bool WasTargetKilled { get; internal set; }
}
