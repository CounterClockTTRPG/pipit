using System;
using System.Collections.Generic;
using System.Linq;
using Pipit.Core.Game;
using Pipit.Core.Lessons;

namespace Pipit.App.Lessons;

/// <summary>
/// Review pages: a lesson page written on the spot around one exercise from
/// <see cref="Review.Write"/>, made of the notes and rhythms learned so far, with the player's
/// weakest notes coming up more often.
/// </summary>
public static class ReviewLessons
{
    /// <summary>
    /// A review for a path step (what came before it), or for everything learned so far when
    /// <paramref name="step"/> is null. Null when nothing has been taught yet.
    /// </summary>
    public static Lesson? Make(PathStep? step)
    {
        var s = Services.Instance;
        var kind = s.Instrument.Kind.ToString();
        var upTo = step ?? s.Study.Current(s.Progress, kind);
        var material = Review.Taught(s.Study, upTo, s.LoadLesson, s.Songs.Get);
        if (material.Notes.Count < 2)
            return null;
        double Weakness(int n) => s.Progress.FindNote(PlayerProgress.Playing, kind, n)?.Weakness ?? 0.5;
        var exercise = Review.Write(material, Weakness, Environment.TickCount);
        var weak = material.Notes.Where(n => s.Progress.FindNote(PlayerProgress.Playing, kind, n) is { Played: > 0, Missed: > 0 })
            .OrderByDescending(Weakness).Take(2).Select(n => s.NoteName(n)).ToList();
        var directive = $"::exercise \"{{0}}\" music=\"{exercise.Music}\" key=\"{exercise.Key}\" tempo=72";

        string Page(string heading, string intro, string weakLine, string play) =>
            $"# {heading}\n\n{intro}{(weak.Count > 0 ? " " + string.Format(weakLine, string.Join(", ", weak)) : "")}\n\n" + string.Format(directive, play);
        var pages = new Dictionary<string, IReadOnlyList<LessonBlock>>
        {
            ["en"] = LessonParser.Parse(Page("Review",
                "An exercise made of the notes and rhythms you have learned so far. Notes you found hard come up more often.",
                "Today: {0}.", "Play it through"), "review/en"),
            ["nl"] = LessonParser.Parse(Page("Herhaling",
                "Een oefening met de noten en ritmes die je tot nu toe geleerd hebt. Noten die je moeilijk vond, komen vaker terug.",
                "Vandaag: {0}.", "Speel ze helemaal"), "review/nl"),
            ["fr"] = LessonParser.Parse(Page("Révision",
                "Un exercice fait des notes et des rythmes que tu as appris jusqu'ici. Les notes qui t'ont donné du mal reviennent plus souvent.",
                "Aujourd'hui : {0}.", "Joue-le en entier"), "review/fr"),
        };
        return new Lesson
        {
            Id = step is null ? "review:free" : StudyPath.ReviewDoneKey(step.Id),
            Titles = new Dictionary<string, string> { ["en"] = "Review", ["nl"] = "Herhaling", ["fr"] = "Révision" },
            Pages = pages,
        };
    }
}
