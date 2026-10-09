# Notes for Claude working in this repo

- Two jobs happen here: developing the SpireMind mod, and playing the game with `/play`. When playing, follow `.claude/commands/play.md` and don't edit code.
- Never copy code from other STS2 AI mods (STS2MCP, STS2-Agent and others). Reading them for ideas or game API names is fine.
- Never commit decompiled game code. It lives in `~/Projects/sts2-decompiled` on Mark's Mac.
- `src/SpireMind.Core` must not reference the game; it is unit tested on any machine with `scripts/test.sh`. Game-specific code goes in `src/SpireMind/Game`.
- Touch Godot or game objects only on the main thread (`MainThreadQueue.Run`). HTTP handlers run on worker threads.
- Prefer the game's own commands and action queue for doing things, so co-op stays in sync. Simulating UI clicks is the fallback.
- Action ids are short and stable within a snapshot. Combat: `play:<handIndex>` or `play:<handIndex>><target>` (targets `e0`, `e1`... index the fight's enemies, `p0`... its players), `potion:<slot>[><target>]`, `discard_potion:<slot>`, `end_turn`; hand select: `select:<handIndex>`, `confirm`. Map `map:<n>`. Rewards `reward:<n>`. Card reward `card:<n>`, `skip`, `reroll`, `sacrifice`. Card select `card:<n>`, `bundle:<n>`, `confirm`, `cancel`, `skip`. Shop `open`, `close`, `buy:card<n>`, `buy:colorless<n>`, `buy:relic<n>`, `buy:potion<n>`, `buy:remove`. Rest site and events `option:<n>`. Treasure `open`, `relic:<n>`. Tutorial popups and tips `dismiss`; game over `continue`. A room's proceed button is always `proceed` (rewards, shop, rest, treasure); an event's Leave is one of its `option:<n>`. Potion ids ride along on every screen that offers them (combat, map, rewards, shop, rest, treasure, event).
- Target the public Steam branch (v0.107.1, commit 59260271 as of 2026-10-09), not public-beta. After a game update, rebuild and recheck any reflected or patched members.
