# STS2B: a Claude-powered Slay the Spire 2 bot

A personal-use mod that lets Claude (Opus, through Claude Code on a Max plan) play Slay the Spire 2, solo or as a co-op teammate. Not for distribution.

## How it fits together

```
Slay the Spire 2 (macOS)
  └─ SpireMind mod (C#, loaded from the game's mods folder)
       └─ loopback HTTP server on 127.0.0.1:17772
            ├─ /mcp     MCP endpoint (Streamable HTTP, JSON responses)
            ├─ /state   current snapshot, for curl debugging
            └─ /act     run one action, for curl debugging
Claude Code (signed in with the Max plan)
  └─ .mcp.json points at /mcp → tools: get_state, wait_for_decision, act
  └─ /play command + strategy/ notes = the brain
```

The mod turns whatever screen is up into a **snapshot**: the screen name, the facts needed to decide (hand, energy, enemies and their intents, map, rewards, shop stock...) and a list of **legal actions** with short ids. The model only ever picks one of those ids, so it can't click something that doesn't exist. Every snapshot has a version; acting on a stale version is rejected instead of misfiring.

`act` waits for the game to settle (animations, enemy turn) and returns the next decision, so a typical turn is one tool call per card.

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

## Ground rules

- Our own code only. Other STS2 AI mods are fine to learn from but nothing is copied from them.
- The game's decompiled source stays on the Mac (`~/Projects/sts2-decompiled`) and never goes in this repo.
