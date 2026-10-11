# Enemy move forecast ("next")

In a fight the snapshot shows each enemy's current intent. `Forecast.For(enemy)` adds what the enemy does after that intent, as far as it is certain, so the model can plan across turns. The code is `src/SpireMind/Game/Forecast.cs`. The recipe it follows, with file and line evidence from the decompile, is on the Mac at `~/Projects/sts2-notes/forecast-v0.107.1/forecast.md`.

None of this has run in a live fight. It is built against v0.107.1 (0 warnings), its self-test passes, and it was run outside the game against the game's own classes, by the writer and again by a reviewer (sections "Checked without the game" and "Review"). "Expected" below means read from the game's code. The reader does not call it yet: the hook is one line, added by hand.

## What the model sees

```json
{"i": 0, "name": "Byrdonis", "hp": 84, "max_hp": 84,
 "powers": [{"name": "Territorial", "amount": 1}],
 "intents": ["attack 17"],
 "next": ["attack 4x3", "attack 19", "attack 6x3"]}
```

- `next` is an array of short strings, one per coming move, in order, starting with the move after the shown intent. At most 3 by default.
- An entry is the move's intents in the words `intents` uses, joined with " + ": `"attack 18 + buff"`, `"attack 3x3"`, `"debuff"`, `"statuscard: 2"`.
- When the first uncertain step is a random pick whose chances are exact, it is the last entry: `"50% debuff | 50% attack 11"` (whole percents, the game's own order, options that read the same merged). Nothing follows it.
- A number with `~` (`"attack ~11"`, `"attack ~4x3"`) is today's number for a hit that something known will change before it lands. The hit count is never marked.
- No `next` means unknown, never "it does nothing". An empty array is never sent.

The note to show with it, once per snapshot, is `Forecast.Hint`:

> next = this enemy's moves after its shown intent, in order and only as far as they are certain (a last entry with % is a random pick at exactly those chances); the list ends where the move after depends on what happens in the fight, and no next means unknown, not nothing. A number already counts the Strength the enemy gains by then and otherwise assumes today's Weak, Vulnerable and other modifiers; ~ marks one that something already in play will change. What you do can replace the list: a stun, waking it, or an HP threshold or death named in its powers.

It says that the enemy's own Strength gains are already counted, so the model does not add Territorial or a buff move a second time. It does not say that a summon replaces a list (the first draft did): a summon adds an enemy and changes nobody's moves, except where a living count decides a branch, and there the list has already stopped.

## The hook

In `CombatReader.EnemiesJson`, after `json["intents"] = Intents(enemy, cs);`:

```csharp
if (Forecast.For(enemy) is { } next) json["next"] = next;
```

And once per snapshot that has any, for example in `BoardState` after the enemies are set:

```csharp
if (state["enemies"] is JsonArray foes && foes.Any(e => e?["next"] is not null)) state["next_note"] = Forecast.Hint;
```

`EnemiesJson` also serves the hand-selection snapshot and the card choice screens in a fight. That is fine: the forecast makes its own timing tests and returns null whenever the shown intents are not still to come. Main thread only, like the reader.

Public surface:

| Member | What |
|---|---|
| `public static JsonArray? Forecast.For(Creature enemy, int maxSteps = 3)` | the `next` array or null. Never throws. More than 6 steps are never walked. |
| `public const string Forecast.Hint` | the note above |
| `public static string Forecast.SelfTest()` | the self-check; needs no game |

## Where it comes from

Everything is read from objects the game already holds (`enemy.Monster.NextMove`, `enemy.Monster.MoveStateMachine`).

- A move's follow-up is plain data: the id of the next state, looked up in the machine's `States` as the game does at a roll.
- A random pick is a list of options with a repeat rule, a cooldown and a weight each. The chance of each move follows from those and the move log, without rolling: weight over the sum of weights. When every option is blocked the game takes the first one. One open option means certain, and the walk goes on with a private copy of the log, to which each walked move is appended.
- A conditional branch is a delegate. It is never trusted as "what it says now", because the game asks it one or more turns later. It is followed only where a rule written for that monster makes the answer certain at that time (table below).
- A number is the game's own preview of the hit on the local player (`AttackIntent.GetSingleDamage`, and `Hook.ModifyDamage` with the same arguments), applied to the future move's intent, plus the Strength the enemy is known to gain first.

The walk stops at the first step that is not certain. Earlier entries stay.

## What it never does

- It never rolls, never asks for a random number, never enters, leaves, forces or performs a state, and never builds a machine. Not called, ever: `RandomBranchState.GetNextState`, any `RollMove`, `PrepareForNextTurn`, `SetMoveImmediate`, `ForceCurrentState`, `PerformMove`, `OnEnterState`, `OnExitState`, `GenerateMoveStateMachine`, `MonsterModel.AssetPaths`, an intent's hover tip, texture or animation.
- It calls a weight, a branch condition or a damage delegate only for the 104 enemy classes that were read one by one in v0.107.1. Two-Tailed Rat's weights are never called at all.
- It checks itself on every call: the monster AI random stream's counter, the move log's length and the shown move are compared before and after. A difference switches the feature off for the session with one error line.
- It never works out what a trigger "will" do (a stun, a phase change, a revive). It reads what the machine holds now.

## When it says nothing

The whole feature is off for the session, with one log line saying why (`forecast: switched off for this session: ...`), when:
- the game is not v0.107.1, commit 59260271 (`ReleaseInfoManager`);
- one of the private members below is not found;
- a read changed the game's state (the check above);
- three reads have failed with an exception. Each failing monster class is logged once.

No `next` in a snapshot when the fight is not running or is ending, it is not the players' side, the local player's phase is `None` (the stretch between the side switching back and the enemies' roll), or there is no local player.

No `next` for an enemy when its class is not one of the 104 (another mod's monster, a test dummy), it has no machine, its move is not rolled yet, it is acting right now, its shown move has been performed already, the machine's current state is not the shown move, or anything threw.

The list ends early at: an escape or death blow (the monster is gone); a missing follow-up; a state that is not exactly one of the game's three state classes; a branch without a certain answer; a random pick with several open options (its chances are the last entry); an intent of an unknown kind.

## Per-monster rules

Classes: 72 with fixed cycles, 10 with a conditional branch in the loop, 22 with a random pick in the loop. Every other monster class gets nothing.

| Monster | Where | Rule | Result |
|---|---|---|---|
| Bowlbug Rock | after Headbutt | The branch itself always gives the headbutt (the dizzy move is only reached through the engine's stun). But a fully blocked headbutt stuns it for a turn first, and blocking a hit in full is ordinary play, not a rare trigger. | the list stops: no `next` while a headbutt shows. While it shows `stunned`, `next` is the one headbutt that follows. |
| Knowledge Demon | after Ponder | The curse comes while its private counter plus the curses performed before that roll is below 3, then Slap. | followed |
| Test Subject | after Respawn | Its respawn move raises the counter before the branch is asked: respawns now plus respawn moves before that roll below 2 means Multi Claw, else the third form's Lacerate. | followed |
| Lagavulin Matriarch | after Sleep | Asleep loses 1 at the end of each enemy turn: sleep while its amount minus the step is 1 or more, then Slash. | followed. Unblocked damage wakes her at once (stun, then Slash). |
| Slumbering Beetle | after Snore | The same with Slumber, then Roll Out for the rest of the fight. | followed. Each hit that removes HP also takes 1 Slumber, so hitting it brings the attack closer. |
| Frog Knight | after For The Queen | Tongue Lash once it has charged (private flag, or the charge is among the moves ahead). | followed then; before its charge the list stops (its HP at that time decides). |
| Queen | both branches | Off With Your Head once the Amalgam has died (private flag, one way). | followed then; while the Amalgam lives the list stops. |
| Fabricator | after every move | Alive on its side now, plus the bots its coming moves add (2 for Fabricate, 1 for Fabricating Strike), below 4: the random pick, 50% each. Nothing else raises that count. | chances as the last entry; at 4 or more the list stops (kills decide). |
| Living Shield | after Shield Slam | An ally alive at that time. | the list stops |
| Ovicopter | after Tenderizer | How many are alive on its side at that time. | the list stops |
| Wriggler | after its spawn turn | By slot name, fixed for the fight: the branch's own answer now is its answer then. | followed |
| Two-Tailed Rat | its random pick | Its weights depend on a countdown, a shared count, free slots and the other rats' moves at roll time. They are never called. While it certainly cannot call for backup (countdown after the moves ahead still above 0, shared count at 3, or it has called already) the weights are known to be 1, 1, 1, 0. | exact chances then; else the list stops. |
| The other 21 random monsters, and Fabricator's pick | random picks | Weights are constants (1 everywhere; Fogmog 0.4 and 0.6). At the first roll the game's own private weight routine, which only reads, must give the same numbers as the forecast's arithmetic, or the read fails. | a certain move when one option is open, else chances |
| Fat Gremlin, Thieving Hopper, Gas Bomb, Waterfall Giant | escape, death blow | The monster is gone after that move. | the list ends with it |
| Test Subject | Multi Claw | One more hit after each use. | counted |
| Waterfall Giant | Pressure Gun | 5 more damage after each use. | counted |
| Waterfall Giant | Explode | Its damage is only stored when "about to blow" is performed: the amount of Steam Eruption. Printed only as the step right after a shown "about to blow". | counted |
| The Forgotten | Dread | Its damage counts its own Dexterity; Miasma gives it 2 each time. | counted |

The start-of-fight branches of Exoskeleton, Myte, Nibbit, Phantasmal Gardener and Toadpole are settled before the first snapshot and are never met. Phrog Parasite's random branch is never entered.

## Numbers

A plain number is what the game would show for that move if it were showing now, plus the Strength the enemy gains before that hit:

| Worked in | How |
|---|---|
| Strength a move gives its own monster | a table of 50 moves read from the code (`Roster.Gains`), with the amounts for the ascension level that makes enemies deadlier. The gain always comes after the move's own hit. |
| Territorial, High Voltage | their amount at the end of every enemy turn |
| Ritual | its amount at the end of every enemy turn except the one it was applied in (3 moves apply it) |
| Temporary Strength on the enemy (the player's cards and potions) | undone at the end of the coming enemy turn: the shown move has it, no later one does |
| Vigor (Terror Eel's Thrash gives 6) | goes into every hit of the next attack, then is gone |

Strength is added before anything multiplies, so "with E more Strength" is the game's preview of base + E.

A number carries `~` when one of these is known and not worked in:

| Marked | Because |
|---|---|
| Weak (or Shrink) on the enemy runs out before that hit | the hit will be harder |
| Vulnerable on the player runs out before that hit | the hit will be softer |
| A move that puts Vulnerable on the player comes first, or may (9 moves: Flyconid, Mawler, Ovicopter, Owl Magistrate, Queen, Soul Fysh, Soul Nexus, Terror Eel, Test Subject), by this enemy or by one that acts before it | the hit will be half again as hard. Every later number of every enemy stays marked; the mark is not taken off when the Vulnerable should have run out again. |
| A living Queen whose Amalgam has not died, or a living Obscura, stands beside it | they give Strength to the others |
| The enemy has Suck (Fossil Stalker) | Strength for each of its hits that gets through |
| The enemy has Enrage (Test Subject) | Strength for every Skill the player plays, and blocking is done with Skills |
| A player holds Brimstone | 1 Strength to every enemy at each player turn |
| The player has Colossus, Covered, Diamond Diadem, Intercept or Tainted | short powers that change the damage taken |
| A cap decides today's number (Intangible) | the cap counts down too |
| A gain whose size is not a constant lies in between (Aeonglass, Axebot's boot-up, Entomancer) | |
| Tough Egg's hatch lies in between | hatching removes every power the egg has but Minion: Strength a relic or a run modifier gave it, and Weak or Shrink on it, are gone for the hatchling's hits |
| The enemy or the player has Debilitate | it doubles what Weak and Vulnerable do and runs out on its own count, usually before they do |
| The player has Surrounded (the Crusher and Rocket fight) | a hit from behind is half again as hard, and the player turns around by targeting |
| An enemy of an unknown class is in the fight | nobody read what it does to the others |

Never worked in: debuffs a coming move will put on the player (only the mark above), Block from a defend move, cards beyond the count of a status move, and anything the player does this turn.

## Limits

- The order of moves is the reliable part, the numbers are the weak part.
- Anything the player does can change it: a stun (Whistle), an HP threshold (Ceremonial Beast's Plow, Terror Eel's Shriek), waking a sleeper, breaking Tunneler's Block, killing an ally. Poison on an enemy can do the same before it acts. The forecast is rebuilt from the live state for every snapshot, so the next one is right again.
- Summons are not forecast: a new enemy gets its own `next` once it exists.
- In co-op the numbers are for the local player.
- `next` is not there for monsters from other mods, and for no monster after a game update until the tables are read again.
- "Certain" has one hole of about 1 in 10^16 per roll: a draw of exactly 0 takes the first option.

## Self-check

`Forecast.SelfTest()` runs the chance arithmetic (weights, repeat rules, cooldowns, the all-blocked fallback), the walk over small made-up machines, the Strength projection and the wording against the recipe's worked examples: Flyconid, Twig Slime M, Mawler, Fogmog, Two-Tailed Rat, Byrdonis, Nibbit, Ceremonial Beast, the cultists, Terror Eel. The review added 29 cases worked out by hand from the game's weight routine: Leaf Slime S, more Mawler logs (the use-once roar, a stun on it), Flyconid after its forced spores and stunned, Fossil Stalker, Exoskeleton, a Decimillipede segment coming back, an unknown change at the shown move. It touches no game object and returns `forecast self-test: all 99 checks passed`, or the list of what failed.

To run it from the built mod, without the game:

```sh
scripts/build.sh
. scripts/env.sh
cat > /tmp/forecast-selftest.fsx <<'EOF'
let forecast = System.Reflection.Assembly.LoadFrom(fsi.CommandLineArgs.[1]).GetType("SpireMind.Game.Forecast", true)
printfn "%s" (forecast.GetMethod("SelfTest").Invoke(null, [||]) :?> string)
EOF
"$DOTNET" fsi --quiet --exec /tmp/forecast-selftest.fsx "$PWD/out/build/SpireMind.dll"
```

In the game, `ModEntry.Log(Forecast.SelfTest())` at start does the same.

## Checked without the game

Done once while writing, with throwaway scripts that load the built mod next to the installed game's managed assemblies in a plain .NET process. The game was not started.

- Arithmetic: `Forecast.Weight` and `Forecast.Chances` against a re-expression of the game's own weight and pick routine, on every move log up to length 6 for 16 option lists (15 of them real monsters'): 33,028 logs, no difference.
- Reflection: all nine private members were found in the installed `sts2.dll` with the expected types: `MonsterMoveStateMachine._currentState`, `MoveState._performedAtLeastOnce`, `KnowledgeDemon._curseOfKnowledgeCounter`, `TestSubject._respawns`, `FrogKnight._hasBeetleCharged`, `Queen._hasAmalgamDied`, `TwoTailedRat._turnsUntilSummonable`, `RitualPower._wasJustAppliedByEnemy`, and the static method `RandomBranchState.GetStateWeight(StateWeight, Creature)`.
- Classes: the assembly has 126 concrete monster classes: the 104 reviewed ones, 16 that are not enemies and 6 test doubles.
- Machines: the game's own builder was run on a fresh model of each of the 104 classes. Every state is one of the three state classes, every follow-up is registered, every move id in the tables exists, no fixed-cycle monster leads into a branch (Wriggler's spawn turn aside), every intent is of a known kind. The walk was run from every move of every machine, and the game's weight routine agreed at every random pick. Not covered there: Knowledge Demon and Fabricator, whose rules need a running game.
- Numbers: a minimal fight was put together from the game's classes (a stand-in player, real monsters, real powers) and the forecast compared with what `GetSingleDamage` shows once the Strength is really there: Byrdonis with Territorial, Ceremonial Beast, Nibbit, Damp Cultist, Terror Eel with Vigor, Weak and Vulnerable running out, Intangible, a temporary Strength loss, Flyconid's spores, Test Subject, Waterfall Giant, The Forgotten, Lagavulin, Slumbering Beetle, Frog Knight, Wriggler, Two-Tailed Rat, a stunned Twig Slime M. 41 checks, all as expected.

## Live checks, in the order to try them

1. Nothing in the log. No `forecast: switched off` and no `forecast: no forecast for` line in a whole fight. That covers the build test, the private members and the purity check on every call.
2. Timing. `next` is there in a normal turn and in a hand selection at turn start and at turn end, and never in a snapshot whose intents are last turn's. Log the side, the local player's phase and each enemy's performed flag for a fight if in doubt.
3. Step 1 against the next turn. For several turns and monsters, the first entry of `next` equals `intents` one turn later, text and number. Include Byrdonis (Territorial), Nibbit or Ceremonial Beast (a Strength gain from a move, the second Plow), and a cultist (Ritual).
4. A seeded fight plays out the same with the hook in and out.
5. Flyconid after Vulnerable Spores, Frail Spores, Smash: the next move is Vulnerable Spores, which `next` gives as certain.
6. A stun. Whistle on Twig Slime M while its first pounce shows: `intents` reads `stunned`, `next` starts with the pounce, then the sticky shot for certain.
7. The branch rules, each where its monster turns up: Test Subject after its first and second death, Knowledge Demon's third curse (the one rule that could not be run outside the game), Lagavulin Matriarch and Slumbering Beetle counting down (amount 1 means the attack is next). Bowlbug Rock: no `next` while its headbutt shows, and a fully blocked headbutt really gives `stunned` the turn after.
8. The marks. Each `~` against what the game shows a turn later: an enemy with 1 Weak, the player with 1 Vulnerable, a Flyconid showing its spores, the Amalgam while the Queen burns bright, a Tough Egg that has Strength when it hatches. And unmarked: an enemy under a temporary Strength loss.
9. Two-Tailed Rat: chances on the first two turns, nothing once it may call for backup.
10. Co-op: the numbers are the local player's.
11. A trigger in the enemy turn (Poison taking Ceremonial Beast under its Plow amount): the shown attack does not happen, and the next snapshot is right again.

## Review

A second pass, 2026-10-11, by a reviewer who did not write the code. Nothing was run in the game.

What was read in the decompile: every game member the code touches (all are getters, list reads, delegates of the reviewed classes or the game's own damage preview; all 38 overrides of the three damage-modifier methods only read and none uses the running amount), the turn code that the timing gate leans on, the machines and moves of the 29 Overgrowth and 22 Underdocks monsters, of every monster with a rule of its own, and of five later monsters drawn at random (Exoskeleton, Stabbot, Spiny Toad, Owl Magistrate, Bowlbug Nectar). The gains table and the list of moves that apply Vulnerable were compared with every monster file by a second script: no difference.

What the review changed:

| Finding | Change |
|---|---|
| Bowlbug Rock's next headbutt was printed as certain. A fully blocked headbutt stuns it instead, which the player does all the time. | nothing is said after a shown headbutt |
| Tough Egg's hatch strips its powers (`ToughEgg.cs`, the hatch move), so a nibble printed at today's Strength or Weak was wrong whenever the egg had either. | the hatchling's numbers are marked |
| Debilitate (a player card) doubles Weak on the enemy and runs out by itself; a number was printed plain while Weak stayed and Debilitate ended. | marked |
| Surrounded turns with the player's targeting; Enrage (Test Subject) gives Strength for every Skill the player plays. | marked |
| The hint said a summon can replace the list. | reworded |

What the review ran, outside the game, on the game's own classes (throwaway scripts again, not in the repo):

- A simulation: for 103 of the 104 classes (Knowledge Demon cannot be built outside the game), the game's own machine was rolled forward with the game's own `RollMove` and random stream for 60 turns, many times over, with random stuns, forced death states and the side effects the rules lean on put in by hand. Before every roll the forecast was read; every step it called certain was compared with the move the game then showed. Over six runs with different seeds about 2.9 million certain steps were compared: none went differently. Tens of thousands of random picks: every one was among the listed options, and the frequencies agree with the printed chances.
- `Forecast.For` itself through its timing gate (with the build test stepped over, since it needs the game's file access): a forecast in every phase from Start to End, none in phase None, on the enemies' side, with the fight not running or ending, without a local player, or for an enemy that has left the enemy list.
- 35 more cases in a minimal fight: the changes above, Mawler, Leaf Slime S, Inklet, Exoskeleton, Sludge Spinner, Fogmog, a segment about to reattach, the Queen without her Amalgam, Ritual in place and just applied, Zapbot, Bygone Effigy, Fabricator at each count, and the cases where nothing may be said (move already performed, not rolled yet, not the machine's current state, no machine, a class that was not reviewed).

Still not exercised anywhere: the build test against the real `release_info.json` at run time, the log lines, labels that need the game's text tables (`statuscard: N`), and Knowledge Demon's rule.

## After a game update

The forecast switches itself off (the build test). To bring it back: run the recipe's scans again (new state classes, new branches, new callers of the forced-move methods, new intents with a delegate, new moves that give Strength or apply Vulnerable), fix the tables in `Roster` and the rules in `Pick` and `Options`, then change `ReviewedVersion` and `ReviewedCommit`. A `typeof` that no longer compiles is the first sign.

## Where this differs from the recipe

- No condition entries ("attack 40 if its HP is under 95 by then, else ...") and no "one of:" lists. A branch that turns on the player ends the list.
- Bowlbug Rock: the recipe follows its branch and adds "unless you block all of this turn's hit". Without condition entries there is nowhere to say "unless", so the list stops there.
- Tough Egg's hatch, Debilitate and Surrounded are not in the recipe, and it leaves Enrage to the next snapshot. All four are marked (section "Numbers").
- The number corrections of the recipe's section 3.3 (dividing a factor out, 1.5 for Vulnerable to come, Strength from another enemy) are not applied. Those numbers are marked.
- Brimstone is marked, not added. Its Strength lands in the turn-start hooks, after the roll, so a hand selection during turn setup is read one point short of what the recipe's formula assumes.
- After a move that applies Vulnerable, later numbers stay marked for good. The recipe takes the mark off once a fresh Vulnerable of the listed amount has run out, but the amount a player receives can be changed by relics, and stacking adds turns.
- The reader now lists an enemy that is down and coming back (`"reviving": true`), which the recipe says is missing. Such an enemy gets a `next` from its forced state: a Decimillipede segment `["heal", "33% ... | 33% ... | 33% ..."]`, a Test Subject its next form's attacks.
