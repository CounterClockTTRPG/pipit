using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.UI;

namespace Pipit.App.Views;

public sealed record BarNote(int Index, int WrittenMidi, double StartBeat, double Beats);

public enum NoteMark { None, Current, Perfect, Good, Wrong, Missed }

/// <summary>
/// The song as bars on a pitch grid, scrolling past a fixed playhead: each note is a bar labelled
/// with its name, bar lines are numbered, and the player's pitch is traced over it. Notes are
/// coloured as they are graded. The screen sets <see cref="CurrentBeat"/> every frame.
/// </summary>
public partial class BarsView : Control
{
    private IReadOnlyList<BarNote> _notes = Array.Empty<BarNote>();
    private readonly Dictionary<int, NoteMark> _marks = new();
    private readonly List<Vector2> _trace = new(); // (beat, written midi); NaN y = break
    private int _low = 60, _high = 72;

    public double BarBeats { get; set; } = 4;
    public double CurrentBeat { get; set; }
    public float PixelsPerBeat { get; set; } = 90;
    public float PlayheadFraction { get; set; } = 0.25f;
    public Func<int, string> NoteLabel { get; set; } = m => m.ToString();
    /// <summary>Big centred text (count-in, "Listen…"); empty for none.</summary>
    public string Overlay { get; set; } = "";
    /// <summary>Highlight the note under the playhead (listening, playing along).</summary>
    public bool HighlightCurrent { get; set; } = true;

    public BarsView()
    {
        ClipContents = true;
        CustomMinimumSize = new Vector2(500, 260);
    }

    public void SetNotes(IReadOnlyList<BarNote> notes)
    {
        _notes = notes;
        _marks.Clear();
        _trace.Clear();
        if (notes.Count > 0)
        {
            _low = notes.Min(n => n.WrittenMidi) - 2;
            _high = notes.Max(n => n.WrittenMidi) + 2;
        }
    }

    public void SetMark(int index, NoteMark mark) => _marks[index] = mark;
    public NoteMark MarkOf(int index) => _marks.TryGetValue(index, out var m) ? m : NoteMark.None;

    public void ClearMarks()
    {
        _marks.Clear();
        _trace.Clear();
    }

    /// <summary>Adds a traced pitch at a beat; NaN breaks the line (silence).</summary>
    public void AddTrace(double beat, double writtenMidi) => _trace.Add(new Vector2((float)beat, (float)writtenMidi));

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Colors.White with { A = 0.75f });
        const float labelW = 60;
        var rows = _high - _low + 1;
        var rowH = Size.Y / rows;
        var font = PipitTheme.Font;
        var playX = labelW + (Size.X - labelW) * PlayheadFraction;
        float X(double beat) => (float)(playX + (beat - CurrentBeat) * PixelsPerBeat);
        float Y(double written) => (float)(Size.Y - (written - _low + 0.5) * rowH);
        var firstVisible = CurrentBeat - (playX - labelW) / PixelsPerBeat;
        var lastVisible = CurrentBeat + (Size.X - playX) / PixelsPerBeat;

        // Grid: natural notes get a line and a name.
        for (var m = _low; m <= _high; m++)
        {
            if (Services.Spell(m).Alter != 0)
                continue;
            DrawLine(new Vector2(labelW, Y(m)), new Vector2(Size.X, Y(m)), PipitTheme.PaperDark, 1);
            DrawString(font, new Vector2(6, Y(m) + 6), NoteLabel(m), HorizontalAlignment.Left, labelW - 8, (int)Math.Clamp(rowH * 0.9f, 11, 18), PipitTheme.InkSoft);
        }

        // Bar lines and numbers.
        var firstBar = (int)Math.Floor(firstVisible / BarBeats);
        for (var b = firstBar; b * BarBeats <= lastVisible; b++)
        {
            var x = X(b * BarBeats);
            if (x < labelW)
                continue;
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), PipitTheme.WoodEdge with { A = 0.5f }, 1);
            if (b >= 0)
                DrawString(font, new Vector2(x + 4, 16), (b + 1).ToString(), HorizontalAlignment.Left, -1, 13, PipitTheme.InkSoft);
        }

        // Notes.
        foreach (var n in _notes)
        {
            if (n.StartBeat + n.Beats < firstVisible || n.StartBeat > lastVisible)
                continue;
            var x1 = Math.Max(labelW, X(n.StartBeat) + 1);
            var x2 = X(n.StartBeat + n.Beats) - 3;
            if (x2 <= x1)
                continue;
            var rect = new Rect2(x1, Y(n.WrittenMidi) - rowH * 0.42f, x2 - x1, rowH * 0.84f);
            var mark = MarkOf(n.Index);
            if (mark == NoteMark.None && HighlightCurrent && CurrentBeat >= n.StartBeat && CurrentBeat < n.StartBeat + n.Beats)
                mark = NoteMark.Current;
            var (fill, border) = mark switch
            {
                NoteMark.Current => (PipitTheme.Accent, PipitTheme.Accent),
                NoteMark.Perfect => (PipitTheme.Good, PipitTheme.Good),
                NoteMark.Good => (PipitTheme.Warn, PipitTheme.Warn),
                NoteMark.Wrong => (PipitTheme.Bad, PipitTheme.Bad),
                NoteMark.Missed => (PipitTheme.InkSoft with { A = 0.35f }, PipitTheme.InkSoft),
                _ => (PipitTheme.Wood, PipitTheme.WoodEdge),
            };
            var box = new StyleBoxFlat
            {
                BgColor = fill, BorderColor = border,
                BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
                CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            };
            DrawStyleBox(box, rect);
            var label = NoteLabel(n.WrittenMidi);
            var size = (int)Math.Clamp(rowH * 0.6f, 11, 18);
            if (font.GetStringSize(label, HorizontalAlignment.Left, -1, size).X < rect.Size.X - 6)
                DrawString(font, new Vector2(rect.Position.X + 5, rect.Position.Y + rect.Size.Y / 2 + size * 0.36f), label,
                    HorizontalAlignment.Left, -1, size, mark is NoteMark.None or NoteMark.Missed ? PipitTheme.Ink : Colors.White);
        }

        // The player's pitch.
        Vector2? previous = null;
        foreach (var p in _trace)
        {
            if (float.IsNaN(p.Y) || p.X < firstVisible - 1 || p.X > lastVisible)
            {
                previous = null;
                continue;
            }
            var point = new Vector2(X(p.X), Y(p.Y));
            if (previous is { } prev && point.X >= labelW)
                DrawLine(prev, point, PipitTheme.Ink, 3, true);
            previous = point;
        }

        DrawLine(new Vector2(playX, 0), new Vector2(playX, Size.Y), PipitTheme.Accent, 3);

        if (Overlay != "")
            DrawString(font, new Vector2(labelW, Size.Y / 2 + 30), Overlay, HorizontalAlignment.Center, Size.X - labelW, 84, PipitTheme.Accent);
    }
}
