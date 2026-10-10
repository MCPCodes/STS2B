# Inklet
11-17 HP, in a pack of 3, each with Slippery 1 (the first hit that gets through takes 1 HP): alternates attack 3 with attack 10 or 2x3.

## Facts (game files, v0.107.1)

Where: normal fight InkletsNormal, act 1 Overgrowth. Always 3 Inklets, e0, e1, e2, acting in that order. e1 is the middle Inklet and starts on a different move.

HP: 11-17 each (asc 8+: 12-18). The 3 always have 3 different values. Total 36-48 (asc 8+: 39-51).

Starts with Slippery 1:
- While an Inklet has Slippery, any hit that would take 1 HP or more takes exactly 1.
- A hit that took HP then removes 1 Slippery. With Slippery 1 that is the first hit that gets through. Every later hit does full damage.
- Each hit of a multi-hit card is its own hit.
- It caps every source: attacks, potions, powers, relics.
- Card text, `vs` and potion numbers do not show it. Subtract it yourself.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Jab | `attack 3` | 1 hit: 3 base (asc 9+: 4) + Strength. |
| Whirlwind | `attack 2x3` | 3 hits: 2 base each (asc 9+: 3) + Strength on every hit. |
| Piercing Gaze | `attack 10` | 1 hit: 10 base (asc 9+: 11) + Strength. |

Pattern: each Inklet alternates Jab with a big move. The big move is Piercing Gaze or Whirlwind, 50% each, picked anew every time.
- e0 and e2: Jab on enemy turns 1, 3, 5, ... Big move on turns 2, 4, 6, ...
- e1: Whirlwind on turn 1, always. Jab on turns 2, 4, 6, ... Big move on turns 3, 5, 7, ...
- An Inklet showing `attack 3` shows `attack 10` or `attack 2x3` next turn. One showing `attack 10` or `attack 2x3` shows `attack 3` next turn.

Damage with all 3 alive:

| Enemy turn | 1 | 2, 4, 6 | 3, 5, 7 |
| --- | --- | --- | --- |
| Damage | 3 + 6 + 3 = 12 | 15, 19 or 23 | 12 or 16 |

Worked numbers:
- Strike (6) on a fresh Inklet: 1 HP, Slippery gone. The next Strike: 6.
- Twin Strike (5x2) on a fresh Inklet: 1 + 5 = 6.
- A 20 damage potion on a fresh Inklet: 1.
- An attack on all enemies takes 1 HP from each fresh Inklet and removes all 3 Slippery.
- Weak on an Inklet rounds down per hit: Whirlwind 2 x 0.75 = 1.5, so 1x3 = 3. Jab 2. Piercing Gaze 7.
- Vulnerable on you: Whirlwind 3x3 = 9. Jab 4. Piercing Gaze 15.

Special rules: they never gain Block or Strength, apply no debuffs and summon nothing. Killing one changes nothing for the others. All 3 must die.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Open each Inklet with your smallest hit. An attack on all enemies opens all 3 at once. Never open with a big hit or a damage potion.
- Count a kill as 1 for the first hit, then full damage. An 11 HP Inklet takes 3 Strikes: 1 + 6 + 6.
- This turn: a kill on an Inklet showing `attack 10` or `attack 2x3` saves the most.
- Next turn: the Inklets showing `attack 3` now are the ones that hit big.
- Block: enemy turn 1 is 12. Even turns reach 23 while all 3 live.

## Lessons
(none yet)
