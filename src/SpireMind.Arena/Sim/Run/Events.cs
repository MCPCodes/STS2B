using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// A relic whose tooltip in the game shows cards beside its own text (its ExtraHoverTips hold
/// card tips: Neow's Torment shows Neow's Fury, Cursed Pearl shows Greed). An event option that
/// offers or shows the relic lists those cards under <c>cards</c>, as the mod does.
/// </summary>
public interface ICardTips
{
    /// <summary>The cards by class name, in the tooltip's order; "+" for an upgraded one ("Bash+").</summary>
    IReadOnlyList<string> CardTips { get; }
}

/// <summary>
/// One option of an event page (the game's EventOption): what the player reads, what its
/// tooltip shows, whether it is locked, whether it would kill, and what happens when it is
/// chosen. Options are made by their event (<see cref="RunEvent.Option(string, string, string, Action{Run}?)"/>)
/// and belong to one page; a page change makes new ones.
/// <code>
/// Option("PLAIN", "Plain", "Lose 6 HP. Gain 55 Gold.", Plain).ThatDoesDamage(6),
/// Option("ORNATE", "Ornate", "Obtain a random Relic. Add Clumsy to your Deck.", Ornate).WithCards("Clumsy")
/// </code>
/// </summary>
public sealed class EventOption
{
    private enum Deadly { Never, AtOrBelowHp, AtOrBelowMaxHp, Asked }

    private Delegate? _onChosen;
    private Deadly _deadly;
    private decimal _deadlyAmount;
    private Func<Run, bool>? _deadlyIf;
    private List<object> _tips = new();

    internal EventOption(string key, string title, string text, Delegate? onChosen, bool isProceed = false)
    {
        Key = key;
        Title = title;
        Text = text;
        _onChosen = onChosen;
        IsProceed = isProceed;
    }

    /// <summary>The option's name in the game's text table ("PLAIN" of THIS_OR_THAT.pages.INITIAL.options.PLAIN): for logs and checks. "PROCEED" for the room's own leave option.</summary>
    public string Key { get; }

    /// <summary>The game's English title of the option.</summary>
    public string Title { get; }

    /// <summary>What it does, in our own words, with the numbers as they stand.</summary>
    public string Text { get; }

    /// <summary>The game's rule: an option with nothing to run when chosen is locked. It is listed and never offered.</summary>
    public bool IsLocked => _onChosen is null;

    /// <summary>The room's own option of a finished event, which opens the map.</summary>
    public bool IsProceed { get; }

    /// <summary>The option was chosen. The game then switches its button off, so it cannot be chosen twice.</summary>
    public bool WasChosen { get; private set; }

    /// <summary>The relic an Ancient's option gives (the game's EventOption.Relic). It is there to be shown: the relic
    /// the player obtains is a new object of the same class.</summary>
    public Relic? Relic { get; private set; }

    /// <summary>A number the option carries for its own method (which potion slot, which card), for options built in a loop.</summary>
    public int Argument { get; private set; }

    // ---------- what the event says about its option ----------

    /// <summary>The option costs this much HP: it would kill at or below that (the game's ThatDoesDamage). What a relic takes
    /// off the loss does not count, as in the game.</summary>
    public EventOption ThatDoesDamage(decimal damage)
    {
        _deadly = Deadly.AtOrBelowHp;
        _deadlyAmount = damage;
        _deadlyIf = null;
        return this;
    }

    /// <summary>The option costs this much max HP: it would kill at or below that (the game's ThatDecreasesMaxHp).</summary>
    public EventOption ThatDecreasesMaxHp(decimal value)
    {
        _deadly = Deadly.AtOrBelowMaxHp;
        _deadlyAmount = value;
        _deadlyIf = null;
        return this;
    }

    /// <summary>
    /// The option would kill whenever <paramref name="willKill"/> says so (the game's
    /// ThatWillKillPlayerIf). It is asked every time the page is read, so it must be a method of
    /// the event that reads the event's own fields and the run it is handed.
    /// </summary>
    public EventOption ThatWillKillPlayerIf(Func<Run, bool> willKill)
    {
        _deadly = Deadly.Asked;
        _deadlyIf = willKill;
        return this;
    }

    /// <summary>Cards the option's tooltip shows (the card it adds, the curse it gives), by class name; "+" for an upgraded one.</summary>
    public EventOption WithCards(params string[] cards)
    {
        foreach (var name in cards) _tips.Add(TipCard(name));
        return this;
    }

    /// <summary>A card of the deck that the option's tooltip shows as it is (the card an option would take away).</summary>
    public EventOption WithCard(CardInstance card)
    {
        _tips.Add(card);
        return this;
    }

    /// <summary>A relic the option's tooltip shows (the game's HoverTipFactory.FromRelic): the relic, then the cards of its own tooltip.</summary>
    public EventOption WithRelicTip(Relic relic)
    {
        _tips.Add(relic);
        AddCardTipsOf(relic);
        return this;
    }

    /// <summary>The same for a relic of the registry, by class name.</summary>
    public EventOption WithRelicTip(string relicClassName) => WithRelicTip(Content.Registry.NewRelic(relicClassName));

    /// <summary>What a relic's own tooltip shows besides the relic (the game's HoverTipFactory.FromRelicExcludingItself): for an
    /// option whose own title and text already are the relic's.</summary>
    public EventOption WithCardTipsOf(Relic relic)
    {
        AddCardTipsOf(relic);
        return this;
    }

    /// <summary>See <see cref="Argument"/>.</summary>
    public EventOption WithArgument(int argument)
    {
        Argument = argument;
        return this;
    }

    /// <summary>The relic an Ancient's option gives; the option's tooltip then shows what the relic's own shows besides itself.</summary>
    internal EventOption WithRelic(Relic relic)
    {
        Relic = relic;
        AddCardTipsOf(relic);
        return this;
    }

    private void AddCardTipsOf(Relic relic)
    {
        if (relic is ICardTips tips)
            foreach (var name in tips.CardTips) _tips.Add(TipCard(name));
    }

    private static CardInstance TipCard(string name)
    {
        var entry = DeckCard.Parse(name);
        var card = new CardInstance(0, Content.Registry.Card(entry.ClassName));
        if (entry.Upgraded && card.IsUpgradable) card.ApplyUpgrade();
        return card;
    }

    // ---------- reading ----------

    /// <summary>Whether choosing the option now would kill the player, as the option itself says (the game's WillKillPlayer).</summary>
    public bool WillKillPlayer(Run run) => _deadly switch
    {
        Deadly.AtOrBelowHp => run.Hp <= _deadlyAmount,
        Deadly.AtOrBelowMaxHp => run.MaxHp <= _deadlyAmount,
        Deadly.Asked => _deadlyIf!(run),
        _ => false,
    };

    /// <summary>What the tooltip shows, in its order: cards (<see cref="CardInstance"/>) and relics (<see cref="Arena.Relic"/>).</summary>
    internal IReadOnlyList<object> Tips => _tips;

    internal void Invoke(Run run)
    {
        WasChosen = true;
        switch (_onChosen)
        {
            case Action<Run> plain: plain(run); break;
            case Action<Run, EventOption> withOption: withOption(run, this); break;
        }
    }

    // ---------- copies ----------

    /// <summary>
    /// A copy for a copy of the event. What the option runs and asks is a method of its event,
    /// so the copy's is the same method of the copied event.
    /// </summary>
    internal EventOption CloneFor(RunEvent from, RunEvent to, CloneMap map) => new(Key, Title, Text, Rebind(_onChosen, from, to), IsProceed)
    {
        WasChosen = WasChosen,
        Relic = map.Get(Relic),
        Argument = Argument,
        _deadly = _deadly,
        _deadlyAmount = _deadlyAmount,
        _deadlyIf = (Func<Run, bool>?)Rebind(_deadlyIf, from, to),
        // A deck card or a held relic in the tooltip is the copy's own; anything made for the tooltip stays as it is.
        _tips = _tips.Select(tip => tip switch { CardInstance card => map.Get(card)!, Relic relic => map.Get(relic)!, _ => tip }).ToList(),
    };

    private static Delegate? Rebind(Delegate? method, RunEvent from, RunEvent to) =>
        method is not null && ReferenceEquals(method.Target, from) ? Delegate.CreateDelegate(method.GetType(), to, method.Method) : method;

    /// <summary>
    /// What an option runs or asks must be a method of its event (or hold nothing at all): an
    /// event is copied with its run, and a function that had caught a variable would go on
    /// pointing at the run it was made in.
    /// </summary>
    internal static void Guard(Delegate? method, RunEvent owner, string what)
    {
        if (method?.Target is not { } target || ReferenceEquals(target, owner)) return;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        if (target.GetType().GetFields(flags).Length == 0) return;
        throw new InvalidOperationException(
            $"{owner.ClassName}: {what} is a function that caught a variable. An event is copied with its run: use a method of the event, and keep what it needs in the event's fields or in the option's Argument.");
    }

    internal void GuardFor(RunEvent owner)
    {
        Guard(_onChosen, owner, $"what the option {Key} runs");
        Guard(_deadlyIf, owner, $"what the option {Key} asks about killing");
    }

    /// <summary>The key, with what a dump needs to tell two pages apart.</summary>
    internal string Describe() => Key + (IsLocked ? ":locked" : "") + (WasChosen ? ":chosen" : "") + (Argument != 0 ? string.Create(CultureInfo.InvariantCulture, $":{Argument}") : "");
}

/// <summary>
/// One event of the game (its EventModel): a room where the player reads a page and chooses
/// one of its options, which may cost and give things, lead to another page, open a card
/// choice or a rewards screen, and in the end leaves the event finished. A new object is made
/// for every event room; it is copied with the run, so everything it rolls and counts is in
/// its own fields.
/// <code>
/// public sealed class ThisOrThat : RunEvent
/// {
///     private int _gold;
///     public override string Title => "This or That";
///     protected override void CalculateVars(Run run) => _gold = Rng.NextInt(41, 69);
///     protected override string InitialDescription(Run run) => "Two chests stand here.";
///     protected override IReadOnlyList&lt;EventOption&gt; GenerateInitialOptions(Run run) =>
///     [
///         Option("PLAIN", "Plain", $"Lose 6 HP. Gain {_gold} Gold.", Plain).ThatDoesDamage(6),
///         Option("ORNATE", "Ornate", "Obtain a random Relic. Add Clumsy to your Deck.", Ornate).WithCards("Clumsy"),
///     ];
///     private void Plain(Run run)
///     {
///         run.Damage(6);
///         if (run.IsOver) return;
///         run.GainGold(_gold);
///         SetEventFinished("You take the gold.");
///     }
///     ...
/// }
/// </code>
/// <para>
/// The order on entering is the game's (EventModel.BeginEvent, then EventRoom.EnterInternal):
/// the event's own random stream is made, <see cref="BeforeEventStarted"/>,
/// <see cref="CalculateVars"/>, the first page (<see cref="GenerateInitialOptions"/>, then
/// <see cref="InitialDescription"/>), then the relics hear "after room entered", then
/// <see cref="AfterEventStarted"/>. So a lock that the first page's options were made with is
/// not undone by what a relic does on entering; whether an option would kill is asked anew
/// each time the page is read.
/// </para>
/// <para>
/// An option's method changes the player through the run's commands (<c>run.Damage</c>,
/// <c>run.GainGold</c>, <see cref="DeckEdits"/>, <see cref="CardSelect"/>,
/// <see cref="RunRewards"/>), tests <c>run.IsOver</c> after anything that can kill, and ends by
/// setting the next page (<see cref="SetEventState"/>) or finishing
/// (<see cref="SetEventFinished"/>). A card choice in the middle runs the whole method again
/// with the answer (<see cref="Run.Ask"/>): the event it runs on is then the copy taken before
/// the action, so nothing it did before the choice is done twice.
/// </para>
/// <para>
/// Titles are the game's English names. Page and option texts are the arena's own plain
/// wording with the live numbers.
/// </para>
/// </summary>
public abstract class RunEvent
{
    private GameRng? _rng;
    private List<EventOption> _options = new();
    private bool _cleanUpDue;
    private bool _cleanedUp;

    /// <summary>The game's class name ("ThisOrThat"): the event's place in an act's list, and what its random stream is seeded with.</summary>
    public virtual string ClassName => GetType().Name;

    /// <summary>The game's English title, the snapshot's <c>event</c>.</summary>
    public abstract string Title { get; }

    /// <summary>An Ancient's room (the snapshot's <c>ancient</c>; options read "Take ...").</summary>
    public virtual bool IsAncient => false;

    /// <summary>
    /// The event's own random stream: seeded with the run's seed and the event's id, so its
    /// rolls are the same whenever the event is met in a run of that seed, in both stream modes.
    /// </summary>
    public GameRng Rng => _rng ?? throw new InvalidOperationException($"{ClassName} was not begun: an event is opened with EventScreen.Open.");

    /// <summary>The page's text, in our own words; empty for a page that has none.</summary>
    public string Text { get; private set; } = "";

    /// <summary>The event is done: its page has no options and the room shows its own leave option.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>The options of the page, in the game's order. None once the event is finished.</summary>
    public IReadOnlyList<EventOption> Options => _options;

    // ---------- what an event writes ----------

    /// <summary>Before anything is rolled (the game's BeforeEventStarted): the Ancient's heal, an event taking hold of the belt.</summary>
    protected virtual void BeforeEventStarted(Run run) { }

    /// <summary>The event's start rolls, drawn from <see cref="Rng"/> in the game's order (the game's CalculateVars).</summary>
    protected virtual void CalculateVars(Run run) { }

    /// <summary>The first page's text. It is read after the first page's options are made.</summary>
    protected abstract string InitialDescription(Run run);

    /// <summary>The first page's options (the game's GenerateInitialOptions). Whatever the game rolls while it builds them is rolled here.</summary>
    protected abstract IReadOnlyList<EventOption> GenerateInitialOptions(Run run);

    /// <summary>After the relics heard "after room entered" (the game's AfterEventStarted).</summary>
    protected virtual void AfterEventStarted(Run run) { }

    /// <summary>
    /// Once (the game's OnEventFinished): giving back what the event held. It runs when the
    /// method that called <see cref="SetEventFinished"/> returns, and at the latest when the
    /// room is left.
    /// </summary>
    protected virtual void OnEventFinished(Run run) { }

    /// <summary>
    /// A rewards screen that an option of this event (or a relic it gave) opened has closed:
    /// every reward taken, or the rest skipped. In the game the option's own code goes on from
    /// here; an event whose option does something after its rewards does it here.
    /// </summary>
    protected virtual void AfterRewardsClosed(Run run, RewardsSet rewards) { }

    /// <summary>Copy here what a plain copy of the event's fields would share: lists, and anything else that is not a number, a text or a deck card.</summary>
    protected virtual void OnCloned(CloneMap map) { }

    /// <summary>An option with a method of this event to run when it is chosen; null makes a locked option.</summary>
    protected EventOption Option(string key, string title, string text, Action<Run>? onChosen) => Made(new EventOption(key, title, text, onChosen));

    /// <summary>The same for a method that wants the chosen option itself (its <see cref="EventOption.Argument"/>, its relic).</summary>
    protected EventOption Option(string key, string title, string text, Action<Run, EventOption>? onChosen) => Made(new EventOption(key, title, text, onChosen));

    /// <summary>An option that is listed and cannot be chosen: a requirement is not met.</summary>
    protected EventOption LockedOption(string key, string title, string text) => new(key, title, text, null);

    private EventOption Made(EventOption option)
    {
        option.GuardFor(this);
        return option;
    }

    /// <summary>
    /// A new page: its text and its options (the game's SetEventState). A page without options
    /// is a finished event too, but one whose clean-up waits until the room is left: only
    /// <see cref="SetEventFinished"/> cleans up at once.
    /// </summary>
    protected void SetEventState(string text, params EventOption[] options) => SetEventState(text, (IReadOnlyList<EventOption>)options);

    protected void SetEventState(string text, IReadOnlyList<EventOption> options)
    {
        foreach (var option in options)
        {
            if (option is null) throw new InvalidOperationException($"{ClassName} has a page with a null option.");
            if (option.IsProceed) throw new InvalidOperationException($"{ClassName}: the leave option is the room's own; an event finishes and never lists it.");
            option.GuardFor(this);
        }
        if (options.Count == 0 && IsFinished) throw new InvalidOperationException($"{ClassName} set a page after it was finished.");
        _options = options.ToList();
        Text = text;
        if (_options.Count == 0) IsFinished = true;
    }

    /// <summary>The event is done: the page shows <paramref name="text"/> and the room's own leave option (the game's SetEventFinished).</summary>
    protected void SetEventFinished(string text)
    {
        SetEventState(text, Array.Empty<EventOption>());
        IsFinished = true;
        _cleanUpDue = true;
    }

    // ---------- what the room does with it ----------

    /// <summary>The game's BeginEvent: the stream, then the start of the event up to its first page.</summary>
    internal void Begin(Run run)
    {
        if (_rng is not null) throw new InvalidOperationException($"{ClassName} was begun before.");
        _rng = run.State.Part<RunWorld>().Streams.Event(ClassName);
        BeforeEventStarted(run);
        CalculateVars(run);
        if (run.IsOver) return;
        // The options first, then the text, as the game does: a page's text may name what the options rolled.
        var options = GenerateInitialOptions(run);
        SetEventState(InitialDescription(run), options);
        CleanUpIfDue(run);
    }

    internal void Started(Run run)
    {
        AfterEventStarted(run);
        CleanUpIfDue(run);
    }

    internal void Choose(Run run, EventOption option)
    {
        option.Invoke(run);
        if (!run.IsOver) CleanUpIfDue(run);
    }

    internal void RewardsClosed(Run run, RewardsSet rewards)
    {
        AfterRewardsClosed(run, rewards);
        if (!run.IsOver) CleanUpIfDue(run);
    }

    /// <summary>The room is left: the clean-up runs now if it has not yet (the game's EnsureCleanup on leaving the room).</summary>
    internal void Left(Run run) => CleanUp(run);

    private void CleanUpIfDue(Run run)
    {
        if (_cleanUpDue) CleanUp(run);
    }

    private void CleanUp(Run run)
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        OnEventFinished(run);
    }

    // ---------- copies and dumps ----------

    internal RunEvent CloneFor(CloneMap map)
    {
        var copy = (RunEvent)MemberwiseClone();
        copy._rng = _rng?.Clone();
        copy._options = _options.Select(option => option.CloneFor(this, copy, map)).ToList();
        EventFields.Remap(copy, map);
        copy.OnCloned(map);
        return copy;
    }

    /// <summary>Everything about the event as it stands, for comparing two runs.</summary>
    internal JsonObject Dump() => new()
    {
        ["event"] = ClassName,
        ["rng"] = _rng?.Counter,
        ["finished"] = IsFinished,
        ["cleaned_up"] = _cleanedUp,
        ["text"] = Text,
        ["options"] = string.Join(" ", _options.Select(option => option.Describe())),
        ["fields"] = EventFields.Dump(this),
    };
}

/// <summary>
/// An Ancient's room (the game's AncientEventModel): the room of an act's start point. On
/// entering, the player is healed by all the HP that is missing (CreatureCmd.Heal outside a
/// fight: no hook hears it); every option gives a relic, and taking one finishes the event.
/// Neow, the first act's Ancient, is the only one in scope.
/// <para>
/// Left out, by name: the smaller heal of ascension 2 (Weary Traveler: 80% of what is
/// missing); a hook that bars the Ancient (the game asks ShouldAllowAncient, which nothing in
/// this build overrides); the Ancient's dialogue, which the mod clicks through by itself; a
/// room entered already finished (a loaded save).
/// </para>
/// </summary>
public abstract class AncientEvent : RunEvent
{
    public sealed override bool IsAncient => true;

    /// <summary>The HP the heal on entering gave (the game's HealedAmount).</summary>
    public int HealedAmount { get; private set; }

    /// <summary>Neow's visitor is put at 0 HP first, so that the heal fills the whole bar (the game tests "this is Neow").</summary>
    protected virtual bool HealsFromZero => false;

    /// <summary>The page text once a relic was taken; none unless an Ancient has one.</summary>
    protected virtual string DoneDescription => "";

    /// <summary>Whether the arena has built a relic of this Ancient: the registry, unless a check says otherwise.</summary>
    protected virtual bool IsBuilt(string relicClassName) => RunRelics.IsBuilt(relicClassName);

    /// <summary>A new relic object of a class the Ancient offers: the registry's, unless a check has its own.</summary>
    protected virtual Relic NewRelic(string relicClassName) => Content.Registry.NewRelic(relicClassName);

    protected override void BeforeEventStarted(Run run)
    {
        var state = run.State;
        if (state.Ascension >= 2)
            throw new NotSupportedException("The arena leaves out the Ancient's heal of ascension 2 and up (Weary Traveler heals 80% of the missing HP).");

        // The game sets the HP by hand (no hook, no death) and heals from there; outside a fight
        // no hook hears a heal either, so the HP is simply set: the run's own heal command would
        // refuse a player who stands at 0.
        if (HealsFromZero) state.Hp = 0;
        var before = state.Hp;
        decimal missing = state.MaxHp - state.Hp;
        state.Hp = (int)Math.Min(state.Hp + missing, state.MaxHp);
        HealedAmount = state.Hp - before;
    }

    /// <summary>
    /// An option that gives a relic and finishes the event (the game's AncientEventModel.RelicOption):
    /// its title and text are the relic's own, its tooltip shows what the relic's shows.
    /// </summary>
    protected EventOption RelicOption(string relicClassName)
    {
        var relic = NewRelic(relicClassName);
        return Option(GameNames.Slug(relicClassName), relic.Name, relic.Describe(), TakeRelic).WithRelic(relic);
    }

    private void TakeRelic(Run run, EventOption option)
    {
        // The pickup effect may ask for a card or open a screen of its own; the event is done either way.
        run.ObtainRelic(NewRelic(option.Relic!.ClassName));
        if (run.IsOver) return;
        Done();
    }

    /// <summary>The event is finished (the game's Done).</summary>
    protected void Done() => SetEventFinished(DoneDescription);
}

/// <summary>
/// What the run holds of events between rooms: which events it has met (the game's
/// VisitedEventIds: an event is met once per run, also across acts), and the event of the room
/// the player is in, with everything that event has rolled and counted.
/// </summary>
public sealed class EventsState : RunPart
{
    private List<string> _visited = new();

    /// <summary>The events met so far, by class name, in the order they were met. Neow is not among them: the Ancient is not taken from the list.</summary>
    public IReadOnlyList<string> Visited => _visited;

    /// <summary>The event of the room the player is in; null outside an event room.</summary>
    public RunEvent? Current { get; internal set; }

    public bool HasVisited(string className) => _visited.Contains(className);

    internal void MarkVisited(string className)
    {
        if (!_visited.Contains(className)) _visited.Add(className);
    }

    public override RunPart Clone(CloneMap map) => new EventsState { _visited = _visited.ToList(), Current = Current?.CloneFor(map) };

    public override JsonNode Dump() => new JsonObject
    {
        ["visited"] = string.Join(" ", _visited),
        ["current"] = Current?.Dump(),
    };
}

/// <summary>
/// The event screen (the game's NEventRoom as the mod's EventReader shows it): the event's
/// title, the page's text, its options and the potion actions. Keys, their order, labels and
/// refusals are those of docs/screens.md, "Event".
/// <code>
/// EventScreen.Open(run, EventList.Pull(run));          // an Event room: the act's next event
/// EventScreen.Open(run, rooms.Ancient!);               // the act's start point: Neow
/// MapState.MarkRoomVisited(run, RoomKind.Event);       // then, as for every room
/// </code>
/// <para>
/// A page's options are numbered from 0 and offered as <c>option:&lt;n&gt;</c>: "Choose
/// &lt;title&gt;", or "Take &lt;relic&gt;" at an Ancient. A locked option is listed and not
/// offered; so is one that would kill. A finished event shows the room's own option, titled
/// "Proceed" as in the game and labelled "Leave": it takes the screen away and tells the
/// director that the room is finished (<see cref="Run.RoomFinished"/>). Potion actions ride
/// along while at least one option is offered.
/// </para>
/// <para>
/// House rule: a page on which nothing can be chosen (every option locked or deadly) is one
/// the mod waits on for a human's hand. The arena has no human, so whatever would lead to
/// such a page is refused by name (an exception: the action is undone). None of the events of
/// an Overgrowth act can make one.
/// </para>
/// </summary>
public sealed class EventScreen : RunScreen, IHearsRewardsClosed
{
    /// <summary>The mod's note on an option that would kill.</summary>
    public const string DeadlyNote = "would kill you; not offered";

    private const string OptionPrefix = "option:";
    private const string NothingToChoose = "no event option the bot can take (all locked, deadly or special); a human has to choose";

    /// <summary>The room's own option of a finished event: the game's PROCEED, whose English title is "Proceed" for events and Ancients alike.</summary>
    private static readonly EventOption Proceed = new("PROCEED", "Proceed", "", null, isProceed: true);

    public override string Name => "event";

    /// <summary>The event of the room the run is in.</summary>
    public static RunEvent? Current(Run run) => run.State.FindPart<EventsState>()?.Current;

    private static RunEvent Event(Run run) =>
        Current(run) ?? throw new InvalidOperationException("The event screen is up but the run is in no event: open an event with EventScreen.Open.");

    // ---------- entering the room ----------

    /// <summary>
    /// Enters an event's room as the game does and puts its screen up: the event is begun (its
    /// stream, its start rolls, its first page), the relics hear "after room entered", then the
    /// event's own last word. The caller moves the act's list counter afterwards
    /// (<see cref="MapState.MarkRoomVisited"/>), as for every room.
    /// </summary>
    public static EventScreen Open(Run run, RunEvent ev)
    {
        var screen = new EventScreen();
        run.State.Part<EventsState>().Current = ev;
        run.Push(screen);
        ev.Begin(run);
        if (run.IsOver) return screen;
        run.AfterRoomEntered(RoomKind.Event);
        if (run.IsOver) return screen;
        ev.Started(run);
        if (!run.IsOver) screen.RefuseAPageNobodyCanLeave(run);
        return screen;
    }

    /// <summary>The same for an event of the registry, by class name ("ThisOrThat", "Neow").</summary>
    /// <exception cref="KeyNotFoundException">The arena has not built that event.</exception>
    public static EventScreen Open(Run run, string eventClassName) => Open(run, Content.Registry.NewEvent(eventClassName));

    // ---------- the page as the mod reads it ----------

    /// <summary>One option as it stands: its entry of <c>options</c>, its label, and why it cannot be chosen now (null when it can).</summary>
    private sealed record View(int N, EventOption Option, string Name, bool Leave, string Label, string? Refusal, JsonObject Json)
    {
        public bool Offered => Refusal is null;
    }

    private static List<View> Views(Run run, RunEvent ev)
    {
        // A finished event has no options of its own: the room shows its one PROCEED option.
        IReadOnlyList<EventOption> options = ev.IsFinished && ev.Options.Count == 0 ? [Proceed] : ev.Options;
        var views = new List<View>(options.Count);
        for (var n = 0; n < options.Count; n++) views.Add(Read(run, ev, n, options[n]));
        return views;
    }

    private static View Read(Run run, RunEvent ev, int n, EventOption option)
    {
        var title = option.Title;
        var text = option.Text;
        var name = title.Length > 0 ? title : text.Length > 0 ? Shorten(text) : string.Create(CultureInfo.InvariantCulture, $"option {n}");
        var leave = option.IsProceed;
        var locked = option.IsLocked && !leave;
        var kills = option.WillKillPlayer(run);

        // What the option shows beyond its own text: an Ancient's relic (else the first relic of
        // its tooltip) and up to three cards of its tooltip, each name once.
        var relic = ev.IsAncient ? option.Relic : null;
        JsonArray? cards = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tip in option.Tips)
        {
            if (tip is CardInstance card)
            {
                if ((cards?.Count ?? 0) < MaxTipCards && seen.Add(card.Title)) (cards ??= new JsonArray()).Add(RunDescribe.Card(card));
            }
            else if (relic is null && tip is Relic shown)
            {
                relic = shown;
            }
        }

        string? note = null;
        string? refusal = null;
        if (locked)
        {
            refusal = $"{name} is locked.";
        }
        else if (kills)
        {
            note = DeadlyNote;
            refusal = $"{name} would kill you.";
        }
        else if (option.WasChosen)
        {
            refusal = $"{name} is not clickable right now.";
        }

        var label = leave ? "Leave" : ev.IsAncient ? $"Take {(relic is not null && relic.Name.Length > 0 ? relic.Name : name)}" : $"Choose {name}";

        var json = new JsonObject { ["n"] = n, ["title"] = name };
        if (text.Length > 0 && text != name) json["text"] = text;
        if (leave) json["leave"] = true;
        if (locked) json["locked"] = true;
        if (kills) json["will_kill_player"] = true;
        // An Ancient's option is usually just its relic's own name and text: those are not repeated.
        if (relic is not null && !(relic.Name == title && relic.Describe() == text)) json["relic"] = RunDescribe.Relic(relic);
        if (cards is not null) json["cards"] = cards;
        if (note is not null) json["note"] = note;
        return new View(n, option, name, leave, label, refusal, json);
    }

    private const int MaxTipCards = 3;

    private static string Shorten(string text, int max = 60) => text.Length <= max ? text : text[..(max - 3)].TrimEnd() + "...";

    public override GameSnapshot Snapshot(Run run)
    {
        var ev = Event(run);
        var views = Views(run, ev);

        var state = new JsonObject { ["run"] = run.RunJson(), ["event"] = ev.Title };
        if (ev.IsAncient) state["ancient"] = true;
        if (ev.Text.Length > 0) state["text"] = ev.Text;
        if (ev.IsFinished) state["finished"] = true;
        var options = new JsonArray();
        foreach (var view in views) options.Add(view.Json);
        state["options"] = options;
        state["player"] = run.PlayerJson(PlayerKeys.NoPotions);
        state["potions"] = RunPotions.Describe(run);

        var actions = new List<LegalAction>();
        foreach (var view in views)
            if (view.Offered) actions.Add(new LegalAction(Id(view.N), view.Label));

        // Potion actions alone are not an event decision; they ride along with the options.
        if (actions.Count == 0)
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = NothingToChoose, State = state };
        actions.AddRange(RunPotions.Legal(run));
        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    private static string Id(int n) => string.Create(CultureInfo.InvariantCulture, $"{OptionPrefix}{n}");

    // ---------- the actions ----------

    public override ActionResult Execute(Run run, string actionId)
    {
        var ev = Event(run);
        var views = Views(run, ev);

        if (actionId.StartsWith("potion:", StringComparison.Ordinal) || actionId.StartsWith("discard_potion:", StringComparison.Ordinal))
        {
            // A potion's id. It is only on offer beside an option of the page.
            if (!views.Any(view => view.Offered)) return ActionResult.Fail($"'{actionId}' is not available now.");
            RunPotions.TryExecute(run, actionId, out var potionResult);
            return potionResult;
        }

        if (!actionId.StartsWith(OptionPrefix, StringComparison.Ordinal)
            || !int.TryParse(actionId.AsSpan(OptionPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return ActionResult.Fail($"Unknown event action '{actionId}'.");
        if (n < 0 || n >= views.Count) return ActionResult.Fail(string.Create(CultureInfo.InvariantCulture, $"There is no event option {n}."));

        var chosen = views[n];
        if (chosen.Refusal is { } refusal) return ActionResult.Fail(refusal);

        if (chosen.Leave)
        {
            Leave(run, ev);
            return ActionResult.Success;
        }

        ev.Choose(run, chosen.Option);
        if (!run.IsOver) RefuseAPageNobodyCanLeave(run);
        return ActionResult.Success;
    }

    /// <summary>The room's leave option: the event is over for good, its screen goes, and the director says what follows (the map).</summary>
    private void Leave(Run run, RunEvent ev)
    {
        ev.Left(run);
        if (!ReferenceEquals(run.Top, this)) throw new InvalidOperationException("The event screen is not the one on top.");
        run.Pop();
        run.State.Part<EventsState>().Current = null;
        run.RoomFinished();
    }

    /// <summary>A rewards screen an option opened has closed: the event hears of it, as its option's code goes on in the game.</summary>
    public void AfterRewardsClosed(Run run, RewardsSet rewards)
    {
        Event(run).RewardsClosed(run, rewards);
        if (!run.IsOver) RefuseAPageNobodyCanLeave(run);
    }

    /// <summary>The house rule of the class comment. Only a page the player is looking at counts: under a fight or a rewards screen it is not read.</summary>
    private void RefuseAPageNobodyCanLeave(Run run)
    {
        if (!ReferenceEquals(run.Top, this)) return;
        var ev = Event(run);
        if (Views(run, ev).Any(view => view.Offered)) return;
        throw new InvalidOperationException(
            $"{ev.ClassName} came to a page on which nothing can be chosen ({string.Join(", ", ev.Options.Select(option => option.Describe()))}). " +
            "In the game the mod waits there for a human; the arena has none, so it does not go there.");
    }

    public override RunScreen Clone(CloneMap map) => new EventScreen();
}

/// <summary>
/// The fields an event class declares, found once per class: deck cards and relics are pointed
/// at the copy's objects when an event is copied, everything is written into the run's dump,
/// and a field that a copy would share is named, so that the self-check can fail on it.
/// </summary>
internal static class EventFields
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly ConcurrentDictionary<Type, FieldInfo[]> Cache = new();

    private static readonly Type[] Roots = [typeof(RunEvent), typeof(AncientEvent), typeof(object)];

    private static FieldInfo[] Find(Type type)
    {
        var found = new List<FieldInfo>();
        for (var t = type; t is not null && !Roots.Contains(t); t = t.BaseType) found.AddRange(t.GetFields(Declared));
        return found.OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
    }

    private static bool IsRunObject(Type type) => type == typeof(CardInstance) || typeof(Relic).IsAssignableFrom(type);

    public static void Remap(RunEvent copy, CloneMap map)
    {
        foreach (var field in Cache.GetOrAdd(copy.GetType(), Find))
        {
            if (!IsRunObject(field.FieldType)) continue;
            var old = field.GetValue(copy);
            if (old is not null && map.TryGet(old, out var mapped)) field.SetValue(copy, mapped);
        }
    }

    public static JsonObject Dump(RunEvent ev)
    {
        var json = new JsonObject();
        foreach (var field in Cache.GetOrAdd(ev.GetType(), Find))
        {
            json[field.Name] = field.GetValue(ev) switch
            {
                null => null,
                CardInstance card => $"card#{card.Id}",
                Relic relic => relic.ClassName,
                Delegate => "(function)",
                IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
                System.Collections.IEnumerable items and not string => string.Join(",", items.Cast<object?>().Select(item => item is CardInstance c ? $"card#{c.Id}" : Convert.ToString(item, CultureInfo.InvariantCulture))),
                var other => other.ToString(),
            };
        }
        return json;
    }

    /// <summary>
    /// Fields a copy of the event would share with the original: anything that is not a value,
    /// a text, a deck card, a relic or a definition, in a class that does not copy its fields
    /// itself (<see cref="RunEvent.OnCloned"/>). A function kept in a field counts: it would go
    /// on pointing at the event it was made on.
    /// </summary>
    public static IEnumerable<string> UnsafeFields(Type type)
    {
        var copiesItself = false;
        for (var t = type; t is not null && !Roots.Contains(t); t = t.BaseType)
            copiesItself |= t.GetMethod("OnCloned", Declared) is not null;
        if (copiesItself) yield break;

        foreach (var field in Find(type))
        {
            var ft = field.FieldType;
            if (ft.IsValueType || ft == typeof(string) || IsRunObject(ft)) continue;
            if (ft == typeof(CardDef) || ft == typeof(PotionDef)) continue; // shared, never changed
            yield return $"{type.Name}.{field.Name} ({ft.Name}) is a field a copy of the event would share: copy it in OnCloned";
        }
    }
}
