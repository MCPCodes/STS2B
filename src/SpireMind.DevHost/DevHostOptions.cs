using System.Globalization;
using SpireMind.Core.Brain;

namespace SpireMind.DevHost;

/// <summary>Bad command-line arguments. The message says what is wrong; <see cref="DevHostOptions.Usage"/> says what works.</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>
/// The dev host's command line. Without <c>--brain</c> it serves the fake fight on the bridge
/// for Claude Code's /play, on the port given as the first argument (default 17772). With
/// <c>--brain</c> the API brain plays the fake fight with the real key and no game, as a smoke
/// test before the first in-game /start (see <see cref="BrainSmoke"/> and docs/brain.md).
/// </summary>
public sealed record DevHostOptions
{
    public const int DefaultPort = 17772;
    public const string DefaultModel = "haiku";
    public const int DefaultMaxRequests = 6;

    /// <summary>The most requests one smoke test may make, so a typo can't run up a bill.</summary>
    public const int MostRequests = 50;

    public const string Usage =
        "Usage:\n" +
        "  scripts/dev-host.sh [port]\n" +
        "      Serve a fake one-fight game on the bridge (default port 17772), for Claude Code's /play.\n" +
        "  scripts/dev-host.sh --brain [--model <name>] [--max-requests <n>]\n" +
        "      Let the API brain play the fake fight with your API key, no game needed: a smoke test of the\n" +
        "      real API calls. --model takes opus, sonnet, haiku, fable or a full model id (default haiku,\n" +
        "      for this run only). --max-requests caps the API calls (default 6, at most 50).";

    public int Port { get; init; } = DefaultPort;

    /// <summary>Run the brain smoke test instead of serving the bridge.</summary>
    public bool Brain { get; init; }

    /// <summary>The full model id the smoke test uses, resolved from --model.</summary>
    public string Model { get; init; } = BrainSettings.ResolveModel(DefaultModel);

    public int MaxRequests { get; init; } = DefaultMaxRequests;

    /// <summary>--help or -h: print <see cref="Usage"/> and exit.</summary>
    public bool Help { get; init; }

    /// <summary>
    /// Reads the arguments: an optional port first, then <c>--brain</c>, <c>--model &lt;name&gt;</c>,
    /// <c>--max-requests &lt;n&gt;</c> and <c>--help</c> in any order. Throws
    /// <see cref="UsageException"/> for anything else, a missing or bad value, or a brain option
    /// without <c>--brain</c>.
    /// </summary>
    public static DevHostOptions Parse(IReadOnlyList<string> args)
    {
        var options = new DevHostOptions();
        string? model = null;
        int? maxRequests = null;
        var port = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--brain":
                    options = options with { Brain = true };
                    break;
                case "--model":
                    model = Value(args, ref i);
                    break;
                case "--max-requests":
                    var text = Value(args, ref i);
                    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1 || n > MostRequests)
                        throw new UsageException($"--max-requests takes a whole number from 1 to {MostRequests}, not '{text}'.");
                    maxRequests = n;
                    break;
                case "--help" or "-h":
                    options = options with { Help = true };
                    break;
                default:
                    if (arg.StartsWith('-'))
                        throw new UsageException($"There's no {arg} option.");
                    if (i > 0 || port)
                        throw new UsageException($"Only the port can come without an option name, and only first ('{arg}').");
                    if (!int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out var p) || p < 1 || p > 65535)
                        throw new UsageException($"The port must be a number from 1 to 65535, not '{arg}'.");
                    options = options with { Port = p };
                    port = true;
                    break;
            }
        }

        if (!options.Brain && (model is not null || maxRequests is not null))
            throw new UsageException("--model and --max-requests only apply with --brain.");
        if (model is not null)
        {
            if (!BrainSettings.TryResolveModel(model, out var id))
                throw new UsageException($"'{model}' isn't a model the price table knows: use opus, sonnet, haiku, fable or a full model id.");
            options = options with { Model = id };
        }
        if (maxRequests is { } max)
            options = options with { MaxRequests = max };
        return options;
    }

    // The value after an option, which must be there and not be another option.
    private static string Value(IReadOnlyList<string> args, ref int i)
    {
        var name = args[i];
        if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new UsageException($"{name} needs a value.");
        return args[++i];
    }
}
