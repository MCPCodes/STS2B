# Fuzzy Wurm Crawler
55-57 HP: fixed loop of attack, +7 Strength, attack, so its hits go 4, 11, 11, 18, 18, 25 with a no-damage turn every third turn.

## Facts (game files, v0.107.1)

Where (act 1 Overgrowth):
- Weak fight FuzzyWurmCrawlerWeak: 1 Fuzzy Wurm Crawler, alone, e0.
- Normal fight OvergrowthCrawlers: e0 Shrinker Beetle, e1 Fuzzy Wurm Crawler. The beetle acts first.

HP: 55-57 (asc 8+: 58-59).

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Acid Goop | `attack 4` | 1 hit: 4 base (asc 9+: 6) + Strength. |
| Inhale | `buff` | Gains 7 Strength. No damage. |

Pattern: fixed, no randomness. Acid Goop, Inhale, Acid Goop, then the same 3 again. After `buff` come 2 attacks, then `buff` again. Inhale is on enemy turns 2, 5, 8, 11.

By enemy turn, when nothing else changes its Strength:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Intent | 4 | buff | 11 | 11 | buff | 18 | 18 | buff | 25 | 25 |
| Running total | 4 | 4 | 15 | 26 | 26 | 44 | 62 | 62 | 87 | 112 |

Worked numbers:
- Weak on it rounds down: 11 becomes 8, 18 becomes 13, 25 becomes 18.
- Vulnerable on you: 11 becomes 16, 18 becomes 27.

Special rules:
- Its Strength stays for the fight. It never gains Block, applies no debuffs and summons nothing.
- In OvergrowthCrawlers the Shrinker Beetle's turn 1 move puts Shrink on you: your attack hits x0.7 (Strike 6 deals 4) until the beetle dies. The beetle then alternates attack 7 and attack 13. See shrinker-beetle.md.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Enemy turns 2, 5, 8 deal 0 from it. Alone, it needs no Block on those turns.
- Its first 4 turns deal 26 in total. Turns 6 and 7 add 36. A kill before enemy turn 6 avoids every 18.
- A `buff` intent means its next 2 turns each hit 7 harder than its last hit.
- In OvergrowthCrawlers kill the Shrinker Beetle first. Shrink ends with it. Under Shrink the Crawler's 55-57 HP costs 79 or more attack damage.

## Lessons
(none yet)
