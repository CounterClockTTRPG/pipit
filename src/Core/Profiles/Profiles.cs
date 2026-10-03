using Pipit.Core.Game;
using Pipit.Core.Settings;

namespace Pipit.Core.Profiles;

/// <summary>Settings that belong to the computer, shared by every player: the microphone and the screen.</summary>
public sealed class MachineSettings
{
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Godot input device name; "Default" follows the Windows default device.</summary>
    public string InputDevice { get; set; } = "Default";
    public Dictionary<string, double> LatencyMsByDevice { get; set; } = new();
    public Dictionary<string, double> GateRmsByDevice { get; set; } = new();
    public bool Fullscreen { get; set; }
    /// <summary>Someone confirmed headphones are used on this computer, so the band may play.</summary>
    public bool HeadphonesConfirmed { get; set; }
    public string? LastPlayerId { get; set; }
    /// <summary>The language of the screens before a player is chosen (the start screen, a new player); "" until set.</summary>
    public string Language { get; set; } = "";
}

/// <summary>A player: who they are (name, avatar bird) and their own settings and recorders.</summary>
public sealed class PlayerProfile
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>One of <see cref="Avatars.All"/>.</summary>
    public string Avatar { get; set; } = Avatars.All[0];
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>Pip has introduced the app to this player.</summary>
    public bool IntroSeen { get; set; }
    public AppSettings Settings { get; set; } = new AppSettings().Normalize();
}

public static class Avatars
{
    /// <summary>The birds a player can be. The app draws each with Pip's rig in its own plumage.</summary>
    public static readonly IReadOnlyList<string> All = new[] { "pipit", "robin", "bluetit", "goldfinch", "bluebird", "greenfinch" };
}

/// <summary>
/// Players on one computer, in a folder: machine.json, and per player
/// profiles/&lt;id&gt;/profile.json and progress.json. Before profiles existed there was one
/// settings.json (machine and player settings mixed) and one progress.json; the first start
/// turns those into machine.json and a first player.
/// </summary>
public sealed class ProfileStore(string root)
{
    private string MachinePath => Path.Combine(root, "machine.json");
    private string ProfilesDir => Path.Combine(root, "profiles");
    private string ProfileDir(string id) => Path.Combine(ProfilesDir, id);

    public MachineSettings LoadMachine() => JsonStore.LoadOrDefault<MachineSettings>(MachinePath);
    public void SaveMachine(MachineSettings machine) => JsonStore.Save(MachinePath, machine);

    public List<PlayerProfile> LoadProfiles()
    {
        if (!Directory.Exists(ProfilesDir))
            return new List<PlayerProfile>();
        return Directory.GetDirectories(ProfilesDir)
            .Select(d => Path.Combine(d, "profile.json"))
            .Where(File.Exists)
            .Select(p => JsonStore.LoadOrDefault<PlayerProfile>(p))
            .Where(p => p.Name != "")
            .Select(p => { p.Settings.Normalize(); return p; })
            .OrderBy(p => p.CreatedAt).ToList();
    }

    public PlayerProfile Create(string name, string avatar, string language)
    {
        var profile = new PlayerProfile { Name = name.Trim(), Avatar = Avatars.All.Contains(avatar) ? avatar : Avatars.All[0] };
        profile.Settings.Language = language;
        Save(profile);
        return profile;
    }

    public void Save(PlayerProfile profile) => JsonStore.Save(Path.Combine(ProfileDir(profile.Id), "profile.json"), profile);

    public PlayerProgress LoadProgress(string id) => JsonStore.LoadOrDefault<PlayerProgress>(Path.Combine(ProfileDir(id), "progress.json"));
    public void SaveProgress(string id, PlayerProgress progress) => JsonStore.Save(Path.Combine(ProfileDir(id), "progress.json"), progress);

    /// <summary>Removes a player and their progress for good.</summary>
    public void Delete(string id)
    {
        if (Directory.Exists(ProfileDir(id)))
            Directory.Delete(ProfileDir(id), recursive: true);
    }

    /// <summary>
    /// The single-player files become machine.json and a first player called
    /// <paramref name="firstName"/>; the old files are kept as *.migrated. Returns the new player,
    /// or null when there was nothing to migrate (or profiles already exist).
    /// </summary>
    public PlayerProfile? MigrateLegacy(string firstName)
    {
        var legacySettings = Path.Combine(root, "settings.json");
        if (LoadProfiles().Count > 0 || !File.Exists(legacySettings))
            return null;
        // The old file had both kinds of settings; each class reads the fields it knows.
        var machine = JsonStore.LoadOrDefault<MachineSettings>(legacySettings);
        var settings = JsonStore.LoadOrDefault<AppSettings>(legacySettings).Normalize();
        var profile = new PlayerProfile { Name = firstName, Settings = settings, IntroSeen = true };
        machine.LastPlayerId = profile.Id;
        SaveMachine(machine);
        Save(profile);
        var legacyProgress = Path.Combine(root, "progress.json");
        if (File.Exists(legacyProgress))
        {
            SaveProgress(profile.Id, JsonStore.LoadOrDefault<PlayerProgress>(legacyProgress));
            File.Move(legacyProgress, legacyProgress + ".migrated", overwrite: true);
        }
        File.Move(legacySettings, legacySettings + ".migrated", overwrite: true);
        return profile;
    }
}
