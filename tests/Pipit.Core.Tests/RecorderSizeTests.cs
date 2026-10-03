using Pipit.Core.Instruments;
using Xunit;

namespace Pipit.Core.Tests;

public class RecorderSizeTests
{
    private static List<double> Steady(double midi, int frames = 30) => Enumerable.Range(0, frames).Select(i => midi + (i % 3 - 1) * 0.05).ToList();

    [Theory]
    [InlineData(72.0, RecorderKind.Soprano)]   // C5
    [InlineData(65.1, RecorderKind.Alto)]      // F4
    [InlineData(60.0, RecorderKind.Tenor)]     // C4
    [InlineData(71.0, RecorderKind.Soprano)]   // a soprano at A = 415
    [InlineData(64.2, RecorderKind.Alto)]      // an alto at A = 415, a bit flat
    public void The_lowest_note_tells_the_size(double heard, RecorderKind kind)
    {
        Assert.Equal(kind, RecorderSize.Guess(Steady(heard))!.Kind);
    }

    [Fact]
    public void A_note_far_from_every_lowest_note_is_not_a_guess()
    {
        // G4: a soprano with a leaking hole, or not every hole closed.
        var guess = RecorderSize.Guess(Steady(67.5))!;
        Assert.Null(guess.Kind);
        Assert.Equal(67.5, guess.HeardMidi, 1);
    }

    [Fact]
    public void Too_little_sound_or_a_few_stray_frames_do_not_decide()
    {
        Assert.Null(RecorderSize.Guess(Steady(72, frames: 5)));
        // A few octave jumps at the start do not move the median.
        var frames = Enumerable.Repeat(84.0, 4).Concat(Steady(65)).Append(double.NaN).ToList();
        Assert.Equal(RecorderKind.Alto, RecorderSize.Guess(frames)!.Kind);
    }
}
