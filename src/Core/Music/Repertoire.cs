using System.Globalization;
using System.Text;

namespace Pipit.Core.Music;

/// <summary>How the Songs screen groups the repertoire.</summary>
public enum RepertoireGrouping { Period, Composer, Difficulty }

/// <summary>The order of songs within each group.</summary>
public enum RepertoireSort { Difficulty, Name }

/// <summary>A heading and its songs. <see cref="Key"/> is a period id, a composer name, "" for an ungrouped list, or <see cref="Repertoire.FavouritesKey"/>.</summary>
public sealed record SongGroup(string Key, IReadOnlyList<Song> Songs);

/// <summary>
/// Finding your way through the repertoire (chapter 0): periods, composer names, text search,
/// and grouping with the player's favourites first.
/// </summary>
public static class Repertoire
{
    public const string FavouritesKey = "favourites";

    /// <summary>Periods in display order: oldest first, then folk and game music.</summary>
    public static readonly IReadOnlyList<string> Periods = new[] { "medieval", "renaissance", "baroque", "classical", "romantic", "traditional", "game" };

    /// <summary>English names of the periods (the app translates them).</summary>
    public static readonly IReadOnlyDictionary<string, string> PeriodNames = new Dictionary<string, string>
    {
        ["medieval"] = "Medieval", ["renaissance"] = "Renaissance", ["baroque"] = "Baroque", ["classical"] = "Classical",
        ["romantic"] = "Romantic", ["traditional"] = "Folk and traditional", ["game"] = "Game music",
    };

    /// <summary>
    /// The composer to group by: "Claude Gervaise, diminutions by David Bellugi" is Gervaise's,
    /// "Traditional (France); words by ..." is Traditional, "Anonymous (14th century)" is Anonymous.
    /// </summary>
    public static string ComposerName(string composer)
    {
        var name = composer.Split("; words by ")[0];
        foreach (var credit in new[] { ", diminutions by ", ", arranged by ", ", set by " })
            name = name.Split(credit)[0];
        foreach (var plain in new[] { "Anonymous", "Traditional" })
            if (name.StartsWith(plain)) return plain;
        return name.Trim();
    }

    /// <summary>Composers sort by surname ("Jacob van Eyck" under E); Anonymous and Traditional come last.</summary>
    public static string ComposerSortKey(string name) =>
        name is "Anonymous" or "Traditional" ? "￿" + name : name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();

    /// <summary>
    /// Whether every word of the query appears in the song's titles (any language), composer
    /// or period, ignoring case and accents ("frere" finds "Frère Jacques").
    /// </summary>
    public static bool Matches(Song song, string query)
    {
        var words = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;
        var text = Fold(string.Join(" ", song.Titles.Values.Append(song.Composer).Append(PeriodNames.GetValueOrDefault(song.Period, song.Period))));
        return words.All(text.Contains);
    }

    /// <summary>
    /// The songs (already easiest first) filtered by period, composer and query, sorted, then
    /// grouped; favourites come first in a group of their own and are not repeated below.
    /// <paramref name="language"/> picks the titles that <see cref="RepertoireSort.Name"/> sorts by.
    /// </summary>
    public static IReadOnlyList<SongGroup> Arrange(IEnumerable<Song> songs, RepertoireGrouping by, IReadOnlySet<string> favourites,
        string? period = null, string? composer = null, string query = "", RepertoireSort sort = RepertoireSort.Difficulty, string language = "en")
    {
        var shown = songs.Where(s => (period is null || s.Period == period) && (composer is null || ComposerName(s.Composer) == composer) && Matches(s, query)).ToList();
        if (sort == RepertoireSort.Name)
            shown = shown.OrderBy(s => Fold(s.Title(language)).Trim(), StringComparer.Ordinal).ToList();
        var groups = new List<SongGroup>();
        var favourite = shown.Where(s => favourites.Contains(s.Id)).ToList();
        if (favourite.Count > 0)
            groups.Add(new SongGroup(FavouritesKey, favourite));
        var rest = shown.Where(s => !favourites.Contains(s.Id)).ToList();
        switch (by)
        {
            case RepertoireGrouping.Period:
                groups.AddRange(rest.GroupBy(s => s.Period)
                    .OrderBy(g => Periods.IndexOf(g.Key) is var i and >= 0 ? i : int.MaxValue)
                    .Select(g => new SongGroup(g.Key, g.ToList())));
                break;
            case RepertoireGrouping.Composer:
                groups.AddRange(rest.GroupBy(s => ComposerName(s.Composer))
                    .OrderBy(g => ComposerSortKey(g.Key), StringComparer.Ordinal).ThenBy(g => g.Key)
                    .Select(g => new SongGroup(g.Key, g.ToList())));
                break;
            default:
                if (rest.Count > 0) groups.Add(new SongGroup("", rest));
                break;
        }
        return groups;
    }

    private static int IndexOf(this IReadOnlyList<string> list, string item)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == item) return i;
        return -1;
    }

    /// <summary>Lower case without accents, punctuation as spaces.</summary>
    private static string Fold(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return builder.ToString();
    }
}
