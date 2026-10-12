using System.Globalization;
using SpireMind.Core.Brain;
using SpireMind.Core.Coop;

namespace SpireMind.DevHost;

/// <summary>Bad command-line arguments. The message says what is wrong; <see cref="DevHostOptions.Usage"/> says what works.</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>
/// The dev host's command line. With no mode it serves the fake fight on the bridge for Claude
/// Code's /play, on the port given as the first argument (default 17772). With <c>--brain</c>
/// the API brain plays the fake fight with the real key and no game, as a smoke test before the
/// first in-game /start (see <see cref="BrainSmoke"/> and docs/brain.md). <c>--bot</c> and
/// <c>--relay</c> try co-op chat on localhost without the game: a fake bot copy, and a fake
/// human copy that relays its chat to the bot copies as Mark's game does (see <see cref="DevCoop"/>).
/// </summary>
public sealed record DevHostOptions
{
    public const int DefaultPort = 17772;
    public const string DefaultModel = "haiku";
    public const int DefaultMaxRequests = 6;

    /// <summary>The most requests one smoke test may make, so a typo can't run up a bill.</summary>
    public const int MostRequests = 50;

    /// <summary>The character a fake bot copy plays unless --character says otherwise.</summary>
    public const string DefaultCharacter = "Ironclad";

    /// <summary>The first bot copy's port; its NetId is 1000 unless --net-id says otherwise, the
    /// next port's 1001, and so on.</summary>
    public const int FirstBotPort = 17773;

    public const ulong FirstBotNetId = 1000;

    /// <summary>What --character takes, in any case, with or without "The".</summary>
    public static IReadOnlyList<string> Characters { get; } = ["Ironclad", "Silent", "Defect", "Regent", "Necrobinder"];

    public const string Usage =
        "Usage:\n" +
        "  scripts/dev-host.sh [port]\n" +
        "      Serve a fake one-fight game on the bridge (default port 17772), for Claude Code's /play.\n" +
        "  scripts/dev-host.sh --brain [--model <name>] [--max-requests <n>] [--fast]\n" +
        "      Let the API brain play the fake fight with your API key, no game needed: a smoke test of the\n" +
        "      real API calls. --model takes opus, sonnet, haiku, fable or a full model id (default haiku,\n" +
        "      for this run only). --max-requests caps the API calls (default 6, at most 50). --fast asks for\n" +
        "      the API's fast mode as /fast on does in game (twice the price; it needs --model opus), to see\n" +
        "      whether your key has it before a game depends on it.\n" +
        "  scripts/dev-host.sh <port> --bot [--character <name>] [--net-id <n>] [--echo]\n" +
        "      A fake bot copy for trying co-op chat without the game; give it a bot port (17773 to 17775).\n" +
        "      It reports itself as a bot copy playing the character (Ironclad, Silent, Defect, Regent or\n" +
        "      Necrobinder; default Ironclad) with the NetId (default 1000 on 17773, 1001 on 17774 and so on),\n" +
        "      and answers /start, /stop, /budget, /model, /effort and /fast with a stub line. With --echo it\n" +
        "      answers each message for it with \"Heard you, <name>.\"\n" +
        "  scripts/dev-host.sh [port] --relay [--bot-ports <list>]\n" +
        "      A fake human copy that relays its chat to the bot copies on localhost, as Mark's game does: type\n" +
        "      lines and commands (hello, /pause, /model Silent sonnet, /bots, /bot add Regent) and see what the\n" +
        "      bots answer. --bot-ports lists the ports to look on, such as 17773,17774 (default\n" +
        "      SPIREMIND_BOT_PORTS, else 17773, 17774 and 17775).";

    public int Port { get; init; } = DefaultPort;

    /// <summary>Run the brain smoke test instead of serving the bridge.</summary>
    public bool Brain { get; init; }

    /// <summary>The full model id the smoke test uses, resolved from --model.</summary>
    public string Model { get; init; } = BrainSettings.ResolveModel(DefaultModel);

    public int MaxRequests { get; init; } = DefaultMaxRequests;

    /// <summary>--fast: the smoke test asks for the API's fast mode, as /fast on does in game.</summary>
    public bool Fast { get; init; }

    /// <summary>--help or -h: print <see cref="Usage"/> and exit.</summary>
    public bool Help { get; init; }

    /// <summary>Run a fake bot copy for co-op chat instead of serving the fake fight.</summary>
    public bool Bot { get; init; }

    /// <summary>Run a fake human copy that relays its chat to the bot copies.</summary>
    public bool Relay { get; init; }

    /// <summary>The character the fake bot copy plays, from --character.</summary>
    public string Character { get; init; } = DefaultCharacter;

    /// <summary>The NetId the fake bot copy reports, from --net-id; null for <see cref="DefaultNetId"/>.</summary>
    public ulong? NetId { get; init; }

    /// <summary>The fake bot copy answers each message for it (--echo).</summary>
    public bool Echo { get; init; }

    /// <summary>The ports the fake human copy looks for bot copies on, from --bot-ports; null
    /// for SPIREMIND_BOT_PORTS or the defaults.</summary>
    public IReadOnlyList<int>? BotPorts { get; init; }

    /// <summary>The NetId the fake bot copy reports: --net-id, else <see cref="DefaultNetId"/> for its port.</summary>
    public ulong BotNetId => NetId ?? DefaultNetId(Port);

    /// <summary>1000 on 17773, 1001 on 17774 and so on, as the bot copies' clientIds go; at
    /// least 2, since 1 is the host's.</summary>
    public static ulong DefaultNetId(int port) => (ulong)Math.Max(2L, (long)FirstBotNetId + port - FirstBotPort);

    /// <summary>
    /// Reads the arguments: an optional port first, then in any order at most one mode
    /// (<c>--brain</c>, <c>--bot</c> or <c>--relay</c>), the brain's <c>--model &lt;name&gt;</c>,
    /// <c>--max-requests &lt;n&gt;</c> and <c>--fast</c>, the bot's <c>--character &lt;name&gt;</c>,
    /// <c>--net-id &lt;n&gt;</c> and <c>--echo</c>, the relay's <c>--bot-ports &lt;list&gt;</c>,
    /// and <c>--help</c>. Throws <see cref="UsageException"/> for anything else, a missing or bad
    /// value, two modes, or an option without its mode.
    /// </summary>
    public static DevHostOptions Parse(IReadOnlyList<string> args)
    {
        var options = new DevHostOptions();
        string? model = null;
        int? maxRequests = null;
        var fast = false;
        var port = false;
        string? character = null;
        ulong? netId = null;
        var echo = false;
        IReadOnlyList<int>? botPorts = null;

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
                case "--fast":
                    fast = true;
                    break;
                case "--bot":
                    options = options with { Bot = true };
                    break;
                case "--relay":
                    options = options with { Relay = true };
                    break;
                case "--character":
                    var name = Value(args, ref i);
                    character = CharacterNamed(name)
                        ?? throw new UsageException($"--character takes {string.Join(", ", Characters.Take(Characters.Count - 1))} or {Characters[^1]}, not '{name}'.");
                    break;
                case "--net-id":
                    var id = Value(args, ref i);
                    if (!ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var nid) || nid < 1)
                        throw new UsageException($"--net-id takes a whole number of 1 or more, not '{id}'.");
                    netId = nid;
                    break;
                case "--echo":
                    echo = true;
                    break;
                case "--bot-ports":
                    var list = Value(args, ref i);
                    var parsed = CoopRelayOptions.ParsePorts(list, out var problem);
                    if (parsed is null || problem is not null)
                        throw new UsageException($"--bot-ports takes ports from 1 to 65535 separated by commas, such as 17773,17774, not '{list}'.");
                    botPorts = parsed;
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

        if ((options.Brain ? 1 : 0) + (options.Bot ? 1 : 0) + (options.Relay ? 1 : 0) > 1)
            throw new UsageException("--brain, --bot and --relay are different modes: use one of them.");
        if (!options.Brain && (model is not null || maxRequests is not null))
            throw new UsageException("--model and --max-requests only apply with --brain.");
        if (!options.Brain && fast)
            throw new UsageException("--fast only applies with --brain.");
        if (!options.Bot && (character is not null || netId is not null || echo))
            throw new UsageException("--character, --net-id and --echo only apply with --bot.");
        if (!options.Relay && botPorts is not null)
            throw new UsageException("--bot-ports only applies with --relay.");
        options = options with { Character = character ?? DefaultCharacter, NetId = netId, Echo = echo, BotPorts = botPorts };
        if (model is not null)
        {
            if (!BrainSettings.TryResolveModel(model, out var id))
                throw new UsageException($"'{model}' isn't a model the price table knows: use opus, sonnet, haiku, fable or a full model id.");
            options = options with { Model = id };
        }
        if (maxRequests is { } max)
            options = options with { MaxRequests = max };
        // A request that asks for fast mode is an error on any other model.
        if (fast && !ModelPrices.SupportsFast(options.Model))
            throw new UsageException($"--fast needs a model that takes fast mode (Opus 5.5, Opus 5 or Opus 4.8), not {options.Model}: add --model opus.");
        options = options with { Fast = fast };
        return options;
    }

    // A character's name as the chat uses it ("Silent" for "the silent"), or null for none.
    private static string? CharacterNamed(string text)
    {
        var name = text.Trim();
        if (name.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
            name = name[4..].TrimStart();
        return Characters.FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
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
