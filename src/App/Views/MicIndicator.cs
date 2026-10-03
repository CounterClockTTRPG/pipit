using System;
using Godot;
using Pipit.App.UI;

namespace Pipit.App.Views;

/// <summary>
/// A small microphone with sound waves, in the corner of every screen, so the player can see
/// that Pipit hears them: light grey in silence, darker and with more waves as the sound gets
/// louder, red when it is too loud (close to clipping).
/// </summary>
public partial class MicIndicator : Control
{
    /// <summary>Below this the room is quiet; at <see cref="FullDb"/> the icon is fully dark.</summary>
    private const double QuietDb = -60, FullDb = -24, TooLoudDb = -8;

    private double _db = -120;

    public MicIndicator()
    {
        CustomMinimumSize = new Vector2(46, 34);
        MouseFilter = MouseFilterEnum.Pass;
        TooltipText = Ui.T("What the microphone hears");
    }

    public override void _Process(double delta)
    {
        var db = Services.Instance.Audio.LevelDb;
        if (Math.Abs(db - _db) > 0.5)
        {
            _db = db;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var level = Math.Clamp((_db - QuietDb) / (FullDb - QuietDb), 0, 1);
        var color = _db >= TooLoudDb
            ? PipitTheme.Bad
            // Solid colours mixed with the paper: overlapping shapes would show through each other.
            : PipitTheme.Paper.Lerp(PipitTheme.InkSoft.Lerp(PipitTheme.Ink, (float)level), (float)(0.3 + 0.7 * level));
        var h = Size.Y;
        var mic = new Vector2(h * 0.38f, h * 0.42f);
        // Capsule: a rounded bar; the stand: an arc under it and a short foot.
        var w = h * 0.26f;
        DrawRect(new Rect2(mic.X - w / 2, mic.Y - h * 0.3f + w / 2, w, h * 0.42f - w), color);
        DrawCircle(new Vector2(mic.X, mic.Y - h * 0.3f + w / 2), w / 2, color);
        DrawCircle(new Vector2(mic.X, mic.Y + h * 0.12f - w / 2), w / 2, color);
        DrawArc(new Vector2(mic.X, mic.Y), h * 0.24f, 0.15f, Mathf.Pi - 0.15f, 12, color, 2, true);
        DrawLine(new Vector2(mic.X, mic.Y + h * 0.24f), new Vector2(mic.X, h * 0.86f), color, 2, true);
        DrawLine(new Vector2(mic.X - h * 0.14f, h * 0.86f), new Vector2(mic.X + h * 0.14f, h * 0.86f), color, 2, true);
        // Waves: one more for every third of the way to full.
        var waves = level <= 0 ? 0 : (int)Math.Ceiling(level * 3);
        for (var i = 0; i < waves; i++)
        {
            var r = h * (0.26f + 0.15f * i);
            DrawArc(new Vector2(mic.X + h * 0.1f, mic.Y - h * 0.04f), r, -0.65f, 0.65f, 10, color, 2, true);
        }
    }
}
