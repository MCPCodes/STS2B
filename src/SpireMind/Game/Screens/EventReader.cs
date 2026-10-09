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
/// Actions: "option:&lt;n&gt;" for state.options[n] while it is unlocked, won't kill us and its
/// button is enabled, plus the out-of-combat potion actions. The plain proceed option (IsProceed
/// with TextKey PROCEED) reads "Leave": the room adds it once the event is finished, and it is an
/// Ancient's only option when a modifier skips the Ancient. Other proceed options, like the
/// Trial's DOUBLE_DOWN that opens the abandon-run popup, are never offered. TheArchitect's last
/// option (keyed PROCEED but not IsProceed) marks us ready to end the run and is clicked once
/// per event. An Ancient's options are offered while it is still talking; choosing one clicks
/// through the rest of the dialogue first. A custom-layout event (FakeMerchant) has no option
/// buttons; the bot can only leave it through its own proceed button. A fight the event starts
/// belongs to the combat reader; when it pops back to the event, the room node is fetched again
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

    /// <summary>Leaving opens the map at once (locally, co-op too); past this the event is offered again.</summary>
    private static readonly TimeSpan LeaveHold = TimeSpan.FromSeconds(10);

    /// <summary>Cards from one option's hover tips listed in the state, at most.</summary>
    private const int MaxTipCards = 3;

    /// <summary>Clicks on an Ancient's dialogue hitbox before choosing, at most; dialogues are a few lines.</summary>
    private const int MaxDialogueClicks = 20;

    /// <summary>TextKey of the plain proceed option, and of TheArchitect's last option.</summary>
    private const string ProceedKey = "PROCEED";

    private EventReader() { }

    /// <summary>
    /// The model side of the current page, read fresh on every call. <see cref="Key"/> changes
    /// whenever the page does: the model hands out the same option objects until the page changes.
    /// </summary>
    private sealed record Page(List<EventOption> Options, bool Finished, string Body, string Key);

    /// <summary>One option as read just now. <see cref="Refusal"/> is null when it can be chosen right now.</summary>
    private sealed record OptionView(
        int N, EventOption? Option, NClickableControl? Button, string Title, bool Locked, bool Leave, bool Special, bool Kills, bool EndsRun, string Label, string? Refusal, JsonObject Json)
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

    /// <summary>When we chose Leave on the current page.</summary>
    private static DateTime? _leftAt;

    /// <summary>
    /// The TheArchitect event we clicked the last option of. That click is an act-change vote
    /// with no stale guard, and its page never changes, so it is latched per event, not per page.
    /// </summary>
    private static EventModel? _runEndVoteFor;

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

        // A new room, event or page: whatever we chose before has played out.
        _pageKey = page.Key;
        _pageSince = now;
        _leftAt = null;
        _loggedUnmatched = false;
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
        var leaving = o.Leave;
        // Proceed options run locally even in a shared event (NEventRoom.OptionButtonClicked); only the rest are votes.
        var vote = !leaving && IsVote(ctx, ev);

        // Any of these changing means the game has acted on the choice.
        var pageBefore = view.Page.Key;
        var finishedBefore = view.Page.Finished;
        var roomModel = rs.CurrentRoom;
        var roomCount = rs.CurrentRoomCount;
        var overlayBefore = Nodes.TopOverlay();

        var click = Clicks.Once(button, () =>
        {
            if (view.Ancient && !view.DialogueDone && room.Layout is NAncientEventLayout talker)
                SkipDialogue(talker);
            button.ForceClick();
            // Latched after the click, so a click that throws leaves nothing to wait on.
            if (leaving) _leftAt = DateTime.UtcNow;
            if (o.EndsRun) _runEndVoteFor = ev;
        }, $"the {o.Title} option");
        if (!click.Ok)
            return click;

        // A vote changes nothing here until teammates vote; Capture reports that wait from the game's own vote record.
        var waitsOnTeam = vote;
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

        if (ReferenceEquals(_runEndVoteFor, view.Event))
            return ctx.IsMultiplayer ? "ready to end the run; waiting for teammates" : "ending the run";

        var now = DateTime.UtcNow;
        if (_leftAt is { } left)
        {
            if (now - left < LeaveHold)
                return "leaving the event";
            ModEntry.Log($"still at event {view.Title} {LeaveHold.TotalSeconds:0} s after choosing Leave; offering it again");
            _leftAt = null;
        }

        // The game keeps our vote until the host picks for everyone, however long teammates take;
        // clicking again would only send it again.
        if (VotedFor(ctx, view) is { } voted)
            return $"voted for {voted}; waiting for teammates' votes";

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
        if (options.All(o => o.Locked || o.Special || o.Kills))
            return "no event option the bot can take (all locked, deadly or special); a human has to choose";

        var unmatched = options.Any(o => o.Button is null && !o.Locked && !o.Special && !o.Kills);
        // A non-shared choice clears the buttons at once; the next page's arrive once the option has played out.
        if (unmatched && view.Room.Layout is not null && Buttons(view.Room).Count == 0)
            return "waiting for the event's next options";

        // Unlocked options without a button while other buttons are up point at a layout we can't read.
        if (!_loggedUnmatched && unmatched)
        {
            _loggedUnmatched = true;
            ModEntry.Log($"event {view.Title}: no option button matched some options (layout {view.Room.Layout?.GetType().Name ?? "none"})");
        }
        return "no event option is clickable yet";
    }

    private static Page ReadPage(EventModel ev)
    {
        var options = OptionsOf(ev);
        var finished = ev.IsFinished;
        var body = ModelBody(ev);
        var key = new StringBuilder(finished ? "done|" : "open|");
        foreach (var o in options)
        {
            // New option objects mean a new page even when the wording repeats.
            key.Append(RuntimeHelpers.GetHashCode(o)).Append(':');
            if (o.IsLocked) key.Append("locked:");
            key.Append(Describe.SafeText(() => o.Title)).Append('|');
        }
        key.Append(body);
        return new Page(options, finished, body, key.ToString());
    }

    private static EventView Read(GameContext ctx, NEventRoom room, EventModel ev, Page page)
    {
        var layout = room.Layout;
        var ancient = layout is NAncientEventLayout;
        // An Ancient's layout has no focused control until its dialogue reaches the last line.
        var dialogueDone = layout is not NAncientEventLayout talker || talker.DefaultFocusedControl is not null;

        // A finished event has no options in the model: the room shows one PROCEED option of its own.
        var buttons = Buttons(room);
        var models = page.Finished && page.Options.Count == 0 ? buttons.Select(b => b.Option).ToList() : page.Options;
        var matched = MatchButtons(models, buttons);
        var vote = IsVote(ctx, ev);
        var options = new List<OptionView>();
        for (var i = 0; i < models.Count; i++)
            options.Add(View(i, models[i], matched[i], ev, ancient, !dialogueDone, vote));

        // A custom-layout event (FakeMerchant) draws its own scene without option buttons.
        if (layout is null && room.CustomEventNode is Node custom)
            options.Add(CustomLeave(options.Count, Nodes.Get<NProceedButton>(custom, "%ProceedButton")));

        var body = page.Body.Length > 0 ? page.Body : LayoutText(room);
        var title = Describe.SafeText(() => ev.Title);
        return new EventView(room, ev, page, title, body, ancient, dialogueDone, options);
    }

    private static OptionView View(int n, EventOption option, NEventOptionButton? button, EventModel ev, bool ancient, bool talking, bool vote)
    {
        var title = Describe.SafeText(() => option.Title);
        var text = Describe.SafeText(() => option.Description);
        var locked = option.IsLocked;
        var kills = WillKill(option, ev);
        var key = option.TextKey ?? "";
        // Only the plain PROCEED leaves. The Trial's DOUBLE_DOWN is IsProceed too and opens the abandon-run popup.
        var leave = option.IsProceed && key == ProceedKey;
        var special = option.IsProceed && !leave;
        // TheArchitect's last option is a normal one keyed PROCEED: it marks us ready to end the run.
        var endsRun = ev is TheArchitect && !option.IsProceed && key == ProceedKey;
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
        else if (kills)
        {
            // In co-op the button refuses it anyway (a thought bubble); alone it would end the run.
            note = "would kill you; not offered";
            refusal = $"{name} would kill you.";
        }
        else if (button is null)
        {
            refusal = $"No button found for {name}.";
        }
        // While an Ancient talks its options wait below the dialogue; choosing clicks through it first.
        else if ((!talking && !Nodes.IsShown(button)) || !button.IsEnabled)
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
            if (endsRun) label += " (ends the run)";
        }

        var json = new JsonObject { ["n"] = n, ["title"] = name };
        if (text.Length > 0 && text != name) json["text"] = text;
        if (leave) json["leave"] = true;
        if (locked) json["locked"] = true;
        if (kills) json["will_kill_player"] = true;
        if (extras.RelicJson is { } relicJson) json["relic"] = relicJson.DeepClone();
        if (extras.Cards is { } cards) json["cards"] = cards.DeepClone();
        if (endsRun) note ??= "ends the run; in co-op once every player has chosen it";
        if (note is not null) json["note"] = note;

        return new OptionView(n, option, button, name, locked, leave, special, kills, endsRun, label, refusal, json);
    }

    /// <summary>
    /// Leave for a custom-layout event: the scene's own proceed button, which only opens the map
    /// (NFakeMerchant; disabled while its stock is open). The bot does not drive the rest of it.
    /// </summary>
    private static OptionView CustomLeave(int n, NProceedButton? proceed)
    {
        string? refusal = null;
        if (proceed is null || !Nodes.Alive(proceed))
            refusal = "This event's proceed button was not found.";
        else if (!Nodes.IsShown(proceed) || !proceed.IsEnabled)
            refusal = "Leave is not clickable right now.";
        else if (Clicks.RecentlyClicked(proceed))
            refusal = "Leave was just clicked; waiting for the game to react.";

        var json = new JsonObject
        {
            ["n"] = n,
            ["title"] = "Leave",
            ["leave"] = true,
            ["note"] = "this event draws its own screen; the bot can only leave it",
        };
        return new OptionView(n, null, proceed, "Leave", false, true, false, false, false, "Leave", refusal, json);
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
            // The button draws this relic's icon only for an Ancient.
            if (ancient)
                relic = option.Relic;

            if (option.HoverTips is { } tips)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var tip in tips)
                {
                    if (tip is CardHoverTip { Card: { } card })
                    {
                        if (cards.Count < MaxTipCards && seen.Add(Describe.CardName(card)))
                            cards.Add(Describe.Card(card, PileType.Deck));
                    }
                    // A relic's own tip is a plain HoverTip carrying the canonical relic.
                    else if (relic is null && tip?.CanonicalModel is RelicModel shown)
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

        var options = new JsonArray();
        foreach (var o in view.Options) options.Add(o.Json);
        state["options"] = options;
        if (VotedFor(ctx, view) is { } voted) state["voted"] = voted;

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

    /// <summary>The layout's option buttons, in on-screen order; none for a custom-layout event.</summary>
    private static List<NEventOptionButton> Buttons(NEventRoom room)
    {
        try
        {
            if (room.Layout is { } layout && GodotObject.IsInstanceValid(layout))
                return layout.OptionButtons.Where(Nodes.Alive).ToList();
        }
        catch (Exception)
        {
            // A layout being torn down; read it again next time.
        }
        return new List<NEventOptionButton>();
    }

    /// <summary>
    /// The button for each option, by the option model the button was built for. A button
    /// holding an option that is not on the page belongs to the last page and is skipped. The
    /// button sends its own index, which is the option's index: the layout numbers new buttons
    /// by child count right after clearing the old ones.
    /// </summary>
    private static NEventOptionButton?[] MatchButtons(List<EventOption> options, List<NEventOptionButton> buttons)
    {
        var matched = new NEventOptionButton?[options.Count];
        foreach (var button in buttons)
        {
            var i = options.FindIndex(o => ReferenceEquals(o, button.Option));
            if (i >= 0 && matched[i] is null) matched[i] = button;
        }
        return matched;
    }

    /// <summary>
    /// Clicks through what is left of an Ancient's dialogue, as a human does before picking. The
    /// lines are local to this machine; the hitbox hides and disables itself on the last line.
    /// </summary>
    private static void SkipDialogue(NAncientEventLayout layout)
    {
        var hitbox = Nodes.Get<NAncientDialogueHitbox>(layout, "%DialogueHitbox");
        for (var i = 0; i < MaxDialogueClicks && Nodes.Alive(hitbox) && hitbox.Visible && hitbox.IsEnabled; i++)
            hitbox.ForceClick();
    }

    // ---------- model access ----------

    private static EventModel? LocalEvent()
    {
        try
        {
            // One mutable copy per player, made when the event begins; it survives the event's fights.
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
            return ev.CurrentOptions?.Where(o => o is not null).ToList() ?? new List<EventOption>();
        }
        catch
        {
            return new List<EventOption>();
        }
    }

    /// <summary>
    /// The current page's text from the model (the room fills in its variables when it shows the
    /// page); the layout's labels stand in when it has none.
    /// </summary>
    private static string ModelBody(EventModel ev) =>
        Describe.SafeText(() => ev.Description);

    private static bool WillKill(EventOption option, EventModel ev)
    {
        try
        {
            // WillKillPlayer is an optional predicate on the event's owner (null when the option can't kill).
            return ev.Owner is { } owner && option.WillKillPlayer?.Invoke(owner) == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Title of the option our vote on this shared page stands for; the host clears every vote when it picks.</summary>
    private static string? VotedFor(GameContext ctx, EventView view)
    {
        if (!IsVote(ctx, view.Event) || ctx.Me is not { } me) return null;
        uint? index;
        try
        {
            index = RunManager.Instance?.EventSynchronizer?.GetPlayerVote(me);
        }
        catch
        {
            return null;
        }
        if (index is not { } i) return null;
        return i < view.Options.Count ? view.Options[(int)i].Title : $"option {i}";
    }

    private static bool IsVote(GameContext ctx, EventModel ev) => ctx.IsMultiplayer && ev.IsShared;

    /// <summary>A fight the event started is up, or its room hasn't popped back yet; the combat and rewards readers have it.</summary>
    private static string? EventFight(GameContext ctx, RunState rs)
    {
        if (ctx.InCombat)
            return "fight started by the event";
        // The event's room stays at the base of the room stack while its fight sits on top.
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
