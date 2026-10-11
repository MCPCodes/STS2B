# Fat Gremlin
13-17 HP, appears when the Gremlin Merc dies: never attacks, escapes on its second enemy turn; killing it returns the stolen gold.

## Facts (game files, v0.107.1)

Where: only in normal fight Two Gremlins in a Trenchcoat [GremlinMercNormal], act 1 Underdocks. It is not there at the start: it is added when the Gremlin Merc (e0, see gremlin-merc.md) dies, together with the Sneaky Gremlin (see sneaky-gremlin.md). The Sneaky Gremlin is e1 and acts first; this one is e2.

HP: 13-17 (asc 8+: 14-18). Rolled when it appears. Never the same number as the Sneaky Gremlin: if that one has 13 or 14, this one has another value.

Co-op: HP x players x 1.1, rounded down (2 players: 28, 30, 33, 35 or 37). It holds 1 Heist per robbed player, and each player gets their own gold back. (2 players seen in a fight: 30 HP, Heist 80, 80 gold back to each player.)

Starts with Heist, if the Merc took any gold:
- The amount is all the gold the Merc took from you: 20 per Merc move made while you had gold, less when you had under 20.
- When the Fat Gremlin dies, a gold reward of exactly that amount is added to the fight's rewards.
- If it escapes, Heist goes with it and the gold is lost.
- No gold taken: no Heist. Either way it is not a Minion.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Wake Up | `stun` | Nothing. Its first move only. |
| Flee | `escape` | It leaves the fight without dying. No damage. |

Pattern: fixed, no randomness. Wake Up, then Flee.
- Merc killed on your turn T: it shows `stun` for the rest of turn T and does nothing on enemy turn T. On your turn T+1 it shows `escape`. It is gone in enemy turn T+1. You have the rest of turn T and all of turn T+1 to kill it.
- Merc killed during enemy turn T (damage dealt back inside the Merc's own move): it shows `stun` for your whole turn T+1 and `escape` on your turn T+2. That gives 2 full turns. (Read from the code, not seen in a fight.)

Gold, by how the fight ends:

| What happens | Gold |
| --- | --- |
| Fat Gremlin killed | The normal reward (10-20), then a second reward equal to the Heist amount. |
| It escapes with Heist | The gold taken is lost. No gold reward at all. |
| It escapes without Heist | 5-10. |

- The potion roll and the card reward are the same in every case.
- The returned gold is its own line on the reward screen, after the normal gold. It has to be taken like any reward.
- Seen: 1 theft, Fat Gremlin killed: rewards 16 then 20. Co-op, 4 thefts (Heist 80), killed: 18 then 80 for each player. Both escape rows are read from the code, not seen.

Special rules:
- It never attacks, never gains Block and applies no debuffs.
- It does not have to die. Once it has escaped, the fight ends when the Sneaky Gremlin is dead. If the Sneaky Gremlin is already dead, the fight is won the moment this one escapes.
- While it is still in the fight, the fight does not end, even with the Sneaky Gremlin dead.
- `stun` is its own do-nothing move, not the `stunned` state.
- A multi-hit attack on all enemies that kills the Merc lands its later hits on this gremlin too.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- The Heist number on it is the gold its death pays back, on top of the normal 10-20. If it escapes with Heist you get neither.
- Time limit when the Merc died on your turn: the rest of that turn plus 1 full turn. 13-17 damage in that window.
- It deals no damage. With Heist on it, kill it before the Sneaky Gremlin; the Sneaky one costs 9 per extra turn and cannot leave.
- No Heist on it: killing it is worth 10-20 gold instead of 5-10. Let it go if the damage is needed on the Sneaky Gremlin.
- Kill the Merc with energy to spare when you can: damage put into this gremlin on the same turn costs no extra enemy turn.

## Lessons
(none yet)
