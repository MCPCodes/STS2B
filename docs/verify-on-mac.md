# Verify on the Mac

The screen readers under `src/SpireMind/Game/Screens/` were written without the game DLLs. Every guess about a game member carries a `// VERIFY:` comment; this is the deduplicated list, for the session on Mark's Mac that builds against v0.107.1 and has `~/Projects/sts2-decompiled` to hand. Fix compile errors first (they settle many of these on their own), then work through the rest against the decompile, and delete each `// VERIFY` comment once it is settled. `grep -rn VERIFY src/` lists what is left.

Line numbers are as of this commit. Items checked in one place but used in several files are under "Shared" with every location.

**Status (2026-10-09, on the Mac):** every item below is settled against the v0.107.1 decompile and no `// VERIFY` comment is left in `src/`. Each file group had one pass that settled its items and a second that reviewed clicks, enqueues, gates and co-op safety. What turned out wrong, the checks only the running game can make, and what is still open are in the three sections at the end.

## Shared

- [x] **NClickableControl** has `bool IsEnabled` and `ForceClick()`, and is the base of the game's buttons. The readers assume it for: `NConfirmButton` (MiscReaders.cs:453), the FTUE confirm button (MiscReaders.cs:275), the game over buttons (MiscReaders.cs:38), the merchant's `%BackButton` (ShopReader.cs:555), the card select screens' `%Confirm`, `%Close`, `SkipButton` and bundle `Hitbox` (CardSelectReader.cs:530, 825, 962, 1138), rest site option buttons from `GetButtonForOption` (RestSiteReader.cs:255), the treasure `Chest` node and `NTreasureRoomRelicHolder` (TreasureReader.cs:581). General note at MiscReaders.cs:544.
- [x] **NCardRewardAlternativeButton.IsEnabled** exists, NButton-style (CardRewardReader.cs:152).
- [x] **Room proceed buttons**: `NMerchantRoom.ProceedButton` (ShopReader.cs:300, 396; usable only while the inventory is closed), `NRestSiteRoom.ProceedButton` (RestSiteReader.cs:180), `NTreasureRoom.ProceedButton` (TreasureReader.cs:168) all exist and are `NProceedButton` (IsEnabled, ForceClick).
- [x] **Card holders expose `CardModel`**: `NGridCardHolder` (CardRewardReader.cs:202, CardSelectReader.cs:814) and the elements of `NPlayerHand.ActiveHolders`, which derive from `NCardHolder` (CombatReader.cs:436).
- [x] **MapCoord** exposes lowercase `col` / `row` (MapReader.cs:273, RewardsReader.cs:446).
- [x] **EncounterModel.Title** is a LocString with the boss's name, for `ActModel.BossEncounter` and `SecondBossEncounter` (MapReader.cs:175, 177; RestSiteReader.cs:298).
- [x] **FTUE namespace**: tutorial popups and the merchant's tip are classes in `MegaCrit.Sts2.Core.Nodes.Ftue` (or have "Ftue" in the class name) (MiscReaders.cs:270, ShopReader.cs:49).
- [x] **TargetType** is the enum behind both `CardModel.TargetType` (CombatReader.cs:191) and `PotionModel.TargetType`, with `TargetedNoCreature` and `RandomEnemy` members (PotionActions.cs:115).

## ScreenRouter.cs

- [x] :102 A rewards screen in a treasure room, a shop (Orrery, Cauldron) or an event leaves `NOverlayStack` once its proceed is pressed. One that stays (like the terminal combat rewards that sit under the map) would hide the room below it, and the router would need to skip it.
- [x] :128 `CombatManager.IsInProgress` turns false once a fight is over, so an event page shown again after the event's fight routes to EventReader, not CombatReader.
- [x] :134 The merchant inventory is not an `NOverlayStack` screen; the router expects `GetCurrentScreen()` to report it and falls back to `RoomType.Shop`.

## Screens/CombatReader.cs

- [x] :166 `NCombatRoom.Mode` exists and its live-fight value is named `ActiveCombat` (compared by name).
- [x] :204 Whether `AnyAlly` targets can include pets (Osty); only other players' creatures are offered now.
- [x] :489 Name of `NPlayerHand`'s member holding the cards picked so far in select mode (tries `_selectedCards`, `SelectedCards`, `_selectedHolders`, `_selectedCardHolders`).
- [x] :510 `NPlayerHand` keeps the CardSelectCmd filter as a `Func<CardModel, bool>` or `Predicate<CardModel>` field in select mode.
- [x] :638 `AttackIntent.GetTotalDamage` returns a number castable to int.
- [x] :680 Orb models expose `Id.Entry`; `PassiveVal` / `EvokeVal` are numbers castable to int.
- [x] :702 `PlayerCombatState.Pets` enumerates `Creature`.
- [x] :712 The Regent's character model class is named `Regent`.
- [x] :734 `Hook.ShouldAllowTargeting` takes `ICombatState` (or needs the concrete `CombatState`).

## Screens/PotionActions.cs

- [x] :109 The enum behind `PotionModel.Usage` is named `PotionUsage`.
- [x] :112 `PotionModel.PassesCustomUsabilityCheck` is a property, not a method.
- [x] :123 `PotionModel.IsValidTarget(Creature?)` signature.

## Screens/MapReader.cs

- [x] :177 The map shows humans the second boss before the first is beaten (else drop `second_boss` until then).
- [x] :183 `ActModel.Ancient` has a `Title`, and the map shows humans which Ancient it is before arrival.
- [x] :281 `MapPointType` member names (Monster, Elite, RestSite, Shop, Treasure, Unknown, Boss, Ancient); anything else is lowercased.
- [x] :308 `MapPoint.Quests` is a collection, non-empty when a quest marker is drawn on the point.
- [x] :345 `MapPoint.Children` enumerates `MapPoint` (or `MapCoord`).
- [x] :400 `MapTravel`'s namespace is in the global usings (expected `MegaCrit.Sts2.Core.Map`).

## Screens/RewardsReader.cs

- [x] :305 `GoldReward.Amount` is an int.
- [x] :343 `SpecialCardReward` exposes its card as a member named `Card`.
- [x] :392 The reward base type has a `Description` or `Title` (LocString), used for reward kinds not special-cased.
- [x] :471 `CardReward.Cards` elements are `CardModel` (a wrapper with `.Card` is handled too).

## Screens/CardSelectReader.cs

- [x] :207 `NDeckCardSelectScreen`'s preview back button is `%PreviewCancel` (offered only when found).
- [x] :210 A multi-card upgrade uses `%UpgradeMultiPreviewContainer`, by analogy with enchant.
- [x] :463 `NCardGrid.GetCardHolder(CardModel)` returns the holder, null when scrolled out of view.
- [x] :529 Deck screens with several picks confirm from `%Confirm` too (documented only for the simple and combat-pile grids).
- [x] :557 `NCardGrid` (or else the screen) keeps every card it shows in an `IEnumerable<CardModel>` field, ideally `_cards`.
- [x] :563 The grid screens keep the picked-so-far cards (or holders) in a field whose name contains "selected".
- [x] :586 The preview containers' back button is named `Cancel` (offered only when found).
- [x] :592 `NDeckCardSelectScreen`'s previewed card sits under `%PreviewContainer` (only used to describe it).
- [x] :719 The selection screens keep their `CardSelectorPrefs` in a field (type name contains "Prefs") with `Prompt`, `MinSelect`, `MaxSelect`.
- [x] :735 `NDeckEnchantSelectScreen` holds its enchantment in a field whose type name contains "Enchantment", with `Title` and `Description`.
- [x] :824 `NChooseACardSelectionScreen`'s canSkip flag is a bool field named `_canSkip`.
- [x] :938 The bundle screen's back-out button is `%Cancel` (offered only when found).
- [x] :962 Bundle nodes have "Bundle" in their type name and expose `Hitbox`.
- [x] :1092 The draw pile's `PileType` member is named `Draw`.
- [x] :1127 `NCard` exposes its `CardModel` as `Model`.

## Screens/ShopReader.cs

- [x] :405 `NMerchantRoom.Inventory` is the inventory screen, a Godot Node (NMerchantInventory), and its `.Inventory` is the `MerchantInventory`.
- [x] :410 Whether `NMerchantInventory.Inventory` is already set while the inventory is closed (else the stock reads "unknown until opened").
- [x] :413 Shop entries: `IsStocked`, `Cost` (int) and `EnoughGold` are properties; `CreationResult.Card` exists on both card entry kinds; `IsOnSale` is on character card entries.
- [x] :546 The merchant inventory node's open flag (a member named `IsOpen`, if any).

## Screens/RestSiteReader.cs

- [x] :244 The element type of `NRestSiteRoom.Options` is named `RestSiteOption`.
- [x] :246 `RestSiteOption.OptionId` values (expected "HEAL", "SMITH", "MEND"...); a string or an enum both work.

## Screens/TreasureReader.cs

- [x] :71 How `NTreasureRoomRelicHolder` exposes its relic (member name; a `RelicModel` or an NRelic node with `.Model`).
- [x] :344 Which object declares `RelicPickingFinished()` (expected the node that parents the holders), that calling it once the relics show is harmless, and that its Task completes only after our relic is awarded.
- [x] :572 The `Chest` node stops reporting IsEnabled once opened (by us or a co-op teammate); while it reports enabled the bot treats the chest as closed.

## Screens/EventReader.cs

- [x] :333 `NAncientEventLayout` derives from the declared type of `NEventRoom.Layout` (else the `is` test doesn't compile).
- [x] :334 `NAncientEventLayout.DefaultFocusedControl` is a property that stays null until the Ancient's dialogue is over.
- [x] :344 `EventModel.Title` is the event's name (LocString), "Neow" and the like for Ancients.
- [x] :431 An Ancient's `EventOption` holds the offered relic in a member named `Relic`.
- [x] :433 `EventOption.HoverTips` enumerates the option's hover tips; card tips expose `Card`, relic tips `Relic`.
- [x] :512 `NEventLayout.OptionButtons` is an `IEnumerable<NEventOptionButton>` in on-screen order.
- [x] :554 `NEventOptionButton`'s `Index` member, if any, is the option's index in `CurrentOptions`.
- [x] :568 `Layout.OptionButtons` follows `CurrentOptions` order, one button per option, locked ones included.
- [x] :617 `NEventOptionButton` keeps its `EventOption` in a member named `Option` (or `_option`).
- [x] :626 `EventSynchronizer.GetLocalEvent()` returns the local player's `EventModel`, the same instance for the whole event.
- [x] :639 `CurrentOptions` enumerates `EventOption` (Title, Description, IsLocked, IsProceed, TextKey, WillKillPlayer).
- [x] :650 `EventModel`'s current page text is a member named `Description` that follows page changes.
- [x] :656 `EventOption.WillKillPlayer` is a bool property, not a method taking the player.
- [x] :675 `RunState.BaseRoom` is an `AbstractRoom` whose `RoomType` is `Event` for an event room.

## Screens/MiscReaders.cs

- [x] :42 Node names of the game over screen's continue and main menu buttons contain "Continue" or "MainMenu" (fallback when the classes at :38 differ).
- [x] :191 The game over screen's score label has "Score" in its node name (Label or RichTextLabel).

## Known follow-ups from the first compile check (not VERIFY comments)

- [x] Custom event nodes: FakeMerchant is the only custom layout in v0.107.1. Its event is finished from the start and `NEventRoom.Layout` is null, so no PROCEED button exists. EventReader now offers a single Leave through NFakeMerchant's `%ProceedButton` (the bot never buys there).
- [x] Failed main-menu Continue: with `NGame.MainMenu` up the router now always reports the menu, which says "a run is starting; if nothing happens, start or continue a run". This holds only if `SetUpSavedSingleplayer` itself throws (see open follow-ups).
- [x] Merchant inventory open: `GetCurrentScreen` returns `NMerchantInventory`, and the RoomType.Shop fallback handles it.

## What turned out wrong

- `EventOption.WillKillPlayer` is a `Func<Player, bool>?`, not a bool. Options that would kill are now refused, in solo too: in co-op the game silently refuses them, and offering them would hang the bot.
- `RelicModel.Description` and `PotionModel.Description` are private; use `DynamicDescription`. `CardModel.Title` already ends in "+" or "+N" when upgraded.
- `SpecialCardReward` keeps its card only in the private `_card`. `Reward` has `Description` but no `Title`.
- When an event is finished, `EventModel.CurrentOptions` is empty. The PROCEED option exists only on the button `NEventRoom.SetOptions` creates, so read it from `button.Option`. `NEventOptionButton.Option` and `EventModel.Description` are public, so no reflection is needed.
- An Ancient's dialogue only advances when `%DialogueHitbox` is clicked. Its options are offered about 0.5 s after arrival, and choosing one clicks through the remaining lines first.
- The transform and enchant screens confirm through a direct child named `Confirm`, not `%Confirm`; the upgrade screen has no screen-level confirm. On the deck-select and transform screens, the preview's back button throws on an empty selection, so it is offered only with a card picked.
- `NChooseABundleSelectionScreen`'s confirm dereferences the private `_selectedBundle` without a null check, so `%Confirm` is pressed only while it IsEnabled.
- `NPlayerHand` keeps its picks in the private `_selectedCards` and its filter in `_currentSelectionFilter`. Hand-select prompt, min and max now come from its private `_prefs` (the Harmony capture expires after 30 s, but hand select has no timeout).
- Enemy target ids indexed `cs.Enemies`, which drops dead and fled enemies, so `e1` could re-point at another monster while the model decided. A per-fight roster now keeps each enemy's index.
- In singleplayer, `CombatManager.IsPaused` is true while the map or a capstone is open mid-fight. It is now part of the may-act gate, because potions were being offered from the map.
- Treasure: the bot could cast a second relic vote when a human had already voted on its seat. It now checks `TreasureRoomRelicSynchronizer.GetPlayerVote(me).voteReceived` (call it only while `CurrentRelics != null`).
- Rest site: `OptionId` is a string (HEAL, SMITH, MEND with more than one player, COOK, DIG, LIFT, KINDLE, CLONE, HATCH). The heal amount comes from `HealRestSiteOption.GetHealAmount(Player)`. There is no rest-site completion sync in v0.107.1: leaving only opens the map, and only the map vote waits for teammates.
- `PotionUsage` also has `None`, which is skipped like `Automatic`.
- FTUE popups all derive from `NFtue`, and their confirm button is an `NFtueConfirmButton : NButton`. `NAcceptTutorialsFtue` stays on the never-touch list, because both of its answers start the run.
- No label holds the run's score on the game over screen; the snapshot computes it with `ScoreUtility.CalculateScore(runState, won)`.

## Check in the running game

- Combat: consecutive plays are not offered before the previous card resolves (the play tracker also ends on an energy or star change). `IsPaused` never sticks after closing the map or deck view. In co-op, a teammate killing `e0` leaves `play:x>e1` on the same monster. The hand-select prompt survives a decision longer than 30 s.
- Ancients: picking mid-dialogue grants the relic, the dialogue clears, and the Leave page follows.
- FTUEs on a fresh profile (the bot's `default/1000`): the map-select, rest-site, merchant, relic-reward and combat-rules FTUEs are each dismissed. The merchant FTUE takes the first proceed click, and a second proceed opens the map.
- Card reward with Driftwood and Pael's Wing: the alternatives read skip, reroll and sacrifice. Taking Calling Bell from boss rewards: its non-terminal screen offers a plain skip, and the terminal screen still offers the act vote once.
- Treasure: the log never shows "Attempted to pick relic while relic picking is not active!", and the bot never presses proceed while it reads Skip. In co-op, a relic clicked by hand on the bot's client is not voted again.
- Crystal Sphere end to end: each `cell:x,y` clears its area, rewards follow, and proceed opens the map.
- Transform and enchant screens with MinSelect != MaxSelect (Claws, Gnarled Hammer, Kifuda): early confirm appears only after enough picks, and Claws' empty confirm offers only "confirm".
- Game over: continue, the summary, then the main menu button (or the leaderboard on dailies).

## Still open

- A human opening the map from the top bar during combat rewards or in a shop: travel is already enabled there, so MapReader offers `map:n`, and voting leaves the room and skips unclaimed rewards. Fix idea: a bridge-level "the bot opened the map" flag, set by a room or event proceed and at act start, required before MapReader offers travel.
- A failed Continue whose error comes after `Transition.FadeOut` leaves `InTransition` stuck, so the busy gate hides the error popup. Rare.
- Treasure: if `AnimateRelicAwards` throws, `RelicPickingFinished` never completes and the bot idles in the room until a human leaves. In co-op, a relic press that never reached `PickRelicLocally` leaves the bot waiting on teammates; the `voteReceived` check would also be safe to use there.
- Rewards: a nested non-terminal rewards screen (Calling Bell, Orrery, Cauldron) resets the parent screen's per-screen memory. The act vote is still guarded by `Latches.ActVoteCastFor`.
- Shop: with Sozu, potion buys are offered and fail with FailureForbidden. With TheCourier, a bought slot restocks in place under the same `buy:` id, so only the snapshot version stops a stale repeat.
- Positional ids (`reward:n`, `option:n`, `card:n`, `buy:...`) are protected only by the snapshot version, which `act` checks when the caller passes it. `GameBridge.ExecuteAsync` itself only checks that the id is still offered.
- Card reward: `Clicks.Once` is keyed by Godot instance id, and `NGridCardHolder` instances are pooled, so a pick on a second card reward within 2.5 s of the first is refused once.
- Rest site: Mend (co-op) is never offered; a human has to do it.

## The in-game brain (2026-10-10)

Written and unit tested on Linux; the game-side wiring was only compiled against GodotSharp 4.5.0 without the game's assemblies. To check on the Mac:

- Smoke test, before the first in-game `/start`: `scripts/dev-host.sh --brain` (optionally `--model sonnet`) ends with PASSED. Expected: a few requests, a cache write on the first and cache reads on every one after (a second request reading 0 cached tokens means the prompt prefix is changing), about a cent in all on haiku, no 400s. `docs/brain.md` (Smoke test) has the details.
- The mod builds against v0.107.1 with the brain in it (`src/SpireMind.Core/Brain/`, `src/SpireMind/Game/BrainWiring.cs`). A name that clashes with a game type gets a file-level alias, as in the other Brain files.
- ModEntry.cs `// VERIFY`: `typeof(ModEntry).Assembly.Location` is set for the mod the game loads. The log line `brain: repo ... (from the mod's brain/repo_path)` shows it worked without `repo` in config.json. When it is empty the brain still runs, but only `repo` in config.json finds the checkout.
- The game loads the mod with the extra `brain/` folder next to the DLL (staged by `scripts/build.sh`).
- In game: `/help` lists /start, /stop, /budget, /model and /effort. `/start` with a key plays on the API, `/budget` shows the spend, `/stop` stops it.
- Subscription fallback (move `api_key` aside, then `/start` during a run): `claude auth status` passes, the session plays through `/mcp`, and `/stop` logs "asking Claude Code (pid N) to stop" then "Claude Code (pid N) exited with code C after the stop". With no "couldn't send SIGINT" line between them (and C other than 137, SIGKILL), the SIGINT through libc `kill` works from the mod; that line means the P/Invoke failed and it fell back to a kill. The "exited with code" line also shows Godot's own SIGCHLD handling doesn't keep `Process.Exited` from completing. A "killing Claude Code" line instead means Claude Code didn't exit within 5 s of the SIGINT; if "hasn't exited after the kill" follows it, `Process.Exited` never fired.
- With the subscription playing, end or quit the run and stay at the menu: 2 minutes later the log says "the run has been over for 120 s; stopping the subscription brain until the next run", and the next run starts it again.
- Fight effort, on the API brain at medium:
  - The first elite fight logs "brain: entered the Elite room; effort medium to high, starting a fresh conversation" once. In the first request after it, "cached" in its log line covers about the system prompt and tools, and "written" about the opening message only. A "written" the size of the system prompt as well means this model renders the effort ahead of tools and system (docs/brain.md, Fight effort); a second elite within the hour should read it.
  - The fight thinks at high: its requests write clearly more output tokens than a hallway fight's at medium, and the fight takes longer (the benchmark: about $0.70 and 2.1 minutes a fight at high, $0.47 and 0.8 at medium). If they look like medium's, the raise isn't taking.
  - The map after the rewards logs "left the Elite room; effort high to medium, keeping the conversation". The request after it isn't refused and reads most of the prompt from the cache (its log line's "cached" against "written"); a "request rejected ... with an effort message in it" line and a chat line saying Claude turned down the in-conversation effort change mean the beta was refused and the brain fell back to fresh conversations. The next map requests write fewer output tokens than the fight's.
  - `/effort` shows all three efforts.
- In co-op, the API brain's "system prompt built" log line lists `coop.md`.
- Quitting the game while Claude Code plays logs "brain: the game is closing; stopping the brains", and `pgrep -fl claude` afterwards finds no child left. Whether `AppDomain.ProcessExit` fires at all in the game is unknown; the root's `TreeExiting` is the one relied on.
- The usage-limit wording of CLI 2.1.287, when it comes up, matches `SubscriptionBrain`'s pattern (the chat then says the subscription hit its limit).
- In a fight, `/stop` while the bot is thinking over a move (the "..." cloud is up) takes the cloud down at once.
- The API brain's first `runs/current.md` starts `# <Character>, started <today's date>, Neow: ...`, and the next run archives it as `runs/<that date>-<character>.md`.
- Rules sheet and wiki, once `strategy/rules.md` and `strategy/wiki/` are in: the API brain's "system prompt built" log line lists `rules.md`. With the subscription playing, the log shows `claude calls Glob` and no `claude: permission denied for` line naming Glob or Edit under `dontAsk`, and a lesson Claude Code adds to a wiki page lands in the file.
- Speech hold: when the bot says a line and moves on outside a fight (a map node, a reward, an event option), the move comes 1.5 to 3 s after the line, and the bubble can be read before the room changes. In a fight it plays on at once.

## Co-op from the human window (to check in game)

Written and unit tested on Linux (`scripts/test.sh`), smoke tested with the dev host's fake bot and human copies on localhost, and the game-side files only compiled against stand-in game types. No `// VERIFY:` comment is left in `src/` for this work; every game member it uses (`InstancePoll`) is already in `docs/game-api.md`. To check on the Mac:

- Build: the mod builds against v0.107.1 with `src/SpireMind.Core/Coop/`, `Game/CoopWiring.cs`, `Game/CoopBotsAdapter.cs` and `Game/InstancePoll.cs`. Until the Mac's `src/SpireMind/Game/Coop/` files land, `SPIREMIND_COOP_BOTS` stays undefined and the adapter's stand-in compiles. Once `CoopBots` is in, delete the `#if SPIREMIND_COOP_BOTS`, the whole `#else` branch and the `#endif` in `Game/CoopBotsAdapter.cs`, then check it compiles against the real class: the tuple names `Port`, `NetId` and `Character`, `Known` as an `IReadOnlyList`, `StartCopyAsync(string?, CancellationToken)` returning `Task<string>`, and `ShowLine(ulong, string)`. `TryCreate` runs `CoopBots`' static constructor at mod load, before the main menu exists, so it must not touch game objects.
- Role: Mark's copy logs `role: human copy main on port 17772`, and a bot copy started with `--sts2bot=1` and `SPIREMIND_PORT=17773` logs `role: bot copy bot-17773 on port 17773; config in ~/.config/spiremind/bot-17773` with no warning. The role is read from Godot's `OS.GetCmdlineArgs()` as the game's own `CommandLineHelper` reads `--fastmp` (`docs/game-api.md`, section g), so the launcher must put `--sts2bot=1` with the other flags and never after a bare `--`.
- NetId: the bot copy logs `coop: this copy's player has NetId 1000` at character select and again in the run (the same id), and `curl -s http://127.0.0.1:17773/coop/info` shows `"role": "bot"`, `"net_id": 1000` and the character once it is picked.
- Discovery: with a bot copy running, Mark's window shows "Silent joined the chat (port 17773)." within about 5 s, and Mark's own log has `coop: linked the bot copy on port 17773`. With no bot running, nothing is shown and solo play is unchanged (`/start` plays as before).
- Talking: "Silent, go poison" typed in Mark's window shows in his log, reaches the bot (`curl -s 'http://127.0.0.1:17773/chat?since=0'` shows it), and the bot's answer comes back into Mark's log as "Silent: ..." in Silent's colour, once.
- Bubble: that answer also shows as a speech bubble over the bot's player on Mark's screen (`CoopBots.ShowLine` with the bot's NetId), and no bubble appears over Mark's own character for it.
- Commands: `/pause`, `/resume`, `/budget`, `/model`, `/effort`, `/start` and `/stop` in Mark's window each get one reply per bot, labelled with its character ("Silent: Silent paused. Type /resume to hand back control."). `/model Silent sonnet` reaches only Silent. `/help` ends with the co-op note. `/bots` lists each bot with port, NetId and what plays.
- `/bot add Regent`: with the Mac's `CoopBots` in, a bot copy starts, joins the lobby as Regent and is announced; without it, the chat says "This build can't start bot copies; start one with scripts/coop-bot.sh."
- Gating: in a co-op lobby and run Mark's copy never plays his hand: with autostart on, his log says "brain: autostart is on, but In co-op the bot copies play..." (or "brain: holding back: ..." from an API brain that was idling at the menu), nothing acts before the controller stops it, and `/start` typed while bots exist goes to the bots. The bot copy starts playing by itself once the run is in progress, without `/start`.
- Co-op detection: `Game/InstancePoll.cs` reads `NCharacterSelectScreen.Lobby.NetService.Type` at character select and `RunManager.Instance.NetService.Type` in a run, and counts Host or Client as co-op. Check that the lobby's net service has `Type` (it compiles), that a multiplayer lobby reads as co-op (`/pause` typed there goes to the bots) and that a singleplayer character select does not.
- Solo after co-op: leave a bot copy running and start a solo run in Mark's copy. `/start` plays Mark's own bot, `/pause` pauses it, his lines stay with it, and only `/model Silent sonnet` or "Silent, ..." reach the bot copy.
- Fight effort: `/effort boss max` in Mark's window during co-op shows each bot's reply with no "Couldn't save" note, and the bot's `~/.config/spiremind/bot-<port>/config.json` then has `"boss_effort": "max"`, still there after `/start` and after the bot copy is relaunched.
- Quitting: closing Mark's game with bot copies still running prints no `coop:` line after "brain: the game is closing".
- Closing and restarting a bot copy: Mark's window says "Silent's copy stopped answering." once within a few seconds, then "Silent's copy restarted." when it comes back, and its new lines show from the start of its new log.
- Party chat with two bots: a line one bot says reaches the other as a teammate message (its transcript shows `"teammate": true` in a tool result's chat) and never makes it send an extra request. `/party off` stops it.
- Spend: with two bots on the API, `~/.config/spiremind/spend.json` is the only spend file, its month total grows with both, and `in_flight` entries for `bot-17773` and `bot-17774` come and go. `/budget` in each says "this month across every copy".
- Journals: each bot writes `runs/bot-<port>/current.md` in the repo, and Mark's `runs/current.md` is untouched. A wiki or learned.md write by one bot after the other changed the same file is refused with `changed_since_read`, and the model reads it again and writes or appends.
