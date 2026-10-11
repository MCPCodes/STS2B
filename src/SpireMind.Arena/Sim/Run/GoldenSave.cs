using System.Globalization;
using System.Text.Json;

namespace SpireMind.Arena;

/// <summary>
/// A run save of the real game (its <c>current_run.save</c>, plain JSON), read as evidence: the
/// seed, the profile's unlock state, and what the game made of them. <see cref="Compare"/>
/// rebuilds the start of the run from the seed and the unlock state alone and lists every
/// place where the result differs from the save. A port of the game's generation is right
/// when that list is empty for every save of the build.
/// <para>
/// A save does not name the build that wrote it. Only saves known to be from v0.107.1 count:
/// an older one (Mark's Regent save of May 2026, whose third act has a boss this build does
/// not know and whose player seed is the seed number plus one) follows older rules.
/// </para>
/// </summary>
public sealed class GoldenSave
{
    /// <summary>One act of the save: its id and its lists, as the entries of the game's ids ("THE_KIN_BOSS").</summary>
    public sealed record SavedAct(string Act, IReadOnlyList<string> Events, IReadOnlyList<string> NormalEncounters,
        IReadOnlyList<string> EliteEncounters, string? Boss, string? Ancient);

    /// <summary>The whole save, for readers of parts this class does not know (the saved map, the decks).</summary>
    public JsonElement Root { get; }

    public RunSeed Seed { get; }

    /// <summary>The player's seed number as saved; the game seeds the three player streams from it.</summary>
    public uint PlayerSeed { get; }

    /// <summary>The character's class name ("Ironclad").</summary>
    public string Character { get; }

    public IReadOnlyList<SavedAct> Acts { get; }

    /// <summary>The shared bag: its rarities in the saved order, each with its relics front first, as id entries ("AKABEKO").</summary>
    public IReadOnlyList<(RelicRarity Rarity, IReadOnlyList<string> Relics)> SharedBag { get; }

    public IReadOnlyList<(RelicRarity Rarity, IReadOnlyList<string> Relics)> PlayerBag { get; }

    /// <summary>How many numbers each long stream had given, by the game's stream name ("up_front", "rewards").</summary>
    public IReadOnlyDictionary<string, int> Counters { get; }

    /// <summary>The unlock state the save carries. Its discovered acts are not in a run save: every act counts as discovered.</summary>
    public RunUnlocks Unlocks { get; }

    private GoldenSave(JsonElement root)
    {
        Root = root;
        Seed = new RunSeed(root.GetProperty("rng").GetProperty("seed").GetString()!);
        var player = root.GetProperty("players")[0];
        PlayerSeed = player.GetProperty("rng").GetProperty("seed").GetUInt32();
        Character = ClassName(Entry(player.GetProperty("character_id").GetString()!), GameLists.CharacterRelics.Keys);

        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var counter in root.GetProperty("rng").GetProperty("counters").EnumerateObject()) counters[counter.Name] = counter.Value.GetInt32();
        foreach (var counter in player.GetProperty("rng").GetProperty("counters").EnumerateObject()) counters[counter.Name] = counter.Value.GetInt32();
        Counters = counters;

        Acts = root.GetProperty("acts").EnumerateArray().Select(act =>
        {
            var rooms = act.GetProperty("rooms");
            return new SavedAct(
                ClassName(Entry(act.GetProperty("id").GetString()!), GameLists.Acts.Select(a => a.ClassName)),
                Entries(rooms.GetProperty("event_ids")),
                Entries(rooms.GetProperty("normal_encounter_ids")),
                Entries(rooms.GetProperty("elite_encounter_ids")),
                OptionalEntry(rooms, "boss_id"),
                OptionalEntry(rooms, "ancient_id"));
        }).ToList();

        SharedBag = Bag(root.GetProperty("shared_relic_grab_bag"));
        PlayerBag = Bag(player.GetProperty("relic_grab_bag"));

        var unlock = player.GetProperty("unlock_state");
        var encounters = GameLists.Acts.SelectMany(a => a.Encounters).Select(e => e.ClassName).Distinct().ToDictionary(GameNames.Slug, n => n, StringComparer.Ordinal);
        Unlocks = new RunUnlocks(
            unlock.GetProperty("unlocked_epochs").EnumerateArray().Select(e => e.GetString()!),
            // Encounters the arena's act lists do not hold (event fights, encounters of older builds) decide nothing here.
            Entries(unlock.GetProperty("encounters_seen")).Where(encounters.ContainsKey).Select(e => encounters[e]),
            unlock.GetProperty("number_of_runs").GetInt32(),
            GameLists.Acts.Select(a => a.ClassName));
    }

    /// <summary>Reads a save from its JSON text.</summary>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    /// <exception cref="KeyNotFoundException">A part every run save has is missing.</exception>
    public static GoldenSave Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new GoldenSave(document.RootElement.Clone());
    }

    public static GoldenSave Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>
    /// Rebuilds the start of the run from the save's seed and unlock state and names every
    /// difference; empty when the save is rebuilt exactly. Compared: the player's seed number;
    /// the act roll; each act's event list, hallway list, elite list, boss and Ancient; the
    /// order of the rarities in both bags and each bag list; the up_front count. A bag list of a
    /// save taken later in a run has lost relics: what is left must be the generated list in
    /// the same order with some relics gone.
    /// </summary>
    public IReadOnlyList<string> Compare()
    {
        var differences = new List<string>();
        void Same<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                differences.Add(string.Create(CultureInfo.InvariantCulture, $"{what}: the save has {expected}, rebuilt {actual}"));
        }
        void SameList(IReadOnlyList<string> saved, IEnumerable<string> rebuiltClassNames, string what)
        {
            var rebuilt = rebuiltClassNames.Select(GameNames.Slug).ToList();
            if (saved.SequenceEqual(rebuilt)) return;
            var at = Enumerable.Range(0, Math.Min(saved.Count, rebuilt.Count)).FirstOrDefault(i => saved[i] != rebuilt[i], Math.Min(saved.Count, rebuilt.Count));
            differences.Add(string.Create(CultureInfo.InvariantCulture,
                $"{what}: first difference at entry {at} (the save has {saved.Count} entries{(at < saved.Count ? ", there " + saved[at] : "")}; rebuilt {rebuilt.Count}{(at < rebuilt.Count ? ", there " + rebuilt[at] : "")})"));
        }

        Same(PlayerSeed, unchecked(Seed.Hash + RunStreams.PlayerSlot), "player seed number");

        var savedActs = Acts.Select(a => a.Act).ToList();
        SameList(savedActs.Select(GameNames.Slug).ToList(), RunGeneration.RollActs(Seed, Unlocks), "acts (rolled on act_selection)");

        var streams = new RunStreams(Seed);
        var start = RunGeneration.Generate(streams, savedActs, Unlocks, Character);
        for (var i = 0; i < Acts.Count; i++)
        {
            var saved = Acts[i];
            var rebuilt = start.Rooms[i];
            var name = $"act {i + 1} ({saved.Act})";
            SameList(saved.Events, rebuilt.Events, $"{name} events");
            SameList(saved.NormalEncounters, rebuilt.NormalEncounters, $"{name} hallway fights");
            SameList(saved.EliteEncounters, rebuilt.EliteEncounters, $"{name} elites");
            Same(saved.Boss, GameNames.Slug(rebuilt.Boss), $"{name} boss");
            Same(saved.Ancient, rebuilt.Ancient is null ? null : GameNames.Slug(rebuilt.Ancient), $"{name} Ancient");
        }

        CompareBag(SharedBag, start.SharedBag, "shared bag", differences);
        CompareBag(PlayerBag, start.PlayerBag, "player bag", differences);

        if (Counters.TryGetValue(RunStreams.NameOf(RunStream.UpFront), out var upFront)) Same(upFront, start.UpFrontDraws, "up_front count");
        else differences.Add("the save has no up_front counter");
        return differences;
    }

    /// <summary>How many relics the save's bags have lost against the rebuilt ones (0 for a save taken before anything was pulled).</summary>
    public int RelicsGoneFromBags()
    {
        var start = RunGeneration.Generate(new RunStreams(Seed), Acts.Select(a => a.Act).ToList(), Unlocks, Character);
        int Gone(IReadOnlyList<(RelicRarity Rarity, IReadOnlyList<string> Relics)> saved, RelicBag rebuilt) =>
            rebuilt.Rarities.Sum(r => rebuilt.Relics(r).Count) - saved.Sum(l => l.Relics.Count);
        return Gone(SharedBag, start.SharedBag) + Gone(PlayerBag, start.PlayerBag);
    }

    private static void CompareBag(IReadOnlyList<(RelicRarity Rarity, IReadOnlyList<string> Relics)> saved, RelicBag rebuilt, string what, List<string> differences)
    {
        if (!saved.Select(l => l.Rarity).SequenceEqual(rebuilt.Rarities))
            differences.Add($"{what}: the save lists the rarities as {string.Join(", ", saved.Select(l => l.Rarity))}, rebuilt {string.Join(", ", rebuilt.Rarities)}");
        foreach (var (rarity, relics) in saved)
        {
            var full = rebuilt.Relics(rarity).Select(GameNames.Slug).ToList();
            // What is left of a list keeps its order: walk the rebuilt list once and tick the saved relics off.
            var next = 0;
            foreach (var relic in full)
                if (next < relics.Count && relics[next] == relic) next++;
            if (next == relics.Count) continue;
            differences.Add(string.Create(CultureInfo.InvariantCulture,
                $"{what}, {rarity}: the save's entry {next} ({relics[next]}) does not follow in the rebuilt order ({string.Join(" ", full)})"));
        }
    }

    private static List<(RelicRarity, IReadOnlyList<string>)> Bag(JsonElement bag) =>
        bag.GetProperty("relic_id_lists").EnumerateObject()
            .Select(list => (Enum.Parse<RelicRarity>(list.Name, ignoreCase: true), (IReadOnlyList<string>)Entries(list.Value))).ToList();

    private static List<string> Entries(JsonElement ids) => ids.EnumerateArray().Select(id => Entry(id.GetString()!)).ToList();

    private static string? OptionalEntry(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var id) && id.ValueKind == JsonValueKind.String ? Entry(id.GetString()!) : null;

    /// <summary>"ENCOUNTER.THE_KIN_BOSS" to "THE_KIN_BOSS".</summary>
    private static string Entry(string id) => id[(id.IndexOf('.') + 1)..];

    private static string ClassName(string entry, IEnumerable<string> classNames) =>
        GameNames.ClassNameOf(entry, classNames) ?? throw new KeyNotFoundException($"The save names '{entry}', which the arena's lists do not hold.");
}
