using System.Globalization;

namespace SpireMind.Arena;

// The first relics of run mode (chunk 12 of the run-mode plan): 25 of the 122 relics of an
// Ironclad's bags, spread over the four bag rarities so that an elite, a chest, a shop's three
// slots and a random-relic event each have something to land on. Each class follows the game
// class of the same name (MegaCrit.Sts2.Core.Models.Relics, v0.107.1); what it is written
// against is Sim/Run/RELICS.md.
//
// Common:   Anchor, Vajra, Bronze Scales, Oddly Smooth Stone, Bag of Preparation, Lantern, Strawberry
// Uncommon: Akabeko, Candelabra, Mercury Hourglass, Horn Cleat, Petrified Toad, Pear
// Rare:     Bellows, Captain's Wheel, Chandelier, Mango, Game Piece, Ice Cream, Tungsten Rod
// Shop:     Bread, Sling of Courage, Brimstone, Lee's Waffle, The Abacus
//
// None of them keeps anything between rooms: a relic that says "turn 2" reads the fight's own
// turn number (the game's PlayerCombatState.TurnNumber, which is Fight.Turn here: both start at
// 1 and go up when the side switches back to the player, before any hook of the new turn).
//
// Block and damage from a relic are unpowered (the game's ValueProp.Unpowered alone): Block is
// not changed by Dexterity or Frail, damage not by Strength, Weak or Vulnerable, and Block
// absorbs the damage.

/// <summary>Registers the 25 relics, the power Vigor (Akabeko) and the potion Potion-Shaped Rock (Petrified Toad).</summary>
public sealed class Relics1 : IContentModule
{
    /// <summary>The class names of this file's relics by rarity, in the plan's order.</summary>
    public static readonly IReadOnlyList<string> Common =
        ["Anchor", "Vajra", "BronzeScales", "OddlySmoothStone", "BagOfPreparation", "Lantern", "Strawberry"];

    public static readonly IReadOnlyList<string> Uncommon =
        ["Akabeko", "Candelabra", "MercuryHourglass", "HornCleat", "PetrifiedToad", "Pear"];

    public static readonly IReadOnlyList<string> Rare =
        ["Bellows", "CaptainsWheel", "Chandelier", "Mango", "GamePiece", "IceCream", "TungstenRod"];

    public static readonly IReadOnlyList<string> Shop =
        ["Bread", "SlingOfCourage", "Brimstone", "LeesWaffle", "TheAbacus"];

    /// <summary>All 25, Common first.</summary>
    public static readonly IReadOnlyList<string> All = [.. Common, .. Uncommon, .. Rare, .. Shop];

    /// <summary>The class name of the potion Petrified Toad makes. It is the game's one Token-rarity
    /// potion: in no pool that rewards, shops or random generation read.</summary>
    public const string PotionShapedRock = "PotionShapedRock";

    /// <summary>What a Potion-Shaped Rock deals.</summary>
    public const int RockDamage = 15;

    public void Register(Registry r)
    {
        r.Power<VigorPower>();

        // The game's PotionShapedRock: 15 unpowered damage to the chosen enemy, the user as dealer.
        r.Potion(new PotionDef(PotionShapedRock, "Potion-Shaped Rock", PotionRarity.Token, TargetType.AnyEnemy)
        {
            Text = _ => string.Create(CultureInfo.InvariantCulture, $"Deal {RockDamage} damage to an enemy. Strength, Weak and Vulnerable do not change it."),
            Use = p => p.F.Damage(p.Target!, RockDamage, Props.NonCardUnpowered, p.Owner),
        });

        r.Relic<Anchor>();
        r.Relic<Vajra>();
        r.Relic<BronzeScales>();
        r.Relic<OddlySmoothStone>();
        r.Relic<BagOfPreparation>();
        r.Relic<Lantern>();
        r.Relic<Strawberry>();

        r.Relic<Akabeko>();
        r.Relic<Candelabra>();
        r.Relic<MercuryHourglass>();
        r.Relic<HornCleat>();
        r.Relic<PetrifiedToad>();
        r.Relic<Pear>();

        r.Relic<Bellows>();
        r.Relic<CaptainsWheel>();
        r.Relic<Chandelier>();
        r.Relic<Mango>();
        r.Relic<GamePiece>();
        r.Relic<IceCream>();
        r.Relic<TungstenRod>();

        r.Relic<Bread>();
        r.Relic<SlingOfCourage>();
        r.Relic<Brimstone>();
        r.Relic<LeesWaffle>();
        r.Relic<TheAbacus>();
    }
}

// ---------- Common ----------

/// <summary>
/// Anchor: 10 unpowered Block in "before combat start", the first hook after the fight is in
/// progress (Block cannot be gained before that). The player's Block is not cleared on turn 1,
/// so it stands through the first enemy turn.
/// </summary>
public sealed class Anchor : Relic
{
    public const int Block = 10;

    public override string Name => "Anchor";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"At the start of every fight: gain {Block} Block. Dexterity and Frail do not change it.");

    public override void BeforeCombatStart(Fight f) => f.GainBlock(f.Player, Block, Props.NonCardUnpowered);
}

/// <summary>Vajra: 1 Strength when a fight's room is entered (any combat room: hallway, elite, boss, an event's fight), the player as applier.</summary>
public sealed class Vajra : Relic
{
    public const int Strength = 1;

    public override string Name => "Vajra";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"At the start of every fight: gain {Strength} Strength.");

    public override void AfterRoomEntered(RunContext c, RoomKind room)
    {
        if (c.Fight is { } f && room.IsCombat()) f.ApplyPower<StrengthPower>(f.Player, Strength, f.Player);
    }
}

/// <summary>Bronze Scales: 3 Thorns when a fight's room is entered, the player as applier.</summary>
public sealed class BronzeScales : Relic
{
    public const int Thorns = 3;

    public override string Name => "Bronze Scales";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"At the start of every fight: gain {Thorns} Thorns (whenever an attack hits you, the attacker takes {Thorns} damage first).");

    public override void AfterRoomEntered(RunContext c, RoomKind room)
    {
        if (c.Fight is { } f && room.IsCombat()) f.ApplyPower("ThornsPower", f.Player, Thorns, f.Player);
    }
}

/// <summary>Oddly Smooth Stone: 1 Dexterity when a fight's room is entered, the player as applier.</summary>
public sealed class OddlySmoothStone : Relic
{
    public const int Dexterity = 1;

    public override string Name => "Oddly Smooth Stone";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"At the start of every fight: gain {Dexterity} Dexterity.");

    public override void AfterRoomEntered(RunContext c, RoomKind room)
    {
        if (c.Fight is { } f && room.IsCombat()) f.ApplyPower<DexterityPower>(f.Player, Dexterity, f.Player);
    }
}

/// <summary>Bag of Preparation: the turn-start draw of turn 1 is 2 cards larger (the first pass of the hand-draw modifiers).</summary>
public sealed class BagOfPreparation : Relic
{
    public const int Cards = 2;

    public override string Name => "Bag of Preparation";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"On the first turn of every fight: draw {Cards} more cards.");

    public override decimal ModifyHandDraw(Fight f, decimal count) => f.Turn > 1 ? count : count + Cards;
}

/// <summary>Lantern: 1 energy in "after side turn start" of the player's turn 1, which is after the hand
/// draw. It is a gain, so the energy-gain modifiers see it.</summary>
public sealed class Lantern : Relic
{
    public const int Energy = 1;

    public override string Name => "Lantern";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"On the first turn of every fight: gain {Energy} energy.");

    public override void AfterSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(f.Player) && f.Turn <= 1) f.GainEnergy(Energy);
    }
}

/// <summary>Strawberry: 7 max HP at pickup (which also heals 7).</summary>
public sealed class Strawberry : Relic
{
    public const int MaxHp = 7;

    public override string Name => "Strawberry";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: gain {MaxHp} max HP.");

    public override void AfterObtained(RunContext c) => c.GainMaxHp(MaxHp);
}

// ---------- Uncommon ----------

/// <summary>Akabeko: 8 Vigor in "after side turn start" of the player's turn 1, the player as applier.</summary>
public sealed class Akabeko : Relic
{
    public const int Vigor = 8;

    public override string Name => "Akabeko";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"On the first turn of every fight: gain {Vigor} Vigor (your next attack deals {Vigor} more damage per hit).");

    public override void AfterSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(f.Player) && f.Turn <= 1) f.ApplyPower<VigorPower>(f.Player, Vigor, f.Player);
    }
}

/// <summary>Candelabra: 2 energy in "after side turn start" of the player's turn 2.</summary>
public sealed class Candelabra : Relic
{
    public const int Energy = 2;
    public const int Turn = 2;

    public override string Name => "Candelabra";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"On turn {Turn} of every fight: gain {Energy} energy.");

    public override void AfterSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(f.Player) && f.Turn == Turn) f.GainEnergy(Energy);
    }
}

/// <summary>
/// Mercury Hourglass: in "after player turn start" (right after the hand draw, every turn, the
/// first too) 3 unpowered damage to every enemy that can be hit, as ONE damage instance with the
/// player as dealer: all of them lose Block and HP first, then the reactions run, then the deaths.
/// </summary>
public sealed class MercuryHourglass : Relic
{
    public const int Damage = 3;

    public override string Name => "Mercury Hourglass";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"At the start of each of your turns: deal {Damage} damage to ALL enemies. Strength, Weak and Vulnerable do not change it.");

    public override void AfterPlayerTurnStart(Fight f) => f.Damage(f.HittableEnemies, Damage, Props.NonCardUnpowered, f.Player);
}

/// <summary>Horn Cleat: 14 unpowered Block in "after block cleared" of the player's turn 2, so after last turn's Block fell.</summary>
public sealed class HornCleat : Relic
{
    public const int Block = 14;
    public const int Turn = 2;

    public override string Name => "Horn Cleat";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"At the start of turn {Turn} of every fight: gain {Block} Block. Dexterity and Frail do not change it.");

    public override void AfterBlockCleared(Fight f, Creature creature)
    {
        if (ReferenceEquals(creature, f.Player) && f.Turn == Turn) f.GainBlock(f.Player, Block, Props.NonCardUnpowered);
    }
}

/// <summary>
/// Petrified Toad: in the late pass of "before combat start" the player is handed a Potion-Shaped
/// Rock, into the first free belt slot; with a full belt nothing happens (the game's
/// PotionCmd.TryToProcure).
/// </summary>
public sealed class PetrifiedToad : Relic
{
    public override string Name => "Petrified Toad";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"At the start of every fight: gain a Potion-Shaped Rock (a potion: deal {Relics1.RockDamage} damage to an enemy), if a potion slot is free.");

    public override void BeforeCombatStartLate(Fight f) => f.GainPotion(Content.Potion(Relics1.PotionShapedRock));
}

/// <summary>Pear: 10 max HP at pickup.</summary>
public sealed class Pear : Relic
{
    public const int MaxHp = 10;

    public override string Name => "Pear";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: gain {MaxHp} max HP.");

    public override void AfterObtained(RunContext c) => c.GainMaxHp(MaxHp);
}

// ---------- Rare ----------

/// <summary>
/// Bellows: in "after player turn start" of turn 1, right after the hand draw, every card in
/// hand that can be upgraded is upgraded for the rest of the fight (the deck cards are not).
/// </summary>
public sealed class Bellows : Relic
{
    public override string Name => "Bellows";

    public override string Describe() => "On the first turn of every fight: the cards you drew are upgraded for that fight.";

    public override void AfterPlayerTurnStart(Fight f)
    {
        if (f.Turn > 1) return;
        foreach (var card in f.Hand.ToList()) f.Upgrade(card);
    }
}

/// <summary>Captain's Wheel: 18 unpowered Block in "after block cleared" of the player's turn 3.</summary>
public sealed class CaptainsWheel : Relic
{
    public const int Block = 18;
    public const int Turn = 3;

    public override string Name => "Captain's Wheel";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"At the start of turn {Turn} of every fight: gain {Block} Block. Dexterity and Frail do not change it.");

    public override void AfterBlockCleared(Fight f, Creature creature)
    {
        if (ReferenceEquals(creature, f.Player) && f.Turn == Turn) f.GainBlock(f.Player, Block, Props.NonCardUnpowered);
    }
}

/// <summary>Chandelier: 3 energy in "after side turn start" of the player's turn 3.</summary>
public sealed class Chandelier : Relic
{
    public const int Energy = 3;
    public const int Turn = 3;

    public override string Name => "Chandelier";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"On turn {Turn} of every fight: gain {Energy} energy.");

    public override void AfterSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(f.Player) && f.Turn == Turn) f.GainEnergy(Energy);
    }
}

/// <summary>Mango: 14 max HP at pickup.</summary>
public sealed class Mango : Relic
{
    public const int MaxHp = 14;

    public override string Name => "Mango";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: gain {MaxHp} max HP.");

    public override void AfterObtained(RunContext c) => c.GainMaxHp(MaxHp);
}

/// <summary>
/// Game Piece: after each run of a Power card's effect, draw 1 card. The game tests that the
/// fight is in progress (the hook is also sent after the card that ends the fight); the draw
/// itself does nothing once the fight is ending.
/// </summary>
public sealed class GamePiece : Relic
{
    public const int Cards = 1;

    public override string Name => "Game Piece";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"Whenever you play a Power card: draw {TextContext.Count(Cards, "card")}.");

    public override void AfterCardPlayed(Fight f, CardPlay play)
    {
        if (f.InProgress && play.Card.Type == CardType.Power) f.Draw(Cards);
    }
}

/// <summary>Ice Cream: from turn 2 on energy is not set to the maximum at turn start; the maximum is added to what is left.</summary>
public sealed class IceCream : Relic
{
    public override string Name => "Ice Cream";

    public override string Describe() => "Energy you do not spend is kept for your next turn.";

    public override bool ShouldResetEnergy(Fight f) => f.Turn == 1;
}

/// <summary>
/// Tungsten Rod: HP the player is about to lose is 1 less, never below 0 (the first pass of the
/// HP-loss modifiers after Block). The game's class tests nothing but whose HP it is, so it
/// also takes 1 off an HP loss outside a fight (an event's).
/// </summary>
public sealed class TungstenRod : Relic
{
    public const int Reduction = 1;

    public override string Name => "Tungsten Rod";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"Whenever you would lose HP, lose {Reduction} less.");

    public override decimal ModifyHpLostAfterOsty(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) =>
        ReferenceEquals(target, f.Player) ? Math.Max(0m, amount - Reduction) : amount;
}

// ---------- Shop ----------

/// <summary>
/// Bread: the maximum energy is 1 higher except on turn 1 of a fight, and in "after side turn
/// start" of turn 1 the player loses 2 energy. So turn 1 has 1 energy (of 3) and every later
/// turn 4.
/// </summary>
public sealed class Bread : Relic
{
    public const int GainEnergy = 1;
    public const int LoseEnergy = 2;

    public override string Name => "Bread";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"The first turn of every fight starts with {LoseEnergy} less energy. From the second turn on you have {GainEnergy} more energy each turn.");

    // The game leaves the maximum alone only "in a fight, on turn 1"; with no fight on it adds the 1.
    public override decimal ModifyMaxEnergy(Fight f, decimal amount) => f.IsCombat && f.Turn == 1 ? amount : amount + GainEnergy;

    public override void AfterSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(f.Player) && f.Turn == 1) f.LoseEnergy(LoseEnergy);
    }
}

/// <summary>Sling of Courage: 2 Strength when an Elite room is entered, the player as applier.</summary>
public sealed class SlingOfCourage : Relic
{
    public const int Strength = 2;

    public override string Name => "Sling of Courage";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"At the start of every Elite fight: gain {Strength} Strength.");

    public override void AfterRoomEntered(RunContext c, RoomKind room)
    {
        if (c.Fight is { } f && room == RoomKind.Elite) f.ApplyPower<StrengthPower>(f.Player, Strength, f.Player);
    }
}

/// <summary>
/// Brimstone (the Ironclad's own Shop relic): in "after side turn start" of every player turn the
/// player gains 2 Strength (itself as applier), then every living enemy gains 1 Strength, one
/// after the other in list order, with nobody as applier.
/// </summary>
public sealed class Brimstone : Relic
{
    public const int SelfStrength = 2;
    public const int EnemyStrength = 1;

    public override string Name => "Brimstone";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"At the start of each of your turns: gain {SelfStrength} Strength, and every enemy gains {EnemyStrength} Strength.");

    public override void AfterSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (!participants.Contains(f.Player)) return;
        f.ApplyPower<StrengthPower>(f.Player, SelfStrength, f.Player);
        // "Alive" is asked enemy by enemy as its turn comes, as the game's lazy filter does.
        foreach (var enemy in f.OpponentsOf(f.Player))
            if (enemy.IsAlive) f.ApplyPower<StrengthPower>(enemy, EnemyStrength, null);
    }
}

/// <summary>Lee's Waffle: 7 max HP at pickup, then a heal of everything that is missing.</summary>
public sealed class LeesWaffle : Relic
{
    public const int MaxHp = 7;

    public override string Name => "Lee's Waffle";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: gain {MaxHp} max HP and heal to full HP.");

    public override void AfterObtained(RunContext c)
    {
        c.GainMaxHp(MaxHp);
        c.Heal(c.MaxHp - c.Hp);
    }
}

/// <summary>The Abacus: 6 unpowered Block after every shuffle of the discard pile into the draw pile (not the
/// deck's shuffle at the start of a fight, which is no such shuffle).</summary>
public sealed class TheAbacus : Relic
{
    public const int Block = 6;

    public override string Name => "The Abacus";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"Whenever your discard pile is shuffled into your draw pile: gain {Block} Block. Dexterity and Frail do not change it.");

    public override void AfterShuffle(Fight f) => f.GainBlock(f.Player, Block, Props.NonCardUnpowered);
}

// ---------- the power Akabeko brings ----------

/// <summary>
/// Vigor (the game's VigorPower): its amount is added to every hit of ONE powered attack of its
/// owner, then that much is taken off again.
/// <list type="bullet">
/// <item>The first powered attack command of the owner claims it when it starts, and notes the
/// amount at that moment. An attack that is no attack command (plain damage with a dealer)
/// gets the bonus and claims nothing.</item>
/// <item>While an attack holds the claim, a hit that comes from another card gets nothing; a hit
/// from the claiming attack's own card, or from no card, gets the amount.</item>
/// <item>When the claiming attack is over, the amount noted at its start is taken off (so Vigor
/// gained during the attack stays). The game never forgets the claim: what is left of that
/// instance afterwards is given only to hits from the same card or from no card, and is never
/// used up. A fresh Vigor after the power went to 0 is a new instance with no claim.</item>
/// <item>"After attack" does not reach anybody once the fight is ending, so the attack that
/// wins the fight uses nothing up.</item>
/// </list>
/// </summary>
public sealed class VigorPower : Power
{
    /// <summary>The attack that claimed the Vigor, by its id; 0 while none has.</summary>
    private int _attackId;
    private CardInstance? _attackCard;
    private int _amountWhenAttackStarted;

    public override string Name => "Vigor";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"The next attack deals {Amount} more damage per hit.");

    public override void BeforeAttack(Fight f, AttackCommand attack)
    {
        if (!ReferenceEquals(attack.Attacker, Owner) || !attack.Props.IsPoweredAttack() || _attackId != 0) return;
        // (The game also passes over an attack whose source is a model that is no card. An attack's
        // source is a card or nothing, here as there.)
        _attackId = attack.Id;
        _attackCard = attack.CardSource;
        _amountWhenAttackStarted = Amount;
    }

    public override decimal ModifyDamageAdditive(Fight f, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(Owner, dealer) || !props.IsPoweredAttack()) return 0m;
        if (_attackId != 0 && cardSource is not null && !ReferenceEquals(cardSource, _attackCard)) return 0m;
        // (The game's last test, "the claiming attack's attacker is the dealer", always holds: only
        // an attack of the owner can claim, and the dealer is the owner here.)
        return Amount;
    }

    public override void AfterAttack(Fight f, AttackCommand attack)
    {
        if (_attackId != 0 && attack.Id == _attackId) f.ModifyPowerAmount(this, -_amountWhenAttackStarted, null);
    }
}
