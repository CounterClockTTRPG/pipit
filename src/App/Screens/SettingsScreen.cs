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
    private readonly Button _checkButton;
    private readonly Label _checkStatus;

    public override string Title => "Settings";

    private static readonly string[] Languages = { "en", "nl", "fr" };

    public SettingsScreen()
    {
        var editor = new InstrumentEditor();
        var language = Ui.Options(new[] { "English", "Nederlands", "Français" }, Math.Max(0, Array.IndexOf(Languages, S.Settings.Language)), ix =>
        {
            S.Settings.Language = Languages[ix];
            S.Save();
            // Screens build their texts once, so rebuild them in the new language.
            S.ApplyLanguage();
            Main.Instance.ShowRoot(new HomeScreen());
            Main.Instance.Show(new SettingsScreen());
        });
        var naming = Ui.Options(new[] { "Do Re Mi (fixed Do)", "C D E (letters)" }, (int)S.Settings.Naming, ix => { S.Settings.Naming = (NoteNaming)ix; S.Save(); });

        var instrument = Ui.VBox(10,
            Ui.Heading("Your recorders", 26),
            editor,
            Ui.Label("Language"), language,
            Ui.Label("Note names"), naming,
            Ui.Label("Sound"), SoundOptions(),
            FullscreenToggle());

        // Microphone column.
        _devices = Ui.MinSize(new ItemList(), 0, 200);
        _devices.ItemSelected += ix => S.SelectDevice(_devices.GetItemText((int)ix));
        _testStatus = Ui.Text("", 16, PipitTheme.InkSoft);
        _testButton = Ui.Button("Test: record 3 s and play back", StartTest, 18);
        _checkButton = Ui.Button("Check for Windows voice processing", RunCheck, 18);
        _checkStatus = Ui.Text("", 16, PipitTheme.InkSoft);
        var android = Services.IsAndroid;
        var checkText = Ui.Text("The check plays a short tone through your speakers (take headphones off) and listens whether the microphone keeps hearing it.", 15, PipitTheme.InkSoft);
        // The voice-processing check is about Windows' own processing; Android tablets have none to switch off.
        checkText.Visible = _checkButton.Visible = _checkStatus.Visible = !android;
        var mic = Ui.VBox(10,
            Ui.Heading("Microphone", 26),
            android
                ? Ui.Text("Play or speak: the bar shows what the selected microphone hears. \"Default\" is the tablet's own microphone.", 15, PipitTheme.InkSoft)
                : Ui.Text("Play or speak: the bar shows what the selected microphone hears. \"Default\" is the Windows default device, normally the built-in microphone.", 15, PipitTheme.InkSoft),
            _devices, _meter, _testButton, _testStatus,
            checkText, _checkButton, _checkStatus,
            Ui.Label("Tips", 20, PipitTheme.Ink),
            android
                ? Ui.Text("• No signal at all? Allow Pipit to use the microphone: Android Settings → Apps → Pipit → Permissions → Microphone.\n" +
                          "• Put the tablet in front of you, about an arm's length away, with its microphone towards your recorder.\n" +
                          "• After changing microphone, run Calibrate again.", 15, PipitTheme.InkSoft)
                : Ui.Text("• No signal at all? Check Windows Settings → Privacy & security → Microphone → \"Let desktop apps access your microphone\".\n" +
                     "• Windows \"audio enhancements\" (noise suppression, automatic gain) can swallow quiet notes and blur the start of notes. " +
                     "Turn them off under Settings → System → Sound → your microphone → Audio enhancements.\n" +
                     "• After changing microphone, run Calibrate again.", 15, PipitTheme.InkSoft));

        var columns = Ui.HBox(32, Ui.Expand(instrument), Ui.Expand(mic));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(columns));
        AddChild(scroll);
    }

    /// <summary>Voice for "Listen" and lessons, and the headphones confirmation the band needs.</summary>
    private static Control SoundOptions()
    {
        var voices = new[] { "natural", "simple" };
        var voice = Ui.Options(new[] { "Natural recorder", "Simple (electronic)" }, Math.Max(0, Array.IndexOf(voices, S.Settings.Voice)), ix => { S.Settings.Voice = voices[ix]; S.Save(); });
        var headphones = new CheckBox { Text = Ui.T("I use headphones (needed for the band)"), ButtonPressed = S.Machine.HeadphonesConfirmed };
        headphones.Toggled += on => { S.Machine.HeadphonesConfirmed = on; S.Save(); };
        var box = Ui.VBox(6, voice, headphones);
        if (!Audio.Synth.SoundFontInstalled)
            box.AddChild(Ui.Text("The natural voice and the band need the SoundFont: run scripts/fetch-soundfont.sh and restart Pipit. Until then the simple voice is used.", 14, PipitTheme.Warn));
        return box;
    }

    private static CheckBox FullscreenToggle()
    {
        var box = new CheckBox { Text = Ui.T("Full screen (F11)"), ButtonPressed = S.Machine.Fullscreen, Visible = !Services.IsAndroid };
        box.Toggled += S.SetFullscreen;
        return box;
    }

    private async void RunCheck()
    {
        _checkButton.Disabled = true;
        _checkStatus.Modulate = Colors.White;
        _checkStatus.Text = Ui.T("Listening… (you will hear a beep)");
        var tester = new Audio.MicTester();
        AddChild(tester);
        var result = await tester.Run();
        tester.QueueFree();
        if (!IsInsideTree())
            return;
        _checkButton.Disabled = false;
        (_checkStatus.Text, _checkStatus.Modulate) = result.Verdict switch
        {
            Core.Analysis.MicCheckVerdict.Ok => (Ui.T("Good: the microphone hears steady notes."), PipitTheme.Good),
            Core.Analysis.MicCheckVerdict.Suppressed => (Ui.T("Windows voice processing is removing steady tones, so held notes disappear. " +
                "Open Windows Settings → System → Sound → Input → your microphone, set \"Audio enhancements\" to Off and switch off \"Voice clarity\" if you have it. Then check again."), PipitTheme.Bad),
            _ => (Ui.T("The microphone did not hear the test tone at all. Either headphones are on or the volume is muted, or Windows echo cancellation is removing Pipit's own sound. " +
                "Turn the volume up and take headphones off; if it still fails, set \"Audio enhancements\" to Off for your microphone (Windows Settings → System → Sound → Input) and check again."), PipitTheme.Warn),
        };
        _checkStatus.Text += $"  ({result.OnsetDb:0} → {result.SustainDb:0} dB)";
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
