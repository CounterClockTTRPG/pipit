# Pipit

A recorder teaching aide. Pipit listens to you play through the microphone and tells you
whether each note had the right pitch, started on time and lasted long enough. Pip, a small
songbird, guides you through short lessons and a learning path, from your first note to
baroque sonatas.

Built with Godot 4.7 (C#/.NET 8) for Windows desktop; an Android build exists too.

## What it does

- **Play along** with a score, piano-roll bars or a falling-note highway, with wait mode, a
  speed dial (20% to 200%), section loops and a metronome.
- **Grading** of pitch, timing and duration, with latency calibration per microphone.
- **Accompaniment**: the app plays the other parts of duets, consorts and sonatas.
- **Lessons** in English and Dutch (interface in English, Dutch and French), with fingering
  charts for baroque and German fingering, and soprano, alto and tenor recorders.
- **Note reading** drills, where you say or tap the note names (Fixed Do or letters).
- **Repertoire**: folk tunes, Renaissance dances and baroque pieces, searchable by period,
  composer and difficulty. You can add your own MusicXML files too.
- **Profiles** for several players on one computer, with levels, stars and high scores, and a
  Focus switch for players who want less game.
- **Recording**: free takes, and replays of what the grader heard.

## Building

You need [Godot 4.7.2 .NET](https://godotengine.org/) and the .NET 8 SDK. The scripts are
written for WSL calling the Windows executables (see `scripts/common.sh`); set `GODOT` to
your Godot executable.

```bash
scripts/fetch-soundfont.sh       # General MIDI sounds for accompaniment (not kept in git)
scripts/fetch-speech-models.sh   # optional: speech recognition for note reading
scripts/build.sh                 # build the app and its core library
scripts/test.sh                  # unit tests plus a headless run through every screen
scripts/run.sh                   # start Pipit (--editor opens the Godot editor)
scripts/export-windows.sh        # release build in build/windows/
```

**Laptop microphones** often run Windows voice processing that silences held notes. If Pipit
stops hearing you, set "Audio enhancements" to Off for the microphone in Windows Settings.
The microphone check in Pipit's Settings detects this.

## Layout

| Path | Contents |
|---|---|
| `src/Core/` | Music model, pitch detection, scoring, profiles, game rules; plain .NET with no Godot dependency |
| `src/App/` | Godot screens and nodes |
| `tests/` | xUnit tests and recorded audio fixtures |
| `data/` | Songs, lessons, fingerings, the learning path and translations |
| `tools/` | Python converters from MIDI, MusicXML and ABC to Pipit's song format |
| `docs/DESIGN.md` | Design, decisions and milestones |

Songs are JSON files in `data/songs/`, listed in `data/songs/index.json`. The music notation
is documented in `src/Core/Music/SongParser.cs`.

## Licence

Free to share and adapt, as long as you credit Pipit:

- **Code**: [MIT](LICENSE).
- **Pipit's own content** (lessons, arrangements, docs, images):
  [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
- **Third-party material** keeps its own licence. This covers the fonts (SIL OFL), the
  soundfont, and some songs that are CC BY-SA or **CC BY-NC-SA (non-commercial)**.

[LICENSE-CONTENT.md](LICENSE-CONTENT.md) lists what is covered by which licence and gives a
suggested credit line. [assets/CREDITS.md](assets/CREDITS.md) lists the sources.
