using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.UI;
using Pipit.Core.Music;

namespace Pipit.App.Screens;

/// <summary>
/// Songs by chapter, with the best stars earned on the current recorder size; then the repertoire,
/// searchable and grouped by type or composer, favourites first.
/// </summary>
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

    /// <summary>For `--shots`: scroll to the repertoire's search and grouping controls.</summary>
    public void DebugScrollToRepertoire() => _scroll.ScrollVertical = (int)(_repertoire.Position.Y - 160);

    /// <summary>For `--shots`: search as if typed.</summary>
    public void DebugSearch(string query) { _query = query; FillRepertoire(); }

    public override string Title => "Songs";

    public LibraryScreen()
    {
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _scroll.AddChild(Ui.Expand(_list));
        AddChild(_scroll);
    }

    // Remembered while the app runs, so coming back from a song keeps the view.
    private static RepertoireGrouping _grouping = RepertoireGrouping.Period;
    private static string _query = "";
    private readonly VBoxContainer _repertoire = Ui.VBox(14);

    public override void Enter()
    {
        // The repertoire box is kept between visits: take it out before the rest is freed.
        if (_repertoire.GetParent() is { } parent) parent.RemoveChild(_repertoire);
        foreach (var child in _list.GetChildren())
            child.QueueFree();
        foreach (var chapter in S.Songs.Songs.Where(s => s.Chapter != 0).GroupBy(s => s.Chapter))
        {
            _list.AddChild(Ui.VBox(0, Heading(string.Format(Ui.T("Chapter {0}"), chapter.Key)),
                Ui.Label(ChapterNotes.TryGetValue(chapter.Key, out var notes) ? notes : "", PipitTheme.Small + 1, PipitTheme.InkSoft)));
            _list.AddChild(Grid(chapter.Select(song => SongTile(song, false))));
        }
        _list.AddChild(Ui.VBox(0, Heading(Ui.T("Repertoire")),
            Ui.Label("More music, from easiest to hardest. The dots show how hard a piece is; tap ♡ to keep a piece at the top.", PipitTheme.Small + 1, PipitTheme.InkSoft)));
        _list.AddChild(Controls());
        _list.AddChild(_repertoire);
        FillRepertoire();
    }

    /// <summary>Search and the grouping toggle (Show all / By type / By composer).</summary>
    private Control Controls()
    {
        var search = new LineEdit { Text = _query, PlaceholderText = Ui.T("Search: title, composer, style…"), ClearButtonEnabled = true, CustomMinimumSize = new Vector2(320, 0) };
        search.AddThemeColorOverride("font_placeholder_color", PipitTheme.InkSoft);
        search.TextChanged += text => { _query = text; FillRepertoire(); };
        var row = Ui.HBox(8, search, new Control { CustomMinimumSize = new Vector2(12, 0) });
        var group = new ButtonGroup();
        foreach (var (grouping, label) in new[] { (RepertoireGrouping.Difficulty, "Show all"), (RepertoireGrouping.Period, "By type"), (RepertoireGrouping.Composer, "By composer") })
        {
            var button = new Button { Text = Ui.T(label), ToggleMode = true, ButtonGroup = group, ButtonPressed = _grouping == grouping };
            button.Pressed += () => { _grouping = grouping; FillRepertoire(); };
            row.AddChild(button);
        }
        return row;
    }

    private void FillRepertoire()
    {
        foreach (var child in _repertoire.GetChildren())
            child.QueueFree();
        var groups = Repertoire.Arrange(S.Songs.Songs.Where(s => s.Chapter == 0), _grouping, S.Progress.Favourites, query: _query);
        if (groups.Count == 0)
            _repertoire.AddChild(Ui.Label("No songs match your search.", PipitTheme.Small + 1, PipitTheme.InkSoft));
        foreach (var group in groups)
        {
            if (group.Key != "")
            {
                var title = group.Key == Repertoire.FavouritesKey ? "♥ " + Ui.T("Favourites")
                    : _grouping == RepertoireGrouping.Period ? Ui.T(Repertoire.PeriodNames.GetValueOrDefault(group.Key, group.Key))
                    : group.Key is "Anonymous" or "Traditional" ? Ui.T(group.Key) : group.Key;
                var heading = Ui.HBox(10, Heading(title, PipitTheme.Lead), Ui.Label(group.Songs.Count.ToString(), PipitTheme.Small + 1, PipitTheme.InkSoft));
                heading.Alignment = BoxContainer.AlignmentMode.Begin;
                _repertoire.AddChild(heading);
            }
            _repertoire.AddChild(Grid(group.Songs.Select(song => SongTile(song, true))));
        }
    }

    private static Label Heading(string text, int size = PipitTheme.Heading)
    {
        var heading = Ui.Label(text, size, PipitTheme.Ink);
        heading.AddThemeFontOverride("font", PipitTheme.BoldFont);
        return heading;
    }

    /// <summary>
    /// A flow, not a fixed grid: as many cards per row as the window holds. Cards have a fixed
    /// width and wrap their text, so a long composer name cannot widen the page (which pushed the
    /// scrollbar off-screen).
    /// </summary>
    private static HFlowContainer Grid(IEnumerable<Control> tiles)
    {
        var grid = new HFlowContainer();
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        foreach (var tile in tiles)
            grid.AddChild(tile);
        return grid;
    }

    private Button SongTile(Song song, bool repertoire)
    {
        var record = S.SongRecord(song.Id);
        var stars = new string('★', record.BestStars) + new string('☆', 3 - record.BestStars);
        // Songs that do not fit this recorder are moved until they do (an alto piece on a soprano).
        var fit = Transposition.FitFor(song, S.Recorder);
        var playable = fit is not null;
        var moved = fit is { Semitones: not 0 } ? "  ↕" : "";
        var dots = new string('●', song.Difficulty) + new string('○', 5 - song.Difficulty);
        var sub = (repertoire ? Ui.Composer(song.Composer) + "   " + dots + moved + "\n" : "") + stars + (record.Plays > 0 ? $"   {record.BestAccuracy:0}%" : "");
        var button = Ui.Tile(song.Title(S.Settings.Language), sub, () => Main.Instance.Show(new PlayScreen(song)),
            300, repertoire ? 104 : 84, record.BestStars > 0 ? Ui.TileState.Done : Ui.TileState.Normal, translate: false);
        button.Disabled = !playable;
        button.TooltipText = !playable ? Ui.T("Some notes are outside this recorder's range.")
            : fit is { Semitones: not 0 } f ? PlayScreen.TransposedText(f.Semitones) : Ui.Composer(song.Composer);
        if (repertoire)
            AddHeart(button, song);
        return button;
    }

    /// <summary>A heart in the tile's corner: favourites move to the top of the repertoire.</summary>
    private void AddHeart(Button tile, Song song)
    {
        var favourite = S.Progress.Favourites.Contains(song.Id);
        var heart = Ui.Quiet(new Button
        {
            Text = favourite ? "♥" : "♡",
            TooltipText = Ui.T(favourite ? "Remove from favourites" : "Add to favourites"),
            CustomMinimumSize = new Vector2(40, 40),
            FocusMode = FocusModeEnum.None,
        });
        heart.AddThemeFontSizeOverride("font_size", PipitTheme.Lead);
        heart.AddThemeColorOverride("font_color", favourite ? PipitTheme.Accent : PipitTheme.InkSoft);
        heart.AddThemeColorOverride("font_hover_color", PipitTheme.Accent);
        heart.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        heart.OffsetLeft = -44; heart.OffsetTop = 2; heart.OffsetRight = -4; heart.OffsetBottom = 42;
        heart.Pressed += () =>
        {
            if (!S.Progress.Favourites.Remove(song.Id))
                S.Progress.Favourites.Add(song.Id);
            S.SaveProgress();
            Callable.From(FillRepertoire).CallDeferred();
        };
        tile.AddChild(heart);
        // Keep the title clear of the heart.
        if (tile.GetChild(0) is MarginContainer margin)
            margin.AddThemeConstantOverride("margin_right", 44);
    }
}
