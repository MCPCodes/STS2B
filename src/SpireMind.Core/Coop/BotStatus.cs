namespace SpireMind.Core.Coop;

/// <summary>
/// Where a bot copy stands in co-op, as it reports itself on /coop/info and /chat for /bots in
/// the human's window. Line is the lobby driver's one line for a human ("joined; picked Silent;
/// ready; waiting for the host to start"); Character is the game's id of the character it holds
/// or plays ("SILENT"), null before its first lobby; InRun is true while it plays a co-op run;
/// JoinStopped is true once a failure that retrying can't fix stopped its attempts to join.
/// </summary>
public sealed record BotStatus(string? Line, string? Character, bool InRun, bool JoinStopped);
