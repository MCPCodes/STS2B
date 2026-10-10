# Crossbow Raider
Overgrowth hallway raider, 18-21 HP: gains 3 Block on odd enemy turns and hits 14 on even ones.

## Facts (game files, v0.107.1)

Where: act 1 Overgrowth only. Hallway fight RubyRaidersNormal (normal): 3 raiders of 3 different types, drawn from 5, in random order. Each type is in 3 of 5 such fights. Normal fights come from hallway fight 4 on, each at most once per act.

The other 4 types:
- Axe Raider: 20-22 HP. Loop of 5 damage + 5 Block, the same again, then 12 damage.
- Assassin Raider: 18-23 HP. Hits 10 every turn.
- Brute Raider: 30-33 HP. Hits on odd enemy turns (7, 10, 13, ...), gains 3 Strength on even ones.
- Tracker Raider: 21-25 HP. 2 Frail on you on enemy turn 1, then 1x8 every turn.

HP: 18-21 (asc 8+: 19-22).

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Reload | `defend` | Gains 3 Block. No damage. |
| Fire | `attack 14` | 1 hit: 14 (asc 9+: 16). |

Pattern: fixed, no randomness. Reload, Fire, Reload, Fire, starting with Reload.
- Odd enemy turns: Reload, no attack.
- Even enemy turns: Fire, 14.

Special rules:
- The `defend` intent shows no number. The Block is 3.
- Block timing: Block gained on its turn stays through your next turn and is removed when the next enemy turn starts.
- So it has 3 Block during your even turns, the turns its intent is Fire, and 0 Block during your odd turns.
- Worked, killing it before the first shot: Bash + Strike on turn 1 = 8 + 9 = 17, leaving 1-4 HP. On turn 2 it has 3 Block; 1 Strike into Vulnerable (9) removes the Block and deals 6. It dies before enemy turn 2.
- No summon, heal, escape, on-death effect or reaction to hits or cards. The fight ends when all 3 raiders are dead.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- It deals nothing before enemy turn 2. Dead by the end of your turn 2, it never fires.
- Attack it on your odd turns (0 Block). On even turns the first 3 damage goes into Block.
- On even turns count 14 from it unless it dies that turn. A kill on an even turn takes its HP + 3.
- Damage over enemy turns 1-6 if left alive: Assassin 60, Axe 44, Crossbow 42, Tracker 40, Brute 30. Middle priority; a kill by turn 2 also denies the first 14.

## Lessons
(none yet)
