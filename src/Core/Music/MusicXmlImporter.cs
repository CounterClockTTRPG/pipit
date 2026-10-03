using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Pipit.Core.Music;

/// <summary>A MusicXML file turned into a Pipit song file (JSON), or why it could not be.</summary>
public sealed record ImportResult(string? Json, string Id, string Title, string? Error, IReadOnlyList<string> Notes)
{
    public bool Ok => Json is not null;
}

/// <summary>
/// Imports a player's own MusicXML (.musicxml, .xml, or compressed .mxl) as a song. The first
/// part's top line is the player's (moved by octaves to fit a soprano, or an alto); the other
/// staves and parts become the band (a bass line from lower staves, the top line of other parts).
/// Keeps pickups, ties, repeats with first and second endings, and triplets; grace notes are
/// dropped, and the player's notes that fall outside the recorder are moved by an octave. A score
/// whose time signature changes becomes one song per section. Refuses tuplets other than triplets.
/// </summary>
public static class MusicXmlImporter
{
    // Ticks per quarter: whole numbers for 32nds (24) and for triplet 32nds (16).
    private const int Tpq = 192;

    private static readonly (int Ticks, string Name)[] Plain =
        { (1152, "w."), (768, "w"), (576, "h."), (384, "h"), (288, "q."), (192, "q"), (144, "e."), (96, "e"), (72, "s."), (48, "s"), (24, "t") };
    private static readonly (int Ticks, string Name)[] Triplet =
        { (256, "h3"), (128, "q3"), (64, "e3"), (32, "s3"), (16, "t3") };

    private static readonly string[] Steps = { "C", "D", "E", "F", "G", "A", "B" };
    private static readonly int[] StepSemitones = { 0, 2, 4, 5, 7, 9, 11 };

    private sealed record Note(int Staff, int Start, int Duration, int? Midi, int Step, int Alter, int Octave, bool TieStart);

    private sealed class Measure
    {
        public int Start, Length;
        public (int Beats, int BeatType) Time;
        public double? Tempo;
        /// <summary>Why this measure cannot be written, if it cannot (its section is then left out).</summary>
        public string? Problem;
        public bool RepeatStart, RepeatEnd;
        public int Ending;
        public readonly List<Note> Notes = new();
    }

    private sealed record PartData(string Name, List<Measure> Measures, int Staves);

    /// <summary>
    /// Reads the bytes of a .musicxml/.xml file or a compressed .mxl: one song, or one per section
    /// when the time signature changes. On failure a single result with the reason.
    /// </summary>
    public static IReadOnlyList<ImportResult> Import(byte[] data, string fileName)
    {
        try
        {
            var xml = IsZip(data) ? ReadMxl(data) : Encoding.UTF8.GetString(data);
            // Exported MusicXML starts with a DOCTYPE; read it without fetching the DTD.
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore, XmlResolver = null };
            using var reader = System.Xml.XmlReader.Create(new StringReader(xml), settings);
            return Convert(XDocument.Load(reader), fileName);
        }
        catch (ImportException e)
        {
            return new[] { new ImportResult(null, "", Path.GetFileNameWithoutExtension(fileName), e.Message, Array.Empty<string>()) };
        }
        catch (Exception e) when (e is System.Xml.XmlException or InvalidDataException or FormatException)
        {
            return new[] { new ImportResult(null, "", Path.GetFileNameWithoutExtension(fileName), "This is not a MusicXML file I can read: " + e.Message, Array.Empty<string>()) };
        }
    }

    private sealed class ImportException(string message) : Exception(message);

    private static bool IsZip(byte[] data) => data.Length > 4 && data[0] == 'P' && data[1] == 'K';

    /// <summary>The score inside an .mxl: the root file named in META-INF/container.xml.</summary>
    private static string ReadMxl(byte[] data)
    {
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        string? rootPath = null;
        if (zip.GetEntry("META-INF/container.xml") is { } container)
        {
            using var reader = new StreamReader(container.Open());
            rootPath = XDocument.Parse(reader.ReadToEnd()).Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
        }
        var entry = (rootPath is not null ? zip.GetEntry(rootPath) : null)
            ?? zip.Entries.FirstOrDefault(e => !e.FullName.StartsWith("META-INF") && (e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".musicxml")))
            ?? throw new ImportException("The .mxl file holds no score.");
        using var score = new StreamReader(entry.Open());
        return score.ReadToEnd();
    }

    private static IReadOnlyList<ImportResult> Convert(XDocument doc, string fileName)
    {
        var root = doc.Root ?? throw new ImportException("The file is empty.");
        if (root.Name.LocalName == "score-timewise")
            throw new ImportException("Timewise MusicXML is not supported; export the score as partwise MusicXML.");
        var title = Text(root.Element("work")?.Element("work-title")) ?? Text(root.Element("movement-title"))
            ?? root.Elements("credit").Select(c => Text(c.Element("credit-words"))).FirstOrDefault(t => t is not null)
            ?? Path.GetFileNameWithoutExtension(fileName);
        var composer = root.Element("identification")?.Elements("creator").FirstOrDefault(c => (string?)c.Attribute("type") == "composer") is { } c0
            ? Text(c0) ?? "" : "";
        var partNames = root.Element("part-list")?.Elements("score-part")
            .ToDictionary(p => (string?)p.Attribute("id") ?? "", p => Text(p.Element("part-name")) ?? "") ?? new();

        var parts = new List<PartData>();
        int fifths = 0, graces = 0;
        var minor = false;
        foreach (var part in root.Elements("part"))
        {
            var (data, partFifths, partMinor, partGraces) = ReadPart(part, partNames.GetValueOrDefault((string?)part.Attribute("id") ?? "", ""));
            if (data.Measures.Count == 0 || data.Measures.All(m => m.Notes.All(n => n.Midi is null)))
                continue;
            if (parts.Count == 0)
                (fifths, minor, graces) = (partFifths, partMinor, partGraces);
            parts.Add(data);
        }
        if (parts.Count == 0 || parts[0].Measures[0].Time == default)
            throw new ImportException("The score has no notes.");

        // One song per run of measures in the same time signature.
        var measures = parts[0].Measures;
        var sections = new List<(int From, int To)>();
        for (var i = 0; i < measures.Count; i++)
            if (i == 0 || measures[i].Time != measures[i - 1].Time)
                sections.Add((i, measures.Count));
        for (var k = 0; k + 1 < sections.Count; k++)
            sections[k] = (sections[k].From, sections[k + 1].From);
        var hash = Hash(doc.ToString(SaveOptions.DisableFormatting));
        var results = new List<ImportResult>();
        double? tempo = null;
        for (var k = 0; k < sections.Count; k++)
        {
            var (from, to) = sections[k];
            tempo = measures.Take(from + 1).LastOrDefault(m => m.Tempo is not null)?.Tempo ?? tempo;
            var name = sections.Count > 1 ? $"{title} ({k + 1})" : title;
            var slice = parts.Select(p => p with { Measures = p.Measures.Skip(from).Take(to - from).ToList() }).ToList();
            try
            {
                var notes = new List<string>();
                if (graces > 0 && k == 0)
                    notes.Add($"{graces} grace notes left out");
                if (sections.Count > 1)
                    notes.Add($"part {k + 1} of {sections.Count}: bars {from + 1}-{to}, the time signature changes there");
                results.Add(Song(slice, name, composer, fifths, minor, tempo, fileName, $"own-{Slug(title)}-{hash}" + (sections.Count > 1 ? $"-{k + 1}" : ""), notes));
            }
            catch (ImportException e)
            {
                results.Add(new ImportResult(null, "", name, e.Message, Array.Empty<string>()));
            }
        }
        return results;
    }

    private static ImportResult Song(List<PartData> parts, string title, string composer, int fifths, bool minor, double? tempo, string fileName, string id, List<string> notes)
    {
        var (beats, beatType) = parts[0].Measures[0].Time;
        var barTicks = beats * 4 * Tpq / beatType;

        if (parts.SelectMany(p => p.Measures).FirstOrDefault(m => m.Problem is not null) is { Problem: { } problem })
            throw new ImportException(char.ToUpperInvariant(problem[0]) + problem[1..] + ".");
        // The player's line: the first part, first staff, top notes.
        var melodyPart = parts[0];
        var melody = Line(melodyPart.Measures, staff: 1, top: true);
        var midis = melody.SelectMany(m => m.Notes).Where(n => n.Midi is not null).Select(n => n.Midi!.Value).ToList();
        if (midis.Count == 0)
            throw new ImportException("The first part has no notes.");
        var (shift, instrument, low, high) = FitRecorder(midis.Min(), midis.Max());
        if (shift != 0)
            notes.Add(shift > 0 ? $"moved up {shift / 12} octave(s)" : $"moved down {-shift / 12} octave(s)");
        // Notes the recorder cannot reach are moved by an octave, as editions for recorder do.
        var folded = 0;
        melody = melody.Select(m =>
        {
            var copy = new Measure { Start = m.Start, Length = m.Length, Time = m.Time, RepeatStart = m.RepeatStart, RepeatEnd = m.RepeatEnd, Ending = m.Ending };
            foreach (var n in m.Notes)
            {
                var octaves = 0;
                while (n.Midi + shift + 12 * octaves < low) octaves++;
                while (n.Midi + shift + 12 * octaves > high) octaves--;
                folded += octaves != 0 ? 1 : 0;
                copy.Notes.Add(n with { Midi = n.Midi + 12 * octaves, Octave = n.Octave + octaves });
            }
            return copy;
        }).ToList();
        if (folded > 0)
            notes.Add($"{folded} note(s) outside the recorder moved by an octave");

        // The pickup: a short first bar, written as one note value when it can be.
        var first = melodyPart.Measures[0].Length;
        string? pickup = null;
        var padFirst = 0;
        if (first < barTicks)
        {
            pickup = Plain.Concat(Triplet).FirstOrDefault(d => d.Ticks == first).Name;
            if (pickup is null)
                padFirst = barTicks - first; // an odd pickup: start with a rest that makes a full bar
        }

        var json = new Dictionary<string, object?>();
        json["id"] = id;
        json["title"] = new Dictionary<string, string> { ["en"] = title };
        json["composer"] = composer;
        json["period"] = "own";
        json["chapter"] = 0;
        json["tempo"] = Math.Round(tempo ?? 90);
        json["time"] = $"{beats}/{beatType}";
        json["key"] = KeyName(fifths, minor);
        if (pickup is not null)
            json["pickup"] = pickup;
        json["source"] = "Imported from " + Path.GetFileName(fileName) + (notes.Count > 0 ? " (" + string.Join("; ", notes) + ")" : "");
        json["parts"] = new[] { new Dictionary<string, object> { ["id"] = "melody", ["name"] = Clean(melodyPart.Name, "Melody"), ["instrument"] = instrument, ["music"] = Music(melody, shift, padFirst, fifths) } };

        // The band: lower staves of the first part, then every other part.
        var band = new List<Dictionary<string, object>>();
        for (var staff = 2; staff <= melodyPart.Staves; staff++)
            band.Add(BandPart(melodyPart, staff, top: false, shift, padFirst, fifths, band.Count));
        foreach (var other in parts.Skip(1))
        {
            band.Add(BandPart(other, 1, top: true, shift, padFirst, fifths, band.Count));
            for (var staff = 2; staff <= other.Staves; staff++)
                band.Add(BandPart(other, staff, top: false, shift, padFirst, fifths, band.Count));
        }
        band.RemoveAll(b => ((string)b["music"]).Split(' ').All(t => t.StartsWith("R/") || t is "|" or "|:" or ":|" or "[1" or "[2" or ""));
        if (band.Count > 0)
            json["accompaniment"] = band;

        var text = JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        // The song must load like any other: let the parser judge it.
        try
        {
            SongParser.ParseJson(text);
        }
        catch (SongFormatException e)
        {
            throw new ImportException("The score could not be turned into a song: " + e.Message);
        }
        return new ImportResult(text, id, title, null, notes);
    }

    private static Dictionary<string, object> BandPart(PartData part, int staff, bool top, int shift, int padFirst, int fifths, int index)
    {
        var line = Line(part.Measures, staff, top);
        return new Dictionary<string, object>
        {
            ["id"] = $"band{index + 1}",
            ["name"] = Clean(part.Name, "Band") + (part.Staves > 1 ? $" {staff}" : ""),
            ["sound"] = SoundFor(part.Name, staff),
            ["music"] = Music(line, shift, padFirst, fifths),
        };
    }

    private static (PartData Part, int Fifths, bool Minor, int Graces) ReadPart(XElement part, string name)
    {
        var measures = new List<Measure>();
        int divisions = 1, start = 0, staves = 1, fifths = 0, graces = 0;
        var minor = false;
        (int, int) time = default;
        var lastStart = new Dictionary<int, int>();
        foreach (var m in part.Elements("measure"))
        {
            var measure = new Measure { Start = start };
            double? tempo = null;
            int pos = 0, maxPos = 0;
            foreach (var e in m.Elements())
            {
                switch (e.Name.LocalName)
                {
                    case "attributes":
                        if (Int(e.Element("divisions")) is { } d) divisions = d;
                        if (Int(e.Element("staves")) is { } st) staves = st;
                        if (e.Element("key") is { } key && measures.Count == 0)
                        {
                            fifths = Int(key.Element("fifths")) ?? 0;
                            minor = Text(key.Element("mode")) == "minor";
                        }
                        if (e.Element("time") is { } t && Int(t.Element("beats")) is { } b && Int(t.Element("beat-type")) is { } bt)
                            time = (b, bt);
                        break;
                    case "direction":
                        if (tempo is null && e.Element("sound")?.Attribute("tempo") is { } tempoAttr && double.TryParse(tempoAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var q))
                            tempo = q;
                        break;
                    case "sound":
                        if (tempo is null && e.Attribute("tempo") is { } ta && double.TryParse(ta.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var q2))
                            tempo = q2;
                        break;
                    case "backup":
                        pos -= Ticks(Int(e.Element("duration")) ?? 0, divisions);
                        break;
                    case "forward":
                        pos += Ticks(Int(e.Element("duration")) ?? 0, divisions);
                        maxPos = Math.Max(maxPos, pos);
                        break;
                    case "barline":
                        if (e.Element("repeat") is { } rep)
                        {
                            if ((string?)rep.Attribute("direction") == "forward") measure.RepeatStart = true;
                            else measure.RepeatEnd = true;
                        }
                        if (e.Element("ending") is { } end && (string?)end.Attribute("type") == "start")
                            measure.Ending = int.TryParse(((string?)end.Attribute("number") ?? "1").Split(',')[0].Trim(), out var en) ? en : 1;
                        break;
                    case "note":
                        if (e.Element("grace") is not null)
                        {
                            graces++;
                            break;
                        }
                        if (e.Element("cue") is not null)
                            break;
                        var staffNo = Int(e.Element("staff")) ?? 1;
                        var dur = Ticks(Int(e.Element("duration")) ?? 0, divisions);
                        if (e.Element("time-modification") is { } tm && (Int(tm.Element("actual-notes")), Int(tm.Element("normal-notes"))) is not (3, 2))
                            measure.Problem = $"bar {measures.Count + 1} has tuplets other than triplets, which Pipit cannot write yet";
                        var chord = e.Element("chord") is not null;
                        var at = chord ? lastStart.GetValueOrDefault(staffNo, pos) : pos;
                        if (!chord)
                        {
                            lastStart[staffNo] = pos;
                            pos += dur;
                            maxPos = Math.Max(maxPos, pos);
                        }
                        if (e.Element("pitch") is { } p)
                        {
                            var step = Array.IndexOf(Steps, Text(p.Element("step")));
                            var alter = (int)Math.Round(double.Parse(Text(p.Element("alter")) ?? "0", CultureInfo.InvariantCulture));
                            var octave = Int(p.Element("octave")) ?? 4;
                            var midi = 12 * (octave + 1) + StepSemitones[Math.Max(0, step)] + alter;
                            var tieStart = e.Elements("tie").Any(t => (string?)t.Attribute("type") == "start");
                            measure.Notes.Add(new Note(staffNo, at, dur, midi, Math.Max(0, step), alter, octave, tieStart));
                        }
                        break;
                }
            }
            // A measure lasts as long as its longest voice (an incomplete first measure is a pickup).
            measure.Length = maxPos;
            measure.Time = time;
            measure.Tempo = tempo;
            measures.Add(measure);
            start += maxPos;
        }
        return (new PartData(name, measures, staves), fifths, minor, graces);
    }

    /// <summary>One line through a staff: per start, the top (or bottom) note; each lasts until the next.</summary>
    private static List<Measure> Line(List<Measure> measures, int staff, bool top)
    {
        var result = new List<Measure>();
        foreach (var m in measures)
        {
            var line = new Measure { Start = m.Start, Length = m.Length, Time = m.Time, RepeatStart = m.RepeatStart, RepeatEnd = m.RepeatEnd, Ending = m.Ending };
            var chosen = m.Notes.Where(n => n.Staff == staff && n.Midi is not null)
                .GroupBy(n => n.Start)
                .Select(g => top ? g.MaxBy(n => n.Midi)! : g.MinBy(n => n.Midi)!)
                .OrderBy(n => n.Start).ToList();
            for (var i = 0; i < chosen.Count; i++)
            {
                var n = chosen[i];
                var end = Math.Min(n.Start + n.Duration, i + 1 < chosen.Count ? chosen[i + 1].Start : m.Length);
                if (end > n.Start)
                    line.Notes.Add(n with { Duration = end - n.Start });
            }
            result.Add(line);
        }
        return result;
    }

    /// <summary>The music string of a line: rests fill the gaps, notes split into writable values.</summary>
    private static string Music(List<Measure> line, int shift, int padFirst, int fifths)
    {
        var sb = new StringBuilder();
        var flats = fifths < 0;
        for (var mi = 0; mi < line.Count; mi++)
        {
            var m = line[mi];
            var tokens = new List<string>();
            if (m.RepeatStart) tokens.Add("|:");
            if (m.Ending is 1 or 2) tokens.Add("[" + m.Ending);
            var pos = 0;
            if (mi == 0 && padFirst > 0)
                tokens.AddRange(Durations(padFirst).Select(d => "R/" + d));
            for (var i = 0; i < m.Notes.Count; i++)
            {
                var n = m.Notes[i];
                if (n.Start > pos)
                    tokens.AddRange(Durations(n.Start - pos).Select(d => "R/" + d));
                var name = PitchName(n.Midi!.Value + shift, n, shift, flats);
                // Tied on to the next note when it is the same pitch starting right after (also into the next bar).
                var next = i + 1 < m.Notes.Count ? m.Notes[i + 1] : mi + 1 < line.Count ? line[mi + 1].Notes.FirstOrDefault() : null;
                var tiedOn = n.TieStart && next is not null && next.Midi == n.Midi
                    && (next.Start == n.Start + n.Duration || (i + 1 >= m.Notes.Count && next.Start == 0 && n.Start + n.Duration == m.Length));
                var pieces = Durations(n.Duration);
                for (var k = 0; k < pieces.Count; k++)
                    tokens.Add($"{name}/{pieces[k]}" + (k < pieces.Count - 1 || tiedOn ? "~" : ""));
                pos = n.Start + n.Duration;
            }
            if (pos < m.Length)
                tokens.AddRange(Durations(m.Length - pos).Select(d => "R/" + d));
            if (tokens.All(t => t is "|:" or "[1" or "[2"))
                continue; // an empty measure (no notes, no length)
            if (sb.Length > 0)
                sb.Append(" | ");
            sb.Append(string.Join(' ', tokens));
            if (m.RepeatEnd)
                sb.Append(" :|");
        }
        // A repeat sign is the bar line itself; back-to-back repeats stay two signs (":| |:").
        return sb.ToString().Replace(":| | ", ":| ").Replace("| |: ", "|: ").Replace(":|: ", ":| |: ");
    }

    /// <summary>A length in ticks as note values, longest first: plain values on the 32nd grid, triplets otherwise.</summary>
    private static List<string> Durations(int ticks)
    {
        var table = ticks % 24 == 0 ? Plain : ticks % 16 == 0 ? Triplet : Plain.Concat(Triplet).OrderByDescending(d => d.Ticks).ToArray();
        var result = new List<string>();
        while (ticks > 0)
        {
            var fit = table.FirstOrDefault(d => d.Ticks <= ticks && (ticks - d.Ticks) % 8 == 0);
            if (fit.Name is null)
                throw new ImportException("The score has a note length Pipit cannot write (shorter than a triplet 32nd).");
            result.Add(fit.Name);
            ticks -= fit.Ticks;
        }
        return result;
    }

    /// <summary>The written name: the score's own spelling, moved by the octave shift.</summary>
    private static string PitchName(int midi, Note n, int shift, bool flats)
    {
        var octave = n.Octave + shift / 12;
        var acc = n.Alter switch { 1 => "#", -1 => "b", 0 => "", _ => null };
        if (acc is null)
        {
            // Double sharps and flats: respell by sound.
            string[] names = flats ? new[] { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" } : new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "Bb", "B" };
            return names[((midi % 12) + 12) % 12] + (midi / 12 - 1);
        }
        return Steps[n.Step] + acc + octave;
    }

    /// <summary>
    /// The octave move that fits a soprano (written C4-D6), else an alto (F4-G6), else the closest
    /// for a soprano; with the range that the notes left outside are folded into.
    /// </summary>
    private static (int Shift, string Instrument, int Low, int High) FitRecorder(int low, int high)
    {
        foreach (var (lo, hi, name) in new[] { (60, 86, "soprano"), (65, 91, "alto") })
            foreach (var k in new[] { 0, 12, -12, 24, -24 })
                if (low + k >= lo && high + k <= hi)
                    return (k, name, lo, hi);
        var best = new[] { 0, 12, -12, 24, -24 }.MinBy(k => Math.Max(0, 60 - (low + k)) + Math.Max(0, high + k - 86));
        return (best, "soprano", 60, 86);
    }

    private static string KeyName(int fifths, bool minor)
    {
        string[] majors = { "Cb", "Gb", "Db", "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#" };
        string[] minors = { "Abm", "Ebm", "Bbm", "Fm", "Cm", "Gm", "Dm", "Am", "Em", "Bm", "F#m", "C#m", "G#m", "D#m", "A#m" };
        var i = Math.Clamp(fifths, -7, 7) + 7;
        return minor ? minors[i] : majors[i];
    }

    private static string SoundFor(string partName, int staff)
    {
        var n = partName.ToLowerInvariant();
        return n switch
        {
            _ when n.Contains("harpsichord") || n.Contains("cembalo") || n.Contains("clavecin") || n.Contains("cravo") || n.Contains("klavecimbel") => "harpsichord",
            _ when n.Contains("piano") || n.Contains("klavier") || n.Contains("keyboard") => "piano",
            _ when n.Contains("organ") || n.Contains("orgel") || n.Contains("orgue") => "organ",
            _ when n.Contains("violoncell") || n.Contains("cello") => "cello",
            _ when n.Contains("viola") || n.Contains("alto viol") => "viola",
            _ when n.Contains("violin") || n.Contains("viool") || n.Contains("violon") => "violin",
            _ when n.Contains("bass") || n.Contains("basso") || n.Contains("contrebasse") => "contrabass",
            _ when n.Contains("guitar") || n.Contains("gitaar") || n.Contains("guitare") || n.Contains("lute") || n.Contains("luit") => "guitar",
            _ when n.Contains("bassoon") || n.Contains("fagot") || n.Contains("basson") => "bassoon",
            _ when n.Contains("flute") || n.Contains("flûte") || n.Contains("fluit") || n.Contains("flauta") || n.Contains("recorder") => "recorder",
            _ => staff > 1 ? "cello" : "piano",
        };
    }

    private static string Clean(string name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

    private static string Slug(string title)
    {
        var s = new string(title.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
        while (s.Contains("--")) s = s.Replace("--", "-");
        s = s.Trim('-');
        return s.Length == 0 ? "song" : s.Length > 40 ? s[..40].Trim('-') : s;
    }

    private static string Hash(string text)
    {
        var h = System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(text));
        return System.Convert.ToHexString(h)[..6].ToLowerInvariant();
    }

    private static int Ticks(int duration, int divisions) => (int)Math.Round(duration * (double)Tpq / divisions);
    private static string? Text(XElement? e) => e is null ? null : string.IsNullOrWhiteSpace(e.Value) ? null : e.Value.Trim();
    private static int? Int(XElement? e) => e is not null && int.TryParse(e.Value.Trim(), out var v) ? v : null;
}
