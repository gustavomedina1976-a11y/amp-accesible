using System.Runtime.InteropServices;

namespace GDMAmpAccessible.Audio;

/// <summary>
/// Grabador corto de práctica. Reserva una única memoria PCM16 al crear el motor,
/// antes de iniciar ASIO. Durante el callback sólo convierte/copía muestras; el WAV
/// se escribe después, fuera del camino de audio, para no introducir cortes.
/// </summary>
internal sealed class PracticeRecorder
{
    private const int MaximumMinutes = 5;

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly object _sync = new();
    private readonly short[] _buffer;
    private int _position;
    private int _maxSamples;
    private int _active;
    private int _captureInProgress;
    private int _autoCompleted;
    private int _saving;
    private string? _path;
    private int _durationMinutes;
    private long _totalCapturedSamples;
    public const float SignalThreshold = 0.0000316f;
    private long _signalSamples;
    private double _receivedPeak;
    public long SignalSamples => Interlocked.Read(ref _signalSamples);
    public bool SignalDetected => SignalSamples > 0;
    private RecordingSnapshot? _lastRecording;
    public RecordingSnapshot? LastRecording => Volatile.Read(ref _lastRecording);
    public RecorderDiagnostics? ActiveDiagnostics => IsRecording ? _diagnostics : null;
    public int RecordingDspMaximum => _diagnostics.DspMaximum;
    public long RecordingDspDeadlines => _diagnostics.DspDeadlines;
    // Muestras individuales intercaladas: un frame estereo cuenta como dos.
    // Se conserva al guardar/cancelar y se reinicia al comenzar otra toma.
    public long TotalCapturedSamples => Interlocked.Read(ref _totalCapturedSamples);
    public double TotalCapturedSeconds => TotalCapturedSamples / ((double)_sampleRate * _channels);
    public double MaximumAbsoluteReceivedPeak => Volatile.Read(ref _receivedPeak);
    // Referencia digital: amplitud 1 = 0 dBFS. Sin logaritmos en Capture.
    public double MaximumReceivedPeakDbfs
    {
        get
        {
            double peak = MaximumAbsoluteReceivedPeak;
            return peak > 0 ? 20.0 * Math.Log10(peak) : double.NegativeInfinity;
        }
    }
    private RecorderDiagnostics _diagnostics;
    public string DiagnosticSummary { get; private set; } = "Sin diagnostico de grabacion.";
    public string? DiagnosticPath { get; private set; }

    public PracticeRecorder(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _diagnostics = new RecorderDiagnostics(sampleRate, channels);
        _buffer = new short[checked(MaximumMinutes * 60 * sampleRate * channels)];
    }

    public bool IsRecording => Volatile.Read(ref _active) != 0;
    public bool IsSaving => Volatile.Read(ref _saving) != 0;
    public bool HasPendingRecording => Volatile.Read(ref _position) > 0;
    public bool AutoCompleted => Volatile.Read(ref _autoCompleted) != 0;
    public int DurationMinutes => Volatile.Read(ref _durationMinutes);
    public int RecordedSamples => Volatile.Read(ref _position);
    public double RecordedSeconds => RecordedSamples / (double)(_sampleRate * _channels);

    public double RemainingSeconds
    {
        get
        {
            int max = Volatile.Read(ref _maxSamples);
            if (max <= 0) return 0;
            return Math.Max(0, (max - RecordedSamples) / (double)(_sampleRate * _channels));
        }
    }

    public string Start(int minutes, string context = "")
    {
        if (minutes != 5)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), "La grabación de práctica está fijada en un máximo de 5 minutos.");
        }

        lock (_sync)
        {
            if (IsRecording)
            {
                throw new InvalidOperationException("Ya hay una grabación de práctica en curso.");
            }
            if (IsSaving)
            {
                throw new InvalidOperationException("La grabación anterior todavía se está guardando.");
            }
            if (HasPendingRecording)
            {
                throw new InvalidOperationException("Hay una grabación pendiente de guardar.");
            }

            int maxSamples = checked(minutes * 60 * _sampleRate * _channels);
            Volatile.Write(ref _position, 0);
            Volatile.Write(ref _maxSamples, maxSamples);
            Volatile.Write(ref _durationMinutes, minutes);
            Volatile.Write(ref _autoCompleted, 0);
            _path = BuildOutputPath();
            _diagnostics = new RecorderDiagnostics(_sampleRate, _channels) { ContextStart = context };
            DiagnosticPath = null;
            DiagnosticSummary = "Grabando; diagnostico pendiente de cierre.";
            Interlocked.Exchange(ref _totalCapturedSamples, 0);
            Interlocked.Exchange(ref _signalSamples, 0);
            Volatile.Write(ref _receivedPeak, 0);
            Volatile.Write(ref _active, 1);
            return _path;
        }
    }

    public void Capture(float[] interleavedStereo, int frames)
    {
        if (Volatile.Read(ref _active) == 0) return;

        Interlocked.Increment(ref _captureInProgress);
        try
        {
            if (Volatile.Read(ref _active) == 0) return;

            // La telemetria se reserva en Start. Aqui no se crean objetos,
            // cadenas ni informes y no se hace E/S ni se toman locks.
            RecorderDiagnostics diagnostics = _diagnostics;
            diagnostics.Callback(frames);
            int position = Volatile.Read(ref _position);
            int requested = frames * _channels;
            int remaining = Volatile.Read(ref _maxSamples) - position;
            int count = Math.Min(requested, remaining);
            if (count <= 0)
            {
                Volatile.Write(ref _active, 0);
                Volatile.Write(ref _autoCompleted, 1);
                return;
            }

            int channel = position % _channels;
            long signalSamples = 0;
            double peak = 0;
            for (int i = 0; i < count; i++)
            {
                float original = interleavedStereo[i];
                bool finite = float.IsFinite(original);
                if (finite)
                {
                    float absolute = Math.Abs(original);
                    if (absolute > peak) peak = absolute;
                    if (absolute >= SignalThreshold) signalSamples++;
                }
                float sample = finite ? original : 0f;
                sample = Math.Clamp(sample, -1f, 1f);
                short pcm = (short)MathF.Round(sample * 32767f);
                _buffer[position + i] = pcm;
                diagnostics.Sample(original, pcm, channel, finite);
                if (++channel == _channels) channel = 0;
            }

            Interlocked.Add(ref _totalCapturedSamples, count);
            Interlocked.Add(ref _signalSamples, signalSamples);
            Volatile.Write(ref _receivedPeak, Math.Max(_receivedPeak, peak));
            position += count;
            Volatile.Write(ref _position, position);
            if (position >= Volatile.Read(ref _maxSamples))
            {
                Volatile.Write(ref _active, 0);
                Volatile.Write(ref _autoCompleted, 1);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _captureInProgress);
        }
    }

    public async Task<string?> StopAndSaveAsync(string context = "")
    {
        Volatile.Write(ref _active, 0);
        bool drained = SpinWait.SpinUntil(() => Volatile.Read(ref _captureInProgress) == 0, 100);

        int sampleCount;
        string? path;
        lock (_sync)
        {
            if (IsSaving)
            {
                throw new InvalidOperationException("La grabación ya se está guardando.");
            }

            _diagnostics.ContextEnd = context;
            _diagnostics.Drained = drained;
            _diagnostics.Automatic = AutoCompleted;
            sampleCount = Math.Clamp(Volatile.Read(ref _position), 0, _buffer.Length);
            path = _path;
            if (sampleCount <= 0 || string.IsNullOrWhiteSpace(path))
            {
                SaveDiagnostics(path, sampleCount, "Sin muestras: no se escribio WAV.");
                ResetState();
                return null;
            }
            Volatile.Write(ref _saving, 1);
            Volatile.Write(ref _autoCompleted, 0);
        }

        try
        {
            await Task.Run(() =>
            {
                try { WritePcm16Wave(path!, _buffer, sampleCount); }
                catch (Exception ex)
                {
                    SaveDiagnostics(path, sampleCount, "Error al escribir WAV: " + ex);
                    throw;
                }
                Volatile.Write(ref _lastRecording, new RecordingSnapshot(sampleCount, sampleCount / ((double)_sampleRate * _channels),
                    MaximumAbsoluteReceivedPeak, MaximumReceivedPeakDbfs, SignalSamples, SignalDetected, path!, _diagnostics));
                string verification;
                try { verification = _diagnostics.Verify(path!, _buffer, sampleCount); }
                catch (Exception ex) { verification = "Error al verificar WAV: " + ex; }
                SaveDiagnostics(path, sampleCount, verification);
            }).ConfigureAwait(false);
            return path;
        }
        finally
        {
            lock (_sync)
            {
                ResetState();
                Volatile.Write(ref _saving, 0);
            }
        }
    }

    public void Cancel()
    {
        Volatile.Write(ref _active, 0);
        SpinWait.SpinUntil(() => Volatile.Read(ref _captureInProgress) == 0, 100);
        lock (_sync)
        {
            if (!IsSaving)
            {
                ResetState();
            }
        }
    }

    private void SaveDiagnostics(string? path, int count, string result)
    {
        DiagnosticSummary = _diagnostics.Report(count, result)
            + $"Total de muestras capturadas: {TotalCapturedSamples} (muestras individuales; {_channels} por frame).\n"
            + $"Segundos capturados: {TotalCapturedSeconds:F6}.\n"
            + $"Muestras con senal: {SignalSamples}; senal detectada: {SignalDetected}; umbral amplitud: {SignalThreshold}.\n"
            + $"Pico maximo absoluto recibido (float finito, antes de limitar a PCM16): {MaximumAbsoluteReceivedPeak:G9}.\n"
            + $"Pico recibido en dBFS: {(double.IsNegativeInfinity(MaximumReceivedPeakDbfs) ? "-infinito (sin senal finita distinta de cero)" : MaximumReceivedPeakDbfs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))}.\n";
        try
        {
            if (path is null) return;
            string reportPath = path + ".diagnostico.txt";
            File.WriteAllText(reportPath, DiagnosticSummary, System.Text.Encoding.UTF8);
            DiagnosticPath = reportPath;
        }
        catch (Exception ex)
        {
            DiagnosticSummary += "\nNo se pudo escribir el diagnostico: " + ex.Message;
        }
    }

    private void ResetState()
    {
        _path = null;
        Volatile.Write(ref _position, 0);
        Volatile.Write(ref _maxSamples, 0);
        Volatile.Write(ref _durationMinutes, 0);
        Volatile.Write(ref _autoCompleted, 0);
    }

    private static string BuildOutputPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string folder = Path.Combine(root, "GDM Amp Accessible", "Grabaciones");
        Directory.CreateDirectory(folder);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        return Path.Combine(folder, $"Practica_{stamp}.wav");
    }

    private void WritePcm16Wave(string path, short[] samples, int sampleCount)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024,
            FileOptions.SequentialScan);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        int blockAlign = _channels * sizeof(short);
        int byteRate = _sampleRate * blockAlign;
        int dataBytes = checked(sampleCount * sizeof(short));

        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)_channels);
        writer.Write(_sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        writer.Flush();

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(samples.AsSpan(0, sampleCount));
        stream.Write(bytes);
    }
}

internal sealed record RecordingSnapshot(long Samples, double Seconds, double Peak, double PeakDbfs,
    long SignalSamples, bool SignalDetected, string Path, RecorderDiagnostics Diagnostics);
