using Pipit.Core.Game;
using Pipit.Core.Scoring;
using Xunit;

namespace Pipit.Core.Tests;

public class SurvivalTests
{
    private static NoteJudgement J(int i, NoteResult result, Timing? timing = Timing.Perfect, bool inTune = true) =>
        new(new ExpectedNote(i, 79, i, i + 1), result, null, 0, result == NoteResult.Hit ? timing : null, inTune, true, 0);

    private static IEnumerable<NoteJudgement> Perfects(int count, int from = 0) =>
        Enumerable.Range(from, count).Select(i => J(i, NoteResult.Hit));

    [Fact]
    public void Combo_raises_the_multiplier_every_ten_notes_up_to_four()
    {
        Assert.Equal(1, Survival.MultiplierFor(9));
        Assert.Equal(2, Survival.MultiplierFor(10));
        Assert.Equal(4, Survival.MultiplierFor(30));
        Assert.Equal(4, Survival.MultiplierFor(99));
        var s = Survival.Evaluate(Perfects(12));
        // Nine at x1, the next three at x2 (combo 10, 11, 12).
        Assert.Equal(9 * 100 + 3 * 200, s.Score);
        Assert.Equal(12, s.BestCombo);
    }

    [Fact]
    public void A_miss_breaks_the_combo_and_costs_life()
    {
        var s = Survival.Evaluate(Perfects(5).Append(J(5, NoteResult.Missed)));
        Assert.Equal(0, s.Combo);
        Assert.Equal(5, s.BestCombo);
        Assert.Equal(Survival.MaxLife - 15, s.Life); // life was already full
        Assert.True(s.Alive);
    }

    [Fact]
    public void Too_many_mistakes_end_the_run_where_they_happen()
    {
        var misses = Enumerable.Range(0, 10).Select(i => J(i, NoteResult.Missed));
        var s = Survival.Evaluate(misses);
        Assert.False(s.Alive);
        Assert.Equal(7, s.Judged);        // 7 x 15 = 105 > 100
        Assert.Equal(6, s.FailedAt);
    }

    [Fact]
    public void Good_notes_win_life_back()
    {
        var s = Survival.Evaluate(new[] { J(0, NoteResult.WrongNote), J(1, NoteResult.WrongNote) }.Concat(Perfects(3, 2)));
        Assert.Equal(100 - 24 + 12, s.Life);
    }

    [Fact]
    public void Off_tune_hits_cost_a_little()
    {
        var s = Survival.Evaluate(new[] { J(0, NoteResult.Missed), J(1, NoteResult.Hit, Timing.Good, inTune: false) });
        Assert.Equal(100 - 15 - 2, s.Life);
    }

    [Fact]
    public void Leaderboard_keeps_the_best_ten_and_reports_ranks()
    {
        var p = new PlayerProgress();
        var now = new DateTime(2026, 10, 1);
        for (var i = 1; i <= 10; i++)
            Leaderboard.Add(p, "s|soprano", new ScoreEntry("You", i * 100, 50, 1, 5, 1, now.AddMinutes(i)));
        Assert.Equal(1, Leaderboard.Add(p, "s|soprano", new ScoreEntry("You", 5000, 90, 1, 30, 1, now.AddHours(1))));
        Assert.Null(Leaderboard.Add(p, "s|soprano", new ScoreEntry("You", 50, 10, 0.2, 1, 1, now.AddHours(2))));
        Assert.Equal(10, p.Leaderboards["s|soprano"].Count);
        Assert.Equal(5000, p.Leaderboards["s|soprano"][0].Score);
    }
}
