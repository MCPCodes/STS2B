# Ceremonial Beast
Overgrowth boss, 252 HP: Plow 18 growing 2 a turn until its HP is 150 or less, then a stun and a 3-turn cycle with a 1-card turn.

## Facts (game files, v0.107.1)

Numbers are for ascension 0. "(asc: n)" is the higher-ascension value: HP from ascension 8, damage from ascension 9. From ascension 9 the Plow threshold and Crush's Strength gain are also higher: read the amount on the Plow power.

Where: boss fight Ceremonial Beast [CeremonialBeastBoss], act 1 Overgrowth, one of its 3 bosses. Alone, enemy e0. Nothing joins. No randomness at all.

HP: 252 (asc: 262).

Starts with: no powers.

Plow 150, a power (its attack move has the same name), which it puts on itself with Stamp on enemy turn 1. It is on the Beast from your turn 2 until phase 2 starts:
- The amount is an HP threshold. The check runs after every damage instance that removes HP from it and leaves it alive, from any source: cards, potions, reflected damage, HP loss.
- When its HP is then 150 or less: all its Strength is removed and set to exactly 0 (Strength loss you applied is wiped too), it is stunned, and Plow is removed. This happens once and starts phase 2.
- Vulnerable and Weak on it stay.
- It needs 102 damage (252 down to 150). Damage on your turn 1 counts, but Plow is not there yet: if its HP is already 150 or less after Stamp, the next hit that removes HP triggers it.
- It can trigger between the hits of a multi-hit attack; the remaining hits still land.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Stamp | `buff` | Puts Plow 150 on itself. No damage. |
| Plow | `attack 18`, `buff` | 1 hit: 18 base (asc: 20) + Strength. Then gains 2 Strength. |
| Stunned | `stunned` | Nothing. |
| Beast Cry | `debuff` | Applies Ringing to you. No damage. |
| Stomp | `attack 15` | 1 hit: 15 base (asc: 17) + Strength. |
| Crush | `attack 17`, `buff` | 1 hit: 17 base (asc: 19) + Strength. Then gains 3 Strength. |

Pattern: fixed.
- Phase 1: enemy turn 1 Stamp, then Plow every turn until the threshold triggers.
- Phase 2: Stunned once, then Beast Cry, Stomp, Crush, repeating for the rest of the fight. It never returns to Plow.
- Next move in phase 2: after `debuff` comes Stomp, after Stomp comes Crush, after Crush comes `debuff`.

Phase 1 damage by enemy turn:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Move | Stamp | Plow | Plow | Plow | Plow | Plow | Plow |
| Damage | 0 | 18 | 20 | 22 | 24 | 26 | 28 |
| Running total | 0 | 18 | 38 | 60 | 84 | 110 | 138 |

Phase 2, when the threshold triggers on your turn S (Strength restarts at 0):

| Enemy turn | S | S+1 | S+2 | S+3 | S+4 | S+5 | S+6 | S+7 | S+8 | S+9 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Move | Stunned | Beast Cry | Stomp | Crush | Beast Cry | Stomp | Crush | Beast Cry | Stomp | Crush |
| Damage | 0 | 0 | 15 | 17 | 0 | 18 | 20 | 0 | 21 | 23 |

- The Plow it was showing on turn S is cancelled.
- Each 3-turn cycle deals 6 more than the last: 32, 38, 44, 50.
- Your 1-card turns are S+2, S+5, S+8: the turns on which it shows Stomp.

Ringing (debuff on you, from Beast Cry):
- It lasts exactly your next turn and is removed when that turn ends.
- On that turn you can play 1 card. Once one card play has started, no other card can be played. Cards an effect plays for you follow the same rule.
- Potions are not limited. Energy and card draw are unchanged.

Special rules:
- Triggered during its own turn (reflected damage while it performs Plow, for one): it still gains the 2 Strength from that Plow and starts phase 2 with 2 Strength. It shows `stunned` on your next turn and does nothing on the enemy turn after it. (Read from the code path, not seen in a fight.)
- The killing blow triggers nothing.
- It never gains Block. No summons.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Phase 1 costs 2 more every turn. Reaching 150 HP cancels the Plow it shows and gives 2 enemy turns with no damage (S and S+1). Get there early.
- After the trigger, play no Block for the rest of turn S and all of turn S+1. Use them for Powers and damage.
- Every 1-card turn faces Stomp: 15, then 18, then 21. Energy is full, so play your strongest single card, Block or attack. Potions still work.
- Strength loss put on it before the trigger only helps until the trigger, which wipes it. Save lasting Strength loss for phase 2.
- Phase 2 starts with at most 150 HP left and deals 32, 38, 44 per 3 turns. Count the cycles you need.

## Lessons
(none yet)
