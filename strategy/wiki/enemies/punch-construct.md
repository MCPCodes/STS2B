# Punch Construct
55 HP, Artifact 1: fixed loop of 10 Block, then attack 5x2 with 1 Frail, then attack 14; its numbers never grow.

## Facts (game files, v0.107.1)

Where:
- Normal fight Punch Construct [PunchConstructNormal], act 1 Underdocks: 1 Punch Construct, alone, enemy e0. Normal fights come from hallway fight 4 on.
- Event fight [PunchOffEventEncounter], started from the Punch Off event (Underdocks): 2 Punch Constructs. See "Punch Off event fight" below.
- Normal fight ConstructMenagerieNormal, act 3 Glory: Punch Construct (e0), then 2 Cubex Constructs (e1, e2). They act in that order. See cubex-construct.md.

HP: 55 (asc 8+: 60). Always the same number.

Co-op: HP x players x 1.1 in act 1 (2 players: 121), x 1.2 in the act 3 fight (2 players: 132). Artifact is 1 plus 1 per extra player (2 players: 2). Ready's Block is scaled like HP (2 players: 22 in act 1, 24 in act 3). Damage and Frail are not scaled; every attack hits every player and Frail goes to every player. (Read from the code, not seen in a fight.)

Starts with Artifact 1 and 0 Block:
- Artifact cancels the first debuff put on it (Vulnerable, Weak, Poison, Strength loss, any other), then it is gone. The damage of the card that carried the debuff still lands.
- A debuff from a relic at fight start uses the Artifact up before your first card. Seen with Bag of Marbles: on turn 1 it had no Artifact and no Vulnerable.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Ready | `defend` | Gains 10 Block. No damage. |
| Fast Punch | `attack 5x2`, `debuff` | 2 hits: 5 base each (asc 9+: 6) + Strength on every hit. Then 1 Frail on you, blocked or not. |
| Strong Punch | `attack 14` | 1 hit: 14 base (asc 9+: 16) + Strength. |

Pattern: fixed loop of 3, no randomness: Ready, Fast Punch, Strong Punch. It starts with Ready.
- After `defend` comes `attack 5x2` + `debuff`. After that comes `attack 14`. After `attack 14` comes `defend`.

By turn. It gains no Strength, so the loop never changes:

| Turn | 1 | 2 | 3 | 4 | 5 | 6 |
| --- | --- | --- | --- | --- | --- | --- |
| Its Block during your turn | 0 | 10 | 0 | 0 | 10 | 0 |
| You are Frail on your turn | no | no | yes | no | no | yes |
| Its move on the enemy turn | Ready | Fast Punch | Strong Punch | Ready | Fast Punch | Strong Punch |
| Damage | 0 | 5x2 = 10 | 14 | 0 | 10 | 14 |

- 24 damage per 3 turns, for the whole fight.
- Its 10 Block is up during your turns 2, 5, 8. It is removed when that enemy turn starts, before it punches.
- Frail 1 covers exactly your next turn: your turns 3, 6, 9, the turns right before Strong Punch lands.

Punch Off event fight (read from the code, not seen in a fight):
- The event Punch Off is in the Underdocks event list, from total floor 6 on. Its first choice adds the curse Injury to your deck and gives a relic, with no fight. Its second choice leads to a Fight button that starts this fight.
- 2 Punch Constructs, Artifact 1 each. Each starts 2-9 HP down: 46-53 of 55.
- e0 starts on Fast Punch: Fast Punch, Strong Punch, Ready. e1 starts on Ready: Ready, Fast Punch, Strong Punch.
- Damage with both alive: 10, 24, 14, repeating.
- You are Frail on your turns 2, 3, 5, 6, 8, 9. Not on turns 1, 4, 7.
- e1 has 10 Block during your turns 2, 5, 8. e0 has it during your turns 4, 7.
- The event adds 1 relic and 1 potion per player to the fight's rewards. The fight's normal rewards were not traced.

Worked numbers:
- Bash while Artifact is up: 8 damage, no Vulnerable, Artifact gone.
- Your turn 2 or 5: 3 Strikes (18) into its 10 Block take 8 HP. Killing it from 30 HP on such a turn takes 40 damage.
- Frail turn: Defend 5 gives 3. 3 Defends give 9 Block, so Strong Punch 14 takes 5 HP.
- Fast Punch into Block: each hit meets the Block that is left. 5x2 into 7 Block: 5 blocked, then 2 blocked and 3 through.
- Weak on it rounds down per hit: Fast Punch 5 x 0.75 = 3.75, so 3x2 = 6, not 7. Strong Punch 10.

Special rules:
- The `defend` intent shows no number. The Block is 10. Frail on the construct cuts it to 7.
- It never gains Strength. No summon, heal, escape or on-death effect.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Alone, enemy turn 1 deals 0 and it has 0 Block on your turn 1: put the whole turn into damage or setup.
- On your turns 2, 5, 8 the first 10 damage goes into its Block, and 10 is coming at you. Good turns for Block and Powers.
- Your turns 3, 6, 9 are Frail turns with 14 incoming: Block cards give 75%, attacks are full, and it has 0 Block.
- Artifact eats the first debuff. Play a cheap debuff before the one you need. With only Bash, expect 8 damage and no Vulnerable.
- It never grows: 24 damage per 3 turns. A longer fight costs the same per turn, so there is no race.

## Lessons
(none yet)
