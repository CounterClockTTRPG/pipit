namespace Pipit.Core.Synthesis;

public sealed record SynthNote(double Start, double Duration, double Hz);

/// <summary>
/// A simple recorder-like voice for "listen first": a near-sine with weak harmonics, a short
/// breathy "chiff" at the start of each note, and a small gap before the next note so tongued
/// notes sound separate. Good enough to hear the tune; MeltySynth replaces it for accompaniment.
/// </summary>
public static class RecorderVoice
{
    public static float[] Render(IReadOnlyList<SynthNote> notes, int sampleRate, double tailSeconds = 0.5, double volume = 0.25)
    {
        var total = notes.Count == 0 ? 0 : notes.Max(n => n.Start + n.Duration) + tailSeconds;
        var output = new float[(int)(total * sampleRate)];
        var rng = new Random(11);
        foreach (var note in notes)
        {
            var start = (int)(note.Start * sampleRate);
            // Articulation: stop a little early (at most 60 ms, never more than a quarter of the note).
            var sounding = note.Duration - Math.Min(0.06, note.Duration * 0.25);
            var length = (int)(sounding * sampleRate);
            var attack = (int)(0.02 * sampleRate);
            var release = (int)(0.03 * sampleRate);
            var chiff = (int)(0.025 * sampleRate);
            var phase = 0.0;
            var step = 2 * Math.PI * note.Hz / sampleRate;
            for (var i = 0; i < length + release && start + i < output.Length; i++)
            {
                var env = i < attack ? (double)i / attack : i < length ? 1 : 1 - (double)(i - length) / release;
                var s = Math.Sin(phase) + 0.12 * Math.Sin(2 * phase) + 0.08 * Math.Sin(3 * phase) + 0.02 * Math.Sin(4 * phase);
                if (i < chiff)
                    s += 0.35 * (1 - (double)i / chiff) * (rng.NextDouble() * 2 - 1);
                output[start + i] += (float)(volume * env * s);
                phase += step;
            }
        }
        return output;
    }
}
