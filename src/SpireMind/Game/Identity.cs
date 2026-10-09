namespace SpireMind.Game;

/// <summary>
/// Keeps <see cref="ChatHub.BotName"/> on the character the local player is playing, so the
/// bot answers to "Ironclad" in an Ironclad run. Between runs the name is cleared, so a message
/// to the character picked next ("Silent, go poison this run") still reaches this bot.
/// </summary>
internal static class Identity
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private static DateTime _nextRead;
    private static string? _lastTitle;

    /// <summary>Called every frame on the main thread; reads the game at most once a second.</summary>
    public static void Poll(ChatHub hub)
    {
        var now = DateTime.UtcNow;
        if (now < _nextRead) return;
        _nextRead = now + Interval;

        var (inRun, title) = Read();
        if (!inRun)
        {
            if (_lastTitle is null) return;
            _lastTitle = null;
            hub.BotName = null;
            ModEntry.Log("chat: no run, answering to any name");
            return;
        }

        if (string.IsNullOrEmpty(title) || title == _lastTitle) return;
        _lastTitle = title;
        hub.BotName = title; // the hub drops a leading "The "
        ModEntry.Log($"chat: answering as {hub.BotName}");
    }

    /// <summary>
    /// Whether a run is being played and the local player's character title. A failed read
    /// counts as in a run with no title, so the name stays put while a run loads or tears down.
    /// </summary>
    private static (bool InRun, string? Title) Read()
    {
        try
        {
            // The main menu wins over RunManager, as in ScreenRouter: a run being set up at
            // character select already counts as in progress, still with no player to read.
            var ctx = GameContext.Capture();
            if (!ctx.InRun || NGame.Instance?.MainMenu is not null) return (false, null);
            return (true, ctx.Me is { } me ? Describe.Text(me.Character?.Title) : null);
        }
        catch
        {
            // Loading or tearing down a run; try again next second.
            return (true, null);
        }
    }
}
