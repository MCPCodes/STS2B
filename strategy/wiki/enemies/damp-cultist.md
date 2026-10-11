# Damp Cultist
51-53 HP, beside a Calcified Cultist: gains Ritual 5 on turn 1, then attacks every turn for 1, 6, 11, 16, 5 more each turn.

## Facts (game files, v0.107.1)

Where: act 1 Underdocks only. Normal fight Cultists [CultistsNormal]: Calcified Cultist (e0), then Damp Cultist (e1). They act in that order. See calcified-cultist.md. Normal fights come from hallway fight 4 on.

HP: 51-53 (asc 8+: 52-54). The Calcified Cultist has 38-41.

Co-op: HP x players x 1.1, rounded down (2 players: 112, 114 or 116). Ritual and damage are not scaled; every attack hits every player. (Read from the code, not seen in a fight.)

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Incantation | `buff` | Gains Ritual 5 (asc 9+: 6). No damage, no Block. Used once, on enemy turn 1. |
| Dark Strike | `attack 1` | 1 hit: 1 base (asc 9+: 3) + Strength. |

Pattern: fixed, no randomness. Incantation on enemy turn 1, then Dark Strike every turn for the rest of the fight.

Ritual:
- At the end of every enemy turn it gains Strength equal to its Ritual: 5.
- The end of the enemy turn it casts Ritual in gives nothing. The first +5 comes at the end of enemy turn 2, after the first Dark Strike. So the first Dark Strike is 1, not 6.
- The intent you see on your turn already has the gain in it.
- Ritual never runs out. Only its death stops the growth.

By enemy turn, when nothing else changes its Strength:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Intent | buff | 1 | 6 | 11 | 16 | 21 | 26 | 31 |
| Strength after | 0 | 5 | 10 | 15 | 20 | 25 | 30 | 35 |
| Running total | 0 | 1 | 7 | 18 | 34 | 55 | 81 | 112 |

Enemy turn N (from 2 on) hits for 1 + 5 x (N - 2).

With the Calcified Cultist, both alive, when nothing else changes Strength:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| e0 Calcified Cultist | buff | 9 | 11 | 13 | 15 | 17 | 19 | 21 |
| e1 Damp Cultist | buff | 1 | 6 | 11 | 16 | 21 | 26 | 31 |
| Total | 0 | 10 | 17 | 24 | 31 | 38 | 45 | 52 |
| Running total | 0 | 10 | 27 | 51 | 82 | 120 | 165 | 217 |

Worked numbers:
- Weak on it rounds down: turn 2 Dark Strike 1 x 0.75 = 0.75, so 0. Turn 3: 6 x 0.75 = 4.5, so 4. Turn 4: 11 x 0.75 = 8.25, so 8.
- Vulnerable on you: turn 2 Dark Strike 1 x 1.5 = 1.5, so 1. Turn 3: 9. Turn 4: 16.5, so 16.
- 1 point of Strength taken from it removes 1 from every later Dark Strike.

Special rules:
- Killing one cultist changes nothing for the other.
- It never gains Block and applies no debuffs. No summon, heal, escape or on-death effect.
- No fight against it is recorded. Everything here is read from the code.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Enemy turn 1 deals 0 from both cultists: no Block on your turn 1.
- Its first 2 hits are small (1, 6). From enemy turn 5 it hits harder than the Calcified Cultist (16 against 15) and the gap grows by 3 a turn.
- Together they deal 10, 17, 24, 31, 38: 7 more every turn. Each turn the fight lasts costs more than the one before.
- Kill order: the Calcified Cultist has 10-15 fewer HP and deals more through enemy turn 7 (84 against 81 in total). In a fight that ends by then, kill it first. In a longer one this cultist costs more: 31 on turn 8 alone.
- Weak on it saves little early and more each turn: 2 on turn 3, 3 on turn 4, 4 on turn 5.

## Lessons
(none yet)
