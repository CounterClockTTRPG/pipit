using System;
using Godot;

namespace Pipit.App.UI;

/// <summary>Small builders so screens read as layout, not boilerplate. Text goes through Tr() for later translation.</summary>
public static class Ui
{
    public static string T(string text) => TranslationServer.Translate(text);

    public static Label Label(string text, int size = 20, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label { Text = T(text), HorizontalAlignment = align, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        return label;
    }

    public static Button Button(string text, Action onPressed, int size = 22)
    {
        var button = new Button { Text = T(text), CustomMinimumSize = new Vector2(0, 48) };
        button.AddThemeFontSizeOverride("font_size", size);
        button.Pressed += onPressed;
        return button;
    }

    public static VBoxContainer VBox(int separation = 12, params Control[] children)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        foreach (var c in children)
            box.AddChild(c);
        return box;
    }

    public static HBoxContainer HBox(int separation = 12, params Control[] children)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        foreach (var c in children)
            box.AddChild(c);
        return box;
    }

    public static PanelContainer Card(Control content)
    {
        var panel = new PanelContainer();
        panel.AddChild(content);
        return panel;
    }

    public static T Expand<T>(T control, bool horizontal = true, bool vertical = false) where T : Control
    {
        if (horizontal)
            control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (vertical)
            control.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return control;
    }

    public static T MinSize<T>(T control, float width, float height) where T : Control
    {
        control.CustomMinimumSize = new Vector2(width, height);
        return control;
    }

    public static Control Spacer() => Expand(new Control(), true, true);

    public static OptionButton Options(string[] items, int selected, Action<int> onSelected)
    {
        var option = new OptionButton();
        foreach (var item in items)
            option.AddItem(T(item));
        option.Selected = selected;
        option.ItemSelected += i => onSelected((int)i);
        return option;
    }
}
