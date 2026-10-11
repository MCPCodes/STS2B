# Phantasmal Gardener
Underdocks elite, 4 at 26-31 HP: each cycles 5, 7, 1x3, +2 Strength; Skittish gives 6 Block after the first attack card that hurts it.

## Facts (game files, v0.107.1)

Numbers are for ascension 0. "(asc: n)" is the higher-ascension value: HP and Skittish from ascension 8, Enlarge's Strength from ascension 9. Bite, Lash and Flail are the same at every ascension.

Where: elite fight Phantasmal Gardeners [PhantasmalGardenersElite], act 1 Underdocks. Always 4 Phantasmal Gardeners, enemies e0 to e3. They act in that order. Nothing joins. None is a Minion: all 4 must die. A dead one leaves a gap; the others keep their ids.

HP: 26-31 each (asc: 27-32). The 4 always have 4 different values. Total 110-118 (asc: 114-122).

Co-op: HP x players x 1.1, rounded down (2 players: 57-68 each). Skittish gives 9 Block with 2 players. Damage and Strength are not scaled; every attack hits every player. (2 players seen in a fight: 57, 59, 61 and 68 HP, Skittish 9.) In co-op two gardeners can have the same HP (read from the code).

Starts with Skittish 6 (asc: 7), one on each gardener:
- The Block comes after your attack, not before it. When an attack card of yours has finished all its hits, each gardener it hurt gains 6 Block.
- Once per gardener per turn. The count resets at the end of your turn.
- So the first attack card that hurts a gardener each turn lands in full, every hit of a multi-hit card included. Later damage on that gardener that turn meets the 6 Block.
- A card that hits all enemies lands in full on all of them. Then every gardener it hurt gains 6 Block.
- The Block is gone when the enemy turn starts. A gardener never has it at the start of your turn.
- Only attack cards trigger it. Potion damage, Poison, Thorns and other relic or power damage do not trigger it and do not use it up. (Read from the code, not seen in a fight.)
- It looks at the card's first hit on that gardener. If that hit removed 0 HP, the card does not trigger it, even when a later hit did damage. (Read from the code, not seen in a fight.)
- A card that an effect plays twice is 2 attacks: the second play meets the 6 Block. (Read from the code, not seen in a fight.)

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Bite | `attack 5` | 1 hit: 5 base + its own Strength. |
| Lash | `attack 7` | 1 hit: 7 base + its own Strength. |
| Flail | `attack 1x3` | 3 hits: 1 base each + its own Strength on every hit. |
| Enlarge | `buff` | Gains 2 Strength (asc: 3). Itself only. No damage. |

Pattern: fixed 4-move cycle, no randomness: Bite, Lash, Flail, Enlarge, repeat. Each gardener enters the cycle at a different move, set by its position:
- e0 starts with Flail, e1 with Bite, e2 with Lash, e3 with Enlarge.
- Next move: after Bite comes Lash, after Lash comes Flail (the 3-hit attack), after Flail comes `buff`, after `buff` comes Bite.
- With all 4 alive, exactly 1 shows `buff` each turn and 3 attack. The `buff` is e3 on turn 1, e0 on turn 2, e2 on turn 3, e1 on turn 4, then e3 again.
- Killing one changes nothing for the others. Its row below is simply missing.
- Each gardener has its own Strength: 2 more after each of its own Enlarges.

Damage by enemy turn, all 4 alive and nothing changing their Strength:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| e0 | Flail 1x3 = 3 | Enlarge | Bite 7 | Lash 9 | Flail 3x3 = 9 | Enlarge | Bite 9 | Lash 11 |
| e1 | Bite 5 | Lash 7 | Flail 1x3 = 3 | Enlarge | Bite 7 | Lash 9 | Flail 3x3 = 9 | Enlarge |
| e2 | Lash 7 | Flail 1x3 = 3 | Enlarge | Bite 7 | Lash 9 | Flail 3x3 = 9 | Enlarge | Bite 9 |
| e3 | Enlarge | Bite 7 | Lash 9 | Flail 3x3 = 9 | Enlarge | Bite 9 | Lash 11 | Flail 5x3 = 15 |
| Total | 15 | 17 | 19 | 25 | 25 | 27 | 29 | 35 |
| Running total | 15 | 32 | 51 | 76 | 101 | 128 | 157 | 192 |

Damage per gardener: in enemy turns 1-4, e3 deals 25, e0 19, e2 17, e1 15. In turns 1-8, e3 deals 60, e0 48, e2 44, e1 40.

Worked numbers:
- Twin Strike 5x2 as the first attack card on a gardener: both hits land, 10 HP lost. Then it gains 6 Block.
- A second attack card on that gardener the same turn meets the 6 Block. Strike 6: 0 HP lost. Twin Strike 5x2: 5 blocked, then 1 blocked and 4 through, 4 HP lost. Thunderclap 4: 0 HP lost and 2 Block left.
- Whirlwind with X = 3 as the first attack: 5x3 = 15 on each of the 4. Then each gains 6 Block.
- Finishing a gardener that already has its Block up takes its HP + 6.
- Flail gains the most from Strength: (1 + Strength) x 3. With 2 Strength 3x3 = 9, with 4 Strength 5x3 = 15.
- Weak on a gardener rounds down per hit. Bite 5 x 0.75 = 3.75, so 3. Lash 7 x 0.75 = 5.25, so 5. Flail with 0 Strength: 1 x 0.75 = 0.75, so 0x3 = 0. Flail with 2 Strength: 3 x 0.75 = 2.25, so 2x3 = 6.
- 1 point of Strength taken from a gardener removes 1 from its Bite or Lash and 3 from its Flail.

Special rules:
- Its only Block is the Skittish Block. It applies no debuffs. No phases, no summons.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Each turn the first attack card on a gardener lands in full. All later attack damage on that gardener that turn loses 6 in total, or all of it when it is under 6.
- Do not open with a small hit to "pop" the Block: the Block comes after the first card. On each gardener play the biggest attack first.
- One attack card per gardener per turn loses nothing. A card that hits all enemies lands in full when it is the first attack on each of them; after it all 4 have Block for the rest of the turn.
- e3 deals the most and e1 the least. With equal HP left, the kill order by damage saved is e3, e0, e2, e1.
- Incoming grows: 15, 17, 19, 25, 25, 27. A gardener that shows `buff` deals 0 this turn and 2 more per hit from then on.

## Lessons
(none yet)
