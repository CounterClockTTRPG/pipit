using System;
using System.Collections.Generic;
using System.Linq;
using Pipit.Core.Analysis;
using Pipit.Core.Music;
using Pipit.Core.Scoring;

namespace Pipit.App.Audio;

/// <summary>
/// One play-along run of a song in rhythm: count-in and clicks, the expected notes on the capture
/// clock, live segmentation and grading. Used by lesson exercises (the play screen has its own,
/// richer version with recording and results).
/// </summary>
public sealed class RhythmRunner
{
    private readonly Song _song;
    private readonly Tolerances _tolerances;
    private readonly NoteSegmenter _segmenter = new();
    private readonly List<DetectedNote> _detected = new();
    private readonly List<ExpectedNote> _expected;
    private readonly double _t0, _spb, _latency, _end;

    public bool Finished { get; private set; }
    public GradedRun? Result { get; private set; }

    public RhythmRunner(Song song, Tolerances tolerances, bool metronome = true)
    {
        var s = Services.Instance;
        _song = song;
        _tolerances = tolerances;
        _spb = song.SecondsPerBeat();
        _latency = (s.DeviceLatencyMs ?? 0) / 1000;
        var notes = song.Melody.Performance;
        var first = notes.Min(n => n.StartBeat);
        var barBeats = (int)Math.Round(song.Time.BarBeats);
        var firstClick = Math.Floor(first) - barBeats;
        _t0 = s.Audio.Now + 0.8 - firstClick * _spb;
        var endBeat = notes.Max(n => n.EndBeat);
        var lastClick = metronome ? Math.Ceiling(endBeat) - 1 : Math.Floor(first) - 1;
        var clicks = new List<(double, bool)>();
        for (var b = firstClick; b <= lastClick; b++)
            clicks.Add((BeatToTime(b), ((int)b % barBeats + barBeats) % barBeats == 0));
        s.Clicks.Schedule(clicks);
        _expected = notes.Select(n => new ExpectedNote(n.Index, s.Recorder.WrittenToSounding(n.Pitch.Midi), BeatToTime(n.StartBeat), BeatToTime(n.EndBeat))).ToList();
        _end = BeatToTime(endBeat) + _latency + 0.6;
    }

    public double BeatToTime(double beat) => _t0 + beat * _spb;
    public double TimeToBeat(double time) => (time - _t0) / _spb;
    public double Latency => _latency;
    /// <summary>Time of the first note; before it the count-in is running.</summary>
    public double FirstNoteTime => BeatToTime(_song.Melody.Performance.Min(n => n.StartBeat));
    public double SecondsPerBeat => _spb;

    public void Push(PitchFrame frame)
    {
        if (!Finished && _segmenter.Push(frame) is { } note)
            _detected.Add(note);
    }

    /// <summary>Grades what is decidable so far; finishes after the last note.</summary>
    public GradedRun Update(double now)
    {
        var offset = Services.Instance.Instrument.MeasuredOffsetCents;
        if (!Finished && now >= _end)
        {
            if (_segmenter.Flush() is { } last)
                _detected.Add(last);
            Finished = true;
            Result = Grader.Grade(_expected, _detected, _tolerances, _latency, offset);
            Services.Instance.Clicks.Stop();
            return Result;
        }
        var live = _detected.ToList();
        if (_segmenter.Current is { } current)
            live.Add(current);
        return Grader.Grade(_expected, live, _tolerances, _latency, offset, until: now);
    }

    public void Stop()
    {
        Finished = true;
        Services.Instance.Clicks.Stop();
    }
}
