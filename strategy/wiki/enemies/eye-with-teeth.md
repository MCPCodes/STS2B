# Eye with Teeth
6 HP Minion summoned by Fogmog: adds 3 Dazed every enemy turn, never attacks, and is back at full HP the turn after it is killed.

## Facts (game files, v0.107.1)

Where: only in normal fight FogmogNormal, act 1 Overgrowth. It is not there at the start: Fogmog (e0, see fogmog.md) summons it on enemy turn 1. It is e1. It first acts on enemy turn 2, and from then on it acts before Fogmog.

HP: 6 at every ascension.

Starts with Illusion and Minion:
- Illusion: at 0 HP it does not leave the fight. It cannot be hit or targeted, attacks on all enemies skip it, and it takes no powers. On its next enemy turn it heals back to 6 HP instead of acting. It keeps its buffs and loses its debuffs.
- Minion: it does not have to die. When Fogmog dies it dies too and the fight is won. Killing it does not count as a kill for Fatal effects such as Feed.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Distract | `statuscard: 3` | Adds 3 Dazed to your discard pile. No damage. |
| Revive | none: at 0 HP it is not in the enemy list | Heals to 6 HP. Takes the place of Distract on the enemy turn after it was killed. |

Pattern: Distract every enemy turn, fixed.
- Killed on your turn T: on enemy turn T it revives and adds no Dazed. On your turn T+1 it is back at 6 HP and shows `statuscard: 3`.
- So each kill cancels exactly 1 Distract.

Special rules:
- While it is at 0 HP the enemy list shows Fogmog only. e1 returns on your next turn.
- A single-target multi-hit attack stops when it reaches 0 HP. The hits that are left are lost, not moved to Fogmog.
- It never attacks and never gains Block.
- Killing it never ends the fight.

Status card: Dazed. Status, Unplayable, Ethereal, cannot be upgraded.
- It cannot be played. In your hand at end of turn it is exhausted: gone for the fight.
- New copies go to the discard pile: you first draw them after the next shuffle.
- Each Dazed takes 1 of your 5 draws, once.

## How to fight it
Derived from the facts above, not from play.
- It never has to die: Fogmog's death kills it.
- 6 damage on your turn cancels its 3 Dazed for that enemy turn only. A plain Strike is exactly 6.
- Left alone it adds 3 Dazed per turn, and each one costs 1 draw later. Compare that with the 1 card and 1 energy a Strike costs.
- Do not use Feed on it, or hits beyond the 6 it takes.
- On the turn Fogmog can die, ignore it.

## Lessons
(none yet)
