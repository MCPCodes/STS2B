using System.Globalization;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// A file of scripted checks. Every class in the arena assembly that implements this and has a
/// parameterless constructor is found by reflection and run by <c>arena selfcheck</c>. Name
/// the class after the content file it checks (Cards1Checks for Cards1).
/// </summary>
public interface IArenaChecks
{
    void Run(Check check);
}

/// <summary>
/// Collects what scripted checks find. A check that throws counts as one failure and the rest
/// of its group is skipped; the other groups still run.
/// </summary>
public sealed class Check
{
    private readonly List<string> _failures = new();
    private string _group = "";

    public int Passed { get; private set; }
    public IReadOnlyList<string> Failures => _failures;

    /// <summary>Runs one named scenario. Put each rule or card in its own group, so a failure names it.</summary>
    public void Group(string name, Action body)
    {
        var outer = _group;
        _group = outer.Length > 0 ? $"{outer} / {name}" : name;
        try
        {
            body();
        }
        catch (Exception ex)
        {
            _failures.Add($"{_group}: threw {ex.GetType().Name}: {ex.Message}");
        }
        _group = outer;
    }

    public void That(bool ok, string what)
    {
        if (ok) Passed++;
        else _failures.Add(_group.Length > 0 ? $"{_group}: {what}" : what);
    }

    /// <summary>Fails with both values when they differ.</summary>
    public void Equal<T>(T expected, T actual, string what) =>
        That(EqualityComparer<T>.Default.Equals(expected, actual),
            string.Create(CultureInfo.InvariantCulture, $"{what}: expected {Show(expected)}, got {Show(actual)}"));

    /// <summary>Compares two sequences item by item.</summary>
    public void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string what)
    {
        var e = expected.ToList();
        var a = actual.ToList();
        That(e.SequenceEqual(a), $"{what}: expected [{string.Join(", ", e.Select(Show))}], got [{string.Join(", ", a.Select(Show))}]");
    }

    private static string Show<T>(T value) => value is null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
}

/// <summary>
/// Sets up any fight exactly, for scripted checks:
/// <code>
/// var f = new FightBuilder().DrawOrder("Bash", "StrikeIronclad", "DefendIronclad").Enemy(new Nibbit { IsAlone = true }, hp: 44).Start();
/// f.Play("Bash", 0);
/// </code>
/// Without a deck the Ironclad starter is used; without enemies or an encounter, NibbitsWeak.
/// </summary>
public sealed class FightBuilder
{
    private List<(CardDef Def, bool Upgraded)> _deck = Starter();
    private bool _shuffle = true;
    private int _hp = 80, _maxHp = 80, _seed = 1, _floor = 1, _maxTurns = Tuning.MaxTurns;
    private readonly string?[] _potions = [null, null, null];
    private string? _encounter;
    private List<PlacedMonster>? _monsters;
    private IReadOnlyList<string> _slots = [];
    private RoomType _room = RoomType.Monster;
    private List<string> _relics = ["BurningBlood"];
    private readonly List<Relic> _extraRelics = new();

    private static List<(CardDef, bool)> Starter() => Resolve(
        "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad",
        "DefendIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad", "Bash");

    private static List<(CardDef, bool)> Resolve(params string[] cards) =>
        cards.Select(DeckCard.Parse).Select(c => (Content.Registry.Card(c.ClassName), c.Upgraded)).ToList();

    /// <summary>The deck by class names, "+" for upgraded ("Bash+"); shuffled by the fight's seed as in the game.</summary>
    public FightBuilder Deck(params string[] cards)
    {
        _deck = Resolve(cards);
        _shuffle = true;
        return this;
    }

    /// <summary>The deck in the exact order it will be drawn: the first card named is drawn first. No shuffle.</summary>
    public FightBuilder DrawOrder(params string[] cards)
    {
        _deck = Resolve(cards);
        _shuffle = false;
        return this;
    }

    /// <summary>Appends a card to the deck by its definition (a check's own card that is not in the registry).</summary>
    public FightBuilder Add(CardDef card, bool upgraded = false)
    {
        _deck.Add((card, upgraded));
        return this;
    }

    /// <summary>Appends cards to the deck by class name ("Bash+").</summary>
    public FightBuilder Add(params string[] cards)
    {
        _deck.AddRange(Resolve(cards));
        return this;
    }

    public FightBuilder Hp(int hp, int maxHp = 80)
    {
        _hp = hp;
        _maxHp = maxHp;
        return this;
    }

    /// <summary>Potion class names by belt slot; null for an empty slot.</summary>
    public FightBuilder Potions(params string?[] potions)
    {
        for (var i = 0; i < _potions.Length; i++) _potions[i] = i < potions.Length ? potions[i] : null;
        return this;
    }

    /// <summary>A registered encounter by class name; its own line-up and HP rolls are used.</summary>
    public FightBuilder Encounter(string className)
    {
        _encounter = className;
        _monsters = null;
        return this;
    }

    /// <summary>Adds a monster by hand, in list order. Give <paramref name="hp"/> to skip the HP roll.
    /// Set the monster's own flags on the object before passing it.</summary>
    public FightBuilder Enemy(Monster monster, int? hp = null, string? slot = null)
    {
        (_monsters ??= new()).Add(new PlacedMonster(monster, slot, hp));
        return this;
    }

    public FightBuilder Enemy(string monsterClassName, int? hp = null, string? slot = null) =>
        Enemy(Content.Registry.NewMonster(monsterClassName), hp, slot);

    /// <summary>The slot order for monsters placed by hand with slots.</summary>
    public FightBuilder Slots(params string[] slots)
    {
        _slots = slots;
        return this;
    }

    public FightBuilder Room(RoomType room)
    {
        _room = room;
        return this;
    }

    public FightBuilder Seed(int seed)
    {
        _seed = seed;
        return this;
    }

    public FightBuilder Floor(int floor)
    {
        _floor = floor;
        return this;
    }

    public FightBuilder MaxTurns(int turns)
    {
        _maxTurns = turns;
        return this;
    }

    /// <summary>Relic class names; the default is Burning Blood alone.</summary>
    public FightBuilder Relics(params string[] relics)
    {
        _relics = relics.ToList();
        return this;
    }

    /// <summary>Adds a relic object that is not in the registry (a check's own).</summary>
    public FightBuilder Relic(Relic relic)
    {
        _extraRelics.Add(relic);
        return this;
    }

    /// <summary>Starts the fight: when this returns, turn 1's hand is drawn and the player may act.</summary>
    public Fight Start() => new(new FightConfig
    {
        Deck = _deck.ToList(), ShuffleDeck = _shuffle, Hp = _hp, MaxHp = _maxHp, Potions = (string?[])_potions.Clone(),
        Encounter = _monsters is null ? _encounter ?? "NibbitsWeak" : null, Monsters = _monsters, Slots = _slots, RoomType = _room,
        Floor = _floor, Seed = _seed, Relics = _relics.ToList(), ExtraRelics = _extraRelics.ToList(), MaxTurns = _maxTurns,
    });
}

// Direct handles on a fight for scripted checks: stepping by action ids with a clear error,
// and setting up a situation without playing up to it. Rules code never calls these.
public sealed partial class Fight
{
    /// <summary>Runs an action id and throws if it was refused, so a check fails where it went wrong.</summary>
    public Fight Do(string actionId)
    {
        var result = Execute(actionId);
        if (!result.Ok)
            throw new InvalidOperationException($"'{actionId}' was refused: {result.Error} Legal now: {string.Join(", ", LegalActions().Select(a => a.Id))}");
        return this;
    }

    /// <summary>Runs several action ids in order.</summary>
    public Fight Do(params string[] actionIds)
    {
        foreach (var id in actionIds) Do(id);
        return this;
    }

    /// <summary>The hand index of the first card with this title ("Bash", "Bash+") or class name; throws when it is not in hand.</summary>
    public int HandIndex(string titleOrClassName)
    {
        for (var i = 0; i < _s.Hand.Count; i++)
            if (_s.Hand[i].Title == titleOrClassName || _s.Hand[i].Def.ClassName == titleOrClassName) return i;
        throw new InvalidOperationException($"No '{titleOrClassName}' in hand: [{string.Join(", ", _s.Hand.Select(c => c.Title))}]");
    }

    /// <summary>Plays the first hand card with this title or class name, at enemy <paramref name="enemy"/> (its e-index) when the card needs one.</summary>
    public Fight Play(string titleOrClassName, int? enemy = null)
    {
        var i = HandIndex(titleOrClassName);
        return Do(enemy is { } j ? $"play:{i}>e{j}" : $"play:{i}");
    }

    public Fight EndTurn() => Do("end_turn");

    /// <summary>Creates a card straight into a pile, with no hooks: for setting up a hand or a draw order.</summary>
    public CardInstance Put(string card, PileType pile, PilePosition position = PilePosition.Bottom)
    {
        var entry = DeckCard.Parse(card);
        var instance = NewCard(Content.Registry.Card(entry.ClassName), entry.Upgraded);
        var list = PileList(pile);
        if (position == PilePosition.Top) list.Insert(0, instance);
        else list.Add(instance);
        instance.Pile = pile;
        return instance;
    }

    /// <summary>The same for a card definition that is not in the registry (a check's own card).</summary>
    public CardInstance Put(CardDef def, PileType pile, bool upgraded = false, PilePosition position = PilePosition.Bottom)
    {
        var instance = NewCard(def, upgraded);
        var list = PileList(pile);
        if (position == PilePosition.Top) list.Insert(0, instance);
        else list.Add(instance);
        instance.Pile = pile;
        return instance;
    }

    /// <summary>Empties a pile, with no hooks.</summary>
    public void Clear(PileType pile)
    {
        var list = PileList(pile);
        foreach (var card in list) card.Pile = PileType.None;
        list.Clear();
    }

    /// <summary>Turns the draw pile upside down, with no hooks: a fight that shows the same and draws differently.</summary>
    public void ReverseDrawPile() => _s.Draw.Reverse();

    public void SetHp(Creature creature, int hp) => creature.Hp = Math.Min(hp, creature.MaxHp);

    public void SetBlock(Creature creature, int block) => creature.Block = block;

    public void SetEnergy(int energy) => _s.Energy = energy;

    /// <summary>Puts a potion in a belt slot (null empties it).</summary>
    public void SetPotion(int slot, string? potionClassName) =>
        _s.Potions[slot] = potionClassName is null ? null : Content.Registry.Potion(potionClassName);

    /// <summary>Puts a potion that is not in the registry in a belt slot (a check's own).</summary>
    public void SetPotion(int slot, PotionDef potion) => _s.Potions[slot] = potion;

    /// <summary>The enemy with this e-index.</summary>
    public Creature Enemy(int rosterIndex) => _s.Roster[rosterIndex];

    /// <summary>Runs engine commands as if inside a player action, so that a card choice they open is shown and can be answered.</summary>
    public ActionResult RunAsAction(Action<Fight> body)
    {
        _scripted = body;
        _checkpoint = _s.Clone(out _);
        _answers = new List<int[]>();
        return Run(ScriptedAction);
    }
}
