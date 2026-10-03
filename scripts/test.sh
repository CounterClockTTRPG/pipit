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

# Lesson probe: plays each "Play this note" target of a lesson into the app as a sharp,
# wobbling note (no microphone) and checks every box completes.
echo "==> lesson probe"
probe_status=0
for lesson in si-and-la high-do pinched-thumb; do
    log="$(timeout 120 "$GODOT" --headless --path "$PROJECT_UNC" -- --lessonprobe=$lesson 2>&1)" || probe_status=1
    grep -E "PROBE" <<<"$log" | tail -1
    grep -q "PROBE OK" <<<"$log" || probe_status=1
done

# Speech probe (when the English model is there): a recorded "do re mi fa sol" must come back
# as those notes, in order, with times.
speech_status=0
if [ -f "$PROJECT_DIR/assets/speech/en/am/final.mdl" ] && [ -f "$PROJECT_DIR/tests/fixtures/speech/solfege-en.wav" ]; then
    echo "==> speech probe"
    log="$(timeout 300 "$GODOT" --headless --path "$PROJECT_UNC" -- "--speechprobe=$(to_unc "$PROJECT_DIR/tests/fixtures/speech/solfege-en.wav")" 2>&1)" || speech_status=1
    grep -E "PROBE" <<<"$log"
    grep -q "PROBE letters: 0 1 2 3 4" <<<"$log" || speech_status=1
fi

ok() { [ "$1" -eq 0 ] && echo ok || echo FAILED; }
echo "==> core tests: $(ok $core_status), boot: $(ok $boot_status), smoke: $(ok $smoke_status), lesson probe: $(ok $probe_status), speech probe: $(ok $speech_status)"
[ $core_status -eq 0 ] && [ $boot_status -eq 0 ] && [ $smoke_status -eq 0 ] && [ $probe_status -eq 0 ] && [ $speech_status -eq 0 ]
