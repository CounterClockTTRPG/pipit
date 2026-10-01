namespace Pipit.Core.Scoring;

public enum FindingKind
{
    // Rhythm
    RhythmSteady, RhythmEarly, RhythmLate, RhythmUneven,
    // Note lengths
    LongNotesHeld, LongNotesShort, NotesShort,
    // Tuning, per note
    NoteInTune, NoteHigh, NoteLow,
    // Accuracy, per note
    NoteSure, NoteHard, RepeatedNotesMerged,
}

/// <summary>
/// One observation about a run. <see cref="SoundingMidi"/> names the note for per-note findings;
/// <see cref="Value"/> is milliseconds, cents or a ratio depending on the kind; Count/Total give
/// "3 of 5". The app turns findings into sentences.
/// </summary>
public sealed record Finding(FindingKind Kind, bool Positive, int? SoundingMidi = null, double Value = 0, int Count = 0, int Total = 0);

/// <summary>
/// Strengths and weaknesses of a graded rhythm run: timing (tendency and steadiness), whether long
/// notes are held, tuning and accuracy per note, and repeated notes run together. Only findings
/// with enough evidence are reported (at least two notes, clear thresholds), strongest first.
/// </summary>
public static class RunAnalysis
{
    public static List<Finding> Analyse(GradedRun run)
    {
        var findings = new List<Finding>();
        var hits = run.Notes.Where(n => n.Result == NoteResult.Hit).ToList();
        if (run.Notes.Count == 0)
            return findings;

        // Rhythm: the median says early or late, the spread says steady or not.
        if (hits.Count >= 4)
        {
            var errors = hits.Select(h => h.OnsetErrorMs).ToList();
            var tendency = Analysis.Stats.Median(errors);
            var spread = Analysis.Stats.MedianAbsoluteDeviation(errors);
            if (tendency < -40)
                findings.Add(new Finding(FindingKind.RhythmEarly, false, Value: -tendency));
            else if (tendency > 40)
                findings.Add(new Finding(FindingKind.RhythmLate, false, Value: tendency));
            if (spread > 45)
                findings.Add(new Finding(FindingKind.RhythmUneven, false, Value: spread));
            else if (Math.Abs(tendency) <= 40)
                findings.Add(new Finding(FindingKind.RhythmSteady, true, Value: spread));
        }

        // Note lengths: long notes are those at least twice the typical length.
        var typical = Analysis.Stats.Median(run.Notes.Select(n => n.Expected.End - n.Expected.Start));
        var withLength = hits.Where(h => h.Played is not null).ToList();
        var longOnes = withLength.Where(h => h.Expected.End - h.Expected.Start >= typical * 1.9).ToList();
        if (longOnes.Count >= 2)
        {
            var ratio = Analysis.Stats.Median(longOnes.Select(h => h.Played!.Duration / (h.Expected.End - h.Expected.Start)));
            findings.Add(ratio < 0.75
                ? new Finding(FindingKind.LongNotesShort, false, Value: ratio, Count: longOnes.Count(h => !h.HeldLongEnough), Total: longOnes.Count)
                : new Finding(FindingKind.LongNotesHeld, true, Value: ratio));
        }
        var shortCount = withLength.Count(h => !h.HeldLongEnough);
        if (withLength.Count >= 4 && shortCount >= withLength.Count / 3 && !findings.Any(f => f.Kind == FindingKind.LongNotesShort))
            findings.Add(new Finding(FindingKind.NotesShort, false, Count: shortCount, Total: withLength.Count));

        // Per note: tuning (from hits) and how often it was found (from all attempts).
        foreach (var group in run.Notes.GroupBy(n => n.Expected.SoundingMidi).OrderBy(g => g.Key))
        {
            var noteHits = group.Where(n => n.Result == NoteResult.Hit).ToList();
            if (noteHits.Count >= 2)
            {
                var cents = Analysis.Stats.Median(noteHits.Select(n => n.CentsOff));
                if (cents > 20)
                    findings.Add(new Finding(FindingKind.NoteHigh, false, group.Key, cents));
                else if (cents < -20)
                    findings.Add(new Finding(FindingKind.NoteLow, false, group.Key, cents));
                else if (noteHits.Count >= 3 && Math.Abs(cents) <= 10)
                    findings.Add(new Finding(FindingKind.NoteInTune, true, group.Key, cents));
            }
            var total = group.Count();
            var failed = total - noteHits.Count;
            if (total >= 2 && failed * 2 >= total)
                findings.Add(new Finding(FindingKind.NoteHard, false, group.Key, Count: failed, Total: total));
            else if (total >= 4 && failed == 0)
                findings.Add(new Finding(FindingKind.NoteSure, true, group.Key, Count: total, Total: total));
        }

        // Repeated notes run together: a missed note straight after a hit on the same pitch.
        var ordered = run.Notes.OrderBy(n => n.Expected.Start).ToList();
        var merged = 0;
        var repeats = 0;
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Expected.SoundingMidi != ordered[i - 1].Expected.SoundingMidi)
                continue;
            repeats++;
            if (ordered[i].Result == NoteResult.Missed && ordered[i - 1].Result == NoteResult.Hit)
                merged++;
        }
        if (merged >= 2)
            findings.Add(new Finding(FindingKind.RepeatedNotesMerged, false, Count: merged, Total: repeats));

        // Weaknesses first (most to work on), then strengths; keep it short.
        return findings.Where(f => !f.Positive).Take(5).Concat(findings.Where(f => f.Positive).Take(4)).ToList();
    }
}
