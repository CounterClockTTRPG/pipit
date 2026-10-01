using System;
using System.Linq;
using Godot;
using Pipit.App.UI;
using Pipit.App.Views;
using Pipit.Core.Instruments;
using Pipit.Core.Music;

namespace Pipit.App.Screens;

public partial class SettingsScreen : Screen
{
    private readonly ItemList _devices;
    private readonly LevelMeter _meter = new();
    private readonly Label _testStatus;
    private readonly Button _testButton;
    private AudioStreamPlayer? _testPlayer;
    private float[]? _testBuffer;
    private int _testFill;

    public override string Title => "Settings";

    private static readonly string[] Languages = { "en", "nl", "fr" };

    public SettingsScreen()
    {
        var i = S.Instrument;

        // Instrument column.
        var name = new LineEdit { Text = i.Name };
        name.TextChanged += t => { S.Instrument.Name = t; S.Save(); };
        var kinds = Enum.GetValues<RecorderKind>();
        var kind = Ui.Options(kinds.Select(k => k.ToString()).ToArray(), Array.IndexOf(kinds, i.Kind), ix => { S.Instrument.Kind = kinds[ix]; Changed(); });
        var systems = Enum.GetValues<FingeringSystem>();
        var system = Ui.Options(systems.Select(s => s.ToString()).ToArray(), Array.IndexOf(systems, i.Fingering), ix => { S.Instrument.Fingering = systems[ix]; Changed(); });
        var keyed = new CheckBox { Text = Ui.T("Holes 6 and 7 are keys (some tenors)"), ButtonPressed = i.Keyed };
        keyed.Toggled += on => { S.Instrument.Keyed = on; S.Save(); };
        var refA = new SpinBox { MinValue = 400, MaxValue = 466, Step = 1, Value = i.ReferenceA, Suffix = "Hz" };
        refA.ValueChanged += v => { S.Instrument.ReferenceA = v; Changed(); };
        var language = Ui.Options(new[] { "English", "Nederlands", "Français" }, Math.Max(0, Array.IndexOf(Languages, S.Settings.Language)), ix => { S.Settings.Language = Languages[ix]; S.Save(); });
        var naming = Ui.Options(new[] { "Do Re Mi (fixed Do)", "C D E (letters)" }, (int)S.Settings.Naming, ix => { S.Settings.Naming = (NoteNaming)ix; S.Save(); });

        var instrument = Ui.VBox(10,
            Ui.Label("Your recorder", 26, PipitTheme.Accent),
            Ui.Label("Name"), name,
            Ui.Label("Size"), kind,
            Ui.Label("Fingering system"), system,
            Ui.Label("Most recorders are baroque (also called English). German-fingered school recorders have a simpler Fa; look for a 'G' or 'German' marking."
                , 15, PipitTheme.InkSoft),
            keyed,
            Ui.Label("Pitch of A (440 Hz is standard; 442 is common in Europe; 415 for baroque-pitch instruments)", 15, PipitTheme.InkSoft), refA,
            Ui.Label("Language"), language,
            Ui.Label("Note names"), naming);

        // Microphone column.
        _devices = Ui.MinSize(new ItemList(), 0, 200);
        _devices.ItemSelected += ix => S.SelectDevice(_devices.GetItemText((int)ix));
        _testStatus = Ui.Label("", 16, PipitTheme.InkSoft);
        _testButton = Ui.Button("Test: record 3 s and play back", StartTest, 18);
        var mic = Ui.VBox(10,
            Ui.Label("Microphone", 26, PipitTheme.Accent),
            Ui.Label("Play or speak: the bar shows what the selected microphone hears. \"Default\" is the Windows default device, normally the built-in microphone.", 15, PipitTheme.InkSoft),
            _devices, _meter, _testButton, _testStatus,
            Ui.Label("Tips", 20, PipitTheme.Ink),
            Ui.Label("• No signal at all? Check Windows Settings → Privacy & security → Microphone → \"Let desktop apps access your microphone\".\n" +
                     "• Windows \"audio enhancements\" (noise suppression, automatic gain) can swallow quiet notes and blur the start of notes. " +
                     "Turn them off under Settings → System → Sound → your microphone → Audio enhancements.\n" +
                     "• After changing microphone, run Calibrate again.", 15, PipitTheme.InkSoft));

        var columns = Ui.HBox(32, Ui.Expand(instrument), Ui.Expand(mic));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(columns));
        AddChild(scroll);
    }

    private static void Changed()
    {
        S.ApplyInstrument();
        S.Save();
    }

    public override void Enter()
    {
        _devices.Clear();
        foreach (var d in Audio.AudioHub.InputDevices())
        {
            var ix = _devices.AddItem(d);
            if (d == S.Audio.CurrentDevice)
                _devices.Select(ix);
        }
        S.Audio.SamplesCaptured += OnSamples;
    }

    public override void Exit()
    {
        S.Audio.SamplesCaptured -= OnSamples;
        _testPlayer?.Stop();
    }

    public override void _Process(double delta)
    {
        _meter.Db = S.Audio.LevelDb;
        _meter.GateDb = S.DeviceGate is { } g ? 20 * Math.Log10(g) : null;
    }

    private void StartTest()
    {
        _testBuffer = new float[S.Audio.SampleRate * 3];
        _testFill = 0;
        _testButton.Disabled = true;
        _testStatus.Text = Ui.T("Recording… play a few notes.");
    }

    private void OnSamples(float[] samples, double time)
    {
        if (_testBuffer is null)
            return;
        var n = Math.Min(samples.Length, _testBuffer.Length - _testFill);
        Array.Copy(samples, 0, _testBuffer, _testFill, n);
        _testFill += n;
        if (_testFill < _testBuffer.Length)
            return;
        var buffer = _testBuffer;
        _testBuffer = null;
        _testStatus.Text = Ui.T("Playing back…");
        _testPlayer ??= AddPlayer(this);
        _testPlayer.Stream = Playback.ToStream(buffer, S.Audio.SampleRate);
        _testPlayer.Play();
    }

    private AudioStreamPlayer AddPlayer(Node parent)
    {
        var p = new AudioStreamPlayer();
        parent.AddChild(p);
        p.Finished += () => { _testButton.Disabled = false; _testStatus.Text = Ui.T("Done. Did that sound like you?"); };
        return p;
    }
}
