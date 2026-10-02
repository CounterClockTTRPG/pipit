using Pipit.Core.Analysis;
using Pipit.Core.Music;
using Pipit.Core.Synthesis;
using Xunit;
using Xunit.Abstractions;

namespace Pipit.Core.Tests;

public class AccompanimentParsingTests
{
    [Fact]
    public void Accompaniment_parts_are_parsed_with_their_sounds()
    {
        var song = SongParser.ParseJson("""
            { "id": "t", "time": "4/4", "pickup": "q",
              "parts": [ { "music": "D4/q | G4/w | G4/h. " } ],
              "accompaniment": [ { "id": "bass", "sound": "cello", "music": "R/q | G2/w | G2/h." } ] }
            """);
        var bass = Assert.Single(song.Accompaniment);
        Assert.Equal("cello", bass.Sound);
        // The accompaniment follows the same clock: its first note is on beat 0, like the melody's G.
        Assert.Equal(0, bass.Performance[0].StartBeat);
        Assert.Equal(song.Melody.Performance[1].StartBeat, bass.Performance[0].StartBeat);
        Assert.True(song.HasOtherParts);
        Assert.Equal(12, song.Melody.SoundingOffset); // soprano parts sound an octave up
    }

    [Fact]
    public void A_round_adds_delayed_entries()
    {
        var song = SongParser.ParseJson("""
            { "id": "r", "time": "4/4", "round": { "entries": 3, "everyBars": 2 },
              "parts": [ { "music": "G4/w | A4/w | B4/w | C5/w" } ] }
            """);
        Assert.Equal(3, song.Parts.Count);
        Assert.Equal(8, song.Parts[1].Performance[0].StartBeat);
        Assert.Equal(16, song.Parts[2].Performance[0].StartBeat);
        Assert.Equal(32, song.EndBeat); // the last entry ends 4 bars after the first
    }

    [Fact]
    public void Choosing_a_part_puts_it_first()
    {
        var song = SongParser.ParseJson("""
            { "id": "r", "time": "4/4", "round": { "entries": 2, "everyBars": 1 }, "parts": [ { "music": "G4/w | A4/w" } ] }
            """);
        var second = song.WithPartFirst(1);
        Assert.Equal("entry2", second.Melody.Id);
        Assert.Equal("melody", second.Parts[1].Id);
    }
}

/// <summary>Needs the SoundFont (scripts/fetch-soundfont.sh); reports and passes without it.</summary>
public class SoundFontRendererTests(ITestOutputHelper output)
{
    private static string Path => System.IO.Path.Combine(Repo.Root, "assets", "soundfont", "GeneralUser-GS.sf2");
    private static readonly Lazy<SoundFontRenderer?> Renderer = new(() => File.Exists(Path) ? new SoundFontRenderer(File.OpenRead(Path)) : null);

    [Theory]
    [InlineData(72)]
    [InlineData(79)]
    [InlineData(86)]
    public void Recorder_preset_is_heard_as_the_right_note(int key)
    {
        if (Renderer.Value is not { } r)
        {
            output.WriteLine("SoundFont not fetched; skipped.");
            return;
        }
        var audio = r.Render(new[] { new SfTrack(Sounds.Recorder, new[] { new SfNote(0.1, 1.0, key) }) }, 48000);
        var frames = PitchTracker.Analyse(audio, 48000, PitchTrackerOptions.ForRange(70, 98));
        var midi = Stats.Median(frames.Where(f => f.Voiced && f.Time > 0.3 && f.Time < 0.9).Select(f => f.Midi));
        output.WriteLine($"key {key}: heard {midi:0.00}");
        Assert.InRange(midi, key - 0.25, key + 0.25);
    }

    [Fact]
    public void Reference_a_retunes_the_preview()
    {
        if (Renderer.Value is not { } r)
            return;
        var audio = r.Render(new[] { new SfTrack(Sounds.Recorder, new[] { new SfNote(0.1, 1.0, 81) }) }, 48000, referenceA: 415);
        var frames = PitchTracker.Analyse(audio, 48000, PitchTrackerOptions.ForRange(70, 98));
        var hz = Stats.Median(frames.Where(f => f.Voiced && f.Time > 0.3 && f.Time < 0.9).Select(f => f.Hz));
        Assert.InRange(hz, 830 * 0.99, 830 * 1.01); // A5 at A4 = 415 Hz
    }

    [Fact]
    public void Repeated_notes_restart_and_tracks_mix()
    {
        if (Renderer.Value is not { } r)
            return;
        var melody = new SfTrack(Sounds.Recorder, Enumerable.Range(0, 4).Select(i => new SfNote(0.1 + i * 0.4, 0.4, 79)).ToList());
        var bass = new SfTrack(Sounds.Program("cello"), new[] { new SfNote(0.1, 1.6, 43) }, Articulate: false);
        var audio = r.Render(new[] { melody }, 48000);
        var notes = NoteSegmenter.Segment(PitchTracker.Analyse(audio, 48000, PitchTrackerOptions.ForRange(70, 98)));
        Assert.Equal(4, notes.Count(n => n.Semitone == 79));
        var mixed = r.Render(new[] { melody, bass }, 48000);
        Assert.True(mixed.Max(Math.Abs) > 0.01);
    }
}

public class AccompanimentContentTests
{
    private static SongLibrary Library => SongLibrary.Load(name => File.ReadAllText(Repo.Data("songs", name)));

    [Fact]
    public void Accompaniment_fits_its_song()
    {
        foreach (var song in Library.Songs.Where(s => s.Accompaniment.Count > 0))
        {
            var melodyEnd = song.Melody.Performance.Max(n => n.EndBeat);
            var melodyStart = song.Melody.Performance.Min(n => n.StartBeat);
            foreach (var part in song.Accompaniment)
            {
                // It starts with the song (not before its pickup) and ends with it.
                Assert.True(part.Performance.Min(n => n.StartBeat) >= melodyStart - 1e-9, $"{song.Id}/{part.Id} starts early");
                Assert.Equal(melodyEnd, part.Performance.Max(n => n.EndBeat), 6);
            }
        }
    }

    [Fact]
    public void Frere_jacques_is_a_four_part_round()
    {
        var song = Library.Get("frere-jacques")!;
        Assert.Equal(4, song.Parts.Count);
        Assert.Equal(song.Melody.Performance.Max(n => n.EndBeat) + 3 * 8, song.EndBeat);
    }
}
