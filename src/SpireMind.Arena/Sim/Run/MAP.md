# Run mode: the map

The map of an act, the path on it, the roll of an Unknown room and the map screen. Chunk 3 of the run-mode plan (`sts2-notes/run-mode-v0.107.1/plan.md`, sections 1.3, 1.4 and the `map` row of section 2). The frame of a run is in `README.md`, the seed and the streams in `STREAMS.md`, next to this file.

Everything here is the game's (v0.107.1, single player, ascension 0) and is held against the real game: five saved maps are rebuilt point for point, and three whole real runs are walked on the generated maps with every Unknown room coming out as it did in the game.

## Files

| File | Holds |
| --- | --- |
| `ActMap.cs` | `MapPointType`, `MapCoord`, `MapPoint`, `MapCounts`, `ActMap` (a finished map), `SlotSet` (a set that lists as the game's sets do) |
| `ActMapGenerator.cs` | `ActMapGenerator` (the game's generator), `ActMapRules` (what an act gives it) |
| `MapState.cs` | `MapState` (the run's part: map, path, travel, entering a point, the Unknown roll, the `boss` key), `MapArrival`, `MapVisit`, `IMapDirector`. A room's kind is `RoomKind` (`../Engine/RunContext.cs`), the game's room type numbers |
| `MapScreen.cs` | the `map` screen as the mod's MapReader shows it |
| `MapGoldenData.cs`, `Tools/golden_maps.py` | the real saves' maps and the real runs' paths, and the script that makes the file from `sts2-notes/golden-saves` |
| `MapChecks.cs` | the checks |
| `MapSecondReading.cs`, `GenerationChecks.cs` | a second reader's own port of the generator, for checks only, and the checks that hold the two against each other (below, "A second reading") |
| `../Engine/GameRng.cs` | two additions: `NextGaussianInt` (the rounded normal draw) and `StableShuffle` (sort, then shuffle) |

## Using it

```csharp
// In a director's Begin, after RunWorld.Begin(run.State):
var map = MapState.BeginAct(run, 0);              // the act's map is generated; nothing is visited
var arrival = map.Enter(run, map.Map.Start.Coord); // the game enters a first act's start point itself: Neow's room (an Event room)

// When a room is finished (AfterRoom):
run.CloseAll();
run.Push(new MapScreen());                         // map:<n> enters next[n] and calls IMapDirector.EnterRoom(run, arrival)

// In IMapDirector.EnterRoom: open the room arrival.Room names, then
MapState.MarkRoomVisited(run, arrival.Room);       // after the room took its entry of the act's lists

run.State.Part<MapState>()                         // everywhere else: Map, Visited, History, Current, Next(run)
MapState.AddBoss(state, run);                      // the "boss" key of map, rest, shop, rewards and card_reward
```

- `MapState.BeginAct(run, actIndex)` is the game's set-up of an act: the act index, the act floor back to 0, the path cleared, the Unknown odds back to their start values, the map generated on `act_<n>_map`. It enters nothing. In a first act the game enters the start point at once; in a later act the map opens with the start point as its one option.
- `MapState.Enter(run, coord)` is the game's step onto a point: the place joins the path, `ActFloor` becomes the row plus 1, the streams are told the place (`Streams.MoveTo`), the room is decided, the visit is recorded, `TotalFloor` goes up by one. It refuses a place that is not a travel option now. It does not move the lists' counters: the room takes its list entry first (`ActRooms.NextNormalEncounter` and so on), then `MapState.MarkRoomVisited`.
- `MapState.Next(run)` is the list the mod shows as `next`: sorted by row, then column. Nothing visited: the start point. On the last row of the grid: the boss. On the boss: nothing. Otherwise the children, or the whole next row while a relic allows free travel.
- `MapState.AddRoom(room)` records a second room on the point the player stands on (a fight an event started). A shop among the rooms of the point before bars Shop for an Unknown point.
- A director that travels on the map implements `IMapDirector`. `EnterRoom` gets a `MapArrival` (place, kind of point, kind of room) with every screen closed. `CanHold` is the seam of a house rule (below) and answers true unless a director says otherwise.

## The generator

`ActMapGenerator.Generate(world.Streams.ActMap(actNumber), "Overgrowth")`. The stream is the act's own (`act_1_map`), a new one for every map. The steps, in the game's order:

1. **The counts.** Rest Sites first, then Unknown rooms. A first act: a rounded normal draw around 7 kept to 6 or 7, then one around 12 kept within 10 to 14. Hive: around 6 kept to 6 or 7, and one Unknown room less. Glory: a plain 5 or 6, and one Unknown room less. Shops 3, elites 5. A normal draw takes two numbers and is drawn again while it is out of range.
2. **Seven paths** from the first row to the last. A path starts in a drawn column (the second path is drawn again until it starts elsewhere than the first). Each step shuffles the three directions (sorted first, so always two draws) and takes the first that does not cross a diagonal already there. Then the last row is linked to the boss and the start point to the first row, both by column.
3. **The kinds.** The last row is Rest Sites, the row seven below the grid's height Treasure, the first row Monster rooms; those cannot be modified later. The counted kinds are queued (Rest Sites, shops, elites, Unknown rooms) and handed out in at most three passes: the free points in sorted-then-shuffled order, each taking the first queued kind allowed there; kinds looked at and refused go to the back of the queue. What is still free becomes a Monster room. The placement rules: no Rest Site in the three top rows; no Rest Site and no elite below row 6; an elite, a Rest Site, a Treasure room or a shop never directly before or after its own kind; two children of one parent never both Rest Sites, both Unknown, both elites, both shops. (The game's list of kinds that may not be siblings names the Monster room too, and the port tests it as written, but nothing ever asks: no Monster room is in the queue, the repair puts none back, and what is left free becomes a Monster room without a question. Two Monster rooms under one parent are common. The fixed rows are not asked either: the last row's Rest Sites are siblings wherever a point of the row below leads to two of them.)
4. **Prune and repair**, up to three rounds. Every way from the start point to the boss is walked, children in set order. A stretch starts at a fork (or at the start point), ends at a join and is at least three points long; two stretches match when they have the same ends and the same kinds and share no inner point. The groups of matching stretches are taken in the plain character order of a text key (`col,row-col,row-kinds`), each group is shuffled as it stands, and all but one of its stretches are pruned (points with one parent and one child that nothing else needs are taken off the map); when nothing could be pruned, a link is cut instead. After every change everything is found again. Then shops, elites, Rest Sites and Unknown rooms that fell short are put back on Monster rooms in sorted-then-shuffled order where the placement rules allow: all four kinds in every round, also after an earlier kind was put back (the game joins the four answers with an "or" that does not stop at the first yes).
5. **Three sideways passes** that draw nothing: the map is centred when two outer columns on one side are empty; points of a row move apart within one column of their parents and children; a point with one parent and one child that sticks out moves back in line. The mod prints the columns after these passes.

What an exact port had to keep, beyond the rules:

- every draw, also one whose answer is forced, and the game's two shuffles (one sorts first);
- the order a point's children are listed in. The game keeps them in hash sets and walks them; a .NET hash set lists in the order of adding, and a removed item leaves a free place that the next added item takes. `SlotSet` is exactly that. In this generator no link is added after one was removed, so only the order of adding and the gaps ever show; the reuse of free places is there because it is the set's rule;
- the order of the groups: character order of the key, so a key starting "10," comes before one starting "2,";
- a quirk: the pruning pass asks whether a parent "is removed" by looking at the grid cell of its place, and the start point was never in the grid, so it counts as removed;
- the order of "all map points": column by column, each column from the bottom up.

A map takes about 260 numbers and 10 ms to make. `ActMap.Draw()` prints one for a maintainer; `ActMap.Describe()` is the one-line text that goes into a run's dump.

## Entering an Unknown point

One number on `unknown_map_point` against odds that grow (`UnknownRoomOdds`, `STREAMS.md`): `Draw(RunStream.UnknownMapPoint, Purpose.UnknownRoom)`, after the streams were told the place, so keyed mode gives every node its own number. Shop is barred when a room on the point before was a shop, or when the point has children and every one is a shop. Then the run's listeners take away what they bar (`RunHooks.ModifyUnknownMapPointRoomTypes(run.Context, ...)`). The odds go back to their start values with every act; the stream goes on.

**House rule (plan 5.1), off by default.** A director can say that the arena cannot show a kind of room right now (`IMapDirector.CanHold`: an Event room while no built event is allowed and unmet). The roll is then made again from the same stream with that kind barred, which is the game's own roll with a kind taken out: with the Event out, a number above every bar gives the lowest kind left, a Monster room (and an elite if the Monster room is out too, so a director is asked about that as well). The refused number is used up; the odds move once, for the roll that counted. When every kind is refused the roll throws, and the travel action is undone. A run that must be the game's own does not use this.

## The screen

`MapScreen` is the mod's `map` snapshot: `run`, `player` (no potions), `potions`, `boss`, `at`, `next`, `map` in that order; `map:<n>` labelled "Go to Rest Site at (3,11)", then the potion ids. `rows` holds the point the player stands on, everything that can be reached from the options, and the boss; visited points are in `visited`. `ahead` gives, kind by kind, the fewest and the most rooms on a way from the option to the end of the grid. Refusals: `Unknown map action '<id>'.` and `There is no map option <n>.`

Standing on the boss the mod's snapshot is a waiting one (`nowhere left to travel on this map; close the map to continue`) with the state and no action. The screen shows that faithfully; a director never opens the map there, it ends the act.

One place where the docs and the game disagree: `docs/screens.md` writes the last row of its example as `c2:rest c3:rest c4:rest`. In the game every point of the last row has the boss as its child and the reader prints every child, so the real text, and the arena's, is `c2:rest^3 c3:rest^3 c4:rest^3`.

The potion block and the potion ids come from the potion actions outside fights, which are the rewards chunk's: three call sites in `MapScreen.cs` (`Describe`, `Legal`, `TryExecute`).

## Hooks

The map sends two of the game's hooks, both declared on `Listener` with the run's other hooks and sent through `RunHooks` (`RELICS.md`): free travel (`RunHooks.ShouldAllowFreeTravel(run.Context)`: Winged Boots overrides `ShouldAllowFreeTravel(RunContext c)`; listeners after the first yes are not asked) and the kinds an Unknown room may be (`RunHooks.ModifyUnknownMapPointRoomTypes(run.Context, kinds)`: Juzu Bracelet overrides `ModifyUnknownMapPointRoomTypes(RunContext c, IReadOnlySet<RolledRoom> roomTypes)` and returns a new set). They ask the run's listeners in the run's one order (`run.Listeners()`: listening deck cards, relics in pickup order, potions). Winged Boots also needs the path: `MapState.Visited` and `Map.Point(coord).Children` are what its own rule reads.

## Checks

`MapChecks`. The evidence for the generator is the real game's (`MapGoldenData`):

- **Five saved maps** (seeds KMEHXZW0R5, EEXRY5WA1R, E9U400ERJ4, 1BUSLN0FY5 in Overgrowth, F30CH975SF in Underdocks) are rebuilt exactly: every point, its kind, whether it can be modified, its children in the save's own listing order, the start point, the boss, and the first-row points in the order the save lists them. With the folder `sts2-notes/golden-saves` on the machine (or `$ARENA_GOLDEN_SAVES`), every `*.save` in it is compared too.
- **Three real runs** from the game's run history: one player through three acts (the second act had Spoils Map's own map and is skipped), two players at ascension 7 through three acts (a row less, eight elites, Juzu Bracelet from the tenth point on), two players through one act. In every act the generated map holds exactly one way with the kinds the run met, and walking it gives every room the game recorded: 21 Unknown rooms in all. That is the evidence for Hive's and Glory's rules, for the roll across acts, the Shop bar and the relic hook.
- **Worked by hand:** the sorted shuffle, the normal draw (against the rule written out once more, and the shares of the normal curve), each act's numbers, the set order (a hand sequence, and 5,000 steps against the runtime's own hash set), the rules every map keeps on 400 seeds, travel and the Unknown roll on small maps drawn in the checks (the numbers behind them are known from the real runs), the worked example of `docs/screens.md` key for key, every screen of a tour up the real map of seed 1BUSLN0FY5 written down from the save, refusals, copies and replays in both stream modes.
- **The passes after the kinds are placed, on maps drawn by hand** (`ActMapGenerator.Finish` runs them on a drawn map): which of two or three equal stretches is pruned for which draw, the point that is left alone because its parent has no other child, the link that is cut when no point can go, the second round after a repair made two stretches equal, the order and the limits of the repair, and the three sideways passes step by step (moving apart, a second turn, the lower of two equal columns, back in line, a taken cell, the map moved towards two empty columns). These are the branches the real maps happen not to reach.

What the real maps do not reach was read a second time (chunk 7 of the plan), and each point is now held by a check or by a reason:

- **Hive's Rest Site draw** (around 6, kept to 6 or 7) rests on the reading of the act's class alone, and both readings agree on it. The one real run through a standard Hive map cannot tell: drawn around 7 the same numbers give 7 Rest Sites instead of 6, and the seventh lands on one point, (5,9), that the run did not pass (`GenerationChecks`, "what a walked path cannot see"). The same goes for every single number of Hive's and Glory's rules (one Unknown room less, Glory's plain 5 or 6): a walked path shows that the numbers were drawn as the port draws them, since another draw would shift every later number and the path would not be on the map at all, but not what each number was. A saved map of a second and of a third act would settle it.
- **The repair's "or"**: stopping at the first kind that was put back gives another map on about 2 seeds in 1,000 (the later kind is then put back a round later, after another pruning, or not at all once the three rounds are used up). A map drawn by hand with all four kinds short tells the two apart (`MapChecks`, "repair: all four kinds are put back in one round"), and so does the second reading on 1,000 seeds.
- **Six tests of the pruning pass cannot decide** (next section): three that never came up in 3,000 generated maps and three more.

After a game update: run `python3 -I Sim/Run/Tools/golden_maps.py <golden-saves> Sim/Run/MapGoldenData.cs` on saves of the new build; read `MegaCrit.Sts2.Core.Map/` again, and hold `MapSecondReading.cs` against it too.

## Six tests of the pruning pass that cannot decide

The pruning pass holds six tests that never change a map. The port keeps every one as the game has it, and no map can show whether it is there: a mutation of any of them passes every check. `GenerationChecks` counts how often each decided on 1,430 maps (never) and makes 200 maps without all six (the same maps). Why they cannot:

Three facts. (1) The stretches of one group have the same first point and the same last point and no other point in common: a stretch joins a group only when it shares no inner point with a member, and stretches of one key are equally long. (2) A group is always looked at on the map it was found on: everything is found again after every change, and a group that pruned nothing and cut nothing left the map untouched (by (c) below a stretch never gives up after it took a point off). (3) The pass stops before the last stretch of a group once all the others are pruned; it reaches the last one only when an earlier stretch lost nothing.

- (a) *"This point of the stretch is no longer on the map: count the stretch as pruned."* Earlier stretches of the group take off only their own points, and the one point below the end that they share with this stretch is the group's first point. That one is never taken off while the group is looked at: it still has two children then, this stretch's and that of a later stretch or of an earlier one that lost nothing, and a point with two children is passed over.
- (b) *"A parent with no other child stands in the way, unless that parent's own cell is empty."* A cell is empty under a pruned point, which is nobody's parent any more, and under the start point, which never stood in the grid. So the excuse could only help a first-row point, and that one is looked at either as the first point of its group, where it is passed over for its two children before this test is reached (a), or as the second point of a stretch from the start point, which by the same count has two children then and is no "parent with no other child".
- (c) *"The stretch's end has one parent left: give up on the stretch."* The end still has this stretch's last inner point as a parent, and that of a later stretch or of an earlier one that lost nothing: two at least.
- (d) *"A stretch starts at a point with more than one child, or on row 0."* Row 0 holds the start point alone. When it has one child, every stretch from it runs through that child, and two stretches with a common inner point are never in one group.
- (e) *"A stretch ends at a point with two parents or more."* Two stretches of a group reach their end from two different points, so an end with one parent never closes a group of two; a group of one is never pruned.
- (f) *"Unless a child outside the stretch would be left without a parent."* The point has passed "not more than one child" just before, and its one child is the stretch's next point.

The game's lines: `MapPathPruning.cs` 294-297 (a), 298 with 342-345 (b), 305-308 (c), 181 (d), 188 (e), 309 (f).

## A second reading

`MapSecondReading` is the generator once more, written from the game's code by a reader who had not written `ActMapGenerator`, for checks only. It keeps a point's links in the runtime's own hash sets and the groups in the runtime's sorted dictionary, where the port has `SlotSet` and its own walk; its shuffles and its normal draw are written out again. `GenerationChecks` makes every map with both and compares the texts (`ActMap.Describe()`: every point, its kind, whether it can be modified, its children in listing order, the start point, the boss, the points the paths started from) and the number of numbers taken:

- 1,000 Overgrowth seeds, and Underdocks, Hive and Glory; each act with a row less (two players), Overgrowth with eight elites (an ascension level) and without the pruning pass: 1,430 maps, no difference.
- The same maps against the placement rules as the second reader took them from the game's code (the grid, the three fixed rows, where a counted kind may stand and what it may stand next to, links one row up and at most a column sideways, every point with a parent and a child, never more of a kind than its number).
- **How much a walked path proves.** `MapChecks` finds exactly one way with the kinds a real run met on the map of its seed. Such a way is rare on any other map: 1 of 2,400 maps of other seeds held one (40 other seeds an act are in the check). So each of the six acts of real runs is strong evidence for the generator as a whole, Hive's and Glory's rules included; what it cannot see is the kind of a single point off the path.

The second reading has a shortcut of its own, proved in its comment: a stretch that lies wholly in the part a way shares with the way before it is not looked at twice.

## Left out, by name

- A second boss, more than one player and the ascension levels, as rules of a run. The generator takes them as changed rules (`ActMapRules`: a row less, eight elites), and real runs of that kind are among the checks.
- The maps other things make: Golden Compass's and Spoils Map's own maps, the quest marks Spoils Map and Fur Coat put on points (`(quest)` in the rows). None can touch a first act's map.
- The `ancient` key, which the game shows only on the first map of a second or third act, and the titles of the bosses of those acts: `MapScreen` and `MapState.BossTitle` refuse by name instead of guessing.
- Listeners of the map's hooks that the arena has not built: the card Lantern Key, the run modifiers Flight and Deadly Events.
- A run that starts without Neow (the game retypes the start point to a Monster room) and the fixed Unknown rooms of a profile's very first run.
- The mod's waiting states that need a human's hand: a map opened in an unfinished room, travel not enabled yet, drawing mode, co-op votes.
