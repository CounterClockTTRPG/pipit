namespace Pipit.Core.Music;

/// <summary>
/// A 1-5 difficulty estimate for the melody of a song on a recorder in C, from what makes a piece
/// hard for a learner: speed (notes per second at the written tempo), range, sharps and flats
/// (cross fingerings), high notes (pinched thumb) and big leaps. A song can override it with "level".
/// </summary>
public static class Difficulty
{
    public sealed record Breakdown(double NotesPerSecond, int Range, double Accidentals, double High, double Leaps, double Score, int Level);

    public static Breakdown Estimate(Song song)
    {
        var notes = song.Melody.Performance;
        if (notes.Count == 0)
            return new Breakdown(0, 0, 0, 0, 0, 0, 1);
        var seconds = (notes[^1].EndBeat - notes[0].StartBeat) * song.SecondsPerBeat();
        var nps = notes.Count / Math.Max(1, seconds);
        var range = notes.Max(n => n.Pitch.Midi) - notes.Min(n => n.Pitch.Midi);
        var accidentals = notes.Count(n => n.Pitch.Alter != 0) / (double)notes.Count;
        // Written E5 and above on a C recorder needs the pinched thumb.
        var high = notes.Count(n => n.Pitch.Midi >= 76) / (double)notes.Count;
        var leaps = notes.Count < 2 ? 0 : notes.Zip(notes.Skip(1), (a, b) => Math.Abs(a.Pitch.Midi - b.Pitch.Midi) >= 7 ? 1 : 0).Average();
        var score = 0.6 * nps + range / 8.0 + 4 * accidentals + 4 * high + 3 * leaps;
        var level = score < 2.6 ? 1 : score < 3.6 ? 2 : score < 4.8 ? 3 : score < 6.2 ? 4 : 5;
        return new Breakdown(nps, range, accidentals, high, leaps, score, song.Level ?? level);
    }
}
