using Pipit.Core.Music;
using Pipit.Core.Notation;
using Xunit;

namespace Pipit.Core.Tests;

public class NotationTests
{
    private static List<StaffSystem> Engrave(string music, string time = "4/4", string key = "C", double width = 60) =>
        StaffLayout.Layout(SongParser.ParseMusic(music, TimeSignature.Parse(time), 0, "t").Bars, TimeSignature.Parse(time), KeySignature.Parse(key), new LayoutOptions { LineWidth = width });

    private static List<PlacedNote> Notes(List<StaffSystem> systems) => systems.SelectMany(s => s.Bars).SelectMany(b => b.Notes).ToList();

    [Theory]
    [InlineData("C", 0)]
    [InlineData("G", 1)]
    [InlineData("F", -1)]
    [InlineData("Bb", -2)]
    [InlineData("Gm", -2)]
    [InlineData("Em", 1)]
    public void Key_signatures(string key, int fifths) => Assert.Equal(fifths, KeySignature.Parse(key).Fifths);

    [Theory]
    [InlineData("E4", 0)]
    [InlineData("G4", 2)]
    [InlineData("B4", 4)]
    [InlineData("F5", 8)]
    [InlineData("C4", -2)]
    [InlineData("D6", 13)]
    [InlineData("F#5", 8)]
    public void Staff_positions(string pitch, int position) => Assert.Equal(position, StaffLayout.PositionOf(Pitch.Parse(pitch)));

    [Fact]
    public void Accidentals_follow_the_key_and_the_bar()
    {
        // G major: F# needs no sign; F natural does; a second F natural in the bar does not;
        // in the next bar the key signature applies again.
        var notes = Notes(Engrave("F#5/q F5 F5 F#5 | F5/w", key: "G"));
        Assert.Equal(new[] { Accidental.None, Accidental.Natural, Accidental.None, Accidental.Sharp, Accidental.Natural },
            notes.Select(n => n.Accidental));
    }

    [Fact]
    public void Same_letter_in_another_octave_keeps_its_own_accidental()
    {
        var notes = Notes(Engrave("F#4/q F5 F#4 F5"));
        Assert.Equal(new[] { Accidental.Sharp, Accidental.None, Accidental.None, Accidental.None }, notes.Select(n => n.Accidental));
    }

    [Fact]
    public void Tie_across_the_bar_needs_no_new_accidental()
    {
        var notes = Notes(Engrave("C5/h. C#5/q~ | C#5/w"));
        Assert.Equal(Accidental.Sharp, notes[1].Accidental);
        Assert.Equal(Accidental.None, notes[2].Accidental);
    }

    [Fact]
    public void Stems_point_away_from_the_middle()
    {
        var notes = Notes(Engrave("G4/q D5 B4 A5"));
        Assert.Equal(new[] { true, false, false, false }, notes.Select(n => n.StemUp));
    }

    [Fact]
    public void Eighths_are_beamed_per_beat_with_one_direction()
    {
        var notes = Notes(Engrave("G4/e A4 B4 C5 D5/q R/e E5/e"));
        Assert.Equal(notes[0].BeamGroup, notes[1].BeamGroup);
        Assert.NotEqual(notes[1].BeamGroup, notes[2].BeamGroup);
        Assert.Equal(notes[2].BeamGroup, notes[3].BeamGroup);
        Assert.Null(notes[4].BeamGroup);   // quarter
        Assert.Null(notes[6].BeamGroup);   // eighth alone after a rest
        Assert.True(notes[0].StemUp && notes[1].StemUp);
    }

    [Fact]
    public void Beam_clears_every_notehead_by_a_stem_length()
    {
        var notes = Notes(Engrave("G4/s B4 D5 G5 G4/q G4 G4"));
        var group = notes.Take(4).ToList();
        Assert.All(group, n => Assert.True(n.StemUp ? n.StemEnd - n.Position >= 6.99 : n.Position - n.StemEnd >= 6.99, $"note at {n.Position}"));
    }

    [Fact]
    public void Six_eight_beams_in_dotted_quarters()
    {
        var notes = Notes(Engrave("G4/e A4 B4 C5 D5 E5", "6/8"));
        Assert.Equal(notes[0].BeamGroup, notes[2].BeamGroup);
        Assert.NotEqual(notes[2].BeamGroup, notes[3].BeamGroup);
    }

    [Fact]
    public void Cut_time_beams_eighths_in_fours()
    {
        var notes = Notes(Engrave("G4/e A4 B4 C5 D5 C5 B4 A4", "2/2"));
        Assert.Equal(notes[0].BeamGroup, notes[3].BeamGroup);
        Assert.NotEqual(notes[3].BeamGroup, notes[4].BeamGroup);
    }

    [Fact]
    public void Lines_break_and_fill_the_width()
    {
        var systems = Engrave(string.Join(" | ", Enumerable.Repeat("G4/q A4 B4 C5", 12)), width: 60);
        Assert.True(systems.Count >= 3);
        Assert.True(systems[0].ShowTime);
        Assert.False(systems[1].ShowTime);
        foreach (var s in systems.Take(systems.Count - 1))
            Assert.InRange(s.Width, 59.9, 60.1);
        // Notes stay in order inside each bar.
        foreach (var bar in systems.SelectMany(s => s.Bars))
            Assert.Equal(bar.Notes.Select(n => n.X).OrderBy(x => x), bar.Notes.Select(n => n.X));
    }

    [Fact]
    public void Every_library_song_can_be_engraved()
    {
        var library = SongLibrary.Load(name => File.ReadAllText(Repo.Data("songs", name)));
        foreach (var song in library.Songs)
        {
            var systems = StaffLayout.Layout(song.Melody.Bars, song.Time, KeySignature.Parse(song.Key));
            Assert.Equal(song.Melody.Bars.Count, systems.Sum(s => s.Bars.Count));
        }
    }
}
