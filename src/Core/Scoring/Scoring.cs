using Pipit.Core.Analysis;

namespace Pipit.Core.Scoring;

public enum Timing { Perfect, Great, Good, Early, Late }

public enum NoteResult { Hit, WrongNote, Missed }

/// <summary>All grading thresholds in one place; difficulty is a choice of preset.</summary>
public sealed record Tolerances(string Name, double PitchCents, double PerfectMs, double GreatMs, double GoodMs, double ShortRatio, double MatchWindowMs)
{
    public static readonly Tolerances Relaxed = new("Relaxed", 45, 70, 120, 180, 0.6, 350);
    public static readonly Tolerances Normal = new("Normal", 35, 50, 90, 140, 0.75, 250);
    public static readonly Tolerances Strict = new("Strict", 20, 35, 60, 100, 0.85, 200);
    public static readonly IReadOnlyList<Tolerances> All = new[] { Relaxed, Normal, Strict };
}

/// <summary>An expected note in seconds on the capture clock (or any clock the detected notes share).</summary>
public sealed record ExpectedNote(int Index, int SoundingMidi, double Start, double End);

public sealed record NoteJudgement(ExpectedNote Expected, NoteResult Result, DetectedNote? Played, double OnsetErrorMs, Timing? Timing, bool InTune, bool HeldLongEnough, double CentsOff)
{
    /// <summary>0-100: a wrong or missing note scores nothing; timing, tuning and length shade a hit.</summary>
    public double Score => Result != NoteResult.Hit ? 0 :
        100 * (Timing switch { Scoring.Timing.Perfect => 1.0, Scoring.Timing.Great => 0.9, Scoring.Timing.Good => 0.75, _ => 0.5 })
            * (InTune ? 1 : 0.8) * (HeldLongEnough ? 1 : 0.8);
}

public sealed record Performance(IReadOnlyList<NoteJudgement> Notes, int Extras, double TempoFactor)
{
    public int Hits => Notes.Count(n => n.Result == NoteResult.Hit);

    /// <summary>Average note score, less 1 % per extra note (at most 10 %), as a percentage.</summary>
    public double Accuracy => Notes.Count == 0 ? 0 : Math.Max(0, Notes.Average(n => n.Score) - Math.Min(10, Extras));

    /// <summary>Stars only count at full tempo or faster: practising slowly is for learning.</summary>
    public int Stars => TempoFactor < 0.999 ? 0 : Accuracy >= 95 ? 3 : Accuracy >= 80 ? 2 : Accuracy >= 60 ? 1 : 0;

    /// <summary>Median onset error of hits; positive means late (dragging), negative early (rushing).</summary>
    public double TimingTendencyMs => Stats.Median(Notes.Where(n => n.Result == NoteResult.Hit).Select(n => n.OnsetErrorMs));

    /// <summary>Wrong notes grouped as (expected, played) with counts, most frequent first.</summary>
    public IReadOnlyList<(int Expected, int Played, int Count)> Confusions => Notes
        .Where(n => n.Result == NoteResult.WrongNote && n.Played is not null)
        .GroupBy(n => (n.Expected.SoundingMidi, n.Played!.Semitone))
        .Select(g => (g.Key.SoundingMidi, g.Key.Semitone, g.Count()))
        .OrderByDescending(c => c.Item3).ToList();
}

/// <summary>
/// Matches what was played against what was expected and judges each note. Matching is greedy in
/// time order and one-to-one: each expected note takes the closest unused played note within the
/// window, preferring one with the right pitch.
/// </summary>
public static class Grader
{
    public static Performance Grade(IReadOnlyList<ExpectedNote> expected, IReadOnlyList<DetectedNote> played, Tolerances tol,
        double latencySeconds = 0, double tuningOffsetCents = 0, double tempoFactor = 1, double? until = null)
    {
        var used = new bool[played.Count];
        var window = tol.MatchWindowMs / 1000;
        var judgements = new List<NoteJudgement>();
        double Corrected(DetectedNote d) => d.Midi - tuningOffsetCents / 100;

        foreach (var e in expected.OrderBy(e => e.Start))
        {
            var target = e.Start + latencySeconds;
            if (until is { } u && target + window > u)
                break; // not decidable yet (live grading)
            var candidates = Enumerable.Range(0, played.Count)
                .Where(i => !used[i] && Math.Abs(played[i].Onset - target) <= window)
                .ToList();
            if (candidates.Count == 0)
            {
                judgements.Add(new NoteJudgement(e, NoteResult.Missed, null, 0, null, false, false, 0));
                continue;
            }
            var right = candidates.Where(i => (int)Math.Round(Corrected(played[i])) == e.SoundingMidi).ToList();
            var pick = (right.Count > 0 ? right : candidates).OrderBy(i => Math.Abs(played[i].Onset - target)).First();
            used[pick] = true;
            var d = played[pick];
            var errorMs = (d.Onset - target) * 1000;
            if (right.Count == 0)
            {
                judgements.Add(new NoteJudgement(e, NoteResult.WrongNote, d, errorMs, null, false, false, 0));
                continue;
            }
            var abs = Math.Abs(errorMs);
            var timing = abs <= tol.PerfectMs ? Timing.Perfect : abs <= tol.GreatMs ? Timing.Great : abs <= tol.GoodMs ? Timing.Good
                : errorMs < 0 ? Timing.Early : Timing.Late;
            var cents = (Corrected(d) - e.SoundingMidi) * 100;
            // Held long enough: compared with the written length, less a little articulation.
            var held = d.Duration >= (e.End - e.Start) * tol.ShortRatio - 0.06;
            judgements.Add(new NoteJudgement(e, NoteResult.Hit, d, errorMs, timing, Math.Abs(cents) <= tol.PitchCents, held, cents));
        }

        // Extras: unmatched notes within the graded span, ignoring very quiet blips and squeaks
        // shorter than 80 ms (they are attack noise rather than notes).
        var end = judgements.Count == 0 ? double.NegativeInfinity : judgements.Max(j => j.Expected.End) + latencySeconds;
        var extras = Enumerable.Range(0, played.Count)
            .Count(i => !used[i] && played[i].Onset <= end && played[i].Duration >= 0.08);
        return new Performance(judgements, extras, tempoFactor);
    }
}
