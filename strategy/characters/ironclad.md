# Ironclad (STS2)

## Kit
- **80 HP.** Deck: 5 Strike, 4 Defend, Bash.
- **Starter relic: Burning Blood** (heal 6 after each combat).
  - Touch of Orobas upgrades it to Black Blood (heal 12). This is rated weak for Ironclad.
  - Prefer **Archaic Tooth**, which upgrades a starter card to an Ancient version. Bash becomes **Break**: 1 cost, 20 (30) damage, 5 (7) Vulnerable.
- **Character relics:**
  - Paper Phrog: Vulnerable is 75% instead of 50%.
  - Ruined Helmet: the first Strength gain each combat is doubled.
  - Red Skull: +3 Strength while at or below 50% HP.
  - Self-Forming Clay: 3 Block next turn after losing HP.
  - Demon Tongue: the first HP loss on your turn is healed.
  - Charon's Ashes: 3 AoE damage per Exhaust.
  - Brimstone: +2 Strength per turn, but enemies gain +1 Strength.
- **Character potions:**
  - Blood Potion: heal 20% max HP.
  - Ashwater: Exhaust any cards in hand.
  - Soldier's Stew: Strikes gain Replay.
- **Identity:** the most forgiving character. Burning Blood lets you take more Act 1 elites and trade HP for tempo.
  - The weak point is consistent block. Uppercut is his only Weak source, so kill fast.

## Archetypes (blend them; they overlap)
- Exact card numbers and upgrades: strategy/wiki/cards/ironclad.md
### 1. Vulnerable / Strength (most common winning core)
- **Vulnerable sources:**
  - Tremble (common, 1 cost: 3 (4) Vulnerable, Exhaust). An elite pick early.
  - Taunt (7 (8) Block + 1 (2) Vulnerable).
  - Molten Fist (10 damage, doubles Vulnerable, Exhaust).
  - Uppercut. Bash or Break.
  - Dominate (uncommon: apply Vulnerable, gain 1 Strength per Vulnerable on the target, Exhaust).
- **Payoffs:**
  - Cruelty (Vulnerable is 75% (100%) instead of 50%).
  - Dismantle (hits twice vs Vulnerable).
  - Bully (0 cost: +2 damage per Vulnerable stack).
  - Colossus (block, and take 50% less damage from Vulnerable enemies this turn). A standard Act 1 defensive pick since it became Uncommon.
- **Strength:**
  - Inflame. Demon Form (+2 (3) Strength per turn: the best boss scaler). Dominate. Rupture (self-damage line).
  - Payoffs: Whirlwind, Sword Boomerang, Twin Strike, Thrash, Conflagration (2 AoE × 4-5), One-Two Punch.
- **Relics:** Paper Phrog, Ruined Helmet (+Inflame or Dominate), Red Skull, Vajra, Shuriken.

### 2. Exhaust (best answer to Aeonglass Withers and junk statuses)
- **Enablers:**
  - Burning Pact (exhaust 1, draw 2 (3)). True Grit. Second Wind. Brand (0 cost: lose 1 HP, exhaust 1, +Strength).
  - Stoke (exhaust hand, get random cards). Forgotten Ritual (1 cost: +3 (4) Energy only if a card was exhausted earlier this turn, Exhaust). Drum of Battle (draw 2; +2 (3) Energy when another effect exhausts it; it does not exhaust itself).
  - Corruption (Ancient rarity: Skills cost 0 and Exhaust).
- **Payoffs:**
  - Feel No Pain (block per exhaust). Dark Embrace (draw per exhaust). Evil Eye (8 (11) Block, twice if a card was exhausted earlier this turn).
  - Fiend Fire. Ashen Strike (+3 (4) damage per exhausted card).
  - Pact's End (0 cost: 17 (23) AoE if 3+ cards are in the exhaust pile).
  - Charon's Ashes.
- **Rule:** commit when you hold 2+ enablers and 1 payoff. Feel No Pain and Dark Embrace are bad without exhaust density.

### 3. HP-loss / self-damage (rated a top archetype in April-May 2026)
- **Engine:**
  - Bloodletting (HP to energy). Offering (0 cost: lose 6 HP, +2 Energy, draw 3 (5), Exhaust; one of his best cards).
  - Rupture (+Strength when you lose HP on your turn).
  - Inferno (lose 1 HP per turn; 6 (9) AoE each time you lose HP on your turn).
  - Crimson Mantle (lose 1 HP, gain 8 (10) Block each turn).
  - Hemokinesis, Breakthrough, Tear Asunder (extra hit per HP-loss instance this combat), Blood Wall.
- **Sustain:** Burning Blood, Not Yet (2 cost rare: heal 10 (13), Exhaust), Feed (+max HP on kill).
  - Relics: Self-Forming Clay, Demon Tongue, Red Skull, Tungsten Rod.
- **Rule:** below 50% HP, only spend HP when it wins the fight. No double-Bloodletting turns at low HP.

### 4. Block / Body Slam (secondary)
- Body Slam, Shrug It Off, Flame Barrier, Impervious (30 (40) Block, Exhaust), Unmovable (doubles the first card-block each turn), Stone Armor, Colossus.
- Barricade + Juggernaut (6 (8) damage per block gain).
- Relics: Anchor, Horn Cleat, Captain's Wheel, Sturdy Clamp.
- Barricade is a trap without a real block engine. Killing fast is usually better.

## Packages to count
Count these in `player.deck` before every pick (general.md 12); upgraded copies count. Thresholds: general.md 12, unless a package sets its own.
- strategy/wiki/cards/ironclad-pick-values.md measures each card in single fights with an average deck, and strategy/wiki/cards/ironclad-synergies.md what it gains with 2 or 4 pieces of its package: a guide to weigh, not a rule, until whole runs test them.
- strategy/wiki/cards/ironclad-powers.md says when to play each Power in a fight and what to play before it, with the measured play rates; strategy/wiki/cards/ironclad-power-timing.md holds the measurements behind them.
- **Strength**: Inflame, Demon Form, Dominate, Rupture, Brand, Fight Me; Brimstone, Red Skull, Ruined Helmet, Vajra, Shuriken. Setup Strike counts on its own turn only.
  - Payoffs: each Strength adds 1 to every hit. Hits: Twin Strike 2, Thrash 2, Fight Me 2, Sword Boomerang 3 (4), Conflagration 4 (5) to ALL, Whirlwind X to ALL, Spite 2 (3) after HP loss that turn, Dismantle 2 against Vulnerable, Tear Asunder 1 + 1 per HP loss, Fiend Fire 1 per card exhausted. One-Two Punch and Anger add hits.
  - At 4 Strength: Sword Boomerang 3 x 7 = 21, Twin Strike 2 x 9 = 18, Strike 10. Flat damage (Inferno, Juggernaut, Flame Barrier) ignores Strength; Perfected Strike, Ashen Strike and Body Slam add it once.
- **Vulnerable**: Break 5 (7), Tremble 3 (4), Bash 2 (3); 1 (2) from Uppercut, Taunt, Dominate; Thunderclap 1 to ALL; Molten Fist doubles it. N stacks cover N of your turns, so weigh long sources above 1-stack ones.
  - Payoffs: every hit on a Vulnerable enemy is x1.5, so it multiplies Strength and multi-hits. Bully +2 (3) per stack before the x1.5. Dismantle hits 2 times. Cruelty x1.75 (2.0), Paper Phrog x1.75, and the two add up. Colossus halves hits from Vulnerable enemies. Dominate gains Strength per stack. Vicious draws 1 (2) per enemy given Vulnerable (Thunderclap on 3 draws 3).
  - Bully, Dismantle and Colossus measure well already. Cruelty and Vicious only once Vulnerable is up most turns (Break, or 3+ sources).
- **Exhaust**: cards that exhaust others: Burning Pact, True Grit, Second Wind, Brand, Cinder, Thrash, Havoc, Stoke, Fiend Fire, Corruption. An Exhaust-keyword card feeds the pile once a fight: count it for Ashen Strike and Pact's End, not toward the 4-5 sources Feel No Pain and Dark Embrace need (Traps).
  - Payoffs: per exhaust, Feel No Pain 3 (4) Block, Dark Embrace 1 draw, Charon's Ashes 3 to ALL. Ashen Strike +3 (4) per card in the exhaust pile, Pact's End 17 (23) to ALL from 3 there. Evil Eye and Forgotten Ritual need an exhaust earlier that turn. Drum of Battle +2 (3) energy when another effect exhausts it.
- **HP loss on your turn**: Bloodletting, Offering, Breakthrough, Hemokinesis, Blood Wall, Brand, and Inferno and Crimson Mantle every turn. Payoffs count losses, not HP: Brand's 1 counts as much as Offering's 6.
  - Payoffs: per loss, Rupture +1 (2) Strength and Inferno 6 (9) to ALL; Spite hits 2 (3) times after one; Tear Asunder +1 hit per loss this fight, unblocked enemy hits included.
- **Block**: the engine is Block that repeats: Unmovable, Stone Armor, Crimson Mantle (every turn), Rage (per Attack), Feel No Pain (per exhaust). Defends do not count. Payoffs only from 2 engine pieces; short of that, kill fast.
  - Payoffs: Body Slam deals your current Block (Strength once). Barricade keeps Block between turns. Juggernaut 6 (8) flat to a random enemy per Block gain, so many small gains beat one big one (Rage per Attack, Second Wind per card, Evil Eye twice after an exhaust).
- **Strike-tagged**: Strike, Perfected Strike, Pommel Strike, Twin Strike, Setup Strike, Ashen Strike; the starter deck has 5. Those 5 are already in the measured values, so count only Strike-tagged cards you add. Perfected Strike gets +2 (3) per one in any pile, itself included (18 (24) with the starter 5). Hellraiser plays each one you draw for free.
  - With either, each Strike-tagged pick moves up, and remove Defends, not Strikes.

## Draft priorities
- **Early (Act 1):**
  - Damage: Tremble (Vulnerable only, no damage itself), Bludgeon, Breakthrough, Twin Strike, Sword Boomerang, Headbutt, Anger, Molten Fist.
  - Block: Shrug It Off, Colossus, Impervious.
  - Draw: Battle Trance (the first copy is almost always good), Pommel Strike (draw-positive only when upgraded).
  - AoE for multi-enemy fights: Thunderclap, Whirlwind, Conflagration.
- **Premium any time:** Offering, Demon Form, Dominate, Impervious, Inferno, Crimson Mantle, Burning Pact, Primal Force (situational).
- **Premium once their package is there** (Packages to count): Cruelty, Dark Embrace, Fiend Fire, Pact's End, Unmovable.
- **Upgrade first:** Bash or Break, Pommel Strike, Body Slam (0 cost), Burning Pact, Offering (draw 5), Demon Form, Shrug It Off. Then an upgrade that adds a hit, a Vulnerable stack or Strength to your package (Packages to count).

## Traps
- Barricade or Body Slam without a block engine.
- Feel No Pain or Dark Embrace in a deck with fewer than 4-5 exhaust sources.
- Brimstone in slow decks (enemies scale too).
- Corruption played carelessly: it exhausts your Defends.
- Expect a Fight: its text changed across patches. Read it. In v0.107.1 it is a 2 (1) cost Skill: +1 Energy per Attack in hand, then no more Energy gain that turn. No Block.
- Drum of Battle and Conflagration were reworked around v0.104. Read the current text.
- Too many attacks with no block: watch the attack-to-block ratio in Vulnerable decks.

## Boss and elite notes
### Act 1 Overgrowth (fight cards: bosses-and-elites.md)
- **Counters to draft for the coming boss, and for elites:**
  - Vantom: multi-hits, since each hit strips 1 Slippery (Twin Strike 2, Sword Boomerang 3, Thrash 2, Conflagration 4, Whirlwind X), and Anger (0 cost, 1 hit per play, adds a copy each play). Flame Barrier's 4 back per enemy hit strips one too. Exhaust (Second Wind, Burning Pact, True Grit) for the Wounds, which never leave the deck by themselves.
  - The Kin: area damage (Breakthrough 9, Thunderclap 4, Whirlwind, Howl from Beyond 16) or single-target burst on the Priest (Bludgeon 32).
  - Ceremonial Beast: fast damage to deal the 102 early, and big single cards for the 1-card turns: Bludgeon, Impervious (30 Block), Mangle (15, and Stomp 15 becomes 5; the trigger wipes its Strength loss).
  - Phrog Parasite: area damage for the 4 Wrigglers. Byrdonis: front-loaded damage and Vulnerable, Weak from Uppercut; Flame Barrier returns 12 against a Peck. Bygone Effigy: Powers for the 2 free turns, Vulnerable, Weak from Uppercut (23 becomes 17).
- **In the fight:** against Vantom a Strike strips a stack exactly like Bludgeon. Against The Kin, Bash the Priest (e2). Against Bygone Effigy, Skills and Powers first, then Bash, then the other attacks, so they get Vulnerable and Slow.
### Other acts (unverified, from guides)
- **Lagavulin Matriarch:** use the 3 sleep turns for Powers (Demon Form, Inflame, Inferno), then race before her second siphon (-2 Strength and -2 Dexterity each).
- **Knowledge Demon:** take Disintegration early. Kill before the 3rd curse.
- **Aeonglass:** Exhaust plus Strength is the most reliable plan (Feel No Pain, Burning Pact, Corruption clear Withers).
  - Hold burst until Ebb's 33 Block is spent. It starts with 3 Artifact, so play cheap debuffs first to strip it.
- **Test Subject phase 1:** it gains Strength per Skill you play. Lean on Attacks and Powers that phase.
