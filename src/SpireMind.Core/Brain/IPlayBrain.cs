namespace SpireMind.Core.Brain;

/// <summary>
/// A brain that plays the game by itself: <see cref="ApiBrain"/> on the API credit, or the
/// subscription fallback that runs Claude Code headless. <see cref="BrainController"/> picks
/// which one plays. See docs/brain.md.
/// </summary>
public interface IPlayBrain
{
    /// <summary>True from <see cref="Start"/> until it stops (already false while
    /// <see cref="Stopped"/> is raised).</summary>
    bool IsRunning { get; }

    /// <summary>Start playing on a background task. False when already running.</summary>
    bool Start();

    /// <summary>Stop playing and wait until it has. Called from a <see cref="Stopped"/> handler
    /// it only asks the brain to stop.</summary>
    Task StopAsync();

    /// <summary>Raised once per start when the brain stops, with the reason and a line saying
    /// why. A brain announces its own stops in the chat itself, so a handler shouldn't post the
    /// line again; <see cref="BrainStopReason.UserStop"/> is never announced.</summary>
    event Action<BrainStopReason, string>? Stopped;
}
