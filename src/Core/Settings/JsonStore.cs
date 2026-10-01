using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pipit.Core.Settings;

public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Writes to a temp file and renames, so a crash never leaves half a file behind.</summary>
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }

    public static T LoadOrDefault<T>(string path) where T : new()
    {
        if (!File.Exists(path))
            return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (JsonException)
        {
            // A corrupt file is kept aside rather than silently overwritten.
            File.Copy(path, path + ".corrupt", overwrite: true);
            return new T();
        }
    }
}
