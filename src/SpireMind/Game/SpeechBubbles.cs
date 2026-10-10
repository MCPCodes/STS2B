// NSpeechBubbleVfx, NThoughtBubbleVfx and VfxColor come from MegaCrit.Sts2.Core.Nodes.Vfx, and
// DialogueSide from MegaCrit.Sts2.Core.Nodes.Vfx.Utilities; both are in the global usings.

namespace SpireMind.Game;

/// <summary>
/// The bot's chat lines as the game's own speech bubbles over its character, and a thought
/// cloud while it decides in combat (docs/chat-and-persona.md, "Speech and thought bubbles").
/// The game draws one NSpeechBubbleVfx per call and queues nothing, so a long line is cut
/// into bubble-sized chunks here and shown one after another, one bubble at a time.
///
/// Everything runs on the main thread: <see cref="Say"/> from the chat overlay's tick, and
/// <see cref="Tick"/> from ModEntry's SceneTree.ProcessFrame handler. Every game call is
/// guarded: a member that turns out wrong costs the bubble (the chat log still has the line)
/// and one log line, never the bridge.
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

    /// <summary>How long the bot has been deciding before the thought cloud shows, so quick moves don't flash it.</summary>
    private static readonly TimeSpan ThinkDelay = TimeSpan.FromSeconds(2.5);

    /// <summary>A decision held longer than this was left behind (the session ended, or the human took over by hand), so it shows no cloud.</summary>
    private static readonly TimeSpan ThinkAbandoned = TimeSpan.FromSeconds(120);

    /// <summary>After a thought cloud failed to appear, wait this long before trying again.</summary>
    private static readonly TimeSpan CloudRetry = TimeSpan.FromSeconds(3);

    /// <summary>The fixed spot outside a run, as a fraction of the screen: the left third, a little above the middle. Tune in game.</summary>
    private static readonly Vector2 ScreenFraction = new(0.2f, 0.42f);

    /// <summary>From the shop's player figure to above its head. Tune in game.</summary>
    private static readonly Vector2 ShopOffset = new(60, -250);

    /// <summary>From the top-bar portrait's corner to beside it; the portrait's height is added to Y.</summary>
    private static readonly Vector2 PortraitOffset = new(110, 235);

    /// <summary>Chunks of the bot's lines waiting their turn, escaped only when shown.</summary>
    private static readonly Queue<string> Pending = new();

    private static NSpeechBubbleVfx? _bubble;

    /// <summary>When the bubble on screen was shown, to tell in the log how long one lasted.</summary>
    private static DateTime _bubbleShown;

    /// <summary>The chunk on screen as it was queued: what a room change carries over.</summary>
    private static string? _bubbleChunk;

    /// <summary>
    /// True when the bubble on screen sits in the room (over the creature, at the rest site or
    /// in the shop) and so goes with it, or under whatever covers it. One by the top bar
    /// outlives rooms.
    /// </summary>
    private static bool _bubbleInRoom;

    /// <summary>A chunk cut short when its room went away or was covered, to show again where it can be seen.</summary>
    private static (string Chunk, double Secs)? _carried;

    /// <summary>When the next chunk may replace <see cref="_bubble"/>: the moment it would start fading out by itself.</summary>
    private static DateTime _bubbleDone;

    /// <summary>When <see cref="_bubble"/> has freed itself, give or take <see cref="Margin"/>.</summary>
    private static DateTime _bubbleGone;

    private static NThoughtBubbleVfx? _cloud;
    private static Creature? _cloudOver;

    /// <summary>The hub's ThinkingSince the cloud was shown for; a newer decision or none ends it.</summary>
    private static DateTime? _cloudFor;

    private static DateTime _cloudRetryAt;

    /// <summary>The run and room the bubbles on screen belong to; (null, null) outside a run.</summary>
    private static (object? Run, object? Room) _place;

    /// <summary>When <see cref="_place"/> last changed. A decision handed over before it belongs to an earlier room.</summary>
    private static DateTime _placeSince;

    /// <summary>Failures already logged, so a wrong guess about a game member logs once instead of every frame.</summary>
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);

    /// <summary>Queues the bot's line as one or more bubbles; the next ticks show them. Main thread.</summary>
    public static void Say(string text)
    {
        foreach (var chunk in Split(text))
            Pending.Enqueue(chunk);
        while (Pending.Count > MaxQueued)
            Pending.Dequeue();
    }

    /// <summary>
    /// Every frame on the main thread: clears everything when the run changes, carries a line
    /// over when only the room does, shows the next chunk once the one before has been up long
    /// enough, and shows or ends the thought cloud.
    /// </summary>
    public static void Tick(ChatHub hub)
    {
        try
        {
            var now = DateTime.UtcNow;
            var place = CurrentPlace();
            if (place != _place)
            {
                if (place.Run is null || !ReferenceEquals(place.Run, _place.Run))
                {
                    // A new run, the end of one or the main menu: nothing said before belongs here.
                    Clear();
                }
                else
                {
                    // Another room of the same run. The bot often speaks and moves on in the same
                    // breath (a map node, an event's Leave), so its line carries over instead of
                    // going with the room. The thought cloud is the room's.
                    EndCloud();
                    CarryOver(now);
                }
                _place = place;
                _placeSince = now;
            }
            else if (_bubble is not null && _bubbleInRoom && RoomCovered())
            {
                // Rewards or the map opened over the fight the line was spoken in.
                CarryOver(now);
            }

            AdvanceSpeech(now);
            UpdateCloud(hub, now);
        }
        catch (Exception ex)
        {
            LogOnce("tick", ex);
        }
    }

    /// <summary>Drops the queued chunks and dismisses the bubble and the thought cloud on screen. Main thread.</summary>
    public static void Clear()
    {
        if (_bubble is not null || _carried is not null || Pending.Count > 0)
            Trace($"cleared by a run change: {(_bubble is null ? "no bubble up" : $"bubble up for {(DateTime.UtcNow - _bubbleShown).TotalSeconds:0.0}s")}, {Pending.Count} chunks dropped");
        Pending.Clear();
        _carried = null;
        DismissBubble();
        EndCloud();
    }

    // ---------- speech ----------

    private static void AdvanceSpeech(DateTime now)
    {
        // A bubble frees itself when its time is up (and with its room). Past that, one still
        // around is faded out here, so it can't hang over the screen.
        if (_bubble is not null && !GodotObject.IsInstanceValid(_bubble))
            CarryOver(now); // freed with its room before this tick saw the room change
        if (_bubble is not null && (now >= _bubbleGone || !GodotObject.IsInstanceValid(_bubble)))
            DismissBubble();

        if ((_carried is null && Pending.Count == 0) || (_bubble is not null && now < _bubbleDone)) return;

        DismissBubble();
        EndCloud();
        string chunk;
        double secs;
        if (_carried is { } carried)
        {
            (chunk, secs) = carried;
            _carried = null;
        }
        else
        {
            chunk = Pending.Dequeue();
            secs = Seconds(chunk);
        }
        _bubble = Show(Escape(chunk), secs, out _bubbleInRoom);
        _bubbleChunk = chunk;
        _bubbleShown = now;
        _bubbleDone = now + TimeSpan.FromSeconds(FadeIn + Math.Max(secs - 1, 1));
        _bubbleGone = _bubbleDone + TimeSpan.FromSeconds(FadeOut + Margin);
        Trace($"shown for {secs:0.0}s {Where(_bubble)}: \"{(chunk.Length > 40 ? chunk[..40] + "..." : chunk)}\"");
    }

    /// <summary>
    /// Takes down a bubble that sits in the room and queues what is left of its time to be
    /// shown again, by the top bar if the room is gone or covered. A bubble already by the top
    /// bar stays as it is. One with too little time left to read is just dropped.
    /// </summary>
    private static void CarryOver(DateTime now)
    {
        if (_bubble is null || !_bubbleInRoom || _bubbleChunk is not { } chunk) return;
        var left = (_bubbleDone - now).TotalSeconds;
        Trace($"carried over after {(now - _bubbleShown).TotalSeconds:0.0}s with {Math.Max(left, 0):0.0}s left");
        DismissBubble();
        if (left >= MinCarry) _carried = (chunk, Math.Max(left + 1 - FadeIn, MinSeconds));
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
    private static void Trace(string what) => ModEntry.Log($"speech bubble: {what}");

    /// <summary>
    /// Creates the bubble at the spot the docs give for where the local player is, and parents
    /// it. A spot the game won't give falls through to the top-bar portrait, then the fixed
    /// screen spot; null when none works.
    /// </summary>
    private static NSpeechBubbleVfx? Show(string text, double secs, out bool inRoom)
    {
        inRoom = false;
        var ctx = GameContext.Capture();
        var color = BubbleColor(ctx.Me);
        if (!ctx.InRun || NGame.Instance?.MainMenu is not null)
            return Place(ScreenSpot(), text, secs, color) ?? NoSpot();

        NSpeechBubbleVfx? bubble = null;
        if (!RoomCovered())
        {
            if (ctx.InCombat && ctx.Me is { } me)
                bubble = OverCreature(me, text, secs, color);
            else if (ctx.Run?.CurrentRoom?.RoomType == RoomType.RestSite)
                bubble = Place(RestSiteSpot(ctx), text, secs, color);
            else if (ctx.Run?.CurrentRoom?.RoomType == RoomType.Shop)
                bubble = Place(ShopSpot(), text, secs, color);
        }
        if (bubble is not null)
        {
            inRoom = true;
            return bubble;
        }
        return Place(PortraitSpot(), text, secs, color)
            ?? Place(ScreenSpot(), text, secs, color)
            ?? NoSpot();
    }

    /// <summary>Fight: the factory that finds the creature's node and sits the bubble above its head.</summary>
    private static NSpeechBubbleVfx? OverCreature(Player me, string text, double secs, VfxColor color)
    {
        if (CombatVfx(me) is not { } layer) return null;
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
    private static NSpeechBubbleVfx? Place(Spot? spot, string text, double secs, VfxColor color)
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
    private static void DismissBubble()
    {
        var bubble = _bubble;
        _bubble = null;
        if (bubble is null || !GodotObject.IsInstanceValid(bubble) || bubble.IsQueuedForDeletion()) return;
        try
        {
            // AnimOut makes a tween, which needs the tree. Out of it, the bubble's room is being
            // swapped out (and frees it at the end of the frame) or its deferred add hasn't happened.
            if (bubble.IsInsideTree()) Fading("AnimOut", bubble, bubble.AnimOut());
            else bubble.QueueFree();
        }
        catch (Exception ex)
        {
            LogOnce("AnimOut", ex);
            Free(bubble);
        }
    }

    private static NSpeechBubbleVfx? NoSpot()
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
    private readonly record struct Spot(Node Parent, Vector2 At, DialogueSide Side = DialogueSide.Left);

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
    private static bool RoomCovered()
    {
        try { return NMapScreen.Instance?.IsOpen == true || Nodes.TopOverlay() is not null; }
        catch (Exception ex)
        {
            LogOnce("overlay check", ex);
            return false;
        }
    }

    /// <summary>The combat room's VFX layer while the local player's creature stands in it, else null.</summary>
    private static Node? CombatVfx(Player me)
    {
        try
        {
            if (me.Creature is not { IsAlive: true } creature) return null;
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
    private static Spot? RestSiteSpot(GameContext ctx)
    {
        try
        {
            var room = NRestSiteRoom.Instance;
            if (!Nodes.Alive(room) || ctx.Me is not { } me) return null;
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

    /// <summary>Shop: above the local player's figure, unless the inventory is open over it.</summary>
    private static Spot? ShopSpot()
    {
        try
        {
            var room = NMerchantRoom.Instance;
            if (!Nodes.Alive(room) || room.Inventory?.IsOpen == true) return null;
            // The local player's figure comes first. It has no talk marker, hence the offset.
            return ScreenPosition(room.PlayerVisuals.FirstOrDefault()) is { } at ? new Spot(room, at + ShopOffset) : null;
        }
        catch (Exception ex)
        {
            LogOnce("shop spot", ex);
            return null;
        }
    }

    /// <summary>Anywhere else in a run: beside the top-bar portrait, on the run's VFX layer above the top bar.</summary>
    private static Spot? PortraitSpot()
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
    private static Spot? ScreenSpot()
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

    private static VfxColor ColorFor(CharacterModel character) =>
        character is Necrobinder ? VfxColor.Purple : character.SpeechBubbleColor;

    // ---------- thought cloud ----------

    /// <summary>
    /// Shows the thought cloud over the local player once the bot has been deciding for a while
    /// in combat, and ends it as soon as that stops being true: the decision is made, a speech
    /// bubble needs the spot, the bot is paused, the fight ends or the creature is gone. A
    /// decision from an earlier room, or one held past <see cref="ThinkAbandoned"/>, counts as
    /// none: a session that ends mid-decision leaves the hub's mark up for minutes.
    /// </summary>
    private static void UpdateCloud(ChatHub hub, DateTime now)
    {
        var since = hub.ThinkingSince is { } t && t >= _placeSince && now - t <= ThinkAbandoned ? t : (DateTime?)null;
        var speaking = _bubble is not null || Pending.Count > 0;

        if (_cloud is not null)
        {
            var keep = since == _cloudFor && !speaking && !hub.Paused && !CombatReader.FightOver()
                       && _cloudOver is { IsAlive: true }
                       && GodotObject.IsInstanceValid(_cloud) && !_cloud.IsQueuedForDeletion();
            if (keep) return;
            EndCloud();
        }

        if (since is not { } started || now - started < ThinkDelay || speaking || now < _cloudRetryAt) return;
        if (hub.Paused || CombatReader.FightOver()) return;

        ShowCloud(started);
        if (_cloud is null) _cloudRetryAt = now + CloudRetry;
    }

    private static void ShowCloud(DateTime since)
    {
        if (GameContext.Capture().Me is not { } me || CombatVfx(me) is not { } layer) return;
        NThoughtBubbleVfx? cloud;
        // No duration: it stays until GoAway. The game's own timer is skipped in instant mode.
        try { cloud = NThoughtBubbleVfx.Create("...", me.Creature, null); }
        catch (Exception ex)
        {
            LogOnce("NThoughtBubbleVfx.Create", ex);
            return;
        }
        if (Adopt(layer, cloud) is not { } shown) return;
        _cloud = shown;
        _cloudOver = me.Creature;
        _cloudFor = since;
    }

    private static void EndCloud()
    {
        var cloud = _cloud;
        _cloud = null;
        _cloudOver = null;
        _cloudFor = null;
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
    /// Cuts a line into chunks of about <see cref="ChunkLength"/> characters and of similar
    /// length: at a sentence end where one falls in range, else between words, else (one very
    /// long word) mid-word.
    /// </summary>
    private static List<string> Split(string text)
    {
        var chunks = new List<string>();
        var rest = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        while (rest.Length > ChunkLength)
        {
            // Aim for even chunks: 120 characters make two of about 60, not 100 and 20.
            var count = (rest.Length + ChunkLength - 1) / ChunkLength;
            var target = rest.Length / count;
            var cut = BestBreak(rest, target, sentenceEnd: true) ?? BestBreak(rest, target, sentenceEnd: false) ?? HardCut(rest);
            chunks.Add(rest[..cut].TrimEnd());
            rest = rest[cut..].TrimStart();
        }
        if (rest.Length > 0) chunks.Add(rest);
        return chunks;
    }

    /// <summary>The space nearest <paramref name="target"/>, between half of it and the chunk limit, optionally only after a sentence; null when none.</summary>
    private static int? BestBreak(string text, int target, bool sentenceEnd)
    {
        int? best = null;
        var last = sentenceEnd ? ChunkLength + SentenceSlack : ChunkLength;
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

    private static int HardCut(string text) =>
        char.IsHighSurrogate(text[ChunkLength - 1]) ? ChunkLength - 1 : ChunkLength;

    /// <summary>How long a chunk stays up, as the game's TalkCmd works it out.</summary>
    private static double Seconds(string chunk) =>
        Math.Clamp(chunk.Count(c => !char.IsWhiteSpace(c)) * SecondsPerLetter, MinSeconds, MaxSeconds);

    /// <summary>
    /// The bubble's label parses BBCode, and an unbalanced closing tag throws in its auto-size
    /// parser. Every "[" becomes [lb], as in the game's EscapeBbcodeTags, which leaves no tag at
    /// all; a "]" by itself is plain text to both parsers.
    /// </summary>
    private static string Escape(string text) => text.Replace("[", "[lb]");

    private static void LogOnce(string what, Exception ex)
    {
        if (Logged.Add(what)) ModEntry.LogError($"speech bubbles: {what}: {ex.Message}");
    }
}
