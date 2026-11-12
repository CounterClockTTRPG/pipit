# Licences for Pipit's content

Pipit is free to share and adapt, as long as you credit it. In practice:

| What | Licence |
|---|---|
| Source code (`src/`, `tests/`, `scripts/`, `tools/`, `scenes/`) | MIT (`LICENSE`) |
| Pipit's own content: lessons (`data/lessons`), the learning path, fingering charts, translations (`data/i18n`), song arrangements written for Pipit, documentation (`docs/`), the icon and UI images | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |
| Third-party material listed below | its own licence |

## Attribution

When you share or adapt Pipit's content, credit it like this, or in a similar way:

> Based on **Pipit** by Raf Janssens, https://github.com/CounterClockTTRPG/pipit, licensed CC BY 4.0.

Say whether you changed anything. For the code, keep the copyright notice in `LICENSE`.

## Third-party material (not covered by the licences above)

- **Fonts**: Bravura (Steinberg) and Andika (SIL International), SIL Open Font License 1.1; see
  `assets/fonts/*-OFL.txt`.
- **SoundFont**: GeneralUser GS by S. Christian Collins, downloaded by
  `scripts/fetch-soundfont.sh`, under its own licence (`assets/soundfont/GeneralUser-GS-LICENSE.txt`).
- **Speech models**: Vosk small models (Apache 2.0), downloaded by `scripts/fetch-speech-models.sh`.
- **Songs** keep the licence of the edition they came from, which each song file names in its
  `source` field:
  - Public Domain editions (Mutopia Project and others), and traditional tunes arranged for
    Pipit: CC BY 4.0 as Pipit content.
  - **CC BY-SA 3.0** (Mutopia Project editions). Adaptations must stay CC BY-SA: `handel-hwv360-1`, `handel-hwv360-2`, `handel-hwv360-3`, `handel-hwv360-4`, `handel-hwv365-1`, `handel-hwv365-2`, `handel-hwv365-3`, `handel-hwv365-4`, `handel-hwv365-5`, `handel-hwv369-1`, `handel-hwv369-2`, `handel-hwv369-3`, `handel-hwv369-4`, `marcello-ciaccona`.
  - **CC BY-NC-SA** (Paul Hardy's tunebooks, pghardy.net). These are **non-commercial only**,
    and adaptations must stay CC BY-NC-SA: `ash-grove`, `barbara-allen`, `black-nag`, `bobby-shaftoe`, `branle-des-chevaux`, `childgrove`, `dargason`, `drunken-sailor`, `grimstock`, `hole-in-the-wall`, `jenny-pluck-pears`, `keel-row`, `la-mourisque`, `minstrel-boy`, `molly-malone`, `newcastle`, `nonesuch`, `old-mole`, `packingtons-pound`, `parsons-farewell`, `parting-glass`, `pastime-with-good-company`, `portsmouth`, `quatre-branle`, `rufty-tufty`, `scarborough-fair`, `simple-gifts`, `skye-boat-song`, `sumer-is-icumen-in`, `water-is-wide`, `well-hall`.

  If you want to use Pipit commercially, remove the CC BY-NC-SA songs (and their entries in
  `data/songs/index.json`) first.
