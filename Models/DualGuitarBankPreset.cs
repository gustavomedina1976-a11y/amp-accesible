namespace GDMAmpAccessible.Models;

public sealed record DualGuitarBankPreset
{
    public string Name { get; init; } = "Banco";
    public ScenePreset Sound { get; init; } = new();
    public float MixPercent { get; init; } = 70f;
    // 2.41.30: paneo independiente. -100 = izquierda, 0 = centro, +100 = derecha.
    // El valor por defecto 0 mantiene exactamente el sonido de bancos creados antes de esta versión.
    public float PanPercent { get; init; } = 0f;
    public bool Muted { get; init; }
    public bool ProcessingEnabled { get; init; } = true;
    public bool EffectsEnabled { get; init; } = true;

    // NAM independiente por guitarra. Desde 2.41.21 estos mismos campos se usan
    // tanto en bancos de Guitarra 1 como de Guitarra 2; cada DSP carga su propia
    // instancia del modelo sin modificar la otra guitarra.
    public bool NamEnabled { get; init; }
    public bool NamIncludesCabinet { get; init; }
    public string? NamPath { get; init; }
    public float NamInputTrimDb { get; init; }
    public float NamOutputTrimDb { get; init; }
    public bool NamAutoLevelEnabled { get; init; }
    public float NamAutoLevelDb { get; init; }
}

public sealed class DualGuitarBankLibrary
{
    public int Version { get; set; } = 3;
    public int Guitar1LastSelectedIndex { get; set; } = -1;
    public int Guitar2LastSelectedIndex { get; set; } = -1;
    public List<DualGuitarBankPreset> Guitar1Banks { get; set; } = new();
    public List<DualGuitarBankPreset> Guitar2Banks { get; set; } = new();

    public List<DualGuitarBankPreset> BanksFor(int guitarIndex) => guitarIndex == 1 ? Guitar2Banks : Guitar1Banks;

    public int LastIndexFor(int guitarIndex) => guitarIndex == 1 ? Guitar2LastSelectedIndex : Guitar1LastSelectedIndex;

    public void SetLastIndexFor(int guitarIndex, int value)
    {
        if (guitarIndex == 1) Guitar2LastSelectedIndex = value;
        else Guitar1LastSelectedIndex = value;
    }

    public void Normalize()
    {
        Guitar1Banks ??= new();
        Guitar2Banks ??= new();
        Guitar1Banks = NormalizeBanks(Guitar1Banks);
        Guitar2Banks = NormalizeBanks(Guitar2Banks);
        Guitar1LastSelectedIndex = NormalizeIndex(Guitar1LastSelectedIndex, Guitar1Banks.Count);
        Guitar2LastSelectedIndex = NormalizeIndex(Guitar2LastSelectedIndex, Guitar2Banks.Count);
        Version = Math.Max(Version, 3);
    }

    private static List<DualGuitarBankPreset> NormalizeBanks(IEnumerable<DualGuitarBankPreset>? source)
    {
        if (source is null) return new();
        return source.Where(bank => bank is not null).Select(bank => bank with
        {
            Name = NormalizeName(bank.Name),
            MixPercent = ClampFinite(bank.MixPercent, 0f, 100f, 70f),
            PanPercent = ClampFinite(bank.PanPercent, -100f, 100f, 0f),
            NamInputTrimDb = ClampFinite(bank.NamInputTrimDb, -24f, 24f, 0f),
            NamOutputTrimDb = ClampFinite(bank.NamOutputTrimDb, -24f, 24f, 0f),
            NamAutoLevelDb = ClampFinite(bank.NamAutoLevelDb, -24f, 24f, 0f)
        }).ToList();
    }

    private static string NormalizeName(string? value)
    {
        string name = string.IsNullOrWhiteSpace(value) ? "Banco" : value.Trim();
        return name.Length <= 60 ? name : name[..60];
    }

    private static int NormalizeIndex(int index, int count) => count == 0 ? -1 : Math.Clamp(index, 0, count - 1);

    private static float ClampFinite(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
