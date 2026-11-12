# Credits

## Fonts
- **Bravura** (`assets/fonts/Bravura.otf`), the SMuFL reference music font by Steinberg Media
  Technologies GmbH, under the SIL Open Font License 1.1 (`assets/fonts/Bravura-OFL.txt`).
  Glyph metrics in `src/App/Views/StaffView.cs` come from its `Bravura.json` metadata.
- **Andika** (`assets/fonts/Andika-*.ttf`), by SIL International, designed for beginning readers,
  under the SIL Open Font License 1.1 (`assets/fonts/Andika-OFL.txt`). The interface typeface.

## Sound
- **GeneralUser GS** SoundFont by S. Christian Collins, for accompaniment and previews, under
  its own licence (`assets/soundfont/GeneralUser-GS-LICENSE.txt`). Fetched by
  `scripts/fetch-soundfont.sh`, not kept in git.
- **Vosk** small speech models (Apache 2.0, alphacephei.com) for spoken note names, fetched by
  `scripts/fetch-speech-models.sh`.

## Music
Each song file names its source and licence in its `source` field.
- **Mutopia Project** editions (https://www.mutopiaproject.org/): most are in the public domain;
  the Handel sonatas and Marcello's Ciaccona are CC BY-SA 3.0.
- **Paul Hardy's tunebooks** (https://www.pghardy.net/), ABC under CC BY-NC-SA: the Playford
  dances, Renaissance dances and several folk songs. Non-commercial use only.
- ABC transcriptions from trillian.mit.edu and alijc.github.io for a few children's and folk songs.
- Folk songs and the chapter songs are traditional or long out of copyright; the arrangements
  in `data/songs` were written for Pipit.

See `../LICENSE-CONTENT.md` for how these licences apply.
