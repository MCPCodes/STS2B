# Gas Bomb
7 HP Minion added by Living Fog on even enemy turns: on the next enemy turn it hits for 8 and dies; killing it first cancels the hit.

## Facts (game files, v0.107.1)

Where: only in normal fight Living Fog [LivingFogNormal], act 1 Underdocks. It is not there at the start: the Living Fog (e0, see living-fog.md) adds 1 with each Bloat, on enemy turns 2, 4, 6, ... The first bomb is e1, the next e2, and so on. A dead bomb's id is not used again.

HP: 7, always (asc 8+: 8).

Co-op: HP x players x 1.1, rounded down (2 players: 15). Damage is not scaled; its blast hits every player. (Read from the code, not seen in a fight.)

Starts with Minion (shown with amount 1):
- A Minion does not have to die. When the Living Fog dies, every living bomb dies with it and the fight is won.
- Killing a Minion does not count as a kill for Fatal effects such as Feed.
- Killing a bomb never ends the fight.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Explode | `attack 8` | 1 hit: 8 base (asc 9+: 9) + Strength. Then it dies, blocked or not. |

Pattern: 1 move, fixed.
- Added during enemy turn T: it does not act that turn.
- Your turn T+1: it shows `attack 8`.
- Enemy turn T+1: it acts before the Living Fog, hits 8 and dies.
- So each bomb gives you exactly 1 turn to kill it.

Special rules:
- Normally 1 bomb is alive at a time: each one blows up the turn after it arrives.
- Its death, by you or by its own blast, triggers nothing else.
- Its intent reads as a plain `attack 8`. The intent does not say that it dies.
- It never gains Block or Strength.
- This enemy has not been seen in play. Its timing and its `attack 8` are read from the code.

Worked numbers:
- 7 HP: a Strike (6) leaves it at 1 HP and it still hits for 8. Bash (8) kills it. A Strike with 1 Strength kills it.
- Weak on the bomb: 8 x 0.75 = 6. Vulnerable on you: 12.
- On its blast turn the Fog also hits 8: 16 in all with the bomb alive, 8 with it dead.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- 7 damage on your turn cancels 8 damage. Under Smoggy you get 1 Skill a turn, so this is often cheaper than Block.
- Check the kill: 6 damage is not enough. Use a hit of 7 or more, or 2 hits.
- An attack on all enemies hits the bomb and the Fog together.
- Do not use Feed on it, or damage beyond the 7 it takes.
- On the turn the Living Fog can die, ignore it: it dies with the Fog.

## Lessons
(none yet)
