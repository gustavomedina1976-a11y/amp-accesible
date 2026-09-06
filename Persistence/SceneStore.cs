using System.Text.Json;
using System.Text.Json.Serialization;
using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Persistence;

internal static class SceneStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string FilePath
    {
        get
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GDM Amp Accessible");
            return Path.Combine(folder, "escenas.json");
        }
    }

    public static SceneLibrary Load()
    {
        SceneLibrary? library = TryLoadFile(FilePath);
        library ??= TryLoadFile(FilePath + ".bak");
        library ??= new SceneLibrary();
        library.Normalize();
        return library;
    }

    public static void Save(SceneLibrary library)
    {
        library.Normalize();
        string path = FilePath;
        string backupPath = path + ".bak";
        string temporaryPath = path + ".tmp";
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        string json = JsonSerializer.Serialize(library, Options);
        File.WriteAllText(temporaryPath, json);

        if (File.Exists(path))
        {
            File.Copy(path, backupPath, true);
        }

        File.Move(temporaryPath, path, true);
    }

    private static SceneLibrary? TryLoadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            SceneLibrary? library = JsonSerializer.Deserialize<SceneLibrary>(json, Options);
            library?.Normalize();
            return library;
        }
        catch
        {
            return null;
        }
    }
}
