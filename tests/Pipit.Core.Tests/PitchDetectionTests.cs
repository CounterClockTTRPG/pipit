using Pipit.Core.Analysis;
using Pipit.Core.Instruments;
using Pipit.Core.Music;
using Xunit;

namespace Pipit.Core.Tests;

public class PitchDetectionTests
{
    public static IEnumerable<object[]> RecorderNotes()
    {
        // Every semitone a tenor, alto or soprano can sound (C4..D7), at both common mix rates.
        foreach (var rate in new[] { 44100, 48000 })
            for (var midi = 60; midi <= 98; midi++)
                yield return new object[] { rate, midi };
    }

    [Theory]
    [MemberData(nameof(RecorderNotes))]
    public void Detects_every_recorder_note_within_three_cents(int rate, int midi)
    {
        var hz = Pitch.FrequencyOfMidi(midi);
        var frames = PitchTracker.Analyse(Signals.Tone(hz, 0.5, rate), rate);
        var voiced = frames.Where(f => f.Voiced).ToList();
        Assert.True(voiced.Count > frames.Count * 0.8, $"only {voiced.Count}/{frames.Count} frames voiced");
        var cents = Stats.Median(voiced.Select(f => (f.Midi - midi) * 100));
        Assert.InRange(cents, -3, 3);
    }

    [Theory]
    [InlineData(72)]
    [InlineData(84)]
    public void Detuning_is_measured_in_cents(int midi)
    {
        foreach (var detune in new[] { -30.0, -12.0, 8.0, 25.0 })
        {
            var hz = Pitch.FrequencyOfMidi(midi + detune / 100);
            var frames = PitchTracker.Analyse(Signals.Tone(hz, 0.4, 48000), 48000);
            var cents = Stats.Median(frames.Where(f => f.Voiced).Select(f => (f.Midi - midi) * 100));
            Assert.InRange(cents, detune - 2, detune + 2);
        }
    }

    [Fact]
    public void Strong_second_harmonic_does_not_cause_an_octave_error()
    {
        // Overblowing-like spectrum: the octave is almost as loud as the fundamental.
        var frames = PitchTracker.Analyse(Signals.Tone(523.25, 0.5, 48000, harmonics: new[] { 1.0, 0.8, 0.3 }), 48000);
        Assert.Equal(72, (int)Math.Round(Stats.Median(frames.Where(f => f.Voiced).Select(f => f.Midi))));
    }

    [Fact]
    public void Breath_noise_does_not_stop_detection()
    {
        var frames = PitchTracker.Analyse(Signals.Tone(659.26, 0.5, 48000, noise: 0.03), 48000);
        var voiced = frames.Where(f => f.Voiced).ToList();
        Assert.True(voiced.Count > frames.Count * 0.8);
        Assert.Equal(76, (int)Math.Round(Stats.Median(voiced.Select(f => f.Midi))));
    }

    [Fact]
    public void Silence_and_hiss_are_unvoiced()
    {
        Assert.DoesNotContain(PitchTracker.Analyse(Signals.Silence(0.5, 48000), 48000), f => f.Voiced);
        Assert.DoesNotContain(PitchTracker.Analyse(Signals.Silence(0.5, 48000, noise: 0.001), 48000), f => f.Voiced);
    }

    [Fact]
    public void Loud_white_noise_is_unvoiced()
    {
        var frames = PitchTracker.Analyse(Signals.Silence(0.5, 48000, noise: 0.3), 48000);
        Assert.True(frames.Count(f => f.Voiced) <= frames.Count / 20, $"{frames.Count(f => f.Voiced)} noise frames voiced");
    }

    [Fact]
    public void Chunked_pushes_give_the_same_frames_as_one_pass()
    {
        var signal = Signals.Concat(Signals.Silence(0.2, 48000), Signals.Tone(784, 0.3, 48000), Signals.Tone(880, 0.3, 48000));
        var whole = PitchTracker.Analyse(signal, 48000);

        var tracker = new PitchTracker(48000);
        var chunked = new List<PitchFrame>();
        var rng = new Random(5);
        for (var i = 0; i < signal.Length;)
        {
            var n = Math.Min(rng.Next(1, 3000), signal.Length - i);
            tracker.Push(signal.AsSpan(i, n), chunked);
            i += n;
        }
        Assert.Equal(whole, chunked);
    }

    [Fact]
    public void Frame_times_follow_the_signal()
    {
        var frames = PitchTracker.Analyse(Signals.Concat(Signals.Silence(1.0, 48000), Signals.Tone(880, 0.5, 48000)), 48000);
        var first = frames.First(f => f.Voiced).Time;
        Assert.InRange(first, 0.98, 1.03);
    }

    [Fact]
    public void Smoothing_resets_after_silence()
    {
        var signal = Signals.Concat(Signals.Tone(523.25, 0.3, 48000), Signals.Silence(0.1, 48000), Signals.Tone(1046.5, 0.3, 48000));
        var frames = PitchTracker.Analyse(signal, 48000);
        var firstOfSecond = frames.First(f => f.Voiced && f.Time > 0.4);
        Assert.Equal(84, (int)Math.Round(firstOfSecond.SmoothedMidi));
    }

    [Fact]
    public void Range_options_cover_each_recorder()
    {
        foreach (var kind in Enum.GetValues<RecorderKind>())
        {
            var info = RecorderInfo.Of(kind);
            var options = PitchTrackerOptions.ForRange(info.BottomSoundingMidi, info.TopSoundingMidi);
            foreach (var midi in new[] { info.BottomSoundingMidi, info.TopSoundingMidi })
            {
                var frames = PitchTracker.Analyse(Signals.Tone(Pitch.FrequencyOfMidi(midi), 0.3, 48000), 48000, options);
                Assert.Equal(midi, (int)Math.Round(Stats.Median(frames.Where(f => f.Voiced).Select(f => f.Midi))));
            }
        }
    }

    [Fact]
    public void Reference_a_shifts_midi()
    {
        var options = new PitchTrackerOptions { ReferenceA = 442 };
        var frames = PitchTracker.Analyse(Signals.Tone(442, 0.3, 48000), 48000, options);
        Assert.InRange(Stats.Median(frames.Where(f => f.Voiced).Select(f => f.Midi)), 68.98, 69.02);
    }
}

public class CalibrationTests
{
    [Fact]
    public void Gate_sits_above_room_noise()
    {
        var silent = PitchTracker.Analyse(Signals.Silence(1, 48000, noise: 0.004), 48000, gateRms: 0);
        var gate = Calibration.GateFromSilence(silent);
        var noiseRms = Stats.Median(silent.Select(f => f.Rms));
        Assert.True(gate > noiseRms * 2, $"gate {gate} vs noise {noiseRms}");
        Assert.True(gate < 0.05);
    }

    [Fact]
    public void Clicks_are_not_onsets_but_notes_are()
    {
        const int rate = 48000;
        var signal = Signals.Silence(4, rate, noise: 0.001);
        for (var beat = 0; beat < 4; beat++)
            Signals.MixInto(signal, Signals.Click(rate), beat * rate);
        // Notes answering beats 1 and 3, 90 ms late.
        Signals.MixInto(signal, Signals.Tone(784, 0.3, rate), (int)(1.09 * rate));
        Signals.MixInto(signal, Signals.Tone(784, 0.3, rate), (int)(3.09 * rate));

        var onsets = Calibration.NoteOnsets(PitchTracker.Analyse(signal, rate));
        Assert.Equal(2, onsets.Count);
        Assert.InRange(onsets[0], 1.07, 1.12);
        Assert.InRange(onsets[1], 3.07, 3.12);
    }

    [Fact]
    public void Latency_is_the_median_offset()
    {
        var clicks = Enumerable.Range(0, 8).Select(i => i * 0.75).ToList();
        var offsets = new[] { 0.08, 0.09, 0.07, 0.085, 0.3, 0.08, 0.075, 0.09 }; // one sloppy answer
        var onsets = clicks.Zip(offsets, (c, o) => c + o).ToList();
        var result = Calibration.Latency(clicks, onsets)!;
        Assert.InRange(result.LatencyMs, 79, 86);
        Assert.Equal(8, result.Used);
    }

    [Fact]
    public void Latency_needs_enough_answers() =>
        Assert.Null(Calibration.Latency(new[] { 0.0, 1, 2, 3, 4, 5 }, new[] { 0.1, 1.1 }));

    [Fact]
    public void Tuning_rejects_a_different_note()
    {
        var frames = PitchTracker.Analyse(Signals.Tone(Pitch.FrequencyOfMidi(72.12), 0.4, 48000), 48000);
        var ok = Calibration.TuneNote(72, frames);
        Assert.True(ok.Accepted);
        Assert.InRange(ok.CentsOff, 10, 14);

        var wrong = Calibration.TuneNote(71, frames); // played Do, asked for Si
        Assert.False(wrong.Accepted);
    }
}

/// <summary>Timing tests run alone: in parallel with the rest of the suite they measure the other tests.</summary>
[CollectionDefinition("Timing", DisableParallelization = true)]
public class TimingCollection;

[Collection("Timing")]
public class PitchPerformanceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void Tracker_runs_well_faster_than_real_time()
    {
        // Widest search the app uses (the free tuner): this is the slowest configuration.
        var signal = Signals.Tone(523.25, 10, 48000);
        PitchTracker.Analyse(signal.AsSpan(0, 48000), 48000); // warm-up (JIT)
        var watch = System.Diagnostics.Stopwatch.StartNew();
        PitchTracker.Analyse(signal, 48000);
        watch.Stop();
        output.WriteLine($"10 s of audio analysed in {watch.ElapsedMilliseconds} ms");
        Assert.True(watch.Elapsed.TotalSeconds < 2.5, $"took {watch.Elapsed.TotalSeconds:F2} s for 10 s of audio");
    }
}
