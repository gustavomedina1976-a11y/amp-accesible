namespace GDMAmpAccessible.Models;

public sealed record DualGuitarScenePreset
{
    public string Name { get; init; } = "Escena dual";
    public DualGuitarBankPreset Guitar1 { get; init; } = new() { Name = "Guitarra 1" };
    public DualGuitarBankPreset Guitar2 { get; init; } = new() { Name = "Guitarra 2" };
    public float MasterVolumePercent { get; init; } = 100f;
    public int SelectedGuitarIndex { get; init; }

    public bool MetronomeEnabled { get; init; }
    public float MetronomeBpm { get; init; } = 80f;
    public int MetronomeBeatsPerBar { get; init; } = 4;
    public bool MetronomeAccentFirstBeat { get; init; } = true;
    public float MetronomeVolumePercent { get; init; } = 25f;
    public bool DrumsEnabled { get; init; }
    public int DrumPattern { get; init; } = 1;
    public float DrumVolumePercent { get; init; } = 35f;
    public bool BackingBassEnabled { get; init; }
    public int BackingBassKey { get; init; } = 7;
    public bool BackingBassMinor { get; init; }
    public int BackingBassLine { get; init; } = 1;
    public float BackingBassVolumePercent { get; init; } = 28f;
    public bool PianoEnabled { get; init; }
    public int PianoSound { get; init; } = 0;
    public int PianoKey { get; init; } = 7;
    public int PianoProgression { get; init; } = 0;
    public string PianoCustomProgression { get; init; } = "I, V, vi, IV";
    public int PianoStyle { get; init; } = 3;
    public float PianoVolumePercent { get; init; } = 24f;
}

public sealed class DualGuitarSceneLibrary
{
    public int Version { get; set; } = 3;
    public int LastSelectedIndex { get; set; } = -1;
    public List<DualGuitarScenePreset> Scenes { get; set; } = new();

    public void Normalize()
    {
        Scenes ??= new();
        Scenes = Scenes.Where(scene => scene is not null).Select(scene => scene with
        {
            Name = NormalizeName(scene.Name),
            Guitar1 = scene.Guitar1 ?? new DualGuitarBankPreset { Name = "Guitarra 1" },
            Guitar2 = scene.Guitar2 ?? new DualGuitarBankPreset { Name = "Guitarra 2" },
            MasterVolumePercent = ClampFinite(scene.MasterVolumePercent, 0f, 100f, 100f),
            SelectedGuitarIndex = Math.Clamp(scene.SelectedGuitarIndex, 0, 1),
            MetronomeBpm = ClampFinite(scene.MetronomeBpm, 40f, 240f, 80f),
            MetronomeBeatsPerBar = scene.MetronomeBeatsPerBar is 2 or 3 or 4 or 6 ? scene.MetronomeBeatsPerBar : 4,
            MetronomeVolumePercent = ClampFinite(scene.MetronomeVolumePercent, 0f, 100f, 25f),
            DrumPattern = Math.Clamp(scene.DrumPattern, 0, 5),
            DrumVolumePercent = ClampFinite(scene.DrumVolumePercent, 0f, 100f, 35f),
            BackingBassKey = Math.Clamp(scene.BackingBassKey, 0, 11),
            BackingBassLine = Math.Clamp(scene.BackingBassLine, 0, 4),
            BackingBassVolumePercent = ClampFinite(scene.BackingBassVolumePercent, 0f, 100f, 28f),
            PianoSound = Math.Clamp(scene.PianoSound, 0, 5),
            PianoKey = Math.Clamp(scene.PianoKey, 0, 23),
            PianoProgression = Math.Clamp(scene.PianoProgression, 0, 5),
            PianoCustomProgression = string.IsNullOrWhiteSpace(scene.PianoCustomProgression) ? "I, V, vi, IV" : scene.PianoCustomProgression.Trim(),
            PianoStyle = Math.Clamp(scene.PianoStyle, 0, 8),
            PianoVolumePercent = ClampFinite(scene.PianoVolumePercent, 0f, 100f, 24f)
        }).ToList();
        LastSelectedIndex = Scenes.Count == 0 ? -1 : Math.Clamp(LastSelectedIndex < 0 ? 0 : LastSelectedIndex, 0, Scenes.Count - 1);
        Version = Math.Max(Version, 3);
    }

    private static string NormalizeName(string? value)
    {
        string name = string.IsNullOrWhiteSpace(value) ? "Escena dual" : value.Trim();
        return name.Length <= 60 ? name : name[..60];
    }

    private static float ClampFinite(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
