# Ironclad synergies (measured)
How much more an Ironclad card is worth in simulated act 1 fights when the deck already holds 2 or 4 cards of its build.

## Facts (simulator, game v0.107.1)

### Status: evidence, not rules
- These numbers come from single fights with a scripted player. They have not been confirmed over whole runs, where the deck is built pick by pick and HP and potions carry between fights.
- Weigh them with everything else you know about the run. Do not take a rule from them.
- This note goes when the run simulator has re-measured them.

### What was measured and how to read it
- A package is a build; its pieces are the cards that start it. For 7 packages the arena simulator played 584 elite and boss fights of act 1 (two separate sets, 284 and 300; 583 for two packages) with the generated deck plus 2 or 4 pieces plus the card, and again with 2 or 4 unrelated reward cards of the same rarity in the pieces' place: 169,388 fights.
- Without = HP left that the card adds over one more Strike in the deck with the unrelated cards. Synergy at 4 = how much MORE the 4 pieces add to a deck that takes this card than to a deck that takes an unrelated card instead (a Strike or a control), with its 95% interval. With 2 and With 4 = Without plus the synergy at 2 or 4.
- HP left counts a loss as 0 and a win with Burning Blood's 6. Win chance moves the same way but is coarser (a card's interval is about 2.6 points either way), so the tables give HP and the line under each names the clear win gains.
- The player was a script that searches one turn ahead and plans nothing across turns. Kind: payoff = should gain from the pieces; one more piece = a further piece on top of the 2 or 4; control = should not care (two cards the rules make losers in a deck have a kind of their own: Bludgeon as the one big hit in a Strength deck, Anger as the card that costs 0 in an Energy deck). (?) = the script played that card, or those pieces, in under 30% of the turns it held them, so the synergy was not shown.

### How to use it at a card reward
- Count the pieces of each package in your deck. Each heading below names the pieces; the starting Bash and Strikes are not pieces.
- If a package has 2 or more, look the offered cards up in its table and add the synergy to the card's HP in `ironclad-pick-values.md`: With 2 minus Without for 2 or 3 pieces, Synergy at 4 for 4 or more. A card that is not in the table gets nothing added.
- A figure marked (?) is yours to judge from the card texts and your deck: the script did not play the card or the pieces often enough to show it. A card marked (low) in `ironclad-pick-values.md` is worth more than its row there says.

### What it shows
- Payoffs gain: with 4 pieces 28 of the 36 payoff rows have the whole interval above 0 and none below (19 of 36 with 2 pieces). The mean payoff gains +1.0 HP (Strength), +1.3 (Energy), +1.6 (Vulnerable), +2.1 (Strike cards), +2.4 (Self-damage), +2.6 (Exhaust), +3.0 (Block).
- One more piece is worth less, never more: 6 of the 14 such rows are below 0 and none above. Once a build has its pieces, take payoffs, not more pieces.
- 2 pieces start a build: the mean payoff already gains +0.7 HP (Energy) to +2.3 (Self-damage) with 2. Going from 2 to 4 changes that by -0.4 (Strength) to +0.9 (Block); only Block's +0.9 [+0.2, +1.6] is clear of 0.

### Strength: pieces are Dominate, Inflame, Fight Me, Brand, Demon Form (?)
| Card | Kind | Without | With 2 | With 4 | Synergy at 4 |
|---|---|---|---|---|---|
| Conflagration | payoff | +3.7 | +7.6 | +6.9 | +3.2 [+2.3, +4.1] |
| Sword Boomerang | payoff | +1.8 | +4.4 | +4.2 | +2.4 [+1.6, +3.2] |
| Whirlwind | payoff | +1.0 | +1.7 | +1.6 | +0.6 [-0.2, +1.4] |
| Defend | control | -1.0 | -1.0 | -0.6 (?) | +0.4 [-0.5, +1.3] (?) |
| Anger | payoff | +4.0 | +5.0 | +4.2 | +0.2 [-0.7, +1.2] |
| Twin Strike | payoff | +2.4 | +3.2 | +2.6 | +0.2 [-0.6, +1.1] |
| Headbutt | control | +1.6 | +1.2 | +1.1 | -0.5 [-1.4, +0.4] |
| Setup Strike | one more piece | +1.6 | +1.4 | +0.8 | -0.8 [-1.6, +0.0] |
| Thrash | payoff | +4.0 | +3.6 | +3.2 | -0.8 [-1.7, +0.1] |
| Bludgeon | one big hit | +3.3 | +1.2 | +0.9 | -2.4 [-3.4, -1.5] |
| Fight Me | one more piece | +4.8 | +3.0 | +2.2 | -2.6 [-3.6, -1.7] |
| Inflame | one more piece | +2.9 | +1.7 | +0.2 | -2.7 [-3.6, -1.8] |

- The pieces themselves are worth +3.4 HP and +10 win points (2) and +2.5 HP and +8 points (4) over other cards in their place. Attacks that hit three times or more gain (Conflagration 4 hits, Sword Boomerang 3). Twin Strike, with one hit more than a Strike, gains +0.8 with 2 pieces and +0.2 with 4: the sign the rules give, too small to count on. One more Strength card and the 3-energy single hit lose 2.4 to 2.7: the deck has its Strength (Strength peaked at 7.2 with 2 pieces and 10.6 with 4, against 1.8 and 1.7 without) and its fights last 5.8 turns instead of 7.1. Thrash and Anger, which grow over a fight, are played less often there (1.2 times a fight instead of 1.4, 2.0 instead of 3.1) and gain nothing. Whirlwind's +0.6 was +1.6 on one set of fights and -0.3 on the other. Demon Form was played in 39% (2) and 28% (4) of the turns held.

### Vulnerable: pieces are Molten Fist, Uppercut, Tremble, Thunderclap, Taunt
| Card | Kind | Without | With 2 | With 4 | Synergy at 4 |
|---|---|---|---|---|---|
| Bully | payoff | +4.0 | +6.0 | +7.5 | +3.5 [+2.6, +4.5] |
| Dismantle | payoff | +3.7 | +5.7 | +5.6 | +2.0 [+1.1, +2.8] |
| Dominate | payoff | +5.6 | +7.2 | +7.2 | +1.6 [+0.6, +2.6] |
| Vicious | payoff | -1.2 | -0.9 | +0.1 | +1.3 [+0.4, +2.1] |
| Cruelty | payoff | -0.1 | +0.0 | +1.1 | +1.2 [+0.4, +2.1] |
| Colossus | payoff | +1.8 | +2.7 | +2.8 | +1.0 [+0.2, +1.9] |
| Molten Fist | one more piece | +2.3 | +2.4 | +2.8 | +0.5 [-0.4, +1.4] |
| Bludgeon | payoff | +3.6 | +3.5 | +3.9 | +0.3 [-0.6, +1.2] |
| Defend | control | -0.8 | -1.0 | -0.7 (?) | +0.0 [-0.8, +0.9] (?) |
| Shrug It Off | control | +1.6 | +1.7 | +1.2 | -0.4 [-1.3, +0.4] |
| Tremble | one more piece | +1.9 | -0.4 | +0.0 | -1.9 [-2.8, -1.0] |

- Bash is not counted as a piece. The pieces themselves are worth +1.2 HP (2) and +0.1 (4): more Vulnerable alone does not pay, the cards that read it do. Bully with 4 pieces: +3.5 HP and +6 win points [+3, +9]. Dominate is a top card either way (+5.6 without). Vicious and Cruelty gain 1.2 to 1.3 and still end near a Strike (+0.1, +1.1). The big single hit gains nothing (Bludgeon +0.3). One more Tremble loses 1.9: Vulnerable adds turns, not strength.

### Exhaust: pieces are Brand, Thrash, Fiend Fire, Cinder, Havoc, Stoke, True Grit, Burning Pact, Second Wind
| Card | Kind | Without | With 2 | With 4 | Synergy at 4 |
|---|---|---|---|---|---|
| Feel No Pain | payoff | -1.0 | +2.3 | +4.3 | +5.3 [+4.2, +6.4] |
| Pact's End | payoff | -0.3 | +4.2 | +5.0 | +5.2 [+4.1, +6.3] |
| Ashen Strike | payoff | +1.2 | +3.9 | +4.5 | +3.3 [+2.3, +4.2] |
| Dark Embrace | payoff | -1.7 | -1.4 (?) | +0.2 (?) | +2.0 [+1.0, +3.0] (?) |
| Drum of Battle | payoff | -0.2 | +1.0 (?) | +1.2 | +1.4 [+0.3, +2.4] |
| Forgotten Ritual | payoff | -0.1 | +0.5 (?) | +0.7 (?) | +0.8 [-0.2, +1.8] (?) |
| Evil Eye | payoff | +1.7 | +2.0 | +1.8 | +0.1 [-0.9, +1.0] |
| Twin Strike | control | +2.1 | +2.7 | +2.1 | +0.0 [-1.0, +1.0] |
| Shrug It Off | control | +1.9 | +1.4 | +1.6 | -0.3 [-1.3, +0.8] |
| True Grit | one more piece | +0.6 | +0.5 | +0.3 | -0.3 [-1.2, +0.7] |
| Second Wind | one more piece | +1.2 | +0.8 | +0.9 (?) | -0.3 [-1.2, +0.5] (?) |

- The pieces themselves cost HP against other cards in their place: -0.9 (2) and -2.3 [-3.4, -1.3] (4). They pay only with a payoff: Feel No Pain and Pact's End go from below a Strike to +4.3 and +5.0, each with +7 win points [+5, +10]; Ashen Strike +5 points [+2, +7]. Evil Eye gains nothing (+0.1). True Grit, Burning Pact and Second Wind were played in 30% to 44% of the turns held.

### Block: pieces are Crimson Mantle, Stone Armor, Rage, Shrug It Off, Flame Barrier, True Grit, Impervious, Blood Wall (?)
| Card | Kind | Without | With 2 | With 4 | Synergy at 4 |
|---|---|---|---|---|---|
| Juggernaut | payoff | +0.9 | +5.4 | +7.0 | +6.1 [+5.0, +7.2] |
| Body Slam | payoff | +0.1 | +1.6 | +2.7 | +2.6 [+1.7, +3.5] |
| Barricade | payoff | -1.1 | -0.4 (?) | +0.7 (?) | +1.8 [+0.9, +2.8] (?) |
| Unmovable | payoff | +1.7 | +3.4 (?) | +3.2 (?) | +1.5 [+0.6, +2.5] (?) |
| Inflame | control | +2.9 | +3.4 | +3.9 | +1.0 [+0.0, +1.9] |
| Twin Strike | control | +2.7 | +2.6 | +2.3 | -0.3 [-1.2, +0.6] |
| Shrug It Off | one more piece | +1.4 | +1.1 | +0.4 | -1.0 [-1.9, +0.0] |
| Impervious | one more piece | +2.0 | +1.3 | +0.9 (?) | -1.0 [-2.0, +0.0] (?) |

- The pieces themselves are worth -1.2 HP (2) and 0.0 (4) against other cards, and fights last 8.9 turns instead of 6.4 (4). Juggernaut is the largest synergy measured: +6.1 HP and +7 win points [+4, +9], although the script played it in only 30% (2) and 38% (4) of the turns it held it. Body Slam +4 win points [+2, +7]. In the long fights a Strength card gains too (Inflame +1.0). Impervious and True Grit were played in 31% to 43% of the turns held.

### Self-damage: pieces are Offering, Brand, Crimson Mantle, Hemokinesis, Breakthrough, Bloodletting, Blood Wall
| Card | Kind | Without | With 2 | With 4 | Synergy at 4 |
|---|---|---|---|---|---|
| Rupture | payoff | -0.8 | +3.0 | +2.9 | +3.7 [+2.8, +4.6] |
| Tear Asunder | payoff | +2.9 | +5.7 | +5.8 | +2.9 [+2.0, +3.8] |
| Inferno | payoff | +2.7 | +4.7 | +4.7 | +2.0 [+1.1, +2.9] |
| Spite | payoff | +3.1 | +3.7 | +4.1 | +1.0 [+0.2, +1.8] |
| Defend | control | -1.2 | -1.2 | -0.4 | +0.8 [-0.1, +1.7] |
| Twin Strike | control | +2.0 | +2.8 | +1.7 | -0.3 [-1.1, +0.5] |
| Hemokinesis | one more piece | +2.2 | +1.6 | +0.7 | -1.5 [-2.3, -0.8] |
| Bloodletting | one more piece | +1.1 | +0.0 | -0.4 | -1.5 [-2.4, -0.7] |

- The one package where the pieces pay by themselves and every payoff gains: the pieces are worth +3.0 HP and +7 win points (2), +3.6 HP and +10 points (4). Rupture goes from below a Strike (-0.8) to +2.9, with +5 win points [+2, +7]. A further HP-cost card loses 1.5 with 4 pieces. Blood Wall was played in 31% to 35% of the turns held.

### Strike cards: pieces are Twin Strike, Perfected Strike, Pommel Strike, Setup Strike, Ashen Strike
| Card | Kind | Without | With 2 | With 4 | Synergy at 4 |
|---|---|---|---|---|---|
| Hellraiser | payoff | +0.8 | +3.3 | +3.3 | +2.5 [+1.6, +3.5] |
| Perfected Strike | payoff | +2.7 | +3.2 | +4.3 | +1.7 [+0.8, +2.5] |
| Defend | control | -1.4 | -1.3 | +0.0 | +1.5 [+0.6, +2.4] |
| Twin Strike | one more piece | +2.3 | +2.8 | +2.3 | +0.0 [-0.7, +0.7] |
| Pommel Strike | one more piece | +2.5 | +2.2 | +1.8 | -0.7 [-1.5, +0.1] |
| Headbutt | control | +1.8 | +1.4 | +0.0 | -1.8 [-2.7, -0.9] |

- The basic Strikes are not counted as pieces. The pieces themselves are worth +1.2 HP (2) and +2.4 HP and +4 win points (4). Hellraiser is the payoff: +2.5, played in 51% of the turns held without the pieces and 77% with 4. Perfected Strike gains +1.7 with 4 and little with 2 (+0.6, interval holds 0). With 4 more attacks in the deck a Defend gains 1.5 and one more plain attack loses 1.8 (Headbutt): the deck has its attacks.

### Energy: pieces are Offering, Unrelenting, Bloodletting, Pyre, Expect a Fight
| Card | Kind | Without | With 2 | With 4 | Synergy at 4 |
|---|---|---|---|---|---|
| Bludgeon | payoff | +3.5 | +5.4 | +6.1 | +2.5 [+1.5, +3.6] |
| Whirlwind | payoff | +1.6 | +1.8 | +3.2 | +1.7 [+0.8, +2.6] |
| Mangle | payoff | +2.5 | +3.8 | +4.1 | +1.6 [+0.7, +2.6] |
| Cascade | payoff | +1.7 | +1.2 | +3.0 | +1.3 [+0.2, +2.5] |
| Howl from Beyond | payoff | +1.9 | +2.4 | +3.1 | +1.1 [+0.2, +2.1] |
| Twin Strike | control | +2.8 | +2.7 | +2.5 | -0.3 [-1.3, +0.8] |
| Demon Form | payoff | +2.8 | +3.5 | +2.4 | -0.3 [-1.3, +0.7] |
| Anger | costs 0 | +4.7 | +4.2 | +2.9 | -1.8 [-2.7, -0.8] |
| Bloodletting | one more piece | +2.1 | +0.1 | -0.9 | -3.0 [-4.0, -2.0] |

- The pieces themselves are worth +1.3 HP (2) and +0.1 (4). Cards that cost 3 or X gain 1.1 to 2.5 with 4 pieces, Demon Form excepted (-0.3). Bludgeon is the steady one (+1.9 with 2, +2.5 with 4): it belongs here, not in a Strength deck (-2.4). Cascade's figure did not repeat (+2.9 on one set of fights, -0.1 on the other). A card that costs 0 loses (Anger -1.8) and one more Bloodletting loses 3.0. Pyre and Expect a Fight were played in 32% to 42% of the turns held.

### What the numbers do not show
- A player who plans. The script plays a set-up card only when it pays this turn, so pieces and Powers stay in the hand (Barricade played in 8% to 9% of the turns held, Dark Embrace 18% to 19%, Blood Wall 25% to 35%, Demon Form 28% to 39%) and their figures are more likely low than high: in the 341 of 584 fights where the script did play Juggernaut with 4 Block pieces, the deck ended 12.6 HP above the same deck with a Strike; in the other fights +0.1.
- A built deck. Each deck is a generated deck of random picks (15 cards before an elite, 17 before the boss) that already holds 0.2 to 0.8 pieces, plus the pieces, plus ONE copy of the card, nothing removed; no relics but Burning Blood, ascension 0, Overgrowth elites and bosses, every fight on its own. Two payoffs together, two packages at once, hallway fights and later acts were not measured. Bosses show more than elites: the mean payoff with 4 pieces gains +4.0 against +2.0 (Block), +3.8 against +1.0 (Self-damage), +2.1 against -0.2 (Strength).
- Chance. A synergy is sure to about 0.9 HP either way: read a row whose interval is clear of 0, and gaps of 2 HP or more between rows. The two sets of fights agree as far as chance allows (a figure differs by 0.9 HP between them, typically): Juggernaut +6.6 and +5.6, Feel No Pain +4.7 and +5.8, Pact's End +4.4 and +6.0, Bully +3.6 and +3.5, Conflagration +3.0 and +3.4. Of 48 figures clear of 0 on the first set, 47 kept their sign on the second and 42 were clear again; the one that turned is Whirlwind in a Strength deck. A second script that follows fixed rules without searching agrees where it plays the pieces: its mean payoff gains +0.9 to +1.3 HP with 4 pieces of Vulnerable, Exhaust, Block or Strike cards, each interval above 0.

## Lessons
(none yet)
