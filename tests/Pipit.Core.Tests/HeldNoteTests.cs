using Pipit.Core.Analysis;
using Pipit.Core.Music;
using Xunit;

namespace Pipit.Core.Tests;

public class HeldNoteTests
{
    private static List<HeldNote> Run(float[] signal, double hold = 0.35)
    {
        var detector = new HeldNoteDetector(hold);
        return PitchTracker.Analyse(signal, 48000).Select(detector.Push).OfType<HeldNote>().ToList();
    }

    [Fact]
    public void Reports_a_held_note_once()
    {
        var held = Run(Signals.Tone(Pitch.FrequencyOfMidi(79), 1.5, 48000));
        Assert.Single(held);
        Assert.Equal(79, held[0].SoundingMidi);
        Assert.InRange(held[0].CentsOff, -3, 3);
    }

    [Fact]
    public void Short_blips_are_not_reported() =>
        Assert.Empty(Run(Signals.Tone(Pitch.FrequencyOfMidi(79), 0.25, 48000)));

    [Fact]
    public void Each_new_note_is_reported()
    {
        var signal = Signals.Concat(
            Signals.Tone(Pitch.FrequencyOfMidi(79), 0.6, 48000),
            Signals.Tone(Pitch.FrequencyOfMidi(81), 0.6, 48000),
            Signals.Silence(0.2, 48000),
            Signals.Tone(Pitch.FrequencyOfMidi(81), 0.6, 48000));
        Assert.Equal(new[] { 79, 81, 81 }, Run(signal).Select(h => h.SoundingMidi));
    }
}
