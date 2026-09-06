namespace GDMAmpAccessible.Models;

public enum AmpChannel
{
    CleanTwin = 0,
    CrunchBritish = 1,
    // Nombre interno conservado para compatibilidad con escenas antiguas; desde 2.11.0
    // el voicing audible es un lead valvular cálido de alta ganancia.
    LeadJcm800 = 2,
    CleanBoutique = 3,
    CleanClassA = 4,
    CrunchPlexi = 5,
    CrunchClassA = 6,
    LeadModern = 7,
    LeadLegacy = 8
}

public enum PreEffectSlot
{
    Booster = 0,
    Compressor = 1,
    AutoWah = 2,
    Gate = 3,
    Divine = 4,
    Ds1 = 5,
    Chorus = 6,
    Octaver = 7,
    // Valores nuevos agregados al final para preservar compatibilidad con escenas JSON antiguas.
    Od1 = 8,
    Fuzz = 9,
    Eq5 = 10
}

public enum OctaverCharacter { Down = 0, Up = 1, Dual = 2, Sub = 3, Organ = 4 }
public enum BoosterCharacter { CaeLineDriver = 0, EpStyle = 1, TrebleBooster = 2 }
public enum FlangerCharacter { Classic = 0, Jet = 1, TapeZero = 2 }
public enum ChorusPlacement { Loop = 0, Input = 1 }
public enum CompressorCharacter { StudioClean = 0, Dyna = 1, Optical = 2, Sustainer = 3 }
public enum AutoWahMode { Dynamic = 0, ManualExpression = 1 }
public enum AutoWahCharacter { Classic = 0, VaiBadHorsie = 1, SatrianiBigBad = 2 }
public enum DistortionCharacter { Ds1 = 0, Guvnor = 1, Rat = 2, Modern = 3 }
public enum DriveCharacter { Ts808 = 0, Ts9 = 1, Klon = 2, Marshall = 3, Dod250 = 4 }
public enum Od1Character { Vintage1977 = 0, Late4558 = 1, OcdLp = 2, OcdHp = 3 }
public enum FuzzCharacter { FuzzFace = 0, BigMuff = 1 }
public enum EqPlacement { BeforeAmp = 0, AfterAmp = 1 }
public enum ChorusCharacter { Ensemble = 0, Dimension = 1 }
public enum DelayCharacter { DigitalClean = 0, AnalogDark = 1, Tape = 2, Reverse = 3 }
public enum ReverbCharacter { Spring = 0, Plate = 1, Room = 2, Hall = 3, Church = 4, Shimmer = 5, Cathedral = 6 }
public enum TempoDivision { Quarter = 0, Eighth = 1, DottedEighth = 2, Triplet = 3, Sixteenth = 4 }

public sealed record DspParameters
{
    public int Revision { get; init; }
    public bool SimulationEnabled { get; init; } = true;
    public AmpChannel Channel { get; init; } = AmpChannel.CleanTwin;

    public float Gain { get; init; } = 3.0f;
    public float Bass { get; init; } = 5.0f;
    public float Middle { get; init; } = 4.0f;
    public float Treble { get; init; } = 6.0f;
    public float Presence { get; init; } = 5.0f;

    // EQ independiente precargada para los tres canales. Esto permite cambiar de canal
    // dentro del callback sin recalcular coeficientes de filtros en tiempo real.
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

    // Procesamiento independiente de voz: entrada física 1 de la interfaz ASIO.
    // No forma parte de las escenas de guitarra.
    public bool VoiceEnabled { get; init; }
    public bool VoiceOnlyMode { get; init; }
    public bool VoiceSuppressorEnabled { get; init; } = true;
    public float VoiceThresholdDb { get; init; } = -48.0f;
    public float VoiceReductionDb { get; init; } = 30.0f;
    public float VoiceReleaseMs { get; init; } = 220.0f;
    public float VoiceHighPassHz { get; init; } = 90.0f;
    public float VoiceBassDb { get; init; } = 0.0f;
    public float VoiceMidDb { get; init; } = 0.0f;
    public float VoiceTrebleDb { get; init; } = 0.0f;
    public float VoiceLevelPercent { get; init; } = 100.0f;
    public float VoiceMonitorPercent { get; init; } = 70.0f;
    public float MeetGuitarPercent { get; init; } = 100.0f;
    public float MeetVoicePercent { get; init; } = 125.0f;

    public bool TunerEnabled { get; init; }
    public bool TunerMuteOutput { get; init; } = true;
    public bool TunerSoundGuideEnabled { get; init; }
    public float TunerReferenceAHz { get; init; } = 440.0f;
    public float TunerGuideVolumePercent { get; init; } = 18.0f;

    // Metrónomo global. No forma parte de las escenas de guitarra.
    public bool MetronomeEnabled { get; init; }
    public float MetronomeBpm { get; init; } = 80.0f;
    public int MetronomeBeatsPerBar { get; init; } = 4;
    public bool MetronomeAccentFirstBeat { get; init; } = true;
    public float MetronomeVolumePercent { get; init; } = 25.0f;

    // Batería de acompañamiento global. Comparte BPM con el metrónomo.
    public bool DrumsEnabled { get; init; }
    public int DrumPattern { get; init; } = 1;
    public float DrumVolumePercent { get; init; } = 35.0f;

    // Bajo de acompañamiento global. Sigue BPM y patrón de la batería.
    public bool BackingBassEnabled { get; init; }
    public int BackingBassKey { get; init; } = 7; // Sol
    public bool BackingBassMinor { get; init; }
    public int BackingBassLine { get; init; } = 1; // Raíz-quinta
    public float BackingBassVolumePercent { get; init; } = 28.0f;

    // Piano de acompañamiento global. Comparte BPM pero mantiene armonía y sonido propios.
    public bool PianoEnabled { get; init; }
    public int PianoSound { get; init; } = 0;
    public int PianoKey { get; init; } = 7;
    public int PianoProgression { get; init; } = 0;
    // Compatibilidad histórica: los ocho packs conservan las primeras 32 posiciones. Cada ulong guarda cuatro
    // posiciones de 16 bits (grado, calidad, alteración y duración). Se evita
    // interpretar texto o asignar memoria en el callback ASIO.
    public ulong PianoCustomProgressionPack1 { get; init; }
    public ulong PianoCustomProgressionPack2 { get; init; }
    public ulong PianoCustomProgressionPack3 { get; init; }
    public ulong PianoCustomProgressionPack4 { get; init; }
    public ulong PianoCustomProgressionPack5 { get; init; }
    public ulong PianoCustomProgressionPack6 { get; init; }
    public ulong PianoCustomProgressionPack7 { get; init; }
    public ulong PianoCustomProgressionPack8 { get; init; }
    public int PianoCustomProgressionCount { get; init; }
    // 2.41.47: secuencia expandida en el hilo de interfaz. Permite bloques (I,V,vi,IV)xN
    // sin interpretar texto ni asignar memoria dentro del callback ASIO. El array se trata como inmutable.
    public ushort[] PianoCustomProgressionSequence { get; init; } = Array.Empty<ushort>();
    public ulong PianoCustomProgressionHash { get; init; }
    public int PianoStyle { get; init; } = 3;
    public float PianoVolumePercent { get; init; } = 24.0f;

    // 2.41.41: cuando batería + bajo + piano se arman juntos con F12, el bus de
    // acompañamiento espera un ataque real de guitarra, arranca desde el tiempo 1
    // y se detiene después de una pausa musical. El metrónomo permanece independiente.
    public bool AccompanimentFollowGuitar { get; init; }

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

    public PreEffectSlot[] PreEffectOrder { get; init; } = new[] { PreEffectSlot.Booster, PreEffectSlot.Compressor, PreEffectSlot.AutoWah, PreEffectSlot.Gate, PreEffectSlot.Octaver, PreEffectSlot.Eq5, PreEffectSlot.Od1, PreEffectSlot.Divine, PreEffectSlot.Ds1, PreEffectSlot.Fuzz, PreEffectSlot.Chorus };

    public bool ExternalIrEnabled { get; init; }
    public bool ExternalIrBEnabled { get; init; }
    public float IrMixPercent { get; init; } = 0.0f;
    public bool IrBPhaseInvert { get; init; }
    public float IrLowCutHz { get; init; } = 20.0f;
    public float IrHighCutHz { get; init; } = 20000.0f;

    // Neural Amp Modeler (NAM). Cuando está activo y hay un modelo válido cargado,
    // reemplaza solamente la etapa de amplificador interno. El gabinete/IR se conserva
    // salvo que NamIncludesCabinet indique que la captura ya contiene caja/micrófono.
    public bool NamEnabled { get; init; }
    public bool NamIncludesCabinet { get; init; }
    public float NamInputTrimDb { get; init; } = 0.0f;
    public float NamOutputTrimDb { get; init; } = 0.0f;
    public bool NamAutoLevelEnabled { get; init; }
    public float NamAutoLevelDb { get; init; } = 0.0f;

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

    // Ensanchador estéreo tipo Eventide H949/H3000: dos voces microafinadas y retardadas.
    public bool MicroPitchEnabled { get; init; }
    public float MicroPitchDetuneCents { get; init; } = 9.0f;
    public float MicroPitchDelayMs { get; init; } = 10.0f;
    public float MicroPitchMixPercent { get; init; } = 38.0f;

    public bool RotaryEnabled { get; init; }
    public bool RotarySyncEnabled { get; init; }
    public TempoDivision RotaryDivision { get; init; } = TempoDivision.Quarter;
    public float RotaryRateHz { get; init; } = 0.82f;
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
}
