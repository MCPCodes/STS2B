# Run mode: a second reading of what the rooms roll

Chunk 17 of the run-mode plan (`sts2-notes/run-mode-v0.107.1/plan.md`, 5.2): a second reader of the rewards (chunk 4), the rest site and the treasure room (chunk 8) and the shop (chunk 10). The rooms themselves are described in `REWARDS.md`, `REST-AND-TREASURE.md` and `SHOP.md`, next to this file. This page says what the second reader made, how to follow one of its rooms by hand, what it found, and how to make it again after a game update.

## Files

| File | Holds |
| --- | --- |
| `Tools/trace_rooms.py` | the second reader: a script that rolls rooms the way the game's code does, and shares nothing with the arena |
| `Tools/trace_rooms_built.txt` | what the arena held on 2026-10-11, for the script's seed searches only |
| `RoomTraces.cs` | what the script made of three seeds: 30 rooms (20 fights, 4 shops, 3 rest sites, 3 chests), six purchases in a shop whose places are filled again, 11 shops with a price on a half, 7 rooms with a roll exactly on its threshold; and floors 2 to 14 of a real run, typed in from the game's own record |
| `RoomTracesChecks.cs` | the checks: the arena's rooms on the same seeds, held to `RoomTraces.cs` |

## The second reader

`trace_rooms.py` was written from the game's code alone (v0.107.1): `MegaCrit.Sts2.Core.Random/MegaRandom.cs` and `Rng.cs`, `Helpers/StringHelper.cs`, `Runs/RunRngSet.cs`, `Random/PlayerRngSet.cs`, `Runs/RelicGrabBag.cs`, `Factories/RelicFactory.cs`, `CardFactory.cs` and `PotionFactory.cs`, `Odds/CardRarityOdds.cs` and `PotionRewardOdds.cs`, `Rewards/RewardsSet.cs` and the reward classes, `Entities.Merchant/*`, `Multiplayer.Game/TreasureRoomRelicSynchronizer.cs` and `OneOffSynchronizer.cs`. It has:

- its own generator (xoshiro256** seeded through SplitMix64), its own string hash and its own 32-bit float arithmetic;
- its own lists, read from the decompiled class files each time it runs: the shared and the Ironclad relic pool with every relic's rarity, the Ironclad and the colorless card pool with type, rarity and the multiplayer-only mark, the Ironclad's three potions and the 45 shared ones with their rarities, and the relics a shop never sells;
- both relic bags, made from the seed on the `up_front` stream as the game makes them;
- the draw order of a fight's rewards, a shop, a chest, written out room by room.

It is held to the game's own records of two real runs (`sts2-notes/golden-saves/history`). With nothing but the seed it gives:

| Seed | Rooms | What the game's record has, and the script prints |
| --- | --- | --- |
| 1BUSLN0FY5 | `A,M,M,V,S` | Barricade for Arcane Scroll; 11 gold, no potion, Colossus, Unrelenting, Havoc; 10 gold, Vulnerable Potion, Fight Me!, Stone Armor, Setup Strike; Stone Cracker as the event's relic (the 26th number of `rewards`); the shop: Tear Asunder on sale for 77, Ashen Strike, Tremble, Blood Wall, Vicious, Discovery, Calamity, Paper Phrog, Pendulum, Ringing Triangle, Block Potion, Potion of Binding, Shackling Potion |
| 75F277YN2G | `M,S` | the shop of floor 3: Pommel Strike, Anger on sale for 24, Taunt, Second Wind, Juggling, Jack of All Trades, Secret Technique, Lantern, Red Skull, Burning Sticks, Stable Serum, Swift Potion, Weak Potion |
| 75F277YN2G | `M,S,M,M,M,E,R,M+5,T,M,R,E,M` | every floor from 2 to 14 as the record has it: nine fights with their gold, their three cards and their potions (Ashwater, Fysh Oil, Power Potion), both elites' relics (Happy Flower, Bag of Marbles), and the chest of floor 10 with Strike Dummy and 51 gold as the 85th number of `rewards`. Floor 15 is an event, where the script stops |

```bash
python3 -I src/SpireMind.Arena/Sim/Run/Tools/trace_rooms.py ~/Projects/sts2-decompiled-v0.107.1 trace 1BUSLN0FY5 A,M,M,V,S
```

Room codes: `M` a hallway fight's rewards, `E` an elite's, `B` a boss's, `M+23` a hallway fight that also hands back 23 stolen gold (a gold row the reward set was made with), `S` a shop, `S!card3/potion1` a shop in which the card of place 3 and the potion of place 1 are bought and their places filled again, `R` a rest site, `T` a chest; `A` (Arcane Scroll's card) and `V` (an event's relic) exist only to hold the script to the first record.

## Following a room by hand

`trace` prints every number and what it decides. The third room of seed QEVNVN1RKF, an elite:

```
room 3: E   (rewards at 24, shops at 0, treasure_room_relics at 0)
  elite
    rewards #25: 0.846515953540802 against a potion below 0.5249999761581421: no potion
    rewards #26: 35 gold (35 to 45)
    rewards #27: 0.8019296526908875 against Rare below 0.10999999940395355, Uncommon below 0.5099999904632568 (offset 0.009999999776482582): Common
    rewards #28: place 19 of 20 Common cards: TwinStrike
    rewards #29: the upgrade roll, 0.24329373240470886: no upgrade
    ...
    rewards #36: 0.21413765847682953: a Common relic; the front of the player's Common list: BagOfPreparation
```

The potion chance stood at 0.4 after one drop and one miss; an elite adds 0.125 to this roll. The pity offset had grown to 0.01 over six cards that were not Rare. Each line can be worked out with the game's file open: the potion roll is `PotionRewardOdds.Roll`, the gold `GoldReward.Populate`, a card's three numbers `CardFactory.CreateForReward` with `CardRarityOdds.Roll`, the relic `RelicFactory.RollRarity` and `RelicGrabBag.PullFromFront`. `RoomTraces.cs` holds the same room as one line of data.

## The three runs of the checks

| Seed | Rooms |
| --- | --- |
| QEVNVN1RKF | `M,M,E,S,M,R,E,M,B,T` |
| YG4K5JKTX0 | `S,M,E,M,M+23,R,S,E,B,T` (a shop before anything was rolled, a second shop, a fight with stolen gold) |
| KPJ8BS3DSJ | `M,E,E,M,S,M,M,R,B,T` |

They were looked for with the script's `find`: the first act is Overgrowth, and on the streams the rooms meet nothing the arena had not built on 2026-10-11 (every potion that drops or is shelved, both colorless cards of every shop). So the arena draws nothing again, and every room must be the game's own for its seed: cards, potions, gold, prices, counters, odds. About one seed in a hundred passes for a run with one shop, one in three hundred for two.

**Relics are the exception, and the checks are written for it.** Most of the game's relics are not built; a pull then takes the next built relic of the same list without drawing a number and notes what it passed over in the run's log. `RoomTraces.cs` therefore names the relic the game gives and carries both relic bags of each seed in the game's order. `RoomTracesChecks` pulls from one copy of the bags as the game does (which must give the trace's relics) and from another as the arena does (the first relic of the list the registry holds), and the run must show that relic and log the ones before it. The only thing asked of the arena there is "is this relic built".

**The checks do not change when content is built.** The player of the runs takes the gold and the first card of every fight, leaves potions and relic rows, buys nothing, rests, and takes a relic only from the chest that ends the run. So no relic, potion or card built later can change a number of these rooms; a relic that gets built is simply shown where the game shows it, and the check, which asks the registry, expects exactly that.

## A real run through the rooms

`RoomTraces.Recorded` is not the script's work: it is the game's own record of Mark's winning run on seed 75F277YN2G (`golden-saves/history/1791592972.run`), floors 2 to 14, typed in by hand: nine fights (gold, the three cards, which one was taken, the potion, an elite's relic), the shop of floor 3 (the four character cards that were left, Anger bought on sale for 24, the three relics, the first colorless card) and the chest of floor 10. `RoomTracesChecks` walks them through the rooms' entry points and takes what the record's player took.

- The run's first act is Underdocks. The arena cannot play its fights, and does not have to: what a room rolls does not ask which act it stands in.
- It is walked in two stretches, floors 2 to 10 and 11 to 14, so that the relic taken from the chest of floor 10 (not the game's while Strike Dummy is not built) cannot reach the later floors. The second stretch starts where the real run stood: `rewards` at 85, the pity offset and the potion chance as the script has them there, and the five relics gone from the bags that the run had seen.
- Where the game's relic is not built, the check holds that the run's log names it as passed over; where it is, that it is shown. The shop's first colorless card (Jack of All Trades) the same way. What follows a redraw on the `shops` stream (the second colorless card, the potions) is not the game's and is not compared; the `rewards` stream is not touched by it, so every later fight is.

With the two fights of seed 1BUSLN0FY5 in `RealRunChecks`, eleven real fights of the game hold the reward order.

## Places filled again

With a relic that has a bought place stocked again (the game's The Courier; a check relic here), every purchase rolls: a card place a rarity and an upgrade roll on `rewards` and a pick and one price on `shops`; a potion place a rarity, a pick and a price on `shops`. `RoomTraces.Refilled` is the first shop of seed YG4K5JKTX0 with six purchases, traced by the script (room `S!card3/card0/potion1/card3/potion1/potion0`): the card on sale is bought for its halved price and replaced by a card at a full price that is never on sale, the card that replaced it is bought again, and three potions are bought, each new one picked among all 16 potions of its rolled rarity although the shelf shows some of them. Relic places are not bought: buying a relic obtains it, and which relic the arena shelves depends on what is built.

## On the threshold

`RoomTraces.Edges` are three versions of one fight in which a roll lands exactly on its threshold, because the potion chance or the pity offset was set to the float that puts it there (the script's `edges`): the game tests "below", so on the threshold no potion drops and the card is of the lower rarity; the checks also move the threshold up by a float step or two and see the other outcome. The thresholds of a potion's rarity (0.1 and 0.35, "at or below") and of a relic's (0.5 and 0.83, "below") are fixed numbers, so there the roll has to be found: `RoomTraces.ExactRolls` are four places of one seed's streams, between the 20 and the 75 millionth number, where the next number is exactly the threshold as a float, and the room is rolled after its stream was moved on to there. The upgrade roll is the other way round, "at or below": a check relic sets the chance to a card's own roll (the three rolls of a traced fight), and that card comes upgraded. And a card that cannot be upgraded takes its upgrade roll all the same.

## Prices on a half

A price is a table value times a rolled factor, worked out in 32-bit floats, and a half goes to the even number. Most prices cannot tell that from other arithmetic. `RoomTraces.PriceTies` are eleven shops in which the price of the first character card, the first relic or the first potion can: the product is exactly a whole number and a half as a float, so that rounding a half up, or working the product out in 64-bit numbers, gives another price. Each shop is rolled after the run's two streams were moved on to the place the script found (`shop-ties`), so the price is made by the shop's own code, not by the check. Three examples:

| Thing | Product | The game | Half up | In 64-bit numbers |
| --- | --- | --- | --- | --- |
| Tear Asunder, 150 x 0.98999995 | 148.4999925, as a float 148.5 | 148 | 149 | 148 |
| a Rare relic, 275 x 0.97272724 | 267.4999908, as a float 267.5 | 268 | 268 | 267 |
| Power Potion, 50 x 0.97000003 | 48.5000014, as a float 48.5 | 48 | 49 | 49 |

The base price of a Rare colorless card, 172, is the same rule: 150 x 1.15 is 172.4999964 in exact numbers and 172.5 as a float.

## What the second reader found

Nothing that behaves differently from the game. All 30 rooms, the real run's 13 floors, the six purchases, the 11 prices on a half and every roll on its threshold agreed with the arena the first time they were compared. No line of the code of chunks 4, 8 and 10 was changed.

Read again against the game's code, line by line, without a difference: the order a reward set is made and filled in and what each reward draws; the potion roll and its float steps; the card factory's reward and merchant halves (the two rarity rolls, the next-rarity rule, the pick over the pool without the cards already shown, the upgrade roll that is drawn whatever the card is); the potion factory; the relic rarity roll and the pulls from front and back with the shop's filter; the shelf's build order, the sale and its second price, the three lines of price arithmetic, buying each kind, the refill of each kind, the gold a relic's purchase reports, the removal's own path; the chest's two questions, its two draws and their streams; the rest's heal, its hooks, its rewards and when the option counts as used; and every loop in `Hook.cs` that these rooms send a hook through.

What a reader of these rooms should know besides (none of it changes what the arena does):

- **85 cards, not 87.** The Ironclad pool has 87 cards and a single-player run rolls over 85: Demonic Shield and Tank are multiplayer only and the game's factory filters them out (`CardFactory.FilterForPlayerCount`). The arena does the same; the traces' counts ("place 19 of 20 Common cards", "7 Rare Attack cards") depend on it.
- **"The relic the game would give" is the game's for the ARENA's bag.** The house rule for unbuilt relics makes the arena's bags differ from the game's after the first pass-over: the game takes the unbuilt relic out, the arena leaves it in and takes a built one from further down. From then on `RelicPull.GameRelic` and `TreasureScreen.GameRelic` name what the game would give from the bag as the arena has it, and the same unbuilt relic is logged again at every later pull of its list. What is SHOWN is not touched by this (the arena's next built relic is the same either way). The run's log says from which floor a run is no longer the game's own; a floor log should not present a later `GameRelic` as the real game's relic.
- **The relic blacklist of a refilled place can never take anything out.** The game hands the pull the relics on the shelf to leave out (`MerchantRelicEntry.cs:73-77`), but a shelved relic has left the bag (`RelicFactory.cs:73-78`), so it could not be pulled anyway. The arena passes the same list; leaving it out changes nothing.
- **The range of a price factor is exact.** `SHOP.md` says the width of the range is a float subtraction. It is, and it cannot be told from a subtraction in 64-bit numbers: 1.05 less 0.95 and 1.15 less 0.85 as floats both fit a float exactly.
- **172 does not tell floats from doubles.** A Rare colorless card's 150 x 1.15 is 172.4999964: 172.5 as a float, which goes to the even 172, and 172 in 64-bit arithmetic as well. Only rounding a half up gives 173. The price ties above are what holds the arithmetic to 32 bits.
- **The proceed button of a rewards screen** is switched on once in the game and never asked again (`NRewardsScreen.cs:603-611`); the arena asks the hook at every snapshot. Nothing in v0.107.1 overrides that hook, so nothing can show.

## The mutation pass

122 small faults were put, one at a time, into a copy of `Rewards.cs`, `CardFactory.cs`, `PotionFactory.cs`, `RunOdds.cs`, `Shop.cs`, `Treasure.cs`, `RestSite.cs` and `RunRelics.cs`, and the check classes were run on each (`RoomTracesChecks`; and the builders' `RewardsChecks`, `ShopChecks`, `TreasureChecks`, `RestSiteChecks`, `RealRunChecks`, `RunStreamsChecks`, `RelicFrameworkChecks`).

| | Faults |
| --- | --- |
| caught by somebody | 119 |
| caught by the builders' classes | 105 |
| caught by `RoomTracesChecks` | 65 |
| caught by `RoomTracesChecks` alone | 14 |
| caught by nobody | 3 |

- The 14 only this reader's checks catch: every line of price arithmetic worked out in 64-bit numbers or rounded half up (five faults: the builders' checks held prices to a range and the factor to a formula, which a tie alone can tell from the others); every threshold tested with the wrong one of "below" and "at or below" (a card's Rare roll, the potion drop, a relic's 0.5, the upgrade roll); the upgrade roll skipped for a card that cannot be upgraded; the potion chance moved in 64-bit steps; a refilled potion place that leaves out the potions on the shelf; a rest's shown number rounded instead of cut; and the rewards a fight earned on top filled before the room's own.
- The 3 nobody catches are no faults: they are the same as the game by arithmetic or by construction (the exact range above; the relic blacklist above; and asking "is it stocked" before a second purchase of a removal that was used, which fails either way).
- The 54 only the builders' checks catch are mostly what a room does besides rolling: hooks and their order, purchases, the removal, the chest's two questions, the rest site's options, the first run's chest, the share of gold.
- The two things wave 2 could not test, the log entry for relics a pull passed over and everything of the shop, are both caught now, by both sides.

The script (`mutate17b.py`) and its table (`mutations-final.txt`) are in the build session's scratch folder (`run-mode/chunk17-tools`); they are not part of the repo.

## After a game update

```bash
D=~/Projects/sts2-decompiled-vNEW
T=src/SpireMind.Arena/Sim/Run/Tools/trace_rooms.py
python3 -I $T $D record ~/Projects/sts2-notes/golden-saves/history/1791592972.run   # an old record: still "as the record" on every floor to 14? Then no rule changed
python3 -I $T $D trace 1BUSLN0FY5 A,M,M,V,S                                          # the other record, with Arcane Scroll's card and the event's relic
python3 -I $T $D data QEVNVN1RKF=M,M,E,S,M,R,E,M,B,T YG4K5JKTX0=S,M,E,M,M+23,R,S,E,B,T KPJ8BS3DSJ=M,E,E,M,S,M,M,R,B,T
                                                    # the entries of RoomTraces.Runs: paste them in place of the old ones
python3 -I $T $D data 'YG4K5JKTX0=S!card3/card0/potion1/card3/potion1/potion0'       # the lines of RoomTraces.Refilled, as comments under the shop
python3 -I $T $D shop-ties QEVNVN1RKF built.txt                                      # entries of RoomTraces.PriceTies: cards and relics in seconds
python3 -I $T $D shop-ties KPJ8BS3DSJ built.txt 40000000 3 card,relic                # potions: a few minutes
python3 -I $T $D edges QEVNVN1RKF                                                    # the entries of RoomTraces.Edges
python3 -I $T $D exact QEVNVN1RKF rewards 160000000 0.1 0.35                         # RoomTraces.ExactRolls: the place of each number; then
python3 -I $T $D exact QEVNVN1RKF treasure_room_relics 120000000 0.5 0.83            #   trace <seed> M rewards=<place - 3> chance=2, or trace <seed> T treasure=<place - 1>
python3 -I $T $D find M,M,E,S,M,R,E,M,B,T built.txt                                  # new seeds, when an old one no longer meets only built things
```

`built.txt` lists what the arena holds, one class name a line under the headings `[potions]`, `[colorless]` and `[relics]` (`scripts/arena.sh content` prints all three); `Tools/trace_rooms_built.txt` is that list as of 2026-10-11. It is read by `find` and `shop-ties` only, to choose seeds; no expected value comes from it. A new record of a real run goes through `record` first: every floor it gives "as the record" can be typed into `RoomTraces.Recorded`.

If a card pool, the potion list or a relic pool changed, the three seeds will most likely meet something unbuilt: look for new ones with `find`, then make the data again. `RoomTraces.Recorded` and the upgrade rolls, the potion chance and the rest's number in `RoomTracesChecks` are typed by hand and stay as they are while the rules do.

## Left out

- The fights themselves: a traced fight room is its rewards (`RunRewards.OfferForRoomEnd`). A fight draws from the run's other streams, never from `rewards`, `shops` or `treasure_room_relics`; `RealRunChecks` plays real fights into the same rewards.
- A traced purchase of a relic, a refilled relic or colorless place, the removal: buying a relic obtains it, and which relic or colorless card the arena shelves depends on what is built, so no trace of them would stay true as content grows. They were read again and are held by the builders' checks with check relics (`ShopChecks`), which the mutation pass put to the test.
- The hooks of the three rooms, the chest's two questions, a rest's rewards, the first run's chest: read again; nothing there rolls a number a trace could add to. `RewardsChecks`, `TreasureChecks` and `RestSiteChecks` hold them.
- Events, Neow, the map: other chunks' second readers.
