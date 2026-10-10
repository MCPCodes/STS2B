using System.Collections.Concurrent;
using System.Reflection;

namespace SpireMind.Arena;

/// <summary>
/// The player or one monster: HP, Block and powers in the order they were first applied
/// (which is the order they hear hooks in).
/// </summary>
public sealed class Creature
{
    private readonly List<Power> _powers = new();

    internal Creature(int id, Side side, string name, int hp, int maxHp, Monster? monster, string? slot)
    {
        Id = id;
        Side = side;
        Name = name;
        Hp = hp;
        MaxHp = maxHp;
        Monster = monster;
        Slot = slot;
    }

    /// <summary>Combat id: 0 for the player, then every monster in the order it was created.</summary>
    public int Id { get; }

    public Side Side { get; }

    /// <summary>Display name: the character's title ("The Ironclad"), or the monster's English name.</summary>
    public string Name { get; internal set; }

    public int Hp { get; internal set; }
    public int MaxHp { get; internal set; }
    public int Block { get; internal set; }

    /// <summary>Null for the player.</summary>
    public Monster? Monster { get; internal set; }

    /// <summary>The encounter slot a monster stands in, when the encounter names slots.</summary>
    public string? Slot { get; }

    /// <summary>The j of "e&lt;j&gt;": the index the snapshots give this enemy for the whole fight. -1 for the player.</summary>
    public int RosterIndex { get; internal set; } = -1;

    /// <summary>False once a monster was removed from the fight (dead and gone, or escaped).
    /// A creature that is not in combat hears no hooks and cannot receive powers.</summary>
    public bool InCombat { get; internal set; } = true;

    public bool IsPlayer => Monster is null;
    public bool IsEnemy => Side == Side.Enemy;
    public bool IsAlive => Hp > 0;
    public bool IsDead => Hp <= 0;

    /// <summary>Powers in the order they were first applied.</summary>
    public IReadOnlyList<Power> Powers => _powers;

    internal List<Power> PowerList => _powers;

    public T? GetPower<T>() where T : Power
    {
        foreach (var p in _powers)
            if (p is T t) return t;
        return null;
    }

    /// <summary>The power with this class name ("CrueltyPower"), for content that must not reference another file's class.</summary>
    public Power? GetPower(string className)
    {
        foreach (var p in _powers)
            if (p.ClassName == className) return p;
        return null;
    }

    public bool HasPower<T>() where T : Power => GetPower<T>() is not null;
    public bool HasPower(string className) => GetPower(className) is not null;

    /// <summary>The power's amount, 0 when the creature does not have it.</summary>
    public int PowerAmount<T>() where T : Power => GetPower<T>()?.Amount ?? 0;
    public int PowerAmount(string className) => GetPower(className)?.Amount ?? 0;

    /// <summary>An enemy without a Minion-like power: the fight goes on while one of these lives.</summary>
    public bool IsPrimaryEnemy => Side == Side.Enemy && !IsSecondaryEnemy;

    public bool IsSecondaryEnemy
    {
        get
        {
            if (Side != Side.Enemy) return false;
            foreach (var p in _powers)
                if (p.OwnerIsSecondaryEnemy) return true;
            return false;
        }
    }

    public override string ToString() => $"{Name}#{Id} {Hp}/{MaxHp}";

    internal Creature CloneFor(CloneMap map)
    {
        var copy = new Creature(Id, Side, Name, Hp, MaxHp, null, Slot)
        {
            Block = Block, RosterIndex = RosterIndex, InCombat = InCombat,
        };
        map.Add(this, copy);
        foreach (var p in _powers) copy._powers.Add(p.CloneFor(map));
        if (Monster is not null) copy.Monster = Monster.CloneFor(map);
        return copy;
    }
}

/// <summary>
/// Old object to new object, for one clone of a fight. Listener fields that hold creatures,
/// cards or powers are pointed at the clone's objects through it.
/// </summary>
public sealed class CloneMap
{
    private readonly Dictionary<object, object> _map = new(ReferenceEqualityComparer.Instance);
    private readonly List<(Listener Old, Listener New)> _listeners = new();

    internal void Add(object old, object copy)
    {
        _map[old] = copy;
        if (old is Listener l) _listeners.Add((l, (Listener)copy));
    }

    /// <summary>The clone's counterpart of <paramref name="old"/>; null stays null. An object the
    /// clone does not know (a card that left the fight) maps to itself.</summary>
    public T? Get<T>(T? old) where T : class
    {
        if (old is null) return null;
        return _map.TryGetValue(old, out var copy) ? (T)copy : old;
    }

    internal bool TryGet(object old, out object? copy) => _map.TryGetValue(old, out copy);

    /// <summary>Second pass, once every object has its copy: remap reference fields, then let listeners copy their own lists.</summary>
    internal void Finish()
    {
        foreach (var (_, copy) in _listeners)
        {
            switch (copy)
            {
                case Power p:
                    p.Owner = Get(p.Owner)!;
                    p.Applier = Get(p.Applier);
                    break;
                case Monster m:
                    m.Creature = Get(m.Creature)!;
                    m.FinishClone(this);
                    break;
            }
            ListenerFields.Remap(copy, this);
            copy.OnCloned(this);
        }
    }
}

/// <summary>
/// Which fields of a listener class point at engine objects. Found once per class by
/// reflection, so content can keep a plain <c>Creature</c> or <c>CardInstance</c> field and
/// still survive a clone.
/// </summary>
internal static class ListenerFields
{
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> Cache = new();

    private static readonly Type[] Roots = [typeof(Listener), typeof(Power), typeof(Monster), typeof(Relic), typeof(CardBehaviour), typeof(object)];

    private static bool IsEngineObject(Type t) =>
        t == typeof(Creature) || t == typeof(CardInstance) || typeof(Listener).IsAssignableFrom(t);

    public static void Remap(Listener copy, CloneMap map)
    {
        var fields = Cache.GetOrAdd(copy.GetType(), Find);
        foreach (var field in fields)
        {
            var old = field.GetValue(copy);
            if (old is not null && map.TryGet(old, out var mapped)) field.SetValue(copy, mapped);
        }
    }

    /// <summary>Fields declared by content classes only; the engine's own base fields are remapped by hand.</summary>
    private static FieldInfo[] Find(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var found = new List<FieldInfo>();
        for (var t = type; t is not null && !Roots.Contains(t); t = t.BaseType)
            found.AddRange(t.GetFields(flags).Where(f => IsEngineObject(f.FieldType)));
        return found.ToArray();
    }

    /// <summary>
    /// Fields a clone would silently share between two fights: mutable reference types that are
    /// neither engine objects nor copied by an <see cref="Listener.OnCloned"/> override. The
    /// self-check reports them, since such a bug shows only as a search player going wrong.
    /// </summary>
    public static IEnumerable<string> UnsafeFields(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var overridesOnCloned = false;
        for (var t = type; t is not null && !Roots.Contains(t); t = t.BaseType)
            overridesOnCloned |= t.GetMethod(nameof(Listener.OnCloned), flags) is not null;

        for (var t = type; t is not null && !Roots.Contains(t); t = t.BaseType)
        {
            foreach (var f in t.GetFields(flags))
            {
                var ft = f.FieldType;
                if (ft.IsValueType || ft == typeof(string) || IsEngineObject(ft)) continue;
                if (typeof(Delegate).IsAssignableFrom(ft)) continue;
                if (ft == typeof(CardDef) || ft == typeof(PotionDef)) continue; // shared, never changed
                if (overridesOnCloned) continue;
                yield return $"{type.Name}.{f.Name} ({ft.Name}) is a mutable reference field: copy it in OnCloned";
            }
        }
    }
}
