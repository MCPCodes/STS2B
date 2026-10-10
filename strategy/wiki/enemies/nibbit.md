# Nibbit
42-46 HP, alone or as a pair: fixed loop of attack 12, attack 6 with 5 Block, and +2 Strength with no damage.

## Facts (game files, v0.107.1)

Where (act 1 Overgrowth):
- Weak fight NibbitsWeak: 1 Nibbit, alone, e0. It opens with Butt.
- Normal fight NibbitsNormal: 2 Nibbits. e0 (front) acts first and opens with Slice. e1 (back) opens with Hiss.

HP: 42-46 (asc 8+: 44-48). In the pair the 2 always differ.

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Butt | `attack 12` | 1 hit: 12 base (asc 9+: 13) + Strength. |
| Slice | `attack 6`, `defend` | 1 hit: 6 base (asc 9+: 7) + Strength. Then gains 5 Block. |
| Hiss | `buff` | Gains 2 Strength. No damage. |

Pattern: fixed, no randomness. Butt, Slice, Hiss, Butt, ... Only the starting point differs.
- After `attack` + `defend` comes `buff`. After `buff` comes Butt. After Butt comes `attack` + `defend`.

By enemy turn, when nothing else changes Strength:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Alone | 12 | 6 +Block | buff | 14 | 8 +Block | buff | 16 | 10 +Block | buff |
| Pair, e0 | 6 +Block | buff | 14 | 8 +Block | buff | 16 | 10 +Block | buff | 18 |
| Pair, e1 | buff | 14 | 8 +Block | buff | 16 | 10 +Block | buff | 18 | 12 +Block |
| Pair total | 6 | 14 | 22 | 8 | 16 | 26 | 10 | 18 | 30 |

Worked numbers:
- The 5 Block from Slice is there for your whole next turn and gone when the enemy turn starts. A Strike (6) into it takes 1 HP. Against a Vulnerable Nibbit: 9 - 5 = 4.
- Weak on it rounds down: Butt at 2 Strength is 14 x 0.75 = 10.5, so 10. Slice at 2 Strength is 6.
- Vulnerable on you: Butt at 2 Strength is 21.

Special rules: its Strength stays for the fight. It applies no debuffs and summons nothing. In the pair, killing one changes nothing for the other.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Alone: enemy turns 3, 6, 9 deal 0.
- Pair: turns 1 and 4 are light (6, 8). On turns 3, 6, 9 both attack (22, 26, 30): keep Block for those.
- A Nibbit that just used Slice holds 5 Block on your turn and only buffs next. In the pair, hit the other one that turn, or lose 5 damage.
- Pair: e1 holds no Block on your turns 1-3; e0 holds 5 on your turn 2. Focusing e1 first loses nothing to Block early.
- A Nibbit showing `buff` hits for 12 + its new Strength next turn.

## Lessons
(none yet)
