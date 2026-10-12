# Run mode: the seed, the streams, the lists and the bags

What a run is made of before its first room, and where every random number of a run comes from. Chunk 2 of the run-mode plan (`sts2-notes/run-mode-v0.107.1/plan.md`, sections 1.1, 1.2 and 5.4). The frame of a run (state, screens, fights) is in `README.md` next to this file.

Everything here is the game's (v0.107.1, single player, ascension 0, standard mode) and is held against real saves: `RunStreamsChecks` rebuilds six saves Mark made on 2026-10-11 from their seed and unlock state alone, down to the number of draws.

## Files

| File | Holds |
| --- | --- |
| `RunSeed.cs` | `RunSeed` (the seed text and its hash), `GameNames` (stream names, id entries) |
| `RunStreams.cs` | `RunStreams`, `RunStream`, `StreamMode`, `Purpose`, `RunPlace`: the one way to draw |
| `RunOdds.cs` | `CardRarityOdds`, `PotionOdds`, `UnknownRoomOdds`, in 32-bit floats |
| `GameLists.cs` | the game's lists by class name: the shared relic pool and the five characters' pools with rarities and epochs, the four acts with their events, encounters, tags, Ancients and boss order, the shared events and Ancient, the epochs. Made by script from the game's files |
| `RunUnlocks.cs` | `RunUnlocks`: epochs, encounters met, runs finished, acts discovered |
| `RelicBag.cs` | `RelicBag`: the game's grab bag of relics |
| `RunGeneration.cs` | `RunGeneration` (the act roll and everything `up_front` makes), `RunStart`, `ActRooms` |
| `RunWorld.cs` | `RunWorld`: all of it as one part of the run's state |
| `GoldenSave.cs` | `GoldenSave`: reads a real save and compares it with what its seed gives |
| `RunGoldenData.cs` | six real saves, reduced to the parts the checks read |
| `Tools/game_lists.py`, `Tools/golden_data.py` | the scripts that make `GameLists.cs` (from the decompiled game) and `RunGoldenData.cs` (from the saves); run the first again after a game update |
| `Tools/check_game_lists.py` | a second reader's script: reads the game's pools, acts, encounters and epochs its own way and names every difference from `GameLists.cs` (none today) |
| `RunStreamsChecks.cs` | the checks |
| `GenerationChecks.cs` | a second reader's checks (below, "A second reading") |

`Sim/Scenario.cs` rolls card rarity, potion drops and Unknown rooms through the odds classes here; its scenarios are the same as before, one by one. `Sim/Engine/GameRng.cs` has one more `RngSet` constructor, from nine streams.

## Using it

```csharp
var world = RunWorld.Begin(run.State);                 // once, before the first room: act roll, bags, every act's rooms
var world = run.State.Part<RunWorld>();                // everywhere else; copied with the run, in its Dump

world.Acts            // ["Overgrowth", "Hive", "Glory"], or "Underdocks" first: the game's roll for the seed
world.Rooms[act]      // ActRooms: Events, NormalEncounters, EliteEncounters, Boss, Ancient, the four counters
world.PlayerBag       // RelicBag: elites, events, shops
world.SharedBag       // RelicBag: chests
world.UnknownOdds     // UnknownRoomOdds
world.Streams         // RunStreams
```

**Every roll is one call that names the game's stream and what the roll decides:**

```csharp
var rng = world.Streams.Draw(RunStream.Rewards, Purpose.Card(reward: 0, card: 2));
var rarity = RunWorld.RollCardRarity(run.State, rng, CardOddsType.Regular);   // moves state.CardRarityOffset
var pick = rng.NextInt(options.Count);
var upgradeRoll = rng.NextFloat();
```

- Draw every number of one decision from the stream one call returned, redraws included. Ask again for the next decision. Never keep a stream in a field.
- The stream is the one the game draws that roll from (plan 1.6, `shop.json`, `neow.json` name it for every roll). In game mode that is all that counts. The purpose is ignored there, so a wrong purpose shows only in keyed mode: take it from the list in `Purpose`, or `Purpose.Of("a_fixed_name", index)`; never a name that depends on what the player did before.
- `up_front`, the act roll, an act's map (`Streams.ActMap(actNumber)`), an encounter's line-up (`Streams.Encounter(className, totalFloor)`) and an event's rolls (`Streams.Event(className)`) belong to the seed: they are the same in both modes.
- Tell the streams where the run stands: `Streams.MoveTo(act, row, col)` on entering every map node (the act's start node included), `PushRoom()` and `PopRoom()` around a fight an event starts.
- A fight needs nothing: `run.StartFight(new FightRequest { Encounter = e })` lends the fight the run's streams (`Streams.LendToFight(e, state.TotalFloor)`) and takes them back when it is over (`Streams.TakeBack`), once the world is begun. In game mode the fight then goes on where the last one stopped, as in the game; in keyed mode it gets streams of the place. Set `state.TotalFloor` before the call. Only a request with a `Seed` or an `Rng` of its own is left out of this (checks), and whoever passes `Rng` hands the streams back himself from `run.LastFightRng`.

## The two modes

- `StreamMode.Game` (the default): the game's fifteen long streams, seeded and consumed number for number. A seed gives the run the game gives for it. `Streams.Counter(stream)` is what a save writes under `rng.counters`; `FastForward` moves a stream to a save's counter.
- `StreamMode.Keyed`: a house rule, for comparing players (plan 5.4). Every roll has a stream of its own, seeded by one hash over the seed text, the act, the node, the room depth, the purpose and how often that purpose was asked at that place. Two players on the same node then meet the same gold, the same cards and the same shop whatever they did before. What the seed alone decides is unchanged. Never mix the two modes in one batch.

## The start of a run, in the game's order

1. `act_selection`: one pick per act index among the unlocked acts of that index, also when there is only one. An unlocked act that is not a default one and was never entered is taken without a pick. With everything unlocked the first act is Overgrowth or Underdocks, half and half. Run mode uses the seeds that give Overgrowth: check `world.Acts[0]`.
2. `up_front`, in this order:
   - the shared bag: all 118 relics of the shared pool, one list per rarity in the order the rarities first appear (Uncommon, Common, Rare, Shop, Event, Ancient), each shuffled: 112 numbers;
   - the player's bag: shared pool plus the character's own, the four bag rarities (122 for the Ironclad: 32, 26, 38, 26): 118 numbers;
   - the shared Ancients (Darv) are shuffled, and each act after the first takes a number of them: one number per such act;
   - each act in turn: its events and the 18 shared ones in one shuffle; the weak hallway fights and then the regular ones from the game's grab bag (an entry that is not the one before it and shares no tag with it, drawn again until one fits; any entry when none can fit); 15 elites the same way; the boss; the Ancient.
3. Without a draw: a boss the profile never met replaces the rolled one (the act's discovery order), and a profile's very first run puts Overgrowth's first fights, events and elites in a fixed order.

A real run takes 403 numbers or more for this; the five saved seeds took 406 to 419.

**The counters of `ActRooms` once the run moves.** The game moves a list's counter after a room was entered as a map node's own room, by the room's type: `MarkVisited(RoomType.Monster)`, `Elite` and `Boss` for fights, `MarkEventVisited()` for an event room. A fight an event starts moves nothing. The Ancient's room on the act's start node is an event room too, so **Neow moves the event counter to 1**, and the first Event room of an act starts its walk at list entry 1; entry 0 is met only if the walk wraps around. The saves of seed 1BUSLN0FY5 show it: `events_visited` is 0 on Neow's screen, 1 after Neow's pick, and 2 after the run's one Event room (This or That, entry 1 of its list; entry 0, The Future of Potions, was never asked). The walk itself (allowed now, not met in this run, every entry stepped over moves the counter for good) belongs to the event room.

## The odds

All three are 32-bit floats changed by float additions, as in the game: 0.02f added five times is 0.099999994f, and a save shows exactly that.

- `CardRarityOdds`: `Roll` (a card reward: the offset counts and moves), `RollWithoutChangingFutureOdds` (a shop), `RollWithOffset`, `RollWithBaseOdds` (no offset; as the game has it, Uncommon is tested against its own odds alone: 3% Rare, 34% Uncommon, 63% Common on the regular table). A boss roll leaves the offset out and still resets it.
- `PotionOdds.Roll(rng, elite, forced)`: the number is drawn also when the drop is forced.
- `UnknownRoomOdds.Roll(rng, allowed)`: `allowed` is `UnknownRoomOdds.Allowed(shopBarred)` after the relics that take kinds away. Elite stands at -1, is never rolled and sinks by 1 a roll, as a save shows. `ResetToBase()` with every new act.

The run keeps the card rarity offset and the potion chance as numbers of `RunState`; `RunWorld.RollCardRarity` and `RunWorld.RollPotionDrop` roll on those.

## The relic bags

`RelicBag` is the game's bag alone: `PullFromFront(rarity, isAllowed, filter)`, `PullFromBack(...)`, `Remove(name)`, `Contains`, `HasAvailableRelics`, `Relics(rarity)`, `Rarities`. An empty rarity gives way Shop, Common, Uncommon, Rare, and after Rare nothing. (The bag counts the steps of that way, four at most, and throws when there are more: no rule of the game's, only so that a way that was changed into a circle fails loudly instead of searching an empty bag forever.) The shared bag refills an empty rarity from its start list, unshuffled; the player's bag does not. The rarity roll, taking a pulled relic out of the other bag, the shop's filter and what to do when a pull lands on a relic the arena has left out belong to whoever pulls.

## Golden saves

`GoldenSave.Load(path).Compare()` rebuilds a real save's start from its seed and unlock state and lists every difference: the player's seed number, the act roll, each act's events, hallway fights, elites, boss and Ancient, both bags (a later save's lists must be the generated ones with relics gone, in order), the `up_front` count. `Root` gives the whole save for readers of other parts (the saved map, the decks, the odds). `RunStreamsChecks` runs the six saves in `RunGoldenData.cs`, and every `*.save` in `$ARENA_GOLDEN_SAVES` or `~/Projects/sts2-notes/golden-saves` when that folder exists.

One of the folder's nine saves looks like a save of the start and is not: `run-164521-69b44341ea82.save` is seed 1BUSLN0FY5 again, saved seven seconds after `run-164514`, AFTER Neow's pick (Arcane Scroll, which put Barricade into the deck). Its `rewards` counter is 1 (the one number Arcane Scroll's card took), its event counter is 1, and its bags are still whole. So nothing on Neow's screen draws from `rewards` before the pick.

Only saves of v0.107.1 count. The Regent save of May 2026 that the plan named as the test was written by an older build (its third act's boss does not exist in this one, and its player seed is the seed number plus one): it cannot be rebuilt by these rules and is not used.

## A second reading

The game's code was read once more by a reader who had not written the port (chunk 7 of the plan), line by line against `RunSeed.cs`, `RunStreams.cs`, `RunGeneration.cs`, `RelicBag.cs`, `RunOdds.cs`, `RunUnlocks.cs` and `GameLists.cs`. No difference in behaviour was found. What that reading added, in `GenerationChecks`:

- **A whole real run on the lists.** The game's record of a three-act run (seed 75F277YN2G, a single Ironclad, 48 floors, `golden-saves/history/1791592972.run`) met 19 hallway fights, 4 elites and 3 Ancients, each the next entry of its act's generated list (the k-th Monster room takes hallway entry k, an Unknown room that turns out to be one included), and its one Event room took entry 1 of the event list. That is the lists in use through Underdocks, Hive and Glory, on a seed no save holds.
- **The forced boss, in the real game.** That run's third boss was Aeonglass where the roll gives the Queen: the first boss of the act's order of introduction that the profile had never met replaces the rolled one, and nothing is drawn for it.
- **The act roll on eleven real seeds** (the five saves' and six more from the run history), and the first hallway fight of four of them.
- **The Unknown room's roll, written out a second time** and held against `UnknownRoomOdds.Roll` over 30,000 rolls with every set of barred kinds, room and odds alike.
- **Names.** `GameNames.SnakeCase` and `GameNames.Slug` against the game's own pattern, run by the runtime's pattern matcher, for every name the lists hold (about 570: streams, relics, events, encounters, acts, Ancients, cards) and the corners of the pattern.
- **A pull always ends**: after Rare nothing, also in a bag that only holds Shop relics.

`Tools/check_game_lists.py <decompiled folder> Sim/Run/GameLists.cs` is the lists' second reading: every pool in order with each relic's rarity and the epoch that gates it, every act's numbers, events, encounters (room, weak, tags), Ancients with their gates and boss order, the shared events, the epochs. It prints `0 differences` on v0.107.1; run it after `game_lists.py` on a new build.

The map's second reading is in `MAP.md`.

## Left out, by name

- More than one player (one room less per act, the bags' side list for shared chests, the per-player act subset), the second boss of ascension 10, run modifiers (Deadly Events doubles the growth of the Treasure odds).
- The rest of a profile's very first run: its list order is here; the Unknown rooms it fixes (Event, Event, Monster), its missing Neow and its scripted rewards are not. Arena runs use `RunUnlocks.All`.
- The game's bad-word check on seeds it makes itself: `RunSeed.Random` can make a seed the game would have drawn again. It is still a seed a player can type.
