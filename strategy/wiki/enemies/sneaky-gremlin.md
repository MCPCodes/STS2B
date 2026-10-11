# Sneaky Gremlin
10-14 HP, appears when the Gremlin Merc dies: loses its first enemy turn, then hits 9 every turn; it must be killed.

## Facts (game files, v0.107.1)

Where: only in normal fight Two Gremlins in a Trenchcoat [GremlinMercNormal], act 1 Underdocks. It is not there at the start: it is added when the Gremlin Merc (e0, see gremlin-merc.md) dies, together with the Fat Gremlin (see fat-gremlin.md). It is e1, the Fat Gremlin e2. It acts before the Fat Gremlin.

HP: 10-14 (asc 8+: 11-15). Rolled when it appears.

Co-op: HP x players x 1.1, rounded down (2 players: 22, 24, 26, 28 or 30). Damage is not scaled; its attack hits every player. (2 players seen in a fight: 28 HP.)

Starts with: no powers. It is not a Minion.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Wake Up | `stun` | Nothing. Its first move only. |
| Tackle | `attack 9` | 1 hit: 9 base (asc 9+: 10) + Strength. |

Pattern: fixed, no randomness. Wake Up once, then Tackle every turn.
- Merc killed on your turn T: it appears at once showing `stun`. Enemy turn T: nothing. Your turn T+1: it shows `attack 9`. It hits on enemy turn T+1 and on every turn after.
- Merc killed during enemy turn T (damage dealt back inside the Merc's own move): it does not act that turn. It shows `stun` for your whole turn T+1 and does nothing on enemy turn T+1. It hits from enemy turn T+2. (Read from the code, not seen in a fight.)

Special rules:
- It must be killed to win, whatever the Fat Gremlin does. The fight ends when it is dead and the Fat Gremlin is dead or gone.
- `stun` is its own do-nothing move. It is not the `stunned` state other enemies show after being stunned.
- It never gains Strength or Block. It applies no debuffs and never escapes.
- A multi-hit attack on all enemies that kills the Merc lands its later hits on this gremlin too.

Worked numbers:
- Weak on it rounds down: 9 x 0.75 = 6.75, so 6.
- Vulnerable on you: 9 x 1.5 = 13.5, so 13.
- If you are still Weak from the Merc's Double Smash, a Strike deals 4: 10 HP then takes 3 Strikes, not 2.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- The enemy turn it appears in is free when the Merc died on your turn: it does nothing. Play no Block against it then.
- From the next enemy turn it deals 9 every turn and never grows. Each turn it lives costs 9.
- Fat Gremlin with Heist on it: kill the Fat Gremlin first. It escapes in the enemy turn after it shows `escape`; this gremlin stays and can die later.
- Fat Gremlin with no Heist: kill this gremlin first. The Fat one deals no damage and leaves by itself.
- An attack on all enemies hits both gremlins.

## Lessons
(none yet)
