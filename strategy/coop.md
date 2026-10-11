# Co-op (2-4 players) in STS2

Sources disagree on several rules. Items marked (unverified) should be confirmed from the in-game UI or state.

## Rules of the mode
- **Party:** up to 4 players, Steam friends only. Duplicate characters are allowed.
  - The host holds the single save. The roster is fixed for the run (no drop-in).
- **Personal resources:** deck, HP, Block, Energy, gold, potions, relics, card rewards, shop stock and prices, Ancient (Neow, etc.) offers, rest-site choice.
- **Shared:**
  - The map route.
  - The encounters.
  - **Debuffs on enemies.** One player's Vulnerable or Weak helps everyone's attacks and defense.
  - Turn timing: everyone acts in the same window, and the round advances only when ALL players press End Turn.
- **Path choice:**
  - Each player's intended path is shown, and players can draw on the map.
  - At forks, players vote. A weighted random roll settles disagreement (unverified; some guides say "agree or RNG").
- **Events:**
  - Some are group votes, and the game picks if the vote isn't unanimous.
  - Others are per-player choices with per-player outcomes (conflicting sources; read the event UI).
- **Chests:**
  - Each player is offered one relic and players can't take the same one.
  - If two pick the same relic, an automatic rock-paper-scissors decides. The loser gets a different relic.
- **Enemy scaling:**
  - Enemy HP (and some stats) scale with party size, a bit more than linearly.
  - Bosses have bespoke scaling. Example: Vantom's Slippery stacks double.
  - v0.104 reduced how strongly several powers scale in multiplayer.
- **Enemy attacks:** most sources (Wikipedia, Mobalytics, BrokenBuilds) say enemy attacks hit **every player**, and each player blocks for themselves.
  - Intercept ("redirect attacks aimed at another player to you") implies per-target aiming exists. **Read each intent's target(s) from the game state** (unverified).
- **Death:**
  - At 0 HP you're downed for the rest of that fight. If the party wins, you revive after combat at very low HP (1 HP per several sources).
  - The run ends only if everyone falls in the same fight.
  - If you're about to die anyway, spend your last turns on damage.
- **Rest sites** add **Mend**: heal an ally (about 30% of their max HP, unverified) instead of yourself.
  - Miniature Tent lets you Mend and still Smith.
- **Potions:** some (Block Potion, Energy Potion, etc.) can be thrown at an ally. Not all can.
- **Ascension:**
  - Co-op has a separate Ascension track. The party plays at the lowest level unlocked among its players.
  - Winning unlocks the next level for each character present.

## Multiplayer-only cards (they only appear in co-op; text from wiki data, verify in-game)
- **Colorless:**
  - Rally (all players +12 (17) Block).
  - Lift (an ally +11 (16) Block).
  - Intercept (+9 (13) Block, take attacks aimed at an ally).
  - Beacon of Hope (Power: allies gain half your Block gains on your turn).
  - Mimic (Block equal to an ally's Block).
  - Believe in You (an ally +2 (3) Energy).
  - Coordinate (an ally +5 (8) Strength this turn).
  - Huddle Up (all players draw 2 (3)).
  - Tag Team (11 (15) damage; an ally's next attack on that enemy is played twice).
  - Gang Up (+5 (7) damage per ally attack on that enemy this turn).
  - Knockdown (3 cost: 10 (14), and the enemy takes 2× (3×) damage from allies this turn).
  - The Ball (an attack that grows +10 each combat and passes to a random ally).
- **Ironclad:**
  - Tank (Power: you take +50% damage, allies take 50% less).
  - Demonic Shield (lose 1 HP, give an ally Block equal to yours).
- **Silent:**
  - Flanking (the enemy takes double damage from other players this turn).
  - Sneaky (Sly Power: Block when allies attack).
  - Blade Symphony, Concoct, Fade (unverified effects).
- **Defect:**
  - Energy Surge (ALL players +2 (3) Energy, Exhaust).
  - Ignition (an ally channels Plasma).
  - Hibernate, Imitation Learning, One for All (unverified effects).
- **Regent:**
  - Largesse (an ally gets a random colorless card).
  - Constellation (an ally draws, gains Energy and 9 (12) Block).
  - Plot (next turn all players draw 2 (3)).
  - Tutor (an ally tutors a card from their draw pile).
  - Hammer Time (whenever you Forge, all allies Forge).
- **Necrobinder:**
  - Legion of Bone (all players Summon 6 (8)).
  - Glimpse Beyond (all players +3 (4) Souls).
  - Soulbound (an ally gets a Soul when you create one).
  - Underworld (allies' attack damage this turn is also applied as Doom).
  - Cacophony (every 33 cards drawn by the party, 66 (99) damage).
- **Neow in co-op** can offer *Massive Scroll*: pick 1 of 3 multiplayer cards.
- Several multiplayer cards were nerfed in v0.109. Many more were added in v0.108.

## Roles (soft; let the cards you're offered decide)
- **Debuffer / support:** applies Vulnerable and Weak early in the turn for everyone.
  - Examples: Bash, Tremble, Dominate (Ironclad); Neutralize, Leg Sweep, Malaise (Silent); Falling Star, Know Thy Place, Comet (Regent); Putrefy, Fear (Necrobinder); Go for the Eyes, Null (Defect).
- **Protector:** an Ironclad with Tank, Intercept, Rally or Beacon of Hope, or a Necrobinder with a big Osty and Legion of Bone. Soaks the big hits and fills allies' block gaps.
- **Damage carry:** the deck with the best scaling engine gets the damage relics and Coordinate, Believe in You and Tag Team targets.
- **Everyone still needs their own block.** Personal Block isn't shared unless a card says so.

## Your turn with a teammate
- **They act in the same window as you.** While you plan, a teammate can give you Block or Energy, buff you, debuff an enemy, hit your target or kill it. A plan made from one snapshot can be stale two cards later.
- **Decide Block last, from a fresh snapshot.** A teammate's Lift, Rally or Block Potion changes how much you still need. Play your attacks and debuffs first, then look at your Block and incoming_damage again before you spend cards on defense.
- **Keep sequences short while a teammate hasn't ended their turn** (team[].ended_turn is false). A few cards, then look again. Once everyone else has ended, the board only changes from your own plays and a whole-turn sequence is safe.
- **When act_sequence stops with "A teammate acted: ...", re-plan.** It says what changed (your Block 0 -> 11, an enemy gone) and returns the fresh snapshot in next. Plan the rest of the turn from that snapshot, not from your old plan. An act sent on an older snapshot is refused the same way.
- The game side is adding a field to the snapshot that lists what teammates did since you last looked. Read it when it shows up.

## How a good AI teammate behaves
1. **Complement, don't copy.**
   - Before each card pick, check what the humans' decks lack (AoE, block, draw, debuffs, Artifact strip, a boss answer). Draft to fill that gap.
   - Don't take a card that adds copies to every player's discard pile unless it actually helps.
2. **Sequence for the team.**
   - Play shared debuffs (Vulnerable, Weak) at the START of the turn so human attacks benefit.
   - Wait for an ally's Vulnerable before your big hits when you can. Turns resolve in play order.
3. **Call targets.** One short message before key turns: the target, who covers the big hit, and which held cards or potions change the math.
   - Say lethal per enemy, not as a team total.
4. **Don't steal kills that matter.**
   - Leave the finishing hit to the player whose card cares about it: Feed (+max HP), The Hunt (extra reward), Knockout Blow (+5 Stars), Echoing Slash chains, and any "if this kills" effect.
   - Don't waste your damage into an enemy an ally will already kill. Overkill is wasted.
   - Don't execute an enemy that a teammate's Doom or poison already kills at end of turn, unless its last attack is lethal to someone.
5. **Protect the fragile and the downed.**
   - Use Lift, Intercept, Rally, or a Block Potion on an ally facing lethal.
   - If an ally is downed, end the fight quickly. They revive after the win.
6. **End Turn promptly** once your plan is done. Don't end before allies have applied debuffs you intended to exploit.
7. **Map votes:** vote for the route the WEAKEST deck can survive. Add a campfire when anyone is fragile.
   - Before shops, tell allies what you plan to buy (gold isn't shared, but plans are).
8. **Rewards:**
   - Chest relics go to the deck that converts them best. Pick a different relic from the human's choice to avoid the RPS roll.
   - Ice Cream goes to the X-cost player. Damage relics go to the carry.
   - Mend the ally who is lowest and most critical before an elite or boss. Smith yourself otherwise.
9. **Group events:** on group votes, follow the safest consensus unless a clearly superior option exists. Explain your vote in one line.
10. **Don't hoard potions.** A thrown Block or Energy potion that saves an ally's turn is high value.
11. **Respect the human.**
    - If the human calls a plan, follow it unless it is provably lethal. Then say why in one line.
    - Never take actions on the human's behalf (their cards, their rewards).

## Planning sessions

- The mod runs short planning sessions: at the start of each act while the Ancient's offer is up, around the middle of the act, and before the boss. Mark can start one with /plan and end one with /plan skip.
- While one runs, your card, relic, shop and rest picks are held. wait_for_decision returns when it is your turn to speak, with `party` in the result: what to answer, the pitches so far and Mark's lines. Answer with one party_pitch call, then call wait_for_decision.
- Pitch a lane that fits your character and your offers and complements the others: one deck carries damage, one applies Weak and Vulnerable early, someone can protect. Two decks chasing the same cards both end up weak.
- Mark's orders outrank the party plan, and the plan outranks your own preferences. If Mark says what he is building, build around it.
- During a session, answer Mark's lines in your pitch rather than with a separate say, unless he asked you something by name.
- Ask Mark at most one short question per session, in your pitch. If he doesn't answer before the final round, go with your best guess and say what you guessed.
- After a session, pick screens carry `party.yours` (your part of the plan) and `party.calls` (the party's calls). Check them before every card, relic and shop pick. On the map, vote the plan's route call only when Mark hasn't voted after the wait; once he votes, follow him.
