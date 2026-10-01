using Godot;
using Pipit.App.UI;
using Pipit.Core.Game;

namespace Pipit.App.Screens;

public partial class AchievementsScreen : Screen
{
    private readonly GridContainer _grid = new() { Columns = 3 };
    private readonly Label _summary;

    public override string Title => "Achievements";

    public AchievementsScreen()
    {
        _grid.AddThemeConstantOverride("h_separation", 14);
        _grid.AddThemeConstantOverride("v_separation", 14);
        _summary = Ui.Label("", 20, PipitTheme.InkSoft);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(Ui.VBox(14, _summary, _grid)));
        AddChild(scroll);
    }

    public override void Enter()
    {
        foreach (var child in _grid.GetChildren())
            child.QueueFree();
        var unlocked = S.Progress.Achievements;
        _summary.Text = string.Format(Ui.T("{0} of {1} unlocked"), unlocked.Count, Achievements.All.Count);
        foreach (var a in Achievements.All)
        {
            var got = unlocked.TryGetValue(a.Id, out var when);
            var card = Ui.MinSize(Ui.Card(Ui.HBox(12,
                Ui.MinSize(Ui.Label(a.Icon, 30, got ? PipitTheme.Accent : PipitTheme.InkSoft with { A = 0.5f }, HorizontalAlignment.Center), 60, 0),
                Ui.Expand(Ui.VBox(2,
                    Ui.Label(a.Title, 19, got ? PipitTheme.Ink : PipitTheme.InkSoft),
                    Ui.Text(a.Description, 14, PipitTheme.InkSoft),
                    Ui.Label(got ? string.Format(Ui.T("Unlocked {0:d MMM yyyy}"), when) : $"+{a.Xp} XP", 13, got ? PipitTheme.Good : PipitTheme.InkSoft))))), 380, 110);
            card.Modulate = got ? Colors.White : new Color(1, 1, 1, 0.7f);
            _grid.AddChild(card);
        }
    }
}
