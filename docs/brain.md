# The in-game brain

The mod can play by itself: it calls the Claude Messages API directly with Mark's API key, so nothing runs besides the game. Without a key, or when the API credit, the Console spend limit or the monthly cap runs out, it falls back to Mark's Claude subscription by starting Claude Code headless in the background, which drives the same tools over MCP. Running `/play` in Claude Code by hand still works too, for debugging (type `/stop` in game first, since two players would cancel each other's waits).

## What the player does

1. Once: create an API key in the Claude Console and save it to `~/.config/spiremind/api_key` (one line, `chmod 600`; the README has the Terminal commands). The mod never writes the key anywhere and never logs it. `ANTHROPIC_API_KEY` in the game's environment works too, but the file wins. Then run `scripts/dev-host.sh --brain` to check the key and the API calls without the game (Smoke test, below).
2. In game, type `/start` in the chat. The bot plays from wherever the run stands. `/stop` stops it (no more API calls, Claude Code is told to quit, and an `act_sequence` or `wait_for_decision` still running for it ends). The choice is remembered: after `/start`, the bot starts by itself the next time a run is in progress, also after a restart of the game, until `/stop`. After a restart the chat says who plays when the first run starts. `/start` is refused while another MCP client (a `/play` session by hand) has called a game tool on `/mcp` in the last 30 seconds: stop it there first.
3. Chat works as in `docs/chat.md`. `/pause` and `/resume` still mean "hands off for a moment" and work with either brain.
4. More commands, each answered with a line in the chat:
   - `/budget`: API spend this month and this run, the request count, the model and effort, and what is playing and why.
   - `/model [opus|sonnet|haiku|fable]`: shows the model, or switches it (a full model id works too, if the price table knows it).
   - `/effort [low|medium|high|xhigh|max]`: shows or sets the thinking effort.
   - `/help` lists them all.

`/start`, `/stop`, `/model` and `/effort` save to config.json. A changed model or effort applies from the next request and starts a fresh API conversation: the cache belongs to a model, thinking blocks to the model that wrote them, and a new effort invalidates the cached messages, so a short restart costs less than the old history. Claude Code gets the model when a session starts, so while the subscription plays a new model waits for its next session (the reply says so), and the effort never reaches it.

## Settings

`~/.config/spiremind/config.json`, all optional:

```json
{
  "repo": "/Users/mark/Projects/STS2B",
  "model": "claude-opus-5-5",
  "effort": "medium",
  "monthly_budget_usd": 200,
  "autostart": false,
  "claude_path": "/opt/homebrew/bin/claude"
}
```

`autostart` is what `/start` and `/stop` flip. `model` takes the same short names as `/model`. `monthly_budget_usd` is the mod's own cap; 0 means the API is never used. `claude_path` is only needed when Claude Code isn't in one of the usual places (below). `max_tokens` (not shown, normally left out) fixes the API requests' `max_tokens` at a number from 1024 to 128000; without it the brain sizes it from the effort (Request, below). Keys the mod doesn't know are kept when it saves, and a value it can't use falls back to its default with a line in the log.

Each copy of the game reads its settings from its own config folder: `~/.config/spiremind` for Mark's copy, `~/.config/spiremind/bot-<port>` for a bot copy (co-op, below), or whatever `SPIREMIND_CONFIG_DIR` names. A bot copy's folder holds its own config.json, transcripts and autostart, layered over the shared `~/.config/spiremind/config.json`: the shared file is read first, then the copy's own over it. `monthly_budget_usd` in a copy's own file is ignored with a log line, since one cap holds for every copy; set it in the shared file. The API key is the copy's own `api_key`, else the shared `~/.config/spiremind/api_key`, else `ANTHROPIC_API_KEY`; the log says "from api_key" or "from the shared api_key", never the key. A bot copy's `/model`, `/effort`, `/start` and `/stop` save only `model`, `effort` and `autostart` to its own config.json and never touch the shared one.

Spend is tracked in `~/.config/spiremind/spend.json` per calendar month (UTC), from each response's `usage` and the price table in `ModelPrices`. A model the table doesn't know is priced at the highest of each rate in it, so the estimate errs high. There is no API to read the credit balance with a normal key, so this is the mod's own estimate. Reaching the monthly cap stops the API brain with a chat line, and the subscription takes over. Every copy counts into that one shared spend.json, so the cap holds across all of them (`/budget` then says "this month across every copy"). Each update reads, merges, changes and writes the file under a cross-process lock (`~/.config/spiremind/locks/spend.json.lock`), so two copies adding at once never lose a cost. Before each request a copy also writes an in-flight estimate under its own id ("main", "bot-17773"): its last request's cost, at least $0.05. A copy refuses to start a request when the month's total plus the other copies' in-flight estimates has reached the cap, so three bots can't each start a request that together goes over it. The estimate is removed when the cost is added or the request fails unbilled, and one older than 10 minutes no longer counts. If the lock can't be had within 5 s, the copy counts without it and logs once.

### Folders

`BrainPaths` works them out for every brain start, so a `repo` added to config.json applies at the next `/start`. The log shows them when they change.

- **The repo** (the STS2B checkout): `repo` from config.json when that folder has `.claude/commands/play.md`. Otherwise the folder named in the mod's `brain/repo_path`, which `scripts/build.sh` writes with the checkout it built from, when that folder has the play prompt. Otherwise none. So a mod installed with `scripts/install.sh` finds the repo without any config, as long as the checkout stays where it was built.
- **The prompt folder**, which the API brain's system prompt is built from: the repo, else the mod's `brain/` folder, where `scripts/build.sh` stages copies of `play.md` and the `strategy/` notes.
- **The notes root**, which holds the run journal `runs/current.md`, `strategy/learned.md` and the wiki's pages under `strategy/wiki/`: the repo, else the shared `~/.config/spiremind` (also for a bot copy, so the notes stay shared), so the journal survives reinstalling the mod. A `strategy/learned.md` there is preferred over the staged copy when the prompt is built, so lessons written without the repo reach the next run. The prompt's wiki index lists the pages there, the ones the notes tools can read, so without the repo the staged wiki isn't used and the brain starts its own.
- **The subscription brain's working folder**: the repo only, since Claude Code needs the real checkout for `/play` and its file tools. Without one it stops with a chat line asking for `repo` in config.json.

## Who plays

`BrainController` (Core) picks the brain and owns the commands. It is built at mod load and lives until the game quits.

- The API brain plays when there is a key, this month's spend is under `monthly_budget_usd` and the API hasn't failed during this run. It may start at the menu, since it idles there for free.
- Otherwise the subscription brain plays, and only while a run is in progress, so it never spends subscription turns at the menu. Once the poll loop has seen the run over (game over or the menu) for 2 minutes, which leaves time for the post-mortem, a subscription brain still playing is stopped without a chat line, and the next run starts it again. A run continued within those 2 minutes keeps it.
- When the API brain stops because the key is missing or rejected, the credit is gone, the Console spend limit or the monthly cap is reached, the chat says "Switching to your Claude subscription for the rest of this run." (or "for the next run", when it happened between runs) and the subscription brain takes over. Whether a run is in progress is what the API brain itself saw last, since a request that fails at once can come between a run start or a game over and the poll's next look. The next run tries the API again: "New run, so back on your API credit."
- It never switches for 429, 529, 5xx or network errors. The API brain retries those itself with a growing pause (30 s doubling up to 5 minutes) and says why once: "Can't reach Claude right now, retrying.", "Claude is overloaded right now, retrying. /model switches models." or, for a 429, "Claude is rate limiting your API key, retrying. /model switches models." with the API's own sentence. A `/model` or `/effort` typed meanwhile goes into the next try.
- A brain that stops with an error, or that something other than the controller stopped, isn't restarted until the next run starts (while autostart is on) or `/start`.
- A subscription brain that stepped back because the run ended (`RunEnded`) is started again once a run is in progress. If it keeps ending while the run goes on, it is restarted at most 3 times per run.
- One brain at a time: a new one starts only after the last one has stopped, or after 15 s, when the old one is aborted. Two brains would cancel each other's `wait_for_decision`.
- A poll loop reads the screen every 5 s from mod load until the game quits, so the controller knows whether a run is in progress even while autostart is off. It acts only while autostart is on. Each run start resets the run's spend and reads config.json and the key again, so a key added meanwhile is used from the next run (or at once, with `/start`).
- Brains announce their own stops in the chat, once. `UserStop` and `RunEnded` are never announced, and the controller only adds the lines about switching.

Command handlers run on Godot's main thread and never wait: brains start on their own tasks, and stopping one happens on a pool thread.

## Co-op

In a co-op run only bot copies play. Mark plays in his own copy, and each bot is a second copy of the game on the same Mac, started with `--sts2bot=1` and its own `SPIREMIND_PORT` (`docs/chat.md`, Co-op, has the relay and the launcher contract).

- **Role.** At mod load `InstanceRole.Detect` reads Godot's `OS.GetCmdlineArgs()`: `--sts2bot=1` (or `--sts2bot 1`, `true`) makes a bot copy; anything else, or no flag, is Mark's copy. The log line `role: ...` says which, with its folders.
- **A bot copy** starts its brain by itself (autostart is on for the session without being saved), so it plays as soon as a run is in progress with no `/start`. `/stop` still stops it until `/start` or the next launch.
- **Mark's copy** never plays while it finds a bot copy on localhost, or during a co-op run at all: `/start` answers why, autostart only logs it, and a brain that was playing is stopped with one line. While bots exist, `/start`, `/stop` and the other brain commands typed in Mark's window go to the bots instead (`docs/chat.md`). Solo play in Mark's copy is unchanged: no bots, no co-op run, nothing blocked.
- **Run journal.** A bot copy keeps its own journal, `runs/bot-<port>/current.md` under the notes root, and archives next to it (`runs/bot-17773/2026-10-10-silent.md`). The notes tools still call it `runs/current.md`, so the prompt and the model don't change; the subscription brain's prompt names the bot's journal. Mark's copy keeps `runs/current.md`.
- **Shared notes.** `strategy/learned.md` and the wiki stay shared by every copy. Every notes read, write, append and list takes a cross-process lock (`~/.config/spiremind/locks/notes.lock`); one that can't be had within 5 s gets "The notes are busy (another copy is writing them); try again in a moment." A `notes_write` to learned.md or a wiki page is refused when the file changed since this brain last saw it (in its prompt, or through `notes_read`): the error says another bot copy or a hand edit changed it, carries `changed_since_read: true`, and tells the model to read it again, merge and write, or to use `notes_append`, which never wipes anything. A file that doesn't exist yet is always writable. Solo use never meets it: nothing else writes, and each write updates what was seen.
- **Party chat.** Another bot's line reaches the brain as a chat message with `teammate: true`. It never wakes a waiting or idle brain, so it costs no extra request; it goes to the model with the next tool result.

## How the API brain works

```
ApiBrain (background task)                      BotTools (same instance the MCP endpoint uses)
  system = play prompt + strategy notes  ──────►  get_state, wait_for_decision, act, act_sequence,
  tools  = BotTools specs + notes tools           say, get_deck, get_persona
                                                  (+ notes_read, notes_write, notes_append,
                                                     notes_list: brain only)
  loop: POST /v1/messages ─► tool_use blocks ─► run each tool in-process ─► tool_result ─► repeat
```

- **ClaudeClient** (Core): `POST https://api.anthropic.com/v1/messages` with `x-api-key`, `anthropic-version: 2023-06-01`. No SDK, so the mod stays one DLL. Retries 429 (honouring `retry-after`), 529 and 5xx with exponential backoff and jitter, at most 5 tries. Replies aren't streamed, so each try waits 10 minutes, or longer when `max_tokens` could take longer to write (30 minutes at 64000, an hour at 128000), and its connections use TCP keep-alive so a router doesn't drop them while a long reply is written. Classifies errors so the brain can tell the human what happened: out of credit (402, or 400 whose message contains "credit balance is too low"), spend limit (400 "reached your specified API usage limits", or 429 with `enforced_spend_limit_reached`), bad key (401), everything else. The key is redacted from everything it logs or throws.
- **System prompt** (`PromptBuilder`): the play prompt (`.claude/commands/play.md`, with `$ARGUMENTS` read as "the whole run"), then the strategy notes it tells the model to read, included since the brain can't read them itself: `strategy/general.md`, the rules sheet `strategy/rules.md` (labelled as the verified rules for this game version), the boss and elite notes `strategy/bosses-and-elites.md` that general.md points to, the character's file in `strategy/characters/`, `strategy/learned.md`, and `strategy/coop.md` in co-op. Then a Wiki index section: one line per page under the notes root's `strategy/wiki/`, its path and summary, sorted, at most 200 with a last line saying how many more `notes_list` shows, or a line saying the wiki is empty. Page text isn't included; the brain reads a page with `notes_read` when it needs it. A short appendix about this harness comes last: the notes tools, the journal's header line in the exact form `runs/README.md` gives (which the brain can't read), and waiting. Missing files are skipped. It is rebuilt only when a new run starts or the character changes, so the cache holds, and the wiki index stays as it was when the prompt was built (`notes_list` shows pages added since).
- **Request**: model and `output_config.effort` from settings; thinking left to its adaptive default (Opus can't turn it off); no temperature. `system` is one text block with `cache_control: {type: ephemeral, ttl: "1h"}`, and the request sets top-level automatic caching so the growing conversation is cached too. Tools are sent with `strict: true` and `additionalProperties: false`; constraints strict mode doesn't take (minimum, maximum, maxItems and the like) move into the description and the tool still checks them. `tool_choice` stays auto (any/tool are refused by Opus and Sonnet 5.5). `max_tokens` is 16000 at low and medium effort, 32000 at high and 64000 at xhigh and max, since thinking counts toward it and runs longer at the top levels; `max_tokens` in config.json overrides it. It is only a ceiling, billed for what is used.
- **Conversation**: assistant content is kept exactly as received (thinking blocks with signatures included) and sent back unmodified. Tool results go back as one user message of `tool_result` blocks, JSON text, `is_error` from the tool.
- **No idle spending**: when the model calls `wait_for_decision`, the brain keeps waiting locally (repeated in-process waits, no API calls) until there is a decision, a chat message, or the brain is stopped. Menus, enemy turns, teammates and pauses cost nothing. A wait repeated on a decision the last wait already handed over (the human said "hold on" without `/pause`, a co-op ally to wait for) is held for up to 60 s, until the snapshot changes or a message arrives, so it costs a request a minute at most. Nothing is sent at all until a run is in progress or the human writes.
- **Chat**: messages from the human arrive in the tool result's `chat.messages` and again in a text block after the tool results, since Sonnet 5.5 and Haiku 5.5 can take user text inside a tool result for an injection attempt.
- **Refusals**: a reply with `stop_reason` "refusal" (a safety classifier declined) starts one fresh conversation; a second one in a row stops the brain. The category goes to the log and the transcript.
- **Fresh context instead of endless history**: once the conversation passes 60k prompt tokens and the bot reaches the map, or passes 150k anywhere (on Haiku, 20k below its 100k long-prompt tier), the brain starts a new conversation. So does a new run, a changed character, a changed model or effort, and a prompt the API calls too long. Its first user message holds today's date (local, for the journal's header line), the run journal (`runs/current.md`: Build plan, Orders, floor log), the last 10 chat lines, any chat messages the model hasn't seen and the current state, and asks it to carry on. The system prompt and tools are unchanged, so their cache survives.
- **end_turn without a tool call**: the model thinks it is done. If a run is in progress, the brain answers "Keep playing: call wait_for_decision." More than 5 in a row without playing stop the brain, as do more than 2 replies in a row cut off at max_tokens, so a model that won't play can't burn money. Outside a run it idles locally until a run starts or the human writes.
- **Notes tools** (brain only; Claude Code has its own file tools): `notes_read {file}`, `notes_write {file, content}` (the whole file) and `notes_append {file, content}` (a line at the end, for the floor log or a wiki lesson) for `runs/current.md`, `strategy/learned.md` and wiki pages under the notes root. A wiki page is `strategy/wiki/<slug>.md` or `strategy/wiki/<folder>/<slug>.md`, folder and slug lowercase letters and digits with single hyphens between words: no deeper folders, no dots, backslashes or capitals (so never `README.md`; the name `readme` is refused too, since a Mac's disk usually ignores case, and so are the Windows device names such as `con`, `nul` and `com1`). The `file` property is a plain string, since strict tool schemas take neither an enum that could hold pages nor a pattern; the tools check it and list the allowed forms in their error. A write creates the folders it needs. Nothing else is readable or writable. `notes_list {}` returns every page (path, bytes, the title from its first line's heading, the summary from the next non-empty line unless that is a heading) sorted by path, and whether the two fixed files exist. A file may be at most 32 KB, small enough that it always fits in one reply; a write or append past that is refused with a hint to prune old log lines or lessons. Writing `runs/current.md` with a new header line, or without the post-mortem the old journal has, archives the old one as `runs/<date>-<character>.md` first; nothing else is archived. A write that replaces a wiki page with a `## Facts` section is refused unless everything above its `## Lessons` heading is unchanged (line endings, trailing spaces and trailing blank lines aside), so a rewrite to fix a lesson can't alter the verified facts.
- **Another client**: if Claude Code's `/play` (or a leftover subscription brain) starts its own `wait_for_decision`, the brain's wait is cancelled and the API brain stops with a chat line instead of fighting over the game.
- **Transcripts**: each conversation is appended to `~/.config/spiremind/transcripts/<date>.jsonl` (requests without the system prompt, responses with usage and cost) for debugging and tuning.

## Subscription fallback

`SubscriptionBrain` lives in Core with everything else: it starts processes through `IProcessLauncher`, which is `ProcessLauncher` (around `System.Diagnostics.Process`) in the mod and a fake in the tests. It runs Claude Code in the repo folder. Mark's Mac has the CLI from Homebrew (v2.1.287 on 2026-10-09). Haiku 5.5 needs Claude Code 2.1.293 or newer: older versions take its full id but report a 200K context and a guessed cost, and read the short name `haiku` as Haiku 4.5 (the brain always passes the full id). Homebrew's `claude-code` cask follows the stable channel and can lag behind; `claude-code@latest` follows the newest release. The arguments, as built:

```
claude -p "/play the whole run" --session-id <uuid> --strict-mcp-config
  --mcp-config '{"mcpServers":{"spiremind":{"type":"http","url":"http://127.0.0.1:<port>/mcp/own"}}}'
  --tools Read,Glob,Edit,Write,ToolSearch --permission-mode dontAsk
  --allowedTools mcp__spiremind "mcp__spiremind__*" "Edit(//<repo>/runs/**)" "Edit(//<repo>/strategy/**)"
  --output-format stream-json --verbose --model <model>
```

A resumed session gets `-p "Keep playing: the run isn't over. Call wait_for_decision."` and `--resume <uuid>` in place of `--session-id`, with every other flag repeated. `--model` is the model from config.json, the same one the API brain uses, read as each session starts, so a `/model` change reaches Claude Code at its next session or resume.

- The binary is found by absolute path, since a game started from Steam has no shell PATH: `claude_path` in config.json, else `/opt/homebrew/bin/claude`, `~/.local/bin/claude`, `/usr/local/bin/claude`, `~/.claude/local/claude`. None found stops it with a chat line saying how to install it.
- Before anything else, `claude auth status` must exit 0 within 15 s and print JSON with `loggedIn` true; otherwise the chat says Claude Code isn't signed in. Its output can hold the account's email, so only `loggedIn`, `authMethod` and `subscriptionType` are logged.
- It waits for a run to be in progress before the first session.
- The child's environment loses `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_BASE_URL`, `CLAUDECODE` (the marker Claude Code leaves for its own children) and every `CLAUDE_CODE_USE_*`, so it bills the subscription login, never the API credit. Its PATH starts with the binary's folder, `/opt/homebrew/bin`, `/usr/local/bin`, `/usr/bin` and `/bin`, for node and helpers. Its standard input is closed at once.
- Its stream-json output is read line by line and logged: `system/init` with the MCP server status, assistant text (cut short), the tools it calls, the result with the names of any tools `dontAsk` refused (`claude: permission denied for ...`; their inputs aren't logged), and stderr.
- A session ends when the process exits. Then, in order: a usage limit message stops the brain with "Your Claude subscription hit its usage limit, so the bot stopped." plus Claude Code's own sentence when it says when the limit resets. A finished run stops it with `RunEnded`, which isn't announced; the controller starts it again with the next run. Three sessions in a row that end within 2 minutes stop it with "Claude Code keeps exiting early". A session Claude Code can't find any more starts afresh with a new id. Anything else is resumed.
- `/stop` asks Claude Code to quit (SIGINT) and kills it after 5 s if it hasn't. The log says how that went: "exited with code N after the stop", plus a line first when SIGINT couldn't be sent and it was killed at once, or "killing Claude Code" when it didn't quit within the 5 s. Quitting the game kills it at once, with every process it started.
- The play prompt is the same `.claude/commands/play.md`, so both brains behave alike. Chat reaches it through the MCP tool results as usual.
- It reads strategy notes and wiki pages with Read and lists the pages with Glob (`strategy/wiki/**/*.md`); both are read-only, so like Read before it, Glob needs no allow rule inside the repo. It writes the journal, `strategy/learned.md` and wiki pages with Edit and Write, which the `strategy/**` rule covers at any depth, new folders included.

## Rules sheet and wiki

`strategy/rules.md` holds the verified combat rules for this game version. Both brains read it with the other strategy notes: Claude Code because `play.md` says to, the API brain in its system prompt right after `strategy/general.md`.

`strategy/wiki/` holds a page per enemy, encounter, card group or other topic, such as `enemies/<slug>.md`, `encounters/act1-overgrowth.md`, `cards/ironclad.md` or `potions.md`; `README.md` there has the conventions for humans. Every page starts with `# <Name>` and a one-sentence summary on line 2, which the index and `notes_list` show, then `## Facts (game files, v0.107.1)`, which the bot never edits, and `## Lessons`, always the last section, so a line appended to a page lands there. Pages the bot makes for topics without one go in `strategy/wiki/lessons/`. `strategy/learned.md` stays the short list of general rules that is always loaded.

`play.md` says when to use it: read a page before the decision it covers (an elite, a boss, an unfamiliar enemy, an event, a card, relic or potion pick), not every turn; add one dated line, tagged with the character, after a fight or event that taught something concrete; fix a wrong lesson instead of contradicting it; and at the end of a run add any lesson the run taught that isn't on its page yet and promote short general rules to `strategy/learned.md`. The API brain does this with the notes tools and finds pages in its prompt's index; Claude Code does it with Read, Glob, Edit and Write.

## Wiring and game exit

`BrainWiring` (game side) builds it all once the bridge has started: the settings, the spend tracker and transcript, and the controller with a factory per brain. The API brain gets the same `BotTools` instance the MCP endpoint uses, and learns whether the run is co-op from the last snapshot the bridge took; the subscription brain gets the bridge's port. Both read the screen through `DecisionWaiter.PeekAsync`, which waits for the main thread and never blocks it, and leaves the snapshot version alone: a deck view opened and closed while the model plans would otherwise count as two changes and get its next `act` refused as stale. It uses only Core, System and Godot types, and a failure anywhere only loses the brain, never the bridge or the chat. The mod's folder comes from the DLL's location.

So that a Claude Code child never outlives the game, the scene tree root's `TreeExiting` signal and `AppDomain.ProcessExit` both call `BrainController.Shutdown()`, which aborts every brain: the API request in flight is cancelled and Claude Code is killed with its whole process tree. Only the first of the two does anything. A game that crashes or is force-quit gets no such chance; Claude Code then loses its MCP server and may linger until it notices.

## act_sequence

Both brains get `act_sequence {version, actions: [id, ...]}` to play several actions from one snapshot, typically a whole turn. Ids are from the snapshot named by `version`. The first runs as `act` does. Before each later step, the tool finds the action again in the current snapshot:

- An id with no index or target (`confirm`, `end_turn`, `proceed`) is taken as it is while it is still offered, since a label can carry a count that moves on ("Confirm selection (1 chosen)").
- Any other id is found by its label in the original snapshot, so shifting hand indices don't matter. A play label names only the card, so a cheaper, enchanted or otherwise changed copy looks like the plain one. When several actions share the label, the tool prefers the card whose hand entry (name, cost, enchantment, text) equals the original's, then one that equals it without the text (a Strength gain rewrites every attack's text), then the same id, then the first.

It stops early, returning what it did and the current snapshot, when:

- nothing changed after the last step, or the game refused an action,
- the screen is no longer the one the sequence started on (a hand selection opened, the fight ended),
- the game doesn't settle into a decision,
- the turn ended (`state.turn` or `state.round` changed), say after a card like Void Form. The settle wait runs through the enemy turn, so without this check the rest of the plan would run on the next hand,
- the bot is paused or a chat message arrives,
- the next label is no longer offered.

A target's label has " (e1)" after the enemy's name only while another living enemy shares it, so when a twin dies the tool also accepts the action at the same target whose label matches once that tag is dropped. Before anything runs, every id must be legal in the snapshot, `end_turn` may only be the last id, the version must match and the game must be waiting on the bot; otherwise it is refused like `act`, and nothing runs. It takes 1 to 12 ids. The result lists what ran, then why it stopped and which ids didn't run, then the snapshot: `{"done": [{"id", "label"}, ...], "stopped", "not_run", "warning", "next"}`, with `stopped` and `not_run` only when it stopped early. It is an error only when nothing ran.

`play.md` tells the model to end a sequence after any card or potion that draws, discovers, creates or transforms cards, has a random result it needs to see, opens a choice or ends its turn, so it sees what it got before planning further, and to leave out `end_turn` after a card that ends the turn.

## Smoke test

`scripts/dev-host.sh --brain` proves the real API calls work before the first in-game `/start`, with no game running. The real `ApiBrain` plays the dev host's fake fight (one Jaw Worm, the same `FakeGame` the tests use) through the real `BotTools` and chat, with the key from `~/.config/spiremind/api_key`, the effort from config.json and the real spend tracker, so what it costs counts in `/budget`. Three things differ from the game:

- The model is `--model <name>` for this run only (default haiku, to keep it cheap; config.json isn't changed). `scripts/dev-host.sh --brain --model sonnet` tries another.
- The notes and the transcript go to a fresh temp folder, never the repo's `runs/` or `strategy/`, so the live run journal isn't touched. The system prompt is still built from the repo's play prompt and strategy notes, but its wiki index lists the temp folder's pages, so it says the wiki is empty.
- At most `--max-requests` requests are sent (default 6, at most 50). The next one is refused, which stops the brain.

It prints each request's model, stop reason, tool calls, tokens (fresh input, cache reads, cache writes with their 1 hour part, output) and cost, then the totals and this month's spend. It ends with PASSED (exit code 0) when the brain stopped at the cap or because it won the fight, and FAILED (exit code 1) when it stopped for any other reason, a request failed, nothing came back, or Ctrl+C or a 10 minute limit cut it short.

What to expect on haiku: a few requests (usually the persona and notes first, then a turn or two of the fight), a cache write of the whole prompt (the system text and tools, over 10k tokens) on the first, cache reads of about that much on every one after, and about a cent in all. No 400s. If the second request reads 0 cached tokens, the start of the prompt is changing between requests, which would cost real money in the game; the script prints a warning for it. A 400 prints the API's own message and means the request (tools, caching, effort) needs fixing before `/start`. The transcript in the temp folder has every exchange.

## Cost

Per decision on Opus 5.5 (input $4, cache read $0.20, output $20 per million tokens): about 30k cached tokens plus 2k fresh plus 500 to 1500 output, roughly $0.02 to $0.04. With `act_sequence` a fight turn is usually one decision, so a full run should be on the order of 200 decisions.
