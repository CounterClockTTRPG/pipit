using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.UI;

namespace Pipit.App.Views;

/// <summary>
/// Shows when to play and how long to hold: a row of beat dots (count-in dots numbered 1-4), a
/// big orange dot where a note starts with an orange bar across the beats it is held, small grey
/// dots for rests, a ball that bounces from dot to dot and lands exactly on each beat, and a big
/// cue above ("1".."4", "Play!", "hold…", "rest"). Beats are in capture-clock seconds.
/// </summary>
public partial class BeatIndicator : Control
{
    private IReadOnlyList<double> _beats = Array.Empty<double>();
    private int _countIn;
    // Notes as (first beat index, length in beats); null means every beat after the count-in is a short note.
    private IReadOnlyList<(int Beat, double Beats)>? _notes;

    /// <summary>How many dots to show around the current beat (long sessions scroll).</summary>
    public int Window { get; set; } = 12;

    public BeatIndicator() => CustomMinimumSize = new Vector2(400, 150);

    public void Start(IReadOnlyList<double> beats, int countIn, IReadOnlyList<(int Beat, double Beats)>? notes = null)
    {
        _beats = beats;
        _countIn = countIn;
        _notes = notes;
        Visible = true;
    }

    private bool StartsNote(int beat) => beat >= _countIn && (_notes is null || _notes.Any(n => n.Beat == beat));

    /// <summary>The note being held at a beat that is not its first beat, if any.</summary>
    private bool Holds(double beat) => _notes is not null && _notes.Any(n => beat > n.Beat && beat < n.Beat + n.Beats);

    public void Stop()
    {
        _beats = Array.Empty<double>();
        Visible = false;
    }

    private double BeatLength(int i) => i + 1 < _beats.Count ? _beats[i + 1] - _beats[i] : i > 0 ? _beats[i] - _beats[i - 1] : 1;

    public override void _Process(double delta)
    {
        if (_beats.Count > 0)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (_beats.Count == 0)
            return;
        var now = Services.Instance.Audio.Now;
        // Index of the latest beat that has happened (-1 before the first).
        var current = -1;
        while (current + 1 < _beats.Count && _beats[current + 1] <= now)
            current++;

        var first = Math.Clamp(current - 2, 0, Math.Max(0, _beats.Count - Window));
        var last = Math.Min(_beats.Count - 1, first + Window - 1);
        var spacing = Math.Min(70f, (Size.X - 40) / Math.Max(1, last - first));
        var x0 = (Size.X - spacing * (last - first)) / 2;
        var dotsY = Size.Y - 28;
        float X(int i) => x0 + (i - first) * spacing;
        var font = PipitTheme.Font;

        // Hold bars behind the dots: from a note's first beat to where it is released.
        if (_notes is not null)
            foreach (var (beat, length) in _notes)
            {
                if (beat + length < first || beat > last || length <= 1)
                    continue;
                var x1 = X(beat);
                var x2 = X(beat) + (float)(length * spacing) - spacing * 0.35f;
                DrawLine(new Vector2(x1, dotsY), new Vector2(Math.Min(x2, Size.X), dotsY), PipitTheme.Accent with { A = 0.55f }, 10, true);
                DrawLine(new Vector2(x2, dotsY - 9), new Vector2(x2, dotsY + 9), PipitTheme.Accent, 3, true); // release mark
            }

        for (var i = first; i <= last; i++)
        {
            var countIn = i < _countIn;
            var play = StartsNote(i);
            var held = !countIn && !play && Holds(i);
            var r = countIn ? 10f : play ? 15f : held ? 6f : 7f;
            var color = countIn ? PipitTheme.InkSoft : play || held ? PipitTheme.Accent : PipitTheme.WoodEdge;
            var done = i <= current;
            if (i == current)
            {
                // Pulse on the beat, shrinking back over 250 ms.
                var since = (float)(now - _beats[i]);
                r *= 1 + 0.6f * Math.Max(0, 1 - since / 0.25f);
            }
            if (done || held)
                DrawCircle(new Vector2(X(i), dotsY), r, color);
            DrawArc(new Vector2(X(i), dotsY), r, 0, Mathf.Tau, 32, color, 3, true);
            if (countIn)
                DrawString(font, new Vector2(X(i) - 6, dotsY + 6), (i + 1).ToString(), HorizontalAlignment.Left, -1, 16, done ? PipitTheme.Paper : PipitTheme.InkSoft);
        }

        // The bouncing ball: a parabola from the previous beat's dot to the next one.
        var next = current + 1;
        if (next < _beats.Count && next >= first && next <= last)
        {
            var from = current >= first ? X(current) : X(next) - spacing;
            var startTime = current >= 0 ? _beats[current] : _beats[next] - (_beats.Count > 1 ? _beats[1] - _beats[0] : 0.75);
            var p = (float)Math.Clamp((now - startTime) / (_beats[next] - startTime), 0, 1);
            var height = Math.Min(60f, Size.Y - 70);
            var ball = new Vector2(from + (X(next) - from) * p, dotsY - 22 - height * 4 * p * (1 - p));
            DrawCircle(ball, 9, PipitTheme.Ink);
        }

        // The big cue above.
        string cue;
        Color cueColor;
        var sinceBeat = current >= 0 ? now - _beats[current] : double.MaxValue;
        if (current < 0)
            (cue, cueColor) = (Ui.T("Get ready"), PipitTheme.InkSoft);
        else if (current < _countIn)
            (cue, cueColor) = ((current + 1).ToString(), PipitTheme.InkSoft); // "1, 2, 3, 4", like the dots
        else if (current >= _beats.Count - 1 && sinceBeat > 0.4)
            (cue, cueColor) = (Ui.T("Done"), PipitTheme.InkSoft);
        else if (StartsNote(current) && sinceBeat < 0.4)
            (cue, cueColor) = (Ui.T("Play!"), PipitTheme.Accent);
        else if (_notes is not null && Holds(current + sinceBeat / BeatLength(current)))
            (cue, cueColor) = (Ui.T("hold…"), PipitTheme.Accent);
        else if (_notes is not null)
            (cue, cueColor) = (Ui.T("rest"), PipitTheme.InkSoft);
        else
            (cue, cueColor) = ("", PipitTheme.Ink);
        if (cue != "")
            DrawString(font, new Vector2(0, 52), cue, HorizontalAlignment.Center, Size.X, 48, cueColor);
    }
}
