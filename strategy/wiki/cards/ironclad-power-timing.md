# Ironclad Power timing (measured)
When two scripted players played each Ironclad Power in simulated act 1 fights (a quiet turn, a small hit or a large hit coming), how often, and what the card was worth to each.

## Facts (simulator, game v0.107.1)

### Status: evidence, not rules
- These numbers come from single simulated fights played by two scripts, in generated decks that were not built around the card. They have not been confirmed over whole runs, where HP, potions and the deck carry from fight to fight.
- The tables say on what kind of turn a script played a Power, and what the deck was worth with it. They do not compare playing it now with playing it a turn later in the same position. Only the hand-built positions further down do that, for four Powers.
- Every Power is different: read the rows of the card you hold, not the totals. Its two rows are in the table under "Card by card", with a line on it below that table; Demon Form, Barricade, Unmovable and Inflame also have a hand-built position near the end. Weigh them with your HP, what is coming, and what the Power will do in this deck. Do not take a rule from this page.
- This note goes when the run simulator has re-measured them.

### What was measured and how to read it
- The one-turn player searches the current turn and scores the position at its end. It plans nothing across turns. A Power it has no rule for is worth a flat amount to it for every turn it expects the fight to last, whatever the deck.
- The play-out player lists ways to play the turn, plays the rest of the fight out on copies in which everything hidden is reshuffled, and takes the turn whose fights ended best: a win first, then HP left. For every Power in hand it always compares the best turn with it and the turn with it and the most Block. It uses nothing a player could not know. The arena calls the two players Lookahead and Rollout.
- Kind of turn = the damage coming at the start of the turn through the Block already there: quiet 0, small 1 to 9, large 10 or more.
- "Could be played" = the card was in hand and affordable at a decision of that turn. A card that stays in the hand is counted again on every such turn.
- Card by card: 300 elite and boss fights of act 1 (150 each) for every Power, in a generated deck with one copy of it added, the same fights for both players. The play-out player ran at its fast setting (24 play-outs a decision). 18 Powers: every single-player Ironclad Power but Stampede.
- Worth = HP left after the fight (a loss counts as 0, a win includes Burning Blood's 6) with the Power, minus the same fight with one more Strike in its place, played by the same player. ± is the 95% interval.
- Worth is on each player's own scale. 10 control cards (cards whose worth was not in doubt, measured with both players on the same fights) came out 1.1 HP higher on average with the play-out player (±1.2), so compare one-turn rows with one-turn rows and play-out rows with play-out rows. `ironclad-pick-values.md` has the table that takes this out: of the 18 Powers only Demon Form (+1.9 ±1.3), Aggression (+1.5 ±1.4) and Unmovable (+2.5 ±1.5) moved clearly beyond the controls.

### All Powers together, by the damage coming
- 780 generated fights from three seeds, the same fights for both players: 188 weak hallway fights (the kind that opens the act), 216 regular hallway fights, 188 elites, 188 bosses. Decks as generated, with whatever Powers they held. The play-out player ran at its normal setting (64 play-outs a decision).
- Share of the turns a Power could be played on which it was played. The row above All holds every turn on which the damage coming was at least the HP left, whatever its size; the four rows above it leave those turns out.

| Coming through at the start of the turn | One-turn player | Play-out player |
|---|---|---|
| 0 | 74% of 125 | 69% of 137 |
| 1 to 9 | 73% of 89 | 59% of 112 |
| 10 to 19 | 50% of 241 | 54% of 228 |
| 20 or more | 49% of 59 | 48% of 58 |
| As much as the HP left or more (it kills unless blocked) | 38% of 13 | 0% of 14 |
| All | 59% of 527 | 57% of 549 |

- Both set up more on quiet turns: about 7 quiet turns in 10, about 5 in 10 with 10 or more coming.
- With a hit coming that kills unless it is blocked, the play-out player never played a Power (0 of 14 turns). The one-turn player did on 5 of 13; it lived through each of those 5 turns and won 1 of the 5 fights. 13 and 14 turns are too few to say more.
- Over all turns of these 780 fights the two played a Power about equally often (59%, 57%). Which Powers each of them played differs (next table).

### Card by card: elites and bosses
- Two rows a Power: the one-turn player, then the play-out player at its fast setting. Sorted from the Power the play-out player plays much more often than the one-turn player down to the one it plays much less often.
- Fights = fights it was played in, of the fights it was in hand in. Quiet, Small, Large = played on that share of the turns of that kind on which it could be played.

| Power | Player | Worth | Fights | Played when it could be | Quiet | Small | Large |
|---|---|---|---|---|---|---|---|
| Juggernaut | one-turn | +2.7 ±1.5 | 138 of 297 | 28% of 498 | 42% of 118 | 51% of 43 | 20% of 337 |
| Juggernaut | play-out | +4.5 ±1.7 | 210 of 296 | 58% of 371 | 75% of 101 | 73% of 37 | 48% of 233 |
| Unmovable | one-turn | +0.5 ±1.3 | 143 of 297 | 28% of 515 | 34% of 107 | 50% of 46 | 23% of 362 |
| Unmovable | play-out | +4.1 ±1.6 | 222 of 298 | 57% of 396 | 58% of 104 | 80% of 40 | 52% of 252 |
| Pyre | one-turn | +1.5 ±1.2 | 130 of 297 | 26% of 511 | 46% of 120 | 50% of 44 | 16% of 347 |
| Pyre | play-out | +3.0 ±1.5 | 202 of 297 | 50% of 405 | 71% of 107 | 61% of 41 | 40% of 257 |
| Corruption | one-turn | +0.8 ±1.5 | 118 of 298 | 22% of 527 | 23% of 115 | 36% of 45 | 20% of 367 |
| Corruption | play-out | +2.9 ±1.6 | 197 of 299 | 46% of 428 | 55% of 110 | 59% of 39 | 41% of 279 |
| Aggression | one-turn | +2.8 ±1.4 | 192 of 298 | 47% of 414 | 68% of 100 | 77% of 30 | 37% of 284 |
| Aggression | play-out | +5.5 ±1.6 | 237 of 294 | 67% of 362 | 81% of 103 | 78% of 36 | 58% of 223 |
| Crimson Mantle | one-turn | +8.8 ±1.6 | 252 of 298 | 71% of 363 | 92% of 96 | 85% of 33 | 60% of 234 |
| Crimson Mantle | play-out | +11.0 ±1.7 | 267 of 299 | 82% of 331 | 92% of 98 | 89% of 38 | 76% of 195 |
| Demon Form | one-turn | +4.0 ±1.6 | 194 of 298 | 49% of 392 | 71% of 98 | 80% of 30 | 38% of 264 |
| Demon Form | play-out | +7.1 ±1.6 | 200 of 296 | 56% of 361 | 75% of 100 | 59% of 34 | 47% of 227 |
| Barricade | one-turn | -1.8 ±1.4 | 52 of 297 | 8% of 616 | 20% of 142 | 29% of 49 | 2% of 425 |
| Barricade | play-out | -0.8 ±1.5 | 71 of 296 | 13% of 565 | 21% of 143 | 38% of 45 | 6% of 377 |
| Dark Embrace | one-turn | -2.3 ±1.3 | 94 of 298 | 17% of 562 | 31% of 116 | 51% of 43 | 9% of 403 |
| Dark Embrace | play-out | -2.4 ±1.2 | 90 of 298 | 16% of 581 | 22% of 148 | 27% of 52 | 12% of 381 |
| Hellraiser | one-turn | +1.7 ±1.5 | 238 of 298 | 63% of 380 | 80% of 98 | 94% of 33 | 52% of 249 |
| Hellraiser | play-out | +2.7 ±1.6 | 219 of 297 | 60% of 369 | 69% of 103 | 76% of 34 | 53% of 232 |
| Cruelty | one-turn | -1.2 ±1.4 | 212 of 298 | 53% of 411 | 77% of 102 | 77% of 35 | 40% of 274 |
| Cruelty | play-out | -0.8 ±1.3 | 209 of 296 | 49% of 430 | 63% of 117 | 51% of 41 | 43% of 272 |
| Inferno | one-turn | +4.2 ±1.5 | 263 of 298 | 79% of 346 | 90% of 93 | 100% of 26 | 72% of 227 |
| Inferno | play-out | +6.5 ±1.7 | 251 of 299 | 74% of 349 | 87% of 101 | 79% of 29 | 67% of 219 |
| Rupture | one-turn | -0.3 ±1.3 | 210 of 297 | 53% of 421 | 74% of 105 | 86% of 35 | 41% of 281 |
| Rupture | play-out | -0.1 ±1.4 | 192 of 296 | 47% of 439 | 67% of 112 | 53% of 45 | 38% of 282 |
| Stone Armor | one-turn | -0.6 ±1.3 | 260 of 298 | 69% of 393 | 64% of 102 | 81% of 32 | 70% of 259 |
| Stone Armor | play-out | +1.3 ±1.3 | 236 of 297 | 60% of 416 | 62% of 110 | 64% of 42 | 59% of 264 |
| Juggling | one-turn | -0.4 ±1.3 | 197 of 299 | 49% of 431 | 72% of 109 | 82% of 33 | 36% of 289 |
| Juggling | play-out | -0.5 ±1.5 | 177 of 298 | 40% of 468 | 54% of 120 | 50% of 36 | 33% of 312 |
| Feel No Pain | one-turn | -1.5 ±1.3 | 218 of 297 | 55% of 431 | 71% of 107 | 70% of 40 | 46% of 284 |
| Feel No Pain | play-out | -0.5 ±1.4 | 194 of 297 | 44% of 474 | 56% of 139 | 54% of 46 | 37% of 289 |
| Inflame | one-turn | +3.2 ±1.3 | 286 of 297 | 92% of 327 | 96% of 95 | 96% of 27 | 90% of 205 |
| Inflame | play-out | +5.2 ±1.4 | 268 of 295 | 80% of 350 | 91% of 98 | 76% of 33 | 75% of 219 |
| Vicious | one-turn | -2.7 ±1.3 | 202 of 297 | 49% of 426 | 73% of 98 | 88% of 32 | 37% of 296 |
| Vicious | play-out | -2.0 ±1.3 | 168 of 298 | 35% of 495 | 51% of 121 | 46% of 48 | 28% of 326 |
| All 18 | one-turn | | | 44% of 7,964 | 60% of 1,921 | 68% of 656 | 35% of 5,387 |
| All 18 | play-out | | | 49% of 7,590 | 62% of 2,035 | 60% of 716 | 42% of 4,839 |

- A Small cell rests on 26 to 52 turns, most of them boss turns (see "Elites and bosses differ"). Read that column as rough.
- Unmovable is the card whose worth the play-outs change most: +4.1 ±1.6 with the play-out player against +0.5 ±1.3 with the one-turn player, +2.5 ±1.5 of it beyond what the control cards gained. Played twice as often (57% of the turns it could be, against 28%; in 222 of 298 fights against 143 of 297). Its highest share is with a small hit coming: 80% of 40 turns, against 58% of 104 quiet turns and 52% of 252 large ones. But 34 of those 40 turns are boss turns, and against bosses the gap to quiet turns is not clear (82% of 34 against 67% of 30).
- Demon Form: both played it on about three quiet turns in four (71% of 98, 75% of 100) and less often into a large hit (38% of 264, 47% of 227). It was still never played in a third of the fights it came to hand in (104 of 298, 96 of 296): three energy is the whole turn. Worth +7.1 ±1.6 with the play-out player against +4.0 ±1.6 with the one-turn player, +1.9 ±1.3 of it beyond the control cards. Elites and bosses differ most for this card (next section).
- Juggernaut, Pyre, Corruption: played about twice as often by the play-out player (58%, 50%, 46% against 28%, 26%, 22%), and more often on every kind of turn. Their worth rose with the control cards, not clearly beyond them.
- Aggression and Crimson Mantle: played more by the play-out player (67% against 47%, 82% against 71%). The gap is widest with a large hit coming (58% against 37%, 76% against 60%).
- Barricade and Dark Embrace: still left in the hand by both players. Barricade was played on 8% and 13% of the turns it could be, and almost never with a large hit coming (2% of 425, 6% of 377). Dark Embrace on 17% and 16%. What they are worth when played well is not shown.
- Played less by the play-out player: Vicious (35% against 49%), Inflame (80% against 92%), Feel No Pain (44% against 55%), Juggling (40% against 49%), Stone Armor (60% against 69%). A little less, by 3 to 6 points, which these counts do not tell from chance: Rupture, Inferno, Cruelty, Hellraiser. For none of these nine did Worth move clearly against the control cards.
- With a small hit coming the one-turn player played 11 of the 18 Powers on at least three such turns in four: Inferno 100% of 26, Hellraiser 94% of 33, Vicious 88% of 32, Rupture 86% of 35. The play-out player played those four on 79%, 76%, 46% and 53% of such turns. It played Unmovable, Juggernaut and Corruption on more of them than the one-turn player did (80%, 73%, 59% against 50%, 51%, 36%).
- Vicious stayed below a Strike with both players (-2.7 ±1.3 one-turn, -2.0 ±1.3 play-out), whether it was played on 49% of the turns or on 35%. The decks were not built to apply Vulnerable.

### Elites and bosses differ
- The 18 Powers together, split by the kind of fight (150 elites and 150 bosses a Power):

| Fights | Player | Quiet | Small | Large | All turns |
|---|---|---|---|---|---|
| Elites | one-turn | 66% of 1,178 | 38% of 73 | 24% of 2,393 | 38% of 3,644 |
| Elites | play-out | 64% of 1,298 | 37% of 98 | 32% of 2,083 | 44% of 3,479 |
| Bosses | one-turn | 52% of 743 | 71% of 583 | 44% of 2,994 | 49% of 4,320 |
| Bosses | play-out | 57% of 737 | 64% of 618 | 50% of 2,756 | 53% of 4,111 |

- An elite fight lasted 5.6 turns on average with either player, a boss fight 9.2 (one-turn) and 9.9 (play-out), in the decks with one more Strike.
- Against elites both players mostly kept a Power in the hand when 10 or more was coming (played on 24% and 32% of those turns) and played it on quiet turns (66%, 64%). Against bosses they played it on about half the large turns too (44%, 50%).
- Demon Form shows it most. Against elites it went into a large hit on 8% of 136 turns (one-turn) and 19% of 108 (play-out), and onto a quiet turn on 72% of 64 and 81% of 67. Against bosses it was played on 70% and 71% of the large turns, as often as on quiet ones (71%, 64%).
- Most small turns are boss turns: 583 of 656 for the one-turn player, 618 of 716 for the play-out player.
- Five Powers that paid had a higher Worth against bosses than against elites. With the play-out player: Crimson Mantle +16.0 ±2.6 against bosses and +6.1 ±2.0 against elites, Demon Form +10.0 ±2.4 and +4.2 ±1.9, Aggression +7.2 ±2.4 and +3.7 ±2.0, Juggernaut +6.1 ±2.6 and +2.8 ±2.1, Unmovable +5.6 ±2.6 and +2.6 ±1.8. The gap is clear for the first three. For Juggernaut and Unmovable it is inside the intervals. Inferno's Worth did not differ (+7.0 ±2.4 against bosses, +6.0 ±2.4 against elites).

### How often a turn is quiet
- Against elites and bosses about two turns in three on which one of the 18 Powers could be played had 10 or more coming: 68% for the one-turn player (5,387 of 7,964 turns), 64% for the play-out player (4,839 of 7,590). About one in four was quiet (24%, 27%). The rest had a small hit coming.
- More than half of the turns on which a player did play one of these Powers had 10 or more coming: 1,905 of 3,507 for the one-turn player, 2,028 of 3,712 for the play-out player. A third were quiet turns (1,159 and 1,253).
- One copy of a Power often never met a quiet turn. The one-turn player had Barricade in hand in 297 fights; in 168 of them (57%) there was no quiet turn on which it could be played. It never played it in 245 of the 297, and 145 of those (59%) had no such turn. For Demon Form it was 82 of the 104 fights in which that player had the card in hand and never played it.
- So in these fights a player that waited for a quiet turn before playing a Power would often not have played it at all. Whether playing it into the hit was the better turn is a different question, and these tables do not answer it.

### Hand-built positions (worked on paper, not measurements)
- These positions were built by hand to test the play-out player, each with its answer worked out before it was run. Unless a line says otherwise the deck is five cards that are all in the hand every turn (four once the Power is played), energy is 3, and the enemy repeats a fixed pattern. They show how the timing question comes out in one exact position. They do not say how often such positions come up. HP at the end is given before Burning Blood.
- A quiet turn, Inflame in hand. Deck: Inflame, Strike, Strike, Defend, Defend; 40 HP. Enemy: 48 HP, nothing on turn 1, then 15 on three turns of every four. Inflame and two Strikes at 8 on turn 1 (32 left); two Strikes and a Defend on turn 2 (10 through); two Strikes kill on turn 3: won on 30 HP. Without Inflame on turn 1 at least 15 gets through before the enemy is dead: 25 HP at best. The play-out player played Inflame first.
- Two quiet turns before a hit of 30, Barricade in hand. Deck: Barricade, Defend, Defend, Strike, Strike; 15 HP. Enemy: 40 HP, nothing on turns 1 and 2, 30 on turn 3, then round again. Barricade on turn 1 takes all 3 energy. Turns 2 and 3: Defend, Defend, Strike each: the Block stays, 20 of it stand against the 30, 10 through, 5 HP left. Turns 4 and 5: two Strikes and a Defend each. Turn 6: one Strike kills before the next 30: won on 5 HP. Without Barricade on turn 1 the enemy cannot be dead by turn 3 (at most 36 damage) and one turn's Block is 10: 20 through at 15 HP, dead. Barricade on turn 2 gives the same 10 Block, dead. The play-out player played Barricade on turn 1. The one-turn player left it in the hand and died to the 30.
- A large hit now and quiet turns after it, Demon Form in hand. Deck: Demon Form, Defend, Defend, Strike, Strike; 25 HP. Enemy: 60 HP, 20 on turns 1, 5 and 9, nothing in between. Block first: Defend, Defend, Strike on turn 1 (10 through, 15 HP), Demon Form on turn 2, then two Strikes a turn at 8, 10 and 12: 6 + 16 + 20 + 24 = 66, dead on turn 5 before the second hit: won on 15 HP. Demon Form on turn 1 instead: 20 through, and the enemy dies a turn sooner: won on 5 HP. Never Demon Form: 10 HP at best (two Strikes and a Defend on turn 1, 15 through, then two Strikes a turn: 60 damage on turn 5, before the second hit); with both Defends on turn 1 it is 42 damage by turn 4, the second hit costs 10 more, and the fight is won on 5 HP. The play-out player blocked on turn 1 and played Demon Form on turn 2.
- A hit that would kill, Demon Form in hand. Hand: Demon Form, Strike, Strike, Defend, Defend, with the starting deck behind it; 15 HP; 22 coming; the enemy has 100 HP. Two Defends leave 3 HP. Demon Form takes all 3 energy and the fight is lost. The play-out player played both Defends and no Demon Form.
- A large hit every turn and one Block card, Unmovable in hand. Deck: Unmovable, Defend, Strike, Strike, Strike; 20 HP. Enemy: 48 HP, 14 every turn. Unmovable, then Defend, on turn 1: 10 Block, 4 through. Turns 2 to 4: Defend for 10 and two Strikes: 4 through each. Turn 5: two Strikes kill: 16 HP lost, won on 4 HP. Defend first and Unmovable after it: that Defend gives 5, 9 through, 21 lost in all, dead. Unmovable a turn later: 9 + 4 + 4 + 4 = 21, dead. No Unmovable: at least 23 through before 48 damage is dealt, dead. The play-out player played Unmovable and then the Defend on turn 1.
- A kill that takes two turns (no Power: the same question for attacks). Hand: Strike, Strike, Strike, Defend, Defend, and the same five cards next turn; 80 HP. Enemy: 36 HP, 10 now and 99 next turn. Three Strikes now and three next turn are 36: dead before the 99, 10 through, won on 70 HP. Any Defend on turn 1 leaves the enemy at least 6 HP when the 99 lands, dead. The play-out player played the three Strikes. The one-turn player blocked the 10 and lost on turn 2.
- The play-out player found each line at its fast and normal settings; the Barricade, Unmovable and two-turn positions were also run at its strong setting (192 play-outs a decision), with the same result. The one-turn player was run on the Barricade and two-turn positions only.

### Limits
- Scripts, generated decks, single fights. One copy of the Power went into a deck of random picks (about 15 cards before an elite, 17 before a boss) that was not built for it. No relics but Burning Blood, ascension 0, Overgrowth only.
- The card table used the play-out player's fast setting, which is not shown to beat the one-turn player against bosses (97 wins against 96 of 160 on other fights; 145 against 135 of 160 elites). Its normal setting, the one used for the 780 fights, was measured on 260 bosses and 260 elites from three seeds (two of them among the 780 fights): it won 176 bosses against the one-turn player's 160, and 239 elites against 220. On one of the three seeds it did not win more bosses (49 against 50 of 80).
- The player inside the play-outs is a one-turn search with a small budget. A Power that needs a better follow-up than that is still left in the hand or undervalued. Barricade and Dark Embrace may be such cards.
- Some decks already held a copy of the Power before one was added: 33 to 46 of the 300 for Feel No Pain, Inflame, Inferno, Juggling, Rupture, Stone Armor and Vicious, 0 to 19 for the others. The turn and fight counts are for any copy of the card.
- The kind of turn is the damage at the start of that turn only. It says nothing of the turns after it, and no count here says whether a play was right.
- Stampede was not in the card table. In the 780 fights the plain card could be played on few turns: the one-turn player played it on 17 of 42, the play-out player on 24 of 34. Upgraded Powers were not measured card by card.
- 300 fights a Power give Worth an interval of about 1.5 HP. Differences under 2 HP between two Powers are not shown.

## Lessons
(none yet)
