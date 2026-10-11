namespace SpireMind.Arena;

/// <summary>One fight to play: which scenario (and its place in the batch) and which model.
/// <paramref name="Run"/> is 0 for a whole fight, and for a position test the run's number
/// among the position's runs, from 1.</summary>
public sealed record FightSlot(int ScenarioIndex, Scenario Scenario, string Model, int Run = 0);

/// <summary>
/// The order a batch's fights start in. Two rules keep a batch that is cut short (a usage limit,
/// Ctrl+C) comparable across models:
/// <list type="bullet">
/// <item>Scenario by scenario: no fight of a scenario starts before every fight of the scenarios
/// before it has started, so no model gets more than one scenario ahead of another.</item>
/// <item>With no more fights at once than there are models, a model never plays two fights at
/// the same time, so the models share the subscription's capacity evenly and none slows itself down.</item>
/// </list>
/// Not thread-safe: the batch asks from one loop.
/// </summary>
public sealed class FightSchedule
{
    private readonly List<FightSlot> _waiting = [];
    private readonly bool _oneFightPerModel;

    /// <param name="skip">Fights to leave out, because the results already hold them.</param>
    public FightSchedule(IReadOnlyList<Scenario> scenarios, IReadOnlyList<string> models, int parallel,
        Func<Scenario, string, bool>? skip = null)
    {
        for (var s = 0; s < scenarios.Count; s++)
            foreach (var model in models)
            {
                Total++;
                if (skip?.Invoke(scenarios[s], model) == true) Skipped++;
                else _waiting.Add(new FightSlot(s, scenarios[s], model));
            }
        _oneFightPerModel = parallel <= models.Count;
    }

    /// <summary>
    /// The runs of a position test: every model plays the one position <paramref name="runs"/>
    /// times. They start run by run (no model's run 2 before every model's run 1 has started),
    /// which is the scenario-by-scenario rule with the run in the scenario's place, so a batch
    /// that is cut short has the same runs for every model.
    /// </summary>
    /// <param name="skip">Runs to leave out, because the results already hold them: model and run number.</param>
    public FightSchedule(Scenario scenario, IReadOnlyList<string> models, int runs, int parallel, Func<string, int, bool>? skip = null)
    {
        for (var run = 1; run <= runs; run++)
            foreach (var model in models)
            {
                Total++;
                if (skip?.Invoke(model, run) == true) Skipped++;
                else _waiting.Add(new FightSlot(run, scenario, model, run));
            }
        _oneFightPerModel = parallel <= models.Count;
    }

    /// <summary>Every fight of the batch, skipped ones included.</summary>
    public int Total { get; }

    /// <summary>Fights left out because they were already played.</summary>
    public int Skipped { get; }

    /// <summary>Fights not started yet.</summary>
    public int Waiting => _waiting.Count;

    /// <summary>
    /// The next fight to start while <paramref name="runningModels"/> are playing, taken off the
    /// list, or null when none may start now (or none is left).
    /// </summary>
    public FightSlot? Next(IReadOnlyCollection<string> runningModels)
    {
        if (_waiting.Count == 0)
            return null;
        // The list is in scenario order, so its head names the earliest scenario still to start.
        var scenario = _waiting[0].ScenarioIndex;
        for (var i = 0; i < _waiting.Count && _waiting[i].ScenarioIndex == scenario; i++)
        {
            if (_oneFightPerModel && runningModels.Contains(_waiting[i].Model))
                continue;
            var slot = _waiting[i];
            _waiting.RemoveAt(i);
            return slot;
        }
        return null;
    }
}
