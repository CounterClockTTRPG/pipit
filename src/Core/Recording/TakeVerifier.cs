using Pipit.Core.Analysis;

namespace Pipit.Core.Recording;

public sealed record NoteCheck(TakeNote Expected, double MedianMidi, double VoicedFraction, bool Recognised, double TuningOffsetCents = 0)
{
    /// <summary>Deviation from the expected note after allowing for the recorder's own tuning.</summary>
    public double CentsOff => (MedianMidi - Expected.SoundingMidi) * 100 - TuningOffsetCents;
}

/// <summary>
/// Checks a guided take against its schedule: for every expected note, the middle half of its
/// time slot (shifted by the recorded latency) must be mostly voiced and its median pitch must
/// round to the expected note. The middle half tolerates late starts and early stops, which
/// the segmenter (M2) grades separately; this check is only about recognising the pitch. The
/// recorder's calibrated offset is subtracted first: a recorder that plays 20 cents sharp
/// overall should not have its sharp low notes counted as the semitone above.
/// </summary>
public static class TakeVerifier
{
    public const double MinVoicedFraction = 0.5;

    public static List<NoteCheck> Verify(TakeInfo take, IReadOnlyList<PitchFrame> frames)
    {
        var latency = (take.LatencyMs ?? 0) / 1000;
        var checks = new List<NoteCheck>();
        foreach (var note in take.Notes)
        {
            var length = note.End - note.Start;
            var from = note.Start + latency + length * 0.25;
            var to = note.End + latency - length * 0.25;
            var slot = frames.Where(f => f.Time >= from && f.Time <= to).ToList();
            var voiced = slot.Where(f => f.Voiced).ToList();
            var fraction = slot.Count == 0 ? 0 : (double)voiced.Count / slot.Count;
            var median = Stats.Median(voiced.Select(f => f.Midi));
            var corrected = median - take.TuningOffsetCents / 100;
            var ok = fraction >= MinVoicedFraction && !double.IsNaN(median) && (int)Math.Round(corrected) == note.SoundingMidi;
            checks.Add(new NoteCheck(note, median, fraction, ok, take.TuningOffsetCents));
        }
        return checks;
    }
}
