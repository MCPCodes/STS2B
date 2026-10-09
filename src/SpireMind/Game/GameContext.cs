namespace SpireMind.Game;

/// <summary>
/// The handful of game objects almost every screen needs, captured once per call on the
/// main thread. Any of them can be null (main menu, between runs, loading).
/// </summary>
internal sealed class GameContext
{
    public RunState? Run { get; private init; }
    public Player? Me { get; private init; }
    public ICombatState? Combat { get; private init; }
    public PlayerCombatState? MyCombat { get; private init; }
    public NetGameType? NetType { get; private init; }

    public bool InRun => Run is not null && RunManager.Instance.IsInProgress;
    public bool InCombat => CombatManager.Instance?.IsInProgress == true;
    public bool IsMultiplayer => NetType is NetGameType.Host or NetGameType.Client;

    /// <summary>Players other than us, for co-op snapshots.</summary>
    public IEnumerable<Player> Teammates =>
        Run?.Players.Where(p => !LocalContext.IsMe(p)) ?? Enumerable.Empty<Player>();

    public static GameContext Capture()
    {
        var rm = RunManager.Instance;
        RunState? run = null;
        Player? me = null;
        NetGameType? netType = null;

        try
        {
            if (rm is not null && rm.IsInProgress)
            {
                run = rm.DebugOnlyGetState();
                netType = rm.NetService?.Type;
            }
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"reading run state: {ex.Message}");
        }

        if (run is not null)
        {
            // The IPlayerCollection overload throws when no player matches (before Launch).
            try { me = LocalContext.GetMe(run); }
            catch { me = null; }
        }

        ICombatState? combat = null;
        PlayerCombatState? myCombat = null;
        if (me is not null && CombatManager.Instance?.IsInProgress == true)
        {
            combat = me.Creature?.CombatState;
            myCombat = me.PlayerCombatState;
        }

        return new GameContext
        {
            Run = run,
            Me = me,
            Combat = combat,
            MyCombat = myCombat,
            NetType = netType,
        };
    }
}
