# STS2B: a Claude-powered Slay the Spire 2 bot

A personal-use mod that lets Claude play Slay the Spire 2, solo or as a co-op teammate. It can play by itself from inside the game, on the Claude API with Claude Code on the Max plan as the fallback, or be driven by `/play` in Claude Code. Not for distribution.

## How it fits together

```
Slay the Spire 2 (macOS)
  └─ SpireMind mod (C#, loaded from the game's mods folder)
       ├─ chat box (T) where you talk to the bot and type commands (/start, /pause...)
       ├─ in-game brain: calls the Claude API itself, or runs Claude Code headless as the fallback
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
| `src/SpireMind.Core` | Game-agnostic bridge: HTTP server, MCP endpoint, snapshot versioning, decision waiter, chat, the in-game brain, a fake game for tests |
| `src/SpireMind` | The mod itself: entry point and per-screen readers against the game's assemblies |
| `src/SpireMind.DevHost` | Runs the bridge on a fake fight so the Claude Code side can be tried without the game, or lets the API brain play it as a smoke test |
| `tests/SpireMind.Core.Tests` | Unit and HTTP tests for the bridge |
| `strategy/` | What the bot reads before playing: general rules, the verified rules sheet, per-character notes, co-op, lessons learned, and a wiki of enemy, card and event pages with facts from the game files and lessons from runs |
| `runs/` | Run journals the bot writes |
| `scripts/` | build, install, dev host, tests (macOS paths by default) |

## Setup on the Mac

Needs the .NET 9 SDK (`brew install dotnet@9`) since the game runs on .NET 9.

```bash
scripts/test.sh               # bridge tests, no game needed
scripts/dev-host.sh           # fake game on :17772; then in another terminal: claude, /play this fight
scripts/dev-host.sh --brain   # the API brain plays the fake fight with your key (see below)
scripts/install.sh            # build against the installed game and copy into its mods folder
curl -s http://127.0.0.1:17772/health
```

Paths default to the Steam install under `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2`. Override `GAME_DIR` or `MODS_DIR` in `scripts/local.env` if yours differ. A Steam update can replace the app bundle, which removes anything inside it, so rerun `scripts/install.sh` after updates.

## Letting the bot play by itself

The mod can play without a Claude Code window open: it calls the Claude API with your own key and pays from your API credit. `docs/brain.md` has the details.

Once, set up the key. Create an API key in the Claude Console, copy it, then in Terminal (paste the key between the quotes):

```bash
mkdir -p ~/.config/spiremind
printf '%s\n' 'PASTE-THE-KEY-HERE' > ~/.config/spiremind/api_key
chmod 600 ~/.config/spiremind/api_key
```

Never paste the key anywhere else: not in the game chat, config.json, a commit or a message to Claude. The mod reads it from that file and never logs or writes it. (Your shell may keep that printf line in its history. To avoid that, copy the key and run `printf '%s\n' "$(pbpaste)" > ~/.config/spiremind/api_key` instead.)

Before the first game, check the key and the API calls with `scripts/dev-host.sh --brain`: the brain plays a fake fight for a few requests (about a cent) and the script ends with PASSED or FAILED. `docs/brain.md` (Smoke test) says what the output should look like.

Then, in game, open the chat (T) and type:

- `/start`: the bot plays from wherever the run stands. It also starts by itself in later runs, until you type `/stop`.
- `/stop`: the bot stops: no more API calls, and Claude Code quits if it was playing.
- `/budget`: what the API has cost this month and this run, the model, and what is playing.
- `/model`: shows the model; `/model opus`, `sonnet`, `haiku` or `fable` switches it.
- `/effort`: shows the thinking effort; `/effort low`, `medium`, `high`, `xhigh` or `max` sets it.
- `/pause` holds the bot so you can play a bit by hand, and `/resume` hands control back.

Without a key, or once the API credit, the Console spend limit or the mod's own monthly cap (`monthly_budget_usd` in `~/.config/spiremind/config.json`, $200 by default) runs out, it plays on your Claude subscription instead, as long as Claude Code is installed and signed in (run `claude` once in Terminal and log in). The chat says when it switches, and the next run tries the API again.

## Talking to the bot in game

The bot plays as its character and answers in that character's voice, taken from the game itself: its name, pronouns and some of its own lines (`get_persona`). Press **T** to open the chat box at the bottom left; the game's hotkeys stay quiet while you type. Enter sends and closes the box; Escape closes it; clicking away closes it and keeps what you typed for next time.

- Address the bot by its character's name: "Ironclad, go for Strength", "hey Silent", "@Defect". With one bot, a message without a name reaches it too. A message that starts with another character's name is left for that bot. At character select it answers to the character you just picked; with nothing picked (or Random), and on the other menus, every message reaches it.
- It sees your message on its next tool call, answers in a line or two and follows orders. Standing orders go under Orders in `runs/current.md` and last the whole run.
- Its lines show in the chat log and as the game's own speech bubbles: over its character in a fight, beside its figure or the top-bar portrait elsewhere. Off combat it waits a moment after speaking before it moves on, so you can read the line. While it is thinking over a move in combat, a thought cloud shows over it.
- Commands take effect at once in the mod, without waiting for the model:
  - `/pause` stops the bot so you can play by hand. Chat keeps working.
  - `/resume` hands control back.
  - `/start`, `/stop`, `/budget`, `/model` and `/effort` drive the in-game brain (above).
  - `/help` lists the commands.
- `SPIREMIND_CHAT_KEY` picks another hotkey (a Godot `Key` name such as `Y`; Enter and Escape fall back to T). A key the game uses itself (a bound hotkey, F2, the dev console keys, V in co-op) will not open the chat; the log says so. `SPIREMIND_HUMAN_NAME` sets the name on your lines; it defaults to your OS user name, capitalised. Set them in the game's Steam launch options, e.g. `SPIREMIND_HUMAN_NAME=Mark %command%`.

The same chat works from a terminal, which helps when testing without the overlay:

```bash
curl -s http://127.0.0.1:17772/chat -d '{"from": "Mark", "text": "Ironclad, how are we doing?"}'
curl -s 'http://127.0.0.1:17772/chat?since=0'    # the log, the bot's name and the pause flag
```

`/state` and `/act` show the bot's unread messages under `chat` but leave them unread, so checking on the bot with curl doesn't take its messages.

See `docs/chat.md` for the design and `docs/chat-and-persona.md` for what the game offers.

## Co-op with bots

Mark hosts a local co-op lobby in his own copy of the game, and each bot is a second copy on the same Mac that joins it. You talk to every bot from your own window: your lines and the commands reach them, and their lines come back into your chat log and as speech bubbles over their players. `docs/chat.md` (Co-op) has the details.

- A bot copy is started with the game flags `--force-steam off --clientId 1000 --fastmp join --sts2bot=1` and the environment variable `SPIREMIND_PORT=17773` (1001 and 17774, 1002 and 17775 for more bots, up to three). `--sts2bot=1` makes it a bot copy: it starts playing by itself once the run is in progress. Your copy keeps port 17772 and never plays your hand while bots exist.
- In your window, `/bots` lists the bots, `/bot add [Ironclad|Silent|Defect|Regent|Necrobinder|Random]` starts one (once the game side's launcher is in), and `/party [on|off]` sets whether bots hear each other so they can plan builds together. `/start`, `/stop`, `/pause`, `/resume`, `/budget`, `/model` and `/effort` go to every bot, each answer labelled with the bot's character; put a name first to send to one bot (`/model Silent sonnet`).
- `SPIREMIND_BOT_PORTS` (such as `17773,17774`) sets the ports your copy looks for bots on; the default is 17773 to 17775. Only localhost is ever contacted.
- `SPIREMIND_CONFIG_DIR` picks a copy's config folder. Unset, your copy uses `~/.config/spiremind` and a bot copy `~/.config/spiremind/bot-<port>`, with its own config.json, transcripts and autostart. Every copy uses the shared `~/.config/spiremind/api_key` unless its folder has its own, and all of them count against one monthly cap in the shared spend.json.
- Each bot keeps its own run journal in `runs/bot-<port>/`; `strategy/learned.md` and the wiki stay shared, and a write that would wipe another bot's newer change is refused.
- Without the game: `scripts/dev-host.sh 17773 --bot --character Silent --echo` runs a fake bot copy, and `scripts/dev-host.sh --relay --bot-ports 17773` a fake human copy you can type into.

## Ground rules

- Our own code only. Other STS2 AI mods are fine to learn from but nothing is copied from them.
- The game's decompiled source stays on the Mac (`~/Projects/sts2-decompiled`) and never goes in this repo.
