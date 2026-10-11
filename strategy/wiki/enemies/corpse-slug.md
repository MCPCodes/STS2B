# Corpse Slug
25-27 HP, in a pair or a trio: loops 3x2, 8 and 2 Frail; when one dies, each other slug loses a turn and gains 4 Strength.

## Facts (game files, v0.107.1)

Where (act 1 Underdocks):
- Weak fight Corpse Slugs [CorpseSlugsWeak]: 2 slugs, e0 and e1. Weak fights are hallway fights 1-3.
- Normal fight Many Corpse Slugs [CorpseSlugsNormal]: 3 slugs, e0, e1, e2. Normal fights come from hallway fight 4 on.
- They act in the order e0, e1, e2.
- Both fights carry the tag Slugs: one does not come right after the other in the act's fight list when that can be avoided.

HP: 25-27 each (asc 8+: 27-29). The pair always has 2 different values. The trio is always 25, 26 and 27, one each: 78 in total.

Co-op: HP x players x 1.1, rounded down (2 players: 55, 57 or 59). Two slugs can then have the same HP. Ravenous and damage are not scaled; every attack hits every player. (2 players seen in a fight: 57 and 59 HP, Ravenous 4.)

Starts with Ravenous 4 (asc 9+: 5):
- Each time another enemy dies, every living slug is stunned and gains 4 Strength, at once.
- Stunned: its intent changes to `stunned` and it does nothing in the coming enemy turn. The turn after, it does the move it had been showing, with the new Strength.
- The Strength stays for the fight and adds to every hit.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Whip Slap | `attack 3x2` | 2 hits: 3 base each (same at every ascension) + Strength on every hit. |
| Glomp | `attack 8` | 1 hit: 8 base (asc 9+: 9) + Strength. |
| Goop | `debuff` | 2 Frail on you. No damage. |
| (stunned) | `stunned` | Nothing. Only on the enemy turn after another slug died. |

Pattern: fixed loop of 3: Whip Slap, Glomp, Goop. Only the starting point is random.
- e0 starts on any of the 3 moves, 1/3 each. e1 starts 1 step later in the loop, e2 1 step later again.
- After `attack 3x2` comes `attack 8`. After `attack 8` comes `debuff`. After `debuff` comes `attack 3x2`.
- A stunned turn pushes that slug's loop back by 1 turn. No move is skipped.
- Trio, all alive: every enemy turn has 1 Whip Slap, 1 Glomp and 1 Goop: 14 damage and 2 Frail.

Pair, both alive, by enemy turn:

| e0, e1 start with | 1 | 2 | 3 | 4 on |
| --- | --- | --- | --- | --- |
| Whip Slap, Glomp | 6 + 8 = 14 | 8, Frail | Frail, 6 | repeats |
| Glomp, Goop | 8, Frail | Frail, 6 | 6 + 8 = 14 | repeats |
| Goop, Whip Slap | Frail, 6 | 6 + 8 = 14 | 8, Frail | repeats |

Hits by Strength:

| Strength | 0 | 4 (1 slug died) | 8 (2 slugs died) |
| --- | --- | --- | --- |
| Whip Slap | 3x2 = 6 | 7x2 = 14 | 11x2 = 22 |
| Glomp | 8 | 12 | 16 |

Ravenous, case by case:
- You kill a slug on your turn T: no other slug acts on enemy turn T. On enemy turn T+1 each does the move it was showing before the kill, 4 Strength higher.
- A slug can be stunned again once it has sat out a stunned turn. Kill 1 slug per turn and the others never act; each kill still adds 4 Strength.
- 2 slugs killed in the same turn, by 1 card or by 2: the third gains 8 Strength and loses only 1 turn.
- A stunned slug can still be hit. It keeps its Strength and Ravenous.
- A slug that dies during the enemy turn (inside its own attack, to damage dealt back such as Flame Barrier): a slug that has not acted yet that turn does nothing that turn and does its move the turn after. A slug that has already acted loses its next turn, then repeats the move it just did. Both gain 4 Strength at once. (Read from the code, not seen in a fight.)

Frail:
- Block from cards is x0.75, rounded down per card: Defend 5 gives 3.
- 2 Frail put on you fresh covers your next 2 turns. Added to Frail you already have, it adds 2.
- Trio, no kills: Frail reads 2 on your turn 2 and 1 more each turn after.
- Pair, no kills: once the first Goop lands, Frail never runs out. 4 arrive every 3 turns and 3 tick away.
- 1 slug left: it uses Goop every third turn. Put on fresh, the 2 Frail covers your next 2 turns.

Worked numbers:
- Weak on a slug rounds down per hit. Whip Slap: 3 x 0.75 = 2.25, so 2x2 = 4. At 4 Strength: 7 x 0.75 = 5.25, so 5x2 = 10. Glomp: 6, at 4 Strength 9.
- Vulnerable on you: Whip Slap 3 x 1.5 = 4.5, so 4x2 = 8. Glomp 12.
- Whip Slap into Block: each hit meets the Block that is left. 7x2 into 10 Block: 7 blocked, then 3 blocked and 4 through.

Special rules:
- It never gains Block. No summon, heal or escape. Every slug must die.
- Seen in real fights: the pair, with `stunned` and Strength 4 after a kill, then `attack 7x2`. The trio has not been seen yet.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- A kill works as Block: no other slug acts in the enemy turn after it. Make the kill on a turn the others show their attacks.
- Each kill also gives every survivor 4 Strength for the rest of the fight: 8 more per Whip Slap, 4 more per Glomp. Kills far apart are paid for in between.
- Best case: bring all slugs low first, then kill at least 1 on every turn. From the first kill on, no slug acts again.
- If a survivor will get a turn, count it at Strength 4 (7x2, 12) or 8 (11x2, 16) and read its move from the loop.
- With 2 or 3 alive you are Frail on almost every turn: Defend gives 3. A kill protects better than card Block.

## Lessons
(none yet)
