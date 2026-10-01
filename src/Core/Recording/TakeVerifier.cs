using Pipit.Core.Analysis;

namespace Pipit.Core.Recording;

public sealed record NoteCheck(TakeNote Expected, double MedianMidi, double VoicedFraction, bool Recognised)
{
    public double CentsOff => (MedianMidi - Expected.SoundingMidi) * 100;
}

/// <summary>
/// Checks a guided take against its schedule: for every expected note, the middle half of its
/// time slot (shifted by the recorded latency) must be mostly voiced and its median pitch must
/// round to the expected note. The middle half tolerates late starts and early stops, which
/// the segmenter (M2) grades separately; this check is only about recognising the pitch.
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
            var ok = fraction >= MinVoicedFraction && !double.IsNaN(median) && (int)Math.Round(median) == note.SoundingMidi;
            checks.Add(new NoteCheck(note, median, fraction, ok));
        }
        return checks;
    }
}
