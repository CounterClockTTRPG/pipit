using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pipit.Core.Instruments;

/// <summary>
/// Fingerings for one system, indexed by semitones above the instrument's bottom note.
/// The same chart serves soprano, alto and tenor because the patterns are relative.
/// </summary>
public sealed class FingeringChart
{
    private readonly IReadOnlyList<Fingering>[] _steps;

    public FingeringSystem System { get; }

    private FingeringChart(FingeringSystem system, IReadOnlyList<Fingering>[] steps)
    {
        System = system;
        _steps = steps;
    }

    /// <summary>Primary fingering first, then alternatives.</summary>
    public IReadOnlyList<Fingering> At(int step) =>
        step is >= 0 and <= RecorderInfo.RangeSemitones
            ? _steps[step]
            : throw new ArgumentOutOfRangeException(nameof(step), step, "Outside the recorder's range");

    public Fingering Primary(int step) => At(step)[0];

    /// <summary>The step whose primary fingering matches, or null.</summary>
    public int? StepOf(Fingering fingering)
    {
        for (var i = 0; i < _steps.Length; i++)
            if (_steps[i].Contains(fingering))
                return i;
        return null;
    }

    /// <summary>Loads a system from a folder on disk (tests, tools).</summary>
    public static FingeringChart Load(string dataDir, FingeringSystem system) =>
        Load(name => File.ReadAllText(Path.Combine(dataDir, name)), system);

    /// <summary>
    /// Loads a system through <paramref name="readFile"/> (file name → JSON text), so the Godot
    /// side can read from res:// inside an exported pack. German is stored as overrides of baroque.
    /// </summary>
    public static FingeringChart Load(Func<string, string> readFile, FingeringSystem system)
    {
        var file = Read(readFile(system.ToString().ToLowerInvariant() + ".json"));
        if (file.Base is null)
            return Build(system, file.Steps ?? throw new InvalidDataException($"{system}: no steps"));

        var baseFile = Read(readFile(file.Base + ".json"));
        var steps = (baseFile.Steps ?? throw new InvalidDataException($"{file.Base}: no steps"))
            .ToDictionary(s => s.Semitone);
        foreach (var o in file.Overrides ?? [])
            steps[o.Semitone] = o;
        return Build(system, steps.Values.ToList());
    }

    private static FingeringChart Build(FingeringSystem system, List<StepDto> dtos)
    {
        var steps = new IReadOnlyList<Fingering>[RecorderInfo.RangeSemitones + 1];
        foreach (var dto in dtos)
        {
            if (dto.Semitone is < 0 or > RecorderInfo.RangeSemitones)
                throw new InvalidDataException($"{system}: step {dto.Semitone} out of range");
            if (dto.Fingerings is not { Count: > 0 })
                throw new InvalidDataException($"{system}: step {dto.Semitone} has no fingerings");
            steps[dto.Semitone] = dto.Fingerings.Select(Fingering.Parse).ToList();
        }
        for (var i = 0; i < steps.Length; i++)
            if (steps[i] is null)
                throw new InvalidDataException($"{system}: step {i} is missing");
        return new FingeringChart(system, steps);
    }

    private static FileDto Read(string json) =>
        JsonSerializer.Deserialize<FileDto>(json, JsonOptions)
        ?? throw new InvalidDataException("Empty fingering file");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class FileDto
    {
        public string? Base { get; set; }
        public List<StepDto>? Steps { get; set; }
        public List<StepDto>? Overrides { get; set; }
    }

    private sealed class StepDto
    {
        public int Semitone { get; set; }
        [JsonPropertyName("fingerings")] public List<string>? Fingerings { get; set; }
    }
}
