using System;
using System.Collections.Generic;
using Godot;
using Pipit.App.UI;
using Pipit.Core.Instruments;

namespace Pipit.App.Views;

/// <summary>
/// Front view of a recorder with the thumb hole drawn beside hole 1. Covered holes are filled in
/// their hand's colour (left blue, right brown) and open holes are white, so an open hole never
/// blends into the wood. A half-covered double hole has one of its pair filled; the pinched
/// thumb is half filled with a slash. The legend underneath says what the colours mean.
/// (A version with drawn fingers was tried and judged busier than this one.)
/// </summary>
public partial class FingeringDiagram : Control
{
    private Fingering? _fingering;
    private bool _keyed;

    public Fingering? Fingering
    {
        get => _fingering;
        set { _fingering = value; QueueRedraw(); }
    }

    public bool Keyed
    {
        get => _keyed;
        set { _keyed = value; QueueRedraw(); }
    }

    /// <summary>Greys the diagram out (for "no note" states) without hiding it.</summary>
    public bool Dimmed { get; set; }

    public FingeringDiagram() => CustomMinimumSize = new Vector2(190, 380);

    public override void _Draw()
    {
        var w = Size.X;
        const float legendH = 44;
        var h = Size.Y - legendH;
        // Body: a tapered tube with a beak at the top.
        var bodyW = Math.Min(w * 0.42f, h * 0.16f);
        var cx = w * 0.58f;
        var top = h * 0.03f;
        var bottom = h * 0.97f;
        var alpha = Dimmed ? 0.35f : 1f;
        var wood = PipitTheme.Wood with { A = alpha };
        var edge = PipitTheme.WoodEdge with { A = alpha };

        var body = new[]
        {
            new Vector2(cx - bodyW * 0.38f, top), new Vector2(cx + bodyW * 0.38f, top),
            new Vector2(cx + bodyW * 0.5f, top + h * 0.12f), new Vector2(cx + bodyW * 0.42f, bottom - h * 0.06f),
            new Vector2(cx + bodyW * 0.55f, bottom), new Vector2(cx - bodyW * 0.55f, bottom),
            new Vector2(cx - bodyW * 0.42f, bottom - h * 0.06f), new Vector2(cx - bodyW * 0.5f, top + h * 0.12f),
        };
        DrawColoredPolygon(body, wood);
        var outline = new List<Vector2>(body) { body[0] };
        DrawPolyline(outline.ToArray(), edge, 2, true);
        // Windway and labium.
        DrawRect(new Rect2(cx - bodyW * 0.18f, top + h * 0.015f, bodyW * 0.36f, h * 0.012f), edge);
        DrawRect(new Rect2(cx - bodyW * 0.22f, top + h * 0.08f, bodyW * 0.44f, h * 0.025f), PipitTheme.Ink with { A = alpha });

        if (_fingering is null)
            return;

        // Holes 1-3 (left hand) and 4-7 (right hand), with a gap between the hands.
        var r = Math.Min(bodyW * 0.22f, h * 0.03f);
        var y0 = top + h * 0.22f;
        var step = (bottom - h * 0.1f - y0) / 7.6f;
        float HoleY(int i) => y0 + step * (i + (i >= 3 ? 0.6f : 0));
        for (var i = 0; i < 7; i++)
        {
            var color = (i < 3 ? PipitTheme.LeftHand : PipitTheme.RightHand) with { A = alpha };
            var state = _fingering.Holes[i];
            if (i >= 5 && !_keyed)
                DoubleHole(new Vector2(cx, HoleY(i)), r, state, color);
            else
                Hole(new Vector2(cx, HoleY(i)), r, state == HoleState.Closed ? 1 : state == HoleState.Half ? 0.5f : 0, color);
        }

        var font = PipitTheme.Font;

        // Thumb hole: beside the body at the height of hole 1, labelled.
        var thumb = new Vector2(cx - bodyW * 0.5f - r * 2.2f, y0 - step * 0.35f);
        var fill = _fingering.Thumb switch { HoleState.Closed => 1f, HoleState.Pinched => 0.5f, _ => 0f };
        Hole(thumb, r, fill, PipitTheme.LeftHand with { A = alpha });
        if (_fingering.Thumb == HoleState.Pinched)
            DrawLine(thumb + new Vector2(-r * 1.2f, r * 1.2f), thumb + new Vector2(r * 1.2f, -r * 1.2f), PipitTheme.Ink with { A = alpha }, 2, true);
        DrawString(font, thumb + new Vector2(-r * 0.6f, -r * 1.6f), "Ø", HorizontalAlignment.Left, -1, (int)(r * 1.3f), PipitTheme.InkSoft with { A = alpha });

        if (_fingering.BellCovered)
            DrawArc(new Vector2(cx, bottom + 2), bodyW * 0.6f, 0, Mathf.Pi, 16, PipitTheme.Bad with { A = alpha }, 3, true);

        // Legend: covered, open, half.
        var ly = Size.Y - legendH / 2;
        var lr = 7f;
        var lx = 4f;
        ly -= 9;
        foreach (var (f, color, text) in new[] { (1f, PipitTheme.LeftHand, Ui.T("left hand")), (1f, PipitTheme.RightHand, Ui.T("right hand")), (0f, PipitTheme.Ink, Ui.T("open")), (0.5f, PipitTheme.Ink, Ui.T("half")) })
        {
            var itemW = lr * 2 + 4 + font.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X;
            if (lx > 4 && lx + itemW > Size.X)
            {
                lx = 4;
                ly += 20; // wrap onto a second line in narrow layouts
            }
            Hole(new Vector2(lx + lr, ly), lr, f, color);
            DrawString(font, new Vector2(lx + lr * 2 + 4, ly + 5), text, HorizontalAlignment.Left, -1, 13, PipitTheme.InkSoft);
            lx += itemW + 10;
        }
    }

    private void Hole(Vector2 c, float r, float fill, Color color)
    {
        // Open holes are white so they stand out from the wood as clearly as covered ones.
        DrawCircle(c, r, Colors.White with { A = color.A });
        if (fill >= 1)
            DrawCircle(c, r, color);
        else if (fill > 0)
            DrawColoredPolygon(HalfDisc(c, r), color);
        DrawArc(c, r, 0, Mathf.Tau, 32, PipitTheme.Ink with { A = color.A }, 2, true);
    }

    /// <summary>Two small holes side by side; Half covers the larger (left) one only.</summary>
    private void DoubleHole(Vector2 c, float r, HoleState state, Color color)
    {
        var big = new Vector2(c.X - r * 0.45f, c.Y);
        var small = new Vector2(c.X + r * 0.75f, c.Y);
        Hole(big, r * 0.75f, state is HoleState.Closed or HoleState.Half ? 1 : 0, color);
        Hole(small, r * 0.5f, state == HoleState.Closed ? 1 : 0, color);
    }

    private static Vector2[] HalfDisc(Vector2 c, float r)
    {
        // The lower half: pinching opens the top of the thumb hole.
        var points = new Vector2[18];
        for (var i = 0; i < points.Length; i++)
        {
            var a = Mathf.Pi * i / (points.Length - 1);
            points[i] = c + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }
        return points;
    }
}
