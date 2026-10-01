using Pipit.Core.Instruments;
using Pipit.Core.Music;

namespace Pipit.Core.Recording;

/// <summary>
/// One scheduled event: a note (Step = semitones above the instrument's bottom note) or a rest
/// (Step = null), lasting Beats. Steps make a session fit any recorder size: step 7 is Sol on a
/// soprano or tenor and Do on an alto, with the same fingering.
/// </summary>
public sealed record SessionEvent(int? Step, double Beats);

/// <summary>A guided recording: the app conducts, the player follows, the schedule is the ground truth.</summary>
public sealed record GuidedSession(string Id, string Title, string Instructions, double Tempo, IReadOnlyList<SessionEvent> Events)
{
    public const int CountInBeats = 4;

    public double SecondsPerBeat => 60.0 / Tempo;
    public double TotalBeats => Events.Sum(e => e.Beats);

    public IReadOnlyList<NoteSpan> Spans()
    {
        var spans = new List<NoteSpan>();
        var beat = 0.0;
        foreach (var e in Events)
        {
            if (e.Step is { } step)
                spans.Add(new NoteSpan(step, beat, e.Beats));
            beat += e.Beats;
        }
        return spans;
    }

    /// <summary>The cue at a beat (negative during the count-in).</summary>
    public Cue CueAt(double beat)
    {
        var spans = Spans();
        var current = spans.FirstOrDefault(n => beat >= n.StartBeat && beat < n.StartBeat + n.Beats);
        if (current is not null)
        {
            var after = spans.FirstOrDefault(n => n.StartBeat > current.StartBeat);
            return new Cue(current.Step, true, after?.Step, false);
        }
        var upcoming = spans.FirstOrDefault(n => n.StartBeat > beat);
        if (upcoming is null)
            return new Cue(null, false, null, beat >= 0);
        var following = spans.FirstOrDefault(n => n.StartBeat > upcoming.StartBeat);
        return new Cue(upcoming.Step, false, following?.Step, false);
    }

    /// <summary>The schedule in seconds from the first beat after the count-in.</summary>
    public IReadOnlyList<ScheduledNote> Schedule(RecorderInfo recorder, double referenceA = Pitch.DefaultReferenceA)
    {
        var notes = new List<ScheduledNote>();
        var beat = 0.0;
        foreach (var e in Events)
        {
            if (e.Step is { } step)
            {
                var written = recorder.BottomWrittenMidi + step;
                notes.Add(new ScheduledNote(step, written, recorder.WrittenToSounding(written), beat * SecondsPerBeat, (beat + e.Beats) * SecondsPerBeat));
            }
            beat += e.Beats;
        }
        return notes;
    }
}

public sealed record ScheduledNote(int Step, int WrittenMidi, int SoundingMidi, double Start, double End);

/// <summary>
/// What the conductor shows at a moment. <see cref="Step"/> is the note to have under your
/// fingers: the note being played, or during a rest or the count-in the one that comes next,
/// so the player always prepares the right fingering. <see cref="Playing"/> says whether it
/// should sound now; <see cref="Following"/> is the note after it, for a small "next" hint.
/// </summary>
public sealed record Cue(int? Step, bool Playing, int? Following, bool Finished);

/// <summary>A note in beats from the first beat after the count-in.</summary>
public sealed record NoteSpan(int Step, double StartBeat, double Beats);

public static class GuidedSessions
{
    // Major-scale steps above the bottom note, first and second octave.
    private static readonly int[] Scale = { 0, 2, 4, 5, 7, 9, 11, 12 };
    private static readonly int[] UpperScale = { 12, 14, 16, 17, 19, 21, 23, 24, 26 };

    public static readonly IReadOnlyList<GuidedSession> All = new[]
    {
        new GuidedSession("silence", "Silence",
            "Stay quiet for a few seconds so Pipit can hear what your room sounds like.",
            60, new[] { new SessionEvent(null, 5) }),

        LongTones("long-tones-low", "Long notes, low octave", Scale),
        LongTones("long-tones-high", "Long notes, high octave",  UpperScale),

        new GuidedSession("chromatic", "Every note (chromatic)",
            "Play every note from the lowest to the highest, following the fingering shown. Skip any you cannot play yet.",
            60, Enumerable.Range(0, RecorderInfo.RangeSemitones + 1)
                .SelectMany(s => new[] { new SessionEvent(s, 1), new SessionEvent(null, 1) }).ToList()),

        new GuidedSession("tongued-repeats", "Repeated notes, tongued",
            "Play each note on every beat, tonguing 'du' every time. Do not slur.",
            80, Repeat(7, 8, 0.5).Concat(new[] { new SessionEvent(null, 2) }).Concat(Repeat(9, 8, 0.5))
                .Concat(new[] { new SessionEvent(null, 2) }).Concat(Repeat(4, 8, 0.5)).ToList()),

        new GuidedSession("slurs", "Slurred scale",
            "Tongue only the first note, then move your fingers without stopping the air.",
            72, new[] { 0, 2, 4, 5, 7, 5, 4, 2, 0 }.Select(s => new SessionEvent(s, 1)).ToList()),

        new GuidedSession("leaps", "Leaps",
            "Jump between low and high notes. Tongue each one.",
            60, new[] { 0, 7, 0, 12, 7, 12, 4, 11, 2, 14 }
                .SelectMany(s => new[] { new SessionEvent(s, 1), new SessionEvent(null, 0.5) }).ToList()),

        new GuidedSession("tune", "A tune: Au clair de la lune",
            "Play the tune as shown, at the tempo of the clicks.",
            92, Tune("7:1 7:1 7:1 9:1 11:2 9:2 7:1 11:1 9:1 9:1 7:4")),
    };

    public static GuidedSession Get(string id) => All.First(s => s.Id == id);

    private static GuidedSession LongTones(string id, string title, int[] steps) => new(
        id, title, "Start each note on \"Play!\" and keep blowing steadily until the orange bar ends (two beats). Breathe during the rests, while you put your fingers on the next note.",
        60, steps.SelectMany(s => new[] { new SessionEvent(s, 2), new SessionEvent(null, 1) }).ToList());

    private static IEnumerable<SessionEvent> Repeat(int step, int count, double beats) =>
        Enumerable.Repeat(new SessionEvent(step, beats), count);

    /// <summary>"step:beats" pairs; step R is a rest.</summary>
    private static List<SessionEvent> Tune(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(t => t.Split(':'))
        .Select(p => new SessionEvent(p[0] == "R" ? null : int.Parse(p[0]), double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)))
        .ToList();
}
