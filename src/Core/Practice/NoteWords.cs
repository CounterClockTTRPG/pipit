namespace Pipit.Core.Practice;

/// <summary>
/// Spoken note names for the speech recognizer: the words to listen for, and the note letter
/// (0 = C/Do ... 6 = B/Si) each stands for. A recognizer hears names as its own language's
/// words ("far" for Fa, "see" for Si), so common spellings are accepted too. The same word can
/// mean different notes: "si" is Si (B) in solfège but "C" when letters are spoken, so the
/// words depend on the naming. Words are plain ASCII ("re", not "ré"): the recognizer's
/// interface mangles other characters.
/// </summary>
public static class NoteWords
{
    private static readonly Dictionary<string, int> Solfege = new()
    {
        ["do"] = 0, ["doe"] = 0, ["dough"] = 0,
        ["re"] = 1, ["ray"] = 1,
        ["mi"] = 2, ["me"] = 2, ["mee"] = 2,
        ["fa"] = 3, ["far"] = 3,
        ["sol"] = 4, ["so"] = 4, ["soh"] = 4, ["sew"] = 4,
        ["la"] = 5, ["lah"] = 5,
        ["si"] = 6, ["see"] = 6, ["ti"] = 6, ["tea"] = 6,
    };

    private static readonly Dictionary<string, int> Letters = new()
    {
        ["c"] = 0, ["see"] = 0, ["si"] = 0,
        ["d"] = 1, ["dee"] = 1,
        ["e"] = 2,
        ["f"] = 3, ["ef"] = 3,
        ["g"] = 4, ["gee"] = 4,
        ["a"] = 5,
        ["b"] = 6, ["bee"] = 6, ["be"] = 6,
    };

    /// <summary>The words to listen for (letters when the player names notes C D E).</summary>
    public static IReadOnlyCollection<string> Words(bool letters) => (letters ? Letters : Solfege).Keys;

    /// <summary>The note letter a heard word stands for, or null for anything else.</summary>
    public static int? LetterFor(string word, bool letters) =>
        (letters ? Letters : Solfege).TryGetValue(word.Trim().ToLowerInvariant(), out var letter) ? letter : null;

    /// <summary>A grammar for the recognizer: the words plus "[unk]" for anything else.</summary>
    public static string Grammar(bool letters) =>
        "[" + string.Join(",", Words(letters).Select(w => $"\"{w}\"")) + ",\"[unk]\"]";
}
