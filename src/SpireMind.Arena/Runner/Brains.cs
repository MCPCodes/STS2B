namespace SpireMind.Arena;

/// <summary>
/// The two ways a fight is driven (<c>--brain</c>), as the options, the result lines and the
/// labels name them. docs/arena.md, "Two ways to drive a fight", says what each is and costs.
/// </summary>
public static class Brains
{
    /// <summary>Claude Code headless on the subscription (<see cref="ClaudeFight"/>): the default,
    /// and what every fight was played by before there were two.</summary>
    public const string ClaudeCode = "claude-code";

    /// <summary>The mod's own brain, <c>SpireMind.Core.Brain.ApiBrain</c>, calling the Claude API
    /// directly on the API credit (<see cref="ApiFight"/>): the program that plays the real game.</summary>
    public const string Api = "api";

    public static readonly IReadOnlyList<string> All = [ClaudeCode, Api];

    /// <summary>Options that only mean something when Claude Code drives the fight.</summary>
    public static readonly IReadOnlyList<string> ClaudeCodeOnly = ["--brief", "--effort-by-tier", "--claude", "--grace-seconds", "--stop-at-weekly"];

    /// <summary>Options that only mean something when the API brain drives the fight.</summary>
    public static readonly IReadOnlyList<string> ApiOnly = ["--yes-spend", "--max-usd", "--elite-effort", "--boss-effort", "--after-fight", "--fake-api", "--only"];

    /// <summary>Why an option of Claude Code's doesn't go with the API brain, as a sentence.</summary>
    public static string WhyNot(string option) => option switch
    {
        "--brief" => "the brain writes its own first message, and SpireMind.Core has no place to add the pages to it.",
        "--effort-by-tier" => "the brain picks the effort by the fight's room itself; set it with --effort, --elite-effort and --boss-effort.",
        "--claude" => "no Claude Code is started.",
        "--grace-seconds" => "the brain is stopped by the arena itself once the fight is over (--after-fight says how many requests later).",
        "--stop-at-weekly" => "nothing runs on the subscription; --max-usd is the limit.",
        _ => "it is an option of --brain claude-code.",
    };

    /// <summary>The game's room type (a snapshot's <c>state.run.room</c>) of a scenario tier's
    /// fights, which is what the brain's settings pick the effort by.</summary>
    public static string RoomOf(string tier) => tier switch
    {
        Scenarios.EliteTier => "Elite",
        Scenarios.BossTier => "Boss",
        _ => "Monster",
    };
}
