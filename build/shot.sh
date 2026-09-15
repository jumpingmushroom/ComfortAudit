#!/usr/bin/env bash
# Capture the Valheim window from the gaming rig and pull it back here.
#
#   ./build/shot.sh <name>
#
# Captures ONLY the Valheim window, never the whole screen. The rig runs niri on
# a 3840x1080 ultrawide with tiled windows, so "screenshot-screen" grabs the
# entire desktop — browser, chat, terminals and all. This finds the game window
# by App ID, focuses it, and captures just that surface.
#
# X11 grabs are not an option: XWayland's root window returns an all-black frame
# because it cannot see the composited desktop.
set -euo pipefail

NAME="${1:-shot}"
SSH_TARGET="${VALHEIM_SSH:-equ@192.168.1.160}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="$ROOT/docs/images"
mkdir -p "$OUT_DIR"

REMOTE=$(ssh "$SSH_TARGET" 'bash -lc '"'"'
export XDG_RUNTIME_DIR=/run/user/1000
export NIRI_SOCKET=$(ls /run/user/1000/niri.*.sock | head -1)

id=$(niri msg windows 2>/dev/null | awk "
    /^Window ID/ { id=\$3; sub(/:/, \"\", id) }
    /App ID: \"valheim/ { print id; exit }
")
[ -n "$id" ] || { echo "NO_VALHEIM_WINDOW" >&2; exit 1; }

niri msg action focus-window --id "$id" >/dev/null 2>&1
sleep 1

before=$(ls -t ~/Pictures/Screenshots/*.png 2>/dev/null | head -1)
niri msg action screenshot-window >/dev/null 2>&1 || exit 1

# Wait for a genuinely new file, then for its size to settle: encoding a large
# PNG takes well over a second and a fixed sleep silently yields a stale frame.
for i in $(seq 1 40); do
    latest=$(ls -t ~/Pictures/Screenshots/*.png 2>/dev/null | head -1)
    if [ "$latest" != "$before" ] && [ -s "$latest" ]; then
        prev=0
        while true; do
            size=$(stat -c%s "$latest")
            [ "$size" = "$prev" ] && break
            prev=$size; sleep 0.3
        done
        echo "$latest"; exit 0
    fi
    sleep 0.5
done
exit 1
'"'"'')

[ -n "$REMOTE" ] || { echo "capture failed" >&2; exit 1; }

scp -q "$SSH_TARGET:$REMOTE" "$OUT_DIR/$NAME.png"
python3 - "$OUT_DIR/$NAME.png" <<'PY'
import struct, sys
p = sys.argv[1]
d = open(p, "rb").read()
w, h = struct.unpack(">II", d[16:24])
print(f"==> {p}\n    {w}x{h}, {len(d)} bytes")
PY
