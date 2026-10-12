using System.Reflection;

namespace SpireMind.Arena;

public enum ContentKind { Card, Power, Monster, Encounter, Potion, Relic, Affliction, Event }

/// <summary>
/// A file of content. Every class in the arena assembly that implements this and has a
/// parameterless constructor is found by reflection and asked once to register what it holds,
/// so content files never have to be listed anywhere.
/// </summary>
public interface IContentModule
{
    void Register(Registry r);
}

/// <summary>A marker a monster puts on cards (Entangled, Ringing). It has no logic of its own;
/// the power that hands it out does the work.</summary>
/// <param name="ClassName">The game's class name.</param>
/// <param name="Name">The game's English name.</param>
/// <param name="ExtraCardText">A line added to the text of a card that carries it (given the amount), or null for none.</param>
/// <param name="IsStackable">True when putting it on a card that already carries it adds to the amount
/// (in the game only Galvanized and Tainted); otherwise the second one is refused.</param>
public sealed record AfflictionDef(string ClassName, string Name, Func<int, string>? ExtraCardText = null, bool IsStackable = false);

/// <summary>
/// Everything the simulator knows, by the game's class names: cards, powers, monsters,
/// encounters, potions, relics and afflictions, plus what was left out on purpose and why.
/// </summary>
public sealed class Registry
{
    private readonly Dictionary<string, CardDef> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<Power>> _powers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<Monster>> _monsters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<Encounter>> _encounters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PotionDef> _potions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<Relic>> _relics = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AfflictionDef> _afflictions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<RunEvent>> _events = new(StringComparer.Ordinal);
    private readonly Dictionary<(ContentKind, string), string> _excluded = new();
    private readonly Dictionary<(ContentKind, string), string> _source = new();
    private readonly List<string> _problems = new();
    private string _module = "?";

    public IReadOnlyDictionary<string, CardDef> Cards => _cards;
    public IReadOnlyDictionary<string, PotionDef> Potions => _potions;
    public IReadOnlyDictionary<string, AfflictionDef> Afflictions => _afflictions;
    public IEnumerable<string> PowerNames => _powers.Keys;
    public IEnumerable<string> MonsterNames => _monsters.Keys;
    public IEnumerable<string> EncounterNames => _encounters.Keys;
    public IEnumerable<string> RelicNames => _relics.Keys;
    public IEnumerable<string> EventNames => _events.Keys;

    private IReadOnlyList<CardDef>? _pool;

    /// <summary>
    /// The Ironclad's card pool as far as the arena has it: every registered card of the game's
    /// Ironclad pool (<see cref="CardPools.Ironclad"/>), in the order of that pool (by class
    /// name). Status cards and tokens are not in it, nor are the cards of the other pools a run
    /// can hold (colorless cards, curses, event cards); cards left out on purpose are missing.
    /// Random card generation filters this list.
    /// </summary>
    public IReadOnlyList<CardDef> IroncladPool => _pool ??= _cards.Values
        .Where(c => c.Rarity is CardRarity.Basic or CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare or CardRarity.Ancient or CardRarity.Event)
        // Run mode registers cards of other pools under the same rarities (Neow's Fury is an Ancient-rarity
        // card of the event pool, the colorless cards are Uncommon and Rare): only the Ironclad's own count here.
        .Where(c => CardPools.Ironclad.Contains(c.ClassName))
        .OrderBy(c => c.ClassName, StringComparer.Ordinal)
        .ToList();

    /// <summary>What was left out on purpose: (kind, class name) to the reason.</summary>
    public IReadOnlyDictionary<(ContentKind Kind, string ClassName), string> Excluded => _excluded;

    /// <summary>Mistakes found while registering (a name registered twice, a field a clone would share).
    /// The self-check fails on any.</summary>
    public IReadOnlyList<string> Problems => _problems;

    // ---------- registering ----------

    public void Card(CardDef def)
    {
        if (!Claim(ContentKind.Card, def.ClassName)) return;
        _cards[def.ClassName] = def;
        if (def.Behaviour is { } make) _problems.AddRange(ListenerFields.UnsafeFields(make().GetType()));
    }

    public void Power<T>() where T : Power, new()
    {
        if (!Claim(ContentKind.Power, typeof(T).Name)) return;
        _powers[typeof(T).Name] = static () => new T();
        _problems.AddRange(ListenerFields.UnsafeFields(typeof(T)));
    }

    public void Monster<T>() where T : Monster, new()
    {
        if (!Claim(ContentKind.Monster, typeof(T).Name)) return;
        _monsters[typeof(T).Name] = static () => new T();
        _problems.AddRange(ListenerFields.UnsafeFields(typeof(T)));
    }

    public void Encounter<T>() where T : Encounter, new()
    {
        if (Claim(ContentKind.Encounter, typeof(T).Name)) _encounters[typeof(T).Name] = static () => new T();
    }

    public void Potion(PotionDef def)
    {
        if (Claim(ContentKind.Potion, def.ClassName)) _potions[def.ClassName] = def;
    }

    public void Relic<T>() where T : Relic, new()
    {
        if (!Claim(ContentKind.Relic, typeof(T).Name)) return;
        _relics[typeof(T).Name] = static () => new T();
        _problems.AddRange(ListenerFields.UnsafeFields(typeof(T)));
    }

    public void Affliction(AfflictionDef def)
    {
        if (Claim(ContentKind.Affliction, def.ClassName)) _afflictions[def.ClassName] = def;
    }

    /// <summary>An event of a run (an event room's, or an Ancient's), under its class name. A new object is made for every room.</summary>
    public void Event<T>() where T : RunEvent, new()
    {
        if (!Claim(ContentKind.Event, typeof(T).Name)) return;
        _events[typeof(T).Name] = static () => new T();
        _problems.AddRange(EventFields.UnsafeFields(typeof(T)));
    }

    /// <summary>
    /// Leaves something out on purpose: a card, monster or potion that cannot be reproduced
    /// exactly, or that cannot occur in a single-player Ironclad fight. A wrong card is worse
    /// than a missing one. Generators skip excluded names; the self-check lists them.
    /// </summary>
    public void Exclude(ContentKind kind, string className, string reason)
    {
        if (Exists(kind, className))
            _problems.Add($"{_module} excludes {kind} {className}, which {_source[(kind, className)]} registers");
        else if (_excluded.ContainsKey((kind, className)))
            _problems.Add($"{kind} {className} is excluded twice ({_source[(kind, className)]} and {_module})");
        else
        {
            _excluded[(kind, className)] = reason;
            _source[(kind, className)] = _module;
        }
    }

    private bool Claim(ContentKind kind, string className)
    {
        if (_source.TryGetValue((kind, className), out var first))
        {
            _problems.Add(_excluded.ContainsKey((kind, className))
                ? $"{_module} registers {kind} {className}, which {first} excludes"
                : $"{kind} {className} is registered twice ({first} and {_module}); the first one is used");
            return false;
        }
        _source[(kind, className)] = _module;
        return true;
    }

    private bool Exists(ContentKind kind, string name) => kind switch
    {
        ContentKind.Card => _cards.ContainsKey(name),
        ContentKind.Power => _powers.ContainsKey(name),
        ContentKind.Monster => _monsters.ContainsKey(name),
        ContentKind.Encounter => _encounters.ContainsKey(name),
        ContentKind.Potion => _potions.ContainsKey(name),
        ContentKind.Relic => _relics.ContainsKey(name),
        ContentKind.Event => _events.ContainsKey(name),
        _ => _afflictions.ContainsKey(name),
    };

    // ---------- looking up ----------

    public bool Has(ContentKind kind, string className) => Exists(kind, className);

    public bool IsExcluded(ContentKind kind, string className, out string reason) => _excluded.TryGetValue((kind, className), out reason!);

    /// <summary>The module (file) that registered or excluded a name; "?" when unknown.</summary>
    public string SourceOf(ContentKind kind, string className) => _source.GetValueOrDefault((kind, className), "?");

    public CardDef Card(string className) => _cards.TryGetValue(className, out var def) ? def : throw Missing(ContentKind.Card, className);
    public PotionDef Potion(string className) => _potions.TryGetValue(className, out var def) ? def : throw Missing(ContentKind.Potion, className);
    public AfflictionDef Affliction(string className) => _afflictions.TryGetValue(className, out var def) ? def : throw Missing(ContentKind.Affliction, className);
    public Power NewPower(string className) => _powers.TryGetValue(className, out var make) ? make() : throw Missing(ContentKind.Power, className);
    public Monster NewMonster(string className) => _monsters.TryGetValue(className, out var make) ? make() : throw Missing(ContentKind.Monster, className);
    public Encounter NewEncounter(string className) => _encounters.TryGetValue(className, out var make) ? make() : throw Missing(ContentKind.Encounter, className);
    public Relic NewRelic(string className) => _relics.TryGetValue(className, out var make) ? make() : throw Missing(ContentKind.Relic, className);
    public RunEvent NewEvent(string className) => _events.TryGetValue(className, out var make) ? make() : throw Missing(ContentKind.Event, className);

    private KeyNotFoundException Missing(ContentKind kind, string className) =>
        new(_excluded.TryGetValue((kind, className), out var why)
            ? $"The arena leaves out {kind} '{className}': {why}"
            : $"The arena has no {kind} called '{className}'.");

    // ---------- discovery ----------

    internal static Registry Discover(Assembly assembly)
    {
        var registry = new Registry();
        var modules = assembly.GetTypes()
            .Where(t => typeof(IContentModule).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .OrderBy(t => t.Name, StringComparer.Ordinal);
        foreach (var type in modules)
        {
            registry._module = type.Name;
            try
            {
                ((IContentModule)Activator.CreateInstance(type)!).Register(registry);
            }
            catch (Exception ex)
            {
                registry._problems.Add($"{type.Name}.Register threw {ex.GetType().Name}: {ex.Message}");
            }
        }
        registry._module = "?";
        return registry;
    }
}

/// <summary>The one registry of the process, built on first use from every <see cref="IContentModule"/>.</summary>
public static class Content
{
    private static readonly Lazy<Registry> Lazy = new(() => Registry.Discover(typeof(Content).Assembly));

    public static Registry Registry => Lazy.Value;

    public static CardDef Card(string className) => Registry.Card(className);
    public static PotionDef Potion(string className) => Registry.Potion(className);
}
