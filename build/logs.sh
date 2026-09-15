#!/usr/bin/env bash
# Tail the BepInEx log on the target, filtered to ComfortAudit unless -a is passed.
set -euo pipefail
SSH_TARGET="${VALHEIM_SSH:-equ@192.168.1.160}"
PROFILE="${VALHEIM_PROFILE:-/home/equ/.config/r2modmanPlus-local/Valheim/profiles/Mods}"
LOG="$PROFILE/BepInEx/LogOutput.log"

if [ "${1:-}" = "-a" ]; then
    ssh "$SSH_TARGET" "tail -f '$LOG'"
else
    ssh "$SSH_TARGET" "grep -iE 'comfortaudit|error|exception' '$LOG' | tail -60"
fi
