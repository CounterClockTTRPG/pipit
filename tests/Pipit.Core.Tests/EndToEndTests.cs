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
/// </summary>
public class EndToEndTests(ITestOutputHelper output)
{
    private const int Rate = 48000;
    private const double Latency = 0.08;

    public static IEnumerable<object[]> SongIds() =>
        SongLibrary.Load(name => File.ReadAllText(Repo.Data("songs", name))).Songs.Select(s => new object[] { s.Id });

    private static Song Load(string id) => SongLibrary.Load(name => File.ReadAllText(Repo.Data("songs", name))).Get(id)!;

    [Theory]
    [MemberData(nameof(SongIds))]
    public void A_perfect_performance_scores_near_perfect(string id)
    {
        var song = Load(id);
        var soprano = RecorderInfo.Of(RecorderKind.Soprano);
        var spb = song.SecondsPerBeat();
        var first = song.Melody.Performance.Min(n => n.StartBeat);
        var t0 = 1.0 - first * spb; // beat 0 in seconds; the first note starts at 1 s
        var expected = song.Melody.Performance.Select(n => new ExpectedNote(n.Index, soprano.WrittenToSounding(n.Pitch.Midi),
            t0 + n.StartBeat * spb, t0 + n.EndBeat * spb)).ToList();
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

    [Theory]
    [MemberData(nameof(SongIds))]
    public void Casual_mode_follows_a_player_who_takes_their_time(string id)
    {
        // Each note played after a random pause of up to two seconds.
        var song = Load(id);
        var soprano = RecorderInfo.Of(RecorderKind.Soprano);
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
