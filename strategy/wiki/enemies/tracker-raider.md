# Tracker Raider
Overgrowth hallway raider, 21-25 HP: puts 2 Frail on you on enemy turn 1, then hits 1x8 every turn.

## Facts (game files, v0.107.1)

Where: act 1 Overgrowth only. Hallway fight RubyRaidersNormal (normal): 3 raiders of 3 different types, drawn from 5, in random order. Each type is in 3 of 5 such fights. Normal fights come from hallway fight 4 on, each at most once per act.

The other 4 types:
- Axe Raider: 20-22 HP. Loop of 5 damage + 5 Block, the same again, then 12 damage.
- Assassin Raider: 18-23 HP. Hits 10 every turn.
- Brute Raider: 30-33 HP. Hits on odd enemy turns (7, 10, 13, ...), gains 3 Strength on even ones.
- Crossbow Raider: 18-21 HP. Gains 3 Block on odd enemy turns, hits 14 on even ones.

HP: 21-25 (asc 8+: 22-26).

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Track | `debuff` | 2 Frail on you. No damage. |
| Hounds | `attack 1x8` | 8 hits of 1 (asc 9+: 9 hits of 1). |

Pattern: fixed, no randomness. Track on enemy turn 1, once per fight. Hounds on every enemy turn from 2.

Special rules:
- Frail: Block you gain from cards is multiplied by 0.75 and rounded down per card. Defend gives 3 instead of 5. The card text shows the reduced number. Block from powers and relics is not cut.
- The 2 Frail covers your turns 2 and 3 and is gone on your turn 4.
- Hounds: each of the 8 hits is worked out, rounded down and blocked on its own. 8 damage in total; 8 Block absorbs all of it.
- Vulnerable on you does not raise Hounds: 1 x 1.5 = 1.5, so 1 per hit, still 8.
- Weak on the Tracker turns Hounds into 0: 1 x 0.75 = 0.75, so 0 per hit. The intent then reads `attack 0x8`.
- Effects that answer each hit trigger 8 times. Flame Barrier returns 4 after each hit, blocked or not: up to 32, which kills it from full HP at hit 6 or 7. The hits after its death do not happen.
- It never gains Block or Strength.
- No summon, heal, escape, on-death effect or reaction to hits or cards. The fight ends when all 3 raiders are dead.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Turn 1: no damage from it. Block you play on turn 1 is not Frail yet.
- From turn 2 count 8 from it every turn. On your turns 2 and 3 that takes 3 Defends (3 each); from turn 4, 2 Defends.
- 1 Weak on it cancels that turn's Hounds completely. Uppercut applies 1 Weak. A Weak Potion applies 3: no Hounds damage for 3 enemy turns.
- Damage over enemy turns 1-6 if left alive: Assassin 60, Axe 44, Crossbow 42, Tracker 40, Brute 30. Middle priority: after the Assassin, before the Brute.

## Lessons
(none yet)
