namespace Pipit.Core.Instruments;

/// <summary>
/// One physical recorder. Calibration fills in the offsets; until M4 moves instruments into
/// user profiles, the app keeps a single active one in its settings.
/// </summary>
public sealed class InstrumentProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "My soprano";
    public RecorderKind Kind { get; set; } = RecorderKind.Soprano;
    public FingeringSystem Fingering { get; set; } = FingeringSystem.Baroque;
    /// <summary>True when holes 6/7 are keys (common on tenors) rather than double holes.</summary>
    public bool Keyed { get; set; }
    public double ReferenceA { get; set; } = 440.0;
    /// <summary>Median deviation of all calibrated notes, applied when grading.</summary>
    public double MeasuredOffsetCents { get; set; }
    /// <summary>Per sounding MIDI note; informational unless the user opts in.</summary>
    public Dictionary<int, double> PerNoteOffsetCents { get; set; } = new();
    public double MinutesPlayed { get; set; }

    public RecorderInfo Info => RecorderInfo.Of(Kind);
}
