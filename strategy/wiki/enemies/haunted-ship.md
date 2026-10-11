# Haunted Ship
63 HP, alone: enemy turn 1 puts 3 Weak on you and 5 Dazed in your discard pile, then it alternates attack 13 and attack 4x3.

## Facts (game files, v0.107.1)

Where: act 1 Underdocks only. Normal fight Haunted Ship [HauntedShipNormal]: alone, enemy e0. Nothing joins. Normal fights come from hallway fight 4 on.

HP: 63, always (asc 8+: 67).

Co-op: HP x players x 1.1, rounded down (2 players: 138). Damage, Weak and Dazed are not scaled; every attack hits every player, and each player gets the 3 Weak and the 5 Dazed. (2 players seen in a fight: 138 HP.)

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Haunt | `debuff`, `statuscard: 5` | 3 Weak on you. Then 5 Dazed into your discard pile. No damage. Used once, on enemy turn 1. |
| Swipe | `attack 13` | 1 hit: 13 base (asc 9+: 14) + Strength. |
| Stomp | `attack 4x3` | 3 hits: 4 base each (asc 9+: 5) + Strength on every hit. |

Pattern: fixed, no randomness.
- Enemy turn 1: Haunt.
- Then Swipe and Stomp alternate, Swipe first: 13 on even enemy turns, 4x3 = 12 on odd enemy turns from 3.

By enemy turn:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Move | Haunt | Swipe | Stomp | Swipe | Stomp | Swipe | Stomp | Swipe |
| Damage | 0 | 13 | 4x3 = 12 | 13 | 4x3 = 12 | 13 | 4x3 = 12 | 13 |
| Running total | 0 | 13 | 25 | 38 | 50 | 63 | 75 | 88 |

Special rules:
- The 3 Weak covers your turns 2, 3 and 4 (it reads 3, 2, 1) and is gone on your turn 5. Your turn 1 is at full damage.
- While Weak your attacks deal x0.75, rounded down per hit. Block and Powers are not changed.
- The 5 Dazed are created in your discard pile. You first draw them after your next shuffle.
- It never gains Strength or Block. Its damage never grows.
- No summon, heal, escape or on-death effect.
- Seen in real fights, single player and co-op: Haunt, Swipe, Stomp in that order. In co-op the intents read `debuff` + `statuscard: 5`, then `attack 13`, then `attack 4x3`, and Weak read 2 on turn 3.

Worked numbers:
- Your attacks on turns 2-4: Strike 6 x 0.75 = 4.5, deals 4. Bash 8 deals 6.
- Weak on you and Vulnerable on the ship together: Strike 6 x 0.75 x 1.5 = 6.75, deals 6. Bash 8 x 0.75 x 1.5 = 9.
- The damage number on a card in your hand already includes your Weak. Do not cut it again.
- Stomp into Block: each hit meets the Block that is left. 4x3 into 10 Block: 4 blocked, 4 blocked, then 2 blocked and 2 through. 2 HP lost. Same as 12 - 10.
- Weak on the ship rounds down per hit: Swipe 13 x 0.75 = 9.75, so 9. Stomp 3x3 = 9.
- Vulnerable on you: Swipe 13 x 1.5 = 19.5, so 19. Stomp 6x3 = 18.

Status card: Dazed. Status, Unplayable, Ethereal, cannot be upgraded.
- It cannot be played. In your hand at end of turn it is exhausted: gone for the fight.
- Each Dazed takes 1 of your draws, once.

## How to fight it
Derived from the facts above, not from play.
- Enemy turn 1 deals 0 and your turn 1 is your last full-damage turn until turn 5: spend all of turn 1 on attacks, no Block.
- Your turns 2, 3 and 4 are Weak while it hits 13, 12, 13. Attacks deal 75% and Block is full: lean those turns toward Block and Powers.
- It has no scaling: 13 and 12 forever, 25 per 2 turns. The only clock is your HP.
- The 5 Dazed are 5 dead draws after your next shuffle. They leave by themselves; hands in that pass through the deck are smaller.
- Vulnerable on it helps most on your Weak turns: a Strike deals 6 instead of 4.

## Lessons
(none yet)
