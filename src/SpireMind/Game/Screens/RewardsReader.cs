using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

// Same namespace as ScreenRouter, which refers to the readers unqualified.
namespace SpireMind.Game;

/// <summary>
/// The rewards list after a fight (or an event, chest or shop relic): gold, potions, relics,
/// card rewards and card removals, plus the proceed button. Claims are clicks on the reward
/// buttons, which the game routes through its own reward synchronizers (docs/game-api.md
/// section e), and only on the topmost rewards screen. After the act's last boss the proceed
/// button is the act-change vote, which the game doesn't guard against repeats, so it is
/// latched per act in <see cref="Latches.ActVoteCastFor"/>.
///
/// Actions: "reward:&lt;n&gt;" for state.rewards[n], "proceed", and the out-of-combat potion
/// actions so a full potion belt can make room. A card reward's cards are listed only after
/// we have opened it on this screen; before that a human sees just the button.
/// </summary>
internal sealed class RewardsReader : IScreen
{
    public static readonly RewardsReader Instance = new();

    public string Name => "rewards";

    /// <summary>Reward buttons fade in; don't offer a "Skip" proceed before the list could be read as clickable.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(500);

    /// <summary>How long a disabled reward button counts as "still appearing" after the screen opens.</summary>
    private static readonly TimeSpan AppearGrace = TimeSpan.FromSeconds(3);

    /// <summary>The same, after one of our claims (the list re-lays out, co-op claims round-trip).</summary>
    private static readonly TimeSpan ClaimGrace = TimeSpan.FromSeconds(2);

    /// <summary>A potion claim that never disables its button and is still enabled after this long has bounced.</summary>
    private static readonly TimeSpan PotionBounceWait = TimeSpan.FromMilliseconds(1200);

    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LeaveTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ActVoteTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Give a co-op act vote time to go out before "idle" can mean "registered".</summary>
    private static readonly TimeSpan VoteSettle = TimeSpan.FromMilliseconds(500);

    private const string SlotsFull = "potion slots full: use or discard a potion to take it";

    private RewardsReader() { }

    private enum Kind { Gold, Potion, Relic, Card, SpecialCard, CardRemoval, Other }

    private sealed record RewardView(
        NRewardButton Button, int N, Kind Kind, string Label, string What, JsonObject Json, bool Enabled, bool Offered, string? Note);

    // ---------- per-screen memory (reset when a different rewards screen shows up) ----------

    private static ulong _screenId;
    private static DateTime _firstSeen;
    private static DateTime _lastClaim;

    /// <summary>Reward buttons we clicked on this screen, with the number of open potion slots at the time.</summary>
    private static readonly Dictionary<ulong, int> Tried = new();

    /// <summary>
    /// Set once we press proceed on a boss room's rewards. That press may be the act vote even
    /// when <see cref="LeavesAct"/> can't tell (two-boss rows), and a second vote skips an act.
    /// </summary>
    private static bool _bossProceedPressed;

    private static void Track(NRewardsScreen screen)
    {
        var id = screen.GetInstanceId();
        if (id == _screenId) return;
        _screenId = id;
        _firstSeen = DateTime.UtcNow;
        _lastClaim = DateTime.MinValue;
        _bossProceedPressed = false;
        Tried.Clear();
    }

    // ---------- IScreen ----------

    public GameSnapshot Capture(GameContext ctx, Node? node)
    {
        if (node is not NRewardsScreen screen || !GodotObject.IsInstanceValid(screen))
            return GameSnapshot.Busy(Name, "rewards closing");
        if (ctx.Run is not { } rs || ctx.Me is not { } me)
            return GameSnapshot.Busy(Name, "run loading");
        if (!Nodes.IsTopOverlay(screen.GetInstanceId()))
            return GameSnapshot.Busy(Name, "another screen is over the rewards");

        Track(screen);
        var rewards = ReadRewards(screen, me);
        var leavesAct = LeavesAct(rs);
        var state = BuildState(ctx, rs, me, rewards, leavesAct);

        var waiting = WaitingReason(ctx, rs, rewards, leavesAct);
        var actions = new List<LegalAction>();
        if (waiting is null)
        {
            foreach (var r in rewards)
                if (r.Offered) actions.Add(new LegalAction($"reward:{r.N}", r.Label));

            var proceed = ProceedButton(screen);
            if (CanClick(proceed))
                actions.Add(new LegalAction("proceed", ProceedLabel(ctx, proceed, leavesAct)));

            var potions = PotionActions.Legal(ctx, false).ToList();
            // Potion actions alone are only a decision when they can unblock a potion reward.
            if (actions.Count == 0 && !(potions.Count > 0 && rewards.Any(r => r.Note == SlotsFull)))
                waiting = rewards.Count > 0 ? "no reward is clickable yet" : "waiting for the proceed button";
            else
                actions.AddRange(potions);
        }

        if (waiting is not null)
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = waiting, State = state };

        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    public ActionResult Execute(GameContext ctx, Node? node, string actionId)
    {
        if (PotionActions.TryExecute(ctx, actionId, false, out var potionResult))
            return potionResult;

        if (node is not NRewardsScreen screen || !GodotObject.IsInstanceValid(screen))
            return ActionResult.Fail("The rewards screen is gone.");
        if (ctx.Run is not { } rs || ctx.Me is not { } me)
            return ActionResult.Fail("No run in progress.");
        if (!Nodes.IsTopOverlay(screen.GetInstanceId()))
            return ActionResult.Fail("Another screen is on top of the rewards.");

        Track(screen);

        if (actionId == "proceed")
            return Proceed(ctx, rs, screen);

        if (actionId.StartsWith("reward:", StringComparison.Ordinal) && int.TryParse(actionId.AsSpan(7), out var n))
            return Claim(screen, me, n);

        return ActionResult.Fail($"Unknown rewards action '{actionId}'.");
    }

    // ---------- claiming ----------

    private static ActionResult Claim(NRewardsScreen screen, Player me, int n)
    {
        var rewards = ReadRewards(screen, me);
        if (n < 0 || n >= rewards.Count)
            return ActionResult.Fail($"There is no reward {n}.");

        var r = rewards[n];
        var button = r.Button;
        if (!button.IsEnabled)
            return ActionResult.Fail($"{Capitalize(r.What)} is not clickable right now.");
        if (!r.Offered)
            return ActionResult.Fail(r.Note is null ? $"{Capitalize(r.What)} is not available right now." : $"Cannot take {r.What}: {r.Note}.");

        var open = OpenSlots(me);
        if (r.Kind == Kind.Potion && open == 0)
            return ActionResult.Fail("Potion slots are full; use or discard a potion first.");

        var id = button.GetInstanceId();
        var click = Clicks.Once(button, () =>
        {
            Tried[id] = open;
            _lastClaim = DateTime.UtcNow;
            button.ForceClick();
        }, r.What);
        if (!click.Ok)
            return click;

        var screenId = screen.GetInstanceId();
        if (r.Kind == Kind.Potion)
        {
            // Taken: the button goes away. Bounced (slots full after all): the button disables
            // while the game tries, then re-enables; Capture then reports the slots as full.
            var sawDisabled = false;
            var since = DateTime.UtcNow;
            ActionTracker.Begin(r.What, _ =>
            {
                if (!Nodes.IsTopOverlay(screenId) || Nodes.Gone(button)) return true;
                if (!button.IsEnabled)
                {
                    sawDisabled = true;
                    return false;
                }
                return sawDisabled || DateTime.UtcNow - since > PotionBounceWait;
            }, ClaimTimeout);
        }
        else
        {
            // Gold and relics remove their button; card rewards and removals push a selection
            // screen on top of this one.
            ActionTracker.Begin(r.What, _ => !Nodes.IsTopOverlay(screenId) || Nodes.Gone(button), ClaimTimeout);
        }
        return ActionResult.Success;
    }

    private static ActionResult Proceed(GameContext ctx, RunState rs, NRewardsScreen screen)
    {
        var proceed = ProceedButton(screen);
        if (proceed is null || !Nodes.IsShown(proceed))
            return ActionResult.Fail("There is no proceed button.");
        if (!proceed.IsEnabled)
            return ActionResult.Fail("The proceed button is not enabled yet.");

        var act = rs.CurrentActIndex;
        var leavesAct = LeavesAct(rs);
        if (leavesAct && Latches.ActVoteCastFor == act)
            return ActionResult.Fail("Already voted to go on to the next act.");
        var bossRoom = rs.CurrentRoom?.RoomType == RoomType.Boss;
        if (bossRoom && _bossProceedPressed)
            return ActionResult.Fail("Proceed was already pressed on these boss rewards.");

        var click = Clicks.Once(proceed, () =>
        {
            // Latched before the click so nothing can send a second vote for this act.
            if (leavesAct) Latches.ActVoteCastFor = act;
            if (bossRoom) _bossProceedPressed = true;
            proceed.ForceClick();
        }, "proceed button");
        if (!click.Ok)
            return click;

        var screenId = screen.GetInstanceId();
        if (leavesAct)
        {
            // Singleplayer moves on straight away; co-op waits for every player's vote, which
            // Capture then reports from the latch.
            var multiplayer = ctx.IsMultiplayer;
            ActionTracker.Begin(
                "the vote to go on to the next act",
                c => c.Run is not { } run || run.CurrentActIndex != act || !Nodes.IsTopOverlay(screenId) || Nodes.ModalOpen()
                    || (multiplayer && ActionTracker.GameLooksIdle()),
                ActVoteTimeout,
                minWait: multiplayer ? VoteSettle : null);
        }
        else
        {
            // Terminal rewards stay on the stack under the map, so the map opening counts as left.
            ActionTracker.Begin(
                "leaving the rewards",
                _ => !Nodes.IsTopOverlay(screenId) || NMapScreen.Instance?.IsOpen == true || Nodes.ModalOpen(),
                LeaveTimeout);
        }
        return ActionResult.Success;
    }

    // ---------- reading ----------

    private static string? WaitingReason(GameContext ctx, RunState rs, List<RewardView> rewards, bool leavesAct)
    {
        if (leavesAct && Latches.ActVoteCastFor == rs.CurrentActIndex)
            return ctx.IsMultiplayer ? "voted to go on to the next act; waiting for teammates" : "starting the next act";
        if (_bossProceedPressed && rs.CurrentRoom?.RoomType == RoomType.Boss)
            return ctx.IsMultiplayer ? "pressed proceed on the boss rewards; waiting for teammates" : "leaving the boss rewards";

        var now = DateTime.UtcNow;
        if (now - _firstSeen < Settle)
            return "rewards appearing";

        // A disabled button right after opening or after a claim is usually mid-animation;
        // offering proceed then could skip a reward that is about to become clickable.
        if (rewards.Any(r => !r.Enabled) && (now - _firstSeen < AppearGrace || now - _lastClaim < ClaimGrace))
            return "rewards appearing";

        // Same for one we just clicked that is enabled again (back from a skipped card reward):
        // it is held back by the click window, so proceed would be the only choice shown.
        if (rewards.Any(r => r.Enabled && r.Note is null && Clicks.RecentlyClicked(r.Button)))
            return "rewards updating";

        return null;
    }

    private static List<RewardView> ReadRewards(NRewardsScreen screen, Player me)
    {
        var views = new List<RewardView>();
        var open = OpenSlots(me);
        foreach (var button in Nodes.FindAll<NRewardButton>(screen))
        {
            if (Nodes.Gone(button)) continue;
            var enabled = button.IsEnabled;
            // One we clicked that is now disabled is being claimed or playing its exit animation.
            if (Tried.ContainsKey(button.GetInstanceId()) && !enabled) continue;
            views.Add(View(button, views.Count, enabled, open));
        }
        return views;
    }

    private static RewardView View(NRewardButton button, int n, bool enabled, int openSlots)
    {
        var id = button.GetInstanceId();
        var json = new JsonObject { ["n"] = n };
        Kind kind;
        string label;
        string what;
        string? note = null;

        object? reward = button.Reward;
        switch (reward)
        {
            case GoldReward gold:
                kind = Kind.Gold;
                json["kind"] = "gold";
                json["amount"] = gold.Amount; // VERIFY: GoldReward.Amount is an int (or another JSON-convertible number)
                label = $"Take {gold.Amount} gold";
                what = $"the {gold.Amount} gold reward";
                break;

            case PotionReward potionReward:
            {
                kind = Kind.Potion;
                json["kind"] = "potion";
                var potion = potionReward.Potion;
                var name = potion is null ? "potion" : Describe.Text(potion.Title);
                if (potion is not null) Merge(json, Describe.Potion(potion));
                label = $"Take potion {name}";
                what = $"the {name} reward";
                // With no free slot the claim just bounces. After a bounce, wait for a slot to
                // free up rather than clicking the same refusal again.
                if (openSlots == 0 || (Tried.TryGetValue(id, out var openWhenTried) && openSlots <= openWhenTried))
                    note = SlotsFull;
                break;
            }

            case RelicReward relicReward:
            {
                kind = Kind.Relic;
                json["kind"] = "relic";
                var relic = relicReward.Relic;
                var name = relic is null ? "relic" : Describe.Text(relic.Title);
                if (relic is not null) Merge(json, Describe.Relic(relic));
                label = $"Take relic {name}";
                what = $"the {name} relic";
                break;
            }

            // Ahead of CardReward in case it derives from it.
            case SpecialCardReward special:
            {
                kind = Kind.SpecialCard;
                json["kind"] = "special_card";
                if (Nodes.TryGetPrivate(special, "Card") is CardModel card) // VERIFY: SpecialCardReward exposes its card as a member named Card
                {
                    var name = Describe.CardName(card);
                    Merge(json, Describe.CardOffer(card));
                    label = $"Take card {name}";
                    what = $"the {name} card";
                }
                else
                {
                    var text = Nodes.FirstLabelText(button);
                    if (text.Length > 0) json["text"] = text;
                    label = text.Length > 0 ? $"Take {text}" : "Take the card";
                    what = "the card reward";
                }
                break;
            }

            case CardRemovalReward _:
                kind = Kind.CardRemoval;
                json["kind"] = "card_removal";
                label = "Remove a card";
                what = "the card removal reward";
                break;

            case CardReward cardReward:
            {
                kind = Kind.Card;
                json["kind"] = "card";
                var cards = CardsOf(cardReward);
                if (cards.Count > 0) json["count"] = cards.Count;
                if (Tried.ContainsKey(id) && cards.Count > 0)
                {
                    var list = new JsonArray();
                    foreach (var card in cards) list.Add(Describe.CardOffer(card));
                    json["cards"] = list;
                }
                label = cards.Count > 0 ? $"Open card reward ({cards.Count} cards)" : "Open card reward";
                what = "the card reward";
                break;
            }

            default:
            {
                kind = Kind.Other;
                var typeName = reward?.GetType().Name ?? "unknown";
                json["kind"] = typeName;
                // What the button itself says is what a human would read.
                var text = Nodes.FirstLabelText(button);
                if (text.Length == 0 && reward is not null)
                    text = Describe.Text(Nodes.TryGetPrivate(reward, "Description") ?? Nodes.TryGetPrivate(reward, "Title")); // VERIFY: reward base type has a Description or Title (LocString) for rewards we don't special-case
                if (text.Length > 0) json["text"] = text;
                label = text.Length > 0 ? $"Take {text}" : $"Take the {typeName} reward";
                what = $"the {typeName} reward";
                break;
            }
        }

        if (!enabled || note is not null) json["available"] = false;
        if (note is not null) json["note"] = note;

        var offered = enabled && note is null && !Clicks.RecentlyClicked(button);
        return new RewardView(button, n, kind, label, what, json, enabled, offered, note);
    }

    private static JsonObject BuildState(GameContext ctx, RunState rs, Player me, List<RewardView> rewards, bool leavesAct)
    {
        var player = Describe.PlayerRun(me, includeDeck: true);
        player.Remove("potions"); // replaced by the detailed slots below

        var list = new JsonArray();
        foreach (var r in rewards) list.Add(r.Json);

        var state = new JsonObject
        {
            ["run"] = Describe.RunInfo(rs),
            ["player"] = player,
            ["potions"] = PotionActions.Describe(ctx, false),
            ["rewards"] = list,
        };
        if (leavesAct) state["proceed_ends_act"] = true;
        return state;
    }

    private static string ProceedLabel(GameContext ctx, NProceedButton proceed, bool leavesAct)
    {
        var skip = proceed.IsSkip;
        if (leavesAct)
        {
            var verb = ctx.IsMultiplayer ? "Vote to go on to the next act" : "Go on to the next act";
            return skip ? $"{verb} (button reads Skip: unclaimed rewards are left behind)" : verb;
        }
        return skip ? "Skip (leave without the unclaimed rewards)" : "Proceed";
    }

    /// <summary>
    /// True when proceeding leaves the act: the rewards of the act's last boss. With a second
    /// boss, the first one's rewards lead back to the map instead.
    /// </summary>
    private static bool LeavesAct(RunState rs)
    {
        if (rs.CurrentRoom?.RoomType != RoomType.Boss) return false;
        if (rs.Map?.SecondBossMapPoint is not { } second) return true;
        MapCoord? at = rs.CurrentMapCoord;
        return at is { } c && c.row == second.coord.row; // VERIFY: MapCoord exposes lowercase row (same assumption as MapReader.Key)
    }

    private static NProceedButton? ProceedButton(NRewardsScreen screen) =>
        Nodes.Get<NProceedButton>(screen, "ProceedButton");

    private static bool CanClick([NotNullWhen(true)] NProceedButton? button) =>
        button is not null && Nodes.IsShown(button) && button.IsEnabled && !Clicks.RecentlyClicked(button);

    // ---------- helpers ----------

    private static int OpenSlots(Player me)
    {
        var open = 0;
        for (var i = 0; i < me.MaxPotionCount; i++)
            if (me.GetPotionAtSlotIndex(i) is null) open++;
        return open;
    }

    private static List<CardModel> CardsOf(CardReward reward)
    {
        var list = new List<CardModel>();
        if (reward.Cards is not { } cards) return list;
        foreach (var item in cards)
        {
            // VERIFY: CardReward.Cards elements are CardModel; a creation-result wrapper with .Card is handled too
            object? entry = item;
            if ((entry as CardModel ?? (entry is null ? null : Nodes.TryGetPrivate(entry, "Card") as CardModel)) is { } card)
                list.Add(card);
        }
        return list;
    }

    private static void Merge(JsonObject into, JsonObject from)
    {
        foreach (var (key, value) in from)
            into[key] = value?.DeepClone();
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
