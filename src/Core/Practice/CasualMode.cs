using Pipit.Core.Analysis;

namespace Pipit.Core.Practice;

public sealed record CasualStep(int Index, int SoundingMidi, double SecondsToFind, int WrongTries)
{
    public bool FirstTry => WrongTries == 0;
}

/// <summary>
/// Casual (wait) mode: the song waits on each note until it is played, with no rhythm or timer.
/// A note counts once it has sounded on the right semitone for <see cref="HoldSeconds"/>. Each
/// played note is judged once, so a repeated note needs a fresh tongue ("du") to count twice.
/// </summary>
public sealed class CasualMode
{
    private readonly IReadOnlyList<int> _targets;
    private readonly NoteSegmenter _segmenter = new();
    private readonly double _tuningOffsetSemitones;
    private double _judgedOnset = double.NaN;
    private double _waitingSince = double.NaN;
    private int _wrongTries;
    private readonly List<CasualStep> _steps = new();

    public double HoldSeconds { get; init; } = 0.15;
    public int Index { get; private set; }
    public bool Finished => Index >= _targets.Count;
    public IReadOnlyList<CasualStep> Steps => _steps;
    /// <summary>The wrong note just played (sounding MIDI), for feedback; cleared when the right one comes.</summary>
    public int? LastWrong { get; private set; }

    public CasualMode(IReadOnlyList<int> soundingTargets, double tuningOffsetCents = 0)
    {
        _targets = soundingTargets;
        _tuningOffsetSemitones = tuningOffsetCents / 100;
    }

    public int? Target => Finished ? null : _targets[Index];

    /// <summary>Feeds a frame; returns the step completed by it, if any.</summary>
    public CasualStep? Push(PitchFrame frame)
    {
        if (double.IsNaN(_waitingSince))
            _waitingSince = frame.Time;
        _segmenter.Push(frame);
        if (Finished || _segmenter.Current is not { } note || note.Onset == _judgedOnset || note.Duration < HoldSeconds)
            return null;

        _judgedOnset = note.Onset;
        var played = (int)Math.Round(note.Midi - _tuningOffsetSemitones);
        if (played != _targets[Index])
        {
            _wrongTries++;
            LastWrong = played;
            return null;
        }
        var step = new CasualStep(Index, played, frame.Time - _waitingSince, _wrongTries);
        _steps.Add(step);
        Index++;
        _wrongTries = 0;
        LastWrong = null;
        _waitingSince = frame.Time;
        return step;
    }

    /// <summary>Notes found at the first try, as a percentage.</summary>
    public double Accuracy => _steps.Count == 0 ? 0 : 100.0 * _steps.Count(s => s.FirstTry) / _steps.Count;
}
