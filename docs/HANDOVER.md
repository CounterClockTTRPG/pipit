# Pipit — handover / status (2026-10-02)

Read `CLAUDE.md` (how to build, test, conventions) and `docs/DESIGN.md` (decisions, §17 for the
requests from play-testing) first. This file says where things stand and what comes next.

## Run it

```bash
scripts/run.sh                 # build and start (--editor for the Godot editor)
scripts/test.sh                # build, xUnit (~5 min, end-to-end songs dominate), headless boot + smoke
scripts/fetch-soundfont.sh     # GeneralUser GS SoundFont (31 MB, not in git) for the natural voice / band
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
| Study path: 4 chapters, 14 lessons EN/NL with try/exercise/quiz blocks | done (first version) |
| Pip mascot (vector rig, 6 moods), see `docs/pip.md` | done |
| Survival mode, per-song top-10 leaderboard | done |
| Repertoire: folk, baroque (Mutopia, PD), Renaissance dances (Bellugi, **personal use only**, `data/songs/personal/`) | 32 songs |

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
| ~~I18N, EN/NL first~~ (done 2026-10-02; FR still to translate) | `data/i18n/nl.json` (English text → Dutch), loaded into TranslationServer by `Services.ApplyLanguage` with the player's locale and culture. `tools/extract-strings.py --update` adds new texts; `LocalisationTests` fails on any untranslated one. `-- --shots --lang=nl` writes `shots/untranslated.txt` (mostly data text, which is localised in the song/lesson files themselves). Original plan: | All UI text already goes through `Ui.T(...)` (Godot `Tr`) with English as the key, and lessons/songs already have EN/NL. Remaining: a CSV translation table (`data/i18n/strings.csv`, keys = English text), loading it into `TranslationServer`, and setting the locale from `Settings.Language`. Strings built with `string.Format` keep their `{0}` placeholders in translations. A test should list `Ui.T("...")` literals in `src/App` and fail when one has no NL entry. |
| **Baroque vs German fingering** | Supported: per-recorder setting, German chart = baroque with Fa/Fa♯ overrides (both octaves), used everywhere (diagrams, lessons, trainer). Still to do: the lesson "Baroque vs German" with a live comparison of both Fa fingerings (in DESIGN §11.3); show alternative fingerings in the trainer; verify the German table against a Mollenhauer chart. |
| Player profiles (M4) | done; leaderboard entries carry the player name. A shared board across players on this computer would be a nice next step. |
| **Android APK for tablets** | Godot 4 C# exports to Android (.NET 8). Needs: Android SDK/JDK + export templates, `RECORD_AUDIO` permission and a runtime prompt, touch-sized controls (most buttons are already 48 px+), checking YIN/segmenter CPU on a tablet (narrow the search range, maybe hop 512), the SoundFont size (31 MB; consider a trimmed bank), `user://` paths (already used everywhere), no F11/keyboard assumptions, and Windows-only bits (`OS.ShellOpen` on folders, "Segoe UI" font: bundle an OFL font instead). |
| More music | Needs Public Domain / CC-BY sources: Dowland *Flow my tears* (the downloaded file was *Burst forth my tears*, Kunstderfuge, not used), Van Eyck *Pavaen Lachrymae* / *Engels Nachtegaeltje*, *Lamento di Tristano*, Scarborough Fair. Gettys' consort MIDIs suit the band later (personal use). |
| Windows export (M9) | Export preset must include `data/*`, `assets/fonts/*`, `assets/soundfont/*.sf2` as non-resource files. |

## Known issues / notes

- The full test suite takes ~5 minutes; a quick subset (`--filter` without EndToEnd) would help day to day.
- The user's laptop mic needs Windows "Audio enhancements" off (Settings → Check for Windows voice processing).
- User: recorder beginner, Yamaha baroque soprano; prefers hand-coloured fingering holes; analyse
  recordings before assuming playing errors (two "failures" were app bugs).
