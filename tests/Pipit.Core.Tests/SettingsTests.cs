using Pipit.Core.Instruments;
using Pipit.Core.Settings;
using Xunit;

namespace Pipit.Core.Tests;

public class SettingsTests
{
    private static AppSettings RoundTrip(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pipit-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            return JsonStore.LoadOrDefault<AppSettings>(path).Normalize();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Schema_1_single_recorder_is_migrated()
    {
        var s = RoundTrip("""
            { "schemaVersion": 1, "instrument": { "id": "abc", "name": "Canta", "kind": "soprano",
              "fingering": "german", "measuredOffsetCents": 19.5 }, "latencyMsByDevice": { "Default": 76 } }
            """);
        Assert.Equal(2, s.SchemaVersion);
        var only = Assert.Single(s.Instruments);
        Assert.Equal("Canta", only.Name);
        Assert.Equal(FingeringSystem.German, only.Fingering);
        Assert.Equal(19.5, only.MeasuredOffsetCents);
        Assert.Equal("abc", s.ActiveInstrumentId);
        Assert.Null(s.LegacyInstrument);
        Assert.Equal(76, s.LatencyMsByDevice["Default"]);
    }

    [Fact]
    public void Migrated_file_no_longer_writes_the_legacy_field()
    {
        var s = RoundTrip("""{ "instrument": { "id": "abc", "name": "Canta" } }""");
        var path = Path.Combine(Path.GetTempPath(), $"pipit-settings-{Guid.NewGuid():N}.json");
        try
        {
            JsonStore.Save(path, s);
            Assert.DoesNotContain("\"instrument\"", File.ReadAllText(path));
            Assert.Contains("\"instruments\"", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Fresh_settings_have_one_active_recorder()
    {
        var s = new AppSettings().Normalize();
        Assert.Single(s.Instruments);
        Assert.Same(s.Instruments[0], s.ActiveInstrument);
    }

    [Fact]
    public void Added_recorders_get_unique_names_and_become_active()
    {
        var s = new AppSettings().Normalize(); // "My soprano"
        var second = s.AddInstrument(RecorderKind.Soprano, FingeringSystem.Baroque);
        var alto = s.AddInstrument(RecorderKind.Alto, FingeringSystem.Baroque);
        Assert.Equal("My soprano 2", second.Name);
        Assert.Equal("My alto", alto.Name);
        Assert.Same(alto, s.ActiveInstrument);
    }

    [Fact]
    public void Removing_keeps_a_valid_active_recorder_and_never_the_last()
    {
        var s = new AppSettings().Normalize();
        var first = s.Instruments[0];
        var alto = s.AddInstrument(RecorderKind.Alto, FingeringSystem.Baroque);
        Assert.True(s.RemoveInstrument(alto.Id));
        Assert.Same(first, s.ActiveInstrument);
        Assert.False(s.RemoveInstrument(first.Id));
        Assert.Single(s.Instruments);
    }
}
