SPIREMIND GUEST PACKAGE
For Slay the Spire 2 on a Mac. Read it once from top to bottom; it takes ten minutes.


WHAT THIS IS

@HOST@ plays Slay the Spire 2 co-op with an AI player. This small mod lets your
game join @HOST@'s game over the Wi-Fi you share, so three play one run:
you, @HOST@ and the AI.

This way of joining is new. Nobody has tried it with a second computer yet.
If something fails, the part IF IT DOES NOT CONNECT says what to look at and
what to tell @HOST@.

What it does on your Mac:
  - It makes the game's own Join dial @HOST@'s computer on your local network.
    That is the only connection it makes.
  - It shows real names in the lobby in place of "Test Host" and "Test Client 2".
  - It adds one line, starting with "SpireMind guest:", to the game's
    "Disconnected" popup, so you know what to do when a join fails.
  - It keeps the game from spoiling a co-op run you saved yourself, and keeps
    the Host from Save button in the Multiplayer menu from running, while you
    are set up as a guest (see AFTERWARDS for why). This only matters if you
    have a saved co-op run of your own.

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

Start the game the normal way. On the main menu choose Settings. On the first
tab, General, scroll down to the line Modding and press the button beside it,
Mod Settings. You see the list Installed Mods. Click a mod in the list and its
details show a line Version:

    BetterSpire2   must read   @BETTERSPIRE@
    BaseLib        must read   @BASELIB@

Tell @HOST@ what yours say, letter for letter. Steam updates Workshop mods by
itself, so yours can be newer than @HOST@'s. If they differ you cannot join until
@HOST@ has updated his side. That takes him two minutes, and only he can do it.
If yours are older than his: quit the game, quit Steam, start Steam again and
let it finish its downloads.

In that same list, untick the box of every other mod for today, also the
harmless looking ones. BetterSpire2 and BaseLib stay ticked. Then quit the game:
the change counts from the next start. (You can tick them again afterwards.)


STEP 2. PUT THE MOD IN THE GAME'S MODS FOLDER

  1. Double-click SpireMind-guest.zip. You get a folder named SpireMind-guest.
     In it are this file and a folder named SpireMind.
  2. In Steam: Library, right-click Slay the Spire 2, Manage, Browse local files.
     A Finder window opens and shows SlayTheSpire2. That is the game itself.
  3. Right-click SlayTheSpire2 and choose Show Package Contents.
     Open Contents, then MacOS. You see a file named Slay the Spire 2.
  4. If there is no folder named mods beside that file, make one: File,
     New Folder, and name it mods (small letters).
  5. Drag the whole SpireMind folder into mods. If macOS asks for permission,
     allow it.

When it is right, the mods folder holds a SpireMind folder, and that holds
SpireMind.dll, SpireMind.json and guest-only.txt. All three must be there.


STEP 3. SET THE LAUNCH OPTIONS

In Steam: Library, right-click Slay the Spire 2, Properties. Under General there
is a box named Launch Options. If something is in the box already, copy it into
a note first, so you can put it back afterwards. Then make the box hold this
one line and nothing else:

    @LAUNCH_LINE@

Copy the line from this file, not from a chat message. Every option starts with
two plain minus signs (--), and a chat app can turn those into one long dash.
The line has no quotes and no spaces around the = signs.
@ADDRESS_NOTE@
Close the Properties window. Steam saves it by itself.


STEP 4. PLAY

  1. Wait until @HOST@ says his lobby is open.
  2. Start the game from Steam. It skips the logo, opens Multiplayer and Join by
     itself, shows "Loading..." and connects. This takes a few seconds.
  3. The first time, macOS may ask whether Slay the Spire 2 (or Steam) may find
     devices on your local network. Click Allow. If the join timed out while
     you were answering: Ok, then Back, then Join.
  4. You are in character select with @HOST@ and the AI. The names read @GUEST@
     for you and @HOST@ for the host. The AI goes by the name of its character.
  5. Pick your character and press Embark. Embark means "I am ready". If the
     game asks you something first (tutorials), answer it: you are ready only
     after that. @HOST@ presses Embark last, and the run starts.

During the run:
  - The AI talks in @HOST@'s window only. @HOST@ will read out what it says, and
    will pass on what you want to tell it.
  - On the map and at chests the AI lets you and @HOST@ choose first.
  - To leave, use the game's own menus: Back in the lobby, or Escape and then
    Disconnect in a run. Do not close the window, press Cmd+Q or let the Mac
    sleep in the middle. @HOST@'s game would keep your player as a ghost and
    get stuck.
  - If you drop out of a run you cannot get back into it, and the game warns
    that the others cannot keep playing either. To go on, @HOST@ has to save
    and quit, host the run again from his save, and then you join again.

To try again after a failed join: Ok on the popup, then Back, then Join.


IF IT DOES NOT CONNECT

A failed join ends in a popup titled "Disconnected". It holds the game's own
sentence and, below it, a line that starts with "SpireMind guest:". Find the
game's sentence in this list. The sentences are the English ones.

No line that starts with "SpireMind guest:" in the popup
    Press Ok, then Back twice: you are on the main menu. In a corner, by the
    version, it says "Running Modded. Loaded ... mods." Point at that text and
    it lists the loaded mods. (To try a join from there: Multiplayer, then Join.)
    - SpireMind is not listed: the mod is not loaded. Check step 2: the
      SpireMind folder sits directly in mods and holds SpireMind.dll. Check
      Settings, Modding, Mod Settings: the box of SpireMind is ticked. If the
      game asks "Load Mods?", press Load Mods. It closes; start it again.
    - SpireMind is listed: the Launch Options are not the whole line of step 3.
      Paste the line again, quit and start the game again. If the line was
      whole, only the extra line is missing: go by the game's sentence.

A popup titled "SpireMind" that lists problems
    The line in Launch Options has a mistake. The popup says which part.
    Quit, fix the line, start again.

"Connection timed out."  (the SpireMind line says "Nobody answered")
    The game waited ten seconds and got no answer.
    - Is @HOST@ sitting in his co-op character select right now?
    - Is the address in your Launch Options the one @HOST@ gave you today?
      It changes from place to place.
    - Are both Macs on the same Wi-Fi? Guest, office, hotel and cafe networks
      often stop devices from talking to each other. A phone hotspot that both
      Macs join works. @HOST@'s address is then another one.
    - Did you click Don't Allow at the local network question? Open System
      Settings, Privacy & Security, Local Network, and switch on Slay the Spire 2
      and Steam. Then start the game again.
    - Is a VPN on, on your Mac or on @HOST@'s? Switch it off for the session.
    - A test: open the Terminal app and type  ping -c 3 @PING_ADDRESS@
      If no line starts with "64 bytes from", your Mac cannot reach @HOST@'s
      at all, and the cause is the network or the address, not the game.
    - Ask @HOST@ whether his game saw you connect. If it did, the next entry
      is yours.

"Connection timed out."  (the SpireMind line says @HOST@'s game answered, "but
the join did not finish")
    The network is fine: @HOST@'s game saw you connect. The two games could not
    understand each other, and after ten seconds his game let go of you. Your
    mods are not the same as his. See step 1.

"Mod mismatch!" and a list of mods
    "We have mods that host does not" names yours. "Host has mods that we do
    not" names @HOST@'s. BetterSpire2 on both lines means two different versions
    of it. Any other mod of yours: switch it off. See step 1.

"The player you attempted to join is running a version of STS2 that does not
match yours."
    Your game is another version than @HOST@'s, or the mods differ: your BaseLib
    is another version than his, or one of you has a mod on that adds cards or
    relics. Compare the version in the main menu corner, and see step 1.

"You were kicked from the game."  (the SpireMind line says the lobby already
holds a player 2000)
    Wait fifteen seconds and try once more: Ok, Back, Join. If it says the same
    again, you left earlier without the game saying goodbye. @HOST@ has to back
    out of his lobby and host again. Then Back, Join.

"You cannot join this game, as it has already started."
    @HOST@ is in a run or has not closed his last one. Wait for him to say the
    lobby is open. Then Back, Join.

"The lobby is full."
    A lobby holds four players. @HOST@ has to send an AI player away.

"You cannot join this game, as it is loaded from save and you were not there
when it began."
    @HOST@ opened a saved run that your player is not in. He has to host a new one.

"Host left the game."
    @HOST@ closed his lobby, or saved and quit. Wait for him to say the lobby is
    open again. Then Back, Join.

A popup whose only button is Report Bug
("An unknown error occurred." or "An internal error occurred.")
    The connection to @HOST@'s game was lost, or the join broke off. No report
    is needed. The popup has no other button: press Report Bug, and on the
    screen that opens press Back without sending anything. Then tell @HOST@.
    When this comes in the middle of a run, you cannot get back into that run.

"Loading..." stays for more than half a minute, with no popup
    Press Back. Most likely your mods are not the same as @HOST@'s. See step 1.

The game starts with its logo and the normal main menu, and never tries to join
    The Launch Options are empty, were not saved, or their dashes got changed.
    Every option starts with two plain minus signs (--). A chat app can turn
    them into one long dash. Copy the line from this file. See step 3.

Any other sentence
    Ok, then Back, then Join. If it comes again, tell @HOST@ the words.

Still stuck? Send @HOST@ the game's log. Its lines that start with [SpireMind]
say what the mod did.
    In Finder choose Go, Go to Folder, and paste
    ~/Library/Application Support/SlayTheSpire2/logs
    Send the file godot.log from that folder. The game starts a new one at
    every start. If you started the game again after the failure, also send the
    newest of the files there that have a date in their name.


AFTERWARDS: BACK TO NORMAL

  1. Quit the game.
  2. Steam, right-click Slay the Spire 2, Properties: empty the Launch Options
     box, or put back what was in it before.
     This is the step that matters. While the line is there:
       - the game tries to join @HOST@ at every start and after every run;
       - your own Host button opens a local lobby, not a Steam one;
       - the game would mishandle a co-op run you saved earlier: it would not
         find you in it and would set the save aside as broken. That happens
         at Host from Save in the Multiplayer menu and, with BaseLib, as soon
         as that menu opens. The mod blocks both for that reason. With the box
         empty, all of it works as always.
  3. Tick your other mods again: Settings, Modding, Mod Settings.
  4. The SpireMind folder can stay in mods. Without the launch options it does
     nothing. To remove it, drag it out of mods into the Trash.

Your saves and progress:
  - You played on your normal profile, the one you use with your Workshop mods.
  - The run counts like any co-op run you joined: play time, statistics, what
    you discovered and unlocked, achievements.
  - Nothing of yours is reset, overwritten or copied. Your own saved runs are
    not touched. The save of this co-op run exists only on @HOST@'s Mac.
  - In your run history the players of this run show as "Test Host" (@HOST@),
    "Test Client 1" (the AI) and "Test Client 2" (you), not by their Steam names.
