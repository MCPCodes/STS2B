// NSpeechBubbleVfx, NThoughtBubbleVfx and VfxColor come from MegaCrit.Sts2.Core.Nodes.Vfx, and
// DialogueSide from MegaCrit.Sts2.Core.Nodes.Vfx.Utilities; both are in the global usings.

using SpireMind.Game.Coop;

namespace SpireMind.Game;

/// <summary>
/// The bot's chat lines as the game's own speech bubbles over its character, and a thought
/// cloud while it decides in combat (docs/chat-and-persona.md, "Speech and thought bubbles").
/// The game draws one NSpeechBubbleVfx per call and queues nothing, so a long line is cut
/// into bubble-sized chunks here and shown one after another, one bubble at a time.
///
/// Everything runs on the main thread: <see cref="Say(string)"/> from the chat overlay's tick, and
/// <see cref="Tick"/> from ModEntry's SceneTree.ProcessFrame handler. Every game call is
/// guarded: a member that turns out wrong costs the bubble (the chat log still has the line)
/// and one log line, never the bridge.
///
/// Co-op: what one speaker has queued and on screen is a <see cref="Voice"/>. The local
/// player's is <see cref="Local"/>, and everything it did before it still does. The other
/// players' voices belong to <see cref="RemoteBubbles"/>, which runs them through the same
/// queue, timing and room-change rules here and reuses the spots below with that player in
/// place of the local one.
/// </summary>
internal static class SpeechBubbles
{
    /// <summary>The bubble's text box is a fixed 280x130 px with 24 to 18 pt text: about 100 characters fit.</summary>
    private const int ChunkLength = 100;

    /// <summary>A chunk may run this much longer to end on a sentence; the text shrinks to fit.</summary>
    private const int SentenceSlack = 10;

    /// <summary>Chunks waiting beyond this are dropped, oldest first, so the bubbles don't trail the chat log by a minute.</summary>
    private const int MaxQueued = 6;

    /// <summary>Reading time per non-space character, clamped, as the game's TalkCmd works it out.</summary>
    private const double SecondsPerLetter = 0.12;
    private const double MinSeconds = 1.5;
    private const double MaxSeconds = 6;

    /// <summary>A bubble fades in over <see cref="FadeIn"/>, holds for max(secs - 1, 1), then fades out over <see cref="FadeOut"/> and frees itself.</summary>
    private const double FadeIn = 0.5;
    private const double FadeOut = 0.4;

    /// <summary>Slack after a bubble should have freed itself before we stop counting it as up.</summary>
    private const double Margin = 0.5;

    /// <summary>A bubble cut short with less than this left to go isn't shown again.</summary>
    private const double MinCarry = 0.6;

    /// <summary>A bubble cut short this soon was still fading in, so it gets its whole time again.</summary>
    private const double BarelySeen = 1.0;

    /// <summary>A fade lasting longer than this got stuck (the game failed mid-transition); speech stops waiting for it.</summary>
    private static readonly TimeSpan FadeStuck = TimeSpan.FromSeconds(20);

    /// <summary>How long the bot has been deciding before the thought cloud shows, so quick moves don't flash it.</summary>
    private static readonly TimeSpan ThinkDelay = TimeSpan.FromSeconds(2.5);

    /// <summary>A decision held longer than this was left behind (the session ended, or the human took over by hand), so it shows no cloud.</summary>
    internal static readonly TimeSpan ThinkAbandoned = TimeSpan.FromSeconds(120);

    /// <summary>After a thought cloud failed to appear, wait this long before trying again.</summary>
    private static readonly TimeSpan CloudRetry = TimeSpan.FromSeconds(3);

    /// <summary>The fixed spot outside a run, as a fraction of the screen: the left third, a little above the middle. Tune in game.</summary>
    private static readonly Vector2 ScreenFraction = new(0.2f, 0.42f);

    /// <summary>From the shop's player figure to above its head. Tune in game.</summary>
    private static readonly Vector2 ShopOffset = new(60, -250);

    /// <summary>From the top-bar portrait's corner to beside it; the portrait's height is added to Y.</summary>
    private static readonly Vector2 PortraitOffset = new(110, 235);

    /// <summary>
    /// One speaker's bubbles: the chunks waiting, the bubble on screen with what a room change
    /// would carry over, and the thought cloud. Only this class reads and writes the state; an
    /// owner makes one and hands it to <see cref="Say(Voice, string, int)"/>,
    /// <see cref="Step"/> and <see cref="UpdateCloud"/>.
    /// </summary>
    /// <param name="label">What this voice's trace lines start with, so the log says whose bubble it was.</param>
    /// <param name="show">Creates and parents the bubble for an escaped chunk, wherever this speaker can be seen right now.</param>
    /// <param name="speaker">The speaker's player while there is one, for the thought cloud.</param>
    internal sealed class Voice(string label, Func<string, double, Placed> show, Func<Player?> speaker)
    {
        internal readonly string Label = label;
        internal readonly Func<string, double, Placed> Show = show;
        internal readonly Func<Player?> Speaker = speaker;

        /// <summary>Chunks of the speaker's lines waiting their turn, escaped only when shown.</summary>
        internal readonly Queue<string> Pending = new();

        internal NSpeechBubbleVfx? Bubble;

        /// <summary>When the bubble on screen was shown, to tell in the log how long one lasted.</summary>
        internal DateTime BubbleShown;

        /// <summary>The chunk on screen as it was queued, and how long it was given: what a room change carries over.</summary>
        internal string? BubbleChunk;
        internal double BubbleSecs;

        /// <summary>Which spot the bubble on screen took, for the log.</summary>
        internal string BubbleSpot = "";

        /// <summary>
        /// True when the bubble on screen sits in the room (over the creature, at the rest site or
        /// in the shop) and so goes with it, or under whatever covers it. One by the top bar
        /// outlives rooms.
        /// </summary>
        internal bool BubbleInRoom;

        /// <summary>
        /// True when the bubble on screen points at the character's figure from the layer above
        /// the overlays. It outlives overlays, but not the map, a capstone or the fight's room,
        /// which take the figure away.
        /// </summary>
        internal bool BubbleOverFigure;

        /// <summary>A chunk cut short when its room went away or was covered, to show again where it can be seen.</summary>
        internal (string Chunk, double Secs)? Carried;

        /// <summary>When the next chunk may replace <see cref="Bubble"/>: the moment it would start fading out by itself.</summary>
        internal DateTime BubbleDone;

        /// <summary>When <see cref="Bubble"/> has freed itself, give or take <see cref="Margin"/>.</summary>
        internal DateTime BubbleGone;

        internal NThoughtBubbleVfx? Cloud;
        internal Creature? CloudOver;

        /// <summary>The thinking mark the cloud was shown for; a newer decision or none ends it.</summary>
        internal DateTime? CloudFor;

        internal DateTime CloudRetryAt;

        /// <summary>A bubble is up, or a chunk is waiting for its turn.</summary>
        public bool Speaking => Bubble is not null || Carried is not null || Pending.Count > 0;
    }

    /// <summary>
    /// A bubble as a voice's <see cref="Voice.Show"/> placed it: the node (null when nowhere
    /// would take it), the spot's name for the log, and which of the room-change rules it
    /// falls under (<see cref="Voice.BubbleInRoom"/>, <see cref="Voice.BubbleOverFigure"/>).
    /// </summary>
    internal readonly record struct Placed(NSpeechBubbleVfx? Bubble, string Spot, bool InRoom = false, bool OverFigure = false);

    /// <summary>The local player's voice: this copy's bot, speaking as the character it plays.</summary>
    private static readonly Voice Local = new("speech bubble", ShowLocal, () => GameContext.Capture().Me);

    /// <summary>When the screen began fading, to stop holding speech for a fade that never ends.</summary>
    private static DateTime? _fadingSince;

    /// <summary>The run and room the bubbles on screen belong to; (null, null) outside a run.</summary>
    private static (object? Run, object? Room) _place;

    /// <summary>When <see cref="_place"/> last changed. A decision handed over before it belongs to an earlier room.</summary>
    private static DateTime _placeSince;

    /// <summary>Failures already logged, so a wrong guess about a game member logs once instead of every frame.</summary>
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);

    /// <summary>When the room on this screen last changed, for a co-op voice whose thinking mark comes from another copy.</summary>
    internal static DateTime PlaceSince => _placeSince;

    /// <summary>Queues the bot's line as one or more bubbles; the next ticks show them. Main thread.</summary>
    public static void Say(string text) => Say(Local, text);

    /// <summary>
    /// Queues a line for one voice. <paramref name="reserve"/> shortens the chunks by that many
    /// characters, for an owner that puts the speaker's name in front of each one.
    /// </summary>
    internal static void Say(Voice v, string text, int reserve = 0)
    {
        foreach (var chunk in Split(text, ChunkLength - reserve))
            v.Pending.Enqueue(chunk);
        while (v.Pending.Count > MaxQueued)
            v.Pending.Dequeue();
    }

    /// <summary>
    /// Every frame on the main thread: clears everything when the run changes, carries a line
    /// over when only the room does, shows the next chunk once the one before has been up long
    /// enough, and shows or ends the thought cloud. Then the same for the other players' voices
    /// in co-op, which do nothing while nobody else has spoken.
    /// </summary>
    public static void Tick(ChatHub hub)
    {
        var now = DateTime.UtcNow;
        bool moved = false, runChanged = false, fading = false;
        try
        {
            var place = CurrentPlace();
            moved = place != _place;
            runChanged = moved && (place.Run is null || !ReferenceEquals(place.Run, _place.Run));

            // The new place is taken before anything else can fail, so a room change is told
            // once: to this frame's voices, the other players' below among them.
            if (moved)
            {
                _place = place;
                _placeSince = now;
            }

            // Travelling between rooms fades the run's whole screen to black, the top-bar layer
            // included, so a line spoken just before a move waits for the next room. Outside a
            // run the bubble hangs from NGame, which draws above the fade.
            fading = place.Run is not null && ScreenFading(now);

            // The bot often speaks and moves on in the same breath (a map node, an event's
            // Leave, the last word before the main menu), so its line carries over instead of
            // going with the room or the run. Chunks waiting are only seconds old. The
            // thought cloud is the room's.
            Step(Local, now, moved, runChanged, fading);

            // A decision from an earlier room, or one held past ThinkAbandoned, counts as none:
            // a session that ends mid-decision leaves the hub's mark up for minutes.
            var since = hub.ThinkingSince is { } t && t >= _placeSince && now - t <= ThinkAbandoned ? t : (DateTime?)null;
            UpdateCloud(Local, since, hub.Paused, now);
        }
        catch (Exception ex)
        {
            LogOnce("tick", ex);
        }

        // Co-op: the other players' lines on this screen. It guards itself, so a failure
        // there never reaches the local player's bubbles above.
        RemoteBubbles.Tick(now, moved, runChanged, fading);
    }

    /// <summary>
    /// One voice's frame. A place that changed takes the cloud with it and carries the line
    /// over (clearing what can't be carried when the run changed); a bubble something now
    /// covers is taken down to be shown again; then the next chunk goes up when its turn has
    /// come, unless the screen is fading.
    /// </summary>
    internal static void Step(Voice v, DateTime now, bool moved, bool runChanged, bool fading)
    {
        if (moved)
        {
            EndCloud(v);
            CarryOver(v, now, whereverItIs: runChanged);
            if (runChanged) DismissBubble(v); // one too far gone to carry still goes with its run
        }
        else if (v.Bubble is not null && (v.BubbleInRoom ? RoomCovered() : v.BubbleOverFigure && FigureHidden()))
        {
            // Rewards or the map opened over the fight the line was spoken in, or the map or
            // deck view opened over the figure it pointed at. Freed at once: the new bubble
            // may take the same spot.
            CarryOver(v, now, whereverItIs: true, atOnce: true);
        }

        if (fading && v.Bubble is not null) CarryOver(v, now, whereverItIs: true);

        AdvanceSpeech(v, now, fading);
    }

    /// <summary>Drops the queued chunks and dismisses the bubble and the thought cloud on screen. Main thread.</summary>
    /// <remarks>Not called on a room or run change any more; kept for a caller that wants silence at once.</remarks>
    public static void Clear() => Clear(Local);

    /// <inheritdoc cref="Clear()"/>
    internal static void Clear(Voice v)
    {
        if (v.Bubble is not null || v.Carried is not null || v.Pending.Count > 0)
            Trace(v, $"cleared: {(v.Bubble is null ? "no bubble up" : $"bubble up for {(DateTime.UtcNow - v.BubbleShown).TotalSeconds:0.0}s")}, {v.Pending.Count + (v.Carried is null ? 0 : 1)} chunks dropped");
        v.Pending.Clear();
        v.Carried = null;
        DismissBubble(v);
        EndCloud(v);
    }

    // ---------- speech ----------

    private static void AdvanceSpeech(Voice v, DateTime now, bool fading)
    {
        // A bubble frees itself when its time is up (and with its room). Past that, one still
        // around is faded out here, so it can't hang over the screen.
        if (v.Bubble is not null && !GodotObject.IsInstanceValid(v.Bubble))
            CarryOver(v, now); // freed with its room before this tick saw the room change
        if (v.Bubble is not null && (now >= v.BubbleGone || !GodotObject.IsInstanceValid(v.Bubble)))
            DismissBubble(v);

        if (fading || (v.Carried is null && v.Pending.Count == 0) || (v.Bubble is not null && now < v.BubbleDone)) return;

        DismissBubble(v);
        EndCloud(v);
        string chunk;
        double secs;
        if (v.Carried is { } carried)
        {
            (chunk, secs) = carried;
            v.Carried = null;
        }
        else
        {
            chunk = v.Pending.Dequeue();
            secs = Seconds(chunk);
        }
        var placed = v.Show(Escape(chunk), secs);
        v.Bubble = placed.Bubble;
        v.BubbleSpot = placed.Spot;
        v.BubbleInRoom = placed.InRoom;
        v.BubbleOverFigure = placed.OverFigure;
        v.BubbleChunk = chunk;
        v.BubbleSecs = secs;
        v.BubbleShown = now;
        v.BubbleDone = now + TimeSpan.FromSeconds(FadeIn + Math.Max(secs - 1, 1));
        v.BubbleGone = v.BubbleDone + TimeSpan.FromSeconds(FadeOut + Margin);
        Trace(v, $"shown for {secs:0.0}s, {v.BubbleSpot} spot, {Where(v.Bubble)}: \"{(chunk.Length > 40 ? chunk[..40] + "..." : chunk)}\"");
    }

    /// <summary>
    /// Takes down a bubble the player can no longer see and queues it to be shown again: one
    /// that sat in the room when the room went away or was covered, or any bubble
    /// (<paramref name="whereverItIs"/>) when the screen fades to black. Shown under a second
    /// it was still fading in and gets its whole time again; otherwise what was left of it. One
    /// that was about done is left to finish.
    /// </summary>
    private static void CarryOver(Voice v, DateTime now, bool whereverItIs = false, bool atOnce = false)
    {
        if (v.Bubble is null || v.BubbleChunk is not { } chunk || (!v.BubbleInRoom && !whereverItIs)) return;
        var seen = (now - v.BubbleShown).TotalSeconds;
        var left = (v.BubbleDone - now).TotalSeconds;
        if (left < MinCarry) return;
        Trace(v, $"carried over from the {v.BubbleSpot} spot after {seen:0.0}s with {left:0.0}s left");
        DismissBubble(v, atOnce);
        v.Carried = (chunk, seen < BarelySeen ? v.BubbleSecs : Math.Max(left + 1 - FadeIn, MinSeconds));
    }

    /// <summary>
    /// The game is fading the run's screen out or in, so nothing drawn under it would be seen.
    /// In Instant mode the game sets the flag but hides the fade, and then nothing is hidden.
    /// A fade that outlasts <see cref="FadeStuck"/> is taken for stuck and ignored.
    /// </summary>
    private static bool ScreenFading(DateTime now)
    {
        try
        {
            if (NGame.Instance?.Transition is not { InTransition: true, Visible: true })
            {
                _fadingSince = null;
                return false;
            }
            _fadingSince ??= now;
            if (now - _fadingSince <= FadeStuck) return true;
            if (Logged.Add("fade stuck"))
                ModEntry.LogError("speech bubbles: the screen has been fading for a long time; no longer waiting for it");
            return false;
        }
        catch (Exception ex)
        {
            LogOnce("transition check", ex);
            return false;
        }
    }

    /// <summary>The map, a capstone or the loss of the fight's room hides the figure an over-the-character bubble points at.</summary>
    private static bool FigureHidden()
    {
        try
        {
            return NMapScreen.Instance?.IsOpen == true || NCapstoneContainer.Instance?.InUse == true
                   || !Nodes.Alive(NCombatRoom.Instance);
        }
        catch (Exception ex)
        {
            LogOnce("figure check", ex);
            return false;
        }
    }

    /// <summary>Where a bubble ended up, for the log: its parent, position and whether it can be seen.</summary>
    private static string Where(NSpeechBubbleVfx? bubble)
    {
        try
        {
            if (bubble is null || !GodotObject.IsInstanceValid(bubble)) return "nowhere";
            var parent = bubble.GetParent();
            var view = bubble.IsInsideTree() ? bubble.GetViewport().GetVisibleRect().Size : Vector2.Zero;
            return $"at {bubble.GlobalPosition} of {view} under {(parent is null ? "no parent" : parent.GetPath().ToString())}"
                   + $" (in tree {bubble.IsInsideTree()}, visible {bubble.IsVisibleInTree()}, z {bubble.ZIndex})";
        }
        catch (Exception ex)
        {
            return $"unknown ({ex.Message})";
        }
    }

    /// <summary>One log line per bubble shown or cut short, while bubble placement is being settled in game.</summary>
    private static void Trace(Voice v, string what) => ModEntry.Log($"{v.Label} {DateTime.UtcNow:HH:mm:ss.fff}: {what}");

    /// <summary>
    /// Creates the bubble at the spot the docs give for where the local player is, and parents
    /// it. A spot the game won't give falls through to the top-bar portrait, then the fixed
    /// screen spot; no bubble when none works.
    /// </summary>
    private static Placed ShowLocal(string text, double secs)
    {
        var ctx = GameContext.Capture();
        var color = BubbleColor(ctx.Me);
        if (!ctx.InRun || NGame.Instance?.MainMenu is not null)
            return new Placed(Place(ScreenSpot(), text, secs, color) ?? NoSpot(), "screen");

        NSpeechBubbleVfx? bubble = null;
        var spot = "";
        if (!RoomCovered())
        {
            if (ctx.InCombat && ctx.Me is { } me)
                (bubble, spot) = (OverCreature(me, text, secs, color), "creature");
            else if (ctx.Run?.CurrentRoom?.RoomType == RoomType.RestSite)
                (bubble, spot) = (Place(RestSiteSpot(ctx.Me), text, secs, color), "rest site");
            else if (ctx.Run?.CurrentRoom?.RoomType == RoomType.Shop)
                (bubble, spot) = (Place(ShopSpot(), text, secs, color), "shop");
        }
        if (bubble is not null) return new Placed(bubble, spot, InRoom: true);

        // Rewards and card picks sit over the fight just won, and that is where the player is
        // looking: over the character still, but on the layer above the overlay.
        if (Place(AboveOverlaySpot(ctx.Me), text, secs, color) is { } above)
            return new Placed(above, "over the character, above the overlay", OverFigure: true);
        if (Place(PortraitSpot(), text, secs, color) is { } byPortrait)
            return new Placed(byPortrait, "portrait");
        return new Placed(Place(ScreenSpot(), text, secs, color) ?? NoSpot(), "screen");
    }

    /// <summary>
    /// Fight: the factory that finds the creature's node and sits the bubble above its head.
    /// <paramref name="evenDead"/> is for another player in co-op, whose figure stays in the
    /// fight when it dies (the game's own ping bubble shows over it too).
    /// </summary>
    internal static NSpeechBubbleVfx? OverCreature(Player me, string text, double secs, VfxColor color, bool evenDead = false)
    {
        if (CombatVfx(me, evenDead) is not { } layer) return null;
        NSpeechBubbleVfx? bubble;
        try { bubble = NSpeechBubbleVfx.Create(text, me.Creature, secs, color); }
        catch (Exception ex)
        {
            LogOnce("NSpeechBubbleVfx.Create over the creature", ex);
            return null;
        }
        return Adopt(layer, bubble);
    }

    /// <summary>Anywhere else: the factory that takes a screen position and the side the bubble opens from.</summary>
    internal static NSpeechBubbleVfx? Place(Spot? spot, string text, double secs, VfxColor color)
    {
        if (spot is not { } s) return null;
        NSpeechBubbleVfx? bubble;
        try { bubble = NSpeechBubbleVfx.Create(text, s.Side, s.At, secs, color); }
        catch (Exception ex)
        {
            LogOnce("NSpeechBubbleVfx.Create at a position", ex);
            return null;
        }
        return Adopt(s.Parent, bubble);
    }

    /// <summary>Fades out and frees the bubble on screen, if it is still there.</summary>
    private static void DismissBubble(Voice v, bool atOnce = false)
    {
        var bubble = v.Bubble;
        v.Bubble = null;
        if (bubble is null || !GodotObject.IsInstanceValid(bubble) || bubble.IsQueuedForDeletion()) return;
        try
        {
            // AnimOut makes a tween, which needs the tree. Out of it, the bubble's room is being
            // swapped out (and frees it at the end of the frame) or its deferred add hasn't happened.
            // At once: the next bubble may open on the same spot, and two would show for a moment.
            if (bubble.IsInsideTree() && !atOnce) Fading("AnimOut", bubble, bubble.AnimOut());
            else bubble.QueueFree();
        }
        catch (Exception ex)
        {
            LogOnce("AnimOut", ex);
            Free(bubble);
        }
    }

    internal static NSpeechBubbleVfx? NoSpot()
    {
        if (Logged.Add("no spot"))
            ModEntry.LogError("speech bubbles: nowhere to show a bubble here; the chat log still has the line");
        return null;
    }

    // ---------- where ----------

    /// <summary>
    /// Where a bubble goes: a parent node, a position in screen coordinates, and the side it
    /// opens from. Left, the player's side in a fight, opens up and to the right of the position.
    /// </summary>
    internal readonly record struct Spot(Node Parent, Vector2 At, DialogueSide Side = DialogueSide.Left);

    /// <summary>The run and room the local player is in, (null, null) outside a run. Bubbles outlive neither.</summary>
    private static (object? Run, object? Room) CurrentPlace()
    {
        try
        {
            // The main menu wins over RunManager, as in ScreenRouter: a run being set up at
            // character select already counts as in progress.
            var rm = RunManager.Instance;
            if (rm is null || !rm.IsInProgress || NGame.Instance?.MainMenu is not null) return (null, null);
            var run = rm.DebugOnlyGetState();
            return (run, run?.CurrentRoom);
        }
        catch (Exception ex)
        {
            // A run loading or tearing down; keep what we had rather than clear on a hiccup.
            LogOnce("reading the room", ex);
            return _place;
        }
    }

    /// <summary>The map or an overlay screen (rewards, card choices) hides the room, so a bubble in it wouldn't be seen.</summary>
    internal static bool RoomCovered()
    {
        try
        {
            return NMapScreen.Instance?.IsOpen == true || Nodes.TopOverlay() is not null
                   || NCapstoneContainer.Instance?.InUse == true; // the deck view or pause menu
        }
        catch (Exception ex)
        {
            LogOnce("overlay check", ex);
            return false;
        }
    }

    /// <summary>
    /// The combat room's VFX layer while the player's creature stands in it, else null. The
    /// local player's must be alive; <paramref name="evenDead"/> lifts that for another player.
    /// </summary>
    private static Node? CombatVfx(Player me, bool evenDead = false)
    {
        try
        {
            if (me.Creature is not { } creature || (!evenDead && !creature.IsAlive)) return null;
            var room = NCombatRoom.Instance;
            // Without a creature node the factories put the bubble at the screen's top left corner.
            if (!Nodes.Alive(room) || !Nodes.Alive(room.GetCreatureNode(creature))) return null;
            return room.CombatVfxContainer is Node layer && Nodes.Alive(layer) ? layer : null;
        }
        catch (Exception ex)
        {
            LogOnce("combat VFX container", ex);
            return null;
        }
    }

    /// <summary>Rest site: where and how the game puts the character's own thought bubble, parented to the room.</summary>
    internal static Spot? RestSiteSpot(Player? me)
    {
        try
        {
            var room = NRestSiteRoom.Instance;
            if (!Nodes.Alive(room) || me is null) return null;
            if (room.GetCharacterForPlayer(me) is not { } character || !Nodes.Alive(character)) return null;
            // Seats are in the run's player order, and the first two use the left-hand marker.
            // Odd seats are mirrored, which puts the marker in use on the figure's left for seats
            // 0 and 3: there the bubble opens leftwards, away from the figure, as the game's does.
            var seat = room.Characters.IndexOf(character);
            var marker = Nodes.Get<Control>(character, seat < 2 ? "%ThoughtBubbleLeft" : "%ThoughtBubbleRight");
            var side = seat is 0 or 3 ? DialogueSide.Right : DialogueSide.Left;
            return ScreenPosition(marker) is { } at ? new Spot(room, at, side) : null;
        }
        catch (Exception ex)
        {
            LogOnce("rest site spot", ex);
            return null;
        }
    }

    /// <summary>
    /// Shop: above a player's figure, unless the inventory is open over it. The local player's
    /// figure comes first; <paramref name="figure"/> picks another player's in co-op.
    /// </summary>
    internal static Spot? ShopSpot(int figure = 0)
    {
        try
        {
            var room = NMerchantRoom.Instance;
            if (!Nodes.Alive(room) || room.Inventory?.IsOpen == true) return null;
            // The figures have no talk marker, hence the offset.
            return ScreenPosition(room.PlayerVisuals.ElementAtOrDefault(figure)) is { } at ? new Spot(room, at + ShopOffset) : null;
        }
        catch (Exception ex)
        {
            LogOnce("shop spot", ex);
            return null;
        }
    }

    /// <summary>
    /// Over the character while its fight's room is still up but covered by rewards or a card
    /// pick: the game's own speech position for the creature, on the top-bar layer, which draws
    /// above overlays. Null with the map or a capstone up (they hide the room for good), with
    /// no fight room, or when the creature has no node. <paramref name="evenDead"/> as in
    /// <see cref="OverCreature"/>.
    /// </summary>
    internal static Spot? AboveOverlaySpot(Player? me, bool evenDead = false)
    {
        try
        {
            if (me?.Creature is not { } creature || (!evenDead && !creature.IsAlive)) return null;
            if (NMapScreen.Instance?.IsOpen == true || NCapstoneContainer.Instance?.InUse == true) return null;
            var room = NCombatRoom.Instance;
            if (!Nodes.Alive(room) || !Nodes.Alive(room.GetCreatureNode(creature))) return null;
            if (NRun.Instance?.GlobalUi?.AboveTopBarVfxContainer is not Node layer || !Nodes.Alive(layer)) return null;
            var at = NThoughtBubbleVfx.GetCreatureSpeechPosition(creature);
            return at == Vector2.Zero ? null : new Spot(layer, at);
        }
        catch (Exception ex)
        {
            LogOnce("spot above the overlay", ex);
            return null;
        }
    }

    /// <summary>Anywhere else in a run: beside the top-bar portrait, on the run's VFX layer above the top bar.</summary>
    internal static Spot? PortraitSpot()
    {
        try
        {
            var ui = NRun.Instance?.GlobalUi;
            if (ui?.AboveTopBarVfxContainer is not Node layer || !Nodes.Alive(layer)) return null;
            if (ui.TopBar?.Portrait is not { } portrait || !Nodes.Alive(portrait)) return null;
            return new Spot(layer, portrait.GlobalPosition + PortraitOffset + new Vector2(0, portrait.Size.Y));
        }
        catch (Exception ex)
        {
            LogOnce("top bar spot", ex);
            return null;
        }
    }

    /// <summary>
    /// Outside a run (main menu, character select), and the last resort in one: a fixed spot
    /// on the left third of the screen, parented to NGame so it draws above the menus and the run.
    /// </summary>
    internal static Spot? ScreenSpot()
    {
        try
        {
            var game = NGame.Instance;
            if (!Nodes.Alive(game)) return null;
            var size = game.GetViewport().GetVisibleRect().Size;
            return new Spot(game, size * ScreenFraction);
        }
        catch (Exception ex)
        {
            LogOnce("screen spot", ex);
            return null;
        }
    }

    /// <summary>A live Control's or Node2D's global position; null for anything else.</summary>
    private static Vector2? ScreenPosition(object? node) => node switch
    {
        Control c when Nodes.Alive(c) => c.GlobalPosition,
        Node2D n when Nodes.Alive(n) => n.GlobalPosition,
        _ => null,
    };

    /// <summary>Parents a new bubble or cloud (the factories don't). Null, and the node freed, when that fails.</summary>
    private static T? Adopt<T>(Node parent, T? node) where T : Node
    {
        if (node is null) return null;
        try
        {
            // The game's own (Helpers): adds at once when the parent is ready, else deferred.
            parent.AddChildSafely(node);
            return node;
        }
        catch (Exception ex)
        {
            LogOnce("AddChildSafely", ex);
        }
        try
        {
            if (node.GetParent() is null) parent.AddChild(node);
            return node;
        }
        catch (Exception ex)
        {
            LogOnce("AddChild", ex);
            Free(node);
            return null;
        }
    }

    /// <summary>
    /// AnimOut and GoAway are async: what they throw while setting up the fade lands in the task
    /// they return, not in the caller. A fade that failed that way would leave the node up, so it is freed.
    /// </summary>
    private static void Fading(string what, Node node, Task fade)
    {
        if (!fade.IsFaulted) return;
        LogOnce(what, fade.Exception?.GetBaseException() ?? new InvalidOperationException("the fade failed"));
        Free(node);
    }

    private static void Free(Node node)
    {
        try
        {
            if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        }
        catch
        {
            // Already on its way out.
        }
    }

    // ---------- colour ----------

    /// <summary>
    /// The local character's bubble colour: its SpeechBubbleColor, Purple for the Necrobinder
    /// (hers falls back to the default look), White when no character is known. Before a run,
    /// the character picked at character select.
    /// </summary>
    private static VfxColor BubbleColor(Player? me)
    {
        try
        {
            if (me?.Character is { } ch) return ColorFor(ch);
        }
        catch (Exception ex)
        {
            LogOnce("bubble colour", ex);
        }
        return me is null ? PickedColor() : VfxColor.White;
    }

    /// <summary>The colour of the character picked at character select, White anywhere else.</summary>
    private static VfxColor PickedColor()
    {
        try
        {
            // The screen is created up front, so it counts only while on top with a lobby.
            // LocalPlayer is a struct copy, re-read on every call.
            if (NGame.Instance?.MainMenu?.SubmenuStack?.Peek() is NCharacterSelectScreen { Lobby: { } lobby }
                && lobby.LocalPlayer.character is { } pick)
                return ColorFor(pick);
        }
        catch (Exception ex)
        {
            LogOnce("character select colour", ex);
        }
        return VfxColor.White;
    }

    internal static VfxColor ColorFor(CharacterModel character) =>
        character is Necrobinder ? VfxColor.Purple : character.SpeechBubbleColor;

    // ---------- thought cloud ----------

    /// <summary>
    /// Shows the thought cloud over a voice's speaker once it has been deciding for a while
    /// in combat, and ends it as soon as that stops being true: the decision is made, a speech
    /// bubble needs the spot, the bot is paused, the fight ends or the creature is gone.
    /// <paramref name="since"/> is when the speaker was handed the decision it is on, null for
    /// none; the caller has already ruled out one from an earlier room or held too long.
    /// </summary>
    internal static void UpdateCloud(Voice v, DateTime? since, bool paused, DateTime now)
    {
        var speaking = v.Bubble is not null || v.Carried is not null || v.Pending.Count > 0;

        if (v.Cloud is not null)
        {
            var keep = since == v.CloudFor && !speaking && !paused && !CombatReader.FightOver()
                       && v.CloudOver is { IsAlive: true }
                       && GodotObject.IsInstanceValid(v.Cloud) && !v.Cloud.IsQueuedForDeletion();
            if (keep) return;
            EndCloud(v);
        }

        if (since is not { } started || now - started < ThinkDelay || speaking || now < v.CloudRetryAt) return;
        if (paused || CombatReader.FightOver()) return;

        ShowCloud(v, started);
        if (v.Cloud is null) v.CloudRetryAt = now + CloudRetry;
    }

    private static void ShowCloud(Voice v, DateTime since)
    {
        if (v.Speaker() is not { } me || CombatVfx(me) is not { } layer) return;
        NThoughtBubbleVfx? cloud;
        // No duration: it stays until GoAway. The game's own timer is skipped in instant mode.
        try { cloud = NThoughtBubbleVfx.Create("...", me.Creature, null); }
        catch (Exception ex)
        {
            LogOnce("NThoughtBubbleVfx.Create", ex);
            return;
        }
        if (Adopt(layer, cloud) is not { } shown) return;
        v.Cloud = shown;
        v.CloudOver = me.Creature;
        v.CloudFor = since;
    }

    private static void EndCloud(Voice v)
    {
        var cloud = v.Cloud;
        v.Cloud = null;
        v.CloudOver = null;
        v.CloudFor = null;
        if (cloud is null || !GodotObject.IsInstanceValid(cloud) || cloud.IsQueuedForDeletion()) return;
        try
        {
            if (cloud.IsInsideTree()) Fading("GoAway", cloud, cloud.GoAway());
            else cloud.QueueFree();
        }
        catch (Exception ex)
        {
            LogOnce("GoAway", ex);
            Free(cloud);
        }
    }

    // ---------- text ----------

    /// <summary>
    /// Cuts a line into chunks of about <paramref name="limit"/> characters and of similar
    /// length: at a sentence end where one falls in range, else between words, else (one very
    /// long word) mid-word.
    /// </summary>
    private static List<string> Split(string text, int limit = ChunkLength)
    {
        limit = Math.Max(limit, ChunkLength / 2);
        var chunks = new List<string>();
        var rest = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        while (rest.Length > limit)
        {
            // Aim for even chunks: 120 characters make two of about 60, not 100 and 20.
            var count = (rest.Length + limit - 1) / limit;
            var target = rest.Length / count;
            var cut = BestBreak(rest, target, sentenceEnd: true, limit) ?? BestBreak(rest, target, sentenceEnd: false, limit) ?? HardCut(rest, limit);
            chunks.Add(rest[..cut].TrimEnd());
            rest = rest[cut..].TrimStart();
        }
        if (rest.Length > 0) chunks.Add(rest);
        return chunks;
    }

    /// <summary>The space nearest <paramref name="target"/>, between half of it and the chunk limit, optionally only after a sentence; null when none.</summary>
    private static int? BestBreak(string text, int target, bool sentenceEnd, int limit)
    {
        int? best = null;
        var last = sentenceEnd ? limit + SentenceSlack : limit;
        for (var i = target / 2; i <= last && i < text.Length; i++)
        {
            if (text[i] != ' ' || (sentenceEnd && !EndsSentence(text, i))) continue;
            if (best is null || Math.Abs(i - target) < Math.Abs(best.Value - target)) best = i;
        }
        return best;
    }

    /// <summary>The text before this space ends in ".", "!", "?" or an ellipsis, maybe inside closing quotes or brackets.</summary>
    private static bool EndsSentence(string text, int space)
    {
        var j = space - 1;
        while (j > 0 && text[j] is '"' or '\'' or ')' or '”' or '’') j--;
        return j >= 0 && text[j] is '.' or '!' or '?' or '…';
    }

    private static int HardCut(string text, int limit) =>
        char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit;

    /// <summary>How long a chunk stays up, as the game's TalkCmd works it out.</summary>
    private static double Seconds(string chunk) =>
        Math.Clamp(chunk.Count(c => !char.IsWhiteSpace(c)) * SecondsPerLetter, MinSeconds, MaxSeconds);

    /// <summary>
    /// The bubble's label parses BBCode, and an unbalanced closing tag throws in its auto-size
    /// parser. Every "[" becomes [lb], as in the game's EscapeBbcodeTags, which leaves no tag at
    /// all; a "]" by itself is plain text to both parsers.
    /// </summary>
    internal static string Escape(string text) => text.Replace("[", "[lb]");

    internal static void LogOnce(string what, Exception ex)
    {
        if (Logged.Add(what)) ModEntry.LogError($"speech bubbles: {what}: {ex.Message}");
    }
}
