# Act 1 between fights
Act 1 outside combat for the Ironclad at ascension 0: floors, room rules, rewards, card odds, rest sites, shops, Neow and heals.

## Facts (game files, v0.107.1)

### Start

- Ironclad: 80 HP, 99 gold, 3 empty potion slots, relic Burning Blood.
- Deck: 5 Strike, 4 Defend, 1 Bash.
- Each turn: energy is set to 3, draw 5. Hand limit 10.
- Act 1 is Overgrowth or Underdocks. On a profile that has unlocked and played Underdocks it is 50/50 each run. Both have the same floor layout. This wiki covers Overgrowth only so far.

### Floors

| Floor | Room |
| --- | --- |
| 1 | Neow |
| 2 | Monster, on every path |
| 3-6 | Monster, Unknown or Shop |
| 7-9 | Monster, Unknown, Shop, Elite or Rest Site |
| 10 | Treasure, on every path |
| 11-13 | Monster, Unknown, Shop, Elite or Rest Site |
| 14-15 | Monster, Unknown, Shop or Elite |
| 16 | Rest Site, on every path |
| 17 | Boss |

- Earliest Elite: floor 7. Earliest Rest Site: floor 7.
- Rest Sites: floors 7-9, 11-13 and 16. None on floors 14-15, so floor 13 is the last one you can choose.
- Elites: floors 7-9 and 11-15. The map has 5 Elite nodes and 3 Shop nodes.
- A path reaches floor 7 after 1-5 Monster rooms, with no Elite and no Rest Site before it.
- An Elite node is never directly before or after another Elite node. The same holds for Rest Site and for Shop.
- Two branches from the same node are never both Elite, both Shop or both Unknown, and never both Rest Site below floor 16.
- Treasure (floor 10): 1 relic and 42-52 gold.

### Unknown rooms

- One roll per Unknown room: fight 10%, Treasure 2%, Shop 3%, otherwise Event (85%). Never an Elite.
- After each Unknown room the type that came up goes back to its starting chance; each other type gains its starting chance. After 1 Event: fight 20%, Treasure 4%, Shop 6%. The chances start over in each act.
- The k-th Unknown room of the act is a fight with 10%, 19%, 25%, 28%, 29% (k = 1 to 5), an Event with 85%, 71%, 60%, 54%, 50%.
- It is not a Shop if the room before it was a Shop.
- A fight from an Unknown room is the next hallway encounter and gives hallway rewards.
- Each event shows at most once per run.

### Rewards

| Room | Gold | Cards | Also |
| --- | --- | --- | --- |
| Monster | 10-20 | 3 cards, hallway odds | potion roll |
| Elite | 35-45 | 3 cards, elite odds | 1 relic (Common 50%, Uncommon 33%, Rare 17%), potion roll with +12.5 points |
| Boss | 100 | 3 Rare cards | potion roll, no relic |

- Take 1 card or skip.
- Potion drops: strategy/wiki/potions.md.

### Card rewards

- Pool: Ironclad cards only, 20 Common, 35 Uncommon, 25 Rare. No Strike, Defend or Bash, no Break or Corruption, no colorless cards.
- The 3 cards of an offer are always different. A card already in the deck can be offered.
- Each card rolls its rarity on its own:

| Fight | Rare | Uncommon | Common |
| --- | --- | --- | --- |
| Hallway | 3% + offset | 37% | the rest |
| Elite | 10% + offset | 40% | the rest |
| Boss | 100% | 0% | 0% |

- While 3% + offset is below 0, Rare is 0% and Uncommon is lower by the shortfall: the first 3 cards of a run are 35%, 36%, 37% Uncommon.
- The offset (pity) is one number for the whole run. It starts at -5 points.
- Each reward card that is not Rare adds 1 point, up to +40. Each Rare card sets it back to -5.
- It moves once per card, 3 times per reward, when the fight ends. Skipping the reward changes nothing.
- It is not reset between acts. A boss reward leaves it at -5.
- Chance that a hallway reward offers at least 1 Rare, hallway fights only from run start: reward 1: 0%. Reward 2: 5.9%. Reward 3: 13.5%. Reward 4: 18.5%. Reward 5: 20.8%. Reward 6 and later: about 20%.
- Elite reward, Rare chance of its first card: 5% at offset -5. 11% after 6 cards without a Rare (2 hallway rewards). 17% after 12 cards (4 hallway rewards).
- Reward cards are never upgraded in act 1. Act 2: 25% per card. Act 3: 50%. Rare cards never.
- Card offers from Lost Coffer and from some events use fixed odds instead: Rare 3%, Uncommon 34%, Common 63%. They do not move the offset.

### Rest Site

- One option per visit: Heal or Smith.
- Heal: 30% of max HP, 24 at 80 max HP, up to max HP.
- Smith: upgrade 1 chosen card. Backing out of the card choice does not use up the visit.
- Floor 16 is always a Rest Site: healing there means the boss starts at HP + 24, up to max HP.

### Shop

- Card removal: 75 gold, +25 for each removal already bought this run. 1 per shop.
- 5 Ironclad cards: 2 Attacks, 2 Skills, 1 Power. 1 of the 5 is half price. The Power is never Common.
- 2 colorless cards: 1 Uncommon, 1 Rare.
- 3 relics. 3 different potions.
- Prices, each varied by up to 5% either way: Common card 50, Uncommon 75, Rare 150. Colorless cards cost 15% more. Potions 50, 75, 100 by rarity.
- Shop card rarity: Rare 9% + offset, Uncommon 37%. Shop cards do not move the offset and are never upgraded.
- Gold on arrival at a shop on floors 3-6: 110-170 (estimate). That pays for the removal plus 1 cheap card or potion.

### Neow (floor 1)

- Neow offers 3 relics: 2 from the first table, 1 from the second. Take 1.

| Relic | Effect |
| --- | --- |
| Arcane Scroll | Adds 1 random Ironclad Rare. |
| Precise Scissors | Remove 1 chosen card. |
| New Leaf | Transform 1 chosen card. |
| Pomander | Upgrade 1 chosen card. |
| Neow's Talisman | Upgrades 1 Strike and 1 Defend. |
| Scroll Boxes | Choose 1 of 2 bundles. A bundle is 2 Commons and 1 Uncommon; all 3 join the deck. |
| Lost Coffer | 1 offer of 3 cards at the fixed odds (Rare 3%) and 1 random potion. |
| Lead Paperweight | Choose 1 of 2 colorless cards, or skip. Each is Rare with 3%, otherwise Uncommon. |
| Kaleidoscope | 2 offers of 3 cards. In each offer the 3 cards come from 3 other characters. |
| Neow's Torment | Adds Neow's Fury: Attack, 1 energy, 10 damage to 1 enemy, then moves up to 2 chosen cards from the discard pile to the hand, Exhaust. Upgraded: 14 damage, 3 cards. |
| Fishing Rod | After every 3rd hallway fight, upgrades 1 random card. |
| Nutritious Oyster | +11 max HP. |
| Stone Humidifier | +5 max HP after every Rest Site heal. |
| Phial Holster | +1 potion slot and 2 random potions. |
| Booming Conch | In elite fights: +2 cards and +1 energy on turn 1. |
| Golden Pearl | +150 gold. |
| Small Capsule | 1 random relic now. |
| Lava Rock | 2 extra relics from the act 1 boss. |
| Winged Boots | 3 free map moves. |

| Relic with a drawback | Effect |
| --- | --- |
| Cursed Pearl | +333 gold. Adds the curse Greed. |
| Hefty Tablet | Choose 1 of 3 Rares, or skip. Adds the curse Injury either way. |
| Large Capsule | 2 random relics. Adds 1 Strike and 1 Defend. |
| Leafy Poultice | Transforms 1 Strike and 1 Defend. -12 max HP. |
| Neow's Bones | 2 other random Neow relics and 1 random curse. |
| Precarious Shears | Remove 2 chosen cards. Take 16 damage. |
| Silken Tress | The cards of the next card reward come enchanted with Glam. Lose all gold. |
| Silver Crucible | The first 3 card rewards are offered upgraded. The first Treasure room is empty. |

- A transform gives a random Common, Uncommon or Rare Ironclad card other than the old one: 31% Rare from a Strike or Defend. The new card is not upgraded.

### Heals

| Source | Amount |
| --- | --- |
| Neow | The run starts at full HP. |
| Burning Blood | +6 after every won fight, boss and event fights included, before the rewards. |
| Rest Site, Heal | +24 (30% of max HP). |
| Blood Potion | +16 (20% of max HP). Usable outside combat. |
| Max HP gain | Heals by the amount gained. |
| Sapphire Seed (event) | One option: +9 and 1 chosen card upgraded. |
| Tablet of Truth (event) | One option: +20. |
| Unrest Site (event, only at 70% HP or less) | One option: to full HP, adds the curse Poor Sleep. |
| Dense Vegetation (event) | One option: a Rest Site heal, then a fight with 4 Wrigglers. |

- No heal can exceed max HP.
- After the act 1 boss only Burning Blood heals. The Ancient at the start of act 2 then heals to full HP, so HP left after the boss fight does not carry into act 2.

## Lessons
(none yet)
