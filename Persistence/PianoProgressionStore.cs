using System.Text.Json;

namespace GDMAmpAccessible.Persistence;

internal sealed class PianoProgressionLibrary
{
    public List<PianoProgressionItem> Items { get; set; } = new();

    public void Normalize()
    {
        Items ??= new List<PianoProgressionItem>();
        var unique = new Dictionary<string, PianoProgressionItem>(StringComparer.OrdinalIgnoreCase);
        foreach (PianoProgressionItem item in Items)
        {
            string name = (item.Name ?? string.Empty).Trim();
            string sequence = (item.Sequence ?? string.Empty).Trim();
            if (name.Length == 0 || sequence.Length == 0) continue;
            unique[name] = new PianoProgressionItem { Name = name, Sequence = sequence };
        }
        Items = unique.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

internal sealed class PianoProgressionItem
{
    public string Name { get; set; } = string.Empty;
    public string Sequence { get; set; } = string.Empty;
    public override string ToString() => Name;
}

internal static class PianoProgressionStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static string FilePath
    {
        get
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GDM Amp Accessible");
            return Path.Combine(folder, "progresiones-piano.json");
        }
    }

    public static PianoProgressionLibrary Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new PianoProgressionLibrary();
            PianoProgressionLibrary? result = JsonSerializer.Deserialize<PianoProgressionLibrary>(File.ReadAllText(FilePath), Options);
            result ??= new PianoProgressionLibrary();
            result.Normalize();
            return result;
        }
        catch
        {
            return new PianoProgressionLibrary();
        }
    }

    public static void Save(PianoProgressionLibrary library)
    {
        library.Normalize();
        string? folder = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(library, Options));
        File.Move(temp, FilePath, true);
    }
}
