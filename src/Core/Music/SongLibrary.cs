namespace Pipit.Core.Music;

/// <summary>
/// All songs: the learning path in chapter order (file order within a chapter, as listed in
/// data/songs/index.json), then the repertoire (chapter 0) from easiest to hardest. Files that
/// fail to parse are reported, not silently skipped.
/// </summary>
public sealed class SongLibrary
{
    public IReadOnlyList<Song> Songs { get; }
    public IReadOnlyList<string> Errors { get; }

    private SongLibrary(IReadOnlyList<Song> songs, IReadOnlyList<string> errors)
    {
        Songs = songs;
        Errors = errors;
    }

    /// <param name="readFile">Reads a file in the songs folder by name.</param>
    /// <param name="own">The player's own songs (imported MusicXML), as (file name, JSON).</param>
    public static SongLibrary Load(Func<string, string> readFile, IEnumerable<(string Name, string Json)>? own = null)
    {
        var index = System.Text.Json.JsonSerializer.Deserialize<List<string>>(readFile("index.json")) ?? new();
        var songs = new List<Song>();
        var errors = new List<string>();
        foreach (var (file, json) in index.Select(f => (f, (string?)null)).Concat((own ?? Array.Empty<(string, string)>()).Select(o => (o.Item1, (string?)o.Item2))))
        {
            try { songs.Add(SongParser.ParseJson(json ?? readFile(file))); }
            catch (Exception e) when (e is SongFormatException or System.Text.Json.JsonException or IOException or KeyNotFoundException)
            {
                errors.Add($"{file}: {e.Message}");
            }
        }
        var order = songs.Select((s, i) => (s, i))
            .OrderBy(p => p.s.Chapter == 0 ? int.MaxValue : p.s.Chapter)
            .ThenBy(p => p.s.Chapter == 0 ? Music.Difficulty.Estimate(p.s).Score : 0)
            .ThenBy(p => p.i).Select(p => p.s).ToList();
        return new SongLibrary(order, errors);
    }

    public Song? Get(string id) => Songs.FirstOrDefault(s => s.Id == id);
}
