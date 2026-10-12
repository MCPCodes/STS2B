# Playing co-op with bots (first live test)

You host a local co-op lobby in your own game and type `/bot add silent` in your chat box. A second copy of the game starts on the same Mac without Steam, hidden and muted, joins your lobby, picks its character and readies up. You start the run, the bot plays its own hand, and you talk to it from your own window.

None of this has run in the game yet. It is built against v0.107.1 (0 warnings) and the Core tests pass. "Expected" below means read from the game's code, not seen in a run.

## What works today, and what does not

Built, never run live:
- `/bot add [Ironclad|Silent|Defect|Regent|Necrobinder|Random]` from your window: up to three copies (players 1000 to 1002, ports 17773 to 17775). `/bots` lists them.
- The copy's own profile (`~/Library/Application Support/SlayTheSpire2/default/<id>/`): a small muted window, mods agreed to, tutorials off, and your revealed epochs, so it can pick the characters you have unlocked. Your own files are only read, from the running game.
- The copy's window is minimised, muted and never takes the keyboard. Its settings, journal and transcripts are its own (`~/.config/spiremind/bot-<port>/`, `runs/bot-<port>/`); the API key, the monthly cap and `strategy/` are shared.
- It joins by itself and keeps trying, picks the asked character, readies, and readies again if anything takes that back. After a run it returns to its menu and joins your next lobby.
- Its brain starts by itself once the run is in progress. Your own copy's brain stays off while bots exist or the run is co-op.
- It follows your map, shared event and chest votes, can Mend you, and sees the team on every screen (last section).
- Your lines and `/start`, `/stop`, `/pause`, `/resume`, `/budget`, `/model`, `/effort`, `/fast` reach the bots. `/fast on` (answers up to 2.5 times faster at twice the price) only reaches the bots running when you type it and is never saved, so type it again after adding or relaunching a bot. Their lines come into your chat log and show as speech bubbles over their figures. Nameplates read your name and the bot's character.
- A bot that is stuck in a way only you can fix says so in your chat: "Silent: Can't join your lobby, and trying again won't fix it: ...".
- `/botstop` asks every copy your game started to leave the lobby or run and quit. Quitting your game does the same, and a copy also quits by itself when your game is gone.

Does not work yet:
- `/botstop` stops all copies, not one, and is not yet `/bot stop`. For one copy: `kill -TERM <pid>` (it leaves cleanly) or Quit on its Dock icon. A bot that left a run can't come back into it.
- No thought cloud over a bot on your screen.
- After a run a bot finds your next lobby by trying, which can take a minute or two.
- A Custom or Daily lobby: the bot joins but does not pick or ready. A friend can't join over Steam; the lobby is local. A friend on the same network can join it from their own Mac with the guest package: `docs/coop-guest.md`.
- A bot copy that crashes or is killed (`kill -9`) stays in your lobby or run as a ghost. Back out and host again.
- A bot can't play a character you have not unlocked; it picks another and says so.
- Both copies share the game's data folder for mods' own files (`betterspire2_*.json`).

Your own side is a normal co-op run in your modded profile: history, stats and unlocks (expected).

## Before the test (game closed)

1. `scripts/install.sh`. The mod in the game's folder is older than this code, and the bot copy loads the same DLL.
2. Steam, Library, right-click the game, Properties, General, Launch Options: `--fastmp host_standard` (after `%command%` if the box already has one). Your game then opens a local lobby at every main menu. Without it your lobby is a Steam lobby, and `/bot add` tells you to add it.
3. Mods: both copies must load the same gameplay mods. The copy only sees the game's own `mods/` folder, which today holds BaseLib, BetterSpire2 and SpireMind. `/bot add` checks this and says what is in the way.

Optional, in the same box before `%command%`: `SPIREMIND_BOT_WINDOW=tile` keeps the bot's window as a small visible tile (`minimized` is the default, `hide` hides the app). The copy inherits your game's environment.

## Start a bot

1. Start your game from Steam. Expected: no logo, then character select with you alone, named Mark.
2. Press T, type `/bot add silent`, Enter. "Starting a bot copy as Silent...", then within a minute "Started a bot copy as the Silent on port 17773 (player 1000). It joins your lobby by itself." and "Silent joined the chat (port 17773)."
3. The bot appears in your lobby, moves to the Silent and shows ready. Pick your character and press Embark. If you were already waiting, the bot's ready starts the run.
4. Play. Type to the bot in your chat box. `/pause`, `/resume`, `/stop` and `/budget` typed there go to the bot.
5. To end: `/botstop` sends the bots away ("Asking the bot copies to leave and quit...", then "The Silent's copy has quit."). Or just quit your game: every copy leaves and quits with it.

A copy is the game's own binary with `--force-steam=off --clientId=<id> --fastmp=join --sts2bot=1 --sts2bot-hostpid=<your pid> [--sts2bot-character=<ID>]`, `SPIREMIND_PORT=<port>` and no Steam variables. Its logs are `~/sts2bot/godot-<id>.log` (`.out` for what it printed before that, `.prev` for the try before).

Fallback: `scripts/coop-bot.sh` still starts a copy from Terminal (`--dry-run` only checks). Such a copy also joins, readies and plays by itself, but picks its own character, has no unlocks beyond the Ironclad and does not quit with your game. The script still refuses to start while `autostart` is on in `~/.config/spiremind/config.json`, though the mod no longer needs that.

Don't run `/play` in Claude Code during a co-op run: it talks to port 17772, your own window.

## First live test: what to look for

Your log: `~/Library/Application Support/SlayTheSpire2/logs/godot.log`. The bot's: `~/sts2bot/godot-1000.log`. The mod's lines (`role:`, `coop:`, `brain:`) start with `[SpireMind]`; the others below are the game's own. Useful: `grep -n "SpireMind\] coop:\|SpireMind\] role:\|Command Line Args" <log>`.

**1. Your game loads**
- `role: human copy main on port 17772`, `coop: manners off in this copy (not a bot copy)`, `coop: names: this copy is the human's`, `coop: looking for bot copies on ports 17773, 17774, 17775`.
- `Command Line Args:` shows `--fastmp host_standard`. `Failed to create host!` must not appear (UDP 33771 taken).
- In the lobby: `coop: names (lobby, this copy is the human's, local 1, host 1, name patch on): 1 is Mark`. Your row reads Mark, not Test Host.

**2. `/bot add silent`, your log, in this order**
- `coop: asked for a bot copy (Silent)`, `coop: mods: this game loaded: ...`, `coop: mods: a copy would load: ...`, `coop: mods: both copies would load the same gameplay mods and network message types`.
- `coop: profile: wrote .../default/1000/settings.save ...` and `... progress.save: ... so it can pick: Ironclad, Silent, ...`.
- `coop: hooked the game's Quit`, `coop: this game's environment variables (names only): ...`, `coop: started a copy (pid P) as player 1000 on port 17773 for SILENT: ...`.
- `coop: copy 1000: its bridge answers on port 17773 after N ms`, `coop: linked the bot copy on port 17773`, then `coop: copy 1000 is in the lobby`.
- Focus: no `coop: focus:` line means your window kept the keyboard. `coop: focus: this window lost the keyboard N ms after a copy started` followed by `... has the keyboard again` is the watch working. Note whether a fullscreen game is thrown to the desktop.
- A refusal comes back as one chat line starting "Can't start a bot copy:" that says what to do.

**3. The bot copy starts (its log)**
- `Command Line Args:` lists the flags above and none of `--log-file`, `--resolution`, `--position`. `Steam initialization skipped` is there. No `Skipping loading mod SpireMind`.
- `role: bot copy bot-17773 on port 17773`, `coop: this is a bot copy: player 1000, bridge port 17773, character SILENT, started by pid N`.
- `coop: window: settings forced in memory: windowed 640x360 ...`, `coop: window: set the no-focus flag (never takes the keyboard); focused now: False`.
- `coop: SIGTERM now means: leave the lobby or run, then quit`, `coop: watching the game that started this copy (pid N)`, `coop: hooked the game's Quit`.
- `coop: manners on: ...`, `coop: names: this copy is a bot's`, `coop: lobby: asked to play 'SILENT'`, `coop: join: this is bot copy bot-17773 (client id 1000, fastmp join)`, `coop: harmony: 4 of 4 bot copy patches applied`.
- `coop: profile: already fit for joining`. It must not be followed by `coop: profile: prepared after the main menu opened`.
- `coop: window: put away at Menu:<id>: mode Windowed -> Minimized ...`, and ten seconds later `coop: window: 60 frames a second 10 s after being put away`. A number far under 60, or `only N frames a second for 6 s (throttled while hidden?)`, means macOS slows a minimised copy; the next lines say what the fallback did. No sound, no popup.

**4. Join, pick, ready (bot's log)**
- `coop: join: attempt 1 (the game's own, from --fastmp join)`, `coop: join: in a lobby after 0 s (attempt 1)`.
- `coop: lobby: joined as player 1000`, `coop: lobby: players: 1 (host) Ironclad, 1000 (me) Ironclad; ascension N`, `coop: lobby: picked Silent (was Ironclad)`, `coop: lobby: ready as Silent (pressed Embark)`, `coop: joined; picked Silent; ready; waiting for the host to start`, `coop: this copy's player has NetId 1000`.
- Your screen: the bot's row reads Silent with a ready mark, and your ascension level is unchanged. Your log: `coop: names (lobby, ...): 1 is Mark, 1000 is Silent`.
- The most likely failure: `coop: join: STOPPED, retrying cannot fix this: ...` with `VersionMismatch ... (model hash)` or `ModMismatch`. Your chat shows the same as "Silent: Can't join your lobby, and trying again won't fix it: ...". A model hash mismatch with equal mods points at SaveMerger (Workshop, your side only): copy it into `mods/` or switch it off.
- Started before you host: `coop: join: nobody is hosting on this Mac yet (Timeout); next try in 3 s`, growing to 30 s. Open the lobby and it joins on the next try.

**5. The run starts**
- Bot: `coop: the run is starting`, `coop: run: started; this copy is player 1000, Silent`, `coop: in the run as Silent`, then `brain:` lines and a "system prompt built" line that lists `coop.md`. No `brain: /start` line may appear in your own log.
- `coop: recovering: a run was set up but never started` would mean the 120 s start limit is too short for a hidden window.
- Nameplates under the figures and the player rows on the left read Mark and Silent.

**6. In the run**
- Map: the bot waits for your vote and follows it: `coop: voted for <Type> at (c,r) on the map`. With your map open and no vote for 30 s it votes itself and says which node.
- A bot line: in your chat log as "Silent: ...", and in your log `coop: bubble for player 1000 (Silent): a line of N characters queued`, then `coop: bubble for player 1000 HH:mm:ss.fff: shown for 3.6s, creature spot, ...`. Other spots: `rest site`, `shop`, `player row` (map, events, treasure; the text starts with "Silent: "), `lobby row`. The offsets beside rows are guesses: take a screenshot.
- `team[].incoming` in `curl -s http://127.0.0.1:17773/state` matches the intent numbers you see. A reviving enemy (Decimillipede segment, Test Subject) is listed with `reviving: true` and never targeted.
- Shared event, chest, rest site: see "To check" in the last section.
- Must not appear: `coop: reading <what> failed, so it is off for the rest of this run`, `coop: remote bubbles switched off`, `coop: names switched off`, `coop: lobby: switched off after repeated errors`, a desync message.

**7. The run ends**
- Bot: `coop: the run is over; reading the result, then returning to the main menu`, then `coop: run: over; returning to the main menu ...` or `coop: run: the game over screen asked for the Timeline; a bot copy goes straight to the main menu instead`, `coop: run: over for this copy (the run was cleaned up)`, `coop: join: a new main menu; its own join attempt is held back, the next try is in 30 s`.
- While you sit on your game over screen: `coop: join: the host is in a run, and a running run cannot be joined (RunInProgress); next try in 60 s`. Watch your window for the bot's row flipping to connected and back, and your log for an exception then.
- Back in your lobby the bot should join within a minute or two and ready up as before.

**8. Quitting**
- Quit your game with a bot in the lobby or run. Your log: `coop: this game is closing; asking 1 bot copy(ies) to quit`. Bot: `coop: quitting this copy: SIGTERM`, `coop: before quitting: left the lobby (goodbye sent)` (in a run: `left the run (goodbye sent)`).
- Afterwards `ps -axww | grep force-steam` shows no copy and Steam shows the game as stopped.
- `/botstop` with a bot in the lobby: your log has `coop: sent SIGTERM to the Silent's copy (pid P)` and `coop: the Silent's copy has quit`, the bot's the same lines as above, and its row leaves your lobby at once.
- Later tests: Quit a bot from the Dock while it is in your lobby (bot: `coop: quitting: goodbye sent to the host`; your log: `Client 1000 disconnected, reason: Quit`; no ghost). `kill -9` your game with a bot running (bot within 10 s: `coop: quitting this copy: the game that started this copy (pid N) is gone`). A second `/bot add` takes player 1001 on port 17774.

**9. Your own game afterwards**
- With the launch option removed, a solo run logs no `coop:` line beyond the three from load, and its snapshots have no `team`, `vote` or `co_op` field. `/start` plays as before once no bot copy is running.
- Nothing under `SlayTheSpire2/steam/` was written by the launcher, and your `logs/godot.log` was not rotated by a copy.

## When the bot does not show up

- Chat says "Can't start a bot copy: ...": do what it says (no lobby, a Steam lobby, mods that differ, a full lobby, a ghost, all three places taken).
- "The bridge of the bot copy ... did not answer within 60 s, so it was closed again": the mod did not load in the copy. In `~/sts2bot/godot-1000.log` look for `Skipping loading mod` and for errors after `Loading assembly DLL`. A popup about loading mods in its window means its seeded `settings.save` was not taken.
- "The bot copy ... quit before its bridge answered": the end of its log is in your log (`coop: copy 1000: ...`).
- Started but never in the lobby: within 90 s your log has `coop: copy 1000: a join attempt failed: <advice>`, with the copy's own lines before it:
  - `Connection timed out!`: nobody listens on UDP 33771. You were not in the co-op character select.
  - `Received non-success code during handshake`: a ghost holds that player id. Back out of the lobby and host again.
  - `Mod mismatch`, `Mods that host has`: both lists are in the log. Make them equal and restart both copies.
  - `ModelDb hash mismatch`: a mod on one side adds cards, relics or the like.
  - `Version mismatch`: the game updated between the two starts.
  - The copy's log ends at `awaiting initial game info message`: a mod that adds network message types is loaded on one side only. The watcher cancels after 45 s (`coop: join: attempt N got no answer for 45 s`) and tries again.
- In the lobby but not ready: its log says where it stands (`coop: joined; ...`). "Silent: A popup is open in this copy's window ..." in your chat means its window (in the Dock) needs a click.

## Rules that save the run

- Never `kill -9` a bot copy or let it crash-loop in your lobby: it stays as a ghost and the run can't start. Back out of the lobby and host again. `/botstop`, quitting it from the Dock, `kill -TERM`, or quitting your own game are all clean.
- Never press Load in the Multiplayer menu without `--fastmp host` in the launch options, and never use `--fastmp load`: the co-op save is renamed to `.corrupt`. To go on with a saved co-op run, set `--fastmp host`, press Load, then `/bot add`: the copy takes the save's player id and character.
- If a bot leaves a lobby where everyone else is ready, the run starts without it (the game's rule).

## Back to normal play

1. Quit your game; the copies go with it.
2. Remove the launch option. While it is set, your game opens a lobby at every main menu.
3. These can stay: the bots' profiles (`default/1000` and up, apart from your own `steam/` folder), `~/sts2bot` and `~/.config/spiremind/bot-*`. Delete `default/<id>/modded/profile1/saves/progress.save` to give a bot your newer unlocks at its next start.

## What the bot sees in co-op

Code: `src/SpireMind/Game/Coop/Teammates.cs` (the players), `CoopManners.cs` (the rules, pure functions) and the co-op parts of the screen readers. Game facts: `sts2-notes/coop-api-v0.107.1/votes.md`, `players.md`. All of it shows only in a multiplayer run with more than one player; a solo snapshot is unchanged except for reviving enemies (below).

**Who is followed.** A bot copy follows the humans' votes; your own copy follows nobody. In a local session the humans are the host and any guest: a player whose id is not a bot's 1000 to 1002 (`docs/coop-guest.md`). `SPIREMIND_COOP_WAIT` sets how many seconds the bot waits for your vote (default 30; 0 never waits, `never` always waits).

**`team` on every screen**: the other players in slot order.

| Field | Meaning |
|---|---|
| `id` | `p0`, `p1`...: the slot, the same on every copy; in a fight the target id |
| `name`, `character`, `net_id` | "Mark", "Ironclad", 1 for the host, 1000 to 1002 for bots, another number (2000) for a guest |
| `hp`, `max_hp`, `block`, `dead`, `host`, `human` | `block` only above 0; the flags only when true |
| `ended_turn` / `map_vote` | in a fight / outside one, once cast: `(col,row)` |
| `powers`, `incoming` | in a fight: their powers, and what the shown intents deal to them before Block |

Rest site, rewards, shop, treasure and per-player events also carry `co_op`: one line on what the room means for the group and who already waits on the map.

**Map.** `next[n].votes` (who voted there), `vote.rule`, `vote.voted` ("1 of 2 players have voted (still to come: you)"), `vote.yours`, `vote.follow` (the action id that follows the human, also the first action), `vote.do` (what a polite bot does now). `map:<n>` votes, or changes a standing vote. `stop_drawing` shows when map drawing mode was left on. Potions only before the vote. The bot waits when:
- you have not voted (30 s of you looking at the map, then it picks and says which);
- you voted but are on another screen and its vote would move the party (up to 30 s, 45 s at most, 4 s when you are just in the room);
- a potion of its own is still in use, or a fight is starting;
- you voted for a node its copy can't click (20 s, then the other nodes with a "cannot follow" line).

A vote the host never echoed is cleared after 8 s and offered again.

**Fight.** `you` is the bot's own target id. `play:<i>>p<j>` and `potion:<slot>>p<j>` target a teammate. `incoming_damage` is the bot's own; `team[].incoming` can differ. No undo for `end_turn`. A card play waits for the host's echo ("resolving <card>"), not a timer. Solo too: an enemy at 0 HP that comes back is listed with `reviving: true`, `untargetable: true` and its intent, and is never a target.

**What the others just did.** Code: `src/SpireMind/Game/Coop/TeamSince.cs`. A `combat` or `hand_select` snapshot of a co-op fight can end its `state` with two more fields. Both are absent in a solo run, in the small waiting snapshots (enemy turn, waiting for teammates) and when there is nothing to tell.

| Field | Meaning |
|---|---|
| `state.team_since` | At most 8 short lines, newest last: what the other players did since the model's last action went through (one `act`, or the first step of an `act_sequence`; what comes up during a sequence is in its `next`). With more, the first line reads "(N earlier lines left out)". |
| `state.team_changed` | `true` when a line in `team_since` touched this player, their pet or an enemy. Absent otherwise. In an `act` or `act_sequence` reply it is `next.state.team_changed`. |

```
"team_since": ["Mark played Lift on you: +11 Block", "Mark ended their turn"], "team_changed": true
"team_since": ["Mark played Bash on Nibbit (e0): 8 damage, 2 Vulnerable", "Mark used Fire Potion on Fogmog (e1): 20 damage"], "team_changed": true
```

The lines come from the fight's own record (`CombatManager.Instance.History`), which every copy keeps for every player:
- A card: "Mark played Iron Wave on Nibbit (e0): 5 damage (2 blocked); Mark +5 Block". What hit the card's target comes first with no name; anyone else is named. Damage is what Block took plus the HP lost (on a kill: the HP that was left); "(2 blocked)" is the part Block took, "(killed)" a kill. A replayed card reads "played Bash 2 times", one played by an effect "(auto)".
- A potion: "Mark used Fire Potion on Fogmog (e1): 20 damage". The record writes a potion after its effects, so the numbers are what it shows the user dealing or applying since the card or potion before (to the potion's target, when it has one), plus Block that landed on that target there. A relic or power of the same player that hit the same target in that gap is counted in.
- "Mark ended their turn", "Mark took back their end of turn": from the ready flags.
- "Mark also: Nibbit (e0) 3 damage": the record names Mark as dealer or applier, outside any card or potion of his (a relic or power of his fired). Only what touched the bot or an enemy.
- "No recorded cause: you +3 Block": in the record with nobody named, on the bot or an enemy. Left out where a card or potion of the bot's own lies next to it. Inside a teammate's card with nothing else around, it goes on that card's line instead.
- "Not in the fight record: you +14 HP; Nibbit (e0) -12 Block": a heal, Block taken away or energy given leaves no entry. This is the difference in the bot's HP, Block and energy and each enemy's HP and Block between two captures with nothing running, less what the record explains. Left out when the bot acted in between.

When lines go. The game side cannot see which snapshot the model got: the wait loop polls, every action is checked against a fresh capture, the brain peeks. So lines stay, the same in every snapshot, until the bot's own action (any `play`, `potion`, `discard_potion`, `end_turn`, `select` or `confirm` the game took). That action clears what the snapshots before it showed, once the board has changed too; an action the game ignored clears nothing, so "nothing changed" still holds. A line that first shows in the action's own check capture stays for the next snapshot. A new turn clears everything. A card still running when its line was cleared reports the rest as "Mark's Lift went on: ...".

What an action clears, the model has read: `act`, and the first step of `act_sequence`, run only when the capture before them has the version the model sent, so every line in it was in the model's own snapshot. A later step of a sequence runs on a snapshot the model never saw. The game side tells it by time: the tools run the next step 300 to 500 ms after the snapshot settled, the model needs seconds. An action that comes less than 700 ms after its snapshot first read as it does clears nothing (`TeamSince.ModelAnswerMs`). So lines that come up during a sequence stay through its later steps and are in the `next` it returns.

For tool code: stop a running `act_sequence` when the snapshot after a step has `state.team_changed`. The flag on the snapshot the sequence started from is old news: the model planned with it, and the first step clears it. Nothing has to be carried: a line first shown after a step is still in every later snapshot of that sequence, so copying it into the reply as well would show it twice. If the tools ever wait longer than 700 ms between a step's snapshot and the next step, raise `ModelAnswerMs` with it, or the old loss is back: lines without `team_changed` shown after a step are cleared by the next one.

Not told: cards a teammate draws, discards or exhausts; orbs channelled, stars and summons; powers that are taken away with no entry; what a teammate did after the bot ended its turn; anything from the turn's setup or its end (start-of-turn and end-of-turn relics and powers, start-of-turn autoplays). A potion's line, and its `team_changed`, come when the game records the potion, which is after its effects: the board can show the damage a moment before the line does. A potion that is the turn's very first card or potion, used before the bot's first snapshot of that turn with nothing running, is listed without numbers: they cannot be told from the setup's. Block a teammate's potion gives the bot is left off the potion's line when the bot's own card or potion ran right before it with no gap: it could be the bot's own trigger.

**Rest site.** Mend is an ordinary option: `option:<n>` with one teammate, `option:<n>>p<j>` with several, labelled "Mend Mark (p0): heals them 21 HP (34/70 now), ...". It runs through the game's own targeting arrow in one step; a failure cancels the arrow and the option is not used up.

**Rewards.** Per player, nobody waits. The `proceed` label counts the rewards still unclaimed. After an act's last boss `act_vote` shows the count; proceed there is the vote and can't be taken back.

**Treasure.** `relics[n].votes`, `vote.rule`, `vote.voted`, `vote.do`. The bot lets you pick first, then takes a relic nobody voted for. `proceed` shows once the relics are handed out.

**Events.** A shared event (`shared_vote: true`) is a vote on every page: `options[n].votes`, `options[n].will_kill` (not offered), `vote.*` as on the map. `option:<n>` votes or changes a vote until the page is decided. A single-option page is voted at once. Potions only before the bot's vote. A host vote already on a new page after a page the bot voted first on may be left over: it is listed as `options[n].unconfirmed_votes` and not followed until it changes.

**Never offered.** Anything in a room once the bot's map vote is out or while a fight is starting ("your map vote is in: nothing in this room may be touched until the party has moved"); a second click on the chest or boss proceed; an option that kills the bot or a teammate.

**In the bot's log**: `coop: voted for Elite at (2,5) on the map`, `coop: voted for <option> in the shared event (...)`, `coop: Mend aimed at Mark (p0) (34/70 hp, heals 21)` and how the arrow was finished, `coop: the host never recorded the map vote ...`, `coop: the vote for <relic> never reached the game ...`, `coop: the map point (c,r) the human voted for has refused clicks here for 20 s ...`, `coop: <name> already has a vote on the new event page ...`, `coop: team_since: following this fight's record (N entries so far)` once per fight, `coop: team_since: Mark played Lift on you: +11 Block` once per line, `coop: team_since: N line(s) handed over with this copy's own action`, `coop: team_since: N line(s) kept: this action came 340 ms after its snapshot, so it is a later step of a sequence`. `coop: team_since failed, so it is left out for the rest of this fight: ...` and `coop: team_since: the hand-over failed: ...` must not appear. Your own copy reads the record too when something asks it for a snapshot in a fight (its brain looks at the screen every 5 s), so its log can carry the bots' `coop: team_since:` lines; nothing but the turn's end clears them there, and nothing uses them.

**To check in the first run**
- With your vote in and your rewards screen open, the bot holds its vote until you are back on the map.
- Mend: the arrow's log line, the heal on your character, and the bot's proceed afterwards.
- Shared event: the bot votes after you on a page with a choice and at once on a one-option page; with its vote in, it is offered no potion.
- Chest: the bot picks after you and takes the other relic.
- `team_since`: play a card on the bot, one on an enemy, a potion, then end your turn while the bot is still thinking. `curl -s http://127.0.0.1:17773/state` shows the four lines with the numbers you saw, the bot's log has one `coop: team_since:` line each, and they are gone from the snapshot after the bot's next action.
- `team_since` in a sequence: play a card on yourself while the bot runs an `act_sequence`. The bot's log has a `line(s) kept: this action came ... ms after its snapshot` line with a number between 300 and 600, the sequence's `next` still has your card's line, and the `handed over` line comes with the bot's next call. A `kept` line with a bot action the model sent on its own (one `act`, or a sequence's first id) means the 700 ms are too long for this model.
- A standing vote of the bot's with yours missing: about one model request a minute on the API brain. A `/play` or subscription client must not loop.
