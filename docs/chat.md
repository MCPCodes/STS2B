# In-game chat

Mark talks to the bot from inside the game, and the bot answers as the character it plays: in a chat box, and in the game's own speech bubbles over its character. The game types, members and behaviours this builds on are in `docs/chat-and-persona.md`.

## What the player sees

- Press **T** (override with `SPIREMIND_CHAT_KEY`, a Godot `Key` name; Enter and Escape fall back to T, since they close the box) to open a chat box at the bottom left. Enter sends and closes the box; Escape closes it; clicking away closes it and keeps what you typed for next time. While the box is open the game's own hotkeys stay quiet, so typing doesn't set them off.
- A small log above the box shows the last few lines: `Mark: Ironclad, go for Strength` and `Ironclad: Aye. Inflame it is, then.`, the bot's name in its character's colour. When the box is closed, lines fade after a few seconds.
- The bot's lines also appear as a speech bubble over its character, in the character's bubble colour. A long line becomes a few bubbles in a row.
- While the bot is deciding in combat, a thought cloud shows over its character after a couple of seconds and goes away when it acts or speaks.
- The bot answers to its character's name: "Ironclad, ...", "hey Silent", "@Defect". With one bot, an unaddressed message goes to it too. A message that starts with a different character's name is for another bot and is ignored. At character select the bot answers to the character just picked, so "Silent, go poison this run" reaches it once Silent is picked. With nothing picked or Random, and on the other menus, it has no name and every message goes to it.
- Commands, handled by the mod at once without waiting for the model:
  - `/pause` stops the bot. It finishes nothing new: `act` is refused and `wait_for_decision` holds. The human plays by hand meanwhile. Chat still works.
  - `/resume` hands control back.
  - `/help` lists the commands in the log.

## Flow

```
ChatOverlay (main thread, stock Godot nodes) ── each bot line ──▶ SpeechBubbles (main thread, every frame)
   │ PostFromHuman("Mark", text)          ▲ LineAdded event          • bubbles over the bot's character
   ▼                                      │ (any thread → queued     • thought cloud from ThinkingSince
ChatHub (SpireMind.Core, thread-safe) ────┘  for the next tick)
   │ unread messages, paused flag, bot name, persona, thinking-since, line log
   │ ◀── BotName, Persona from Identity (main thread, once a second)
   ▼
BotTools (HTTP worker threads)
   • every tool result carries "chat": {...} when there are unread messages or the bot is paused
   • wait_for_decision returns early when a message arrives; a newer one cancels it
   • say(text): the bot speaks; the line goes to the log, the overlay and a speech bubble
   • get_persona: the character as the game describes it
   • act is refused while paused
   • results that hand over a decision set ThinkingSince; acting clears it
```

ModEntry wires it on the `SceneTree.ProcessFrame` tick: `Identity.Poll`, then the overlay's `Tick`, then `SpeechBubbles.Tick`. Each fails on its own and logs once.

### ChatHub (src/SpireMind.Core/ChatHub.cs)

Game-agnostic and unit tested. All members are safe to call from any thread.

- `string? BotName`: the character this bot plays, without a leading "The " ("Ironclad"). The game side (`Identity`) keeps it current: the character in a run, the one picked at character select, null anywhere else. While it is null every message is for this bot.
- `IReadOnlyList<string> KnownNames`: every character name, for telling "a message for another bot" apart from an unaddressed one. Defaults to Ironclad, Silent, Defect, Regent, Necrobinder.
- `JsonObject? Persona`: the character as the game describes it, for `get_persona` (shape below). `Identity` replaces it whole when the character changes and sets it to null with the name. Both the setter and the getter deep-copy, so neither side can change the other's.
- `DateTime? ThinkingSince`: when (UTC) the bot was handed a decision it hasn't acted on yet, or null. `MarkThinking(bool)` sets it (keeping an earlier time) or clears it; BotTools calls it, and `/pause` clears it too. A mark older than `ThinkingStaleAfter` (5 minutes, above the longest `wait_for_decision`) reads as null and is restarted by the next `MarkThinking(true)`: a session that ended mid-decision leaves no tool call to clear it. The thought cloud reads it.
- `PostResult PostFromHuman(string from, string text)`: trims; handles `/pause`, `/resume`, `/help` (case-insensitive) by changing state and adding a system line; otherwise logs the human line and, when it is for this bot, queues it as unread. Returns what happened (command handled, queued, ignored as addressed elsewhere, rejected as empty).
- `ChatLine Say(string text)`: the bot speaks. Whitespace collapsed, capped at `MaxSayLength` (280) with "..." when cut. Throws `ArgumentException` on empty text. A burst limit (more than 6 lines in 10 seconds) throws `InvalidOperationException` so the model slows down.
- `IReadOnlyList<ChatMessage> TakeUnread()`: hands over and clears the unread human messages addressed to this bot. `bool HasUnread`. `PeekUnread()` returns them without clearing, for the curl debugging routes.
- `bool Paused`, `string? PausedBy`.
- `IReadOnlyList<ChatLine> LinesSince(long seq)`: the log after a sequence number (capped at the last 200 lines). Each `ChatLine` has `Seq`, `Kind` (Human, Bot, System), `From`, `Text`, `At`.
- `event Action<ChatLine>? LineAdded`: raised after the lock is released, on whatever thread caused it.
- Addressing: a message is addressed to a name when it starts with that name (or "The name"), optionally after "hey", "hi", "ok" or "@", followed by a comma, colon, space, "!" or "?" or the end. Case-insensitive.

### Tool results

`get_state` and `wait_for_decision` return the snapshot JSON; `act` returns `{done, next}`; errors return `{error, current}`. Each of these gets one extra top-level key when it matters:

```json
"chat": {
  "you_are": "Ironclad",
  "paused": true,
  "messages": [{ "from": "Mark", "text": "Ironclad, go for Strength", "seconds_ago": 3 }]
}
```

`paused` only when true, `messages` only when there are any. Messages are taken (marked read) when the result is built, right before it is returned. A cancelled call never builds its result, so its messages stay unread for the next one. The snapshot version is computed from the snapshot alone, so chat never changes it.

A tool call can outlive its client: Esc in Claude Code drops the request, but the server-side wait would run on and take the next message for an answer nobody reads. Two things stop that. `McpEndpoint` cancels a call when the client sends `notifications/cancelled` for its id (answering the dropped request with JSON-RPC error -32800). And only one `wait_for_decision` runs at a time: a new one cancels the one before.

- `wait_for_decision` returns as soon as an unread message arrives, with whatever the game shows at that moment (possibly `awaiting_decision: false`). While paused it never reports a decision: it waits for a message or the timeout, then returns the snapshot with `awaiting_decision` forced to false, `waiting: "paused by Mark"` and no actions.
- `act` while paused is rejected: "Paused by Mark. Call wait_for_decision; you can still talk with say."
- `get_state` while paused shows the real state but with `awaiting_decision` false and no actions, same as above, so the model doesn't plan moves it can't make.
- `say {text}` returns `{"said": "<text as shown>"}` (plus `chat` when there are new messages). Errors from ChatHub become tool errors. The tool description tells the model its lines show as bubbles of about 100 characters.
- `get_deck` returns the run's full deck with card text, relics with text, potions, HP and gold, for build decisions. It goes through `IGameBridge.DescribeDeckAsync`.
- `get_persona` returns `ChatHub.Persona`, or the error `"no character yet; call get_persona again once the run has started"`, with the usual `chat` key. The play prompt calls it once at the start and after context compaction, and again once the run has started if it had no character yet or the character changed.

Thinking, for the thought cloud: when `get_state` or `wait_for_decision` hands back a snapshot with `awaiting_decision` true and actions, or `act` hands back one (its `next`, or the `current` it was refused against), BotTools calls `MarkThinking(true)`; any other result of those three clears it. `act` clears it as soon as it starts. Paused snapshots never await a decision, so they clear it. `say`, `get_deck` and `get_persona` leave it alone, since the model calls them mid-decision.

### HTTP

- `POST /chat` `{"from": "Mark", "text": "..."}` posts as the overlay does (curl testing now; the co-op relay later). It answers `{"outcome": "queued" | "command" | "not_for_me" | "empty", "reply": ...}`, with 400 for empty text. A body that isn't JSON gets 400 `{"error": "body must be a JSON object: ..."}`, here and on `/act`.
- `GET /chat?since=<seq>` returns `{"bot": "Ironclad", "paused": false, "lines": [...]}`.
- `GET /state` and `POST /act` carry the same `chat` key as the tools, but peek: the messages stay unread, so a curl doesn't take them from the model, and their results don't mark the bot as thinking.

### Overlay (src/SpireMind/Game/ChatOverlay.cs)

- Built from stock nodes only (CanvasLayer, PanelContainer, VBoxContainer, RichTextLabel, LineEdit). No custom Node subclasses with `_Ready`/`_Process`/`_Input` overrides: a mod assembly loaded at runtime has no Godot source generators or script registration, so those overrides may never be called. Per-frame work hangs off the `SceneTree.ProcessFrame` signal ModEntry already uses, and signals connect with C# events or `Callable.From`.
- A `CanvasLayer` on layer 6 under `NGame.Instance`: game UI draws on layer 0 and the dev console on 10, so the box is above the one and under the other. It is added from the overlay's tick once NGame is ready (the mod initializer runs too early, inside `NGame._EnterTree`) and survives the menu, runs and new runs. `ProcessMode = Always`. The log ignores the mouse; only the open input box takes clicks.
- Keys come from a Harmony prefix on `NInputManager._UnhandledKeyInput` (`Patches/ChatInputPatches.cs`), which only sees keys the GUI didn't use. With the box closed, a fresh press of T with no modifiers opens it, is marked handled and isn't typed. With the box open, every key that reaches the prefix is kept from the game: Cmd or Ctrl plus a digit (which picks cards whatever has focus), F-keys, key releases. Escape, Up, Down and Tab are accepted in the LineEdit's `GuiInput`, and a second prefix on `NFeedbackScreenOpener._Input` keeps F2 from opening the feedback screen while typing. Game buttons click on a hotkey's release, so the release of a key pressed while typing (the Enter or Escape that closed the box, a letter still held) is swallowed too when it arrives after the box closed, within 3 seconds of its press. If the input patch didn't apply, the hotkey is polled on the tick instead (logged), and game hotkeys may then fire while typing.
- The LineEdit keeps editing across Enter (`KeepEditingOnTextSubmit`) and opens with `GrabFocus()` plus `Edit()`, since the game's hotkey manager stands down only while the focused LineEdit is editing. Game screens release focus under an open box (the deck upgrade, transform, enchant and remove screens, which the bot's own picks open), so `Window.WindowInput` takes it back before each key. Switching to a controller closes the box and keeps the draft.
- Not a game screen: it never registers with `ActiveScreenContext`, `NModalContainer` or `NOverlayStack`, whose checks would disable the combat UI and confuse SpireMind's router. The bot keeps playing while the box is open.
- The game's look (`ChatTheme`): Kreon regular for text, Kreon bold for names, cream text, gold for the human's name and the box border, a black outline. The bot's name is in its character's `NameColor` (`Identity.NameColor`), else a built-in colour per character.
- The overlay never touches game types. ModEntry hands it hooks: the parent node, the theme, the bot's name colour, controller mode, the speech bubble callback, plus the human's name (`SPIREMIND_HUMAN_NAME`, else the OS user name capitalised).

### Speech bubbles (src/SpireMind/Game/SpeechBubbles.cs)

- The overlay hands every bot line to `SpeechBubbles.Say`, which cuts it into chunks of about 100 characters (the bubble's text box is a fixed 280x130 px), at a sentence end where it can, and queues them. `Tick` shows one at a time, each for about 0.12 s per letter as the game's `TalkCmd` does, between 1.5 and 6 s. At most 6 chunks wait; older ones are dropped, and the log always has the whole line.
- Where the bubble goes: over the character in combat (in `NCombatRoom.Instance.CombatVfxContainer`), at the character's thought bubble spot at a rest site, above the player's figure in a shop, beside the top-bar portrait anywhere else in a run, and at a fixed spot on the left of the screen under `NGame` outside a run (menus, character select). The colour is the character's `SpeechBubbleColor`, Purple for the Necrobinder, whose own falls back to the default look.
- Bubbles belong to the room: a room or run change clears the queue and the bubble on screen.
- The thought cloud: in combat, once `ChatHub.ThinkingSince` is 2.5 s old, an `NThoughtBubbleVfx` with "..." shows over the character. It goes away when the decision is made (a newer or no `ThinkingSince`), a speech bubble needs the spot, the bot is paused, or the fight ends. A decision handed over before the current room, or held for over 2 minutes, shows none: the session ended or the human took over by hand.
- Every game call is guarded: a failure costs the bubble and one log line, never the bridge.

### Persona (src/SpireMind/Game/Identity.cs)

- Polled once a second on the main thread. In a run the character is `LocalContext.GetMe(...).Character`. At character select (the top of `NGame.Instance.MainMenu.SubmenuStack` is an `NCharacterSelectScreen` with a `Lobby`) it is `Lobby.LocalPlayer.character`, the local player's pick. Random, nothing picked, and every other menu mean no character. A read that fails while a run loads or tears down keeps the last one.
- The name comes from `Character.Id.Entry` (IRONCLAD becomes Ironclad, and so on for Silent, Defect, Regent, Necrobinder), else the title without "The ". `Identity.NameColor` is the character's `NameColor`, for the chat log.
- When the character changes it sets `BotName` and replaces `Persona`:

```json
{
  "name": "Ironclad",
  "title": "The Ironclad",
  "id": "IRONCLAD",
  "pronouns": { "subject": "...", "object": "...", "possessive": "..." },
  "tone": "A terse, grim soldier.",
  "voice_lines": ["...", "..."]
}
```

- `pronouns` comes from `PronounSubject`, `PronounObject` and `PossessiveAdjective`, leaving out what can't be read. `tone` is our paraphrase per character (the list in `docs/chat-and-persona.md`).
- `voice_lines`: up to 15 short lines in the character's own voice, read once per character after `LocManager.Instance` is up. From the `characters` table, `<ID>.goldMonologue`, `aromaPrinciple`, `eventDeathPrevention`, `banter.alive.endTurnPing` and `banter.dead.endTurnPing`; from the `ancients` table, the character's lines to each Ancient, `<ANCIENT>.talk.<ID>.<i>-<j>.char`, with the numbers probed. Each key is checked with `LocString.Exists` before it is read. Lines that are missing, empty, echo their key or hold a placeholder are dropped, the rest are cleaned to plain text, and the pick takes one source at a time, shortest first, skipping lines over 160 characters and repeats. Never epoch lore: it would spoil the timeline.

## Co-op later

Each game instance runs its own SpireMind server. The overlay only matters in the human's instance; the bot's instance has the MCP server Claude Code talks to. The human's instance will relay its chat to the bot instances over `POST /chat` (ports from a small config file) and poll their `GET /chat` for replies, so each bot still sees only the messages addressed to it or to everyone. At character select each bot answers to its own pick, but on the other menus no bot has a name, so each would take every message. Showing a bot's line as a bubble on the human's screen needs the bot's player rather than the local one (see `docs/chat-and-persona.md`).
