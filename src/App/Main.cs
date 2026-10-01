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
        Show(new AchievementsScreen());
        for (var i = 0; i < 20; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "achievements.png"));
        Back();

        // Pip in every mood, large, after the moods have settled and animated a little.
        var pipSheet = new ColorRect { Color = PipitTheme.Paper };
        pipSheet.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(pipSheet);
        var pips = new HBoxContainer();
        pips.SetAnchorsPreset(LayoutPreset.FullRect);
        pipSheet.AddChild(pips);
        var moods = System.Enum.GetValues<Views.PipMood>();
        foreach (var mood in moods)
        {
            var pip = new Views.PipView { CustomMinimumSize = new Vector2(210, 210) };
            pip.SetMood(mood);
            var cell = new PanelContainer();
            cell.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = PipitTheme.Paper });
            cell.AddChild(Ui.VBox(4, pip, Ui.Label(mood.ToString(), 18, PipitTheme.Ink, HorizontalAlignment.Center)));
            pips.AddChild(cell);
        }
        await ToSignal(GetTree().CreateTimer(1.6), SceneTreeTimer.SignalName.Timeout);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "pip-moods.png"));
        pipSheet.QueueFree();

        // The study path and two lesson pages (top, and scrolled to an exercise).
        Show(new StudyScreen());
        for (var i = 0; i < 20; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "study.png"));
        Back();
        foreach (var id in new[] { "meet-the-recorder", "si-and-la", "the-staff" })
        {
            var lessonScreen = new LessonScreen(Services.Instance.LoadLesson(id)!);
            Show(lessonScreen);
            for (var i = 0; i < 20; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, $"lesson-{id}.png"));
            lessonScreen.DebugScrollToEnd();
            for (var i = 0; i < 10; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, $"lesson-{id}-end.png"));
            Back();
        }

        // Songs: the library, a song before starting, and during "Listen".
        Show(new LibraryScreen());
        for (var i = 0; i < 20; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "library.png"));
        Back();
        var play = new PlayScreen(Services.Instance.Songs.Get("ode-to-joy")!);
        Show(play);
        for (var i = 0; i < 20; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "play-ready.png"));
        play.DebugListen();
        await ToSignal(GetTree().CreateTimer(3.2), SceneTreeTimer.SignalName.Timeout);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "play-listen.png"));
        Back();

        // The score view for songs covering different notation.
        foreach (var id in new[] { "ode-to-joy", "jingle-bells", "minuet-in-g-minor", "bach-prelude-opening", "musette", "greensleeves", "amazing-grace", "bransle-champaigne-diminutions", "ductia" })
        {
            var scoreScreen = new PlayScreen(Services.Instance.Songs.Get(id)!);
            Show(scoreScreen);
            scoreScreen.DebugShowScore();
            for (var i = 0; i < 25; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, $"score-{id}.png"));
            if (id == "minuet-in-g-minor")
            {
                // The cursor and highlighted note while listening, a few bars in.
                scoreScreen.DebugListen();
                await ToSignal(GetTree().CreateTimer(6.5), SceneTreeTimer.SignalName.Timeout);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "score-listen.png"));
            }
            Back();
        }

        // The Studio conductor during a held note and during the rest after it.
        var studio = new StudioScreen();
        Show(studio);
        studio.DebugStartGuided("long-tones-low");
        var t0 = Services.Instance.Audio.Now;
        foreach (var (at, file) in new[] { (5.8, "studio-note.png"), (7.7, "studio-rest.png") })
        {
            while (Services.Instance.Audio.Now - t0 < at)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, file));
        }
        Back();

        // A sheet of characteristic fingerings: all covered, forked Fa, Sol, a half hole, thumb off, pinched thumb.
        var sheet = new HBoxContainer();
        sheet.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(sheet);
        foreach (var step in new[] { 0, 3, 5, 7, 14, 16 })
        {
            var d = new Views.FingeringDiagram { Fingering = Services.Instance.Chart.Primary(step), CustomMinimumSize = new Vector2(210, 520) };
            var label = Ui.Label(Services.Instance.NoteName(Services.Instance.Recorder.BottomWrittenMidi + step), 28, PipitTheme.Ink, HorizontalAlignment.Center);
            sheet.AddChild(Ui.VBox(4, label, d));
        }
        for (var i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "fingerings.png"));
        sheet.QueueFree();
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
        else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 })
            Services.Instance.SetFullscreen(!Services.Instance.Settings.Fullscreen);
    }

    // ---- smoke test: `-- --smoke` visits every screen and quits (scripts/test.sh) ----------

    private void StartSmoke()
    {
        foreach (var make in new System.Func<Screen>[] { () => new TunerScreen(), () => new TrainerScreen(), () => new CalibrationScreen(), () => new StudioScreen(), () => new SettingsScreen(), () => new LibraryScreen(), () => new PlayScreen(Services.Instance.Songs.Songs[0]), () => new AchievementsScreen(), () => new StudyScreen(), () => new LessonScreen(Services.Instance.LoadLesson("meet-the-recorder")!), () => new LessonScreen(Services.Instance.LoadLesson("si-and-la")!) })
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
