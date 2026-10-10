namespace SpireMind.Core.Coop;

/// <summary>
/// What a copy of the game says about itself to the co-op relay (GET /coop/info and
/// GET /chat): whether it is a bot copy, its port and instance id, the local player's NetId and
/// which brain plays. The game sets <see cref="NetId"/> on the main thread about once a second
/// and the bridge reads it on HTTP worker threads, so every member is safe from any thread.
/// </summary>
public sealed class InstanceInfo
{
    private readonly object _gate = new();
    private ulong? _netId;
    private Func<string?>? _playing;

    public InstanceInfo(bool isBot, int port, string instanceId)
    {
        IsBot = isBot;
        Port = port;
        InstanceId = string.IsNullOrWhiteSpace(instanceId) ? (isBot ? $"bot-{port}" : "main") : instanceId.Trim();
    }

    /// <summary>True for a copy started with --sts2bot=1, which plays its hand by itself.</summary>
    public bool IsBot { get; }

    public int Port { get; }

    /// <summary>"bot-17773" for a bot copy, "main" for the human's.</summary>
    public string InstanceId { get; }

    /// <summary>The local player's NetId in the lobby or run, else null.</summary>
    public ulong? NetId
    {
        get { lock (_gate) return _netId; }
        set { lock (_gate) _netId = value; }
    }

    /// <summary>Which brain plays in this copy: "api", "subscription" or "none". A null
    /// function or a null result means unknown. Read it through <see cref="ReadPlaying"/>.</summary>
    public Func<string?>? Playing
    {
        get { lock (_gate) return _playing; }
        set { lock (_gate) _playing = value; }
    }

    /// <summary>What <see cref="Playing"/> says now, or null when it is unset, says nothing or
    /// throws. Never throws.</summary>
    public string? ReadPlaying()
    {
        var playing = Playing;
        if (playing is null) return null;
        try
        {
            var value = playing()?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
