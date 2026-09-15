#!/usr/bin/env bash
# Build ComfortAudit and copy it into the r2modman profile on the target machine.
#
#   ./build/deploy.sh                 # build + deploy to the default target
#   VALHEIM_SSH=user@host ./build/deploy.sh
#   ./build/deploy.sh --local /path/to/profile   # deploy to a local profile instead
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/src/ComfortAudit/ComfortAudit.csproj"
OUT="$ROOT/src/ComfortAudit/bin/Release/net472/ComfortAudit.dll"

SSH_TARGET="${VALHEIM_SSH:-equ@192.168.1.160}"
PROFILE="${VALHEIM_PROFILE:-/home/equ/.config/r2modmanPlus-local/Valheim/profiles/Mods}"
PLUGIN_DIR="$PROFILE/BepInEx/plugins/ComfortAudit"

echo "==> building"
dotnet build "$PROJ" -c Release --nologo -v minimal

[ -f "$OUT" ] || { echo "build produced no DLL at $OUT" >&2; exit 1; }

if [ "${1:-}" = "--local" ]; then
    DEST="${2:?--local needs a profile path}/BepInEx/plugins/ComfortAudit"
    echo "==> deploying to $DEST"
    mkdir -p "$DEST"
    cp "$OUT" "$DEST/"
else
    echo "==> deploying to $SSH_TARGET:$PLUGIN_DIR"
    ssh "$SSH_TARGET" "mkdir -p '$PLUGIN_DIR'"
    scp -q "$OUT" "$SSH_TARGET:$PLUGIN_DIR/"
fi

echo "==> done: $(basename "$OUT") $(stat -c%s "$OUT") bytes"
