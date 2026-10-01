namespace Pipit.Core.Analysis;

public sealed record LatencyResult(double LatencyMs, double SpreadMs, int Used, int Total);

public sealed record NoteTuning(int ExpectedMidi, double MedianMidi, double CentsOff, bool Accepted);

/// <summary>Pure calculations behind the calibration wizard.</summary>
public static class Calibration
{
    /// <summary>
    /// The noise gate from a stretch of silence: a little above the loudest typical frame,
    /// never below about -54 dBFS (digital silence would otherwise open the gate on hiss).
    /// </summary>
    public static double GateFromSilence(IEnumerable<PitchFrame> silentFrames)
    {
        var p90 = Stats.Percentile(silentFrames.Select(f => f.Rms), 0.9);
        return double.IsNaN(p90) ? 0.003 : Math.Max(p90 * 2.5, 0.002);
    }

    /// <summary>
    /// Times at which a note starts: an unvoiced→voiced transition that stays voiced for
    /// <paramref name="minVoicedFrames"/> frames. A click is broadband and short, so the
    /// clarity requirement in the tracker keeps metronome clicks out of this list.
    /// </summary>
    public static List<double> NoteOnsets(IReadOnlyList<PitchFrame> frames, int minVoicedFrames = 4)
    {
        var onsets = new List<double>();
        var previousVoiced = false;
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames[i].Voiced && !previousVoiced)
            {
                var run = 0;
                while (i + run < frames.Count && frames[i + run].Voiced)
                    run++;
                if (run >= minVoicedFrames)
                    onsets.Add(frames[i].Time);
            }
            previousVoiced = frames[i].Voiced;
        }
        return onsets;
    }

    /// <summary>
    /// The player's total offset (input + output latency + reaction) from notes played on
    /// clicks: each click is paired with the first onset within its window, and the median of
    /// the differences is the latency. Needs at least half of the clicks answered.
    /// </summary>
    public static LatencyResult? Latency(IReadOnlyList<double> clickTimes, IReadOnlyList<double> onsets, double windowBefore = 0.25, double windowAfter = 0.6)
    {
        var offsets = new List<double>();
        foreach (var click in clickTimes)
        {
            var match = onsets.Where(o => o >= click - windowBefore && o <= click + windowAfter)
                              .OrderBy(o => o).Cast<double?>().FirstOrDefault();
            if (match is { } m)
                offsets.Add(m - click);
        }
        if (offsets.Count < Math.Max(3, clickTimes.Count / 2))
            return null;
        return new LatencyResult(Stats.Median(offsets) * 1000, Stats.MedianAbsoluteDeviation(offsets) * 1000, offsets.Count, clickTimes.Count);
    }

    /// <summary>
    /// Tuning of one calibrated note from its voiced frames. Readings more than 50 cents from
    /// the expected note are rejected: that is a different note (a wrong fingering), and
    /// calibration must not learn it as "this instrument's Fa is 80 cents flat".
    /// </summary>
    public static NoteTuning TuneNote(int expectedMidi, IEnumerable<PitchFrame> frames)
    {
        var median = Stats.Median(frames.Where(f => f.Voiced).Select(f => f.Midi));
        if (double.IsNaN(median))
            return new NoteTuning(expectedMidi, double.NaN, double.NaN, false);
        var cents = (median - expectedMidi) * 100;
        return new NoteTuning(expectedMidi, median, cents, Math.Abs(cents) <= 50);
    }
}
