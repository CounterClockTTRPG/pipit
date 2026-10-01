namespace Pipit.Core.Analysis;

public enum MicCheckVerdict
{
    /// <summary>The test tone was heard and stayed audible: steady notes will reach Pipit.</summary>
    Ok,
    /// <summary>Heard at the start, then faded away: Windows voice processing is removing steady tones.</summary>
    Suppressed,
    /// <summary>Not heard at all: headphones on, speakers muted, or the input fully cancels the app's own sound.</summary>
    NotHeard,
}

public sealed record MicCheckResult(MicCheckVerdict Verdict, double FloorDb, double OnsetDb, double SustainDb);

/// <summary>
/// Judges a speaker-to-microphone test tone from short RMS blocks. Voice processing on laptop
/// microphone arrays (noise suppression, echo cancellation) lets the start of a steady tone
/// through and then removes it; a recorder note is exactly such a tone.
/// </summary>
public static class MicCheck
{
    public static MicCheckResult Evaluate(IReadOnlyList<(double Time, double Rms)> blocks, double toneStart, double toneEnd)
    {
        static double Db(double rms) => 20 * Math.Log10(rms + 1e-9);
        var before = blocks.Where(b => b.Time < toneStart - 0.05).Select(b => Db(b.Rms)).ToList();
        var floor = before.Count > 0 ? Stats.Median(before) : -120;
        // The tone reaches the mic after the output and input latency, typically under 300 ms.
        var onset = blocks.Where(b => b.Time >= toneStart && b.Time < toneStart + 0.4).Select(b => Db(b.Rms)).DefaultIfEmpty(-180).Max();
        var sustain = Stats.Median(blocks.Where(b => b.Time >= toneStart + 0.6 && b.Time < toneEnd - 0.05).Select(b => Db(b.Rms)));
        if (double.IsNaN(sustain))
            sustain = -180;

        var heard = onset > Math.Max(floor + 15, -70);
        var verdict = !heard ? MicCheckVerdict.NotHeard
            : sustain < onset - 15 ? MicCheckVerdict.Suppressed
            : MicCheckVerdict.Ok;
        return new MicCheckResult(verdict, floor, onset, sustain);
    }
}
