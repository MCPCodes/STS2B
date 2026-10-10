namespace SpireMind.Arena;

// Applying, changing and removing powers (rules-turns.md R51 to R53, R56; rules-damage.md R15).
public sealed partial class Fight
{
    /// <summary>
    /// Applies <paramref name="amount"/> of a power. If the creature already has it the amount
    /// is added to that instance; otherwise a new instance goes to the end of the creature's
    /// power list. Artifact and the other modifiers see the amount first. Refused while the
    /// fight is ending, for an amount of 0, and for a creature that cannot receive powers.
    /// Returns the instance, or null when nothing was applied or the result was 0.
    /// </summary>
    public T? ApplyPower<T>(Creature target, decimal amount, Creature? applier, CardInstance? cardSource = null) where T : Power, new() =>
        ApplyPower(new T(), target, amount, applier, cardSource) as T;

    /// <summary>The same by class name, for a power defined in another content file.</summary>
    public Power? ApplyPower(string className, Creature target, decimal amount, Creature? applier, CardInstance? cardSource = null) =>
        ApplyPower(Content.Registry.NewPower(className), target, amount, applier, cardSource);

    private Power? ApplyPower(Power fresh, Creature target, decimal amount, Creature? applier, CardInstance? cardSource)
    {
        if (IsEnding || !CanReceivePowers(target)) return null;

        var existing = FindForStacking(fresh, target, applier);
        if (existing is not null)
            return ModifyPowerAmount(existing, amount, applier, cardSource) == 0 ? null : existing;

        if (amount == 0m) return null;

        var power = fresh;
        power.Applier = applier;
        power.Owner = target; // hooks may ask the type of the amount before it is attached
        Each(l => l.BeforePowerAmountChanged(this, power, amount, target, applier, cardSource));

        var modified = amount;
        if (applier is not null && applier.InCombat) modified = ModifyPowerAmountGiven(power, applier, modified, target, cardSource);
        modified = ModifyPowerAmountReceived(power, target, modified, applier, out var receivedModifiers);

        power.BeforeApplied(this, target, modified, applier, cardSource);
        if (!CanReceivePowers(target)) return null;

        // A power that was blocked down to 0 is never attached (R-damage R15).
        var attached = (int)modified != 0 || modified != 0m;
        if (modified != 0m)
        {
            power.Amount = Math.Clamp((int)modified, -Cap, Cap);
            target.PowerList.Add(power);
            _s.History.PowerReceived(this, power, modified, applier);
        }

        // R53: a NEW debuff on the player's side skips its next duration tick, whoever applied it.
        if (target.Side == Side.Player && power.Type == PowerType.Debuff) power.SkipNextDurationTick = true;

        if (receivedModifiers is not null)
            foreach (var l in receivedModifiers) l.AfterModifyingPowerAmountReceived(this, power);

        if (modified != 0m)
        {
            power.AfterApplied(this, applier, cardSource);
            Each(l => l.AfterPowerAmountChanged(this, power, modified, applier, cardSource));
        }
        return attached && target.PowerList.Contains(power) ? power : null;
    }

    private static Power? FindForStacking(Power fresh, Creature target, Creature? applier)
    {
        switch (fresh.InstanceType)
        {
            case PowerInstanceType.Instanced:
                return null;
            case PowerInstanceType.InstancedPerApplier:
                foreach (var p in target.PowerList)
                    if (p.GetType() == fresh.GetType() && ReferenceEquals(p.Applier, applier)) return p;
                return null;
            default:
                foreach (var p in target.PowerList)
                    if (p.GetType() == fresh.GetType()) return p;
                return null;
        }
    }

    private decimal ModifyPowerAmountGiven(Power power, Creature giver, decimal amount, Creature? target, CardInstance? cardSource)
    {
        if (!_s.Starting && IsOverOrEnding) return amount;
        var list = RentListeners();
        foreach (var l in list)
            if (StillListening(l)) amount += l.ModifyPowerAmountGivenAdditive(this, power, giver, amount, target, cardSource);
        foreach (var l in list)
            if (StillListening(l)) amount *= l.ModifyPowerAmountGivenMultiplicative(this, power, giver, amount, target, cardSource);
        ReturnListeners(list);
        return amount;
    }

    private decimal ModifyPowerAmountReceived(Power power, Creature target, decimal amount, Creature? applier, out List<Listener>? modifiers)
    {
        modifiers = null;
        if (!_s.Starting && IsOverOrEnding) return amount;
        var list = RentListeners();
        foreach (var l in list)
        {
            if (!StillListening(l) || !l.TryModifyPowerAmountReceived(this, power, target, amount, applier, out var changed)) continue;
            amount = changed;
            (modifiers ??= new()).Add(l);
        }
        ReturnListeners(list);
        return amount;
    }

    /// <summary>
    /// Changes the amount of a power a creature already has by <paramref name="offset"/> (which
    /// may be negative), through the same modifiers as an application, and removes the power when
    /// its amount calls for it. Returns the new amount; 0 when the fight is ending.
    /// </summary>
    public int ModifyPowerAmount(Power power, decimal offset, Creature? applier, CardInstance? cardSource = null)
    {
        if (IsEnding) return 0;
        var owner = power.Owner;
        if (!owner.InCombat) return 0;

        Each(l => l.BeforePowerAmountChanged(this, power, offset, owner, applier, cardSource));
        var modified = offset;
        if (applier is not null && applier.InCombat) modified = ModifyPowerAmountGiven(power, applier, modified, owner, cardSource);
        modified = ModifyPowerAmountReceived(power, owner, modified, applier, out var receivedModifiers);
        _s.History.PowerReceived(this, power, modified, applier);

        var newAmount = Math.Clamp(power.Amount + (int)modified, -Cap, Cap);
        power.Amount = newAmount;

        if (receivedModifiers is not null)
            foreach (var l in receivedModifiers) l.AfterModifyingPowerAmountReceived(this, power);
        if ((int)modified != 0)
            Each(l => l.AfterPowerAmountChanged(this, power, modified, applier, cardSource));
        if (power.ShouldRemoveDueToAmount) RemovePower(power);
        return newAmount;
    }

    /// <summary>Lowers a power by 1 (a plain countdown: Colossus, Plating).</summary>
    public void Decrement(Power power) => ModifyPowerAmount(power, -1m, null);

    /// <summary>The duration tick of Vulnerable, Weak and Frail: a power flagged to skip its next
    /// tick only loses the flag; otherwise it loses 1 (R53).</summary>
    public void TickDownDuration(Power power)
    {
        if (power.SkipNextDurationTick) power.SkipNextDurationTick = false;
        else Decrement(power);
    }

    /// <summary>Takes a power off its owner and runs its removal callback. Works while the fight is ending.
    /// As in the game, the callback also runs for a power that was already off its owner (a power
    /// that was removed earlier in a dispatch and then counts itself down to nothing in the same one).</summary>
    public void RemovePower(Power? power)
    {
        if (power is null) return;
        power.Owner.PowerList.Remove(power);
        power.AfterRemoved(this, power.Owner);
    }

    public void RemovePower<T>(Creature creature) where T : Power => RemovePower(creature.GetPower<T>());
}
