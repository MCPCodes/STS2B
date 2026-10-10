# Vantom
Overgrowth boss, 173 HP: Slippery cuts each of the first 8 damaging hits to 1 HP; cycle 7, 6x2, 26 + 3 Wounds, then +2 Strength.

## Facts (game files, v0.107.1)

Numbers are for ascension 0. "(asc: n)" is the higher-ascension value: HP from ascension 8, damage from ascension 9. From ascension 8 it also starts with more Slippery: read the amount on it.

Where: boss fight Vantom [VantomBoss], act 1 Overgrowth, one of its 3 bosses. Alone, enemy e0. Nothing joins. No randomness at all.

HP: 173 (asc: 183).

Starts with Slippery 8:
- While it has stacks, any HP loss of 1 or more is cut to exactly 1. This applies after Block and to every source: attack cards, potions, reflected damage, HP-loss effects.
- Each damage instance that removes HP removes 1 stack. Every hit of a multi-hit attack is its own instance. At 0 stacks Slippery is gone.
- A hit that removes 0 HP costs no stack.
- So the first 8 damaging hits remove 8 HP in total, whatever their size. The other 165 HP take normal damage.
- Slippery changes HP loss only. Powers a card applies (Vulnerable, Weak) land as usual.
- Card numbers do not show the cap. A card that shows 20 against it still removes 1 HP while stacks remain.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Ink Blot | `attack 7` | 1 hit: 7 base (asc: 8) + Strength. |
| Inky Lance | `attack 6x2` | 2 hits: 6 base each (asc: 7) + Strength on every hit. |
| Dismember | `attack 26`, `statuscard: 3` | 1 hit: 26 base (asc: 30) + Strength. Then adds 3 Wounds to your discard pile, blocked or not. |
| Prepare | `buff` | Gains 2 Strength. No damage. |

Pattern: fixed 4-move cycle, no randomness: Ink Blot, Inky Lance, Dismember, Prepare, repeat. It starts with Ink Blot. No phases; its animations between moves change nothing.

Damage by enemy turn, when nothing changes its Strength:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Move | Ink Blot | Inky Lance | Dismember | Prepare | Ink Blot | Inky Lance | Dismember | Prepare | Ink Blot | Inky Lance | Dismember | Prepare |
| Damage | 7 | 6x2 = 12 | 26 | 0 | 9 | 8x2 = 16 | 28 | 0 | 11 | 10x2 = 20 | 30 | 0 |
| Wounds added | 0 | 0 | 3 | 0 | 0 | 0 | 3 | 0 | 0 | 0 | 3 | 0 |

Each 4-turn cycle deals 8 more than the last: 45, 53, 61.

Worked numbers:
- Slippery 8, Twin Strike 5x2: 2 HP lost, 6 stacks left. A single hit of 20: 1 HP lost, 7 stacks left.
- Slippery 1, a 6x2 attack: hit 1 removes 1 HP and the last stack, hit 2 removes 6.
- Fire Potion (20) with stacks up removes 1 HP and 1 stack.
- Weak on it rounds down per hit. Turn 3 Dismember: 26 x 0.75 = 19.5, so 19. Turn 2 Inky Lance: 6 x 0.75 = 4.5, so 4x2 = 8.
- 1 point of Strength taken from it removes 1 from Ink Blot and Dismember and 2 from Inky Lance.

Special rules:
- It never gains Block. It applies no debuffs. No summons.

Status card: Wound. Status, Unplayable, cannot be upgraded.
- It has no effect. It takes up a draw and a hand slot.
- It does not exhaust itself: each copy cycles through your deck for the rest of the fight.
- New copies start in the discard pile, so you draw them after the next reshuffle.

## How to fight it
Derived from the facts above, not from play.
- Strip the 8 stacks with your cheapest hits: multi-hit cards and low-cost attacks. Count hits, not damage.
- Hold big single hits and damage potions until Slippery is gone; before that each is worth 1 HP.
- Enemy turns 3, 7, 11 are the big hit (26, 28, 30). Turns 4, 8, 12 deal 0: play no Block there.
- After Slippery it has 165 HP left. Each extra cycle costs 8 more damage and 3 more Wounds in the deck.
- Vulnerable on it adds no damage while Slippery is up. Apply it when the stacks are nearly gone.

## Lessons
(none yet)
