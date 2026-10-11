using System.Globalization;

namespace SpireMind.Game.Coop;

/// <summary>
/// The default rules of a polite co-op bot, as pure functions over plain values. The screen
/// readers gather the facts on the main thread, ask these functions, hold back what must not
/// be done yet and put the returned sentences into the snapshot ("vote.do", "co_op"), which is
/// what the brain and the prompt rely on. The game facts behind every rule are in
/// sts2-notes/coop-api-v0.107.1/votes.md; section numbers below point there.
///
/// The rules, in one place:
/// <list type="bullet">
/// <item>Follow the human's vote (map node, shared event option). While they have not voted,
/// wait <see cref="Patience"/> of their time on that screen, then vote and say which
/// (<see cref="Vote"/>).</item>
/// <item>Never cast the vote that moves the party while the human who voted is busy on
/// another screen, for at most <see cref="BusyHoldCap"/> (<see cref="HoldFor"/>).</item>
/// <item>At a chest, let the human pick first and take a different relic (<see cref="Relic"/>).</item>
/// <item>End the turn when out of useful plays; the bot has no undo, so it can't take an
/// end of turn back by accident (<see cref="CombatNote"/>).</item>
/// <item>Proceed only once its own choices in the room are made; the map vote is the last
/// thing it sends from a room (<see cref="RoomClosed"/>, <see cref="UnclaimedWarning"/>).</item>
/// <item>Never block the group: every wait on the bot has an action that ends it
/// (<see cref="Gates"/>), and every wait of the bot on a human runs out.</item>
/// </list>
/// Who counts as "the human" is the readers' business (Teammates.Followed): on the human's own
/// copy nobody does, and every rule here then answers as in a solo run.
///
/// Nothing in this file touches a game or Godot type, so it can move to SpireMind.Core with only
/// its namespace changed once its checks should run under scripts/test.sh.
/// </summary>
internal static class CoopManners
{
    // ---------- how long the bot waits for the human ----------

    /// <summary>
    /// Seconds the bot lets the human choose first (map node, shared event option, chest relic)
    /// before it chooses on its own. 0 never waits; "never" or a negative number always waits.
    /// </summary>
    public const string WaitEnv = "SPIREMIND_COOP_WAIT";

    public static readonly TimeSpan DefaultPatience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest the bot holds a vote that would move the party while the human looks busy on
    /// another screen. The screen report is one unreliable message, so it is a hint and this
    /// cap keeps a stale one from blocking the group (1.5).
    /// </summary>
    public static readonly TimeSpan BusyHoldCap = TimeSpan.FromSeconds(45);

    /// <summary>The same for a screen that could be either ("in the room": a shop, or just done).</summary>
    public static readonly TimeSpan UnsureHold = TimeSpan.FromSeconds(4);

    public static TimeSpan Patience { get; } = ParsePatience(System.Environment.GetEnvironmentVariable(WaitEnv));

    internal static TimeSpan ParsePatience(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return DefaultPatience;
        if (text.Equals("never", StringComparison.OrdinalIgnoreCase)) return TimeSpan.MaxValue;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || double.IsNaN(seconds))
            return DefaultPatience;
        if (seconds < 0 || seconds > 86400) return TimeSpan.MaxValue;
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>How long to hold a party-moving vote for a human who looks busy (or might be).</summary>
    public static TimeSpan HoldFor(bool busy, bool unsure, TimeSpan patience)
    {
        var cap = busy ? BusyHoldCap : unsure ? UnsureHold : TimeSpan.Zero;
        return patience < cap ? patience : cap;
    }

    /// <summary>
    /// Time spent waiting on a human, counted only on the polls where the wait applies, so
    /// "Mark has been looking at the map for 30 s" does not count the minute he spent on his
    /// rewards. It restarts whenever the thing waited on changes (a new room, a new page).
    /// Polls further apart than <see cref="MaxStep"/> count as that much: nobody was watching.
    /// </summary>
    internal sealed class WaitClock
    {
        public static readonly TimeSpan MaxStep = TimeSpan.FromSeconds(2);

        private object? _key;
        private DateTime _last;

        public TimeSpan Elapsed { get; private set; }

        public TimeSpan Tick(object key, bool running, DateTime now)
        {
            if (!Equals(_key, key))
            {
                _key = key;
                _last = now;
                Elapsed = TimeSpan.Zero;
                return Elapsed;
            }

            var step = now - _last;
            _last = now;
            if (running && step > TimeSpan.Zero)
                Elapsed += step < MaxStep ? step : MaxStep;
            return Elapsed;
        }
    }

    // ---------- votes the host draws from: the map and shared events ----------

    public enum VoteMove
    {
        /// <summary>No human teammate to follow: choose as in a solo run.</summary>
        Free,

        /// <summary>The human has not voted yet: do not vote, wait for them.</summary>
        WaitForHuman,

        /// <summary>Vote for what the human chose: a unanimous vote is certain.</summary>
        Follow,

        /// <summary>The human voted but looks busy elsewhere, and our vote would move the party now: hold it.</summary>
        HoldForHuman,

        /// <summary>The human stayed quiet past the wait: vote for our own pick and say which.</summary>
        VoteFirst,

        /// <summary>Our vote stands and nothing asks for a change: only wait.</summary>
        Keep,

        /// <summary>We voted before the human, who still has not: keep it, change it only when asked.</summary>
        Standing,

        /// <summary>The human voted for something else and the round is still open: change to theirs.</summary>
        Switch,
    }

    /// <summary>What a reader knows about one vote round. Options are indexes into the screen's option list.</summary>
    /// <param name="Mine">Our standing vote, or null.</param>
    /// <param name="Humans">Each human teammate's vote: null while they have not voted, negative when it is not one of the options.</param>
    /// <param name="OthersMissing">Other players, human or not, whose vote is still out. 0 means our vote decides the round.</param>
    /// <param name="WaitedForVote">How long we have waited for a human to vote first.</param>
    /// <param name="Patience">How long that wait may last.</param>
    /// <param name="WaitedForBusy">How long we have held a deciding vote for a busy human.</param>
    /// <param name="HoldFor">How long that hold may last; zero when the human is where voters wait.</param>
    public readonly record struct VoteFacts(
        int? Mine,
        IReadOnlyList<int?> Humans,
        int OthersMissing,
        TimeSpan WaitedForVote,
        TimeSpan Patience,
        TimeSpan WaitedForBusy = default,
        TimeSpan HoldFor = default);

    /// <param name="Option">The human's choice, for the moves that follow or defer to it.</param>
    public readonly record struct VoteAdvice(VoteMove Move, int? Option = null)
    {
        /// <summary>True when the bot has something to do now; false while it only waits.</summary>
        public bool Decision =>
            Move is VoteMove.Free or VoteMove.Follow or VoteMove.VoteFirst or VoteMove.Standing or VoteMove.Switch;
    }

    /// <summary>
    /// Follow the human's vote; while they have not voted, wait a while, then vote and say
    /// which (1.5, 4.3). The draw happens the instant the last vote lands and can't be taken
    /// back, so voting first hands the human a coin flip unless they agree: hence the wait.
    /// </summary>
    public static VoteAdvice Vote(VoteFacts f)
    {
        if (f.Humans.Count == 0)
            return new VoteAdvice(f.Mine is null ? VoteMove.Free : VoteMove.Keep);

        var theirs = HumanChoice(f.Humans);
        if (f.Mine is null)
        {
            if (theirs is null)
            {
                // A human vote that is none of our options can't be followed, and waiting won't change it.
                var unfollowable = f.Humans.Any(v => v is < 0);
                return new VoteAdvice(!unfollowable && f.WaitedForVote < f.Patience ? VoteMove.WaitForHuman : VoteMove.VoteFirst);
            }
            // Completing the round pulls the party out under the human's hands (1.5).
            if (f.OthersMissing == 0 && f.WaitedForBusy < f.HoldFor)
                return new VoteAdvice(VoteMove.HoldForHuman, theirs);
            return new VoteAdvice(VoteMove.Follow, theirs);
        }

        if (theirs is null) return new VoteAdvice(VoteMove.Standing);
        return new VoteAdvice(theirs == f.Mine ? VoteMove.Keep : VoteMove.Switch, theirs);
    }

    /// <summary>The option most humans voted for; the earliest voter in the list wins a tie (the host comes first).</summary>
    internal static int? HumanChoice(IReadOnlyList<int?> humans)
    {
        int? best = null;
        var bestCount = 0;
        foreach (var vote in humans)
        {
            if (vote is not { } v || v < 0) continue;
            var count = 0;
            foreach (var other in humans)
                if (other == v) count++;
            if (count > bestCount)
            {
                best = v;
                bestCount = count;
            }
        }
        return best;
    }

    /// <summary>The sentence for "vote.do", or for the snapshot's waiting line when the move is a wait.</summary>
    /// <param name="human">Who is followed, e.g. "Mark (p0)".</param>
    /// <param name="theirs">What they voted for, as the screen names it.</param>
    /// <param name="mine">What our vote stands on.</param>
    public static string VoteLine(VoteMove move, string human, string? theirs, string? mine) => move switch
    {
        VoteMove.Free => "No human teammate to follow: vote for your own pick.",
        VoteMove.WaitForHuman => $"waiting for {human} to vote first",
        VoteMove.Follow => $"{human} voted for {theirs}: vote for it too. A unanimous vote is certain.",
        VoteMove.HoldForHuman => $"{human} voted for {theirs} but is still busy on another screen; following once they are done",
        VoteMove.VoteFirst => $"{human} has not given you a vote to follow: vote for your own pick now and say in chat which one. It can be changed until the last vote lands.",
        VoteMove.Keep => $"voted for {mine}; waiting for the other players to vote",
        VoteMove.Standing => $"You voted for {mine} before {human}. Keep it; change it only if {human} asks for something else before voting. Otherwise just wait.",
        VoteMove.Switch => $"{human} voted for {theirs} but your vote is on {mine}: change your vote to follow.",
        _ => "",
    };

    /// <summary>
    /// The longest the bot holds out for a human vote it cannot follow because this copy of
    /// the game refuses the click (a node off screen while a controller is in use, a flag that
    /// never came on). The human has voted, so without a vote of ours nobody travels: past
    /// this the other options are offered, and a split vote is the smaller harm.
    /// </summary>
    public static readonly TimeSpan FollowBlockedCap = TimeSpan.FromSeconds(20);

    /// <summary>"vote.do" once <see cref="FollowBlockedCap"/> has run out.</summary>
    public static string CannotFollowLine(string human, string theirs) =>
        $"{human} voted for {theirs}, but this copy of the game will not take a click on it. Vote for another option and " +
        $"say so in chat, so that {human} can move their vote too: a split vote is a coin flip.";

    public const string MapRule =
        "Co-op map vote: the party moves when every player has voted. The host then draws one of the votes at random, " +
        "so a unanimous vote is certain and a split is a coin flip. A vote can be changed until the last one lands. " +
        "Your vote is the last thing you do in a room: nothing may be sent after it.";

    public const string EventRule =
        "Shared event: every player votes on every page, a one-option page included. The host draws one of the votes " +
        "at random and the winning option is applied to every player.";

    /// <summary>
    /// "vote.do" on a shared page with a single option: there is nothing to agree on and every
    /// player has to vote for it, so the bot does not wait for the human there (4.3).
    /// </summary>
    public const string LoneOptionLine =
        "This page has a single option, and every player has to vote for it before anything happens: vote for it now.";

    /// <summary>
    /// "vote.unconfirmed": a human vote the reader holds back as a possible leftover from the
    /// page before (the host's phantom vote, 4.2). The bot can't tell it from a real one, so
    /// the model is told: a real vote that goes unseen would turn the bot's own pick into a coin flip.
    /// </summary>
    /// <param name="human">Who the vote belongs to, e.g. "Mark (p0)".</param>
    /// <param name="option">The option it is on, as the screen names it.</param>
    public static string UnconfirmedVote(string human, string option) =>
        $"{human} already showed a vote for {option} when this page appeared. The game can repeat a vote the host cast on " +
        "the page before, so it is not followed unless it changes. If it is real, voting for the same option is certain " +
        "and any other is a coin flip: ask in chat when unsure.";

    /// <summary>Why no potion is offered once our vote on a shared page is in (0.2 item 12, list E items 5 and 14).</summary>
    public const string VoteBeforePotion =
        "your vote on this page is in: the last vote decides it at once, and a potion sent now could arrive while a fight it starts is being set up";

    // ---------- the treasure chest ----------

    public enum RelicMove
    {
        /// <summary>No human teammate: pick as in a solo run.</summary>
        Free,

        /// <summary>A human has not picked yet: let them choose first.</summary>
        WaitForHuman,

        /// <summary>Pick now, preferring a relic no human has voted for.</summary>
        PickAround,

        /// <summary>Our vote is in: only wait.</summary>
        Voted,
    }

    /// <summary>
    /// Let the humans pick their relic first, then take a different one: everybody gets what
    /// they chose and nothing is fought over (3.3). Past the wait, pick anyway and say which,
    /// because without our vote nobody gets a relic and nobody can leave.
    /// </summary>
    /// <param name="mine">Our own vote is in.</param>
    /// <param name="humans">Number of human teammates.</param>
    /// <param name="humansVoted">How many of them have voted.</param>
    public static RelicMove Relic(bool mine, int humans, int humansVoted, TimeSpan waited, TimeSpan patience)
    {
        if (mine) return RelicMove.Voted;
        if (humans <= 0) return RelicMove.Free;
        if (humansVoted >= humans) return RelicMove.PickAround;
        return waited < patience ? RelicMove.WaitForHuman : RelicMove.PickAround;
    }

    public static string RelicLine(RelicMove move, string human, bool humanVoted) => move switch
    {
        RelicMove.WaitForHuman => $"waiting for {human} to pick a relic first",
        RelicMove.PickAround when humanVoted => $"{human} has picked: take a relic nobody has voted for, so nothing is fought over.",
        RelicMove.PickAround => $"{human} has not picked for a while: vote for the relic you want and say in chat which one.",
        RelicMove.Voted => "your vote is in; waiting for the other players",
        _ => "",
    };

    public const string RelicRule =
        "Co-op chest: each player opens their own chest and votes for one relic. A relic with one voter goes to that " +
        "voter; a contested one is settled by a random rock-paper-scissors and the loser gets a relic nobody took. " +
        "Nothing is awarded until every player has voted.";

    // ---------- rooms: what to know to neither block the group nor leave early ----------

    public const string RewardsNote =
        "Rewards are yours alone and nobody waits here. Proceed opens your map; the party leaves when every player " +
        "has voted there. Decide every reward before you proceed.";

    public const string BossRewardsNote =
        "Rewards are yours alone. Proceed is your vote to leave the act: it cannot be taken back, and the act changes " +
        "the moment every player has pressed it, so take your rewards first and touch nothing after.";

    public const string ShopNote =
        "Each player has their own stock and nobody waits here. Buy first, then leave: the party moves when every " +
        "player has voted on the map.";

    public const string RestNote =
        "Each player chooses for themselves and nobody waits here. Mend heals a teammate by their own rest amount " +
        "instead of healing you. Proceed shows once your choice has played out; the party moves when every player " +
        "has voted on the map.";

    public const string EventNote =
        "This event is per player: you choose for yourself and nobody waits. Leave when it is finished; the party " +
        "moves when every player has voted on the map.";

    public const string TreasureNote =
        "Co-op chest: every player has to open their own chest and vote for a relic before anyone gets one or can " +
        "leave. Leave only once the relics have been handed out on your screen; proceed is offered from then.";

    public const string CombatNote =
        "Shared turn: the enemies act once every living player has ended it. Every enemy attack hits every living " +
        "player in full: incoming_damage is what the shown intents deal to you before Block, team[].incoming what " +
        "they deal to that teammate. A teammate is the target of a card or potion by their id (team[].id). End your " +
        "turn when you have no useful play, without waiting for the others; the bot cannot take it back.";

    /// <summary>Shown instead of any action once our map vote is out (0.2 item 11, list E item 15).</summary>
    public const string RoomClosed =
        "your map vote is in: nothing in this room may be touched until the party has moved";

    /// <summary>Shown instead of any action while a fight is being set up (0.2 item 12, list E item 5).</summary>
    public const string FightStarting =
        "a fight is starting: nothing may be sent until it has begun";

    /// <summary>What proceeding leaves behind; null when nothing is left. The choice stays the bot's own.</summary>
    public static string? UnclaimedWarning(int unclaimed) => unclaimed switch
    {
        <= 0 => null,
        1 => "1 reward is still unclaimed and is lost once the party moves",
        _ => $"{unclaimed} rewards are still unclaimed and are lost once the party moves",
    };

    /// <summary>"1 of 2 players have voted to leave the act (still to come: Mark)".</summary>
    public static string Count(int done, int players, string what, IReadOnlyList<string>? missing = null)
    {
        var line = $"{done} of {players} players {what}";
        return missing is { Count: > 0 } ? $"{line} (still to come: {string.Join(", ", missing)})" : line;
    }

    // ---------- list B: where a client that does nothing blocks the whole group ----------

    /// <summary>One place the group waits on the bot, and the action a snapshot offers to end the wait.</summary>
    public sealed record Gate(string Where, string Blocks, string Unblock);

    /// <summary>
    /// votes.md 8.1, item by item. Every entry names the action id that unblocks it, and the
    /// reader of that screen offers it; the ones without an action are not decisions.
    /// </summary>
    public static readonly IReadOnlyList<Gate> Gates =
    [
        new("map", "1. The map vote, after every room and at each act's start: no vote, no travel for anyone.",
            "map:<n>, offered at the latest once the wait for the human has run out"),
        new("combat", "2. The end of the turn: every living player must end it.", "end_turn"),
        new("hand_select, card_select", "3. A choice that belongs to the bot in a fight, also one that opens after its end_turn (retain).",
            "select:<i> and confirm; card:<n> and confirm"),
        new("treasure", "4. Opening its own chest and voting for a relic: without it nobody gets one and nobody can leave.",
            "open, then relic:<n>"),
        new("event", "5. A shared event's vote on every page, one-option pages included.", "option:<n>"),
        new("event", "6. A shared option the game refuses because it would kill the bot: the click casts no vote.",
            "never offered; another option:<n> is"),
        new("rewards, event", "7. The act-change vote on the boss rewards, and The Architect's last option.",
            "proceed; option:<n> (ends the run)"),
        new("rewards, rest, shop, treasure, event", "8. Finishing its own room so that it can vote on the map.",
            "proceed, or the event's Leave option:<n>"),
        new("modal", "9. A tutorial popup in front of any of these.", "dismiss"),
        new("(none)", "10. The bot copy's main thread must keep running: the enemy-turn handover and room sync are automatic.",
            "no action; every main-thread job stays short"),
        new("(none)", "11. A bot copy that crashed or left: nothing skips a missing player.", "no action; re-host the saved run"),
        new("map", "12. Local map drawing mode left on: map clicks are ignored.", "stop_drawing"),
        new("map", "13. A map vote the host dropped: the screen shows it, the host never recorded it.",
            "cleared by the reader after a few seconds, then map:<n> again"),
        new("treasure", "14. An empty chest: no relic and no vote, but travel stays off until it is opened.", "open"),
        new("map, treasure, event", "15. Waiting for each other: the bot waits for the human's choice while the human waits for the bot.",
            "the wait ends after SPIREMIND_COOP_WAIT seconds, then the bot chooses and says which"),
        new("combat", "16. An end-turn undo for a dead or sitting-out bot would take it out of the ready set.",
            "never sent; the bot has no undo"),
    ];
}
