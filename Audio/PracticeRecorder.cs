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

    public PracticeRecorder(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _channels = channels;
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

    public string Start(int minutes)
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

            for (int i = 0; i < count; i++)
            {
                float sample = interleavedStereo[i];
                if (!float.IsFinite(sample)) sample = 0f;
                sample = Math.Clamp(sample, -1f, 1f);
                _buffer[position + i] = (short)MathF.Round(sample * 32767f);
            }

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

    public async Task<string?> StopAndSaveAsync()
    {
        Volatile.Write(ref _active, 0);
        SpinWait.SpinUntil(() => Volatile.Read(ref _captureInProgress) == 0, 100);

        int sampleCount;
        string? path;
        lock (_sync)
        {
            if (IsSaving)
            {
                throw new InvalidOperationException("La grabación ya se está guardando.");
            }

            sampleCount = Math.Clamp(Volatile.Read(ref _position), 0, _buffer.Length);
            path = _path;
            if (sampleCount <= 0 || string.IsNullOrWhiteSpace(path))
            {
                ResetState();
                return null;
            }
            Volatile.Write(ref _saving, 1);
            Volatile.Write(ref _autoCompleted, 0);
        }

        try
        {
            await Task.Run(() => WritePcm16Wave(path!, _buffer, sampleCount)).ConfigureAwait(false);
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
