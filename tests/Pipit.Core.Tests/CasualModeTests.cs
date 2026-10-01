using Pipit.Core.Analysis;
using Pipit.Core.Music;
using Pipit.Core.Practice;
using Xunit;

namespace Pipit.Core.Tests;

public class CasualModeTests
{
    private const int Rate = 48000;
    private static float[] Tone(int midi, double s) => Signals.Tone(Pitch.FrequencyOfMidi(midi), s, Rate);
    private static float[] Gap(double s) => Signals.Silence(s, Rate);

    private static CasualMode Run(int[] targets, float[] signal)
    {
        var mode = new CasualMode(targets);
        foreach (var f in PitchTracker.Analyse(signal, Rate))
            mode.Push(f);
        return mode;
    }

    [Fact]
    public void Waits_for_each_note_however_long_it_takes()
    {
        var mode = Run(new[] { 79, 81, 83 }, Signals.Concat(Gap(2), Tone(79, 0.4), Gap(3), Tone(81, 0.4), Gap(0.2), Tone(83, 0.4)));
        Assert.True(mode.Finished);
        Assert.All(mode.Steps, s => Assert.True(s.FirstTry));
        Assert.InRange(mode.Steps[1].SecondsToFind, 3, 3.6);
    }

    [Fact]
    public void Wrong_notes_do_not_advance_and_are_counted()
    {
        var mode = Run(new[] { 79, 81 }, Signals.Concat(Tone(79, 0.4), Gap(0.1), Tone(80, 0.4), Gap(0.1), Tone(83, 0.4), Gap(0.1), Tone(81, 0.4)));
        Assert.True(mode.Finished);
        Assert.Equal(2, mode.Steps[1].WrongTries);
        Assert.Equal(50, mode.Accuracy);
    }

    [Fact]
    public void A_repeated_note_needs_a_new_attack()
    {
        // One long Sol must not count for "Sol Sol".
        var mode = Run(new[] { 79, 79 }, Signals.Concat(Tone(79, 1.2)));
        Assert.Equal(1, mode.Index);
        mode = Run(new[] { 79, 79 }, Signals.Concat(Tone(79, 0.4), Gap(0.1), Tone(79, 0.4)));
        Assert.True(mode.Finished);
    }

    [Fact]
    public void A_blip_is_not_a_note() =>
        Assert.Equal(0, Run(new[] { 79 }, Signals.Concat(Tone(79, 0.08), Gap(0.3))).Index);
}
