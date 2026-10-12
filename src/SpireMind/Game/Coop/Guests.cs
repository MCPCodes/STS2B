// Aliases rather than the namespaces, so a game type of the same name in the global usings can
// never make them ambiguous.
using CoopSeats = SpireMind.Core.Coop.CoopSeats;
using InstanceRole = SpireMind.Core.Brain.InstanceRole;

namespace SpireMind.Game.Coop;

/// <summary>
/// What this copy calls the guests of a local co-op session: the people who joined from
/// another computer (docs/coop-guest.md). The host sets it, in the file
/// <c>~/.config/spiremind/guest_name</c> or in SPIREMIND_GUEST_NAME; his own copy and the bot
/// copies read the same two places, so the nameplates in his window and the names the bot is
/// told agree. A guest's own copy never calls <see cref="Start"/> and so reads no file.
///
/// The file is looked at again every few seconds, so a name written while the game runs
/// shows without a restart. Main thread; never throws.
/// </summary>
internal static class Guests
{
    private static readonly TimeSpan ReadEvery = TimeSpan.FromSeconds(5);

    private static string? _sharedDir;
    private static IReadOnlyList<string> _names = [];
    private static DateTime _readAt = DateTime.MinValue;
    private static string? _logged;

    /// <summary>Says where the host's settings are. Call once at mod load in the host's copy and in a bot copy.</summary>
    public static void Start(InstanceRole? role)
    {
        _sharedDir = role?.SharedConfigDir;
        _readAt = DateTime.MinValue;
    }

    /// <summary>The guests' names in seat order; empty when the host named nobody.</summary>
    public static IReadOnlyList<string> Names
    {
        get
        {
            var now = DateTime.UtcNow;
            if (now - _readAt < ReadEvery) return _names;
            _readAt = now;
            _names = CoopSeats.ReadNames(System.Environment.GetEnvironmentVariable, _sharedDir);
            var line = _names.Count == 0 ? "" : string.Join(", ", _names);
            if (line != _logged)
            {
                var first = _logged is null;
                _logged = line;
                // Once, and again only when it changes; nothing when nobody was ever named.
                if (!first || line.Length > 0)
                    ModEntry.Log(line.Length > 0
                        ? $"coop: names: a player who joins from another computer is called {line} ({CoopSeats.GuestNameEnvVar}, or the file {CoopSeats.GuestNameFile} in the shared config folder)"
                        : $"coop: names: no name for a guest any more; one shows as {CoopSeats.DefaultGuestName}");
            }
            return _names;
        }
    }

    /// <summary>The name of the nth guest of a session, from 0, in seat order.</summary>
    public static string Name(int nth) => CoopSeats.GuestName(Names, nth);
}
