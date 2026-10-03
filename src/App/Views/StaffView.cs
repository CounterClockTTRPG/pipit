using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pipit.App.UI;
using Pipit.Core.Instruments;
using Pipit.Core.Music;
using Pipit.Core.Notation;

namespace Pipit.App.Views;

/// <summary>
/// The song as a score: <see cref="StaffLayout"/> engraved with the Bravura (SMuFL) font. Lines
/// scroll so the current one stays in view; a cursor follows the music, graded notes are
/// coloured, names can be shown under the notes, and a faint "ghost" note shows what is being
/// played right now.
/// </summary>
public partial class StaffView : Control, ISongView
{
    // SMuFL code points.
    private const string GClef = "", GClef8va = "", HeadBlack = "", HeadHalf = "", HeadWhole = "";
    private const string Sharp = "", Flat = "", Natural = "", Dot = "", Breath = "", RepeatDot = "";
    private const string RestWhole = "", RestHalf = "", RestQuarter = "", Rest8th = "", Rest16th = "", Rest32nd = "";
    private const string Flag8Up = "", Flag8Down = "", Flag16Up = "", Flag16Down = "", Flag32Up = "", Flag32Down = "";
    private const string Tuplet3 = "";
    private const char TimeSigZero = '';
    // Bravura engraving defaults and glyph widths, in staff spaces (from Bravura.json).
    private const float StemW = 0.12f, LineW = 0.13f, BeamW = 0.5f, LedgerW = 0.16f, LedgerExt = 0.4f, ThinBar = 0.16f, ThickBar = 0.5f;
    private const float HeadWidth = 1.18f, WholeWidth = 1.688f, StemAnchorY = 0.168f;

    private Song? _song;
    private KeySignature _key;
    private bool _octaveClef;
    private List<StaffSystem> _systems = new();
    private readonly Dictionary<int, (int System, PlacedBar Bar, PlacedNote Note)> _byWrittenId = new();
    private readonly Dictionary<int, NoteMark> _marks = new();      // by performed index
    private readonly Dictionary<int, (string Text, Color Color, string Sub, double Shift)> _captions = new(); // by performed index
    private readonly Dictionary<int, int> _lastPerformedOf = new(); // written id -> latest performed index marked
    private Vector2 _laidOutFor;
    private float _scroll;
    private (double Beat, double Written) _heard = (0, double.NaN);

    public double CurrentBeat { get; set; }
    public int? WaitingFor { get; set; }
    public string Overlay { get; set; } = "";
    public bool ShowNames { get; set; } = true;
    /// <summary>False for static snippets (lessons): no cursor, no highlighted note.</summary>
    public bool ShowCursor { get; set; } = true;
    public Func<Pitch, string> NoteLabel { get; set; } = p => p.ToString();

    public StaffView()
    {
        ClipContents = true;
        CustomMinimumSize = new Vector2(500, 260);
    }

    public void SetSong(Song song, RecorderKind kind)
    {
        _song = song;
        _key = KeySignature.Parse(song.Key);
        _octaveClef = RecorderInfo.Of(kind).OctaveTransposition == 1;
        _laidOutFor = Vector2.Zero;
        ClearMarks();
    }

    public void SetMark(int performedIndex, NoteMark mark)
    {
        _marks[performedIndex] = mark;
        if (_song is null || performedIndex >= _song.Melody.Performance.Count)
            return;
        foreach (var id in _song.Melody.Performance[performedIndex].WrittenIds)
            _lastPerformedOf[id] = performedIndex;
    }

    /// <summary>
    /// A word under a note (what the player said or tapped in Note reading), in its own colour,
    /// shifted sideways by <paramref name="shiftBeats"/> (said early: left, late: right), with an
    /// optional grey line under it (what the note should have been).
    /// </summary>
    public void SetCaption(int performedIndex, string text, Color color, string sub = "", double shiftBeats = 0)
    {
        _captions[performedIndex] = (text, color, sub, shiftBeats);
        if (_song is not null && performedIndex < _song.Melody.Performance.Count)
            foreach (var id in _song.Melody.Performance[performedIndex].WrittenIds)
                _lastPerformedOf[id] = performedIndex;
        QueueRedraw();
    }

    public void ClearMarks()
    {
        _marks.Clear();
        _captions.Clear();
        _lastPerformedOf.Clear();
        _heard = (0, double.NaN);
    }

    public void AddTrace(double beat, double writtenMidi) => _heard = (beat, writtenMidi);

    private float Space => Math.Clamp(Size.X / 80f, 9f, 13f);
    private float SystemHeight => Space * 15;
    private const float Margin = 16;

    private void EnsureLayout()
    {
        if (_song is null || _laidOutFor == Size)
            return;
        _laidOutFor = Size;
        _systems = StaffLayout.Layout(_song.Melody.Bars, _song.Time, _key, new LayoutOptions { LineWidth = (Size.X - 2 * Margin) / Space });
        _byWrittenId.Clear();
        for (var s = 0; s < _systems.Count; s++)
            foreach (var bar in _systems[s].Bars)
                foreach (var n in bar.Notes)
                    _byWrittenId[n.Note.Id] = (s, bar, n);
    }

    public override void _Process(double delta)
    {
        if (_song is null)
            return;
        EnsureLayout();
        // Keep the current line about a third of a line from the top.
        var current = ShowCursor ? CurrentPlace() : null;
        var target = current is { } c ? Math.Max(0, c.System * SystemHeight - SystemHeight * 0.35f) : 0;
        target = Math.Min(target, Math.Max(0, _systems.Count * SystemHeight - Size.Y + Space * 2));
        _scroll += (target - _scroll) * (float)Math.Min(1, delta * 5);
        QueueRedraw();
    }

    /// <summary>The performed note at the cursor (or the one being waited for), and where it is written.</summary>
    private (int Index, int System, PlacedBar Bar, PlacedNote Note)? CurrentPlace()
    {
        if (_song is null)
            return null;
        var performance = _song.Melody.Performance;
        int index;
        if (WaitingFor is { } w)
            index = Math.Min(w, performance.Count - 1);
        else
        {
            index = 0;
            for (var i = 0; i < performance.Count && performance[i].StartBeat <= CurrentBeat + 1e-9; i++)
                index = i;
        }
        var id = performance[index].WrittenIds[0];
        return _byWrittenId.TryGetValue(id, out var place) ? (index, place.System, place.Bar, place.Note) : null;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Colors.White with { A = 0.75f });
        if (_song is null || Services.Instance.MusicFont is not { } font)
            return;
        EnsureLayout();
        var sp = Space;
        var fontSize = (int)Math.Round(sp * 4);
        var ink = PipitTheme.Ink;
        var current = ShowCursor ? CurrentPlace() : null;

        for (var s = 0; s < _systems.Count; s++)
        {
            var top = s * SystemHeight - _scroll + sp * 5;   // top staff line
            if (top > Size.Y + sp * 6 || top + sp * 10 < 0)
                continue;
            var system = _systems[s];
            float Y(double position) => (float)(top + sp * 4 - position * sp / 2);
            float X(double x) => (float)(Margin + x * sp);

            for (var l = 0; l < 5; l++)
                DrawLine(new Vector2(X(0), Y(l * 2)), new Vector2(X(system.Width), Y(l * 2)), ink, LineW * sp);

            // Clef, key and time signature.
            DrawString(font, new Vector2(X(0.4), Y(2)), _octaveClef ? GClef8va : GClef, HorizontalAlignment.Left, -1, fontSize, ink);
            var kx = 3.6;
            foreach (var (step, i) in _key.Steps.Select((st, i) => (st, i)))
                DrawString(font, new Vector2(X(kx + i * 1.1), Y(KeyPosition(step, _key.Fifths > 0))), _key.Fifths > 0 ? Sharp : Flat, HorizontalAlignment.Left, -1, fontSize, ink);
            if (system.ShowTime)
            {
                var tx = X(system.ContentStart - 2.6);
                DrawString(font, new Vector2(tx, Y(6)), Digits(_song.Time.Upper), HorizontalAlignment.Left, -1, fontSize, ink);
                DrawString(font, new Vector2(tx, Y(2)), Digits(_song.Time.Lower), HorizontalAlignment.Left, -1, fontSize, ink);
            }

            foreach (var bar in system.Bars)
                DrawBar(font, fontSize, sp, bar, X, Y, current?.Note);

            // Ties (also into the next line, where they are drawn as far as the line's end).
            foreach (var bar in system.Bars)
                foreach (var n in bar.Notes.Where(n => n.Note.TieToNext && !n.IsRest))
                {
                    var x1 = X(bar.X + n.X) + HeadWidth * sp * 0.8f;
                    var next = NextWritten(n.Note.Id);
                    var x2 = next is { } nx && nx.System == s ? X(nx.Bar.X + nx.Note.X) + HeadWidth * sp * 0.2f : X(system.Width) - sp * 0.3f;
                    var below = n.StemUp;
                    var y = Y(n.Position) + (below ? sp * 0.6f : -sp * 0.6f);
                    DrawArcCurve(new Vector2(x1, y), new Vector2(x2, y), below ? sp * 0.7f : -sp * 0.7f, ink, sp);
                }

            // Cursor and the ghost of what is being played.
            if (current is { } c && c.System == s && (Overlay == "" || WaitingFor is not null))
            {
                var cx = CursorX(c, X, sp);
                DrawLine(new Vector2(cx, Y(12)), new Vector2(cx, Y(-4)), PipitTheme.Accent with { A = 0.7f }, 2);
                if (!double.IsNaN(_heard.Written) && Math.Abs(_heard.Beat - CurrentBeat) < 1 || WaitingFor is not null && !double.IsNaN(_heard.Written))
                {
                    var written = (int)Math.Round(_heard.Written);
                    var position = StaffLayout.PositionOf(Services.Spell(written));
                    var right = written == (_song.Melody.Performance[c.Index].Pitch.Midi);
                    var ghost = (right ? PipitTheme.Good : PipitTheme.Bad) with { A = 0.55f };
                    DrawLedgers(position, cx + sp * 0.4f, sp, ghost, Y);
                    DrawString(font, new Vector2(cx + sp * 0.4f, Y(position)), HeadBlack, HorizontalAlignment.Left, -1, fontSize, ghost);
                }
            }
        }

        if (Overlay != "")
            DrawString(PipitTheme.Font, new Vector2(0, Size.Y / 2 + 30), Overlay, HorizontalAlignment.Center, Size.X, 84, PipitTheme.Accent);
    }

    private float CursorX((int Index, int System, PlacedBar Bar, PlacedNote Note) c, Func<double, float> X, float sp)
    {
        var x = X(c.Bar.X + c.Note.X) - sp * 0.3f;
        if (WaitingFor is not null || _song is null)
            return x;
        // Glide towards the next note in proportion to the time elapsed.
        var performance = _song.Melody.Performance;
        var note = performance[c.Index];
        var fraction = Math.Clamp((CurrentBeat - note.StartBeat) / Math.Max(0.01, note.Beats), 0, 1);
        var endX = X(c.Bar.X + c.Bar.Width) - sp * 0.5f;
        if (c.Index + 1 < performance.Count && _byWrittenId.TryGetValue(performance[c.Index + 1].WrittenIds[0], out var next) && next.System == c.System)
            endX = X(next.Bar.X + next.Note.X) - sp * 0.3f;
        return x + (float)fraction * Math.Max(0, endX - x);
    }

    private void DrawBar(Font font, int fontSize, float sp, PlacedBar bar, Func<double, float> X, Func<double, float> Y, PlacedNote? current)
    {
        var ink = PipitTheme.Ink;
        var left = X(bar.X);
        var right = X(bar.X + bar.Width);
        var last = _song!.Melody.Bars[^1] == bar.Bar;

        // Bar lines and repeats.
        void Line(float x, float width) => DrawLine(new Vector2(x, Y(8)), new Vector2(x, Y(0)), ink, width * sp);
        void Dots(float x) { DrawString(font, new Vector2(x, Y(5)), RepeatDot, HorizontalAlignment.Left, -1, fontSize, ink); DrawString(font, new Vector2(x, Y(3)), RepeatDot, HorizontalAlignment.Left, -1, fontSize, ink); }
        if (bar.Bar.StartRepeat)
        {
            Line(left + ThickBar * sp / 2, ThickBar);
            Line(left + (ThickBar + 0.4f) * sp, ThinBar);
            Dots(left + (ThickBar + 0.7f) * sp);
        }
        if (bar.Bar.EndRepeat || last)
        {
            if (bar.Bar.EndRepeat)
                Dots(right - (ThickBar + 1.3f) * sp);
            Line(right - (ThickBar + 0.5f) * sp, ThinBar);
            Line(right - ThickBar * sp / 2, ThickBar);
        }
        else if (bar.Bar.DoubleBarAfter)
        {
            Line(right - 0.5f * sp, ThinBar);
            Line(right, ThinBar);
        }
        else
            Line(right, ThinBar);

        // First and second endings: one bracket over all their bars, with the number and the
        // hook only at the start.
        if (bar.Bar.Ending > 0)
        {
            var bars = _song.Melody.Bars;
            var index = bar.Bar.Number - 1;
            var starts = index == 0 || bars[index - 1].Ending != bar.Bar.Ending;
            var y = Y(13);
            DrawLine(new Vector2(left + (starts ? sp * 0.2f : 0), y), new Vector2(right - (bar.Bar.EndRepeat || bar.Bar.Ending == 2 ? sp * 0.8f : 0), y), ink, 1.5f);
            if (starts)
            {
                DrawLine(new Vector2(left + sp * 0.2f, y), new Vector2(left + sp * 0.2f, y + sp * 1.5f), ink, 1.5f);
                DrawString(PipitTheme.Font, new Vector2(left + sp * 0.6f, y + sp * 1.4f), bar.Bar.Ending + ".", HorizontalAlignment.Left, -1, (int)(sp * 1.4f), ink);
            }
        }

        foreach (var n in bar.Notes)
        {
            var x = X(bar.X + n.X);
            var color = ColorOf(n, current);
            if (n.IsRest)
            {
                var (glyph, position) = n.Value switch
                {
                    NoteValue.Whole => (RestWhole, 6), NoteValue.Half => (RestHalf, 4), NoteValue.Quarter => (RestQuarter, 4),
                    NoteValue.Eighth => (Rest8th, 4), NoteValue.Sixteenth => (Rest16th, 4), _ => (Rest32nd, 4),
                };
                DrawString(font, new Vector2(x, Y(position)), glyph, HorizontalAlignment.Left, -1, fontSize, color);
                DrawDots(font, fontSize, n, x + sp * 1.4f, 5, color, Y);
                continue;
            }
            DrawLedgers(n.Position, x, sp, color, Y);
            if (n.Accidental != Accidental.None)
            {
                var glyph = n.Accidental switch { Accidental.Sharp => Sharp, Accidental.Flat => Flat, _ => Natural };
                DrawString(font, new Vector2(x - sp * 1.25f, Y(n.Position)), glyph, HorizontalAlignment.Left, -1, fontSize, color);
            }
            var head = n.Value switch { NoteValue.Whole => HeadWhole, NoteValue.Half => HeadHalf, _ => HeadBlack };
            DrawString(font, new Vector2(x, Y(n.Position)), head, HorizontalAlignment.Left, -1, fontSize, color);
            DrawDots(font, fontSize, n, x + (n.Value == NoteValue.Whole ? WholeWidth : HeadWidth) * sp + sp * 0.3f, n.Position % 2 == 0 ? n.Position + 1 : n.Position, color, Y);

            if (n.Value != NoteValue.Whole)
            {
                var stemX = n.StemUp ? x + (HeadWidth - StemW / 2) * sp : x + StemW / 2 * sp;
                var from = Y(n.Position) + (n.StemUp ? -StemAnchorY : StemAnchorY) * sp;
                DrawLine(new Vector2(stemX, from), new Vector2(stemX, Y(n.StemEnd)), color, StemW * sp);
                if (n.BeamGroup is null && n.Beams > 0)
                {
                    var flag = (n.Beams, n.StemUp) switch
                    {
                        (1, true) => Flag8Up, (1, false) => Flag8Down, (2, true) => Flag16Up, (2, false) => Flag16Down,
                        (_, true) => Flag32Up, _ => Flag32Down,
                    };
                    DrawString(font, new Vector2(stemX - StemW / 2 * sp, Y(n.StemEnd)), flag, HorizontalAlignment.Left, -1, fontSize, color);
                }
            }
            if (n.Note.BreathAfter)
                DrawString(font, new Vector2(x + sp * 2f, Y(10)), Breath, HorizontalAlignment.Left, -1, fontSize, ink);
            if (_lastPerformedOf.TryGetValue(n.Note.Id, out var captioned) && _captions.TryGetValue(captioned, out var caption))
            {
                // Half a beat early or late moves the word about two note-widths.
                var shift = (float)Math.Clamp(caption.Shift, -0.5, 0.5) * sp * 4.5f;
                DrawString(PipitTheme.BoldFont, new Vector2(x - sp * 1.5f + shift, Y(-6.5)), caption.Text, HorizontalAlignment.Center, HeadWidth * sp + sp * 3f, (int)(sp * 1.5f), caption.Color);
                if (caption.Sub != "")
                    DrawString(PipitTheme.Font, new Vector2(x - sp * 1.5f, Y(-10)), caption.Sub, HorizontalAlignment.Center, HeadWidth * sp + sp * 3f, (int)(sp * 1.3f), PipitTheme.InkSoft with { A = 0.75f });
            }
            else if (ShowNames)
            {
                var label = NoteLabel(n.Note.Pitch!.Value);
                DrawString(PipitTheme.Font, new Vector2(x - sp * 0.6f, Y(-6)), label, HorizontalAlignment.Center, HeadWidth * sp + sp * 1.2f, (int)(sp * 1.25f), color == PipitTheme.Ink ? PipitTheme.InkSoft : color);
            }
        }

        // Beams: a primary beam over each group; for sixteenths and thirty-seconds a second and
        // third one between consecutive notes that have them, and a short stub for a lone one.
        foreach (var group in bar.Notes.Where(n => n.BeamGroup is not null).GroupBy(n => n.BeamGroup))
        {
            var g = group.ToList();
            var up = g[0].StemUp;
            Vector2 Tip(PlacedNote n) => new((n.StemUp ? X(bar.X + n.X) + (HeadWidth - StemW / 2) * sp : X(bar.X + n.X) + StemW / 2 * sp), Y(n.StemEnd));
            var color = g.Select(n => ColorOf(n, current)).FirstOrDefault(c => c != PipitTheme.Ink, PipitTheme.Ink);
            Beam(Tip(g[0]), Tip(g[^1]), up, 0, sp, color);
            for (var level = 1; level <= 2; level++)
                for (var i = 0; i < g.Count; i++)
                {
                    if (g[i].Beams <= level)
                        continue;
                    var prev = i > 0 && g[i - 1].Beams > level;
                    var next = i + 1 < g.Count && g[i + 1].Beams > level;
                    if (next)
                        Beam(Tip(g[i]), Tip(g[i + 1]), up, level, sp, color);
                    else if (!prev)
                    {
                        var a = Tip(g[i]);
                        var b = i + 1 < g.Count ? Tip(g[i + 1]) : Tip(g[i - 1]);
                        var stub = a + (b - a).Normalized() * sp * 1.1f;
                        Beam(i + 1 < g.Count ? a : stub, i + 1 < g.Count ? stub : a, up, level, sp, color);
                    }
                }
        }

        // Triplets: a small 3 beyond the beam (or the notes), centred over the group.
        foreach (var (from, to) in bar.Triplets)
        {
            var a = bar.Notes[from];
            var b = bar.Notes[to];
            var up = a.IsRest ? b.StemUp : a.StemUp;
            var notes = bar.Notes.Skip(from).Take(to - from + 1).ToList();
            var edge = up
                ? notes.Max(n => n.IsRest ? 8 : Math.Max(n.Position, n.StemEnd)) + 2
                : notes.Min(n => n.IsRest ? 0 : Math.Min(n.Position, n.StemEnd)) - 1;
            var mid = (X(bar.X + a.X) + X(bar.X + b.X)) / 2 + HeadWidth * sp / 2;
            DrawString(font, new Vector2(mid - sp * 0.5f, Y(edge)), Tuplet3, HorizontalAlignment.Left, -1, (int)(fontSize * 0.7f), ink);
        }
    }

    private void Beam(Vector2 a, Vector2 b, bool up, int level, float sp, Color color)
    {
        // Beams hang from the stem tips towards the noteheads.
        var shift = (up ? 1 : -1) * level * (BeamW + 0.25f) * sp;
        var thick = (up ? 1 : -1) * BeamW * sp;
        a.Y += shift;
        b.Y += shift;
        DrawColoredPolygon(new[] { a, b, b with { Y = b.Y + thick }, a with { Y = a.Y + thick } }, color);
    }

    private void DrawDots(Font font, int fontSize, PlacedNote n, float x, int position, Color color, Func<double, float> Y)
    {
        for (var d = 0; d < n.Dots; d++)
            DrawString(font, new Vector2(x + d * Space * 0.6f, Y(position)), Dot, HorizontalAlignment.Left, -1, fontSize, color);
    }

    private void DrawLedgers(int position, float x, float sp, Color color, Func<double, float> Y)
    {
        for (var p = -2; p >= position; p -= 2)
            DrawLine(new Vector2(x - LedgerExt * sp, Y(p)), new Vector2(x + (HeadWidth + LedgerExt) * sp, Y(p)), color, LedgerW * sp);
        for (var p = 10; p <= position; p += 2)
            DrawLine(new Vector2(x - LedgerExt * sp, Y(p)), new Vector2(x + (HeadWidth + LedgerExt) * sp, Y(p)), color, LedgerW * sp);
    }

    private void DrawArcCurve(Vector2 a, Vector2 b, float bulge, Color color, float sp)
    {
        var points = new Vector2[17];
        for (var i = 0; i < points.Length; i++)
        {
            var t = i / (float)(points.Length - 1);
            points[i] = a.Lerp(b, t) + new Vector2(0, bulge * 4 * t * (1 - t));
        }
        DrawPolyline(points, color, Math.Max(1.5f, 0.18f * sp), true);
    }

    private Color ColorOf(PlacedNote n, PlacedNote? current)
    {
        if (_lastPerformedOf.TryGetValue(n.Note.Id, out var performed) && _marks.TryGetValue(performed, out var mark))
            return mark switch
            {
                NoteMark.Perfect => PipitTheme.Good,
                NoteMark.Good => PipitTheme.Warn,
                NoteMark.Wrong => PipitTheme.Bad,
                NoteMark.Missed => PipitTheme.InkSoft,
                NoteMark.Current => PipitTheme.Accent,
                _ => PipitTheme.Ink,
            };
        return ReferenceEquals(n, current) && Overlay == "" ? PipitTheme.Accent : PipitTheme.Ink;
    }

    private (int System, PlacedBar Bar, PlacedNote Note)? NextWritten(int id)
    {
        var next = _byWrittenId.Keys.Where(k => k > id).DefaultIfEmpty(-1).Min();
        return next >= 0 ? _byWrittenId[next] : null;
    }

    /// <summary>Treble-clef positions of key-signature accidentals, in the usual zig-zag.</summary>
    private static int KeyPosition(Step step, bool sharp) => (step, sharp) switch
    {
        (Step.F, true) => 8, (Step.C, true) => 5, (Step.G, true) => 9, (Step.D, true) => 6, (Step.A, true) => 3, (Step.E, true) => 7, (Step.B, true) => 4,
        (Step.B, false) => 4, (Step.E, false) => 7, (Step.A, false) => 3, (Step.D, false) => 6, (Step.G, false) => 2, (Step.C, false) => 5, _ => 1,
    };

    private static string Digits(int value) => string.Concat(value.ToString().Select(c => (char)(TimeSigZero + (c - '0'))));
}
