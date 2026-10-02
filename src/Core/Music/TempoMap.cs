namespace Pipit.Core.Music;

/// <summary>
/// Beats to clock times for a run whose speed can change while it plays. Each change starts a
/// new segment at the beat where it happens, so everything before it keeps its time and only
/// what comes after moves. Before the first segment (the count-in) the first tempo applies.
/// </summary>
public sealed class TempoMap
{
    private readonly List<(double Beat, double Time, double SecondsPerBeat)> _segments = new();

    /// <param name="timeOfBeatZero">Clock time of beat 0.</param>
    public TempoMap(double timeOfBeatZero, double secondsPerBeat) => _segments.Add((0, timeOfBeatZero, secondsPerBeat));

    /// <summary>Seconds per beat of the latest segment.</summary>
    public double SecondsPerBeat => _segments[^1].SecondsPerBeat;

    public int Changes => _segments.Count - 1;

    public double BeatToTime(double beat)
    {
        var s = _segments[0];
        foreach (var seg in _segments)
            if (seg.Beat <= beat)
                s = seg;
        return s.Time + (beat - s.Beat) * s.SecondsPerBeat;
    }

    public double TimeToBeat(double time)
    {
        var s = _segments[0];
        foreach (var seg in _segments)
            if (seg.Time <= time)
                s = seg;
        return s.Beat + (time - s.Time) / s.SecondsPerBeat;
    }

    /// <summary>From <paramref name="beat"/> on, beats last <paramref name="secondsPerBeat"/>.</summary>
    public void Change(double beat, double secondsPerBeat)
    {
        var time = BeatToTime(beat);
        _segments.RemoveAll(s => s.Beat >= beat);
        // A change in the count-in (before beat 0) removes every segment: the new one then
        // rules the whole run, anchored where the change happened.
        _segments.Add((beat, time, secondsPerBeat));
    }
}
