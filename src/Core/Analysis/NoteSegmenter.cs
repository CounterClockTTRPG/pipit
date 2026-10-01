namespace Pipit.Core.Analysis;

/// <summary>A note as played: when it started and stopped, and its pitch (fractional MIDI, sounding).</summary>
public sealed record DetectedNote(double Onset, double Offset, double Midi, double StabilityCents, double PeakDb)
{
    public double Duration => Offset - Onset;
    public int Semitone => (int)Math.Round(Midi);
}

/// <summary>
/// Turns pitch frames into notes. A new note starts when
/// <list type="bullet">
/// <item>sound starts after silence (the tracker's gate),</item>
/// <item>the pitch settles on a different semitone (a slur), or</item>
/// <item>the volume dips and comes back while the pitch stays the same: tonguing a repeated
/// note ("du du du") makes a dip of 20-60 ms that may not reach silence.</item>
/// </list>
/// Onsets use the same convention as <see cref="Calibration.NoteOnsets"/> (the first voiced
/// frame), so the calibrated latency applies unchanged.
/// </summary>
public sealed class NoteSegmenter
{
    private readonly SegmenterOptions _o;
    private readonly List<PitchFrame> _current = new();
    private double _lastVoiced = double.NegativeInfinity;
    // Slur detection: a run of frames on another semitone.
    private int? _otherSemitone;
    private readonly List<PitchFrame> _other = new();
    // Tonguing detection: running peak and whether we are in a dip.
    private double _peakRms;
    private bool _inDip;
    private PitchFrame? _dipMinimum;

    public NoteSegmenter(SegmenterOptions? options = null) => _o = options ?? new SegmenterOptions();

    /// <summary>The note being played right now, if any (not yet finished).</summary>
    public DetectedNote? Current => _current.Count >= _o.MinFrames ? Summarise(_current) : null;

    /// <summary>Feeds one frame; returns the note that just finished, if any.</summary>
    public DetectedNote? Push(PitchFrame frame)
    {
        if (!frame.Voiced || double.IsNaN(frame.Midi))
        {
            // Short dropouts inside a note are tolerated.
            return frame.Time - _lastVoiced > _o.MaxGapSeconds ? Close() : null;
        }

        DetectedNote? finished = null;
        if (_current.Count > 0 && frame.Time - _lastVoiced > _o.MaxGapSeconds)
            finished = Close();
        _lastVoiced = frame.Time;

        if (_current.Count == 0)
        {
            Start(frame);
            return finished;
        }

        // Slur: enough consecutive frames clearly away from the note's own pitch start a new note
        // at the first of them. "Clearly" is measured from the note's pitch, not by rounding: a
        // note played 50 cents sharp drifts across the semitone boundary without being two notes.
        // The release is excluded too: as the breath stops, a recorder's pitch sags while the
        // level falls, which is not a slur. A real slur keeps the air (and level) up.
        var semitone = (int)Math.Round(frame.SmoothedMidi);
        if (Math.Abs(frame.SmoothedMidi - NoteMidi()) >= _o.SlurSemitones && frame.Rms >= _peakRms * _o.SlurMinLevel)
        {
            if (_otherSemitone != semitone)
            {
                _otherSemitone = semitone;
                _other.Clear();
            }
            _other.Add(frame);
            if (_other[^1].Time - _other[0].Time >= _o.SlurSeconds)
            {
                var moved = _other.ToList();
                finished = CloseKeeping(moved[0].Time) ?? finished;
                foreach (var f in moved)
                    Add(f);
            }
            return finished;
        }
        if (_other.Count > 0)
        {
            // A brief excursion (a glitch or a grace note): count it as part of the note.
            foreach (var f in _other)
                _current.Add(f);
            _other.Clear();
            _otherSemitone = null;
        }

        // Tonguing: a dip well below the note's level, then a recovery.
        if (frame.Rms < _peakRms * _o.DipRatio)
        {
            if (!_inDip || frame.Rms < _dipMinimum!.Value.Rms)
                _dipMinimum = frame;
            _inDip = true;
        }
        else if (_inDip && frame.Rms > _peakRms * _o.RecoverRatio)
        {
            _inDip = false;
            var splitAt = _dipMinimum!.Value.Time;
            if (splitAt - _current[0].Time >= _o.MinNoteSeconds)
            {
                var after = _current.Where(f => f.Time > splitAt).ToList();
                finished = CloseKeeping(splitAt);
                foreach (var f in after)
                    Add(f);
                Add(frame);
                return finished;
            }
        }
        Add(frame);
        return finished;
    }

    /// <summary>Ends the current note (call at the end of a take).</summary>
    public DetectedNote? Flush() => Close();

    public static List<DetectedNote> Segment(IEnumerable<PitchFrame> frames, SegmenterOptions? options = null)
    {
        var segmenter = new NoteSegmenter(options);
        var notes = new List<DetectedNote>();
        foreach (var f in frames)
            if (segmenter.Push(f) is { } n)
                notes.Add(n);
        if (segmenter.Flush() is { } last)
            notes.Add(last);
        return notes;
    }

    private void Start(PitchFrame frame)
    {
        _current.Clear();
        _peakRms = 0;
        _inDip = false;
        _dipMinimum = null;
        _other.Clear();
        _otherSemitone = null;
        Add(frame);
    }

    private void Add(PitchFrame frame)
    {
        _current.Add(frame);
        // The peak decays slowly so a gentle diminuendo is not mistaken for tonguing.
        _peakRms = Math.Max(frame.Rms, _peakRms * _o.PeakDecay);
    }

    private double NoteMidi() =>
        Stats.Median(_current.Skip(Math.Min(_current.Count - 1, _o.SettleFrames)).Select(f => f.SmoothedMidi));

    /// <summary>Closes the current note at <paramref name="end"/>; frames after it are dropped (the caller re-adds them).</summary>
    private DetectedNote? CloseKeeping(double end)
    {
        var kept = _current.Where(f => f.Time < end).ToList();
        _current.Clear();
        _current.AddRange(kept);
        var note = Close();
        _current.Clear();
        _peakRms = 0;
        _inDip = false;
        _dipMinimum = null;
        _other.Clear();
        _otherSemitone = null;
        return note;
    }

    private DetectedNote? Close()
    {
        var frames = _current.ToList();
        _current.Clear();
        _other.Clear();
        _otherSemitone = null;
        if (frames.Count < _o.MinFrames || frames[^1].Time - frames[0].Time < _o.MinNoteSeconds)
            return null;
        return Summarise(frames);
    }

    private DetectedNote Summarise(List<PitchFrame> frames)
    {
        var settled = frames.Skip(Math.Min(frames.Count - 1, _o.SettleFrames)).ToList();
        var midi = Stats.Median(settled.Select(f => f.Midi));
        var spread = Stats.MedianAbsoluteDeviation(settled.Select(f => f.Midi)) * 100;
        var peak = frames.Max(f => f.Db);
        return new DetectedNote(frames[0].Time, frames[^1].Time, midi, spread, peak);
    }
}

public sealed record SegmenterOptions
{
    /// <summary>Unvoiced time inside a note that does not end it.</summary>
    public double MaxGapSeconds { get; init; } = 0.025;
    /// <summary>How long the pitch must stay on a new semitone to count as a new (slurred) note.</summary>
    public double SlurSeconds { get; init; } = 0.035;
    /// <summary>How far from the note's own pitch (in semitones) a slurred note must be.</summary>
    public double SlurSemitones { get; init; } = 0.7;
    /// <summary>A slurred note must be at least this fraction of the current note's peak level.</summary>
    public double SlurMinLevel { get; init; } = 0.3;
    /// <summary>A tonguing dip: the level falls below this fraction of the note's peak...</summary>
    public double DipRatio { get; init; } = 0.35;
    /// <summary>...and comes back above this fraction.</summary>
    public double RecoverRatio { get; init; } = 0.6;
    public double PeakDecay { get; init; } = 0.995;
    public double MinNoteSeconds { get; init; } = 0.04;
    public int MinFrames { get; init; } = 4;
    /// <summary>Frames at the start of a note left out of its pitch (the attack wobbles).</summary>
    public int SettleFrames { get; init; } = 4;
}
