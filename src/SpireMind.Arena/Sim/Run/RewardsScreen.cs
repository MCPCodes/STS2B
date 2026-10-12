using System.Globalization;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// A screen that opened a rewards screen in the middle of its room (an event, a chest, a shop)
/// can hear when that rewards screen has closed: every reward taken, or the rest skipped. The
/// rewards screen tells the screen right under it, if it implements this.
/// </summary>
public interface IHearsRewardsClosed
{
    void AfterRewardsClosed(Run run, RewardsSet rewards);
}

/// <summary>
/// The rewards screen (the game's NRewardsScreen as the mod's RewardsReader shows it): the rows
/// of a <see cref="RewardsSet"/>, the proceed button and the potion actions. Keys, their order,
/// labels and refusals are those of docs/screens.md, "Screen rewards".
/// <para>
/// A terminal screen is the last one of a fight's room: it stays when every row is taken, its
/// proceed button then reads Proceed instead of Skip, and proceed closes it and tells the
/// director that the room is finished (<see cref="Run.RoomFinished"/>); in a boss's room that
/// ends the act (<c>proceed_ends_act</c>). Every other rewards screen (an event's, a relic's,
/// a chest's) closes by itself when its last row is taken, and its proceed button (always
/// Skip) closes it; the screen under it shows again and is told
/// (<see cref="IHearsRewardsClosed"/>).
/// </para>
/// <para>
/// A row is numbered by its place among the rows still there: after reward 0 was taken the
/// next row is 0. A card reward opens the card reward screen as a question of the
/// <c>reward:&lt;n&gt;</c> action (<see cref="CardRewardScreen"/>).
/// </para>
/// </summary>
public sealed class RewardsScreen : RunScreen, IHearsRelicObtained
{
    public RewardsScreen(RewardsSet set, bool isTerminal)
    {
        Set = set;
        IsTerminal = isTerminal;
    }

    public RewardsSet Set { get; }

    /// <summary>The last screen of a fight's room: its proceed leaves the room.</summary>
    public bool IsTerminal { get; }

    public override string Name => "rewards";

    /// <summary>The rows still on the screen, in the screen's order.</summary>
    public IReadOnlyList<Reward> Rows => Set.Rewards.Where(reward => !reward.SuccessfullySelected).ToList();

    /// <summary>The proceed button reads Skip: always, except on a terminal screen with no row left.</summary>
    public bool ProceedIsSkip => !(IsTerminal && Rows.Count == 0);

    /// <summary>Proceed ends the act: the terminal screen of a boss's room (the game's test in NRewardsScreen.OnProceedButtonPressed;
    /// a first boss of two, which leads back to the map, needs an ascension level the arena does not play).</summary>
    public bool ProceedEndsAct => IsTerminal && Set.Room == RoomKind.Boss;

    /// <summary>The proceed button can be pressed: not while skipping is disallowed and rows are left, and not while a hook holds it back.</summary>
    public bool CanProceed(Run run) => (!Set.DisallowSkipping || !ProceedIsSkip) && RewardHooks.ShouldProceedToNextMapPoint(run);

    // ---------- the snapshot ----------

    public override GameSnapshot Snapshot(Run run)
    {
        var rows = Rows;
        var list = new JsonArray();
        var actions = new List<LegalAction>();
        var slotsFull = false;
        for (var n = 0; n < rows.Count; n++)
        {
            var reward = rows[n];
            var row = new JsonObject { ["n"] = n, ["kind"] = reward.Kind };
            reward.Describe(row);
            var note = reward.Note(run);
            if (note is not null)
            {
                row["available"] = false;
                row["note"] = note;
                slotsFull |= note == RunPotions.SlotsFullNote;
            }
            else
            {
                actions.Add(new LegalAction(Id(n), reward.Label));
            }
            list.Add(row);
        }

        var state = new JsonObject { ["run"] = run.RunJson() };
        MapState.AddBoss(state, run);
        state["player"] = run.PlayerJson(PlayerKeys.NoPotions);
        state["potions"] = RunPotions.Describe(run);
        state["rewards"] = list;
        if (ProceedEndsAct) state["proceed_ends_act"] = true;

        if (CanProceed(run)) actions.Add(new LegalAction("proceed", ProceedLabel()));

        // Potion actions ride along with the screen's own; alone they are a decision only when they can make room for a potion reward.
        var potions = RunPotions.Legal(run);
        if (actions.Count == 0 && !(potions.Count > 0 && slotsFull))
        {
            return new GameSnapshot
            {
                Screen = Name, AwaitingDecision = false, State = state,
                Waiting = rows.Count > 0 ? "no reward is clickable yet" : "waiting for the proceed button",
            };
        }
        actions.AddRange(potions);
        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    private static string Id(int n) => string.Create(CultureInfo.InvariantCulture, $"reward:{n}");

    private string ProceedLabel()
    {
        var skip = ProceedIsSkip;
        if (ProceedEndsAct)
            return skip ? "Go on to the next act (button reads Skip: unclaimed rewards are left behind)" : "Go on to the next act";
        return skip ? "Skip (leave without the unclaimed rewards)" : "Proceed";
    }

    // ---------- the actions ----------

    public override ActionResult Execute(Run run, string actionId)
    {
        if (RunPotions.TryExecute(run, actionId, out var potionResult)) return potionResult;
        if (actionId == "proceed") return Proceed(run);
        if (actionId.StartsWith("reward:", StringComparison.Ordinal) && int.TryParse(actionId.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            return Claim(run, n);
        return ActionResult.Fail($"Unknown rewards action '{actionId}'.");
    }

    private ActionResult Claim(Run run, int n)
    {
        var rows = Rows;
        if (n < 0 || n >= rows.Count) return ActionResult.Fail(string.Create(CultureInfo.InvariantCulture, $"There is no reward {n}."));
        var reward = rows[n];
        if (reward.Note(run) is { } note) return ActionResult.Fail($"Cannot take {reward.What}: {note}.");

        // False: the reward was not taken after all (a skipped card reward, a removal backed out of, a potion that bounced). Its row stays.
        if (!reward.Select(run)) return ActionResult.Success;

        // The last row of a screen that is not a room's last one closes it.
        if (!IsTerminal && Rows.Count == 0) Close(run);
        return ActionResult.Success;
    }

    private ActionResult Proceed(Run run)
    {
        // The button is always there; it is switched off while every reward must be taken, or while a hook holds it.
        if (!CanProceed(run)) return ActionResult.Fail("The proceed button is not enabled yet.");

        // Whatever is left is skipped, as the game does when the room or the screen is left.
        foreach (var reward in Rows) reward.OnSkipped(run);
        if (!IsTerminal)
        {
            Close(run);
            return ActionResult.Success;
        }
        TakeOffTheStack(run);
        run.RoomFinished();
        return ActionResult.Success;
    }

    /// <summary>Closes a screen that is not a room's last one and tells the screen under it.</summary>
    private void Close(Run run)
    {
        var under = TakeOffTheStack(run);
        if (under is IHearsRewardsClosed parent) parent.AfterRewardsClosed(run, Set);
    }

    /// <summary>
    /// Takes this screen off the run's screens, also when another screen was put over it in the
    /// meantime (a relic taken from a row whose pickup opened a screen of its own): those stay,
    /// in their order. Returns the screen right under this one, or null.
    /// </summary>
    private RunScreen? TakeOffTheStack(Run run)
    {
        var above = new Stack<RunScreen>();
        while (!ReferenceEquals(run.Top, this))
        {
            if (run.Top is null) throw new InvalidOperationException("The rewards screen is gone.");
            above.Push(run.Pop());
        }
        run.Pop();
        var under = run.Top;
        while (above.Count > 0) run.Push(above.Pop());
        return under;
    }

    // ---------- relics obtained while card rewards wait ----------

    /// <summary>
    /// The player obtained a relic: every card reward of this screen that still waits is asked
    /// whether the relic changes its cards, in the order the rewards were made.
    /// </summary>
    public void OnRelicObtained(Run run, Relic relic)
    {
        foreach (var reward in Set.Rewards.OfType<CardReward>().OrderBy(reward => reward.Sequence).ToList())
            reward.OnRelicObtained(run, relic);
    }

    public override RunScreen Clone(CloneMap map) => new RewardsScreen(Set.Clone(), IsTerminal);
}

/// <summary>
/// The card reward screen (the game's NCardRewardSelectionScreen as the mod's CardRewardReader
/// shows it): the offered cards and the Skip button; no potions here. It is a question of the
/// action that opened the card reward (<see cref="CardReward"/>): <c>card:&lt;n&gt;</c> answers
/// with the card, <c>skip</c> with nothing, and the action then runs again with the answer.
/// Keys, order and labels are those of docs/screens.md, "Screen card_reward".
/// </summary>
public sealed class CardRewardScreen : RunScreen
{
    private readonly List<CardInstance> _cards;

    /// <param name="cards">The cards on offer, in row order. The screen only shows them.</param>
    /// <param name="canSkip">The Skip button is there.</param>
    public CardRewardScreen(IEnumerable<CardInstance> cards, bool canSkip)
    {
        _cards = cards.ToList();
        CanSkip = canSkip;
    }

    public IReadOnlyList<CardInstance> Cards => _cards;

    public bool CanSkip { get; }

    public override string Name => "card_reward";

    public override GameSnapshot Snapshot(Run run)
    {
        var cards = new JsonArray();
        var actions = new List<LegalAction>();
        for (var n = 0; n < _cards.Count; n++)
        {
            var entry = new JsonObject { ["n"] = n };
            foreach (var (key, value) in RunDescribe.CardOffer(_cards[n])) entry[key] = value?.DeepClone();
            cards.Add(entry);
            actions.Add(new LegalAction(string.Create(CultureInfo.InvariantCulture, $"card:{n}"), $"Take {_cards[n].Title}"));
        }
        if (CanSkip) actions.Add(new LegalAction("skip", "Skip this card reward"));

        var state = new JsonObject { ["run"] = run.RunJson() };
        MapState.AddBoss(state, run);
        state["player"] = run.PlayerJson();
        state["cards"] = cards;
        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    public override ActionResult Execute(Run run, string actionId)
    {
        if (actionId.StartsWith("card:", StringComparison.Ordinal) && int.TryParse(actionId.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            if (n < 0 || n >= _cards.Count) return ActionResult.Fail(string.Create(CultureInfo.InvariantCulture, $"There is no card {n}."));
            return run.Answer([n]);
        }
        if (actionId == "skip" && CanSkip) return run.Answer(null);
        return ActionResult.Fail($"Unknown card reward action '{actionId}'.");
    }

    public override RunScreen Clone(CloneMap map) => new CardRewardScreen(_cards.Select(CardCreationResult.CopyOf), CanSkip);
}
