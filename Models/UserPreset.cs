namespace GDMAmpAccessible.Models;

public sealed record UserPreset
{
    public string Name { get; init; } = "Preset";
    public ScenePreset Sound { get; init; } = new();
    public bool NamEnabled { get; init; }
    public bool NamIncludesCabinet { get; init; }
    public string? NamPath { get; init; }
    public float NamInputTrimDb { get; init; }
    public float NamOutputTrimDb { get; init; }
    public bool NamAutoLevelEnabled { get; init; }
    public float NamAutoLevelDb { get; init; }
    public List<PreEffectSlot> PreEffectOrder { get; init; } = DefaultOrder();

    public static List<PreEffectSlot> DefaultOrder() => new()
    {
        PreEffectSlot.Booster, PreEffectSlot.Compressor, PreEffectSlot.AutoWah,
        PreEffectSlot.Gate, PreEffectSlot.Octaver, PreEffectSlot.Eq5, PreEffectSlot.Od1,
        PreEffectSlot.Divine, PreEffectSlot.Ds1, PreEffectSlot.Fuzz, PreEffectSlot.Chorus
    };
}

public sealed class UserPresetLibrary
{
    public int Version { get; set; } = 1;
    public int LastSelectedIndex { get; set; } = -1;
    public List<UserPreset> Presets { get; set; } = new();

    public void Normalize()
    {
        Presets ??= new();
        Presets = Presets.Where(p => p is not null).Select(p => p with
        {
            Name = NormalizeName(p.Name),
            PreEffectOrder = NormalizeOrder(p.PreEffectOrder)
        }).ToList();
        LastSelectedIndex = Presets.Count == 0 ? -1 : Math.Clamp(LastSelectedIndex, 0, Presets.Count - 1);
    }

    private static string NormalizeName(string? name)
    {
        string value = string.IsNullOrWhiteSpace(name) ? "Preset" : name.Trim();
        return value.Length <= 60 ? value : value[..60];
    }

    public static List<PreEffectSlot> NormalizeOrder(IEnumerable<PreEffectSlot>? order)
    {
        var valid = Enum.GetValues<PreEffectSlot>();
        var result = new List<PreEffectSlot>();
        if (order is not null)
            foreach (var item in order) if (valid.Contains(item) && !result.Contains(item)) result.Add(item);
        foreach (var item in valid) if (!result.Contains(item)) result.Add(item);
        return result;
    }
}
