using System.Globalization;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// One option of a rest site (the game's RestSiteOption): Rest, Smith, and whatever a relic or
/// a deck card adds (<see cref="Listener.TryModifyRestSiteOptions"/>). The id is the game's
/// ("HEAL", "SMITH"), the title the game's English title; the text is the arena's own wording
/// with the live numbers.
/// <para>
/// An option belongs to the rest site's screen and is copied with it, so it keeps what it
/// holds in its own fields and never a <see cref="Run"/>: every method is handed the run. Two
/// options are the same option when their ids are, as in the game (where the owner counts too,
/// and a run of the arena has one player): that is what a hook's <c>options.Remove</c> and
/// <c>options.Contains</c> go by.
/// </para>
/// </summary>
public abstract class RestSiteOption : IEquatable<RestSiteOption>
{
    /// <summary>The game's option id, the snapshot's <c>id</c>: "HEAL", "SMITH", "DIG", "LIFT", "HATCH".</summary>
    public abstract string OptionId { get; }

    /// <summary>The game's English title, the snapshot's <c>name</c>: "Rest", "Smith".</summary>
    public abstract string Title { get; }

    /// <summary>What the option does right now, in the arena's own words; empty for an option that has nothing to say.</summary>
    public abstract string Describe(Run run);

    /// <summary>False greys the option out: it is listed and cannot be chosen (Smith with no card left to upgrade).</summary>
    public virtual bool IsEnabled(Run run) => true;

    /// <summary>
    /// The option was chosen: do what it does. True when it succeeded; false when it did not after
    /// all (Smith backed out of), which leaves the visit as it was. It may ask the player
    /// something (<see cref="CardSelect"/>, <see cref="Run.Ask"/>) and may open a screen over the
    /// rest site (rewards): the option then counts as used once that screen has closed.
    /// </summary>
    public abstract bool OnSelect(Run run);

    /// <summary>A copy for a copy of the run. The default copies the fields as they are.</summary>
    public virtual RestSiteOption Clone() => (RestSiteOption)MemberwiseClone();

    public bool Equals(RestSiteOption? other) => other is not null && OptionId == other.OptionId;
    public override bool Equals(object? obj) => Equals(obj as RestSiteOption);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(OptionId);
}

/// <summary>
/// Rest (the game's HealRestSiteOption, id HEAL): heal 30% of max HP after what the relics make
/// of it, then whatever a rest brings with it (<see cref="RestSite.ExecuteHeal"/>). Always
/// enabled, also at full HP.
/// </summary>
public sealed class HealRestSiteOption : RestSiteOption
{
    public override string OptionId => "HEAL";
    public override string Title => "Rest";

    /// <summary>"Heal 24 HP.", then one part for each line a relic adds to what a rest does, joined as the mod joins lines.</summary>
    public override string Describe(Run run)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"Heal {RestSite.ShownHealAmount(run)} HP.");
        foreach (var line in RunHooks.ModifyExtraRestSiteHealText(run.Context, []))
            text += " / " + line;
        return text;
    }

    public override bool OnSelect(Run run)
    {
        RestSite.ExecuteHeal(run, isMimicked: false);
        return true;
    }
}

/// <summary>
/// Smith (the game's SmithRestSiteOption, id SMITH): upgrade one card of the deck, chosen on
/// the upgrade screen. The screen can be closed without a choice; the option then did not
/// succeed and the visit is not used up. Greyed out when no card of the deck can be upgraded.
/// </summary>
public sealed class SmithRestSiteOption : RestSiteOption
{
    public override string OptionId => "SMITH";
    public override string Title => "Smith";

    /// <summary>How many cards are upgraded. The game's option has this number and nothing in v0.107.1 changes it from 1.</summary>
    public int SmithCount { get; init; } = 1;

    public override string Describe(Run run)
    {
        if (!IsEnabled(run)) return "No card in your deck can be upgraded.";
        return SmithCount == 1
            ? "Upgrade a card in your deck."
            : string.Create(CultureInfo.InvariantCulture, $"Upgrade {SmithCount} cards in your deck.");
    }

    public override bool IsEnabled(Run run) => run.Deck.Any(card => card.IsUpgradable);

    public override bool OnSelect(Run run)
    {
        // The game's prefs: the screen has a close button, and it opens also for a single card that can be upgraded.
        var prefs = CardSelectPrefs.Upgrade(SmithCount) with { Cancelable = true, RequireManualConfirmation = true };
        var picked = CardSelect.FromDeckForUpgrade(run, prefs);
        if (picked.Count == 0) return false;
        foreach (var card in picked) DeckEdits.Upgrade(run, card);
        // The game then sends "after rest site smith", which nothing in v0.107.1 listens to: not built.
        return true;
    }
}

/// <summary>
/// The rest site as a room (the game's RestSiteRoom, RestSiteSynchronizer and
/// HealRestSiteOption): what is offered there, what a rest heals, and entering it.
/// <code>
/// // from the director's EnterRoom, for arrival.Room == RoomKind.RestSite
/// RestSite.Enter(run);
/// </code>
/// The flow is the game's. On entering, the options are made (Rest, Smith; then every listener
/// may change the list), the screen is up, and "after room entered" is sent. One option that
/// succeeds uses the visit up: the others vanish, unless a listener says they stay (Miniature
/// Tent), and the proceed button turns on. Backing out of Smith's card choice is no success and
/// leaves everything as it was.
/// <para>
/// Left out by name (<see cref="LeftOut"/>): the seven options that a relic, a deck card or a
/// second player adds. Their classes come with the content that brings them
/// (<see cref="RestSiteOption"/>, <see cref="Listener.TryModifyRestSiteOptions"/>); until then
/// nothing can put them on the list. Also left out: "after rest site smith", a hook the game
/// sends and nothing in it listens to; the rest site's tutorial popup of a profile's first
/// visit; the run history the game writes (which options were chosen is on the screen:
/// <see cref="RestSiteScreen.ChosenIds"/>).
/// </para>
/// </summary>
public static class RestSite
{
    /// <summary>The share of max HP a rest heals before the relics (HealRestSiteOption.GetBaseHealAmount).</summary>
    public const decimal HealShare = 0.3m;

    /// <summary>The options the arena builds, by the game's id.</summary>
    public static readonly IReadOnlyList<string> Built = ["HEAL", "SMITH"];

    /// <summary>
    /// The game's other rest site options (every class under its Entities.RestSite), each with
    /// why the arena does not hold it. None can appear: an option is only ever put on the list
    /// by its relic or card, and none of those is built.
    /// </summary>
    public static readonly IReadOnlyList<(string OptionId, string Reason)> LeftOut =
    [
        ("MEND", "only in a run of more than one player: it heals a teammate"),
        ("DIG", "added by the relic Shovel, which is not built yet (plan chunk 25)"),
        ("LIFT", "added by the relic Girya, which is not built yet (plan chunk 29)"),
        ("HATCH", "added by the deck card Byrdonis Egg, which is not built yet (plan chunk 35)"),
        ("COOK", "added by Meat Cleaver, a relic of an Ancient of act 2 or 3"),
        ("KINDLE", "added by Pumpkin Candle, a relic of an Ancient of act 2 or 3"),
        ("CLONE", "added by Pael's Growth, a relic of an Ancient of act 2 or 3"),
    ];

    // ---------- the heal ----------

    /// <summary>30% of max HP, not rounded (HealRestSiteOption.GetBaseHealAmount): 24 at 80, 26.1 at 87.</summary>
    public static decimal BaseHealAmount(Run run) => run.MaxHp * HealShare;

    /// <summary>What a rest heals: the base amount after every listener (HealRestSiteOption.GetHealAmount). Regal Pillow adds 15.</summary>
    public static decimal HealAmount(Run run) => RunHooks.ModifyRestSiteHealAmount(run.Context, BaseHealAmount(run));

    /// <summary>The number the Rest option names: the heal cut to a whole number, as HP is (the mod's "heal 24"). What
    /// the rest really restores is no more than the HP that is missing.</summary>
    public static int ShownHealAmount(Run run) => (int)HealAmount(run);

    /// <summary>
    /// A rest's heal and what comes with it (HealRestSiteOption.ExecuteRestSiteHeal): the heal;
    /// "after rest site heal" to the listeners (Stone Humidifier raises max HP); then the rewards
    /// a rest gives besides (<see cref="IRewardHooks.TryModifyRestSiteHealRewards"/>: Tiny Mailbox
    /// two potions, Dream Catcher a card reward), offered as custom rewards. The game does
    /// nothing with who added rewards, so nobody is told "after modifying rewards" for it; the
    /// rewards themselves then go the way every custom set does.
    /// <para>
    /// An event that lets the player rest calls this with <paramref name="isMimicked"/> true (the
    /// game's PlayerCmd.MimicRestSiteHeal; Dense Vegetation). Returns the rewards screen it put
    /// up, or null when the rest brought no rewards.
    /// </para>
    /// </summary>
    public static RewardsScreen? ExecuteHeal(Run run, bool isMimicked)
    {
        run.Heal(HealAmount(run));
        RunHooks.AfterRestSiteHeal(run.Context, isMimicked);
        var rewards = new List<Reward>();
        RewardHooks.ModifyRestSiteHealRewards(run, rewards, isMimicked);
        return RunRewards.OfferCustom(run, rewards);
    }

    // ---------- the room ----------

    /// <summary>
    /// The options of a rest site that is being entered (RestSiteOption.Generate): Rest and Smith,
    /// then every listener may add to the list or take away from it, deck cards first, relics in
    /// pickup order. This is the call site of <see cref="Listener.TryModifyRestSiteOptions"/>; the
    /// game does nothing with who changed the list.
    /// </summary>
    public static List<RestSiteOption> GenerateOptions(Run run)
    {
        // In a run of more than one player Mend joins the two here; a run of the arena has one.
        var options = new List<RestSiteOption> { new HealRestSiteOption(), new SmithRestSiteOption() };
        var c = run.Context;
        RunHooks.Modifiers(c, listener => listener.TryModifyRestSiteOptions(c, options));
        return options;
    }

    /// <summary>
    /// The run enters a rest site (RestSiteRoom.EnterInternal): the options are made, the screen
    /// is up, and the listeners hear "after room entered" (Eternal Feather heals here), in that
    /// order. The map point is entered already (<see cref="MapState.Enter"/>); a rest site moves
    /// none of the act's counters.
    /// </summary>
    public static RestSiteScreen Enter(Run run)
    {
        if (run.Fight is not null) throw new InvalidOperationException("A rest site cannot be entered while a fight is on.");
        var screen = new RestSiteScreen(GenerateOptions(run));
        run.Push(screen);
        run.AfterRoomEntered(RoomKind.RestSite);
        return screen;
    }
}

/// <summary>
/// The rest site's screen, as the mod's RestSiteReader shows the game's NRestSiteRoom
/// (docs/screens.md, "Rest site"). The state's keys in order: <c>run</c>, <c>hp</c>,
/// <c>max_hp</c>, <c>boss</c>, <c>player</c> (character, gold, relics, deck_size, deck),
/// <c>potions</c>, <c>options</c> (each <c>n</c>, <c>id</c>, <c>name</c>, <c>text</c> when not
/// empty, <c>available: false</c> when it cannot be chosen), and <c>chosen</c> once an option
/// was used. The actions: <c>option:&lt;n&gt;</c> for every option that can be chosen, <c>proceed</c>
/// once the game's proceed button is on, then the potion ids.
/// <para>
/// <c>n</c> is the option's place in the list as it stands: after Rest was used with Miniature
/// Tent held, Smith is option 0. Smith's card choice is a question of the <c>option:&lt;n&gt;</c>
/// action (<see cref="Run.Ask"/>): the <c>card_select</c> screen shows over this one, and
/// backing out of it brings this screen back unchanged.
/// </para>
/// <para>
/// Not imitated, because the arena cannot be in those states: the settling time on arrival,
/// an option still playing out, the tutorial popup, Mend and everything else of co-op.
/// </para>
/// </summary>
public sealed class RestSiteScreen : RunScreen, IHearsRewardsClosed
{
    private readonly List<RestSiteOption> _options;
    private List<string> _chosen = new();
    private List<string> _chosenIds = new();
    private bool _proceedOn;

    // The place of an option that succeeded while a screen it opened (the rewards of a rest) is still up; -1 for none.
    private int _finishing = -1;

    public RestSiteScreen(IEnumerable<RestSiteOption> options) => _options = options.ToList();

    public override string Name => "rest";

    /// <summary>The options still on offer, in the game's order: what <c>option:&lt;n&gt;</c> indexes.</summary>
    public IReadOnlyList<RestSiteOption> Options => _options;

    /// <summary>The titles of the options used at this rest site, in order: the snapshot's <c>chosen</c>.</summary>
    public IReadOnlyList<string> Chosen => _chosen;

    /// <summary>The ids of the options used at this rest site, in order: what the game's run history records ("HEAL", "SMITH").</summary>
    public IReadOnlyList<string> ChosenIds => _chosenIds;

    /// <summary>The game's proceed button is on: after an option succeeded, or when no option is left.</summary>
    public bool CanProceed => _finishing < 0 && (_proceedOn || _options.Count == 0);

    private bool Available(Run run, RestSiteOption option) => _finishing < 0 && option.IsEnabled(run);

    // ---------- the snapshot (RestSiteReader.Capture) ----------

    public override GameSnapshot Snapshot(Run run)
    {
        var state = new JsonObject
        {
            ["run"] = run.RunJson(),
            ["hp"] = run.Hp,
            ["max_hp"] = run.MaxHp,
        };
        MapState.AddBoss(state, run);
        state["player"] = run.PlayerJson(PlayerKeys.Gold | PlayerKeys.Deck);
        state["potions"] = RunPotions.Describe(run);

        var list = new JsonArray();
        var actions = new List<LegalAction>();
        var anyAvailable = false;
        for (var n = 0; n < _options.Count; n++)
        {
            var option = _options[n];
            var text = option.Describe(run);
            var available = Available(run, option);
            var entry = new JsonObject { ["n"] = n, ["id"] = option.OptionId, ["name"] = option.Title };
            if (text.Length > 0) entry["text"] = text;
            if (!available) entry["available"] = false;
            list.Add(entry);
            if (!available) continue;
            anyAvailable = true;
            actions.Add(new LegalAction(Id(n), Label(run, option, text)));
        }
        state["options"] = list;

        if (_chosen.Count > 0)
        {
            var chosen = new JsonArray();
            foreach (var title in _chosen) chosen.Add(title);
            state["chosen"] = chosen;
        }

        // Proceed turns on only after an option succeeded or when none is left, so options still on offer beside it are extras.
        if (CanProceed) actions.Add(new LegalAction("proceed", anyAvailable ? "Proceed (leave the remaining options unused)" : "Proceed"));

        // Potion actions alone are no rest site decision; they ride along with the screen's own.
        if (actions.Count == 0)
        {
            var waiting = _chosen.Count > 0 ? $"{_chosen[^1]} in progress" : _options.Count == 0 ? "rest site options appearing" : "no rest site option is clickable yet";
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = waiting, State = state };
        }
        actions.AddRange(RunPotions.Legal(run));
        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    private static string Id(int n) => string.Create(CultureInfo.InvariantCulture, $"option:{n}");

    /// <summary>The label of an option's action (RestSiteReader.Label): Rest names its heal, Smith what it does, every
    /// other option the first sentence of its text.</summary>
    private static string Label(Run run, RestSiteOption option, string text) => option.OptionId switch
    {
        "HEAL" => string.Create(CultureInfo.InvariantCulture, $"{option.Title} (heal {RestSite.ShownHealAmount(run)})"),
        "SMITH" => $"{option.Title} (upgrade a card)",
        _ => text.Length > 0 ? $"{option.Title} ({Short(text)})" : option.Title,
    };

    /// <summary>The first sentence of a text, without its full stop, cut to 67 characters and three dots when it is longer than 70.</summary>
    internal static string Short(string text)
    {
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        var sentence = (end > 0 ? text[..end] : text).TrimEnd('.', ' ');
        return sentence.Length <= 70 ? sentence : sentence[..67].TrimEnd() + "...";
    }

    // ---------- the actions (RestSiteReader.Execute, RestSiteSynchronizer.ChooseOption) ----------

    public override ActionResult Execute(Run run, string actionId)
    {
        if (RunPotions.TryExecute(run, actionId, out var potion)) return potion;
        if (actionId == "proceed") return Proceed(run);
        if (actionId.StartsWith("option:", StringComparison.Ordinal)
            && int.TryParse(actionId.AsSpan(7), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return Choose(run, n);
        return ActionResult.Fail($"Unknown rest site action '{actionId}'.");
    }

    private ActionResult Choose(Run run, int n)
    {
        if (n < 0 || n >= _options.Count) return ActionResult.Fail(string.Create(CultureInfo.InvariantCulture, $"There is no rest site option {n}."));
        var option = _options[n];
        if (!Available(run, option)) return ActionResult.Fail($"{option.Title} is not available right now.");

        // False: the option did not succeed after all (Smith's card choice was closed). Nothing is used up.
        if (!option.OnSelect(run)) return ActionResult.Success;

        // The game goes on only when the option has run to its end, and a rest ends when the rewards it brought are closed.
        if (ReferenceEquals(run.Top, this)) Finish(run, n);
        else _finishing = n;
        return ActionResult.Success;
    }

    /// <summary>The rewards a rest brought are closed (every one taken, or the rest skipped): the option has run to its end.</summary>
    public void AfterRewardsClosed(Run run, RewardsSet rewards)
    {
        if (_finishing < 0 || !ReferenceEquals(run.Top, this)) return;
        var n = _finishing;
        _finishing = -1;
        Finish(run, n);
    }

    /// <summary>
    /// An option succeeded (the end of RestSiteSynchronizer.ChooseOption): it is noted, the
    /// listeners are asked whether the others go (Miniature Tent keeps them), the list loses
    /// them all or this one alone, and the proceed button turns on.
    /// </summary>
    private void Finish(Run run, int n)
    {
        var option = _options[n];
        _chosenIds.Add(option.OptionId);
        _chosen.Remove(option.Title);
        _chosen.Add(option.Title);
        if (RunHooks.ShouldDisableRemainingRestSiteOptions(run.Context)) _options.Clear();
        else _options.RemoveAt(n);
        _proceedOn = true;
    }

    private ActionResult Proceed(Run run)
    {
        if (!CanProceed) return ActionResult.Fail("The proceed button is not enabled yet.");
        run.Pop();
        run.RoomFinished();
        return ActionResult.Success;
    }

    public override RunScreen Clone(CloneMap map) => new RestSiteScreen(_options.Select(option => option.Clone()))
    {
        _chosen = _chosen.ToList(), _chosenIds = _chosenIds.ToList(), _proceedOn = _proceedOn, _finishing = _finishing,
    };
}
