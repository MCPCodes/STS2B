SPIREMIND GUEST PACKAGE
For Slay the Spire 2 on a Mac. Read it once from top to bottom; it takes ten minutes.


WHAT THIS IS

@HOST@ plays Slay the Spire 2 co-op with an AI player. This small mod lets your
game join @HOST@'s game over the Wi-Fi you share, so three play one run:
you, @HOST@ and the AI.

What it does on your Mac:
  - It makes the game's own Join dial @HOST@'s computer on your local network.
    That is the only connection it makes.
  - It shows real names in the lobby in place of "Test Host" and "Test Client 2".
  - It adds one line, starting with "SpireMind guest:", to the game's
    "Disconnected" popup, so you know what to do when a join fails.
  - It keeps the Load button in the Multiplayer menu from running while you are
    set up as a guest (see AFTERWARDS for why).

What it does not do:
  - No AI runs on your Mac. It needs no account, no key and no sign-in.
  - It sends nothing anywhere except the game's own co-op traffic to @HOST@'s game.
  - It writes no files and starts no programs.
  - Without the launch options of step 3 it does nothing at all.


WHAT YOU NEED

  - Slay the Spire 2 on Steam, version @GAME_VERSION@. The main menu shows the version
    in a corner. The normal Steam version, not a beta.
  - The Workshop mods BaseLib and BetterSpire2, switched on. You have them already.
  - Your Mac on the same Wi-Fi (or cable network) as @HOST@'s.


STEP 1. CHECK THAT YOUR MODS MATCH (before anything else)

Start the game the normal way. On the main menu open Settings and then the
mod list. Click each mod and read its Version line:

    BetterSpire2   must read   @BETTERSPIRE@
    BaseLib        must read   @BASELIB@

Tell @HOST@ what yours say. Steam updates Workshop mods by itself, so yours can
be newer than @HOST@'s. If they differ you cannot join until @HOST@ has updated
his side. That takes him two minutes, and only he can do it.

Switch off every other mod in that same list for today, also the harmless
looking ones. Then quit the game. (You can switch them on again afterwards.)


STEP 2. PUT THE MOD IN THE GAME'S MODS FOLDER

  1. Double-click SpireMind-guest.zip. You get a folder with this file and a
     folder named SpireMind.
  2. In Steam: Library, right-click Slay the Spire 2, Manage, Browse local files.
     A Finder window opens and shows SlayTheSpire2.
  3. Right-click SlayTheSpire2 and choose Show Package Contents.
     Open Contents, then MacOS.
  4. If there is no folder named mods in there, make one: File, New Folder,
     and name it mods (small letters).
  5. Drag the SpireMind folder into mods. If macOS asks for permission, allow it.

When it is right, the mods folder holds a SpireMind folder, and that holds
SpireMind.dll, SpireMind.json and guest-only.txt.


STEP 3. SET THE LAUNCH OPTIONS

In Steam: Library, right-click Slay the Spire 2, Properties. Under General there
is a box named Launch Options. Paste this one line into it, exactly:

    @LAUNCH_LINE@

@ADDRESS_NOTE@
Close the Properties window. Steam saves it by itself.


STEP 4. PLAY

  1. Wait until @HOST@ says his lobby is open.
  2. Start the game from Steam. It skips the logo, opens Join by itself and
     connects. This takes a few seconds.
  3. The first time, macOS may ask whether Slay the Spire 2 (or Steam) may find
     devices on your local network. Click Allow. If the join timed out while
     you were answering: OK, then Back, then Join.
  4. You are in character select with @HOST@ and the AI. The names read @GUEST@
     for you and @HOST@ for the host. The AI goes by the name of its character.
  5. Pick your character and press Embark. Embark means "I am ready".
     @HOST@ presses Embark last, and the run starts.

During the run:
  - The AI talks in @HOST@'s window only. @HOST@ will read out what it says, and
    will pass on what you want to tell it.
  - On the map and at chests the AI lets you and @HOST@ choose first.
  - To leave, use the game's own menus: Back in the lobby, or Escape and then
    Disconnect in a run. Do not close the window, press Cmd+Q or let the Mac
    sleep in the middle. @HOST@'s game would keep your player as a ghost and
    get stuck.
  - If you drop out of a run you cannot get back into it. @HOST@ has to save and
    quit, load the run again, and then you join again.

To try again after a failed join: OK on the popup, then Back, then Join.


IF IT DOES NOT CONNECT

Look at what is on your screen.

A "Disconnected" popup WITHOUT a line that starts with "SpireMind guest:"
    The mod is not at work. Check step 2 (the SpireMind folder must sit directly
    in mods, with SpireMind.dll in it) and step 3 (the line is in Launch Options).
    Quit and start the game again. On the main menu, the corner with the version
    also says how many mods are loaded; point at it and SpireMind must be listed.
    If the game asks whether to load mods, say yes. It closes; start it again.

A popup titled "SpireMind" that lists problems
    The line in Launch Options has a mistake. The popup says which part.
    Quit, fix the line, start again.

"Disconnected", and the SpireMind line says "Nobody answered"
    The game waited ten seconds and got no answer.
    - Is @HOST@ sitting in his co-op character select right now?
    - Is the address in your Launch Options the one @HOST@ gave you today?
      It changes from place to place.
    - Are both Macs on the same Wi-Fi? Guest, office, hotel and cafe networks
      often stop devices from talking to each other. A phone hotspot that both
      Macs join works.
    - Did you click Don't Allow at the local network question? Open System
      Settings, Privacy & Security, Local Network, and switch on Slay the Spire 2
      and Steam. Then start the game again.
    - A test: open the Terminal app and type  ping -c 3 @PING_ADDRESS@
      If no line starts with "64 bytes from", your Mac cannot reach @HOST@'s
      at all, and the cause is the network or the address, not the game.

"Disconnected", and the popup lists mods
    Your BetterSpire2 is another version than @HOST@'s, or you have a gameplay mod
    @HOST@ does not. The popup names them. See step 1.

"Disconnected", and the popup says the versions do not match
    Your game is another version than @HOST@'s, or one of you has a mod that adds
    cards or relics. Compare the version in the main menu corner. See step 1.

The SpireMind line says the lobby already holds your player
    You left earlier without the game saying goodbye. @HOST@ has to back out of
    his lobby and host again. Then Back, Join.

The SpireMind line says the run has already started
    @HOST@ is in a run or has not closed his last one. Wait for him to say the
    lobby is open. Then Back, Join.

The lobby is full
    A lobby holds four players. @HOST@ has to send an AI player away.

The Join screen shows its loading sign for more than half a minute, with no popup
    Press Back. Most likely your BaseLib is another version than @HOST@'s, or
    another mod of yours is on that @HOST@ does not have. See step 1.

The game starts with its logo and the normal main menu, and never tries to join
    The Launch Options are empty, were not saved, or their dashes got changed.
    Every option starts with two plain minus signs (--). A chat app can turn
    them into one long dash. Copy the line from this file. See step 3.

A popup with only a Report Bug button, in the middle of a run
    The connection to @HOST@'s game was lost. No report is needed. Close it and
    tell @HOST@.

Still stuck? Send @HOST@ this file. It is the game's log, and its lines that
start with [SpireMind] say what the mod did:
    In Finder choose Go, Go to Folder, and paste
    ~/Library/Application Support/SlayTheSpire2/logs/godot.log


AFTERWARDS: BACK TO NORMAL

  1. Quit the game.
  2. Steam, right-click Slay the Spire 2, Properties: empty the Launch Options box.
     This is the step that matters. While the line is there:
       - the game tries to join @HOST@ at every start and after every run;
       - your own Host button opens a local lobby, not a Steam one;
       - the game itself would mishandle Load in the Multiplayer menu: it would
         not find you in a co-op run you saved earlier and would set that save
         aside as broken. The mod blocks Load for that reason. With the box
         empty, Load works as always.
  3. The SpireMind folder can stay in mods. Without the launch options it does
     nothing. To remove it, drag it out of mods into the Trash.

Your saves and progress:
  - You played on your normal profile, the one you use with your Workshop mods.
  - The run counts like any co-op run you joined: play time, statistics, what
    you discovered and unlocked, achievements.
  - Nothing of yours is reset, overwritten or copied. Your own saved runs are
    not touched. The save of this co-op run exists only on @HOST@'s Mac.
  - In your run history the players of this run show as "Test Host" and
    "Test Client 2", not by their Steam names.
