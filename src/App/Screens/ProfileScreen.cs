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
        _cards.AddThemeConstantOverride("h_separation", 20);
        _cards.AddThemeConstantOverride("v_separation", 20);
        var heading = Ui.Heading("Who's playing?", PipitTheme.Display, HorizontalAlignment.Center);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(Ui.VBox(30, heading, _cards)));
        AddChild(scroll);
    }

    public override void Enter()
    {
        foreach (var child in _cards.GetChildren())
            child.QueueFree();
        foreach (var player in S.Players)
            _cards.AddChild(Card(player));
        _cards.AddChild(NewCard());
    }

    private Control Card(PlayerProfile player)
    {
        var bird = new PipView { Avatar = player.Avatar, CustomMinimumSize = new Vector2(150, 150), MouseFilter = MouseFilterEnum.Ignore };
        var level = Levels.LevelFor(S.Store.LoadProgress(player.Id).Xp);
        var name = Ui.Label(player.Name, 26, PipitTheme.Ink, HorizontalAlignment.Center);
        name.MouseFilter = MouseFilterEnum.Ignore;
        var sub = Ui.Label(string.Format(Ui.T("Level {0}"), level), 16, PipitTheme.InkSoft, HorizontalAlignment.Center);
        sub.MouseFilter = MouseFilterEnum.Ignore;
        var button = new Button { CustomMinimumSize = new Vector2(230, 250) };
        var content = Ui.VBox(4, bird, name, sub);
        content.SetAnchorsPreset(LayoutPreset.FullRect);
        content.MouseFilter = MouseFilterEnum.Ignore;
        button.AddChild(content);
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
