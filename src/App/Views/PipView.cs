using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Pipit.App.Views;

public enum PipMood { Idle, Listening, Singing, Cheering, Thinking, Dozing }

/// <summary>
/// Pip, the meadow pipit who guides the player. Drawn in code as a cut-out rig of vector parts
/// (tail, body, belly, wing, head, eyebrow, eye, beak, legs) in a 200 x 200 design space,
/// scaled to the control. Each mood sets a handful of pose targets that the rig eases towards;
/// breathing, blinking and the pipit's tail bob run underneath. Pip's breast streaks are small
/// note shapes, a nod to the app.
/// </summary>
public partial class PipView : Control
{
    // Plumage, from the app palette (see docs/pip.md).
    private static readonly Color Back = new("8a6a3f");
    private static readonly Color BackLight = new("a8865a");
    private static readonly Color Ink = new("4a3423");
    private static readonly Color Belly = new("f3dcae");
    private static readonly Color Cream = new("fcf4e5");
    private static readonly Color Beak = new("d9822b");
    private static readonly Color Legs = new("e4a07a");
    private static readonly Color Blush = new("e8a27a");

    private PipMood _mood = PipMood.Idle;
    private double _time;
    private double _nextBlink = 2.5;
    private double _blink;          // 0 open .. 1 closed (blink only)
    private double _nextBob = 4;
    private double _bob;            // tail bob phase, 0 when at rest
    private readonly List<(Vector2 Pos, double Age, string Glyph)> _floaters = new();
    private double _nextFloater;
    private double _moodUntil = double.PositiveInfinity;
    private PipMood _after = PipMood.Idle;

    // Pose, eased towards the mood's targets.
    private Pose _pose = Pose.For(PipMood.Idle);

    public PipMood Mood => _mood;

    /// <summary>Changes mood; with a duration Pip returns to <paramref name="after"/> afterwards.</summary>
    public void SetMood(PipMood mood, double seconds = double.PositiveInfinity, PipMood after = PipMood.Idle)
    {
        _mood = mood;
        _moodUntil = _time + seconds;
        _after = after;
    }

    public PipView() => CustomMinimumSize = new Vector2(120, 120);

    private readonly record struct Pose(float HeadTilt, float HeadDrop, float EyeOpen, float Brow, float BrowTilt, float LookX, float LookY,
        float Wing, float Hop, float Beak, bool HappyEyes)
    {
        public static Pose For(PipMood mood) => mood switch
        {
            PipMood.Listening => new(-0.38f, 0, 1.2f, 5, 0.15f, 1.2f, 0.4f, 0.1f, 0, 0, false),
            PipMood.Singing => new(-0.22f, -3, 0.8f, 2.5f, 0, 0.3f, -0.4f, 0.3f, 0, 1, false),
            PipMood.Cheering => new(-0.12f, -4, 1, 5, 0, 0, 0, 1, 1, 0.7f, true),
            PipMood.Thinking => new(0.3f, 0, 1, 2.5f, -0.6f, -1.6f, -2.2f, 0.2f, 0, 0, false),
            PipMood.Dozing => new(0.18f, 11, 0, -1.5f, 0, 0, 0.5f, 0, 0, 0, false),
            _ => new(0, 0, 1, 0, 0, 0.35f, 0, 0, 0, 0, false),
        };

        public Pose Lerp(Pose to, float t) => new(
            Mathf.Lerp(HeadTilt, to.HeadTilt, t), Mathf.Lerp(HeadDrop, to.HeadDrop, t), Mathf.Lerp(EyeOpen, to.EyeOpen, t),
            Mathf.Lerp(Brow, to.Brow, t), Mathf.Lerp(BrowTilt, to.BrowTilt, t), Mathf.Lerp(LookX, to.LookX, t), Mathf.Lerp(LookY, to.LookY, t),
            Mathf.Lerp(Wing, to.Wing, t), Mathf.Lerp(Hop, to.Hop, t), Mathf.Lerp(Beak, to.Beak, t), t > 0.5f ? to.HappyEyes : HappyEyes);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_time >= _moodUntil)
            SetMood(_after);
        _pose = _pose.Lerp(Pose.For(_mood), (float)Math.Min(1, delta * 7));

        // Blinks (not while dozing or squinting happily).
        if (_time >= _nextBlink && _mood is not (PipMood.Dozing or PipMood.Cheering))
        {
            _blink = 1;
            _nextBlink = _time + 2.5 + GD.Randf() * 3;
        }
        _blink = Math.Max(0, _blink - delta * 7);

        // The pipit's tail bob, now and then when calm.
        if (_time >= _nextBob && _mood is PipMood.Idle or PipMood.Listening)
        {
            _bob = 1;
            _nextBob = _time + 4 + GD.Randf() * 5;
        }
        _bob = Math.Max(0, _bob - delta * 2.5);

        // Notes rise while singing or cheering, z's while dozing.
        if (_time >= _nextFloater && _mood is PipMood.Singing or PipMood.Cheering or PipMood.Dozing)
        {
            var glyph = _mood == PipMood.Dozing ? "z" : GD.Randf() < 0.5f ? "♪" : "♫";
            _floaters.Add((new Vector2(172, 62), 0, glyph));
            _nextFloater = _time + (_mood == PipMood.Dozing ? 1.3 : 0.55);
        }
        for (var i = _floaters.Count - 1; i >= 0; i--)
        {
            var f = _floaters[i];
            f.Age += delta;
            if (f.Age > 2.2)
                _floaters.RemoveAt(i);
            else
                _floaters[i] = f;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Fit the 200 x 200 design space into the control, centred.
        var scale = Math.Min(Size.X, Size.Y) / 200f;
        var origin = new Vector2((Size.X - 200 * scale) / 2, (Size.Y - 200 * scale) / 2);
        Vector2 P(float x, float y) => origin + new Vector2(x, y) * scale;
        var p = _pose;
        var t = (float)_time;

        // Whole-body motion: breathing, a hop when cheering.
        var breath = (float)Math.Sin(t * (_mood == PipMood.Dozing ? 1.6 : 2.6)) * (_mood == PipMood.Dozing ? 1.6f : 0.9f);
        var hop = p.Hop * Math.Max(0, (float)Math.Sin(t * 7)) * 14;
        var lift = new Vector2(0, -hop);
        Vector2 B(float x, float y) => P(x, y + breath * 0.4f) + lift * scale;

        // Legs (behind the body).
        var legTop = 158f;
        foreach (var lx in new[] { 96f, 110f })
        {
            DrawLine(B(lx, legTop), P(lx - 2, 186) + lift * scale * 0.3f, Legs, 3.2f * scale, true);
            DrawLine(P(lx - 2, 186) + lift * scale * 0.3f, P(lx + 8, 188) + lift * scale * 0.3f, Legs, 2.6f * scale, true);
            DrawLine(P(lx - 2, 186) + lift * scale * 0.3f, P(lx - 11, 189) + lift * scale * 0.3f, Legs, 2.4f * scale, true); // long hind claw
        }

        // Tail: a narrow fan of three feathers, carried slightly up, white along the outer edge;
        // it dips on a bob.
        var bob = (float)Math.Sin(_bob * Math.PI) * 9;
        foreach (var (tipY, shade) in new[] { (144f, BackLight), (153f, Back), (162f, Back.Darkened(0.12f)) })
        {
            var feather = Bezier(B(84, 128), B(60, 132), B(34, tipY - 8 + bob), B(18, tipY + bob), 8)
                .Concat(Bezier(B(18, tipY + bob), B(22, tipY + 8 + bob), B(54, 150), B(86, 146), 8)).ToArray();
            DrawColoredPolygon(feather, shade);
            DrawPolyline(feather.Append(feather[0]).ToArray(), Ink, 1.4f * scale, true);
        }
        DrawPolyline(Bezier(B(22, 170 + bob), B(40, 164 + bob), B(62, 154), B(84, 148), 8).ToArray(), Cream, 2.4f * scale, true);

        // Body: a forward-leaning teardrop.
        var body = Bezier(B(140, 104), B(146, 150), B(110, 168), B(84, 160))
            .Concat(Bezier(B(84, 160), B(60, 152), B(58, 120), B(80, 100)))
            .Concat(Bezier(B(80, 100), B(98, 84), B(126, 82), B(140, 104))).ToArray();
        DrawColoredPolygon(body, Back);

        // Belly and breast, buff, with note-shaped streaks.
        var belly = Bezier(B(141, 106), B(146, 146), B(118, 164), B(96, 158))
            .Concat(Bezier(B(96, 158), B(102, 136), B(112, 112), B(141, 106))).ToArray();
        DrawColoredPolygon(belly, Belly);
        foreach (var (x, y, s) in new[] { (126f, 116f, 1f), (136f, 124f, 0.9f), (118f, 130f, 1f), (130f, 138f, 0.85f), (114f, 146f, 0.8f), (124f, 150f, 0.7f) })
            Streak(B(x, y), s * scale);
        DrawPolyline(body.Append(body[0]).ToArray(), Ink, 1.8f * scale, true);

        // Wing: folded on the side, lifting (and flapping when cheering).
        var flap = p.Wing * (_mood == PipMood.Cheering ? 0.65f + 0.35f * (float)Math.Sin(t * 14) : 1);
        var wingPivot = B(92, 108);
        Vector2 W(float x, float y) => Rotate(B(x, y), wingPivot, -flap * 1.1f);
        var wingPoints = Bezier(W(92, 106), W(118, 100), W(130, 118), W(116, 134), 10)
            .Concat(Bezier(W(116, 134), W(100, 146), W(84, 152), W(66, 154), 10))
            // Three scalloped feather tips at the back.
            .Concat(Bezier(W(66, 154), W(64, 148), W(70, 146), W(72, 146), 4))
            .Concat(Bezier(W(72, 146), W(66, 142), W(70, 136), W(74, 136), 4))
            .Concat(Bezier(W(74, 136), W(68, 130), W(74, 122), W(78, 122), 4))
            .Concat(Bezier(W(78, 122), W(80, 114), W(86, 108), W(92, 106), 6)).ToArray();
        DrawColoredPolygon(wingPoints, BackLight);
        DrawPolyline(wingPoints.Append(wingPoints[0]).ToArray(), Ink, 1.6f * scale, true);
        // Two pale wing bars, following the curve of the wing.
        DrawPolyline(Bezier(W(84, 120), W(96, 114), W(110, 114), W(120, 118), 8).ToArray(), Cream, 2.4f * scale, true);
        DrawPolyline(Bezier(W(78, 134), W(90, 128), W(104, 128), W(114, 132), 8).ToArray(), Cream, 2.4f * scale, true);

        // Head, rotating about the neck.
        var neck = B(116, 100);
        var tilt = p.HeadTilt + (_mood == PipMood.Singing ? (float)Math.Sin(t * 3) * 0.05f : 0);
        Vector2 H(float x, float y) => Rotate(B(x, y + p.HeadDrop), neck, tilt);
        var head = Circle(H(128, 76), 30 * scale);
        DrawColoredPolygon(head, Back);
        // Pale throat and cheek.
        DrawColoredPolygon(Ellipse(H(138, 92), 15 * scale, 10 * scale, tilt), Belly);
        DrawColoredPolygon(Ellipse(H(134, 90), 6 * scale, 3.5f * scale, tilt), Blush with { A = 0.55f });
        DrawPolyline(head.Append(head[0]).ToArray(), Ink, 1.8f * scale, true);
        // Crown streaks.
        foreach (var (x1, y1, x2, y2) in new[] { (112f, 54f, 118f, 50f), (122f, 50f, 128f, 47f), (133f, 50f, 139f, 48f), (106f, 64f, 111f, 59f) })
            DrawLine(H(x1, y1), H(x2, y2), Ink with { A = 0.7f }, 2 * scale, true);

        // Beak: upper and lower halves; the lower one opens to sing.
        var open = p.Beak * (_mood == PipMood.Singing ? 0.35f + 0.65f * Math.Abs((float)Math.Sin(t * 6)) : 0.6f);
        DrawColoredPolygon(new[] { H(154, 76), H(178, 82), H(154, 85) }, Beak);
        DrawColoredPolygon(new[] { H(154, 85), H(173, 86 + open * 12), H(153, 91) }, Beak.Darkened(0.12f));
        DrawPolyline(new[] { H(154, 76), H(178, 82), H(154, 85) }, Ink, 1.4f * scale, true);

        // Eye: a big dark eye with a highlight, an eyelid for blinks and sleep.
        var eye = H(140 + p.LookX, 75 + p.LookY);
        var openness = Math.Clamp(p.EyeOpen * (1 - (float)_blink), 0, 1.2f);
        if (p.HappyEyes)
            DrawArc(eye + new Vector2(0, 2 * scale), 6 * scale, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 12, Ink, 2.6f * scale, true);
        else if (openness < 0.15f)
            DrawLine(eye + new Vector2(-6, 1) * scale, eye + new Vector2(6, 1) * scale, Ink, 2.4f * scale, true);
        else
        {
            DrawColoredPolygon(Ellipse(eye, 7 * scale, 8 * scale * openness, tilt), Ink);
            DrawCircle(eye + new Vector2(2.2f, -2.6f * openness) * scale, 2.3f * scale, Colors.White);
        }

        // The eyebrow (a pipit's pale supercilium) carries the expression.
        var browLift = p.Brow + (_mood == PipMood.Listening ? (float)Math.Sin(t * 2) * 0.8f : 0);
        var brow = new[] { H(124, 66 - browLift * 0.6f), H(138, 62 - browLift + p.BrowTilt * 8), H(152, 66 - browLift * 0.8f) };
        DrawPolyline(brow, Cream, 3.6f * scale, true);

        // Floating notes and z's.
        var font = UI.PipitTheme.Font; // ♪ ♫ come from the Segoe UI Symbol fallback
        foreach (var (pos, age, glyph) in _floaters)
        {
            var rise = (float)age * 34;
            var drift = (float)Math.Sin(age * 3 + pos.X) * 6;
            var a = (float)Math.Clamp(1.2 - age / 2.0, 0, 1);
            var color = (glyph == "z" ? Ink : Beak) with { A = a };
            DrawString(font, P(pos.X + drift + (float)age * 10, pos.Y - rise) + lift * scale, glyph, HorizontalAlignment.Left, -1, (int)(18 * scale), color);
        }
    }

    /// <summary>A small note-shaped streak: a tilted oval head and a short stem.</summary>
    private void Streak(Vector2 at, float s)
    {
        DrawColoredPolygon(Ellipse(at, 3.4f * s, 2.4f * s, -0.45f), Ink with { A = 0.85f });
        DrawLine(at + new Vector2(2.9f, -0.8f) * s, at + new Vector2(2.9f, -8.5f) * s, Ink with { A = 0.85f }, 1.3f * s, true);
    }

    private static IEnumerable<Vector2> Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, int steps = 14)
    {
        for (var i = 0; i < steps; i++)
        {
            var t = i / (float)steps;
            var u = 1 - t;
            yield return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
        }
    }

    private static Vector2[] Circle(Vector2 c, float r) => Ellipse(c, r, r, 0);

    private static Vector2[] Ellipse(Vector2 c, float rx, float ry, float rotation)
    {
        var points = new Vector2[28];
        for (var i = 0; i < points.Length; i++)
        {
            var a = Mathf.Tau * i / points.Length;
            points[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry).Rotated(rotation);
        }
        return points;
    }

    private static Vector2 Rotate(Vector2 point, Vector2 pivot, float angle) => pivot + (point - pivot).Rotated(angle);
}
