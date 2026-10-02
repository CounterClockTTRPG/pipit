using Pipit.Core.Instruments;
using Pipit.Core.Music;
using Xunit;

namespace Pipit.Core.Tests;

public class SongLibraryTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static SongLibrary Library => SongLibrary.Load(name => File.ReadAllText(Repo.Data("songs", name)));

    /// <summary>The notes taught up to and including each chapter (written pitch, soprano).</summary>
    private static readonly Dictionary<int, string[]> Taught = new()
    {
        [1] = new[] { "G4", "A4", "B4", "D5" },
        [2] = new[] { "G4", "A4", "B4", "C5", "D5" },
        [3] = new[] { "D4", "E4", "G4", "A4", "B4", "C5", "D5" },
        [4] = new[] { "C4", "D4", "E4", "F4", "G4", "A4", "B4", "C5", "D5" },
        [5] = new[] { "C4", "D4", "E4", "F4", "G4", "A4", "B4", "C5", "D5", "E5", "F5", "G5" },
        [6] = new[] { "C4", "D4", "E4", "F4", "F#4", "G4", "G#4", "A4", "B4", "C5", "C#5", "D5", "E5", "F5", "F#5", "G5", "A5", "B5" },
        [7] = new[] { "C4", "D4", "E4", "F4", "F#4", "G4", "G#4", "A4", "Bb4", "B4", "C5", "C#5", "D5", "E5", "F5", "F#5", "G5", "A5", "B5" },
        [8] = new[] { "C4", "D4", "E4", "F4", "F#4", "G4", "G#4", "A4", "Bb4", "B4", "C5", "C#5", "D5", "E5", "F5", "F#5", "G5", "A5", "B5" },
        [9] = new[] { "C4", "D4", "E4", "F4", "F#4", "G4", "G#4", "A4", "Bb4", "B4", "C5", "C#5", "D5", "Eb5", "E5", "F5", "F#5", "G5", "Ab5", "A5", "Bb5", "B5", "C6" },
        // Chapter 10 is the alto's sonatas: Pipit transposes them for a soprano.
    };

    [Fact]
    public void Every_song_parses()
    {
        var library = Library;
        Assert.Empty(library.Errors);
        // Every song file (including personal/) is listed in index.json and loads.
        Assert.Equal(Directory.GetFiles(Repo.Data("songs"), "*.json", SearchOption.AllDirectories).Length - 1, library.Songs.Count);
    }

    [Fact]
    public void Songs_fit_the_recorder_they_are_written_for()
    {
        foreach (var song in Library.Songs)
        {
            var recorder = RecorderInfo.Of(Enum.Parse<RecorderKind>(song.Melody.Instrument, ignoreCase: true));
            Assert.All(song.Melody.Performance, n => Assert.True(recorder.InRange(n.Pitch.Midi), $"{song.Id}: {n.Pitch} on {song.Melody.Instrument}"));
        }
    }

    [Fact]
    public void Chapters_only_use_notes_taught_so_far()
    {
        foreach (var song in Library.Songs.Where(s => Taught.ContainsKey(s.Chapter)))
        {
            var allowed = Taught[song.Chapter].Select(p => Pitch.Parse(p).Midi).ToHashSet();
            Assert.All(song.Melody.Performance, n => Assert.True(allowed.Contains(n.Pitch.Midi), $"{song.Id} uses {n.Pitch}"));
        }
    }

    [Fact]
    public void Every_song_has_an_introduction_and_a_tip_in_english_and_dutch()
    {
        foreach (var song in Library.Songs)
            foreach (var language in new[] { "en", "nl" })
            {
                Assert.True(song.About.ContainsKey(language), $"{song.Id}: no about ({language})");
                Assert.True(song.Tip.ContainsKey(language), $"{song.Id}: no tip ({language})");
            }
    }

    [Fact]
    public void Learning_path_first_then_repertoire_by_difficulty()
    {
        var songs = Library.Songs;
        var path = songs.TakeWhile(s => s.Chapter > 0).Select(s => s.Chapter).ToList();
        Assert.Equal(path.OrderBy(c => c), path);
        var repertoire = songs.SkipWhile(s => s.Chapter > 0).ToList();
        Assert.All(repertoire, s => Assert.Equal(0, s.Chapter));
        var scores = repertoire.Select(s => Difficulty.Estimate(s).Score).ToList();
        Assert.Equal(scores.OrderBy(x => x), scores);
    }

    [Fact]
    public void Difficulty_estimates_are_sensible()
    {
        var lib = Library;
        foreach (var s in lib.Songs)
        {
            var d = Difficulty.Estimate(s);
            output.WriteLine($"{s.Id,-26} level {d.Level}  score {d.Score:0.0}  nps {d.NotesPerSecond:0.0} range {d.Range} acc {d.Accidentals:P0} high {d.High:P0} leaps {d.Leaps:P0}");
        }
        Assert.Equal(1, lib.Get("hot-cross-buns")!.Difficulty);
        Assert.True(lib.Get("bach-prelude-first-half")!.Difficulty >= 4);
        Assert.True(lib.Get("ode-to-joy")!.Difficulty < lib.Get("minuet-in-g")!.Difficulty);
    }

    [Theory]
    [InlineData("au-clair-de-la-lune", 22)]   // 11 notes, repeated
    [InlineData("jingle-bells", 51)]          // 2 x 20 shared notes + 6 (first ending) + 5 (second)
    [InlineData("hot-cross-buns", 17)]
    [InlineData("ode-to-joy-full", 62)]        // 4 lines of 15/16 notes
    public void Note_counts(string id, int count) => Assert.Equal(count, Library.Get(id)!.Melody.Performance.Count);

    [Fact]
    public void Jingle_bells_endings_are_in_the_right_place()
    {
        var notes = Library.Get("jingle-bells")!.Melody.Performance;
        // First time: "... A4/h D5/h", second time: "D5 D5 C5 A4 | G4/w".
        Assert.Equal("G4", notes[^1].Pitch.ToString());
        Assert.Equal(4.0, notes[^1].Beats);
        Assert.Equal(64.0, notes[^1].StartBeat + 4); // 16 bars of 4/4 in total
    }
}
