using Pipit.Core.Game;
using Pipit.Core.Profiles;
using Xunit;

namespace Pipit.Core.Tests;

public sealed class ProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pipit-profiles-" + Guid.NewGuid().ToString("N"));
    private ProfileStore Store => new(_root);

    public ProfileTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Players_are_created_saved_and_listed_in_order()
    {
        var a = Store.Create("Lena", "robin", "nl");
        var b = Store.Create("  Tom ", "bluetit", "en");
        var listed = Store.LoadProfiles();
        Assert.Equal(new[] { "Lena", "Tom" }, listed.Select(p => p.Name));
        Assert.Equal("nl", listed[0].Settings.Language);
        Assert.Equal("bluetit", listed[1].Avatar);
        Assert.Single(listed[0].Settings.Instruments); // a fresh player gets a default recorder
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Each_player_has_their_own_progress()
    {
        var a = Store.Create("Lena", "robin", "nl");
        var b = Store.Create("Tom", "pipit", "en");
        Store.SaveProgress(a.Id, new PlayerProgress { Xp = 500 });
        Assert.Equal(500, Store.LoadProgress(a.Id).Xp);
        Assert.Equal(0, Store.LoadProgress(b.Id).Xp);
    }

    [Fact]
    public void Unknown_avatars_fall_back_to_the_pipit() =>
        Assert.Equal("pipit", Store.Create("X", "dragon", "en").Avatar);

    [Fact]
    public void Deleting_removes_the_player_and_their_progress()
    {
        var a = Store.Create("Lena", "robin", "nl");
        Store.SaveProgress(a.Id, new PlayerProgress { Xp = 5 });
        Store.Delete(a.Id);
        Assert.Empty(Store.LoadProfiles());
        Assert.Equal(0, Store.LoadProgress(a.Id).Xp);
    }

    [Fact]
    public void Single_player_files_become_the_first_player()
    {
        File.WriteAllText(Path.Combine(_root, "settings.json"), """
            { "schemaVersion": 2, "language": "nl", "inputDevice": "Microphone Array", "fullscreen": true,
              "latencyMsByDevice": { "Default": 76 }, "headphonesConfirmed": true,
              "instruments": [ { "id": "s1", "name": "Yamaha", "kind": "soprano", "fingering": "baroque", "measuredOffsetCents": 19.5 } ],
              "activeInstrumentId": "s1" }
            """);
        File.WriteAllText(Path.Combine(_root, "progress.json"), """{ "xp": 858, "streakDays": 2 }""");

        var player = Store.MigrateLegacy("Player 1");
        Assert.NotNull(player);
        var machine = Store.LoadMachine();
        Assert.Equal("Microphone Array", machine.InputDevice);
        Assert.Equal(76, machine.LatencyMsByDevice["Default"]);
        Assert.True(machine.Fullscreen && machine.HeadphonesConfirmed);
        Assert.Equal(player!.Id, machine.LastPlayerId);

        var loaded = Assert.Single(Store.LoadProfiles());
        Assert.Equal("Player 1", loaded.Name);
        Assert.Equal("nl", loaded.Settings.Language);
        Assert.Equal("Yamaha", loaded.Settings.ActiveInstrument.Name);
        Assert.Equal(19.5, loaded.Settings.ActiveInstrument.MeasuredOffsetCents);
        Assert.True(loaded.IntroSeen); // an existing player has used the app already
        Assert.Equal(858, Store.LoadProgress(loaded.Id).Xp);
        Assert.True(File.Exists(Path.Combine(_root, "settings.json.migrated")));
        Assert.Null(Store.MigrateLegacy("again")); // only once
    }

    [Fact]
    public void Nothing_to_migrate_on_a_fresh_computer() => Assert.Null(Store.MigrateLegacy("Player 1"));
}
