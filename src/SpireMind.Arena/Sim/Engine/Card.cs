using System.Globalization;

namespace SpireMind.Arena;

/// <summary>How a card number is previewed in hand: damage and block numbers show the player's
/// own modifiers (rules-damage.md R24), everything else shows as it is.</summary>
public enum VarKind { Auto, Plain, Damage, Block }

/// <summary>One named number of a card ("Damage" 8). An int or decimal converts to one whose
/// kind follows the name: "Damage" is a damage number, "Block" a block number, the rest plain.</summary>
public readonly record struct CardVar(decimal Value, VarKind Kind, ValueProp Props)
{
    public static implicit operator CardVar(int value) => new(value, VarKind.Auto, ValueProp.Move);
    public static implicit operator CardVar(decimal value) => new(value, VarKind.Auto, ValueProp.Move);
}

/// <summary>Explicit card numbers, for when the name does not say the kind or the flags are not a plain card's.</summary>
public static class Var
{
    public static CardVar Plain(decimal value) => new(value, VarKind.Plain, ValueProp.None);
    public static CardVar Damage(decimal value, ValueProp props = Props.Card) => new(value, VarKind.Damage, props);
    public static CardVar Block(decimal value, ValueProp props = Props.Card) => new(value, VarKind.Block, props);
}

/// <summary>The numbers of a card definition, in declaration order. Use a collection initializer:
/// <c>Vars = { ["Damage"] = 8, ["Vulnerable"] = 2 }</c>.</summary>
public sealed class VarSet
{
    private readonly List<string> _names = new();
    private readonly List<CardVar> _vars = new();

    public CardVar this[string name]
    {
        get => _vars[IndexOf(name)];
        set
        {
            var kind = value.Kind != VarKind.Auto ? value.Kind : name switch { "Damage" => VarKind.Damage, "Block" => VarKind.Block, _ => VarKind.Plain };
            var v = value with { Kind = kind };
            var i = _names.IndexOf(name);
            if (i >= 0) _vars[i] = v;
            else
            {
                _names.Add(name);
                _vars.Add(v);
            }
        }
    }

    public IReadOnlyList<string> Names => _names;

    public bool Has(string name) => _names.Contains(name);

    internal int IndexOf(string name)
    {
        var i = _names.IndexOf(name);
        if (i < 0) throw new KeyNotFoundException($"The card has no number called '{name}'. It has: {string.Join(", ", _names)}.");
        return i;
    }

    internal decimal[] Values() => _vars.Select(v => v.Value).ToArray();
    internal CardVar At(int i) => _vars[i];
}

/// <summary>
/// One card as the registry knows it: what never changes about it. The class name is the
/// game's ("StrikeIronclad"); <see cref="Name"/> is the title the player sees ("Strike").
/// Everything that can change in a fight (upgrade, numbers, cost changes) is on <see cref="CardInstance"/>.
/// </summary>
public sealed class CardDef
{
    /// <summary>Pass as the cost of an X-cost card.</summary>
    public const int CostX = -2;

    /// <summary>The cost of a card that cannot be paid for (the unplayable statuses).</summary>
    public const int NoCost = -1;

    public CardDef(string className, string name, CardType type, CardRarity rarity, int cost, TargetType target)
    {
        ClassName = className;
        Name = name;
        Type = type;
        Rarity = rarity;
        CostsX = cost == CostX;
        Cost = CostsX ? 0 : cost;
        Target = target;
    }

    public string ClassName { get; }
    public string Name { get; }
    public CardType Type { get; }
    public CardRarity Rarity { get; }

    /// <summary>Printed energy cost; -1 for none; 0 for X-cost cards (see <see cref="CostsX"/>).</summary>
    public int Cost { get; }
    public bool CostsX { get; }
    public TargetType Target { get; }

    public CardKeywords Keywords { get; init; }
    public CardTags Tags { get; init; }

    /// <summary>The card's numbers, by the game's variable names.</summary>
    public VarSet Vars { get; init; } = new();

    /// <summary>1 for every card that can be upgraded, 0 for statuses and curses.</summary>
    public int MaxUpgradeLevel { get; init; } = 1;

    /// <summary>Applies the upgrade to an instance: change numbers, the cost, keywords. Effects that
    /// differ when upgraded branch on <see cref="CardInstance.IsUpgraded"/> in <see cref="Play"/>.</summary>
    public Action<CardInstance>? Upgrade { get; init; }

    /// <summary>The card's effect: one run of it. Null for cards that do nothing when played.</summary>
    public Action<PlayContext>? Play { get; init; }

    /// <summary>The rules text in our own plain words, with the live numbers from the context.
    /// Keywords are added by the engine; do not write "Exhaust." yourself.</summary>
    public Func<TextContext, string>? Text { get; init; }

    /// <summary>A hook object per instance, for cards that react while they sit in a pile (Void, Stomp).</summary>
    public Func<CardBehaviour>? Behaviour { get; init; }

    /// <summary>Set for the cards that do something when they are in hand at the end of the turn
    /// (Burn, Infection, Beckon...). The engine moves the card to the Play pile, runs this, then
    /// discards it (or exhausts it if Ethereal).</summary>
    public Action<Fight, CardInstance>? TurnEndInHand { get; init; }

    /// <summary>
    /// For a card whose damage is worked out from the fight (Body Slam, Ashen Strike, Perfected
    /// Strike, Bully): the multiplier. Per hit the card deals CalculationBase + ExtraDamage x this,
    /// both being numbers of the card under those names. The target is the hit's single target,
    /// or null (in the hand text, and when another effect reads the card's damage). Declare it
    /// here, not inside <see cref="Play"/>, so that the text, the play and Thrash agree.
    /// </summary>
    public Func<Fight, CardInstance, Creature?, decimal>? DamageMultiplier { get; init; }

    /// <summary>The card's own extra play condition (false reads "BlockedByCardLogic").</summary>
    public Func<Fight, CardInstance, bool>? IsPlayable { get; init; }

    /// <summary>False for cards random generation never makes (Feed, Not Yet, some statuses).</summary>
    public bool CanBeGeneratedInCombat { get; init; } = true;

    /// <summary>The title when it is not just the name plus "+" (Wither counts its fake upgrades).</summary>
    public Func<CardInstance, string>? Title { get; init; }

    public bool HasTurnEndInHandEffect => TurnEndInHand is not null;

    /// <summary>What random generation in a fight may produce: not Basic, Ancient or Event rarity,
    /// and not a card flagged as never generated (Feed, Not Yet).</summary>
    public bool IsGeneratableInCombat =>
        CanBeGeneratedInCombat && Rarity is not (CardRarity.Basic or CardRarity.Ancient or CardRarity.Event);

    public override string ToString() => ClassName;
}

/// <summary>A cost change that sits on one card instance (rules for when it ends are the game's).</summary>
internal struct LocalCost
{
    public int Amount;
    public bool Absolute;
    public bool EndsAtTurnEnd;
    public bool EndsWhenPlayed;
    public bool ReduceOnly;

    public readonly int Modify(int cost) => Absolute
        ? (ReduceOnly ? Math.Min(cost, Amount) : Amount)
        : (ReduceOnly ? Math.Min(cost, cost + Amount) : cost + Amount);
}

/// <summary>
/// One card in a fight. It keeps its own copy of everything that can change: upgrade level,
/// numbers, keywords, cost changes, an affliction a monster put on it.
/// </summary>
public sealed class CardInstance
{
    private decimal[] _vars;
    private bool _varsOwned;
    private List<LocalCost>? _localCosts;

    internal CardInstance(int id, CardDef def)
    {
        Id = id;
        Def = def;
        _vars = def.Vars.Values();
        _varsOwned = true;
        Keywords = def.Keywords;
        BaseCost = def.Cost;
        if (def.Behaviour is { } make)
        {
            Behaviour = make();
            Behaviour.Card = this;
        }
    }

    /// <summary>Unique within the fight and kept by clones, so a card can be found again.</summary>
    public int Id { get; }

    public CardDef Def { get; }

    public int UpgradeLevel { get; internal set; }
    public bool IsUpgraded => UpgradeLevel > 0;
    public bool IsUpgradable => UpgradeLevel < Def.MaxUpgradeLevel;

    public CardType Type => Def.Type;
    public TargetType Target => Def.Target;

    /// <summary>The instance's keywords; upgrades and effects may add or remove some.</summary>
    public CardKeywords Keywords { get; set; }

    public bool Has(CardKeywords keyword) => (Keywords & keyword) != 0;
    public bool HasTag(CardTags tag) => (Def.Tags & tag) != 0;

    /// <summary>The pile the card is in; None when it is in no pile (just created, or removed from the fight).</summary>
    public PileType Pile { get; internal set; }

    /// <summary>Base energy cost of this instance (an upgrade may have lowered it); -1 for none.</summary>
    public int BaseCost { get; internal set; }

    public bool CostsX => Def.CostsX;

    /// <summary>For an X-cost card: the X of its current or last play, before <see cref="Listener.ModifyXValue"/>.</summary>
    public int CapturedX { get; internal set; }

    /// <summary>Set by Havoc-like effects: the next play sends the card to the exhaust pile. Cleared at end of turn.</summary>
    public bool ExhaustOnNextPlay { get; set; }

    /// <summary>Keep in hand at the end of this turn only. Cleared at end of turn.</summary>
    public bool RetainThisTurn { get; set; }

    /// <summary>Extra runs of the effect on every play of this instance (Soldier's Stew).</summary>
    public int ReplayCount { get; set; }

    /// <summary>A copy that leaves the fight after it is played, like a Power card.</summary>
    public bool IsDupe { get; set; }

    /// <summary>Made by <see cref="Fight.CloneCard"/> as a copy of another card of this fight (Anger, Juggling).</summary>
    public bool IsClone { get; private set; }

    /// <summary>The class name of an affliction a monster put on the card ("Entangled", "Ringing"), or null.</summary>
    public string? Affliction { get; internal set; }
    public int AfflictionAmount { get; internal set; }

    /// <summary>The player knows where this card sits in the draw pile (it was put on top);
    /// a determinized clone leaves it there.</summary>
    internal bool KnownPosition { get; set; }

    /// <summary>The card's hook object, when its definition has one.</summary>
    public CardBehaviour? Behaviour { get; private set; }

    /// <summary>The title the player sees: the name, plus "+" when upgraded.</summary>
    public string Title => Def.Title?.Invoke(this) ?? (IsUpgraded ? Def.Name + "+" : Def.Name);

    // ---------- numbers ----------

    /// <summary>The current value of one of the card's numbers ("Damage").</summary>
    public decimal Get(string name) => _vars[Def.Vars.IndexOf(name)];

    public bool HasVar(string name) => Def.Vars.Has(name);

    /// <summary>Changes one of this instance's numbers for the rest of the fight (Rampage, upgrades).</summary>
    public void Set(string name, decimal value)
    {
        if (!_varsOwned)
        {
            _vars = (decimal[])_vars.Clone();
            _varsOwned = true;
        }
        _vars[Def.Vars.IndexOf(name)] = value;
    }

    public void Add(string name, decimal delta) => Set(name, Get(name) + delta);

    internal CardVar VarDef(string name) => Def.Vars[name];

    // ---------- cost changes (the game's CardEnergyCost) ----------

    private void AddLocal(int amount, bool absolute, bool turnEnd, bool played, bool reduceOnly)
    {
        if (absolute ? amount == 0 && Def.Cost < 0 : amount == 0) return;
        (_localCosts ??= new()).Add(new LocalCost { Amount = amount, Absolute = absolute, EndsAtTurnEnd = turnEnd, EndsWhenPlayed = played, ReduceOnly = reduceOnly });
    }

    public void SetCostUntilPlayed(int cost, bool reduceOnly = false) => AddLocal(cost, true, false, true, reduceOnly);
    public void SetCostThisTurnOrUntilPlayed(int cost, bool reduceOnly = false) => AddLocal(cost, true, true, true, reduceOnly);
    public void SetCostThisTurn(int cost, bool reduceOnly = false) => AddLocal(cost, true, true, false, reduceOnly);
    public void SetCostThisCombat(int cost, bool reduceOnly = false) => AddLocal(cost, true, false, false, reduceOnly);
    public void AddCostUntilPlayed(int amount, bool reduceOnly = false) => AddLocal(amount, false, false, true, reduceOnly);
    public void AddCostThisTurnOrUntilPlayed(int amount, bool reduceOnly = false) => AddLocal(amount, false, true, true, reduceOnly);
    public void AddCostThisTurn(int amount, bool reduceOnly = false) => AddLocal(amount, false, true, false, reduceOnly);
    public void AddCostThisCombat(int amount, bool reduceOnly = false) => AddLocal(amount, false, false, false, reduceOnly);

    /// <summary>An upgrade's cost change ("3 -> 2" is -1). Absolute cost overrides above the new base come down with it.</summary>
    public void UpgradeCostBy(int addend)
    {
        if (CostsX || addend == 0) return;
        var old = BaseCost;
        var now = Math.Max(BaseCost + addend, 0);
        if (now < old && _localCosts is not null)
        {
            for (var i = 0; i < _localCosts.Count; i++)
            {
                var m = _localCosts[i];
                if (m.Absolute && m.Amount > now)
                {
                    m.Amount = now;
                    _localCosts[i] = m;
                }
            }
        }
        BaseCost = now;
    }

    /// <summary>The cost after this instance's own changes, before the hooks of powers and relics.
    /// Negative base costs and X costs are never changed.</summary>
    internal int LocalCostValue()
    {
        var cost = BaseCost;
        if (cost < 0 || CostsX) return cost;
        if (_localCosts is not null)
            foreach (var m in _localCosts) cost = m.Modify(cost);
        return cost;
    }

    internal void EndOfTurnCleanup()
    {
        ExhaustOnNextPlay = false;
        RetainThisTurn = false;
        _localCosts?.RemoveAll(m => m.EndsAtTurnEnd);
    }

    internal void AfterPlayedCleanup() => _localCosts?.RemoveAll(m => m.EndsWhenPlayed);

    internal void ApplyUpgrade()
    {
        UpgradeLevel++;
        Def.Upgrade?.Invoke(this);
    }

    public override string ToString() => $"{Title}#{Id}";

    // ---------- copies ----------

    private CardInstance(CardInstance o, int id, bool keepPile)
    {
        Id = id;
        Def = o.Def;
        _vars = o._vars;
        _varsOwned = false;
        o._varsOwned = false; // both copy on their next write
        _localCosts = o._localCosts is null ? null : new List<LocalCost>(o._localCosts);
        UpgradeLevel = o.UpgradeLevel;
        Keywords = o.Keywords;
        BaseCost = o.BaseCost;
        CapturedX = o.CapturedX;
        ReplayCount = o.ReplayCount;
        Affliction = o.Affliction;
        AfflictionAmount = o.AfflictionAmount;
        RetainThisTurn = o.RetainThisTurn;
        IsDupe = o.IsDupe;
        IsClone = o.IsClone;
        if (keepPile)
        {
            Pile = o.Pile;
            ExhaustOnNextPlay = o.ExhaustOnNextPlay;
            KnownPosition = o.KnownPosition;
        }
    }

    /// <summary>The same card in a cloned fight.</summary>
    internal CardInstance CloneFor(CloneMap map)
    {
        var copy = new CardInstance(this, Id, keepPile: true);
        map.Add(this, copy);
        if (Behaviour is not null)
        {
            copy.Behaviour = Behaviour.CloneFor(map);
            copy.Behaviour.Card = copy;
        }
        return copy;
    }

    /// <summary>A new card that is a copy of this one as it is now (Anger, Juggling): same upgrade,
    /// numbers, cost changes and hook state, in no pile, and not flagged to exhaust on its next play.</summary>
    internal CardInstance Duplicate(int newId)
    {
        var copy = new CardInstance(this, newId, keepPile: false) { IsClone = true };
        if (Behaviour is not null)
        {
            copy.Behaviour = Behaviour.CopyFor(copy);
        }
        return copy;
    }
}

/// <summary>
/// The hook object of one card instance: a <see cref="Listener"/> that knows its card. It is
/// heard while the card is in any combat pile (hand, draw, discard, exhaust, play) and the
/// player is alive. Test <c>card == Card</c> in hooks that are about one card.
/// </summary>
public abstract class CardBehaviour : Listener
{
    public CardInstance Card { get; internal set; } = null!;

    internal CardBehaviour CloneFor(CloneMap map)
    {
        var copy = (CardBehaviour)MemberwiseClone();
        map.Add(this, copy);
        return copy;
    }

    /// <summary>The hook object of a card that copies this one's card within the same fight.</summary>
    internal CardBehaviour CopyFor(CardInstance card)
    {
        var copy = (CardBehaviour)MemberwiseClone();
        copy.Card = card;
        copy.OnCloned(new CloneMap());
        return copy;
    }
}

/// <summary>One run of a card's effect. A card that is played twice by an effect has two of these, one per run.</summary>
public sealed class CardPlay
{
    public required CardInstance Card { get; init; }
    public required Creature? Target { get; init; }

    /// <summary>Where the card goes afterwards (decided before the effect ran).</summary>
    public required PileType ResultPile { get; init; }

    public required bool IsAutoPlay { get; init; }
    public required int PlayIndex { get; init; }
    public required int PlayCount { get; init; }

    /// <summary>Energy actually paid (0 when auto-played).</summary>
    public required int EnergySpent { get; init; }

    /// <summary>The cost the play is worth (for an auto-played X card: the energy the player had).</summary>
    public required int EnergyValue { get; init; }

    /// <summary>Unique per run within the fight; history entries carry it.</summary>
    public required int PlayId { get; init; }

    public bool IsFirstInSeries => PlayIndex == 0;
    public bool IsLastInSeries => PlayIndex == PlayCount - 1;
}

/// <summary>
/// What a card's <see cref="CardDef.Play"/> gets: the fight, the card, its target, and short
/// forms of the commands a card uses most. Anything else is on <see cref="F"/>.
/// </summary>
public sealed class PlayContext
{
    internal PlayContext(Fight f, CardPlay play)
    {
        F = f;
        Play = play;
    }

    public Fight F { get; }
    public CardPlay Play { get; }
    public CardInstance Card => Play.Card;

    /// <summary>The player.</summary>
    public Creature Owner => F.Player;

    /// <summary>The chosen (or, auto-played, the rolled) enemy of an AnyEnemy card; null for every other card.</summary>
    public Creature? Target => Play.Target;

    public bool Upgraded => Card.IsUpgraded;

    /// <summary>X of an X-cost card: the energy spent (auto-played: the energy the player has), after X modifiers.</summary>
    public int X => F.ResolveX(Card);

    /// <summary>One of the card's numbers.</summary>
    public decimal V(string name) => Card.Get(name);

    /// <summary>
    /// An attack from this card for <paramref name="damagePerHit"/>, aimed by the card's target
    /// type: the chosen enemy, all enemies, or a random enemy per hit. Chain <c>.Hits(n)</c> if
    /// needed and finish with <c>.Execute()</c>.
    /// </summary>
    public AttackCommand Attack(decimal damagePerHit) => Attack(_ => damagePerHit);

    /// <summary>An attack with the card's calculated damage (see <see cref="CardDef.DamageMultiplier"/>), worked out again before every hit.</summary>
    public AttackCommand CalculatedAttack() => Attack(target => F.CalculatedDamage(Card, target));

    /// <summary>An attack whose damage is worked out again before every hit by a function of your own.</summary>
    public AttackCommand Attack(Func<Creature?, decimal> damagePerHit)
    {
        var attack = F.Attack(Owner, damagePerHit).FromCard(Card);
        return Card.Target switch
        {
            TargetType.AnyEnemy => attack.Targeting(Target),
            TargetType.RandomEnemy => attack.TargetingRandomOpponent(),
            _ => attack.TargetingAllOpponents(),
        };
    }

    /// <summary>Block from this card (Dexterity and Frail apply). Returns the unrounded amount gained.</summary>
    public decimal GainBlock(decimal amount) => F.GainBlock(Owner, amount, Props.Card, Play);

    /// <summary>"Lose N HP": ignores Block, not modified, with the card as the source.</summary>
    public void LoseHp(decimal amount) => F.Damage(Owner, amount, Props.CardHpLoss, Owner, Card);

    /// <summary>Applies a power with the player as applier and this card as source.</summary>
    public T? Apply<T>(Creature? target, decimal amount) where T : Power, new() =>
        target is null ? null : F.ApplyPower<T>(target, amount, Owner, Card);

    public T? ApplyToSelf<T>(decimal amount) where T : Power, new() => F.ApplyPower<T>(Owner, amount, Owner, Card);

    public IReadOnlyList<CardInstance> Draw(decimal count) => F.Draw(count);

    public void GainEnergy(decimal amount) => F.GainEnergy(amount);
}

/// <summary>
/// What a card's <see cref="CardDef.Text"/> gets. Damage and block numbers come back as the
/// game previews them: with the player's own modifiers and no target while the card is in the
/// hand or the Play pile of a running fight, the plain number everywhere else
/// (rules-damage.md R24, R25).
/// </summary>
public sealed class TextContext
{
    internal TextContext(Fight? f, CardInstance card, PileType pile)
    {
        F = f;
        Card = card;
        Pile = pile;
    }

    internal TextContext(Fight? f, CardInstance card, PileType pile, Creature? target) : this(f, card, pile) => Target = target;

    /// <summary>Null when the card is described outside a fight (the deck list).</summary>
    public Fight? F { get; }
    public CardInstance Card { get; }
    public PileType Pile { get; }

    /// <summary>
    /// The enemy the numbers are worked out against, for the snapshot's "vs" entries (the mod
    /// previews a hand card against each enemy it can be aimed at); null for the text at rest.
    /// <see cref="Damage"/>, <see cref="CalculatedDamage"/> and <see cref="CalcBlock"/> use it;
    /// a text that works out a number of its own must pass it on too.
    /// </summary>
    public Creature? Target { get; }
    public bool Upgraded => Card.IsUpgraded;

    private bool Live => F is not null && Pile is PileType.Hand or PileType.Play;

    /// <summary>A plain number as a whole number ("Cards", "Vulnerable", "HpLoss").</summary>
    public int N(string name) => (int)Card.Get(name);

    /// <summary>A number by its declared kind: damage and block numbers previewed, the rest plain.</summary>
    public int V(string name) => Card.VarDef(name).Kind switch
    {
        VarKind.Damage => Damage(name),
        VarKind.Block => Block(name),
        _ => N(name),
    };

    /// <summary>A damage number as shown: base, plus Strength, times Weak and Shrink, truncated. At rest there is
    /// no target, so no Vulnerable; against <see cref="Target"/> the target's side of the chain counts too.</summary>
    public int Damage(string name = "Damage")
    {
        var v = Card.VarDef(name);
        var amount = Card.Get(name);
        if (Live) amount = F!.ModifyDamage(Target, F.Player, amount, v.Kind == VarKind.Damage ? v.Props : Props.Card, Card);
        return (int)amount;
    }

    /// <summary>A block number as shown: base, plus Dexterity, times Frail, truncated.</summary>
    public int Block(string name = "Block")
    {
        var v = Card.VarDef(name);
        var amount = Card.Get(name);
        if (Live) amount = F!.ModifyBlock(F.Player, amount, v.Kind == VarKind.Block ? v.Props : Props.Card, Card, null);
        return (int)amount;
    }

    /// <summary>
    /// The card's calculated damage number as shown (see <see cref="CardDef.DamageMultiplier"/>):
    /// CalculationBase + ExtraDamage x the multiplier, then previewed like any damage. The
    /// multiplier is asked with no target (Bully shows only its base at rest), and counts as 0
    /// outside a running fight, as in the game.
    /// </summary>
    public int CalculatedDamage(ValueProp props = Props.Card)
    {
        var amount = F is not null ? F.CalculatedDamage(Card, Target) : Card.Get("CalculationBase");
        if (Live) amount = F!.ModifyDamage(Target, F.Player, amount, props, Card);
        return (int)Math.Max(amount, 0m);
    }

    /// <summary>The same for a calculated block number.</summary>
    public int CalcBlock(decimal baseValue, decimal extra, Func<Fight, Creature?, decimal> multiplier, ValueProp props = Props.Card)
    {
        var m = F is { InProgress: true } ? multiplier(F, Target) : 0m;
        var amount = baseValue + extra * m;
        if (Live) amount = F!.ModifyBlock(F.Player, amount, props, Card, null);
        return (int)Math.Max(amount, 0m);
    }

    /// <summary>"1 card" / "2 cards".</summary>
    public static string Count(int n, string singular, string? plural = null) =>
        string.Create(CultureInfo.InvariantCulture, $"{n} {(n == 1 ? singular : plural ?? singular + "s")}");
}
