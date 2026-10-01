using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.UI;

namespace Pipit.App.Screens;

/// <summary>Songs by chapter, with the best stars earned on the current recorder size.</summary>
public partial class LibraryScreen : Screen
{
    private static readonly Dictionary<int, string> ChapterNotes = new()
    {
        [1] = "Your first notes: Si, La, Sol",
        [2] = "New notes: high Do and Re",
        [3] = "New note: low Re",
    };

    private readonly VBoxContainer _list = Ui.VBox(14);

    public override string Title => "Songs";

    public LibraryScreen()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Expand(_list));
        AddChild(scroll);
    }

    public override void Enter()
    {
        foreach (var child in _list.GetChildren())
            child.QueueFree();
        foreach (var chapter in S.Songs.Songs.GroupBy(s => s.Chapter))
        {
            _list.AddChild(Ui.Label(string.Format(Ui.T("Chapter {0}"), chapter.Key), 28, PipitTheme.Accent));
            if (ChapterNotes.TryGetValue(chapter.Key, out var notes))
                _list.AddChild(Ui.Label(notes, 17, PipitTheme.InkSoft));
            var grid = new GridContainer { Columns = 3 };
            grid.AddThemeConstantOverride("h_separation", 14);
            grid.AddThemeConstantOverride("v_separation", 14);
            foreach (var song in chapter)
            {
                var record = S.SongRecord(song.Id);
                var stars = new string('★', record.BestStars) + new string('☆', 3 - record.BestStars);
                var playable = song.Melody.Performance.All(n => S.Recorder.InRange(n.Pitch.Midi));
                var button = Ui.Button(song.Title(S.Settings.Language) + "\n" + stars + (record.Plays > 0 ? $"  {record.BestAccuracy:0}%" : ""),
                    () => Main.Instance.Show(new PlayScreen(song)), 20);
                button.CustomMinimumSize = new Vector2(330, 90);
                button.Disabled = !playable;
                button.TooltipText = playable ? song.Composer : Ui.T("Some notes are outside this recorder's range.");
                grid.AddChild(button);
            }
            _list.AddChild(grid);
        }
    }
}
