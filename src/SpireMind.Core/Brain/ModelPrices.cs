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
/// </summary>
public readonly record struct UsageCost(decimal Usd, string PricedAs, bool KnownModel, bool LongPrompt);

/// <summary>
/// List prices for the models the brain can use, and the cost of a response from its usage.
/// There is no API to read the credit balance with a normal key, so spend is the mod's own
/// estimate from these numbers (checked against the pricing docs on 2026-10-10). Update the
/// table when prices change or a model is added.
/// </summary>
public static class ModelPrices
{
    private const decimal PerToken = 1_000_000m;

    /// <summary>What <see cref="UsageCost.PricedAs"/> says for a model the table doesn't know.</summary>
    public const string FallbackName = "the highest rates";

    public static IReadOnlyDictionary<string, ModelPrice> Table { get; } = new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-5-5"] = new(Input: 4m, CacheWrite5m: 5m, CacheWrite1h: 8m, CacheRead: 0.20m, Output: 20m),
        ["claude-sonnet-5-5"] = new(Input: 2m, CacheWrite5m: 2.5m, CacheWrite1h: 4m, CacheRead: 0.20m, Output: 10m),
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

    /// <summary>The cost of one response from its <c>usage</c> object. Pass the response's own
    /// <c>model</c> field when there is one, since that is what was billed.</summary>
    public static UsageCost Cost(string? model, JsonObject? usage) => Cost(model, TokenUsage.FromJson(usage));

    /// <summary>The cost of the given token counts on <paramref name="model"/>.</summary>
    public static UsageCost Cost(string? model, TokenUsage usage)
    {
        var key = Lookup(model);
        var price = key is not null ? Table[key] : Fallback;
        var longPrompt = price.LongPromptThreshold is { } threshold && usage.PromptTokens > threshold;
        var factor = longPrompt ? price.LongPromptMultiplier : 1m;

        var usd = (usage.Input * price.Input
                   + usage.CacheWrite5m * price.CacheWrite5m
                   + usage.CacheWrite1h * price.CacheWrite1h
                   + usage.CacheRead * price.CacheRead
                   + usage.Output * price.Output) * factor / PerToken;
        return new UsageCost(usd, key ?? FallbackName, KnownModel: key is not null, LongPrompt: longPrompt);
    }

    // The table key for a model id: an exact match, an alias, or a dated or suffixed snapshot
    // such as "claude-opus-5-5-20261001" (an "@" or "[" suffix counts too). After a hyphen only
    // a date counts, so a later version of a listed model isn't taken for a snapshot of it.
    // Null when nothing matches.
    private static string? Lookup(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return null;
        var id = BrainSettings.ResolveModel(model);
        if (Table.ContainsKey(id))
            return Table.Keys.First(k => string.Equals(k, id, StringComparison.OrdinalIgnoreCase));
        return Table.Keys
            .Where(k => id.Length > k.Length && id.StartsWith(k, StringComparison.OrdinalIgnoreCase) && IsSnapshotSuffix(id.AsSpan(k.Length)))
            .OrderByDescending(k => k.Length)
            .FirstOrDefault();
    }

    // "@..." or "[...]" after the model id, or a hyphen and a date of 8 digits.
    private static bool IsSnapshotSuffix(ReadOnlySpan<char> suffix) =>
        suffix[0] is '@' or '['
        || (suffix.Length >= 9 && suffix[0] == '-' && suffix[1..9].IndexOfAnyExceptInRange('0', '9') < 0);
}
