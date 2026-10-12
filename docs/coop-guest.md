# Co-op with a friend on another Mac (guest mode)

You host a local co-op lobby as in `docs/coop.md` and add a bot with `/bot add`. A friend on the same network joins that lobby from their own Mac, with their own Steam copy of the game and the guest package. Three players, one run: you, the friend and the bot.

Built on 2026-10-12 against v0.107.1 (0 warnings, Core tests pass). **None of it has run in the game.** No second computer has joined a lobby of yours yet. "Expected" below means read from the game's code. The last section lists what only a live session can show.

## What a guest copy is

The friend's game becomes a guest copy through one launch option, `--spiremind-host=<your address>`. Nothing else makes one: not an environment variable, not a default. A copy that hosts (`--fastmp host...`, `load`) is never a guest, so your own game cannot turn into one, and a bot copy stays a bot copy.

In a guest copy the mod starts nothing but this (`src/SpireMind/Game/Coop/GuestMode.cs`):

- **The join.** The game's own fast join dials 127.0.0.1:33771. A prefix on that one private method makes the same call the game makes, with your address in place of 127.0.0.1. After that it is the game's own join: one attempt each time the Join screen opens, the game's own popups.
- **The names.** The game sends no names. The friend's copy shows their own name, your name and each bot's character.
- **One line on the game's "Disconnected" popup**, starting with `SpireMind guest:`, that says where the copy dialed and what to do.
- **A guard on Load** in the friend's Multiplayer menu. With `--fastmp` set the game would rename a co-op run the friend once saved to `.corrupt`. In a guest copy Load does nothing and says why.

A guest copy has no bridge, no brain, no chat box, no relay and no `/bot add`. It reads no key, writes no file and starts no process. The package also holds `guest-only.txt` beside the DLL: with that file the mod is a guest copy or does nothing at all, even with no launch options. `scripts/install.sh` never puts that file in your own game.

The friend cannot see the bot's chat lines and cannot type to it. You read them out, and you type what the friend wants to say ("Amir wants the elite").

## What changes in your game and in the bot

Players are told apart by their id alone, the same on every copy (`src/SpireMind.Core/Coop/CoopSeats.cs`): 1 is you, 1000 to 1002 are bots, anyone else is a guest. The friend joins as 2000.

- **Name.** Your window and the bot call the guest by the name you set (next section), whatever character they play. Without a name: "Guest". The bot is told the name by seat, never by what the game calls the player.
- **The Steam name never shows.** In a local lobby the game makes every name from the player id and never asks Steam, on any copy (`Creature.Name`, the lobby rows and the player list all ask with the session's platform, which is "none"). So "RunForYoLife" cannot reach your screen or the bot. Nothing needs to be mapped.
- **The bot treats the guest as a person**: `human: true` in `team`. On the map and in shared events it waits for a person's vote (up to 30 s, `SPIREMIND_COOP_WAIT`) and follows the first one cast. When you two have voted for different things, it sides with you, the host, as long as its own vote can still change. At a chest it waits for both of you and then takes a relic neither of you voted for.
- **Bots keep their names.** If the friend plays the Silent too, the bot is still "Silent", not "Silent 2".
- A bot started by hand with `scripts/coop-bot.sh --client-id` and an id outside 1000 to 1002 would be taken for a guest. Use `/bot add`.
- The planning sessions (`/plan`) still know one person, you. The friend's deck is not in the briefs. The bot holds its picks during a session as before, so at an act's start all three wait for it.
- The bot keeps its character when the friend picks the same one (the game allows doubles). `/bot add <character>` chooses.

## Before the session (game closed)

1. **Install the new build.** Quit the game. `scripts/install.sh`. Your game and the bot copies load the same DLL.
2. **Launch options stay** `--fastmp host_standard`.
3. **Name your guest.** One line in Terminal:
   ```
   mkdir -p ~/.config/spiremind && echo Amir > ~/.config/spiremind/guest_name
   ```
   Your game and the bot copies both read that file (only that file; several guests: a name per line, in lobby order). It is read again every few seconds, so it also works while the game runs. `rm ~/.config/spiremind/guest_name` takes it back. The other way is `SPIREMIND_GUEST_NAME=Amir %command% --fastmp host_standard` in the launch options; that form has not been tried through Steam on this Mac.
4. **Find your address** on the network you will share:
   ```
   ipconfig getifaddr en0
   ```
   Four numbers with dots. It was 10.250.203.164 on the morning of 2026-10-12. It changes with the network and can change on the same network, so read it again right before you play. System Settings, Wi-Fi, Details shows the same.
5. **Build the package**:
   ```
   scripts/package-guest.sh --address <your address> --guest-name Amir --host-name Mark
   ```
   It writes `out/guest/SpireMind-guest.zip` and, beside it, the same `README.txt` that is in the zip. Send the zip (AirDrop is fine). Without `--address` the friend's line holds the word `HOSTS-ADDRESS` for them to replace. If your address changes later, tell the friend to change only the numbers after `--spiremind-host=`.
6. **Compare the mods.** The join compares gameplay mods in both directions, by id and version text. Yours announces `BetterSpire2-v1.83.10`. The friend's Workshop copy must be exactly that.
   - Your game loads the copies in its own `mods/` folder (BaseLib v3.4.7, BetterSpire2 v1.83.10). When a mod is there and on the Workshop both, the `mods/` copy always wins, and the bot copies see no Workshop at all. The friend loads the Workshop copies. Steam updates those by itself; yours in `mods/` stay as they are. Today all four are the same.
   - BaseLib is not in the mod check, but it adds network message types. A different BaseLib can break the join with no clear message: the friend's Join screen hangs, or shows the version popup.
   - So before you start, the friend reads both versions in the game (Settings, the mod list, click the mod) and tells you. They must read v1.83.10 and v3.4.7.
   - If the friend's are newer, update yours. Let Steam update your Workshop copies, quit the game, then:
     ```
     W="$HOME/Library/Application Support/Steam/steamapps/workshop/content/2868840"
     M="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods"
     grep '"version"' "$W/3747530215/BetterSpire2.json" "$W/3737335127/BaseLib/BaseLib.json" "$M/BetterSpire2/BetterSpire2.json" "$M/BaseLib/BaseLib.json"
     rsync -a --delete "$W/3747530215/" "$M/BetterSpire2/"
     rsync -a --delete "$W/3737335127/BaseLib/" "$M/BaseLib/"
     ```
     Run the `grep` first: if the Workshop lines already equal the friend's versions, the two `rsync` lines make your game and the bots match. Do not delete the `mods/` copies (the bots would have no BetterSpire2), and do not switch BetterSpire2 off in your settings only (the bots have their own settings).
   - If the friend's are older, they restart Steam and let it update.
   - The friend switches off every other mod for the session. A mod that adds cards, relics or network messages on one side breaks the join.
7. **The network.**
   - Your Mac's firewall is off (checked on 2026-10-12: "Firewall is disabled"). Nothing to do. If you ever switch it on: when your lobby opens, macOS asks whether Slay the Spire 2 may accept incoming connections. Allow it. The game then receives on UDP 33771. The switch is System Settings, Network, Firewall, Options.
   - System Settings, Privacy & Security, Local Network: Slay the Spire 2 and Steam must be on, on both Macs. Yours too: your game answers the friend's Mac, and macOS counts that as reaching a device on the local network. On the friend's Mac the question comes at their first join, and that first try can time out while they answer it.
   - Both Macs must be on a network that lets devices talk to each other. Guest, office, hotel and cafe networks often do not. Your address this morning starts with 10.250, which looks like such a network. The test: the friend types `ping -c 3 <your address>` in Terminal. No "64 bytes from" lines means the game cannot connect either. A phone hotspot both Macs join works; your address is then another one.
8. **Optional: try it alone first.** See "A trial on your own Mac".

## The order of starting

1. Start your game from Steam. Character select, you alone, named Mark.
2. `/bot add silent` (or another character). At most two bots: a lobby holds four players.
3. Tell the friend to start their game. Their row appears within seconds, named Amir.
4. The friend picks a character and presses Embark.
5. **You press Embark last.** The run starts the moment everyone in the lobby is ready, the bot readies at once, and a run that has started cannot be joined. If you are ready before the friend's row is there, the run starts without them.
6. To end, everyone leaves through the game's menus. `/botstop` sends the bots away.

Everyone joins with the same id every time: a saved co-op run takes back only the ids it holds. To go on with a saved run later: `--fastmp host` in your launch options, press Load, `/bot add`, and the friend starts their game with the same line as before.

The lobby's ascension level is capped by the lowest cap among the players. If the friend's cap is lower than your level, your level drops and the game saves that as your preferred level. Set it back afterwards.

## What the logs show

Your log: `~/Library/Application Support/SlayTheSpire2/logs/godot.log`. The bot's: `~/sts2bot/godot-1000.log`. The friend's is the same first path on their Mac.

**Yours, when the friend joins** (the first three are the game's own lines, as in the bot's join on 2026-10-11):
- `[ENetHost] Received handshake packet containing peer ID 2000`
- `[StartRunLobby (1)] Client 2000 connected. Sending initial game info message`
- `Received ClientLobbyJoinRequestMessage for 2000`
- `[SpireMind] coop: names: a player who joins from another computer is called Amir (...)`
- `[SpireMind] coop: names (lobby, this copy is the human's, local 1, host 1, name patch on): 1 is Mark, 1000 is Silent, 2000 is Amir`

"Client 2000 connected" with no join request after it, and the friend back on their Join screen with a popup: their game refused yours over the version or the mods. Their popup says which.

**The bot's**:
- `coop: lobby: players: 1 (host) Ironclad, 1000 (me) Silent ready, 2000 Defect; ascension N`
- `coop: names (lobby, this copy is a bot's, local 1000, host 1, name patch on): 1 is Mark, 1000 is Silent, 2000 is Amir`
- In a run, `curl -s http://127.0.0.1:17773/state` has a `team` entry with `"name":"Amir"`, `"net_id":2000` and `"human":true`.

**The friend's** (`grep -n 'SpireMind\]' godot.log`):
- `role: guest copy; joins 10.250.203.164:33771 as player 2000; no brain, no bridge, no bot copies, no files`
- `guest: names: this copy's own player shows as Amir, the host as Mark and each bot as its character`
- `guest: harmony: the game's fast join now takes the host's address from the launch options`
- `guest: ready: the Join screen dials 10.250.203.164:33771 as player 2000`
- `guest: join attempt 1: dialing 10.250.203.164:33771 as player 2000 (...)`
- `coop: names (lobby, this copy is a guest's, local 2000, host 1, name patch on): 1 is Mark, 1000 is Silent, 2000 is Amir`
- After a failure: `guest: the game reports: Timeout ...` and `guest: SpireMind guest: Mark's game is at ...; you are player 2000. Nobody answered. ...`
- With a mistake in the launch options: `role: guest copy; NOT joining, the launch options have 1 problem(s)`, a `guest: launch options: ...` line for each, and a popup titled SpireMind in their window.
- No `role:` line at all: the mod did not load there. `Command Line Args:` near the top shows what Steam passed.

## When the friend cannot join

Ask what their screen shows. Their README has the same list.

| What they see | What it is | What to do |
|---|---|---|
| "Disconnected" popup with no `SpireMind guest:` line | The mod is not loaded, or the launch options did not reach the game | Folder: `mods/SpireMind/SpireMind.dll`. Launch options saved. Restart their game |
| Popup titled SpireMind with a list | A mistake in the launch line | It says which part |
| "Nobody answered" (Timeout, after 10 s) | Nothing reached your lobby | Are you in character select? `Failed to create host!` in your log means UDP 33771 was taken: quit every copy and start again. Address still right? `ping`. Local Network switch on both Macs. Another network |
| The popup lists mods (ModMismatch) | BetterSpire2 versions differ, or they have a gameplay mod you lack | Step 6 above |
| "Versions do not match" (VersionMismatch) | Game build differs, or the sets of cards and relics differ (a mod on one side). Their log tells them apart: `Version mismatch. Host: X Ours: Y` or `ModelDb hash mismatch` | Same Steam branch on both. Switch off extra mods |
| Their Join screen shows its loading sign and nothing more | A mod with network messages on one side only, most likely another BaseLib | They press Back. Compare BaseLib. Extra mods off |
| "Lobby already holds a player 2000" (Kicked) | A ghost of the friend from before | Back out of the lobby and host again. Everyone joins again |
| "The run has already started" (RunInProgress) | You are in a run or on your game over screen | Open a new lobby. They press Back, then Join |
| "The lobby is full" | Four players | `/botstop` and add fewer bots |
| Popup with only Report Bug, in a run | The connection to your game dropped | They can't rejoin a running run. Save and Quit, host with Load (`--fastmp host`), everyone joins again |

A friend who closes the window, presses Cmd+Q in a run, or whose Mac sleeps leaves no goodbye. Your game keeps their player as a ghost and waits for it at the next room change. Only leaving the lobby or the run clears it. Over Wi-Fi the goodbye can also be lost when they leave properly: it is one packet, sent once.

## A trial on your own Mac

`scripts/coop-guest-trial.sh` starts a second copy of the game without Steam as a guest. It joins as player 2000 and dials your Mac's own network address, not 127.0.0.1. You play that copy yourself in its window.

```
scripts/coop-guest-trial.sh --dry-run    # every check and the command; changes and starts nothing
scripts/coop-guest-trial.sh              # prepares the copy's profile, starts it, says whether it joined
```

Before: `scripts/install.sh` with the game closed, then your game hosting in character select. Add a bot first if you want to see three players. The script seeds the copy's `settings.save` under `SlayTheSpire2/default/2000` through `scripts/coop-bot.sh --prepare-only` (mods agreed to, a small muted window) and writes the copy's log to `~/sts2bot/godot-guest-2000.log`.

What it shows:
- the guest role's log lines, and that no bridge answers for that copy;
- the join going to your network address and getting in, so your lobby is reachable there and not only on 127.0.0.1;
- the names: "Amir" in your window and in the bot's `team`, with `human: true`;
- the bot following two people's votes;
- started before your lobby is open: the "Disconnected" popup with the `SpireMind guest:` line.

What it does not show:
- traffic between two computers: Wi-Fi, the firewall, the Local Network permission. On one Mac the packets never leave it;
- a guest whose game runs with Steam: their real profile, the progress written at the run's end, the Load guard;
- the friend's own mods and their versions;
- two Macs with different processors.

The copy's profile is new: only the Ironclad, tutorials on, ascension cap 0. While it sits in your lobby your ascension level drops to 0 and the game saves that as your preferred level. Leave the copy through the game's menus before you close it.

## Afterwards

- The friend empties their Launch Options box. Their README says what happens while the line is there.
- `~/.config/spiremind/guest_name` can stay. It only matters when someone who is not a bot joins.
- Check your ascension level in the next lobby.

## Game members a guest copy touches

Recheck these after a game update (`CLAUDE.md`). All were found in the installed v0.107.1 assembly by reflection on 2026-10-12; none has run.

| Member | How | Why it is safe |
|---|---|---|
| `NJoinFriendScreen.FastMpJoin()` (private, returns Task) | prefix, guest copies only | It replaces a method whose whole body is "read `--clientId`, set the window title, call `JoinGameAsync` with 127.0.0.1:33771". The prefix does the same with the host's address. If it throws, the game's own method runs |
| `NJoinFriendScreen.JoinGameAsync(IClientConnectionInitializer)` (public) | called | The game's own join, the one its fast join and its Steam friend buttons call |
| `ENetClientConnectionInitializer(ulong, string, ushort)` (public) | constructed | The game's own type for an address; the address is checked to be a plain dotted IPv4 before, since the game does not check what the engine makes of the text |
| `NErrorPopup.Create(NetErrorInfo)` | postfix, guest copies only | Read-only but for one string: the popup's text gets one more line |
| `NErrorPopup._body` (private string field) | reflected | Read by the popup when it enters the tree. If the field is gone, the line is in the log only |
| `NErrorPopup.Create(string, string, bool)`, `NModalContainer.Instance.Add`, `.OpenModal` (public) | called | The game's own way to show a text popup; never while another popup is open |
| `NMultiplayerSubmenu.StartLoad(NButton)` (private) | prefix, guest copies only | Skips Load while `--fastmp` is set, the one case in which the game would rename the save |
| `NullPlatformUtilStrategy.GetPlayerName(ulong)` | postfix, as in every copy | Display only (`docs/coop.md`) |
| `CommandLineHelper.TryGetValue`, `.HasArg`, `DisplayServer.WindowSetTitle` | called | Read-only; the title is the one the game's own fast join sets |

The mod's DLL is processor-neutral, like BaseLib's and BetterSpire2's, so the same file loads on an Intel Mac and an Apple Silicon Mac. It adds no cards, relics or network messages, so it is in nothing the join compares, and the friend's build of it need not be the same as yours.

## Not tested

- A join from a second computer: that the lobby, which the game binds to 0.0.0.0:33771, takes traffic from the network, and what macOS asks on either Mac.
- The prefix on the private `NJoinFriendScreen.FastMpJoin`. The bot copies' patches on other private members work the same way and have run, this one has not. If it does not apply, the friend's log says `guest: starting the join to the host's address failed`.
- A client whose game runs with Steam on a local lobby. Your game is the same mix as a host and has run; a client has not. Read from the code: the friend plays in their normal Steam profile, and `--clientId=2000` is what makes the game book the run under their own player.
- The line added to the game's popup, the SpireMind popup for a wrong launch line, and the Load guard.
- Three players in one lobby, and the bot with two people to follow.
- The names in all three windows.
- `SPIREMIND_GUEST_NAME` through Steam's launch options.
- What BetterSpire2 does with a third player (it has multiplayer options of its own).
- An Intel Mac with an Apple Silicon Mac.
- The exact wording of the game's popups: the reasons are read from the code, the texts are not.
