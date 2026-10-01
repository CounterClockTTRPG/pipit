using System.Globalization;
using System.Text.Json;

namespace Pipit.Core.Music;

public sealed class SongFormatException(string message) : Exception(message);

/// <summary>
/// Reads the song files in data/songs. The music of a part is a compact string:
/// <code>
///   G4/q G4 G4 A4 | B4/h A4 | G4/q B4 A4 A4 | G4/w
/// </code>
/// <list type="bullet">
/// <item><c>PITCH[/DUR]</c>: G4, F#4, Bb4. DUR is w h q e s, with a trailing "." for dotted. The
/// duration carries over to following notes until it changes.</item>
/// <item><c>R[/DUR]</c>: a rest.</item>
/// <item>A trailing <c>~</c> ties a note to the next one (same pitch); a trailing <c>,</c> marks a breath.</item>
/// <item><c>|</c> bar line, <c>||</c> double bar, <c>|:</c> and <c>:|</c> repeats, <c>[1</c> and <c>[2</c> first and second endings.</item>
/// </list>
/// Every bar must add up to the time signature, except a pickup bar (declared with "pickup")
/// and a final bar that completes it.
/// </summary>
public static class SongParser
{
    private static readonly Dictionary<char, double> Durations = new() { ['w'] = 4, ['h'] = 2, ['q'] = 1, ['e'] = 0.5, ['s'] = 0.25 };

    public static Song ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        string Str(string name, string? fallback = null) =>
            root.TryGetProperty(name, out var v) ? v.GetString() ?? fallback ?? "" : fallback ?? throw new SongFormatException($"Missing \"{name}\"");

        var id = Str("id");
        var titles = new Dictionary<string, string>();
        if (root.TryGetProperty("title", out var title))
        {
            if (title.ValueKind == JsonValueKind.String)
                titles["en"] = title.GetString()!;
            else
                foreach (var p in title.EnumerateObject())
                    titles[p.Name] = p.Value.GetString() ?? "";
        }
        if (titles.Count == 0)
            titles["en"] = id;

        var time = TimeSignature.Parse(Str("time", "4/4"));
        var pickup = root.TryGetProperty("pickup", out var pk) ? ParseDuration(pk.GetString() ?? "", id) : 0;
        if (!root.TryGetProperty("parts", out var partsJson) || partsJson.GetArrayLength() == 0)
            throw new SongFormatException($"{id}: no parts");

        var parts = new List<SongPart>();
        foreach (var p in partsJson.EnumerateArray())
        {
            var partId = p.TryGetProperty("id", out var pid) ? pid.GetString() ?? "melody" : "melody";
            var context = $"{id}/{partId}";
            var music = p.GetProperty("music").GetString() ?? throw new SongFormatException($"{context}: no music");
            var (bars, performance) = ParseMusic(music, time, pickup, context);
            parts.Add(new SongPart(partId,
                p.TryGetProperty("name", out var n) ? n.GetString() ?? partId : partId,
                p.TryGetProperty("instrument", out var ins) ? ins.GetString() ?? "soprano" : "soprano",
                bars, performance));
        }

        return new Song
        {
            Id = id,
            Titles = titles,
            Composer = Str("composer", ""),
            Chapter = root.TryGetProperty("chapter", out var ch) ? ch.GetInt32() : 0,
            Tempo = root.TryGetProperty("tempo", out var tempo) ? tempo.GetDouble() : 90,
            Time = time,
            Key = Str("key", "C"),
            Pickup = pickup,
            Parts = parts,
        };
    }

    public static double ParseDuration(string text, string context)
    {
        if (text.Length is < 1 or > 2 || !Durations.TryGetValue(text[0], out var beats) || (text.Length == 2 && text[1] != '.'))
            throw new SongFormatException($"{context}: bad duration '{text}' (use w h q e s, optionally dotted)");
        return text.Length == 2 ? beats * 1.5 : beats;
    }

    private enum Mark { Note, Bar, DoubleBar, RepeatStart, RepeatEnd, Ending1, Ending2 }

    private sealed record Token(Mark Mark, WrittenNote? Note = null);

    /// <summary>Parses one part's music into written bars and the performed note sequence.</summary>
    public static (IReadOnlyList<Bar> Bars, IReadOnlyList<PerformedNote> Performance) ParseMusic(string music, TimeSignature time, double pickup, string context)
    {
        var tokens = Tokenise(music, context);

        // Written bars, as notated (repeats not expanded), for the staff view and validation.
        var bars = new List<Bar>();
        var current = new List<WrittenNote>();
        void CloseBar()
        {
            if (current.Count > 0)
                bars.Add(new Bar(bars.Count + 1, current.ToList()));
            current.Clear();
        }
        foreach (var t in tokens)
        {
            if (t.Mark == Mark.Note)
                current.Add(t.Note!);
            else if (t.Mark is Mark.Bar or Mark.DoubleBar or Mark.RepeatStart or Mark.RepeatEnd)
                CloseBar();
        }
        CloseBar();
        Validate(bars, time, pickup, context);

        // Performance order: expand |: ... :| with optional [1 / [2 endings.
        var order = PerformanceOrder(tokens, context);
        return (bars, Perform(order, pickup, context));
    }

    private static List<Token> Tokenise(string music, string context)
    {
        var tokens = new List<Token>();
        var duration = 1.0;
        foreach (var raw in music.Split((char[])[' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw)
            {
                case "|": tokens.Add(new Token(Mark.Bar)); continue;
                case "||": tokens.Add(new Token(Mark.DoubleBar)); continue;
                case "|:": tokens.Add(new Token(Mark.RepeatStart)); continue;
                case ":|": tokens.Add(new Token(Mark.RepeatEnd)); continue;
                case "[1": tokens.Add(new Token(Mark.Ending1)); continue;
                case "[2": tokens.Add(new Token(Mark.Ending2)); continue;
            }
            var text = raw;
            var breath = text.EndsWith(',');
            if (breath)
                text = text[..^1];
            var tie = text.EndsWith('~');
            if (tie)
                text = text[..^1];
            var slash = text.IndexOf('/');
            if (slash >= 0)
            {
                duration = ParseDuration(text[(slash + 1)..], context);
                text = text[..slash];
            }
            Pitch? pitch = null;
            if (text != "R")
            {
                try { pitch = Pitch.Parse(text); }
                catch (FormatException) { throw new SongFormatException($"{context}: cannot read '{raw}'"); }
            }
            tokens.Add(new Token(Mark.Note, new WrittenNote(pitch, duration, tie, breath)));
        }
        return tokens;
    }

    private static void Validate(List<Bar> bars, TimeSignature time, double pickup, string context)
    {
        if (bars.Count == 0)
            throw new SongFormatException($"{context}: no notes");
        for (var i = 0; i < bars.Count; i++)
        {
            var expected = time.BarBeats;
            if (i == 0 && pickup > 0)
                expected = pickup;
            else if (i == bars.Count - 1 && pickup > 0 && Math.Abs(bars[i].Beats - (time.BarBeats - pickup)) < 1e-9)
                expected = time.BarBeats - pickup; // the last bar completes the pickup
            if (Math.Abs(bars[i].Beats - expected) > 1e-9)
                throw new SongFormatException(string.Create(CultureInfo.InvariantCulture,
                    $"{context}: bar {bars[i].Number} has {bars[i].Beats} beats, expected {expected}"));
        }
    }

    /// <summary>Notes (and bar boundaries) in the order they are played.</summary>
    private static List<Token> PerformanceOrder(List<Token> tokens, string context)
    {
        var result = new List<Token>();
        var repeatStart = 0; // index into tokens of the current repeat section
        var i = 0;
        var taken = new HashSet<int>(); // repeat-end indices already taken once
        var skipEnding1 = false;
        while (i < tokens.Count)
        {
            var t = tokens[i];
            switch (t.Mark)
            {
                case Mark.RepeatStart:
                    repeatStart = i + 1;
                    result.Add(new Token(Mark.Bar));
                    break;
                case Mark.RepeatEnd when !taken.Contains(i):
                    taken.Add(i);
                    result.Add(new Token(Mark.Bar));
                    i = repeatStart;
                    skipEnding1 = true; // second time round, jump over the first ending
                    continue;
                case Mark.Ending1 when skipEnding1:
                    // Skip to the matching second ending.
                    var j = i + 1;
                    while (j < tokens.Count && tokens[j].Mark != Mark.Ending2)
                        j++;
                    if (j == tokens.Count)
                        throw new SongFormatException($"{context}: [1 without [2");
                    i = j + 1;
                    skipEnding1 = false;
                    continue;
                case Mark.Ending2:
                case Mark.Ending1:
                    break;
                case Mark.RepeatEnd:
                case Mark.Bar:
                case Mark.DoubleBar:
                    result.Add(new Token(Mark.Bar));
                    // A plain repeat (no endings) ends here once it has been played twice.
                    if (t.Mark == Mark.RepeatEnd)
                        skipEnding1 = false;
                    break;
                default:
                    result.Add(t);
                    break;
            }
            i++;
        }
        return result;
    }

    private static List<PerformedNote> Perform(List<Token> order, double pickup, string context)
    {
        var notes = new List<PerformedNote>();
        var beat = -pickup;
        var bar = pickup > 0 ? 0 : 1;
        var barHasNotes = false;
        PerformedNote? tied = null;
        foreach (var t in order)
        {
            if (t.Mark == Mark.Bar)
            {
                if (barHasNotes)
                    bar++;
                barHasNotes = false;
                continue;
            }
            var n = t.Note!;
            barHasNotes = true;
            if (n.Pitch is { } pitch)
            {
                if (tied is not null)
                {
                    if (tied.Pitch.Midi != pitch.Midi)
                        throw new SongFormatException($"{context}: tie between different pitches {tied.Pitch} and {pitch}");
                    notes[^1] = tied = tied with { Beats = tied.Beats + n.Beats };
                }
                else
                {
                    notes.Add(new PerformedNote(notes.Count, pitch, beat, n.Beats, bar));
                    tied = notes[^1];
                }
                if (!n.TieToNext)
                    tied = null;
            }
            else
                tied = null;
            beat += n.Beats;
        }
        if (tied is not null && notes.Count > 0 && order.LastOrDefault(t => t.Mark == Mark.Note)?.Note?.TieToNext == true)
            throw new SongFormatException($"{context}: the last note is tied to nothing");
        return notes;
    }
}
