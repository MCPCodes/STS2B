# Kin Follower
Minion in The Kin boss fight, 58-59 HP: cycles slash 5, boomerang 2x2 and +2 Strength; it dies when the Kin Priest dies.

## Facts (game files, v0.107.1)

Numbers are for ascension 0. "(asc: n)" is the higher-ascension value: HP from ascension 8, damage from ascension 9. From ascension 9 Power Dance also gives more Strength.

Where: boss fight The Kin [TheKinBoss], act 1 Overgrowth, one of its 3 bosses. Always 2 Kin Followers and 1 Kin Priest (see kin-priest.md). e0 is the Follower that opens with Power Dance, e1 the Follower that opens with Quick Slash, e2 the Kin Priest. They act in that order.

HP: 58-59 (asc: 62-63). One Follower has 58, the other 59.

Starts with Minion (shown with amount 1):
- A Minion does not have to die. When the Kin Priest dies, both Followers die at once and the fight is won.
- Killing a Minion does not count as a kill for Fatal effects such as Feed.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Quick Slash | `attack 5` | 1 hit: 5 base + its own Strength. |
| Boomerang | `attack 2x2` | 2 hits: 2 base each + its own Strength on every hit. |
| Power Dance | `buff` | Gains 2 Strength. Itself only. |

Pattern: fixed 3-move cycle, no randomness: Quick Slash, Boomerang, Power Dance, repeat. e0 enters the cycle at Power Dance, e1 at Quick Slash.
- Next move: after `buff` comes Quick Slash, after the single hit comes Boomerang, after the 2-hit attack comes `buff`.
- Each Power Dance adds 2 to its Quick Slash and 2 to each Boomerang hit (4 in total).

Damage by enemy turn, when nothing changes their Strength:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| e0 | dance | 7 | 4x2 = 8 | dance | 9 | 6x2 = 12 | dance | 11 | 8x2 = 16 |
| e1 | 5 | 2x2 = 4 | dance | 7 | 4x2 = 8 | dance | 9 | 6x2 = 12 | dance |

Per 3 turns: e0 deals 15, 21, 27. e1 deals 9, 15, 21. Each Follower adds 6 per cycle. Totals with the Priest are in kin-priest.md.

Special rules:
- Killing a Follower changes nothing for the other Follower or the Priest.
- It never gains Block. It applies no debuffs. No summons.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- The fight is won by killing the Kin Priest (190 HP). Killing both Followers first adds 117 HP of work.
- Killing a Follower removes its damage only: for 58-59 HP that is 15 (e0) or 9 (e1) in the first 3 turns, 6 more each later cycle.
- e0 deals 6 more than e1 in every 3 turns (15 vs 9, 21 vs 15), so it is the better one to kill.
- Do not plan a Fatal effect (Feed) on a Follower; it does not trigger.

## Lessons
(none yet)
