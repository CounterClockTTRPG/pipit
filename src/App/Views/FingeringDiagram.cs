using System;
using System.Collections.Generic;
using Godot;
using Pipit.App.UI;
using Pipit.Core.Instruments;

namespace Pipit.App.Views;

/// <summary>
/// Front view of a recorder with a finger drawn for every hole, as recorder method books do.
/// A covering finger rests its tip on the hole in its hand's colour (left blue, right brown); a
/// lifted finger is pale and stops short, leaving the hole white. Half-holing covers only the
/// larger of a double hole; the thumb, which plays the hole at the back, comes in from the other
/// side and covers half its hole when pinched. Each finger is labelled, and a legend sits below.
/// </summary>
public partial class FingeringDiagram : Control
{
    private Fingering? _fingering;
    private bool _keyed;

    private static readonly string[] FingerNames = { "index", "middle", "ring", "index", "middle", "ring", "little" };

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

    public FingeringDiagram() => CustomMinimumSize = new Vector2(240, 380);

    public override void _Draw()
    {
        const float legendH = 44;
        var w = Size.X;
        var h = Size.Y - legendH;
        var alpha = Dimmed ? 0.35f : 1f;
        var font = PipitTheme.Font;

        // Body: a tapered tube with a beak, a little right of centre to leave room for fingers.
        var bodyW = Math.Min(w * 0.2f, h * 0.15f);
        var cx = w * 0.56f;
        var top = h * 0.03f;
        var bottom = h * 0.97f;
        var body = new[]
        {
            new Vector2(cx - bodyW * 0.38f, top), new Vector2(cx + bodyW * 0.38f, top),
            new Vector2(cx + bodyW * 0.5f, top + h * 0.12f), new Vector2(cx + bodyW * 0.42f, bottom - h * 0.06f),
            new Vector2(cx + bodyW * 0.55f, bottom), new Vector2(cx - bodyW * 0.55f, bottom),
            new Vector2(cx - bodyW * 0.42f, bottom - h * 0.06f), new Vector2(cx - bodyW * 0.5f, top + h * 0.12f),
        };
        DrawColoredPolygon(body, PipitTheme.Wood with { A = alpha });
        DrawPolyline(new List<Vector2>(body) { body[0] }.ToArray(), PipitTheme.WoodEdge with { A = alpha }, 2, true);
        DrawRect(new Rect2(cx - bodyW * 0.18f, top + h * 0.015f, bodyW * 0.36f, h * 0.012f), PipitTheme.WoodEdge with { A = alpha });
        DrawRect(new Rect2(cx - bodyW * 0.22f, top + h * 0.08f, bodyW * 0.44f, h * 0.025f), PipitTheme.Ink with { A = alpha });

        var r = Math.Min(bodyW * 0.24f, h * 0.03f);
        var y0 = top + h * 0.22f;
        var step = (bottom - h * 0.1f - y0) / 7.6f;
        float HoleY(int i) => y0 + step * (i + (i >= 3 ? 0.6f : 0));

        // Holes first (white when open), fingers on top.
        for (var i = 0; i < 7; i++)
        {
            var c = new Vector2(cx, HoleY(i));
            if (i >= 5 && !_keyed)
            {
                Hole(Big(c, r), r * 0.75f, alpha);
                Hole(Small(c, r), r * 0.5f, alpha);
            }
            else
                Hole(c, r, alpha);
        }
        var thumbHole = new Vector2(cx + bodyW * 0.5f + r * 1.8f, y0 - step * 0.5f);
        Hole(thumbHole, r, alpha);
        DrawString(font, new Vector2(thumbHole.X - 40, thumbHole.Y - r * 1.6f), Ui.T("thumb (back)"), HorizontalAlignment.Center, 80, 12, PipitTheme.InkSoft with { A = alpha });

        if (_fingering is null)
            return;

        // Fingers from the left for holes 1-7.
        var thickness = r * 1.9f;
        var baseX = 4f;
        for (var i = 0; i < 7; i++)
        {
            var c = new Vector2(cx, HoleY(i));
            var hand = (i < 3 ? PipitTheme.LeftHand : PipitTheme.RightHand) with { A = alpha };
            var state = _fingering.Holes[i];
            var isDouble = i >= 5 && !_keyed;
            var (tip, tipR) = state switch
            {
                HoleState.Closed when isDouble => (c with { X = c.X + r * 0.05f }, r * 1.3f),
                HoleState.Closed => (c, r * 1.15f),
                HoleState.Half => (Big(c, r) with { X = Big(c, r).X - r * 0.15f }, r * 0.85f),
                _ => (c with { X = c.X - (isDouble ? r * 3.0f : r * 2.6f) }, r * 0.95f),
            };
            var lifted = state == HoleState.Open;
            Finger(new Vector2(baseX, c.Y), tip, thickness, tipR, lifted ? Pale(hand) : hand, FingerNames[i], lifted, alpha);
        }

        // The thumb from the right, at the back.
        var thumbColor = PipitTheme.LeftHand with { A = alpha };
        var thumbBase = new Vector2(w - 4, thumbHole.Y);
        switch (_fingering.Thumb)
        {
            case HoleState.Closed:
                Finger(thumbBase, thumbHole, thickness, r * 1.15f, thumbColor, null, false, alpha);
                break;
            case HoleState.Pinched:
                // Rolled back: the tip covers only the lower half; the slash marks "pinched".
                Finger(thumbBase, thumbHole + new Vector2(r * 0.5f, r * 0.75f), thickness, r * 0.95f, thumbColor, null, false, alpha);
                DrawLine(thumbHole + new Vector2(-r * 1.3f, r * 1.3f), thumbHole + new Vector2(r * 1.3f, -r * 1.3f), PipitTheme.Ink with { A = alpha }, 2.5f, true);
                break;
            default:
                Finger(thumbBase, thumbHole + new Vector2(r * 2.6f, 0), thickness, r * 0.95f, Pale(thumbColor), null, true, alpha);
                break;
        }

        if (_fingering.BellCovered)
            DrawArc(new Vector2(cx, bottom + 2), bodyW * 0.6f, 0, Mathf.Pi, 16, PipitTheme.Bad with { A = alpha }, 3, true);

        DrawLegend(Size.Y - legendH / 2, r);
    }

    private static Vector2 Big(Vector2 c, float r) => new(c.X - r * 0.45f, c.Y);
    private static Vector2 Small(Vector2 c, float r) => new(c.X + r * 0.75f, c.Y);

    private static Color Pale(Color c) => c.Lerp(Colors.White, 0.65f);

    private void Hole(Vector2 c, float r, float alpha)
    {
        DrawCircle(c, r, Colors.White with { A = alpha });
        DrawArc(c, r, 0, Mathf.Tau, 32, PipitTheme.Ink with { A = alpha }, 2, true);
    }

    /// <summary>A finger: a band from its base to a rounded tip, with its name written along it.</summary>
    private void Finger(Vector2 from, Vector2 tip, float thickness, float tipR, Color color, string? name, bool lifted, float alpha)
    {
        var dir = (tip - from).Normalized();
        var normal = new Vector2(-dir.Y, dir.X) * thickness / 2;
        var tipBase = tip - dir * tipR * 0.6f;
        DrawColoredPolygon(new[] { from + normal, tipBase + normal, tipBase - normal, from - normal }, color);
        DrawCircle(tip, tipR, color);
        var edge = (lifted ? PipitTheme.InkSoft : PipitTheme.Ink) with { A = alpha * (lifted ? 0.5f : 0.8f) };
        DrawLine(from + normal, tipBase + normal, edge, 1.5f, true);
        DrawLine(from - normal, tipBase - normal, edge, 1.5f, true);
        DrawArc(tip, tipR, dir.Angle() - 1.25f, dir.Angle() + 1.25f, 16, edge, 1.5f, true);

        // Name along the finger, near its base (fingers from the right are labelled from the right).
        if (name is null)
            return;
        var size = (int)Math.Clamp(thickness * 0.55f, 10, 14);
        var text = Ui.T(name);
        var width = PipitTheme.Font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var textX = dir.X >= 0 ? from.X + 4 : from.X - 4 - width;
        var textColor = lifted ? PipitTheme.InkSoft with { A = alpha } : Colors.White with { A = alpha };
        DrawString(PipitTheme.Font, new Vector2(textX, from.Y + size * 0.36f), text, HorizontalAlignment.Left, -1, size, textColor);
    }

    private void DrawLegend(float y, float r)
    {
        var font = PipitTheme.Font;
        var x = 4f;
        var lr = 7f;
        y -= 9;
        foreach (var (color, text) in new[] { (PipitTheme.LeftHand, Ui.T("left hand")), (PipitTheme.RightHand, Ui.T("right hand")), (Pale(PipitTheme.LeftHand), Ui.T("lifted")) })
        {
            var itemW = lr * 2 + 8 + font.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X;
            if (x > 4 && x + itemW > Size.X)
            {
                x = 4;
                y += 20; // wrap onto a second line in narrow layouts
            }
            DrawCircle(new Vector2(x + lr, y), lr, color);
            DrawString(font, new Vector2(x + lr * 2 + 4, y + 5), text, HorizontalAlignment.Left, -1, 13, PipitTheme.InkSoft);
            x += itemW + 10;
        }
    }
}
