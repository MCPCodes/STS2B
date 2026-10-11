# Bosses and Elites (STS2)

**First move each act:** look at the boss icon on the map and draft and path for that fight.
Sections marked unverified give base numbers with ascension values in parentheses, and drift by patch: **read intents and power tooltips in-game.**

## ACT 1A: OVERGROWTH
Verified from the game files, single player, ascension 0. From A8 HP and Vantom's Slippery are higher; from A9 damage, the Beast's Plow threshold and every Strength gain; in co-op enemies scale too (coop.md). There, read the HP bar and the power amounts and redo the sums. Enemy turn N follows your turn N. Full pages: `strategy/wiki/enemies/<name>.md`.
### Elites
- **Bygone Effigy** (127 HP, alone)
  - Enemy turns 1 (Sleep) and 2 (Wake, +10 Strength) deal 0, and damage does not wake it early. From turn 3: Slashes 23 every turn.
  - *Slow*: each card you finish this turn makes your later attack cards hit it 10% harder (card 3 x1.2, card 6 x1.5). Potions and reflected damage get no bonus.
  - Your turns 1-2: no Block. Play Powers, Vulnerable and attacks.
  - Every turn: Skills, Powers and cheap cards first, the biggest attack last.
  - Weak on it cuts 23 to 17. Each turn after turn 2 costs 23 minus your Block. A kill on your turn 7 takes 4 Slashes: 92.
- **Byrdonis** (81-84 HP, alone)
  - Swoop 17, Peck 3x3, alternating, +1 Strength after every enemy turn: 17, 4x3, 19, 6x3, 21, 8x3. No free turn.
  - The next number is known: never Block past it. Each Block point up to it saves 1 HP; weigh that against damage, since each Swoop + Peck pair deals 8 more (29, 37, 45) and stalling loses.
  - Weak rounds down per hit: Peck 6x3 becomes 4x3, Swoop 19 becomes 14. Strength loss is worth 3 per point on a Peck, 1 on a Swoop.
- **Phrog Parasite** (61-64 HP, then 4 Wrigglers of 17-21 HP; 135-142 HP in all)
  - Infect (3 Infections into your discard, no attack), then Lash 4x4 = 16, alternating. Each Infection in hand at end of turn deals 3; Block absorbs it.
  - Killing the Phrog cancels its shown move and the Wrigglers skip that enemy turn. Best on a Lash turn (the 16 never lands); a kill on an Infect turn saves the 3 Infections.
  - The fight ends when all 4 Wrigglers die. e1 and e3 bite 6 while e2 and e4 add 1 Infection and +2 Strength, swapping every turn: 12, 16, 16, 20 with all 4 alive.
  - The Wrigglers appear at once: on the kill turn, kill the Phrog with single-target cards first, then play area damage (4 different HP, check each). An all-enemy multi-hit keeps going onto them after the Phrog dies (from the code, not seen in a fight); a single-target one stops.
  - Then finish one biting pair (e1+e3 or e2+e4), starting with the pair that shows attack: with a pair dead, every other enemy turn has no attack.
### Bosses
- **Vantom** (173 HP, alone)
  - Ink Blot 7, Inky Lance 6x2, Dismember 26 + 3 Wounds into your discard (blocked or not), Prepare +2 Strength. By enemy turn: 7, 6x2 = 12, 26, 0, 9, 8x2 = 16, 28, 0, 11, 10x2 = 20, 30, 0.
  - *Slippery 8*: each instance that removes HP removes exactly 1 and 1 stack, from cards, potions, reflected damage and HP loss alike. After 8 such hits the other 165 HP take full damage.
  - Strip stacks with the cheapest hits first: count hits, not damage. Damage potions wait until Slippery is gone. A big single hit waits only while a cheaper hit can take the stack: with energy left and nothing better to play, it still strips 1. Play Bash when the stacks are nearly gone (its own hit strips one).
  - Turns 4, 8, 12 deal 0: no Block. Turns 3, 7, 11 bring the big hit (26, 28, 30). Weak works through Slippery: Dismember 26 becomes 19.
- **Ceremonial Beast** (252 HP, alone)
  - Enemy turn 1: Stamp, no damage (puts Plow 150 on itself). Then Plow every turn: 18, 20, 22, 24, 26 (+2 Strength each).
  - From your turn 2, the first HP loss from any source (cards, potions, reflected damage) that leaves it at 150 or less (102 damage) wipes its Strength to 0 and stuns it. Turn 1 damage counts, but the trigger waits for the next hit.
  - Triggered on your turn, it cancels the Plow it showed: 2 enemy turns with no damage follow, so get there early. Triggered during its Plow (Flame Barrier), the Plow lands, it keeps 2 Strength, and the stun comes the enemy turn after (from the code).
  - Play no Block on the trigger turn or the turn after. Strength loss before the trigger only helps until the trigger, which wipes it: save lasting Strength loss for phase 2. Vulnerable and Weak stay.
  - Phase 2 repeats Beast Cry (0 damage, *Ringing*: you play only 1 card next turn), Stomp 15, Crush 17 + 3 Strength: 32, 38, 44 per 3 turns. A turn it shows `debuff`: no Block, all damage before the 1-card turn.
  - Each 1-card turn faces Stomp (15, 18, 21). Energy is full and potions are not limited: play your single strongest card, Block or attack.
- **The Kin** (Kin Priest e2 190 HP; Kin Followers e0 and e1, 58-59 HP, Minions)
  - Killing the Priest kills both Followers and wins: 190 HP of work against 307 for all 3. Single-target damage goes into e2 by default.
  - Kill a Follower only when a leftover hit or area damage finishes it, e0 before e1 (e0 deals 6 more per 3 turns), or when the low-HP count in play.md step 2 says not to race. Feed on a Follower gives nothing.
  - Priest: 8 + Frail, 8 + Weak, Beam 3x3, Ritual +2 Strength. All 3 together by enemy turn: 13, 19, 17, 7, 27, 22, 24, 23, 28, 23, 50, 20.
  - Your turns 2, 6, 10 are Frail (card Block x0.75): lean on attacks. Turns 3, 7, 11 are Weak (attacks x0.75): lean on Block.
  - Enemy turns 1 (13) and 4 (7) are the cheap ones to skip Block and set up. End it before enemy turn 11 (50 damage).

## ACT 1B: UNDERDOCKS (unverified, from guides)
### Elites
- **Skulking Colony** (70-75 HP; buffed 3 times through v0.106)
  - *Hardened Shell*: it can't lose more than 15 HP per turn (20 from v0.106).
  - Moves: Zoom (14 + Block; no Block from v0.106), Inertia (9 + Strength), Piercing Stabs (7×2).
  - Play: you can't burst it. Spread damage at about the cap every turn and plan a 4-5 turn fight against rising Strength.
  - Ignore guides that say "burst it". Overflow damage above the cap is wasted.
  - Take it only with 12-14+ damage per turn available plus decent block.
- **Phantasmal Gardener** (×4, 26-31 HP each)
  - Rotation: Bite 5 / Lash 7 / Flail 1×3 / Enlarge +2 Strength, offset per Gardener.
  - The first hit each turn gives that Gardener 6 Block.
  - Play: AoE and multi-hit. Open each Gardener with a cheap hit to pop its Block, then focus one at a time.
- **Terror Eel** (140 HP)
  - Crash 16 (18). Thrash 3×3 + 6 Vigor.
  - Stunned at 50% HP. Right after the stun it uses Terror (99 Vulnerable on you).
  - Play: get it to 50%, then use the stun window to kill it, or block massively for the post-Terror hits.
### Bosses
- **Soul Fysh** (211 HP)
  - Shuffles *Beckon* into your piles. A Beckon (costs 1) in hand at end of turn = 6 unblockable damage.
  - Gains Intangible around turn 4-5. Applies Vulnerable at the end of its cycle.
  - Play: play, discard or exhaust Beckons early while its Strength is low. Deal your main damage on turns 2-3 before Intangible.
  - Don't attack into Intangible.
- **Lagavulin Matriarch** (222 HP)
  - Sleeps 3 turns with 12 Plating (wakes if its Block breaks).
  - Awake: attack, multi-attack, attack + block, then *Siphon*: you lose 2 Strength and 2 Dexterity, it gains 2 Strength.
  - Play: don't break the Plating early. Use the sleep turns for Powers and setup.
  - Race so you see at most one Siphon. Poison, orbs and Doom ignore the Strength loss.
- **Waterfall Giant** (250 HP)
  - Accumulates *Steam Eruption* every turn (starts at about 15-20). Applies Weak on turn 2. Big attack on turn 5.
  - At 0 HP it is stunned and invulnerable. NEXT turn it explodes for the stored Steam damage.
  - Play: kill fast to keep Steam low. Trading HP for speed is right.
  - Before landing the kill, make sure you can block or survive the explosion. Sometimes delay the kill a turn to set up Block or Weak.

## ACT 2: HIVE (unverified, from guides)
### Elites
- **Decimillipede** (3 segments, 40-46 HP each)
  - Each segment cycles Bulk (6 + 2 Strength) / Writhe 5×2 / Outgas (8 + Weak).
  - A killed segment revives at 25 HP while any other segment lives.
  - Play: spread damage so the segments die close together (AoE helps). Finish the last two in the same turn if possible.
- **Entomancer** (145 HP)
  - Fixed rotation: Beeeees! 3×7 / Spear! 18 / Pheromone Spit (Hive stack + Strength).
  - *Personal Hive*: each Attack that hits it adds Dazed to your draw pile.
  - Play: fewer, bigger attacks. Poison, orbs and Doom avoid the Dazed. Kill fast before Strength stacks.
  - Block and Thorns are strong vs the 3×7.
- **Infested Prism** (200 HP, the biggest Act 2 elite; reworked in v0.106)
  - Fixed 4-move cycle: Jab 22 / Radiate 16 + 16 Block / Whirlwind 9×3 / Pulsate (20 Block + 4 Strength).
  - Play: time burst for the turns after its Block expires. Expect a long fight. Bring a potion.
  - *Vital Spark* grants Energy on the first Attack hit each turn (unverified wording, read it).
### Bosses
- **The Insatiable** (321 (341) HP)
  - Turn 1: applies *Sandpit 4* to you (it ticks down 1 per turn; **at 0 you die instantly**) and adds 6 Frantic Escape.
  - *Frantic Escape*: costs 1. Playing it raises Sandpit +1 and raises its own cost each play.
  - Cycle: Thrash 8×2, Thrash 8×2, Lunging Bite 28 (31), Salivate +2 (3) Strength.
  - Play: pure DPS race. Play Frantic Escapes early while they're cheap. Never let Sandpit reach 0, so track it every turn.
  - Slow scaling decks and Frost-tank lose. Use potions.
- **Knowledge Demon** (379 HP)
  - Turn 1 (and after each cycle, up to 3 times): you choose a curse.
    - 1st: Disintegration (6 end-of-turn damage) or Mind Rot (-1 draw).
    - 2nd: Disintegration escalates, or Sloth (max 3 cards per turn).
    - 3rd: Disintegration escalates, or Waste Away (-1 Energy).
  - Moves: Slap 17, Knowledge Overwhelming 8×3, Ponder (11 + heals 30 + 2 Strength).
  - Play: be aggressive and kill before the 3rd curse.
  - Usually take Disintegration early: blockable chip beats losing draw or energy. Pick whatever hurts YOUR deck least.
  - Respect the 8×3 turn.
- **Kaiser Crab** (Crusher about 199 HP, left; Rocket about 189 HP, right)
  - Crusher: Thrash 12, Bug Sting 6×2 + 2 Weak + 2 Frail, Adapt +2 Strength, Guarded Strike 12 + 18 Block.
  - Rocket: Targeting Reticle 3, Precision Beam 18, Charge Up, **Laser 31 (35)**, Recharge.
  - Attacks from behind deal +50% (facing mechanic; read tooltips).
  - When one claw dies, the other gets 99 Block and +5 Strength for a turn.
  - Play: usually kill Rocket first (its Laser is the biggest hit). Turtle on the turn after the first claw dies.
  - Considered the easiest Act 2 boss.

## ACT 3: GLORY (unverified, from guides)
### Elites
- **Knights** (Flail Knight 101 HP, Spectral Knight 93 HP, Magi Knight 82 HP; presumably fought together, unverified)
  - Flail Knight: Ram 15 / Flail 9×2 / Breaker +3 Strength.
  - Spectral Knight: *Hex*, so while it lives ALL your cards are Ethereal. Then Soul Slash 15 / Soul Flame 3×3.
  - Magi Knight: *Dampen*, so while it lives your cards are downgraded. Then Ram 10 / Prep / **Magic Bomb 35 (40)**.
  - Play: kill Magi before Magic Bomb if you can. Spectral is the next priority if Ethereal is wrecking Retain or Power setups. Use AoE.
- **Mecha Knight** (300 HP, 3 Artifact)
  - Charge 25 → Flamethrower (4 Burns into your hand) → Windup (15 Block + 5 Strength) → Heavy Cleave 35 (40) → repeat.
  - Play: strip Artifact with cheap debuffs. Exhaust or play around Burns. Big block on the Heavy Cleave turn.
- **Soul Nexus** (234 HP)
  - Random moves, never the same twice in a row: Soul Burn 29 / Maelstrom 6×4 / Drain Life 18 + 2 Vulnerable + 2 Weak.
  - Play: every turn is an attack, so keep block steady and race. Potions are fine here.
### Bosses
- **The Queen** (400 HP) + **Torch Head Amalgam** (199 HP)
  - Queen turn 1: *Chains of Binding* (only some drawn cards are playable; afflicts cards with Bound).
  - Queen turn 2: Frail, Weak and Vulnerable on you. She also gains Strength and 20 Block per turn while the Amalgam lives.
  - Amalgam: heavy attacks, about 18-19 ×2, then 8×3.
  - After the Amalgam dies, the Queen attacks about 3-4 ×5 and 15 (18) and gains Strength. The Amalgam flees if the Queen dies.
  - Play: either kill the Amalgam first (stops its damage and the Queen's Block), or burn the Queen with Block-ignoring damage (poison, Doom).
  - Card draw and Retain mitigate Chains. Rated the hardest Act 3 boss.
- **Test Subject** (100 / 200 / 300 HP phases; stunned 1 turn between phases)
  - P1: gains 2 (3) Strength per **Skill you play**. Attack, then attack + Vulnerable.
  - P2: unblocked hits add Wounds. Its multi-hit grows each cycle.
  - P3: Intangible every other turn, a 45 hit, 3-5 Burns, +Strength.
  - Play:
    - P1: attacks and Powers only.
    - P2: block cleanly.
    - P3: burst on non-Intangible turns and block on the others.
    - Use the stun turns for Powers. It's an endurance fight.
- **Aeonglass** (512 (535) HP, 3 Artifact; Act 3 boss from v0.105 beta / v0.107.1 main)
  - Fixed loop: **Ebb** 22-26 (26-32) + gains 33 Block → **Eye Lasers** 11×2 (12×2) → **Increasing Intensity** (+2+X Strength; shuffles in and upgrades Wither) → repeat.
  - *Withering Presence*: every 6 cards you play, a **Wither** status is added to your hand. Wither hurts if it stays there, and it can't be upgraded or Retained.
  - Play:
    - Front-load scaling. Don't burst into Ebb's fresh 33 Block. Strip Artifact first.
    - Clear Wither with Exhaust or discard.
    - Prefer fewer, high-impact cards per turn: X-costs, big hits, poison, Doom, orbs.
    - Block-only stalling loses because Strength and Wither escalate.

## A10 Double Boss (unverified, from guides)
- Two Act 3 bosses back-to-back. Your scaling resets between them.
- Spend a potion in the first boss when it saves more HP there than it would in the second; don't hold potions until the first boss threatens a kill. Decks with fast re-setup win.
