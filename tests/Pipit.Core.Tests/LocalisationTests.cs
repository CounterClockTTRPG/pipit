using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Pipit.Core.Tests;

/// <summary>
/// Every interface text has a Dutch translation (the same rules as tools/extract-strings.py,
/// which adds new texts to every data/i18n/*.json for translating).
/// </summary>
public partial class LocalisationTests
{
    private static readonly Regex Literal = new("\"((?:[^\"\\\\]|\\\\.)*)\"");

    private static string Unescape(string s) => Regex.Unescape(s);

    private static IEnumerable<string> Texts()
    {
        var found = new HashSet<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(Repo.Root, "src", "App"), "*.cs", SearchOption.AllDirectories))
        {
            var src = File.ReadAllText(file);
            foreach (Match m in Call().Matches(src))
                found.Add(string.Concat(Literal.Matches(m.Groups[1].Value).Select(l => Unescape(l.Groups[1].Value))));
            foreach (Match m in Options().Matches(src))
                foreach (Match l in Literal.Matches(m.Groups[1].Value))
                    found.Add(Unescape(l.Groups[1].Value));
            foreach (var rx in new[] { Helper(), Conditional() })
                foreach (Match m in rx.Matches(src))
                    foreach (Match l in Literal.Matches(m.Groups[1].Value))
                        found.Add(Unescape(l.Groups[1].Value));
            foreach (var rx in new[] { Title(), Tile(), Table(), Page(), Tuple() })
                foreach (Match m in rx.Matches(src))
                    for (var g = 1; g < m.Groups.Count; g++)
                        found.Add(Unescape(m.Groups[g].Value));
        }
        var achievements = File.ReadAllText(Path.Combine(Repo.Root, "src", "Core", "Game", "Achievements.cs"));
        foreach (Match m in Achievement().Matches(achievements))
        {
            found.Add(Unescape(m.Groups[1].Value));
            found.Add(Unescape(m.Groups[2].Value));
        }
        // Fragments of interpolated strings ("{entry.Score}") are not texts; "{0}" placeholders are.
        return found.Where(t => t.Any(char.IsLetter) && !t.StartsWith("res://") && !Regex.IsMatch(t, @"\{(?!\d)") && !Regex.IsMatch(t, @"\b[a-z]\w*\.[A-Z]\w*"));
    }

    private static Dictionary<string, string> Table(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Repo.Data("i18n", language + ".json")))!;

    [Theory]
    [InlineData("nl")]
    [InlineData("fr")]
    public void Every_interface_text_is_translated(string language)
    {
        var table = Table(language);
        var missing = Texts().Where(t => !table.TryGetValue(t, out var translated) || translated == "").OrderBy(t => t).ToList();
        Assert.True(missing.Count == 0, $"Not translated into {language} (run tools/extract-strings.py --update):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void The_extractor_finds_every_kind_of_text()
    {
        // One example of each rule, so a broken pattern cannot pass silently.
        var texts = Texts().ToHashSet();
        foreach (var example in new[] { "Who's playing?", "Players", "Songs", "Play along, or at your own pace", "Your first notes: Si, La, Sol", "Study", "Survivor", "Reach level 5.", "Score", "3 · Tuning", "Create player", "Low octave" })
            Assert.Contains(example, texts);
        Assert.True(texts.Count > 250, $"only {texts.Count} texts found");
    }

    [Theory]
    [InlineData("nl")]
    [InlineData("fr")]
    public void Translations_keep_their_placeholders(string language)
    {
        static string Placeholders(string s) => string.Join(",", Regex.Matches(s, @"\{\d+[^}]*\}").Select(m => m.Value).OrderBy(x => x));
        var broken = Table(language).Where(kv => kv.Value != "" && Placeholders(kv.Key) != Placeholders(kv.Value)).Select(kv => kv.Key).ToList();
        Assert.Empty(broken);
    }

    [GeneratedRegex("""Ui\.(?:T|Label|Text|Button)\(\s*((?:"(?:[^"\\]|\\.)*"\s*\+\s*)*"(?:[^"\\]|\\.)*")""")]
    private static partial Regex Call();
    [GeneratedRegex("""Ui\.Options\(\s*new\[\]\s*\{([^}]*)\}""")]
    private static partial Regex Options();
    [GeneratedRegex(@"override string Title => ""((?:[^""\\]|\\.)*)""")]
    private static partial Regex Title();
    [GeneratedRegex(@"Tile\(\s*""((?:[^""\\]|\\.)*)"",\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex Tile();
    [GeneratedRegex(@"\[\d+\]\s*=\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex Table();
    [GeneratedRegex(@"new\(PipMood\.\w+,\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex Page();
    [GeneratedRegex(@"\bSet\(\s*((?:""(?:[^""\\]|\\.)*""\s*,?\s*)+)")]
    private static partial Regex Helper();
    [GeneratedRegex(@"Ui\.(?:T|Button|Label|Text)\(([^;\n]*\?[^;\n]*:[^;\n]*)\)")]
    private static partial Regex Conditional();
    [GeneratedRegex(@"\(\s*""((?:[^""\\]|\\.)*)"",\s*new\[\]")]
    private static partial Regex Tuple();
    [GeneratedRegex(@"new Achievement\(""[^""]+"", ""((?:[^""\\]|\\.)*)"", ""((?:[^""\\]|\\.)*)""")]
    private static partial Regex Achievement();
}
