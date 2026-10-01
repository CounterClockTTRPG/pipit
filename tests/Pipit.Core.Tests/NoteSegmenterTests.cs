using System.Globalization;
using Pipit.Core.Analysis;
using Pipit.Core.Instruments;
using Pipit.Core.Music;
using Pipit.Core.Recording;
using Xunit;

namespace Pipit.Core.Tests;

public class NoteSegmenterTests
{
    private const int Rate = 48000;

    private static List<DetectedNote> Segment(float[] signal) =>
        NoteSegmenter.Segment(PitchTracker.Analyse(signal, Rate));

    private static float[] Tone(int midi, double seconds, double cents = 0) =>
        Signals.Tone(Pitch.FrequencyOfMidi(midi + cents / 100), seconds, Rate);

    [Fact]
    public void Separate_notes_with_silence_between()
    {
        var notes = Segment(Signals.Concat(Tone(79, 0.4), Signals.Silence(0.1, Rate), Tone(81, 0.4), Signals.Silence(0.1, Rate), Tone(83, 0.4)));
        Assert.Equal(new[] { 79, 81, 83 }, notes.Select(n => n.Semitone));
        Assert.InRange(notes[1].Onset, 0.49, 0.53);
        Assert.InRange(notes[1].Duration, 0.33, 0.42);
    }

    [Fact]
    public void Slurred_notes_split_on_the_pitch_change()
    {
        // No gap at all: the fingers change, the air keeps going.
        var notes = Segment(Signals.Concat(Tone(79, 0.4), Tone(81, 0.4), Tone(83, 0.4)));
        Assert.Equal(new[] { 79, 81, 83 }, notes.Select(n => n.Semitone));
        Assert.InRange(notes[1].Onset, 0.39, 0.45);
        Assert.InRange(notes[2].Onset, 0.79, 0.85);
    }

    [Fact]
    public void Tongued_repeats_split_even_without_silence()
    {
        // Same pitch, a 40 ms dip to 15 % level between notes (a "du" that never reaches silence).
        var one = Tone(79, 0.3);
        var signal = Signals.Concat(one, one, one, one);
        for (var n = 1; n < 4; n++)
        {
            var center = n * one.Length;
            for (var i = -Rate / 50; i < Rate / 50; i++)
                signal[center + i] *= 0.15f;
        }
        var notes = Segment(signal);
        Assert.Equal(4, notes.Count);
        Assert.All(notes, n => Assert.Equal(79, n.Semitone));
    }

    [Fact]
    public void Vibrato_and_a_glitch_do_not_split_a_note()
    {
        // ±25 cent vibrato at 5 Hz, and a 15 ms blip a semitone up in the middle.
        var n = (int)(1.0 * Rate);
        var signal = new float[n];
        var phase = 0.0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / Rate;
            var midi = 79 + 0.25 * Math.Sin(2 * Math.PI * 5 * t) + (t is > 0.5 and < 0.515 ? 1 : 0);
            phase += 2 * Math.PI * Pitch.FrequencyOfMidi(midi) / Rate;
            signal[i] = (float)(0.3 * Math.Sin(phase));
        }
        var notes = Segment(signal);
        var note = Assert.Single(notes);
        Assert.Equal(79, note.Semitone);
    }

    [Fact]
    public void A_sharp_note_drifting_across_the_semitone_boundary_stays_one_note()
    {
        // Sol played 40-60 cents sharp, as on the user's recorder: rounding flips between 79 and 80.
        var n = (int)(0.8 * Rate);
        var signal = new float[n];
        var phase = 0.0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / Rate;
            phase += 2 * Math.PI * Pitch.FrequencyOfMidi(79.5 + 0.1 * Math.Sin(2 * Math.PI * 3 * t)) / Rate;
            signal[i] = (float)(0.3 * Math.Sin(phase));
        }
        Assert.Single(Segment(signal));
    }

    [Fact]
    public void Pitch_sagging_in_the_release_is_not_a_new_note()
    {
        // 0.4 s of Sol, then a 150 ms fade during which the pitch drops 80 cents.
        var n = (int)(0.55 * Rate);
        var signal = new float[n];
        var phase = 0.0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / Rate;
            var fade = t < 0.4 ? 0.0 : (t - 0.4) / 0.15;
            phase += 2 * Math.PI * Pitch.FrequencyOfMidi(79 - 0.8 * fade) / Rate;
            signal[i] = (float)(0.3 * Math.Pow(1 - 0.97 * fade, 2) * Math.Sin(phase));
        }
        Assert.Single(Segment(signal));
    }

    [Fact]
    public void Too_short_to_be_a_note() =>
        Assert.Empty(Segment(Signals.Concat(Signals.Silence(0.2, Rate), Tone(79, 0.02), Signals.Silence(0.2, Rate))));

    [Fact]
    public void Live_current_note_is_reported_before_it_ends()
    {
        var segmenter = new NoteSegmenter();
        foreach (var f in PitchTracker.Analyse(Tone(81, 0.3), Rate))
            segmenter.Push(f);
        Assert.Equal(81, segmenter.Current?.Semitone);
    }

    /// <summary>The user's real calibration take: eight tongued Sols on the clicks, built-in laptop mic.</summary>
    [Fact]
    public void Real_tongued_notes_from_the_calibration_take()
    {
        var dir = Path.Combine(Repo.Root, "tests", "fixtures", "calibration");
        var (samples, rate) = WavFile.Read(Path.Combine(dir, "latency-soprano-builtin-mic.wav"));
        var meta = File.ReadAllLines(Path.Combine(dir, "latency-soprano-builtin-mic.txt")).Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var gate = double.Parse(meta["gate"].Replace(',', '.'), CultureInfo.InvariantCulture);
        var clicks = meta["clicks"].Split(',').Select(c => double.Parse(c, CultureInfo.InvariantCulture)).ToList();
        var info = RecorderInfo.Of(RecorderKind.Soprano);
        var frames = PitchTracker.Analyse(samples, rate, PitchTrackerOptions.ForRange(info.BottomSoundingMidi, info.TopSoundingMidi), gate);

        var notes = NoteSegmenter.Segment(frames).Where(n => n.Semitone is 79 or 80).ToList();
        // Exactly one note answering each click.
        foreach (var click in clicks)
            Assert.Single(notes, n => n.Onset > click - 0.25 && n.Onset < click + 0.5);
    }
}
