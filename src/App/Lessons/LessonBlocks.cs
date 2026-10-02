using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Pipit.App.Audio;
using Pipit.App.UI;
using Pipit.App.Views;
using Pipit.Core.Analysis;
using Pipit.Core.Lessons;
using Pipit.Core.Music;
using Pipit.Core.Scoring;
using Pipit.Core.Synthesis;

namespace Pipit.App.Lessons;

/// <summary>A lesson block that can be completed (try, exercise, quiz).</summary>
public interface ITaskBlock
{
    bool Done { get; }
    event Action? Completed;
}

/// <summary>Builds the controls for lesson blocks.</summary>
public static partial class LessonBlocks
{
    private static Services S => Services.Instance;

    public static Control Build(LessonBlock block, LessonContext context) => block switch
    {
        HeadingBlock h => Ui.Label(h.Text, h.Level == 1 ? 32 : 24, PipitTheme.Accent),
        ParagraphBlock p => Rich(p.Text),
        ListBlock l => Rich(string.Join("\n", l.Items.Select(i => "•  " + i))),
        DirectiveBlock d => d.Name switch
        {
            "pip" => Pip(d.Text ?? "", d.Arg("mood")),
            "fingering" => Fingering(Pitch.Parse(d.Arg("note"))),
            "staff" => Staff(d),
            "play" => Listen(d, context),
            "try" => new TryBlock(d, context),
            "exercise" => new ExerciseBlock(d, context),
            "quiz" => new QuizBlock(d),
            _ => Ui.Label($"[{d.Name}]", 14, PipitTheme.Bad),
        },
        _ => new Control(),
    };

    /// <summary>A song built from a lesson snippet, so the score and rhythm code can use it.</summary>
    public static Song SnippetSong(DirectiveBlock d)
    {
        var time = TimeSignature.Parse(d.Arg("time", "4/4"));
        var (bars, performance) = SongParser.ParseMusic(d.Arg("music"), time, 0, "lesson");
        return new Song
        {
            Id = "lesson-snippet", Titles = new Dictionary<string, string> { ["en"] = "" }, Tempo = d.Number("tempo", 72),
            Time = time, Key = d.Arg("key", "C"),
            Parts = new[] { new SongPart("melody", "melody", "soprano", bars, performance) },
        };
    }

    private static RichTextLabel Rich(string markdown)
    {
        var bbcode = Italic().Replace(Bold().Replace(markdown, "[b]$1[/b]"), "[i]$1[/i]");
        var label = new RichTextLabel { BbcodeEnabled = true, Text = bbcode, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        label.AddThemeFontSizeOverride("normal_font_size", 19);
        label.AddThemeFontSizeOverride("bold_font_size", 19);
        label.AddThemeFontSizeOverride("italics_font_size", 19);
        label.AddThemeColorOverride("default_color", PipitTheme.Ink);
        label.AddThemeFontOverride("normal_font", PipitTheme.Font);
        label.AddThemeFontOverride("bold_font", PipitTheme.BoldFont);
        label.AddThemeFontOverride("italics_font", PipitTheme.ItalicFont);
        return label;
    }

    /// <summary>Pip with a speech bubble. Praise bubbles (after=task) cheer when they are shown.</summary>
    private static Control Pip(string text, string mood)
    {
        var pip = new PipView { CustomMinimumSize = new Vector2(96, 96) };
        if (System.Enum.TryParse<PipMood>(mood, true, out var m))
            pip.SetMood(m);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", PipitTheme.Box(PipitTheme.Accent with { A = 0.12f }, PipitTheme.Accent, 10));
        panel.AddChild(Ui.HBox(12, pip, Ui.Expand(Ui.VBox(2, Ui.Spacer(), Ui.Label("Pip", 16, PipitTheme.Accent), Ui.Text(text, 19, PipitTheme.Ink), Ui.Spacer()))));
        panel.VisibilityChanged += () =>
        {
            if (panel.Visible && panel.IsInsideTree() && panel.GetMeta("praise", false).AsBool())
                pip.SetMood(PipMood.Cheering, 2.5, PipMood.Singing);
        };
        return panel;
    }

    private static Control Fingering(Pitch pitch)
    {
        var diagram = new FingeringDiagram { Fingering = S.FingeringFor(pitch.Midi), Keyed = S.Instrument.Keyed, CustomMinimumSize = new Vector2(220, 340) };
        return Ui.HBox(30, diagram, Ui.VBox(4, Ui.Spacer(), Ui.Label(S.NoteName(pitch), 64, PipitTheme.Ink), Ui.Spacer()));
    }

    private static Control Staff(DirectiveBlock d)
    {
        var view = new StaffView { ShowCursor = false, CustomMinimumSize = new Vector2(0, 210), NoteLabel = p => S.NoteName(p) };
        view.SetSong(SnippetSong(d), S.Instrument.Kind);
        return view;
    }

    private static Control Listen(DirectiveBlock d, LessonContext context)
    {
        var song = SnippetSong(d);
        return Ui.HBox(10, Ui.Button("♪ " + (d.Text ?? Ui.T("Listen")), async () =>
        {
            var audio = await Audio.Synth.RenderAsync(song, new[] { song.Melody }, song.SecondsPerBeat(),
                song.Melody.Performance.Min(n => n.StartBeat), 0.3, natural: S.Settings.Voice == "natural");
            context.Play(audio);
        }, 18), Ui.Spacer());
    }

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"\*(.+?)\*")]
    private static partial Regex Italic();
}

/// <summary>What blocks share with their lesson screen: live pitch frames and a player.</summary>
public sealed class LessonContext
{
    public event Action<PitchFrame>? Frame;
    public Action<float[]> Play { get; init; } = _ => { };
    public void Push(PitchFrame frame) => Frame?.Invoke(frame);
}

/// <summary>"Play this note": done once the note has been held on the right semitone.</summary>
public partial class TryBlock : PanelContainer, ITaskBlock
{
    private readonly int _target;
    private readonly HeldNoteDetector _detector;
    private readonly Label _status;
    public bool Done { get; private set; }
    public event Action? Completed;

    public TryBlock(DirectiveBlock d, LessonContext context)
    {
        var pitch = Pitch.Parse(d.Arg("note"));
        _target = Services.Instance.Recorder.WrittenToSounding(pitch.Midi);
        _detector = new HeldNoteDetector(d.Number("hold", 1.0), settleSeconds: 0.15);
        AddThemeStyleboxOverride("panel", PipitTheme.Box(Colors.White, PipitTheme.WoodEdge, 12));
        _status = Ui.Label(Ui.T("Listening… play when you are ready."), 18, PipitTheme.InkSoft);
        AddChild(Ui.HBox(16, Ui.Label("🎵", 30), Ui.VBox(4, Ui.Label(d.Text ?? Ui.T("Play this note"), 21, PipitTheme.Ink), _status)));
        context.Frame += OnFrame;
        TreeExiting += () => context.Frame -= OnFrame;
    }

    private void OnFrame(PitchFrame frame)
    {
        if (Done || _detector.Push(frame) is not { } held)
            return;
        var s = Services.Instance;
        // Allow for the recorder's calibrated tuning, as the grader does.
        var played = (int)Math.Round(held.MedianMidi - s.Instrument.MeasuredOffsetCents / 100);
        if (played == _target)
        {
            Done = true;
            _status.Text = "✓ " + Ui.T("Well done!");
            _status.Modulate = PipitTheme.Good;
            Completed?.Invoke();
        }
        else
        {
            var name = s.NoteName(s.Recorder.SoundingToWritten(played));
            _status.Text = string.Format(Ui.T("That was {0}. Check the fingering above and try again."), name);
            _status.Modulate = PipitTheme.Bad;
        }
    }
}

/// <summary>A short play-along on the snippet: count-in, clicks, graded live on the score.</summary>
public partial class ExerciseBlock : PanelContainer, ITaskBlock
{
    private readonly Song _song;
    private readonly StaffView _view = new() { CustomMinimumSize = new Vector2(0, 210) };
    private readonly Label _status;
    private readonly Button _start;
    private RhythmRunner? _runner;
    public bool Done { get; private set; }
    public event Action? Completed;

    public ExerciseBlock(DirectiveBlock d, LessonContext context)
    {
        var s = Services.Instance;
        _song = LessonBlocks.SnippetSong(d);
        _view.NoteLabel = p => s.NoteName(p);
        _view.SetSong(_song, s.Instrument.Kind);
        _view.ShowCursor = false; // shown once the exercise runs
        AddThemeStyleboxOverride("panel", PipitTheme.Box(Colors.White, PipitTheme.WoodEdge, 12));
        _status = Ui.Text(Ui.T("Press Start: four clicks count you in, then play along."), 17, PipitTheme.InkSoft);
        _start = Ui.Button("▶ Start", Start, 18);
        AddChild(Ui.VBox(8, Ui.HBox(12, Ui.Label("🥁", 28), Ui.Label(d.Text ?? Ui.T("Play along"), 21, PipitTheme.Ink), Ui.Spacer(), _start), _view, _status));
        context.Frame += OnFrame;
        TreeExiting += () => { context.Frame -= OnFrame; _runner?.Stop(); };
    }

    private void Start()
    {
        _view.ClearMarks();
        _view.ShowCursor = true;
        _runner = new RhythmRunner(_song, Tolerances.Relaxed);
        _start.Disabled = true;
        _status.Text = Ui.T("Get ready…");
        _status.Modulate = Colors.White;
    }

    private void OnFrame(PitchFrame frame)
    {
        if (_runner is null || _runner.Finished)
            return;
        _runner.Push(frame);
        var written = frame.Voiced ? frame.SmoothedMidi - Services.Instance.Recorder.OctaveTransposition * 12 : double.NaN;
        _view.AddTrace(_runner.TimeToBeat(frame.Time - _runner.Latency), written);
    }

    public override void _Process(double delta)
    {
        if (_runner is null || _runner.Finished && _runner.Result is null)
            return;
        var now = Services.Instance.Audio.Now;
        if (!_runner.Finished)
        {
            _view.CurrentBeat = _runner.TimeToBeat(now);
            _view.Overlay = now < _runner.FirstNoteTime ? Math.Ceiling((_runner.FirstNoteTime - now) / _runner.SecondsPerBeat).ToString("0") : "";
        }
        var run = _runner.Update(now);
        foreach (var j in run.Notes)
            _view.SetMark(j.Expected.Index, j.Result switch
            {
                NoteResult.Missed => NoteMark.Missed,
                NoteResult.WrongNote => NoteMark.Wrong,
                _ => j.Timing is Timing.Perfect or Timing.Great or Timing.Good ? NoteMark.Perfect : NoteMark.Good, // amber: early or late
            });
        if (_runner.Finished && _runner.Result is { } result)
            Finish(result);
    }

    private void Finish(GradedRun result)
    {
        _runner = null;
        _view.Overlay = "";
        _start.Disabled = false;
        _start.Text = Ui.T("↻ Again");
        var verdict = ExerciseVerdict.Of(result);
        var passed = verdict.Passed;
        var tips = new List<string>();
        if (verdict.TuningTip != 0)
            tips.Add(Ui.T(verdict.TuningTip > 0 ? "A little high: blow a bit more gently." : "A little low: blow a bit more firmly."));
        if (verdict.LengthTip)
            tips.Add(Ui.T("Keep blowing until the next note: some notes were short."));
        _status.Text = string.Format(Ui.T("{0} of {1} notes right and on time."), verdict.OnTime, verdict.Total) + " " +
            Ui.T(passed ? "✓ Good, on to the next part." : "Try again: listen to the clicks and play one note on each.") +
            (tips.Count > 0 ? "\n" + string.Join(" ", tips) : "");
        _status.Modulate = passed ? PipitTheme.Good : PipitTheme.Warn;
        if (passed && !Done)
        {
            Done = true;
            Completed?.Invoke();
        }
    }
}

public partial class QuizBlock : PanelContainer, ITaskBlock
{
    public bool Done { get; private set; }
    public event Action? Completed;

    public QuizBlock(DirectiveBlock d)
    {
        var (options, correct) = LessonParser.QuizOptions(d);
        AddThemeStyleboxOverride("panel", PipitTheme.Box(Colors.White, PipitTheme.WoodEdge, 12));
        var feedback = Ui.Label("", 17, PipitTheme.InkSoft);
        var buttons = Ui.HBox(10);
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            Button? button = null;
            button = Ui.Button(options[i], () =>
            {
                if (Done)
                    return;
                if (index == correct)
                {
                    Done = true;
                    button!.AddThemeColorOverride("font_color", PipitTheme.Good);
                    feedback.Text = "✓ " + Ui.T("Right!");
                    feedback.Modulate = PipitTheme.Good;
                    Completed?.Invoke();
                }
                else
                {
                    button!.Disabled = true;
                    feedback.Text = Ui.T("Not quite, try another answer.");
                    feedback.Modulate = PipitTheme.Bad;
                }
            }, 18);
            buttons.AddChild(button);
        }
        AddChild(Ui.VBox(8, Ui.HBox(12, Ui.Label("❓", 26), Ui.Label(d.Text ?? "", 21, PipitTheme.Ink)), buttons, feedback));
    }
}
