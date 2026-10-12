using System.Globalization;

namespace SpireMind.Core.Coop;

/// <summary>
/// The launch options of a guest copy: a friend's own game, on another computer, that joins
/// the host's local co-op lobby (docs/coop-guest.md). The friend pastes one line into Steam's
/// launch options:
/// <code>--fastmp=join --clientId=2000 --spiremind-host=192.168.1.23 --spiremind-name=Amir --spiremind-host-name=Mark</code>
/// <list type="bullet">
/// <item><c>--spiremind-host=&lt;address&gt;[:port]</c> makes the copy a guest and says where the
/// host's game is: a dotted IPv4 address, and the lobby's UDP port when it is not 33771.</item>
/// <item><c>--fastmp=join</c> and <c>--clientId=&lt;number&gt;</c> are the game's own flags. The first
/// makes its Join screen dial instead of listing Steam friends; the second is the player id the
/// copy joins with, and also the id the game takes for "me" when it books the run's result, so
/// it must be there and must be the number that is dialed with.</item>
/// <item><c>--spiremind-name</c> and <c>--spiremind-host-name</c> are what this copy calls its own
/// player and the host on the nameplates. Both are optional.</item>
/// </list>
/// The line is read the way the game reads its command line (see <see cref="Table"/>), because
/// two of the flags are the game's. Flag names of ours are matched without regard to case, and
/// any option that starts with <c>--spiremind-</c> counts as asking for a guest copy: a mistyped
/// line then gives a guest copy that says what is wrong, never a copy that starts the host's
/// machinery on someone else's computer.
///
/// Pure and immutable; <see cref="Parse"/> never throws.
/// </summary>
public sealed record GuestArgs
{
    /// <summary>The flag that makes a guest copy and names the host's address, without its dashes.</summary>
    public const string HostFlag = "spiremind-host";

    /// <summary>What the guest's copy calls its own player.</summary>
    public const string NameFlag = "spiremind-name";

    /// <summary>What the guest's copy calls the host.</summary>
    public const string HostNameFlag = "spiremind-host-name";

    /// <summary>Every option of ours starts with this.</summary>
    public const string OwnPrefix = "spiremind-";

    /// <summary>The game's own flags, as its command-line reader names them.</summary>
    public const string ClientIdFlag = "clientId";

    /// <inheritdoc cref="ClientIdFlag"/>
    public const string FastMpFlag = "fastmp";

    /// <summary>The value of <c>--fastmp</c> that makes the game's Join screen dial.</summary>
    public const string FastMpJoin = "join";

    /// <summary>The UDP port a lobby opened with <c>--fastmp host_standard</c> listens on.</summary>
    public const int LobbyPort = 33771;

    /// <summary>The player id the instructions give a guest. The host is 1 and the bots are 1000 to 1002.</summary>
    public const ulong SuggestedClientId = 2000;

    /// <summary>The line the messages show as the example to copy.</summary>
    public const string Example = "--fastmp=join --clientId=2000 --spiremind-host=192.168.1.23";

    /// <summary>True when an option of ours is on the command line: the copy was meant to be a guest.</summary>
    public bool Asked { get; init; }

    /// <summary>True when the same line makes this copy host or load a lobby (<c>--fastmp host...</c>
    /// or <c>load</c>). Such a copy is never a guest.</summary>
    public bool Hosting { get; init; }

    /// <summary>The host's address as four numbers with dots; null when none usable was given.</summary>
    public string? Address { get; init; }

    /// <summary>The host's UDP port.</summary>
    public int Port { get; init; } = LobbyPort;

    /// <summary>The player id from <c>--clientId</c>; null when it is missing or can't be used.</summary>
    public ulong? ClientId { get; init; }

    /// <summary>What this copy calls its own player, or null for the default.</summary>
    public string? Name { get; init; }

    /// <summary>What this copy calls the host, or null for the default.</summary>
    public string? HostName { get; init; }

    /// <summary>What is wrong with the line, one sentence each, in the words a player can act
    /// on. With any problem the copy does not dial.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>True when this copy is a guest: asked for, and not hosting.</summary>
    public bool IsGuest => Asked && !Hosting;

    /// <summary>True when the copy may dial: a guest with an address, a player id and no problem.</summary>
    public bool CanDial => IsGuest && Problems.Count == 0 && Address is not null && ClientId is not null;

    /// <summary>What to call the host in a sentence.</summary>
    public string HostLabel => HostName ?? "the host";

    /// <summary>
    /// Reads a command line. Never throws: whatever can't be used becomes a problem.
    /// </summary>
    /// <param name="args">The command line as Godot's OS.GetCmdlineArgs gives it.</param>
    public static GuestArgs Parse(IReadOnlyList<string>? args)
    {
        try
        {
            args ??= [];
            return Read(Table(args), LongDashed(args));
        }
        catch (Exception ex)
        {
            // Not reachable by any input known; kept so that a guest's game can never fail to load over its options.
            return new GuestArgs { Problems = [$"The launch options couldn't be read ({ex.GetType().Name})."] };
        }
    }

    /// <summary>
    /// The command line as a table, the way the game's own reader builds its: leading dashes
    /// are dropped; "key=value" is one entry; otherwise the next word is the value unless it
    /// starts with "-" or "+"; keys are case-sensitive and the last one wins.
    /// </summary>
    public static Dictionary<string, string?> Table(IReadOnlyList<string> args)
    {
        var table = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var word = (args[i] ?? "").TrimStart('-');
            var eq = word.IndexOf('=');
            if (eq > 0)
            {
                table[word[..eq]] = word[(eq + 1)..];
                continue;
            }

            string? value = null;
            if (i + 1 < args.Count && args[i + 1] is { } next && !next.StartsWith('-') && !next.StartsWith('+'))
            {
                value = next;
                i++;
            }
            table[word] = value;
        }
        return table;
    }

    /// <summary>
    /// The words that start with a long dash where "--" belongs. A chat app or a text editor
    /// turns two minus signs into one long dash, and the game then does not see an option at
    /// all. Told to the player instead of failing without a word.
    /// </summary>
    private static List<string> LongDashed(IReadOnlyList<string> args) =>
        args.Where(a => a is { Length: > 0 } && IsLongDash(a[0])).ToList();

    private static bool IsLongDash(char c) => c is '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2015' or '\u2212';

    private static GuestArgs Read(Dictionary<string, string?> table, List<string> longDashed)
    {
        // Our own options, whatever their case; an unknown one is a problem, not a silent miss.
        string? host = null, name = null, hostName = null;
        var hostGiven = false;
        var asked = false;
        var problems = new List<string>();
        // One of ours behind a long dash still means "this was meant to be a guest copy".
        foreach (var word in longDashed)
            if (word.TrimStart('-', '\u2010', '\u2011', '\u2012', '\u2013', '\u2014', '\u2015', '\u2212').StartsWith(OwnPrefix, StringComparison.OrdinalIgnoreCase))
                asked = true;
        foreach (var (key, value) in table)
        {
            if (!key.StartsWith(OwnPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            asked = true;
            if (key.Equals(HostFlag, StringComparison.OrdinalIgnoreCase))
            {
                hostGiven = true;
                host = value;
            }
            else if (key.Equals(NameFlag, StringComparison.OrdinalIgnoreCase)) name = value;
            else if (key.Equals(HostNameFlag, StringComparison.OrdinalIgnoreCase)) hostName = value;
            else problems.Add($"--{Shown(key)} is not an option SpireMind knows. The line should look like: {Example}");
        }

        table.TryGetValue(FastMpFlag, out var fastMp);
        var hosting = fastMp is not null
                      && (fastMp.StartsWith("host", StringComparison.Ordinal) || fastMp.Equals("load", StringComparison.Ordinal));
        if (!asked)
            return new GuestArgs { Hosting = hosting };

        // The address.
        string? address = null;
        var port = LobbyPort;
        if (!hostGiven)
            problems.Add($"--{HostFlag}=<the host's address> is missing. The line should look like: {Example}");
        else if (!TryAddress(host, out address, out port, out var why))
            problems.Add($"--{HostFlag} needs the host's address: four numbers with dots, like 192.168.1.23. {why}");

        // The player id, by the game's own flag.
        ulong? clientId = null;
        if (!table.TryGetValue(ClientIdFlag, out var rawId) || string.IsNullOrWhiteSpace(rawId))
            problems.Add($"--{ClientIdFlag}={SuggestedClientId} is missing. Without it the game would join as a bot's player and book the run under the host's.");
        else if (!ulong.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            problems.Add($"--{ClientIdFlag} must be a plain number such as {SuggestedClientId}, not '{Shown(rawId)}'.");
        else if (id is 0 or 1)
            problems.Add($"--{ClientIdFlag}={id} can't be used: 1 is the host's own player. Use {SuggestedClientId}.");
        else if (CoopSeats.IsBotId(id))
            problems.Add($"--{ClientIdFlag}={id} can't be used: {CoopSeats.FirstBotId} to {CoopSeats.FirstBotId + CoopSeats.BotIds - 1} are the bots' players. Use {SuggestedClientId}.");
        else
            clientId = id;

        // Without this the game's Join screen lists Steam friends and never dials.
        if (!hosting && fastMp != FastMpJoin)
            problems.Add(fastMp is null
                ? $"--{FastMpFlag}={FastMpJoin} is missing. Without it the game never dials the host."
                : $"--{FastMpFlag} must be '{FastMpJoin}', not '{Shown(fastMp)}'.");

        // A long dash is the cause of whatever else is "missing": say that, and only that.
        if (longDashed.Count > 0)
            problems = [$"'{Shown(longDashed[0])}' starts with a long dash where two minus signs (--) belong, so the game does not see it. " +
                        "Copy the line again from README.txt, not from a chat message."];

        return new GuestArgs
        {
            Asked = true,
            Hosting = hosting,
            Address = address,
            Port = port,
            ClientId = clientId,
            Name = CoopSeats.CleanName(name),
            HostName = CoopSeats.CleanName(hostName),
            Problems = problems,
        };
    }

    /// <summary>
    /// Reads "a.b.c.d" or "a.b.c.d:port". Only the plain dotted form: four numbers from 0 to
    /// 255 without leading zeros. A name ("Marks-Mac.local") is refused, since the game hands
    /// the text to the engine as it is and does not check what comes back.
    /// </summary>
    public static bool TryAddress(string? text, out string? address, out int port, out string why)
    {
        address = null;
        port = LobbyPort;
        why = "";
        var raw = text?.Trim() ?? "";
        if (raw.Length == 0)
        {
            why = "None was given.";
            return false;
        }

        var host = raw;
        var colon = raw.IndexOf(':');
        if (colon >= 0)
        {
            host = raw[..colon];
            var portText = raw[(colon + 1)..];
            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
            {
                port = LobbyPort;
                why = $"'{Shown(portText)}' is not a port number.";
                return false;
            }
        }

        var parts = host.Split('.');
        var numbers = new int[4];
        var ok = parts.Length == 4;
        for (var i = 0; ok && i < 4; i++)
        {
            var part = parts[i];
            ok = part.Length is >= 1 and <= 3
                 && (part.Length == 1 || part[0] != '0')
                 && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])
                 && numbers[i] <= 255;
        }
        if (!ok)
        {
            why = $"'{Shown(host)}' is not one.";
            return false;
        }
        // 0.x is "this network", 224 and up are multicast, reserved and broadcast: none is a computer.
        if (numbers[0] == 0 || numbers[0] >= 224)
        {
            why = $"'{Shown(host)}' is not the address of a computer.";
            return false;
        }

        address = string.Join('.', numbers);
        return true;
    }

    /// <summary>Text from the command line, made safe to print: short, and nothing but plain characters.</summary>
    private static string Shown(string? text)
    {
        var s = text ?? "";
        if (s.Length > 40) s = s[..40] + "...";
        return new string(s.Select(c => char.IsControl(c) || c is '[' or ']' ? '?' : c).ToArray());
    }
}
