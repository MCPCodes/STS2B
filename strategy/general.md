# Slay the Spire 2: General Strategy Rules (read before every run)

Compiled Oct 2026 from community guides, wiki data, patch notes and the AgenticSTS paper (see sources.md).
Game version: v0.107.1 (Major Update #2, Jun 19 2026; Major Update #1 was v0.103.2).
**Rule 0: the in-game card, relic, potion and intent text always beats this file.** Numbers here drift patch to patch.
Baseline: the developer-reported human win rate at Ascension 0 is about 16%. Unscaffolded LLM agents have scored 0 wins at A0.
Adding short, triggered strategy rules like these took one agent from 3/10 to 6/10 wins. They are defaults: when verified facts (rules.md, the wiki, the screen) let you compute a line, the computation beats these defaults.

## 1. STS2 vs STS1: unlearn these assumptions
- **Roster:** Ironclad, Silent, Defect (returning), plus Regent and Necrobinder (new). That is all 5 as of Oct 2026.
  - A 6th character and an alternate Act 2 are announced but not released (unverified).
- **Acts:** 3 acts plus the final boss. There is no Act 4 and no keys.
  - Act 1 is randomly **Overgrowth** or **Underdocks**, each with different enemies, elites and bosses.
  - Act 2 is **Hive**. Act 3 is **Glory**.
- **A10 is the current cap, not A20.** A10 = two bosses back-to-back at the end of Act 3.
- **Ancients replace boss-relic choices and the between-act heal.**
  - Run start: **Neow** offers a choice of relics. Many have a built-in downside (curse, Injury, max-HP loss, losing gold).
  - Start of Acts 2 and 3: an Ancient heals you to full (A2+: only 80% of missing HP), then offers **pick 1 of 3** relics.
    - Act 2 pool: Orobas, Pael, Tezcatara. Act 3 pool: Nonupeipe, Tanx, Vakuu.
    - **Darv** can replace either one. Darv offers classic STS1 boss relics: Ectoplasm, Sozu, Snecko Eye, Velvet Choker, Philosopher's Stone, Runic Pyramid, Pandora's Box, Black Star, Astrolabe, Empty Cage, Calling Bell, Dusty Tome.
  - Act 1 and Act 2 bosses drop no relic: 100 gold, a potion roll and a card reward of 3 Rares. Neow's Lava Rock reads "Act 1 Boss drops 2 Relics".
- **Starter upgrades come from Orobas, not from a boss.**
  - *Touch of Orobas* upgrades your starter relic (e.g. Burning Blood to Black Blood, Cracked Core to Infused Core).
  - *Archaic Tooth* transforms a starter card into its Ancient-rarity version. Community-rated S, a near auto-pick.
- **Ancient-rarity cards** are strong, run-defining cards. They come from Archaic Tooth, Dusty Tome and similar sources.
  - Examples: Corruption and Break (Ironclad), Wraith Form and Suppress (Silent), Quadcast and Biased Cognition (Defect), Meteor Shower and The Sealed Throne (Regent), Protector and Forbidden Grimoire (Necrobinder).
  - Corruption is NOT a normal Ironclad reward in STS2.
- **Enchantments** are permanent positive card modifiers that last the whole run. Many come from Ancients and events.
  - Examples:
    - Sharp / Vigorous / Momentum: +damage.
    - Nimble / Adroit: +Block.
    - Swift: draw on first play.
    - Sown: +Energy on first play.
    - Steady: Retain.
    - Royally Approved: Innate and Retain.
    - Spiral: Replay 1.
    - Glam: Replay once per combat.
    - Imbued: auto-played at combat start.
    - Clone: duplicate it at rest sites.
    - Goopy: +1 permanent Block each play, but Exhausts.
    - Corrupted: +50% damage, but lose 2 HP on play.
    - Slither: random cost 0-3 when drawn.
    - Inky: +damage and Weak.
    - Instinct: double attack damage.
  - Put enchantments on cards you play every fight.
- **Afflictions** are negative modifiers that enemies put on your cards. Example: *Bound* from Chains of Binding (Queen). Assume they last only for that combat (unverified).
  - Not afflictions: Ringing (Ceremonial Beast: play only 1 card) and Tangled (Vine Shambler: Attacks cost 1 more) are debuffs on you that last exactly your next turn.
- **New or changed keywords:**
  - **Sly:** if discarded from hand during your turn, the card is played for free.
  - **Replay N:** played N extra times.
  - **Eternal:** cannot be removed or transformed.
  - **Forge** (Regent): grows Sovereign Blade. **Stars** (Regent): a persistent resource.
  - **Summon** (Necrobinder): raises Osty's HP. **Doom** (Necrobinder): execute at end of enemy turn if Doom >= HP. **Souls** (Necrobinder): 0-cost, draw 2, Exhaust.
  - Defect has a new **Glass** orb.
- **New status cards that punish you if they stay in hand at end of turn:**
  - Beckon (Soul Fysh): 6 unblockable damage.
  - Wither (Aeonglass).
  - Frantic Escape (Insatiable): no end-of-turn effect. Play it anyway: each play adds 1 to the Sandpit countdown, and at 0 you die.
  - Infection (3), Burn (2), Toxic (5): damage that Block absorbs. Soot and Debris do nothing at end of turn.
  - Exact numbers: strategy/wiki/cards/status.md.
- **Quest cards** are unplayable cards that pay off later. *Never remove them by accident.*
  - *Spoils Map*: reach the red X next act for 600 gold.
  - *Dowsing*: transforms into Abundance.
- **Silent starts with 12 cards (5 Strike, 5 Defend). Other characters start with 10.** Base hand is 5 cards. Base energy is 3.
- **STS1 card names that are NOT in STS2:** Limit Break, Heavy Blade, Spot Weakness, Flex, Sentinel, Searing Blow, Reaper (Ironclad). Clash and Entrench exist only as event cards, never in a card reward.
  - If a guide cites these for STS2, distrust that guide.
- **Save & Quit restarts the current fight from turn 1** (a legit game feature). Use it only if your harness and operator allow it.

## 2. Deckbuilding rules
1. **Score each card on four axes: Damage, Block, Draw, Energy.** Take the card that best fixes your weakest axis.
   - In Act 1, until the deck holds 1-2 answers to the act boss (your character file's list, or the fight card in bosses-and-elites.md), take an answer when one is offered, and otherwise front-loaded damage or efficient Block over a skip. After that, if it fixes nothing, **skip**. Skipping is a real, frequently correct option.
2. **Act 1 pick priority:** front-loaded damage > reliable block > draw > scaling.
   - Starter decks cannot kill Act 1 elites. Take 2-3 real damage cards in your first ~4 rewards.
3. **Lean beats big.** Cycle time = (deck size − draw effects) / cards drawn per turn. A 10-card starter cycles in 2 turns.
   - A card that only draws 1 is cycle-neutral. Powers and Exhaust cards don't recycle, so they don't count.
   - Healthy final decks are about 15-25 cards. Above 30 needs a reason (an engine that wants volume, or A6+ removal costs).
   - Don't over-thin to about 8 cards either. You still need damage AND block in every fight.
4. **Block density about 33%.** About a third of the deck should mitigate damage (block, Weak, Strength-down). 20% bricks often. 50% can't kill.
5. **Upgrade density:** aim for 33-50% of cards upgraded by Act 3 at higher Ascension.
6. **Two-phase drafting:**
   - Phase 1: draft generic good cards until a core engine piece shows up (Demon Form, Noxious Fumes, Defragment, Genesis, Reaper Form...).
   - Phase 2: commit. Never abandon a half-built engine for a shiny new one. That leaves two half-engines.
7. **Directional relics are signals.** Commit toward what your relics reward. Examples:
   - Tingsha + Tough Bandages: Silent discard.
   - Snecko Skull or Twisted Funnel: poison.
   - Kunai / Shuriken / Ornamental Fan: attack volume.
   - Data Disk: Focus/orbs.
   - Mini Regent: Stars.
   - Funerary Mask: Souls. Book Repair Knife / Undying Sigil: Doom.
   - Mummified Hand or Frozen Egg: Powers.
8. **Boss DPS check (Act 3):** about 500-600 boss HP over about 8-10 turns means you need about 60 damage per turn.
   - Also expect armor: Aeonglass has 512 HP and gains 33 Block per cycle.
   - If short on damage, buy or draft damage. If over, buy defense, draw or relics.
9. **Upgrade order:**
   - First: cards whose upgrade cuts cost or removes Exhaust/Ethereal (e.g. Body Slam, Mirage, Hologram, Chill, Echo Form, Void Form).
   - Then: key Powers and draw.
   - Then: big attacks.
   - **Never** upgrade Strike or Defend. Remove them instead.
10. **Every deck needs:** single-target burst for elites and bosses, AoE or multi-hit for multi-enemy fights, and an answer to Artifact and Block-gaining bosses.
    - Expose and Know Thy Place strip Artifact. Poison and Doom ignore Block.

## 3. Act checkpoints (fix these before each boss)
- **Act 1 boss:**
  - 2-3 real damage cards. One AoE or multi-hit option. 1-2 upgrades on key cards. Ideally 1 Strike removed.
  - At least 1 elite killed for a relic. HP at least 50-60% at the pre-boss campfire.
  - Look at which boss is coming (bosses-and-elites.md) and draft its counter (your character file, or the fight card there).
- **Act 2 boss:**
  - A scaling plan online: Strength, Focus, Stars, poison, Doom, Souls, Exhaust value...
  - Draw and energy sources, 2-3 elites done, deck mostly thinned.
  - Act 2 bosses are DPS checks. The Insatiable kills you on a timer. Knowledge Demon escalates curses.
- **Act 3 boss:**
  - Consistency over ceiling. Key cards upgraded. A way to clear junk cards (Exhaust or discard) for Aeonglass Withers and Test Subject Burns/Wounds.
  - Enough HP to absorb 2-3 mistakes. At least 2 strong potions saved.
  - A10: plan for two bosses in a row. Scaling resets between them.

## 4. Map and pathing
- At act start, read the boss icon first. Then plan backward from the pre-boss rest site.
- **Map facts:** the boss shows at the top of the map from the start. A rest site sits right before the boss and a treasure room at the act midpoint.
  - Act 1 (both versions): floor 2 Monster; floors 3-6 Monster, Unknown or Shop only (1-5 Monster rooms before floor 7); Elites on 7-9 and 11-15; Rest Sites on 7-9, 11-13 and 16, none on 14-15; floor 10 Treasure; floor 17 boss.
  - Act 1 Overgrowth: hallway fights 1-3 are the weak encounters; from fight 4 on all are regular (Underdocks unverified).
  - An Unknown room is never an Elite. The k-th one of an act (verified for Act 1, A0) is an Event 85, 71, 60, 54, 50% of the time and a fight 10, 19, 25, 28, 29%.
- List the top 2-3 paths. Count elites, rests, shops and unknowns on each. Prefer flexible paths whose branches re-merge.
- **First floors:** fight 2-3 normal monsters before any elite (gold, cards, deck fixes).
- **Elite budget:**
  - Act 1: 1-2. Take more only with a strong deck.
    - Untapped (Flare) suggests 2-3. Ironclad can be greedier thanks to Burning Blood. Silent, Necrobinder and Regent less so.
  - Act 2: 2-3.
  - Act 3: fight elites only while healthy.
  - Skipping all elites is under-powered, not "safe".
- **Take an elite when ALL of these hold:**
  - HP >= about 60-70% (A9+: near full), OR a rest site within 1-2 nodes after.
  - The deck has 2-3 real damage cards plus a block or draw card.
  - You hold a potion, or the elite is a favorable matchup.
- **Default node value:** Rest/upgrade > Shop (with 150+ gold) > Treasure > Event (?) > Monster.
  - Elites are your relic source. Take them when the rule above says yes.
- Visit at least one shop per act, ideally an early one (floors 3-6), since the first removal is cheapest.
- With Winged Boots, save its 3 off-path moves to dodge a bad forced elite or to reach a shop or rest before the boss.

## 5. Rest sites
- **Default: Smith (upgrade).** An upgrade helps every remaining fight. A heal (30% max HP) is one-time.
- **Rest when:** HP below about 40-50% AND the next fights include an elite or the boss, with no healing in between.
  - Never rest at 80%+. Before a burst-heavy boss, resting from below about 60% is reasonable.
- Pre-boss campfire: upgrade the single card that matters most for THAT boss.
- **Other options:**
  - Dig (Shovel): relic.
  - Lift (Girya): +Strength, max 3 times.
  - Remove or Clone (Clone enchant / event relics).
  - Mend in co-op: heal an ally.
  - Miniature Tent lets you take several options.
  - Stone Humidifier (+5 max HP per Rest) and Pumpkin Candle (relit at rest sites) change the math.

## 6. Shops
- Arrive with about 150-250 gold. Decide your top need before entering.
- **Card removal cost:** A0-A5 is 75 gold, +25 per later removal. A6+ (Inflation) is 100 gold, +50 per removal.
  - The first removal is almost always worth it. At A6+, later removals cost about a relic.
- **Priority:**
  1. Removal (Strike first; curses always).
  2. A run-defining or boss-solving relic.
  3. An on-plan premium card (rare or colorless).
  4. Potions to fill empty slots before an elite or boss.
- Relic prices dropped 25 gold in v0.103.2. Gold-generating relics no longer appear in shops.
- Membership Card (50% off) and The Courier (restock, -20%) make shops your main engine. Route through more shops if you have them.
- Don't spend everything on mediocre cards. One great relic beats 3-4 filler cards.

## 7. Potions
- Potions are pre-paid tempo. **Dying with potions is the worst outcome.** You have 3 slots (A4+: 2).
- **Act 1:** use freely. A Fire or Strength potion that ends an elite 1-2 turns early saves large HP.
- **Act 2:** spend on elites and the boss, not hallway fights.
- **Act 3:** keep 2 strong potions for the final boss unless an elite threatens the run.
- Use a potion now if your slots are full and more are coming (a reward screen, a shop).
- **Sequencing:** buff potions (Strength, Focus, Energy) BEFORE attacks. Damage potions to secure kills.
- A potion that adds a card: open it at the start of a turn, before any card. Do not open it to block a hit you can afford (elite or boss, above half HP).
- A card you take inside a fight (Skill, Attack, Power or Colorless Potion) stays for the rest of the fight unless it exhausts, and costs 0 only this turn. Read every offer before you decide what you need. For each, count what it gives over the turns left, not only this turn, and name the card in your deck or the enemy rule it works with. In an elite or boss fight take the one that shortens the fight, unless another is the difference between living and dying this turn.
- Flex Potion adds 5 to every hit this turn only: use it on the turn with the most hits, never on one attack unless it kills.
- Gigantification goes on your biggest multiplied hit: Vulnerable target, after your Strength cards, never while Weak. Write the number first.
- Snecko Oil: drink it with 2 or more energy left, on a low-damage turn, after the cards whose cost you already like.
- Shackling, Speed and Block potions go on the biggest of the enemy's next 3 turns.
- No potion gives Artifact in v0.107.1 (there is no Ancient Potion). Distilled Chaos is burst. Blood Potion heals 20% (Ironclad).
- Potion drops: one roll after every fight. 40% at run start, 10 points down after a drop, 10 up after a miss, never reset between acts. Elites roll with +12.5 points. White Beast Statue guarantees drops. Exact effects and odds: strategy/wiki/potions.md.

## 8. Combat turn procedure (run this every turn)
1. **Read all intents and sum incoming damage.**
   - Count multi-hits (11×2 = 22). The intent number already includes the enemy's Strength and Weak and your Vulnerable: don't add them again. Frail cuts your card Block, not the incoming damage. Remember Thorns-like and end-of-turn effects (Beckon, Burn, Constrict, Disintegration, Wither).
2. **Check lethal on enemies.** A dead enemy deals 0. Prefer the kill that removes the most incoming damage or scaling.
3. **If no enemy attacks this turn, it's a free window.** Play Powers and setup, or all-out attack. Don't block, except against end-of-turn damage that Block absorbs (Constrict on you, Infection or Burn in hand), or to play the cards you want to keep, Defends too, before a random exhaust takes one (play.md step 2).
4. **Block only what's needed.** Never Block more than the incoming damage plus end-of-turn damage that Block absorbs (the exceptions in item 3 aside). Up to that, each Block point saves 1 HP: weigh it against damage that ends the fight a turn sooner. Spend the rest on damage or setup.
   - Exception: early Act 1 hallway fights, when the damage would not heal back (item 12): zero-damage lines are worth a little tempo.
   - Exception: when HP is your binding constraint for an upcoming elite or boss.
5. **Sequence for value:**
   - Draw, 0-cost and setup cards first.
   - Vulnerable before attacks. Weak before the enemy acts. Strength or Focus before multi-hits and orbs.
   - Discard outlets after you've seen your hand (Sly). Pinpoint after Skills. Pounce before an expensive Skill.
6. **Spend all energy.** Unspent energy is wasted, unless you have Ice Cream, Pael's Tears, Convergence-type Retain, or similar.
7. **Kill order:**
   - First: enemies that buff others, summon, revive, or scale fast.
   - Except Minions and enemies that come back (Eye with Teeth): when the fight ends with one main enemy, damage goes there, unless the low-HP count in play.md step 2 says not to race.
   - Then the highest damage dealer. Then the rest.
   - Don't overkill. Split damage when one more hit isn't needed.
   - In 3+ enemy fights, AoE beats focus fire.
8. **Long fights (elites, bosses):** a turn-1 or turn-2 scaling Power is usually right if incoming damage is manageable.
   - Short hallway fights: frontload.
9. **End-of-turn check:**
   - No Beckon or Wither left in hand if avoidable. Frantic Escape has been played to keep Sandpit alive.
   - Retain choices are correct. Know what reshuffles into your next draw.
10. **Delayed damage:**
    - Doom executes at the END of the enemy turn. The enemy still attacks once.
    - Poison ticks at the start of the enemy's turn and is blocked by Artifact. Strip Artifact first (Expose, Know Thy Place, cheap debuffs).
11. **Avoid high-variance all-ins** unless you've verified you survive the next enemy turn. Under 20 HP in an elite or boss fight, count two turns first (play.md step 2).
12. **HP policy:**
    - Burning Blood (Ironclad) heals 6 after every won fight, up to max HP: a won fight that ends within 6 of max HP costs nothing, so stop chasing zero-damage lines there.
    - Act 1 Overgrowth, first 3 fights (the weak encounters, from floor 2; Underdocks unverified): prefer zero-damage lines when the damage would not heal back.
    - Later: trade a few HP to end fights a turn earlier. HP above max is wasted, and Ancients refill you between acts.

## 9. Neow, Ancients and events
- **Neow** (pick 1). Prefer:
  - Strong and safe: removal (Precise Scissors), transform (New Leaf), a Rare (Arcane Scroll; Hefty Tablet = choose 1 of 3 Rares + Injury).
  - Pomander (upgrade 1 chosen card), Phial Holster (+slot, 2 potions), Winged Boots, gold if a shop is near (Golden Pearl 150).
  - Neow's Talisman: Strike 6 to 9 and Defend 5 to 8, on cards you plan to remove. Rank it below Pomander, removal, transform or a Rare.
  - Cautious: Neow's Bones (2 Neow relics + curse) and Leafy Poultice (-12 max HP) on fragile characters.
  - Lava Rock: 2 extra relics from the Act 1 boss, nothing before it.
  - Silken Tress: loses all gold.
  - Large Capsule: adds a Strike and a Defend.
- **Act 2 Ancient:**
  - Archaic Tooth: S, near auto-pick.
  - Pael's Blood (+1 card draw per turn): the safest top pick.
  - Prismatic Gem (+1 Energy, cross-color rewards).
  - Touch of Orobas: S for Defect and Necrobinder, A for Regent, weak for Ironclad.
  - Pael's Claw if Defends stay in the deck.
  - Toasty Mittens for Strength decks.
  - Nutritious Soup only with 4+ Strikes still in the deck.
  - Alchemical Coffer (4 potion slots).
  - Storybook: high ceiling, adds Brightest Flame.
  - Avoid Golden Compass and Pumpkin Candle unless you understand the route cost.
- **Act 3 Ancient:**
  - Diamond Diadem (version-dependent text).
  - Distinguished Cape (-9 max HP for 3 Apparitions: strong).
  - Meat Cleaver (removals + max HP at rests).
  - Big gold (Signet Ring) only if shops remain.
  - Avoid Velvet Choker (6 cards per turn) for combo or Shiv decks.
- **Darv energy relics:**
  - Sozu: fine if you rarely rely on potions.
  - Ectoplasm: fine late, when gold no longer matters.
  - Philosopher's Stone: good for fast decks.
  - Snecko Eye: good with high-cost cards.
  - Black Star: elite-heavy routes. Runic Pyramid: strong.
- **Events:** avoid curses and big HP or max-HP losses unless the payoff is a relic or removal you need.
  - Transform or remove Strikes and Defends when offered. "Leave" is always legal.

## 10. Ascension ladder (stacking)
1. More elites.
2. Ancients heal only 80% of missing HP.
3. 25% less gold.
4. 1 fewer potion slot.
5. Start with Ascender's Bane (curse).
6. Inflation: removal 100, +50 each. (Before v0.103.2 this was Gloom: fewer rest sites.)
7. Scarcity: fewer Rare and upgraded cards.
8. Tougher enemies (+HP).
9. Deadlier enemies (+damage).
10. Double boss at the end of Act 3.

- A6+: thin early, and favor Exhaust as free removal. Expect 24-26 card decks and draft for consistency.
- A8-A9: elites only near full HP. Block value rises.
- A10: you need an engine that rebuilds damage quickly, twice.

## 11. Agent failure modes seen in LLM runs (don't do these)
- Picking a card every reward and ballooning to 30+ cards. Or picking "cute" low-impact cards in Act 1.
- Wasting energy on Block during enemy buff turns. Playing attacks before Vulnerable. Missing lethal.
- Ending turns with unspent energy or playable 0-cost cards.
- Walking into elites at 40% HP. Skipping every shop. Entering the boss without upgrades.
- Resting at high HP. Upgrading Strikes.
- Hoarding potions and dying with full slots.
- Hard-casting Sly cards (costed 2-3) instead of discarding them for free.
- Following stale advice. Re-read the card text. Patches changed many cards (Borrowed Time, Drum of Battle, Shatter, Blade of Ink, Expect a Fight...).

## 12. Committing to a build (the Build plan)
Rules 2.6 and 2.7 say when to commit. This is how to run it, with the plan written in `runs/current.md`.
- **Pick a target lane at Neow.** Read your character file's archetypes and choose the one your Neow pick (and any rare it gave) points toward.
  - With no signal, aim for the lane the file calls most consistent, and stay open until rares and relics point elsewhere.
  - The plan names the lane, the cards and relics that make it work, what to avoid, and what the deck lacks for the act boss.
- **Act 1: flexible power first.** Take cards that are good in any deck (front-loaded damage, efficient block, draw) over narrow payoffs.
  - An on-lane card wins a close call. A payoff with no enablers yet does not.
- **Commit by mid Act 2** (around the treasure room). By then you hold the engine's core, which the character files spell out (often 2+ enablers and a payoff).
  - After that, draft only for the engine and for gaps: block, AoE, an Artifact or boss answer.
  - Pivot late only for a run-defining rare or relic, or a standing order from the human. Rewrite the plan when you do.
- **Skips and removals are build tools.** Every off-plan card makes the engine show up less often.
  - Once committed, skip any reward that is neither on plan nor fixing a gap.
  - Count a Strike or Defend removal as a pick. It is often worth more than an average on-plan common.
- **Check the plan before every card reward, purchase, removal, upgrade and relic choice.** Upgrade the cards the plan leans on.
