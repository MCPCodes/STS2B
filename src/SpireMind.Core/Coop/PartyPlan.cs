using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpireMind.Core.Coop;

/// <summary>
/// One bot's part of a planning session, as party_pitch takes it: say (its line to the party),
/// role, archetype, the cards and relics it wants, what it needs from and gives to teammates,
/// party calls and a question for the human. <see cref="Read"/> checks the limits and cuts
/// anything longer.
/// </summary>
public sealed record PartyPitch(string Say, string? Role, string? Archetype, IReadOnlyList<string> Wants,
    IReadOnlyList<string> Needs, IReadOnlyList<string> Gives, IReadOnlyList<string> Calls, string? Question)
{
    public const int MaxSay = 200;
    public const int MaxArchetype = 60;
    public const int MaxItems = 4;
    public const int MaxItem = 80;
    public const int MaxQuestion = 150;

    public static IReadOnlyList<string> Roles { get; } = ["debuffer", "protector", "damage", "support", "flex"];

    /// <summary>The pitch in <paramref name="json"/> with every field cut to its limit, or null
    /// with <paramref name="error"/> when say is missing or empty. An unknown role counts as flex.</summary>
    public static PartyPitch? Read(JsonObject json, out string? error)
    {
        error = null;
        var say = Clean(Str(json["say"]), MaxSay);
        if (say is null)
        {
            error = "say is required: your line to the party.";
            return null;
        }
        var role = Clean(Str(json["role"]), 20)?.ToLowerInvariant();
        if (role is not null && !Roles.Contains(role))
            role = "flex";
        return new PartyPitch(say, role, Clean(Str(json["archetype"]), MaxArchetype),
            Items(json["wants"]), Items(json["needs"]), Items(json["gives"]), Items(json["calls"]),
            Clean(Str(json["question"]), MaxQuestion));
    }

    public JsonObject ToJson()
    {
        var json = new JsonObject { ["say"] = Say };
        if (Role is not null) json["role"] = Role;
        if (Archetype is not null) json["archetype"] = Archetype;
        if (Wants.Count > 0) json["wants"] = Array(Wants);
        if (Needs.Count > 0) json["needs"] = Array(Needs);
        if (Gives.Count > 0) json["gives"] = Array(Gives);
        if (Calls.Count > 0) json["calls"] = Array(Calls);
        if (Question is not null) json["question"] = Question;
        return json;
    }

    internal static JsonArray Array(IEnumerable<string> items) => new(items.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray());

    private static IReadOnlyList<string> Items(JsonNode? node)
    {
        if (node is JsonValue single && single.TryGetValue<string>(out var one))
            return Clean(one, MaxItem) is { } item ? [item] : [];
        if (node is not JsonArray list)
            return [];
        return list.Select(i => Clean(Str(i), MaxItem)).OfType<string>().Take(MaxItems).ToArray();
    }

    /// <summary>Whitespace collapsed, cut to <paramref name="max"/> with "...", null when empty.</summary>
    internal static string? Clean(string? text, int max)
    {
        var clean = Spaces().Replace(text ?? "", " ").Trim();
        if (clean.Length == 0)
            return null;
        return clean.Length <= max ? clean : clean[..(max - 3)].TrimEnd() + "...";
    }

    internal static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static readonly Regex SpacesPattern = new(@"\s+", RegexOptions.CultureInvariant);
    private static Regex Spaces() => SpacesPattern;
}

/// <summary>A question a bot asked the human in a session, with his answer (his first line after
/// it), or what the bot guessed in the final round when he didn't answer.</summary>
public sealed record PartyQuestion(string Bot, string Text, string? Answer, string? Guess);

/// <summary>What one finished session adds to the plan: the finals in speaking order, the
/// human's lines and the questions.</summary>
public sealed record PartySessionResult(string Key, string Kind, int Act, string Status, DateTime At, string? Boss,
    IReadOnlyList<(string Label, PartyPitch Pitch)> Finals, IReadOnlyList<string> HumanLines, IReadOnlyList<PartyQuestion> Questions);

/// <summary>
/// The shared party plan: one section per bot (role, archetype, what it takes, needs and gives,
/// and the session it comes from), the party's calls, the boss plan, the human's lines and the
/// questions. <see cref="Merge"/> folds a finished session in; <see cref="Render"/> writes it as
/// markdown of at most <see cref="MaxBytes"/>, dropping the human's oldest lines first and then
/// the oldest questions. Not thread-safe: the conductor keeps it under its lock.
/// </summary>
public sealed class PartyPlan
{
    public const int MaxBytes = 4096;
    private const int KeptHumanLines = 8;
    private const int KeptQuestions = 6;
    private const int KeptCalls = 8;

    public sealed class Section
    {
        public required string Label { get; init; }
        public string? Role { get; set; }
        public string? Archetype { get; set; }
        public List<string> Takes { get; set; } = [];
        public List<string> Needs { get; set; } = [];
        public List<string> Gives { get; set; } = [];
        public string? From { get; set; }
    }

    public sealed record Call(string Text, string By);
    public sealed record HumanLine(string Text, string Session);

    public int Act { get; set; }
    public string? LastSession { get; set; }
    public string? LastStatus { get; set; }
    public DateTime? LastAt { get; set; }
    public string? Boss { get; set; }
    public List<Section> Bots { get; } = [];
    public List<Call> Calls { get; } = [];
    public List<Call> BossCalls { get; } = [];
    public List<HumanLine> Human { get; } = [];
    public List<PartyQuestion> Questions { get; } = [];

    /// <summary>True while no session has put anything in it.</summary>
    public bool IsEmpty => Bots.Count == 0 && Calls.Count == 0 && BossCalls.Count == 0 && Human.Count == 0 && Questions.Count == 0;

    /// <summary>
    /// Folds a finished session in.
    /// <list type="bullet">
    /// <item>act_start and manual: each bot that pitched gets its section from its final pitch;
    /// the party calls become this session's calls in speaking order (when anyone pitched); the
    /// boss plan is cleared.</item>
    /// <item>mid: only the bots that pitched get new sections; the calls are replaced only when
    /// some bot sent calls.</item>
    /// <item>boss: the calls go to the boss plan alone; sections stay.</item>
    /// </list>
    /// A pitch that leaves a field out keeps the old value, so a short mid-act pitch doesn't wipe a
    /// lane. A bot that didn't pitch keeps its old section and its From.
    /// </summary>
    public void Merge(PartySessionResult result)
    {
        Act = result.Act;
        LastSession = result.Key;
        LastStatus = result.Status;
        LastAt = result.At;
        if (result.Boss is not null) Boss = result.Boss;

        var calls = new List<Call>();
        foreach (var (label, pitch) in result.Finals)
            foreach (var call in pitch.Calls)
                if (!calls.Any(c => c.Text.Equals(call, StringComparison.OrdinalIgnoreCase)))
                    calls.Add(new Call(call, label));

        if (result.Kind == PartyTriggers.Boss)
        {
            if (calls.Count > 0)
            {
                BossCalls.Clear();
                BossCalls.AddRange(calls.Take(KeptCalls));
            }
        }
        else
        {
            foreach (var (label, pitch) in result.Finals)
                Apply(label, pitch, result.Key);
            var replaceCalls = result.Kind == PartyTriggers.MidAct ? calls.Count > 0 : result.Finals.Count > 0;
            if (replaceCalls)
            {
                Calls.Clear();
                Calls.AddRange(calls.Take(KeptCalls));
            }
            if (result.Kind is PartyTriggers.ActStart or PartyTriggers.Manual)
                BossCalls.Clear();
        }

        foreach (var line in result.HumanLines)
            Human.Add(new HumanLine(line, result.Key));
        if (Human.Count > KeptHumanLines) Human.RemoveRange(0, Human.Count - KeptHumanLines);
        Questions.AddRange(result.Questions);
        if (Questions.Count > KeptQuestions) Questions.RemoveRange(0, Questions.Count - KeptQuestions);
    }

    private void Apply(string label, PartyPitch pitch, string key)
    {
        var section = Bots.Find(b => b.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (section is null)
        {
            section = new Section { Label = label };
            Bots.Add(section);
        }
        if (pitch.Role is not null) section.Role = pitch.Role;
        if (pitch.Archetype is not null) section.Archetype = pitch.Archetype;
        if (pitch.Wants.Count > 0) section.Takes = [.. pitch.Wants];
        if (pitch.Needs.Count > 0) section.Needs = [.. pitch.Needs];
        if (pitch.Gives.Count > 0) section.Gives = [.. pitch.Gives];
        section.From = key;
    }

    /// <summary>
    /// The plan as markdown of at most <see cref="MaxBytes"/> bytes. Deterministic: the same plan
    /// gives the same text. <paramref name="humanName"/> heads the human's section ("Mark") with
    /// <paramref name="humanCharacter"/> in brackets when known.
    /// </summary>
    public string Render(string? humanName, string? humanCharacter)
    {
        var human = new List<HumanLine>(Human);
        var questions = new List<PartyQuestion>(Questions);
        while (true)
        {
            var text = RenderWith(humanName, humanCharacter, human, questions);
            if (Encoding.UTF8.GetByteCount(text) <= MaxBytes)
                return text;
            if (human.Count > 0) human.RemoveAt(0);
            else if (questions.Count > 0) questions.RemoveAt(0);
            else return Cut(text);
        }
    }

    private static string Cut(string text)
    {
        while (Encoding.UTF8.GetByteCount(text) > MaxBytes - 4)
            text = text[..(text.Length * 9 / 10)];
        return text.TrimEnd() + "\n...";
    }

    private string RenderWith(string? humanName, string? humanCharacter, List<HumanLine> human, List<PartyQuestion> questions)
    {
        var name = string.IsNullOrWhiteSpace(humanName) ? "Player" : humanName.Trim();
        var b = new StringBuilder("# Party plan\n");
        var head = $"Act {Act.ToString(CultureInfo.InvariantCulture)}";
        if (LastSession is not null)
        {
            head += $", last session {LastSession} ({LastStatus ?? "done"})";
            if (LastAt is { } at) head += $" at {Display(at):HH:mm}";
        }
        b.Append(head).Append(".\n\n");
        b.Append($"{name}'s orders outrank this plan. Map votes follow {name}; a route call here is for when {name} hasn't voted after the wait.\n");

        if (human.Count > 0)
        {
            b.Append("\n## ").Append(name);
            if (!string.IsNullOrWhiteSpace(humanCharacter)) b.Append(" (").Append(humanCharacter.Trim()).Append(')');
            b.Append('\n');
            foreach (var line in human)
                b.Append("- \"").Append(line.Text).Append("\" (").Append(line.Session).Append(")\n");
        }

        foreach (var section in Bots)
        {
            b.Append("\n## ").Append(section.Label).Append(" (bot)\n");
            foreach (var line in SectionLines(section))
                b.Append("- ").Append(line).Append('\n');
        }

        if (Calls.Count > 0)
        {
            b.Append("\n## Party calls\n");
            foreach (var call in Calls)
                b.Append("- ").Append(call.Text).Append(" (").Append(call.By).Append(")\n");
        }

        if (BossCalls.Count > 0)
        {
            b.Append("\n## Boss plan");
            if (Boss is not null) b.Append(" (").Append(Boss).Append(')');
            b.Append('\n');
            foreach (var call in BossCalls)
                b.Append("- ").Append(call.Text).Append(" (").Append(call.By).Append(")\n");
        }

        if (questions.Count > 0)
        {
            b.Append("\n## Questions\n");
            foreach (var q in questions)
            {
                b.Append("- ").Append(q.Bot).Append(" asked ").Append(name).Append(": \"").Append(q.Text).Append("\" ");
                if (q.Answer is not null) b.Append(name).Append(": \"").Append(q.Answer).Append('"');
                else if (q.Guess is not null) b.Append("no answer; ").Append(q.Bot).Append(" guessed: ").Append(q.Guess);
                else b.Append("no answer");
                b.Append('\n');
            }
        }
        return b.ToString();
    }

    private static DateTime Display(DateTime at) => at.Kind == DateTimeKind.Utc ? at.ToLocalTime() : at;

    private static IEnumerable<string> SectionLines(Section s)
    {
        if (s.Role is not null) yield return $"Role: {s.Role}";
        if (s.Archetype is not null) yield return $"Archetype: {s.Archetype}";
        if (s.Takes.Count > 0) yield return $"Takes: {string.Join(", ", s.Takes)}";
        if (s.Needs.Count > 0) yield return $"Needs: {string.Join(", ", s.Needs)}";
        if (s.Gives.Count > 0) yield return $"Gives: {string.Join(", ", s.Gives)}";
        if (s.From is not null) yield return $"From: {s.From}";
    }

    /// <summary>The bot's own section as one short text for its pick screens ("Role: debuffer.
    /// Archetype: poison with Catalyst. ..."), or null when the plan has none for it.</summary>
    public string? Yours(string label)
    {
        var section = Bots.Find(b => b.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (section is null)
            return null;
        var lines = SectionLines(section).ToList();
        return lines.Count == 0 ? null : string.Join(". ", lines) + ".";
    }

    /// <summary>The party's calls and then the boss plan's, as the pick screens carry them
    /// ("Damage relics go to Mark (Silent)", "Boss (Kin Priest): ...").</summary>
    public IReadOnlyList<string> CallList()
    {
        var list = Calls.Select(c => $"{c.Text} ({c.By})").ToList();
        var boss = Boss is null ? "Boss" : $"Boss ({Boss})";
        list.AddRange(BossCalls.Select(c => $"{boss}: {c.Text} ({c.By})"));
        return list;
    }

    // ---- Saved beside the markdown, so a restart of the human's copy can take the plan up again ----

    public JsonObject ToJson()
    {
        static JsonArray Calls(IEnumerable<Call> calls) =>
            new(calls.Select(c => (JsonNode)new JsonObject { ["text"] = c.Text, ["by"] = c.By }).ToArray());
        return new JsonObject
        {
            ["v"] = 1,
            ["act"] = Act,
            ["last_session"] = LastSession,
            ["last_status"] = LastStatus,
            ["last_at"] = LastAt?.ToString("O", CultureInfo.InvariantCulture),
            ["boss"] = Boss,
            ["bots"] = new JsonArray(Bots.Select(s => (JsonNode)new JsonObject
            {
                ["label"] = s.Label,
                ["role"] = s.Role,
                ["archetype"] = s.Archetype,
                ["takes"] = PartyPitch.Array(s.Takes),
                ["needs"] = PartyPitch.Array(s.Needs),
                ["gives"] = PartyPitch.Array(s.Gives),
                ["from"] = s.From,
            }).ToArray()),
            ["calls"] = Calls(this.Calls),
            ["boss_calls"] = Calls(BossCalls),
            ["human"] = new JsonArray(Human.Select(h => (JsonNode)new JsonObject { ["text"] = h.Text, ["session"] = h.Session }).ToArray()),
            ["questions"] = new JsonArray(Questions.Select(q => (JsonNode)new JsonObject
            {
                ["bot"] = q.Bot, ["text"] = q.Text, ["answer"] = q.Answer, ["guess"] = q.Guess,
            }).ToArray()),
        };
    }

    /// <summary>The plan saved by <see cref="ToJson"/>, or null when it doesn't read.</summary>
    public static PartyPlan? FromJson(JsonObject? json)
    {
        if (json is null || json["v"] is not JsonValue v || !v.TryGetValue<int>(out var version) || version != 1)
            return null;
        static string? S(JsonNode? n) => PartyPitch.Str(n);
        static List<string> L(JsonNode? n) => n is JsonArray a ? a.Select(S).OfType<string>().ToList() : [];
        var plan = new PartyPlan
        {
            Act = json["act"] is JsonValue a && a.TryGetValue<int>(out var act) ? act : 0,
            LastSession = S(json["last_session"]),
            LastStatus = S(json["last_status"]),
            LastAt = DateTime.TryParse(S(json["last_at"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null,
            Boss = S(json["boss"]),
        };
        foreach (var s in (json["bots"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (S(s["label"]) is not { } label) continue;
            plan.Bots.Add(new Section
            {
                Label = label, Role = S(s["role"]), Archetype = S(s["archetype"]),
                Takes = L(s["takes"]), Needs = L(s["needs"]), Gives = L(s["gives"]), From = S(s["from"]),
            });
        }
        foreach (var c in (json["calls"] as JsonArray ?? []).OfType<JsonObject>())
            if (S(c["text"]) is { } text) plan.Calls.Add(new Call(text, S(c["by"]) ?? "?"));
        foreach (var c in (json["boss_calls"] as JsonArray ?? []).OfType<JsonObject>())
            if (S(c["text"]) is { } text) plan.BossCalls.Add(new Call(text, S(c["by"]) ?? "?"));
        foreach (var h in (json["human"] as JsonArray ?? []).OfType<JsonObject>())
            if (S(h["text"]) is { } text) plan.Human.Add(new HumanLine(text, S(h["session"]) ?? "?"));
        foreach (var q in (json["questions"] as JsonArray ?? []).OfType<JsonObject>())
            if (S(q["bot"]) is { } bot && S(q["text"]) is { } text) plan.Questions.Add(new PartyQuestion(bot, text, S(q["answer"]), S(q["guess"])));
        return plan;
    }
}
