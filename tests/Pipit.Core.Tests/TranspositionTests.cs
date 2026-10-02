using Pipit.Core.Instruments;
using Pipit.Core.Music;
using Pipit.Core.Notation;
using Xunit;

namespace Pipit.Core.Tests;

public class TranspositionTests
{
    private static SongLibrary Library => SongLibrary.Load(name => File.ReadAllText(Repo.Data("songs", name)));

    [Theory]
    [InlineData("Bb", -3, "G")]
    [InlineData("G", 5, "C")]
    [InlineData("C", 12, "C")]
    [InlineData("F", 2, "G")]
    [InlineData("Gm", 2, "Am")]
    [InlineData("Am", -2, "Gm")]
    public void Keys_move_with_the_music(string key, int shift, string expected) => Assert.Equal(expected, Transposition.KeyAfter(key, shift).Name);

    [Fact]
    public void Notes_are_spelled_for_the_new_key()
    {
        Assert.Equal("Bb4", Transposition.SpellIn(70, new KeySignature(-1)).ToString()); // F major
        Assert.Equal("F#5", Transposition.SpellIn(78, new KeySignature(1)).ToString());  // G major
        Assert.Equal("C#5", Transposition.SpellIn(73, new KeySignature(0)).ToString());  // chromatic in C: sharp
        Assert.Equal("Ab4", Transposition.SpellIn(68, new KeySignature(-2)).ToString()); // chromatic in Bb: flat
        Assert.Equal("B3", Transposition.SpellIn(59, new KeySignature(0)).ToString());
        Assert.Equal("F5", Transposition.SpellIn(77, new KeySignature(1)).ToString());  // a natural in G, not E sharp
        Assert.Equal("A4", Transposition.SpellIn(69, new KeySignature(-3)).ToString()); // a natural in E flat
        Assert.Equal("B4", Transposition.SpellIn(71, new KeySignature(-2)).ToString()); // not C flat
    }

    [Fact]
    public void The_alto_bransles_fit_a_soprano_a_few_semitones_down()
    {
        var soprano = RecorderInfo.Of(RecorderKind.Soprano);
        foreach (var id in new[] { "bransle-gay-2-diminutions", "bransle-gay-7-diminutions" })
        {
            var song = Library.Get(id)!;
            var fit = Transposition.FitFor(song, soprano);
            Assert.NotNull(fit);
            // VII (D5-F6) fits an octave down, keeping its key; II (G4-F6) goes down 4 to Eb,
            // as 3 down would be E major with four sharps.
            Assert.Contains(fit!.Value.Semitones, new[] { -12, -4 });
            Assert.True(Math.Abs(KeySignature.Parse(fit.Value.Song.Key).Fifths) <= 3);
            Assert.All(fit.Value.Song.Melody.Performance, n => Assert.True(soprano.InRange(n.Pitch.Midi)));
            // The written bars move too, and the score can still be engraved.
            Assert.Equal(fit.Value.Song.Melody.Performance[0].Pitch.Midi, song.Melody.Performance[0].Pitch.Midi + fit.Value.Semitones);
            StaffLayout.Layout(fit.Value.Song.Melody.Bars, fit.Value.Song.Time, KeySignature.Parse(fit.Value.Song.Key));
        }
    }

    [Fact]
    public void A_song_that_fits_is_left_alone()
    {
        var song = Library.Get("ode-to-joy")!;
        var fit = Transposition.FitFor(song, RecorderInfo.Of(RecorderKind.Soprano));
        Assert.Same(song, fit!.Value.Song);
        Assert.Equal(0, fit.Value.Semitones);
    }

    [Fact]
    public void Every_song_fits_every_recorder_somehow()
    {
        foreach (var kind in Enum.GetValues<RecorderKind>())
            foreach (var song in Library.Songs)
                Assert.True(Transposition.FitFor(song, RecorderInfo.Of(kind)) is not null, $"{song.Id} on {kind}");
    }
}
