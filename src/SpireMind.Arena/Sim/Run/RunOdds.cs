namespace SpireMind.Arena;

// The three odds a run carries from room to room, as the game keeps them: one 32-bit float
// each, changed by 32-bit float additions. The arithmetic is done in float on purpose: a
// roll is compared with these values, and 0.1f added five times is not the 0.5 a double
// would give for every such sum (0.02f five times is 0.099999994f, and a save shows it).
// None of them owns a stream: the caller draws with RunStreams.Draw and passes the stream.

/// <summary>Which table of base odds a card rarity roll uses.</summary>
public enum CardOddsType
{
    /// <summary>A hallway fight's reward, and most cards made outside fights.</summary>
    Regular,
    Elite,
    /// <summary>A boss's reward: always Rare, and the pity offset is left out of the roll.</summary>
    Boss,
    Shop,
    /// <summary>One third each, before the offset is added.</summary>
    Uniform,
}

/// <summary>
/// The rarity of a card that is rolled, with the pity offset of the run: it starts at -0.05,
/// grows by 0.01 with every rolled card that is not Rare (up to 0.4) and falls back to -0.05
/// after a Rare. Ascension 0 values (the Scarcity level changes them).
/// </summary>
public sealed class CardRarityOdds
{
    public const float BaseOffset = -0.05f;
    public const float MaxOffset = 0.4f;
    public const float Growth = 0.01f;

    public const float RegularRare = 0.03f;
    public const float RegularUncommon = 0.37f;
    public const float RegularCommon = 0.6f;
    public const float EliteRare = 0.1f;
    public const float EliteUncommon = 0.4f;
    public const float EliteCommon = 0.5f;
    public const float ShopRare = 0.09f;
    public const float ShopUncommon = 0.37f;
    public const float ShopCommon = 0.54f;
    public const float UniformEach = 0.33f;

    /// <summary>The pity offset: what a save writes as <c>card_rarity_odds_value</c>.</summary>
    public float Offset { get; private set; }

    public CardRarityOdds(float offset = BaseOffset) => Offset = offset;

    public CardRarityOdds Clone() => new(Offset);

    /// <summary>The base chance of a rarity in a table (Rare, Uncommon or Common).</summary>
    public static float BaseOdds(CardOddsType type, CardRarity rarity) => (type, rarity) switch
    {
        (CardOddsType.Regular, CardRarity.Rare) => RegularRare,
        (CardOddsType.Regular, CardRarity.Uncommon) => RegularUncommon,
        (CardOddsType.Regular, CardRarity.Common) => RegularCommon,
        (CardOddsType.Elite, CardRarity.Rare) => EliteRare,
        (CardOddsType.Elite, CardRarity.Uncommon) => EliteUncommon,
        (CardOddsType.Elite, CardRarity.Common) => EliteCommon,
        (CardOddsType.Boss, CardRarity.Rare) => 1f,
        (CardOddsType.Boss, CardRarity.Uncommon) => 0f,
        (CardOddsType.Boss, CardRarity.Common) => 0f,
        (CardOddsType.Shop, CardRarity.Rare) => ShopRare,
        (CardOddsType.Shop, CardRarity.Uncommon) => ShopUncommon,
        (CardOddsType.Shop, CardRarity.Common) => ShopCommon,
        (CardOddsType.Uniform, CardRarity.Rare or CardRarity.Uncommon or CardRarity.Common) => UniformEach,
        _ => throw new ArgumentOutOfRangeException(nameof(rarity), $"{type} has no base odds for {rarity}."),
    };

    /// <summary>
    /// The roll of a card reward: one number; Rare below the Rare odds plus the offset, Uncommon
    /// below that plus the Uncommon odds, else Common. Then the offset moves: back to its start
    /// after a Rare, up by <see cref="Growth"/> after anything else. A boss roll leaves the
    /// offset out of the roll and still moves it, so three boss cards reset it three times.
    /// </summary>
    public CardRarity Roll(GameRng rng, CardOddsType type)
    {
        var rarity = RollWithOffset(rng, type, type == CardOddsType.Boss ? 0f : Offset);
        Offset = rarity == CardRarity.Rare ? BaseOffset : Math.Min(Offset + Growth, MaxOffset);
        return rarity;
    }

    /// <summary>The same roll with the run's offset, which stays as it is (a shop's cards).</summary>
    public CardRarity RollWithoutChangingFutureOdds(GameRng rng, CardOddsType type) => RollWithOffset(rng, type, Offset);

    /// <summary>The same roll with an offset the caller names; the run's offset stays as it is.</summary>
    public static CardRarity RollWithOffset(GameRng rng, CardOddsType type, float offset)
    {
        var roll = rng.NextFloat();
        var rare = BaseOdds(type, CardRarity.Rare) + offset;
        if (roll < rare) return CardRarity.Rare;
        return roll < BaseOdds(type, CardRarity.Uncommon) + rare ? CardRarity.Uncommon : CardRarity.Common;
    }

    /// <summary>
    /// The roll some events and relics use, without any offset. As the game has it, the Uncommon
    /// test is against the Uncommon odds alone, not against Rare plus Uncommon: with the regular
    /// table that is Rare below 0.03, Uncommon below 0.37, else Common (3%, 34%, 63%).
    /// </summary>
    public static CardRarity RollWithBaseOdds(GameRng rng, CardOddsType type)
    {
        var roll = rng.NextFloat();
        if (roll < BaseOdds(type, CardRarity.Rare)) return CardRarity.Rare;
        return roll < BaseOdds(type, CardRarity.Uncommon) ? CardRarity.Uncommon : CardRarity.Common;
    }
}

/// <summary>
/// Whether a won fight drops a potion. The chance starts at 0.4 and moves by 0.1 after every
/// roll: down after a drop, up after none. An elite fight adds 0.125 to the chance of that roll.
/// </summary>
public sealed class PotionOdds
{
    public const float Base = 0.4f;
    public const float Step = 0.1f;

    /// <summary>The elite bonus as the game's constant has it; the roll adds half of it.</summary>
    public const float EliteBonus = 0.25f;

    /// <summary>The chance of the next roll: what a save writes as <c>potion_reward_odds_value</c>.</summary>
    public float Chance { get; private set; }

    public PotionOdds(float chance = Base) => Chance = chance;

    public PotionOdds Clone() => new(Chance);

    /// <summary>
    /// One roll. The number is drawn also when <paramref name="forced"/> says a relic makes the
    /// drop certain (White Beast Statue), and the chance goes down after a forced drop too.
    /// </summary>
    /// <param name="elite">The room is an Elite room; a Boss room has no bonus.</param>
    public bool Roll(GameRng rng, bool elite, bool forced = false)
    {
        var needed = Chance + (elite ? EliteBonus : 0f) * 0.5f;
        var roll = rng.NextFloat();
        if (forced || roll < needed)
        {
            Chance -= Step;
            return true;
        }
        Chance += Step;
        return false;
    }
}

/// <summary>What an Unknown room can turn out to be. The numbers are the game's room type numbers:
/// when Event is barred the lowest of the others is the fallback.</summary>
public enum RolledRoom { Monster = 1, Elite = 2, Treasure = 4, Shop = 5, Event = 6 }

/// <summary>
/// What an Unknown room becomes. One number against odds that grow: Monster starts at 0.10,
/// Treasure at 0.02, Shop at 0.03 and whatever is left is an Event. The kind that was rolled
/// goes back to its start, every other kind that was allowed in the roll grows by its start
/// value. Elite stands at -1, is never rolled, and sinks by 1 with every roll (a save shows
/// -2 after one room). All values go back to their start with a new act.
/// </summary>
public sealed class UnknownRoomOdds
{
    public const float BaseMonster = 0.1f;
    public const float BaseElite = -1f;
    public const float BaseTreasure = 0.02f;
    public const float BaseShop = 0.03f;

    /// <summary>Every kind, in the order the roll walks them; Event is what is left.</summary>
    public static readonly IReadOnlySet<RolledRoom> AllRooms =
        new HashSet<RolledRoom> { RolledRoom.Monster, RolledRoom.Elite, RolledRoom.Treasure, RolledRoom.Shop, RolledRoom.Event };

    private static readonly (RolledRoom Room, float Base)[] Bases =
        [(RolledRoom.Monster, BaseMonster), (RolledRoom.Elite, BaseElite), (RolledRoom.Treasure, BaseTreasure), (RolledRoom.Shop, BaseShop)];

    // In the order of Bases: Monster, Elite, Treasure, Shop.
    private readonly float[] _odds = [BaseMonster, BaseElite, BaseTreasure, BaseShop];

    public float Monster => _odds[0];
    public float Elite => _odds[1];
    public float Treasure => _odds[2];
    public float Shop => _odds[3];

    public UnknownRoomOdds() { }

    /// <summary>The odds as a save holds them.</summary>
    public UnknownRoomOdds(float monster, float elite, float treasure, float shop) => _odds = [monster, elite, treasure, shop];

    public UnknownRoomOdds Clone() => new(Monster, Elite, Treasure, Shop);

    /// <summary>The kinds a roll may give when nothing else bars one: all of them, without Shop when
    /// the room before was a shop or every next node is one.</summary>
    public static IReadOnlySet<RolledRoom> Allowed(bool shopBarred) => shopBarred ? WithoutShop : AllRooms;

    private static readonly IReadOnlySet<RolledRoom> WithoutShop =
        new HashSet<RolledRoom> { RolledRoom.Monster, RolledRoom.Elite, RolledRoom.Treasure, RolledRoom.Event };

    /// <summary>
    /// One roll. <paramref name="allowed"/> is what may come out: <see cref="Allowed"/>, after
    /// the relics that take kinds away had their say (Juzu Bracelet takes Monster). A kind that
    /// is not allowed is not in the roll and does not grow.
    /// <para>
    /// Left out: a profile's very first run, where the game gives Event, Event, Monster for the
    /// first three Unknown rooms without rolling; and the Deadly Events modifier, which doubles
    /// the growth of Treasure.
    /// </para>
    /// </summary>
    public RolledRoom Roll(GameRng rng, IReadOnlySet<RolledRoom> allowed)
    {
        var room = allowed.Contains(RolledRoom.Event) ? RolledRoom.Event : allowed.Min();
        var roll = rng.NextFloat();
        var sum = 0f;
        for (var i = 0; i < Bases.Length; i++)
        {
            if (!allowed.Contains(Bases[i].Room) || _odds[i] < 0f) continue;
            sum += _odds[i];
            if (roll <= sum)
            {
                room = Bases[i].Room;
                break;
            }
        }
        for (var i = 0; i < Bases.Length; i++)
        {
            if (room == Bases[i].Room) _odds[i] = Bases[i].Base;
            else if (allowed.Contains(Bases[i].Room)) _odds[i] += Bases[i].Base;
        }
        return room;
    }

    /// <summary>A new act: every kind back to its start value.</summary>
    public void ResetToBase()
    {
        for (var i = 0; i < Bases.Length; i++) _odds[i] = Bases[i].Base;
    }
}
