using System.IO.Compression;
using System.Text;
using Pipit.Core.Music;
using Xunit;

namespace Pipit.Core.Tests;

public class MusicXmlImporterTests
{
    private const string Head = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 4.0 Partwise//EN" "http://www.musicxml.org/dtds/partwise.dtd">
        <score-partwise version="4.0">
          <work><work-title>Test Tune</work-title></work>
          <identification><creator type="composer">Anon</creator></identification>
        """;

    private static string N(string step, int octave, int duration, string extra = "", int staff = 1, int alter = 0) =>
        $"<note><pitch><step>{step}</step>{(alter != 0 ? $"<alter>{alter}</alter>" : "")}<octave>{octave}</octave></pitch><duration>{duration}</duration>{extra}<staff>{staff}</staff></note>";

    private static string Attr(string time = "4/4", int fifths = 0, int staves = 1) =>
        $"<attributes><divisions>2</divisions><key><fifths>{fifths}</fifths></key><time><beats>{time.Split('/')[0]}</beats><beat-type>{time.Split('/')[1]}</beat-type></time><staves>{staves}</staves></attributes>";

    private static Song Import(string xml, out ImportResult result)
    {
        result = Assert.Single(MusicXmlImporter.Import(Encoding.UTF8.GetBytes(xml), "test.musicxml"));
        Assert.True(result.Ok, result.Error);
        return SongParser.ParseJson(result.Json!);
    }

    [Fact]
    public void A_melody_with_a_pickup_a_tie_and_a_chord()
    {
        // Pickup quarter D5, then G4 G4 (chord with B4 on top) A4 tied over the bar line, B4 half.
        var xml = Head + """
            <part-list><score-part id="P1"><part-name>Flute</part-name></score-part></part-list>
            <part id="P1">
            """ +
            "<measure number=\"0\" implicit=\"yes\">" + Attr() + "<direction><sound tempo=\"100\"/></direction>" + N("D", 5, 2) + "</measure>" +
            "<measure number=\"1\">" + N("G", 4, 2) + N("G", 4, 2) + N("B", 4, 2, "<chord/>") + N("A", 4, 4, "<tie type=\"start\"/>") + "</measure>" +
            "<measure number=\"2\">" + N("A", 4, 2, "<tie type=\"stop\"/>") + N("B", 4, 4) + "<note><rest/><duration>2</duration></note></measure>" +
            "</part></score-partwise>";
        var song = Import(xml, out var result);
        Assert.Equal("Test Tune", song.Title("en"));
        Assert.Equal("Anon", song.Composer);
        Assert.Equal(100, song.Tempo);
        Assert.Equal(1, song.Pickup);
        Assert.Equal("own", song.Period);
        var notes = song.Melody.Performance;
        // D5 pickup, G4, B4 (top of the chord), A4 (2 + 1 beats tied), B4.
        Assert.Equal(new[] { 74, 67, 71, 69, 71 }, notes.Select(n => n.Pitch.Midi));
        Assert.Equal(3, notes[3].Beats);
        Assert.StartsWith("own-test-tune-", result.Id);
    }

    [Fact]
    public void A_piano_scores_left_hand_is_the_band_and_repeats_are_kept()
    {
        var xml = Head + """
            <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
            <part id="P1">
            """ +
            "<measure number=\"1\">" + Attr("3/4", -1, 2) + "<barline location=\"left\"><repeat direction=\"forward\"/></barline>" +
                N("F", 5, 6) + "<backup><duration>6</duration></backup>" + N("F", 3, 6, staff: 2) + "</measure>" +
            "<measure number=\"2\"><barline location=\"left\"><ending number=\"1\" type=\"start\"/></barline>" +
                N("G", 5, 6) + "<backup><duration>6</duration></backup>" + N("C", 3, 6, staff: 2) +
                "<barline location=\"right\"><ending number=\"1\" type=\"stop\"/><repeat direction=\"backward\"/></barline></measure>" +
            "<measure number=\"3\"><barline location=\"left\"><ending number=\"2\" type=\"start\"/></barline>" +
                N("A", 5, 6) + "<backup><duration>6</duration></backup>" + N("F", 3, 6, staff: 2) + "</measure>" +
            "</part></score-partwise>";
        var song = Import(xml, out _);
        Assert.Equal("F", song.Key);
        // Played: F G (first time), F A (second ending); F5-A5 fits the soprano as written.
        Assert.Equal(new[] { 77, 79, 77, 81 }, song.Melody.Performance.Select(n => n.Pitch.Midi));
        var bass = Assert.Single(song.Accompaniment);
        Assert.Equal("piano", bass.Sound);
        Assert.Equal(4, bass.Performance.Count);
        Assert.Equal(song.Melody.Performance[^1].EndBeat, bass.Performance[^1].EndBeat);
    }

    [Fact]
    public void Triplets_and_compressed_files()
    {
        var triplet = "<time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification>";
        var xml = Head + """
            <part-list><score-part id="P1"><part-name>Recorder</part-name></score-part></part-list>
            <part id="P1">
            """ +
            "<measure number=\"1\">" + Attr("2/4").Replace("<divisions>2</divisions>", "<divisions>6</divisions>") +
                N("C", 5, 2, triplet) + N("D", 5, 2, triplet) + N("E", 5, 2, triplet) + N("F", 5, 6) + "</measure>" +
            "</part></score-partwise>";
        using var zipped = new MemoryStream();
        using (var zip = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("META-INF/container.xml").Open()))
                w.Write("<container><rootfiles><rootfile full-path=\"score.xml\"/></rootfiles></container>");
            using (var w = new StreamWriter(zip.CreateEntry("score.xml").Open()))
                w.Write(xml);
        }
        var result = Assert.Single(MusicXmlImporter.Import(zipped.ToArray(), "test.mxl"));
        Assert.True(result.Ok, result.Error);
        var song = SongParser.ParseJson(result.Json!);
        var beats = song.Melody.Performance.Select(n => n.Beats).ToList();
        Assert.Equal(4, beats.Count);
        Assert.All(beats.Take(3), b => Assert.Equal(1 / 3.0, b, 6));
        Assert.Equal(1, beats[3], 6);
    }

    [Fact]
    public void A_change_of_time_signature_makes_one_song_per_section()
    {
        var xml = Head + """
            <part-list><score-part id="P1"><part-name>Flute</part-name></score-part></part-list>
            <part id="P1">
            """ +
            "<measure number=\"1\">" + Attr("4/4") + N("C", 5, 8) + "</measure>" +
            "<measure number=\"2\">" + N("D", 5, 8) + "</measure>" +
            "<measure number=\"3\">" + Attr("3/4") + N("E", 5, 6) + "</measure>" +
            "</part></score-partwise>";
        var results = MusicXmlImporter.Import(Encoding.UTF8.GetBytes(xml), "x.musicxml");
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Ok, r.Error));
        Assert.Equal(new[] { "Test Tune (1)", "Test Tune (2)" }, results.Select(r => r.Title));
        Assert.Equal("3/4", SongParser.ParseJson(results[1].Json!).Time.ToString());
        Assert.NotEqual(results[0].Id, results[1].Id);
    }

    [Fact]
    public void Notes_out_of_reach_move_by_an_octave_and_garbage_is_refused()
    {
        // C4 to G6 is wider than a soprano: the G6 comes down an octave.
        var xml = Head + """
            <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
            <part id="P1">
            """ +
            "<measure number=\"1\">" + Attr("4/4") + N("C", 4, 4) + N("G", 6, 4) + "</measure>" +
            "</part></score-partwise>";
        var song = Import(xml, out var result);
        Assert.Equal(new[] { 60, 79 }, song.Melody.Performance.Select(n => n.Pitch.Midi));
        Assert.Contains(result.Notes, n => n.Contains("moved by an octave"));
        Assert.False(Assert.Single(MusicXmlImporter.Import(Encoding.UTF8.GetBytes("not xml at all"), "x.xml")).Ok);
    }
}
