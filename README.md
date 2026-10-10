# STS2B: a Claude-powered Slay the Spire 2 bot

A personal-use mod that lets Claude (Opus, through Claude Code on a Max plan) play Slay the Spire 2, solo or as a co-op teammate. Not for distribution.

## How it fits together

```
Slay the Spire 2 (macOS)
  └─ SpireMind mod (C#, loaded from the game's mods folder)
       ├─ chat box (T) where you talk to the bot, /pause and /resume
       └─ loopback HTTP server on 127.0.0.1:17772
            ├─ /mcp     MCP endpoint (Streamable HTTP, JSON responses)
            ├─ /state   current snapshot, for curl debugging
            ├─ /act     run one action, for curl debugging
            └─ /chat    post to or read the in-game chat, for curl debugging
Claude Code (signed in with the Max plan)
  └─ .mcp.json points at /mcp → tools: get_state, wait_for_decision, act, act_sequence, say, get_deck, get_persona
  └─ /play command + strategy/ notes = the brain
```

The mod turns whatever screen is up into a **snapshot**: the screen name, the facts needed to decide (hand, energy, enemies and their intents, map, rewards, shop stock...) and a list of **legal actions** with short ids. The model only ever picks one of those ids, so it can't click something that doesn't exist. Every snapshot has a version; acting on a stale version is rejected instead of misfiring.

`act` waits for the game to settle (animations, enemy turn) and returns the next decision. `act_sequence` plays several actions from one snapshot the same way, so a typical fight turn is one tool call. `say` puts a line in the in-game chat, `get_deck` returns the full deck with card and relic text for build decisions, and `get_persona` describes the character the bot plays.

## Layout

| Path | What |
| --- | --- |
| `src/SpireMind.Core` | Game-agnostic bridge: HTTP server, MCP endpoint, snapshot versioning, decision waiter, a fake game for tests |
| `src/SpireMind` | The mod itself: entry point and per-screen readers against the game's assemblies |
| `src/SpireMind.DevHost` | Runs the bridge on a fake fight so the Claude Code side can be tried without the game |
| `tests/SpireMind.Core.Tests` | Unit and HTTP tests for the bridge |
| `strategy/` | What the bot reads before playing: general rules, per-character notes, co-op, lessons learned |
| `runs/` | Run journals the bot writes |
| `scripts/` | build, install, dev host, tests (macOS paths by default) |

## Setup on the Mac

Needs the .NET 9 SDK (`brew install dotnet@9`) since the game runs on .NET 9.

```bash
scripts/test.sh            # bridge tests, no game needed
scripts/dev-host.sh        # fake game on :17772; then in another terminal: claude, /play this fight
scripts/install.sh         # build against the installed game and copy into its mods folder
curl -s http://127.0.0.1:17772/health
```

Paths default to the Steam install under `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2`. Override `GAME_DIR` or `MODS_DIR` in `scripts/local.env` if yours differ. A Steam update can replace the app bundle, which removes anything inside it, so rerun `scripts/install.sh` after updates.

## Talking to the bot in game

The bot plays as its character and answers in that character's voice, taken from the game itself: its name, pronouns and some of its own lines (`get_persona`). Press **T** to open the chat box at the bottom left; the game's hotkeys stay quiet while you type. Enter sends and closes the box; Escape closes it; clicking away closes it and keeps what you typed for next time.

- Address the bot by its character's name: "Ironclad, go for Strength", "hey Silent", "@Defect". With one bot, a message without a name reaches it too. A message that starts with another character's name is left for that bot. At character select it answers to the character you just picked; with nothing picked (or Random), and on the other menus, every message reaches it.
- It sees your message on its next tool call, answers in a line or two and follows orders. Standing orders go under Orders in `runs/current.md` and last the whole run.
- Its lines show in the chat log and as the game's own speech bubbles over its character. While it is thinking over a move in combat, a thought cloud shows over it.
- Commands take effect at once in the mod, without waiting for the model:
  - `/pause` stops the bot so you can play by hand. Chat keeps working.
  - `/resume` hands control back.
  - `/help` lists the commands.
- `SPIREMIND_CHAT_KEY` picks another hotkey (a Godot `Key` name such as `Y`; Enter and Escape fall back to T). `SPIREMIND_HUMAN_NAME` sets the name on your lines; it defaults to your OS user name, capitalised. Set them in the game's Steam launch options, e.g. `SPIREMIND_HUMAN_NAME=Mark %command%`.

The same chat works from a terminal, which helps when testing without the overlay:

```bash
curl -s http://127.0.0.1:17772/chat -d '{"from": "Mark", "text": "Ironclad, how are we doing?"}'
curl -s 'http://127.0.0.1:17772/chat?since=0'    # the log, the bot's name and the pause flag
```

`/state` and `/act` show the bot's unread messages under `chat` but leave them unread, so checking on the bot with curl doesn't take its messages.

See `docs/chat.md` for the design and `docs/chat-and-persona.md` for what the game offers.

## Ground rules

- Our own code only. Other STS2 AI mods are fine to learn from but nothing is copied from them.
- The game's decompiled source stays on the Mac (`~/Projects/sts2-decompiled`) and never goes in this repo.
