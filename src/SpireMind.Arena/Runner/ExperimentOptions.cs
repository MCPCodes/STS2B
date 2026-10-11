namespace SpireMind.Arena;

/// <summary>
/// The switches of an experiment that the simulator acts on, handed to a fight when it is made
/// (<see cref="ArenaGame.Experiments"/>). Each is off unless <c>arena run</c> (or
/// <c>arena position</c>) turns it on, and with all of them off a fight is exactly the fight it
/// was before there were any. The runner only passes them on and names them in the run's label
/// (<see cref="ExperimentLabel"/>); what a switch shows a model, and where the real mod would
/// get that from in the real game, is said where the simulator implements it.
/// </summary>
/// <param name="Forecast"><c>--forecast</c>.</param>
public sealed record ExperimentOptions(bool Forecast = false);
