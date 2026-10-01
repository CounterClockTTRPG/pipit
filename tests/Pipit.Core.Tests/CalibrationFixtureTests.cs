using System.Globalization;
using Pipit.Core.Analysis;
using Pipit.Core.Instruments;
using Pipit.Core.Recording;
using Xunit;

namespace Pipit.Core.Tests;

/// <summary>
/// A real latency calibration: soprano Sol played on 8 clicks into a laptop microphone array
/// (audio enhancements off), saved by the app's diagnostics. The app reported "0 notes heard"
/// for this take because the screen cleared its frames before computing; the data was fine.
/// </summary>
public class CalibrationFixtureTests
{
    private static string Dir => Path.Combine(Repo.Root, "tests", "fixtures", "calibration");

    [Fact]
    public void Real_latency_take_finds_every_note()
    {
        var (samples, rate) = WavFile.Read(Path.Combine(Dir, "latency-soprano-builtin-mic.wav"));
        var meta = File.ReadAllLines(Path.Combine(Dir, "latency-soprano-builtin-mic.txt"))
            .Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var gate = double.Parse(meta["gate"].Replace(',', '.'), CultureInfo.InvariantCulture);
        var clicks = meta["clicks"].Split(',').Select(c => double.Parse(c, CultureInfo.InvariantCulture)).ToList();
        var info = RecorderInfo.Of(RecorderKind.Soprano);

        var frames = PitchTracker.Analyse(samples, rate, PitchTrackerOptions.ForRange(info.BottomSoundingMidi, info.TopSoundingMidi), gate);
        var result = Calibration.Latency(clicks, Calibration.NoteOnsets(frames));

        Assert.NotNull(result);
        Assert.Equal(8, result!.Used);
        Assert.InRange(result.LatencyMs, 60, 140);
        // The note played was Sol (G5 sounding on a soprano).
        Assert.Equal(79, (int)Math.Round(Stats.Median(frames.Where(f => f.Voiced).Select(f => f.Midi))));
    }
}
