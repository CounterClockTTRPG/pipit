using System.Numerics;

namespace Pipit.Core.Analysis;

public readonly record struct PitchEstimate(double Hz, double Clarity)
{
    public static readonly PitchEstimate None = new(0, 0);
    public bool HasPitch => Hz > 0;
}

/// <summary>
/// YIN fundamental-frequency estimator (de Cheveigné and Kawahara, 2002). A recorder tone is
/// close to a sine with weak overtones, which YIN handles very well. The lag search is limited
/// to [minHz, maxHz]; keeping it near the instrument's range removes most octave errors.
/// </summary>
public sealed class YinPitchDetector
{
    private readonly int _tauMin;
    private readonly int _tauMax;
    private readonly int _integration;
    private readonly float[] _diff;

    public int SampleRate { get; }
    public int WindowSize { get; }
    public double Threshold { get; }

    public YinPitchDetector(int sampleRate, int windowSize, double minHz, double maxHz, double threshold = 0.12)
    {
        if (minHz <= 0 || maxHz <= minHz)
            throw new ArgumentException("Need 0 < minHz < maxHz");
        SampleRate = sampleRate;
        WindowSize = windowSize;
        Threshold = threshold;
        _tauMin = Math.Max(2, (int)Math.Floor(sampleRate / maxHz));
        _tauMax = (int)Math.Ceiling(sampleRate / minHz);
        _integration = windowSize - _tauMax - 1;
        if (_integration < _tauMax)
            throw new ArgumentException($"Window of {windowSize} samples is too short for {minHz} Hz at {sampleRate} Hz");
        _diff = new float[_tauMax + 2];
    }

    public PitchEstimate Estimate(ReadOnlySpan<float> window)
    {
        if (window.Length < WindowSize)
            throw new ArgumentException("Window shorter than WindowSize");

        // Step 2: difference function, vectorised over the integration window.
        for (var tau = 1; tau <= _tauMax + 1; tau++)
            _diff[tau] = SquaredDifference(window[.._integration], window.Slice(tau, _integration));

        // Step 3: cumulative mean normalised difference, in place. d'(0) = 1 by definition.
        _diff[0] = 1;
        double running = 0;
        for (var tau = 1; tau <= _tauMax + 1; tau++)
        {
            running += _diff[tau];
            _diff[tau] = running > 0 ? (float)(_diff[tau] * tau / running) : 1;
        }

        // Step 4: first dip below the threshold, followed down to its local minimum.
        var best = -1;
        for (var tau = _tauMin; tau <= _tauMax; tau++)
        {
            if (_diff[tau] < Threshold)
            {
                while (tau + 1 <= _tauMax && _diff[tau + 1] < _diff[tau])
                    tau++;
                best = tau;
                break;
            }
        }
        if (best < 0)
        {
            // No confident period: report the global minimum with its (low) clarity.
            best = _tauMin;
            for (var tau = _tauMin + 1; tau <= _tauMax; tau++)
                if (_diff[tau] < _diff[best])
                    best = tau;
        }

        // Step 5: parabolic interpolation around the chosen lag.
        double refined = best;
        if (best > 1 && best <= _tauMax)
        {
            double a = _diff[best - 1], b = _diff[best], c = _diff[best + 1];
            var denom = a - 2 * b + c;
            if (denom > 1e-12)
                refined = best + 0.5 * (a - c) / denom;
        }
        var clarity = Math.Clamp(1 - _diff[best], 0, 1);
        return new PitchEstimate(SampleRate / refined, clarity);
    }

    private static float SquaredDifference(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var sum = Vector<float>.Zero;
        var i = 0;
        var width = Vector<float>.Count;
        for (; i <= a.Length - width; i += width)
        {
            var d = new Vector<float>(a[i..]) - new Vector<float>(b[i..]);
            sum += d * d;
        }
        var total = Vector.Sum(sum);
        for (; i < a.Length; i++)
        {
            var d = a[i] - b[i];
            total += d * d;
        }
        return total;
    }

    public static double Rms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (var s in samples)
            sum += s * s;
        return samples.Length == 0 ? 0 : Math.Sqrt(sum / samples.Length);
    }
}
