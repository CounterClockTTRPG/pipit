using Pipit.Core.Analysis;
using Xunit;

namespace Pipit.Core.Tests;

public class MicCheckTests
{
    /// <summary>5 ms blocks from 0 to 3 s; the tone plays 1-2.5 s and arrives 0.1 s later.</summary>
    private static List<(double, double)> Blocks(Func<double, double> rmsAt) =>
        Enumerable.Range(0, 600).Select(i => i * 0.005).Select(t => (t, rmsAt(t))).ToList();

    [Fact]
    public void Steady_tone_is_ok() =>
        Assert.Equal(MicCheckVerdict.Ok, MicCheck.Evaluate(Blocks(t => t is > 1.1 and < 2.6 ? 0.05 : 0.0005), 1, 2.5).Verdict);

    [Fact]
    public void Tone_that_fades_to_silence_is_suppressed()
    {
        // As measured on a Realtek microphone array: ~150 ms of tone, then digital zeros.
        var result = MicCheck.Evaluate(Blocks(t => t is > 1.1 and < 1.25 ? 0.05 : 0), 1, 2.5);
        Assert.Equal(MicCheckVerdict.Suppressed, result.Verdict);
    }

    [Fact]
    public void Nothing_heard() =>
        Assert.Equal(MicCheckVerdict.NotHeard, MicCheck.Evaluate(Blocks(_ => 0.0005), 1, 2.5).Verdict);
}
