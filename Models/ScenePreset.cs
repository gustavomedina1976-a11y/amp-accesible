namespace GDMAmpAccessible.Models;

public sealed record ScenePreset
{
    public string Name { get; init; } = "Escena";

    public AmpChannel Channel { get; init; } = AmpChannel.CleanTwin;
    public float Gain { get; init; } = 3.0f;
    public float Bass { get; init; } = 5.0f;
    public float Middle { get; init; } = 4.0f;
    public float Treble { get; init; } = 6.0f;
    public float Presence { get; init; } = 5.0f;

    // EQ independiente de los tres canales. Estos valores permiten que una escena
    // restaure toda la configuración tonal, no solamente el canal activo.
    public float CleanBass { get; init; } = 5.0f;
    public float CleanMiddle { get; init; } = 4.0f;
    public float CleanTreble { get; init; } = 6.0f;
    public float CleanPresence { get; init; } = 5.0f;

    public float CrunchBass { get; init; } = 5.0f;
    public float CrunchMiddle { get; init; } = 6.0f;
    public float CrunchTreble { get; init; } = 5.2f;
    public float CrunchPresence { get; init; } = 5.0f;

    public float LeadBass { get; init; } = 4.8f;
    public float LeadMiddle { get; init; } = 6.2f;
    public float LeadTreble { get; init; } = 5.0f;
    public float LeadPresence { get; init; } = 5.3f;

    public float OutputPercent { get; init; } = 25.0f;

    public bool OctaverEnabled { get; init; }
    public OctaverCharacter OctaverCharacter { get; init; } = OctaverCharacter.Down;
    public float OctaverDryPercent { get; init; } = 70f;
    public float OctaverDownPercent { get; init; } = 55f;
    public float OctaverUpPercent { get; init; } = 55f;
    public float OctaverTonePercent { get; init; } = 50f;
    public float OctaverLevelPercent { get; init; } = 85f;

    public bool GateEnabled { get; init; } = true;
    public float GateThresholdDb { get; init; } = -58.0f;
    public float GateReleaseMs { get; init; } = 180.0f;

    public bool CompressorEnabled { get; init; }
    public CompressorCharacter CompressorCharacter { get; init; } = CompressorCharacter.StudioClean;
    public float CompressorSustain { get; init; } = 5.0f;
    public float CompressorAttackMs { get; init; } = 18.0f;
    public float CompressorLevel { get; init; } = 5.0f;

    public bool AutoWahEnabled { get; init; }
    public AutoWahMode AutoWahMode { get; init; } = AutoWahMode.Dynamic;
    public AutoWahCharacter AutoWahCharacter { get; init; } = AutoWahCharacter.Classic;
    public float AutoWahSensitivity { get; init; } = 5.0f;
    public float AutoWahRange { get; init; } = 6.0f;
    public float AutoWahResonance { get; init; } = 6.0f;
    public float AutoWahManualPositionPercent { get; init; } = 50.0f;

    // Estos campos representan ahora la distorsión DS-1. Se conservan los nombres internos para compatibilidad JSON.
    public bool OverdriveEnabled { get; init; }
    public DistortionCharacter DistortionCharacter { get; init; } = DistortionCharacter.Ds1;
    public float OverdriveGain { get; init; } = 4.0f;
    public float OverdriveTone { get; init; } = 4.5f;
    public float OverdriveLevel { get; init; } = 5.0f;

    public bool Ts9Enabled { get; init; }
    public DriveCharacter DriveCharacter { get; init; } = DriveCharacter.Ts808;
    public float Ts9Gain { get; init; } = 3.0f;
    public float Ts9Tone { get; init; } = 5.0f;
    public float Ts9Level { get; init; } = 5.0f;

    public bool Od1Enabled { get; init; }
    public Od1Character Od1Character { get; init; } = Od1Character.Vintage1977;
    public float Od1Drive { get; init; } = 3.5f;
    public float Od1Tone { get; init; } = 5.0f;
    public float Od1Level { get; init; } = 5.0f;

    public bool FuzzEnabled { get; init; }
    public FuzzCharacter FuzzCharacter { get; init; } = FuzzCharacter.FuzzFace;
    public float FuzzGain { get; init; } = 5.0f;
    public float FuzzTone { get; init; } = 5.0f;
    public float FuzzLevel { get; init; } = 5.0f;

    public bool Eq5Enabled { get; init; }
    public EqPlacement Eq5Placement { get; init; } = EqPlacement.BeforeAmp;
    public float Eq5Band100Db { get; init; }
    public float Eq5Band250Db { get; init; }
    public float Eq5Band800Db { get; init; }
    public float Eq5Band2500Db { get; init; }
    public float Eq5Band6400Db { get; init; }
    public float Eq5OutputDb { get; init; }

    public bool BoosterEnabled { get; init; }
    public BoosterCharacter BoosterCharacter { get; init; } = BoosterCharacter.CaeLineDriver;
    public float BoosterDb { get; init; } = 6.0f;

    // Orden configurable de los efectos previos al amplificador/NAM.
    // Las escenas antiguas que no tengan este campo usan el orden por defecto.
    public PreEffectSlot[] PreEffectOrder { get; init; } = new[]
    {
        PreEffectSlot.Booster, PreEffectSlot.Compressor, PreEffectSlot.AutoWah,
        PreEffectSlot.Gate, PreEffectSlot.Octaver, PreEffectSlot.Eq5, PreEffectSlot.Od1,
        PreEffectSlot.Divine, PreEffectSlot.Ds1, PreEffectSlot.Fuzz, PreEffectSlot.Chorus
    };

    public bool ExternalIrEnabled { get; init; }
    public string? ExternalIrPath { get; init; }
    public bool ExternalIrBEnabled { get; init; }
    public string? ExternalIrBPath { get; init; }
    public float IrMixPercent { get; init; } = 0.0f;
    public bool IrBPhaseInvert { get; init; }
    public float IrLowCutHz { get; init; } = 20.0f;
    public float IrHighCutHz { get; init; } = 20000.0f;

    public bool FxLoopEnabled { get; init; } = true;
    public float FxLoopSendPercent { get; init; } = 100.0f;
    public float FxLoopReturnPercent { get; init; } = 100.0f;

    public bool PhaserEnabled { get; init; }
    public float PhaserRateHz { get; init; } = 0.55f;
    public float PhaserDepthPercent { get; init; } = 68.0f;
    public float PhaserFeedbackPercent { get; init; } = 18.0f;
    public float PhaserMixPercent { get; init; } = 45.0f;

    public bool FlangerEnabled { get; init; }
    public FlangerCharacter FlangerCharacter { get; init; } = FlangerCharacter.Classic;
    public float FlangerRateHz { get; init; } = 0.35f;
    public float FlangerDepthPercent { get; init; } = 62.0f;
    public float FlangerFeedbackPercent { get; init; } = 28.0f;
    public float FlangerMixPercent { get; init; } = 42.0f;

    public bool ChorusEnabled { get; init; }
    public ChorusPlacement ChorusPlacement { get; init; } = ChorusPlacement.Loop;
    public ChorusCharacter ChorusCharacter { get; init; } = ChorusCharacter.Ensemble;
    public float ChorusRateHz { get; init; } = 0.8f;
    public float ChorusDepthMs { get; init; } = 7.5f;
    public float ChorusMixPercent { get; init; } = 45.0f;

    public bool AnalogChorusEnabled { get; init; }
    public ChorusPlacement AnalogChorusPlacement { get; init; } = ChorusPlacement.Loop;
    public float AnalogChorusRateHz { get; init; } = 0.65f;
    public float AnalogChorusDepth { get; init; } = 6.5f;
    public float AnalogChorusMixPercent { get; init; } = 42.0f;
    public float AnalogChorusLow { get; init; } = 5.0f;
    public float AnalogChorusHigh { get; init; } = 5.0f;

    public bool MicroPitchEnabled { get; init; }
    public float MicroPitchDetuneCents { get; init; } = 9.0f;
    public float MicroPitchDelayMs { get; init; } = 10.0f;
    public float MicroPitchMixPercent { get; init; } = 38.0f;

    public bool RotaryEnabled { get; init; }
    public bool RotarySyncEnabled { get; init; }
    public TempoDivision RotaryDivision { get; init; } = TempoDivision.Quarter;
    public float RotaryRateHz { get; init; }
    public bool RotaryFast { get; init; }
    public float RotaryDepthPercent { get; init; } = 70f;
    public float RotaryMixPercent { get; init; } = 45f;

    public bool TremoloEnabled { get; init; }
    public bool TremoloSyncEnabled { get; init; }
    public TempoDivision TremoloDivision { get; init; } = TempoDivision.Quarter;
    public float TremoloRateHz { get; init; } = 4.0f;
    public float TremoloDepthPercent { get; init; } = 45.0f;

    public bool DelayEnabled { get; init; }
    public bool DelaySyncEnabled { get; init; }
    public TempoDivision DelayDivision { get; init; } = TempoDivision.DottedEighth;
    public DelayCharacter DelayCharacter { get; init; } = DelayCharacter.DigitalClean;
    public float DelayTimeMs { get; init; } = 380.0f;
    public float DelayFeedbackPercent { get; init; } = 25.0f;
    public float DelayMixPercent { get; init; } = 20.0f;

    public bool ReverbEnabled { get; init; } = true;
    public ReverbCharacter ReverbCharacter { get; init; } = ReverbCharacter.Spring;
    public float ReverbMixPercent { get; init; } = 24.0f;
    public float ReverbDecayPercent { get; init; } = 48.0f;
    public float ReverbTonePercent { get; init; } = 55.0f;
    public float ReverbPreDelayMs { get; init; } = 18.0f;
    public float ReverbDampingPercent { get; init; } = 45.0f;
    public float ReverbDiffusionPercent { get; init; } = 60.0f;

    // Desde 2.21.0 una escena nueva puede guardar también el acompañamiento.
    // Las escenas antiguas conservan AccompanimentStored=false y no pisan las preferencias globales.
    public bool AccompanimentStored { get; init; }
    public bool SceneMetronomeEnabled { get; init; }
    public float SceneMetronomeBpm { get; init; } = 80.0f;
    public int SceneMetronomeBeatsPerBar { get; init; } = 4;
    public bool SceneMetronomeAccentFirstBeat { get; init; } = true;
    public float SceneMetronomeVolumePercent { get; init; } = 25.0f;
    public bool SceneDrumsEnabled { get; init; }
    public int SceneDrumPattern { get; init; } = 1;
    public float SceneDrumVolumePercent { get; init; } = 35.0f;
    public bool SceneBackingBassEnabled { get; init; }
    public int SceneBackingBassKey { get; init; } = 7;
    public bool SceneBackingBassMinor { get; init; }
    public int SceneBackingBassLine { get; init; } = 1;
    public float SceneBackingBassVolumePercent { get; init; } = 28.0f;
    public bool ScenePianoEnabled { get; init; }
    public int ScenePianoSound { get; init; } = 0;
    public int ScenePianoKey { get; init; } = 7;
    public int ScenePianoProgression { get; init; } = 0;
    public string ScenePianoCustomProgression { get; init; } = "I, V, vi, IV";
    public int ScenePianoStyle { get; init; } = 3;
    public float ScenePianoVolumePercent { get; init; } = 24.0f;

    public static List<ScenePreset> CreateFactoryDefaults()
    {
        return new List<ScenePreset>
        {
            new()
            {
                Name = "Limpio",
                Channel = AmpChannel.CleanTwin,
                Gain = 3.0f,
                Bass = 5.0f,
                Middle = 4.0f,
                Treble = 6.0f,
                Presence = 5.0f,
                OutputPercent = 25.0f,
                CompressorEnabled = true,
                CompressorSustain = 4.8f,
                CompressorAttackMs = 20.0f,
                CompressorLevel = 5.0f,
                ReverbEnabled = true,
                ReverbMixPercent = 24.0f
            },
            new()
            {
                Name = "Crunch",
                Channel = AmpChannel.CrunchBritish,
                Gain = 4.8f,
                Bass = 5.0f,
                Middle = 6.0f,
                Treble = 5.2f,
                Presence = 5.0f,
                OutputPercent = 22.0f,
                Ts9Enabled = true,
                Ts9Gain = 1.8f,
                Ts9Tone = 5.2f,
                Ts9Level = 5.5f,
                ReverbEnabled = true,
                ReverbMixPercent = 16.0f
            },
            new()
            {
                Name = "Lead",
                Channel = AmpChannel.LeadJcm800,
                Gain = 5.5f,
                Bass = 4.8f,
                Middle = 6.2f,
                Treble = 5.0f,
                Presence = 5.3f,
                OutputPercent = 20.0f,
                OverdriveEnabled = false,
                OverdriveGain = 4.0f,
                OverdriveTone = 4.5f,
                OverdriveLevel = 5.0f,
                BoosterEnabled = true,
                BoosterDb = 4.0f,
                ReverbEnabled = true,
                ReverbMixPercent = 18.0f
            }
        };
    }
}

public sealed class SceneLibrary
{
    public int Version { get; set; } = 9;
    public int LastSelectedIndex { get; set; }
    public List<ScenePreset> Scenes { get; set; } = ScenePreset.CreateFactoryDefaults();

    public void Normalize()
    {
        Scenes ??= new List<ScenePreset>();
        List<ScenePreset> defaults = ScenePreset.CreateFactoryDefaults();

        while (Scenes.Count < 3)
        {
            Scenes.Add(defaults[Scenes.Count]);
        }

        if (Scenes.Count > 3)
        {
            Scenes = Scenes.Take(3).ToList();
        }

        bool migrateCentaurToDs1 = Version < 3;
        bool migratePerChannelEq = Version < 4;

        for (int index = 0; index < Scenes.Count; index++)
        {
            ScenePreset scene = Scenes[index] ?? defaults[index];
            if (migrateCentaurToDs1)
            {
                // El Centaur fue eliminado. No trasladamos su estado encendido al DS-1,
                // porque son pedales de carácter muy distinto.
                scene = scene with
                {
                    OverdriveEnabled = false,
                    OverdriveGain = 4.0f,
                    OverdriveTone = 4.5f,
                    OverdriveLevel = 5.0f
                };
            }
            if (migratePerChannelEq)
            {
                // Las escenas anteriores guardaban solamente la EQ del canal activo.
                // Conservamos esa EQ en su canal y dejamos valores seguros en los demás.
                scene = scene.Channel switch
                {
                    AmpChannel.CleanTwin => scene with
                    {
                        CleanBass = scene.Bass,
                        CleanMiddle = scene.Middle,
                        CleanTreble = scene.Treble,
                        CleanPresence = scene.Presence
                    },
                    AmpChannel.CrunchBritish => scene with
                    {
                        CrunchBass = scene.Bass,
                        CrunchMiddle = scene.Middle,
                        CrunchTreble = scene.Treble,
                        CrunchPresence = scene.Presence
                    },
                    AmpChannel.LeadJcm800 => scene with
                    {
                        LeadBass = scene.Bass,
                        LeadMiddle = scene.Middle,
                        LeadTreble = scene.Treble,
                        LeadPresence = scene.Presence
                    },
                    _ => scene
                };
            }
            string name = string.IsNullOrWhiteSpace(scene.Name) ? defaults[index].Name : scene.Name.Trim();
            if (name.Length > 40)
            {
                name = name[..40];
            }
            Scenes[index] = scene with { Name = name };
        }

        Version = Math.Max(Version, 8);
        LastSelectedIndex = Math.Clamp(LastSelectedIndex, 0, 2);
    }
}
