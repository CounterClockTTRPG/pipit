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

public sealed record XpLine(string Reason, int Amount);

public sealed record XpAward(IReadOnlyList<XpLine> Lines, int LevelBefore, int LevelAfter)
{
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
    /// <summary>Keyed by "songId|recorderKind".</summary>
    public Dictionary<string, SongRecord> Songs { get; set; } = new();

    public int Level => Levels.LevelFor(Xp);

    public static string SongKey(string songId, string recorderKind) => $"{songId}|{recorderKind.ToLowerInvariant()}";

    public SongRecord Song(string songId, string recorderKind)
    {
        var key = SongKey(songId, recorderKind);
        if (!Songs.TryGetValue(key, out var record))
            Songs[key] = record = new SongRecord();
        return record;
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
