# Sludge Spinner
37-39 HP: opens with attack 8 + 1 Weak, then each turn 1 of 3 attacks, never the same twice: 8 + Weak, 11, or 6 with +3 Strength.

## Facts (game files, v0.107.1)

Where: weak fight Sludge Spinner [SludgeSpinnerWeak], act 1 Underdocks. 1 Sludge Spinner, alone, enemy e0. Weak fights are hallway fights 1-3.

HP: 37-39 (asc 8+: 41-42).

Co-op: HP x players x 1.1, rounded down (2 players: 81, 83 or 85). Damage, Weak and Strength are not scaled; every attack hits every player and Weak goes to every player. (Read from the code, not seen in a fight.)

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Oil Spray | `attack 8`, `debuff` | 1 hit: 8 base (asc 9+: 9) + Strength. Then 1 Weak on you, blocked or not. |
| Slam | `attack 11` | 1 hit: 11 base (asc 9+: 12) + Strength. |
| Rage | `attack 6`, `buff` | 1 hit: 6 base (asc 9+: 7) + Strength. Then gains 3 Strength (same at every ascension). The hit comes first. |

Pattern:
- Enemy turn 1: Oil Spray, always.
- Every later turn: 50/50 between the 2 moves it did not use last turn. The same move never comes twice in a row.
- After Oil Spray: Slam or Rage. After Slam: Oil Spray or Rage. After Rage: Oil Spray or Slam.
- It attacks every turn. Rage comes at most every second turn.
- Its Strength is 3 per Rage done, when nothing else changes it.

Damage by Rages done so far:

| Rages done | 0 | 1 | 2 | 3 |
| --- | --- | --- | --- | --- |
| Oil Spray | 8 | 11 | 14 | 17 |
| Slam | 11 | 14 | 17 | 20 |
| Rage | 6 | 9 | 12 | 15 |

First turns:

| Enemy turn | 1 | 2 | 3 |
| --- | --- | --- | --- |
| 50% of fights | Oil Spray 8 | Slam 11 | Oil Spray 8 or Rage 6 |
| 50% of fights | Oil Spray 8 | Rage 6 | Oil Spray 11 or Slam 14 |

Weak it puts on you:
- Weak 1 lands after Oil Spray's hit and covers exactly your next turn. Since turn 1 is always Oil Spray, you are Weak on your turn 2.
- Your attack damage is x0.75, rounded down per hit: Strike 6 deals 4, Bash 8 deals 6.
- Oil Spray never comes twice in a row, so you are never Weak 2 turns running from it.

Worked numbers:
- Seen in a fight: enemy turn 2 Rage for 6, then Strength 3 and `attack 14` (Slam 11 + 3) on turn 3.
- Your turn 2, Weak: 3 Strikes deal 12, not 18.
- Weak on it rounds down: Slam 11 x 0.75 = 8.25, so 8. Oil Spray 6. Rage 4. Slam after 1 Rage: 14 x 0.75 = 10.5, so 10.
- 1 point of Strength taken from it removes 1 from every later hit.

Special rules: its Strength stays for the fight. It never gains Block and summons nothing. No heal, escape or on-death effect.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Turn 1 attacks are full, turn 2 attacks deal 75%. Lean turn 1 toward damage and turn 2 toward Block.
- Intent `attack` + `debuff` means Weak on your next turn: spend attacks this turn. It is never Oil Spray next turn.
- It attacks every turn. The smallest hit is Rage (6 + Strength), and each Rage adds 3 to every later hit.
- 37-39 HP is 7 Strikes. Every Rage it gets in makes the rest of the fight cost 3 more a turn: do not stall.
- After `attack` + `buff` the next move is Oil Spray or Slam, both 3 higher than before.

## Lessons
(none yet)
