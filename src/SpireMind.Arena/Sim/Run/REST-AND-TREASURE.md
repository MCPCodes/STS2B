# Run mode: the rest site and the treasure room

Two rooms that are no fight: the rest site with Rest and Smith, and the treasure room with its chest. Chunk 8 of the run-mode plan (`sts2-notes/run-mode-v0.107.1/plan.md`, 1.5 and section 2). The frame of a run is in `README.md`, the hooks and the relic pulls in `RELICS.md`, rewards and potions in `REWARDS.md`, the card choice in `DECK.md`, all next to this file.

Everything here is the game's (v0.107.1, single player, ascension 0), read from its code: `MegaCrit.Sts2.Core.Entities.RestSite/*`, `MegaCrit.Sts2.Core.Rooms/RestSiteRoom.cs` and `TreasureRoom.cs`, `MegaCrit.Sts2.Core.Multiplayer.Game/RestSiteSynchronizer.cs`, `TreasureRoomRelicSynchronizer.cs` and `OneOffSynchronizer.cs`, `MegaCrit.Sts2.Core.Nodes.Rooms/NRestSiteRoom.cs` and `NTreasureRoom.cs`. The screens are the mod's: `src/SpireMind/Game/Screens/RestSiteReader.cs` and `TreasureReader.cs`, `docs/screens.md`.

It is held against the game's own record of a real run (seed 75F277YN2G, `sts2-notes/golden-saves/history/1791592972.run`): the chest of floor 10 gave Strike Dummy and 51 gold, both of which follow from the seed and from what the run had drawn before; the rest sites of floors 8, 12 and 16 were Smith on Inflame at 49 HP and two rests of 24 HP (35 to 59, 52 to 76).

## Files

| File | Holds |
| --- | --- |
| `RestSite.cs` | `RestSiteOption` with `HealRestSiteOption` and `SmithRestSiteOption`, `RestSite` (entering, the options, the heal), `RestSiteScreen` |
| `Treasure.cs` | `TreasureRoom` (entering), `TreasureScreen`, `TreasureState` |
| `RestSiteChecks.cs`, `TreasureChecks.cs` | the checks |

## Using it

```csharp
// A director's EnterRoom (IMapDirector), after MapState.Enter:
case RoomKind.RestSite: RestSite.Enter(run); break;
case RoomKind.Treasure: TreasureRoom.Enter(run); break;
```

Each puts its screen up and sends "after room entered" at the game's moment. Neither moves a counter of the act's lists. The screens' `proceed` takes the screen off and calls `run.RoomFinished()`, so the director's `AfterRoom` opens the map.

An event that lets the player rest (Dense Vegetation) calls `RestSite.ExecuteHeal(run, isMimicked: true)`.

## The rest site

**Entering** (`RestSiteRoom.EnterInternal`), in this order:

1. The options are made: Rest (`HEAL`) and Smith (`SMITH`). Then every listener may add to the list or take away from it: `Listener.TryModifyRestSiteOptions(c, options)`, deck cards first, relics in pickup order. `RestSite.GenerateOptions` is the call site; the game does nothing with who changed the list.
2. The screen is up.
3. "After room entered" with `RoomKind.RestSite`.

**Rest** heals 30% of max HP (`RestSite.BaseHealAmount`: 24 at 80, 26.1 at 87, not rounded) after every listener's `ModifyRestSiteHealAmount` (`RestSite.HealAmount`). HP is a whole number and the sum is cut, not rounded: 50 + 26.1 is 76, 10 + 22.5 is 32. The option and its label name the amount cut to a whole number (`RestSite.ShownHealAmount`), as the mod's label does; what is restored is no more than what is missing. Rest is offered at full HP too. After the heal the listeners hear `AfterRestSiteHeal(c, isMimicked: false)`; then the rewards a rest gives besides are collected (`IRewardHooks.TryModifyRestSiteHealRewards`: Tiny Mailbox two potions, Dream Catcher a card reward) and offered as custom rewards. All of that is `RestSite.ExecuteHeal`.

- The game drops the list of who added rest rewards (`HealRestSiteOption.cs:111`), so nobody is told `AfterModifyingRewards` for it. The custom set then runs its own hooks like any other.
- A row of that list was made with the set, so a gold row of a fixed amount there still draws its number (`REWARDS.md`).

**Smith** asks for one card on the upgrade screen: `CardSelect.FromDeckForUpgrade` with the game's prefs, which can be closed (`Cancelable`) and which ask even when a single card can be upgraded (`RequireManualConfirmation`). The chosen card is upgraded where it stands. Closing the screen without a choice is no success. Smith is greyed out (`available: false`, not offered) while no card of the deck can be upgraded.

**One option, then the rest vanish** (`RestSiteSynchronizer.ChooseOption`). When an option succeeded: its id is noted (`RestSiteScreen.ChosenIds`, the game's run history), the listeners are asked `ShouldDisableRemainingRestSiteOptions` (the first no decides; Miniature Tent says no), the list loses every option or this one alone, and the proceed button turns on. An option that did not succeed changes nothing: no question, no proceed.

- An option has run to its end only when the screens it opened are closed: the game awaits a rest's rewards before it asks whether the other options go. `RestSiteScreen` is an `IHearsRewardsClosed` and finishes the option when that rewards screen closes (every row taken, or Skip). An option may open rewards screens and ask questions (`run.Ask`); any other kind of screen has no rule here.
- The proceed button is also on when the list is empty on arrival.

**The screen** (`rest`). Keys in order: `run`, `hp`, `max_hp`, `boss`, `player` (`PlayerKeys.Gold | PlayerKeys.Deck`), `potions`, `options`, and `chosen` once an option was used. An option: `n` (its place in the list as it stands now), `id`, `name`, `text` when not empty, `available: false` when it cannot be chosen. Actions: `option:<n>` for each option that can be chosen, `proceed`, then the potion ids.

| Id | Label |
| --- | --- |
| `option:<n>` on Rest | `Rest (heal 24)` |
| `option:<n>` on Smith | `Smith (upgrade a card)` |
| `option:<n>` on any other option | its title and, in brackets, the first sentence of its text without the full stop, cut to 67 characters and three dots when longer than 70; the title alone when it has no text |
| `proceed` | `Proceed`, or `Proceed (leave the remaining options unused)` while an option can still be chosen |

Refusals: `There is no rest site option <n>.`, `<Title> is not available right now.`, `The proceed button is not enabled yet.`, `Unknown rest site action '<id>'.`, and the potion ids' own.

The texts are the arena's own: `Heal 24 HP.` with ` / ` and one part for every line a relic adds (`ModifyExtraRestSiteHealText`), `Upgrade a card in your deck.`, `No card in your deck can be upgraded.`

**Writing an option** (for the relic or card that brings one):

```csharp
public sealed class LiftRestSiteOption : RestSiteOption          // LiftRestSiteOption.cs
{
    public override string OptionId => "LIFT";
    public override string Title => "Lift";
    public override string Describe(Run run) => $"... {3 - run.State.FindRelic<Girya>()!.TimesLifted} lifts left.";
    public override bool OnSelect(Run run) { run.State.FindRelic<Girya>()!.TimesLifted++; return true; }
}

public override bool TryModifyRestSiteOptions(RunContext c, ICollection<RestSiteOption> options)   // on the relic
{
    if (TimesLifted >= 3) return false;
    options.Add(new LiftRestSiteOption());
    return true;
}
```

An option is copied with the screen (`Clone`, by default field by field) and never keeps a `Run`. Two options are the same when their ids are, as in the game, so a hook can `Remove` or look up an option with a new object of its class. Move the option's id from `RestSite.LeftOut` to `RestSite.Built` when it is built: a check holds the two lists to the game's nine.

## The treasure room

**Entering** (`TreasureRoom.EnterInternal`), in this order:

1. The screen is up. "After room entered" with `RoomKind.Treasure` (Silver Crucible counts here).
2. The chest's relic is settled now, not when the chest is opened (`TreasureRoomRelicSynchronizer.BeginRelicPicking`). The listeners are asked `ShouldGenerateTreasure` (the first no decides). On yes: one number of the run's `treasure_room_relics` stream rolls the rarity (`Purpose.ChestRarity`; Common below 0.5, Uncommon below 0.83, else Rare), and the relic is the front of that rarity's list in the SHARED bag (`RunRelics.PullFromShared`). It has left the shared bag at once and leaves the player's bag only when it is obtained. On no: nothing is rolled and the chest holds no relic.

**Opening the chest** (`NTreasureRoom.OpenChest`), in this order:

1. The listeners are asked `ShouldGenerateTreasure` again. On yes: one number of the player's `rewards` stream gives 42 to 52 gold (`Purpose.ChestGold`), paid at once through `run.GainGold`, so the gold hooks hear it. No rewards screen for it.
2. The room's rewards are generated and offered: `RunRewards.OfferForRoomEnd(run, RewardRoom.Treasure)`. A treasure room has none of its own; a screen shows only when a listener adds some (`IRewardHooks.TryModifyRewards` with `RoomKind.Treasure`). That screen lies over the chest until it is closed, taken or skipped.
3. The relic is on show.

The two questions are independent, as in the game: the relic goes by the answer on entering, the gold by the answer on opening.

**Taking the relic** is `run.ObtainRelic`: into the relics, out of both bags, pickup effect. Then proceed is on. The game also lets a single player skip a chest's relic; the mod never offers that, and neither does the arena: `proceed` is refused while the relic is in the chest.

**The first chest of a profile's very first run** holds Gorget whatever the roll says (`TryGetRelicForTutorial`): the rarity is rolled all the same, Gorget leaves the shared bag, nothing is pulled. `TreasureState.RoomsEntered` counts the run's treasure rooms for it. Arena runs use a profile with runs behind it, so this never comes up outside a check.

**The screen** (`treasure`). Keys in order: `run`, `chest_open`, `relics` (only while the chest is open and its relic not taken: `n`, `name`, `text`), `picked` and `relic_awarded` (once the relic is taken; the award is at once, so `relic_awarded` is always true), `player` (`PlayerKeys.NoPotions`), `potions`. Never `boss`. Actions, then the potion ids:

| State | Action | Label |
| --- | --- | --- |
| the chest is shut | `open` | `Open the chest` |
| open, a relic in it | `relic:0` | `Take relic <name>` |
| open, the relic taken | `proceed` | `Leave the treasure room` |
| open, an empty chest | `proceed` | `Leave the treasure room (no relic on offer)` |

Refusals: `The chest was already clicked once; waiting for it to open.`, `The relics are still appearing.` (a relic before the chest is open), `The game isn't taking relic votes right now.` (a relic of an empty chest), `There is no relic <n>.`, `Already picked <name>.`, `The relic hasn't been awarded yet; leaving now would lose it.` (proceed while the chest is shut or its relic not taken), `Unknown treasure room action '<id>'.`

`TreasureScreen` also says what a floor log wants: `GameRelic` (the class name of the relic the game puts into this chest, built or not), `RolledRarity`, `Gold` (what was paid), `Picked`.

## Relics the arena has not built

The chest's pull follows the rule of every pull (`RELICS.md`). The game's relic is built: it is in the chest. It is not: in strict mode (`LeftOutLog.Strict`) entering the room throws `RunUnrepresentableException` and the run ends as unrepresentable, standing as before the action that travelled; otherwise the next built relic of the same list is taken, and every relic passed over is noted in the run's `LeftOutLog` as `relic`, "a chest's relic", with the floor. When the list holds no built relic the rarity is rolled again on the same stream; when no rolled rarity has one the run ends as unrepresentable in either mode. The first run's Gorget is not rolled, so it cannot be drawn again: not built, the run ends.

`TreasureRoom.Enter(run, isBuilt, newRelic)` takes the checks' own answer to "which relics are built" and their own way of making one, as the pulls do.

## Left out by name

- **Rest site options** other than Rest and Smith (`RestSite.LeftOut`, each with its reason): Mend (more than one player), Dig (Shovel), Lift (Girya), Hatch (Byrdonis Egg), Cook, Kindle and Clone (relics of Ancients of acts 2 and 3). None can appear before its relic or card is built.
- **"After rest site smith"**: the game sends it after Smith's upgrade, and no relic, card, potion or enchantment of v0.107.1 overrides it.
- **More than one player** in the treasure room: one relic per player, the votes, the fight over a relic, the consolation prize, the other players' bags.
- **The game's Skip** of a chest's relic.
- **The gold multiplier of an ascension level** on a chest, and **the gold Spoils Map adds** to a chest it marked (that card rewrites the map of act 2).
- **The fallback relic Circlet** for an empty bag: the pull finds nothing and the run ends as unrepresentable.
- **The tutorial popups** of a profile's first rest site and first relic, and **the run history** the game writes.
- **The mod's waiting states** (arriving, an option playing out, relics rising, the award): one action here is done when it returns.

## Checks

`RestSiteChecks`: the arrival snapshot of `docs/screens.md` key for key; Rest at 41, 50 of 87, 10 of 75, 70 and 80 HP; the real run's three rest sites; Smith with its grid, its preview, both ways back and a deck with one or no card to upgrade; the heal's hooks with the game's Regal Pillow, Stone Humidifier and Tiny Mailbox and the order they are asked in; a rest's rewards taken and skipped, and when the option counts as used; a mimicked rest; Miniature Tent; options added by a relic and by a deck card, taken away, and none left; labels and refusals; potions; the nine option ids; replay and a copy in the middle of Smith's card choice.

`TreasureChecks`: the real chest (Strike Dummy from the first number of `treasure_room_relics` and the shared bag's front; 51 gold as the 85th number of `rewards`, with the 84 before it counted from the record); the order of hooks and draws on entering and opening; the two questions answered differently; 300 chests' gold against a second reading of the stream, with 42 and 52 both reached; the gold hooks; the screen in its three states; Silver Crucible's empty chest and the chest after it; rewards a relic adds, taken and skipped; a relic that is not built, strict and not; the first run's Gorget; keyed streams against the seed of each purpose; refusals; a walk over the real map of seed 1BUSLN0FY5 to the chest of row 9 (floor 10) and the rest site of row 15 (floor 16); replay and copies.

The relics in both are the checks' own stand-ins or the game's classes as far as these rooms ask, written in the checks because none of them is built yet.
