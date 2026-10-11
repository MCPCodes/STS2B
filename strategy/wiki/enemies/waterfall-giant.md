# Waterfall Giant
Underdocks boss, 240 HP: at 0 HP it does not die; one enemy turn later it explodes for its Steam Eruption (15, +3 a turn), then dies.

## Facts (game files, v0.107.1)

Numbers are for ascension 0. "(asc: n)" is the higher-ascension value: HP and Siphon's heal from ascension 8, damage and the first Steam Eruption from ascension 9. The +3 Steam Eruption per move, Pressure Gun's +5 and the 1 Weak are the same at every ascension.

This boss has not been met in a recorded fight. Everything here is read from the code, by two readers.

Where: boss fight Waterfall Giant [WaterfallGiantBoss], act 1 Underdocks, one of its 3 bosses. Alone, enemy e0. Nothing joins. No randomness at all.

HP: 240 (asc: 250).

Co-op: HP x players x 1.1 (2 players: 528). Siphon heals 10 per player. Steam Eruption is not scaled. Every attack, the Weak and the explosion hit every player.

Starts with: no powers.

Steam Eruption, a buff it puts on itself with Pressurize on enemy turn 1. It is not there on your turn 1:
- The amount is the damage of the explosion to come: 15 (asc: 20) from Pressurize, then 3 more at the end of every later move.
- The amount you see on your turn is the size of the explosion if you bring it to 0 HP this turn.
- While it has Steam Eruption, bringing it to 0 HP does not kill it and does not end the fight. See "The ending" below.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Pressurize | `buff` | Puts 15 Steam Eruption on itself (asc: 20). No damage. Once, on enemy turn 1. |
| Stomp | `attack 15`, `debuff`, `buff` | 1 hit: 15 base (asc: 16). Then 1 Weak on you. Then +3 Steam Eruption. |
| Ram | `attack 10`, `buff` | 1 hit: 10 base (asc: 11). Then +3 Steam Eruption. |
| Siphon | `heal`, `buff` | Heals itself 10 HP (asc: 15), never above its max HP. No damage. Then +3 Steam Eruption. |
| Pressure Gun | `attack 20`, `buff` | 1 hit: 20 the first time (asc: 23), 5 more on each later use. Then +3 Steam Eruption. |
| Pressure Up | `attack 13`, `buff` | 1 hit: 13 base (asc: 14). Then +3 Steam Eruption. |
| About to Blow | `stun` | Only after it reached 0 HP. Stores the Steam Eruption amount as the explosion damage and removes the power. No damage. |
| Explode | `attack N` | 1 hit of the stored damage N. Then it dies for good and the fight is won. |

Pattern: fixed, no randomness. Enemy turn 1 Pressurize, once. Then a 5-move cycle: Stomp, Ram, Siphon, Pressure Gun, Pressure Up, repeat.
- Next move: after Stomp (`attack` + `debuff` + `buff`) comes Ram, after Ram comes `heal`, after `heal` comes Pressure Gun, after Pressure Gun comes Pressure Up, after Pressure Up comes Stomp.
- It never gains Strength or Block.

Damage by enemy turn, while it is not brought to 0 HP:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Move | Pressurize | Stomp | Ram | Siphon | Pressure Gun | Pressure Up | Stomp | Ram | Siphon | Pressure Gun | Pressure Up | Stomp |
| Damage | 0 | 15 | 10 | 0 | 20 | 13 | 15 | 10 | 0 | 25 | 13 | 15 |
| Running total | 0 | 15 | 25 | 25 | 45 | 58 | 73 | 83 | 83 | 108 | 121 | 136 |
| Steam Eruption after | 15 | 18 | 21 | 24 | 27 | 30 | 33 | 36 | 39 | 42 | 45 | 48 |
| Also | | Weak 1 | | heals 10 | | | Weak 1 | | heals 10 | | | Weak 1 |

- Each 5-move cycle deals 5 more than the last: 58, 63, 68. Pressure Gun is 20 on enemy turn 5, 25 on turn 10, 30 on turn 15.
- Weak 1 from Stomp lasts exactly your next turn: your turns 3, 8, 13.
- Siphon heals 10 on enemy turns 4, 9, 14. It is its only heal.

The ending, when it reaches 0 HP on your turn T (T is 2 or more):
- It comes back at once. Its `hp` and `max_hp` read 999999999 (the game shows an infinity sign).
- All its other powers are removed: Vulnerable, Weak, Poison, lowered Strength. Steam Eruption stays.
- The attack it was showing is cancelled. Its intent becomes `stun`.
- Enemy turn T: About to Blow. No damage.
- Your turn T+1: it shows `attack N`. It can still be hit, but damage does nothing useful.
- Enemy turn T+1: the explosion, 1 hit of N. Then it dies and the fight is won. If the explosion kills you, the fight is lost.
- N = 9 + 3 x T (asc: 14 + 3 x T). It is the Steam Eruption amount you saw on turn T.

| It reaches 0 HP on your turn | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Explosion N | 15 | 18 | 21 | 24 | 27 | 30 | 33 | 36 | 39 |

- The explosion is a normal attack. Your Block absorbs it. Vulnerable on you raises it x1.5. Weak on the Giant cuts it x0.75. Your own Weak does not change it.
- Your Block from turn T is wiped at the start of your turn T+1. Only Block made on turn T+1, or Block that is kept (Barricade), meets the explosion.
- Weak already on it when it reaches 0 HP is removed with its other powers. Weak put on it after that counts only if it is still there on enemy turn T+1, and Weak on it loses 1 at the end of every enemy turn. So on turn T it takes 2 or more Weak, applied after the kill. On turn T+1 any Weak works.
- 0 HP on your turn 1 (240 damage, before Pressurize): it has no Steam Eruption. It dies normally and the fight is won at once.

Worked numbers:
- 0 HP on your turn 4: N = 21. With Weak on it on enemy turn 5: 21 x 0.75 = 15.75, so 15. With Vulnerable on you: 21 x 1.5 = 31.5, so 31.
- 0 HP on your turn 6: N = 27. With Weak: 27 x 0.75 = 20.25, so 20.
- Weak on it in the normal cycle, rounded down: Stomp 15 x 0.75 = 11.25, so 11. Ram 10 becomes 7. Pressure Gun 20 becomes 15. Pressure Up 13 becomes 9.

Special rules:
- Killed by Poison at the start of enemy turn T (T is 2 or more): About to Blow happens in that same enemy turn. The move it was showing is skipped, with its +3. It explodes on enemy turn T+1 for 9 + 3 x T. On enemy turn 1 it has no Steam Eruption yet and Poison kills it for good.
- Killed by reflected damage (Thorns, Flame Barrier) during its own attack on enemy turn T: the rest of that move still happens, its +3 Steam Eruption and Stomp's Weak included. Your next turn shows `stun`. About to Blow is enemy turn T+1 and the explosion is enemy turn T+2, for 12 + 3 x T.
- A Fatal effect (Feed) counts the hit that brings it to 0 HP as a kill.
- No summons.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- The fight ends with an explosion you must survive. Before the hit that brings it to 0 HP, read the Steam Eruption amount: that is N. Your HP plus the Block you can make next turn must beat it.
- After it reaches 0 HP, stop attacking. Enemy turn T deals 0, so play no Block for the rest of turn T. On turn T+1 play only Block, and Weak on it if you have it.
- Every extra turn adds 3 to the explosion and one more attack. Enemy turns 1, 4, 9 and 14 deal 0: play no Block on those turns.
- Weak meant for the explosion goes on it on turn T+1, or 2 or more stacks after the kill on turn T. Weak applied before the kill is removed.
- It has 240 HP plus up to 10 for each Siphon it gets to use. Your turns 3, 8 and 13 are Weak turns: your attacks deal x0.75.

## Lessons
(none yet)
