using Pipit.Core.Scoring;

namespace Pipit.Core.Game;

public sealed record SurvivalState(double Life, long Score, int Combo, int BestCombo, int Multiplier, bool Alive, int Judged, double? FailedAt);

/// <summary>
/// Survival scoring, replayed from the judgements so far (in time order). Life starts full;
/// good notes give a little back, bad ones cost more, and at zero the run is over. Points per
/// note depend on timing and are multiplied by the combo (x1, rising by one every 10 notes in
/// a row without a miss or wrong note, up to x4).
/// </summary>
public static class Survival
{
    public const double MaxLife = 100;

    public static (int Points, double Life) Value(NoteJudgement j) => j.Result switch
    {
        NoteResult.Missed => (0, -15),
        NoteResult.WrongNote => (0, -12),
        _ => j.Timing switch
        {
            Timing.Perfect => (100, +4),
            Timing.Great => (70, +2),
            Timing.Good => (40, 0),
            _ => (15, -5), // early or late: the right note, but off the beat
        },
    };

    public static SurvivalState Evaluate(IEnumerable<NoteJudgement> judgements)
    {
        double life = MaxLife;
        long score = 0;
        int combo = 0, best = 0, judged = 0;
        double? failedAt = null;
        foreach (var j in judgements.OrderBy(j => j.Expected.Start))
        {
            judged++;
            var (points, delta) = Value(j);
            var hit = j.Result == NoteResult.Hit;
            combo = hit ? combo + 1 : 0;
            best = Math.Max(best, combo);
            score += points * MultiplierFor(combo);
            // An off-tune hit costs a little life too: in tune matters in survival.
            life = Math.Clamp(life + delta - (hit && !j.InTune ? 2 : 0), 0, MaxLife);
            if (life <= 0)
            {
                failedAt = j.Expected.Start;
                break;
            }
        }
        return new SurvivalState(life, score, combo, best, MultiplierFor(combo), failedAt is null, judged, failedAt);
    }

    public static int MultiplierFor(int combo) => Math.Min(4, 1 + combo / 10);
}

/// <summary>One leaderboard entry. <see cref="Reached"/> is how far into the song the run got (0-1).</summary>
public sealed record ScoreEntry(string Player, long Score, double Accuracy, double Reached, int BestCombo, double TempoFactor, DateTime When);

public static class Leaderboard
{
    public const int Size = 10;

    /// <summary>Adds an entry to a song's board (best first) and returns its rank, or null if it did not make the board.</summary>
    public static int? Add(PlayerProgress progress, string songKey, ScoreEntry entry)
    {
        if (!progress.Leaderboards.TryGetValue(songKey, out var board))
            progress.Leaderboards[songKey] = board = new List<ScoreEntry>();
        board.Add(entry);
        board.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : a.When.CompareTo(b.When));
        var rank = board.IndexOf(entry) + 1;
        if (board.Count > Size)
            board.RemoveRange(Size, board.Count - Size);
        return rank <= Size ? rank : null;
    }
}
