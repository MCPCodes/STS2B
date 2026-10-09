# In-game chat and character persona: what v0.107.1 offers

Findings for the in-game chat feature (Mark types in the game; the bot answers in character as the character it plays). Gathered from the v0.107.1 decompile on Mark's Mac by a researcher and a skeptic per topic; full notes with file:line evidence are in `~/Projects/sts2-notes/api-v0.107.1/chat/{speech,input,coop,persona}.md`. Names and behaviour only; no game code or text is copied here.

## The short version

- The game has **no text chat** of any kind (no chat message, no lobby chat). Co-op players can only send 8 fixed emotes, an end-turn "hurry" ping (a fixed banter line in a speech bubble), map pings and drawings, hovers.
- It does have **speech and thought bubbles** that take plain strings: `NSpeechBubbleVfx` and `NThoughtBubbleVfx`. They are local, visual-only nodes; every instance draws its own.
- A mod can add its own **chat box**: a `CanvasLayer` under `NGame.Instance` with a plain Godot `LineEdit`. A handful of keys leak through to the game and need blocking (below).
- Co-op text between Mark's instance and the bot instance should go **out of band over localhost** between the two SpireMind bridges. The only in-game channel that carries free text is the networked dev-console action, a hack that rides the gameplay action queue.
- Persona: `LocalContext.GetMe(rs).Character` gives `Id.Entry` (IRONCLAD, SILENT, DEFECT, NECROBINDER, REGENT), `Title` ("The Ironclad"), pronouns, `SpeechBubbleColor` and `NameColor`. The game's own voice lines live in the `characters` and `ancients` loc tables and can be read at runtime.

## Speech and thought bubbles

`MegaCrit.Sts2.Core.Nodes.Vfx.NSpeechBubbleVfx` (scene `res://scenes/vfx/vfx_speech_bubble.tscn`):

- `static NSpeechBubbleVfx? Create(string text, Creature speaker, double secondsToDisplay, VfxColor vfxColor = White)`: positions above the creature. **Combat only**: outside combat (no creature node) it lands at (0,0). Player side gives `DialogueSide.Left`.
- `static NSpeechBubbleVfx? Create(string text, DialogueSide side, Vector2 globalPosition, double secondsToDisplay, VfxColor vfxColor = White)`: absolute position, works anywhere.
- Neither factory parents the node: call `AddChildSafely` yourself. `Task AnimOut()` fades and frees it (check `IsInstanceValid` and `IsInsideTree` first).
- Text goes into a `MegaRichTextLabel` wrapped in `[center][fly_in]...`, so BBCode is live. Escape `[` (`text.EscapeBbcodeTags()` or `[lb]`); an unbalanced closing tag throws in the auto-size parser.
- Fixed 280x130 px text box, font 24 to 18 pt: keep each bubble to about 100 characters and split longer replies into a sequence. On screen for about 0.5 + max(secs-1, 1) + 0.4 s. Duration like the game's `TalkCmd`: about 0.12 s per non-space character. Nothing queues bubbles, so keep one per speaker and dismiss the previous one.
- Bubbles ignore the mouse and never take focus.

`NThoughtBubbleVfx` is the smoky "thinking" variant (no colour parameter). It is good for a "thinking..." indicator: `Create(text, creature, null)`, then end it yourself with `GoAway()`. The timed variant uses `Cmd.Wait`, which is skipped in Instant mode.

Where to put a bubble over the local player (main thread):

| Where | Anchor | Parent |
|---|---|---|
| Combat (incl. an event's embedded fight) | `Create(text, me.Creature, secs, me.Character.SpeechBubbleColor)` | `NCombatRoom.Instance.CombatVfxContainer` (= `me.Creature.GetVfxContainer()`) |
| Rest site | `NRestSiteRoom.Instance.GetCharacterForPlayer(me)`; its `%ThoughtBubbleLeft`/`%ThoughtBubbleRight` control (index < 2 uses Left) | the room, or the character node (renders at 0.76 scale) |
| Shop | `NMerchantRoom.Instance.PlayerVisuals[0]` (the local player) plus an offset to tune, starting near (+60,-250) | `NMerchantRoom.Instance` |
| Anywhere else in a run (map, events, Neow, treasure, rewards, card select) | next to the top-bar portrait: `GlobalUi.TopBar.Portrait` + (110, height+170) | `NRun.Instance.GlobalUi.AboveTopBarVfxContainer` (full screen, ignores the mouse, lives for the run) |
| Menu, character select | any position | `NGame.Instance` or the character select screen |

To show the **bot's** line on Mark's screen in co-op, Mark's instance resolves the bot's `Player` by NetId and uses the same table: its creature node in combat, `GetCharacterForPlayer(bot)` at rest sites, `PlayerVisuals[1 + i]` in shops, and otherwise its `NMultiplayerPlayerState` row (`GlobalUi.MultiplayerPlayerContainer` children, match `.Player.NetId`).

Colours: Ironclad Red, Silent Swamp, Defect Blue, Regent Orange. The Necrobinder's `SpeechBubbleColor` falls back to the default look, so pass `VfxColor.Purple` for her explicitly.

## The chat box

- **Parent**: once `NGame.Instance` is ready (not in the mod initializer, which runs inside `NGame._EnterTree`), add `new CanvasLayer { Layer = 6 }` with `NGame.Instance.AddChildSafely`. All game UI is on canvas layer 0 and the dev console on 10, so layer 6 draws above the game and under the console. It survives menu, run and new run.
- **Control**: a plain `LineEdit`, not `TextEdit` and not the game's `NMegaLineEdit`. Set `KeepEditingOnTextSubmit = true`. Open it with `GrabFocus()` plus `Edit()`: `NHotkeyManager` stands down only when the focused LineEdit `IsEditing()`. The project has no global theme, so override every font, colour and stylebox.
- **Open key**: T is free, and so are `/` and Tab. Catch it with a Harmony prefix on `NInputManager._UnhandledKeyInput(InputEvent)`: when closed and the key is T with no modifiers, open the box, call `SetInputAsHandled()` and return false, so the 't' is not typed. Avoid E, Enter, S, D, X, A, M, Esc, Space, the arrows, 1-0 (rebindable; check `NInputManager.Instance.GetShortcutKey`), V (emote wheel), F2 (feedback), the dev console keys `` ` `` `'` `*` `^` Shift+8, Ctrl+Cmd+F, and Shift+Tab (Steam overlay).
- **Leaks while typing** (block them):
  - Escape, Up and Down: handle them in the LineEdit's `GuiInput` with `AcceptEvent()`.
  - Cmd/Ctrl+digit (`NPlayerHand` selects cards regardless of focus), F-keys and key releases: swallow every key in the same `_UnhandledKeyInput` prefix while the box is open.
  - F2: prefix `NFeedbackScreenOpener._Input`.
- **Focus gets stolen**: the deck upgrade, transform, enchant and remove screens call `GuiReleaseFocus` (the bot's own picks trigger this), and so do screens without a default control and controller mode switches. Re-grab in `GetTree().Root.WindowInput` before each key while the box is open.
- **Not a screen**: don't register it in `ActiveScreenContext`, `NModalContainer` or `NOverlayStack`. The game's `IsCurrent` gates would disable the end-turn button and combat UI, and SpireMind's router would misroute. Keep a `ChatState` in the mod instead. The bot can keep playing while the box is open, because it acts through `RequestEnqueue` and `ForceClick`, which need no focus.
- **Controller**: keep the box keyboard-only, and close it when `NControllerManager` switches to controller mode.
- **Look**: fonts `res://themes/kreon_regular_shared.tres` (body) and `kreon_bold_shared.tres` (names); `StsColors.cream` text and `StsColors.gold` highlights; the speaker name in `Character.NameColor`; a black outline, as the game's labels have.
- Stopgap without UI: a mod console command (`AbstractConsoleCmd` subclass, picked up from mod assemblies automatically). Typing `'` `"` `` ` `` `*` or `^` closes the console, though, so contractions break it.

## Co-op transport

- **Recommended: localhost between the two SpireMind instances.** Both load the mod; the bot instance sets `SPIREMIND_PORT` (17773).
  - Mark's mod POSTs `{from_net_id, text}` to the bot bridge's new `/chat` route; the bot queues it for the brain.
  - The bot's reply is drawn locally and POSTed back to 17772 `/chat`, where Mark's mod draws it over the bot's player.
  - None of this touches the game network, so `affects_gameplay` stays false and the join check is unaffected.
  - Use a role flag (`--sts2bot=1`) to decide which instance runs the brain.
- In-game free text exists only as `ConsoleCmdGameAction` / `NetConsoleCmdGameAction`: a networked mod console command delivers `(Player, string)` to every peer. Avoid it for chat:
  - it rides the gameplay action queue (deferred during the enemy turn, held behind choices, cancelled at combat end);
  - it is logged and checksummed;
  - SpireMind's action tracking sees it in `ActionEnqueued`.
- Nonverbal extras the bot can send as the local player:
  - emotes: `NGame.Instance.ReactionContainer.DoLocalReaction(texture, globalPos)`;
  - end-turn ping: `RunManager.Instance.FlavorSynchronizer.SendEndTurnPing()`;
  - map ping: `SendMapPing(MapCoord)`;
  - pointing at a card, relic or potion: `HoveredModelTracker.OnLocalCardHovered(card)` / `OnLocalRelicHovered` / `OnLocalPotionHovered`.
- Observing Mark:
  - end-turn pings: `FlavorSynchronizer.OnEndTurnPingReceived` (re-subscribe every run);
  - emotes: postfix `ReactionSynchronizer.HandleReactionMessage(ReactionMessage, ulong senderId)`;
  - map pings: postfix `NMapScreen.PingMapCoord(MapCoord, Player)`.
- Nameplates in ENet co-op read "Test Host" / "Test Client 1". A Harmony postfix on `NullPlatformUtilStrategy.GetPlayerName(ulong)` can return "Mark" for NetId 1 and the bot's character title for 1000. Install it in both instances; nameplates are set once in `_Ready`.

## Persona

- **In a run**: `var ch = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState()).Character;`. `ch.Id.Entry` is the stable key and `ch.Title.GetFormattedText()` the display name ("The Ironclad"); answer to it with and without the article, and to the Id. Also available: pronouns (`PronounSubject`, `PronounObject`, `PossessiveAdjective`), `Gender`, `NameColor` and `SpeechBubbleColor`. Read it on `RunManager.RunStarted` and clear it in a `RunManager.CleanUp` postfix.
- **Before a run**: `NGame.Instance.MainMenu.SubmenuStack.Peek() as NCharacterSelectScreen`. The screen is pre-created, so test `Peek()` or `Lobby != null`. Then `screen.Lobby.LocalPlayer.character`, and `lobby.Players` for everyone (struct copies; re-read each time). Changes: postfix `NCharacterSelectScreen.PlayerChanged(LobbyPlayer, bool)`. It fires for local and remote picks and also for ready toggles, so diff it yourself. `RANDOM_CHARACTER` stays undecided until the run starts. The daily run's character comes from the date seed.
- **Unlocks are per save profile**: only the Ironclad is always unlocked. The bot's no-Steam profile (`default/1000`) may have only the Ironclad until it plays more, or until Mark's progress is copied over.
- **Player names**: `PlatformUtil.GetPlayerNameRaw(platform, netId)`. ENet lobbies are `PlatformType.None` (`mp_names.json` in the working directory, else Test Host / Test Client N). The bot instance has no Steam, so pass Mark's name through config.
- **The game's own voice text**, for the persona prompt. Read it on the main thread after `LocManager.Instance != null`, which happens after mods load, so defer to the menu or `RunStarted`.
  - `characters` table, keys `<ID>.description`, `goldMonologue`, `aromaPrinciple`, `eventDeathPrevention`, `banter.alive.endTurnPing`, `banter.dead.endTurnPing`. Read with `LocManager.Instance.GetTable("characters").GetRawText(key)`, guarding with `LocString.Exists`.
  - `ancients` table, keys `<ANCIENT>.talk.<ID>.<i>-<j>.char`: the character's lines to each Ancient. `THE_ARCHITECT` holds the confrontation lines.
  - Epoch lore: `EpochModel.Get("<ID><n>_EPOCH").Description`. Gate it on `SaveManager.Instance.Progress.IsEpochRevealed` to avoid spoilers.
- **Tone, paraphrased** from the game's data:
  - Ironclad: a terse, grim soldier.
  - Silent: an almost wordless huntress who speaks in ellipses.
  - Defect: a chirping, beeping automaton; refers to itself as "it".
  - Necrobinder: a vengeful but darkly funny young lich, with Osty her animated hand.
  - Regent: a loud, pompous royal who refers to himself as "his Majesty".
  - The line counts above (Silent 10, Necrobinder 21, Regent 25 Ancient lines) show how chatty each one is.
