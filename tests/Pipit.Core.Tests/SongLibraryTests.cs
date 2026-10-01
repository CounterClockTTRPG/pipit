using Pipit.Core.Instruments;
using Pipit.Core.Music;
using Xunit;

namespace Pipit.Core.Tests;

public class SongLibraryTests
{
    private static SongLibrary Library => SongLibrary.Load(name => File.ReadAllText(Repo.Data("songs", name)));

    /// <summary>The notes taught up to and including each chapter (written pitch, soprano).</summary>
    private static readonly Dictionary<int, string[]> Taught = new()
    {
        [1] = new[] { "G4", "A4", "B4", "D5" },
        [2] = new[] { "G4", "A4", "B4", "C5", "D5" },
    };

    [Fact]
    public void Every_song_parses()
    {
        var library = Library;
        Assert.Empty(library.Errors);
        Assert.Equal(6, library.Songs.Count);
        Assert.Equal(library.Songs.Count, Directory.GetFiles(Repo.Data("songs"), "*.json").Length - 1); // all listed in index.json
    }

    [Fact]
    public void Songs_fit_a_soprano()
    {
        var soprano = RecorderInfo.Of(RecorderKind.Soprano);
        foreach (var song in Library.Songs)
            Assert.All(song.Melody.Performance, n => Assert.True(soprano.InRange(n.Pitch.Midi), $"{song.Id}: {n.Pitch}"));
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
    public void Songs_are_in_learning_order() =>
        Assert.Equal(Library.Songs.Select(s => s.Chapter).OrderBy(c => c), Library.Songs.Select(s => s.Chapter));

    [Theory]
    [InlineData("au-clair-de-la-lune", 22)]   // 11 notes, repeated
    [InlineData("jingle-bells", 51)]          // 2 x 20 shared notes + 6 (first ending) + 5 (second)
    [InlineData("hot-cross-buns", 17)]
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
