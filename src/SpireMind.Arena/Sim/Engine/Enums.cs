namespace SpireMind.Arena;

// Names and member names follow the game's own enums where the mod prints them
// (type, target, pile, power kind, intent kind, unplayable reason), so snapshots read the same.

public enum Side { Player, Enemy }

public enum CardType { None, Attack, Skill, Power, Status, Curse, Quest }

public enum CardRarity { None, Basic, Common, Uncommon, Rare, Ancient, Event, Token, Status, Curse, Quest }

public enum TargetType { None, Self, AnyEnemy, AllEnemies, RandomEnemy, AnyPlayer, AnyAlly, AllAllies, TargetedNoCreature, Osty }

[Flags]
public enum CardKeywords
{
    None = 0, Exhaust = 1, Ethereal = 2, Innate = 4, Unplayable = 8, Retain = 16, Sly = 32, Eternal = 64,
}

[Flags]
public enum CardTags { None = 0, Strike = 1, Defend = 2 }

public enum PileType { None, Draw, Hand, Discard, Exhaust, Play, Deck }

/// <summary>Where a card lands in a pile. Index 0 of every pile is its top; Bottom appends.</summary>
public enum PilePosition { Bottom, Top, Random }

public enum PowerType { None, Buff, Debuff }

public enum PowerStackType { None, Counter, Single }

public enum PowerInstanceType { None, Instanced, InstancedPerApplier }

public enum IntentType
{
    Attack, Buff, Debuff, DebuffStrong, Defend, Escape, Heal, Hidden, Summon, Sleep, Stun, StatusCard, CardDebuff, DeathBlow, Unknown,
}

public enum AutoPlayType { None, Default, SlyDiscard }

public enum RoomType { Monster, Elite, Boss }

public enum PotionRarity { None, Common, Uncommon, Rare, Event, Token }

public enum PotionUsage { None, CombatOnly, AnyTime, Automatic }

public enum TurnPhase { None, Start, AutoPrePlay, Play, AutoPostPlay, End }

/// <summary>Why a hand card cannot be played. The mod prints "not enough energy" for the
/// energy flag and this enum's own text for the rest, so the names and values are the game's.</summary>
[Flags]
public enum UnplayableReason
{
    None = 0,
    HasUnplayableKeyword = 2,
    BlockedByHook = 4,
    BlockedByCardLogic = 8,
    EnergyCostTooHigh = 0x10,
    StarCostTooHigh = 0x20,
    NoLivingAllies = 0x40,
}

/// <summary>
/// What kind of damage or block an amount is (rules-damage.md R2). Move marks an amount that
/// comes from a card or a monster move; Unpowered switches off Strength, Weak, Vulnerable,
/// Dexterity, Frail and the like; Unblockable skips Block.
/// </summary>
[Flags]
public enum ValueProp
{
    None = 0,
    Unblockable = 2,
    Unpowered = 4,
    Move = 8,
    SkipHurtAnim = 0x10,
}

/// <summary>The named flag combinations the game uses, and the two tests nearly every modifier asks.</summary>
public static class Props
{
    /// <summary>Attack or block from a card: modified by powers, absorbed by Block.</summary>
    public const ValueProp Card = ValueProp.Move;

    /// <summary>A status card hurting its holder (Burn, Infection): blockable, not modified.</summary>
    public const ValueProp CardUnpowered = ValueProp.Unpowered | ValueProp.Move;

    /// <summary>"Lose N HP" on a card: ignores Block, not modified.</summary>
    public const ValueProp CardHpLoss = ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move;

    /// <summary>Attack or block from a monster move: modified by powers, absorbed by Block.</summary>
    public const ValueProp MonsterMove = ValueProp.Move;

    /// <summary>Damage or block from a power, relic or potion: blockable, not modified.</summary>
    public const ValueProp NonCardUnpowered = ValueProp.Unpowered;

    /// <summary>HP loss from a power (Poison, Inferno's self-damage): ignores Block, not modified.</summary>
    public const ValueProp NonCardHpLoss = ValueProp.Unblockable | ValueProp.Unpowered;

    /// <summary>Move set and Unpowered not set: the test of Strength, Weak, Vulnerable, Shrink, Slow, Flame Barrier.</summary>
    public static bool IsPoweredAttack(this ValueProp props) =>
        (props & ValueProp.Move) != 0 && (props & ValueProp.Unpowered) == 0;

    /// <summary>The same test for block: Dexterity and Frail.</summary>
    public static bool IsPoweredBlock(this ValueProp props) => props.IsPoweredAttack();

    /// <summary>Move set, powered or not (Unmovable).</summary>
    public static bool IsCardOrMove(this ValueProp props) => (props & ValueProp.Move) != 0;

    public static bool IsUnblockable(this ValueProp props) => (props & ValueProp.Unblockable) != 0;

    public static Side Opposite(this Side side) => side == Side.Player ? Side.Enemy : Side.Player;
}
