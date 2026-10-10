# Bygone Effigy
Overgrowth elite, 127 HP: no attack for 2 turns, then 23 every turn; Slow makes each later card in your turn hit it 10% harder.

## Facts (game files, v0.107.1)

Numbers are for ascension 0. "(asc: n)" is the higher-ascension value: HP from ascension 8, damage from ascension 9.

Where: elite fight Bygone Effigy [BygoneEffigyElite], act 1 Overgrowth. Alone, enemy e0. Nothing joins.

HP: 127 (asc: 132).

Starts with Slow:
- It counts the cards you have finished playing this turn. Every card type counts. A card played twice counts twice.
- Damage from your attack cards against it is multiplied by 1 + 0.1 x count. Potion damage, reflected damage and HP-loss effects are not multiplied.
- A card does not count itself. Card 1 hits x1.0, card 2 x1.1, card 3 x1.2, card 6 x1.5. All hits of one card use the same multiplier.
- The count resets to 0 when the enemy turn starts. Slow is never removed.
- The power shows count x 10 as its amount (no amount at 0). Amount 20 means the next attack card hits x1.2.
- It multiplies with Vulnerable and Weak, after your Strength is added. Each hit is rounded down once, at the end.
- Worked: Strike 6 as the 3rd card: 6 x 1.2 = 7.2, deals 7. The same into Vulnerable: 6 x 1.5 x 1.2 = 10.8, deals 10.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Sleep | `sleep` | Nothing. |
| Wake | `buff` | Gains 10 Strength. |
| Slashes | `attack 23` | 1 hit: 13 base (asc: 15) + its Strength. 23 with the 10 Strength. |

Pattern: fixed, no randomness. Enemy turn 1 Sleep. Enemy turn 2 Wake. Enemy turn 3 and every later turn Slashes.

Damage: 0, 0, then 23 every turn. Total through enemy turn N: 23 x (N - 2).
- Weak on it: 23 x 0.75 = 17.25, deals 17.
- Each point of Strength taken from it removes 1 damage.

Special rules:
- Damage does not wake it early. The 2 idle turns always happen.
- It never gains Block. It applies no debuffs. No phases, no summons.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Your turns 1 and 2: no damage comes. Play no Block. Play Powers, Vulnerable and attacks.
- From your turn 3 on, 23 comes every turn. It has 127 HP; every extra turn costs 23 minus your Block.
- Order each turn: Skills, Powers and cheap cards first, the biggest attack last.
- Weak on it cuts each hit from 23 to 17.

## Lessons
(none yet)
