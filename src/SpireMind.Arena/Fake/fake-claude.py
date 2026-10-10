#!/usr/bin/env python3
"""A stand-in for the Claude Code binary, to test the arena's runner without spending anything.

    scripts/arena.sh run --claude src/SpireMind.Arena/Fake/fake-claude.py --models fake-play,fake-seq ...

It takes the arguments the arena passes to claude, prints stream-json lines shaped like Claude
Code's, and plays the fight over the bridge's MCP endpoint with a rule a child could follow. The
model name picks what it does:

  playing
    fake-play      plays to the end with act: the first card it may play, else end_turn; a card
                   choice is confirmed when it can be, else its first card is taken
    fake-seq       the same with act_sequence: one card and end_turn per call
    fake-block     plays to the end, cards without a target first (so it mostly blocks)
    fake-clumsy    like fake-play, but starts every turn with an action id that doesn't exist
    fake-done      plays to the end, then runs the end screen's "done" action once before stopping
    fake-prose     plays to the end while talking about limits ("hand limit reached"), which is
                   the model's own prose and must never be taken for the usage limit

  reading the wiki (Read is the one built-in tool the arena passes; see "Reading" below)
    fake-reader    before its first move reads the act's encounter page and the page of every
                   enemy in the fight that the wiki has, then plays to the end
    fake-snoop     reads one page, then asks for a page that isn't there, for a file outside the
                   strategy folder (refused) and for the wiki's README (readable, not a page), and
                   calls a Write tool it was never given (there is none, and nothing is written);
                   then plays to the end

  ending early (Claude Code ends a headless session when the model answers without a tool call)
    fake-pause     ends its session normally after 3 actions, once; when resumed it plays to the end
    fake-pauseN    the same N times (fake-pause3: three sessions end early, the fourth plays on)
    fake-abandon   ends every session normally after 2 actions, however often it is resumed
    fake-forget    like fake-pause, but loses its session, so the resume finds no conversation
    fake-softlimit says "usage limit reached" in an ordinary message and exits normally, every
                   time: prose again, so it is resumed and in the end an error for never playing

  not ending
    fake-hang      plays one action, then does nothing (for the time limit)
    fake-linger    plays to the end, then never exits and prints no result line (for the grace)
    fake-stubborn  like fake-linger, and ignores Ctrl+C (so it has to be killed)

  failing
    fake-limit     fails with Claude Code's sentence for a usage limit in an error result
    fake-rejected  reports the 5 hour window as rejected in a rate_limit_event, then fails
    fake-warning   reports status allowed_warning (ahead of the window's pace) and plays to the end
    fake-overage   reports isUsingOverage true after two actions and keeps playing (must be stopped)
    fake-nomcp     reports the MCP server as failed
    fake-notools   reports the MCP server as connected but lists none of its tools
    fake-pending   reports the MCP server as pending with no tools yet, then plays to the end
    fake-writer    lists Edit and Write among its tools, as a Claude Code would that ignored
                   --tools: the arena must stop it before it does anything
    fake-blind     lists no Read tool, so the wiki the prompt names couldn't be opened: stopped too
    fake-crash     writes to stderr and exits with code 3
    fake-replay    prints the file FAKE_CLAUDE_REPLAY names line by line (the stream-json output of
                   a real session, say) and exits normally, to see what the runner reads from it
    anything else  like fake-play

Sessions are kept the way Claude Code keeps them, so a resume can be checked: --session-id ID
writes <config>/projects/<working folder, every non-alphanumeric character as "-">/ID.jsonl, and
--resume ID fails with "No conversation found" unless that file is there. <config> is
CLAUDE_CONFIG_DIR or ~/.claude. --no-session-persistence writes nothing, as in Claude Code.

Every session prints one rate_limit_event after its first message, shaped like Claude Code
2.1.296's. With FAKE_CLAUDE_USAGE set to a file holding two numbers ("0.02 0.42": the 5 hour and
the 7 day window), the numbers come from there and each session adds 0.01 to the first and
FAKE_CLAUDE_WEEKLY_STEP (default 0.01) to the second, so a batch can be seen filling the windows.
FAKE_CLAUDE_RESET (Unix seconds) changes when the 5 hour window says it resets.

The result line's total_cost_usd and modelUsage are running totals, as Claude Code 2.1.296
documents them: a resumed session goes on from the totals its session kept (a .totals.json beside
the session file), so its result line already holds the sessions before it. num_turns, duration_ms
and usage are the process's own. FAKE_CLAUDE_CARRY=0 turns the carrying off, so every result line
holds only its own process's numbers.

Reading. The init line lists the built-in tools --tools names (Read, from the arena) before the
fight's own. A Read is answered the way the arena's flags make Claude Code answer it: the file's
text when it lies under the folder the --allowedTools rule Read(//<folder>/**) names, Claude
Code's refusal in dontAsk mode when it lies anywhere else, and an error when it isn't there. This
is a stand-in for the permission check, good for testing what the runner counts; it proves nothing
about Claude Code's own. The fake never writes anywhere but its session files.

`auth status` answers as a signed-in subscription. With FAKE_CLAUDE_LOG set to a file, each start
appends its arguments, working folder and the names (never the values) of the CLAUDE*/ANTHROPIC*
variables it was given, so a test can check what the arena passes on. With FAKE_CLAUDE_PROMPT set
to a file, each start writes there what a model would be told: the system prompt the arena appends
and, under a line of dashes, the user prompt.
"""
import json
import os
import re
import signal
import sys
import time
import urllib.request

PREFIX = "mcp__spiremind__"
OVER = "arena_over"


def emit(obj):
    sys.stdout.write(json.dumps(obj) + "\n")
    sys.stdout.flush()


def option(args, name, default=None):
    return args[args.index(name) + 1] if name in args and args.index(name) + 1 < len(args) else default


def values(args, name):
    """Every value after a flag that takes several (--tools Read, --allowedTools a b c), up to the next flag."""
    if name not in args:
        return []
    found = []
    for arg in args[args.index(name) + 1:]:
        if arg.startswith("--"):
            break
        found.append(arg)
    return found


def read_root(args):
    """The folder the arena's Read rule opens: Read(//Users/x/repo/strategy/**) is /Users/x/repo/strategy."""
    for rule in values(args, "--allowedTools"):
        match = re.fullmatch(r"Read\(//(.+)/\*\*\)", rule)
        if match:
            return "/" + match.group(1)
    return None


def log_start(args):
    told = os.environ.get("FAKE_CLAUDE_PROMPT")
    if told and "--append-system-prompt" in args:
        with open(told, "w", encoding="utf-8") as f:
            f.write(option(args, "--append-system-prompt", "") + "\n" + "-" * 60 + "\n" + option(args, "-p", "") + "\n")
    path = os.environ.get("FAKE_CLAUDE_LOG")
    if not path:
        return
    shown = [a if len(a) <= 200 else a[:60] + f"... ({len(a)} characters)" for a in args]
    names = sorted(n for n in os.environ if n.startswith(("CLAUDE", "ANTHROPIC")))
    with open(path, "a", encoding="utf-8") as f:
        f.write(json.dumps({"args": shown, "cwd": os.getcwd(), "cwd_entries": os.listdir("."), "env_names": names}) + "\n")


def session_file(session_id):
    """Where Claude Code keeps a session: by the working folder as the process sees it."""
    config = os.environ.get("CLAUDE_CONFIG_DIR") or os.path.join(os.path.expanduser("~"), ".claude")
    return os.path.join(config, "projects", re.sub(r"[^a-zA-Z0-9]", "-", os.getcwd()), session_id + ".jsonl")


def totals_file(session_id):
    return session_file(session_id)[:-len(".jsonl")] + ".totals.json"


def carried_totals(session_id):
    """The cost and output tokens a resumed session goes on from, when its session kept them."""
    if os.environ.get("FAKE_CLAUDE_CARRY", "1") == "0":
        return None
    try:
        with open(totals_file(session_id), encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def usage_windows():
    """The 5 hour and 7 day utilization this session reports, moving on the shared file if there is one."""
    path = os.environ.get("FAKE_CLAUDE_USAGE")
    if not path:
        return 0.02, 0.42
    try:
        with open(path, encoding="utf-8") as f:
            five, seven = (float(x) for x in f.read().split()[:2])
    except (OSError, ValueError):
        five, seven = 0.02, 0.42
    step = float(os.environ.get("FAKE_CLAUDE_WEEKLY_STEP", "0.01"))
    with open(path, "w", encoding="utf-8") as f:
        f.write(f"{five + 0.01:.2f} {seven + step:.2f}\n")
    return five, seven


class Bridge:
    """The fight's MCP endpoint, spoken to with plain JSON-RPC over HTTP."""

    def __init__(self, url):
        self.url = url
        self.next_id = 0

    def rpc(self, method, params):
        self.next_id += 1
        body = json.dumps({"jsonrpc": "2.0", "id": self.next_id, "method": method, "params": params}).encode()
        request = urllib.request.Request(self.url, data=body, headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.loads(response.read())["result"]

    def call(self, tool, arguments):
        result = self.rpc("tools/call", {"name": tool, "arguments": arguments})
        return json.loads(result["content"][0]["text"]), result.get("isError", False)


class Session:
    def __init__(self, model, bridge, session_id, number):
        self.model = model
        self.bridge = bridge
        self.session_id = session_id
        self.number = number  # 1 for a new session, 2 for its first resume, and so on
        self.turns = 0
        self.output_tokens = 0
        self.started = time.time()
        self.usage_sent = False
        self.kept = True  # whether the session is kept on disk (not under --no-session-persistence)
        self.builtins = []  # the built-in tools --tools named
        self.read_root = None  # the folder the Read rule opens

    def rate_limit(self, status="allowed", overage=False, limit_type="five_hour"):
        """One rate_limit_event, as Claude Code 2.1.296 prints it."""
        five, seven = usage_windows()
        # A window resets at a fixed time, the same in every event until it has.
        reset = int(os.environ.get("FAKE_CLAUDE_RESET", "1791651600"))
        emit({"type": "rate_limit_event", "rate_limit_info": {
            "status": status, "resetsAt": reset, "rateLimitType": limit_type,
            "overageStatus": "allowed" if overage else "rejected", "isUsingOverage": overage,
            "unifiedWindows": {"five_hour": {"utilization": five, "resetsAt": reset},
                               "seven_day": {"utilization": seven, "resetsAt": 1791748800}}},
              "session_id": self.session_id})

    def after_message(self):
        if self.usage_sent:
            return
        self.usage_sent = True
        if self.model == "fake-warning":
            self.rate_limit(status="allowed_warning", limit_type="seven_day")
        elif self.model == "fake-rejected":
            self.rate_limit(status="rejected")
        else:
            self.rate_limit()

    def tool(self, name, arguments):
        """One assistant turn that calls a tool, as Claude Code prints it, and the tool's answer."""
        self.turns += 1
        self.output_tokens += 40
        emit({"type": "assistant", "session_id": self.session_id, "message": {
            "id": f"msg_{self.number}_{self.turns}", "model": self.model, "role": "assistant",
            "content": [{"type": "tool_use", "id": f"toolu_{self.number}_{self.turns}", "name": PREFIX + name, "input": arguments}],
            "usage": {"input_tokens": 10, "cache_creation_input_tokens": 0, "cache_read_input_tokens": 9000, "output_tokens": 40}}})
        self.after_message()
        payload, is_error = self.bridge.call(name, arguments)
        emit({"type": "user", "session_id": self.session_id, "message": {"role": "user", "content": [
            {"type": "tool_result", "tool_use_id": f"toolu_{self.number}_{self.turns}", "is_error": is_error,
             "content": [{"type": "text", "text": json.dumps(payload)}]}]}})
        return payload

    def builtin(self, name, arguments, content, is_error):
        """One assistant turn that calls a built-in tool, and what Claude Code answers."""
        self.turns += 1
        self.output_tokens += 30
        emit({"type": "assistant", "session_id": self.session_id, "message": {
            "id": f"msg_{self.number}_{self.turns}", "model": self.model, "role": "assistant",
            "content": [{"type": "tool_use", "id": f"toolu_{self.number}_{self.turns}", "name": name, "input": arguments}],
            "usage": {"input_tokens": 10, "cache_creation_input_tokens": 0, "cache_read_input_tokens": 9000, "output_tokens": 30}}})
        self.after_message()
        result = {"type": "tool_result", "tool_use_id": f"toolu_{self.number}_{self.turns}", "content": content}
        if is_error:
            result["is_error"] = True
        emit({"type": "user", "session_id": self.session_id, "message": {"role": "user", "content": [result]}})

    def read(self, path):
        """A Read call, answered as the arena's flags make Claude Code answer it. True when the file was opened."""
        inside = self.read_root is not None and os.path.realpath(path).startswith(os.path.realpath(self.read_root) + os.sep)
        if "Read" not in self.builtins:
            content, failed = "Error: No such tool available: Read", True
        elif not inside:
            content, failed = "Permission to use Read has been denied because Claude Code is running in don't ask mode.", True
        else:
            try:
                with open(path, encoding="utf-8") as f:
                    content, failed = "     1\u2192" + f.read(300), False
            except OSError:
                content, failed = "File does not exist.", True
        self.builtin("Read", {"file_path": path}, content, failed)
        return not failed

    def not_a_tool(self, name, arguments):
        """A call to a tool the session was never given. Nothing happens but the error."""
        self.builtin(name, arguments, f"Error: No such tool available: {name}", True)

    def say(self, text, synthetic=False):
        self.turns += 1
        self.output_tokens += 12
        emit({"type": "assistant", "session_id": self.session_id, "message": {
            "id": f"msg_{self.number}_{self.turns}", "model": "<synthetic>" if synthetic else self.model, "role": "assistant",
            "content": [{"type": "text", "text": text}],
            "usage": {"input_tokens": 10, "cache_creation_input_tokens": 0, "cache_read_input_tokens": 9000, "output_tokens": 12}}})
        self.after_message()

    def result(self, text, is_error=False):
        """The result line. As in Claude Code, num_turns, duration_ms and usage are this process's
        own, while total_cost_usd and modelUsage go on from what the session kept when it is resumed."""
        own = {"inputTokens": 10 * self.turns, "outputTokens": self.output_tokens,
               "cacheReadInputTokens": 9000 * max(0, self.turns - 1), "cacheCreationInputTokens": 9000,
               "costUSD": round(0.01 + 0.002 * self.turns, 4)}
        before = (carried_totals(self.session_id) if self.number > 1 else None) or {}
        total = {key: round(value + before.get(key, 0), 4) if key == "costUSD" else value + before.get(key, 0)
                 for key, value in own.items()}
        if self.kept:
            try:
                with open(totals_file(self.session_id), "w", encoding="utf-8") as f:
                    json.dump(total, f)
            except OSError:
                pass
        emit({"type": "result", "subtype": "error_during_execution" if is_error else "success", "is_error": is_error,
              "duration_ms": int((time.time() - self.started) * 1000), "num_turns": self.turns, "result": text,
              "session_id": self.session_id, "total_cost_usd": total["costUSD"],
              "usage": {"input_tokens": own["inputTokens"], "cache_creation_input_tokens": own["cacheCreationInputTokens"],
                        "cache_read_input_tokens": own["cacheReadInputTokens"], "output_tokens": own["outputTokens"]},
              "modelUsage": {self.model + "-20260101": {**total, "contextWindow": 200000, "maxOutputTokens": 128000}}})


def snapshot_of(reply):
    """The snapshot in a tool result: its own, or the next or current one of act and act_sequence."""
    if "screen" in reply:
        return reply
    return reply.get("next") or reply.get("current") or {}


def over(snapshot):
    return snapshot.get("screen") == OVER


def must_wait(reply, snapshot):
    """Whether a result tells the model to wait: no decision is ready, or the tools say the game is busy."""
    return not snapshot.get("awaiting_decision") or "still busy" in json.dumps(reply)


def pick(snapshot, block_first):
    actions = [a["id"] for a in snapshot.get("actions", [])]
    if snapshot.get("screen") != "combat":
        # A card choice (hand_select, card_select) has no end_turn: confirm as soon as that is
        # offered, else take the first card, else skip. Every step brings the choice nearer its end.
        if "confirm" in actions:
            return "confirm"
        picks = [a for a in actions if a.startswith(("select:", "card:"))]
        if picks:
            return picks[0]
        return "skip" if "skip" in actions else (actions[0] if actions else "end_turn")
    plays = [a for a in actions if a.startswith("play:")]
    if block_first:
        plays.sort(key=lambda action: ">" in action)
    return plays[0] if plays else "end_turn"


def enemy_pages(session, snapshot):
    """The wiki pages of the fight a model that follows the prompt opens first: the act's, then
    each enemy's, as far as the wiki under the Read rule's folder has them."""
    if session.read_root is None:
        return []
    pages = ["wiki/encounters/act1-overgrowth.md"]
    for enemy in snapshot.get("state", {}).get("enemies", []):
        slug = re.sub(r"[^a-z0-9]+", "-", str(enemy.get("name", "")).lower()).strip("-")
        if slug and f"wiki/enemies/{slug}.md" not in pages:
            pages.append(f"wiki/enemies/{slug}.md")
    return [os.path.join(session.read_root, page) for page in pages if os.path.isfile(os.path.join(session.read_root, page))]


def read_first(session, behaviour, snapshot):
    """What the reading behaviours do before their first move."""
    pages = enemy_pages(session, snapshot)
    if behaviour == "fake-reader":
        for page in pages:
            session.read(page)
    elif behaviour == "fake-snoop" and session.read_root is not None:
        for page in pages[:1]:
            session.read(page)
        session.read(os.path.join(session.read_root, "wiki", "enemies", "no-such-enemy.md"))
        session.read(os.path.join(os.path.dirname(session.read_root), "CLAUDE.md"))
        session.read(os.path.join(session.read_root, "wiki", "README.md"))
        session.not_a_tool("Write", {"file_path": os.path.join(session.read_root, "wiki", "lessons", "arena-fake.md"),
                                     "content": "# Never written\n"})


def pauses(behaviour):
    """How many sessions of this behaviour end early before one plays on."""
    if behaviour == "fake-abandon":
        return 1_000_000
    if behaviour == "fake-forget":
        return 1
    match = re.fullmatch(r"fake-pause(\d*)", behaviour)
    return int(match.group(1) or 1) if match else 0


def play(session, behaviour):
    reply = session.tool("wait_for_decision", {})
    snapshot = reply
    if session.number == 1:
        read_first(session, behaviour, snapshot)
    actions = 0
    clumsy_turn = None
    waits_after_the_end = 0
    stop_after = 2 if behaviour == "fake-abandon" else 3
    while True:
        # A model that does as the tools say waits whenever a result tells it to, also on a
        # finished fight. The end snapshot must therefore never say so.
        if must_wait(reply, snapshot):
            if over(snapshot):
                waits_after_the_end += 1
                if waits_after_the_end > 3:
                    sys.stderr.write("fake-claude: the finished fight keeps telling me to wait\n")
                    session.result("The finished fight keeps telling me to wait.", is_error=True)
                    return 4
            reply = session.tool("wait_for_decision", {"timeout_seconds": 5})
            snapshot = reply
            continue
        if over(snapshot):
            break
        if behaviour == "fake-clumsy" and snapshot.get("state", {}).get("turn") != clumsy_turn:
            clumsy_turn = snapshot.get("state", {}).get("turn")
            session.tool("act", {"action_id": "play:99", "version": snapshot["version"]})
        if behaviour == "fake-prose" and actions % 4 == 1:
            session.say("Hand limit reached, so no more draws this turn. I have hit my limit of three energy; usage limit of block is next.")
        choice = pick(snapshot, behaviour == "fake-block")
        if behaviour == "fake-seq" and choice.startswith("play:"):
            reply = session.tool("act_sequence", {"version": snapshot["version"], "actions": [choice, "end_turn"]})
        else:
            reply = session.tool("act", {"action_id": choice, "version": snapshot["version"]})
        snapshot = snapshot_of(reply)
        actions += 1
        if over(snapshot):
            continue
        if session.number <= pauses(behaviour) and actions >= stop_after:
            if behaviour == "fake-forget":
                try:
                    os.remove(session_file(session.session_id))
                except OSError:
                    pass
            session.say("That is enough for me.")
            session.result("That is enough for me.")
            return 0
        if behaviour == "fake-overage" and actions == 2:
            session.rate_limit(status="rejected", overage=True)
        if behaviour == "fake-hang":
            time.sleep(3600)

    if behaviour == "fake-done":
        session.tool("act", {"action_id": "done", "version": snapshot["version"]})
    if behaviour in ("fake-linger", "fake-stubborn"):
        time.sleep(3600)
    won = snapshot.get("state", {}).get("result") == "won"
    text = "Won." if won else "Lost."
    if behaviour == "fake-prose":
        text += " The enemy hit its limit; rate limit of my patience reached."
    session.say(text)
    session.result(text)
    return 0


def main():
    args = sys.argv[1:]
    log_start(args)
    if args[:2] == ["auth", "status"]:
        print(json.dumps({"loggedIn": True, "authMethod": "claude.ai", "apiProvider": "firstParty",
                          "email": "nobody@example.invalid", "subscriptionType": "fake"}))
        return 0

    model = option(args, "--model", "fake-play")
    if model == "fake-stubborn":
        signal.signal(signal.SIGINT, signal.SIG_IGN)
    else:
        signal.signal(signal.SIGINT, lambda *_: os._exit(130))

    # The session: a new one under --session-id, or an earlier one under --resume, which must be on disk.
    keep = "--no-session-persistence" not in args
    resumed = option(args, "--resume")
    session_id = resumed or option(args, "--session-id") or "fake"
    number = 1
    if resumed:
        try:
            with open(session_file(resumed), encoding="utf-8") as f:
                number = len(f.read().splitlines()) + 1
        except OSError:
            sys.stderr.write(f"No conversation found with session ID: {resumed}\n")
            return 1
    if keep:
        os.makedirs(os.path.dirname(session_file(session_id)), exist_ok=True)
        with open(session_file(session_id), "a", encoding="utf-8") as f:
            f.write(json.dumps({"session": number, "prompt": option(args, "-p", "")[:80]}) + "\n")

    if model == "fake-replay":
        with open(os.environ["FAKE_CLAUDE_REPLAY"], encoding="utf-8") as f:
            for line in f:
                sys.stdout.write(line if line.endswith("\n") else line + "\n")
        sys.stdout.flush()
        return 0

    config = json.loads(option(args, "--mcp-config", "{}"))
    url = config.get("mcpServers", {}).get("spiremind", {}).get("url")
    bridge = Bridge(url)
    session = Session(model, bridge, session_id, number)
    session.kept = keep
    session.builtins = [tool for value in values(args, "--tools") for tool in re.split(r"[ ,]+", value) if tool]
    session.read_root = read_root(args)

    print(f"[claude-code:unrecognized_model] {model} is not a model this fake knows, running it anyway")
    sys.stdout.flush()

    status = "connected"
    tools = []
    if model == "fake-nomcp":
        status = "failed"
    else:
        try:
            bridge.rpc("initialize", {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "fake-claude", "version": "0"}})
            tools = [PREFIX + t["name"] for t in bridge.rpc("tools/list", {})["tools"]]
        except Exception as ex:  # the bridge isn't there
            status = "failed"
            sys.stderr.write(f"fake-claude: {type(ex).__name__}: {ex}\n")
    # Claude Code lists its built-in tools first, then the MCP servers' as they connect.
    builtins = list(session.builtins)
    if model == "fake-writer":
        builtins += ["Edit", "Write"]
    if model == "fake-blind":
        builtins = [tool for tool in builtins if tool != "Read"]
    tools = builtins + tools
    if model == "fake-notools":
        tools = ["ToolSearch"]
    if model == "fake-pending":
        status, tools = "pending", builtins
    emit({"type": "system", "subtype": "init", "cwd": os.getcwd(), "session_id": session_id, "model": model,
          "claude_code_version": "0.0.0-fake", "apiKeySource": "none", "permissionMode": option(args, "--permission-mode"),
          "tools": tools if status != "failed" else [],
          "mcp_servers": [{"name": "spiremind", "status": status}]})
    if status == "failed" or model in ("fake-notools", "fake-writer", "fake-blind"):
        time.sleep(3600)

    if model == "fake-limit":
        session.say("You've hit your session limit · resets 3pm (Europe/Berlin)", synthetic=True)
        session.result("You've hit your session limit · resets 3pm (Europe/Berlin)", is_error=True)
        return 1
    if model == "fake-rejected":
        session.say("API Error: the request was refused.", synthetic=True)
        session.result("API Error: the request was refused.", is_error=True)
        return 1
    if model == "fake-softlimit":
        session.say("Claude usage limit reached. Your limit will reset at 3pm.")
        session.result("Claude usage limit reached. Your limit will reset at 3pm.")
        return 0
    if model == "fake-crash":
        sys.stderr.write("fake-claude: something broke\n")
        return 3
    return play(session, model)


if __name__ == "__main__":
    sys.exit(main())
