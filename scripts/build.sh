#!/usr/bin/env bash
# Builds the Godot assembly (which builds Pipit.Core through its project reference).
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
require_tools
build_csharp
