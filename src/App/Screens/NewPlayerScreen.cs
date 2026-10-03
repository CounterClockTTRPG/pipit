using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.UI;
using Pipit.App.Views;
using Pipit.Core.Profiles;

namespace Pipit.App.Screens;

/// <summary>Create a player (name, bird, language), or edit or remove an existing one.</summary>
public partial class NewPlayerScreen : Screen
{
    private static string[] Languages => Services.Languages;

    private readonly PlayerProfile? _editing;
    private readonly LineEdit _name = new() { MaxLength = 20, PlaceholderText = "Your name", CustomMinimumSize = new Vector2(360, 0) };
    private readonly List<Button> _birdButtons = new();
    private readonly OptionButton _language;
    private readonly Button _save;
    private string _avatar;

    public override string Title => _editing is null ? "New player" : "Edit player";

    /// <param name="name">, <paramref name="chosenBird"/>, <paramref name="language"/>: what was entered
    /// before the screen was rebuilt in another language.</param>
    public NewPlayerScreen(PlayerProfile? editing, string? name = null, string? chosenBird = null, string? language = null)
    {
        _editing = editing;
        _avatar = chosenBird ?? editing?.Avatar ?? Avatars.All[new Random().Next(Avatars.All.Count)];
        _name.Text = name ?? editing?.Name ?? "";
        _name.PlaceholderText = Ui.T("Your name");
        _name.AddThemeColorOverride("font_placeholder_color", PipitTheme.InkSoft);
        _name.AddThemeFontSizeOverride("font_size", 26);
        _name.TextChanged += _ => _save!.Disabled = _name.Text.Trim() == "";
        var chosen = language ?? editing?.Settings.Language ?? S.StartLanguage;
        // Choosing a language shows this screen (and the start screen behind it) in it at once.
        _language = Ui.Options(new[] { "English", "Nederlands", "Français" }, Math.Max(0, Array.IndexOf(Languages, chosen)), ix =>
        {
            S.SetStartLanguage(Languages[ix]);
            Main.Instance.ShowRoot(new ProfileScreen());
            Main.Instance.Show(new NewPlayerScreen(_editing, _name.Text, _avatar, Languages[ix]));
        });

        var birds = new HFlowContainer();
        birds.AddThemeConstantOverride("h_separation", 12);
        foreach (var avatar in Avatars.All)
        {
            var a = avatar;
            var bird = new PipView { Avatar = avatar, CustomMinimumSize = new Vector2(110, 110), MouseFilter = MouseFilterEnum.Ignore };
            var label = Ui.Label(PipView.AvatarName(avatar), 15, PipitTheme.Ink, HorizontalAlignment.Center);
            label.MouseFilter = MouseFilterEnum.Ignore;
            var content = Ui.VBox(0, bird, label);
            content.SetAnchorsPreset(LayoutPreset.FullRect);
            content.MouseFilter = MouseFilterEnum.Ignore;
            var button = new Button { ToggleMode = true, CustomMinimumSize = new Vector2(130, 150), ButtonPressed = avatar == _avatar };
            button.AddChild(content);
            button.Pressed += () => Pick(a);
            _birdButtons.Add(button);
            birds.AddChild(button);
        }

        _save = Ui.Primary(Ui.Button(editing is null ? "Create player" : "Save", Save, 22));
        _save.Disabled = _name.Text.Trim() == "";
        var actions = Ui.HBox(16, _save);
        if (editing is not null)
            actions.AddChild(Ui.Quiet(Ui.Button("Remove player", ConfirmRemove, 18)));

        var form = Ui.VBox(14,
            Ui.Label("Name", 20), _name,
            Ui.Label("Choose your bird", 20), birds,
            Ui.Label("Language", 20), _language,
            Ui.Text("Lessons and song notes are in English and Dutch; in French they are shown in English for now.", 14, PipitTheme.InkSoft),
            actions);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(form));
        AddChild(scroll);
    }

    public override void Enter() => _name.GrabFocus();

    private void Pick(string avatar)
    {
        _avatar = avatar;
        for (var i = 0; i < _birdButtons.Count; i++)
            _birdButtons[i].SetPressedNoSignal(Avatars.All[i] == avatar);
    }

    private void Save()
    {
        var name = _name.Text.Trim();
        var language = Languages[_language.Selected];
        if (_editing is null)
        {
            S.CreatePlayer(name, _avatar, language);
            Main.Instance.ShowRoot(new HomeScreen());
            Main.Instance.Show(new IntroScreen());
            return;
        }
        _editing.Name = name;
        _editing.Avatar = _avatar;
        if (_editing.Id == S.Player.Id)
            S.SetLanguage(language);
        else
            Services.SetLanguage(_editing.Settings, language);
        S.Store.Save(_editing);
        Main.Instance.ShowRoot(new ProfileScreen());
    }

    private void ConfirmRemove()
    {
        var dialog = new ConfirmationDialog
        {
            DialogText = string.Format(Ui.T("Remove {0} and all their progress? This cannot be undone."), _editing!.Name),
            OkButtonText = Ui.T("Remove"),
        };
        dialog.Confirmed += () =>
        {
            S.DeletePlayer(_editing.Id);
            dialog.QueueFree();
            Main.Instance.ShowRoot(new ProfileScreen());
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }
}
