using System;
using Godot;
using Pipit.App.UI;
using Pipit.App.Views;
using Pipit.Core.Analysis;

namespace Pipit.App.Screens;

/// <summary>Free tuner: shows the note being played, how far off it is, and its fingering.</summary>
public partial class TunerScreen : Screen
{
    private readonly Label _note;
    private readonly Label _detail;
    private readonly TunerGauge _gauge = new();
    private readonly FingeringDiagram _diagram = new();
    private readonly PitchStrip _strip = new();
    private readonly LevelMeter _meter = new();
    private double _lastVoiced = -10;

    public override string Title => "Tuner";

    public TunerScreen()
    {
        _note = Ui.Label("–", 96, PipitTheme.Ink, HorizontalAlignment.Center);
        _detail = Ui.Text(" ", 20, PipitTheme.InkSoft, HorizontalAlignment.Center);
        var left = Ui.VBox(8, _note, _detail, _gauge);
        var top = Ui.HBox(32, Ui.Expand(left), Ui.MinSize(_diagram, 200, 360));
        AddChild(Ui.VBox(16, top, Ui.Expand(_strip, true, true), Ui.HBox(12, Ui.Label("Input", 16, PipitTheme.InkSoft), Ui.Expand(_meter))));
    }

    public override void Enter()
    {
        S.ApplyInstrument();
        var info = S.Recorder;
        _strip.LowWritten = info.BottomWrittenMidi - 2;
        _strip.HighWritten = info.TopWrittenMidi + 2;
        _strip.Transposition = info.OctaveTransposition * 12;
        _strip.NoteLabel = S.NoteName;
        _diagram.Keyed = S.Instrument.Keyed;
        S.Audio.FrameReady += OnFrame;
    }

    public override void Exit() => S.Audio.FrameReady -= OnFrame;

    private void OnFrame(PitchFrame frame)
    {
        _strip.Add(frame);
        if (!frame.Voiced || double.IsNaN(frame.SmoothedMidi))
            return;
        _lastVoiced = frame.Time;
        var sounding = (int)Math.Round(frame.SmoothedMidi);
        var written = S.Recorder.SoundingToWritten(sounding);
        var cents = (frame.SmoothedMidi - sounding) * 100;
        _note.Text = S.NoteName(written);
        _gauge.Cents = cents;
        var fingering = S.FingeringFor(written);
        _diagram.Fingering = fingering;
        _diagram.Dimmed = false;
        _detail.Text = $"{frame.Hz:0.0} Hz · {cents:+0;-0;0} cents" + (fingering is null ? " · " + Ui.T("outside this recorder's range") : "");
    }

    public override void _Process(double delta)
    {
        _meter.Db = S.Audio.LevelDb;
        // Keep the last note on screen briefly after the sound stops, then fade it.
        if (S.Audio.Now - _lastVoiced > 0.6)
        {
            _gauge.Cents = double.NaN;
            _diagram.Dimmed = true;
            _diagram.QueueRedraw();
            _note.Modulate = new Color(1, 1, 1, 0.35f);
        }
        else
            _note.Modulate = Colors.White;
    }
}
