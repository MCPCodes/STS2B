# Twig Slime (M)
26-28 HP slime: opens with 1 Slimed, then attacks for 11 once or twice between each further Slimed.

## Facts (game files, v0.107.1)

Where (all act 1 Overgrowth; enemies act in e order):
- Weak fight SlimesWeak: 3 slimes. e1 is a medium slime: Twig Slime (M) in half of these fights, Leaf Slime (M) in the other half. e0 and e2 are 1 Leaf Slime (S) and 1 Twig Slime (S), either order.
- Normal fight SlimesNormal: always 4 slimes. e0 Twig Slime (M), e1 Leaf Slime (M), e2 and e3 Leaf Slime (S) and Twig Slime (S), either order.
- Normal fight FlyconidNormal: e0 a medium slime (Twig 50%, Leaf 50%), e1 Flyconid.
- Normal fight SlitheringStranglerNormal: in 1 of 6 of these fights e0 is Twig Slime (M) and e1 the Slithering Strangler.

HP: 26-28 (asc 8+: 27-29).

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Sticky Shot | `statuscard: 1` | Adds 1 Slimed to your discard pile. No damage. |
| Pokey Pounce | `attack 11` | 1 hit: 11 base (asc 9+: 12) + Strength. |

Pattern: turn 1 fixed, then random with limits.
- Enemy turn 1: Sticky Shot. Enemy turn 2: Pokey Pounce.
- After Sticky Shot: always Pokey Pounce.
- After 1 Pokey Pounce: Pokey Pounce 50%, Sticky Shot 50%.
- After 2 Pokey Pounces in a row: always Sticky Shot.
- So it never shows `statuscard: 1` twice in a row and never attacks 3 times in a row.

Worked numbers:
- Over a long fight it attacks 3 turns in 5: 6.6 damage per turn.
- With Vulnerable on you (Flyconid gives 2) Pokey Pounce is 11 x 1.5 = 16.5, so 16.
- Weak on it: Pokey Pounce is 8.

Special rules: it does not split or spawn anything when hit or killed. Its moves give it no Block and no Strength. It applies no debuffs.

Status card: Slimed. Status, costs 1, Exhaust, cannot be upgraded.
- Playing it draws 1 card. Then it is exhausted: gone for the fight.
- Left in hand it does nothing and is discarded with the rest of the hand.
- New copies go to the discard pile: you first draw them after the next shuffle.

## How to fight it
Derived from the facts above, not from play.
- Enemy turn 1 deals 0 from it. Enemy turn 2 is always 11.
- After `statuscard: 1` the next intent is `attack 11` for certain. After 2 attacks in a row the next turn is 0 from it.
- It has less HP and more damage than Leaf Slime (M): 26-28 HP for 6.6 per turn against 32-35 HP for 4. Kill it first.
- Slimed: play it with energy left over. 1 energy buys 1 draw and removes it for the fight. Unplayed, it takes a draw again after each shuffle.

## Lessons
(none yet)
