# Terror Eel
Underdocks elite, 140 HP: alternates Crash 16 and Thrash 3x3; at 70 HP it is stunned once, then puts 99 Vulnerable on you.

## Facts (game files, v0.107.1)

Numbers are for ascension 0. "(asc: n)" is the higher-ascension value: HP and the Shriek threshold from ascension 8, damage from ascension 9. Vigor 6 and Vulnerable 99 are the same at every ascension.

Where: elite fight Terror Eel [TerrorEelElite], act 1 Underdocks. Alone, enemy e0. Nothing joins. No randomness at all.

HP: 140 (asc: 150).

Co-op: HP x players x 1.1 (2 players: 308). Shriek is scaled the same way (2 players: 154). Damage, Vigor and the 99 Vulnerable are not scaled; attacks and Terrorize hit every player. (Read from the code, not seen in a fight.)

Starts with Shriek 70 (asc: 75). It is listed as a debuff on the Eel:
- The amount is an HP threshold. The check runs after every damage instance that removes HP from it and leaves it alive, from any source: cards, potions, Poison, reflected damage.
- When its HP is then 70 or less: it is stunned, the move after the stun is set to Terrorize, and Shriek is removed. This happens once per fight.
- It needs 70 damage (140 down to 70).
- The stun cancels the attack it was showing. The intent changes to `stunned` at once, in the middle of your turn.
- It can trigger between the hits of a multi-hit attack; the remaining hits still land.
- The killing blow triggers nothing.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Crash | `attack 16` | 1 hit: 16 base (asc: 18). With Vigor 6 on the Eel it is 22, and the Vigor is used up. |
| Thrash | `attack 3x3`, `buff` | 3 hits: 3 base each (asc: 4). Then gains 6 Vigor. |
| Stunned | `stunned` | Nothing. Once, when Shriek triggers. |
| Terrorize | `debuff` | Applies 99 Vulnerable to you. No damage. Once, on the enemy turn after the stun. |

Vigor 6 (a buff on the Eel, from Thrash). Read from the code, not seen in a fight:
- It adds its amount to every hit of the Eel's next attack and is then used up.
- Thrash gains it after its own hits, so Thrash never has it. The next Crash does: 16 + 6 = 22. The intent already shows 22.
- It is added before Weak and Vulnerable multiply.
- The stun does not remove it. It waits for the Eel's next attack.

Pattern: fixed, no randomness.
- Before the stun: Crash, Thrash, Crash, Thrash, starting with Crash. The next move is the one it did not just use.
- Stun on your turn S: enemy turn S it does nothing. Your turn S+1 it shows `debuff`. Enemy turn S+1 is Terrorize. From enemy turn S+2 it is Crash, Thrash, Crash again, starting with Crash.
- Terrorize happens once. It never gains Strength.

Damage by enemy turn with no stun:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 |
| --- | --- | --- | --- | --- | --- | --- |
| Move | Crash | Thrash | Crash | Thrash | Crash | Thrash |
| Damage | 16 | 3x3 = 9 | 22 | 3x3 = 9 | 22 | 3x3 = 9 |
| Running total | 16 | 25 | 47 | 56 | 78 | 87 |

After a stun on your turn S. You have 99 Vulnerable from enemy turn S+1 on:

| Enemy turn | S | S+1 | S+2 | S+3 | S+4 | S+5 | S+6 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Move | Stunned | Terrorize | Crash | Thrash | Crash | Thrash | Crash |
| Damage | 0 | 0 | 24 or 33 | 4x3 = 12 | 33 | 4x3 = 12 | 33 |

- The first Crash after Terrorize is 24 when the Eel holds no Vigor and 33 when it does.
- It holds no Vigor when the stun cancelled a Thrash (S even), or when S is 1.
- It holds Vigor when the stun cancelled a Crash on turn 3 or later (S odd, 3 or more): the 6 from the Thrash before is still there.

99 Vulnerable on you:
- Every attack on you deals x1.5. Crash 16 x 1.5 = 24. Crash with Vigor (16 + 6) x 1.5 = 33. Thrash 3 x 1.5 = 4.5, so 4x3 = 12.
- It loses 1 per enemy turn. 99 outlasts the fight.
- It is one application: 1 Artifact on you stops all 99.

Worked numbers:
- Eel at 75 HP, Twin Strike 5x2: hit 1 brings it to 70 and triggers the stun. Hit 2 still lands.
- Weak on the Eel before Terrorize, rounded down per hit. Crash 16 x 0.75 = 12. Crash with Vigor 22 x 0.75 = 16.5, so 16. Thrash 3 x 0.75 = 2.25, so 2x3 = 6.
- Weak on the Eel after Terrorize (x 0.75 x 1.5). Crash 16 becomes 18. Crash with Vigor 22 becomes 24.75, so 24. Thrash 3 becomes 3.375, so 3x3 = 9.

Special rules:
- Threshold crossed by Poison at the start of enemy turn T: the stun replaces its move before it acts. It is stunned for that same enemy turn, and your turn T+1 already shows `debuff`. (Read from the code path, not seen in a fight.)
- Threshold crossed during its own attack (Thorns, Flame Barrier): the move it is performing finishes; the remaining Thrash hits land and it still gains Vigor. Your next turn shows `stunned` and the stunned turn is its next enemy turn. (Read from the code path, not seen in a fight.)
- Already stunned by something else this turn (the event card Whistle) when its HP crosses 70: Shriek's stun is dropped, Shriek is still removed, and Terrorize never happens. If Whistle's own hit is the one that crosses 70, the normal sequence runs. (Read from the code only.)
- It never gains Block. No summons.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- The trigger is 70 damage, not a turn count. The stun cancels the attack it shows, so cross 70 before you spend energy on Block that turn.
- Enemy turns S and S+1 deal 0. After the stun, play no Block for the rest of turn S and all of turn S+1.
- From enemy turn S+2 every hit is x1.5: 24 or 33, then 12, then 33. It has at most 70 HP when the stun lands. Kill it by the end of your turn S+2 and none of them lands.
- Crossing 70 on a turn it shows Crash cancels 16 or 22. On a Thrash turn it cancels only 9.
- Before the stun, Block for the number shown: 16, 9, 22, 9. After Terrorize, Weak on it cuts Crash from 33 to 24 and from 24 to 18.

## Lessons
(none yet)
