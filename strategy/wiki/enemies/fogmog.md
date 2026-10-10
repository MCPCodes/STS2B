# Fogmog
74 HP: summons an Eye with Teeth on turn 1, then mixes attack 8 with +1 Strength and attack 14; its death ends the fight.

## Facts (game files, v0.107.1)

Where: normal fight FogmogNormal, act 1 Overgrowth. It starts alone as e0. Its turn 1 move adds 1 Eye with Teeth as e1 (see eye-with-teeth.md).

HP: 74 (asc 8+: 78). Always the same number.

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Illusion (bestiary: Illusory Spores) | `summon` | Adds 1 Eye with Teeth (6 HP). No damage. Used once per fight. |
| Swipe (bestiary: Thwack) | `attack 8`, `buff` | 1 hit: 8 base (asc 9+: 9) + Strength. Then gains 1 Strength. |
| Headbutt | `attack 14` | 1 hit: 14 base (asc 9+: 16) + Strength. |

Pattern:
- Enemy turn 1: Illusion. Enemy turn 2: Swipe.
- After Headbutt: always Swipe.
- After 1 Swipe: Headbutt 60%, a second Swipe 40%.
- After 2 Swipes in a row: always Headbutt.
- Its Strength is the number of Swipes so far, when nothing else changes it.

First turns:

| Enemy turn | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- |
| 60% of fights | summon | Swipe 8 | Headbutt 15 | Swipe 9 | Headbutt 16 (60%) or Swipe 10 (40%) |
| 40% of fights | summon | Swipe 8 | Swipe 9 | Headbutt 16 | Swipe 10 |

Special rules:
- From enemy turn 2 the Eye acts before Fogmog every enemy turn, although it is e1. It adds 3 Dazed to your discard pile, except on a turn it starts at 0 HP.
- Fogmog is the only main enemy. When it dies the Eye dies with it and the fight is won, whatever the Eye's HP.
- Only 1 Eye is ever summoned. A killed Eye is back at 6 HP on your next turn.
- Fogmog never gains Block and applies no debuffs.

Status cards: none from Fogmog. The Eye adds Dazed: Status, Unplayable, Ethereal. It cannot be played. It is exhausted at the end of a turn it spends in your hand.

## How to fight it
Derived from the facts above, not from play.
- Enemy turn 1 deals 0: no Block on turn 1.
- Only Fogmog's 74 HP wins the fight. The Eye never has to die.
- From turn 2, each turn is a choice: 6 damage on the Eye (a plain Strike) cancels that turn's 3 Dazed, or 3 dead draws arrive after the next shuffle.
- After Headbutt (`attack` with no `buff`) the next move is Swipe, the smaller hit. After 2 turns of `attack` + `buff` in a row the next is Headbutt.
- On the turn Fogmog can die, put nothing into the Eye.

## Lessons
(none yet)
