using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

// Same namespace as ScreenRouter, which refers to the readers unqualified.
namespace SpireMind.Game;

/// <summary>
/// An event room (NEventRoom), Ancients included: the event's text and options, and choosing
/// one. Options come from the local player's EventModel
/// (RunManager.Instance.EventSynchronizer.GetLocalEvent()); a choice is a click on the
/// option's NEventOptionButton, which the game sends through EventSynchronizer.ChooseLocalOption,
/// so a shared co-op event becomes a vote (docs/game-api.md section e).
///
/// Actions: "option:&lt;n&gt;" for state.options[n] while it is unlocked and its button is
/// enabled, plus the out-of-combat potion actions. The plain proceed option reads "Leave" and
/// is offered only once the event is finished; other proceed options, like the Trial's
/// DOUBLE_DOWN that opens the abandon-run popup, are never offered. An Ancient's options
/// (relic picks) are listed only once its dialogue is over. A fight the event starts belongs
/// to the combat reader; when it pops back to the event, the room node is fetched again
/// rather than trusting the one the router saw before the fight.
/// </summary>
internal sealed class EventReader : IScreen
{
    public static readonly EventReader Instance = new();

    public string Name => "event";

    /// <summary>The room fades in and its text appears on arrival.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(500);

    /// <summary>Option buttons are rebuilt and slide in after every page change.</summary>
    private static readonly TimeSpan PageSettle = TimeSpan.FromMilliseconds(300);

    /// <summary>An option can play out for a while: HP loss, relic or card animations, a fight starting.</summary>
    private static readonly TimeSpan ChooseTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan LeaveTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan VoteTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Give a co-op vote time to go out before "idle" can mean "registered".</summary>
    private static readonly TimeSpan VoteSettle = TimeSpan.FromMilliseconds(500);

    /// <summary>Leaving normally opens the map within a second or two; past this the event is offered again (co-op waits <see cref="VoteHold"/>).</summary>
    private static readonly TimeSpan LeaveHold = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A shared-event vote with the page unchanged for this long is taken to be lost and the
    /// options are offered again. Voting twice is harmless; waiting forever is not.
    /// </summary>
    private static readonly TimeSpan VoteHold = TimeSpan.FromMinutes(2);

    /// <summary>An Ancient still talking after this long is logged once; the reader keeps waiting.</summary>
    private static readonly TimeSpan TalkWarn = TimeSpan.FromSeconds(30);

    /// <summary>Cards from one option's hover tips listed in the state, at most.</summary>
    private const int MaxTipCards = 3;

    private EventReader() { }

    /// <summary>
    /// The model side of the current page, read fresh on every call. <see cref="Key"/> changes
    /// whenever the page does; <see cref="StableOptions"/> says whether the game hands out the
    /// same option objects on every read, so they can identify the page and its buttons.
    /// </summary>
    private sealed record Page(List<EventOption> Options, bool Finished, string Body, string Key, bool StableOptions);

    /// <summary>One option as read just now. <see cref="Refusal"/> is null when it can be chosen right now.</summary>
    private sealed record OptionView(
        int N, EventOption Option, NEventOptionButton? Button, string Title, bool Locked, bool Leave, bool Special, string Label, string? Refusal, JsonObject Json)
    {
        public bool Offered => Refusal is null;
    }

    private sealed record EventView(NEventRoom Room, EventModel Event, Page Page, string Title, string Body, bool Ancient, bool DialogueDone, List<OptionView> Options);

    /// <summary>What an option shows beyond its own text (an Ancient's relic, cards in its hover tips); read once per page.</summary>
    private sealed record Extras(RelicModel? Relic, JsonObject? RelicJson, JsonArray? Cards);

    // ---------- per-room memory (reset when a different room, event or page shows up) ----------

    private static ulong _roomId;
    private static EventModel? _event;
    private static DateTime _firstSeen;
    private static string _pageKey = "";
    private static DateTime _pageSince;
    private static bool _loggedUnmatched;
    private static bool _loggedTalking;

    /// <summary>When we chose Leave on the current page.</summary>
    private static DateTime? _leftAt;

    /// <summary>Our vote on the current page of a shared co-op event.</summary>
    private sealed record Vote(string Title, DateTime At);

    private static Vote? _vote;

    /// <summary>Extras per option index on the current page.</summary>
    private static readonly Dictionary<int, Extras> ExtrasByIndex = new();

    private static void Track(NEventRoom room, EventModel ev, Page page)
    {
        var id = room.GetInstanceId();
        var now = DateTime.UtcNow;
        if (id != _roomId || !ReferenceEquals(ev, _event))
        {
            _roomId = id;
            _event = ev;
            _firstSeen = now;
        }
        else if (page.Key == _pageKey)
        {
            return;
        }

        // A new room, event or page: whatever we chose or voted for before has played out.
        _pageKey = page.Key;
        _pageSince = now;
        _leftAt = null;
        _vote = null;
        _loggedUnmatched = false;
        _loggedTalking = false;
        ExtrasByIndex.Clear();
    }

    // ---------- IScreen ----------

    public GameSnapshot Capture(GameContext ctx, Node? node)
    {
        if (ctx.Run is not { } rs || ctx.Me is not { } me)
            return GameSnapshot.Busy(Name, "run loading");
        if (EventFight(ctx, rs) is { } fight)
            return GameSnapshot.Busy(Name, fight);
        if (Room(node) is not { } room)
            return GameSnapshot.Busy(Name, "event room loading");
        if (LocalEvent() is not { } ev)
            return GameSnapshot.Busy(Name, "event loading");

        var page = ReadPage(ev);
        Track(room, ev, page);
        var view = Read(ctx, room, ev, page);

        var waiting = WaitingReason(ctx, view);
        var actions = new List<LegalAction>();
        if (waiting is null)
        {
            foreach (var o in view.Options)
                if (o.Offered) actions.Add(new LegalAction($"option:{o.N}", o.Label));

            // Potion actions alone are not an event decision; they ride along with the options.
            if (actions.Count > 0)
                actions.AddRange(PotionActions.Legal(ctx, false));
            else
                waiting = NothingOffered(view);
        }

        var state = BuildState(ctx, rs, me, view);
        if (waiting is not null)
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = waiting, State = state };

        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    public ActionResult Execute(GameContext ctx, Node? node, string actionId)
    {
        if (PotionActions.TryExecute(ctx, actionId, false, out var potionResult))
            return potionResult;

        if (!actionId.StartsWith("option:", StringComparison.Ordinal) || !int.TryParse(actionId.AsSpan(7), out var n))
            return ActionResult.Fail($"Unknown event action '{actionId}'.");

        if (ctx.Run is not { } rs || ctx.Me is null)
            return ActionResult.Fail("No run in progress.");
        if (EventFight(ctx, rs) is { } fight)
            return ActionResult.Fail($"Not at the event right now ({fight}).");
        if (Room(node) is not { } room)
            return ActionResult.Fail("The event room is gone.");
        if (LocalEvent() is not { } ev)
            return ActionResult.Fail("No event is running.");

        // Re-read everything: the page may have moved on since the snapshot.
        var page = ReadPage(ev);
        Track(room, ev, page);
        var view = Read(ctx, room, ev, page);
        if (WaitingReason(ctx, view) is { } waiting)
            return ActionResult.Fail($"Not ready: {waiting}.");
        if (n < 0 || n >= view.Options.Count)
            return ActionResult.Fail($"There is no event option {n}.");

        var option = view.Options[n];
        if (option.Refusal is { } refusal)
            return ActionResult.Fail(refusal);

        return Choose(ctx, rs, view, option);
    }

    // ---------- choosing ----------

    private static ActionResult Choose(GameContext ctx, RunState rs, EventView view, OptionView o)
    {
        var button = o.Button!;
        var ev = view.Event;
        var room = view.Room;
        var vote = IsVote(ctx, ev);
        var leaving = o.Leave;
        var multiplayer = ctx.IsMultiplayer;

        // Any of these changing means the game has acted on the choice.
        var pageBefore = view.Page.Key;
        var finishedBefore = view.Page.Finished;
        var roomModel = rs.CurrentRoom;
        var roomCount = rs.CurrentRoomCount;
        var overlayBefore = Nodes.TopOverlay();

        var click = Clicks.Once(button, () =>
        {
            button.ForceClick();
            // Latched after the click, so a click that throws leaves nothing to wait on.
            var now = DateTime.UtcNow;
            if (leaving) _leftAt = now;
            if (vote) _vote = new Vote(leaving ? "Leave" : o.Title, now);
        }, $"the {o.Title} option");
        if (!click.Ok)
            return click;

        // A vote, or leaving in co-op, changes nothing here until teammates act; Capture reports that wait.
        var waitsOnTeam = vote || (leaving && multiplayer);
        ActionTracker.Begin(
            leaving ? "leaving the event" : vote ? $"voting for {o.Title}" : $"choosing {o.Title}",
            c =>
            {
                if (!Nodes.Alive(room) || c.Run is not { } run) return true;
                if (!ReferenceEquals(run.CurrentRoom, roomModel) || run.CurrentRoomCount != roomCount) return true;
                if (c.InCombat || NMapScreen.Instance?.IsOpen == true || Nodes.ModalOpen()) return true;
                // Card choices, rewards and the like open over the event and are the next decision.
                if (!ReferenceEquals(Nodes.TopOverlay(), overlayBefore)) return true;
                if (LocalEvent() is not { } current || !ReferenceEquals(current, ev)) return true;
                var after = ReadPage(current);
                if (after.Finished != finishedBefore || after.Key != pageBefore) return true;
                return waitsOnTeam && ActionTracker.GameLooksIdle();
            },
            leaving ? LeaveTimeout : vote ? VoteTimeout : ChooseTimeout,
            minWait: waitsOnTeam ? VoteSettle : null);
        return ActionResult.Success;
    }

    // ---------- reading ----------

    private static string? WaitingReason(GameContext ctx, EventView view)
    {
        if (ActionTracker.Pending is { } pending)
            return $"resolving {pending}";

        var now = DateTime.UtcNow;
        // Teammates can take minutes over their own choices; re-offering Leave every few seconds would only re-click it.
        var leaveHold = ctx.IsMultiplayer ? VoteHold : LeaveHold;
        if (_leftAt is { } left && now - left < leaveHold)
            return ctx.IsMultiplayer ? "left the event; waiting for teammates" : "leaving the event";

        if (_vote is { } vote)
        {
            if (now - vote.At < VoteHold)
                return $"voted for {vote.Title}; waiting for teammates' votes";
            ModEntry.Log($"event vote for {vote.Title} changed nothing in {VoteHold.TotalSeconds:0} s; offering the options again");
            _vote = null;
        }

        if (_leftAt is not null)
        {
            ModEntry.Log($"still at event {view.Title} {leaveHold.TotalSeconds:0} s after choosing Leave; offering it again");
            _leftAt = null;
        }

        if (view.Ancient && !view.DialogueDone)
        {
            // There is no action to move the dialogue on, so a dialogue that waits for clicks would stall here.
            if (!_loggedTalking && now - _pageSince > TalkWarn)
            {
                _loggedTalking = true;
                ModEntry.Log($"Ancient {view.Title} still talking after {TalkWarn.TotalSeconds:0} s (DefaultFocusedControl still null); does its dialogue need clicks?");
            }
            return "Ancient is talking";
        }
        if (now - _firstSeen < Settle)
            return "event appearing";
        if (now - _pageSince < PageSettle)
            return "event options appearing";
        return null;
    }

    /// <summary>Why nothing is offered although the event is otherwise ready.</summary>
    private static string NothingOffered(EventView view)
    {
        var options = view.Options;
        if (options.Count == 0)
            return view.Page.Finished ? "event finished; waiting for the leave option" : "event options appearing";
        if (options.All(o => o.Locked || o.Special))
            return "no event option the bot can take (all locked or special); a human has to choose";
        if (!view.Page.Finished && options.Any(o => o.Leave && !o.Locked))
            return "event finishing";

        // Unlocked options still without a button after the page settled point at a layout we can't read.
        if (!_loggedUnmatched && options.Any(o => o.Button is null && !o.Locked && !o.Special))
        {
            _loggedUnmatched = true;
            ModEntry.Log($"event {view.Title}: no option button matched some options (layout {view.Room.Layout?.GetType().Name ?? "none"})");
        }
        return "no event option is clickable yet";
    }

    private static Page ReadPage(EventModel ev)
    {
        var options = OptionsOf(ev);
        var again = OptionsOf(ev);
        var stable = options.Count == again.Count && options.Zip(again).All(p => ReferenceEquals(p.First, p.Second));

        var finished = ev.IsFinished;
        var body = ModelBody(ev);
        var key = new StringBuilder(finished ? "done|" : "open|");
        foreach (var o in options)
        {
            // New option objects mean a new page even when the wording repeats.
            if (stable) key.Append(RuntimeHelpers.GetHashCode(o)).Append(':');
            if (o.IsLocked) key.Append("locked:");
            key.Append(Describe.SafeText(() => o.Title)).Append('|');
        }
        key.Append(body);
        return new Page(options, finished, body, key.ToString(), stable);
    }

    private static EventView Read(GameContext ctx, NEventRoom room, EventModel ev, Page page)
    {
        var layout = room.Layout;
        var ancient = layout is NAncientEventLayout; // VERIFY: NAncientEventLayout derives from the declared type of NEventRoom.Layout (else this "is" does not compile)
        // VERIFY: NAncientEventLayout.DefaultFocusedControl is a property that stays null until the Ancient's dialogue is over
        var dialogueDone = layout is not NAncientEventLayout talker || talker.DefaultFocusedControl is not null;

        var buttons = MatchButtons(page, Buttons(room));
        var vote = IsVote(ctx, ev);
        var options = new List<OptionView>();
        for (var i = 0; i < page.Options.Count; i++)
            options.Add(View(i, page.Options[i], buttons[i], page.Finished, ancient, vote));

        var body = page.Body.Length > 0 ? page.Body : LayoutText(room);
        var title = Describe.SafeText(() => ev.Title); // VERIFY: EventModel.Title is the event's name (LocString), "Neow" and the like for Ancients
        return new EventView(room, ev, page, title, body, ancient, dialogueDone, options);
    }

    private static OptionView View(int n, EventOption option, NEventOptionButton? button, bool finished, bool ancient, bool vote)
    {
        var title = Describe.SafeText(() => option.Title);
        var text = Describe.SafeText(() => option.Description);
        var locked = option.IsLocked;
        var kills = WillKill(option);
        var key = $"{option.TextKey}";
        var leave = option.IsProceed && IsProceedKey(key);
        var special = option.IsProceed && !leave;
        var extras = ExtrasOf(n, option, title, text, ancient);
        var name = title.Length > 0 ? title : text.Length > 0 ? Shorten(text) : $"option {n}";

        // note: a lasting reason shown in the state; refusal: why it can't be chosen right now.
        string? note = null;
        string? refusal = null;
        if (locked)
        {
            refusal = $"{name} is locked.";
        }
        else if (special)
        {
            note = key.Contains("DOUBLE_DOWN", StringComparison.OrdinalIgnoreCase)
                ? "opens the abandon-run popup; not offered"
                : "leaves the event in a way the bot does not drive; not offered";
            refusal = $"{name} {note}.";
        }
        else if (leave && !finished)
        {
            refusal = "The event is not finished yet.";
        }
        else if (button is null)
        {
            refusal = $"No button found for {name}.";
        }
        else if (!Nodes.IsShown(button) || !button.IsEnabled)
        {
            refusal = $"{name} is not clickable right now.";
        }
        else if (Clicks.RecentlyClicked(button))
        {
            refusal = $"{name} was just clicked; waiting for the game to react.";
        }

        string label;
        if (leave)
        {
            label = "Leave";
        }
        else
        {
            var relic = extras.Relic is { } r ? Describe.Text(r.Title) : "";
            label = ancient
                ? $"{(vote ? "Vote for" : "Take")} {(relic.Length > 0 ? relic : name)}"
                : $"{(vote ? "Vote for" : "Choose")} {name}";
            if (kills) label += " (this kills you)";
        }

        var json = new JsonObject { ["n"] = n, ["title"] = name };
        if (text.Length > 0 && text != name) json["text"] = text;
        if (leave) json["leave"] = true;
        if (locked) json["locked"] = true;
        if (kills) json["will_kill_player"] = true;
        if (extras.RelicJson is { } relicJson) json["relic"] = relicJson.DeepClone();
        if (extras.Cards is { } cards) json["cards"] = cards.DeepClone();
        if (note is not null) json["note"] = note;

        return new OptionView(n, option, button, name, locked, leave, special, label, refusal, json);
    }

    /// <summary>
    /// The relic an Ancient offers and the cards an option's hover tips show, as a human sees
    /// them on hover. A non-Ancient option's relic member is not read: "a random relic" can be
    /// rolled in advance without being shown.
    /// </summary>
    private static Extras ExtrasOf(int n, EventOption option, string title, string text, bool ancient)
    {
        if (ExtrasByIndex.TryGetValue(n, out var cached)) return cached;

        RelicModel? relic = null;
        var cards = new JsonArray();
        try
        {
            if (ancient)
                relic = Nodes.TryGetPrivate(option, "Relic") as RelicModel; // VERIFY: an Ancient's EventOption holds the offered relic in a member named Relic

            // VERIFY: EventOption.HoverTips enumerates the option's hover tips; card tips expose the card as Card, relic tips the relic as Relic
            if (Nodes.TryGetPrivate(option, "HoverTips") is System.Collections.IEnumerable tips)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var tip in tips)
                {
                    if (tip is null) continue;
                    if (Nodes.TryGetPrivate(tip, "Card") is CardModel card)
                    {
                        if (cards.Count < MaxTipCards && seen.Add(Describe.CardName(card)))
                            cards.Add(Describe.Card(card, PileType.Deck));
                    }
                    else if (relic is null && Nodes.TryGetPrivate(tip, "Relic") is RelicModel shown)
                    {
                        relic = shown;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Cached below either way, so a broken option logs once per page.
            ModEntry.LogError($"reading event option {n} hover tips: {ex.Message}");
        }

        JsonObject? relicJson = null;
        if (relic is not null)
        {
            var described = Describe.Relic(relic);
            // Ancient options are often just the relic's own name and text; don't repeat them.
            var same = (string?)described["name"] == title && (string?)described["text"] == text;
            if (!same) relicJson = described;
        }

        var extras = new Extras(relic, relicJson, cards.Count > 0 ? cards : null);
        ExtrasByIndex[n] = extras;
        return extras;
    }

    private static JsonObject BuildState(GameContext ctx, RunState rs, Player me, EventView view)
    {
        var state = new JsonObject
        {
            ["run"] = Describe.RunInfo(rs),
            ["event"] = view.Title,
        };
        if (view.Ancient) state["ancient"] = true;
        if (ctx.IsMultiplayer) state["shared_vote"] = view.Event.IsShared;
        if (view.Body.Length > 0) state["text"] = view.Body;
        if (view.Page.Finished) state["finished"] = true;

        // An Ancient's options show once it stops talking; until then a human sees none.
        if (view.DialogueDone)
        {
            var options = new JsonArray();
            foreach (var o in view.Options) options.Add(o.Json);
            state["options"] = options;
        }
        if (_vote is { } vote) state["voted"] = vote.Title;

        var player = Describe.PlayerRun(me, includeDeck: true);
        player.Remove("potions"); // replaced by the detailed slots below
        state["player"] = player;
        state["potions"] = PotionActions.Describe(ctx, false);

        // Everyone lives with a shared event's result, so teammates' HP matters to the vote.
        if (ctx.IsMultiplayer && view.Event.IsShared && Describe.TeamHp(ctx.Teammates) is { Count: > 0 } team)
            state["team"] = team;
        return state;
    }

    // ---------- buttons ----------

    private static List<NEventOptionButton> Buttons(NEventRoom room)
    {
        IEnumerable<NEventOptionButton>? source = null;
        try
        {
            if (room.Layout is { } layout && GodotObject.IsInstanceValid(layout))
                source = layout.OptionButtons; // VERIFY: NEventLayout.OptionButtons is an IEnumerable<NEventOptionButton> in on-screen order
        }
        catch (Exception)
        {
            source = null;
        }

        var buttons = source?.Where(b => b is not null && Nodes.Alive(b)).ToList();
        if (buttons is { Count: > 0 }) return buttons;

        // Custom layouts may keep their buttons elsewhere in the room.
        return Nodes.FindAll<NEventOptionButton>(room).Where(Nodes.Alive).ToList();
    }

    /// <summary>
    /// The button for each option: by the option model the button holds when it exposes one
    /// (a button holding an option that is no longer on the page belongs to the last page and
    /// is skipped), else by its index. When neither member works: by the title the button
    /// shows, then by position when the leftover buttons and options pair up one to one.
    /// </summary>
    private static NEventOptionButton?[] MatchButtons(Page page, List<NEventOptionButton> buttons)
    {
        var options = page.Options;
        var matched = new NEventOptionButton?[options.Count];
        var loose = new List<NEventOptionButton>();

        foreach (var button in buttons)
        {
            var model = page.StableOptions ? OptionOf(button) : null;
            if (model is null)
            {
                loose.Add(button);
                continue;
            }
            var i = options.FindIndex(o => ReferenceEquals(o, model));
            if (i >= 0 && matched[i] is null) matched[i] = button;
        }
        if (loose.Count == 0) return matched;

        var unplaced = new List<NEventOptionButton>();
        foreach (var button in loose)
        {
            if (Nodes.TryGetPrivate(button, "Index") is int i && i >= 0 && i < options.Count && matched[i] is null) // VERIFY: NEventOptionButton's Index member (if any) is the option's index in CurrentOptions
                matched[i] = button;
            else
                unplaced.Add(button);
        }

        // A member that placed any button is trusted alone. Otherwise go by what the buttons draw,
        // then by position for the rest.
        if (matched.Any(b => b is not null)) return matched;
        unplaced = MatchByText(options, matched, unplaced);

        var open = Enumerable.Range(0, options.Count).Where(i => matched[i] is null).ToList();
        if (open.Count > 0 && unplaced.Count == open.Count)
        {
            // VERIFY: Layout.OptionButtons follows CurrentOptions order, one button per option (locked ones included)
            for (var k = 0; k < open.Count; k++)
                matched[open[k]] = unplaced[k];
        }
        return matched;
    }

    /// <summary>
    /// Places each button one of whose labels starts with exactly one unplaced option's title,
    /// and returns the buttons left over. It reads only what is on screen, so it works without
    /// knowing the button's members.
    /// </summary>
    private static List<NEventOptionButton> MatchByText(List<EventOption> options, NEventOptionButton?[] matched, List<NEventOptionButton> buttons)
    {
        if (buttons.Count == 0) return buttons;
        var titles = options.Select(o => Describe.SafeText(() => o.Title).TrimStart('[', ' ')).ToList();
        var claim = new int[buttons.Count];
        for (var b = 0; b < buttons.Count; b++)
        {
            var labels = Nodes.FindAll<Godot.Control>(buttons[b])
                .Select(Nodes.LabelText)
                .Where(t => t.Length > 0)
                .Select(t => t.TrimStart('[', ' '))
                .ToList();

            var hits = new List<int>();
            for (var i = 0; i < titles.Count; i++)
            {
                if (matched[i] is not null || titles[i].Length < 2) continue;
                var title = titles[i];
                if (labels.Any(l => l.StartsWith(title, StringComparison.Ordinal))) hits.Add(i);
            }
            // Two titles fitting one button (a shared prefix, duplicate wording) is a guess; leave it.
            claim[b] = hits.Count == 1 ? hits[0] : -1;
        }

        // An option two buttons claim (one may be the last page's, still fading out) stays open too.
        var left = new List<NEventOptionButton>();
        for (var b = 0; b < buttons.Count; b++)
        {
            var i = claim[b];
            if (i >= 0 && claim.Count(c => c == i) == 1) matched[i] = buttons[b];
            else left.Add(buttons[b]);
        }
        return left;
    }

    /// <summary>The option model a button was built for, when it exposes one.</summary>
    private static EventOption? OptionOf(NEventOptionButton button) =>
        // VERIFY: NEventOptionButton keeps its EventOption in a member named Option (or _option)
        (Nodes.TryGetPrivate(button, "Option") ?? Nodes.TryGetPrivate(button, "_option")) as EventOption;

    // ---------- model access ----------

    private static EventModel? LocalEvent()
    {
        try
        {
            // VERIFY: GetLocalEvent returns the local player's EventModel, the same instance for the whole event
            return RunManager.Instance?.EventSynchronizer?.GetLocalEvent();
        }
        catch
        {
            return null;
        }
    }

    private static List<EventOption> OptionsOf(EventModel ev)
    {
        try
        {
            // VERIFY: CurrentOptions enumerates EventOption, the type with Title, Description, IsLocked, IsProceed, TextKey and WillKillPlayer
            return ev.CurrentOptions?.Where(o => o is not null).ToList() ?? new List<EventOption>();
        }
        catch
        {
            return new List<EventOption>();
        }
    }

    /// <summary>The current page's text from the model; the layout's labels stand in when it has none.</summary>
    private static string ModelBody(EventModel ev) =>
        Describe.SafeText(() => Nodes.TryGetPrivate(ev, "Description")); // VERIFY: EventModel's current page text is a member named Description (LocString) that follows page changes, not just the first page

    private static bool WillKill(EventOption option)
    {
        try
        {
            return option.WillKillPlayer; // VERIFY: WillKillPlayer is a bool property, not a method taking the player
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The plain proceed option. Other proceed options (the Trial's DOUBLE_DOWN) do more than leave.</summary>
    private static bool IsProceedKey(string key) =>
        key.Equals("PROCEED", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".PROCEED", StringComparison.OrdinalIgnoreCase);

    private static bool IsVote(GameContext ctx, EventModel ev) => ctx.IsMultiplayer && ev.IsShared;

    /// <summary>A fight the event started is up, or its room hasn't popped back yet; the combat and rewards readers have it.</summary>
    private static string? EventFight(GameContext ctx, RunState rs)
    {
        if (ctx.InCombat)
            return "fight started by the event";
        // VERIFY: RunState.BaseRoom is an AbstractRoom whose RoomType is Event for an EventRoom
        if (rs.CurrentRoomCount > 1 && rs.BaseRoom?.RoomType == RoomType.Event && !ReferenceEquals(rs.CurrentRoom, rs.BaseRoom))
            return "returning to the event after the fight";
        return null;
    }

    /// <summary>The page text as drawn: the longest visible label in the layout outside the option buttons.</summary>
    private static string LayoutText(NEventRoom room)
    {
        if (room.Layout is not Node layout || !GodotObject.IsInstanceValid(layout)) return "";
        var best = "";
        foreach (var control in Nodes.FindAll<Godot.Control>(layout))
        {
            if (!control.IsVisibleInTree() || InsideButton(control, layout)) continue;
            var text = Nodes.LabelText(control);
            if (text.Length > best.Length) best = text;
        }
        return best;
    }

    private static bool InsideButton(Node node, Node stop)
    {
        for (var n = node.GetParent(); n is not null && !ReferenceEquals(n, stop); n = n.GetParent())
            if (n is NEventOptionButton) return true;
        return false;
    }

    // ---------- helpers ----------

    private static NEventRoom? Room(Node? node)
    {
        if (node is NEventRoom given && Nodes.Alive(given)) return given;
        // After an event fight pops back, the router's node can be the room from before the fight.
        if (NRun.Instance?.EventRoom is NEventRoom fromRun && Nodes.Alive(fromRun)) return fromRun;
        return NEventRoom.Instance is NEventRoom current && Nodes.Alive(current) ? current : null;
    }

    private static string Shorten(string s, int max = 60) =>
        s.Length <= max ? s : s[..(max - 3)].TrimEnd() + "...";
}
