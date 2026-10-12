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
- `/effort`: shows the thinking effort, and the effort in elite and boss rooms; `/effort low`, `medium`, `high`, `xhigh` or `max` sets the normal one. Elite and boss rooms use high by default (or the normal effort when that is higher), since it wins more of the hard fights; `/effort elite medium` or `/effort boss max` sets a fight's own, `same` makes it follow the normal effort and `default` puts it back.
- `/fast`: shows whether the API's fast mode is on; `/fast on` makes the bot's answers come up to 2.5 times faster at twice the price ($8 input and $40 output per million tokens on Opus 5.5), and `/fast off` goes back. It works on Opus 5.5, Opus 5 and Opus 4.8, is off every time the game starts, and switches itself off with a line in the chat if the API turns it down. Each switch, on or off, also writes the prompt to the cache again once (up to about 50 cents a bot on Opus 5.5), so leave it on for a stretch rather than flipping it. Fast mode is a research preview, so a key may not have it: `scripts/dev-host.sh --brain --model opus --fast --max-requests 3` tells you without the game.
- `/pause` holds the bot so you can play a bit by hand, and `/resume` hands control back.

Without a key, or once the API credit, the Console spend limit or the mod's own monthly cap (`monthly_budget_usd` in `~/.config/spiremind/config.json`, $200 by default) runs out, it plays on your Claude subscription instead, as long as Claude Code is installed and signed in (run `claude` once in Terminal and log in). The chat says when it switches, and the next run tries the API again.

On the API, each copy keeps a transcript of every exchange in `~/.config/spiremind/transcripts/`, with Claude's own short summary of its thinking for each move, and one line per fight in `~/.config/spiremind/fights.jsonl`: HP before and after, turns, potions used and kept, effort and cost. Lessons the bot writes for the wiki or `strategy/learned.md` wait in `strategy/pending/` for you to check, and don't reach its later prompts until you move them onto the page yourself.

## Talking to the bot in game

The bot plays as its character and answers in that character's voice, taken from the game itself: its name, pronouns and some of its own lines (`get_persona`). Press **T** to open the chat box at the bottom left; the game's hotkeys stay quiet while you type. Enter sends and closes the box; Escape closes it; clicking away closes it and keeps what you typed for next time.

- Address the bot by its character's name: "Ironclad, go for Strength", "hey Silent", "@Defect". With one bot, a message without a name reaches it too. A message that starts with another character's name is left for that bot. At character select it answers to the character you just picked; with nothing picked (or Random), and on the other menus, every message reaches it.
- It sees your message on its next tool call, answers in a line or two and follows orders. Standing orders go under Orders in `runs/current.md` and last the whole run.
- Its lines show in the chat log and as the game's own speech bubbles: over its character in a fight, beside its figure or the top-bar portrait elsewhere. Off combat it waits a moment after speaking before it moves on, so you can read the line. While it is thinking over a move in combat, a thought cloud shows over it.
- Commands take effect at once in the mod, without waiting for the model:
  - `/pause` stops the bot so you can play by hand. Chat keeps working.
  - `/resume` hands control back.
  - `/start`, `/stop`, `/budget`, `/model`, `/effort` and `/fast` drive the in-game brain (above).
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

You host a local co-op lobby in your own copy of the game, and each bot is a second copy on the same Mac that joins it, started from your chat box. You talk to every bot from your own window: your lines and the commands reach them, and their lines come back into your chat log and as speech bubbles over their players, with a thought cloud while a bot decides. `docs/coop.md` walks through the first live test and what the bot sees; `docs/chat.md` (Co-op) has the relay's details. A friend on the same network can join the lobby from their own Mac as a guest: `docs/coop-guest.md` and `scripts/package-guest.sh`.

- Once: Steam's launch options for the game get `--fastmp host_standard`, so your game opens a local lobby at every main menu. Both copies must load the same gameplay mods.
- In your window:
  - `/bot add [Ironclad|Silent|Defect|Regent|Necrobinder|Random]` starts a bot copy (up to three, ports 17773 to 17775). It joins your lobby by itself, picks its character, readies, and starts playing once the run is in progress. When your copy opens a lobby, the bots are asked to look for it at once.
  - `/bots` lists the bots, one line each, with where each stands ("Silent on 17773 (NetId 1000, playing on API credit): joined; picked Silent; ready; waiting for the host to start.").
  - `/bot stop [name]` asks one bot to leave and quit, or every bot without a name. A bot goes by its nameplate on your screen ("Ironclad 2" for a second Ironclad), else its character.
  - `/party [on|off]` sets whether bots hear each other's lines.
  - `/plan` starts a planning session now, `/plan skip` ends one, `/plan show` shows the party plan, and `/plan on` or `/plan off` turns the automatic sessions on or off.
  - `/start`, `/stop`, `/pause`, `/resume`, `/budget`, `/model`, `/effort` and `/fast` go to every bot, each answer labelled with the bot's name; put a name first to send to one bot (`/model Silent sonnet`, "Ironclad 2, block this turn"). `/fast on` isn't saved, so a bot added later needs it typed again.
- Planning sessions: at the start of each act (while the Ancient's offer is up), around the middle and before the boss, the bots hold their picks for a minute or two and agree on lanes, so two decks don't chase the same cards. Say what you're building in the chat while one runs. The plan is saved in `runs/party/plan.md`, and each bot checks its part before every card, relic and shop pick. `docs/brain.md` (Planning sessions) has the details.
- In a fight a bot notices when you act during its turn: if you give it Block or kill its target, it stops and plans the rest of its turn again instead of finishing a stale plan.
- Your copy keeps port 17772 and never plays your hand in a co-op lobby or run; outside co-op it plays as in solo, even with a bot copy still running. A copy that runs with Steam is always your copy, even with `--sts2bot=1` on its command line.
- A bot copy is the game started without Steam, with `--force-steam off --clientId 1000 --fastmp join --sts2bot=1` and `SPIREMIND_PORT=17773` (1001 and 17774, 1002 and 17775 for more bots). `/bot add` does that for you; `scripts/coop-bot.sh` still starts one from Terminal.
- `SPIREMIND_BOT_PORTS` (such as `17773,17774`) sets the ports your copy looks for bots on; the default is 17773 to 17775. Only localhost is ever contacted.
- `SPIREMIND_CONFIG_DIR` picks a copy's config folder. Unset, your copy uses `~/.config/spiremind` and a bot copy `~/.config/spiremind/bot-<port>`, with its own config.json, transcripts, fights.jsonl and autostart. Every copy uses the shared `~/.config/spiremind/api_key` unless its folder has its own, and all of them count against one monthly cap in the shared spend.json.
- Each bot keeps its own run journal in `runs/bot-<port>/`; `strategy/learned.md` and the wiki stay shared, and a write that would wipe another bot's newer change is refused.
- Without the game: `scripts/dev-host.sh 17773 --bot --character Silent --echo` runs a fake bot copy, and `scripts/dev-host.sh --relay --bot-ports 17773` a fake human copy you can type into.

## Ground rules

- Our own code only. Other STS2 AI mods are fine to learn from but nothing is copied from them.
- The game's decompiled source stays on the Mac (`~/Projects/sts2-decompiled`) and never goes in this repo.
