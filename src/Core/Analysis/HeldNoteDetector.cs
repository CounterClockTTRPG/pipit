namespace Pipit.Core.Analysis;

public sealed record HeldNote(int SoundingMidi, double Start, IReadOnlyList<PitchFrame> Frames)
{
    public double MedianMidi => Stats.Median(Frames.Select(f => f.Midi));
    public double CentsOff => (MedianMidi - SoundingMidi) * 100;
}

/// <summary>
/// Reports a note once it has been held steadily for <see cref="HoldSeconds"/>: voiced, and on
/// the same semitone throughout. Reports each held note once; a new report needs a different
/// note or a break in the sound. Short dropouts (a few frames) do not count as a break.
/// </summary>
public sealed class HeldNoteDetector(double holdSeconds = 0.35, double settleSeconds = 0.08, double maxGapSeconds = 0.05)
{
    private readonly List<PitchFrame> _frames = new();
    private int? _note;
    private double _start;
    private double _lastVoiced = double.NegativeInfinity;
    private bool _reported;

    public double HoldSeconds { get; } = holdSeconds;

    /// <summary>The semitone currently being held (not necessarily long enough yet).</summary>
    public int? Current => _note;

    public HeldNote? Push(PitchFrame frame)
    {
        if (!frame.Voiced || double.IsNaN(frame.SmoothedMidi))
        {
            if (frame.Time - _lastVoiced > maxGapSeconds)
                Reset();
            return null;
        }
        _lastVoiced = frame.Time;
        var note = (int)Math.Round(frame.SmoothedMidi);
        if (note != _note)
        {
            Reset();
            _note = note;
            _start = frame.Time;
        }
        // The attack is often unsteady; only frames after it count towards the median.
        if (frame.Time - _start >= settleSeconds)
            _frames.Add(frame);
        if (_reported || frame.Time - _start < HoldSeconds)
            return null;
        _reported = true;
        return new HeldNote(note, _start, _frames.ToList());
    }

    public void Reset()
    {
        _note = null;
        _frames.Clear();
        _reported = false;
    }
}
