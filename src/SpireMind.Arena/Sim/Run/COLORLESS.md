# Run mode: colorless cards

The colorless card pool and the twelve cards of it that are built. Chunk 13 of the run-mode plan (`sts2-notes/run-mode-v0.107.1/plan.md`, "colorless-core"). The pool as an object, the pick that is drawn again and the transform rule are in `DECK.md`; a shop's card factory is in `REWARDS.md`.

## Files

| File | Holds |
| --- | --- |
| `../Content/ColorlessCore.cs` | the twelve cards, and every other card of the pool left out by name |
| `../Content/ColorlessCoreChecks.cs` | the checks |

## What is built

The game's colorless pool has 64 cards (`CardPools.Colorless`, in the game's order). Eleven are multiplayer only, so a single-player run can meet 53: 32 Uncommon and 21 Rare.

| Built (12) | |
| --- | --- |
| Uncommon (8 of 32) | Dramatic Entrance, Finesse, Fisticuffs, Impatience, Mind Blast, Ultimate Defend, Ultimate Strike, Volley |
| Rare (4 of 21) | Gold Axe, Master of Strategy, Salvo, Scrawl |

None of them needs a power the arena did not have: Salvo gives Retain Hand, the Stable Serum's power (`Potions.cs`).

Left out by name (`r.Exclude`, so `arena content` lists each with its reason):

- the eleven multiplayer-only cards (`ColorlessCore.MultiplayerOnly`);
- Splash, which offers Attacks of the other four characters;
- the twenty cards of chunk 27 (`ColorlessCore.WaitingForColorlessB`) and the twenty of chunk 28 (`WaitingForColorlessC`): not built yet. **Whoever builds one takes its name off that list**, or the registry reports that one file registers what another excludes.

## Where a run meets them

- **A shop's two colorless slots**, one Uncommon and one Rare: `CardFactory.CreateForMerchant(run, CardPools.Colorless.Unlocked(CardPools.UnlocksOf(run)), rarity, Purpose.ShopCard(slot))`. One number of the player's `shops` stream is an index into the pool's cards of that rarity; one number of `rewards` is the upgrade roll that cannot succeed.
- **A random transform** of a Quest card or of a card of Event, Ancient or Token rarity (`DeckEdits.TransformToRandom`): an index into all 53. A colorless card that is transformed becomes one of the other 52.

Both picks run over the game's full list and are drawn again while they land on a card that is not built (`CardPools.Pick`): every card passed over goes into the run's `LeftOutLog`, and a strict run ends there as unrepresentable. With today's twelve, a shop's Uncommon slot lands on a built card at once one time in four, its Rare slot one time in five.

## Colorless cards and the single fights

A single fight never deals or generates a colorless card: the scenario generator, Infernal Blade, Stoke and the card potions all pick from `Registry.IroncladPool`, which lists the registered cards **of the game's Ironclad pool** and nothing else. Before this chunk that property took every registered card of a rarity from Basic to Event, which was the same thing while only Ironclad cards had those rarities. `ColorlessCoreChecks` holds it (no colorless card in that list, 78 cards a fight may generate, forty Infernal Blades), and the byte comparison of single-fight snapshots is unchanged.

Whatever later makes colorless cards inside a fight (Colorless Potion, Jack of All Trades, Toolbox) has to pick from `CardPools.Colorless` with the pool's `GeneratedInCombat` flag, never from the registry's Ironclad list.

## The cards, where the game's code says more than the card does

- **Fisticuffs.** The Block is the sum, over what the hit did, of the damage the enemy's Block absorbed, the HP it lost and the damage beyond its last HP. So a Vulnerable enemy gives more, a Slippery one gives 1. It is given as a card's Block: Dexterity adds to it and Frail cuts it. A second run of the card on an enemy the first run killed hits nothing and still asks for a Block gain of 0, which Dexterity raises.
- **Gold Axe.** It counts the card plays finished in this fight: every run of a card's effect (a card played twice counts twice), cards played for free too. Its own play is not finished while it hits. The plain card has no keyword; upgraded it has Retain.
- **Mind Blast.** The count of the draw pile is taken when the hit lands, and is the hit's size before Strength and Vulnerable.
- **Volley.** One target is drawn per hit from the fight's target stream, also when one enemy stands. With no energy it is still an attack, of no hits.
- **Impatience.** Only Attacks in the hand count; the card itself has left the hand.
- **Scrawl.** Has Exhaust; upgraded it has Retain as well. **Salvo** has no Retain of its own.

`ColorlessCore.GainsBlock` names the three cards whose game class is marked as gaining Block (Finesse, Fisticuffs, Ultimate Defend): the enchantment Nimble asks for that mark, and the arena's card definition has no switch for it yet.

## Held against the real game

The game's own run records (`sts2-notes/golden-saves/history`, v0.107.1, single player) show the colorless cards of six shops: the first shop of the run on seed 1BUSLN0FY5 (Discovery, Calamity) and the five shops of the run on seed 75F277YN2G (Jack of All Trades and Secret Technique; Equilibrium and Entropy; Impatience and Entropy; Ultimate Strike and The Gambit; Flash of Steel and Jackpot). Only shops draw from the player's `shops` stream, and a shop takes 28 numbers of it when nothing bought is restocked: 1 for the card on sale, 2 for each of five character cards, 1 for the second price of the card on sale, 2 for each colorless card, 3 relic prices, 6 for three potions and 3 potion prices. The Uncommon colorless pick is number 13 of a shop's 28 and the Rare pick number 15, and all twelve cards are the pool's Uncommons and Rares at exactly those indexes. That holds the pool's order, its rarities and the single-player filter to the game, and it means the colorless cards of a seed's first shop are fixed by the seed alone. Where the shown card is built (Impatience at the third shop, Ultimate Strike at the fourth) the arena's own shop call gives it with one number.

## Left out, by name

- 52 of the pool's 64 cards, as listed above.
- Colorless cards made inside a fight (the in-combat half of the game's card factory over the colorless pool): Colorless Potion, Jack of All Trades and Toolbox bring it.
- A mark on a card definition for "gains Block" (see above).

## Checks

`ColorlessCoreChecks`: the twelve cards against their game classes (title, type, rarity, cost, target, keywords and tags, plain and upgraded, with the pool's own entry); the pool's lists typed in a second time from the game's files; every card's numbers and edge cases worked out by hand; live fights that stay in step with their copies and replay; the shop's two slots and the transforms against a second stream walked by the rule in words, drawing again and strict; the six real shops; a colorless card through a run's fight and back.
