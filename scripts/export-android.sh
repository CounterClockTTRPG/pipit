#!/usr/bin/env bash
# Exports an Android APK (arm64, landscape, microphone permission) into build/android/Pipit.apk.
# It is signed with Godot's debug keystore (from the editor settings), which is fine for
# installing on your own tablets; a store release would need its own key. With a tablet
# connected over USB (developer mode, USB debugging on), --install also installs it with adb.
#
#   scripts/export-android.sh [--install]
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
require_tools
[ -f "$PROJECT_DIR/assets/soundfont/GeneralUser-GS.sf2" ] || { echo "SoundFont missing: run scripts/fetch-soundfont.sh" >&2; exit 1; }
WIN_HOME="$(wslpath "$(cmd.exe /c 'echo %USERPROFILE%' 2>/dev/null | tr -d '\r')")"
KEYSTORE="${ANDROID_KEYSTORE:-$WIN_HOME/AppData/Roaming/Godot/keystores/debug.keystore}"
[ -f "$KEYSTORE" ] || { echo "No keystore at $KEYSTORE (set ANDROID_KEYSTORE)" >&2; exit 1; }
export GODOT_ANDROID_KEYSTORE_RELEASE_PATH="$(wslpath -w "$KEYSTORE")"
export GODOT_ANDROID_KEYSTORE_RELEASE_USER="${ANDROID_KEYSTORE_USER:-androiddebugkey}"
export GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD="${ANDROID_KEYSTORE_PASSWORD:-android}"
# Windows programs only see variables listed in WSLENV.
export WSLENV="${WSLENV:+$WSLENV:}GODOT_ANDROID_KEYSTORE_RELEASE_PATH:GODOT_ANDROID_KEYSTORE_RELEASE_USER:GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD"
# The Android export runs MSBuild on the project path, and MSBuild takes a //wsl.localhost/...
# path for a command-line switch. So the export works on a copy on the Windows drive.
STAGE="$WIN_HOME/AppData/Local/Temp/pipit-android-build"
echo "==> copying the project to $STAGE"
mkdir -p "$STAGE"
rsync -a --delete --exclude .git --exclude build --exclude bin --exclude obj --exclude tests --exclude tools --exclude docs \
    "$PROJECT_DIR/" "$STAGE/"
STAGE_WIN="$(wslpath -w "$STAGE")"
rm -rf "$PROJECT_DIR/build/android"
mkdir -p "$PROJECT_DIR/build/android"
echo "==> exporting"
"$GODOT" --headless --path "$STAGE_WIN" --export-release "Android" "$STAGE_WIN\\Pipit.apk" 2>&1 | grep -E "ERROR|WARNING|DONE|rror" | tail -30
[ -f "$STAGE/Pipit.apk" ] || { echo "export: FAILED (no APK)"; exit 1; }
cp "$STAGE/Pipit.apk" "$PROJECT_DIR/build/android/Pipit.apk"
echo "==> build/android/Pipit.apk ($(du -h "$PROJECT_DIR/build/android/Pipit.apk" | cut -f1))"
if [ "${1:-}" = "--install" ]; then
    ADB="$WIN_HOME/AppData/Local/Android/Sdk/platform-tools/adb.exe"
    "$ADB" install -r "$(wslpath -w "$PROJECT_DIR/build/android/Pipit.apk")"
fi
