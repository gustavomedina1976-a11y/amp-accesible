using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Persistence;

// A named group in the existing factory list. No separate storage or loading path.
internal static class CleanPresetBank
{
    public const string Name = "Clean";
    public const string AccessibleName = "Banco Clean dedicado";
    public const string AccessibleDescription = "Sonidos limpios para worship, arpegios, acordes, rítmicas y clases de guitarra.";
    public const int FirstFactoryIndex = 32;
    public static IReadOnlyList<UserPreset> Presets { get; } = new[]
    {
        P("Clean Warm", Base() with
        {
            Bass = 4.8f, Middle = 5.2f, Treble = 4.7f, Presence = 4.0f,
            CompressorSustain = 1.0f, CompressorAttackMs = 28f,
            ReverbCharacter = ReverbCharacter.Spring, ReverbMixPercent = 13f,
            ReverbDecayPercent = 38f, ReverbPreDelayMs = 18f
        }),
        P("Clean Worship", Base() with
        {
            Bass = 4.3f, Middle = 5.2f, Treble = 5.3f, Presence = 4.3f,
            CompressorSustain = 1.8f, CompressorAttackMs = 28f,
            DelayEnabled = true, DelayTimeMs = 420f, DelayFeedbackPercent = 25f, DelayMixPercent = 18f,
            ReverbCharacter = ReverbCharacter.Hall, ReverbMixPercent = 19f,
            ReverbDecayPercent = 55f, ReverbPreDelayMs = 32f
        }),
        P("Clean Arpeggio", Base() with
        {
            Bass = 4.0f, Middle = 5.0f, Treble = 5.5f, Presence = 4.5f,
            CompressorSustain = 2.0f, CompressorAttackMs = 30f,
            DelayEnabled = true, DelayTimeMs = 230f, DelayFeedbackPercent = 13f, DelayMixPercent = 10f,
            ReverbCharacter = ReverbCharacter.Plate, ReverbMixPercent = 11f,
            ReverbDecayPercent = 28f, ReverbPreDelayMs = 22f
        }),
        P("Clean Rhythm", Base() with
        {
            Bass = 3.9f, Middle = 5.5f, Treble = 5.2f, Presence = 4.3f,
            CompressorSustain = 2.8f, CompressorAttackMs = 20f,
            ReverbCharacter = ReverbCharacter.Room, ReverbMixPercent = 7f,
            ReverbDecayPercent = 22f, ReverbPreDelayMs = 10f
        }),
        P("Clean Ambient", Base() with
        {
            Bass = 4.2f, Middle = 5.0f, Treble = 5.0f, Presence = 4.0f,
            CompressorSustain = 1.6f, CompressorAttackMs = 32f,
            DelayEnabled = true, DelayTimeMs = 620f, DelayFeedbackPercent = 32f, DelayMixPercent = 24f,
            ReverbCharacter = ReverbCharacter.Hall, ReverbMixPercent = 27f,
            ReverbDecayPercent = 70f, ReverbPreDelayMs = 38f
        })
    };

    public static bool Contains(UserPreset preset) => Presets.Contains(preset);
    public static bool IsCleanSound(ScenePreset? sound) => sound is not null && Presets.Any(p => p.Name == sound.Name);

    public static ScenePreset PreserveEq(ScenePreset sound, ScenePreset source) => sound with
    {
        Bass = source.Bass, Middle = source.Middle, Treble = source.Treble, Presence = source.Presence,
        CleanBass = source.CleanBass, CleanMiddle = source.CleanMiddle,
        CleanTreble = source.CleanTreble, CleanPresence = source.CleanPresence,
        CrunchBass = source.CrunchBass, CrunchMiddle = source.CrunchMiddle,
        CrunchTreble = source.CrunchTreble, CrunchPresence = source.CrunchPresence,
        LeadBass = source.LeadBass, LeadMiddle = source.LeadMiddle,
        LeadTreble = source.LeadTreble, LeadPresence = source.LeadPresence
    };

    public static DspParameters ApplyEq(DspParameters target, ScenePreset source) => target with
    {
        Bass = source.Bass, Middle = source.Middle, Treble = source.Treble, Presence = source.Presence,
        CleanBass = source.CleanBass, CleanMiddle = source.CleanMiddle,
        CleanTreble = source.CleanTreble, CleanPresence = source.CleanPresence,
        CrunchBass = source.CrunchBass, CrunchMiddle = source.CrunchMiddle,
        CrunchTreble = source.CrunchTreble, CrunchPresence = source.CrunchPresence,
        LeadBass = source.LeadBass, LeadMiddle = source.LeadMiddle,
        LeadTreble = source.LeadTreble, LeadPresence = source.LeadPresence
    };

    public static string Description(UserPreset preset) => preset.Name switch
    {
        "Clean Warm" => "Limpio cálido y redondo con Spring Reverb suave.",
        "Clean Worship" => "Limpio espacioso con delay y reverb moderados.",
        "Clean Arpeggio" => "Limpio definido para escuchar cada cuerda.",
        "Clean Rhythm" => "Limpio firme y directo para rasgueos.",
        "Clean Ambient" => "Limpio ambiental con delay y reverb larga controlada.",
        _ => ""
    };

    // Recognition after restoring scenes uses existing sound fields, not a new JSON ID.
    // Runs only on the UI thread while preparing diagnostics/accessibility.
    public static UserPreset? Find(ScenePreset sound) => Presets.FirstOrDefault(p =>
        Identity(p.Sound).Equals(Identity(sound)));

    private static object Identity(ScenePreset s) => new
    {
        s.Channel, s.Gain, s.Bass, s.Middle, s.Treble, s.Presence, s.OutputPercent,
        s.GateEnabled, s.CompressorEnabled, s.CompressorCharacter, s.CompressorSustain,
        s.CompressorAttackMs, s.CompressorLevel, s.BoosterEnabled, s.OverdriveEnabled,
        s.Od1Enabled, s.Ts9Enabled, s.FuzzEnabled, s.Eq5Enabled, s.ChorusEnabled,
        s.AnalogChorusEnabled, s.AutoWahEnabled, s.OctaverEnabled, s.PhaserEnabled,
        s.FlangerEnabled, s.MicroPitchEnabled, s.RotaryEnabled, s.TremoloEnabled,
        s.DelayEnabled, s.DelaySyncEnabled, s.DelayCharacter, s.DelayTimeMs,
        s.DelayFeedbackPercent, s.DelayMixPercent, s.ReverbEnabled, s.ReverbCharacter,
        s.ReverbMixPercent, s.ReverbDecayPercent, s.ReverbPreDelayMs,
        s.ReverbTonePercent, s.ReverbDampingPercent, s.ReverbDiffusionPercent
    };

    public static ScenePreset PreserveCabinet(ScenePreset sound, ScenePreset current) => sound with
    {
        ExternalIrEnabled = current.ExternalIrEnabled, ExternalIrPath = current.ExternalIrPath,
        ExternalIrBEnabled = current.ExternalIrBEnabled, ExternalIrBPath = current.ExternalIrBPath,
        IrMixPercent = current.IrMixPercent, IrBPhaseInvert = current.IrBPhaseInvert,
        IrLowCutHz = current.IrLowCutHz, IrHighCutHz = current.IrHighCutHz
    };

    private static ScenePreset Base() => new()
    {
        Channel = AmpChannel.CleanTwin, Gain = 1.5f, OutputPercent = 33f,
        GateEnabled = false, CompressorEnabled = true,
        CompressorCharacter = CompressorCharacter.StudioClean, CompressorLevel = 5f,
        DelayCharacter = DelayCharacter.DigitalClean, DelaySyncEnabled = false,
        ReverbEnabled = true, ReverbTonePercent = 48f,
        ReverbDampingPercent = 55f, ReverbDiffusionPercent = 70f
    };

    private static UserPreset P(string name, ScenePreset sound) => new()
    {
        Name = name,
        Sound = sound with
        {
            Name = name, CleanBass = sound.Bass, CleanMiddle = sound.Middle,
            CleanTreble = sound.Treble, CleanPresence = sound.Presence
        },
        NamEnabled = false,
        PreEffectOrder = UserPreset.DefaultOrder()
    };
}
