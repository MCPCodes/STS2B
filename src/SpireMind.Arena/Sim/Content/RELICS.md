# Relics as content

Chunk 12 of the run-mode plan (`sts2-notes/run-mode-v0.107.1/plan.md`, 5.2 and 5.3): the first relics of the game. What a relic is written against (the hooks, who hears them, the commands, the pulls from the bags) is `Sim/Run/RELICS.md`; how content files work is `Sim/README.md`. Everything here is the game's (v0.107.1, Ironclad, ascension 0, single player) unless a line says "house rule".

## Files

| File | Holds |
| --- | --- |
| `Relics1.cs` | 25 relics, the power Vigor, the potion Potion-Shaped Rock |
| `Relics1Checks.cs` | their checks, and the checks of the list below |
| `../Run/ReachableRelics.cs` | the 157 relics an act 1 Ironclad can get, each with its source and the plan chunk that builds it; the module that leaves out by name those that have no class yet |
| `../Completeness.cs` | one line for the relics and one for the token potion: registered, left out, missing |

## The 25

| Rarity | Relic | What it does | Hook |
| --- | --- | --- | --- |
| Common | Anchor | 10 Block when the fight starts | `BeforeCombatStart` |
| Common | Vajra | 1 Strength in every fight's room | `AfterRoomEntered` |
| Common | Bronze Scales | 3 Thorns in every fight's room | `AfterRoomEntered` |
| Common | Oddly Smooth Stone | 1 Dexterity in every fight's room | `AfterRoomEntered` |
| Common | Bag of Preparation | 2 more cards in the draw of turn 1 | `ModifyHandDraw` |
| Common | Lantern | 1 energy on turn 1 | `AfterSideTurnStart` |
| Common | Strawberry | 7 max HP at pickup | `AfterObtained` |
| Uncommon | Akabeko | 8 Vigor on turn 1 | `AfterSideTurnStart` |
| Uncommon | Candelabra | 2 energy on turn 2 | `AfterSideTurnStart` |
| Uncommon | Mercury Hourglass | 3 damage to every enemy at each turn start, as one damage instance | `AfterPlayerTurnStart` |
| Uncommon | Horn Cleat | 14 Block on turn 2 | `AfterBlockCleared` |
| Uncommon | Petrified Toad | a Potion-Shaped Rock when the fight starts, if a slot is free | `BeforeCombatStartLate` |
| Uncommon | Pear | 10 max HP at pickup | `AfterObtained` |
| Rare | Bellows | the hand of turn 1 is upgraded for the fight | `AfterPlayerTurnStart` |
| Rare | Captain's Wheel | 18 Block on turn 3 | `AfterBlockCleared` |
| Rare | Chandelier | 3 energy on turn 3 | `AfterSideTurnStart` |
| Rare | Mango | 14 max HP at pickup | `AfterObtained` |
| Rare | Game Piece | a Power card played draws 1 card | `AfterCardPlayed` |
| Rare | Ice Cream | from turn 2 on energy is added to, not set | `ShouldResetEnergy` |
| Rare | Tungsten Rod | every HP loss of the player is 1 less, in a fight and outside | `ModifyHpLostAfterOsty` |
| Shop | Bread | turn 1 loses 2 energy; from turn 2 on the maximum is 4 | `ModifyMaxEnergy`, `AfterSideTurnStart` |
| Shop | Sling of Courage | 2 Strength in an Elite room | `AfterRoomEntered` |
| Shop | Brimstone | each player turn: 2 Strength for the player, 1 for every living enemy | `AfterSideTurnStart` |
| Shop | Lee's Waffle | 7 max HP at pickup and a heal to full | `AfterObtained` |
| Shop | The Abacus | 6 Block whenever the discard pile is shuffled into the draw pile | `AfterShuffle` |

All of them may be sold in shops and may come up on any floor. Brimstone is the Ironclad's own (a chest, which pulls from the shared bag, never holds it); the other 24 are shared.

What to know when reading them:

- **None keeps anything between rooms.** "Turn 2" is the fight's own turn number: the game's `PlayerCombatState.TurnNumber` is `Fight.Turn` (both start at 1 and go up when the side switches back to the player, before any hook of the new turn).
- **Block and damage from a relic are unpowered** (`Props.NonCardUnpowered`): Dexterity, Frail, Strength, Weak and Vulnerable do not change them; Block absorbs the damage.
- **The moments of a turn start** (`CombatManager.StartTurn`): Block falls, "after block cleared" (Horn Cleat, Captain's Wheel), energy (Ice Cream, Bread's maximum), the hand draw (Bag of Preparation), "after player turn start" (Mercury Hourglass, Bellows), "after side turn start" (Lantern, Candelabra, Chandelier, Akabeko, Bread's loss, Brimstone). Relics of one hook act in pickup order.
- **Vigor** (`VigorPower`, in `Relics1.cs`) is the game's power with its quirk: the first powered attack command of the owner claims it and takes off, when it is over, the amount it started with; the claim is never forgotten, so Vigor gained during that attack stays and is given only to hits of the same card (or of no card), without being used up. With Akabeko alone that cannot happen. A card whose effect is two attack commands gets the bonus on the first only, while its text shows it on every number, as in the game.
- **Potion-Shaped Rock** is the game's one Token-rarity potion (15 unpowered damage to an enemy, in a fight only). It is registered like every potion but is in no pool: rewards, shops and random generation read `Scenarios.PotionPool` and never make it. Petrified Toad hands it out through `Fight.GainPotion`, which sends no "potion procured" hook yet; nothing reachable but Belt Buckle listens to it, and its chunk adds the call there.
- **Tungsten Rod acts outside fights too** (an event's HP loss), since the game's class tests nothing but whose HP it is. It is in the first HP-loss pass after Block, so a loss it takes to 0 costs no Buffer.
- **Brimstone and the forecast experiment** (`Sim/Forecast.cs`): the forecast marks every coming hit with "~" while the player holds it, as the mod's does (the Strength is not added up).

## The list of reachable relics, and how a later chunk adds a relic

`ReachableRelics.All` is the 157: Burning Blood, the 122 of the player's bag, Neow's 27 in single player, the 7 that act 1 events hand out by name. Each entry carries the chunk of the plan that builds it (`chunks.json`); Kaleidoscope carries none, because it needs the other four characters' card pools.

`Completeness` holds the registry to the list: a relic of it must be registered or left out by name with a reason, and no other relic may be registered. The leaving out is automatic. `ReachableRelicsLeftOut` leaves out every relic of the list for which the assembly has no top-level `Relic` class of that name, with the reason "not built yet: chunk N (name) of the run-mode plan builds it". A pull from a bag passes such a relic over; a strict run that lands on one ends as unrepresentable and says that reason.

So to add a relic:

1. Write `public sealed class StoneCracker : Relic` (top-level, the game's class name) in your content file and register it with `r.Relic<StoneCracker>()`.
2. Nothing else. The class's existence takes the relic off the left-out list; forgetting to register it shows as "neither registered nor left out" and fails the self-check. A check's own stand-in relic is nested in its checks class and does not count.
3. A relic that is not on the list (another act's, another character's) is refused as "registered but on none of the game's lists": add it to the table in `ReachableRelics.cs` with its source first.

## Left out by name

- The relics of the list without a class (115 today: Neow's first 14 are in `NEOW-CORE.md`, the two swords of the events in `EVENTS.md`): printed by `arena content` and at the top of the self-check, each with its chunk.
- Kaleidoscope, for good (see above).
- A relic's tradability (the game's `IsTradable`, which is false for relics with a pickup effect: Strawberry, Pear, Mango, Lee's Waffle): only an act 2 event asks.
- Relic counters shown on the relic (none of these 25 has one; the mod's snapshot has no field for one, so Sword of Stone, the first built relic that counts, says its count in its text: `EVENTS.md`).

## Checks

`Relics1Checks`: every number by hand from the game's classes; each relic's hook held to its moment by a probe relic that writes down what it sees between the hooks; Vigor's claim; the pickup effects outside a fight, through a rewards row and in `get_deck`; relics over a run of fights with a replay; the list against the game's bag (`RelicBag.PlayerPool`), Neow's options and the plan's chunk counts; pulls with the real registry against the bags the game saved for seed 1BUSLN0FY5 (its event's Stone Cracker and its first shop's Paper Phrog, Pendulum and Ringing Triangle are not among the 25, so the pulls pass them over: Mercury Hourglass; Pear, Anchor, Sling of Courage) and against the first shop of the real run on seed 75F277YN2G, which showed Lantern; an Elite's relic row on the seed's own streams; the run's log of what a relic row did not take from the game, also when its rarity had to be rolled again; the forecast's mark for Brimstone. Whole fights of the game's encounters with every relic, played twice by the scripted players. Eighty-one deliberate faults in the code (a number, a turn, a hook, a flag of every relic; Vigor's claim; the list; the edits to shared files) were each caught by a check.
