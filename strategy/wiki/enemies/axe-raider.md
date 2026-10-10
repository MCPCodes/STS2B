# Axe Raider
Overgrowth hallway raider, 20-22 HP: fixed loop of 5 damage plus 5 Block, the same again, then 12 damage.

## Facts (game files, v0.107.1)

Where: act 1 Overgrowth only. Hallway fight RubyRaidersNormal (normal): 3 raiders of 3 different types, drawn from 5, in random order. Each type is in 3 of 5 such fights. Normal fights come from hallway fight 4 on, each at most once per act.

The other 4 types:
- Assassin Raider: 18-23 HP. Hits 10 every turn.
- Brute Raider: 30-33 HP. Hits on odd enemy turns (7, 10, 13, ...), gains 3 Strength on even ones.
- Crossbow Raider: 18-21 HP. Gains 3 Block on odd enemy turns, hits 14 on even ones.
- Tracker Raider: 21-25 HP. 2 Frail on you on enemy turn 1, then 1x8 every turn.

HP: 20-22 (asc 8+: 21-23).

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Swing | `attack 5`, `defend` | 1 hit: 5 (asc 9+: 6). Then it gains 5 Block (asc 9+: 6). |
| Big Swing | `attack 12` | 1 hit: 12 (asc 9+: 13). |

Pattern: fixed loop of 3, no randomness: Swing, Swing, Big Swing.
- Big Swing on enemy turns 3, 6, 9, ...
- Swing on every other enemy turn.

Special rules:
- The `defend` intent shows no number. The Block is 5.
- Block timing: it gains the Block after its hit. The Block stays through your next turn and is removed when the next enemy turn starts.
- So it has 0 Block during your turns 1, 4, 7, ... and 5 Block during every other turn.
- Worked: on a 5 Block turn a Strike (6) removes the Block and 1 HP. Killing it from 22 HP on such a turn takes 27 damage.
- No summon, heal, escape, on-death effect or reaction to hits or cards. The fight ends when all 3 raiders are dead.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Attack it on your turns 1, 4, 7 (0 Block). On other turns the first 5 damage goes into Block: hit another raider unless the hit kills.
- 3 energy of starter cards do not kill it on turn 1: 3 Strikes = 18, Bash + Strike = 8 + 9 = 17.
- Incoming from it: 5, 5, 12, repeating.
- Damage over enemy turns 1-6 if left alive: Assassin 60, Axe 44, Crossbow 42, Tracker 40, Brute 30. Middle priority: after the Assassin, before the Brute.

## Lessons
(none yet)
