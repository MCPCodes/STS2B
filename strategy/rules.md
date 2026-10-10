# Combat rules (from the game files, v0.107.1)

From the game's code, ascension 0. Card, relic or power text can change a rule here. A strategy note cannot.

## Turn order
Start of your turn:
1. Every enemy chooses its move. It does that move this round unless it dies, is stunned or an effect replaces the move.
2. Your Block goes to 0. Not on turn 1: fight-start Block lasts through enemy turn 1.
3. Energy is set to max_energy. Unspent energy is lost.
4. Draw 5.
5. Start-of-turn powers act: Strength per turn, Poison on you, HP costs.

End of your turn:
1. End-of-turn Block from powers and relics is added.
2. Ethereal cards in hand are exhausted, even with Retain.
3. Cards with an end-of-turn effect in hand resolve, left to right.
4. The hand is discarded, except Retain cards. "When discarded" effects do not trigger.
5. Your temporary Strength ends. End-of-turn debuff damage lands.

Enemy turn:
1. All enemy Block goes to 0, so Block from an enemy's move lasts through your whole next turn.
2. Poison on enemies triggers.
3. The starting enemies act in order e0, e1, ... A summoned enemy can act before a lower id, and does not act in the enemy turn it arrives in. Effects land at once: Vulnerable from one enemy raises its own later hits and those of enemies after it.
4. Vulnerable, Weak and Frail drop by 1 on everyone. They never drop at the end of your turn.

## Attack damage
One hit = (base + attacker's Strength), x 0.75 if the attacker is Weak, x 1.5 if the target is Vulnerable.
- Add first, then multiply, no rounding in between. Below 0 becomes 0. Round down once, at the end.
- Other bonuses fit the same order: flat ones add, percentages multiply.
- Each hit of a multi-hit attack is computed, rounded and blocked on its own. Strength adds to every hit.
- Example: 6 base, Strength 1, you Weak, target Vulnerable: 7 x 0.75 x 1.5 = 7.875, deals 7.
- Example: 5x2, Strength 2, target Vulnerable with 12 Block: each hit is 10.5. Hit 1 leaves 2 Block. Hit 2 breaks it and takes 8 HP. Total 20, not 21.
- If the target or the attacker dies, the remaining hits are lost. No retarget.
- Attacks on all enemies are computed per enemy. Random-target hits pick a living enemy anew each hit.
- Vulnerable applied by a card does not raise that card's earlier hit.

## Block
- Card Block and enemy-move Block = (base + Dexterity) x 0.75 if Frail, rounded down. Frail on an enemy cuts its Block.
- Block from powers, relics and potions ignores Dexterity and Frail.
- Block stops anything worded as damage: attacks, potions, powers that hit back, end-of-turn cards and debuffs. Block left from your turn absorbs the end-of-turn ones.
- Block does not stop anything worded as "lose HP": card and power costs, Poison, some end-of-turn cards. Same for enemy Block.
- Strength, Weak and Vulnerable change attacks only.

## Vulnerable, Weak, Frail, Strength, Dexterity
- Vulnerable: attacks taken x 1.5. Weak: attacks dealt x 0.75. Frail: card and move Block x 0.75. More stacks add time, not effect.
- A round is your turn plus the enemy turn after it. The amount shown on your turn is rounds left including this one: 1 Vulnerable on an enemy is gone before your next turn, 1 Weak on it covers the coming enemy turn.
- A debuff newly put on you skips its first drop: N stacks from an enemy last the rest of that enemy turn and N more rounds. Stacks added to one you already had do not skip.
- Strength: + amount to each attack hit. Dexterity: + amount to card Block. Both last the fight and can be negative.
- Temporary Strength or Strength loss is undone at the end of its owner's turn, so a loss put on an enemy covers its coming turn.
- Artifact cancels the next debuff on its owner, 1 per stack, Strength loss included. The card's damage still lands.

## Cards and piles
- A draw from an empty draw pile first shuffles the discard pile into it, without the card being played. Both empty: the draw stops.
- Hand limit 10. A draw into a full hand is skipped: the card stays on the draw pile. A card created in or returned to a full hand goes to the discard pile.
- A played card leaves the hand before its effect. Then: Power, out of the fight. Exhaust, exhaust pile, which is never reshuffled. Any other, discard pile.
- Innate: in the opening hand. More than 5 Innate cards raise the first draw, up to 10.
- X cost: takes all your energy, X = energy taken. Playable at 0 energy with X = 0. Play other cards first.
- An X card played for free by an effect uses your current energy as X and spends none.

## Intents
- "attack N" or "attack NxH": N is per hit, rounded down, with the enemy's Strength and Weak and your Vulnerable already in it. Total N x H. Do not add Strength again.
- Weak rounds per hit: 2x3 becomes 1x3 = 3, not 4.
- incoming_damage is the sum of all attack intents. Your Block is not subtracted.
- HP you lose = max(0, incoming_damage + blockable end-of-turn damage - Block) + lose-HP effects.
- Numbers are live: killing or stunning an enemy removes its part, Weak on it lowers it. Not in them: Vulnerable an enemy puts on you mid enemy turn, which raises the hits after it.
- Other intents have no number. A defend intent does not say how much Block. "statuscard: N" adds N cards to your piles.

## Card numbers
- A hand card's text is per hit, rounded down, with your Strength and Weak in it (Dexterity and Frail for Block). The target's Vulnerable is not in it.
- vs gives the text against each enemy id where it differs. Use vs when present. No vs entry for an enemy means the text number holds against it.
- Random-target cards never have vs. Compute their hits from the card's base, not from its text, which is already rounded: a 5-damage hit while Weak reads 3 and deals 5 to a Vulnerable enemy, not 4.
- Neither text nor vs counts enemy Block or a power that caps HP lost per hit.

## Potions
- No energy cost. In a fight, usable only on your turn.
- Potion damage and Block are fixed: no Strength, Weak, Vulnerable, Dexterity or Frail. Enemy Block and per-hit HP caps still apply.

## Win and loss
- Win: no living enemy except Minions. Minions die with the last main enemy. An enemy that flees counts as gone.
- The win is checked after each card or potion, never mid-card. A win on your turn skips the end-of-turn effects.
- After the last enemy dies the card finishes: its draws, Block and powers do nothing, its damage and HP loss to you still apply.
- You and the last enemy dying in the same action is a loss.
- Burning Blood: heal 6 after every won fight, up to max HP.

## If you know Slay the Spire 1, these are different here
- A new debuff on you skips its first drop even when you gave it to yourself.
- Frail and Dexterity change enemy Block.
- Ethereal beats Retain.
- Temporary Strength is taken back as a Strength loss. Artifact on the owner cancels it and the Strength stays.
- Thorns hits before the attack lands. The attack still lands.
- The fight does not end mid-card. A double kill is a loss.
