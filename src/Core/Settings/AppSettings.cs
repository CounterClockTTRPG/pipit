using Pipit.Core.Instruments;
using Pipit.Core.Music;

namespace Pipit.Core.Settings;

/// <summary>Per-machine settings. The instrument lives here until M4 moves it into user profiles.</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Language { get; set; } = "en";
    public NoteNaming Naming { get; set; } = NoteNaming.FixedDo;
    /// <summary>Godot input device name; "Default" follows the Windows default device.</summary>
    public string InputDevice { get; set; } = "Default";
    public Dictionary<string, double> LatencyMsByDevice { get; set; } = new();
    public Dictionary<string, double> GateRmsByDevice { get; set; } = new();
    public InstrumentProfile Instrument { get; set; } = new();
}
