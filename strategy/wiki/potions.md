# Potions
Every potion the Ironclad can find: target and exact effect, when a potion can be used, how potion damage and Block work, drop odds.

## Facts (game files, v0.107.1)

### Rules

- 3 potion slots, empty at run start.
- A potion costs no energy. In a fight it can be used only on your own turn.
- Usable outside fights too: Blood Potion, Fruit Juice, Entropic Brew. Fairy in a Bottle works by itself. Every other potion is fight only.
- Potion damage ignores your Strength, your Weak and the target's Vulnerable. Fire Potion is always 20, Explosive Ampoule always 10.
- Potion Block ignores your Dexterity and your Frail. Block Potion is always 12.
- Enemy Block absorbs potion damage.
- Slippery cuts potion damage: a hit on an enemy with Slippery removes 1 HP and 1 stack. Inklets start with 1 stack, Vantom with 8. A Fire Potion on an enemy that still has Slippery does 1.
- Two Underdocks caps also apply to potion damage: Hardened Shell (Skulking Colony loses at most 20 HP per turn) and Intangible (Soul Fysh, while it has it: each hit does 1).
- Potion damage does not set off an enemy's Thorns.
- An enemy with Vulnerable takes 1.5 times attack damage. An enemy with Weak deals 0.75 times attack damage. 3 Vulnerable put on an enemy on your turn lasts that turn and your next 2. 3 Weak covers the enemy's next 3 turns.
- With every slot full, a potion reward can only be taken after discarding one.
- Target "you": the potion acts on you. Some of these ask for a player target; solo that is you.

### Drops

- After every fight (hallway, elite, boss) one roll decides a potion drop. The chance starts the run at 40%, falls 10 points after a drop and rises 10 points after no drop. It is never reset. An elite fight rolls with +12.5 points.
- Rarity of a drop: Common 65%, Uncommon 25%, Rare 10%, then an even pick among the 16 potions of that rarity.
- Shops sell 3 potions: 50, 75 or 100 gold by rarity.

### Common

| Potion | Target | Effect |
| --- | --- | --- |
| Fire Potion | 1 enemy | 20 damage. |
| Explosive Ampoule | all enemies | 10 damage to each. |
| Block Potion | you | 12 Block. |
| Strength Potion | you | +2 Strength for the fight. |
| Flex Potion | you | +5 Strength until the end of your turn. |
| Dexterity Potion | you | +2 Dexterity for the fight. |
| Speed Potion | you | +5 Dexterity until the end of your turn. |
| Energy Potion | you | +2 energy. |
| Swift Potion | you | Draw 3. |
| Weak Potion | 1 enemy | 3 Weak. |
| Vulnerable Potion | 1 enemy | 3 Vulnerable. |
| Blood Potion | you | Heal 20% of max HP: 16 at 80. |
| Attack Potion | you | Choose 1 of 3 random Ironclad Attacks, or skip. It goes to your hand and costs 0 this turn. |
| Skill Potion | you | The same with Skills. |
| Power Potion | you | The same with Powers. |
| Colorless Potion | you | The same with colorless cards. |

- The 3 choices are drawn evenly from all cards of the type, whatever their rarity: 33 Attacks (6 Rare), 27 Skills (7 Rare), 18 Powers (10 Rare). Feed and Not Yet are never offered.

### Uncommon

| Potion | Target | Effect |
| --- | --- | --- |
| Regen Potion | you | Regen 5: heal 5 at the end of your turn, then 4, 3, 2, 1. 15 HP in all. |
| Heart of Iron | you | Plating 7: 7 Block at the end of your turn, then 6, 5, 4, 3, 2, 1. 28 Block in all. |
| Liquid Bronze | you | Thorns 3 for the fight: each time an enemy attack hit is about to land on you, the attacker takes 3 damage first. |
| Fortifier | you | Triples your current Block. |
| Cure All | you | +1 energy and draw 2. It removes no debuff. |
| Fysh Oil | you | +1 Strength and +1 Dexterity for the fight. |
| Potion of Binding | all enemies | 1 Weak and 1 Vulnerable to each. |
| Ashwater | you | Exhaust any number of cards from your hand. |
| Blessing of the Forge | you | Upgrades every card in your hand that can be upgraded. |
| Clarity Extract | you | Draw 1 now. For the next 3 turns, draw 1 more card at turn start. |
| Duplicator | you | The next card you play this turn is played 1 extra time. |
| Gambler's Brew | you | Discard any number of cards, then draw that many. |
| Radiant Tincture | you | +1 energy now and +1 energy at the start of each of the next 3 turns. |
| Stable Serum | you | Your hand is not discarded at end of turn, for 2 turns. |
| Powdered Demise | 1 enemy | Demise 9: it loses 9 HP at the end of every enemy turn for the rest of the fight. Block does not stop it. |
| Touch of Insanity | you | 1 chosen card in your hand that costs energy costs 0 for the rest of the fight. |

### Rare

| Potion | Target | Effect |
| --- | --- | --- |
| Fruit Juice | you | +5 max HP and heal 5. |
| Fairy in a Bottle | you, automatic | When you would die: you do not, once, and heal 30% of max HP (24 at 80). The potion is used up. |
| Shackling Potion | all enemies | Each loses 7 Strength until the end of the enemy turn. |
| Soldier's Stew | you | For the rest of the fight these cards are played 1 extra time: Strike, Twin Strike, Pommel Strike, Perfected Strike, Setup Strike, Ashen Strike. Only copies you have in a pile at that moment; cards created later are not affected. |
| Beetle Juice | 1 enemy | Shrink 4: its attacks deal 30% less for its next 4 turns. |
| Bottled Potential | you | Your hand goes into the draw pile, the pile is shuffled, then draw 5. |
| Distilled Chaos | you | Plays the top 3 cards of your draw pile. |
| Droplet of Precognition | you | Choose 1 card from your draw pile and put it in your hand. |
| Entropic Brew | you | Fills every empty potion slot with a random potion. |
| Gigantification Potion | you | Your next Attack card deals 3 times damage. |
| Liquid Memories | you | Choose 1 card from your discard pile. It goes to your hand and costs 0 this turn. |
| Lucky Tonic | you | Buffer 1: the next time you would lose HP, you lose 0. |
| Mazaleth's Gift | you | Ritual 1: +1 Strength at the end of each of your turns. |
| Orobic Acid | you | Adds 1 random Ironclad Attack, 1 Skill and 1 Power to your hand. Each costs 0 this turn. |
| Ship in a Bottle | you | 10 Block now. 10 more right after your Block is next cleared, at the start of your next turn. |
| Snecko Oil | you | Draw 7. Then every card in your hand without an X cost gets a random cost of 0-3 for this turn. |

## Lessons
(none yet)
