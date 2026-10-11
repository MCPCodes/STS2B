namespace SpireMind.Core.Coop;

/// <summary>
/// What a copy of the game says about itself to the co-op relay (GET /coop/info and
/// GET /chat): whether it is a bot copy, its port and instance id, the local player's NetId,
/// which brain plays and, in a bot copy, where it stands in the lobby. A bot copy also takes two
/// requests from the relay through it: quit (POST /coop/quit) and look for the lobby now (POST
/// /coop/join-now). The game sets <see cref="NetId"/> on the main thread about once a second
/// and the bridge reads it on HTTP worker threads, so every member is safe from any thread.
/// </summary>
public sealed class InstanceInfo
{
    private readonly object _gate = new();
    private ulong? _netId;
    private Func<string?>? _playing;
    private Func<BotStatus?>? _status;
    private Func<string, string>? _quit;
    private Action? _retryJoin;

    public InstanceInfo(bool isBot, int port, string instanceId)
    {
        IsBot = isBot;
        Port = port;
        InstanceId = string.IsNullOrWhiteSpace(instanceId) ? (isBot ? $"bot-{port}" : "main") : instanceId.Trim();
    }

    /// <summary>True for a copy started with --sts2bot=1 and without Steam, which plays its hand
    /// by itself (<see cref="SpireMind.Core.Brain.InstanceRole.IsBot"/>).</summary>
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

    /// <summary>Where this bot copy stands in co-op; null in the human's copy. Called on HTTP
    /// worker threads, so it must return at once and not touch the game. Read it through
    /// <see cref="ReadStatus"/>.</summary>
    public Func<BotStatus?>? Status
    {
        get { lock (_gate) return _status; }
        set { lock (_gate) _status = value; }
    }

    /// <summary>What <see cref="Status"/> says now, or null when it is unset, says nothing or
    /// throws. Never throws.</summary>
    public BotStatus? ReadStatus()
    {
        var status = Status;
        if (status is null) return null;
        try
        {
            return status();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// In a bot copy: asks it to leave the lobby or run and quit, for /bot stop in the human's
    /// window. Takes who asked and returns a line for the human's chat. Called on an HTTP worker
    /// thread, so it must return at once and hand the game work to the main thread. Null means
    /// this copy can't be asked (POST /coop/quit then answers 501).
    /// </summary>
    public Func<string, string>? Quit
    {
        get { lock (_gate) return _quit; }
        set { lock (_gate) _quit = value; }
    }

    /// <summary>
    /// In a bot copy: look for the host's lobby now rather than at the next retry, for when the
    /// human's copy has just opened one. Called on an HTTP worker thread, so it must return at
    /// once and hand the game work to the main thread. Null means POST /coop/join-now answers 501.
    /// </summary>
    public Action? RetryJoin
    {
        get { lock (_gate) return _retryJoin; }
        set { lock (_gate) _retryJoin = value; }
    }
}
