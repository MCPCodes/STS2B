#!/usr/bin/env bash
# Try guest mode on this Mac alone, before a friend tries it from theirs. See docs/coop-guest.md.
# It starts a second copy of the game WITHOUT Steam as a guest: player 2000, dialing this Mac's
# own network address (not 127.0.0.1), with its own profile folder as the bot copies have.
# You play that copy yourself, in its window.
#   scripts/coop-guest-trial.sh --dry-run    print every check and the command; change and start nothing
#   scripts/coop-guest-trial.sh              prepare the copy's profile, start it, say whether it joined
# Options:
#   --address A       the address to dial (default: this Mac's address on its main network)
#   --client-id N     the guest's player id and profile folder default/N (default 2000)
#   --name N          what the guest's copy calls its own player (default Amir)
#   --host-name N     what the guest's copy calls you (default: your user name with a capital)
#   --log FILE        the copy's game log (default ~/sts2bot/godot-guest-<client id>.log)
# Before you run it:
#   1. scripts/install.sh with the game closed, so your game has the build with guest mode.
#   2. Start your game from Steam with --fastmp host_standard and wait in character select.
#      A bot can be in the lobby too (/bot add): then the trial shows three players.
# It writes the copy's settings under SlayTheSpire2/default/<client id> (through
# scripts/coop-bot.sh --prepare-only) and the log folder. It never writes under
# SlayTheSpire2/steam, never copies a mod and never opens the API key file.
set -euo pipefail
. "$(dirname "$0")/env.sh"

: "${STS2_USER_DIR:=$HOME/Library/Application Support/SlayTheSpire2}"
: "${TRIAL_WAIT_SECS:=60}"
GAME_BIN="$GAME_APP/Contents/MacOS/Slay the Spire 2"
LOBBY_PORT=33771
# coop-bot.sh wants a bridge port for the copy it prepares. A guest copy opens none; this one
# is only outside the ports the bots and your own game use.
SPARE_PORT=17799

address=""
client_id=2000
guest_name="Amir"
host_name=""
log_file=""
dry_run=0
blockers=0

usage() { sed -n '2,/^set -euo pipefail$/p' "$0" | sed -e '$d' -e 's/^# \{0,1\}//'; }
die() { printf 'coop-guest-trial: %s\n' "$*" >&2; exit 2; }
step() { printf '\n== %s\n' "$*"; }
more() { local line; for line in "$@"; do printf '        %s\n' "$line"; done; }
ok() { printf '  ok    %s\n' "$1"; shift; more "$@"; }
note() { printf '  note  %s\n' "$1"; shift; more "$@"; }
# A reason not to go on. A real run ends here. A dry run counts it and carries on, so one dry
# run shows everything that is in the way.
stop() {
  printf '  STOP  %s\n' "$1"; shift; more "$@"
  if [ "$dry_run" = 1 ]; then
    blockers=$((blockers + 1))
    more "(dry run: a real run ends here)"
    return 0
  fi
  printf '\nStopped. Nothing was started.\n' >&2
  exit 1
}

while [ $# -gt 0 ]; do
  case "$1" in
    --address) [ $# -ge 2 ] || die "--address needs an address"; address="$2"; shift 2 ;;
    --address=*) address="${1#*=}"; shift ;;
    --client-id) [ $# -ge 2 ] || die "--client-id needs a number"; client_id="$2"; shift 2 ;;
    --client-id=*) client_id="${1#*=}"; shift ;;
    --name) [ $# -ge 2 ] || die "--name needs a name"; guest_name="$2"; shift 2 ;;
    --name=*) guest_name="${1#*=}"; shift ;;
    --host-name) [ $# -ge 2 ] || die "--host-name needs a name"; host_name="$2"; shift 2 ;;
    --host-name=*) host_name="${1#*=}"; shift ;;
    --log) [ $# -ge 2 ] || die "--log needs a file"; log_file="$2"; shift 2 ;;
    --log=*) log_file="${1#*=}"; shift ;;
    --dry-run) dry_run=1; shift ;;
    -h | --help) usage; exit 0 ;;
    *) die "unknown option $1 (--help lists them)" ;;
  esac
done

re_id='^[1-9][0-9]{0,17}$'
re_name='^[A-Za-z0-9._-]{1,24}$'
re_addr='^([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})$'
[[ $client_id =~ $re_id ]] || die "--client-id must be a plain number without leading zeros, not '$client_id'"
[ "$client_id" != 1 ] || die "client id 1 is the host's own"
{ [ "$client_id" -lt 1000 ] || [ "$client_id" -gt 1002 ]; } || die "client ids 1000 to 1002 are the bots'; a guest takes another, such as 2000"
if [ -z "$host_name" ]; then
  host_name="$(id -un)"
  host_name="$(printf '%s' "${host_name:0:1}" | tr '[:lower:]' '[:upper:]')${host_name:1}"
fi
[[ $guest_name =~ $re_name ]] || die "--name must be 1 to 24 letters, digits, dots, dashes or underscores, not '$guest_name'"
[[ $host_name =~ $re_name ]] || die "--host-name must be 1 to 24 letters, digits, dots, dashes or underscores, not '$host_name'"

[ -n "$log_file" ] || log_file="$HOME/sts2bot/godot-guest-$client_id.log"
case "$log_file" in
  "~/"*) log_file="$HOME/${log_file#"~/"}" ;;
  /*) ;;
  *) log_file="$PWD/$log_file" ;;
esac
log_dir="$(dirname "$log_file")"
console_log="$log_file.out"

# ---------------------------------------------------------------------------------------------
step "1. The address"
if [ -z "$address" ]; then
  iface="$(route -n get default 2>/dev/null | awk '/interface:/ {print $2; exit}' || true)"
  for candidate in "$iface" en0 en1; do
    [ -n "$candidate" ] || continue
    address="$(ipconfig getifaddr "$candidate" 2>/dev/null || true)"
    [ -z "$address" ] || { iface="$candidate"; break; }
  done
  [ -n "$address" ] || die "this Mac has no network address (not on Wi-Fi or cable?). Join a network, or pass --address"
  ok "This Mac's address on $iface is $address" "That is also the address a friend on this network puts in their launch options."
else
  ok "Dialing $address (given with --address)"
fi
[[ $address =~ $re_addr ]] || die "the address must be four numbers with dots, not '$address'"
case "$address" in
  127.*) note "$address is this Mac's loopback address. The trial then shows the guest role, but not that" \
    "your lobby can be reached on the network address." ;;
esac

# ---------------------------------------------------------------------------------------------
step "2. The build"
[ -x "$GAME_BIN" ] || die "the game is not at $GAME_APP (set GAME_DIR in scripts/local.env)"
installed="$MODS_DIR/SpireMind"
[ -f "$installed/SpireMind.dll" ] || stop "SpireMind is not installed in $MODS_DIR." "Run scripts/install.sh with the game closed."
# The installed DLL must know the guest option. .NET keeps such text as two-byte characters.
PYTHON="${PYTHON:-$(command -v python3 || true)}"
[ -n "$PYTHON" ] || die "python3 is needed (scripts/coop-bot.sh needs it too)"
if "$PYTHON" -I -c 'import sys
sys.exit(0 if "spiremind-host".encode("utf-16-le") in open(sys.argv[1], "rb").read() else 1)' "$installed/SpireMind.dll"; then
  ok "The installed SpireMind has guest mode"
else
  stop "The installed SpireMind is a build without guest mode." "Run scripts/install.sh with the game closed, then start your game again."
fi
[ ! -f "$installed/guest-only.txt" ] || stop "The installed SpireMind is the GUEST package (guest-only.txt is in $installed)." \
  "With it your own game runs no bridge, no brain and no bots." "Run scripts/install.sh with the game closed."

# ---------------------------------------------------------------------------------------------
step "3. The copy's profile"
note "The checks below are scripts/coop-bot.sh --prepare-only. It seeds the copy's settings.save," \
  "so the copy loads mods without asking. What it says about the brain's settings and about a" \
  "'bot' does not apply here: a guest copy runs no brain and opens no bridge."
prepare=("$(dirname "$0")/coop-bot.sh" --client-id "$client_id" --port "$SPARE_PORT" --prepare-only)
[ "$dry_run" = 0 ] || prepare+=(--dry-run)
"${prepare[@]}" || stop "The copy's profile could not be prepared (see above)."

# ---------------------------------------------------------------------------------------------
step "4. Your lobby"
lobby=unknown
if command -v lsof >/dev/null; then
  if lsof -nP -iUDP:"$LOBBY_PORT" 2>/dev/null | grep -q .; then lobby=open; else lobby=closed; fi
fi
case "$lobby" in
  open) ok "Something listens on UDP $LOBBY_PORT: your lobby is open. Stay in character select." ;;
  closed) note "Nothing listens on UDP $LOBBY_PORT, so the copy's first try will time out after 10 s." \
    "That shows the failure a friend would see. Then open your lobby (start your game with" \
    "--fastmp host_standard) and, in the copy's window, press OK, Back, then Join." ;;
  *) note "Can't see whether your lobby is open. It must be, or the copy's first try times out." ;;
esac

# ---------------------------------------------------------------------------------------------
step "5. Start the guest copy"
launch_args=(--force-steam=off "--clientId=$client_id" --fastmp=join "--spiremind-host=$address"
  "--spiremind-name=$guest_name" "--spiremind-host-name=$host_name" --log-file "$log_file")
launch_line="$(printf '%q ' "$GAME_BIN" "${launch_args[@]}")"

print_next() {
  cat <<EOF
What to look for:
  In the copy's log:   grep -n 'SpireMind\] \(role\|guest\|coop: names\)' $(printf '%q' "$log_file")
    role: guest copy; joins $address:$LOBBY_PORT as player $client_id; no brain, no bridge, no bot copies, no files
    guest: harmony: the game's fast join now takes the host's address from the launch options
    guest: join attempt 1: dialing $address:$LOBBY_PORT as player $client_id ...
    coop: names (lobby, this copy is a guest's, local $client_id, host 1, name patch on): 1 is $host_name, $client_id is $guest_name
  In your own window:  a new row in the lobby, named after your guest (the file
    ~/.config/spiremind/guest_name, else "Guest"), and in your log
    coop: names (lobby, this copy is the human's, ...): 1 is <you>, $client_id is <the guest's name>
  In the copy's window: pick a character and press Embark. Press Embark in your own window last.
  A failed join shows the game's "Disconnected" popup with one more line that starts with
    "SpireMind guest:". OK, Back, Join tries again.
What this trial does not show: traffic between two computers (Wi-Fi, the firewall, the Local
  Network permission), a guest whose game runs WITH Steam, and the friend's own mods.
The copy's profile is new, so while it is in your lobby your ascension level drops to 0 and the
  game saves that as your preferred level. Set it back afterwards.
To end: in the copy's window leave through the game's menus first (Back in character select;
  Esc, Disconnect in a run). Only then Cmd+Q there. A copy that is closed while in your lobby
  or run stays as a ghost: back out of the lobby and host again.
EOF
}

if [ "$dry_run" = 1 ]; then
  note "would run: mkdir -p $(printf '%q' "$log_dir")"
  note "would start, in the background and detached from the terminal:" \
    "cd $(printf '%q' "$log_dir") && nohup $launch_line\\" \
    "  > $(printf '%q' "$console_log") 2>&1 < /dev/null &"
  note "would wait up to $TRIAL_WAIT_SECS s for the copy's log to say whether it joined, then print:"
  print_next | sed 's/^/        /'
  if [ "$blockers" = 0 ]; then
    printf '\nDry run done. No STOP above. Nothing was changed or started.\n'
    exit 0
  fi
  printf '\nDry run done. A real run would stop at the first of the %s STOP(s) above. Nothing was changed or started.\n' "$blockers"
  exit 1
fi

mkdir -p "$log_dir"
[ ! -s "$log_file" ] || mv -f "$log_file" "$log_file.prev"
# The copy's working folder: without Steam the game looks for mp_names.json there, nothing else.
# No Steam variable may reach it (none is set in a Terminal; this also covers a shell started by Steam).
(
  cd "$log_dir"
  for variable in $(env | sed -n 's/^\(Steam[A-Za-z0-9_]*\)=.*/\1/p; s/^\(STEAM[A-Za-z0-9_]*\)=.*/\1/p'); do unset "$variable"; done
  unset DYLD_INSERT_LIBRARIES SPIREMIND_PORT
  exec nohup "$GAME_BIN" "${launch_args[@]}" >"$console_log" 2>&1 </dev/null
) &
copy_pid=$!
disown 2>/dev/null || true
ok "Started the guest copy (pid $copy_pid)" "$launch_line" "log: $log_file"

printf '  wait  for it to say whether it joined (up to %s s) ' "$TRIAL_WAIT_SECS"
deadline=$((SECONDS + TRIAL_WAIT_SECS))
state=unknown
while :; do
  for file in "$log_file" "$console_log"; do
    [ -s "$file" ] || continue
    if grep -q "this copy is a guest's" "$file" 2>/dev/null; then state=joined; break 2; fi
    if grep -q 'SpireMind\] guest: the game reports' "$file" 2>/dev/null; then state=failed; break 2; fi
  done
  if ! kill -0 "$copy_pid" 2>/dev/null; then state=gone; break; fi
  [ "$SECONDS" -lt "$deadline" ] || break
  printf '.'
  sleep 1
done
printf '\n'

shown() { # the mod's lines so far, from whichever file has them
  for file in "$log_file" "$console_log"; do
    [ -s "$file" ] || continue
    grep 'SpireMind\] \(role\|guest\|coop: names\)' "$file" 2>/dev/null | cut -c1-260 | sed 's/^/        /' && return 0
  done
  return 0
}

case "$state" in
  joined)
    ok "The guest copy is in your lobby: it dialed $address and got in."
    shown
    ;;
  failed)
    printf '  STOP  The join failed. The copy says:\n'
    shown
    more "Its window shows the same in the game's popup. Fix the cause, then OK, Back, Join there."
    ;;
  gone)
    printf '  STOP  The copy quit. The end of its log:\n'
    for file in "$log_file" "$console_log"; do
      [ ! -s "$file" ] || { tail -n 12 "$file" | sed 's/^/        /'; break; }
    done
    exit 1
    ;;
  unknown)
    note "No answer in the log after $TRIAL_WAIT_SECS s. Look at the copy's window." \
      "No '[SpireMind] role: guest copy' line below means the mod did not load in the copy" \
      "(a popup about loading mods in its window, or 'Skipping loading mod' in its log)."
    shown
    ;;
esac

printf '\n'
print_next
printf '\nThe guest copy is pid %s. Its log: %s\n' "$copy_pid" "$log_file"
