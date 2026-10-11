# Gremlin Merc
47-49 HP: loops 7x2, 6x2 with 2 Weak, 8 with +2 Strength, and takes 20 gold per move; its death adds a Sneaky and a Fat Gremlin.

## Facts (game files, v0.107.1)

Where: act 1 Underdocks only. Normal fight Two Gremlins in a Trenchcoat [GremlinMercNormal]. It starts alone as enemy e0. When it dies, a Sneaky Gremlin (e1) and a Fat Gremlin (e2) join (see sneaky-gremlin.md, fat-gremlin.md). Normal fights come from hallway fight 4 on.

HP: 47-49 (asc 8+: 51-53). The Sneaky Gremlin adds 10-14, the Fat Gremlin 13-17.

Co-op: HP x players x 1.1, rounded down (2 players: 103, 105 or 107). Damage and Thievery are not scaled; every attack hits every player. Each player loses their own gold, up to 20 per Merc move, and gets their own gold back. (2 players seen in a fight: 103 HP, 80 gold back to each player.)

Starts with Surprise (shown with amount 1) and Thievery 20:
- Surprise: when the Merc dies, a Sneaky Gremlin and a Fat Gremlin are added and the fight goes on.
- Thievery: each Merc move takes gold from you right after its attack: 20, or all you have if that is less. Blocked or not. At 0 gold nothing is taken.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Gimme | `attack 7x2` | 2 hits: 7 base each (asc 8+: 8) + Strength on every hit. Then takes up to 20 gold. |
| Double Smash | `attack 6x2`, `debuff` | 2 hits: 6 base each (asc 8+: 7) + Strength on every hit. Then takes up to 20 gold. Then 2 Weak on you. |
| Hehe | `attack 8`, `buff` | 1 hit: 8 base (asc 8+: 9) + Strength. Then takes up to 20 gold. Then gains 2 Strength. |

Its damage goes up at ascension 8, not 9 as for most monsters.

Pattern: fixed loop of 3, no randomness: Gimme, Double Smash, Hehe. It starts with Gimme.
- After `attack` alone comes `attack` + `debuff`. After that comes `attack` + `buff`. After that comes `attack` alone.
- Each Hehe adds 2 to every later hit: 4 per Gimme and Double Smash, 2 per Hehe.

By enemy turn, when nothing else changes its Strength:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Move | Gimme | Double Smash | Hehe | Gimme | Double Smash | Hehe | Gimme | Double Smash | Hehe |
| Damage | 7x2 = 14 | 6x2 = 12 | 8 | 9x2 = 18 | 8x2 = 16 | 10 | 11x2 = 22 | 10x2 = 20 | 12 |
| Running total | 14 | 26 | 34 | 52 | 68 | 78 | 100 | 120 | 132 |
| Gold taken so far, while you have it | 20 | 40 | 60 | 80 | 100 | 120 | 140 | 160 | 180 |

Loop totals: 34, 44, 54.

Gold:
- Taken gold leaves your gold at once.
- When the Merc dies the Fat Gremlin gets Heist with the total taken. Kill the Fat Gremlin and that full amount comes back as a second gold reward, listed after the normal one (10-20). Take it on the reward screen like any reward.
- If the Fat Gremlin escapes after gold was taken: the gold is lost and the fight gives no gold reward at all.
- If it escapes and nothing was taken: the gold reward is 5-10. That needs the Merc dead before its first move, or 0 gold on every Merc move.
- The potion roll and the card reward are the same in every case.
- Worked, 50 gold: the Merc takes 20, 20, 10, then nothing. Heist reads 50.
- Seen: 1 Merc move, Fat Gremlin killed, rewards 16 gold then 20 gold. 4 Merc moves in co-op: 18 then 80. Both escape cases are read from the code, not seen.

When it dies:
- On your turn T: both gremlins appear at once showing `stun`. Enemy turn T: nothing happens. The attack the Merc was showing is lost, and so is its theft. Enemy turn T+1: the Sneaky Gremlin hits 9 and the Fat Gremlin escapes.
- During the enemy turn (inside its own move, to damage dealt back such as Flame Barrier): its remaining hits and that move's theft do not happen. The 2 Weak of a Double Smash cut short this way still lands. Both gremlins show `stun` for your whole next turn and skip the enemy turn after it. (Read from the code, not seen in a fight.)
- A multi-hit attack on all enemies that kills the Merc keeps going: its later hits land on both gremlins. A single-target multi-hit stops. (Read from the code, not seen in a fight.)

Worked numbers:
- Weak on it rounds down per hit. Gimme: 7 x 0.75 = 5.25, so 5x2 = 10. Double Smash: 6 x 0.75 = 4.5, so 4x2 = 8, not 9. Hehe: 6.
- Vulnerable on you: Gimme 7 x 1.5 = 10.5, so 10x2 = 20, not 21. Double Smash 9x2 = 18. Hehe 12.
- 2 Weak from Double Smash covers your next 2 turns: your turns 3 and 4, then 6 and 7. Your attacks deal x0.75, rounded down per hit: Strike 6 deals 4.
- The Weak lands after the hits and the theft. It does not change that turn's damage.

Special rules:
- It never gains Block.
- Killing it never ends the fight. The fight ends when the Sneaky Gremlin is dead and the Fat Gremlin is dead or gone.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- All the gold comes back if the Fat Gremlin dies and none if it escapes. A fast Merc kill does not save gold; a dead Fat Gremlin does.
- The enemy turn after the Merc's death deals 0. On the turn you kill it, play no Block; energy left over goes into the Fat Gremlin.
- Its hits climb 10 per loop (34, 44, 54). The first 4 enemy turns deal 14, 12, 8, 18.
- Your turns 3, 4, 6 and 7 are Weak: attacks deal 75%, Block is full. Turns 1, 2 and 5 are the full-damage turns.
- The whole fight is 70-80 HP of enemies. Plan the Merc's death for a turn that leaves damage for the Fat Gremlin (13-17 HP) by the end of the next turn.

## Lessons
(none yet)
