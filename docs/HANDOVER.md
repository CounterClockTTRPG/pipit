# Pipit — handover / status (2026-10-02)

Read `CLAUDE.md` (how to build, test, conventions) and `docs/DESIGN.md` (decisions, §17 for the
requests from play-testing) first. This file says where things stand and what comes next.

## Run it

```bash
scripts/run.sh                 # build and start (--editor for the Godot editor)
scripts/test.sh                # build, xUnit (~4 min), headless boot + smoke
scripts/fetch-soundfont.sh     # GeneralUser GS SoundFont (31 MB, not in git) for the natural voice / band
scripts/export-windows.sh      # release build in build/windows + build/Pipit-windows.zip, smoke-tested
scripts/export-android.sh      # build/android/Pipit.apk; --install puts it on a USB-connected tablet (adb)
scripts/import-fixtures.sh     # guided Studio takes -> tests/fixtures/audio (rejected.txt excludes bad ones)
"$GODOT" --path "$PROJECT_UNC" -- --shots   # screenshots of every screen into user://shots (opens a window)
```

## Done

| Area | State |
|---|---|
| M0 skeleton, WSL→Windows build, tests | done |
| M1 listening: mic picker, voice-processing check, calibration (gate, latency, tuning), tuner, fingering trainer, Studio with guided takes | done; real-take fixtures in tests/fixtures |
| Recorders: several per player, soprano/alto/tenor, **baroque and German fingering** (per recorder, Settings or Calibrate), pitch of A | done |
| M2 songs: format/parser, segmenter, grader, Rhythm and Casual modes, speed dial, listen, replay, analysis, XP, levels, streaks, 18 achievements | done |
| M3 score view (Bravura), Bars / Score / **Highway** views | done |
| Study path: 10 chapters, 25 lessons EN/NL with try/exercise/quiz blocks: up to the pinched thumb, sharps (G), Si♭ (F), sixteenths, 6/8, baroque flats, Re'', and the alto | done; chapter songs only use notes taught so far (tests) |
| Pip mascot (vector rig, 6 moods), see `docs/pip.md` | done |
| Survival mode, per-song top-10 leaderboard | done |
| Repertoire: folk, baroque (Mutopia, PD / CC BY-SA), Renaissance dances (Bellugi, **personal use only**, `data/songs/personal/`); Handel sonatas HWV 360/365/369 with continuo, Telemann duet TWV 40:107 (two parts), Japart *Tmeiskin* (consort), Brahms' Lullaby (`tools/import-songs.py`); personal: 21 van Eyck pieces (MusicXML via `tools/musicxml2pipit.py`), Dowland *Flow my tears* SATB, Encina, Lasso, Ravenscroft, *Procurans odium*, Handel recorder concerto | 95 songs |

## Players (M4), done 2026-10-02

The app starts on "Who's playing?": players have a name, an avatar bird (Pip's rig in six
plumages) and their own settings (`AppSettings`: language, names, recorders, voice, view, band)
and progress (`PlayerProgress`, in `user://profiles/<id>/`). The computer's settings
(microphone, calibration, full screen, headphones) are `MachineSettings` in `machine.json`.
New players get Pip's tour of the app (`IntroScreen`, EN/NL). The single-player files were
migrated to "Player 1" (`ProfileStore.MigrateLegacy`, tested; originals kept as *.migrated).
`--smoke`/`--shots` start at Home as the last player.

## M7 natural sound and accompaniment (done, apart from speaker mode)

Committed (Core, tested): `SoundFontRenderer` (MeltySynth), accompaniment parts and rounds in
the song format (`accompaniment`, `round`, `Song.WithPartFirst`, `Song.EndBeat`).

Wired in since the first handover: part picker, Band switch (headphones confirmed once per
computer), ensemble Listen, Voice setting, bass lines and the Frère Jacques round, the
repertoire's accompaniment from the source MIDIs (`tools/add-accompaniment.py`). Earlier notes:
- `src/App/Audio/Synth.cs`: loads the SoundFont in the background, renders a song's parts
  (player's part at the player's pitch, others at their own) or falls back to the simple voice.
- `ClickPlayer.ScheduleSound(sound, captureTime)`: mixes any sound (the band) into the click
  generator on the capture clock, so it stays in sync with grading.
- `AppSettings.Voice` ("simple"/"natural"), `HeadphonesConfirmed`, `BandOn`.

Next steps, in order:
1. **Play screen**: "Your part" picker when `Parts.Count > 1` (rebuild the screen with
   `song.WithPartFirst(i)`); "Band" checkbox when `HasOtherParts`, asking once to confirm
   headphones; on Start in Rhythm/Survival render the other parts + accompaniment
   (`Synth.RenderAsync`, show "Preparing the band…"), schedule at `BeatToTime(firstBeat)`,
   and run until `song.EndBeat` (rounds finish after the player's part). No band in Casual.
   "Listen" renders the whole ensemble when the band is on, with `Settings.Voice`.
2. **Settings**: Voice Simple/Natural, "I use headphones"; hint to run fetch-soundfont.sh if missing.
3. **Lessons**: `::play` blocks use `Synth` with the voice setting.
4. **Content**: bass lines for the chapter songs (harmonies sketched: G-D for the G songs,
   C-F-G for Twinkle, Am-G-E for Greensleeves), `round` for Frère Jacques (4 entries, 2 bars),
   left hands of the Mutopia minuets (`midi2pipit.py` needs a `--bottom` option for chords),
   and the other consort voices of the Bellugi dances (convert every track from the same
   start bar: the player's track had its leading rest bars stripped).
5. Speaker mode (high-pass + gating of the app's own pitches) only if headphones turn out
   not to be enough; the user said headphones are fine.

## Roadmap after M7

| Item | Notes |
|---|---|
| ~~I18N: EN/NL (2026-10-02), FR interface (2026-10-03)~~; FR lessons and song texts still to translate (they show in English) | `data/i18n/nl.json` (English text → Dutch), loaded into TranslationServer by `Services.ApplyLanguage` with the player's locale and culture. `tools/extract-strings.py --update` adds new texts; `LocalisationTests` fails on any untranslated one. `-- --shots --lang=nl` writes `shots/untranslated.txt` (mostly data text, which is localised in the song/lesson files themselves). Original plan: | All UI text already goes through `Ui.T(...)` (Godot `Tr`) with English as the key, and lessons/songs already have EN/NL. Remaining: a CSV translation table (`data/i18n/strings.csv`, keys = English text), loading it into `TranslationServer`, and setting the locale from `Settings.Language`. Strings built with `string.Format` keep their `{0}` placeholders in translations. A test should list `Ui.T("...")` literals in `src/App` and fail when one has no NL entry. |
| ~~Baroque vs German fingering~~ | Done 2026-10-03: per-recorder setting; German chart checked against Mollenhauer's German soprano chart (adds the upper Sol♯); lesson "Baroque or German?" (chapter 4) with a live `::compare` block that measures Fa with both fingerings. Still possible: show alternative fingerings in the trainer. |
| Player profiles (M4) | done; leaderboard entries carry the player name. A shared board across players on this computer would be a nice next step. |
| Android APK for tablets | Builds since 2026-10-03: `scripts/export-android.sh [--install]` → `build/android/Pipit.apk` (arm64, landscape, mic permission asked at start, signed with Godot's debug key from the editor settings). The export runs on a copy in %TEMP% because MSBuild reads a `//wsl.localhost/...` project path as a switch; Android builds target net9.0 (the template's runtime). Not yet tried on a tablet: CPU of the pitch tracker, touch sizes, the 31 MB SoundFont (APK is 128 MB). No launcher icon yet. |
| More music | Candidates in the user's `C:\data\flute` (all scanned PDFs, so transcription by hand or OMR): Pachelbel *Canon* SATB recorders (mfiles, © J. Paterson, personal use), Mel Bay *Celtic Tunes for Recorder* (tunes from O'Neill/Roche, PD at source), baroque flute pieces playable on recorder (Hotteterre, Naudot, Boismortier, Loeillet, Blavet, Marcello, Mattheson, Telemann duets), *A Tune a Day* / Suzuki (copyrighted, personal use only). Also needs Public Domain / CC-BY sources: Dowland *Flow my tears* (the downloaded file was *Burst forth my tears*, Kunstderfuge, not used), Van Eyck *Pavaen Lachrymae* / *Engels Nachtegaeltje*, *Lamento di Tristano*, Scarborough Fair. Gettys' consort MIDIs suit the band later (personal use). |
| ~~Windows export (M9)~~ | Done 2026-10-03: `scripts/export-windows.sh` exports a release build with the committed `export_presets.cfg` (data, fonts and SoundFont packed into Pipit.pck), smoke-tests it and zips it to `build/Pipit-windows.zip`. Not yet: an app icon and version numbers in the preset. |

## Speech in Note reading (2026-10-03)

"Say the names" in Note reading uses Vosk offline (`src/App/Audio/SpeechListener.cs`) with the
small models from `scripts/fetch-speech-models.sh` (en/nl/fr, ~66 MB each, git-ignored in
`assets/speech/`, which has a `.gdignore`: Godot scanning them made start-up take minutes).
Exported Windows builds carry them in `speech/` next to the exe (zip ~230 MB). Native libs
are loaded by full path from the build folder (Godot loads assemblies without a Location).
Grammar and spellings in `Core/Practice/NoteWords.cs`. Verified with Windows TTS (English):
do re mi fa sol and C D F G A B are heard with timings; La/Si by the TTS voice were not, and
Dutch/French could not be checked by ear here (no voices installed): needs a real try.
Not on Android (Vosk's .NET package has no Android library; vosk-android's libvosk.so could
be bundled later).

## Known issues / notes

- The full test suite takes about 4 minutes: the end-to-end songs run in six parallel classes (`EndToEndShard0-5`), and songs over 80 s are checked in four 20 s excerpts. Without them (`--filter 'FullyQualifiedName!~EndToEnd'`) it takes ~40 s.
- Song format: triplets (`e3`) and 32nd notes (`t`) since 2026-10-03. Van Eyck pieces that change time signature mid-piece (*Batali*, *Rosemond die lagh gedoocken*, *Stemme nova* 2) or use 64ths (*Amarilli*, vol. 1) are still out.
- Held notes (lesson "Play this note", the fingering trainer) allow for the recorder's calibrated tuning and stick to a note until the pitch moves 0.7 semitone away: a recorder playing 40-50 cents sharp otherwise flipped semitones and was never heard (fixed 2026-10-03). `-- --lessonprobe=<lesson>` plays each target into a lesson as a sharp, wobbling note; test.sh runs it for three lessons.
- The pitch tracker's speed test (`PitchPerformanceTests`, < 2.5 s for 10 s of audio) can fail when Windows is busy; rerun it alone.
- The user's laptop mic needs Windows "Audio enhancements" off (Settings → Check for Windows voice processing).
- User: recorder beginner, Yamaha baroque soprano; prefers hand-coloured fingering holes; analyse
  recordings before assuming playing errors (two "failures" were app bugs).
