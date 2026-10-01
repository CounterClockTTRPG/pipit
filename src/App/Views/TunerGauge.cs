using System;
using Godot;
using Pipit.App.UI;

namespace Pipit.App.Views;

/// <summary>A ±50 cent arc with a needle; green inside the tolerance, amber to ±25, red beyond.</summary>
public partial class TunerGauge : Control
{
    private double _cents = double.NaN;
    private double _shown;

    public double Tolerance { get; set; } = 10;

    /// <summary>NaN hides the needle.</summary>
    public double Cents
    {
        get => _cents;
        set { _cents = value; QueueRedraw(); }
    }

    public TunerGauge() => CustomMinimumSize = new Vector2(360, 190);

    public override void _Process(double delta)
    {
        if (double.IsNaN(_cents))
            return;
        // Ease the needle so it reads as a needle, not a flicker.
        _shown += (Math.Clamp(_cents, -50, 50) - _shown) * Math.Min(1, delta * 14);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var center = new Vector2(Size.X / 2, Size.Y * 0.92f);
        var radius = Math.Min(Size.X / 2.2f, Size.Y * 0.85f);
        float Angle(double cents) => (float)(-Math.PI / 2 + cents / 50 * Math.PI * 0.4);

        void Band(double from, double to, Color color) =>
            DrawArc(center, radius, Angle(from), Angle(to), 24, color, radius * 0.12f, true);
        Band(-50, -25, PipitTheme.Bad with { A = 0.5f });
        Band(-25, -Tolerance, PipitTheme.Warn with { A = 0.5f });
        Band(-Tolerance, Tolerance, PipitTheme.Good with { A = 0.6f });
        Band(Tolerance, 25, PipitTheme.Warn with { A = 0.5f });
        Band(25, 50, PipitTheme.Bad with { A = 0.5f });

        var font = PipitTheme.Font;
        foreach (var tick in new[] { -50, -25, 0, 25, 50 })
        {
            var a = Angle(tick);
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            DrawLine(center + dir * radius * 0.8f, center + dir * radius * 0.88f, PipitTheme.InkSoft, 2, true);
            var label = tick == 0 ? "0" : tick.ToString("+0;-0");
            DrawString(font, center + dir * radius * 0.68f + new Vector2(-14, 6), label, HorizontalAlignment.Center, 28, 14, PipitTheme.InkSoft);
        }
        DrawString(font, center + new Vector2(-radius, -8), Ui.T("♭ low"), HorizontalAlignment.Left, -1, 16, PipitTheme.InkSoft);
        DrawString(font, center + new Vector2(radius - 60, -8), Ui.T("high ♯"), HorizontalAlignment.Right, 60, 16, PipitTheme.InkSoft);

        if (double.IsNaN(_cents))
            return;
        var na = Angle(_shown);
        var tip = center + new Vector2(Mathf.Cos(na), Mathf.Sin(na)) * radius * 0.95f;
        var color = Math.Abs(_cents) <= Tolerance ? PipitTheme.Good : Math.Abs(_cents) <= 25 ? PipitTheme.Warn : PipitTheme.Bad;
        DrawLine(center, tip, PipitTheme.Ink, 4, true);
        DrawCircle(center, 9, color);
    }
}
