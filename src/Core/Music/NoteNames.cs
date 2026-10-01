namespace Pipit.Core.Music;

public enum NoteNaming { FixedDo, Letters }

/// <summary>Display names for pitches. Fixed Do: Do is always C.</summary>
public static class NoteNames
{
    private static readonly string[] FixedDo = { "Do", "Re", "Mi", "Fa", "Sol", "La", "Si" };

    /// <summary>
    /// The name without octave, e.g. "Fa♯". French spells Re as "Ré"; Dutch and English do not.
    /// </summary>
    public static string Name(Pitch pitch, NoteNaming naming = NoteNaming.FixedDo, string language = "en")
    {
        var name = naming == NoteNaming.Letters
            ? pitch.Step.ToString()
            : FixedDo[(int)pitch.Step];
        if (naming == NoteNaming.FixedDo && pitch.Step == Step.D && language == "fr")
            name = "Ré";
        return name + pitch.Alter switch { 1 => "♯", -1 => "♭", 0 => "", var a => throw new ArgumentOutOfRangeException(nameof(pitch), a, "Only single accidentals are supported") };
    }
}
