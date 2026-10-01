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
```

## Environment notes

- Godot and the .NET SDK are Windows executables; the project is passed to them as a UNC
  path (`\\wsl.localhost\<distro>\...`). `scripts/common.sh` derives this, and runs dotnet
  from `C:` because MSBuild dislikes a UNC working directory.
- `NuGet.Config` lists Godot's bundled nupkgs first so restore works offline.
- `audio/driver/enable_input` is on in `project.godot`; the mic is needed from M1 onward.

## Conventions

- Notes are stored as **written** pitch; the instrument type maps to sounding pitch.
- Note names are Fixed Do (Do = C) by default; French spells Re as "Ré".
- Commit in milestone-sized steps.
