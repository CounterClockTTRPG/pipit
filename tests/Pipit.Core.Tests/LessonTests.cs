using Pipit.Core.Game;
using Pipit.Core.Lessons;
using Xunit;

namespace Pipit.Core.Tests;

public class LessonParserTests
{
    [Fact]
    public void Markdown_subset_and_directives()
    {
        var blocks = LessonParser.Parse("""
            # Your first note

            Hold the recorder with the **left hand** on top.
            Blow gently.

            - thumb on the back hole
            - first finger on hole 1

            ::fingering note=B4
            ::try "Play Si and hold it" note=B4 hold=1.5
            ::quiz "Which hand goes on top?" options="Left* | Right"
            """);
        Assert.Equal(new[] { "HeadingBlock", "ParagraphBlock", "ListBlock", "DirectiveBlock", "DirectiveBlock", "DirectiveBlock" },
            blocks.Select(b => b.GetType().Name));
        Assert.Equal("Hold the recorder with the **left hand** on top. Blow gently.", ((ParagraphBlock)blocks[1]).Text);
        Assert.Equal(2, ((ListBlock)blocks[2]).Items.Count);
        var tryIt = (DirectiveBlock)blocks[4];
        Assert.Equal("try", tryIt.Name);
        Assert.Equal("Play Si and hold it", tryIt.Text);
        Assert.Equal(1.5, tryIt.Number("hold", 0));
        Assert.True(tryIt.IsTask);
        var (options, correct) = LessonParser.QuizOptions((DirectiveBlock)blocks[5]);
        Assert.Equal(new[] { "Left", "Right" }, options);
        Assert.Equal(0, correct);
    }

    [Fact]
    public void Escaped_quotes_inside_quoted_text()
    {
        var pip = (DirectiveBlock)LessonParser.Parse("""::pip "Tongue every note with \"du\", so each starts clearly." mood=happy""")[0];
        Assert.Equal("Tongue every note with \"du\", so each starts clearly.", pip.Text);
        Assert.Equal("happy", pip.Arg("mood"));
    }

    [Fact]
    public void Quiz_without_an_answer_is_an_error() =>
        Assert.Throws<InvalidDataException>(() => LessonParser.QuizOptions((DirectiveBlock)LessonParser.Parse("::quiz \"Q\" options=\"A | B\"")[0]));
}

public class StudyPathTests
{
    private static readonly StudyPath Path = StudyPath.Parse("""
        [ { "chapter": 1, "title": { "en": "First notes" }, "steps": [ { "lesson": "a" }, { "lesson": "b" }, { "song": "s" } ] },
          { "chapter": 2, "title": { "en": "Higher" }, "steps": [ { "lesson": "c" } ] } ]
        """);

    [Fact]
    public void Steps_unlock_in_order()
    {
        var p = new PlayerProgress();
        var steps = Path.Steps.ToList();
        Assert.Equal(new[] { StepState.Current, StepState.Locked, StepState.Locked, StepState.Locked }, steps.Select(s => Path.StateOf(s, p, "soprano")));
        p.LessonsDone.Add("a");
        Assert.Equal(StepState.Done, Path.StateOf(steps[0], p, "soprano"));
        Assert.Equal(StepState.Current, Path.StateOf(steps[1], p, "soprano"));
    }

    [Fact]
    public void A_song_counts_with_a_star_or_a_good_casual_run()
    {
        var p = new PlayerProgress();
        p.LessonsDone.UnionWith(new[] { "a", "b" });
        var song = Path.Steps.ElementAt(2);
        Assert.Equal(StepState.Current, Path.StateOf(song, p, "soprano"));
        p.Song("s", "soprano").BestCasualAccuracy = 85;
        Assert.Equal(StepState.Done, Path.StateOf(song, p, "soprano"));
        Assert.Equal("c", Path.Current(p, "soprano")!.Id);
    }

    [Fact]
    public void Song_progress_is_per_recorder_size()
    {
        var p = new PlayerProgress();
        p.LessonsDone.UnionWith(new[] { "a", "b" });
        p.Song("s", "soprano").BestStars = 2;
        Assert.Equal(StepState.Current, Path.StateOf(Path.Steps.ElementAt(2), p, "alto"));
    }
}

/// <summary>Checks on the real lesson and path content in data/.</summary>
public class LessonContentTests
{
    private static string? Read(string id, string file)
    {
        var path = Repo.Data("lessons", id, file);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static StudyPath Path => StudyPath.Parse(File.ReadAllText(Repo.Data("path.json")));

    public static IEnumerable<object[]> LessonIds() =>
        Directory.GetDirectories(Repo.Data("lessons")).Select(d => new object[] { System.IO.Path.GetFileName(d) });

    [Theory]
    [MemberData(nameof(LessonIds))]
    public void Lesson_is_complete_and_valid(string id)
    {
        var lesson = LessonParser.Load(id, f => Read(id, f));
        var soprano = Pipit.Core.Instruments.RecorderInfo.Of(Pipit.Core.Instruments.RecorderKind.Soprano);
        foreach (var language in new[] { "en", "nl" })
        {
            Assert.True(lesson.Pages.ContainsKey(language), $"{id}: no {language} page");
            Assert.True(lesson.Titles.ContainsKey(language), $"{id}: no {language} title");
            var blocks = lesson.Page(language).OfType<DirectiveBlock>().ToList();
            Assert.Contains(blocks, b => b.IsTask); // every lesson asks the player to do something
            foreach (var b in blocks)
            {
                switch (b.Name)
                {
                    case "fingering" or "try":
                        Assert.True(soprano.InRange(Pipit.Core.Music.Pitch.Parse(b.Arg("note")).Midi), $"{id}: {b.Arg("note")}");
                        break;
                    case "staff" or "play" or "exercise":
                        var (bars, played) = Pipit.Core.Music.SongParser.ParseMusic(b.Arg("music"), Pipit.Core.Music.TimeSignature.Parse(b.Arg("time", "4/4")), 0, $"{id}/{language}");
                        Assert.All(played, n => Assert.True(soprano.InRange(n.Pitch.Midi), $"{id}: {n.Pitch}"));
                        break;
                    case "quiz":
                        LessonParser.QuizOptions(b);
                        break;
                    case "pip":
                        Assert.False(string.IsNullOrWhiteSpace(b.Text));
                        break;
                    default:
                        Assert.Fail($"{id}: unknown block ::{b.Name}");
                        break;
                }
            }
            // Both languages ask the same things in the same order.
            Assert.Equal(lesson.Page("en").OfType<DirectiveBlock>().Select(b => b.Name + b.Arg("note") + b.Arg("music")),
                blocks.Select(b => b.Name + b.Arg("note") + b.Arg("music")));
        }
    }

    [Fact]
    public void Path_points_at_existing_lessons_and_songs_and_uses_every_lesson()
    {
        var songs = Pipit.Core.Music.SongLibrary.Load(n => File.ReadAllText(Repo.Data("songs", n)));
        var lessons = LessonIds().Select(o => (string)o[0]).ToHashSet();
        foreach (var step in Path.Steps)
            Assert.True(step.Kind == StepKind.Lesson ? lessons.Contains(step.Id) : songs.Get(step.Id) is not null, step.Key);
        Assert.Equal(lessons.OrderBy(x => x), Path.Steps.Where(s => s.Kind == StepKind.Lesson).Select(s => s.Id).OrderBy(x => x));
    }

    [Fact]
    public void Path_songs_only_use_notes_already_met_in_lessons()
    {
        // A note is "met" once a lesson has a fingering or try block for it.
        var songs = Pipit.Core.Music.SongLibrary.Load(n => File.ReadAllText(Repo.Data("songs", n)));
        var met = new HashSet<int>();
        foreach (var step in Path.Steps)
        {
            if (step.Kind == StepKind.Lesson)
            {
                var lesson = LessonParser.Load(step.Id, f => Read(step.Id, f));
                foreach (var b in lesson.Page("en").OfType<DirectiveBlock>().Where(b => b.Name is "fingering" or "try"))
                    met.Add(Pipit.Core.Music.Pitch.Parse(b.Arg("note")).Midi);
            }
            else
                Assert.All(songs.Get(step.Id)!.Melody.Performance, n =>
                    Assert.True(met.Contains(n.Pitch.Midi), $"{step.Id} uses {n.Pitch} before a lesson teaches it"));
        }
    }
}
