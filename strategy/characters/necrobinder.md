# Necrobinder (STS2, new character)

## Kit
- **66 HP** (lowest). Deck: 4 Strike, 4 Defend, **Bodyguard** (1 cost: Summon 5 (7)), **Unleash** (1 cost: Osty deals 6 (9) + Osty's current HP).
- **Starter relic: Bound Phylactery** (Summon 1 at the start of each turn). Older guides call it Osty's Binding.
  - Touch of Orobas upgrades it to **Phylactery Unbound** (Summon 5 at combat start, then 2 per turn). Rated S, take it.
  - Archaic Tooth likely turns Unleash into **Protector** (Osty deals 10 (15) + Osty's MAX HP) (unverified mapping). Forbidden Grimoire is her other Ancient card.
- **Character relics:**
  - Funerary Mask: 3 Souls into the draw pile at start (a Souls signal).
  - Bone Flute: 2 Block when Osty attacks.
  - Book Repair Knife: heal 3 when Doom kills.
  - Undying Sigil: enemies with Doom >= HP deal 50% less.
  - Plague Carrier: Osty attacks apply Doom (unverified).
  - Big Hat: 2 random Ethereal cards at start. Weak without Soul or Ethereal synergy.
  - Bookmark: Retained cards get cheaper.
  - Ivory Tile: +Energy when you play a 3+ cost card.
- **Identity:** highest ceiling, highest skill floor. Two bodies (you and Osty) and two clocks (Doom and Souls).
  - One failed setup fight can end a run. Grab attacks early, especially at higher Ascension.

## Mechanics
- **Osty:**
  - Your skeletal hand. He absorbs damage aimed at you **after your Block**, before your HP.
  - **Summon N** summons him, or raises his Max HP for this combat. Summoned HP is NOT Block: it doesn't expire each turn.
  - Osty attacks are cards that say "Osty deals X". Your buffs don't boost him (unverified, per PCGamesN).
  - If he dies, Summon brings him back (unverified details).
- **Doom:** at the END of the enemy's turn, an enemy with Doom >= current HP dies (ignores Block).
  - It still attacks that turn. Block for its last hit.
  - Direct damage lowers the threshold. AoE Doom only clears enemies whose own HP is below their own Doom.
  - Healing bosses (Knowledge Demon +30 HP) push HP back above Doom.
- **Souls:** 0-cost Skills: draw 2, Exhaust. They cycle your deck fast.
- **Ethereal theme:** Ethereal cards Exhaust if left in hand at end of turn. Payoffs: Pagestorm, Spirit of Ash, Banshee's Cry, Pull from Below, Veilpiercer, Eidolon.

## Archetypes (pick ONE main lane; splash Souls into anything)
### 1. Souls engine (most consistent)
- **Generators:** Capture Spirit (1 cost: enemy loses 3 HP, 3 (4) Souls into the draw pile; take 2 copies), Grave Warden (8 (11) Block + Soul), Reave (10 (13) + Soul), Severance (13 (18) + 3 Souls), Dirge (X: Summon 3 per X + X Souls), Seance (transform a card into a Soul), Funerary Mask.
- **Payoffs:**
  - Death March (8 + 4 (6) per card drawn this turn; buffed). Soul Storm (+4 (6) per exhausted Soul).
  - Haunt (enemy loses 7 (9) per Soul played). Devour Life (Summon per Soul played).
- **Rule:** the best default lane, and the pivot if Doom rares don't show by about floor 10.

### 2. Osty (tank and hitter)
- **Grow him:** Bodyguard, Pull Aggro (Summon + 7 Block), Afterlife, Spur (Retain, Summon, heal Osty), Sic 'Em (Summon when Osty hits; buffed), Necro Mastery (enemies lose HP whenever Osty does), Reanimate (Summon 20).
- **Hit with him:** Unleash or Protector, Fetch (0 cost, draw), Flatten (0 cost if Osty already attacked: 12 (16)), Rattle (extra hit per Osty attack this turn), Right Hand Hand, Poke, Snap, High Five (AoE + Vulnerable), Calcify (+4 (6) to Osty attacks), Squeeze (3 cost: 25 (30), and other Osty attacks +5).
- **Combo:** Fetch → Flatten (free) → Rattle.
- **Relic:** Bone Flute.
- Bone Shards and Sacrifice are a separate "sacrifice Osty" plan. **Don't mix them with Osty-growth cards.**

### 3. Doom execute (best vs single high-HP bosses)
- **Apply:** Negative Pulse (common: Block + 7 (11) Doom to all; signals the lane), Blight Strike, Scourge (13 (16) Doom + draw), Deathbringer (21 (26) Doom to all), No Escape (scales with existing Doom), Countdown, End of Days (29 (37) to all, executes immediately).
- **Multipliers:** Oblivion (0 cost: 3 (4) Doom per card played this turn), Reaper Form (attack damage also applies Doom), Time's Up (damage equal to Doom).
- **Defense:** Shroud (Block per Doom application), Death's Door, Undying Sigil, Book Repair Knife, Unsettling Lamp (doubles the first debuff).
- **Rule:** you need a closer (Reaper Form, No Escape, Time's Up, End of Days, or a Doom relic). Otherwise you just apply Doom slowly. Weak vs multi-enemy rooms.

### 4. Ethereal / velocity (splash)
- Pagestorm (draw when you draw Ethereal), Call of the Void, Spirit of Ash (4 (5) Block per Ethereal played), Banshee's Cry (9 cost, -2 per Ethereal played: 33 AoE), Defile (13 (17) Ethereal attack), Defy, Fear, Parse (Ethereal draw 3), Veilpiercer, Sculpting Strike.

## Packages to count
Count these in `player.deck` before every pick (general.md 12). Upgraded copies count too.
- **Souls**: Capture Spirit, Grave Warden, Reave, Severance, Dirge, Seance, Funerary Mask. Each Soul is a 0-cost Skill: draw 2, Exhaust.
  - Payoffs: Haunt (enemy loses 7 (9) per Soul played), Soul Storm (+4 (6) per exhausted Soul), Devour Life (Summon per Soul played), Death March (+4 (6) per card drawn this turn: a Soul played first adds 8 (12)).
  - Move up: payoffs once 2+ generators are in (aim for 2 Capture Spirit). Big Hat is weak without Soul producers.
- **Osty**: Summon (Bodyguard, Pull Aggro, Afterlife, Spur, Sic 'Em, Reanimate, Dirge, Devour Life, Bound Phylactery or Phylactery Unbound) and Osty attacks (Unleash or Protector, Fetch, Flatten, Rattle, Right Hand Hand, Poke, Snap, High Five, Squeeze).
  - Payoffs: Unleash adds Osty's current HP (Protector his max HP), so every Summon grows it. Calcify (+4 (6) to Osty attacks), Squeeze (other Osty attacks +5), Rattle (extra hit per Osty attack this turn), Sic 'Em (Summon when Osty hits) and Bone Flute (2 Block when Osty attacks) grow with your count of Osty attacks. Necro Mastery: enemies lose HP whenever Osty does.
  - Loses value: Bone Shards and Sacrifice (the sacrifice-Osty plan) in an Osty-growth deck. Strength cards likely add nothing to Osty attacks (unverified).
- **Doom**: Negative Pulse, Blight Strike, Scourge, Deathbringer, Countdown, Reaper Form, Plague Carrier (unverified).
  - Payoffs: No Escape (scales with existing Doom), Time's Up (damage equal to Doom), End of Days (29 (37) to all, executes at once), Oblivion (3 (4) Doom per card played this turn: 0-cost Souls feed it), Shroud (Block per Doom application), Undying Sigil, Book Repair Knife.
  - Move up: appliers pay only once you hold a closer (Reaper Form, No Escape, Time's Up, End of Days, or a Doom relic). With one, each applier and Shroud move up. No Doom rares by about floor 10: pivot to Souls.
  - Loses value: multi-enemy rooms without AoE, and healing bosses (Knowledge Demon).
- **Ethereal** (splash): Defile, Parse, Demesne, Big Hat; read get_deck for others. Payoffs: Spirit of Ash (4 (5) Block per Ethereal played), Banshee's Cry (9 cost, 2 less per Ethereal played), Pagestorm (draw when you draw Ethereal), Veilpiercer, Pull from Below, Eidolon. Payoffs only with several Ethereal cards.

## Energy and draw
- **Borrowed Time:** text changed repeatedly. Main v0.103.2 per the patch notes: 1 cost, +4 (6) Energy, cards cost +1 this turn. Earlier: 0 cost with self-Doom. Read the in-game text.
- **Neurosurge** (0 cost: +3 (4) Energy, draw 2, then 3 Doom on YOURSELF each turn). Use late in a fight, or in decks that end fights fast.
- **Others:** Friendship (-Strength, +1 Energy per turn), Demesne (Ethereal Power: +Energy and +draw each turn), Wisp, Invoke, Delay, Ivory Tile.
- **Shared Fate:** you lose 2 Strength, the enemy loses 2 (3). Fine, since Osty attacks don't use your Strength (unverified).

## Draft priorities
- **Act 1 (damage + Osty buffer):** Reave, Blight Strike, Defile, Sow (Retain AoE), The Scythe (grows +5 (7) each play; buffed), Death March, Capture Spirit, Grave Warden, Negative Pulse, Pull Aggro, Defy, Enfeebling Touch (enemy -8 (11) Strength this turn), Putrefy.
- **Premium:** Capture Spirit, Dirge, Haunt, Devour Life, Spirit of Ash, Soul Storm, Squeeze, Sic 'Em, Necro Mastery, Reaper Form, Deathbringer, Demesne, Sleight of Flesh (9 (13) damage per debuff applied), Banshee's Cry, Seance, Dredge, Cleanse.
- **Upgrade first:** Graveblast (loses Exhaust), Wisp (Retain), Dredge (Retain), Unleash, Capture Spirit (+1 Soul), Borrowed Time, Bodyguard.

## Traps
- Half-committing to two lanes. Big Hat without Soul producers. Bone Shards in an Osty-growth deck.
- Neurosurge early in long fights (self-Doom executes YOU when Doom >= your HP).
- Debilitate (nerfed to 2 turns) as a stall plan.
- Doom vs multi-enemy hallways without AoE.
- Neow's Bones and Leafy Poultice on this low-HP character.

## Boss notes
- **Doom shines:** Aeonglass (512 HP, Doom ignores its 33 Block; rated the highest-ceiling answer) and Kaiser Crab claws (a single target each).
- **Doom vs Knowledge Demon:** its heals push HP back above Doom.
- **Waterfall Giant:** killing it, even via Doom, still triggers the Steam Eruption explosion next turn. Block for it.
- **Insatiable:** DPS race. Doom applied early plus Time's Up, or Osty burst.
- **Lagavulin Matriarch:** her Strength siphon hurts your attacks, but Osty and Doom are mostly unaffected (unverified for Osty).
- **Test Subject:** Doom set before a phase change may not carry over to the next phase (unverified). Don't over-invest Doom near a phase threshold.
