#!/usr/bin/env bash
# Builds and launches Pipit in a window. `--editor` opens the Godot editor instead.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
require_tools
build_csharp
if [ "${1:-}" = "--editor" ]; then
    "$GODOT_GUI" --editor --path "$PROJECT_UNC" &
else
    "$GODOT_GUI" --path "$PROJECT_UNC" &
fi
