# Sewer Clam
56 HP behind Block that is renewed every turn (8, 8, 7, 6, down to 0): alternates attack 10 and +4 Strength.

## Facts (game files, v0.107.1)

Where: normal fight Sewer Clam [SewerClamNormal], act 1 Underdocks. 1 Sewer Clam, alone, enemy e0. Normal fights come from hallway fight 4 on.

HP: 56 (asc 8+: 58). Always the same number.

Co-op: HP x players x 1.1, rounded down (2 players: 123). Plating is x3 with 2 players (24) and drops by 1 per player each round from round 2 (2 players: 2). With 2 players its Block on your turns 1-14 is 24, 24, 22, 20, 18, 16, 14, 12, 10, 8, 6, 4, 2, 0. Damage and Strength are not scaled; every attack hits every player. (Read from the code, not seen in a fight.)

Starts with Plating 8 (asc 8+: 9) and 8 Block:
- Before your turn 1 it gains Block equal to its Plating: 8.
- When an enemy turn starts, its Block is removed. From enemy turn 2 on, Plating then drops by 1.
- At the end of every enemy turn it gains Block equal to the Plating it has left.
- Hitting it does not lower Plating. Only time does. Plating is gone from your turn 10.
- Frail on the clam does not cut this Block.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Jet | `attack 10` | 1 hit: 10 base (asc 9+: 11) + Strength. |
| Pressurize | `buff` | Gains 4 Strength (same at every ascension). No damage. |

Pattern: fixed, no randomness. Jet, Pressurize, Jet, Pressurize, ... Jet on odd enemy turns, Pressurize on even ones. It starts with Jet.
- After `attack` comes `buff`. After `buff` comes `attack`, 4 higher than the last one.

By turn, when nothing else changes its Strength:

| Turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Its Block during your turn | 8 | 8 | 7 | 6 | 5 | 4 | 3 | 2 | 1 | 0 |
| Its intent | 10 | buff | 14 | buff | 18 | buff | 22 | buff | 26 | buff |
| Damage on the enemy turn | 10 | 0 | 14 | 0 | 18 | 0 | 22 | 0 | 26 | 0 |
| Total damage to kill it on this turn, attacking every turn | 64 | 72 | 79 | 85 | 90 | 94 | 97 | 99 | 100 | 100 |

- Jet on odd enemy turn N hits for 10 + 2 x (N - 1).
- Incoming over enemy turns 1-5: 42. Turns 1-7: 64. Turns 1-9: 90.

Special rules:
- The Block is new every turn. Block you remove is back next turn, 1 lower from turn 3. Block you leave is removed anyway. Damage that does not get through a turn's Block is wasted.
- To kill it you must deal 56 plus the Block of every turn you attacked in. A turn with no attacks costs nothing.
- It applies no debuffs and summons nothing. No heal, escape or on-death effect.
- The whole fight is read from the code. No Sewer Clam fight is recorded. On another monster, Plating's Block before turn 1 was seen in a fight.

Worked numbers:
- Your turn 1: 1 Strike (6) takes 0 HP. 3 Strikes (18) take 10 HP.
- 12 damage a turn: 4 HP on turn 1, 7 HP on turn 5, 12 HP on turn 10.
- 24 damage on turn 1 takes 16 HP. 12 on turn 1 and 12 on turn 2 take 8 HP in all.
- Bash on your turn 1: its 8 damage is all blocked, the 2 Vulnerable still lands. A Strike after it deals 9.
- Weak on it rounds down: Jet 14 x 0.75 = 10.5, so 10. Jet 18: 13.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Each turn's Block eats the first damage of that turn. Pack attacks into few big turns: 24 in one turn beats 12 in each of two.
- Even enemy turns deal 0. Your even turns need no Block: they are the turns to spend fully on damage.
- A turn with only 1 small attack is wasted on it in the early turns: 8 Block on turns 1-2, 7 on turn 3.
- Two clocks: its Block falls by 1 a turn, Jet rises by 4 every 2 turns. Waiting for the Block to fall saves 1 damage per turn waited and costs a Jet of 14, 18, 22.
- Vulnerable multiplies the damage before Block takes its share: with 8 Block, 12 damage takes 4 HP and 18 takes 10.

## Lessons
(none yet)
