# Snapping Jaxfruit
Overgrowth hallway enemy, 31-33 HP: every turn it hits 3 + its Strength, then gains 2 Strength, so 3, 5, 7, 9 and up.

## Facts (game files, v0.107.1)

Where: act 1 Overgrowth only. It is e0 and acts first in both fights.
- Hallway fight SnappingJaxfruitNormal (normal): Snapping Jaxfruit e0, Flyconid e1.
- Hallway fight SlitheringStranglerNormal (normal), 1 fight in 3: Snapping Jaxfruit e0, Slithering Strangler e1.
- Normal fights come from hallway fight 4 on, each at most once per act.

Companions:
- Flyconid: 47-49 HP. Each turn 1 of: 8 damage + 2 Frail on you, 11 damage, or 2 Vulnerable on you. Its first move is never the Vulnerable one.
- Slithering Strangler: 53-55 HP. Adds 3 Constrict on odd enemy turns, hits 7 or 12 on even ones.

HP: 31-33 (asc 8+: 34-36).

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Energy Orb | `attack 3`, `buff` | 1 hit: 3 base (asc 9+: 4) + its Strength. Then it gains 2 Strength. |

Pattern: the same move every enemy turn. No randomness.
- The Strength comes after the hit. Damage on enemy turn N is 3 + 2 x (N - 1): 3, 5, 7, 9, 11, 13.
- Running total: 3, 8, 15, 24, 35, 48.

Special rules:
- Strength adds to every hit it deals, before multipliers. It never decays.
- The attack intent already includes its current Strength: it shows this turn's hit. Do not add Strength again.
- With the Flyconid: its 2 Vulnerable lands after the Jaxfruit has hit that turn, then raises both enemies' hits x 1.5 on the next 2 enemy turns. Worked, Jaxfruit with 4 Strength: (3 + 4) x 1.5 = 10.5, deals 10.
- It never gains Block and applies no debuffs.
- No summon, heal, escape, on-death effect or reaction to hits or cards. The fight ends when every enemy is dead.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Every turn it lives adds 2 to each of its later hits. It has the fewest HP in both fights (31-33 against 47-49 or 53-55): kill it first.
- Block needed for it alone on turn N: 2 x N + 1.
- Next turn's hit is this turn's intent + 2.

## Lessons
(none yet)
