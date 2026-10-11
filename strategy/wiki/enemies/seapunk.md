# Seapunk
44-46 HP: fixed loop of attack 11, attack 2x4, then 7 Block with +1 Strength; alone or behind a Calcified Cultist.

## Facts (game files, v0.107.1)

Where (act 1 Underdocks):
- Weak fight Seapunk [SeapunkWeak]: 1 Seapunk, alone, enemy e0. Weak fights are hallway fights 1-3.
- Normal fight Underdocks Wildlife [SeapunkNormal]: Calcified Cultist (e0), then Seapunk (e1). They act in that order. See calcified-cultist.md. Normal fights come from hallway fight 4 on.
- Both fights carry the tag Seapunk: one does not come right after the other in the act's fight list when that can be avoided.

HP: 44-46 (asc 8+: 47-49).

Co-op: HP x players x 1.1, rounded down (2 players: 96, 99 or 101). Bubble Burp's Block is scaled the same way (2 players: 15). Damage and Strength are not scaled; every attack hits every player. (2 players seen in a fight: 101 HP, 15 Block, +1 Strength.)

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Sea Kick | `attack 11` | 1 hit: 11 base (asc 9+: 13) + Strength. |
| Spinning Kick | `attack 2x4` | 4 hits: 2 base each (same at every ascension) + Strength on every hit. |
| Bubble Burp | `buff`, `defend` | Gains 7 Block (asc 8+: 8). Then gains 1 Strength (asc 9+: 2). No damage. |

Pattern: fixed loop of 3, no randomness: Sea Kick, Spinning Kick, Bubble Burp. It starts with Sea Kick.
- After a 1-hit `attack` comes the 4-hit `attack`. After the 4-hit `attack` comes `buff` + `defend`. After that comes Sea Kick.
- Each Bubble Burp adds 1 to Sea Kick and 4 to Spinning Kick.

By enemy turn, when nothing else changes its Strength:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Intent | 11 | 2x4 | buff, defend | 12 | 3x4 | buff, defend | 13 | 4x4 | buff, defend |
| Damage | 11 | 8 | 0 | 12 | 12 | 0 | 13 | 16 | 0 |
| Strength after | 0 | 0 | 1 | 1 | 1 | 2 | 2 | 2 | 3 |

Loop totals: 19, 24, 29.

Underdocks Wildlife, both alive, when nothing else changes Strength:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| e0 Calcified Cultist | buff | 9 | 11 | 13 | 15 | 17 | 19 | 21 |
| e1 Seapunk | 11 | 8 | 0 | 12 | 12 | 0 | 13 | 16 |
| Total | 11 | 17 | 11 | 25 | 27 | 17 | 32 | 37 |

Special rules:
- Block timing: the 7 Block from Bubble Burp (enemy turns 3, 6, 9) is up during your turns 4, 7, 10. It is removed when that enemy turn starts, before it kicks. On every other turn it has 0 Block.
- The `defend` intent shows no number. The Block is 7. Frail on the Seapunk cuts it to 5.
- Its Strength stays for the fight. It applies no debuffs and summons nothing. No heal, escape or on-death effect.

Worked numbers:
- Spinning Kick into Block: each hit meets the Block that is left. Turn 2, 2x4 into 5 Block: 2 blocked, 2 blocked, then 1 blocked and 1 through, then 2 through. 3 HP lost. Same as 8 - 5.
- Weak on it rounds down per hit. Turn 2 Spinning Kick: 2 x 0.75 = 1.5, so 1x4 = 4, not 6. Turn 5: 3 x 0.75 = 2.25, so 2x4 = 8, not 9. Sea Kick 11 becomes 8.
- Vulnerable on you: Sea Kick 16. Spinning Kick 3x4 = 12.
- Your turn 4: a Strike (6) into its 7 Block takes 0 HP. Killing it from 20 HP on that turn takes 27 damage.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Alone: enemy turns 3, 6, 9 deal 0. Your turns 3, 6, 9 need no Block.
- It has 0 Block on your turns 1-3 and 7 Block on turn 4. Damage dealt on turns 1-3 loses nothing.
- Weak on it saves most on a Spinning Kick turn: 4 of 8 on turn 2, against 3 of 11 on turn 1.
- Each loop deals 5 more than the last (19, 24, 29). A short fight is cheap; a long one is not.
- With the Cultist: the Cultist has less HP (38-41) and from enemy turn 4 hits harder than the Seapunk every turn (13, 15, 17 against 12, 12, 0).

## Lessons
(none yet)
