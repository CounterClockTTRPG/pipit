namespace Pipit.Core.Music;

/// <summary>
/// All songs, in learning order (chapter, then file order within the chapter as listed in
/// data/songs/index.json). Files that fail to parse are reported, not silently skipped.
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
    public static SongLibrary Load(Func<string, string> readFile)
    {
        var index = System.Text.Json.JsonSerializer.Deserialize<List<string>>(readFile("index.json")) ?? new();
        var songs = new List<Song>();
        var errors = new List<string>();
        foreach (var file in index)
        {
            try { songs.Add(SongParser.ParseJson(readFile(file))); }
            catch (Exception e) when (e is SongFormatException or System.Text.Json.JsonException or IOException or KeyNotFoundException)
            {
                errors.Add($"{file}: {e.Message}");
            }
        }
        var order = songs.Select((s, i) => (s, i)).OrderBy(p => p.s.Chapter).ThenBy(p => p.i).Select(p => p.s).ToList();
        return new SongLibrary(order, errors);
    }

    public Song? Get(string id) => Songs.FirstOrDefault(s => s.Id == id);
}
