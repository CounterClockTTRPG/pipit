using Pipit.Core.Analysis;
using Pipit.Core.Instruments;
using Pipit.Core.Music;
using Pipit.Core.Recording;
using Xunit;
using Xunit.Abstractions;

namespace Pipit.Core.Tests;

/// <summary>
/// Real recordings made with the Studio's guided sessions (tests/fixtures/audio/*.wav + .json).
/// Every expected note must be recognised. A synthetic take exercises the same path so the
/// suite means something before the first real recording exists.
/// </summary>
public class FixtureTests(ITestOutputHelper output)
{
    public static string FixtureDir => Path.Combine(Repo.Root, "tests", "fixtures", "audio");

    public static IEnumerable<object[]> Fixtures()
    {
        var files = Directory.Exists(FixtureDir)
            ? Directory.GetFiles(FixtureDir, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList()
            : new List<string?>();
        // xUnit fails a theory with no data, so an empty folder yields one placeholder case.
        return files.Count == 0 ? new[] { new object[] { "" } } : files.Select(f => new object[] { f! });
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Recorded_take_is_recognised(string name)
    {
        if (name == "")
        {
            output.WriteLine("No recorded fixtures yet (record guided sessions in the Studio, then scripts/import-fixtures.sh).");
            return;
        }
        var take = TakeInfo.Load(Path.Combine(FixtureDir, name + ".json"));
        var (samples, rate) = WavFile.Read(Path.Combine(FixtureDir, name + ".wav"));
        AssertRecognised(take, samples, rate);
    }

    [Fact]
    public void Synthetic_take_is_recognised()
    {
        const int rate = 48000;
        var session = GuidedSessions.Get("long-tones-low");
        var info = RecorderInfo.Of(RecorderKind.Soprano);
        var countIn = GuidedSession.CountInBeats * session.SecondsPerBeat;
        var latency = 0.12;
        var schedule = session.Schedule(info);
        var samples = Signals.Silence(countIn + session.TotalBeats * session.SecondsPerBeat + 1, rate, noise: 0.002);
        foreach (var n in schedule)
            Signals.MixInto(samples, Signals.Tone(Pitch.FrequencyOfMidi(n.SoundingMidi + 0.1), n.End - n.Start - 0.15, rate),
                (int)((countIn + n.Start + latency) * rate));

        var take = new TakeInfo
        {
            SampleRate = rate, Session = session.Id, LatencyMs = latency * 1000, Instrument = RecorderKind.Soprano,
            Notes = schedule.Select(n => new TakeNote(n.Step, n.WrittenMidi, n.SoundingMidi, countIn + n.Start, countIn + n.End)).ToList(),
        };
        AssertRecognised(take, samples, rate);
    }

    private void AssertRecognised(TakeInfo take, float[] samples, int rate)
    {
        var info = RecorderInfo.Of(take.Instrument);
        var options = PitchTrackerOptions.ForRange(info.BottomSoundingMidi, info.TopSoundingMidi, take.ReferenceA);
        var frames = PitchTracker.Analyse(samples, rate, options, take.GateRms);
        var checks = TakeVerifier.Verify(take, frames);
        foreach (var c in checks)
            output.WriteLine($"{c.Expected.SoundingMidi,3} {(c.Recognised ? "ok " : "BAD")} median {c.MedianMidi:F2} ({c.CentsOff:+0;-0} c) voiced {c.VoicedFraction:P0}");
        Assert.All(checks, c => Assert.True(c.Recognised, $"note {c.Expected.SoundingMidi} at {c.Expected.Start:F2}s heard as {c.MedianMidi:F2}"));
    }
}
