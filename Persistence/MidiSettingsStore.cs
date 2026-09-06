using System.Text.Json;

namespace GDMAmpAccessible.Persistence;

internal enum MidiMessageKind
{
    ControlChange = 0,
    ProgramChange = 1
}

internal sealed class MidiBinding
{
    public MidiMessageKind MessageKind { get; set; } = MidiMessageKind.ControlChange;
    public int Channel { get; set; } = 1;
    public int Number { get; set; }
    public string ActionId { get; set; } = string.Empty;
}

internal sealed class MidiSettings
{
    public int Version { get; set; } = 1;
    public string PreferredInputDevice { get; set; } = string.Empty;
    public bool AutoConnect { get; set; } = true;
    public bool ProgramChangesLoadFactoryBanks { get; set; } = true;
    public List<MidiBinding> Bindings { get; set; } = new();

    public void Normalize()
    {
        PreferredInputDevice ??= string.Empty;
        Bindings ??= new();
        Bindings = Bindings
            .Where(binding => binding is not null && !string.IsNullOrWhiteSpace(binding.ActionId))
            .Select(binding => new MidiBinding
            {
                MessageKind = binding.MessageKind is MidiMessageKind.ProgramChange
                    ? MidiMessageKind.ProgramChange
                    : MidiMessageKind.ControlChange,
                Channel = Math.Clamp(binding.Channel, 1, 16),
                Number = Math.Clamp(binding.Number, 0, 127),
                ActionId = binding.ActionId.Trim()
            })
            .GroupBy(binding => $"{(int)binding.MessageKind}:{binding.Channel}:{binding.Number}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
    }
}

internal static class MidiSettingsStore
{
    private static string FilePath
    {
        get
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GDM Amp Accessible");
            return Path.Combine(folder, "midi.json");
        }
    }

    public static MidiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                MidiSettings? settings = JsonSerializer.Deserialize<MidiSettings>(File.ReadAllText(FilePath));
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch { }

        return new MidiSettings();
    }

    public static void Save(MidiSettings settings)
    {
        settings.Normalize();
        string? folder = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
