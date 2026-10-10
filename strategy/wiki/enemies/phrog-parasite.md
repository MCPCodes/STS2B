# Phrog Parasite
Overgrowth elite, 61-64 HP: alternates 3 Infections and Lash 4x4; when it dies 4 Wrigglers spawn and all must die.

## Facts (game files, v0.107.1)

Numbers are for ascension 0. "(asc: n)" is the higher-ascension value: HP from ascension 8, damage from ascension 9.

Where: elite fight Phrog Parasite [PhrogParasiteElite], act 1 Overgrowth. It starts alone as enemy e0. When it dies, 4 Wrigglers join as e1 to e4 (see wriggler.md); e0 stays empty.

HP: 61-64 (asc: 66-68). The 4 Wrigglers add 74-78 HP in total (asc: 78-82). Whole fight: 135-142 HP.

Starts with Infested (shown with amount 4):
- When the Phrog dies, 4 Wrigglers are added. Always 4.
- The fight does not end when the Phrog dies. It ends when all 4 Wrigglers are dead.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Infect | `statuscard: 3` | Adds 3 Infection to your discard pile. No damage. |
| Lash | `attack 4x4` | 4 hits of 4 (asc: 5). 16 in total (asc: 20). |

Pattern: fixed, no randomness. Infect, Lash, Infect, Lash, starting with Infect. The next move is the one it did not just use. It gains no Strength and no Block.

Damage by enemy turn while it lives: 0, 16, 0, 16, 0, 16. Add 3 for each Infection in your hand at the end of your turn. It adds 3 Infections on enemy turns 1, 3, 5.

Special rules:
- Death on your turn T: the 4 Wrigglers appear at once with intent `stun`. Enemy turn T: nothing happens; the move the Phrog was showing is lost. Enemy turn T+1: 2 Wrigglers bite for 6 each (12) and 2 add 1 Infection each. Then 16, 16, 20, 20, 24 with all 4 alive (table in wriggler.md).
- Death during the enemy turn (reflected damage during Lash, for one): its remaining Lash hits are cancelled. The Wrigglers show `stun` for your whole next turn and also skip the enemy turn after it.
- A multi-hit attack that hits all enemies keeps going after the Phrog dies: its remaining hits land on the 4 new Wrigglers. A single-target multi-hit stops. (Read from the code path, not seen in a fight.)
- The Wrigglers are not Minions. Each one must be killed.

Status card: Infection. Status, Unplayable, cannot be upgraded.
- If it is in your hand when your turn ends, you take 3 damage per copy.
- Block absorbs it. Strength, Weak and Vulnerable do not change it.
- It resolves before the enemy acts, one copy at a time, and uses up this turn's Block first.
- It goes back to the discard pile. It is not Ethereal and does not exhaust itself: each copy cycles through your deck for the rest of the fight.
- New copies start in the discard pile, so you draw them after the next reshuffle.
- Worked: 2 Infections in hand, Lash 4x4 coming, 12 Block. The Infections take 6 Block, Lash meets 6 Block, 10 HP lost. Taking 0 needs 6 + 16 = 22 Block.

## How to fight it
Derived from the facts above, not from play.
- Kill the Phrog on a turn it shows `attack 4x4`: the Lash never happens and the Wrigglers skip that enemy turn, so no attack lands.
- Its `statuscard: 3` turns deal no attack damage. Block only for Infections in hand; spend the rest on damage.
- Block needed each turn = 3 per Infection in hand + the attack shown.
- Have area damage ready for the turn the Wrigglers appear: 4 targets with 17-21 HP each, and they do nothing that enemy turn.
- A long fight fills the deck: the Phrog adds 3 Infections every 2 turns, the Wrigglers 2 per turn.

## Lessons
(none yet)
