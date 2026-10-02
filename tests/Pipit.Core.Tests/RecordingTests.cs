using Pipit.Core.Analysis;
using Pipit.Core.Instruments;
using Pipit.Core.Music;
using Pipit.Core.Recording;
using Pipit.Core.Settings;
using Xunit;

namespace Pipit.Core.Tests;

public class RecordingTests
{
    [Fact]
    public void Wav_round_trip()
    {
        var samples = Signals.Tone(440, 0.1, 48000);
        using var stream = new MemoryStream();
        WavFile.Write(stream, samples, 48000);
        stream.Position = 0;
        var (read, rate) = WavFile.Read(stream);
        Assert.Equal(48000, rate);
        Assert.Equal(samples.Length, read.Length);
        for (var i = 0; i < samples.Length; i++)
            Assert.Equal(samples[i], read[i], 1e-4f);
    }

    [Fact]
    public void Wav_clips_instead_of_wrapping()
    {
        Assert.Equal(short.MaxValue, WavFile.ToPcm16(1.5f));
        Assert.Equal(-short.MaxValue, WavFile.ToPcm16(-1.5f));
    }

    [Fact]
    public void Sessions_stay_in_range_for_every_recorder()
    {
        foreach (var kind in Enum.GetValues<RecorderKind>())
        {
            var info = RecorderInfo.Of(kind);
            foreach (var session in GuidedSessions.All)
                Assert.All(session.Schedule(info), n => Assert.True(info.InRange(n.WrittenMidi), $"{session.Id} {n.WrittenMidi} on {kind}"));
        }
    }

    [Fact]
    public void Same_steps_give_different_names_on_alto()
    {
        var tune = GuidedSessions.Get("tune");
        Assert.Equal(Pitch.Parse("G4").Midi, tune.Schedule(RecorderInfo.Of(RecorderKind.Soprano))[0].WrittenMidi);
        Assert.Equal(Pitch.Parse("C5").Midi, tune.Schedule(RecorderInfo.Of(RecorderKind.Alto))[0].WrittenMidi);
        Assert.Equal(16, tune.TotalBeats);
    }

    [Fact]
    public void Take_info_round_trips_through_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pipit-take-{Guid.NewGuid():N}.json");
        try
        {
            var info = new TakeInfo { Name = "x", SampleRate = 48000, Session = "tune", Instrument = RecorderKind.Alto,
                Notes = { new TakeNote(7, 72, 72, 1.0, 2.0) } };
            info.Save(path);
            var back = TakeInfo.Load(path);
            Assert.Equal(RecorderKind.Alto, back.Instrument);
            Assert.Equal(info.Notes, back.Notes);
            Assert.Contains("\"alto\"", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Corrupt_settings_are_kept_aside()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pipit-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not json");
            var settings = JsonStore.LoadOrDefault<Pipit.Core.Profiles.MachineSettings>(path);
            Assert.Equal("Default", settings.InputDevice);
            Assert.True(File.Exists(path + ".corrupt"));
        }
        finally { File.Delete(path); File.Delete(path + ".corrupt"); }
    }
}

public class ConductorTests
{
    // Long tones: Do (2 beats), rest (1), Re (2), rest (1), ...
    private static readonly GuidedSession LongTones = GuidedSessions.Get("long-tones-low");

    [Fact]
    public void During_a_note_the_cue_is_that_note()
    {
        var cue = LongTones.CueAt(0.5);
        Assert.Equal(new Cue(0, true, 2, false), cue);
    }

    [Fact]
    public void During_a_rest_the_cue_is_the_next_note_not_the_previous_one()
    {
        // Beat 2.5 is the rest after Do: the fingers should already be on Re.
        var cue = LongTones.CueAt(2.5);
        Assert.Equal(2, cue.Step);
        Assert.False(cue.Playing);
        Assert.Equal(4, cue.Following);
    }

    [Fact]
    public void Count_in_prepares_the_first_note()
    {
        var cue = LongTones.CueAt(-2);
        Assert.Equal(0, cue.Step);
        Assert.False(cue.Playing);
    }

    [Fact]
    public void After_the_last_note_the_session_is_finished()
    {
        var cue = LongTones.CueAt(LongTones.TotalBeats + 0.1);
        Assert.True(cue.Finished);
        Assert.Null(cue.Step);
    }

    [Fact]
    public void Spans_carry_note_lengths()
    {
        var spans = LongTones.Spans();
        Assert.Equal(8, spans.Count);
        Assert.Equal(new NoteSpan(2, 3, 2), spans[1]);
    }

    [Fact]
    public void Verifier_allows_for_the_recorders_tuning()
    {
        // Do played 60 cents sharp on a recorder calibrated at +20: still Do, not Do#.
        var take = new TakeInfo { TuningOffsetCents = 20, Notes = { new TakeNote(0, 60, 72, 0, 1) } };
        var frames = Enumerable.Range(0, 100)
            .Select(i => new Pipit.Core.Analysis.PitchFrame(i * 0.01, 0, 1, 0.1, true, 72.6, 72.6)).ToList();
        var check = Assert.Single(TakeVerifier.Verify(take, frames));
        Assert.True(check.Recognised);
        Assert.Equal(40, check.CentsOff, 6);
        Assert.False(Assert.Single(TakeVerifier.Verify(new TakeInfo { Notes = take.Notes }, frames)).Recognised);
    }
}
