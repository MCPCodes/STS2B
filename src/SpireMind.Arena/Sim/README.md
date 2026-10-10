# The arena simulator: how it is built and how to add content

The simulator plays one Slay the Spire 2 fight (public build v0.107.1, Ironclad, ascension 0, single player, act 1 "Overgrowth") by the game's own rules. The engine is in `Sim/Engine/`. Everything a fight contains (cards, powers, monsters, encounters, potions) is content, one file per chunk in `Sim/Content/`. This page is for whoever changes the engine or writes a content file. What the arena is for, and how to read its results, is in `docs/arena.md`.

What it holds now: all 85 single-player cards of the Ironclad's pool (the two multiplayer-only cards are left out), the 12 status cards, Giant Rock, all 22 Overgrowth encounters with their 29 monsters, 46 of the 48 potions an Ironclad can find, 58 powers and Burning Blood. `scripts/arena.sh content` prints the table and every name; section 13 lists what is left out and the known gaps.

The rule that matters most: **never approximate.** If something cannot be reproduced exactly, leave it out with `r.Exclude(...)` and a reason. A wrong card is worse than a missing card.

Sources, in this order: the verified spec files in `/Users/mark/Projects/sts2-notes/arena-spec-v0.107.1/`, then the decompiled game in `/Users/mark/Projects/sts2-decompiled-v0.107.1/` (one folder per namespace). Where code and spec disagree the code wins; report the difference. The decompiled code is copyrighted: read it, never copy it. Card, power and potion texts are your own plain wording with the live numbers; the game's own description text must not be imitated from memory.

## 1. Files and names

| You write | File | Checks in |
| --- | --- | --- |
| Ironclad pool cards, part 1 to 3 | `Sim/Content/Cards1.cs`, `Cards2.cs`, `Cards3.cs` | `Cards1Checks.cs` ... |
| Overgrowth monsters and encounters | `Sim/Content/MonstersA.cs`, `MonstersB.cs`, `MonstersC.cs` | `MonstersAChecks.cs` ... |
| Elites, bosses | `Sim/Content/Elites.cs`, `Bosses.cs` | `ElitesChecks.cs`, `BossesChecks.cs` |
| Potions | `Sim/Content/Potions.cs` | `PotionsChecks.cs` |
| Scenario generator | `Sim/Scenario.cs` | `Sim/ScenarioChecks.cs` |
| Scripted players | `Policies/GreedyPolicy.cs`, `Policies/LookaheadPolicy.cs` | `Policies/PolicyChecks.cs` |
| The engine's rules | `Sim/Engine/*.cs` | `Sim/Content/Core*Checks.cs`, `Sim/Engine/EngineReviewChecks.cs`, `Sim/Engine/IntegrationChecks.cs` |
| What the game has against what the registry has | `Sim/Completeness.cs` | (the self-check fails on a hole) |
| Promise against outcome | `Sim/Honesty.cs` | (`scripts/arena.sh honesty`) |

- Namespace `SpireMind.Arena`, file-scoped; nullable is enabled; no warnings.
- One public class per content file implements `IContentModule` (name it after the file: `Cards1`). It is found by reflection; nothing else has to be edited. The same for checks: one public class implementing `IArenaChecks` (`Cards1Checks`).
- Everything is looked up by **the game's class name**: card `"StrikeIronclad"`, power class `StrengthPower`, monster class `Nibbit`, encounter class `NibbitsWeak`, potion `"FirePotion"`.
- All files share one namespace, so type names must not collide. Power, monster and encounter classes carry the game's class names and are top-level. **Every other type you need (card behaviours, helpers, check-only cards and powers) goes inside your module or checks class as a private nested type.**
- A name registered twice, or registered by one file and excluded by another, fails the self-check with both file names.
- The self-check also fails when a card of the Ironclad's pool, a status card, an Overgrowth encounter or a potion of the Ironclad's pool is neither registered nor left out with a reason, when something is registered that is on none of those lists, and when random card generation would not pick from the game's 78 cards (33 Attacks, 27 Skills, 18 Powers): it picks by index, so one card too few shifts every pick. The lists are in `Sim/Completeness.cs`.
- If the engine cannot express something exactly, extend the engine from the game's code, with a check; where that is not possible, exclude the content with the reason.

The core (in `Sim/Content/Core*.cs`): the powers `StrengthPower`, `DexterityPower`, `VulnerablePower`, `WeakPower`, `FrailPower`, `ArtifactPower`, `MinionPower`, `SlipperyPower`, `ShrinkPower`, `PlatingPower`, the abstract `TemporaryStrengthPower`; the relic `BurningBlood`; the cards `StrikeIronclad`, `DefendIronclad`, `Bash`, `Armaments`; the 12 status cards (`Beckon`, `Burn`, `Dazed`, `Debris`, `FranticEscape`, `Infection`, `Wither`, `Slimed`, `Soot`, `Toxic`, `Void`, `Wound`); the monster `Nibbit` with the encounters `NibbitsWeak` and `NibbitsNormal`. Read them as worked examples. The powers only potions use (`RegenPower`, `ThornsPower`, `ClarityPower`, `DuplicationPower`, `RadiancePower`, `RetainHandPower`, `DemisePower`, `GigantificationPower`, `BufferPower`, `RitualPower`, `BlockNextTurnPower`) are in `Potions.cs`.

A power defined in another content file is used by class name, never by type: `f.ApplyPower("NoDrawPower", target, 1, applier)`, `creature.GetPower("CrueltyPower")`, `creature.PowerAmount("SandpitPower")`. (Vulnerable already asks the dealer for `"CrueltyPower"`: define that one as a plain counter power.)

## 2. How the engine works, in one page

`Fight` holds the state and the commands, and runs the turn loop. Content never sees a clock, a UI or a thread.

- **Hooks.** Almost every rule is a hook that is offered to every listener in a fixed order: the player's powers in the order they were first applied, then the player's relics, potions in the belt, and the hook objects of cards in the hand, draw, discard, exhaust and play piles; then for each enemy in list order its powers and the monster itself. A listener filters for itself ("is my owner the dealer?"). Powers, monsters, relics and card behaviours are all `Listener`s and override the same methods (section 6).
- **Ending.** When the last primary enemy dies, or the player dies, the fight is "ending" but not over: the card finishes, most commands do nothing from then on, and the result is processed at the next safe point. You do not check for this; the commands do. Damage and HP loss are *not* switched off, so a card that hurts its owner after its killing hit still does.
- **Deterministic and copyable.** A fight is fully decided by its seed and the actions. It is copied whole before every action and by search players. Therefore:
  - **Keep every bit of state in the fight**: fields of your power, monster or card behaviour, or numbers on the card (`card.Set("Damage", ...)`). Never in `static` fields, never in variables captured by a lambda, never in a field of the module class.
  - Fields holding plain values are copied automatically. Fields of type `Creature`, `CardInstance` or `Power` are pointed at the copy's objects automatically. **Any other mutable reference field (a `List`, a `HashSet`) must be copied in `OnCloned`**: `public override void OnCloned(CloneMap map) => _cards = _cards.Select(c => map.Get(c)!).ToList();`. The self-check fails a registered class that has such a field and no `OnCloned`.
  - Never keep a `Fight` in a field. Every hook and command gets it as `f`.
  - Use only the fight's random streams (`f.Rng....`), and the stream the spec names for that decision.
  - Do not catch exceptions around engine calls (a card choice is delivered by an exception, see section 5).
- **Numbers.** HP, Block and power amounts are whole numbers. Amounts in flight are `decimal` and are never rounded by you: pass `7.5m` on, the engine truncates where a value is written into Block or HP.

## 3. Writing a card

A card is a `CardDef` registered in your module. Bash, complete:

```csharp
public sealed class Cards9 : IContentModule
{
    public void Register(Registry r)
    {
        r.Card(new CardDef("Bash", "Bash", CardType.Attack, CardRarity.Basic, cost: 2, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 8, ["Vulnerable"] = 2 },
            Upgrade = c => { c.Add("Damage", 2); c.Add("Vulnerable", 1); },
            Text = t => $"Deal {t.Damage()} damage. Apply {t.N("Vulnerable")} Vulnerable.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Execute();
                c.Apply<VulnerablePower>(c.Target, c.V("Vulnerable"));
            },
        });
    }
}
```

**Constructor**: class name, the title the player sees, type, rarity, cost, target type. Cost: a number; `CardDef.CostX` for an X card; `CardDef.NoCost` (-1) for a card without a cost (the unplayable statuses). Target types that matter: `AnyEnemy` (the player picks one: `play:3>e1`), `AllEnemies`, `RandomEnemy`, `Self`, `None` (all played with `play:3`). `AnyAlly` cards cannot be played in single player.

**Properties** (all optional):

| Property | Meaning |
| --- | --- |
| `Vars` | The card's numbers by the game's variable names. A plain number's kind follows the name: `"Damage"` previews as damage, `"Block"` as block, anything else is plain. Say otherwise with `Var.Damage(2, Props.CardUnpowered)`, `Var.Block(...)`, `Var.Plain(...)`. |
| `Keywords` | `CardKeywords.Exhaust`, `Ethereal`, `Innate`, `Unplayable`, `Retain` (flags, combine with `\|`). |
| `Tags` | `CardTags.Strike`, `CardTags.Defend`. |
| `Upgrade` | Applies the upgrade to an instance: `c.Add("Damage", 3)`, `c.UpgradeCostBy(-1)` (for "3 -> 2"), `c.Keywords \|= CardKeywords.Innate`, `c.Keywords &= ~CardKeywords.Exhaust`. Behaviour that differs when upgraded branches on `c.Upgraded` inside `Play` and `Text`. |
| `MaxUpgradeLevel` | 1 by default; 0 for cards that cannot be upgraded. |
| `Play` | One run of the effect, given a `PlayContext` (below). A card that does nothing when played leaves it out. |
| `Text` | The rules text in your own words, given a `TextContext` (below). Do not write keywords: the engine adds "Unplayable.", "Innate.", "Retain.", "Ethereal." before and "Exhaust." after, in the game's order. |
| `Behaviour` | `() => new MyBehaviour()`: a hook object per card instance, for cards that react while they sit in a pile (section 3.3). |
| `TurnEndInHand` | `(f, card) => ...` for a card that does something when it is in hand at end of turn. The engine moves it to the Play pile, runs this, then discards it (exhausts it if Ethereal). |
| `DamageMultiplier` | `(f, card, target) => multiplier` for a card whose damage is calculated: per hit it deals `CalculationBase + ExtraDamage x multiplier`, both declared in `Vars` under those names. Body Slam: `(f, _, _) => f.Player.Block`; Bully: `(_, _, t) => t?.PowerAmount<VulnerablePower>() ?? 0`. Play it with `c.CalculatedAttack()`, show it with `t.CalculatedDamage()`. Always declare it this way: Thrash reads other cards' damage through it (`f.DamageNumberOf(card)`). |
| `IsPlayable` | `(f, card) => bool`: the card's own extra play condition. |
| `CanBeGeneratedInCombat` | `false` for Feed, Not Yet and the like. |
| `Title` | Only when the title is not the name plus "+". |

### 3.1 `PlayContext` (the `c` in `Play`)

| Member | Does |
| --- | --- |
| `c.F` | The fight: every command of section 4. |
| `c.Card`, `c.Play` | The card instance and this run (`Play.PlayIndex`, `Play.IsAutoPlay`, ...). |
| `c.Owner` | The player's creature. |
| `c.Target` | The chosen enemy of an `AnyEnemy` card (rolled for an auto-played one); null for other cards. May have died earlier in the same effect. |
| `c.Upgraded` | Whether this instance is upgraded. |
| `c.V("Damage")` | One of the card's numbers, as a decimal. |
| `c.X` | X of an X-cost card: the energy spent (auto-played: the energy at hand, not spent). |
| `c.Attack(perHit)` | An attack from this card aimed by its target type: the chosen enemy, all enemies, or a random enemy per hit. Returns an `AttackCommand`: chain `.Hits(n)` and finish with `.Execute()`. Read `.Results` and `.KilledAny` afterwards. |
| `c.CalculatedAttack()` | The same for a card with calculated damage (Body Slam, Ashen Strike, Perfected Strike, Bully): see `DamageMultiplier` below. |
| `c.Attack(target => amount)` | An attack whose damage is worked out again before every hit by a function of your own. The argument is the hit's single target, or null when a hit lands on several enemies at once. |
| `c.GainBlock(amount)` | Block from this card (Dexterity, Frail apply). Each call is its own Block gain. |
| `c.LoseHp(amount)` | "Lose N HP": ignores Block, not modified, with the card as source. |
| `c.Apply<T>(target, amount)` | Applies a power, the player as applier, this card as source. A null target is ignored. |
| `c.ApplyToSelf<T>(amount)` | The same on the player. |
| `c.Draw(n)`, `c.GainEnergy(n)` | As named. |

An attack that is not aimed by the card's target type: `c.F.Attack(c.Owner, amount).FromCard(c.Card).Targeting(creature)` (or `.TargetingAllOpponents()`, `.TargetingRandomOpponent()`), then `.Hits(n).Execute()`.

Order matters and is the game's: Bash hits and then applies Vulnerable; Iron Wave gains Block and then hits; Hemokinesis loses HP and then hits. Write the steps in the order the spec's `play` list gives.

### 3.2 `TextContext` (the `t` in `Text`)

The game shows damage and block numbers with the player's own modifiers while the card is in hand, and plain everywhere else. The context does that for you. For a hand card aimed at one enemy or at all of them the snapshot also carries `vs`: the same text worked out against each enemy that can be targeted, kept only where it differs from the text at rest (a Vulnerable enemy). That is what the real mod writes (`Describe.HandCard`); cards that pick their own target at random get no `vs`, there as here.

| Member | Shows |
| --- | --- |
| `t.Damage("Damage")` | Base + Strength, x Weak, x Shrink, truncated. At rest there is no target, so no Vulnerable; for a "vs" entry the numbers are worked out against `t.Target`. (`t.Damage()` is the var named "Damage".) |
| `t.Block("Block")` | Base + Dexterity, x Frail, truncated. |
| `t.N("Cards")` | A plain number as a whole number (power amounts, card counts, HP loss, energy, hit counts). |
| `t.V(name)` | By the var's declared kind. |
| `t.CalculatedDamage()` | The card's calculated damage number (`DamageMultiplier`), previewed as damage. The multiplier is asked with `t.Target` (null at rest, so Bully shows only its base there) and counts as 0 outside a fight, as in the game. |
| `t.CalcBlock(base, extra, (f, target) => multiplier)` | A calculated block number, previewed as block. |
| `t.Upgraded`, `t.Card`, `t.F` (null outside a fight), `t.Pile` | For text that changes. |
| `t.Target` | The enemy a "vs" entry is being worked out against; null for the text at rest. A text that works out a damage number of its own must pass it on (`F.ModifyDamage(t.Target, ...)`), or its "vs" entries will be wrong. |
| `TextContext.Count(n, "card")` | "1 card" / "2 cards". |

### 3.3 Cards that react from a pile

```csharp
private sealed class VoidBehaviour : CardBehaviour   // nested in your module class
{
    public override void AfterCardDrawn(Fight f, CardInstance card, bool fromHandDraw)
    {
        if (ReferenceEquals(card, Card)) f.LoseEnergy(Card.Get("Energy"));
    }
}
// in the CardDef:  Behaviour = () => new VoidBehaviour(),
```

A behaviour is a `Listener` that knows its `Card`. It hears hooks while its card is in any combat pile (hand, draw, discard, exhaust, play) and the player is alive; a card that left the fight (a played Power card) hears nothing. State goes in its fields.

### 3.4 What a `CardInstance` offers

`card.Def`, `Type`, `Target`, `Pile`, `Title`, `IsUpgraded`, `IsUpgradable`, `Keywords` (settable), `Has(CardKeywords.X)`, `HasTag(CardTags.Strike)`, `Get(name)` / `Set(name, value)` / `Add(name, delta)` for this instance's numbers (Rampage raises its own Damage this way), `ExhaustOnNextPlay`, `RetainThisTurn`, `ReplayCount`, `IsDupe`, `IsClone`, `Affliction`.

Cost changes on one instance, with the game's lifetimes: `SetCostThisTurn(n)`, `SetCostUntilPlayed(n)`, `SetCostThisTurnOrUntilPlayed(n)`, `SetCostThisCombat(n)`, and the same with `AddCost...(delta)`; each takes `reduceOnly: true` when it may only lower the cost. `f.CostOf(card)` is the cost after these and after the cost hooks of powers.

## 4. The commands (`Fight`)

Flags for damage and block, from `Props`:

| Constant | Use | Block absorbs? | Strength, Weak, Vulnerable / Dexterity, Frail? |
| --- | --- | --- | --- |
| `Props.Card` | attack or block from a card | yes | yes |
| `Props.MonsterMove` | attack or block from a monster move | yes | yes |
| `Props.CardUnpowered` | a status card hurting its holder (Burn, Infection) | yes | no |
| `Props.CardHpLoss` | "lose N HP" on a card | no | no |
| `Props.NonCardUnpowered` | damage or block from a power, relic or potion | yes | no |
| `Props.NonCardHpLoss` | HP loss from a power (Poison, Inferno's self-damage) | no | no |

Tests on flags: `props.IsPoweredAttack()` (what Strength, Weak, Vulnerable, Flame Barrier ask), `props.IsPoweredBlock()`, `props.IsCardOrMove()`.

**Damage and block**

| Command | Does |
| --- | --- |
| `f.Attack(attacker, perHit)` / `f.Attack(attacker, target => perHit)` | Starts an `AttackCommand` (powered, one hit). Every command has an `Id` that is unique in the fight; a power that must recognise "its" attack again between `BeforeAttack` and `AfterAttack` keeps that number, not the object (Gigantification). `.FromCard(card)`, `.Targeting(creature)` / `.TargetingAllOpponents()` / `.TargetingRandomOpponent()`, `.Hits(n)`, then `.Execute()`. Each hit is its own damage instance; a single target that dies loses the remaining hits; a dead attacker stops; a random target is drawn from `CombatTargets` once per hit, also with one enemy alive. Does nothing while the fight is over or ending. |
| `f.Damage(target, amount, props, dealer, cardSource)` | One damage instance on one creature (or a list: all lose HP first, then reactions, then deaths). For everything that is not an attack: HP loss, a power's damage, a potion. The dealer of self-damage is the creature itself. A dead dealer deals nothing. Not switched off while the fight is ending. Returns `DamageResult` (`UnblockedDamage`, `BlockedDamage`, `WasTargetKilled`, ...). |
| `f.ModifyDamage(target, dealer, amount, props, cardSource)` | The damage chain as a number, without hitting. |
| `f.DamageNumberOf(card)`, `f.CalculatedDamage(card, target)` | A card's per-hit damage number before modifiers (Thrash reads another card's with these, then runs it through `ModifyDamage` with a null target). |
| `f.GainBlock(creature, amount, props, cardPlay)` | Block. Cards use `c.GainBlock`. Powers and potions: `f.GainBlock(owner, amount, Props.NonCardUnpowered)`. Monster moves: the monster's `GainBlock(f, amount)`. Returns the unrounded amount; 0 while the fight is over, ending, or not yet started. |
| `f.LoseBlock(creature, amount)` | Removes Block without damage. |
| `f.Heal(creature, amount)` | Heals up to max HP. |
| `f.GainMaxHp(creature, amount)`, `f.LoseMaxHp(creature, amount, isFromCard)` | As in the game (gaining heals the same amount). |
| `f.Kill([creatures])` | Kills outright (the engine already kills a leader's minions). |

**Powers**

| Command | Does |
| --- | --- |
| `f.ApplyPower<T>(target, amount, applier, cardSource)` | Applies or stacks. Goes through Artifact; sets the skip-first-tick flag on a new debuff on the player; refused while the fight is ending. Returns the instance, or null when nothing was attached. To read the result, look at the creature (`target.PowerAmount<T>()`). |
| `f.ApplyPower("ClassName", ...)` | The same for a power of another file. |
| `f.ModifyPowerAmount(power, offset, applier)` | Changes an existing power by an offset through the same modifiers; removes it when the amount says so. |
| `f.Decrement(power)` | Minus 1, a plain countdown. |
| `f.TickDownDuration(power)` | The duration tick of Vulnerable, Weak, Frail: honours the skip flag. Only for powers that use the game's `TickDownDuration`. |
| `f.RemovePower(power)`, `f.RemovePower<T>(creature)` | Removes and runs `AfterRemoved`, also for a power that was already off its owner, as the game's remove command does. |
| `creature.GetPower<T>()`, `HasPower<T>()`, `PowerAmount<T>()` (and by class name) | Reading. `creature.Powers` is in applied order. |

**Cards and piles** (index 0 of every pile is its top)

| Command | Does |
| --- | --- |
| `f.Hand`, `f.DrawPile`, `f.DiscardPile`, `f.ExhaustPile`, `f.PlayPile`, `f.AllCards`, `f.PileOf(pile)` | Reading. The card being played is in the Play pile, not in the hand. Copy a pile (`.ToList()`) before you change it in a loop. |
| `f.Draw(n)` | Draws: "should draw" is asked once, the hand stops at 10, the discard pile is shuffled in when the draw pile is empty, "card drawn" hooks run after each card. Returns the cards drawn. |
| `f.Shuffle()` | A forced shuffle of discard and draw pile into a new draw pile. `f.ShuffleIfNecessary()` is the usual one. |
| `f.Exhaust(card)` | To the exhaust pile, with the exhaust hooks. |
| `f.Discard(card)` | A real discard by an effect, with the discard hooks. |
| `f.MoveCard(card, pile, position)` | A plain move with no discard or exhaust hooks (`PilePosition.Bottom` appends, `Top` is index 0, `Random` draws an index from the Shuffle stream). A card headed for a full hand lands in the discard pile. |
| `f.CreateCard("ClassName", upgraded)` then `f.AddGeneratedCard(card, pile, position, creator)` | A new card into a pile, with the "card generated" hook. Into the draw pile the default is the **bottom**; say `PilePosition.Random` when the spec says a random position. |
| `f.CloneCard(card)` | A new card copying another as it is now (Anger, Juggling); then `AddGeneratedCard`. |
| `f.AddStatusCards("Slimed", count, pile, position, creator)` | What monsters do: cards created one by one straight into a pile. |
| `f.Upgrade(card)` | Upgrades for the rest of the fight if it can be. |
| `f.Transform(card, def, upgraded)` | Replaces a card in place (Primal Force). The new card counts as generated: `AfterCardEnteredCombat`, `AfterCardChangedPiles`, then `AfterCardGeneratedForCombat` with the player as creator. |
| `f.DiscardAndDraw(cards, drawCount)` | The game's DiscardAndDraw (Gambler's Brew): every card is discarded with its hooks, in the order given, then the cards are drawn. With no card to discard nothing is drawn. |
| `f.RemoveFromCombat(card)` | Takes a card out of the fight (a played Power card). Everyone else still hears `AfterCardChangedPiles` for it. |
| `f.AutoPlay(card, target, type)` | Plays a card for free by the game's auto-play rules (random target from `CombatTargets`, X = energy at hand, an unplayable card just moves on). |
| `f.AutoPlayFromDrawPile(count, position, forceExhaust)` | Havoc, Cascade, Distilled Chaos: takes the cards off the draw pile first, then plays them. |
| `f.GeneratePlayCount(card, target)` | How many times a play of the card would run (spends one-shot doubling charges): only for effects that count as a play, like Drum of Battle's trigger. |
| `f.Afflict(card, "Entangled", amount)`, `f.ClearAffliction(card)` | Afflictions monsters put on cards (register the affliction with `r.Affliction(new AfflictionDef(...))`). A card that already carries one keeps it: another kind is refused, and so is the same kind again unless the affliction is registered with `IsStackable: true` (in the game only Galvanized and Tainted are; none in the arena is). Returns whether anything changed. |
| `f.CostOf(card)`, `f.LocalCostOf(card)`, `f.CanPlay(card, out reason)` | Reading. `CostOf` is what the card costs to play; `LocalCostOf` is its own cost before the cost hooks of powers (Touch of Insanity asks for both). |
| `Content.Registry.IroncladPool` | The card pool for random generation, in pool order. Filter it (`c.Type == CardType.Attack && c.IsGeneratableInCombat`) and pick with the stream and the call the spec names (`f.Rng.CombatCardGeneration`). |

**Energy, potions, monsters**

| Command | Does |
| --- | --- |
| `f.Energy`, `f.MaxEnergy`, `f.GainEnergy(n)`, `f.LoseEnergy(n)` | Gains go through the energy-gain hook and are not capped. |
| `f.Potions`, `f.GainPotion(def)`, `f.RemovePotion(slot)`, `f.PotionSlot(def)` | The belt (3 slots). |
| `f.UsePotionFromHook(slot, target)` | Uses a potion from inside a hook, with every step of a use by hand: out of the belt, the potion hooks around its effect, a line in the history (Fairy in a Bottle). |
| `f.Enemies` | The live enemy list in acting order. Copy it before a loop that may kill. `f.HittableEnemies` for effects that pick among hittable enemies. `f.Roster` is every enemy by its `e<j>` index. |
| `f.AddMonster(monster, slot)` | A summon (section 8.3). |
| `f.Stun(enemy, nextMoveId)` | The engine's stun. |
| `f.Escape(enemy)` | The monster leaves without dying. |
| `f.IsHittable(creature)`, `f.IsTargetableEnemy(creature)`, `creature.IsAlive`, `IsPrimaryEnemy` | Reading. |

**Reading the fight**: `f.Player`, `f.Turn`, `f.Round`, `f.CurrentSide`, `f.IsEnding`, `f.IsOverOrEnding`, `f.RoomType`.

**Random streams** (`f.Rng`): `Shuffle` (shuffles, random pile positions, Stampede's pick), `MonsterAi` (monster move choices; the machine uses it for you), `Niche` (HP rolls; the engine), `CombatTargets` (random targets), `CombatCardSelection` (random picks among the player's own cards), `CombatCardGeneration` (random new cards), `CombatPotionGeneration`, `CombatEnergyCosts`, and the encounter's own stream, which only your `GenerateMonsters` gets. Calls: `NextInt(max)`, `NextInt(min, maxExclusive)`, `NextFloat(max)`, `NextBool()`, `NextItem(list)` (no draw for an empty list), `Shuffle(list)` (the game's shuffle). The spec's `randomness` field names the stream and the number of draws; match both.

**History** (`f.History`), for "did X happen this turn": `CardPlaysStartedThisTurn(f, CardType.Attack)`, `CardPlaysFinishedThisTurn(f, type)`, `CardPlaysFinishedThisTurn(f)`, `WasCardExhaustedThisTurn(f)`, `CardsExhaustedThisTurn(f)`, `HpLossEventsThisTurn(f, creature)`, `HpLossEvents(creature)` (the whole fight), `CardBlockGainsThisTurn(f, creature, exceptPlay)`. For anything else: `CountThisTurn(f, e => ...)`, or `Count(e => ...)` / `Any(e => ...)` over the whole fight (an entry has `Kind`, `ActorId`, `OtherId`, `Card`, `CardId`, `PlayId`, `Amount`, `Props`, `Name`, ...). Entries are recorded at the game's points; "this turn" is the same round, side and turn number.

## 5. Card choices

When an effect needs the player to pick cards, ask and use the answer as if the call had simply waited:

```csharp
Play = c =>
{
    c.GainBlock(c.V("Block"));
    if (c.F.ChooseHandCardToUpgrade() is { } chosen) c.F.Upgrade(chosen);
},
```

| Call | The game's | The model sees |
| --- | --- | --- |
| `f.ChooseFromHand(prompt, min, max, filter)` | `FromHand` | screen `hand_select`: `select:<handIndex>`, then `confirm` |
| `f.ChooseHandCardToExhaust()` | `FromHand` with the exhaust prompt, exactly 1 | the same |
| `f.ChooseHandCardToUpgrade()` | `FromHandForUpgrade` | the same, in "upgrade" mode |
| `f.ChooseFromPile(pile, prompt, min, max, filter)` | `FromCombatPile` | screen `card_select`, kind `combat_pile_select`: `card:<n>` (and `confirm` when min and max differ) |
| `f.ChooseOneOf(cards, canSkip)` | `FromChooseACardScreen` | screen `card_select`, kind `choose`: `card:<n>`, `skip` |
| `f.ChooseFromGrid(cards, prompt, min, max)` | `FromSimpleGrid` | screen `card_select`, kind `select` |

They follow the game's rules for not asking: nothing with no candidate; with min = max and no more candidates than that, all are taken without a question (the upgrade choice also takes a single candidate without asking). The prompt is your own wording.

How it works, and what it asks of you: the first time the answer is not known, the action is abandoned (by an exception you must not catch) and the fight shows the selection screen; once the player answered, the whole action is run again from a copy of the fight taken before it began, and this time the call returns the answer. Because the engine is deterministic, the second run is the first one continued. That only holds if your code keeps all its state in the fight (section 2).

## 6. Hooks: what there is and exactly when each fires

Override these on a `Power`, `Monster`, `Relic` or `CardBehaviour`. Every hook gets the fight first. "Guarded" hooks reach nobody once the fight is over or ending (the test is made once, when the dispatch starts); the others still run for the killing hit.

**A round, in order.** Player turn N:

| # | Hook | When |
| --- | --- | --- |
| 1 | `BeforeSideTurnStart(f, Side.Player, participants)` | First step: turn and round numbers are already raised; before monsters choose moves; before Block is cleared (Block gained here by the player is wiped). |
|   | (engine) | Every monster chooses its next move (not on turn 1). |
| 2 | `ShouldClearBlock(f, creature)` | Return false to keep Block (Barricade). The player's Block is not cleared on turn 1 at all. |
| 3 | `AfterBlockCleared(f, creature)` | Runs on turn 1 too ("block next turn" effects). |
| 4 | `ShouldResetEnergy`, then `AfterEnergyReset(f)`, `AfterEnergyResetLate(f)` | Energy was set to `ModifyMaxEnergy` (base 3). |
| 5 | `BeforeHandDraw(f)`, `BeforeHandDrawLate(f)`, `ModifyHandDraw(f, count)`, `ModifyHandDrawLate` | Before the draw of 5. On turn 1 Innate cards are then moved to the top. |
|   | (engine) | The hand draw; `AfterCardDrawnEarly` / `AfterCardDrawn` after each card. |
| 6 | `AfterPlayerTurnStartEarly(f)`, `AfterPlayerTurnStart(f)`, `AfterPlayerTurnStartLate(f)` | Right after the draw (Inferno, Crimson Mantle). |
| 7 | `AfterSideTurnStart(f, Side.Player, participants)`, `...Late` | After the draw (Demon Form, Plating's countdown, Poison on the player). |
| 8 | `AfterAutoPrePlayPhaseEnteredEarly/.../Late(f)` | Last step before the player may act. Then a win check. |

The player acts. Per card: section "A card play" below. Then "end_turn":

| # | Hook | When |
| --- | --- | --- |
| 9 | `AfterAutoPostPlayPhaseEntered(f)` | First thing after the turn was ended (Stampede, Howl from Beyond). |
| 10 | `BeforeSideTurnEndVeryEarly`, `BeforeSideTurnEndEarly`, `BeforeSideTurnEnd(f, Side.Player, participants)` | Three passes, hand still there (Plating's Block in Early). Then a win check. |
|    | (engine) | Ethereal cards without an end-of-turn effect are exhausted, left to right (`ShouldEtherealTrigger` can veto); then cards with `TurnEndInHand` resolve, left to right. |
| 11 | `BeforeFlush(f)`, `BeforeFlushLate(f)` | Then a win check. |
|    | (engine) | `ShouldFlush`; the hand goes to the discard pile as a plain move (no discard hook); Retain cards stay. `AfterFlush`. This-turn cost changes and flags end. |
| 12 | `AfterSideTurnEnd(f, Side.Player, participants)`, `...Late` | After the flush; the side is still the player's (temporary Strength, Rage, No Draw, Constrict end or act here). |

Enemy turn N:

| # | Hook | When |
| --- | --- | --- |
| 13 | `BeforeSideTurnStart(f, Side.Enemy, participants)` | |
| 14 | `ShouldClearBlock`, `AfterBlockCleared` per enemy | All monsters lose their Block now, before anything else. |
| 15 | `AfterSideTurnStart(f, Side.Enemy, participants)`, `...Late` | Before any monster acts (Slow's reset, Poison on monsters). Then a win check. |
|    | (engine) | Each monster of the list as it was now performs its move; a win check after each. |
| 16 | `BeforeSideTurnEndVeryEarly/Early/BeforeSideTurnEnd(f, Side.Enemy, participants)` | |
| 17 | `AfterSideTurnEnd(f, Side.Enemy, participants)`, `...Late` | **Vulnerable, Weak and Frail tick here, for everyone.** Territorial, Colossus, Flame Barrier's removal. Then a win check; round and turn go up; back to 1. |

`participants` is the list of creatures of the side whose turn it is, taken when the turn (or its end) starts. The usual two filters: `participants.Contains(Owner)` ("my owner's side": temporary Strength, Ritual, Poison) and `side == Side.Enemy` ("once per round, whoever owns me": Vulnerable, Weak, Frail).

**A card play, in order** (R24 to R28 of rules-turns.md): cost paid → `AfterEnergySpent` → card to the Play pile (`AfterCardChangedPiles`) → `ModifyCardPlayResultPile` (where it will go: decided now) → `ModifyCardPlayCount` and, on a listener that changed it, `AfterModifyingCardPlayCount` → per run: `BeforeCardPlayed(f, play)` → the effect → `AfterCardPlayed(f, play)` → `AfterCardPlayedLate` → after the last run the card moves to its pile (`AfterCardExhausted` if exhausted) → `AfterHandEmptied` if the hand is empty → win check. An auto-played card gets `BeforeCardAutoPlayed` first and pays nothing.

**All hooks by subject** (G = guarded):

| Hook | G | Fires |
| --- | --- | --- |
| `BeforeCombatStart(f)`, `BeforeCombatStartLate(f)` |  | Once, after every monster has its powers and first intent, the fight just marked in progress. Block can be gained here. |
| `AfterCombatEnd(f)`, `AfterCombatVictoryEarly(f)`, `AfterCombatVictory(f)` |  | On a win only. Victory hooks run after the player's powers, piles and Block are gone. |
| `ShouldPlay(f, card, autoPlay)` | G | False forbids a play. |
| `ModifyEnergyCost(f, card, cost)`, `ModifyEnergyCostLate` | G | Whenever a cost is read. Return the new cost. Never called for X or cost-less cards. |
| `AfterEnergySpent(f, card, amount)` | G | After the cost was paid. |
| `ModifyXValue(f, card, x)` | G | When an X effect reads its X. |
| `ModifyCardPlayResultPile(f, card, isAutoPlay, pile, position)` | G | Before the effect (Corruption sends Skills to `PileType.Exhaust`). |
| `ModifyCardPlayCount(f, card, target, count)`, `AfterModifyingCardPlayCount(f, card)` | G | Before the first run. |
| `BeforeCardAutoPlayed(f, card, target, type)` | G | Before an auto-play. |
| `BeforeCardPlayed(f, play)` | G | Before each run; the card is in the Play pile, the cost is paid. |
| `AfterCardPlayed(f, play)`, `AfterCardPlayedLate` |  | After each run, also after the killing card. |
| `AfterHandEmptied(f)` | G | The hand is empty after a card or potion finished (not inside a nested auto-play). |
| `ShouldDraw(f, fromHandDraw)` | G | Once per draw request. |
| `AfterCardDrawnEarly(f, card, fromHandDraw)`, `AfterCardDrawn` | G | After each single card, before the next (Hellraiser plays Strikes in Early; Void costs energy). |
| `AfterShuffle(f)` | G | After a reshuffle. |
| `AfterCardExhausted(f, card, causedByEthereal)` | G | After the card reached the exhaust pile. |
| `AfterCardDiscarded(f, card)` | G | A real discard only. |
| `AfterCardGeneratedForCombat(f, card, creator)` | G | A created card was put in a pile. |
| `AfterCardEnteredCombat(f, card)` | G | A card that was in no pile entered one (created or transformed in). The new card's own behaviour hears this. |
| `AfterCardChangedPiles(f, card, oldPile)`, `...Late` |  | Any move between piles. |
| `ModifyEnergyGain(f, amount)` | G | A gain through `GainEnergy`. |
| `ModifyMaxEnergy(f, amount)` | G | Whenever max energy is read. |
| `ModifyDamageAdditive(f, target, amount, props, dealer, cardSource)` |  | Per hit, all additions first. Return the addend (0 for none). `target` is null in card previews. |
| `ModifyDamageMultiplicative(...)` |  | Per hit, after all additions. Return the factor (1 for none). |
| `ModifyDamageCap(f, target, props, dealer, cardSource)` |  | Return the most a hit may do. |
| `BeforeDamageReceived(f, target, amount, props, dealer, cardSource)` |  | After the modifiers, before Block absorbs (Thorns). The hit still lands. |
| `ModifyHpLostBeforeOsty`, `...BeforeOstyLate`, `ModifyHpLostAfterOsty`, `...AfterOstyLate` |  | After Block: the HP about to be lost, as a decimal. Return the new amount (Slippery: `AfterOsty`; Buffer: `AfterOstyLate`). |
| `AfterModifyingHpLostBeforeOsty(f)`, `AfterModifyingHpLostAfterOsty(f)` |  | On a listener whose HP-loss modifier changed the whole-number result. |
| `AfterBlockBroken(f, creature)` |  | A hit took Block to 0. |
| `AfterCurrentHpChanged(f, creature, delta)` |  | HP went down (negative) or up. |
| `AfterDamageGiven(f, dealer, result, props, target, cardSource)` |  | After every damage instance, also the killing one. |
| `AfterDamageReceived(f, target, result, props, dealer, cardSource)`, `...Late` |  | After every damage instance the target survived, also fully blocked hits and hits of 0. Not for the hit that kills. With several targets in one hit: all lose HP first, then this runs target by target. |
| `BeforeAttack(f, attack)`, `AfterAttack(f, attack)` | G | Once per attack command. |
| `ModifyBlockAdditive(f, target, block, props, cardSource, cardPlay)`, `ModifyBlockMultiplicative` | G | Per Block gain; additions, then factors. |
| `AfterBlockGained(f, creature, amount, props, cardSource)` | G | After every Block gain, with the unrounded amount. |
| `BeforePowerAmountChanged(f, power, amount, target, applier, cardSource)` | G | Before an application or change. |
| `ModifyPowerAmountGivenAdditive`, `...Multiplicative` | G | The applier's side of an application. |
| `TryModifyPowerAmountReceived(f, power, target, amount, applier, out modified)`, `AfterModifyingPowerAmountReceived(f, power)` | G | The receiver's side (Artifact). The second is called on the listener that returned true. |
| `AfterPowerAmountChanged(f, power, amount, applier, cardSource)` | G | After a power landed or changed by a non-zero amount (Vicious). |
| `BeforeDeath(f, creature)` |  | HP is 0; before the death is decided. |
| `ShouldDie(f, creature)`, `ShouldDieLate` |  | False prevents the death; that listener then gets `AfterPreventingDeath(f, creature)`. |
| `AfterDeath(f, creature, wasRemovalPrevented)` |  | After the death was decided; the dead monster is still in the list and still has its powers (Infested summons here; Shrink and Constrict end when their applier dies). |
| `ShouldCreatureBeRemovedFromCombatAfterDeath(f, creature)` |  | False keeps a dead monster in the list (Illusion). |
| `ShouldPowerBeRemovedOnDeath(f, power)` |  | False keeps a power on its dead owner. |
| `ShouldStopCombatFromEnding(f)` |  | True keeps the fight going with no primary enemy alive. |
| `ShouldAllowHitting(f, creature)` | G | False: cannot be hit and cannot receive powers. |
| `ShouldAllowTargeting(f, creature)` | G | False: cannot be aimed at. |
| `AfterCreatureAddedToCombat(f, creature)` |  | After a summon. |
| `BeforePotionUsed(f, potion, target)`, `AfterPotionUsed` |  | Around a potion's effect. |

Not available because nothing in scope needs them: the purely visual "after modifying" callbacks, "after preventing block clear" and "after preventing draw", the attack hit-count hook, the shuffle-order hook, "should afflict", "after room entered", extra turns, orbs, stars, pets. If content needs one, add it to the engine from the game's code, with a check.

Two listener facts that matter (R2): a power applied during a dispatch does not hear that dispatch; a power removed from a living owner during a dispatch still hears the rest of it.

## 7. Writing a power

```csharp
/// <summary>Weak: powered attacks dealt by its owner are multiplied by 0.75. Ticks at the end of every enemy turn.</summary>
public sealed class WeakPower : Power
{
    public override string Name => "Weak";                       // the game's English title
    public override PowerType Type => PowerType.Debuff;
    public override string Describe() => $"Deals 25% less damage with attacks. Wears off in {Turns(Amount)}.";

    public override decimal ModifyDamageMultiplicative(Fight f, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) =>
        ReferenceEquals(dealer, Owner) && props.IsPoweredAttack() ? 0.75m : 1m;

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (side == Side.Enemy) f.TickDownDuration(this);
    }
}
```

Register it: `r.Power<WeakPower>();`. The class name is the game's class name and must be top-level and public with a parameterless constructor.

| Member | Meaning |
| --- | --- |
| `Owner`, `Applier`, `Amount`, `AmountOnTurnStart` | Set by the engine. Change `Amount` only through the commands. |
| `Name` | The game's English title (the spec has it). The snapshot's power `name`. |
| `Type` | `Buff` or `Debuff`: the fixed type. |
| `StackType` | `Counter` (default) or `Single`. May depend on the amount (Shrink). |
| `InstanceType` | `None` (default: one instance, applying again adds), `Instanced`, `InstancedPerApplier`. |
| `AllowNegative` | True for Strength-like powers: kept at negative amounts, removed only at exactly 0, and a negative amount counts as a Debuff for Artifact and for the snapshot. |
| `IsVisible` | False hides it from snapshots and from Artifact. |
| `DisplayAmount` | What the snapshot prints as `amount` (left out when 0). Default: `Amount`, also for `Single` powers (Minion shows 1). |
| `Describe()` | Your own plain wording with the live numbers. The snapshot's power `text`. |
| `OwnerIsSecondaryEnemy`, `ShouldBeRemovedAfterOwnerDeath`, `ShouldOwnerDeathTriggerFatal` | Minion-like switches. |
| `BeforeApplied(f, target, amount, applier, cardSource)` | For a new instance, just before it is attached, with the amount after Artifact (0 when blocked). |
| `AfterApplied(f, applier, cardSource)` | Once, after a new instance was attached with a non-zero amount. Not called when stacks are added: use `AfterPowerAmountChanged` with `ReferenceEquals(power, this)` for that. |
| `AfterRemoved(f, oldOwner)` | After removal by a command or by the owner's death. |

Extra state (Inferno's self-damage number, Slow's counter) is a field of your class. Temporary Strength kinds derive from `TemporaryStrengthPower` (`IsPositive => false` for the kinds that lower Strength) and only add a `Name`.

Timing pitfalls, all in the specs: Vulnerable, Weak and Frail tick at the end of the *enemy* turn for everyone; a debuff that is new on the player skips its first tick whoever applied it; "after damage received" does not run for the hit that kills; a power that reacts to cards played by the player acts after the player's own powers; Block from a power is `Props.NonCardUnpowered`.

## 8. Writing a monster and an encounter

```csharp
public sealed class Nibbit : Monster
{
    private const int ButtDamage = 12;          // one constant for the intent and the hit, so they cannot differ

    public bool IsFront { get; set; }           // flags the encounter sets before the fight
    public bool IsAlone { get; set; }

    public override string Name => "Nibbit";    // the game's English name
    public override int MinHp => 42;            // ascension 0: the LAST argument of the game's ascension helper
    public override int MaxHp => 46;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var butt = m.Move("BUTT_MOVE", Butt, Intent.Attack(ButtDamage));
        var slice = m.Move("SLICE_MOVE", Slice, Intent.Attack(6), Intent.Defend);
        var hiss = m.Move("HISS_MOVE", Hiss, Intent.Buff);
        butt.Then(slice).Then(hiss).Then(butt);

        var init = m.Conditional("INIT_MOVE");
        if (IsAlone) init.When(() => IsAlone, butt);
        else init.When(() => !IsFront, hiss).When(() => IsFront, slice);
        return init;                             // the initial state: a move, or a branch resolved at once
    }

    private void Butt(Fight f) => Attack(f, ButtDamage);
    private void Slice(Fight f) { Attack(f, 6); GainBlock(f, 5); }
    private void Hiss(Fight f) => ApplyToSelf<StrengthPower>(f, 2);
}
```

Register it: `r.Monster<Nibbit>();`.

### 8.1 The monster class

- `Name`, `MinHp`, `MaxHp` (both ends included; the engine rolls HP on the Niche stream, avoiding values other enemies already have).
- `AfterAddedToRoom(f)`: starting powers (`ApplyToSelf<SlipperyPower>(f, 1)`). At the start of a fight Block cannot be gained here (the fight is not in progress yet), powers can; write the call anyway if the game makes it, the engine drops it exactly as the game does (Cubex Construct).
- Moves are instance methods taking the fight. Everything in a move takes effect at once, in the order written. Helpers: `Attack(f, damage, hits)` (a powered attack on the player, hit by hit), `GainBlock(f, amount)` (move Block: the monster's own Frail and Dexterity apply), `ApplyToSelf<T>(f, amount)`, `ApplyToPlayer<T>(f, amount)`, `AddStatusCards(f, "Slimed", count, PileType.Discard, PilePosition.Bottom)` (nobody is named as the cards' creator: the game's monsters name none). Anything else through `f`.
- Flags and counters are fields or properties of your class.
- A monster is also a `Listener`: it hears every hook after its own powers.
- Do not reproduce animation flags, sounds or speech.

### 8.2 The move machine

`BuildMachine` creates the states through the builder and returns the initial one.

| Builder call | State |
| --- | --- |
| `m.Move(id, method, intents...)` | A move. Set what follows with `move.FollowUp = state` or `a.Then(b)`. `MustPerformOnce = true` for a move the machine may not leave before it was performed. |
| `m.Random(id)` | A weighted random branch. `.Add(state, weight, repeat, maxInARow, cooldown)`: `repeat` is `MoveRepeat.Forever` (default), `CannotRepeat`, `UseOnlyOnce`, or `MaxInARow` with `maxInARow: n`; `cooldown: n` forbids the option while it is among the last n chosen moves. List the options in the game's order. A weight may be a `Func<float>`. |
| `m.Conditional(id)` | Takes the first listed option whose condition holds: `.When(() => flag, state)`, `.When(f => f.Player.HasPower<WeakPower>(), state)`, `.Otherwise(state)`. |

Use the game's state ids (the spec's `machine` and `moves` fields have them): the mod prints `stunned` for the id `STUNNED`, and checks and reports quote ids.

The engine applies the game's timing, so you only describe the graph: the first move is settled when the monster joins (a random initial branch draws one MonsterAi number then); all later moves are chosen at the start of the next player turn, in enemy-list order, after the end-of-enemy-turn ticks; a random branch draws exactly one number per visit, also when only one option is open, and takes the first listed option when every weight is 0; the move log holds the chosen moves (the initial move included) and is what the repeat rules read.

**Intents**: `Intent.Attack(damage, hits)` (the base number: the snapshot shows it with Strength, Weak and the player's Vulnerable applied, per hit, and `x<hits>` when hits is above 1), `Intent.Buff`, `Debuff`, `DebuffStrong`, `Defend`, `Status(count)` (reads `statuscard: N`), `CardDebuff`, `Sleep`, `Summon`, `Heal`, `Stun`, `Escape`, `Hidden`, `Unknown`, `Intent.DeathBlow(damage)`. List them in the game's order.

**Moves from outside the machine.** `f.Stun(enemy, nextMoveId)` puts in the engine's stun (it must be performed once; then `nextMoveId` follows, by default the move the monster was about to make). `monster.SetMoveImmediate("STATE_ID")` jumps to one of the machine's own moves. A power that forces a move of its own (Illusion's revive) uses `monster.SetMoveImmediate(new ForcedMove("REVIVE_MOVE", [Intent.Heal], followUpId, MustPerformOnce: true, Performer: this))` and implements `IForcedMovePerformer` to do the work when the move is performed. `monster.MoveLog` is the log.

### 8.3 Summons

`f.AddMonster(new Wriggler { StartStunned = true }, "wriggler1")` (or by class name). The new monster's HP is rolled, it gets its starting powers, and its first move is settled at once when it joins during the player's turn. One that joins during the enemy turn does not act that turn; its first move is settled at the start of the next player turn. Monsters with a slot are kept in the encounter's slot order (which is the acting order). The model's `e<j>` index of an enemy never changes and is never reused; new enemies get the next index.

### 8.4 The encounter

```csharp
public sealed class NibbitsNormal : Encounter
{
    public override IReadOnlyList<string> Slots => ["front", "back"];
    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new Nibbit { IsFront = true }, "front"), (new Nibbit(), "back")];
}
```

Register it: `r.Encounter<NibbitsNormal>();`. Override `RoomType` for elites and bosses (`RoomType.Elite`, `RoomType.Boss`) and `IsWeak` for the weak encounters. Return the line-up in the game's creation order (HP is rolled in that order). Every random choice of the line-up is drawn from `rng`, the encounter's own stream, with the calls and in the order the spec's `composition` gives (`rng.NextItem(list)`, `rng.NextBool()`, ...), also the draws whose answer is forced.

## 9. Writing a potion

```csharp
r.Potion(new PotionDef("FirePotion", "Fire Potion", PotionRarity.Common, TargetType.AnyEnemy)
{
    Text = _ => "Deal 20 damage.",
    Use = p => p.F.Damage(p.Target!, 20, Props.NonCardUnpowered, p.Owner),
});
```

- Target: `AnyEnemy` asks for an enemy (`potion:0>e1`); every other kind is used with `potion:0`. `p.Target` is the chosen enemy, the player for `AnyPlayer` and `Self` potions, null otherwise. For "all enemies": `p.F.HittableEnemies`.
- `Usage`: `CombatOnly` (default), `AnyTime`, or `Automatic` (cannot be used by hand). `CanUse` for a potion's own extra condition.
- Potion damage and Block are `Props.NonCardUnpowered`: not modified, Block absorbs.
- The potion has left its slot before `Use` runs. A win check follows.
- A potion that reacts while it sits in the belt (Fairy in a Bottle) sets `Hooks` to a `Listener` object. That object is shared by every fight: it must hold no state. It finds its slot by looking for itself in `f.Potions` and uses itself with `f.UsePotionFromHook(slot, target)`.
- `CanBeGeneratedInCombat = false` where the game says so.

## 10. Leaving something out

```csharp
r.Exclude(ContentKind.Card, "Tank", "multiplayer only: the game filters it out of every single-player pool");
```

Use it for what cannot occur in a single-player Ironclad act-1 fight and for what the engine cannot reproduce exactly. Generators skip excluded names, lookups say why, and the self-check lists every exclusion. Excluding is never a failure; approximating is.

## 11. Scripted checks

```csharp
public sealed class Cards9Checks : IArenaChecks
{
    public void Run(Check c)
    {
        c.Group("Bash: 8 damage, then 2 Vulnerable", () =>
        {
            var f = CheckKit.VsDummy();                 // starter deck in a fixed order against one idle 100 HP dummy
            f.Play("Bash", 0);                          // by title or class name, at enemy e0
            c.Equal(92, f.Enemy(0).Hp, "8 damage");
            c.Equal(2, f.Enemy(0).PowerAmount<VulnerablePower>(), "2 Vulnerable");
            c.Equal(1, f.Energy, "costs 2");
        });
    }
}
```

- `Check`: `c.Group(name, () => ...)` (one per card, power, move or rule; a throw inside is one failure and the other groups still run), `c.Equal(expected, actual, what)`, `c.That(condition, what)`, `c.Sequence(expected, actual, what)`.
- `FightBuilder`: `.Deck(...)` (shuffled by the seed) or `.DrawOrder(...)` (drawn in exactly this order, first name first; "Bash+" is upgraded), `.Add(...)`, `.Hp(hp, maxHp)`, `.Potions(...)`, `.Encounter("NibbitsNormal")` or `.Enemy(monster, hp: 44, slot)` / `.Enemy("ClassName")` (repeat for several; `.Slots(...)` when they have slots), `.Room(...)`, `.Seed(n)`, `.Floor(n)`, `.MaxTurns(n)`, `.Relics(...)`, then `.Start()`: turn 1's hand is drawn and the player may act.
- `CheckKit.VsDummy(dummy, seed)`, `CheckKit.VsDummies(...)`, `CheckKit.StarterInOrder` (turn 1 hand: Strike, Strike, Defend, Defend, Bash; turn 2: Strike, Strike, Strike, Defend, Defend), `f.HandTitles()`, `f.Seen()` (the whole snapshot as a string), `CheckKit.PlayOut(f)`.
- `CheckDummy`: a scripted monster. `new CheckDummy { Hp = 40, DisplayName = "Left", Steps = [new DummyStep("HIT", Damage: 7, Hits: 2), new DummyStep("GUARD", Block: 5), new DummyStep("HEX", Then: (f, me) => f.ApplyPower<WeakPower>(f.Player, 1, me))], OnAdded = (f, me) => f.ApplyPower<ArtifactPower>(me, 1, me) }`. It cycles through its steps; the default idles.
- Stepping a fight: `f.Do("play:0>e0", "end_turn")` (throws with the legal ids when an id is refused), `f.Play("Strike", 0)`, `f.EndTurn()`, `f.Execute(id)` (returns the refusal instead of throwing), `f.LegalActions()`, `f.Snapshot()`.
- Setting up a situation without playing up to it: `f.Put("Bash+", PileType.Hand)` (or a `CardDef`; `PilePosition.Top` for the top of the draw pile), `f.Clear(pile)`, `f.SetHp(creature, n)`, `f.SetBlock(creature, n)`, `f.SetEnergy(n)`, `f.SetPotion(slot, "FirePotion")`, `f.Enemy(j)`, any command (`f.ApplyPower<...>(...)`, `f.Stun(...)`), `monster.ForceNextMove("STATE_ID")`, and `f.RunAsAction(x => ...)` to run commands that may open a card choice.
- Reading results: the creatures, the piles, `f.IntentTexts(enemy)`, `f.IncomingDamage()`, `Fight.CardText(f, card, PileType.Hand)`, `f.CostOf(card)`, `f.Rng.MonsterAi.Counter` (how many numbers a stream gave so far), `monster.MoveLog`.

What to check for every piece of content: every number in the spec (base and upgraded), the order of steps where it matters, each trigger at its real moment (play it across a turn boundary), the edge cases the spec's notes call out, the random stream and number of draws where the spec names them, the intent strings, and for cards the hand text with a modifier on (`"Deal 9 damage."` with 3 Strength). Take the expected values from the spec's worked examples, not from running the code.

Run them:

```
scripts/arena.sh selfcheck                                   # everything, a minute or two
scripts/arena.sh selfcheck --checks Cards1Checks,PotionsChecks   # only these classes, a few seconds
scripts/arena.sh honesty --scenarios 300                     # promise against outcome, at scale
scripts/arena.sh content                                     # what is registered and what is left out, with every text
```

`selfcheck` lists what the registry holds and leaves out against the game's lists, runs every checks class, then plays generated fights with the scripted players (each twice: the replay must match; no action may be turned down), runs the honesty check on those fights, and plays two fights over HTTP through the tools a model gets: a generated one, and one built by hand that is sure to open a hand selection (Armaments) and to use a potion. It must end in `PASSED` with no warnings in the build.

`honesty` (`Sim/Honesty.cs`) is the check to run after any change to the engine or to a card's text: before every action it notes what the snapshot promised (each enemy's intents and the incoming damage; a card's cost and the numbers of its text, its `vs` text for the enemy it is aimed at; a potion's numbers) and compares that with what the action then did, hit by hit as the damage chain left it. A difference must have a cause the check can tell from what happened before it (the player died first, the target died, an end-of-turn auto-play changed something after the snapshot, a card ran twice, a random-target card has no `vs`); one that has none fails the check and is a fault to look into. `ARENA_HONESTY_TRACE=<scenario id>,<policy>` in front of the command writes every action of that one fight with everything it did.

A check should be able to fail. After writing one, break the rule it checks in a scratch copy and see that it does.

## 12. For the scenario generator and the scripted players

- `Sim/Scenario.cs` must keep: `public sealed record Scenario(string Id, int Seed, string Tier, string Summary)`, `Scenarios.Generate(int count, int seed)`, `Scenarios.FromId(string id)` (throws `ArgumentException` for an id it did not make), and `internal static FightSetup Scenarios.Setup(Scenario scenario)`, which `ArenaGame` calls. `FightSetup(Deck, Hp, MaxHp, Potions, Encounter, Floor, Seed)` is in `Sim/Engine/FightState.cs`: deck entries are `DeckCard(className, upgraded)` in deck order, potions are class names by slot (null for empty; three slots), the encounter is a class name. Ask the registry what exists: `Content.Registry.Cards`, `IroncladPool`, `Potions`, `EncounterNames`, `Has(kind, name)`, `IsExcluded(kind, name, out reason)`; `Content.Registry.NewEncounter(name).RoomType` and `.IsWeak`. `Tuning.MaxTurns` is the arena's one house rule (a fight not won by the end of turn 40 is lost).
- A scripted player implements `IPolicy` (`Name`, `Choose(GameSnapshot)`) and has a parameterless constructor; it is found by reflection. `Policies.All` lists "Random", "Greedy", "Lookahead" in that order, then any others by name. The snapshot it gets can be `combat`, `hand_select` or `card_select`; it must return one of its action ids. A player that searches implements `IEnginePolicy` and also gets the running `Fight`: it must not act on it; it copies it with `fight.CloneDeterminized(seed)` or `fight.Clone()` (exact), steps copies with `Execute(id)` / `LegalActions()`, and reads `IsOver`, `Won`, `Player.Hp`, `Enemies`, `IncomingDamage()` and so on. Copies work in the middle of a card choice too. A copy costs a few microseconds.
- `CloneDeterminized(seed)` replaces what the player cannot know: every random number still to come, and the order of the draw pile (a card the player put on top stays there). The hidden cards are first sorted by what they look like, so the real order leaves no trace: two fights that show the same snapshot give the same copy for the same seed. In the middle of a card choice the copy forgets the future at the moment the choice is answered.
- What the three players are: Random takes any legal action but ending the turn or throwing a potion away. Greedy goes one card at a time from the snapshot alone (kill, else block, else the most damage per energy) and uses its potions in elite and boss fights, at half HP, or when it would die this turn. Lookahead searches the turn on determinized copies. `docs/arena.md` has their numbers.

## 13. What is left out, and known gaps

Left out on purpose (`r.Exclude`, printed by `scripts/arena.sh content`):

- **Demonic Shield and Tank**: multiplayer only. The game takes them out of every single-player pool.
- **Colorless Potion**: it offers three cards of the colorless pool, 64 cards the arena does not have. Adding it means adding that pool, card by card, as the Ironclad's was.
- **Entropic Brew**: it fills the belt with random potions of the whole pool, Colorless Potion among them, so it cannot be exact before that one is.

The scenario generator rolls a drop again when it comes up with one of these two potions (2 of 48).

Not in the simulator at all, because a single act-1 Ironclad fight never meets them: relics other than Burning Blood, other characters' cards and powers, the Underdocks act, events' fights, enchantments, pets, orbs and stars, co-op.

Known gaps in the engine, none of which shows in a snapshot today:

- A monster that joins during the enemy turn has its first move in `NextMove` at once; the game leaves it unset until the next player turn starts. No snapshot is taken in between.
- `Fight.Afflict` has no "should afflict" hook, and no affliction in the arena is stackable; the stackable branch is written from the game's code and has no check of its own.
- A random branch adds its weights up as the game does (in double, narrowed to float once). No in-scope monster has weights for which that differs from adding in float, so no check can tell the two apart.
- Titles: every card, power, monster and potion name was compared with the game's English name table; the powers that have no title of their own there (the temporary Strength kinds) carry the name of the card or potion they come from. The player's creature is named by the character's title in that table, "The Ironclad" (`Fight.PlayerName`), as the mod's `state.me.name` and `get_deck` show it.
