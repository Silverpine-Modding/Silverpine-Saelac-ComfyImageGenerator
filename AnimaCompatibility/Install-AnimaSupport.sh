#!/bin/sh
set -eu
ANIMA_INSTALL_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if command -v python3 >/dev/null 2>&1; then
    exec python3 "$ANIMA_INSTALL_DIR/install.py" "$@"
elif command -v python >/dev/null 2>&1; then
    exec python "$ANIMA_INSTALL_DIR/install.py" "$@"
else
    printf '%s\n' 'Python was not found. Run install.py with the Python executable used by ComfyUI.' >&2
    exit 1
fi
