using Pipit.Core.Analysis;
using Pipit.Core.Game;
using Pipit.Core.Scoring;
using Pipit.Core.Synthesis;
using Pipit.Core.Music;
using Xunit;

namespace Pipit.Core.Tests;

public class LevelTests
{
    [Fact]
    public void Steps_grow_by_fifty()
    {
        Assert.Equal(0, Levels.Threshold(1));
        Assert.Equal(100, Levels.Threshold(2));
        Assert.Equal(250, Levels.Threshold(3));
        Assert.Equal(450, Levels.Threshold(4));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(249, 2)]
    [InlineData(250, 3)]
    public void Level_for_xp(long xp, int level) => Assert.Equal(level, Levels.LevelFor(xp));

    [Fact]
    public void Fraction_to_next_level() => Assert.Equal(0.5, Levels.Fraction(175), 6);
}

public class ProgressTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 18, 0, 0);

    [Fact]
    public void First_play_of_the_day_earns_a_bonus_once()
    {
        var p = new PlayerProgress();
        var first = p.Award(new[] { new XpLine("x", 10) }, Monday);
        var second = p.Award(new[] { new XpLine("x", 10) }, Monday.AddHours(1));
        Assert.Equal(30, first.Total);
        Assert.Equal(10, second.Total);
        Assert.Equal(1, p.StreakDays);
    }

    [Fact]
    public void Streak_grows_daily_and_resets_after_a_gap()
    {
        var p = new PlayerProgress();
        p.Award(Array.Empty<XpLine>(), Monday);
        var tuesday = p.Award(Array.Empty<XpLine>(), Monday.AddDays(1));
        Assert.Equal(2, p.StreakDays);
        Assert.Equal(25, tuesday.Total);
        p.Award(Array.Empty<XpLine>(), Monday.AddDays(3)); // skipped Wednesday
        Assert.Equal(1, p.StreakDays);
        Assert.Equal(2, p.BestStreak);
    }

    [Fact]
    public void Level_up_is_reported()
    {
        var p = new PlayerProgress { Xp = 90, LastPlayDay = DateOnly.FromDateTime(Monday) };
        var award = p.Award(new[] { new XpLine("x", 15) }, Monday);
        Assert.True(award.LevelledUp);
        Assert.Equal(2, award.LevelAfter);
    }

    [Fact]
    public void Rhythm_run_xp_and_records()
    {
        var record = new SongRecord();
        var lines = XpRules.RhythmRun(record, hits: 20, stars: 2, accuracy: 85, Monday);
        Assert.Equal(40 + 20 + 25, lines.Sum(l => l.Amount)); // notes, stars, new star record
        Assert.Equal(2, record.BestStars);
        // Same stars, better accuracy: a personal best.
        Assert.Contains(XpRules.RhythmRun(record, 20, 2, 90, Monday), l => l.Reason == "Personal best");
    }

    [Fact]
    public void Repeating_a_song_all_day_earns_less()
    {
        var record = new SongRecord();
        var amounts = Enumerable.Range(0, 5).Select(_ => XpRules.CasualRun(record, 10, 10, 100, Monday).Sum(l => l.Amount)).ToList();
        Assert.Equal(new[] { 20, 20, 20, 10, 10 }, amounts);
        Assert.Equal(20, XpRules.CasualRun(record, 10, 10, 100, Monday.AddDays(1)).Sum(l => l.Amount));
    }
}

public class RecorderVoiceTests
{
    [Fact]
    public void Preview_is_heard_as_the_notes_it_plays()
    {
        // The synth fed back into Pipit's own ears: Sol La Si, tongued, at 90 bpm.
        const int rate = 48000;
        var notes = new[] { 79, 81, 83 }.Select((m, i) => new SynthNote(0.2 + i * 0.667, 0.667, Pitch.FrequencyOfMidi(m))).ToList();
        var detected = NoteSegmenter.Segment(PitchTracker.Analyse(RecorderVoice.Render(notes, rate), rate));
        Assert.Equal(new[] { 79, 81, 83 }, detected.Select(d => d.Semitone));
        for (var i = 0; i < 3; i++)
            Assert.InRange(detected[i].Onset, notes[i].Start - 0.01, notes[i].Start + 0.04);
    }
}

public class AchievementTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 18, 0, 0);
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<string>> Chapters = new Dictionary<int, IReadOnlyList<string>>
    {
        [1] = new[] { "a", "b" },
    };

    private static GradedRun Run(int perfect, int missed, double tempo = 1)
    {
        var notes = Enumerable.Range(0, perfect + missed).Select(i => new Pipit.Core.Scoring.NoteJudgement(
            new Pipit.Core.Scoring.ExpectedNote(i, 79, i, i + 1),
            i < perfect ? Pipit.Core.Scoring.NoteResult.Hit : Pipit.Core.Scoring.NoteResult.Missed,
            null, 0, i < perfect ? Pipit.Core.Scoring.Timing.Perfect : null, true, true, 0)).ToList();
        return new GradedRun(notes, 0, tempo);
    }

    private static XpAward Play(PlayerProgress p, string song, GradedRun run, double tempo = 1) =>
        p.Award(new[] { new XpLine("x", 1) }, Now, new AchievementEvent { SongId = song, Run = run, TempoFactor = tempo }, Chapters, "soprano");

    [Fact]
    public void First_song_three_stars_perfect_ten_and_clean_run_together()
    {
        var p = new PlayerProgress();
        var award = Play(p, "a", Run(12, 0));
        Assert.Equal(new[] { "first-song", "first-star", "three-stars", "perfect-ten", "clean-run" }, award.Unlocked.Select(a => a.Id));
        Assert.Equal(1 + 20 + 20 + 20 + 50 + 40 + 50, award.Total); // XP line, daily bonus, then the achievements
    }

    [Fact]
    public void Each_achievement_unlocks_once()
    {
        var p = new PlayerProgress();
        Play(p, "a", Run(12, 0));
        Assert.Empty(Play(p, "a", Run(12, 0)).Unlocked);
    }

    [Fact]
    public void Chapter_goal_needs_a_star_on_every_song()
    {
        var p = new PlayerProgress();
        p.Song("a", "soprano").BestStars = 1;
        Assert.DoesNotContain(Play(p, "a", Run(1, 9)).Unlocked, a => a.Id == "chapter-1");
        p.Song("b", "soprano").BestStars = 2;
        Assert.Contains(Play(p, "b", Run(1, 9)).Unlocked, a => a.Id == "chapter-1");
    }

    [Fact]
    public void Achievement_xp_can_reach_level_five()
    {
        var p = new PlayerProgress { Xp = Levels.Threshold(5) - 30, LastPlayDay = DateOnly.FromDateTime(Now) };
        var award = Play(p, "a", Run(12, 0));
        Assert.Contains(award.Unlocked, a => a.Id == "level-5");
    }

    [Fact]
    public void Slow_practice_is_rewarded() =>
        Assert.Contains(Play(new PlayerProgress(), "a", Run(1, 3, tempo: 0.6), 0.6).Unlocked, a => a.Id == "slow-and-steady");
}
