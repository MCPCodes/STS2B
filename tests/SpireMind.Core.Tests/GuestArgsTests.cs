using SpireMind.Core.Brain;
using SpireMind.Core.Coop;
using Xunit;

namespace SpireMind.Core.Tests;

/// <summary>
/// The launch options of a guest copy (docs/coop-guest.md), and the role they give. The other
/// half of the promise is in <see cref="The_hosts_and_the_bots_command_lines_are_never_a_guest"/>:
/// a line without an option of ours gives the role it gave before guests existed.
/// </summary>
public class GuestArgsTests
{
    private const string Home = "/home/mark";

    private static readonly string[] FriendLine =
        ["--fastmp=join", "--clientId=2000", "--spiremind-host=192.168.1.23", "--spiremind-name=Amir", "--spiremind-host-name=Mark"];

    private static InstanceRole Detect(string[] args, int port = 17772, Func<bool>? steam = null) =>
        InstanceRole.Detect(args, port, _ => null, Home, steam);

    // ---------- the line the instructions give ----------

    [Fact]
    public void The_friends_line_makes_a_guest_that_can_dial()
    {
        var guest = GuestArgs.Parse(FriendLine);
        Assert.True(guest.Asked);
        Assert.True(guest.IsGuest);
        Assert.True(guest.CanDial);
        Assert.Empty(guest.Problems);
        Assert.Equal("192.168.1.23", guest.Address);
        Assert.Equal(33771, guest.Port);
        Assert.Equal(2000UL, guest.ClientId);
        Assert.Equal("Amir", guest.Name);
        Assert.Equal("Mark", guest.HostName);
        Assert.Equal("Mark", guest.HostLabel);
    }

    [Fact]
    public void The_two_word_form_reads_the_same_as_the_equals_form()
    {
        // Steam passes the box's words on as they are; both forms are in use for the game's own flags.
        var guest = GuestArgs.Parse(["--fastmp", "join", "--clientId", "2000", "--spiremind-host", "10.250.203.164"]);
        Assert.True(guest.CanDial);
        Assert.Equal("10.250.203.164", guest.Address);
        Assert.Equal(2000UL, guest.ClientId);
        Assert.Null(guest.Name);
        Assert.Equal("the host", guest.HostLabel);
    }

    [Fact]
    public void The_role_of_the_friends_line_is_guest_with_or_without_Steam()
    {
        foreach (var steam in new Func<bool>?[] { null, () => true, () => false })
        {
            var role = Detect(FriendLine, steam: steam);
            Assert.Equal(InstanceKind.Guest, role.Kind);
            Assert.True(role.IsGuest);
            Assert.False(role.IsBot);
            Assert.Equal("guest", role.InstanceId);
            Assert.NotNull(role.Guest);
            Assert.True(role.Guest!.CanDial);
            Assert.Empty(role.Warnings);
            Assert.Contains("joins 192.168.1.23:33771 as player 2000", role.ToString());
        }
    }

    [Fact]
    public void A_guest_copy_never_may_run_a_brain()
    {
        var role = Detect(FriendLine);
        Assert.Equal(InstanceRole.GuestLine, role.BrainBlockedReason(inCoopRun: false, botsExist: false));
        Assert.Equal(InstanceRole.GuestLine, role.BrainBlockedReason(inCoopRun: true, botsExist: true));
    }

    [Fact]
    public void A_port_can_ride_on_the_address()
    {
        var guest = GuestArgs.Parse(["--fastmp=join", "--clientId=2000", "--spiremind-host=192.168.1.23:40000"]);
        Assert.True(guest.CanDial);
        Assert.Equal("192.168.1.23", guest.Address);
        Assert.Equal(40000, guest.Port);
    }

    // ---------- the host's and the bots' lines ----------

    public static TheoryData<string[], InstanceKind> OtherLines => new()
    {
        { [], InstanceKind.Human },
        { ["--fastmp", "host_standard"], InstanceKind.Human },
        { ["--fastmp=host_standard"], InstanceKind.Human },
        { ["--fastmp", "host"], InstanceKind.Human },
        { ["--fastmp", "join"], InstanceKind.Human },
        { ["--fastmp=join", "--clientId=2000"], InstanceKind.Human },
        { ["--clientId=2000"], InstanceKind.Human },
        // What CoopBots.Launch starts a bot copy with.
        { ["--force-steam=off", "--clientId=1000", "--fastmp=join", "--sts2bot=1", "--sts2bot-hostpid=4242", "--sts2bot-character=SILENT",
            "--log-file", "/Users/mark/sts2bot/godot-1000.log", "--resolution", "640x360", "--position", "40,60"], InstanceKind.Bot },
        // What scripts/coop-bot.sh starts one with.
        { ["--force-steam", "off", "--clientId", "1000", "--fastmp", "join", "--sts2bot=1", "--log-file", "/tmp/x.log"], InstanceKind.Bot },
    };

    [Theory]
    [MemberData(nameof(OtherLines))]
    public void The_hosts_and_the_bots_command_lines_are_never_a_guest(string[] args, InstanceKind kind)
    {
        var guest = GuestArgs.Parse(args);
        Assert.False(guest.Asked);
        Assert.False(guest.IsGuest);
        Assert.False(guest.CanDial);
        Assert.Empty(guest.Problems);

        var role = Detect(args, port: kind == InstanceKind.Bot ? 17773 : 17772);
        Assert.Equal(kind, role.Kind);
        Assert.False(role.IsGuest);
        Assert.Null(role.Guest);
        Assert.Empty(role.Warnings);
        Assert.Equal(kind == InstanceKind.Bot ? "bot-17773" : "main", role.InstanceId);
        Assert.DoesNotContain("guest", role.ToString());
    }

    [Fact]
    public void An_environment_variable_never_makes_a_guest()
    {
        // The role comes from the command line only, so a bot copy can't inherit it from the host's game.
        var role = InstanceRole.Detect(["--fastmp", "host_standard"], 17772,
            name => name.StartsWith("SPIREMIND", StringComparison.Ordinal) ? "192.168.1.23" : null, Home);
        Assert.Equal(InstanceKind.Human, role.Kind);
    }

    [Theory]
    [InlineData("host_standard")]
    [InlineData("host")]
    [InlineData("host_custom")]
    [InlineData("host_daily")]
    [InlineData("load")]
    public void A_copy_that_hosts_is_never_a_guest(string fastMp)
    {
        // The guest option pasted into the host's own launch options by mistake.
        string[] args = ["--fastmp", fastMp, "--clientId=2000", "--spiremind-host=192.168.1.23"];
        var guest = GuestArgs.Parse(args);
        Assert.True(guest.Asked);
        Assert.True(guest.Hosting);
        Assert.False(guest.IsGuest);
        Assert.False(guest.CanDial);

        var role = Detect(args);
        Assert.Equal(InstanceKind.Human, role.Kind);
        Assert.Null(role.Guest);
        Assert.Equal("main", role.InstanceId);
        Assert.Single(role.Warnings);
        Assert.Contains("ignored", role.Warnings[0]);
    }

    [Fact]
    public void A_bot_copy_stays_a_bot_copy()
    {
        string[] args = ["--force-steam=off", "--clientId=1000", "--fastmp=join", "--sts2bot=1", "--spiremind-host=192.168.1.23"];
        var role = Detect(args, port: 17773);
        Assert.Equal(InstanceKind.Bot, role.Kind);
        Assert.Null(role.Guest);
        Assert.Contains(role.Warnings, w => w.Contains("--spiremind-host") && w.Contains("ignored"));
    }

    [Fact]
    public void A_bot_flag_that_Steam_cancels_leaves_a_guest()
    {
        // With Steam the bot flag never counts; the guest option still does.
        string[] args = ["--sts2bot=1", "--fastmp=join", "--clientId=2000", "--spiremind-host=192.168.1.23"];
        var role = Detect(args, steam: () => true);
        Assert.Equal(InstanceKind.Guest, role.Kind);
        Assert.True(role.Guest!.CanDial);
    }

    // ---------- lines that are wrong still give a guest, one that says why ----------

    [Theory]
    [InlineData("--spiremind-host")]                    // no value
    [InlineData("--spiremind-host=")]
    [InlineData("--spiremind-host=Marks-Mac.local")]    // a name: refused, the game does not check what the engine makes of it
    [InlineData("--spiremind-host=192.168.1")]
    [InlineData("--spiremind-host=192.168.1.256")]
    [InlineData("--spiremind-host=192.168.01.23")]
    [InlineData("--spiremind-host=192.168.1.23.4")]
    [InlineData("--spiremind-host=0.0.0.0")]
    [InlineData("--spiremind-host=255.255.255.255")]
    [InlineData("--spiremind-host=224.0.0.251")]
    [InlineData("--spiremind-host=192.168.1.23:")]
    [InlineData("--spiremind-host=192.168.1.23:0")]
    [InlineData("--spiremind-host=192.168.1.23:70000")]
    [InlineData("--spiremind-host=fe80::1")]
    [InlineData("--spiremind-host=<address>")]
    public void An_address_that_cant_be_used_is_a_problem_not_a_dial(string hostFlag)
    {
        var guest = GuestArgs.Parse(["--fastmp=join", "--clientId=2000", hostFlag]);
        Assert.True(guest.IsGuest);
        Assert.False(guest.CanDial);
        Assert.Null(guest.Address);
        Assert.Single(guest.Problems);
        Assert.Contains("--spiremind-host", guest.Problems[0]);

        // Still a guest copy: a typo must never start the host's machinery on the friend's computer.
        Assert.Equal(InstanceKind.Guest, Detect(["--fastmp=join", "--clientId=2000", hostFlag]).Kind);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.0.1")]
    [InlineData("172.16.254.3")]
    [InlineData("100.75.54.34")]
    [InlineData(" 10.0.0.5 ")]
    public void A_dotted_address_is_taken(string address)
    {
        Assert.True(GuestArgs.TryAddress(address, out var parsed, out var port, out _));
        Assert.Equal(address.Trim(), parsed);
        Assert.Equal(GuestArgs.LobbyPort, port);
    }

    [Theory]
    [InlineData(null, "missing")]                 // the game's default would be 1000, the first bot's
    [InlineData("--clientId=", "missing")]
    [InlineData("--clientId=abc", "plain number")]
    [InlineData("--clientId=+2000", "plain number")]
    [InlineData("--clientId=2000.0", "plain number")]
    [InlineData("--clientId=0", "host's own")]
    [InlineData("--clientId=1", "host's own")]
    [InlineData("--clientId=1000", "bots'")]
    [InlineData("--clientId=1001", "bots'")]
    [InlineData("--clientId=1002", "bots'")]
    public void A_player_id_that_cant_be_used_is_a_problem_not_a_dial(string? idFlag, string says)
    {
        var args = new List<string> { "--fastmp=join", "--spiremind-host=192.168.1.23" };
        if (idFlag is not null) args.Add(idFlag);
        var guest = GuestArgs.Parse(args);
        Assert.True(guest.IsGuest);
        Assert.False(guest.CanDial);
        Assert.Null(guest.ClientId);
        Assert.Single(guest.Problems);
        Assert.Contains(says, guest.Problems[0]);
        Assert.Contains("NOT joining", Detect(args.ToArray()).ToString());
    }

    [Theory]
    [InlineData("--clientId=2000", 2000UL)]
    [InlineData("--clientId=3000", 3000UL)]
    [InlineData("--clientId=1003", 1003UL)]
    [InlineData("--clientId=999", 999UL)]
    [InlineData("--clientId=76561198000000000", 76561198000000000UL)]
    public void Any_other_player_id_is_taken(string idFlag, ulong id)
    {
        var guest = GuestArgs.Parse(["--fastmp=join", "--spiremind-host=192.168.1.23", idFlag]);
        Assert.True(guest.CanDial);
        Assert.Equal(id, guest.ClientId);
    }

    [Theory]
    [InlineData]                       // the Join screen would list Steam friends and never dial
    [InlineData("--fastmp")]
    [InlineData("--fastmp=joins")]
    [InlineData("--fastmp", "Join")]
    public void Without_fastmp_join_nothing_is_dialed(params string[] fastMp)
    {
        var args = fastMp.Concat(["--clientId=2000", "--spiremind-host=192.168.1.23"]).ToArray();
        var guest = GuestArgs.Parse(args);
        Assert.True(guest.IsGuest);
        Assert.False(guest.CanDial);
        Assert.Single(guest.Problems);
        Assert.Contains("--fastmp", guest.Problems[0]);
    }

    [Theory]
    [InlineData("--Spiremind-Host=192.168.1.23", true)]     // our flags ignore case
    [InlineData("--SPIREMIND-HOST=192.168.1.23", true)]
    [InlineData("-spiremind-host=192.168.1.23", true)]      // the game's reader drops any number of dashes
    [InlineData("--spiremind-hots=192.168.1.23", false)]    // a typo: a guest copy with a problem
    [InlineData("--spiremind-name=Amir", false)]            // the address forgotten
    public void A_mistyped_option_of_ours_still_makes_a_guest_copy(string flag, bool canDial)
    {
        string[] args = ["--fastmp=join", "--clientId=2000", flag];
        var guest = GuestArgs.Parse(args);
        Assert.True(guest.Asked);
        Assert.True(guest.IsGuest);
        Assert.Equal(canDial, guest.CanDial);
        Assert.Equal(canDial, guest.Problems.Count == 0);
        Assert.Equal(InstanceKind.Guest, Detect(args).Kind);
    }

    [Theory]
    [InlineData("\u2014fastmp=join", "--clientId=2000", "--spiremind-host=10.0.0.5")]   // one option hit
    [InlineData("\u2014fastmp=join", "\u2014clientId=2000", "\u2014spiremind-host=10.0.0.5")]   // the whole line, as a chat app sends it
    [InlineData("--fastmp=join", "--clientId=2000", "\u2013spiremind-host=10.0.0.5")]
    public void A_long_dash_from_a_chat_app_is_named_as_the_problem(params string[] args)
    {
        var guest = GuestArgs.Parse(args);
        Assert.True(guest.IsGuest);
        Assert.False(guest.CanDial);
        Assert.Single(guest.Problems);
        Assert.Contains("long dash", guest.Problems[0]);
        Assert.Equal(InstanceKind.Guest, Detect(args).Kind);
    }

    [Fact]
    public void A_long_dash_elsewhere_makes_no_guest()
    {
        // Not one of ours: the host's own line with a mangled dash stays the host's.
        var args = new[] { "\u2014fastmp", "host_standard" };
        Assert.False(GuestArgs.Parse(args).Asked);
        Assert.Equal(InstanceKind.Human, Detect(args).Kind);
        Assert.Empty(Detect(args).Warnings);
    }

    [Fact]
    public void Every_problem_is_listed_at_once()
    {
        var guest = GuestArgs.Parse(["--spiremind-host=nowhere"]);
        Assert.True(guest.IsGuest);
        Assert.False(guest.CanDial);
        Assert.Equal(3, guest.Problems.Count);
    }

    [Fact]
    public void Names_are_cleaned_for_a_nameplate()
    {
        var guest = GuestArgs.Parse(["--fastmp=join", "--clientId=2000", "--spiremind-host=10.0.0.5",
            "--spiremind-name=  [b]Amir[/b]\t", "--spiremind-host-name=" + new string('M', 60)]);
        Assert.True(guest.CanDial);
        Assert.Equal("bAmir/b", guest.Name);
        Assert.Equal(CoopSeats.MaxNameLength, guest.HostName!.Length);
    }

    [Fact]
    public void The_command_line_is_read_as_the_game_reads_it()
    {
        // Leading dashes dropped, "=" or the next word, a next word that starts with - or + is no value, the last one wins.
        var table = GuestArgs.Table(["--fastmp", "join", "-clientId=5", "--clientId=2000", "--flag", "--other", "+connect_lobby", "42", "x=y=z", "--", "tail"]);
        Assert.Equal("join", table["fastmp"]);
        Assert.Equal("2000", table["clientId"]);
        Assert.Null(table["flag"]);
        Assert.Null(table["other"]);
        Assert.Equal("42", table["+connect_lobby"]);
        Assert.Equal("y=z", table["x"]);
        Assert.Equal("tail", table[""]);
        Assert.False(table.ContainsKey("ClientId"));
        Assert.Equal(7, table.Count);
    }

    [Fact]
    public void Odd_input_never_throws()
    {
        Assert.False(GuestArgs.Parse(null).Asked);
        Assert.False(GuestArgs.Parse([null!, "", "=", "-", "--", "---"]).Asked);
        var guest = GuestArgs.Parse(["--spiremind-host=" + new string('9', 5000), "--clientId=" + new string('9', 5000), "--fastmp=join"]);
        Assert.True(guest.IsGuest);
        Assert.False(guest.CanDial);
        Assert.All(guest.Problems, p => Assert.True(p.Length < 400));
    }

    // ---------- who is a guest, and what they are called ----------

    [Theory]
    [InlineData(1UL, false)]
    [InlineData(999UL, false)]
    [InlineData(1000UL, true)]
    [InlineData(1001UL, true)]
    [InlineData(1002UL, true)]
    [InlineData(1003UL, false)]
    [InlineData(2000UL, false)]
    public void The_bots_have_three_ids(ulong id, bool bot) => Assert.Equal(bot, CoopSeats.IsBotId(id));

    [Theory]
    [InlineData(1UL, false)]      // the host
    [InlineData(1000UL, false)]   // a bot
    [InlineData(1002UL, false)]
    [InlineData(2000UL, true)]    // the id the instructions give
    [InlineData(3000UL, true)]
    [InlineData(1003UL, true)]
    public void A_guest_is_neither_the_host_nor_a_bot(ulong id, bool guest) => Assert.Equal(guest, CoopSeats.IsGuest(id, hostNetId: 1));

    [Fact]
    public void Guests_go_by_the_hosts_names_in_seat_order()
    {
        var names = CoopSeats.ParseNames(" Amir , Sam,, ");
        Assert.Equal(["Amir", "Sam"], names);
        Assert.Equal("Amir", CoopSeats.GuestName(names, 0));
        Assert.Equal("Sam", CoopSeats.GuestName(names, 1));
        Assert.Equal("Guest 3", CoopSeats.GuestName(names, 2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(",")]
    public void A_guest_nobody_named_is_Guest(string? env)
    {
        var names = CoopSeats.ParseNames(env);
        Assert.Empty(names);
        Assert.Equal("Guest", CoopSeats.GuestName(names, 0));
        Assert.Equal("Guest 2", CoopSeats.GuestName(names, 1));
        Assert.Equal("Guest", CoopSeats.GuestName(null, -1));
    }

    [Fact]
    public void The_host_names_a_guest_in_a_file_or_in_the_environment()
    {
        var dir = Directory.CreateTempSubdirectory("spiremind-guest-names-").FullName;
        try
        {
            Func<string, string?> noEnv = _ => null;
            Func<string, string?> env = name => name == CoopSeats.GuestNameEnvVar ? "Sam" : null;

            // Nothing set: nobody is named.
            Assert.Empty(CoopSeats.ReadNames(noEnv, dir));
            Assert.Empty(CoopSeats.ReadNames(noEnv, null));
            Assert.Empty(CoopSeats.ReadNames(null, Path.Combine(dir, "missing")));

            // The file, as "echo Amir > guest_name" writes it.
            File.WriteAllText(Path.Combine(dir, CoopSeats.GuestNameFile), "Amir\n");
            Assert.Equal(["Amir"], CoopSeats.ReadNames(noEnv, dir));
            Assert.Equal(["Amir"], CoopSeats.ReadNames(null, dir));

            // The environment wins when it names anyone; an empty variable does not hide the file.
            Assert.Equal(["Sam"], CoopSeats.ReadNames(env, dir));
            Assert.Equal(["Amir"], CoopSeats.ReadNames(name => name == CoopSeats.GuestNameEnvVar ? " , " : null, dir));
            Assert.Equal(["Amir"], CoopSeats.ReadNames(_ => throw new InvalidOperationException("no environment"), dir));

            // Several guests, a line each or with commas; a huge file is read only at its start.
            File.WriteAllText(Path.Combine(dir, CoopSeats.GuestNameFile), "Amir\r\nSam, Kim\n" + new string('x', 100_000));
            var names = CoopSeats.ReadNames(noEnv, dir);
            Assert.Equal(["Amir", "Sam", "Kim"], names.Take(3));
            Assert.Equal(4, names.Count);
            Assert.Equal(CoopSeats.MaxNameLength, names[3].Length);

            // Only that one file is ever opened: the key file beside it is not a name source.
            File.WriteAllText(Path.Combine(dir, "api_key"), "not-a-name");
            File.Delete(Path.Combine(dir, CoopSeats.GuestNameFile));
            Assert.Empty(CoopSeats.ReadNames(noEnv, dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------- the line on the game's popup ----------

    [Theory]
    [InlineData("Timeout", "Nobody answered")]
    [InlineData("HandshakeTimeout", "Nobody answered")]
    [InlineData("ModMismatch", "BetterSpire2")]
    [InlineData("VersionMismatch", "version")]
    [InlineData("LobbyFull", "four players")]
    [InlineData("RunInProgress", "already started")]
    [InlineData("Kicked", "player 2000")]
    [InlineData("NotInSaveGame", "saved run")]
    [InlineData("Quit", "left the lobby")]
    [InlineData("HostAbandoned", "abandoned")]
    [InlineData("StateDivergence", "out of step")]
    [InlineData("UnknownNetworkError", "No bug report")]
    [InlineData("InternalError", "the log")]
    public void Every_reason_the_game_shows_gets_a_next_step(string reason, string says)
    {
        var line = GuestAdvice.ForPopup(reason, GuestArgs.Parse(FriendLine));
        Assert.StartsWith("SpireMind guest: Mark's game is at 192.168.1.23:33771; you are player 2000.", line);
        Assert.Contains(says, line);
        // Drawn as BBCode by the game's popup, and it must fit: no brackets, not long.
        Assert.DoesNotContain("[", line);
        Assert.True(line.Length < 330, line);
    }

    [Fact]
    public void An_unknown_reason_still_says_where_this_copy_dialed()
    {
        var line = GuestAdvice.ForPopup("SomethingNew", GuestArgs.Parse(["--fastmp=join", "--clientId=2000", "--spiremind-host=10.0.0.5"]));
        Assert.Equal("SpireMind guest: the host's game is at 10.0.0.5:33771; you are player 2000.", line);
        // A sentence that starts with the host starts with a capital.
        Assert.EndsWith(" The host has to send a bot away.", GuestAdvice.For("LobbyFull", GuestArgs.Parse(["--spiremind-host=10.0.0.5"])));
        Assert.StartsWith("SpireMind guest: the launch options are not complete.", GuestAdvice.ForPopup("Timeout", GuestArgs.Parse(["--spiremind-host=10.0.0.5"])));
    }
}
