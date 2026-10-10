namespace SpireMind.Arena;

// Damage, attacks, block, healing and death (rules-damage.md R1 to R9, R19 to R21;
// rules-turns.md R27, R45). Amounts in flight are exact decimals; they are truncated only
// where they are written into Block or HP.
public sealed partial class Fight
{
    private const int Cap = 999_999_999;

    // ---------- the damage modifier chain (R-damage R4) ----------

    /// <summary>
    /// One hit's amount after every damage modifier: all additions (Strength), then all factors
    /// (Weak, Vulnerable, Shrink, Slow), then caps, never below 0, never rounded. The same
    /// function serves real hits, monster intents and card previews; a preview passes no target.
    /// </summary>
    public decimal ModifyDamage(Creature? target, Creature? dealer, decimal amount, ValueProp props, CardInstance? cardSource)
    {
        // These hooks are not behind the "ending" guard: the killing hit resolves fully.
        var list = RentListeners();
        foreach (var l in list)
            if (StillListening(l)) amount += l.ModifyDamageAdditive(this, target, amount, props, dealer, cardSource);
        foreach (var l in list)
            if (StillListening(l)) amount *= l.ModifyDamageMultiplicative(this, target, amount, props, dealer, cardSource);
        var cap = decimal.MaxValue;
        foreach (var l in list)
        {
            if (!StillListening(l)) continue;
            var c = l.ModifyDamageCap(this, target, props, dealer, cardSource);
            if (c >= cap) continue;
            cap = c;
            if (amount > c) amount = c;
        }
        ReturnListeners(list);
        return Math.Max(0m, amount);
    }

    private decimal ModifyHpLost(Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource, bool afterOsty)
    {
        var list = RentListeners();
        List<Listener>? changed = null;
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var l in list)
            {
                if (!StillListening(l)) continue;
                var before = amount;
                amount = (afterOsty, pass) switch
                {
                    (false, 0) => l.ModifyHpLostBeforeOsty(this, target, amount, props, dealer, cardSource),
                    (false, _) => l.ModifyHpLostBeforeOstyLate(this, target, amount, props, dealer, cardSource),
                    (true, 0) => l.ModifyHpLostAfterOsty(this, target, amount, props, dealer, cardSource),
                    _ => l.ModifyHpLostAfterOstyLate(this, target, amount, props, dealer, cardSource),
                };
                if (decimal.Truncate(before) != decimal.Truncate(amount)) (changed ??= new()).Add(l);
            }
        }
        ReturnListeners(list);
        if (changed is not null)
        {
            foreach (var l in changed)
            {
                if (afterOsty) l.AfterModifyingHpLostAfterOsty(this);
                else l.AfterModifyingHpLostBeforeOsty(this);
            }
        }
        return amount;
    }

    /// <summary>
    /// For audits of the arena itself (the honesty check): told about every damage instance, with
    /// the amount as the damage chain left it. It is not part of the fight's state: copies do not
    /// carry it, and no rule reads it.
    /// </summary>
    internal Action<DamageTrace>? DamageTraced;

    /// <summary>The same for every entry the fight writes into its history, as it is written (the
    /// history itself is cleared when the fight is won, as the game's is).</summary>
    internal Action<HistoryEntry>? HistoryTraced;

    // ---------- one damage instance (R-damage R3) ----------

    public DamageResult Damage(Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource = null) =>
        Damage([target], amount, props, dealer, cardSource).FirstOrDefault() ?? new DamageResult(target, props);

    /// <summary>
    /// Everything that hurts a creature goes through here; attacks call it once per hit. All
    /// targets lose Block and HP first, in list order; then the reactions run per target; then
    /// the deaths are processed. There is no "fight is ending" gate: self-damage after the
    /// killing blow still lands.
    /// </summary>
    public IReadOnlyList<DamageResult> Damage(IReadOnlyList<Creature> targets, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource = null)
    {
        var results = new List<DamageResult>(targets.Count);

        // R3.1: a dead dealer deals nothing, and nothing reacts.
        if (dealer is { IsDead: true })
        {
            foreach (var t in targets) results.Add(new DamageResult(t, props));
            return results;
        }

        foreach (var target in targets)
        {
            if (target.IsDead) continue;

            var modified = ModifyDamage(target, dealer, amount, props, cardSource);
            DamageTraced?.Invoke(new DamageTrace(target.Id, dealer?.Id ?? -1, cardSource?.Id ?? 0, modified, props));
            Each(l => l.BeforeDamageReceived(this, target, modified, props, dealer, cardSource), guarded: false);

            // R7: Block absorbs; it drops by the whole-number part of what it absorbed.
            var blocked = props.IsUnblockable() ? 0m : Math.Min(target.Block, modified);
            target.Block -= (int)blocked;

            var unblocked = ModifyHpLost(target, Math.Max(modified - blocked, 0m), props, dealer, cardSource, afterOsty: false);
            unblocked = ModifyHpLost(target, unblocked, props, dealer, cardSource, afterOsty: true);

            var result = LoseHpInternal(target, unblocked, props);
            result.BlockedDamage = (int)blocked;
            result.WasBlockBroken = target.Block <= 0 && blocked > 0m;
            result.WasFullyBlocked = !props.IsUnblockable() && (blocked > 0m || target.Block > 0) && (int)unblocked == 0;

            if (_s.InProgress && !IsEnding) _s.History.DamageReceived(this, target, dealer, result, cardSource);
            if (target.IsPlayer) _s.DamageTaken += result.UnblockedDamage;
            results.Add(result);
        }

        // R3.3: reactions, target by target.
        List<Creature>? killed = null;
        foreach (var result in results)
        {
            var target = result.Receiver;
            if (result.WasBlockBroken) Each(l => l.AfterBlockBroken(this, target), guarded: false);
            if (result.UnblockedDamage > 0) Each(l => l.AfterCurrentHpChanged(this, target, -result.UnblockedDamage), guarded: false);
            Each(l => l.AfterDamageGiven(this, dealer, result, props, target, cardSource), guarded: false);
            if (!result.WasTargetKilled || !target.IsDead)
            {
                // Also for fully blocked hits and hits of 0; never for the hit that kills.
                Each(l => l.AfterDamageReceived(this, target, result, props, dealer, cardSource), guarded: false);
                Each(l => l.AfterDamageReceivedLate(this, target, result, props, dealer, cardSource), guarded: false);
            }
            else
            {
                (killed ??= new()).Add(target);
            }
        }

        // R3.4
        if (killed is not null) Kill(killed);
        return results;
    }

    private static DamageResult LoseHpInternal(Creature target, decimal amount, ValueProp props)
    {
        var before = target.Hp;
        var killed = before > 0 && amount >= before;
        var loss = (int)Math.Min(amount, Cap);
        target.Hp = Math.Max(before - loss, 0);
        return new DamageResult(target, props)
        {
            UnblockedDamage = before - target.Hp,
            WasTargetKilled = killed,
            OverkillDamage = killed ? Math.Max(loss - before, 0) : 0,
        };
    }

    // ---------- attacks (R-damage R6) ----------

    /// <summary>Starts an attack by <paramref name="attacker"/>: powered, one hit, no target yet.
    /// Pick the targeting, the hit count, then <see cref="AttackCommand.Execute"/>.</summary>
    public AttackCommand Attack(Creature attacker, decimal damagePerHit) => new(this, attacker, _ => damagePerHit, _s.NextAttackId++);

    /// <summary>An attack whose damage is worked out again before every hit; the function gets the
    /// hit's single target, or null when the hit lands on several creatures at once.</summary>
    public AttackCommand Attack(Creature attacker, Func<Creature?, decimal> damagePerHit) => new(this, attacker, damagePerHit, _s.NextAttackId++);

    /// <summary>
    /// The per-hit amount of a card with calculated damage, before modifiers: its CalculationBase
    /// plus its ExtraDamage times the card's multiplier (0 while no fight is in progress).
    /// </summary>
    public decimal CalculatedDamage(CardInstance card, Creature? target)
    {
        var multiplier = _s.InProgress && card.Def.DamageMultiplier is { } m ? m(this, card, target) : 0m;
        return card.Get("CalculationBase") + card.Get("ExtraDamage") * multiplier;
    }

    /// <summary>The per-hit damage number of an attack card as another effect reads it (Thrash): its
    /// calculated damage with no target if it has one, otherwise its Damage number; null when it has neither.</summary>
    public decimal? DamageNumberOf(CardInstance card) =>
        card.Def.DamageMultiplier is not null ? CalculatedDamage(card, null) : card.HasVar("Damage") ? card.Get("Damage") : null;

    // ---------- block (R-damage R8) ----------

    /// <summary>A Block gain's amount after Dexterity, Frail and the like; never below 0, not rounded.</summary>
    public decimal ModifyBlock(Creature target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!_s.Starting && IsOverOrEnding) return Math.Max(0m, block);
        var list = RentListeners();
        foreach (var l in list)
            if (StillListening(l)) block += l.ModifyBlockAdditive(this, target, block, props, cardSource, cardPlay);
        foreach (var l in list)
            if (StillListening(l)) block *= l.ModifyBlockMultiplicative(this, target, block, props, cardSource, cardPlay);
        ReturnListeners(list);
        return Math.Max(0m, block);
    }

    /// <summary>
    /// Gives Block. From a card pass <see cref="Props.Card"/> and the card play (or use
    /// <see cref="PlayContext.GainBlock"/>); from a monster move <see cref="Props.MonsterMove"/>;
    /// from a power, relic or potion <see cref="Props.NonCardUnpowered"/>. Does nothing, and
    /// returns 0, while the fight is over or ending, which includes the set-up before turn 1.
    /// Returns the unrounded amount.
    /// </summary>
    public decimal GainBlock(Creature creature, decimal amount, ValueProp props, CardPlay? cardPlay = null)
    {
        if (IsOverOrEnding) return 0m;
        var modified = ModifyBlock(creature, amount, props, cardPlay?.Card, cardPlay);
        if (modified > 0m)
        {
            creature.Block = (int)Math.Min(creature.Block + modified, Cap);
            _s.History.BlockGained(this, creature, (int)modified, props, cardPlay);
        }
        Each(l => l.AfterBlockGained(this, creature, modified, props, cardPlay?.Card));
        return modified;
    }

    /// <summary>Removes Block without damage; "block broken" fires when it reaches 0.</summary>
    public void LoseBlock(Creature creature, decimal amount)
    {
        if (IsOverOrEnding || creature.IsDead || amount <= 0m) return;
        var before = creature.Block;
        creature.Block = (int)Math.Max(creature.Block - amount, 0m);
        if (before > 0 && creature.Block <= 0) Each(l => l.AfterBlockBroken(this, creature), guarded: false);
    }

    // ---------- healing and max HP (R-damage R20) ----------

    /// <summary>Heals up to max HP. A monster is not healed while the fight is ending.</summary>
    public void Heal(Creature creature, decimal amount)
    {
        if (IsEnding && !creature.IsPlayer) return;
        var wasDead = creature.IsDead;
        creature.Hp = (int)Math.Min(creature.Hp + amount, creature.MaxHp);
        if (wasDead && creature.IsAlive && creature.IsPlayer) _s.PlayerHooksActive = true;
        // Only a heal of more than 0, and only for a creature still in the fight.
        if (amount > 0m && creature.InCombat) Each(l => l.AfterCurrentHpChanged(this, creature, amount), guarded: false);
    }

    /// <summary>Raises max HP and heals by the amount it actually rose.</summary>
    public void GainMaxHp(Creature creature, decimal amount)
    {
        var before = creature.MaxHp;
        SetMaxHp(creature, creature.MaxHp + amount);
        Heal(creature, creature.MaxHp - before);
    }

    /// <summary>Lowers max HP (never below 1); HP above the new maximum is lost as unblockable damage.</summary>
    public void LoseMaxHp(Creature creature, decimal amount, bool isFromCard)
    {
        var newMax = creature.MaxHp - amount;
        if (newMax < creature.Hp)
            Damage(creature, creature.Hp - newMax, isFromCard ? Props.CardHpLoss : Props.NonCardHpLoss, null);
        SetMaxHp(creature, Math.Max(1m, newMax));
    }

    private void SetMaxHp(Creature creature, decimal amount)
    {
        creature.MaxHp = (int)Math.Min(Math.Max(0m, amount), Cap);
        if (creature.Hp > creature.MaxHp) creature.Hp = creature.MaxHp;
        if (creature.MaxHp <= 0) Kill([creature]);
    }

    // ---------- death (R-damage R21, R-turns R45) ----------

    /// <summary>Kills creatures outright (a leader's minions; forced kills). Damage calls this for whoever a hit killed.</summary>
    public void Kill(IReadOnlyList<Creature> creatures, bool force = false)
    {
        if (creatures.Count == 0) return;
        foreach (var creature in creatures.ToList()) KillOne(creature, force, 0);
        // The loss is only noted here; the next win check processes it (R47).
        if (_s.Player.IsDead && _s.InProgress) _s.PendingLoss = true;
    }

    private void KillOne(Creature creature, bool force, int recursion)
    {
        if (!creature.InCombat && !creature.IsPlayer) return;

        if (creature.Hp > 0)
        {
            var hp = creature.Hp;
            creature.Hp = 0;
            if (creature.IsPlayer) _s.DamageTaken += hp;
            Each(l => l.AfterCurrentHpChanged(this, creature, -hp), guarded: false);
        }

        Each(l => l.BeforeDeath(this, creature), guarded: false);

        Listener? preventer = null;
        if (!force && creature.MaxHp > 0)
            preventer = FirstListener(l => !l.ShouldDie(this, creature)) ?? FirstListener(l => !l.ShouldDieLate(this, creature));
        if (preventer is not null)
        {
            if (recursion >= 10) throw new InvalidOperationException($"Something keeps preventing the death of {creature.Name}.");
            Each(l => l.AfterDeath(this, creature, true), guarded: false);
            // Only the one that prevented the death is told (it heals its owner and uses itself up).
            preventer.AfterPreventingDeath(this, creature);
            if (creature.IsDead) KillOne(creature, force, recursion + 1);
            return;
        }

        var remove = ShouldRemoveAfterDeath(creature);
        Each(l => l.AfterDeath(this, creature, false), guarded: false);

        // Read after the death hooks: monsters they summoned count as teammates.
        var teammates = creature.IsEnemy ? _s.Enemies.Where(t => t.IsAlive).ToList() : new List<Creature>();

        // A monster that died during its own move stays in the list until the move is done (R41).
        if (remove && creature.IsEnemy && _s.Enemies.Contains(creature) && !creature.Monster!.IsPerformingMove)
            RemoveFromEnemies(creature);

        var wasPrimary = creature.IsPrimaryEnemy;

        // Which powers go is settled for all of them first; then they all leave; only then does
        // each get its removal callback (so a callback never sees a sibling that is about to go).
        // A monster that has already left the fight is not put to the hook: all its removable
        // powers go, whatever another creature's power (Illusion) would say.
        var leaving = new List<Power>();
        foreach (var power in creature.PowerList)
        {
            if (!power.ShouldBeRemovedAfterOwnerDeath) continue;
            if (creature.InCombat && !AllListeners(l => l.ShouldPowerBeRemovedOnDeath(this, power), guarded: false)) continue;
            leaving.Add(power);
        }
        foreach (var power in leaving) creature.PowerList.Remove(power);
        foreach (var power in leaving) power.AfterRemoved(this, creature);

        if (creature.IsEnemy)
        {
            // The last primary enemy takes its minions with it.
            if (wasPrimary && teammates.Count != 0 && teammates.All(t => t.IsSecondaryEnemy)) Kill(teammates);
        }
        else
        {
            _s.PlayerHooksActive = false;
        }
    }

    private bool ShouldRemoveAfterDeath(Creature creature) =>
        AllListeners(l => l.ShouldCreatureBeRemovedFromCombatAfterDeath(this, creature), guarded: false);
}

/// <summary>One damage instance on one creature as the damage chain left it: after every damage
/// modifier, before Block and before the HP-loss modifiers. Creatures and the card by their ids
/// (-1 for no dealer, 0 for no card), so that it stays meaningful when the fight is copied.</summary>
internal readonly record struct DamageTrace(int TargetId, int DealerId, int CardId, decimal Modified, ValueProp Props);

/// <summary>
/// One attack: an attacker, damage per hit, a hit count and a way of choosing targets. Each hit
/// is its own damage instance: modifiers are read again, the amount is truncated on its own,
/// reactions and deaths run before the next hit (R-damage R6, R-turns R27).
/// </summary>
public sealed class AttackCommand
{
    private enum Aim { None, Single, AllOpponents, RandomOpponent }

    private readonly Fight _f;
    private readonly Func<Creature?, decimal> _damage;
    private readonly List<DamageResult> _results = new();
    private Aim _aim;
    private Creature? _single;

    internal AttackCommand(Fight f, Creature attacker, Func<Creature?, decimal> damage, int id)
    {
        _f = f;
        Attacker = attacker;
        _damage = damage;
        Id = id;
    }

    /// <summary>Unique within the fight, from 1. A power that has to recognise "its" attack again
    /// between the before and after hooks keeps this number, which survives a copy of the fight.</summary>
    public int Id { get; }

    public Creature Attacker { get; }
    public CardInstance? CardSource { get; private set; }
    public ValueProp Props { get; private set; } = ValueProp.Move;
    public int HitCount { get; private set; } = 1;

    /// <summary>Every damage result of every hit, in order. Empty when the attack did not happen.</summary>
    public IReadOnlyList<DamageResult> Results => _results;

    /// <summary>Some hit of this attack killed its target.</summary>
    public bool KilledAny => _results.Any(r => r.WasTargetKilled);

    public AttackCommand FromCard(CardInstance card)
    {
        CardSource = card;
        return this;
    }

    /// <summary>One creature. If it dies, the remaining hits are lost; there is no retargeting.</summary>
    public AttackCommand Targeting(Creature? target)
    {
        _aim = Aim.Single;
        _single = target;
        return this;
    }

    /// <summary>Everyone alive on the other side, read again before every hit.</summary>
    public AttackCommand TargetingAllOpponents()
    {
        _aim = Aim.AllOpponents;
        return this;
    }

    /// <summary>One living opponent picked anew for every hit from the CombatTargets stream, the same one allowed again.</summary>
    public AttackCommand TargetingRandomOpponent()
    {
        _aim = Aim.RandomOpponent;
        return this;
    }

    public AttackCommand Hits(int count)
    {
        HitCount = count;
        return this;
    }

    /// <summary>Not modified by Strength, Weak, Vulnerable and the rest. No in-scope card or monster attack uses it.</summary>
    public AttackCommand Unpowered()
    {
        Props |= ValueProp.Unpowered;
        return this;
    }

    public AttackCommand Execute()
    {
        if (_f.IsOverOrEnding || Attacker.IsDead) return this;
        if (_aim == Aim.None) throw new InvalidOperationException("The attack has no targets set.");

        _f.Each(l => l.BeforeAttack(_f, this));
        for (var hit = 0; hit < HitCount; hit++)
        {
            if (Attacker.IsDead) break;

            IReadOnlyList<Creature> possible = _aim == Aim.Single
                ? (_single is null ? [] : [_single])
                : _f.OpponentsOf(Attacker);
            var valid = possible.Where(c => c.IsAlive).ToList();
            if (valid.Count == 0) break;

            Creature? single = _aim == Aim.RandomOpponent
                ? _f.Rng.CombatTargets.NextItem(valid)
                : valid.Count == 1 ? valid[0] : null;

            var amount = _damage(single);
            _results.AddRange(_f.Damage(single is not null ? [single] : valid, amount, Props, Attacker, CardSource));
        }
        _f.History.CreatureAttacked(_f, Attacker, _results.Count);
        _f.Each(l => l.AfterAttack(_f, this));
        return this;
    }
}
