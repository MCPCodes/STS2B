# Run mode: the act as one run

This page is for whoever works on the run loop: the first act from Neow's room to the proceed of the boss's rewards as one director, the floor log, the three modes, the seed screen, the scripted run players and `run-baseline`. Everything a room does is on the page of its part (`README.md` lists them); this page is about what strings the rooms together and what is built on a whole run. The plan is `sts2-notes/run-mode-v0.107.1/plan.md` (1.2, 1.4, 1.5 for the game's order; 5.1, 5.3 and 5.5 for the modes, the share and the players).

## What is here

| File | Holds |
| --- | --- |
| `Act.cs` | `RunMode` (draw again, screened, strict), `ActDirector` (the act), `ActRun` (start a run, carry one on from its save, seeds for a mode, how a run ended) |
| `FloorLog.cs` | `RunChoice`, `FightLine`, `FloorLine`, and `FloorLog`: the part the director writes and the view a report reads |
| `SeedScreen.cs` | `SeedLook` (what a seed fixes before anything is played), `BagEnd`, `SeedScreen` (which seeds a screened run may start on) |
| `RunPlayers.cs` | `IRunPlayer`, `RunWalker`, `PlainPlayer`, `RunPlayers` (by name, the loop that plays a run), `ActResult`, `RealRunShare` |
| `../../Runner/RunBaselineCommand.cs` | `arena.sh run-baseline` with its own options |
| `Run.cs` (two additions) | `RunDirector.KeepsChoices`, `Run.Choices` |
| `RunLoopChecks.cs` | the run self-check |

## The act

```csharp
var run = ActRun.Start("1BUSLN0FY5");                    // draw again, the game's streams
var run = ActRun.Start(seed, RunMode.Screened);           // on a seed that passes the default screen
var run = ActRun.Start(seed, RunMode.Strict);             // the game's own run, or unrepresentable
var run = ActRun.Start(seed, RunMode.DrawAgain, StreamMode.Keyed);   // keyed streams, for comparing players
run.Snapshot();  run.Execute("option:1");  run.Actions;  run.Choices;  FloorLog.Of(run);
```

`ActDirector` is a `RunDirector` and an `IMapDirector`. What it does is the game's order and nothing else:

1. **Begin**: `RunWorld.Begin` (the act roll, the bags, the lists, the streams), the strict switch (`LeftOutLog.Strict`, before anything is rolled: Neow's offer reads it), the refusals of the start (below), `MapState.BeginAct(run, 0)`, the start point entered at once (the game makes no map decision before Neow: `RunManager.EnterAct`), Neow's room through `EventScreen.Open`, then the act's event counter (`MapState.MarkRoomVisited(run, RoomKind.Event)`): her room is an Event room.
2. **EnterRoom** (the map screen calls it after `MapState.Enter`): a Monster, Elite or Boss room takes the act's next list entry and `run.StartFight`; `ShopRoom.Enter`, `RestSite.Enter`, `TreasureRoom.Enter`; an Event room takes `EventList.Pull(run)`. Then the counter of that kind of room moves. Each room announces itself to the relics ("after room entered"); the director never does.
3. **AfterFightWon**: `RunRewards.OfferForRoomEnd`; when that gives no screen (an encounter without rewards) the room is finished at once.
4. **AfterRoom** (every room's proceed or Leave ends here): on the boss's point `run.End(RunResult.Cleared)`; otherwise the map opens.
5. **CanHold**: `room != RolledRoom.Event || EventList.CanHold(run)`, the house rule for an Unknown point when no built event is allowed and unmet. A no means the map rolls the point again, which is a redraw like any other: the director keeps the event the game would have met (`EventList.Peek(run).GameEvent`) and writes it to `LeftOutLog` when the room that came out instead is entered, so the floor shows it as drawn again and the run no longer counts as the game's own. (The map itself logs nothing.)

It also writes the floor log (`FloorLog.Enter`, `FightWon`, `Leave`) and says `KeepsChoices`.

The director knows nothing about which relics, events or cards exist. Whatever the registry holds decides: a relic registered tomorrow is offered by Neow tomorrow, and a run that could not begin yesterday begins.

**Refused at the start** (the run is over at once as unrepresentable, with no floor entered and no action):

- a seed whose first act is not Overgrowth, in every mode: it is the game's run of another act ("the first act of seed X is Underdocks; the arena has built Overgrowth alone");
- in screened mode, a seed that fails the director's screen ("the seed does not pass the screen (neow,events=1): ...");
- Neow's offer: in strict mode when one of the game's three relics is not built; in the other modes only when no redraw can help (no cursed relic built, or fewer than two positive ones). That one comes from Neow's room, not from the director.

**Not built, by name**: the game's "after act entered" hook, which it sends once the start room is entered (only the run modifier Cursed Run hears it); a second act (the run ends at the boss's proceed, where the game goes on to the next act's Ancient); profiles other than "everything unlocked" as a command line option (`ActDirector.Unlocks` takes one from code); more than one player; ascensions.

## The three modes

| Mode | Seeds | A roll that lands on something unbuilt | For |
| --- | --- | --- | --- |
| `DrawAgain` | any seed whose first act is Overgrowth | is drawn again by the part's house rule and written to `LeftOutLog`; the run ends as unrepresentable only where no redraw can help | scripted baselines, soak runs |
| `Screened` | a seed that passes `ActDirector.Screen` | the same | runs a model plays: the run begins as the game's own |
| `Strict` | every seed, as it comes | ends the run as unrepresentable, standing as it stood before the action | measuring the share of real runs; golden saves |

Keyed streams (`StreamMode.Keyed`, the house rule of `STREAMS.md` for comparing players) go only with `DrawAgain`: `ActRun.Director` and `ActDirector.Begin` refuse them for a strict or screened run with an `ArgumentException`, since such a run has to be the game's own.

`LeftOutLog.FirstFloor` is the floor from which a run is no longer the game's own run for its seed; null means the game gives exactly this run for this seed and these actions. A strict run that ends cleared or dead always has null there, and it is, action for action, the draw-again run of the same seed and player (the self-check holds both).

## The seed screen

`SeedLook.Of(seed)` is what a seed fixes before anything is played, the game's own things whether built or not: the act; Neow's three relics; the act's events in the order the Event rooms walk the list (from the entry after Neow's, without the ten shared events no first act can hold); both ends of the player's bag and the front of the shared bag, a list per rarity; the first shop's two colorless cards. It plays nothing: a few list lookups and one generation of the run's start.

- **Events**: which event an Event room takes also depends on the player then (gold, HP, the deck), so the k-th room takes the k-th entry of the walk or a later one. "The first N are built" guards the first rooms as long as the walk stays inside those N. It is the cautious form of the plan's "first allowed events": it asks for the entries whatever the player holds when the room comes (`EventList.Peek(run).IsTheGames` answers for one state of one run).
- **Bag ends**: the front of the player's Common, Uncommon and Rare lists (an elite's relic, an event's), the back of all four without the relics a shop never sells (a shop's three), the front of the shared bag's three (a chest).
- **The first shop's colorless cards** are the 13th and the 15th number of the player's `shops` stream, whenever the first shop is met: a shop draws the sale, five picks, five prices and the sale's second price before them, each exactly one number while every character card is built. It holds for a player with nothing that changes a shop's cards (no such relic is built).

`SeedScreen(Neow, Events, BagEnds, ShopColorless)` says how deep to look; `WhyNot(look)` names the first thing in the way, or null. `Default` is Neow's offer and the first event; `ActOnly` the act alone; `Full` the plan's screen (Neow, six events, every bag end one deep, the shop's cards). **The plan's screen passes no seed today**: with 25 of the bags' 122 relics built, all ten bag ends are built on about one Overgrowth seed in five million (7 of 26 Commons, 6 of 32 Uncommons, 7 of 38 Rares and 5 of 26 Shop relics, at ten ends). What the shallower screens pass with the first playable version's content (measured on 60,000 seeds): the default 1 seed in 42, `neow,events=2` 1 in 122, `neow,events=3` 1 in 460, `neow,events=1,shop` 1 in 1,240, `neow,events=2,shop` 1 in 3,750. That is why the tests are switches and the default is shallow; deepen it as content lands (`--screen neow,events=2,bags=1` on the command line, `SeedScreen.Parse`). `ActRun.Seeds(count, makerSeed, mode, out tried, screen)` finds seeds for a mode and says how many it looked at.

## The floor log

One `FloorLine` per map point entered, from `FloorLog.Of(run)` (also `ToJson(run)` for a result line and `Describe(run)` as text):

```
floor  2  monster (5,1)  Monster ShrinkerBeetleWeak [Shrinker Beetle, 5 turns]  HP 80/80 > 79/80  gold 99 > 110  deck 11 > 12  | reward:0 Take 11 gold; reward:0 Open card reward (3 cards); card:0 Take Colossus; proceed Proceed; map:0 Go to Monster at (6,2)
```

- **The room**: `Floor`, `At` ("(col,row)"), `Point` (the map's word: ancient, monster, elite, unknown, shop, treasure, rest, boss), `Room` (what the point held: Event, Monster, Elite, Boss, Shop, Treasure, RestSite), `What` (the encounter's or event's class name; null for a shop, a rest site and a chest), `Title` (the event's title, or the enemies of the fights).
- **The player**: `HpIn`, `MaxHpIn`, `GoldIn`, `DeckIn` on entering; `HpOut`, `MaxHpOut`, `GoldOut`, `DeckOut` at the room's proceed or Leave (`Finished`). For a room that was not left (the room a run died in, or the one the player is in) the "out" values are the player as it stands. A potion used on the map after the room shows as a choice of the floor and in the next floor's `HpIn`.
- **Fights**: every fight of the floor with its turns, HP before and after and damage taken. The fight a run died in is the last one, `Won: false`.
- **Choices**: every `RunChoice` taken while standing on the floor, in order, with the screen, the id and the label the snapshot offered it under. The path choice that leaves a floor is that floor's last choice.
- **DrawnAgain**: what rolls on this floor passed over, as "kind:ClassName". Empty while the floor is the game's own.

Where the pieces live. The director writes what only it knows at the time into the run's state (`FloorLog`, a `RunPart`: copied with the run, undone with an action that is undone, built again by a replay). The choices are kept by `Run` itself beside the action list (`Run.Choices`), for a director that says `KeepsChoices`: each accepted action outside a fight and each potion action inside one, with its place in `Run.Actions`. They are not in the state, so an action that ends a run as it stood (unrepresentable) is still listed, as it is in the save. `FloorLog.Of` joins the two with `Run.LastFight` (a lost fight never reaches the director) and `LeftOutLog`.

A run that ends as unrepresentable stands as before the action that met what is missing: its log ends on the floor the player was on, with that action as the last choice. `ActResult.MetFloor` is the floor the missing thing was on (one more than the run's floor when the action was a path choice; 0 for a seed refused before Neow's room).

## The save on disk

A run is decided by its seed, its mode (with the stream mode, and the screen of a screened run) and its accepted action ids. `RunGame.ActionLogPath` is the save: a first line `# spiremind arena run, seed <seed>`, then one accepted action id per line, each written when it is accepted; the file appears with the first accepted action. `ActRun.StartGame(seed, mode, actionLogPath: path)` starts a run that writes it; `ActRun.Resume(seed, mode, path)` rebuilds the run by replaying the file and appends further actions to it. The file does not hold the mode: keep it beside the save. A file of another seed, another mode or another build is refused with `InvalidOperationException` (an action in it is turned down). Never start a new run on a path where a file already is: it would be appended to. `ActRun.Replay(seed, mode, actions)` builds a run from action ids without a file: with the first k actions of a save it is the run at any earlier point (a `RunChoice.Action` is a choice's place in the list), which is how a finished run is looked at again: the state a choice was made from, or the start of a fight to be played another way on a copy.

## Scripted players

```csharp
public sealed class MyPlayer : IRunPlayer
{
    public string Name => "Mine";
    public string Choose(Run run, GameSnapshot snapshot) =>
        run.Fight is not null ? RunPlayers.InFight(new LookaheadPolicy(), run, snapshot)   // a fight player's answer, with the guard
        : snapshot.Screen == "game_over" ? "continue"
        : snapshot.Actions[0].Id;                                                           // decide from the snapshot, as a model does
}
var result = RunPlayers.Play(ActRun.StartGame(seed, mode, actionDelay: TimeSpan.Zero), new MyPlayer());
```

- A player returns one id of the snapshot it is handed and keeps nothing between calls: what it needs beyond the snapshot it reads off the run (`run.Actions.Count` as a step, `run.Fight` to search on copies). Then a run answered twice is answered the same, also after it was rebuilt from its save.
- It must not implement `IPolicy`: the single-fight baseline finds its players by that interface (a check holds it).
- `RunPlayers.InFight(policy, run, snapshot)` hands a fight to a fight player (`IPolicy`, `IEnginePolicy`) and guards one thing: a card choice inside a fight that offers `confirm` and lets picks be taken back (Neow's Fury) is confirmed after twelve picks in a row, so a player that answers a screen the same way every time cannot loop.
- `RunPlayers.Play(game, player)` is the loop: snapshot, choice, action, until the end snapshot. A refused action is counted and the first offered action is taken in its place; a snapshot with no action, or 60,000 actions, stops the loop as `Stalled`. Neither happens in a sound arena.
- `RunPlayers.PlayFight(copy, policy)` plays the fight that is on to its end on a copy of a run: the yardstick for a fight a model played (plan 5.6).

The two that exist (`RunPlayers.ByName`; a fight player can be named after a colon: `Walker:random`, `Plain:greedy`):

- **Walker**: outside a fight any offered action, each as likely as the other; a potion action one time in eight; Greedy in fights. Its choice is a hash of its seed, the number of actions taken and the screen, so a walk is the same every time and a screen met again unchanged gets another answer.
- **Plain** (plan 5.5): the first path, every reward, the first card of a card reward, the first option of an event (Neow's first relic), every chest, Rest below half HP and Smith otherwise, nothing bought; Lookahead in fights (about 2.5 s an act). In a card choice it picks the first card not picked yet and confirms as soon as that is offered.

## run-baseline

`scripts/arena.sh run-baseline [--runs N=20] [--seed S=1] [--seeds A,B] [--mode draw-again|screened|strict] [--screen ...] [--streams game|keyed] [--players Walker,Plain] [--threads K=1] [--out DIR] [--floors] [--look] [--tries T=200000]`

It finds seeds for the mode (`ActRun.Seeds`), lets each player play each seed, and prints a table of the runs (up to 60), the totals by player, what unrepresentable runs were missing, and in strict mode the share of real runs. `--look` plays nothing and prints each seed's `SeedLook` with what is built. `--out DIR` keeps every run's save (`DIR/actions/<player>/<seed>.txt`; a save that is there is carried on, one that does not fit is played again) and writes `DIR/runs.jsonl`: one line per run, `ActResult.ToJson(run)` with `summary`, `floors` (the floor log), `end` (get_deck at the end), `mode`, `streams`. It ends with 1 when an action was refused, a run hung, the simulator failed, or no seed passed the screen among the `--tries` seeds it looked at. It reads its own options (`RunBaselineCommand.Options.Parse`), as `values` does; `Program.cs` hands it the arguments.

## The share of real runs

`RealRunShare.Of(strictResults)` measures what the plan asks in 5.3: of real runs (any seed, everything unlocked), which share can the arena represent with nothing drawn again? Strict runs on seeds as they come answer it, and two numbers are printed:

- **The plain share**: runs that ended cleared or dead, of runs started. It is the player's own: a player that dies on floor 6 never meets what waits on floor 10.
- **As whole acts, deaths taken out**: floor by floor, of the runs that entered the floor alive and still the game's own, the share that met nothing unbuilt on it; the product over the seventeen floors, times the share of Overgrowth seeds. A run that died on a floor counts for the floors before it. This is the plan's question ("from Neow to the boss"), and it does not reward dying early. It assumes that dying and meeting something unbuilt do not depend on each other, and it needs runs that are still the game's own at the boss: the report says how many runs each floor's figure rests on, and says "too small for N runs to show" when none got through.

Measured on the first playable version's content (the wave 3 tree with the 14 relics of Neow and the 7 events of chunks 14 and 15, 2026-10-12): 40,000 strict seeds played by Plain gave **0.023% of all real runs as whole acts** (0.046% of the Overgrowth ones; 6 runs cleared the act as the game's own, 64 died as the game's own), with 11.6% of the Overgrowth runs still the game's own after Neow's room, 4.4% after floor 5, 1.1% after floor 9, 0.075% after floor 13. The plan's estimate was 0.025%. What the other 39,930 runs were missing, counted with Plain:greedy's on the same seeds: the act 39,856 times (half of all seeds), Neow's offer 35,478, an event 2,115, a card 1,291 (the shop's colorless cards), a relic 587, a potion 314. `scripts/arena.sh run-baseline --mode strict --runs 40000 --players Plain --threads 4` measures it again in ten minutes after content lands. Plain takes the first path at every fork; `--players Walker:lookahead` walks at random with the same fight player.

## Checks

`RunLoopChecks` (`scripts/arena.sh selfcheck --checks RunLoopChecks`, about 25 s on four threads; `ARENA_RUN_SOAK=3000` in front walks thousands of acts):

- **The seed screen on real seeds**: the act, the event walk and the first two relics of all ten bag ends of four saved seeds, typed in from Mark's saves; the act and Neow's offer of five recorded runs; the first shop's colorless cards of two recorded runs (Discovery and Calamity; Jack of All Trades and Secret Technique). Then `WhyNot` with hand-made "built" sets at every boundary, the text form, and seeds for each mode.
- **The start**: three real Underdocks seeds refused in every mode; keyed streams refused for strict and screened; a seed that fails its screen; Neow's room on floor 1.
- **The real run on seed 1BUSLN0FY5 through the director**: Neow's pick, two fights with their rewards, the option That of This or That? (the game's ORNATE), the shop's purchase and the third fight, every label typed in by hand, and the floor log held against the game's record floor by floor (rooms, encounters, gold 99, 110, 120, 120, 43, the deck, the choices); the fight of floor 6 played on copies (the yardstick), and the run rebuilt up to one of its choices. Then the same run under the strict switch on both options of the event.
- **An Unknown point rolled again**: twins on 30 seeds, one strict and one drawing again, with every built event marked as met: where the strict twin stops at an Event room, the other's log names the game's event on that floor, once, and the floor shows it as drawn again.
- **A won fight whose rewards cannot be made** still shows on its floor and counts in the result.
- **Run.Choices**: a refused action, a question's steps, an action that fails after its question, a potion in a fight, a copy, a replay, a director that keeps none.
- **The share**: ten runs counted by hand.
- **The players**: every plain rule on a hand-made snapshot; the walker's shares over 8,000 steps; the guard at twelve picks.
- **Whole acts**: both players on 100 seeds, keyed and screened runs, a few acts with Lookahead. Per run: no action refused or thrown, every name shown outside a fight is of the registry, every shop full on arrival with prices above 0, purchases paid for and held, the floor log consistent with itself and with the run, Neow's offer and the first shop's colorless cards as the seed's look says, an end the mode allows, a replay to the same dump, floor log and choices, a copy played on to the same end.
- **Strict runs**: 240 seeds as they come, both players: all of the above, each strict run is a prefix of its draw-again run and the first redraw there is on the floor the strict run stopped on; about half the seeds are Underdocks; the share is printed.
- **The command**: every option and every refusal; a batch with `--out`, every save rebuilt to its result line, a save cut in half carried on to the same end, a save that does not fit played again; strict mode and `--look`.

The checks are written for whatever the registry holds: with none of Neow's relics built every run ends at its start as unrepresentable and the checks hold that instead (363 assertions); with Neow's relics and no event, Unknown points are never Event rooms (441); with the first playable version's content they walk whole acts through everything (507). The soak on that content (`ARENA_RUN_SOAK=3000`, six and a half minutes on four threads): 7,350 acts walked (271 cleared, 7,079 deaths), 82,741 floors, 1,313,537 actions, 4,361 shops, and 6,000 strict runs with their draw-again twins, without one problem.

A mutation pass held the checks to account: 83 one-line faults in the six files of the run loop (the director's order and refusals, the floor log's fields, the list of choices, the seed screen, the players' rules, the share's arithmetic, the command), each run against `RunLoopChecks` alone. All are caught; three were not at first and got a check each (the fights won of a result line, and that a refused action ends `run-baseline` with 1).

`RealRunChecks` holds the director from the other side: its own small director (`Floors`, written before this one and kept apart from it) walks whole acts, and every one of them is played again through `ActDirector` action for action and must end in the same state but for the floor log and the run's log (24 acts in every self-check, 600 with `ARENA_RUN_SOAK=600`).

**How to get it wrong.** Forgetting `FloorLog.Enter` before a room is opened (the room's own failure then leaves no floor). Opening the map on the boss's point. Marking a list's counter before the room took its entry. Calling `run.AfterRoomEntered` for a room that announces itself. Keeping anything in the director that the floor log or a replay needs (it has no fields but its settings: everything else is in the run). Writing a run player that remembers something between calls, or that implements `IPolicy`.
