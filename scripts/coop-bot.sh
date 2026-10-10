#!/usr/bin/env bash
# Start one bot copy of the game for a local co-op test. See docs/coop.md.
# You host in your own game (Steam launch option: --fastmp host_standard) and wait in character
# select. This starts a second copy without Steam, which joins it on this Mac as its own player.
#   scripts/coop-bot.sh --dry-run        print every check and command; change and start nothing
#   scripts/coop-bot.sh --prepare-only   run the checks and set up the bot's profile; start nothing
#   scripts/coop-bot.sh                  the same, then start the bot's copy, wait for its bridge
#                                        and say whether it got into your lobby
# Options:
#   --client-id N     the bot's player id and profile folder default/N (default 1000; never 1)
#   --port P          the bot's bridge port, passed as SPIREMIND_PORT (default 17773; never 17772)
#   --log FILE        the bot copy's game log (default ~/sts2bot/godot-<client id>.log)
#   --reset-profile   seed the profile's settings.save again; the old one is kept as a .bak file
#   --no-host-check   start even when your own game or its lobby is not seen
# It writes two things: the bot's settings.save under SlayTheSpire2/default/<client id>, and the
# log folder. It never writes under SlayTheSpire2/steam, into the game or into Steam's folders,
# never copies a mod and never opens the API key file.
set -euo pipefail
. "$(dirname "$0")/env.sh"

# The build the launch flags, the profile fields and the join rules were checked against
# (docs/game-api.md, section g). After a game update, check them again before raising this.
: "${EXPECTED_VERSION:=v0.107.1}"
: "${STS2_USER_DIR:=$HOME/Library/Application Support/SlayTheSpire2}"
: "${WORKSHOP_DIR:=$(dirname "$(dirname "$GAME_DIR")")/workshop/content/2868840}"
: "${COOP_WAIT_SECS:=90}" # for the bot copy's bridge to answer
: "${COOP_JOIN_SECS:=60}" # after that, for the bot's copy to get into your lobby
# The mod always reads ~/.config/spiremind. This override is for tests of this script only.
CONFIG_DIR="${COOP_TEST_CONFIG_DIR:-$HOME/.config/spiremind}"
GAME_BIN="$GAME_APP/Contents/MacOS/Slay the Spire 2"
BOT_MOD_ID="SpireMind"
HOST_PORT=17772  # ModEntry.DefaultPort: the bridge of your own game
LOBBY_PORT=33771 # the UDP port a --fastmp host listens on (NMultiplayerHostSubmenu.StartHostAsync)

client_id=1000
port=17773
log_file=""
dry_run=0
prepare_only=0
reset_profile=0
host_check=1
blockers=0

usage() { sed -n '2,/^set -euo pipefail$/p' "$0" | sed -e '$d' -e 's/^# \{0,1\}//'; }
die() { printf 'coop-bot: %s\n' "$*" >&2; exit 2; }
step() { printf '\n== %s\n' "$*"; }
more() { local line; for line in "$@"; do printf '        %s\n' "$line"; done; }
ok() { printf '  ok    %s\n' "$1"; shift; more "$@"; }
note() { printf '  note  %s\n' "$1"; shift; more "$@"; }
warn() { printf '  WARN  %s\n' "$1"; shift; more "$@"; }

# A reason not to go on. A real run ends here. A dry run counts it and carries on, so one dry
# run shows everything that is in the way.
stopped() {
  if [ "$dry_run" = 1 ]; then
    blockers=$((blockers + 1))
    more "(dry run: a real run ends here)"
    return 0
  fi
  printf '\nStopped. Nothing was started.\n' >&2
  exit 1
}
stop() { printf '  STOP  %s\n' "$1"; shift; more "$@"; stopped; }

while [ $# -gt 0 ]; do
  case "$1" in
    --client-id) [ $# -ge 2 ] || die "--client-id needs a number"; client_id="$2"; shift 2 ;;
    --client-id=*) client_id="${1#*=}"; shift ;;
    --port) [ $# -ge 2 ] || die "--port needs a number"; port="$2"; shift 2 ;;
    --port=*) port="${1#*=}"; shift ;;
    --log) [ $# -ge 2 ] || die "--log needs a file"; log_file="$2"; shift 2 ;;
    --log=*) log_file="${1#*=}"; shift ;;
    --dry-run) dry_run=1; shift ;;
    --prepare-only) prepare_only=1; shift ;;
    --reset-profile) reset_profile=1; shift ;;
    --no-host-check) host_check=0; shift ;;
    -h | --help) usage; exit 0 ;;
    *) die "unknown option $1 (--help lists them)" ;;
  esac
done

# The game parses the id strictly, and the profile folder is named after the parsed number.
re_id='^[1-9][0-9]{0,17}$'
re_port='^[1-9][0-9]{3,4}$'
re_secs='^[0-9]{1,4}$'
[[ $client_id =~ $re_id ]] || die "--client-id must be a plain number without leading zeros, not '$client_id'"
[ "$client_id" != 1 ] || die "client id 1 is the host's own id; a bot with it would collide with you in the run"
[[ $port =~ $re_port ]] && [ "$port" -ge 1024 ] && [ "$port" -le 65535 ] || die "--port must be from 1024 to 65535, not '$port'"
[ "$port" != "$HOST_PORT" ] || die "port $HOST_PORT is the bridge of your own game; the bot's copy needs another one"
[[ $COOP_WAIT_SECS =~ $re_secs ]] || die "COOP_WAIT_SECS must be a number of seconds"
[[ $COOP_JOIN_SECS =~ $re_secs ]] || die "COOP_JOIN_SECS must be a number of seconds"

[ -n "$log_file" ] || log_file="$HOME/sts2bot/godot-$client_id.log"
case "$log_file" in
  "~/"*) log_file="$HOME/${log_file#"~/"}" ;;
  /*) ;;
  *) log_file="$PWD/$log_file" ;;
esac
log_dir="$(dirname "$log_file")"
console_log="$log_file.out"
profile_dir="$STS2_USER_DIR/default/$client_id"
bot_settings="$profile_dir/settings.save"
launch_args=(--force-steam off --clientId "$client_id" --fastmp join --sts2bot=1 --log-file "$log_file")

PYTHON="${PYTHON:-$(command -v python3 || true)}"
[ -n "$PYTHON" ] && "$PYTHON" -I -c 'import json' >/dev/null 2>&1 || die "python3 is needed to read and write the JSON files"

# Every JSON read and write goes through this helper. -I keeps the environment and the current
# folder out of Python's module path. The files are data: nothing in them is run.
py() {
  "$PYTHON" -I - "$@" <<'PY'
import copy, json, os, re, shlex, shutil, sys, time

BOT = "SpireMind"
STOPPED = 10
# SettingsSave of v0.107.1: schema 5 (SettingsSaveV4ToV5), these JSON names (SettingsSave.cs).
SCHEMA = 5
KNOWN = ("schema_version fps_limit language window_position window_size fullscreen aspect_ratio "
         "target_display resize_windows vsync msaa volume_bgm volume_master volume_sfx volume_ambience "
         "mod_settings skip_intro_logo keyboard_mapping controller_mapping_type controller_mapping "
         "limit_fps_in_background full_console seen_ea_disclaimer").split()
WHERE = {"mods_directory": "local", "steam_workshop": "Workshop"}


class Problem(Exception):
    pass


def say(tag, first, *rest):
    print("  %-5s %s" % (tag, first))
    for line in rest:
        print("        " + line)


def load(path):
    with open(path, "rb") as f:
        return json.loads(f.read().decode("utf-8-sig"))


def compact(value):
    return json.dumps(value, separators=(",", ":"), ensure_ascii=False)


def semver(text):
    m = re.match(r"^v?(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:[-+].*)?$", text.strip()) if isinstance(text, str) else None
    return tuple(int(part or 0) for part in m.groups()) if m else None


def cmd_version(paths):
    for path in paths:
        try:
            version = load(path).get("version")
        except (OSError, ValueError, AttributeError):
            continue
        if isinstance(version, str):
            print(version)
            return 0
    return 1


def cmd_outside(path, roots):
    # 0 when path lies in none of the folders, links followed. Else prints the folder it is in.
    real = os.path.realpath(path)
    for root in roots:
        base = os.path.realpath(root).rstrip(os.sep)
        if real == base or real.startswith(base + os.sep):
            print(root)
            return 1
    return 0


def cmd_autostart(path):
    # The mod reads config.json leniently (comments, trailing commas), so a file that is not
    # plain JSON is searched as text instead of being taken for "off".
    try:
        with open(path, "rb") as f:
            text = f.read().decode("utf-8-sig", "replace")
    except OSError:
        print("off missing")
        return 0
    try:
        data = json.loads(text)
        print("on plain" if isinstance(data, dict) and data.get("autostart") is True else "off plain")
    except ValueError:
        print("on loose" if re.search(r'"autostart"\s*:\s*true\b', text) else "off loose")
    return 0


# Which mods a copy loads, following ModManager of v0.107.1: every *.json under a mods folder is
# a manifest when it has an id; a mod is off when settings list its id and source as disabled;
# a local copy that is on switches the Workshop copy of the same id off; a mod needs its minimum
# game version and its dependencies loaded.
class Mod(object):
    def __init__(self, data, folder, source):
        self.id = data["id"]
        self.version = data["version"] if isinstance(data.get("version"), str) else None
        self.gameplay = data.get("affects_gameplay") is not False
        self.min_game = data.get("min_game_version")
        self.deps = []
        deps = data.get("dependencies")
        for dep in deps if isinstance(deps, list) else []:
            if isinstance(dep, str):
                self.deps.append((dep, None))
            elif isinstance(dep, dict) and isinstance(dep.get("id"), str):
                self.deps.append((dep["id"], dep.get("min_version")))
        self.folder, self.source = folder, source
        self.state, self.why, self.net = None, "", False
        self.dll = os.path.join(folder, self.id + ".dll") if data.get("has_dll") is True else None

    @property
    def key(self):  # the name the join check compares
        return "%s-%s" % (self.id, self.version if self.version is not None else "")

    @property
    def label(self):
        return "%s %s" % (self.id, self.version or "(no version)")


def read_mods(root, source):
    mods = []
    if not os.path.isdir(root):
        return mods
    for folder, dirs, files in os.walk(root):
        dirs.sort()
        for name in sorted(files):
            if not name.endswith(".json"):
                continue
            try:
                data = load(os.path.join(folder, name))
            except (OSError, ValueError):
                continue
            if isinstance(data, dict) and isinstance(data.get("id"), str):
                mods.append(Mod(data, folder, source))
    return mods


def names_net_types(dll):
    # A mod that defines network message or action types names these interfaces in its DLL.
    try:
        if os.path.getsize(dll) > 64 << 20:
            return False
        with open(dll, "rb") as f:
            data = f.read()
    except OSError:
        return False
    return b"INetMessage" in data or b"INetAction" in data


def evaluate(mods, mod_settings, game):
    entries = mod_settings.get("mod_list") if isinstance(mod_settings, dict) else None
    entries = [e for e in entries if isinstance(e, dict)] if isinstance(entries, list) else []
    local_on = set()
    for mod in mods:
        if any(e.get("id") == mod.id and e.get("source") == mod.source and e.get("is_enabled") is False for e in entries):
            mod.state, mod.why = "off", "switched off in its settings"
        elif mod.source == "mods_directory":
            local_on.add(mod.id)
    for mod in mods:
        if mod.state is None and mod.source == "steam_workshop" and mod.id in local_on:
            mod.state, mod.why = "off", "the local copy is used instead"
    active = [mod for mod in mods if mod.state is None]
    if not (isinstance(mod_settings, dict) and mod_settings.get("mods_enabled") is True):
        for mod in active:
            mod.state, mod.why = "off", "its settings have not agreed to load mods"
        return
    loaded = set()

    def fail(mod, why):
        mod.state, mod.why = "failed", why
        return False

    def resolve(mod, chain):
        if mod.state is not None:
            return mod.state == "loaded"
        if mod.id in chain:
            return fail(mod, "circular dependency")
        if mod.id in loaded:
            return fail(mod, "another copy with this id is loaded")
        if mod.min_game is not None and game is not None:
            need = semver(mod.min_game)
            if need is None or need > game:
                return fail(mod, "needs game version %s" % mod.min_game)
        for dep_id, min_version in mod.deps:
            dep = next((m for m in active if m.id == dep_id), None)
            if dep is None or not resolve(dep, chain | {mod.id}):
                return fail(mod, "needs %s, which is not loaded" % dep_id)
            if min_version is not None:
                need, have = semver(min_version), semver(dep.version)
                if need is None or have is None or have < need:
                    return fail(mod, "needs %s %s or newer" % (dep_id, min_version))
        mod.state = "loaded"
        mod.net = mod.dll is not None and names_net_types(mod.dll)
        loaded.add(mod.id)
        return True

    for mod in active:
        resolve(mod, frozenset())


def seeded_mod_settings(host):
    # Your choices for local mods carry over. Workshop entries mean nothing without Steam.
    entries = []
    for e in (host.get("mod_list") if isinstance(host, dict) else None) or []:
        if isinstance(e, dict) and e.get("source") == "mods_directory" and isinstance(e.get("id"), str) and e["id"] != BOT:
            entries.append({"id": e["id"], "is_enabled": e.get("is_enabled") is not False, "source": "mods_directory"})
    entries.append({"id": BOT, "is_enabled": True, "source": "mods_directory"})
    return {"mod_list": entries, "mods_enabled": True}


def show(title, mods):
    lines = []
    for mod in mods:
        if mod.state == "loaded":
            tags = [WHERE[mod.source]] + (["changes gameplay"] if mod.gameplay else []) + (["adds network message types"] if mod.net else [])
            lines.append("  %s: %s" % (mod.label, ", ".join(tags)))
    lines += ["  not loaded: %s (%s, %s)" % (mod.label, WHERE[mod.source], mod.why) for mod in mods if mod.state != "loaded"]
    say("", title, *(lines or ["  nothing"]))


def cmd_mods(host_path, bot_path, mods_dir, workshop_dir, version):
    try:
        host_settings = load(host_path)
    except (OSError, ValueError) as ex:
        raise Problem("Can't read your game's settings.save (%s): %s" % (host_path, ex))
    host_ms = host_settings.get("mod_settings") if isinstance(host_settings, dict) else None
    if bot_path:
        try:
            bot_settings = load(bot_path)
        except (OSError, ValueError):
            bot_settings = None
        bot_ms = bot_settings.get("mod_settings") if isinstance(bot_settings, dict) else None
    else:
        bot_ms = seeded_mod_settings(host_ms)
    game = semver(version)
    test_dir = os.path.join(os.path.dirname(mods_dir.rstrip("/")), "mods_STEAMTEST")

    def discover(with_steam):
        mods = read_mods(mods_dir, "mods_directory") + read_mods(test_dir, "steam_workshop")
        return mods + (read_mods(workshop_dir, "steam_workshop") if with_steam else [])

    host, bot = discover(True), discover(False)
    evaluate(host, host_ms, game)
    evaluate(bot, bot_ms, game)
    show("Your game (with Steam) would load:", host)
    show("The bot's copy (no Steam, so no Workshop mods) would load:", bot)

    rc = 0
    mine = next((mod for mod in bot if mod.id == BOT), None)
    if mine is None or mine.state != "loaded":
        why = mine.why if mine is not None else "it is not in the game's mods folder"
        fix = "Run scripts/install.sh with the game closed." if mine is None or mine.state == "failed" else "Run this again with --reset-profile."
        say("STOP", "The bot's copy would not load %s (%s). Without it there is no bot." % (BOT, why), fix)
        rc = STOPPED

    def matters(mods):
        return dict((mod.key, mod) for mod in mods if mod.state == "loaded" and (mod.gameplay or mod.net))

    host_set, bot_set = matters(host), matters(bot)
    only_host = [host_set[key] for key in host_set if key not in bot_set]
    only_bot = [bot_set[key] for key in bot_set if key not in host_set]
    harmless = []
    for side, other in ((host, bot), (bot, host)):
        there = set(mod.key for mod in other if mod.state == "loaded")
        harmless += [mod.label for mod in side if mod.state == "loaded" and mod.id != BOT
                     and not (mod.gameplay or mod.net) and mod.key not in there]
    if harmless:
        say("note", "On one side only, and allowed: %s." % ", ".join(harmless),
            "The join only logs mods that say they do not change gameplay.")
    if not only_host and not only_bot:
        if rc == 0:
            say("ok", "Both copies would load the same gameplay mods and network message types.")
        return rc

    lines = []
    workshop = [mod for mod in only_host if mod.source == "steam_workshop"]
    if workshop:
        lines.append("Your game loads these. The bot's copy cannot: without Steam it never sees Workshop mods.")
        for mod in workshop:
            if mod.gameplay:
                lines.append("  %s changes gameplay. The join is refused with ModMismatch." % mod.label)
            if mod.net:
                lines.append("  %s adds network message types. The join check does not look at them, but the" % mod.label)
                lines.append("    two copies would number their messages differently, so the join breaks")
                lines.append("    without a clear error.")
            users = [m.label for m in host if m.state == "loaded" and any(dep == mod.id for dep, _ in m.deps)]
            lines.append("    %s" % ("Needed by: %s." % ", ".join(users) if users else "No other mod you have switched on needs it."))
        lines.append("Two ways out, for each mod above. Your choice. Close both copies of the game first.")
        lines.append("  A. Give both copies one local copy. A local copy wins over the Workshop one in your")
        lines.append("     game, so both load the same files. It stays that version until you delete it.")
        for mod in workshop:
            lines.append("       cp -R %s %s" % (shlex.quote(mod.folder), shlex.quote(os.path.join(mods_dir, mod.id))))
        lines.append("  B. Switch it off for the co-op session. In your game: Settings, the mod list, untick it.")
        lines.append("     Mods load only at startup, so quit and start the game again. Tick it again afterwards.")
    local = [mod for mod in only_host if mod.source != "steam_workshop"]
    if local or only_bot:
        lines.append("Your game and the bot's profile disagree about local mods:")
        lines += ["  %s: loaded by your game, not by the bot's copy." % mod.label for mod in local]
        lines += ["  %s: loaded by the bot's copy, not by your game." % mod.label for mod in only_bot]
        lines.append("  Run this again with --reset-profile to copy your mod choices to the bot's profile.")
    lines.append("Nothing was changed. Run this script again afterwards.")
    say("STOP", "The two copies would not load the same mods, so the bot could not join.", *lines)
    return STOPPED


def build_seed(src):
    if not isinstance(src, dict):
        raise Problem("Your settings.save is not a JSON object.")
    version = src.get("schema_version")
    if not isinstance(version, int) or isinstance(version, bool) or version < 4:
        raise Problem("Your settings.save has schema_version %r; this script knows 4 and up." % (version,))
    seed = dict((key, copy.deepcopy(value)) for key, value in src.items() if key in KNOWN)
    changes = [
        ("schema_version", SCHEMA, "the newest this build knows"),
        ("mod_settings", seeded_mod_settings(src.get("mod_settings")), "mods agreed to, so no consent popup"),
        ("seen_ea_disclaimer", True, "no early-access popup"),
        ("fullscreen", False, "a window"),
        ("window_size", {"X": 1280, "Y": 720}, ""),
        ("window_position", {"X": -1, "Y": -1}, "the game centres it"),
        ("volume_master", 0.0, "muted, so you hear one game"),
    ]
    for key, value, _ in changes:
        seed[key] = value
    return seed, changes, [key for key in src if key not in KNOWN]


def check_seed(data):
    ms = data.get("mod_settings") if isinstance(data, dict) else None
    good = (isinstance(ms, dict) and ms.get("mods_enabled") is True
            and any(e == {"id": BOT, "is_enabled": True, "source": "mods_directory"} for e in ms.get("mod_list", []))
            and data.get("schema_version") == SCHEMA and data.get("seen_ea_disclaimer") is True
            and data.get("fullscreen") is False and data.get("window_size") == {"X": 1280, "Y": 720})
    if not good:
        raise Problem("The settings written for the bot did not read back as expected.")


def cmd_seed(src_path, dest, user_dir, flags):
    dry, reset = "--dry-run" in flags, "--reset" in flags
    try:
        src = load(src_path)
    except (OSError, ValueError) as ex:
        raise Problem("Can't read your game's settings.save (%s): %s" % (src_path, ex))
    seed, changes, dropped = build_seed(src)
    check_seed(seed)
    # Only ever inside <user dir>/default: never your own steam/ folder, never the source file.
    allowed = os.path.join(os.path.realpath(user_dir), "default") + os.sep
    real = os.path.realpath(dest)
    if not real.startswith(allowed) or real == os.path.realpath(src_path):
        raise Problem("Refusing to write %s: it is not inside %s" % (dest, allowed))
    exists = os.path.lexists(dest)
    if exists and not reset:
        raise Problem("%s exists. It is only replaced with --reset-profile." % dest)
    backup = dest + ".bak-" + time.strftime("%Y%m%d-%H%M%S")

    def brief(key, value):
        if key != "mod_settings" or not isinstance(value, dict):
            return compact(value)
        mods = ["%s %s (%s)" % (e.get("id"), "on" if e.get("is_enabled") is not False else "off", WHERE.get(e.get("source"), "?"))
                for e in value.get("mod_list") or [] if isinstance(e, dict)]
        return "mods_enabled %s; %s" % (compact(value.get("mods_enabled")), ", ".join(mods) or "no mods listed")

    lines = ["from %s (only read)" % src_path]
    lines += ["  %s: %s -> %s%s" % (key, brief(key, src.get(key)), brief(key, value), "  (%s)" % why if why else "")
              for key, value, why in changes]
    if dropped:
        lines.append("  left out, since this build does not know them: %s" % ", ".join(dropped))
    lines.append("  everything else as in your settings (language, keys, graphics)")
    if exists:
        lines.append("the old one is kept as %s" % backup)
    if dry:
        say("", "would write %s" % dest, *lines)
        return 0

    made, tmp = [], "%s.tmp-%d" % (dest, os.getpid())
    try:
        missing, folder = [], os.path.dirname(dest)
        while not os.path.isdir(folder):
            missing.append(folder)
            folder = os.path.dirname(folder)
        for folder in reversed(missing):
            os.mkdir(folder)
            made.append(folder)
        with open(tmp, "x", encoding="utf-8") as f:
            f.write(json.dumps(seed, indent=2, ensure_ascii=False) + "\n")
            f.flush()
            os.fsync(f.fileno())
        check_seed(load(tmp))
        if exists:
            shutil.copy2(dest, backup)
        os.replace(tmp, dest)
    except BaseException:
        # No half-made profile: the temporary file goes, and so do the folders made just now.
        try:
            os.unlink(tmp)
        except OSError:
            pass
        for folder in reversed(made):
            try:
                os.rmdir(folder)
            except OSError:
                pass
        raise
    say("ok", "wrote %s" % dest, *lines)
    return 0


def cmd_profile(profile_dir, user_dir):
    # The bot's copy keeps its whole profile in <user dir>/default/<id> and writes there as it
    # plays. That must be a plain folder of its own. A link in its place, or inside it, would
    # send those writes somewhere else: into your own steam/ folder, for one.
    profile_dir = profile_dir.rstrip(os.sep)
    want = os.path.join(os.path.realpath(user_dir), "default", os.path.basename(profile_dir))
    real = os.path.realpath(profile_dir)
    if real != want:
        raise Problem("The bot's profile folder is a link, or lies behind one:", "  %s" % profile_dir,
                      "It leads to:", "  %s" % real,
                      "The bot's copy would read and write its saves there. Remove the link first.")
    for folder, dirs, files in os.walk(profile_dir):
        for name in dirs + files:
            path = os.path.join(folder, name)
            if os.path.islink(path):
                raise Problem("The bot's profile holds a link:", "  %s" % path,
                              "It leads to:", "  %s" % os.path.realpath(path),
                              "The bot's copy would read and write there. Remove the link first.")
    return 0


def cmd_inspect(path):
    try:
        data = load(path)
    except (OSError, ValueError):
        data = None
    ms = data.get("mod_settings") if isinstance(data, dict) else None
    if not (isinstance(ms, dict) and ms.get("mods_enabled") is True):
        say("STOP", "The bot's settings.save has not agreed to load mods, or can't be read.",
            "Its copy would start without the bot and show the mod popup, whose Yes quits the game.",
            "Run this again with --reset-profile.")
        return STOPPED
    if data.get("seen_ea_disclaimer") is not True:
        say("WARN", "The bot's profile has not seen the early-access popup. It will show once,",
            "and can hide a join error. --reset-profile seeds the settings again.")
    if data.get("fullscreen") is not False:
        say("WARN", "The bot's profile is set to fullscreen. --reset-profile makes it a window again.")
    say("ok", "Its settings have agreed to load mods.")
    return 0


def main(argv):
    cmd, args = argv[0], argv[1:]
    try:
        if cmd == "version":
            return cmd_version(args)
        if cmd == "outside":
            return cmd_outside(args[0], args[1:])
        if cmd == "autostart":
            return cmd_autostart(args[0])
        if cmd == "mods":
            return cmd_mods(*args)
        if cmd == "seed":
            return cmd_seed(args[0], args[1], args[2], args[3:])
        if cmd == "profile":
            return cmd_profile(args[0], args[1])
        if cmd == "inspect":
            return cmd_inspect(args[0])
        raise Problem("unknown helper command %s" % cmd)
    except Problem as ex:
        say("STOP", *[str(line) for line in ex.args])
        return STOPPED
    except OSError as ex:
        say("STOP", "A file could not be read or written: %s" % ex, "Nothing half-made was left behind.")
        return STOPPED
    except KeyboardInterrupt:
        return 130


sys.exit(main(sys.argv[1:]))
PY
}

# The log path is the one place a caller can aim what this script and the bot's copy write.
# Keep it out of the game's data, the game itself, Steam's folders and the brain's folder.
keep_out=("$STS2_USER_DIR" "$GAME_DIR" "$WORKSHOP_DIR" "$CONFIG_DIR")
steamapps="$(dirname "$(dirname "$GAME_DIR")")"
[ "$(basename "$steamapps")" != steamapps ] || keep_out+=("$(dirname "$steamapps")")
inside="$(py outside "$log_file" "${keep_out[@]}")" ||
  die "--log must not be inside ${inside:-a game folder}; use a folder of its own, like ~/sts2bot"

# Runs a helper step that may end in a STOP of its own.
helper() {
  local rc=0
  py "$@" || rc=$?
  case "$rc" in
    0) ;;
    10) stopped ;;
    *) die "the JSON helper failed in '$1' (exit $rc)" ;;
  esac
}

# Running copies of the game, by process name and by path, so a copy started by Steam or by
# this script is found either way. Fills host_pid, host_local and bot_ids ("id:pid" words).
re_host='--fastmp[= ](host|load)'
re_nosteam='--force-steam[= ][Oo][Ff][Ff]'
re_client='--clientId[= ]([0-9]+)'
scan_game_procs() {
  local named pid cmd
  host_pid="" host_local=0 bot_ids=""
  named=" $(pgrep -x "Slay the Spire 2" 2>/dev/null | tr '\n' ' ') " || true
  while read -r pid cmd; do
    case "$cmd" in
      "$GAME_BIN" | "$GAME_BIN "*) ;;
      *) case "$named" in *" $pid "*) ;; *) continue ;; esac ;;
    esac
    if [[ $cmd =~ $re_nosteam ]] && ! [[ $cmd =~ $re_host ]]; then
      # A copy without Steam that joins: its id is --clientId, or 1000 without one.
      if [[ $cmd =~ $re_client ]]; then bot_ids="$bot_ids ${BASH_REMATCH[1]}:$pid"; else bot_ids="$bot_ids 1000:$pid"; fi
    else
      host_pid="$pid"
      if [[ $cmd =~ $re_host ]]; then host_local=1; fi
    fi
  done < <(ps -axww -o pid=,command= 2>/dev/null || true)
}

port_in_use() {
  if command -v lsof >/dev/null 2>&1; then
    lsof -nP -iTCP:"$1" -sTCP:LISTEN >/dev/null 2>&1
  else
    nc -z 127.0.0.1 "$1" >/dev/null 2>&1
  fi
}

# Whether a local lobby is open: 0 when something listens on its UDP port, 1 when nothing does,
# 2 when that can't be told. Only your own game, hosting with --fastmp, opens that port.
# A listener shows in lsof as "UDP *:33771". A joined copy's own socket ("...->127.0.0.1:33771")
# does not count.
lobby_open() {
  local seen line
  command -v lsof >/dev/null 2>&1 || return 2
  seen="$(lsof -nP -iUDP:"$LOBBY_PORT" 2>/dev/null || true)"
  while IFS= read -r line; do
    case "$line" in
      *"->"*) ;;
      *":$LOBBY_PORT") return 0 ;;
    esac
  done <<<"$seen"
  return 1
}

# The bot copy's log: the file the game writes for --log-file, else what it printed.
bot_log() { if [ -s "$log_file" ]; then printf '%s' "$log_file"; else printf '%s' "$console_log"; fi; }

# What the bot's copy logs when its one join attempt fails (ENetClient, JoinFlow and
# NJoinFriendScreen of v0.107.1), and the lines that say why.
re_join_fail='Connection timed out!|Timed out waiting for handshake ack!|Received non-success code during handshake|Received initial join message with failure|Received connection failed exception while joining game|Received unexpected exception .* while joining game'
re_join_why="$re_join_fail|Version mismatch\\. Host|Mod mismatch\\. Host|Mods that host has|Mods that we have|ModelDb hash mismatch"

# One line of advice for the failure the log lines in $1 show.
join_advice() {
  case "$1" in
    *"Connection timed out!"*)
      more "Nobody answered on UDP $LOBBY_PORT. Your game was not in the co-op character select," \
        "or it is not hosting locally (launch option --fastmp host_standard)." ;;
    *"non-success code during handshake"*)
      more "Your lobby already holds a player with client id $client_id: the ghost of an earlier copy." \
        "In your game, back out of the lobby and host again." ;;
    *"Mod mismatch"* | *"Mods that host has"*)
      more "The two copies loaded different gameplay mods. Close both, then run this with --dry-run." ;;
    *"ModelDb hash mismatch"*)
      more "A mod on one side adds cards, relics or the like. Make that mod the same on both sides." ;;
    *"Version mismatch"*)
      more "The two copies are different game versions. Quit both and start again." ;;
    *RunInProgress*)
      more "Your run had already started. A bot can only join in character select." ;;
    *NotInSaveGame*)
      more "You loaded a saved run that does not know client id $client_id. Use the id it began with." ;;
  esac
}

print_next() {
  cat <<EOF
Next:
  1. In the bot's window (titled "Slay The Spire 2 (Client)", small, no sound): pick the Ironclad,
     then press the button that starts a solo run. A new profile has only the Ironclad.
     The first time it asks about tutorials: answer No (the cancel button).
  2. In your own window the bot shows as a second player. Pick your character and press the
     same button. The run starts when both have pressed it.
  3. When the run has started, start the bot. In the bot's window: press T, type /start, press
     Enter. Or from a Terminal:
       curl -s http://127.0.0.1:$port/chat -d '{"from":"Mark","text":"/start"}'
     Never type /start in your own window: there it makes the bot play your hand.
To stop the bot playing: /stop, the same two ways. Do this before you close the bot's copy:
  /start is kept in the shared settings file until /stop, and your own game would read it at
  its next start.
To close the bot's copy: quit your own game first, or take the bot out of your game (Back in its
  character select; Esc, Disconnect in a run). Only then Cmd+Q in the bot's window.
If something breaks:
  - A bot copy that quit, crashed or was killed while in your lobby or run stays there as a
    ghost and cannot rejoin. In your game, back out of the lobby and host again. Then run this
    script again with the same client id.
  - The bot cannot join a run that has already started.
  - Never press Load in the Multiplayer menu without --fastmp host in your launch options, and
    never use --fastmp load: the co-op save is renamed to .corrupt.
  - The bot's log is $log_file
    docs/coop.md says what to look for in it.
EOF
}

finish() {
  if [ "$dry_run" = 1 ]; then
    if [ "$blockers" = 0 ]; then
      printf '\nDry run done. No STOP above. Nothing was changed or started.\n'
    else
      printf '\nDry run done. A real run would stop at the first of the %s STOP(s) above. Nothing was changed or started.\n' "$blockers"
      exit 1
    fi
  else
    printf '\n%s\n' "$1"
  fi
  exit 0
}

# ---------------------------------------------------------------------------------------------
step "1. Checks"
[ "$dry_run" = 0 ] || note "Dry run: checks only read. Nothing is written or started."

[ -x "$GAME_BIN" ] || die "the game is not at $GAME_APP (set GAME_DIR in scripts/local.env)"
# Where the game itself looks on macOS: Contents/Resources first, then beside the binary.
version="$(py version "$GAME_APP/Contents/Resources/release_info.json" "$GAME_APP/Contents/MacOS/release_info.json")" || version=""
if [ "$version" = "$EXPECTED_VERSION" ]; then
  ok "Game $version at $GAME_APP"
else
  stop "The game is ${version:-an unknown version}. This script was checked against $EXPECTED_VERSION only." \
    "Launch flags, profile fields and join rules can change between versions." \
    "Check them again (docs/game-api.md, section g), then run with EXPECTED_VERSION=${version:-<version>}."
fi

installed="$MODS_DIR/$BOT_MOD_ID"
staged="$REPO_ROOT/out/$BOT_MOD_ID"
if [ -f "$installed/$BOT_MOD_ID.dll" ] && [ -f "$installed/$BOT_MOD_ID.json" ]; then
  if [ ! -d "$staged" ]; then
    note "SpireMind is installed. There is no out/SpireMind here to compare it with."
  elif differences="$(diff -rq "$staged" "$installed" 2>&1)"; then
    ok "SpireMind is installed and is this checkout's build (same as out/SpireMind)"
  else
    warn "The installed SpireMind is not this checkout's build (out/SpireMind differs)." \
      "Run scripts/install.sh with the game closed. The first differences:"
    printf '%s\n' "$differences" | head -n 3 | sed 's/^/          /'
  fi
  if [ -f "$staged/$BOT_MOD_ID.dll" ]; then
    newer="$(find "$REPO_ROOT/src/SpireMind" "$REPO_ROOT/src/SpireMind.Core" \( -name obj -o -name bin \) -prune \
      -o -type f -newer "$staged/$BOT_MOD_ID.dll" -print -quit 2>/dev/null || true)"
    [ -z "$newer" ] || warn "Source files are newer than the build, for example ${newer#"$REPO_ROOT/"}." \
      "Run scripts/install.sh with the game closed."
  fi
else
  stop "SpireMind is not installed in $MODS_DIR." "Run scripts/install.sh with the game closed."
fi

config="$CONFIG_DIR/config.json"
answer="$(py autostart "$config")" || die "could not read $config"
read -r autostart how <<<"$answer"
[ "$autostart" = on ] || [ "$autostart" = off ] || die "could not tell whether autostart is on in $config"
if [ "$autostart" = on ]; then
  stop "The brain's autostart is on in $config." \
    "Both copies read that one file when they start. With autostart on, the brain would also" \
    "start in your own window and play your hand." \
    "Turn it off: type /stop in your own game's chat (T). That also stops a brain playing there." \
    "With no game running, set \"autostart\": false in that file instead."
else
  case "$how" in
    missing) ok "Brain autostart is off (no config.json yet, so the defaults apply)" ;;
    loose) ok "Brain autostart is off (config.json is not plain JSON; read as text)" ;;
    *) ok "Brain autostart is off" ;;
  esac
fi
warn "Both copies share one settings file, one spend file and one journal for now:" \
  "$CONFIG_DIR/config.json and spend.json, and runs/current.md." \
  "/start, /stop, /model and /effort typed in either window are saved for both."

scan_game_procs
id_free=1
for entry in $bot_ids; do
  if [ "${entry%%:*}" = "$client_id" ]; then
    id_free=0
    stop "A bot copy with client id $client_id is already running (pid ${entry##*:})." \
      "Nothing to do: it is safe to leave it. For a second bot, pass another --client-id and --port."
  fi
done
if port_in_use "$port"; then
  holder="$(lsof -nP -t -iTCP:"$port" -sTCP:LISTEN 2>/dev/null | head -n 1 || true)"
  stop "Port $port is in use${holder:+ (by pid $holder)}, so the bot's bridge could not listen there." "Pass another --port."
elif [ "$id_free" = 1 ]; then
  ok "Client id $client_id and port $port are free"
fi

host_log="$STS2_USER_DIR/logs/godot.log"
if [ -n "$host_pid" ]; then
  ok "Your own game is running (pid $host_pid)"
  [ "$host_local" = 1 ] || warn "Its command line does not show --fastmp host_standard." \
    "If no lobby opens, the Steam launch option did not reach the game."
  lobby=0
  lobby_open || lobby=$?
  case "$lobby" in
    0) ok "It has a local lobby open (UDP $LOBBY_PORT)" ;;
    1) note "It has no local lobby open yet (nothing listens on UDP $LOBBY_PORT)." \
      "It must be in the co-op character select before the bot starts." ;;
    *) note "It must be in the co-op character select before the bot starts. This script can't see that." ;;
  esac
else
  note "Your own game is not running. It must be in the co-op character select, as host, before the bot starts."
fi

if [ -n "${HOST_SETTINGS:-}" ]; then
  [ -f "$HOST_SETTINGS" ] || die "HOST_SETTINGS is not a file: $HOST_SETTINGS"
else
  found=()
  for candidate in "$STS2_USER_DIR"/steam/*/settings.save; do
    [ ! -f "$candidate" ] || found+=("$candidate")
  done
  case "${#found[@]}" in
    0) die "no settings.save under $STS2_USER_DIR/steam: start your own game from Steam once first" ;;
    1) HOST_SETTINGS="${found[0]}" ;;
    *) die "several Steam accounts under $STS2_USER_DIR/steam: set HOST_SETTINGS to your settings.save" ;;
  esac
fi
host_dir="$(dirname "$HOST_SETTINGS")"
for saved in "$host_dir"/profile*/saves/current_run_mp.save "$host_dir"/modded/profile*/saves/current_run_mp.save; do
  [ ! -f "$saved" ] || warn "You have a saved co-op run: $saved" \
    "Hosting with --fastmp host_standard starts a new run in its place." \
    "To go on with it, use --fastmp host and press Load, with the same bot client id as before."
done

# The bot's copy will write its saves under default/<client id>. Only a plain folder will do.
helper profile "$profile_dir" "$STS2_USER_DIR"

# ---------------------------------------------------------------------------------------------
step "2. Mods"
bot_view="$bot_settings"
if [ ! -f "$bot_settings" ] || [ "$reset_profile" = 1 ]; then
  bot_view="" # judged as the profile will be seeded
fi
helper mods "$HOST_SETTINGS" "$bot_view" "$MODS_DIR" "$WORKSHOP_DIR" "${version:-$EXPECTED_VERSION}"

# ---------------------------------------------------------------------------------------------
step "3. Profile"
if [ -n "$bot_view" ]; then
  ok "The bot's profile exists and is left as it is: $profile_dir"
  helper inspect "$bot_settings"
else
  seed_flags=()
  [ "$dry_run" = 0 ] || seed_flags+=(--dry-run)
  [ "$reset_profile" = 0 ] || seed_flags+=(--reset)
  helper seed "$HOST_SETTINGS" "$bot_settings" "$STS2_USER_DIR" ${seed_flags[@]+"${seed_flags[@]}"}
fi

[ "$prepare_only" = 0 ] || finish "Prepared. Nothing was started."

# ---------------------------------------------------------------------------------------------
step "4. Launch"
# The bot's copy tries to join once, about 10 seconds after it starts. So the lobby must be open
# now: your game running, and its local lobby listening.
scan_game_procs
lobby=0
lobby_open || lobby=$?
if [ "$host_check" = 0 ]; then
  note "Not checking for your own game and its lobby, because of --no-host-check."
elif [ "$lobby" = 0 ]; then
  ok "A local lobby is open on UDP $LOBBY_PORT${host_pid:+ (your game, pid $host_pid)}. Stay in character select."
elif [ -z "$host_pid" ] && [ "$dry_run" = 1 ]; then
  note "Your own game is not running. A real run stops here until it is (skipped in a dry run)."
elif [ -z "$host_pid" ]; then
  stop "Your own game is not running." \
    "Start it from Steam with the launch option --fastmp host_standard and wait in character select." \
    "Then run this again. --no-host-check skips this check."
elif [ "$lobby" = 1 ] && [ "$dry_run" = 1 ]; then
  note "Your own game is running, but no local lobby is open. A real run stops here (skipped in a dry run)."
elif [ "$lobby" = 1 ]; then
  stop "Your own game is running (pid $host_pid), but no local lobby is open." \
    "Nothing listens on UDP $LOBBY_PORT, so the bot's one join attempt would time out." \
    "Either you are not in the co-op character select yet: go there, then run this again." \
    "Or the launch option did not reach the game. This must show --fastmp host_standard:" \
    "  grep 'Command Line Args' $(printf '%q' "$host_log")" \
    "--no-host-check skips this check."
else
  ok "Your own game is running (pid $host_pid). It must be in the co-op character select now."
fi

launch_line="$(printf '%q ' "$GAME_BIN" "${launch_args[@]}")"
if [ "$dry_run" = 1 ]; then
  note "would run: mkdir -p $(printf '%q' "$log_dir")"
  note "would start, in the background and detached from the terminal:" \
    "cd $(printf '%q' "$log_dir") && SPIREMIND_PORT=$port nohup $launch_line\\" \
    "  > $(printf '%q' "$console_log") 2>&1 < /dev/null &"
  note "would wait up to $COOP_WAIT_SECS s for its bridge: curl -s http://127.0.0.1:$port/health"
  note "would wait up to $COOP_JOIN_SECS s for the join: character select in" \
    "curl -s http://127.0.0.1:$port/state, or a failed join in its log"
  note "would then print:"
  print_next | sed 's/^/        /'
  finish ""
fi

mkdir -p "$log_dir"
# The game starts its log file afresh. Keep the last one, so a failed try can still be read.
[ ! -s "$log_file" ] || mv -f "$log_file" "$log_file.prev"
(cd "$log_dir" && SPIREMIND_PORT="$port" exec nohup "$GAME_BIN" "${launch_args[@]}" >"$console_log" 2>&1 </dev/null) &
bot_pid=$!
disown 2>/dev/null || true
ok "Started the bot's copy (pid $bot_pid)" "$launch_line" "log: $log_file"

printf '  wait  for its bridge on port %s (up to %s s) ' "$port" "$COOP_WAIT_SECS"
deadline=$((SECONDS + COOP_WAIT_SECS))
state=timeout
while :; do
  if answer="$(curl -s -m 2 "http://127.0.0.1:$port/health" 2>/dev/null)" && [[ $answer == *spiremind* ]]; then
    state=up
    break
  fi
  if ! kill -0 "$bot_pid" 2>/dev/null; then
    state=gone
    break
  fi
  [ "$SECONDS" -lt "$deadline" ] || break
  printf '.'
  sleep 1
done
printf '\n'

case "$state" in
  up)
    ok "The bridge answers: $answer"
    ;;
  gone)
    printf "  STOP  The bot's copy quit before its bridge answered. The end of its log:\n"
    for file in "$log_file" "$console_log"; do
      [ ! -s "$file" ] || { tail -n 12 "$file" | sed 's/^/        /'; break; }
    done
    exit 1
    ;;
  timeout)
    printf '  STOP  No answer from the bridge on port %s after %s s. The copy is still running (pid %s).\n' "$port" "$COOP_WAIT_SECS" "$bot_pid"
    more "Look at its window first: a popup there may want an answer." \
      "Then its log: grep -n 'SpireMind\\|RUNNING MODDED\\|Skipping loading mod' $(printf '%q' "$(bot_log)")" \
      "If it is in your lobby, leave the lobby in its window first (Back). Then Cmd+Q there," \
      "and try again."
    exit 1
    ;;
esac

# The bridge says nothing about the join. The mod's own snapshot does: it names the menu the
# bot's copy is on, and a client only reaches character select through a join that worked.
printf '  wait  for it to get into your lobby (up to %s s) ' "$COOP_JOIN_SECS"
deadline=$((SECONDS + COOP_JOIN_SECS))
joined=unknown
while :; do
  screen="$(curl -s -m 2 "http://127.0.0.1:$port/state" 2>/dev/null)" || screen=""
  if [[ $screen == *NCharacterSelectScreen* ]]; then
    joined=yes
    break
  fi
  if grep -q -E "$re_join_fail" "$(bot_log)" 2>/dev/null; then
    joined=no
    break
  fi
  if ! kill -0 "$bot_pid" 2>/dev/null; then
    joined=gone
    break
  fi
  [ "$SECONDS" -lt "$deadline" ] || break
  printf '.'
  sleep 1
done
printf '\n'

case "$joined" in
  yes)
    ok "It is in your lobby: its window shows character select."
    ;;
  no)
    printf "  STOP  The bot's copy did not get into your lobby. Its log says:\n"
    why="$(grep -E "$re_join_why" "$(bot_log)" 2>/dev/null | head -n 6 | cut -c1-240 || true)"
    printf '%s\n' "$why" | sed 's/^/          /'
    join_advice "$why"
    more "Its window shows the error. Close it with Cmd+Q there, fix the cause, then run this again." \
      "docs/coop.md, \"When the join fails\", lists every case."
    exit 1
    ;;
  gone)
    printf "  STOP  The bot's copy quit before it got into your lobby. The end of its log:\n"
    tail -n 12 "$(bot_log)" 2>/dev/null | sed 's/^/        /' || true
    exit 1
    ;;
  unknown)
    warn "Can't tell whether it got into your lobby: no character select and no error in $COOP_JOIN_SECS s." \
      "Look at the bot's window. If it shows character select next to you, carry on below." \
      "If it shows a popup, see docs/coop.md, \"When the join fails\"."
    ;;
esac

printf '\n'
print_next
printf '\nThe bot is pid %s. Its log: %s\n' "$bot_pid" "$log_file"
