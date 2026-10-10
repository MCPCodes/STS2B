# Cubex Construct
65 HP, Artifact 1: buffs on turn 1, then loops 2 single hits (+2 Strength after each) and a 2-hit attack: 9, 11, 11x2, 13, 15, 15x2.

## Facts (game files, v0.107.1)

Where:
- Normal fight CubexConstructNormal, act 1 Overgrowth: 1 Cubex Construct, alone, enemy e0.
- Normal fight ConstructMenagerieNormal, act 3 Glory: Punch Construct (e0), then 2 Cubex Constructs (e1, e2). They act in that order. Both Cubex Constructs follow the same schedule: count the damage table below twice.

HP: 65 (asc 8+: 70). Always the same number.

Starts with Artifact 1 and 0 Block:
- Artifact cancels the first debuff put on it (Vulnerable, Weak, Poison, Strength loss, any other), then it is gone. The damage of the card that carried the debuff still lands.
- A debuff from a relic at fight start uses the Artifact up before your first card.
- It has 0 Block on turn 1. The 13 Block in the game files is never given (seen in a real fight).

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Charge Up | `buff` | Gains 2 Strength. No damage. |
| Repeater Blast | `attack 7`, `buff` | 1 hit: 7 base (asc 9+: 8) + Strength. Then gains 2 Strength. |
| Expel | `attack 5x2` | 2 hits: 5 base each (asc 9+: 6) + Strength on every hit. No Strength gain. |

Pattern: fixed, no randomness. Charge Up once, then Repeater Blast, Repeater Blast, Expel, repeated for the whole fight.

By enemy turn, when nothing else changes its Strength:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Intent | buff | 9 | 11 | 11x2 | 13 | 15 | 15x2 | 17 | 19 | 19x2 |
| Damage | 0 | 9 | 11 | 22 | 13 | 15 | 30 | 17 | 19 | 38 |
| Strength after | 2 | 4 | 6 | 6 | 8 | 10 | 10 | 12 | 14 | 14 |

Each 3-turn loop hits 4 harder per hit than the loop before. Loop totals: 42, 58, 74.

Worked numbers:
- Bash while Artifact is up: 8 damage, no Vulnerable, Artifact gone.
- Expel into Block: each hit meets the Block that is left. Turn 4, 11x2 into 15 Block: 11 blocked, then 4 blocked and 7 through. 7 HP lost.
- Weak on it rounds down per hit. Turn 4 Expel: 11 x 0.75 = 8.25, so 8x2 = 16, not 22. Turn 3 Repeater Blast: 8.

Special rules: it never gains Block and applies no debuffs. No summons, no on-death effect.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Enemy turn 1 deals 0 from it. Alone, it needs no Block on turn 1.
- Artifact eats the first debuff. Play a cheap debuff before the one you need. With only Bash, expect 8 damage and no Vulnerable.
- Turns 2-4 deal 42, turns 5-7 deal 58, turns 8-10 deal 74. Every extra turn costs more: race it.
- The Expel turns 4, 7, 10 are the big ones (22, 30, 38). Weak saves 6 on turn 4 against 3 on turn 3.

## Lessons
(none yet)
