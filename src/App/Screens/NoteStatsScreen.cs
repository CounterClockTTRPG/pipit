using System.Linq;
using Godot;
using Pipit.App.UI;
using Pipit.Core.Game;

namespace Pipit.App.Screens;

/// <summary>
/// My notes: how well each note is known on this recorder, from every answer in the fingering
/// trainer, the lessons and the songs. A five-step bar per note shows its box (see
/// <see cref="NoteStat"/>); the top line counts the notes known reliably.
/// </summary>
public partial class NoteStatsScreen : Screen
{
    private readonly VBoxContainer _page = Ui.VBox(18);

    public override string Title => "My notes";

    public NoteStatsScreen()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(_page));
        AddChild(scroll);
    }

    public override void Enter()
    {
        foreach (var child in _page.GetChildren())
            child.QueueFree();
        var kind = S.Instrument.Kind.ToString();
        var order = TrainerScreen.TeachingOrder();
        NoteStat? Stat(int n) => S.Progress.FindNote(PlayerProgress.Playing, kind, n);
        var known = order.Count(n => Stat(n)?.Box >= 4);
        var mastery = order.Sum(n => (double)(Stat(n)?.Box ?? 0)) / (NoteStat.TopBox * order.Count);

        _page.AddChild(Ui.Heading(string.Format(Ui.T("You know {0} of {1} fingerings"), known, order.Count), PipitTheme.Heading));
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = mastery, ShowPercentage = false, CustomMinimumSize = new Vector2(420, 14) };
        _page.AddChild(Ui.HBox(12, bar, Ui.Label(string.Format(Ui.T("{0:0}% of the way"), mastery * 100), PipitTheme.Body, PipitTheme.InkSoft)));
        _page.AddChild(Ui.Text("Each bar fills as you find a note right, time after time; a miss sends it back to the start. Practise in the fingering trainer's learning mode: Pip asks the notes in the low bars more often.", PipitTheme.Small + 1, PipitTheme.InkSoft));

        var grid = new HFlowContainer();
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        foreach (var note in order)
            grid.AddChild(Card(note, Stat(note)));
        _page.AddChild(grid);
    }

    private static Control Card(int writtenMidi, NoteStat? stat)
    {
        var seen = stat is { Played: > 0 };
        var name = Ui.Label(S.NoteName(writtenMidi), PipitTheme.Lead, seen ? PipitTheme.Ink : PipitTheme.InkSoft with { A = 0.6f });
        name.AddThemeFontOverride("font", PipitTheme.BoldFont);
        var boxes = Ui.HBox(3);
        var box = stat?.Box ?? 0;
        var fill = box >= 4 ? PipitTheme.Good : box >= 2 ? PipitTheme.Sky : PipitTheme.Warn;
        for (var i = 1; i <= NoteStat.TopBox; i++)
            boxes.AddChild(new ColorRect { Color = i <= box ? fill : PipitTheme.PaperDark, CustomMinimumSize = new Vector2(22, 8) });
        var detail = !seen ? Ui.T("not played yet")
            : string.Format(Ui.T("{0} played, {1} missed"), stat!.Played, stat.Missed)
              + (stat.AverageSeconds > 0 ? "\n" + string.Format(Ui.T("found in {0:0.0} s"), stat.AverageSeconds) : "");
        var card = new PanelContainer { CustomMinimumSize = new Vector2(150, 0) };
        card.AddThemeStyleboxOverride("panel", PipitTheme.Box(seen ? Colors.White : PipitTheme.Paper, PipitTheme.PaperDark, 10, radius: 12, line: 1));
        card.AddChild(Ui.VBox(6, name, boxes, Ui.Label(detail, PipitTheme.Small, PipitTheme.InkSoft)));
        return card;
    }
}
