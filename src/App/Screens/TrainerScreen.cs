using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.UI;
using Pipit.App.Views;
using Pipit.Core.Analysis;

namespace Pipit.App.Screens;

/// <summary>
/// Shows a note (name and fingering), listens until a note is held, and says whether it was the
/// right one. With the fingering hidden it becomes a quiz.
/// </summary>
public partial class TrainerScreen : Screen
{
    private static readonly (string Name, int[] Steps)[] Ranges =
    {
        ("Low octave", new[] { 0, 2, 4, 5, 7, 9, 11, 12 }),
        ("High octave", new[] { 12, 14, 16, 17, 19, 21, 23, 24, 26 }),
        ("All natural notes", new[] { 0, 2, 4, 5, 7, 9, 11, 12, 14, 16, 17, 19, 21, 23, 24, 26 }),
        ("Every note (with ♯ and ♭)", Enumerable.Range(0, 27).ToArray()),
        ("First notes: Si La Sol", new[] { 11, 9, 7 }),
    };

    private readonly Label _target;
    private readonly Label _feedback;
    private readonly Label _score;
    private readonly FingeringDiagram _diagram = new();
    private readonly TunerGauge _gauge = new() { Tolerance = 15 };
    private readonly CheckBox _showFingering;
    private readonly CheckBox _random;
    private readonly HeldNoteDetector _detector = new(0.4);
    private readonly Random _rng = new();
    private int _range;
    private List<int> _order = new();
    private int _index;
    private int _correct, _attempts, _streak;
    private double _advanceAt = double.NaN;
    private readonly Dictionary<int, List<double>> _cents = new();

    public override string Title => "Fingering trainer";

    public TrainerScreen()
    {
        _target = Ui.Label("", 110, PipitTheme.Ink, HorizontalAlignment.Center);
        _feedback = Ui.Text("", 26, PipitTheme.InkSoft, HorizontalAlignment.Center);
        _score = Ui.Label("", 18, PipitTheme.InkSoft, HorizontalAlignment.Center);
        _showFingering = new CheckBox { Text = Ui.T("Show fingering"), ButtonPressed = true };
        _showFingering.Toggled += _ => ShowTarget();
        _random = new CheckBox { Text = Ui.T("Random order") };
        _random.Toggled += _ => Restart();
        var range = Ui.Options(Ranges.Select(r => r.Name).ToArray(), 0, ix => { _range = ix; Restart(); });

        var controls = Ui.HBox(16, Ui.Label("Notes:"), range, _random, _showFingering, Ui.Spacer(), Ui.Button("Skip", Next, 18));
        var center = Ui.VBox(8, _target, _feedback, _gauge, _score);
        var main = Ui.HBox(40, Ui.Expand(center), Ui.MinSize(_diagram, 250, 420));
        AddChild(Ui.VBox(20, controls, Ui.Expand(main, true, true)));
    }

    public override void Enter()
    {
        S.ApplyInstrument();
        _diagram.Keyed = S.Instrument.Keyed;
        S.Audio.FrameReady += OnFrame;
        Restart();
    }

    public override void Exit() => S.Audio.FrameReady -= OnFrame;

    private void Restart()
    {
        _order = Ranges[_range].Steps.ToList();
        if (_random.ButtonPressed)
            _order = _order.OrderBy(_ => _rng.Next()).ToList();
        _index = 0;
        ShowTarget();
    }

    private int TargetWritten => S.Recorder.BottomWrittenMidi + _order[_index];
    private int TargetSounding => S.Recorder.WrittenToSounding(TargetWritten);

    private void ShowTarget()
    {
        _target.Text = S.NoteName(TargetWritten);
        _target.Modulate = Colors.White;
        _diagram.Fingering = S.FingeringFor(TargetWritten);
        _diagram.Visible = _showFingering.ButtonPressed;
        _feedback.Text = Ui.T(_showFingering.ButtonPressed ? "Play this note and hold it" : "Which fingering is this? Play it!");
        _feedback.Modulate = Colors.White;
        _gauge.Cents = double.NaN;
        _detector.Reset();
        _advanceAt = double.NaN;
        UpdateScore();
    }

    private void Next()
    {
        _index = (_index + 1) % _order.Count;
        if (_index == 0 && _random.ButtonPressed)
            _order = _order.OrderBy(_ => _rng.Next()).ToList();
        ShowTarget();
    }

    private void OnFrame(PitchFrame frame)
    {
        if (!double.IsNaN(_advanceAt))
            return;
        if (frame.Voiced && !double.IsNaN(frame.SmoothedMidi) && (int)Math.Round(frame.SmoothedMidi) == TargetSounding)
            _gauge.Cents = (frame.SmoothedMidi - TargetSounding) * 100;

        if (_detector.Push(frame) is not { } held)
            return;
        _attempts++;
        if (held.SoundingMidi == TargetSounding)
        {
            _correct++;
            _streak++;
            if (!_cents.TryGetValue(TargetSounding, out var list))
                _cents[TargetSounding] = list = new List<double>();
            list.Add(held.CentsOff);
            _feedback.Text = Ui.T("Yes!") + "  " + TuningRemark(held.CentsOff);
            _feedback.Modulate = PipitTheme.Good;
            _diagram.Visible = true; // reveal the answer in quiz mode
            _advanceAt = frame.Time + 1.0;
        }
        else
        {
            _streak = 0;
            var playedWritten = S.Recorder.SoundingToWritten(held.SoundingMidi);
            var octave = Math.Abs(held.SoundingMidi - TargetSounding) == 12;
            _feedback.Text = string.Format(Ui.T("That was {0}."), S.NoteName(playedWritten)) + " " +
                Ui.T(octave ? "Right fingering, wrong octave: check the thumb and blow more gently or firmly." : "Check the fingering and try again.");
            _feedback.Modulate = PipitTheme.Bad;
        }
        UpdateScore();
    }

    private static string TuningRemark(double cents) => Math.Abs(cents) <= 15 ? Ui.T("Nicely in tune.")
        : cents > 0 ? Ui.T("A little high: blow a bit more gently.")
        : Ui.T("A little low: blow a bit more firmly.");

    private void UpdateScore() =>
        _score.Text = string.Format(Ui.T("{0} of {1} right · streak {2}"), _correct, _attempts, _streak);

    public override void _Process(double delta)
    {
        if (!double.IsNaN(_advanceAt) && S.Audio.Now >= _advanceAt)
            Next();
    }
}
