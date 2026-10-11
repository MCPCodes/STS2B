using System.Collections.Concurrent;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using SpireMind.Patches;
// An alias rather than the namespace, so a game type of the same name in the global usings can
// never make it ambiguous.
using InstanceRole = SpireMind.Core.Brain.InstanceRole;

namespace SpireMind.Game.Coop;

/// <summary>
/// Another player's chat lines on this copy's screen: in co-op the human sees what a bot said
/// as a speech bubble over that bot's figure, and a thought cloud over it while it decides.
/// The words reach this copy over the mod's own HTTP bridge; nothing here goes over the game's
/// network, so it cannot desync, and it works the same on the host and on a client.
///
/// One <see cref="SpeechBubbles.Voice"/> per speaker, so two bots can talk at once, each run
/// through the local bubbles' own queue, timing and room-change rules. Only where a bubble
/// goes is decided here, by what this screen shows of that player:
/// <list type="bullet">
/// <item>its figure: in a fight (also under the rewards and card picks that follow one), at
/// a rest site and in a shop, through the spots the local player's bubble uses;</item>
/// <item>otherwise its row in the player list on the left (map, events, treasure, an open
/// shop inventory), or in the lobby its row there;</item>
/// <item>failing that the local bubble's own fallbacks: beside the top-bar portrait, then
/// the fixed spot on the left of the screen.</item>
/// </list>
/// A bubble that is not over the figure starts with the speaker's name, and away from both
/// figure and row a line nobody can put a name to is not drawn at all: beside the local
/// player's portrait it would read as that player's.
///
/// Only in a local co-op lobby or run (<see cref="CoopNames.SeatOf"/>, read from the game for
/// every line). The relay calls <see cref="Show(ulong, string)"/> for every line of every bot
/// copy it finds on this Mac, whatever this copy is doing, so outside such a session a line
/// is logged and dropped: nothing is ever drawn in a solo game, in Steam co-op or on the
/// menus. A line under this copy's own NetId is dropped too in the human's copy (a bot copy
/// playing solo always reports NetId 1, the host's); in a bot's copy it goes to the local
/// voice, which already speaks for that player. Which copy this is comes from ModEntry's
/// InstanceRole through <see cref="Start"/>; until told, it counts as the human's.
///
/// <see cref="Show(ulong, string)"/> and <see cref="Thinking"/> may be called from any
/// thread; the work happens on the main thread, in <see cref="Tick"/>, which SpeechBubbles
/// calls every frame. Nothing runs until someone calls them. Every step logs with a "coop:"
/// prefix; a failure costs the bubble (the chat log still has the line), and one that escapes
/// the guards switches these bubbles off for the session. The offsets beside a row are
/// guesses from the scenes' sizes, to tune from a screenshot.
/// </summary>
internal static class RemoteBubbles
{
    /// <summary>Calls waiting for the frame tick. Capped, in case the tick isn't running.</summary>
    private const int MaxWaiting = 64;

    /// <summary>
    /// From the right edge of a row's name and counters to the bubble's position: clear of the
    /// row, with the bubble's middle level with the row's. Tune in game.
    /// </summary>
    private static readonly Vector2 RowOffset = new(100, 45);

    /// <summary>The same from the right edge of a lobby row. Tune in game.</summary>
    private static readonly Vector2 LobbyRowOffset = new(100, 45);

    /// <summary>
    /// How far a bubble's art reaches from its position when it opens to the right, a little
    /// rounded up: a bubble beside a row near the screen's edge is moved in by this much.
    /// </summary>
    private const float ReachLeft = 100, ReachRight = 225, ReachUp = 160, ReachDown = 70;

    /// <summary>Between two speakers' bubbles at the fixed screen spot, so they don't cover each other.</summary>
    private static readonly Vector2 Stagger = new(0, 220);

    /// <summary>Room kept in each chunk for "Name: " while the speaker's name isn't known yet: the longest of the bots' names.</summary>
    private const int UnknownNameLength = 11;

    /// <summary>How often speakers with nothing to say are let go once this copy has left co-op.</summary>
    private static readonly TimeSpan PruneEvery = TimeSpan.FromSeconds(2);

    /// <summary>One remote speaker: its voice, and what the callers said about it.</summary>
    private sealed class Speaker
    {
        public readonly ulong NetId;
        public readonly SpeechBubbles.Voice Voice;

        /// <summary>What the caller called it, for a speaker this copy's session doesn't know.</summary>
        public string? GivenName;

        /// <summary>The last name this speaker went by, for the words it still has queued when the session ends.</summary>
        public string? LastName;

        /// <summary>When this copy was told the speaker is deciding; null when it isn't.</summary>
        public DateTime? ThinkingSince;

        public Speaker(ulong netId)
        {
            NetId = netId;
            Voice = new SpeechBubbles.Voice($"coop: bubble for player {netId}", (text, secs) => Place(this, text, secs), () => PlayerOf(netId));
        }
    }

    private static readonly ConcurrentQueue<Action> Waiting = new();
    private static readonly Dictionary<ulong, Speaker> Speakers = new();
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);
    private static volatile bool _off;

    /// <summary>True in a bot's copy, as <see cref="Start"/> was told. Main thread.</summary>
    private static bool _botCopy;
    private static DateTime _nextPrune;

    /// <summary>
    /// Says which copy this is, once at mod load on the main thread, with the role ModEntry
    /// detected (the one place it is worked out) and the name the chat box calls the human.
    /// It only records the two: the names and the bubbles still appear only in a local co-op
    /// lobby or run. A copy that never calls it counts as the human's.
    /// </summary>
    public static void Start(InstanceRole role, string? humanName = null)
    {
        try
        {
            _botCopy = role?.IsBot == true;
            CoopNames.Start(_botCopy, humanName);
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: remote bubbles: couldn't take this copy's role, so it counts as the human's: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows a player's line on this screen as one or more speech bubbles, over that player's
    /// figure where it has one. Queued behind that player's earlier lines. Any thread.
    /// </summary>
    public static void Show(ulong netId, string text) => Show(netId, text, null);

    /// <inheritdoc cref="Show(ulong, string)"/>
    /// <param name="speaker">What to call the speaker when this copy's session doesn't know the
    /// player (a bot that hasn't joined yet); a player in the session goes by its name there.</param>
    public static void Show(ulong netId, string text, string? speaker)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Enqueue(() => Say(netId, text, speaker));
    }

    /// <summary>
    /// A thought cloud over a player's figure while it decides, as the local bot has: only in
    /// a fight, once it has been on for a moment, and never while one of its lines is up.
    /// Call it with true when the bot is handed a decision (again is fine) and with false
    /// when it has acted, is paused or is gone. Any thread.
    /// </summary>
    public static void Thinking(ulong netId, bool on) => Enqueue(() =>
    {
        if (!on && !Speakers.ContainsKey(netId)) return;
        // Only for another player of this copy's session: the local player's cloud follows
        // this copy's own chat hub, and anyone else has no figure here.
        if (on && CoopNames.SeatOf(netId) is not (CoopNames.Seating.Other or CoopNames.Seating.Unknown)) return;
        var s = SpeakerFor(netId);
        if (on == s.ThinkingSince.HasValue) return;
        s.ThinkingSince = on ? DateTime.UtcNow : null;
        ModEntry.Log($"coop: player {netId} {(on ? "is thinking" : "stopped thinking")}");
    });

    /// <summary>
    /// Every frame on the main thread, from SpeechBubbles.Tick with what it worked out about
    /// this frame: whether the room or the run changed and whether the screen is fading.
    /// Keeps the co-op names current, takes in what was asked for since the last frame and
    /// advances every speaker's bubbles and cloud.
    /// </summary>
    internal static void Tick(DateTime now, bool moved, bool runChanged, bool fading)
    {
        CoopNames.Tick(now); // guards itself

        if (_off || (Speakers.Count == 0 && Waiting.IsEmpty)) return;
        try
        {
            while (Waiting.TryDequeue(out var work)) work();
            foreach (var s in Speakers.Values)
            {
                // A thinking mark belongs to the run it was made in; whoever sets it says so again.
                if (runChanged) s.ThinkingSince = null;
                var cloudWasUp = s.Voice.Cloud is not null;
                SpeechBubbles.Step(s.Voice, now, moved, runChanged, fading);
                SpeechBubbles.UpdateCloud(s.Voice, ThinkingHere(s, now), paused: false, now);
                if (cloudWasUp != (s.Voice.Cloud is not null))
                    ModEntry.Log($"coop: thought cloud over player {s.NetId} {(cloudWasUp ? "gone" : "shown")}");
            }

            // Speakers with nothing left to say go with their run, and with the session when
            // it ended without one (a lobby backed out of), so the game played after a co-op
            // session is back to doing nothing here.
            var left = false;
            if (!runChanged && now >= _nextPrune)
            {
                _nextPrune = now + PruneEvery;
                left = CoopNames.NoSession();
            }
            if (runChanged || left)
                foreach (var gone in Speakers.Values.Where(s => !s.Voice.Speaking && s.Voice.Cloud is null).ToList())
                    Speakers.Remove(gone.NetId);
        }
        catch (Exception ex)
        {
            SwitchOff(ex);
        }
    }

    // ---------- speakers ----------

    private static void Enqueue(Action work)
    {
        if (_off) return;
        Waiting.Enqueue(work);
        while (Waiting.Count > MaxWaiting && Waiting.TryDequeue(out _)) { }
    }

    private static void Say(ulong netId, string text, string? given)
    {
        // The caller is the relay, which passes on every line of every bot copy it finds on
        // this Mac, with the NetId that copy reported. Whether that means anything on this
        // screen is decided here, from this copy's own game.
        var named = !string.IsNullOrWhiteSpace(given) || (Speakers.TryGetValue(netId, out var had) && had.LastName is not null);
        var seat = CoopNames.SeatOf(netId);
        // While the session can't be read (a run loading or tearing down), the run's own word
        // on who the local player is still stands.
        if (seat == CoopNames.Seating.Unknown && LocalContext.NetId == netId) seat = CoopNames.Seating.Local;
        switch (seat)
        {
            case CoopNames.Seating.NoSession:
                Skip(netId, "this copy is in no local co-op lobby or run");
                return;
            case CoopNames.Seating.Local when _botCopy:
                // This copy's own player has a voice already; a second one would talk over it.
                SpeechBubbles.Say(text);
                return;
            case CoopNames.Seating.Local:
                Skip(netId, "that NetId is this copy's own player, the human; the bot copy that reported it is not in this session");
                return;
            case CoopNames.Seating.Absent when !named:
                Skip(netId, "this session has no player with that NetId, and the caller gave no name to show the line under");
                return;
        }

        var s = SpeakerFor(netId);
        if (!string.IsNullOrWhiteSpace(given)) s.GivenName = given.Trim();
        var name = NameOf(s);
        ModEntry.Log($"coop: bubble for player {netId} ({(name.Length > 0 ? name : "no name known yet")}): a line of {text.Length} characters queued");
        // Room for "Name: " in front of each chunk, whether or not this one ends up needing it.
        SpeechBubbles.Say(s.Voice, text, reserve: (name.Length > 0 ? name.Length : UnknownNameLength) + 2);
    }

    /// <summary>A line that is not drawn, said once in the log for each speaker and reason. The chat log has the line.</summary>
    private static void Skip(ulong netId, string why)
    {
        if (Logged.Add($"skip {netId} {why}"))
            ModEntry.Log($"coop: bubble for player {netId} not shown, nor later ones like it: {why}");
    }

    private static Speaker SpeakerFor(ulong netId)
    {
        if (!Speakers.TryGetValue(netId, out var s))
            Speakers[netId] = s = new Speaker(netId);
        return s;
    }

    /// <summary>
    /// The run's player with that NetId while this copy is in a run, else null. Looked up
    /// afresh every time: players and their nodes go with the run.
    /// </summary>
    private static Player? PlayerOf(ulong netId)
    {
        try
        {
            var ctx = GameContext.Capture();
            return ctx.InRun && NGame.Instance?.MainMenu is null ? ctx.Run?.GetPlayer(netId) : null;
        }
        catch (Exception ex)
        {
            LogOnce("finding the player", ex);
            return null;
        }
    }

    /// <summary>
    /// What the speaker is called on this screen: the co-op session's name for it (the one on
    /// its nameplate), else what the caller called it, else its character's name, else the
    /// last of those it went by; "" when nothing was ever known.
    /// </summary>
    private static string NameOf(Speaker s)
    {
        var name = CoopNames.NameOf(s.NetId) ?? s.GivenName;
        if (name is null)
        {
            try { name = CoopNames.CharacterName(PlayerOf(s.NetId)?.Character); }
            catch { name = ""; }
        }
        if (name.Length > 0) s.LastName = name;
        // The session's names go when it ends; what the speaker still has queued keeps its name.
        return s.LastName ?? "";
    }

    /// <summary>
    /// The thinking mark as this screen counts it. The mark was made on another copy and told
    /// to this one a moment later, so it is not held against the time this screen's room
    /// changed as the local one is: a bot still deciding when the room changes gets its cloud
    /// again in the new one. One on for longer than the local limit was left behind.
    /// </summary>
    private static DateTime? ThinkingHere(Speaker s, DateTime now)
    {
        if (s.ThinkingSince is not { } told) return null;
        var since = told > SpeechBubbles.PlaceSince ? told : SpeechBubbles.PlaceSince;
        return now - since <= SpeechBubbles.ThinkAbandoned ? since : null;
    }

    // ---------- where ----------

    /// <summary>
    /// Creates and parents the bubble for one chunk of a remote speaker's line: the local
    /// bubble's ladder of spots with the speaker in place of the local player, a row spot
    /// before the portrait, and the speaker's name in front wherever it isn't over the figure.
    /// </summary>
    private static SpeechBubbles.Placed Place(Speaker s, string text, double secs)
    {
        var ctx = GameContext.Capture();
        var inRun = ctx.InRun && NGame.Instance?.MainMenu is null;
        if (inRun && CoopNames.NoSession())
        {
            // The session these words were said in is over and this copy is in another run:
            // solo, or co-op over Steam. Nothing of a bot's is drawn there.
            if (s.Voice.Pending.Count > 0)
                ModEntry.Log($"coop: bubble for player {s.NetId}: {s.Voice.Pending.Count} more chunks dropped with this one, this copy is now in a run that is not local co-op");
            s.Voice.Pending.Clear();
            return new(null, "no (left behind by its session)");
        }

        // The name in front, for a bubble that isn't over its figure. Both are BBCode-escaped.
        var name = NameOf(s);
        var named = name.Length > 0 ? $"{SpeechBubbles.Escape(name)}: {text}" : null;
        if (!inRun)
        {
            // Menus and the lobby have no figures. In the lobby every player has a row, and
            // the character it has picked so far gives the colour.
            var picked = ColorOf(CoopNames.CharacterOf(s.NetId));
            if (SpeechBubbles.Place(LobbyRowSpot(s.NetId), named ?? text, secs, picked) is { } byLobbyRow)
                return new(byLobbyRow, "lobby row");
            if (named is null) return Nameless(s);
            return new(SpeechBubbles.Place(ScreenSpot(s), named, secs, picked) ?? SpeechBubbles.NoSpot(), "screen");
        }

        // Null for a speaker who is not in this run: a bot still at its menu, or one from an earlier session.
        var who = PlayerOf(s.NetId);
        var color = ColorOf(who?.Character ?? CoopNames.CharacterOf(s.NetId));
        if (who is not null)
        {
            // A dead player's figure stays in the fight, and the game's own "hurry" bubble
            // shows over it too, hence evenDead.
            NSpeechBubbleVfx? bubble = null;
            var spot = "";
            if (!SpeechBubbles.RoomCovered())
            {
                if (ctx.InCombat)
                    (bubble, spot) = (SpeechBubbles.OverCreature(who, text, secs, color, evenDead: true), "creature");
                else if (ctx.Run?.CurrentRoom?.RoomType == RoomType.RestSite)
                    (bubble, spot) = (SpeechBubbles.Place(SpeechBubbles.RestSiteSpot(who), text, secs, color), "rest site");
                else if (ctx.Run?.CurrentRoom?.RoomType == RoomType.Shop)
                    (bubble, spot) = (SpeechBubbles.Place(SpeechBubbles.ShopSpot(ShopFigure(ctx.Run, who)), text, secs, color), "shop");
            }
            if (bubble is not null) return new(bubble, spot, InRoom: true);

            if (SpeechBubbles.Place(SpeechBubbles.AboveOverlaySpot(who, evenDead: true), text, secs, color) is { } above)
                return new(above, "over the character, above the overlay", OverFigure: true);
        }

        // No figure of this player on the screen. Its row in the player list stands for it;
        // the list slides away with the top bar under the pause menu, the settings and a
        // player's expanded view, and then only the fixed spot, which hangs from NGame, shows.
        if (SpeechBubbles.Place(RowSpot(s.NetId, out var listHidden), named ?? text, secs, color) is { } byRow)
            return new(byRow, "player row");
        // From here on the bubble stands where the local player's own would, so only its
        // name says whose it is.
        if (named is null) return Nameless(s);
        if (!listHidden && SpeechBubbles.Place(SpeechBubbles.PortraitSpot(), named, secs, color) is { } byPortrait)
            return new(byPortrait, "portrait");
        return new(SpeechBubbles.Place(ScreenSpot(s), named, secs, color) ?? SpeechBubbles.NoSpot(), "screen");
    }

    /// <summary>No bubble: neither figure nor row is on this screen, and without a name the line would read as the local player's.</summary>
    private static SpeechBubbles.Placed Nameless(Speaker s)
    {
        if (Logged.Add($"nameless {s.NetId}"))
            ModEntry.Log($"coop: bubble for player {s.NetId} not shown, nor later ones like it: neither its figure nor its row is on this screen and no name is known for it");
        return new(null, "no (nameless away from its figure)");
    }

    private static VfxColor ColorOf(CharacterModel? character)
    {
        try
        {
            if (character is not null) return SpeechBubbles.ColorFor(character);
        }
        catch (Exception ex)
        {
            LogOnce("bubble colour", ex);
        }
        return VfxColor.White;
    }

    /// <summary>
    /// Which of the shop's figures is this player's. The room lines them up with the local
    /// player first and the others after it in the run's order, and the figures themselves
    /// don't say whose they are. -1 when the player isn't in the run.
    /// </summary>
    private static int ShopFigure(RunState run, Player who)
    {
        if (LocalContext.IsMe(who)) return 0;
        var figure = 1;
        foreach (var player in run.Players)
        {
            if (ReferenceEquals(player, who)) return figure;
            if (!LocalContext.IsMe(player)) figure++;
        }
        return -1;
    }

    /// <summary>
    /// Beside the player's row in the run's player list, on the run's VFX layer above the top
    /// bar. Null when the row can't be found; <paramref name="listHidden"/> when the list is
    /// off screen and the top bar with it.
    /// </summary>
    private static SpeechBubbles.Spot? RowSpot(ulong netId, out bool listHidden)
    {
        listHidden = false;
        try
        {
            var ui = NRun.Instance?.GlobalUi;
            if (ui?.MultiplayerPlayerContainer is not { } list || !Nodes.Alive(list)) return null;
            if (!list.IsVisibleInTree() || list.Position.X < 0 || list.Modulate.A < 0.5f)
            {
                listHidden = true;
                return null;
            }
            if (ui.AboveTopBarVfxContainer is not Node layer || !Nodes.Alive(layer)) return null;
            if (CoopNames.RunRows().FirstOrDefault(r => r.Player.NetId == netId) is not { } row) return null;

            // A row is as wide as the whole list column; what is drawn in it ends with the
            // name and the counters.
            var whole = row.GetGlobalRect();
            var right = Nodes.Get<Control>(row, "TopInfoContainer") is { } drawn && Nodes.Alive(drawn) ? drawn.GetGlobalRect().End.X : whole.End.X;
            return new SpeechBubbles.Spot(layer, OnScreen(layer, new Vector2(right, whole.GetCenter().Y) + RowOffset));
        }
        catch (Exception ex)
        {
            LogOnce("player row spot", ex);
            return null;
        }
    }

    /// <summary>Beside the player's row in the lobby, parented to NGame like the fixed spot. Null when it has no row on this screen.</summary>
    private static SpeechBubbles.Spot? LobbyRowSpot(ulong netId)
    {
        try
        {
            var game = NGame.Instance;
            if (!Nodes.Alive(game)) return null;
            if (CoopNames.LobbyRows().FirstOrDefault(r => r.PlayerId == netId) is not { } row || !row.IsVisibleInTree()) return null;
            var rect = row.GetGlobalRect();
            return new SpeechBubbles.Spot(game, OnScreen(game, new Vector2(rect.End.X, rect.GetCenter().Y) + LobbyRowOffset));
        }
        catch (Exception ex)
        {
            LogOnce("lobby row spot", ex);
            return null;
        }
    }

    /// <summary>The local bubble's fixed screen spot, a step lower for each speaker with a lower NetId.</summary>
    private static SpeechBubbles.Spot? ScreenSpot(Speaker s) =>
        SpeechBubbles.ScreenSpot() is { } spot ? spot with { At = spot.At + Stagger * Speakers.Keys.Count(id => id < s.NetId) } : null;

    /// <summary>A position moved in from the screen's edges far enough for the whole bubble to show.</summary>
    private static Vector2 OnScreen(Node inTree, Vector2 at)
    {
        var size = inTree.GetViewport().GetVisibleRect().Size;
        return new Vector2(
            Math.Clamp(at.X, ReachLeft, Math.Max(ReachLeft, size.X - ReachRight)),
            Math.Clamp(at.Y, ReachUp, Math.Max(ReachUp, size.Y - ReachDown)));
    }

    // ---------- plumbing ----------

    private static void SwitchOff(Exception ex)
    {
        _off = true;
        ModEntry.LogError($"coop: remote bubbles switched off for this session; the chat log still has the lines: {ex}");
        foreach (var s in Speakers.Values)
        {
            try { SpeechBubbles.Clear(s.Voice); }
            catch { /* whatever is still up frees itself when its time is up */ }
        }
        Speakers.Clear();
        Waiting.Clear();
    }

    private static void LogOnce(string what, Exception ex)
    {
        if (Logged.Add(what)) ModEntry.LogError($"coop: remote bubbles: {what}: {ex.Message}");
    }
}
