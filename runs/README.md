Run journals. The bot keeps the run in progress in `current.md` and writes a post-mortem when a run ends. Lessons get promoted from there: short general rules to `strategy/learned.md`, and lessons about one enemy, card, event or other topic to its page in the wiki under `strategy/wiki/`.

`current.md` starts with a header line that names its run, such as `# Ironclad, started 2026-10-10, Neow: <what was picked>`: the character, the start date and the Neow pick. Then three parts, in this order:

1. **Build plan**, written at Neow and rewritten when the run pivots: the archetype, the key cards and relics wanted, what to avoid, and what the deck still lacks for the act boss.
2. **Orders**: standing orders from the human ("go for a Strength build", "always take elites"), one per line. They hold until the run ends.
3. **Log**: one line per floor (floor, what happened, HP, what to do differently), then the post-mortem.

The bot re-reads the first two after a context compaction, so keep them short.

When `/play` starts, the bot checks the header and log against the live run. If the character differs, or the log has a post-mortem or a floor past the live one, the file is from a different run. It is archived whether that run finished or not: renamed to `YYYY-MM-DD-<character>.md` (the run's start date), adding `-2`, `-3`... if that name exists. Never overwrite an archive. A fresh `current.md` then begins.
