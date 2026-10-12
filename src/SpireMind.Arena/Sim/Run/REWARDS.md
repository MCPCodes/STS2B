# Run mode: rewards

What a won fight gives, the cards and potions the game makes outside fights, the `rewards` and `card_reward` screens, and potions outside fights. Chunk 4 of the run-mode plan (`sts2-notes/run-mode-v0.107.1/plan.md`, 1.6 and section 2). The frame of a run is in `README.md`, the streams and odds in `STREAMS.md`, both next to this file.

Everything here is the game's (v0.107.1, single player, ascension 0), read from its code: `MegaCrit.Sts2.Core.Rewards/*`, `MegaCrit.Sts2.Core.Factories/CardFactory.cs` and `PotionFactory.cs`, `MegaCrit.Sts2.Core.Runs/CardCreationOptions.cs`, `MegaCrit.Sts2.Core.Nodes.Screens/NRewardsScreen.cs`, `MegaCrit.Sts2.Core.Commands/PotionCmd.cs`. It is held against the game's own record of a real run: on seed 1BUSLN0FY5 the code gives Barricade for Arcane Scroll, then 11 gold, no potion and Colossus, Unrelenting, Havoc after the first fight, then 10 gold, a Vulnerable Potion and Fight Me!, Stone Armor, Setup Strike after the second, with the stream counter and both odds as the saves have them.

## Files

| File | Holds |
| --- | --- |
| `Rewards.cs` | `RewardRoom`, `Reward` and its six kinds (`GoldReward`, `PotionReward`, `RelicReward`, `SpecialCardReward`, `CardReward`, `CardRemovalReward`), `RewardsSet`, `SmallSort`, `RunRewards` |
| `CardFactory.cs` | `CardCreationOptions`, `CardCreationSource`, `CardCreationFlags`, `CardCreationResult`, `CardFactory` |
| `PotionFactory.cs` | `PotionFactory`: random potions outside a fight |
| `RunPotions.cs` | `RunPotions`: the potion block, ids, use, discard and gain outside a fight; `PotionProcureResult` |
| `RewardsScreen.cs` | `RewardsScreen`, `CardRewardScreen`, `IHearsRewardsClosed` |
| `RewardHooks.cs` | `IRewardHooks`, `RewardHooks` (the call sites), `RewardsState` |
| `RewardsChecks.cs` | the checks |

## Using it

```csharp
// After a won fight (a director's AfterFightWon): the room's rewards screen. Its proceed calls run.RoomFinished().
RunRewards.OfferForRoomEnd(run, fight);

// Rewards an event or a relic offers: a screen that closes by itself and shows what was under it again.
RunRewards.OfferCustom(run, [new CardReward(run, CardCreationOptions.ForNonCombatWithDefaultOdds([CardPools.Character]), 3), new PotionReward()]);

// Cards for anything else that rolls "cards for a reward" (an event's grid, Arcane Scroll's one Rare):
var cards = CardFactory.CreateForReward(run, 3, options);          // List<CardCreationResult>
var potion = PotionFactory.CreateRandomPotionOutOfCombat(run, world.Streams.Draw(RunStream.Rewards, Purpose.Potion()));

// A screen outside a fight that offers potions (map, shop, rest, treasure, event):
state["potions"] = RunPotions.Describe(run);
actions.AddRange(RunPotions.Legal(run));                            // only beside at least one action of the screen itself
if (RunPotions.TryExecute(run, actionId, out var result)) return result;
```

Rewards need the run's world (`RunWorld.Begin`): their rolls are draws of the player's `rewards` stream. They are for outside a fight.

`OfferForRoomEnd` returns null when there is no screen: a dead player, an encounter that gives no rewards (`RewardRoom.ShouldGiveRewards` false: the room is then simply finished), a treasure room no relic added anything to (`RewardRoom.Treasure`).

## The draw order of a fight's rewards

It follows from how the game builds a set (`RewardsSet.cs:85-147, 206-256`):

1. Making the set rolls whether a potion drops: one number (`Purpose.PotionChance`). The hooks are asked first whether the drop is forced; the number is drawn either way.
2. Filling the set rolls each reward in the order the set was made in:
   - the gold: one number (`Purpose.Gold`), also for a fixed amount such as a boss's 100;
   - the potion, if one dropped: two numbers, rarity and pick (`Purpose.Potion(row)`);
   - each of the three cards: three numbers, rarity, pick, upgrade roll (`Purpose.Card(reward, card)`);
   - after an elite, the relic's rarity: one number (`Purpose.RelicRarity(row)`), then the front of the player's bag (`RunRelics.RollAndPullFromFront`).
3. The hooks may add rewards (`TryModifyRewards`, then `TryModifyRewardsLate`). What they added and is not filled yet is filled in the order added.
4. The rows are sorted for the screen: gold, potion, relic, special card, card, removal. The sort is the game's list sort (`SmallSort`): three rows are settled by fixed swaps, which turns two equal rows round when a lower one stands behind them (two card rewards made before a gold row change places).

So a hallway fight takes 11 numbers, or 13 with a potion; an elite 12 or 14; a boss 11 or 13. The boss of the run's last act gives nothing and draws nothing.

Gold: 10 to 20 after a hallway fight, 35 to 45 after an elite, 100 after a boss. Only a hallway fight pays by the share of its monsters that did not get away: `RewardRoom.GoldShare` is 1 minus the escaped creatures over the KINDS of monster the fight had (the game keeps each monster class once), each bound is rounded to the nearest whole number with a half going to the even one, and a share of 0 or less means no gold row. `RewardRoom.Of(FightOutcome)` counts the distinct titles in the outcome's enemy names; a check holds that no two monster classes of the arena share a title. No Overgrowth monster escapes.

## Cards

`CardFactory.CreateForReward(run, count, options, purpose)` is the game's, number for number. Per card:

- the options hooks (`ModifyCardRewardCreationOptions`), which may change the offer's own options object for the cards that follow;
- the cards it may be: the unlocked single-player cards of the options' pools that pass the filter (or the caller's list), each class once, without the cards the offer already shows;
- the rarity. A fight's reward on the regular, elite or boss table, and anything that sets `ForceRarityOddsChange`, rolls with the run's pity offset and moves it (`RunWorld.RollCardRarity`). Everything else rolls on the table alone, where the game tests Uncommon against its own odds only: Rare below 0.03, Uncommon below 0.37, else Common. With `Uniform` odds no rarity is rolled at all and Basic and Ancient cards are left out;
- a rarity the cards do not have gives way: Common to Uncommon, Uncommon to Rare, Rare to Common; back at the rolled one the game fails, and so does this;
- the pick, one index into the cards of that rarity;
- the upgrade roll, unless the options say `NoUpgradeRoll`: one number whatever the card is; a card that can be upgraded comes upgraded when the number is at or below 0.25 times the acts cleared (0 for a Rare card), after `ModifyCardRewardUpgradeOdds`. In act 1 that is a roll of exactly 0.

After the last card the hooks see the whole offer (`TryModifyCardRewardOptions`, then the Late pass), unless the options say `NoModifyHooks`.

`CardFactory.CreateForMerchant` makes a shop's cards: by type (the rarity rolled on the shop table with the pity offset, which stays as it is) or by rarity (the colorless slots). The rarity and the dead upgrade roll come from `rewards`, the pick from `shops`.

`CardCreationOptions` is one changeable object the offer owns, as in the game: `With...` changes it and returns it. A pool filter is a function over `GameCard` and must not hold anything of a run.

## Potions

`PotionFactory`: a rarity (Rare at or below 0.1, Uncommon at or below 0.35, else Common), then one of the potions of that rarity out of the game's list for the character: the Ironclad's three, then the 45 shared ones (`Scenarios.PotionPool`), without those of an epoch the profile has not revealed.

`RunPotions` is the mod's `PotionActions` outside a fight. A potion can be used when the game lets it be used at any time and it is not aimed at an enemy: today Blood Potion and Fruit Juice. Every held potion can be thrown away. No potion action is offered for a dead player or while an event holds the belt (`RewardsState.CanRemovePotions`). Using one runs the potion's own effect (`PotionDef.Use`) on the player as it stands between rooms (`Run.OutOfCombat`): it leaves the belt first, and the run's listeners hear the fight's two potion hooks around the effect. A check uses every any-time potion of the registry that way.

## The screens

`RewardsScreen` and `CardRewardScreen` write what `docs/screens.md` says the mod's readers write: keys, order, labels, refusals.

- A row's number is its place among the rows still there. A potion row with a full belt is listed with `available: false` and the note, and has no action; the potion ids are then offered even when nothing else is.
- **Terminal** screens are the last screen of a fight's room. They stay when every row is taken; proceed then reads `Proceed` instead of `Skip (leave without the unclaimed rewards)`. Proceed skips what is left, takes the screen off the run's screens and calls `run.RoomFinished()`: the director's `AfterRoom` finds whatever was under the fight (an event) still on the screens, or none. In a boss's room the state has `proceed_ends_act: true` and the label is `Go on to the next act`.
- Every other rewards screen closes by itself when its last row is taken, and its proceed (always Skip) closes it. The screen right under it is told if it implements `IHearsRewardsClosed`. `RewardsSet.WithSkippingDisallowed()` takes the proceed button away.
- A card reward opens as a **question** of its `reward:<n>` action (`run.Ask`): `card:<n>` or `skip` answers, and the action runs again from its start with the answer. Nothing points from one screen to another, so copies are safe. A card reward that was opened and skipped lists its cards from then on.
- From the moment a rolled card reward is made until its screen was opened once, or its rewards screen is left, a relic the player obtains is asked whether it changes that reward (`RewardsScreen.OnRelicObtained`, the relic framework's `IHearsRelicObtained`). The game tells such a relic "rewards were changed", not the card-offer form; so does this.

## The hooks

The run's hooks are the relic framework's: declared on `Listener`, sent through `RunHooks`, heard by the deck cards that listen, the relics and the potions in the game's order (`RELICS.md`). The hooks whose arguments are rewards, card offers and their options are declared here instead, on the interface `IRewardHooks`, since those types are the rewards'. A relic (or a deck card's hook object, or a potion's) opts in by implementing it and writes the methods it needs; the interface carries the marker `IListensInDeck`, so a card's hook class that implements it is heard as the deck card. `RewardHooks` has their call sites, each built from `RunHooks`' own helpers, so the listeners and their order have one definition (`run.Listeners()`). No hook is declared in both places; `RunHooks.Hooks` lists the six of these that reachable relics use, with the call site of each.

| Hook | Declared on | Sent from |
| --- | --- | --- |
| `ShouldForcePotionReward` | `Listener` | `RewardsSet.WithRewardsFromRoom`, before the potion roll (`RunHooks.ShouldForcePotionReward`) |
| `TryModifyRewards`, `TryModifyRewardsLate` | `IRewardHooks` | `RewardsSet.GenerateWithoutOffering` (`RewardHooks.ModifyRewards`) |
| `AfterModifyingRewards` | `Listener` | the same, to those that changed something (`RunHooks.AfterModifyingRewards`); and to a relic that changed a waiting card reward when it was obtained |
| `AfterRewardTaken` | `IRewardHooks` | `Reward.Select` |
| `ShouldProceedToNextMapPoint` | `IRewardHooks` | `RewardsScreen.CanProceed` |
| `ModifyCardRewardCreationOptions` (and Late) | `IRewardHooks` | `CardFactory`, before each card of an offer |
| `TryModifyCardRewardOptions` (and Late) | `IRewardHooks` | `CardFactory.CreateForReward` after the last card; `CardReward.Populate` for named cards; `CardReward.OnRelicObtained` |
| `AfterModifyingCardRewardOptions` | `Listener` | after the two above, to those that changed something (`RunHooks.AfterModifyingCardRewardOptions`) |
| `ModifyCardRewardUpgradeOdds` | `IRewardHooks` | `CardFactory.RollForUpgrade` |
| `ShouldAllowSelectingMoreCardRewards`, `TryModifyCardRewardAlternatives` | `IRewardHooks` | `CardReward` |
| `ModifyMerchantCardPool`, `ModifyMerchantCardRarity` | `IRewardHooks` | `CardFactory.CreateForMerchant` |
| `ModifyMerchantCardCreationResults` | `IRewardHooks` | the shop, for each card it made (`RewardHooks.ModifyMerchantCardCreationResults`) |
| `TryModifyRestSiteHealRewards` | `IRewardHooks` | the rest site (`RewardHooks.ModifyRestSiteHealRewards`), which then offers the list as custom rewards |
| `ShouldProcurePotion` | `IRewardHooks` | `RunPotions.TryToProcure` |
| `AfterPotionProcured`, `AfterPotionDiscarded` | `Listener` | `RunPotions.TryToProcure`, `RunPotions.Discard` (`RunHooks`) |
| `BeforePotionUsed`, `AfterPotionUsed` (the fight's) | `Listener` | a potion used outside a fight, through `Run.OutOfCombat` |

## What the arena has not built

One rule: a roll runs over the game's full list, and one that lands on something the arena has not built is drawn again from the same stream and written to the run's `LeftOutLog`. In strict mode (`LeftOutLog.Strict`) nothing is drawn again: `RunUnrepresentableException` is thrown, and whoever runs the act ends the run as unrepresentable. The same exception is thrown in either mode when nothing built is left to land on.

- A card: the pick alone is drawn again (`CardPools.Pick`); the rarity roll has already moved the pity offset.
- A potion: rarity and pick are both drawn again, as the single-fight scenarios have always done.
- A relic: the relic framework's pull decides (`RunRelics`: the next built relic of the same list, a rolled rarity rolled again when the list has none). A relic reward that gets nothing throws the exception with the pull's reason.

The exception flies out of `RunRewards.OfferForRoomEnd` and out of a rewards screen's action, and nobody has to catch it: since the merge of wave 2 the run itself does (`Run.Perform`), in an action outside a fight and in what follows a won fight alike. The run ends as `unrepresentable` with the exception's text as the reason, standing as it stood before that action.

## House rules and what is left out, by name

- Drawing again, above.
- Reroll (Driftwood) and Sacrifice (Pael's Wing) on a card reward: relics of act 2 Ancients. A hook that adds a button is refused.
- The rng override on a reward and on card options: only the Crystal Sphere of act 2 sets one.
- Linked rewards (take one, lose the others): nothing in the game makes one.
- The scripted rewards of a profile's very first run.
- The second boss of an ascension level (its rewards lead back to the map).
- A sort of more than sixteen rows (`SmallSort`): .NET takes another path there.
- A relic whose pickup opens a screen of its own while it is taken from a rewards row: the row is settled at once, where the game settles it once that screen has closed. No decision differs.
- What a card reward writes into the game's run history. The floor log is the run loop's.

## Checks

`RewardsChecks`: the real run's rewards from its seed, also played through the screens; 1,000 scripted fights (hallway, elite, boss) each compared with a second, straight-line reading of the game's draw order that calls nothing of the rewards; the elite's bonus on the potion roll and the relic's place in the order; every kind of row and the screens' shapes, labels and refusals; potions outside fights; one check relic per hook; the card factory's two rarity rolls, the next-rarity rule, uniform odds, the upgrade roll and the shop's cards at places of the stream chosen so that the outcome can be worked out by hand; the potion thresholds and list; the gold share; the three-row sort; copies, the save, keyed streams.
