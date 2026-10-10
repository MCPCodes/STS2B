# Brute Raider
Overgrowth hallway raider, 30-33 HP: hits on odd enemy turns and gains 3 Strength on even ones, so its hits go 7, 10, 13, 16.

## Facts (game files, v0.107.1)

Where: act 1 Overgrowth only. Hallway fight RubyRaidersNormal (normal): 3 raiders of 3 different types, drawn from 5, in random order. Each type is in 3 of 5 such fights. Normal fights come from hallway fight 4 on, each at most once per act.

The other 4 types:
- Axe Raider: 20-22 HP. Loop of 5 damage + 5 Block, the same again, then 12 damage.
- Assassin Raider: 18-23 HP. Hits 10 every turn.
- Crossbow Raider: 18-21 HP. Gains 3 Block on odd enemy turns, hits 14 on even ones.
- Tracker Raider: 21-25 HP. 2 Frail on you on enemy turn 1, then 1x8 every turn.

HP: 30-33 (asc 8+: 31-34).

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Beat | `attack 7` | 1 hit: 7 base (asc 9+: 8) + its Strength. |
| Roar | `buff` | Gains 3 Strength. Itself only, not the other raiders. No damage, no Block. |

Pattern: fixed, no randomness. Beat, Roar, Beat, Roar, starting with Beat.
- Odd enemy turns: Beat. Even enemy turns: Roar, no attack.
- Beat damage: 7 on turn 1, 10 on turn 3, 13 on turn 5, 16 on turn 7. +3 per Roar.

Special rules:
- Strength adds to every hit it deals, before multipliers. It never decays.
- The attack intent already includes its Strength. Do not add it again.
- Worked, with Weak on it: turn 3 Beat is (7 + 3) x 0.75 = 7.5, deals 7. The intent reads `attack 7`.
- It never gains Block and applies no debuffs.
- No summon, heal, escape, on-death effect or reaction to hits or cards. The fight ends when all 3 raiders are dead.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- It does not attack on even turns. Count 0 from it then.
- Damage over enemy turns 1-6 if left alive: Assassin 60, Axe 44, Crossbow 42, Tracker 40, Brute 30. Least damage and most HP: kill it last.
- Each Roar adds 3 to every later Beat for the rest of the fight: 16 on turn 7, 19 on turn 9.

## Lessons
(none yet)
