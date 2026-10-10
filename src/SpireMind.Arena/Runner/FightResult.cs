using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>How a fight ended, as written to <c>outcome</c> in results.jsonl.</summary>
public static class Outcomes
{
    /// <summary>The fight was played to the end and the player lived.</summary>
    public const string Won = "won";

    /// <summary>The fight was played to the end and the player died.</summary>
    public const string Lost = "lost";

    /// <summary>The model stopped before the fight was over and stayed stopped: its session
    /// ended without an error, was started again as often as the runner does that
    /// (<see cref="ClaudeFight.MaxResumes"/>) and ended the same way every time.</summary>
    public const string Abandoned = "abandoned";

    /// <summary>The fight wasn't over when the time limit passed. That depends on how busy the
    /// service was as much as on the model, so it isn't counted for or against it, and the
    /// fight is played again when the batch is run again.</summary>
    public const string Timeout = "timeout";

    /// <summary>The fight couldn't be played: Claude Code didn't start, didn't reach the bridge,
    /// failed on the API, hit the usage limit, or ended or ran out of time without one call to
    /// the fight's tools. Says nothing about the model.</summary>
    public const string Error = "error";
}

/// <summary>Token counts of one fight, with Claude Code's own field names in the JSON.</summary>
public sealed record TokenCounts(long Input, long CacheCreation, long CacheRead, long Output);

/// <summary>
/// One fight as one line of results.jsonl: which fight, who played it, how it ended, the
/// simulator's counters and what Claude Code reported about its session. It never holds the
/// system prompt or anything from the environment. docs/arena.md describes the fields.
/// </summary>
public sealed record FightResult
{
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public required string ScenarioId { get; init; }
    public string Tier { get; init; } = "";
    public string Summary { get; init; } = "";
    public int Seed { get; init; }

    /// <summary>The model as asked for on the command line.</summary>
    public required string Model { get; init; }

    /// <summary>The model ids Claude Code says it used (the keys of its <c>modelUsage</c>).</summary>
    public IReadOnlyList<string> ModelsUsed { get; init; } = [];

    public string Effort { get; init; } = "";

    /// <summary>One of <see cref="Outcomes"/>.</summary>
    public required string Outcome { get; init; }

    public int HpLeft { get; init; }
    public int MaxHp { get; init; }
    public int DamageTaken { get; init; }
    public int Turns { get; init; }
    public int CardsPlayed { get; init; }

    /// <summary>Actions the simulator refused (an illegal id that got past the tools).</summary>
    public int RefusedActions { get; init; }

    /// <summary>Tool calls by tool name, without the <c>mcp__spiremind__</c> prefix.</summary>
    public IReadOnlyDictionary<string, int> ToolCalls { get; init; } = new Dictionary<string, int>();

    /// <summary>Tool calls that came back as errors (an illegal id, a stale version, acting
    /// while the game was busy): the tools refuse those before the simulator sees them. A Read
    /// that failed is in <see cref="ReadErrors"/>.</summary>
    public int ToolErrors { get; init; }

    /// <summary>How often the model called Claude Code's Read tool, the one way a session has
    /// to open the wiki's pages.</summary>
    public int ReadCalls { get; init; }

    /// <summary>Read calls that came back as an error: a file that isn't there, or one outside
    /// the checkout's strategy folder, which the session's permissions refuse.</summary>
    public int ReadErrors { get; init; }

    /// <summary>The wiki pages the model opened, each once, in the order it first opened them,
    /// as the prompt's index names them (<c>strategy/wiki/enemies/vantom.md</c>).</summary>
    public IReadOnlyList<string> WikiPages { get; init; } = [];

    /// <summary>Every other file the model asked Read for (see <see cref="StreamWatch.OtherReads"/>).</summary>
    public IReadOnlyList<string> OtherReads { get; init; } = [];

    /// <summary>From the start of the session to the end of the fight (or of the session, when
    /// the fight never ended). The time Claude Code takes to wind down afterwards isn't in it.</summary>
    public double WallSeconds { get; init; }

    /// <summary>How often the session ended before the fight was over and was started again
    /// with <c>--resume</c>. The numbers below cover all of those sessions.</summary>
    public int Resumes { get; init; }

    public long? CliDurationMs { get; init; }
    public int? NumTurns { get; init; }

    /// <summary>What the sessions would have cost on the API, as Claude Code works it out.</summary>
    public decimal? TotalCostUsd { get; init; }

    public TokenCounts? Tokens { get; init; }

    /// <summary>Where <see cref="Tokens"/> came from: "result" (the result lines' usage),
    /// "messages" (added up from the assistant messages, when a session printed no result line)
    /// or "result+messages" when the fight's sessions differ in that.</summary>
    public string? TokensSource { get; init; }

    /// <summary>Claude Code's <c>modelUsage</c>: tokens and cost per model id, over all the fight's sessions.</summary>
    public JsonObject? ModelUsage { get; init; }

    /// <summary>How full the subscription's 5 hour window was (0 to 1) when Claude Code last
    /// said so during this fight; null when it never did.</summary>
    public double? UsageFiveHour { get; init; }

    /// <summary>The same for the 7 day window.</summary>
    public double? UsageSevenDay { get; init; }

    /// <summary>What went wrong, for <see cref="Outcomes.Error"/> (and a won or lost fight whose
    /// session still ended badly).</summary>
    public string? Error { get; init; }

    /// <summary>Claude Code's own word that the subscription's usage limit was hit: the
    /// sentence of an error result, a rate limit status that refuses requests, or extra usage.
    /// Never the model's words. A batch stops when a fight comes back with it.</summary>
    public string? UsageLimit { get; init; }

    /// <summary>Present when a session's init line didn't list the tools the arena asked for
    /// (<see cref="StreamWatch.ToolsProblem"/>): the same sentence as <see cref="Error"/>. Every
    /// session would be the same, so a batch stops when a fight comes back with it.</summary>
    public string? ToolsProblem { get; init; }

    /// <summary>Output lines that weren't JSON, stderr, and remarks from the runner.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    public DateTime StartedAt { get; init; }

    /// <summary>The model Claude Code's init line names, i.e. what it made of <see cref="Model"/>.</summary>
    public string? CliModel { get; init; }
    public string? CliVersion { get; init; }

    /// <summary>Claude Code's <c>apiKeySource</c>: "none" when it plays on the subscription login.</summary>
    public string? ApiKeySource { get; init; }
    public string? McpStatus { get; init; }
    public string? ResultSubtype { get; init; }

    /// <summary>The status of the last <c>rate_limit_event</c>: "allowed", "allowed_warning"
    /// (served, but ahead of the window's pace) or "rejected".</summary>
    public string? RateLimitStatus { get; init; }

    /// <summary>How the session ended: "exited" by itself, "interrupted" (SIGINT) or "killed".</summary>
    public string? CliExit { get; init; }
    public int? ExitCode { get; init; }

    /// <summary>The start of the model's last text, which says why it stopped when it gave up.</summary>
    public string? LastText { get; init; }

    /// <summary>The first 12 hex digits of the system prompt's SHA-256, to tell whether two
    /// fights were played with the same play prompt and strategy notes. The checkout's own path
    /// is taken out first (<see cref="ArenaPrompt.Hash(string, string)"/>).</summary>
    public string? PromptHash { get; init; }

    /// <summary>The same for the wiki's pages as they stood when the fight ended
    /// (<see cref="ArenaPrompt.WikiHash"/>): their text isn't in the prompt, so this tells
    /// whether two fights could read the same pages. Null when the checkout has no wiki.</summary>
    public string? WikiHash { get; init; }

    /// <summary>The fight was played to the end.</summary>
    public bool Finished => Outcome is Outcomes.Won or Outcomes.Lost;

    /// <summary>The fight says something about how the model plays: it won, lost or gave up.
    /// A timeout and an error don't, are counted nowhere and are played again by the next run.</summary>
    public bool Counts => Outcome is Outcomes.Won or Outcomes.Lost or Outcomes.Abandoned;

    public bool Won => Outcome == Outcomes.Won;

    /// <summary>HP left as the report scores it: 0 unless the fight was won.</summary>
    public int HpScore => Won ? HpLeft : 0;

    /// <summary>Output tokens of every model the session used, from <see cref="ModelUsage"/> when
    /// it is there (it also counts a helper model), else from <see cref="Tokens"/>.</summary>
    public long? OutputTokens
    {
        get
        {
            if (ModelUsage is { Count: > 0 } usage)
            {
                long sum = 0;
                var any = false;
                foreach (var (_, entry) in usage)
                    if (entry is JsonObject model && Long(model["outputTokens"]) is { } n)
                    {
                        sum += n;
                        any = true;
                    }
                if (any) return sum;
            }
            return Tokens?.Output;
        }
    }

    /// <summary>
    /// The session's cost at the repo's own list prices (<see cref="ModelPrices"/>), from the
    /// tokens in <see cref="ModelUsage"/>: a second opinion on <see cref="TotalCostUsd"/> for a
    /// model Claude Code has no price for. Cache writes count as 5 minute writes and the
    /// long-prompt tier is left out, since the totals don't say which request was long. A model
    /// the table doesn't know (a helper model, say) is left out; null when it knows none of them.
    /// </summary>
    public decimal? ListPriceUsd
    {
        get
        {
            if (ModelUsage is not { Count: > 0 } usage)
                return null;
            decimal usd = 0;
            var priced = false;
            foreach (var (id, entry) in usage)
            {
                if (entry is not JsonObject tokens)
                    continue;
                var price = ModelPrices.For(id, out var known);
                if (!known)
                    continue;
                priced = true;
                usd += ((Long(tokens["inputTokens"]) ?? 0) * price.Input
                        + (Long(tokens["cacheCreationInputTokens"]) ?? 0) * price.CacheWrite5m
                        + (Long(tokens["cacheReadInputTokens"]) ?? 0) * price.CacheRead
                        + (Long(tokens["outputTokens"]) ?? 0) * price.Output) / 1_000_000m;
            }
            return priced ? usd : null;
        }
    }

    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["scenario"] = ScenarioId,
            ["tier"] = Tier,
            ["summary"] = Summary,
            ["seed"] = Seed,
            ["model"] = Model,
            ["models_used"] = new JsonArray(ModelsUsed.Select(m => (JsonNode)JsonValue.Create(m)!).ToArray()),
            ["effort"] = Effort,
            ["outcome"] = Outcome,
            ["hp_left"] = HpLeft,
            ["max_hp"] = MaxHp,
            ["damage_taken"] = DamageTaken,
            ["turns"] = Turns,
            ["cards_played"] = CardsPlayed,
            ["refused_actions"] = RefusedActions,
        };
        var calls = new JsonObject();
        foreach (var (name, count) in ToolCalls.OrderBy(c => c.Key, StringComparer.Ordinal))
            calls[name] = count;
        json["tool_calls"] = calls;
        json["tool_errors"] = ToolErrors;
        json["read_calls"] = ReadCalls;
        json["read_errors"] = ReadErrors;
        json["wiki_pages"] = new JsonArray(WikiPages.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray());
        json["other_reads"] = new JsonArray(OtherReads.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray());
        json["wall_seconds"] = Math.Round(WallSeconds, 1);
        json["resumes"] = Resumes;
        json["cli_duration_ms"] = CliDurationMs;
        json["num_turns"] = NumTurns;
        json["total_cost_usd"] = TotalCostUsd;
        json["input_tokens"] = Tokens?.Input;
        json["cache_creation_input_tokens"] = Tokens?.CacheCreation;
        json["cache_read_input_tokens"] = Tokens?.CacheRead;
        json["output_tokens"] = Tokens?.Output;
        json["tokens_source"] = TokensSource;
        json["model_usage"] = ModelUsage?.DeepClone();
        json["usage_five_hour"] = UsageFiveHour;
        json["usage_seven_day"] = UsageSevenDay;
        json["error"] = Error;
        if (UsageLimit is not null) json["usage_limit"] = UsageLimit;
        if (ToolsProblem is not null) json["tools_problem"] = ToolsProblem;
        json["notes"] = new JsonArray(Notes.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray());
        json["started_at"] = StartedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        json["cli"] = new JsonObject
        {
            ["model"] = CliModel,
            ["version"] = CliVersion,
            ["api_key_source"] = ApiKeySource,
            ["mcp_status"] = McpStatus,
            ["result_subtype"] = ResultSubtype,
            ["rate_limit_status"] = RateLimitStatus,
            ["exit"] = CliExit,
            ["exit_code"] = ExitCode,
        };
        json["last_text"] = LastText;
        json["prompt_hash"] = PromptHash;
        json["wiki_hash"] = WikiHash;
        return json;
    }

    /// <summary>The result as one line of JSON, without a line break.</summary>
    public string ToLine() => ToJson().ToJsonString(Relaxed);

    /// <summary>Reads a result back. Null when the object has no scenario, model or outcome.</summary>
    public static FightResult? FromJson(JsonObject json)
    {
        if (Str(json["scenario"]) is not { } scenario || Str(json["model"]) is not { } model || Str(json["outcome"]) is not { } outcome)
            return null;
        var cli = json["cli"] as JsonObject;
        var tokens = Long(json["output_tokens"]) is { } output
            ? new TokenCounts(Long(json["input_tokens"]) ?? 0, Long(json["cache_creation_input_tokens"]) ?? 0,
                Long(json["cache_read_input_tokens"]) ?? 0, output)
            : null;
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        if (json["tool_calls"] is JsonObject toolCalls)
            foreach (var (name, count) in toolCalls)
                calls[name] = (int)(Long(count) ?? 0);

        return new FightResult
        {
            ScenarioId = scenario,
            Tier = Str(json["tier"]) ?? "",
            Summary = Str(json["summary"]) ?? "",
            Seed = (int)(Long(json["seed"]) ?? 0),
            Model = model,
            ModelsUsed = Strings(json["models_used"]),
            Effort = Str(json["effort"]) ?? "",
            Outcome = outcome,
            HpLeft = (int)(Long(json["hp_left"]) ?? 0),
            MaxHp = (int)(Long(json["max_hp"]) ?? 0),
            DamageTaken = (int)(Long(json["damage_taken"]) ?? 0),
            Turns = (int)(Long(json["turns"]) ?? 0),
            CardsPlayed = (int)(Long(json["cards_played"]) ?? 0),
            RefusedActions = (int)(Long(json["refused_actions"]) ?? 0),
            ToolCalls = calls,
            ToolErrors = (int)(Long(json["tool_errors"]) ?? 0),
            ReadCalls = (int)(Long(json["read_calls"]) ?? 0),
            ReadErrors = (int)(Long(json["read_errors"]) ?? 0),
            WikiPages = Strings(json["wiki_pages"]),
            OtherReads = Strings(json["other_reads"]),
            WallSeconds = Number(json["wall_seconds"]) ?? 0,
            Resumes = (int)(Long(json["resumes"]) ?? 0),
            CliDurationMs = Long(json["cli_duration_ms"]),
            NumTurns = (int?)Long(json["num_turns"]),
            TotalCostUsd = (decimal?)Number(json["total_cost_usd"]),
            Tokens = tokens,
            TokensSource = Str(json["tokens_source"]),
            ModelUsage = json["model_usage"]?.DeepClone() as JsonObject,
            UsageFiveHour = Number(json["usage_five_hour"]),
            UsageSevenDay = Number(json["usage_seven_day"]),
            Error = Str(json["error"]),
            UsageLimit = Str(json["usage_limit"]),
            ToolsProblem = Str(json["tools_problem"]),
            Notes = Strings(json["notes"]),
            StartedAt = DateTime.TryParse(Str(json["started_at"]), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : default,
            CliModel = Str(cli?["model"]),
            CliVersion = Str(cli?["version"]),
            ApiKeySource = Str(cli?["api_key_source"]),
            McpStatus = Str(cli?["mcp_status"]),
            ResultSubtype = Str(cli?["result_subtype"]),
            RateLimitStatus = Str(cli?["rate_limit_status"]),
            CliExit = Str(cli?["exit"]),
            ExitCode = (int?)Long(cli?["exit_code"]),
            LastText = Str(json["last_text"]),
            PromptHash = Str(json["prompt_hash"]),
            WikiHash = Str(json["wiki_hash"]),
        };
    }

    /// <summary>Every result in a results.jsonl, in file order. Lines that aren't a result (a
    /// line cut short by a crash, say) are counted in <paramref name="unreadable"/> and skipped.</summary>
    public static List<FightResult> ReadFile(string path, out int unreadable)
    {
        var results = new List<FightResult>();
        unreadable = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            FightResult? result = null;
            try
            {
                if (JsonNode.Parse(line) is JsonObject json)
                    result = FromJson(json);
            }
            catch (JsonException)
            {
                // counted below
            }
            if (result is null) unreadable++;
            else results.Add(result);
        }
        return results;
    }

    internal static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    // A JSON number as a whole number, whether it was parsed from text or made in memory.
    internal static long? Long(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<long>(out var n)) return n;
        if (v.TryGetValue<int>(out var i)) return i;
        return Number(node) is { } d && Math.Abs(d) < 9e15 ? (long)Math.Round(d) : null;
    }

    internal static double? Number(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<decimal>(out var m)) return (double)m;
        if (v.TryGetValue<long>(out var n)) return n;
        if (v.TryGetValue<int>(out var i)) return i;
        return null;
    }

    private static string[] Strings(JsonNode? node) =>
        node is JsonArray array ? array.Select(Str).OfType<string>().ToArray() : [];
}
