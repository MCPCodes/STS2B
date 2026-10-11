// The mod compiles Core with global usings for Godot and the game, and Godot has its own HttpClient.
using HttpMessageHandler = System.Net.Http.HttpMessageHandler;

namespace SpireMind.Core.Coop;

/// <summary>How the co-op relay in the human's copy finds and talks to the bot copies.</summary>
public sealed record CoopRelayOptions
{
    /// <summary>The environment variable with the bot ports to probe ("17773,17774"); the caller
    /// reads it and passes <see cref="ParsePorts"/>' result as <see cref="ProbePorts"/>.</summary>
    public const string PortsEnvVar = "SPIREMIND_BOT_PORTS";

    /// <summary>The ports bot copies use by default, one per bot (SPIREMIND_PORT).</summary>
    public static IReadOnlyList<int> DefaultProbePorts { get; } = [17773, 17774, 17775];

    /// <summary>This copy's own bridge port, never contacted.</summary>
    public required int OwnPort { get; init; }

    public IReadOnlyList<int> ProbePorts { get; init; } = DefaultProbePorts;

    /// <summary>How often each linked bot's chat log is read when there is nothing to send.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How often ports without a linked bot are tried.</summary>
    public TimeSpan ProbeInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan PollTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan PostTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>What <see cref="StartTimeout"/> is unless set.</summary>
    public static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromMinutes(3);

    /// <summary>How long /bot add waits for the game side to start a bot copy before it says the
    /// copy didn't come up and takes /bot add again.</summary>
    public TimeSpan StartTimeout { get; init; } = DefaultStartTimeout;

    /// <summary>Failed polls in a row before a bot copy counts as gone.</summary>
    public int DownAfterFailures { get; init; } = 3;

    /// <summary>The most bot copies linked at once (and started with /bot add).</summary>
    public int MaxBots { get; init; } = 3;

    /// <summary>The most lines from one bot passed to the other bots in a minute.</summary>
    public int PartyLinesPerMinute { get; init; } = 10;

    /// <summary>Whether bots hear each other's lines at first; /party changes it.</summary>
    public bool PartyChat { get; init; } = true;

    /// <summary>Whether the party's planning sessions open by themselves at first (act start,
    /// mid-act, before the boss); /plan on|off changes it. /plan works either way.</summary>
    public bool Planning { get; init; } = true;

    /// <summary>Where the party plan is written (runs/party/plan.md under the notes root), or null
    /// to keep it in memory only.</summary>
    public string? PartyPlanFile { get; init; }

    /// <summary>The human's name ("Mark"), for the planning sessions' chat lines and the plan.</summary>
    public string? HumanName { get; init; }

    /// <summary>The human's character for the plan's heading, or null. Called with no lock held;
    /// it may read the chat hub.</summary>
    public Func<string?>? HumanCharacter { get; init; }

    /// <summary>The planning sessions' timings; tests shorten them.</summary>
    public PartyTimings PartyTimings { get; init; } = new();

    /// <summary>For tests. Null uses <see cref="CoopRelay.NewLoopbackHandler"/>: no proxy, no redirects.</summary>
    public HttpMessageHandler? Handler { get; init; }

    /// <summary>
    /// The ports in <paramref name="text"/> ("17773,17774", commas or spaces), each once, or
    /// null when the text is null or blank or has no usable port. Entries that aren't a port
    /// (1 to 65535) are skipped and named in <paramref name="warning"/>, which is null when
    /// everything was fine.
    /// </summary>
    public static IReadOnlyList<int>? ParsePorts(string? text, out string? warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var ports = new List<int>();
        var bad = new List<string>();
        foreach (var entry in text.Split([',', ' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(entry, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port)
                && port is >= 1 and <= 65535)
            {
                if (!ports.Contains(port)) ports.Add(port);
            }
            else
            {
                bad.Add(entry);
            }
        }

        if (ports.Count == 0)
        {
            warning = $"{PortsEnvVar} has no port in it ('{text.Trim()}'), so the default ports are used.";
            return null;
        }
        if (bad.Count > 0)
            warning = $"{PortsEnvVar}: skipped {string.Join(", ", bad.Select(b => $"'{b}'"))}, which {(bad.Count == 1 ? "isn't a port" : "aren't ports")}.";
        return ports;
    }
}
