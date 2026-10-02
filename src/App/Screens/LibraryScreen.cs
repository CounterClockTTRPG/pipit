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
        [4] = "The low notes: Do, Mi and Fa",
    };

    private readonly VBoxContainer _list = Ui.VBox(14);
    private readonly ScrollContainer _scroll;

    /// <summary>For `--shots`: scroll to the end and report the sizes.</summary>
    public string DebugScrollToEnd()
    {
        _scroll.ScrollVertical = 100000;
        return $"screen {Size} scroll {_scroll.Size} list {_list.Size} max {_scroll.GetVScrollBar().MaxValue}";
    }

    public override string Title => "Songs";

    public LibraryScreen()
    {
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _scroll.AddChild(Ui.Expand(_list));
        AddChild(_scroll);
    }

    public override void Enter()
    {
        foreach (var child in _list.GetChildren())
            child.QueueFree();
        foreach (var chapter in S.Songs.Songs.GroupBy(s => s.Chapter))
        {
            var repertoire = chapter.Key == 0;
            _list.AddChild(Ui.Label(repertoire ? Ui.T("Repertoire") : string.Format(Ui.T("Chapter {0}"), chapter.Key), 28, PipitTheme.Accent));
            if (repertoire)
                _list.AddChild(Ui.Label("More music, from easiest to hardest. The dots show how hard a piece is.", 17, PipitTheme.InkSoft));
            else if (ChapterNotes.TryGetValue(chapter.Key, out var notes))
                _list.AddChild(Ui.Label(notes, 17, PipitTheme.InkSoft));
            // A flow, not a fixed grid: as many cards per row as the window holds. Cards have a
            // fixed width and wrap their text, so a long composer name cannot widen the page
            // (which pushed the scrollbar off-screen).
            var grid = new HFlowContainer();
            grid.AddThemeConstantOverride("h_separation", 14);
            grid.AddThemeConstantOverride("v_separation", 14);
            foreach (var song in chapter)
            {
                var record = S.SongRecord(song.Id);
                var stars = new string('★', record.BestStars) + new string('☆', 3 - record.BestStars);
                var playable = song.Melody.Performance.All(n => S.Recorder.InRange(n.Pitch.Midi));
                var dots = new string('●', song.Difficulty) + new string('○', 5 - song.Difficulty);
                var button = Ui.Button(song.Title(S.Settings.Language) + "\n" + (repertoire ? Ui.Composer(song.Composer) + "  " + dots + "\n" : "") + stars + (record.Plays > 0 ? $"  {record.BestAccuracy:0}%" : ""),
                    () => Main.Instance.Show(new PlayScreen(song)), 20);
                button.CustomMinimumSize = new Vector2(360, repertoire ? 118 : 90);
                button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
                button.Disabled = !playable;
                button.TooltipText = playable ? Ui.Composer(song.Composer) : Ui.T("Some notes are outside this recorder's range.");
                grid.AddChild(button);
            }
            _list.AddChild(grid);
        }
    }
}
