using Pipit.Core.Game;
using Pipit.Core.Lessons;
using Pipit.Core.Music;
using Pipit.Core.Practice;
using Xunit;

namespace Pipit.Core.Tests;

public class ReviewTests
{
    private static StudyPath Path => StudyPath.Parse(File.ReadAllText(Repo.Data("path.json")));
    private static SongLibrary Library => SongLibrary.Load(name => File.ReadAllText(Repo.Data("songs", name)));

    private static Lesson? LoadLesson(string id) =>
        LessonParser.Load(id, f => File.Exists(Repo.Data("lessons", id, f)) ? File.ReadAllText(Repo.Data("lessons", id, f)) : null);

    private static ReviewMaterial TaughtBefore(int chapter)
    {
        var path = Path;
        var library = Library;
        var upTo = path.Chapters.First(c => c.Number == chapter).Steps.First(s => s.Kind == StepKind.Review);
        return Review.Taught(path, upTo, LoadLesson, library.Get);
    }

    [Fact]
    public void Chapter_one_teaches_si_la_sol_first()
    {
        var m = TaughtBefore(1);
        Assert.Equal(new[] { 71, 69, 67 }, m.Notes.Take(3)); // Si, La, Sol
        Assert.Contains(1.0, m.Beats);
        Assert.Contains(0.5, m.Beats);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(9)]
    public void Exercises_use_only_what_was_taught_and_parse(int chapter)
    {
        var m = TaughtBefore(chapter);
        for (var seed = 0; seed < 20; seed++)
        {
            var exercise = Review.Write(m, _ => 0, seed);
            var (bars, played) = SongParser.ParseMusic(exercise.Music, TimeSignature.Parse("4/4"), 0, $"seed {seed}");
            Assert.Equal(4, bars.Count);
            Assert.All(played, n => Assert.Contains(n.Pitch.Midi, m.Notes));
            Assert.All(bars.SelectMany(b => b.Notes), n => Assert.Contains(n.Beats, m.Beats));
            Assert.Equal(played[0].Pitch.Midi, played[^1].Pitch.Midi); // ends where it began
        }
    }

    [Fact]
    public void Weak_notes_come_up_more_often()
    {
        var m = TaughtBefore(4);
        int Count(Func<int, double> weakness) => Enumerable.Range(0, 40)
            .Sum(seed => SongParser.ParseMusic(Review.Write(m, weakness, seed).Music, TimeSignature.Parse("4/4"), 0, "t").Performance.Count(n => n.Pitch.Midi == 64));
        Assert.True(Count(n => n == 64 ? 1 : 0) > Count(_ => 0) * 1.5, "Mi should come up clearly more often when it is weak");
    }

    [Fact]
    public void A_note_moves_up_the_boxes_and_back()
    {
        var stat = new NoteStat();
        var now = new DateTime(2026, 10, 3);
        for (var i = 0; i < 7; i++)
            stat.Record(true, 1.0, now);
        Assert.Equal(NoteStat.TopBox, stat.Box);
        stat.Record(false, 0, now);
        Assert.Equal(1, stat.Box);
        Assert.Equal(8, stat.Played);
        Assert.InRange(stat.Weakness, 0.15, 0.25);
    }

    [Fact]
    public void The_deck_brings_in_a_new_note_once_the_others_are_ready()
    {
        var stats = new Dictionary<int, NoteStat>();
        var deck = new LearningDeck(new[] { 71, 69, 67, 74 }, 0, n => stats.GetValueOrDefault(n), new Random(1));
        Assert.Equal(new[] { 71, 69, 67 }, deck.Active);
        Assert.Null(deck.IntroduceIfReady());
        int? previous = null;
        for (var i = 0; i < 30 && deck.IntroduceIfReady() is null; i++)
        {
            var note = deck.Next(previous);
            Assert.NotEqual(previous, note);
            if (!stats.TryGetValue(note, out var s))
                stats[note] = s = new NoteStat();
            s.Record(true, 1, DateTime.Now);
            previous = note;
        }
        Assert.Equal(4, deck.Introduced);
        Assert.Contains(74, deck.Active);
    }

    [Fact]
    public void Weak_notes_are_asked_more_often()
    {
        var stats = new Dictionary<int, NoteStat> { [71] = new() { Box = 5 }, [69] = new() { Box = 5 }, [67] = new() { Box = 0 } };
        var deck = new LearningDeck(new[] { 71, 69, 67 }, 3, n => stats.GetValueOrDefault(n), new Random(2));
        var asked = Enumerable.Range(0, 300).Select(_ => deck.Next(null)).ToList();
        Assert.True(asked.Count(n => n == 67) > asked.Count(n => n == 71) * 5);
    }
}
