# The arena

The arena compares how Claude models play fights, on Mark's Claude subscription, without the game running. It generates seeded fights the way an act-1 run of the game produces them, plays each in a simulator of the game's combat that is built from the game's own data (public build v0.107.1), lets each model play each fight once through the same tools the mod serves, and reports who won with how much HP left. Scripted policies play the same fights for free, as a yardstick.

A fight can be driven in two ways: by Claude Code on the subscription, which is the default and what everything below describes unless it says otherwise, or by the mod's own brain on the API credit (`--brain api`), the program that plays the real game. [Two ways to drive a fight](#two-ways-to-drive-a-fight) sets them side by side.

It lives in `src/SpireMind.Arena`, a console project that only references `SpireMind.Core`. Nothing in it touches the game or the mod, and it writes nothing into the repo: it reads the play prompt and the strategy notes, its sessions may read the wiki under `strategy/`, and nothing in it can change a file there or under `runs/` (see [What a session can read](#what-a-session-can-read-and-why-it-can-write-nothing)).

## What it is and is not

- **The game's fights, rebuilt from the game's data.** Every card, enemy, potion and rule in a fight was read from the v0.107.1 game code and written again for the simulator: the Ironclad's card pool, act 1 "Overgrowth" with its hallway fights, elites and bosses, the potions an Ironclad finds, with the game's numbers, timing and randomness. What could not be made exact is left out and listed, not approximated. See [The simulator](#the-simulator) for what is exact and what is missing.
- **One fight, nothing around it.** No map, rewards, shop, deck building or run journal, and no relics beyond the Ironclad's Burning Blood. It measures combat play: reading intents, damage and block arithmetic, sequencing, potions, when to race and when to block. It says nothing about drafting or pathing, which decide most runs.
- **The real tools and the real prompt.** A fight is served through the same `BotTools`, MCP endpoint and snapshot versioning as the game, and its snapshots are the mod's own, key for key (`state.me`, `enemies[]` with `intents`, `hand[]` with `vs`, piles, potions, the hand and card selection screens, `play:<i>>e<j>` ids). The system text is the in-game brain's own play prompt, strategy notes and wiki index, and a session can open the wiki's pages as the in-game brain can (an enemy's page before an elite or a boss, as the prompt asks). So tool handling (stale versions, `act_sequence`, card choices, stopping at the end) is tested as in the game. A session that ends before its fight is over is started again, as the in-game brain does it.
- **The same fights for every model.** A scenario is fixed by its id: the same deck, HP, potions, enemies and seed whoever plays it, and `Scenarios.FromId` builds it again for the report.

## Commands

```bash
scripts/arena.sh baseline                       # the scripted policies on 30 fights; half a minute, costs nothing
scripts/arena.sh baseline --scenarios 100 --seed 7
scripts/arena.sh run --models claude-haiku-5-5,claude-sonnet-5-5,claude-opus-5-5
scripts/arena.sh run --models claude-opus-5-5 --scenarios 12 --seed 2 --effort high --parallel 1
scripts/arena.sh run --models claude-opus-5-5 --scenarios 40 --seed 7 --brief   # an experiment: one idea switched on
scripts/arena.sh position ~/.config/spiremind/arena/pilot-seed7 boss-878005 claude-opus-5-5 --turn 4 --check   # one turn of a recorded fight, rebuilt and checked
scripts/arena.sh position ~/.config/spiremind/arena/pilot-seed7 boss-878005 claude-opus-5-5 --turn 4           # 5 new sessions play on from there
scripts/arena.sh run --brain api --scenarios 4 --seed 7                # what the mod's own brain would play and cost; starts nothing without --yes-spend
scripts/arena.sh run --brain api --fake-api --model fake-notes --scenarios 4 --seed 7   # the same against a scripted stand-in for the API: no key, no cost
scripts/arena.sh report ~/.config/spiremind/arena/2026-10-10-141500
scripts/arena.sh report ~/.config/spiremind/arena/opus-seed7 --by-label         # a folder with several experiments
scripts/arena.sh compare ~/.config/spiremind/arena/opus-base ~/.config/spiremind/arena/opus-brief
scripts/arena.sh selfcheck                      # the simulator and the tool path still hold; a minute or two, costs nothing
scripts/arena.sh honesty                        # what the snapshots promise against what happens, on 300 fights
scripts/arena.sh content                        # what the simulator holds of the game, what it leaves out, every card text
scripts/arena.sh snapshot elite-880367          # a scenario's first snapshot, as a model gets it
```

`scripts/arena.sh` builds the project (Release) and starts it directly, so Ctrl+C or `kill` reaches the arena itself and it stops its Claude Code sessions. Bad arguments print what is wrong and the usage, and exit with code 2.

### baseline

`baseline [--scenarios N=30] [--seed S=1]` plays every policy in `Policies.All` (Random, Greedy, Lookahead) on every generated fight. It prints one row per fight (id, tier, each policy's `W <HP left>` or `L`, and the fight's one-line summary), then per policy the win rate, the mean HP left on wins, the mean HP the won fights cost and the mean turns, and by tier the wins with the mean HP left on them. It names every fight that was lost to the turn limit and every action a fight turned down; neither should happen, and the exit code is 1 when an action was turned down.

Use it to look at the fights before spending anything: a fight every policy wins untouched tells the models apart no better than one nobody can win. Lookahead searches, so 100 fights take about a minute.

### run

`run --models a,b,c [--scenarios N=8] [--seed S=1] [--parallel K=3] [--effort medium] [--timeout-minutes 30] [--grace-seconds 20] [--stop-at-weekly 0.90] [--out DIR] [--claude PATH] [--repo DIR] [--brief] [--effort-by-tier TIER=LEVEL,...] [--forecast]`

Each model plays each of the N fights once, each fight in its own Claude Code session on the subscription. It prints a line when a fight starts and when it ends, how full the subscription's usage windows are, and the report at the end.

- **The brain** (`--brain claude-code`, the default, or `--brain api`). Everything in this section is Claude Code's. With `--brain api` the same fights are played by the mod's own brain on the API credit, with options of its own (`--yes-spend`, `--max-usd`, `--model`, `--elite-effort`, `--boss-effort`, `--after-fight`, `--fake-api`): see [Two ways to drive a fight](#two-ways-to-drive-a-fight). `--brief`, `--effort-by-tier`, `--claude`, `--grace-seconds` and `--stop-at-weekly` are Claude Code's and are refused with `--brain api`, each with its reason.
- **Models** go to `claude --model` exactly as typed. Use full ids (`claude-haiku-5-5`, `claude-sonnet-5-5`, `claude-opus-5-5`): on Claude Code 2.1.287 the alias `haiku` meant Haiku 4.5. That version printed a `[claude-code:unrecognized_model]` notice for `claude-haiku-5-5` and ran it anyway; such a notice ends up in the result's `notes`. The result also records the model Claude Code's init line names (`cli.model`) and the ids it billed (`models_used`), and the report says so when they differ from what was asked.
- **Order.** Fights start scenario by scenario: none of scenario 2 starts before every fight of scenario 1 has started. With K no larger than the number of models, a model never plays two fights at once. So a batch that is cut short still has (almost) the same scenarios for every model.
- **Output folder** (`--out`, default `~/.config/spiremind/arena/<date-time>`):
  - `results.jsonl`: one JSON line per fight, appended the moment it ends.
  - `run.json`: the batch's settings, the scenarios, the prompt's files, length and hash (never its text), the checkout (`repo`), the Read rule its sessions were given (`read_rule`) and the wiki as the batch found it (`wiki_pages`, `wiki_hash`). It also holds the batch's experiment: its label (`experiment`, `base` for none) and each switch as it was given (`switches`). The file is written anew by every batch, so in a folder that several batches share it describes the last one.
  - `transcripts/<scenario>__<model>.jsonl`: the raw stream-json output of the fight's sessions, for reading how a model played. Where a session was started again there is a line of type `arena` saying so. A batch with a switch on puts its label behind the name (`elite-880367__claude-opus-5-5__brief.jsonl`, with every character that isn't a letter, a digit, `-`, `_` or `.` turned into `_`), so it never replaces the plain batch's transcript in the same folder.
- **The checkout** (`--repo`, default the one the arena was built in) is where the prompt is built from and where the sessions read the wiki: one path goes into the prompt, the sessions' Read rule and `run.json`, by its real path with links followed. A batch started from a frozen copy of the repo therefore reads that copy's wiki, not the live one. Real runs add lessons to the live wiki, so a batch that should give every model the same pages is best run from a copy nothing else touches; the batch says at its end whether the wiki changed under it, and each result carries the wiki's hash.
- **Carrying on.** Run the same command with the same `--out` and it plays only what is missing: a fight that was won, lost or abandoned is the model's own result and is skipped. A fight that ended in an `error` or a `timeout` says nothing about how the model plays, so it is played again. A fight is the same fight only when the scenario, the model, the effort it is played at and the experiment's label are all the same, so a batch at another `--effort` or with other switches plays every fight of its own and leaves the others' lines alone.
- **Experiments.** The last three options are switches for trying an idea: each is off unless given, and with none given a batch is what it was before there were any. [Experiments](#experiments) says what each does and how to compare the results.
- **The time limit** (`--timeout-minutes`, default 30) is for the whole fight, all its sessions together. A fight that isn't over by then is stopped and recorded as a `timeout`. How long a fight takes depends on how busy the service is as much as on the model, so a timeout is no evidence about play: it is in no win rate and no mean, has its own column in the report, and the next run with the same `--out` plays the fight again.
- **Usage.** Claude Code reports how full the subscription's 5 hour and 7 day windows are (`rate_limit_event` lines, whenever a window's rounded percentage moves). The batch prints both as soon as the first fight reports them and again at the end with the difference, and every result line holds what its fight saw last. The numbers are the whole account's: anything else using the subscription meanwhile is in them too.
- **Stopping.** Ctrl+C (or SIGTERM) kills the running sessions, keeps what finished, prints the report and exits with 130. One failed fight never stops the batch. Four things Claude Code itself reports do, each with exit code 1, the results kept and the same command carrying on later:
  - **The 7 day window is as full as `--stop-at-weekly` allows** (a fraction, default 0.90). No new fights start; the ones running are left to end. This is checked while fights run, not only when one ends.
  - **The usage limit is hit**: an error result that says so, or a `rate_limit_event` whose status says requests are refused (`rejected`). No new fights start; the ones running are left to end, usually as errors. When the limit is one model's own (an Opus limit, say), the same command without that model plays the others.
  - **Extra usage** (`isUsingOverage` or `overageInUse` true): the window is used up and requests are billed on top of the subscription. The running sessions are stopped at once and their fights recorded as errors.
  - **A session without the right tools**: its init line lists none of the fight's tools, a tool besides them and `Read`, or no `Read` (see [How one fight is run](#how-one-fight-is-run), step 6). Which tools a session is offered depends on the Claude Code that is installed and on the flags, not on the fight, so every later session would be stopped the same way and each would have cost its start. That fight is an error with `tools_problem` set, no new fights start, and the ones running are left to end.

  Nothing a model writes can trigger any of these. Only Claude Code's own lines are read for it, so a model saying "hand limit reached" is just talking about its hand. The status `allowed_warning` doesn't stop a batch either: Claude Code 2.1.296 sets it when a window fills faster than it empties (90% of the 5 hour window used in its first 72%; 25, 50 or 75% of the week used in its first 15, 35 or 60%), and requests are still served. The batch prints a note the first time it sees it, and `cli.rate_limit_status` records it.
- **Before anything starts**, `claude auth status` must say Claude Code is signed in with `claude.ai`. Signed in any other way, the batch refuses to start, since it would not run on the subscription.

### position

`position RESULTS_DIR SCENARIO MODEL --turn N [--runs K=5] [--models a,b] [--effort medium] [--out DIR] [--label L] [--reshuffle] [--check] [--repo DIR] [--claude PATH] [--parallel K=3] [--timeout-minutes 30] [--grace-seconds 20] [--stop-at-weekly 0.90] [--brief] [--forecast]`

A position test: the fight MODEL played on SCENARIO in the run RESULTS_DIR is rebuilt up to the start of its player turn N and checked against the recording, and new sessions then take the fight over there and play it to the end, K times each. [Position tests](#position-tests) has all of it. `--check` rebuilds, checks and prints the position, and starts nothing; it costs nothing. `--brain api` lets the mod's own brain play the position, with the options and the budget of `run --brain api`. Everything else (the checks before the first session, the stops, carrying on with the same `--out`) is `run`'s. Exit code 1 when the rebuilt fight doesn't match the recording, 2 when the recording or the turn doesn't exist.

### report

`report DIR|results.jsonl [--by-label]` prints the report again from the file, for a finished or interrupted run. It replays the scripted policies on the same scenario ids, so it needs a build that can still generate them. Position runs in the file are printed after the fights, in a part of their own ([The position report](#the-position-report)); a file that holds only position runs prints only that, and replays nothing.

A file that holds fights of more than one experiment is not pooled into one table: two labels are two treatments of the model, and one win rate over both would describe neither. The plain report says which labels the file holds and exits with code 1. `--by-label` prints the tables with one row per model and label (`claude-opus-5-5 [brief]`) and compares each model's rows fight by fight; see [Comparing](#comparing).

### compare

`compare DIR_A DIR_B [--model M]` pairs the fights of two runs on the scenarios both played, for every model both hold or only for M, and prints B against A: the table of [Comparing](#comparing), then every scenario that was won in one run and lost in the other. Each folder must hold a model once, under one label and one effort; a folder with several variants of a model is compared inside itself with `report --by-label`. It starts nothing and replays nothing, so it takes a moment and costs nothing. Exit code 1 when a folder has no results, the two share no model, or a folder holds a model in more than one variant.

Position runs are compared apart from whole fights and paired by position, not by scenario: see [Old prompt against new](#old-prompt-against-new). A folder of whole fights against a folder of position runs has nothing to pair and exits with 1.

### selfcheck

`selfcheck [--scenarios N=30] [--seed S=1] [--checks A,B]` ends with `PASSED` or `FAILED` (exit code 0 or 1). Run it after a change to the simulator or to `SpireMind.Core`, before a batch.

1. **The content** (`Completeness`): every card of the Ironclad's pool, every status card, every Overgrowth encounter and every potion of the Ironclad's pool is either in the registry or left out by name with a reason; nothing is registered twice.
2. **The scripted checks** (`IArenaChecks`, 21 classes, about 158,000 assertions; two of them, `RunStateChecks` and `RunStreamsChecks`, are run mode's, see [Run mode (in progress)](#run-mode-in-progress), and one, `ApiBrainChecks`, is the API brain's, see [What holds it to its word](#what-holds-it-to-its-word)): the engine's rules and every card, power, monster, encounter and potion against numbers worked out by hand from the game's code; the scenario generator against the game's odds; the scripted players; and the runner's own promises (`RunnerChecks`: the flags a session is started with, what the prompt's appendix says about the wiki, that prompt and Read rule name the same checkout, what the init line must list, how Read calls are counted; and for the experiments: the label, that every switch is off by default, the pages `--brief` gives and that the prompt is otherwise unchanged, what each session of a fight is started with, when a fight counts as played, and the numbers of `report --by-label` and `compare` on fights whose HP was chosen by hand; and for position tests: how a transcript is read, that a recorded fight is rebuilt to any of its turns and every difference from the recording is caught, that a game put at a position plays on exactly as the recorded fight did, what a reshuffle changes and what it leaves, what a run's line holds, and the numbers of the position report and of `compare`). `ForecastChecks` holds the forecast switch to the mod's own forecast (its note, its arithmetic, its tables and the powers that mark a number, read from the mod's source file), and puts every power the simulator has on the player and on an enemy to see that no number the forecast prints plain comes out different. `--checks Cards1Checks,PotionsChecks` runs only the named classes and nothing else, in a few seconds.
3. **The arena as a whole** (`SelfCheck`): every policy finishes every generated fight with no action turned down, each fight replays identically, ids rebuild their scenario through `Scenarios.FromId`, the combat snapshot has the game's keys, bad action ids are refused with a reason and change nothing, the snapshot is busy right after an action, a finished fight shows the end decision (`arena_over`, one action `done` that runs and changes nothing), and eight threads calling at once break nothing. The honesty check (below) runs on the same fights. It prints the policies' wins by tier.
4. **The tool path** (`ToolPathCheck`): two fights are played over HTTP through a fight's own bridge, as a model plays them: the MCP handshake, `tools/list`, then `wait_for_decision`, `get_state`, `get_deck`, `get_persona`, `act` and whole turns as `act_sequence`. The first is one of the generated fights with Greedy choosing. The second is built by hand so that it is sure to open a hand selection and to use a potion: three Armaments in the deck and a Fire Potion in the belt, with a player that throws the potion and plays Armaments first. A turn's plan carries the picks for a hand selection its card opens (`select:<i>` by the card's index in the plan's snapshot, then `confirm`) and goes on after it, as the play prompt allows, so the second fight must answer at least one hand selection inside the `act_sequence` that opened it; the line it prints also counts upgraded cards played under their new name after their pick. A second game without a bridge gets the same actions, and every snapshot the tools return must equal that game's. It also checks what the tools must refuse (a stale version, an illegal id, an `act` without a version, `end_turn` before the last id) and that a sequence stops when its next card's target is dead and when the fight ends under it.
5. **The end of the fight**, since a model that is told to wait there waits for ever. The fight's last action is played three more times, each on a new game one action short of the end: by `act`, by an `act_sequence` that ends on it, and by an `act_sequence` with an `end_turn` behind it. Each must hand back the end snapshot at once (well under the tools' 3 s settle time), with no `warning`, no "still busy" and no word about `wait_for_decision`; the third must stop with `The screen changed to arena_over.` A `wait_for_decision` after the end must come straight back with the same snapshot, `act end_turn` must be refused against it, and `act done` must succeed and leave the snapshot at the same version. The line it prints says how long each took.

### honesty

`honesty [--scenarios N=300] [--seed S=1]` plays the generated fights with every scripted policy and checks at every action that the snapshot kept what it promised: before `end_turn` each enemy's intents and `incoming_damage`, before a card its cost and the numbers of its text (its `vs` text for the enemy it is aimed at), before a potion the numbers of its text. Then it compares: every hit as the damage chain left it, the hits per enemy, the Block gained, the energy paid, the status cards added. It prints how many promises were kept and every kind of difference with its cause and an example, and ends with `PASSED` when no difference is left without a cause. [What the snapshot does not promise](#what-the-snapshot-does-not-promise) lists the causes. It also works out the forecast of every enemy before every `end_turn`, whether or not `--forecast` is on, and holds each forecast move against what the enemy then does; `Sim/README.md` section 14 says how.

### content and snapshot

`content` prints what the registry holds against the game's lists, every name left out with its reason, and every card and potion text as a model reads it. `snapshot SCENARIO-ID` prints a scenario's first snapshot; with `--forecast` it prints it as a fight started with that switch shows it (see [Experiments](#experiments)).

## The simulator

`src/SpireMind.Arena/Sim` holds the engine (`Engine/`: the turn loop, damage, powers, cards, monsters' move machines, snapshots), the content (`Content/`: one file per chunk of cards, monsters, elites, bosses and potions), the scenario generator (`Scenario.cs`) and the arena's one own number (`Tuning.MaxTurns`). `ArenaGame` puts a fight behind `IGameBridge`, the seam the real game sits behind. `Sim/README.md` is the maintainer's guide.

### Built from the game's data

The source is the game itself: the public Steam build v0.107.1, decompiled on Mark's Mac. One reader turned the code into spec files (rules of the turn, rules of damage, every card, every encounter, how a run is generated), a second re-derived them from the code, and whoever wrote a content file read the game's classes a third time. Where a spec and the code disagreed, the code won. The code is copyrighted and was never copied: the simulator is its own program, and every card, power and potion text a model reads is the arena's own plain wording with the live numbers.

After the simulator was built, a second pass read the game's code again with fresh eyes, part by part: every hallway monster and encounter (also written a second time as a small independent model and compared turn by turn over 16 encounters and 80 seeds), the three elites and three bosses, all 98 cards and their powers (the static data compared by script with the game's classes), the potions and their powers, the scenario generator's odds, and some 25 generated fights traced action by action against the game's classes. It found no rule, number or order that differed from the game. What it changed is what a model reads and may open: the player is now named `The Ironclad` in every snapshot, as in the real game, and a session can read the wiki's pages. The enemies' numbers and intents also agree with the wiki's own enemy pages, which were written from the game files separately. All of this is a comparison with the decompiled code, not with a running game.

The scope is one character and one act: the Ironclad at ascension 0, single player, act 1 "Overgrowth".

| What | In the game | In the arena |
| --- | --- | --- |
| Ironclad card pool | 87 | 85 (the two multiplayer-only cards are left out) |
| Status cards | 12 | 12 |
| Cards made by Ironclad cards | 1 (Giant Rock) | 1 |
| Overgrowth encounters | 22 (4 weak, 12 regular, 3 elites, 3 bosses) | 22, with all 29 monsters |
| Potions an Ironclad can find | 48 | 46 |
| Powers these need | | 58 |
| Relics | | Burning Blood |

`scripts/arena.sh content` prints every name and every text.

**What is exact** (each has scripted checks against numbers worked out by hand from the game's code):

- **The turn.** The order of every step and hook of a round: Block falling, energy, the draw, start-of-turn powers, the three passes before the hand is discarded, Ethereal and end-of-turn cards, the flush, the enemy turn monster by monster, the end-of-turn ticks, and where a won or lost fight is noticed.
- **Damage and Block.** Additions first, then factors, then caps; exact fractions in flight, cut to whole numbers only where Block or HP changes; every hit of a multi-hit attack on its own; what still happens after the killing blow and what does not.
- **Powers.** Stacking, removal, Artifact, the skipped first tick of a debuff that is new on the player, Vulnerable, Weak and Frail ticking at the end of the enemy turn for everyone, and each power's own hooks at their real moments.
- **Cards.** Cost, type, target, keywords, numbers and upgrade of every card; the order of a card's effects; cost changes with the game's lifetimes; X costs; auto-play; the card choices (hand selection, pile selection, one of three) with the game's rules for when nothing is asked.
- **Piles.** The opening shuffle, Innate cards, the hand limit of 10, the reshuffle (the game sorts the cards first and then shuffles, so the result does not depend on discard order), where generated cards land.
- **Monsters.** HP ranges and the rule that two enemies never roll the same HP; every move's numbers, its effects in order, and the intents it shows; the move machines with their weights, repeat limits and cooldowns; summons, stuns, minions, reviving, and a boss's phase change.
- **Encounters and randomness.** Which monsters stand in a fight and in which order they act; the game's random generator and its separate streams (shuffle, monster moves, HP, targets, card picks, card generation), with the same number of draws for the same decision. The seeds are the arena's own, so a fight does not reproduce a particular fight of a real run.
- **Potions.** Every effect, including the ones that work through a power of their own (Regen, Thorns, Buffer, Ritual and the like) and Fairy in a Bottle's rescue.
- **What the model sees.** The snapshot has the keys, order, labels and action ids of the mod's own readers; intents are worked out as the game works them out (per hit, with Strength, Weak and the player's Vulnerable, before Block); hand cards show the player's own modifiers, and `vs` what the card would do to each enemy.
- **Names.** Every card, power, monster and potion carries the game's English name (compared with the game's name table; names only). The player is `The Ironclad`, the character's title in that table, which is what the mod shows as `state.me.name` and as `get_deck`'s `character`.

**What is left out, and why.**

| Left out | Why |
| --- | --- |
| Demonic Shield, Tank (cards) | Multiplayer only: the game itself takes them out of every single-player pool, rewards and random generation alike. |
| Colorless Potion | It offers three cards of the colorless pool, 64 cards the arena does not have. |
| Entropic Brew | It fills the belt with random potions of the whole pool, Colorless Potion among them, so it cannot be exact before that one is. |
| Relics other than Burning Blood | A real run reaches the boss with several relics (Neow's, elites', the treasure room's, the shop's). None of them is in the arena. |
| Events, shops' wares, the map | The arena plays fights. The scenario generator knows that a path passed a shop or a rest site, and nothing of what an event did. |
| Underdocks as act 1, acts 2 and 3, other characters, ascensions, co-op | Out of scope. |

Nothing in a fight is approximated: a card, monster or potion is either reproduced from the game's code or it is not in the arena.

### House rules

Four things about an arena fight are the arena's, not the game's:

- **The turn limit.** A fight that is not won by the end of turn 40 (`Tuning.MaxTurns`) is lost, so that stalling can neither win nor run for ever. The game has no such limit. No scripted policy ran into it in some 7,000 generated fights; the baseline names every fight that does.
- **Single fights.** HP, deck and potions are given, and nothing carries over. After a win Burning Blood heals 6, as in the game, and that is the "HP left" the results show.
- **No relics beyond Burning Blood.** This makes the later fights harder than they are in a real run, bosses most of all.
- **The player's own history is made up.** Which fight a scenario is, with what deck and HP, comes from the generator below; parts of it are estimates.

### The snapshot

The snapshot is the mod's combat snapshot key for key (`CombatReader`, `CardSelectReader`, `PotionActions` and `Describe`): `run` (act, floor, ascension, room), `turn`, `round`, `energy`, `max_energy`, `me`, `enemies` (each with `i`, `name`, `hp`, `max_hp`, `block`, `powers`, `intents`, and `primary` or `untargetable` where that says something), `incoming_damage`, `hand` (each with `i`, `name`, `cost`, `type`, `text`, `vs`, `playable`, `unplayable`), `draw_pile`, `discard_pile`, `exhaust_count`, `potions` (three slots, each with `slot`, `name`, `text`, `target`, `usable`, or null) and `relics`. Intents read `attack 8`, `attack 5x2`, `defend`, `buff`, `debuff`, `debuffstrong`, `carddebuff`, `statuscard: 2`, `summon`, `heal`, `sleep`, `stun` and `stunned`.

Actions are `play:<handIndex>`, `play:<handIndex>>e<enemyIndex>`, `potion:<slot>`, `potion:<slot>>e<enemyIndex>`, `discard_potion:<slot>` and `end_turn`; an enemy keeps its index for the whole fight, and enemies that join later get the next one. A card that asks for a choice opens the game's own screens: `hand_select` (`select:<handIndex>`, then `confirm`) for Armaments, Burning Pact, Gambler's Brew and the like, and `card_select` (`card:<n>`, `confirm`, `skip`) for a pick from a pile or one of three offered cards. While an action resolves (150 ms after a card, 300 ms after `end_turn`) the snapshot is the game's waiting one: `awaiting_decision` false, `turn`, `round`, `hp` and `block`. `get_deck` lists the deck the fight started with, card by card with its text, and the relic and potions.

**The end of the fight** is a decision with nothing left to do, not a busy snapshot. Its `screen` is `arena_over`, `awaiting_decision` is true, and it has exactly one action: `done`, labelled `The fight is over: you won. Nothing is left to do; stop here.` (or `lost`). `state` holds `result` (`won` or `lost`), `turn`, `me` (name, `hp`, `max_hp`) and, for a fight lost to the turn limit, `reason`. Running `done` succeeds and changes nothing; every other id is refused.

It is shaped this way because of what the tools say otherwise. On a snapshot with `awaiting_decision` false, `act` and `act_sequence` add `The game is still busy. Call wait_for_decision before acting again.`, and a model that does as it is told then waits on a finished fight for ever. With the end as a decision, the `act` that ends the fight comes back with the end snapshot and no warning, an `act_sequence` with ids left over stops with `The screen changed to arena_over.`, and a `wait_for_decision` after the end returns at once. A model that does run `done` gets it back after the tools' 3 s settle time with their remark that nothing changed, which is true; the tools live in `SpireMind.Core` and treat it like any action that changes nothing.

### What the snapshot does not promise

The honesty check (`scripts/arena.sh honesty`) compared promise and outcome over 1,500 generated fights played by all three policies: 4,500 fights and 110,000 decisions. Of 23,000 attack intents, 48,000 card damage numbers, 25,000 Block numbers and 78,000 costs, every cost was paid as shown, and every other difference (1,400 in all) has one of the causes below. Each is how the game's own display behaves, and the real mod shows the same:

| What differed | Why (the rule) |
| --- | --- |
| An enemy did less than it showed, or nothing | The player died earlier in the enemy turn, and the turn stops there. Or the enemy died, was stunned at its HP threshold, or was brought to 0 HP and spent its turn coming back, after the snapshot: effects at the end of the player's turn (Stampede's free Attack, Howl from Beyond, Demise) come after the last snapshot of the turn. |
| An enemy hit for another number than it showed | An intent is worked out when the snapshot is taken. A card played for free at the end of the turn changed the enemy's Strength or Weak afterwards. |
| An attack of several hits stopped early | Its attacker died of Thorns or Flame Barrier in the middle of it, or the player died. |
| A card hit fewer times than its text says | Its target died first. The remaining hits of an aimed card are lost. |
| A card that hits a random enemy did other damage than its text says | The game shows such a card without a target, so what the enemy it lands on adds (Vulnerable and the like) is not in the number. Only cards aimed at one enemy or at all of them get `vs`. |
| A card that ran twice (Duplicator, One-Two Punch, Soldier's Stew) did other damage on its second run | The text shows one run. The second run meets what the first one changed: the Vulnerable it applied, the Strength it gave, its own growing damage, a tripling that the first run used up. |
| A hit of an all-enemies card of several hits differed from its text | An earlier hit of the same card killed the Shrinker Beetle, and the Shrink it had put on the player ended with it. Or the first hit killed the Phrog Parasite, and the following hits also took the Wrigglers that came out of it. |
| A card gave no Block although its text says so | The card costs HP first, and that killed the player. |

Two things a snapshot never shows, in the game as here: an enemy's Block is not taken off a card's damage number, and what a power does to HP after Block (Slippery's cap of 1, Buffer) is not in it either.

### Scenarios

An id is the tier and a seed (`elite-880367`), and those two decide everything. `Scenarios.Generate(count, seed)` deals tiers in a fixed pattern, 4 normal, 2 elite, 2 weak and 2 boss in every 10, so a longer list starts with the shorter one. The harder fights tell players apart, so they get more room than a run gives them.

| Tier | The fight | Floor | HP on entering | Deck | Potions held |
| --- | --- | --- | --- | --- | --- |
| weak | one of the act's first three fights (4 encounters) | 2 to 11 | 68 to 80, the first fight always full | 9 to 13 cards, 10.8 on average | 0.4 on average |
| normal | a later hallway fight (12 encounters) | 5 to 15 | 35 to 75 | 10 to 20, 14.1 | 1.3 |
| elite | one of the 3 elites | 7 to 15 | 35 to 75 | 11 to 19, 14.5 | 1.4 |
| boss | one of the 3 bosses, after the rest site | 17 | 55 to 80 | 13 to 22, 16.8 | 1.6 |

(Measured on 500 scenarios, so the deck ranges are what that sample held, not limits: over 30,000 scenarios elite decks ran from 9 to 20 cards and boss decks from 10 to 22, with 1 boss deck in 100 at 12 cards or fewer. A Nutritious Oyster from Neow raises maximum and entry HP by 11.)

A scenario is a moment of a run that is generated the way the game generates one (`run-generation.md` of the spec has the sources):

- **From the game's code:** the act's lists of encounters (three weak ones, then regular ones, each from a bag that is emptied before it is refilled, never two with a shared tag in a row; fifteen elites the same way; one boss); the floors that are fixed (the first fight on floor 2, the treasure room on 10, the rest site on 16, the boss on 17) and the map's bans (no elite or rest site below floor 7, none of the same kind twice in a row); what an Unknown room turns out to be, with the game's growing odds; after every fight won on the way the potion roll (40%, moving by 10 points after each roll, elites 12.5 points more), which potion (10% rare, 25% uncommon, 65% common, then one of 16), and the three cards offered with the game's rarity odds and pity counter; Neow's relics that change the starting deck or HP; the Ironclad's starting deck and 80 HP.
- **Derived:** which rooms a path passes on the floors in between. The game builds a whole map; the generator draws each floor from the shares the spec measured on its port of the map generator. Paths come out close to the measured ones.
- **Estimates** (plain constants in `Scenario.cs`, leaning on one logged run): a card is taken from 85% of offers, any offered card equally likely, so decks are not drafted with a plan; 40% of rest sites upgrade a card; the first shop removes a Strike half the time; a held potion was drunk in an earlier hallway fight with 20% and in an elite with 50%; which relic was taken from Neow; and the HP ranges above. The HP ranges are coarse: they go by the kind of fight and the floor, not by what came before it. The fourth hallway fight gets the later range even when no elite stood between it and the three weak fights (one normal-tier fight in five), which is some 12 to 15 HP lower than a real run would likely arrive with; a rest site just before a fight earns no heal; and the boss's range does not ask whether the last rest site was used to upgrade. Every player meets the same HP, so the comparison between them is untouched.
- **Not modelled:** relics, events, cards and potions bought in shops.

A card or potion the arena leaves out is rolled again (two potions of 48; no card of a single-player run).

### The scripted policies

The scripted policies (`src/SpireMind.Arena/Policies`) keep nothing between calls.

- **Random** takes any legal action other than ending the turn or throwing a potion away, until nothing else is left, then ends the turn. It uses potions like any other action.
- **Greedy** decides from the snapshot alone, as a model does, one card at a time with no plan: finish an enemy it can finish, else block while more is coming than it has Block, else the most damage per energy into the weakest enemy, then powers and free cards. It reads what a card does from its text and `vs`. It never sets up and never spends HP. Its potions go at once in an elite or boss fight, and in any fight once it is at half HP or would die this turn.
- **Lookahead** searches the orders of plays the hand and the energy allow, through the enemy turn, on copies of the fight in which everything it cannot know is replaced (the draw order, the random numbers to come), and scores where each leaves it. It sees one turn only. It uses a potion when that wins the fight, saves its life, or is worth a lot in an elite or boss fight.

### How hard it is

Over 500 generated fights (seed 11), with the game's numbers as they are:

| Tier | Fights | HP on entering | Random | Greedy | Lookahead |
| --- | --- | --- | --- | --- | --- |
| weak | 100 | 77 | 100%, 70 HP left | 100%, 77 | 100%, 78 |
| normal | 200 | 56 | 83%, 33 | 90%, 40 | 100%, 46 |
| elite | 100 | 54 | 40%, 23 | 44%, 28 | 84%, 38 |
| boss | 100 | 69 | 6%, 15 | 9%, 20 | 56%, 28 |
| all | 500 | | 62% | 67% | 88% |

(Win rate, and the mean HP left on the fights won, Burning Blood's 6 included. Three more sets of 100 fights, seeds 1 to 3, gave 55 to 65%, 66 to 71% and 83 to 89% overall. Random and Lookahead draw their own random numbers from the snapshot they are shown, so their numbers move a little with any change to a snapshot's text; Greedy's do not. These were measured after the player's name became `The Ironclad`.)

Nothing was tuned to get these: the monsters' HP and damage and the cards' values are the game's. What follows from that:

- **The weak tier tells nobody apart by wins.** The act's first three fights are easy in the game too (a lone Nibbit, a Shrinker Beetle, a Fuzzy Wurm Crawler, small slimes), and every policy wins all of them. HP left still separates players a little: Random ends them about 8 HP lower than Lookahead.
- **Bosses are harder than in a real run.** The arena's player arrives with a deck that was picked at random from the offers, no relics and nothing bought, where a real player has several relics and a deck built for the fight. Greedy almost never wins a boss; Lookahead wins a little over half. In the 60 boss fights of seeds 1 to 3 Lookahead won 16 of 22 against the Ceremonial Beast, 9 of 22 against The Kin and 8 of 16 against Vantom; Random won 3 of the 60, one against each boss. The bosses were checked against the game's code again; they are as the game has them.
- **Elites and bosses are where planning shows.** Lookahead beats Greedy by 40 to 50 points there and by 10 on normal fights.
- **A sample of 30 fights swings.** The tiers hold 12, 6, 6 and 6 fights of it; read the HP comparison, not the win counts.

A change to the content or to the generator changes what a scenario id rebuilds, so results from before and after don't compare.

## Run mode (in progress)

The arena is growing a run mode: a whole act played through the same screens, snapshots and action ids the mod gives, with HP, the deck, gold, potions and relics carried from room to room, so that drafting, pathing, resting and buying can be tested as well as fights. Two pieces are built, in `src/SpireMind.Arena/Sim/Run` (`README.md` and `STREAMS.md` there are the maintainer's pages). The first is the frame: the state that lasts between rooms, the hand-over of the player to a fight and back (HP, max HP, gold, the belt, relics with what they have counted, the deck), a run as a stack of screens that can be copied and replayed from its list of action ids, the run behind the tools' bridge with that list saved to disk, and the end screens (the mod's `game_over` after a death, then `arena_over`). The second is the start of a run exactly as the game makes it from a seed: the act roll, the fifteen long random streams, both relic bags, and every act's events, hallway fights, elites, boss and Ancient. Six real v0.107.1 saves that the checks carry are rebuilt from their seed and the profile's unlock state alone, down to the number of draws. The map, the rewards, the rooms, the relics and the events are not built yet, so there is nothing to play and no command for it: `run`, `baseline` and the rest play single fights, exactly as before.

To try what is there: `scripts/arena.sh selfcheck --checks RunStateChecks,RunStreamsChecks` runs run mode's checks in a few seconds (a player carried through scripted fights with every number worked out by hand, replays and copies, the real saves, a real run's gold, potion and card rolls); with `ARENA_GOLDEN_SAVES=<folder>` set, or the folder `~/Projects/sts2-notes/golden-saves` present, it also rebuilds every `*.save` in it. The plan and its parts lists are in `~/Projects/sts2-notes/run-mode-v0.107.1/`.

## Experiments

An experiment is a batch with one idea switched on, played on the same fights as a batch without it, so that the two can be compared fight by fight. Every idea is a switch of `run` that is off unless it is given. With no switch a batch is what it was before there were any: the same prompt and prompt hash, the same snapshots, the same fights, and result lines that differ in one new field, `experiment`, which says `base`.

That was checked on 2026-10-11 against a build of the commit the switches were added on top of (b4afe53, whose arena has none). The three scripted policies played 100 scenarios (seeds 1 and 7) on both builds: 7,426 snapshots, every deck and every result, byte for byte the same. And the fake CLI played the same 20 fights through both runners from one checkout: the same prompt hash, the same 20 session prompts, the same 303 tool results, and result lines that differ only in `"experiment": "base"`.

The standard command lines: the same 40 fights (seed 7) for one model, once plain and once with each switch, each batch into its own folder, then each against the plain one.

```bash
A=~/.config/spiremind/arena
scripts/arena.sh run --models claude-opus-5-5 --scenarios 40 --seed 7 --out $A/opus-base
scripts/arena.sh run --models claude-opus-5-5 --scenarios 40 --seed 7 --brief --out $A/opus-brief
scripts/arena.sh run --models claude-opus-5-5 --scenarios 40 --seed 7 --effort-by-tier elite=high,boss=high --out $A/opus-tier
scripts/arena.sh run --models claude-opus-5-5 --scenarios 40 --seed 7 --forecast --out $A/opus-forecast
scripts/arena.sh run --models claude-opus-5-5 --scenarios 40 --seed 7 --out $A/opus-base2   # the plain batch a second time

scripts/arena.sh compare $A/opus-base $A/opus-brief        # B against A, fight by fight
scripts/arena.sh compare $A/opus-base $A/opus-tier
scripts/arena.sh compare $A/opus-base $A/opus-forecast
scripts/arena.sh compare $A/opus-base $A/opus-base2        # two plain batches: the noise an idea has to beat
```

Or all variants into one folder, and the report by label:

```bash
scripts/arena.sh run --models claude-opus-5-5 --scenarios 40 --seed 7 --out $A/opus-seed7
scripts/arena.sh run --models claude-opus-5-5 --scenarios 40 --seed 7 --brief --out $A/opus-seed7
scripts/arena.sh run --models claude-opus-5-5 --scenarios 40 --seed 7 --forecast --out $A/opus-seed7
scripts/arena.sh report $A/opus-seed7 --by-label
```

Each batch plays its own 40 fights. A batch with another label never skips or replaces a line or a transcript of the others in the same folder, and the same command again plays only what is missing.

### The switches

| Switch | What it changes | In the label |
| --- | --- | --- |
| `--brief` | The first message of every fight ends with the wiki pages of the fight's enemies | `brief` |
| `--effort-by-tier TIER=LEVEL[,TIER=LEVEL...]` | Fights of a named tier are started at their own effort | `elite=high`, one part per tier |
| `--forecast` | Every enemy a snapshot lists with its intents also says what it does after them (`next_moves`), with one note on how to read it (`next_moves_note`) | `forecast` |

**`--brief`** puts the enemies' pages in front of the model instead of hoping it opens them. The user prompt of a fight's first session ends with a section headed `What the wiki says about this fight`. It holds, for each different enemy the fight starts with, the full text of its page under `<checkout>/strategy/wiki/enemies/`, wrapped as the prompt wraps the strategy notes (`<file path="strategy/wiki/enemies/vantom.md">` ... `</file>`), and for an enemy the wiki has no page for the one line `The wiki has no page for <Name>.` Nothing else is in the section, and nothing else of the message changes. A resumed session gets the plain resume prompt: the pages are in its conversation already. The system prompt's appendix gets one more line, which says that those pages are already in the first message and need not be opened again; everything else of the system prompt is as it is without the switch.

- *Which pages.* A page is found by the enemy's name, not by a guessed file name: line 1 of every page is `# <Name>`, and the lookup is built from the files as they are when the fight starts (capitals don't count). The enemies are the ones the fight's first snapshot names in `state.enemies`, each different one once, in the order they stand. An enemy that only joins later (the Wrigglers that come out of a Phrog Parasite) is not among them; the act's own page and the cards' pages are not given either.
- *Where this would come from in the real game.* From the brain. A fight's first snapshot names its enemies, and the brain can read every wiki page (its notes tools do), so it can add the pages to the first snapshot of a fight before the model sees it. The switch gives a model nothing it could not open itself with its first calls.
- *What is recorded.* Each result line has `brief_pages`, `brief_chars` and `brief_missing`, the fight's transcript starts with a line of type `arena` that lists the pages, and the batch says before it starts how many of its fights' enemies have a page and names the ones without (`brief_missing` in `run.json`). `wiki_pages` stays what the model opened itself with `Read`, so the two can be told apart: the report counts the fights in which a model opened a page it had been given.
- *The prompt hash* is the plain run's (see `prompt_hash` in [The result line](#the-result-line)), so a brief batch and a plain one can be checked for having had the same notes.

**`--effort-by-tier`** starts the fights of a tier at their own effort: `--effort-by-tier elite=high,boss=high` plays elite and boss fights at high and the others at `--effort`. The tiers are `weak`, `normal`, `elite` and `boss`, the levels those of `--effort`; a tier named twice, an unknown tier or level is refused with what is wrong. Each result line's `effort` is the effort its fight's sessions were really started with, and the report treats the batch as one player whose fights differ in effort by tier. In the real game this would be the brain setting the effort of its own requests: a fight's first snapshot says what kind of room it is (`run.room`: Monster, Elite or Boss) and the act's first three hallway fights are its weak ones, so the brain knows the tier before the model's first request of the fight. The model is shown nothing it doesn't see anyway. What the arena does not measure is what a change of effort costs inside a running conversation (the in-game brain on the API starts its conversation over when the effort changes): every arena fight is a session of its own.

**`--forecast`** shows the model each enemy's coming moves, not only the one it is about to make. It is a switch of the simulator: the runner parses it, hands it to every fight and every position run (`ArenaGame.Experiments`, a record with the one flag) and names it in the label. With it off nothing of it runs and every snapshot is the fight's own, byte for byte.

```json
"enemies": [
  {"i": 0, "name": "Byrdonis", "hp": 81, "max_hp": 81, "powers": [{"name": "Territorial", "amount": 1}],
   "intents": ["attack 17"],
   "next_moves": ["attack 4x3", "attack 19", "attack 6x3"]}
],
"next_moves_note": "next_moves = this enemy's moves after its shown intent, in order and only as far as they are certain (...)",
```

- *What it gives the model.* `next_moves` is 1 to 3 strings: the enemy's moves after the intent it shows, in order, in the words of `intents`, a move's intents joined by ` + `. Only certain moves are listed. Where the next step is a random pick, the last entry gives its chances (`"50% debuff | 50% attack 11"`, whole percents, the game's order of options) and nothing follows it. A number already holds the Strength the enemy gains by then: from its own moves, from Territorial, from temporary Strength running out. A number with `~` (`"attack ~11"`) is today's number for a hit that something already in play will change: Weak or Shrink on the enemy or Vulnerable on the player runs out first, a coming move puts Vulnerable on the player or may, the player has Colossus. No `next_moves` means unknown, and there is never an empty list. `next_moves_note` is one note on how to read all this. Nothing else of a snapshot changes, and the fight goes the same way.
- *Where the keys are.* Exactly where the mod's reader puts them. The list follows an enemy's `intents` wherever a snapshot lists the enemies: in `combat` and `hand_select` snapshots, and in the small picture of the fight a `card_select` snapshot has under `combat`. The note follows `enemies` in `combat` and `hand_select` snapshots, whenever some enemy has a list; a `card_select` snapshot has the lists and no note. (The keys are not `next` and `next_note`, as they were for a few hours on 2026-10-11: a tool result of `act` and `act_sequence` has a `next` of its own, the snapshot after the action.)
- *What it does not foresee.* Anything that forces a move on an enemy from outside its own cycle, and a death: the Ceremonial Beast's stun at its Plow threshold, an Eye with Teeth coming back from 0 HP. That includes what the player's powers already in play do with no new action (Thorns, Flame Barrier, Inferno, Stampede, Hellraiser). The note says so. Measured over 300 scenarios and three policies with the player only ending turns: 17 of 18,367 forecast moves did not come, each after such a forced move, and all 18,520 numbers without `~` came word for word.
- *Where this would come from in the real game.* From the mod, which has it since commit 76e01a2 (2026-10-11). `src/SpireMind/Game/Forecast.cs` works the same list out from the game's live objects, reading only: the move that shows, each move's follow-up, a random branch's options with their weights, repeat rules and cooldowns, the move log, the powers. `CombatReader.EnemiesJson` puts it into the snapshot and `CombatReader.BoardState` the note. `docs/forecast.md` describes it. The arithmetic here is the mod file's, line for line, the note is its sentence, word for word, and the keys and their places are the reader's; `selfcheck` reads those source files and fails when anything differs, also in the lists of powers that mark a number. One thing is still open: nothing of the mod's forecast has run in a live fight. So this switch measures what the forecast is worth to a model before the bot has played with it.
- *Where the arena differs from the mod.* The mod's reader lists an enemy that is down and coming back (`"reviving": true`) and gives it a list; the arena's snapshot leaves a 0 HP Eye with Teeth out for that turn, with the switch or without.
- *What holds it to its word.* `honesty` works every enemy's forecast out before every `end_turn`, switch or no switch, and holds each entry against what the enemy then does, once on a copy in which the player only ends turns and once as played. `selfcheck` puts every power the simulator has on the player and on an enemy and fails when a number the forecast printed plain comes out different. `src/SpireMind.Arena/Sim/README.md`, section 14, has the rules and the numbers.

`snapshot SCENARIO-ID --forecast` prints a scenario's first snapshot with the switch on, and `position ... --check --forecast` a position's.

The suggested-line helper that an earlier draft had as a fourth switch (`--advisor`) was ruled out by the review of Opus's play (`docs/opus-review.md`, "Do not do") and is not in the arena.

### The label

A batch's label is built from the switches that are on, joined by `+`, always in the same order: `brief`, `forecast`, then one `tier=level` per tier that `--effort-by-tier` names, in the tiers' own order. A position test can put a name of its own in front (`position --label old`: `old`, `new+forecast`). `--brief --effort-by-tier boss=high,elite=high` is `brief+elite=high+boss=high` however it was typed, and a batch with no switch is `base`. The label is in every result line (`experiment`), in `run.json`, in the batch's first lines and in the report's title. It names what was asked for: `--effort high --effort-by-tier elite=high` is `elite=high`, though every elite fight would have been played at high anyway.

What makes two result lines the same fight is the scenario, the model, the effort the fight was played at, and the label. That is the rule a batch skips by, so:

- the same command again plays only what is missing, as ever;
- a batch with other switches, or at another `--effort`, plays all of its own fights into the same folder, and neither skips nor replaces the other's lines or transcripts;
- under `--effort-by-tier elite=high`, an elite fight is the same fight whatever `--effort` the batch has, so a second batch that differs only in `--effort` plays the other tiers and takes the elite fights as played. The report counts such a fight for both.

### Comparing

Two variants are compared on the fights both played to the end, fight by fight, which cancels how hard each fight is. `report DIR --by-label` does it for every two rows of a model inside one folder, the later-listed against the earlier (`base` is listed first, so everything is compared with it first). `compare DIR_A DIR_B` does it across two folders, B against A. Both print the same table:

```
pair                     tier    fights       won  better  same  worse  HP left difference  sign test p
claude-opus-5-5: B vs A  all         40  36 vs 34      15    17      8             +1.0 HP         0.21
                         weak         8    8 vs 8       2     4      2             -1.0 HP         1.00
                         normal      16  16 vs 16       8     6      2             +2.0 HP         0.11
                         elite        8    7 vs 6       2     5      1             +0.5 HP         1.00
                         boss         8    5 vs 4       3     2      3             +1.3 HP         1.00
```

| Column | Meaning |
| --- | --- |
| pair | The first against the second: `B vs A` in `compare`, `claude-opus-5-5 [brief] vs claude-opus-5-5 [base]` by label |
| tier | `all`, then one row per tier, which is the wins by tier |
| fights | Scenarios both sides played to the end (won or lost) |
| won | The first's wins against the second's on those fights |
| better, same, worse | In how many of them the first had more HP left, the same, less. A fight that wasn't won is 0 HP |
| HP left difference | The mean of first minus second: positive means the first kept more HP |
| sign test p | The two-sided sign test on better against worse, ties left out: how likely a split at least that lopsided is when neither is better |

A pair is dropped when either side didn't play its fight to the end (abandoned, a timeout, an error), and the line under the table counts those; `compare` also counts the scenarios only one folder has. Under its table `compare` lists every scenario whose outcome flipped, with the fight's enemies:

```
4 scenarios' outcomes flipped (won in one run, lost in the other; W and the HP left, L for a loss):
  boss-878005  boss  claude-opus-5-5  A W 11 -> B L  vs Kin Follower + Kin Follower + Kin Priest (TheKinBoss)
```

Both say when the comparison is not of the switch alone: when the two sides were played with different system prompts (`prompt_hash`), when they read different wikis (`wiki_hash`), and, in `compare`, when A and B are the same variant, which shows how much the same fights differ from one run to the next.

### An honest experiment

- **Only what the real mod could give.** A switch may put in front of the model only information the mod has in the real game at that moment. `--brief` gives pages the model could open itself; a switch that showed the order of the draw pile, or which move an enemy's random pick will land on, would measure something the bot can never have. Each switch says in its code where its information would come from in the real game.
- **One switch at a time, against the same fights.** Use the same `--scenarios` and `--seed`, the same model and, unless effort is the idea, the same `--effort`. Switches can be combined, but then only the combination was measured.
- **The same notes and the same wiki.** Real runs add lessons to `strategy/learned.md` and to the wiki, and a batch reads both. Run the batches of one comparison from one frozen copy of the checkout (`--repo`), or check that their `prompt_hash` and `wiki_hash` agree; the comparison says so when they don't.
- **Know the noise first.** A model doesn't play a fight the same way twice. Two plain batches on the same fights, compared with `compare`, show how many fights flip and how far HP moves with nothing changed. An idea has to beat that, and forty fights seldom show a difference of a point or two of HP: read the sign test's p and the wins by tier, not the mean alone.
- **What costs more should win more.** `--brief` adds a few thousand characters to every fight's first message and may save the `Read` calls; a higher effort on elites and bosses costs time and usage. The per-model table (`min/fight`, `$/fight`, `out tokens`) is by label under `--by-label`, so the price stands next to the gain.

## Position tests

A position test asks what a model does at one decision. A whole fight cannot ask that twice: the decision only comes up again if everything before it goes the same way. So the arena takes a fight that was recorded, rebuilds it up to the start of one of its turns, and hands that turn to new sessions. Each session gets the play prompt and tools of today, sees the snapshot of that turn's start as its first snapshot, and plays the fight to the end. `docs/opus-review.md` names the positions this was built for (section 9) and what each should show (section 7).

```bash
P=~/.config/spiremind/arena/pilot-seed7
scripts/arena.sh position $P boss-878005 claude-opus-5-5 --turn 4 --check      # rebuild and check only; costs nothing
scripts/arena.sh position $P boss-878005 claude-opus-5-5 --turn 4 --label new --out ~/.config/spiremind/arena/positions-new
scripts/arena.sh position $P boss-878005 claude-opus-5-5 --turn 4 --label new --out ~/.config/spiremind/arena/positions-new-r --reshuffle
scripts/arena.sh report ~/.config/spiremind/arena/positions-new
```

The three names are the run the recording is in (its folder or `results.jsonl`), the scenario, and the model whose fight is rebuilt. `--turn` is the player turn, as the snapshots count it (`state.turn`, from 1). `--models` names who plays the position (default: the recorded model), `--runs` how often each (default 5, at most 50).

### How a position is rebuilt and checked

1. **The recording.** `RESULTS_DIR/transcripts/<scenario>__<model>.jsonl`, the transcript of a whole fight played without a switch, and that fight's line of `results.jsonl`. Every tool call is read with its result. What ran is taken from the result, never from what the model sent: `done` is one id for `act` and a list of ids with labels for `act_sequence` (which finds a later card again by its label, so the ids that ran are not always the ids sent); a refused call ran nothing.
2. **The rebuild.** The scenario's fight is built from its id. The recorded actions are applied in the order they ran, up to the first action the recorded session ran on turn N.
3. **The check.** After every tool result that carries a snapshot (`act`, `act_sequence`, `wait_for_decision`, `get_state`, refused calls too), the rebuilt fight's snapshot is compared with the recorded one, whole: `screen`, `awaiting_decision`, `state` and `actions`, value by value. A snapshot taken while the game was busy holds turn, round, HP and Block, and those are compared. Inside an `act_sequence` the recording has no snapshot between actions; there every action must be on offer and carry the recorded label (card and target).
4. **The position must have been seen.** The recording has to hold at least one full snapshot of the start of turn N, and the rebuilt fight must equal it. A turn the recorded fight never reached, or one whose start the recording holds no snapshot of, is no position: the command says so and exits with 2.
5. **A difference stops everything.** The command prints `THE REBUILT FIGHT DOES NOT MATCH THE RECORDING. Nothing was started.`, the transcript line, each differing value (`state.enemies[0].hp: recorded 43, rebuilt 42`) and both sides' HP, hand and enemies, and exits with 1. No session is started and no folder is made.

Two differences are let through, counted and printed as notes. Both are changes to what a snapshot says, made after the pilot was recorded, not to the fight:

- **The player's name.** The pilot's snapshots name the player `Ironclad`; snapshots now say `The Ironclad`, as the game does. Only that pair, and only under a `me.name` key.
- **The small picture of the fight on a card choice screen** (`card_select`, `state.combat`). Its layout changed: hand cards became objects with their text, the enemies' entries gained their index, two counts were added. There a key that only one side has, and two values of different kinds, are a difference of layout. Two numbers or texts that differ, and lists of different lengths, are a mismatch there as anywhere. The choice itself (its cards, prompt and actions) and every combat snapshot around it are compared whole.

Checked on what is on disk (2026-10-11): all 200 recordings of `pilot-seed7` (120), `opus-high-seed7` (40) and `opus-wiki-seed7` (40) rebuild to the start of their last turn with no other difference.

What the command prints before it spends anything (`--check` stops after this and the position's first snapshot):

```
Position: boss-170286 T4, the start of turn 4 of claude-opus-5-5's fight in /Users/mark/.config/spiremind/arena/pilot-seed7 (lost).
  floor 17 boss: 62/80 HP, 15 cards (added Pommel Strike, Cinder, Thunderclap, Rupture, Stone Armor), potions Radiant Tincture, Power Potion, vs Vantom (VantomBoss)
Rebuilt from the scenario by applying 18 recorded actions. 7 recorded snapshots compared with the rebuilt fight, all equal (1 of the position itself); 16 action labels compared, all equal.
At the position: combat, turn 4, HP 38, Block 0, energy 4, hand [Strike, Defend, Defend, Cinder, Pommel Strike], enemies: Vantom 156/173 [buff]. State hash 941e6a189b22.
The recorded session then played: Pommel Strike on Vantom, Bash on Vantom, Strike on Vantom.
```

The state hash is 12 hex digits of the SHA-256 of the position's first snapshot. Scenario, turn and state hash together are the position: the same turn of another recording of the scenario is another state, and its runs are never pooled or paired with these.

### How a run is played

Each run is a new session on a new game. A new fight of the scenario gets the recorded actions applied again and must show the position's snapshot exactly; only then is it put behind a bridge, so a session sees nothing of the way there. From there a run is a fight like any other ([How one fight is run](#how-one-fight-is-run)): the same system prompt, tools, flags, resumes, time limit and stops. Three things differ:

- **The first message** is the usual one with one sentence added to its first paragraph: `This fight is already under way: you take it over at the start of turn 4, and the turns before it cannot be changed. Your first snapshot shows where the fight stands.` Nothing about the earlier turns is told. The system prompt is unchanged, so `prompt_hash` means what it means for a whole fight.
- **The turn's line is kept.** Every action the game accepted while the fight stood on turn N goes into the result line by its label (`Play Defend`, `Play Cinder on Vantom`, `Use Skill Potion`, `Take Flame Barrier`, `End turn`) and by its id, in the order it ran, whether the session played card by card or with one `act_sequence`.
- **The result line** has the object `position` ([The result line](#the-result-line)), and the transcript is saved as `<scenario>__<model>__turn<N>-recorded<k>.jsonl` (`-reshuffled<k>` under `--reshuffle`, and `__<label>` behind it for a label other than `base`). Its first line is of type `arena` and says which position and deal the run had.

`turns`, `cards_played` and `damage_taken` in the line count from the fight's first turn, the recorded part included. A run in which no session made one call to the fight's tools wasn't played and is an error, as for a whole fight.

Run the same command with the same `--out` and it plays only what is missing: a run is the same run when the position, the deal (recorded or reshuffled), the model, the effort, the label and the run's number are the same. A position run never stands in for the scenario's whole fight, nor the other way round: `run` and `position` can share a folder.

### The recorded deal, or a reshuffle

A fight's random streams are fixed by its scenario's seed. Without more, every run of a position draws the same cards in the same order and meets the same enemy rolls as the recorded fight.

- **Recorded deal** (the default). The runs differ only in what the model does. This is the mode for the question "which line does it take here": five runs show five choices on the very deal the recorded session had. The outcome says less: it is one deal, and a line can win or lose on it by the draw. With the fake CLI, a player that plays the same way as the recorded one ends every run exactly as the recorded fight ended, card for card.
- **`--reshuffle`.** Each run gets its own deal of what the player cannot know: the order of the draw pile (a card whose place the player knows stays put) and every random number still to come (enemy rolls, random targets, later shuffles). It is the simulator's own copy for players that search (`Fight.CloneDeterminized`). What the player sees does not change: the hand, the piles' contents, HP, powers and the intents that show, so the first snapshot is the same to the letter, and that is checked for every run before anything starts. The deal's seed follows from the scenario, the turn and the run's number alone. Run 3 is therefore the same deal for every model, prompt and label, and two batches can be compared deal by deal.

The report and `compare` say which deal the runs had (`deal` column, and one line that starts `Deal: recorded.` or `Deal: reshuffled.`). The review's "points of win chance" were measured on reshuffled copies with the scripted Lookahead player finishing the fight; here the model itself finishes it.

### The eight positions of the review

As `--check` printed them on 2026-10-11 from Opus's pilot fights (`pilot-seed7`, medium effort):

| Position | HP there | State hash | What the recorded session played on that turn | What it is for (review section 7) |
| --- | --- | --- | --- | --- |
| boss-878005 T4 | 15 | `bd5d552b633a` | Bash, Strike and Spite, all on the Kin Priest | Item 1: count two turns before an all-in |
| boss-548112 T3 | 70 | `cb2bfc263650` | Skill Potion, took Evil Eye, Bash, Molten Fist, Evil Eye | Item 2: the card taken inside a fight |
| boss-170286 T4 | 38 | `941e6a189b22` | Pommel Strike, Bash, Strike (Cinder left in hand with two Defends) | Item 3: random exhaust |
| boss-541818 T5 | 23 | `9a5fcb08bac2` | Anger, Strike, Cinder | Item 3: random exhaust |
| boss-504297 T2 | 69 | `eae6d6b094b1` | Skill Potion, took Flame Barrier, Flame Barrier, Twin Strike, Strike, True Grit | Item 4: when to open a card potion |
| normal-273726 T1 | 58 | `7928902ad12d` | Iron Wave, Strike, Sword Boomerang (Flex Potion kept) | Item 4: Flex Potion on the turn with the most hits |
| elite-780979 T2 | 49 | `5efc402034ff` | Breakthrough, Snecko Oil, Strike, Strike, Stomp+ | Item 4: Snecko Oil with energy left |
| boss-243639 T3 | 47 | `179c16893ddd` | Shackling Potion, Tremble, Infernal Blade, Perfected Strike, Stone Armor | Item 4: Shackling on the biggest of the next turns |

(The review's own list has boss-541818 on turn 1, its Barricade; turn 5 is its Cinder turn. `--turn 1` works like any other turn.)

Today's prompt, five runs each on the recorded deal (40 sessions):

```bash
P=~/.config/spiremind/arena/pilot-seed7
NEW=~/.config/spiremind/arena/positions-new
scripts/arena.sh position $P boss-878005   claude-opus-5-5 --turn 4 --label new --out $NEW
scripts/arena.sh position $P boss-548112   claude-opus-5-5 --turn 3 --label new --out $NEW
scripts/arena.sh position $P boss-170286   claude-opus-5-5 --turn 4 --label new --out $NEW
scripts/arena.sh position $P boss-541818   claude-opus-5-5 --turn 5 --label new --out $NEW
scripts/arena.sh position $P boss-504297   claude-opus-5-5 --turn 2 --label new --out $NEW
scripts/arena.sh position $P normal-273726 claude-opus-5-5 --turn 1 --label new --out $NEW
scripts/arena.sh position $P elite-780979  claude-opus-5-5 --turn 2 --label new --out $NEW
scripts/arena.sh position $P boss-243639   claude-opus-5-5 --turn 3 --label new --out $NEW
scripts/arena.sh report $NEW
```

All eight at once, checked first without spending anything, and the same under the pilot's prompt:

```bash
P=~/.config/spiremind/arena/pilot-seed7
POSITIONS="boss-878005:4 boss-548112:3 boss-170286:4 boss-541818:5 boss-504297:2 normal-273726:1 elite-780979:2 boss-243639:3"
# the frozen copy of the checkout the pilot was played from
OLD_REPO=/private/tmp/claude-501/-Users-mark-Projects-STS2B/468d2c16-6f8a-4b84-8206-5f7877671d52/scratchpad/arena-pilot

for at in $(echo $POSITIONS); do
  scripts/arena.sh position $P ${at%:*} claude-opus-5-5 --turn ${at#*:} --check | head -5
done

for at in $(echo $POSITIONS); do
  scripts/arena.sh position $P ${at%:*} claude-opus-5-5 --turn ${at#*:} --label old --repo $OLD_REPO --out ~/.config/spiremind/arena/positions-old
done
scripts/arena.sh compare ~/.config/spiremind/arena/positions-old ~/.config/spiremind/arena/positions-new
```

(`${at%:*}` is the scenario and `${at#*:}` the turn; the loops run in zsh and in bash.) Add `--reshuffle` to both arms, with other `--out` folders, for the same positions over five deals each. Add `--effort high` for the review's item 5.

A position run takes the switches of a whole fight, `--brief` and `--forecast`, and its label names them behind the batch's own name (`new+forecast`). A switch does not change the position: the state hash is of the snapshot without the switch's keys, so runs with a switch pair with runs without one. The same eight positions with the enemies' coming moves shown, against the plain new prompt:

```bash
for at in $(echo $POSITIONS); do
  scripts/arena.sh position $P ${at%:*} claude-opus-5-5 --turn ${at#*:} --label new --forecast --out ~/.config/spiremind/arena/positions-new-forecast
done
scripts/arena.sh compare ~/.config/spiremind/arena/positions-new ~/.config/spiremind/arena/positions-new-forecast
```

### Old prompt against new

`--repo DIR` picks the checkout the play prompt, the strategy notes and the wiki index are read from, and whose wiki the sessions may open ([run](#run)). A position played from a frozen copy of an earlier checkout is the position under that checkout's notes; everything else is today's: the arena's appendix to the prompt, the tools, the snapshots, the simulator. So the old side's `prompt_hash` is the hash of the old notes under today's build, not the hash the pilot's own result lines carry (`aac717a13026`, from the pilot's build). The frozen pilot checkout gives `29dff10cfc7b` today. Every position run records its `prompt_hash`, the batch prints it before it starts, and the report and `compare` print it per label and per side.

`compare DIR_A DIR_B` pairs position runs by position (scenario, turn and state hash), for every model both folders hold or only for `--model M`. Each folder must hold a model's position runs in one variant (one label, effort and deal); two labels in one folder are compared by that folder's `report`. This is the output for three positions, three runs each, played with the fake CLI under two checkouts whose notes differ in one line (`fake-notes`, reshuffled):

```
Arena compare, position runs: B against A, position by position (scenario and turn). A positive difference means B did better.
  A: ~/arena/old-r/results.jsonl
     fake-notes: experiment old, effort medium, deal reshuffled, 9 runs that count on 3 positions, system prompt 93f5015229a4
  B: ~/arena/new-r/results.jsonl
     fake-notes: experiment new, effort medium, deal reshuffled, 9 runs that count on 3 positions, system prompt 2d3b9cc7c5ad
The two sides were played under different system prompts (A 93f5015229a4; B 2d3b9cc7c5ad): the play prompt, the strategy notes or the wiki index differ.

fake-notes: B against A.
  position          A: won  HP left  B: won  HP left    win share  HP left difference
  boss-878005 T3       0/3      0.0     0/3      0.0     0 points              0.0 HP
  elite-803170 T2      3/3     15.7     2/3     11.0   -33 points             -4.7 HP
  normal-280469 T2     3/3     31.7     0/3      0.0  -100 points            -31.7 HP
  all 3 positions      6/9     15.8     2/9      3.7   -44 points            -12.1 HP
  By mean HP left, B was ahead in 0 of 3 positions, level in 1, behind in 2 (sign test p 0.50). A few runs of a position say little: read the lines taken, not only the means.
  Deal by deal (both sides were reshuffled, so run k of a position had the same deal on both): on 9 deals both played to the end, B had more HP left in 2, the same in 3, less in 4 (sign test p 0.69); it won 2, A 6.
  Lines taken on the position's turn (runs that took the line, how many of them won, their mean HP left):
    boss-878005 T3
      A  3 runs: Bash on Kin Follower (e0), Strike on Kin Follower (e0)  ->  0 won, HP left 0.0
      B  3 runs: Barricade  ->  0 won, HP left 0.0
```

| Part | Meaning |
| --- | --- |
| A, B | Each side's variant, its runs that count, and the hash of the system prompt it was played under |
| won, HP left | Per side: wins of the runs that count (won, lost or abandoned), and their mean HP left, 0 for a run that wasn't won |
| win share | B's share of runs won minus A's, in points of 100 |
| HP left difference | B's mean HP left minus A's |
| all N positions | Over the positions both sides played: the wins added up, the mean of the positions' mean HP, and the means of the two differences. Each position counts the same |
| By mean HP left | In how many positions B's mean HP left was higher, equal, lower, with the two-sided sign test |
| Deal by deal | Only when both sides were reshuffled: run k of a position had the same deal on both, so the runs pair one to one. In how many deals B had more HP left, the same, less; the sign test; and each side's wins on those deals |
| Lines taken | Per position and side, each different line of the position's turn, most often taken first |

It says so when both sides were played under the same prompt, when one side was reshuffled and the other not (then they did not face the same draws), and when a position of the same scenario and turn has another state on the other side (rebuilt from another recording, or by a build that shows the fight differently): that one is named and left out.

### The position report

`report` prints position runs in a part of their own, after the fights if the file holds any, and `position` prints it when its batch ends:

- **The header**: runs that count, positions, models; then which deal the runs had, and the system prompt's hash for each label (`System prompt by label (its hash): old 29dff10cfc7b; new ...`). A label with two hashes was played under two prompts and is said to be.
- **Per position and label**: one row per model, label, effort and deal, with `runs` (won, lost or abandoned), `won`, the win rate with its 95% interval, the mean `HP left` (0 for a run that wasn't won), the mean last turn of the runs played to the end, minutes and API-price dollars a run, and columns for abandoned runs, timeouts and errors when there are any. Labels stand in the order they first appear in the file, `base` first.
- **Lines taken on the position's turn**: for each position its HP at the start and what the recorded session played there, then per row each different line with the runs that took it, how many of them won and their mean HP left (`4 runs: Defend, Defend, Cinder on Vantom  ->  3 won, HP left 9.5`). "Play" is left off each card and the closing "End turn" off the line. A run whose session stopped before the turn was ended is listed under what it had played, marked `(the turn wasn't ended)`.
- **Row against row**: where a model has two rows on the same kind of deal (two labels, two efforts), the later against the earlier, with the table and the lines `compare` prints. A reshuffled row is never set against a recorded one: that would compare the deals.

Each run is counted once, by its number: the last line that counts, else the last line there is, so a run played again after a timeout or an error replaces it.

### Reading a position test honestly

- **The line is the measure, the outcome is noise around it.** Five runs that all take the same line say something about the decision. Four wins against one say little about the line's worth: the 95% intervals are 38-96% and 4-62%, and everything the session does on the later turns is in the outcome too.
- **The recorded deal is one deal.** On it a line wins or loses by the one draw that follows. Use the recorded deal to see which line is taken and `--reshuffle` to see what a line is worth, and read the deal-by-deal count before the means.
- **A new session starts cold.** It has the snapshot, the turn number and the sentence that it took the fight over. It has no plan from the turns before and has not seen what the enemy did on them, where the recorded session had both in its conversation. So a position test measures the decision from the snapshot and the prompt alone, for the old prompt and the new one alike.
- **Old against new needs the same everything else.** The same recording, turn, model, effort, deal and number of runs on both sides, and the two checkouts differing only in what is being tried. `compare` prints both prompt hashes and says when the deals differ.
- **A pilot recording reads a little differently.** The recorded session read `Ironclad` where a new session reads `The Ironclad`, and an older layout of the card choice screen's small picture. The position's notes say which of the two a recording has.
- **It scores nothing by itself.** The review rated lines with the replay tool's search (its "turnscan" and "reroll", in the scratchpad's analysis copy). The arena does not hold that tool: a position test shows which line a model takes and how its fights then end, and no more.

## Two ways to drive a fight

`run` and `position` take `--brain claude-code` (the default) or `--brain api`. Both play the same fights in the same simulator through the same `BotTools`. They differ in the program that talks to the model, and in who pays.

**Where this stands.** `--brain api` is built and checked against a scripted stand-in for the API (`selfcheck`, `--fake-api`). Mark's rule of 2026-10-11 (`docs/opus-review.md`) is that tests run on the subscription and the API credit is for real play, so this mode runs only when he says so, each time, with a dollar figure. It has run against the real API once, on his word of 2026-10-12: the eight recorded positions, five runs each, at the game's settings and one level higher, 81 runs for $44.47 by the brain's own count ([What each costs](#what-each-costs) has the figures; the results are on the Mac in `~/.config/spiremind/arena/positions-api-default` and `positions-api-up1`). It cannot spend by accident: it starts nothing without `--yes-spend`.

| | `--brain claude-code` | `--brain api` |
| --- | --- | --- |
| What plays | Claude Code headless, one session per fight, with the fight's bridge as its MCP server (`ClaudeFight`) | The mod's own brain, `SpireMind.Core`'s `ApiBrain`: the program that plays the real game, unchanged, calling the Messages API itself (`ApiFight`) |
| Billed to | The Claude subscription: it fills the 5 hour and 7 day usage windows, and nothing is charged | The API credit, in dollars, request by request |
| System prompt | Claude Code's own system prompt, and after it the play prompt, the strategy notes, the wiki index and the arena's appendix | What the game sends and nothing else: `PromptBuilder`'s text with the brain's own appendix, byte for byte |
| What the model is told about the arena | The arena's appendix and the user prompt: a practice fight, nothing can be written, how it ends | One line of the mod in the chat, which the brain shows under "Recent chat" in its first message (below) |
| Thinking | Whatever Claude Code makes of `--effort` | The brain's own request: `output_config.effort`, adaptive thinking with summaries, `max_tokens` by effort (16000, 32000, 64000) |
| Effort by room | One effort per fight, chosen by the runner (`--effort`, `--effort-by-tier`) | The brain's own switch: the first snapshot's `run.room` picks `elite_effort` or `boss_effort`, high by default |
| The wiki | `Read`, on the checkout's strategy folder | The brain's `notes_read` and `notes_list`, on a throwaway copy of the notes |
| Writing | Nothing can be written, and the prompt says so | The notes tools work as in the game, into the throwaway copy; what was written is in the result line |
| A model that stops mid-fight | The session is started again with `--resume`, at most 5 times | The brain answers "Keep playing" in the same conversation, as in the game, and gives up after 5 in a row |
| The end | 20 s of grace, then SIGINT | The brain is stopped before its next request once the fight is over and `--after-fight` requests have gone out (default 1) |
| What limits a batch | `--stop-at-weekly`, and the usage limit | `--max-usd` |
| Fights at once | `--parallel`, default 3 | `--parallel`, default 1 |

### The API brain's commands

```bash
A=~/.config/spiremind/arena
scripts/arena.sh run --brain api --scenarios 8 --seed 7 --out $A/opus-api                 # says what it would play and cost, and stops: no --yes-spend
scripts/arena.sh run --brain api --yes-spend --scenarios 8 --seed 7 --out $A/opus-api     # plays, on a budget of $5
scripts/arena.sh run --brain api --yes-spend --only elite-803170 --max-usd 1.50 --out $A/opus-api-one   # one elite fight by itself, about a dollar
scripts/arena.sh run --brain api --yes-spend --scenarios 40 --seed 7 --max-usd 30 --out $A/opus-api
scripts/arena.sh run --brain api --yes-spend --model sonnet --effort low --elite-effort same --boss-effort high --scenarios 8 --seed 7 --out $A/sonnet-api
scripts/arena.sh position $A/pilot-seed7 boss-878005 claude-opus-5-5 --turn 4 --brain api --yes-spend --label new --out $A/positions-api
scripts/arena.sh compare $A/opus-tier $A/opus-api     # Claude Code at elite=high,boss=high against the mod's own brain, fight by fight
```

`run --brain api [--yes-spend] [--models a,b | --model M] [--scenarios N=8] [--seed S=1] [--only ID,ID] [--max-usd D=5] [--effort LEVEL] [--elite-effort LEVEL|same|default] [--boss-effort LEVEL|same|default] [--after-fight N=1] [--parallel K=1] [--timeout-minutes 30] [--out DIR] [--repo DIR] [--forecast] [--fake-api]`

- **Before anything is spent** the batch prints how many fights it plays at most, the model, the efforts and `max_tokens`, where the settings came from, the budget, the prompt's size and hash and where the results go. Without `--yes-spend` it stops there with exit code 2 and has read no key.
- **The settings are the game's.** `config.json` in the game's config folder (`~/.config/spiremind`, or what `SPIREMIND_CONFIG_DIR` names) is read by the game's own code (`BrainSettings`), and with no file the mod's defaults hold: `claude-opus-5-5`, effort medium, elite and boss rooms high. `--model` (or `--models`, each model playing every fight) names another model, by a full id or by `opus`, `sonnet`, `haiku` or `fable`. `--effort` is the normal effort, `--elite-effort` and `--boss-effort` the rooms' own: a level, `same` for the normal effort, or `default` for high-or-the-normal-effort-when-higher. `max_tokens` in `config.json` is kept; there is no option for it.
- **`--only ID,ID`** plays just the named fights, in the place of the first N of a seed: the ids are the ones the reports and `baseline` print (`elite-803170`). It is the way to play, and pay for, one fight of one kind. The fight is the same fight a batch of its seed holds, so that batch skips it later and `compare` pairs it.
- **The budget** (`--max-usd`, dollars, default 5, at most 200) **is the results folder's, not one command's.** Every answered request is written as one line to `api-spend.jsonl` beside the results (`at`, `usd`, `api`: `real` or `stand-in`, `fight`, `model`; never a key or any text), and a run starts from what that file adds up to. So the same command again with the same `--out` gets no new budget: once the folder's fights have cost `--max-usd`, nothing starts until a higher `--max-usd` is typed, and a new `--out` is a new budget. What a batch spent is on disk even when it was interrupted or it crashed, to within the request that was in flight. Stand-in requests and real ones never count against each other.
- **How the budget holds.** No fight starts once the folder's fights have cost that much; nor when what is left is less than a fight's first request costs (the system prompt written to the cache: about $0.18 on Opus 5.5, worked out from the prompt's size); nor when it is less than the dearest fight so far, earlier runs' fights included (times the fights that would then be running). Each fight's brain gets what is left as its own cap, in the place of the game's monthly cap, so the brain's own check before every request stops it there. And no request leaves once the sum is reached. **The request that reaches the budget is the last one sent, so a batch can end above `--max-usd` by one request for each fight that was running**: usually a few cents, at the very most a reply that uses all of its `max_tokens` ($0.64 at high on Opus 5.5). A fight stopped by the budget is an `error`, counted for nothing and played again by the next run that has the budget for it; the batch then ends with exit code 1.
- **The costs are the brain's own count**: each response's `usage` at the repo's price table (`ModelPrices`), which is what the game's `/budget` shows too. The Claude Console has what was really billed. Two things can be billed without being counted, here as in the game: a request cut off in flight (Ctrl+C, the fight's time limit), and a try the client gave up on after its deadline and sent again.
- **Mark's spend record is not touched.** Each fight has a spend tracker of its own in its temp folder. `~/.config/spiremind/spend.json` is neither read nor written, so a batch never counts against the game's month and what the game spent this month never stops a batch. Of the game's monthly cap only the setting is honoured: with `monthly_budget_usd` at 0 in `config.json` (never use the API) nothing starts, and `--max-usd` may not be above it. So the arena's spend and the game's are two separate sums on the same key; the Console's own spend limit is the one limit over both.
- **The key** is read when the first fight is about to start, by the game's own code, from the place the game reads it: `api_key` in the config folder, else `ANTHROPIC_API_KEY`. It goes from Core's loader straight into Core's API client. The arena never prints it, writes it or keeps it, and the settings a batch plans with are read without opening the key file. With no key the batch says where the game looks for one and ends with exit code 1, before any fight.
- **Carrying on** works as ever, on the folder's budget: the same command with the same `--out` plays only what is missing, and an `error` or a `timeout` is played again, as far as what is left of `--max-usd` goes. To go on after the budget stopped a batch, give a higher `--max-usd`.
- **The label** of an API batch starts with `api`, followed by the rooms' efforts where they differ from the normal one, in the form `--effort-by-tier` uses: `api+elite=high+boss=high` with the default settings. So an API fight is never skipped for, pooled with or mistaken for Claude Code's fight on the same scenario, both can share a folder, and `compare` against a Claude Code batch run with `--effort-by-tier elite=high,boss=high` pairs fights that were played at the same efforts. `report` says how many fights the API brain played and what they cost, and `compare` says when its two sides were not driven the same way.
- **Stops.** Ctrl+C stops the brains, keeps what finished and exits with 130. A key the API refuses, credit that has run out and the Console's spend limit stop the batch after the fight that met them (exit code 1); a fight that fails for another reason doesn't.

### How the API brain plays one fight

`ApiFight` wires the brain the way the game does (`src/SpireMind/Game/BrainWiring.cs`):

1. A new `ArenaGame` for the scenario, with `SnapshotVersioner`, `DecisionWaiter`, `ChatHub` and `BotTools`, as for Claude Code. There is no bridge and no port: the brain calls `BotTools` in-process through `BotToolsHost`, as it does in the game.
2. A temp folder for the fight, with a throwaway copy of the notes in it: `strategy/learned.md` and everything under `strategy/wiki/`, from the checkout (`--repo`, else `repo` in `config.json`, else the checkout the arena was built in). Not copied: `runs/`, since the journal of the real run says nothing about this fight, and `strategy/pending/`, which no brain ever reads.
3. `new ApiBrain(...)` with the arguments the game gives it: the API client, the tools, `NotesTools` on the copy, the checkout as the prompt folder, the settings, a `SpendTracker`, the character (`Ironclad`), single player, a `Transcript`, the chat's recent lines, and a `FightLog`. The tracker, the transcript, the fight log and the notes lock live in the temp folder.
4. `brain.Start()`. From here the brain does what it does in the game after `/start` in the middle of a fight. It reads the state, builds its system prompt with `PromptBuilder` (play prompt, strategy notes, wiki index, its own appendix), opens a conversation with "You have control of the game now.", today's date, the journal (empty), the recent chat, and the snapshot, and plays.
5. **Effort.** The first snapshot names the room (`state.run.room`: Monster, Elite or Boss), so the brain's own code gives the conversation the room's effort from its first request (`BrainSettings.EffortFor`) and sizes `max_tokens` by it. The arena sets no effort anywhere.
6. **The arena's facts** reach the model as one line the mod says in the chat before the brain starts. The brain puts the chat's last lines under `## Recent chat` in the first message of every conversation, the mod's own as `(mod) ...`, so this is the way a line the mod announces reaches the model in the real game. The line, whole:

   > SpireMind arena: this is one practice fight in a simulator of the game's combat (v0.107.1), with the game's rules, snapshots and tools. There is no map, reward or run around it: win it, with as much HP left as you can. Card, power and potion texts are worded by the arena, so go by the snapshot where it and your memory differ. The fight counts as lost if it isn't won by the end of turn 40. When it is over the screen is `arena_over` and nothing follows.

   A position run adds: `This fight is already under way: you take it over at the start of turn N, and the turns before it cannot be changed.` Nothing else is added anywhere, and the system prompt is not touched.
7. **A model that ends its turn mid-fight** is carried on by the brain itself, as in the game: "Keep playing: call wait_for_decision." in the same conversation, and after more than 5 ended turns in a row the brain stops. The arena restarts nothing. The result's `resumes` counts those messages, and a fight the brain gave up on that way is `abandoned`.
8. **The end.** A pass-through recorder around the API client (`ApiRecorder`) sees every request. Once the game says the fight is over, it lets `--after-fight` more requests out (default 1, so the model sees the end screen once and can write its journal line and its lesson, as it would at the rewards screen) and stops the brain before the next one: the brain's own `Abort`, with the request cancelled before it is sent. The brain ends with its ordinary stop, and nothing more is billed. `--after-fight 0` stops it before it sees the end. The fight's time limit (`--timeout-minutes`) stops the brain the same way; the fight is then a `timeout`.
9. **Afterwards** the arena compares the copy of the notes with what it copied (every new or changed file goes into the result as `api.notes_written`, with its text), closes the brain's fight log to get its fight line, copies the brain's own transcript to `transcripts/<scenario>__<model>__<label>.jsonl` (requests without the system text, responses with thinking summaries, usage and cost, under one line of type `arena`), and deletes the temp folder.

### What still differs from the bot in the real game

What the model is sent is the game's, with these differences:

- **Every fight starts like `/start` in the middle of a fight.** The first message opens with "You have control of the game now." In the game an elite or boss fight usually opens with the brain's fresh conversation for the room's effort ("The thinking effort changed with the room ..."), which holds the same parts, and a hallway fight is played inside the conversation that already holds the floors before it. Here every fight is a conversation of its own with nothing before it.
- **No journal.** `runs/current.md` is empty when the fight starts. In the game it holds the run's Build plan, Orders and floor log, and the first message carries it.
- **The recent chat is the arena's one line.** In the game it is the last ten lines of the real chat.
- **The model is told there is no run around the fight**, and to keep as much HP as it can. So it may use potions it would keep for a later fight in a real run.
- **The notes are a copy.** The model reads the same wiki and `learned.md`. Lessons it holds go to the copy's `strategy/pending/`, which starts empty.
- **The tools' waits and the persona are the arena's**, as for Claude Code: `act` waits at most 3 s and a wait 5 s (the simulator settles at once; the game has 20 s and 4 minutes), and `get_persona` gives the arena's four fields, not the game's voice lines.
- **The fight is the simulator's**, as for Claude Code: the snapshots of [The snapshot](#the-snapshot), `arena_over` in the place of the rewards or game over screen, no relic beyond Burning Blood.
- **The brain is stopped by the arena at the end**, where in the game it goes on to the rewards. Its fight log doesn't know `arena_over`, so the arena closes the fight in it with a game over built from the simulator's end state.
- **No controller.** The game's `BrainController` picks between the API brain and the subscription fallback and owns `/start`, `/model` and `/effort`. Its fallback would start Claude Code on the subscription when the brain stops for its cap, which a test must not do, so the brain is made and started directly, as the dev host's smoke test does. The settings are fixed for a fight.
- **The cap is the folder's budget**, and the spend tracker is the fight's own, so the game's sharing of one month between several copies never comes into play.
- **Parts of the brain a single fight never reaches**: lowering the effort on leaving a room (the effort-only message), the refresh at the map, a new run, co-op. They are as untested here as before.

Three of these could be closed with small changes to `SpireMind.Core`, which this mode was built without touching: a place for a harness to add its own notes to the brain's appendix (then the arena's facts would stand where the game's own harness notes stand), a way to name a harness's end screen to `FightLog` and `RunScreens` (then the fight log would close by itself and the brain would idle at the end by itself), and a hook that lets a harness add text to a fight's first message (which `--brief` would need).

### What each costs

- **`--brain claude-code`** costs subscription usage and no money. `$/fight` in its report is what the sessions would have cost on the API by Claude Code's price list, Claude Code's own system prompt included.
- **`--brain api`** costs API credit. Measured once, on position runs with the enemies' next moves shown (2026-10-12, Opus 5.5, 40 runs a setting; a position run is the rest of a fight from a recorded turn, seven of the eight positions elite or boss fights): at the game's settings (medium, elite and boss rooms high) $0.32 a run, 7,200 output tokens, about 800 a request, 1.6 minutes; one level higher (high, elite and boss rooms xhigh) $0.77 a run, 23,200 output tokens, about 2,240 a request, 4.2 minutes. The first request of the hour cost $0.37, most of it the system prompt written to the cache; a first request that read the cache cost about $0.05. No request failed, was cut off or was refused in the 81 runs. Whole fights have not been measured in the arena: the game's own benchmark of 40 real Act 1 fights on Opus 5.5 (`docs/brain.md`, Fight effort) came to about $0.47 a fight at medium and $0.70 at high, and an arena fight should cost about that or a little less, since its conversation starts empty: so roughly $20 to $25 for the usual 40 fights of a seed, and the default budget of $5 is 7 to 10 fights. The first fight of an hour also writes the system prompt to the cache once per effort (about 25,000 tokens, some $0.20 on Opus 5.5); fights after it read it. The first fight of a run is played alone to its end even with `--parallel`: it writes that cache, and it tells the budget what a fight costs before several run at once.
- A fight takes about as long as in the game: 0.8 minutes at medium and 2.1 at high in that benchmark. One fight runs at a time unless `--parallel` says otherwise.

### The result line under `--brain api`

The line has the fields of [The result line](#the-result-line), with `brain` `api`. These mean something else than under Claude Code:

| Field | Under `--brain api` |
| --- | --- |
| `effort` | The effort the brain's settings give the fight's room. `api.efforts` has what the requests really carried, and `notes` says so if the two ever differ |
| `total_cost_usd` | The brain's own figure: the run total of the fight's `SpendTracker`. Real spend, as the game counts it |
| `input_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens`, `output_tokens`, `model_usage` | Added up from the responses' `usage`; `tokens_source` is `api`. `model_usage` is per model the API said it billed, with `cacheCreation1hInputTokens`, `costUSD` and `requests` |
| `num_turns` | API requests answered |
| `resumes` | How often the brain told the model to keep playing |
| `read_calls`, `read_errors`, `wiki_pages`, `other_reads` | The brain's `notes_read` calls: the wiki pages they opened, and every other file they asked for (a missing one ends in ` (missing)`) |
| `tool_calls` | By tool name, the notes tools and `get_persona` included |
| `prompt_hash` | 12 hex digits of the SHA-256 of the system text the brain sent. It is never Claude Code's hash for the same notes, since the appendix differs |
| `cli` | `model` is the model the API answered as and `result_subtype` how the brain stopped; the rest is null |
| `usage_five_hour`, `usage_seven_day`, `cli_duration_ms` | null |

| In `api` | Meaning |
| --- | --- |
| `model`, `room`, `settings` | The model asked for, the fight's room, and the settings' `effort`, `elite_effort`, `boss_effort` (each as it works out) and `max_tokens` (a number, or `by effort`) |
| `requests` | Requests the API answered |
| `efforts`, `top_level_efforts`, `max_tokens` | The requests counted by the effort in effect, by the top-level `output_config.effort`, and by `max_tokens` |
| `thinking` | The thinking field the requests carried, each different one once; `none` for a model the brain sends none to |
| `effort_messages` | Requests that carried the per-message effort beta (0 in a single fight) |
| `conversations`, `openings` | Conversations the brain started, and the first line of each one's first message |
| `keep_playing`, `cut_offs`, `refusals`, `failed_requests` | The brain's "Keep playing" messages, replies cut off at `max_tokens`, replies a classifier declined, and requests the client gave up on (`RateLimited 429`) |
| `after_fight_requests` | Requests answered after the fight was over |
| `stop`, `stopped_for`, `stop_message` | The brain's stop reason (`UserStop` when the arena stopped it), what stopped it (`the fight was over`, `the batch's budget was reached`, `the time limit`, `the brain itself`), and the brain's own sentence |
| `spend` | The fight's spend tracker: `usd`, `requests`, and `cap_usd`, what was left of the budget when the fight started |
| `key_source` | `file`, `environment` or `fake`. Never the key |
| `prompt_chars`, `prompts`, `tools` | The system text's length, how many different system texts the requests carried (1), and the tools offered |
| `arena_note` | The line the fight was given |
| `said`, `announced` | What the model said in the chat, and what the brain announced there |
| `fight_log` | The brain's own fight line (`docs/brain.md`, Fight log): potions used and held, the efforts in order, requests, conversations, cost |
| `notes_copied`, `notes_written` | How many notes files the copy started with, and every file that was new, changed or gone afterwards: `file`, `change`, `kind` (`journal`, `held lesson`, `journal archive`, `learned`, `wiki page`) and its `text` (cut at 6,000 characters) |

`run.json` of an API batch names the brain, the settings and where they came from, the budget and what the folder had already cost (`max_usd`, `spent_before_usd`), the fights named with `--only`, the prompt's files and hash, and the arena's line. It never holds the key or the prompt's text. `api-spend.jsonl` beside it is the folder's ledger: one line for every request the API answered, in every run.

A transcript of an API fight is the brain's own format, not Claude Code's stream-json, so a position cannot be rebuilt from it yet: `position` reads recordings of Claude Code fights, and either brain can play them.

### What holds it to its word

`ApiBrainChecks` (in `selfcheck`, or alone with `selfcheck --checks ApiBrainChecks`, ten seconds) plays whole fights with the real brain and Core's real API client over the scripted transport, in temp folders of its own, and makes about 500 checks: that the request the arena's brain sends for a hallway fight, an elite and a boss is, field by field (model, `max_tokens`, system, tools, `output_config`, thinking, caching), the request a brain wired as `BrainWiring` wires it sends for the same snapshot, and that the first message differs from that brain's by the arena's one chat line and nothing else; that the system prompt on every request is `PromptBuilder`'s own for the checkout, byte for byte; that the first message is the brain's own opening with the arena's line as its one chat line; that a weak, an elite and a boss fight go out at medium, high and high with 16000, 32000 and 32000 `max_tokens`, and at other efforts when the settings name them; that a real model id gets the game's thinking field and prices, and the reply's thinking block is sent back untouched; that the notes written are in the copy and the result and the checkout's files are unchanged; that the cost is the spend tracker's and equals what the usage comes to; that the brain is stopped after the allowed requests; that a model that pauses is carried on and one that quits is abandoned; that nothing starts without `--yes-spend`; that the budget stops a batch between fights and inside one, with no request sent after it; that the same command again in a folder whose budget is used up sends nothing, and a higher `--max-usd` goes on from what the ledger says; that a monthly budget of 0 and a `--max-usd` above the monthly budget start nothing; that `--only` plays the named fight and no other; that a missing key and a refused key say so; and that the report and compare keep the two brains apart. No check calls the real API or reads a real key: the whole of `selfcheck` passes with every outbound connection but localhost denied by the system and no key in the environment. What they cannot show is the real API's own behaviour: whether it accepts the requests is what `scripts/dev-host.sh --brain` was built to prove (`docs/brain.md`, Smoke test).

## How one fight is run

`ClaudeFight` does this for every fight:

1. A new `ArenaGame` for the scenario (with the simulator's experiment switches, when the batch has any), with `SnapshotVersioner`, `DecisionWaiter`, `ChatHub` (the Ironclad, with the dev host's small persona) and `BotTools`, behind a `BridgeServer` on a free loopback port (never 17772 or 17773, the game's). Two timings differ from the game: `act` waits at most 3 s for the next decision and `wait_for_decision` at most 5 s, since the simulator settles in well under a second and nothing in it takes the game's 20 s or four minutes.
2. The system text: `PromptBuilder` builds it from `.claude/commands/play.md` and `strategy/general.md`, `rules.md`, `bosses-and-elites.md`, `characters/ironclad.md` and `learned.md`, with its index of the pages under `strategy/wiki/` (path and one-line summary each, not their text), exactly as for the in-game brain. Only the brain's closing appendix about its own harness (notes tools, the run journal) is swapped for a short one about the arena: that the wiki's pages are files under `<checkout>/strategy/wiki/` (the absolute path of the checkout the prompt was built from), read with the `Read` tool when the prompt says to; that the index stands in for globbing and the `notes_` tools the index names don't exist here; that nothing can be written, no lesson, no `learned.md`, no journal; that the fight follows the real game (v0.107.1) with only Burning Blood as a relic and with texts worded by the arena, so the snapshot wins over memory where the two differ; that the fight counts as lost if it isn't won by the end of turn 40 (the number is read from `Tuning.MaxTurns`); and what the end looks like. `learned.md` changes as the bot plays real runs, so each result carries a short hash of the prompt and the report says when a file mixes prompts. The hash is taken with the checkout's path left out, so the same notes in a frozen copy give the same hash. Under `--brief` the appendix has one more line, which the hash leaves out too (see [Experiments](#experiments)). The pages' text is not in the prompt, so the wiki has a hash of its own (`wiki_hash`).
3. The user prompt says this is a practice fight in a simulator with the same rules and tools, to play it to the end as well as possible (win first, then HP), to use `act_sequence` for whole turns where safe, that the wiki's pages can be read with `Read` and nothing can be written, to call `say` at most once, and to stop when a snapshot's `screen` is `arena_over`. Under `--brief` the first session's prompt ends with the wiki pages of the fight's enemies.
4. Claude Code is started with no shell:

   ```
   claude -p <prompt> --session-id <new uuid> --model <model> --effort <effort> --append-system-prompt <system text>
     --strict-mcp-config --mcp-config '{"mcpServers":{"spiremind":{"type":"http","url":"http://127.0.0.1:<port>/mcp"}}}'
     --tools Read --permission-mode dontAsk
     --allowedTools mcp__spiremind "mcp__spiremind__*" "Read(//<checkout>/strategy/**)"
     --disallowedTools Edit Write NotebookEdit Bash
     --output-format stream-json --verbose
   ```

   in a new empty temp folder (so no project settings, `CLAUDE.md` or journals are picked up), with its standard input closed and both output streams read continuously. The tools and rules are the in-game brain's (`SubscriptionBrain`), less everything that writes: that session gets `Read,Glob,Edit,Write,ToolSearch` and `Edit(//<repo>/runs/**)` and `Edit(//<repo>/strategy/**)`; a fight's session gets `Read` and a `Read` rule for the strategy folder. [What a session can read](#what-a-session-can-read-and-why-it-can-write-nothing) says what the flags amount to.
5. Its environment is the arena's own without every `ANTHROPIC_*` variable (API key, auth token, base URL, model pins) and every `CLAUDE_*` variable and `CLAUDECODE`, except `CLAUDE_CONFIG_DIR` and `CLAUDE_CODE_OAUTH_TOKEN`, which say where the login is. That removes the provider switches (`CLAUDE_CODE_USE_*`) and whatever a Claude Code session leaves for its children when the arena is started from inside one (its session id and socket, `CLAUDE_EFFORT`, host auth), so the session bills the subscription login and runs the model and effort that were asked for.
6. The stream-json output is read line by line: the init line (model, MCP server status, the tools on offer), the tools the model calls, for each `Read` the file it asked for and whether the result was an error, the result line (cost, turns, usage, `modelUsage`) and the `rate_limit_event` lines (the usage windows, the limit's status, extra usage). Lines that aren't JSON, and stderr, are kept as notes and never read for meaning. Four things on the init line end the fight at once as an error:
   - the `spiremind` server is listed as anything but `connected` or `pending`, or is missing from the list;
   - none of the tools on offer is an `mcp__spiremind__` tool. `--tools Read` leaves `Read` as the only built-in tool, and the runner counts on the MCP server's tools being offered to the model directly, as Claude Code 2.1.296 did in the pilot (started with `--tools ""`, its sessions listed the fight's seven tools and nothing else). A version that hides them would leave the model with nothing to play with, so the error says what the init line listed instead. (A server still `pending` with no tools yet is let through with a note: its tools can arrive a moment later.)
   - a tool is on offer that is neither `Read` nor one of the fight's own: `Edit`, `Write`, `Bash`, `Glob`, anything. The session was to get nothing that could write a file, so it is stopped at once and the error names the tools;
   - `Read` is not on offer. The prompt tells the model to open the wiki's pages with it, and a fight is not played under a prompt that doesn't hold.
7. **Resuming.** Claude Code headless ends its session as soon as the model answers without a tool call, whether the fight is over or not. The in-game brain then starts the same session again, and so does the arena: while the fight isn't over and the session ended by itself without an error, it is started again in the same folder with `--resume <the same uuid>` in place of `--session-id`, the same other arguments, and the prompt `Keep playing: the fight isn't over. Call wait_for_decision, and go on until a snapshot's screen is arena_over.` At most 5 times per fight. Cost, tokens, turns and duration of all those sessions go into the fight's numbers (see [The result line](#the-result-line) for how), the result line counts them in `resumes`, and the transcript has a line of type `arena` where each one starts. Only when the fifth resume also ends with the fight unfinished is the fight `abandoned`. A session isn't resumed after an error, after a refusing rate limit status, or once the time limit has passed.
8. When the game says the fight is over, Claude Code gets 20 s of silence (`--grace-seconds`, at least 10) to finish by itself; each line it prints restarts the wait, up to three times that in all. Then it gets SIGINT, and is killed 5 s later if still there. Ending by itself matters: the result line with the cost and token counts only comes then. If the time limit (all sessions together) passes before the fight is over, the session is stopped and the fight is a `timeout`.
9. A fight whose sessions never made one call to the fight's tools wasn't played: the tools weren't there for the model, or the service kept it waiting. That says nothing about the model, so instead of `abandoned` or `timeout` the fight is an `error`, to be played again.
10. The bridge is closed and the temp folder deleted, whatever happened. So is the one folder Claude Code kept the fight's session in so that it could be resumed: `~/.claude/projects/<name>` (under `CLAUDE_CONFIG_DIR` when that is set), where `<name>` is the temp folder's path as the child saw it (`/private/var/folders/.../spiremind-arena-XXXXXX` on macOS) with every character that isn't an ASCII letter or digit turned into `-`, which is how Claude Code 2.1.296 names it. The runner only looks at all when the fight's folder is what it made itself: an absolute path whose last part starts with `spiremind-arena-`. It only deletes a folder whose name it derived that way and that ends with the temp folder's own name, random letters included (`...-spiremind-arena-Ab12Cd`), that lies directly in the projects folder, exists and is a real folder and not a link; links inside it are removed as links, and nothing else there is touched. If it finds no such folder after a session ran, or can't delete it, the fight's `notes` say so. A batch that is killed outright (a second Ctrl+C, `kill -9`) can't clean up, and leaves its fights' temp folders and session folders behind.

## What a session can read, and why it can write nothing

The real bot reads its wiki: `strategy/rules.md` is in its prompt, the prompt lists the pages under `strategy/wiki/`, and the play prompt tells it when to open one. An arena session gets the same prompt, so it gets the same pages, or the comparison would be of models told about a wiki they cannot open.

**What it can read.** The files under `<checkout>/strategy/`: the wiki's pages, the wiki's `README.md`, and the strategy notes that are in the prompt already. The checkout is the one the prompt was built from. Nothing else of the repo: not `runs/`, not `CLAUDE.md`, not the source. Beyond that only what Claude Code lets every session read: its empty working folder and some folders of Claude Code's own (the session's tool results, scratchpad and memory, and the task and team folders under `~/.claude`). A file read from any of those shows in the result's `other_reads`. There is no `Glob`, so the prompt's index is how a model knows which pages exist.

**Why it can write nothing.** A session could only write through a tool, and it has none that can. The first three points overlap on purpose:

1. `--tools Read`. No tool that edits, writes or runs a command exists in the session. Claude Code 2.1.296 does more than leave the others out: it puts every other built-in tool under a deny rule for the session, and a deny rule beats any allow rule, the user's own settings included.
2. `--permission-mode dontAsk` with three allow rules: the fight's MCP server, its tools, and `Read(//<checkout>/strategy/**)`. Whatever no rule allows is refused instead of asked. A `Read` rule allows reading and nothing else.
3. `--disallowedTools Edit Write NotebookEdit Bash`: the tools that could change a file, denied by name, in case a later Claude Code reads `--tools` differently or an allow rule for one of them sits in the user's own settings.
4. The fight's MCP server is the only one (`--strict-mcp-config`), and its seven tools (`wait_for_decision`, `get_state`, `get_deck`, `get_persona`, `act`, `act_sequence`, `say`) act on the fight in memory. None of them touches a file.

**How far this is proven.** The fake CLI cannot test permissions: it is a script that answers a `Read` the way these flags should make Claude Code answer it. So the claim rests on reading, not on a trial. The flags' meaning was read from the Claude Code 2.1.296 binary on Mark's Mac: `--tools` turns every tool it doesn't name into a deny rule; under `dontAsk` a call that would need approval is refused (`Permission to use Read has been denied because Claude Code is running in don't ask mode.`); a `Read` of a file outside the working folders goes through only when an allow rule matches every spelling of its path, the one asked for and the one its links lead to; and `//` starts an absolute path in a rule. No live session has been run with these flags yet. What has been seen live is the pilot (`--tools ""`), whose sessions listed no built-in tool at all.

Three things hold a batch to the claim while it runs:

- **Every session's init line is checked.** It must list `Read` and the fight's tools and nothing else. A session that lists any other tool is stopped the next time the runner looks at it, within a quarter of a second of its init line, and its fight is an error (step 6 above). So no fight is played by a session that had a writing tool.
- **Every result line says what was read**: `read_calls`, `read_errors`, `wiki_pages` and `other_reads`, taken from the session's `tool_use` blocks and their results. A model that reaches for a tool it doesn't have (`Write`, `Glob`) shows by that name in `tool_calls`, with the error it got in `tool_errors`.
- **The wiki is hashed** when the batch starts, when each fight ends (`wiki_hash`) and when the batch ends, and the batch says whether it changed. Since no session can write there, a change means something else did (a real run adding a lesson); the fights before and after then read different pages, and the report says so.

Two limits of the path the rule is made for: it is the checkout's real path, because Claude Code refuses a file reached through a link unless the link's target is allowed too; and a checkout whose path has a bracket, `*`, `?` or a backslash in it is turned down when the batch starts, since a rule could not spell it.

## The result line

One JSON object per fight in `results.jsonl`. It never holds the system prompt or anything from the environment.

| Field | Meaning |
| --- | --- |
| `scenario`, `tier`, `summary`, `seed` | The fight: its id, weak/normal/elite/boss, the one-line description, its seed |
| `model` | As asked for on the command line |
| `effort` | The effort the fight's sessions were started with: `--effort`, or what `--effort-by-tier` names for the fight's tier |
| `experiment` | The label of the batch's experiment: the switches that were on, `base` for none, with a position test's own name in front (`old`, `new+forecast`). A line from before there were switches has no such field and is read as `base` |
| `brain` | What drove the fight: `claude-code` or `api`. A line from before there were two has no such field and is read as `claude-code` |
| `api` | Only on the line of a fight the API brain played: what the brain was run with and what it did; see the table below and [The result line under `--brain api`](#the-result-line-under---brain-api) for the fields that mean something else there |
| `position` | Only on the line of a position run ([Position tests](#position-tests)); see the table below. A line with it is no fight of its scenario: the fights' report and `run`'s rule for what is already played leave it out |
| `models_used` | The model ids in Claude Code's `modelUsage` (what was billed) |
| `outcome` | See the table below |
| `hp_left`, `max_hp`, `damage_taken` | The player at the end; `damage_taken` is HP lost during the fight |
| `turns`, `cards_played` | The simulator's counters at the end |
| `refused_actions` | Actions the simulator itself refused |
| `tool_calls` | Calls by tool name (`act`, `act_sequence`, `wait_for_decision`..., and `Read`), over all the fight's sessions. A name here that isn't one of the fight's tools or `Read` is a tool the model asked for and didn't have |
| `tool_errors` | Tool calls that came back as errors: an illegal id, a stale version, acting while busy. The tools refuse those before the simulator sees them. A `Read` that failed is not counted here |
| `read_calls` | How often the model called `Read` |
| `read_errors` | `Read` calls that came back as an error: a file that isn't there, or one outside the strategy folder, which the session's permissions refuse |
| `wiki_pages` | The wiki pages the model opened, each once, in the order it first opened them, as the prompt's index names them (`strategy/wiki/enemies/vantom.md`). A page counts once its `Read` came back without an error |
| `brief_pages`, `brief_chars`, `brief_missing` | Only under `--brief`: the wiki pages the fight's first message held, as the index names them, in the order the enemies stand; the length of those pages' text together, in characters; and the enemies the wiki had no page for, by name (each is also a line in `notes`) |
| `other_reads` | Every other file it asked `Read` for, each once, at most 12: a file that isn't a wiki page (`strategy/wiki/README.md`, `strategy/general.md`, a path outside the checkout as it was asked for), and a wiki page whose `Read` failed. An entry ends in ` (failed)` when the call came back as an error |
| `wall_seconds` | From the first session's start to the end of the fight (or of the last session, when the fight never ended), the gaps between sessions included; Claude Code's wind-down isn't in it |
| `resumes` | How often the session ended before the fight was over and was started again with `--resume` (0 to 5) |
| `cli_duration_ms`, `num_turns` | `duration_ms` and `num_turns` of Claude Code's result lines, added up over the fight's sessions; null when no session printed one (it had to be stopped) |
| `total_cost_usd` | What the fight's sessions would have cost on the API, from the result lines' `total_cost_usd`: a running total that a resumed session carries on, so the last line's when it does and the sum when it doesn't (see below the tables) |
| `input_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens`, `output_tokens` | The result lines' `usage`, added up. `tokens_source` is `result`; `messages` when no session printed a result line and the assistant messages' own usage was added up instead (a lower bound); `result+messages` when the fight's sessions differ in that |
| `model_usage` | Claude Code's `modelUsage` over the fight's sessions, tokens and cost per model id, put together the same way as `total_cost_usd` |
| `usage_five_hour`, `usage_seven_day` | How full the subscription's 5 hour and 7 day windows were (0 to 1) when Claude Code last reported them during this fight; null when it never did |
| `error` | What went wrong, for an `error` (and for a won or lost fight whose session still ended badly) |
| `tools_problem` | Present when a session's init line didn't list the tools the arena asked for (the same sentence as `error`). A fight that comes back with it stops the batch |
| `usage_limit` | Present when Claude Code itself reported the usage limit: the sentence of an error result, a rate limit status that refuses requests, or extra usage. Never the model's words. A fight that comes back with it stops the batch |
| `notes` | Output lines that weren't JSON, stderr, and remarks from the runner (a line repeated by a resumed session is kept once) |
| `started_at` | UTC, ISO 8601 |
| `cli` | `model` and `version` from the init line, `api_key_source` (`none` on the subscription login), `mcp_status`, `result_subtype`, `rate_limit_status` (`allowed`, `allowed_warning` or `rejected`, from the last `rate_limit_event`), `exit` (`exited` by itself, `interrupted`, `killed`), `exit_code`; where sessions differ, the last one's |
| `last_text` | The start of the model's last text; says why it stopped when it gave up |
| `prompt_hash` | 12 hex digits of the system text's SHA-256, with the checkout's path left out of it, and the one line `--brief` adds to the appendix left out the same way. So a brief run and a plain run on the same notes have the same hash, and a hash that differs between two runs always means the play prompt, the strategy notes or the wiki index changed |
| `wiki_hash` | 12 hex digits of a SHA-256 over the files under `strategy/wiki/` as they stood when the fight ended; null when the checkout has no wiki, and in lines from before the sessions could read it |

| In `position` | Meaning |
| --- | --- |
| `turn`, `run` | The player turn the session took the fight over at the start of, and the run's number among the position's runs, from 1 |
| `mode`, `deal_seed` | `recorded`: the run went on with the recorded fight's own draw order and random numbers. `reshuffled`: with its own deal of what the player cannot know, from `deal_seed`, which follows from scenario, turn and run alone |
| `state_hash` | 12 hex digits of the SHA-256 of the position's first snapshot. Scenario, turn and this are the position |
| `hp_at_start` | The player's HP at the position |
| `replayed_actions`, `snapshots_checked` | Recorded actions applied to reach the position, and recorded snapshots the rebuilt fight was compared with on the way |
| `from` | The recording: `results` (the run's folder), `model`, the recorded fight's `outcome` and `hp_left`, and `line`, what the recorded session ran on that turn, by label |
| `line`, `line_ids` | What this run's session ran on the position's turn, in order: the labels (`Play Defend`, `Play Cinder on Vantom`, `End turn`) and the ids |
| `line_complete` | True when the turn was ended (or the fight ended in it); false when the session stopped in the middle of it, so `line` is only the turn's beginning |

In a position run's line `turns`, `cards_played` and `damage_taken` count from the fight's first turn, the recorded part included, and `summary` and `seed` are the scenario's.

| `outcome` | Meaning | Counts in the report | Played again by the next run |
| --- | --- | --- | --- |
| `won`, `lost` | The fight was played to the end. The game decides this first: however the session ended afterwards, a finished fight is won or lost | yes | no |
| `abandoned` | The model stopped before the end and stayed stopped: the session ended by itself without an error, was resumed 5 times and ended the same way each time | yes, as not won with 0 HP | no |
| `timeout` | The fight wasn't over when the time limit passed | no, own column | yes |
| `error` | The fight couldn't be played, which says nothing about the model: Claude Code didn't start, couldn't reach the bridge, or offered a session other tools than the fight's own and `Read`; the API failed; the usage limit or extra usage; a resume that failed (Claude Code no longer finding the session, say); or the sessions ended or ran out of time without one call to the fight's tools | no, own column | yes |

**Numbers of a resumed fight.** A fight with `resumes` above 0 has one result line per session, and Claude Code's fields don't all count the same way. Claude Code 2.1.296 describes `total_cost_usd` and `modelUsage` (in the result line's own field descriptions) as running totals that a resumed session takes up again from what its transcript saved, when it saved any, so that its first result line already includes the earlier sessions. `usage`, `num_turns` and `duration_ms` are the process's own. So the runner adds up turns, duration and `usage`, and for the cost and `modelUsage` takes a resumed session's line in place of the earlier ones when that line carries them, and adds it when it doesn't. It tells the two apart by the line itself: it carries the earlier sessions when its cost is no lower than the total so far and its `modelUsage` grew by about the output tokens of its own `usage` or more (nine tenths of them); a session that started from zero reports about the same output tokens in both, so its `modelUsage` didn't grow by that much, and it is added. This was read from the Claude Code binary and tried with the fake CLI both ways, not yet seen on a real resumed session. Each session's result line is in the fight's transcript, so the first real fight with `resumes` above 0 shows whether the cost in its result line is right.

## The report

Position runs are reported apart from fights: nothing below counts them, and [The position report](#the-position-report) says what is printed for them.

Each model's fight on a scenario is counted once: the last line that counts (won, lost or abandoned), else the last line there is (a timeout or an error). So a fight played again after a timeout or an error replaces it.

The title names the effort and the experiment (`effort medium, experiment base`; under `--effort-by-tier` also the tiers that had their own, `effort medium (elite high, boss high)`). A model played at two `--effort`s is two rows, `claude-opus-5-5 (medium)` and `claude-opus-5-5 (high)`. A batch run with `--effort-by-tier` is one row: its fights differ in effort by tier, and that is the experiment, not two players.

Under the title the report says how many fights ran out of time or couldn't be played, and how full Claude Code reported the subscription's 5 hour and 7 day windows from the first of the fights to end to the last. Those are the whole account's numbers, so they are the batch's footprint (and that of anything else running on the subscription meanwhile) and can't be split by model.

**Per model**

| Column | Meaning |
| --- | --- |
| fights | Fights that count: won, lost or abandoned. Timeouts and errors are in their own columns and counted nowhere else |
| won, win rate (95%) | Wins, and the share of fights with its 95% Wilson interval: the range the model's true win rate on such fights plausibly lies in |
| HP left | Mean HP at the end over all fights, with 0 for every fight that wasn't won |
| HP lost/win | Mean `damage_taken` in the fights it won |
| turns, cards | Means over fights played to the end (won or lost) |
| refused/fight | Mean `refused_actions` |
| min/fight | Mean `wall_seconds`, in minutes |
| $/fight | Mean `total_cost_usd`: what the fight's sessions would have cost on the API, by Claude Code's own price list. Nothing is charged; it runs on the subscription |
| $ repo prices | The same from `model_usage` and the repo's price table (`ModelPrices`), as a second opinion when Claude Code doesn't know a model's price. Cache writes count as 5 minute writes, the long-prompt tier is left out, and a helper model the table doesn't know is left out |
| out tokens | Mean output tokens (all models in `model_usage`), thinking included |
| resumes | How many times in all a session of the model's ended before its fight was over and was started again. A model that keeps stopping to report mid-fight shows here long before it shows as abandoned |
| abandoned, timeout, errors | Shown when there are any. A timeout is in this column only |

**Tool calls per fight**: the mean calls of each tool, the tool errors and Claude Code's turn count. A model that plays card by card with `act` pays for it in time and tokens. `Read` is the mean of `read_calls` and `wiki pages` the mean number of different pages a fight opened; `other` is every tool that is neither one of the five named nor `Read`.

**Wiki pages opened**: per model, in how many of its fights it opened a page at all, in how many of its elite and boss fights (where the prompt asks for the enemy's page), which pages most often with how many fights opened each, and how many of its `Read` calls failed.

**Wiki pages given in the first message**, when a batch ran with `--brief`: per model, in how many fights pages were given, the mean number of pages and of characters, in how many fights a `Read` call opened a page the fight had been given all the same, and the enemies the wiki had no page for.

**Per scenario**: each model's fight (`W <HP left>`, `L`, `gave up`, `timeout`, `error`, `-` for not played) next to each scripted policy's on the same fight, then the fights' summaries.

**Paired comparison**: for each pair of models, on the fights both played to the end: in how many the first had more HP left, the same, less (a loss is 0 HP), the mean difference, and the two-sided sign test's p (how likely a split at least that lopsided is if neither is better; ties left out). The same against each scripted policy.

A pair is dropped when either side didn't play its fight to the end, and that includes a fight one side abandoned, which the per-model table counts as 0 HP. So this table flatters a model that gives up on the fights it finds hard. The line under the table says so and counts the dropped pairs per model: for each model, the pairs lost to its own unfinished fights, and what those fights were (`claude-x 4 (2 abandoned)` is two abandoned fights, each dropping the pair with two other models). A second line under the policies' table counts each model's fights left out there.

## Reading the results honestly

- **Few fights, noisy win rates.** With 8 fights, 6 wins against 4 is no evidence of anything: the intervals are 41-93% and 22-78%. The interval column is there to be read. Telling two models' win rates apart takes dozens of fights unless one is far better.
- **HP left on the same fights is the sharper measure.** Every model plays the same seeded fights, so the comparison is paired: fight by fight, who came out with more HP. That cancels how hard each fight is, which is most of the noise in a win rate. Still, look at the sign test's p before believing a split: 5 against 3 is p = 0.73.
- **One try per fight.** Models don't play the same fight the same way twice. A single upset on one scenario means little; a pattern across scenarios and seeds means more. To add evidence, run more scenarios or another `--seed` into another folder, not the same fights again.
- **An abandoned fight counts against the model** (as not won, 0 HP), since stopping for good is its own doing; but it takes six sessions ending early to get there, so read `resumes` first, and the transcript when it is high. **Timeouts and errors don't count at all**: a slow service produces timeouts as well as a slow model, and the next run plays those fights again. If a model has many of either, read its transcripts before its numbers, and remember the paired table drops abandoned fights.
- **The scripted policies are a yardstick, not a rival.** They cost nothing. Greedy sees exactly what a model sees; Lookahead also runs the engine on copies of the fight, which no model can. A model that matches Lookahead on HP is playing the turn's arithmetic about as well as it can be played; a model that loses fights Greedy wins is worth a look at its transcript.
- **Reading the wiki is part of what is measured.** The prompt asks for an enemy's page before an elite, a boss or an enemy the model doesn't know, and a model that follows it pays in time and tokens and may play better for it. So read HP and cost next to the `wiki pages` column: a model that opened no page played from the prompt alone. Results from before the sessions could read (no `wiki_hash`, another `prompt_hash`) are another experiment; don't put them in one table with these.
- **It is the game's combat, and not a run.** The fights follow the game, so what a model knows about the game's cards and enemies applies, and the strategy notes fit. But the decks are not drafted, there are no relics, and the tiers are dealt 4:2:2:2 instead of as a run meets them, so a win rate here is no forecast of winning runs. The card texts are the arena's own wording: a model that plays from the snapshot is safe, one that plays from the remembered text of a card may misread a detail.
- **The weak tier is nearly free, the boss tier is steep.** See [How hard it is](#how-hard-it-is). Weak fights separate models by HP only; boss fights mostly by who wins at all.
- **The tools are the game's.** Ids and names arrive as written (`play:0>e0`, `Bash+`), exactly as the mod's endpoint sends them in the real game: both serve tool results through the same `McpEndpoint`, which leaves `>` and `+` unescaped.
- **Cost and time are Claude Code's, not the in-game brain's** (under `--brain claude-code`; under `--brain api` they are the brain's own, and the dollars are real). `$/fight` includes Claude Code's own system prompt and its caching, and wall-clock time depends on how busy the service is and on how many fights run at once. Use them to compare models within one run, at the same `--parallel`.
- **The usage windows are the account's.** The percentages a batch prints tell what it took out of the 5 hour and 7 day windows (with whatever else ran on the subscription meanwhile), in whole points, for all its models together. To budget a model, run it alone.
- **The effort is the same for every model** in a batch. Models respond to it differently; compare efforts with separate batches on the same seed, into separate `--out` folders (`compare A B`) or into one (`report --by-label`).
- **One switch, the same fights, the same notes.** An experiment says something only against a batch that differs in nothing else: see [An honest experiment](#an-honest-experiment).

## Trying the runner without Claude

`src/SpireMind.Arena/Fake/fake-claude.py` stands in for the Claude Code binary: it takes the same arguments, prints stream-json lines shaped like Claude Code's (a `rate_limit_event` among them) and plays over the bridge with a fixed rule. It keeps its sessions where Claude Code does, so `--resume` only works if the session was kept and the runner's cleanup has something to delete. The model name picks its behaviour; the file lists them all:

| Models | What they show |
| --- | --- |
| `fake-play`, `fake-seq`, `fake-block`, `fake-clumsy` | Fights played to the end with `act` or `act_sequence` |
| `fake-done` | Running the end screen's `done` |
| `fake-pause`, `fake-pause3` | A session that ends early once or three times and plays on when resumed |
| `fake-abandon` | A session that ends early every time: abandoned after 5 resumes |
| `fake-forget` | A resume that finds no session: an error |
| `fake-prose`, `fake-softlimit` | A model talking about limits, which must never count as the usage limit |
| `fake-limit`, `fake-rejected` | The usage limit in an error result, and as a `rejected` status |
| `fake-warning` | The `allowed_warning` status, which doesn't stop anything |
| `fake-overage` | Extra usage: every running session is stopped |
| `fake-hang`, `fake-linger`, `fake-stubborn` | The time limit, the grace after the fight, and a session that has to be killed |
| `fake-nomcp`, `fake-notools`, `fake-pending`, `fake-crash` | A dead MCP server, one whose tools aren't offered, one still connecting, and a crash |
| `fake-reader` | Reading the act's page and each enemy's page before the first move: `read_calls` and `wiki_pages` |
| `fake-briefed` | The same, but an enemy's page that the first message already holds under `--brief` is not opened again |
| `fake-snoop` | Asking for a page that isn't there, a file outside the strategy folder and the wiki's README, and calling a `Write` tool it doesn't have: `read_errors`, `other_reads`, `Write` in `tool_calls`, and nothing written |
| `fake-writer`, `fake-blind` | An init line that lists `Edit` and `Write`, and one without `Read`: both stopped at once, and the batch starts no more fights |
| `fake-notes` | Playing by its notes: like `fake-block` when the system prompt it was started with holds the line `FAKE: block first`, else like `fake-play`. Two checkouts whose notes differ in that line (`--repo`) give two prompt hashes and two ways of playing, which is what an old prompt against a new one looks like |
| `fake-replay` | A real session's saved stream-json output read back, to see what the runner makes of it |

```bash
scripts/arena.sh run --claude src/SpireMind.Arena/Fake/fake-claude.py --models fake-play,fake-seq,fake-pause3,fake-abandon \
  --scenarios 3 --out /tmp/arena-fake
```

With `FAKE_CLAUDE_USAGE` set to a file holding two numbers (`0.05 0.86`), the fake reports those as the 5 hour and 7 day windows and raises them with every session, which is how `--stop-at-weekly` was tried. A resumed fake session carries its cost and `modelUsage` on from the sessions before it, as Claude Code 2.1.296 says it does; `FAKE_CLAUDE_CARRY=0` makes every result line hold only its own process's numbers, and the fight's totals must come out the same either way. Set `CLAUDE_CONFIG_DIR` to a scratch folder to keep the fake's sessions out of `~/.claude` while trying things. With `FAKE_CLAUDE_PROMPT` set to a file, the fake writes there the system prompt and the user prompt it was started with, which is the quickest way to read what a model is told. Set to a folder, every start writes its own file there (`<session id>.<n>.txt`, n counting the session's starts, with the model and the effort it was started at in the first line): that shows what each fight of a batch was told, the first message apart from the resumes, and at which effort.

The fake answers a `Read` as the arena's flags should make Claude Code answer it: the file's text when it lies under the folder the `Read(//...)` rule names, Claude Code's refusal anywhere else, an error for a file that isn't there. That exercises the runner's counting. It is not a test of Claude Code's permissions, and the fake never writes anything but its own session files. `fake-replay` of a transcript saved before the sessions had `Read` (the pilot's) is stopped at its init line, which lists no `Read`.

The fake plays the first card it may play and answers a card choice by confirming as soon as it can, so it gets through any fight the arena deals, and loses the hard ones.

**The API brain has a stand-in of its own.** `--fake-api` puts a scripted transport (`Runner/FakeApi.cs`) in the place of the network under Core's real API client, so the real brain builds its prompt, sends its requests, runs the tool calls that come back and counts its spend, and nothing leaves the Mac: no key is read, nothing is billed, and `--yes-spend` isn't needed. It plays like the fake CLI (the first card it may play). The model name picks what else it does: `fake-play` only plays; `fake-notes` (and any name that doesn't start with `fake-`, so a real id can be tried for its thinking field and prices) also reads the first enemy's wiki page and writes a journal line before its first move, and holds two lessons and adds a journal line once the fight is over; `fake-pause` ends its turn once without a tool call; `fake-quit` makes one move and then only talks, which the brain gives up on after its five reminders; `fake-401` is a key the API refuses. Its usage is fixed (10,000 tokens written to the cache on a conversation's first request and read on each one after), so a fight's cost follows from its request count: at the price of a model the table doesn't know, $0.22 and then $0.023 a request.

```bash
scripts/arena.sh run --brain api --fake-api --model fake-notes --scenarios 4 --seed 7 --out /tmp/arena-api-fake
scripts/arena.sh run --brain api --fake-api --model fake-play --scenarios 3 --seed 7 --max-usd 0.30 --out /tmp/arena-api-broke   # the budget, reached inside the first fight
scripts/arena.sh position /tmp/arena-rec boss-878005 fake-play --turn 3 --runs 2 --brain api --fake-api --out /tmp/arena-api-pos
```

A position test needs a recording, and the fake makes those too. This records four fights, rebuilds one of them to turn 3, plays the position three times on the recorded deal and three times reshuffled, and prints the report:

```bash
F=src/SpireMind.Arena/Fake/fake-claude.py
scripts/arena.sh run --claude $F --models fake-play --scenarios 4 --seed 7 --out /tmp/arena-rec
scripts/arena.sh position /tmp/arena-rec boss-878005 fake-play --turn 3 --check
scripts/arena.sh position /tmp/arena-rec boss-878005 fake-play --turn 3 --runs 3 --claude $F --models fake-play,fake-block --out /tmp/arena-pos
scripts/arena.sh position /tmp/arena-rec boss-878005 fake-play --turn 3 --runs 3 --claude $F --models fake-play,fake-block --out /tmp/arena-pos --reshuffle
scripts/arena.sh report /tmp/arena-pos
```

On the recorded deal `fake-play` ends every run as its recorded fight ended (lost on turn 7 after 20 cards), since the same player goes on with the same draws; reshuffled, the runs end differently. Change one number in a snapshot of `/tmp/arena-rec/transcripts/boss-878005__fake-play.jsonl` that lies before turn 3 and the position command stops with `THE REBUILT FIGHT DOES NOT MATCH THE RECORDING` and exit code 1.

It spends nothing, and it is how the runner's paths (resume, timeout, grace, the usage limit and windows, a dead MCP server, Ctrl+C) were tested.
