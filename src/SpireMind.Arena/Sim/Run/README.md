# Run mode: the run around the fights

A run carries the player from room to room: HP, max HP, gold, the deck, the relics and the potion belt. This folder holds the frame every room and screen of a run is built on, and what a run is made of before its first room: the seed, the random streams, the act's lists and the relic bags (`STREAMS.md`, next to this file). These are the first two pieces of run mode (the plan is `sts2-notes/run-mode-v0.107.1/plan.md`); the map, the rewards, the rooms and their content come in on top of them. The single-fight arena does not use any of it and is unchanged.

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

## The state

`RunState` is plain state: `Hp`, `MaxHp`, `Gold`, `Belt`, `Deck`, `Relics`, the two odds values (`CardRarityOffset`, starting at -0.05, and `PotionChance`, starting at 0.4, both 32-bit floats as in the game), `ActIndex`, `ActFloor`, `TotalFloor` (the game's total floor: the `floor` of every snapshot), `Seed`. A new one is the Ironclad as the game makes the player: 80 HP, 99 gold, five Strikes, four Defends, Bash, Burning Blood, three empty slots.

- **Deck cards are `CardInstance`s in pile Deck**, each with an id that is its own for the whole run. `AddCard`, `NewCard` (a card on offer, not in the deck yet), `PutInDeck`, `RemoveCard`, `CardById` keep the list; they call no hook. Adding a card as the game does it (with the card-added hooks) is a command of the deck edits.
- **A relic is one object for the whole run**, with whatever it has counted in its fields. `AddRelic`, `RemoveRelic`, `FindRelic` keep the list; the pickup effect is the relic framework's to run.
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

**The end.** `run.End(RunResult.Cleared)`, or `Unrepresentable` or `Error` with a reason in one line, puts up the end snapshot: screen `arena_over`, `state` with `result`, `run`, `me`, `summary` and `reason`, one action `done` that runs and changes nothing. A lost fight ends the run as `Died` and shows the mod's `game_over` first (`result: "defeat"`, `run`, `room`, `player`, one action `continue`, no `score`), so that a model's post-mortem step meets what it meets in the game. If the step after a won fight throws, the run stops as `Error` instead of being left half done.

## House rules of run mode (so far)

- **The end snapshot**, and `game_over` before it on a loss. The game would go on to its run summary and the menu.
- **The turn limit** (`Tuning.MaxTurns`, 40): a fight of a run that reaches it is lost and ends the run, with the reason in the end snapshot. The game has no such limit.
- **The fights' seeds of a run without a world** are the arena's own, as they are for single fights. A run with a world runs its fights on the run's streams, as the game does (in keyed mode on streams of the place, which is the house rule of `STREAMS.md`).

## Checks

`RunStreamsChecks` (the seed, the streams, the lists, the bags, six real saves, and fights on the run's streams) is described in `STREAMS.md`. `RunStateChecks` works every number out by hand: three scripted fights in a fixed order (HP 80, 78 of 84, 74, 80; gold 99, 106, 113, 120; the belt; a relic that counts turns over the whole run; a deck that loses a card and gains one in a fight), `get_deck` and the shared blocks against `docs/screens.md`, a death, the turn limit, replays and copies of runs of the game's encounters, questions, refusals, failures, the bridge and the save file.

For your own checks: `new Run(setup, new FixedFights(...))` with `FightRequest.Monsters` (a function that makes `PlacedMonster`s, so a replay gets fresh ones) and `ShuffleDeck = false` gives fights whose every number can be worked out; `RunSetup.ExtraRelics` and `ExtraCards` take a check's own relics and cards. Inside a checks class the name `Run` is the class's own method, so write `Arena.Run.Replay(...)` for the static one.
