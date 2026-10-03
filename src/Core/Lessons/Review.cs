using Pipit.Core.Music;
using Pipit.Core.Notation;

namespace Pipit.Core.Lessons;

/// <summary>What the study path has taught so far: notes in the order they first appear, and note lengths.</summary>
public sealed record ReviewMaterial(IReadOnlyList<int> Notes, IReadOnlySet<double> Beats);

/// <summary>A generated review exercise: music in the song format and its key.</summary>
public sealed record ReviewExercise(string Music, string Key);

/// <summary>
/// Review steps: short exercises written on the spot from what the path has taught so far,
/// with the player's weak notes coming up more often.
/// </summary>
public static class Review
{
    /// <summary>
    /// Notes (written MIDI) and note lengths met in the path's lessons and songs before
    /// <paramref name="upTo"/> (all of the path when null), notes in the order they first appear.
    /// </summary>
    public static ReviewMaterial Taught(StudyPath path, PathStep? upTo, Func<string, Lesson?> lesson, Func<string, Song?> song)
    {
        var notes = new List<int>();
        var beats = new HashSet<double>();
        void Add(int midi)
        {
            if (!notes.Contains(midi))
                notes.Add(midi);
        }
        void AddBars(IEnumerable<Bar> bars)
        {
            foreach (var n in bars.SelectMany(b => b.Notes).Where(n => !n.IsRest && !n.Triplet))
            {
                Add(n.Pitch!.Value.Midi);
                beats.Add(n.Beats);
            }
        }
        foreach (var step in path.Steps)
        {
            if (step == upTo)
                break;
            if (step.Kind == StepKind.Lesson && lesson(step.Id) is { } l)
            {
                foreach (var d in l.Page("en").OfType<DirectiveBlock>())
                {
                    if (d.Name is "try" or "fingering" or "compare" && d.Arg("note") is { Length: > 0 } note)
                        Add(Pitch.Parse(note).Midi);
                    else if (d.Name is "staff" or "play" or "exercise" && d.Arg("music") is { Length: > 0 } music)
                        AddBars(SongParser.ParseMusic(music, TimeSignature.Parse(d.Arg("time", "4/4")), 0, l.Id, validate: false).Bars);
                }
            }
            else if (step.Kind == StepKind.Song && song(step.Id) is { } s)
                AddBars(s.Melody.Bars);
        }
        return new ReviewMaterial(notes, beats);
    }

    /// <summary>
    /// Writes <paramref name="bars"/> bars of 4/4 from the material: mostly steps and small leaps,
    /// weak notes (<paramref name="weakness"/>, 0-1) more often, ending on the note it began with.
    /// Only note lengths the path has taught are used. The same seed gives the same exercise.
    /// </summary>
    public static ReviewExercise Write(ReviewMaterial material, Func<int, double> weakness, int seed, int bars = 4)
    {
        var rng = new Random(seed);
        var notes = material.Notes.Distinct().OrderBy(n => n).ToList();
        if (notes.Count == 0)
            throw new ArgumentException("Nothing taught yet", nameof(material));
        var has = material.Beats;
        // Rhythm cells (beats) with how often to use them.
        var cells = new List<(double[] Cell, double Weight)> { (new[] { 1.0 }, 4) };
        if (has.Contains(2))
            cells.Add((new[] { 2.0 }, 2));
        if (has.Contains(0.5))
            cells.Add((new[] { 0.5, 0.5 }, 2));
        if (has.Contains(1.5) && has.Contains(0.5))
            cells.Add((new[] { 1.5, 0.5 }, 1));

        var rhythm = new List<List<double>>();
        for (var b = 0; b < bars - 1; b++)
        {
            // Every cell lasts a whole number of beats, and a quarter always fits.
            var bar = new List<double>();
            while (bar.Sum() < 4)
                bar.AddRange(Pick(rng, cells.Where(c => c.Cell.Sum() <= 4 - bar.Sum()).ToList(), c => c.Weight).Cell);
            rhythm.Add(bar);
        }
        rhythm.Add(has.Contains(2) ? new List<double> { 1, 1, 2 } : new List<double> { 1, 1, 1, 1 });

        // The tune: a walk over the taught notes, mostly by step, drawn to weak notes.
        var index = notes.Count / 2;
        var start = notes[index];
        var pitches = new List<int> { start };
        var total = rhythm.Sum(b => b.Count);
        for (var i = 1; i < total - 1; i++)
        {
            var options = Enumerable.Range(-2, 5).Select(step => index + step).Where(j => j >= 0 && j < notes.Count).ToList();
            index = Pick(rng, options, j =>
            {
                var distance = Math.Abs(j - index);
                var shape = distance switch { 0 => 0.6, 1 => 3.0, _ => 1.2 };
                // Not three of the same in a row.
                if (distance == 0 && pitches.Count >= 2 && pitches[^1] == pitches[^2])
                    shape = 0;
                return shape * (1 + 3 * weakness(notes[j]));
            });
            pitches.Add(notes[index]);
        }
        pitches.Add(start);

        var key = KeyFor(notes);
        var signature = KeySignature.Parse(key);
        var tokens = new List<string>();
        var k = 0;
        foreach (var bar in rhythm)
        {
            tokens.Add(string.Join(" ", bar.Select(beats => $"{Transposition.SpellIn(pitches[k++], signature)}/{Name(beats)}")));
        }
        return new ReviewExercise(string.Join(" | ", tokens), key);
    }

    /// <summary>A key whose signature fits the notes: one sharp for Fa♯ without Fa, one flat for Si♭ without Si.</summary>
    private static string KeyFor(IReadOnlyCollection<int> notes)
    {
        bool Has(int pitchClass) => notes.Any(n => n % 12 == pitchClass);
        if (Has(6) && !Has(5))
            return "G";
        if (Has(10) && !Has(11))
            return "F";
        return "C";
    }

    private static string Name(double beats) => beats switch
    {
        4 => "w", 2 => "h", 1.5 => "q.", 1 => "q", 0.5 => "e",
        _ => throw new ArgumentOutOfRangeException(nameof(beats)),
    };

    private static T Pick<T>(Random rng, IReadOnlyList<T> items, Func<T, double> weight)
    {
        var weights = items.Select(weight).ToList();
        var pick = rng.NextDouble() * weights.Sum();
        for (var i = 0; i < items.Count; i++)
        {
            pick -= weights[i];
            if (pick <= 0)
                return items[i];
        }
        return items[^1];
    }
}
