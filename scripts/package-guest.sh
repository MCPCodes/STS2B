#!/usr/bin/env bash
# Build the zip a friend needs to join your co-op lobby from their own Mac. See docs/coop-guest.md.
#   scripts/package-guest.sh [options]
# Options:
#   --address A       your Mac's address on the network you will share (four numbers with dots).
#                     With it the friend's launch line is complete. Without it the line holds the
#                     word HOSTS-ADDRESS, which the friend replaces with the address you tell them.
#   --guest-name N    what the friend's own game calls them (default Guest)
#   --host-name N     what the friend's game calls you (default Host)
#   --out DIR         where the zip goes (default out/guest)
#   --no-build        package out/SpireMind as it is; do not build first
# The zip holds one folder, SpireMind-guest, with:
#   README.txt                  the friend's instructions
#   SpireMind/SpireMind.dll     the mod, the same build you install
#   SpireMind/SpireMind.json    its manifest
#   SpireMind/guest-only.txt    a marker: with it the mod is a guest or does nothing at all
# Nothing of this Mac goes in: no brain folder, no notes, no key, no config, no logs, no paths.
# The script checks that before it zips. It installs nothing and starts nothing.
set -euo pipefail
. "$(dirname "$0")/env.sh"

address=""
guest_name="Guest"
host_name="Host"
out_dir="$REPO_ROOT/out/guest"
build=1

usage() { sed -n '2,/^set -euo pipefail$/p' "$0" | sed -e '$d' -e 's/^# \{0,1\}//'; }
die() { printf 'package-guest: %s\n' "$*" >&2; exit 2; }

while [ $# -gt 0 ]; do
  case "$1" in
    --address) [ $# -ge 2 ] || die "--address needs an address"; address="$2"; shift 2 ;;
    --address=*) address="${1#*=}"; shift ;;
    --guest-name) [ $# -ge 2 ] || die "--guest-name needs a name"; guest_name="$2"; shift 2 ;;
    --guest-name=*) guest_name="${1#*=}"; shift ;;
    --host-name) [ $# -ge 2 ] || die "--host-name needs a name"; host_name="$2"; shift 2 ;;
    --host-name=*) host_name="${1#*=}"; shift ;;
    --out) [ $# -ge 2 ] || die "--out needs a folder"; out_dir="$2"; shift 2 ;;
    --out=*) out_dir="${1#*=}"; shift ;;
    --no-build) build=0; shift ;;
    -h | --help) usage; exit 0 ;;
    *) die "unknown option $1 (--help lists them)" ;;
  esac
done

# Names go into one pasted line of launch options, so no spaces and nothing a shell would read.
re_name='^[A-Za-z0-9._-]{1,24}$'
[[ $guest_name =~ $re_name ]] || die "--guest-name must be 1 to 24 letters, digits, dots, dashes or underscores, not '$guest_name'"
[[ $host_name =~ $re_name ]] || die "--host-name must be 1 to 24 letters, digits, dots, dashes or underscores, not '$host_name'"

# The same rule as the mod's own (GuestArgs.TryAddress): four numbers from 0 to 255, no leading zeros.
if [ -n "$address" ]; then
  re_addr='^([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})$'
  [[ $address =~ $re_addr ]] || die "--address must be four numbers with dots, like 192.168.1.23, not '$address'"
  for part in "${BASH_REMATCH[@]:1}"; do
    { [ "$part" -le 255 ] && { [ "${#part}" = 1 ] || [ "${part:0:1}" != 0 ]; }; } || die "--address: '$part' is not a number from 0 to 255 without leading zeros"
  done
  first="${BASH_REMATCH[1]}"
  { [ "$first" -ge 1 ] && [ "$first" -le 223 ]; } || die "--address: $address is not the address of a computer"
  case "$address" in 127.*) die "--address: $address is this Mac itself; the friend needs your address on the network (ipconfig getifaddr en0)" ;; esac
fi

PYTHON="${PYTHON:-$(command -v python3 || true)}"
[ -n "$PYTHON" ] || die "python3 is needed to read the mods' version files"
command -v zip >/dev/null || die "zip is needed"

[ "$build" = 0 ] || "$(dirname "$0")/build.sh"
staged="$REPO_ROOT/out/SpireMind"
[ -f "$staged/SpireMind.dll" ] && [ -f "$staged/SpireMind.json" ] || die "no build in $staged; run without --no-build"

# What the friend must match: the game's version, and the mods YOUR game loads. Your game loads
# the copies in its own mods folder when a mod is there and on the Workshop both.
version_of() { # <json file> -> its "version" text, or nothing
  [ -f "$1" ] || return 0
  "$PYTHON" -I -c 'import json, sys
try:
    with open(sys.argv[1], encoding="utf-8-sig") as f:
        value = json.load(f).get("version")
    print(value if isinstance(value, str) else "")
except Exception:
    print("")' "$1"
}
game_version="$(version_of "$GAME_APP/Contents/Resources/release_info.json")"
betterspire="$(version_of "$MODS_DIR/BetterSpire2/BetterSpire2.json")"
baselib="$(version_of "$MODS_DIR/BaseLib/BaseLib.json")"
[ -n "$game_version" ] || { game_version="the same as the host's"; echo "package-guest: WARN: could not read the game's version at $GAME_APP" >&2; }
[ -n "$betterspire" ] || { betterspire="the same as the host's"; echo "package-guest: WARN: no BetterSpire2 in $MODS_DIR; the README will not name its version" >&2; }
[ -n "$baselib" ] || { baselib="the same as the host's"; echo "package-guest: WARN: no BaseLib in $MODS_DIR; the README will not name its version" >&2; }

if [ -n "$address" ]; then
  host_value="$address"
  ping_address="$address"
  address_note="The numbers after --spiremind-host= are $host_name's address on the network you share today.
If $host_name gives you another address, change only those numbers."
else
  host_value="HOSTS-ADDRESS"
  ping_address="(the address from your Launch Options)"
  address_note="Then replace HOSTS-ADDRESS with the address $host_name gives you: four numbers
with dots, like 192.168.1.23. No spaces around it."
fi
launch_line="--fastmp=join --clientId=2000 --spiremind-host=$host_value --spiremind-name=$guest_name --spiremind-host-name=$host_name"

work="$(mktemp -d "${TMPDIR:-/tmp}/spiremind-guest.XXXXXX")"
trap 'rm -rf "$work"' EXIT
root="$work/SpireMind-guest"
mkdir -p "$root/SpireMind"
cp "$staged/SpireMind.dll" "$staged/SpireMind.json" "$root/SpireMind/"
cat >"$root/SpireMind/guest-only.txt" <<'EOF'
This file marks the SpireMind package made for a guest. Leave it next to SpireMind.dll.

With it the mod does one thing only: it lets this game join a host's co-op lobby when the
launch options ask for that (--spiremind-host=<address>). Everything else in the mod stays
switched off on this computer, whatever the launch options say. Without those launch options
the mod does nothing at all.
EOF

# The README, with this session's values. Python fills it in, so no value is read by a shell or by sed.
GAME_VERSION="$game_version" BETTERSPIRE="$betterspire" BASELIB="$baselib" HOST="$host_name" GUEST="$guest_name" \
  LAUNCH_LINE="$launch_line" ADDRESS_NOTE="$address_note" PING_ADDRESS="$ping_address" \
  "$PYTHON" -I -c 'import os, re, sys
text = open(sys.argv[1], encoding="utf-8").read()
def fill(match):
    return os.environ[match.group(1)]
text = re.sub(r"@([A-Z_]+)@", fill, text)
open(sys.argv[2], "w", encoding="utf-8", newline="\n").write(text)' "$REPO_ROOT/scripts/guest/README.txt" "$root/README.txt"

# Checks before zipping: exactly these files, the guest code is in the DLL, and nothing of this
# Mac is in any of them (as plain text or as the DLL's two-byte text).
expected="README.txt
SpireMind/SpireMind.dll
SpireMind/SpireMind.json
SpireMind/guest-only.txt"
actual="$(cd "$root" && find . -type f | sed 's|^\./||' | LC_ALL=C sort)"
[ "$actual" = "$expected" ] || die "the package would hold other files than it should: $actual"
CHECK_HOME="$HOME" CHECK_REPO="$REPO_ROOT" CHECK_USER="$(id -un)" "$PYTHON" -I -c 'import os, sys
root = sys.argv[1]
needles = {os.environ["CHECK_HOME"], os.environ["CHECK_REPO"], "/Users/", "/private/tmp", "/home/" + os.environ["CHECK_USER"], "sk-ant-"}
needles = {n for n in needles if len(n) >= 6}
bad = []
for folder, _, files in os.walk(root):
    for name in files:
        path = os.path.join(folder, name)
        data = open(path, "rb").read()
        for needle in needles:
            if needle.encode() in data or needle.encode("utf-16-le") in data:
                bad.append("%s holds %r" % (os.path.relpath(path, root), needle))
dll = open(os.path.join(root, "SpireMind", "SpireMind.dll"), "rb").read()
if "spiremind-host".encode("utf-16-le") not in dll:
    bad.append("SpireMind.dll has no guest mode in it (an old build?)")
if b"@" in open(os.path.join(root, "README.txt"), "rb").read():
    bad.append("README.txt still holds an @ placeholder")
for line in bad:
    print("package-guest: " + line, file=sys.stderr)
sys.exit(1 if bad else 0)' "$root" || die "the package was not written"

mkdir -p "$out_dir"
zip_path="$(cd "$out_dir" && pwd)/SpireMind-guest.zip"
rm -f "$zip_path"
# -X: no extra file attributes; COPYFILE_DISABLE: no ._ files from this Mac.
(cd "$work" && COPYFILE_DISABLE=1 zip -q -r -X "$zip_path" SpireMind-guest -x '*.DS_Store')
cp "$root/README.txt" "$(dirname "$zip_path")/README.txt"

echo "Wrote $zip_path"
unzip -l "$zip_path" | sed 's/^/  /'
echo "  sha256 $(shasum -a 256 "$zip_path" | cut -d' ' -f1)"
echo "The friend's instructions are also beside it: $(dirname "$zip_path")/README.txt"
echo "Their launch line: $launch_line"
[ -n "$address" ] || echo "No --address given: tell the friend your address (ipconfig getifaddr en0), or run this again with --address."
