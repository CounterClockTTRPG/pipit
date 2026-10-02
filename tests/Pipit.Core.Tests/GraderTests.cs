using Pipit.Core.Analysis;
using Pipit.Core.Scoring;
using Xunit;

namespace Pipit.Core.Tests;

public class GraderTests
{
    private static readonly List<ExpectedNote> Scale = new()
    {
        new(0, 79, 1.0, 2.0), new(1, 81, 2.0, 3.0), new(2, 83, 3.0, 4.0), new(3, 84, 4.0, 6.0),
    };

    private static DetectedNote Played(double onset, double length, double midi) => new(onset, onset + length, midi, 3, -10);

    [Fact]
    public void Perfect_run()
    {
        var played = Scale.Select(e => Played(e.Start + 0.02, e.End - e.Start - 0.05, e.SoundingMidi + 0.05)).ToList();
        var p = Grader.Grade(Scale, played, Tolerances.Normal);
        Assert.All(p.Notes, n => Assert.Equal(Timing.Perfect, n.Timing));
        Assert.Equal(100, p.Accuracy, 6);
        Assert.Equal(3, p.Stars);
    }

    [Fact]
    public void Latency_is_subtracted()
    {
        var played = Scale.Select(e => Played(e.Start + 0.1, 0.9, e.SoundingMidi)).ToList();
        Assert.All(Grader.Grade(Scale, played, Tolerances.Normal, latencySeconds: 0.1).Notes, n => Assert.Equal(Timing.Perfect, n.Timing));
        Assert.All(Grader.Grade(Scale, played, Tolerances.Normal).Notes, n => Assert.Equal(Timing.Good, n.Timing)); // 100 ms late
    }

    [Fact]
    public void Timing_grades()
    {
        var played = new[] { Played(1.04, 0.9, 79), Played(2.08, 0.9, 81), Played(2.88, 0.9, 83), Played(4.2, 1.8, 84) };
        var timings = Grader.Grade(Scale, played, Tolerances.Normal).Notes.Select(n => n.Timing).ToArray();
        Assert.Equal(new Timing?[] { Timing.Perfect, Timing.Great, Timing.Good, Timing.Late }, timings);
    }

    [Fact]
    public void Wrong_missed_and_extra()
    {
        var played = new[] { Played(1.0, 0.9, 79), Played(2.0, 0.9, 80), Played(3.6, 0.3, 90), Played(4.0, 1.9, 84) };
        var p = Grader.Grade(Scale, played, Tolerances.Normal);
        Assert.Equal(new[] { NoteResult.Hit, NoteResult.WrongNote, NoteResult.Missed, NoteResult.Hit }, p.Notes.Select(n => n.Result));
        Assert.Equal(1, p.Extras); // the squeak at 3.6 s
        Assert.Equal((81, 80, 1), Assert.Single(p.Confusions));
    }

    [Fact]
    public void Right_pitch_is_preferred_over_the_nearest()
    {
        // A short wrong blip right on the beat, then the right note 60 ms later.
        var played = new[] { Played(1.0, 0.1, 78), Played(1.06, 0.9, 79) };
        var n = Grader.Grade(Scale.Take(1).ToList(), played, Tolerances.Normal).Notes[0];
        Assert.Equal(NoteResult.Hit, n.Result);
        Assert.Equal(Timing.Great, n.Timing);
    }

    [Fact]
    public void Tuning_offset_and_tolerance()
    {
        // 45 cents sharp: out of tune on Normal, in tune once a +20 c recorder offset is allowed for.
        var played = new[] { Played(1.0, 0.9, 79.45) };
        var one = Scale.Take(1).ToList();
        Assert.False(Grader.Grade(one, played, Tolerances.Normal).Notes[0].InTune);
        Assert.True(Grader.Grade(one, played, Tolerances.Normal, tuningOffsetCents: 20).Notes[0].InTune);
    }

    [Fact]
    public void Short_notes_lose_a_little()
    {
        var n = Grader.Grade(Scale.Take(1).ToList(), new[] { Played(1.0, 0.3, 79) }, Tolerances.Normal).Notes[0];
        Assert.False(n.HeldLongEnough);
        Assert.Equal(80, n.Score, 6);
    }

    [Fact]
    public void No_stars_below_full_tempo() =>
        Assert.Equal(0, Grader.Grade(Scale, Scale.Select(e => Played(e.Start, 0.9, e.SoundingMidi)).ToList(), Tolerances.Normal, tempoFactor: 0.8).Stars);

    [Fact]
    public void Live_grading_waits_until_a_note_is_decidable()
    {
        var p = Grader.Grade(Scale, new[] { Played(1.0, 0.9, 79) }, Tolerances.Normal, until: 2.1);
        Assert.Single(p.Notes); // the note at 2.0 s still has 150 ms of window left
    }
}

public class ExerciseVerdictTests
{
    private static NoteJudgement J(int i, NoteResult r, Timing? t, bool inTune = true, bool held = true, double cents = 0) =>
        new(new ExpectedNote(i, 79, i, i + 1), r, null, 0, t, inTune, held, cents);

    [Fact]
    public void The_users_screenshot_passes_with_tips()
    {
        // As reported: one late note (amber), one wrong (red), six on time but sharp and short.
        var notes = new List<NoteJudgement> { J(0, NoteResult.Hit, Timing.Late, false, false, 45) };
        notes.AddRange(Enumerable.Range(1, 3).Select(i => J(i, NoteResult.Hit, Timing.Good, false, false, 40)));
        notes.Add(J(4, NoteResult.WrongNote, null));
        notes.AddRange(Enumerable.Range(5, 3).Select(i => J(i, NoteResult.Hit, Timing.Great, false, false, 42)));
        var run = new GradedRun(notes, 0, 1);
        Assert.True(run.Accuracy < 60); // a graded song would fail this...
        var verdict = ExerciseVerdict.Of(run);
        Assert.True(verdict.Passed);    // ...but the exercise asked for the right notes on the beat
        Assert.Equal(6, verdict.OnTime);
        Assert.Equal(1, verdict.TuningTip);
        Assert.True(verdict.LengthTip);
    }

    [Fact]
    public void Too_few_on_time_fails()
    {
        var notes = Enumerable.Range(0, 8).Select(i => J(i, i < 5 ? NoteResult.Hit : NoteResult.Missed, Timing.Perfect)).ToList();
        Assert.False(ExerciseVerdict.Of(new GradedRun(notes, 0, 1)).Passed); // 5 of 8
    }
}
