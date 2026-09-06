using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Persistence;

internal sealed class BackupRestoreResult
{
    public SceneLibrary Scenes { get; init; } = new();
    public UserPresetLibrary Presets { get; init; } = new();
    public AudioPreferences Audio { get; init; } = new();
    public NamLibrary NamLibrary { get; init; } = new();
    public MidiSettings Midi { get; init; } = new();
    public DualGuitarBankLibrary DualGuitarBanks { get; init; } = new();
    public DualGuitarSceneLibrary DualGuitarScenes { get; init; } = new();
    public int RestoredNamCount { get; init; }
    public int RestoredIrCount { get; init; }
    public int MissingAssetCount { get; init; }
}

internal static class BackupService
{
    private const int CurrentFormatVersion = 5;
    private const string ManifestEntryName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Export(
        string destinationPath,
        SceneLibrary scenes,
        UserPresetLibrary presets,
        DualGuitarBankLibrary dualGuitarBanks,
        DualGuitarSceneLibrary dualGuitarScenes,
        AudioPreferences audio,
        NamLibrary namLibrary,
        MidiSettings midi,
        string appVersion)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("La ruta de la copia de seguridad está vacía.", nameof(destinationPath));

        scenes.Normalize();
        presets.Normalize();
        dualGuitarBanks.Normalize();
        dualGuitarScenes.Normalize();
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        string temporaryPath = destinationPath + ".tmp";
        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);

        var manifest = new BackupManifest
        {
            FormatVersion = CurrentFormatVersion,
            AppVersion = appVersion,
            CreatedUtc = DateTime.UtcNow,
            Scenes = scenes,
            Presets = presets,
            DualGuitarBanks = dualGuitarBanks,
            DualGuitarScenes = dualGuitarScenes,
            Audio = audio,
            Midi = midi
        };

        using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
        {
            int namIndex = 0;
            var seenNam = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NamLibraryItem item in namLibrary.Items)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path)) continue;
                string fullPath;
                try { fullPath = Path.GetFullPath(item.Path); }
                catch { continue; }
                if (!seenNam.Add(fullPath)) continue;
                string entryName = $"nam/{namIndex++:D4}_{SanitizeFileName(Path.GetFileName(fullPath))}";
                AddFile(archive, fullPath, entryName);
                NamLibraryItem metadata = CloneNamItem(item);
                metadata.Path = fullPath;
                manifest.NamItems.Add(new BackupNamItem { Metadata = metadata, EntryName = entryName });
            }
            foreach (string namPath in EnumerateNamPaths(presets, dualGuitarBanks, dualGuitarScenes, audio))
            {
                string fullPath;
                try { fullPath = Path.GetFullPath(namPath); }
                catch { continue; }
                if (!File.Exists(fullPath) || !seenNam.Add(fullPath)) continue;
                string entryName = $"nam/{namIndex++:D4}_{SanitizeFileName(Path.GetFileName(fullPath))}";
                AddFile(archive, fullPath, entryName);
                manifest.NamItems.Add(new BackupNamItem
                {
                    Metadata = new NamLibraryItem
                    {
                        Name = Path.GetFileNameWithoutExtension(fullPath),
                        Path = fullPath,
                        OriginalSourcePath = fullPath
                    },
                    EntryName = entryName
                });
            }

            var seenIr = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int irIndex = 0;
            foreach (string irPath in EnumerateIrPaths(scenes, presets, dualGuitarBanks, dualGuitarScenes, audio))
            {
                string fullPath;
                try { fullPath = Path.GetFullPath(irPath); }
                catch { continue; }
                if (!seenIr.Add(fullPath) || !File.Exists(fullPath)) continue;

                string entryName = $"ir/{irIndex++:D4}_{SanitizeFileName(Path.GetFileName(fullPath))}";
                AddFile(archive, fullPath, entryName);
                manifest.IrAssets.Add(new BackupAsset { OriginalPath = fullPath, EntryName = entryName });
            }

            ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open()))
                writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));

            ZipArchiveEntry readme = archive.CreateEntry("LEEME.txt", CompressionLevel.Fastest);
            using (var writer = new StreamWriter(readme.Open()))
            {
                writer.WriteLine("Copia de seguridad de Amp Accessible.");
                writer.WriteLine("Contiene escenas, presets de usuario, bancos independientes, escenas completas de dos guitarras, configuración, asignaciones MIDI, biblioteca NAM y los IR externos disponibles al crear la copia.");
                writer.WriteLine("Restaure este archivo desde Amp Accessible; no es necesario descomprimirlo manualmente.");
            }
        }

        File.Move(temporaryPath, destinationPath, overwrite: true);
    }

    public static BackupRestoreResult Restore(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
            throw new FileNotFoundException("No se encontró la copia de seguridad seleccionada.", backupPath);

        using var archive = ZipFile.OpenRead(backupPath);
        ZipArchiveEntry? manifestEntry = archive.GetEntry(ManifestEntryName);
        if (manifestEntry is null) throw new InvalidDataException("La copia no contiene manifest.json.");

        BackupManifest? manifest;
        using (var reader = new StreamReader(manifestEntry.Open()))
            manifest = JsonSerializer.Deserialize<BackupManifest>(reader.ReadToEnd(), JsonOptions);

        if (manifest is null) throw new InvalidDataException("No se pudo leer el contenido de la copia.");
        if (manifest.FormatVersion <= 0 || manifest.FormatVersion > CurrentFormatVersion)
            throw new InvalidDataException($"Formato de copia no compatible: {manifest.FormatVersion}.");

        manifest.Scenes ??= new SceneLibrary();
        manifest.Presets ??= new UserPresetLibrary();
        manifest.Audio ??= new AudioPreferences();
        manifest.Midi ??= new MidiSettings();
        manifest.DualGuitarBanks ??= new DualGuitarBankLibrary();
        manifest.DualGuitarScenes ??= new DualGuitarSceneLibrary();
        manifest.NamItems ??= new List<BackupNamItem>();
        manifest.IrAssets ??= new List<BackupAsset>();

        var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int missingAssets = 0;
        int restoredNam = 0;
        int restoredIr = 0;
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string restoredNamFolder = Path.Combine(NamLibraryStore.ManagedModelsFolder, $"Restaurados_{stamp}");
        Directory.CreateDirectory(restoredNamFolder);
        var restoredLibrary = new NamLibrary();

        foreach (BackupNamItem backupItem in manifest.NamItems)
        {
            if (backupItem?.Metadata is null || string.IsNullOrWhiteSpace(backupItem.EntryName)) continue;
            ZipArchiveEntry? entry = archive.GetEntry(backupItem.EntryName);
            if (entry is null)
            {
                missingAssets++;
                continue;
            }

            string fileName = SanitizeFileName(Path.GetFileName(backupItem.Metadata.Path));
            if (!string.Equals(Path.GetExtension(fileName), ".nam", StringComparison.OrdinalIgnoreCase))
                fileName = string.IsNullOrWhiteSpace(fileName) ? "captura.nam" : fileName + ".nam";
            string destination = GetUniquePath(restoredNamFolder, fileName);
            ExtractEntry(entry, destination);

            if (!string.IsNullOrWhiteSpace(backupItem.Metadata.Path))
                pathMap[NormalizeMapKey(backupItem.Metadata.Path)] = destination;

            NamLibraryItem restoredItem = CloneNamItem(backupItem.Metadata);
            restoredItem.Path = destination;
            restoredLibrary.Items.Add(restoredItem);
            restoredNam++;
        }

        string restoredIrFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GDM Amp Accessible", "IR", $"Restaurados_{stamp}");

        foreach (BackupAsset asset in manifest.IrAssets)
        {
            if (asset is null || string.IsNullOrWhiteSpace(asset.OriginalPath) || string.IsNullOrWhiteSpace(asset.EntryName)) continue;
            ZipArchiveEntry? entry = archive.GetEntry(asset.EntryName);
            if (entry is null)
            {
                missingAssets++;
                continue;
            }

            Directory.CreateDirectory(restoredIrFolder);
            string fileName = SanitizeFileName(Path.GetFileName(asset.OriginalPath));
            string destination = GetUniquePath(restoredIrFolder, fileName);
            ExtractEntry(entry, destination);
            pathMap[NormalizeMapKey(asset.OriginalPath)] = destination;
            restoredIr++;
        }

        manifest.Scenes.Normalize();
        manifest.Presets.Normalize();
        manifest.DualGuitarBanks.Normalize();
        manifest.DualGuitarScenes.Normalize();

        manifest.Scenes.Scenes = manifest.Scenes.Scenes
            .Select(scene => RemapSceneAssets(scene, pathMap))
            .ToList();
        manifest.Scenes.Normalize();

        manifest.Presets.Presets = manifest.Presets.Presets.Select(preset =>
        {
            string? remappedNam = RemapExistingPath(preset.NamPath, pathMap);
            bool hasNam = !string.IsNullOrWhiteSpace(remappedNam) && File.Exists(remappedNam);
            return preset with
            {
                Sound = RemapSceneAssets(preset.Sound, pathMap),
                NamPath = remappedNam,
                NamEnabled = preset.NamEnabled && hasNam
            };
        }).ToList();
        manifest.Presets.Normalize();

        manifest.DualGuitarBanks.Guitar1Banks = manifest.DualGuitarBanks.Guitar1Banks
            .Select(bank =>
            {
                string? remappedNam = RemapExistingPath(bank.NamPath, pathMap);
                bool hasNam = !string.IsNullOrWhiteSpace(remappedNam) && File.Exists(remappedNam);
                return bank with
                {
                    Sound = RemapSceneAssets(bank.Sound, pathMap),
                    NamPath = remappedNam,
                    NamEnabled = bank.NamEnabled && hasNam
                };
            })
            .ToList();
        manifest.DualGuitarBanks.Guitar2Banks = manifest.DualGuitarBanks.Guitar2Banks
            .Select(bank =>
            {
                string? remappedNam = RemapExistingPath(bank.NamPath, pathMap);
                bool hasNam = !string.IsNullOrWhiteSpace(remappedNam) && File.Exists(remappedNam);
                return bank with
                {
                    Sound = RemapSceneAssets(bank.Sound, pathMap),
                    NamPath = remappedNam,
                    NamEnabled = bank.NamEnabled && hasNam
                };
            }).ToList();
        manifest.DualGuitarBanks.Normalize();

        manifest.DualGuitarScenes.Scenes = manifest.DualGuitarScenes.Scenes.Select(scene => scene with
        {
            Guitar1 = RemapDualBankAssets(scene.Guitar1, pathMap),
            Guitar2 = RemapDualBankAssets(scene.Guitar2, pathMap)
        }).ToList();
        manifest.DualGuitarScenes.Normalize();

        string remappedGuitar1Ir = RemapExistingPath(manifest.Audio.Guitar1IrPath, pathMap) ?? string.Empty;
        manifest.Audio.Guitar1IrPath = remappedGuitar1Ir;
        manifest.Audio.Guitar1IrBrowserLastFilePath = remappedGuitar1Ir;
        if (!string.IsNullOrWhiteSpace(remappedGuitar1Ir) && File.Exists(remappedGuitar1Ir))
            manifest.Audio.Guitar1IrBrowserFolder = Path.GetDirectoryName(remappedGuitar1Ir) ?? string.Empty;

        string remappedGuitar1Nam = RemapExistingPath(manifest.Audio.Guitar1NamModelPath, pathMap) ?? string.Empty;
        manifest.Audio.Guitar1NamModelPath = remappedGuitar1Nam;
        if (string.IsNullOrWhiteSpace(remappedGuitar1Nam) || !File.Exists(remappedGuitar1Nam))
            manifest.Audio.Guitar1NamEnabled = false;

        string remappedGlobalNam = RemapExistingPath(manifest.Audio.NamModelPath, pathMap) ?? string.Empty;
        manifest.Audio.NamModelPath = remappedGlobalNam;
        if (string.IsNullOrWhiteSpace(remappedGlobalNam) || !File.Exists(remappedGlobalNam))
            manifest.Audio.NamEnabled = false;

        SceneStore.Save(manifest.Scenes);
        PresetStore.Save(manifest.Presets);
        DualGuitarBankStore.Save(manifest.DualGuitarBanks);
        DualGuitarSceneStore.Save(manifest.DualGuitarScenes);
        AudioSettingsStore.Save(manifest.Audio);
        MidiSettingsStore.Save(manifest.Midi);
        NamLibraryStore.Save(restoredLibrary);

        return new BackupRestoreResult
        {
            Scenes = manifest.Scenes,
            Presets = manifest.Presets,
            Audio = manifest.Audio,
            NamLibrary = restoredLibrary,
            Midi = manifest.Midi,
            DualGuitarBanks = manifest.DualGuitarBanks,
            DualGuitarScenes = manifest.DualGuitarScenes,
            RestoredNamCount = restoredNam,
            RestoredIrCount = restoredIr,
            MissingAssetCount = missingAssets
        };
    }

    private static DualGuitarBankPreset RemapDualBankAssets(DualGuitarBankPreset bank, IReadOnlyDictionary<string, string> map)
    {
        string? remappedNam = RemapExistingPath(bank.NamPath, map);
        bool hasNam = !string.IsNullOrWhiteSpace(remappedNam) && File.Exists(remappedNam);
        return bank with
        {
            Sound = RemapSceneAssets(bank.Sound, map),
            NamPath = remappedNam,
            NamEnabled = bank.NamEnabled && hasNam
        };
    }

    private static ScenePreset RemapSceneAssets(ScenePreset scene, IReadOnlyDictionary<string, string> map)
    {
        string? irA = RemapExistingPath(scene.ExternalIrPath, map);
        string? irB = RemapExistingPath(scene.ExternalIrBPath, map);
        return scene with
        {
            ExternalIrPath = irA,
            ExternalIrEnabled = scene.ExternalIrEnabled && !string.IsNullOrWhiteSpace(irA) && File.Exists(irA),
            ExternalIrBPath = irB,
            ExternalIrBEnabled = scene.ExternalIrBEnabled && !string.IsNullOrWhiteSpace(irB) && File.Exists(irB)
        };
    }

    private static string? RemapExistingPath(string? path, IReadOnlyDictionary<string, string> map)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        string key = NormalizeMapKey(path);
        if (map.TryGetValue(key, out string? mapped)) return mapped;
        try { return File.Exists(path) ? Path.GetFullPath(path) : path; }
        catch { return path; }
    }

    private static IEnumerable<string> EnumerateNamPaths(UserPresetLibrary presets, DualGuitarBankLibrary dualGuitarBanks, DualGuitarSceneLibrary dualGuitarScenes, AudioPreferences audio)
    {
        if (!string.IsNullOrWhiteSpace(audio.NamModelPath)) yield return audio.NamModelPath;
        if (!string.IsNullOrWhiteSpace(audio.Guitar1NamModelPath)) yield return audio.Guitar1NamModelPath;
        foreach (UserPreset preset in presets.Presets)
            if (!string.IsNullOrWhiteSpace(preset.NamPath)) yield return preset.NamPath;
        foreach (DualGuitarBankPreset bank in dualGuitarBanks.Guitar1Banks.Concat(dualGuitarBanks.Guitar2Banks))
            if (!string.IsNullOrWhiteSpace(bank.NamPath)) yield return bank.NamPath;
        foreach (DualGuitarScenePreset scene in dualGuitarScenes.Scenes)
        {
            if (!string.IsNullOrWhiteSpace(scene.Guitar1.NamPath)) yield return scene.Guitar1.NamPath;
            if (!string.IsNullOrWhiteSpace(scene.Guitar2.NamPath)) yield return scene.Guitar2.NamPath;
        }
    }

    private static IEnumerable<string> EnumerateIrPaths(SceneLibrary scenes, UserPresetLibrary presets, DualGuitarBankLibrary dualGuitarBanks, DualGuitarSceneLibrary dualGuitarScenes, AudioPreferences audio)
    {
        if (!string.IsNullOrWhiteSpace(audio.Guitar1IrPath)) yield return audio.Guitar1IrPath;
        foreach (ScenePreset scene in scenes.Scenes)
        {
            if (!string.IsNullOrWhiteSpace(scene.ExternalIrPath)) yield return scene.ExternalIrPath;
            if (!string.IsNullOrWhiteSpace(scene.ExternalIrBPath)) yield return scene.ExternalIrBPath;
        }
        foreach (UserPreset preset in presets.Presets)
        {
            if (!string.IsNullOrWhiteSpace(preset.Sound.ExternalIrPath)) yield return preset.Sound.ExternalIrPath;
            if (!string.IsNullOrWhiteSpace(preset.Sound.ExternalIrBPath)) yield return preset.Sound.ExternalIrBPath;
        }
        foreach (DualGuitarBankPreset bank in dualGuitarBanks.Guitar1Banks.Concat(dualGuitarBanks.Guitar2Banks))
        {
            if (!string.IsNullOrWhiteSpace(bank.Sound.ExternalIrPath)) yield return bank.Sound.ExternalIrPath;
            if (!string.IsNullOrWhiteSpace(bank.Sound.ExternalIrBPath)) yield return bank.Sound.ExternalIrBPath;
        }
        foreach (DualGuitarScenePreset scene in dualGuitarScenes.Scenes)
        {
            foreach (DualGuitarBankPreset bank in new[] { scene.Guitar1, scene.Guitar2 })
            {
                if (!string.IsNullOrWhiteSpace(bank.Sound.ExternalIrPath)) yield return bank.Sound.ExternalIrPath;
                if (!string.IsNullOrWhiteSpace(bank.Sound.ExternalIrBPath)) yield return bank.Sound.ExternalIrBPath;
            }
        }
    }

    private static void AddFile(ZipArchive archive, string sourcePath, string entryName)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using Stream source = File.OpenRead(sourcePath);
        using Stream destination = entry.Open();
        source.CopyTo(destination);
    }

    private static void ExtractEntry(ZipArchiveEntry entry, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using Stream source = entry.Open();
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(output);
    }

    private static NamLibraryItem CloneNamItem(NamLibraryItem source) => new()
    {
        Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id,
        Name = source.Name,
        Path = source.Path,
        OriginalSourcePath = source.OriginalSourcePath,
        IncludesCabinet = source.IncludesCabinet,
        InputTrimDb = source.InputTrimDb,
        OutputTrimDb = source.OutputTrimDb,
        AutoLevelEnabled = source.AutoLevelEnabled,
        AutoLevelDb = source.AutoLevelDb,
        LastCalibrationRmsDb = source.LastCalibrationRmsDb,
        Category = source.Category,
        Favorite = source.Favorite
    };

    private static string NormalizeMapKey(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path.Trim(); }
    }

    private static string SanitizeFileName(string? name)
    {
        string value = string.IsNullOrWhiteSpace(name) ? "archivo" : name;
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value;
    }

    private static string GetUniquePath(string directory, string fileName)
    {
        string first = Path.Combine(directory, fileName);
        if (!File.Exists(first)) return first;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for (int index = 2; index <= 9999; index++)
        {
            string candidate = Path.Combine(directory, $"{stem}_{index}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(directory, $"{stem}_{Guid.NewGuid():N}{extension}");
    }

    private sealed class BackupManifest
    {
        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public string AppVersion { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public SceneLibrary? Scenes { get; set; }
        public UserPresetLibrary? Presets { get; set; }
        public DualGuitarBankLibrary? DualGuitarBanks { get; set; }
        public DualGuitarSceneLibrary? DualGuitarScenes { get; set; }
        public AudioPreferences? Audio { get; set; }
        public MidiSettings? Midi { get; set; }
        public List<BackupNamItem>? NamItems { get; set; } = new();
        public List<BackupAsset>? IrAssets { get; set; } = new();
    }

    private sealed class BackupNamItem
    {
        public NamLibraryItem Metadata { get; set; } = new();
        public string EntryName { get; set; } = string.Empty;
    }

    private sealed class BackupAsset
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string EntryName { get; set; } = string.Empty;
    }
}
