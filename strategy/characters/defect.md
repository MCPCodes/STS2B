# Defect (STS2)

## Kit
- **75 HP.** 3 orb slots. Deck: 4 Strike, 4 Defend, Zap, Dualcast.
- **Starter relic: Cracked Core** (channel 1 Lightning at combat start).
  - Touch of Orobas upgrades it to **Infused Core** (channel 3 Lightning; Lightning +1 damage). Rated S for Defect, take it.
  - Archaic Tooth likely gives Quadcast (evoke the rightmost orb 4 times) or Biased Cognition (+5 Focus, then -1 per turn) (unverified mapping).
- **Character relics:**
  - Data Disk: +1 Focus.
  - Gold-Plated Cables: the rightmost orb's passive triggers twice.
  - Emotion Chip: if you lost HP last turn, all passives trigger.
  - Metronome: 30 AoE damage the first time you channel 7 orbs in a combat.
  - Runic Capacitor: +3 slots.
  - Power Cell: 2 zero-cost cards from the draw pile into your opening hand.
  - Symbiotic Virus: channel 1 Dark at start.
- **Potions:** Focus Potion, Potion of Capacity, Essence of Darkness.
- **Identity:** lowest win rate in mid-2026 aggregates (about 30%; the spread between all 5 characters is narrow).
  - Got buffs in v0.105. Lightning damage is the current best plan.
  - Frost-only stalling is punished by Aeonglass and the Insatiable.

## Orbs (STS2 values; each Focus adds +1 to passive and evoke)
| Orb | Passive (end of turn) | Evoke |
|---|---|---|
| Lightning | 3 damage to a random enemy | 8 damage to a random enemy |
| Frost | 2 Block | 5 Block |
| Dark | +6 to its stored damage | stored damage to the LOWEST-HP enemy |
| Glass (new) | 4 damage to ALL enemies, -1 each turn | 2× its current passive to ALL enemies |
| Plasma | +1 Energy | +2 Energy |

- Channeling into full slots evokes the rightmost orb.
- Dark targets the lowest-HP enemy, which is bad vs a boss with adds.
- Whether Focus affects Plasma is unverified (it didn't in STS1).

## Archetypes
### 1. Lightning (current best)
- **Channel:** Ball Lightning, Lightning Rod (block, plus a Lightning on each of the next 2 turns), Tempest (X Lightning), Storm (Lightning per Power played), Voltaic (re-channel all Lightning channeled so far).
- **Payoffs:**
  - Thunder (+8 (11) per Lightning evoke hit).
  - Tesla Coil (0 cost: trigger all Lightning passives 1 (2) times).
  - Loop. Gold-Plated Cables. Infused Core. Metronome.
- **Burst finishers:**
  - Shatter (7 (11) AoE, evoke all orbs twice, Exhaust; nerfed in v0.106, now a finisher not a scaler).
  - Hyperbeam (24 (30) AoE but -3 Focus this turn). Barrage.

### 2. Frost wall (safest at low Ascension; weak vs Act 3 now)
- Glacier (block + 2 Frost), Chill (0 cost: Frost per enemy), Cold Snap, Coolheaded (Frost + draw), Ice Lance, Hailstorm.
- Coolant (block per unique orb each turn).
- Scale with Capacitor, Defragment, Loop, Echo Form, Creative AI.
- Add real damage. Frost dies to fast heavy hitters and to DPS checks.

### 3. Dark / evoke burst
- Darkness (channel Dark, trigger Dark passives), Shadow Shield, Null (damage + Weak + Dark), Consuming Shadow.
- Evoke with Dualcast, Multi-Cast (X), or Quadcast. Hologram retrieves Dualcast.
- Best vs single bosses. Add AoE for minion fights.

### 4. Claw / zero-cost (falls off at high Ascension)
- Claw (+2 (3) damage to all Claws per play), Scrape, FTL, Skim, Hologram, All for One (returns 0-cost cards from discard), Go for the Eyes, Momentum Strike, Beam Cell.
- **Relics:** Power Cell, Kunai, Shuriken, Ornamental Fan.
- Don't take every 0-cost card. Bloat buries Claw.

### 5. Status (niche)
- Smokestack (5 AoE per status created), Iteration, Trash to Treasure (orb per status), Flak Cannon, Compact, Overclock, Rocket Punch.

## Packages to count
Count these in `player.deck` and your relics before every pick (general.md 12). Upgraded copies count too.
- **Lightning**: Ball Lightning, Lightning Rod, Tempest, Storm, Voltaic, Cracked Core or Infused Core.
  - Payoffs: Thunder (+8 (11) per Lightning evoke hit), Tesla Coil (every Lightning passive again, 1 (2) times: more Lightning held, more damage), Gold-Plated Cables, Metronome, Loop. Storm adds a Lightning per Power played.
  - Move up: Thunder and Tesla Coil once 2+ Lightning cards are in the deck.
- **Focus and slots**: Defragment, Synchronize, Biased Cognition, Data Disk. Each Focus adds +1 to orb passives and evokes, so it gains with every channeler. Synchronize and Coolant count unique orb types.
  - Slots (Capacitor, Modded, Runic Capacitor) pay only if you channel often.
- **Frost**: Glacier, Chill, Cold Snap, Coolheaded, Ice Lance, Hailstorm. Focus adds Block to each Frost passive and evoke. A Frost-only deck still needs a damage plan before more Frost.
- **Dark / evoke**: Darkness, Shadow Shield, Null, Consuming Shadow, Symbiotic Virus. A Dark orb stores +6 a turn and evoke releases it, so evoke cards (Dualcast, Multi-Cast, Quadcast; Hologram retrieves Dualcast) gain with every Dark source.
- **Claw / zero-cost**: Claw (+2 (3) damage to all Claws per play: each Claw grows the others), Scrape, FTL, Skim, Hologram, All for One (returns 0-cost cards from discard), Go for the Eyes, Momentum Strike, Beam Cell, Power Cell, Kunai, Shuriken, Ornamental Fan.

## Scaling and energy (good in every Defect deck)
- **Focus:**
  - Defragment (+1 (2) Focus; the best Focus card).
  - Synchronize (Focus per unique orb).
  - Biased Cognition (Ancient). Focused Strike and Hotfix (temporary, only with draw).
- **Engines:** Echo Form (the first card each turn played twice; upgrade removes Ethereal), Machine Learning (+1 draw), Creative AI, Buffer, Genetic Algorithm (grows permanently each play).
- **Slots:** Capacitor (+2 (3)) and Modded only if you channel often.
- **Energy:**
  - Supercritical (0 cost: +4 (6) Energy, Exhaust). Double Energy.
  - Fusion (1 cost: Plasma; upgrade removes Exhaust). Charge Battery. TURBO. Boot Sequence (Innate 10 Block).

## Draft priorities
- **Early:** Ball Lightning, Cold Snap, Coolheaded, Barrage, Glacier, Chill, Hologram, Boot Sequence, Charge Battery, Lightning Rod, Focused Strike, Compile Driver, Go for the Eyes (Weak).
- **Premium:** Defragment, Echo Form, Glacier, Chill, Capacitor (with channels), Modded, Machine Learning, Supercritical, Genetic Algorithm, Synchronize, Tesla Coil, Thunder, Buffer, Double Energy.
- **Pick an orb plan by about floor 10.** Upgrade Zap (becomes 0 cost), Dualcast (becomes 0 cost), Hologram (loses Exhaust), Chill, Echo Form, Fusion and Defragment first.

## Traps
- Capacitor or Modded without enough channels.
- Focused Strike or Hotfix without draw.
- A 2nd Shatter. Shatter as an opener: it's better after Lightning has pinged for turns.
- Meteor Strike (too costly). Glass decks without constant cycling.
- Hyperbeam on a Focus-dependent turn.
- Dark vs a boss with summons.
- Frost-only decks with no damage plan.

## Boss and elite notes
- **Vantom:** Lightning passives are many small hits, perfect for stripping Slippery.
- **Lagavulin Matriarch and Ceremonial Beast:** orb damage ignores your Strength loss and works under Ringing (passives fire without card plays).
- **Infested Prism:** reportedly gives Energy to the first Attack that damages it each turn (unverified; reworked in v0.106, so read its power text). Orbs aren't Attacks, so you still need an Attack to collect that Energy.
- **Insatiable, Knowledge Demon:** DPS checks. Lightning + Focus by Act 2.
- **Aeonglass:** Lightning passive damage keeps your card count per turn low, which means fewer Withers. Frost-tank is a poor fit.
- **Phrog Parasite (elite):** its Wrigglers swap attack and buff roles. Random Lightning may hit the wrong one, so finish attackers with cards.
