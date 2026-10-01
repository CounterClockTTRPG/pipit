# Pipit

A recorder teaching aide in Godot 4.7.2 (C#/.NET 8) for Windows desktop. It listens through
the microphone and grades pitch, timing and duration. Design, decisions and milestones:
`docs/DESIGN.md`. Read it before adding features; it records what was agreed with the user.

The name lives in exactly one place: `application/config/name` in `project.godot`.

## Layout

- `src/Core/` — `Pipit.Core.csproj`, plain net8.0 with **no Godot dependency**. Music model,
  pitch detection, scoring, profiles, game rules. Anything that can be tested without Godot
  belongs here.
- `src/App/` — Godot nodes and screens; thin glue over Core.
- `tests/Pipit.Core.Tests/` — xUnit tests for Core.
- `Pipit.csproj` excludes `src/Core/**` and `tests/**` from its own glob (the Godot SDK would
  otherwise compile them twice) and references Core as a project.

## Commands

```bash
scripts/build.sh          # build Pipit + Core
scripts/test.sh           # build, xUnit, headless boot of the main scene (fails on any ERROR line)
scripts/run.sh            # launch the app; --editor opens the Godot editor
scripts/import-fixtures.sh  # copy guided Studio takes into tests/fixtures/audio (--list to preview)
```

Diagnostics: `"$GODOT" --headless --audio-driver WASAPI --path "$PROJECT_UNC" -- --micprobe` lists the
input devices and reports what the microphone delivered in 3 s (headless alone uses a dummy
audio driver, so it never hears anything). `-- --smoke` visits every screen and quits; test.sh
runs it.

## Environment notes

- Godot and the .NET SDK are Windows executables; the project is passed to them as a UNC
  path (`\\wsl.localhost\<distro>\...`). `scripts/common.sh` derives this, and runs dotnet
  from `C:` because MSBuild dislikes a UNC working directory.
- `NuGet.Config` lists Godot's bundled nupkgs first so restore works offline.
- `audio/driver/enable_input` is on in `project.godot`; the mic is needed from M1 onward.

## Audio architecture

- `AudioHub` (child of the `Services` autoload) captures from a **muted** bus with an
  `AudioEffectCapture`, so the mic is never played back. Everything is timed on the **capture
  clock** (seconds of audio captured); `ClickPlayer` converts scheduled capture times to
  generator samples. The constant part of the remaining offset is what latency calibration
  measures, per input device.
- Pitch analysis is the same `PitchTracker` live and offline, so a recorded take replays exactly
  what the grader heard. Guided takes store their schedule as ground truth; `TakeVerifier` is
  used by both the Studio and `FixtureTests`.
- Data files are read through Godot's `FileAccess` (`res://data/...`) because exported builds
  pack them; the export preset must include `data/*` as non-resource files (M9).

## Conventions

- Notes are stored as **written** pitch; the instrument type maps to sounding pitch.
- Note names are Fixed Do (Do = C) by default; French spells Re as "Ré".
- Commit in milestone-sized steps.
