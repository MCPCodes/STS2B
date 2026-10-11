using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

public class InstanceRoleTests
{
    private const string Home = "/home/mark";
    private static readonly string Shared = Path.Combine(Home, ".config", "spiremind");

    private static Func<string, string?> Env(string? configDir = null) =>
        name => name == InstanceRole.ConfigDirEnvVar ? configDir : null;

    private static InstanceRole Detect(string[] args, int port = 17773, string? configDir = null) =>
        InstanceRole.Detect(args, port, Env(configDir), Home);

    [Theory]
    [InlineData("--sts2bot=1")]
    [InlineData("--sts2bot=true")]
    [InlineData("--sts2bot=TRUE")]
    [InlineData("--sts2bot=True")]
    [InlineData("--sts2bot", "1")]
    [InlineData("--sts2bot", "true")]
    public void The_flag_makes_a_bot_copy(params string[] flag)
    {
        var args = new[] { "--force-steam", "off", "--clientId", "1000" }.Concat(flag).Concat(["--fastmp", "join"]).ToArray();
        var role = Detect(args);
        Assert.Equal(InstanceKind.Bot, role.Kind);
        Assert.True(role.IsBot);
    }

    [Theory]
    [InlineData]
    [InlineData("--sts2bot=0")]
    [InlineData("--sts2bot=false")]
    [InlineData("--sts2bot=")]
    [InlineData("--sts2bot=yes")]
    [InlineData("--sts2bot")]
    [InlineData("--sts2bot", "--fastmp")]
    [InlineData("--STS2BOT=1")]
    [InlineData("--Sts2bot", "1")]
    [InlineData("--sts2bots=1")]
    [InlineData("--", "--sts2bot=1")]
    public void Anything_else_is_the_human_copy(params string[] args)
    {
        var role = Detect(args, port: 17772);
        Assert.Equal(InstanceKind.Human, role.Kind);
        Assert.False(role.IsBot);
        Assert.Empty(role.Warnings);
    }

    [Fact]
    public void A_copy_with_Steam_is_the_human_copy_whatever_its_flag_says()
    {
        // A stray --sts2bot=1 in Mark's Steam launch options.
        var role = InstanceRole.Detect(["--sts2bot=1"], 17772, Env(), Home, steamRunning: () => true);
        Assert.Equal(InstanceKind.Human, role.Kind);
        Assert.False(role.IsBot);
        Assert.Equal(Shared, role.ConfigDir);
        Assert.Equal("main", role.InstanceId);
        var warning = Assert.Single(role.Warnings);
        Assert.Contains("--sts2bot=1", warning);
        Assert.Contains("runs with Steam", warning);
        Assert.Contains("ignored", warning);

        // Without Steam the flag counts, and Steam is only asked about when the flag is there.
        var bot = InstanceRole.Detect(["--sts2bot=1"], 17773, Env(), Home, steamRunning: () => false);
        Assert.True(bot.IsBot);
        Assert.Empty(bot.Warnings);
        var asked = false;
        var human = InstanceRole.Detect(["--fastmp", "host_standard"], 17772, Env(), Home, steamRunning: () => asked = true);
        Assert.False(human.IsBot);
        Assert.False(asked);
        Assert.Empty(human.Warnings);
    }

    [Fact]
    public void A_copy_that_cant_tell_whether_Steam_runs_is_the_human_copy()
    {
        var role = InstanceRole.Detect(["--sts2bot", "1"], 17773, Env(), Home,
            steamRunning: () => throw new MissingMemberException("SteamInitializer.Initialized"));
        Assert.False(role.IsBot);
        Assert.Contains(role.Warnings, w => w.Contains("couldn't be told") && w.Contains("ignored"));
    }

    [Fact]
    public void The_human_copy_keeps_everything_in_the_shared_folder()
    {
        var role = Detect(["--fastmp", "host_standard"], port: 17772);
        Assert.Equal(Shared, role.ConfigDir);
        Assert.Equal(Shared, role.SharedConfigDir);
        Assert.Equal("main", role.InstanceId);
        Assert.Equal(NotesTools.Journal, role.JournalFile);
        Assert.Equal(Path.Combine(Shared, "locks", "notes.lock"), role.NotesLockPath);
        Assert.Equal(SubscriptionBrainOptions.DefaultPrompt, role.SubscriptionPrompt);
        Assert.Equal(17772, role.Port);
    }

    [Fact]
    public void A_bot_copy_gets_a_folder_and_journal_of_its_own()
    {
        var role = Detect(["--sts2bot=1"], port: 17774);
        Assert.Equal(Path.Combine(Shared, "bot-17774"), role.ConfigDir);
        Assert.Equal(Shared, role.SharedConfigDir);
        Assert.Equal("bot-17774", role.InstanceId);
        Assert.Equal("runs/bot-17774/current.md", role.JournalFile);
        Assert.Equal(Path.Combine(Shared, "locks", "notes.lock"), role.NotesLockPath);
        Assert.Empty(role.Warnings);
    }

    [Fact]
    public void A_bot_copys_subscription_prompt_names_its_journal()
    {
        var prompt = Detect(["--sts2bot=1"]).SubscriptionPrompt;
        Assert.StartsWith(SubscriptionBrainOptions.DefaultPrompt + ". ", prompt);
        Assert.Contains("runs/bot-17773/current.md instead of runs/current.md", prompt);
        Assert.Contains("archives go in runs/bot-17773/", prompt);
    }

    [Theory]
    [InlineData("~/sts2bot/config", "/home/mark/sts2bot/config")]
    [InlineData("  ~/sts2bot/config/  ", "/home/mark/sts2bot/config")]
    [InlineData("/tmp/spiremind-bot", "/tmp/spiremind-bot")]
    [InlineData("/tmp/spiremind-bot/", "/tmp/spiremind-bot")]
    [InlineData("/tmp/a/../spiremind-bot", "/tmp/spiremind-bot")]
    [InlineData("~", "/home/mark")]
    public void The_env_var_picks_the_folder(string value, string expected)
    {
        if (OperatingSystem.IsWindows()) return;
        var role = Detect(["--sts2bot=1"], configDir: value);
        Assert.Equal(expected, role.ConfigDir);
        Assert.Equal(Shared, role.SharedConfigDir);
    }

    [Fact]
    public void A_relative_env_folder_is_made_absolute()
    {
        var role = Detect(["--sts2bot=1"], configDir: "bot-config");
        Assert.True(Path.IsPathRooted(role.ConfigDir));
        Assert.Equal(Path.GetFullPath("bot-config"), role.ConfigDir);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_blank_env_var_counts_as_unset(string? value)
    {
        Assert.Equal(Path.Combine(Shared, "bot-17773"), Detect(["--sts2bot=1"], configDir: value).ConfigDir);
        Assert.Equal(Shared, Detect([], port: 17772, configDir: value).ConfigDir);
    }

    [Fact]
    public void The_env_var_moves_the_human_copys_folder_too()
    {
        var role = Detect([], port: 17772, configDir: "/tmp/mark-config");
        Assert.Equal("/tmp/mark-config", role.ConfigDir);
        Assert.Equal(Shared, role.SharedConfigDir);
        Assert.False(role.IsBot);
    }

    [Fact]
    public void An_env_value_that_cant_be_a_path_falls_back_with_a_warning()
    {
        var role = Detect(["--sts2bot=1"], configDir: "bad\0path");
        Assert.Equal(Path.Combine(Shared, "bot-17773"), role.ConfigDir);
        var warning = Assert.Single(role.Warnings);
        Assert.Contains(InstanceRole.ConfigDirEnvVar, warning);
        Assert.Contains("can't be a folder path", warning);
    }

    [Fact]
    public void A_bot_on_the_human_port_is_warned_about()
    {
        var role = Detect(["--sts2bot=1"], port: 17772);
        Assert.True(role.IsBot);
        var warning = Assert.Single(role.Warnings);
        Assert.Contains("clashes with the human copy", warning);
        Assert.Contains("SPIREMIND_PORT", warning);
    }

    [Fact]
    public void A_bot_sharing_the_human_folder_is_warned_about()
    {
        var role = Detect(["--sts2bot=1"], configDir: Shared + "/");
        Assert.Equal(Shared, role.ConfigDir);
        var warning = Assert.Single(role.Warnings);
        Assert.Contains("shares autostart and transcripts with the human copy", warning);
    }

    [Fact]
    public void Detect_never_throws_on_a_broken_environment()
    {
        var role = InstanceRole.Detect(["--sts2bot=1"], 17773, _ => throw new InvalidOperationException("no env"), Home);
        Assert.Equal(Path.Combine(Shared, "bot-17773"), role.ConfigDir);
    }

    [Fact]
    public void The_default_home_is_the_user_profile()
    {
        var role = InstanceRole.Detect([], 17772, _ => null);
        Assert.Equal(BrainSettings.DefaultConfigDir, role.SharedConfigDir);
        Assert.Equal(BrainSettings.DefaultConfigDir, role.ConfigDir);
    }

    [Theory]
    [InlineData(false, false, false, null)]
    [InlineData(false, true, false, InstanceRole.HumanCoopNoBotsLine)]
    [InlineData(false, false, true, null)] // solo play with a bot copy still running stays solo play
    [InlineData(false, true, true, InstanceRole.HumanCoopLine)]
    [InlineData(true, false, false, null)]
    [InlineData(true, true, false, null)]
    [InlineData(true, false, true, null)]
    [InlineData(true, true, true, null)]
    public void Who_may_run_a_brain(bool bot, bool inCoopRun, bool botsExist, string? expected)
    {
        var role = Detect(bot ? ["--sts2bot=1"] : [], port: bot ? 17773 : 17772);
        Assert.Equal(expected, role.BrainBlockedReason(inCoopRun, botsExist));
    }

    [Fact]
    public void ToString_says_what_and_where()
    {
        var text = Detect(["--sts2bot=1"]).ToString();
        Assert.Contains("bot copy bot-17773 on port 17773", text);
        Assert.Contains(Path.Combine(Shared, "bot-17773"), text);
        Assert.Contains("shared " + Shared, text);
        Assert.StartsWith("human copy main on port 17772", Detect([], port: 17772).ToString());
    }
}
