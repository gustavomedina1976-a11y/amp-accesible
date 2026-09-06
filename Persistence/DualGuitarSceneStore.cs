using System.Text.Json;
using System.Text.Json.Serialization;
using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Persistence;

internal static class DualGuitarSceneStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "GDM Amp Accessible", "Presets", "dual-guitar-scenes.json");

    public static DualGuitarSceneLibrary Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var library = JsonSerializer.Deserialize<DualGuitarSceneLibrary>(File.ReadAllText(FilePath), Options) ?? new();
                library.Normalize();
                return library;
            }
        }
        catch { }
        return new();
    }

    public static void Save(DualGuitarSceneLibrary library)
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
