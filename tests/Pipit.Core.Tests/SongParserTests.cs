using Pipit.Core.Music;
using Xunit;

namespace Pipit.Core.Tests;

public class SongParserTests
{
    private static IReadOnlyList<PerformedNote> Perform(string music, string time = "4/4", double pickup = 0) =>
        SongParser.ParseMusic(music, TimeSignature.Parse(time), pickup, "test").Performance;

    [Fact]
    public void Triplets_and_thirty_seconds()
    {
        var (bars, performance) = SongParser.ParseMusic("C5/e3 D5 E5 F5/q. G5/t A5 B5 C6 D5/q | C5/q3 D5 E5 F5/h", TimeSignature.Parse("4/4"), 0, "t");
        Assert.Equal(new[] { 4.0, 4.0 }, bars.Select(b => Math.Round(b.Beats, 9)));
        Assert.True(bars[0].Notes[2].Triplet);
        Assert.False(bars[0].Notes[3].Triplet);
        Assert.Equal(1.0 / 3, performance[1].StartBeat, 9);
        Assert.Equal(0.125, performance[4].Beats, 9);
        Assert.Equal(4 + 2.0 / 3, performance[10].StartBeat, 9);
        Assert.Throws<SongFormatException>(() => SongParser.ParseMusic("C5/e4", TimeSignature.Parse("4/4"), 0, "t"));
    }

    [Fact]
    public void Durations_carry_over()
    {
        var notes = Perform("G4/q G4 G4 A4 | B4/h A4 | G4/w");
        Assert.Equal(new[] { 1.0, 1, 1, 1, 2, 2, 4 }, notes.Select(n => n.Beats));
        Assert.Equal(new[] { 0.0, 1, 2, 3, 4, 6, 8 }, notes.Select(n => n.StartBeat));
        Assert.Equal(new[] { 1, 1, 1, 1, 2, 2, 3 }, notes.Select(n => n.Bar));
    }

    [Fact]
    public void Dotted_notes_and_rests()
    {
        var notes = Perform("B4/q. A4/e A4/h | R/q G4/q G4/h");
        Assert.Equal(new[] { 1.5, 0.5, 2, 1, 2 }, notes.Select(n => n.Beats));
        Assert.Equal(5.0, notes[3].StartBeat); // after the quarter rest
    }

    [Fact]
    public void Ties_merge_into_one_sounding_note()
    {
        var notes = Perform("G4/h G4~ | G4/q A4/h.");
        Assert.Equal(3, notes.Count);
        Assert.Equal(3.0, notes[1].Beats);
    }

    [Fact]
    public void Tie_between_different_pitches_is_an_error() =>
        Assert.Throws<SongFormatException>(() => Perform("G4/w~ | A4/w"));

    [Fact]
    public void Simple_repeat_plays_twice()
    {
        var notes = Perform("|: G4/w | A4/w :| B4/w");
        Assert.Equal(new[] { "G4", "A4", "G4", "A4", "B4" }, notes.Select(n => n.Pitch.ToString()));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, notes.Select(n => n.Bar));
    }

    [Fact]
    public void First_and_second_endings()
    {
        var notes = Perform("|: G4/w | [1 A4/w :| [2 B4/w | C5/w");
        Assert.Equal(new[] { "G4", "A4", "G4", "B4", "C5" }, notes.Select(n => n.Pitch.ToString()));
        Assert.Equal(new[] { 0.0, 4, 8, 12, 16 }, notes.Select(n => n.StartBeat));
    }

    [Fact]
    public void Pickup_starts_before_beat_zero()
    {
        var notes = Perform("D4/q | G4/q G4 G4 G4 | G4/h G4/q", "4/4", 1); // last bar completes the pickup
        Assert.Equal(-1.0, notes[0].StartBeat);
        Assert.Equal(0, notes[0].Bar);
        Assert.Equal(0.0, notes[1].StartBeat);
    }

    [Fact]
    public void Wrong_bar_length_names_the_bar()
    {
        var e = Assert.Throws<SongFormatException>(() => Perform("G4/q G4 G4 A4 | B4/h A4/q | G4/w"));
        Assert.Contains("bar 2", e.Message);
        Assert.Contains("3 beats", e.Message);
    }

    [Fact]
    public void A_section_may_end_on_a_short_bar()
    {
        // 3/4 with a quarter pickup: the A part ends on a two-beat bar at its repeat sign (completing
        // the pickup), then the B part has its own pickup, together a full bar with the bar before.
        var notes = Perform("D4/q |: G4/h. | A4/h :| B4/h B4/q | C5/h D5/q | B4/h", "3/4", pickup: 1);
        Assert.Equal(new[] { -1.0, 0, 3, 5, 8, 10, 12, 13, 15, 16 }, notes.Select(n => n.StartBeat));
        Assert.Equal(18, notes[^1].EndBeat);
        // A short bar in the middle of a section is still an error.
        Assert.Throws<SongFormatException>(() => Perform("G4/q G4 G4 A4 | B4/h | G4/w | G4/w"));
    }

    [Theory]
    [InlineData("G4/x")]
    [InlineData("H4/q")]
    [InlineData("G4/q..")]
    public void Garbage_is_rejected(string music) =>
        Assert.Throws<SongFormatException>(() => Perform(music + " G4/q G4/h"));

    [Fact]
    public void Three_four_and_six_eight_bars()
    {
        Assert.Equal(4, Perform("G4/h. | A4/q B4 C5", "3/4").Count);
        Assert.Equal(2, Perform("G4/q. A4/q. | B4/h.", "6/8").Count(n => n.Bar == 1));
    }

    [Fact]
    public void Json_header_and_titles()
    {
        var song = SongParser.ParseJson("""
            { "id": "x", "title": { "fr": "Au clair", "en": "Moonlight" }, "chapter": 1, "tempo": 92,
              "time": "4/4", "key": "G", "parts": [ { "id": "melody", "music": "G4/w | A4/w" } ] }
            """);
        Assert.Equal("Au clair", song.Title("fr"));
        Assert.Equal("Moonlight", song.Title("nl")); // falls back to English
        Assert.Equal(92, song.Tempo);
        Assert.Equal((67, 69), song.Range);
        Assert.Equal(8, song.TotalBeats);
    }
}

public class WrittenBarTests
{
    private static (IReadOnlyList<Bar> Bars, IReadOnlyList<PerformedNote> Performance) Parse(string music) =>
        SongParser.ParseMusic(music, new TimeSignature(4, 4), 0, "test");

    [Fact]
    public void Repeat_and_ending_marks_are_kept_on_the_written_bars()
    {
        var (bars, _) = Parse("|: G4/w | [1 A4/w :| [2 B4/w || C5/w");
        Assert.Equal(4, bars.Count);
        Assert.True(bars[0].StartRepeat);
        Assert.Equal(1, bars[1].Ending);
        Assert.True(bars[1].EndRepeat);
        Assert.Equal(2, bars[2].Ending);
        Assert.True(bars[2].DoubleBarAfter);
        Assert.Equal(0, bars[3].Ending);
    }

    [Fact]
    public void Performed_notes_point_back_to_written_notes()
    {
        var (bars, played) = Parse("|: G4/h A4~ | A4/w :|");
        var g = bars[0].Notes[0].Id;
        // Played twice: both performances of G come from the same written note.
        Assert.Equal(new[] { g }, played[0].WrittenIds);
        Assert.Equal(new[] { g }, played[2].WrittenIds);
        // The tied A is one performed note from two written ones.
        Assert.Equal(new[] { bars[0].Notes[1].Id, bars[1].Notes[0].Id }, played[1].WrittenIds);
    }
}
