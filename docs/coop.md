# Playing co-op with the bot (first test)

You host a co-op run in your own game. A second copy of the game runs on the same Mac, without Steam, and joins as the bot's player. `scripts/coop-bot.sh` checks the setup, makes the bot's profile, starts that copy and says whether it got into your lobby.

The mod has never run in co-op. This page is the plan for the first hands-on test. "Expected" below means read from the game's code (v0.107.1), not seen in a run.

## What works today, and what does not

Checked without the game: the launcher's checks, the profile it writes, the command it starts, how it tells a join that worked from one that failed, and that a second run changes nothing. All of it was tried against a stand-in for the game. The game itself was never started for this.

Built for co-op but never tried in it:
- The join itself, and the bot playing its own hand through the game's synced actions.
- Map votes, shared event votes, its own rewards, shop, rest site and treasure pick.
- The API brain reading `strategy/coop.md` in a co-op run.

Does not work yet:
- The bot can't pick a character or start the run. You do both in its window.
- No chat from your own window. Your chat box talks to the mod in your own copy, where no brain runs. `/start` typed there makes the bot play your hand. To talk to the bot, use its window or the `curl` lines below.
- The bot's speech bubbles and thought cloud show only in its own window.
- Both copies share one settings file, one spend file and one journal: `~/.config/spiremind/config.json`, `spend.json` and `runs/current.md`. `/start` is kept in that settings file until `/stop`. The co-op run takes over `runs/current.md`; the journal that was there is archived under `runs/` (expected).
- Mods keep their own files in the game's data folder, and both copies use the same ones (for example `betterspire2_*.json`).
- Mend at a rest site is never offered to the bot. A human has to choose it.
- A friend can't join over Steam. This lobby is local to the Mac, not a Steam lobby.
- The bot's profile is new: only the Ironclad is unlocked.
- A bot copy that crashes, is killed or is quit while connected can't rejoin the run.

Your own side of the run is a normal co-op run in your modded profile: it goes into your run history, stats and unlocks (expected).

## Before the test (game closed)

1. Run `scripts/coop-bot.sh --dry-run`. It only reads. Fix every `STOP` it prints.
   - Mods: both copies must load the same gameplay mods, and the same mods that add network message types. The bot's copy never sees Workshop mods. For each mod in the way the script names two ways out: one local copy in the game's mods folder, or switching the mod off in your game for the session. The choice is yours.
     - A local copy: you keep the mod while you play. Not known: whether BaseLib and BetterSpire2 work in a copy without Steam. BaseLib's code refers to Steam.
     - Switched off: the bot's copy loads only SpireMind, so less can go wrong in a first test. You play that session without the mod.
   - Autostart must be off. If it is on, the brain also starts in your own window.
2. Run `scripts/coop-bot.sh --prepare-only`. It writes the bot's profile, `~/Library/Application Support/SlayTheSpire2/default/1000/settings.save`, from your own settings: a small window, muted, mods agreed to, no first-run popups. Your own files are only read.

State of this Mac on 2026-10-10: the game's mods folder already holds local copies of BaseLib and BetterSpire2, the same files as your Workshop copies. They appeared there at 14:28 that day. The launcher did not put them there; it never copies mods. With them the mods check passes. They also change your normal game: it loads these copies in place of the Workshop ones, and they do not update by themselves. If you did not choose that, delete the two folders (`SlayTheSpire2.app/Contents/MacOS/mods/BaseLib` and `.../BetterSpire2`, never `SpireMind`). The dry run then stops and shows both ways out again.

## Set your game to host locally

In Steam: Library, right-click Slay the Spire 2, Properties, General, Launch Options. Type exactly:

```
--fastmp host_standard
```

If the box already holds something ending in `%command%`, put it after that: `... %command% --fastmp host_standard`. Close the window. Do this while the game is closed. While it is set, the game opens a local co-op lobby every time its main menu loads.

To remove it afterwards: the same box, delete that text.

## The test, step by step

1. Start your game from Steam.
   - Expected: no logo, then character select with you as the only player, named "Test Host".
   - macOS may ask whether the game may accept network connections. Either answer should do for a bot on the same Mac.
2. In Terminal, in the repo: `scripts/coop-bot.sh`
   - It checks that your lobby is open (something listens on UDP port 33771). If not, it stops and starts nothing.
   - Then: "Started the bot's copy", dots, "The bridge answers", dots, "It is in your lobby", and what to do next.
   - A second game window opens, with no sound, titled "Slay The Spire 2 (Client)". It joins by itself as soon as it reaches its main menu, about 5 seconds after it opens. Your lobby shows a second player, "Test Client 1".
   - The window is 1280x720 pixels. On this Retina screen that is small, under half the screen's width (expected). Drag a corner to make it bigger.
   - Your game is fullscreen, so reach the other window with Cmd+Tab (two entries with the same name) or Mission Control.
3. In the bot's window: pick the Ironclad, then press the button that starts a solo run. The first time it asks about tutorials: answer No (the cancel button).
4. In your window: pick your character and press the same button. The run starts in both windows when both have pressed it.
5. Start the bot, once the run has started. Either way works:
   - In the bot's window: press T, type `/start`, press Enter.
   - From Terminal: `curl -s http://127.0.0.1:17773/chat -d '{"from":"Mark","text":"/start"}'`
   - A chat line says who plays and on which credit. The bot starts to act. `/budget` shows the spend.
   - Never type `/start` in your own window.
6. Play. A fight's round ends when both of you have ended the turn. The map is a vote.
   - To tell the bot something: the same `curl` line with your words as the text, or T in its window. Its answers: `curl -s 'http://127.0.0.1:17773/chat?since=0'`.
   - To take its seat for a moment: `/pause` the same way, play in its window, then `/resume`.
7. To end:
   - `/stop` for the bot (its window, or the `curl` line with `/stop`).
   - In your window: Esc, then Save and Quit to keep the run, or Give Up to end it. Give Up leaves nothing behind but a loss in your history (expected).
   - Expected after either: your game goes back to its main menu and at once opens a new lobby (the launch option does that). The bot's copy is thrown out and tries to join the new lobby by itself.
   - To stop for the day: quit your own game first. Then Cmd+Q in the bot's window.
   - For another run: stay in the new lobby. If the bot's window shows an error and not character select, Cmd+Q there and run the script again. `/start` again once the run has started.

Don't run `/play` in Claude Code during the test. It talks to port 17772, which is your own window.

## What to watch in the logs

The bot's copy: `~/sts2bot/godot-1000.log`. The log of the try before it is kept as `godot-1000.log.prev`. If the `.log` file stays empty, the game ignored `--log-file`: read `godot-1000.log.out`, which holds the same lines.

```
grep -n "Command Line Args\|Steam initialization skipped\|RUNNING MODDED\|SpireMind\]\|Beginning join\|initial game info\|JoinResponse\|ismatch\|timed out\|handshake\|onnection failed" ~/sts2bot/godot-1000.log
```

- `Command Line Args:` shows the flags the game received.
- `Steam initialization skipped` and ` --- RUNNING MODDED! --- Loaded N mods`: no Steam, mods on.
- `[SpireMind] bridge listening on http://127.0.0.1:17773/`: the bot's bridge.
- `Beginning join with initializer`, then `Got initial game info message. Version: ...`: it reached your lobby.
- `Received ClientLobbyJoinResponseMessage`: it is in.
- After `/start`: `[SpireMind] brain: /start by ...`, and with the API brain a "system prompt built" line that should list `coop.md`.

Your own game: `~/Library/Application Support/SlayTheSpire2/logs/godot.log`

- `Command Line Args:` must show `--fastmp host_standard`. If it does not, the launch option did not reach the game.
- `Failed to create host!` must not appear. It means UDP port 33771 was taken, and nobody can join that lobby.
- `[SpireMind] bridge listening on http://127.0.0.1:17772/` is your copy's bridge. No `brain: /start` line should ever appear here.

What the bot sees right now: `curl -s http://127.0.0.1:17773/state`

## When the join fails

Only the bot's copy checks. It shows an error popup, and the reason is in its log. The script reads that log and prints the lines. Your own window only shows a player coming and going. One popup shows at a time, so trust the log over the popup.

To try again: Cmd+Q in the bot's window, fix the cause, run the script again. A copy that never got in leaves no ghost (expected).

What the log line means:

- `Connection timed out!` Nobody listens on UDP 33771. You were not in the co-op character select, or the launch option is missing.
- `Received non-success code during handshake (IdCollision)!` A ghost of an earlier bot copy holds that client id. In your game, back out of the lobby and host again.
- `Version mismatch. Host: ... Ours: ...` The game updated between the two starts. Quit both and start again.
- `Mismatch in gameplay-relevant mods with the host!`, with both lists. Close both copies, run `scripts/coop-bot.sh --dry-run` and follow it. Mods change only when a game starts.
- `ModelDb hash mismatch` A mod on one side adds cards, relics or the like. Make that mod the same on both sides.
- `Received initial join message with failure: RunInProgress` The run had started. A bot can only join in character select.
- `Received initial join message with failure: NotInSaveGame` You loaded a saved run that does not know this client id. Use the id the run began with.
- The log ends at `awaiting initial game info message` and the bot's window hangs: a mod that adds network message types (BaseLib) is loaded on one side only.

## Most likely to go wrong, in this order

1. Your game shows its normal main menu, not a lobby. The launch option did not arrive: a typo, or the game was running when you set it. Quit, check the box, start again. The script will not start the bot until the lobby is open. If the option still does not arrive, stop there and note it. If you sit in the co-op character select and the script still sees no lobby, the script is wrong: `--no-host-check` starts the bot anyway. Note that too.
2. You can't find the bot's window. It opens small on the desktop, not in your game's fullscreen space. Use Mission Control.
3. The script says "No answer from the bridge". The bot's copy runs, but SpireMind did not load in it. Look at its window. A popup about loading mods means the seeded settings were not taken. Pick its cancel button. The other one saves and quits, which leaves a ghost if the copy is in your lobby. Leave the lobby with Back, quit that copy, run the script with `--reset-profile`. If the popup comes back, stop and note what the bot's log says about `settings.save`. With no popup: search its log for `Skipping loading mod` and for errors after `Loading assembly DLL`.
4. BaseLib or BetterSpire2 misbehave in a copy without Steam: errors in the bot's log while mods load, or a refused join. The way out is the other option: switch that mod off in your game for the session, delete its local copy, restart both.
5. T does not open the chat in the bot's window. Click inside the window first. Or use the `curl` line.
6. `/start` answers that it could not start, or the bot plays on the subscription. The chat line says why; the bot's log has `[SpireMind] brain:` lines.
7. The bot stops at a screen it has never seen in co-op: a vote, a shared event, the treasure room, a rest site. `/pause`, do its choice by hand in its window, `/resume`, and note the screen.
8. The two windows disagree, or a desync message shows. Note it and end the run.

## Rules that save the run

- Never quit, kill or close the bot's copy while it is in your lobby or run. Cmd+Q counts. It stays as a ghost. Then you must back out of the lobby and host again. To take it out cleanly: Back in its character select, or Esc, Disconnect in a run. Or quit your own game first.
- While the bot is started, do not restart your own game. It would read the saved `/start` and play your hand as well. `/stop` first.
- Never press Load in the Multiplayer menu without `--fastmp host` in the launch options, and never use `--fastmp load`. The co-op save is renamed to `.corrupt`.
- `--fastmp host_standard` always opens a new lobby. To go on with a saved co-op run, change the option to `--fastmp host`, press Load, and start the bot with the same `--client-id`.
- `scripts/coop-bot.sh` is safe to run twice. It refuses a second copy with the same client id or port.

## Back to normal play

1. `/stop` for the bot. Check: `scripts/coop-bot.sh --dry-run` says "Brain autostart is off".
2. Quit both copies.
3. Remove the Steam launch option. While it is set, your game opens a lobby at every main menu, and your Host and Join buttons only do local lobbies.
4. A co-op run you saved in the test: without the launch option, the Multiplayer menu shows Load and Abandon in place of Host. Abandon clears it. So does Load, with an error. Either way the save is renamed to `.corrupt` and nothing else is touched (expected). To play it on instead, see the rules above.
5. Mods: tick again what you switched off. If local copies of mods are in the game's mods folder, delete those folders to get Workshop updates again. Keep `SpireMind`.
6. These can stay: the bot's profile (`default/1000`, apart from your own `steam/` folder) and its logs in `~/sts2bot`.

## What to note down for the next round

- Did `--fastmp host_standard` reach the game (your log), and did the script see the lobby ("A local lobby is open")?
- Did macOS ask about network connections?
- Did the bot's window open as a small window, with no popup before the join? How big?
- Did `~/sts2bot/godot-1000.log` fill up, or only the `.out` file?
- Seconds from starting the script to "The bridge answers", and to "It is in your lobby". Did the script see the join?
- The names shown in the lobby.
- Did the join pass the first time? If not: the popup text and the log line.
- Did the tutorial question show, and did No start the run?
- Did `/start` work from the window, and from `curl`?
- After `/start`: which credit it played on, and whether "system prompt built" lists `coop.md`.
- Did anything play your hand? It must not.
- Where the bot got stuck or waited: end of turn, map vote, shared events, treasure, rest site (Mend), rewards.
- Did the two games drift apart (a desync message, different HP or cards in the two windows)?
- Does the bot's window keep up while it is hidden behind your game?
- What closing did: Save and Quit or Give Up on your side, and what the bot's window did then. Did a ghost stay anywhere?
- Did the run show up in your own history and stats as you expected?
- Did BetterSpire2's layout or settings change after the bot's copy ran?
- `/budget` at the end.
