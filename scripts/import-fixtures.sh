#!/usr/bin/env bash
# Copies guided takes recorded in Pipit's Studio into tests/fixtures/audio, where the xUnit
# FixtureTests check that every expected note is recognised.
#
#   scripts/import-fixtures.sh            # all guided takes not imported yet
#   scripts/import-fixtures.sh '*tune*'   # only takes whose name matches the pattern
#   scripts/import-fixtures.sh --list     # show what is available
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

appdata="$(wslpath "$(cmd.exe /c 'echo %APPDATA%' 2>/dev/null | tr -d '\r')")"
src="$appdata/Godot/app_userdata/Pipit/recordings"
dst="$PROJECT_DIR/tests/fixtures/audio"
mkdir -p "$dst"
[ -d "$src" ] || { echo "No recordings folder at $src" >&2; exit 1; }

pattern="${1:-*}"
list=false
[ "$pattern" = "--list" ] && { list=true; pattern="*"; }

# Takes listed in rejected.txt (first word of each non-comment line) are never imported.
rejected="$(grep -v '^#' "$dst/rejected.txt" 2>/dev/null | awk '{print $1}')"

count=0
for json in "$src"/$pattern.json; do
    [ -e "$json" ] || continue
    name="$(basename "$json" .json)"
    # Only guided takes carry ground truth.
    grep -q '"session": "' "$json" || continue
    if grep -qx "$name" <<<"$rejected"; then
        $list && echo "$name  (rejected, see rejected.txt)"
        continue
    fi
    if $list; then
        echo "$name$([ -e "$dst/$name.json" ] && echo '  (imported)')"
        continue
    fi
    [ -e "$dst/$name.json" ] && continue
    cp "$json" "$src/$name.wav" "$dst/"
    echo "imported $name"
    count=$((count + 1))
done
$list || echo "$count take(s) imported into tests/fixtures/audio"
