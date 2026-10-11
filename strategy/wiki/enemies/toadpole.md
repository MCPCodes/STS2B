# Toadpole
21-25 HP, fought as a pair: fixed loop of attack 7, Thorns 2 for one turn, attack 3x3; the front one starts on Thorns.

## Facts (game files, v0.107.1)

Where: weak fight Toadpoles [ToadpolesWeak], act 1 Underdocks. 2 Toadpoles. e0 (front) acts first and opens with Spiken. e1 (back) opens with Whirl. Weak fights are hallway fights 1-3.

HP: 21-25 each (asc 8+: 22-26). In single player the 2 always differ.

Co-op: HP x players x 1.1, rounded down (2 players: 46, 48, 50, 52 or 55 each). The 2 can have the same HP (seen: 52 and 52). Thorns and damage are not scaled; every attack hits every player. Thorns damage goes only to the player who attacks.

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Whirl | `attack 7` | 1 hit: 7 base (asc 9+: 8) + Strength. |
| Spiken | `buff` | Gains Thorns 2 (same at every ascension). No damage, no Block. |
| Spike Spit | `attack 3x3` | First loses its 2 Thorns. Then 3 hits: 3 base each (asc 9+: 4) + Strength on every hit. |

Pattern: fixed loop of 3, no randomness: Whirl, Spiken, Spike Spit. Only the starting point differs.
- After `attack 7` comes `buff`. After `buff` comes `attack 3x3`. After `attack 3x3` comes `attack 7`.

By turn, both alive:

| Turn | 1 | 2 | 3 | 4 | 5 | 6 |
| --- | --- | --- | --- | --- | --- | --- |
| Thorns 2 during your turn | nobody | e0 | e1 | nobody | e0 | e1 |
| e0 on the enemy turn | buff | 3x3 = 9 | 7 | buff | 9 | 7 |
| e1 on the enemy turn | 7 | buff | 9 | 7 | buff | 9 |
| Total damage | 7 | 9 | 16 | 7 | 9 | 16 |

Thorns:
- While a Toadpole has Thorns 2, every attack hit on it deals 2 damage to you. It lands before your hit does.
- Your Block absorbs it. Strength, Weak and Vulnerable, on you or on it, do not change it.
- It is paid per hit: a 2-hit attack costs 4. An attack on all enemies costs 2 for each Toadpole that has Thorns.
- A hit that is fully blocked or deals 0 still costs 2. So does the hit that kills it. Hits of a multi-hit attack after it is dead cost nothing.
- Damage that is not an attack (from powers, relics) does not set it off. One exception: the second hit of the colorless card Omnislice does.
- It lasts exactly 1 of your turns: gained on Spiken's enemy turn, up for your whole next turn, removed when Spike Spit starts.
- The Toadpole showing `attack 3x3` has Thorns now. The one showing `buff` has none yet and will have it on your next turn.
- The Thorns damage itself is read from the code. Nobody was seen taking it in a fight.

Worked numbers:
- Your turn 2: 3 Strikes into e0 cost you 6. 3 Strikes into e1 cost 0.
- Thorns uses up your Block first: with 5 Block, 2 hits on a Thorns Toadpole leave 1 Block for the enemy turn.
- Spike Spit into Block: each hit meets the Block that is left. 3x3 into 5 Block: 3 blocked, then 2 blocked and 1 through, then 3 through. 4 HP lost. Same as 9 - 5.
- Weak on it rounds down per hit: Spike Spit 3 x 0.75 = 2.25, so 2x3 = 6. Whirl 7 becomes 5.

Special rules:
- Killing one changes nothing for the other. Both must die.
- They never gain Block or Strength and apply no debuffs. No summon, heal, escape or on-death effect.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Each turn, attack the Toadpole without Thorns: either on turn 1, e1 on turn 2, e0 on turn 3, either on turn 4.
- Focus e1 first: it can be hit free on turns 1 and 2, and its death takes 9 off enemy turn 3.
- Enemy turn 3 is the big one with both alive: 16, against 7 and 9 on turns 1 and 2.
- If the Thorns one has to be hit, use few big hits, not many small ones. An attack on all enemies costs 2 on turns 2 and 3.
- 2 damage per hit is small next to a kill: finishing a Thorns Toadpole about to deal 9 is worth the 2.

## Lessons
(none yet)
