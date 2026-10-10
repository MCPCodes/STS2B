# Assassin Raider
Overgrowth hallway raider, 18-23 HP: hits 10 every turn and does nothing else.

## Facts (game files, v0.107.1)

Where: act 1 Overgrowth only. Hallway fight RubyRaidersNormal (normal): 3 raiders of 3 different types, drawn from 5, in random order. Each type is in 3 of 5 such fights. Normal fights come from hallway fight 4 on, each at most once per act.

The other 4 types:
- Axe Raider: 20-22 HP. Loop of 5 damage + 5 Block, the same again, then 12 damage.
- Brute Raider: 30-33 HP. Hits on odd enemy turns (7, 10, 13, ...), gains 3 Strength on even ones.
- Crossbow Raider: 18-21 HP. Gains 3 Block on odd enemy turns, hits 14 on even ones.
- Tracker Raider: 21-25 HP. 2 Frail on you on enemy turn 1, then 1x8 every turn.

HP: 18-23 (asc 8+: 19-24).

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Killshot | `attack 10` | 1 hit: 10 (asc 9+: 11). |

Pattern: the same move every enemy turn, from turn 1. No randomness.

Special rules:
- It never gains Block or Strength and applies no debuffs.
- No summon, heal, escape, on-death effect or reaction to hits or cards. The fight ends when all 3 raiders are dead.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Damage over enemy turns 1-6 if left alive: Assassin 60, Axe 44, Crossbow 42, Tracker 40, Brute 30. Most damage for 18-23 HP: kill it first.
- 3 Strikes (18) kill it only at 18 HP. Bash + Strike on turn 1 (8 + 9 = 17) leaves 1-6 HP; 1 Strike into Vulnerable (9) finishes it on turn 2.
- Count 10 from it every turn it lives.

## Lessons
(none yet)
