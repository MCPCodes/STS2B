# Two-Tailed Rat
17-21 HP, 3 at the start: attack 8, attack 6 or 1 Frail; from turn 3 a rat can summon 1 more (3 summons a fight, 5 rats at once).

## Facts (game files, v0.107.1)

Where: normal fight Two-Tailed Rats [TwoTailedRatsNormal], act 1 Underdocks. 3 rats at the start, e0, e1, e2, acting in that order. Up to 3 more are summoned during the fight. Normal fights come from hallway fight 4 on.

HP: 17-21 each (asc 8+: 18-22). In single player the 3 starters always differ, 54-60 in total, and a summoned rat differs from every living rat.

Co-op: HP x players x 1.1, rounded down (2 players: 37, 39, 41, 44 or 46 each). Rats can have the same HP (seen). Damage, Frail and the summon rules are not scaled: still 3 summons a fight and 5 rats at once. Every attack hits every player and Frail goes to every player. (2 players seen in a fight, with 1 summon.)

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Scratch | `attack 8` | 1 hit: 8 base (asc 9+: 9) + Strength. |
| Disease Bite | `attack 6` | 1 hit: 6 base (asc 9+: 7) + Strength. Nothing else: no debuff, no status card. |
| Screech | `debuff` | 1 Frail on you (same at every ascension). No damage. |
| Call for Backup | `summon` | Adds 1 Two-Tailed Rat at full HP. No damage. Once per rat. |

Pattern, turns 1 and 2:
- Enemy turn 1: the 3 starters use 1 Scratch, 1 Disease Bite and 1 Screech between them. Always 14 damage and 1 Frail.
- Which rat does which is 1 of 3 line-ups, 1/3 each. (e0, e1, e2) = (Scratch, Disease Bite, Screech), (Disease Bite, Screech, Scratch) or (Screech, Scratch, Disease Bite).
- Enemy turn 2: each rat picks 50/50 between the 2 moves it did not just use. 6 to 22 damage, 14 on average.

| Its turn 1 intent | Its turn 2 intent, 50/50 |
| --- | --- |
| `attack 8` | `attack 6` or `debuff` |
| `attack 6` | `attack 8` or `debuff` |
| `debuff` | `attack 8` or `attack 6` |

Pattern, every later move of a rat:
- Never the move it used last.
- Never Screech if Screech is among its last 3 moves. So a rat that just screeched attacks on its next 3 moves, Scratch and Disease Bite in turn, unless it summons.
- Among the moves left it picks evenly, unless it can summon.

Summon:
- A rat can summon when all of these hold: it has done 2 moves that were not a summon; it has not summoned before; fewer than 3 summons were done in this fight; fewer than 5 rats are in the fight; no other rat is set to summon.
- A rat that can summon shows `summon` 9 times in 11 (82%). If only 1 other move is open to it, 9 times in 10: on turn 3 that is the starter that screeched on turn 1.
- The rats pick in acting order. Only 1 rat shows `summon` per turn.
- On the turn right after a summon, a rat that acts before the summoner cannot show `summon`. One that acts after it can. (Read from the code.)
- The first `summon` can show on your turn 3. With all 3 starters alive, e0 picks first, then e1, then e2. The chance that none of them shows it is about 1 in 300.
- The new rat appears during that enemy turn, at full HP, with no powers, and does not act in it. On your next turn it shows its first intent: Scratch, Disease Bite or Screech, 1/3 each.
- If nobody dies, the first 2 summons usually come on enemy turns 3 and 4. The fight then has 5 rats, and the third summon needs a rat to die first.
- A new rat needs 2 moves of its own first. One that arrives on enemy turn K can show `summon` from your turn K + 3.
- Killing a rat that shows `summon` before it acts cancels the summon. It does not count toward the 3. Another rat can show `summon` on the next turn.
- The move after its own summon: Scratch, Disease Bite or Screech, 1/3 each, or 50/50 between the 2 attacks if Screech is among its last 3 moves. It may be the move it used before the summon (seen).
- At most 6 rats are fought: 3 starters and 3 summoned.
- Seen in fights: `summon` shown on turn 3 (twice), the summon done, and the new rat as e3 on turn 4. Read from the code only: the second and third summon, the cap of 3, the stop at 5 rats, the block on the turn after a summon.

Ids and acting order:
- Summoned rats get e3, e4, e5 in the order they arrive.
- Acting order is by place, not by id. There are 5 places and the starters hold the last 3. A new rat takes the last free place.
- With all starters alive that place is in front of them, so the new rat acts before e0. The place of a dead starter is free again.

Frail from Screech:
- Frail 1 covers exactly your next turn. Block from cards is x0.75, rounded down: Defend 5 gives 3.
- You are Frail on your turn 2, unless the rat showing `debuff` dies on your turn 1.
- 2 Screeches on the same enemy turn give Frail 2, which covers your next 2 turns.

Worked numbers:
- Enemy turn 1 with all 3 alive: 8 + 6 = 14. Your turn 1 Block is full. Frail starts on your turn 2.
- 3 Strikes (18) kill a rat with 17 or 18 HP. A 19-21 HP rat needs 4.
- Weak on a rat rounds down: Scratch 8 x 0.75 = 6. Disease Bite 6 x 0.75 = 4.5, so 4.
- Vulnerable on you: Scratch 12, Disease Bite 9.

Special rules:
- Every rat must die, summoned ones too. None is a Minion.
- They never gain Block or Strength by themselves. No heal, escape or on-death effect.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- A rat that is dead before your turn 3 never summons. Each starter still alive then is likely to add 17-21 HP to the fight.
- Kill one rat at a time, lowest HP first. Spread damage stops no summon.
- A rat showing `summon` deals 0 this turn, and killing it now cancels the new rat. Kill it first when the damage is there.
- Turn 1 Block is full and 14 is coming. Turn 2 Block cards give 75%: lean turn 2 toward attacks.
- Damage to all enemies counts 3 to 5 times here.

## Lessons
(none yet)
