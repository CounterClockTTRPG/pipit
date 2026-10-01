namespace Pipit.Core.Tests;

/// <summary>Synthetic test signals.</summary>
public static class Signals
{
    /// <summary>
    /// A recorder-like tone: fundamental plus weak harmonics and a little breath noise, with a
    /// 20 ms attack and release so note edges look like a real tongued note.
    /// </summary>
    public static float[] Tone(double hz, double seconds, int sampleRate, double amplitude = 0.3,
        double[]? harmonics = null, double noise = 0.0, int seed = 1)
    {
        harmonics ??= new[] { 1.0, 0.15, 0.08, 0.03 };
        var rng = new Random(seed);
        var n = (int)(seconds * sampleRate);
        var result = new float[n];
        var ramp = (int)(0.02 * sampleRate);
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / sampleRate;
            double s = 0;
            for (var h = 0; h < harmonics.Length; h++)
                s += harmonics[h] * Math.Sin(2 * Math.PI * hz * (h + 1) * t);
            var env = Math.Min(1.0, Math.Min((double)i / ramp, (double)(n - 1 - i) / ramp));
            result[i] = (float)(amplitude * env * s + noise * (rng.NextDouble() * 2 - 1));
        }
        return result;
    }

    public static float[] Silence(double seconds, int sampleRate, double noise = 0.0, int seed = 2)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, (int)(seconds * sampleRate))
            .Select(_ => (float)(noise * (rng.NextDouble() * 2 - 1))).ToArray();
    }

    /// <summary>A metronome-like click: a 3 ms decaying noise burst.</summary>
    public static float[] Click(int sampleRate, double amplitude = 0.8, int seed = 3)
    {
        var rng = new Random(seed);
        var n = (int)(0.003 * sampleRate);
        return Enumerable.Range(0, n)
            .Select(i => (float)(amplitude * (1 - (double)i / n) * (rng.NextDouble() * 2 - 1))).ToArray();
    }

    public static float[] Concat(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>Adds <paramref name="other"/> into <paramref name="target"/> starting at a sample offset.</summary>
    public static void MixInto(float[] target, float[] other, int offset)
    {
        for (var i = 0; i < other.Length && offset + i < target.Length; i++)
            target[offset + i] += other[i];
    }
}

public static class Repo
{
    /// <summary>The repository root, found by walking up to project.godot.</summary>
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
        }
    }

    public static string Data(params string[] parts) => Path.Combine(new[] { Root, "data" }.Concat(parts).ToArray());
}
