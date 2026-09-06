using System.Text.Json;

namespace GDMAmpAccessible.Persistence;

internal sealed class NamLibrary
{
    public List<NamLibraryItem> Items { get; set; } = new();
}

internal sealed class NamLibraryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Captura NAM";
    public string Path { get; set; } = string.Empty;
    public string OriginalSourcePath { get; set; } = string.Empty;
    public bool IncludesCabinet { get; set; }
    public float InputTrimDb { get; set; }
    public float OutputTrimDb { get; set; }
    public bool AutoLevelEnabled { get; set; }
    public float AutoLevelDb { get; set; }
    public float LastCalibrationRmsDb { get; set; } = -120f;
    public string Category { get; set; } = string.Empty;
    public bool Favorite { get; set; }

    public override string ToString()
    {
        string favorite = Favorite ? "Favorito, " : string.Empty;
        string category = string.IsNullOrWhiteSpace(Category) ? "Sin categoría" : Category;
        return $"{favorite}{Name}, {category}";
    }
}

internal static class NamLibraryStore
{
    private static string SettingsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GDM Amp Accessible");

    public static string ManagedModelsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "GDM Amp Accessible",
        "Capturas NAM",
        "Biblioteca");

    private static string FilePath => Path.Combine(SettingsFolder, "nam-bank.json");

    public static NamLibrary Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                NamLibrary? library = JsonSerializer.Deserialize<NamLibrary>(File.ReadAllText(FilePath));
                if (library is not null)
                {
                    Normalize(library);
                    return library;
                }
            }
        }
        catch { }

        return new NamLibrary();
    }

    public static void Save(NamLibrary library)
    {
        Normalize(library);
        Directory.CreateDirectory(SettingsFolder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(library, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    public static NamLibraryItem Import(
        NamLibrary library,
        string sourcePath,
        bool includesCabinet,
        float inputTrimDb,
        float outputTrimDb)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("No se encontró el archivo NAM a incorporar al banco.", sourcePath);
        }

        string fullSource = Path.GetFullPath(sourcePath);
        NamLibraryItem? existing = library.Items.FirstOrDefault(item =>
            PathEquals(item.Path, fullSource) ||
            (!string.IsNullOrWhiteSpace(item.OriginalSourcePath) && PathEquals(item.OriginalSourcePath, fullSource)));

        if (existing is not null && File.Exists(existing.Path))
        {
            existing.IncludesCabinet = includesCabinet;
            existing.InputTrimDb = ClampTrim(inputTrimDb);
            existing.OutputTrimDb = ClampTrim(outputTrimDb);
            Save(library);
            return existing;
        }

        Directory.CreateDirectory(ManagedModelsFolder);
        string safeName = SanitizeFileName(Path.GetFileName(fullSource));
        string destination = GetUniquePath(ManagedModelsFolder, safeName);
        File.Copy(fullSource, destination, overwrite: false);

        var item = new NamLibraryItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = MakeUniqueDisplayName(library, Path.GetFileNameWithoutExtension(safeName)),
            Path = destination,
            OriginalSourcePath = fullSource,
            IncludesCabinet = includesCabinet,
            InputTrimDb = ClampTrim(inputTrimDb),
            OutputTrimDb = ClampTrim(outputTrimDb)
        };
        library.Items.Add(item);
        Save(library);
        return item;
    }

    public static void Remove(NamLibrary library, NamLibraryItem item, bool deleteManagedFile)
    {
        library.Items.RemoveAll(candidate => string.Equals(candidate.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (deleteManagedFile && IsManagedPath(item.Path))
        {
            try
            {
                if (File.Exists(item.Path)) File.Delete(item.Path);
            }
            catch { }
        }
        Save(library);
    }

    public static NamLibraryItem? FindByPath(NamLibrary library, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch { return null; }
        return library.Items.FirstOrDefault(item => PathEquals(item.Path, fullPath));
    }

    private static void Normalize(NamLibrary library)
    {
        library.Items ??= new List<NamLibraryItem>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<NamLibraryItem>();
        foreach (NamLibraryItem? item in library.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path)) continue;
            if (string.IsNullOrWhiteSpace(item.Id) || seenIds.Contains(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
            }
            seenIds.Add(item.Id);
            item.Name = string.IsNullOrWhiteSpace(item.Name)
                ? Path.GetFileNameWithoutExtension(item.Path)
                : item.Name.Trim();
            item.OriginalSourcePath ??= string.Empty;
            item.Category = NormalizeCategory(item.Category);
            item.InputTrimDb = ClampTrim(item.InputTrimDb);
            item.OutputTrimDb = ClampTrim(item.OutputTrimDb);
            item.AutoLevelDb = Math.Clamp(float.IsFinite(item.AutoLevelDb) ? item.AutoLevelDb : 0f, -12f, 12f);
            item.LastCalibrationRmsDb = float.IsFinite(item.LastCalibrationRmsDb) ? Math.Clamp(item.LastCalibrationRmsDb, -120f, 0f) : -120f;
            normalized.Add(item);
        }
        library.Items = normalized;
    }


    public static string NormalizeCategory(string? category)
    {
        string value = string.IsNullOrWhiteSpace(category) ? string.Empty : category.Trim();
        if (value.Length > 40) value = value[..40];
        return value;
    }

    private static bool PathEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsManagedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            string managed = Path.GetFullPath(ManagedModelsFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(path);
            return candidate.StartsWith(managed, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? "captura.nam" : name;
    }

    private static string GetUniquePath(string directory, string fileName)
    {
        string first = Path.Combine(directory, fileName);
        if (!File.Exists(first)) return first;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        for (int index = 2; index <= 999; index++)
        {
            string candidate = Path.Combine(directory, $"{stem}_{index}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(directory, $"{stem}_{Guid.NewGuid():N}{ext}");
    }

    private static string MakeUniqueDisplayName(NamLibrary library, string baseName)
    {
        string clean = string.IsNullOrWhiteSpace(baseName) ? "Captura NAM" : baseName.Trim();
        if (!library.Items.Any(item => string.Equals(item.Name, clean, StringComparison.OrdinalIgnoreCase))) return clean;
        for (int index = 2; index <= 999; index++)
        {
            string candidate = $"{clean} {index}";
            if (!library.Items.Any(item => string.Equals(item.Name, candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
        return $"{clean} {DateTime.Now:HHmmss}";
    }

    private static float ClampTrim(float value)
    {
        if (!float.IsFinite(value)) return 0f;
        return Math.Clamp(value, -24f, 24f);
    }
}
