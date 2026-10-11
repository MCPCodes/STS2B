# Silent (STS2)

## Kit
- **70 HP.** **12-card deck:** 5 Strike, 5 Defend, Neutralize, Survivor. Survivor is your built-in discard outlet.
- **Starter relic: Ring of the Snake** (draw 2 extra cards on turn 1).
  - Touch of Orobas upgrades it to Ring of the Drake (+2 draw on each of the first 3 turns). Rated only B for Silent.
  - Archaic Tooth likely turns Neutralize into **Suppress**: 0 cost, Innate, 11 (17) damage, 3 (5) Weak (unverified mapping).
- **Character relics:**
  - Tingsha: 3 damage to a random enemy per card discarded on your turn.
  - Tough Bandages: 3 Block per discard.
  - Snecko Skull: +1 Poison per application.
  - Twisted Funnel: 4 Poison to all enemies at combat start.
  - Helical Dart: +1 Dexterity this turn per Shiv.
  - Ninja Scroll: 3 Shivs at start.
  - Paper Krane: Weak enemies deal 40% less.
- **Identity:** the strongest win rate in mid-2026 community aggregates. She wins by tempo: free cards, discards, cheap attacks.
  - She is fragile early, and her starter is light on damage. Take damage first.

## The Sly engine (read this before drafting)
- **Sly:** a Sly card discarded from your hand during your turn is played for free.
- Sly cards are **deliberately expensive to hard-cast**:
  - Tactician (3 cost: +1 (2) Energy).
  - Reflex (3: draw 2 (3)).
  - Abrasive (3 Power: Dexterity + 4 (6) Thorns).
  - Untouchable (2: 6 (9) Block).
  - Ricochet (2: 3×4 random).
  - Flick-Flack (1: 7 (9) AoE).
  - Multiplayer only: Sneaky.
- **Never hard-cast a 3-cost Sly card if a discard outlet is available.**
- **Discard outlets:**
  - Survivor (starter). Prepared (0: draw 1 (2), discard 1 (2); the best common enabler once upgraded).
  - Acrobatics (draw 3 (4), discard 1; now Uncommon). Calculated Gamble (discard hand, redraw; Exhaust). Dagger Throw.
  - Tools of the Trade (Power: draw 1 and discard 1 each turn). Storm of Steel (discard hand for Shivs).
  - Hand Trick (gives a Skill Sly). Master Planner (your Skills gain Sly).
- **Build shape:** 2-3 Sly cards + 1-2 discard triggers + 1 refill (draw).
  - Tingsha + Tough Bandages together means commit to discard.

## Archetypes
### 1. Sly / discard tempo (top tier)
- See above. Plus Backflip, Predator (common since v0.106: 15 (20) damage, draw 2 next turn), Pounce (14 (20) damage, next Skill costs 0), Burst, Well-Laid Plans.
- **Side benefit:** discarding cleans Beckon (Soul Fysh) and Wither (Aeonglass) out of your hand.

### 2. Shiv
- **Generators:**
  - Leading Strike (common: 3 damage + 2 Shivs). Blade Dance. Cloak and Dagger. Hidden Daggers.
  - Infinite Blades (1 Shiv per turn). Blade of Ink (Inky Shivs add Weak; nerfed later). Storm of Steel.
- **Payoffs:**
  - Accuracy (+4 (6) per Shiv; only if you make Shivs most turns).
  - Phantom Blades (Shivs Retain; first Shiv each turn +9 (12)).
  - Fan of Knives (Shivs hit ALL, plus 4-5 Shivs). Knife Trap (replays exhausted Shivs). Finisher.
- **Relics:** Kunai (Dexterity), Shuriken (Strength), Ornamental Fan (Block), Helical Dart, Ninja Scroll.
  - **Velvet Choker** (max 6 cards per turn) kills this deck. Never take it.

### 3. Poison
- **Core:** Noxious Fumes (the best poison card: 2 (3) to all per turn), Deadly Poison, Poisoned Stab, Bouncing Flask, Bubble Bubble (9 (12) if already poisoned), Corrosive Wave.
  - Accelerant (poison ticks extra times). Envenom. Haze. Mirage (Block equal to total enemy Poison; the upgrade loses Exhaust).
- **Relics:** Snecko Skull, Twisted Funnel.
- **Rules:**
  - Build a defensive base first (Backflip, Footwork, Leg Sweep, Piercing Wail). Too much early poison means you die before it ticks.
  - Poison ignores Block, the Queen's Block, and Strength siphons. It is weak in DPS races (Insatiable) and vs Artifact. Use Expose first.

### 4. Weak / control splash (any deck)
- Tracking (Weak enemies take +50% Attack damage). Malaise (X: -Strength and Weak, Exhaust). Leg Sweep. Piercing Wail (all enemies -6 (8) Strength this turn). Neutralize or Suppress. Paper Krane.

## Packages to count
Count these in `player.deck` and your relics before every pick (general.md 12). Upgraded copies count too.
- **Sly / discard**: Sly cards (Tactician, Reflex, Abrasive, Untouchable, Ricochet, Flick-Flack) and outlets (Survivor, Prepared, Acrobatics, Calculated Gamble, Dagger Throw, Tools of the Trade, Storm of Steel; Hand Trick and Master Planner add Sly). A Sly card is free only when an outlet discards it, so each half raises the other.
  - Payoffs per discard: Tingsha (3 damage), Tough Bandages (3 Block). With both, commit to discard.
  - Move up: once in the lane (a Sly card plus an outlet besides Survivor, or Tingsha or Tough Bandages), the missing part of the build shape (2-3 Sly cards + 1-2 discard triggers + 1 refill) beats a generic card of similar strength. In Act 1 it wins only a close call. A 4th Sly card gets no bump.
- **Shiv**: Leading Strike, Blade Dance, Cloak and Dagger, Hidden Daggers, Infinite Blades, Blade of Ink, Storm of Steel, Fan of Knives, Ninja Scroll.
  - Payoffs: Accuracy (+4 (6) per Shiv), Phantom Blades (first Shiv each turn +9 (12)), Fan of Knives (Shivs hit ALL), Knife Trap (replays exhausted Shivs), Finisher, Helical Dart (+1 Dexterity this turn per Shiv), Kunai, Shuriken, Ornamental Fan. Strength adds to every Shiv.
  - Move up: Accuracy and Phantom Blades only once you make Shivs most turns (about 1 generator per 5 cards in the deck, or Infinite Blades). After that, every generator gains from them.
- **Poison**: Noxious Fumes, Deadly Poison, Poisoned Stab, Bouncing Flask, Corrosive Wave, Envenom, Haze, Twisted Funnel.
  - Payoffs: Bubble Bubble (9 (12) if already poisoned), Accelerant (poison ticks extra times), Mirage (Block equal to total enemy Poison), Snecko Skull (+1 per application: many small applications gain most).
  - Move up: after the defensive base (Backflip, Footwork, Leg Sweep, Piercing Wail) and 2+ sources. Then a payoff beats another plain source (no Deadly Poison or Snakebite bloat).
- **Weak**: Neutralize or Suppress, Leg Sweep, Malaise, other Weak cards (read get_deck). Payoffs: Tracking (Weak enemies take +50% Attack damage), Paper Krane (Weak enemies deal 40% less). Both are blank on an enemy without Weak.

## Draft priorities
- **Act 1 (damage first):**
  - Leading Strike, Backstab (0 cost, Innate, 11 (15), Exhaust), Predator, Pounce, Echoing Slash (AoE that repeats on kills), Dagger Spray (early AoE only), Expose, Assassinate.
  - Defense: Piercing Wail, Leg Sweep, Footwork, Backflip, Dodge and Roll, Afterimage.
- **Premium:** Adrenaline (almost always take it), Well-Laid Plans, Tools of the Trade, Footwork, Afterimage, Burst, Calculated Gamble (1 copy), Noxious Fumes, Acrobatics, Tactician, Serpent Form (needs energy), Wraith Form (Ancient), Master Planner.
- **Pinpoint:** 15 (19) damage, costs 1 less per Skill played this turn. Play your Skills first.
- **Upgrade first:** Prepared, Footwork, Mirage, Calculated Gamble (gains Retain), Acrobatics, Backstab, Afterimage (Innate), Infinite Blades (Innate), Backflip.
- **Shop (AgenticSTS seed, A6-A8 ladder):** go in with 200+ gold. Buy order:
  1. Exceptional colorless card.
  2. On-plan Silent card.
  3. Fitting relic.
  4. Removal.

## Combat habits
- Count playable cards, not just energy.
- Order: zero-cost and setup first. Skills before Pinpoint. Pounce before an expensive Skill. Discard Sly cards for free plays.
- **First 3 hallway fights:** take the full-block line before racing. After that, protect HP and use potions.
- **Long fights:** once defense is stable, stack poison and block rather than making fair attack trades.
- Remember: Beckon and Infection still hurt at end of turn even if you kill the enemy.

## Traps
- Accuracy without Shiv volume. Reflex or Tactician without discard outlets.
- More than 1 Calculated Gamble. Deadly Poison or Snakebite bloat.
- Storm of Steel discards your Retained cards.
- Expertise or Serpent Form with no energy support.
- Blade of Ink outside Shiv or Sly (its text changed across patches).
- Scare (formerly Follow Through) was reworked in v0.106. Read the current text.

## Boss and elite notes
- **Vantom:** Shivs and Ricochet are ideal for stripping Slippery.
- **Soul Fysh:** discard Beckons with Survivor or Prepared. Deal your damage on turns 2-3, before Intangible.
- **Lagavulin Matriarch:** poison ignores her Strength siphon.
- **Insatiable:** poison is too slow alone. Add frontload.
- **Queen:** poison bypasses her 20 Block per turn.
- **Aeonglass and Mecha Knight:** 3 Artifact. Expose or cheap debuffs before poison.
  - Discard lines clear Withers. Silent is rated the most adaptable vs Aeonglass.
