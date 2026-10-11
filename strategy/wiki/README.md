# The bot's wiki

Pages the bot reads when it needs them and adds to when a fight teaches it something. The system prompt carries `strategy/rules.md` (the combat rules) in full; everything here is looked up on demand.

## What is here

- `enemies/<name>.md`: one page per monster: where it appears, HP, moves, its pattern, special rules.
- `encounters/act1-overgrowth.md`: every act 1 (Overgrowth) fight and how the act orders them.
- `cards/ironclad.md`: every Ironclad card with exact numbers and upgrades. `cards/status.md`: the status cards enemies add.
- `potions.md`: what each common potion does.
- `run/act1.md`: what an act gives between fights: floors, card reward odds, rest sites, heals.
- `lessons/`: pages the bot starts itself, for anything that doesn't belong to one enemy or card.

So far the enemy and encounter pages cover both first acts, Overgrowth and Underdocks (`encounters/act1-underdocks.md`), and the card pages the Ironclad only. Acts 2 and 3 and the other characters have no pages yet.

## How a page is laid out

1. `# Title` on the first line.
2. One sentence on the second line saying what the page holds. The index the bot sees is built from these two lines.
3. `## Facts (game files, v0.107.1)`: read out of the game's own code for that version, at ascension 0, and checked twice. Don't edit this section from play. If the game on screen disagrees with it, the game is right: note the difference under Lessons with the date and the game version, so the facts can be re-read from the new version.
4. `## How to fight it` (enemy pages): a few bullets that follow from the facts. They have not been tested in play.
5. `## Lessons`: what the bot learned in real fights, once checked. One line each, newest last, in one form: `- (Character, YYYY-MM-DD, <fight id or floor>) <lesson>`, for example `- (Ironclad, 2026-10-12, 2026-10-12 A1 F4) Held Bash for the turn after Hiss; the 2 Vulnerable covered both big hits.` The fight id is the date, act and floor, as the fight log (`fights.jsonl`) names fights; the floor alone (`floor 4`) works too. Remove a lesson that later proves wrong.

## Lessons wait for review

The bot doesn't add lessons to pages or to `strategy/learned.md` itself. A new lesson goes to a pending file in `strategy/pending/`, at the page's path below `strategy/`: `strategy/pending/wiki/enemies/jaw-worm.md` holds the lessons waiting for `enemies/jaw-worm.md`, and `strategy/pending/learned.md` the ones for `strategy/learned.md`. In the game the notes tools do this when the bot appends to a page; Claude Code writes the pending file directly. Nothing the bot reads loads these files (not its prompt, the wiki index, `notes_read` or `notes_list`), so a held lesson can't steer later fights. Only lesson lines are held: a page the bot starts goes in at once with its summary and other sections, and so does anything it writes above `## Lessons` on a page without Facts, so look over those too. Once a lesson has been checked, move it under the page's `## Lessons` by hand and delete it from the pending file. Removing a wrong lesson from a page works at once.

## Rules for the bot

- Before an elite, a boss, or an enemy you have no plan for, read its page.
- After a fight that went badly, or better than expected for a reason you can name, write one lesson line for that enemy's page. It is held for review (above).
- General lessons that apply to every run are held the same way for `strategy/learned.md`.
- A new page follows the layout above and gets a lowercase, hyphenated file name.
- After a game update, the Facts sections are only as good as the version in their heading.
