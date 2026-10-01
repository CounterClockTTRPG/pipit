using Pipit.Core.Music;

namespace Pipit.Core.Instruments;

public enum RecorderKind { Soprano, Alto, Tenor }

public enum FingeringSystem { Baroque, German }

/// <summary>
/// Range and transposition of a recorder size. Every size spans 26 semitones above its
/// bottom note, and fingering patterns are identical relative to that bottom note, so
/// "semitones above bottom" is the instrument-independent coordinate used everywhere.
/// </summary>
public sealed record RecorderInfo(RecorderKind Kind, Pitch BottomWritten, int OctaveTransposition)
{
    public const int RangeSemitones = 26;

    public static RecorderInfo Of(RecorderKind kind) => kind switch
    {
        RecorderKind.Soprano => new(kind, Pitch.Parse("C4"), 1), // sounds an octave above written
        RecorderKind.Alto => new(kind, Pitch.Parse("F4"), 0),
        RecorderKind.Tenor => new(kind, Pitch.Parse("C4"), 0),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public int BottomWrittenMidi => BottomWritten.Midi;
    public int TopWrittenMidi => BottomWrittenMidi + RangeSemitones;
    public int BottomSoundingMidi => BottomWrittenMidi + 12 * OctaveTransposition;
    public int TopSoundingMidi => TopWrittenMidi + 12 * OctaveTransposition;

    public int WrittenToSounding(int writtenMidi) => writtenMidi + 12 * OctaveTransposition;
    public int SoundingToWritten(int soundingMidi) => soundingMidi - 12 * OctaveTransposition;

    /// <summary>Semitones above the bottom note for a written pitch, or null when out of range.</summary>
    public int? StepOf(int writtenMidi)
    {
        var step = writtenMidi - BottomWrittenMidi;
        return step is >= 0 and <= RangeSemitones ? step : null;
    }

    public bool InRange(int writtenMidi) => StepOf(writtenMidi) is not null;
}
