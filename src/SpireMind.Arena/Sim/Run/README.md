# Run mode: the run around the fights

A run carries the player from room to room: HP, max HP, gold, the deck, the relics and the potion belt. This folder holds the frame every room and screen of a run is built on, and what a run is made of before its first room: the seed, the random streams, the act's lists and the relic bags (`STREAMS.md`, next to this file). On top of them sit the map (`MAP.md`), the rewards (`REWARDS.md`), the relic framework with the run's hooks and commands (`RELICS.md`), the deck edits with the run-level card choices (`DECK.md`), and the rooms: the rest site and the treasure room (`REST-AND-TREASURE.md`), the shop (`SHOP.md`), the event room with the list walk and Neow's offer (`EVENTS.md`). The first content of a run is in `Sim/Content/`: 25 relics (`../Content/RELICS.md`), the 18 curses and Neow's Fury (`CURSES.md`), 12 colorless cards (`COLORLESS.md`). The plan is `sts2-notes/run-mode-v0.107.1/plan.md`; no event of the game and none of Neow's relics is built yet, and the act as one director with its command line entry is still to come (`RealRunChecks` holds a small director that walks whole acts through everything that is built). The single-fight arena is unchanged by all of it: its snapshots are byte for byte what they were (a fight now sends "after room entered" during its set-up, which nothing in a single fight answers).

The rules of `Sim/README.md` hold here too: never approximate, read the game's code and write your own, keep every bit of state where a copy finds it.

## What is here

| File | Holds |
| --- | --- |
| `RunState.cs` | `RunSetup` (what a new run starts with), `RunState` (everything that lasts from room to room), `PotionBelt`, `RunPart` (state another file owns) |
| `Run.cs` | `Run` (screens, actions, questions, copies, the save), `RunDirector` (where a run goes), `RunResult` (how it can end) |
| `Run.Fights.cs` | `FightRequest`, `FightOutcome`, `FightScreen`, `FixedFights`, and the hand-over to a fight and back |
| `Run.Screens.cs` | `RunScreen`, the end screens (`GameOverScreen`, `ArenaOverScreen`), `RunDescribe` and the blocks every snapshot shares (`run`, `player`, `potions`, `get_deck`) |
| `RunGame.cs` | `RunGame`: the run behind `IGameBridge`, with the lock, the busy spell and the save on disk |
| `RunStateChecks.cs` | the checks of all of it |
| `../Engine/Fight.Run.cs` | what a fight takes from a run and hands back |
| `RunSeed.cs`, `RunStreams.cs`, `RunOdds.cs`, `GameLists.cs`, `RunUnlocks.cs`, `RelicBag.cs`, `RunGeneration.cs`, `RunWorld.cs`, `GoldenSave.cs`, `RunGoldenData.cs`, `RunStreamsChecks.cs`, `Tools/` | the seed, the streams, the odds, the game's lists, the bags and the start of a run: see `STREAMS.md` |
| `ActMap.cs`, `ActMapGenerator.cs`, `MapState.cs`, `MapScreen.cs`, `MapGoldenData.cs`, `MapChecks.cs` | the act's map, the path, the Unknown roll, the `map` screen, the `boss` key: see `MAP.md` |
| `MapSecondReading.cs`, `GenerationChecks.cs`, `Tools/check_game_lists.py` | a second reader's work: a second port of the map generator (for checks only), the checks that hold generation and map against it and against a whole real run, and a script that holds `GameLists.cs` against the game's files: see "A second reading" in `MAP.md` and `STREAMS.md` |
| `Rewards.cs`, `RewardsScreen.cs`, `CardFactory.cs`, `PotionFactory.cs`, `RunPotions.cs`, `RewardHooks.cs`, `RewardsChecks.cs` | reward sets, the `rewards` and `card_reward` screens, card offers, random potions and potion actions outside fights: see `REWARDS.md` |
| `RunHooks.cs`, `Run.Commands.cs`, `RunRelics.cs`, `RelicFrameworkChecks.cs`, `../Engine/RunContext.cs`, `../Engine/Fight.Relics.cs` | relics owned by the run, the run's hooks and who hears them, the commands that change the player outside a fight, pulls from the bags: see `RELICS.md` |
| `ReachableRelics.cs`, `../Content/Relics1.cs`, `../Content/Relics1Checks.cs` | the 157 relics an act 1 Ironclad can get, the leaving out by name of those not built yet, and the first 25: see `../Content/RELICS.md` |
| `DeckEdits.cs`, `CardSelect.cs`, `CardPools.cs`, `CardPoolLists.cs`, `DeckEditsChecks.cs`, `CardSelectChecks.cs`, `CardPoolsChecks.cs` | deck changes with their hooks, the run-level `card_select` screens, the seven card pools, the pick that is drawn again: see `DECK.md` |
| `RestSite.cs`, `Treasure.cs`, `RestSiteChecks.cs`, `TreasureChecks.cs` | the rest site (Rest, Smith, the options a relic adds) and the treasure room (the chest's relic and gold), with the `rest` and `treasure` screens: see `REST-AND-TREASURE.md` |
| `Shop.cs`, `ShopScreen.cs`, `ShopChecks.cs` | the merchant's room: the shelf, prices, buying, the removal, the `shop` screen: see `SHOP.md` |
| `Events.cs`, `EventList.cs`, `Neow.cs`, `EventsChecks.cs`, `../Content/EventsLeftOut.cs` | the event room and its `event` screen, which event an Event room holds, the Ancient's room and Neow's offer of three: see `EVENTS.md` |
| `../Content/Curses.cs`, `../Content/CursesChecks.cs`, `../Engine/Fight.Deck.cs`, `../Engine/ChoicePicks.cs` | the 18 curses and Neow's Fury, a card leaving the deck inside a fight, the order a fight's grid choice hands its picks back in: see `CURSES.md` |
| `../Content/ColorlessCore.cs`, `../Content/ColorlessCoreChecks.cs` | the first 12 colorless cards and the leaving out by name of the other 52: see `COLORLESS.md` |
| `RealRunChecks.cs` | all of it together: a real run of the game from Neow's room to its sixth floor through the screens, and whole acts walked through every room that is built |

## The state

`RunState` is plain state: `Hp`, `MaxHp`, `Gold`, `Belt`, `Deck`, `Relics`, the two odds values (`CardRarityOffset`, starting at -0.05, and `PotionChance`, starting at 0.4, both 32-bit floats as in the game), `ActIndex`, `ActFloor`, `TotalFloor` (the game's total floor: the `floor` of every snapshot), `Seed`. A new one is the Ironclad as the game makes the player: 80 HP, 99 gold, five Strikes, four Defends, Bash, Burning Blood, three empty slots.

- **Deck cards are `CardInstance`s in pile Deck**, each with an id that is its own for the whole run. `AddCard`, `NewCard` (a card on offer, not in the deck yet), `PutInDeck`, `RemoveCard`, `CardById` keep the list; they call no hook. Changing the deck as the game does it (with its hooks) is `DeckEdits` (`DECK.md`).
- **A relic is one object for the whole run**, with whatever it has counted in its fields. `AddRelic`, `RemoveRelic`, `FindRelic` keep the list; giving the player a relic as the game does it (pickup effect, out of the bags) is `run.ObtainRelic` (`RELICS.md`). The same holds for HP, max HP and gold: the setters here are bare, and a room changes the player through the run's commands (`run.Heal`, `GainMaxHp`, `LoseMaxHp`, `Damage`, `GainGold`, `LoseGold`).
- **The belt** grows (`Grow`) and never shrinks: only an ascension level takes a slot away, so that half of the game's rule is left out by name.
- **Your own state goes into a `RunPart`**: a class of your own with `Clone(CloneMap)` (and `Dump()`, so that replay checks see it), fetched with `state.Part<MapState>()`. Nobody has to edit `RunState` to add the map, the bags or the streams, and a part is copied with the run.
- `Dump()` writes everything, every field of every relic and card hook object included. Two runs with equal dumps are in the same state.

## A run

```csharp
var run = new Run(new RunSetup { Seed = "7QK2M9XWPA" }, director);
run.Snapshot();          // the screen the player sees
run.Execute("map:2");    // refused with a reason, or accepted and added to run.Actions
run.Clone();             // a deep copy, also in the middle of a fight or of a question
Run.Replay(setup, director, run.Actions);   // the same run again from its save
```

A run is decided by its setup, its director and its accepted action ids. `Actions` is the save; `done` at the end is not in it.

**Screens.** A `RunScreen` builds one screen's snapshot and runs its action ids: keys, key order, labels and refusal texts are the mod's (`docs/screens.md`). The run keeps the open screens in a stack (`Push`, `Pop`, `CloseAll`, `Top`) and shows the top one. A screen is copied with the run: it keeps what it holds in its own fields, implements `Clone(CloneMap)` (the map gives the copy's deck card or relic for one of the run's), and never keeps a `Run`.

**Directors.** A `RunDirector` says where the run goes: `Begin` puts up the first screen or starts the first fight, `AfterFightWon` says what follows a won fight, `AfterRoom` what follows a finished room (screens call `run.RoomFinished()` from their proceed or Leave). The act as the game lays it out is one director. `FixedFights` is another: fights in a fixed order, one floor each, cleared after the last; checks use it to carry a player through fights without the rooms in between. A director is copied with its run and keeps what it counts in its own fields.

**Fights.** `run.StartFight(new FightRequest { Encounter = "NibbitsWeak" })` hands the player to a new `Fight` and puts it on the screens as a `FightScreen`: the fight's own snapshots and action ids, unchanged. What goes in: HP, max HP, the deck (every card with its id, upgrade and numbers), the relic objects, the belt with all its slots, the gold, the total floor. The fight works on its own copies, so its copy before every action and its search copies cover them. When it is over the run takes back HP, max HP, gold, the belt, the relics and the deck as it stands, and the screen is gone; Block, powers, cards made in the fight and upgrades made in the fight end with it. A won fight goes to the director with a `FightOutcome`; a lost one ends the run. While a fight is on, `run.State` is the player as the fight began: `run.Hp`, `run.Gold`, `run.Potions`, `run.Relics`, `run.Deck` and the describe methods give the live numbers.

- Inside a fight: `f.Gold`, `f.DeckCards` (the deck as it stands), `f.DeckVersionOf(card)` (the deck card a combat card was made from, the game's DeckVersion; `card.DeckCardId` is its id, 0 for a card made in the fight), `f.DeckList` and `f.NewDeckCard(def)` for the commands that change the deck from inside a fight.
- The fight's streams are the run's, as in the game, once the run has them: when the run's world is begun (`RunWorld.Begin(run.State)`, see `STREAMS.md`) `StartFight` lends the fight the run's streams as they stand and the run takes them back when the fight is over, won or lost. Nobody has to call anything; set `run.State.TotalFloor` first, since the encounter's own stream is seeded with it. `FightScreen.OnRunStreams` says that a fight runs this way.
- A request can opt out, for checks: `FightRequest.Seed` gives the fight streams of its own that start at the fight's start, and `FightRequest.Rng` hands it streams the caller made, to be read back from `run.LastFightRng` (not from the object that was lent: a fight works on copies). Either leaves the run's streams alone.
- A run without a world (`FixedFights` alone, most checks) has no streams to lend: its fights get the seed `run.FightSeed()`, a hash of the run's seed, the act, the floor and the number of fights started.

**Copies.** A fight copies itself before every action, so during a fight the run copies nothing. For an action outside a fight the run takes a copy of itself first. So:

- an action that is refused, or that throws, leaves the run exactly as it was;
- **an action may ask the player something in the middle** (`run.Ask(questionScreen)`, which returns the chosen indexes, or null when the player backed out). It works as a card choice does in a fight: the first time the answer is not known the action is abandoned by an exception (never catch it) and the question's screen shows; when that screen calls `run.Answer(...)` the whole action runs again from the copy, and this time `Ask` returns. That only holds if the action keeps all its state in the run. What follows a won fight (`AfterFightWon`) is such an action too.

**Something the arena has not built.** A roll that can only land on something unbuilt throws `RunUnrepresentableException` with the reason in one line (`DECK.md`, `REWARDS.md`). The run catches it itself, wherever it is thrown: it ends as `unrepresentable`, standing as it stood before that action, and the action stays in the save so a replay ends the same way. Nobody else catches it.

**The end.** `run.End(RunResult.Cleared)`, or `Unrepresentable` or `Error` with a reason in one line, puts up the end snapshot: screen `arena_over`, `state` with `result`, `run`, `me`, `summary` and `reason`, one action `done` that runs and changes nothing. A lost fight ends the run as `Died` and shows the mod's `game_over` first (`result: "defeat"`, `run`, `room`, `player`, one action `continue`, no `score`), so that a model's post-mortem step meets what it meets in the game. If the step after a won fight throws, the run stops as `Error` instead of being left half done.

## House rules of run mode (so far)

- **The end snapshot**, and `game_over` before it on a loss. The game would go on to its run summary and the menu.
- **The turn limit** (`Tuning.MaxTurns`, 40): a fight of a run that reaches it is lost and ends the run, with the reason in the end snapshot. The game has no such limit.
- **The fights' seeds of a run without a world** are the arena's own, as they are for single fights. A run with a world runs its fights on the run's streams, as the game does (in keyed mode on streams of the place, which is the house rule of `STREAMS.md`).
- **Drawing again** when a roll lands on something the arena has not built, with the run's log of everything passed over and the strict switch that ends the run instead: cards and potions (`DECK.md`, `REWARDS.md`), relics from the bags (`RELICS.md`), the event of an Event room and an Unknown room that cannot be an Event room (`EVENTS.md`, `MAP.md`), Neow's offer (`EVENTS.md`), a shop's shelf (`SHOP.md`), a chest's relic (`REST-AND-TREASURE.md`). Each page says where the rule stops and the run ends as unrepresentable.

## Checks

`RunStreamsChecks` (the seed, the streams, the lists, the bags, six real saves, and fights on the run's streams) is described in `STREAMS.md`. `RunStateChecks` works every number out by hand: three scripted fights in a fixed order (HP 80, 78 of 84, 74, 80; gold 99, 106, 113, 120; the belt; a relic that counts turns over the whole run; a deck that loses a card and gains one in a fight), `get_deck` and the shared blocks against `docs/screens.md`, a death, the turn limit, replays and copies of runs of the game's encounters, questions, refusals, failures, a run that meets something the arena has not built (in an action, after a question, after a fight, at the start), the bridge and the save file. The other parts have their own check classes, each described on its page: `MapChecks`, `RewardsChecks`, `RelicFrameworkChecks`, `DeckEditsChecks`, `CardSelectChecks`, `CardPoolsChecks`, `RestSiteChecks`, `TreasureChecks`, `ShopChecks`, `EventsChecks`, and for the content `Relics1Checks`, `CursesChecks`, `ColorlessCoreChecks`. `GenerationChecks` is a second reader's: the map made by two readings of the game's generator on 1,430 seeds, the placement rules on the same maps, a whole real three-act run on the generated lists (`MAP.md`, `STREAMS.md`).

`RealRunChecks` holds that the parts fit. It plays Mark's real run on seed 1BUSLN0FY5 from Neow's room to its sixth floor through the screens (the map, two fights and their rewards, the event's relic and curse, the shop with the purchase of the game's record, the third fight's line-up), every expected value taken from the game's saves and its own record of the run; the same run under the strict switch, which ends as unrepresentable at the first thing that is not built; and 24 whole acts walked by a scripted player through every room that is built (fights of all three kinds with their rewards, shops with purchases, rest sites, chests, a stand-in for the events), each of which must end cleared or in a death, replay from its action list and go on the same from a copy taken on the way. `ARENA_RUN_SOAK=600 scripts/arena.sh selfcheck --checks RealRunChecks` walks 600 acts (half a minute). Its director, `Floors`, is the worked example of a director that travels and opens every built room (about 70 lines).

For your own checks: `new Run(setup, new FixedFights(...))` with `FightRequest.Monsters` (a function that makes `PlacedMonster`s, so a replay gets fresh ones) and `ShuffleDeck = false` gives fights whose every number can be worked out; `RunSetup.ExtraRelics` and `ExtraCards` take a check's own relics and cards. Inside a checks class the name `Run` is the class's own method, so write `Arena.Run.Replay(...)` for the static one.
