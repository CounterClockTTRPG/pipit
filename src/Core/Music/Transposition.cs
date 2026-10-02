using Pipit.Core.Instruments;
using Pipit.Core.Notation;

namespace Pipit.Core.Music;

/// <summary>
/// Moves a song up or down so it fits a recorder: an octave if that is enough (the key stays),
/// otherwise the smallest shift into a key with at most three sharps or flats. Notes are
/// respelled for the new key, so the score stays readable.
/// </summary>
public static class Transposition
{
    private static readonly (string Name, int Fifths, int Tonic)[] Majors =
    {
        ("C", 0, 0), ("G", 1, 7), ("D", 2, 2), ("A", 3, 9), ("E", 4, 4), ("B", 5, 11), ("F#", 6, 6),
        ("F", -1, 5), ("Bb", -2, 10), ("Eb", -3, 3), ("Ab", -4, 8), ("Db", -5, 1),
    };

    /// <summary>The song itself when it fits, else the best-fitting transposition (and the shift), or null.</summary>
    public static (Song Song, int Semitones)? FitFor(Song song, RecorderInfo recorder)
    {
        if (Fits(song, recorder, 0))
            return (song, 0);
        var candidates = new[] { 12, -12 }.Concat(Enumerable.Range(1, 11).SelectMany(k => new[] { -k, k }));
        foreach (var k in candidates)
        {
            if (!Fits(song, recorder, k) || Math.Abs(KeyAfter(song.Key, k).Fifths) > 3)
                continue;
            return (Transpose(song, k), k);
        }
        return null;
    }

    private static bool Fits(Song song, RecorderInfo recorder, int k) =>
        song.Melody.Performance.All(n => recorder.InRange(n.Pitch.Midi + k));

    /// <summary>The key a shift leads to: "Bb" down 3 is "G"; minor keys stay minor.</summary>
    public static (string Name, int Fifths) KeyAfter(string key, int semitones)
    {
        var minor = key.EndsWith('m');
        var fifths = KeySignature.Parse(key).Fifths;
        // Minor keys share their signature with the relative major, a minor third up.
        var major = Majors.First(m => m.Fifths == (minor ? fifths : fifths));
        var tonic = ((major.Tonic + semitones) % 12 + 12) % 12;
        var target = Majors.Where(m => m.Tonic == tonic).OrderBy(m => Math.Abs(m.Fifths)).First();
        if (!minor)
            return (target.Name, target.Fifths);
        var minorTonic = ((tonic + 9) % 12 + 12) % 12; // relative minor of the new major
        var name = Majors.Where(m => m.Tonic == minorTonic).OrderBy(m => Math.Abs(m.Fifths - target.Fifths)).First().Name;
        return (name + "m", target.Fifths);
    }

    public static Song Transpose(Song song, int semitones)
    {
        if (semitones == 0)
            return song;
        var (keyName, fifths) = KeyAfter(song.Key, semitones);
        var key = new KeySignature(fifths);
        Pitch Shift(Pitch p) => SpellIn(p.Midi + semitones, key);
        SongPart Move(SongPart part) => part with
        {
            Bars = part.Bars.Select(b => b with { Notes = b.Notes.Select(n => n.Pitch is { } p ? n with { Pitch = Shift(p) } : n).ToList() }).ToList(),
            Performance = part.Performance.Select(n => n with { Pitch = Shift(n.Pitch) }).ToList(),
        };
        return new Song
        {
            Id = song.Id, Titles = song.Titles, Composer = song.Composer, Chapter = song.Chapter, Level = song.Level, Source = song.Source,
            Tempo = song.Tempo, Time = song.Time, Key = keyName, Pickup = song.Pickup, About = song.About, Tip = song.Tip,
            Parts = song.Parts.Select(Move).ToList(), Accompaniment = song.Accompaniment.Select(Move).ToList(),
        };
    }

    /// <summary>Spells a MIDI note in a key: the key's own notes as the key has them, others with the key's kind of accidental.</summary>
    public static Pitch SpellIn(int midi, KeySignature key)
    {
        var pc = ((midi % 12) + 12) % 12;
        int[] naturals = { 0, 2, 4, 5, 7, 9, 11 };
        for (var s = 0; s < 7; s++)
        {
            var step = (Step)s;
            var alter = key.AlterOf(step);
            if (((naturals[s] + alter) % 12 + 12) % 12 == pc)
                return new Pitch(step, alter, (midi - naturals[s] - alter) / 12 - 1);
        }
        // Not in the key: a sharp in sharp keys (and C), a flat in flat keys.
        var up = key.Fifths >= 0;
        for (var s = 0; s < 7; s++)
        {
            var alter = up ? 1 : -1;
            if (((naturals[s] + alter) % 12 + 12) % 12 == pc)
                return new Pitch((Step)s, alter, (midi - naturals[s] - alter) / 12 - 1);
        }
        throw new InvalidOperationException("unreachable");
    }
}
