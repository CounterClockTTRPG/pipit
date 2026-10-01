namespace Pipit.Core.Instruments;

public enum HoleState { Open, Closed, Half, Pinched }

/// <summary>
/// One fingering: the thumb hole, front holes 1-7 (index 0 = hole 1) and whether the bell is
/// covered. Holes 6 and 7 are double holes on most sopranos and altos; Half means one of the
/// pair is covered. Pinched applies to the thumb only.
/// </summary>
public sealed record Fingering(HoleState Thumb, IReadOnlyList<HoleState> Holes, bool BellCovered)
{
    /// <summary>Parses the 8-character code used in data/fingerings (see the legend there).</summary>
    public static Fingering Parse(string code)
    {
        if (code.Length is not (8 or 9) || (code.Length == 9 && code[8] != 'b'))
            throw new FormatException($"Fingering code must be 8 characters plus an optional 'b': '{code}'");
        var thumb = code[0] switch
        {
            'x' => HoleState.Closed,
            'o' => HoleState.Open,
            'p' => HoleState.Pinched,
            var c => throw new FormatException($"Bad thumb state '{c}' in '{code}'"),
        };
        var holes = new HoleState[7];
        for (var i = 0; i < 7; i++)
        {
            var c = code[i + 1];
            holes[i] = c switch
            {
                'x' => HoleState.Closed,
                'o' => HoleState.Open,
                'h' when i >= 5 => HoleState.Half,
                _ => throw new FormatException($"Bad state '{c}' for hole {i + 1} in '{code}'"),
            };
        }
        return new Fingering(thumb, holes, code.Length == 9);
    }

    public string Code
    {
        get
        {
            var chars = new char[BellCovered ? 9 : 8];
            chars[0] = Char(Thumb);
            for (var i = 0; i < 7; i++)
                chars[i + 1] = Char(Holes[i]);
            if (BellCovered)
                chars[8] = 'b';
            return new string(chars);
        }
    }

    private static char Char(HoleState s) => s switch
    {
        HoleState.Closed => 'x',
        HoleState.Open => 'o',
        HoleState.Half => 'h',
        HoleState.Pinched => 'p',
        _ => '?',
    };

    // Records compare lists by reference; fingerings are equal when their codes are.
    public bool Equals(Fingering? other) => other is not null && Code == other.Code;
    public override int GetHashCode() => Code.GetHashCode();
    public override string ToString() => Code;
}
