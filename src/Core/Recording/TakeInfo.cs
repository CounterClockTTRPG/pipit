using System.Text.Json;
using System.Text.Json.Serialization;
using Pipit.Core.Instruments;
using Pipit.Core.Settings;

namespace Pipit.Core.Recording;

/// <summary>
/// The JSON sidecar next to each recorded WAV. For a guided take it carries the ground truth:
/// the schedule the player was asked to follow, written before the take is analysed. Note
/// times are seconds from the start of the WAV and already include the count-in.
/// </summary>
public sealed class TakeInfo
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public DateTime RecordedAt { get; set; } = DateTime.Now;
    public int SampleRate { get; set; }
    public double DurationSeconds { get; set; }
    public string InputDevice { get; set; } = "";
    public string? Session { get; set; }
    public double? Tempo { get; set; }
    /// <summary>Calibrated latency at recording time; notes should be heard this much after their scheduled time.</summary>
    public double? LatencyMs { get; set; }
    public RecorderKind Instrument { get; set; }
    public FingeringSystem Fingering { get; set; }
    public double ReferenceA { get; set; } = 440;
    /// <summary>The recorder's calibrated overall offset at recording time; the verifier allows for it.</summary>
    public double TuningOffsetCents { get; set; }
    public double? GateRms { get; set; }
    public List<TakeNote> Notes { get; set; } = new();
    public bool Starred { get; set; }

    public static TakeInfo Load(string path) =>
        JsonSerializer.Deserialize<TakeInfo>(File.ReadAllText(path), JsonStore.Options)
        ?? throw new InvalidDataException($"Empty take info {path}");

    public void Save(string path) => JsonStore.Save(path, this);
}

public sealed record TakeNote(
    [property: JsonPropertyName("step")] int Step,
    [property: JsonPropertyName("writtenMidi")] int WrittenMidi,
    [property: JsonPropertyName("soundingMidi")] int SoundingMidi,
    [property: JsonPropertyName("start")] double Start,
    [property: JsonPropertyName("end")] double End);
