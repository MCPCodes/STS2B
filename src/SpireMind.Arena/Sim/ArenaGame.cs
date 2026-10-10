using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// One arena fight behind the same seam the real game sits behind, so the bot's tools
/// (get_state, wait_for_decision, act, act_sequence, get_deck) drive it unchanged. It adds to
/// <see cref="Fight"/> what a game has and a rule book doesn't: a lock, since snapshots and
/// actions arrive from several threads, and a short busy spell after every action, so a model
/// has to wait for its next decision as it does in the game.
/// </summary>
public sealed class ArenaGame : IGameBridge
{
    /// <summary>The <c>screen</c> of the snapshot once the fight is over, won or lost
    /// (<c>state.result</c> says which). It awaits a decision whose only action is <see cref="DoneAction"/>.</summary>
    public const string OverScreen = Fight.OverScreen;

    /// <summary>The one action of the end snapshot. Running it succeeds and changes nothing.</summary>
    public const string DoneAction = Fight.DoneAction;

    private readonly object _gate = new();
    private readonly Fight _fight;
    private DateTime _busyUntil = DateTime.MinValue;
    private int _refused;

    /// <exception cref="ArgumentException">The scenario cannot be turned into a fight.</exception>
    public ArenaGame(Scenario scenario) : this(scenario, Scenarios.Setup(scenario)) { }

    /// <summary>A fight that is set up by hand instead of worked out from the scenario's id (the
    /// tool-path check builds one that is sure to hold a card choice and a potion).</summary>
    internal ArenaGame(Scenario scenario, FightSetup setup)
    {
        Scenario = scenario;
        _fight = new Fight(FightConfig.From(setup));
    }

    public Scenario Scenario { get; }

    /// <summary>How long the snapshot stays busy after a card, and twice that after end_turn (the
    /// enemy turn). Zero for the scripted players, which never wait.</summary>
    public TimeSpan ActionDelay { get; init; } = DefaultActionDelay;

    /// <summary>The busy time after a card when nothing else is asked for.</summary>
    public static readonly TimeSpan DefaultActionDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// The running fight, for a scripted player that searches (<see cref="IEnginePolicy"/>). It is
    /// the live fight: look, or copy it with <see cref="Fight.Clone"/> or
    /// <see cref="Fight.CloneDeterminized"/> and play the copy; never act on it directly, and
    /// only touch it from the thread that drives this game.
    /// </summary>
    public Fight Fight => _fight;

    public bool IsOver { get { lock (_gate) return _fight.IsOver; } }
    public bool Won { get { lock (_gate) return _fight.Won; } }

    /// <summary>The fight was lost by running into <see cref="Tuning.MaxTurns"/>, not by dying.</summary>
    public bool TimedOut { get { lock (_gate) return _fight.TimedOut; } }

    /// <summary>The player's HP now. After a win it includes Burning Blood's 6, as in the game.</summary>
    public int PlayerHp { get { lock (_gate) return _fight.Player.Hp; } }
    public int PlayerMaxHp { get { lock (_gate) return _fight.Player.MaxHp; } }
    public int Turn { get { lock (_gate) return _fight.Turn; } }

    /// <summary>Cards the player played from the hand (cards an effect played for free are not counted).</summary>
    public int CardsPlayed { get { lock (_gate) return _fight.CardsPlayed; } }

    /// <summary>Actions <see cref="ExecuteAsync"/> turned down: illegal ids, or anything sent
    /// while the fight was busy or over.</summary>
    public int RefusedActions { get { lock (_gate) return _refused; } }

    /// <summary>All HP lost in the fight, whatever took it: enemies, status cards or the player's own cards.</summary>
    public int DamageTaken { get { lock (_gate) return _fight.DamageTaken; } }

    /// <summary>HP the enemies still have between them; 0 after a win. Says how close a lost fight was.</summary>
    public int EnemyHpLeft { get { lock (_gate) return _fight.EnemyHpLeft; } }

    public Task<GameSnapshot> CaptureAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (DateTime.UtcNow >= _busyUntil)
                return Task.FromResult(_fight.Snapshot());
            return Task.FromResult(new GameSnapshot
            {
                Screen = "combat", AwaitingDecision = false, Waiting = "resolving", State = _fight.WaitingState(),
            });
        }
    }

    public Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct)
    {
        lock (_gate)
        {
            ActionResult result;
            // Nothing resolves after the end, so "done" leaves no busy spell behind it.
            var wasOver = _fight.IsOver;
            if (DateTime.UtcNow < _busyUntil)
            {
                result = ActionResult.Fail("Cannot act now (resolving). Call wait_for_decision.");
            }
            else
            {
                try
                {
                    result = _fight.Execute(actionId);
                }
                catch (Exception ex)
                {
                    // The rules are meant to refuse, never to throw; if they do, the fight is put back
                    // as it was before the action and the caller still gets an answer.
                    result = ActionResult.Fail($"The arena could not run '{actionId}' ({ex.GetType().Name}: {ex.Message}).");
                }
            }

            if (!result.Ok)
                _refused++;
            else if (!wasOver && ActionDelay > TimeSpan.Zero)
                _busyUntil = DateTime.UtcNow + (actionId == "end_turn" ? ActionDelay * 2 : ActionDelay);
            return Task.FromResult(result);
        }
    }

    public Task<JsonObject> DescribeDeckAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_fight.DescribeDeck());
        }
    }
}
