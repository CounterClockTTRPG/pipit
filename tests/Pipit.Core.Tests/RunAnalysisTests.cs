using Pipit.Core.Analysis;
using Pipit.Core.Scoring;
using Xunit;

namespace Pipit.Core.Tests;

public class RunAnalysisTests
{
    /// <summary>Sol La Sol La … quarter notes at 1 s, then two long Si (2 s each).</summary>
    private static List<ExpectedNote> Tune()
    {
        var notes = new List<ExpectedNote>();
        for (var i = 0; i < 8; i++)
            notes.Add(new ExpectedNote(i, i % 2 == 0 ? 79 : 81, 1 + i, 2 + i));
        notes.Add(new ExpectedNote(8, 83, 9, 11));
        notes.Add(new ExpectedNote(9, 83, 11, 13));
        return notes;
    }

    private static List<Finding> Analyse(Func<ExpectedNote, DetectedNote?> play)
    {
        var tune = Tune();
        return RunAnalysis.Analyse(Grader.Grade(tune, tune.Select(play).OfType<DetectedNote>().ToList(), Tolerances.Normal));
    }

    private static DetectedNote Note(double onset, double length, double midi) => new(onset, onset + length, midi, 3, -10);

    [Fact]
    public void A_clean_run_lists_strengths_only()
    {
        var findings = Analyse(e => Note(e.Start + 0.01, (e.End - e.Start) * 0.92, e.SoundingMidi));
        Assert.All(findings, f => Assert.True(f.Positive));
        Assert.Contains(findings, f => f.Kind == FindingKind.RhythmSteady);
        Assert.Contains(findings, f => f.Kind == FindingKind.LongNotesHeld);
    }

    [Fact]
    public void Rushing_and_short_long_notes()
    {
        var findings = Analyse(e => Note(e.Start - 0.07, Math.Min(0.8, (e.End - e.Start) * 0.9), e.SoundingMidi));
        Assert.Contains(findings, f => f.Kind == FindingKind.RhythmEarly && f.Value is > 60 and < 80);
        var longShort = Assert.Single(findings, f => f.Kind == FindingKind.LongNotesShort);
        Assert.Equal(2, longShort.Total);
    }

    [Fact]
    public void Uneven_rhythm()
    {
        var rng = new Random(4);
        var findings = Analyse(e => Note(e.Start + (rng.NextDouble() - 0.5) * 0.24, 0.9, e.SoundingMidi));
        Assert.Contains(findings, f => f.Kind == FindingKind.RhythmUneven);
    }

    [Fact]
    public void One_note_consistently_high()
    {
        // La (81) 28 cents sharp every time; the rest in tune.
        var findings = Analyse(e => Note(e.Start, (e.End - e.Start) * 0.9, e.SoundingMidi + (e.SoundingMidi == 81 ? 0.28 : 0)));
        var high = Assert.Single(findings, f => f.Kind == FindingKind.NoteHigh);
        Assert.Equal(81, high.SoundingMidi);
        Assert.InRange(high.Value, 25, 31);
        Assert.Contains(findings, f => f.Kind == FindingKind.NoteInTune && f.SoundingMidi == 79);
    }

    [Fact]
    public void A_hard_note_and_merged_repeats()
    {
        // La is never found; the two long Si are blown as one note.
        var findings = Analyse(e => e.SoundingMidi == 81 ? null
            : e.Index == 9 ? null
            : Note(e.Start, e.Index == 8 ? 3.9 : 0.9, e.SoundingMidi));
        Assert.Contains(findings, f => f.Kind == FindingKind.NoteHard && f.SoundingMidi == 81 && f.Count == 4);
        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.RepeatedNotesMerged); // only one merge: not enough evidence
    }
}
