#!/usr/bin/env bash
# Builds everything, runs the xUnit suite for Pipit.Core, then boots the main scene
# headless to catch scene/script load errors. Non-zero exit if anything fails.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
require_tools
build_csharp

echo "==> core unit tests"
core_status=0
dotnet_win test "$TESTS_UNC" -v q -nologo || core_status=$?

# A load error in Godot is logged, not fatal, so the log is checked for errors.
# `timeout` turns a hang into a failure rather than a stuck terminal.
echo "==> headless boot of main scene"
boot_status=0
log="$(timeout 120 "$GODOT" --headless --path "$PROJECT_UNC" --quit-after 30 2>&1)" || boot_status=$?
if grep -E "^(ERROR|SCRIPT ERROR)" <<<"$log"; then boot_status=1; fi
[ "$boot_status" -eq 0 ] || printf '%s\n' "$log" | tail -40

# Smoke run: Main visits every screen (Enter/Exit, audio subscriptions) and prints SMOKE OK.
echo "==> headless smoke run through every screen"
smoke_status=0
log="$(timeout 120 "$GODOT" --headless --path "$PROJECT_UNC" -- --smoke 2>&1)" || smoke_status=$?
if grep -E "^(ERROR|SCRIPT ERROR)" <<<"$log" || ! grep -q "SMOKE OK" <<<"$log"; then smoke_status=1; fi
[ "$smoke_status" -eq 0 ] || printf '%s\n' "$log" | tail -40

ok() { [ "$1" -eq 0 ] && echo ok || echo FAILED; }
echo "==> core tests: $(ok $core_status), boot: $(ok $boot_status), smoke: $(ok $smoke_status)"
[ $core_status -eq 0 ] && [ $boot_status -eq 0 ] && [ $smoke_status -eq 0 ]
