using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
// The mod compiles Core with global usings for Godot, which has its own FileAccess.
using FileAccess = System.IO.FileAccess;

namespace SpireMind.Core.Brain;

/// <summary>
/// One JSON line per fight in <c>&lt;dir&gt;/fights.jsonl</c>, built from what <see cref="ApiBrain"/>
/// already sees: the game snapshots in its tool results, and its own requests. See docs/brain.md
/// (Fight log) for the fields. A fight opens at the first combat or hand_select snapshot with the
/// whole board and closes at the first snapshot of another room's screen (rewards, game over, the
/// map...), which also gives the HP after it (the rewards screen's, so after Burning Blood's heal).
/// The deck is written once too, on its own line, at the rest site of each floor in
/// <see cref="DeckFloors"/>.
/// <para>Nothing here throws: a bad snapshot or a failed write is logged (once until a write works
/// again) and the brain carries on. Safe to call from any thread; every call takes one lock.</para>
/// </summary>
public sealed class FightLog
{
    /// <summary>The file name in the folder given to the constructor.</summary>
    public const string FileName = "fights.jsonl";

    // Names stay readable ("Jaw Worm", "Bash+"); this never goes into HTML.
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _clock;
    private Fight? _fight;
    private JsonObject? _board; // the last whole combat board of the open fight
    private readonly HashSet<string> _decksWritten = new(StringComparer.Ordinal);
    private int _lastFloor;
    private bool _failed;

    /// <param name="dir">The copy's own config folder (a bot copy has its own).</param>
    /// <param name="log">Where problems are reported.</param>
    /// <param name="clock">Time source for tests; defaults to <see cref="DateTime.UtcNow"/>.</param>
    public FightLog(string dir, Action<string>? log = null, Func<DateTime>? clock = null)
    {
        _path = Path.Combine(dir, FileName);
        _log = log ?? (_ => { });
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>The file the lines go to.</summary>
    public string FilePath => _path;

    /// <summary>The floors whose rest site gets a deck line. Floor 16 is the rest before the Act 1 boss.</summary>
    public IReadOnlySet<int> DeckFloors { get; init; } = new HashSet<int> { 16 };

    /// <summary>True while a fight is open.</summary>
    public bool InFight
    {
        get { lock (_gate) return _fight is not null; }
    }

    /// <summary>
    /// A tool result or a snapshot: the snapshot itself, or an act or act_sequence result with
    /// its <c>next</c> (or the <c>current</c> it was refused against). An act's <c>done</c> ids
    /// count the potions used and discarded, by the slot's name on the board before it.
    /// </summary>
    public void Observe(JsonNode? payload)
    {
        try
        {
            lock (_gate)
                ObserveLocked(payload as JsonObject);
        }
        catch (Exception ex)
        {
            Report($"fights: couldn't read a snapshot ({ex.GetType().Name}: {ex.Message})");
        }
    }

    /// <summary>One response the brain got: its cost, the effort it was asked at, and whether
    /// it hit max_tokens. Counted only while a fight is open.</summary>
    public void Response(decimal usd, string? effort, bool cutOff)
    {
        lock (_gate)
        {
            if (_fight is not { } fight)
                return;
            fight.Requests++;
            fight.SpentUsd += usd;
            if (cutOff)
                fight.CutOffs++;
            var level = string.IsNullOrWhiteSpace(effort) ? "default" : effort;
            if (!fight.Efforts.Contains(level))
                fight.Efforts.Add(level);
        }
    }

    /// <summary>The brain started a fresh conversation. <paramref name="refresh"/> is true when
    /// it did so to save tokens (the conversation got long, the context window filled up or a
    /// refusal), not for a new run, a model or effort change or a fight room's effort.</summary>
    public void ConversationStarted(bool refresh)
    {
        lock (_gate)
        {
            if (_fight is not { } fight)
                return;
            fight.Conversations++;
            if (refresh)
                fight.Refreshes++;
        }
    }

    // ---- Snapshots ----

    private void ObserveLocked(JsonObject? payload)
    {
        if (payload is null)
            return;
        var snapshot = payload.ContainsKey("screen") ? payload
            : payload["next"] is JsonObject next && next.ContainsKey("screen") ? next
            : payload["current"] is JsonObject current && current.ContainsKey("screen") ? current
            : null;

        // Potions used or discarded, looked up on the board the ids were chosen from.
        if (_fight is { } open && _board is not null)
            foreach (var id in DoneIds(payload["done"]))
                CountPotion(open, id);

        if (snapshot is null)
            return;
        var screen = Str(snapshot["screen"]);
        var state = snapshot["state"] as JsonObject;

        // A lower floor than before means a new run, whose rest sites get deck lines again.
        if (Int((state?["run"] as JsonObject)?["floor"]) is { } seenFloor)
        {
            if (seenFloor < _lastFloor)
                _decksWritten.Clear();
            _lastFloor = seenFloor;
        }

        if (screen is "combat" or "hand_select")
        {
            if (state is null)
                return;
            if (IsBoard(state))
            {
                var run = state["run"] as JsonObject;
                var act = Int(run?["act"]);
                var floor = Int(run?["floor"]);
                // A combat board from another floor: the last fight was never seen to end.
                if (_fight is { } stale && (stale.Act != act || stale.Floor != floor))
                    Close(null, "unseen");
                _fight ??= Open(state, act, floor);
                TakeBoard(_fight, state);
            }
            else if (_fight is { } fight)
            {
                // A waiting combat snapshot: {turn, round, hp, block}.
                if (Int(state["turn"]) is { } turn && turn > fight.Turns)
                    fight.Turns = turn;
                if (Int(state["hp"]) is { } hp)
                    fight.LastHp = hp;
            }
            return;
        }

        // A card choice mid-fight carries a combat summary and keeps the fight open; so does a
        // modal, an unknown or error screen, or a busy snapshot.
        if (screen is "card_select" && state?["combat"] is JsonObject)
            return;
        if (_fight is not null && EndsFight(screen))
            Close(snapshot, screen!);

        if (screen == "rest" && state is not null)
            WriteDeck(state);
        if (screen == "menu")
            _decksWritten.Clear();
    }

    // The whole board: deciding combat and hand_select snapshots carry the run block and the player creature.
    private static bool IsBoard(JsonObject state) => state["run"] is JsonObject && state["me"] is JsonObject;

    // Screens of another room or of leaving the run.
    private static bool EndsFight(string? screen) => screen is
        "rewards" or "card_reward" or "card_select" or "map" or "shop" or "rest" or "event"
        or "treasure" or "crystal_sphere" or "game_over" or "menu";

    private Fight Open(JsonObject state, int? act, int? floor)
    {
        var me = state["me"] as JsonObject;
        return new Fight
        {
            Start = _clock(),
            Act = act,
            Floor = floor,
            Room = Str(state["run"]?["room"]),
            Character = Str(me?["name"]),
            HpBefore = Int(me?["hp"]),
            MaxHpBefore = Int(me?["max_hp"]),
            FirstTurn = Int(state["turn"]),
        };
    }

    private void TakeBoard(Fight fight, JsonObject state)
    {
        _board = state;
        if (Int(state["turn"]) is { } turn && turn > fight.Turns)
            fight.Turns = turn;
        if (state["me"] is JsonObject me && Int(me["hp"]) is { } hp)
            fight.LastHp = hp;
        if (state["enemies"] is JsonArray enemies)
            foreach (var enemy in enemies.OfType<JsonObject>())
                if (Int(enemy["i"]) is { } i && Str(enemy["name"]) is { } name)
                    fight.Enemies.TryAdd(i, name);
        fight.LastPotions = PotionNames(state["potions"]);
    }

    private void CountPotion(Fight fight, string id)
    {
        var used = id.StartsWith("potion:", StringComparison.Ordinal);
        var discarded = id.StartsWith("discard_potion:", StringComparison.Ordinal);
        if (!used && !discarded)
            return;
        var rest = id[(id.IndexOf(':') + 1)..];
        var cut = rest.IndexOf('>');
        var slotText = cut >= 0 ? rest[..cut] : rest;
        var name = int.TryParse(slotText, NumberStyles.None, CultureInfo.InvariantCulture, out var slot)
                   && _board?["potions"] is JsonArray slots && slot < slots.Count
            ? Str(slots[slot]?["name"])
            : null;
        (used ? fight.PotionsUsed : fight.PotionsDiscarded).Add(name ?? $"slot {slotText}");
    }

    // The ids an act ("done": "id") or act_sequence ("done": [{"id", "label"}]) ran.
    private static IEnumerable<string> DoneIds(JsonNode? done) => done switch
    {
        JsonValue v when v.TryGetValue<string>(out var id) => [id],
        JsonArray steps => steps.Select(s => s is JsonObject o ? Str(o["id"]) : Str(s)).OfType<string>().ToArray(),
        _ => [],
    };

    private void Close(JsonObject? after, string endedOn)
    {
        var fight = _fight!;
        _fight = null;
        _board = null;
        var state = after?["state"] as JsonObject;
        var player = state?["player"] as JsonObject;

        // HP after: top level on the rest site, in player elsewhere; the last seen in the fight otherwise.
        var hpAfter = Int(state?["hp"]) ?? Int(player?["hp"]) ?? fight.LastHp;
        var maxHpAfter = Int(state?["max_hp"]) ?? Int(player?["max_hp"]) ?? fight.MaxHpBefore;
        // Potions held: the detailed slots at top level, the name list in player, or the fight's last board.
        var held = state?["potions"] is JsonArray detailed ? PotionNames(detailed)
            : player?["potions"] is JsonArray names ? PotionNames(names)
            : fight.LastPotions;
        var result = endedOn switch
        {
            // The game over reader says victory, defeat, abandoned (from the pause menu) or unknown.
            "game_over" => Str(state?["result"]) switch
            {
                "victory" => "won",
                "defeat" => "lost",
                "abandoned" => "abandoned",
                _ => hpAfter is 0 ? "lost" : "unknown",
            },
            "menu" or "unseen" => "unknown",
            _ => hpAfter is 0 ? "lost" : "won",
        };

        var line = new JsonObject
        {
            ["kind"] = "fight",
            ["id"] = FightId(fight.Start, fight.Act, fight.Floor),
            ["character"] = fight.Character,
            ["act"] = fight.Act,
            ["floor"] = fight.Floor,
            ["room"] = fight.Room,
            ["enemies"] = Strings(fight.Enemies.OrderBy(e => e.Key).Select(e => e.Value)),
            ["result"] = result,
            ["ended_on"] = endedOn,
            ["hp_before"] = fight.HpBefore,
            ["max_hp_before"] = fight.MaxHpBefore,
            ["hp_after"] = hpAfter,
            ["max_hp_after"] = maxHpAfter,
            ["turns"] = fight.Turns,
            ["seen_from_turn"] = fight.FirstTurn,
            ["potions_used"] = Strings(fight.PotionsUsed),
            ["potions_discarded"] = Strings(fight.PotionsDiscarded),
            ["potions_held"] = Strings(held),
            ["effort"] = Strings(fight.Efforts),
            ["requests"] = fight.Requests,
            ["refreshes"] = fight.Refreshes,
            ["conversations"] = fight.Conversations,
            ["cut_offs"] = fight.CutOffs,
            ["spent_usd"] = Math.Round(fight.SpentUsd, 4),
            ["started_at"] = fight.Start.ToString("O", CultureInfo.InvariantCulture),
        };
        Write(line);
    }

    // The deck line at a rest site on one of DeckFloors, once per floor until the next run.
    private void WriteDeck(JsonObject state)
    {
        var run = state["run"] as JsonObject;
        if (Int(run?["floor"]) is not { } floor || !DeckFloors.Contains(floor))
            return;
        if (state["player"] is not JsonObject player || player["deck"] is not JsonObject deck)
            return;
        var act = Int(run?["act"]);
        if (!_decksWritten.Add($"{act}/{floor}"))
            return;
        Write(new JsonObject
        {
            ["kind"] = "deck",
            ["id"] = FightId(_clock(), act, floor),
            ["character"] = Str(player["character"]),
            ["act"] = act,
            ["floor"] = floor,
            ["hp"] = Int(state["hp"]),
            ["max_hp"] = Int(state["max_hp"]),
            ["gold"] = Int(player["gold"]),
            ["deck_size"] = Int(player["deck_size"]),
            ["deck"] = deck.DeepClone(),
            ["relics"] = player["relics"]?.DeepClone(),
            ["potions"] = Strings(PotionNames(state["potions"])),
        });
    }

    /// <summary>The id of a fight (or a deck line): the player's local date, the act and the
    /// floor, as in "2026-10-11 A1 F7". A lesson can quote it as its source.</summary>
    public static string FightId(DateTime when, int? act, int? floor) =>
        string.Create(CultureInfo.InvariantCulture, $"{ApiBrain.Today(when):yyyy-MM-dd} A{act?.ToString(CultureInfo.InvariantCulture) ?? "?"} F{floor?.ToString(CultureInfo.InvariantCulture) ?? "?"}");

    // ---- Writing ----

    private void Write(JsonObject line)
    {
        try
        {
            var record = new JsonObject { ["at"] = _clock().ToString("O", CultureInfo.InvariantCulture) };
            foreach (var (key, value) in line)
                record[key] = value?.DeepClone();
            var text = record.ToJsonString(Json) + "\n";
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using (var file = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                file.Write(bytes, 0, bytes.Length);
            }
            _failed = false;
        }
        catch (Exception ex)
        {
            Report($"fights: couldn't write {FileName} ({ex.GetType().Name}: {ex.Message})");
        }
    }

    // Logged once until a write works again; a log that throws is ignored.
    private void Report(string line)
    {
        bool first;
        lock (_gate)
        {
            first = !_failed;
            _failed = true;
        }
        if (!first)
            return;
        try { _log(line); }
        catch (Exception) { /* nowhere left to report it */ }
    }

    // ---- Helpers ----

    // Potion names from detailed slots ({"slot", "name"...} or null) or a name list; empty slots left out.
    private static List<string> PotionNames(JsonNode? slots) =>
        slots is JsonArray list
            ? list.Select(p => p is JsonObject o ? Str(o["name"]) : Str(p)).OfType<string>().ToList()
            : [];

    private static JsonArray Strings(IEnumerable<string> values) =>
        new(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? Int(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l) && l is >= int.MinValue and <= int.MaxValue) return (int)l;
        if (v.TryGetValue<double>(out var d) && double.IsFinite(d) && Math.Abs(d) < int.MaxValue) return (int)d;
        return null;
    }

    private sealed class Fight
    {
        public DateTime Start;
        public int? Act;
        public int? Floor;
        public string? Room;
        public string? Character;
        public int? HpBefore;
        public int? MaxHpBefore;
        public int? LastHp;
        public int? FirstTurn;
        public int Turns;
        public readonly SortedDictionary<int, string> Enemies = new();
        public readonly List<string> PotionsUsed = [];
        public readonly List<string> PotionsDiscarded = [];
        public List<string> LastPotions = [];
        public readonly List<string> Efforts = [];
        public int Requests;
        public int Refreshes;
        public int Conversations;
        public int CutOffs;
        public decimal SpentUsd;
    }
}
