# Shrinker Beetle
Overgrowth hallway enemy, 38-40 HP: on enemy turn 1 it cuts your attack damage by 30% until it dies, then alternates hits of 7 and 13.

## Facts (game files, v0.107.1)

Where: act 1 Overgrowth only.
- Hallway fight ShrinkerBeetleWeak (weak): alone, enemy e0. Weak fights come only as hallway fights 1-3 of the act.
- Hallway fight OvergrowthCrawlers (normal): Shrinker Beetle e0, Fuzzy Wurm Crawler e1. The beetle acts first. Normal fights come from hallway fight 4 on, each at most once per act.
- Fuzzy Wurm Crawler: 55-57 HP. Enemy turn 1 it hits 4, turn 2 it gains 7 Strength, then 11, 11, +7 Strength, 18, 18, and so on.

HP: 38-40 (asc 8+: 40-42).

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Shrinker | `debuffstrong` | Puts Shrink on you. No damage, no Block. |
| Chomp | `attack 7` | 1 hit: 7 (asc 9+: 8). |
| Stomp | `attack 13` | 1 hit: 13 (asc 9+: 14). |

Pattern: fixed, no randomness.
- Enemy turn 1: Shrinker. Used once per fight.
- Then Chomp and Stomp alternate, Chomp first: 7 on even enemy turns, 13 on odd enemy turns from 3.

Special rules:
- Shrink is a debuff on you. Every attack hit you deal is multiplied by 0.7.
- Order per hit: card damage + your Strength, then x 0.7, x 1.5 if the target is Vulnerable, x 0.75 if you are Weak, then rounded down once.
- It has no duration and never counts down. The snapshot shows it with amount -1.
- It ends the moment the beetle dies, also while the Fuzzy Wurm Crawler lives.
- It lands during enemy turn 1. Your turn 1 attacks deal full damage.
- The damage number on a card in your hand already includes Shrink. Do not reduce it again.
- Not reduced: your Block, potion damage (Fire Potion still deals 20), and damage from powers (Flame Barrier, Juggernaut, Inferno).
- Artifact on you blocks Shrink and loses 1 stack.
- It never gains Block. No summon, heal, escape, on-death effect or reaction to hits or cards. The fight ends when every enemy is dead.

Worked numbers under Shrink:
- Strike: 6 x 0.7 = 4.2, deals 4. Bash: 8 x 0.7 = 5.6, deals 5.
- Strike into a Vulnerable enemy: 6 x 0.7 x 1.5 = 6.3, deals 6. Bash into Vulnerable: 8 x 0.7 x 1.5 = 8.4, deals 8.
- Strike with 2 Strength: (6 + 2) x 0.7 = 5.6, deals 5.
- Strike while you are Weak: 6 x 0.7 x 0.75 = 3.15, deals 3.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Turn 1: it does not attack and Shrink is not on you yet. Spend all energy on attacks. Alone, Block on turn 1 is wasted.
- From turn 2 each point of its HP costs 1.43 times the card damage. Damage dealt on turn 1 is the cheapest.
- Block for 7 on even turns and 13 on odd turns from turn 3.
- Vulnerable multiplies with Shrink: after Bash a Strike deals 6, not 4.
- With the Fuzzy Wurm Crawler, kill the beetle first: it has fewer HP, and every attack on the Crawler while the beetle lives deals 30% less.

## Lessons
(none yet)
