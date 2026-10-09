using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SpireMind.Core;

/// <summary>
/// Gives each distinct snapshot a monotonically increasing version so the bot can tell
/// "nothing changed yet" apart from "new decision", and so a stale action id can be
/// rejected instead of doing something unintended.
/// </summary>
public sealed class SnapshotVersioner
{
    private readonly object _gate = new();
    private string? _lastHash;
    private long _version;

    public GameSnapshot Stamp(GameSnapshot snapshot)
    {
        var hash = Hash(snapshot);
        lock (_gate)
        {
            if (hash != _lastHash)
            {
                _lastHash = hash;
                _version++;
            }
            snapshot.Version = _version;
        }
        return snapshot;
    }

    public long Current
    {
        get { lock (_gate) return _version; }
    }

    private static string Hash(GameSnapshot s)
    {
        var json = new JsonObject
        {
            ["screen"] = s.Screen,
            ["awaiting"] = s.AwaitingDecision,
            ["state"] = s.State.DeepClone(),
            ["actions"] = new JsonArray(s.Actions.Select(a => (JsonNode)JsonValue.Create(a.Id)!).ToArray()),
        };
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json.ToJsonString()));
        return Convert.ToHexString(bytes);
    }
}
