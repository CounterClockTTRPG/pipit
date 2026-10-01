using Godot;

namespace Pipit.App.Screens;

/// <summary>A full-window page. Main shows one at a time and calls Enter/Exit around it.</summary>
public abstract partial class Screen : MarginContainer
{
    protected static Services S => Services.Instance;

    public abstract string Title { get; }

    protected Screen()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            AddThemeConstantOverride("margin_" + side, 24);
    }

    /// <summary>Subscribe to audio events here, not in the constructor.</summary>
    public virtual void Enter() { }

    /// <summary>Undo whatever Enter subscribed or started.</summary>
    public virtual void Exit() { }
}
