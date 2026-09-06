using System.Diagnostics;
using System.Runtime;
using GDMAmpAccessible.Dsp;
using GDMAmpAccessible.Models;
using NAudio.Wave;
using NAudio.Wave.Asio;
using NAudio.CoreAudioApi;

namespace GDMAmpAccessible.Audio;

/// <summary>
/// Motor dúplex ASIO de camino directo. Cada callback lee la entrada de guitarra y,
/// cuando está disponible, también la entrada 1 para voz; procesa ambos caminos de forma
/// independiente y escribe la mezcla estéreo. No hay cola ni hilo DSP intermedio.
/// </summary>
internal sealed class AudioEngine : IDisposable
{
    public const int SampleRate = 48000;
    private const int RealtimeBufferCapacityFrames = 4096;
    private const int SuccessfulCallbacksToCloseFaultIncident = 64;

    private const int FaultStageBuffer = 1;
    private const int FaultStageInput = 2;
    private const int FaultStageDsp = 3;
    private const int FaultStageOutput = 4;
    private const int FaultStageDriverReset = 5;

    private readonly AudioProcessor _processor = new(SampleRate);
    // 2.41.0: segundo DSP completamente separado para la guitarra de la entrada 1.
    // El rig principal continúa siendo _processor y no cambia en los perfiles existentes.
    private readonly AudioProcessor _guitar1Processor = new(SampleRate);
    // 2.41.19: acompañamiento global del modo Dos Guitarras. Se genera una sola vez
    // después de las dos cadenas para que metrónomo, batería, bajo y piano no dependan del mute
    // ni del nivel de ninguna guitarra y tampoco se dupliquen cuando ambas están activas.
    private readonly MetronomeGenerator _dualMetronome = new(SampleRate);
    private readonly DrumMachineGenerator _dualDrums = new(SampleRate);
    private readonly BassAccompanimentGenerator _dualBackingBass = new(SampleRate);
    private readonly PianoAccompanimentGenerator _dualPiano = new(SampleRate);
    private DspParameters _dualAccompanimentParameters = new();
    private int _dualAccompanimentAppliedRevision = -1;
    private int _dualAccompanimentResetRequested;
    private float _dualAccompanimentPeak;
    // 2.41.41: en Dos Guitarras, cualquiera de las dos entradas activas puede
    // disparar el bus global. Se mantiene la misma histéresis que en modo normal.
    private bool _dualAccompanimentFollowLast;
    private bool _dualAccompanimentGateActive;
    private int _dualAccompanimentAttackCounter;
    private int _dualAccompanimentSilenceCounter;
    private const int DualAccompanimentAttackRequiredSamples = 58; // ~1,2 ms a 48 kHz
    private const int DualAccompanimentSilenceHoldSamples = 86400; // 1,8 s
    private const float DualAccompanimentStartThreshold = 0.0045f;
    private const float DualAccompanimentStopThreshold = 0.0017f;
    private readonly PracticeRecorder _practiceRecorder = new(SampleRate, 2);
    private readonly PracticeLooper _practiceLooper = new(SampleRate, 2);
    private AsioOut? _asio;
    private RealtimeWaveProvider? _outputProvider;
    private float[]? _captureInput;
    private float[]? _voiceInput;
    private float[]? _guitar1Input;
    private float[]? _guitar1Output;
    private float[]? _guitar2Output;
    private float[]? _guitar1LoopCapture;
    private float[]? _guitar2LoopCapture;
    private float[]? _processedOutput;
    private float[]? _loopCaptureOutput;
    private float[]? _meetOutput;
    // Buffer con el formato real del endpoint virtual. Algunos dispositivos VoiceMeeter
    // exponen más de dos canales aunque la mezcla lógica de Amp Accessible sea estéreo.
    // En Modo Voz duplicamos la voz en todos esos canales para que ninguna aplicación
    // termine recibiéndola sólo por izquierda.
    private float[]? _meetEndpointOutput;
    private byte[]? _meetBytes;
    private int _meetEndpointChannels = 2;
    private BufferedWaveProvider? _meetProvider;
    private WasapiOut? _meetWasapi;
    private MMDevice? _meetDevice;
    private string? _meetDeviceId;
    private bool _meetEnabled;
    private int _selectedInputBufferIndex;

    // 2.41.1: medidores de diagnóstico del modo dual. Sólo observan la señal;
    // no intervienen en ganancia, mezcla ni procesamiento DSP.
    private float _guitar1RawPeak;
    private float _guitar2RawPeak;
    // 2.41.30: pico crudo de Input 1 cuando se usa como microfono.
    private float _voiceRawPeak;
    private float _guitar1ProcessedPeak;
    private float _guitar2ProcessedPeak;
    // 2.41.2: medidores posteriores a la mezcla para verificar qué aporta cada cadena
    // a Playback 1-2 sin modificar una sola muestra del audio.
    private float _guitar1MixedPeak;
    private float _guitar2MixedPeak;
    private float _dualFinalMixPeak;
    // 2.41.30: medidores L/R posteriores al paneo para comprobar fuga digital real.
    private float _guitar1PostPanLeftPeak;
    private float _guitar1PostPanRightPeak;
    private float _guitar2PostPanLeftPeak;
    private float _guitar2PostPanRightPeak;
    private float _masterLeftPeak;
    private float _masterRightPeak;
    private int _twoGuitarMode;
    private int _guitar1Muted;
    private int _guitar1ProcessingEnabled = 1;
    private int _guitar2Muted;
    // 2.41.10: volumen final global. Sólo atenúa; 100 % conserva exactamente
    // el comportamiento de las versiones anteriores.
    private float _masterVolume = 1.0f;
    private float _masterOutputPeak;
    private float _guitar1Mix = 0.70f;
    private float _guitar2Mix = 0.70f;
    // 2.41.30: paneo independiente post-DSP y pre-mezcla. -1 izquierda, 0 centro, +1 derecha.
    private float _guitar1Pan;
    private float _guitar2Pan;
    // 2.41.31: fuente que se imprime en el looper cuando Modo Dos Guitarras está activo.
    // 0 = Guitarra 1, 1 = Guitarra 2, 2 = Ambas. Fuera del modo dual se conserva
    // exactamente la captura histórica del rig principal.
    private int _loopCaptureSource = 2;
    private int _initialBufferSize;
    private int _actualBufferSize;
    private int _playbackLatencySamples;
    private int _sessionInputChannelIndex = -1;
    private int _sessionDriverInputChannels;
    private int _sessionDriverOutputChannels;
    private string? _sessionDriverName;
    private int _lastDspLoadPercent;
    private int _maxDspLoadPercent;
    private int _maxDspLoadClean;
    private int _maxDspLoadCrunch;
    private int _maxDspLoadLead;
    private int _outputUnderrunCount;
    private int _totalAudioErrorCount;
    private int _bufferErrorCount;
    private int _inputReadErrorCount;
    private int _dspErrorCount;
    private int _outputWriteErrorCount;
    private int _driverResetRequestCount;
    private int _bufferChangeCount;
    private long _lastCallbackTimestamp;
    private long _callbackCount;
    private int _consecutiveDeadlineMisses;

    // Estado del primer error de cada incidente. El callback sólo almacena referencias
    // y enteros; el texto completo y el stack se construyen luego desde el hilo de UI.
    private int _faultIncidentActive;
    private int _successfulCallbacksAfterFault;
    private int _faultNotificationPending;
    private int _faultSequence;
    private int _faultStage;
    private Exception? _faultException;
    private string? _faultSyntheticMessage;
    private int _faultFrames;
    private int _faultInputBuffers;
    private int _faultOutputBuffers;
    private int _faultSampleType;
    private int _faultBufferAtIncident;
    private int _faultSelectedInput;
    private long _faultCallbackCount;

    private GCLatencyMode _previousGcLatencyMode = GCSettings.LatencyMode;
    private bool _gcLatencyModeChanged;

    public bool IsRunning => _asio?.PlaybackState == PlaybackState.Playing;
    public bool HasActiveSession => _asio is not null;
    public AudioProcessor Processor => _processor;
    public AudioProcessor Guitar1Processor => _guitar1Processor;
    public bool TwoGuitarMode => Volatile.Read(ref _twoGuitarMode) != 0;
    public float Guitar1MixPercent => Volatile.Read(ref _guitar1Mix) * 100f;
    public float Guitar2MixPercent => Volatile.Read(ref _guitar2Mix) * 100f;
    public float Guitar1PanPercent => Volatile.Read(ref _guitar1Pan) * 100f;
    public float Guitar2PanPercent => Volatile.Read(ref _guitar2Pan) * 100f;
    public int LoopCaptureSource => Math.Clamp(Volatile.Read(ref _loopCaptureSource), 0, 2);
    public bool Guitar1Muted => Volatile.Read(ref _guitar1Muted) != 0;
    public bool Guitar1ProcessingEnabled => Volatile.Read(ref _guitar1ProcessingEnabled) != 0;
    public bool Guitar2Muted => Volatile.Read(ref _guitar2Muted) != 0;
    public float MasterVolumePercent => Volatile.Read(ref _masterVolume) * 100f;
    public float MasterOutputPeak => Volatile.Read(ref _masterOutputPeak);
    public int OutputUnderrunCount => Volatile.Read(ref _outputUnderrunCount);
    public int TotalAudioErrorCount => Volatile.Read(ref _totalAudioErrorCount);
    public int BufferErrorCount => Volatile.Read(ref _bufferErrorCount);
    public int InputReadErrorCount => Volatile.Read(ref _inputReadErrorCount);
    public int DspErrorCount => Volatile.Read(ref _dspErrorCount);
    public int OutputWriteErrorCount => Volatile.Read(ref _outputWriteErrorCount);
    public int DriverResetRequestCount => Volatile.Read(ref _driverResetRequestCount);
    public int BufferChangeCount => Volatile.Read(ref _bufferChangeCount);
    // Conservado por compatibilidad con código anterior: ahora representa errores totales.
    public int InputDropCount => TotalAudioErrorCount;
    public int InitialBufferSize => Volatile.Read(ref _initialBufferSize);
    public int ActualBufferSize => Volatile.Read(ref _actualBufferSize);
    public int PlaybackLatencySamples => Volatile.Read(ref _playbackLatencySamples);
    public string? SessionDriverName => _sessionDriverName;
    public int SessionInputChannelIndex => Volatile.Read(ref _sessionInputChannelIndex);
    public int SessionDriverInputChannels => Volatile.Read(ref _sessionDriverInputChannels);
    public int SessionDriverOutputChannels => Volatile.Read(ref _sessionDriverOutputChannels);
    public float Guitar1RawPeak => Volatile.Read(ref _guitar1RawPeak);
    public float Guitar2RawPeak => Volatile.Read(ref _guitar2RawPeak);
    public float VoiceRawPeak => Volatile.Read(ref _voiceRawPeak);
    public float Guitar1ProcessedPeak => Volatile.Read(ref _guitar1ProcessedPeak);
    public float Guitar2ProcessedPeak => Volatile.Read(ref _guitar2ProcessedPeak);
    public float Guitar1MixedPeak => Volatile.Read(ref _guitar1MixedPeak);
    public float Guitar2MixedPeak => Volatile.Read(ref _guitar2MixedPeak);
    public float DualFinalMixPeak => Volatile.Read(ref _dualFinalMixPeak);
    public float Guitar1PostPanLeftPeak => Volatile.Read(ref _guitar1PostPanLeftPeak);
    public float Guitar1PostPanRightPeak => Volatile.Read(ref _guitar1PostPanRightPeak);
    public float Guitar2PostPanLeftPeak => Volatile.Read(ref _guitar2PostPanLeftPeak);
    public float Guitar2PostPanRightPeak => Volatile.Read(ref _guitar2PostPanRightPeak);
    public float MasterLeftPeak => Volatile.Read(ref _masterLeftPeak);
    public float MasterRightPeak => Volatile.Read(ref _masterRightPeak);
    public float DualAccompanimentPeak => Volatile.Read(ref _dualAccompanimentPeak);

    public long CallbackCount => Interlocked.Read(ref _callbackCount);
    public int LastDspLoadPercent => Volatile.Read(ref _lastDspLoadPercent);
    public int MaxDspLoadPercent => Volatile.Read(ref _maxDspLoadPercent);
    public int MaxDspLoadClean => Volatile.Read(ref _maxDspLoadClean);
    public int MaxDspLoadCrunch => Volatile.Read(ref _maxDspLoadCrunch);
    public int MaxDspLoadLead => Volatile.Read(ref _maxDspLoadLead);
    public bool IsPracticeRecording => _practiceRecorder.IsRecording;
    public bool HasPendingPracticeRecording => _practiceRecorder.HasPendingRecording;
    public bool PracticeRecordingAutoCompleted => _practiceRecorder.AutoCompleted;
    public double PracticeRecordingSeconds => _practiceRecorder.RecordedSeconds;
    public double PracticeRecordingRemainingSeconds => _practiceRecorder.RemainingSeconds;
    public int PracticeRecordingDurationMinutes => _practiceRecorder.DurationMinutes;
    public bool IsLoopRecording => _practiceLooper.IsRecording;
    public bool IsLoopPlaying => _practiceLooper.IsPlaying;
    public bool IsLoopOverdubbing => _practiceLooper.IsOverdubbing;
    public bool HasLoop => _practiceLooper.HasLoop;
    public bool LoopAutoCompleted => _practiceLooper.AutoCompleted;
    public bool LoopTempoCompleted => _practiceLooper.TempoCompleted;
    public bool CanUndoLoopOverdub => _practiceLooper.CanUndoOverdub;
    public bool IsLoopTempoSyncedRecording => _practiceLooper.IsTempoSyncedRecording;
    public double LoopSeconds => _practiceLooper.LoopSeconds;
    public int MaximumLoopSeconds => _practiceLooper.MaximumLoopSeconds;
    public bool IsMeetOutputRunning => _meetWasapi?.PlaybackState == PlaybackState.Playing;
    public string? MeetDeviceId => _meetDeviceId;
    public int MeetEndpointChannels => Math.Max(1, Volatile.Read(ref _meetEndpointChannels));

    public static IReadOnlyList<MeetOutputDevice> GetMeetOutputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(device => new MeetOutputDevice(device.ID, device.FriendlyName))
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Dispositivos de captura que una aplicación de videollamada puede usar como micrófono.
    /// VoiceMeeter 2024 renombró el BUS B1 a "Voicemeeter Out B1"; instalaciones
    /// antiguas pueden seguir mostrando "Voicemeeter Output".
    /// </summary>
    public static IReadOnlyList<MeetInputDevice> GetMeetInputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(device => new MeetInputDevice(device.ID, device.FriendlyName))
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static MeetInputDevice? GetPreferredVoicemeeterB1InputDevice()
    {
        IReadOnlyList<MeetInputDevice> devices = GetMeetInputDevices();

        // Nombre actual (VoiceMeeter 2024+). Debe tener prioridad absoluta.
        MeetInputDevice? current = devices.FirstOrDefault(device =>
            device.Name.Contains("Voicemeeter Out B1", StringComparison.OrdinalIgnoreCase));
        if (current is not null) return current;

        // Nombre histórico conservado por instalaciones/actualizaciones anteriores.
        return devices.FirstOrDefault(device =>
            device.Name.Contains("Voicemeeter Output", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ruta preferida para videollamadas. VB-CABLE es un puente directo: lo que Amp
    /// Accessible escribe en CABLE Input aparece en Windows como CABLE Output.
    /// No requiere abrir ni manejar un mezclador externo.
    /// </summary>
    public static MeetInputDevice? GetPreferredVbCableInputDevice()
    {
        IReadOnlyList<MeetInputDevice> devices = GetMeetInputDevices();
        return devices.FirstOrDefault(device =>
            device.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase)
            || device.Name.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase)
               && device.Name.Contains("Output", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Loopback estéreo expuesto por Focusrite para aplicaciones no ASIO. En Scarlett
    /// 2i2 4th Gen corresponde a los canales internos 3-4 y normalmente aparece como
    /// "Loopback L + R" cuando está expuesto desde Focusrite Notifier.
    /// </summary>
    public static MeetInputDevice? GetPreferredFocusriteLoopbackInputDevice()
    {
        IReadOnlyList<MeetInputDevice> devices = GetMeetInputDevices();
        MeetInputDevice? exact = devices.FirstOrDefault(device =>
            device.Name.Contains("Loopback L + R", StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        return devices.FirstOrDefault(device =>
            device.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
            && (device.Name.Contains("Focusrite", StringComparison.OrdinalIgnoreCase)
                || device.Name.Contains("Scarlett", StringComparison.OrdinalIgnoreCase)
                || device.Name.Contains("USB Audio", StringComparison.OrdinalIgnoreCase)));
    }

    public static MeetOutputDevice? GetPreferredFocusriteRenderDevice()
    {
        return GetMeetOutputDevices().FirstOrDefault(device =>
            device.Name.Contains("Focusrite", StringComparison.OrdinalIgnoreCase)
            || device.Name.Contains("Scarlett", StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<CaptureDeviceStatus> GetCaptureDeviceStatuses()
    {
        // Algunas instalaciones Focusrite devuelven 0xE000020B al pedir DeviceState.All.
        // Enumeramos cada estado por separado y toleramos un estado defectuoso para que
        // el diagnóstico accesible pueda seguir mostrando los endpoints válidos.
        using var enumerator = new MMDeviceEnumerator();
        var found = new Dictionary<string, CaptureDeviceStatus>(StringComparer.OrdinalIgnoreCase);
        DeviceState[] states = { DeviceState.Active, DeviceState.Disabled, (DeviceState)8, (DeviceState)4 };
        foreach (DeviceState state in states)
        {
            try
            {
                foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, state))
                {
                    try
                    {
                        found[device.ID] = new CaptureDeviceStatus(device.ID, device.FriendlyName, state.ToString());
                    }
                    catch { }
                }
            }
            catch { }
        }

        static int StatePriority(string state) => state.Equals(DeviceState.Active.ToString(), StringComparison.OrdinalIgnoreCase) ? 0
            : state.Equals(DeviceState.Disabled.ToString(), StringComparison.OrdinalIgnoreCase) ? 1
            : state.Equals(((DeviceState)8).ToString(), StringComparison.OrdinalIgnoreCase) ? 2
            : 3;

        // Algunos drivers conservan endpoints antiguos con otro ID pero el mismo nombre
        // (por ejemplo Loopback L + R Active y NotPresent). Para JAWS mostramos una sola
        // entrada por nombre y priorizamos el endpoint realmente activo.
        return found.Values
            .GroupBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.OrderBy(device => StatePriority(device.State)).First())
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static bool TryGetCapturePeak(string? deviceId, out float peak)
    {
        peak = 0f;
        if (string.IsNullOrWhiteSpace(deviceId)) return false;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using MMDevice device = enumerator.GetDevice(deviceId);
            peak = Math.Clamp(device.AudioMeterInformation.MasterPeakValue, 0f, 1f);
            return true;
        }
        catch
        {
            peak = 0f;
            return false;
        }
    }

    public string ConfigureMeetOutput(string? deviceId, bool enabled)
    {
        _meetEnabled = enabled;
        _meetDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        RestartMeetOutput();
        return !_meetEnabled
            ? "Salida para videollamadas desactivada."
            : IsMeetOutputRunning
                ? "Salida para videollamadas activa. Seleccione la salida virtual correspondiente como micrófono en Zoom, Meet, Teams u otra aplicación."
                : "La salida para videollamadas quedó preparada y se iniciará junto con el audio.";
    }


    public double CallbackAgeMilliseconds
    {
        get
        {
            long last = Interlocked.Read(ref _lastCallbackTimestamp);
            if (last <= 0) return 0;
            long now = Stopwatch.GetTimestamp();
            return Math.Max(0, (now - last) * 1000.0 / Stopwatch.Frequency);
        }
    }

    /// <summary>
    /// Sólo considera detenido al motor cuando el callback ASIO dejó realmente de llegar.
    /// No se reinicia por un bloque DSP lento: eso evitaba cortes largos innecesarios.
    /// </summary>
    public bool NeedsRestart(int callbackThresholdMilliseconds = 1800, int unusedOutputThresholdMilliseconds = 0)
    {
        if (_asio is null || Interlocked.Read(ref _callbackCount) < 4)
        {
            return false;
        }

        long last = Interlocked.Read(ref _lastCallbackTimestamp);
        if (last <= 0)
        {
            return false;
        }

        long now = Stopwatch.GetTimestamp();
        double ageMilliseconds = (now - last) * 1000.0 / Stopwatch.Frequency;
        return ageMilliseconds >= callbackThresholdMilliseconds;
    }

    public static string[] GetDriverNames()
    {
        try { return AsioOut.GetDriverNames(); }
        catch { return Array.Empty<string>(); }
    }

    public static IReadOnlyList<string> GetInputChannels(string driverName)
    {
        return GetDeviceInfo(driverName).InputChannels;
    }

    public static AsioDeviceInfo GetDeviceInfo(string driverName)
    {
        using var driver = new AsioOut(driverName);
        var channels = new List<string>();
        for (int index = 0; index < driver.DriverInputChannelCount; index++)
        {
            string name = driver.AsioInputChannelName(index);
            if (string.IsNullOrWhiteSpace(name)) name = $"Entrada {index + 1}";
            channels.Add($"{index + 1}: {name}");
        }

        bool supports48Khz;
        try { supports48Khz = driver.IsSampleRateSupported(SampleRate); }
        catch { supports48Khz = false; }

        return new AsioDeviceInfo(
            driverName,
            driver.DriverInputChannelCount,
            driver.DriverOutputChannelCount,
            supports48Khz,
            channels);
    }

    private bool ShouldPlayDualFollowedAccompaniment(float guitar1Sample, float guitar2Sample, bool followGuitar)
    {
        if (followGuitar != _dualAccompanimentFollowLast)
        {
            _dualAccompanimentFollowLast = followGuitar;
            _dualAccompanimentGateActive = false;
            _dualAccompanimentAttackCounter = 0;
            _dualAccompanimentSilenceCounter = 0;
            _dualDrums.Reset();
            _dualBackingBass.Reset();
            _dualPiano.Reset();
        }

        if (!followGuitar) return true;

        float level1 = MathF.Abs(float.IsFinite(guitar1Sample) ? guitar1Sample : 0f);
        float level2 = MathF.Abs(float.IsFinite(guitar2Sample) ? guitar2Sample : 0f);
        float level = MathF.Max(level1, level2);

        if (!_dualAccompanimentGateActive)
        {
            if (level >= DualAccompanimentStartThreshold)
                _dualAccompanimentAttackCounter++;
            else if (_dualAccompanimentAttackCounter > 0)
                _dualAccompanimentAttackCounter--;

            if (_dualAccompanimentAttackCounter >= DualAccompanimentAttackRequiredSamples)
            {
                _dualAccompanimentGateActive = true;
                _dualAccompanimentAttackCounter = 0;
                _dualAccompanimentSilenceCounter = 0;
                _dualDrums.Reset();
                _dualBackingBass.Reset();
                _dualPiano.Reset();
            }
        }
        else
        {
            if (level >= DualAccompanimentStopThreshold)
            {
                _dualAccompanimentSilenceCounter = 0;
            }
            else if (++_dualAccompanimentSilenceCounter >= DualAccompanimentSilenceHoldSamples)
            {
                _dualAccompanimentGateActive = false;
                _dualAccompanimentSilenceCounter = 0;
                _dualAccompanimentAttackCounter = 0;
                _dualDrums.Reset();
                _dualBackingBass.Reset();
                _dualPiano.Reset();
            }
        }

        return _dualAccompanimentGateActive;
    }

    public void ConfigureMasterVolume(float percent)
    {
        float normalized = float.IsFinite(percent) ? Math.Clamp(percent / 100f, 0f, 1f) : 1f;
        float previous = Volatile.Read(ref _masterVolume);
        Volatile.Write(ref _masterVolume, normalized);
        if (MathF.Abs(previous - normalized) > 0.0001f)
        {
            Volatile.Write(ref _masterOutputPeak, 0f);
            Volatile.Write(ref _masterLeftPeak, 0f);
            Volatile.Write(ref _masterRightPeak, 0f);
        }
    }

    public void ConfigureDualAccompaniment(DspParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        Volatile.Write(ref _dualAccompanimentParameters, parameters);
    }

    public void RequestMetronomeReset()
    {
        // Conserva el comportamiento del perfil normal y además reinicia el bus global
        // cuando Modo Dos Guitarras está activo. El reset global se ejecuta en callback.
        _processor.RequestMetronomeReset();
        Interlocked.Exchange(ref _dualAccompanimentResetRequested, 1);
    }

    public void ConfigureTwoGuitarMode(bool enabled, bool guitar1ProcessingEnabled, float guitar1MixPercent, float guitar1PanPercent, bool guitar1Muted,
        float guitar2MixPercent, float guitar2PanPercent, bool guitar2Muted)
    {
        Volatile.Write(ref _guitar1ProcessingEnabled, guitar1ProcessingEnabled ? 1 : 0);
        Volatile.Write(ref _guitar1Mix, Math.Clamp(guitar1MixPercent / 100f, 0f, 1f));
        Volatile.Write(ref _guitar2Mix, Math.Clamp(guitar2MixPercent / 100f, 0f, 1f));
        float nextG1Pan = Math.Clamp(guitar1PanPercent / 100f, -1f, 1f);
        float nextG2Pan = Math.Clamp(guitar2PanPercent / 100f, -1f, 1f);
        float previousG1Pan = Volatile.Read(ref _guitar1Pan);
        float previousG2Pan = Volatile.Read(ref _guitar2Pan);
        Volatile.Write(ref _guitar1Pan, nextG1Pan);
        Volatile.Write(ref _guitar2Pan, nextG2Pan);
        if (MathF.Abs(previousG1Pan - nextG1Pan) > 0.0001f)
        {
            Volatile.Write(ref _guitar1PostPanLeftPeak, 0f);
            Volatile.Write(ref _guitar1PostPanRightPeak, 0f);
            Volatile.Write(ref _masterLeftPeak, 0f);
            Volatile.Write(ref _masterRightPeak, 0f);
        }
        if (MathF.Abs(previousG2Pan - nextG2Pan) > 0.0001f)
        {
            Volatile.Write(ref _guitar2PostPanLeftPeak, 0f);
            Volatile.Write(ref _guitar2PostPanRightPeak, 0f);
            Volatile.Write(ref _masterLeftPeak, 0f);
            Volatile.Write(ref _masterRightPeak, 0f);
        }
        Volatile.Write(ref _guitar1Muted, guitar1Muted ? 1 : 0);
        Volatile.Write(ref _guitar2Muted, guitar2Muted ? 1 : 0);
        Volatile.Write(ref _twoGuitarMode, enabled ? 1 : 0);
    }

    public string Start(string driverName, int inputChannelIndex, int requestedBufferSize)
    {
        Stop();
        Volatile.Write(ref _guitar1RawPeak, 0f);
        Volatile.Write(ref _guitar2RawPeak, 0f);
        Volatile.Write(ref _guitar1ProcessedPeak, 0f);
        Volatile.Write(ref _guitar2ProcessedPeak, 0f);
        Volatile.Write(ref _guitar1MixedPeak, 0f);
        Volatile.Write(ref _guitar2MixedPeak, 0f);
        Volatile.Write(ref _dualFinalMixPeak, 0f);
        Volatile.Write(ref _guitar1PostPanLeftPeak, 0f);
        Volatile.Write(ref _guitar1PostPanRightPeak, 0f);
        Volatile.Write(ref _guitar2PostPanLeftPeak, 0f);
        Volatile.Write(ref _guitar2PostPanRightPeak, 0f);
        Volatile.Write(ref _masterLeftPeak, 0f);
        Volatile.Write(ref _masterRightPeak, 0f);
        Volatile.Write(ref _dualAccompanimentPeak, 0f);
        Volatile.Write(ref _masterOutputPeak, 0f);
        _dualMetronome.Reset();
        _dualDrums.Reset();
        _dualBackingBass.Reset();
        _dualPiano.Reset();
        _dualAccompanimentAppliedRevision = -1;

        if (requestedBufferSize is not (64 or 128 or 256 or 512))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedBufferSize),
                "El buffer debe ser 64, 128, 256 o 512 muestras.");
        }

        EnterRealtimeGcMode();

        var asio = new AsioOut(driverName)
        {
            // Se crean desde la primera entrada y se elige explícitamente el buffer físico
            // en el callback. Así la entrada 1 nunca se suma a la 2 ni viceversa.
            InputChannelOffset = 0,
            ChannelOffset = 0,
            AutoStop = false
        };

        if (asio.DriverOutputChannelCount < 2)
        {
            asio.Dispose();
            throw new InvalidOperationException("Amp Accessible necesita al menos 2 salidas ASIO para la mezcla estéreo.");
        }

        if (!asio.IsSampleRateSupported(SampleRate))
        {
            asio.Dispose();
            throw new InvalidOperationException("El controlador ASIO no admite 48 kHz.");
        }

        if (inputChannelIndex < 0 || inputChannelIndex >= asio.DriverInputChannelCount)
        {
            asio.Dispose();
            throw new ArgumentOutOfRangeException(nameof(inputChannelIndex),
                "La entrada seleccionada no existe en este controlador ASIO.");
        }

        bool twoGuitars = TwoGuitarMode;
        if (twoGuitars && asio.DriverInputChannelCount < 2)
        {
            asio.Dispose();
            throw new InvalidOperationException("Modo Dos Guitarras necesita una interfaz ASIO con al menos 2 entradas.");
        }

        // Se solicitan las dos primeras entradas cuando el dispositivo las ofrece.
        // Así la entrada 1 puede procesarse como micrófono mientras la guitarra usa la 2.
        // En interfaces de una sola entrada se conserva el comportamiento anterior.
        int recordChannelCount = twoGuitars
            ? 2
            : Math.Max(inputChannelIndex + 1, Math.Min(2, asio.DriverInputChannelCount));
        var outputProvider = new RealtimeWaveProvider(SampleRate, 2);
        outputProvider.SetSilence(RealtimeBufferCapacityFrames);

        asio.AudioAvailable += OnAudioAvailable;
        asio.DriverResetRequest += OnDriverResetRequest;
        asio.InitRecordAndPlayback(outputProvider, recordChannelCount, SampleRate);

        int frames = asio.FramesPerBuffer;
        _sessionDriverName = driverName;
        Volatile.Write(ref _sessionInputChannelIndex, twoGuitars ? 1 : inputChannelIndex);
        Volatile.Write(ref _sessionDriverInputChannels, asio.DriverInputChannelCount);
        Volatile.Write(ref _sessionDriverOutputChannels, asio.DriverOutputChannelCount);
        Volatile.Write(ref _playbackLatencySamples, asio.PlaybackLatency);
        int capacity = Math.Max(frames, RealtimeBufferCapacityFrames);
        _captureInput = new float[capacity];
        _voiceInput = new float[capacity];
        _guitar1Input = new float[capacity];
        _guitar1Output = new float[capacity * 2];
        _guitar2Output = new float[capacity * 2];
        _guitar1LoopCapture = new float[capacity * 2];
        _guitar2LoopCapture = new float[capacity * 2];
        _processedOutput = new float[capacity * 2];
        _loopCaptureOutput = new float[capacity * 2];
        _meetOutput = new float[capacity * 2];
        // Reservamos hasta 8 canales para endpoints virtuales multicanal (VoiceMeeter).
        // Esto se hace fuera del callback ASIO para no asignar memoria en tiempo real.
        _meetEndpointOutput = new float[capacity * 8];
        _meetBytes = new byte[capacity * 8 * sizeof(float)];
        _selectedInputBufferIndex = twoGuitars ? 1 : inputChannelIndex;
        _outputProvider = outputProvider;
        _asio = asio;
        _processor.Reset();
        Volatile.Write(ref _voiceRawPeak, 0f);
        if (twoGuitars) _guitar1Processor.Reset();

        // Precalentamiento antes de Play: los canales y efectos recorren su código
        // una vez fuera del callback ASIO, evitando el tirón de primera utilización.
        _processor.WarmUp(Math.Max(frames, 256));
        if (twoGuitars) _guitar1Processor.WarmUp(Math.Max(frames, 256));

        // 2.40.19: WarmUp crea objetos temporales y fuerza el JIT de las rutas DSP.
        // Los retiramos antes de entregar el hilo al driver ASIO para reducir la
        // probabilidad de una pausa de GC aislada durante los primeros callbacks.
        CollectAfterWarmUp();

        long initialTimestamp = Stopwatch.GetTimestamp();
        Interlocked.Exchange(ref _initialBufferSize, frames);
        Interlocked.Exchange(ref _actualBufferSize, frames);
        Interlocked.Exchange(ref _callbackCount, 0);
        Interlocked.Exchange(ref _lastCallbackTimestamp, initialTimestamp);
        Interlocked.Exchange(ref _outputUnderrunCount, 0);
        Interlocked.Exchange(ref _totalAudioErrorCount, 0);
        Interlocked.Exchange(ref _bufferErrorCount, 0);
        Interlocked.Exchange(ref _inputReadErrorCount, 0);
        Interlocked.Exchange(ref _dspErrorCount, 0);
        Interlocked.Exchange(ref _outputWriteErrorCount, 0);
        Interlocked.Exchange(ref _driverResetRequestCount, 0);
        Interlocked.Exchange(ref _bufferChangeCount, 0);
        Interlocked.Exchange(ref _lastDspLoadPercent, 0);
        Interlocked.Exchange(ref _maxDspLoadPercent, 0);
        Interlocked.Exchange(ref _maxDspLoadClean, 0);
        Interlocked.Exchange(ref _maxDspLoadCrunch, 0);
        Interlocked.Exchange(ref _maxDspLoadLead, 0);
        Interlocked.Exchange(ref _consecutiveDeadlineMisses, 0);
        ResetFaultIncidentState();

        asio.Play();
        RestartMeetOutput();

        double bufferMilliseconds = frames * 1000.0 / SampleRate;
        double playbackMilliseconds = asio.PlaybackLatency * 1000.0 / SampleRate;
        string bufferNotice = frames switch
        {
            64 => " Modo exigente: con varios efectos puede no haber margen suficiente.",
            128 => " Modo equilibrado.",
            256 => " Modo de estabilidad recomendado para usar varios efectos.",
            _ => " Modo de estabilidad máxima; agrega latencia pero ofrece el mayor margen."
        };
        string requestedNotice = frames == requestedBufferSize
            ? string.Empty
            : $" La referencia era {requestedBufferSize}, pero el controlador está trabajando en {frames}. El cambio real se hace en el panel ASIO de la interfaz seleccionada.";

        string routeNotice = twoGuitars
            ? "Modo Dos Guitarras activo: Guitarra 1 usa Input 1 con DSP y NAM independientes; Guitarra 2 usa Input 2 con el rig principal y su propio NAM. Las dos instancias NAM pueden procesarse simultáneamente. La cadena de voz queda desactivada en este modo."
            : $"Entrada de guitarra: {inputChannelIndex + 1}. La entrada 1 queda disponible para la cadena de voz cuando la guitarra usa otra entrada.";
        return $"DSP precalentado. Audio iniciado por camino ASIO directo. {routeNotice} Buffer efectivo del driver: {frames} muestras, {bufferMilliseconds:0.0} ms. " +
               $"Latencia de salida informada: {playbackMilliseconds:0.0} ms.{bufferNotice}{requestedNotice}";
    }

    public void Stop()
    {
        AsioOut? asio = _asio;
        _asio = null;
        if (asio is not null)
        {
            try
            {
                asio.AudioAvailable -= OnAudioAvailable;
                asio.DriverResetRequest -= OnDriverResetRequest;
                if (asio.PlaybackState != PlaybackState.Stopped) asio.Stop();
            }
            catch { }
            finally { asio.Dispose(); }
        }

        _outputProvider = null;
        _captureInput = null;
        _voiceInput = null;
        _guitar1Input = null;
        _guitar1Output = null;
        _guitar2Output = null;
        _guitar1LoopCapture = null;
        _guitar2LoopCapture = null;
        _processedOutput = null;
        _loopCaptureOutput = null;
        _meetOutput = null;
        _meetEndpointOutput = null;
        _meetBytes = null;
        Volatile.Write(ref _meetEndpointChannels, 2);
        StopMeetOutput();
        LeaveRealtimeGcMode();
        Interlocked.Exchange(ref _initialBufferSize, 0);
        Interlocked.Exchange(ref _actualBufferSize, 0);
        Interlocked.Exchange(ref _lastDspLoadPercent, 0);
        Interlocked.Exchange(ref _consecutiveDeadlineMisses, 0);
        ResetFaultIncidentState();
    }

    public string StartPracticeRecording(int minutes)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("Inicie primero el audio con F4 antes de grabar.");
        }
        return _practiceRecorder.Start(minutes);
    }

    public Task<string?> StopAndSavePracticeRecordingAsync()
    {
        return _practiceRecorder.StopAndSaveAsync();
    }

    public void CancelPracticeRecording()
    {
        _practiceRecorder.Cancel();
    }

    public void ConfigureLoopCaptureSource(int source)
    {
        Volatile.Write(ref _loopCaptureSource, Math.Clamp(source, 0, 2));
    }

    public void StartLoopRecording()
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("Inicie primero el audio con F4 antes de grabar un loop.");
        }
        _practiceLooper.StartFirstPass();
    }

    public double StartLoopRecordingSynced(float bpm, int beatsPerBar, int bars)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("Inicie primero el audio con F4 antes de grabar un loop.");
        }
        return _practiceLooper.StartFirstPassSynced(bpm, beatsPerBar, bars);
    }

    public bool FinishLoopRecordingAndPlay() => _practiceLooper.FinishFirstPassAndPlay();

    public bool ToggleLoopPlayback() => _practiceLooper.TogglePlayback();

    public bool ToggleLoopOverdub() => _practiceLooper.ToggleOverdub();

    public bool UndoLastLoopOverdub() => _practiceLooper.UndoLastOverdub();

    public void ClearLoop() => _practiceLooper.Clear();

    public Task<string?> SaveLoopAsync() => _practiceLooper.SaveAsync();

    public static void ShowControlPanel(string driverName)
    {
        using var driver = new AsioOut(driverName);
        driver.ShowControlPanel();
    }

    /// <summary>
    /// Devuelve sólo la primera falla de cada incidente continuo. El detalle se construye
    /// aquí, fuera del callback ASIO, e incluye etapa, excepción, tamaños y stack trace.
    /// </summary>
    public AudioFaultReport? ConsumeAudioFault()
    {
        if (Interlocked.Exchange(ref _faultNotificationPending, 0) == 0)
        {
            return null;
        }

        int sequence = Volatile.Read(ref _faultSequence);
        int stage = Volatile.Read(ref _faultStage);
        Exception? exception = _faultException;
        string? syntheticMessage = _faultSyntheticMessage;
        int frames = Volatile.Read(ref _faultFrames);
        int inputBuffers = Volatile.Read(ref _faultInputBuffers);
        int outputBuffers = Volatile.Read(ref _faultOutputBuffers);
        int sampleTypeValue = Volatile.Read(ref _faultSampleType);
        int bufferAtIncident = Volatile.Read(ref _faultBufferAtIncident);
        int selectedInput = Volatile.Read(ref _faultSelectedInput);
        long callback = Interlocked.Read(ref _faultCallbackCount);

        string stageName = GetFaultStageName(stage);
        string exceptionType = exception?.GetType().FullName ?? "Sin excepción administrada";
        string exceptionText = exception?.ToString() ?? syntheticMessage ?? "Sin detalle adicional";
        string sampleType = sampleTypeValue < 0 ? "n/d" : ((AsioSampleType)sampleTypeValue).ToString();

        string userMessage = stage == FaultStageDriverReset
            ? "La interfaz seleccionada solicitó un reinicio del controlador ASIO. Se guardó un diagnóstico único del incidente. Si el audio continúa mal, pulse F4 para detener y F4 nuevamente para iniciar."
            : $"Se detectó una falla en {stageName}. Se aisló el bloque afectado y se guardó solamente el primer error de este incidente.";

        string detail =
            $"Incidente #{sequence}\r\n" +
            $"Etapa: {stageName}\r\n" +
            $"Tipo de excepción: {exceptionType}\r\n" +
            $"Callback: {callback}\r\n" +
            $"Frames del callback: {frames}\r\n" +
            $"Buffer inicial: {InitialBufferSize}\r\n" +
            $"Buffer observado al fallar: {bufferAtIncident}\r\n" +
            $"Buffers de entrada entregados: {inputBuffers}\r\n" +
            $"Buffers de salida entregados: {outputBuffers}\r\n" +
            $"Entrada física seleccionada: {selectedInput + 1}\r\n" +
            $"Formato ASIO: {sampleType}\r\n" +
            $"Cambios de buffer detectados: {BufferChangeCount}\r\n" +
            $"Solicitudes de reset del driver: {DriverResetRequestCount}\r\n" +
            $"Errores acumulados: total={TotalAudioErrorCount}; buffer={BufferErrorCount}; entrada={InputReadErrorCount}; DSP={DspErrorCount}; salida={OutputWriteErrorCount}\r\n" +
            $"Excepción / detalle:\r\n{exceptionText}";

        return new AudioFaultReport(sequence, userMessage, detail);
    }

    private void RestartMeetOutput()
    {
        StopMeetOutput();
        if (!_meetEnabled || !IsRunning || string.IsNullOrWhiteSpace(_meetDeviceId))
            return;

        try
        {
            var enumerator = new MMDeviceEnumerator();
            _meetDevice = enumerator.GetDevice(_meetDeviceId);

            int endpointChannels = 2;
            try
            {
                // VoiceMeeter puede exponer su entrada virtual como multicanal.
                // Usamos ese número real en lugar de forzar siempre 2 canales.
                endpointChannels = Math.Clamp(_meetDevice.AudioClient.MixFormat.Channels, 1, 8);
            }
            catch
            {
                endpointChannels = 2;
            }
            Volatile.Write(ref _meetEndpointChannels, endpointChannels);

            _meetProvider = new BufferedWaveProvider(
                WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, endpointChannels))
            {
                BufferDuration = TimeSpan.FromMilliseconds(250),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
            _meetWasapi = new WasapiOut(_meetDevice, AudioClientShareMode.Shared, false, 80);
            _meetWasapi.Init(_meetProvider);
            _meetWasapi.Play();
        }
        catch
        {
            StopMeetOutput();
        }
    }

    private void StopMeetOutput()
    {
        try
        {
            if (_meetWasapi is not null && _meetWasapi.PlaybackState != PlaybackState.Stopped)
                _meetWasapi.Stop();
        }
        catch { }
        try { _meetWasapi?.Dispose(); } catch { }
        try { _meetDevice?.Dispose(); } catch { }
        _meetWasapi = null;
        _meetProvider = null;
        _meetDevice = null;
    }

    private void OnDriverResetRequest(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _driverResetRequestCount);
        RegisterSyntheticFault(
            FaultStageDriverReset,
            ActualBufferSize,
            0,
            0,
            -1,
            "El controlador ASIO emitió DriverResetRequest.");
    }

    private void OnAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
    {
        long callbackStart = Stopwatch.GetTimestamp();
        Interlocked.Exchange(ref _lastCallbackTimestamp, callbackStart);
        Interlocked.Increment(ref _callbackCount);

        int frames = e.SamplesPerBuffer;
        int previousFrames = Interlocked.Exchange(ref _actualBufferSize, frames);
        if (previousFrames > 0 && previousFrames != frames)
        {
            Interlocked.Increment(ref _bufferChangeCount);
        }

        float[]? input = _captureInput;
        float[]? voiceInput = _voiceInput;
        float[]? guitar1Input = _guitar1Input;
        float[]? guitar1Output = _guitar1Output;
        float[]? guitar2Output = _guitar2Output;
        float[]? guitar1LoopCapture = _guitar1LoopCapture;
        float[]? guitar2LoopCapture = _guitar2LoopCapture;
        float[]? output = _processedOutput;
        float[]? loopCaptureOutput = _loopCaptureOutput;
        float[]? meetOutput = _meetOutput;
        float[]? meetEndpointOutput = _meetEndpointOutput;
        byte[]? meetBytes = _meetBytes;
        if (input is null || voiceInput is null || guitar1Input is null || guitar1Output is null ||
            guitar2Output is null || guitar1LoopCapture is null || guitar2LoopCapture is null ||
            output is null || loopCaptureOutput is null || meetOutput is null || meetEndpointOutput is null || meetBytes is null)
        {
            TryClearOutputs(e);
            return;
        }

        try
        {
            if (frames <= 0 || frames > input.Length || frames > voiceInput.Length || frames > guitar1Input.Length ||
                frames * 2 > output.Length || frames * 2 > loopCaptureOutput.Length ||
                frames * 2 > guitar1Output.Length || frames * 2 > guitar2Output.Length)
            {
                Interlocked.Increment(ref _totalAudioErrorCount);
                Interlocked.Increment(ref _bufferErrorCount);
                RegisterSyntheticFault(
                    FaultStageBuffer,
                    frames,
                    e.InputBuffers.Length,
                    e.OutputBuffers.Length,
                    (int)e.AsioSampleType,
                    "El tamaño del callback excedió la capacidad preventiva reservada. No se asigna memoria dentro del callback.");
                TryClearOutputs(e);
                return;
            }

            try
            {
                if (TwoGuitarMode)
                {
                    // Ruta dual fija y predecible: Input 1 = Guitarra 1, Input 2 = Guitarra 2.
                    AsioInputReader.ReadMono(e, 0, guitar1Input, frames);
                    AsioInputReader.ReadMono(e, 1, input, frames);
                    UpdateHeldPeak(ref _guitar1RawPeak, MeasurePeak(guitar1Input, frames));
                    UpdateHeldPeak(ref _guitar2RawPeak, MeasurePeak(input, frames));
                    Array.Clear(voiceInput, 0, frames);
                }
                else
                {
                    AsioInputReader.ReadMono(e, _selectedInputBufferIndex, input, frames);
                    // El canal 1 es el micrófono. Sólo se mezcla cuando la guitarra está en
                    // otra entrada; así nunca duplicamos una guitarra conectada al canal 1.
                    if ((_processor.VoiceOnlyMode || _selectedInputBufferIndex != 0) && e.InputBuffers.Length > 0)
                    {
                        AsioInputReader.ReadMono(e, 0, voiceInput, frames);
                        UpdateHeldPeak(ref _voiceRawPeak, MeasurePeak(voiceInput, frames));
                    }
                    else
                    {
                        Array.Clear(voiceInput, 0, frames);
                    }
                    Volatile.Write(ref _guitar1RawPeak, 0f);
                    Volatile.Write(ref _guitar2RawPeak, 0f);
                    Volatile.Write(ref _guitar1ProcessedPeak, 0f);
                    Volatile.Write(ref _guitar2ProcessedPeak, 0f);
                }
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref _totalAudioErrorCount);
                Interlocked.Increment(ref _inputReadErrorCount);
                RegisterExceptionFault(FaultStageInput, exception, e, frames);
                TryClearOutputs(e);
                return;
            }

            try
            {
                if (TwoGuitarMode)
                {
                    DspParameters accompanimentParameters = Volatile.Read(ref _dualAccompanimentParameters);
                    if (accompanimentParameters.Revision != _dualAccompanimentAppliedRevision)
                    {
                        _dualMetronome.Configure(accompanimentParameters.MetronomeEnabled, accompanimentParameters.MetronomeBpm,
                            accompanimentParameters.MetronomeBeatsPerBar, accompanimentParameters.MetronomeAccentFirstBeat,
                            accompanimentParameters.MetronomeVolumePercent);
                        _dualDrums.Configure(accompanimentParameters.DrumsEnabled, accompanimentParameters.MetronomeBpm,
                            accompanimentParameters.DrumPattern, accompanimentParameters.DrumVolumePercent);
                        _dualBackingBass.Configure(accompanimentParameters.BackingBassEnabled, accompanimentParameters.MetronomeBpm,
                            accompanimentParameters.DrumPattern, accompanimentParameters.BackingBassVolumePercent,
                            accompanimentParameters.BackingBassKey, accompanimentParameters.BackingBassMinor,
                            accompanimentParameters.BackingBassLine);
                        _dualPiano.Configure(accompanimentParameters.PianoEnabled, accompanimentParameters.MetronomeBpm,
                            accompanimentParameters.MetronomeBeatsPerBar, accompanimentParameters.DrumPattern,
                            accompanimentParameters.PianoVolumePercent, accompanimentParameters.PianoKey, accompanimentParameters.PianoSound,
                            accompanimentParameters.PianoProgression, accompanimentParameters.PianoStyle,
                            accompanimentParameters.PianoCustomProgressionPack1, accompanimentParameters.PianoCustomProgressionPack2,
                            accompanimentParameters.PianoCustomProgressionPack3, accompanimentParameters.PianoCustomProgressionPack4,
                            accompanimentParameters.PianoCustomProgressionPack5, accompanimentParameters.PianoCustomProgressionPack6,
                            accompanimentParameters.PianoCustomProgressionPack7, accompanimentParameters.PianoCustomProgressionPack8,
                            accompanimentParameters.PianoCustomProgressionCount, accompanimentParameters.PianoCustomProgressionSequence,
                            accompanimentParameters.PianoCustomProgressionHash);
                        _dualAccompanimentAppliedRevision = accompanimentParameters.Revision;
                    }
                    if (Interlocked.Exchange(ref _dualAccompanimentResetRequested, 0) != 0)
                    {
                        _dualMetronome.Reset();
                        _dualDrums.Reset();
                        _dualBackingBass.Reset();
                        _dualPiano.Reset();
                    }

                    // Guitarra 2 conserva exactamente el rig principal (incluido NAM).
                    // Guitarra 1 usa un segundo AudioProcessor con su propio ampli interno.
                    _processor.ProcessMonoToStereo(input, null, guitar2Output, null, guitar2LoopCapture, frames);

                    if (Guitar1ProcessingEnabled)
                    {
                        // 2.41.4: Input 1 es instrumento dedicado. Se fuerza 100 % DSP
                        // para que no pueda quedar ninguna mezcla seca heredada del antiguo
                        // uso de Input 1 como micrófono/entrada compartida.
                        _guitar1Processor.ForceSimulationFullyOn();
                        _guitar1Processor.ProcessMonoToStereo(guitar1Input, null, guitar1Output, null, guitar1LoopCapture, frames);
                    }
                    else
                    {
                        // Bypass explícito de Guitarra 1 sólo cuando el usuario lo pide.
                        // Esto permite comprobar auditivamente la diferencia entre crudo y DSP.
                        for (int frame = 0; frame < frames; frame++)
                        {
                            float dry = float.IsFinite(guitar1Input[frame]) ? guitar1Input[frame] : 0f;
                            int oi = frame * 2;
                            guitar1Output[oi] = dry;
                            guitar1Output[oi + 1] = dry;
                            guitar1LoopCapture[oi] = dry;
                            guitar1LoopCapture[oi + 1] = dry;
                        }
                    }

                    bool g1Muted = Guitar1Muted;
                    bool g2Muted = Guitar2Muted;
                    float g1 = g1Muted ? 0f : Volatile.Read(ref _guitar1Mix);
                    float g2 = g2Muted ? 0f : Volatile.Read(ref _guitar2Mix);
                    bool g1CanTriggerAccompaniment = !g1Muted && g1 > 0.0001f;
                    bool g2CanTriggerAccompaniment = !g2Muted && g2 > 0.0001f;
                    float g1Pan = Volatile.Read(ref _guitar1Pan);
                    float g2Pan = Volatile.Read(ref _guitar2Pan);
                    bool copyMeet = _meetEnabled && _meetProvider is not null;
                    float processedPeak1 = 0f;
                    float processedPeak2 = 0f;
                    float finalMixPeak = 0f;
                    float accompanimentPeak = 0f;
                    float g1PostPanLeftPeak = 0f;
                    float g1PostPanRightPeak = 0f;
                    float g2PostPanLeftPeak = 0f;
                    float g2PostPanRightPeak = 0f;

                    // 2.41.30: con ambos paneos en Centro se conserva literalmente el camino
                    // de mezcla de 2.41.26. Así no cambia ni una muestra ni la carga DSP de la
                    // configuración histórica. Sólo se entra al bloque nuevo cuando algún paneo
                    // se mueve fuera de cero.
                    bool panIsCentered = MathF.Abs(g1Pan) <= 0.000001f && MathF.Abs(g2Pan) <= 0.000001f;
                    int loopSource = Volatile.Read(ref _loopCaptureSource);
                    if (panIsCentered)
                    {
                        int samples = frames * 2;
                        float accompanimentSample = 0f;
                        for (int i = 0; i < samples; i++)
                        {
                            float abs1 = MathF.Abs(guitar1Output[i]);
                            float abs2 = MathF.Abs(guitar2Output[i]);
                            if (abs1 > processedPeak1) processedPeak1 = abs1;
                            if (abs2 > processedPeak2) processedPeak2 = abs2;

                            if ((i & 1) == 0)
                            {
                                int frameIndex = i >> 1;
                                bool bandActive = ShouldPlayDualFollowedAccompaniment(
                                    g1CanTriggerAccompaniment ? guitar1Input[frameIndex] : 0f,
                                    g2CanTriggerAccompaniment ? input[frameIndex] : 0f,
                                    accompanimentParameters.AccompanimentFollowGuitar);
                                float band = bandActive ? _dualDrums.Process() + _dualBackingBass.Process() + _dualPiano.Process() : 0f;
                                accompanimentSample = _dualMetronome.Process() + band;
                                accompanimentSample = Math.Clamp(float.IsFinite(accompanimentSample) ? accompanimentSample : 0f, -0.95f, 0.95f);
                                float absAccompaniment = MathF.Abs(accompanimentSample);
                                if (absAccompaniment > accompanimentPeak) accompanimentPeak = absAccompaniment;
                            }

                            float g1Contribution = guitar1Output[i] * g1;
                            float g2Contribution = guitar2Output[i] * g2;
                            if ((i & 1) == 0)
                            {
                                g1PostPanLeftPeak = MathF.Max(g1PostPanLeftPeak, MathF.Abs(g1Contribution));
                                g2PostPanLeftPeak = MathF.Max(g2PostPanLeftPeak, MathF.Abs(g2Contribution));
                            }
                            else
                            {
                                g1PostPanRightPeak = MathF.Max(g1PostPanRightPeak, MathF.Abs(g1Contribution));
                                g2PostPanRightPeak = MathF.Max(g2PostPanRightPeak, MathF.Abs(g2Contribution));
                            }

                            float mixed = MixLimiter(g1Contribution + g2Contribution + accompanimentSample);
                            output[i] = mixed;
                            float absMixed = MathF.Abs(mixed);
                            if (absMixed > finalMixPeak) finalMixPeak = absMixed;

                            float loopG1 = loopSource == 1 ? 0f : guitar1LoopCapture[i] * g1;
                            float loopG2 = loopSource == 0 ? 0f : guitar2LoopCapture[i] * g2;
                            loopCaptureOutput[i] = MixLimiter(loopG1 + loopG2);
                            if (copyMeet) meetOutput[i] = output[i];
                        }

                        UpdateHeldPeak(ref _guitar1MixedPeak, processedPeak1 * g1);
                        UpdateHeldPeak(ref _guitar2MixedPeak, processedPeak2 * g2);
                    }
                    else
                    {
                        float mixedPeak1 = 0f;
                        float mixedPeak2 = 0f;

                        // El paneo se aplica a cada cadena ya procesada, justo antes de sumarlas.
                        // Al acercarse a un extremo la imagen se desplaza y se cierra hacia ese lado
                        // sin descartar directamente el canal opuesto.
                        for (int frame = 0; frame < frames; frame++)
                        {
                            int leftIndex = frame * 2;
                            int rightIndex = leftIndex + 1;

                            float g1Left = guitar1Output[leftIndex];
                            float g1Right = guitar1Output[rightIndex];
                            float g2Left = guitar2Output[leftIndex];
                            float g2Right = guitar2Output[rightIndex];

                            float abs1 = MathF.Max(MathF.Abs(g1Left), MathF.Abs(g1Right));
                            float abs2 = MathF.Max(MathF.Abs(g2Left), MathF.Abs(g2Right));
                            if (abs1 > processedPeak1) processedPeak1 = abs1;
                            if (abs2 > processedPeak2) processedPeak2 = abs2;

                            ApplyStereoPan(g1Left, g1Right, g1Pan, out float g1PannedLeft, out float g1PannedRight);
                            ApplyStereoPan(g2Left, g2Right, g2Pan, out float g2PannedLeft, out float g2PannedRight);
                            ApplyStereoPan(guitar1LoopCapture[leftIndex], guitar1LoopCapture[rightIndex], g1Pan,
                                out float g1LoopLeft, out float g1LoopRight);
                            ApplyStereoPan(guitar2LoopCapture[leftIndex], guitar2LoopCapture[rightIndex], g2Pan,
                                out float g2LoopLeft, out float g2LoopRight);

                            float g1ContributionLeft = g1PannedLeft * g1;
                            float g1ContributionRight = g1PannedRight * g1;
                            float g2ContributionLeft = g2PannedLeft * g2;
                            float g2ContributionRight = g2PannedRight * g2;
                            mixedPeak1 = MathF.Max(mixedPeak1, MathF.Max(MathF.Abs(g1ContributionLeft), MathF.Abs(g1ContributionRight)));
                            mixedPeak2 = MathF.Max(mixedPeak2, MathF.Max(MathF.Abs(g2ContributionLeft), MathF.Abs(g2ContributionRight)));
                            g1PostPanLeftPeak = MathF.Max(g1PostPanLeftPeak, MathF.Abs(g1ContributionLeft));
                            g1PostPanRightPeak = MathF.Max(g1PostPanRightPeak, MathF.Abs(g1ContributionRight));
                            g2PostPanLeftPeak = MathF.Max(g2PostPanLeftPeak, MathF.Abs(g2ContributionLeft));
                            g2PostPanRightPeak = MathF.Max(g2PostPanRightPeak, MathF.Abs(g2ContributionRight));

                            bool bandActive = ShouldPlayDualFollowedAccompaniment(
                                g1CanTriggerAccompaniment ? guitar1Input[frame] : 0f,
                                g2CanTriggerAccompaniment ? input[frame] : 0f,
                                accompanimentParameters.AccompanimentFollowGuitar);
                            float band = bandActive ? _dualDrums.Process() + _dualBackingBass.Process() + _dualPiano.Process() : 0f;
                            float accompanimentSample = _dualMetronome.Process() + band;
                            accompanimentSample = Math.Clamp(float.IsFinite(accompanimentSample) ? accompanimentSample : 0f, -0.95f, 0.95f);
                            accompanimentPeak = MathF.Max(accompanimentPeak, MathF.Abs(accompanimentSample));

                            float mixedLeft = MixLimiter(g1ContributionLeft + g2ContributionLeft + accompanimentSample);
                            float mixedRight = MixLimiter(g1ContributionRight + g2ContributionRight + accompanimentSample);
                            output[leftIndex] = mixedLeft;
                            output[rightIndex] = mixedRight;
                            finalMixPeak = MathF.Max(finalMixPeak, MathF.Max(MathF.Abs(mixedLeft), MathF.Abs(mixedRight)));

                            float loopG1Left = loopSource == 1 ? 0f : g1LoopLeft * g1;
                            float loopG1Right = loopSource == 1 ? 0f : g1LoopRight * g1;
                            float loopG2Left = loopSource == 0 ? 0f : g2LoopLeft * g2;
                            float loopG2Right = loopSource == 0 ? 0f : g2LoopRight * g2;
                            loopCaptureOutput[leftIndex] = MixLimiter(loopG1Left + loopG2Left);
                            loopCaptureOutput[rightIndex] = MixLimiter(loopG1Right + loopG2Right);
                            if (copyMeet)
                            {
                                meetOutput[leftIndex] = mixedLeft;
                                meetOutput[rightIndex] = mixedRight;
                            }
                        }

                        UpdateHeldPeak(ref _guitar1MixedPeak, mixedPeak1);
                        UpdateHeldPeak(ref _guitar2MixedPeak, mixedPeak2);
                    }

                    UpdateHeldPeak(ref _dualAccompanimentPeak, accompanimentPeak);
                    UpdateHeldPeak(ref _guitar1ProcessedPeak, processedPeak1);
                    UpdateHeldPeak(ref _guitar2ProcessedPeak, processedPeak2);
                    UpdateHeldPeak(ref _dualFinalMixPeak, finalMixPeak);
                    UpdateHeldPeak(ref _guitar1PostPanLeftPeak, g1PostPanLeftPeak);
                    UpdateHeldPeak(ref _guitar1PostPanRightPeak, g1PostPanRightPeak);
                    UpdateHeldPeak(ref _guitar2PostPanLeftPeak, g2PostPanLeftPeak);
                    UpdateHeldPeak(ref _guitar2PostPanRightPeak, g2PostPanRightPeak);
                }
                else
                {
                    _processor.ProcessMonoToStereo(input, voiceInput, output,
                        _meetEnabled && _meetProvider is not null ? meetOutput : null,
                        loopCaptureOutput, frames);
                }
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref _totalAudioErrorCount);
                Interlocked.Increment(ref _dspErrorCount);
                RegisterExceptionFault(FaultStageDsp, exception, e, frames);
                TryClearOutputs(e);
                return;
            }

            try
            {
                _practiceLooper.Process(loopCaptureOutput, output,
                    _meetEnabled && _meetProvider is not null ? meetOutput : null, frames);

                // La grabadora conserva la mezcla interna completa, antes del master.
                // Así el usuario puede bajar auriculares/Loopback sin degradar el archivo guardado.
                _practiceRecorder.Capture(output, frames);

                // 2.41.10: master global post-DSP y post-looper. Afecta Playback 1-2 y
                // la salida virtual de videollamada, pero no cambia bancos, tonos ni grabaciones.
                float masterVolume = Volatile.Read(ref _masterVolume);
                bool copyMasterToMeet = _meetEnabled && _meetProvider is not null;
                int masterSamples = frames * 2;
                float masterPeak = 0f;
                float masterLeftPeak = 0f;
                float masterRightPeak = 0f;
                for (int i = 0; i < masterSamples; i++)
                {
                    float mastered = output[i] * masterVolume;
                    output[i] = float.IsFinite(mastered) ? mastered : 0f;
                    float absolute = MathF.Abs(output[i]);
                    if (absolute > masterPeak) masterPeak = absolute;
                    if ((i & 1) == 0) masterLeftPeak = MathF.Max(masterLeftPeak, absolute);
                    else masterRightPeak = MathF.Max(masterRightPeak, absolute);
                    if (copyMasterToMeet)
                    {
                        float meetMastered = meetOutput[i] * masterVolume;
                        meetOutput[i] = float.IsFinite(meetMastered) ? meetMastered : 0f;
                    }
                }
                UpdateHeldPeak(ref _masterOutputPeak, masterPeak);
                UpdateHeldPeak(ref _masterLeftPeak, masterLeftPeak);
                UpdateHeldPeak(ref _masterRightPeak, masterRightPeak);

                AsioBufferWriter.WriteStereo(e, output, frames);
                BufferedWaveProvider? meetProvider = _meetProvider;
                if (_meetEnabled && meetProvider is not null)
                {
                    int endpointChannels = Math.Clamp(Volatile.Read(ref _meetEndpointChannels), 1, 8);
                    int endpointSampleCount = frames * endpointChannels;
                    if (endpointSampleCount > meetEndpointOutput.Length)
                        throw new InvalidOperationException("El buffer de salida virtual multicanal es insuficiente.");

                    bool voiceOnly = !TwoGuitarMode && _processor.VoiceOnlyMode;
                    for (int frame = 0; frame < frames; frame++)
                    {
                        int stereoIndex = frame * 2;
                        float left = meetOutput[stereoIndex];
                        float right = meetOutput[stereoIndex + 1];
                        int endpointIndex = frame * endpointChannels;

                        if (voiceOnly)
                        {
                            // En Inglés / Modo Voz, la señal es mono lógica. La copiamos a
                            // TODOS los canales que expone el endpoint virtual. Así VoiceMeeter,
                            // Windows Recorder, Zoom o cualquier otra app reciben la misma voz
                            // a izquierda y derecha aunque el endpoint sea 4/6/8 canales.
                            float mono = (left + right) * 0.5f;
                            for (int channel = 0; channel < endpointChannels; channel++)
                                meetEndpointOutput[endpointIndex + channel] = mono;
                        }
                        else if (endpointChannels == 1)
                        {
                            meetEndpointOutput[endpointIndex] = (left + right) * 0.5f;
                        }
                        else
                        {
                            // En guitarra conservamos el estéreo original en L/R y dejamos
                            // silenciosos los canales adicionales para no alterar chorus/delay.
                            meetEndpointOutput[endpointIndex] = left;
                            meetEndpointOutput[endpointIndex + 1] = right;
                            for (int channel = 2; channel < endpointChannels; channel++)
                                meetEndpointOutput[endpointIndex + channel] = 0f;
                        }
                    }

                    int byteCount = endpointSampleCount * sizeof(float);
                    Buffer.BlockCopy(meetEndpointOutput, 0, meetBytes, 0, byteCount);
                    meetProvider.AddSamples(meetBytes, 0, byteCount);
                }
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref _totalAudioErrorCount);
                Interlocked.Increment(ref _outputWriteErrorCount);
                RegisterExceptionFault(FaultStageOutput, exception, e, frames);
                TryClearOutputs(e);
                return;
            }

            MarkSuccessfulCallback();
        }
        finally
        {
            EvaluateCallbackLoad(Stopwatch.GetTimestamp() - callbackStart, frames);
        }
    }

    private static void UpdateHeldPeak(ref float destination, float candidate)
    {
        if (candidate > Volatile.Read(ref destination))
            Volatile.Write(ref destination, candidate);
    }

    private static float MeasurePeak(float[] samples, int count)
    {
        float peak = 0f;
        int limit = Math.Min(count, samples.Length);
        for (int i = 0; i < limit; i++)
        {
            float value = MathF.Abs(samples[i]);
            if (value > peak) peak = value;
        }
        return peak;
    }

    /// <summary>
    /// Desplaza una fuente estéreo sin alterar absolutamente nada en el centro.
    /// Al llegar a un extremo pliega ambos canales hacia ese lado con compensación de potencia,
    /// de modo que un chorus/reverb estéreo no pierda el contenido del canal opuesto.
    /// </summary>
    private static void ApplyStereoPan(float left, float right, float pan, out float pannedLeft, out float pannedRight)
    {
        if (!float.IsFinite(left)) left = 0f;
        if (!float.IsFinite(right)) right = 0f;
        pan = float.IsFinite(pan) ? Math.Clamp(pan, -1f, 1f) : 0f;

        float amount = MathF.Abs(pan);
        if (amount <= 0.000001f)
        {
            pannedLeft = left;
            pannedRight = right;
            return;
        }

        // 2.41.30: el extremo es un hard-pan real y verificable. Se fuerza exactamente
        // a cero el canal contrario, sin depender de redondeos de coma flotante.
        const float InvSqrt2 = 0.7071067811865476f;
        float collapsed = (left + right) * InvSqrt2;
        if (amount >= 0.9999f)
        {
            if (pan < 0f)
            {
                pannedLeft = collapsed;
                pannedRight = 0f;
            }
            else
            {
                pannedLeft = 0f;
                pannedRight = collapsed;
            }
            return;
        }

        // En el extremo se usa (L + R) / sqrt(2): conserva el contenido de ambos canales
        // con una compensación razonable de potencia. Entre centro y extremo la transición
        // es lineal y continua, sin saltos al mover el control en tiempo real.
        float keep = 1f - amount;
        if (pan < 0f)
        {
            pannedLeft = (left * keep) + (collapsed * amount);
            pannedRight = right * keep;
        }
        else
        {
            pannedLeft = left * keep;
            pannedRight = (right * keep) + (collapsed * amount);
        }
    }

    private static float MixLimiter(float value)
    {
        if (!float.IsFinite(value)) return 0f;
        const float threshold = 0.92f;
        float magnitude = MathF.Abs(value);
        if (magnitude <= threshold) return value;
        float excess = magnitude - threshold;
        float compressed = threshold + ((1f - threshold) * (excess / ((1f - threshold) + excess)));
        compressed = MathF.Min(compressed, 0.999f);
        return value < 0f ? -compressed : compressed;
    }

    private void TryClearOutputs(AsioAudioAvailableEventArgs e)
    {
        try
        {
            AsioBufferWriter.ClearOutputs(e);
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref _totalAudioErrorCount);
            Interlocked.Increment(ref _outputWriteErrorCount);
            RegisterExceptionFault(FaultStageOutput, exception, e, e.SamplesPerBuffer);
        }
    }

    private void RegisterExceptionFault(int stage, Exception exception, AsioAudioAvailableEventArgs e, int frames)
    {
        if (Interlocked.CompareExchange(ref _faultIncidentActive, 1, 0) != 0)
        {
            Interlocked.Exchange(ref _successfulCallbacksAfterFault, 0);
            return;
        }

        CaptureFaultSnapshot(
            stage,
            exception,
            null,
            frames,
            e.InputBuffers.Length,
            e.OutputBuffers.Length,
            (int)e.AsioSampleType);
    }

    private void RegisterSyntheticFault(int stage, int frames, int inputBuffers, int outputBuffers,
        int sampleType, string message)
    {
        if (Interlocked.CompareExchange(ref _faultIncidentActive, 1, 0) != 0)
        {
            Interlocked.Exchange(ref _successfulCallbacksAfterFault, 0);
            return;
        }

        CaptureFaultSnapshot(stage, null, message, frames, inputBuffers, outputBuffers, sampleType);
    }

    private void CaptureFaultSnapshot(int stage, Exception? exception, string? syntheticMessage,
        int frames, int inputBuffers, int outputBuffers, int sampleType)
    {
        int sequence = Interlocked.Increment(ref _faultSequence);
        Volatile.Write(ref _faultStage, stage);
        Interlocked.Exchange(ref _faultException, exception);
        Interlocked.Exchange(ref _faultSyntheticMessage, syntheticMessage);
        Volatile.Write(ref _faultFrames, frames);
        Volatile.Write(ref _faultInputBuffers, inputBuffers);
        Volatile.Write(ref _faultOutputBuffers, outputBuffers);
        Volatile.Write(ref _faultSampleType, sampleType);
        Volatile.Write(ref _faultBufferAtIncident, ActualBufferSize);
        Volatile.Write(ref _faultSelectedInput, _selectedInputBufferIndex);
        Interlocked.Exchange(ref _faultCallbackCount, CallbackCount);
        Interlocked.Exchange(ref _successfulCallbacksAfterFault, 0);
        Interlocked.Exchange(ref _faultNotificationPending, 1);
    }

    private void MarkSuccessfulCallback()
    {
        if (Volatile.Read(ref _faultIncidentActive) == 0)
        {
            return;
        }

        int successful = Interlocked.Increment(ref _successfulCallbacksAfterFault);
        if (successful >= SuccessfulCallbacksToCloseFaultIncident &&
            Volatile.Read(ref _faultNotificationPending) == 0)
        {
            Interlocked.Exchange(ref _successfulCallbacksAfterFault, 0);
            Interlocked.Exchange(ref _faultIncidentActive, 0);
        }
    }

    private static string GetFaultStageName(int stage) => stage switch
    {
        FaultStageBuffer => "BUFFER ASIO",
        FaultStageInput => "ENTRADA ASIO",
        FaultStageDsp => "PROCESAMIENTO DSP",
        FaultStageOutput => "SALIDA ASIO",
        FaultStageDriverReset => "RESET DEL DRIVER ASIO",
        _ => "AUDIO"
    };

    private void ResetFaultIncidentState()
    {
        Interlocked.Exchange(ref _faultIncidentActive, 0);
        Interlocked.Exchange(ref _successfulCallbacksAfterFault, 0);
        Interlocked.Exchange(ref _faultNotificationPending, 0);
        Interlocked.Exchange(ref _faultSequence, 0);
        Volatile.Write(ref _faultStage, 0);
        Interlocked.Exchange(ref _faultException, null);
        Interlocked.Exchange(ref _faultSyntheticMessage, null);
        Volatile.Write(ref _faultFrames, 0);
        Volatile.Write(ref _faultInputBuffers, 0);
        Volatile.Write(ref _faultOutputBuffers, 0);
        Volatile.Write(ref _faultSampleType, 0);
        Volatile.Write(ref _faultBufferAtIncident, 0);
        Volatile.Write(ref _faultSelectedInput, 0);
        Interlocked.Exchange(ref _faultCallbackCount, 0);
    }

    private void EvaluateCallbackLoad(long elapsedTicks, int frames)
    {
        if (frames <= 0)
        {
            return;
        }

        double availableTicks = Stopwatch.Frequency * (frames / (double)SampleRate);
        int loadPercent = (int)Math.Clamp(Math.Round((elapsedTicks / availableTicks) * 100.0), 0, 999);
        Volatile.Write(ref _lastDspLoadPercent, loadPercent);

        UpdateMaximum(ref _maxDspLoadPercent, loadPercent);
        switch (_processor.CurrentChannel)
        {
            case AmpChannel.CleanTwin:
                UpdateMaximum(ref _maxDspLoadClean, loadPercent);
                break;
            case AmpChannel.CrunchBritish:
                UpdateMaximum(ref _maxDspLoadCrunch, loadPercent);
                break;
            case AmpChannel.LeadJcm800:
                UpdateMaximum(ref _maxDspLoadLead, loadPercent);
                break;
        }

        if (loadPercent >= 100)
        {
            Interlocked.Increment(ref _outputUnderrunCount);
            int misses = Interlocked.Increment(ref _consecutiveDeadlineMisses);
            if (misses == 3)
            {
                RegisterSyntheticFault(
                    FaultStageDsp,
                    frames,
                    0,
                    0,
                    -1,
                    "La carga DSP superó el tiempo disponible del buffer tres callbacks seguidos.");
            }
        }
        else
        {
            Interlocked.Exchange(ref _consecutiveDeadlineMisses, 0);
        }
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        while (true)
        {
            int current = Volatile.Read(ref target);
            if (current >= value || Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }

    private void EnterRealtimeGcMode()
    {
        try
        {
            // Se limpia antes de arrancar, nunca durante una sesión de audio.
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            GC.WaitForPendingFinalizers();

            _previousGcLatencyMode = GCSettings.LatencyMode;
            if (_previousGcLatencyMode != GCLatencyMode.SustainedLowLatency)
            {
                GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
                _gcLatencyModeChanged = true;
            }
        }
        catch
        {
            _gcLatencyModeChanged = false;
        }
    }

    private static void CollectAfterWarmUp()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            GC.WaitForPendingFinalizers();
        }
        catch
        {
            // El audio todavía no empezó; si el runtime rechaza la limpieza preventiva,
            // continuamos con el modo SustainedLowLatency ya configurado.
        }
    }

    private void LeaveRealtimeGcMode()
    {
        if (!_gcLatencyModeChanged)
        {
            return;
        }

        try
        {
            GCSettings.LatencyMode = _previousGcLatencyMode;
        }
        catch
        {
            // No se interrumpe el cierre si el runtime no permite cambiar el modo.
        }
        finally
        {
            _gcLatencyModeChanged = false;
        }
    }

    public void Dispose()
    {
        Stop();
        _processor.Dispose();
        _guitar1Processor.Dispose();
    }
}

internal readonly record struct AsioDeviceInfo(
    string DriverName,
    int InputCount,
    int OutputCount,
    bool Supports48Khz,
    IReadOnlyList<string> InputChannels);

internal sealed record MeetOutputDevice(string Id, string Name);
internal sealed record CaptureDeviceStatus(string Id, string Name, string State);
internal sealed record MeetInputDevice(string Id, string Name);
