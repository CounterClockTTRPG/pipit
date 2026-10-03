using Pipit.Core.Music;

namespace Pipit.Core.Notation;

public enum Accidental { None, Sharp, Flat, Natural }

/// <summary>How a duration is written: the notehead/flag type, and its dots.</summary>
public enum NoteValue { Whole, Half, Quarter, Eighth, Sixteenth, ThirtySecond }

/// <summary>
/// One note or rest placed on the staff. Units are staff spaces; <see cref="Position"/> counts
/// half spaces up from the bottom line (E4 = 0, G4 = 2, middle line B4 = 4, F5 = 8).
/// </summary>
public sealed class PlacedNote
{
    public required WrittenNote Note { get; init; }
    public double X { get; set; }
    public int Position { get; init; }
    public NoteValue Value { get; init; }
    public int Dots { get; init; }
    public Accidental Accidental { get; init; }
    public bool StemUp { get; set; }
    /// <summary>Notes in the same beam group share an id; null for unbeamed notes.</summary>
    public int? BeamGroup { get; set; }
    /// <summary>Stem tip height (in positions, like <see cref="Position"/>) when beamed; set by the layout.</summary>
    public double StemEnd { get; set; }
    /// <summary>Beat within the bar where the note starts.</summary>
    public double BeatInBar { get; init; }
    public bool IsRest => Note.Pitch is null;
    public int Beams => Value switch { NoteValue.Eighth => 1, NoteValue.Sixteenth => 2, NoteValue.ThirtySecond => 3, _ => 0 };
}

public sealed class PlacedBar
{
    public required Bar Bar { get; init; }
    public double X { get; set; }
    public double Width { get; set; }
    public required List<PlacedNote> Notes { get; init; }
    /// <summary>Triplet groups, as indexes of their first and last note in <see cref="Notes"/>.</summary>
    public List<(int First, int Last)> Triplets { get; init; } = new();
}

public sealed class StaffSystem
{
    public required List<PlacedBar> Bars { get; init; }
    public bool ShowTime { get; init; }
    /// <summary>Where the first bar starts, after clef, key and (on the first line) time signature.</summary>
    public double ContentStart { get; init; }
    public double Width { get; set; }
}

public sealed record LayoutOptions
{
    public double LineWidth { get; init; } = 60;   // staff spaces
    public double ClefWidth { get; init; } = 3.6;
    public double KeyAccidentalWidth { get; init; } = 1.1;
    public double TimeWidth { get; init; } = 2.6;
    public double BarPadding { get; init; } = 1.2;
    /// <summary>Space per note: a minimum plus a share that grows with the square root of its length.</summary>
    public double MinNoteSpace { get; init; } = 2.0;
    public double NoteSpacePerSqrtBeat { get; init; } = 2.2;
    public double AccidentalSpace { get; init; } = 1.2;
    public double StemLength { get; init; } = 3.5;
}

/// <summary>
/// Engraves one part: staff positions, the accidentals actually printed (key signature and earlier
/// accidentals in the bar taken into account), stems, beams per beat, and line breaks. Pure
/// geometry in staff spaces; the app scales and draws it with a SMuFL font.
/// </summary>
public static class StaffLayout
{
    public static List<StaffSystem> Layout(IReadOnlyList<Bar> bars, TimeSignature time, KeySignature key, LayoutOptions? options = null)
    {
        var o = options ?? new LayoutOptions();
        // Each bar needs the note before it, so a tie across the bar line carries its accidental.
        var placed = new List<PlacedBar>();
        WrittenNote? last = null;
        foreach (var b in bars)
        {
            placed.Add(PlaceBar(b, time, key, o, last));
            last = b.Notes.Count > 0 ? b.Notes[^1] : last;
        }

        // Break into lines: as many bars as fit, then stretch each line to the full width.
        var systems = new List<StaffSystem>();
        var i = 0;
        while (i < placed.Count)
        {
            var first = systems.Count == 0;
            var start = o.ClefWidth + Math.Abs(key.Fifths) * o.KeyAccidentalWidth + (first ? o.TimeWidth : 0) + 0.8;
            var line = new List<PlacedBar>();
            var used = start;
            while (i < placed.Count && (line.Count == 0 || used + placed[i].Width <= o.LineWidth))
            {
                used += placed[i].Width;
                line.Add(placed[i++]);
            }
            // The last line is not stretched more than 30 % so a lone bar does not spread out.
            var natural = used - start;
            var available = o.LineWidth - start;
            var stretch = i < placed.Count ? available / natural : Math.Min(available / natural, 1.3);
            var x = start;
            foreach (var bar in line)
            {
                var scale = stretch;
                foreach (var n in bar.Notes)
                    n.X = o.BarPadding + (n.X - o.BarPadding) * scale;
                bar.Width *= scale;
                bar.X = x;
                x += bar.Width;
            }
            systems.Add(new StaffSystem { Bars = line, ShowTime = first, ContentStart = start, Width = x });
        }
        return systems;
    }

    public static int PositionOf(Pitch pitch) => pitch.Octave * 7 + (int)pitch.Step - (4 * 7 + (int)Step.E);

    public static (NoteValue Value, int Dots) ValueOf(double beats) => beats switch
    {
        4 => (NoteValue.Whole, 0), 3 => (NoteValue.Half, 1), 2 => (NoteValue.Half, 0),
        1.5 => (NoteValue.Quarter, 1), 1 => (NoteValue.Quarter, 0), 0.75 => (NoteValue.Eighth, 1),
        0.5 => (NoteValue.Eighth, 0), 0.375 => (NoteValue.Sixteenth, 1), 0.25 => (NoteValue.Sixteenth, 0),
        0.1875 => (NoteValue.ThirtySecond, 1), 0.125 => (NoteValue.ThirtySecond, 0),
        6 => (NoteValue.Whole, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(beats), beats, "Duration cannot be written as one note"),
    };

    /// <summary>The written value of a note: a triplet is written as the value it is two thirds of.</summary>
    public static (NoteValue Value, int Dots) ValueOf(WrittenNote note) =>
        ValueOf(note.Triplet ? Math.Round(note.Beats * 1.5 * 64) / 64 : note.Beats);

    private static PlacedBar PlaceBar(Bar bar, TimeSignature time, KeySignature key, LayoutOptions o, WrittenNote? previous)
    {
        // Accidentals: the key signature, overridden by earlier accidentals on the same line or
        // space within this bar. Ties carry the alteration across the bar line without a new sign.
        var state = new Dictionary<int, int>();
        var notes = new List<PlacedNote>();
        var x = o.BarPadding + (bar.StartRepeat ? 1.6 : 0);
        var beat = 0.0;
        foreach (var n in bar.Notes)
        {
            var (value, dots) = ValueOf(n);
            var accidental = Accidental.None;
            var position = 4; // rests sit on the middle line
            if (n.Pitch is { } pitch)
            {
                position = PositionOf(pitch);
                var current = state.TryGetValue(position, out var a) ? a : key.AlterOf(pitch.Step);
                var tiedIn = previous is { TieToNext: true } && previous.Pitch?.Midi == pitch.Midi;
                if (pitch.Alter != current && !tiedIn)
                    accidental = pitch.Alter switch { 1 => Accidental.Sharp, -1 => Accidental.Flat, _ => Accidental.Natural };
                state[position] = pitch.Alter;
                if (accidental != Accidental.None)
                    x += o.AccidentalSpace;
            }
            notes.Add(new PlacedNote
            {
                Note = n, X = x, Position = position, Value = value, Dots = dots, Accidental = accidental,
                StemUp = position < 4, BeatInBar = beat,
            });
            x += o.MinNoteSpace + o.NoteSpacePerSqrtBeat * Math.Sqrt(n.Beats) + (dots > 0 ? 0.5 : 0);
            beat += n.Beats;
            previous = n;
        }
        Beam(notes, time, o);
        return new PlacedBar { Bar = bar, Notes = notes, Width = x + o.BarPadding * 0.5, Triplets = TripletGroups(notes) };
    }

    /// <summary>
    /// Consecutive triplet notes in groups that fill twice their first note's written value
    /// (three triplet eighths make one beat).
    /// </summary>
    private static List<(int First, int Last)> TripletGroups(List<PlacedNote> notes)
    {
        var groups = new List<(int, int)>();
        int? start = null;
        double sum = 0, target = 0;
        for (var i = 0; i < notes.Count; i++)
        {
            var n = notes[i].Note;
            if (!n.Triplet)
            {
                start = null;
                continue;
            }
            if (start is null)
            {
                start = i;
                sum = 0;
                target = n.Beats * 1.5 * 2;
            }
            sum += n.Beats;
            if (sum >= target - 1e-6)
            {
                groups.Add((start.Value, i));
                start = null;
            }
        }
        return groups;
    }

    /// <summary>
    /// Groups eighths, sixteenths and thirty-seconds within one beat (see below for the beat unit), gives each
    /// group a common stem direction (away from the note furthest from the middle line) and a
    /// straight beam that clears every notehead by a full stem length at its shortest.
    /// </summary>
    private static void Beam(List<PlacedNote> notes, TimeSignature time, LayoutOptions o)
    {
        // Beam per beat: a dotted quarter in compound time (6/8), a half note in 2/2 and 3/2.
        var compound = time.Lower == 8 && time.Upper % 3 == 0;
        var unit = compound ? 1.5 : time.Lower == 2 ? 2.0 : 1.0;
        var group = new List<PlacedNote>();
        var id = 0;
        void Flush()
        {
            if (group.Count >= 2)
            {
                var furthest = group.MaxBy(n => Math.Abs(n.Position - 4))!;
                var up = furthest.Position < 4;
                var groupId = id++;
                // Beam slope: half the interval between first and last note, at most one space.
                var slope = Math.Clamp((group[^1].Position - group[0].Position) / 2.0, -2, 2) / Math.Max(1, group[^1].X - group[0].X);
                // Place the beam so the note nearest to it still gets a full stem.
                var offset = group.Select(n => n.Position + (up ? 1 : -1) * o.StemLength * 2 - slope * (n.X - group[0].X))
                    .Aggregate(up ? double.MinValue : double.MaxValue, (acc, v) => up ? Math.Max(acc, v) : Math.Min(acc, v));
                foreach (var n in group)
                {
                    n.StemUp = up;
                    n.BeamGroup = groupId;
                    n.StemEnd = offset + slope * (n.X - group[0].X);
                }
            }
            else
                foreach (var n in group)
                    n.BeamGroup = null;
            group.Clear();
        }
        foreach (var n in notes)
        {
            if (n.IsRest || n.Beams == 0)
            {
                Flush();
                continue;
            }
            if (group.Count > 0 && Math.Floor(n.BeatInBar / unit + 1e-9) != Math.Floor(group[0].BeatInBar / unit + 1e-9))
                Flush();
            group.Add(n);
        }
        Flush();
        foreach (var n in notes.Where(n => n.BeamGroup is null && !n.IsRest))
            n.StemEnd = n.Position + (n.StemUp ? 1 : -1) * o.StemLength * 2;
    }
}
