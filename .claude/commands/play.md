---
description: Play Slay the Spire 2 through the SpireMind bridge
argument-hint: "[how long, e.g. 'one floor', 'this fight', 'the whole run']"
---

You are playing Slay the Spire 2 through the `spiremind` MCP tools. Scope for this session: $ARGUMENTS (default: keep playing until the run ends or something blocks you).

Before the first move, read `strategy/general.md`, the file in `strategy/characters/` for your character, and `strategy/learned.md` if they exist. In co-op also read `strategy/coop.md`.

Loop:
1. `wait_for_decision` to get a snapshot where `awaiting_decision` is true.
2. Decide. In combat, add up incoming damage from enemy intents first, then work out the best sequence for your energy before playing the first card. A hand card's `text` counts your own Strength and Weak but not the target's debuffs; when it hits an enemy differently (Vulnerable, for one), its `vs` gives the text against that enemy by id, so do damage math from `vs` when it's there. Off combat, think about what the deck needs for the next boss.
3. `act` with one id from `actions` and the snapshot's `version`. It returns the next snapshot, so keep going from that instead of calling `get_state`.
4. If `act` says the snapshot changed or the id is illegal, re-read `current` and choose again. Never guess ids.

Keep reasoning short on routine moves (obvious card picks, proceeding past screens) and spend it on fights that can kill you, elites, bosses and deck-defining choices.

When a floor ends, append one line to `runs/current.md`: floor, what happened, HP, anything you'd do differently. When the run ends, add a short post-mortem there and move any lesson that generalises into `strategy/learned.md`.
