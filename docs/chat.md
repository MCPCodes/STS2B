# In-game chat

Mark talks to the bot from inside the game, and the bot answers as the character it plays.

## What the player sees

- Press **T** (override with `SPIREMIND_CHAT_KEY`, a Godot `Key` name; Enter and Escape fall back to T, since they close the box) to open a chat box at the bottom left. Enter sends and closes the box; Escape closes it; clicking away closes it and keeps what you typed for next time.
- A small log above the box shows the last few lines: `Mark: Ironclad, go for Strength` and `Ironclad: Aye. Inflame it is, then.` When the box is closed, lines fade after a few seconds.
- The bot answers to its character's name: "Ironclad, ...", "hey Silent", "@Defect". With one bot, an unaddressed message goes to it too. A message that starts with a different character's name is for another bot and is ignored. Between runs the bot has no name, so every message goes to it ("Silent, go poison this run" at character select).
- Commands, handled by the mod at once without waiting for the model:
  - `/pause` stops the bot. It finishes nothing new: `act` is refused and `wait_for_decision` holds. The human plays by hand meanwhile. Chat still works.
  - `/resume` hands control back.
  - `/help` lists the commands in the log.
- Later: the bot's lines also appear as a speech bubble over its character, where the game has one to reuse.

## Flow

```
ChatOverlay (main thread, stock Godot nodes)
   │ PostFromHuman("Mark", text)          ▲ LineAdded event (any thread → queued for the next frame tick)
   ▼                                      │
ChatHub (SpireMind.Core, thread-safe) ────┘
   │ unread messages, paused flag, bot name, line log
   ▼
BotTools (HTTP worker threads)
   • every tool result carries "chat": {...} when there are unread messages or the bot is paused
   • wait_for_decision returns early when a message arrives; a newer one cancels it
   • say(text): the bot speaks; the line goes to the log and the overlay
   • act is refused while paused
```

### ChatHub (src/SpireMind.Core/ChatHub.cs)

Game-agnostic and unit tested. All members are safe to call from any thread.

- `string? BotName`: the character this bot plays, without a leading "The " ("Ironclad"). The game side (`Identity`) keeps it current and sets it to null between runs; while it is null every message is for this bot.
- `IReadOnlyList<string> KnownNames`: every character name, for telling "a message for another bot" apart from an unaddressed one. Defaults to Ironclad, Silent, Defect, Regent, Necrobinder.
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
- `say {text}` returns `{"said": "<text as shown>"}` (plus `chat` when there are new messages). Errors from ChatHub become tool errors.
- `get_deck` returns the run's full deck with card text, relics with text, potions, HP and gold, for build decisions. It goes through a new `IGameBridge.DescribeDeckAsync`.

### HTTP

- `POST /chat` `{"from": "Mark", "text": "..."}` posts as the overlay does (curl testing now; the co-op relay later). It answers `{"outcome": "queued" | "command" | "not_for_me" | "empty", "reply": ...}`, with 400 for empty text. A body that isn't JSON gets 400 `{"error": "body must be a JSON object: ..."}`, here and on `/act`.
- `GET /chat?since=<seq>` returns `{"bot": "Ironclad", "paused": false, "lines": [...]}`.
- `GET /state` and `POST /act` carry the same `chat` key as the tools, but peek: the messages stay unread, so a curl doesn't take them from the model.

### Overlay (src/SpireMind/Game/ChatOverlay.cs)

- Built from stock nodes only (CanvasLayer, PanelContainer, VBoxContainer, RichTextLabel, LineEdit). No custom Node subclasses with `_Ready`/`_Process`/`_Input` overrides: a mod assembly loaded at runtime has no Godot source generators or script registration, so those overrides may never be called. Per-frame work hangs off the `SceneTree.ProcessFrame` signal ModEntry already uses, and signals connect with C# events or `Callable.From`.
- Added under `SceneTree.Root` with a deferred `add_child` so it survives scene changes. `ProcessMode = Always`. The log ignores the mouse; only the open input box takes clicks.
- The hotkey is polled with `Input.IsKeyPressed` on the frame tick (edge-triggered), and ignored while another `LineEdit`/`TextEdit` has focus (seed entry, the game's console).
- The overlay never touches game types. ModEntry hands it the bot name provider, the human's name (`SPIREMIND_HUMAN_NAME`, else the OS user name capitalised) and an optional speech bubble callback.
- Whether the game's own hotkeys still fire while the box has focus depends on how the game reads input. `ChatOverlay.IsTyping` is the seam a Harmony prefix can check to swallow game hotkeys while typing.

## Co-op later

Each game instance runs its own SpireMind server. The overlay only matters in the human's instance; the bot's instance has the MCP server Claude Code talks to. The human's instance will relay its chat to the bot instances over `POST /chat` (ports from a small config file) and poll their `GET /chat` for replies, so each bot still sees only the messages addressed to it or to everyone. One gap to close then: between runs no bot has a name, so each would take every message.
