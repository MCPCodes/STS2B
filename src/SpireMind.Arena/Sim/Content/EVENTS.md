# Events as content

Chunk 15 of the run-mode plan (`sts2-notes/run-mode-v0.107.1/plan.md`, 5.2 and 5.3; `events.json`): the first seven events of the game and the two relics one of them leads to. What an event is written against (the room, the screen, the list walk, the order on entering) is `Sim/Run/EVENTS.md`; relics are written against `Sim/Run/RELICS.md`. Everything here is the game's (v0.107.1, Ironclad, ascension 0, single player), read from `MegaCrit.Sts2.Core.Models.Events/<Name>.cs` and `Models.Relics/SwordOfStone.cs`, `SwordOfJade.cs`, unless a line says "house rule".

## Files

| File | Holds |
| --- | --- |
| `EventsA.cs` | the module `EventsA`; the events `AromaOfChaos`, `JungleMazeAdventure`, `MorphicGrove`, `SunkenStatue`, `TabletOfTruth`, `ThisOrThat`, `UnrestSite`; the relics `SwordOfStone`, `SwordOfJade` |
| `EventsAChecks.cs` | their checks |
| `Tools/event_rolls.py` | what the events roll on a seed, worked out apart from the arena: the source of the numbers typed into the checks |
| `EventsLeftOut.cs` | the fourteen events of an Overgrowth act that are still left out by name, each with its chunk |
| `../Run/EventList.cs` | when each event is allowed (`EventList.IsAllowed`): not in the event classes |

## The seven

| Event (the game's title) | Allowed | Rolls at its start (the event's own stream) | Options, in the game's order |
| --- | --- | --- | --- |
| Aroma of Chaos | always | nothing | **Let Go**: choose 1 card on the transform screen, it becomes a random card (event stream). **Maintain Control**: choose 1 card on the upgrade screen, it is upgraded. |
| Jungle Maze Adventure | always | two numbers: Solo Quest's gold is 150 plus a fraction from -15 up to 15, Join Forces' gold 50 plus another | **Solo Quest**: lose 18 HP, then gain the gold (135 to 164). Deadly at 18 HP or less. **Join Forces**: gain the gold (35 to 64). |
| Morphic Grove | 100 gold or more, and two transformable cards | nothing | **Group**: lose ALL gold, then choose 2 cards on the transform screen; each becomes a random card (event stream), one after the other. **Loner**: gain 5 max HP. |
| The Sunken Statue | always | one number: the gold is 111 plus a whole number from -10 to 10 | **Grab the Sword**: obtain Sword of Stone. **Dive into the Water**: gain the gold, THEN lose 7 HP. Deadly at 7 HP or less. |
| Tablet of Truth | always | nothing | **Decipher**: lose 3 max HP, upgrade 1 random card (event stream). The next pages offer it again for 6, 12 and 24 max HP, and a fifth time for all max HP but 1, which upgrades every card and ends the event. Deadly while max HP is at or below the price. **Smash** (first page): heal 20. **Give Up** (later pages): leave. |
| This or That? | always | one number: the gold, 41 to 68 | **This**: lose 6 HP, then gain the gold. Deadly at 6 HP or less. **That**: obtain a random relic, THEN the curse Clumsy is added. |
| Unrest Site | HP at or below 70% of max HP | nothing (it notes the HP that is missing) | **Rest Anyways**: heal the noted amount, then the curse Poor Sleep is added. **Kill the Trees**: lose 8 max HP, THEN obtain a random relic. Deadly at 8 max HP or less. |

Five of the seven are always allowed. A new player (99 gold, full HP) is allowed fifteen of the twenty-one events of an Overgrowth act, so with these seven built the first Event room of a run holds the game's own event for one seed in three (the check counts 600 seeds).

## The titles are the game's, and not what the class names suggest

The event and option titles are read from the game's English text table (`localization/eng/events.json` inside `Slay the Spire 2.pck`, plain text; grep it for `"THIS_OR_THAT.` and the like). Several differ from the names in the code:

| In the code | The title |
| --- | --- |
| `ThisOrThat` | "This or That?"; its option `PLAIN` is "This", `ORNATE` is "That" |
| `SunkenStatue` | "The Sunken Statue"; `GRAB_SWORD` is "Grab the Sword", `DIVE_INTO_WATER` is "Dive into the Water" |
| `UnrestSite` | `REST` is "Rest Anyways", `KILL` is "Kill the Trees" |
| `TabletOfTruth` | Decipher is "Decipher" on the first page, then "Continue Deciphering", "Keep Deciphering", "KEEP DECIPHERING" and "Lose Everything" |

An option's `Key` is its name in the game's table (`PLAIN`, `ORNATE`, `DECIPHER_1`, ...), which is what the game's run records write (`THIS_OR_THAT.pages.INITIAL.options.ORNATE.title`).

Every other text is the arena's own. An option's text carries the game's numbers (a "Lose 6 HP" stays 6 when Tungsten Rod will take 1 off, as in the game). A finished page says what the option did with the numbers that really moved, each taken at the moment it happened: "you lost 5 HP and gained 61 Gold", "Strike became Body Slam", the relic obtained by name.

## What to know when reading them

- **Each class is the game's class method by method**: `CalculateVars`, `GenerateInitialOptions`, one method per option under the game's name (`Plain`, `Ornate`, `DontNeedHelp`, `SafetyInNumbers`, `LoseMaxHpAndUpgrade`, `GetDecipherCost`, ...). The order of the lines inside a method is the game's and it shows: The Sunken Statue pays before it hurts, This or That? and Jungle Maze hurt before they pay, That gives the relic before the curse, Kill the Trees takes the max HP before the relic.
- **"Obtain a random Relic"** (`EventsA.ObtainRandomRelic`) is the game's `RelicFactory.PullNextRelicFromFront(player)`: one number of the player's `rewards` stream for the rarity (`Purpose.RelicRarity(0)`), the front of the player's bag, obtained at once. What the pull did not take from the game goes into the run's log (kind `relic`, "the relic of This or That?" or "the relic of Unrest Site"; the game's relic of a rarity that was rolled again is logged too). A strict run whose relic is not built ends as unrepresentable, standing as before the option.
- **A transform is rolled on the event's own stream**, by the game's rule for the card (`DeckEdits.TransformToRandom`): an Ironclad card becomes one of the 80 Common, Uncommon and Rare Ironclad cards a single-player run can hold (all built), a curse one of the 17 other curses (all built), Neow's Fury a colorless card (drawn again while it lands on one that is not built, logged). The new card goes to the END of the deck. Morphic Grove transforms its two cards one at a time, in the order the screen hands them back, which is the order they were picked in.
- **The card choices cannot be backed out of** (the game's screens have no close button here), and a choice with no more candidates than it asks for is not shown: one transformable card at Aroma of Chaos, or two at Morphic Grove, are simply taken. With no candidate nothing happens and the event finishes all the same (Morphic Grove's gold is lost for nothing).
- **Morphic Grove loses the gold before the choice is asked**, so the choice's own snapshot shows 0 gold.
- **Jungle Maze Adventure** adds a 32-bit fraction to 150 and to 50 as a decimal; gold is paid in whole numbers with the fraction cut off, and the game's own text shows that whole number too. Solo Quest first shuffles the game's three sound effects on the event's stream: TWO numbers (a shuffle of three), which decide nothing but move the stream.
- **Tablet of Truth's last price** is "max HP minus 1" worked out when its page is made (after the fourth Decipher). Max HP gained while that page is open (Fruit Juice) stays. The pick of the card to upgrade takes a number also when only one card can be upgraded, and none when none can. The game's branch for a Decipher that costs all the max HP there is (max HP to 1, then the player is killed) is ported; the screen never offers such an option.
- **Unrest Site heals what was missing when the event started**, not what is missing when Rest is chosen: a potion drunk on the page leaves less to heal, and the heal stops at max HP.
- **Losing max HP** (Tablet of Truth, Unrest Site) takes the HP above the new maximum as damage first, with the damage hooks, then lowers the maximum (`run.LoseMaxHp`).

## The two swords

| Relic | What it does | Hook |
| --- | --- | --- |
| Sword of Stone (Event rarity; The Sunken Statue) | counts the Elite fights won while it is held; at the fifth it is replaced by Sword of Jade, which stands in its place among the relics | `AfterCombatVictory` |
| Sword of Jade (Event rarity) | 3 Strength when a fight's room is entered, with no applier | `AfterRoomEntered` |

- The count is the relic's own field and goes from room to room with it. The game shows it on the relic's icon and its text does not name what the sword turns into; the arena's text does both ("After 5 won Elite fights it turns into Sword of Jade (...). Won so far: 2."), because the mod's snapshot has no place for a relic's counter.
- An Elite fight is a fight whose room type is Elite (the encounter's own). A boss or a hallway fight counts nothing; a lost fight counts nothing.
- Neither sword is in a relic bag: no pull, chest or shop gives one. Both are on the list of reachable relics with the source Event.

## Left out by name

- The other fourteen events of an Overgrowth act (`EventsLeftOut.cs`, each with its chunk).
- More than one player: Jungle Maze Adventure and Morphic Grove are shared events (one vote for all, one stream without the player's slot); the others give every player an own copy on a stream of the own slot. `SunkenStatue.RollGold` takes any stream because the one real record of this event is a co-op run.
- What the events write into the game's run history, the sounds and effects they play, and Aroma of Chaos's flavour line for the character.
- Sword of Stone's counter as a number on the relic: it is in the relic's text.

## Checks

`EventsAChecks`. What the expected values rest on:

- **Prices, amounts and their order**: worked out by hand from each event's class. The order inside an option is held by a relic of the checks that writes down every gold gain, HP loss and card arriving in the deck with the state at that moment. Two more relics of the checks hold what no state of a real run shows: one that costs HP on entering an Event room (Unrest Site heals the amount it noted at its start, which is before a relic acts on entering), and one picked up after Sword of Stone that looks at the relics at the hook before the victory hook (the sword turns in the victory hook itself) and at who applied the player's Strength (nobody, for Sword of Jade).
- **What an event rolls on a seed**: a reading of the game's generator and string hash that is not the arena's (`Tools/event_rolls.py`, which shares no code with the arena and reads the option lists of a transform out of the decompiled game's own card pool files when it runs). Its results are typed into the checks: the gold of This or That?, The Sunken Statue and Jungle Maze Adventure (as rolled, to the last digit of the decimal) on seven seeds, Tablet of Truth's four random upgrades, what a Strike, Bash, Clumsy and Neow's Fury become at Aroma of Chaos, Morphic Grove's two new cards.
- **The real game**: the event This or That? of Mark's run on seed 1BUSLN0FY5 (the relic's rarity is the 26th number of `rewards`, the relic Stone Cracker, the front of the saved Uncommon list; the curse Clumsy), and the record of a co-op run of v0.108.0 in which both players dived at The Sunken Statue for 112 and 121 gold: the seed gives exactly these to the first and the second player. With the folder of run records on the machine (or `$ARENA_GOLDEN_SAVES`) every recorded choice at one of these events is compared.
- **The screens**: every first page key for key against `docs/screens.md` ("Event"), deadly options listed and refused at their boundary and offered one HP above it, the card choices' prompts, ids and labels, potions on a page.
- **Copies and replays** of every way through the seven events, a copy taken while a card choice is open included; **1,050 rooms** (150 per event; `ARENA_EVENT_SOAK=<n>` for n per event, which also prints what was counted) with every kind of player (1 to 80 HP, max HP down to 1, decks with curses, Eternal cards and nothing to upgrade, potions, Tungsten Rod, both stream modes) walked by a scripted visitor: no action turned down, every room left alive, nothing unbuilt ever held, every visit replays.
- **The whole act**: `RealRunChecks` plays the real run through its event room on the screens and walks whole acts whose Event rooms hold these events.

A mutation pass over `EventsA.cs` held the checks to account: 137 one-line changes (every number, every roll, the order of every option's steps, each deadly mark, each title, the hooks and the count of the swords, the pull of the random relic and its log), each run against `EventsAChecks` and `RealRunChecks`. All 137 are caught; six of them were not at first and got a check of their own (the pull's own strict path, scaling in 64 bits, Decipher's deadliness by max HP, the sword's hook, the heal noted at the start, Sword of Jade's applier).

What no check can tell apart, because nothing hears it: that Tablet of Truth lowers max HP before it upgrades; which purpose key the random relic's rarity has in keyed mode (it is `Purpose.RelicRarity(0)`, and no real value exists to hold it to).

After a game update: read the seven event classes and the two relic classes again; grep the pack for the titles; run `python3 -I Sim/Content/Tools/event_rolls.py <decompiled game folder>` and compare what it prints with the numbers typed into `EventsAChecks.cs` (they change when a card pool's order or a card's rarity changed); run `EventsAChecks` (the typed-in rolls fail when the generator, the hash or an event's draws changed).
