using System.Globalization;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// One screen of a run, as the mod's readers show it: the map, an event, the rewards, a card
/// choice, the shop, a fight. A screen builds its snapshot (keys, their order, labels and
/// refusals are the mod's: docs/screens.md) and runs its action ids. The run keeps the open
/// screens in a stack and shows the top one.
/// <para>
/// A screen is copied with the run, so it keeps what it holds in its own fields and never
/// keeps a <see cref="Run"/>: every method is handed the run.
/// </para>
/// </summary>
public abstract class RunScreen
{
    /// <summary>The snapshot's <c>screen</c> ("map", "rewards", "game_over").</summary>
    public abstract string Name { get; }

    /// <summary>
    /// True for a screen that copies itself before every action and puts itself back when a rule
    /// throws (a fight). The run then takes no copy of its own for the screen's actions.
    /// </summary>
    public virtual bool CopiesItself => false;

    /// <summary>The snapshot as it stands: the state, and every action that may be taken now.</summary>
    public abstract GameSnapshot Snapshot(Run run);

    /// <summary>The actions of <see cref="Snapshot"/>, for callers that need nothing else.</summary>
    public virtual IReadOnlyList<LegalAction> LegalActions(Run run) => Snapshot(run).Actions;

    /// <summary>
    /// Runs one action id. An id that is not offered now is refused with the mod's own wording
    /// and changes nothing. Whatever the action does, it does to the run it is handed.
    /// </summary>
    public abstract ActionResult Execute(Run run, string actionId);

    /// <summary>
    /// A deep copy for a copy of the run. <paramref name="map"/> gives the copy's deck card or
    /// relic for one of this run's (<see cref="CloneMap.Get{T}"/>), for a screen that points at any.
    /// </summary>
    public abstract RunScreen Clone(CloneMap map);
}

/// <summary>
/// The game over screen after a lost fight, in the shape of the mod's reader (docs/screens.md,
/// "Game over"), so that a model's own post-mortem step sees what it sees in the game. The
/// game's <c>score</c> is left out: the arena does not work it out. The one action is the
/// mod's <c>continue</c>; where the game would go on to its run summary and the menu, the arena
/// goes to its end snapshot. Showing it at all is a house rule: the run is already over.
/// </summary>
public sealed class GameOverScreen : RunScreen
{
    private readonly string _room;

    /// <param name="room">The kind of room the run ended in, as the mod prints it ("Monster", "Elite", "Boss").</param>
    public GameOverScreen(string room) => _room = room;

    public override string Name => "game_over";

    public override GameSnapshot Snapshot(Run run) => new()
    {
        Screen = Name,
        AwaitingDecision = true,
        State = new JsonObject
        {
            ["result"] = "defeat",
            ["run"] = run.RunJson(),
            ["room"] = _room,
            ["player"] = run.PlayerJson(),
        },
        Actions = [new LegalAction("continue", "Continue to the next page of the game over screen")],
    };

    public override ActionResult Execute(Run run, string actionId)
    {
        if (actionId != "continue") return ActionResult.Fail($"Unknown game over action '{actionId}'.");
        run.ShowEnd();
        return ActionResult.Success;
    }

    public override RunScreen Clone(CloneMap map) => new GameOverScreen(_room);
}

/// <summary>
/// The end of a run: the arena's own snapshot, shaped like the end of a single fight. It is a
/// decision with nothing left to do, not a busy snapshot (told to wait, a model would wait on
/// a finished run for ever): screen <c>arena_over</c>, one action <c>done</c> that runs and
/// changes nothing. <c>state</c> holds <c>result</c> (one of <see cref="RunResult"/>), the
/// <c>run</c> block with the floor reached, <c>me</c> with HP and max HP, a one-line
/// <c>summary</c> and, when the result needs one, a <c>reason</c>.
/// </summary>
public sealed class ArenaOverScreen : RunScreen
{
    public override string Name => Run.OverScreen;

    public override GameSnapshot Snapshot(Run run)
    {
        var state = new JsonObject
        {
            ["result"] = run.Result,
            ["run"] = run.RunJson(),
            ["me"] = new JsonObject { ["name"] = run.State.Character, ["hp"] = run.State.Hp, ["max_hp"] = run.State.MaxHp },
            ["summary"] = run.Summary(),
        };
        if (run.ResultReason is { } reason) state["reason"] = reason;
        return new GameSnapshot
        {
            Screen = Name, AwaitingDecision = true, State = state,
            Actions = [new LegalAction(Run.DoneAction, Run.OverLabel(run.Result))],
        };
    }

    public override ActionResult Execute(Run run, string actionId) =>
        actionId == Run.DoneAction
            ? ActionResult.Success
            : ActionResult.Fail($"The run is over; '{actionId}' cannot run. The only action left is '{Run.DoneAction}'.");

    public override RunScreen Clone(CloneMap map) => new ArenaOverScreen();
}

/// <summary>Which keys a screen's <c>player</c> block carries (docs/screens.md, "player": each screen moves some of them elsewhere).</summary>
[Flags]
public enum PlayerKeys
{
    /// <summary><c>character</c> and <c>relics</c> only: they are always there.</summary>
    None = 0,

    /// <summary><c>hp</c> and <c>max_hp</c>.</summary>
    Hp = 1,
    Gold = 2,

    /// <summary><c>potions</c>: one name or null per slot.</summary>
    Potions = 4,

    /// <summary><c>deck_size</c> and <c>deck</c>.</summary>
    Deck = 8,

    /// <summary><c>deck_size</c> alone, without the deck's name counts (nothing more when <see cref="Deck"/> is set too).</summary>
    DeckSize = 16,

    /// <summary>The full form, as on <c>card_reward</c> and <c>game_over</c>, and on a <c>card_select</c> outside a
    /// fight whose grid does not list the whole deck.</summary>
    Full = Hp | Gold | Potions | Deck,

    /// <summary>As on a <c>card_select</c> outside a fight whose grid lists every card of the deck (a removal or an
    /// upgrade early in a run): the full form with the deck left out and <c>deck_size</c> at the end
    /// (CardSelectReader.AddContext).</summary>
    FullWithoutDeck = Hp | Gold | Potions | DeckSize,

    /// <summary>As on <c>map</c>, <c>rewards</c>, <c>treasure</c> and <c>event</c>: the potions are a block of their own.</summary>
    NoPotions = Hp | Gold | Deck,
}

/// <summary>
/// Cards, relics and decks as the mod's Describe writes them for the lists outside a fight.
/// What depends on the run as it stands (the run and player blocks, the potions, get_deck) is
/// on <see cref="Run"/>.
/// </summary>
public static class RunDescribe
{
    /// <summary>A card for a list outside a fight (Describe.Card with the deck's text): name, cost, type, text.</summary>
    public static JsonObject Card(CardInstance card) => Fight.DescribeCard(null, card, PileType.Deck);

    /// <summary>The same with <c>rarity</c>, for rewards, shops, deck lists and get_deck (Describe.CardOffer).</summary>
    public static JsonObject CardOffer(CardInstance card)
    {
        var json = Card(card);
        json["rarity"] = card.Def.Rarity.ToString();
        return json;
    }

    /// <summary>A relic with what it does (Describe.Relic).</summary>
    public static JsonObject Relic(Relic relic) => new() { ["name"] = relic.Name, ["text"] = relic.Describe() };

    /// <summary>A potion with what it does (Describe.Potion): name, text, target. The text is the one outside a fight.</summary>
    public static JsonObject Potion(PotionDef potion) => new() { ["name"] = potion.Name, ["text"] = potion.Text(null), ["target"] = potion.Target.ToString() };

    /// <summary>Card name to count, names in ordinal order, an upgraded card under its own name (Describe.DeckCounts).</summary>
    public static JsonObject DeckCounts(IEnumerable<CardInstance> cards)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var card in cards) counts[card.Title] = counts.TryGetValue(card.Title, out var n) ? n + 1 : 1;
        var json = new JsonObject();
        foreach (var (name, n) in counts) json[name] = n;
        return json;
    }
}

// The blocks every run screen shares, written as the mod's Describe writes them: run, player,
// the detailed potions, a card on offer, a relic, and get_deck. They show the player as it is
// now: during a fight that is the fight's own copy, not the state the fight began with.
public sealed partial class Run
{
    /// <summary>HP now: the fight's while one is on.</summary>
    public int Hp => Fight?.Player.Hp ?? _c.State.Hp;

    public int MaxHp => Fight?.Player.MaxHp ?? _c.State.MaxHp;

    public int Gold => Fight?.Gold ?? _c.State.Gold;

    /// <summary>The belt now, slot by slot, null for an empty slot.</summary>
    public IReadOnlyList<PotionDef?> Potions => Fight is { } fight ? fight.Potions : _c.State.Belt.Slots;

    /// <summary>The relics now, in pickup order.</summary>
    public IReadOnlyList<Relic> Relics => Fight is { } fight ? fight.Relics : _c.State.Relics;

    /// <summary>The deck now, in deck order.</summary>
    public IReadOnlyList<CardInstance> Deck => Fight is { } fight ? fight.DeckCards : _c.State.Deck;

    /// <summary>The <c>run</c> block (Describe.RunInfo): the act from 1, the game's total floor, the ascension.</summary>
    public JsonObject RunJson() => new()
    {
        ["act"] = _c.State.ActIndex + 1,
        ["floor"] = _c.State.TotalFloor,
        ["ascension"] = _c.State.Ascension,
    };

    /// <summary>
    /// The <c>player</c> block (Describe.PlayerRun), keys in the mod's order: <c>character</c>,
    /// <c>hp</c>, <c>max_hp</c>, <c>gold</c>, <c>relics</c> (names in pickup order),
    /// <c>potions</c> (a name or null per slot), <c>deck_size</c>, <c>deck</c> (name to count,
    /// names in ordinal order, an upgraded card under its own name).
    /// </summary>
    public JsonObject PlayerJson(PlayerKeys keys = PlayerKeys.Full)
    {
        var json = new JsonObject { ["character"] = _c.State.Character };
        if ((keys & PlayerKeys.Hp) != 0)
        {
            json["hp"] = Hp;
            json["max_hp"] = MaxHp;
        }
        if ((keys & PlayerKeys.Gold) != 0) json["gold"] = Gold;

        var relics = new JsonArray();
        foreach (var relic in Relics) relics.Add(relic.Name);
        json["relics"] = relics;

        if ((keys & PlayerKeys.Potions) != 0)
        {
            var potions = new JsonArray();
            foreach (var potion in Potions) potions.Add(potion?.Name);
            json["potions"] = potions;
        }
        if ((keys & (PlayerKeys.Deck | PlayerKeys.DeckSize)) != 0) json["deck_size"] = Deck.Count;
        if ((keys & PlayerKeys.Deck) != 0) json["deck"] = RunDescribe.DeckCounts(Deck);
        return json;
    }

    /// <summary>
    /// The detailed <c>potions</c> list of the screens outside a fight (PotionActions.Describe):
    /// one entry per slot, null for an empty one, each with <c>slot</c>, <c>name</c>,
    /// <c>text</c>, <c>target</c> and <c>usable</c>.
    /// </summary>
    /// <param name="usable">Whether the potion in a slot can be used on this screen right now; nobody asked means no.</param>
    public JsonArray PotionsJson(Func<int, PotionDef, bool>? usable = null)
    {
        var fight = Fight;
        var slots = new JsonArray();
        var belt = Potions;
        for (var slot = 0; slot < belt.Count; slot++)
        {
            if (belt[slot] is not { } potion)
            {
                slots.Add((JsonNode?)null);
                continue;
            }
            slots.Add(new JsonObject
            {
                ["slot"] = slot, ["name"] = potion.Name, ["text"] = potion.Text(fight), ["target"] = potion.Target.ToString(),
                ["usable"] = usable?.Invoke(slot, potion) ?? false,
            });
        }
        return slots;
    }

    /// <summary>
    /// get_deck (Describe.Deck), keys in the mod's order: <c>character</c>, <c>hp</c>,
    /// <c>max_hp</c>, <c>gold</c>, <c>run</c>, <c>deck_size</c>, <c>deck</c> (one entry per card
    /// in deck order, as an offer, with <c>upgraded: true</c> on an upgraded one), <c>relics</c>
    /// with their text, and <c>potions</c>: the filled slots only, without a slot number.
    /// </summary>
    public JsonObject DescribeDeck()
    {
        var fight = Fight;
        var deck = new JsonArray();
        foreach (var card in Deck)
        {
            var entry = RunDescribe.CardOffer(card);
            if (card.IsUpgraded) entry["upgraded"] = true;
            deck.Add(entry);
        }

        var relics = new JsonArray();
        foreach (var relic in Relics) relics.Add(RunDescribe.Relic(relic));

        var potions = new JsonArray();
        foreach (var potion in Potions)
            if (potion is not null)
                potions.Add(new JsonObject { ["name"] = potion.Name, ["text"] = potion.Text(fight), ["target"] = potion.Target.ToString() });

        return new JsonObject
        {
            ["character"] = _c.State.Character,
            ["hp"] = Hp,
            ["max_hp"] = MaxHp,
            ["gold"] = Gold,
            ["run"] = RunJson(),
            ["deck_size"] = Deck.Count,
            ["deck"] = deck,
            ["relics"] = relics,
            ["potions"] = potions,
        };
    }

    // ---------- the end screens ----------

    /// <summary>Puts up the end snapshot of a run that is already decided (after the game over screen).</summary>
    internal void ShowEnd()
    {
        if (_c.Result is null) throw new InvalidOperationException("The run is not over.");
        _c.Screens.Clear();
        _c.Screens.Add(new ArenaOverScreen());
    }

    /// <summary>The label of the end snapshot's one action.</summary>
    internal static string OverLabel(string? result) => result switch
    {
        RunResult.Cleared => "The run is over: you cleared the act. Nothing is left to do; stop here.",
        RunResult.Died => "The run is over: you died. Nothing is left to do; stop here.",
        RunResult.Unrepresentable => "The run is over: it met something the arena does not hold. Nothing is left to do; stop here.",
        _ => "The run is over: the arena failed. Nothing is left to do; stop here.",
    };

    /// <summary>One line on how the run ended, for the end snapshot and the result line.</summary>
    public string Summary()
    {
        var state = _c.State;
        var floor = state.TotalFloor;
        switch (_c.Result)
        {
            case RunResult.Cleared:
                return string.Create(CultureInfo.InvariantCulture,
                    $"Cleared the act on floor {floor} with {state.Hp}/{state.MaxHp} HP, {state.Gold} gold, {state.Deck.Count} cards and {state.Relics.Count} {(state.Relics.Count == 1 ? "relic" : "relics")}.");
            case RunResult.Died when _c.LastFight is { Won: false } fight:
                return string.Create(CultureInfo.InvariantCulture,
                    $"{(fight.TimedOut ? "Lost to the turn limit" : "Died")} on floor {fight.Floor} against {fight.Enemies} ({fight.RoomType}, turn {fight.Turns}).");
            case RunResult.Died:
                return string.Create(CultureInfo.InvariantCulture, $"Died on floor {floor}.");
            case RunResult.Unrepresentable:
                return string.Create(CultureInfo.InvariantCulture, $"Stopped on floor {floor}: the run met something the arena does not hold.");
            case null:
                return string.Create(CultureInfo.InvariantCulture, $"The run is on floor {floor}.");
            default:
                return string.Create(CultureInfo.InvariantCulture, $"Stopped on floor {floor}: the arena failed.");
        }
    }
}
