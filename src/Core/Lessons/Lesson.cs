using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pipit.Core.Lessons;

public abstract record LessonBlock;

public sealed record HeadingBlock(int Level, string Text) : LessonBlock;

/// <summary>A paragraph with inline **bold** and *italic*.</summary>
public sealed record ParagraphBlock(string Text) : LessonBlock;

public sealed record ListBlock(IReadOnlyList<string> Items) : LessonBlock;

/// <summary>
/// An interactive or illustrated block: <c>::name "positional text" key=value key="quoted value"</c>.
/// Known names: fingering, staff, play, try, exercise, quiz, pip, image.
/// </summary>
public sealed record DirectiveBlock(string Name, string? Text, IReadOnlyDictionary<string, string> Args) : LessonBlock
{
    public string Arg(string key, string fallback = "") => Args.TryGetValue(key, out var v) ? v : fallback;
    public double Number(string key, double fallback) =>
        Args.TryGetValue(key, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;

    /// <summary>Blocks the player must complete before the lesson counts as done.</summary>
    public bool IsTask => Name is "try" or "exercise" or "quiz";
}

public sealed class Lesson
{
    public required string Id { get; init; }
    public required IReadOnlyDictionary<string, string> Titles { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<LessonBlock>> Pages { get; init; }

    public string Title(string language) => Titles.TryGetValue(language, out var t) ? t : Titles.GetValueOrDefault("en", Id);

    /// <summary>The page in a language, falling back to English (French lessons are not written yet).</summary>
    public IReadOnlyList<LessonBlock> Page(string language) => Pages.TryGetValue(language, out var p) ? p : Pages["en"];
}

/// <summary>Reads lessons: lesson.json (id, titles) plus one markdown-like page per language.</summary>
public static partial class LessonParser
{
    public static Lesson Load(string id, Func<string, string?> readFile)
    {
        var meta = JsonDocument.Parse(readFile("lesson.json") ?? throw new InvalidDataException($"{id}: no lesson.json")).RootElement;
        var titles = meta.GetProperty("title").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        var pages = new Dictionary<string, IReadOnlyList<LessonBlock>>();
        foreach (var language in new[] { "en", "nl", "fr" })
            if (readFile(language + ".md") is { } text)
                pages[language] = Parse(text, $"{id}/{language}");
        if (!pages.ContainsKey("en"))
            throw new InvalidDataException($"{id}: no en.md");
        return new Lesson { Id = id, Titles = titles, Pages = pages };
    }

    public static List<LessonBlock> Parse(string text, string context = "lesson")
    {
        var blocks = new List<LessonBlock>();
        var paragraph = new List<string>();
        var list = new List<string>();
        void FlushParagraph()
        {
            if (paragraph.Count > 0)
                blocks.Add(new ParagraphBlock(string.Join(" ", paragraph)));
            paragraph.Clear();
        }
        void FlushList()
        {
            if (list.Count > 0)
                blocks.Add(new ListBlock(list.ToList()));
            list.Clear();
        }

        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                FlushParagraph();
                FlushList();
                continue;
            }
            if (line.StartsWith("::"))
            {
                FlushParagraph();
                FlushList();
                blocks.Add(ParseDirective(line[2..], context));
            }
            else if (line.StartsWith('#'))
            {
                FlushParagraph();
                FlushList();
                var level = line.TakeWhile(c => c == '#').Count();
                blocks.Add(new HeadingBlock(level, line[level..].Trim()));
            }
            else if (line.StartsWith("- "))
            {
                FlushParagraph();
                list.Add(line[2..].Trim());
            }
            else
            {
                FlushList();
                paragraph.Add(line);
            }
        }
        FlushParagraph();
        FlushList();
        return blocks;
    }

    private static DirectiveBlock ParseDirective(string body, string context)
    {
        var m = DirectiveRegex().Match(body);
        if (!m.Success)
            throw new InvalidDataException($"{context}: cannot read directive '::{body}'");
        var name = m.Groups["name"].Value;
        string? positional = m.Groups["text"].Success ? m.Groups["text"].Value : null;
        var args = new Dictionary<string, string>();
        foreach (Match a in ArgRegex().Matches(m.Groups["rest"].Value))
            args[a.Groups["key"].Value] = a.Groups["q"].Success ? a.Groups["q"].Value : a.Groups["v"].Value;
        return new DirectiveBlock(name, positional, args);
    }

    [GeneratedRegex("""^(?<name>[a-z]+)\s*(?:"(?<text>[^"]*)")?(?<rest>.*)$""")]
    private static partial Regex DirectiveRegex();

    [GeneratedRegex("""(?<key>[a-z]+)=(?:"(?<q>[^"]*)"|(?<v>\S+))""")]
    private static partial Regex ArgRegex();

    /// <summary>A quiz's options from "Right* | Wrong | Wrong": the starred one is correct.</summary>
    public static (IReadOnlyList<string> Options, int Correct) QuizOptions(DirectiveBlock quiz)
    {
        var options = quiz.Arg("options").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var correct = options.FindIndex(o => o.EndsWith('*'));
        if (correct < 0)
            throw new InvalidDataException($"Quiz \"{quiz.Text}\" marks no correct answer with *");
        options[correct] = options[correct].TrimEnd('*').Trim();
        return (options, correct);
    }
}
