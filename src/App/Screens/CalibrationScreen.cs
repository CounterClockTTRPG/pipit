using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.UI;
using Pipit.App.Views;
using Pipit.Core.Analysis;

namespace Pipit.App.Screens;

/// <summary>
/// Three steps: room noise (sets the gate), latency (notes played on clicks), and tuning (the
/// first octave note by note). Gate and latency belong to the microphone; tuning to the recorder.
/// </summary>
public partial class CalibrationScreen : Screen
{
    private enum Step { Intro, Silence, Latency, LatencyResult, Tuning, Done }

    private const double LatencyTempo = 60;
    private const int LatencyCountIn = 4, LatencyAnswers = 8;
    private static readonly int[] TuningSteps = { 0, 2, 4, 5, 7, 9, 11, 12 };

    private readonly Label _heading, _body, _status;
    private readonly FingeringDiagram _diagram = new();
    private readonly LevelMeter _meter = new();
    private readonly Button _primary, _secondary;
    private readonly BeatIndicator _beats = new() { Visible = false };
    private readonly InstrumentEditor _instrumentEditor = new(showAdvanced: false);
    private List<float>? _audio;
    private double _audioStart;
    private readonly ProgressBar _progress = new() { MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 14) };

    private Step _step = Step.Intro;
    private readonly List<PitchFrame> _frames = new();
    private double _phaseStart, _phaseEnd;
    private List<double> _answerClicks = new();
    private readonly HeldNoteDetector _detector = new(1.2, settleSeconds: 0.25);
    private int _tuningIndex;
    private readonly List<NoteTuning> _tunings = new();
    private LatencyResult? _latency;

    public override string Title => "Calibrate";

    public CalibrationScreen()
    {
        _heading = Ui.Label("", 36, PipitTheme.Accent);
        _body = Ui.Text("", 22, PipitTheme.Ink);
        _status = Ui.Text("", 20, PipitTheme.InkSoft);
        _primary = Ui.Button("", OnPrimary);
        _secondary = Ui.Button("", OnSecondary);
        _instrumentEditor.Changed += () => _diagram.Keyed = S.Instrument.Keyed;
        var text = Ui.VBox(16, _heading, _body, _instrumentEditor, _progress, _beats, _status, Ui.HBox(16, _primary, _secondary), Ui.Spacer(),
            Ui.HBox(12, Ui.Label("Input", 16, PipitTheme.InkSoft), Ui.Expand(_meter)));
        AddChild(Ui.HBox(40, Ui.Expand(text, true, true), Ui.MinSize(_diagram, 200, 400)));
    }

    public override void Enter()
    {
        S.ApplyInstrument();
        _diagram.Keyed = S.Instrument.Keyed;
        S.Audio.FrameReady += OnFrame;
        S.Audio.SamplesCaptured += OnSamples;
        Go(Step.Intro);
    }

    public override void Exit()
    {
        S.Audio.FrameReady -= OnFrame;
        S.Audio.SamplesCaptured -= OnSamples;
        S.Clicks.Stop();
        S.ApplyInstrument(); // restores the gate if we left during the silence step
    }

    private void Go(Step step)
    {
        _step = step;
        // Only the listening steps start a fresh collection: the result steps still need the
        // frames just collected (clearing here made every latency result "0 notes heard").
        if (step is Step.Silence or Step.Latency)
            _frames.Clear();
        _progress.Value = 0;
        _progress.Visible = step is Step.Silence;
        _instrumentEditor.Visible = step == Step.Intro;
        _beats.Stop();
        _diagram.Visible = step is Step.Tuning or Step.Latency;
        _secondary.Visible = false;
        _primary.Visible = true;
        _primary.Disabled = false;
        _status.Text = "";
        _status.Modulate = Colors.White;
        var now = S.Audio.Now;
        switch (step)
        {
            case Step.Intro:
                _instrumentEditor.Refresh();
                Set("Which recorder are you calibrating?", "Pick it below, or add it and give it a name. Pipit will then learn three things: how quiet your room is, how long sound takes to travel through your computer (both belong to the microphone), and how this recorder is tuned. It takes about two minutes.", "Start");
                break;
            case Step.Silence:
                Set("1 · Quiet please", "Stay silent for three seconds while Pipit listens to the room.", "");
                _primary.Visible = false;
                // Measure without a gate so the room's level is visible.
                S.Audio.SetGate(0);
                _phaseStart = now + 0.3;
                _phaseEnd = _phaseStart + 3;
                break;
            case Step.Latency:
                var sol = S.Recorder.BottomWrittenMidi + 7;
                Set("2 · Play on the clicks", string.Format(Ui.T("After four count-in clicks, play a short {0} exactly on each of the next eight clicks. Tongue each note: \"du\"."), S.NoteName(sol)), "");
                _diagram.Fingering = S.FingeringFor(sol);
                _primary.Visible = false;
                var beat = 60 / LatencyTempo;
                var start = now + 1.0;
                var clicks = Enumerable.Range(0, LatencyCountIn + LatencyAnswers).Select(i => start + i * beat).ToList();
                _answerClicks = clicks.Skip(LatencyCountIn).ToList();
                S.Clicks.Schedule(clicks, 4);
                _beats.Start(clicks, LatencyCountIn);
                _audio = new List<float>();
                _audioStart = double.NaN;
                _phaseStart = start;
                _phaseEnd = clicks[^1] + 1.0;
                break;
            case Step.LatencyResult:
                ShowLatency();
                break;
            case Step.Tuning:
                _tuningIndex = 0;
                _tunings.Clear();
                ShowTuningNote();
                break;
            case Step.Done:
                Finish();
                break;
        }
    }

    /// <summary>For `--shots`: jump straight to the clicks step.</summary>
    public void DebugStartLatency() => Go(Step.Latency);

    /// <summary>For `--shots`: jump straight to the tuning step.</summary>
    public void DebugStartTuning() => Go(Step.Tuning);

    private void Set(string heading, string body, string primary)
    {
        _heading.Text = Ui.T(heading);
        _body.Text = Ui.T(body);
        _primary.Text = Ui.T(primary);
    }

    private void OnPrimary()
    {
        switch (_step)
        {
            case Step.Intro: Go(Step.Silence); break;
            case Step.LatencyResult: Go(_latency is null ? Step.Latency : Step.Tuning); break;
            case Step.Tuning: NextTuningNote(); break; // "Skip this note"
            case Step.Done: Main.Instance.Back(); break;
        }
    }

    private void OnSecondary()
    {
        if (_step == Step.LatencyResult)
            Go(Step.Latency);
    }

    private void OnFrame(PitchFrame frame)
    {
        switch (_step)
        {
            case Step.Silence or Step.Latency when frame.Time >= _phaseStart:
                _frames.Add(frame);
                break;
            case Step.Tuning:
                if (_detector.Push(frame) is { } held)
                    OnTuningHeld(held);
                break;
        }
    }

    /// <summary>Keeps the latency step's audio, so a failed calibration can be inspected later.</summary>
    private void OnSamples(float[] samples, double time)
    {
        if (_step != Step.Latency || _audio is null)
            return;
        if (double.IsNaN(_audioStart))
            _audioStart = time;
        _audio.AddRange(samples);
    }

    private void SaveLatencyAudio(List<PitchFrame> frames)
    {
        if (_audio is null || _audio.Count == 0)
            return;
        var dir = System.IO.Path.Combine(Services.UserDir, "diagnostics");
        System.IO.Directory.CreateDirectory(dir);
        Core.Recording.WavFile.Write(System.IO.Path.Combine(dir, "calibration-latency.wav"), _audio.ToArray(), S.Audio.SampleRate);
        var voiced = frames.Count(f => f.Voiced);
        var peak = frames.Count == 0 ? -120 : frames.Max(f => f.Db);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "calibration-latency.txt"),
            $"device={S.Audio.CurrentDevice}\nrate={S.Audio.SampleRate}\naudioStart={_audioStart:R}\ngate={S.Audio.GateRms:R}\n" +
            $"clicks={string.Join(",", _answerClicks.Select(c => (c - _audioStart).ToString("R", System.Globalization.CultureInfo.InvariantCulture)))}\n" +
            $"frames={frames.Count} voiced={voiced} peakDb={peak:0.0}\n");
        _audio = null;
    }

    public override void _Process(double delta)
    {
        _meter.Db = S.Audio.LevelDb;
        _meter.GateDb = S.Audio.GateRms > 0 ? 20 * Math.Log10(S.Audio.GateRms) : null;
        var now = S.Audio.Now;
        if (_step is Step.Silence or Step.Latency)
        {
            _progress.Value = Math.Clamp((now - _phaseStart) / (_phaseEnd - _phaseStart), 0, 1);
            if (now >= _phaseEnd)
            {
                if (_step == Step.Silence)
                    FinishSilence();
                else
                    Go(Step.LatencyResult);
            }
        }
    }

    private void FinishSilence()
    {
        var gate = Calibration.GateFromSilence(_frames);
        S.Settings.GateRmsByDevice[S.Audio.CurrentDevice] = gate;
        S.Audio.SetGate(gate);
        S.Save();
        var db = 20 * Math.Log10(gate);
        GD.Print($"Calibration: gate {db:0.0} dBFS on '{S.Audio.CurrentDevice}'");
        Go(Step.Latency);
        if (db > -30)
            _status.Text = Ui.T("Your room (or microphone) is quite noisy; quiet notes may be missed.");
    }

    private void ShowLatency()
    {
        var onsets = Calibration.NoteOnsets(_frames);
        _latency = Calibration.Latency(_answerClicks, onsets);
        SaveLatencyAudio(_frames);
        var peakDb = _frames.Count == 0 ? -120 : _frames.Max(f => f.Db);
        var voicedFrames = _frames.Count(f => f.Voiced);
        _secondary.Visible = true;
        _secondary.Text = Ui.T("Try again");
        if (_latency is null)
        {
            // Loud enough but hardly any pitched frames: something is removing the steady part of the notes.
            var suppressed = peakDb > -45 && voicedFrames < 40;
            Set("2 · Play on the clicks", string.Format(Ui.T("Pipit heard {0} of the 8 notes."), onsets.Count) + " " + Ui.T(suppressed
                ? "The microphone picks up sound, but the notes vanish almost immediately. Windows voice processing is probably removing them: go to Settings and run \"Check for Windows voice processing\"."
                : "Play each one clearly, a little louder, and stop briefly between notes."), "Try again");
            _secondary.Visible = false;
            return;
        }
        S.Settings.LatencyMsByDevice[S.Audio.CurrentDevice] = _latency.LatencyMs;
        S.Save();
        var steady = _latency.SpreadMs < 40;
        Set("2 · Play on the clicks",
            string.Format(Ui.T("Your notes arrive {0:0} ms after the click (heard {1} of {2}, spread ±{3:0} ms)."), _latency.LatencyMs, _latency.Used, _latency.Total, _latency.SpreadMs) + "\n" +
            Ui.T(steady ? "That is steady enough to use." : "That was quite uneven; try again for a more reliable value."),
            "Next");
    }

    private int TuningWritten => S.Recorder.BottomWrittenMidi + TuningSteps[_tuningIndex];

    private void ShowTuningNote()
    {
        Set("3 · Tuning", string.Format(Ui.T("Play {0} and hold it steadily, with a relaxed, normal breath."), S.NoteName(TuningWritten)), "Skip this note");
        _diagram.Fingering = S.FingeringFor(TuningWritten);
        _status.Text = $"{_tuningIndex + 1} / {TuningSteps.Length}";
        _status.Modulate = Colors.White;
        _detector.Reset();
    }

    private void OnTuningHeld(HeldNote held)
    {
        var expected = S.Recorder.WrittenToSounding(TuningWritten);
        var tuning = Calibration.TuneNote(expected, held.Frames);
        if (!tuning.Accepted)
        {
            var heard = S.Recorder.SoundingToWritten(held.SoundingMidi);
            _status.Text = string.Format(Ui.T("That sounds like {0}. Check the fingering and try again."), S.NoteName(heard));
            _status.Modulate = PipitTheme.Bad;
            return;
        }
        _tunings.Add(tuning);
        NextTuningNote();
    }

    private void NextTuningNote()
    {
        if (++_tuningIndex < TuningSteps.Length)
            ShowTuningNote();
        else
            Go(Step.Done);
    }

    private void Finish()
    {
        var i = S.Instrument;
        var lines = new List<string>();
        if (_tunings.Count > 0)
        {
            i.MeasuredOffsetCents = Stats.Median(_tunings.Select(t => t.CentsOff));
            i.CalibratedAt = DateTime.Now;
            foreach (var t in _tunings)
            {
                i.PerNoteOffsetCents[t.ExpectedMidi] = t.CentsOff;
                var name = S.NoteName(S.Recorder.SoundingToWritten(t.ExpectedMidi));
                lines.Add($"{name}: {t.CentsOff:+0;-0;0} c");
            }
            S.Save();
        }
        var summary = _tunings.Count == 0
            ? Ui.T("No notes were tuned; Pipit will assume your recorder is in tune.")
            : string.Format(Ui.T("Overall \"{2}\" plays {0:+0;-0;0} cents from A = {1:0} Hz."), i.MeasuredOffsetCents, i.ReferenceA, i.Name) + "\n" + string.Join("   ", lines);
        Set("All done", summary, "Back");
        _diagram.Visible = false;
    }
}
