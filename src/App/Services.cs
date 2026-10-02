using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Pipit.App.Audio;
using Pipit.Core.Analysis;
using Pipit.Core.Game;
using Pipit.Core.Instruments;
using Pipit.Core.Lessons;
using Pipit.Core.Music;
using Pipit.Core.Profiles;
using Pipit.Core.Settings;
using FileAccess = Godot.FileAccess;

namespace Pipit.App;

/// <summary>
/// Autoload holding the app-wide pieces: settings, fingering charts, the microphone hub and
/// the click player. Screens reach it through <see cref="Instance"/>.
/// </summary>
public partial class Services : Node
{
    public static Services Instance { get; private set; } = null!;

    /// <summary>Players live in user://profiles; the computer's settings in user://machine.json.</summary>
    public ProfileStore Store { get; private set; } = null!;
    public MachineSettings Machine { get; private set; } = new();
    public PlayerProfile Player { get; private set; } = new() { Name = "Guest" };
    public List<PlayerProfile> Players { get; private set; } = new();
    /// <summary>The current player's settings.</summary>
    public AppSettings Settings => Player.Settings;
    /// <summary>True until a real player is chosen (smoke tests, a computer with no players yet).</summary>
    public bool IsGuest => Players.All(p => p.Id != Player.Id);
    public AudioHub Audio { get; private set; } = null!;
    public ClickPlayer Clicks { get; private set; } = null!;
    private readonly Dictionary<FingeringSystem, FingeringChart> _charts = new();

    public static string UserDir => ProjectSettings.GlobalizePath("user://");
    public static string RecordingsDir => Path.Combine(UserDir, "recordings");
    public static string RunsDir => Path.Combine(RecordingsDir, "runs");

    public SongLibrary Songs { get; private set; } = null!;

    public StudyPath Study { get; private set; } = null!;
    private readonly Dictionary<string, Lesson?> _lessons = new();

    /// <summary>A lesson from res://data/lessons/&lt;id&gt;/, cached; null (and an error) if it does not load.</summary>
    public Lesson? LoadLesson(string id)
    {
        if (_lessons.TryGetValue(id, out var cached))
            return cached;
        try
        {
            var lesson = LessonParser.Load(id, file =>
            {
                var path = $"res://data/lessons/{id}/{file}";
                return FileAccess.FileExists(path) ? FileAccess.GetFileAsString(path) : null;
            });
            return _lessons[id] = lesson;
        }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException)
        {
            GD.PushError($"Lesson {id}: {e.Message}");
            return _lessons[id] = null;
        }
    }

    /// <summary>Bravura, the SMuFL music font (SIL OFL), for the score view.</summary>
    public Font? MusicFont { get; private set; }
    /// <summary>The current player's XP, levels, song records and leaderboards.</summary>
    public PlayerProgress Progress { get; private set; } = new();

    /// <summary>Raised after XP is awarded, with the award (for toasts and the home screen).</summary>
    public event Action<XpAward>? XpAwarded;

    /// <summary>The recorder currently being played.</summary>
    public InstrumentProfile Instrument => Settings.ActiveInstrument;
    public RecorderInfo Recorder => Instrument.Info;
    public FingeringChart Chart => ChartFor(Instrument.Fingering);

    /// <summary>Raised after the instrument, device or naming changes, so screens can redraw.</summary>
    public event Action? Changed;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        Store = new ProfileStore(UserDir);
        Store.MigrateLegacy("Player 1"); // the single-player files from before profiles
        Machine = Store.LoadMachine();
        Players = Store.LoadProfiles();
        // Start as the last player so every screen works; the profile screen lets them change.
        Player = Players.FirstOrDefault(p => p.Id == Machine.LastPlayerId) ?? Players.FirstOrDefault() ?? new PlayerProfile { Name = "Guest" };
        ApplyFullscreen();
        Audio = new AudioHub { Name = "AudioHub" };
        AddChild(Audio);
        Clicks = new ClickPlayer { Name = "Clicks" };
        Clicks.Init(Audio);
        AddChild(Clicks);
        Audio.SelectDevice(Machine.InputDevice);
        ApplyInstrument();
        Songs = SongLibrary.Load(name => FileAccess.GetFileAsString("res://data/songs/" + name));
        foreach (var error in Songs.Errors)
            GD.PushError("Song: " + error);
        Progress = IsGuest ? new PlayerProgress() : Store.LoadProgress(Player.Id);
        ApplyLanguage();
        MusicFont = LoadMusicFont();
        Study = StudyPath.Parse(FileAccess.GetFileAsString("res://data/path.json"));
    }

    /// <summary>
    /// The imported font resource when the editor has imported it (and in exported builds);
    /// otherwise the raw file, so a fresh checkout works without an editor import.
    /// </summary>
    private static Font? LoadMusicFont()
    {
        const string path = "res://assets/fonts/Bravura.otf";
        if (ResourceLoader.Exists(path) && ResourceLoader.Load<Font>(path) is { } imported)
            return imported;
        var bytes = FileAccess.GetFileAsBytes(path);
        if (bytes.Length == 0)
        {
            GD.PushError("Bravura.otf not found: the score view is unavailable");
            return null;
        }
        return new FontFile { Data = bytes };
    }

    public void SetFullscreen(bool on)
    {
        Machine.Fullscreen = on;
        ApplyFullscreen();
        Save();
    }

    private void ApplyFullscreen()
    {
        if (DisplayServer.GetName() == "headless")
            return;
        DisplayServer.WindowSetMode(Machine.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
    }

    public FingeringChart ChartFor(FingeringSystem system)
    {
        if (!_charts.TryGetValue(system, out var chart))
            _charts[system] = chart = FingeringChart.Load(name => FileAccess.GetFileAsString("res://data/fingerings/" + name), system);
        return chart;
    }

    public void Save()
    {
        Store.SaveMachine(Machine);
        if (!IsGuest)
            Store.Save(Player);
        Changed?.Invoke();
    }

    /// <summary>Points the pitch tracker at the active instrument's range and the device's gate.</summary>
    public void ApplyInstrument()
    {
        var info = Recorder;
        Audio.Configure(PitchTrackerOptions.ForRange(info.BottomSoundingMidi, info.TopSoundingMidi, Instrument.ReferenceA), DeviceGate);
    }

    /// <summary>A wide search (any recorder) for the free tuner.</summary>
    public void ApplyWideRange() =>
        Audio.Configure(PitchTrackerOptions.ForRange(RecorderInfo.Of(RecorderKind.Tenor).BottomSoundingMidi,
            RecorderInfo.Of(RecorderKind.Soprano).TopSoundingMidi, Instrument.ReferenceA), DeviceGate);

    public void SetActiveInstrument(string id)
    {
        if (Settings.ActiveInstrumentId == id || Settings.Instruments.All(i => i.Id != id))
            return;
        Settings.ActiveInstrumentId = id;
        ApplyInstrument();
        Save();
    }

    public InstrumentProfile AddInstrument(RecorderKind kind, FingeringSystem fingering)
    {
        var added = Settings.AddInstrument(kind, fingering);
        ApplyInstrument();
        Save();
        return added;
    }

    public bool RemoveInstrument(string id)
    {
        if (!Settings.RemoveInstrument(id))
            return false;
        ApplyInstrument();
        Save();
        return true;
    }

    /// <summary>Awards XP and unlocks any achievements earned by <paramref name="e"/> (or by streaks and levels).</summary>
    public XpAward AwardXp(IEnumerable<XpLine> lines, AchievementEvent? e = null)
    {
        var chapters = Songs.Songs.GroupBy(s => s.Chapter)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(s => s.Id).ToList());
        var award = Progress.Award(lines, DateTime.Now, e ?? new AchievementEvent(), chapters, Instrument.Kind.ToString());
        SaveProgress();
        XpAwarded?.Invoke(award);
        return award;
    }

    public SongRecord SongRecord(string songId) => Progress.Song(songId, Instrument.Kind.ToString());

    public void SaveProgress()
    {
        if (!IsGuest)
            Store.SaveProgress(Player.Id, Progress);
    }

    // ---- players --------------------------------------------------------------------------

    /// <summary>
    /// Switches the interface to the player's language: data/i18n/&lt;lang&gt;.json maps English
    /// texts to translations and is loaded into Godot's TranslationServer (no import step).
    /// </summary>
    private readonly HashSet<string> _loadedLanguages = new();

    public void ApplyLanguage()
    {
        var language = Settings.Language;
        if (language != "en" && _loadedLanguages.Add(language))
        {
            var path = $"res://data/i18n/{language}.json";
            if (FileAccess.FileExists(path))
            {
                var table = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(FileAccess.GetFileAsString(path)) ?? new();
                var translation = new Translation { Locale = language };
                foreach (var (english, translated) in table)
                    if (translated != "")
                        translation.AddMessage(english, translated);
                TranslationServer.AddTranslation(translation);
            }
        }
        TranslationServer.SetLocale(language);
        // Dates and numbers too ("1 okt", "1,5 s"): Belgian Dutch, British English, French.
        var culture = System.Globalization.CultureInfo.GetCultureInfo(language switch { "nl" => "nl-BE", "fr" => "fr-BE", _ => "en-GB" });
        System.Globalization.CultureInfo.CurrentCulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
    }

    public void SelectPlayer(PlayerProfile player)
    {
        Player = player;
        Progress = Store.LoadProgress(player.Id);
        Machine.LastPlayerId = player.Id;
        Store.SaveMachine(Machine);
        ApplyLanguage();
        ApplyInstrument();
        Changed?.Invoke();
    }

    public PlayerProfile CreatePlayer(string name, string avatar, string language)
    {
        var player = Store.Create(name, avatar, language);
        Players = Store.LoadProfiles();
        SelectPlayer(Players.First(p => p.Id == player.Id));
        return Player;
    }

    public void DeletePlayer(string id)
    {
        Store.Delete(id);
        Players = Store.LoadProfiles();
        if (Player.Id == id)
        {
            Player = Players.FirstOrDefault() ?? new PlayerProfile { Name = "Guest" };
            Progress = IsGuest ? new PlayerProgress() : Store.LoadProgress(Player.Id);
            ApplyInstrument();
        }
    }

    public double? DeviceGate => Machine.GateRmsByDevice.TryGetValue(Audio.CurrentDevice, out var g) ? g : null;
    public double? DeviceLatencyMs => Machine.LatencyMsByDevice.TryGetValue(Audio.CurrentDevice, out var l) ? l : null;

    public void SelectDevice(string name)
    {
        Audio.SelectDevice(name);
        Machine.InputDevice = Audio.CurrentDevice;
        ApplyInstrument();
        Save();
    }

    // ---- note names ------------------------------------------------------------------

    /// <summary>The display name of a written MIDI note, with an octave mark ("Sol", "Sol'", "Do''").</summary>
    public string NoteName(int writtenMidi) => NoteName(Spell(writtenMidi, PreferFlats));

    /// <summary>The display name of a written pitch, keeping its spelling (E♭ stays Mi♭).</summary>
    public string NoteName(Pitch pitch)
    {
        var writtenMidi = pitch.Midi;
        var name = NoteNames.Name(pitch, Settings.Naming, Settings.Language);
        // Octave marks relative to the instrument's first octave, as recorder methods do.
        var octave = (writtenMidi - Recorder.BottomWrittenMidi) / 12;
        return name + new string('\'', Math.Max(0, octave));
    }

    /// <summary>Set by the play screen while a song in a flat key is open, so names use flats.</summary>
    public bool PreferFlats { get; set; }

    /// <summary>
    /// Spells a MIDI number: sharps, except B♭ which recorder music uses far more than A♯; with
    /// <paramref name="flats"/> (flat keys) every black key is a flat.
    /// </summary>
    public static Pitch Spell(int midi, bool flats)
    {
        if (!flats)
            return Spell(midi);
        var pc = ((midi % 12) + 12) % 12;
        var (step, alter) = pc switch
        {
            1 => (Step.D, -1), 3 => (Step.E, -1), 6 => (Step.G, -1), 8 => (Step.A, -1), 10 => (Step.B, -1),
            _ => (Spell(midi).Step, 0),
        };
        return new Pitch(step, alter, midi / 12 - 1);
    }

    public static Pitch Spell(int midi)
    {
        var pc = ((midi % 12) + 12) % 12;
        var octave = midi / 12 - 1;
        var (step, alter) = pc switch
        {
            0 => (Step.C, 0), 1 => (Step.C, 1), 2 => (Step.D, 0), 3 => (Step.D, 1), 4 => (Step.E, 0),
            5 => (Step.F, 0), 6 => (Step.F, 1), 7 => (Step.G, 0), 8 => (Step.G, 1), 9 => (Step.A, 0),
            10 => (Step.B, -1), _ => (Step.B, 0),
        };
        return new Pitch(step, alter, octave);
    }

    public Fingering? FingeringFor(int writtenMidi) =>
        Recorder.StepOf(writtenMidi) is { } step ? Chart.Primary(step) : null;
}
