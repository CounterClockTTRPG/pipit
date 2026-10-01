using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Pipit.App.Audio;
using Pipit.Core.Analysis;
using Pipit.Core.Instruments;
using Pipit.Core.Music;
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

    public AppSettings Settings { get; private set; } = new();
    public AudioHub Audio { get; private set; } = null!;
    public ClickPlayer Clicks { get; private set; } = null!;
    private readonly Dictionary<FingeringSystem, FingeringChart> _charts = new();

    public static string UserDir => ProjectSettings.GlobalizePath("user://");
    public static string SettingsPath => Path.Combine(UserDir, "settings.json");
    public static string RecordingsDir => Path.Combine(UserDir, "recordings");

    public InstrumentProfile Instrument => Settings.Instrument;
    public RecorderInfo Recorder => Settings.Instrument.Info;
    public FingeringChart Chart => ChartFor(Settings.Instrument.Fingering);

    /// <summary>Raised after the instrument, device or naming changes, so screens can redraw.</summary>
    public event Action? Changed;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        Settings = JsonStore.LoadOrDefault<AppSettings>(SettingsPath);
        Audio = new AudioHub { Name = "AudioHub" };
        AddChild(Audio);
        Clicks = new ClickPlayer { Name = "Clicks" };
        Clicks.Init(Audio);
        AddChild(Clicks);
        Audio.SelectDevice(Settings.InputDevice);
        ApplyInstrument();
    }

    public FingeringChart ChartFor(FingeringSystem system)
    {
        if (!_charts.TryGetValue(system, out var chart))
            _charts[system] = chart = FingeringChart.Load(name => FileAccess.GetFileAsString("res://data/fingerings/" + name), system);
        return chart;
    }

    public void Save()
    {
        JsonStore.Save(SettingsPath, Settings);
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

    public double? DeviceGate => Settings.GateRmsByDevice.TryGetValue(Audio.CurrentDevice, out var g) ? g : null;
    public double? DeviceLatencyMs => Settings.LatencyMsByDevice.TryGetValue(Audio.CurrentDevice, out var l) ? l : null;

    public void SelectDevice(string name)
    {
        Audio.SelectDevice(name);
        Settings.InputDevice = Audio.CurrentDevice;
        ApplyInstrument();
        Save();
    }

    // ---- note names ------------------------------------------------------------------

    /// <summary>The display name of a written MIDI note, with an octave mark ("Sol", "Sol'", "Do''").</summary>
    public string NoteName(int writtenMidi)
    {
        var pitch = Spell(writtenMidi);
        var name = NoteNames.Name(pitch, Settings.Naming, Settings.Language);
        // Octave marks relative to the instrument's first octave, as recorder methods do.
        var octave = (writtenMidi - Recorder.BottomWrittenMidi) / 12;
        return name + new string('\'', Math.Max(0, octave));
    }

    /// <summary>Spells a MIDI number with sharps, except B♭ which recorder music uses far more than A♯.</summary>
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
