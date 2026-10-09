using System.Text.Json.Nodes;

namespace SpireMind.Core;

/// <summary>
/// A tiny stand-in for the real game: one fight against a single enemy. Used by unit tests
/// and by the dev host so the MCP wiring and the play prompt can be exercised from Claude
/// Code without launching Slay the Spire 2.
/// </summary>
public sealed class FakeGame : IGameBridge
{
    private readonly object _gate = new();
    private readonly List<(string Name, int Cost, int Damage, int Block)> _hand = [];
    private int _energy, _hp = 80, _block, _enemyHp = 40, _turn;
    private DateTime _busyUntil = DateTime.MinValue;

    /// <summary>How long the fake "animates" after each action and during the enemy turn.</summary>
    public TimeSpan ActionDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    public FakeGame() => StartTurn();

    public Task<GameSnapshot> CaptureAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_enemyHp <= 0)
                return Task.FromResult(new GameSnapshot
                {
                    Screen = "rewards",
                    AwaitingDecision = true,
                    State = new JsonObject { ["gold"] = 15 },
                    Actions = [new LegalAction("proceed", "Leave the reward screen")],
                });

            if (DateTime.UtcNow < _busyUntil)
                return Task.FromResult(GameSnapshot.Busy("combat", "resolving"));

            var hand = new JsonArray();
            var actions = new List<LegalAction>();
            for (var i = 0; i < _hand.Count; i++)
            {
                var c = _hand[i];
                hand.Add(new JsonObject { ["i"] = i, ["name"] = c.Name, ["cost"] = c.Cost });
                if (c.Cost <= _energy)
                    actions.Add(c.Damage > 0
                        ? new LegalAction($"play:{i}>e0", $"Play {c.Name} on Jaw Worm")
                        : new LegalAction($"play:{i}", $"Play {c.Name}"));
            }
            actions.Add(new LegalAction("end_turn", "End turn"));

            return Task.FromResult(new GameSnapshot
            {
                Screen = "combat",
                AwaitingDecision = true,
                State = new JsonObject
                {
                    ["turn"] = _turn,
                    ["player"] = new JsonObject { ["hp"] = _hp, ["block"] = _block, ["energy"] = _energy },
                    ["hand"] = hand,
                    ["enemies"] = new JsonArray(new JsonObject
                    {
                        ["i"] = 0, ["name"] = "Jaw Worm", ["hp"] = _enemyHp, ["intent"] = "attack 11",
                    }),
                },
                Actions = actions,
            });
        }
    }

    public Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct)
    {
        lock (_gate)
        {
            if (actionId == "end_turn")
            {
                _hp -= Math.Max(0, 11 - _block);
                _busyUntil = DateTime.UtcNow + ActionDelay * 2;
                StartTurn();
                return Task.FromResult(ActionResult.Success);
            }

            if (actionId == "proceed")
                return Task.FromResult(ActionResult.Success);

            if (!actionId.StartsWith("play:"))
                return Task.FromResult(ActionResult.Fail($"unknown action {actionId}"));

            var index = int.Parse(actionId[5..].Split('>')[0]);
            if (index < 0 || index >= _hand.Count)
                return Task.FromResult(ActionResult.Fail("no such card"));

            var card = _hand[index];
            _energy -= card.Cost;
            _enemyHp -= card.Damage;
            _block += card.Block;
            _hand.RemoveAt(index);
            _busyUntil = DateTime.UtcNow + ActionDelay;
            return Task.FromResult(ActionResult.Success);
        }
    }

    private void StartTurn()
    {
        _turn++;
        _energy = 3;
        _block = 0;
        _hand.Clear();
        _hand.AddRange([("Strike", 1, 6, 0), ("Strike", 1, 6, 0), ("Defend", 1, 0, 5), ("Bash", 2, 8, 0), ("Defend", 1, 0, 5)]);
    }
}
