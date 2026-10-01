#!/usr/bin/env bash
# Downloads GeneralUser GS (S. Christian Collins), the General MIDI SoundFont used for the
# natural recorder voice and accompaniment. ~31 MB, so it is not kept in git. Its licence
# allows use in software projects; see assets/soundfont/GeneralUser-GS-LICENSE.txt.
set -euo pipefail
dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/assets/soundfont"
mkdir -p "$dir"
base=https://raw.githubusercontent.com/mrbumpy409/GeneralUser-GS/main
[ -s "$dir/GeneralUser-GS.sf2" ] || curl -fL --progress-bar -o "$dir/GeneralUser-GS.sf2" "$base/GeneralUser-GS.sf2"
curl -fsL -o "$dir/GeneralUser-GS-LICENSE.txt" "$base/documentation/LICENSE.txt"
ls -la "$dir"
