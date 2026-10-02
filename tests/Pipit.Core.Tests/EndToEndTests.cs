using Pipit.Core.Analysis;
using Pipit.Core.Instruments;
using Pipit.Core.Music;
using Pipit.Core.Practice;
using Pipit.Core.Scoring;
using Pipit.Core.Synthesis;
using Xunit;
using Xunit.Abstractions;

namespace Pipit.Core.Tests;

/// <summary>
/// The whole rhythm pipeline without a player: each library song is synthesised as a perfect
/// player would sound it (80 ms late through the "microphone"), then tracked, segmented and
/// graded exactly as the play screen does. Anything below near-perfect is a pipeline bug.
/// The songs are spread over <see cref="Shards"/> test classes (below), which xUnit runs in
/// parallel.
/// </summary>
public abstract class EndToEndTests(ITestOutputHelper output)
{
    public const int Shards = 6;

    private const int Rate = 48000;
    private const double Latency = 0.08;

    public static IEnumerable<object[]> SongIds(int shard) =>
        SongLibrary.Load(name => File.ReadAllText(Repo.Data("songs", name))).Songs
            .Where((_, i) => i % Shards == shard).Select(s => new object[] { s.Id });

    /// <summary>Casual mode is slow to simulate (pauses between notes); shorter songs cover it.</summary>
    public static IEnumerable<object[]> ShorterSongIds(int shard) =>
        SongLibrary.Load(name => File.ReadAllText(Repo.Data("songs", name))).Songs.Where(s => s.Melody.Performance.Count <= 120)
            .Where((_, i) => i % Shards == shard).Select(s => new object[] { s.Id });

    private static RecorderInfo RecorderFor(Song song) => RecorderInfo.Of(Enum.Parse<RecorderKind>(song.Melody.Instrument, ignoreCase: true));

    private static Song Load(string id) => SongLibrary.Load(name => File.ReadAllText(Repo.Data("songs", name))).Get(id)!;

    protected void PerfectPerformance(string id)
    {
        var song = Load(id);
        var soprano = RecorderFor(song);
        var spb = song.SecondsPerBeat();
        // Long pieces are checked in excerpts, so the suite stays quick: the start, the end and
        // two places between, played one after the other.
        var expected = new List<ExpectedNote>();
        var t = 1.0; // the first note starts at 1 s
        foreach (var excerpt in Excerpts(song.Melody.Performance, spb))
        {
            var shift = t - excerpt[0].StartBeat * spb;
            expected.AddRange(excerpt.Select(n => new ExpectedNote(n.Index, soprano.WrittenToSounding(n.Pitch.Midi),
                shift + n.StartBeat * spb, shift + n.EndBeat * spb)));
            t = expected[^1].End + 1.0;
        }
        var audio = RecorderVoice.Render(expected.Select(e => new SynthNote(e.Start + Latency, e.End - e.Start, Pitch.FrequencyOfMidi(e.SoundingMidi))).ToList(), Rate);

        var frames = PitchTracker.Analyse(audio, Rate, PitchTrackerOptions.ForRange(soprano.BottomSoundingMidi, soprano.TopSoundingMidi));
        var run = Grader.Grade(expected, NoteSegmenter.Segment(frames), Tolerances.Normal, Latency);

        output.WriteLine($"{id}: {run.Accuracy:0.0}% hits {run.Hits}/{run.Notes.Count} extras {run.Extras} tendency {run.TimingTendencyMs:0} ms");
        foreach (var bad in run.Notes.Where(n => n.Result != NoteResult.Hit || n.Timing is not (Timing.Perfect or Timing.Great)))
            output.WriteLine($"  note {bad.Expected.Index} at {bad.Expected.Start:0.00}s: {bad.Result} {bad.Timing} {bad.OnsetErrorMs:0} ms");
        Assert.Equal(run.Notes.Count, run.Hits);
        Assert.Equal(0, run.Extras);
        Assert.True(run.Accuracy >= 95, $"{id}: {run.Accuracy:0.0}%");
        Assert.Equal(3, run.Stars);
    }

    private const double ExcerptSeconds = 20;

    /// <summary>The whole performance when it lasts up to four excerpts, else four excerpts of
    /// <see cref="ExcerptSeconds"/>: the start, the end and two places between.</summary>
    private static List<List<PerformedNote>> Excerpts(IReadOnlyList<PerformedNote> notes, double secondsPerBeat)
    {
        var first = notes[0].StartBeat;
        double At(PerformedNote n) => (n.StartBeat - first) * secondsPerBeat;
        var total = At(notes[^1]);
        if (total <= 4 * ExcerptSeconds)
            return new List<List<PerformedNote>> { notes.ToList() };
        return Enumerable.Range(0, 4)
            .Select(k => k * (total - ExcerptSeconds) / 3)
            .Select(from => notes.Where(n => At(n) >= from && At(n) < from + ExcerptSeconds).ToList())
            .Where(excerpt => excerpt.Count > 0)
            .ToList();
    }

    protected static void CasualPlayer(string id)
    {
        // Each note played after a random pause of up to two seconds.
        var song = Load(id);
        var soprano = RecorderFor(song);
        var rng = new Random(3);
        var t = 0.5;
        var notes = new List<SynthNote>();
        foreach (var n in song.Melody.Performance)
        {
            t += rng.NextDouble() * 2;
            notes.Add(new SynthNote(t, 0.4, Pitch.FrequencyOfMidi(soprano.WrittenToSounding(n.Pitch.Midi))));
            t += 0.4;
        }
        var mode = new CasualMode(song.Melody.Performance.Select(n => soprano.WrittenToSounding(n.Pitch.Midi)).ToList());
        foreach (var f in PitchTracker.Analyse(RecorderVoice.Render(notes, Rate), Rate))
            mode.Push(f);
        Assert.True(mode.Finished, $"{id}: stopped at note {mode.Index}");
        Assert.Equal(100, mode.Accuracy);
    }
}

public class EndToEndShard0(ITestOutputHelper output) : EndToEndTests(output)
{
    [Theory]
    [MemberData(nameof(SongIds), 0, MemberType = typeof(EndToEndTests))]
    public void A_perfect_performance_scores_near_perfect(string id) => PerfectPerformance(id);

    [Theory]
    [MemberData(nameof(ShorterSongIds), 0, MemberType = typeof(EndToEndTests))]
    public void Casual_mode_follows_a_player_who_takes_their_time(string id) => CasualPlayer(id);
}

public class EndToEndShard1(ITestOutputHelper output) : EndToEndTests(output)
{
    [Theory]
    [MemberData(nameof(SongIds), 1, MemberType = typeof(EndToEndTests))]
    public void A_perfect_performance_scores_near_perfect(string id) => PerfectPerformance(id);

    [Theory]
    [MemberData(nameof(ShorterSongIds), 1, MemberType = typeof(EndToEndTests))]
    public void Casual_mode_follows_a_player_who_takes_their_time(string id) => CasualPlayer(id);
}

public class EndToEndShard2(ITestOutputHelper output) : EndToEndTests(output)
{
    [Theory]
    [MemberData(nameof(SongIds), 2, MemberType = typeof(EndToEndTests))]
    public void A_perfect_performance_scores_near_perfect(string id) => PerfectPerformance(id);

    [Theory]
    [MemberData(nameof(ShorterSongIds), 2, MemberType = typeof(EndToEndTests))]
    public void Casual_mode_follows_a_player_who_takes_their_time(string id) => CasualPlayer(id);
}

public class EndToEndShard3(ITestOutputHelper output) : EndToEndTests(output)
{
    [Theory]
    [MemberData(nameof(SongIds), 3, MemberType = typeof(EndToEndTests))]
    public void A_perfect_performance_scores_near_perfect(string id) => PerfectPerformance(id);

    [Theory]
    [MemberData(nameof(ShorterSongIds), 3, MemberType = typeof(EndToEndTests))]
    public void Casual_mode_follows_a_player_who_takes_their_time(string id) => CasualPlayer(id);
}

public class EndToEndShard4(ITestOutputHelper output) : EndToEndTests(output)
{
    [Theory]
    [MemberData(nameof(SongIds), 4, MemberType = typeof(EndToEndTests))]
    public void A_perfect_performance_scores_near_perfect(string id) => PerfectPerformance(id);

    [Theory]
    [MemberData(nameof(ShorterSongIds), 4, MemberType = typeof(EndToEndTests))]
    public void Casual_mode_follows_a_player_who_takes_their_time(string id) => CasualPlayer(id);
}

public class EndToEndShard5(ITestOutputHelper output) : EndToEndTests(output)
{
    [Theory]
    [MemberData(nameof(SongIds), 5, MemberType = typeof(EndToEndTests))]
    public void A_perfect_performance_scores_near_perfect(string id) => PerfectPerformance(id);

    [Theory]
    [MemberData(nameof(ShorterSongIds), 5, MemberType = typeof(EndToEndTests))]
    public void Casual_mode_follows_a_player_who_takes_their_time(string id) => CasualPlayer(id);
}
