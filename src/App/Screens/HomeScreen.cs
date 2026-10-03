using Godot;
using Pipit.App.UI;
using Pipit.App.Views;

namespace Pipit.App.Screens;

public partial class HomeScreen : Screen
{
    private readonly Label _instrument;
    private readonly PipView _pip = new() { CustomMinimumSize = new Vector2(170, 170) };
    private readonly PipView _playerBird = new() { CustomMinimumSize = new Vector2(64, 64) };
    private double _idle;
    private readonly OptionButton _picker = new();
    private readonly Label _level;
    private readonly ProgressBar _xpBar = new() { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(260, 16) };
    private readonly Label _xpText;
    private readonly Label _hint;

    public override string Title => "Home";
    public override bool ShowTitle => false;

    private readonly Label _greeting = Ui.Label("", PipitTheme.Display, PipitTheme.Ink);
    private readonly Button _continue;

    public HomeScreen()
    {
        _greeting.AddThemeFontOverride("font", PipitTheme.BoldFont);
        _instrument = Ui.Label("", PipitTheme.Small + 1, PipitTheme.InkSoft);
        _picker.ItemSelected += ix => { S.SetActiveInstrument((string)_picker.GetItemMetadata((int)ix)); Enter(); };
        _level = Ui.Label("", PipitTheme.Lead, PipitTheme.Accent);
        _level.AddThemeFontOverride("font", PipitTheme.BoldFont);
        _xpText = Ui.Label("", PipitTheme.Small, PipitTheme.InkSoft);
        var switchPlayer = Ui.Quiet(Ui.Button("Switch player", () => Main.Instance.ShowRoot(new ProfileScreen()), PipitTheme.Small + 1));
        _hint = Ui.Label("", PipitTheme.Small + 1, PipitTheme.Bad);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        // The one big thing on Home: straight into the next step of the study path.
        _continue = Ui.Primary(Ui.Button("", () => { if (S.Study.Current(S.Progress, S.Instrument.Kind.ToString()) is { } step) StudyScreen.Open(step); }, PipitTheme.Lead));
        _continue.CustomMinimumSize = new Vector2(0, 64);
        _continue.Alignment = HorizontalAlignment.Left;

        var hero = Ui.HBox(28,
            Ui.VBox(0, Ui.Spacer(), _pip, Ui.Spacer()),
            Ui.Expand(Ui.VBox(10,
                Ui.HBox(12, _greeting, Ui.Spacer(), Ui.VBox(0, switchPlayer, Ui.Spacer())),
                Ui.HBox(12, _playerBird, Ui.VBox(4, Ui.Spacer(), Ui.HBox(12, _level, _xpBar), _xpText, Ui.Spacer())),
                _continue,
                Ui.HBox(10, Ui.Label("Recorder:", PipitTheme.Body, PipitTheme.InkSoft), _picker, _instrument),
                _hint)));
        _playerBird.CustomMinimumSize = new Vector2(52, 52);
        _pip.CustomMinimumSize = new Vector2(210, 210);

        // Practice tiles, then the less frequent places in a quieter row. Tiles flow and wrap
        // their text, so longer translations cannot widen the page.
        var tiles = new HFlowContainer();
        tiles.AddThemeConstantOverride("h_separation", 14);
        tiles.AddThemeConstantOverride("v_separation", 14);
        void Tile(string text, string sub, System.Func<Screen> make) => tiles.AddChild(Ui.Tile(text, sub, () => Main.Instance.Show(make()), 206, 104));
        Tile("Study path", "Learn step by step, with Pip", () => new StudyScreen());
        Tile("Songs", "Play along, or at your own pace", () => new LibraryScreen());
        Tile("Fingering trainer", "Show me a note, I play it", () => new TrainerScreen());
        Tile("Note reading", "Read the notes, name them on the beat", () => new NoteReadingScreen());
        tiles.AddChild(Ui.Tile("Review", "Practise what you have learned", () =>
        {
            if (Lessons.ReviewLessons.Make(null) is { } review)
                Main.Instance.Show(new LessonScreen(review, null, () => Lessons.ReviewLessons.Make(null)));
            else
                _hint.Text = Ui.T("Learn your first notes on the study path, then come back to review them.");
        }, 206, 104));
        var more = Ui.HBox(4);
        foreach (var (text, make) in new (string, System.Func<Screen>)[]
                 {
                     ("Tuner", () => new TunerScreen()), ("Studio", () => new StudioScreen()), ("Achievements", () => new AchievementsScreen()),
                     ("Calibrate", () => new CalibrationScreen()), ("Settings", () => new SettingsScreen()),
                 })
            more.AddChild(Ui.Quiet(Ui.Button(text, () => Main.Instance.Show(make()), PipitTheme.Body)));

        var page = Ui.VBox(26, hero, tiles, more);
        page.CustomMinimumSize = new Vector2(1120, 0);
        var center = new CenterContainer();
        center.AddChild(page);
        AddChild(center);
    }

    public override void _Process(double delta)
    {
        _idle += delta;
        if (_idle > 60 && _pip.Mood != PipMood.Dozing)
            _pip.SetMood(PipMood.Dozing);
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseMotion or InputEventKey or InputEventMouseButton)
        {
            _idle = 0;
            if (_pip.Mood == PipMood.Dozing)
                _pip.SetMood(PipMood.Cheering, 1.2);
        }
    }

    public override void Enter()
    {
        _pip.SetMood(PipMood.Singing, 2.0);
        _playerBird.Avatar = S.Player.Avatar;
        _greeting.Text = string.Format(Ui.T("Hi {0}!"), S.Player.Name);
        var step = S.Study.Current(S.Progress, S.Instrument.Kind.ToString());
        _continue.Text = step is null
            ? "  " + Ui.T("You have finished the whole path! More chapters are coming.")
            : "  ▶  " + Ui.T("Continue: ") + StudyScreen.StepTitle(step);
        _continue.Disabled = step is null;
        var i = S.Instrument;
        _picker.Clear();
        foreach (var each in S.Settings.Instruments)
        {
            _picker.AddItem(each.Name);
            _picker.SetItemMetadata(_picker.ItemCount - 1, each.Id);
            if (each.Id == i.Id)
                _picker.Select(_picker.ItemCount - 1);
        }
        _instrument.Text = $"{Ui.T(i.Kind.ToString())} · {Ui.T(i.Fingering.ToString())} · A = {i.ReferenceA:0} Hz";
        var progress = S.Progress;
        var next = Pipit.Core.Game.Levels.Threshold(progress.Level + 1);
        _level.Text = string.Format(Ui.T("Level {0}"), progress.Level);
        _xpBar.Value = Pipit.Core.Game.Levels.Fraction(progress.Xp);
        _xpText.Text = $"{progress.Xp} / {next} XP" + (progress.StreakDays > 1 && progress.LastPlayDay >= System.DateOnly.FromDateTime(System.DateTime.Now.AddDays(-1))
            ? "     " + string.Format(Ui.T("{0} days in a row"), progress.StreakDays) : "");
        _hint.Text = S.DeviceGate is null || S.DeviceLatencyMs is null
            ? Ui.T("Not calibrated for this microphone yet — open Calibrate first.")
            : "";
    }
}
