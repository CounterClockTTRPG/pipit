using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.UI;
using Pipit.Core.Analysis;
using Pipit.Core.Recording;

namespace Pipit.App.Views;

/// <summary>
/// A whole recorded take: waveform along the bottom, detected pitch over a semitone grid, and
/// for guided takes the expected notes as boxes, green when recognised and red when not.
/// </summary>
public partial class TakeView : Control
{
    private float[] _samples = Array.Empty<float>();
    private int _rate = 48000;
    private IReadOnlyList<PitchFrame> _frames = Array.Empty<PitchFrame>();
    private IReadOnlyList<NoteCheck> _checks = Array.Empty<NoteCheck>();
    private double _latency;

    public int LowWritten { get; set; } = 60;
    public int HighWritten { get; set; } = 86;
    public int Transposition { get; set; }
    public Func<int, string> NoteLabel { get; set; } = m => m.ToString();
    public double Playhead { get; set; } = -1;
    public string Message { get; set; } = "";

    public TakeView() => CustomMinimumSize = new Vector2(500, 300);

    public void Set(float[] samples, int rate, IReadOnlyList<PitchFrame> frames, IReadOnlyList<NoteCheck> checks, double latencySeconds)
    {
        _samples = samples;
        _rate = rate;
        _frames = frames;
        _checks = checks;
        _latency = latencySeconds;
        QueueRedraw();
    }

    public void Clear() => Set(Array.Empty<float>(), 48000, Array.Empty<PitchFrame>(), Array.Empty<NoteCheck>(), 0);

    public override void _Process(double delta)
    {
        if (Playhead >= 0)
            QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Colors.White with { A = 0.7f });
        var font = PipitTheme.Font;
        if (_samples.Length == 0)
        {
            DrawString(font, new Vector2(20, Size.Y / 2), string.IsNullOrEmpty(Message) ? Ui.T("No take selected") : Message, HorizontalAlignment.Left, Size.X - 40, 18, PipitTheme.InkSoft);
            return;
        }

        const float labelW = 56;
        var waveH = Size.Y * 0.18f;
        var gridH = Size.Y - waveH - 6;
        var duration = (double)_samples.Length / _rate;
        float X(double t) => (float)(labelW + t / duration * (Size.X - labelW));
        var rows = HighWritten - LowWritten + 1;
        var rowH = gridH / rows;
        float Y(double written) => (float)(gridH - (written - LowWritten + 0.5) * rowH);

        for (var m = LowWritten; m <= HighWritten; m++)
        {
            var natural = Services.Spell(m).Alter == 0;
            DrawLine(new Vector2(labelW, Y(m)), new Vector2(Size.X, Y(m)), natural ? PipitTheme.PaperDark : PipitTheme.Paper, 1);
            if (natural && rowH >= 5)
                DrawString(font, new Vector2(4, Y(m) + 5), NoteLabel(m), HorizontalAlignment.Left, labelW - 6, (int)Math.Min(rowH * 1.7f, 15), PipitTheme.InkSoft);
        }

        // Expected notes (guided takes), shifted by the latency the player was measured to have.
        foreach (var c in _checks)
        {
            var written = c.Expected.WrittenMidi;
            var rect = new Rect2(X(c.Expected.Start + _latency), Y(written) - rowH * 0.45f, X(c.Expected.End + _latency) - X(c.Expected.Start + _latency), rowH * 0.9f);
            var color = c.Recognised ? PipitTheme.Good : PipitTheme.Bad;
            DrawRect(rect, color with { A = 0.25f });
            DrawRect(rect, color, false, 1.5f);
        }

        // Detected pitch.
        Vector2? previous = null;
        foreach (var f in _frames)
        {
            if (!f.Voiced || double.IsNaN(f.SmoothedMidi))
            {
                previous = null;
                continue;
            }
            var p = new Vector2(X(f.Time), Y(f.SmoothedMidi - Transposition));
            if (previous is { } prev)
                DrawLine(prev, p, PipitTheme.Ink, 2, true);
            previous = p;
        }

        // Waveform: peak per pixel column.
        var top = gridH + 6;
        var mid = top + waveH / 2;
        var columns = (int)(Size.X - labelW);
        var per = Math.Max(1, _samples.Length / Math.Max(1, columns));
        for (var c = 0; c < columns; c++)
        {
            float peak = 0;
            for (int i = c * per, end = Math.Min(_samples.Length, i + per); i < end; i++)
                peak = Math.Max(peak, Math.Abs(_samples[i]));
            var h = Math.Min(1, peak * 2) * waveH / 2;
            DrawLine(new Vector2(labelW + c, mid - h), new Vector2(labelW + c, mid + h), PipitTheme.WoodEdge, 1);
        }

        if (Playhead >= 0)
            DrawLine(new Vector2(X(Playhead), 0), new Vector2(X(Playhead), Size.Y), PipitTheme.Accent, 2);
    }
}
