# Leaf Slime (M)
32-35 HP slime: adds 2 Slimed to your discard pile on odd turns and attacks for 8 on even turns; fixed, no randomness.

## Facts (game files, v0.107.1)

Where (all act 1 Overgrowth; enemies act in e order):
- Weak fight SlimesWeak: 3 slimes. e1 is a medium slime: Leaf Slime (M) in half of these fights, Twig Slime (M) in the other half. e0 and e2 are 1 Leaf Slime (S) and 1 Twig Slime (S), either order.
- Normal fight SlimesNormal: always 4 slimes. e0 Twig Slime (M), e1 Leaf Slime (M), e2 and e3 Leaf Slime (S) and Twig Slime (S), either order.
- Normal fight FlyconidNormal: e0 a medium slime (Leaf 50%, Twig 50%), e1 Flyconid.
- Normal fight SlitheringStranglerNormal: in 1 of 6 of these fights e0 is Leaf Slime (M) and e1 the Slithering Strangler.

HP: 32-35 (asc 8+: 33-36).

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Sticky Shot | `statuscard: 2` | Adds 2 Slimed to your discard pile. No damage. |
| Clump Shot | `attack 8` | 1 hit: 8 base (asc 9+: 9) + Strength. |

Pattern: fixed, no randomness. Sticky Shot on enemy turns 1, 3, 5, ... Clump Shot on enemy turns 2, 4, 6, ... The next move is the one it did not just use.

Worked numbers:
- Damage by enemy turn: 0, 8, 0, 8. 4 per turn on average.
- With Vulnerable on you (Flyconid gives 2) Clump Shot is 12.
- Weak on it: Clump Shot is 6.

Special rules: it does not split or spawn anything when hit or killed. Its moves give it no Block and no Strength. It applies no debuffs.

Status card: Slimed. Status, costs 1, Exhaust, cannot be upgraded.
- Playing it draws 1 card. Then it is exhausted: gone for the fight.
- Left in hand it does nothing and is discarded with the rest of the hand.
- New copies go to the discard pile: you first draw them after the next shuffle.

## How to fight it
Derived from the facts above, not from play.
- Enemy turns 1, 3, 5 deal 0 from it. Count only its even turns when planning Block.
- It averages 4 damage per turn; Twig Slime (M) averages 6.6 with less HP. In SlimesNormal kill the Twig first.
- Killing it on your turn 1, 3 or 5 stops 2 Slimed. Killing it on your turn 2, 4 or 6 stops an 8 hit.
- Slimed: play it with energy left over. 1 energy buys 1 draw and removes it for the fight. Unplayed, it takes a draw again after each shuffle.

## Lessons
(none yet)
