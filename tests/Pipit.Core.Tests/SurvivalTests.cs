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
    public void Each_note_is_graded_for_the_callout()
    {
        var notes = new[] { J(0, NoteResult.Hit, Timing.Perfect), J(1, NoteResult.Hit, Timing.Great), J(2, NoteResult.Hit, Timing.Good),
            J(3, NoteResult.Hit, Timing.Early), J(4, NoteResult.Hit, Timing.Late), J(5, NoteResult.Missed), J(6, NoteResult.WrongNote) };
        Assert.Equal(new[] { Grade.Perfect, Grade.Great, Grade.Good, Grade.Early, Grade.Late, Grade.Miss, Grade.Wrong },
            Callouts.From(notes, 0).Select(c => c.Grade));
    }

    [Fact]
    public void Streaks_call_out_milestones_and_a_long_streak_that_breaks()
    {
        var run = Perfects(25).Append(J(25, NoteResult.Missed)).Concat(Perfects(3, 26)).ToList();
        var all = Callouts.From(run, 0);
        Assert.Equal(new int?[] { 10, 20 }, all.Where(c => c.Milestone is not null).Select(c => c.Milestone));
        Assert.Equal(2, all[9].Multiplier);
        Assert.Equal(25, all[25].StreakLost);
        Assert.Equal(0, all[25].Streak);
        Assert.Equal(3, all[^1].Streak);
        // Only the new ones are returned, but the streak counts from the start.
        var tail = Callouts.From(run, 20);
        Assert.Equal(9, tail.Count);
        Assert.Equal(21, tail[0].Streak);
        // A short streak that breaks is not worth a callout.
        Assert.Null(Callouts.From(Perfects(3).Append(J(3, NoteResult.Missed)), 3).Single().StreakLost);
        Assert.True(Callouts.IsMilestone(50));
        Assert.False(Callouts.IsMilestone(40));
    }

    [Fact]
    public void A_full_combo_is_every_note_hit()
    {
        Assert.True(Callouts.FullCombo(Survival.Evaluate(Perfects(12)), 12));
        Assert.False(Callouts.FullCombo(Survival.Evaluate(Perfects(11)), 12));
        Assert.False(Callouts.FullCombo(Survival.Evaluate(Perfects(5).Append(J(5, NoteResult.Missed)).Concat(Perfects(6, 6))), 12));
    }

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

public class ComputerBoardTests
{
    private static ScoreEntry E(string who, long score, int minute) => new(who, score, 0.9, 1, 10, 1, new DateTime(2026, 10, 3, 12, minute, 0));

    [Fact]
    public void The_computer_board_merges_every_players_runs_best_first()
    {
        var raf = new[] { E("Raf", 900, 1), E("Raf", 400, 2) };
        var mia = new[] { E("Mia", 1200, 3), E("Mia", 400, 0) };
        var board = Leaderboard.Combined(new[] { raf, mia });
        Assert.Equal(new[] { "Mia", "Raf", "Mia", "Raf" }, board.Select(e => e.Player));
        Assert.Equal(1200, board[0].Score);
        Assert.Equal(3, Leaderboard.Combined(new[] { raf, mia }, size: 3).Count);
    }

    [Fact]
    public void The_crown_goes_to_the_most_xp_when_there_is_someone_to_beat()
    {
        Assert.Null(Levels.CrownHolder(new[] { ("a", 500L) }));                       // alone
        Assert.Equal("b", Levels.CrownHolder(new[] { ("a", 500L), ("b", 900L), ("c", 10L) }));
        Assert.Null(Levels.CrownHolder(new[] { ("a", 500L), ("b", 500L) }));           // a tie
        Assert.Null(Levels.CrownHolder(new[] { ("a", 0L), ("b", 0L) }));               // nobody has played
    }
}
