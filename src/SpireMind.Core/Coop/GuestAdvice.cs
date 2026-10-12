namespace SpireMind.Core.Coop;

/// <summary>
/// What a guest copy adds to the game's own "Disconnected" popup, and writes to its log: where
/// it dialed, and what to do about that failure. The guest is not technical and nobody can
/// look at their screen, so the popup itself has to say whether the mod is at work (the line
/// is there at all), which address it used, and the next step. Pure; the reasons are the names
/// of the game's NetError values.
/// </summary>
public static class GuestAdvice
{
    /// <summary>Starts every line a guest copy adds to a popup, so "is the SpireMind line there?" is a question a player can answer.</summary>
    public const string Lead = "SpireMind guest:";

    /// <summary>The line for the game's error popup: where this copy dials, as whom, and what to do.</summary>
    /// <param name="reason">The NetError's name, such as "Timeout".</param>
    public static string ForPopup(string? reason, GuestArgs args)
    {
        var where = args.CanDial
            ? $"{Lead} {args.HostLabel}'s game is at {args.Address}:{args.Port}; you are player {args.ClientId}."
            : $"{Lead} the launch options are not complete.";
        var what = For(reason, args);
        return what.Length == 0 ? where : $"{where} {what}";
    }

    /// <summary>What to do about one failure, or "" for a reason with nothing to add.</summary>
    public static string For(string? reason, GuestArgs args)
    {
        var host = args.HostLabel;
        // The same, to start a sentence with ("The host").
        var Host = char.ToUpperInvariant(host[0]) + host[1..];
        return reason switch
        {
            "Timeout" or "HandshakeTimeout" or "NoInternet" =>
                $"Nobody answered. Is {host} in the co-op character select? Are both computers on the same Wi-Fi? Is the address still right? Then Back and Join.",
            "ModMismatch" =>
                $"Your gameplay mods differ from {host}'s: BetterSpire2 must be the same version on both computers, and no other gameplay mod may be on.",
            "VersionMismatch" =>
                $"Your game version or your set of mods differs from {host}'s. Compare the version on the main menu, and switch off mods {host} does not have.",
            "LobbyFull" =>
                $"The lobby holds four players. {Host} has to send a bot away.",
            "RunInProgress" =>
                $"{Host}'s run has already started or is not closed yet. Wait for the new lobby, then Back and Join.",
            "Kicked" =>
                $"The lobby already holds a player {args.ClientId?.ToString() ?? "with your number"}. {Host} has to back out of the lobby and host again.",
            "NotInSaveGame" =>
                $"{Host} loaded a saved run that your player was not part of.",
            "Quit" =>
                $"{Host} left the lobby, or saved and quit.",
            "HostAbandoned" =>
                $"{Host} abandoned the run.",
            "StateDivergence" =>
                $"Your game and {host}'s fell out of step, so you were dropped. This run can't be rejoined.",
            "UnknownNetworkError" =>
                $"The connection to {host}'s game was lost. No bug report is needed: close this, then Back and Join once {host} hosts again.",
            "InternalError" =>
                "Something failed while joining. Back, then Join. If it keeps happening, the log has the reason.",
            _ => "",
        };
    }
}
