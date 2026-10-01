using Pipit.Core.Music;

namespace Pipit.Core.Notation;

/// <summary>A key signature as a count of fifths: +1 is one sharp (G major / E minor), -2 two flats.</summary>
public readonly record struct KeySignature(int Fifths)
{
    private static readonly Step[] SharpOrder = { Step.F, Step.C, Step.G, Step.D, Step.A, Step.E, Step.B };
    private static readonly Step[] FlatOrder = { Step.B, Step.E, Step.A, Step.D, Step.G, Step.C, Step.F };
    private static readonly Dictionary<string, int> Major = new()
    {
        ["C"] = 0, ["G"] = 1, ["D"] = 2, ["A"] = 3, ["E"] = 4, ["B"] = 5, ["F#"] = 6,
        ["F"] = -1, ["Bb"] = -2, ["Eb"] = -3, ["Ab"] = -4, ["Db"] = -5, ["Gb"] = -6,
    };

    /// <summary>"G", "Bb", "F#m", "Gm" (a trailing m means minor: three fifths flatter than the major).</summary>
    public static KeySignature Parse(string key)
    {
        var minor = key.EndsWith('m');
        var tonic = minor ? key[..^1] : key;
        if (!Major.TryGetValue(tonic, out var fifths))
            throw new FormatException($"Unknown key '{key}'");
        return new KeySignature(minor ? fifths - 3 : fifths);
    }

    /// <summary>The steps carrying an accidental, in the order they are written.</summary>
    public IReadOnlyList<Step> Steps => Fifths >= 0 ? SharpOrder[..Fifths] : FlatOrder[..(-Fifths)];

    /// <summary>The alteration the key gives a step: +1, -1 or 0.</summary>
    public int AlterOf(Step step) => Steps.Contains(step) ? Math.Sign(Fifths) : 0;
}
