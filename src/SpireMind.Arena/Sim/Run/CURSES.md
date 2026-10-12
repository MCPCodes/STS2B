# Run mode: the curses and Neow's Fury

Chunk 11 of the run-mode plan (`sts2-notes/run-mode-v0.107.1/plan.md`, 5.2). The cards are content like any other (`Sim/README.md` says how a card is written); this page is what is particular to them: a deck card that acts as the card in the deck, a card removed from the deck inside a fight, and the order in which a fight's pile choice hands its picks back. Everything is the game's (v0.107.1), read from the card's class in `MegaCrit.Sts2.Core.Models.Cards`.

## Files

| File | Holds |
| --- | --- |
| `../Content/Curses.cs` | the 18 cards of the game's curse pool and Neow's Fury; by name, the five other event and quest cards an act 1 run can be given, as not built yet |
| `../Content/CursesChecks.cs` | the checks |
| `../Engine/Fight.Deck.cs` | `Fight.RemoveFromDeck`: a deck card leaves the deck inside a fight |
| `../Engine/ChoicePicks.cs` | the picks of a fight's card choice, in the order the game's screen holds them |

## The cards

All 18 curses are built, not only the 13 that a fixed source or Neow's Bones gives: a transformed curse becomes any other of the eighteen with equal chance (`CardPools.TransformOptions`), so with all of them here that pick is always the game's own and nothing is drawn again. A curse goes into the deck with `DeckEdits.AddCurses(run, [Content.Registry.Card("Injury")])`, Neow's Fury with `DeckEdits.AddNew(run, "NeowsFury")`.

| Card | What it is |
| --- | --- |
| Ascender's Bane, Clumsy, Curse of the Bell, Folly, Greed, Injury, Poor Sleep, Writhe | keywords only (Unplayable, with Ethereal, Innate, Retain, Eternal as the class lists them) |
| Spore Mind | costs 1, does nothing, Exhaust |
| Bad Luck, Regret | in hand at the end of the turn: HP loss that ignores Block (13; 1 per card in the hand) |
| Decay | in hand at the end of the turn: 2 damage, blockable, never modified (as Burn) |
| Debt | in hand at the end of the turn: lose 10 gold, or what is left (`Fight.LoseGold`; a single fight has no gold) |
| Doubt, Shame | in hand at the end of the turn: 1 Weak, 1 Frail, with no applier and the card as source |
| Normality | in the hand: `ShouldPlay` says no to every card, played by hand or by an effect, once 3 runs of card effects have started this turn |
| Enthralled | costs 2, does nothing; in the hand: `ShouldPlay` says no to every other card played by hand (an effect's plays go through) |
| Guilty | in the DECK: counts the fights that end and leaves the deck after the fifth |
| Neow's Fury | Attack, cost 1, Exhaust, Ancient rarity, event pool: 10 damage (14), then up to 2 (3) cards of the discard pile into the hand |

- **Regret counts early.** Its hook object notes the size of the hand in "before side turn end", the last hook before the hand is touched, and the end-of-turn effect uses that number: an Ethereal card that was exhausted in between and an end-of-turn card that resolved before it still count.
- **Normality counts runs, and auto-plays.** The count is the fight history's "card play started" entries of this turn: a card that runs twice counts twice, and a card an effect plays counts and is stopped (Havoc as the third card of a turn exhausts the top card unplayed). Its text shows what is left in any pile of a running fight, as the game's does.
- **Eternal** (Ascender's Bane, Bad Luck, Curse of the Bell, Enthralled, Folly, Greed) is the keyword `CardKeywords.Eternal`; `DeckEdits.IsRemovable` and `IsTransformable` read it. It is written last in a card's text, after Exhaust.
- **Titles** are the game's English names (its `cards` table; "Clumsy" is also what Mark's run record of seed 1BUSLN0FY5 shows for the curse of This or That). Texts are the arena's own words.

## Guilty: a card that acts in the deck

In the game every deck card hears the run's hooks and some of the fight's, beside the copy of it that a fight puts into its piles (`RELICS.md`, "Who hears a hook"). Guilty is the one card in reach that uses this: its hook class carries the marker `IListensInDeck` and overrides `AfterCombatEnd(Fight f)` alone. The same class serves the copy in the piles, which hears the hook too and does nothing, because the class tests `Card.Pile == PileType.Deck`, as Guilty.cs does.

- The count is a field of the hook object (the game's saved property); the card's number `Combats` is what is left and is what the text shows. Both go into a fight with the deck card and come back with it; a copy of the run or of the fight has its own.
- A fight's copy of Guilty is made when the fight starts, so its text shows what was left then.
- Only a won fight counts ("after combat end" is sent on a win; a lost fight ends the run). HP lost outside a fight is no fight.
- After the fifth fight it removes itself with `f.RemoveFromDeck(Card)`. Deck cards are asked before relics, so a relic's own "after combat end" already sees the deck without it.

## A card removed from the deck inside a fight

`Fight.RemoveFromDeck(deckCard)` is the game's `CardPileCmd.RemoveFromDeck`, which is one command in a fight and outside (outside a fight use `DeckEdits.Remove`). "Before card removed" (`IDeckListener.BeforeCardRemoved`) is heard while the card is still in the deck, by the run's listeners alone (listening deck cards, relics, potions; the hook is handed the fight's `RunContext`), then the card leaves the fight's deck list and is in no pile. The run gets the deck without it when the fight ends. The cards the fight made from it stay in their piles; `DeckVersionOf` gives null for them. A card that is not in the fight's deck is refused, as in the game.

## The order of a pile choice's picks

Neow's Fury asks with `f.ChooseFromPile(PileType.Discard, prompt, 0, count)`: minimum and maximum differ, so the screen has a confirm button, is shown also for a single card, and stays open at the maximum. `count` is the card's number or the room left in the hand, whichever is less (the card itself is in the Play pile); with no room, or an empty discard pile, nothing is asked.

The game's pile screen and plain grid keep their picks in a .NET HashSet and hand that set back, so the cards reach the effect, and the hand, in the set's order: the order picked, except that a new pick takes the place the pick taken back last left free. Bash and Defend picked, Bash taken back, Anger picked: the hand gets Anger, then Defend. The mod's reader lists `selected` from the same set. `ChoicePicks` holds a fight's picks that way for the grid kinds (`combat_pile_select`, `select`, `choose`) and as a plain list for the hand selection, where the game keeps a list. It wraps the run-level screens' `SelectionSet`; `CursesChecks` holds it against a real HashSet. Before this, `PendingChoice.Selected` was a list in pick order for every kind; no card until Neow's Fury could show the difference (every pile choice was one card).

## Left out, by name

- **Peck, Toric Toughness, Spoils Map, Byrdonis Egg, Byrd Swoop**: the other event and quest cards an act 1 Ironclad run can be given. Each is excluded in `Curses.cs` with the chunk that builds it; whoever builds one takes its line out. `Completeness.EventAndQuestCards` lists all six.
- **Neow's Bones** (chunk 33) picks among the ten curses whose `GameCard.GeneratedByModifiers` is true; all ten are built.
- **A run history entry for a removed card** (the game writes one in `RemoveFromDeck`): no floor log yet.
- **Take-backs on the hand selection**: the arena's `hand_select` screen has no action that takes a pick back, so its list order is the pick order.

## Checks

`CursesChecks`: every card's type, rarity, cost, target, keywords, numbers and text against a table read off the classes; each keyword and each end-of-turn effect at its real moment in a fight (Decay with Block, with Vulnerable and Strength; Bad Luck and Regret through Block; three Debts on 25 gold; Doubt and Shame over three turns, stacked, and against Artifact; Regret beside an Ethereal card and other end-of-turn cards); Normality and Enthralled with cards played by hand, by Havoc and by Distilled Chaos, and a card that runs twice; Guilty over runs of six and eight scripted fights with every number of its text, a replay, a copy, a second Guilty added later, and a relic that writes down the order of what it hears; `RemoveFromDeck` on its own; Neow's Fury through its choice screen with every label, with one card, with none, with a full hand, upgraded, killing, and played by an effect; the set order of the picks in three sequences and against a real HashSet; the Eternal curses in the deck commands; sixty curse transforms against a second stream; the game's own ids and the deck of Mark's run on seed 1BUSLN0FY5 after its event; and a run of six of the game's fights with all nineteen cards in the deck, played by a fixed rule, which must go through, replay to the same state and end the same from a copy taken halfway. Sixty-eight deliberate faults in the code were each caught by a check.

What no check can tell apart, because the result is the same either way: Debt taking the smaller of 10 and the gold held (losing gold stops at 0 anyway), the skip flag Doubt and Shame set on a new power (applying a new debuff to the player sets it already), and their missing applier (no built power or relic asks who applied Weak or Frail, or changes what a creature gives). Each is written as the game's class has it.
