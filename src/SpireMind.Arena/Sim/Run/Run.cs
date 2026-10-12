using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// Decides where a run goes: what its first screen is and what follows a won fight or a
/// finished room. The act as the game lays it out (Neow, the map, the rooms, the boss) is one
/// director; <see cref="FixedFights"/> is another. A director belongs to its run and is copied
/// with it, so it keeps what it counts in its own fields.
/// </summary>
public abstract class RunDirector
{
    /// <summary>The run has just been made: put up its first screen, or start its first fight.</summary>
    public abstract void Begin(Run run);

    /// <summary>
    /// A fight was won and has handed back: <see cref="Run.State"/> is the player as the fight
    /// left it, and the fight's screen is gone. Put up what follows (the rewards, the next room)
    /// or end the run. A lost fight never comes here: it ends the run.
    /// </summary>
    public abstract void AfterFightWon(Run run, FightOutcome fight);

    /// <summary>
    /// A room is finished: its proceed button, or an event's Leave, was used. In the game the
    /// map opens now, and after the boss the act ends. Screens call this through
    /// <see cref="Run.RoomFinished"/>.
    /// </summary>
    public virtual void AfterRoom(Run run) =>
        throw new NotSupportedException($"{GetType().Name} has no rule for what follows a finished room.");

    /// <summary>
    /// A copy for a copy of the run. The default copies the fields as they are, which is right
    /// for plain values and for lists that never change; a director with anything else copies
    /// it here. <paramref name="map"/> gives the copy's deck card or relic for one of the run's.
    /// </summary>
    public virtual RunDirector Clone(CloneMap map) => (RunDirector)MemberwiseClone();

    /// <summary>
    /// True for a director whose run keeps its choices (<see cref="Run.Choices"/>): every accepted
    /// action outside a fight, and every potion action inside one, with the floor, the screen and
    /// the label it was offered under. The act's director says yes, for its floor log; a run of
    /// anything else keeps none and reads no label.
    /// </summary>
    public virtual bool KeepsChoices => false;
}

/// <summary>How a run can end: the <c>state.result</c> of the end snapshot and <see cref="Run.Result"/>.</summary>
public static class RunResult
{
    /// <summary>The act's boss was beaten and its rewards were left.</summary>
    public const string Cleared = "cleared";

    /// <summary>A fight was lost: the player died, or the fight ran into the arena's turn limit.</summary>
    public const string Died = "died";

    /// <summary>The run met something the arena does not hold and could not go on as the game would. Not a loss; not scored.</summary>
    public const string Unrepresentable = "unrepresentable";

    /// <summary>The simulator itself failed. Says nothing about play.</summary>
    public const string Error = "error";
}

/// <summary>Thrown to unwind an action of the run that needs an answer the player has not given
/// yet. Nothing may catch it but <see cref="Run"/>.</summary>
internal sealed class RunQuestionPendingException : Exception;

/// <summary>
/// One run: the state that lasts from room to room (<see cref="RunState"/>), the screens the
/// player is looking at, and the fight while one is on. It answers <see cref="Snapshot"/> and
/// <see cref="Execute"/> like a <see cref="Fight"/>, with the mod's screens and action ids, and
/// it is decided by its setup and its actions alone: <see cref="Actions"/> is the save, and
/// <see cref="Replay"/> builds the run again from it.
/// <para>
/// The parts: Run.Fights (starting a fight from the run and taking it back), Run.Screens (what
/// a screen is, the end screens, and the blocks every snapshot shares: run, player, potions,
/// get_deck).
/// </para>
/// <para>
/// Copies. A fight copies itself before every action, so during a fight the run copies nothing.
/// For an action outside a fight the run takes a copy of itself first: an action that is
/// refused or throws leaves the run as it was, and an action that has to ask the player
/// something (<see cref="Ask"/>) is run again from that copy once the answer is known.
/// </para>
/// <para>
/// One exception is not a failure: <see cref="RunUnrepresentableException"/>, thrown by a roll
/// that can only land on something the arena has not built. Wherever it is thrown (an action
/// outside a fight, what follows a won fight, the director's first step) the run ends as
/// <see cref="RunResult.Unrepresentable"/> with the exception's text as the reason, standing
/// as it stood before that action. Nobody has to catch it.
/// </para>
/// </summary>
public sealed partial class Run
{
    /// <summary>The screen of the snapshot once the run is over; the same name a finished single fight uses.</summary>
    public const string OverScreen = Fight.OverScreen;

    /// <summary>The one action of the end snapshot. Running it succeeds and changes nothing.</summary>
    public const string DoneAction = Fight.DoneAction;

    private const string BeginAction = "(begin)";
    private const string AfterFightAction = "(after fight)";

    /// <summary>Everything that changes, in one object, so that the run can be put back whole.</summary>
    private sealed class Core
    {
        public required RunState State;
        public required RunDirector Director;
        public List<RunScreen> Screens = new();
        public string? Result, Reason;
        public FightOutcome? LastFight;
        public RngSet? LastFightRng;
        public int FightsStarted;

        public Core Clone(out CloneMap map)
        {
            var state = State.Clone(out map);
            var copy = new Core
            {
                State = state, Director = Director.Clone(map), Result = Result, Reason = Reason,
                LastFight = LastFight, LastFightRng = LastFightRng?.Clone(), FightsStarted = FightsStarted,
            };
            foreach (var screen in Screens) copy.Screens.Add(screen.Clone(map));
            return copy;
        }
    }

    private readonly RunSetup _setup;
    private Core _c;
    private List<string> _actions = new();

    // The choices among the actions, each with the label it was offered under: kept beside the
    // save, never in the state, so that an action that ends the run as it stood is still listed.
    private List<RunChoice> _choices = new();

    // An action outside a fight that is running, or waiting for an answer.
    private Core? _checkpoint;
    private string? _pendingAction;
    private int _pendingMark;
    private List<int[]?> _answers = new();
    private int _answerCursor;
    private RunScreen? _question;
    private bool _running;

    /// <summary>
    /// A new run. The director is copied: the run works on its own, so the one passed in can
    /// start another run, or the same one again for a replay.
    /// </summary>
    public Run(RunSetup setup, RunDirector director)
    {
        _setup = setup;
        _c = new Core { State = new RunState(setup), Director = director.Clone(new CloneMap()) };
        RunAction(BeginAction);
    }

    private Run(Run other)
    {
        _setup = other._setup;
        _c = other._c.Clone(out var map);
        _actions = other._actions.ToList();
        _choices = other._choices.ToList();
        _checkpoint = other._checkpoint?.Clone(out _);
        _pendingAction = other._pendingAction;
        _pendingMark = other._pendingMark;
        _answers = other._answers.ToList();
        _question = other._question?.Clone(map);
    }

    // ---------- reading ----------

    /// <summary>What the run was started with.</summary>
    public RunSetup Setup => _setup;

    /// <summary>
    /// The state that lasts from room to room. While a fight is on it is the player as the fight
    /// began; <see cref="Hp"/>, <see cref="Gold"/> and the describe methods give the live numbers.
    /// </summary>
    public RunState State => _c.State;

    public RunDirector Director => _c.Director;

    /// <summary>The screens that are open, the one the player sees last. A fight is one of them.</summary>
    public IReadOnlyList<RunScreen> Screens => _c.Screens;

    /// <summary>The screen the player sees, unless a question is open over it (<see cref="Question"/>).</summary>
    public RunScreen? Top => _c.Screens.Count > 0 ? _c.Screens[^1] : null;

    /// <summary>The screen of a question an action has asked and the player has not answered yet, or null.</summary>
    public RunScreen? Question => _question;

    /// <summary>
    /// The fight that is on, or null. It is the live fight: look, or copy it with
    /// <see cref="Fight.Clone"/> or <see cref="Fight.CloneDeterminized"/> and play the copy; never
    /// act on it directly, or the run does not hear of it.
    /// </summary>
    public Fight? Fight => (Top as FightScreen)?.Fight;

    /// <summary>The last fight that ended, won or lost; null before the first.</summary>
    public FightOutcome? LastFight => _c.LastFight;

    /// <summary>
    /// The random streams of the last fight that ended, as it left them: for a caller that lent
    /// the fight the run's own long streams (<see cref="FightRequest.Rng"/>) and takes them back.
    /// The object that was lent is not the one to read: a fight works on copies of its state.
    /// </summary>
    public RngSet? LastFightRng => _c.LastFightRng;

    /// <summary>How the run ended (one of <see cref="RunResult"/>), or null while it goes on.</summary>
    public string? Result => _c.Result;

    /// <summary>What the result needs said about it (the turn limit, what was missing, what failed), or null.</summary>
    public string? ResultReason => _c.Reason;

    /// <summary>The run is decided. After a death one screen is still to come before the end
    /// snapshot (<c>game_over</c>, as in the game); see <see cref="AtEnd"/>.</summary>
    public bool IsOver => _c.Result is not null;

    /// <summary>The end snapshot is up: nothing is left to do but <see cref="DoneAction"/>.</summary>
    public bool AtEnd => Top is ArenaOverScreen;

    /// <summary>
    /// Every action the run accepted, in order: the save. The run is decided by its setup, its
    /// director and these ids; <see cref="Replay"/> builds it again. <see cref="DoneAction"/> at
    /// the end changes nothing and is not kept.
    /// </summary>
    public IReadOnlyList<string> Actions => _actions;

    /// <summary>
    /// The choices of the run so far, in order, when its director keeps them
    /// (<see cref="RunDirector.KeepsChoices"/>): every accepted action outside a fight (a path, a
    /// reward, a card, an event option, a purchase, a rest site option, the steps of a card
    /// choice) and every potion used or thrown away inside one, each with the total floor and
    /// the screen it was taken on and the label the snapshot offered it under. It is a view of
    /// <see cref="Actions"/>: a replay builds the same list, and an action that was taken back
    /// is not in it. The plays of a fight are not choices of this list.
    /// </summary>
    public IReadOnlyList<RunChoice> Choices => _choices;

    /// <summary>The snapshot of the run as it stands: the open question, else the top screen.</summary>
    public GameSnapshot Snapshot() => Showing.Snapshot(this);

    /// <summary>Every action the player may take now, in the order the snapshot lists them.</summary>
    public IReadOnlyList<LegalAction> LegalActions() => Showing.LegalActions(this);

    private RunScreen Showing => _question ?? Top ?? throw new InvalidOperationException("The run has no screen: its director put none up.");

    // ---------- doing ----------

    /// <summary>
    /// Runs one action by id on the screen the player sees. An id that is not offered is refused
    /// with a reason and changes nothing. An accepted id is added to <see cref="Actions"/>.
    /// </summary>
    public ActionResult Execute(string? actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return ActionResult.Fail("No action id was given.");

        if (_question is { } question)
        {
            // A question's own steps (a pick, a preview, backing out of one) change only the
            // question. Its last step calls Answer, which runs the waiting action again.
            var step = Read(question, actionId);
            var answered = question.Execute(this, actionId);
            if (answered.Ok) Accept(actionId, step);
            return answered;
        }

        var screen = Showing;
        if (screen is ArenaOverScreen)
        {
            return actionId == DoneAction
                ? ActionResult.Success
                : ActionResult.Fail($"The run is over; '{actionId}' cannot run. The only action left is '{DoneAction}'.");
        }

        if (screen.CopiesItself)
        {
            // A fight: it copies itself before every action, so the run takes no copy of its own.
            // Of a fight's actions only the potions are choices of the run's list.
            var potion = IsPotionAction(actionId) ? Read(screen, actionId) : null;
            var result = screen.Execute(this, actionId);
            if (result.Ok) Accept(actionId, potion);
            return result;
        }

        var choice = Read(screen, actionId);
        var outcome = RunAction(actionId);
        if (outcome.Ok) Accept(actionId, choice);
        return outcome;
    }

    private static bool IsPotionAction(string actionId) =>
        actionId.StartsWith("potion:", StringComparison.Ordinal) || actionId.StartsWith("discard_potion:", StringComparison.Ordinal);

    /// <summary>
    /// What a choice looks like before it is taken: the floor and the screen the player stands on
    /// and the label the screen offers the id under (the id itself for one it does not list).
    /// Null when the run's director keeps no choices.
    /// </summary>
    private RunChoice? Read(RunScreen screen, string actionId)
    {
        if (!_c.Director.KeepsChoices) return null;
        var label = screen.LegalActions(this).FirstOrDefault(action => action.Id == actionId)?.Label ?? actionId;
        return new RunChoice(_actions.Count, _c.State.TotalFloor, screen.Name, actionId, label);
    }

    /// <summary>An action was accepted: it joins the save, and the choices when it is one.</summary>
    private void Accept(string actionId, RunChoice? choice)
    {
        // The place in the save is read now: an action that asked a question was put back and run again since.
        if (choice is not null) _choices.Add(choice with { Action = _actions.Count });
        _actions.Add(actionId);
    }

    /// <summary>An action outside a fight: the run is copied first, then the action runs on the live state.</summary>
    private ActionResult RunAction(string actionId)
    {
        _checkpoint = _c.Clone(out _);
        _pendingAction = actionId;
        _pendingMark = _actions.Count;
        _answers = new List<int[]?>();
        return Perform();
    }

    /// <summary>Runs the waiting action on the live state with the answers known so far.</summary>
    private ActionResult Perform()
    {
        var actionId = _pendingAction!;
        var again = _answers.Count > 0;
        _question = null;
        _answerCursor = 0;
        _running = true;
        try
        {
            var result = ActionResult.Success;
            switch (actionId)
            {
                case BeginAction:
                    _c.Director.Begin(this);
                    break;
                case AfterFightAction:
                    _c.Director.AfterFightWon(this, _c.LastFight!);
                    break;
                default:
                    result = Showing.Execute(this, actionId);
                    break;
            }
            // A refused action has changed nothing; the copy makes that certain.
            if (!result.Ok) Undo();
            Settle();
            return result;
        }
        catch (RunQuestionPendingException)
        {
            // The state stays as it was when the question came up: that is what the question's screen shows.
            return ActionResult.Success;
        }
        catch (RunUnrepresentableException missing)
        {
            // A roll could only land on something the arena has not built, or strict mode met such
            // a thing (LeftOutLog): the run cannot go on as the game would. It ends here, as it stood
            // before the action, with the reason. Unlike a failure the action stays in the save
            // (the caller adds it; a question's steps are there already), so a replay ends the same way.
            _c = _checkpoint!;
            Settle();
            _question = null;
            End(RunResult.Unrepresentable, missing.Message);
            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            // A rule threw: the run is put back as it was before the action, so it is still in one piece.
            Undo();
            Settle();
            _question = null;
            if (actionId == AfterFightAction || (actionId == BeginAction && again))
            {
                // Nothing is behind these to go back to: the fight is over and its screen is gone
                // (or the run has not put up its first screen). A run that cannot go on says so and stops.
                var where = actionId == BeginAction ? "at the start of the run" : $"after the fight on floor {_c.LastFight!.Floor}";
                End(RunResult.Error, $"{where}: {ex.GetType().Name}: {ex.Message}");
                return ActionResult.Success;
            }
            throw;
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>Puts the run back as it was before the waiting action, and takes out of the save
    /// what the action had already put there (the action's own id and the steps of its questions).</summary>
    private void Undo()
    {
        _c = _checkpoint!;
        // What follows a fight is not in the save by itself; the fight's last action is, and it stays.
        if (_pendingAction is not (AfterFightAction or BeginAction) && _actions.Count > _pendingMark)
        {
            _actions.RemoveRange(_pendingMark, _actions.Count - _pendingMark);
            _choices.RemoveAll(choice => choice.Action >= _pendingMark);
        }
    }

    private void Settle()
    {
        _checkpoint = null;
        _pendingAction = null;
        _answers = new List<int[]?>();
    }

    // ---------- questions (the run-level card choice and its like) ----------

    /// <summary>
    /// Asks the player something in the middle of an action outside a fight (which card to
    /// remove, which bundle to take) and returns the answer, as if the call had simply waited:
    /// the indexes the player chose among what <paramref name="question"/> offered, or null when
    /// the player backed out.
    /// <para>
    /// Underneath it works as a card choice does in a fight. The first time the answer is not
    /// known the action is abandoned (by an exception nothing may catch) and the run shows the
    /// question's screen; once that screen has called <see cref="Answer"/>, the whole action is
    /// run again from the copy taken before it began, and this time the call returns. The run is
    /// deterministic, so the second run is the first one continued. That only holds if the
    /// action keeps all its state in the run: in <see cref="RunState"/>, its parts, the screens
    /// and the director, never in statics or captured variables.
    /// </para>
    /// </summary>
    /// <param name="question">The screen that shows the question. It is only shown when the answer is not known yet.</param>
    public int[]? Ask(RunScreen question)
    {
        if (!_running)
            throw new InvalidOperationException("A question was asked outside an action of the run; the arena cannot show it.");
        if (_answerCursor < _answers.Count) return _answers[_answerCursor++];
        _question = question;
        throw new RunQuestionPendingException();
    }

    /// <summary>
    /// The open question is answered; its screen calls this from its last step. The action that
    /// asked starts again from before it began, now with the answer. Null says the player backed out.
    /// </summary>
    public ActionResult Answer(int[]? answer)
    {
        if (_question is null || _checkpoint is null || _pendingAction is null)
            throw new InvalidOperationException("No question is open.");
        _answers.Add(answer);
        _c = _checkpoint.Clone(out _);
        return Perform();
    }

    // ---------- screens ----------

    /// <summary>Puts a screen on top: the player sees it from now on.</summary>
    public void Push(RunScreen screen) => _c.Screens.Add(screen);

    /// <summary>Closes the top screen and returns it; the one under it shows again.</summary>
    public RunScreen Pop()
    {
        if (_c.Screens.Count == 0) throw new InvalidOperationException("No screen is open.");
        var top = _c.Screens[^1];
        _c.Screens.RemoveAt(_c.Screens.Count - 1);
        return top;
    }

    /// <summary>Closes every screen (a room is left behind).</summary>
    public void CloseAll() => _c.Screens.Clear();

    /// <summary>A room is finished (its proceed button, or an event's Leave): the director says what follows.</summary>
    public void RoomFinished() => _c.Director.AfterRoom(this);

    // ---------- the end ----------

    /// <summary>
    /// Ends the run here and puts up the end snapshot. <paramref name="result"/> is one of
    /// <see cref="RunResult"/> (a lost fight ends the run by itself, as <see cref="RunResult.Died"/>);
    /// <paramref name="reason"/> says what the result needs said, in one line.
    /// </summary>
    public void End(string result, string? reason = null)
    {
        _c.Result = result;
        _c.Reason = reason;
        _c.Screens.Clear();
        _c.Screens.Add(new ArenaOverScreen());
    }

    // ---------- copies and the save ----------

    /// <summary>
    /// A deep copy of the run as it stands, also in the middle of a fight, of a card choice in a
    /// fight, or of a question. Playing the copy never changes this run, and both go on
    /// identically from the same actions.
    /// </summary>
    public Run Clone() => new(this);

    /// <summary>
    /// Builds a run again from its save: a new run from the same setup and director, with every
    /// action applied in order.
    /// </summary>
    /// <exception cref="InvalidOperationException">An action was refused: the list is not of this run, or the simulator changed since.</exception>
    public static Run Replay(RunSetup setup, RunDirector director, IEnumerable<string> actions)
    {
        var run = new Run(setup, director);
        var n = 0;
        foreach (var actionId in actions)
        {
            var result = run.Execute(actionId);
            if (!result.Ok)
                throw new InvalidOperationException($"The save does not fit the run: action {n} ('{actionId}') was refused: {result.Error}");
            n++;
        }
        return run;
    }

    /// <summary>
    /// Everything about the run as one text: the state with every field, the screens, the result,
    /// and where the open fight stands. Equal texts mean two runs are in the same state.
    /// </summary>
    public string Dump()
    {
        var json = _c.State.Dump();
        json["result"] = _c.Result;
        json["reason"] = _c.Reason;
        json["fights_started"] = _c.FightsStarted;
        json["last_fight"] = _c.LastFight?.ToString();
        json["screens"] = string.Join(" > ", _c.Screens.Select(s => s.Name));
        json["question"] = _question?.Name;
        json["snapshot"] = Snapshot().ToJson();
        if (Fight is { } fight)
        {
            json["fight_rng"] = string.Join(",", new[]
            {
                fight.Rng.Shuffle, fight.Rng.MonsterAi, fight.Rng.Niche, fight.Rng.CombatTargets, fight.Rng.CombatCardSelection,
                fight.Rng.CombatCardGeneration, fight.Rng.CombatPotionGeneration, fight.Rng.CombatEnergyCosts, fight.Rng.Encounter,
            }.Select(stream => stream.Counter));
            json["fight_draw_order"] = string.Join(",", fight.DrawPile.Select(card => $"{card.Title}#{card.Id}<{card.DeckCardId}"));
        }
        return json.ToJsonString();
    }
}
