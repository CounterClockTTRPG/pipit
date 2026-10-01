using Pipit.Core.Music;

namespace Pipit.Core.Analysis;

/// <summary>
/// One analysis frame. <see cref="Time"/> is the centre of the window in seconds since the
/// first sample pushed. <see cref="Midi"/> is the raw estimate and <see cref="SmoothedMidi"/>
/// the median of recent voiced frames; both are NaN when the frame is unvoiced.
/// </summary>
public readonly record struct PitchFrame(double Time, double Hz, double Clarity, double Rms, bool Voiced, double Midi, double SmoothedMidi)
{
    public double Db => Rms > 0 ? 20 * Math.Log10(Rms) : -120;
}

public sealed record PitchTrackerOptions
{
    public int WindowSize { get; init; } = 1024;
    public int HopSize { get; init; } = 256;
    public double MinHz { get; init; } = 200;
    public double MaxHz { get; init; } = 3000;
    public double YinThreshold { get; init; } = 0.12;
    /// <summary>Frames below this clarity are unvoiced even when loud enough.</summary>
    public double MinClarity { get; init; } = 0.8;
    /// <summary>RMS below this is silence. Calibration raises it above the room's noise.</summary>
    public double GateRms { get; init; } = 0.003; // about -50 dBFS
    public int MedianFrames { get; init; } = 5;
    public double ReferenceA { get; init; } = Pitch.DefaultReferenceA;

    /// <summary>Search range for a recorder: its sounding range widened by two semitones.</summary>
    public static PitchTrackerOptions ForRange(int bottomSoundingMidi, int topSoundingMidi, double referenceA = Pitch.DefaultReferenceA) => new()
    {
        MinHz = Pitch.FrequencyOfMidi(bottomSoundingMidi - 2, referenceA),
        MaxHz = Pitch.FrequencyOfMidi(topSoundingMidi + 2, referenceA),
        ReferenceA = referenceA,
    };
}

/// <summary>
/// Streaming pitch analysis: push samples as they arrive, get frames every hop.
/// Used live (microphone) and offline (recorded takes, test fixtures) with identical results.
/// </summary>
public sealed class PitchTracker
{
    private readonly YinPitchDetector _yin;
    private readonly float[] _buffer;
    private int _filled;
    private long _consumed; // samples dropped off the front of the buffer so far
    private readonly double[] _recent;
    private int _recentCount;
    private int _recentNext;

    public PitchTrackerOptions Options { get; }
    public int SampleRate { get; }
    public double GateRms { get; set; }

    public PitchTracker(int sampleRate, PitchTrackerOptions? options = null)
    {
        Options = options ?? new PitchTrackerOptions();
        SampleRate = sampleRate;
        GateRms = Options.GateRms;
        _yin = new YinPitchDetector(sampleRate, Options.WindowSize, Options.MinHz, Options.MaxHz, Options.YinThreshold);
        _buffer = new float[Options.WindowSize * 4];
        _recent = new double[Options.MedianFrames];
    }

    public void Push(ReadOnlySpan<float> samples, List<PitchFrame> output)
    {
        while (!samples.IsEmpty)
        {
            var take = Math.Min(samples.Length, _buffer.Length - _filled);
            samples[..take].CopyTo(_buffer.AsSpan(_filled));
            _filled += take;
            samples = samples[take..];

            var start = 0;
            while (_filled - start >= Options.WindowSize)
            {
                output.Add(Analyse(_buffer.AsSpan(start, Options.WindowSize), _consumed + start));
                start += Options.HopSize;
            }
            // Keep the unconsumed tail at the front for the next push.
            Array.Copy(_buffer, start, _buffer, 0, _filled - start);
            _filled -= start;
            _consumed += start;
        }
    }

    public static List<PitchFrame> Analyse(ReadOnlySpan<float> samples, int sampleRate, PitchTrackerOptions? options = null, double? gateRms = null)
    {
        var tracker = new PitchTracker(sampleRate, options);
        if (gateRms is { } g)
            tracker.GateRms = g;
        var frames = new List<PitchFrame>(samples.Length / tracker.Options.HopSize + 1);
        tracker.Push(samples, frames);
        return frames;
    }

    private PitchFrame Analyse(ReadOnlySpan<float> window, long firstSample)
    {
        var time = (firstSample + Options.WindowSize / 2.0) / SampleRate;
        var rms = YinPitchDetector.Rms(window);
        if (rms < GateRms)
            return Unvoiced(time, 0, 0, rms);

        var estimate = _yin.Estimate(window);
        if (!estimate.HasPitch || estimate.Clarity < Options.MinClarity)
            return Unvoiced(time, estimate.Hz, estimate.Clarity, rms);

        var midi = Pitch.MidiOfFrequency(estimate.Hz, Options.ReferenceA);
        _recent[_recentNext] = midi;
        _recentNext = (_recentNext + 1) % _recent.Length;
        _recentCount = Math.Min(_recentCount + 1, _recent.Length);
        return new PitchFrame(time, estimate.Hz, estimate.Clarity, rms, true, midi, Median());
    }

    private PitchFrame Unvoiced(double time, double hz, double clarity, double rms)
    {
        // Silence resets the smoother so a new note is not dragged towards the previous one.
        _recentCount = 0;
        _recentNext = 0;
        return new PitchFrame(time, hz, clarity, rms, false, double.NaN, double.NaN);
    }

    private double Median()
    {
        Span<double> values = stackalloc double[_recentCount];
        for (var i = 0; i < _recentCount; i++)
            values[i] = _recent[i];
        values.Sort();
        return _recentCount % 2 == 1
            ? values[_recentCount / 2]
            : (values[_recentCount / 2 - 1] + values[_recentCount / 2]) / 2;
    }
}
