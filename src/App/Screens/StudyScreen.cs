using System.Linq;
using Godot;
using Pipit.App.UI;
using Pipit.Core.Lessons;

namespace Pipit.App.Screens;

/// <summary>The study path: chapters of lessons and songs, done one after the other.</summary>
public partial class StudyScreen : Screen
{
    private readonly VBoxContainer _list = Ui.VBox(14);
    private readonly Button _continue;

    public override string Title => "Study";

    public StudyScreen()
    {
        _continue = Ui.Primary(Ui.Button("", () => { if (Current is { } step) Open(step); }, PipitTheme.Lead));
        _continue.CustomMinimumSize = new Vector2(0, 64);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(Ui.VBox(18, _continue, _list)));
        AddChild(scroll);
    }

    private static string Kind => S.Instrument.Kind.ToString();
    private static PathStep? Current => S.Study.Current(S.Progress, Kind);

    /// <summary>Opens a step: a lesson page, or the song in the play screen.</summary>
    public static void Open(PathStep step)
    {
        if (step.Kind == StepKind.Lesson)
        {
            if (S.LoadLesson(step.Id) is { } lesson)
                Main.Instance.Show(new LessonScreen(lesson));
        }
        else if (step.Kind == StepKind.Review)
        {
            if (Lessons.ReviewLessons.Make(step) is { } review)
                Main.Instance.Show(new LessonScreen(review, step, () => Lessons.ReviewLessons.Make(step)));
        }
        else if (S.Songs.Get(step.Id) is { } song)
            Main.Instance.Show(new PlayScreen(song, fromPath: true));
    }

    public static string StepTitle(PathStep step) => step.Kind switch
    {
        StepKind.Lesson => S.LoadLesson(step.Id)?.Title(S.Settings.Language) ?? step.Id,
        StepKind.Review => string.Format(Ui.T("Review of chapter {0}"), step.Id),
        _ => S.Songs.Get(step.Id)?.Title(S.Settings.Language) ?? step.Id,
    };

    public override void Enter()
    {
        var current = Current;
        _continue.Text = current is null
            ? Ui.T("You have finished the whole path! More chapters are coming.")
            : "▶  " + Ui.T("Continue: ") + StepTitle(current);
        _continue.Disabled = current is null;

        foreach (var child in _list.GetChildren())
            child.QueueFree();
        foreach (var chapter in S.Study.Chapters)
        {
            var done = chapter.Steps.Count(step => S.Study.IsDone(step, S.Progress, Kind));
            var heading = Ui.Label(chapter.Title(S.Settings.Language), PipitTheme.Heading, done == chapter.Steps.Count ? PipitTheme.Good : PipitTheme.Ink);
            heading.AddThemeFontOverride("font", PipitTheme.BoldFont);
            _list.AddChild(Ui.VBox(0, heading,
                Ui.Label(string.Format(Ui.T("Chapter {0}: {1} of {2} done"), chapter.Number, done, chapter.Steps.Count), PipitTheme.Small + 1, PipitTheme.InkSoft)));
            var grid = new HFlowContainer();
            grid.AddThemeConstantOverride("h_separation", 12);
            grid.AddThemeConstantOverride("v_separation", 12);
            foreach (var step in chapter.Steps)
            {
                var state = S.Study.StateOf(step, S.Progress, Kind) switch
                {
                    StepState.Done => Ui.TileState.Done, StepState.Current => Ui.TileState.Current, _ => Ui.TileState.Locked,
                };
                var kind = step.Kind switch { StepKind.Lesson => Ui.T("Lesson"), StepKind.Review => Ui.T("Review: what you have learned"), _ => Ui.T("Song") };
                var s = step;
                grid.AddChild(Ui.Tile(StepTitle(step), (state == Ui.TileState.Locked ? "🔒 " : "") + kind, () => Open(s), 262, 80, state, translate: false));
            }
            _list.AddChild(grid);
        }
    }
}
