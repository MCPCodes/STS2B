namespace SpireMind.Arena;

/// <summary>
/// What a profile has unlocked, as far as the start of a run asks: which timeline epochs are
/// revealed (they gate relics, events, Ancients and the Underdocks act), which encounters it
/// has met (a boss never met is forced), how many runs it has finished, and which acts it has
/// discovered (an unlocked first act never entered is forced once). A run save carries the
/// first three under <c>players[].unlock_state</c>; the discovered acts are in the profile's
/// progress save.
/// </summary>
public sealed class RunUnlocks
{
    private readonly HashSet<string> _epochs;
    private readonly HashSet<string> _encountersSeen;
    private readonly HashSet<string> _discoveredActs;

    /// <summary>Revealed epochs by id ("RELIC1_EPOCH").</summary>
    public IReadOnlySet<string> Epochs => _epochs;

    /// <summary>Encounters met, by class name ("VantomBoss").</summary>
    public IReadOnlySet<string> EncountersSeen => _encountersSeen;

    /// <summary>Acts entered by a run that ended, by class name.</summary>
    public IReadOnlySet<string> DiscoveredActs => _discoveredActs;

    public int NumberOfRuns { get; }

    public RunUnlocks(IEnumerable<string> epochs, IEnumerable<string> encountersSeen, int numberOfRuns, IEnumerable<string> discoveredActs)
    {
        _epochs = new HashSet<string>(epochs, StringComparer.Ordinal);
        _encountersSeen = new HashSet<string>(encountersSeen, StringComparer.Ordinal);
        _discoveredActs = new HashSet<string>(discoveredActs, StringComparer.Ordinal);
        NumberOfRuns = numberOfRuns;
    }

    /// <summary>
    /// Everything unlocked: every epoch revealed, every encounter met, every act discovered,
    /// many runs behind it. This is what arena runs use. Mark's profile differs from it only in
    /// nine epochs of other characters, which change nothing for an Ironclad.
    /// </summary>
    public static RunUnlocks All { get; } = new(
        GameLists.Epochs,
        GameLists.Acts.SelectMany(a => a.Encounters).Select(e => e.ClassName),
        999_999_999,
        GameLists.Acts.Select(a => a.ClassName));

    public bool IsRevealed(string? epoch) => epoch is null || _epochs.Contains(epoch);

    public bool HasSeen(string encounterClassName) => _encountersSeen.Contains(encounterClassName);

    public bool IsUnlocked(GameAct act) => IsRevealed(act.UnlockEpoch);

    /// <summary>The relics of a pool this profile can meet, in the pool's order.</summary>
    public IEnumerable<string> Relics(IEnumerable<GameRelic> pool) => pool.Where(r => IsRevealed(r.Epoch)).Select(r => r.ClassName);
}
