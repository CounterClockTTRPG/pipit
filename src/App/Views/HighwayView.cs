using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.UI;

namespace Pipit.App.Views;

/// <summary>
/// The falling-note highway: one lane per note the song uses, low to high from left to right,
/// each labelled at the hit line. Notes fall towards the line and must be played as they reach
/// it; their length shows how long to hold them. The lane being played lights up, and every
/// judged note pops up its result.
/// </summary>
public partial class HighwayView : Control, ISongView
{
    private IReadOnlyList<BarNote> _notes = Array.Empty<BarNote>();
    private readonly List<int> _lanes = new();
    private readonly Dictionary<int, NoteMark> _marks = new();
    private readonly List<(int Lane, string Text, Color Color, double Age)> _popups = new();
    private double _heard = double.NaN;
    private double _heardAt;

    public double CurrentBeat { get; set; }
    public int? WaitingFor { get; set; }
    public string Overlay { get; set; } = "";
    public float PixelsPerBeat { get; set; } = 110;
    public Func<int, string> NoteLabel { get; set; } = m => m.ToString();
    /// <summary>Shown top right: score, combo (set by the play screen).</summary>
    public string Hud { get; set; } = "";

    public HighwayView()
    {
        ClipContents = true;
        CustomMinimumSize = new Vector2(500, 260);
    }

    public void SetNotes(IReadOnlyList<BarNote> notes)
    {
        _notes = notes;
        _lanes.Clear();
        _lanes.AddRange(notes.Select(n => n.WrittenMidi).Distinct().OrderBy(m => m));
        ClearMarks();
    }

    public void SetMark(int index, NoteMark mark)
    {
        var first = !_marks.ContainsKey(index);
        _marks[index] = mark;
        if (!first || mark is NoteMark.Current or NoteMark.None || index >= _notes.Count)
            return;
        var (text, color) = mark switch
        {
            NoteMark.Perfect => (Ui.T("Perfect"), PipitTheme.Good),
            NoteMark.Good => (Ui.T("Good"), PipitTheme.Warn),
            NoteMark.Wrong => (Ui.T("Wrong note"), PipitTheme.Bad),
            _ => (Ui.T("Miss"), PipitTheme.InkSoft),
        };
        _popups.Add((_lanes.IndexOf(_notes[index].WrittenMidi), text, color, 0));
    }

    public void ClearMarks()
    {
        _marks.Clear();
        _popups.Clear();
    }

    public void AddTrace(double beat, double writtenMidi)
    {
        _heard = writtenMidi;
        _heardAt = Time.GetTicksMsec() / 1000.0;
    }

    public override void _Process(double delta)
    {
        for (var i = _popups.Count - 1; i >= 0; i--)
        {
            var p = _popups[i];
            p.Age += delta;
            if (p.Age > 0.9)
                _popups.RemoveAt(i);
            else
                _popups[i] = p;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), PipitTheme.Ink with { A = 0.92f });
        if (_lanes.Count == 0)
            return;
        var font = PipitTheme.Font;
        var laneW = Math.Min(110f, (Size.X - 40) / _lanes.Count);
        var left = (Size.X - laneW * _lanes.Count) / 2;
        var hitY = Size.Y - 64;
        float LaneX(int lane) => left + lane * laneW;
        float Y(double beat) => (float)(hitY - (beat - CurrentBeat) * PixelsPerBeat);

        // The lane being played now (within the last 0.15 s).
        var heardLane = -1;
        if (!double.IsNaN(_heard) && Time.GetTicksMsec() / 1000.0 - _heardAt < 0.15)
            heardLane = _lanes.IndexOf((int)Math.Round(_heard));

        for (var l = 0; l < _lanes.Count; l++)
        {
            var x = LaneX(l);
            var natural = Services.Spell(_lanes[l], Services.Instance.PreferFlats).Alter == 0;
            DrawRect(new Rect2(x + 2, 0, laneW - 4, Size.Y), (l == heardLane ? PipitTheme.Accent with { A = 0.28f } : Colors.White with { A = natural ? 0.06f : 0.03f }));
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), Colors.White with { A = 0.08f }, 1);
        }

        // Notes, falling. Their length is how long to hold; a sounding note shrinks into the
        // hit line and is gone when it ends.
        var font16 = (int)Math.Min(18, laneW * 0.22f);
        foreach (var n in _notes)
        {
            if (n.StartBeat + n.Beats < CurrentBeat - 0.05)
                continue;
            var y1 = Math.Min(Y(n.StartBeat), hitY);
            var y2 = Y(n.StartBeat + n.Beats) + 4;
            if (y1 < -20 || y2 > hitY)
                continue;
            var lane = _lanes.IndexOf(n.WrittenMidi);
            var mark = _marks.TryGetValue(n.Index, out var m) ? m : NoteMark.None;
            var sounding = CurrentBeat >= n.StartBeat && CurrentBeat < n.StartBeat + n.Beats;
            if (WaitingFor == n.Index || mark == NoteMark.None && sounding && WaitingFor is null)
                mark = NoteMark.Current;
            var color = mark switch
            {
                NoteMark.Perfect => PipitTheme.Good,
                NoteMark.Good => PipitTheme.Warn,
                NoteMark.Wrong => PipitTheme.Bad,
                NoteMark.Missed => PipitTheme.InkSoft with { A = 0.5f },
                NoteMark.Current => PipitTheme.Accent,
                _ => PipitTheme.Wood,
            };
            var rect = new Rect2(LaneX(lane) + 8, y2, laneW - 16, Math.Max(10, y1 - y2));
            DrawStyleBox(new StyleBoxFlat
            {
                BgColor = color, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
                BorderColor = color.Lightened(0.3f), BorderWidthBottom = 3,
            }, rect);
            if (rect.Size.Y > font16 + 6)
                DrawString(font, new Vector2(rect.Position.X, rect.End.Y - 7), NoteLabel(n.WrittenMidi), HorizontalAlignment.Center, rect.Size.X, font16,
                    mark is NoteMark.None ? PipitTheme.Ink : Colors.White);
        }

        // Hit line and lane names.
        DrawRect(new Rect2(left, hitY - 3, laneW * _lanes.Count, 6), PipitTheme.Accent);
        for (var l = 0; l < _lanes.Count; l++)
        {
            var label = NoteLabel(_lanes[l]);
            DrawString(font, new Vector2(LaneX(l), hitY + 34), label, HorizontalAlignment.Center, laneW, (int)Math.Min(24, laneW * 0.3f), l == heardLane ? PipitTheme.Accent : PipitTheme.Paper);
        }

        foreach (var (lane, text, color, age) in _popups)
        {
            var y = hitY - 30 - (float)age * 60;
            DrawString(font, new Vector2(LaneX(lane) - 30, y), text, HorizontalAlignment.Center, laneW + 60, 20, color with { A = (float)(1 - age / 0.9) });
        }

        if (Hud != "")
            DrawString(font, new Vector2(16, 34), Hud, HorizontalAlignment.Right, Size.X - 32, 24, PipitTheme.Paper);
        if (Overlay != "")
            DrawString(font, new Vector2(0, Size.Y / 2 + 30), Overlay, HorizontalAlignment.Center, Size.X, 84, PipitTheme.Accent);
    }
}
