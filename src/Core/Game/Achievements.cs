using Pipit.Core.Scoring;

namespace Pipit.Core.Game;

public sealed record Achievement(string Id, string Title, string Description, int Xp, string Icon);

/// <summary>What just happened, for checking achievements. Fields not relevant to the event stay default.</summary>
public sealed record AchievementEvent
{
    public string? SongId { get; init; }
    public bool Casual { get; init; }
    public GradedRun? Run { get; init; }
    public double CasualAccuracy { get; init; }
    public double TempoFactor { get; init; } = 1;
    public SurvivalState? Survival { get; init; }
}

/// <summary>
/// The achievements and their conditions. Checked after every award; each unlocks once and
/// brings its XP. Chapter goals need the song list, so they are passed in.
/// </summary>
public static class Achievements
{
    public static readonly IReadOnlyList<Achievement> All = new[]
    {
        new Achievement("first-lesson", "First lesson", "Finish a lesson on the study path.", 20, "📖"),
        new Achievement("first-song", "First song", "Play a song all the way through.", 20, "♪"),
        new Achievement("first-star", "First star", "Earn a star on any song.", 20, "★"),
        new Achievement("three-stars", "Three stars", "Earn all three stars on a song.", 50, "★★★"),
        new Achievement("perfect-ten", "Perfect ten", "Play 10 notes in a row with Perfect timing.", 40, "10"),
        new Achievement("clean-run", "Clean run", "Play a song in rhythm without a wrong or missed note.", 50, "✓"),
        new Achievement("casual-clean", "Found them all", "Play a song in casual mode, finding every note first time.", 30, "🔍"),
        new Achievement("survivor", "Survivor", "Reach the end of a song in survival mode.", 50, "❤"),
        new Achievement("combo-30", "On a roll", "Reach a combo of 30 notes in survival mode.", 40, "x4"),
        new Achievement("slow-and-steady", "Slow and steady", "Finish a song at 70 % speed or slower. Practising slowly is how musicians learn.", 20, "🐢"),
        new Achievement("chapter-1", "Si La Sol", "Earn at least one star on every song in chapter 1.", 60, "1"),
        new Achievement("chapter-2", "High notes", "Earn at least one star on every song in chapter 2.", 80, "2"),
        new Achievement("space-musician", "Space musician", "Play Jingle Bells, the first song played in space.", 25, "🚀"),
        new Achievement("trainer-25", "Nimble fingers", "Find 25 notes in the fingering trainer.", 30, "✋"),
        new Achievement("streak-3", "Three days", "Practise three days in a row.", 30, "3"),
        new Achievement("streak-7", "A whole week", "Practise seven days in a row.", 70, "7"),
        new Achievement("practice-30", "Half an hour", "Practise for 30 minutes in total.", 40, "⏱"),
        new Achievement("level-5", "Level 5", "Reach level 5.", 50, "5"),
    };

    public static Achievement Get(string id) => All.First(a => a.Id == id);

    /// <summary>Achievements newly earned by this event (not yet recorded in the progress).</summary>
    public static List<Achievement> Check(PlayerProgress progress, AchievementEvent e, IReadOnlyDictionary<int, IReadOnlyList<string>> songsByChapter, string recorderKind)
    {
        var earned = new List<Achievement>();
        void Check(string id, bool condition)
        {
            if (condition && !progress.Achievements.ContainsKey(id))
                earned.Add(Get(id));
        }

        var run = e.Run;
        Check("first-lesson", progress.LessonsDone.Count > 0);
        Check("first-song", e.SongId is not null);
        Check("first-star", run?.Stars >= 1);
        Check("three-stars", run?.Stars == 3);
        Check("perfect-ten", run is not null && LongestPerfectRun(run) >= 10);
        Check("clean-run", run is not null && run.Notes.Count > 0 && run.Notes.All(n => n.Result == NoteResult.Hit));
        Check("casual-clean", e.Casual && e.CasualAccuracy >= 100);
        Check("slow-and-steady", e.SongId is not null && e.TempoFactor <= 0.7);
        Check("space-musician", e.SongId == "jingle-bells");
        Check("survivor", e.Survival is { Alive: true });
        Check("combo-30", e.Survival?.BestCombo >= 30);
        foreach (var (chapter, id) in new[] { (1, "chapter-1"), (2, "chapter-2") })
            Check(id, songsByChapter.TryGetValue(chapter, out var songs) && songs.Count > 0
                && songs.All(s => progress.Songs.TryGetValue(PlayerProgress.SongKey(s, recorderKind), out var r) && r.BestStars >= 1));
        Check("trainer-25", progress.TrainerNotes >= 25);
        Check("streak-3", progress.StreakDays >= 3);
        Check("streak-7", progress.StreakDays >= 7);
        Check("practice-30", progress.PracticeMinutes >= 30);
        Check("level-5", progress.Level >= 5);
        return earned;
    }

    private static int LongestPerfectRun(GradedRun run)
    {
        int best = 0, current = 0;
        foreach (var n in run.Notes.OrderBy(n => n.Expected.Start))
        {
            current = n.Result == NoteResult.Hit && n.Timing == Timing.Perfect ? current + 1 : 0;
            best = Math.Max(best, current);
        }
        return best;
    }
}
