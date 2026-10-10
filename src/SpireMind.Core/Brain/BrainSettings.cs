using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
// The mod compiles Core with global usings for Godot and the game, and Godot has its own Environment.
// These aliases keep the .NET ones there.
using Environment = System.Environment;

namespace SpireMind.Core.Brain;

/// <summary>Where <see cref="BrainSettings.ApiKey"/> came from.</summary>
public enum ApiKeySource { None, File, Environment }

/// <summary>
/// The brain's settings: <c>config.json</c> in the config folder (default
/// <c>~/.config/spiremind</c>), every key optional, plus the API key from <c>api_key</c> in the
/// same folder or the ANTHROPIC_API_KEY environment variable. See docs/brain.md.
///
/// Immutable, so the brain task and the chat commands can share one instance without locks:
/// a command makes a changed copy (<c>settings with { Model = "sonnet" }</c>), saves it and
/// swaps it in. The key is never written anywhere by the mod and never appears in
/// <see cref="ToString"/>.
/// </summary>
public sealed record BrainSettings
{
    public const string ConfigFileName = "config.json";
    public const string ApiKeyFileName = "api_key";
    public const string ApiKeyEnvVar = "ANTHROPIC_API_KEY";
    public const string DefaultModel = "claude-opus-5-5";
    public const string DefaultEffort = "medium";

    /// <summary>The effort of elite and boss rooms when config.json doesn't set one, raised to
    /// <see cref="Effort"/> when that is higher (see <see cref="EffortFor"/>).</summary>
    public const string DefaultFightEffort = "high";

    /// <summary>The value of <see cref="EliteEffort"/> or <see cref="BossEffort"/> (and of a JSON
    /// null in config.json) that means "the normal effort".</summary>
    public const string SameEffort = "same";

    /// <summary>Clears a fight effort back to <see cref="DefaultFightEffort"/> (/effort boss default).</summary>
    public const string DefaultWord = "default";

    /// <summary>The room types (the game's RoomType names, run.room in a snapshot) with an effort of their own.</summary>
    public const string EliteRoom = "Elite";

    /// <inheritdoc cref="EliteRoom"/>
    public const string BossRoom = "Boss";
    public const decimal DefaultMonthlyBudgetUsd = 200m;

    /// <summary>The range config.json's max_tokens accepts: below the minimum a reply has no room
    /// to think and act, and the maximum is the models' output limit.</summary>
    public const int MinMaxTokens = 1024;

    /// <inheritdoc cref="MinMaxTokens"/>
    public const int MaxMaxTokens = 128_000;

    /// <summary>The values output_config.effort accepts, lowest first.</summary>
    public static IReadOnlyList<string> Efforts { get; } = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>Short names for /model and config.json, mapped to full model ids.</summary>
    public static IReadOnlyDictionary<string, string> ModelAliases { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["opus"] = "claude-opus-5-5",
        ["sonnet"] = "claude-sonnet-5-5",
        ["haiku"] = "claude-haiku-5-5",
        ["fable"] = "claude-fable-5-1",
    };

    /// <summary><c>~/.config/spiremind</c>.</summary>
    public static string DefaultConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "spiremind");

    private readonly string _model = DefaultModel;
    private readonly string _effort = DefaultEffort;
    private readonly decimal _budget = DefaultMonthlyBudgetUsd;
    private readonly int? _maxTokens;
    private readonly string? _eliteEffort;
    private readonly string? _bossEffort;

    /// <summary>The folder holding config.json, api_key, spend.json and transcripts.</summary>
    public string ConfigDir { get; init; } = DefaultConfigDir;

    /// <summary>The STS2B checkout the brain reads its prompt and strategy notes from and keeps
    /// its run journal in. Null when not configured; the game side then uses its fallbacks.</summary>
    public string? Repo { get; init; }

    /// <summary>A full model id. Aliases (opus, sonnet, haiku, fable) are resolved on the way in;
    /// other ids are kept as given, so a new model works before the price table knows it.</summary>
    public string Model
    {
        get => _model;
        init => _model = ResolveModel(value);
    }

    /// <summary>One of <see cref="Efforts"/>. Setting anything else throws <see cref="ArgumentException"/>;
    /// check with <see cref="TryParseEffort"/> first.</summary>
    public string Effort
    {
        get => _effort;
        init => _effort = TryParseEffort(value, out var effort)
            ? effort
            : throw new ArgumentException($"Effort must be one of {string.Join(", ", Efforts)}.", nameof(Effort));
    }

    /// <summary>The effort while the bot is in an elite room (elite_effort in config.json): one of
    /// <see cref="Efforts"/>, used as it is; <see cref="SameEffort"/> for <see cref="Effort"/>; or
    /// null (the key left out) for <see cref="DefaultFightEffort"/>, or <see cref="Effort"/> when
    /// that is higher. Other values throw <see cref="ArgumentException"/>; check with
    /// <see cref="TryParseFightEffort"/> first. See <see cref="EffortFor"/>.</summary>
    public string? EliteEffort
    {
        get => _eliteEffort;
        init => _eliteEffort = CheckFightEffort(value, nameof(EliteEffort));
    }

    /// <summary>Like <see cref="EliteEffort"/>, for boss rooms (boss_effort in config.json).</summary>
    public string? BossEffort
    {
        get => _bossEffort;
        init => _bossEffort = CheckFightEffort(value, nameof(BossEffort));
    }

    /// <summary>The mod's own monthly cap in US dollars, compared against spend.json. Zero
    /// means the API is never used. Negative values throw <see cref="ArgumentOutOfRangeException"/>.</summary>
    public decimal MonthlyBudgetUsd
    {
        get => _budget;
        init => _budget = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MonthlyBudgetUsd), "The budget can't be negative.");
    }

    /// <summary>The max_tokens of every API request, or null (the default) to size it from the
    /// effort (<see cref="ApiBrain.MaxTokensFor"/>). Values outside <see cref="MinMaxTokens"/> to
    /// <see cref="MaxMaxTokens"/> throw <see cref="ArgumentOutOfRangeException"/>.</summary>
    public int? MaxTokens
    {
        get => _maxTokens;
        init => _maxTokens = value is null or (>= MinMaxTokens and <= MaxMaxTokens)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MaxTokens), $"max_tokens must be from {MinMaxTokens} to {MaxMaxTokens}.");
    }

    /// <summary>Start the brain by itself when a run is in progress. /start sets it, /stop clears it.</summary>
    public bool Autostart { get; init; }

    /// <summary>The Claude Code binary for the subscription fallback, when it isn't in one of
    /// the usual places. Null when not configured.</summary>
    public string? ClaudePath { get; init; }

    /// <summary>The API key, or null. Never logged, never saved by the mod.</summary>
    public string? ApiKey { get; init; }

    public ApiKeySource ApiKeySource { get; init; }

    public bool HasApiKey => !string.IsNullOrEmpty(ApiKey);

    /// <summary>Problems found while loading (a bad value, an unknown key, an unreadable file),
    /// one sentence each, for a chat line or the log. Never contains the key. Not saved.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public string ConfigPath => Path.Combine(ConfigDir, ConfigFileName);

    public string ApiKeyPath => Path.Combine(ConfigDir, ApiKeyFileName);

    /// <summary>
    /// Reads config.json and the API key from <paramref name="configDir"/> (default
    /// <see cref="DefaultConfigDir"/>). Never throws for a missing, unreadable or invalid file:
    /// bad values fall back to their defaults and are reported in <see cref="Warnings"/>.
    /// </summary>
    /// <param name="getEnv">Environment lookup for tests; defaults to <see cref="Environment.GetEnvironmentVariable(string)"/>.</param>
    public static BrainSettings Load(string? configDir = null, Func<string, string?>? getEnv = null)
    {
        var dir = string.IsNullOrWhiteSpace(configDir) ? DefaultConfigDir : configDir;
        getEnv ??= Environment.GetEnvironmentVariable;
        var warnings = new List<string>();
        var settings = new BrainSettings { ConfigDir = dir };

        if (ReadConfig(settings.ConfigPath, warnings) is { } json)
            settings = Apply(settings, json, warnings);

        var (key, source) = ReadApiKey(settings.ApiKeyPath, getEnv, warnings);
        return settings with { ApiKey = key, ApiKeySource = source, Warnings = warnings };
    }

    /// <summary>
    /// Writes config.json atomically. Keys the mod doesn't know are kept; repo, max_tokens and
    /// claude_path are removed when null. A config.json that can't be parsed is first moved aside to
    /// config.json.bak so a hand edit with a typo isn't lost. Throws <see cref="IOException"/>
    /// or <see cref="UnauthorizedAccessException"/> when the file can't be written.
    /// </summary>
    public void Save()
    {
        var path = ConfigPath;
        var json = new JsonObject();
        if (File.Exists(path))
        {
            try
            {
                json = JsonNode.Parse(File.ReadAllText(path), documentOptions: ConfigFiles.ReadOptions) as JsonObject
                       ?? throw new JsonException("not an object");
            }
            catch (JsonException)
            {
                ConfigFiles.MoveAside(path);
                json = new JsonObject();
            }
        }

        SetOrRemove(json, "repo", Repo);
        json["model"] = Model;
        json["effort"] = Effort;
        SetOrRemove(json, "elite_effort", EliteEffort);
        SetOrRemove(json, "boss_effort", BossEffort);
        json["monthly_budget_usd"] = MonthlyBudgetUsd;
        if (MaxTokens is { } maxTokens) json["max_tokens"] = maxTokens;
        else json.Remove("max_tokens");
        json["autostart"] = Autostart;
        SetOrRemove(json, "claude_path", ClaudePath);
        ConfigFiles.WriteJson(path, json);
    }

    /// <summary>
    /// A full model id for <paramref name="model"/>: aliases (any case) are mapped, anything else
    /// is trimmed and lowercased, and an empty value gives <see cref="DefaultModel"/>.
    /// </summary>
    public static string ResolveModel(string? model)
    {
        var name = model?.Trim() ?? "";
        if (name.Length == 0)
            return DefaultModel;
        return ModelAliases.TryGetValue(name, out var id) ? id : name.ToLowerInvariant();
    }

    /// <summary>Like <see cref="ResolveModel"/>, but false unless the result is in the price
    /// table, so /model can refuse a typo.</summary>
    public static bool TryResolveModel(string? model, out string id)
    {
        id = ResolveModel(model);
        return !string.IsNullOrWhiteSpace(model) && ModelPrices.IsKnown(id);
    }

    /// <summary>True when <paramref name="text"/> is one of <see cref="Efforts"/> (any case, trimmed).</summary>
    public static bool TryParseEffort(string? text, out string effort)
    {
        effort = text?.Trim().ToLowerInvariant() ?? "";
        if (Efforts.Contains(effort))
            return true;
        effort = DefaultEffort;
        return false;
    }

    /// <summary>True when <paramref name="text"/> (any case, trimmed) is a value
    /// <see cref="EliteEffort"/> and <see cref="BossEffort"/> take: a level, <see cref="SameEffort"/>,
    /// or <see cref="DefaultWord"/> (and null), which gives null.</summary>
    public static bool TryParseFightEffort(string? text, out string? effort)
    {
        var word = text?.Trim().ToLowerInvariant() ?? DefaultWord;
        effort = null;
        if (word == DefaultWord)
            return true;
        if (word == SameEffort || Efforts.Contains(word))
        {
            effort = word;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The effort for a request made in <paramref name="room"/> (a RoomType name, or null):
    /// the elite or boss effort in those rooms (<see cref="EliteEffort"/>, <see cref="BossEffort"/>),
    /// <see cref="Effort"/> everywhere else. A fight effort the human chose is used as it is, lower
    /// than the normal effort too; the default one is <see cref="DefaultFightEffort"/> but never
    /// lower than <see cref="Effort"/>.
    /// </summary>
    public string EffortFor(string? room) =>
        string.Equals(room, EliteRoom, StringComparison.OrdinalIgnoreCase) ? FightEffort(EliteEffort)
        : string.Equals(room, BossRoom, StringComparison.OrdinalIgnoreCase) ? FightEffort(BossEffort)
        : Effort;

    private string FightEffort(string? fight) => fight switch
    {
        null => Rank(Effort) >= Rank(DefaultFightEffort) ? Effort : DefaultFightEffort,
        SameEffort => Effort,
        _ => fight,
    };

    private static int Rank(string effort) => Efforts.ToList().IndexOf(effort);

    private static string? CheckFightEffort(string? value, string name) =>
        TryParseFightEffort(value, out var effort)
            ? effort
            : throw new ArgumentException($"{name} must be one of {string.Join(", ", Efforts)}, {SameEffort} or {DefaultWord}.", name);

    /// <summary>The settings for logs and /budget, with the key shown only as where it came from.</summary>
    public override string ToString()
    {
        var key = ApiKeySource switch
        {
            ApiKeySource.File => "from " + ApiKeyFileName,
            ApiKeySource.Environment => "from " + ApiKeyEnvVar,
            _ => HasApiKey ? "set" : "none",
        };
        var sb = new StringBuilder("BrainSettings { ");
        sb.Append(CultureInfo.InvariantCulture, $"ConfigDir = {ConfigDir}, Model = {Model}, Effort = {Effort}, ");
        sb.Append(CultureInfo.InvariantCulture, $"EliteEffort = {EliteEffort ?? "default"}, BossEffort = {BossEffort ?? "default"}, ");
        sb.Append(CultureInfo.InvariantCulture, $"MonthlyBudgetUsd = {MonthlyBudgetUsd}, MaxTokens = {MaxTokens?.ToString(CultureInfo.InvariantCulture) ?? "by effort"}, Autostart = {Autostart}, ");
        sb.Append(CultureInfo.InvariantCulture, $"Repo = {Repo ?? "(none)"}, ClaudePath = {ClaudePath ?? "(none)"}, ApiKey = {key} }}");
        return sb.ToString();
    }

    private static JsonObject? ReadConfig(string path, List<string> warnings)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            if (JsonNode.Parse(File.ReadAllText(path), documentOptions: ConfigFiles.ReadOptions) is JsonObject json)
                return json;
            warnings.Add($"{ConfigFileName} isn't a JSON object; using the defaults.");
        }
        catch (JsonException ex)
        {
            warnings.Add($"{ConfigFileName} isn't valid JSON ({ex.Message}); using the defaults.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Couldn't read {ConfigFileName} ({ex.GetType().Name}); using the defaults.");
        }
        return null;
    }

    private static BrainSettings Apply(BrainSettings s, JsonObject json, List<string> warnings)
    {
        foreach (var (name, node) in json)
        {
            switch (name)
            {
                case "repo":
                    s = s with { Repo = Text(node, name, warnings) is { } repo ? ConfigFiles.ExpandHome(repo) : null };
                    break;

                case "model":
                    if (Text(node, name, warnings) is { } model)
                    {
                        s = s with { Model = model };
                        if (!ModelPrices.IsKnown(s.Model))
                            warnings.Add($"Model '{s.Model}' isn't in the price table; spend is estimated at the highest rates in it, so the cap may stop it early.");
                    }
                    break;

                case "effort":
                    if (Text(node, name, warnings) is { } effortText)
                    {
                        if (TryParseEffort(effortText, out var effort))
                            s = s with { Effort = effort };
                        else
                            warnings.Add($"effort '{effortText}' isn't one of {string.Join(", ", Efforts)}; using {DefaultEffort}.");
                    }
                    break;

                case "elite_effort":
                case "boss_effort":
                    // JSON null means the normal effort, like "same"; a left-out key means the default.
                    string? fight;
                    if (node is null)
                        fight = SameEffort;
                    else if (node is JsonValue f && f.TryGetValue<string>(out var fightText) && TryParseFightEffort(fightText, out var parsed))
                        fight = parsed;
                    else
                    {
                        warnings.Add($"{name} must be one of {string.Join(", ", Efforts)} or {SameEffort}; using {DefaultFightEffort}.");
                        fight = null;
                    }
                    s = name == "elite_effort" ? s with { EliteEffort = fight } : s with { BossEffort = fight };
                    break;

                case "monthly_budget_usd":
                    if (node is JsonValue v && v.TryGetValue<decimal>(out var budget) && budget >= 0)
                        s = s with { MonthlyBudgetUsd = budget };
                    else if (node is not null)
                        warnings.Add($"monthly_budget_usd must be a number of dollars, zero or more; using {DefaultMonthlyBudgetUsd}.");
                    break;

                case "max_tokens":
                    if (node is JsonValue t && t.TryGetValue<int>(out var maxTokens) && maxTokens is >= MinMaxTokens and <= MaxMaxTokens)
                        s = s with { MaxTokens = maxTokens };
                    else if (node is not null)
                        warnings.Add($"max_tokens must be a whole number from {MinMaxTokens} to {MaxMaxTokens}; sizing it from the effort.");
                    break;

                case "autostart":
                    if (node is JsonValue a && a.TryGetValue<bool>(out var autostart))
                        s = s with { Autostart = autostart };
                    else if (node is not null)
                        warnings.Add("autostart must be true or false; using false.");
                    break;

                case "claude_path":
                    s = s with { ClaudePath = Text(node, name, warnings) is { } claude ? ConfigFiles.ExpandHome(claude) : null };
                    break;

                default:
                    warnings.Add($"Unknown key '{name}' in {ConfigFileName}; ignored.");
                    break;
            }
        }
        return s;
    }

    // A non-empty trimmed string, or null (with a warning when the value has the wrong type).
    private static string? Text(JsonNode? node, string name, List<string> warnings)
    {
        if (node is null)
            return null;
        if (node is JsonValue v && v.TryGetValue<string>(out var text))
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        warnings.Add($"{name} must be a string; ignored.");
        return null;
    }

    private static (string? Key, ApiKeySource Source) ReadApiKey(string path, Func<string, string?> getEnv, List<string> warnings)
    {
        try
        {
            if (File.Exists(path))
            {
                var key = File.ReadAllText(path).Trim().Split('\n')[0].Trim();
                if (key.Length == 0)
                    warnings.Add($"{ApiKeyFileName} is empty.");
                else if (!LooksLikeKey(key))
                    warnings.Add($"{ApiKeyFileName} doesn't hold a usable key (it has spaces, quotes or other characters a key doesn't have); ignored.");
                else
                {
                    if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) != 0)
                        warnings.Add($"{ApiKeyFileName} can be read by other users; run chmod 600 on it.");
                    return (key, ApiKeySource.File);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Couldn't read {ApiKeyFileName} ({ex.GetType().Name}).");
        }

        var env = getEnv(ApiKeyEnvVar)?.Trim();
        if (string.IsNullOrEmpty(env))
            return (null, ApiKeySource.None);
        if (!LooksLikeKey(env))
        {
            warnings.Add($"{ApiKeyEnvVar} doesn't hold a usable key; ignored.");
            return (null, ApiKeySource.None);
        }
        return (env, ApiKeySource.Environment);
    }

    /// <summary>True when <paramref name="key"/> can go in an HTTP header as is: printable
    /// ASCII without spaces or quotes. Says nothing about whether the API accepts it.</summary>
    internal static bool LooksLikeKey(string key) =>
        key.Length > 0 && key.All(c => c > ' ' && c < 127 && c is not '"' and not '\'');

    private static void SetOrRemove(JsonObject json, string name, string? value)
    {
        if (value is null) json.Remove(name);
        else json[name] = value;
    }
}
