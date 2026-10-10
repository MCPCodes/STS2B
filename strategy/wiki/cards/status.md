# Status cards
The 12 status cards: cost, what playing one does, what it does in hand or when drawn, and which act 1 enemies add it.

## Facts (game files, v0.107.1)

### Rules for all of them
- Type Status. None can be upgraded; Armaments skips them. They are not Skills, so Corruption does not make them free.
- Act 1 enemies add them during the enemy turn. They last for that fight only.
- An Unplayable card leaves your deck only by being exhausted.
- End of your turn, in this order: Stampede's free Attack; Stone Armor's Block; Ethereal cards in hand are exhausted; each Beckon, Burn, Infection, Toxic and Wither in hand takes effect, left to right, and goes to the discard pile; the rest of the hand is discarded.
- "Blockable" damage is flat (no Strength, Weak or Vulnerable) and your Block absorbs it first. Block used up this way is gone before the enemy attacks. Block needed = enemy damage + blockable status damage in hand.
- HP lost to a status at end of turn counts as HP lost on your turn: Rupture gives Strength, Inferno deals its damage, Tear Asunder gains a hit.

### The cards
| Card | Cost | When played | In hand at end of turn, or when drawn | Added in act 1 by |
|---|---|---|---|---|
| Beckon | 1 | No effect. Goes to the discard pile, so it comes back. | End of turn: lose 6 HP. Block does not help. | Soul Fysh |
| Burn | Unplayable | | End of turn: 2 damage, blockable. | nobody |
| Dazed | Unplayable | | Ethereal: exhausted at end of turn. No damage. | Eye with Teeth, Haunted Ship |
| Debris | 1 | No effect. Exhausted. | Nothing. | nobody |
| Frantic Escape | 1, and 1 more after each play of that copy | Adds 1 to the Sandpit countdown on you. Goes to the discard pile. | Nothing. | nobody |
| Infection | Unplayable | | End of turn: 3 damage, blockable. | Phrog Parasite, Wriggler |
| Slimed | 1 | Draw 1. Exhausted. | Nothing. | Leaf Slime (M), Twig Slime (M), Leaf Slime (S) |
| Soot | Unplayable | | Nothing. | nobody |
| Toxic | 1 | No effect. Exhausted, so it is gone. | End of turn: 5 damage, blockable. | nobody |
| Void | Unplayable | | When drawn: lose 1 energy. Ethereal: exhausted at end of turn. | nobody |
| Wither | Unplayable | | End of turn: 3 damage, blockable. 3 more for each +1 in its name. | nobody |
| Wound | Unplayable | | Nothing. | Vantom |

- Void only costs energy when drawn, the turn's opening draw included: a Void in the opening 5 leaves 2 energy. Put in hand another way, it costs nothing.
- Without a Sandpit on you, playing Frantic Escape only wastes energy.

### Act 1 sources: Overgrowth
- Leaf Slime (M): 2 Slimed to the discard pile on enemy turns 1, 3, 5... It attacks on the turns between.
- Twig Slime (M): 1 Slimed to the discard pile on enemy turn 1, then an attack on turn 2. After that it is random, but never Slimed 2 turns in a row and never more than 2 attacks in a row.
- Leaf Slime (S): 1 Slimed to the discard pile every other turn, alternating with an attack. Which comes first is 50/50.
- Twig Slime (S) adds none.
- Where the slimes are: Slimes weak fight (1 medium, both small), Slimes normal fight (both medium, both small; 3 or 4 Slimed arrive on turn 1), Flyconid fight (1 medium), Slithering Strangler fight (1 medium or 2 small in 2 of 3 cases).
- Eye with Teeth: Fogmog summons it on enemy turn 1. From enemy turn 2 it adds 3 Dazed to the discard pile every turn. It has 6 HP. At 0 HP it skips that turn's Dazed and is back at 6 HP for your next turn. It dies for good with Fogmog.
- Phrog Parasite (elite): 3 Infection to the discard pile on enemy turns 1, 3, 5...
- Wriggler: 1 Infection to the discard pile each time it wriggles, every other turn. The 4 Wrigglers that appear when the Phrog Parasite dies wriggle 2 at a time. The Dense Vegetation event fight (DenseVegetation) is 4 Wrigglers too.
- Vantom (boss): 3 Wounds to the discard pile with its big hit on enemy turns 3, 7, 11..., blocked or not.

### Act 1 sources: Underdocks
- Haunted Ship: on enemy turn 1 only, 3 Weak on you and 5 Dazed to the discard pile.
- Soul Fysh (boss): on enemy turns 1, 6, 11..., 1 Beckon into the draw pile at a random place and 1 into the discard pile. On enemy turns 3, 8, 13..., an attack, then 1 Beckon into the discard pile.

### Later acts
- Act 2: Chomper (3 Dazed to the discard pile), Entomancer (each attack hit on it, blocked or not, puts 1 Dazed into your draw pile, later up to 3), Myte (2 Toxic straight into your hand), The Insatiable (Sandpit 4 and 6 Frantic Escape, 3 in the draw pile and 3 in the discard pile).
- The Insatiable's Sandpit: 4 on enemy turn 1, then 1 less at the start of each later enemy turn. At 0 you die: the start of enemy turn 5 if you play no Frantic Escape. Each play delays it 1 turn.
- Act 3: Mecha Knight (4 Burn straight into your hand), Test Subject (3 Burn to the discard pile; in its 2nd form, 1 Wound to the discard pile for each of its hits that gets through your Block), Slimed Berserker (10 Slimed to the discard pile), Noisebot (2 Dazed, 1 to the discard pile and 1 into the draw pile), Aeonglass (1 Wither into your hand after every 6th card you play; its buff move adds +1 to every Wither you have, then 1 Wither to the discard pile).
- No enemy adds Debris, Soot or Void.
- Underdocks and later-act names here are split from the game's class names (HauntedShip, SoulFysh, TheInsatiable, MechaKnight, TestSubject, SlimedBerserker), not read from its text.

### Ironclad cards against them
- You choose the card to exhaust: Burning Pact, Brand, upgraded True Grit.
- Random card in hand: True Grit, Cinder.
- Every non-Attack in hand: Second Wind (5 Block per card, 7 upgraded). The whole hand: Fiend Fire (1 hit per card), Stoke.
- Havoc: an Unplayable status on top of the draw pile is exhausted unplayed. A playable one (Beckon, Slimed, Frantic Escape) is played for free, then exhausted. Cascade only moves an Unplayable status to the discard pile.
- Playing Slimed, Debris or Toxic exhausts it. That is a card exhausted this turn for Evil Eye and Forgotten Ritual, and it triggers Feel No Pain and Dark Embrace.
- Dazed and Void exhausted at end of turn trigger Feel No Pain (the Block is there for the enemy turn) and Dark Embrace (the card drawn stays for next turn). That is too late for Evil Eye and Forgotten Ritual.
- Slimed draws nothing after Battle Trance.
- Pillage: a drawn status is a non-Attack and ends its draw chain.

## Lessons
(none yet)
