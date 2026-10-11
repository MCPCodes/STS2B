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

### Cards by tier
- All figures are from the first run. Sorted by HP, the steadiest column; read each table across. (?) marks a number that says more about the script than about the card: see "Do not trust the number for these".
- All cards share the same Strike fights, so every HP figure can be about 1 HP off in the same direction. The first tier is above a Strike even then.
- Break and Corruption are Ancient cards (relics give them, rewards never offer them). Bash and Defend are listed for comparison.

### Clearly good (27): the whole HP interval lies above +1
| Card | Win | HP | Hall | Up | Card | Win | HP | Hall | Up |
|---|---|---|---|---|---|---|---|---|---|
| Break | +21.8 | +13.5 | +7.3 | +4.3 | Primal Force | +18.2 | +10.3 | +6.9 | +4.5 |
| Crimson Mantle (?) | +20.4 | +8.7 | +1.2 | +3.6 | Unrelenting | +14.3 | +6.1 | +3.7 | +3.8 |
| Dominate | +15.4 | +5.9 | +0.3 | +4.1 | Anger | +13.9 | +5.4 | +3.4 | +2.5 |
| Stoke | +12.5 | +5.3 | +0.7 | +5.8 | Fight Me | +15.0 | +5.2 | +1.7 | +4.2 |
| Thrash | +11.8 | +4.6 | +1.7 | +2.7 | Stampede | +10.7 | +4.6 | -0.8 | +2.8 |
| Bully | +13.2 | +4.5 | +3.2 | +0.7 | Stomp | +9.3 | +4.3 | +3.7 | +1.0 |
| Infernal Blade | +10.7 | +4.3 | +1.8 | +2.2 | Inferno (?) | +16.1 | +4.0 | -0.6 | +2.7 |
| Demon Form (?) | +13.2 | +3.9 | -1.0 | +3.7 | Offering | +9.6 | +3.7 | +0.6 | +2.9 |
| Conflagration | +8.9 | +3.6 | +3.1 | +2.2 | Tear Asunder | +12.9 | +3.3 | +0.3 | +3.2 |
| Dismantle | +9.3 | +3.2 | +3.4 | +2.5 | Rampage | +9.3 | +3.2 | +2.2 | +1.3 |
| Inflame | +11.1 | +3.1 | +0.2 | +2.7 | Uppercut | +7.9 | +3.1 | +2.6 | +4.5 |
| Twin Strike | +8.6 | +3.1 | +2.9 | +2.7 | Bludgeon | +9.6 | +3.1 | +3.5 | +5.3 |
| Spite | +9.3 | +2.9 | +2.6 | +1.0 | Aggression (?) | +11.4 | +2.9 | +0.3 | +2.1 |
| Taunt | +5.7 | +2.6 | +2.1 | +2.8 | | | | | |

### Good (18): the whole HP interval lies above 0
| Card | Win | HP | Hall | Up | Card | Win | HP | Hall | Up |
|---|---|---|---|---|---|---|---|---|---|
| Battle Trance | +7.1 | +2.5 | +1.1 | +2.1 | Colossus | +7.9 | +2.4 | +0.2 | +1.2 |
| Hemokinesis | +9.6 | +2.4 | +2.3 | +3.6 | Brand | +11.1 | +2.3 | +0.3 | +5.4 |
| Rage | +5.7 | +2.3 | +2.5 | +3.3 | Juggernaut (?) | +6.8 | +2.1 | -0.6 | +2.2 |
| Perfected Strike | +3.9 | +2.0 | +3.0 | +3.7 | Setup Strike | +5.7 | +1.9 | +2.3 | +2.0 |
| Pommel Strike | +6.1 | +1.9 | +2.4 | +2.3 | Sword Boomerang | +6.8 | +1.9 | +1.5 | +1.5 |
| Armaments | +5.7 | +1.9 | +1.8 | +3.6 | Hellraiser (?) | +5.4 | +1.8 | -2.3 | +2.7 |
| Mangle | +7.9 | +1.8 | +1.8 | +3.7 | Cinder | +7.5 | +1.7 | +2.3 | +3.2 |
| Bloodletting | +4.6 | +1.5 | -0.1 | +0.8 | Breakthrough | +7.1 | +1.4 | +2.4 | +2.7 |
| Molten Fist | +4.6 | +1.4 | +2.2 | +1.8 | Iron Wave | +5.7 | +1.4 | +2.4 | +3.9 |

### About the same as a Strike (34): the HP interval holds 0
| Card | Win | HP | Hall | Up | Card | Win | HP | Hall | Up |
|---|---|---|---|---|---|---|---|---|---|
| Pyre (?) | +4.3 | +1.3 | -0.8 | +3.2 | Whirlwind | +2.5 | +1.2 | +1.6 | +4.3 |
| Second Wind | +5.7 | +1.2 | +0.5 | +1.2 | Tremble | +3.6 | +1.2 | -0.3 | -0.3 |
| Headbutt | +5.0 | +1.2 | +1.7 | +2.2 | Thunderclap | +4.3 | +1.2 | +1.0 | +2.7 |
| Flame Barrier | +7.1 | +1.1 | +2.8 | +4.4 | Fiend Fire | +0.0 | +1.1 | +3.7 | +4.7 |
| Ashen Strike | +3.6 | +1.0 | +1.4 | +0.5 | Impervious | +2.1 | +0.9 | +2.1 | +0.2 |
| Howl from Beyond (?) | +3.6 | +0.9 | +1.3 | +2.8 | Pillage | +3.6 | +0.8 | +1.3 | +1.3 |
| One-Two Punch | +5.0 | +0.7 | +0.4 | +2.7 | Shrug It Off | +1.8 | +0.6 | +1.8 | +1.4 |
| Cascade | +0.0 | +0.5 | +0.5 | +5.9 | Pact's End | +2.1 | +0.5 | -0.9 | -0.3 |
| Feed | +1.8 | +0.5 | +3.2 | +1.0 | Unmovable (?) | +4.3 | +0.5 | +1.0 | +5.2 |
| Corruption (?) | +3.9 | +0.4 | -0.1 | +4.2 | Evil Eye | +2.5 | +0.1 | +1.9 | +2.7 |
| Not Yet | +1.8 | +0.0 | +2.0 | +2.7 | Expect a Fight | -0.4 | -0.1 | +0.3 | +2.8 |
| Blood Wall | +1.4 | -0.1 | +0.8 | +1.4 | Rupture (?) | +3.2 | -0.3 | -1.6 | +1.2 |
| Body Slam | -1.4 | -0.3 | +0.9 | +4.0 | Bash | -1.1 | -0.5 | +0.1 | +2.8 |
| Stone Armor | -1.1 | -0.6 | +1.7 | +4.6 | Havoc | +2.1 | -0.7 | +0.1 | +4.9 |
| True Grit | +1.1 | -0.7 | +1.4 | +2.5 | Juggling (?) | +2.1 | -0.7 | -1.0 | +0.3 |
| Forgotten Ritual (?) | -0.7 | -0.8 | -0.5 | +0.3 | Cruelty (?) | -1.4 | -1.3 | -0.9 | +2.2 |
| Burning Pact | +0.7 | -1.4 | -0.5 | +0.1 | Drum of Battle | -1.4 | -1.4 | -0.7 | +0.7 |

### Worse than nothing (5): the whole HP interval lies below 0
| Card | Win | HP | Hall | Up | Card | Win | HP | Hall | Up |
|---|---|---|---|---|---|---|---|---|---|
| Feel No Pain (?) | -1.4 | -1.5 | -0.6 | +0.4 | Barricade (?) | -1.8 | -1.7 | -0.7 | +0.4 |
| Defend | -3.6 | -2.4 | -0.7 | +2.4 | Dark Embrace (?) | -5.4 | -2.7 | -1.0 | -0.2 |
| Vicious (?) | -5.4 | -3.0 | -2.2 | +1.4 | | | | | |

- One more Strike is worth about nothing, so below a Strike is below nothing. Only Defend is shown to be worse than nothing directly (Add a Defend, below); the other four are (?) cards.
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

### Do not trust the number for these
- The 18 cards marked (?) need a plan the scripted player lacks. Their numbers, Up included, say what a player without a plan got out of them, so judge them yourself from your deck. They fall into three groups.
- Left in the hand: the script played these in under 30% of the turns it held them: Barricade 8%, Dark Embrace 16%, Forgotten Ritual 17%, Corruption 21%, Pyre 24%, Juggernaut 26%, Unmovable 27%, Howl from Beyond 27%. Barricade's -1.7 is about what a card that is never played costs.
- Powers that pay over many turns, played by a script that sees one turn: Crimson Mantle, Inferno, Demon Form (played in 47% of the turns held), Aggression (45%), Hellraiser, Rupture, Juggling, Cruelty, Feel No Pain.
- Energy thrown away: the script plays a Power it has no rule for whether or not the deck can use it. In a traced fight it played Vicious on turn 1 over a Strike, in a deck whose only Vulnerable was Bash. Vicious (-3.0) and Dark Embrace (-2.7) come out below a never-played card that way; the same habit weighs on Cruelty, Feel No Pain, Juggling and Rupture.
- Not marked, played in about a third of the turns held: Drum of Battle 31%, Pact's End 33%, Burning Pact 35%, Second Wind 35%, Blood Wall 36%, Expect a Fight 38%. Read with care.
- Marked or not, every card that needs partners went into decks that were not built for it, so its number is the card in an average deck and not in its own: Exhaust cards (Evil Eye, Pact's End, Ashen Strike, Second Wind, Forgotten Ritual, Corruption, Dark Embrace, Feel No Pain), Block cards (Body Slam, Barricade, Juggernaut, Unmovable), Vulnerable cards (Bully, Dismantle, Colossus, Vicious, Cruelty), HP-loss cards (Spite, Tear Asunder, Rupture, Inferno), Strike cards (Perfected Strike, Hellraiser).

### Limits
- No relics but Burning Blood, ascension 0, every fight on its own: what a relic or a longer run does for a card is not counted.
- Decks of random picks: the simulated run took any offered card equally often, so no deck was built around a plan. One copy went into a deck of 14 to 15 cards on average (hallway, elite) or 17 (boss).
- Overgrowth only: its 12 regular hallway fights (floors 5 to 15, none of the weak fights that open the act), its 3 elites and its 3 bosses. Nothing here is about Underdocks or acts 2 and 3.

## Lessons
(none yet)
