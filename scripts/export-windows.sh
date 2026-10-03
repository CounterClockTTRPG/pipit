#!/usr/bin/env bash
# Exports a release build for Windows into build/windows/ (Pipit.exe, Pipit.pck and the .NET
# assemblies) and smoke-tests it headless. Needs the Godot 4.7.2 mono export templates and the
# SoundFont (scripts/fetch-soundfont.sh), which is packed into the build.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
require_tools
[ -f "$PROJECT_DIR/assets/soundfont/GeneralUser-GS.sf2" ] || { echo "SoundFont missing: run scripts/fetch-soundfont.sh" >&2; exit 1; }
build_csharp
rm -rf "$PROJECT_DIR/build/windows"
mkdir -p "$PROJECT_DIR/build/windows"
echo "==> exporting"
"$GODOT" --headless --path "$PROJECT_UNC" --export-release "Windows Desktop" "$(to_unc "$PROJECT_DIR/build/windows/Pipit.exe")" 2>&1 | grep -E "ERROR|WARNING|DONE" | tail -20
ls -la "$PROJECT_DIR/build/windows"
chmod +x "$PROJECT_DIR"/build/windows/*.exe
echo "==> smoke run of the exported build"
log="$(timeout 120 "$PROJECT_DIR/build/windows/Pipit.console.exe" --headless -- --smoke 2>&1)" || true
grep -E "^(ERROR|SCRIPT ERROR)" <<<"$log" | head -20 || true
grep -q "SMOKE OK" <<<"$log" && echo "export: ok" || { printf '%s\n' "$log" | tail -30; echo "export: FAILED"; exit 1; }
# A zip to hand out: unzip anywhere and run Pipit.exe (the console wrapper is left out).
python3 - "$PROJECT_DIR/build" <<'PY'
import os, sys, zipfile
build = sys.argv[1]; src = os.path.join(build, 'windows')
with zipfile.ZipFile(os.path.join(build, 'Pipit-windows.zip'), 'w', zipfile.ZIP_DEFLATED) as z:
    for root, _, files in os.walk(src):
        for f in files:
            if f != 'Pipit.console.exe':
                path = os.path.join(root, f)
                z.write(path, os.path.join('Pipit', os.path.relpath(path, src)))
PY
echo "==> build/Pipit-windows.zip ($(du -h "$PROJECT_DIR/build/Pipit-windows.zip" | cut -f1))"
