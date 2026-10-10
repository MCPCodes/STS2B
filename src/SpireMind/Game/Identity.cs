using System.Text.Json.Nodes;

namespace SpireMind.Game;

/// <summary>
/// Keeps the chat on the character this bot plays: <see cref="ChatHub.BotName"/>, so it answers
/// to "Ironclad" in an Ironclad run; <see cref="ChatHub.Persona"/>, for get_persona (name,
/// pronouns, tone and some of the character's own lines from the game); and
/// <see cref="NameColor"/>, for its name in the chat log. At character select that is the
/// character just picked, so "Silent, go poison this run" reaches this bot once Silent is
/// picked. With nothing picked or Random, and anywhere else outside a run, there is no name and
/// every message is for this bot. Game details: docs/chat-and-persona.md, "Persona".
/// </summary>
internal static class Identity
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private const int MaxVoiceLines = 15;

    /// <summary>Longer lines are monologues; the persona wants the feel of the voice, not speeches.</summary>
    private const int MaxVoiceLength = 160;

    /// <summary>Highest dialogue and line numbers probed in the Ancient keys.</summary>
    private const int MaxDialogue = 4;
    private const int MaxLine = 7;

    /// <summary>The name the bot answers to, by Character.Id.Entry. Other ids use the title without "The ".</summary>
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IRONCLAD"] = "Ironclad",
        ["SILENT"] = "Silent",
        ["DEFECT"] = "Defect",
        ["REGENT"] = "Regent",
        ["NECROBINDER"] = "Necrobinder",
    };

    /// <summary>How each character talks, paraphrased from the game's own lines (docs/chat-and-persona.md, "Tone, paraphrased").</summary>
    private static readonly Dictionary<string, string> Tones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IRONCLAD"] = "A terse, grim soldier.",
        ["SILENT"] = "An almost wordless huntress who speaks in ellipses.",
        ["DEFECT"] = "A chirping, beeping automaton that refers to itself as \"it\".",
        ["NECROBINDER"] = "A vengeful but darkly funny young lich, with Osty, her animated hand.",
        ["REGENT"] = "A loud, pompous royal who refers to himself as \"his Majesty\".",
    };

    /// <summary>Keys of the characters table in the character's own voice, after "ID.". Never epoch lore: it spoils the timeline.</summary>
    private static readonly string[] OwnKeys =
        ["goldMonologue", "aromaPrinciple", "eventDeathPrevention", "banter.alive.endTurnPing", "banter.dead.endTurnPing"];

    /// <summary>The Ancients with lines for each character in the ancients table. THE_ARCHITECT holds the confrontation.</summary>
    private static readonly string[] Ancients =
        ["NEOW", "OROBAS", "PAEL", "TEZCATARA", "NONUPEIPE", "TANX", "VAKUU", "DARV", "THE_ARCHITECT"];

    /// <summary>Voice lines by character id, read once each; empty when the tables had none.</summary>
    private static readonly Dictionary<string, List<string>> Voices = new(StringComparer.OrdinalIgnoreCase);

    private static DateTime _nextRead;

    /// <summary>Id of the character the hub is on; null when there is none.</summary>
    private static string? _current;

    /// <summary>The current character's NameColor for its name in the chat log; null when there is no character or it can't be read.</summary>
    public static Color? NameColor { get; private set; }

    /// <summary>A character as read from the game. Pronouns that couldn't be read are empty.</summary>
    private sealed record Seen(string Id, string Name, string Title, (string Subject, string Object, string Possessive) Pronouns, Color? NameColor);

    /// <summary>Called every frame on the main thread; reads the game at most once a second.</summary>
    public static void Poll(ChatHub hub)
    {
        var now = DateTime.UtcNow;
        if (now < _nextRead) return;
        _nextRead = now + Interval;

        var (known, seen) = Read();
        if (!known) return;
        if (seen is null)
        {
            if (_current is null) return;
            _current = null;
            NameColor = null;
            hub.BotName = null;
            hub.Persona = null;
            ModEntry.Log("chat: no character, answering to any name");
            return;
        }

        // The same character again needs nothing, unless its voice lines are still to be read
        // because the loc tables weren't loaded last time.
        if (seen.Id == _current && (Voices.ContainsKey(seen.Id) || !LocReady())) return;

        var changed = seen.Id != _current;
        _current = seen.Id;
        NameColor = seen.NameColor;
        hub.BotName = seen.Name;
        hub.Persona = Persona(seen, VoiceLines(seen.Id));
        if (changed) ModEntry.Log($"chat: answering as {seen.Name}");
    }

    /// <summary>
    /// The character the local player plays in a run, or has picked at character select. Known
    /// is false while a run loads or tears down (no player yet, or a read failed), so the name
    /// stays put; known with no character means there is no name to answer to.
    /// </summary>
    private static (bool Known, Seen? Character) Read()
    {
        try
        {
            // The main menu wins over RunManager, as in ScreenRouter: a run being set up at
            // character select already counts as in progress, still with no player to read.
            var ctx = GameContext.Capture();
            var inRun = ctx.InRun && NGame.Instance?.MainMenu is null;
            var select = inRun ? null : CharacterSelect();
            if (!inRun && select is null) return (true, null);

            // LocalPlayer is a struct copy, so it is read afresh every time. VERIFY:
            // LobbyPlayer.character is the same model type as Player.Character.
            var ch = inRun ? ctx.Me?.Character : select?.Lobby?.LocalPlayer.character;
            if (ch is null) return (!inRun, null);

            // Random is only decided when the run starts; until then any name may be the one.
            var id = ch.Id.Entry ?? "";
            if (id.Contains("RANDOM", StringComparison.OrdinalIgnoreCase)) return (true, null);

            var title = Describe.SafeText(() => ch.Title);
            var name = Names.TryGetValue(id, out var mapped) ? mapped : WithoutThe(title);
            if (name.Length == 0) return (!inRun, null);

            var pronouns = (
                Describe.SafeText(() => ch.PronounSubject),
                Describe.SafeText(() => ch.PronounObject),
                Describe.SafeText(() => ch.PossessiveAdjective));
            return (true, new Seen(id.Length > 0 ? id : name.ToUpperInvariant(), name, title, pronouns, ColorOf(() => ch.NameColor)));
        }
        catch
        {
            // Loading or tearing down a run; try again next second.
            return (false, null);
        }
    }

    /// <summary>
    /// The character select screen while it is up with a lobby, else null. The screen is
    /// created before it is shown, so it counts only on top of the submenu stack.
    /// </summary>
    private static NCharacterSelectScreen? CharacterSelect()
    {
        try
        {
            // VERIFY: SubmenuStack.Peek() returns the top submenu, and may throw when the stack is empty.
            return NGame.Instance?.MainMenu?.SubmenuStack?.Peek() is NCharacterSelectScreen { Lobby: not null } screen ? screen : null;
        }
        catch
        {
            // Nothing on the stack: the main menu itself.
            return null;
        }
    }

    /// <summary>The character's NameColor. VERIFY: Character.NameColor is a Godot Color; anything else reads as none.</summary>
    private static Color? ColorOf(Func<object?> nameColor)
    {
        try { return nameColor() is Color color ? color : null; }
        catch { return null; }
    }

    private static string WithoutThe(string title) =>
        (title.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? title[4..] : title).Trim();

    /// <summary>What get_persona hands the model (docs/chat.md). Anything that couldn't be read is left out.</summary>
    private static JsonObject Persona(Seen seen, List<string>? voice)
    {
        var json = new JsonObject { ["name"] = seen.Name };
        if (seen.Title.Length > 0) json["title"] = seen.Title;
        json["id"] = seen.Id;

        var pronouns = new JsonObject();
        if (seen.Pronouns.Subject.Length > 0) pronouns["subject"] = seen.Pronouns.Subject;
        if (seen.Pronouns.Object.Length > 0) pronouns["object"] = seen.Pronouns.Object;
        if (seen.Pronouns.Possessive.Length > 0) pronouns["possessive"] = seen.Pronouns.Possessive;
        if (pronouns.Count > 0) json["pronouns"] = pronouns;

        if (Tones.TryGetValue(seen.Id, out var tone)) json["tone"] = tone;
        if (voice is { Count: > 0 })
            json["voice_lines"] = new JsonArray(voice.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray());
        return json;
    }

    // ---------- voice lines ----------

    /// <summary>
    /// Up to <see cref="MaxVoiceLines"/> of the character's own lines from the loc tables, read
    /// once per character. Null while the tables aren't loaded (LocManager comes up after the
    /// mods), so a later poll reads them.
    /// </summary>
    private static List<string>? VoiceLines(string id)
    {
        if (Voices.TryGetValue(id, out var cached)) return cached;
        if (!LocReady()) return null;
        var lines = Pick(ReadVoice(id));
        Voices[id] = lines;
        ModEntry.Log($"chat: {lines.Count} voice lines for {id}");
        return lines;
    }

    private static bool LocReady()
    {
        // VERIFY: LocManager (Localization namespace, beside LocString) has a static Instance.
        try { return LocManager.Instance is not null; }
        catch { return false; }
    }

    /// <summary>
    /// The character's lines, one list per source so <see cref="Pick"/> can mix them: its own
    /// keys in the characters table, then its lines to each Ancient, keyed
    /// "ANCIENT.talk.ID.dialogue-line.char" with numbers that are probed.
    /// </summary>
    private static List<List<string>> ReadVoice(string id)
    {
        var own = new List<string>();
        foreach (var key in OwnKeys)
            if (Raw("characters", $"{id}.{key}") is { } raw)
                AddUsable(own, raw);
        var sources = new List<List<string>> { own };

        foreach (var ancient in Ancients)
        {
            var talk = new List<string>();
            // Numbers may start at 0 or 1, and the Ancient's own lines may sit between the
            // character's, so a range ends at its second miss in a row rather than its first.
            for (int dialogue = 0, emptyRows = 0; dialogue <= MaxDialogue && emptyRows < 2; dialogue++)
            {
                var found = 0;
                for (int line = 0, misses = 0; line <= MaxLine && misses < 2; line++)
                {
                    if (Raw("ancients", $"{ancient}.talk.{id}.{dialogue}-{line}.char") is not { } raw)
                    {
                        misses++;
                        continue;
                    }
                    misses = 0;
                    found++;
                    AddUsable(talk, raw);
                }
                emptyRows = found > 0 ? 0 : emptyRows + 1;
            }
            sources.Add(talk);
        }
        return sources;
    }

    /// <summary>A loc table entry as written, or null when the key is missing.</summary>
    private static string? Raw(string table, string key)
    {
        try
        {
            // Most probed keys don't exist, so check first, as the docs say, rather than let
            // GetRawText complain dozens of times per character. VERIFY: LocString.Exists(string
            // table, string key).
            if (!LocString.Exists(table, key)) return null;
            // VERIFY: LocManager.GetTable(string) and the table's GetRawText(string) returning the
            // text as written. Should a missing key still get through, it may throw, or come back
            // empty or as the key itself.
            var raw = LocManager.Instance.GetTable(table).GetRawText(key);
            return string.IsNullOrWhiteSpace(raw) || raw.Contains(key, StringComparison.Ordinal) ? null : raw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Adds a line as plain text, unless nothing is left of it or it has a placeholder only the game fills in.</summary>
    private static void AddUsable(List<string> lines, string raw)
    {
        var line = TextCleaner.Clean(raw);
        if (line.Length > 0 && !line.Contains('{')) lines.Add(line);
    }

    /// <summary>
    /// Up to <see cref="MaxVoiceLines"/> lines with variety: one from each source in turn,
    /// shortest first within a source, skipping long lines and repeats.
    /// </summary>
    private static List<string> Pick(List<List<string>> sources)
    {
        var queues = sources
            .Select(s => new Queue<string>(s.Where(l => l.Length <= MaxVoiceLength).OrderBy(l => l.Length)))
            .Where(q => q.Count > 0)
            .ToList();
        var picked = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (picked.Count < MaxVoiceLines && queues.Any(q => q.Count > 0))
        {
            foreach (var queue in queues)
            {
                while (queue.TryDequeue(out var line))
                {
                    if (!seen.Add(line)) continue;
                    picked.Add(line);
                    break;
                }
                if (picked.Count == MaxVoiceLines) break;
            }
        }
        return picked;
    }
}
