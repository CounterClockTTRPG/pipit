# Pipit — design document

Pipit is a recorder teaching aide for Windows desktop, written in Godot 4.7.2
(C#/.NET 8). It shows music as a score, as piano-roll bars or as a falling-note
highway, listens through the microphone, and grades whether each note was the right
pitch, started at the right time and held for the right duration. It also has short
illustrated lessons, in-app recording, and accompaniment where the app plays the
other parts of a piece.

*Pipit*: a small songbird that "pipes". Its mascot, **Pip**, guides players through
lessons and the learning path, which makes the app approachable for young students
(section 12.1).

Status: **draft for review**. Nothing is built yet. Section 16 lists the open questions.

---

## 1. Decisions taken so far

| Topic | Decision |
|---|---|
| Name | **Pipit** (`application/config/name` in `project.godot` is the single source) |
| Instrument | Recorder: soprano first, alto as advanced content, tenor supported (same fingerings as soprano; only the range differs) |
| Fingering | Baroque **and** German, chosen per instrument profile |
| Instruments | Several **instrument profiles** per user (e.g. "Mollenhauer Canta, baroque") |
| Note names | Fixed Do (Do Ré Mi Fa Sol La Si, Do = C), letter names optional |
| UI language | Switchable EN / NL / FR for the interface; **lessons in EN and NL** (French falls back to English) |
| Platform | Windows desktop, developed from WSL like `../godot/godot` |
| Microphone | Device picker with a live level meter; the Windows default input is preselected |
| Recording | In-app WAV recording: free takes, guided test-fixture sessions, replay of play-along runs |
| Sound during play | Metronome + "listen first" preview; **accompaniment** in game mode (section 10) |
| Songs | Built-in public-domain library in a hand-editable JSON format, with multi-part support |
| Lessons | Short illustrated pages: text, images, fingering charts, sound, live "try it" checks |
| Profiles | Several people on one PC, no passwords |
| Practice modes | Play-along, wait mode, tempo slider, section loop, drills, highway mode |
| Gamification | Light but present, with a Focus switch (section 12) |
| Mascot | **Pip the pipit** guides and encourages; a *Young player* profile style (section 12) |
| Difficulty | Tolerances are data-driven and get tuned from real use |
| Version control | git, one commit per meaningful step |

---

## 2. Feature overview

1. **Lessons.** Short pages that combine text, pictures, fingering diagrams, sound
   examples and a live "try it" check. They cover technique, notes, reading music,
   care of the instrument and a bit of history.
2. **Tuner / fingering trainer.** Shows a note and its fingering; you play it; the app
   shows which note it heard and how many cents off it was. This also calibrates the
   instrument.
3. **Play-along.** Score or bars view with a moving playhead, count-in and metronome.
   Each note is graded and coloured.
4. **Help overlay** (toggle). The Do-name above every note and a fingering diagram for
   the current and next note.
5. **Practice modes.** Wait mode, tempo slider, A–B loop, drills.
6. **Highway mode** (the "Guitar Hero" mode). Falling notes with combos. It can add
   **accompaniment**: you play one part and the app plays the others, and which part is
   yours changes as levels advance.
7. **Recording studio.** Record, play back and label takes. You can listen back to any
   graded run alongside the score and your pitch trace.
8. **Profiles and progress.** Stars, history, per-note mastery, XP, streaks and
   achievements.

---

## 3. Architecture

Two assemblies, so that everything that can be tested without Godot is:

```
flutist/                           (repo folder; the app is called Pipit)
├── project.godot
├── Pipit.csproj                   Godot.NET.Sdk/4.7.2, references Pipit.Core
├── src/
│   ├── Core/                      Pipit.Core.csproj — plain net8.0, NO Godot dependency
│   │   ├── Music/                 Pitch, Duration, Note, Part, Song, SongParser, NoteNames
│   │   ├── Instruments/           RecorderType, FingeringSystem, FingeringTable, InstrumentProfile
│   │   ├── Analysis/              YinPitchDetector, PitchFrame, NoteSegmenter, DetectedNote
│   │   ├── Scoring/               NoteMatcher, NoteJudgement, PerformanceScore, Tolerances
│   │   ├── Practice/              WaitModeController, LoopRange, TempoPolicy, Drills
│   │   ├── Synthesis/             Sequencer (song → timed events), RecorderVoice (procedural fallback)
│   │   ├── Recording/             WavWriter, WavReader, Take, FixtureManifest
│   │   ├── Lessons/               Lesson, LessonParser (markdown subset + blocks)
│   │   ├── Profiles/              UserProfile, SongProgress, NoteMastery, ProfileStore (JSON)
│   │   ├── Game/                  Xp, Streaks, Achievements, Unlocks, DailyPractice, PartRotation
│   │   └── Mascot/                TipSelector (grading pattern → tip), MascotMood
│   └── App/                       Godot side
│       ├── Audio/                 MicInput, InputDevices, Recorder, Metronome, SynthPlayer (AudioStreamGenerator)
│       ├── Mascot/                Pip (cut-out rig, AnimationPlayer moods, speech bubble)
│       ├── Views/                 StaffView, BarsView, HighwayView, FingeringDiagram, TunerGauge, LevelMeter
│       └── Screens/               ProfileSelect, Home, Path, Lesson, Library, Play, Drills, Studio, Instruments, Settings, Stats
├── scenes/
├── data/
│   ├── songs/*.json
│   ├── lessons/<id>/{lesson.json, en.md, nl.md, fr.md, *.svg, *.png, *.ogg}
│   ├── fingerings/{baroque,german}.json
│   ├── achievements.json
│   ├── mascot/tips.json
│   └── i18n/strings.csv           Godot CSV translation (en, nl, fr)
├── assets/
│   ├── fonts/Bravura.otf          SMuFL music font, SIL OFL 1.1
│   ├── soundfont/*.sf2            General MIDI soundfont with a permissive licence (section 10.2)
│   └── CREDITS.md                 attribution for every third-party font, image and sound
├── tests/
│   ├── Pipit.Core.Tests/          xUnit, runs via Windows dotnet.exe
│   └── fixtures/audio/            WAV + JSON ground truth, recorded in-app (section 7)
├── scripts/                       build.sh, test.sh, run.sh (WSL → Windows UNC, as in ../godot/godot)
└── docs/
```

**Data flow while playing:**

```
Mic → AudioEffectCapture (muted bus) → MicInput ring buffer ──→ Recorder (WAV, optional)
    → YinPitchDetector (1024 window, 256 hop ≈ 5 ms)         → PitchFrame{t, hz, clarity, rms}
    → NoteSegmenter                                           → DetectedNote{onset, offset, midi, cents}
    → NoteMatcher vs. the user's part (latency-corrected)     → NoteJudgement per expected note
    → views + PerformanceScore → ProfileStore

Song (other parts) → Sequencer → SynthPlayer (AudioStreamGenerator) → speakers/headphones
                                └→ known output timeline, used for leakage handling (10.3)
```

`Core` is fed `float[]` samples and timestamps. The whole pipeline can therefore be
tested from WAV files with no microphone and no Godot, and those WAV files are recorded
with the app itself.

---

## 4. Music model and song format

### 4.1 Model
- `Pitch` is a MIDI number plus a spelling, so that Fa♯ and Sol♭ print correctly.
- Songs store **written** pitch. The instrument type maps written to sounding pitch.
- Time is kept in **beats** (rational numbers) and converted to seconds only at
  playback.
- Repeats and volta brackets are written out when the song is loaded. Ties merge into
  a single sounding note but keep their notation for drawing.
- A song has **one or more parts**. Single-melody songs have one part. Rounds,
  duets and consort pieces have several.

### 4.2 File format

```json
{
  "id": "frere-jacques",
  "title": { "fr": "Frère Jacques", "nl": "Vader Jacob", "en": "Brother John" },
  "composer": "Traditional",
  "chapter": 2,
  "tempo": 100,
  "time": "4/4",
  "key": "G",
  "parts": [
    { "id": "melody", "name": "Melody", "instrument": "soprano",
      "music": "G4/q A4 B4 G4 | G4 A4 B4 G4 | B4 C5 D5/h | B4/q C5 D5/h | ..." }
  ],
  "round": { "entries": 4, "everyBars": 2 },
  "accompaniment": [
    { "id": "drone", "sound": "cello", "music": "G2/w | D3 | G2 | ..." }
  ]
}
```

Music grammar: `PITCH[/DUR]`, where the duration carries over to the following notes
until it changes. Durations are `w h q e s` with an optional `.` for dotted. A rest
is `R/q`. Accidentals are `F#4` and `Bb4`. A tie is `~`, a breath mark `,`, a bar
line `|`, repeats are `|:` and `:|`, and voltas `[1` and `[2`. A pickup bar is
declared with `"pickup": "q"`. Triplets `{3 e e e}` come in a later version.

- `parts` are parts a recorder player can play. Each one can be assigned to the user
  or to the app.
- `accompaniment` is played only by the app (drone, bass line, chords). Each entry
  uses a General MIDI `sound`.
- `round` generates the extra entries from a single melody, so canons cost nothing
  to write.

The parser checks the length of every bar, that all parts have the same total length,
and that every part fits the range of its instrument.

### 4.3 Fixed-Do naming
`Do Ré Mi Fa Sol La Si` with ♯/♭. The French UI uses `Ré`, Dutch and English `Re`.
Octave dots mark the second octave. Letter names are a setting.

---

## 5. Instruments and fingerings

### 5.1 Recorder types
| Type | Written range | Sounding | Fingering base (all holes closed) |
|---|---|---|---|
| Soprano (C) | C4–D6 | one octave above written (C5–D7) | Do |
| Alto (F) | F4–G6 | as written | Fa |
| Tenor (C) | C4–D6 | as written (C4–D6) | Do |

C instruments (soprano and tenor) share fingerings and written notes; F instruments
(alto, and later bass) share theirs. Within a family, the **range is the only
difference**, exactly as you guessed. Baroque fingering is the same pattern relative to
the lowest note on every size, so there is one pattern table per fingering system,
indexed by semitones above the bottom note. The alto shifts the names by a fifth.

What changes per instrument type in code:
- the transposition from written to sounding (the soprano adds an octave);
- the frequency range the pitch detector searches;
- which songs and parts fit.

On a tenor, holes 6 and 7 and the low notes are often keyed (a single key instead of a
double hole). The fingering diagram draws a key symbol when the instrument profile says
so.

### 5.2 Fingering data
`data/fingerings/baroque.json` and `german.json` give, for each step above the
bottom note, a primary fingering and alternatives. Each hole has a state: `open`,
`closed`, `half` (one of a double hole), or `pinched` (half-open thumb). Each table
will be checked against Mollenhauer's published charts before it ships.

### 5.3 Fingering diagram
This is a front view with 7 holes, holes 6 and 7 drawn as double holes (or keys), and
the thumb hole drawn beside the instrument. Closed holes are filled, half holes
half-filled, and the pinched thumb has a slash. The two hands are tinted differently.
The same component is used in lessons, the help overlay, the highway lanes and the
tuner.

### 5.4 Instrument profiles
```json
{
  "id": "b3f1…",
  "name": "Mollenhauer Canta soprano",
  "type": "soprano",
  "fingering": "baroque",
  "keyed": false,
  "referenceA": 440.0,
  "measuredOffsetCents": 6.5,
  "perNoteOffsetCents": { "72": 4.0, "77": 18.0 },
  "minutesPlayed": 312
}
```
- `referenceA` (440, 442, or 415 for baroque-pitch instruments) sets every target
  frequency.
- Calibration fills in the offsets. The overall offset is applied when grading. The
  per-note offsets are shown for information and only applied if you turn that on.
- Progress is tracked **per recorder family and range** (soprano, alto, tenor). Each
  physical instrument keeps its own play time.

---

## 6. Microphone and audio input

### 6.1 Device selection
- **Settings → Audio** lists `AudioServer.GetInputDeviceList()`. The first entry is
  "Windows default", which is preselected; on a laptop this is normally the built-in
  microphone.
- Each device in the list gets a **live level meter** while it is highlighted, and a
  "test" button records 3 s and plays it back. You can tell by ear which mic is which.
- The chosen device is saved by name. If it is missing at startup (a USB mic that is
  unplugged), the app falls back to the Windows default and shows a notice.
- If the app hears no signal at all, a hint points to the Windows privacy setting
  *"Let desktop apps access your microphone"*, which is the usual reason for silence.
- Latency is calibrated **per device** (section 8.3) and stored with the device name.

### 6.2 Capture
- Project setting `audio/driver/enable_input = true`. An `AudioStreamMicrophone` plays
  into a **muted** bus that carries an `AudioEffectCapture`, so the mic is never heard
  through the speakers.
- `MicInput` drains the capture buffer every frame into a ring buffer stamped with
  the audio clock. The pitch analysis and the recorder read the **same samples**, so a
  recording replays exactly what the grader heard.

---

## 7. Recording

### 7.1 Studio screen
- **Free recording**: record, stop, play back, trim, name, delete. Shows the waveform
  and the pitch trace (the detected Do-names drawn over time).
- **Guided fixture sessions**: the app prompts step by step ("hold Sol for 3 s",
  "play Do–Ré–Mi slowly", "tongue La 8×", "slur Sol–La–Si", "play *Au clair de la
  lune*"). Each take is saved with a JSON **ground-truth sidecar** that lists the
  notes it should contain. The sidecar is written from the prompt, before the take is
  analysed, so it states what *should* be there. "Export to test fixtures" copies the
  pairs into `tests/fixtures/audio/`. This gives the pitch detector and segmenter
  regression tests made from your own recorder and your own room.
- **Run replays**: every graded run keeps its recording (the last 20 per profile, plus
  any you star). A replay plays the audio while the score shows the grades and the
  bars view shows your pitch trace against the targets. Hearing yourself is one of the
  most useful practice tools.
- **Lesson audio**: the same recorder is used to make the sound examples for lessons.

### 7.2 Format and storage
- WAV, mono, 16-bit, at the device's mix rate (normally 48 kHz). `WavWriter` is our
  own code (a few dozen lines), not Godot's `AudioEffectRecord`, so the analysis and
  the recording share one capture path.
- Takes are stored in `user://profiles/<userId>/recordings/`, with an
  "Open folder" button. About 5.6 MB per minute, and the oldest unstarred run
  recordings are pruned.

---

## 8. Audio analysis

### 8.1 Pitch detection (YIN)
- The fundamentals cover about 260 Hz (tenor C4) to about 2350 Hz (soprano D7). A
  recorder tone is close to a sine wave, which is close to ideal for YIN.
- The window is 1024 samples at 48 kHz (21 ms, more than 5 periods even of the tenor's
  lowest note), with a 256-sample hop. Absolute threshold about 0.12, parabolic
  interpolation, and a search limited to the active instrument's range ±2 semitones
  (this removes most octave errors).
- Each frame gives `{hz, clarity, rms}`. The noise gate is set from 1 s of silence.
  Results are smoothed with a median filter, and note changes need 30 ms of
  hysteresis.
- Frames that overlap a metronome click are left out, because the click times are
  known.

### 8.2 Note segmentation
A state machine runs over the pitch frames: *silence → attack → sustain → release*. A
new note starts when the sound goes from silent to voiced, when the pitch holds on a
new semitone (a slur), or when the volume dips and recovers while the pitch stays the
same (tonguing a repeated note, a "du" dip of 20–60 ms). **This is the main technical
risk**, and it is the reason the guided fixture sessions exist.

### 8.3 Calibration
1. **Silence**: set the noise floor.
2. **Latency**: play 8 short notes on the click and take the median offset. This is
   stored per input device.
3. **Tuning**: play the first octave note by note. The app records the median cents
   for each note and rejects any reading more than 50 cents off ("that sounds like X —
   check the fingering").
4. **Leak test** (only needed for accompaniment): the app plays a short accompaniment
   sample and measures how much of it reaches the mic (section 10.3).

---

## 9. Scoring

### 9.1 Matching
Each expected note gets a search window of ±250 ms around its latency-corrected onset.
Matching is greedy, in time order, and one-to-one, preferring the detected note with
the correct pitch. Unmatched detected notes count as *extra* (a small penalty) and
unmatched expected notes as *missed*.

### 9.2 Per-note judgement (starting values, tuned from real use)
| Aspect | Rule (Normal) |
|---|---|
| Pitch | correct semitone, and ≥ 60 % of the held frames within ±35 cents of target |
| Onset | Perfect ≤ 50 ms · Great ≤ 90 ms · Good ≤ 140 ms · otherwise Early/Late |
| Duration | held 75–110 % of the time until the next onset, allowing a short articulation gap |
| Wrong note | stable on another semitone; the note that was played is recorded |

All thresholds live in one `Tolerances` data file with Relaxed / Normal / Strict
presets. Changing the difficulty is a data change, not a code change. **Stars**:
★ ≥ 60 %, ★★ ≥ 80 %, ★★★ ≥ 95 %, and they only count at 100 % tempo or faster.

### 9.3 Feedback
The run summary shows the weakest bars, the timing tendency ("you rush by 40 ms on
average"), recurring wrong-note pairs together with the right fingering, and a button
to play back the recording.

---

## 10. Playback, accompaniment and parts

### 10.1 What the app plays
- **Metronome**: a short click from its own generator.
- **Preview**: "listen first" plays your part before you play it.
- **Accompaniment**: a drone, bass line or chords, from the song's `accompaniment`.
- **Other parts**: in rounds, duets, trios and quartets, the app plays every part that
  is not yours.

### 10.2 Synthesis
- **MeltySynth** (pure C#, MIT licence) renders a SoundFont into a Godot
  `AudioStreamGenerator`. This works in pure .NET, so there is nothing native to ship.
  It provides General MIDI recorder, flute, strings, harpsichord and so on. The
  SoundFont must have a licence that allows redistribution. FluidR3_GM (MIT) is the
  candidate, perhaps trimmed to the instruments the app uses.
- A small procedural `RecorderVoice` (a sine with weak harmonics, breath noise and an
  envelope) is the fallback, and lets the tests check sequencing with no SoundFont.
- Because the app writes the output samples itself, it knows exactly when each note
  leaves the speakers (after `AudioServer.GetOutputLatency()`). Playback and grading
  therefore share one clock.

### 10.3 Accompaniment vs. the microphone (the hard part)
If the speakers play while the mic listens, the grader can hear the app instead of you.
Pitch detection is built for one sound at a time; a mix of two recorder-like tones
confuses it. The plan uses several layers:

1. **Headphones mode** (the default for accompaniment, confirmed fine by the user; selected automatically when the leak test
   passes): with headphones on, almost nothing leaks into the mic, and every
   accompaniment feature is available.
2. **Speaker mode**, where the leak test decides what is allowed:
   - **Register separation.** Accompaniment voices are kept below the user part's
     lowest note, and the analysis input gets a high-pass filter just below that
     note. A soprano's lowest note is 523 Hz, so a cello or bassoon bass line under
     about 400 Hz can be filtered out almost completely. This covers drones, bass
     lines and chords in a low register, which is what the early levels use.
   - **Expected-pitch gating.** The grader knows both your part and the app's parts.
     A detected pitch that matches what the app is playing *at that moment*, but not
     what you should be playing, is ignored.
   - **Volume ducking.** Accompaniment volume is capped at the level the leak test
     found safe.
3. **Echo cancellation (dropped for now; headphones are acceptable).** The app has the exact signal it sent to the
   speakers, so an adaptive filter (NLMS) could subtract it from the mic signal. This
   only comes in if layers 1 and 2 are not enough in practice.

Levels in which **you** play a lower part while the app plays the **top** part with a
recorder sound need headphones mode, because the app's part then sits in the same
register as yours. The level is marked with a 🎧 icon.

### 10.4 Part rotation in game mode
Each multi-part piece becomes several levels:

| Level | You play | The app plays |
|---|---|---|
| 1 | melody | drone or bass (filterable in speaker mode) |
| 2 | melody | full accompaniment |
| 3 | 2nd voice of a round or duet | melody (🎧) |
| 4 | inner part of a trio/quartet | the other parts (🎧) |
| 5 | any part, with an alto part where there is one | the rest |

Repertoire suited to this includes rounds (Frère Jacques, Kookaburra, Dona nobis
pacem), Renaissance dance music from **Tielman Susato's *Danserye* (Antwerp, 1551)**,
Playford dances, and Praetorius's *Terpsichore*. All are public domain, and recorder
consorts have always played them.

In highway mode the app's parts are shown as faint ghost lanes, or hidden.

---

## 11. Lessons

### 11.1 Format
Each lesson is a folder `data/lessons/<id>/` with `lesson.json` (id, chapter, the
notes it introduces, its prerequisites) and one page file per language (`en.md`,
`nl.md`, and optionally `fr.md`). Lessons are written in English and Dutch; a missing
language falls back to English. Pages are written in a
small markdown subset (headings, paragraphs, bold, italics, lists) plus **blocks**
that become live controls:

```
::fingering note=Sol4
::staff "G4/q A4 B4/h"
::play song=au-clair-de-la-lune bars=1-2
::audio file=tonguing-du.ogg caption="Tonguing: 'du du du'"
::image file=praetorius-1619.png caption="The recorder family, Praetorius, 1619"
::try note=Sol4 hold=1.5
::quiz "Which note has every hole closed?" Do* | Ré | Sol
```

The **`::try`** block uses the live microphone check from the tuner. A lesson about
Sol ends with you playing a correct Sol, not just reading about it.

### 11.2 Images and sound
- Fingering diagrams and staff snippets are **drawn live** by the app, so they follow
  the profile's fingering system and language.
- Diagrams of instrument parts, hand position and breathing are SVG files made for the
  project.
- Historical pictures are public-domain scans, for example Virdung's *Musica
  getutscht* (1511), Praetorius's *Syntagma musicum* (1619) and the title page of Van
  Eyck's *Der Fluyten Lust-hof* (1644). Their source and licence are recorded in
  `assets/CREDITS.md`.
- Sound examples are synthesised, or recorded in the Studio (section 7).

### 11.3 Planned lessons
| Group | Lessons |
|---|---|
| Getting started | Meet the recorder (parts, the family, which recorder you have) · Holding it · Breathing & blowing gently · Tonguing with "du" |
| Reading music | The staff and treble clef · Note lengths & rests · Time signatures & counting · Ties, dots, pickups · Sharps & flats · Repeats |
| Notes | A short lesson for every new note, attached to its chapter in the learning path |
| Technique | Half-holing (double holes) · The pinched thumb and the upper octave · Slurs vs. tonguing · Long tones & steady pitch |
| Your instrument | Baroque vs. German fingering, with a live comparison: play Fa with the forked and the simple fingering and see how many cents apart they are on *your* recorder (suggested by the user after finding the simple Fa themselves) · Care: warming up, condensation, drying, oiling (wooden instruments) |
| Technique, learned the hard way | Low notes need gentle air (beginners overblow them sharp; the tuner shows it) · Breaking into the high octave: seal the holes, pinch the thumb, faster (not more) air |
| History | Medieval & Renaissance consorts · The Baroque golden age (Van Eyck, Handel, Telemann) · Decline and the 20th-century revival (Dolmetsch, school recorders, German fingering) |

History lessons are short (one page, two or three pictures, one sound example), and
every fact is checked against sources before it ships. The lessons are part of the
learning path, so finishing one earns XP, and some unlock achievements.

---

## 12. Gamification

A **Focus** switch hides popups and confetti for plain practice.

- **Learning path**: chapters with a *lesson* → *new note* (fingering trainer) →
  drill → 3–4 songs → a *chapter piece*, and from chapter 4 onwards an *ensemble
  piece* with accompaniment. The next chapter unlocks at about ⅔ of the stars. The
  path is drawn as the pipit's journey, in keeping with the name.
- **XP and levels** for each user, from finished songs, stars, lessons, drills and
  practice minutes. There is no XP for repeating a 3-star song unless you beat your
  best score.
- **Daily goal and streak** (10 minutes by default), with a *streak freeze* earned for
  each week of streak.
- **Daily practice set**: a warm-up long tone, a drill on your weakest note, one review
  song and one new song.
- **Note mastery**: bronze, silver or gold per note on the fingering chart.
- **Achievements** (about 25, in `data/achievements.json`), for example: first song;
  10 Perfects in a row; a clean wait-mode run; an 8 s long tone within ±10 cents; gold
  on every first-octave note; a 7-day streak; every part of a round played; first
  consort piece; first alto song; all history lessons read.
- **Personal bests**: score, combo and a ghost line in highway mode.
- **Ensemble stars**: one star per part of a piece you have mastered. This rewards
  part rotation.
- **Instrument mileage**: play time per instrument profile, with badges.

### 12.1 Pip, the guide

**Pip the pipit** is a small, friendly songbird who guides the player. Pip's main job
is to make the app approachable for young students, without getting in the way of
adults.

- **Where Pip appears**:
  - first-run onboarding (making a profile, choosing a recorder, the mic check);
  - lessons, where Pip talks in speech bubbles and explains the `::try` blocks;
  - the tuner ("blow a bit softer, you're sharp");
  - run summaries;
  - the learning path, where Pip travels from chapter to chapter.
- **Pip reacts to sound**:
  - *listening*: tilts their head while the mic hears you;
  - *singing along*: small notes float up while you hold a correct note;
  - *cheering*: on stars, streaks and achievements;
  - *thinking*: when offering a hint;
  - *dozing*: when nothing has happened for a while.
- **Hints are tied to situations.** `data/mascot/tips.json` maps grading patterns to
  tips. Examples:
  - a recurring Fa/Fa♯ confusion → shows both fingerings;
  - consistently sharp → "blow more gently";
  - notes too short → "keep the air going until the next note";
  - squeaks (sudden octave jumps) → "check every hole is fully covered".

  Tips are localised like every other string.
- **Encouraging, never harsh.** A failed run is "almost! let's try bars 3–4 slowly",
  with a button that starts exactly that loop.
- **Art**: Pip is made of layered vector parts (body, head, wings, eyes, beak) and
  animated with Godot's `AnimationPlayer` in a cut-out style. That makes the moods
  cheap to add and keeps Pip sharp at any size. The first version is drawn for the
  project as SVG. If you want something more polished, an illustrator could redraw the
  parts later without code changes.
- **Volume**: in Focus mode Pip is reduced to a small icon that only shows tips when
  clicked.

### 12.2 Young players
Each profile has a **player style**: *Young player* or *Standard*.

| | Young player | Standard |
|---|---|---|
| Pip | always present, talks more | appears for tips and celebrations |
| Text | short sentences and a larger font, written for readers aged 9 and up (no read-aloud needed) | normal |
| Feedback | arrows and faces ("a bit high ↑"), no cents or milliseconds | numbers available |
| Difficulty default | Relaxed | Normal |
| Note names | Do-names always shown above notes | toggleable |
| Rewards | stickers for Pip's nest in addition to XP | XP, levels, achievements |

Big click targets and keyboard-free navigation apply to both styles.

---

## 13. Song library (first pass, all public domain)

| Ch. | New notes / skill | Songs |
|---|---|---|
| 1 | Si La Sol | Hot Cross Buns · Au clair de la lune · Mary Had a Little Lamb (adds Ré') |
| 2 | Do' Ré' | Vader Jacob / Frère Jacques (+ round) · Ode to Joy · Lightly Row |
| 3 | Mi, Ré (low) | Altijd is Kortjakje ziek · Sinterklaas kapoentje · Ik zag twee beren |
| 4 | Fa♯, eighths, dotted rhythms | Oh When the Saints · Jingle Bells · Simple Gifts · *ensemble:* Kookaburra (round) |
| 5 | Fa, Do (low), 3/4 | Amazing Grace · Brahms' Lullaby · Scarborough Fair · *ensemble:* Dona nobis pacem |
| 6 | Si♭, 6/8, ties | Greensleeves · Londonderry Air · Daar was laatst een meisje loos · *ensemble:* Playford duet |
| 7 | Upper octave, pinched thumb | Minuet in G (Petzold) · Purcell Rondeau (theme) · *ensemble:* Susato — *La Mourisque* |
| 8 | Speed, articulation | Van Eyck — *Engels Nachtegaeltje* (theme) · Handel Hornpipe (excerpt) · *ensemble:* Susato — *Ronde* |
| A1+ | **Alto** path | Restarts the note-by-note path with alto fingerings, then baroque repertoire and alto parts in consort pieces |
| T1+ | **Tenor** | Uses the soprano path's material, filtered to what fits the tenor's range; its own progress |

---

## 14. Persistence

Data lives under `user://` (`%APPDATA%\Godot\app_userdata\Pipit\`):

```
settings.json                      language, input device, latency per device, headphones mode, focus mode
profiles/<userId>/profile.json     name, avatar colour, instruments[], activeInstrumentId, xp, streak, achievements, lessonsDone
profiles/<userId>/progress.json    per recorder type: song/part → {stars, best, history[≤20], lastPlayed}, noteMastery
profiles/<userId>/recordings/      *.wav + *.json (take metadata, ground truth or run link)
```

Every file carries a `schemaVersion`, migrations are plain functions, and files are
written atomically (temp file, then rename).

---

## 15. Milestones

Each milestone ends with something you can run and try with your own recorder.

| # | Milestone | Done when |
|---|---|---|
| M0 | Skeleton: Godot + Core projects, WSL build/test scripts, xUnit, git | `scripts/test.sh` is green; an empty Pipit window opens |
| **M1** | **Listening**: mic device picker and level meters, Studio (record, play back, guided fixture sessions), YIN, live tuner, fingering trainer (soprano/tenor/alto, baroque and German), calibration | Your Mollenhauer's notes are recognised across the whole range; the first fixtures are committed and the tests pass on them |
| M2 | Song format and parser, chapter 1–2 songs, bars view, segmenter, play-along scoring, metronome, preview (synth) | You play *Au clair de la lune* and get a believable per-note grade, with replay |
| M3 | Staff view (Bravura) and help overlay | Every library song renders correctly |
| M4 | User and instrument profiles, progress, stars | Two people with two instruments keep separate progress |
| M5 | Lessons engine, first Pip art (speech bubble, basic moods), and the "Getting started" and "Reading music" lessons | Lessons with live `::try` blocks work in EN, presented by Pip |
| M6 | Practice modes: wait, tempo/auto-ramp, A–B loop, drills | All usable from the song screen |
| M7 | Highway mode and accompaniment: MeltySynth, leak test, headphones/speaker mode, part rotation | A round and a Susato dance play with the app on the other parts |
| M8 | Gamification and Pip: path, XP, streaks, daily set, mastery, achievements, mascot moods and tips, Young-player style | A new young-player profile can follow the path from chapter 1 with Pip's help |
| M9 | Full library, alto path, history lessons, NL/FR translations, Windows export | A zip runs on a clean Windows machine |

---

## 16. Answers to the open questions

| Question | Answer | Consequence |
|---|---|---|
| Test recordings | Yes, via the guided sessions | M1 ends with fixtures from the user's soprano (tenor and alto when available) |
| Microphones | Built-in laptop mic only (on this PC) | Tune for a built-in mic. Windows "audio enhancements" (noise suppression, automatic gain) can smear note onsets and cut quiet notes. The Settings screen explains how to switch them off; automatic detection is not attempted yet |
| Headphones | Fine for accompaniment | Headphones mode is the default for accompaniment; echo cancellation is dropped |
| Lesson languages | EN and NL | Each lesson is written in both; the French UI falls back to English lessons |
| Youngest player | 9 | The Young player style keeps text short and simple, without read-aloud |
| Pip's look | Cartoonish | A friendly cartoon bird with features of the real pipit (streaky chest, slim beak) in warm, bright colours |

---

## 17. Requests from play-testing (2026-10-01)

Done in M2: casual (wait) mode, speed dial (songs and Studio), XP and levels, achievements,
optional run analysis, song introductions and tips, full Ode to Joy, difficulty ratings and a
repertoire section, baroque pieces from Public Domain editions, full screen.

Still to do, roughly in the order agreed:

| Request | Where it goes |
|---|---|
| ~~Score view: real notation as an alternative to the bars (View: Bars / Score)~~ | done (M3) |
| ~~Study path: theory pages between blocks, a gentle start, rhythm exercises~~ | first version done: 4 chapters, 14 lessons (EN/NL); Pip is a placeholder 🐦 until the art (M5/M8) |
| ~~Survival mode with a per-song leaderboard~~ | done, with the highway view (M6) |
| ~~More songs: Frère Jacques, Twinkle, Amazing Grace, Greensleeves~~; Scarborough Fair | done, except Scarborough Fair |
| Dowland, *Flow my tears*; Van Eyck's *Pavaen Lachrymae* on it | needs a Public Domain / CC-BY MIDI or MusicXML (user to download from IMSLP/CPDL) |
| Van Eyck, *Der Fluyten Lust-hof*: themes at intermediate, variations at advanced | next song round |
| *Lamento di Tristano* (advanced) | next song round |
| Alto repertoire: Polonaise BWV Anh. 119 (fits the alto exactly), Handel recorder sonatas HWV 360/365/369 | Handel sonatas and Telemann duet in (Mutopia), chapter 10; Polonaise still to do |
| Renaissance dances from David Bellugi's site | done, personal use only (`data/songs/personal/`) |
| A natural (sampled) recorder sound next to the simple one | with MeltySynth, M7 |
| Accompaniment through headphones | M7, in progress (see docs/HANDOVER.md) |
| ~~UI translation: EN/NL~~; FR | done for Dutch (342+ texts, tested); French to do |
| Baroque vs German fingering | done: per recorder, chart checked against Mollenhauer's, comparison lesson with a live measurement (chapter 4) |
| Android APK for tablets | after M9; see docs/HANDOVER.md for what it needs |
| More lessons, spaced out along the path | done: 10 chapters, 25 lessons |
| Van Eyck, *Der Fluyten Lust-hof* | 21 songs from a MuseScore transcription (MusicXML, `tools/musicxml2pipit.py`), personal use; pieces with triplets or 32nd notes wait for those in the song format |
| Dowland *Flow my tears*, SATB | done from an OMR MusicXML, band plays alto/tenor/bass, personal use |
| Band switch and speed dial work during a song | done: the band fades in/out (rendered on the fly if it was off), a speed change applies from the current beat (`TempoMap`); results count the slowest speed used |

Song sources: anything longer than a folk tune is converted from a Public Domain edition (the
Mutopia Project) with `tools/midi2pipit.py`, never transcribed from memory; each song file
records its `source`.
