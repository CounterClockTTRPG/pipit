#!/usr/bin/env bash
# Downloads the small Vosk speech models (Apache 2.0, alphacephei.com/vosk/models) used to
# hear note names in Note reading: English, Dutch and French, ~40 MB each, so not kept in git.
# Speech works on Windows (and Linux/macOS); on Android the note names are tapped.
set -euo pipefail
dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/assets/speech"
mkdir -p "$dir"
declare -A models=([en]=vosk-model-small-en-us-0.15 [nl]=vosk-model-small-nl-0.22 [fr]=vosk-model-small-fr-0.22)
for lang in "${!models[@]}"; do
    [ -f "$dir/$lang/am/final.mdl" ] && continue
    name=${models[$lang]}
    tmp="$(mktemp -d)"
    curl -fL --progress-bar -o "$tmp/$name.zip" "https://alphacephei.com/vosk/models/$name.zip"
    python3 -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])" "$tmp/$name.zip" "$tmp"
    rm -rf "$dir/$lang"
    mv "$tmp/$name" "$dir/$lang"
    rm -rf "$tmp"
done
du -sh "$dir"/*
