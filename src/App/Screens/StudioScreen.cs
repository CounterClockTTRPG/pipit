using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Pipit.App.UI;
using Pipit.App.Views;
using Pipit.Core.Analysis;
using Pipit.Core.Recording;

namespace Pipit.App.Screens;

/// <summary>
/// Record, play back and inspect takes. Guided sessions conduct the player (count-in, clicks,
/// the note and fingering to play now) and save the schedule as ground truth next to the WAV,
/// which is what turns a recording into a test fixture.
/// </summary>
public partial class StudioScreen : Screen
{
    private readonly ItemList _takes;
    private readonly TakeView _view = new();
    private readonly Label _summary;
    private readonly Button _record, _guided, _play, _delete, _star;
    private readonly OptionButton _sessions;
    private readonly HSlider _speed = new() { MinValue = 50, MaxValue = 120, Step = 5, Value = 100, CustomMinimumSize = new Vector2(150, 0) };
    private readonly Label _speedLabel = new() { Text = "100%" };
    private readonly LevelMeter _meter = new();
    private readonly AudioStreamPlayer _player = new();

    // Conductor (visible while a guided take records).
    private readonly PanelContainer _conductor;
    private readonly Label _now, _next, _beat;
    private readonly FingeringDiagram _diagram = new();
    private readonly BeatIndicator _beatDots = new();

    private List<float>? _recording;
    private double _recordStart = double.NaN;
    private GuidedSession? _session;
    private double _sessionStart; // capture time of the first beat after the count-in
    private double _stopAt = double.NaN;
    private string? _selected;
    private double _playStartedAt = double.NaN;

    public override string Title => "Studio";

    public StudioScreen()
    {
        _takes = Ui.Expand(new ItemList(), true, true);
        _takes.ItemSelected += ix => Select((string)_takes.GetItemMetadata((int)ix));
        _record = Ui.Button("● Record", ToggleRecord);
        _sessions = Ui.Options(GuidedSessions.All.Select(s => s.Title).ToArray(), 0, _ => { });
        _guided = Ui.Button("Start guided take", StartGuided, 18);
        _play = Ui.Button("▶ Play", TogglePlay, 18);
        _delete = Ui.Button("Delete", Delete, 18);
        _star = Ui.Button("☆ Keep", ToggleStar, 18);
        var openFolder = Ui.Button("Open folder", () => OS.ShellOpen(Services.RecordingsDir), 18);
        openFolder.Visible = !Services.IsAndroid;

        var left = Ui.MinSize(Ui.VBox(10,
            _record,
            Ui.Text("Guided takes record what Pipit asks for, so they can check recognition and become test recordings.", 14, PipitTheme.InkSoft),
            _sessions, Ui.HBox(8, Ui.Label("Speed", 16), Ui.Expand(_speed), _speedLabel), _guided,
            Ui.Heading("Takes", 20), _takes,
            Ui.HBox(8, _play, _star, _delete), openFolder,
            Ui.HBox(8, Ui.Label("Input", 14, PipitTheme.InkSoft), Ui.Expand(_meter))), 340, 0);

        _summary = Ui.Text("", 18, PipitTheme.Ink);
        _now = Ui.Label("", 84, PipitTheme.Ink, HorizontalAlignment.Center);
        _next = Ui.Label("", 22, PipitTheme.InkSoft, HorizontalAlignment.Center);
        _beat = Ui.Label("", 22, PipitTheme.Accent, HorizontalAlignment.Center);
        _conductor = Ui.Card(Ui.HBox(24, Ui.Expand(Ui.VBox(6, _beat, _now, _next, _beatDots)), Ui.MinSize(_diagram, 200, 300)));
        _conductor.Visible = false;
        _speed.ValueChanged += v => _speedLabel.Text = $"{v:0}%";

        var right = Ui.VBox(12, _conductor, Ui.Expand(_view, true, true), _summary);
        AddChild(Ui.HBox(24, left, Ui.Expand(right, true, true)));
        AddChild(_player);
        _player.Finished += () => { _view.Playhead = -1; _play.Text = Ui.T("▶ Play"); _view.QueueRedraw(); };
    }

    public override void Enter()
    {
        S.ApplyInstrument();
        var info = S.Recorder;
        _view.LowWritten = info.BottomWrittenMidi - 2;
        _view.HighWritten = info.TopWrittenMidi + 2;
        _view.Transposition = info.OctaveTransposition * 12;
        _view.NoteLabel = S.NoteName;
        _diagram.Keyed = S.Instrument.Keyed;
        S.Audio.SamplesCaptured += OnSamples;
        Directory.CreateDirectory(Services.RecordingsDir);
        RefreshList();
        UpdateButtons();
    }

    public override void Exit()
    {
        S.Audio.SamplesCaptured -= OnSamples;
        if (_recording is not null)
            StopRecording(save: false);
        _player.Stop();
    }

    // ---- recording ---------------------------------------------------------------------

    private void ToggleRecord()
    {
        if (_recording is null)
            BeginRecording(S.Audio.Now);
        else
            StopRecording(save: true);
    }

    private void BeginRecording(double startTime)
    {
        _player.Stop();
        _recording = new List<float>(S.Audio.SampleRate * 60);
        _recordStart = startTime;
        _view.Clear();
        _view.Message = Ui.T("Recording…");
        _summary.Text = "";
        UpdateButtons();
    }

    private void OnSamples(float[] samples, double time)
    {
        if (_recording is null)
            return;
        // Trim the first block so the take starts exactly at _recordStart.
        var skip = (int)Math.Round((_recordStart - time) * S.Audio.SampleRate);
        if (skip >= samples.Length)
            return;
        if (_recording.Count == 0 && skip < 0)
            _recordStart = time; // started late: the take starts at this block instead
        _recording.AddRange(skip > 0 ? samples.Skip(skip) : samples);
    }

    /// <summary>For `--shots`: start a guided session by id.</summary>
    public void DebugStartGuided(string id)
    {
        _sessions.Selected = GuidedSessions.All.ToList().FindIndex(g => g.Id == id);
        StartGuided();
    }

    private void StartGuided()
    {
        var session = GuidedSessions.All[_sessions.Selected];
        session = session with { Tempo = session.Tempo * _speed.Value / 100 };
        _session = session;
        var lead = 0.5;
        var now = S.Audio.Now;
        BeginRecording(now + lead);
        var countInStart = now + lead + 0.5;
        _sessionStart = countInStart + GuidedSession.CountInBeats * session.SecondsPerBeat;
        var beats = (int)Math.Ceiling(GuidedSession.CountInBeats + session.TotalBeats);
        var beatTimes = Enumerable.Range(0, beats).Select(b => countInStart + b * session.SecondsPerBeat).ToList();
        S.Clicks.Schedule(beatTimes, 4);
        // Whole-beat notes get a dot and a hold bar; notes starting off the beat still sound
        // with the clicks but have no dot of their own.
        var spans = session.Spans()
            .Where(n => Math.Abs(n.StartBeat - Math.Round(n.StartBeat)) < 1e-9)
            .Select(n => (GuidedSession.CountInBeats + (int)Math.Round(n.StartBeat), n.Beats)).ToList();
        _beatDots.Start(beatTimes, GuidedSession.CountInBeats, spans);
        _stopAt = _sessionStart + session.TotalBeats * session.SecondsPerBeat + 1.0;
        _conductor.Visible = true;
        UpdateButtons();
    }

    private void StopRecording(bool save)
    {
        var samples = _recording!.ToArray();
        _recording = null;
        S.Clicks.Stop();
        _beatDots.Stop();
        _conductor.Visible = false;
        var session = _session;
        _session = null;
        _stopAt = double.NaN;
        _view.Message = "";
        UpdateButtons();
        if (!save || samples.Length < S.Audio.SampleRate / 2)
            return;

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var name = session is null ? $"{stamp}_take" : $"{stamp}_{session.Id}_{S.Instrument.Kind.ToString().ToLowerInvariant()}";
        var info = new TakeInfo
        {
            Name = name,
            SampleRate = S.Audio.SampleRate,
            DurationSeconds = (double)samples.Length / S.Audio.SampleRate,
            InputDevice = S.Audio.CurrentDevice,
            Session = session?.Id,
            Tempo = session?.Tempo,
            LatencyMs = S.DeviceLatencyMs,
            Instrument = S.Instrument.Kind,
            Fingering = S.Instrument.Fingering,
            ReferenceA = S.Instrument.ReferenceA,
            GateRms = S.DeviceGate,
            TuningOffsetCents = S.Instrument.MeasuredOffsetCents,
        };
        if (session is not null)
        {
            var offset = _sessionStart - _recordStart; // schedule time 0 in WAV seconds
            info.Notes = session.Schedule(S.Recorder, S.Instrument.ReferenceA)
                .Select(n => new TakeNote(n.Step, n.WrittenMidi, n.SoundingMidi, offset + n.Start, offset + n.End)).ToList();
        }
        WavFile.Write(Path.Combine(Services.RecordingsDir, name + ".wav"), samples, S.Audio.SampleRate);
        info.Save(Path.Combine(Services.RecordingsDir, name + ".json"));
        RefreshList();
        Select(name);
    }

    public override void _Process(double delta)
    {
        _meter.Db = S.Audio.LevelDb;
        var now = S.Audio.Now;
        if (_session is not null)
        {
            if (now >= _stopAt)
            {
                StopRecording(save: true);
                return;
            }
            Conduct(_session, (now - _sessionStart) / _session.SecondsPerBeat);
        }
        else if (_recording is not null)
            _view.Message = string.Format(Ui.T("Recording… {0:0.0} s"), _recording.Count / (double)S.Audio.SampleRate);

        if (!double.IsNaN(_playStartedAt) && _player.Playing)
            _view.Playhead = _player.GetPlaybackPosition();
    }

    /// <summary>
    /// Shows what to play at a given beat (negative during the count-in). During rests the next
    /// note is shown, so the fingers are ready when "Play!" comes.
    /// </summary>
    private void Conduct(GuidedSession session, double beat)
    {
        var cue = session.CueAt(beat);
        _beat.Text = beat < 0
            ? string.Format(Ui.T("Get ready… {0}"), (int)Math.Ceiling(-beat))
            : string.Format(Ui.T("Beat {0} of {1}"), Math.Min((int)beat + 1, (int)Math.Ceiling(session.TotalBeats)), (int)Math.Ceiling(session.TotalBeats));
        if (cue.Step is not { } step)
        {
            _now.Text = Ui.T(cue.Finished ? "Done!" : "");
            _next.Text = "";
            _diagram.Fingering = null;
            return;
        }
        var written = S.Recorder.BottomWrittenMidi + step;
        _now.Text = cue.Playing ? S.NoteName(written) : Ui.T("Get ready: ") + S.NoteName(written);
        _now.Modulate = cue.Playing ? Colors.White : new Color(1, 1, 1, 0.6f);
        _diagram.Fingering = S.FingeringFor(written);
        _diagram.Dimmed = false;
        _diagram.QueueRedraw();
        _next.Text = cue.Following is { } f ? Ui.T("then: ") + S.NoteName(S.Recorder.BottomWrittenMidi + f) : "";
    }

    // ---- takes -------------------------------------------------------------------------

    private void RefreshList()
    {
        _takes.Clear();
        foreach (var json in Directory.GetFiles(Services.RecordingsDir, "*.json").OrderByDescending(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(json);
            if (!File.Exists(Path.Combine(Services.RecordingsDir, name + ".wav")))
                continue;
            TakeInfo info;
            try { info = TakeInfo.Load(json); }
            catch (Exception) { continue; }
            var label = $"{(info.Starred ? "★ " : "")}{info.RecordedAt:dd MMM HH:mm} · {info.Session ?? Ui.T("free")} · {info.DurationSeconds:0}s";
            var ix = _takes.AddItem(label);
            _takes.SetItemMetadata(ix, name);
            if (name == _selected)
                _takes.Select(ix);
        }
    }

    private async void Select(string name)
    {
        _selected = name;
        _player.Stop();
        _view.Playhead = -1;
        for (var i = 0; i < _takes.ItemCount; i++)
            if ((string)_takes.GetItemMetadata(i) == name)
                _takes.Select(i);
        UpdateButtons();

        var info = TakeInfo.Load(Path.Combine(Services.RecordingsDir, name + ".json"));
        _summary.Text = Ui.T("Analysing…");
        var (samples, rate, frames, checks) = await Task.Run(() =>
        {
            var (s, r) = WavFile.Read(Path.Combine(Services.RecordingsDir, name + ".wav"));
            var recorder = Core.Instruments.RecorderInfo.Of(info.Instrument);
            var options = PitchTrackerOptions.ForRange(recorder.BottomSoundingMidi, recorder.TopSoundingMidi, info.ReferenceA);
            var f = PitchTracker.Analyse(s, r, options, info.GateRms);
            return (s, r, f, TakeVerifier.Verify(info, f));
        });
        if (_selected != name || !IsInsideTree())
            return; // the user moved on while we were analysing
        _view.Set(samples, rate, frames, checks, (info.LatencyMs ?? 0) / 1000);
        _player.Stream = Playback.ToStream(samples, rate);
        _summary.Text = checks.Count == 0
            ? string.Format(Ui.T("Free take, {0:0.0} s."), info.DurationSeconds)
            : Summary(checks);
        UpdateButtons();
    }

    private string Summary(IReadOnlyList<NoteCheck> checks)
    {
        var ok = checks.Count(c => c.Recognised);
        var text = string.Format(Ui.T("Recognised {0} of {1} notes."), ok, checks.Count);
        var missed = checks.Where(c => !c.Recognised).Take(6).Select(c =>
            S.NoteName(c.Expected.WrittenMidi) + " → " + (double.IsNaN(c.MedianMidi) ? Ui.T("not heard") : S.NoteName(S.Recorder.SoundingToWritten((int)Math.Round(c.MedianMidi)))));
        return ok == checks.Count ? text + " " + Ui.T("Pipit understands this recorder.") : text + "  " + string.Join(", ", missed);
    }

    private void TogglePlay()
    {
        if (_player.Playing)
        {
            _player.Stop();
            _view.Playhead = -1;
            _play.Text = Ui.T("▶ Play");
            return;
        }
        _player.Play();
        _playStartedAt = S.Audio.Now;
        _play.Text = Ui.T("■ Stop");
    }

    private void Delete()
    {
        if (_selected is null)
            return;
        foreach (var ext in new[] { ".wav", ".json" })
            File.Delete(Path.Combine(Services.RecordingsDir, _selected + ext));
        _selected = null;
        _player.Stop();
        _view.Clear();
        _summary.Text = "";
        RefreshList();
        UpdateButtons();
    }

    private void ToggleStar()
    {
        if (_selected is null)
            return;
        var path = Path.Combine(Services.RecordingsDir, _selected + ".json");
        var info = TakeInfo.Load(path);
        info.Starred = !info.Starred;
        info.Save(path);
        RefreshList();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var recording = _recording is not null;
        _record.Text = Ui.T(recording ? "■ Stop recording" : "● Record");
        _record.Disabled = _session is not null;
        _guided.Disabled = recording;
        _sessions.Disabled = recording;
        _speed.Editable = !recording;
        _play.Disabled = recording || _selected is null;
        _delete.Disabled = recording || _selected is null;
        _star.Disabled = recording || _selected is null;
    }
}
