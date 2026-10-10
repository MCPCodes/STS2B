# Ironclad cards
All 87 Ironclad cards and the starting deck: cost, exact effect, upgrade, keywords, and the interactions that are easy to get wrong.

## Facts (game files, v0.107.1)

### Starting deck
- 10 cards: 5 Strike, 4 Defend, 1 Bash.
- 80 HP, 99 gold, 3 energy a turn, draw 5 a turn, hand limit 10, 3 potion slots.
- Relic Burning Blood: heal 6 HP after each fight won. No effect during a fight.
- Each fight uses copies of the deck. Upgrades and growth gained in a fight (Armaments, Aggression, Rampage, Thrash) end with it.

### How to read the tables
- Effect gives the unupgraded numbers. Upgrade gives what the upgraded card has instead.
- "Deal N" is attack damage to the chosen enemy: N + your Strength, x0.75 if you are Weak, x1.5 if the target is Vulnerable, rounded down once at the end. Every hit of a multi-hit gets all of it. "ALL" is every living enemy.
- "Block" is card Block: N + your Dexterity, x0.75 if you are Frail.
- "Flat" Block or damage ignores Strength, Dexterity, Weak, Vulnerable and Frail. Enemy Block still absorbs flat damage.
- "Lose N HP" ignores Block, Strength, Weak and Vulnerable. It counts as HP lost on your turn (Rupture, Inferno, Spite, Tear Asunder). If it kills you, the rest of the card does not happen.
- A played Power leaves the fight. It is in no pile and does not count as exhausted.
- A card being played is not in your hand or discard pile. Effects that read those piles never pick, count or upgrade it.
- A draw stops at 10 cards in hand. An empty draw pile is refilled by shuffling the discard pile. A created card goes to the discard pile when the hand has 10.
- Vulnerable and Weak on an enemy lose 1 at the end of each enemy turn. N Vulnerable applied on your turn covers this turn and your next N-1 turns: 1 Vulnerable helps this turn only. N Weak covers the enemy's next N turns.
- Artifact on an enemy cancels 1 debuff application (Vulnerable, Weak, Mangle) and loses 1 stack.
- Innate: the card is in your opening hand.
- Cards played by another effect (Havoc, Cascade, Hellraiser, Stampede, Howl from Beyond's return) cost nothing and take a random enemy when they need a target. An X-cost card played this way uses your current energy as X and spends none. They count as card plays for Rage, Juggling, Stomp, One-Two Punch and Unrelenting.
- Every unlock-gated Ironclad card is unlocked on this profile.

### Basic (the starting deck; never in card rewards or shops)
| Card | Cost | Type | Effect | Upgrade | Keywords and notes |
|---|---|---|---|---|---|
| Strike | 1 | Attack | Deal 6. | Deal 9 | Strike tag |
| Bash | 2 | Attack | Deal 8, then apply 2 Vulnerable to the target. | Deal 10, 3 Vulnerable | |
| Defend | 1 | Skill | Gain 5 Block. | 8 Block | Defend tag |

### Common
| Card | Cost | Type | Effect | Upgrade | Keywords and notes |
|---|---|---|---|---|---|
| Anger | 0 | Attack | Deal 6. Then put a copy of this card, same upgrade, in the discard pile. | Deal 8 | |
| Body Slam | 1 | Attack | Deal damage equal to your current Block (Strength is added). Block is kept. | Cost 0 | |
| Breakthrough | 1 | Attack | Lose 1 HP, then deal 9 to ALL. | Deal 13 | |
| Cinder | 2 | Attack | Deal 18. Then exhaust 1 random card in hand. | Deal 24 | |
| Headbutt | 1 | Attack | Deal 9. Then choose 1 card in the discard pile and put it on top of the draw pile. | Deal 12 | |
| Iron Wave | 1 | Attack | Gain 5 Block, then deal 5. | 7 Block, deal 7 | |
| Molten Fist | 1 | Attack | Deal 10. Then double the target's Vulnerable. Nothing if it has none. | Deal 14 | Exhaust |
| Perfected Strike | 2 | Attack | Deal 6 + 2 for each Strike-tagged card you have in any pile, itself and exhausted ones included. | +3 for each | Strike tag |
| Pommel Strike | 1 | Attack | Deal 9, then draw 1. | Deal 10, draw 2 | Strike tag |
| Setup Strike | 1 | Attack | Deal 7. Then gain 2 Strength until end of turn. | Deal 9, 3 Strength | Strike tag |
| Sword Boomerang | 1 | Attack | Deal 3, 3 times. Each hit takes a random living enemy. You choose no target. | 4 times | |
| Thunderclap | 1 | Attack | Deal 4 to ALL, then apply 1 Vulnerable to each. | Deal 7 | |
| Twin Strike | 1 | Attack | Deal 5, 2 times. | Deal 7, 2 times | Strike tag |
| Armaments | 1 | Skill | Gain 5 Block. Choose 1 card in hand; it is upgraded for this fight. | Upgrades every card in hand, no choice | |
| Blood Wall | 2 | Skill | Lose 2 HP, then gain 16 Block. | 20 Block | |
| Bloodletting | 0 | Skill | Lose 3 HP, then gain 2 energy. | 3 energy | |
| Havoc | 1 | Skill | Play the top card of the draw pile for free, then exhaust it. | Cost 0 | |
| Shrug It Off | 1 | Skill | Gain 8 Block, then draw 1. | 11 Block | |
| Tremble | 1 | Skill | Apply 3 Vulnerable to the chosen enemy. | 4 Vulnerable | Exhaust |
| True Grit | 1 | Skill | Gain 7 Block. Then exhaust 1 random card in hand. | 9 Block, and you choose the card | |

### Uncommon
| Card | Cost | Type | Effect | Upgrade | Keywords and notes |
|---|---|---|---|---|---|
| Ashen Strike | 1 | Attack | Deal 6 + 3 for each card in your exhaust pile. | +4 for each | Strike tag |
| Bludgeon | 3 | Attack | Deal 32. | Deal 42 | |
| Bully | 0 | Attack | Deal 4 + 2 for each Vulnerable stack on the target. | +3 for each | |
| Dismantle | 1 | Attack | Deal 8. 2 times if the target has Vulnerable. | Deal 10 | |
| Fight Me (FightMe) | 2 | Attack | Deal 5, 2 times. Then you gain 3 Strength and the target gains 1 Strength. | Deal 6, 2 times; you gain 4 Strength | |
| Hemokinesis | 1 | Attack | Lose 2 HP, then deal 15. | Deal 20 | |
| Howl from Beyond | 3 | Attack | Deal 16 to ALL. If this card is in the exhaust pile when your turn ends, it plays itself for free and goes to the discard pile. | Deal 21 | |
| Pillage | 1 | Attack | Deal 6. Then draw 1, and draw again each time the card just drawn is an Attack. | Deal 9 | |
| Rampage | 1 | Attack | Deal 9. After each play this copy deals 5 more for the rest of the fight: 9, 14, 19... | 9 more after each play: 9, 18, 27... | |
| Spite | 0 | Attack | Deal 5. 2 times if you lost HP earlier this turn. | 3 times, not 2 | |
| Stomp | 3 | Attack | Deal 12 to ALL. Costs 1 less for each Attack you played this turn. | Deal 15 | |
| Unrelenting | 2 | Attack | Deal 14. Your next Attack costs 0. | Deal 20 | |
| Uppercut | 2 | Attack | Deal 13, then apply 1 Weak and 1 Vulnerable to the target. | 2 Weak, 2 Vulnerable; damage stays 13 | |
| Whirlwind | X | Attack | Spend all energy. Deal 5 to ALL, X times. | Deal 8 | |
| Battle Trance | 0 | Skill | Draw 3. You cannot draw more cards this turn. | Draw 4 | |
| Burning Pact | 1 | Skill | Choose 1 card in hand and exhaust it, then draw 2. | Draw 3 | |
| Colossus | 1 | Skill | Gain 5 Block. Through the coming enemy turn, attacks from enemies that have Vulnerable deal half damage to you. | 8 Block | |
| Demonic Shield | 0 | Skill | Lose 1 HP. Another player gains Block equal to your Block. | Loses Exhaust | Exhaust. Multiplayer only: not offered and not playable in single player |
| Dominate | 1 | Skill | Apply 1 Vulnerable to the chosen enemy. Then gain Strength equal to its total Vulnerable. | Apply 2 Vulnerable | Exhaust |
| Drum of Battle | 1 | Skill | Draw 2. Whenever this card is exhausted, gain 2 energy. | 3 energy | |
| Evil Eye | 1 | Skill | Gain 8 Block. If a card of yours was exhausted earlier this turn, gain 8 Block again. | 11 Block each time | |
| Expect a Fight | 2 | Skill | Gain 1 energy for each Attack in hand. Then you cannot gain energy for the rest of the turn. | Cost 1 | |
| Flame Barrier | 2 | Skill | Gain 12 Block. Through the coming enemy turn, each attack hit on you deals 4 flat damage back to the attacker. | 16 Block, 6 back | |
| Forgotten Ritual | 1 | Skill | If a card of yours was exhausted earlier this turn, gain 3 energy. Otherwise nothing. | 4 energy | Exhaust |
| Infernal Blade | 1 | Skill | Add 1 random unupgraded Ironclad Attack to your hand. It costs 0 this turn. | Cost 0 | Exhaust |
| Rage | 0 | Skill | For the rest of the turn, gain 3 flat Block after each Attack you play. | 5 Block | |
| Second Wind | 1 | Skill | Exhaust every non-Attack card in hand. Gain 5 Block for each. | 7 Block for each | |
| Taunt | 1 | Skill | Gain 7 Block. Apply 1 Vulnerable to the chosen enemy. | 8 Block, 2 Vulnerable | |
| Feel No Pain | 1 | Power | Whenever a card of yours is exhausted, gain 3 flat Block. | 4 Block | |
| Inferno | 1 | Power | At the start of your turn, lose 1 HP. Whenever you lose HP on your turn, deal 6 flat damage to ALL. | 9 damage | |
| Inflame | 1 | Power | Gain 2 Strength. | 3 Strength | |
| Juggling | 1 | Power | When your 3rd Attack play of a turn finishes, add a copy of that Attack to your hand. | Innate | |
| Rupture | 1 | Power | Whenever you lose HP on your turn, gain 1 Strength. | 2 Strength | |
| Stampede | 2 | Power | When you end your turn, 1 random Attack in hand is played for free. | Cost 1 | |
| Stone Armor | 1 | Power | Gain 4 Plating. At the end of your turn, gain flat Block equal to Plating. Plating drops by 1 at the start of each later turn. | 6 Plating | |
| Vicious | 1 | Power | Whenever you apply Vulnerable to an enemy, draw 1. | Draw 2 | |

### Rare
| Card | Cost | Type | Effect | Upgrade | Keywords and notes |
|---|---|---|---|---|---|
| Conflagration | 1 | Attack | Deal 2 to ALL, 4 times. | 5 times | |
| Feed | 1 | Attack | Deal 10. If this kills the target, gain 3 max HP and heal 3. | Deal 12, 4 max HP, heal 4 | Exhaust |
| Fiend Fire | 2 | Attack | Exhaust every card in hand. Then deal 7, 1 time for each card exhausted. | Deal 10 | Exhaust |
| Mangle | 3 | Attack | Deal 15. The target loses 10 Strength until the end of its next turn. | Deal 20, 15 Strength | |
| Pact's End | 0 | Attack | If your exhaust pile has 3 or more cards, deal 17 to ALL. Otherwise nothing. | Deal 23 | |
| Tear Asunder | 2 | Attack | Deal 5, 1 time plus 1 more for each time you lost HP this fight. | Deal 7 | |
| Thrash | 1 | Attack | Deal 4, 2 times. Then exhaust 1 random Attack in hand and add its damage number to this copy for the rest of the fight. | Deal 6, 2 times | |
| Brand | 0 | Skill | Lose 1 HP. Choose 1 card in hand and exhaust it. Gain 1 Strength. | 2 Strength | |
| Cascade | X | Skill | Spend all energy. Play the top X cards of the draw pile for free. | X+1 cards | |
| Impervious | 2 | Skill | Gain 30 Block. | 40 Block | Exhaust |
| Not Yet | 2 | Skill | Heal 10 HP. | Heal 13 | Exhaust |
| Offering | 0 | Skill | Lose 6 HP, gain 2 energy, draw 3. | Draw 5 | Exhaust |
| One-Two Punch | 1 | Skill | The next Attack you play this turn is played 2 times. | The next 2 Attacks | |
| Primal Force | 0 | Skill | Turn every Attack in hand into a Giant Rock. | The Rocks are upgraded | |
| Stoke | 1 | Skill | Exhaust every card in hand. Add that many random Ironclad cards to your hand. | The new cards are upgraded | |
| Aggression | 1 | Power | At the start of your turn, 1 random Attack moves from the discard pile to your hand and is upgraded for this fight. | Innate | |
| Barricade | 3 | Power | Your Block is not removed at the start of your turn. | Cost 2 | |
| Crimson Mantle | 1 | Power | At the start of your turn, lose 1 HP, then gain 8 flat Block. | 10 Block | |
| Cruelty | 1 | Power | Your attacks on Vulnerable enemies are multiplied by 1.75, not 1.5. | By 2.0 | |
| Dark Embrace | 2 | Power | Whenever a card of yours is exhausted, draw 1. | Cost 1 | |
| Demon Form | 3 | Power | At the start of your turn, gain 2 Strength. | 3 Strength | |
| Hellraiser | 2 | Power | Whenever you draw a Strike-tagged card, it is played at once for free on a random enemy. | Cost 1 | |
| Juggernaut | 2 | Power | Whenever you gain Block, deal 6 flat damage to a random enemy. | 8 damage | |
| Pyre | 2 | Power | You get 1 more energy each turn, from next turn. | 2 more | |
| Tank | 1 | Power | You take double attack damage. Other players take half. | Cost 0 | Multiplayer only: not offered in single player |
| Unmovable | 2 | Power | The first card that gives you Block each turn gives double. | Cost 1 | |

### Ancient (never in card rewards or shops)
| Card | Cost | Type | Effect | Upgrade | Keywords and notes |
|---|---|---|---|---|---|
| Break | 1 | Attack | Deal 20, then apply 5 Vulnerable to the target. | Deal 30, 7 Vulnerable | The Archaic Tooth relic turns Bash into it, upgraded if Bash was |
| Corruption | 3 | Power | Your Skills cost 0 and are exhausted when played. | Cost 2 | The Dusty Tome relic adds it, upgraded |

### Token (made in a fight only)
| Card | Cost | Type | Effect | Upgrade | Keywords and notes |
|---|---|---|---|---|---|
| Giant Rock (GiantRock) | 1 | Attack | Deal 16. | Deal 20 | Made only by Primal Force. Goes to the discard pile after play |

### Details that are easy to get wrong
- Aggression: the Attack arrives before the turn's draw, so you start with 5 drawn cards plus it. It is not a draw: Battle Trance does not block it and Hellraiser does not play it.
- Aggression, Crimson Mantle, Demon Form, Inferno's HP loss, Pyre: first effect at the start of your next turn. Nothing on the turn they are played.
- Armaments, Brand, Burning Pact, Headbutt, upgraded True Grit: with exactly 1 valid card there is no prompt; it is taken. With none, that step is skipped and the rest of the card still happens.
- Armaments: status cards cannot be upgraded.
- Ashen Strike, Perfected Strike: Strength is added once, not per card counted. Ashen Strike counts the exhaust pile at the moment it hits.
- Battle Trance: after it, every other draw this turn gives nothing: Pommel Strike, Shrug It Off, Offering, Burning Pact, Pillage, Drum of Battle, Dark Embrace, Vicious, a 2nd Battle Trance. Play those first. The next turn's hand draw is normal.
- Bully: Vulnerable counts 2 ways: +2 per stack, then the x1.5.
- Bully, Dismantle, Molten Fist: they read the target's Vulnerable when played. Apply it first.
- Cascade: playable at 0 energy. X = 0 plays nothing; upgraded it plays 1. The cards are taken off the top first, then played in order. An Unplayable card taken this way goes to the discard pile unplayed.
- Cinder, True Grit, Thrash: the exhausted card is random. Cinder and True Grit pick from the whole hand, Thrash from Attacks only. Play what you want to keep first.
- Colossus: only attackers that have Vulnerable when they hit are halved. 1 Vulnerable applied this turn is still on the enemy when it attacks. 1 play covers 1 enemy turn.
- Corruption: X-cost Skills (Cascade) still spend all energy. Each Skill played is exhausted, which triggers Feel No Pain and Dark Embrace, gives Drum of Battle's energy when Drum is the Skill, and enables Evil Eye and Forgotten Ritual. Status cards are not Skills.
- Crimson Mantle, Inferno: each extra copy played adds its number (8 Block, 6 damage) and 1 more HP lost per turn. The HP loss comes after the turn's draw.
- Cruelty: it adds 0.25 (upgraded 0.50) to the Vulnerable multiplier. Extra copies and the Paper Phrog relic (+0.25) add up.
- Dark Embrace: draws at once on each exhaust. For an Ethereal card exhausted at end of turn, the draw comes after the hand is discarded and that card stays for next turn.
- Dominate: Strength equals the target's Vulnerable after the application, old stacks included. If Artifact blocks it and the target has none, you gain 0.
- Drum of Battle: it has no Exhaust keyword. Played normally it only draws 2. The energy comes when another effect exhausts it (Burning Pact, Brand, True Grit, Cinder, Second Wind, Fiend Fire, Stoke, Havoc) or when it is played under Corruption.
- Evil Eye, Forgotten Ritual: any exhaust earlier in your current turn counts, a played Exhaust card included. A played Power does not. Forgotten Ritual's own exhaust does not. Evil Eye's 2nd gain is a separate Block gain.
- Expect a Fight: after it, Bloodletting, Offering, Drum of Battle, Forgotten Ritual and potions give 0 energy this turn. Play those first.
- Feed: killing a Minion (Eye with Teeth, Kin Follower) gives nothing. The gain still happens when the kill ends the fight.
- Fiend Fire: it exhausts first, then hits. Hits = cards in hand when played. Empty hand = 0 hits. Cards drawn during the exhausts (Dark Embrace) stay in hand and add no hits.
- Fight Me: your Strength comes after its hits. The enemy keeps its +1 Strength for the fight; Artifact does not stop it.
- Flame Barrier: it triggers on every hit, blocked or not. The attacker's Block absorbs the damage back. If it kills an attacker during a multi-hit, the remaining hits are cancelled.
- Havoc: the card it plays is exhausted even without the Exhaust keyword. A Power played by Havoc works as usual. An Unplayable top card is exhausted unplayed. With an empty draw pile the discard pile is shuffled first.
- Hellraiser: Strike-tagged cards are Strike, Perfected Strike, Pommel Strike, Twin Strike, Setup Strike and Ashen Strike. It also triggers during the start-of-turn draw; those cards are not replaced, so the hand is smaller.
- Hemokinesis, Breakthrough: with Rupture, the Strength from the card's own HP loss arrives after the card finishes. Its own hit does not get it.
- Howl from Beyond: it has no Exhaust keyword, so another effect must exhaust it (Havoc, Burning Pact, Brand, True Grit, Cinder, Thrash, Fiend Fire, Stoke). Played by Havoc it hits, is exhausted, and hits again at end of turn.
- Infernal Blade: it picks from 33 Attacks (no Strike, Bash, Break or Feed). A created Whirlwind still spends all energy. The free cost ends with the turn.
- Inferno: its own start-of-turn HP loss triggers its damage: 1 HP for 6 to ALL every turn. HP lost on the enemy turn triggers nothing.
- Juggernaut: once per Block gain from any source: Rage, Feel No Pain, Stone Armor, Crimson Mantle, and each separate gain of Second Wind and Evil Eye.
- Juggling: only the 3rd Attack play each turn. Attacks played earlier the same turn, free plays by other effects and One-Two Punch repeats all count. The copy keeps the upgrade and a free-this-turn cost.
- Mangle: the enemy attacks with 10 less Strength on its coming turn, then gets it back. A hit cannot go below 0.
- One-Two Punch: energy is paid once. The repeat hits the same target and does nothing if it is dead. Unused charges end with the turn.
- Pact's End: always playable. With fewer than 3 cards in the exhaust pile it does nothing. Played Powers are not in the exhaust pile.
- Perfected Strike: the starting 5 Strikes plus this card make 6 + 2 x 6 = 18 (upgraded 24). Removing Strikes lowers it.
- Pillage: the first non-Attack drawn ends the chain and stays in hand. The chain continues through a reshuffle.
- Primal Force: the Attacks are gone for the rest of the fight. They are not exhausted, so nothing triggers. The Rocks stay in the deck for the fight. Primal Force goes to the discard pile and can be played again.
- Rage: Attacks played before Rage give nothing. Whirlwind with X = 0 counts as an Attack played.
- Rupture: every HP loss on your own turn is 1 trigger: cards, Inferno, Crimson Mantle, Burn-type cards in hand at end of turn. HP lost on the enemy turn never counts.
- Second Wind: Power cards and status cards in hand are non-Attacks and are exhausted too. Each exhausted card is a separate Block gain. Cards drawn during it (Dark Embrace) are not exhausted.
- Spite: only HP lost in your current turn counts (self-damage cards, Inferno, Crimson Mantle). Damage taken on the enemy turn does not.
- Stampede: it is the first thing at end of turn, before Stone Armor's Block and before Burn-type damage. Rage still gives Block for it. A Whirlwind picked this way uses the energy you left unspent.
- Stoke: it picks from 78 cards (no Basic, Ancient or multiplayer cards, no Feed, no Not Yet). Duplicates are possible, Stoke included. The new cards cost their normal price.
- Stomp: the discount counts Attacks played this turn wherever Stomp is, the draw pile included. It ends with the turn.
- Stone Armor: 4 Plating gives 4, 3, 2, 1 Block over 4 turns (10 in total; upgraded 21). Being hit does not reduce it.
- Tear Asunder: every HP loss this fight counts, from any source: each unblocked enemy hit, self-damage, Burn. A fully blocked hit does not.
- Thrash: it adds the exhausted Attack's per-hit number with your Strength already in it (x0.75 if you are Weak); hit counts are ignored. With no Attack in hand it does not grow.
- Unmovable: it doubles card Block only, not Rage, Stone Armor, Feel No Pain or Crimson Mantle. A Block card played earlier the same turn has already used it up. Every gain of a multi-gain card (Second Wind, Evil Eye) is doubled.
- Unrelenting: the free charge does not expire at end of turn. The next Attack play uses it up, even a 0-cost one or a free play by another effect. X-cost Attacks use it up without becoming free.
- Vicious: 1 draw per enemy that receives Vulnerable (Thunderclap on 3 enemies draws 3). Nothing when Artifact blocks it.

## Lessons
(none yet)
