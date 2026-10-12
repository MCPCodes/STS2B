# Run mode: deck edits, card choices and card pools

How the deck changes outside a fight, how a run asks the player for cards, and where a random card comes from. Chunk 6 of the run-mode plan (`sts2-notes/run-mode-v0.107.1/plan.md`, gaps 9, 11 and 12). The frame it stands on (state, screens, questions) is in `README.md`; the streams are in `STREAMS.md`.

Everything here is the game's (v0.107.1): `CardPileCmd`, `CardCmd`, `CardSelectCmd`, `CardFactory`, the four grid screens and the choose-a-card row under `MegaCrit.Sts2.Core.Nodes.Screens.CardSelection`, and the mod's `CardSelectReader` for what a snapshot shows (`docs/screens.md`, "Card selection").

## Files

| File | Holds |
| --- | --- |
| `DeckEdits.cs` | `DeckEdits` (add, remove, upgrade, transform), `DeckHooks` (the call sites of the deck hooks), `IDeckListener` (the deck's own hooks), `DeckAddResult`, `CardTransformation` |
| `CardSelect.cs` | `CardSelect` (the choices), `CardSelectPrefs`, `CardSelectScreen` (remove, upgrade, transform, select), `ChooseCardScreen` (choose), `SelectionSet` |
| `CardPools.cs` | `CardPool`, `CardPools` (the seven pools, a card's pool, what a card can be transformed into, the pick that is drawn again), `LeftOutLog`, `RunUnrepresentableException` |
| `CardPoolLists.cs` | the pools' lists with each card's type, rarity and flags. Made by `Tools/card_pools.py` from the decompiled game; run it again after a game update, never edit by hand |
| `DeckEditsChecks.cs`, `CardSelectChecks.cs`, `CardPoolsChecks.cs` | the checks (`CardPoolsChecks.cs` also holds `DeckCheckKit`, what the three share) |

## Using it

Everything takes the `Run` and is for outside a fight. During a fight `run.State` is the player as the fight began and the fight holds the live deck (`Fight.DeckList`), so the deck commands throw there.

```csharp
// Precise Scissors: the player removes one card.
foreach (var card in CardSelect.FromDeckForRemoval(run, CardSelectPrefs.Remove(1)))
    DeckEdits.Remove(run, card);

// Smith: one upgrade; the screen can be closed, and then nothing was chosen.
var picked = CardSelect.FromDeckForUpgrade(run, CardSelectPrefs.Upgrade(1) with { Cancelable = true, RequireManualConfirmation = true });
if (picked.Count == 0) return;            // backed out: the visit is not used up
DeckEdits.Upgrade(run, picked);

// Leafy Poultice: the first Strike and the first Defend, on the player's transformations stream.
var world = run.State.Part<RunWorld>();
DeckEdits.Transform(run, [new CardTransformation(strike), new CardTransformation(defend)],
    n => world.Streams.Draw(RunStream.Transformations, Purpose.Transform(n)));

// Hefty Tablet: one of three, or none; the card and an Injury go in together.
var chosen = CardSelect.FromChooseACardScreen(run, offered, canSkip: true);
DeckEdits.Add(run, chosen is null ? [injury] : [chosen, injury]);
```

A choice is a question of the run (`Run.Ask`, see `README.md`): the action that calls `CardSelect` is abandoned the first time and run again from its start once the player has answered. So keep all state in the run, and do not catch the exception that unwinds the action. What the action did before it asked is done once, and the question's screen shows the state as it stood when the question came up (the gold already paid, the card already removed).

## The deck commands, in the game's order

- **Add** (`DeckEdits.Add`, `AddNew`, `AddCurses`). Cards go to the END of the deck; no caller of the game adds anywhere else. For several cards in one call: every card is put to `ShouldAddToDeck` first; then each card that may go in passes `ModifyCardBeingAddedToDeck` (and its late pass) and is appended; only then `AfterCardChangedPiles` (and its late pass) is heard for each, with old pile `None`. A listener that says no is told `AfterAddToDeckPrevented`, and no listener after it is asked about that card. Nothing goes into the deck of a dead player (the game's own test; in a single-player run death ends the run, so it does not come up). `AddCurses` adds one curse at a time, hooks and all.
- **Remove** (`DeckEdits.Remove`). Card by card: `BeforeCardRemoved` while the card is still in the deck (the card itself hears it), then it leaves. Whether a card may be removed is asked by the screen that offers it (`DeckEdits.IsRemovable`: not Eternal), not by the command, as in the game.
- **Upgrade** (`DeckEdits.Upgrade`). No hook hears an upgrade. A card that cannot be upgraded further is passed over. A card on offer is upgraded the same way.
- **Transform** (`DeckEdits.TransformToRandom`, `TransformTo`, `Transform`). In the order given each original is checked, its place in the deck is noted, its replacement is rolled and the original leaves the deck at once, so a later original's place is counted without the earlier ones. Then the replacements go in, sorted by those places: each passes `ModifyCardBeingAddedToDeck` (not `ShouldAddToDeck`), is put at the END of the deck (not where the original was) and is heard as `AfterCardChangedPiles` with old pile `Deck`. `BeforeCardRemoved` is not heard for an original. An Eternal card in the deck cannot be transformed.
  - The sort is .NET's `List.Sort`, which the game uses and which does not keep equal places in order in every case (two neighbours transformed in deck order note the same place). For up to sixteen items it is rebuilt here: two items by one compare-and-swap, three by three (which can turn two equal ones around), more by insertion. More than sixteen at once are refused.
  - The roll: `CardPools.TransformOptions` gives the list (the card's own pool, or the colorless pool for a Quest card and for a card of Event, Ancient or Token rarity; unlocked cards of a single-player run; only Common, Uncommon and Rare unless the original is of Status or Curse rarity; never the original's own class), and one pick is made on the stream the caller hands over. Leafy Poultice rolls on the player's `transformations`, New Leaf on the run's `niche`, the events on their own stream.

## The hooks and who hears them

`DeckHooks` has one method per hook that is the deck's alone: `ShouldAddToDeck`, `AfterAddToDeckPrevented`, `AfterCardChangedPiles` (and its late pass), `BeforeCardRemoved`. A relic, a deck card's hook object or a potion's hooks takes part by implementing `IDeckListener`; each method is handed the run's `RunContext` like every hook of the run (`c.Run` is there, since these are heard outside a fight). The hook that changes a card on its way in is declared with the run's other hooks on `Listener` (`TryModifyCardBeingAddedToDeck(RunContext c, CardInstance card, out CardInstance? newCard)` and its late pass) and sent by `RunHooks.ModifyCardBeingAddedToDeck`, which `DeckEdits` calls: a relic overrides it there. In the game: the three Eggs and Fresnel Lens change a card on its way in; Lucky Fysh, Book of Five Rings and Darkstone Periapt hear a card arrive; Spoils Map hears its own removal. Nothing in v0.107.1 keeps a card out.

The order outside a fight has one definition, `run.Listeners()` (`Run.Commands.cs`, `RELICS.md`), the game's: the deck cards that listen in deck order, then the relics in pickup order, then the potions slot by slot. A deck card listens when its hook class carries the marker `IListensInDeck`; `IDeckListener` carries it, so a card's hook class that implements the interface is heard as the deck card (and may then override only hooks a deck card hears: `DeckListening`). The list is taken when a pass starts and each listener is asked only if it is still there when its turn comes; a late pass takes a new list. So a card a hook adds is not asked in the pass that was under way and is asked in the next.

## The choices

| Method | The game's | Screen | Kind |
| --- | --- | --- | --- |
| `FromDeckForRemoval` | FromDeckForRemoval | generic deck screen with preview | `remove` with a removal prompt, else `select` |
| `FromDeckGeneric` | FromDeckGeneric | the same | `select` (or `remove`, by the prompt) |
| `FromDeckForUpgrade` | FromDeckForUpgrade | upgrade screen with preview | `upgrade` |
| `FromDeckForTransformation` | FromDeckForTransformation | transform screen with preview | `transform` |
| `FromSimpleGrid` | FromSimpleGrid, FromSimpleGridForRewards | plain grid, no preview | `select` |
| `FromChooseACardScreen` | FromChooseACardScreen | the row of up to three | `choose` |

- **Which cards are listed.** Removal: every card that is not Eternal, the curses first, then the rest, each group in deck order. Upgrade: every card that can still be upgraded, in deck order. Transform: every card that is not a Quest card and not Eternal, in deck order. `card:<n>` indexes that list, not the deck.
- **When nothing is asked.** No candidate: nothing is chosen. No more candidates than the minimum: they are taken without a question, unless the prefs ask for manual confirmation (which every caller that can be backed out of sets: Smith, the shop's removal, a removal reward). A dead player is not asked for a generic deck pick.
- **The flow of a deck screen.** A pick toggles a card. The pick that reaches the maximum opens the preview (on the upgrade screen with a maximum of 1, every pick does); `confirm` there finishes; `cancel` there goes back to the cards and drops the whole selection. When minimum and maximum differ the grid has a `confirm` of its own that opens the preview early, also with nothing picked ("choose none": the preview is the text `open` and can only be confirmed). `cancel` on the grid closes the screen where the prefs allow it; while a preview is up the close button is off.
- **What a preview shows.** The generic deck screen: the picked cards. One upgrade: the card, then the upgraded card. Several upgrades: the upgraded cards only. A transform: the picked cards only, never what they become (the game cycles through the possible cards there and the mod skips that).
- **The plain grid.** No preview and no close button. The pick that reaches the maximum closes it, unless the prefs ask for confirmation; then a `confirm` finishes, and at the maximum only the picked cards and `confirm` are offered.
- **The order of the picks.** The game keeps the picks in a .NET HashSet and hands that set back, so `selected`, the preview, the confirm label and the cards the caller gets are in the set's order: a new pick takes the place the last deselected one left, else it goes to the end (`SelectionSet`).
- **The player block.** Full form; on a grid that lists every card of the deck the deck's name counts are left out and `deck_size` stays, at the end. No potion actions on any `card_select`.
- **Prompts** are the arena's own words (`CardSelectPrefs.Remove`, `Upgrade`, `Transform`, or the caller's text). Whether a prompt is one of the game's two removal prompts is a switch of the prefs (`IsRemovalPrompt`), because that is what the mod reads to tell `remove` from `select`.

## Card pools and the pick that is drawn again

`CardPools.Ironclad`, `Colorless`, `Curse`, `Event`, `Quest`, `Status` and `Token` hold the game's full lists in the game's order, with every card's type, rarity, multiplayer-only flag, may-a-fight-generate-it flag, may-a-random-pick-outside-a-fight-make-it flag (eight curses say no; Neow's Bones picks among the other ten) and epoch, whether the arena has built the card or not. `pool.Unlocked(unlocks)` is the game's list for a single-player run of a profile; `CardPools.Of(className)` is the pool a card belongs to.

A random pick must run over the game's full list, or leaving a card out would shift which index means which card. So: `CardPools.Pick(run, rng, options, what)` draws an index into the full list and draws again, from the same stream, while it lands on a card that is not built. Every card passed over is written to the run's `LeftOutLog` with the floor (`FirstFloor` is where the run stopped being the game's own run for its seed). Two cases end the run instead, by throwing `RunUnrepresentableException`: a list with no built card at all, and any landing on an unbuilt card when `LeftOutLog.Strict` is set (the game's streams, nothing drawn again). `CardPools.NextBuilt` is the same pick without a run.

What is built today: the Ironclad's pool (85 of its 87 cards; the two missing ones are multiplayer only and never in a single-player list), all 18 curses (`CURSES.md`), 12 of the 64 colorless cards (`COLORLESS.md`) and Neow's Fury of the event pool. So a transformed curse is always the game's own pick (any of the other 17, nothing drawn again), and a transformed Quest card or Event-, Ancient- or Token-rarity card becomes one of the built colorless cards, with every colorless card the pick passed over in the run's log.

## For whoever connects the chunks

- **Merged with the relic framework (wave 2).** The listener order is the run's (`run.Listeners()`), the card-being-added hook is `Listener`'s, and the deck's own hooks take a `RunContext`. `DeckEdits` calls the four `DeckHooks` methods and `RunHooks.ModifyCardBeingAddedToDeck`, nothing else.
- **`RunUnrepresentableException` is caught by the run itself** (`Run.Perform`, since the merge of wave 2). Thrown inside an action outside a fight, in what follows a won fight or in the director's first step, it ends the run as `unrepresentable` with its text as the reason; the run stands as before that action and the action stays in the save, so a replay ends the same way. Nobody else catches it.
- **`LeftOutLog`** is meant for every kind of thing that is drawn again (cards here; relics, potions, events elsewhere): `PassedOver(state, kind, className, what)`.

## Left out, by name

- Adding a card anywhere but the end of the deck, and the `clonedBy` argument of AfterCardChangedPiles: no caller in reach uses either (only a run modifier and Bing Bong, a relic of an act 2 event).
- Re-adding a card that was removed: the game refuses a card it has marked as removed from the run; the arena keeps no such mark and no caller does it.
- `FloorAddedToDeck` and the run history entries (cards gained, removed, upgraded, transformed, choices): they feed the game's run history screen only. A floor log is the run loop's.
- `AfterTransformedFrom` and `AfterTransformedTo`: only a Regent card overrides them.
- A transform with a list of options of the caller's own (no caller in the game), and the transform screen's preview of a NAMED replacement (only Claws, a relic of an act 3 Ancient). A named transform itself is there: `DeckEdits.TransformTo` and `Transform`.
- The `enchant` kind (chunk 31) and the `bundle` kind (Scroll Boxes, chunk 22) of `card_select`; the `enchant` key of a card entry.
- `CardSelectorPrefs.Comparison` (a sort of the plain grid): only the Sealed Deck run modifier sets it.
- Cloning a deck card (Dolly's Mirror, the Eggs): `Listener.TryModifyCardBeingAddedToDeck` hands back the replacement card; making an exact copy of a deck card with an id of its own is left to the chunks that need it.
- Deck edits from inside a fight other than a removal. A removal is built: `Fight.RemoveFromDeck(deckCard)` (`../Engine/Fight.Deck.cs`) sends "before card removed" to the run's listeners while the card is still in the deck, as the game's one command does in a fight and outside (Guilty leaves the deck this way after its fifth fight, `CURSES.md`). Adding a card to the deck from inside a fight with its hooks is not built: nothing in reach does it.
- The other four characters' pools and the pool of withdrawn cards: no card of the seven pools is in any of them, so a card's pool is found the same without them.

## Held against the real game

- The Ironclad's list in its order, with wave 1's streams and odds, gives the cards the real game gave on seed 1BUSLN0FY5 (Mark's saves and run history of 2026-10-11): Arcane Scroll's one pick among the 25 Rares is Barricade; the first fight offered Colossus, Unrelenting and Havoc (11 gold, no potion, `rewards` at 12), the second Fight Me!, Stone Armor and Setup Strike (10 gold, a potion, `rewards` at 25). A pick is an index into the list, so one card too many, too few or out of place would give other cards.
- The deck of that run after Neow, after each fight, after the event (the curse Clumsy) and after the shop is the starter with each card obtained appended at the end, in order: what `DeckEdits.Add` does.
- The order of the preview of an upgrade (the card, then the upgraded card) is read from the game's scene files (`scenes/cards/upgrade_preview.tscn` and the transform screen: the node Before comes ahead of After) together with the mod's reader, which lists the cards under the preview in tree order. It has not been seen on a recorded snapshot yet.

## Checks

`CardPoolsChecks`: the seven lists against the game's counts, the single fights' own lists and the plan's counts; a card's pool; what a profile can meet; what each kind of card can be transformed into; the pick against a second stream walked by the rule in words; the log and strict mode. `DeckEditsChecks`: every command's hook sequence written out by hand from the game's code, the listener order, what happens when a hook changes the listeners, the sort's three-item case, random replacements against a second stream and a list built from the single fights' own, deck commands inside the actions of a run on the run's streams (game and keyed), and the deck of Mark's real run on seed 1BUSLN0FY5 card by card. `CardSelectChecks`: each kind through its whole flow with every snapshot written out by hand from `docs/screens.md`, the refusals in the reader's words, copies and replays in the middle of a choice, two choices in one action, and the selection set against a real HashSet.
