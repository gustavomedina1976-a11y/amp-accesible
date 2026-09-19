using System.Text;
using NAudio.Wave;
using NAudio.Wave.Asio;
using NAudio.Wave.SampleProviders;

namespace GDMAmpAccessible.Audio;

internal sealed record NamCaptureRequest(
    string DriverName,
    int SendOutputIndex,
    int ReturnInputIndex,
    string TrainingInputPath,
    string CaptureName,
    float SendAttenuationDb,
    bool LevelTest);

internal sealed record NamCaptureResult(
    bool LevelTest,
    string? Folder,
    string? OriginalInputPath,
    string? SentInputPath,
    string? OutputPath,
    string? ReportPath,
    double Seconds,
    float Peak,
    double PeakDbfs,
    long ClippedSamples,
    long FramesCaptured,
    int BufferSize);

/// <summary>
/// Ruta independiente para capturar un amplificador real con NAM.
/// No utiliza AudioProcessor, IR, NAM, efectos, master, voz, Loopback ni acompañamiento.
/// La señal de retorno se lee solamente para grabar: jamás se copia a ninguna salida.
/// </summary>
internal sealed class NamCaptureEngine : IDisposable
{
    public const int SampleRate = 48000;
    public static string CaptureFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "GDM Amp Accessible", "Capturas NAM");

    private readonly object _stateLock = new();
    private AsioOut? _asio;
    private TaskCompletionSource<bool>? _completion;
    private float[]? _captureBlock;
    private float[]? _recorded;
    private int _recordPosition;
    private int _targetFrames;
    private int _returnInputIndex;
    private float _peak;
    private long _clippedSamples;
    private int _running;
    private int _cancelRequested;
    private Exception? _callbackException;

    public bool IsRunning => Volatile.Read(ref _running) != 0;

    public async Task<NamCaptureResult> CaptureAsync(NamCaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsRunning) throw new InvalidOperationException("Ya hay una captura NAM en curso.");
        if (string.IsNullOrWhiteSpace(request.DriverName)) throw new ArgumentException("Seleccione un controlador ASIO.");
        if (!File.Exists(request.TrainingInputPath)) throw new FileNotFoundException("No se encontró el archivo input.wav seleccionado.", request.TrainingInputPath);

        float[] original = LoadTrainingInput(request.TrainingInputPath);
        int targetFrames = request.LevelTest
            ? Math.Min(original.Length, SampleRate * 5)
            : original.Length;
        if (targetFrames < SampleRate)
            throw new InvalidOperationException("El archivo de entrenamiento es demasiado corto.");

        float attenuationDb = Math.Clamp(float.IsFinite(request.SendAttenuationDb) ? request.SendAttenuationDb : -30f, -60f, 0f);
        float sendGain = MathF.Pow(10f, attenuationDb / 20f);
        float[] sent = new float[targetFrames];
        for (int i = 0; i < sent.Length; i++)
        {
            sent[i] = Math.Clamp(original[i] * sendGain, -1f, 1f);
        }

        AsioOut asio = new(request.DriverName)
        {
            InputChannelOffset = 0,
            ChannelOffset = request.SendOutputIndex,
            AutoStop = false
        };

        if (!asio.IsSampleRateSupported(SampleRate))
        {
            asio.Dispose();
            throw new InvalidOperationException("El controlador ASIO seleccionado no admite 48 kHz.");
        }
        if (request.SendOutputIndex < 0 || request.SendOutputIndex >= asio.DriverOutputChannelCount)
        {
            asio.Dispose();
            throw new InvalidOperationException("La salida ASIO de envío seleccionada no existe.");
        }
        if (request.ReturnInputIndex < 0 || request.ReturnInputIndex >= asio.DriverInputChannelCount)
        {
            asio.Dispose();
            throw new InvalidOperationException("La entrada ASIO de retorno seleccionada no existe.");
        }

        var provider = new NamTrainingWaveProvider(sent);
        int recordChannelCount = request.ReturnInputIndex + 1;
        asio.AudioAvailable += OnAudioAvailable;
        asio.InitRecordAndPlayback(provider, recordChannelCount, SampleRate);

        int bufferSize = asio.FramesPerBuffer;
        int capacity = Math.Max(bufferSize, 4096);
        var recorded = new float[targetFrames];
        var captureBlock = new float[capacity];
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_stateLock)
        {
            _asio = asio;
            _completion = completion;
            _captureBlock = captureBlock;
            _recorded = recorded;
            _recordPosition = 0;
            _targetFrames = targetFrames;
            _returnInputIndex = request.ReturnInputIndex;
            _peak = 0f;
            _clippedSamples = 0;
            _callbackException = null;
            Interlocked.Exchange(ref _cancelRequested, 0);
            Interlocked.Exchange(ref _running, 1);
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(RequestStop);
        try
        {
            asio.Play();
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            StopDriver();
        }

        if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _cancelRequested) != 0)
            throw new OperationCanceledException(cancellationToken);
        if (_callbackException is not null)
            throw new InvalidOperationException("La captura ASIO falló dentro del callback.", _callbackException);

        int captured = Math.Min(_recordPosition, targetFrames);
        float peak = _peak;
        long clipped = Interlocked.Read(ref _clippedSamples);
        double peakDbfs = peak > 0.0000001f ? 20.0 * Math.Log10(peak) : -120.0;
        double seconds = captured / (double)SampleRate;

        if (request.LevelTest)
        {
            return new NamCaptureResult(true, null, null, null, null, null,
                seconds, peak, peakDbfs, clipped, captured, bufferSize);
        }

        Directory.CreateDirectory(CaptureFolder);
        string safeName = SafeName(request.CaptureName);
        string folder = Path.Combine(CaptureFolder, $"{safeName}_{DateTime.Now:yyyy-MM-dd_HHmmss}");
        Directory.CreateDirectory(folder);

        string originalPath = Path.Combine(folder, "input_original.wav");
        string sentPath = Path.Combine(folder, "input_enviado.wav");
        string outputPath = Path.Combine(folder, "output_capturado.wav");
        string reportPath = Path.Combine(folder, "informe_captura.txt");

        File.Copy(request.TrainingInputPath, originalPath, overwrite: true);
        WriteMono24(sentPath, sent);
        WriteMono24(outputPath, recorded.AsSpan(0, captured));

        string levelAssessment = clipped > 0
            ? "CLIPPING: repita la toma bajando la ganancia del retorno."
            : peakDbfs < -36.0
                ? "Retorno muy bajo: conviene revisar nivel de envío o ganancia de entrada."
                : peakDbfs > -3.0
                    ? "Retorno alto: hay poco margen; conviene bajar un poco la ganancia de entrada."
                    : "Nivel de retorno útil, sin clipping detectado.";

        var report = new StringBuilder();
        report.AppendLine($"Amp Accessible {AppInfo.Version} - Captura NAM");
        report.AppendLine($"Fecha local: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Nombre: {safeName}");
        report.AppendLine($"Driver ASIO: {request.DriverName}");
        report.AppendLine($"Salida de envío: {request.SendOutputIndex + 1}");
        report.AppendLine($"Entrada de retorno: {request.ReturnInputIndex + 1}");
        report.AppendLine($"Frecuencia: {SampleRate} Hz");
        report.AppendLine($"Buffer ASIO: {bufferSize} muestras");
        report.AppendLine($"Atenuación digital de envío: {attenuationDb:0.0} dB");
        report.AppendLine($"Duración: {seconds:0.000} s");
        report.AppendLine($"Frames capturados: {captured}");
        report.AppendLine($"Pico de retorno: {peakDbfs:0.00} dBFS");
        report.AppendLine($"Muestras en clipping: {clipped}");
        report.AppendLine($"Evaluación: {levelAssessment}");
        report.AppendLine();
        report.AppendLine("RUTA DE CAPTURA:");
        report.AppendLine("input.wav -> salida ASIO seleccionada -> amplificador real -> load box -> entrada ASIO seleccionada -> output_capturado.wav");
        report.AppendLine("El retorno NO fue monitorizado ni enviado nuevamente a ninguna salida.");
        report.AppendLine("No se aplicaron ampli interno, NAM, IR, EQ, gate, efectos, master, voz, Loopback, metrónomo ni acompañamiento.");
        report.AppendLine();
        report.AppendLine("ARCHIVOS:");
        report.AppendLine("input_original.wav = copia exacta del archivo elegido.");
        report.AppendLine("input_enviado.wav = señal digital realmente enviada después de la atenuación de seguridad.");
        report.AppendLine("output_capturado.wav = retorno mono crudo de la load box.");
        File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

        return new NamCaptureResult(false, folder, originalPath, sentPath, outputPath, reportPath,
            seconds, peak, peakDbfs, clipped, captured, bufferSize);
    }

    public void RequestStop()
    {
        if (!IsRunning) return;
        Interlocked.Exchange(ref _cancelRequested, 1);
        _completion?.TrySetResult(true);
    }

    private void OnAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
    {
        if (!IsRunning) return;

        float[]? block = _captureBlock;
        float[]? recorded = _recorded;
        if (block is null || recorded is null)
        {
            _callbackException = new InvalidOperationException("Buffers de captura NAM no disponibles.");
            _completion?.TrySetResult(true);
            return;
        }

        try
        {
            int frames = e.SamplesPerBuffer;
            if (frames <= 0 || frames > block.Length)
                throw new InvalidOperationException($"Buffer ASIO inesperado: {frames} muestras.");

            AsioInputReader.ReadMono(e, _returnInputIndex, block, frames);

            int position = _recordPosition;
            int remaining = _targetFrames - position;
            int copy = Math.Min(frames, Math.Max(0, remaining));
            float localPeak = _peak;
            long localClipped = 0;

            for (int i = 0; i < copy; i++)
            {
                float sample = float.IsFinite(block[i]) ? block[i] : 0f;
                recorded[position + i] = sample;
                float abs = MathF.Abs(sample);
                if (abs > localPeak) localPeak = abs;
                if (abs >= 0.999f) localClipped++;
            }

            _peak = localPeak;
            if (localClipped > 0) Interlocked.Add(ref _clippedSamples, localClipped);
            _recordPosition = position + copy;

            if (_recordPosition >= _targetFrames)
                _completion?.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _callbackException = ex;
            _completion?.TrySetResult(true);
        }
    }

    private void StopDriver()
    {
        AsioOut? asio;
        lock (_stateLock)
        {
            asio = _asio;
            _asio = null;
        }

        if (asio is not null)
        {
            try
            {
                asio.AudioAvailable -= OnAudioAvailable;
                if (asio.PlaybackState != PlaybackState.Stopped) asio.Stop();
            }
            catch { }
            finally { asio.Dispose(); }
        }

        Interlocked.Exchange(ref _running, 0);
        _completion = null;
        _captureBlock = null;
    }

    private static float[] LoadTrainingInput(string path)
    {
        using var reader = new WaveFileReader(path);
        if (reader.WaveFormat.SampleRate != SampleRate)
            throw new InvalidOperationException($"El input.wav debe estar a 48 kHz. Archivo seleccionado: {reader.WaveFormat.SampleRate} Hz.");
        if (reader.WaveFormat.Channels != 1)
            throw new InvalidOperationException($"El input.wav debe ser mono. Archivo seleccionado: {reader.WaveFormat.Channels} canales.");

        long frameCountLong = reader.Length / Math.Max(1, reader.WaveFormat.BlockAlign);
        if (frameCountLong <= 0 || frameCountLong > SampleRate * 60L * 15L)
            throw new InvalidOperationException("Duración de input.wav no válida o superior a 15 minutos.");

        int frameCount = checked((int)frameCountLong);
        float[] samples = new float[frameCount];
        ISampleProvider provider = reader.ToSampleProvider();

        int total = 0;
        while (total < samples.Length)
        {
            int read = provider.Read(samples, total, samples.Length - total);
            if (read <= 0) break;
            total += read;
        }

        if (total < SampleRate)
            throw new InvalidOperationException("No se pudieron leer al menos un segundo de audio desde input.wav.");
        if (total != samples.Length) Array.Resize(ref samples, total);

        for (int i = 0; i < samples.Length; i++)
            samples[i] = float.IsFinite(samples[i]) ? Math.Clamp(samples[i], -1f, 1f) : 0f;

        return samples;
    }

    private static void WriteMono24(string path, ReadOnlySpan<float> samples)
    {
        var format = new WaveFormat(SampleRate, 24, 1);
        using var writer = new WaveFileWriter(path, format);
        byte[] bytes = new byte[8192 * 3];

        int position = 0;
        while (position < samples.Length)
        {
            int count = Math.Min(8192, samples.Length - position);
            for (int i = 0; i < count; i++)
            {
                float sample = Math.Clamp(float.IsFinite(samples[position + i]) ? samples[position + i] : 0f, -1f, 0.999999f);
                int value = (int)MathF.Round(sample * 8388607f);
                int offset = i * 3;
                bytes[offset] = (byte)(value & 0xFF);
                bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
                bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
            }
            writer.Write(bytes, 0, count * 3);
            position += count;
        }
    }

    private static string SafeName(string? value)
    {
        string text = string.IsNullOrWhiteSpace(value) ? "Captura_NAM" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
            text = text.Replace(invalid, '_');
        return text.Length <= 60 ? text : text[..60];
    }

    public void Dispose()
    {
        RequestStop();
        StopDriver();
        _recorded = null;
    }

    private sealed class NamTrainingWaveProvider : IWaveProvider
    {
        private readonly float[] _samples;
        private int _position;

        public NamTrainingWaveProvider(float[] samples)
        {
            _samples = samples;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            int requestedSamples = count / sizeof(float);
            int remaining = Math.Max(0, _samples.Length - _position);
            int samplesToWrite = Math.Min(requestedSamples, remaining);
            int bytesToWrite = samplesToWrite * sizeof(float);

            if (bytesToWrite > 0)
            {
                Buffer.BlockCopy(_samples, _position * sizeof(float), buffer, offset, bytesToWrite);
                _position += samplesToWrite;
            }

            int remainingBytes = count - bytesToWrite;
            if (remainingBytes > 0)
                Array.Clear(buffer, offset + bytesToWrite, remainingBytes);

            return count;
        }
    }
}