namespace Pipit.Core.Scoring;

/// <summary>
/// The verdict on a lesson exercise. Exercises teach reading notes and playing on the beat, so
/// that alone decides: three quarters of the notes right and on time (Perfect, Great or Good)
/// pass. Tuning and note length are reported as tips (when a third of the notes or more are
/// off), not counted against the player as in a graded song.
/// </summary>
public sealed record ExerciseVerdict(bool Passed, int OnTime, int Total, int TuningTip, bool LengthTip)
{
    public static ExerciseVerdict Of(GradedRun run)
    {
        var onTime = run.Notes.Count(n => n.Result == NoteResult.Hit && n.Timing is Timing.Perfect or Timing.Great or Timing.Good);
        var hits = run.Notes.Where(n => n.Result == NoteResult.Hit).ToList();
        var offTune = hits.Where(n => !n.InTune).ToList();
        var tuning = hits.Count > 0 && offTune.Count * 3 >= hits.Count ? Math.Sign(offTune.Average(n => n.CentsOff)) : 0;
        var length = hits.Count > 0 && hits.Count(n => !n.HeldLongEnough) * 3 >= hits.Count;
        return new ExerciseVerdict(onTime * 4 >= run.Notes.Count * 3, onTime, run.Notes.Count, tuning, length);
    }
}
