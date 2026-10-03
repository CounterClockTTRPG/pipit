using System;
using System.Linq;
using Godot;
using Pipit.App.UI;
using Pipit.App.Views;
using Pipit.Core.Game;
using Pipit.Core.Profiles;

namespace Pipit.App.Screens;

/// <summary>The start screen: who is playing? One card per player (their bird, name and level).</summary>
public partial class ProfileScreen : Screen
{
    private readonly HFlowContainer _cards = new();

    public override string Title => "Players";

    public ProfileScreen()
    {
        // Before a player is chosen the screens speak the computer's language, which can be changed here.
        S.ApplyLanguage(S.StartLanguage);
        _cards.AddThemeConstantOverride("h_separation", 20);
        _cards.AddThemeConstantOverride("v_separation", 20);
        var heading = Ui.Heading("Who's playing?", PipitTheme.Display, HorizontalAlignment.Center);
        var language = Ui.Options(new[] { "English", "Nederlands", "Français" }, Math.Max(0, Array.IndexOf(Services.Languages, S.StartLanguage)), ix =>
        {
            S.SetStartLanguage(Services.Languages[ix]);
            Main.Instance.ShowRoot(new ProfileScreen());
        });
        language.TooltipText = Ui.T("Language");
        var languageRow = Ui.HBox(8, Ui.Label("🌐", 18, PipitTheme.InkSoft), language);
        languageRow.Alignment = BoxContainer.AlignmentMode.Center;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(Ui.VBox(30, heading, _cards, languageRow)));
        AddChild(scroll);
    }

    public override void Enter()
    {
        foreach (var child in _cards.GetChildren())
            child.QueueFree();
        // The player with the most XP wears the crown (when there is someone to beat).
        var xp = S.Players.ToDictionary(p => p.Id, p => S.Store.LoadProgress(p.Id).Xp);
        var crown = Levels.CrownHolder(xp.Select(kv => (kv.Key, kv.Value)).ToList());
        foreach (var player in S.Players)
            _cards.AddChild(Card(player, xp[player.Id], player.Id == crown));
        _cards.AddChild(NewCard());
    }

    private Control Card(PlayerProfile player, long xp, bool crowned)
    {
        var bird = new PipView { Avatar = player.Avatar, CustomMinimumSize = new Vector2(150, 150), MouseFilter = MouseFilterEnum.Ignore };
        var level = Levels.LevelFor(xp);
        var name = Ui.Label(player.Name, 26, PipitTheme.Ink, HorizontalAlignment.Center);
        name.MouseFilter = MouseFilterEnum.Ignore;
        var sub = Ui.Label(string.Format(Ui.T("Level {0}"), level), 20, PipitTheme.Accent, HorizontalAlignment.Center);
        sub.AddThemeFontOverride("font", PipitTheme.BoldFont);
        sub.MouseFilter = MouseFilterEnum.Ignore;
        // How far to the next level.
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = Levels.Fraction(xp), ShowPercentage = false, CustomMinimumSize = new Vector2(150, 8), MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        var button = new Button { CustomMinimumSize = new Vector2(230, 270) };
        var content = Ui.VBox(4, bird, name, sub, bar);
        content.SetAnchorsPreset(LayoutPreset.FullRect);
        content.MouseFilter = MouseFilterEnum.Ignore;
        button.AddChild(content);
        if (crowned)
        {
            var crown = Ui.Label("👑", 40, null, HorizontalAlignment.Center);
            crown.MouseFilter = MouseFilterEnum.Ignore;
            crown.TooltipText = Ui.T("Highest level");
            crown.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
            crown.OffsetTop = -14;
            crown.OffsetBottom = 40;
            button.AddChild(crown);
            button.TooltipText = Ui.T("Highest level");
        }
        button.Pressed += () => Choose(player);
        var edit = Ui.Quiet(Ui.Button("✎ " + Ui.T("Edit"), () => Main.Instance.Show(new NewPlayerScreen(player)), 15));
        return Ui.VBox(6, button, edit);
    }

    private static Control NewCard()
    {
        var button = Ui.Button("+\n" + Ui.T("New player"), () => Main.Instance.Show(new NewPlayerScreen(null)), 26);
        button.CustomMinimumSize = new Vector2(230, 250);
        return button;
    }

    public static void Choose(PlayerProfile player)
    {
        S.SelectPlayer(player);
        Main.Instance.ShowRoot(new HomeScreen());
        if (!player.IntroSeen)
            Main.Instance.Show(new IntroScreen());
    }
}
