using Pipit.Core.Instruments;
using Pipit.Core.Music;
using Xunit;

namespace Pipit.Core.Tests;

public class InstrumentTests
{
    private static FingeringChart Chart(FingeringSystem s) => FingeringChart.Load(Repo.Data("fingerings"), s);

    [Theory]
    [InlineData(RecorderKind.Soprano, 72, 98)]
    [InlineData(RecorderKind.Alto, 65, 91)]
    [InlineData(RecorderKind.Tenor, 60, 86)]
    public void Sounding_ranges(RecorderKind kind, int bottom, int top)
    {
        var info = RecorderInfo.Of(kind);
        Assert.Equal(bottom, info.BottomSoundingMidi);
        Assert.Equal(top, info.TopSoundingMidi);
    }

    [Fact]
    public void Soprano_and_tenor_share_written_notes_and_differ_by_an_octave()
    {
        var soprano = RecorderInfo.Of(RecorderKind.Soprano);
        var tenor = RecorderInfo.Of(RecorderKind.Tenor);
        var g4 = Pitch.Parse("G4").Midi;
        Assert.Equal(soprano.StepOf(g4), tenor.StepOf(g4));
        Assert.Equal(12, soprano.WrittenToSounding(g4) - tenor.WrittenToSounding(g4));
    }

    [Fact]
    public void Alto_all_closed_is_fa() =>
        Assert.Equal(0, RecorderInfo.Of(RecorderKind.Alto).StepOf(Pitch.Parse("F4").Midi));

    [Fact]
    public void Out_of_range_has_no_step()
    {
        Assert.Null(RecorderInfo.Of(RecorderKind.Soprano).StepOf(Pitch.Parse("B3").Midi));
        Assert.Null(RecorderInfo.Of(RecorderKind.Soprano).StepOf(Pitch.Parse("E6").Midi));
    }

    [Theory]
    [InlineData(FingeringSystem.Baroque)]
    [InlineData(FingeringSystem.German)]
    public void Charts_cover_the_whole_range_with_distinct_fingerings(FingeringSystem system)
    {
        var chart = Chart(system);
        var codes = Enumerable.Range(0, RecorderInfo.RangeSemitones + 1).Select(s => chart.Primary(s).Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Theory]
    [InlineData(0, "xxxxxxxx")]  // Do: everything closed
    [InlineData(5, "xxxxxoxx")]  // Fa: the baroque forked fingering
    [InlineData(6, "xxxxoxxo")]  // Fa#
    [InlineData(7, "xxxxoooo")]  // Sol
    [InlineData(11, "xxoooooo")] // Si
    [InlineData(12, "xoxooooo")] // Do'
    [InlineData(14, "ooxooooo")] // Re'
    [InlineData(16, "pxxxxxoo")] // Mi': first pinched-thumb note
    public void Baroque_landmarks(int step, string code) =>
        Assert.Equal(code, Chart(FingeringSystem.Baroque).Primary(step).Code);

    [Fact]
    public void German_differs_only_on_fa_and_fa_sharp()
    {
        var baroque = Chart(FingeringSystem.Baroque);
        var german = Chart(FingeringSystem.German);
        var differing = Enumerable.Range(0, RecorderInfo.RangeSemitones + 1)
            .Where(s => baroque.Primary(s) != german.Primary(s)).ToArray();
        Assert.Equal(new[] { 5, 6, 17, 18 }, differing);
        Assert.Equal("xxxxxooo", german.Primary(5).Code);
    }

    [Fact]
    public void Fingering_codes_round_trip_and_validate()
    {
        Assert.Equal("pxoxxoxxb", Fingering.Parse("pxoxxoxxb").Code);
        Assert.Equal(HoleState.Half, Fingering.Parse("xxxxxxho").Holes[5]);
        Assert.Throws<FormatException>(() => Fingering.Parse("xxxx"));
        Assert.Throws<FormatException>(() => Fingering.Parse("xxhxxxxx")); // only 6/7 can be half
        Assert.Throws<FormatException>(() => Fingering.Parse("xpxxxxxx")); // only the thumb pinches
    }

    [Fact]
    public void Lookup_by_fingering()
    {
        var chart = Chart(FingeringSystem.Baroque);
        Assert.Equal(9, chart.StepOf(Fingering.Parse("xxxooooo")));
        Assert.Null(chart.StepOf(Fingering.Parse("oooooooo")));
    }
}
