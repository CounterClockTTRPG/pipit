using Pipit.Core.Music;
using Xunit;

namespace Pipit.Core.Tests;

public class PitchTests
{
    [Theory]
    [InlineData("C4", 60)]
    [InlineData("A4", 69)]
    [InlineData("F#4", 66)]
    [InlineData("Gb4", 66)]
    [InlineData("Bb3", 58)]
    [InlineData("C5", 72)]
    [InlineData("D7", 98)]
    public void Parse_gives_midi(string text, int midi) => Assert.Equal(midi, Pitch.Parse(text).Midi);

    [Theory]
    [InlineData("H4")]
    [InlineData("C")]
    [InlineData("C##4")]
    [InlineData("")]
    public void Parse_rejects_garbage(string text) => Assert.Throws<FormatException>(() => Pitch.Parse(text));

    [Fact]
    public void Round_trips_through_text() => Assert.Equal("F#5", Pitch.Parse("F#5").ToString());

    [Fact]
    public void Frequency_of_reference_and_octaves()
    {
        Assert.Equal(440.0, Pitch.Parse("A4").Frequency(), 6);
        Assert.Equal(880.0, Pitch.Parse("A5").Frequency(), 6);
        Assert.Equal(442.0, Pitch.Parse("A4").Frequency(442), 6);
        Assert.Equal(523.2511, Pitch.Parse("C5").Frequency(), 3); // soprano recorder's lowest note
    }

    [Fact]
    public void Frequency_and_midi_are_inverse()
    {
        for (var midi = 60; midi <= 98; midi++)
            Assert.Equal(midi, Pitch.MidiOfFrequency(Pitch.FrequencyOfMidi(midi)), 9);
    }

    [Fact]
    public void Cents_between_semitone_and_octave()
    {
        Assert.Equal(1200, Pitch.CentsBetween(440, 880), 9);
        Assert.Equal(100, Pitch.CentsBetween(440, Pitch.FrequencyOfMidi(70)), 9);
        Assert.Equal(-100, Pitch.CentsBetween(Pitch.FrequencyOfMidi(70), 440), 9);
    }

    [Fact]
    public void Soprano_sounds_an_octave_above_written() =>
        Assert.Equal(Pitch.Parse("C5"), Pitch.Parse("C4").Transpose(1));
}

public class NoteNamesTests
{
    [Theory]
    [InlineData("C4", "Do")]
    [InlineData("D4", "Re")]
    [InlineData("G4", "Sol")]
    [InlineData("B4", "Si")]
    [InlineData("F#4", "Fa♯")]
    [InlineData("Bb4", "Si♭")]
    public void Fixed_do(string pitch, string expected) =>
        Assert.Equal(expected, NoteNames.Name(Pitch.Parse(pitch)));

    [Fact]
    public void French_re_has_an_accent()
    {
        Assert.Equal("Ré", NoteNames.Name(Pitch.Parse("D5"), language: "fr"));
        Assert.Equal("Re", NoteNames.Name(Pitch.Parse("D5"), language: "nl"));
    }

    [Fact]
    public void Letters() => Assert.Equal("C♯", NoteNames.Name(Pitch.Parse("C#4"), NoteNaming.Letters));
}
