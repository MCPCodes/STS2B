namespace SpireMind.Arena;

/// <summary>
/// The kind of room a run can be in, under the game's own names and numbers (its RoomType).
/// The engine's <see cref="RoomType"/> names only the three kinds of fight and
/// <see cref="RolledRoom"/> only what an Unknown map point can become; this one has them all,
/// for the hooks that are told about a room.
/// </summary>
public enum RoomKind
{
    Unassigned = 0,
    Monster = 1,
    Elite = 2,
    Boss = 3,
    Treasure = 4,
    Shop = 5,
    Event = 6,
    RestSite = 7,
    Map = 8,
}

public static class RoomKinds
{
    /// <summary>A fight's room (the game's "room is CombatRoom"): Monster, Elite or Boss. A fight an
    /// event starts is one too; its kind is the encounter's.</summary>
    public static bool IsCombat(this RoomKind kind) => kind is RoomKind.Monster or RoomKind.Elite or RoomKind.Boss;

    public static RoomKind Of(RoomType fight) => fight switch
    {
        RoomType.Elite => RoomKind.Elite,
        RoomType.Boss => RoomKind.Boss,
        _ => RoomKind.Monster,
    };

    /// <summary>What an Unknown map point became. The numbers are the game's in both enums.</summary>
    public static RoomKind Of(RolledRoom rolled) => (RoomKind)(int)rolled;
}

/// <summary>
/// Put on the hook class of a card (its <see cref="CardBehaviour"/>) whose DECK card hears hooks
/// too, as every deck card does in the game: Guilty counts fights as the card in the deck, not
/// as its copy in a pile. Such a card's hook object is then asked first, before the fight's own
/// listeners, and it hears the run's hooks outside fights. The same class serves the deck card
/// and its combat copy; tell them apart by <c>Card.Pile == PileType.Deck</c>, as the game's
/// cards do.
/// <para>
/// The price of the opt-in: the class may override only hooks that the game sends to deck cards
/// (the run's hooks, and the fight hooks listed in <see cref="DeckListening.HeardByDeckCards"/>).
/// The arena asks a listening deck card on every dispatch of a fight, also on those the game
/// keeps to the fight's own listeners (a card played, a turn starting), so a class that
/// overrode one of those would be heard once too often. <see cref="DeckListening.Problems"/>
/// names such a class and the self-check fails on it.
/// </para>
/// </summary>
public interface IListensInDeck;

/// <summary>
/// A screen that holds card rewards the player has not taken yet hears of every relic the
/// player obtains outside a fight, at once and before the relic's own pickup effect, as the
/// game's card reward does (CardReward.OnRelicObtained): taking Molten Egg from the relic row
/// upgrades the Attacks of the card row beside it. The run asks every open screen, the one
/// opened first first.
/// </summary>
public interface IHearsRelicObtained
{
    void OnRelicObtained(Run run, Relic relic);
}

/// <summary>
/// What a hook of the run is handed: where the player is right now, and the commands that work
/// in either place. Outside a fight <see cref="Run"/> is there and <see cref="Fight"/> is null;
/// inside one it is the other way round (a fight works on its own copy of the player and does
/// not know its run). In the game the player is one object and a relic need not ask; here it
/// reads and acts through this, and one hook body serves both places:
/// <code>
/// public override void AfterGoldGained(RunContext c) => c.GainMaxHp(1);                       // Dragon Fruit
/// public override void AfterRoomEntered(RunContext c, RoomKind room)                          // Vajra
/// {
///     if (c.Fight is { } f &amp;&amp; room.IsCombat()) f.ApplyPower&lt;StrengthPower&gt;(f.Player, 1, f.Player);
/// }
/// </code>
/// </summary>
public sealed class RunContext
{
    internal RunContext(Run run) => Run = run;

    internal RunContext(Fight fight) => Fight = fight;

    /// <summary>The run, when the hook is heard outside a fight: its state, its world, its screens, <see cref="Arena.Run.Ask"/>.</summary>
    public Run? Run { get; }

    /// <summary>The fight, when the hook is heard inside one.</summary>
    public Fight? Fight { get; }

    public bool InFight => Fight is not null;

    /// <summary>The run, for a hook that cannot do its work without it (a pickup that draws on the run's
    /// streams, pulls from a bag or opens a screen).</summary>
    /// <exception cref="NotSupportedException">The hook was heard inside a fight.</exception>
    public Run NeedRun(string what) => Run ?? throw new NotSupportedException(
        $"The arena leaves out {what} inside a fight: a fight works on its own copy of the player and cannot reach the run's streams, bags or screens.");

    // ---------- the player, wherever it is ----------

    public int Hp => Fight?.Player.Hp ?? Run!.State.Hp;
    public int MaxHp => Fight?.Player.MaxHp ?? Run!.State.MaxHp;
    public int Gold => Fight?.Gold ?? Run!.State.Gold;
    public bool IsDead => Hp <= 0;

    /// <summary>The relics in pickup order.</summary>
    public IReadOnlyList<Relic> Relics => Fight is { } f ? f.Relics : Run!.State.Relics;

    /// <summary>The deck in deck order (in a fight: the deck cards, not the cards in the piles).</summary>
    public IReadOnlyList<CardInstance> Deck => Fight is { } f ? f.DeckCards : Run!.State.Deck;

    /// <summary>The belt slot by slot, null for an empty slot.</summary>
    public IReadOnlyList<PotionDef?> Potions => Fight is { } f ? f.Potions : Run!.State.Belt.Slots;

    /// <summary>The game's total floor.</summary>
    public int TotalFloor => Fight?.Floor ?? Run!.State.TotalFloor;

    // ---------- commands that work in either place ----------

    /// <summary>Heals up to max HP (CreatureCmd.Heal).</summary>
    public void Heal(decimal amount)
    {
        if (Fight is { } f) f.Heal(f.Player, amount);
        else Run!.Heal(amount);
    }

    /// <summary>Raises max HP and heals by what it rose (CreatureCmd.GainMaxHp).</summary>
    public void GainMaxHp(decimal amount)
    {
        if (Fight is { } f) f.GainMaxHp(f.Player, amount);
        else Run!.GainMaxHp(amount);
    }

    /// <summary>Lowers max HP, never below 1; HP above the new maximum is lost as damage that ignores Block (CreatureCmd.LoseMaxHp).</summary>
    public void LoseMaxHp(decimal amount, bool isFromCard = false)
    {
        if (Fight is { } f) f.LoseMaxHp(f.Player, amount, isFromCard);
        else Run!.LoseMaxHp(amount, isFromCard);
    }

    /// <summary>Damage to the player with no dealer and no card (CreatureCmd.Damage); "lose N HP" is
    /// <see cref="Props.NonCardHpLoss"/>. Returns the HP lost.</summary>
    public int Damage(decimal amount, ValueProp props = Props.NonCardHpLoss) =>
        Fight is { } f ? f.Damage(f.Player, amount, props, null).UnblockedDamage : Run!.Damage(amount, props);

    /// <summary>Gives gold through the gold hooks (PlayerCmd.GainGold). Returns what was paid.</summary>
    public int GainGold(decimal amount) => RunHooks.GainGold(this, amount);

    /// <summary>Takes gold, never below 0 (PlayerCmd.LoseGold). No hook hears it.</summary>
    public void LoseGold(decimal amount) => SetGold(Math.Max(0, Gold - (int)amount));

    /// <summary>Gives the player a relic and runs its pickup effect (RelicCmd.Obtain).</summary>
    public Relic ObtainRelic(Relic relic, int index = -1) =>
        Fight is { } f ? f.ObtainRelic(relic, index) : Run!.ObtainRelic(relic, index);

    /// <summary>Takes a relic away (RelicCmd.Remove).</summary>
    public void RemoveRelic(Relic relic)
    {
        if (Fight is { } f) f.RemoveRelic(relic);
        else Run!.RemoveRelic(relic);
    }

    /// <summary>Puts <paramref name="replacement"/> where <paramref name="original"/> was in the relic list (RelicCmd.Replace).</summary>
    public Relic ReplaceRelic(Relic original, Relic replacement) =>
        Fight is { } f ? f.ReplaceRelic(original, replacement) : Run!.ReplaceRelic(original, replacement);

    // ---------- for the dispatchers ----------

    internal void SetGold(int gold)
    {
        if (Fight is { } f) f.Gold = gold;
        else Run!.State.Gold = Math.Max(0, gold);
    }

    /// <summary>
    /// Calls <paramref name="call"/> on everyone who hears a hook of the run, in the game's order.
    /// <paramref name="withFight"/> false is the game's IterateHookListeners(null): listening deck
    /// cards in deck order, relics in pickup order, potions in slot order, whether or not a fight
    /// is on. True adds the fight's own listeners after the deck cards when the hook is heard
    /// inside a fight (the game's IterateHookListeners(combatState)): the player's powers, the
    /// relics, the potions, the cards in the piles, then each enemy's powers and the monster.
    /// </summary>
    internal void Each(Action<Listener> call, bool withFight)
    {
        if (Fight is { } f)
        {
            if (withFight) f.Each(call, guarded: false);
            else f.EachOfRun(call);
        }
        else
        {
            Run!.EachListener(call);
        }
    }
}
