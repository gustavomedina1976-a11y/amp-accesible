using System.Text.Json;
using System.Text.Json.Serialization;
using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Persistence;

internal static class PresetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "GDM Amp Accessible", "Presets", "presets.json");

    public static UserPresetLibrary Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var lib = JsonSerializer.Deserialize<UserPresetLibrary>(File.ReadAllText(FilePath), Options) ?? new();
                lib.Normalize();
                return lib;
            }
        }
        catch { }
        return new();
    }

    public static void Save(UserPresetLibrary library)
    {
        library.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string tmp = FilePath + ".tmp";
        string bak = FilePath + ".bak";
        File.WriteAllText(tmp, JsonSerializer.Serialize(library, Options));
        if (File.Exists(FilePath)) File.Copy(FilePath, bak, true);
        File.Move(tmp, FilePath, true);
    }
}
