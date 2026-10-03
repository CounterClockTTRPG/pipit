namespace Pipit.Core.Game;

/// <summary>How a song went for one recorder size: bests and play count.</summary>
public sealed class SongRecord
{
    public double BestAccuracy { get; set; }
    public int BestStars { get; set; }
    public double BestCasualAccuracy { get; set; }
    public int Plays { get; set; }
    public DateTime? LastPlayed { get; set; }
    /// <summary>Plays on <see cref="PlaysDay"/>, for diminishing returns on repeats.</summary>
    public int PlaysToday { get; set; }
    public DateOnly? PlaysDay { get; set; }
}

/// <summary>
/// How well one note is known, for one skill (playing it from its name or fingering, or reading
/// it) and one recorder size. <see cref="Box"/> is a Leitner box: right answers move a note up
/// (0-5), a miss sends it back to 1, so the notes in low boxes are the ones to practise.
/// </summary>
public sealed class NoteStat
{
    public const int TopBox = 5;
    public int Played { get; set; }
    public int Missed { get; set; }
    public int Box { get; set; }
    public DateTime? LastSeen { get; set; }
    /// <summary>Average time to find the note when right (seconds), 0 until known.</summary>
    public double AverageSeconds { get; set; }

    /// <summary>0 (known well) to 1 (often missed), with a mild prior so one miss is not 100 %.</summary>
    public double Weakness => (Missed + 1.0) / (Played + 2.0);

    public void Record(bool right, double seconds, DateTime now)
    {
        Played++;
        LastSeen = now;
        if (right)
        {
            Box = Math.Min(TopBox, Box + 1);
            // Only timed answers (the trainer) count towards the average; songs pass 0.
            if (seconds > 0)
                AverageSeconds = AverageSeconds == 0 ? seconds : AverageSeconds * 0.7 + seconds * 0.3;
        }
        else
        {
            Missed++;
            Box = Math.Min(Box, 1);
        }
    }
}

public sealed record XpLine(string Reason, int Amount);

public sealed record XpAward(IReadOnlyList<XpLine> Lines, int LevelBefore, int LevelAfter)
{
    public IReadOnlyList<Achievement> Unlocked { get; init; } = Array.Empty<Achievement>();

    public int Total => Lines.Sum(l => l.Amount);
    public bool LevelledUp => LevelAfter > LevelBefore;
}

/// <summary>
/// A player's experience, levels, daily streak and song records. One player until M4 adds
/// profiles. Regular play is what is rewarded: every run earns XP, the first session of the day
/// earns a bonus that grows with the streak, and replaying one song all day earns less.
/// </summary>
public sealed class PlayerProgress
{
    public int SchemaVersion { get; set; } = 1;
    public long Xp { get; set; }
    public DateOnly? LastPlayDay { get; set; }
    public int StreakDays { get; set; }
    public int BestStreak { get; set; }
    public double PracticeMinutes { get; set; }
    public int TrainerNotes { get; set; }
    /// <summary>Study-path lessons completed (their tasks all done).</summary>
    public HashSet<string> LessonsDone { get; set; } = new();
    /// <summary>Survival leaderboards by song key ("songId|recorderKind"), best first.</summary>
    public Dictionary<string, List<ScoreEntry>> Leaderboards { get; set; } = new();
    /// <summary>Unlocked achievements by id, with when.</summary>
    public Dictionary<string, DateTime> Achievements { get; set; } = new();
    /// <summary>Keyed by "songId|recorderKind".</summary>
    public Dictionary<string, SongRecord> Songs { get; set; } = new();
    /// <summary>Per-note learning records, keyed by "skill|recorderKind|writtenMidi" (see <see cref="NoteKey"/>).</summary>
    public Dictionary<string, NoteStat> Notes { get; set; } = new();
    /// <summary>How many notes the fingering trainer's learning mode has brought in, per "skill|recorderKind".</summary>
    public Dictionary<string, int> NotesIntroduced { get; set; } = new();

    /// <summary>Skills that keep per-note records.</summary>
    public const string Playing = "play", Reading = "read";

    public static string NoteKey(string skill, string recorderKind, int writtenMidi) => $"{skill}|{recorderKind.ToLowerInvariant()}|{writtenMidi}";

    public NoteStat Note(string skill, string recorderKind, int writtenMidi)
    {
        var key = NoteKey(skill, recorderKind, writtenMidi);
        if (!Notes.TryGetValue(key, out var stat))
            Notes[key] = stat = new NoteStat();
        return stat;
    }

    /// <summary>The record if there is one (without creating it).</summary>
    public NoteStat? FindNote(string skill, string recorderKind, int writtenMidi) =>
        Notes.GetValueOrDefault(NoteKey(skill, recorderKind, writtenMidi));

    public int Level => Levels.LevelFor(Xp);

    public static string SongKey(string songId, string recorderKind) => $"{songId}|{recorderKind.ToLowerInvariant()}";

    public SongRecord Song(string songId, string recorderKind)
    {
        var key = SongKey(songId, recorderKind);
        if (!Songs.TryGetValue(key, out var record))
            Songs[key] = record = new SongRecord();
        return record;
    }

    /// <summary>
    /// Applies an award and unlocks any achievements it earns (their XP is added too). The check
    /// runs after the XP so that streaks and levels reached by this award count.
    /// </summary>
    public XpAward Award(IEnumerable<XpLine> lines, DateTime now, AchievementEvent e, IReadOnlyDictionary<int, IReadOnlyList<string>> songsByChapter, string recorderKind)
    {
        var award = Award(lines, now);
        var all = award.Lines.ToList();
        var unlocked = new List<Achievement>();
        // Unlocking can raise the level, which can unlock "level-5": repeat until nothing new.
        for (var found = Game.Achievements.Check(this, e, songsByChapter, recorderKind); found.Count > 0; found = Game.Achievements.Check(this, e, songsByChapter, recorderKind))
            foreach (var a in found)
            {
                Achievements[a.Id] = now;
                Xp += a.Xp;
                all.Add(new XpLine("Achievement: " + a.Title, a.Xp));
                unlocked.Add(a);
            }
        return new XpAward(all, award.LevelBefore, Level) { Unlocked = unlocked };
    }

    /// <summary>Applies an award: adds the daily/streak bonus when this is the day's first, then the XP.</summary>
    public XpAward Award(IEnumerable<XpLine> lines, DateTime now)
    {
        var all = lines.Where(l => l.Amount > 0).ToList();
        var today = DateOnly.FromDateTime(now);
        if (LastPlayDay != today)
        {
            StreakDays = LastPlayDay == today.AddDays(-1) ? StreakDays + 1 : 1;
            BestStreak = Math.Max(BestStreak, StreakDays);
            LastPlayDay = today;
            all.Add(new XpLine(StreakDays > 1 ? $"Day {StreakDays} in a row" : "First practice today", XpRules.DailyBonus(StreakDays)));
        }
        var before = Level;
        Xp += all.Sum(l => l.Amount);
        return new XpAward(all, before, Level);
    }
}

public static class Levels
{
    /// <summary>XP needed to go from <paramref name="level"/> to the next: 100, 150, 200, …</summary>
    public static long StepFrom(int level) => 100 + 50L * (level - 1);

    /// <summary>Total XP at which <paramref name="level"/> is reached (level 1 at 0).</summary>
    public static long Threshold(int level)
    {
        long total = 0;
        for (var l = 1; l < level; l++)
            total += StepFrom(l);
        return total;
    }

    public static int LevelFor(long xp)
    {
        var level = 1;
        while (xp >= Threshold(level + 1))
            level++;
        return level;
    }

    /// <summary>Progress towards the next level, 0..1.</summary>
    public static double Fraction(long xp)
    {
        var level = LevelFor(xp);
        return (double)(xp - Threshold(level)) / StepFrom(level);
    }
}

public static class XpRules
{
    public const int RepeatsAtFullXp = 3;

    public static int DailyBonus(int streakDays) => 20 + 5 * Math.Min(streakDays - 1, 6);

    /// <summary>XP for a song played in rhythm, updating the song's record.</summary>
    public static List<XpLine> RhythmRun(SongRecord record, int hits, int stars, double accuracy, DateTime now)
    {
        var lines = new List<XpLine> { new("Notes played", hits * 2) };
        if (stars > 0)
            lines.Add(new XpLine($"{stars} star{(stars > 1 ? "s" : "")}", stars * 10));
        if (stars > record.BestStars)
            lines.Add(new XpLine("New star record", 25));
        else if (record.Plays > 0 && accuracy > record.BestAccuracy + 0.5)
            lines.Add(new XpLine("Personal best", 15));
        record.BestStars = Math.Max(record.BestStars, stars);
        record.BestAccuracy = Math.Max(record.BestAccuracy, accuracy);
        return Played(record, lines, now);
    }

    /// <summary>XP for a song played in casual mode: one per note, one more per note found first time.</summary>
    public static List<XpLine> CasualRun(SongRecord record, int notes, int firstTry, double accuracy, DateTime now)
    {
        var lines = new List<XpLine> { new("Notes found", notes), new("Found first time", firstTry) };
        record.BestCasualAccuracy = Math.Max(record.BestCasualAccuracy, accuracy);
        return Played(record, lines, now);
    }

    /// <summary>XP for a survival run: notes played, plus a share of the score, plus a bonus for surviving.</summary>
    public static List<XpLine> SurvivalRun(SongRecord record, int hits, long score, bool survived, DateTime now)
    {
        var lines = new List<XpLine> { new("Notes played", hits * 2), new("Survival score", (int)Math.Min(60, score / 500)) };
        if (survived)
            lines.Add(new XpLine("Survived to the end", 30));
        return Played(record, lines, now);
    }

    public static List<XpLine> Lesson(bool firstTime) => new() { new("Lesson finished", firstTime ? 25 : 5) };

    public static List<XpLine> Trainer(int correct) => new() { new("Fingering trainer", correct) };

    public static XpLine Practice(double minutes) => new("Practice time", (int)Math.Floor(minutes * 2));

    /// <summary>Counts the play; the fourth and later plays of a song on one day earn half.</summary>
    private static List<XpLine> Played(SongRecord record, List<XpLine> lines, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (record.PlaysDay != today)
        {
            record.PlaysDay = today;
            record.PlaysToday = 0;
        }
        record.PlaysToday++;
        record.Plays++;
        record.LastPlayed = now;
        if (record.PlaysToday > RepeatsAtFullXp)
            lines = lines.Select(l => l with { Amount = l.Amount / 2 }).Append(new XpLine("Played often today: half XP", 0)).ToList();
        return lines;
    }
}
