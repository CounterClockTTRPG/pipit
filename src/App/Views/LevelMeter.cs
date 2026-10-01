using System;
using Godot;
using Pipit.App.UI;

namespace Pipit.App.Views;

/// <summary>Horizontal input level, -60..0 dBFS, with an optional marker for the noise gate.</summary>
public partial class LevelMeter : Control
{
    public double Db { get; set; } = -120;
    public double? GateDb { get; set; }

    public LevelMeter() => CustomMinimumSize = new Vector2(200, 18);

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, PipitTheme.PaperDark);
        float X(double db) => (float)(Math.Clamp((db + 60) / 60, 0, 1) * Size.X);
        var color = Db > -6 ? PipitTheme.Bad : Db > -20 ? PipitTheme.Warn : PipitTheme.Good;
        DrawRect(new Rect2(0, 0, X(Db), Size.Y), color);
        if (GateDb is { } g)
            DrawLine(new Vector2(X(g), 0), new Vector2(X(g), Size.Y), PipitTheme.Ink, 2);
        DrawRect(rect, PipitTheme.WoodEdge, false, 1);
    }
}
