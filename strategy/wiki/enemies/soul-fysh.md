# Soul Fysh
Underdocks boss, 211 HP: adds Beckons (1 energy to play, or 6 HP in hand at turn end); Intangible on your turns 5, 10, 15.

## Facts (game files, v0.107.1)

Numbers are for ascension 0. "(asc: n)" is the higher-ascension value: HP from ascension 8, damage from ascension 9. The Beckon counts, Intangible 2 and Vulnerable 3 are the same at every ascension.

Where: boss fight Soul Fysh [SoulFyshBoss], act 1 Underdocks, one of its 3 bosses. Alone, enemy e0. Nothing joins. The only randomness is where 1 Beckon lands in your draw pile.

HP: 211 (asc: 221).

Co-op: HP x players x 1.1, rounded down (2 players: 464). Every player gets their own Beckons. Attacks and the Vulnerable hit every player. Damage is not scaled. (2 players seen in a fight: 464 HP.)

Starts with: no powers.

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Beckon | `statuscard: 2` | Adds 2 Beckons: 1 into your draw pile at a random place, 1 onto your discard pile. No damage. |
| De-Gas | `attack 16` | 1 hit: 16 base (asc: 17). |
| Gaze | `attack 7`, `statuscard: 1` | 1 hit: 7 base (asc: 8). Then adds 1 Beckon to your discard pile, blocked or not. |
| Fade | `buff` | Gains 2 Intangible. No damage. |
| Scream | `attack 13`, `debuff` | 1 hit: 13 base (asc: 15). Then 3 Vulnerable on you. The hit lands before the Vulnerable. |

Pattern: fixed 5-move cycle, no randomness: Beckon, De-Gas, Gaze, Fade, Scream, repeat. It starts with Beckon.
- Next move: after `statuscard: 2` comes De-Gas, after De-Gas comes Gaze (`attack` + `statuscard: 1`), after Gaze comes `buff`, after `buff` comes Scream, after Scream comes `statuscard: 2`.
- It never gains Strength or Block and never heals.

Intangible (a buff on the Fysh, from Fade):
- While it has it, every hit on it removes at most 1 HP, whatever the source: attack cards, potions, Poison, HP loss. Each hit of a multi-hit card removes 1.
- Fade gives 2 on enemy turn 4. It loses 1 at the end of every enemy turn. So 1 is left for the whole of your turn 5, and it is gone at the end of enemy turn 5.
- It is Intangible for exactly 1 of your turns per cycle: your turns 5, 10, 15. That is the turn it shows Scream.
- Poison on it: the tick at the start of enemy turns 5, 10, 15 removes only 1 HP. The Poison amount still drops by 1.

Vulnerable 3 on you (from Scream):
- You read 3, 2, 1 on your next three turns. It covers the next 3 enemy turns: Beckon (no damage), De-Gas and Gaze.
- De-Gas 16 x 1.5 = 24. Gaze 7 x 1.5 = 10.5, so 10.
- It has run out before the next Scream, which is 13 again, when nothing else put Vulnerable on you.

Damage by enemy turn:

| Enemy turn | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Move | Beckon | De-Gas | Gaze | Fade | Scream | Beckon | De-Gas | Gaze | Fade | Scream |
| Damage | 0 | 16 | 7 | 0 | 13 | 0 | 24 | 10 | 0 | 13 |
| Running total | 0 | 16 | 23 | 23 | 36 | 36 | 60 | 70 | 70 | 83 |
| Beckons added | 2 | 0 | 1 | 0 | 0 | 2 | 0 | 1 | 0 | 0 |
| Beckons so far | 2 | 2 | 3 | 3 | 3 | 5 | 5 | 6 | 6 | 6 |
| Intangible on your turn of that number | no | no | no | no | yes | no | no | no | no | yes |

- The first cycle deals 36. Every later cycle deals 47.
- 3 Beckons per cycle: 2 on enemy turns 1, 6, 11 and 1 on enemy turns 3, 8, 13.

Worked numbers:
- Your turn 5 (Intangible 1): Twin Strike 5x2 removes 2 HP. A Fire Potion (20) removes 1 HP.
- Weak on the Fysh, rounded down: De-Gas 16 x 0.75 = 12. Gaze 7 x 0.75 = 5.25, so 5. Scream 13 x 0.75 = 9.75, so 9.
- Weak on the Fysh while you are Vulnerable (x 0.75 x 1.5): De-Gas 18. Gaze 7.875, so 7.

Special rules:
- No phases, no summons.

Status card: Beckon. Status, cost 1, cannot be upgraded. It is not Unplayable.
- Playing it costs 1 energy and does nothing. It goes to the discard pile, so it comes back after the next reshuffle.
- If it is in your hand when your turn ends, you lose 6 HP per copy. Block does not absorb it. Strength, Weak and Vulnerable do not change it.
- After that it goes to the discard pile. It is not Ethereal and does not exhaust itself: each copy cycles through your deck for the rest of the fight.
- A discard effect takes it out of your hand for free; it comes back after the next reshuffle. An Exhaust effect removes it for the fight.
- Of the 2 from the Beckon move, the one in the draw pile can be drawn on your very next turn. The other waits for the next reshuffle.
- Worked: 2 Beckons in hand with 3 energy. Playing both costs 2 energy and 0 HP. Keeping both costs 12 HP.
- Ironclad cards against status cards: strategy/wiki/cards/status.md.

## How to fight it
Derived from the facts above, not from play.
- Each Beckon in hand is a choice: 1 energy or 6 HP. Count the energy for them first. Exhaust a Beckon when a card lets you: it is then gone for the fight.
- Your turns 5, 10, 15: it is Intangible and each hit removes 1 HP. Play Block for Scream (13), Powers and Beckons. Hold attacks and damage potions.
- Enemy turns 1, 4, 6 and 9 deal 0. Play no Block on those turns. Turn 4 is the last full-damage turn before the Intangible turn.
- After Scream the next De-Gas is 24 and the next Gaze 10. Plan Block for your turns 7 and 8.
- It has 211 HP and takes real damage on 4 turns out of 5. Each extra cycle costs 47 damage and 3 more Beckons.

## Lessons
(none yet)
