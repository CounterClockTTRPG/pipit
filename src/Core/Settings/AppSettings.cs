using System.Text.Json.Serialization;
using Pipit.Core.Instruments;
using Pipit.Core.Music;

namespace Pipit.Core.Settings;

/// <summary>
/// Per-machine settings. Holds the list of recorders until M4 moves them into user profiles.
/// Microphone calibration (gate, latency) is per input device; tuning is per recorder.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchema = 2;

    public int SchemaVersion { get; set; } = CurrentSchema;
    public string Language { get; set; } = "en";
    public NoteNaming Naming { get; set; } = NoteNaming.FixedDo;
    /// <summary>Godot input device name; "Default" follows the Windows default device.</summary>
    public string InputDevice { get; set; } = "Default";
    public Dictionary<string, double> LatencyMsByDevice { get; set; } = new();
    public Dictionary<string, double> GateRmsByDevice { get; set; } = new();
    public List<InstrumentProfile> Instruments { get; set; } = new();
    public string ActiveInstrumentId { get; set; } = "";

    /// <summary>Schema 1 had a single recorder; read only to migrate it.</summary>
    [JsonPropertyName("instrument")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InstrumentProfile? LegacyInstrument { get; set; }

    [JsonIgnore]
    public InstrumentProfile ActiveInstrument => Instruments.First(i => i.Id == ActiveInstrumentId);

    /// <summary>
    /// Brings a loaded file up to date: migrates the schema-1 recorder, guarantees at least one
    /// recorder and a valid active one. Safe to call repeatedly.
    /// </summary>
    public AppSettings Normalize()
    {
        if (LegacyInstrument is { } legacy)
        {
            if (Instruments.All(i => i.Id != legacy.Id))
                Instruments.Insert(0, legacy);
            if (string.IsNullOrEmpty(ActiveInstrumentId))
                ActiveInstrumentId = legacy.Id;
            LegacyInstrument = null;
        }
        if (Instruments.Count == 0)
            Instruments.Add(new InstrumentProfile());
        if (Instruments.All(i => i.Id != ActiveInstrumentId))
            ActiveInstrumentId = Instruments[0].Id;
        SchemaVersion = CurrentSchema;
        return this;
    }

    /// <summary>Adds a recorder with a name that is not taken yet, and makes it active.</summary>
    public InstrumentProfile AddInstrument(RecorderKind kind, FingeringSystem fingering)
    {
        var baseName = $"My {kind.ToString().ToLowerInvariant()}";
        var name = baseName;
        for (var n = 2; Instruments.Any(i => i.Name == name); n++)
            name = $"{baseName} {n}";
        var instrument = new InstrumentProfile { Name = name, Kind = kind, Fingering = fingering };
        Instruments.Add(instrument);
        ActiveInstrumentId = instrument.Id;
        return instrument;
    }

    /// <summary>Removes a recorder; the last one cannot be removed.</summary>
    public bool RemoveInstrument(string id)
    {
        if (Instruments.Count <= 1)
            return false;
        var removed = Instruments.RemoveAll(i => i.Id == id) > 0;
        Normalize();
        return removed;
    }
}
