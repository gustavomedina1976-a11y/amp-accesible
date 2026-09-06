namespace GDMAmpAccessible.Audio;

/// <summary>
/// Looper estéreo de práctica pensado para uso por teclado/JAWS.
/// La memoria se reserva una sola vez al crear el motor. El callback ASIO no asigna
/// memoria ni escribe archivos: sólo captura, mezcla y reproduce muestras.
/// </summary>
internal sealed class PracticeLooper
{
    private const int MaximumSeconds = 120;
    private const float PlaybackGain = 0.82f;
    private const float OverdubInputGain = 0.72f;
    private const float ExistingLoopGainDuringOverdub = 0.94f;

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly float[] _buffer;
    // Segundo buffer reservado una sola vez para poder deshacer el último overdub
    // sin asignar memoria dentro del callback ASIO.
    private readonly float[] _undoBuffer;
    private readonly object _sync = new();

    private int _lengthSamples;
    private int _positionSamples;
    private int _recording;
    private int _playing;
    private int _overdubbing;
    private int _captureInProgress;
    private int _autoCompleted;
    private int _tempoCompleted;
    private int _targetFirstPassSamples;
    private int _undoLengthSamples;
    private int _undoAvailable;

    public PracticeLooper(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _buffer = new float[checked(MaximumSeconds * sampleRate * channels)];
        _undoBuffer = new float[_buffer.Length];
    }

    public bool IsRecording => Volatile.Read(ref _recording) != 0;
    public bool IsPlaying => Volatile.Read(ref _playing) != 0;
    public bool IsOverdubbing => Volatile.Read(ref _overdubbing) != 0;
    public bool HasLoop => Volatile.Read(ref _lengthSamples) > 0;
    public bool AutoCompleted => Volatile.Read(ref _autoCompleted) != 0;
    public bool TempoCompleted => Volatile.Read(ref _tempoCompleted) != 0;
    public bool CanUndoOverdub => Volatile.Read(ref _undoAvailable) != 0;
    public bool IsTempoSyncedRecording => IsRecording && Volatile.Read(ref _targetFirstPassSamples) > 0;
    public int MaximumLoopSeconds => MaximumSeconds;
    public double LoopSeconds => Volatile.Read(ref _lengthSamples) / (double)(_sampleRate * _channels);

    public void StartFirstPass()
    {
        StartFirstPassCore(targetSamples: 0);
    }

    public double StartFirstPassSynced(float bpm, int beatsPerBar, int bars)
    {
        bpm = Math.Clamp(float.IsFinite(bpm) ? bpm : 80f, 40f, 240f);
        beatsPerBar = Math.Clamp(beatsPerBar, 2, 12);
        bars = Math.Clamp(bars, 1, 8);

        double seconds = (60.0 / bpm) * beatsPerBar * bars;
        int frames = Math.Max(1, (int)Math.Round(seconds * _sampleRate));
        int targetSamples = checked(frames * _channels);
        targetSamples = Math.Clamp(targetSamples, _sampleRate * _channels / 8, _buffer.Length);
        targetSamples -= targetSamples % _channels;

        StartFirstPassCore(targetSamples);
        return targetSamples / (double)(_sampleRate * _channels);
    }

    private void StartFirstPassCore(int targetSamples)
    {
        StopRealtimeActivityAndWait();
        lock (_sync)
        {
            int previousLength = Math.Clamp(Volatile.Read(ref _lengthSamples), 0, _buffer.Length);
            if (previousLength > 0)
            {
                Array.Clear(_buffer, 0, previousLength);
            }

            Volatile.Write(ref _lengthSamples, 0);
            Volatile.Write(ref _positionSamples, 0);
            Volatile.Write(ref _autoCompleted, 0);
            Volatile.Write(ref _tempoCompleted, 0);
            Volatile.Write(ref _targetFirstPassSamples, Math.Clamp(targetSamples, 0, _buffer.Length));
            ClearUndoSnapshot();
            Volatile.Write(ref _recording, 1);
        }
    }

    /// <summary>Finaliza la primera pasada y comienza a reproducir desde el inicio.</summary>
    public bool FinishFirstPassAndPlay()
    {
        Volatile.Write(ref _recording, 0);
        SpinWait.SpinUntil(() => Volatile.Read(ref _captureInProgress) == 0, 100);

        int length = Math.Clamp(Volatile.Read(ref _positionSamples), 0, _buffer.Length);
        length -= length % _channels;
        if (length < _sampleRate * _channels / 8) // menos de 125 ms: se considera accidental
        {
            Clear();
            return false;
        }

        Volatile.Write(ref _lengthSamples, length);
        Volatile.Write(ref _positionSamples, 0);
        Volatile.Write(ref _overdubbing, 0);
        Volatile.Write(ref _playing, 1);
        Volatile.Write(ref _autoCompleted, 0);
        Volatile.Write(ref _tempoCompleted, 0);
        Volatile.Write(ref _targetFirstPassSamples, 0);
        return true;
    }

    public bool TogglePlayback()
    {
        if (!HasLoop || IsRecording) return false;

        if (IsPlaying)
        {
            Volatile.Write(ref _playing, 0);
            Volatile.Write(ref _overdubbing, 0);
            return false;
        }

        Volatile.Write(ref _positionSamples, 0);
        Volatile.Write(ref _playing, 1);
        return true;
    }

    public bool ToggleOverdub()
    {
        if (!HasLoop || IsRecording) return false;

        if (!IsPlaying)
        {
            Volatile.Write(ref _positionSamples, 0);
            Volatile.Write(ref _playing, 1);
        }

        bool enable = !IsOverdubbing;
        if (enable)
        {
            CaptureUndoSnapshot();
        }
        Volatile.Write(ref _overdubbing, enable ? 1 : 0);
        return enable;
    }

    public bool UndoLastOverdub()
    {
        if (!HasLoop || Volatile.Read(ref _undoAvailable) == 0) return false;

        bool resumePlaying = IsPlaying;
        int resumePosition = Math.Max(0, Volatile.Read(ref _positionSamples));
        Volatile.Write(ref _overdubbing, 0);
        Volatile.Write(ref _playing, 0);
        SpinWait.SpinUntil(() => Volatile.Read(ref _captureInProgress) == 0, 100);

        lock (_sync)
        {
            int currentLength = Math.Clamp(Volatile.Read(ref _lengthSamples), 0, _buffer.Length);
            int undoLength = Math.Clamp(Volatile.Read(ref _undoLengthSamples), 0, _undoBuffer.Length);
            if (undoLength <= 0 || undoLength != currentLength)
            {
                ClearUndoSnapshot();
                if (resumePlaying && currentLength > 0) Volatile.Write(ref _playing, 1);
                return false;
            }

            Array.Copy(_undoBuffer, _buffer, undoLength);
            ClearUndoSnapshot();
            Volatile.Write(ref _positionSamples, undoLength > 0 ? resumePosition % undoLength : 0);
            if (resumePlaying) Volatile.Write(ref _playing, 1);
            return true;
        }
    }

    public void Clear()
    {
        StopRealtimeActivityAndWait();
        lock (_sync)
        {
            int length = Math.Clamp(Volatile.Read(ref _lengthSamples), 0, _buffer.Length);
            if (length > 0)
            {
                Array.Clear(_buffer, 0, length);
            }
            Volatile.Write(ref _lengthSamples, 0);
            Volatile.Write(ref _positionSamples, 0);
            Volatile.Write(ref _autoCompleted, 0);
            Volatile.Write(ref _tempoCompleted, 0);
            Volatile.Write(ref _targetFirstPassSamples, 0);
            ClearUndoSnapshot();
        }
    }

    /// <summary>
    /// Procesa el loop usando una fuente de captura separada de la mezcla final.
    /// captureSource contiene sólo la guitarra procesada: batería, bajo y voz pueden
    /// sonar como guía sin quedar impresos ni acumularse en cada overdub.
    /// Durante reproducción el loop se suma a la salida física y, si corresponde, a Meet.
    /// </summary>
    public void Process(float[] captureSource, float[] physicalOutput, float[]? meetOutput, int frames)
    {
        bool recording = IsRecording;
        bool playing = IsPlaying;
        if (!recording && !playing) return;

        Interlocked.Increment(ref _captureInProgress);
        try
        {
            int sampleCount = Math.Min(frames * _channels, Math.Min(physicalOutput.Length, captureSource.Length));
            int position = Volatile.Read(ref _positionSamples);

            if (recording)
            {
                int target = Volatile.Read(ref _targetFirstPassSamples);
                int recordingLimit = target > 0 ? Math.Min(target, _buffer.Length) : _buffer.Length;
                int remaining = recordingLimit - position;
                int count = Math.Min(sampleCount, Math.Max(0, remaining));
                for (int i = 0; i < count; i++)
                {
                    _buffer[position + i] = Sanitize(captureSource[i]);
                }
                position += count;
                Volatile.Write(ref _positionSamples, position);

                if (position >= recordingLimit)
                {
                    int length = recordingLimit - (recordingLimit % _channels);
                    Volatile.Write(ref _lengthSamples, length);
                    Volatile.Write(ref _recording, 0);
                    Volatile.Write(ref _playing, 1);
                    Volatile.Write(ref _overdubbing, 0);
                    Volatile.Write(ref _autoCompleted, target <= 0 ? 1 : 0);
                    Volatile.Write(ref _tempoCompleted, target > 0 ? 1 : 0);
                    Volatile.Write(ref _targetFirstPassSamples, 0);

                    // Si el final exacto cae dentro de un callback, reproducimos el
                    // comienzo del loop en las muestras restantes del mismo bloque.
                    // Esto evita un hueco de hasta un buffer ASIO en el empalme.
                    int playPosition = 0;
                    for (int i = count; i < sampleCount; i++)
                    {
                        if (playPosition >= length) playPosition = 0;
                        float loop = Sanitize(_buffer[playPosition]);
                        physicalOutput[i] = LimitFinalMix(Sanitize(physicalOutput[i]) + (loop * PlaybackGain));
                        if (meetOutput is not null && i < meetOutput.Length)
                        {
                            meetOutput[i] = LimitFinalMix(Sanitize(meetOutput[i]) + (loop * PlaybackGain));
                        }
                        playPosition++;
                    }
                    Volatile.Write(ref _positionSamples, playPosition);
                }
                return;
            }

            int lengthSamples = Volatile.Read(ref _lengthSamples);
            if (lengthSamples <= 0)
            {
                Volatile.Write(ref _playing, 0);
                Volatile.Write(ref _overdubbing, 0);
                return;
            }

            bool overdubbing = IsOverdubbing;
            for (int i = 0; i < sampleCount; i++)
            {
                if (position >= lengthSamples) position = 0;

                float liveOutput = Sanitize(physicalOutput[i]);
                float overdubInput = Sanitize(captureSource[i]);
                float loop = Sanitize(_buffer[position]);

                if (overdubbing)
                {
                    _buffer[position] = ClampAudio((loop * ExistingLoopGainDuringOverdub) + (overdubInput * OverdubInputGain));
                    loop = _buffer[position];
                }

                physicalOutput[i] = LimitFinalMix(liveOutput + (loop * PlaybackGain));
                if (meetOutput is not null && i < meetOutput.Length)
                {
                    meetOutput[i] = LimitFinalMix(Sanitize(meetOutput[i]) + (loop * PlaybackGain));
                }

                position++;
            }

            Volatile.Write(ref _positionSamples, position);
        }
        finally
        {
            Interlocked.Decrement(ref _captureInProgress);
        }
    }

    public async Task<string?> SaveAsync()
    {
        int length = Math.Clamp(Volatile.Read(ref _lengthSamples), 0, _buffer.Length);
        length -= length % _channels;
        if (length <= 0) return null;

        // Evitamos que un overdub modifique la memoria mientras se obtiene la copia.
        Volatile.Write(ref _overdubbing, 0);
        float[] snapshot = new float[length];
        Array.Copy(_buffer, snapshot, length);
        string path = BuildOutputPath();
        await Task.Run(() => WriteFloatSnapshotAsPcm16Wave(path, snapshot)).ConfigureAwait(false);
        return path;
    }

    private void CaptureUndoSnapshot()
    {
        int length = Math.Clamp(Volatile.Read(ref _lengthSamples), 0, _buffer.Length);
        if (length <= 0)
        {
            ClearUndoSnapshot();
            return;
        }

        // En este momento el overdub todavía está desactivado; el callback sólo lee
        // el loop, por lo que la copia es segura y no bloquea el hilo de audio.
        Array.Copy(_buffer, _undoBuffer, length);
        Volatile.Write(ref _undoLengthSamples, length);
        Volatile.Write(ref _undoAvailable, 1);
    }

    private void ClearUndoSnapshot()
    {
        Volatile.Write(ref _undoLengthSamples, 0);
        Volatile.Write(ref _undoAvailable, 0);
    }

    private void StopRealtimeActivityAndWait()
    {
        Volatile.Write(ref _recording, 0);
        Volatile.Write(ref _playing, 0);
        Volatile.Write(ref _overdubbing, 0);
        SpinWait.SpinUntil(() => Volatile.Read(ref _captureInProgress) == 0, 100);
    }

    private static float Sanitize(float value) => float.IsFinite(value) ? value : 0f;

    private static float ClampAudio(float value) => Math.Clamp(Sanitize(value), -0.98f, 0.98f);

    // Limitador suave sólo para la suma loop + directo. Por debajo de 0,90 no toca la señal.
    private static float LimitFinalMix(float value)
    {
        value = Sanitize(value);
        float abs = MathF.Abs(value);
        if (abs <= 0.90f) return value;
        float excess = abs - 0.90f;
        float compressed = 0.90f + (0.08f * (1f - MathF.Exp(-excess * 5f)));
        return MathF.CopySign(MathF.Min(compressed, 0.98f), value);
    }

    private static string BuildOutputPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string folder = Path.Combine(root, "GDM Amp Accessible", "Loops");
        Directory.CreateDirectory(folder);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        return Path.Combine(folder, $"Loop_{stamp}.wav");
    }

    private void WriteFloatSnapshotAsPcm16Wave(string path, float[] samples)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024,
            FileOptions.SequentialScan);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        int blockAlign = _channels * sizeof(short);
        int byteRate = _sampleRate * blockAlign;
        int dataBytes = checked(samples.Length * sizeof(short));

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

        for (int i = 0; i < samples.Length; i++)
        {
            float sample = Math.Clamp(Sanitize(samples[i]), -1f, 1f);
            writer.Write((short)MathF.Round(sample * 32767f));
        }
    }
}
