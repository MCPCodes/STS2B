using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// What a new run starts with. The defaults are the Ironclad's at ascension 0 as the game
/// makes the player (Player.CreateForNewRun with the Ironclad's character model): 80 HP, 99
/// gold, five Strikes, four Defends and Bash in that order, Burning Blood, three empty potion
/// slots. Checks change what they need.
/// </summary>
public sealed class RunSetup
{
    public const int IroncladHp = 80;
    public const int IroncladGold = 99;

    /// <summary>The potion slots every character starts with.</summary>
    public const int StartingPotionSlots = 3;

    /// <summary>The Ironclad's starting deck in deck order.</summary>
    public static readonly IReadOnlyList<DeckCard> IroncladDeck =
    [
        new("StrikeIronclad"), new("StrikeIronclad"), new("StrikeIronclad"), new("StrikeIronclad"), new("StrikeIronclad"),
        new("DefendIronclad"), new("DefendIronclad"), new("DefendIronclad"), new("DefendIronclad"), new("Bash"),
    ];

    /// <summary>The run's seed, a text as in the game. It names the run; every random stream of the run follows from it.</summary>
    public string Seed { get; init; } = "ARENA";

    public int Hp { get; init; } = IroncladHp;
    public int MaxHp { get; init; } = IroncladHp;
    public int Gold { get; init; } = IroncladGold;
    public int PotionSlots { get; init; } = StartingPotionSlots;

    /// <summary>The deck in deck order, by class name.</summary>
    public IReadOnlyList<DeckCard> Deck { get; init; } = IroncladDeck;

    /// <summary>Cards that are not in the registry (a check's own), appended to the deck.</summary>
    public IReadOnlyList<(CardDef Def, bool Upgraded)> ExtraCards { get; init; } = [];

    /// <summary>Relic class names in pickup order.</summary>
    public IReadOnlyList<string> Relics { get; init; } = ["BurningBlood"];

    /// <summary>Relics that are not in the registry (a check's own), after <see cref="Relics"/>. Each is a
    /// function that makes a new object, so that a replay of the run starts with fresh ones.</summary>
    public IReadOnlyList<Func<Relic>> ExtraRelics { get; init; } = [];

    /// <summary>Potion class names by belt slot, null for an empty slot. Slots not named are empty.</summary>
    public IReadOnlyList<string?> Potions { get; init; } = [];

    /// <summary>The arena's turn limit for every fight of the run (a house rule: the game has none).</summary>
    public int MaxTurns { get; init; } = Tuning.MaxTurns;
}

/// <summary>
/// A piece of run state that another part of the simulator owns (the map, the relic bags, the
/// encounter lists, the random streams): it lives in the <see cref="RunState"/> under its own
/// type, so nobody has to edit <see cref="RunState"/> to add one, and it is copied with the run.
/// </summary>
public abstract class RunPart
{
    /// <summary>
    /// A deep copy for a copy of the run. <paramref name="map"/> gives the copy's deck card or
    /// relic for one of this state's (<see cref="CloneMap.Get{T}"/>), for a part that points at any.
    /// </summary>
    public abstract RunPart Clone(CloneMap map);

    /// <summary>Everything the part holds, for comparing two runs (<see cref="RunState.Dump"/>). Null says nothing.</summary>
    public virtual JsonNode? Dump() => null;
}

/// <summary>
/// The potion belt: a row of slots, each empty or holding one potion (the game's
/// Player.PotionSlots). A run starts with three; a relic can add more (Potion Belt).
/// </summary>
public sealed class PotionBelt
{
    private readonly List<PotionDef?> _slots;

    public PotionBelt(int slots)
    {
        _slots = new List<PotionDef?>(Math.Max(slots, 0));
        for (var i = 0; i < slots; i++) _slots.Add(null);
    }

    private PotionBelt(PotionBelt other) => _slots = other._slots.ToList();

    /// <summary>The number of slots (the game's MaxPotionCount).</summary>
    public int Count => _slots.Count;

    /// <summary>The potion in a slot, or null.</summary>
    public PotionDef? this[int slot] => _slots[slot];

    /// <summary>Slot by slot, null for an empty one.</summary>
    public IReadOnlyList<PotionDef?> Slots => _slots;

    public bool HasOpenSlot => _slots.Contains(null);

    /// <summary>
    /// Puts a potion into <paramref name="slot"/>, or into the first empty slot when none is
    /// named. False, and nothing changes, when that slot is taken or the belt is full: the
    /// game never pushes a potion out.
    /// </summary>
    public bool Add(PotionDef potion, int slot = -1)
    {
        if (slot < 0) slot = _slots.IndexOf(null);
        if (slot < 0 || slot >= _slots.Count || _slots[slot] is not null) return false;
        _slots[slot] = potion;
        return true;
    }

    /// <summary>Empties a slot (a potion used or thrown away) and returns what was in it.</summary>
    public PotionDef? Remove(int slot)
    {
        var potion = _slots[slot];
        _slots[slot] = null;
        return potion;
    }

    /// <summary>
    /// Adds empty slots at the end (the game's AddToMaxPotionCount). The belt never shrinks in
    /// scope: only an ascension level takes a slot away, so that half of the game's rule is left out.
    /// </summary>
    public void Grow(int by)
    {
        if (by < 0) throw new NotSupportedException("The arena leaves out a shrinking potion belt: only an ascension level takes a slot away.");
        for (var i = 0; i < by; i++) _slots.Add(null);
    }

    /// <summary>The belt as a fight left it.</summary>
    internal void Set(IReadOnlyList<PotionDef?> slots)
    {
        _slots.Clear();
        _slots.AddRange(slots);
    }

    public PotionBelt Clone() => new(this);
}

/// <summary>
/// Everything about a run that lasts from room to room: the player (HP, gold, the deck, the
/// relics, the belt), the two odds values that move with every roll, and where the run stands.
/// The game keeps the same on its Player and RunState objects.
/// <para>
/// It is plain state. The rules that change it with their hooks (gaining gold, healing, adding
/// a card, picking up a relic) are commands of the parts that own them; the members here only
/// keep the state in one piece. While a fight is on, the fight works on its own copy of the
/// player and this object is the player as the fight began: ask <see cref="Run"/> for the live
/// numbers, and read this again once the fight has handed back.
/// </para>
/// <para>
/// Deck cards are <see cref="CardInstance"/>s in pile Deck. Each has an id that is its own for
/// the whole run; the cards of a fight are made from them and point back
/// (<see cref="CardInstance.DeckCardId"/>). A relic is the same object for the whole run, with
/// whatever it has counted.
/// </para>
/// </summary>
public sealed partial class RunState
{
    /// <summary>The offset on the Rare odds of a card reward that a run starts with (the game's CardRarityOdds): -0.05.</summary>
    public const float StartingCardRarityOffset = CardRarityOdds.BaseOffset;

    /// <summary>The chance of a potion after a fight that a run starts with (the game's PotionRewardOdds): 0.4.</summary>
    public const float StartingPotionChance = PotionOdds.Base;

    private readonly List<CardInstance> _deck = new();
    private readonly List<Relic> _relics = new();
    private readonly Dictionary<Type, RunPart> _parts = new();
    private int _nextCardId = 1;

    public RunState(RunSetup setup)
    {
        Seed = setup.Seed;
        MaxHp = setup.MaxHp;
        Hp = Math.Min(setup.Hp, setup.MaxHp);
        Gold = setup.Gold;
        MaxTurns = setup.MaxTurns;
        Belt = new PotionBelt(setup.PotionSlots);
        var registry = Content.Registry;
        for (var slot = 0; slot < setup.Potions.Count; slot++)
            if (setup.Potions[slot] is { } potion && !Belt.Add(registry.Potion(potion), slot))
                throw new ArgumentException($"The belt has no free slot {slot} for {potion}.", nameof(setup));
        foreach (var card in setup.Deck) AddCard(registry.Card(card.ClassName), card.Upgraded);
        foreach (var (def, upgraded) in setup.ExtraCards) AddCard(def, upgraded);
        foreach (var relic in setup.Relics) _relics.Add(registry.NewRelic(relic));
        foreach (var make in setup.ExtraRelics) _relics.Add(make());
    }

    private RunState(RunState other)
    {
        Seed = other.Seed;
        Hp = other.Hp;
        MaxHp = other.MaxHp;
        Gold = other.Gold;
        MaxTurns = other.MaxTurns;
        Belt = other.Belt.Clone();
        CardRarityOffset = other.CardRarityOffset;
        PotionChance = other.PotionChance;
        ActIndex = other.ActIndex;
        ActFloor = other.ActFloor;
        TotalFloor = other.TotalFloor;
        _nextCardId = other._nextCardId;
    }

    // ---------- the run ----------

    /// <summary>The run's seed text.</summary>
    public string Seed { get; }

    /// <summary>The act the run is in, from 0. The snapshots' "act" is this plus 1.</summary>
    public int ActIndex { get; set; }

    /// <summary>The floor inside the act as the game counts it: the map row plus 1.</summary>
    public int ActFloor { get; set; }

    /// <summary>
    /// The game's total floor: how many map points the run has entered, over all acts. It is the
    /// "floor" of every snapshot's run block. Whoever moves the run to a new map point raises it.
    /// </summary>
    public int TotalFloor { get; set; }

    /// <summary>Always 0: ascensions are out of scope.</summary>
    public int Ascension => 0;

    /// <summary>The arena's turn limit for a fight of this run.</summary>
    public int MaxTurns { get; }

    // ---------- the player ----------

    /// <summary>The character's title, as the mod writes it for <c>player.character</c> and the player's creature.</summary>
    public string Character => Fight.PlayerName;

    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Gold { get; set; }

    public bool IsDead => Hp <= 0;

    public PotionBelt Belt { get; }

    /// <summary>The deck in deck order: new cards go to the end.</summary>
    public IReadOnlyList<CardInstance> Deck => _deck;

    /// <summary>The relics in pickup order.</summary>
    public IReadOnlyList<Relic> Relics => _relics;

    // ---------- the two odds values ----------

    /// <summary>
    /// What is added to the Rare odds of the next reward card (the game's CardRarityOdds value):
    /// it starts at -0.05, grows by 0.01 with every card that is not Rare and falls back when
    /// one is. A 32-bit float, as in the game; the rolls that move it are the rewards'
    /// (<see cref="RunWorld.RollCardRarity"/>).
    /// </summary>
    public float CardRarityOffset { get; set; } = StartingCardRarityOffset;

    /// <summary>
    /// The chance that the next fight drops a potion (the game's PotionRewardOdds value): it
    /// starts at 0.4 and moves by 0.1 after every roll, down after a drop. A 32-bit float
    /// (<see cref="RunWorld.RollPotionDrop"/>).
    /// </summary>
    public float PotionChance { get; set; } = StartingPotionChance;

    // ---------- the deck ----------

    /// <summary>
    /// A new deck card with an id of its own, put at the end of the deck (or at
    /// <paramref name="index"/>). No hook hears this: adding a card as the game does it is a
    /// command of the deck edits.
    /// </summary>
    public CardInstance AddCard(CardDef def, bool upgraded = false, int index = -1)
    {
        var card = NewCard(def, upgraded);
        if (index < 0) _deck.Add(card);
        else _deck.Insert(index, card);
        return card;
    }

    public CardInstance AddCard(string className, bool upgraded = false) => AddCard(Content.Registry.Card(className), upgraded);

    /// <summary>A new deck card (pile Deck) with an id of its own that is in no deck yet: a card on offer, a preview.</summary>
    public CardInstance NewCard(CardDef def, bool upgraded = false)
    {
        var card = new CardInstance(_nextCardId++, def) { Pile = PileType.Deck };
        if (upgraded && card.IsUpgradable) card.ApplyUpgrade();
        return card;
    }

    /// <summary>Puts a card made with <see cref="NewCard"/> into the deck.</summary>
    public void PutInDeck(CardInstance card, int index = -1)
    {
        if (_deck.Contains(card)) throw new InvalidOperationException($"{card} is already in the deck.");
        card.Pile = PileType.Deck;
        if (index < 0) _deck.Add(card);
        else _deck.Insert(index, card);
    }

    /// <summary>Takes a card out of the deck. False when it is not in it.</summary>
    public bool RemoveCard(CardInstance card)
    {
        if (!_deck.Remove(card)) return false;
        card.Pile = PileType.None;
        return true;
    }

    /// <summary>The deck card with this id, or null when it has left the deck.</summary>
    public CardInstance? CardById(int id)
    {
        foreach (var card in _deck)
            if (card.Id == id) return card;
        return null;
    }

    // ---------- the relics ----------

    /// <summary>Puts a relic object at the end of the list (or at <paramref name="index"/>, for a relic
    /// that takes another's place). Its pickup effect is the relic framework's to run.</summary>
    public void AddRelic(Relic relic, int index = -1)
    {
        if (index < 0) _relics.Add(relic);
        else _relics.Insert(index, relic);
    }

    public bool RemoveRelic(Relic relic) => _relics.Remove(relic);

    /// <summary>The first relic with this class name, or null.</summary>
    public Relic? FindRelic(string className)
    {
        foreach (var relic in _relics)
            if (relic.ClassName == className) return relic;
        return null;
    }

    public T? FindRelic<T>() where T : Relic
    {
        foreach (var relic in _relics)
            if (relic is T found) return found;
        return null;
    }

    public bool HasRelic(string className) => FindRelic(className) is not null;

    // ---------- other parts' state ----------

    /// <summary>The run's piece of state of type <typeparamref name="T"/>; made empty the first time it is asked for.</summary>
    public T Part<T>() where T : RunPart, new()
    {
        if (_parts.TryGetValue(typeof(T), out var part)) return (T)part;
        var made = new T();
        _parts[typeof(T)] = made;
        return made;
    }

    /// <summary>The same without making one: null when the run has none yet.</summary>
    public T? FindPart<T>() where T : RunPart => _parts.TryGetValue(typeof(T), out var part) ? (T)part : null;

    /// <summary>Puts a part in place that needs more than an empty constructor.</summary>
    public void SetPart<T>(T part) where T : RunPart => _parts[typeof(T)] = part;

    // ---------- the hand-over to a fight and back ----------

    /// <summary>What a fight is given. The fight copies it; nothing of this state changes while the fight runs.</summary>
    internal RunHandOver HandOver() => new(_deck, _relics, Belt.Slots, Gold, _nextCardId);

    /// <summary>
    /// Takes back what a fight left: HP and max HP of the player's creature, the gold, the belt,
    /// the relic objects with whatever they counted, and the deck as it stands (a card the fight
    /// took out of the deck is gone, one it put in is there).
    /// </summary>
    internal void TakeBack(int hp, int maxHp, RunHandOver fromFight)
    {
        MaxHp = maxHp;
        Hp = Math.Max(0, Math.Min(hp, maxHp));
        Gold = fromFight.Gold;
        Belt.Set(fromFight.Belt);
        _relics.Clear();
        _relics.AddRange(fromFight.Relics);
        _deck.Clear();
        _deck.AddRange(fromFight.Deck);
        _nextCardId = Math.Max(_nextCardId, fromFight.NextDeckCardId);
    }

    // ---------- copies ----------

    /// <summary>A deep copy: nothing done to the copy shows in this state.</summary>
    public RunState Clone() => Clone(out _);

    /// <summary>The same, with the map from this state's deck cards and relics to the copy's.</summary>
    public RunState Clone(out CloneMap map)
    {
        map = new CloneMap();
        var copy = new RunState(this);
        foreach (var card in _deck) copy._deck.Add(card.CloneFor(map));
        foreach (var relic in _relics) copy._relics.Add(relic.CloneFor(map));
        map.Finish();
        foreach (var (type, part) in _parts) copy._parts[type] = part.Clone(map);
        return copy;
    }

    // ---------- everything, for comparing two runs ----------

    /// <summary>
    /// The whole state as JSON, every field of every relic and card hook object included. Two runs
    /// whose dumps are equal texts are in the same state; a replay is checked this way.
    /// </summary>
    public JsonObject Dump()
    {
        var deck = new JsonArray();
        foreach (var card in _deck) deck.Add(DumpCard(card));
        var relics = new JsonArray();
        foreach (var relic in _relics) relics.Add(new JsonObject { ["class"] = relic.ClassName, ["fields"] = DumpFields(relic) });
        var belt = new JsonArray();
        foreach (var potion in Belt.Slots) belt.Add(potion?.ClassName);
        var parts = new JsonObject();
        foreach (var (type, part) in _parts.OrderBy(p => p.Key.FullName, StringComparer.Ordinal))
            parts[type.Name] = part.Dump();

        return new JsonObject
        {
            ["seed"] = Seed,
            ["act"] = ActIndex,
            ["act_floor"] = ActFloor,
            ["total_floor"] = TotalFloor,
            ["hp"] = Hp,
            ["max_hp"] = MaxHp,
            ["gold"] = Gold,
            ["belt"] = belt,
            ["card_rarity_offset"] = Exact(CardRarityOffset),
            ["potion_chance"] = Exact(PotionChance),
            ["next_card_id"] = _nextCardId,
            ["deck"] = deck,
            ["relics"] = relics,
            ["parts"] = parts,
        };
    }

    /// <summary>A float with every bit of it: the shortest text that reads back as the same float.</summary>
    private static string Exact(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static JsonObject DumpCard(CardInstance card)
    {
        var numbers = new JsonObject();
        foreach (var name in card.Def.Vars.Names) numbers[name] = card.Get(name).ToString(CultureInfo.InvariantCulture);
        var json = new JsonObject
        {
            ["id"] = card.Id,
            ["class"] = card.Def.ClassName,
            ["upgrade"] = card.UpgradeLevel,
            ["cost"] = card.BaseCost,
            ["keywords"] = card.Keywords.ToString(),
            ["numbers"] = numbers,
        };
        if (card.Behaviour is { } behaviour) json["hooks"] = DumpFields(behaviour);
        return json;
    }

    /// <summary>The fields a content class declares on its relic or card hook object, by name.</summary>
    private static JsonObject DumpFields(Listener listener)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var json = new JsonObject();
        var fields = new List<FieldInfo>();
        for (var type = listener.GetType(); type is not null && type != typeof(Listener) && type != typeof(Relic) && type != typeof(CardBehaviour) && type != typeof(object); type = type.BaseType)
            fields.AddRange(type.GetFields(flags));
        foreach (var field in fields.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            json[field.Name] = field.GetValue(listener) switch
            {
                null => null,
                CardInstance card => $"card#{card.Id}",
                IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
                System.Collections.IEnumerable items and not string => string.Join(",", items.Cast<object?>().Select(i => i is CardInstance c ? $"card#{c.Id}" : Convert.ToString(i, CultureInfo.InvariantCulture))),
                var other => other.ToString(),
            };
        }
        return json;
    }
}
