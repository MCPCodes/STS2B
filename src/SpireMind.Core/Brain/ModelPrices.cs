using System.Text.Json.Nodes;

namespace SpireMind.Core.Brain;

/// <summary>
/// One model's list prices in US dollars per million tokens. Cache writes cost more than plain
/// input and depend on the cache lifetime (5 minutes or 1 hour); cache reads cost a fraction.
/// </summary>
public sealed record ModelPrice(decimal Input, decimal CacheWrite5m, decimal CacheWrite1h, decimal CacheRead, decimal Output)
{
    /// <summary>Prompts longer than this many tokens (input plus cache reads plus cache writes)
    /// are billed at <see cref="LongPromptMultiplier"/> times every rate, output included.
    /// Null when the model has a single tier.</summary>
    public long? LongPromptThreshold { get; init; }

    public decimal LongPromptMultiplier { get; init; } = 1m;
}

/// <summary>
/// The token counts of one response, from its <c>usage</c> object. Cache writes are split by
/// lifetime because a 1 hour write costs more than a 5 minute one.
/// </summary>
public sealed record TokenUsage(long Input, long Output, long CacheRead, long CacheWrite5m, long CacheWrite1h)
{
    public static TokenUsage Zero { get; } = new(0, 0, 0, 0, 0);

    /// <summary>All cache writes, whatever their lifetime (the API's cache_creation_input_tokens).</summary>
    public long CacheWrite => CacheWrite5m + CacheWrite1h;

    /// <summary>The whole prompt: fresh input plus what was read from and written to the cache.
    /// This is the conversation's size, the number to compare against context thresholds.</summary>
    public long PromptTokens => Input + CacheRead + CacheWrite;

    /// <summary>
    /// Reads a usage object: input_tokens, output_tokens, cache_read_input_tokens,
    /// cache_creation_input_tokens and cache_creation {ephemeral_5m_input_tokens,
    /// ephemeral_1h_input_tokens}. Without the cache_creation split every write counts as a
    /// 5 minute one; writes the split doesn't account for count as 5 minute too. Missing
    /// fields are zero, and null gives <see cref="Zero"/>.
    /// </summary>
    public static TokenUsage FromJson(JsonObject? usage)
    {
        if (usage is null)
            return Zero;

        var writes = Count(usage["cache_creation_input_tokens"]);
        long write5m, write1h;
        if (usage["cache_creation"] is JsonObject split)
        {
            write5m = Count(split["ephemeral_5m_input_tokens"]);
            write1h = Count(split["ephemeral_1h_input_tokens"]);
            write5m += Math.Max(0, writes - write5m - write1h);
        }
        else
        {
            write5m = writes;
            write1h = 0;
        }

        return new TokenUsage(
            Input: Count(usage["input_tokens"]),
            Output: Count(usage["output_tokens"]),
            CacheRead: Count(usage["cache_read_input_tokens"]),
            CacheWrite5m: write5m,
            CacheWrite1h: write1h);
    }

    public static TokenUsage operator +(TokenUsage a, TokenUsage b) => new(
        a.Input + b.Input, a.Output + b.Output, a.CacheRead + b.CacheRead,
        a.CacheWrite5m + b.CacheWrite5m, a.CacheWrite1h + b.CacheWrite1h);

    // A non-negative token count. Parsed responses hold JsonElement numbers, but a usage object
    // built in code holds whatever CLR type was stored, and TryGetValue doesn't convert those.
    private static long Count(JsonNode? node)
    {
        if (node is not JsonValue v) return 0;
        if (v.TryGetValue<long>(out var l)) return Math.Max(0, l);
        if (v.TryGetValue<int>(out var i)) return Math.Max(0, i);
        if (v.TryGetValue<double>(out var d) && double.IsFinite(d)) return (long)Math.Clamp(d, 0, 1e18);
        if (v.TryGetValue<decimal>(out var m)) return (long)Math.Clamp(m, 0, long.MaxValue);
        return 0;
    }
}

/// <summary>
/// What a response cost. <see cref="KnownModel"/> is false when the model isn't in the price
/// table and the highest rate of each kind in it was used instead
/// (<see cref="ModelPrices.Fallback"/>, priced as <see cref="ModelPrices.FallbackName"/>), so
/// the estimate errs high.
/// <see cref="LongPrompt"/> is true when the long-prompt tier applied.
/// <see cref="Fast"/> is true when the response was priced at the fast-mode rates
/// (<see cref="ModelPrices.FastTable"/>).
/// </summary>
public readonly record struct UsageCost(decimal Usd, string PricedAs, bool KnownModel, bool LongPrompt, bool Fast = false);

/// <summary>
/// List prices for the models the brain can use, and the cost of a response from its usage.
/// There is no API to read the credit balance with a normal key, so spend is the mod's own
/// estimate from these numbers (checked against the pricing docs on 2026-10-10). Update the
/// table when prices change or a model is added. A response that ran in the API's fast mode
/// (its usage says <c>"speed": "fast"</c>) is priced from <see cref="FastTable"/> instead.
/// </summary>
public static class ModelPrices
{
    private const decimal PerToken = 1_000_000m;

    /// <summary>What <see cref="UsageCost.PricedAs"/> says for a model the table doesn't know.</summary>
    public const string FallbackName = "the highest rates";

    public static IReadOnlyDictionary<string, ModelPrice> Table { get; } = new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-5-5"] = new(Input: 4m, CacheWrite5m: 5m, CacheWrite1h: 8m, CacheRead: 0.20m, Output: 20m),
        ["claude-sonnet-5-5"] = new(Input: 2m, CacheWrite5m: 2.5m, CacheWrite1h: 4m, CacheRead: 0.10m, Output: 10m),
        ["claude-haiku-5-5"] = new(Input: 0.10m, CacheWrite5m: 0.125m, CacheWrite1h: 0.20m, CacheRead: 0.01m, Output: 0.50m)
        {
            LongPromptThreshold = 100_000,
            LongPromptMultiplier = 5m,
        },
        ["claude-fable-5-1"] = new(Input: 10m, CacheWrite5m: 12.5m, CacheWrite1h: 20m, CacheRead: 0.25m, Output: 50m),
        // The previous releases of the entry above, still served: the same prices, but cache reads at $1.
        ["claude-fable-5"] = new(Input: 10m, CacheWrite5m: 12.5m, CacheWrite1h: 20m, CacheRead: 1.00m, Output: 50m),
        ["claude-mythos-5"] = new(Input: 10m, CacheWrite5m: 12.5m, CacheWrite1h: 20m, CacheRead: 1.00m, Output: 50m),
    };

    /// <summary>The prices of a model the table doesn't know: the highest of each rate in
    /// <see cref="Table"/>, taken one by one, so no line of the estimate comes out lower than it
    /// would for any model listed here, and the monthly cap can't be overrun by a model priced
    /// like one of them. A cheaper unknown model is overcounted (and stops sooner) until it is
    /// added to the table. Declared after the table, which it is computed from.</summary>
    public static ModelPrice Fallback { get; } = new(
        Input: Table.Values.Max(p => p.Input),
        CacheWrite5m: Table.Values.Max(p => p.CacheWrite5m),
        CacheWrite1h: Table.Values.Max(p => p.CacheWrite1h),
        CacheRead: Table.Values.Max(p => p.CacheRead),
        Output: Table.Values.Max(p => p.Output));

    /// <summary>The beta flag a request with <c>"speed": "fast"</c> needs in its anthropic-beta header.</summary>
    public const string FastModeBeta = "fast-mode-2026-02-01";

    /// <summary>The value of a request's top-level <c>speed</c> field, and of a response's
    /// <c>usage.speed</c>, in fast mode.</summary>
    public const string FastSpeed = "fast";

    /// <summary>What <c>usage.speed</c> says for a response at the normal speed.</summary>
    public const string StandardSpeed = "standard";

    /// <summary>
    /// The API's fast mode (a research preview): the same model at up to 2.5 times the output
    /// tokens per second, at these prices. Only the models listed here take it; on any other a
    /// request with <c>"speed": "fast"</c> is an error, so <see cref="SupportsFast"/> gates it.
    /// The pricing page lists input and output ($8 and $40 on Opus 5.5, $10 and $50 on Opus 5 and
    /// Opus 4.8) and says the prompt caching multipliers apply on top: a 5 minute write at 1.25
    /// times the fast input price, a 1 hour write at 2 times, a read at 0.1 times (0.05 on Opus
    /// 5.5). Every rate comes out at twice the model's standard one. Checked against the pricing
    /// and fast mode pages on 2026-10-12.
    /// </summary>
    public static IReadOnlyDictionary<string, ModelPrice> FastTable { get; } = new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-5-5"] = new(Input: 8m, CacheWrite5m: 10m, CacheWrite1h: 16m, CacheRead: 0.40m, Output: 40m),
        ["claude-opus-5"] = new(Input: 10m, CacheWrite5m: 12.5m, CacheWrite1h: 20m, CacheRead: 1.00m, Output: 50m),
        ["claude-opus-4-8"] = new(Input: 10m, CacheWrite5m: 12.5m, CacheWrite1h: 20m, CacheRead: 1.00m, Output: 50m),
    };

    /// <summary>What a fast response costs on a model <see cref="FastTable"/> doesn't list: this
    /// many times the rates it would have at standard speed, the ratio of every listed model.
    /// The API refuses fast mode on such a model, so this only keeps the estimate from coming out
    /// low if one ever answers at fast speed.</summary>
    public const decimal FastMultiplier = 2m;

    /// <summary>The models that take <c>thinking: {"type": "adaptive", "display": "summarized"}</c>:
    /// the price table's, and their siblings the docs list with the same thinking options. Thinking
    /// is on by default on all of them, so the field changes only what comes back: a readable
    /// summary in each thinking block instead of an empty string (billing is the same). Opus 4.8
    /// and 4.7 take the field too but are left out on purpose: thinking is off there unless the
    /// request turns it on, so sending it would make them think and cost more than before.
    /// Checked against the API docs on 2026-10-11.</summary>
    public static IReadOnlySet<string> SummarizedThinkingModels { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "claude-opus-5-5", "claude-opus-5",
        "claude-sonnet-5-5", "claude-sonnet-5", "claude-haiku-5-5",
        "claude-fable-5-1", "claude-fable-5", "claude-mythos-5-1", "claude-mythos-5",
    };

    /// <summary>True when <paramref name="model"/> (a full id, a dated snapshot of one, or an
    /// alias) is in <see cref="SummarizedThinkingModels"/>. Other models get no thinking field,
    /// as before.</summary>
    public static bool TakesSummarizedThinking(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return false;
        var id = BrainSettings.ResolveModel(model);
        return SummarizedThinkingModels.Contains(id)
               || SummarizedThinkingModels.Any(k => id.Length > k.Length && id.StartsWith(k, StringComparison.OrdinalIgnoreCase) && IsSnapshotSuffix(id.AsSpan(k.Length)));
    }

    /// <summary>True when <paramref name="model"/> (a full id, a dated snapshot of one, or an
    /// alias such as "sonnet") has an entry in <see cref="Table"/>.</summary>
    public static bool IsKnown(string? model) => Lookup(model) is not null;

    /// <summary>The prices for <paramref name="model"/>, or <see cref="Fallback"/> when it is unknown.</summary>
    public static ModelPrice For(string? model, out bool known)
    {
        var key = Lookup(model);
        known = key is not null;
        return key is not null ? Table[key] : Fallback;
    }

    /// <summary>True when <paramref name="model"/> (a full id, a dated snapshot of one, or an
    /// alias) takes the API's fast mode: it has an entry in <see cref="FastTable"/>.</summary>
    public static bool SupportsFast(string? model) => Lookup(model, FastTable) is not null;

    /// <summary>The fast-mode prices for <paramref name="model"/>, or null when it doesn't take fast mode.</summary>
    public static ModelPrice? FastFor(string? model) => Lookup(model, FastTable) is { } key ? FastTable[key] : null;

    /// <summary>True when a response's <c>usage</c> object says it ran in fast mode
    /// (<c>"speed": "fast"</c>). A usage without the field, or with "standard", is false.</summary>
    public static bool IsFast(JsonObject? usage) => string.Equals(SpeedOf(usage), FastSpeed, StringComparison.OrdinalIgnoreCase);

    /// <summary>A response's <c>usage.speed</c> ("fast" or "standard"), or null when it has none.</summary>
    public static string? SpeedOf(JsonObject? usage) =>
        usage?["speed"] is JsonValue v && v.TryGetValue<string>(out var speed) && !string.IsNullOrWhiteSpace(speed) ? speed.Trim() : null;

    /// <summary>The cost of one response from its <c>usage</c> object, at the fast-mode rates when
    /// the usage says <c>"speed": "fast"</c>: what the response reports was billed, not what the
    /// request asked for. Pass the response's own <c>model</c> field when there is one, since
    /// that is what was billed.</summary>
    public static UsageCost Cost(string? model, JsonObject? usage) => Cost(model, TokenUsage.FromJson(usage), IsFast(usage));

    /// <summary>The cost of the given token counts on <paramref name="model"/>, at the fast-mode
    /// rates (<see cref="FastTable"/>, or <see cref="FastMultiplier"/> times the standard ones
    /// for a model it doesn't list) when <paramref name="fast"/> is true.</summary>
    public static UsageCost Cost(string? model, TokenUsage usage, bool fast = false)
    {
        var key = Lookup(model);
        var price = key is not null ? Table[key] : Fallback;
        var speed = 1m;
        if (fast)
        {
            if (Lookup(model, FastTable) is { } fastKey)
            {
                key = fastKey;
                price = FastTable[fastKey];
            }
            else
            {
                speed = FastMultiplier;
            }
        }
        var longPrompt = price.LongPromptThreshold is { } threshold && usage.PromptTokens > threshold;
        var factor = longPrompt ? price.LongPromptMultiplier : 1m;

        var usd = (usage.Input * price.Input
                   + usage.CacheWrite5m * price.CacheWrite5m
                   + usage.CacheWrite1h * price.CacheWrite1h
                   + usage.CacheRead * price.CacheRead
                   + usage.Output * price.Output) * factor * speed / PerToken;
        return new UsageCost(usd, key ?? FallbackName, KnownModel: key is not null, LongPrompt: longPrompt, Fast: fast);
    }

    // The table key for a model id: an exact match, an alias, or a dated or suffixed snapshot
    // such as "claude-opus-5-5-20261001" (an "@" or "[" suffix counts too). After a hyphen only
    // a date counts, so a later version of a listed model isn't taken for a snapshot of it.
    // Null when nothing matches.
    private static string? Lookup(string? model) => Lookup(model, Table);

    // The same, in the standard table or the fast one.
    private static string? Lookup(string? model, IReadOnlyDictionary<string, ModelPrice> table)
    {
        if (string.IsNullOrWhiteSpace(model))
            return null;
        var id = BrainSettings.ResolveModel(model);
        if (table.ContainsKey(id))
            return table.Keys.First(k => string.Equals(k, id, StringComparison.OrdinalIgnoreCase));
        return table.Keys
            .Where(k => id.Length > k.Length && id.StartsWith(k, StringComparison.OrdinalIgnoreCase) && IsSnapshotSuffix(id.AsSpan(k.Length)))
            .OrderByDescending(k => k.Length)
            .FirstOrDefault();
    }

    // "@..." or "[...]" after the model id, or a hyphen and a date of 8 digits.
    private static bool IsSnapshotSuffix(ReadOnlySpan<char> suffix) =>
        suffix[0] is '@' or '['
        || (suffix.Length >= 9 && suffix[0] == '-' && suffix[1..9].IndexOfAnyExceptInRange('0', '9') < 0);
}
