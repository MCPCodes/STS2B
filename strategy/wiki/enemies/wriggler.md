# Wriggler
17-21 HP enemy in packs of 4 (Phrog Parasite's death, Dense Vegetation event): alternates a bite of 6 with +2 Strength and 1 Infection.

## Facts (game files, v0.107.1)

Numbers are for ascension 0. "(asc: n)" is the higher-ascension value: HP from ascension 8, damage from ascension 9.

Where:
- Elite fight Phrog Parasite [PhrogParasiteElite]: 4 Wrigglers spawn when the Phrog Parasite dies. They are enemies e1 to e4. They start with intent `stun` and lose their first enemy turn.
- Event fight [DenseVegetationEventEncounter], started by one option of the Dense Vegetation event: 4 Wrigglers from the start, not stunned, acting from enemy turn 1. That option heals like a rest site first. The fight gives hallway rewards.

HP: 17-21 each (asc: 18-22). The 4 always have 4 different values. Total 74-78 (asc: 78-82).

Starts with: no powers. It is not a Minion; every Wriggler must be killed.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Spawned | `stun` | Nothing. Only the first move of a Wriggler spawned by the Phrog Parasite. |
| Nasty Bite | `attack 6` | 1 hit: 6 base (asc: 7) + its own Strength. |
| Wriggle | `buff`, `statuscard: 1` | Adds 1 Infection to your discard pile, then gains 2 Strength (itself only). |

Pattern: fixed by position, no randomness.
- First real move: the 1st and 3rd Wriggler in the list bite, the 2nd and 4th wriggle. In the Phrog fight that is e1 and e3 biting, e2 and e4 wriggling. In the event fight e0 and e2 bite, e1 and e3 wriggle.
- After that each one alternates Nasty Bite and Wriggle. One that shows `attack` now shows `buff` next turn, and the reverse.
- With all 4 alive, 2 bite and 2 wriggle every enemy turn.
- A bite deals 6 + 2 per Wriggle that Wriggler has done.

Damage with all 4 alive, counted from their first acting turn:

| Acting turn | 1 | 2 | 3 | 4 | 5 | 6 |
| --- | --- | --- | --- | --- | --- | --- |
| Biters | 1st, 3rd | 2nd, 4th | 1st, 3rd | 2nd, 4th | 1st, 3rd | 2nd, 4th |
| Damage | 6+6 = 12 | 8+8 = 16 | 8+8 = 16 | 10+10 = 20 | 10+10 = 20 | 12+12 = 24 |
| Infections added | 2 | 2 | 2 | 2 | 2 | 2 |

Special rules:
- Spawn timing in the Phrog fight. Phrog dies on your turn T: they do nothing on enemy turn T, and acting turn 1 is enemy turn T+1. Phrog dies during the enemy turn: they show `stun` for your whole next turn, skip the enemy turn after it, and then start.
- Killing one changes nothing for the others.
- It never gains Block.

Status card: Infection. Status, Unplayable, cannot be upgraded.
- If it is in your hand when your turn ends, you take 3 damage per copy. Block absorbs it. Strength, Weak and Vulnerable do not change it.
- It resolves before the enemy acts and uses up this turn's Block first. Block needed = 3 per Infection in hand + the attacks shown.
- It is not Ethereal and does not exhaust itself: each copy cycles through your deck for the rest of the fight. New copies start in the discard pile.

## How to fight it
Derived from the facts above, not from play.
- Area damage hits all 4. Their HP are 4 different values in 17-21: check each one before counting a kill.
- The 1st and 3rd bite on the same turns, the 2nd and 4th on the others. With one pair dead, every other enemy turn has no attack.
- Killing one that shows `attack` removes its bite this turn. Killing one that shows `buff` stops 1 Infection and its +2 Strength.
- A Wriggler showing `buff` bites next turn for 2 more than its last bite.
- In the Phrog fight the spawn turn is free: they do nothing on that enemy turn. Play no Block against them then.

## Lessons
(none yet)
