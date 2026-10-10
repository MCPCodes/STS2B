# The in-game brain

The mod can play by itself: it calls the Claude Messages API directly with Mark's API key, so nothing runs besides the game. When the API credit or the monthly cap runs out, it falls back to Mark's Claude subscription by starting Claude Code headless in the background, which drives the same tools over MCP. Running `/play` in Claude Code by hand still works too, for debugging.

## What the player does

1. Once: create an API key in the Claude Console and save it to `~/.config/spiremind/api_key` (one line, `chmod 600`). The mod never writes the key anywhere and never logs it.
2. In game, type `/start` in the chat. The bot plays from wherever the run stands. `/stop` stops it (no more API calls). The choice is remembered: after `/start`, the bot starts by itself the next time a run is in progress, until `/stop`.
3. Chat works as in `docs/chat.md`. `/pause` and `/resume` still mean "hands off for a moment" and work with either brain.
4. More commands: `/budget` (spend this month and this run, the model, the cap), `/model opus|sonnet|haiku`, `/effort low|medium|high`.

## Settings

`~/.config/spiremind/config.json`, all optional:

```json
{
  "repo": "/Users/mark/Projects/STS2B",
  "model": "claude-opus-5-5",
  "effort": "medium",
  "monthly_budget_usd": 200,
  "autostart": false
}
```

`autostart` is what `/start` and `/stop` flip. `repo` is where the brain reads its prompt and strategy notes and keeps its run journal. When the folder is missing it falls back to the copies `scripts/install.sh` puts in the mod folder (`brain/`), and keeps the journal under `~/.config/spiremind/runs/`.

Spend is tracked in `~/.config/spiremind/spend.json` per calendar month (UTC), from each response's `usage` and the price table in `ModelPrices`. There is no API to read the credit balance with a normal key, so this is the mod's own estimate. Reaching the monthly cap stops the brain with a chat line.

## How it works

```
Brain (background task)                         BotTools (same instance the MCP endpoint uses)
  system = play prompt + strategy notes  ──────►  get_state, wait_for_decision, act, act_sequence,
  tools  = BotTools specs + notes tools           say, get_deck   (+ notes_read, notes_write: brain only)
  loop: POST /v1/messages ─► tool_use blocks ─► run each tool in-process ─► tool_result ─► repeat
```

- **ClaudeClient** (Core): `POST https://api.anthropic.com/v1/messages` with `x-api-key`, `anthropic-version: 2023-06-01`. No SDK, so the mod stays one DLL. Retries 429 (honouring `retry-after`), 529 and 5xx with exponential backoff and jitter, at most 5 tries. Classifies errors so the brain can tell the human what happened: out of credit (402, or 400 whose message contains "credit balance is too low"), spend limit (400 "reached your specified API usage limits", or 429 with `enforced_spend_limit_reached`), bad key (401), everything else.
- **Request**: model and `output_config.effort` from settings; thinking left to its adaptive default (Opus can't turn it off); no temperature. `system` is one text block holding the play prompt and strategy notes with `cache_control: {type: ephemeral, ttl: "1h"}`, and the request sets top-level automatic caching so the growing conversation is cached too. Tools are sent with `strict: true` and `additionalProperties: false`. `tool_choice` stays auto (any/tool are refused by Opus and Sonnet 5.5). `max_tokens` 16000.
- **Conversation**: assistant content is kept exactly as received (thinking blocks with signatures included) and sent back unmodified. Tool results go back as one user message of `tool_result` blocks, JSON text, `is_error` from the tool.
- **No idle spending**: when the model calls `wait_for_decision`, the brain keeps waiting locally (repeated in-process waits, no API calls) until there is a decision, a chat message, or the brain is stopped. Menus, enemy turns, teammates and pauses cost nothing.
- **Fresh context instead of endless history**: once the conversation passes 60k input tokens and the bot reaches the map, or passes 150k anywhere, the brain starts a new conversation. Its first user message holds the run journal (`runs/current.md`: Build plan, Orders, floor log) and the last 10 chat lines, and asks it to carry on. The system prompt and tools are unchanged, so their cache survives.
- **end_turn without a tool call**: the model thinks it is done. If a run is in progress, the brain answers "Keep playing: call wait_for_decision." Outside a run it idles locally until a run starts or the human writes.
- **Notes tools** (brain only; Claude Code has its own file tools): `notes_read {file}` and `notes_write {file, content}` for `runs/current.md` and `strategy/learned.md` under the configured folder. Nothing else is readable or writable.
- **Transcripts**: each conversation is appended to `~/.config/spiremind/transcripts/<date>.jsonl` (requests without the system prompt, responses with usage and cost) for debugging and tuning.

## Subscription fallback

`SubscriptionBrain` (game side, since it spawns a process) runs `claude -p "/play the whole run"` in the repo folder. It takes over when there is no API key, the key is rejected (401), the credit is gone (402, or a 400 mentioning "credit balance"), or the mod's monthly cap is reached. It never takes over for 429, 529, 5xx or network errors; those are retried with backoff. The switch lasts until the next run starts, when the API is tried again, and the chat says which brain is playing. Mark's Mac has the CLI from Homebrew (v2.1.287 on 2026-10-09). Flags, per the Claude Code headless docs:

```
claude -p "/play the whole run" --session-id <uuid> --strict-mcp-config
  --mcp-config '{"mcpServers":{"spiremind":{"type":"http","url":"http://127.0.0.1:<port>/mcp"}}}'
  --tools "Read,Edit,Write,ToolSearch" --permission-mode dontAsk
  --allowedTools "mcp__spiremind__*" "Edit(//<repo>/runs/**)" "Edit(//<repo>/strategy/**)"
  --output-format stream-json --verbose
```

- The binary is found by absolute path, since a game started from Steam has no shell PATH: `/opt/homebrew/bin/claude`, `~/.local/bin/claude`, `/usr/local/bin/claude`, `~/.claude/local/claude`, or `claude_path` in config.json. Before starting, `claude auth status` must exit 0; otherwise the chat says the subscription isn't signed in.
- The child environment drops `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN` and `CLAUDE_CODE_USE_*` so it uses the subscription login, not a key.
- Started off the main thread with `System.Diagnostics.Process`, stdout read line by line (stream-json): `system/init` is logged with the MCP server status, assistant text is logged, the `result` line ends the run. A run that ends while the game run is still going is resumed with `--resume <uuid>` and every flag repeated, at most a few times in a row.
- `/stop` and quitting the game end it (SIGINT, then kill after a few seconds). A subscription limit message in the output stops it and is relayed to the chat.
- The play prompt is the same `.claude/commands/play.md`, so both brains behave alike. Chat reaches it through the MCP tool results as usual.

## act_sequence

Both brains get `act_sequence {version, actions: [id, ...]}` to play several actions from one snapshot, typically a whole turn. Ids are from the snapshot named by `version`. The first runs as `act` does. Before each later step, the tool looks up that id's label in the original snapshot and runs the action with the same label in the current snapshot (two copies of the same card have the same label and are interchangeable, so shifting hand indices don't matter). It stops early, returning what it did and the current snapshot, when:

- the label is no longer offered,
- the screen is no longer the one the sequence started on (a hand selection opened, the fight ended),
- the game doesn't settle into a decision,
- an action is refused by the game or changes nothing,
- a chat message arrives or the bot is paused.

A target's label has " (e1)" after the enemy's name only while another living enemy shares it, so when a twin dies the tool also accepts the action at the same target whose label matches once that tag is dropped. Before anything runs, every id must be legal in the snapshot, the version must match and the game must be waiting on the bot; otherwise it is refused like `act`, and nothing runs. It takes 1 to 12 ids. The result lists what ran, then why it stopped and which ids didn't run, then the snapshot: `{"done": [{"id", "label"}, ...], "stopped", "not_run", "warning", "next"}`, with `stopped` and `not_run` only when it stopped early. It is an error only when nothing ran.

`play.md` tells the model to end a sequence after any card or potion that draws, discovers, creates or transforms cards, has a random result it needs to see, or opens a choice, so it sees what it got before planning further.

## Cost

Per decision on Opus 5.5 (input $4, cache read $0.20, output $20 per million tokens): about 30k cached tokens plus 2k fresh plus 500 to 1500 output, roughly $0.02 to $0.04. With `act_sequence` a fight turn is usually one decision, so a full run should be on the order of 200 decisions.
