using MeltySynth;

namespace Pipit.Core.Synthesis;

/// <summary>A note for the SoundFont: start and length in seconds, MIDI key.</summary>
public sealed record SfNote(double Start, double Duration, int Key, int Velocity = 90);

/// <summary>One instrument's notes. <see cref="Program"/> is the General MIDI program (0-based: 74 = recorder).</summary>
public sealed record SfTrack(int Program, IReadOnlyList<SfNote> Notes, float Volume = 1f, bool Articulate = true);

/// <summary>General MIDI programs Pipit uses, by the names song files give them.</summary>
public static class Sounds
{
    private static readonly Dictionary<string, int> Programs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["piano"] = 0, ["harpsichord"] = 6, ["organ"] = 19, ["guitar"] = 24, ["bass"] = 32,
        ["violin"] = 40, ["viola"] = 41, ["cello"] = 42, ["contrabass"] = 43, ["strings"] = 48, ["pizzicato"] = 45,
        ["tuba"] = 58, ["oboe"] = 68, ["bassoon"] = 70, ["clarinet"] = 71, ["flute"] = 73, ["recorder"] = 74, ["panflute"] = 75,
    };

    public const int Recorder = 74;

    public static int Program(string? name) => name is not null && Programs.TryGetValue(name, out var p) ? p : Recorder;
}

/// <summary>
/// Renders tracks with a General MIDI SoundFont through MeltySynth (pure C#). Offline: the
/// whole piece is rendered to a buffer before it plays, which keeps timing sample-exact.
/// </summary>
public sealed class SoundFontRenderer
{
    private readonly SoundFont _soundFont;

    public SoundFontRenderer(Stream sf2) => _soundFont = new SoundFont(sf2);

    /// <summary>
    /// Mono mix of the tracks. <paramref name="referenceA"/> retunes everything (a recorder at
    /// A = 442 should hear its preview at 442). Articulated tracks release each note a little
    /// early, like a tongued recorder.
    /// </summary>
    public float[] Render(IReadOnlyList<SfTrack> tracks, int sampleRate, double referenceA = 440, double tailSeconds = 1.0, float gain = 0.6f)
    {
        var synth = new Synthesizer(_soundFont, sampleRate);
        var events = new List<(int Sample, int Order, Action Do)>();
        var end = 0.0;
        // Pitch bend range is two semitones: centre 8192, +-8192 = +-200 cents.
        var cents = 1200 * Math.Log2(referenceA / 440.0);
        var bend = Math.Clamp((int)Math.Round(8192 + cents / 200 * 8192), 0, 16383);
        for (var t = 0; t < tracks.Count && t < 15; t++)
        {
            var channel = t >= 9 ? t + 1 : t; // channel 9 is drums
            var track = tracks[t];
            var volume = (int)Math.Clamp(track.Volume * 100, 0, 127);
            events.Add((0, 0, () =>
            {
                synth.ProcessMidiMessage(channel, 0xC0, track.Program, 0);
                synth.ProcessMidiMessage(channel, 0xB0, 7, volume);
                synth.ProcessMidiMessage(channel, 0xE0, bend & 0x7F, bend >> 7);
            }));
            foreach (var n in track.Notes)
            {
                var length = track.Articulate ? n.Duration - Math.Min(0.06, n.Duration * 0.2) : n.Duration * 0.98;
                var on = (int)Math.Round(n.Start * sampleRate);
                var off = (int)Math.Round((n.Start + length) * sampleRate);
                var key = n.Key;
                var velocity = n.Velocity;
                // Note-offs sort before note-ons at the same sample, so repeated notes restart.
                events.Add((on, 2, () => synth.NoteOn(channel, key, velocity)));
                events.Add((off, 1, () => synth.NoteOff(channel, key)));
                end = Math.Max(end, n.Start + n.Duration);
            }
        }
        events.Sort((a, b) => a.Sample != b.Sample ? a.Sample.CompareTo(b.Sample) : a.Order.CompareTo(b.Order));

        var total = (int)((end + tailSeconds) * sampleRate);
        var left = new float[total];
        var right = new float[total];
        var position = 0;
        var e = 0;
        while (position < total)
        {
            while (e < events.Count && events[e].Sample <= position)
                events[e++].Do();
            var next = e < events.Count ? Math.Min(events[e].Sample, total) : total;
            var count = Math.Max(1, Math.Min(next - position, 512));
            count = Math.Min(count, total - position);
            synth.Render(left.AsSpan(position, count), right.AsSpan(position, count));
            position += count;
        }
        var mono = new float[total];
        for (var i = 0; i < total; i++)
            mono[i] = Math.Clamp((left[i] + right[i]) * 0.5f * gain, -1f, 1f);
        return mono;
    }
}
