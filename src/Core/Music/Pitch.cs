using System.Text.RegularExpressions;

namespace Pipit.Core.Music;

public enum Step { C, D, E, F, G, A, B }

/// <summary>
/// A spelled pitch: letter, accidental and octave (scientific pitch notation, C4 = middle C).
/// The spelling is kept, not just the MIDI number, so Fa♯ and Sol♭ print as written.
/// </summary>
public readonly partial record struct Pitch(Step Step, int Alter, int Octave)
{
    public const double DefaultReferenceA = 440.0;

    private static readonly int[] StepSemitones = { 0, 2, 4, 5, 7, 9, 11 };

    public int Midi => 12 * (Octave + 1) + StepSemitones[(int)Step] + Alter;

    public double Frequency(double referenceA = DefaultReferenceA) => FrequencyOfMidi(Midi, referenceA);

    public Pitch Transpose(int octaves) => this with { Octave = Octave + octaves };

    /// <summary>Parses "G4", "F#5", "Bb3". One sharp or flat at most.</summary>
    public static Pitch Parse(string text)
    {
        var m = PitchRegex().Match(text.Trim());
        if (!m.Success)
            throw new FormatException($"Not a pitch: '{text}'");
        var step = Enum.Parse<Step>(m.Groups[1].Value);
        var alter = m.Groups[2].Value switch { "#" => 1, "b" => -1, _ => 0 };
        return new Pitch(step, alter, int.Parse(m.Groups[3].Value));
    }

    public static double FrequencyOfMidi(double midi, double referenceA = DefaultReferenceA) =>
        referenceA * Math.Pow(2, (midi - 69) / 12.0);

    /// <summary>Fractional MIDI number of a frequency; 69.0 is the reference A.</summary>
    public static double MidiOfFrequency(double hz, double referenceA = DefaultReferenceA) =>
        69 + 12 * Math.Log2(hz / referenceA);

    public static double CentsBetween(double fromHz, double toHz) => 1200 * Math.Log2(toHz / fromHz);

    public override string ToString() =>
        $"{Step}{(Alter switch { 1 => "#", -1 => "b", _ => "" })}{Octave}";

    [GeneratedRegex(@"^([A-G])(#|b)?(-?\d)$")]
    private static partial Regex PitchRegex();
}
