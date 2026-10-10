# Vine Shambler
Overgrowth hallway enemy, 61 HP, alone: fixed loop of 6x2, then 8 and your Attacks cost 1 more next turn, then 16.

## Facts (game files, v0.107.1)

Where: act 1 Overgrowth only. Hallway fight VineShamblerNormal (normal): alone, enemy e0. Normal fights come from hallway fight 4 on, each at most once per act.

HP: 61, always (asc 8+: 64).

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Swipe | `attack 6x2` | 2 hits of 6 (asc 9+: 7). 12 in total. |
| Grasping Vines | `attack 8`, `carddebuff` | 1 hit: 8 (asc 9+: 9). Then 1 Tangled on you. |
| Chomp | `attack 16` | 1 hit: 16 (asc 9+: 18). |

Pattern: fixed loop of 3, no randomness: Swipe, Grasping Vines, Chomp.
- Enemy turns 1, 4, 7: Swipe, 12.
- Enemy turns 2, 5, 8: Grasping Vines, 8 and Tangled.
- Enemy turns 3, 6, 9: Chomp, 16.
- 36 damage per loop.

Special rules:
- Tangled is a debuff on you. During your next turn every Attack card costs 1 more energy. It is removed at the end of that turn.
- Your Tangled turns are 3, 6, 9, ...: always the turns its intent is Chomp.
- It marks your Attack cards in every pile, and Attacks created during that turn. Skills and Powers are not affected.
- X-cost Attacks are not affected. A 0-cost Attack costs 1.
- The `cost` of a card in the snapshot already includes the +1. Do not add it again.
- Worked, 3 energy on a Tangled turn: Strike costs 2, Bash costs 3. Playable: Bash alone, or 1 Strike + 1 Defend, or 3 Defends. 2 Strikes need 4 energy.
- Swipe: each hit is worked out, rounded down and blocked on its own. Into 8 Block: hit 1 takes Block to 2, hit 2 loses 2 Block and 4 HP.
- Swipe with Weak on it: 6 x 0.75 = 4.5, so 4 per hit, 8 and not 9.
- It never gains Block or Strength. Its damage never grows.
- No summon, heal, escape, on-death effect or reaction to hits or cards.

Status cards: none. Tangled marks cards; it adds none.

## How to fight it
Derived from the facts above, not from play.
- Turns 3, 6, 9: Attacks cost 1 more and 16 is coming. Use those turns for Block and Powers: 3 Defends give 15.
- Do the damage on turns 1, 2, 4, 5, 7, 8, when Attacks cost their normal price.
- Vulnerable from Bash covers the turn it is played and the next one. Played on turn 1 or 4 it covers 2 normal-price turns.
- It has no scaling: 12, 8, 16 forever. Each loop costs up to 36 HP or Block; the only clock is your HP.

## Lessons
(none yet)
