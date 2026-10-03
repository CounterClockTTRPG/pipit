using Pipit.Core.Music;
using Xunit;

namespace Pipit.Core.Tests;

public class RepertoireTests
{
    private static SongLibrary Library => SongLibrary.Load(name => File.ReadAllText(Repo.Data("songs", name)));
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static IEnumerable<Song> RepertoireSongs => Library.Songs.Where(s => s.Chapter == 0);

    [Fact]
    public void Every_song_has_a_known_period()
    {
        foreach (var song in Library.Songs)
            Assert.True(Repertoire.Periods.Contains(song.Period), $"{song.Id}: period '{song.Period}'");
    }

    [Theory]
    [InlineData("Claude Gervaise, diminutions by David Bellugi", "Claude Gervaise")]
    [InlineData("George Frideric Handel, arranged by David Bellugi", "George Frideric Handel")]
    [InlineData("Anonymous (14th century), set by David Bellugi", "Anonymous")]
    [InlineData("Traditional (Scotland); words by Robert Burns", "Traditional")]
    [InlineData("Jacob van Eyck", "Jacob van Eyck")]
    public void Composer_names_drop_arrangers_and_origins(string composer, string name) =>
        Assert.Equal(name, Repertoire.ComposerName(composer));

    [Fact]
    public void Search_ignores_case_and_accents_and_looks_at_every_title()
    {
        var song = Library.Get("frere-jacques")!;
        Assert.True(Repertoire.Matches(song, "FRERE"));
        Assert.True(Repertoire.Matches(song, "vader jacob"));     // the Dutch title
        Assert.True(Repertoire.Matches(song, "traditional"));
        Assert.False(Repertoire.Matches(song, "jacob handel"));   // every word must match
        Assert.True(Repertoire.Matches(Library.Get("ultima5-stones")!, "game ultima"));
    }

    [Fact]
    public void Grouping_by_period_follows_history()
    {
        var keys = Repertoire.Arrange(RepertoireSongs, RepertoireGrouping.Period, None).Select(g => g.Key).ToList();
        Assert.Equal(Repertoire.Periods.Where(keys.Contains), keys);
        Assert.Equal(RepertoireSongs.Count(), Repertoire.Arrange(RepertoireSongs, RepertoireGrouping.Period, None).Sum(g => g.Songs.Count));
    }

    [Fact]
    public void Composers_sort_by_surname_with_anonymous_last()
    {
        var keys = Repertoire.Arrange(RepertoireSongs, RepertoireGrouping.Composer, None).Select(g => g.Key).ToList();
        Assert.True(keys.IndexOf("Johann Sebastian Bach") < keys.IndexOf("Jacob van Eyck"));
        Assert.True(keys.IndexOf("Jacob van Eyck") < keys.IndexOf("George Frideric Handel"));
        Assert.Equal("Anonymous", keys.Last());
    }

    [Fact]
    public void Favourites_come_first_and_are_not_repeated()
    {
        var favourites = new HashSet<string> { "ultima5-stones", "la-folia-theme" };
        var groups = Repertoire.Arrange(RepertoireSongs, RepertoireGrouping.Period, favourites);
        Assert.Equal(Repertoire.FavouritesKey, groups[0].Key);
        Assert.Equal(favourites, groups[0].Songs.Select(s => s.Id).ToHashSet());
        Assert.DoesNotContain(groups.Skip(1).SelectMany(g => g.Songs), s => favourites.Contains(s.Id));
    }

    [Fact]
    public void Filters_combine()
    {
        var groups = Repertoire.Arrange(RepertoireSongs, RepertoireGrouping.Difficulty, None, period: "baroque", composer: "Arcangelo Corelli", query: "theme");
        Assert.Equal("la-folia-theme", Assert.Single(Assert.Single(groups).Songs).Id);
        Assert.Empty(Repertoire.Arrange(RepertoireSongs, RepertoireGrouping.Difficulty, None, query: "no such song"));
    }

    [Fact]
    public void Sorting_by_name_ignores_accents_and_uses_the_language()
    {
        static string Fold(string t) => new string(t.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray()).Trim();
        var titles = Repertoire.Arrange(RepertoireSongs, RepertoireGrouping.Difficulty, None, sort: RepertoireSort.Name, language: "nl")
            .Single().Songs.Select(s => Fold(s.Title("nl"))).ToList();
        Assert.All(titles.Zip(titles.Skip(1)), pair => Assert.True(string.CompareOrdinal(pair.First, pair.Second) <= 0, $"{pair.First} before {pair.Second}"));
        Assert.StartsWith("a", titles[0]);
    }
}
