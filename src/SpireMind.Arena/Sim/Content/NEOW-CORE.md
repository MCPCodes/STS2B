# Neow's relics, the first 14

Chunk 14 of the run-mode plan (`sts2-notes/run-mode-v0.107.1/plan.md`, 5.2 and 5.3; `neow.json`): the relics of Neow that need no screen the arena does not have. What a relic is written against is `Sim/Run/RELICS.md`; Neow's room and how her three options are rolled are `Sim/Run/EVENTS.md`; the deck commands and card choices are `Sim/Run/DECK.md`; card offers and rewards screens are `Sim/Run/REWARDS.md`. Everything here is the game's (v0.107.1, Ironclad, ascension 0, single player), each relic from its class under `MegaCrit.Sts2.Core.Models.Relics`, unless a line says "house rule".

## Files

| File | Holds |
| --- | --- |
| `NeowCore.cs` | the 14 relics and the module that registers them (`NeowCore.Positive`, `Cursed`, `All`) |
| `NeowCoreChecks.cs` | their checks |

Nothing else was needed: a relic is offered by Neow as soon as it is registered (`Neow.cs` asks the registry), and `ReachableRelics` takes a relic off the left-out list as soon as a top-level class of its name exists.

## The 14

| Group at Neow | Relic | What it does at pickup | Asks | Rolls |
| --- | --- | --- | --- | --- |
| positive, fixed | Arcane Scroll | 1 random Rare card of the Ironclad's pool into the deck | nothing | 1 number of `rewards` |
| positive, fixed | Booming Conch | nothing at pickup. In an Elite fight: 2 more cards in the draw of turn 1, and 1 energy after it | | |
| positive, fixed | Golden Pearl | 150 gold | nothing | |
| positive, fixed | Lost Coffer | a rewards screen: a potion and a card reward of 3 | `rewards`, then `card_reward` | 11 numbers of `rewards` |
| positive, fixed | Neow's Torment | the card Neow's Fury into the deck | nothing | |
| positive, fixed | New Leaf | 1 chosen card is transformed | `card_select`, kind `transform` | 1 number of `niche` |
| positive, fixed | Precise Scissors | 1 chosen card is removed | `card_select`, kind `remove` | |
| positive, coin 2 heads | Nutritious Oyster | 11 max HP (and 11 HP) | nothing | |
| positive, coin 3 heads | Neow's Talisman | the last starter Strike and the last starter Defend are upgraded | nothing | |
| positive, coin 3 tails | Pomander | 1 chosen card is upgraded | `card_select`, kind `upgrade` | |
| cursed | Cursed Pearl | the curse Greed, then 333 gold | nothing | |
| cursed | Hefty Tablet | 1 of 3 random Rares, or none; and the curse Injury | `card_select`, kind `choose` (with `skip`) | 3 numbers of `rewards` |
| cursed | Leafy Poultice | 12 max HP lost; the first starter Strike and the first starter Defend are transformed | nothing | 2 numbers of `transformations` |
| cursed | Precarious Shears | 2 chosen cards are removed, then 16 damage | `card_select`, kind `remove` | |

The four cursed relics take their twins out of Neow's offer: Cursed Pearl Golden Pearl, Hefty Tablet Arcane Scroll, Leafy Poultice New Leaf, Precarious Shears Precise Scissors. All eight are here.

What to know when reading them:

- **A relic is the player's before its pickup asks.** `run.ObtainRelic` puts the relic in the list and then runs `AfterObtained`, as the game does. So while Precise Scissors' grid is up the snapshot's `relics` already name it; the deck changes when the choice is answered. Neow's page comes back finished after the last screen of the pickup.
- **"Starter" means Basic rarity.** Neow's Talisman and Leafy Poultice look only at the deck's cards of Basic rarity (Strike, Defend, Bash) and among them for the Strike and the Defend tag. Twin Strike, Perfected Strike or Ultimate Defend never count. Talisman takes the LAST of each, Poultice the FIRST.
- **Neow's Talisman does not look for a card it can upgrade.** It takes the last starter Strike whatever its state; if that one is upgraded already, nothing happens for the Strikes. The same for the Defends.
- **Arcane Scroll and Hefty Tablet roll "cards for a reward"** (`CardFactory.CreateForReward`) with uniform odds over the pool's Rares and the flag that leaves the upgrade roll out: no rarity roll, one pick per card, and the offer hooks are asked (a relic that changes offered cards would change these). The 25 Rares a single-player Ironclad can roll are all built, so the pick is never drawn again. The pity offset of card rewards does not move.
- **Hefty Tablet adds its two cards in one add** (`DeckEdits.Add` with a list): both are in the deck before a listener hears that either arrived. Skipping still adds Injury.
- **Lost Coffer's cards are on the regular table without the pity offset** (the offer's source is not a fight): Rare below 0.03, Uncommon below 0.37, else Common; the offset stays where it was. Each card takes a rarity roll, a pick and an upgrade roll (which cannot succeed in act 1), then the potion takes a rarity roll and a pick. The screen sorts its rows, so the potion's row is shown first. Its rows may be left: `proceed` closes the screen. A potion roll that lands on a potion that is left out is rolled again (the house rule of `REWARDS.md`), which takes two more numbers.
- **New Leaf rolls on `niche`, Leafy Poultice on the player's `transformations`.** New Leaf transforms its chosen cards one at a time (`DeckEdits.TransformToRandom`); Leafy Poultice transforms both of its cards in ONE call (`DeckEdits.Transform`), the Strike's roll first: both originals leave the deck, then the new cards go to the end of the deck in the order of the places their originals had. A transformed starter card becomes one of the 80 Common, Uncommon and Rare cards of the Ironclad's pool that a single-player run can roll; all are built.
- **Leafy Poultice's loss is the game's max HP loss**: HP above the new maximum is lost first, as an HP loss that ignores Block, and the damage hooks hear it (Tungsten Rod takes 1 off, and the cut to the new maximum makes the HP the same). A player whose maximum is 12 or less dies of it; nothing is transformed after a death. At Neow the player has 80, so this cannot come up in a run.
- **Precarious Shears deals damage, not an HP loss**: 16, unpowered, no dealer (`Props.NonCardUnpowered`). Block would absorb it, which there is none of outside a fight; Tungsten Rod makes it 15; it can kill a player at 16 HP or less, which cannot come up at Neow, where the player was just healed to full.
- **The choices cannot be backed out of** and do not ask for confirmation of their own, so a deck with no more candidates than the relic asks for is handled without a screen: Precise Scissors with one removable card removes it, Precarious Shears with two removes both, Pomander with one upgradable card upgrades it.
- **Eternal cards** (Greed) are never listed for removal or transformation; curses lead the removal grid.
- **Booming Conch** reads the fight's room kind (`f.RoomType`), as the game reads the current room: an Elite's fight only. The draw is in the first pass of the hand-draw modifiers; the energy is gained in "after side turn start" of the player's turn 1, after the draw. It keeps nothing between rooms.
- **Inside a fight** (no relic of Neow is ever obtained there): Golden Pearl and Nutritious Oyster work through the `RunContext`; the other pickups need the run and are refused by name (`RunContext.NeedRun`) before they change anything.
- **Tooltips.** Neow's Torment shows Neow's Fury, Cursed Pearl Greed, Hefty Tablet Injury (`ICardTips`): the option at Neow lists the card under `cards`. The Transform and energy tips of other relics are not cards and are not shown.
- **Keyed streams** (the house rule of `STREAMS.md`): New Leaf's roll is the purpose `relic:NewLeaf/<n>`, Leafy Poultice's two are `transform/0` and `transform/1`, the reward cards are `card/<reward>/<card>` as for every offer.

## How much of the real game this is

Neow's offer is three built relics on 7 seeds in 60 (11.7%): the cursed relic is one of these four half the time, and then 7 or 8 of the 15 relics in the shuffled list are of this file. On every other seed the offer is drawn again around what is missing (`EVENTS.md`), and the run's log says so from floor 1; a strict run ends there as unrepresentable. Seed 1BUSLN0FY5, the real run the checks follow, is one of the 7 in 60: Precise Scissors, Arcane Scroll, Leafy Poultice.

## Left out by name

- The other 13 of Neow's relics: Fishing Rod, Phial Holster, Scroll Boxes, Winged Boots, Lava Rock, Small Capsule, Stone Humidifier, Large Capsule, Silver Crucible (chunk 22), Lead Paperweight (28), Silken Tress (32), Neow's Bones (33), Kaleidoscope (for good). `arena content` prints each with its chunk.
- What the game writes into its run history at these pickups (the cards Hefty Tablet offered and did not give, max HP lost, gold gained).
- What the game goes on to do after a pickup killed the player (Leafy Poultice would still transform its two cards): the run is over and its record was written at the death.
- These relics obtained by a player who is not at Neow with a fresh deck are built all the same (the checks give them to decks of their own); only Neow's Bones, which hands out two other relics of Neow, would do that in a run.

## Checks

`NeowCoreChecks` (464 assertions with the folder of real records on the machine, 453 without). What the expected values rest on:

- **The real game's records** (`sts2-notes/golden-saves/history`), typed in: on seed 1BUSLN0FY5 Neow offered Precise Scissors, Arcane Scroll and Leafy Poultice and Arcane Scroll gave Barricade from one number of `rewards` (played through the registry's own Neow, strict and not, with the page key for key); on seed 75F277YN2G the offer was Pomander, Nutritious Oyster, Silver Crucible and Pomander upgraded Bash; on seed AG5XPLQQNY it was Phial Holster, Golden Pearl, Silken Tress and Golden Pearl paid 150 gold; seed 1CJXGGA4E5 offered Neow's Torment, Lava Rock and Cursed Pearl. Where an offer holds a relic of a later chunk, a stand-in of the checks stands in for it and is never taken.
- **The game's classes, by hand**: every number, the order inside a pickup (a witness relic writes down what it hears and what the player has at that moment: the curse before the gold, both cards of one add, both originals out before the first new card, the loss before the transform, the removals before the damage, the kind of damage), which stream moved by how many numbers, what each grid lists, and what happens with too few candidates.
- **Random cards**: the Ironclad's pool typed into the checks from the game's own list with each card's rarity (80 cards, 25 Rares), indexed by a second stream of the same seed walked by the rule in words; on 200 or 300 seeds for each of Arcane Scroll, Hefty Tablet, Lost Coffer, New Leaf and Leafy Poultice.
- **Through an Ancient's room**: all 14 as options of an Ancient of the checks (titles, labels, the three card tooltips), each taken, its screens answered, the room left, with a replay, a copy at the end and a copy taken in the middle of the pickup; and the registry's Neow on seeds whose whole offer is built until every relic was taken on two seeds.
- **The share**: 7 in 60 of 6,000 seeds, worked out from Neow's lists.
- **The folder of real records**, when it is on the machine (`sts2-notes/golden-saves/history`, or `$ARENA_GOLDEN_SAVES`): every record of a single-player Ironclad run of this build whose pick at Neow is one of these relics and whose outcome the record states (the card gained, the gold gained, the max HP gained, the cards upgraded) is played and compared. Three today: Arcane Scroll, Pomander, Golden Pearl.

A mutation pass (84 one-line faults in `NeowCore.cs`: every number, stream, purpose, flag, order and guard, the list and a title; script `chunk14-tools/mutate.py` of the build session) was run against the checks: all 84 are caught.

`RealRunChecks` opens the registry's Neow on the start point of every act it walks and takes Arcane Scroll on the real seed; its scripted walker takes one of Neow's three relics in each of its whole acts and answers whatever the pickup asks.
