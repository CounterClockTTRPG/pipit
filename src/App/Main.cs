using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.Lessons;
using Pipit.App.Screens;
using Pipit.App.UI;
using Pipit.Core.Analysis;
using Pipit.Core.Practice;

namespace Pipit.App;

/// <summary>Root of the app: theme, top bar with a back button, and a stack of screens.</summary>
public partial class Main : Control
{
    private readonly Stack<Screen> _stack = new();
    private Control _content = null!;
    private Label _title = null!;
    private Button _back = null!;
    private PanelContainer _bar = null!;
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

        var bar = _bar = new PanelContainer();
        // A quiet header: the page title, a text-only back button, a thin wooden rule under it.
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = PipitTheme.Paper, BorderColor = PipitTheme.Wood, BorderWidthBottom = 2,
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        _back = Ui.Quiet(Ui.Button("← Back", Back, PipitTheme.Body));
        _title = Ui.Label("", PipitTheme.Lead, PipitTheme.Ink);
        _title.AddThemeFontOverride("font", PipitTheme.BoldFont);
        _title.CustomMinimumSize = new Vector2(0, 44);
        _title.VerticalAlignment = VerticalAlignment.Center;
        bar.AddChild(Ui.HBox(16, _back, _title));
        layout.AddChild(bar);

        _content = Ui.Expand(new Control(), true, true);
        layout.AddChild(_content);

        // The microphone level, in the top-right corner of every screen.
        var mic = new Views.MicIndicator();
        mic.SetAnchorsPreset(LayoutPreset.TopRight);
        mic.Position = new Vector2(-mic.CustomMinimumSize.X - 18, 10);
        AddChild(mic);

        // Normally the app starts by asking who is playing; the test and screenshot runs go
        // straight to Home as the last player.
        var args = OS.GetCmdlineUserArgs();
        var diagnostic = args.Any(a => a is "--smoke" or "--shots" or "--micprobe" or "--miccheck" or "--loopback" || a.StartsWith("--lessonprobe=") || a.StartsWith("--speechprobe=") || a == "--icon");
        Show(diagnostic ? new HomeScreen() : new ProfileScreen());

        if (OS.GetCmdlineUserArgs().Contains("--smoke"))
            StartSmoke();
        if (OS.GetCmdlineUserArgs().Contains("--micprobe"))
            MicProbe();
        if (OS.GetCmdlineUserArgs().Contains("--shots"))
        {
            Services.Instance.ReadOnly = true;
            // `--lang=nl`: shoot in Dutch (not saved) and list texts shown untranslated.
            if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--lang=")) is { } lang)
            {
                Services.Instance.Settings.Language = lang["--lang=".Length..];
                Services.Instance.Machine.Language = lang["--lang=".Length..];
                Services.Instance.ApplyLanguage();
                ShowRoot(new HomeScreen());
            }
            Screenshots();
        }
        if (OS.GetCmdlineUserArgs().Contains("--miccheck"))
            MicCheckAndQuit();
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--lessonprobe=")) is { } probe)
            LessonProbe(probe["--lessonprobe=".Length..]);
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--speechprobe=")) is { } speech)
            SpeechProbe(speech["--speechprobe=".Length..]);
        if (OS.GetCmdlineUserArgs().Contains("--icon"))
            RenderIcon();
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
        var screens = new (string, System.Func<Screen>?)[] { ("home", null), ("tuner", () => new TunerScreen()), ("trainer", () => new TrainerScreen()), ("my-notes", () => new NoteStatsScreen()), ("note-reading", () => new NoteReadingScreen()), ("review", () => new LessonScreen(Lessons.ReviewLessons.Make(new Core.Lessons.PathStep(Core.Lessons.StepKind.Review, "4"))!)),
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
        var reading = new NoteReadingScreen();
        Show(reading);
        for (var i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        reading.DebugResults();
        for (var i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "note-reading-results.png"));
        Back();
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
        var library = (LibraryScreen)_stack.Peek();
        GD.Print("LIBRARY " + library.DebugScrollToEnd());
        for (var i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "library-end.png"));
        library.DebugScrollToRepertoire();
        for (var i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "library-repertoire.png"));
        // Coming back from a song with a search on keeps the view (the box is reused, not rebuilt).
        library.DebugSearch("ultima");
        Show(new PlayScreen(Services.Instance.Songs.Get("ultima5-stones")!));
        for (var i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Back();
        for (var i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        library.DebugScrollToRepertoire();
        for (var i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "library-return.png"));
        library.DebugSearch("");
        // Adding a MusicXML song: the report after the import.
        var sample = System.Environment.GetEnvironmentVariable("USERPROFILE") is { } home ? System.IO.Path.Combine(home, "Downloads", "druid-ii-enlightenment-david-hanlon.mxl") : "";
        if (System.IO.File.Exists(sample))
        {
            library.DebugImport(sample);
            for (var i = 0; i < 10; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "library-import.png"));
        }
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

        // Players: who is playing, a new player, and the first page of Pip's tour.
        // A second player (in memory only: --shots writes nothing) so the crown shows.
        if (Services.Instance.Players.Count == 1)
            Services.Instance.Players.Add(new Core.Profiles.PlayerProfile { Name = "Mia", Avatar = "robin" });
        foreach (var (make, file) in new (System.Func<Screen>, string)[] { (() => new ProfileScreen(), "players"), (() => new NewPlayerScreen(null), "new-player"), (() => new IntroScreen(), "intro") })
        {
            Show(make());
            for (var i = 0; i < 25; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, file + ".png"));
            Back();
        }

        // A round: part picker and band switch.
        var round = new PlayScreen(Services.Instance.Songs.Get("frere-jacques")!, part: 1);
        Show(round);
        for (var i = 0; i < 20; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "play-round.png"));
        Back();

        // The highway while listening.
        var highway = new PlayScreen(Services.Instance.Songs.Get("ode-to-joy")!);
        Show(highway);
        highway.DebugShowHighway();
        highway.DebugListen();
        await ToSignal(GetTree().CreateTimer(4.0), SceneTreeTimer.SignalName.Timeout);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "highway.png"));
        Back();

        // "Don't know?" recorder size: Pip hears the lowest note.
        var settings = new SettingsScreen();
        Show(settings);
        for (var i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        settings.Editor.DebugDetect();
        for (var i = 0; i < 5; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "settings-size.png"));
        Back();

        // Survival callouts: a grade, the streak and a milestone over the highway.
        var survival = new PlayScreen(Services.Instance.Songs.Get("ode-to-joy")!);
        Show(survival);
        survival.DebugShowHighway();
        for (var i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        survival.DebugSurvivalCallouts();
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "survival-callouts.png"));
        Back();

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
        System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "untranslated.txt"), Ui.Missing);
        GD.Print($"SHOTS {dir} ({Ui.Missing.Count} untranslated)");
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

    /// <summary>Replaces every open screen with <paramref name="screen"/> (after choosing a player).</summary>
    public void ShowRoot(Screen screen)
    {
        while (_stack.Count > 0)
        {
            var leaving = _stack.Pop();
            leaving.Exit();
            leaving.QueueFree();
        }
        foreach (var child in _content.GetChildren())
            _content.RemoveChild(child);
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
        _title.Text = screen.ShowTitle ? Ui.T(screen.Title) : "";
        _back.Visible = _stack.Count > 1;
        _bar.Visible = screen.ShowTitle || _back.Visible;
        screen.Enter();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_cancel"))
            Back();
        else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 })
            Services.Instance.SetFullscreen(!Services.Instance.Machine.Fullscreen);
    }

    // ---- lesson probe: `-- --lessonprobe=high-do` plays each "Play this note" target into the
    // lesson as made-up frames (no microphone) and prints whether every box completed. --------

    private async void LessonProbe(string id)
    {
        if (Services.Instance.LoadLesson(id) is not { } lesson)
        {
            GD.PrintErr($"PROBE: no lesson {id}");
            GetTree().Quit(1);
            return;
        }
        Show(new LessonScreen(lesson));
        for (var i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        static IEnumerable<Node> All(Node n) => n.GetChildren().SelectMany(c => All(c).Prepend(c));
        var blocks = All(this).OfType<TryBlock>().ToList();
        var ok = blocks.Count > 0;
        var time = Services.Instance.Audio.Now;
        foreach (var block in blocks)
        {
            // 1.6 s of a note played like a sharp recorder: the calibrated offset plus 30 cents,
            // wobbling ±10 cents, at the tracker's frame rate; then silence.
            var sharp = Services.Instance.Instrument.MeasuredOffsetCents / 100 + 0.3;
            for (var t = 0.0; t < 1.6; t += 256.0 / 48000)
            {
                var midi = block.Target + sharp + 0.1 * Math.Sin(t * 2 * Math.PI * 5);
                Services.Instance.Audio.InjectFrame(new PitchFrame(time += 256.0 / 48000, 440 * Math.Pow(2, (midi - 69) / 12), 0.95, 0.05, true, midi, midi));
            }
            for (var t = 0.0; t < 0.3; t += 256.0 / 48000)
                Services.Instance.Audio.InjectFrame(new PitchFrame(time += 256.0 / 48000, 0, 0, 0, false, double.NaN, double.NaN));
            GD.Print($"PROBE try {block.Target}: {(block.Done ? "done" : "NOT DONE")}");
            ok &= block.Done;
        }
        GD.Print(ok ? $"PROBE OK ({blocks.Count} tries)" : $"PROBE FAILED ({blocks.Count} tries)");
        GetTree().Quit(ok ? 0 : 1);
    }

    // ---- `-- --icon`: draws Pip on a round paper badge into user://icon.png (needs a window) ----

    private async void RenderIcon()
    {
        const int size = 512;
        var viewport = new SubViewport { Size = new Vector2I(size, size), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var badge = new Panel { Size = new Vector2(size, size) };
        var round = new StyleBoxFlat { BgColor = PipitTheme.Paper, BorderColor = PipitTheme.Accent, AntiAliasing = true };
        round.SetCornerRadiusAll(size / 2);
        round.SetBorderWidthAll(18);
        badge.AddThemeStyleboxOverride("panel", round);
        viewport.AddChild(badge);
        var pip = new Views.PipView { Position = new Vector2(size * 0.1f, size * 0.12f), Size = new Vector2(size * 0.8f, size * 0.8f) };
        viewport.AddChild(pip);
        AddChild(viewport);
        pip.SetMood(Views.PipMood.Singing, 0);
        for (var i = 0; i < 20; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var path = ProjectSettings.GlobalizePath("user://icon.png");
        viewport.GetTexture().GetImage().SavePng(path);
        GD.Print("ICON " + path);
        GetTree().Quit();
    }

    // ---- speech probe: `-- --speechprobe=file.wav[,letters]` plays a recording (16-bit mono)
    // into the speech listener as if from the microphone and prints the words and times heard.

    private async void SpeechProbe(string arg)
    {
        try
        {
            await SpeechProbeRun(arg);
        }
        catch (Exception e)
        {
            GD.PrintErr("PROBE failed: " + e.Message);
            GetTree().Quit(1);
        }
    }

    private async System.Threading.Tasks.Task SpeechProbeRun(string arg)
    {
        var parts = arg.Split(',');
        var letters = parts.Length > 1 && parts[1] == "letters";
        var (samples, rate) = Core.Recording.WavFile.Read(parts[0]);
        var hub = Services.Instance.Audio;
        // Up to the hub's rate, so the listener's resampling is exercised too.
        var factor = hub.SampleRate / (double)rate;
        var up = new float[(int)(samples.Length * factor)];
        for (var i = 0; i < up.Length; i++)
            up[i] = samples[Math.Min(samples.Length - 1, (int)(i / factor))];
        var listener = await Audio.SpeechListener.Create(hub, "en", NoteWords.Grammar(letters));
        if (listener is null)
        {
            GD.PrintErr("PROBE: no English speech model (run scripts/fetch-speech-models.sh)");
            GetTree().Quit(1);
            return;
        }
        // Two sessions on one listener, as the Test button and a round do: the second session's
        // times must count from its own start.
        var words = new List<(string Word, double Time)>();
        var start = 100.0;
        for (var session = 0; session < 2; session++)
        {
            listener.Start();
            start = 100.0 + session * 50;
            for (var i = 0; i < up.Length; i += 1024)
                hub.InjectSamples(up[i..Math.Min(up.Length, i + 1024)], start + i / (double)hub.SampleRate);
            words = listener.Stop();
        }
        GD.Print("PROBE heard: " + string.Join("  ", words.Select(w => $"{w.Word}@{w.Time - start:0.00}")));
        GD.Print("PROBE letters: " + string.Join(" ", words.Select(w => NoteWords.LetterFor(w.Word, letters)?.ToString() ?? "-")));
        GetTree().Quit();
    }

    // ---- smoke test: `-- --smoke` visits every screen and quits (scripts/test.sh) ----------

    private void StartSmoke()
    {
        foreach (var make in new System.Func<Screen>[] { () => new TunerScreen(), () => new TrainerScreen(), () => new CalibrationScreen(), () => new StudioScreen(), () => new SettingsScreen(), () => new LibraryScreen(), () => new PlayScreen(Services.Instance.Songs.Songs[0]), () => new AchievementsScreen(), () => new ProfileScreen(), () => new NewPlayerScreen(null), () => new IntroScreen(), () => new StudyScreen(), () => new LessonScreen(Services.Instance.LoadLesson("meet-the-recorder")!), () => new LessonScreen(Services.Instance.LoadLesson("si-and-la")!), () => new NoteStatsScreen(), () => new NoteReadingScreen(),
                     () => new LessonScreen(Lessons.ReviewLessons.Make(new Core.Lessons.PathStep(Core.Lessons.StepKind.Review, "4"))!) })
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
