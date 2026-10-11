# The arena

The arena compares how Claude models play fights, on Mark's Claude subscription, without the game running. It generates seeded fights the way an act-1 run of the game produces them, plays each in a simulator of the game's combat that is built from the game's own data (public build v0.107.1), lets each model play each fight once through the same tools the mod serves, and reports who won with how much HP left. Scripted policies play the same fights for free, as a yardstick.

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
scripts/arena.sh report ~/.config/spiremind/arena/2026-10-10-141500
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

`run --models a,b,c [--scenarios N=8] [--seed S=1] [--parallel K=3] [--effort medium] [--timeout-minutes 30] [--grace-seconds 20] [--stop-at-weekly 0.90] [--out DIR] [--claude PATH] [--repo DIR]`

Each model plays each of the N fights once, each fight in its own Claude Code session on the subscription. It prints a line when a fight starts and when it ends, how full the subscription's usage windows are, and the report at the end.

- **Models** go to `claude --model` exactly as typed. Use full ids (`claude-haiku-5-5`, `claude-sonnet-5-5`, `claude-opus-5-5`): on Claude Code 2.1.287 the alias `haiku` meant Haiku 4.5. That version printed a `[claude-code:unrecognized_model]` notice for `claude-haiku-5-5` and ran it anyway; such a notice ends up in the result's `notes`. The result also records the model Claude Code's init line names (`cli.model`) and the ids it billed (`models_used`), and the report says so when they differ from what was asked.
- **Order.** Fights start scenario by scenario: none of scenario 2 starts before every fight of scenario 1 has started. With K no larger than the number of models, a model never plays two fights at once. So a batch that is cut short still has (almost) the same scenarios for every model.
- **Output folder** (`--out`, default `~/.config/spiremind/arena/<date-time>`):
  - `results.jsonl`: one JSON line per fight, appended the moment it ends.
  - `run.json`: the batch's settings, the scenarios, the prompt's files, length and hash (never its text), the checkout (`repo`), the Read rule its sessions were given (`read_rule`) and the wiki as the batch found it (`wiki_pages`, `wiki_hash`).
  - `transcripts/<scenario>__<model>.jsonl`: the raw stream-json output of the fight's sessions, for reading how a model played. Where a session was started again there is a line of type `arena` saying so.
- **The checkout** (`--repo`, default the one the arena was built in) is where the prompt is built from and where the sessions read the wiki: one path goes into the prompt, the sessions' Read rule and `run.json`, by its real path with links followed. A batch started from a frozen copy of the repo therefore reads that copy's wiki, not the live one. Real runs add lessons to the live wiki, so a batch that should give every model the same pages is best run from a copy nothing else touches; the batch says at its end whether the wiki changed under it, and each result carries the wiki's hash.
- **Carrying on.** Run the same command with the same `--out` and it plays only what is missing: a fight that was won, lost or abandoned is the model's own result and is skipped. A fight that ended in an `error` or a `timeout` says nothing about how the model plays, so it is played again.
- **The time limit** (`--timeout-minutes`, default 30) is for the whole fight, all its sessions together. A fight that isn't over by then is stopped and recorded as a `timeout`. How long a fight takes depends on how busy the service is as much as on the model, so a timeout is no evidence about play: it is in no win rate and no mean, has its own column in the report, and the next run with the same `--out` plays the fight again.
- **Usage.** Claude Code reports how full the subscription's 5 hour and 7 day windows are (`rate_limit_event` lines, whenever a window's rounded percentage moves). The batch prints both as soon as the first fight reports them and again at the end with the difference, and every result line holds what its fight saw last. The numbers are the whole account's: anything else using the subscription meanwhile is in them too.
- **Stopping.** Ctrl+C (or SIGTERM) kills the running sessions, keeps what finished, prints the report and exits with 130. One failed fight never stops the batch. Four things Claude Code itself reports do, each with exit code 1, the results kept and the same command carrying on later:
  - **The 7 day window is as full as `--stop-at-weekly` allows** (a fraction, default 0.90). No new fights start; the ones running are left to end. This is checked while fights run, not only when one ends.
  - **The usage limit is hit**: an error result that says so, or a `rate_limit_event` whose status says requests are refused (`rejected`). No new fights start; the ones running are left to end, usually as errors. When the limit is one model's own (an Opus limit, say), the same command without that model plays the others.
  - **Extra usage** (`isUsingOverage` or `overageInUse` true): the window is used up and requests are billed on top of the subscription. The running sessions are stopped at once and their fights recorded as errors.
  - **A session without the right tools**: its init line lists none of the fight's tools, a tool besides them and `Read`, or no `Read` (see [How one fight is run](#how-one-fight-is-run), step 6). Which tools a session is offered depends on the Claude Code that is installed and on the flags, not on the fight, so every later session would be stopped the same way and each would have cost its start. That fight is an error with `tools_problem` set, no new fights start, and the ones running are left to end.

  Nothing a model writes can trigger any of these. Only Claude Code's own lines are read for it, so a model saying "hand limit reached" is just talking about its hand. The status `allowed_warning` doesn't stop a batch either: Claude Code 2.1.296 sets it when a window fills faster than it empties (90% of the 5 hour window used in its first 72%; 25, 50 or 75% of the week used in its first 15, 35 or 60%), and requests are still served. The batch prints a note the first time it sees it, and `cli.rate_limit_status` records it.
- **Before anything starts**, `claude auth status` must say Claude Code is signed in with `claude.ai`. Signed in any other way, the batch refuses to start, since it would not run on the subscription.

### report

`report DIR|results.jsonl` prints the report again from the file, for a finished or interrupted run. It replays the scripted policies on the same scenario ids, so it needs a build that can still generate them.

### selfcheck

`selfcheck [--scenarios N=30] [--seed S=1] [--checks A,B]` ends with `PASSED` or `FAILED` (exit code 0 or 1). Run it after a change to the simulator or to `SpireMind.Core`, before a batch.

1. **The content** (`Completeness`): every card of the Ironclad's pool, every status card, every Overgrowth encounter and every potion of the Ironclad's pool is either in the registry or left out by name with a reason; nothing is registered twice.
2. **The scripted checks** (`IArenaChecks`, 17 classes, about 150,000 assertions): the engine's rules and every card, power, monster, encounter and potion against numbers worked out by hand from the game's code; the scenario generator against the game's odds; the scripted players; and the runner's own promises (`RunnerChecks`: the flags a session is started with, what the prompt's appendix says about the wiki, that prompt and Read rule name the same checkout, what the init line must list, how Read calls are counted). `--checks Cards1Checks,PotionsChecks` runs only the named classes and nothing else, in a few seconds.
3. **The arena as a whole** (`SelfCheck`): every policy finishes every generated fight with no action turned down, each fight replays identically, ids rebuild their scenario through `Scenarios.FromId`, the combat snapshot has the game's keys, bad action ids are refused with a reason and change nothing, the snapshot is busy right after an action, a finished fight shows the end decision (`arena_over`, one action `done` that runs and changes nothing), and eight threads calling at once break nothing. The honesty check (below) runs on the same fights. It prints the policies' wins by tier.
4. **The tool path** (`ToolPathCheck`): two fights are played over HTTP through a fight's own bridge, as a model plays them: the MCP handshake, `tools/list`, then `wait_for_decision`, `get_state`, `get_deck`, `get_persona`, `act` and whole turns as `act_sequence`. The first is one of the generated fights with Greedy choosing. The second is built by hand so that it is sure to open a hand selection and to use a potion: three Armaments in the deck and a Fire Potion in the belt, with a player that throws the potion and plays Armaments first. A turn's plan carries the picks for a hand selection its card opens (`select:<i>` by the card's index in the plan's snapshot, then `confirm`) and goes on after it, as the play prompt allows, so the second fight must answer at least one hand selection inside the `act_sequence` that opened it; the line it prints also counts upgraded cards played under their new name after their pick. A second game without a bridge gets the same actions, and every snapshot the tools return must equal that game's. It also checks what the tools must refuse (a stale version, an illegal id, an `act` without a version, `end_turn` before the last id) and that a sequence stops when its next card's target is dead and when the fight ends under it.
5. **The end of the fight**, since a model that is told to wait there waits for ever. The fight's last action is played three more times, each on a new game one action short of the end: by `act`, by an `act_sequence` that ends on it, and by an `act_sequence` with an `end_turn` behind it. Each must hand back the end snapshot at once (well under the tools' 3 s settle time), with no `warning`, no "still busy" and no word about `wait_for_decision`; the third must stop with `The screen changed to arena_over.` A `wait_for_decision` after the end must come straight back with the same snapshot, `act end_turn` must be refused against it, and `act done` must succeed and leave the snapshot at the same version. The line it prints says how long each took.

### honesty

`honesty [--scenarios N=300] [--seed S=1]` plays the generated fights with every scripted policy and checks at every action that the snapshot kept what it promised: before `end_turn` each enemy's intents and `incoming_damage`, before a card its cost and the numbers of its text (its `vs` text for the enemy it is aimed at), before a potion the numbers of its text. Then it compares: every hit as the damage chain left it, the hits per enemy, the Block gained, the energy paid, the status cards added. It prints how many promises were kept and every kind of difference with its cause and an example, and ends with `PASSED` when no difference is left without a cause. [What the snapshot does not promise](#what-the-snapshot-does-not-promise) lists the causes.

### content and snapshot

`content` prints what the registry holds against the game's lists, every name left out with its reason, and every card and potion text as a model reads it. `snapshot SCENARIO-ID` prints a scenario's first snapshot.

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

## How one fight is run

`ClaudeFight` does this for every fight:

1. A new `ArenaGame` for the scenario, with `SnapshotVersioner`, `DecisionWaiter`, `ChatHub` (the Ironclad, with the dev host's small persona) and `BotTools`, behind a `BridgeServer` on a free loopback port (never 17772 or 17773, the game's). Two timings differ from the game: `act` waits at most 3 s for the next decision and `wait_for_decision` at most 5 s, since the simulator settles in well under a second and nothing in it takes the game's 20 s or four minutes.
2. The system text: `PromptBuilder` builds it from `.claude/commands/play.md` and `strategy/general.md`, `rules.md`, `bosses-and-elites.md`, `characters/ironclad.md` and `learned.md`, with its index of the pages under `strategy/wiki/` (path and one-line summary each, not their text), exactly as for the in-game brain. Only the brain's closing appendix about its own harness (notes tools, the run journal) is swapped for a short one about the arena: that the wiki's pages are files under `<checkout>/strategy/wiki/` (the absolute path of the checkout the prompt was built from), read with the `Read` tool when the prompt says to; that the index stands in for globbing and the `notes_` tools the index names don't exist here; that nothing can be written, no lesson, no `learned.md`, no journal; that the fight follows the real game (v0.107.1) with only Burning Blood as a relic and with texts worded by the arena, so the snapshot wins over memory where the two differ; that the fight counts as lost if it isn't won by the end of turn 40 (the number is read from `Tuning.MaxTurns`); and what the end looks like. `learned.md` changes as the bot plays real runs, so each result carries a short hash of the prompt and the report says when a file mixes prompts. The hash is taken with the checkout's path left out, so the same notes in a frozen copy give the same hash. The pages' text is not in the prompt, so the wiki has a hash of its own (`wiki_hash`).
3. The user prompt says this is a practice fight in a simulator with the same rules and tools, to play it to the end as well as possible (win first, then HP), to use `act_sequence` for whole turns where safe, that the wiki's pages can be read with `Read` and nothing can be written, to call `say` at most once, and to stop when a snapshot's `screen` is `arena_over`.
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
| `model`, `effort` | As asked for on the command line |
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
| `prompt_hash` | 12 hex digits of the system text's SHA-256, with the checkout's path left out of it |
| `wiki_hash` | 12 hex digits of a SHA-256 over the files under `strategy/wiki/` as they stood when the fight ended; null when the checkout has no wiki, and in lines from before the sessions could read it |

| `outcome` | Meaning | Counts in the report | Played again by the next run |
| --- | --- | --- | --- |
| `won`, `lost` | The fight was played to the end. The game decides this first: however the session ended afterwards, a finished fight is won or lost | yes | no |
| `abandoned` | The model stopped before the end and stayed stopped: the session ended by itself without an error, was resumed 5 times and ended the same way each time | yes, as not won with 0 HP | no |
| `timeout` | The fight wasn't over when the time limit passed | no, own column | yes |
| `error` | The fight couldn't be played, which says nothing about the model: Claude Code didn't start, couldn't reach the bridge, or offered a session other tools than the fight's own and `Read`; the API failed; the usage limit or extra usage; a resume that failed (Claude Code no longer finding the session, say); or the sessions ended or ran out of time without one call to the fight's tools | no, own column | yes |

**Numbers of a resumed fight.** A fight with `resumes` above 0 has one result line per session, and Claude Code's fields don't all count the same way. Claude Code 2.1.296 describes `total_cost_usd` and `modelUsage` (in the result line's own field descriptions) as running totals that a resumed session takes up again from what its transcript saved, when it saved any, so that its first result line already includes the earlier sessions. `usage`, `num_turns` and `duration_ms` are the process's own. So the runner adds up turns, duration and `usage`, and for the cost and `modelUsage` takes a resumed session's line in place of the earlier ones when that line carries them, and adds it when it doesn't. It tells the two apart by the line itself: it carries the earlier sessions when its cost is no lower than the total so far and its `modelUsage` grew by about the output tokens of its own `usage` or more (nine tenths of them); a session that started from zero reports about the same output tokens in both, so its `modelUsage` didn't grow by that much, and it is added. This was read from the Claude Code binary and tried with the fake CLI both ways, not yet seen on a real resumed session. Each session's result line is in the fight's transcript, so the first real fight with `resumes` above 0 shows whether the cost in its result line is right.

## The report

Each model's fight on a scenario is counted once: the last line that counts (won, lost or abandoned), else the last line there is (a timeout or an error). So a fight played again after a timeout or an error replaces it.

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
- **Cost and time are Claude Code's, not the in-game brain's.** `$/fight` includes Claude Code's own system prompt and its caching, and wall-clock time depends on how busy the service is and on how many fights run at once. Use them to compare models within one run, at the same `--parallel`.
- **The usage windows are the account's.** The percentages a batch prints tell what it took out of the 5 hour and 7 day windows (with whatever else ran on the subscription meanwhile), in whole points, for all its models together. To budget a model, run it alone.
- **The effort is the same for every model** in a batch. Models respond to it differently; compare efforts with separate batches (or separate `--out` folders) on the same seed.

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
| `fake-snoop` | Asking for a page that isn't there, a file outside the strategy folder and the wiki's README, and calling a `Write` tool it doesn't have: `read_errors`, `other_reads`, `Write` in `tool_calls`, and nothing written |
| `fake-writer`, `fake-blind` | An init line that lists `Edit` and `Write`, and one without `Read`: both stopped at once, and the batch starts no more fights |
| `fake-replay` | A real session's saved stream-json output read back, to see what the runner makes of it |

```bash
scripts/arena.sh run --claude src/SpireMind.Arena/Fake/fake-claude.py --models fake-play,fake-seq,fake-pause3,fake-abandon \
  --scenarios 3 --out /tmp/arena-fake
```

With `FAKE_CLAUDE_USAGE` set to a file holding two numbers (`0.05 0.86`), the fake reports those as the 5 hour and 7 day windows and raises them with every session, which is how `--stop-at-weekly` was tried. A resumed fake session carries its cost and `modelUsage` on from the sessions before it, as Claude Code 2.1.296 says it does; `FAKE_CLAUDE_CARRY=0` makes every result line hold only its own process's numbers, and the fight's totals must come out the same either way. Set `CLAUDE_CONFIG_DIR` to a scratch folder to keep the fake's sessions out of `~/.claude` while trying things. With `FAKE_CLAUDE_PROMPT` set to a file, the fake writes there the system prompt and the user prompt it was started with, which is the quickest way to read what a model is told.

The fake answers a `Read` as the arena's flags should make Claude Code answer it: the file's text when it lies under the folder the `Read(//...)` rule names, Claude Code's refusal anywhere else, an error for a file that isn't there. That exercises the runner's counting. It is not a test of Claude Code's permissions, and the fake never writes anything but its own session files. `fake-replay` of a transcript saved before the sessions had `Read` (the pilot's) is stopped at its init line, which lists no `Read`.

The fake plays the first card it may play and answers a card choice by confirming as soon as it can, so it gets through any fight the arena deals, and loses the hard ones.

It spends nothing, and it is how the runner's paths (resume, timeout, grace, the usage limit and windows, a dead MCP server, Ctrl+C) were tested.
