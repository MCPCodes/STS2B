# Screens: the bot's snapshots and action ids

This is the reference for every screen the bot sees through its tools (`get_state`, `wait_for_decision`, `act`, `act_sequence`, `get_deck`): what each snapshot holds, key by key, and which action ids it offers, with their labels, refusals and waits. It was written from the game-side readers (`src/SpireMind/Game/Screens/*.cs`, `src/SpireMind/Game/Describe.cs`, `ScreenRouter.cs`, `GameBridge.cs`) and from Core (`GameSnapshot.cs`, `BotTools.cs`, `DecisionWaiter.cs`, `SnapshotVersioner.cs`), on the public branch v0.107.1.

## Purpose and how to use it

The full-run arena (`src/SpireMind.Arena`) implements `IGameBridge` and serves its fights through the same `BotTools` as the mod. For a model to play the arena exactly as it plays the game, **the arena must emit these shapes exactly**: the same screen names, the same keys in the same order, the same omissions, the same action ids and the same labels. Labels matter as much as ids, because `act_sequence` finds every step after the first by its label.

**The mod's readers are the source of truth.** Where this page and the code disagree, the code wins and this page is wrong. Where it says a value is illustrative (most card, relic and event texts in the examples), the shape is exact and the words are not. `FakeGame` in Core is a wiring stand-in and does not follow these shapes; do not copy it.

How the page is laid out:

- **The common envelope** covers what every screen shares: the snapshot keys, versions, busy and waiting snapshots, the paused view, the `chat` key, the shared blocks (`run`, `player`, detailed `potions`, `team`, `boss` and `second_boss`, cards, creatures and powers), the potion actions that ride along, the tool results, every Core and bridge refusal, `get_deck`, and the order screens are routed in. Later sections refer back to it instead of repeating it.
- Then one section per screen, in the order a run meets them: combat and hand selection, the map, rewards and the card reward, treasure, the shop, the rest site, card selection, and events with the other screens (Crystal Sphere, popups, game over, menu).
- Each screen section says when the bot sees it, its `state` keys in order, its actions, its own refusals and waits, examples, what changes in co-op, what is left out on purpose, and what the arena has to match.
- **Open questions for the Mac** at the end lists what the readers could not settle from the code alone.

Conventions: key names and labels in backticks are exact, with `<...>` standing for the part that varies. "Present" says when a key appears; a key that is not present is left out, never written as `null`, unless the page says otherwise.

## The common envelope

This section covers what is shared by every screen: the snapshot envelope, versions, the busy, waiting and paused states, the `chat` key, the `run`, `player`, `potions`, `team` and `boss` blocks that repeat across screens, the potion actions that ride along, the results of `act`, `act_sequence` and `get_deck`, every refusal text, and the order screens are routed in. Each screen's own `state` keys and action ids are in that screen's section.

Sources: `src/SpireMind.Core/GameSnapshot.cs`, `BotTools.cs`, `DecisionWaiter.cs`, `SnapshotVersioner.cs`, `ChatHub.cs`, `McpEndpoint.cs`, `BridgeServer.cs`; `src/SpireMind/Game/GameBridge.cs`, `ScreenRouter.cs`, `IScreen.cs`, `Describe.cs`, `ActionTracker.cs`, `Latches.cs`, `Screens/PotionActions.cs`, `Screens/MiscReaders.cs` (menu).

### Who builds which part (read this first if you are writing the arena)

The game side implements `IGameBridge` and nothing else:

- `CaptureAsync` returns a `GameSnapshot`: `screen`, `awaiting_decision`, `waiting`, `state`, `actions`.
- `ExecuteAsync(actionId)` returns `ActionResult(Ok, Error)`. `Ok` means the game accepted the action, not that it finished.
- `DescribeDeckAsync` returns the `get_deck` object.

Core adds everything else on top, and this code is the same for the real mod and for any other `IGameBridge`:

- `version`, from `SnapshotVersioner`.
- The waiting and settling logic, from `DecisionWaiter`.
- The tool surface, `act` and `act_sequence` result shapes, refusals made before the game is asked, the paused view and the `chat` key, all from `BotTools`.
- The MCP wrapping, from `McpEndpoint`.

So an arena that implements `IGameBridge` and runs through `BotTools` gets the envelope for free. Its job is to make `CaptureAsync`, `ExecuteAsync` refusals and `DescribeDeckAsync` match the real readers. The "Arena checklist" at the end lists what Core relies on.

### Transport

- **MCP** (Claude Code on `/mcp`, the subscription brain on `/mcp/own`): every tool result is `{"content": [{"type": "text", "text": "<payload as compact JSON>"}], "isError": <bool>}`. The JSON has no indentation, and `>`, `+`, quotes and non-ASCII characters are written as they are, not as `\uXXXX` (`UnsafeRelaxedJsonEscaping`). A call the client cancels, or a `wait_for_decision` replaced by a newer one, gets JSON-RPC error `-32800 "Request cancelled"` instead of a tool result.
- **API brain**: the same payload text goes into a `tool_result` block as `content`, with `is_error`. The API brain also has its own tools `notes_read`, `notes_write`, `notes_append` and `notes_list`, which are not part of the game bridge. Its `wait_for_decision` loops in-process with `timeout_seconds` 240, and it holds back for up to 60 s a decision whose version it already handed over. That changes nothing in the shapes.
- **HTTP debugging routes**: `GET /state` is `get_state` (200, or 500 when `isError`). `POST /act {"action_id", "version"}` is `act` (200, or 409 when `isError`). Both show unread chat but leave it unread.

### The snapshot envelope

`GameSnapshot.ToJson()` writes the keys in this order:

| Key | Type | Meaning |
|---|---|---|
| `version` | integer | Stamped by Core. Goes up by one every time the snapshot differs from the last one stamped. See Version. |
| `screen` | string | One of `combat`, `hand_select`, `map`, `rewards`, `card_reward`, `card_select`, `shop`, `rest`, `event`, `treasure`, `crystal_sphere`, `game_over`, `modal`, `menu`, `unknown`, `error`. |
| `awaiting_decision` | bool | True when the local player has a choice to make and `actions` can be used. |
| `waiting` | string | **Only present when not null.** A short English reason why there is no decision (`"enemy turn"`, `"resolving Strike"`, `"paused by Mark"`). Readers set it only together with `awaiting_decision: false`. |
| `state` | object | The screen's facts. Always present. `{}` on busy snapshots. |
| `actions` | array of `{"id": string, "label": string}` | Always present. Empty whenever `awaiting_decision` is false (readers, busy snapshots and the paused view all leave it empty). In a decision it lists every legal action. |
| `chat` | object | Added by `BotTools` only when there are unread messages or the bot is paused. See The chat key. |

Rules every screen follows:

- An id is only valid for the snapshot it came from. Ids are short and stable within one snapshot. The id grammar is in CLAUDE.md and repeated under Action id grammar below.
- `label` is English text for the model to read, built from the game's own names (`"Play Strike on Jaw Worm"`, `"Use Fire Potion on Jaw Worm"`, `"Discard Fire Potion"`). `act_sequence` finds actions again by their labels, so labels must match the real game's exactly.
- Within `state`, a key that would be empty, false or zero is usually left out instead of written as `null`, `false` or `0` (`block`, `powers`, `enchant`, `text`, `team`). The per-screen sections say which.
- All game text goes through `TextCleaner.Clean`. It strips BBCode tags, turns the energy icon into `(E)` and the star icon into `(Star)`, drops other icons, collapses runs of spaces and joins lines with `" / "`.

### Version

`SnapshotVersioner.Stamp` hashes four things: `screen`, `awaiting_decision`, `state`, and the **ids** in `actions`. If the hash differs from the last snapshot it stamped, the counter goes up by one. The snapshot then gets the current counter.

- The counter starts at 0 when the bridge starts, so the first stamped snapshot is version 1. It never resets between fights or runs, only when the game restarts.
- Not in the hash: `waiting`, action labels, `chat`, and the paused view. So a change only to a waiting reason, or only to a label (such as `"Confirm selection (1 chosen)"`), keeps the version.
- Every capture made through the tools is stamped: `get_state`, each 150 ms poll inside `wait_for_decision`, the captures before and after each step of `act` and `act_sequence`, `/state` and `/act`. States the bot never saw are stamped too, so it sees gaps in the numbers. Only equality and order mean anything.
- A change and a change back (A, then B, then A) counts as two bumps.
- The brain controller's screen poll uses `DecisionWaiter.PeekAsync`, which does not stamp. A deck view opened and closed while the model plans would otherwise make its next `act` stale.

### Decisions, busy states and waiting

Snapshots with `awaiting_decision: false` come in four kinds:

1. **Bridge busy** (`GameSnapshot.Busy`): `state` is `{}` and `actions` is `[]`. `screen` is the **last screen that was routed** (`ScreenRouter.LastScreenName`, `"unknown"` until the first one), so a busy snapshot during a card play still says `combat`. Reasons, checked in this order:
   - `"resolving <what>"` while a tracked request is in flight (`ActionTracker.Pending`). This comes before routing. `<what>` is set by the reader that started the request. Examples: a card name (`"resolving Strike"`), a potion name, `"end turn"`, `"discarding Fire Potion"`, `"taking <card>"`, `"buying <item>"`, `"opening the shop"`, `"leaving the rewards"`, `"choosing <option>"`, `"voting for <option>"`, `"leaving the event"`, `"opening the chest"`, `"divining at column x, row y"`. A request ends when its own done-check passes or its timeout runs out (6 s by default; potion use 6 s, potion discard 4 s).
   - `"game starting up"`, `"screen transition"`, `"run cleaning up"`, `"loading"`, `"run loading"`, `"travelling on the map"`, `"waiting on the network"` (the co-op "waiting for player" overlay).
2. **Unrouted**: `screen: "unknown"`, `state: {}`. Reasons (the router's starting text `"no recognised screen yet"` is always overwritten before it returns, so it is never sent):
   - `"an inspect or feedback screen is open; close it to continue"`
   - `"a deck view or the pause menu is open; close it to continue"`
   - `"a screen the bot can't read is open (<OverlayTypeName>)"`
   - `"between rooms"`
   - `"nothing to do in this <RoomType> room yet"` (for example a `Map` room with the map closed, or a fight's room after the fight)
3. **Reader error**: `screen: "error"`, `state: {}`, `waiting: "reader error: <ExceptionType>: <message>"`. A reader threw, and the bot should wait.
4. **Reader waiting**: the screen's own reader says no. `screen` is that screen. `state` is usually the full screen state (a map, shop or event with its facts, and so on); combat sends a small `{"turn", "round", "hp", "block", "teammates"?}`. `waiting` gives the reader's reason. Readers can also return a Busy snapshot under their own name with `state: {}` (for example `map` with `"map closing"`).

A **decision** is `awaiting_decision: true`, no `waiting`, and at least one action. `DecisionWaiter` hands a decision over only after it has kept the **same version for 300 ms** (`SettleTime`), polling every 150 ms. That way a hand mid-draw or a screen mid-transition is never handed over.

### Paused view

While a human has typed `/pause`, every snapshot the tools return (`get_state`, `wait_for_decision`, and `act` or `act_sequence`'s `next` or `current`) is changed as follows:

- `awaiting_decision` is false.
- `waiting` is `"paused by <name>"`.
- `actions` is `[]`.
- `state` is the real state, and the version is the real snapshot's version.

The versioner never sees this copy. `chat.paused` is true. `act` and `act_sequence` are refused (see Refusals under The common envelope).

### The chat key

`BotTools.WithChat` adds a top-level `"chat"` to **any** object result: snapshots, `act` and `act_sequence` results, errors, `get_deck`, `say` and `get_persona`. It is added only when there are unread messages for this bot or the bot is paused. In `act` results it sits beside `done` and `next`, not inside `next`.

```json
"chat": {
  "you_are": "Ironclad",
  "paused": true,
  "messages": [
    {"from": "Mark", "text": "Ironclad, take the elite", "seconds_ago": 4},
    {"from": "Silent", "text": "I'll cover the left path.", "seconds_ago": 2, "teammate": true}
  ]
}
```

- `you_are`: the character this bot plays without a leading "The ". Present when known.
- `paused`: only when true.
- `messages`: only when there are any, in arrival order. Each has `from` (string), `text` (string) and `seconds_ago` (integer, at least 0). `teammate: true` marks another bot's line relayed by the co-op relay.
- MCP calls take the messages (mark them read). `/state` and `/act` only peek. A cancelled call leaves them unread.
- An unread **human** message ends `wait_for_decision` at once, and stops an `act_sequence` before its next step. Teammate messages do neither; they ride along on the next result.
- Chat never changes `version`.

### Shared blocks

#### `run` (`Describe.RunInfo`)

```json
"run": {"act": 1, "floor": 3, "ascension": 0}
```

- `act`: integer, 1-based (`CurrentActIndex + 1`).
- `floor`: integer, the game's `TotalFloor`.
- `ascension`: integer.
- In `combat` and `hand_select`, `run` also has `"room"`: the room type as a string (`"Monster"`, `"Elite"`, `"Boss"`, or `"Event"` for an event's fight).
- No seed or RNG state is ever shown.

#### `player` (`Describe.PlayerRun`)

Full form, in key order:

```json
"player": {
  "character": "The Ironclad",
  "hp": 72, "max_hp": 80, "gold": 141,
  "relics": ["Burning Blood", "Anchor"],
  "potions": ["Fire Potion", null, null],
  "deck_size": 12,
  "deck": {"Anger": 1, "Bash": 1, "Defend": 4, "Strike": 5, "Twin Strike": 1}
}
```

- `character`: string, the game's character title.
- `hp`, `max_hp`, `gold`: integers.
- `relics`: relic names (strings) in the order they were picked up. No text; `get_deck` has it.
- `potions`: one entry per potion slot (`MaxPotionCount` long). Each entry is the potion name, or `null` for an empty slot.
- `deck_size`: integer.
- `deck`: an object from card name to count, sorted by ordinal string order. Upgraded cards are separate names (`"Bash+"`, or `"+N"` for multi-upgrade cards).

Which screens carry it, and how:

| Screen | `player` keys | Moved elsewhere |
|---|---|---|
| `map`, `rewards`, `treasure`, `event` | `character, hp, max_hp, gold, relics, deck_size, deck` | `potions` replaced by the detailed top-level `potions` |
| `shop` | `character, hp, max_hp, relics, deck_size, deck` | `gold` at top level; detailed `potions` at top level |
| `rest` | `character, gold, relics, deck_size, deck` | `hp`, `max_hp` at top level; detailed `potions` at top level |
| `card_reward`, `game_over` | full form (with name-only `potions`) | none |
| `card_select` outside combat | full form, but `deck` is left out (and `deck_size` comes after `potions`) when the cards on offer are the whole deck | none |
| `card_select` during combat | none: a `combat` summary instead (the out-of-combat form if the summary can't be built) | none |
| `combat`, `hand_select` | none: `me` (the creature), `relics` (names) and detailed `potions` at top level | none |
| `crystal_sphere`, `modal`, `menu`, `unknown`, `error`, busy | none | none |

#### Detailed `potions` (`PotionActions.Describe`)

These are on every screen that offers potion actions: `combat`, `hand_select`, `map`, `rewards`, `shop`, `rest`, `treasure`, `event`. There is one entry per slot, `null` for an empty slot:

```json
"potions": [
  {"slot": 0, "name": "Fire Potion", "text": "Deal 20 damage to an enemy.", "target": "AnyEnemy", "usable": false},
  null,
  {"slot": 2, "name": "Blood Potion", "text": "Heal for 20% of your Max HP.", "target": "AnyPlayer", "usable": true}
]
```

- `slot`: integer, the slot index used in potion ids.
- `name`: string.
- `text`: the potion's live description.
- `target`: the game's `TargetType` name for this potion right now. Values seen in code: `AnyEnemy`, `AllEnemies`, `RandomEnemy`, `AnyPlayer`, `Self`, `TargetedNoCreature`. It is read at decision time; Foul Potion is `TargetedNoCreature` out of combat and `AllEnemies` in one.
- `usable`: true when at least one `potion:<slot>...` use is legal right now on this screen. A combat-only potion shows `false` off combat. Every potion shows `false` while combat's may-act gate is closed, for example during `hand_select`.
- `usable` describes the potion, not the snapshot. On a screen where potion actions are not a decision by themselves, `usable` can be true while `actions` is empty and `awaiting_decision` is false.

#### `team` (co-op only)

An array of strings, one per teammate: `"<character title> <hp>/<max_hp> hp"`, for example `"The Silent 41/70 hp"`. Present only in co-op with at least one teammate, and only on `map`, `rest`, and `event` when the event is shared. Combat has its own `teammates` block (see Combat).

#### `boss` and `second_boss` (`Describe.AddBosses`)

Two top-level string keys on `map`, `rest`, `shop`, `rewards` and `card_reward` (never on `treasure`, `event`, `card_select` or combat). Each screen's table says where they sit in its key order; on `shop`, `rewards` and `card_reward` they come right after `run`.

- `boss`: the title of the act's boss encounter, for example `"Ceremonial Beast"`. Present when the title is non-empty. It is named from the start of the act, as the boss node's art shows it to humans.
- `second_boss`: the second boss encounter's title. Present only when the map has a second boss node and the title is non-empty. Golden Compass and Spoils maps have no second boss node, so the key is left out there even when the act rolled a second boss.
- A boss already beaten is not named again. While the party stands on the boss node (its rewards screen, or the map after it), `boss` is left out and only `second_boss` can appear. While it stands on the second boss node, both are left out.
- Neither key appears when the run has no act.

#### Cards in lists (`Describe.Card` and `Describe.CardOffer`)

```json
{"name": "Bash+", "cost": "2", "type": "Attack", "text": "Deal 10 damage. Apply 3 Vulnerable.", "enchant": "Sharp", "rarity": "Basic"}
```

- `name`: string. It ends in `+` (or `+N`) when the card is upgraded.
- `cost`: **a string**:
  - `"0"` to `"N"`
  - `"X"`
  - `"-"`: unplayable (negative cost)
  - `"?"`: the cost could not be read
  - plus `"+N*"` or `"+X*"` when the card also costs stars, for example `"1+2*"`
- `type`: string, the game's `CardType` name: `Attack`, `Skill`, `Power`, `Status`, `Curse`, `Quest`.
- `text`: string, left out when empty. Off combat (offers, deck grids, event hover tips) it is the deck-pile description with no combat modifiers. In combat it is the live text for the card's pile; hand cards show the hand's text.
- `enchant`: string, the enchantment's title, only when the card is enchanted.
- `rarity`: string, the `CardRarity` name (`Basic`, `Common`, `Uncommon`, `Rare`, `Ancient`, `Event`, `Token`, `Status`, `Curse`, `Quest`). Only on offers (`Describe.CardOffer`: rewards, the card reward, the shop, deck grids out of combat and `get_deck`). Never in combat, and not on event hover-tip cards.
- There is no upgraded flag in snapshots; the `+` in `name` carries it (`get_deck` adds `upgraded: true`).
- Hand cards in combat also carry `i`, `playable`, `unplayable` and `vs`, and grid entries carry `i`, `selected` or `copies`; see Combat and Card selection.

#### Creatures and powers (`Describe.Creature` and `Describe.Powers`)

A creature is `{"name", "hp", "max_hp", "block"?, "powers"?}`. `block` appears only when above 0, `powers` only when the creature has at least one visible power. The player's creature is named by the character's title, for example `"The Ironclad"`.

`powers` is an array of visible powers only (`PowerModel.IsVisible`; invisible powers are left out). It is the same on `me`, enemies, teammates, pets and the `card_select` combat summary. Each power, keys in order:

| Key | Type | Present | Meaning |
| --- | --- | --- | --- |
| `name` | string | always | The power's title, or its id entry if the title is empty. |
| `amount` | integer | when not 0 | `DisplayAmount`, the number shown on the icon (for Vulnerable, Weak, Frail: rounds left including this one). |
| `kind` | string | when not `None` | `TypeForCurrentAmount`: `"Buff"` or `"Debuff"` for the current amount (a negative Strength reads as a debuff). |
| `text` | string | when non-empty | The power's first hover tip text with amounts filled in. |

### Potion actions (they ride along)

Ids:

| Id | Label | When offered |
|---|---|---|
| `potion:<slot>` | `"Use <name>"` | No target needed: `AllEnemies` or `RandomEnemy` (combat only), `Self`, `TargetedNoCreature`, or `AnyPlayer` used on yourself. Off combat, an `AnyPlayer` potion you could throw always goes to you. |
| `potion:<slot>>e<j>` | `"Use <name> on <enemy>"` | `AnyEnemy` potions, combat only. One per targetable enemy (alive, still in the fight, hittable, allowed by the game's targeting hooks, and a valid target for this potion). `j` is the enemy's roster index for this fight; dead or fled enemies leave gaps. While another living enemy of the fight shares the name (a fled one counts, since it is still alive), the label adds `" (e<j>)"`, as for card plays. |
| `potion:<slot>>p<j>` | `"Use <name> on yourself"`, or `"Use <name> on <player>"` | `AnyPlayer` potions in combat that the game lets you throw at allies (co-op). One per living, valid player. `j` indexes the fight's players, including you. `" (p<j>)"` is added while two players share a name. |
| `discard_potion:<slot>` | `"Discard <name>"` | Every filled slot, including potions that cannot be used by hand (Automatic ones such as Fairy in a Bottle). |

Order: slot by slot, ascending. Within a slot the uses come first, then that slot's discard.

When nothing is offered at all (no uses and no discards):

- the player is dead
- the game's `CanRemovePotions` is false
- a tracked request is in flight
- during a fight, combat's may-act gate is closed, whichever screen is asking

A potion that is already queued is skipped.

When a use is offered:

- The potion's `Usage` is not `Automatic` or `None`.
- `CombatOnly` potions are offered only on the combat screen during a fight.
- The potion passes its own custom usability check.
- The target rules in the table above apply.

Where the ids appear in `actions`, after the screen's own ids:

| Screen | When the potion ids are added |
|---|---|
| `combat` | Every decision, after the card plays and before `end_turn`. |
| `map` | Every decision (the map is a decision only when at least one `map:<n>` is offered). |
| `rewards` | When the screen has at least one `reward:<n>` or `proceed`. Also when potion actions are the only thing possible and a potion reward is blocked by full slots: that reward's note is `"potion slots full: use or discard a potion to take it"`, and using or discarding a potion is then the decision. |
| `shop`, `rest`, `treasure`, `event` | Only when the screen offers at least one action of its own. Otherwise the snapshot is waiting. |
| `card_reward`, `card_select`, `hand_select`, `crystal_sphere`, `modal`, `game_over`, `menu` | Never. |

What happens when one runs:

- A **use** calls `PotionModel.EnqueueManualUse(target)` on the synced action queue. The snapshot is busy with `"resolving <potion name>"` until one of these happens, or 6 s pass:
  - the slot no longer holds that potion, or the potion is no longer queued, and the game looks idle
  - a hand selection opens, or a new overlay opens (some potions open a choice; that choice is the next decision)
  - the fight ends
- A **discard** enqueues `DiscardPotionGameAction(me, slot, inCombat)`. The snapshot is busy with `"discarding <name>"` until the slot changes and the game looks idle, the fight ends, or 4 s pass.

Refusals for potion ids:

- Not in the snapshot's actions: `'<id>' is not a legal action now.` (from BotTools).
- Gone by the time the game checks again:
  - `'<id>' is no longer available on the <screen> screen.` (from the bridge)
  - `'<id>' is not available now.` (from PotionActions)
- In combat, combat's own checks run first: `No fight to act in (<why>).` or `Cannot act now (<why>).`
- Discard only: `The action queue is not ready.`

### Tools and their results

| Tool | Arguments | Success payload | `isError` |
|---|---|---|---|
| `get_state` | none | the snapshot | false |
| `wait_for_decision` | `timeout_seconds` (integer 1 to 240, default 60; clamped) | the snapshot | false |
| `act` | `action_id` (string, required), `version` (integer, optional) | `{"done": "<id>", "next": <snapshot>, "warning"?: "..."}` | false |
| `act_sequence` | `version` (integer, required), `actions` (1 to 12 id strings, required) | `{"done": [{"id", "label"}...], "stopped"?, "not_run"?, "warning"?, "next": <snapshot>}` | true only when nothing ran |
| `get_deck` | none | see get_deck | false, even when it carries `error` |
| `say` | `text` (string, required) | `{"said": "<text as shown>"}` | false (true on a refusal, see Refusals) |
| `get_persona` | none | `{"name", "title"?, "id", "pronouns"?: {"subject"?, "object"?, "possessive"?}, "tone"?, "voice_lines"?: [string]}` | false (true before a character is known, see Refusals) |

All of them can also carry `chat`.

**wait_for_decision**:

- Returns the first decision that has kept the same version for 300 ms.
- Returns at once, settled or not, when an unread human message arrives or the bot is stopped with `/stop`. That snapshot may have `awaiting_decision: false`.
- On timeout it returns whatever the game shows, which may be waiting.
- While paused it never hands over a decision.
- A newer `wait_for_decision` cancels the one still running.

**act** runs these steps:

1. Capture `before` and check it (see Refusals under The common envelope).
2. Off combat (any screen except `combat` and `hand_select`), wait out what is left of the bot's last `say` line, 1.5 to 3 s. Then capture and check again.
3. Call `ExecuteAsync`.
4. Wait up to 20 s (`ActSettleTimeout`) for a settled decision whose version is newer than `before`.

On a decision it returns `done` and `next`. Key order is `done`, `next`, `warning`:

- `warning: "Nothing changed after this action. The game may have ignored it; re-check the state before repeating it."` when `next` has the same version as `before`.
- `warning: "The game is still busy. Call wait_for_decision before acting again."` when `next` is not a decision.

Without `version`, `act` skips the stale check.

**act_sequence**:

- Every id is checked against the snapshot named by `version` before anything runs.
- The first id runs like `act`. Each later id is found again in the current snapshot:
  - Ids with no digit (`end_turn`, `confirm`, `proceed`) are kept as they are while still offered.
  - Other ids are found by their label in the starting snapshot. If none match, a target id also matches an action with the same target part (`>e<j>` or `>p<j>`) whose label is equal once the twin tag `" (e1)"` or `" (p0)"` is dropped from both.
  - Between copies of a card, it prefers the one whose `state.hand` entry equals the original's (without `i`, `playable`, `unplayable`, `vs`, `selected`, `selectable`), first with `text` compared and then without it. Then it prefers the same id, then the first match.
- Key order is `done`, then `stopped` and `not_run` if it stopped early, otherwise `warning` if there is one, then `next`.
- `done[].id` is the id actually run (it may be a re-found index). `not_run` lists the ids as sent, from the first one that did not run.
- `stopped` texts, in the order they are checked:
  - `The bot was stopped with /stop.`
  - `Nothing changed after '<label>'. The game may have ignored it; re-check the state before repeating it.`
  - `The screen changed to <screen>.`
  - `The game is still busy (<waiting, or "no decision yet">). Call wait_for_decision before acting again.`
  - `Your turn ended after '<label>'.` (`state.turn` or `state.round` changed; both are absent off combat, so this never fires there)
  - `Paused by <name>. Call wait_for_decision; you can still talk with say.`
  - `A chat message arrived.`
  - `'<label>' is no longer offered.`
  - `The game refused '<label>': <refusal>` (that id is put in `not_run`)

#### Refusals

Argument errors return `{"error": "<text>"}` with `isError` true and no snapshot:

- `action_id is required`
- `actions must be a list of action ids`
- `actions takes 1 to 12 ids; you sent <n>.`
- `version is required`
- `end_turn can only be the last id, since the turn is over once it runs. Nothing was played.`
- `Unknown tool '<name>'`
- `<ExceptionType>: <message>`

`say` and `get_persona` refuse the same way, `{"error": "<text>"}` with `isError` true and no snapshot:

- `say` with empty or blank `text`: `Nothing to say: text is empty.`
- `say` past the burst limit: `Too many chat lines: at most 6 in 10 seconds. Slow down: wait a little and keep it brief.`
- `get_persona` before a character is known: `no character yet; call get_persona again once the run has started`

State refusals return `{"error": "<text>", "current": <snapshot>}` with `isError` true. Nothing ran. They are checked in this order:

1. `Paused by <name>. Call wait_for_decision; you can still talk with say.`
2. `Snapshot changed (you sent version <v>, current is <c>). Pick from the current actions.`
3. `Not your decision right now (<waiting, or screen>). Call wait_for_decision.`
4. For `act`, `'<id>' is not a legal action now.` For `act_sequence`, `'<id>' is not a legal action now, so nothing was played.` (the first illegal id)

When the game refuses at execution time, `act` returns `{"error": "<game's text>", "current": <snapshot captured right after>}`, and `act_sequence` stops with `The game refused '<label>': <text>`. If the game gave no text, it is `The game refused the action.` Texts from the bridge (`GameBridge.ExecuteAsync`), which first captures again on the main thread:

- `Still resolving <what>.`
- `The game is busy.`
- `Not your decision right now (<waiting, or screen>).` (no "Call wait_for_decision" here)
- `'<id>' is no longer available on the <screen> screen.`
- `<ExceptionType>: <message>`

Then the screen's own refusals. Every screen re-checks right before it clicks. Common ones:

- `No run in progress.`
- `<thing> is gone.`
- `<thing> was already clicked; waiting for the game to react.` (a node clicked within the last 2.5 s)
- `Unknown <screen> action '<id>'.`

Each screen section lists its own. Because BotTools checks the snapshot first, the bot normally sees game-side refusals only in races, such as a teammate acting or an animation finishing between the check and the click.

### get_deck

`Describe.Deck` writes keys in this order:

```json
{
  "character": "The Ironclad",
  "hp": 80, "max_hp": 80, "gold": 99,
  "run": {"act": 1, "floor": 1, "ascension": 0},
  "deck_size": 10,
  "deck": [
    {"name": "Strike", "cost": "1", "type": "Attack", "text": "Deal 6 damage.", "rarity": "Basic"},
    {"name": "Bash+", "cost": "2", "type": "Attack", "text": "Deal 10 damage. Apply 3 Vulnerable.", "rarity": "Basic", "upgraded": true}
  ],
  "relics": [{"name": "Burning Blood", "text": "At the end of combat, heal 6 HP."}],
  "potions": [{"name": "Fire Potion", "text": "Deal 20 damage to an enemy.", "target": "AnyEnemy"}]
}
```

- `deck` has **one entry per card in deck order**, not grouped. Each entry is a card offer, plus `upgraded: true` when the card is upgraded.
- `relics` are in pickup order, each with its live text.
- `potions` lists filled slots only, with no `slot` key. The snapshots say which slot each potion is in.
- Outside a run it returns `{"error": "no run in progress"}`; if the read throws, `{"error": "reader error: <Type>: <message>"}`. Both have `isError` **false**, because BotTools wraps the bridge's object as is.
- `FakeGame.DescribeDeckAsync` differs from the real game: it groups cards with a `count` key, uses integer costs, and has no `rarity` or `run`. The arena should copy the real shape above, not FakeGame.

### Routing order

`GameBridge.CaptureAsync` runs on the main thread:

1. Read the game context (run, local player, combat, co-op type) and poll the tracked request.
2. If a tracked request is still pending, return busy `"resolving <what>"` under the last screen name.
3. `ScreenRouter.Resolve`:
   1. A different run object than last time clears the per-run latches (`ProceededFrom`, the end-turn latch, the act vote, click latches).
   2. Busy gates, in this order:
      - game starting up
      - screen transition
      - run cleaning up
      - loading (co-op)
      - run loading (run scene without its UI yet)
      - travelling on the map
      - co-op network timeout overlay
   3. If the feedback screen or a card or relic inspect screen is open, return `unknown` and wait for the human.
   4. If a popup is open in the modal container: **`modal`**.
   5. If a deck view or the pause menu (`ICapstoneScreen`) is open, return `unknown` and wait for the human.
   6. If not in a run, or the main menu exists: **`game_over`** if the top overlay is the game over screen, otherwise **`menu`**.
   7. If the map is open: **`map`**. It draws over a fight's rewards screen, which stays on the overlay stack until the room changes.
   8. The top of the overlay stack:
      - `NGameOverScreen` gives **`game_over`**.
      - `NRewardsScreen` gives **`rewards`**.
      - `NCardRewardSelectionScreen` gives **`card_reward`**.
      - `NCrystalSphereScreen` gives **`crystal_sphere`**.
      - The deck card select, upgrade, transform, enchant, simple, combat pile, choose-a-card and choose-a-bundle screens give **`card_select`**.
      - Any other overlay gives `unknown`, with the overlay's type name in `waiting`.
   9. If combat is in progress: **`combat`**, or **`hand_select`** while the hand is in select mode. A fight started by an event comes here too.
   10. The room the game's screen context reports: combat room gives `combat`, event room gives **`event`** (Neow and the other Ancients are events with `state.ancient: true`), treasure room gives **`treasure`**, rest site gives **`rest`**, merchant room gives **`shop`**.
   11. Otherwise, fall back on the room type: `Event`, `Treasure`, `RestSite`, `Shop`. Anything else is `unknown`, with `"between rooms"` or `"nothing to do in this <RoomType> room yet"`.
4. The reader that matched builds the snapshot. If it throws, the result is `screen: "error"`.

`ExecuteAsync` runs the same steps, then:

1. Captures the matched screen again.
2. Refuses if that capture is not a decision, or does not offer the id.
3. Calls the screen's `Execute`.

When `proceed` succeeds (except on a non-terminal rewards screen, such as Orrery's), or an event option labelled `Leave...` succeeds, it records the current room in `Latches.ProceededFrom`. The map reader only travels from a room the bot has left this way, or at act start. A map opened by hand over an unfinished room waits instead.

### Action id grammar (summary)

From CLAUDE.md. Each screen's section has the details.

| Screen | Ids |
|---|---|
| `combat` | `play:<i>`, `play:<i>>e<j>`, `play:<i>>p<j>`, potion ids, `end_turn` |
| `hand_select` | `select:<i>`, `confirm` |
| `map` | `map:<n>`, potion ids |
| `rewards` | `reward:<n>`, `proceed`, potion ids |
| `card_reward` | `card:<n>`, `skip`, `reroll`, `sacrifice` |
| `card_select` | `card:<n>`, `bundle:<n>`, `confirm`, `cancel`, `skip` |
| `shop` | `open`, `close`, `buy:card<n>`, `buy:colorless<n>`, `buy:relic<n>`, `buy:potion<n>`, `buy:remove`, `proceed`, `dismiss` (tutorial tip), potion ids |
| `rest` | `option:<n>`, `proceed`, potion ids |
| `event` | `option:<n>` (Leave is one of these), potion ids |
| `treasure` | `open`, `relic:<n>`, `proceed`, potion ids |
| `crystal_sphere` | `cell:<x>,<y>`, `tool:big`, `tool:small`, `proceed` |
| `modal` | `dismiss` (tutorial popups only) |
| `game_over` | `continue` |
| `menu`, `unknown`, `error` | none |

### Examples

Busy while a card resolves:

```json
{"version": 214, "screen": "combat", "awaiting_decision": false, "waiting": "resolving Strike", "state": {}, "actions": []}
```

Between rooms:

```json
{"version": 57, "screen": "unknown", "awaiting_decision": false, "waiting": "between rooms", "state": {}, "actions": []}
```

Paused during an enemy turn. The state is combat's waiting state; the waiting reason and actions come from the paused view:

```json
{"version": 301, "screen": "combat", "awaiting_decision": false, "waiting": "paused by Mark", "state": {"turn": 3, "round": 3, "hp": 61, "block": 5}, "actions": [], "chat": {"you_are": "Ironclad", "paused": true}}
```

The shared blocks on a map decision. Map-specific keys such as the nodes and `next` are left out here; see Map. Fire Potion offers only a discard off combat; Blood Potion can be used:

```json
{"version": 88, "screen": "map", "awaiting_decision": true,
 "state": {
   "run": {"act": 1, "floor": 3, "ascension": 0},
   "player": {"character": "The Ironclad", "hp": 66, "max_hp": 80, "gold": 131, "relics": ["Burning Blood"], "deck_size": 11, "deck": {"Bash": 1, "Defend": 4, "Pommel Strike": 1, "Strike": 5}},
   "potions": [
     {"slot": 0, "name": "Fire Potion", "text": "Deal 20 damage to an enemy.", "target": "AnyEnemy", "usable": false},
     null,
     {"slot": 2, "name": "Blood Potion", "text": "Heal for 20% of your Max HP.", "target": "AnyPlayer", "usable": true}
   ]
 },
 "actions": [
   {"id": "map:0", "label": "Go to Monster at (1,3)"},
   {"id": "map:1", "label": "Go to Rest Site at (3,3)"},
   {"id": "discard_potion:0", "label": "Discard Fire Potion"},
   {"id": "potion:2", "label": "Use Blood Potion"},
   {"id": "discard_potion:2", "label": "Discard Blood Potion"}
 ]}
```

(The exact map label wording is the Map section's.)

`act` refused as stale. The `current` snapshot is shortened here:

```json
{"error": "Snapshot changed (you sent version 231, current is 233). Pick from the current actions.", "current": {"version": 233, "screen": "combat", "awaiting_decision": true, "state": {"...": "..."}, "actions": [{"id": "play:0>e0", "label": "Play Strike on Jaw Worm"}, {"id": "end_turn", "label": "End turn"}]}}
```

An `act_sequence` that stopped early. The `next` snapshot is shortened here:

```json
{"done": [{"id": "play:2>e1", "label": "Play Bash on Louse (e1)"}, {"id": "play:0>e1", "label": "Play Strike on Louse (e1)"}],
 "stopped": "'Play Strike on Louse (e1)' is no longer offered.",
 "not_run": ["play:3>e1", "end_turn"],
 "next": {"version": 240, "screen": "combat", "awaiting_decision": true, "state": {"...": "..."}, "actions": [{"id": "play:0>e0", "label": "Play Strike on Louse"}, {"id": "end_turn", "label": "End turn"}]}}
```

### Co-op differences at the envelope level

- `team` appears on `map`, on `rest`, and on `event` when the event is shared.
- More waiting reasons:
  - `"waiting on the network"` (timeout overlay)
  - combat's `"waiting for teammates to end turn"`, `"you are down; teammates fight on"`, `"a teammate is taking an extra turn"`
  - the readers' vote waits (map, events, treasure, the act vote on rewards)
- Potions: `AnyPlayer` potions that can be thrown get one `>p<j>` id per living player in combat, labelled `"on yourself"` for you. Twin tags `" (p<j>)"` mark players who share a name.
- What teammates do changes your snapshot, so the version moves without you. That causes more stale refusals and game-side refusals from races.
- `chat.messages[].teammate: true` marks relayed bot lines. They never interrupt a wait.

### Left out on purpose

- Snapshots carry deck **name counts** only. Card text for the whole deck, relic text and potion text without slots come from `get_deck`. Pickers that remove or upgrade cards list their cards with text.
- Combat piles are name counts only; draw order is hidden from humans too.
- No seed, RNG, timestamps or internal ids. Teammates appear only as HP lines off combat, never their decks or relics.
- Popups other than tutorials (errors, mod consent, abandon, quit, disconnect, "enable tutorials?") offer no actions. Neither do deck views, the pause menu, inspect screens or unknown overlays. They wait for the human.
- Action labels and `waiting` are left out of the version hash, so rewording them never makes an id stale.

### Arena checklist (what Core logic relies on)

1. **Busy while resolving.** After a successful `ExecuteAsync`, report `awaiting_decision: false` until the action has fully landed. `DecisionWaiter` hands over the first state that has kept the same version for 300 ms. A half-updated decision would be handed over.
2. **Every accepted action changes the snapshot.** It must change `screen`, `awaiting_decision`, `state` or an action id. Otherwise `act` waits the full 20 s for a newer version, then warns "Nothing changed after this action...", and `act_sequence` stops with "Nothing changed after '<label>'...".
3. **Empty `actions` whenever `awaiting_decision` is false.** Keep the waiting state's `state` the way the real reader builds it: `{}` for busy snapshots, full state for reader waits, combat's small waiting state.
4. **Labels exactly as the real readers write them**, including twin tags `" (e<j>)"` and `" (p<j>)"`. `act_sequence` finds later steps by label.
5. **Hand entries.** `state.hand[]` must use the same keys as the real game. `act_sequence` compares them, without the placement keys `i`, `playable`, `unplayable`, `vs`, `selected` and `selectable`, to pick between copies of a card.
6. **`state.turn` and `state.round` in `combat` and `hand_select`.** A change in either is how `act_sequence` sees a turn ended by a card.
7. **Screen names.** `combat` and `hand_select` skip the speech wait. The brain's in-run detection (`RunScreens`) treats `combat`, `hand_select`, `map`, `rewards`, `card_reward`, `card_select`, `shop`, `rest`, `event`, `treasure` and `crystal_sphere` as in a run, and `menu` and `game_over` as outside one.
8. **Refusal texts from `ExecuteAsync`** as listed above and in each screen's section. `act` passes them through word for word, and `act_sequence` quotes them in `stopped`.
9. **`get_deck` in the real shape.** One entry per card, with string costs, `rarity`, `run` and filled potion slots only. Not FakeGame's grouped shape.
10. **Potion ids on exactly the screens and under exactly the conditions above.** That includes the rewards special case: potion ids alone are a decision only when a potion reward is blocked by full slots.

## Combat (`combat`) and hand selection (`hand_select`)

Built by `src/SpireMind/Game/Screens/CombatReader.cs`, with cards, creatures, powers and intents from `src/SpireMind/Game/Describe.cs` and potions from `src/SpireMind/Game/Screens/PotionActions.cs`. Both screen names come from the one reader: `combat` while the local player plays cards, `hand_select` while the hand is in select mode (a card or potion asked for cards from the hand). Everything below is the local player's view; in co-op every bot gets its own snapshot.

All text goes through `TextCleaner.Clean` (see the snapshot envelope rules). An upgraded card's name already ends in `+` (or `+N`).

### When the bot sees it

- `combat` whenever `CombatManager.IsInProgress` is true and nothing ranks above the fight in `ScreenRouter`: hallway, elite and boss fights, and fights an event starts (they run inside the event's room; `state.run.room` then says `Event`). The router checks, in order: busy gates (screen transition, loading, map travel, network wait), a human-opened feedback or inspect screen, a modal (tutorial tips and FTUE popups: the `modal` screen with `dismiss`), a deck view or pause menu, the open map, then the overlay stack, and only then the fight. So a card choice from a pile or a "choose 1 of 3" pick that opens an overlay mid-fight is the `card_select` screen, not combat; an overlay with no reader gives screen `unknown`, not awaiting, with `waiting` "a screen the bot can't read is open (<type>)".
- `awaiting_decision` is true with the full state only while the local player may act (the may-act gate below). Otherwise the screen is still `combat` but not awaiting, with a small waiting state (enemy turn, teammates, animations).
- `hand_select` whenever the hand is in card selection mode (`NPlayerHand.IsInCardSelection`), checked before the may-act gate. It can open mid-card, from a potion, or at end of turn (a retain or discard choice), so it can appear when a plain combat snapshot would be waiting.
- The fight ends when `IsOverOrEnding` turns true: from then the snapshot is a busy `combat` with `waiting` "combat ending" until the rewards (or game over) screen takes over.

The common envelope applies, with `screen` `combat` or `hand_select`. While the human has paused the bot, the paused view keeps the real `state` and the snapshot's own version.

### Combat snapshot: deciding (`awaiting_decision: true`)

`state` keys, in order:

| Key | Type | Present | Meaning |
| --- | --- | --- | --- |
| `run` | object | always | `{"act", "floor", "ascension", "room"}`. `act` is 1-based (`CurrentActIndex + 1`), `floor` is `RunState.TotalFloor`, `ascension` the ascension level, `room` the current room's `RoomType` as a string: `Monster` (hallway), `Elite`, `Boss`, or `Event` for an event's fight. `room` is omitted only if the run has no current room. |
| `turn` | integer | always | `PlayerCombatState.TurnNumber`: the local player's turn count, restarting at 1 each fight. |
| `round` | integer | always | `CombatState.RoundNumber`. `act_sequence` treats a change in `turn` or `round` as "your turn ended". |
| `energy` | integer | always | Energy left now. Can exceed `max_energy` (energy potions and the like). |
| `max_energy` | integer | always | `PlayerCombatState.MaxEnergy`, already modified by relics and powers. The energy each turn starts with. |
| `stars` | integer | Regent, or any character with stars not 0 | Regent's star count. Omitted for other characters while they have 0. |
| `me` | object | always | The local player's creature, see below. There is no `player` key in combat. |
| `pets` | array | only when the player has pets | Necrobinder's Osty and any other pet: each a creature object (`name`, `hp`, `max_hp`, `block?`, `powers?`) with no index. Pets are never targets of `p` ids. |
| `teammates` | array | co-op only (more than one player in the fight) | Other players, see Co-op. |
| `enemies` | array | always | Enemies still in the fight, see below. |
| `incoming_damage` | integer | always, unless computing it threw | Sum of every attack intent's total damage (`AttackIntent.GetTotalDamage`) over living enemies that are not stunned. Includes Strength, Weak and the player's Vulnerable. The player's Block is not subtracted. Non-attack damage (Constrict, end-of-turn effects) is not in it. `0` when nobody attacks. |
| `hand` | array | always | Hand cards left to right as on screen, see below. |
| `draw_pile` | object | always | `{"count": int, "cards": {name: count}}`. Names sorted ordinally; draw order is never shown. `Strike` and `Strike+` are separate keys. |
| `discard_pile` | object | always | Same shape as `draw_pile`. |
| `exhaust_count` | integer | always | Number of cards in the exhaust pile. Contents are not listed. |
| `potions` | array | always | Detailed potion slots, one per slot (`MaxPotionCount`, 3 at run start), see Shared blocks. `usable` is true when at least one `potion:<slot>` id is offered right now. |
| `relics` | array of strings | always | Relic names only, in the player's relic order. No text or counters (`get_deck` has the text). |

#### `state.me`

`{"name", "hp", "max_hp", "block"?, "powers"?, "orb_slots"?, "orbs"?}`

- `name`: the player creature's name. It is the character's title (`"The Ironclad"`, `"The Silent"`...); `play.md` relies on it naming the character in combat.
- `hp`, `max_hp`: integers.
- `block`: integer, only when above 0.
- `powers`: only when the creature has at least one visible power (format below).
- `orb_slots` and `orbs`: only when the orb queue has capacity above 0 or holds orbs (Defect). `orb_slots` is the capacity; `orbs` is an array of `{"id": string, "passive": int, "evoke": int}`, where `id` is the orb model's id entry and `passive` / `evoke` already include Focus (truncated to integers). `orbs[0]` is evoked next. If reading orbs throws, both keys are simply left out.

#### Powers (on `me`, enemies, teammates, pets)

The shared power format (see Creatures and powers under Shared blocks).

#### `state.enemies[]`

One entry per enemy that is alive and still in the fight, in id order. Keys in order:

| Key | Type | Present | Meaning |
| --- | --- | --- | --- |
| `i` | integer | always | The enemy's id number: `e<i>` in action ids. Each enemy keeps the index it got when it first appeared for the whole fight; dead and fled enemies are dropped from the list but their index is never reused, so the list has gaps (e.g. `i` 1 and 2 with e0 dead). A summoned enemy gets the next unused index. |
| `name` | string | always | Display name. |
| `hp`, `max_hp` | integer | always | |
| `block` | integer | when above 0 | Current Block. Block from an enemy's move lasts through the player's whole next turn. |
| `powers` | array | when any visible | The shared power format. |
| `intents` | array of strings | always | The enemy's next move, see below. Can be empty (no move chosen yet). |
| `primary` | `true` | only when the living enemies are mixed | Present on the primary enemies when some living enemies are primary and some are not (minions or summons). Never `false`. |
| `untargetable` | `true` | when the enemy is not hittable | Present when `IsHittable` is false. Such an enemy appears in no target id and has no `vs` entry. |

Intent strings, one per intent of the move, in the move's order:

- `"stunned"` alone when the move is the stun move.
- `"attack N"` for a single hit or `"attack NxH"` for H hits. N is per-hit damage after the enemy's Strength and Weak and the player's Vulnerable (`AttackIntent.GetSingleDamage`). A death-blow attack is also shown as `attack`.
- Any other intent: its `IntentType` lowercased (`buff`, `debuff`, `defend`, `statuscard`, `carddebuff`, `summon`, `sleep`...), followed by `": <label>"` when the intent's on-screen label is non-empty and differs from the kind, e.g. `"statuscard: 1"`. A defend intent does not say how much Block.
- `["unknown"]` if reading the intents threw.

#### `state.hand[]`

Keys in order: `i`, `name`, `cost`, `type`, `text`?, `enchant`?, `vs`?, `playable`, `unplayable`?.

| Key | Type | Present | Meaning |
| --- | --- | --- | --- |
| `i` | integer | always | Position in the hand from the left, 0-based. The `<handIndex>` of `play:` ids. Shifts after every play; `act_sequence` re-finds later ids by label. |
| `name` | string | always | Card title, ending in `+` when upgraded. |
| `cost` | string | always | The displayed energy cost with every modifier: `"0"`, `"1"`, `"2"`..., `"X"` for X cost, `"-"` when the card has no cost (unplayable status and curse cards), `"?"` if reading the cost threw. A star cost is appended: `"1+2*"` for 1 energy and 2 stars, `"+X*"` appended for an X star cost. A string, never a number. |
| `type` | string | always | `CardType` name: `Attack`, `Skill`, `Power`, `Status`, `Curse`. |
| `text` | string | when non-empty | The card text as the hand shows it with no enemy hovered: numbers include the player's own modifiers (Strength, Weak and Shrink for damage, Dexterity and Frail for Block) but not the target's. |
| `enchant` | string | when enchanted | The enchantment's title. |
| `vs` | object | only when some enemy's text differs | `{"e<j>": text}`: the card's text as it reads hovering enemy j, for each targetable enemy (alive, in the fight, hittable, allowed by targeting hooks) whose text differs from `text`. Computed only for cards whose target type is a single enemy (`AnyEnemy`) or all enemies (`AllEnemies`); random-target and self cards never have it. No entry for an enemy means `text` holds against it. Neither `text` nor `vs` counts enemy Block or a cap on HP lost per hit. Computed for every such card, playable or not. |
| `playable` | bool | always | True when at least one `play:<i>...` id is offered for this card. |
| `unplayable` | string | whenever `playable` is false | Why: `"not enough energy"`, `"not enough stars"`, `"no valid target"` (the card could be played but no target passes, e.g. an ally-target card in singleplayer), `"unreadable (<ExceptionType>)"`, or otherwise the game's `UnplayableReason` flags as text (comma-separated flag names). Energy wins over every other reason. |

The hand entry does not list targets; the legal targets of a card are exactly the `play:<i>>...` ids offered for it.

### Combat snapshot: waiting (`awaiting_decision: false`)

When the fight is running and readable but the local player may not act, the screen is `combat`, `actions` is empty, `waiting` gives the reason, and `state` is small:

`{"turn": int, "round": int, "hp": int, "block": int, "teammates"?: [{"i": int, "name": string, "ended_turn": bool}]}`

Here `block` is always present (0 included). `teammates` appears only in co-op. No hand, enemies, potions or piles.

The may-act gate, checked in this order; the first that fails is the `waiting` text:

| `waiting` | When |
| --- | --- |
| `you are dead` (singleplayer) / `you are down; teammates fight on` (co-op) | The local creature is dead. |
| `resolving <what>` | A request of ours is still in flight (normally reported earlier, as a busy snapshot with empty state). |
| `enemy turn` | The current side is not the players'. |
| `ending turn` (singleplayer) / `waiting for teammates to end turn` (co-op) | The game marks us ready to end the turn, or we sent end turn this turn and the latch still holds: for 12 s after the request, and after that only while the game has us ready, has player actions disabled or the action queue is busy. Once 12 s have passed with none of those, the request counts as dropped and the gate opens again. |
| `not your turn` (singleplayer) / `a teammate is taking an extra turn` (co-op) | `IsPartOfPlayerTurn` is false. |
| `turn phase <Phase>` | The player's turn phase is not `Play` (start or end of turn running). |
| `turn changing` | The action queue synchronizer is not in its play phase. |
| `resolving` | The game has player actions disabled. |
| `combat paused` | Singleplayer pauses the fight while the map or a deck view is open. |
| `hand not ready` | No live hand node yet. |
| `card in play` | The hand is not in plain play mode, or a card is mid-play. |
| `peeking at the board` | The human is using the peek button. |
| `a screen is open` | Something is on the overlay stack. |

### Busy combat snapshots

`state` is `{}` and `actions` is empty in these:

- From the reader, before the board can be read: `waiting` is `combat not running`, `combat ending`, `combat loading`, or `combat room in <Mode> mode` (an event's fight that has not switched to live combat yet, or a reloaded finished fight).
- From `GameBridge`, while one of our requests resolves: `waiting` is `resolving <what>` and `screen` is the last routed screen's reader name, which is `combat` even when the request came from `hand_select`. `<what>` is the card name for a play (`resolving Strike`), `end turn`, the potion name for a use, `discarding <potion>`, `selecting <card>` for a hand pick, `the selection` for a confirm. Each wait ends when the effect is seen (card left the hand or its cost was paid and the queue is idle, potion slot changed, turn moved on, a choice or screen opened) or on a timeout: play 6 s, end turn 5 s, potion use 6 s, discard 4 s, hand pick 3 s, confirm 6 s.
- From the router: `screen transition`, `loading`, `run loading`, `travelling on the map`, `waiting on the network`, `run cleaning up`, labelled with the last screen name.

### Combat action ids

Offered only when deciding. Order in `actions`: card plays in hand order (for each card, its targets in enemy or player index order), then potion actions slot by slot (each slot's uses, then its discard), then `end_turn` last.

| Id | Label | What it does | Offered when |
| --- | --- | --- | --- |
| `play:<i>` | `Play <card>` | Plays hand card i with no target (`CardModel.TryManualPlay(null)` on the synced action queue). Used for every target type except single enemy and single ally: self, all enemies, random enemy, all allies, any player, Osty, none. The game picks. | `card.CanPlay` is true and `CanPlayTargeting(null)` is true. |
| `play:<i>>e<j>` | `Play <card> on <enemy>` | Plays hand card i at enemy j. | The card targets one enemy (`AnyEnemy`), enemy j is alive, in the fight, hittable, allowed by the targeting hooks, and `CanPlayTargeting(enemy)` is true. One id per such enemy. |
| `play:<i>>p<j>` | `Play <card> on <player>` | Plays hand card i at player j (co-op). | The card targets one ally (`AnyAlly`) and player j is another living player the hooks allow and the card accepts. Never the local player, never a pet. |
| `potion:<slot>` | `Use <potion>` | Uses the potion in that slot with no target (`PotionModel.EnqueueManualUse(null)`); the game applies it to the player for self potions, to all or random enemies, or to no creature. | Slot filled, potion not already queued, usage is not automatic, the potion's own usability check passes, target type all enemies, random enemy, self, targeted-no-creature, or any player when it cannot be thrown at an ally, and the null or own-creature target is valid. |
| `potion:<slot>>e<j>` | `Use <potion> on <enemy>` | Uses a single-enemy potion on enemy j. | Target type `AnyEnemy`, enemy j targetable as for cards and `IsValidTarget(enemy)`. |
| `potion:<slot>>p<j>` | `Use <potion> on yourself` or `Use <potion> on <player>` | Uses an any-player potion on player j, the local player included. | Target type `AnyPlayer` and `CanThrowAtAlly()` is true; one id per living player the potion accepts. |
| `discard_potion:<slot>` | `Discard <potion>` | Throws the potion away (`DiscardPotionGameAction` on the synced queue). | Slot filled and the potion not queued. |
| `end_turn` | `End turn` | Requests `EndPlayerTurnAction` for this turn number on the synced queue and latches it. The latch holds for 12 s. After that, if the game has not marked the player ready, player actions are still enabled and the action queue is idle (the request was dropped), the latch is ignored and `end_turn` is offered again with the same turn number. In co-op it only marks the player ready; the turn ends when every player is ready. | Always, while deciding. |

Potion ids, like card ids, exist only while the may-act gate is open: no potion actions while waiting, during an enemy turn, during `hand_select`, or while any request of ours is in flight. Every potion id requires the player alive and `Player.CanRemovePotions`. A potion read that throws loses only its use ids; its discard stays.

Target names in labels: `<enemy>` is the enemy's name, with ` (e<j>)` appended while another enemy of the fight that is still alive (a fled one counts) shares that name (`Play Strike on Nibbit (e1)`); `<player>` is the teammate's creature name (or character title when that is empty) with ` (p<j>)` appended while another player shares it.

#### Refusals

`act` and `act_sequence` check the request against a fresh snapshot first, then the mod's `GameBridge` re-checks on the game thread, then the reader. The Core and bridge texts are the common ones (see Refusals under The common envelope); the bridge's re-check says `'<id>' is no longer available on the combat screen.` or `on the hand_select screen.`

From the combat reader itself, reachable only in a race or when the game rejects the request:

- `No fight to act in (<why>).` and `Cannot act now (<why>).`, with the busy or waiting reasons above.
- `play:`: `'<id>' is not playable any more.`; `The game refused to play <card>.` when `TryManualPlay` returns false.
- `potion:` / `discard_potion:`: `'<id>' is not available now.`; discard also `The action queue is not ready.`
- `end_turn`: `This turn is already ended.`; `The action queue is not ready.`
- Any other id: `Unknown combat action '<id>'.`

After `end_turn`, the `next` of `act` is normally the next turn's `combat` snapshot, a `hand_select` (an end-of-turn choice), the rewards screen, or game over.

### Example: combat

Ironclad, act 1 floor 5, the weak slimes fight. Turn 1 killed Leaf Slime (S) (e0, now a gap); turn 2 Bashed the Twig Slime (M). Card, power and potion text here is illustrative.

```json
{
  "version": 112,
  "screen": "combat",
  "awaiting_decision": true,
  "state": {
    "run": {"act": 1, "floor": 5, "ascension": 0, "room": "Monster"},
    "turn": 3,
    "round": 3,
    "energy": 3,
    "max_energy": 3,
    "me": {"name": "The Ironclad", "hp": 52, "max_hp": 80},
    "enemies": [
      {"i": 1, "name": "Twig Slime (M)", "hp": 10, "max_hp": 27,
       "powers": [{"name": "Vulnerable", "amount": 1, "kind": "Debuff", "text": "Takes 50% more damage from attacks for 1 turn."}],
       "intents": ["attack 11"]},
      {"i": 2, "name": "Twig Slime (S)", "hp": 9, "max_hp": 9, "intents": ["attack 4"]}
    ],
    "incoming_damage": 15,
    "hand": [
      {"i": 0, "name": "Strike", "cost": "1", "type": "Attack", "text": "Deal 6 damage.", "vs": {"e1": "Deal 9 damage."}, "playable": true},
      {"i": 1, "name": "Twin Strike", "cost": "1", "type": "Attack", "text": "Deal 5 damage 2 times.", "vs": {"e1": "Deal 7 damage 2 times."}, "playable": true},
      {"i": 2, "name": "Defend", "cost": "1", "type": "Skill", "text": "Gain 5 Block.", "playable": true},
      {"i": 3, "name": "Defend", "cost": "1", "type": "Skill", "text": "Gain 5 Block.", "playable": true},
      {"i": 4, "name": "Slimed", "cost": "1", "type": "Status", "text": "Draw 1 card. / Exhaust.", "playable": true}
    ],
    "draw_pile": {"count": 7, "cards": {"Bash": 1, "Defend": 2, "Strike": 4}},
    "discard_pile": {"count": 0, "cards": {}},
    "exhaust_count": 0,
    "potions": [{"slot": 0, "name": "Fire Potion", "text": "Deal 20 damage to an enemy.", "target": "AnyEnemy", "usable": true}, null, null],
    "relics": ["Burning Blood"]
  },
  "actions": [
    {"id": "play:0>e1", "label": "Play Strike on Twig Slime (M)"},
    {"id": "play:0>e2", "label": "Play Strike on Twig Slime (S)"},
    {"id": "play:1>e1", "label": "Play Twin Strike on Twig Slime (M)"},
    {"id": "play:1>e2", "label": "Play Twin Strike on Twig Slime (S)"},
    {"id": "play:2", "label": "Play Defend"},
    {"id": "play:3", "label": "Play Defend"},
    {"id": "play:4", "label": "Play Slimed"},
    {"id": "potion:0>e1", "label": "Use Fire Potion on Twig Slime (M)"},
    {"id": "potion:0>e2", "label": "Use Fire Potion on Twig Slime (S)"},
    {"id": "discard_potion:0", "label": "Discard Fire Potion"},
    {"id": "end_turn", "label": "End turn"}
  ]
}
```

The same fight a moment after `end_turn`, and while a play resolves:

```json
{"version": 113, "screen": "combat", "awaiting_decision": false, "waiting": "enemy turn", "state": {"turn": 3, "round": 3, "hp": 52, "block": 5}, "actions": []}
{"version": 114, "screen": "combat", "awaiting_decision": false, "waiting": "resolving Strike", "state": {}, "actions": []}
```

### Hand selection (`hand_select`)

The bot sees it while the hand is in select mode: a card or potion asked for cards from the hand (exhaust, discard, retain, put on top, upgrade...), mid-card or at end of turn. It replaces the combat snapshot until the choice is confirmed or select mode closes by itself.

`state` is the full combat board (same keys and order as the deciding combat snapshot, with `run`, `turn`, `round`, `energy`, `max_energy`, `stars`?, `me`, `pets`?, `teammates`?, `enemies`, `incoming_damage`?, `hand`, `draw_pile`, `discard_pile`, `exhaust_count`, `potions`, `relics`), plus `selection` last. The differences:

- `hand[]` entries are `{"i", "name", "cost", "type", "text"?, "enchant"?}` plus either `"selected": true` (already picked) or `"selectable": bool` (can be picked now). No `vs`, `playable` or `unplayable`. The card being played has normally already left the hand (a played card leaves the hand before its effect). Cards the selection's filter rejects (the game hides them) are listed with `selectable: false`.
- Every potion shows `usable: false` (the may-act gate is closed), and no potion actions are offered.
- `selection`: `{"mode", "prompt"?, "min"?, "max"?, "selected"?}`.

| Key | Type | Present | Meaning |
| --- | --- | --- | --- |
| `mode` | string | always | `"upgrade"` for an upgrade pick (the hand's UpgradeSelect mode), else `"select"`. |
| `prompt` | string | when known and non-empty | The game's header, e.g. `Choose a card to Exhaust.` Read from the hand's own selector prefs, which last as long as this selection; if those are missing, from the latest hand-selection command seen in the last 30 s. |
| `min`, `max` | integer | when known | Fewest and most cards this selection takes. |
| `selected` | array of strings | when the picked list is readable (usually; `[]` before any pick) | Names of the cards picked so far, in pick order. |

`awaiting_decision` is true when the selection gate is open and at least one action exists. The selection gate is its own, much smaller than combat's: `waiting` is `you are dead`, `resolving <what>`, `a screen is open` or `peeking at the board`, or `selection not ready` when the gate is open but nothing can be pressed (for example the maximum is reached and confirm is not enabled yet). Not awaiting means empty `actions`, but `state` is still the full board.

Hand selection action ids:

| Id | Label | What it does | Offered when |
| --- | --- | --- | --- |
| `select:<i>` | `Select <card>` | Presses hand card i (emits the card holder's Pressed signal). The card moves to the picked list. Reaching the maximum can close select mode by itself and resume the card, without a confirm. | Fewer than `max` cards are picked, card i is not picked, passes the selection's filter and is shown in the hand. If that leaves nothing while confirm is disabled, every shown card is offered instead, so the bot never stalls on a filter it misread. |
| `confirm` | `Confirm selection (<n> chosen)`, or `Confirm selection` when the picked list cannot be read | Clicks the select-mode confirm button once. | The confirm button is enabled (enough cards picked, or the minimum is 0). |

There is no deselect: a picked card is never offered again. There is no `end_turn`, `play:` or potion id while selecting. Refusals, beyond the BotTools and GameBridge ones above (with `on the hand_select screen.`): `Cannot choose now (<why>).`, `The confirm button is not enabled yet.`, `'<id>' is not a hand selection action.`, `'<id>' cannot be selected now.`, `the confirm button was already clicked; waiting for the game to react.`, `the confirm button is gone.`, `<card> was already clicked; waiting for the game to react.`, `<card> is gone.` The next decision after a pick or confirm can be another `hand_select` (a follow-up choice), the combat snapshot, or another screen.

Example: the Ironclad played Burning Pact (1 energy) from a hand of Burning Pact, Strike, Twin Strike, Defend and Slimed. Text illustrative.

```json
{
  "version": 131,
  "screen": "hand_select",
  "awaiting_decision": true,
  "state": {
    "run": {"act": 1, "floor": 5, "ascension": 0, "room": "Monster"},
    "turn": 3,
    "round": 3,
    "energy": 2,
    "max_energy": 3,
    "me": {"name": "The Ironclad", "hp": 52, "max_hp": 80},
    "enemies": [
      {"i": 1, "name": "Twig Slime (M)", "hp": 10, "max_hp": 27,
       "powers": [{"name": "Vulnerable", "amount": 1, "kind": "Debuff", "text": "Takes 50% more damage from attacks for 1 turn."}],
       "intents": ["attack 11"]},
      {"i": 2, "name": "Twig Slime (S)", "hp": 9, "max_hp": 9, "intents": ["attack 4"]}
    ],
    "incoming_damage": 15,
    "hand": [
      {"i": 0, "name": "Strike", "cost": "1", "type": "Attack", "text": "Deal 6 damage.", "selectable": true},
      {"i": 1, "name": "Twin Strike", "cost": "1", "type": "Attack", "text": "Deal 5 damage 2 times.", "selectable": true},
      {"i": 2, "name": "Defend", "cost": "1", "type": "Skill", "text": "Gain 5 Block.", "selectable": true},
      {"i": 3, "name": "Slimed", "cost": "1", "type": "Status", "text": "Draw 1 card. / Exhaust.", "selectable": true}
    ],
    "draw_pile": {"count": 8, "cards": {"Bash": 1, "Defend": 3, "Strike": 4}},
    "discard_pile": {"count": 0, "cards": {}},
    "exhaust_count": 0,
    "potions": [{"slot": 0, "name": "Fire Potion", "text": "Deal 20 damage to an enemy.", "target": "AnyEnemy", "usable": false}, null, null],
    "relics": ["Burning Blood"],
    "selection": {"mode": "select", "prompt": "Choose a card to Exhaust.", "min": 1, "max": 1, "selected": []}
  },
  "actions": [
    {"id": "select:0", "label": "Select Strike"},
    {"id": "select:1", "label": "Select Twin Strike"},
    {"id": "select:2", "label": "Select Defend"},
    {"id": "select:3", "label": "Select Slimed"}
  ]
}
```

### Co-op differences

- `p<j>` indexes `CombatState.Players`, the fight's player list, which includes the local player. The local player's own index appears in no field; it shows only as the gap in `teammates[].i` and in `potion:<slot>>p<j>` ids labelled `on yourself`.
- Deciding state gains `teammates`: one entry per other player, `{"i", "name", "hp", "max_hp", "block"?, "powers"?, "ended_turn"}`, where `ended_turn` is whether that player is marked ready to end the turn. Dead teammates are not filtered out. Teammates carry no hand, energy, potions, orbs or pets. Waiting state gains `teammates: [{"i", "name", "ended_turn"}]`; there `name` falls back to the character title when the creature name is empty, while the deciding entry uses the creature name as is.
- `play:<i>>p<j>` exists only in co-op (single-ally cards need another living player; in singleplayer those cards show `unplayable: "no valid target"`). Any-player potions get one `potion:<slot>>p<j>` per living player when they can be thrown at an ally.
- `end_turn` only marks the player ready. Until every player is ready the snapshot waits with `waiting for teammates to end turn`. Undoing an end turn is never offered.
- Other waiting texts: `you are down; teammates fight on` when the local player is dead, `a teammate is taking an extra turn`.
- Enemy ids stay stable when a teammate kills an enemy or an enemy flees between snapshots: the roster keeps every enemy at its first index for the whole fight on each machine.
- Intent numbers and `incoming_damage` are computed with every player's creature passed as the target set; the snapshot does not say which player an attack is aimed at.
- Hand selection is per machine: a teammate's choice never shows up as `hand_select`, and the prompt capture ignores prompts for other players.

### Left out on purpose

- Draw order: `draw_pile` is name counts only (hidden from human players too). Exhaust pile contents (count only) and the play pile.
- Any card id, rarity, keyword list or upgrade flag beyond what `name` and `text` show. Full deck text is `get_deck`'s job.
- Relic text and counters (names only), gold, deck summary. `run` has only act, floor, ascension and room.
- Enemy move names, monster ids, future moves, and which player an attack targets. Dead and fled enemies.
- Enemy Block and per-hit HP caps in card numbers; random-target cards never get `vs`.
- Targets inside hand entries: targets are only expressed as action ids.
- Teammates' hands, energy, potions, orbs and pets.
- Deselecting in `hand_select`, undoing end turn, and potions while selecting or waiting.

### Notes for an implementation outside the game

- `src/SpireMind.Core/FakeGame.cs` is a stand-in for wiring tests and does not use the real shape: it has `state.player` `{hp, block, energy}` instead of `me` plus top-level `energy`, an enemy's single `intent` string instead of the `intents` array, hand entries with only `i`, `name` and an integer `cost`, and no `run`, `round`, `max_energy`, piles, potions or relics. Match the reader's shape above, not FakeGame's.
- `version`, the stale-version and not-legal refusals, the 300 ms settle and `act_sequence` all live in Core and come for free with an `IGameBridge`. The "no longer available on the <screen> screen" and "Not your decision right now (...)." re-checks live in the mod's `GameBridge`, so another bridge has to reproduce them to give the same texts.
- `act_sequence` relies on `state.turn` and `state.round` to see a turn end, on `state.hand[i]` (minus `i`, `playable`, `unplayable`, `vs`, `selected`, `selectable`, and then without `text`) to tell copies apart, and on the ` (e<j>)` / ` (p<j>)` twin tags in labels.

## Map

Reader: `src/SpireMind/Game/Screens/MapReader.cs`. Screen name `"map"`. `ScreenRouter` sends the bot here whenever `NMapScreen.Instance.IsOpen`. The map comes after the busy gates, the inspect and feedback screens, modals, and capstone screens a human opened (deck view, pause menu). It comes before the overlay stack and the room. So an open map wins over the terminal combat rewards screen that stays on the overlay stack under it, and a modal (the map-select tutorial on a fresh profile, for one) is shown as `screen: "modal"` with `dismiss` before the map.

### When the bot sees it

- **Act start.** In Act 1 with Neow, the run starts in the Neow room on the start point (row 0). The map opens when the bot leaves Neow through the event's `Leave` option (`option:<n>`). The start point already counts as visited then: `at` is the start point and the options are its children in row 1. In acts 2 and 3, the map opens with nothing visited and the only option is the start point, which is the act's Ancient.
- **After every room, once the bot leaves it:** combat rewards `proceed`, shop `proceed`, rest site `proceed`, treasure `proceed`, the Crystal Sphere's proceed, or an event's `option:<n>` labelled "Leave". The bot normally gets the map snapshot as the `next` of that `act` call.
- **Not after the act's last boss.** That boss's rewards `proceed` is the act vote and leads to the next act, not to the map. A map opened by hand there says "nowhere left to travel on this map; close the map to continue".
- **When a human opens it from the top bar.** Inside a room that isn't finished, the snapshot carries the full state but offers no travel (see the waiting reasons below). During a fight it is a busy snapshot.

The common envelope applies, with `screen` always `"map"`. `awaiting_decision` is true only when at least one `map:<n>` is offered.

### `state`

Keys come in this order: `run`, `player`, `potions`, `team`, `boss`, `second_boss`, `ancient`, `at`, `next`, `map`.

| key | type | present | meaning |
| --- | --- | --- | --- |
| `run` | object | always | The shared `run` block: `act`, `floor`, `ascension`. |
| `player` | object | always | The shared `player` block without `potions`: `character`, `hp`, `max_hp`, `gold`, `relics`, `deck_size`, `deck`. |
| `potions` | array | always | Detailed potion slots (see Shared blocks). `usable` is computed even in waiting snapshots, which offer no actions at all. |
| `team` | array of string | co-op only, with at least one teammate | The shared `team` lines, e.g. `"The Silent 41/70 hp"`. |
| `boss` | string | when the title is non-empty, and not while standing on the boss node or the second boss node | Title of the act's boss encounter (e.g. "Ceremonial Beast"). It is shown from the start of the act, as the boss node's art shows it to humans. See `boss` and `second_boss` under Shared blocks. |
| `second_boss` | string | only when the map has a second boss node and the title is non-empty, and not while standing on the second boss node | The second boss's title. It is shown before the first boss is beaten, as it is to humans. Golden Compass and Spoils maps have no second boss node, so the key is left out there even when the act rolled a second boss. |
| `ancient` | string | only while nothing is visited this act and the start point is Ancient-typed | Title of the act's Ancient. It never appears in Act 1: with Neow the start is already visited, and without Neow the start point is retyped to Monster. |
| `at` | string | when something is visited this act | `"(col,row) type"`: where the party stands, which is the last visited point. Example: `"(4,10) monster"`. |
| `next` | array | always (it can be empty) | The travel options. See "next and map:n numbering". |
| `map` | object | always | The compact map: `key`, `visited`, `rows`. |

### Coordinates, rows and room kinds

- **Coordinates** are written `"(col,row)"`, with no space. Both are integers counted from 0.
- **Rows.** Row 0 is the start point. Rows 1 to R-1 are the grid, where R is `ActMap.GetRowCount()`. The boss is on row R and a second boss on row R+1.
- **Act 1 rows.** By the floor table in strategy/wiki/run/act1.md (not MapReader), R is 16 in Act 1: row 0 is Neow (floor 1), row 1 is always Monster, row 9 is always Treasure, row 15 is always a Rest Site, and row 16 is the boss (floor 17).
- **Room kind words** (`type`) come from `MapPointType`: `monster`, `elite`, `rest`, `shop`, `treasure`, `unknown`, `boss`, `ancient`. Any other enum member is written lowercased.
- **Kinds never change.** They are fixed when the map is generated. An unknown point's room is rolled only on entry, and the reader always reads the point's own kind. So a visited `?` stays `unknown` in `at`, `visited` and `rows` after it turned out to be an event, fight, shop or treasure.
- **Display words in action labels:** Monster, Elite, Rest Site, Shop, Treasure, `Unknown (?)`, Boss, Ancient.

### `state.map`

- **`key`** (string, always). This exact constant:
  `rows go upward by row number; each node is cCOL:TYPE^CHILD_COLS (children are in the next row); @ marks you; (quest) marks a quest target. next[].ahead: min-max count of each room type over the routes from that node to the boss, itself included.`
- **`visited`** (string). Left out when nothing is visited this act. It lists every visited point of this act, oldest first, as `"(c,r) type"` joined by `", "`. The last entry is the current point (`at`).
- **`rows`** (object, always).
  - Each key is a row number as a string, in ascending order.
  - Each value is that row's nodes, ordered by column and separated by single spaces.
  - Node token: an optional `@` (you stand here), then `c<col>:<type>`, then an optional `(quest)`, then `^` and the children's columns joined by commas in ascending order. The `^` part appears only when the node has children, and children are always in the next row. Example: `"c4:monster^3,5"`.
  - `(quest)` means the point carries a quest marker. Per the code comment, Spoils Map and Fur Coat add these.
- **Which nodes appear in `rows`:**
  - the current point;
  - every point reachable from a `next` option by following child links, the options themselves included;
  - each boss node whose row is at or above the current row (or every boss node when nothing is visited).
- **What `rows` leaves out:** visited points other than the current one (they are in `visited`) and branches that can no longer be reached. With nothing visited, the start point is the only option, so the whole map is shown.
- **No `<`, `>`, `'` or `+` in map strings.** The debug HTTP routes serialize with the default encoder, which would escape them. The MCP output uses relaxed escaping.

### `next` and `map:<n>` numbering

Each entry is `{"n": int, "at": "(col,row)", "type": string, "ahead": string}`, in that key order. `n` is the entry's index and the `n` of its `map:<n>` id. `ahead` is left out when it would be empty.

The list mirrors the game's own `NMapScreen.RecalculateTravelability`:

1. Nothing visited this act: the start point only.
2. Standing on the boss, and the map has a second boss: the second boss only.
3. Standing on the second boss, or on the boss of a map with no second boss: empty. The act ends through the rewards screen.
4. Standing on the last grid row (R-1): the boss only.
5. Otherwise: `MapTravel.GetTravelablePointsFrom(run, here)`, with duplicates removed, sorted by row and then by column. Normally these are the current point's children. Under free travel (per the code comment: Winged Boots solo, and Flight) it is the whole next row.
6. Last visited coordinate not on the map: the start point (the game's own fallback).

So `n` counts from 0, left to right by column, across the next row.

**`ahead`**
- The six kinds are counted in this fixed order: monster, elite, unknown, shop, rest, treasure.
- For each kind it gives the minimum and maximum number of rooms of that kind on a route from that option to the end of the grid. The option itself counts, and routes follow child links only (never free travel).
- Min and max are taken for each kind separately, so the route with the fewest monsters and the route with the fewest elites can be different routes.
- Format: `"<kind> <min>-<max>"`, or `"<kind> <n>"` when min equals max. A kind whose max is 0 is left out. Parts are joined by `", "`.
- `boss` and `ancient` are never counted, so the boss option has no `ahead`.

**Every travel option is listed in `next`, even when its action is not offered.** Ids follow `n`, so the offered ids can skip a number (for example `map:0` and `map:2` offered, `map:1` blocked).

### Actions

**`map:<n>`**
- Label: `"Go to <Kind> at (col,row)"`, e.g. `"Go to Unknown (?) at (5,11)"` or `"Go to Boss at (3,16)"`.
- **When it is offered:**
  - only in an awaiting snapshot (no waiting reason);
  - only when the point's `NMapPoint` node is found (a point without a node is skipped silently);
  - only when the point is `Travelable` and enabled;
  - not for a row 0 point while the map-select tutorial is unseen;
  - not for a point off screen while a controller is in use.
- **What it does.** It force-clicks the map point. The game turns the click into a synced map vote (`VoteForMapCoordAction`).
- **Solo:** the one vote decides at once and the party travels.
- **While travelling:** the snapshot is busy (`screen: "map"`, `waiting: "resolving travel to Rest Site at (3,11)"`). That lasts until the party arrives (current coordinate equals the target, the room has changed, and the map is no longer travelling), or until 15 s pass.
- **What `act` returns.** `act` waits up to 20 s for the next settled decision. So its reply `{"done": "map:<n>", "next": ...}` normally carries the new room's first snapshot: combat, event, shop, rest, treasure, or a modal.

**Potion actions.** These are added after the map ids, and only in an awaiting snapshot. For each filled slot, in slot order:
- `potion:<slot>`, labelled `"Use <name>"`. Only for potions usable outside a fight: not Automatic or None usage, not CombatOnly, passing the potion's own check, and not aimed at enemies (AnyEnemy, AllEnemies and RandomEnemy are dropped). An AnyPlayer potion goes to you. There is never a `>e`/`>p` target on the map.
- `discard_potion:<slot>`, labelled `"Discard <name>"`.

A slot whose potion is queued is skipped entirely. There are no potion actions at all while the player is dead, while the player can't remove potions, or while another bot request is resolving.

### Refusals

The Core and bridge refusals are the common ones (see Refusals under The common envelope); the bridge's re-check says `'map:1' is no longer available on the map screen.`, with the fresh screen's name. A missing id returns only `{"error": "action_id is required"}`.

MapReader.Execute's own refusals run in the same main-thread call as the bridge's fresh capture, which applies the same gates, so most of them are defensive.

Potion ids:
- `'<id>' is not available now.`
- `The action queue is not ready.` (discard only)

Map ids:
- `Unknown map action '<id>'.`
- `No run in progress.`
- `The map is not open.`
- `Already travelling.`
- `Travel is not enabled on the map yet.`
- `A combat is in progress.`
- `Map drawing mode is on; the game ignores map point clicks.`
- `Already voted for (c,r); waiting for teammates.`
- `The current room isn't finished (<reason>).` The reason is one of: `a fight is on`, `rewards are still unclaimed`, `the event isn't finished`, `no rest site option was chosen`, `in the shop`, `in the treasure room`.
- `Could not read the map: <message>`
- `There is no map option <n>.`
- `No map point node at (c,r).`
- `The map point at (c,r) <why>.` The why is one of: `is not selectable right now`, `waits for the map tutorial popup`, `is off screen while a controller is in use (scroll the map to it)`.
- `map point (c,r) is gone.` / `map point (c,r) was already clicked; waiting for the game to react.`
- Co-op only, and reachable: `The game ignored the click on map point (c,r).`

**What the bot actually sees.** In practice it gets the Core texts. It gets the bridge's "no longer available" when it omits `version` and the state moved, and the co-op ignored-click text.

### Waiting and busy snapshots

**Busy** (`state: {}`, `actions: []`), from MapReader, checked in this order:
1. `map closing`
2. `run loading`
3. `travelling on the map`
4. `map opened during combat; close it to continue`
5. `map not readable yet`
6. `map not generated yet`

**Busy from before routing.** `resolving travel to <Kind> at (c,r)` shows while our travel request is pending, labelled with the last routed screen (`"map"`). The generic `travelling on the map`, `screen transition`, `loading` and `waiting on the network` can also come from here.

**Waiting with the full `state`** (`actions: []`, no potion actions either), checked in this order:
1. `waiting for teammates to vote on the map (you voted (c,r))`
2. `the map was opened by hand while <reason>; close the map so the room can be finished`, with the same reasons as above. This applies only when the room was not left through the bot's own proceed or Leave and something is already visited this act. The arena never needs it if the map only opens through proceed or Leave.
3. `map open but travel not enabled (viewing only, or still animating)`
4. `map drawing mode is on; the game ignores map point clicks until it is turned off`
5. `nowhere left to travel on this map; close the map to continue`, when `next` is empty.
6. `no map point is selectable yet`, or `no map point is selectable yet: (c,r) <why>` with the first blocked point.

### Example (solo, Act 1, standing on floor 11)

The coordinates and the potion text are illustrative. The `rows` and `ahead` strings are computed exactly as the reader does for this map. One unreachable branch, at column 0 of rows 11 to 12, is left out.

```json
{
  "version": 412,
  "screen": "map",
  "awaiting_decision": true,
  "state": {
    "run": {"act": 1, "floor": 11, "ascension": 0},
    "player": {
      "character": "The Ironclad",
      "hp": 52,
      "max_hp": 80,
      "gold": 187,
      "relics": ["Burning Blood", "Pomander", "Vajra"],
      "deck_size": 14,
      "deck": {"Bash+": 1, "Defend": 4, "Headbutt": 1, "Inflame": 1, "Pommel Strike": 1, "Shrug It Off": 1, "Strike": 4, "Twin Strike": 1}
    },
    "potions": [
      {"slot": 0, "name": "Fire Potion", "text": "Deal 20 damage to an enemy.", "target": "AnyEnemy", "usable": false},
      {"slot": 1, "name": "Blood Potion", "text": "Heal for 20% of your Max HP.", "target": "AnyPlayer", "usable": true},
      null
    ],
    "boss": "Ceremonial Beast",
    "at": "(4,10) monster",
    "next": [
      {"n": 0, "at": "(3,11)", "type": "rest", "ahead": "monster 0-2, elite 0-2, unknown 0-1, rest 2"},
      {"n": 1, "at": "(5,11)", "type": "unknown", "ahead": "monster 0-2, elite 0-1, unknown 1-2, shop 0-1, rest 1"}
    ],
    "map": {
      "key": "rows go upward by row number; each node is cCOL:TYPE^CHILD_COLS (children are in the next row); @ marks you; (quest) marks a quest target. next[].ahead: min-max count of each room type over the routes from that node to the boss, itself included.",
      "visited": "(3,0) ancient, (3,1) monster, (2,2) monster, (2,3) unknown, (1,4) shop, (2,5) monster, (2,6) elite, (3,7) unknown, (3,8) rest, (3,9) treasure, (4,10) monster",
      "rows": {
        "10": "@c4:monster^3,5",
        "11": "c3:rest^2,4 c5:unknown^4,6",
        "12": "c2:elite^1,3 c4:monster^3,5 c6:shop^5,6",
        "13": "c1:monster^2 c3:unknown^2,4 c5:elite^4,5 c6:monster^5",
        "14": "c2:elite^2 c4:monster^3,4 c5:unknown^4",
        "15": "c2:rest c3:rest c4:rest",
        "16": "c3:boss"
      }
    }
  },
  "actions": [
    {"id": "map:0", "label": "Go to Rest Site at (3,11)"},
    {"id": "map:1", "label": "Go to Unknown (?) at (5,11)"},
    {"id": "discard_potion:0", "label": "Discard Fire Potion"},
    {"id": "potion:1", "label": "Use Blood Potion"},
    {"id": "discard_potion:1", "label": "Discard Blood Potion"}
  ]
}
```

Other moments in the act, as fragments:

- **First map of Act 1 with Neow:**
  - `at` is `"(c,0) ancient"` and `visited` is the same single entry.
  - `next` is the start's children in row 1, all `monster`.
  - `rows` holds every point from row 0 to row 16.
  - There is no `ancient` key.
- **On the last grid row:**
  - `"at": "(3,15) rest"`
  - `"next": [{"n": 0, "at": "(3,16)", "type": "boss"}]`, with no `ahead`.
  - `"rows": {"15": "@c3:rest", "16": "c3:boss"}`
  - The action is `{"id": "map:0", "label": "Go to Boss at (3,16)"}`.
- **Busy:** `{"version": 57, "screen": "map", "awaiting_decision": false, "waiting": "map not generated yet", "state": {}, "actions": []}`.

### Co-op differences

- **`state.team`** lists each teammate's HP. Routes should suit the weakest deck.
- **A click is only our vote.** `map:<n>` casts our vote (`multiplayer` here means `run.Players.Count > 1`). The host moves the whole party once every player slot holds a vote. A lone player's vote decides at once.
- **The vote is checked.** After the click the reader checks that the screen's `PlayerVoteDictionary` holds our vote for that point. If not, it fails with `The game ignored the click on map point (c,r).`
- **After voting:**
  - The request resolves once the party arrives, or once the game looks idle after at least 0.5 s. The timeout is 15 s.
  - Then the snapshot is the map with `awaiting_decision: false`, `waiting: "waiting for teammates to vote on the map (you voted (c,r))"`, the full `state` and no actions, not even potion actions.
  - The bot cannot change its vote through the bridge.
  - The vote is forgotten when the room, the number of visited points or the map changes.
- **What `act` returns.** Because a teammate may take a while, `act` usually waits its full 20 s and returns that waiting snapshot with the warning `The game is still busy. Call wait_for_decision before acting again.`
- **Teammates' votes are not shown.** The party travels together, so there is a single `at`.
- **Free travel.** Per the code comment, Winged Boots free travel is solo only.

### Left out on purpose

- Visited points other than the current one in `rows` (they are in `visited`), and branches no longer reachable.
- What an unknown point will be, the Unknown room odds, and which encounter a monster or elite point holds.
- Boss details beyond the title. The bot reads the wiki for those.
- Teammates' votes.
- Quest details: there is only the `(quest)` marker.
- Node parents, screen positions, the map's column count, map drawings and the legend.
- Whether a `next` option is a free-travel move (Winged Boots). It can be inferred: an option that is not among the current node's `^` children is one.
- Relic text and card text. `get_deck` has them; the snapshot carries only deck name counts.
- `ancient` once the start point is visited. `second_boss` on maps with no second boss node. `boss` while standing on the boss node, and both boss keys while standing on the second boss node.
- Any action while waiting, potion actions included, even when `potions[].usable` is true.

### What an arena must match

- Key order and omissions exactly as above. `at` in `next` is a bare `"(c,r)"`, while `state.at` is `"(c,r) type"`.
- `rows` keys are strings in ascending order. Tokens are sorted by column, and child lists are sorted.
- `next` is sorted by row and then column, and `n` equals the index. Ids follow `n` even when an entry is not offered.
- `ahead` is computed per kind, separately, with the option itself included. Kinds with max 0 are left out, and boss and ancient are never counted.
- A visited unknown point stays `unknown`.
- Potion actions come after the map ids, in slot order, with each use before its discard.
- The map snapshot is the `next` of the proceed or Leave that opened it.
- In co-op, voting produces the waiting snapshot described above until every player has voted.

## Rewards and card reward

Read from `src/SpireMind/Game/Screens/RewardsReader.cs`, `CardRewardReader.cs` and `TreasureReader.cs`, plus the helpers they call: `Describe.cs`, `Screens/PotionActions.cs`, `Latches.cs` (also holds `Clicks`), `ActionTracker.cs`, `ScreenRouter.cs`, `GameBridge.cs`, and Core's `GameSnapshot.cs`, `BotTools.cs`, `DecisionWaiter.cs` and `SnapshotVersioner.cs`. Game v0.107.1. Key names in backticks are exact. Labels are exact too, with `<...>` standing for the part that varies.

### Pieces these screens share

- `state.run` is the shared `run` block. On `rewards` and `card_reward` it is followed by the shared `boss` and `second_boss` keys (see Shared blocks); `treasure` has neither. `state.player` is the shared `player` block (`Describe.PlayerRun(me, includeDeck: true)`). Its name-only `potions` stays only on `card_reward`; on `rewards` and `treasure` it is removed and the detailed `state.potions` (`PotionActions.Describe(ctx, false)`) takes its place.
- Card offers (`Describe.CardOffer`, used by `card_reward` `cards`, a card reward's `cards` and a `special_card` reward) are the shared card object with `rarity` (see Cards in lists).
- The out-of-combat potion actions follow the common potion rules: `rewards` and `treasure` only, never `card_reward`. An `AnyPlayer` potion targets yourself. If the screen refuses one, the text is `'<id>' is not available now.`
- Refusals reach the bot in the common order (Core, then the bridge, then the screen's own `Execute`). Step 2 has just confirmed the id is offered, so screen texts appear only in a race. Last comes the click-once guard (`Clicks.Once`, 2.5 s per node): `<what> is gone.` or `<what> was already clicked; waiting for the game to react.` Each action below gives its screen texts and its `<what>`, which starts lowercase. An id that simply isn't offered always gets `'<id>' is not a legal action now.`; the screen texts are what an arena should return when its own state rejects an action that it did offer.
- `act_sequence` finds later ids (those containing digits) by their exact label in the newest snapshot. So `["reward:0", "reward:1"]` takes both rewards even though claiming reward 0 renumbers the rest.

### Screen `rewards`

**When the bot sees it.** An `NRewardsScreen` is the top overlay and the map is closed:
- After every won fight (hallway, elite, boss): the room's terminal rewards screen. It stays on the overlay stack under the map until the room changes.
- Non-terminal rewards screens, pushed by an event, a treasure chest, a shop relic (Orrery, Cauldron), the Crystal Sphere, or a relic taken from another rewards screen (Calling Bell). Their proceed closes them, and the screen underneath comes back.
- A rewards screen under another overlay (a `card_reward`, a removal's `card_select`, a nested rewards screen) is not read. The top one is.
- From the strategy wiki, not the reader: in Act 1 a hallway gives 10 to 20 gold, a card reward and a potion roll. An elite gives 35 to 45 gold, a relic, a card reward and a potion roll with better odds. The boss gives 100 gold, a potion roll and a card reward of 3 Rares, and no relic (`strategy/wiki/run/act1.md`).

**state**, in this key order:

| key | type | present | meaning |
|---|---|---|---|
| `run` | object | always | The shared `run` block. |
| `boss` | string | when the title is non-empty, and not on the boss's own rewards | The act's boss encounter title, as on the map (see `boss` and `second_boss` under Shared blocks). |
| `second_boss` | string | only when the map has a second boss node and the title is non-empty, and not on the second boss's own rewards | The second boss encounter's title, as on the map. |
| `player` | object | always | The shared `player` block, without `potions`. |
| `potions` | array | always | Detailed potion slots (see Shared blocks). |
| `rewards` | array | always, possibly empty | One entry per reward button, in the screen's (tree) order. |
| `proceed_ends_act` | `true` | only when proceed casts the act-change vote | The terminal rewards screen in a boss room or victory room. The first of two bosses is the exception, because its rewards lead back to the map. |

Each `rewards[]` entry starts with `n` (int: its index, used in `reward:<n>`) and `kind`:

| `kind` | other keys | label of `reward:<n>` |
|---|---|---|
| `"gold"` | `amount` (int, gold) | `Take <amount> gold` |
| `"potion"` | `name`, `text`, `target`, as in `state.potions` (no `slot`, no `usable`). None of them when the reward has no potion model. | `Take potion <name>` (`Take potion potion` with no model) |
| `"relic"` | `name`, `text`. Neither when the reward has no relic model. | `Take relic <name>` (`Take relic relic` with no model) |
| `"card"` | `count` (int, cards in the offer; left out when 0). `cards` (card offers without `n`) appears only once the bot has opened this reward on this screen. | `Open card reward (<count> cards)`, or `Open card reward` with no count |
| `"special_card"` | The single card's offer keys (`name`, `cost`, `type`, `text`, `enchant`, `rarity`). If the card can't be read: `text`, the button's caption. | `Take card <name>` (fallbacks `Take <text>`, `Take the card`) |
| `"card_removal"` | none | `Remove a card` |
| anything else | `kind` is the reward's C# type name (`unknown` when the button has no reward). `text` is the button's caption, else the reward's `Description`, when not empty. | `Take <text>` (fallback `Take the <Type> reward`) |

A potion or relic reward whose model can't be read has only `n`, `kind` and the availability keys below. The reader then uses the placeholder name `potion` or `relic` in its label and its `<what>`.

Any entry can end with these two keys:
- `available: false`: the button is disabled, or a note applies. When the key is absent, the reward is available.
- `note` (string): always `"potion slots full: use or discard a potion to take it"`. It appears on a potion reward when no slot is free, and also after a claim of it bounced while no further slot has opened since.

`n` runs 0 to k-1 over the entries listed right now. A reward the bot clicked whose button is disabled (claim in flight) drops out of the list. A claimed reward's button disappears. Either way the entries after it move down by one, so always use ids from the newest snapshot.

**Actions**, in this order: rewards, then proceed, then potions.

`reward:<n>` claims `rewards[n]` by clicking its button. The game routes the claim through its own reward synchronizer.
- Offered when the button is enabled, has no note, and wasn't clicked in the last 2.5 s.
- What it does by kind:
  - gold, potion, relic and special card: the game grants the reward and removes the button.
  - `card`: opens the `card_reward` screen.
  - `card_removal`: opens a removal `card_select` screen.
- Skipping the card reward or cancelling the removal brings its button back, enabled. A card reward that comes back lists its `cards` from then on.
- A potion claim that bounces (slots full after all) brings its button back with the note.
- Refusals from the screen:
  - `There is no reward <n>.`
  - `<What> is not clickable right now.`
  - `<What> is not available right now.`
  - `Cannot take <what>: potion slots full: use or discard a potion to take it.`
  - `Potion slots are full; use or discard a potion first.`
  - the click guard, with `<what>`.
- `<what>` is one of `the <amount> gold reward`, `the <name> reward` (potion; `the potion reward` with no model), `the <name> relic` (`the relic relic` with no model), `the <name> card` (special card), `the card reward` (a card reward, or a special card whose card can't be read), `the card removal reward`, `the <Type> reward`. `<What>` is the same with a capital first letter.
- Afterwards the snapshot is busy with `resolving <what>` until the button goes (gold, potion, relic, special card) or another screen covers the rewards (card, removal). For a potion the wait also ends when the button disables and then re-enables (the claim bounced), or once 1.2 s have passed with the button still enabled and never disabled. Timeout 5 s.

`proceed` presses the screen's proceed button.
- Offered when the button is shown, enabled, and wasn't clicked in the last 2.5 s.
- Labels:
  - `Proceed` when the button doesn't read Skip.
  - `Skip (leave without the unclaimed rewards)` when it reads Skip.
  - With `proceed_ends_act`: `Go on to the next act` (solo) or `Vote to go on to the next act` (co-op). If the button reads Skip, ` (button reads Skip: unclaimed rewards are left behind)` is appended.
- On a terminal screen it opens the map. On a non-terminal screen it closes the screen, and the screen underneath comes back.
- With `proceed_ends_act` it casts the act-change vote. This happens once per act (latched in `Latches.ActVoteCastFor` before the click), and in a boss room also once per rewards screen.
- Refusals from the screen: `There is no proceed button.`, `The proceed button is not enabled yet.`, `Already voted to go on to the next act.`, `Proceed was already pressed on these boss rewards.`, and the click guard with `proceed button`.
- Afterwards the snapshot is busy with one of:
  - `resolving leaving the rewards`, until the screen is no longer on top, the map opens or a popup opens. Timeout 6 s.
  - `resolving the vote to go on to the next act`, until the act changes, the screen is covered or a popup opens. In co-op it also ends once the game looks idle after at least 0.5 s. Timeout 15 s.

`potion:<slot>`, `discard_potion:<slot>` are the out-of-combat potion actions described above.
- They are listed after proceed whenever any reward or proceed action is offered.
- When nothing else is offered, they are offered alone only if a potion reward has the slots-full note, so the bot can make room.

Other refusals from the screen: `Unknown rewards action '<id>'.`, `The rewards screen is gone.`, `No run in progress.`, `Another screen is on top of the rewards.`

**Waiting states.** The reader checks them in this order:

| `waiting` | state | when |
|---|---|---|
| `rewards closing` | `{}` | the screen node is gone |
| `run loading` | `{}` | no run or no local player yet |
| `another screen is over the rewards` | `{}` | this screen isn't the top overlay |
| `voted to go on to the next act; waiting for teammates` (co-op) / `starting the next act` (solo) | kept | the act vote for this act has been cast |
| `pressed proceed on the boss rewards; waiting for teammates` (co-op) / `leaving the boss rewards` (solo) | kept | proceed was pressed on this rewards screen in a boss room |
| `rewards appearing` | kept | under 0.5 s since the screen appeared, or a button is disabled within 3 s of the screen appearing or within 2 s of a claim |
| `rewards updating` | kept | an enabled button with no note was clicked in the last 2.5 s (it is back from a skipped card reward) |
| `no reward is clickable yet` | kept | rewards are listed but none can be offered, and there is no proceed |
| `waiting for the proceed button` | kept | no rewards and no proceed yet |
| `resolving <what>` | `{}` | from the bridge: the bot's last action is still landing |

**Example** (solo, after an elite on floor 7, holding a Fire Potion and a Blood Potion; illustrative card and relic text):

```json
{
  "version": 412,
  "screen": "rewards",
  "awaiting_decision": true,
  "state": {
    "run": {"act": 1, "floor": 7, "ascension": 0},
    "boss": "Ceremonial Beast",
    "player": {
      "character": "The Ironclad", "hp": 51, "max_hp": 80, "gold": 163,
      "relics": ["Burning Blood"],
      "deck_size": 12,
      "deck": {"Bash": 1, "Defend": 4, "Inflame": 1, "Strike": 5, "Twin Strike": 1}
    },
    "potions": [
      {"slot": 0, "name": "Fire Potion", "text": "Deal 20 damage to an enemy.", "target": "AnyEnemy", "usable": false},
      {"slot": 1, "name": "Blood Potion", "text": "Heal for 20% of your Max HP.", "target": "AnyPlayer", "usable": true},
      null
    ],
    "rewards": [
      {"n": 0, "kind": "gold", "amount": 41},
      {"n": 1, "kind": "potion", "name": "Block Potion", "text": "Gain 12 Block.", "target": "Self"},
      {"n": 2, "kind": "relic", "name": "Vajra", "text": "Start each combat with 1 Strength."},
      {"n": 3, "kind": "card", "count": 3}
    ]
  },
  "actions": [
    {"id": "reward:0", "label": "Take 41 gold"},
    {"id": "reward:1", "label": "Take potion Block Potion"},
    {"id": "reward:2", "label": "Take relic Vajra"},
    {"id": "reward:3", "label": "Open card reward (3 cards)"},
    {"id": "proceed", "label": "Skip (leave without the unclaimed rewards)"},
    {"id": "discard_potion:0", "label": "Discard Fire Potion"},
    {"id": "potion:1", "label": "Use Blood Potion"},
    {"id": "discard_potion:1", "label": "Discard Blood Potion"}
  ]
}
```

After `reward:0` the next snapshot lists the potion as `n: 0` (`reward:0`), the relic as 1 and the card reward as 2. With all three slots full, the potion entry would instead read `{"n": 1, "kind": "potion", "name": "Block Potion", "text": "Gain 12 Block.", "target": "Self", "available": false, "note": "potion slots full: use or discard a potion to take it"}`, and `reward:1` would not be offered. After the bot opens the card reward and skips it, the entry reads `{"n": 3, "kind": "card", "count": 3, "cards": [{"name": "Iron Wave", "cost": "1", "type": "Attack", "text": "Gain 5 Block. / Deal 5 damage.", "rarity": "Common"}, ...]}`.

**Co-op.**
- Each player reads and claims their own rewards screen. Nothing about teammates (their HP, their rewards, their choices) is in this snapshot.
- Only the act-ending proceed changes. Its label is `Vote to go on to the next act`. After it, the snapshot waits with `voted to go on to the next act; waiting for teammates` until every player has voted.
- A proceed on a boss room's rewards that doesn't end the act (the first of two bosses) is followed by `pressed proceed on the boss rewards; waiting for teammates` until the map opens.
- A proceed outside a boss room opens the map, and the map vote does the waiting.

**Left out on purpose.**
- A card reward's cards, until the bot has opened it on this screen, because a human sees only the button. `count` is there from the start.
- Rewards whose claim is in flight (clicked, now disabled). They leave the list until they bounce back or vanish.
- Deck card text. `deck` gives name counts only, and `get_deck` has the full text.
- A reward relic's or potion's rarity, and whether a reward potion could be used out of combat.
- Whether proceed reads Skip. This is not a field: only the proceed label says it.

### Screen `card_reward`

**When the bot sees it.** `NCardRewardSelectionScreen` is the top overlay, which follows `reward:<n>` on a `card` reward. Card rewards are personal, so co-op has no vote here.

**state**, in this key order:

| key | type | meaning |
|---|---|---|
| `run` | object | The shared `run` block. |
| `boss` | string | The act's boss encounter title, as on the map. Present when the title is non-empty, and not on the boss's own card reward (see `boss` and `second_boss` under Shared blocks). |
| `second_boss` | string | The second boss encounter's title. Present only when the map has a second boss node and the title is non-empty, and not on the second boss's own card reward. |
| `player` | object | The shared `player` block, including `potions` (titles or null, one per slot). |
| `cards` | array | The offered cards, in row order. Each is `{"n": int, ...card offer}`, and `n` is the index used in `card:<n>`. |

This screen has no `state.potions` and offers no potion actions.

**Actions**, in this order: one per card, then the alternative buttons in the game's order (Skip, Reroll, Sacrifice).

`card:<n>` takes `cards[n]` into the deck and closes the screen. The card reward's button then leaves the rewards screen.
- Label `Take <name>`.
- Offered for every listed card once the row has stayed the same for 350 ms.
- Refusals from the screen: `There is no card <n>.`, `Not ready: card reward closing.`, `Not ready: cards appearing.`, and the click guard with `card <name>`.
- Afterwards the snapshot is busy with `resolving taking <name>` until the screen is gone (6 s). The rewards screen follows.

`skip` presses the game's Skip button.
- Label `Skip this card reward`.
- Closes the screen without taking a card. The card reward's button comes back on the rewards screen, enabled, and now lists `cards`.

`reroll` swaps the offered cards in place (Driftwood gives this button, for example).
- Label `Reroll the offered cards`.
- The screen stays up with new `cards`, so `card:<n>` now means the new cards.
- A 350 ms `cards appearing` wait follows.

`sacrifice` (Pael's Wing gives this button, for example).
- Label `Sacrifice this card reward`.
- Ends the selection, as skip does.
- Offered at most once per screen.

Rules for all three alternatives:
- Each is offered only while its button is shown, enabled, and wasn't clicked in the last 2.5 s.
- The id is the button's `OptionId` in lowercase (the game's ids are `Skip`, `REROLL` and `SACRIFICE`). When the game's option list doesn't line up with the live buttons, the id is read from the button's name or caption instead.
- When the button's caption is non-empty and differs from its id (compared case-insensitively), the caption is appended to the label in parentheses, e.g. `Reroll the offered cards (<caption>)`. So a caption such as `Skip reward`, or any translated caption, is appended too: `Skip this card reward (Skip reward)`.
- Any other `OptionId` would show up as its lowercase id, labelled with the id itself.
- Refusals from the screen: `The <id> button is not available right now.`, `Already sacrificed on this card reward.`, `Not ready: <waiting>.`, `Unknown card reward action '<id>'.`, and the click guard with `<id> button`.
- Afterwards the snapshot is busy with `resolving skipping the card reward` or `resolving sacrificing the card reward` until the screen is gone (6 s), or with `resolving rerolling the cards` until the cards change or the screen is gone (6 s).

Other refusals from the screen: `The card reward screen is gone.`, `Another screen is on top of the card reward.`

**Waiting states:**

| `waiting` | state | when |
|---|---|---|
| `card reward closing` | `{}` | the screen node is gone |
| `run loading` | `{}` | no run or no local player yet |
| `another screen is over the card reward` | `{}` | this screen isn't the top overlay |
| `card reward closing` | kept | up to 8 s after a pick, skip or sacrifice while the screen is still up |
| `cards appearing` | kept | under 350 ms since the screen opened or the cards changed (a reroll), or nothing can be offered |
| `resolving <what>` | `{}` | from the bridge |

**Example** (solo, the card reward from the elite above):

```json
{
  "version": 417,
  "screen": "card_reward",
  "awaiting_decision": true,
  "state": {
    "run": {"act": 1, "floor": 7, "ascension": 0},
    "boss": "Ceremonial Beast",
    "player": {
      "character": "The Ironclad", "hp": 51, "max_hp": 80, "gold": 204,
      "relics": ["Burning Blood", "Vajra"],
      "potions": ["Fire Potion", "Blood Potion", "Block Potion"],
      "deck_size": 12,
      "deck": {"Bash": 1, "Defend": 4, "Inflame": 1, "Strike": 5, "Twin Strike": 1}
    },
    "cards": [
      {"n": 0, "name": "Tremble", "cost": "1", "type": "Skill", "text": "Apply 3 Vulnerable. / Exhaust.", "rarity": "Common"},
      {"n": 1, "name": "Shrug It Off", "cost": "1", "type": "Skill", "text": "Gain 8 Block. / Draw 1 card.", "rarity": "Common"},
      {"n": 2, "name": "Bludgeon", "cost": "3", "type": "Attack", "text": "Deal 32 damage.", "rarity": "Uncommon"}
    ]
  },
  "actions": [
    {"id": "card:0", "label": "Take Tremble"},
    {"id": "card:1", "label": "Take Shrug It Off"},
    {"id": "card:2", "label": "Take Bludgeon"},
    {"id": "skip", "label": "Skip this card reward"}
  ]
}
```

With Driftwood and Pael's Wing, `{"id": "reroll", "label": "Reroll the offered cards"}` and `{"id": "sacrifice", "label": "Sacrifice this card reward"}` would follow `skip`, each with its caption in parentheses whenever the caption is non-empty and differs from the id.

**Co-op.** Nothing changes, because the choice is personal.

**Left out on purpose.**
- Potion details and potion actions. They come back on the rewards screen.
- What reroll or sacrifice will give, beyond the button's caption.
- An `upgraded` flag (the name already carries the `+`).
- Deck card text (`get_deck` has it).

## Treasure (`treasure`)

**When the bot sees it.** The current room is a treasure room (`NTreasureRoom`, which is a room, not an overlay) and no overlay is up. The route is the screen context's `NTreasureRoom`, or `RoomType.Treasure` as a fallback.
- If opening the chest pushes a rewards screen (the code's "chest's extra rewards"), that screen is a `rewards` snapshot while it is up. The relics are read only after it closes.
- The strategy wiki says an Act 1 chest holds 42 to 52 gold as well as one relic, that floor 10 is always a treasure room, and that an Unknown room can roll one.

The usual flow: arrive, `open`, take the chest's rewards if it pushes a screen, wait for the relics to rise, `relic:<n>`, wait for the award, then `proceed` to the map.

**state**, in this key order:

| key | type | present | meaning |
|---|---|---|---|
| `run` | object | always | The shared `run` block. |
| `chest_open` | bool | always | True once relics are listed, or the game's relic picking has finished, or the bot's click went in and the chest is no longer clickable (or its node is gone). |
| `relics` | array | only while at least one relic holder is drawn | `{"n": int, "name": string, "text": string}`, plus `available: false` when that holder is disabled. A holder that can't be read gives `{"n": int, "name": "(unreadable relic)"}`. Its action, refusals, `picked` and waiting texts use a different name, `#<n> (unreadable)`: the label is `Take relic #<n> (unreadable)` (solo) or `Pick relic #<n> (unreadable) (co-op vote)`. |
| `picked` | string | after the bot has picked (voted) | The picked relic's name. |
| `relic_awarded` | bool | alongside `picked`, when the game's picking Task was found | True once `RelicPickingFinished()` has completed. |
| `co_op` | string | co-op, while relics are listed | Always `"each player picks one relic; teammates' picks aren't read by the bot"`. |
| `player` | object | always | The shared `player` block, without `potions`. |
| `potions` | array | always | Detailed potion slots (see Shared blocks). |

**Actions**, in this order: open, relics, proceed, potions.

`open` clicks the chest. The bot clicks it once per room, ever.
- Label `Open the chest`.
- Offered when the chest node is found, shown and enabled, the chest isn't open, the bot hasn't left, and the bot hasn't clicked it before.
- Refusals from the screen: `Can't find the chest.`, `The chest was already clicked once; waiting for it to open.`, `The chest can't be opened right now.`, and the click guard with `the chest`.
- Afterwards the snapshot is busy with `resolving opening the chest` until relics show, picking finishes, an overlay (the chest's rewards) or a popup opens, or the room goes. Timeout 6 s.

`relic:<n>` clicks `relics[n]`. In the game this calls `TreasureRoomRelicSynchronizer.PickRelicLocally`: solo it takes the relic, and in co-op it is a vote.
- Labels: `Take relic <name>` (solo), `Pick relic <name> (co-op vote)` (co-op).
- Offered for each enabled holder that wasn't clicked in the last 2.5 s, once all of these hold:
  - relics have been on show for 0.5 s;
  - the game is taking relic votes (`CurrentRelics` is set);
  - the bot hasn't picked yet and hasn't left;
  - no vote is in for this player yet (a human may have clicked for the bot's seat).
- Refusals from the screen:
  - `Already picked <name>.`
  - `The game isn't taking relic votes right now.`
  - `A relic vote is already in for this player.`
  - `The relics are still appearing.`
  - `There is no relic <n>.`
  - `<name> can't be picked right now.`
  - `The game refused the pick: <message>`
  - the click guard, with `relic <name>`.
- Afterwards, solo: busy with `resolving taking <name>` until the award lands (picking Task done, relic count up, the top overlay changes, a popup opens, or the room goes). Timeout 6 s. Co-op: busy for 0.5 s only, then the waiting states take over.
- Solo safety net: if 15 s pass after the pick with the picking Task not complete, no new relic, some holder still enabled, the game still taking votes and no vote of ours in, the pick is forgotten and the relics are offered again.

`proceed` leaves through the room's proceed button to the map.
- Labels: `Leave the treasure room (no relic on offer)` when nothing was picked and no relic is listed, otherwise `Leave the treasure room`.
- Offered only when the button is shown, enabled, doesn't read Skip, wasn't clicked in the last 2.5 s, and leaving is safe. Safe means:
  - Never while relics are listed and the bot hasn't picked one. The bot can't skip a chest relic.
  - Otherwise, once the game's picking Task has completed. This also covers an empty chest, about a second after it opens.
  - If the Task wasn't found: the relic count has risen since the pick. Or, solo only, 12 s have passed since the pick and no holder is enabled. Or no relic ever showed: 6 s after the bot's click (an empty chest), or after 10 s on screen with a chest the bot never clicked and can't click (looted before arrival, e.g. after a reload).
- Refusals from the screen:
  - `The relic hasn't been awarded yet; leaving now would lose it.`
  - `There is no proceed button.`
  - `The proceed button is not enabled yet.`
  - `The button still says Skip; pressing it would give up the relic.`
  - the click guard, with `the proceed button`.
- Afterwards the snapshot is busy with `resolving leaving the treasure room` until the room goes, the map opens, a popup opens, or the run's current room changes. Timeout 6 s.

`potion:<slot>`, `discard_potion:<slot>` are offered alongside any of the actions above, never alone.

Other refusals from the screen: `Unknown treasure room action '<id>'.`, `The treasure room is gone.`, `No run in progress.`

**Waiting states.** Each keeps the full `state` unless the table says `{}`:

| `waiting` | when |
|---|---|
| `treasure room closing` (`{}`) | no live treasure room node |
| `run loading` (`{}`) | no run or no local player yet |
| `entering the treasure room` | the first 0.4 s in the room |
| `left the treasure room; waiting for teammates` (co-op) / `leaving the treasure room` (solo) | after the bot's proceed |
| `can't find the chest in the treasure room` | there is no chest node |
| `the chest is not clickable yet` | closed, never clicked, and not clickable |
| `opening the chest` | clicked less than 8 s ago and not open yet, or open with no relic shown yet |
| `the chest still looks closed after the bot's click; a human has to open it` | still clickable 8 s after the bot's one click |
| `picked <name>; waiting for teammates to pick` (co-op) / `taking <name>` (solo) | picked, and the picking Task isn't complete |
| `waiting for the proceed button` | picked and done, but proceed can't be offered yet |
| `a relic vote the bot didn't make is in; waiting for teammates` (co-op) / `a relic pick or skip the bot didn't make is in` (solo) | a human voted, or skipped, for the bot's seat |
| `relics appearing` | relics are shown and some holder is enabled, but picking isn't open yet (the 0.5 s rise, or votes not open) |
| `relics not pickable yet` | relics are shown and no holder is enabled |
| `resolving <what>` (`{}`) | from the bridge |

**Example** (solo, floor 10, chest open, relic risen; illustrative relic text):

```json
{
  "version": 530,
  "screen": "treasure",
  "awaiting_decision": true,
  "state": {
    "run": {"act": 1, "floor": 10, "ascension": 0},
    "chest_open": true,
    "relics": [
      {"n": 0, "name": "Shuriken", "text": "Every time you play 3 Attacks in a single turn, gain 1 Strength."}
    ],
    "player": {
      "character": "The Ironclad", "hp": 62, "max_hp": 80, "gold": 151,
      "relics": ["Burning Blood", "Vajra"],
      "deck_size": 14,
      "deck": {"Bash": 1, "Defend": 4, "Inflame": 1, "Shrug It Off": 1, "Strike": 5, "Tremble": 1, "Twin Strike": 1}
    },
    "potions": [
      {"slot": 0, "name": "Fire Potion", "text": "Deal 20 damage to an enemy.", "target": "AnyEnemy", "usable": false},
      null,
      null
    ]
  },
  "actions": [
    {"id": "relic:0", "label": "Take relic Shuriken"},
    {"id": "discard_potion:0", "label": "Discard Fire Potion"}
  ]
}
```

There is no `proceed` here, because an unpicked relic is on offer. Before the chest opens, the state is `run`, `chest_open: false`, `player`, `potions`, and the actions are `{"id": "open", "label": "Open the chest"}` plus the potion actions. After `relic:0` the snapshot waits with `taking Shuriken`, carrying `"picked": "Shuriken", "relic_awarded": false`. Once the award lands it offers `{"id": "proceed", "label": "Leave the treasure room"}` with `"relic_awarded": true`.

**Co-op.**
- Each player has their own chest. Only the bot's click, or a human's on the bot's client, opens the bot's chest.
- `relic:<n>` is a vote, labelled `Pick relic <name> (co-op vote)`, and `state.co_op` appears while relics are listed.
- The bot never votes when a vote is already in for its seat, because a re-vote would overwrite a human's choice.
- After voting, the snapshot waits with `picked <name>; waiting for teammates to pick`, and `relic_awarded` stays false, until the game finishes picking: every vote is in and the awards have animated. The game settles clashing picks itself; the code doesn't model how.
- There is no 15 s re-offer and no 12 s fallback in co-op.
- After proceed, the snapshot waits with `left the treasure room; waiting for teammates`.

**Left out on purpose.**
- The relics, until the chest is open, so nothing set up inside a closed chest leaks.
- The game's Skip, which forfeits the relic. It is never offered: proceed is held back while the button reads Skip (solo, from 2.5 s after the chest opens until picking finishes) and while an unpicked relic is on offer.
- Teammates' votes, and which relic each player ended up with. Only `relic_awarded` and the bot's own `player.relics` afterwards show the outcome.

### What an arena has to match on rewards, card reward and treasure

- Labels exactly as above. `act_sequence` finds every id after the first by its label.
- `boss` (and `second_boss` when the map has a second boss node) right after `run` on `rewards` and `card_reward`, left out for a boss already beaten, as described under Shared blocks. `treasure` has neither.
- `reward:<n>` renumbers after each claim. Entries with `available: false` still take an index.
- A card reward shows `count` but no `cards` until the bot opens it. After a skip, the reward comes back enabled and lists `cards`.
- A potion reward with every slot full carries `available: false` plus the note and isn't offered. The potion actions are still offered, alone if nothing else is.
- After the act's boss, `proceed_ends_act: true` and the proceed label `Go on to the next act`.
- `card_reward` keeps `player.potions` as names, with no `state.potions` and no potion actions.
- Treasure: `open` first. Relics are listed only once the chest is open. `proceed` is never offered while an unpicked relic is listed. Once the bot picks, `picked` and `relic_awarded` appear.
- Settle windows (0.5 s rewards, 0.35 s card reward, 0.4 s plus 0.5 s treasure) only show as `waiting` snapshots, which `wait_for_decision` and `act` skip past. An arena can leave them out, but it must never offer an id the mod would hold back at that point.

## Shop (`"screen": "shop"`)

Read from `src/SpireMind/Game/Screens/ShopReader.cs` and, for the rest site in the next section, `RestSiteReader.cs` (v0.107.1), with the screens they open: the card removal, upgrade and Cook grids (`CardSelectReader.cs`), the potion slots and potion ids (`PotionActions.cs`), and the shared JSON pieces (`Describe.cs`). Example names, texts and prices are made up to look like an Act 1 Ironclad run. The keys, types, labels and rules are taken from the code.

Both the shop and the rest site use the common envelope and refusals, with two notes:

- A busy snapshot made by the bridge (an action still resolving, or the room node gone) has `"state": {}`. A wait made by the reader itself still carries the full `state`. The waits are listed under each screen.
- Potion ids (`potion:<slot>`, `discard_potion:<slot>`) are added only beside at least one real shop or rest action. They are never the whole list.

Reader refusals are listed per action. Core checks the id against a fresh snapshot, and then `GameBridge.ExecuteAsync` captures again on the main thread and checks that the id is still offered, in the same main-thread call that runs the reader. So a race (a teammate acting, an animation finishing, the 2.5 s click window) shows up as a bridge text: `'<id>' is no longer available on the <screen> screen.`, `Not your decision right now (<waiting>).` or `Still resolving <what>.` The reader refusals listed below are a backstop that almost never fires, as on events. The usual answer to an illegal shop or rest id is `'<id>' is not a legal action now.`

### When the bot sees it

In a merchant room: a Shop map node, or an Unknown room that rolled a shop. The router picks the shop reader when the game's current screen is the `NMerchantRoom`, or, failing that, when `RunState.CurrentRoom.RoomType` is `Shop`. That fallback is what happens while the stock panel is open, because the game then reports the panel as the current screen. Anything drawn over the room comes first and gets its own screen:

- the merchant's tutorial popup on a fresh profile: `modal`
- the map, if open: `map`
- the card removal grid: `card_select`
- the reward list that Orrery or Cauldron opens: `rewards`

The shop is one screen in two modes. With `inventory_open: false` the bot stands in the merchant room, which is how every visit starts. With `inventory_open: true` the stock panel is up. Buying is offered only while it is open, and leaving only while it is closed, as for a human. The stock is listed in both modes, so the bot can judge the shop and leave without opening it.

### State

Keys in this order:

| Key | Type | Meaning | Present |
| --- | --- | --- | --- |
| `run` | object | `act` (int, 1-based), `floor` (int, `RunState.TotalFloor`), `ascension` (int) | always |
| `boss` | string | the title of the act's boss encounter (see `boss` and `second_boss` under Shared blocks) | when the game names one |
| `second_boss` | string | the title of the act's second boss encounter | only when the map has a second boss node and the game names one (as on rest) |
| `gold` | int | the player's gold now | always |
| `inventory_open` | bool | the stock panel is open | always |
| `player` | object | see below | always |
| `potions` | array | one entry per potion slot (3 at the start of a run): `null` for an empty slot, else `{slot, name, text, target, usable}` | always |
| `stock` | object, or the string `"unknown until the inventory is opened"` | what is for sale, see below | always. The string appears only while the game has no stock model on the panel yet. In v0.107.1 the room attaches it on `_Ready`, so in practice it is always the object. |
| `potion_slots_full` | `true` | every potion slot is full and the stock holds at least one potion, so no potion buy is offered | only then |
| `last_purchase_failed` | string | the name of the item whose purchase failed, or `"a card removal"`. Failed means the game's purchase task returned false, threw, or was cancelled (closing the removal grid). | from the moment the failed purchase settles until the next shop action that succeeds (`open`, `close`, `proceed`, `dismiss` or a buy; potion actions don't clear it), or until a different merchant room |

`player` is the shared `player` block without `gold` (top level instead) and without `potions` (the detailed slots instead): `character`, `hp`, `max_hp`, `relics`, `deck_size`, `deck`.

`potions` are the detailed slots, as on every non-combat screen that offers potions. Outside combat `usable` is true only for potions the game allows any time, such as Blood Potion and Fruit Juice.

`stock` holds up to five keys, each left out when it would be empty:

| Key | Type | Entry keys, in order |
| --- | --- | --- |
| `cards` | array | character cards: `n`, `name`, `cost`, `type`, `text`, `enchant`, `rarity`, `price`, `on_sale`, `affordable` |
| `colorless` | array | colorless cards: same keys as `cards`, never `on_sale` |
| `relics` | array | `n`, `name`, `text`, `price`, `affordable` |
| `potions` | array | `n`, `name`, `text`, `target`, `price`, `affordable` |
| `removal` | object | `price`, `affordable` only. No `n` and no name. Left out once the removal is no longer stocked. |

Entry fields:

| Key | Type | Meaning | Present |
| --- | --- | --- | --- |
| `n` | int | the entry's index in the game's own list for that category, counting sold-out entries. A purchase leaves a gap (`0, 1, 3, 4`) instead of renumbering, so the ids of the rest stay put. | always (not on `removal`) |
| `name` | string | card title (ends in `+` when upgraded), relic or potion title | always (not on `removal`) |
| `cost` | string | energy cost: `"0"` and up, `"X"`, `"-"` for unplayable, `"?"` if unreadable; `"+2*"` or `"+X*"` appended for a star cost | cards |
| `type` | string | `Attack`, `Skill`, `Power`, ... | cards |
| `text` | string | card text as in the deck view (omitted when empty); relic or potion description (always present, may be `""`) | as stated |
| `enchant` | string | enchantment title | enchanted cards only |
| `rarity` | string | `Common`, `Uncommon`, `Rare`, ... | cards |
| `target` | string | the potion's `TargetType` | potions |
| `price` | int | gold. The entry's `Cost`, which already includes the game's price hook (discount relics), because the current room is the shop. | always |
| `on_sale` | `true` | the game marks this character card as on sale. The price already shows the discount. | only when on sale |
| `affordable` | bool | the game's `EnoughGold` for this entry | always |

Sold-out entries are left out of the lists. So is an entry whose read throws (logged once per room), so one odd entry can't hide the rest. With The Courier a bought slot restocks in place under the same `n`, so the same `buy:` id then names a new item; only the snapshot version tells the two apart.

### Actions

Order in `actions`: buys (cards, colorless, relics, potions, removal), then `close`; or `open`, then `proceed`; then the potion ids slot by slot (use before discard).

| Id | Label | Offered when | What it does |
| --- | --- | --- | --- |
| `open` | `Open the shop inventory to buy` (`... to see the stock` while `stock` is the string; `Open the shop inventory again (the last try did not open it)` after an open that did nothing) | panel closed, the player is alive, the room was not clicked in the last 2.5 s, and some stocked entry is affordable (or the stock is unknown). With nothing affordable, only `proceed` is offered. | `room.OpenInventory()`. Waits until the panel is open (up to 4 s). |
| `close` | `Close the shop inventory` | panel open and its back button shown, enabled and not just clicked | clicks the panel's back button. Waits until it is shut (up to 4 s). |
| `buy:card<n>` | `Buy <name> for <price> gold`, plus ` (on sale)` | panel open, the entry is stocked and `affordable` | the entry's own purchase (`OnTryPurchaseWrapper`), never awaited |
| `buy:colorless<n>` | `Buy colorless <name> for <price> gold` | same | same |
| `buy:relic<n>` | `Buy relic <name> for <price> gold` | same | same. Orrery and Cauldron then open a reward list over the shop. |
| `buy:potion<n>` | `Buy potion <name> for <price> gold` | same, and a potion slot is free | same |
| `buy:remove` | `Pay <price> gold to remove a card from your deck` | panel open, removal stocked and `affordable` | the removal entry's purchase with `cancelable: true`, which opens the removal grid (below) |
| `proceed` | `Leave the shop` | panel closed and the room's proceed button shown, enabled and not just clicked | clicks proceed. Waits for the map, a modal or a room change (up to 6 s). |
| `dismiss` | `Dismiss the tutorial tip` | a tutorial tip is drawn inside the room. It is then the only action, without potion ids. | clicks the tip's button (up to 3 s) |
| `potion:<slot>` | `Use <name>` | a potion the game allows outside combat | the game's manual use on the synced action queue |
| `discard_potion:<slot>` | `Discard <name>` | any held potion not already queued | the game's discard action |

`n` is the entry's `n` in `stock`. A sequence like `open`, buy, `close`, `proceed` takes several `act` calls, because no snapshot offers a buy and `proceed` together, and `act_sequence` checks every id against its first snapshot. Several buys from one open panel can go in one `act_sequence`. Each later buy is found again by its label, and the sequence stops if it is no longer offered.

Reader refusals (layer 3), checked in this order:

| When | Text |
| --- | --- |
| potion id not available | `'<id>' is not available now.` |
| room gone | `The shop is gone.` |
| no run | `No run in progress.` |
| a purchase still pending | `Still buying <name>.` (`Still buying a card removal.`) |
| Lord's Parasol still buying out | `Lord's Parasol is still buying out the shop.` |
| panel node missing | `The shop inventory is not ready.` |
| `open` | `The shop inventory is already open.` / `The merchant won't trade with a dead player.` / `the shop was already clicked; waiting for the game to react.` |
| `close` | `The shop inventory is not open.` / `There is no back button on the shop.` / `The shop's back button is not enabled yet.` / `the shop's back button was already clicked; waiting for the game to react.` |
| `proceed` | `Close the shop inventory first.` / `There is no proceed button.` / `The proceed button is not enabled yet.` / `the proceed button was already clicked; waiting for the game to react.` |
| any `buy:` | `Open the shop inventory first.` / `'<id>' is not in stock.` (also for a malformed `buy:` id) / `Not enough gold for <name> (<price>).` (`Not enough gold for a card removal (<price>).`) / `Potion slots are full; use or discard a potion first.` / `The game refused to sell <name>: <exception message>` |
| `dismiss` | `No tip is showing.` / `the tip's button was already clicked; waiting for the game to react.` |
| anything else | `Unknown shop action '<id>'.` |

A buy the game turns down after it started (the purchase task returns false) is not a refusal. `act` succeeds, and the next shop snapshot carries `last_purchase_failed`. For example, Sozu lets potion buys be offered, and then the game refuses them.

### Waits

With full `state` (`awaiting_decision: false`):

| `waiting` | When |
| --- | --- |
| `entering the shop` | the first 400 ms in a new merchant room |
| `buying <name>` / `buying a card removal` | a purchase task still pending while the shop itself is on screen. After 15 s of continuous shop-on-screen time the reader gives up on it and offers the shop again. A gap of more than 1.5 s between shop captures (time on the removal grid or a reward list, or nobody polling) restarts the count rather than pausing it. |
| `Lord's Parasol is buying out the shop` | the player holds Lord's Parasol and map travel is still off (it opens the panel itself, buys everything, forces a removal, then turns travel back on) |
| `dismissing the tutorial tip` | right after `dismiss` |
| `opening the shop`, `shop inventory loading`, `closing the shop` | within 2.5 s of the bot's own open or close click, while the panel catches up |
| `the merchant's tutorial took the proceed click` | the first `proceed` on a profile that has not seen the merchant tutorial (and hasn't opened the panel) shows the tutorial instead of leaving. `proceed` is offered again once the popup is dismissed. |
| `leaving the shop` / co-op: `left the shop; waiting for teammates` | after `proceed`, while the room is still on screen |
| `shop inventory opening` / `shop not ready` | nothing is clickable for any other reason (panel open or closed) |

With `"state": {}`: `shop closing` (room node gone), `run loading`, `shop loading` (no panel node yet), and from the bridge `resolving opening the shop`, `resolving closing the shop`, `resolving leaving the shop`, `resolving dismissing the tip`, `resolving buying <name>` (until the purchase task completes, the room goes, an overlay or modal opens, or 8 s pass), `resolving <potion name>`, `resolving discarding <potion name>`.

### Card removal

1. With the panel open, `buy:remove` starts the removal purchase. The game puts its deck grid over the shop while the purchase is still pending. The bridge reports `resolving buying a card removal` until the grid shows.
2. `card_select` with `state.kind: "remove"` (the screen's prompt key is the removal one, or failing that the room is a shop). The full state is described under Card selection: `kind`, `prompt`, `min`, `max`, `hint`, `cards` (identical unselected copies grouped with `copies`), `run`, and `player` with name-only `potions` and no `deck` when the grid holds the whole deck. Actions: `card:<i>` `Pick <name> to remove` for each listed entry, and `cancel` `Close without removing a card` (the shop opens the grid as cancelable).
3. `card:<i>` opens the preview (`waiting: "preview opening"` meanwhile). Then `state` adds `selected: [<name>]` and `preview: [<card>]` instead of `hint`, the picked entry is listed on its own with `selected: true`, and the actions are `confirm` `Confirm: remove <name>` and `cancel` `Back to the cards (pick a different one)`.
4. `confirm` closes the grid (`choice sent; waiting for the screen to close`, or in co-op `choice sent; waiting for the game and teammates`). The purchase settles, and the shop comes back with less gold, the card gone from `player.deck` and `stock.removal` gone.
5. `cancel` on the grid closes it without removing anything. The purchase ends unsuccessful, so the shop comes back with `last_purchase_failed: "a card removal"` and the removal still in `stock`.

The grid's refusals are listed under Card selection.

### Orrery and Cauldron

Buying either relic opens a non-terminal `rewards` screen over the shop (`reward:<n>`, `proceed`). Its `proceed` only closes that list and returns to the shop. The bridge does not count it as leaving the room. Back on the shop, `buying <name>` may show until the purchase task completes.

### Example: panel open, one card already bought

The bot bought `Shrug It Off` (card 2) for 48 gold, so card 2 is missing from `stock.cards`, and its count is in `player.deck`. The rare colorless card and two of the relics cost more than the 162 gold left, so they have no buy action.

```json
{
  "version": 412,
  "screen": "shop",
  "awaiting_decision": true,
  "state": {
    "run": { "act": 1, "floor": 5, "ascension": 0 },
    "boss": "Ceremonial Beast",
    "gold": 162,
    "inventory_open": true,
    "player": {
      "character": "The Ironclad",
      "hp": 58,
      "max_hp": 80,
      "relics": ["Burning Blood", "Golden Pearl"],
      "deck_size": 13,
      "deck": { "Bash": 1, "Defend": 4, "Pommel Strike": 1, "Shrug It Off": 1, "Strike": 5, "Thunderclap": 1 }
    },
    "potions": [
      { "slot": 0, "name": "Blood Potion", "text": "Heal for 20% of your Max HP.", "target": "AnyPlayer", "usable": true },
      null,
      null
    ],
    "stock": {
      "cards": [
        { "n": 0, "name": "Headbutt", "cost": "1", "type": "Attack", "text": "Deal 9 damage. Put a card from your Discard Pile on top of your Draw Pile.", "rarity": "Common", "price": 52, "affordable": true },
        { "n": 1, "name": "Twin Strike", "cost": "1", "type": "Attack", "text": "Deal 5 damage twice.", "rarity": "Common", "price": 24, "on_sale": true, "affordable": true },
        { "n": 3, "name": "Flame Barrier", "cost": "2", "type": "Skill", "text": "Gain 12 Block. Whenever you are attacked this turn, deal 4 damage back.", "rarity": "Uncommon", "price": 78, "affordable": true },
        { "n": 4, "name": "Inflame", "cost": "1", "type": "Power", "text": "Gain 2 Strength.", "rarity": "Uncommon", "price": 73, "affordable": true }
      ],
      "colorless": [
        { "n": 0, "name": "Finesse", "cost": "0", "type": "Skill", "text": "Gain 4 Block. Draw 1 card.", "rarity": "Uncommon", "price": 85, "affordable": true },
        { "n": 1, "name": "Apotheosis", "cost": "2", "type": "Skill", "text": "Upgrade ALL your cards for the rest of combat. Exhaust.", "rarity": "Rare", "price": 168, "affordable": false }
      ],
      "relics": [
        { "n": 0, "name": "Anchor", "text": "Start each combat with 10 Block.", "price": 147, "affordable": true },
        { "n": 1, "name": "Vajra", "text": "At the start of each combat, gain 1 Strength.", "price": 236, "affordable": false },
        { "n": 2, "name": "Horn Cleat", "text": "At the start of your 2nd turn, gain 14 Block.", "price": 218, "affordable": false }
      ],
      "potions": [
        { "n": 0, "name": "Fire Potion", "text": "Deal 20 damage to an enemy.", "target": "AnyEnemy", "price": 52, "affordable": true },
        { "n": 1, "name": "Swift Potion", "text": "Draw 3 cards.", "target": "AnyPlayer", "price": 49, "affordable": true },
        { "n": 2, "name": "Fruit Juice", "text": "Gain 5 Max HP.", "target": "AnyPlayer", "price": 104, "affordable": true }
      ],
      "removal": { "price": 75, "affordable": true }
    }
  },
  "actions": [
    { "id": "buy:card0", "label": "Buy Headbutt for 52 gold" },
    { "id": "buy:card1", "label": "Buy Twin Strike for 24 gold (on sale)" },
    { "id": "buy:card3", "label": "Buy Flame Barrier for 78 gold" },
    { "id": "buy:card4", "label": "Buy Inflame for 73 gold" },
    { "id": "buy:colorless0", "label": "Buy colorless Finesse for 85 gold" },
    { "id": "buy:relic0", "label": "Buy relic Anchor for 147 gold" },
    { "id": "buy:potion0", "label": "Buy potion Fire Potion for 52 gold" },
    { "id": "buy:potion1", "label": "Buy potion Swift Potion for 49 gold" },
    { "id": "buy:potion2", "label": "Buy potion Fruit Juice for 104 gold" },
    { "id": "buy:remove", "label": "Pay 75 gold to remove a card from your deck" },
    { "id": "close", "label": "Close the shop inventory" },
    { "id": "potion:0", "label": "Use Blood Potion" },
    { "id": "discard_potion:0", "label": "Discard Blood Potion" }
  ]
}
```

The same shop with the panel closed has `"inventory_open": false`, the same `stock`, and the actions `open` (`Open the shop inventory to buy`), `proceed` (`Leave the shop`), `potion:0` and `discard_potion:0`.

### Co-op

- Each player has their own stock, prices and gold, and shops alone. Nothing is voted on in the shop. The snapshot has no `team` key.
- After `proceed` the map's vote is where teammates wait. If the room is still on screen meanwhile, the wait reads `left the shop; waiting for teammates`.
- A teammate's Lord's Parasol buys only from that teammate's stock. Only the player's own parasol makes the bot wait.
- The removal grid's final wait reads `choice sent; waiting for the game and teammates`.

### Left out on purpose

- Sold-out entries. The gaps in `n` are the only trace.
- Card text for the player's own deck (`deck` is name counts) and the text of owned relics (names only). `get_deck` has both.
- No buy ids while the panel is closed and no `proceed` while it is open, as for a human. The stock is still listed while closed.
- No buy id for an unaffordable entry, or for a potion when the slots are full. They stay listed with `affordable: false`, or with `potion_slots_full`.
- Prices before discounts, the size of a sale discount, what the next removal will cost, and the merchant's lines.
- The removal entry's index and name. It is always `buy:remove`.

### For the arena

- Keep `n` as the game's list index, with sold-out gaps.
- Stock and prices are per room and per player. `price` already includes discounts. `affordable` is the game's `EnoughGold`, which in a plain run should be `gold >= price`.
- Arrival is closed. `open`, buy, `close`, `proceed` are separate decisions.
- The arena can skip the merchant tutorial, the in-room tip, the settle and transition waits and Lord's Parasol, as a profile that has seen the tutorials would.
- Per `strategy/wiki/run/act1.md` (game data, not this code), an Act 1 shop stocks 5 character cards (2 Attacks, 2 Skills, 1 Power, which is never Common), one of them half price; 2 colorless cards (1 Uncommon, 1 Rare); 3 relics; 3 different potions; and 1 card removal at 75 gold, plus 25 for each removal already bought this run. Prices vary by up to 5% around Common 50, Uncommon 75 and Rare 150 for cards, colorless 15% more, and potions 50, 75 and 100 by rarity.

## Rest site (`"screen": "rest"`)

### When the bot sees it

In a rest site room. (Which floors can hold one is map-generation data, not reader logic: `strategy/wiki/run/act1.md` gives Act 1 floors 7 to 9, 11 to 13 and 16. See open question 5.) The router picks the rest reader when the game's current screen is the `NRestSiteRoom`, or, failing that, when `RunState.CurrentRoom.RoomType` is `RestSite`. Anything drawn over it comes first: the rest site tutorial popup on the first visit ever (`modal`), the map, and the deck grids that Smith and Cook open (`card_select`).

### State

Keys in this order:

| Key | Type | Meaning | Present |
| --- | --- | --- | --- |
| `run` | object | the shared `run` block: `act`, `floor`, `ascension` | always |
| `hp`, `max_hp` | int | the player's current and maximum HP | always |
| `boss` | string | the title of the act's boss encounter (see `boss` and `second_boss` under Shared blocks) | when the game names one |
| `second_boss` | string | the title of the act's second boss encounter | only when the map has a second boss node and the game names one |
| `player` | object | `character`, `gold`, `relics`, `deck_size`, `deck`: the shop's `player` with `gold` instead of `hp` and `max_hp` (those are top level here) | always |
| `potions` | array | detailed potion slots, as on the shop | always |
| `team` | string array | each teammate as `"<character> <hp>/<max_hp> hp"`, for example `"The Silent 41/70 hp"` (the shared `team` lines) | co-op only, when there is a teammate |
| `options` | array | the campfire options, see below | always (may be `[]` while they appear) |
| `chosen` | string array | titles of the options the bot chose at this rest site, in order (Miniature Tent allows several) | after the bot's first choice here |

`options` entries, in the game's order:

| Key | Type | Meaning | Present |
| --- | --- | --- | --- |
| `n` | int | the option's index in the game's list right now. It is what `option:<n>` names. An option whose read throws is left out, and the others keep their index. | always |
| `id` | string | the game's option id: `HEAL`, `SMITH`, `MEND` (co-op only), or a relic's `COOK`, `DIG`, `LIFT`, `KINDLE`, `CLONE`, `HATCH` | always |
| `name` | string | the option's title, or the id capitalized (`Heal`) if the title is empty | always |
| `text` | string | the option's description | when not empty |
| `available` | `false` | the option can't be chosen now: the game disabled it (no card to upgrade, too few cards to cook), its button is missing or hidden (the room rebuilds its buttons after a choice), or another option is playing out (every button is off then) | only when false |
| `supported` | `false` | the bot can't do this option | `MEND` only |
| `note` | string | `"heals a teammate chosen with the targeting arrow, which the bot can't do yet"` | `MEND` only |

An entry of `chosen` is dropped again when that option becomes clickable once more, which is what backing out of the Smith or Cook grid does.

### Actions

| Id | Label | Offered when | What it does |
| --- | --- | --- | --- |
| `option:<n>` | `HEAL`: `<name> (heal <amount>)`, the amount being the game's own heal for this player, 30% of max HP after relics, rounded down (just `<name> (heal)` if the game can't say). `SMITH`: `<name> (upgrade a card)`. Others: `<name> (<text up to its first ". ", trailing period dropped, cut to 67 characters plus "..." when longer than 70>)`, or `<name>` when the option has no text. | the option is `available`, supported (not `MEND`), and its button was not clicked in the last 2.5 s | clicks the option's button. The game sends the choice through its rest site synchronizer, so co-op sees it. Rest heals with a short animation. Smith and Cook open a deck grid. |
| `proceed` | `Proceed`, or `Proceed (leave the remaining options unused)` while an option is still on offer beside it | the game enabled its proceed button (after an option succeeded, or when none is left), no option is playing out (no option button disabled), and proceed was not clicked in the last 2.5 s | clicks proceed. Waits for the map, a modal or a room change (up to 6 s). |
| `potion:<slot>`, `discard_potion:<slot>` | as on the shop | beside at least one of the above | as on the shop |

So a normal visit is one option and then `proceed`. No `proceed` is offered before an option has succeeded, unless the game has no option left to offer. Only with Miniature Tent can options and `proceed` be on offer together.

Reader refusals (layer 3):

| When | Text |
| --- | --- |
| potion id not available | `'<id>' is not available now.` |
| room gone | `The rest site is gone.` |
| no run | `No run in progress.` |
| `option:<n>` with no such option | `There is no rest site option <n>.` |
| `option:<n>` on Mend | `<name> heals a teammate chosen with the targeting arrow, which the bot can't do yet.` |
| `option:<n>` not available | `<name> is not available right now.` |
| `option:<n>` clicked twice | `the <name> option was already clicked; waiting for the game to react.` (`the <name> option is gone.` if its button was freed) |
| `proceed` | `There is no proceed button.` / `The proceed button is not enabled yet.` / `A rest site option is still playing out.` / `the proceed button was already clicked; waiting for the game to react.` |
| anything else, including `option:` without a number | `Unknown rest site action '<id>'.` |

### Waits

With full `state`: `arriving at the rest site` (first 400 ms), then, whenever nothing is clickable, the first that applies of: `leaving the rest site` (after `proceed`), `only Mend is available, which the bot can't target; a human has to choose it`, `<last chosen name> in progress` (Rest's heal playing out, for example), `rest site options appearing` (no options yet), and `no rest site option is clickable yet`.

With `"state": {}`: `rest site closing` (room node gone), `run loading`, and from the bridge `resolving choosing <name>` (until the button goes off, an overlay or modal opens, the room changes or proceed turns on, up to 8 s) and `resolving leaving the rest site`.

### Smith: the upgrade grid

`option:<n>` on `SMITH` opens the game's upgrade grid: `card_select` with `state.kind: "upgrade"`.

- State and card entries are those of any deck grid (see Card selection): `kind`, `prompt`, `min`, `max`, `hint`, `cards` (`i`, `name`, `cost`, `type`, `text`, `enchant`, `rarity`, `copies`), `run`, `player` with name-only `potions`. When every card in the deck can be upgraded (usual early in a run), the grid is the whole deck: `deck` is then left out and `deck_size` comes last.
- Actions: `card:<i>` `Pick <name> to upgrade` (only the first copy of identical cards has one), and `cancel` `Close without choosing` when the grid's close button is usable.
- After `card:<i>` the preview opens (`preview opening`). `state` adds `selected: [<name>]` and `preview` (the cards the preview shows, which is where the upgraded text appears; the string `"open"` if none can be read) instead of `hint`, and the actions are `confirm` `Confirm: upgrade <name>` and `cancel` `Back to the cards (pick a different one)`.
- `confirm` upgrades the card and closes the grid. Back on the rest site, Smith is used up, `chosen` holds its name, and `proceed` comes up.
- `cancel` from the grid closes it. Back on the rest site, the options are clickable again, Smith's name leaves `chosen`, and there is still no `proceed`: backing out doesn't use the visit.

### Cook: the card grid

`option:<n>` on `COOK` (a relic's option) opens the game's generic deck grid: `card_select` with `state.kind: "remove"` when the screen's prompt is the game's removal prompt (`TO_REMOVE`), otherwise `"select"`. `min` and `max` come from the screen.

- Picking cards: `card:<i>` (`Pick <name> to remove` or `Select <name>`). Below the maximum, a picked card is listed on its own with `selected: true` and its action becomes `Deselect <name>`.
- At the maximum the preview opens, with `confirm` (`Confirm: remove <names>` or `Confirm selection: <names>`) and `cancel` back to the cards. When `min` and `max` differ, a screen-level `confirm` can open the preview early.
- `cancel` on the grid (`Close without removing a card` or `Close without choosing`) returns to the rest site with Cook clickable again.

Other relic options (`DIG`, `LIFT`, `KINDLE`, `CLONE`, `HATCH`) go through the same click. Whatever screen the game shows next (a card grid, a reward list) is read by that screen's own reader.

### Example: arriving with 41 of 80 HP

```json
{
  "version": 588,
  "screen": "rest",
  "awaiting_decision": true,
  "state": {
    "run": { "act": 1, "floor": 8, "ascension": 0 },
    "hp": 41,
    "max_hp": 80,
    "boss": "Ceremonial Beast",
    "player": {
      "character": "The Ironclad",
      "gold": 131,
      "relics": ["Burning Blood", "Golden Pearl"],
      "deck_size": 13,
      "deck": { "Bash": 1, "Defend": 4, "Pommel Strike": 1, "Shrug It Off": 1, "Strike": 5, "Thunderclap": 1 }
    },
    "potions": [
      { "slot": 0, "name": "Blood Potion", "text": "Heal for 20% of your Max HP.", "target": "AnyPlayer", "usable": true },
      null,
      null
    ],
    "options": [
      { "n": 0, "id": "HEAL", "name": "Rest", "text": "Heal for 30% of your Max HP." },
      { "n": 1, "id": "SMITH", "name": "Smith", "text": "Upgrade a card in your deck." }
    ]
  },
  "actions": [
    { "id": "option:0", "label": "Rest (heal 24)" },
    { "id": "option:1", "label": "Smith (upgrade a card)" },
    { "id": "potion:0", "label": "Use Blood Potion" },
    { "id": "discard_potion:0", "label": "Discard Blood Potion" }
  ]
}
```

After `option:1`, the upgrade grid (no card upgraded yet, so it is the whole deck and `player.deck` is left out):

```json
{
  "version": 590,
  "screen": "card_select",
  "awaiting_decision": true,
  "state": {
    "kind": "upgrade",
    "prompt": "Choose a card to Upgrade.",
    "min": 1,
    "max": 1,
    "hint": "pick a card, then confirm it on the preview",
    "cards": [
      { "i": 0, "name": "Strike", "cost": "1", "type": "Attack", "text": "Deal 6 damage.", "rarity": "Basic", "copies": 5 },
      { "i": 5, "name": "Defend", "cost": "1", "type": "Skill", "text": "Gain 5 Block.", "rarity": "Basic", "copies": 4 },
      { "i": 9, "name": "Bash", "cost": "2", "type": "Attack", "text": "Deal 8 damage. Apply 2 Vulnerable.", "rarity": "Basic" },
      { "i": 10, "name": "Pommel Strike", "cost": "1", "type": "Attack", "text": "Deal 9 damage. Draw 1 card.", "rarity": "Common" },
      { "i": 11, "name": "Thunderclap", "cost": "1", "type": "Attack", "text": "Deal 4 damage and apply 1 Vulnerable to ALL enemies.", "rarity": "Common" },
      { "i": 12, "name": "Shrug It Off", "cost": "1", "type": "Skill", "text": "Gain 8 Block. Draw 1 card.", "rarity": "Common" }
    ],
    "run": { "act": 1, "floor": 8, "ascension": 0 },
    "player": {
      "character": "The Ironclad",
      "hp": 41,
      "max_hp": 80,
      "gold": 131,
      "relics": ["Burning Blood", "Golden Pearl"],
      "potions": ["Blood Potion", null, null],
      "deck_size": 13
    }
  },
  "actions": [
    { "id": "card:0", "label": "Pick Strike to upgrade" },
    { "id": "card:5", "label": "Pick Defend to upgrade" },
    { "id": "card:9", "label": "Pick Bash to upgrade" },
    { "id": "card:10", "label": "Pick Pommel Strike to upgrade" },
    { "id": "card:11", "label": "Pick Thunderclap to upgrade" },
    { "id": "card:12", "label": "Pick Shrug It Off to upgrade" },
    { "id": "cancel", "label": "Close without choosing" }
  ]
}
```

After `card:9`, `state` holds `"selected": ["Bash"]`, a `preview` array instead of `hint`, and the Bash entry as `{ "i": 9, ..., "selected": true }`. The actions are `confirm` (`Confirm: upgrade Bash`) and `cancel` (`Back to the cards (pick a different one)`). After `confirm` the rest snapshot has `"chosen": ["Smith"]`, no Smith option on offer, and `proceed` (`Proceed`) with the potion ids.

### Co-op

- `MEND` is in `options` with `supported: false` and the `note`, and is never offered. If it is the only clickable option, the bot waits (`only Mend is available, ...`) for a human.
- `team` lists the teammates' HP.
- Each choice goes through the game's rest site synchronizer. v0.107.1 syncs nothing else here: no waiting for teammates before `proceed` or after it. Only the map vote waits for everyone.
- The Smith and Cook grids are the chooser's own, with no vote. The final wait reads `choice sent; waiting for the game and teammates`.

### Left out on purpose

- Which cards Smith could upgrade. The upgrade grid lists them, with the upgraded text in the preview.
- The heal amount is only in Rest's label (and in the option text if the game puts it there). It is not a field.
- Option buttons, the fire and animations. Whether a later fight is the boss is left for the bot to work out from `run.floor` and `boss`.
- Mend's targeting.
- The rest site tutorial, which is a `modal` with `dismiss`.

### For the arena

- Arrival offers the options and no `proceed`. A succeeded option turns `proceed` on. Backing out of the grid gives the options back and does not use the visit.
- Rest heals what the game's `HealRestSiteOption.GetHealAmount` says: 30% of max HP after relics that change it, rounded down (24 at 80 max HP).
- The upgrade grid is `card_select` kind `upgrade` with a preview step, so `card:<i>` then `confirm` (two decisions).
- The tutorial popup and the waits can be skipped.

## Card selection (`card_select`)

Source: `src/SpireMind/Game/Screens/CardSelectReader.cs`. Routing is in `src/SpireMind/Game/ScreenRouter.cs`. Prompt, min and max capture is in `src/SpireMind/Patches/ChoicePromptPatch.cs`. Card, creature and player JSON comes from `src/SpireMind/Game/Describe.cs`. Envelope, refusals and tools are in `src/SpireMind.Core/GameSnapshot.cs`, `src/SpireMind/Game/GameBridge.cs` and `src/SpireMind.Core/BotTools.cs`.

One reader covers every overlay that asks the local player to pick cards outside the hand. All of them use `"screen": "card_select"`, and `state.kind` says which one is up. The router hands an overlay to this reader when it is the top of `NOverlayStack` and is one of the eight types below, and only when the map is not open (the map wins).

These are not on this screen:
- Picks from the hand (Armaments, Burning Pact, True Grit+, `FromHand*` factories) are `hand_select`, read by `CombatReader`.
- A fight's card reward (`NCardRewardSelectionScreen`) is `card_reward`.
- The card selection overlay has no potion actions. CLAUDE.md's "potion ids ride along" list does not include `card_select`, and this reader never adds them.

### Which screens, and when the bot sees them

| `state.kind` | Game overlay | Opened by (`CardSelectCmd` factory) | Typical Act 1 trigger | Steps to finish |
|---|---|---|---|---|
| `upgrade` | `NDeckUpgradeSelectScreen` | `FromDeckForUpgrade` | Rest site Smith (`option:<n>` on `rest`), Neow's Pomander, an event that upgrades a chosen card (Sapphire Seed) | `card:<n>` up to max, then `confirm` on the preview |
| `transform` | `NDeckTransformSelectScreen` | `FromDeckForTransformation` | Neow's New Leaf, events | `card:<n>`, then `confirm` on the preview |
| `remove` | `NDeckCardSelectScreen` with a removal prompt | `FromDeckForRemoval` (which runs through `FromDeckGeneric`) | Shop `buy:remove`, a rewards screen's `card_removal` reward, Neow's Precise Scissors (1 card) and Precarious Shears (2 cards), possibly the rest site's relic option Cook | `card:<n>`, then `confirm` on the preview |
| `enchant` | `NDeckEnchantSelectScreen` | `FromDeckForEnchantment` | An event or relic that enchants a chosen card (rare in Act 1) | `card:<n>`, then `confirm` on the preview |
| `select` | `NDeckCardSelectScreen` with any other prompt, or `NSimpleCardSelectScreen` | `FromDeckGeneric`, `FromSimpleGrid`, `FromSimpleGridForRewards` | A deck pick that is not a removal (Cook if its prompt is not a removal prompt), a plain grid of cards in or out of a fight | Deck screen: as `remove`. Simple grid: as `combat_pile_select` |
| `combat_pile_select` | `NCombatPileCardSelectScreen` | `FromCombatPile` | Mid-fight: Headbutt (discard to top of draw), Liquid Memories (discard to hand), Droplet of Precognition (draw to hand), Neow's Fury (up to 2 from discard to hand) | `card:<n>` until max (the screen closes by itself), or `confirm` when the game shows one |
| `choose` | `NChooseACardSelectionScreen` | `FromChooseACardScreen` | Mid-fight Discovery-like picks: Attack Potion, Skill Potion, Power Potion (1 of 3 generated cards, skippable) | One `card:<n>` or `skip` |
| `bundle` | `NChooseABundleSelectionScreen` | `FromChooseABundleScreen` | Neow's Scroll Boxes (1 of 2 bundles of 3 cards) | `bundle:<n>`, then `confirm` |

How `remove` is told from `select` on `NDeckCardSelectScreen`: the screen's own prompt loc key, read from its private `_prefs`, decides. `TO_REMOVE` and `COMBAT_REWARD_CARD_REMOVAL.selectionScreenPrompt` mean `remove`, and any other key means `select`. When the key cannot be read, the screen counts as a removal if the factory record says `FromDeckForRemoval`, if the current room is a shop, or if the prompt text contains "remov" (case-insensitive).

After the final action the overlay closes, and the bot is back on whatever opened it: the rest site, the shop, the rewards screen, the Neow/event page, or the fight.

### The envelope

The common envelope applies. When `awaiting_decision` is false, `actions` is `[]`, `waiting` says why (see "Waiting snapshots"), and `state` is still filled, except for the three cases listed there with an empty `state`.

### `state` for the grid kinds (`upgrade`, `transform`, `remove`, `enchant`, `select`, `combat_pile_select`)

Keys appear in this order.

| Key | Type | Present when | Meaning |
|---|---|---|---|
| `kind` | string | always | One of the six grid kinds above. |
| `prompt` | string | the game supplied prompt text | The game's own prompt (`CardSelectorPrefs.Prompt`), BBCode stripped by `TextCleaner` (newlines become `" / "`, the energy icon becomes `(E)`). It comes from the factory record when one matched, else from the screen's `_prefs`. The mod never writes it. |
| `min` | int | known (normally always on grids) | Fewest cards the game accepts. The screen's `_prefs.MinSelect` wins over the factory record. |
| `max` | int | known (normally always on grids) | Most cards the game accepts (`MaxSelect`). If it is 0 or less, the reader never treats the selection as full. |
| `pile` | string | in combat, and every listed card is in the same non-null pile | `PileType` name of that pile: `"Draw"`, `"Discard"`, `"Exhaust"`, `"Hand"`, `"Play"`, `"Deck"`. |
| `enchantment` | object | `enchant` only, when readable | `{"name": <string>, "text": <string, omitted when empty>}`: the enchantment with its amount applied, as the screen's panel shows it. |
| `selected` | array of string | one or more cards are selected | Names of the selected cards (`CardModel.Title`, so upgraded ones end in `+`), read from the screen's own `_selectedCards`. A human's mouse picks on the bot's window show up here too. Order is the game's HashSet order. |
| `preview` | array of card objects, or the string `"open"` | the deck screen's preview is opening or ready | The cards drawn in the preview container, as card objects without `i`. On `upgrade` this is where the upgraded text appears. A random transform's cycling result is left out. The value is `"open"` when the preview shows no card (a "choose none" preview, reached with min 0 and nothing picked). |
| `hint` | string | deck screens (`upgrade`, `transform`, `enchant`, and `NDeckCardSelectScreen`) whenever no preview is open | Always `"pick a card, then confirm it on the preview"`. The simple and combat-pile grids never carry it. |
| `cards` | array of card entries | always (may be empty) | What can be picked. See "Card entries". |
| `partial` | string | the grid's full list could not be read | `"only the cards scrolled into view are listed"`. The list then holds only the holders the virtualized grid has allocated. |
| `run` | object | a run exists | `{"act": <1-based int>, "floor": <RunState.TotalFloor>, "ascension": <int>}`. |
| `combat` | object | in combat | See "Context". Replaces `player`. |
| `player` | object | out of combat | See "Context". |

`preview` and `hint` never appear together.

### Card entries (grids and `choose`)

Each entry is `Describe.Lead("i", n, card)`, so `i` comes first:

- Out of combat (`Describe.CardOffer`): `i`, `name`, `cost`, `type`, `text` (omitted if empty), `enchant` (only if the card is enchanted), `rarity`.
- In combat (`Describe.Card` with the card's current pile, `Deck` if it has none): `i`, `name`, `cost`, `type`, `text`, `enchant`, then `pile` when the listed cards come from more than one pile. There is no `rarity`. The text is the live variant for that pile.
- Grids only: then `selected: true` on a selected card, or `copies: <int>` on a group of identical unselected cards.

| Field | Type | Meaning |
|---|---|---|
| `i` | int | The index `card:<n>` uses. On grids it indexes the full card list in display order, so gaps are normal (grouped copies, see below). On `choose` it is the position in the row, 0 to count-1. |
| `name` | string | Card title. It ends in `+` (or `+N`) when upgraded. No separate upgraded flag. |
| `cost` | string | Energy cost with modifiers: `"0"`, `"1"`, ..., `"X"`, `"-"` (unplayable), `"?"` (unreadable). A star cost is appended as `"+2*"` or `"+X*"`. |
| `type` | string | `CardType` name: `"Attack"`, `"Skill"`, `"Power"`, `"Status"`, `"Curse"`, `"Quest"`. |
| `text` | string | Description, cleaned like `prompt`. |
| `enchant` | string | Name of the card's enchantment. |
| `rarity` | string | `CardRarity` name: `"Basic"`, `"Common"`, `"Uncommon"`, `"Rare"`, `"Ancient"`, `"Event"`, `"Token"`, `"Status"`, `"Curse"`, `"Quest"`. Out of combat only. |
| `pile` | string | `PileType` name of this card's pile. In combat only, and only when the cards come from more than one pile. |
| `selected` | bool (`true`) | This card is currently selected. Selected cards are never grouped. |
| `copies` | int (2 or more) | This entry stands for that many identical unselected cards (identical means the whole JSON matches). Only the first copy's index is listed and offered, and pressing any copy does the same thing. |

Draw order is hidden. When any listed card (grid or `choose`) is in the draw pile, the whole list is sorted by its card JSON (ordinal string order) before indexes are assigned. Capture and execute both sort, so `card:<n>` stays consistent.

### `state` for `choose`

Keys: `kind` (`"choose"`), `prompt`, `min`, `max`, `cards`, `can_skip`, `run`, `combat`/`player`.

- `prompt` is normally absent. `FromChooseACardScreen` has no prefs, and the patch records no text for it.
- `min` is `0` when the factory's `canSkip` was true and `1` otherwise. `max` is `1`. Both appear only when the factory call was seen within 30 s, for the local player.
- `cards` are card entries (no `selected` or `copies`). Generated offers are in no pile, so in combat their text is the `Deck` variant.
- `can_skip` (bool) is true only when the screen's private `_canSkip` is not false and its `SkipButton` is visible and enabled right now. It can be false for a moment while the screen slides in.

### `state` for `bundle`

Keys: `kind` (`"bundle"`), `prompt`, `min`, `max`, `bundles`, then `picked` or `hint`, then `run`, `combat`/`player`.

- `bundles`: array of `{"n": <int>, "cards": [card objects]}`, in the factory's order (the `NCardBundle` children of `%BundleRow`). Bundle cards are plain card objects with no `i`. Out of combat they carry `rarity`.
- `picked` (int): the bundle the screen has open (its private `_selectedBundle`, or our own click if that can't be read). Present while that bundle's preview is opening or ready.
- `hint`: `"pick a bundle, then confirm it"` whenever `picked` is absent.
- `prompt`, `min` and `max` appear only if `FromChooseABundleScreen` carries `CardSelectorPrefs`. The patch records no defaults for it.

### Context: `run`, `player`, `combat`

`run` is `{"act", "floor", "ascension"}`. There is no `room` key here.

`player` (out of combat, `Describe.PlayerRun`, local player only):
`{"character": <string>, "hp": <int>, "max_hp": <int>, "gold": <int>, "relics": [<name>...], "potions": [<name or null per slot>], "deck_size": <int>, "deck": {<name>: <count>}}`.
- On a grid that lists every card of the deck (say a removal or upgrade at the start of a run), `deck` is left out and only `deck_size` is added, at the end.
- On `choose`, `bundle` and grids that don't list the whole deck, both `deck_size` and `deck` are present. `deck` keys are sorted ordinally.
- Relics and potions are names only.

`combat` (in combat; `player` is then absent):
`{"turn": <int>, "energy": <int>, "max_energy": <int>, "me": <creature>, "hand": [<card object>...], "draw_count": <int>, "discard_count": <int>, "exhaust_count": <int>, "enemies": [<enemy entry>...], "incoming_damage": <int>}`.
- A creature is `{"name", "hp", "max_hp", "block" (only if > 0), "powers" (only if any visible)}`. Each power is `{"name", "amount" (if not 0), "kind" (`"Buff"`/`"Debuff"`), "text"}`.
- `hand` entries are card objects (`Describe.Card` with the hand pile): `name`, `cost`, `type`, `text` (when not empty), `enchant` (when enchanted). The text is the live hand text. There is no `i`, `playable`, `unplayable` or `vs`, since no action here names a hand card.
- `enemies` entries are the combat snapshot's own `state.enemies[]` entries (`CombatReader.EnemiesJson`): `i`, `name`, `hp`, `max_hp`, `block`?, `powers`?, `intents`, `primary`?, `untargetable`?. Living enemies only, and each keeps the `i` it has for the whole fight, so the list can have gaps. `intents` are strings such as `"attack 12"`, `"attack 5x2"`, `"defend"`, `"buff"`, `"stunned"`, or `"<kind>: <label>"`, and `["unknown"]` if reading them threw.
- `incoming_damage` is the same sum of attack intents as the combat snapshot's (0 when nobody attacks). It is left out only when computing it threw.
- If the summary can't be built (it throws, or the local player's combat state can't be read), `combat` is left out and the out-of-combat `player` block is added instead.

### Actions

Action order in the list is: card or bundle picks in entry order, then `confirm`, then `cancel` (or `skip`).

#### Deck screens (`upgrade`, `transform`, `enchant`, `remove`, and `select` on `NDeckCardSelectScreen`)

These screens show the pick in a preview with its own confirm and back buttons, so a single pick takes two actions.

| Id | Label | Offered when | What it does |
|---|---|---|---|
| `card:<n>` | `"Pick <name> to <kind>"` for `remove`/`upgrade`/`transform`/`enchant`, `"Select <name>"` for `select`, `"Deselect <name>"` for a selected card | No preview is open or expected, and either the selection is below `max` or this card is already selected | Presses the card the way the grid does (its holder's `Pressed` signal, or the screen's `OnCardClicked` for a card scrolled out of view), toggling it. The pick that brings the selection to `max` (or any pick when `max` is 1 or unknown) opens the preview. |
| `confirm` (grid) | as below | The screen-level Confirm is visible and enabled. The game shows it only when min and max differ, and never on `upgrade`. On `enchant` at least one card must be selected. | Opens the preview early. This is not final, and a second `confirm` on the preview finishes. |
| `confirm` (preview) | `"Confirm: <kind> <names>"` (names comma-separated), or `"Confirm without choosing a card to <kind>"` when nothing is selected. For `select`: `"Confirm selection: <names>"` or `"Confirm with nothing selected"`. | The preview is visible, its Confirm is enabled, and it has been up at least 250 ms | Final: applies the upgrade, transform, enchant, removal or selection, and the screen closes. |
| `cancel` (preview) | `"Back to the cards (pick a different one)"` | The preview is ready, its back button is enabled, and at least one card is selected | Closes the preview. The game drops the pick, so the grid comes back. |
| `cancel` (close) | `"Close without removing a card"` (`remove`) or `"Close without choosing"` | `%Close` (an `NBackButton`) is visible and enabled, which the game allows only on cancelable screens (Smith, shop removal). In a ready preview it is offered only when the back button is not, as in a "choose none" preview. | Final: closes the whole screen without choosing. |

While the preview is open, picks are not offered. These screens would take a press past the maximum (a second card on a single upgrade gets upgraded too), so the reader guards against it.

#### Simple and combat-pile grids (`select` on `NSimpleCardSelectScreen`, `combat_pile_select`)

There is no preview.

| Id | Label | Offered when | What it does |
|---|---|---|---|
| `card:<n>` | `"Select <name>"` / `"Deselect <name>"` | Below `max`, or the card is selected | Toggles the card. When min equals max and the game needs no manual confirmation, the pick that reaches `max` closes the screen by itself. |
| `confirm` | `"Confirm selection: <names>"` or `"Confirm with nothing selected"` | The screen's `%Confirm` is visible and enabled. That happens when min and max differ, or when the prefs require manual confirmation, and the game decides when the button is enabled. | Final. |
| `cancel` | `"Close without choosing"` | `%Close` is visible and enabled | Final. |

At `max` with an enabled confirm, only the selected cards (to deselect), `confirm`, and `cancel` when the close button is usable are offered. At `max` without a confirm, nothing is offered: the waiting text is `"selection complete; screen closing"`. If the grid is still up 5 s later, the reader stops trusting `max` and offers every card again.

#### `choose`

| Id | Label | Offered when | What it does |
|---|---|---|---|
| `card:<n>` | `"Take <name>"` | The screen has been up 400 ms (it ignores presses for 350 ms) | Final: takes that card. There is no confirm. |
| `skip` | `"Skip"` | `can_skip` is true | Final: takes nothing. |

#### `bundle`

| Id | Label | Offered when | What it does |
|---|---|---|---|
| `bundle:<n>` | `"Pick the bundle with <card names>"` (`"unknown cards"` if empty) | No bundle preview is open, that bundle's hitbox is enabled, and the bundle itself (`NCardBundle`) is visible with its alpha above 0.05 | Opens that bundle's preview. Not final. |
| `confirm` | `"Take the bundle with <card names>"` (or `"Take the picked bundle"`) | A bundle is previewed, `%Confirm` is enabled, and it has been up 250 ms | Final: the bundle's cards join the deck. |
| `cancel` | `"Back to the bundles (pick a different one)"` | A bundle is previewed and `%Cancel` is enabled | Back to the bundle row. |

`confirm`, `cancel` and `skip` are not offered anywhere else. On grids there is no `skip`, and on `choose` there is no `confirm` or `cancel`.

### Min and max

Both count cards. Where they come from:

- Grids: the screen's private `_prefs` (`CardSelectorPrefs.MinSelect`/`MaxSelect`), read once per screen. Failing that, the factory record that `ChoicePromptPatch` saw.
- `choose`: the factory record (`canSkip` gives min 0, otherwise 1; max 1).
- `bundle`: the factory record only.

A factory record counts only when all of these hold:
- it is no older than 30 s;
- its factory fits the screen type;
- it belongs to the local player;
- no other screen has already taken it.

How they shape the flow:
- `min == max` (most Act 1 cases: Smith 1/1, removal 1/1, Shears 2/2, Headbutt 1/1): pick exactly `max`. Deck screens then show the preview, and simple and pile grids close by themselves.
- `min < max`: a confirm appears once the game enables it (grid confirm on deck screens, then the preview confirm).
- `min == 0`: on a deck screen, confirming with nothing picked opens a "choose none" preview (`preview: "open"`), which can only be confirmed or closed.

### Refusals

The common Core and bridge refusals come first (see Refusals under The common envelope); the bridge's re-check says `'<id>' is no longer available on the card_select screen.` Any id not in the current `actions` gets `'<id>' is not a legal action now.`: for example `skip` on a grid, `confirm` on `choose`, or `card:6` when only `card:5` is listed for a group of copies.

The reader's own refusals only fire in races, because Core and the bridge already matched the id:

- Any screen: `"The selection screen is gone."`, `"No run in progress."`, `"Cannot choose now (<reason>)."` (reason is one of the waiting texts below).
- Grids: `"Unknown card selection action '<id>'."`, `"A preview is open; confirm or cancel it first."`, `"The preview is still opening."`, `"There is no card <n>."`, `"The maximum is already selected; deselect a card first."`, `"<name> is gone."`, `"<name> was already clicked; waiting for the game to react."`, `"Could not pick <name>: <ExceptionType>: <message>"`, `"The confirm button is not enabled."`, `"the confirm button was already clicked; waiting for the game to react."`, `"There is no enabled way to back out of this screen."`, `"the preview's back button was already clicked; waiting for the game to react."`, `"the close button was already clicked; waiting for the game to react."`
- `choose`: `"This choice cannot be skipped now."`, `"the skip button was already clicked; waiting for the game to react."`, `"Unknown card choice action '<id>'."`, `"There is no card <n>."`, `"<name> was already clicked; waiting for the game to react."`
- `bundle`: `"No bundle is ready to confirm."`, `"There is no bundle preview to back out of."`, `"Unknown bundle action '<id>'."`, `"A bundle is already picked; confirm or cancel it first."`, `"There is no bundle <n>."`, `"That bundle cannot be picked right now."`, `"bundle <n> was already clicked; waiting for the game to react."`, `"the cancel button was already clicked; waiting for the game to react."`

In `act_sequence`, a game refusal stops the run with `"The game refused '<label>': <refusal>"`.

On success, `act` returns `{"done": <id>, "next": <snapshot>}`. `next` is the next settled decision: the tools wait up to 20 s past any transitional snapshot. In `act_sequence`, `confirm`, `cancel` and `skip` are matched again by id. `card:<n>` and `bundle:<n>` are matched again by label, so `["card:9", "confirm"]` on a deck screen runs as pick then preview confirm.

### Waiting snapshots (`awaiting_decision: false`)

| `waiting` | `state` | When |
|---|---|---|
| `"resolving <what>"` | `{}` | One of our clicks is being tracked. `<what>` is one of: `picking <name>`, `deselecting <name>`, `backing out of the preview`, the confirm label, `closing the selection`, `skipping the card choice`, `taking <name>`, `picking bundle <n>`, `backing out of the bundle`, or the bundle confirm label. `screen` is the last routed screen name. |
| `"selection screen closing"` | `{}` | The overlay is no longer on top. |
| `"run loading"` | `{}` | No local player yet. |
| `"screen opening"` | full | The screen is not yet visible, or has been up less than 300 ms (400 ms for `choose`). |
| `"choice sent; waiting for the screen to close"` (single player) or `"choice sent; waiting for the game and teammates"` (co-op) | full | After a final click, for up to 10 s. After that the screen's actions are offered again. |
| `"preview opening"` | full | Grids: a pick or grid confirm was made and the preview is expected (up to 1.5 s), or the preview is up but its confirm is not ready. A preview whose confirm never enables is treated as closed after 4 s. |
| `"selection complete; waiting for the preview"` | full | Deck screen at `max` without a preview yet. |
| `"selection complete; screen closing"` | full | Simple or pile grid at `max` with no confirm (5 s grace, see above). |
| `"nothing to pick yet"` | full | Grid with no action to offer. |
| `"cards still being dealt"` | full | `choose` with no cards and no skip. |
| `"bundle preview opening"` | full | `bundle` preview up for less than 250 ms. |
| `"bundles still being dealt"` | full | No pickable bundle. |

### Example snapshots

Card texts, prompts and numbers below are illustrative. The real text is the game's own.

Shop removal, grid (after `buy:remove`; an upgraded Defend and a Pommel Strike in the deck):

```json
{
  "version": 412,
  "screen": "card_select",
  "awaiting_decision": true,
  "state": {
    "kind": "remove",
    "prompt": "Choose a card to remove.",
    "min": 1,
    "max": 1,
    "hint": "pick a card, then confirm it on the preview",
    "cards": [
      {"i": 0, "name": "Strike", "cost": "1", "type": "Attack", "text": "Deal 6 damage.", "rarity": "Basic", "copies": 5},
      {"i": 5, "name": "Defend", "cost": "1", "type": "Skill", "text": "Gain 5 Block.", "rarity": "Basic", "copies": 3},
      {"i": 8, "name": "Defend+", "cost": "1", "type": "Skill", "text": "Gain 8 Block.", "rarity": "Basic"},
      {"i": 9, "name": "Bash", "cost": "2", "type": "Attack", "text": "Deal 8 damage. Apply 2 Vulnerable.", "rarity": "Basic"},
      {"i": 10, "name": "Pommel Strike", "cost": "1", "type": "Attack", "text": "Deal 9 damage. Draw 1 card.", "rarity": "Common"}
    ],
    "run": {"act": 1, "floor": 6, "ascension": 0},
    "player": {"character": "The Ironclad", "hp": 58, "max_hp": 80, "gold": 41, "relics": ["Burning Blood", "Pomander"], "potions": ["Fire Potion", null, null], "deck_size": 11}
  },
  "actions": [
    {"id": "card:0", "label": "Pick Strike to remove"},
    {"id": "card:5", "label": "Pick Defend to remove"},
    {"id": "card:8", "label": "Pick Defend+ to remove"},
    {"id": "card:9", "label": "Pick Bash to remove"},
    {"id": "card:10", "label": "Pick Pommel Strike to remove"},
    {"id": "cancel", "label": "Close without removing a card"}
  ]
}
```

The same screen after `card:9`, preview ready. `state` gains `"selected": ["Bash"]`, loses `hint`, and gains `"preview": [{"name": "Bash", "cost": "2", "type": "Attack", "text": "Deal 8 damage. Apply 2 Vulnerable.", "rarity": "Basic"}]`. The Bash entry becomes `{"i": 9, ..., "rarity": "Basic", "selected": true}`. Actions become:

```json
[
  {"id": "confirm", "label": "Confirm: remove Bash"},
  {"id": "cancel", "label": "Back to the cards (pick a different one)"}
]
```

A multi-pick (Precarious Shears, `min` 2, `max` 2) after one Strike is picked. The picked copy leaves its group:

```json
"selected": ["Strike"],
"hint": "pick a card, then confirm it on the preview",
"cards": [
  {"i": 0, "name": "Strike", "cost": "1", "type": "Attack", "text": "Deal 6 damage.", "rarity": "Basic", "selected": true},
  {"i": 1, "name": "Strike", "cost": "1", "type": "Attack", "text": "Deal 6 damage.", "rarity": "Basic", "copies": 4},
  {"i": 5, "name": "Defend", "cost": "1", "type": "Skill", "text": "Gain 5 Block.", "rarity": "Basic", "copies": 4},
  {"i": 9, "name": "Bash", "cost": "2", "type": "Attack", "text": "Deal 8 damage. Apply 2 Vulnerable.", "rarity": "Basic"}
]
```

with actions `card:0` "Deselect Strike", `card:1` "Pick Strike to remove", `card:5` "Pick Defend to remove", `card:9` "Pick Bash to remove", and `cancel` only if the screen is closable.

Headbutt, mid-fight pile pick:

```json
{
  "version": 980,
  "screen": "card_select",
  "awaiting_decision": true,
  "state": {
    "kind": "combat_pile_select",
    "prompt": "Choose a card to put on top of your draw pile.",
    "min": 1,
    "max": 1,
    "pile": "Discard",
    "cards": [
      {"i": 0, "name": "Strike", "cost": "1", "type": "Attack", "text": "Deal 6 damage.", "copies": 2},
      {"i": 2, "name": "Bash", "cost": "2", "type": "Attack", "text": "Deal 8 damage. Apply 2 Vulnerable."},
      {"i": 3, "name": "Defend", "cost": "1", "type": "Skill", "text": "Gain 5 Block."}
    ],
    "run": {"act": 1, "floor": 3, "ascension": 0},
    "combat": {
      "turn": 2,
      "energy": 1,
      "max_energy": 3,
      "me": {"name": "The Ironclad", "hp": 71, "max_hp": 80, "block": 5},
      "hand": [
        {"name": "Strike", "cost": "1", "type": "Attack", "text": "Deal 6 damage."},
        {"name": "Defend", "cost": "1", "type": "Skill", "text": "Gain 5 Block."},
        {"name": "Defend", "cost": "1", "type": "Skill", "text": "Gain 5 Block."}
      ],
      "draw_count": 1,
      "discard_count": 4,
      "exhaust_count": 0,
      "enemies": [
        {"i": 0, "name": "Nibbit", "hp": 22, "max_hp": 44, "intents": ["attack 6", "defend"]}
      ],
      "incoming_damage": 6
    }
  },
  "actions": [
    {"id": "card:0", "label": "Select Strike"},
    {"id": "card:2", "label": "Select Bash"},
    {"id": "card:3", "label": "Select Defend"}
  ]
}
```

Attack Potion, choose one of three:

```json
{
  "version": 233,
  "screen": "card_select",
  "awaiting_decision": true,
  "state": {
    "kind": "choose",
    "min": 0,
    "max": 1,
    "cards": [
      {"i": 0, "name": "Pommel Strike", "cost": "1", "type": "Attack", "text": "Deal 9 damage. Draw 1 card."},
      {"i": 1, "name": "Thunderclap", "cost": "1", "type": "Attack", "text": "Deal 4 damage to ALL enemies. Apply 1 Vulnerable to ALL enemies."},
      {"i": 2, "name": "Uppercut", "cost": "2", "type": "Attack", "text": "Deal 13 damage. Apply 1 Weak. Apply 1 Vulnerable."}
    ],
    "can_skip": true,
    "run": {"act": 1, "floor": 4, "ascension": 0},
    "combat": {
      "turn": 1,
      "energy": 3,
      "max_energy": 3,
      "me": {"name": "The Ironclad", "hp": 64, "max_hp": 80},
      "hand": [
        {"name": "Strike", "cost": "1", "type": "Attack", "text": "Deal 6 damage."},
        {"name": "Strike", "cost": "1", "type": "Attack", "text": "Deal 6 damage."},
        {"name": "Defend", "cost": "1", "type": "Skill", "text": "Gain 5 Block."},
        {"name": "Bash", "cost": "2", "type": "Attack", "text": "Deal 8 damage. Apply 2 Vulnerable."},
        {"name": "Defend", "cost": "1", "type": "Skill", "text": "Gain 5 Block."}
      ],
      "draw_count": 5,
      "discard_count": 0,
      "exhaust_count": 0,
      "enemies": [
        {"i": 0, "name": "Nibbit", "hp": 44, "max_hp": 44, "intents": ["attack 12"]}
      ],
      "incoming_damage": 12
    }
  },
  "actions": [
    {"id": "card:0", "label": "Take Pommel Strike"},
    {"id": "card:1", "label": "Take Thunderclap"},
    {"id": "card:2", "label": "Take Uppercut"},
    {"id": "skip", "label": "Skip"}
  ]
}
```

Neow's Scroll Boxes, bundle pick:

```json
{
  "version": 18,
  "screen": "card_select",
  "awaiting_decision": true,
  "state": {
    "kind": "bundle",
    "bundles": [
      {"n": 0, "cards": [
        {"name": "Anger", "cost": "0", "type": "Attack", "text": "Deal 6 damage. Add a copy of this card into your discard pile.", "rarity": "Common"},
        {"name": "Iron Wave", "cost": "1", "type": "Attack", "text": "Gain 5 Block. Deal 5 damage.", "rarity": "Common"},
        {"name": "Uppercut", "cost": "2", "type": "Attack", "text": "Deal 13 damage. Apply 1 Weak. Apply 1 Vulnerable.", "rarity": "Uncommon"}
      ]},
      {"n": 1, "cards": [
        {"name": "Pommel Strike", "cost": "1", "type": "Attack", "text": "Deal 9 damage. Draw 1 card.", "rarity": "Common"},
        {"name": "Shrug It Off", "cost": "1", "type": "Skill", "text": "Gain 8 Block. Draw 1 card.", "rarity": "Common"},
        {"name": "Battle Trance", "cost": "0", "type": "Skill", "text": "Draw 3 cards. You cannot draw additional cards this turn.", "rarity": "Uncommon"}
      ]}
    ],
    "hint": "pick a bundle, then confirm it",
    "run": {"act": 1, "floor": 1, "ascension": 0},
    "player": {"character": "The Ironclad", "hp": 80, "max_hp": 80, "gold": 99, "relics": ["Burning Blood", "Scroll Boxes"], "potions": [null, null, null], "deck_size": 10, "deck": {"Bash": 1, "Defend": 4, "Strike": 5}}
  },
  "actions": [
    {"id": "bundle:0", "label": "Pick the bundle with Anger, Iron Wave, Uppercut"},
    {"id": "bundle:1", "label": "Pick the bundle with Pommel Strike, Shrug It Off, Battle Trance"}
  ]
}
```

After `bundle:0`, `hint` is replaced by `"picked": 0`, and the actions become `confirm` "Take the bundle with Anger, Iron Wave, Uppercut" and `cancel` "Back to the bundles (pick a different one)".

### Co-op differences

- The screens are local to the chooser. There is no vote and no teammate gate before the pick. When the final click lands the overlay closes, and whatever waited on it (event, relic, card, rest option) carries on.
- `ChoicePromptPatch` ignores factory calls whose `Player` argument is not the local player. A teammate's choice never supplies this screen's `prompt`, `min` or `max`.
- A teammate's selection screen opens only on the teammate's machine. The bot never sees it as `card_select`; it sees its own underlying screen.
- After a final click in a multiplayer run, the waiting text is `"choice sent; waiting for the game and teammates"` instead of `"choice sent; waiting for the screen to close"`.
- `player` and `combat` describe only the local player. There are no teammate HP lines (unlike screens that use `Describe.TeamHp`). `combat.enemies` is the shared enemies list.
- `selected` and the bundle `picked` read the screen's own state, so a human clicking on the bot's window is reflected.

### Left out on purpose

- Draw pile order: any list holding draw-pile cards is sorted by card JSON.
- Repeated identical cards: unselected copies collapse into one entry with `copies`, under the first copy's index.
- A random transform's result: the cycling preview holder is skipped, so `preview` never reveals the outcome.
- Cards in hidden or faded nodes (alpha 0.05 or less) are not counted in the preview.
- Deck name counts when the grid already lists the whole deck (only `deck_size`).
- Relic and potion text, and full deck card text on `choose`/`bundle`: `player` has names only. `get_deck` has the rest and works on this screen.
- `rarity` in combat. Combat card text is the live pile text instead.
- In combat: relics, potions, pile contents (counts only), teammates.
- Potion actions: none are offered on `card_select`.
- An upgraded flag: the `+` in `name` carries it.
- What opened the screen (which event, relic, card or shop) and what the choice will cost or give beyond the prompt text.

### For the arena

To match the mod key for key, reproduce:
- the eight `kind` values and when each is used;
- the two-action deck flow (pick, then preview `confirm`; `cancel` goes back) against the one-action `choose` and the self-closing simple and pile grids at `max`;
- `min`/`max` from the game's prefs;
- copy grouping and draw-pile sorting, with `i` as the pre-grouping index;
- `deck_size` instead of `deck` when the grid is the whole deck;
- the exact labels, because `act_sequence` matches picks by label;
- `can_skip`;
- the bundle `bundle:<n>` then `confirm` flow with `picked`/`hint`;
- the context keys, including the in-combat `combat` summary exactly as above: hand card objects, `exhaust_count`, the combat snapshot's enemy entries (with `i`, `primary` and `untargetable`) and `incoming_damage`. The arena's `Fight.CardSelectSnapshot` summary already writes this shape (commit f3ff64a).

The arena's current fight-only `Fight.CardSelectSnapshot` always writes `min` and `max`, and it covers only `choose`, `combat_pile_select` and `select`.

## Events and other screens

Built by `src/SpireMind/Game/Screens/EventReader.cs` (`event`) and `src/SpireMind/Game/Screens/MiscReaders.cs` (`crystal_sphere`, `modal`, `game_over`, `menu`). Shared pieces come from `PotionActions.cs`, `Describe.cs`, `ScreenRouter.cs`, `GameBridge.cs`, and Core's `BotTools.cs` and `GameSnapshot.cs`. Everything below is read from that code (public branch v0.107.1).

### What these screens share

These screens use the common envelope, busy snapshots and refusals. After an event choice the busy snapshot's `screen` is `event`, with a `waiting` such as `"resolving choosing Walk away"`. The reader refusals listed below are a backstop the bot sees only in a race, because the bridge has just checked a fresh capture in the same main-thread call. A successful `act` returns once the game has settled into a newer decision that holds for 300 ms, or after 20 s, which is why the short "appearing" waits below are usually absorbed and never seen.

- `run`, `player` (event and game over) and the detailed `potions` (event only) are the shared blocks. The event snapshot removes `player.potions` and lists the detailed `potions` at top level instead; `game_over` keeps the name-only `player.potions`.
- A card in an event option's hover tips is the shared card object without `rarity`: `{"name", "cost", "type", "text"?, "enchant"?}`, where `text` is the deck-pile description.
- A relic is `{"name": string, "text": string}`.

### Event (`"screen": "event"`)

#### When the bot sees it

In an event room (`NEventRoom`, or `RunState.CurrentRoom.RoomType == Event`) when nothing outranks it. The router checks in this order:

1. The feedback or an inspect screen (busy).
2. A modal popup.
3. A deck view or the pause menu (busy).
4. Outside a run: the menu or game over.
5. The map, if open.
6. The top of the overlay stack (rewards, card reward, card selection, Crystal Sphere, game over).
7. A running fight.
8. Only then the room.

So the event snapshot shows:

- On arrival at an Unknown room that rolled an event.
- At Neow on floor 1 of act 1, and at the Ancient that opens acts 2 and 3. They are events in `NEventRoom` too (see Neow and the Ancients).
- Again once anything an option opened on top of it has closed: a card selection, a rewards screen, a card reward, or a fight the event started (the room is fetched again after the fight). The Crystal Sphere usually does not return here, because its proceed opens the map.

One event room can show several pages. A page is a set of options (each an `EventOption` object), the `IsFinished` flag and the page text. When any of these change, it is a new page.

#### `state`

Keys in this order:

| Key | Type | Present | Meaning |
| --- | --- | --- | --- |
| `run` | object | always | Act, floor, ascension. |
| `event` | string | always | The event's title (`""` if the game's text can't be formatted). |
| `ancient` | `true` | Ancient layout only | The room uses `NAncientEventLayout`: an Ancient (and Neow, see below). Left out otherwise. |
| `shared_vote` | bool | co-op only | `EventModel.IsShared`: true when the choice is a team vote. Left out in single player. |
| `text` | string | when non-empty | The page text from the model (`EventModel.Description`, only if its loc key exists). When the model has none, the longest visible label in the room's layout outside the option buttons. Left out when both are empty. |
| `finished` | `true` | finished events only | `EventModel.IsFinished`. The model then has no options, and the room shows its own Leave option. Left out otherwise. |
| `options` | array | always | One object per option of the current page, in the model's order (index = `n`). Can be `[]` while options are appearing. |
| `voted` | string | co-op shared events, after we voted | The title of the option our pending vote is for (`"option <i>"` if the index is out of range). The game clears it when the host picks. |
| `player` | object | always | See shared sub-objects. No `potions` key. |
| `potions` | array | always | Detailed potion slots. |
| `team` | array of strings | co-op shared events with teammates | Each teammate as `"<character title> <hp>/<max_hp> hp"`. |

#### An option object

Keys in this order. Every key after `title` is left out when it does not apply.

| Key | Type | Meaning |
| --- | --- | --- |
| `n` | int | The option's index on this page. The action is `option:<n>`. Starts at 0 on every page. |
| `title` | string | The option's title. If it has none, its text cut to 60 characters (57 plus `...`). If the text is empty too, `"option <n>"`. |
| `text` | string | The option's description, when it is non-empty and differs from `title`. |
| `leave` | `true` | The plain proceed option (`IsProceed` with `TextKey` `PROCEED`), or the Leave of a custom-layout event. Choosing it leaves the room for the map. |
| `locked` | `true` | `EventOption.IsLocked` (a requirement is not met). Never offered. |
| `will_kill_player` | `true` | The option's `WillKillPlayer` predicate says it would kill us. Never offered. |
| `relic` | relic object | For Ancient options, the relic the option grants (`EventOption.Relic`). For any option, a relic shown in its hover tips. Left out when the relic's name and text equal the option's `title` and `text`, which is usual for Ancient options. |
| `cards` | array of card objects | Up to 3 distinct cards (by name) from the option's hover tips, such as the card an option adds or the curse it gives. |
| `note` | string | A lasting reason. One of: `"opens the abandon-run popup; not offered"` (a proceed option keyed `DOUBLE_DOWN`, the Trial's), `"leaves the event in a way the bot does not drive; not offered"` (any other `IsProceed` option not keyed `PROCEED`), `"would kill you; not offered"`, `"ends the run; in co-op once every player has chosen it"` (TheArchitect's last option), `"this event draws its own screen; the bot can only leave it"` (custom-layout Leave). A locked option gets no note. |

#### Which options are offered

An option is offered as `option:<n>` only when all of these hold. They are checked in this order, and the first one that fails gives the refusal:

1. Not locked. Refusal: `<title> is locked.`
2. Not a special proceed option. Refusal: `<title> opens the abandon-run popup; not offered.` or `<title> leaves the event in a way the bot does not drive; not offered.`
3. Would not kill us. Refusal: `<title> would kill you.`
4. Its `NEventOptionButton` was found, matched by the option object it was built for. Refusal: `No button found for <title>.`
5. The button is shown and enabled. The "shown" part is skipped while an Ancient is still talking. Refusal: `<title> is not clickable right now.`
6. The button was not clicked in the last 2.5 s. Refusal: `<title> was just clicked; waiting for the game to react.`

Options that fail checks 4 to 6 are still listed in `state.options`, with no flag. An option that is listed, has no `locked`, `will_kill_player` or `note`, and is missing from `actions` is simply not clickable yet.

#### Actions

| Id | Label | Does |
| --- | --- | --- |
| `option:<n>` | `Leave` for a leave option. Ancient: `Take <relic name, else title>`. Otherwise: `Choose <title>`. In a co-op shared event, `Vote for` replaces `Take` and `Choose`. TheArchitect's last option adds ` (ends the run)`. | Clicks the option's button (`ForceClick`), which the game sends through `EventSynchronizer.ChooseLocalOption`. For an Ancient whose dialogue is still running, it first clicks through the rest of the dialogue (at most 20 clicks). |
| `potion:<slot>` | `Use <potion>` | Uses a potion that works out of combat: not CombatOnly or Automatic, and needing no enemy. Self and AnyPlayer potions go to us. |
| `discard_potion:<slot>` | `Discard <potion>` | Discards the potion. |

Option ids come first, in `n` order, then the potion ids. Potion ids are added only when at least one option is offered, so potions alone never make the event a decision. The out-of-combat potion rules are the same as on the map.

#### What happens after `option:<n>`

The tracker keeps every snapshot busy (`"resolving <what>"`, `state: {}`) until the game has visibly acted or a timeout passes:

| Choice | `<what>` | Timeout |
| --- | --- | --- |
| Leave | `leaving the event` | 6 s |
| Co-op shared event, not Leave | `voting for <title>` | 10 s (at least 500 ms) |
| Anything else | `choosing <title>` | 8 s |

The choice counts as done when any of these happens:

- the room or its model changes
- a fight starts
- the map opens
- a modal opens
- a new overlay goes on top (card selection, rewards, card reward, Crystal Sphere)
- the local event changes
- the page's finished flag or page key changes
- for votes only, the game looks idle

The next snapshot is whichever screen is then up: the same event's next page, `card_select`, `rewards`, `card_reward`, `combat`, `crystal_sphere`, `modal`, or `map` after Leave.

#### Waiting snapshots

Busy, with `state: {}`:

- `run loading`, `event room loading`, `event loading`
- `fight started by the event` (a fight is in progress, which the combat reader normally handles)
- `returning to the event after the fight` (the event's room is at the base of the room stack and the fight's room is still on top)

With the full `state` and `awaiting_decision: false`:

- `ending the run`, or in co-op `ready to end the run; waiting for teammates`: after TheArchitect's last option, for the rest of that event.
- `leaving the event`: for 10 s after choosing Leave, if the event is still the screen. After that, Leave is offered again.
- `voted for <title>; waiting for teammates' votes`: in a co-op shared event, while our vote stands, until the host picks for everyone.
- `event appearing`: the first 500 ms after a new room or event shows.
- `event options appearing`: the first 300 ms after every page change, or a page that has no options and is not finished.
- `event finished; waiting for the leave option`: the event is finished but the room's Leave button is not up yet.
- `waiting for the event's next options`: an option's buttons were cleared and the next page's buttons have not arrived.
- `no event option the bot can take (all locked, deadly or special); a human has to choose`: every option is locked, deadly or a special proceed. The bot stays here by design until a human picks.
- `no event option is clickable yet`: any other case where no option passes the checks above.

#### Refusals from the reader (backstop)

- `Unknown event action '<id>'.`
- `No run in progress.`
- `Not at the event right now (<fight started by the event | returning to the event after the fight>).`
- `The event room is gone.`
- `No event is running.`
- `Not ready: <waiting reason>.`
- `There is no event option <n>.`
- The per-option refusals listed above.
- The click guard: `the <title> option is gone.` and `the <title> option was already clicked; waiting for the game to react.`
- The custom-layout Leave: `This event's proceed button was not found.`, `Leave is not clickable right now.` and `Leave was just clicked; waiting for the game to react.`
- Potions: `'<id>' is not available now.`

#### Multi-page events

- Each page is read fresh from the model (`EventModel.CurrentOptions`). New option objects make a new page even when the wording repeats. A lock appearing or lifting, a title change or a text change also makes a new page.
- Indices restart at 0 on every page, so an `option:1` from one page means nothing on the next. The version changes with the page, so a stale id is refused when the bot sends `version`. Without `version`, the same id on the new page would run whatever option now has that index.
- After a page change nothing is offered for 300 ms (`event options appearing`). The Leave latch, the "nothing matched" log and the cached hover-tip extras are reset.
- A non-shared choice clears the buttons at once. Until the next page's buttons arrive, the snapshot waits with `waiting for the event's next options`.
- The last page of most events is the finished page: `finished: true`, the result text in `text`, and one option. That option is the room's own proceed option, with `leave: true` and the label `Leave`. Its `title` is the game's own text for it (the code comments say it reads "Leave").
- Leaving is always one of the `option:<n>` ids, never `proceed`. Choosing it opens the map, and the bridge records the room as left. The map reader needs that record before it will travel.

#### Neow and the Ancients

- Ancients are events with `NAncientEventLayout`, and their snapshot carries `ancient: true`. Neow is act 1's Ancient: the map reader treats the act's start node as Ancient-typed and names `act.Ancient`. The event reader has no Neow-specific code. Neow's first page has no page text of its own (its `Description` has no loc key), so `text` falls back, as for any such page, to the longest visible label in the layout outside the option buttons.
- Options are offered while the Ancient is still talking. The buttons only need to be enabled, not shown. Choosing one clicks through the remaining dialogue first, as a human does.
- The label names the relic: `Take <relic name>`, from `EventOption.Relic` or a relic in the option's hover tips. `relic` appears in the option object only when its name or text differ from the option's own. Usually the option's `title` and `text` already are the relic's name and text.
- When a modifier skips the Ancient, its only option is the plain proceed: `Leave`.
- Picking a relic can open another screen at once: a removal, transform or upgrade picker, or a choose-one offer, as `card_select`; a card offer as `card_reward`; extra rewards as `rewards`. The event comes back afterwards with its finished page.
- TheArchitect, the run's last event, has a last option keyed `PROCEED` that is not a proceed option. It is offered with the label suffix ` (ends the run)` and the note `"ends the run; in co-op once every player has chosen it"`. It is clicked once per event, and the event then waits with `ending the run`. A won run reaches `game_over` with `result: "victory"` this way (docs/game-api.md: victory goes through TheArchitect's PROCEED option). An act 1 boss win never does, because the run goes on to act 2.

#### Events that draw their own screen

A custom-layout event (FakeMerchant in v0.107.1) has no option layout. Any options its model has are listed but never offered, because they have no button. The reader adds one more option at the end: `{"n": <count>, "title": "Leave", "leave": true, "note": "this event draws its own screen; the bot can only leave it"}`, which clicks the scene's own proceed button. The bot does not drive the rest of such an event.

#### Fights, rewards and card choices an event starts

These are not part of the event snapshot:

- A fight goes to `combat`, and its rewards go to `rewards`.
- Card selection, rewards and card reward screens pushed over the room go to their own readers.
- The Crystal Sphere goes to `crystal_sphere`.

When they close, the event shows again. After a fight there is a short `returning to the event after the fight` gap first.

#### Co-op differences

- `shared_vote` is present on every event in co-op.
- In a shared event every option except Leave is a vote. Labels read `Vote for ...`, and the action counts as done once the game is idle (at least 500 ms). Afterwards the snapshot waits with `voted for <title>; waiting for teammates' votes` and carries `voted` until the host picks. The bot never votes twice.
- Leave runs locally even in a shared event (the room handles proceed options itself), so it is never a vote.
- `team` lists teammates' HP in shared events, since everyone lives with the result.
- A non-shared event in co-op is each player's own copy: labels read `Choose` or `Take`, and there is no `voted` and no `team`.
- A deadly option is never offered. In co-op the game's button would refuse it anyway.
- TheArchitect's last option waits with `ready to end the run; waiting for teammates`.

#### Left out on purpose

- The relic behind a non-Ancient option's "random relic". The game can roll it in advance without showing it, so `EventOption.Relic` is read only for Ancients, whose buttons draw the relic's icon.
- Hover tips other than cards (at most 3) and relics, such as keyword tooltips.
- Outcomes the option text does not state (random results, which fight an option starts).
- The Ancient's dialogue lines, except where one ends up as the `text` fallback.
- Teammates' votes. Only our own vote is shown.
- The full deck text. `player.deck` is name counts only; the bot calls `get_deck` for text.
- Which button is not yet clickable. Such an option has no flag.
- Special proceed options (the Trial's DOUBLE_DOWN and others). They are listed with a note but never offered.

#### Example: Neow, single player

The shape is exact. The relic wording and the `text` line are made up for illustration.

```json
{
  "version": 37,
  "screen": "event",
  "awaiting_decision": true,
  "state": {
    "run": { "act": 1, "floor": 1, "ascension": 0 },
    "event": "Neow",
    "ancient": true,
    "text": "Ah... a new challenger. Choose a gift.",
    "options": [
      { "n": 0, "title": "Precise Scissors", "text": "Upon pickup, remove 1 card from your Deck." },
      {
        "n": 1, "title": "Neow's Torment", "text": "Upon pickup, add 1 Neow's Fury to your Deck.",
        "cards": [ { "name": "Neow's Fury", "cost": "1", "type": "Attack", "text": "Deal 10 damage. Put up to 2 cards from your Discard Pile into your Hand. Exhaust." } ]
      },
      { "n": 2, "title": "Precarious Shears", "text": "Upon pickup, remove 2 cards from your Deck and take 16 damage." }
    ],
    "player": {
      "character": "The Ironclad", "hp": 80, "max_hp": 80, "gold": 99,
      "relics": [ "Burning Blood" ],
      "deck_size": 10, "deck": { "Bash": 1, "Defend": 4, "Strike": 5 }
    },
    "potions": [ null, null, null ]
  },
  "actions": [
    { "id": "option:0", "label": "Take Precise Scissors" },
    { "id": "option:1", "label": "Take Neow's Torment" },
    { "id": "option:2", "label": "Take Precarious Shears" }
  ]
}
```

#### Example: an ordinary event page with a locked and a deadly option

The event and option wording are made up. The keys, flags, labels and which ids appear are exact. The bot has 31 HP, 40 gold and a Blood Potion.

```json
{
  "version": 212,
  "screen": "event",
  "awaiting_decision": true,
  "state": {
    "run": { "act": 1, "floor": 6, "ascension": 0 },
    "event": "The Shrine",
    "text": "A crumbling shrine hums with old power.",
    "options": [
      { "n": 0, "title": "Pray", "text": "Pay 75 Gold. Obtain a random relic.", "locked": true },
      { "n": 1, "title": "Bleed", "text": "Lose 35 HP. Upgrade 2 random cards.", "will_kill_player": true, "note": "would kill you; not offered" },
      { "n": 2, "title": "Walk away" }
    ],
    "player": {
      "character": "The Ironclad", "hp": 31, "max_hp": 80, "gold": 40,
      "relics": [ "Burning Blood" ],
      "deck_size": 13, "deck": { "Bash": 1, "Defend": 4, "Inflame": 1, "Pommel Strike": 1, "Strike": 5, "Twin Strike": 1 }
    },
    "potions": [
      { "slot": 0, "name": "Blood Potion", "text": "Heal for 20% of your Max HP.", "target": "AnyPlayer", "usable": true },
      null,
      null
    ]
  },
  "actions": [
    { "id": "option:2", "label": "Choose Walk away" },
    { "id": "potion:0", "label": "Use Blood Potion" },
    { "id": "discard_potion:0", "label": "Discard Blood Potion" }
  ]
}
```

After `option:2` the finished page could read as below. The Leave option would also carry `text` if the game gave the proceed option a description of its own.

```json
{
  "version": 215,
  "screen": "event",
  "awaiting_decision": true,
  "state": {
    "run": { "act": 1, "floor": 6, "ascension": 0 },
    "event": "The Shrine",
    "text": "You leave the shrine undisturbed.",
    "finished": true,
    "options": [ { "n": 0, "title": "Leave", "leave": true } ],
    "player": {
      "character": "The Ironclad", "hp": 31, "max_hp": 80, "gold": 40,
      "relics": [ "Burning Blood" ],
      "deck_size": 13, "deck": { "Bash": 1, "Defend": 4, "Inflame": 1, "Pommel Strike": 1, "Strike": 5, "Twin Strike": 1 }
    },
    "potions": [
      { "slot": 0, "name": "Blood Potion", "text": "Heal for 20% of your Max HP.", "target": "AnyPlayer", "usable": true },
      null,
      null
    ]
  },
  "actions": [
    { "id": "option:0", "label": "Leave" },
    { "id": "potion:0", "label": "Use Blood Potion" },
    { "id": "discard_potion:0", "label": "Discard Blood Potion" }
  ]
}
```

In co-op, a shared page after our vote has `awaiting_decision: false`, `waiting: "voted for Walk away; waiting for teammates' votes"` and `actions: []`. Its `state` adds `"shared_vote": true` after `event`, `"voted": "Walk away"` after `options`, and `"team": ["The Silent 41/70 hp"]` at the end.

### Crystal Sphere (`"screen": "crystal_sphere"`)

#### When the bot sees it

When `NCrystalSphereScreen`, the Crystal Sphere event's minigame, is the top of the overlay stack. It outranks the event room under it. It is local and unsynced: in co-op each player plays their own. The flow is:

1. Divine cells until the divinations run out.
2. The items cleared completely come as a `rewards` screen pushed on top.
3. Back on this screen, its own `proceed` opens the map.

The sphere stays on the overlay stack under the map until the next room.

#### `state`

Keys in this order. When the minigame cannot be read (its private `_entity` field is missing), `state` is `{}`.

| Key | Type | Meaning |
| --- | --- | --- |
| `divinations_left` | int | Divinations not yet spent (`DivinationCount`). |
| `tool` | string | `"big: clears the 3x3 around the chosen cell"` or `"small: clears just the chosen cell"`. Big is the default. |
| `grid` | array of strings | One string per row, top row first, with one character per column, left to right. `#` is fog, `.` is cleared and empty, `R` relic, `P` potion, `C` card reward, `G` gold, `X` curse, `?` an item kind the reader does not know. A letter is a cleared cell of that item. The code comments say the grid is 11x11; the size is read from the game. |
| `legend` | string | Always `"grid rows run top to bottom and columns left to right, both from 0; # fog, . nothing, R relic, P potion, C card reward, G gold, X curse (a Doubt; bad). Letters are item parts already cleared; an item is yours once all its cells are clear"`. |
| `uncovered` | array of strings | The kind of every item whose cells are all clear, in the game's item order: `"relic"`, `"potion"`, `"card reward"`, `"gold"`, `"curse"`, or the item's class name for an unknown kind. Repeats are possible. |

There is no `run`, `player` or `potions` key, and no potion actions on this screen.

#### Actions

Exactly one of these sets is offered:

- **The sphere's proceed button is usable** (alive, shown and enabled): only `proceed`, labelled `Leave the Crystal Sphere for the map`. It clicks the proceed button, and the tracker waits (`leaving the crystal sphere`, up to 10 s) for the map to open.
- **Otherwise, with divinations left:**
  - `cell:<x>,<y>` for every cell still under fog (and shown), ordered by row, then column. `x` is the column and `y` the row, both from 0 at the top left. Label: `Divine at column <x>, row <y>`. It clicks the cell and spends one divination there with the current tool: big clears the 3x3 around it, clipped to the grid, and small clears that cell. The tracker waits (`divining at column <x>, row <y>`, at least 500 ms, up to 4 s) until the divination is spent and every cell of that area is clear, or the screen leaves the top. Cleared cells are never offered, because a click on one still spends a divination.
  - Then one tool switch, to the tool not in use, if its button is usable: `tool:small` (`Switch to the small tool (clears one cell)`) while big is selected, or `tool:big` (`Switch to the big tool (clears the 3x3 around a cell)`) while small is. It takes effect at once, with no tracker. The tool already in use is never offered.

Refusals from the reader (backstop):

- `The Crystal Sphere is gone.`
- `The Crystal Sphere is still appearing.`
- `The proceed button is not enabled yet.`
- `Can't read the Crystal Sphere.`
- `That tool's button is not available.`
- `No divinations left.`
- `There is no cell at column <x>, row <y>.`
- `That cell is already clear; divining it would waste a divination.`
- `Unknown Crystal Sphere action '<id>'.` (also for a malformed `cell:` id)
- The click guard: `<what> is gone.` and `<what> was already clicked; waiting for the game to react.`, where `<what>` is `the cell at column <x>, row <y>`, `the small tool button`, `the big tool button` or `the proceed button`.

#### Waiting snapshots

- Busy, with `state: {}`: `crystal sphere closing`.
- With `state`:
  - `crystal sphere appearing` (the first 1 s)
  - `leaving the crystal sphere` (proceed was just clicked)
  - `can't read the Crystal Sphere; please play it by hand` (the minigame is unreadable)
  - `the Crystal Sphere is handing out its rewards` (no divinations left and proceed not up yet)
  - `no fog left to divine`

#### Left out on purpose

What lies under the fog, and which relic, potion or card reward an item is. The grid shows only what a human sees. The items' contents appear on the following `rewards` screen.

#### Example

After one big divination at column 5, row 5, which cleared a 1x2 gold item and the top half of a potion. The item layout and the divination count are made up.

```json
{
  "version": 530,
  "screen": "crystal_sphere",
  "awaiting_decision": true,
  "state": {
    "divinations_left": 4,
    "tool": "big: clears the 3x3 around the chosen cell",
    "grid": [
      "###########", "###########", "###########", "###########",
      "####...####",
      "####..G####",
      "####.PG####",
      "###########", "###########", "###########", "###########"
    ],
    "legend": "grid rows run top to bottom and columns left to right, both from 0; # fog, . nothing, R relic, P potion, C card reward, G gold, X curse (a Doubt; bad). Letters are item parts already cleared; an item is yours once all its cells are clear",
    "uncovered": [ "gold" ]
  },
  "actions": [
    { "id": "cell:0,0", "label": "Divine at column 0, row 0" },
    { "id": "cell:1,0", "label": "Divine at column 1, row 0" },
    { "id": "cell:10,10", "label": "Divine at column 10, row 10" },
    { "id": "tool:small", "label": "Switch to the small tool (clears one cell)" }
  ]
}
```

The list is trimmed here. The real one has all 112 fogged cells, row by row, before `tool:small`.

### Popups (`"screen": "modal"`)

#### When the bot sees it

Whenever `NModalContainer` has an open modal. It outranks every other screen except the feedback and inspect screens, so it can appear in a fight, on the map, in a shop or at the main menu. There are two kinds:

- **Tutorial** (`kind: "tutorial"`): the popup is an `NFtue` or holds an `NFtueConfirmButton`, and its class name is not on the deny list. These show the first time on a profile: the merchant's tutorial `NMerchantFtue`, the three-page combat rules `NCombatRulesFtue`, and the like.
- **Everything else** (`kind: "other"`): error popups, the mod-loading consent prompt (its Yes saves and quits the game), the abandon-run confirmation, the "enable tutorials?" choice, and anything unknown. A popup whose class name contains `ModLoading`, `ModConsent`, `Error`, `Abandon`, `Quit`, `Disconnect` or `AcceptTutorials` (case-insensitive) is never clicked, whatever buttons it holds. These are left to Mark.

Tutorial tips drawn inside the shop room, rather than as a modal, are the shop snapshot's own `dismiss`, not this screen.

#### `state`

| Key | Type | Present | Meaning |
| --- | --- | --- | --- |
| `popup` | string | always | The popup's class name, e.g. `"NMerchantFtue"`. |
| `kind` | string | always | `"tutorial"` or `"other"`. |
| `text` | string | after the first 400 ms, when non-empty | Every visible `Label` and `RichTextLabel` in the popup, in tree order, without repeats, joined with ` / `, cut to 300 characters (297 plus `...`). |

#### Actions

Only `dismiss` (label `Dismiss the tutorial popup`), and only for a tutorial whose confirm button is usable. It clicks the confirm button, which is one of:

- an `NFtueConfirmButton`
- in an `NFtue`, the button named `FtueConfirmButton`
- in the combat rules tutorial, its next-page arrow (`RightArrow`, which closes the tutorial on the last page)

A shown button is preferred to a hidden one. The tracker waits (`dismissing the tutorial popup`, up to 4 s) until one of these happens: the popup closes, it is no longer the open modal, its button goes away or disables, or its text changes (a new page). A multi-page tutorial takes one `dismiss` per page. If two clicks on the same page (same text) change nothing, the popup is handed to Mark.

Refusals from the reader (backstop):

- `Unknown popup action '<id>'.`
- `The popup is gone.`
- `<class name> is for Mark to answer.`
- `The popup is still appearing.`
- `Dismissing this popup did nothing; Mark needs to close it.`
- `Can't find the popup's confirm button.`
- `The confirm button is not enabled yet.`
- The click guard: `the tutorial popup's confirm button was already clicked; waiting for the game to react.`

#### Waiting snapshots

- Busy, with `state: {}`: `popup closing`.
- With `state`:
  - `tutorial popup appearing`: the first 400 ms, up to 2 s while no confirm button is found, or up to 5 s while the button is not clickable.
  - `dismissing the tutorial popup`: just clicked.
  - `a popup needs Mark: <class name>`: any non-tutorial popup. A stuck tutorial gets the same text plus one of these suffixes: ` (dismissing it did nothing)`, ` (no confirm button found)`, ` (its confirm button is not clickable)`.

#### Example

The text is made up.

```json
{
  "version": 88,
  "screen": "modal",
  "awaiting_decision": true,
  "state": {
    "popup": "NMerchantFtue",
    "kind": "tutorial",
    "text": "The Merchant / Spend gold here on cards, relics and potions, or pay to remove a card from your deck. / Got it"
  },
  "actions": [ { "id": "dismiss", "label": "Dismiss the tutorial popup" } ]
}
```

A non-tutorial popup reads `"awaiting_decision": false`, `"waiting": "a popup needs Mark: NErrorPopup"`, `"state": {"popup": "NErrorPopup", "kind": "other", "text": "..."}`, `"actions": []`.

#### Co-op

Nothing differs in the code. Each game instance shows its own popups.

### Game over (`"screen": "game_over"`)

#### When the bot sees it

When `NGameOverScreen` is the top of the overlay stack: after a death (in any fight, event fights included), an abandon, or a won run. It is also read when the run has already stopped counting as in progress while the screen is still up; the router checks for it before falling back to the menu. The screen exists so the bot can write its post-mortem before leaving.

#### `state`

Keys in this order:

| Key | Type | Present | Meaning |
| --- | --- | --- | --- |
| `result` | string | always | `"victory"` when the current room is the victory room, else `"abandoned"` when the run was abandoned, else `"defeat"` when the run state is readable, else `"unknown"`. |
| `run` | object | run state readable | Act, floor, ascension where the run ended. |
| `room` | string | run state readable with a room | The room's type: `Monster`, `Elite`, `Boss`, `Event`, `Treasure`, `Shop`, `RestSite`, `Map`. |
| `score` | int | run state readable | `ScoreUtility.CalculateScore(run, victory)`, the number the screen shows. Left out if it throws. |
| `player` | object | local player readable | `PlayerRun` with the deck: `character`, `hp`, `max_hp`, `gold`, `relics`, `potions` (names or null, kept here), `deck_size`, `deck`. |

The run state is read only while the run counts as in progress. Once the game has torn it down, only `result` is left (`"unknown"` or `"abandoned"`).

#### Actions

Only `continue`: one click on whichever of the screen's buttons is shown and enabled. The screen has stages: continue (to the run summary), then on a daily run the leaderboard button, then return to the main menu. So the bot presses `continue` two or three times.

- The label is `Return to the main menu` when the button is the `NReturnToMainMenuButton`, and `Continue to the next page of the game over screen` otherwise.
- The tracker waits (`leaving the game over screen`, up to 10 s) until the screen or that button is gone, hidden or disabled, the screen is no longer on top, the run is torn down, or a modal opens.
- After the last press the bot is on `menu`.
- There are no potion actions.

Refusals from the reader (backstop):

- `Unknown game over action '<id>'.`
- `The game over screen is gone.`
- `The game over screen is still appearing.`
- `There is no continue button.`
- `The continue button is not enabled yet.`
- The click guard: `the continue button was already clicked; waiting for the game to react.`

#### Waiting snapshots

- Busy, with `state: {}`: `game over screen closing`.
- With `state`:
  - `game over screen appearing` (the first 800 ms)
  - `leaving the game over screen` (just clicked)
  - `waiting for the continue button to enable` (found, but hidden or disabled)
  - `waiting for the continue button` (none found yet)

#### Example

The score is made up.

```json
{
  "version": 1904,
  "screen": "game_over",
  "awaiting_decision": true,
  "state": {
    "result": "defeat",
    "run": { "act": 1, "floor": 9, "ascension": 0 },
    "room": "Elite",
    "score": 96,
    "player": {
      "character": "The Ironclad", "hp": 0, "max_hp": 80, "gold": 61,
      "relics": [ "Burning Blood", "Precise Scissors" ],
      "potions": [ null, null, null ],
      "deck_size": 13,
      "deck": { "Bash": 1, "Defend": 4, "Headbutt": 1, "Inflame": 1, "Pommel Strike": 1, "Strike": 4, "Twin Strike": 1 }
    }
  },
  "actions": [ { "id": "continue", "label": "Continue to the next page of the game over screen" } ]
}
```

#### Co-op

Nothing differs in the code.

### Menu (`"screen": "menu"`)

This covers everything outside a run: the main menu, its submenus, character select, or a run being set up from the main menu. Starting or continuing a run is Mark's call, so the menu is never a decision and the bot never starts a run.

- `awaiting_decision` is false and `actions` is `[]`.
- `state` is `{"menu": <the screen context's class name, the Godot node type of the current screen>}`, or `{"menu": "main menu"}` when there is none.
- `waiting` is one of:
  - `in character select; start the run and I'll take over`
  - `a run is starting; if nothing happens, start or continue a run and I'll take over` (a run counts as in progress but there is no run scene, which a failed Continue also leaves behind)
  - `on the main menu; start or continue a run and I'll take over`
- The tools refuse any id with `Not your decision right now (<waiting>). Call wait_for_decision.` The reader's own refusal, never reached that way, is `Nothing to do on the menu; Mark starts the run.`
- If the run is already flagged over while its game over screen is up, the menu reader hands off to `game_over`.

```json
{"version": 1, "screen": "menu", "awaiting_decision": false, "waiting": "in character select; start the run and I'll take over", "state": {"menu": "NCharacterSelectScreen"}, "actions": []}
```

### What an act 1 run in the arena has to match here

- Neow on floor 1 is an `event` with three relic options. If it uses the Ancient layout, as the code implies, it also has `ancient: true` and `Take <relic>` labels. After the pick comes whatever screen the relic opens (`card_select`, `card_reward`, `rewards`), then the finished page and `Leave`.
- Leaving an event is always an `option:<n>` labelled `Leave`, never `proceed`.
- Locked and deadly options are listed but never offered, so the bot never takes an event choice the game flags as fatal. It can still die in a fight an event starts.
- A page where every option is locked, deadly or special waits for a human. An arena that cannot hand it to one should not generate such a page.
- Option indices restart on every page, and the version changes with the page.
- On the event screen, potion ids follow the option ids only while some option is offered. The Crystal Sphere, popups, game over and the menu never offer potions.
- The Crystal Sphere never offers a cleared cell or the tool already in use, and it offers only `proceed` once its proceed button is up.
- In act 1, `game_over` can only be `defeat` or `abandoned`. Beating the act 1 boss leads to act 2, not to a game over screen.
- Tutorial popups depend on the profile. A profile that has seen them shows none.

## Open questions for the Mac

The readers could not settle these from the code alone. Most need a look at the decompiled game or a live snapshot. Until one is answered, the arena should follow what this page says.

**Names and text**

1. **Character title everywhere.** The readers' examples disagreed between `"Ironclad"` and `"The Ironclad"`. The code writes `Character.Title` for `player.character`, `get_deck`'s `character` and the `team` lines, and the creature name for `state.me.name`. Both are taken here to be `"The Ironclad"`, as `docs/arena.md` and `docs/chat-and-persona.md` say. A code comment on `Describe.TeamHp` gives `"Silent 41/70 hp"` without the article. Please confirm from a live snapshot that `team` lines read `"The Silent 41/70 hp"`. (`chat.you_are` drops the "The" on purpose.)
2. **Neow's page text.** Neow's first page has no model description, so the mod falls back to the longest visible label in the room's layout. What should the arena put in `state.text` for it: that label's real wording, or nothing (leave `text` out)?
3. **Neow's layout.** The code implies Neow uses `NAncientEventLayout`, which gives `ancient: true` and `Take <relic>` labels. Please confirm.
4. **The finished page's Leave option.** Its `title` is the game's own text for the room's proceed option; code comments say it reads `"Leave"`. Please confirm the wording, and whether that option ever carries a `text` of its own.

**Map and run layout**

5. **Act 1 rows.** R = 16 (row 1 always Monster, row 9 always Treasure, row 15 always Rest Site, the boss on row 16 at floor 17) comes from `strategy/wiki/run/act1.md`, not from `MapReader`. Please check it against the game's map generator before the arena generates maps.
6. **Free travel and quest markers.** That Winged Boots (solo only) and Flight give free travel, and that Spoils Map and Fur Coat add `(quest)` markers, comes from code comments. It matters only if the arena hands out those relics.

**Rewards, treasure and shop numbers (from the wiki, not the readers)**

7. **Reward contents.** Hallway 10 to 20 gold, elite 35 to 45 gold plus a relic, boss 100 gold and a card reward of 3 Rares with no relic. Please confirm against the game's reward code.
8. **Reward order.** `rewards[]` follows the screen's tree order. The examples show gold, potion, relic, card. Is that the game's fixed order?
9. **The chest's gold.** The wiki says an Act 1 chest holds 42 to 52 gold as well as a relic, and the reader says opening the chest can push a `rewards` screen ("the chest's extra rewards"). Does the Act 1 chest's gold come as a pushed `rewards` screen (a gold reward plus `proceed`) before the relics rise, or is it granted directly?
10. **Shop stock and prices.** 5 character cards (2 Attacks, 2 Skills, 1 Power that is never Common, one half price), 2 colorless (1 Uncommon, 1 Rare), 3 relics, 3 different potions, removal at 75 gold plus 25 per removal already bought, prices within 5% of the stated bases. Please confirm against the merchant code.
11. **`affordable`.** It is the game's `EnoughGold`. Is that always `gold >= price` in a plain run, with no relic or ascension rule that changes it?
12. **Shop stock as a string.** `stock` can be `"unknown until the inventory is opened"` only while the panel has no stock model. In v0.107.1 the room attaches it on `_Ready`, so the arena can always emit the object. Please confirm.

**Card selection**

13. **Cook's grid kind.** Cook opens the generic deck grid, which is `kind: "remove"` when its prompt key is `TO_REMOVE` and `"select"` otherwise. Which one is it in v0.107.1? That decides its labels (`Pick <name> to remove` against `Select <name>`).
14. **Order of `selected`.** On grids, `selected` follows the game's HashSet order, not pick order. For a multi-pick such as Precarious Shears the two orders may differ. Should the arena use pick order, or work out the HashSet order?
15. **The arena's current card selection.** `Fight.CardSelectSnapshot` always writes `min` and `max` and covers only `choose`, `combat_pile_select` and `select`. The mod leaves `min` and `max` out when unknown (on `choose` they appear only when the factory call was seen within 30 s, and `prompt` is normally absent there), and a full run also needs `upgrade`, `transform`, `remove`, `enchant` and `bundle` with the preview step.

**Other screens**

16. **Game over score.** `score` is `ScoreUtility.CalculateScore(run, victory)`, left out if it throws. Should the arena compute it the game's way, or leave the key out?
17. **Crystal Sphere size.** Code comments say the grid is 11x11; the reader reads the size from the game. Please confirm, along with the starting number of divinations.
18. **Co-op treasure clashes.** How the game settles two players picking the same chest relic is not modelled by the reader. It matters only for a co-op arena.
