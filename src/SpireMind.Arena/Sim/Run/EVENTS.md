# Run mode: events and Neow

The event room and its screen, which event an Event room holds, the Ancient's room, and Neow's offer of three relics. Chunk 9 of the run-mode plan (`sts2-notes/run-mode-v0.107.1/plan.md`, 1.2, 1.4, 1.5 and the `event` row of section 2; `events.json`, `neow.json`). The frame of a run is in `README.md`, the streams and the act's lists in `STREAMS.md`, the map in `MAP.md`, next to this file.

Everything here is the game's (v0.107.1, single player, ascension 0), read from its code: `MegaCrit.Sts2.Core.Models/EventModel.cs` and `AncientEventModel.cs`, `Core.Events/EventOption.cs`, `Core.Rooms/EventRoom.cs` and `RoomSet.cs`, `Core.Models/ActModel.cs` (PullNextEvent), `Core.Runs/RunManager.cs` (the room's creation), `Core.Models.Events/Neow.cs` and every event's own `IsAllowed`, `Core.Nodes.Rooms/NEventRoom.cs`. The screen is the mod's (`docs/screens.md`, "Event"; `src/SpireMind/Game/Screens/EventReader.cs`).

This page is the room: what events and Neow's relics are written against. The only event in this folder is Neow; the first seven events of the act's list are in `../Content/EventsA.cs` (`../Content/EVENTS.md`), and the first 14 of Neow's relics in `../Content/NeowCore.cs` (`../Content/NEOW-CORE.md`).

## Files

| File | Holds |
| --- | --- |
| `Events.cs` | `RunEvent` (one event), `EventOption`, `AncientEvent`, `ICardTips`, `EventsState` (the run's part: events met, the event of the room), `EventScreen` |
| `EventList.cs` | `EventList`: when each event is allowed, the walk of the act's list, `CanHold`; `EventPull` |
| `Neow.cs` | `NeowRelics` (the game's lists), `NeowOffer` (the roll), `Neow` |
| `../Content/EventsLeftOut.cs` | the events of an Overgrowth act that are not built, each left out by name |
| `EventsChecks.cs` | the checks |
| `../Engine/Registry.cs`, `../Completeness.cs` | events are content of the registry (`ContentKind.Event`, `r.Event<T>()`, `NewEvent`), with a row in the completeness table |

## Using it (a director)

```csharp
// The act's start point, after MapState.BeginAct and map.Enter(run, map.Map.Start.Coord):
EventScreen.Open(run, rooms.Ancient!);                 // "Neow"
MapState.MarkRoomVisited(run, RoomKind.Event);         // her room counts as an event room: the counter is 1 now

// IMapDirector.EnterRoom, for arrival.Room == RoomKind.Event:
EventScreen.Open(run, EventList.Pull(run));            // the act's next event; throws RunUnrepresentableException when it must
MapState.MarkRoomVisited(run, RoomKind.Event);

// IMapDirector.CanHold:
room != RolledRoom.Event || EventList.CanHold(run)
```

- `EventScreen.Open` enters the room as the game does and sends "after room entered" itself, at the game's moment. Do not call `run.AfterRoomEntered` for an event room.
- Leaving is the event's own `option:<n>` (labelled Leave). It takes the screen away and calls `run.RoomFinished()`, so the director's `AfterRoom` opens the map.
- `EventScreen.Current(run)` is the event of the room the run is in (null outside one). It lives in the run's `EventsState`, so it is copied with the run and is in the run's dump.

## Writing an event

```csharp
public sealed class ThisOrThat : RunEvent              // the class name is the game's
{
    private int _gold;
    public override string Title => "This or That?";   // the game's English title, from its text table (not the class name)
    protected override void CalculateVars(Run run) => _gold = Rng.NextInt(41, 69);
    protected override string InitialDescription(Run run) => "...";            // our own words
    protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) =>
    [
        Option("PLAIN", "This", $"Lose 6 HP. Gain {_gold} Gold.", Plain).ThatDoesDamage(6),      // key PLAIN, title "This"
        Option("ORNATE", "That", "Obtain a random Relic. Add Clumsy to your Deck.", Ornate).WithCards("Clumsy"),
    ];
    private void Plain(Run run)
    {
        run.Damage(6);
        if (run.IsOver) return;                         // a death has put up game over
        run.GainGold(_gold);
        SetEventFinished("...");
    }
}
// and in the chunk's content module: r.Event<ThisOrThat>();  (take its line out of Content/EventsLeftOut.cs)
```

- **The methods have the game's names**: `BeforeEventStarted`, `CalculateVars`, `GenerateInitialOptions`, `AfterEventStarted`, `OnEventFinished`, `SetEventState(text, options...)`, `SetEventFinished(text)`. Port an event method by method. `Rng` is the event's own stream.
- **An option**: `Option(key, title, text, method)`. The key is the option's name in the game's text table (for logs; it is what the game's run records write). The title is the game's English text for that key, read from the game's pack: the key and the class name mislead (`PLAIN` is titled "This", `ORNATE` "That", the event "This or That?"; `../Content/EVENTS.md` has the seven that are built). The text is ours, with the live numbers. `LockedOption(key, title, text)` is an option whose requirement is not met (the game: an option with nothing to run).
- **Deadly options**: `.ThatDoesDamage(n)` (would kill at or below n HP), `.ThatDecreasesMaxHp(n)`, `.ThatWillKillPlayerIf(method)`. They are asked every time the page is read. The mod never offers an option that says it would kill, so declare what the game declares, no more and no less.
- **Tooltips** (a card named here must be built: it is looked up when the option is made): `.WithCards("Clumsy", "Bash+")`, `.WithCard(deckCard)`, `.WithRelicTip("SwordOfStone")` (the game's `HoverTipFactory.FromRelic`: the relic, then the cards its own tooltip shows), `.WithCardTipsOf(relic)` (`FromRelicExcludingItself`). The snapshot lists up to three different cards and one relic, as the mod does. A relic whose tooltip shows cards implements `ICardTips`.
- **What an option runs is a method of the event**, taking the run (and the option, for `option.Argument` set with `.WithArgument(n)` on options made in a loop). A function that caught a local variable is refused when the option is made: an event is copied with its run, and the copy's options are bound to the copied event.
- **Everything the event rolls or counts is in its fields.** Numbers, texts, deck cards and relics are copied right by themselves; a list or anything else needs `OnCloned`, and the registry fails the self-check on a field that would be shared.
- **Changing the player**: the run's commands and nothing else (`run.Damage`, `Heal`, `GainMaxHp`, `LoseMaxHp`, `GainGold`, `LoseGold`, `ObtainRelic`; `DeckEdits`; `RunPotions`; `RunRelics` for a random relic; `RunRewards.OfferCustom` for a rewards screen; `CardSelect.*` for a choice). Test `run.IsOver` after anything that can kill.
- **A card choice in an option** works as everywhere outside a fight: the method is abandoned at the choice and run again from the start with the answer, on a copy of the event taken before the action. So what it did before the choice (a roll, gold paid) happens once.
- **A rewards screen in an option** is put over the event and the method goes on at once. Whatever the game does after the rewards screen has closed belongs in `AfterRewardsClosed(run, rewards)`; an event whose `OnEventFinished` gives something back (the belt) must finish there, not before.
- `OnEventFinished` runs once: when the method that called `SetEventFinished` returns, and at the latest when the room is left. A page set without options is a finished event too, but its clean-up waits for the leaving, as in the game.

## The order on entering

`EventScreen.Open` follows `EventModel.BeginEvent` and `EventRoom.EnterInternal`:

1. the event's stream is made (`world.Streams.Event(className)`: the seed number plus the hash of the event's id, the same whenever the event is met and in both stream modes);
2. `BeforeEventStarted` (the Ancient's heal; an event taking hold of the belt);
3. `CalculateVars` (the start rolls);
4. the first page: `GenerateInitialOptions`, then `InitialDescription` (the text may name what the options rolled);
5. the relics hear "after room entered" (`run.AfterRoomEntered(RoomKind.Event)`);
6. `AfterEventStarted`.

So a lock decided while the first page was made (gold below a price) stays when a relic pays gold on entering. Whether an option would kill is asked anew at every look.

## The screen

`EventScreen` writes what the mod's EventReader writes. `state`: `run`, `event` (the title), `ancient` (true, Ancients only), `text` (left out when empty), `finished` (true, finished events only), `options`, `player` (without potions), `potions`. An option: `n`, `title`, `text` (when not empty and not the title), `leave`, `locked`, `will_kill_player`, `relic`, `cards`, `note`.

- Actions: `option:<n>`, labelled `Choose <title>`, at an Ancient `Take <relic>`, and `Leave` for the room's leave option; then the potion ids, only beside at least one option.
- A locked option and one that would kill are listed and not offered. Refusals: `<title> is locked.`, `<title> would kill you.`, `There is no event option <n>.`, `Unknown event action '<id>'.`, `'<id>' is not available now.` (a potion id).
- A finished event has no options of its own; the room shows one, the game's PROCEED. Its title is the game's English text, **"Proceed"**, for events and Ancients alike (`PROCEED.title` in the game's `events` and `ancients` tables); the label is `Leave`. `docs/screens.md` shows it with the title "Leave" in its made-up example.
- An option that was chosen is switched off, as the game's button is.
- A title that is empty is replaced by the text cut to 60 characters, then by `option <n>`, as the reader does.

**House rule.** A page on which nothing can be chosen (everything locked or deadly) is one the mod waits on for a human's hand. The arena has no human: whatever would lead to such a page throws, and the action is undone. None of the 21 events of an Overgrowth act can make one. (The screen itself would show the mod's waiting snapshot.)

## Which event a room holds

`EventList.Pull(run)` is the game's `ActModel.PullNextEvent`:

- The act's list (`ActRooms.Events`) is its own events and all 18 shared ones in one shuffle. The walk starts at the act's counter and takes the first entry that is **allowed now** and **not met in this run**. Every entry it steps over moves the counter for good; the list wraps.
- After a whole lap without a find the game takes the entry it stood on, allowed or not.
- The event taken is met from then on (`EventsState.Visited`, the game's VisitedEventIds, for the whole run). Then the room itself moves the counter once more (`MapState.MarkRoomVisited`), as every room of type Event does, the Ancient's too. So the first Event room of an act starts at entry 1.
- `EventList.IsAllowed` has every event's own condition, for the 31 entries of an Overgrowth act's list (13 of the act, 18 shared, 10 of which a first act never holds). The rules need nothing of the events themselves, so they are here for events that are not built too.
- **The floor an event reads is one less than the room's.** The game's total floor is the number of map points it has recorded, and it records a point after the room is made. Slippery Bridge asks for "above 6": its room is on floor 8 at the earliest. `EventList.FloorsBehind(run)` is that number, the same while the map asks `CanHold` and when the room is made.

**Events that are not built** (house rule, the plan's "draw again"). When the event the game takes is not built: a strict run (`LeftOutLog.Strict`) ends as unrepresentable; otherwise the event is passed over, noted in the run's `LeftOutLog` (kind `event`), and the walk goes on along the same lap for an entry that is allowed, unmet and built. When there is none the room cannot be an Event room: `EventList.CanHold(run)` says so while the map rolls the Unknown point, and the map rolls again with the Event barred (`MAP.md`); the director writes that second roll to the run's log with the event the game would have met (`EventList.Peek(run).GameEvent`), as `ActDirector` does. `EventList.Peek(run)` is the walk without any change, for that question and for screening seeds.

## Neow

`Neow` is an `AncientEvent`: on entering the player is put at 0 HP and healed by everything that is missing (the game does exactly that, with no hook: every record of a new run shows 80 HP healed), three relics are offered, taking one obtains it (its pickup effect runs and may ask for a card or open a screen), and the event is finished.

`NeowOffer.Roll(rng, unlocks, ...)` is `Neow.GenerateInitialOptions`, on Neow's own stream:

1. the cursed relic: one pick among the eight (`NeowRelics.Cursed`);
2. the positive list: the fourteen (`NeowRelics.Positive`), without the cursed relic's twin (`NeowRelics.Twins`);
3. a coin for Lava Rock or Small Capsule, unless the cursed relic is Large Capsule; a coin for Nutritious Oyster or Stone Humidifier; a coin for Neow's Talisman or Pomander;
4. what this player may not be offered is taken out (`NeowRelics.IsAllowed`: Massive Scroll needs a second player, Winged Boots and Silver Crucible a run of one, Kaleidoscope all five characters unlocked, Scroll Boxes four Commons and two Uncommons unlocked);
5. the list is shuffled as it stands; its first two are shown, then the cursed one.

That is 17, 18 or 19 numbers. `NeowOffer.OfTheGame(streams, unlocks)` is the game's offer for a seed, whatever is built: what a seed screen asks.

**Relics that are not built** (house rule). A cursed pick that lands on a relic that is not built is drawn again from the same stream; of the shuffled list the first two built relics are shown. Everything passed over is in `NeowOffer.PassedOver` and goes into the run's `LeftOutLog` (kind `relic`, "Neow's offer"). In strict mode nothing is passed over. When no offer can be made (strict with a relic missing, no cursed relic built, fewer than two positive relics built) the run ends as unrepresentable at its start, with the reason.

Neow has no page text in the game's text table (there is no `NEOW.pages.*` key): the mod shows a line of her dialogue as `text` on the first page. Here the first page has a plain sentence of the arena's own, and the finished page has no `text`.

## Left out, by name

- **An event that starts a fight** (the game's EnterCombatWithoutExitingEvent and Resume): Dense Vegetation in an Overgrowth act. The frame is ready for it (a fight is pushed over the event's screen, `MapState.AddRoom`, `Streams.PushRoom`); the event side belongs to the chunk that builds that event.
- **The hook that swaps the next event** (ModifyNextEvent): only the card Lantern Key overrides it, which an act 2 event gives.
- **A hook that bars an Ancient** (ShouldAllowAncient): nothing in this build overrides it.
- **The smaller heal of ascension 2** (Weary Traveler); `AncientEvent` refuses an ascension of 2 or more by name.
- **Run modifiers at Neow** (a daily or custom run offers the modifiers' options), a profile without Neow's epoch (its run starts with a fight), an Ancient's room entered already finished (a loaded save), the Ancients of acts 2 and 3.
- **More than one player**: shared events and votes, each player's own copy of an event on a stream of their slot. (`NeowRelics.IsAllowed` and `NeowOffer.Roll` take the number of players, because the real records of two co-op runs are among the checks.)
- **The rules for when events of other acts are allowed** (Underdocks, Hive, Glory), and Ranwid the Elder, Relic Trader and Fake Merchant after the first act: `EventList.IsAllowed` refuses them by name.
- **Special leave options** (the Trial's, the Architect's last option), options that can be chosen again after they were chosen (the same two events), events that draw their own screen (Fake Merchant), the Crystal Sphere: none can occur in a first act.
- **The mod's waiting states** (event appearing, options appearing, resolving): the bridge's busy spell stands in for all of them.
- **What an event writes into the game's run history.** The floor log is the run loop's.

## Checks

`EventsChecks`. What the expected values rest on:

- **Neow's offer**: the game's own record of nine offers (`sts2-notes/golden-saves/history`): five single-player runs of v0.107.1 (seeds 1BUSLN0FY5, 1CJXGGA4E5, 75F277YN2G, 3FBTGQ63TM, AG5XPLQQNY) and both players of two co-op runs (FS5LGNDZZ7, BQ4CE2FRRJ). All nine come out of the seed. With the folder on the machine (or `$ARENA_GOLDEN_SAVES`) every `history/*.run` of this build is compared too. The draw counts are worked out from the list lengths; the cases with relics left out are worked by hand from the first numbers of Neow's stream of seed 1BUSLN0FY5 and its shuffled list; and 600 seeds with random sets of built relics are held against a second reading of the rule written straight down.
- **The list**: the event list of seed 1BUSLN0FY5 as the game saved it, walked by hand room after room with each event's own condition; the real run met This or That as its first event (entry 1) with the counters and `events_seen` of the later save. Seed 75F277YN2G's record agrees: its first event was entry 1 although entry 0 was This or That, which is always allowed. Every condition is checked at its boundary.
- **The room and its screen**: events of the checks' own (two shaped like This or That and Tablet of Truth) on numbers set by hand, against `docs/screens.md` key for key: pages, locked and deadly options, tooltips, the leave option, refusals, potions, a card choice, a rewards screen, the order on entering, the clean-up, a death, the two guards, copies and replays.
- **Neow's room** with relics of the checks under the game's class names (so that these checks do not depend on which of Neow's relics are built; the built ones have their own, `NeowCoreChecks`): the page of the real seed, the heal, the pick the real run made (Arcane Scroll gave Barricade from one number of `rewards`), a pickup that asks, strict mode, and a seed whose offer is not built.
- **The real run's path**: from Neow over two rooms to the Unknown point of floor 4, which is an Event room and holds This or That; the same with no event built (a Monster room) and in a strict run (unrepresentable).

A mutation pass over `Events.cs`, `EventList.cs` and `Neow.cs` (133 one-line changes, each run against `EventsChecks`) is how the checks were held to account: 130 were caught, and the other three change nothing that can show (the healed amount written as "what was missing"; an option's own relic read outside an Ancient's room, where no option has one; 70% of max HP worked out in whole numbers, which is the same test for a whole number of HP).

After a game update: read `Neow.cs` again and compare `NeowRelics`; read every event's `IsAllowed`; grep the game's pack for `"PROCEED.title"` and the titles of new events (`localization/eng/events.json` and `ancients.json` are plain text inside `Slay the Spire 2.pck`).
