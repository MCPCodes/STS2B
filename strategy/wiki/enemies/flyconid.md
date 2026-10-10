# Flyconid
47-49 HP: random mix of attack 11, attack 8 with 2 Frail, and 2 Vulnerable with no damage; never the same move twice in a row.

## Facts (game files, v0.107.1)

Where (act 1 Overgrowth):
- Normal fight FlyconidNormal: e0 a medium slime (Leaf Slime (M) or Twig Slime (M), 50% each), e1 Flyconid.
- Normal fight SnappingJaxfruitNormal: e0 Snapping Jaxfruit, e1 Flyconid.
- It acts last in both. Its debuffs land after its partner's move, so they never change the partner's hit on that same turn.

HP: 47-49 (asc 8+: 51-53).

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Vulnerable Spores | `debuff` | Puts 2 Vulnerable on you. No damage. |
| Frail Spores | `attack 8`, `debuff` | 1 hit: 8 base (asc 9+: 9) + Strength. Then puts 2 Frail on you. |
| Smash | `attack 11` | 1 hit: 11 base (asc 9+: 12) + Strength. |

Pattern: random, limited by cooldowns.
- Enemy turn 1: Frail Spores or Smash, 50% each. Never Vulnerable Spores.
- Later turns: equal chance among the moves not barred. Vulnerable Spores is barred if it was any of its last 3 moves, Frail Spores if it was either of its last 2, Smash if it was its last move.
- Exception: when all 3 are barred (last 3 moves were Vulnerable Spores, Frail Spores, Smash, in that order) it uses Vulnerable Spores.
- Over a long fight: Smash 3 turns in 7, each spore move 2 in 7.

Next move from its last moves:

| Last move | Before it | Next move |
| --- | --- | --- |
| Smash | nothing (turn 1) | Vulnerable Spores or Frail Spores, 50% each |
| Smash | Frail Spores | Vulnerable Spores |
| Smash | Vulnerable Spores | Frail Spores |
| Frail Spores | Vulnerable Spores 1 or 2 moves earlier | Smash |
| Frail Spores | no Vulnerable Spores in the 2 moves before it | Vulnerable Spores or Smash, 50% each |
| Vulnerable Spores | Frail Spores | Smash |
| Vulnerable Spores | Smash | Frail Spores or Smash, 50% each |

Worked numbers:
- Vulnerable 2 put on you on enemy turn T raises enemy hits on enemy turns T+1 and T+2. Smash 11 x 1.5 = 16.5, so 16. Frail Spores 12. Leaf Slime (M) 12. Twig Slime (M) 16. Snapping Jaxfruit at 4 Strength: 7 x 1.5 = 10.5, so 10.
- Frail 2 cuts card Block on your next 2 turns. Defend 5 x 0.75 = 3.75, so 3. An 8 Block card gives 6. Block from powers, relics and potions is not cut.
- The hit of Frail Spores lands before the Frail. Block you already hold is not cut.
- Each spore debuff has run out before the same spore move can come again, so each one starts fresh at 2.

Special rules: it never gains Block or Strength. No summons, no on-death effect.

Status cards: none from the Flyconid. A slime partner adds Slimed (see leaf-slime-m.md, twig-slime-m.md).

## How to fight it
Derived from the facts above, not from play.
- It always attacks on enemy turn 1: 8 with Frail, or 11.
- A lone `debuff` intent: 0 damage from it this turn, then 2 enemy turns at x1.5. Attack on the `debuff` turn, keep Block for the next 2.
- After Frail Spores (`attack` + `debuff`) your Defends give 3 for 2 turns. Put those turns into damage, or into Block from potions and relics.
- After Frail Spores then Smash the next move is Vulnerable Spores: 0 damage from it that turn.
- After Smash (`attack` alone) the next move is never Smash: it deals 0, or 8 (12 while you are Vulnerable).

## Lessons
(none yet)
