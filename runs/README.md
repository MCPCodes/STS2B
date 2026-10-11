Run journals. The bot keeps the run in progress in `current.md` and writes a post-mortem when a run ends. Lessons get promoted from there: short general rules for `strategy/learned.md`, and lessons about one enemy, card, event or other topic for its page in the wiki under `strategy/wiki/`. Both wait in `strategy/pending/` for review first (see `strategy/wiki/README.md`).

`current.md` starts with a header line that names its run, such as `# Ironclad, started 2026-10-10, Neow: <what was picked>`: the character, the start date and the Neow pick. Then three parts, in this order:

1. **Build plan**, written at Neow and rewritten when the run pivots: the archetype, the key cards and relics wanted, what to avoid, what the deck still lacks for the act boss, and a `Packages:` line counting each package's pieces (`Packages: Strength 3 (Inflame, Demon Form, Brand), Vulnerable 2 (Bash, Tremble)`), rewritten after every pick that changes a count.
2. **Orders**: standing orders from the human ("go for a Strength build", "always take elites"), one per line. They hold until the run ends.
3. **Log**: one line per floor (floor, what happened, HP, what to do differently), then the post-mortem.

The bot re-reads the first two after a context compaction, so keep them short.

When `/play` starts, the bot checks the header and log against the live run. If the character differs, or the log has a post-mortem or a floor past the live one, the file is from a different run. It is archived whether that run finished or not: renamed to `YYYY-MM-DD-<character>.md` (the run's start date), adding `-2`, `-3`... if that name exists. Never overwrite an archive. A fresh `current.md` then begins.

In co-op each bot copy keeps its own journal in `runs/bot-<port>/current.md` (such as `runs/bot-17773/current.md`) and archives its runs in that folder, so bots never write over each other's run or over Mark's `runs/current.md`. The bot still calls it `runs/current.md`; the mod maps the name. Lessons are held for review in the shared `strategy/pending/` as usual.
