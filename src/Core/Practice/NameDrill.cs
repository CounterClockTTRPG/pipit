using Pipit.Core.Scoring;

namespace Pipit.Core.Practice;

/// <summary>A name given for a note: its letter (0 = C/Do ... 6 = B/Si) and when (seconds, capture clock).</summary>
public sealed record NameAnswer(int Letter, double Time);

public enum NameResult { Right, WrongName, Missed }

/// <summary>How one note of a reading drill was named.</summary>
public sealed record NameJudgement(int Index, int WrittenMidi, NameResult Result, double? ErrorMs, Timing? Timing)
{
    /// <summary>The right name, on the beat (Perfect, Great or Good).</summary>
    public bool RightOnTime => Result == NameResult.Right && Timing is Scoring.Timing.Perfect or Scoring.Timing.Great or Scoring.Timing.Good;
}

/// <summary>
/// The note-reading drill: notes appear on the staff one per beat, and the player names each
/// one (tapping or saying Do Re Mi / C D E) on its beat. Octaves do not matter for the name.
/// </summary>
public static class NameDrill
{
    private static readonly int[] NaturalPitchClasses = { 0, 2, 4, 5, 7, 9, 11 };

    /// <summary>The letter of a natural note (0 = C ... 6 = B), or -1 for a sharp or flat.</summary>
    public static int LetterOf(int midi) => Array.IndexOf(NaturalPitchClasses, ((midi % 12) + 12) % 12);

    public static bool IsNatural(int midi) => LetterOf(midi) >= 0;

    /// <summary>
    /// Matches answers to notes: each note takes the first unused answer within half a beat of
    /// its time; the name must match its letter. Timing windows: Perfect within 80 ms, Great
    /// 160 ms, Good 250 ms, beyond that Early or Late.
    /// </summary>
    public static List<NameJudgement> Grade(IReadOnlyList<(int WrittenMidi, double Time)> notes, IReadOnlyList<NameAnswer> answers, double secondsPerBeat)
    {
        var used = new bool[answers.Count];
        var result = new List<NameJudgement>();
        for (var i = 0; i < notes.Count; i++)
        {
            var (midi, time) = notes[i];
            var match = -1;
            for (var a = 0; a < answers.Count; a++)
                if (!used[a] && Math.Abs(answers[a].Time - time) <= secondsPerBeat / 2)
                {
                    match = a;
                    break;
                }
            if (match < 0)
            {
                result.Add(new NameJudgement(i, midi, NameResult.Missed, null, null));
                continue;
            }
            used[match] = true;
            var errorMs = (answers[match].Time - time) * 1000;
            var timing = Math.Abs(errorMs) switch
            {
                <= 80 => Timing.Perfect,
                <= 160 => Timing.Great,
                <= 250 => Timing.Good,
                _ => errorMs < 0 ? Timing.Early : Timing.Late,
            };
            var right = answers[match].Letter == LetterOf(midi);
            result.Add(new NameJudgement(i, midi, right ? NameResult.Right : NameResult.WrongName, errorMs, timing));
        }
        return result;
    }
}
