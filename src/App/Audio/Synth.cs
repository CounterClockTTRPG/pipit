using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Pipit.Core.Music;
using Pipit.Core.Synthesis;
using FileAccess = Godot.FileAccess;

namespace Pipit.App.Audio;

/// <summary>
/// Sound for previews and the band. The SoundFont (natural voice, accompaniment) loads in the
/// background the first time it is needed; until then, or if it was never fetched, previews
/// use the simple procedural voice and the band is unavailable.
/// </summary>
public static class Synth
{
    public const string SoundFontPath = "res://assets/soundfont/GeneralUser-GS.sf2";
    private static Task<SoundFontRenderer?>? _loading;

    public static bool SoundFontInstalled => FileAccess.FileExists(SoundFontPath);

    /// <summary>Starts loading the SoundFont if it is installed; safe to call repeatedly.</summary>
    public static Task<SoundFontRenderer?> Renderer()
    {
        return _loading ??= Task.Run(() =>
        {
            if (!SoundFontInstalled)
                return null;
            var bytes = FileAccess.GetFileAsBytes(SoundFontPath);
            return bytes.Length == 0 ? null : new SoundFontRenderer(new MemoryStream(bytes));
        });
    }

    /// <summary>
    /// Renders parts of a song from <paramref name="fromBeat"/> on, with the player's part (the
    /// song's first part) at the player's instrument pitch and the others at their own. Notes
    /// start at <paramref name="lead"/> seconds into the buffer.
    /// </summary>
    public static async Task<float[]> RenderAsync(Song song, IEnumerable<SongPart> parts, double secondsPerBeat, double fromBeat, double lead, bool natural)
    {
        var s = Services.Instance;
        var referenceA = s.Instrument.ReferenceA;
        var rate = s.Audio.SampleRate;
        var player = song.Melody;
        var list = parts.ToList();
        var renderer = natural || list.Any(p => p != player) ? await Renderer() : null;

        int Key(SongPart part, PerformedNote n) => part == player ? s.Recorder.WrittenToSounding(n.Pitch.Midi) : n.Pitch.Midi + part.SoundingOffset;
        double Start(PerformedNote n) => lead + (n.StartBeat - fromBeat) * secondsPerBeat;

        if (renderer is null)
        {
            // Simple voice: the player's part only.
            var notes = player.Performance.Select(n => new SynthNote(Start(n), n.Beats * secondsPerBeat, Pitch.FrequencyOfMidi(Key(player, n), referenceA))).ToList();
            return await Task.Run(() => RecorderVoice.Render(notes, rate));
        }
        var tracks = list.Select(part => new SfTrack(
            part == player || part.Instrument != "accompaniment" ? Sounds.Recorder : Sounds.Program(part.Sound),
            part.Performance.Select(n => new SfNote(Start(n), n.Beats * secondsPerBeat, Key(part, n))).ToList(),
            Volume: part == player ? 1f : 0.8f,
            Articulate: part.Instrument != "accompaniment" || Sounds.Program(part.Sound) is Sounds.Recorder or 73 or 68 or 70)).ToList();
        return await Task.Run(() => renderer.Render(tracks, rate, referenceA));
    }
}
