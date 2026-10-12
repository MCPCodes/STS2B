# Opus review: what to change so it plays better

Source: six analysts' reports on Opus's 40 pilot fights (seed 7, medium effort, 33 wins, 7 losses), checked against each other, against the replay tool, and against two more Opus runs on the same 40 fights that the reports barely used: high effort, and medium effort with the wiki readable. 17 fights were read turn by turn (all 8 bosses, 6 elites, 3 hallway fights). "Points" are points of win chance with the scripted Lookahead player finishing the fight on reshuffled copies.

## 1. Bottom line

- Inside a turn Opus has nothing to fix. Over 162 turns: 0 HP lost to an unplayed Block card, 0 missed kills, every stated sum right. Its turn was the best of all possible turns on most turns scanned.
- Every costly mistake is a choice across turns: a lasting cost paid for a one-turn gain, or a race not counted. 14 tested mistakes in 11 of 17 fights. 9 could be avoided from what it saw. 5 needed facts it did not have.
- 2 of the 7 losses were lost before turn 1 (0% for any fair player), a third nearly (3%). Of the 4 open boss losses, 3 had one cross-turn mistake worth 13 to 22 points each; 1 had none.
- The same deals played three times show the good choices are not stable. Each run got some of the key choices right and some wrong (table in section 3). Rules have to make the good choice repeat.
- High effort won 36 of 40 against 33, at 1.5 times the cost. That is 4 fights gained and 1 lost: inside the noise. It did not fix the two mistakes that repeat.
- Two levers look larger than anything inside the fight, and the single-fight arena cannot measure either: what is brought to the boss (a potion is worth about 25 points there, 1 HP about 1.7) and which fights are entered at all.

## 2. Checks made on the reports

Re-run for this review (outputs in `synth/`):

| Claim | Analyst's number | Re-run | Verdict |
| --- | --- | --- | --- |
| boss-170286 T4: Defend, Defend, Cinder beats Opus's line | +13 ±3 (K=400) | +16 ±4, 53% against 37% (K=300, new seed) | holds |
| boss-548112 T3: Primal Force beats Evil Eye | +17 ±7 and +35 ±6 | +13 ±7 (new seed). Pooled, 300 play-outs: 66% against 45% | holds at about +22 |
| boss-878005 T4: kill Follower e1, not all-in | 80% against 7% | 84% against 7%; Greedy 73% against 0% (K=150, new seed) | holds |

Dropped or cut down:

- "Cruelty over Pillage is +45 points" (boss-243639 T4). Lookahead rates Opus's real, winning line at 8% there, so the 45 is the scripted player's weakness. Kept: +7.6 HP ±1.8 on average, up to 26 HP on the real deal (that needed Armaments in the same hand).
- "None of the 7 losses was thrown" (stats). True for one-turn measures only. Three open boss losses each had one cross-turn choice the one-turn trace cannot see.
- "No scripted player beat a boss, Opus beat 3." Lookahead wins 51% of these 8 fights on reshuffles. Opus over three runs won 12 of 24. An edge over the search on bosses is not shown.
- Dropped as noise or hindsight: Beast T10 Armaments (+2 to +4, ±2 to 3), boss-878005 T3 Uppercut (+13 ±5, loses the real deal), boss-53637 T3 Attack Potion (+0.3 ±2.5 on reshuffles).
- Weak, kept as notes only: Bludgeon at once against the Beast (20% against 17%), the Phrog wiki tip (one test), Ashwater on all 5 cards (+7 to +11, ±4).
- Arena scoring, not play: Blood Potion kept at an elite kill (16 HP), Potion of Binding kept at a boss kill (8 HP), Swift Potion kept (14 HP). In a run these keeps are right. The reverse also holds: throwing 24 of 25 hallway potions cost nothing in the arena and would cost in a run.
- "More thinking gives better turns" is mixed up with harder turns drawing more thought. The direct test exists: see section 3.

## 3. The same fights, three Opus runs

| Decision | Pilot (medium) | High effort | Medium + wiki |
| --- | --- | --- | --- |
| boss-170286 T4: Cinder left for Stampede with only 2 Defends in hand | yes, lost T7 | yes, lost T7 | yes, lost T7 |
| boss-548112 Skill Potion pick | Evil Eye, lost T14 | Primal Force, won 7 HP | Primal Force, won 30 HP |
| boss-878005 Power Potion pick | Rupture, won 11 HP | Hellraiser, lost T9 | Rupture, won 11 HP |
| boss-541818 T1 Barricade (fight is 0% either way) | yes | no | yes |
| boss-504297 Skill Potion | T2, Flame Barrier, lost T11 | T2, Flame Barrier, won 9 HP | T4, Offering, lost T9 |
| boss-243639 the big hit | Pillage, 94, 23 HP | Cruelty + Armaments, 55 HP | Gigantification while Weak, 60, 20 HP |
| elite-780979 Snecko Oil | 2 energy left, 30 HP | 0 energy, 7 HP | 0 energy, 7 HP |
| normal-273726 Flex Potion | T3 on one Strike, 42 HP | T1 with Sword Boomerang, 50 HP | same, 50 HP |
| elite-717141 Ashwater | kept 1 Defend, lost | all 5 cards, won 10 HP | lost T4 |
| **Wins, mean HP left** | 33/40, 43.5 | 36/40, 45.4 | 34/40, 44.5 |
| Bosses, elites | 3/8, 6/8 | 5/8, 7/8 | 4/8, 6/8 |
| Cost, time per fight | $18.80, 47 s | $28.16, 128 s | $20.42, 54 s |

HP left against the pilot, fight by fight: high effort +1.9 ±1.3 (bosses +5.8 ±4.3, elites +1.6 ±4.0, hallway +0.8); wiki +1.0 ±1.2.

## 4. The 7 losses

| Fight | At the door | Search wins from T1 | Tested mistake | Cause |
| --- | --- | --- | --- | --- |
| elite-943840 Effigy | 55/80, starter-level deck, no potion | 0% | none: reached the best result the deal allows | lost at the door |
| boss-541818 Vantom | 63/80, Barricade and 4 Defends, no potion | 0% | Barricade T1, Cinder order T5: 23 boss HP, 0 points | lost at the door |
| elite-717141 Effigy | 38/80, 2 potions | 3% | Ashwater kept a Defend: +7 to +11 ±4, not knowable | door, then a 6.3% draw |
| boss-548112 Vantom | 75/80, 3 potions | 27-30% | Evil Eye over Primal Force: about 22 points | avoidable |
| boss-170286 Vantom | 62/80, 2 potions | 33-39% | Cinder exhausted a Defend on the free turn: 13 to 16 points | avoidable, then a 30% draw |
| boss-504297 Beast | 69/80, 1 potion | 50-52% | Skill Potion opened T2 to block an 18 hit: 18 to 22 points | partly knowable, then a 2.4% draw |
| boss-206278 Vantom | 78/80, 1 potion | 60% | none in 13 other lines tested | the deal |

The three costs in the open fights add up to about 0.55 of a win. Opus's closing lines named none of the three, and blamed four choices the simulator rates as its best (boss-206278 T1 and T3, elite-943840 T1, Beast T3).

## 5. Mistake types

| Type | Fights | Tested cost | Could Opus know? |
| --- | --- | --- | --- |
| No two-turn count before an all-in | 1: boss-878005 T4 (won) | about 50 points (84% against about 1 in 3); 0 on the deal | Partly. Draw pile was shown; the next enemy turns need the cycle table. |
| Card picked in a fight judged for this turn only | 1: boss-548112 T3 | about 22 points, and the fight | Yes. All three texts and the hand were shown. No rule covers it. |
| Potion on the wrong turn | 3: boss-504297 T2, boss-243639 T3, normal-273726 T1 | 18-22 points; 9-14 HP; 12 HP | Partly; no (needs the damage table); yes. |
| Random exhaust not counted (Cinder) | 2: boss-170286 T4, boss-541818 T5 | 13-16 points; 12 damage and a Stomp | Yes. The learned.md Stampede line pointed at it. All 3 runs did it. |
| Setup or small damage over the multiplier | 2: boss-541818 T1, boss-243639 T4 | 23 boss HP; 7.6 HP (26 on the deal) | Yes. The notes call Barricade a trap, under drafting. |
| HP or Block spent for nothing | 2: boss-53637 T5, weak-954058 T1 | 4 HP; 0.5 HP (6 and 7 on the deal) | Yes; no (the notes asked for the third Defend). |
| Random card played last or skipped | 2: normal-273726 T1, elite-803170 T2 | 1.3-2.6 HP; 3.3 HP | Yes. The play prompt already says so. |
| Ashwater kept a Defend | 1: elite-717141 T3 | 7-11 points ±4 | No. Needs play-outs. |

No tested mistake in 6 of 17 fights: boss-206278, elite-943840, elite-768161, elite-780979, and (scoring effects only) elite-840113, normal-863794.

## 6. Three causes

1. **Lost before it started: 2 fights for sure, 1 nearly.** elite-943840 and boss-541818 had no winning line with every draw known, and all three Opus runs lost both. This is a deck, HP and potion problem. The arena's decks come from a generator, so the pilot says nothing about how well Opus drafts or paths.
2. **Avoidable from what it saw: 9 mistakes.** The pick, both Cinder turns, Barricade, Pillage over Cruelty, Flex on one Strike, Sword Boomerang last, Havoc skipped, Breakthrough for damage not needed. Worth about 35 points in two lost bosses plus about 30 HP across wins. A prompt problem: effort did not fix the Cinder turn, and each run missed different ones.
3. **Needed facts it did not have: 5 mistakes.** Shackling timing (damage by turn), the all-in (next two enemy turns: 27 and 22), the third Defend (Nibbit pattern), Skill Potion timing (offer unknown), the Ashwater pick (play-outs). The first three facts are in the wiki, which these fights could not open. The wiki run did time Shackling right and still played the all-in, so facts alone are not enough.

The deal comes on top: the forced 30% draw on boss-170286 T7, 5 Skills on the Beast's free turn (2.4%), no Block card on elite-717141 T4 (6.3%).

## 7. Ranked plan

Gains for 1 to 4 are predicted from tested positions; none has been played by Opus yet. Together, if followed every time, at most about +1 win per 8 bosses (0.5 + 0.22 + 0.15 + 0.2). Items 5 and 6 are the only ones measured in whole fights, once each. Ranks 7 and 8 may be worth more than all of 1 to 6 in a real run, but nothing here measures them, so they sit below what is tested.

**1. Boss and elite plan line, with the damage tables in the notes.** Largest single tested gain; one fight.
- play.md, after Loop step 2: "In an elite or boss fight, on turn 1 and on every turn the enemy does not attack, write one line before you play: the enemy's damage over its next 3 turns, your HP, the Block cards still in your deck, and the turn you expect to kill. If the kill comes after the next big hit, keep the Block cards for that hit. Under 20 HP, before a turn with no Block, count the damage you are sure to deal this turn and next from your hand and draw pile, not counting random targets. If that does not kill, do not race: kill or weaken what attacks next turn. Fairy in a Bottle is a safety net, not HP to spend."
- bosses-and-elites.md, always in the prompt: Vantom by turn 7, 6x2, 26, 0, 9, 8x2, 28, 0, 11, 10x2, 30, 0. The Kin, all alive: 13, 19, 17, 7, 27, 22, 24, 23. Effigy: 0, 0, then 23 a turn; 7 turns to kill means 92 taken.
- Changes: boss-878005 T4 (84% against about 33%), boss-170286 T4 (53 coming against 38 HP), boss-243639 T3.
- Test: positions boss-878005 T4 and boss-170286 T4. Must not change: elite-840113 T2 (turtling is -32 points), elite-780979 T3, Beast T3 (Bludgeon with no Block beat blocking, 20% against 7%).
- Risk: timid play. Max-Block lines would save 210 HP and give up 545 damage. Keep the rule to its trigger.

**2. Cards picked inside a fight.**
- play.md or general.md 7: "A card you take inside a fight (Skill, Attack, Power or Colorless Potion, Infernal Blade) stays in your deck until the fight ends. Read every offer before you decide what you need. For each, count what it gives over the turns left, not only this turn, and name the card in your deck or the enemy rule it works with. In an elite or boss fight take the one that shortens the fight, unless another is the difference between living and dying this turn."
- Wiki: "Primal Force: cost 0, not exhausted; Rocks stay for the fight; 16 for 1 energy." In the arena a held Skill Potion offered the same 3 cards on turns 1 to 4; check the game code before that goes in the wiki.
- Changes: boss-548112 (45% to 66%; both runs that took Primal Force won). Guards boss-878005: Rupture 78%, Hellraiser 30%, Stone Armor 0%.
- Test: 5 runs each of boss-548112, boss-878005, boss-170286; count picks of Primal Force, Rupture, Stampede. Risk: low. Beast T2 shows the exception is real (Flame Barrier 41%, Offering 1-2%).

**3. Random exhaust, and the Stampede line in learned.md.**
- learned.md line 7, add: "If that attack is Cinder, it then exhausts a random card still in hand. First play every card you want to keep, Defends too, even on a turn the enemy does not attack. Never leave Cinder alone with Defends."
- play.md step 2: "Before a card that exhausts at random (Cinder, True Grit), list what will be in hand at that moment: each card has a 1 in N chance to go. Play the cards you must keep first."
- Changes: boss-170286 T4 (+13 to +16; wrong in 3 of 3 runs), boss-541818 T5 (1 in 3, not 1 in 2, to lose Stomp).
- Test: position boss-170286 T4; right answer Defend, Defend, Cinder. Risk: none seen. Only that exact line gains; the two other ways to keep Defends tested worse (-7, -14).

**4. Potion-use rules** (general.md 7).
- "A potion that adds a card: open it at the start of a turn, before any card. Do not open it to block a hit you can afford (elite or boss, above half HP)." Beast: 40% opened on T2, 58% held, 62% opened at the start of T3 (700 copies). Not confirmed on the real deal: the wiki run held it and lost.
- "Flex and Strength potions add to every hit: use them on the turn with the most hits, never on one attack unless it kills." normal-273726: +12 HP.
- "Gigantification goes on your biggest multiplied hit: Vulnerable target, after your Strength cards, never while Weak. Write the number first." 94 and 108 against 60, 49, 27.
- "Snecko Oil: drink it with 2 or more energy left, on a low-damage turn, after the cards whose cost you already like." 88% against 31%; 30 HP against 7 HP in the other two runs.
- "Shackling, Speed and Block potions go on the biggest of the enemy's next 3 turns." boss-243639: 9 to 14 HP.
- Test: positions Beast T2, normal-273726 T1, elite-780979 T2, boss-243639 T3. Risk: 5 more lines of prompt.

**5. High effort on elites and bosses only.**
- Measured on the same 16 fights: 12 wins against 9 (4 gained, 1 lost), HP +5.8 ±4.3 on bosses. Hallway fights gained 0.8 HP, so leave them at medium.
- Cost on those 16: $15.75 against $9.42, 3.7 times the output tokens, 212 s a fight against 72 s.
- It found Primal Force and the Cruelty line. It took Hellraiser, drank Snecko Oil at 0 energy, and repeated the Cinder turn.
- Test: 40 seed-7 bosses, medium against high, both with rules 1-4; compare with Lookahead's reshuffle share per fight. 8 bosses cannot show it.

**6. Enemy facts in front of it: wiki page at fight start, forecast in the snapshot.**
- Arena: allow reading `strategy/wiki` as the real bot can. Measured: 34 wins against 33, +1.0 HP ±1.2; it fixed Shackling timing and the Primal Force pick, not the all-in or the Cinder turn.
- Snapshot, fixed-cycle enemies only: `next_moves: ["attack 9", "attack 8x2", "attack 28 + 3 Wounds"]` and `incoming_next_3: 53`, marked "if nothing changes". Also `end_of_turn: ["Stampede plays Cinder (only Attack in hand)", "Cinder exhausts 1 of: Defend, Defend"]`, and `next_hand_known` when the draw pile has 5 cards or fewer.
- Risk: a wrong forecast is worse than none. Start from the wiki tables, which come from the game files.

**7. Potion economy in a run, and a chained arena scenario to test it.**
- With Lookahead playing: a potion at the boss is about +25 points; the same potion in a hallway fight saves about 5 HP, worth about 8 points at the boss door. Opus threw 24 of 25 hallway potions because the notes say "Act 1: use freely".
- Proposed general.md 7: "Act 1: keep your two best potions for the boss. In a hallway fight use one only when the belt is full, when it saves about 15 HP, or when the fight can kill you. Dying with potions is still the worst outcome."
- Test first: arena scenario of 1-2 hallway fights then the boss, HP and potions carried over, scored on the boss. Risk: the numbers are the search's, not Opus's.

**8. The door: fight forecast before elite nodes, measured card values for drafting.**
- Tool: Lookahead's win share for the current deck, HP and potions against the act's elites and boss (seconds for 100 deals). elite-943840 reads 0 of 400. Rule: "Under about 25%: other path, rest first, or bring a potion."
- Card values: add a card to standard decks and report the change in boss and elite win share on the card's wiki page.
- Limits: Lookahead underrates Opus (elite-717141 read 3%; Opus made it 54%), and it underrates plan cards. Needs the run simulator to test. Possibly the largest gain here; unmeasured.

**9. Lessons kept only when confirmed.** Rule: "After a loss, name a turn or card as the mistake only if a replay what-if confirmed it; otherwise write what happened and mark it untested." Offline: turnscan each loss, keep a lesson only at 2 standard errors on rerolled copies. Evidence: the closing lines above, and the learned.md line behind mistake 3.

**10. Tools.** Let `act_sequence` run through the choice its own card opens, and make `version` required on `act`. Saves about 45 calls per 40 fights and closes the unchecked second `act`.

**11. Small note fixes.** general.md 8.4 and 8.12: no third Defend that wastes most of its Block when an attack is playable (0.5 HP). Havoc before a plain Strike when the draw pile is mostly attacks (3.3 HP). ironclad.md Beast line: do not hold Bludgeon for Ringing before 150 HP.

**Do not do.** A calculator that returns the best line for the current hand: the in-turn leak is 0 HP, and the one-turn search rated Opus's two winning plans at -75 and -35 and lost both fights when it took over. More rules on energy, Block or kill order. A general "block more" rule.

## 8. What Opus does well: do not train it out

- Clean turns: 0 HP lost to unplayed Block, 0 missed kills, about 40 stated sums all right, re-reads after 40 of 46 draws.
- Plans over 2-3 turns that the search cannot follow: Gigantification held for hits of 94 and 108; Shackling to buy a free setup turn; Snecko Oil on the Effigy's last idle turn (+55 points).
- Hits the leader when the rest are Minions: both Kin fights won on turn 6 with the Followers alive. The search killed Followers and lost on turns 10 and 11.
- Races when racing is right: 2.6 enemy damage kept per HP paid (Sonnet and Haiku 1.6); the Byrdonis kill on turn 3; Bludgeon with no Block against the Beast.
- Setup on quiet turns: Stampede on turn 2 (-19 points without it), Pyre and Stampede+ on turn 1 (60% against 11-32%).
- Slippery: counts hits, holds big cards and damage potions until it is gone, puts Weak and its best Block on the big hit.
- Potions under pressure: Swift Potion on 3 known cards and Ashwater before the reshuffle (17% to 54%), Speed held for the 28 hit (+19 points), Stampede from the Power Potion (20% to 80%).
- Kills what attacks this turn; counts lethal from the visible draw pile.
- It follows its notes to the letter. That is why a wrong note costs: the Stampede line, "zero-damage lines", "use freely".

## 9. How to test

1. Add a position test to the arena: replay a recorded fight to turn N, then hand the turn to a new session. 8 positions, 5 runs each, old prompt against new: boss-878005 T4, boss-548112 T3, boss-170286 T4, boss-541818 T1, boss-504297 T2, boss-243639 T3, normal-273726 T1, elite-780979 T2. Score the line played with turnscan or reroll. Until this exists only the turn-1 positions can be tested, with whole fights.
2. Guard against harm with whole fights: 40 seed-7 bosses and the 8 elites, old against new. Report each fight's reshuffle win share next to W/L. One deal says little: 40 bosses have a standard error near 8 points.
3. Let arena fights read the wiki, so the test matches the real bot.

Files: this folder's `vantom1`, `vantom2`, `beast`, `leaks`, `strengths`, `stats` (each has PROGRESS.md and out/), `TOOL.md`, and `synth/` (compare.py for the three runs, the three re-runs, replays of the high-effort run).

## 10. Notes from applying items 1 to 4 and 10 (cloud, 692a460)

- Item 3's learned.md sentence is not in the repo: this checkout's `strategy/learned.md` has no line 7. Add it to the Mac's local copy before running the new arm, or item 3 is only half applied.
- Section 6 gives the next two enemy turns after boss-878005 T4 as 27 and 22. In The Kin's table those are enemy turns 5 and 6; enemy turn 4 deals 7. Worth checking how the arena counts turns.
- Item 4's Strength line was narrowed to Flex Potion: per `wiki/potions.md` Strength Potion lasts the fight, so the existing "buff potions before attacks" line covers it. Item 2 drops Infernal Blade (no offer to pick from) and says the card stays for the rest of the fight unless it exhausts, costing 0 only this turn.
- The prompt hash changes with play.md step 3 (act_sequence through hand picks), so old and new arms differ in that line too.

## 11. Notes on the position-test result (cloud, after the 40 + 40 runs)

The result (the four cross-turn rules did not change the lines Opus takes, and it skipped the "write one line before you play" step) points away from more prompt text. Thoughts on the next arms, for the Mac to weigh:

- **Effort is already on in the game.** The live API brain plays elite and boss rooms at high effort (`BrainSettings.EffortFor`, default high for both), which is what fixed three of these moments on 2026-10-10. If the position runs were at medium, an arm at high effort (old and new notes) would show whether the rules matter once Opus has the time, and how far the game already is from the arena result.
- **A `plan` field, if the snapshot forecast alone does not move the plays.** Proposed shape, so the tool schema stays the same in every room (it is part of the cached prompt):
  - `act` and `act_sequence` gain an optional string `plan`.
  - In an elite or boss fight (`state.run.room` Elite or Boss), the first `act` or `act_sequence` of each player turn without a `plan` of at least about 40 characters is refused once that turn: "Elite or boss turn: put your count in plan first: the enemy's damage over its next 3 turns (next_moves), your HP, the Block left in your deck, the turn you expect to kill." A second call that turn runs with or without it, so a refusal never loops.
  - The plan is logged with the turn (transcript and fights.jsonl), which also gives the post-mortems something real to read.
  - BotTools takes a switch for it (off by default), so the arena can run it as an arm through the same code; the game turns it on only if it wins.
  - Cost: at most one extra request per elite or boss turn, usually only the first time in a conversation.
- **Prompt length as its own arm.** "Agreed with and then not applied" may get worse as the notes grow. A trimmed general.md (combat rules only, no draft and event advice in fights) against the full one would say whether the length itself costs decisions.
- The package-counting and setup-card rules (general.md 8.8 and 12) are prompt rules too, so the same caveat holds for them. The setup-card rule is the in-fight one; the forecast in the snapshot gives it the numbers it needs without lookups.
- **Mark's rule for every test from here (2026-10-11 23:09 UTC):** run under the game's own conditions, even at more usage: the live effort per room (medium in hallways, high in elites and bosses; `--effort-by-tier` on), the same model, notes, tools and snapshot fields as the installed mod. The position arms above should be rerun that way before any of them decides what ships.
- **Mark, 2026-10-11 23:10 UTC: tests run on the subscription only.** No API credit for tests, not even short confirmation runs on the mod's own brain loop. The gap between Claude Code and the mod's brain is accepted; the API credit is for real play.

## 12. Why the in-game brain thinks so little at high effort (cloud, answer to the Mac's numbers)

Mark's co-op log: boss room 48 calls, 17.8K output tokens (mean 371, max 5,054); elites mean 215; hallway medium mean 154. Claude Code headless at `--effort high` spent 25K to 66K over two or three boss turns.

How `ApiBrain` sends effort today:
- `output_config.effort` at the top level, fixed for a conversation by its first request. Entering an elite or boss room starts a fresh conversation at the room's effort (`BrainSettings.EffortFor`, high by default); leaving it lowers effort with an effort-only system message (beta `mid-conversation-output-config-2026-07-01`) where the model takes one, else a fresh conversation.
- `thinking: {type: adaptive, display: summarized}` on the models that take summaries. The usage's `output_tokens` counts the full thinking billed, not the summary, so the 371 is real.
- `max_tokens` 16,000 at low and medium, 32,000 at high, 64,000 at xhigh and max (a ceiling; unused tokens aren't billed). Nothing else caps thinking, and no prompt line asks for short thinking (the one "keep it short" line is about text outside `say`).

So the low volume is adaptive thinking's own choice, not a cap. Likely causes:
- Every tool call is its own request, and adaptive thinking decides per request. Most requests in a turn follow a tool result that changes little (the next `act` of a planned turn, a state read), so they think little; the mean hides that. The 5,054 max shows it can think hard when it judges a request needs it.
- A model reading a history written with short thinking tends to keep it short (the same reason raising effort starts a fresh conversation, see the `SwitchRoomEffort` comment).
- Claude Code's own wrapper and its tool loop differ, so its "high" isn't comparable.

Switches for the arena's real-brain mode (both off by default, set through `BrainSettings` / config.json; being built on the cloud side now, landing on this branch shortly):
- `turn_plan_note` (bool): in elite and boss rooms, the first actionable snapshot of each player turn gets a short note asking for the whole-turn plan before the first tool call.
- `turn_plan_effort` (a level, e.g. `max`): in elite and boss rooms, the first request of each player turn runs at that effort (an effort-only system message raises it before that user message), and the next request lowers it back to the room's effort. Skipped on models without per-message effort or once the beta is refused. Transcript and fights.jsonl record the effort each request actually used, so tokens per effort can be compared.
- The plain arm needs no code: `elite_effort` / `boss_effort` at `xhigh` or `max`.

Suggested arms on the same positions, once Mark okays API credit (asked 2026-10-12 ~00:10 UTC, after his 23:10 "subscription only" pick, given that Claude Code's "high" doesn't match the game): game default (high); `boss_effort`/`elite_effort` max; `turn_plan_effort` max; `turn_plan_note` on. Report tokens out per turn next to wins, since a raised setting costs real-play money too.

**Mark, 2026-10-12 00:16 UTC (decision card): "Full set, ~$50".** The 8 positions, five runs each, on the mod's own brain (real-brain mode, API credit) at today's setting and one level higher, about $40 to $60. This replaces the subscription-only rule for this set only. The turn-plan arms run only if they fit that budget; anything more asks Mark first.
