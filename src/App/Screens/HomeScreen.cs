using Godot;
using Pipit.App.UI;

namespace Pipit.App.Screens;

public partial class HomeScreen : Screen
{
    private readonly Label _instrument;
    private readonly OptionButton _picker = new();
    private readonly Label _level;
    private readonly ProgressBar _xpBar = new() { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(260, 16) };
    private readonly Label _xpText;
    private readonly Label _hint;

    public override string Title => "Home";

    public HomeScreen()
    {
        var title = Ui.Label("Pipit", 88, PipitTheme.Accent, HorizontalAlignment.Center);
        var tagline = Ui.Label("Learn the recorder, one note at a time", 24, PipitTheme.InkSoft, HorizontalAlignment.Center);
        _instrument = Ui.Label("", 18, PipitTheme.InkSoft);
        _picker.ItemSelected += ix => { S.SetActiveInstrument((string)_picker.GetItemMetadata((int)ix)); Enter(); };
        _level = Ui.Label("", 22, PipitTheme.Accent);
        _xpText = Ui.Label("", 16, PipitTheme.InkSoft);
        var levelRow = new CenterContainer();
        levelRow.AddChild(Ui.HBox(12, _level, _xpBar, _xpText));
        var instrumentRow = new CenterContainer();
        instrumentRow.AddChild(Ui.HBox(12, Ui.Label("Recorder:", 20), _picker, _instrument));
        _hint = Ui.Label("", 18, PipitTheme.Bad, HorizontalAlignment.Center);

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 16);
        void Tile(string text, string sub, System.Func<Screen> make)
        {
            var b = Ui.Button(text + "\n" + Ui.T(sub), () => Main.Instance.Show(make()), 22);
            b.CustomMinimumSize = new Vector2(300, 110);
            grid.AddChild(b);
        }
        Tile("Study", "Learn step by step, with Pip", () => new StudyScreen());
        Tile("Songs", "Play along, or at your own pace", () => new LibraryScreen());
        Tile("Tuner", "What note am I playing?", () => new TunerScreen());
        Tile("Fingering trainer", "Show me a note, I play it", () => new TrainerScreen());
        Tile("Calibrate", "Teach Pipit your room and recorder", () => new CalibrationScreen());
        Tile("Studio", "Record and listen back", () => new StudioScreen());
        Tile("Achievements", "What you have unlocked", () => new AchievementsScreen());
        Tile("Settings", "Microphone, recorder, names", () => new SettingsScreen());

        var center = new CenterContainer();
        center.AddChild(Ui.VBox(18, title, tagline, levelRow, instrumentRow, _hint, grid));
        AddChild(center);
    }

    public override void Enter()
    {
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
            ? "  ·  " + string.Format(Ui.T("{0} days in a row"), progress.StreakDays) : "");
        _hint.Text = S.DeviceGate is null || S.DeviceLatencyMs is null
            ? Ui.T("Not calibrated for this microphone yet — open Calibrate first.")
            : "";
    }
}
