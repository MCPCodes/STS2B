# Living Fog
80 HP: turn 1 hits 8 and limits you to 1 Skill per turn for the fight, then it alternates 5 plus a Gas Bomb and 8; its death wins.

## Facts (game files, v0.107.1)

Where: act 1 Underdocks only. Normal fight Living Fog [LivingFogNormal]. It starts alone as enemy e0. Each Bloat adds 1 Gas Bomb (see gas-bomb.md). The first bomb is e1, the next e2, and so on: every new bomb gets a new id. Normal fights come from hallway fight 4 on.

HP: 80, always (asc 8+: 82).

Co-op: HP x players x 1.1, rounded down (2 players: 176). Damage is not scaled; every attack hits every player, and each player gets Smoggy. (Read from the code, not seen in a fight.)

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Advanced Gas | `attack 8`, `carddebuff` | 1 hit: 8 base (asc 9+: 9) + Strength. Then Smoggy on you, blocked or not. Used once, on enemy turn 1. |
| Bloat | `attack 5`, `summon` | Adds 1 Gas Bomb (7 HP). Then 1 hit: 5 base (asc 9+: 6) + Strength. |
| Super Gas Blast | `attack 8` | 1 hit: 8 base (asc 9+: 9) + Strength. |

Pattern: fixed, no randomness.
- Enemy turn 1: Advanced Gas.
- Then Bloat and Super Gas Blast alternate, Bloat first: Bloat on even enemy turns, Super Gas Blast on odd enemy turns from 3.
- A bomb added on an even enemy turn does not act that turn. On the next enemy turn it acts before the Fog: it hits 8 and dies.

By enemy turn:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Living Fog | 8, Smoggy | 5, adds a bomb | 8 | 5, adds a bomb | 8 | 5, adds a bomb | 8 |
| Gas Bomb, if left alive | none | none | 8 | none | 8 | none | 8 |
| Total, bombs left alive | 8 | 5 | 16 | 5 | 16 | 5 | 16 |
| Total, bombs killed | 8 | 5 | 8 | 5 | 8 | 5 | 8 |
| Running total, bombs left alive | 8 | 13 | 29 | 34 | 50 | 55 | 71 |
| Running total, bombs killed | 8 | 13 | 21 | 26 | 34 | 39 | 47 |

Smoggy:
- A debuff on you with no counter and no duration. It stays for the whole fight.
- After you play a Skill, every other Skill you have is locked until the end of your turn: in hand, draw pile, discard pile and exhaust pile. A locked card cannot be played, by you or by an effect that plays cards.
- So you play at most 1 Skill per turn. Attacks and Powers are not limited.
- It lands in enemy turn 1. Your turn 1 has no limit; the limit holds from your turn 2 on.
- A Skill that enters the fight later in that turn is locked too.
- The lock goes on when a Skill finishes, not when it starts. Havoc or Cascade played as the turn's first Skill still plays the first Skill it turns up; later Skills in the same batch are not played. After another Skill, Havoc and Cascade are locked like any Skill.
- The lock comes off at the end of your turn, after the hand is discarded.
- Artifact on you stops Smoggy.
- In the snapshot Smoggy shows in your powers. Cards are not marked and their text does not name it. After the first Skill the other Skills in hand should read `playable: false` with `unplayable: BlockedByHook`, or `not enough energy` when they also cost too much. (Read from the mod's code, not seen.)

Special rules:
- The Living Fog is the only main enemy. The fight is won the moment it dies, and any living Gas Bomb dies with it.
- While a bomb is alive the Fog is marked `primary: true`.
- Bloat adds the bomb first, then hits. It is always 1 bomb per Bloat.
- It never gains Strength or Block.
- This fight has not been seen in play. Read from the code only: the one-Skill limit, the bomb's timing, the intent words `carddebuff` and `summon`, and the bomb's `attack 8`.

Worked numbers:
- From enemy turn 2, each 2 turns deal 21 with the bomb left alive (5 + 16) and 13 with the bomb killed (5 + 8).
- Weak on the Fog rounds down: 8 x 0.75 = 6. Bloat 5 x 0.75 = 3.75, so 3.
- Vulnerable on you: 8 becomes 12, Bloat 5 x 1.5 = 7.5, so 7. A turn with a bomb and Super Gas Blast: 12 + 12 = 24.
- A bomb has 7 HP: a Strike (6) leaves it at 1 HP and it still hits for 8.

Status cards: none. Smoggy locks cards; it adds none.

## How to fight it
Derived from the facts above, not from play.
- From your turn 2 you get 1 Skill per turn. Pick the best Skill in hand before you play any; the others are dead cards that turn. Your turn 1 has no limit: play the extra Skills then.
- Block from Skills is 1 card a turn, so stop damage with attacks: 7 damage on a bomb cancels its 8.
- Bombs are there on your odd turns from 3. Kill the bomb first (7 HP), then hit the Fog.
- Even enemy turns deal only 5. Those are the turns to skip Block and spend the 1 Skill on something else.
- On the turn the Fog can die, ignore the bomb: it dies with the Fog.

## Lessons
(none yet)
