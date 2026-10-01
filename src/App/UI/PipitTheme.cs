using Godot;

namespace Pipit.App.UI;

/// <summary>Colours and the app theme, built in code so there is one place to change them.</summary>
public static class PipitTheme
{
    public static readonly Color Paper = new("fcf4e5");
    public static readonly Color PaperDark = new("f1e4c9");
    public static readonly Color Ink = new("4a3423");
    public static readonly Color InkSoft = new("8a7360");
    public static readonly Color Accent = new("d9822b");   // pipit orange
    public static readonly Color Good = new("4f9a52");
    public static readonly Color Warn = new("d9a62b");
    public static readonly Color Bad = new("c4553f");
    public static readonly Color LeftHand = new("6b8fb5");
    public static readonly Color RightHand = new("b5866b");
    public static readonly Color Wood = new("e8c99a");
    public static readonly Color WoodEdge = new("b48a55");

    public static Font Font { get; } = MakeFont();

    private static Font MakeFont()
    {
        // Segoe UI is on every Windows machine; Segoe UI Symbol supplies ♯ and ♭.
        var symbols = new SystemFont { FontNames = new[] { "Segoe UI Symbol", "DejaVu Sans" } };
        return new SystemFont { FontNames = new[] { "Segoe UI", "Arial" }, Fallbacks = new Godot.Collections.Array<Font> { symbols } };
    }

    public static Theme Build()
    {
        var theme = new Theme { DefaultFont = Font, DefaultFontSize = 20 };
        foreach (var type in new[] { "Label", "Button", "OptionButton", "CheckBox", "LineEdit", "SpinBox", "ItemList" })
            theme.SetColor("font_color", type, Ink);
        theme.SetColor("font_color", "ItemList", Ink);
        theme.SetColor("font_selected_color", "ItemList", Paper);

        theme.SetStylebox("normal", "Button", Box(PaperDark, WoodEdge));
        theme.SetStylebox("hover", "Button", Box(new Color("f6dcb4"), Accent));
        theme.SetStylebox("pressed", "Button", Box(Accent, Accent));
        theme.SetStylebox("disabled", "Button", Box(Paper, PaperDark));
        theme.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), Accent));
        theme.SetColor("font_hover_color", "Button", Ink);
        theme.SetColor("font_pressed_color", "Button", Paper);
        theme.SetColor("font_disabled_color", "Button", InkSoft);
        theme.SetStylebox("normal", "OptionButton", Box(PaperDark, WoodEdge));
        theme.SetStylebox("hover", "OptionButton", Box(new Color("f6dcb4"), Accent));
        theme.SetStylebox("panel", "ItemList", Box(Colors.White, PaperDark));
        theme.SetStylebox("selected", "ItemList", Box(Accent, Accent));
        theme.SetStylebox("selected_focus", "ItemList", Box(Accent, Accent));
        theme.SetStylebox("panel", "PanelContainer", Box(Colors.White with { A = 0.6f }, PaperDark, 14));
        theme.SetStylebox("normal", "LineEdit", Box(Colors.White, PaperDark));
        return theme;
    }

    public static StyleBoxFlat Box(Color bg, Color border, int padding = 10) => new()
    {
        BgColor = bg,
        BorderColor = border,
        BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
        CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10, CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
        ContentMarginLeft = padding + 4, ContentMarginRight = padding + 4, ContentMarginTop = padding, ContentMarginBottom = padding,
    };
}
