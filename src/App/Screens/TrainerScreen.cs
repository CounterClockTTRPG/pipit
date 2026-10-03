using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.UI;
using Pipit.App.Views;
using Pipit.Core.Analysis;
using Pipit.Core.Game;
using Pipit.Core.Practice;

namespace Pipit.App.Screens;

/// <summary>
/// Shows a note (name and fingering), listens until a note is held, and says whether it was the
/// right one. With the fingering hidden it becomes a quiz.
///
/// Learning mode (the first option) works like flash cards: Pip starts with the first notes of
/// the study path, brings in a new note once those are found reliably, asks weak notes more
/// often, and hides the fingering of notes that are known. Every answer, in either mode, goes
/// into the player's per-note record (<see cref="Core.Game.NoteStat"/>), shown under My notes.
/// </summary>
public partial class TrainerScreen : Screen
{
    private static readonly (string Name, int[] Steps)[] Ranges =
    {
        ("Learn: Pip picks the notes", Array.Empty<int>()), // learning mode, see LearningDeck
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
    private readonly PipView _pip = new() { CustomMinimumSize = new Vector2(110, 110) };
    private readonly TunerGauge _gauge = new() { Tolerance = 15 };
    private readonly CheckBox _showFingering;
    private readonly CheckBox _random;
    private readonly HeldNoteDetector _detector = new(0.4);
    private readonly Random _rng = new();
    private int _range; // 0 is learning mode
    private bool Learning => _range == 0;
    private LearningDeck? _deck;
    private int _learnTarget;
    private double _shownAt;
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
        var myNotes = Ui.Quiet(Ui.Button("My notes", () => Main.Instance.Show(new NoteStatsScreen()), 18));

        var controls = Ui.HBox(16, Ui.Label("Notes:"), range, _random, _showFingering, Ui.Spacer(), myNotes, Ui.Quiet(Ui.Button("Skip", Next, 18)));
        var center = Ui.VBox(8, _target, _feedback, _gauge, _score, Ui.HBox(0, Ui.Spacer(), _pip, Ui.Spacer()));
        var main = Ui.HBox(40, Ui.Expand(center), Ui.MinSize(_diagram, 200, 420));
        AddChild(Ui.VBox(20, controls, Ui.Expand(main, true, true)));
    }

    public override void Enter()
    {
        S.ApplyInstrument();
        _diagram.Keyed = S.Instrument.Keyed;
        // Allow for this recorder's tuning, so a sharp recorder's notes do not flip semitones.
        _detector.TuningOffsetCents = S.Instrument.MeasuredOffsetCents;
        S.Audio.FrameReady += OnFrame;
        Restart();
    }

    public override void Exit()
    {
        S.Audio.FrameReady -= OnFrame;
        S.SaveProgress();
        if (_correct > _awarded)
        {
            S.Progress.TrainerNotes += _correct - _awarded;
            S.AwardXp(Pipit.Core.Game.XpRules.Trainer(_correct - _awarded));
        }
        _awarded = _correct;
    }

    private int _awarded;

    private string Kind => S.Instrument.Kind.ToString();
    private string DeckKey => $"{PlayerProgress.Playing}|{Kind.ToLowerInvariant()}";

    /// <summary>Every note of this recorder, in the order the study path teaches them, then the rest.</summary>
    public static List<int> TeachingOrder()
    {
        var info = S.Recorder;
        var taught = Core.Lessons.Review.Taught(S.Study, null, S.LoadLesson, S.Songs.Get).Notes
            .Where(n => n >= info.BottomWrittenMidi && n <= info.BottomWrittenMidi + Core.Instruments.RecorderInfo.RangeSemitones);
        var all = Enumerable.Range(info.BottomWrittenMidi, Core.Instruments.RecorderInfo.RangeSemitones + 1);
        return taught.Concat(all).Distinct().ToList();
    }

    private void Restart()
    {
        _random.Visible = _showFingering.Visible = !Learning;
        if (Learning)
        {
            _deck = new LearningDeck(TeachingOrder(), S.Progress.NotesIntroduced.GetValueOrDefault(DeckKey),
                n => S.Progress.FindNote(PlayerProgress.Playing, Kind, n), _rng);
            _learnTarget = _deck.Next(null);
            ShowTarget();
            return;
        }
        _order = Ranges[_range].Steps.ToList();
        if (_random.ButtonPressed)
            _order = _order.OrderBy(_ => _rng.Next()).ToList();
        _index = 0;
        ShowTarget();
    }

    private int TargetWritten => Learning ? _learnTarget : S.Recorder.BottomWrittenMidi + _order[_index];
    private int TargetSounding => S.Recorder.WrittenToSounding(TargetWritten);

    private void ShowTarget(bool isNew = false)
    {
        _target.Text = S.NoteName(TargetWritten);
        _target.Modulate = Colors.White;
        _diagram.Fingering = S.FingeringFor(TargetWritten);
        // Learning mode shows the fingering while a note is still new, and asks known ones from memory.
        var show = Learning
            ? (S.Progress.FindNote(PlayerProgress.Playing, Kind, TargetWritten)?.Box ?? 0) < LearningDeck.ReadyBox
            : _showFingering.ButtonPressed;
        _diagram.Visible = show;
        _feedback.Text = isNew ? string.Format(Ui.T("A new note: {0}! Here is its fingering."), S.NoteName(TargetWritten))
            : Ui.T(show ? "Play this note and hold it" : "Which fingering is this? Play it!");
        if (isNew)
            _pip.SetMood(PipMood.Cheering, 2.0);
        _shownAt = S.Audio.Now;
        _feedback.Modulate = Colors.White;
        _gauge.Cents = double.NaN;
        _detector.Reset();
        _advanceAt = double.NaN;
        UpdateScore();
    }

    private void Next()
    {
        if (Learning && _deck is { } deck)
        {
            if (deck.IntroduceIfReady() is { } newcomer)
            {
                S.Progress.NotesIntroduced[DeckKey] = deck.Introduced;
                S.SaveProgress();
                _learnTarget = newcomer;
                ShowTarget(isNew: true);
                return;
            }
            _learnTarget = deck.Next(_learnTarget);
            ShowTarget();
            return;
        }
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
        var right = held.SoundingMidi == TargetSounding;
        S.Progress.Note(PlayerProgress.Playing, Kind, TargetWritten).Record(right, held.Start - _shownAt, DateTime.Now);
        if (right)
        {
            _correct++;
            _streak++;
            if (!_cents.TryGetValue(TargetSounding, out var list))
                _cents[TargetSounding] = list = new List<double>();
            list.Add(held.CentsOff);
            _feedback.Text = Ui.T("Yes!") + "  " + TuningRemark(held.CentsOff);
            _feedback.Modulate = PipitTheme.Good;
            _pip.SetMood(_streak >= 5 ? PipMood.Cheering : PipMood.Singing, 1.2);
            _diagram.Visible = true; // reveal the answer in quiz mode
            _advanceAt = frame.Time + 1.0;
        }
        else
        {
            _streak = 0;
            var playedWritten = S.Recorder.SoundingToWritten(held.SoundingMidi);
            _feedback.Text = string.Format(Ui.T("That was {0}."), S.NoteName(playedWritten)) + " " + Hint(held.SoundingMidi - TargetSounding);
            _feedback.Modulate = PipitTheme.Bad;
            _pip.SetMood(PipMood.Thinking, 2.0);
        }
        UpdateScore();
    }

    /// <summary>What to change, given how far the played note was from the target.</summary>
    private string Hint(int semitonesOff)
    {
        if (semitonesOff == -12)
        {
            // Aimed high, sounded low: the octave did not "break".
            var pinched = S.FingeringFor(TargetWritten)?.Thumb == Core.Instruments.HoleState.Pinched;
            return Ui.T(pinched
                ? "Right fingering, but the low octave: pinch the thumb (open a small gap at the top of the thumb hole with your nail) and use slightly faster air."
                : "Right fingering, but the low octave: seal every hole fully and use slightly faster air with a crisp \"du\".");
        }
        if (semitonesOff == 12)
            return Ui.T("Right fingering, but it jumped an octave up: use gentler, slower air, and close the thumb hole completely.");
        return Ui.T("Check the fingering and try again.");
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
