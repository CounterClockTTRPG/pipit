using System.Globalization;

namespace Pipit.Core.Music;

/// <summary>
/// A time signature. Beats are counted in quarter notes throughout Pipit, so 6/8 is a bar of
/// three beats; tempo is quarter notes per minute.
/// </summary>
public readonly record struct TimeSignature(int Upper, int Lower)
{
    public double BarBeats => Upper * 4.0 / Lower;

    public static TimeSignature Parse(string text)
    {
        var parts = text.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var u) || !int.TryParse(parts[1], out var l) || u <= 0 || l is not (1 or 2 or 4 or 8 or 16))
            throw new FormatException($"Not a time signature: '{text}'");
        return new TimeSignature(u, l);
    }

    public override string ToString() => $"{Upper}/{Lower}";
}

/// <summary>
/// One written symbol: a note or a rest, as it appears in a bar. <see cref="Id"/> is its position
/// in the part's music (unique), linking it to the performed notes it produces.
/// </summary>
public sealed record WrittenNote(int Id, Pitch? Pitch, double Beats, bool TieToNext, bool BreathAfter)
{
    public bool IsRest => Pitch is null;
}

/// <summary>A written bar with its repeat and ending marks, as notated (repeats not expanded).</summary>
public sealed record Bar(int Number, IReadOnlyList<WrittenNote> Notes)
{
    public double Beats => Notes.Sum(n => n.Beats);
    public bool StartRepeat { get; init; }
    public bool EndRepeat { get; init; }
    public bool DoubleBarAfter { get; init; }
    /// <summary>1 or 2 for bars under a first or second ending, else 0.</summary>
    public int Ending { get; init; }
}

/// <summary>
/// A note as performed: repeats written out, ties merged, rests left out. StartBeat counts from
/// the first beat of the first full bar, so a pickup starts at a negative beat.
/// </summary>
public sealed record PerformedNote(int Index, Pitch Pitch, double StartBeat, double Beats, int Bar)
{
    /// <summary>The written notes this sounds (more than one when tied).</summary>
    public IReadOnlyList<int> WrittenIds { get; init; } = Array.Empty<int>();

    public double EndBeat => StartBeat + Beats;
}

/// <summary>
/// A part of a song. Playable parts have a recorder <see cref="Instrument"/>; accompaniment
/// parts are only ever played by the app, with a General MIDI <see cref="Sound"/>.
/// </summary>
public sealed record SongPart(string Id, string Name, string Instrument, IReadOnlyList<Bar> Bars, IReadOnlyList<PerformedNote> Performance)
{
    /// <summary>The General MIDI sound the app uses when it plays this part.</summary>
    public string Sound { get; init; } = "recorder";
    /// <summary>Written pitch + this = sounding pitch when the app plays the part (12 for a soprano part).</summary>
    public int SoundingOffset { get; init; }
}

public sealed class Song
{
    public required string Id { get; init; }
    public required IReadOnlyDictionary<string, string> Titles { get; init; }
    public string Composer { get; init; } = "";
    /// <summary>Learning-path chapter; 0 for repertoire (outside the path, sorted by difficulty).</summary>
    public int Chapter { get; init; }
    /// <summary>Manual difficulty (1-5) overriding the estimate, or null.</summary>
    public int? Level { get; init; }
    /// <summary>Where the notes come from (edition, licence), for the credits.</summary>
    public string Source { get; init; } = "";
    public int Difficulty => Music.Difficulty.Estimate(this).Level;
    public double Tempo { get; init; } = 90;
    public TimeSignature Time { get; init; } = new(4, 4);
    public string Key { get; init; } = "C";
    /// <summary>Length of the pickup (anacrusis) in beats; 0 when the song starts on a full bar.</summary>
    public double Pickup { get; init; }
    public required IReadOnlyList<SongPart> Parts { get; init; }
    /// <summary>A short introduction (history, a fun fact), per language.</summary>
    public IReadOnlyDictionary<string, string> About { get; init; } = new Dictionary<string, string>();
    /// <summary>A playing tip for this song, per language.</summary>
    public IReadOnlyDictionary<string, string> Tip { get; init; } = new Dictionary<string, string>();

    /// <summary>The part the player plays: the first one (see <see cref="WithPartFirst"/>).</summary>
    public SongPart Melody => Parts[0];

    /// <summary>Parts only the app plays (bass lines, drones, consort voices).</summary>
    public IReadOnlyList<SongPart> Accompaniment { get; init; } = Array.Empty<SongPart>();

    public bool HasOtherParts => Parts.Count > 1 || Accompaniment.Count > 0;

    /// <summary>The same song with another playable part as the player's.</summary>
    public Song WithPartFirst(int index)
    {
        var parts = Parts.ToList();
        var chosen = parts[index];
        parts.RemoveAt(index);
        parts.Insert(0, chosen);
        return new Song
        {
            Id = Id, Titles = Titles, Composer = Composer, Chapter = Chapter, Level = Level, Source = Source, Tempo = Tempo,
            Time = Time, Key = Key, Pickup = Pickup, Parts = parts, Accompaniment = Accompaniment, About = About, Tip = Tip,
        };
    }

    public string Title(string language) =>
        Titles.TryGetValue(language, out var t) ? t : Titles.TryGetValue("en", out var en) ? en : Titles.Values.First();

    public string AboutText(string language) => Localised(About, language);
    public string TipText(string language) => Localised(Tip, language);

    private static string Localised(IReadOnlyDictionary<string, string> texts, string language) =>
        texts.TryGetValue(language, out var t) ? t : texts.TryGetValue("en", out var en) ? en : "";

    public double SecondsPerBeat(double tempoFactor = 1) => 60.0 / (Tempo * tempoFactor);

    /// <summary>Lowest and highest written MIDI note of the melody.</summary>
    public (int Low, int High) Range =>
        (Melody.Performance.Min(n => n.Pitch.Midi), Melody.Performance.Max(n => n.Pitch.Midi));

    public double TotalBeats => Melody.Performance.Count == 0 ? 0 : Melody.Performance.Max(n => n.EndBeat);

    /// <summary>The end of the last note of any part, so accompaniment can outlast the player's part.</summary>
    public double EndBeat => Parts.Concat(Accompaniment).SelectMany(p => p.Performance).Select(n => n.EndBeat).DefaultIfEmpty(0).Max();

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Id} ({Parts.Count} part(s), {Melody.Performance.Count} notes)");
}
