# The bot's wiki

Pages the bot reads when it needs them and adds to when a fight teaches it something. The system prompt carries `strategy/rules.md` (the combat rules) in full; everything here is looked up on demand.

## What is here

- `enemies/<name>.md`: one page per monster: where it appears, HP, moves, its pattern, special rules.
- `encounters/act1-overgrowth.md`: every act 1 (Overgrowth) fight and how the act orders them.
- `cards/ironclad.md`: every Ironclad card with exact numbers and upgrades. `cards/status.md`: the status cards enemies add.
- `potions.md`: what each common potion does.
- `run/act1.md`: what an act gives between fights: floors, card reward odds, rest sites, heals.
- `lessons/`: pages the bot starts itself, for anything that doesn't belong to one enemy or card.

So far the enemy and encounter pages cover Overgrowth only, and the card pages the Ironclad only. Underdocks, acts 2 and 3 and the other characters have no pages yet.

## How a page is laid out

1. `# Title` on the first line.
2. One sentence on the second line saying what the page holds. The index the bot sees is built from these two lines.
3. `## Facts (game files, v0.107.1)`: read out of the game's own code for that version, at ascension 0, and checked twice. Don't edit this section from play. If the game on screen disagrees with it, the game is right: note the difference under Lessons with the date and the game version, so the facts can be re-read from the new version.
4. `## How to fight it` (enemy pages): a few bullets that follow from the facts. They have not been tested in play.
5. `## Lessons`: what the bot learned in real fights. One line each, newest last, starting with the date: `- 2026-10-12: Held Bash for the turn after Hiss; the 2 Vulnerable covered both big hits.` Remove a lesson that later proves wrong.

## Rules for the bot

- Before an elite, a boss, or an enemy you have no plan for, read its page.
- After a fight that went badly, or better than expected for a reason you can name, add one line under that enemy's Lessons.
- General lessons that apply to every run still go in `strategy/learned.md`.
- A new page follows the layout above and gets a lowercase, hyphenated file name.
- After a game update, the Facts sections are only as good as the version in their heading.
