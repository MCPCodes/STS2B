namespace SpireMind.Core.Brain;

// /fast: the API's fast mode (BrainSettings.Fast), the same model at up to 2.5 times the output
// speed for twice the price. Off whenever the game starts and never saved: it lasts until
// /fast off or the game closes. See docs/brain.md, Fast mode.
public sealed partial class BrainController
{
    /// <summary>The argument hint /help shows for /fast.</summary>
    public const string FastUsage = "[on|off]";

    /// <summary>What /help says /fast does.</summary>
    public const string FastHelp = "shows or switches fast mode (answers up to 2.5x faster at twice the price)";

    /// <summary>The models that take fast mode, as the replies name them.</summary>
    public const string FastModels = "Opus 5.5, Opus 5 and Opus 4.8";

    /// <summary>The reply to /fast on when it is on.</summary>
    public const string FastAlreadyOn = "Fast mode is on already.";

    /// <summary>The reply to /fast off when it is off.</summary>
    public const string FastAlreadyOff = "Fast mode is off already.";

    /// <summary>The reply to /fast off.</summary>
    public const string FastSwitchedOff = "Fast mode off: back to normal speed and price.";

    /// <summary>Added to a /fast on reply while Claude Code plays: fast mode is the API's.</summary>
    public const string FastApiOnly = " Only the API brain uses it, so Claude Code playing now isn't affected.";

    /// <summary>
    /// Called by the API brain when the API turned fast mode down (its rate limit or capacity, or
    /// a refusal of the speed field or the beta flag) and the brain went on at standard speed:
    /// switches the setting off, so /fast and /budget say what is true and nothing asks for fast
    /// mode again until /fast on. The brain has already said why in the chat. Safe from any thread.
    /// </summary>
    public void FastDeclined()
    {
        lock (_gate)
        {
            if (!_settings.Fast)
                return;
            _settings = _settings with { Fast = false };
        }
        Log("brain: fast mode switched off, since the API turned it down");
    }

    // /fast, /fast on, /fast off. Nothing is read from or saved to config.json here.
    private string FastCommand(string from, string args)
    {
        var word = args.Trim().ToLowerInvariant();
        if (word.Length == 0)
            return FastStatus(Settings);
        if (word is not ("on" or "off"))
            return $"\"{args}\" isn't on or off. /fast shows whether fast mode is on; /fast on and /fast off switch it.";

        var on = word == "on";
        BrainSettings settings;
        bool subscription;
        lock (_gate)
        {
            settings = _settings;
            if (settings.Fast == on)
                return on ? FastAlreadyOn : FastAlreadyOff;
            // A request with the speed field is an error on any other model, so it stays off there.
            if (on && !ModelPrices.SupportsFast(settings.Model))
                return $"{FastOnlyOn(settings.Model)} It stays off.";
            settings = settings with { Fast = on };
            _settings = settings;
            subscription = _kind == BrainKind.Subscription;
        }
        Log($"brain: fast mode switched {word} by {from}");
        if (!on)
            return FastSwitchedOff;
        return $"Fast mode on: {FastPitch(settings.Model)}. /fast off to go back.{(subscription ? FastApiOnly : "")}";
    }

    // The reply to /fast without arguments: whether it is on, and what it costs on this model.
    private static string FastStatus(BrainSettings settings)
    {
        if (settings.Fast)
            return $"Fast mode is on: {FastPitch(settings.Model)}. /fast off to go back.";
        return ModelPrices.SupportsFast(settings.Model)
            ? $"Fast mode is off. /fast on makes {FastPitch(settings.Model)}."
            : $"Fast mode is off. {FastOnlyOn(settings.Model)}";
    }

    // "answers come up to 2.5x faster at twice the price ($8 / $40 per million tokens on Opus 5.5)"
    private static string FastPitch(string model) =>
        $"answers come up to 2.5x faster at twice the price ({FastPrice(model)} per million tokens on {ModelName(model)})";

    // "$8 / $40": the fast-mode input and output prices of a model that takes it.
    private static string FastPrice(string model) =>
        ModelPrices.FastFor(model) is { } price ? $"{Budget(price.Input)} / {Budget(price.Output)}" : "twice the usual";

    private static string FastOnlyOn(string model) =>
        $"Fast mode works on {FastModels} only, and the model is {ModelName(model)}.";

    // Added to the model and effort in /budget and the line that says who plays, while it is on.
    private static string FastNote(BrainSettings settings) => settings.Fast ? ", fast mode" : "";

    // Caller holds _gate. The settings a command built from an earlier read, with fast mode as it
    // stands now: only /fast switches it on, and the brain may have switched it off (FastDeclined)
    // since the command read them.
    private BrainSettings WithFastAsNow(BrainSettings settings) =>
        settings.Fast && !_settings.Fast ? settings with { Fast = false } : settings;
}
