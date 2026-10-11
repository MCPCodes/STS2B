# Fossil Stalker
51-53 HP, alone: attack 12 on turn 1, then a random mix of 12, 9 with 1 Frail, and 3x2; each hit that takes HP gives it 3 Strength.

## Facts (game files, v0.107.1)

Where: act 1 Underdocks only. Normal fight Fossil Stalker [FossilStalkerNormal]: alone, enemy e0. Nothing joins. Normal fights come from hallway fight 4 on.

HP: 51-53 (asc 8+: 54-56).

Co-op: HP x players x 1.1, rounded down (2 players: 112, 114 or 116). Suck and damage are not scaled; every attack hits every player. A hit counts once for Suck when any player lost HP to it. (Read from the code, not seen in a fight.)

Starts with Suck 3 (same at every ascension):
- After each of its attacks it gains 3 Strength for every hit of that attack that took at least 1 HP from you.
- A hit blocked in full gives it nothing. A hit that takes 1 HP gives the same 3 as a hit that takes 12.
- The Strength arrives after the whole attack: both hits of Lash use the old number.
- The Strength stays for the fight and adds to every later hit.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Latch | `attack 12` | 1 hit: 12 base (asc 9+: 14) + Strength. |
| Tackle | `attack 9`, `debuff` | 1 hit: 9 base (asc 9+: 11) + Strength. Then 1 Frail on you, blocked or not. |
| Lash | `attack 3x2` | 2 hits: 3 base each (asc 9+: 4) + Strength on every hit. |

Pattern: random after turn 1. It attacks every turn.
- Enemy turn 1: Latch, always.
- Every later turn: Latch, Tackle or Lash, 1/3 each.
- Exception: a move it used on both of the last 2 turns is barred. The other 2 are then 1/2 each. So never the same move 3 times in a row.
- Enemy turn 2 is 1/3 each, Latch included. After Latch, Latch, turn 3 is Tackle or Lash.
- The intent on your turn shows which move comes.

Hits by its Strength:

| Strength | 0 | 3 | 6 | 9 |
| --- | --- | --- | --- | --- |
| Latch | 12 | 15 | 18 | 21 |
| Tackle | 9 | 12 | 15 | 18 |
| Lash | 3x2 = 6 | 6x2 = 12 | 9x2 = 18 | 12x2 = 24 |

Worked numbers:
- No Block on turn 1: Latch takes 12 HP, Strength 3. Turn 2 is then Latch 15, Tackle 12 or Lash 6x2. An unblocked Lash 6x2 leaves it at Strength 9.
- Latch 12 into 11 Block: 1 HP lost, and still +3 Strength.
- Lash 3x2 into 3 Block: hit 1 is blocked in full, hit 2 takes 3 HP: +3 Strength. Into 5 Block: hit 2 still takes 1 HP: +3 Strength. Into 6 Block: nothing gets through, no Strength.
- Block that denies all growth = the whole attack: Latch 12 + Strength, Tackle 9 + Strength, Lash 2 x (3 + Strength).
- Each +3 Strength adds 3 to every later Latch and Tackle and 6 to every later Lash.
- Weak on it rounds down per hit: Lash 3 x 0.75 = 2.25, so 2x2 = 4. Latch 9. Tackle 9 x 0.75 = 6.75, so 6.
- Vulnerable on you: Latch 18. Tackle 9 x 1.5 = 13.5, so 13. Lash 4x2 = 8.

Special rules:
- Frail 1 from Tackle covers exactly your next turn: Block from cards is x0.75, rounded down per card (Defend 5 gives 3). It lands after the hit and does not cut Block you already hold.
- In Tackle the Strength from Suck arrives before the Frail. No number changes.
- It never gains Block. No summon, heal, escape or on-death effect.
- One fight against it is recorded and it died on turn 1 without moving. Its moves and Suck are read from the code, not seen.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Its damage grows only when a hit takes HP. Block a hit in full or not at all: Block that leaves 1 through saves HP but still gives it 3 Strength.
- Enemy turn 1 is always Latch 12. 12 Block on your turn 1 keeps it at Strength 0.
- Lash is the move to block in full: it can give 6 Strength, twice what Latch or Tackle can. That takes 2 x (3 + Strength) Block: 6 at Strength 0.
- After Tackle your next turn is Frail: Defend gives 3. Full Block costs more cards that turn.
- It is alone and attacks every turn. Each turn it lives is 1 more chance for it to grow, so damage dealt early is worth the most.

## Lessons
(none yet)
