using Godot;

namespace Pipit.App.UI;

/// <summary>
/// Colours, type and the app theme, built in code so there is one place to change them.
///
/// Colour carries meaning: Pip's orange (<see cref="Accent"/>) marks the one main action on a
/// screen (and Pip), meadow green (<see cref="Good"/>) means done or right, sky blue
/// (<see cref="Sky"/>, the left hand's colour) is information, and wood is the surface of
/// everything else. Buttons come in three weights: primary (filled orange, see
/// <see cref="Ui.Primary"/>), the default wooden button, and quiet (text only, see
/// <see cref="Ui.Quiet"/>).
///
/// Type is Andika (SIL, OFL), designed for children learning to read: unambiguous letter
/// shapes, generous spacing. Symbols it lacks (♯ ♭, emoji) fall back to the system fonts.
/// </summary>
public static class PipitTheme
{
    public static readonly Color Paper = new("fcf4e5");
    public static readonly Color PaperDark = new("f1e4c9");
    public static readonly Color Ink = new("4a3423");
    public static readonly Color InkSoft = new("8a7360");
    public static readonly Color Accent = new("d9822b");   // Pip's beak orange
    public static readonly Color AccentLight = new("e8984a");
    public static readonly Color AccentDark = new("bb6a1c");
    public static readonly Color Good = new("4f9a52");     // meadow green
    public static readonly Color GoodLight = new("e3f0dc");
    public static readonly Color Warn = new("d9a62b");
    public static readonly Color Bad = new("c4553f");
    public static readonly Color Sky = new("6b8fb5");
    public static readonly Color LeftHand = new("6b8fb5");
    public static readonly Color RightHand = new("b5866b");
    public static readonly Color Wood = new("e8c99a");
    public static readonly Color WoodLight = new("f5e6c8");
    public static readonly Color WoodEdge = new("b48a55");

    /// <summary>Type scale (font sizes in px), roughly a fourth apart.</summary>
    public const int Small = 15, Body = 19, Lead = 22, Heading = 28, Display = 44;

    public static Font Font { get; } = MakeFont("Andika-Regular.ttf", 400);
    public static Font BoldFont { get; } = MakeFont("Andika-Bold.ttf", 700);
    public static Font ItalicFont { get; } = MakeFont("Andika-Italic.ttf", 400, italic: true);

    private static Font MakeFont(string file, int weight, bool italic = false)
    {
        // Segoe UI Symbol / Emoji supply ♯ ♭ and the emoji on Windows; DejaVu elsewhere.
        var fallback = new SystemFont { FontNames = new[] { "Segoe UI Symbol", "Segoe UI Emoji", "Segoe UI", "DejaVu Sans" }, FontWeight = weight, FontItalic = italic };
        var bytes = FileAccess.GetFileAsBytes("res://assets/fonts/" + file);
        if (bytes.Length == 0)
            return fallback;
        return new FontFile { Data = bytes, Fallbacks = new Godot.Collections.Array<Font> { fallback } };
    }

    private static Texture2D? Icon(string name)
    {
        var bytes = FileAccess.GetFileAsBytes("res://assets/ui/" + name);
        if (bytes.Length == 0)
            return null;
        var image = new Image();
        return image.LoadPngFromBuffer(bytes) == Error.Ok ? ImageTexture.CreateFromImage(image) : null;
    }

    public static Theme Build()
    {
        var theme = new Theme { DefaultFont = Font, DefaultFontSize = Body };
        foreach (var type in new[] { "Label", "Button", "OptionButton", "CheckBox", "LineEdit", "SpinBox", "ItemList", "RichTextLabel" })
            theme.SetColor("font_color", type, Ink);
        theme.SetColor("default_color", "RichTextLabel", Ink);
        theme.SetColor("font_selected_color", "ItemList", Colors.White);

        // Default (wooden) button.
        theme.SetStylebox("normal", "Button", Box(WoodLight, WoodEdge with { A = 0.55f }, radius: 12, line: 1));
        theme.SetStylebox("hover", "Button", Box(new Color("f8ecd5"), Accent, radius: 12, line: 1));
        theme.SetStylebox("pressed", "Button", Box(Wood, WoodEdge, radius: 12, line: 1));
        theme.SetStylebox("disabled", "Button", Box(Paper, PaperDark, radius: 12, line: 1));
        theme.SetStylebox("focus", "Button", Outline(Accent, 12));
        theme.SetColor("font_hover_color", "Button", Ink);
        theme.SetColor("font_pressed_color", "Button", Ink);
        theme.SetColor("font_focus_color", "Button", Ink);
        theme.SetColor("font_disabled_color", "Button", InkSoft with { A = 0.7f });

        // Primary: the one main action on a screen.
        theme.SetTypeVariation("PrimaryButton", "Button");
        theme.SetStylebox("normal", "PrimaryButton", Box(Accent, AccentDark with { A = 0.4f }, radius: 14, line: 1, bottom: 3));
        theme.SetStylebox("hover", "PrimaryButton", Box(AccentLight, AccentDark with { A = 0.4f }, radius: 14, line: 1, bottom: 3));
        theme.SetStylebox("pressed", "PrimaryButton", Box(AccentDark, AccentDark, radius: 14, line: 1));
        theme.SetStylebox("disabled", "PrimaryButton", Box(Accent with { A = 0.35f }, new Color(0, 0, 0, 0), radius: 14, line: 1));
        theme.SetStylebox("focus", "PrimaryButton", Outline(Ink, 14));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
            theme.SetColor(state, "PrimaryButton", Colors.White);
        theme.SetColor("font_disabled_color", "PrimaryButton", Colors.White with { A = 0.8f });
        theme.SetFont("font", "PrimaryButton", BoldFont);

        // Quiet: text-only actions (back, skip, edit).
        theme.SetTypeVariation("QuietButton", "Button");
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
            theme.SetStylebox(state, "QuietButton", Box(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), radius: 10, line: 0));
        theme.SetStylebox("hover", "QuietButton", Box(WoodLight, new Color(0, 0, 0, 0), radius: 10, line: 0));
        theme.SetColor("font_color", "QuietButton", InkSoft);
        theme.SetColor("font_hover_color", "QuietButton", Accent);
        theme.SetColor("font_pressed_color", "QuietButton", AccentDark);

        // Tick boxes: drawn boxes instead of Godot's grey squares, and no button frame.
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
            theme.SetStylebox(state, "CheckBox", new StyleBoxEmpty { ContentMarginLeft = 2, ContentMarginRight = 4 });
        if (Icon("check-on.png") is { } on && Icon("check-off.png") is { } off)
        {
            theme.SetIcon("checked", "CheckBox", on);
            theme.SetIcon("unchecked", "CheckBox", off);
            theme.SetIcon("checked_disabled", "CheckBox", Icon("check-on-disabled.png") ?? on);
            theme.SetIcon("unchecked_disabled", "CheckBox", Icon("check-off-disabled.png") ?? off);
        }
        theme.SetConstant("h_separation", "CheckBox", 8);
        theme.SetColor("font_pressed_color", "CheckBox", Ink);
        theme.SetColor("font_hover_color", "CheckBox", AccentDark);
        theme.SetColor("font_hover_pressed_color", "CheckBox", AccentDark);

        theme.SetStylebox("normal", "OptionButton", Box(WoodLight, WoodEdge with { A = 0.55f }, radius: 12, line: 1));
        theme.SetStylebox("hover", "OptionButton", Box(new Color("f8ecd5"), Accent, radius: 12, line: 1));
        theme.SetStylebox("pressed", "OptionButton", Box(Wood, WoodEdge, radius: 12, line: 1));
        theme.SetStylebox("focus", "OptionButton", Outline(Accent, 12));
        theme.SetStylebox("panel", "PopupMenu", Box(Colors.White, WoodEdge with { A = 0.5f }, 6, radius: 10, line: 1));
        theme.SetStylebox("hover", "PopupMenu", Box(WoodLight, new Color(0, 0, 0, 0), 4, radius: 8, line: 0));
        theme.SetColor("font_color", "PopupMenu", Ink);
        theme.SetColor("font_hover_color", "PopupMenu", Ink);

        theme.SetStylebox("panel", "ItemList", Box(Colors.White, PaperDark, radius: 12, line: 1));
        theme.SetStylebox("selected", "ItemList", Box(Accent, Accent, 4, radius: 8, line: 0));
        theme.SetStylebox("selected_focus", "ItemList", Box(Accent, Accent, 4, radius: 8, line: 0));
        theme.SetStylebox("panel", "PanelContainer", Box(Colors.White with { A = 0.7f }, PaperDark, 14, radius: 16, line: 1));
        theme.SetStylebox("normal", "LineEdit", Box(Colors.White, WoodEdge with { A = 0.45f }, radius: 10, line: 1));
        theme.SetStylebox("focus", "LineEdit", Outline(Accent, 10));

        // Sliders and scroll bars: wood track, orange handle.
        theme.SetStylebox("slider", "HSlider", Bar(PaperDark, 6));
        theme.SetStylebox("grabber_area", "HSlider", Bar(Accent, 6));
        theme.SetStylebox("grabber_area_highlight", "HSlider", Bar(AccentLight, 6));
        theme.SetStylebox("scroll", "VScrollBar", Bar(new Color(0, 0, 0, 0), 8));
        theme.SetStylebox("grabber", "VScrollBar", Bar(Wood, 8));
        theme.SetStylebox("grabber_highlight", "VScrollBar", Bar(WoodEdge with { A = 0.7f }, 8));
        theme.SetStylebox("grabber_pressed", "VScrollBar", Bar(WoodEdge, 8));
        theme.SetStylebox("scroll", "HScrollBar", Bar(new Color(0, 0, 0, 0), 8));
        theme.SetStylebox("grabber", "HScrollBar", Bar(Wood, 8));
        theme.SetStylebox("grabber_highlight", "HScrollBar", Bar(WoodEdge with { A = 0.7f }, 8));
        theme.SetStylebox("grabber_pressed", "HScrollBar", Bar(WoodEdge, 8));
        theme.SetStylebox("background", "ProgressBar", Bar(PaperDark, 8));
        theme.SetStylebox("fill", "ProgressBar", Bar(Good, 8));
        theme.SetStylebox("panel", "TooltipPanel", Box(Ink, Ink, 8, radius: 8, line: 0));
        theme.SetColor("font_color", "TooltipLabel", Paper);
        return theme;
    }

    /// <summary>A panel or button surface.</summary>
    public static StyleBoxFlat Box(Color bg, Color border, int padding = 10, int radius = 10, int line = 2, int bottom = -1)
    {
        var box = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthTop = line, BorderWidthLeft = line, BorderWidthRight = line,
            BorderWidthBottom = bottom >= 0 ? bottom : line,
            ContentMarginLeft = padding + 4, ContentMarginRight = padding + 4, ContentMarginTop = padding, ContentMarginBottom = padding,
            AntiAliasing = true,
        };
        box.SetCornerRadiusAll(radius);
        return box;
    }

    /// <summary>Keyboard focus: a ring outside the control, drawn over whatever it has.</summary>
    private static StyleBoxFlat Outline(Color color, int radius)
    {
        var box = new StyleBoxFlat { DrawCenter = false, BorderColor = color, ExpandMarginLeft = 3, ExpandMarginRight = 3, ExpandMarginTop = 3, ExpandMarginBottom = 3 };
        box.SetBorderWidthAll(2);
        box.SetCornerRadiusAll(radius + 3);
        return box;
    }

    private static StyleBoxFlat Bar(Color color, int radius)
    {
        var box = new StyleBoxFlat { BgColor = color, ContentMarginTop = 4, ContentMarginBottom = 4, ContentMarginLeft = 4, ContentMarginRight = 4 };
        box.SetCornerRadiusAll(radius);
        return box;
    }
}
