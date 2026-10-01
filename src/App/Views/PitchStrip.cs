using System;
using System.Collections.Generic;
using Godot;
using Pipit.App.UI;
using Pipit.Core.Analysis;

namespace Pipit.App.Views;

/// <summary>
/// The last few seconds of detected pitch, scrolling right to left over a semitone grid labelled
/// with note names. Pitches are drawn as written notes for the active instrument.
/// </summary>
public partial class PitchStrip : Control
{
    private readonly List<PitchFrame> _frames = new();

    public double Seconds { get; set; } = 6;
    public int LowWritten { get; set; } = 60;
    public int HighWritten { get; set; } = 86;
    /// <summary>Sounding → written offset (12 for a soprano).</summary>
    public int Transposition { get; set; }
    public Func<int, string> NoteLabel { get; set; } = m => m.ToString();
    public int? Target { get; set; }

    public PitchStrip() => CustomMinimumSize = new Vector2(400, 220);

    public void Add(PitchFrame frame)
    {
        _frames.Add(frame);
        var cutoff = frame.Time - Seconds;
        var drop = 0;
        while (drop < _frames.Count && _frames[drop].Time < cutoff)
            drop++;
        _frames.RemoveRange(0, drop);
        QueueRedraw();
    }

    public void Clear()
    {
        _frames.Clear();
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Colors.White with { A = 0.6f });
        var labelW = 56f;
        var rows = HighWritten - LowWritten + 1;
        var rowH = Size.Y / rows;
        float Y(double written) => (float)(Size.Y - (written - LowWritten + 0.5) * rowH);
        var font = PipitTheme.Font;

        for (var m = LowWritten; m <= HighWritten; m++)
        {
            var natural = Services.Spell(m).Alter == 0;
            if (Target == m)
                DrawRect(new Rect2(labelW, Y(m) - rowH / 2, Size.X - labelW, rowH), PipitTheme.Accent with { A = 0.25f });
            DrawLine(new Vector2(labelW, Y(m)), new Vector2(Size.X, Y(m)), natural ? PipitTheme.PaperDark : PipitTheme.Paper, 1);
            if (natural && rowH >= 5)
                DrawString(font, new Vector2(4, Y(m) + Math.Min(rowH * 1.7f, 16) * 0.35f), NoteLabel(m), HorizontalAlignment.Left, labelW - 6, (int)Math.Min(rowH * 1.7f, 16), PipitTheme.InkSoft);
        }

        if (_frames.Count == 0)
            return;
        var now = _frames[^1].Time;
        float X(double t) => (float)(labelW + (1 - (now - t) / Seconds) * (Size.X - labelW));
        Vector2? previous = null;
        foreach (var f in _frames)
        {
            if (!f.Voiced || double.IsNaN(f.SmoothedMidi))
            {
                previous = null;
                continue;
            }
            var written = f.SmoothedMidi - Transposition;
            if (written < LowWritten - 1 || written > HighWritten + 1)
            {
                previous = null;
                continue;
            }
            var p = new Vector2(X(f.Time), Y(written));
            var off = Math.Abs(written - Math.Round(written)) * 100;
            var color = off <= 15 ? PipitTheme.Good : off <= 30 ? PipitTheme.Warn : PipitTheme.Bad;
            if (previous is { } prev)
                DrawLine(prev, p, color, 3, true);
            previous = p;
        }
    }
}
