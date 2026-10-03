namespace Pipit.Core.Instruments;

/// <summary>What the lowest note told about the recorder: its size, or null when it was not clear.</summary>
public sealed record SizeGuess(RecorderKind? Kind, double HeardMidi, double CentsFromBottom);

/// <summary>
/// "Which recorder do I have?" With every hole closed a recorder plays its lowest note:
/// soprano C5, alto F4, tenor C4 (sounding). Their sizes are a fourth or a fifth apart, so the
/// note heard tells them apart even on a recorder at baroque pitch (A = 415, a semitone low).
/// </summary>
public static class RecorderSize
{
    /// <summary>How far (in semitones) the heard note may be from a recorder's lowest note.</summary>
    public const double Tolerance = 1.5;

    /// <summary>Voiced pitches (sounding MIDI) needed before a guess is made.</summary>
    public const int MinimumFrames = 15;

    /// <summary>The size whose lowest note is nearest the median of <paramref name="midis"/>; Kind is null if none is close.</summary>
    public static SizeGuess? Guess(IReadOnlyList<double> midis)
    {
        var voiced = midis.Where(m => !double.IsNaN(m)).OrderBy(m => m).ToList();
        if (voiced.Count < MinimumFrames)
            return null;
        var median = voiced[voiced.Count / 2];
        var (kind, bottom) = Enum.GetValues<RecorderKind>()
            .Select(k => (k, (double)RecorderInfo.Of(k).BottomSoundingMidi))
            .MinBy(x => Math.Abs(x.Item2 - median));
        var off = median - bottom;
        return new SizeGuess(Math.Abs(off) <= Tolerance ? kind : null, median, off * 100);
    }
}
