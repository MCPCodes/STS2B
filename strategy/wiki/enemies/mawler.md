# Mawler
72 HP: attack 4x2 on turn 1, then random attack 14 or 4x2, plus one Roar per fight that puts 3 Vulnerable on you.

## Facts (game files, v0.107.1)

Where: normal fight MawlerNormal, act 1 Overgrowth. Alone, enemy e0. Nothing joins.

HP: 72 (asc 8+: 76). Always the same number.

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Claw | `attack 4x2` | 2 hits: 4 base each (asc 9+: 5) + Strength on every hit. |
| Rip and Tear | `attack 14` | 1 hit: 14 base (asc 9+: 16) + Strength. |
| Roar | `debuff` | Puts 3 Vulnerable on you. No damage. Once per fight. |

Pattern: random, never the same move twice in a row.
- Enemy turn 1: Claw.
- While Roar is unused: the next move is Roar 50%, or the attack it did not just use 50%. So turn 2 is Rip and Tear or Roar.
- The turn after Roar: Rip and Tear or Claw, 50% each.
- Every turn after that: the attack it did not just use. Fully known.
- Roar has come by the end of enemy turn 2, 3, 4, 5 in 50%, 75%, 88%, 94% of fights.

Worked numbers:
- Roar on enemy turn T: Vulnerable covers enemy turns T+1, T+2, T+3. Rip and Tear 21. Claw 6x2 = 12. Those 3 turns deal 21 + 12 + 21 = 54 or 12 + 21 + 12 = 45.
- Without Vulnerable the 2 attacks deal 14 and 8.
- Claw into Block: each hit meets the Block that is left. 6x2 into 10 Block: 6 blocked, then 4 blocked and 2 through. 2 HP lost.
- Weak on it rounds down per hit: Rip and Tear 10, Claw 3x2 = 6. Weak on it and Vulnerable on you: Rip and Tear 14 x 0.75 x 1.5 = 15.75, so 15. Claw 4x2 = 8.

Special rules: it never gains Block or Strength. No summons, no on-death effect.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Enemy turn 1 is always 8.
- The `debuff` turn deals 0: put that whole turn into damage.
- The 3 enemy turns after Roar deal 45 or 54. Keep Block and Weak for them.
- Once the turn after Roar has passed, every intent is known in advance: 14 and 4x2 alternate.

## Lessons
(none yet)
