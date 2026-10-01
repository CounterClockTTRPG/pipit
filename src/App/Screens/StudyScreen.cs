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
        _continue = Ui.Button("", () => { if (Current is { } step) Open(step); }, 24);
        _continue.CustomMinimumSize = new Vector2(0, 70);
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
        else if (S.Songs.Get(step.Id) is { } song)
            Main.Instance.Show(new PlayScreen(song, fromPath: true));
    }

    private static string StepTitle(PathStep step) => step.Kind == StepKind.Lesson
        ? S.LoadLesson(step.Id)?.Title(S.Settings.Language) ?? step.Id
        : S.Songs.Get(step.Id)?.Title(S.Settings.Language) ?? step.Id;

    public override void Enter()
    {
        var current = Current;
        _continue.Text = current is null
            ? Ui.T("You have finished the whole path! More chapters are coming.")
            : (current.Kind == StepKind.Lesson ? "📖  " : "♪  ") + Ui.T("Continue: ") + StepTitle(current);
        _continue.Disabled = current is null;

        foreach (var child in _list.GetChildren())
            child.QueueFree();
        foreach (var chapter in S.Study.Chapters)
        {
            _list.AddChild(Ui.Label(string.Format(Ui.T("Chapter {0}"), chapter.Number) + ": " + chapter.Title(S.Settings.Language), 26, PipitTheme.Accent));
            var grid = new GridContainer { Columns = 4 };
            grid.AddThemeConstantOverride("h_separation", 12);
            grid.AddThemeConstantOverride("v_separation", 12);
            foreach (var step in chapter.Steps)
            {
                var state = S.Study.StateOf(step, S.Progress, Kind);
                var icon = state switch { StepState.Done => "✓", StepState.Current => "▶", _ => "🔒" };
                var kind = step.Kind == StepKind.Lesson ? Ui.T("Lesson") : Ui.T("Song");
                var s = step;
                var button = Ui.Button($"{icon}  {StepTitle(step)}\n{kind}", () => Open(s), 17);
                button.CustomMinimumSize = new Vector2(270, 74);
                button.Disabled = state == StepState.Locked;
                if (state == StepState.Current)
                    button.AddThemeStyleboxOverride("normal", PipitTheme.Box(new Color("f6dcb4"), PipitTheme.Accent));
                if (state == StepState.Done)
                    button.AddThemeColorOverride("font_color", PipitTheme.Good);
                grid.AddChild(button);
            }
            _list.AddChild(grid);
        }
    }
}
