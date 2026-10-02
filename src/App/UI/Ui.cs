using System;
using System.Linq;
using Godot;

namespace Pipit.App.UI;

/// <summary>Small builders so screens read as layout, not boilerplate. Text goes through Tr() for later translation.</summary>
public static class Ui
{
    /// <summary>Texts asked for while a non-English locale was active but missing from its table.</summary>
    public static readonly System.Collections.Generic.SortedSet<string> Missing = new();

    public static string T(string text)
    {
        var translated = TranslationServer.Translate(text).ToString();
        if (translated == text && TranslationServer.GetLocale() != "en" && text.Any(char.IsLetter))
            Missing.Add(text);
        return translated;
    }

    /// <summary>
    /// A label. Leave <paramref name="wrap"/> off for short text: a wrapping label has no minimum
    /// width, so in a row or a centring container it collapses to one letter per line.
    /// </summary>
    /// <summary>An XP reason from Core, which may be built from parts ("Achievement: Survivor", "2 stars").</summary>
    public static string Reason(string reason)
    {
        if (reason.StartsWith("Achievement: "))
            return string.Format(T("Achievement: {0}"), T(reason["Achievement: ".Length..]));
        var stars = System.Text.RegularExpressions.Regex.Match(reason, @"^(\d+) stars?$");
        if (stars.Success)
            return string.Format(T(stars.Groups[1].Value == "1" ? "{0} star" : "{0} stars"), stars.Groups[1].Value);
        var day = System.Text.RegularExpressions.Regex.Match(reason, @"^Day (\d+) in a row$");
        if (day.Success)
            return string.Format(T("Day {0} in a row"), day.Groups[1].Value);
        return T(reason);
    }

    /// <summary>A part name from song data: "Entry 2" (rounds), "Melody", "Left hand"...</summary>
    public static string PartName(string name)
    {
        var entry = System.Text.RegularExpressions.Regex.Match(name, @"^(Entry|Voice) (\d+)$");
        return entry.Success ? string.Format(T(entry.Groups[1].Value + " {0}"), entry.Groups[2].Value) : T(name);
    }

    /// <summary>A composer line from song data: "Traditional (France)", "X, diminutions by Y", "...; words by Z".</summary>
    public static string Composer(string text)
    {
        var words = text.Split("; words by ");
        var composer = words[0];
        var diminutions = composer.Split(", diminutions by ");
        var main = diminutions[0];
        var traditional = System.Text.RegularExpressions.Regex.Match(main, @"^Traditional \((.+)\)$");
        var result = traditional.Success ? string.Format(T("Traditional ({0})"), T(traditional.Groups[1].Value))
            : main.StartsWith("Anonymous") ? T("Anonymous") + main["Anonymous".Length..].Replace("14th century", T("14th century"))
            : main;
        if (diminutions.Length > 1)
            result += string.Format(T(", diminutions by {0}"), diminutions[1]);
        if (words.Length > 1)
            result += string.Format(T("; words by {0}"), words[1]);
        return result;
    }

    public static Label Label(string text, int size = 20, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left, bool wrap = false)
    {
        var label = new Label { Text = T(text), HorizontalAlignment = align };
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        label.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        return label;
    }

    /// <summary>A wrapping paragraph that fills the available width.</summary>
    public static Label Text(string text, int size = 18, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left) =>
        Label(text, size, color, align, wrap: true);

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
