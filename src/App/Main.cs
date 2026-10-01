using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.Screens;
using Pipit.App.UI;

namespace Pipit.App;

/// <summary>Root of the app: theme, top bar with a back button, and a stack of screens.</summary>
public partial class Main : Control
{
    private readonly Stack<Screen> _stack = new();
    private Control _content = null!;
    private Label _title = null!;
    private Button _back = null!;
    private int _smokeFrame = -1;
    private readonly Queue<System.Func<Screen>> _smokeScreens = new();

    public static Main Instance { get; private set; } = null!;

    public override void _Ready()
    {
        Instance = this;
        GetWindow().Title = (string)ProjectSettings.GetSetting("application/config/name");
        Theme = PipitTheme.Build();
        SetAnchorsPreset(LayoutPreset.FullRect);

        AddChild(new ColorRect { Color = PipitTheme.Paper, AnchorRight = 1, AnchorBottom = 1 });
        var layout = Ui.VBox(0);
        layout.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(layout);

        var bar = new PanelContainer();
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = PipitTheme.PaperDark, ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 8, ContentMarginBottom = 8 });
        _back = Ui.Button("← Back", Back, 18);
        _title = Ui.Label("", 24, PipitTheme.Ink);
        bar.AddChild(Ui.HBox(16, _back, _title));
        layout.AddChild(bar);

        _content = Ui.Expand(new Control(), true, true);
        layout.AddChild(_content);

        Show(new HomeScreen());

        if (OS.GetCmdlineUserArgs().Contains("--smoke"))
            StartSmoke();
        if (OS.GetCmdlineUserArgs().Contains("--micprobe"))
            MicProbe();
        if (OS.GetCmdlineUserArgs().Contains("--shots"))
            Screenshots();
        if (OS.GetCmdlineUserArgs().Contains("--miccheck"))
            MicCheckAndQuit();
        if (OS.GetCmdlineUserArgs().Contains("--loopback"))
        {
            var d = new Diagnostics();
            AddChild(d);
            d.Loopback();
        }
    }

    /// <summary>`-- --shots`: saves a PNG of every screen to user://shots and quits (needs a window).</summary>
    private async void Screenshots()
    {
        var dir = System.IO.Path.Combine(Services.UserDir, "shots");
        System.IO.Directory.CreateDirectory(dir);
        var screens = new (string, System.Func<Screen>?)[] { ("home", null), ("tuner", () => new TunerScreen()), ("trainer", () => new TrainerScreen()),
            ("calibrate", () => new CalibrationScreen()), ("studio", () => new StudioScreen()), ("settings", () => new SettingsScreen()) };
        foreach (var (name, make) in screens)
        {
            if (make is not null)
                Show(make());
            for (var i = 0; i < 20; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, name + ".png"));
            if (make is not null)
                Back();
        }
        var calibrate = new CalibrationScreen();
        Show(calibrate);
        calibrate.DebugStartLatency();
        await ToSignal(GetTree().CreateTimer(3.3), SceneTreeTimer.SignalName.Timeout);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "calibrate-beats.png"));
        Back();
        var tuning = new CalibrationScreen();
        Show(tuning);
        tuning.DebugStartTuning();
        for (var i = 0; i < 20; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "calibrate-tuning.png"));
        Back();
        GD.Print("SHOTS " + dir);
        GetTree().Quit();
    }

    /// <summary>`-- --miccheck`: the Settings speaker-to-microphone check, printed, then quit.</summary>
    private async void MicCheckAndQuit()
    {
        var tester = new Audio.MicTester();
        AddChild(tester);
        await tester.Run();
        GetTree().Quit();
    }

    /// <summary>`-- --micprobe`: lists input devices, listens for 3 s, prints what arrived, quits.</summary>
    private async void MicProbe()
    {
        var audio = Services.Instance.Audio;
        GD.Print($"MICPROBE driver={AudioServer.GetDriverName()} mix_rate={AudioServer.GetMixRate()} device='{audio.CurrentDevice}'");
        GD.Print("MICPROBE devices: " + string.Join(" | ", Audio.AudioHub.InputDevices()));
        long samples = 0;
        double peakDb = -120;
        var voiced = 0;
        audio.SamplesCaptured += (s, _) => samples += s.Length;
        audio.FrameReady += f => { peakDb = System.Math.Max(peakDb, f.Db); if (f.Voiced) voiced++; };
        await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
        GD.Print($"MICPROBE samples={samples} ({samples / AudioServer.GetMixRate():0.00} s) peak={peakDb:0.0} dBFS voiced_frames={voiced}");
        GetTree().Quit();
    }

    public void Show(Screen screen)
    {
        if (_stack.TryPeek(out var current))
        {
            current.Exit();
            _content.RemoveChild(current);
        }
        _stack.Push(screen);
        Attach(screen);
    }

    public void Back()
    {
        if (_stack.Count <= 1)
            return;
        var leaving = _stack.Pop();
        leaving.Exit();
        leaving.QueueFree();
        Attach(_stack.Peek());
    }

    private void Attach(Screen screen)
    {
        screen.SetAnchorsPreset(LayoutPreset.FullRect);
        _content.AddChild(screen);
        _title.Text = Ui.T(screen.Title);
        _back.Visible = _stack.Count > 1;
        screen.Enter();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_cancel"))
            Back();
    }

    // ---- smoke test: `-- --smoke` visits every screen and quits (scripts/test.sh) ----------

    private void StartSmoke()
    {
        foreach (var make in new System.Func<Screen>[] { () => new TunerScreen(), () => new TrainerScreen(), () => new CalibrationScreen(), () => new StudioScreen(), () => new SettingsScreen() })
            _smokeScreens.Enqueue(make);
        _smokeFrame = 0;
    }

    public override void _Process(double delta)
    {
        if (_smokeFrame < 0 || ++_smokeFrame % 15 != 0)
            return;
        if (_stack.Count > 1)
            Back();
        else if (_smokeScreens.Count > 0)
            Show(_smokeScreens.Dequeue()());
        else
        {
            GD.Print("SMOKE OK");
            GetTree().Quit();
        }
    }
}
