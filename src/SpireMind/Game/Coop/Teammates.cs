using System.Text.Json.Nodes;
// An alias rather than the namespace, so a game type of the same name in the global usings can
// never make it ambiguous.
using InstanceRole = SpireMind.Core.Brain.InstanceRole;

namespace SpireMind.Game.Coop;

/// <summary>
/// The players of a co-op run as the snapshots show them: who they are, how they stand, what
/// they have voted for, and which of them a human plays. Main thread only, and read-only:
/// every copy simulates every player (sts2-notes/coop-api-v0.107.1/players.md, section 3), so
/// these are plain getters that send nothing and answer the same on the host and on a client.
///
/// Everything co-op in the readers starts with <see cref="Active"/>, which is false in a solo
/// run, so a solo snapshot never reaches this file. Deferring to a human (waiting for their
/// vote, following it) is a bot copy's behaviour only: <see cref="Followed"/> is empty until
/// <see cref="Start"/> has been told this copy is a bot's. A read that throws is logged once,
/// and that one feature stays off for the rest of the run (<see cref="Safe{T}"/>).
/// </summary>
internal static class Teammates
{
    // ---------- this copy's role ----------

    /// <summary>
    /// True on a bot's copy of the game (started with --sts2bot=1), where the co-op manners
    /// apply. False on the human's own copy and until <see cref="Start"/> is called.
    /// </summary>
    public static bool BotCopy { get; private set; }

    /// <summary><see cref="Start"/> has run. Until it has, a co-op run says so once in the log.</summary>
    private static bool _started;
    private static bool _notStartedLogged;

    /// <summary>
    /// Tells this file which copy it runs in. Call once at mod load, with the role ModEntry
    /// detected. It only stores the answer and logs it; nothing here touches the game, so any
    /// thread may call it and it cannot throw.
    /// </summary>
    public static void Start(InstanceRole? role)
    {
        try
        {
            BotCopy = role?.IsBot == true;
            _started = true;
            ModEntry.Log(BotCopy
                ? $"coop: manners on: in a co-op run this bot copy follows the human's map, event and chest votes; it waits {Shown(CoopManners.Patience)} for one before it chooses itself ({CoopManners.WaitEnv})"
                : "coop: manners off in this copy (not a bot copy): co-op snapshots carry the team and the votes, but nobody is followed");
        }
        catch
        {
            // The log is all that can fail here.
        }

        static string Shown(TimeSpan patience) =>
            patience == TimeSpan.MaxValue ? "for as long as it takes" : $"{patience.TotalSeconds:0} s";
    }

    /// <summary>One player. <see cref="Slot"/> is the index in RunState.Players, the same on every copy, and the "p&lt;n&gt;" of combat targets.</summary>
    internal sealed record Mate(int Slot, Player Player, string Name, string Character, bool IsMe, bool IsHost, bool IsHuman)
    {
        /// <summary>True for a teammate whose choices this copy's bot follows: a human, seen from a bot's copy.</summary>
        public bool Followed => IsHuman && BotCopy;

        public string Id => $"p{Slot}";

        public ulong NetId => Player.NetId;

        /// <summary>"Mark (p0)": the name with the id that targets them.</summary>
        public string Label => $"{Name} ({Id})";
    }

    /// <summary>Where a teammate is looking, as far as their last screen report says (one unreliable message per change).</summary>
    internal enum Presence
    {
        /// <summary>Their map is open: where a player waits after voting.</summary>
        AtMap,

        /// <summary>In the room itself: a shop, a rest site, an event, or a rewards screen that is done.</summary>
        InRoom,

        /// <summary>On a screen of their own: unfinished rewards, a card choice, a deck view, settings.</summary>
        Busy,

        /// <summary>No report yet, or it could not be read.</summary>
        Unknown,
    }

    // ---------- is this co-op at all ----------

    /// <summary>
    /// True in a multiplayer run with more than one player. False in a solo run, at the menus
    /// and in the game's own "fake multiplayer" test setup (dummy players on a singleplayer
    /// service), where nobody else acts and the solo behaviour is the right one.
    /// </summary>
    public static bool Active(GameContext ctx) =>
        ctx.IsMultiplayer && ctx.Me is not null && ctx.Run is { } run && run.Players.Count > 1;

    // ---------- the players ----------

    // One capture asks several times (the team list, the vote, the room note); the context is
    // made fresh for every capture, so the list is kept for as long as the same one asks.
    private static GameContext? _listedFor;
    private static List<Mate> _listed = new();

    /// <summary>Every player in slot order, the local one included. Empty outside a co-op run. Don't change the list.</summary>
    public static IReadOnlyList<Mate> All(GameContext ctx)
    {
        if (ReferenceEquals(ctx, _listedFor)) return _listed;

        var mates = new List<Mate>();
        if (Active(ctx))
        {
            if (!_started && !_notStartedLogged)
            {
                // The one line that explains a bot that votes without waiting for anybody.
                _notStartedLogged = true;
                ModEntry.Log("coop: manners: Teammates.Start(role) was never called at mod load (ModEntry.Initialize, after DetectRole), " +
                    "so this copy follows nobody: a bot copy here votes without waiting for the human");
            }

            var host = HostNetId();
            var enet = IsEnet();
            var players = ctx.Run!.Players;
            for (var slot = 0; slot < players.Count; slot++)
            {
                var p = players[slot];
                var me = LocalContext.IsMe(p);
                var isHost = host == p.NetId;
                // An ENet session is local to this Mac: its host is the human's own game and every
                // other player is a bot copy (players.md 1.1). Over Steam the others are people.
                var human = !me && (!enet || isHost);
                mates.Add(new Mate(slot, p, NameOf(p, isHost, enet), CharacterName(p), me, isHost, human));
            }
        }

        // Kept only once it is whole: a read that threw half way must not leave a short list behind.
        _listedFor = ctx;
        _listed = mates;
        return mates;
    }

    public static List<Mate> Others(GameContext ctx) => All(ctx).Where(m => !m.IsMe).ToList();

    /// <summary>
    /// The teammates whose choices the bot follows: the ones a person plays, on a bot's copy.
    /// Empty on the human's own copy, where nobody defers to anybody, so every rule in
    /// <see cref="CoopManners"/> then answers "choose as in a solo run".
    /// </summary>
    public static List<Mate> Followed(GameContext ctx) => All(ctx).Where(m => m.Followed).ToList();

    /// <summary>"Mark (p0)", or "Mark (p0) and Sam (p2)": who the bot defers to, for hint sentences.</summary>
    public static string Names(IEnumerable<Mate> mates)
    {
        var labels = mates.Select(m => m.Label).ToList();
        return labels.Count switch
        {
            0 => "the others",
            1 => labels[0],
            _ => $"{string.Join(", ", labels.Take(labels.Count - 1))} and {labels[^1]}",
        };
    }

    // ---------- the snapshot's "team" list ----------

    /// <summary>
    /// The compact "team" list: every other player in slot order with name, character, NetId,
    /// HP, Block, dead, host, human, and either "ended_turn" (in a fight) or "map_vote" (outside
    /// one, once cast). Null outside a co-op run or when the read failed.
    /// </summary>
    public static JsonArray? Team(GameContext ctx)
    {
        if (!Active(ctx)) return null;
        return Safe<JsonArray?>("the team list", null, () =>
        {
            var team = new JsonArray();
            foreach (var mate in Others(ctx)) team.Add(Json(mate, ctx));
            return team;
        });
    }

    /// <summary>One "team" entry; readers add what only their screen knows (powers, incoming damage).</summary>
    public static JsonObject Json(Mate m, GameContext ctx)
    {
        var creature = m.Player.Creature;
        var json = new JsonObject
        {
            ["id"] = m.Id,
            ["name"] = m.Name,
            ["character"] = m.Character,
            ["net_id"] = m.NetId,
            ["hp"] = creature.CurrentHp,
            ["max_hp"] = creature.MaxHp,
        };
        if (creature.Block > 0) json["block"] = creature.Block;
        if (creature.IsDead) json["dead"] = true;
        if (m.IsHost) json["host"] = true;
        if (m.IsHuman) json["human"] = true;

        if (ctx.InCombat)
            json["ended_turn"] = EndedTurn(m.Player);
        else if (MapVote(m.Player) is { } vote)
            json["map_vote"] = $"({vote.col},{vote.row})";
        return json;
    }

    // ---------- what the others are doing ----------

    /// <summary>The node a player has voted to travel to, or null. The votes are cleared at every move.</summary>
    public static MapCoord? MapVote(Player p)
    {
        try { return RunManager.Instance?.MapSelectionSynchronizer?.GetVote(p)?.coord; }
        catch { return null; }
    }

    /// <summary>True once a player has ended this turn; the game also sets dead players ready.</summary>
    public static bool EndedTurn(Player p)
    {
        try { return CombatManager.Instance is { IsInProgress: true } cm && cm.IsPlayerReadyToEndTurn(p); }
        catch { return false; }
    }

    /// <summary>
    /// Where a teammate is looking. Only ever asked for players of this run: for an unknown id
    /// the game would create an input state and a phantom cursor (players.md 3.1, traps).
    /// </summary>
    public static Presence Where(Mate m)
    {
        NetScreenType screen;
        try
        {
            if (RunManager.Instance?.InputSynchronizer is not { } input) return Presence.Unknown;
            screen = input.GetScreenType(m.NetId);
        }
        catch
        {
            return Presence.Unknown;
        }

        return screen switch
        {
            NetScreenType.Map => Presence.AtMap,
            NetScreenType.Room or NetScreenType.GameOver => Presence.InRoom,
            NetScreenType.None => Presence.Unknown,
            _ => Presence.Busy,
        };
    }

    /// <summary>
    /// Why nothing in the current room may be touched any more, or null. Co-op only.
    ///
    /// Once our map vote is out, the last teammate's vote moves the party at once, and a claim,
    /// purchase or potion that reaches a copy after it has left the room is thrown away there
    /// while it stays applied here: the next fight's checksum then gets this client kicked
    /// (votes.md 0.2 item 11, list E item 15). The map stays open over the room after a vote,
    /// so this only shows when something closed it.
    ///
    /// And while a fight is being set up, a potion used or discarded from a room screen is not
    /// a fight action: it can keep the fight from starting on a copy that is still loading the
    /// room, or sit in front of our own plays for the whole fight (0.2 item 12, list E item 5).
    /// </summary>
    public static string? RoomClosed(GameContext ctx)
    {
        if (!Active(ctx)) return null;
        if (MapReader.VoteStanding(ctx)) return CoopManners.RoomClosed;
        return FightStarting(ctx) ? CoopManners.FightStarting : null;
    }

    /// <summary>
    /// A fight is being set up, or wound up: the current room is a fight that is not won yet
    /// while no fight is in progress (votes.md 2.6; CombatManager.IsStarting covers only the
    /// later part of the start). A won fight's room is marked finished before its rewards show,
    /// so the rewards screen never counts.
    /// </summary>
    public static bool FightStarting(GameContext ctx)
    {
        try
        {
            if (CombatManager.Instance is not { IsInProgress: false } cm) return false;
            return cm.IsStarting || ctx.Run?.CurrentRoom is CombatRoom { IsPreFinished: false };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The room's co-op rule plus who is already waiting on the map, for the "co_op" field: a
    /// teammate who has voted there is done with the room and waits for us.
    /// </summary>
    public static string RoomNote(GameContext ctx, string rule) =>
        Safe("the room note", rule, () =>
        {
            var others = Others(ctx);
            var voted = others.Where(m => MapVote(m.Player) is not null).ToList();
            if (voted.Count == 0) return rule;
            return voted.Count == others.Count
                ? $"{rule} Everyone else has already voted on the map and is waiting for you."
                : $"{rule} {Names(voted)} already voted on the map.";
        });

    // ---------- failing soft ----------

    /// <summary>Features whose read threw during this run; each is logged once and then left off.</summary>
    private static readonly HashSet<string> Broken = new(StringComparer.Ordinal);

    private static object? _brokenRun;

    /// <summary>
    /// Runs a co-op read. One that throws is logged once and stays off until the next run,
    /// returning <paramref name="fallback"/> (the solo behaviour) from then on, so a co-op
    /// helper can never take a snapshot or an action down with it.
    /// </summary>
    public static T Safe<T>(string what, T fallback, Func<T> read)
    {
        var run = RunManager.Instance?.DebugOnlyGetState();
        if (!ReferenceEquals(run, _brokenRun))
        {
            _brokenRun = run;
            Broken.Clear();
        }

        if (Broken.Contains(what)) return fallback;
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            Broken.Add(what);
            ModEntry.LogError($"coop: reading {what} failed, so it is off for the rest of this run: {ex}");
            return fallback;
        }
    }

    // ---------- names ----------

    /// <summary>
    /// The name to call a player by. Names never cross the network: each copy makes its own,
    /// and a Steam-less session only has the stock "Test Host" and "Test Client 1". Those are
    /// replaced the way the human's copy shows them: the host by the human's name, a bot by its
    /// character. A name some patch already made real is kept.
    /// </summary>
    private static string NameOf(Player p, bool isHost, bool enet)
    {
        var shown = Describe.SafeText(() => p.Creature.Name);
        if (shown.Length > 0 && !(enet && IsStockName(shown))) return shown;

        if (enet && isHost) return HumanName();
        var character = CharacterName(p);
        if (character.Length > 0) return character;
        return shown.Length > 0 ? shown : $"player {p.NetId}";
    }

    private static bool IsStockName(string name) =>
        name.StartsWith("Test Host", StringComparison.Ordinal)
        || name.StartsWith("Test Client", StringComparison.Ordinal)
        || name.All(char.IsDigit);

    /// <summary>"Ironclad" for "The Ironclad": the name a bot answers to (as Identity does).</summary>
    private static string CharacterName(Player p)
    {
        var title = Describe.SafeText(() => p.Character?.Title);
        return title.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? title[4..] : title;
    }

    /// <summary>
    /// The human's name as their own copy works it out (ModEntry.ResolveHumanName): every copy of
    /// a local session runs under the same user on the same Mac, so the same rule gives the same name.
    /// </summary>
    private static string HumanName() => _humanName ??= ResolveHumanName();

    private static string? _humanName;

    private static string ResolveHumanName()
    {
        var name = System.Environment.GetEnvironmentVariable("SPIREMIND_HUMAN_NAME")?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            try { name = System.Environment.UserName.Trim(); }
            catch { name = null; }
            if (!string.IsNullOrEmpty(name)) name = char.ToUpperInvariant(name[0]) + name[1..];
        }
        return string.IsNullOrEmpty(name) ? "the human" : name;
    }

    // ---------- the session ----------

    /// <summary>The host's NetId: ours when we host, the service's HostNetId on a client; null when it can't be read.</summary>
    private static ulong? HostNetId()
    {
        try
        {
            return RunManager.Instance?.NetService switch
            {
                NetClientGameService client => client.HostNetId,
                { Type: NetGameType.Host } host => host.NetId,
                _ => null,
            };
        }
        catch
        {
            // Both getters throw once the connection is gone.
            return null;
        }
    }

    /// <summary>A Steam-less (ENet) session: the platform is None on both sides, also when Steam is running.</summary>
    private static bool IsEnet()
    {
        try { return RunManager.Instance?.NetService?.Platform == PlatformType.None; }
        catch { return false; }
    }
}
