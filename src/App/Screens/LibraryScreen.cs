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
        [5] = "High notes: Mi', Fa' and Sol'",
        [6] = "Sharps: Fa♯, Do♯' and Sol♯; and La', Si'",
        [7] = "A flat: Si♭",
        [8] = "Sixteenth notes and six-eight time",
        [9] = "Mi♭', Si♭', La♭' and Do''",
        [10] = "Sonatas for the alto recorder",
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
            var heading = Ui.Label(repertoire ? Ui.T("Repertoire") : string.Format(Ui.T("Chapter {0}"), chapter.Key), PipitTheme.Heading, PipitTheme.Ink);
            heading.AddThemeFontOverride("font", PipitTheme.BoldFont);
            var note = repertoire ? "More music, from easiest to hardest. The dots show how hard a piece is."
                : ChapterNotes.TryGetValue(chapter.Key, out var notes) ? notes : "";
            _list.AddChild(Ui.VBox(0, heading, Ui.Label(note, PipitTheme.Small + 1, PipitTheme.InkSoft)));
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
                // Songs that do not fit this recorder are moved until they do (an alto piece on a soprano).
                var fit = Core.Music.Transposition.FitFor(song, S.Recorder);
                var playable = fit is not null;
                var moved = fit is { Semitones: not 0 } ? "  ↕" : "";
                var dots = new string('●', song.Difficulty) + new string('○', 5 - song.Difficulty);
                var sub = (repertoire ? Ui.Composer(song.Composer) + "   " + dots + moved + "\n" : "") + stars + (record.Plays > 0 ? $"   {record.BestAccuracy:0}%" : "");
                var button = Ui.Tile(song.Title(S.Settings.Language), sub, () => Main.Instance.Show(new PlayScreen(song)),
                    300, repertoire ? 104 : 84, record.BestStars > 0 ? Ui.TileState.Done : Ui.TileState.Normal, translate: false);
                button.Disabled = !playable;
                button.TooltipText = !playable ? Ui.T("Some notes are outside this recorder's range.")
                    : fit is { Semitones: not 0 } f ? PlayScreen.TransposedText(f.Semitones) : Ui.Composer(song.Composer);
                grid.AddChild(button);
            }
            _list.AddChild(grid);
        }
    }
}
