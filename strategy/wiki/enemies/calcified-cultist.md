# Calcified Cultist
38-41 HP: gains Ritual 2 on turn 1, then attacks every turn for 9, 11, 13, 15, 2 more each turn until it dies.

## Facts (game files, v0.107.1)

Where (act 1 Underdocks, both normal fights, from hallway fight 4 on):
- Underdocks Wildlife [SeapunkNormal]: Calcified Cultist (e0), then Seapunk (e1). They act in that order. See seapunk.md.
- Cultists [CultistsNormal]: Calcified Cultist (e0), then Damp Cultist (e1). They act in that order. See damp-cultist.md.

HP: 38-41 (asc 8+: 39-42).

Co-op: HP x players x 1.1, rounded down (2 players: 83, 85, 88 or 90). Ritual and damage are not scaled; every attack hits every player. (Read from the code, not seen in a fight.)

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Incantation | `buff` | Gains Ritual 2 (same at every ascension). No damage, no Block. Used once, on enemy turn 1. |
| Dark Strike | `attack 9` | 1 hit: 9 base (asc 9+: 11) + Strength. |

Pattern: fixed, no randomness. Incantation on enemy turn 1, then Dark Strike every turn for the rest of the fight.

Ritual:
- At the end of every enemy turn, after all enemies have acted, it gains Strength equal to its Ritual: 2.
- The end of the enemy turn it casts Ritual in gives nothing. The first +2 comes at the end of enemy turn 2, after the first Dark Strike. So the first Dark Strike is 9, not 11.
- The intent you see on your turn already has the gain in it.
- Ritual never runs out. Only its death stops the growth.

By enemy turn, when nothing else changes its Strength:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Intent | buff | 9 | 11 | 13 | 15 | 17 | 19 | 21 |
| Strength after | 0 | 2 | 4 | 6 | 8 | 10 | 12 | 14 |
| Running total | 0 | 9 | 20 | 33 | 48 | 65 | 84 | 105 |

Enemy turn N (from 2 on) hits for 9 + 2 x (N - 2).

With its partner, both alive, when nothing else changes Strength:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Underdocks Wildlife: Seapunk | 11 | 8 | 0 | 12 | 12 | 0 | 13 | 16 |
| Underdocks Wildlife: total | 11 | 17 | 11 | 25 | 27 | 17 | 32 | 37 |
| Cultists: Damp Cultist | buff | 1 | 6 | 11 | 16 | 21 | 26 | 31 |
| Cultists: total | 0 | 10 | 17 | 24 | 31 | 38 | 45 | 52 |

Worked numbers:
- Weak on it rounds down: turn 3 Dark Strike 11 x 0.75 = 8.25, so 8. Turn 4: 13 x 0.75 = 9.75, so 9.
- Vulnerable on you: turn 2 Dark Strike 9 x 1.5 = 13.5, so 13. Turn 3: 16.
- 1 point of Strength taken from it removes 1 from every later Dark Strike.

Special rules:
- It never gains Block and applies no debuffs. No summon, heal, escape or on-death effect.
- No fight against it is recorded. Its HP and the 9 are read from the code. Ritual's skipped first gain was seen in a fight on another monster with Ritual.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Enemy turn 1 deals 0 from it. Its first hit, on enemy turn 2, is 9.
- Every turn it lives adds 2 to all its later hits: turns 2-4 deal 33, turns 5-7 deal 51. Killing it early is worth more each turn.
- Underdocks Wildlife: it has less HP than the Seapunk (44-46) and from enemy turn 4 hits harder every turn (13, 15, 17 against 12, 12, 0).
- Cultists: the Damp Cultist starts at 1 but adds 5 a turn; it passes this one on enemy turn 5 (16 against 15).
- Weak on it saves more each turn: 3 on turn 3, 4 on turn 4.

## Lessons
(none yet)
