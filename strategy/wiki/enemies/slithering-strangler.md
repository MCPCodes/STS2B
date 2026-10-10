# Slithering Strangler
Overgrowth hallway enemy, 53-55 HP: adds 3 Constrict (damage at the end of your turns) on odd enemy turns, hits 7 or 12 on even ones.

## Facts (game files, v0.107.1)

Where: act 1 Overgrowth only. Hallway fight SlitheringStranglerNormal (normal). Normal fights come from hallway fight 4 on, each at most once per act.
- The Strangler is always the last enemy (e1 or e2) and acts last.
- Before it, 1 of 3 companion groups, 1/3 each:
  - Snapping Jaxfruit: 31-33 HP. Hits 3, 5, 7, 9, +2 every turn.
  - 1 medium slime, 50/50: Leaf Slime (M), 32-35 HP, 2 Slimed on odd enemy turns and 8 damage on even ones; or Twig Slime (M), 26-28 HP, 1 Slimed on turn 1, 11 damage on turn 2, then 11 damage or 1 Slimed (never Slimed twice in a row, at most 2 hits in a row).
  - 2 small slimes, each 50/50 and both can be the same kind: Leaf Slime (S), 11-15 HP, alternates 3 damage and 1 Slimed; Twig Slime (S), 7-11 HP, 4 damage every turn.

HP: 53-55 (asc 8+: 54-56).

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Constrict | `debuff` | Adds 3 Constrict to you. No damage, no Block. |
| Thwack | `attack 7`, `defend` | 1 hit: 7 (asc 9+: 8). Then it gains 5 Block. |
| Lash | `attack 12` | 1 hit: 12 (asc 9+: 13). |

Pattern:
- Odd enemy turns (1, 3, 5, ...): Constrict, always.
- Even enemy turns: Thwack or Lash, 50/50 each time, with no memory of earlier picks.

Special rules:
- Constrict is a debuff on you. At the end of each of your turns, after the hand is discarded, you take damage equal to its amount.
- Your Block from that turn is still up and absorbs it first. The enemy attacks then hit the Block that is left.
- It is not attack damage: Vulnerable on you does not raise it and Weak on the Strangler does not lower it.
- It never counts down. Each Constrict move adds 3 to the same stack.
- Damage at the end of your turn: 0 on turn 1; 3 on turns 2 and 3; 6 on turns 4 and 5; 9 on turns 6 and 7.
- Running total if never blocked: 6 after turn 3, 18 after turn 5, 36 after turn 7.
- The snapshot's `incoming_damage` counts attack intents only. Add the Constrict amount on you to it.
- Constrict is removed the moment the Strangler dies, also while companions live. Killing companions does not remove it.
- Unblocked Constrict damage counts as damage taken on your own turn: it triggers Rupture and Inferno.
- Thwack's 5 Block is gained after its hit, stays through your next turn (an odd turn) and is removed before it acts again. It has 0 Block during your even turns and your turn 1.
- No summon, heal, escape, on-death effect or reaction to hits or cards. The fight ends when every enemy is dead.

Worked numbers:
- Constrict 3, intent `attack 12`, you end the turn with 10 Block: Constrict takes Block to 7, then Lash deals 12 - 7 = 5. Taking 0 needs 15 Block.
- Constrict 3 and 2 Block at the end of your turn: Block 0, 1 HP lost.
- Turn 5, intent `debuff`, Constrict 6: you still take 6 at the end of your turn unless you have 6 Block.

Status cards: none from the Strangler. Slime companions add Slimed (cost 1, Exhaust, draws 1 card when played, harmless in hand).

## How to fight it
Derived from the facts above, not from play.
- Turn 1: nothing from it. No Constrict yet and no attack. Block only for the companions.
- From turn 2, Block needed each turn = Constrict on you + the attacks shown. That holds on its `debuff` turns too.
- The cost rises by 3 per turn every 2 turns and only its death ends it. A long fight is paid in HP or Block: 18 by turn 5, 36 by turn 7.
- It has 0 Block on your even turns and turn 1. After a Thwack the first 5 damage of your next turn goes into Block.

## Lessons
(none yet)
