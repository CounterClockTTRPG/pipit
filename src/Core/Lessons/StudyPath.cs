using System.Text.Json;
using Pipit.Core.Game;

namespace Pipit.Core.Lessons;

public enum StepKind { Lesson, Song }

public sealed record PathStep(StepKind Kind, string Id)
{
    public string Key => $"{Kind.ToString().ToLowerInvariant()}:{Id}";
}

public sealed record PathChapter(int Number, IReadOnlyDictionary<string, string> Titles, IReadOnlyList<PathStep> Steps)
{
    public string Title(string language) => Titles.TryGetValue(language, out var t) ? t : Titles.GetValueOrDefault("en", "");
}

public enum StepState { Done, Current, Locked }

/// <summary>
/// The study path: chapters of lessons and songs, done in order. A lesson is done when its tasks
/// are; a song when it has a star, or was played through in casual mode finding most notes first
/// time (so a player who is not ready for the beat can still move on).
/// </summary>
public sealed class StudyPath
{
    public IReadOnlyList<PathChapter> Chapters { get; }

    private StudyPath(IReadOnlyList<PathChapter> chapters) => Chapters = chapters;

    public IEnumerable<PathStep> Steps => Chapters.SelectMany(c => c.Steps);

    public static StudyPath Parse(string json)
    {
        var chapters = new List<PathChapter>();
        foreach (var c in JsonDocument.Parse(json).RootElement.EnumerateArray())
        {
            var steps = new List<PathStep>();
            foreach (var s in c.GetProperty("steps").EnumerateArray())
            {
                if (s.TryGetProperty("lesson", out var l))
                    steps.Add(new PathStep(StepKind.Lesson, l.GetString()!));
                else if (s.TryGetProperty("song", out var song))
                    steps.Add(new PathStep(StepKind.Song, song.GetString()!));
                else
                    throw new InvalidDataException("A path step needs \"lesson\" or \"song\"");
            }
            chapters.Add(new PathChapter(c.GetProperty("chapter").GetInt32(),
                c.GetProperty("title").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? ""), steps));
        }
        return new StudyPath(chapters);
    }

    public static bool SongDone(SongRecord? record) => record is not null && (record.BestStars >= 1 || record.BestCasualAccuracy >= 80);

    public bool IsDone(PathStep step, PlayerProgress progress, string recorderKind) => step.Kind switch
    {
        StepKind.Lesson => progress.LessonsDone.Contains(step.Id),
        _ => SongDone(progress.Songs.GetValueOrDefault(PlayerProgress.SongKey(step.Id, recorderKind))),
    };

    /// <summary>Done steps, then the first not-done step is current, everything after it locked.</summary>
    public StepState StateOf(PathStep step, PlayerProgress progress, string recorderKind)
    {
        foreach (var s in Steps)
        {
            if (s == step)
                return IsDone(s, progress, recorderKind) ? StepState.Done : StepState.Current;
            if (!IsDone(s, progress, recorderKind))
                return StepState.Locked;
        }
        return StepState.Locked;
    }

    public PathStep? Current(PlayerProgress progress, string recorderKind) =>
        Steps.FirstOrDefault(s => !IsDone(s, progress, recorderKind));

    public PathStep? After(PathStep step) => Steps.SkipWhile(s => s != step).Skip(1).FirstOrDefault();
}
