using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Pipit.App.UI;
using Pipit.App.Views;
using Pipit.Core.Analysis;
using Pipit.Core.Game;
using Pipit.Core.Music;
using Pipit.Core.Practice;
using Pipit.Core.Recording;
using Pipit.Core.Scoring;
using Pipit.Core.Synthesis;

namespace Pipit.App.Screens;

/// <summary>
/// Plays one song. Rhythm mode follows the beat (count-in, metronome, live grading); Casual mode
/// waits on every note until it is played. "Listen" plays the tune first; after a run the
/// results show the grade and XP, and "Listen to your take" replays the recording against the bars.
/// </summary>
public partial class PlayScreen : Screen
{
    private enum Mode { Rhythm, Casual }
    private enum State { Ready, Preview, Running, Results, Replay }

    private readonly Song _song;
    private readonly List<BarNote> _barNotes;
    private readonly BarsView _bars = new();
    private readonly FingeringDiagram _diagram = new();
    private readonly Label _nextLabel, _feedback, _speedLabel, _best;
    private readonly PanelContainer _intro, _results;
    private readonly VBoxContainer _resultsBox = Ui.VBox(8);
    private readonly Button _start, _listen;
    private readonly OptionButton _modeOption, _difficultyOption;
    private readonly HSlider _speed = new() { MinValue = 50, MaxValue = 120, Step = 5, Value = 100, CustomMinimumSize = new Vector2(160, 0) };
    private readonly CheckBox _metronome = new() { ButtonPressed = true };
    private readonly CheckBox _help = new() { ButtonPressed = true };
    private readonly AudioStreamPlayer _player = new();

    private State _state = State.Ready;
    private Mode _mode = Mode.Rhythm;
    private Tolerances _tolerances = Tolerances.Normal;
    private double _tempoFactor = 1;

    // Rhythm run.
    private double _t0, _spb, _endTime, _latency, _nextGrade;
    private List<ExpectedNote> _expected = new();
    private NoteSegmenter _segmenter = new();
    private readonly List<DetectedNote> _detected = new();
    private GradedRun? _performance;

    // Casual run.
    private CasualMode? _casual;
    private double _casualStart;
    private float _casualBeat;

    // Audio of the run, for replay.
    private List<float> _take = new();
    private double _takeStart = double.NaN;
    private float[]? _takeSamples;

    // Preview.
    private double _previewLead;

    public override string Title => _song.Title(S.Settings.Language);

    public PlayScreen(Song song)
    {
        _song = song;
        _barNotes = song.Melody.Performance.Select(n => new BarNote(n.Index, n.Pitch.Midi, n.StartBeat, n.Beats)).ToList();
        var language = S.Settings.Language;

        var header = Ui.HBox(16, Ui.Label(Title, 30, PipitTheme.Ink), Ui.Label(song.Composer, 18, PipitTheme.InkSoft), Ui.Spacer());
        _best = Ui.Label("", 18, PipitTheme.Accent);
        header.AddChild(_best);

        _intro = Ui.Card(Ui.VBox(6,
            Ui.Text(song.AboutText(language), 17, PipitTheme.Ink),
            Ui.Text(song.TipText(language) is { Length: > 0 } tip ? Ui.T("Tip: ") + tip : "", 16, PipitTheme.Accent)));

        _bars.BarBeats = song.Time.BarBeats;
        _bars.NoteLabel = S.NoteName;
        _bars.SetNotes(_barNotes);
        _bars.CurrentBeat = _barNotes.Min(n => n.StartBeat) - 0.5;
        Ui.Expand(_bars, true, true);
        _nextLabel = Ui.Label("", 18, PipitTheme.InkSoft, HorizontalAlignment.Center);
        var side = Ui.VBox(4, Ui.MinSize(_diagram, 200, 330), _nextLabel);
        _feedback = Ui.Text("", 20, PipitTheme.Ink, HorizontalAlignment.Center);

        _modeOption = Ui.Options(new[] { "Rhythm", "Casual" }, 0, ix => { _mode = (Mode)ix; UpdateControls(); });
        _modeOption.TooltipText = Ui.T("Rhythm: play along with the beat. Casual: the song waits for each note.");
        _difficultyOption = Ui.Options(Tolerances.All.Select(t => t.Name).ToArray(), 1, ix => _tolerances = Tolerances.All[ix]);
        _speedLabel = Ui.Label("", 16, PipitTheme.Ink);
        _speed.ValueChanged += v => { _tempoFactor = v / 100; UpdateControls(); };
        _metronome.Text = Ui.T("Metronome");
        _help.Text = Ui.T("Fingering");
        _help.Toggled += on => side.Visible = on;
        _listen = Ui.Button("♪ Listen", ToggleListen, 18);
        _start = Ui.Button("▶ Start", ToggleRun, 20);
        _start.CustomMinimumSize = new Vector2(140, 48);
        var controls = Ui.HBox(14, _modeOption, Ui.Label("Speed", 16), _speed, _speedLabel, _difficultyOption, _metronome, _help, Ui.Spacer(), _listen, _start);

        _results = Ui.Card(_resultsBox);
        _results.Visible = false;

        AddChild(Ui.VBox(10, header, _intro, Ui.Expand(Ui.HBox(16, _bars, side), true, true), _feedback, _results, controls));
        AddChild(_player);
        _player.Finished += OnPlayerFinished;
        UpdateControls();
    }

    public override void Enter()
    {
        S.ApplyInstrument();
        _diagram.Keyed = S.Instrument.Keyed;
        S.Audio.FrameReady += OnFrame;
        S.Audio.SamplesCaptured += OnSamples;
        var record = S.SongRecord(_song.Id);
        _best.Text = record.Plays == 0 ? "" : $"{Ui.T("Best")}: {new string('★', record.BestStars)}{new string('☆', 3 - record.BestStars)} {record.BestAccuracy:0}%";
        ShowFingeringAt(_barNotes.Min(n => n.StartBeat));
    }

    public override void Exit()
    {
        S.Audio.FrameReady -= OnFrame;
        S.Audio.SamplesCaptured -= OnSamples;
        S.Clicks.Stop();
        _player.Stop();
    }

    private void UpdateControls()
    {
        _speedLabel.Text = $"{_speed.Value:0}%";
        var busy = _state is State.Running or State.Preview or State.Replay;
        _start.Text = Ui.T(_state == State.Running ? "■ Stop" : _state == State.Results ? "▶ Again" : "▶ Start");
        _start.Disabled = _state is State.Preview or State.Replay;
        _listen.Text = Ui.T(_state == State.Preview ? "■ Stop" : "♪ Listen");
        _listen.Disabled = _state is State.Running or State.Replay;
        _modeOption.Disabled = busy;
        _speed.Editable = !busy;
        _difficultyOption.Disabled = busy || _mode == Mode.Casual;
        _metronome.Disabled = busy || _mode == Mode.Casual;
        _speed.Visible = _speedLabel.Visible = true;
    }

    private void SetState(State state)
    {
        _state = state;
        _intro.Visible = state == State.Ready;
        _results.Visible = state is State.Results or State.Replay;
        UpdateControls();
    }

    // ---- shared helpers ------------------------------------------------------------------

    private double TimeToBeat(double time) => (time - _t0) / _spb;
    private double BeatToTime(double beat) => _t0 + beat * _spb;
    private int Sounding(int written) => S.Recorder.WrittenToSounding(written);

    /// <summary>The fingering of the note sounding at (or next after) a beat, plus the following note's name.</summary>
    private void ShowFingeringAt(double beat)
    {
        var current = _barNotes.FirstOrDefault(n => beat < n.StartBeat + n.Beats) ?? _barNotes[^1];
        _diagram.Fingering = S.FingeringFor(current.WrittenMidi);
        var next = _barNotes.FirstOrDefault(n => n.StartBeat > current.StartBeat);
        _nextLabel.Text = S.NoteName(current.WrittenMidi) + (next is null ? "" : "   " + Ui.T("then") + " " + S.NoteName(next.WrittenMidi));
    }

    private void OnSamples(float[] samples, double time)
    {
        if (_state != State.Running)
            return;
        if (double.IsNaN(_takeStart))
            _takeStart = time;
        _take.AddRange(samples);
    }

    // ---- starting and stopping ------------------------------------------------------------

    private void ToggleRun()
    {
        if (_state == State.Running)
        {
            Finish(aborted: true);
            return;
        }
        _player.Stop();
        _bars.ClearMarks();
        _take = new List<float>();
        _takeStart = double.NaN;
        _feedback.Text = "";
        if (_mode == Mode.Rhythm)
            StartRhythm();
        else
            StartCasual();
    }

    private void StartRhythm()
    {
        _spb = _song.SecondsPerBeat(_tempoFactor);
        _latency = (S.DeviceLatencyMs ?? 0) / 1000;
        var firstBeat = _barNotes.Min(n => n.StartBeat);
        var barBeats = (int)Math.Round(_song.Time.BarBeats);
        var firstClick = Math.Floor(firstBeat) - barBeats; // one bar of count-in before the first note
        _t0 = S.Audio.Now + 1.0 - firstClick * _spb;
        var endBeat = _barNotes.Max(n => n.StartBeat + n.Beats);
        var lastClick = _metronome.ButtonPressed ? Math.Ceiling(endBeat) - 1 : Math.Floor(firstBeat) - 1;
        var clicks = new List<(double, bool)>();
        for (var b = firstClick; b <= lastClick; b++)
            clicks.Add((BeatToTime(b), ((int)b % barBeats + barBeats) % barBeats == 0));
        S.Clicks.Schedule(clicks);

        _expected = _song.Melody.Performance.Select(n => new ExpectedNote(n.Index, Sounding(n.Pitch.Midi), BeatToTime(n.StartBeat), BeatToTime(n.EndBeat))).ToList();
        _segmenter = new NoteSegmenter();
        _detected.Clear();
        _performance = null;
        _endTime = BeatToTime(endBeat) + _latency + 0.7;
        _nextGrade = 0;
        SetState(State.Running);
    }

    private void StartCasual()
    {
        _casual = new CasualMode(_song.Melody.Performance.Select(n => Sounding(n.Pitch.Midi)).ToList(), S.Instrument.MeasuredOffsetCents);
        _casualStart = S.Audio.Now;
        _casualBeat = (float)_barNotes[0].StartBeat;
        _bars.SetMark(0, NoteMark.Current);
        _feedback.Text = Ui.T("Take your time: play each note when you have found it.");
        SetState(State.Running);
    }

    // ---- live analysis -----------------------------------------------------------------------

    private void OnFrame(PitchFrame frame)
    {
        if (_state != State.Running)
            return;
        if (_mode == Mode.Casual)
        {
            CasualFrame(frame);
            return;
        }
        // Draw what was played where it was meant to be: shift the trace back by the latency.
        var written = frame.Voiced ? frame.SmoothedMidi - S.Recorder.OctaveTransposition * 12 : double.NaN;
        _bars.AddTrace(TimeToBeat(frame.Time - _latency), written);
        if (_segmenter.Push(frame) is { } note)
            _detected.Add(note);
    }

    private void CasualFrame(PitchFrame frame)
    {
        var casual = _casual!;
        var step = casual.Push(frame);
        if (step is not null)
        {
            _bars.SetMark(step.Index, step.FirstTry ? NoteMark.Perfect : NoteMark.Good);
            if (casual.Finished)
            {
                Finish(aborted: false);
                return;
            }
            _bars.SetMark(casual.Index, NoteMark.Current);
            _feedback.Text = step.FirstTry ? Ui.T("Yes!") : Ui.T("Got it.");
            _feedback.Modulate = PipitTheme.Good;
        }
        else if (casual.LastWrong is { } wrong)
        {
            var target = _barNotes[casual.Index].WrittenMidi;
            _feedback.Text = string.Format(Ui.T("That was {0}. Look for {1}."), S.NoteName(S.Recorder.SoundingToWritten(wrong)), S.NoteName(target));
            _feedback.Modulate = PipitTheme.Bad;
        }
    }

    public override void _Process(double delta)
    {
        var now = S.Audio.Now;
        switch (_state)
        {
            case State.Running when _mode == Mode.Rhythm:
                _bars.CurrentBeat = TimeToBeat(now);
                var firstNote = BeatToTime(_barNotes.Min(n => n.StartBeat));
                _bars.Overlay = now < firstNote ? Math.Ceiling((firstNote - now) / _spb).ToString("0") : "";
                ShowFingeringAt(_bars.CurrentBeat);
                if (now >= _nextGrade)
                {
                    _nextGrade = now + 0.1;
                    ApplyMarks(Grader.Grade(_expected, LiveNotes(), _tolerances, _latency, S.Instrument.MeasuredOffsetCents, _tempoFactor, until: now), live: true);
                }
                if (now >= _endTime)
                    Finish(aborted: false);
                break;
            case State.Running when _mode == Mode.Casual && _casual is not null:
                // Glide to the note being waited for.
                var target = (float)_barNotes[Math.Min(_casual.Index, _barNotes.Count - 1)].StartBeat - 0.3f;
                _casualBeat += (target - _casualBeat) * (float)Math.Min(1, delta * 6);
                _bars.CurrentBeat = _casualBeat;
                ShowFingeringAt(_barNotes[Math.Min(_casual.Index, _barNotes.Count - 1)].StartBeat);
                break;
            case State.Preview:
                _bars.CurrentBeat = _barNotes.Min(n => n.StartBeat) + (_player.GetPlaybackPosition() - _previewLead) / _spb;
                ShowFingeringAt(_bars.CurrentBeat);
                break;
            case State.Replay:
                _bars.CurrentBeat = TimeToBeat(_takeStart + _player.GetPlaybackPosition() - _latency);
                ShowFingeringAt(_bars.CurrentBeat);
                break;
        }
    }

    private List<DetectedNote> LiveNotes()
    {
        var notes = _detected.ToList();
        if (_segmenter.Current is { } current)
            notes.Add(current);
        return notes;
    }

    private void ApplyMarks(GradedRun performance, bool live)
    {
        foreach (var j in performance.Notes)
            _bars.SetMark(j.Expected.Index, Mark(j));
        if (live && performance.Notes.Count > 0)
        {
            var last = performance.Notes[^1];
            (_feedback.Text, _feedback.Modulate) = last.Result switch
            {
                NoteResult.Missed => (Ui.T("Missed"), PipitTheme.InkSoft),
                NoteResult.WrongNote => (string.Format(Ui.T("Wrong note: that was {0}"), S.NoteName(S.Recorder.SoundingToWritten(last.Played!.Semitone))), PipitTheme.Bad),
                _ => (Ui.T(last.Timing.ToString()!) + (last.InTune ? "" : " · " + Ui.T(last.CentsOff > 0 ? "a bit high" : "a bit low")),
                      last.Timing is Timing.Perfect or Timing.Great ? PipitTheme.Good : PipitTheme.Warn),
            };
        }
    }

    private static NoteMark Mark(NoteJudgement j) => j.Result switch
    {
        NoteResult.Missed => NoteMark.Missed,
        NoteResult.WrongNote => NoteMark.Wrong,
        _ => j.Timing is Timing.Perfect or Timing.Great && j.InTune ? NoteMark.Perfect : NoteMark.Good,
    };

    // ---- finishing ------------------------------------------------------------------------------

    private void Finish(bool aborted)
    {
        S.Clicks.Stop();
        _bars.Overlay = "";
        _takeSamples = _take.ToArray();
        if (aborted)
        {
            SetState(State.Ready);
            _feedback.Text = "";
            return;
        }
        SaveTake();
        var record = S.SongRecord(_song.Id);
        var minutes = _takeSamples.Length / (double)S.Audio.SampleRate / 60;
        List<XpLine> lines;
        if (_mode == Mode.Rhythm)
        {
            if (_segmenter.Flush() is { } last)
                _detected.Add(last);
            _performance = Grader.Grade(_expected, _detected, _tolerances, _latency, S.Instrument.MeasuredOffsetCents, _tempoFactor);
            ApplyMarks(_performance, live: false);
            lines = XpRules.RhythmRun(record, _performance.Hits, _performance.Stars, _performance.Accuracy, DateTime.Now);
        }
        else
        {
            var casual = _casual!;
            lines = XpRules.CasualRun(record, casual.Steps.Count, casual.Steps.Count(s => s.FirstTry), casual.Accuracy, DateTime.Now);
        }
        lines.Add(XpRules.Practice(minutes));
        var award = S.AwardXp(lines);
        ShowResults(award);
        SetState(State.Results);
        _feedback.Text = "";
    }

    private void ShowResults(XpAward award)
    {
        foreach (var child in _resultsBox.GetChildren())
            child.QueueFree();
        var summary = Ui.VBox(4);
        if (_mode == Mode.Rhythm && _performance is { } p)
        {
            var stars = new string('★', p.Stars) + new string('☆', 3 - p.Stars);
            summary.AddChild(Ui.HBox(16, Ui.Label($"{p.Accuracy:0}%", 44, PipitTheme.Accent), Ui.Label(stars, 40, PipitTheme.Warn)));
            if (_tempoFactor < 0.999)
                summary.AddChild(Ui.Label(Ui.T("Stars count at 100% speed; this was practice."), 15, PipitTheme.InkSoft));
            int Count(Func<NoteJudgement, bool> f) => p.Notes.Count(f);
            summary.AddChild(Ui.Label(string.Format(Ui.T("Perfect {0} · Great {1} · Good {2} · Early/late {3} · Wrong {4} · Missed {5}"),
                Count(n => n.Timing == Timing.Perfect), Count(n => n.Timing == Timing.Great), Count(n => n.Timing == Timing.Good),
                Count(n => n.Timing is Timing.Early or Timing.Late), Count(n => n.Result == NoteResult.WrongNote), Count(n => n.Result == NoteResult.Missed)), 16));
            var tips = new List<string>();
            if (Math.Abs(p.TimingTendencyMs) > 40)
                tips.Add(string.Format(Ui.T(p.TimingTendencyMs < 0 ? "You tend to play about {0:0} ms early: relax and wait for the beat." : "You tend to play about {0:0} ms late: get your fingers ready a little sooner."), Math.Abs(p.TimingTendencyMs)));
            foreach (var (expected, played, count) in p.Confusions.Take(2))
                tips.Add(string.Format(Ui.T("You played {1} instead of {0} ({2}×)."), S.NoteName(S.Recorder.SoundingToWritten(expected)), S.NoteName(S.Recorder.SoundingToWritten(played)), count));
            var outOfTune = p.Notes.Where(n => n.Result == NoteResult.Hit && !n.InTune).ToList();
            if (outOfTune.Count >= 3)
                tips.Add(Ui.T(outOfTune.Average(n => n.CentsOff) > 0 ? "Several notes were high: blow more gently." : "Several notes were low: blow a little more firmly."));
            foreach (var tip in tips)
                summary.AddChild(Ui.Text("• " + tip, 16, PipitTheme.Ink));
        }
        else if (_casual is { } c)
        {
            summary.AddChild(Ui.Label(string.Format(Ui.T("{0:0}% found first time"), c.Accuracy), 36, PipitTheme.Accent));
            var slow = c.Steps.OrderByDescending(s => s.SecondsToFind).Take(3).Where(s => s.SecondsToFind > 3).ToList();
            if (slow.Count > 0)
                summary.AddChild(Ui.Text(Ui.T("Took longest to find: ") + string.Join(", ", slow.Select(s => $"{S.NoteName(S.Recorder.SoundingToWritten(s.SoundingMidi))} ({s.SecondsToFind:0} s)")), 16));
        }

        var xp = Ui.VBox(4, Ui.Label($"+{award.Total} XP", 26, PipitTheme.Good));
        foreach (var line in award.Lines.Where(l => l.Amount > 0 || l.Reason.Contains("half")))
            xp.AddChild(Ui.Label($"{Ui.T(line.Reason)}  {(line.Amount > 0 ? "+" + line.Amount : "")}", 15, PipitTheme.InkSoft));
        var level = S.Progress.Level;
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = Levels.Fraction(S.Progress.Xp), ShowPercentage = false, CustomMinimumSize = new Vector2(220, 14) };
        xp.AddChild(Ui.HBox(8, Ui.Label(award.LevelledUp ? string.Format(Ui.T("Level up! Level {0}"), level) : string.Format(Ui.T("Level {0}"), level), 18, award.LevelledUp ? PipitTheme.Accent : PipitTheme.Ink), bar));

        var replay = Ui.Button("♪ Listen to your take", ToggleReplay, 16);
        replay.Disabled = _takeSamples is not { Length: > 0 } || _mode == Mode.Casual;
        _resultsBox.AddChild(Ui.HBox(30, Ui.Expand(summary), xp, Ui.VBox(8, replay, Ui.Button("Songs", Main.Instance.Back, 16))));
    }

    /// <summary>Keeps the last 20 run recordings next to the Studio's takes.</summary>
    private void SaveTake()
    {
        if (_takeSamples is not { Length: > 0 })
            return;
        Directory.CreateDirectory(Services.RunsDir);
        var name = $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{_song.Id}_{_mode.ToString().ToLowerInvariant()}";
        WavFile.Write(Path.Combine(Services.RunsDir, name + ".wav"), _takeSamples, S.Audio.SampleRate);
        new TakeInfo
        {
            Name = name, SampleRate = S.Audio.SampleRate, DurationSeconds = _takeSamples.Length / (double)S.Audio.SampleRate,
            InputDevice = S.Audio.CurrentDevice, Session = "song:" + _song.Id, Tempo = _song.Tempo * _tempoFactor,
            LatencyMs = S.DeviceLatencyMs, Instrument = S.Instrument.Kind, Fingering = S.Instrument.Fingering,
            ReferenceA = S.Instrument.ReferenceA, GateRms = S.DeviceGate, TuningOffsetCents = S.Instrument.MeasuredOffsetCents,
        }.Save(Path.Combine(Services.RunsDir, name + ".json"));
        foreach (var old in Directory.GetFiles(Services.RunsDir, "*.json").OrderByDescending(f => f).Skip(20))
        {
            File.Delete(old);
            File.Delete(Path.ChangeExtension(old, ".wav"));
        }
    }

    // ---- listening -------------------------------------------------------------------------------

    /// <summary>For `--shots`.</summary>
    public void DebugListen() => ToggleListen();

    private void ToggleListen()
    {
        if (_state == State.Preview)
        {
            _player.Stop();
            OnPlayerFinished();
            return;
        }
        _spb = _song.SecondsPerBeat(_tempoFactor);
        _previewLead = 0.4;
        var first = _barNotes.Min(n => n.StartBeat);
        var notes = _barNotes.Select(n => new SynthNote(_previewLead + (n.StartBeat - first) * _spb, n.Beats * _spb,
            Pitch.FrequencyOfMidi(Sounding(n.WrittenMidi), S.Instrument.ReferenceA))).ToList();
        _player.Stream = Playback.ToStream(RecorderVoice.Render(notes, S.Audio.SampleRate), S.Audio.SampleRate);
        _player.Play();
        _bars.ClearMarks();
        SetState(State.Preview);
    }

    private void ToggleReplay()
    {
        if (_state == State.Replay)
        {
            _player.Stop();
            OnPlayerFinished();
            return;
        }
        _player.Stream = Playback.ToStream(_takeSamples!, S.Audio.SampleRate);
        _player.Play();
        SetState(State.Replay);
    }

    private void OnPlayerFinished()
    {
        if (_state == State.Preview)
            SetState(State.Ready);
        else if (_state == State.Replay)
            SetState(State.Results);
    }
}
