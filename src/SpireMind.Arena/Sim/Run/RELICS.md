# Run mode: relics, the run's hooks, and the commands that change the player

Chunk 5 of the run-mode plan (`sts2-notes/run-mode-v0.107.1/plan.md`, sections 1.5, 1.6, gaps 3 to 5 and 7). The frame of a run is in `README.md`, the streams and bags in `STREAMS.md`. Everything here is the game's (v0.107.1, single player) unless a line says "house rule"; the game's files are named where a rule comes from.

No relic of the game is built here. This is what relic content is written against.

## Files

| File | Holds |
| --- | --- |
| `../Engine/Listener.cs` (last region) | the run's hooks: 18 virtual methods handed a `RunContext`. The hooks of the rewards and of the deck are on two opt-in interfaces of those parts, `IRewardHooks` (`REWARDS.md`) and `IDeckListener` (`DECK.md`), handed a `RunContext` too |
| `../Engine/Models.cs` | `Relic.AfterObtained`, `Relic.AfterRemoved` |
| `../Engine/RunContext.cs` | `RunContext`, `RoomKind`, `IListensInDeck`, `IHearsRelicObtained` |
| `../Engine/Fight.Hooks.cs` | who hears a hook in a fight, in which order; `DeckListening` |
| `../Engine/Fight.cs` | "after room entered" in a fight's set-up |
| `../Engine/Fight.Relics.cs` | gold and relics inside a fight; the player outside a fight (`Fight.OutOfCombat`) |
| `RunHooks.cs` | one named call site per hook of the run; the list of the 27 |
| `Run.Commands.cs` | the commands outside a fight: heal, max HP, damage, gold, relics, room entered |
| `RunRelics.cs` | pulling relics from the bags; what a pull knows about relics that are not built |
| `RelicFrameworkChecks.cs` | the checks |

## Writing a relic

```csharp
public sealed class Strawberry : Relic            // Strawberry.cs
{
    public override string Name => "Strawberry";
    public override string Describe() => "At pickup: gain 7 Max HP.";
    public override void AfterObtained(RunContext c) => c.GainMaxHp(7);
}

public sealed class Vajra : Relic                 // Vajra.cs
{
    ...
    public override void AfterRoomEntered(RunContext c, RoomKind room)
    {
        if (c.Fight is { } f && room.IsCombat()) f.ApplyPower<StrengthPower>(f.Player, 1, f.Player);
    }
}
```

- A relic is one object for the run. Its fields are its memory: they go into every fight and come back (the fight works on a copy, so never keep a reference to a relic across a fight). A list field needs `OnCloned`, as for every listener.
- **Fight hooks** take the fight (`AfterCombatVictory(Fight f)`), as before. **Run hooks** take a `RunContext`: `c.Run` outside a fight, `c.Fight` inside one, and commands that work in both places (`c.Heal`, `c.GainMaxHp`, `c.LoseMaxHp`, `c.Damage`, `c.GainGold`, `c.LoseGold`, `c.ObtainRelic`, `c.ReplaceRelic`, `c.RemoveRelic`), so one hook body serves both. A pickup that needs the run itself (a card choice with `run.Ask`, a roll on a run stream, a pull, a screen) takes it with `c.NeedRun("what for")`; inside a fight that is refused by name.
- **A fight hook can be heard with no fight on.** When a command of the run hurts or heals the player (an event's HP loss, a max HP loss), the hooks of the damage chain are sent as in the game, and the `Fight` they are handed is the player outside a fight: `f.IsCombat` is false, `f.InProgress` is false, there are no enemies and no piles. A relic whose game code tests `CombatManager.IsInProgress` tests `f.InProgress`; one that tests "the current room is a combat room" tests `f.IsCombat`. A relic that tests neither acts outside fights too, as in the game: Tungsten Rod takes 1 off an event's HP loss, Lizard Tail and Fairy in a Bottle prevent a death in an event room.
- Replace a relic with `f.ReplaceRelic(this, new SwordOfJade())` in a fight hook, `c.ReplaceRelic(...)` in a run hook: the new one stands where the old one stood.

## Who hears a hook, and in which order

The game sends a hook in one of three ways (`Hooks/Hook.cs`, `RunState.IterateHookListeners`, `CombatState.IterateHookListeners`):

| Sent to | Order | Hooks |
| --- | --- | --- |
| the run's listeners alone, fight or no fight | deck cards in deck order, relics in pickup order, potions in slot order | room entered, gold gained, card added to the deck, rewards, card offers, shop, rest site, map, treasure |
| the run's deck cards, then the fight's listeners | deck cards; then the player's powers, relics, potions, the cards in hand, draw, discard, exhaust and play pile; then each enemy's powers and the monster | start and end of a fight, victory, damage and HP-loss modifiers, HP changed, damage received, death, card moves, potion used, gold modifiers, potion gained or thrown away |
| the fight's listeners alone | the same without the deck cards | everything else of a fight (a card played, a turn starting) |

Outside a fight the second kind goes to the run's listeners. The list is taken when a dispatch starts; each entry is tested again just before its call (a relic another hook removed is not called; nobody is once the player is dead).

- In a fight: `Fight.Each` and its siblings (second and third kind), `Fight.EachOfRun` (first kind). Outside: `run.Listeners()` (`Run.EachListener`), the ONE definition of the order outside a fight: the rewards' call sites (`RewardHooks`), the deck's (`DeckHooks`) and the map's all walk it. Content never calls these: it calls a method of `RunHooks`, which takes a `RunContext` and picks.
- **Deck cards listen only by opting in**: put the marker `IListensInDeck` on the card's `CardBehaviour` class (the interfaces `IDeckListener` and `IRewardHooks` carry it, so implementing one of them opts a card's hook class in). The deck card's own hook object is then asked, first, and its copy in a pile as before; the class tells them apart by `Card.Pile == PileType.Deck`, as the game's Guilty does. The arena asks a listening deck card on every dispatch of a fight, also the third kind, so such a class may override only hooks of the first two kinds: `DeckListening.HeardByDeckCards` lists the fight hooks, every run hook is allowed, and `DeckListening.Problems()` names a registered card that breaks the rule (the self-check fails on it). A card that needs both a deck-card hook and a fight-only hook cannot be written yet.

## "After room entered"

- **A fight** sends it itself, in `Fight.StartCombat`: after the monsters stand and the draw pile is shuffled, before the monsters get their starting powers and first intents, before the fight is in progress and before `BeforeCombatStart` (`CombatRoom.StartCombat`: between `SetUpCombat` and `AfterCombatRoomLoaded`). Powers can be applied there. A single fight sends it too.
- **Every other room** calls `run.AfterRoomEntered(RoomKind.Shop)` at the game's moment: a shop after its stock is made, a treasure room before its relic is rolled, a rest site after its options are made, an event after its start rolls and before its first page.
- `RoomKind` has the game's `RoomType` names and numbers. `run.Room` is the kind of room the run is in (for the game over screen).

## Commands outside a fight (`Run.Commands.cs`)

`RunState`'s setters stay bare. A room, an event or a relic changes the player through these, which send the game's hooks:

| Command | The game's | Hooks |
| --- | --- | --- |
| `run.Heal(n)` | `CreatureCmd.Heal` | none outside a fight ("HP changed" is sent for a heal only in a combat) |
| `run.GainMaxHp(n)` | `CreatureCmd.GainMaxHp` | none; heals by what the maximum rose |
| `run.LoseMaxHp(n)` | `CreatureCmd.LoseMaxHp` | HP above the new maximum is lost as damage (below); the maximum never goes under 1 |
| `run.Damage(n, props)` | `CreatureCmd.Damage`, no combat state | damage modifiers, before damage received, HP-loss modifiers, HP changed, after damage received; death: before death, should die, after death, after preventing death. Returns the HP lost |
| `run.GainGold(n)` | `PlayerCmd.GainGold` | modify gold gained; after modifying (those that changed the whole number, with the final amount); nothing at 0 or less; else the whole-number part is paid and "gold gained" is sent |
| `run.LoseGold(n)` | `PlayerCmd.LoseGold` | none; never below 0 |
| `run.ObtainRelic(relic, index)` | `RelicCmd.Obtain` | into the list; every open screen that is an `IHearsRelicObtained` (a pending card reward); out of both bags; `AfterObtained` |
| `run.RemoveRelic`, `run.ReplaceRelic` | `RelicCmd.Remove`, `Replace` | `AfterRemoved`; a replacement is obtained at the original's index |
| `run.OutOfCombat(f => ...)` | | runs a fight command on the player outside a fight (a potion's effect) |

- While a fight is on these throw: the player is the fight's. A fight has `f.GainGold`, `f.LoseGold`, `f.ObtainRelic`, `f.RemoveRelic`, `f.ReplaceRelic` beside the heal, max HP and damage commands it always had.
- **A player can die outside a fight** (`Damage`, `LoseMaxHp`). The run is then over (`RunResult.Died`; the game over screen with the room's kind, then the end snapshot) and every command does nothing from then on. Whoever called the command that killed tests `run.IsOver` and stops.
- How the damage commands work outside a fight: `Run.OutOfCombat` builds a `Fight` that is no combat around the run's own relic objects and deck cards, runs the engine's command on it, and takes HP, max HP, gold and the belt back. Nothing may draw a random number from that fight's streams (it throws).

## Pulling relics (`RunRelics.cs`)

```csharp
var rng = world.Streams.Draw(RunStream.Rewards, Purpose.RelicRarity(row));
var pull = RunRelics.RollAndPullFromFront(run.State, rng, strict);   // elite, event, capsule
var pull = RunRelics.PullFromBack(run.State, rarity, rng, strict);   // a shop's slot (rng: the stream its rarity was rolled on; null for the Shop slot)
var pull = RunRelics.PullFromShared(run.State, rarity, rng, strict); // a chest (rng: treasure_room_relics)
if (!pull.Found) throw new RunUnrepresentableException(pull.Reason!);   // the run ends itself as unrepresentable (Run.Perform)
run.ObtainRelic(pull.Relic!);
```

- `RollRarity`: one number; Common below 0.5, Uncommon below 0.83, else Rare (`RelicFactory.RollRarity`).
- Front of the player's bag for elites, events and capsules; back for shops, never one of `NeverInShops` (five relics); front of the shared bag for a chest. An empty rarity gives the next one's: Shop, Common, Uncommon, Rare (`RelicBag`). A relic pulled from the player's bag leaves the shared bag at once; a chest's relic leaves the player's bag when it is obtained.
- `IsAllowed`: the seventeen relics of `OnlyBeforeTheLastChest` are dropped from the bag from total floor 41 on, Lasting Candy also for a profile with no finished run. In act 1 everything else is allowed.
- A relic obtained inside a fight cannot reach the bags; `SyncBags`, which every pull calls first, takes every held relic out of both.
- **House rule, relics that are not built** (the plan's "draw again"). The pull first finds the relic the game would give (`pull.GameRelic`). Built: it is taken, and the pull is the game's own (`pull.IsTheGames`). Not built, `strict`: nothing is pulled; end the run as unrepresentable. Not built, otherwise: the next built relic of the same list is taken and the ones before it are passed over (`pull.PassedOver`); they stay in the bag, where they will be passed over again. When the list has no built relic, a rolled rarity is rolled again on the same stream (`pull.Rerolls`), as long as some rolled rarity still has one; a rarity that was not rolled gives nothing. `isBuilt` is the registry unless a check passes its own.

## The 27 hooks outside a fight

`RunHooks.Hooks` lists them with the number of reachable relics that use each. Twenty-four are declared and sent. Eighteen are on `Listener` (and `Relic` for the pickup):

| Hook | Sent by |
| --- | --- |
| `AfterObtained` (and `AfterRemoved`) | `Run.ObtainRelic`, `Fight.ObtainRelic` |
| `AfterRoomEntered` | `Run.AfterRoomEntered`, `Fight.StartCombat` |
| `ModifyGoldGained`, `AfterModifyingGoldGained`, `AfterGoldGained` | `RunHooks.GainGold` (`Run.GainGold`, `Fight.GainGold`) |
| `TryModifyCardBeingAddedToDeck` (and `...Late`) | `RunHooks.ModifyCardBeingAddedToDeck`: the deck edits call it |
| `AfterPotionProcured`, `AfterPotionDiscarded` | `RunHooks.AfterPotionProcured`, `AfterPotionDiscarded`: whoever puts a potion in the belt or throws one away |
| `ShouldForcePotionReward` | `RunHooks.ShouldForcePotionReward`: the rewards, before the potion roll |
| `ModifyUnknownMapPointRoomTypes`, `ShouldAllowFreeTravel` | `RunHooks`: the map |
| `ShouldGenerateTreasure` | `RunHooks`: the treasure room |
| `ShouldDisableRemainingRestSiteOptions`, `ModifyRestSiteHealAmount`, `AfterRestSiteHeal`, `ModifyExtraRestSiteHealText` | `RunHooks`: the rest site |
| `AfterModifyingRewards`, `AfterModifyingCardRewardOptions` | `RunHooks`: the rewards, with the listeners that changed something |

Six more have rewards and card offers as their arguments and are declared on the rewards' interface `IRewardHooks`, sent by `RewardHooks` (`REWARDS.md`): `TryModifyRewards` (`RewardHooks.ModifyRewards`), `TryModifyRestSiteHealRewards` (`RewardHooks.ModifyRestSiteHealRewards`), `TryModifyCardRewardOptions` and `TryModifyCardRewardOptionsLate` (`RewardHooks.ModifyCardRewardOptions`, and `CardReward.OnRelicObtained` for a relic obtained while a card reward waits), `ModifyCardRewardCreationOptions` (`RewardHooks.ModifyCardRewardCreationOptions`), `ModifyMerchantCardCreationResults` (`RewardHooks.ModifyMerchantCardCreationResults`).

Three wait for a type a later part defines: `TryModifyRestSiteOptions` (a rest site option), `ModifyMerchantPrice` and `ShouldRefillMerchantEntry` (a shop entry). The part that defines the type declares the hook on `Listener` with a `RunContext` first and sends it with `RunHooks.Modifiers`, `Fold`, `Any`, `All` or `Each`; each entry of the list says how the game does it. Then the entry gets its `SentBy` and loses its `Waits`. A check holds the list to what is declared: a waiting hook that turns up on `Listener` or `IRewardHooks` fails it, every call site the list names must exist, and no hook of the run may be declared both on `Listener` and on an interface of a part.

Only the pickup, room entered, the gold hooks and the fight's end and victory are exercised by relics in the checks; the other declared hooks are checked for who is asked, in which order and when the asking stops. The relic that first uses one brings its own checks.

## Left out by name

- **A relic's pickup effect that needs the run, inside a fight** (`RunContext.NeedRun` throws). Only a replacement in a victory hook obtains a relic in a fight in act 1 (Sword of Jade, which has no pickup effect).
- **Whether a fight's room is the map point's own or one an event started** (the game's `CurrentRoomCount`, `BaseRoom`): a fight does not know. Winged Boots and Maw Bank read it; whoever builds them adds it to the fight's request.
- **Potion gained or thrown away inside a fight**: `Fight.GainPotion` and `Fight.RemovePotion` send no hook yet. Belt Buckle is the one reachable relic that listens; its chunk adds the two calls (`RunHooks.AfterPotionProcured(f.Context, potion)`).
- **"Before room entered"**: no relic, card, potion or enchantment of the game overrides it.
- **A card that listens in the deck and also overrides a fight-only hook** (see above).
- **A random number drawn from a fight's streams by a hook that is heard outside a fight** (it throws).
- **Stackable relics** (only the fallback relic Circlet, which the game hands out when a bag is empty: a pull then finds nothing), wax and melted relics (an act 2 Ancient's), the floor a relic was obtained on (the game keeps it for its run history).
- **The run history entries** the game writes in these commands (gold gained, HP healed, relics picked).
- **The completeness list for relics**: `RunRelics.IsBuilt` is "registered". The chunk that registers the first relics adds the list of the 157 reachable ones to `Completeness`, so that each is registered or left out with a reason.

## Checks

`RelicFrameworkChecks`: a run of eight fights with a pickup relic (Strawberry), a room-entered relic (Vajra) and a counting relic that replaces itself (Sword of Stone, Sword of Jade), every number by hand, with a replay and a copy; the order outside and inside a fight with listening deck cards, relics, potions and pile cards; a relic removed in the middle of a dispatch; what a relic sees when a fight's room is entered; the pickup order with open reward screens and the bags; gold with Bowler Hat and Dragon Fruit, in and out of a fight; heal, max HP and damage outside a fight with Tungsten Rod, Lizard Tail and Fairy in a Bottle; a death in an event room; the pulls against the real run on seed 1BUSLN0FY5 (Arcane Scroll's card from one `rewards` number, the event's Stone Cracker from the 26th, both bags as the game saved them afterwards, the three relics of the first shop); relics that are not built, strict and not; the bag rules; the hook list; the guard on listening deck cards. Thirty-five deliberate faults in the code were each caught by a check.
