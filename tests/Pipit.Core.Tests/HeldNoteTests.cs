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

    /// <summary>Frames of a note wobbling around <paramref name="midi"/> (±<paramref name="wobble"/> semitones).</summary>
    private static IEnumerable<PitchFrame> Wobbling(double midi, double wobble, double seconds)
    {
        for (var t = 0.0; t < seconds; t += 256.0 / 48000)
        {
            var m = midi + wobble * Math.Sin(t * 2 * Math.PI * 5);
            yield return new PitchFrame(t, 0, 0.9, 0.05, true, m, m);
        }
    }

    [Fact]
    public void A_sharp_wobbling_note_is_still_reported()
    {
        // Do (84) played 45 cents sharp with a ±10 cent wobble crosses the 84/85 boundary all the time.
        var tuned = new HeldNoteDetector(1.0) { TuningOffsetCents = 20 };
        Assert.Equal(84, Assert.Single(Wobbling(84.45, 0.1, 2).Select(tuned.Push).OfType<HeldNote>()).SoundingMidi);
        // Starting 55 cents sharp, it would read as Do sharp without the recorder's tuning offset.
        var plain = new HeldNoteDetector(1.0);
        Assert.Equal(85, Assert.Single(Wobbling(84.55, 0.05, 2).Select(plain.Push).OfType<HeldNote>()).SoundingMidi);
        var offset = new HeldNoteDetector(1.0) { TuningOffsetCents = 20 };
        Assert.Equal(84, Assert.Single(Wobbling(84.55, 0.05, 2).Select(offset.Push).OfType<HeldNote>()).SoundingMidi);
        // Even without the tuning offset, a note that starts on Do stays Do through the wobble.
        var sticky = new HeldNoteDetector(1.0);
        var frames = Wobbling(84.3, 0.0, 0.2).Concat(Wobbling(84.45, 0.1, 2).Select(f => f with { Time = f.Time + 0.2 }));
        Assert.Equal(84, Assert.Single(frames.Select(sticky.Push).OfType<HeldNote>()).SoundingMidi);
    }

    [Fact]
    public void A_clear_change_of_note_still_counts()
    {
        var detector = new HeldNoteDetector(0.4);
        var frames = Wobbling(84, 0, 0.6).Concat(Wobbling(86, 0, 0.6).Select(f => f with { Time = f.Time + 0.6 }));
        Assert.Equal(new[] { 84, 86 }, frames.Select(detector.Push).OfType<HeldNote>().Select(h => h.SoundingMidi));
    }
}
