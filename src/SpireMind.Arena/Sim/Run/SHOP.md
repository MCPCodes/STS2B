# Run mode: the shop

What a merchant's room sells, what each thing costs, buying, the card removal and the `shop` screen. Chunk 10 of the run-mode plan (`sts2-notes/run-mode-v0.107.1/plan.md`, 1.5, 1.6 and section 2; `shop.json` next to it). The frame of a run is in `README.md`, the streams in `STREAMS.md`, the cards and potions a shop rolls in `REWARDS.md`, the relic pulls and the run's hooks in `RELICS.md`, the deck's removal screen in `DECK.md`.

Everything here is the game's (v0.107.1, single player, ascension 0), read from its code: `MegaCrit.Sts2.Core.Entities.Merchant/*` (the inventory and its four kinds of entry), `MegaCrit.Sts2.Core.Rooms/MerchantRoom.cs`, `MegaCrit.Sts2.Core.Factories/CardFactory.cs`, `PotionFactory.cs` and `RelicFactory.cs`, `MegaCrit.Sts2.Core.Multiplayer.Game/OneOffSynchronizer.cs` (the removal), `MegaCrit.Sts2.Core.Random/Rng.cs` (the ranged float draw). The screen is the mod's `src/SpireMind/Game/Screens/ShopReader.cs` and `docs/screens.md`, "Shop".

It is held against two real shops, from the game's own records of two runs (`sts2-notes/golden-saves/history`). From the seed and the place the run stood at, the draw order below gives both shelves slot for slot:

| Run | Before the shop | Shelf |
| --- | --- | --- |
| seed 1BUSLN0FY5, floor 5 (`1791755111.run`) | `rewards` at 26, pity offset 0.01, Stone Cracker held | Tear Asunder (on sale, 77 gold: what the record says was spent), Ashen Strike, Tremble, Blood Wall, Vicious; Discovery, Calamity; Paper Phrog, Pendulum, Ringing Triangle; Block Potion, Potion of Binding, Shackling Potion |
| seed 75F277YN2G, floor 3 (`1791592972.run`; an Underdocks run) | `rewards` at 11, pity offset -0.02 | Pommel Strike, Anger (on sale, 24 gold: the record's 99 spent less the removal's 75), Taunt, Second Wind, Juggling; Jack of All Trades, Secret Technique; Lantern, Red Skull, Burning Sticks; Stable Serum, Swift Potion, Weak Potion |

## Files

| File | Holds |
| --- | --- |
| `Shop.cs` | `ShopInventory` (the shelf and how it is made), the entries `ShopEntry`, `ShopCardEntry`, `ShopRelicEntry`, `ShopPotionEntry`, `ShopRemovalEntry`, `ShopPrices`, `ShopHooks` (the call sites of the shop's three hooks), `ShopState` (the run's count of removals bought), `ShopRoom.Enter` |
| `ShopScreen.cs` | `ShopScreen`: the `shop` screen |
| `ShopChecks.cs` | the checks |
| `../Engine/Listener.cs` (last region) | the three hooks themselves: `ModifyMerchantPrice`, `ShouldRefillMerchantEntry`, `AfterItemPurchased` |

## Using it

```csharp
// A director that travels (IMapDirector.EnterRoom), for arrival.Room == RoomKind.Shop:
ShopRoom.Enter(run);                              // rolls the stock, puts up the screen, tells the listeners "Shop room entered"
MapState.MarkRoomVisited(run, RoomKind.Shop);

// The screen's proceed takes the screen away and calls run.RoomFinished(): the director's AfterRoom opens the map.
```

A shop needs the run's world (`RunWorld.Begin`): its rolls are draws of the player's `shops` and `rewards` streams, and its relics come out of the player's bag. `ShopRoom.Enter` throws `RunUnrepresentableException` when the shelf cannot be filled from what the arena has built (below); the run ends itself as unrepresentable, standing as it stood before the action that entered the room.

## What a shop sells, and the order it is made in

`ShopInventory.CreateForNormalMerchant(run)` is the game's `MerchantInventory.CreateForNormalMerchant`, number for number:

1. **One number of `shops`: which of the five character cards is on sale** (0 to 4).
2. **Five cards of the character**, of the types Attack, Attack, Skill, Skill, Power. For each (`CardFactory.CreateForMerchant` by type):
   - the pool: the unlocked single-player cards of the character, without Basic cards and without the cards the shelf already shows;
   - a rarity on **`rewards`**: the shop table (Rare below 0.09 plus the pity offset, Uncommon below that plus 0.37, else Common). The pity offset is read and does not move. A rarity that has no card of the type gives way to the next (so the Ironclad's Power is never Common: he has no Common Power);
   - the pick on `shops`;
   - an upgrade roll on **`rewards`** that cannot succeed (its chance is far below zero) but takes its number;
   - the card hook (`ModifyMerchantCardCreationResults`: the Eggs upgrade it);
   - a price factor on `shops`. The card on sale then draws a **second** factor at once; its price is that one's, halved and rounded down.
3. **Two colorless cards**, an Uncommon and then a Rare one, from the unlocked single-player colorless cards the shelf does not show: the pick on `shops`, the upgrade roll on `rewards`, the hook, a price factor on `shops`. No rarity roll.
4. **Three relics.** First two rarities on **`rewards`** (Common below 0.5, Uncommon below 0.83, else Rare); the third place is of Shop rarity. Then, place by place: the next relic of the rarity from the **back** of the player's bag that a shop may sell (`RunRelics.PullFromBack`; five relics are never sold; an empty rarity gives way Shop, Common, Uncommon, Rare), and a price factor on `shops`. A relic leaves both bags when it is put on the shelf, bought or not.
5. **Three potions** rolled together on `shops` (`PotionFactory.CreateRandomPotionsOutOfCombat`: a rarity and a pick each, no two the same), then their three price factors.
6. **The card removal**, without a number.

So a shop takes **28 numbers of `shops`** (1 + 5 picks + 6 prices + 2 picks + 2 prices + 3 prices + 6 + 3) and **14 of `rewards`** (5 rarities + 7 upgrade rolls + 2 relic rarities).

Each roll names its place on the shelf for keyed streams: `Purpose.ShopSale`; `Purpose.ShopCard(slot)` with the character's cards 0 to 4 and the colorless ones 5 and 6 (a card asks for it twice: once for the `rewards` numbers, once for the pick); `Purpose.ShopPrice(slot)` over the whole shelf (cards 0 to 6, relics 7 to 9, potions 10 to 12; the card on sale asks twice); `Purpose.ShopRelicRarity(0)` and `(1)`; `Purpose.ShopPotion(0)` for the three potions together. A place that is filled again asks for its own purposes once more.

## Prices

A price is a table value times a rolled factor, in 32-bit floats, rounded to the nearest whole number with a half going to the even one (`ShopPrices`).

| Thing | Table value | Factor |
| --- | --- | --- |
| a card | Common 50, Uncommon 75, Rare 150 (any other rarity 50) | 0.95 to 1.05 |
| a colorless card | the card value times 1.15, rounded: Uncommon 86, Rare **172** | 0.95 to 1.05 |
| a relic | Common 175, Uncommon 225, Rare 275, Shop 200 | 0.85 to 1.15 |
| a potion | Common 50, Uncommon 75, Rare 100 | 0.95 to 1.05 |
| the card removal | 75, plus 25 for every removal bought in a shop before | none |

- The factor is the game's ranged float draw: the stream's fraction times the width of the range (a float subtraction) plus the lower end, worked out in doubles and narrowed to a float (`ShopPrices.Factor`).
- The Rare colorless value is 172, not the 173 a calculator gives: 150 times 1.15 is exactly 172.5 in a 32-bit float, and the game's rounding (Godot's `RoundToInt`, which is .NET's `MathF.Round`) sends a half to the even number. The plan's `shop.json` says 173; the code decides.
- The card on sale costs half of its second rolled price, rounded down.
- The removal's price is fixed when the shop is made. Only a removal bought in a shop makes the next one dearer (`ShopState.CardRemovalsUsed`); a removal an event or a relic gives does not.
- **What the player pays is `entry.Cost(run)`**: the rolled price (`BaseCost`) run through every listener's `ModifyMerchantPrice` in turn and cut to a whole number (cut, not rounded). The removal's price goes through the hook too. `entry.EnoughGold(run)` is "the price is at most the gold".

## Buying

`ShopInventory.TryPurchase(run, entry)` is the game's purchase (`MerchantEntry.OnTryPurchaseWrapper`). An empty place and a price above the gold fail at once. Then, by kind:

| Kind | What happens, in order | Fails, with nothing paid, when |
| --- | --- | --- |
| a card | the card goes into the deck (`DeckEdits.Add`: the card-added hooks); the gold is taken | the deck did not take it |
| a relic | the gold is taken; the relic is obtained (`run.ObtainRelic`: its pickup effect, which may ask the player something or put a screen over the shop) | never |
| a potion | the potion goes into the belt (`RunPotions.TryToProcure`); the gold is taken | the belt is full, or a hook keeps the potion out |
| the removal | the player is asked which card (`CardSelect.FromDeckForRemoval`, one card, can be closed, the pick is confirmed); the gold is taken; the card is removed (`DeckEdits.Remove`); the run's count goes up; the removal is used up | the screen was closed without a card |

After a purchase that went through:

1. The place is emptied, or filled again when a listener's `ShouldRefillMerchantEntry` says yes (the first yes decides; The Courier). The removal is never asked about. Filled again: a card place makes a new card of its type or rarity that the shelf does not show (the one just bought included) and is never on sale; a relic place rolls a new rarity on `rewards` and pulls from the back of the bag again, never a relic that is on the shelf; a potion place rolls any potion of the list, also one that is on the shelf (the game works out which potions are shelved and then does not use it). Each is priced anew.
2. `AfterItemPurchased` is heard, with the entry and the gold spent. For a relic the gold reported is the price read once more after the relic was obtained, so a relic that lowers prices reports its own lowered price although the full one was paid; that is the game's.
3. Every entry is brought up to date: each card still on the shelf is put to `ModifyMerchantCardCreationResults` again, one card at a time. That is how an Egg bought in a shop upgrades the cards beside it.

## The screen

`ShopScreen` writes what the mod's `ShopReader` writes.

- **State**, keys in this order: `run`, `boss` (when the run has a world), `gold`, `inventory_open`, `player` (`character`, `hp`, `max_hp`, `relics`, `deck_size`, `deck`: no gold, no potions), `potions` (the detailed slots), `stock`, `potion_slots_full` (only `true`: the belt is full and a potion is on the shelf), `last_purchase_failed` (only after a purchase that did not go through).
- **`stock`**: `cards`, `colorless`, `relics`, `potions`, each left out when nothing of the kind is stocked, and `removal` (`price`, `affordable`, nothing else) while it is not used up. An entry is `n`, then the card as an offer, the relic (`name`, `text`) or the potion (`name`, `text`, `target`), then `price` (after the price hooks), `on_sale` (only `true`, only character cards), `affordable`. **`n` is the place in the game's list of that kind**: a bought entry leaves a gap, the others keep their numbers.
- **One screen, two modes.** A visit starts closed: `open` ("Open the shop inventory to buy"), offered only when some stocked entry can be paid for, and `proceed` ("Leave the shop"). Open: a buy for every stocked entry the gold reaches (a potion only with a free slot), in the order cards, colorless cards, relics, potions, removal, then `close`. Never a buy and `proceed` in one snapshot. The potion ids follow the shop's own actions in both modes. The stock is listed in both.
- **Ids and labels**: `buy:card<n>` "Buy X for P gold" (plus " (on sale)"), `buy:colorless<n>` "Buy colorless X for P gold", `buy:relic<n>` "Buy relic X for P gold", `buy:potion<n>` "Buy potion X for P gold", `buy:remove` "Pay P gold to remove a card from your deck", `close` "Close the shop inventory".
- **Refusals**, the reader's, in its order: a potion id that is not offered (`'<id>' is not available now.`); `The shop inventory is already open.`; `The shop inventory is not open.`; `Close the shop inventory first.`; `Open the shop inventory first.`; `'<id>' is not in stock.` (also for a `buy:` id that names nothing); `Not enough gold for <name> (<price>).` (`... for a card removal (...)`); `Potion slots are full; use or discard a potion first.`; `No tip is showing.` for `dismiss`; `Unknown shop action '<id>'.`
- **`buy:remove`** asks its card as a question of the action (`run.Ask`): the `card_select` screen of kind `remove` is up, with `cancel`. `confirm` on the preview finishes the purchase. `cancel` on the grid ends it with nothing paid and the removal still stocked.
- **`last_purchase_failed`**: a buy the game turns down after it started is not a refusal. The action is accepted, and the next snapshot names what failed: the thing's title, or `a card removal`. The next shop action that succeeds clears it (`open`, `close`, `proceed`, a buy); potion actions do not.
- **A relic whose pickup opens a screen** (a card choice, a rewards list) puts it over the shop; the shop shows again when it is done.
- `proceed` takes the screen off the run's screens and calls `run.RoomFinished()`.

One house rule of the screen: `open` when nothing on the shelf can be paid for. The reader does not offer the id then and has no refusal of its own for it; in the game the mod's bridge turns down any id the fresh snapshot does not offer. The screen refuses with the bridge's words, `'open' is no longer available on the shop screen.`, so that an id that is not offered is never accepted.

## The hooks

| Hook | Declared on | Sent from |
| --- | --- | --- |
| `ModifyMerchantPrice` | `Listener` | `ShopHooks.ModifyMerchantPrice`: `ShopEntry.Cost`, whenever a price is read |
| `ShouldRefillMerchantEntry` | `Listener` | `ShopHooks.ShouldRefillMerchantEntry`: `ShopInventory.TryPurchase`, after a purchase that is not the removal's |
| `AfterItemPurchased` | `Listener` | `ShopHooks.AfterItemPurchased`: `ShopInventory.TryPurchase`, after every purchase |
| `ModifyMerchantCardPool`, `ModifyMerchantCardRarity` | `IRewardHooks` | `CardFactory.CreateForMerchant` |
| `ModifyMerchantCardCreationResults` | `IRewardHooks` | `ShopCardEntry.Populate` (a card was made) and `ShopCardEntry.UpdateEntry` (something was bought) |
| `AfterRoomEntered(RoomKind.Shop)` | `Listener` | `ShopRoom.Enter`, after the stock is made |
| the card-added hooks, the potion hooks, a relic's pickup | | `DeckEdits.Add`, `RunPotions.TryToProcure`, `run.ObtainRelic` |

The first two are among the 27 hooks of `RunHooks.Hooks`. The third has one user in the whole game, Maw Bank of Underdocks. The hooks are asked from `Snapshot` too (a price is read to be shown), so a price hook must not change anything.

## What the arena has not built

A real shop has no empty place. A roll that lands on something the arena has not built is drawn again by the rule of its factory, and written to the run's `LeftOutLog`; in strict mode the run ends as unrepresentable instead, and its reason names what the game's shelf holds there.

- A card: the pick is drawn again on `shops` (`CardPools.Pick`). The rarity is not rolled again.
- A potion: its rarity and pick are both drawn again on `shops` (`PotionFactory`).
- A relic: the next built relic of the same list from the back; when the list has none, a rolled rarity is rolled again on `rewards` (`RunRelics.PullFromBack`). The Shop-rarity place has no roll to repeat.

Every redraw shifts the numbers that follow on the same stream, so from the first redraw on the shelf is no longer the game's for that seed (`LeftOutLog.FirstFloor`). When a place can only be filled with unbuilt things (no built Uncommon or Rare colorless card, no built relic the place could show) the shop is not shown at all: `RunUnrepresentableException`. The character's five cards are always the game's own, since the whole pool is built.

## Left out, by name

- **`ShouldAllowMerchantCardRemoval`**: only the run modifier Hoarder says no, and run modifiers are left out.
- **Lord's Parasol** (buys the whole shelf on entering): a relic of Vakuu, the Ancient of act 3. **Foul Potion** thrown at the merchant: an event potion of act 2.
- **The Fake Merchant** (an event of act 2 or later with a shelf of its own, priced outside a merchant's room) and its relics' own prices.
- **Ascension**: the removal's 100 and 50 from the Inflation level on.
- **More than one player**: each player has a shelf of their own.
- **The merchant's tutorial** and tips drawn in the room, and the reader's waits while the panel slides: a profile that has seen the tutorials meets none of it. `dismiss` is refused as the reader refuses it with no tip up.
- **When a place is settled after a relic whose pickup opens a rewards list** (Orrery, Cauldron): the place is emptied, the purchase told and the shelf brought up to date at once, where the game does all three once that list has closed. The shop cannot be seen or acted on in between, and the draws come in the same order; no decision differs. A pickup that asks for a card is a question of the buy, so there the order is the game's.
- **What the game writes into its run history** when a shop is left (what was on the shelf, what was bought). The floor log is the run loop's.
- **The merchant's lines.** They are drawn from a stream that belongs to no run.

## Checks

`ShopChecks`: the two real shops against a second, straight-line reading of the game's draw order that calls nothing of the shop (their cards, relics, potions, the price of the card that was bought, 14 and 28 numbers); the same two shops from the shop itself (the five character cards with the sale and its price; in strict mode the run ends naming the first thing of the game's shelf that is not built); 300 rolled shops, each compared with the second reading entry by entry, price by price, with both stream counters, both bags and the log, and every price held to the range its table value and factor give; the relic places against a plain walk of the bag from its back; keyed streams (which stream every roll asks for; the same shelf whatever was rolled before); entering the room; the screen's keys, order, labels and refusals; buying each kind with every number worked out by hand on a shelf built by hand; potions and the belt; the removal with its question, backing out, the note and the price over three shops; the price hook's order and cut; refill, the purchase hook and the shelf brought up to date; copies and replays, also in the middle of the removal's question.

Checks that need a whole rolled shop ask first whether the arena can fill one from what is registered, and otherwise hold that the run ends as unrepresentable.

`RoomTracesChecks` is a second reader's (`ROOM-TRACES.md`): four shops of three runs item for item with all thirteen prices, the shop of a real run's record, eleven shops in which a price lies exactly on a half before it is rounded (which is what tells 32-bit from 64-bit arithmetic, and half-to-even from half-up, for a card, a relic and a potion), and six purchases in a shop whose bought places are filled again, each new card and potion with its price.
