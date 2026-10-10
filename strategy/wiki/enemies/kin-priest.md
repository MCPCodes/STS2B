# Kin Priest
Leader of The Kin boss fight, 190 HP: cycles 8 + Frail, 8 + Weak, 3x3, +2 Strength; killing it kills both Kin Followers and wins.

## Facts (game files, v0.107.1)

Numbers are for ascension 0. "(asc: n)" is the higher-ascension value: HP from ascension 8, damage from ascension 9. From ascension 9 Ritual and the Followers' Power Dance also give more Strength.

Where: boss fight The Kin [TheKinBoss], act 1 Overgrowth, one of its 3 bosses. Always with 2 Kin Followers (see kin-follower.md). e0 and e1 are the Followers, e2 is the Kin Priest. They act in that order, the Priest last. The Priest is marked `primary: true` while a Follower lives.

HP: 190 (asc: 199). The Followers have 58 and 59 (asc: 62 and 63).

Starts with: no powers. It is the only enemy that is not a Minion: when it dies, the living Followers die at once and the fight is won.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Orb of Frailty | `attack 8`, `debuff` | 1 hit: 8 base (asc: 9) + Strength. Then 1 Frail on you, blocked or not. |
| Orb of Weakness | `attack 8`, `debuff` | 1 hit: 8 base (asc: 9) + Strength. Then 1 Weak on you, blocked or not. |
| Beam | `attack 3x3` | 3 hits: 3 base each + Strength on every hit. |
| Ritual | `buff` | Gains 2 Strength. Itself only. |

Pattern: fixed 4-move cycle, no randomness: Orb of Frailty, Orb of Weakness, Beam, Ritual, repeat. It starts with Orb of Frailty.
- The 2 orbs show the same intent. The first one after `buff` (or at the start of the fight) is Frailty, the second is Weakness.
- Each Ritual adds 2 to each orb and 6 to Beam.

Debuffs it puts on you:
- Frail 1 covers exactly your next turn: your turns 2, 6, 10. Block from cards is x0.75, rounded down: Defend 5 gives 3.
- Weak 1 covers exactly the turn after that: your turns 3, 7, 11. Your attack damage is x0.75, rounded down per hit: Strike 6 deals 4.
- Your turns 1, 4, 5, 8, 9, 12 have neither.

Damage by enemy turn with all 3 alive, when nothing changes their Strength (turns 9-12 continue the same cycles):

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Priest move | Frailty | Weakness | Beam | Ritual | Frailty | Weakness | Beam | Ritual | Frailty | Weakness | Beam | Ritual |
| Priest | 8 | 8 | 3x3 = 9 | 0 | 10 | 10 | 5x3 = 15 | 0 | 12 | 12 | 7x3 = 21 | 0 |
| e0 Follower | 0 | 7 | 4x2 = 8 | 0 | 9 | 6x2 = 12 | 0 | 11 | 8x2 = 16 | 0 | 13 | 10x2 = 20 |
| e1 Follower | 5 | 2x2 = 4 | 0 | 7 | 4x2 = 8 | 0 | 9 | 6x2 = 12 | 0 | 11 | 8x2 = 16 | 0 |
| Total | 13 | 19 | 17 | 7 | 27 | 22 | 24 | 23 | 28 | 23 | 50 | 20 |

- The Priest alone deals 25, 35, 45 per 4 turns (10 more each cycle).
- All 3 together deal 56 over turns 1-4, 96 over turns 5-8, 121 over turns 9-12.

Worked numbers:
- Turn 3 with 12 Block: e0 hits 4 then 4 (8 blocked), then Beam hits 3, 3, 3 into the 4 Block left: 5 HP lost. Same as 17 - 12.
- Weak on the Priest rounds down per hit. Turn 7 Beam: 5 x 0.75 = 3.75, so 3x3 = 9, not 11.
- 1 point of Strength taken from the Priest removes 1 from an orb and 3 from Beam.

Special rules:
- A Follower's death changes none of the Priest's numbers.
- Frail and Weak land after the orb's hit. They do not change that turn's Block or damage.
- It never gains Block. No summons.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Killing the Priest wins: 190 HP against 307 for all 3. Followers only need to die if that saves more HP than it costs in turns.
- Frail turns (2, 6, 10): Block cards give 75%, attacks are full. Weak turns (3, 7, 11): attacks deal 75%, Block is full. Lean each turn toward the half that is not cut.
- Enemy turn 4 deals 7 and turn 1 deals 13: the cheapest turns to skip Block and set up.
- Damage climbs each cycle (56, 96, 121 per 4 turns) and turn 11 deals 50. Aim to end the fight before then.
- Area damage also hits both Followers; single-target damage goes to e2.

## Lessons
(none yet)
