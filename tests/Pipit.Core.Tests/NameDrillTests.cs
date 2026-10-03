using Pipit.Core.Practice;
using Pipit.Core.Scoring;
using Xunit;

namespace Pipit.Core.Tests;

public class NameDrillTests
{
    private static readonly (int, double)[] Notes = { (71, 1.0), (69, 2.0), (67, 3.0), (72, 4.0) }; // Si La Sol Do', 1 s apart

    [Fact]
    public void Letters_ignore_the_octave_and_reject_accidentals()
    {
        Assert.Equal(0, NameDrill.LetterOf(60));
        Assert.Equal(0, NameDrill.LetterOf(72));
        Assert.Equal(6, NameDrill.LetterOf(71));
        Assert.Equal(-1, NameDrill.LetterOf(66));
    }

    [Fact]
    public void Right_names_on_the_beat()
    {
        var answers = new[] { new NameAnswer(6, 1.05), new NameAnswer(5, 1.9), new NameAnswer(4, 3.2), new NameAnswer(0, 4.3) };
        var j = NameDrill.Grade(Notes, answers, 1.0);
        Assert.Equal(new[] { Timing.Perfect, Timing.Great, Timing.Good, Timing.Late }, j.Select(x => x.Timing!.Value));
        Assert.All(j, x => Assert.Equal(NameResult.Right, x.Result));
        Assert.Equal(3, j.Count(x => x.RightOnTime));
    }

    [Fact]
    public void Wrong_names_and_missed_notes()
    {
        var answers = new[] { new NameAnswer(5, 1.0), new NameAnswer(4, 3.0), new NameAnswer(4, 3.1) };
        var j = NameDrill.Grade(Notes, answers, 1.0);
        Assert.Equal(new[] { NameResult.WrongName, NameResult.Missed, NameResult.Right, NameResult.Missed }, j.Select(x => x.Result));
    }

    [Fact]
    public void An_answer_counts_for_one_note_only()
    {
        // One tap halfway between two beats belongs to the first only.
        var j = NameDrill.Grade(Notes, new[] { new NameAnswer(6, 1.5) }, 1.0);
        Assert.Equal(NameResult.Right, j[0].Result);
        Assert.Equal(NameResult.Missed, j[1].Result);
    }
}
