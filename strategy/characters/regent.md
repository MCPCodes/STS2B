# Regent (STS2, new character)

## Kit
- **75 HP.** Deck: 4 Strike, 4 Defend, **Falling Star** (0 Energy + 2 Stars: 8 (12) damage, 1 Weak, 1 Vulnerable), **Venerate** (1 Energy: gain 2 (3) Stars).
- **Starter relic: Divine Right** (+3 Stars at combat start). Older guides call it Crown of Stars.
  - Touch of Orobas upgrades it to **Divine Destiny** (7 Stars at start). Rated A.
  - Archaic Tooth likely turns Falling Star into **Meteor Shower** (0 + 2 Stars: 14 (21) AoE, 2 Weak and 2 Vulnerable to all), or Venerate into **The Sealed Throne** (Power: +1 Star per card played) (unverified mapping).
- **Unlock:** beat a run with Silent.
- **Character relics:**
  - Lunar Pastry: +1 Star at end of turn.
  - Mini Regent: +1 Strength the first time you spend Stars each turn.
  - Galactic Dust: 10 Block per 10 Stars spent.
  - Fencing Manual: Forge 10 at start.
  - Orange Dough: 2 random colorless cards at start.
  - Regalite: Block when you create a card. Changed repeatedly; read the text.
  - Vitruvian Minion: doubles Minion card damage and Block.
- **Identity:** weak early, strong late. Short on natural draw, so card generation substitutes. He was buffed heavily in v0.103.2.

## Mechanics
- **Stars:** a second resource shown as a cost on cards. Stars **persist between turns with no cap**. Bank them for big turns.
  - Target 6+ Stars of income or banking by the end of Act 2.
- **Forge N:** the first Forge each combat adds **Sovereign Blade** (2-cost Retain attack) to your hand.
  - Every Forge raises the Blade's damage for the combat. The Blade cycles back so you can swing it repeatedly.
  - Your starter deck has no Forge cards. Sovereign Blade only appears once you draft Forge.
- **Created cards:** many Regent payoffs count cards you create (colorless, Minion, Debris). Examples: Pillar of Creation, Arsenal, Supermassive, Regalite.
- **Minion cards:** CHARGE!! (transforms 2 draw-pile cards into Minion Dive Bomb), GUARDS!!! (transform hand cards into Minion Sacrifice), BEGONE! (into Minion Strike).
  - These double as cleanup that turns curses, statuses and Strikes into useful cards.

## Archetypes
### 1. Star engine (most reliable)
- **Income:** Venerate, Glow (+Star, draw now and next turn), Gather Light (8 (11) Block + 1 Star), Hidden Cache (+1 now, +3 (4) next turn), Solar Strike, Shining Strike, Genesis (Power: +2 (3) Stars per turn), Royal Gamble (5 Stars to gain 9, Exhaust), Convergence, The Sealed Throne, Lunar Pastry.
- **Spenders:**
  - Reflect (3 Stars: 15 (20) Block and reflect blocked damage; an S-tier early pick).
  - Astral Pulse (3 Stars: 6×2 AoE). Gamma Blast. Comet (5 Stars: 33 (44) + 3 Weak and 3 Vulnerable). Devastate (4 Stars: 35).
  - Stardust (X Stars). Seven Stars (7 Stars: 7×7 AoE). Particle Wall (2 Stars: Block, returns to hand). Cloak of Stars.
  - Decisions, Decisions. Neutron Aegis (Plating).
- **Star-spend payoffs:** Child of the Stars (2 (3) Block per Star spent; a scaling block engine), Black Hole (AoE per Star gained or spent), Galactic Dust, Mini Regent.
- **Endgame:** Void Form (the first 2 cards each turn are free; playing it ENDS your turn).

### 2. Forge / Sovereign Blade
- **Forge cards:** Wrought in War, Spoils of Battle (Forge + draw 2), Refine Blade, Bulwark (12 (15) Block + Forge 10), Furnace (Forge each turn), Summon Forth (fetch the Blade), Seeking Edge (Blade hits ALL), Conqueror, Beat into Shape, The Smith (4 Stars: Forge 30 (40)).
- **Defensive loop (mid-2026 best Regent build):**
  - Parry (Power: the Blade gives 10 (14) Block).
  - Sword Sage (the Blade gains Replay 1).
  - Add Fencing Manual and Mini Regent.
- Pure Forge is slow at higher Ascension. Run it on top of a Star base (Forge cards often cost Stars).
  - One guide says never mix Star and Forge. Most others mix. Treat that as low confidence.

### 3. Created / colorless
- Bundle of Joy (3 (4) colorless cards). Spectrum Shift (a colorless card each turn; "best of the best" per a top player, but slow in short fights). Quasar. Manifest Authority. Big Bang (0: draw, Energy, Star, Forge 5, Exhaust; contender for the best card).
- Pillar of Creation (Block per created card). Arsenal (Strength per created card). Supermassive. Heirloom Hammer. Orange Dough.
- High variance. Go here only when the support appears naturally.

### 4. Minion / Crash Landing (support package)
- CHARGE!! (one of the strongest early damage picks), GUARDS!!!, Crash Landing (21 (26) AoE, but fills your hand with Debris), Minion Sacrifice, Lunar Blast, Terraforming (Vigor), Vitruvian Minion.

## Packages to count
Count these in `player.deck` and your relics before every pick (general.md 12). Upgraded copies count too.
- **Star income**: Venerate, Glow, Gather Light, Hidden Cache, Solar Strike, Shining Strike, Genesis, Royal Gamble, Convergence, The Sealed Throne, Big Bang, Lunar Pastry, Divine Right or Divine Destiny.
  - Spenders live on it: Particle Wall (2), Reflect (3), Astral Pulse (3), Devastate (4), The Smith (4), Comet (5), Seven Stars (7), Stardust (X). Secure income before payoffs: 6+ Stars of income or banking by the end of Act 2.
- **Star spending**: Child of the Stars (2 (3) Block per Star spent), Black Hole (AoE per Star gained or spent), Galactic Dust (10 Block per 10 Stars spent), Mini Regent. They grow with every income card and spender: take them once 2+ income cards besides Venerate and a spender besides Falling Star are in.
- **Strength**: Mini Regent (+1 the first time you spend Stars each turn), Arsenal (Strength per created card). Strength adds to every hit of an Attack: the Blade with Sword Sage (Replay 1), and Astral Pulse (2 hits) and Seven Stars (7 hits on every enemy) if get_deck shows them as Attacks. With Mini Regent, or Arsenal plus 2+ creators, these move up.
- **Forge**: Wrought in War, Spoils of Battle, Refine Blade, Bulwark, Furnace, Summon Forth (fetches the Blade), Seeking Edge (the Blade hits ALL), Conqueror, Beat into Shape, The Smith, Big Bang, Fencing Manual. Every Forge raises Sovereign Blade's damage for the fight.
  - Payoffs: Parry (the Blade gives 10 (14) Block), Sword Sage (the Blade gains Replay 1).
  - Move up: Parry and Sword Sage need a Forge card first (the starter has none). Run Forge on a Star base: pure Forge is slow at higher Ascension.
- **Created cards**: Bundle of Joy, Spectrum Shift, Orange Dough, Debris from Collision Course and Crash Landing, Minion cards from CHARGE!!, GUARDS!!! and BEGONE!. Quasar and Manifest Authority sit in this lane.
  - Payoffs: Pillar of Creation (Block per created card), Arsenal (Strength per created card), Supermassive, Regalite (Block when you create a card).
  - Move up: payoffs only after 2+ creators (Traps: Arsenal before creators).
- **Minion**: CHARGE!!, GUARDS!!! and BEGONE! make Minion cards. Vitruvian Minion pays only with several.

## Draft priorities
- **Early:** Reflect, Astral Pulse, CHARGE!!, Collision Course (0 cost: 10 (14) damage, adds Debris), Glitterstream (11 Block now + 5 next turn), Bulwark, Gather Light, Know Thy Place (0 cost: Weak + Vulnerable; strips Artifact), Glow, Photon Cut, Hidden Cache.
- **Premium:** Big Bang, Spectrum Shift, Royal Gamble (if you can reach 5 Stars), Convergence, Bundle of Joy, Child of the Stars, Pillar of Creation, Genesis, Void Form, Comet, Meteor Shower, The Sealed Throne, Seven Stars, Parry and Sword Sage (with a Blade).
- **Upgrade first:** Glow (+2 Stars lets you hold Falling Star and bank 3), Venerate (+3 Stars), Falling Star, Void Form (loses Ethereal), Royal Gamble (Retain), Big Bang (Innate), Know Thy Place (loses Exhaust).
- **Neow and Ancients:** acceleration (Hefty Tablet, Neow's Talisman) beats defensive picks.
  - Glass Eye (more card rewards) is best on Regent. Pael's Eye fits Regent.

## Traps
- Alignment: overpicked, subpar energy.
- Arsenal before you have creators.
- Kingly Punch or Kingly Kick in big decks.
- Heavenly Drill without energy. Bombardment and The Smith are hard to use.
- Crash Landing with no way to clear Debris.
- Drafting payoffs before income: secure Star generation first.
- Void Form: remember it ends your turn.
- Neow's Bones (curse) and Leafy Poultice: Regent's Act 1 is fragile.

## Boss notes
- **Ceremonial Beast:** single high-value plays on Ringing turns (Comet, The Smith, Devastate). Bank Stars beforehand.
- **Aeonglass:**
  - Spend after Ebb's 33 Block is gone. Set up Star-dump turns: few cards, big effects, fewer Withers.
  - Monarch's Gaze (enemies lose Strength when attacked) is good. Know Thy Place strips its 3 Artifact.
- **Queen:** Chains of Binding limits playable cards. Retain and Stars help (Stars persist).
- **Knowledge Demon:** Sloth (max 3 cards per turn) is relatively harmless for a Star-dump deck. Pick by deck.
