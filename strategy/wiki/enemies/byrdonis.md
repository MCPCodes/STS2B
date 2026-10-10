# Byrdonis
Overgrowth elite, 81-84 HP: alternates Swoop 17 and Peck 3x3 and gains 1 Strength at the end of every enemy turn.

## Facts (game files, v0.107.1)

Numbers are for ascension 0. "(asc: n)" is the higher-ascension value: HP from ascension 8, damage from ascension 9.

Where: elite fight Byrdonis [ByrdonisElite], act 1 Overgrowth. Alone, enemy e0. Nothing joins.

HP: 81-84 (asc: 90).

Starts with Territorial 1:
- At the end of every enemy turn, the first one included, it gains 1 Strength.
- Lowering or removing its Strength does not stop the gain.
- The gain lands after its move. The intent you see on your turn already holds the right number.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Swoop | `attack 17` | 1 hit: 17 base (asc: 19) + Strength. |
| Peck | `attack 3x3` | 3 hits: 3 base each (asc: 4) + Strength on every hit. |

Pattern: fixed, no randomness. Swoop, Peck, Swoop, Peck, starting with Swoop. The next move is the one it did not just use.

Damage by enemy turn (its Strength is turn - 1 when nothing else changes it):

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Move | Swoop | Peck | Swoop | Peck | Swoop | Peck | Swoop | Peck |
| Damage | 17 | 4x3 = 12 | 19 | 6x3 = 18 | 21 | 8x3 = 24 | 23 | 10x3 = 30 |
| Running total | 17 | 29 | 48 | 66 | 87 | 111 | 134 | 164 |

Each Swoop + Peck pair deals 8 more than the pair before: 29, 37, 45, 53.

Worked numbers:
- Peck into Block: each hit meets the Block that is left. Turn 4, 6x3 into 10 Block: 6 blocked, then 4 blocked and 2 through, then 6 through. 8 HP lost.
- Weak on it rounds down per hit. Turn 4 Peck: 6 x 0.75 = 4.5, so 4x3 = 12, not 13. Turn 3 Swoop: 19 x 0.75 = 14.25, so 14.
- 1 point of Strength taken from it removes 1 damage from a Swoop and 3 from a Peck.

Special rules: it never gains Block. It applies no debuffs. No phases, no summons.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- It attacks every turn and each pair of turns deals 8 more than the last. Kill it early; stalling loses.
- Next turn's damage is known: Swoop 17 + Strength, Peck 3 x (3 + Strength), with 1 more Strength than this turn.
- The first 4 enemy turns deal 17, 12, 19, 18. Plan Block for those numbers and put the rest into damage.
- Strength loss on it is worth 3 per point on a Peck turn and 1 on a Swoop turn.

## Lessons
(none yet)
