using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

// Same namespace as ScreenRouter, which refers to the readers unqualified.
namespace SpireMind.Game;

/// <summary>
/// Picking one card from a card reward (NCardRewardSelectionScreen). The offered cards are
/// the NGridCardHolders in "UI/CardRow", taken by emitting Pressed once; Skip, Reroll and
/// Sacrifice are the NCardRewardAlternativeButtons in "UI/RewardAlternatives", clicked with
/// ForceClick (docs/game-api.md section e). Card rewards are personal, so co-op needs no
/// vote here. Actions: "card:&lt;n&gt;" for state.cards[n], plus "skip", "reroll" and
/// "sacrifice" when the game shows that button enabled.
/// </summary>
internal sealed class CardRewardReader : IScreen
{
    public static readonly CardRewardReader Instance = new();

    public string Name => "card_reward";

    /// <summary>Cards slide in on open and after a reroll; the row must have been stable this long.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(350);

    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan RerollTimeout = TimeSpan.FromSeconds(6);

    /// <summary>
    /// A pick or skip completes the screen's TaskCompletionSource, so a second press throws.
    /// Hold back after choosing; if the screen is somehow still up after this long, show it
    /// again rather than wait forever.
    /// </summary>
    private static readonly TimeSpan ChoiceHold = TimeSpan.FromSeconds(8);

    /// <summary>Child order of the alternative buttons, used when neither node name nor label identifies one.</summary>
    private static readonly string[] AlternativeOrder = ["skip", "reroll", "sacrifice"];

    private sealed record Option(NGridCardHolder Holder, CardModel Card);

    private sealed record Alternative(NCardRewardAlternativeButton Button, string Id, string Text);

    // ---------- per-screen memory (reset when a different screen instance shows up) ----------

    private static ulong _screenId;
    private static string _cardsKey = "";
    private static DateTime _cardsSince;
    private static DateTime? _choseAt;
    private static bool _sacrificed;

    private CardRewardReader() { }

    private static void Track(NCardRewardSelectionScreen screen, List<Option> options)
    {
        var id = screen.GetInstanceId();
        var key = CardsKey(options);
        var now = DateTime.UtcNow;
        if (id != _screenId)
        {
            _screenId = id;
            _choseAt = null;
            _sacrificed = false;
            _cardsKey = key;
            _cardsSince = now;
        }
        else if (key != _cardsKey)
        {
            _cardsKey = key;
            _cardsSince = now;
        }
    }

    // ---------- IScreen ----------

    public GameSnapshot Capture(GameContext ctx, Node? node)
    {
        if (node is not NCardRewardSelectionScreen screen || !GodotObject.IsInstanceValid(screen))
            return GameSnapshot.Busy(Name, "card reward closing");
        if (ctx.Run is not { } rs || ctx.Me is not { } me)
            return GameSnapshot.Busy(Name, "run loading");
        if (!Nodes.IsTopOverlay(screen.GetInstanceId()))
            return GameSnapshot.Busy(Name, "another screen is over the card reward");

        var options = ReadOptions(screen);
        var alternatives = ReadAlternatives(screen);
        Track(screen, options);
        var state = BuildState(rs, me, options);

        var waiting = WaitingReason();
        var actions = new List<LegalAction>();
        if (waiting is null)
        {
            for (var i = 0; i < options.Count; i++)
                actions.Add(new LegalAction($"card:{i}", $"Take {Describe.CardName(options[i].Card)}"));
            foreach (var alt in alternatives)
                if (Clickable(alt)) actions.Add(new LegalAction(alt.Id, AlternativeLabel(alt)));
            if (actions.Count == 0)
                waiting = "cards appearing";
        }

        if (waiting is not null)
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = waiting, State = state };

        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    public ActionResult Execute(GameContext ctx, Node? node, string actionId)
    {
        if (node is not NCardRewardSelectionScreen screen || !GodotObject.IsInstanceValid(screen))
            return ActionResult.Fail("The card reward screen is gone.");
        var screenId = screen.GetInstanceId();
        if (!Nodes.IsTopOverlay(screenId))
            return ActionResult.Fail("Another screen is on top of the card reward.");

        // Re-read the row: a reroll replaces the holders.
        var options = ReadOptions(screen);
        Track(screen, options);
        if (WaitingReason() is { } waiting)
            return ActionResult.Fail($"Not ready: {waiting}.");

        if (actionId.StartsWith("card:", StringComparison.Ordinal) && int.TryParse(actionId.AsSpan(5), out var n))
            return Pick(screenId, options, n);

        var alt = ReadAlternatives(screen).FirstOrDefault(a => a.Id == actionId);
        if (alt is null)
            return ActionResult.Fail($"Unknown card reward action '{actionId}'.");
        return UseAlternative(screen, options, alt);
    }

    // ---------- acting ----------

    private static ActionResult Pick(ulong screenId, List<Option> options, int n)
    {
        if (n < 0 || n >= options.Count)
            return ActionResult.Fail($"There is no card {n}.");

        var (holder, card) = options[n];
        var name = Describe.CardName(card);
        var click = Clicks.Once(holder, () =>
        {
            _choseAt = DateTime.UtcNow;
            holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
        }, $"card {name}");
        if (!click.Ok)
            return click;

        ActionTracker.Begin($"taking {name}", _ => !Nodes.IsTopOverlay(screenId), CloseTimeout);
        return ActionResult.Success;
    }

    private static ActionResult UseAlternative(NCardRewardSelectionScreen screen, List<Option> options, Alternative alt)
    {
        var button = alt.Button;
        if (!Nodes.IsShown(button) || !button.IsEnabled) // VERIFY: NCardRewardAlternativeButton has IsEnabled (NButton-style, like the other clickables)
            return ActionResult.Fail($"The {alt.Id} button is not available right now.");
        if (alt.Id == "sacrifice" && _sacrificed)
            return ActionResult.Fail("Already sacrificed on this card reward.");

        var screenId = screen.GetInstanceId();
        var before = CardsKey(options);
        var click = Clicks.Once(button, () =>
        {
            if (alt.Id == "skip") _choseAt = DateTime.UtcNow;
            else if (alt.Id == "sacrifice") _sacrificed = true;
            button.ForceClick();
        }, $"{alt.Id} button");
        if (!click.Ok)
            return click;

        if (alt.Id == "skip")
        {
            ActionTracker.Begin("skipping the card reward", _ => !Nodes.IsTopOverlay(screenId), CloseTimeout);
        }
        else
        {
            // Reroll swaps the cards in place. What Sacrifice leaves behind isn't documented,
            // so accept either outcome for it too.
            ActionTracker.Begin(
                alt.Id == "reroll" ? "rerolling the cards" : "sacrificing the card reward",
                _ => !Nodes.IsTopOverlay(screenId) || CardsChanged(screen, before),
                RerollTimeout);
        }
        return ActionResult.Success;
    }

    // ---------- reading ----------

    private static string? WaitingReason()
    {
        var now = DateTime.UtcNow;
        if (_choseAt is { } at && now - at < ChoiceHold)
            return "card reward closing";
        if (now - _cardsSince < Settle)
            return "cards appearing";
        return null;
    }

    private static List<Option> ReadOptions(NCardRewardSelectionScreen screen)
    {
        var options = new List<Option>();
        foreach (var holder in Nodes.FindAll<NGridCardHolder>(Nodes.Get<Node>(screen, "UI/CardRow")))
        {
            if (Nodes.Gone(holder)) continue;
            if (holder.CardModel is { } card) // VERIFY: NGridCardHolder exposes CardModel (inherited from NCardHolder, as the hand holders do)
                options.Add(new Option(holder, card));
        }
        return options;
    }

    private static List<Alternative> ReadAlternatives(NCardRewardSelectionScreen screen)
    {
        var buttons = Nodes.FindAll<NCardRewardAlternativeButton>(Nodes.Get<Node>(screen, "UI/RewardAlternatives"))
            .Where(b => GodotObject.IsInstanceValid(b) && b.IsInsideTree() && !b.IsQueuedForDeletion())
            .ToList();

        var texts = buttons.Select(b => Nodes.FirstLabelText(b)).ToList();
        var ids = new string?[buttons.Count];
        for (var i = 0; i < buttons.Count; i++)
            ids[i] = IdFrom(buttons[i].Name.ToString()) ?? IdFrom(texts[i]);

        // The documented child order is Skip, Reroll, Sacrifice. Positions are only trusted
        // when all three are there; otherwise just the first slot is taken to be Skip.
        for (var i = 0; i < buttons.Count; i++)
        {
            if (ids[i] is not null) continue;
            var guess = buttons.Count == AlternativeOrder.Length ? AlternativeOrder[i] : i == 0 ? "skip" : null;
            if (guess is not null && !ids.Contains(guess)) ids[i] = guess;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<Alternative>();
        for (var i = 0; i < buttons.Count; i++)
        {
            if (ids[i] is not { } id || !seen.Add(id)) continue;
            list.Add(new Alternative(buttons[i], id, texts[i]));
        }
        return list;
    }

    private static bool Clickable(Alternative alt) =>
        Nodes.IsShown(alt.Button)
        && alt.Button.IsEnabled
        && !Clicks.RecentlyClicked(alt.Button)
        && !(alt.Id == "sacrifice" && _sacrificed);

    private static JsonObject BuildState(RunState rs, Player me, List<Option> options)
    {
        var cards = new JsonArray();
        for (var i = 0; i < options.Count; i++)
        {
            var json = new JsonObject { ["n"] = i };
            foreach (var (key, value) in Describe.CardOffer(options[i].Card))
                json[key] = value?.DeepClone();
            cards.Add(json);
        }

        return new JsonObject
        {
            ["run"] = Describe.RunInfo(rs),
            ["player"] = Describe.PlayerRun(me, includeDeck: true),
            ["cards"] = cards,
        };
    }

    private static string AlternativeLabel(Alternative alt)
    {
        var label = alt.Id switch
        {
            "skip" => "Skip this card reward",
            "reroll" => "Reroll the offered cards",
            "sacrifice" => "Sacrifice this card reward",
            _ => alt.Id,
        };
        // The button's own wording when it says more than its name (a cost or what you get).
        if (alt.Text.Length > 0 && !alt.Text.Equals(alt.Id, StringComparison.OrdinalIgnoreCase))
            label += $" ({alt.Text})";
        return label;
    }

    private static string? IdFrom(string text)
    {
        if (text.Length == 0) return null;
        var t = text.ToLowerInvariant();
        if (t.Contains("reroll", StringComparison.Ordinal) || t.Contains("re-roll", StringComparison.Ordinal)) return "reroll";
        if (t.Contains("sacrific", StringComparison.Ordinal)) return "sacrifice";
        if (t.Contains("skip", StringComparison.Ordinal)) return "skip";
        return null;
    }

    // ---------- helpers ----------

    /// <summary>Identity of the cards on offer: changes when a reroll replaces the holders or their cards.</summary>
    private static string CardsKey(List<Option> options) =>
        string.Join(',', options.Select(o => $"{o.Holder.GetInstanceId()}:{RuntimeHelpers.GetHashCode(o.Card)}"));

    private static bool CardsChanged(NCardRewardSelectionScreen screen, string before)
    {
        if (!GodotObject.IsInstanceValid(screen)) return true;
        var now = ReadOptions(screen);
        return now.Count > 0 && CardsKey(now) != before;
    }
}
