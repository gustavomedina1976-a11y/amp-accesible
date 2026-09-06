using System.Runtime.InteropServices;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Puente opcional al motor NeuralAudioCAPI. La aplicación no depende de la DLL para
/// arrancar: si falta, los amplificadores internos continúan funcionando normalmente.
/// La carga/descarga del modelo debe hacerse con ASIO detenido.
/// </summary>
internal sealed class NamProcessor : IDisposable
{
    private const string NativeLibraryName = "NeuralAudioCAPI";
    private const int MaximumFrames = 4096;

    // 2.41.21: margen nominal posterior al modelo. Algunos NAM tienen una salida
    // perceptualmente mucho mas fuerte que los amplificadores internos. El pad es fijo
    // y preserva la dinamica; el usuario conserva NamOutputTrimDb para ajustar a gusto.
    public const float SafetyOutputPadDb = -9.0f;
    public const float MaximumAutomaticRecommendedOutputBoostDb = 3.0f;

    private IntPtr _loader;
    private IntPtr _model;
    private bool _faulted;

    public bool HasModel => _model != IntPtr.Zero && !_faulted;
    public string? ModelPath { get; private set; }
    public float ModelSampleRate { get; private set; }
    public float RecommendedInputDb { get; private set; }
    public float RecommendedOutputDb { get; private set; }
    public float AppliedRecommendedOutputDb => Math.Clamp(RecommendedOutputDb, -36f, MaximumAutomaticRecommendedOutputBoostDb);
    public int LoadMode { get; private set; }
    public bool IsStaticModel { get; private set; }
    public string? LastError { get; private set; }

    public static bool IsNativeEngineAvailable(out string description)
    {
        IntPtr handle = IntPtr.Zero;
        string local = Path.Combine(AppContext.BaseDirectory, "NeuralAudioCAPI.dll");
        bool localExists = File.Exists(local);
        try
        {
            if (localExists && NativeLibrary.TryLoad(local, out handle))
            {
                NativeLibrary.Free(handle);
                description = "Motor NAM nativo disponible.";
                return true;
            }

            if (NativeLibrary.TryLoad(NativeLibraryName, out handle))
            {
                NativeLibrary.Free(handle);
                description = "Motor NAM nativo disponible.";
                return true;
            }
        }
        catch (Exception ex)
        {
            description = $"Motor NAM no disponible: {ex.Message}";
            return false;
        }

        description = localExists
            ? "NeuralAudioCAPI.dll fue encontrada, pero Windows no pudo cargarla. Puede faltar el runtime de Visual C++ o una dependencia del motor."
            : "Motor NAM no disponible. Falta NeuralAudioCAPI.dll junto a AmpAccessible.exe. Ejecute INSTALAR_MOTOR_NAM.bat y vuelva a ejecutar COMPILAR.bat.";
        return false;
    }

    public NamModelInfo Load(string path, int applicationSampleRate)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("No se encontró el archivo NAM.", path);
        }

        if (!path.EndsWith(".nam", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El archivo seleccionado no tiene extensión .nam.");
        }

        if (!IsNativeEngineAvailable(out string availability))
        {
            throw new DllNotFoundException(availability);
        }

        Clear();
        _faulted = false;
        LastError = null;

        try
        {
            _loader = Native.CreateLoader();
            if (_loader == IntPtr.Zero)
            {
                throw new InvalidOperationException("NeuralAudio no pudo crear el cargador de modelos.");
            }

            // NeuralAudio usa 128 por defecto. La app puede recibir hasta 4096 frames en
            // un callback y esta reserva se realiza antes de que el modelo toque ASIO.
            Native.SetDefaultMaxAudioBufferSize(_loader, MaximumFrames);
            _model = Native.CreateModelFromFile(_loader, path);
            if (_model == IntPtr.Zero)
            {
                throw new InvalidOperationException("El motor NAM no pudo abrir este modelo.");
            }

            Native.SetMaxAudioBufferSize(_model, MaximumFrames);
            ModelSampleRate = Native.GetSampleRate(_model);
            RecommendedInputDb = SanitizeDb(Native.GetRecommendedInputDBAdjustment(_model));
            RecommendedOutputDb = SanitizeDb(Native.GetRecommendedOutputDBAdjustment(_model));
            LoadMode = Native.GetLoadMode(_model);
            IsStaticModel = Native.IsStatic(_model);

            // La C API pública actual usa 48 kHz como frecuencia externa por defecto y
            // no expone SetExternalSampleRate. Amp Accessible también trabaja a 48 kHz.
            // No rechazamos modelos por su frecuencia interna: NeuralAudio puede adaptar
            // determinados WaveNet cuando 48 kHz es un múltiplo compatible del modelo.
            if (applicationSampleRate != 48000)
            {
                Clear();
                throw new InvalidOperationException($"NAM requiere que Amp Accessible trabaje a 48000 Hz; la sesión actual usa {applicationSampleRate} Hz.");
            }

            ModelPath = path;
            return new NamModelInfo(
                Path.GetFileName(path),
                ModelSampleRate,
                RecommendedInputDb,
                RecommendedOutputDb,
                LoadMode,
                IsStaticModel);
        }
        catch
        {
            Clear();
            throw;
        }
    }

    public unsafe void Process(float[] input, float[] output, int frames, float inputTrimDb, float outputTrimDb, float autoLevelDb = 0f)
    {
        if (!HasModel)
        {
            throw new InvalidOperationException("No hay un modelo NAM disponible para procesar.");
        }
        if (frames < 1 || frames > MaximumFrames || frames > input.Length || frames > output.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), "Tamaño de bloque NAM no válido.");
        }

        float inputGain = DbToLinear(RecommendedInputDb + Math.Clamp(inputTrimDb, -24f, 24f));
        // La recomendacion de salida del modelo se respeta para atenuaciones, pero una
        // recomendacion positiva exagerada no puede elevar automaticamente el NAM mas de
        // +3 dB. A eso se suma un pad nominal de -9 dB. Con trim de salida 0 dB, ningun
        // modelo obtiene una subida automatica neta superior a -6 dB respecto de su salida
        // cruda. El usuario puede aumentar NamOutputTrimDb conscientemente si lo necesita.
        float automaticOutputDb = AppliedRecommendedOutputDb + SafetyOutputPadDb;
        float outputGain = DbToLinear(automaticOutputDb + Math.Clamp(outputTrimDb, -24f, 24f) + Math.Clamp(autoLevelDb, -12f, 12f));

        for (int i = 0; i < frames; i++)
        {
            float value = input[i];
            if (!float.IsFinite(value)) value = 0f;
            input[i] = Math.Clamp(value * inputGain, -8f, 8f);
        }

        try
        {
            fixed (float* inputPtr = input)
            fixed (float* outputPtr = output)
            {
                Native.Process(_model, inputPtr, outputPtr, (nuint)frames);
            }
        }
        catch (Exception ex)
        {
            _faulted = true;
            LastError = ex.Message;
            throw;
        }

        for (int i = 0; i < frames; i++)
        {
            float value = output[i];
            if (!float.IsFinite(value)) value = 0f;
            output[i] = Math.Clamp(value * outputGain, -8f, 8f);
        }
    }

    public void Clear()
    {
        if (_model != IntPtr.Zero)
        {
            try { Native.DeleteModel(_model); } catch { }
            _model = IntPtr.Zero;
        }
        if (_loader != IntPtr.Zero)
        {
            try { Native.DeleteLoader(_loader); } catch { }
            _loader = IntPtr.Zero;
        }

        ModelPath = null;
        ModelSampleRate = 0f;
        RecommendedInputDb = 0f;
        RecommendedOutputDb = 0f;
        LoadMode = 0;
        IsStaticModel = false;
        _faulted = false;
        LastError = null;
    }

    public void Dispose() => Clear();

    private static float SanitizeDb(float value) => float.IsFinite(value) ? Math.Clamp(value, -36f, 36f) : 0f;
    private static float DbToLinear(float db) => MathF.Pow(10f, db / 20f);

    private static class Native
    {
        [DllImport(NativeLibraryName)] public static extern IntPtr CreateLoader();
        [DllImport(NativeLibraryName)] public static extern void DeleteLoader(IntPtr loader);
        [DllImport(NativeLibraryName)] public static extern IntPtr CreateModelFromFile(IntPtr loader, [MarshalAs(UnmanagedType.LPWStr)] string modelPath);
        [DllImport(NativeLibraryName)] public static extern void DeleteModel(IntPtr model);
        [DllImport(NativeLibraryName)] public static extern void SetDefaultMaxAudioBufferSize(IntPtr loader, int maxSize);
        [DllImport(NativeLibraryName)] public static extern int GetLoadMode(IntPtr model);
        [DllImport(NativeLibraryName)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool IsStatic(IntPtr model);
        [DllImport(NativeLibraryName)] public static extern void SetMaxAudioBufferSize(IntPtr model, int maxSize);
        [DllImport(NativeLibraryName)] public static extern float GetRecommendedInputDBAdjustment(IntPtr model);
        [DllImport(NativeLibraryName)] public static extern float GetRecommendedOutputDBAdjustment(IntPtr model);
        [DllImport(NativeLibraryName)] public static extern float GetSampleRate(IntPtr model);
        [DllImport(NativeLibraryName)] public static extern unsafe void Process(IntPtr model, float* input, float* output, nuint numSamples);
    }
}

internal sealed record NamModelInfo(
    string FileName,
    float SampleRate,
    float RecommendedInputDb,
    float RecommendedOutputDb,
    int LoadMode,
    bool IsStatic);
