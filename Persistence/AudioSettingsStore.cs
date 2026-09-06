using System.Text.Json;
using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Persistence;

internal sealed class AudioPreferences
{
    public int BufferSize { get; set; } = 256;
    public string AsioDriverName { get; set; } = string.Empty;
    public int GuitarInputIndex { get; set; } = 1; // Entrada 2 por defecto cuando existe; en interfaces de una entrada se ajusta a 0.

    // Volumen final de todo Amp Accessible. Se aplica después del DSP y del looper,
    // justo antes de Playback 1-2 y de la salida virtual. No altera presets ni grabaciones.
    public float MasterVolumePercent { get; set; } = 100f;

    // Afinador en Modo Dos Guitarras: 0 = Guitarra 1 / Input 1, 1 = Guitarra 2 / Input 2.
    // Fuera del modo dual este valor se conserva pero el afinador usa el rig principal.
    public int TunerGuitarIndex { get; set; } = 1;

    // Explorador de carpetas IR. Guarda la última carpeta indexada y el último archivo
    // recorrido para continuar rápidamente entre distintas tomas del mismo gabinete.
    public string IrBrowserFolder { get; set; } = string.Empty;
    public string IrBrowserLastFilePath { get; set; } = string.Empty;

    // Ecualización propia de cada canal. Se guarda fuera de las escenas para que,
    // al cambiar manualmente de canal, cada amplificador recupere su tono.
    public float CleanBass { get; set; } = 5.0f;
    public float CleanMiddle { get; set; } = 4.0f;
    public float CleanTreble { get; set; } = 6.0f;
    public float CleanPresence { get; set; } = 5.0f;

    public float CrunchBass { get; set; } = 5.0f;
    public float CrunchMiddle { get; set; } = 6.0f;
    public float CrunchTreble { get; set; } = 5.2f;
    public float CrunchPresence { get; set; } = 5.0f;

    public float LeadBass { get; set; } = 4.8f;
    public float LeadMiddle { get; set; } = 6.2f;
    public float LeadTreble { get; set; } = 5.0f;
    public float LeadPresence { get; set; } = 5.3f;

    // Voz independiente de las escenas. Usa la entrada física 1 cuando el dispositivo ofrece más de una entrada.
    public bool VoiceEnabled { get; set; } = false;
    public bool VoiceOnlyMode { get; set; } = false;
    public bool VoiceSuppressorEnabled { get; set; } = true;
    public float VoiceThresholdDb { get; set; } = -48f;
    public float VoiceReductionDb { get; set; } = 30f;
    public float VoiceReleaseMs { get; set; } = 220f;
    public float VoiceHighPassHz { get; set; } = 90f;
    public float VoiceBassDb { get; set; } = 0f;
    public float VoiceMidDb { get; set; } = 0f;
    public float VoiceTrebleDb { get; set; } = 0f;
    public float VoiceLevelPercent { get; set; } = 100f;
    public float VoiceMonitorPercent { get; set; } = 70f;

    // Perfiles de clase accesibles. El retorno de guitarra se conserva aparte
    // porque el perfil de inglés fuerza el monitoreo local a cero.
    public float GuitarClassMonitorPercent { get; set; } = 55f;
    // El perfil Inglés apaga NAM para dejar sólo la voz. Recordamos aquí si NAM
    // estaba activo en guitarra para restaurarlo al volver al perfil Guitarra.
    public bool GuitarClassNamEnabled { get; set; } = false;
    public string LastClassProfile { get; set; } = "Personalizado";

    public float MeetGuitarPercent { get; set; } = 100f;
    public float MeetVoicePercent { get; set; } = 125f;
    public bool MeetOutputEnabled { get; set; } = false;
    public string MeetOutputDeviceId { get; set; } = string.Empty;

    // Modo Dos Guitarras (2.41.0). Guitarra 1 usa la entrada física 1 con un
    // amplificador interno independiente. Guitarra 2 conserva el rig principal
    // de Amp Accessible (incluido NAM cuando esté activo).
    public bool TwoGuitarMode { get; set; } = false;
    public bool Guitar1ProcessingEnabled { get; set; } = true;
    public bool Guitar1UseRigEffects { get; set; } = true;
    public int Guitar1AmpChannel { get; set; } = 0;
    public float Guitar1Gain { get; set; } = 3.0f;
    public float Guitar1OutputPercent { get; set; } = 50f;
    public float Guitar1MixPercent { get; set; } = 70f;
    // 2.41.30: -100 izquierda, 0 centro, +100 derecha.
    public float Guitar1PanPercent { get; set; } = 0f;
    public bool Guitar1Muted { get; set; } = false;
    // IR A independiente de Guitarra 1. Se conserva fuera de las escenas para no alterar
    // el formato histórico de escenas del rig principal.
    public string Guitar1IrPath { get; set; } = string.Empty;
    public string Guitar1IrBrowserFolder { get; set; } = string.Empty;
    public string Guitar1IrBrowserLastFilePath { get; set; } = string.Empty;

    // 2.41.21: NAM completamente independiente de Guitarra 1. La biblioteca de
    // capturas es compartida como catálogo, pero el modelo cargado y sus ajustes son
    // propios de Input 1 y no alteran el NAM del rig principal / Guitarra 2.
    public string Guitar1NamModelPath { get; set; } = string.Empty;
    public bool Guitar1NamEnabled { get; set; } = false;
    public bool Guitar1NamIncludesCabinet { get; set; } = false;
    public float Guitar1NamInputTrimDb { get; set; } = 0f;
    public float Guitar1NamOutputTrimDb { get; set; } = 0f;
    public bool Guitar1NamAutoLevelEnabled { get; set; } = false;
    public float Guitar1NamAutoLevelDb { get; set; } = 0f;

    public float Guitar2MixPercent { get; set; } = 70f;
    public float Guitar2PanPercent { get; set; } = 0f;
    public bool Guitar2Muted { get; set; } = false;

    // 2.41.10: memorias independientes de la sección Efectos. Se reutiliza ScenePreset
    // como contenedor serializable, pero sólo se aplican sus campos de efectos.
    public ScenePreset? Guitar1EffectMemory { get; set; }
    public ScenePreset? Guitar2EffectMemory { get; set; }

    // NAM se guarda como configuración global de equipo, no dentro de las escenas.
    public string NamModelPath { get; set; } = string.Empty;
    public bool NamEnabled { get; set; } = false;
    public bool NamIncludesCabinet { get; set; } = false;
    public float NamInputTrimDb { get; set; } = 0f;
    public float NamOutputTrimDb { get; set; } = 0f;

    // Metrónomo global, independiente de escenas.
    public bool MetronomeEnabled { get; set; } = false;
    public float MetronomeBpm { get; set; } = 80f;
    public int MetronomeBeatsPerBar { get; set; } = 4;
    public bool MetronomeAccentFirstBeat { get; set; } = true;
    public float MetronomeVolumePercent { get; set; } = 25f;

    // Batería de acompañamiento. Comparte el BPM del metrónomo.
    public bool DrumsEnabled { get; set; } = false;
    public int DrumPattern { get; set; } = 1;
    public float DrumVolumePercent { get; set; } = 35f;

    // Bajo de acompañamiento. Sigue el tempo y el patrón de batería.
    public bool BackingBassEnabled { get; set; } = false;
    public int BackingBassKey { get; set; } = 7; // Sol
    public bool BackingBassMinor { get; set; } = false;
    public int BackingBassLine { get; set; } = 1; // Raíz-quinta
    public float BackingBassVolumePercent { get; set; } = 28f;

    public bool PianoEnabled { get; set; } = false;
    public int PianoSound { get; set; } = 0;
    public int PianoKey { get; set; } = 7;
    public int PianoProgression { get; set; } = 0;
    public string PianoCustomProgression { get; set; } = "I, V, vi, IV";
    public int PianoStyle { get; set; } = 3;
    public float PianoVolumePercent { get; set; } = 24f;

    // Looper. La sincronización queda desactivada por defecto para conservar
    // exactamente el comportamiento de versiones anteriores.
    public bool LoopSyncTempo { get; set; } = false;
    public int LoopBars { get; set; } = 4;
    // 2.41.31: 0 = Guitarra 1, 1 = Guitarra 2, 2 = Ambas.
    // El valor 2 conserva exactamente la captura dual de versiones anteriores.
    public int LoopCaptureSource { get; set; } = 2;
}

internal static class AudioSettingsStore
{
    private static string FilePath
    {
        get
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GDM Amp Accessible");
            return Path.Combine(folder, "audio.json");
        }
    }

    public static AudioPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                AudioPreferences? settings = JsonSerializer.Deserialize<AudioPreferences>(File.ReadAllText(FilePath));
                if (settings is not null)
                {
                    Normalize(settings);
                    return settings;
                }
            }
        }
        catch { }

        return new AudioPreferences();
    }

    public static void Save(AudioPreferences settings)
    {
        Normalize(settings);

        string? folder = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static void Normalize(AudioPreferences settings)
    {
        if (settings.BufferSize is not (64 or 128 or 256 or 512))
        {
            settings.BufferSize = 256;
        }

        settings.AsioDriverName ??= string.Empty;
        settings.GuitarInputIndex = Math.Max(0, settings.GuitarInputIndex);
        settings.MasterVolumePercent = ClampFinite(settings.MasterVolumePercent, 0f, 100f, 100f);
        settings.TunerGuitarIndex = Math.Clamp(settings.TunerGuitarIndex, 0, 1);
        settings.IrBrowserFolder ??= string.Empty;
        settings.IrBrowserLastFilePath ??= string.Empty;
        if (settings.TwoGuitarMode)
        {
            settings.VoiceOnlyMode = false;
            settings.VoiceEnabled = false;
        }
        else if (settings.VoiceOnlyMode) settings.VoiceEnabled = true;

        settings.CleanBass = ClampTone(settings.CleanBass, 5.0f);
        settings.CleanMiddle = ClampTone(settings.CleanMiddle, 4.0f);
        settings.CleanTreble = ClampTone(settings.CleanTreble, 6.0f);
        settings.CleanPresence = ClampTone(settings.CleanPresence, 5.0f);

        settings.CrunchBass = ClampTone(settings.CrunchBass, 5.0f);
        settings.CrunchMiddle = ClampTone(settings.CrunchMiddle, 6.0f);
        settings.CrunchTreble = ClampTone(settings.CrunchTreble, 5.2f);
        settings.CrunchPresence = ClampTone(settings.CrunchPresence, 5.0f);

        settings.LeadBass = ClampTone(settings.LeadBass, 4.8f);
        settings.LeadMiddle = ClampTone(settings.LeadMiddle, 6.2f);
        settings.LeadTreble = ClampTone(settings.LeadTreble, 5.0f);
        settings.LeadPresence = ClampTone(settings.LeadPresence, 5.3f);

        settings.VoiceThresholdDb = ClampFinite(settings.VoiceThresholdDb, -75f, -20f, -48f);
        settings.VoiceReductionDb = ClampFinite(settings.VoiceReductionDb, 0f, 60f, 30f);
        settings.VoiceReleaseMs = ClampFinite(settings.VoiceReleaseMs, 40f, 1000f, 220f);
        settings.VoiceHighPassHz = ClampFinite(settings.VoiceHighPassHz, 50f, 180f, 90f);
        settings.VoiceBassDb = ClampFinite(settings.VoiceBassDb, -12f, 12f, 0f);
        settings.VoiceMidDb = ClampFinite(settings.VoiceMidDb, -12f, 12f, 0f);
        settings.VoiceTrebleDb = ClampFinite(settings.VoiceTrebleDb, -12f, 12f, 0f);
        settings.VoiceLevelPercent = ClampFinite(settings.VoiceLevelPercent, 0f, 150f, 100f);
        settings.VoiceMonitorPercent = ClampFinite(settings.VoiceMonitorPercent, 0f, 150f, 70f);
        settings.GuitarClassMonitorPercent = ClampFinite(settings.GuitarClassMonitorPercent, 0f, 150f, 55f);
        settings.LastClassProfile ??= "Personalizado";
        if (settings.LastClassProfile is not ("Inglés" or "Guitarra" or "Personalizado"))
            settings.LastClassProfile = "Personalizado";
        settings.MeetGuitarPercent = ClampFinite(settings.MeetGuitarPercent, 0f, 150f, 100f);
        settings.MeetVoicePercent = ClampFinite(settings.MeetVoicePercent, 0f, 150f, 125f);
        settings.MeetOutputDeviceId ??= string.Empty;
        settings.Guitar1AmpChannel = Math.Clamp(settings.Guitar1AmpChannel, 0, 8);
        settings.Guitar1Gain = ClampFinite(settings.Guitar1Gain, 0f, 10f, 3f);
        settings.Guitar1OutputPercent = ClampFinite(settings.Guitar1OutputPercent, 0f, 100f, 50f);
        settings.Guitar1MixPercent = ClampFinite(settings.Guitar1MixPercent, 0f, 100f, 70f);
        settings.Guitar1PanPercent = ClampFinite(settings.Guitar1PanPercent, -100f, 100f, 0f);
        settings.Guitar1IrPath ??= string.Empty;
        settings.Guitar1IrBrowserFolder ??= string.Empty;
        settings.Guitar1IrBrowserLastFilePath ??= string.Empty;
        settings.Guitar1NamModelPath ??= string.Empty;
        settings.Guitar1NamInputTrimDb = ClampFinite(settings.Guitar1NamInputTrimDb, -24f, 24f, 0f);
        settings.Guitar1NamOutputTrimDb = ClampFinite(settings.Guitar1NamOutputTrimDb, -24f, 24f, 0f);
        settings.Guitar1NamAutoLevelDb = ClampFinite(settings.Guitar1NamAutoLevelDb, -12f, 12f, 0f);
        settings.Guitar2MixPercent = ClampFinite(settings.Guitar2MixPercent, 0f, 100f, 70f);
        settings.Guitar2PanPercent = ClampFinite(settings.Guitar2PanPercent, -100f, 100f, 0f);
        settings.NamModelPath ??= string.Empty;
        settings.NamInputTrimDb = ClampFinite(settings.NamInputTrimDb, -24f, 24f, 0f);
        settings.NamOutputTrimDb = ClampFinite(settings.NamOutputTrimDb, -24f, 24f, 0f);
        settings.MetronomeBpm = ClampFinite(settings.MetronomeBpm, 40f, 240f, 80f);
        settings.MetronomeBeatsPerBar = Math.Clamp(settings.MetronomeBeatsPerBar, 2, 12);
        settings.MetronomeVolumePercent = ClampFinite(settings.MetronomeVolumePercent, 0f, 100f, 25f);
        settings.DrumPattern = Math.Clamp(settings.DrumPattern, 0, 5);
        settings.DrumVolumePercent = ClampFinite(settings.DrumVolumePercent, 0f, 100f, 35f);
        settings.BackingBassKey = Math.Clamp(settings.BackingBassKey, 0, 11);
        settings.BackingBassLine = Math.Clamp(settings.BackingBassLine, 0, 4);
        settings.BackingBassVolumePercent = ClampFinite(settings.BackingBassVolumePercent, 0f, 100f, 28f);
        settings.PianoSound = Math.Clamp(settings.PianoSound, 0, 5);
        settings.PianoKey = Math.Clamp(settings.PianoKey, 0, 23);
        settings.PianoProgression = Math.Clamp(settings.PianoProgression, 0, 5);
        settings.PianoCustomProgression = string.IsNullOrWhiteSpace(settings.PianoCustomProgression) ? "I, V, vi, IV" : settings.PianoCustomProgression.Trim();
        settings.PianoStyle = Math.Clamp(settings.PianoStyle, 0, 8);
        settings.PianoVolumePercent = ClampFinite(settings.PianoVolumePercent, 0f, 100f, 24f);
        settings.LoopBars = settings.LoopBars switch { 1 or 2 or 4 or 8 => settings.LoopBars, _ => 4 };
        settings.LoopCaptureSource = Math.Clamp(settings.LoopCaptureSource, 0, 2);
    }

    private static float ClampFinite(float value, float minimum, float maximum, float fallback)
    {
        if (!float.IsFinite(value)) return fallback;
        return Math.Clamp(value, minimum, maximum);
    }

    private static float ClampTone(float value, float fallback)
    {
        if (!float.IsFinite(value))
        {
            return fallback;
        }

        return Math.Clamp(value, 0f, 10f);
    }
}
