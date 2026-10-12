# Ironclad pick values (measured)
Measured worth of adding, upgrading or removing each Ironclad card in simulated act 1 fights, and which of the numbers not to trust.

## Facts (simulator, game v0.107.1)

### Status: evidence, not rules
- These numbers come from single fights. They have not been confirmed over whole runs, where HP, potions and the deck carry from fight to fight.
- Weigh them with everything else you know about the run. Do not take a rule from them, least of all for removals: "remove a Defend before a Strike" is what single elite and boss fights show, and whether it holds over a run is not known yet.
- This note goes when the run simulator has re-measured them.

### What was measured and how to read it
- The arena simulator played 300 hallway, 300 elite and 300 boss fights of act 1 with generated decks (see Limits), each as it was and again with one change: one copy of a card added, one card upgraded, or one Strike or Defend removed.
- The player was a script that searches one turn ahead and plans nothing across turns. Unchanged, it won every hallway fight, 84% of the elite fights and 60% of the boss fights.
- Win = change in win chance against elites and bosses, in percentage points. HP = change in HP left after those fights (a loss counts as 0, a win includes the 6 that Burning Blood heals). Hall = change in HP left after a hallway fight. Up = HP an upgraded copy is worth over a plain one, against elites and bosses. Win, HP and Hall compare with the same fight with one more plain Strike in the card's place; one more Strike is worth about nothing and one more Defend costs (table under Removals and upgrades).
- Each card rests on 280 elite and boss fights and 140 hallway fights: the 95% interval is about 4.5 either way for Win, 1.4 for HP, 1.1 for Hall and Up. A second run on fresh fights repeated 27 of the additions: same order (rank agreement 0.96), HP 1.3 higher on average. So trust the order and gaps of 3 HP or more between two cards, not gaps under 2 HP.
- A different player measured 29 of the additions again: the 18 cards the first run could not judge, 10 control cards (cards whose first number was trusted, measured again to show what the change of player alone does to a number) and Stone Armor (a Power the first run did not mark). It played the first 150 elite and 150 boss fights of the same set (the first run's 280 and 20 more), each with one more Strike and again with the card in the Strike's place.
- This play-out player lists ways to play the turn, plays the rest of the fight out on copies in which everything hidden is reshuffled (24 play-outs a decision, its fast setting), and takes the turn whose fights ended best. It uses nothing a player could not know. With one more Strike it won 231 of the 300 fights and kept 26.0 HP; the script of the first run (the one-turn player below), on the same fights again, 215 and 24.0. The arena calls the two Lookahead and Rollout.
- Its figures are on a scale of their own (the control cards came out about 1.1 HP higher with it), so they stand in a table of their own under "The marked cards, measured again" and not in the tier tables. On other fights this fast setting was ahead of the one-turn player against elites (145 wins against 135 of 160) and not shown to be ahead against bosses (97 against 96 of 160).

### Cards by tier
- All figures are from the first run, with the one-turn player. Sorted by HP, the steadiest column; read each table across.
- Two marks point to "The marked cards, measured again". (low) = the play-out player got clearly more HP out of the card than this row says: against the other cards it stood higher by +1.9 ±1.3 HP (Demon Form), +1.5 ±1.4 (Aggression) and +2.5 ±1.5 (Unmovable). (?) = neither player plays the card often enough to show what it is worth. 11 more cards carried a (?) until the play-out player measured them; it did not move them against the control cards, so they are unmarked now.
- All cards share the same Strike fights, so every HP figure can be about 1 HP off in the same direction. The first tier is above a Strike even then.
- Break and Corruption are Ancient cards (relics give them, rewards never offer them). Bash and Defend are listed for comparison.

### Clearly good (27): the whole HP interval lies above +1
| Card | Win | HP | Hall | Up | Card | Win | HP | Hall | Up |
|---|---|---|---|---|---|---|---|---|---|
| Break | +21.8 | +13.5 | +7.3 | +4.3 | Primal Force | +18.2 | +10.3 | +6.9 | +4.5 |
| Crimson Mantle | +20.4 | +8.7 | +1.2 | +3.6 | Unrelenting | +14.3 | +6.1 | +3.7 | +3.8 |
| Dominate | +15.4 | +5.9 | +0.3 | +4.1 | Anger | +13.9 | +5.4 | +3.4 | +2.5 |
| Stoke | +12.5 | +5.3 | +0.7 | +5.8 | Fight Me | +15.0 | +5.2 | +1.7 | +4.2 |
| Thrash | +11.8 | +4.6 | +1.7 | +2.7 | Stampede | +10.7 | +4.6 | -0.8 | +2.8 |
| Bully | +13.2 | +4.5 | +3.2 | +0.7 | Stomp | +9.3 | +4.3 | +3.7 | +1.0 |
| Infernal Blade | +10.7 | +4.3 | +1.8 | +2.2 | Inferno | +16.1 | +4.0 | -0.6 | +2.7 |
| Demon Form (low) | +13.2 | +3.9 | -1.0 | +3.7 | Offering | +9.6 | +3.7 | +0.6 | +2.9 |
| Conflagration | +8.9 | +3.6 | +3.1 | +2.2 | Tear Asunder | +12.9 | +3.3 | +0.3 | +3.2 |
| Dismantle | +9.3 | +3.2 | +3.4 | +2.5 | Rampage | +9.3 | +3.2 | +2.2 | +1.3 |
| Inflame | +11.1 | +3.1 | +0.2 | +2.7 | Uppercut | +7.9 | +3.1 | +2.6 | +4.5 |
| Twin Strike | +8.6 | +3.1 | +2.9 | +2.7 | Bludgeon | +9.6 | +3.1 | +3.5 | +5.3 |
| Spite | +9.3 | +2.9 | +2.6 | +1.0 | Aggression (low) | +11.4 | +2.9 | +0.3 | +2.1 |
| Taunt | +5.7 | +2.6 | +2.1 | +2.8 | | | | | |

### Good (18): the whole HP interval lies above 0
| Card | Win | HP | Hall | Up | Card | Win | HP | Hall | Up |
|---|---|---|---|---|---|---|---|---|---|
| Battle Trance | +7.1 | +2.5 | +1.1 | +2.1 | Colossus | +7.9 | +2.4 | +0.2 | +1.2 |
| Hemokinesis | +9.6 | +2.4 | +2.3 | +3.6 | Brand | +11.1 | +2.3 | +0.3 | +5.4 |
| Rage | +5.7 | +2.3 | +2.5 | +3.3 | Juggernaut | +6.8 | +2.1 | -0.6 | +2.2 |
| Perfected Strike | +3.9 | +2.0 | +3.0 | +3.7 | Setup Strike | +5.7 | +1.9 | +2.3 | +2.0 |
| Pommel Strike | +6.1 | +1.9 | +2.4 | +2.3 | Sword Boomerang | +6.8 | +1.9 | +1.5 | +1.5 |
| Armaments | +5.7 | +1.9 | +1.8 | +3.6 | Hellraiser | +5.4 | +1.8 | -2.3 | +2.7 |
| Mangle | +7.9 | +1.8 | +1.8 | +3.7 | Cinder | +7.5 | +1.7 | +2.3 | +3.2 |
| Bloodletting | +4.6 | +1.5 | -0.1 | +0.8 | Breakthrough | +7.1 | +1.4 | +2.4 | +2.7 |
| Molten Fist | +4.6 | +1.4 | +2.2 | +1.8 | Iron Wave | +5.7 | +1.4 | +2.4 | +3.9 |

### About the same as a Strike (34): the HP interval holds 0
| Card | Win | HP | Hall | Up | Card | Win | HP | Hall | Up |
|---|---|---|---|---|---|---|---|---|---|
| Pyre | +4.3 | +1.3 | -0.8 | +3.2 | Whirlwind | +2.5 | +1.2 | +1.6 | +4.3 |
| Second Wind | +5.7 | +1.2 | +0.5 | +1.2 | Tremble | +3.6 | +1.2 | -0.3 | -0.3 |
| Headbutt | +5.0 | +1.2 | +1.7 | +2.2 | Thunderclap | +4.3 | +1.2 | +1.0 | +2.7 |
| Flame Barrier | +7.1 | +1.1 | +2.8 | +4.4 | Fiend Fire | +0.0 | +1.1 | +3.7 | +4.7 |
| Ashen Strike | +3.6 | +1.0 | +1.4 | +0.5 | Impervious | +2.1 | +0.9 | +2.1 | +0.2 |
| Howl from Beyond (?) | +3.6 | +0.9 | +1.3 | +2.8 | Pillage | +3.6 | +0.8 | +1.3 | +1.3 |
| One-Two Punch | +5.0 | +0.7 | +0.4 | +2.7 | Shrug It Off | +1.8 | +0.6 | +1.8 | +1.4 |
| Cascade | +0.0 | +0.5 | +0.5 | +5.9 | Pact's End | +2.1 | +0.5 | -0.9 | -0.3 |
| Feed | +1.8 | +0.5 | +3.2 | +1.0 | Unmovable (low) | +4.3 | +0.5 | +1.0 | +5.2 |
| Corruption | +3.9 | +0.4 | -0.1 | +4.2 | Evil Eye | +2.5 | +0.1 | +1.9 | +2.7 |
| Not Yet | +1.8 | +0.0 | +2.0 | +2.7 | Expect a Fight | -0.4 | -0.1 | +0.3 | +2.8 |
| Blood Wall | +1.4 | -0.1 | +0.8 | +1.4 | Rupture | +3.2 | -0.3 | -1.6 | +1.2 |
| Body Slam | -1.4 | -0.3 | +0.9 | +4.0 | Bash | -1.1 | -0.5 | +0.1 | +2.8 |
| Stone Armor | -1.1 | -0.6 | +1.7 | +4.6 | Havoc | +2.1 | -0.7 | +0.1 | +4.9 |
| True Grit | +1.1 | -0.7 | +1.4 | +2.5 | Juggling | +2.1 | -0.7 | -1.0 | +0.3 |
| Forgotten Ritual (?) | -0.7 | -0.8 | -0.5 | +0.3 | Cruelty | -1.4 | -1.3 | -0.9 | +2.2 |
| Burning Pact | +0.7 | -1.4 | -0.5 | +0.1 | Drum of Battle | -1.4 | -1.4 | -0.7 | +0.7 |

### Worse than nothing (5): the whole HP interval lies below 0
| Card | Win | HP | Hall | Up | Card | Win | HP | Hall | Up |
|---|---|---|---|---|---|---|---|---|---|
| Feel No Pain | -1.4 | -1.5 | -0.6 | +0.4 | Barricade (?) | -1.8 | -1.7 | -0.7 | +0.4 |
| Defend | -3.6 | -2.4 | -0.7 | +2.4 | Dark Embrace (?) | -5.4 | -2.7 | -1.0 | -0.2 |
| Vicious | -5.4 | -3.0 | -2.2 | +1.4 | | | | | |

- One more Strike is worth about nothing, so below a Strike is below nothing. Only Defend is shown to be worse than nothing directly (Add a Defend, below). The other four were measured again: with the play-out player Vicious (-2.0 ±1.3) and Dark Embrace (-2.4 ±1.2) are below a Strike again, Feel No Pain (-0.5 ±1.4) and Barricade (-0.8 ±1.5) are not told apart from one. Barricade and Dark Embrace are (?) cards: both players mostly leave them in the hand, so their place in this tier says nothing about a deck that uses them.
- A second script that follows fixed rules without searching (600 fights per card) also has the whole HP interval above +1 for 13 of the 27: Break, Primal Force, Thrash, Anger, Unrelenting, Crimson Mantle, Bludgeon, Inferno, Spite, Rampage, Conflagration, Bully, Twin Strike.

### Removals and upgrades
| One change to the deck | Win | HP | Hall | Fights |
|---|---|---|---|---|
| Upgrade a card that is not a Strike or Defend (average) | +5.1 [+3.3, +6.8] | +2.9 [+2.4, +3.5] | +1.0 [+0.6, +1.5] | 600 / 300 |
| Upgrade Bash | +4.4 [+1.5, +7.4] | +3.0 [+2.1, +4.0] | +0.4 [-0.3, +1.1] | 473 / 245 |
| Upgrade a Strike | +2.8 [+0.6, +5.0] | +2.6 [+1.9, +3.3] | +1.5 [+0.9, +2.1] | 600 / 300 |
| Upgrade a Defend | +1.2 [-1.0, +3.4] | +2.1 [+1.4, +2.7] | +1.6 [+1.1, +2.1] | 600 / 300 |
| Add the average reward card, against one more Strike | +6.0 [+2.9, +9.1] | +1.9 [+0.9, +2.8] | +1.2 [+0.5, +1.9] | 280 / 140 |
| Remove a Defend | +2.5 [+0.2, +4.8] | +1.8 [+1.1, +2.4] | -0.1 [-0.7, +0.5] | 1,200 / 599 |
| Remove a Strike | -0.8 [-3.1, +1.4] | +0.7 [-0.1, +1.4] | +0.1 [-0.6, +0.7] | 1,200 / 599 |
| Add a Strike | -0.9 [-3.2, +1.3] | +0.4 [-0.4, +1.1] | -0.7 [-1.4, -0.1] | 1,200 / 599 |
| Add a Defend | -4.2 [-6.6, -1.9] | -1.2 [-2.0, -0.5] | -1.3 [-2.0, -0.6] | 1,200 / 599 |

- Each figure with its 95% interval. Fights: elite and boss / hallway. The add and remove lines are against the unchanged deck, both runs pooled; the other lines are from the first run.
- Removing a Strike is not worth more than the average card: it is not told apart from nothing, while the average reward card is HP +1.9 and Win +6.0 above a Strike. The rule-following script lost by it: HP -0.9 [-1.5, -0.4], Win -2.7 [-4.7, -0.6].
- Removing a Defend is worth about the HP of the average reward card (+1.8 against +1.9), with a smaller Win (+2.5 against +6.0, intervals overlapping).
- In hallway fights neither removal changes anything. One more Strike costs about 0.7 HP there, so a Hall figure under +0.7 means the card did not pay for its place in those fights.
- An upgrade of a card that is not a Strike or Defend is worth about what adding the average reward card is worth (HP +2.9 against +1.9, Win +5.1 against +6.0, intervals overlapping). Upgrading Bash, a Strike or a Defend is worth 2 to 3 HP each; for the Defend the Win is not told apart from 0.
- Largest Up: Cascade +5.9, Stoke +5.8, Brand +5.4, Bludgeon +5.3, Unmovable +5.2, Havoc +4.9, Fiend Fire +4.7, Stone Armor +4.6, Uppercut +4.5, Primal Force +4.5. The average reward card: +2.5 [+2.3, +2.7], Win +4.5 [+3.7, +5.3].
- Up not told apart from 0 (the interval holds 0, 14 cards): Stomp, Bloodletting, Drum of Battle, Bully, Ashen Strike, Feel No Pain, Barricade, Juggling, Forgotten Ritual, Impervious, Burning Pact, Dark Embrace, Tremble, Pact's End. No upgrade is shown to hurt.

### The marked cards, measured again
- The first run marked 18 cards (?): cards that need a plan the one-turn player lacks. It left 8 of them in the hand on more than 70% of the turns it held them. It sees one turn of a Power that pays over many. And it plays a Power it has no rule for whether or not the deck can use it: in a traced fight it played Vicious on turn 1 over a Strike, in a deck whose only Vulnerable was Bash.
- The play-out player measured these 18 again, with 10 control cards and Stone Armor. Every figure in the table below rests on the same 300 elite and boss fights for both players.
- HP and Win are against one more Strike played by the same player; ± is the 95% interval. "One-turn again" is the one-turn player on these 300 fights: it repeats the tier tables to within 0.6 HP for every card.
- The two HP columns are not on one scale. The 10 controls came out 1.1 HP higher on average with the play-out player (±1.2), so read every card as gaining about that much from the player alone. Compare a play-out figure with other play-out figures, never one to one with a tier table.
- Moved = the card's change from the one-turn to the play-out player minus the controls' average change, fight by fight: how far the card moved against the other cards.
- Played = the share of the turns the card was in hand and affordable on which it was played: one-turn player, then play-out player.

| Card | One-turn again, HP | Play-out, HP | Play-out, Win | Moved | Played | Reading |
|---|---|---|---|---|---|---|
| Break | +13.8 ±1.7 | +16.9 ±1.7 | +18.3 ±4.4 | +1.9 ±1.2 | 96%, 96% | control |
| Primal Force | +10.4 ±1.8 | +11.8 ±1.7 | +17.7 ±4.4 | +0.3 ±1.2 | 70%, 74% | control |
| Crimson Mantle | +8.8 ±1.6 | +11.0 ±1.7 | +16.7 ±4.5 | +1.1 ±1.3 | 71%, 82% | first number stands |
| Unrelenting | +6.3 ±1.4 | +7.3 ±1.5 | +10.7 ±4.4 | -0.1 ±1.1 | 89%, 87% | control |
| Demon Form | +4.0 ±1.6 | +7.1 ±1.6 | +13.7 ±4.2 | +1.9 ±1.3 | 49%, 56% | more than the first number says |
| Inferno | +4.2 ±1.5 | +6.5 ±1.7 | +12.0 ±4.4 | +1.1 ±1.2 | 79%, 74% | first number stands |
| Anger | +5.7 ±1.4 | +6.2 ±1.5 | +8.0 ±4.3 | -0.6 ±1.1 | 91%, 89% | control |
| Aggression | +2.8 ±1.4 | +5.5 ±1.6 | +10.0 ±4.5 | +1.5 ±1.4 | 47%, 67% | more than the first number says |
| Inflame | +3.2 ±1.3 | +5.2 ±1.4 | +10.7 ±4.1 | +0.9 ±1.2 | 92%, 80% | control |
| Juggernaut | +2.7 ±1.5 | +4.5 ±1.7 | +8.0 ±3.9 | +0.6 ±1.4 | 28%, 58% | first number stands |
| Unmovable | +0.5 ±1.3 | +4.1 ±1.6 | +6.7 ±4.3 | +2.5 ±1.5 | 28%, 57% | more than the first number says |
| Twin Strike | +3.3 ±1.1 | +3.7 ±1.2 | +6.7 ±3.6 | -0.8 ±1.0 | 83%, 80% | control |
| Pyre | +1.5 ±1.2 | +3.0 ±1.5 | +7.0 ±4.5 | +0.3 ±1.4 | 26%, 50% | first number stands |
| Corruption | +0.8 ±1.5 | +2.9 ±1.6 | +3.3 ±4.3 | +0.9 ±1.3 | 22%, 46% | first number stands |
| Hellraiser | +1.7 ±1.5 | +2.7 ±1.6 | +5.7 ±4.2 | -0.2 ±1.3 | 63%, 60% | first number stands |
| Bash | -0.3 ±1.3 | +1.9 ±1.4 | +2.0 ±4.3 | +1.1 ±1.1 | 68%, 60% | control |
| Pommel Strike | +1.9 ±1.1 | +1.8 ±1.3 | +3.7 ±3.8 | -1.2 ±1.1 | 81%, 75% | control |
| Shrug It Off | +0.6 ±1.3 | +1.3 ±1.3 | +2.7 ±3.8 | -0.4 ±1.3 | 60%, 64% | control |
| Stone Armor | -0.6 ±1.3 | +1.3 ±1.3 | +0.0 ±3.7 | +0.7 ±1.3 | 69%, 60% | never marked, not a control |
| Howl from Beyond | +1.0 ±1.3 | +1.0 ±1.3 | +1.3 ±4.2 | -1.1 ±1.4 | 29%, 30% | still not shown |
| Rupture | -0.3 ±1.3 | -0.1 ±1.4 | +0.0 ±4.4 | -1.0 ±1.4 | 53%, 47% | first number stands |
| Feel No Pain | -1.5 ±1.3 | -0.5 ±1.4 | -2.3 ±4.3 | -0.1 ±1.3 | 55%, 44% | first number stands |
| Juggling | -0.4 ±1.3 | -0.5 ±1.5 | +0.7 ±3.8 | -1.2 ±1.6 | 49%, 40% | first number stands |
| Barricade | -1.8 ±1.4 | -0.8 ±1.5 | -1.3 ±4.2 | -0.1 ±1.3 | 8%, 13% | still not shown |
| Cruelty | -1.2 ±1.4 | -0.8 ±1.3 | +1.7 ±4.1 | -0.8 ±1.4 | 53%, 49% | first number stands |
| Forgotten Ritual | -0.8 ±1.3 | -0.9 ±1.4 | -3.0 ±4.3 | -1.2 ±1.3 | 17%, 27% | still not shown |
| Vicious | -2.7 ±1.3 | -2.0 ±1.3 | -6.0 ±4.1 | -0.4 ±1.5 | 49%, 35% | first number stands |
| Defend | -2.3 ±1.3 | -2.2 ±1.4 | -5.0 ±4.1 | -1.0 ±1.1 | 42%, 46% | control |
| Dark Embrace | -2.3 ±1.3 | -2.4 ±1.2 | -5.3 ±4.3 | -1.2 ±1.3 | 17%, 16% | still not shown |

- The controls kept their order: rank agreement 0.96 between the tier tables and the play-out player. Bash rose from below Pommel Strike and Shrug It Off to level with them. Break moved up by more than the other controls (+1.9 ±1.2) and Pommel Strike by less (-1.2 ±1.1).
- More than the first number says (3 cards, marked (low)): each moved up by more than its own interval and by more than the controls.
- Demon Form: tier table +3.9. On these fights +4.0 with the one-turn player and +7.1 ±1.6 with the play-out player; moved +1.9 ±1.3. Played on 49% and 56% of the turns it could be.
- Aggression: tier table +2.9. On these fights +2.8 (one-turn) and +5.5 ±1.6 (play-out); moved +1.5 ±1.4, the least sure of the three (its interval ends at +0.1). Played on 47% and 67%.
- Unmovable: tier table +0.5. On these fights +0.5 (one-turn) and +4.1 ±1.6 (play-out); moved +2.5 ±1.5. Played on 28% and 57%.
- For these three the gain is in HP left. Their Win is not shown to differ between the players on these fights: Demon Form +13.0 with the one-turn player and +13.7 with the play-out player (change +0.7 ±5.2), Aggression +10.3 and +10.0 (-0.3 ±5.5), Unmovable +3.3 and +6.7 (+3.3 ±5.8).
- The first number stands (11 cards, unmarked now): Crimson Mantle, Inferno, Juggernaut, Pyre, Corruption, Hellraiser, Rupture, Feel No Pain, Juggling, Cruelty, Vicious. The play-out player did not move them against the controls: every Moved interval holds 0. No card moved down. That is two scripts agreeing on where the card stands among the other cards, in decks that were not built for it (last bullet of this section).
- Among those 11, Crimson Mantle, Inferno and Corruption rose by more than their own interval (+2.2 ±1.8, +2.3 ±1.7, +2.1 ±1.7 before the controls are taken off) but not by more than the controls.
- Juggernaut, Pyre and Corruption were played about twice as often by the play-out player (28% to 58%, 26% to 50%, 22% to 46%). Their HP rose with the controls, not clearly beyond them. Pyre (+3.0 ±1.5) and Corruption (+2.9 ±1.6) are above a Strike with the play-out player, which the first run could not say for them.
- Vicious is below a Strike with both players (-2.7 ±1.3 one-turn, -2.0 ±1.3 play-out), though the play-out player plays it less (49% to 35%).
- Still not shown (4 cards, marked (?)): neither player plays them often (on 8% to 30% of the turns they could be), so both numbers are those of a card that mostly stays in the hand. They say nothing about a deck that uses the card: judge these four yourself from the card text and your deck.
- Howl from Beyond: tier table +0.9, play-out player +1.0 ±1.3. Played on 29% and 30% of the turns it could be.
- Forgotten Ritual: tier table -0.8, play-out player -0.9 ±1.4. Played on 17% and 27%.
- Barricade: tier table -1.7, play-out player -0.8 ±1.5. Played on 8% and 13%. It is no longer shown to be worse than a Strike, and that is all the play-out player adds for it.
- Dark Embrace: tier table -2.7, play-out player -2.4 ±1.2. Played on 17% and 16%.
- On what kind of turn the two players played each Power is in `ironclad-power-timing.md`.
- Not marked, played by the one-turn player in about a third of the turns held (first run, not measured again): Drum of Battle 31%, Pact's End 33%, Burning Pact 35%, Second Wind 35%, Blood Wall 36%, Expect a Fight 38%. Read with care.
- Marked or not, and with either player, every card that needs partners went into decks that were not built for it, so its number is the card in an average deck and not in its own: Exhaust cards (Evil Eye, Pact's End, Ashen Strike, Second Wind, Forgotten Ritual, Corruption, Dark Embrace, Feel No Pain), Block cards (Body Slam, Barricade, Juggernaut, Unmovable), Vulnerable cards (Bully, Dismantle, Colossus, Vicious, Cruelty), HP-loss cards (Spite, Tear Asunder, Rupture, Inferno), Strike cards (Perfected Strike, Hellraiser).

### Limits
- No relics but Burning Blood, ascension 0, every fight on its own: what a relic or a longer run does for a card is not counted.
- Decks of random picks: the simulated run took any offered card equally often, so no deck was built around a plan. One copy went into a deck of 14 to 15 cards on average (hallway, elite) or 17 (boss).
- Overgrowth only: its 12 regular hallway fights (floors 5 to 15, none of the weak fights that open the act), its 3 elites and its 3 bosses. Nothing here is about Underdocks or acts 2 and 3.
- The play-out player measured only the 29 additions of its table, and only against elites and bosses. Hall, Up, the removals and the other 55 cards rest on the one-turn player alone.
- It ran at its fast setting, which is not shown to beat the one-turn player against bosses. A stronger setting may value the setup cards differently again.
- The player inside its play-outs is a one-turn search with a small budget. A card that needs a better follow-up than that is still undervalued. Barricade and Dark Embrace may be such cards.
- 300 fights a card give the play-out figures intervals of about 1.5 HP. Differences under 2 HP between two cards are not shown.
- Both players are scripts, and the decks are the same generated decks: the play-out figures change the player, not the deck.

## Lessons
(none yet)
