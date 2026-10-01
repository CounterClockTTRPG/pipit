#!/usr/bin/env bash
# Shared setup for the helper scripts.
#
# Godot and the .NET SDK are Windows executables, so the project is handed to them
# as a UNC path into the WSL filesystem. Everything else stays on the Linux side.

GODOT_DIR="${GODOT_DIR:-/mnt/c/data/tools/Godot_v4.7.2-stable_mono_win64}"
GODOT="${GODOT:-$GODOT_DIR/Godot_v4.7.2-stable_mono_win64_console.exe}"
GODOT_GUI="${GODOT_GUI:-$GODOT_DIR/Godot_v4.7.2-stable_mono_win64.exe}"
DOTNET="${DOTNET:-/mnt/c/Program Files/dotnet/dotnet.exe}"

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
to_unc() { printf '\\\\wsl.localhost\\%s%s' "$WSL_DISTRO_NAME" "$(printf '%s' "$1" | tr '/' '\\')"; }
PROJECT_UNC="$(to_unc "$PROJECT_DIR")"
CSPROJ_UNC="${PROJECT_UNC}\\Pipit.csproj"
TESTS_UNC="${PROJECT_UNC}\\tests\\Pipit.Core.Tests\\Pipit.Core.Tests.csproj"

require_tools() {
    [ -x "$GODOT" ] || { echo "Godot not found at: $GODOT" >&2; exit 127; }
    [ -x "$DOTNET" ] || { echo "dotnet not found at: $DOTNET" >&2; exit 127; }
}

# MSBuild dislikes a UNC working directory, so every dotnet call runs from C:.
dotnet_win() { (cd /mnt/c && "$DOTNET" "$@"); }

build_csharp() {
    echo "==> building C# (Pipit + Pipit.Core)"
    dotnet_win build "$CSPROJ_UNC" -v q -nologo
}
