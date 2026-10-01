using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.Lessons;
using Pipit.App.UI;
using Pipit.Core.Analysis;
using Pipit.Core.Game;
using Pipit.Core.Lessons;

namespace Pipit.App.Screens;

/// <summary>One lesson page: text and pictures, with tasks (try, exercise, quiz) to complete.</summary>
public partial class LessonScreen : Screen
{
    private readonly Lesson _lesson;
    private readonly LessonContext _context;
    private readonly List<ITaskBlock> _tasks = new();
    private readonly Label _progress;
    private readonly Button _finish, _next;
    private readonly AudioStreamPlayer _player = new();
    private readonly ScrollContainer _scroll;

    public override string Title => _lesson.Title(S.Settings.Language);

    public LessonScreen(Lesson lesson)
    {
        _lesson = lesson;
        _context = new LessonContext { Play = Play };
        var page = Ui.VBox(18);
        foreach (var block in lesson.Page(S.Settings.Language))
        {
            var control = LessonBlocks.Build(block, _context);
            page.AddChild(control);
            if (control is ITaskBlock task)
            {
                _tasks.Add(task);
                task.Completed += UpdateProgress;
            }
            // "after=task": shown only once the task before it is done (praise, "now try...").
            else if (block is DirectiveBlock { Name: "pip" } pip && pip.Arg("after") == "task" && _tasks.Count > 0)
            {
                var previous = _tasks[^1];
                control.SetMeta("praise", true);
                control.Visible = previous.Done;
                previous.Completed += () => control.Visible = true;
            }
        }
        _progress = Ui.Label("", 18, PipitTheme.InkSoft);
        _finish = Ui.Button("✓ Finish lesson", Finish, 20);
        _next = Ui.Button("Next step →", Next, 20);
        _next.Visible = false;
        page.AddChild(Ui.HBox(16, _progress, Ui.Spacer(), _finish, _next));

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right" })
            margin.AddThemeConstantOverride("margin_" + side, 80);
        margin.AddChild(Ui.Expand(page));
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _scroll.AddChild(Ui.Expand(margin));
        AddChild(_scroll);
        AddChild(_player);
        UpdateProgress();
    }

    public override void Enter()
    {
        S.ApplyInstrument();
        S.Audio.FrameReady += _context.Push;
    }

    public override void Exit()
    {
        S.Audio.FrameReady -= _context.Push;
        _player.Stop();
        S.Clicks.Stop();
    }

    /// <summary>For `--shots`.</summary>
    public void DebugScrollToEnd() => _scroll.ScrollVertical = 100000;

    private void Play(float[] samples)
    {
        _player.Stream = Playback.ToStream(samples, S.Audio.SampleRate);
        _player.Play();
    }

    private void UpdateProgress()
    {
        var done = _tasks.Count(t => t.Done);
        _progress.Text = string.Format(Ui.T("{0} of {1} tasks done"), done, _tasks.Count);
        _finish.Disabled = done < _tasks.Count;
        _finish.TooltipText = _finish.Disabled ? Ui.T("Do every task on the page first.") : "";
    }

    private void Finish()
    {
        var first = S.Progress.LessonsDone.Add(_lesson.Id);
        var award = S.AwardXp(XpRules.Lesson(first));
        _progress.Text = $"✓ {Ui.T("Lesson finished")}  +{award.Total} XP" +
            string.Concat(award.Unlocked.Select(a => $"   {a.Icon} {Ui.T(a.Title)}"));
        _progress.Modulate = PipitTheme.Good;
        _finish.Visible = false;
        _next.Visible = S.Study.After(new PathStep(StepKind.Lesson, _lesson.Id)) is not null;
    }

    private void Next()
    {
        var next = S.Study.After(new PathStep(StepKind.Lesson, _lesson.Id));
        Main.Instance.Back();
        if (next is not null)
            StudyScreen.Open(next);
    }
}
