# Verify on the Mac

The screen readers under `src/SpireMind/Game/Screens/` were written without the game DLLs. Every guess about a game member carries a `// VERIFY:` comment; this is the deduplicated list, for the session on Mark's Mac that builds against v0.107.1 and has `~/Projects/sts2-decompiled` to hand. Fix compile errors first (they settle many of these on their own), then work through the rest against the decompile, and delete each `// VERIFY` comment once it is settled. `grep -rn VERIFY src/` lists what is left.

Line numbers are as of this commit. Items checked in one place but used in several files are under "Shared" with every location.

## Shared

- [ ] **NClickableControl** has `bool IsEnabled` and `ForceClick()`, and is the base of the game's buttons. The readers assume it for: `NConfirmButton` (MiscReaders.cs:453), the FTUE confirm button (MiscReaders.cs:275), the game over buttons (MiscReaders.cs:38), the merchant's `%BackButton` (ShopReader.cs:555), the card select screens' `%Confirm`, `%Close`, `SkipButton` and bundle `Hitbox` (CardSelectReader.cs:530, 825, 962, 1138), rest site option buttons from `GetButtonForOption` (RestSiteReader.cs:255), the treasure `Chest` node and `NTreasureRoomRelicHolder` (TreasureReader.cs:581). General note at MiscReaders.cs:544.
- [ ] **NCardRewardAlternativeButton.IsEnabled** exists, NButton-style (CardRewardReader.cs:152).
- [ ] **Room proceed buttons**: `NMerchantRoom.ProceedButton` (ShopReader.cs:300, 396; usable only while the inventory is closed), `NRestSiteRoom.ProceedButton` (RestSiteReader.cs:180), `NTreasureRoom.ProceedButton` (TreasureReader.cs:168) all exist and are `NProceedButton` (IsEnabled, ForceClick).
- [ ] **Card holders expose `CardModel`**: `NGridCardHolder` (CardRewardReader.cs:202, CardSelectReader.cs:814) and the elements of `NPlayerHand.ActiveHolders`, which derive from `NCardHolder` (CombatReader.cs:436).
- [ ] **MapCoord** exposes lowercase `col` / `row` (MapReader.cs:273, RewardsReader.cs:446).
- [ ] **EncounterModel.Title** is a LocString with the boss's name, for `ActModel.BossEncounter` and `SecondBossEncounter` (MapReader.cs:175, 177; RestSiteReader.cs:298).
- [ ] **FTUE namespace**: tutorial popups and the merchant's tip are classes in `MegaCrit.Sts2.Core.Nodes.Ftue` (or have "Ftue" in the class name) (MiscReaders.cs:270, ShopReader.cs:49).
- [ ] **TargetType** is the enum behind both `CardModel.TargetType` (CombatReader.cs:191) and `PotionModel.TargetType`, with `TargetedNoCreature` and `RandomEnemy` members (PotionActions.cs:115).

## ScreenRouter.cs

- [ ] :102 A rewards screen in a treasure room, a shop (Orrery, Cauldron) or an event leaves `NOverlayStack` once its proceed is pressed. One that stays (like the terminal combat rewards that sit under the map) would hide the room below it, and the router would need to skip it.
- [ ] :128 `CombatManager.IsInProgress` turns false once a fight is over, so an event page shown again after the event's fight routes to EventReader, not CombatReader.
- [ ] :134 The merchant inventory is not an `NOverlayStack` screen; the router expects `GetCurrentScreen()` to report it and falls back to `RoomType.Shop`.

## Screens/CombatReader.cs

- [ ] :166 `NCombatRoom.Mode` exists and its live-fight value is named `ActiveCombat` (compared by name).
- [ ] :204 Whether `AnyAlly` targets can include pets (Osty); only other players' creatures are offered now.
- [ ] :489 Name of `NPlayerHand`'s member holding the cards picked so far in select mode (tries `_selectedCards`, `SelectedCards`, `_selectedHolders`, `_selectedCardHolders`).
- [ ] :510 `NPlayerHand` keeps the CardSelectCmd filter as a `Func<CardModel, bool>` or `Predicate<CardModel>` field in select mode.
- [ ] :638 `AttackIntent.GetTotalDamage` returns a number castable to int.
- [ ] :680 Orb models expose `Id.Entry`; `PassiveVal` / `EvokeVal` are numbers castable to int.
- [ ] :702 `PlayerCombatState.Pets` enumerates `Creature`.
- [ ] :712 The Regent's character model class is named `Regent`.
- [ ] :734 `Hook.ShouldAllowTargeting` takes `ICombatState` (or needs the concrete `CombatState`).

## Screens/PotionActions.cs

- [ ] :109 The enum behind `PotionModel.Usage` is named `PotionUsage`.
- [ ] :112 `PotionModel.PassesCustomUsabilityCheck` is a property, not a method.
- [ ] :123 `PotionModel.IsValidTarget(Creature?)` signature.

## Screens/MapReader.cs

- [ ] :177 The map shows humans the second boss before the first is beaten (else drop `second_boss` until then).
- [ ] :183 `ActModel.Ancient` has a `Title`, and the map shows humans which Ancient it is before arrival.
- [ ] :281 `MapPointType` member names (Monster, Elite, RestSite, Shop, Treasure, Unknown, Boss, Ancient); anything else is lowercased.
- [ ] :308 `MapPoint.Quests` is a collection, non-empty when a quest marker is drawn on the point.
- [ ] :345 `MapPoint.Children` enumerates `MapPoint` (or `MapCoord`).
- [ ] :400 `MapTravel`'s namespace is in the global usings (expected `MegaCrit.Sts2.Core.Map`).

## Screens/RewardsReader.cs

- [ ] :305 `GoldReward.Amount` is an int.
- [ ] :343 `SpecialCardReward` exposes its card as a member named `Card`.
- [ ] :392 The reward base type has a `Description` or `Title` (LocString), used for reward kinds not special-cased.
- [ ] :471 `CardReward.Cards` elements are `CardModel` (a wrapper with `.Card` is handled too).

## Screens/CardSelectReader.cs

- [ ] :207 `NDeckCardSelectScreen`'s preview back button is `%PreviewCancel` (offered only when found).
- [ ] :210 A multi-card upgrade uses `%UpgradeMultiPreviewContainer`, by analogy with enchant.
- [ ] :463 `NCardGrid.GetCardHolder(CardModel)` returns the holder, null when scrolled out of view.
- [ ] :529 Deck screens with several picks confirm from `%Confirm` too (documented only for the simple and combat-pile grids).
- [ ] :557 `NCardGrid` (or else the screen) keeps every card it shows in an `IEnumerable<CardModel>` field, ideally `_cards`.
- [ ] :563 The grid screens keep the picked-so-far cards (or holders) in a field whose name contains "selected".
- [ ] :586 The preview containers' back button is named `Cancel` (offered only when found).
- [ ] :592 `NDeckCardSelectScreen`'s previewed card sits under `%PreviewContainer` (only used to describe it).
- [ ] :719 The selection screens keep their `CardSelectorPrefs` in a field (type name contains "Prefs") with `Prompt`, `MinSelect`, `MaxSelect`.
- [ ] :735 `NDeckEnchantSelectScreen` holds its enchantment in a field whose type name contains "Enchantment", with `Title` and `Description`.
- [ ] :824 `NChooseACardSelectionScreen`'s canSkip flag is a bool field named `_canSkip`.
- [ ] :938 The bundle screen's back-out button is `%Cancel` (offered only when found).
- [ ] :962 Bundle nodes have "Bundle" in their type name and expose `Hitbox`.
- [ ] :1092 The draw pile's `PileType` member is named `Draw`.
- [ ] :1127 `NCard` exposes its `CardModel` as `Model`.

## Screens/ShopReader.cs

- [ ] :405 `NMerchantRoom.Inventory` is the inventory screen, a Godot Node (NMerchantInventory), and its `.Inventory` is the `MerchantInventory`.
- [ ] :410 Whether `NMerchantInventory.Inventory` is already set while the inventory is closed (else the stock reads "unknown until opened").
- [ ] :413 Shop entries: `IsStocked`, `Cost` (int) and `EnoughGold` are properties; `CreationResult.Card` exists on both card entry kinds; `IsOnSale` is on character card entries.
- [ ] :546 The merchant inventory node's open flag (a member named `IsOpen`, if any).

## Screens/RestSiteReader.cs

- [ ] :244 The element type of `NRestSiteRoom.Options` is named `RestSiteOption`.
- [ ] :246 `RestSiteOption.OptionId` values (expected "HEAL", "SMITH", "MEND"...); a string or an enum both work.

## Screens/TreasureReader.cs

- [ ] :71 How `NTreasureRoomRelicHolder` exposes its relic (member name; a `RelicModel` or an NRelic node with `.Model`).
- [ ] :344 Which object declares `RelicPickingFinished()` (expected the node that parents the holders), that calling it once the relics show is harmless, and that its Task completes only after our relic is awarded.
- [ ] :572 The `Chest` node stops reporting IsEnabled once opened (by us or a co-op teammate); while it reports enabled the bot treats the chest as closed.

## Screens/EventReader.cs

- [ ] :333 `NAncientEventLayout` derives from the declared type of `NEventRoom.Layout` (else the `is` test doesn't compile).
- [ ] :334 `NAncientEventLayout.DefaultFocusedControl` is a property that stays null until the Ancient's dialogue is over.
- [ ] :344 `EventModel.Title` is the event's name (LocString), "Neow" and the like for Ancients.
- [ ] :431 An Ancient's `EventOption` holds the offered relic in a member named `Relic`.
- [ ] :433 `EventOption.HoverTips` enumerates the option's hover tips; card tips expose `Card`, relic tips `Relic`.
- [ ] :512 `NEventLayout.OptionButtons` is an `IEnumerable<NEventOptionButton>` in on-screen order.
- [ ] :554 `NEventOptionButton`'s `Index` member, if any, is the option's index in `CurrentOptions`.
- [ ] :568 `Layout.OptionButtons` follows `CurrentOptions` order, one button per option, locked ones included.
- [ ] :617 `NEventOptionButton` keeps its `EventOption` in a member named `Option` (or `_option`).
- [ ] :626 `EventSynchronizer.GetLocalEvent()` returns the local player's `EventModel`, the same instance for the whole event.
- [ ] :639 `CurrentOptions` enumerates `EventOption` (Title, Description, IsLocked, IsProceed, TextKey, WillKillPlayer).
- [ ] :650 `EventModel`'s current page text is a member named `Description` that follows page changes.
- [ ] :656 `EventOption.WillKillPlayer` is a bool property, not a method taking the player.
- [ ] :675 `RunState.BaseRoom` is an `AbstractRoom` whose `RoomType` is `Event` for an event room.

## Screens/MiscReaders.cs

- [ ] :42 Node names of the game over screen's continue and main menu buttons contain "Continue" or "MainMenu" (fallback when the classes at :38 differ).
- [ ] :191 The game over screen's score label has "Score" in its node name (Label or RichTextLabel).

## Known follow-ups from the first compile check (not VERIFY comments)

- Custom event nodes (NFakeMerchant and friends) report `CustomEventNode.CurrentScreenContext`, so the router falls through to EventReader with `NRun.Instance.EventRoom`. Check EventReader copes when the layout has no normal option buttons.
- After a failed main-menu Continue, `RunManager.IsInProgress` can stay true with `NRun` null, and the router reports "run loading" forever. Add a guard if it shows up.
- With the merchant inventory open, `GetCurrentScreen` returns `NMerchantInventory`; the router's RoomType.Shop fallback handles it.
