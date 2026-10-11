# Skulking Colony
Underdocks elite, 75 HP: Hardened Shell lets it lose at most 20 HP per turn; cycle 14, 14, 9 with +2 Strength, 7x2.

## Facts (game files, v0.107.1)

Numbers are for ascension 0. "(asc: n)" is the higher-ascension value: HP from ascension 8, damage from ascension 9. From ascension 9 Inertia also gives more Strength. Hardened Shell is 20 at every ascension.

Where: elite fight Skulking Colony [SkulkingColonyElite], act 1 Underdocks. Alone, enemy e0. Nothing joins. No randomness at all.

HP: 75 (asc: 80).

Co-op: HP x players x 1.1 (2 players: 165). Hardened Shell is scaled the same way (2 players: 44). Damage and Strength are not scaled; every attack hits every player. (2 players seen in a fight: 165 HP, shell 44.)

Starts with Hardened Shell 20:
- It counts the HP the Colony has lost since the last reset. A loss bigger than what is left of the 20 is cut down to what is left. After 20, every loss is 0.
- The cut applies to every source: attack cards, potions, Poison, reflected damage, HP-loss effects.
- The count resets at the start of your turn and again at the start of the enemy turn. So it can lose 20 on your turn and another 20 during the enemy turn.
- The amount shown on the power is what is left right now: 20, then 20 minus the HP lost since the reset. At 0 the amount is not shown.
- Damage above the cap is wasted, the rest of a multi-hit card included.
- Card numbers do not show the cap. A card that shows 30 against it still removes only what is left. Vulnerable on it raises a hit as usual, up to the cap.
- Poison ticks at the start of the enemy turn, after the reset, so it comes out of the enemy turn's 20. So does reflected damage (Thorns, Flame Barrier). (Poison and Thorns seen in a fight; Flame Barrier read from the code.)

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Zoom | `attack 14` | 1 hit: 14 base (asc: 16) + Strength. It gains no Block. |
| Inertia | `attack 9`, `buff` | 1 hit: 9 base (asc: 11) + Strength. Then gains 2 Strength (asc: 4). |
| Piercing Stabs | `attack 7x2` | 2 hits: 7 base each (asc: 8) + Strength on every hit. Block absorbs them as usual. |

Pattern: fixed 4-move cycle, no randomness: Zoom, Zoom, Inertia, Piercing Stabs, repeat. It starts with Zoom.
- Next move: after the first Zoom comes a second Zoom, after the second Zoom comes Inertia, after Inertia comes Piercing Stabs, after Piercing Stabs comes Zoom.
- Zoom is on enemy turns 1, 2, 5, 6, 9, 10. Inertia on 3, 7, 11. Piercing Stabs on 4, 8, 12.
- Its Strength is 2 per Inertia done, when nothing else changes it.

Damage by enemy turn, when nothing changes its Strength:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Move | Zoom | Zoom | Inertia | Piercing Stabs | Zoom | Zoom | Inertia | Piercing Stabs | Zoom |
| Damage | 14 | 14 | 9 | 9x2 = 18 | 16 | 16 | 11 | 11x2 = 22 | 18 |
| Running total | 14 | 28 | 37 | 55 | 71 | 87 | 98 | 120 | 138 |

Each 4-turn cycle deals 10 more than the last: 55, 65, 75.

How long it lives:
- With damage on your turns only, it cannot die before your turn 4: 20 + 20 + 20 + 15. By then it has acted 3 times: 14 + 14 + 9 = 37.
- With damage in the enemy turn too (Poison, reflected damage) it can lose up to 40 per round. The earliest death is the start of enemy turn 2: 20 on your turn 1, 20 in enemy turn 1, 20 on your turn 2, and Poison takes the last 15 before it acts. (Read from the code.)

Worked numbers:
- Shell at 20, a card for 14: 14 HP lost, the shell shows 6. The next hit for 9 is cut to 6 and the amount disappears. Every later hit that turn removes 0.
- Fire Potion (20) on a full shell: 20 HP lost. Nothing else removes HP until the enemy turn starts.
- Twin Strike 5x2 with 6 left on the shell: hit 1 removes 5, hit 2 removes 1.
- Weak on it rounds down per hit. Zoom 14 x 0.75 = 10.5, so 10. Inertia 9 x 0.75 = 6.75, so 6. Turn 4 Piercing Stabs: 9 x 0.75 = 6.75, so 6x2 = 12.
- 1 point of Strength taken from it removes 1 from Zoom and Inertia and 2 from Piercing Stabs.

Special rules:
- It never gains Block. It applies no debuffs. No phases, no summons.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Each of your turns removes at most 20 HP. Deal the 20, then put everything else into Block and Powers. When the shell shows no amount, stop attacking.
- The fight lasts at least 4 of your turns. Plan Block for 14, 14, 9, then 18 if it is still alive.
- Your turn 4 needs only 15 damage when turns 1-3 each took the full 20. A turn that falls short of 20 adds a turn.
- Burst cards and damage potions above the 20 are wasted. A damage potion is worth using only on a turn where your cards fall short of 20.
- Poison, Thorns and Flame Barrier use the enemy turn's own 20 and can shorten the fight.

## Lessons
(none yet)
