using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Afinador cromático basado en YIN. El callback ASIO solamente copia muestras a un
/// búfer circular. Todo el análisis se realiza en un hilo de baja prioridad y la interfaz
/// lee el último resultado disponible, evitando pausas periódicas en el audio.
/// </summary>
internal sealed class TunerAnalyzer : IDisposable
{
    private const int DownsampleFactor = 4;
    private const int AnalysisSamples = 2048;
    private const int RawSamples = AnalysisSamples * DownsampleFactor;
    private const float MinimumFrequency = 60f;
    private const float MaximumFrequency = 1300f;
    private const float YinThreshold = 0.16f;

    private readonly int _analysisRate;
    private readonly float[] _ring = new float[32768];
    private readonly float[] _analysis = new float[AnalysisSamples];
    private readonly float[] _difference = new float[384];
    private readonly float[] _cmnd = new float[384];
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _worker;

    private int _writeIndex;
    private int _enabled;
    private int _referenceABits = BitConverter.SingleToInt32Bits(440f);
    private int _resetRequested;
    private volatile bool _disposed;
    private float _smoothedFrequency;
    private TunerReading _latestReading = TunerReading.NoSignal;

    public TunerAnalyzer(int sampleRate)
    {
        _analysisRate = sampleRate / DownsampleFactor;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "GDM Afinador",
            Priority = ThreadPriority.BelowNormal
        };
        _worker.Start();
    }

    public void SetEnabled(bool enabled, float referenceAHz)
    {
        SetReference(referenceAHz);
        int newValue = enabled ? 1 : 0;
        int previous = Interlocked.Exchange(ref _enabled, newValue);
        if (previous != newValue)
        {
            Interlocked.Exchange(ref _resetRequested, 1);
            if (!enabled)
            {
                Volatile.Write(ref _latestReading, TunerReading.NoSignal);
            }
        }
        _wake.Set();
    }

    public void SetReference(float referenceAHz)
    {
        float clamped = Math.Clamp(referenceAHz, 430f, 450f);
        Volatile.Write(ref _referenceABits, BitConverter.SingleToInt32Bits(clamped));
    }

    public void AddSamples(float[] input, int count)
    {
        if (Volatile.Read(ref _enabled) == 0)
        {
            return;
        }

        int index = Volatile.Read(ref _writeIndex);
        int length = _ring.Length;
        int safeCount = Math.Min(count, input.Length);

        for (int i = 0; i < safeCount; i++)
        {
            float sample = input[i];
            _ring[index] = float.IsFinite(sample) ? sample : 0f;
            index++;
            if (index >= length)
            {
                index = 0;
            }
        }

        Volatile.Write(ref _writeIndex, index);
    }

    public TunerReading GetLatestReading(float referenceAHz)
    {
        SetReference(referenceAHz);
        return Volatile.Read(ref _latestReading);
    }

    private void WorkerLoop()
    {
        while (!_disposed)
        {
            int waitMilliseconds = Volatile.Read(ref _enabled) != 0 ? 300 : Timeout.Infinite;
            _wake.WaitOne(waitMilliseconds);
            if (_disposed)
            {
                break;
            }

            if (Interlocked.Exchange(ref _resetRequested, 0) != 0)
            {
                _smoothedFrequency = 0f;
            }

            if (Volatile.Read(ref _enabled) == 0)
            {
                continue;
            }

            try
            {
                float referenceAHz = BitConverter.Int32BitsToSingle(Volatile.Read(ref _referenceABits));
                TunerReading reading = AnalyzeCore(referenceAHz);
                Volatile.Write(ref _latestReading, reading);
            }
            catch
            {
                _smoothedFrequency = 0f;
                Volatile.Write(ref _latestReading, TunerReading.NoSignal);
            }
        }
    }

    private TunerReading AnalyzeCore(float referenceAHz)
    {
        CopyAndDownsample();

        float mean = 0f;
        for (int i = 0; i < AnalysisSamples; i++)
        {
            mean += _analysis[i];
        }
        mean /= AnalysisSamples;

        float energy = 0f;
        float previousInput = 0f;
        float previousOutput = 0f;
        const float twoPi = 2f * MathF.PI;
        for (int i = 0; i < AnalysisSamples; i++)
        {
            float centered = _analysis[i] - mean;
            float highPassed = centered - previousInput + (0.995f * previousOutput);
            previousInput = centered;
            previousOutput = highPassed;

            float window = 0.5f - (0.5f * MathF.Cos(twoPi * i / (AnalysisSamples - 1)));
            float value = highPassed * window;
            _analysis[i] = value;
            energy += value * value;
        }

        float rms = MathF.Sqrt(energy / AnalysisSamples);
        if (!float.IsFinite(rms) || rms < 0.0012f)
        {
            _smoothedFrequency = 0f;
            return TunerReading.NoSignal;
        }

        int minimumTau = Math.Max(2, (int)(_analysisRate / MaximumFrequency));
        int maximumTau = Math.Min(_difference.Length - 2,
            Math.Min((int)(_analysisRate / MinimumFrequency), AnalysisSamples / 2));
        int comparisonLength = AnalysisSamples - maximumTau;

        Array.Clear(_difference, 0, maximumTau + 2);
        Array.Clear(_cmnd, 0, maximumTau + 2);

        for (int tau = 1; tau <= maximumTau; tau++)
        {
            double sum = 0.0;
            for (int index = 0; index < comparisonLength; index++)
            {
                float delta = _analysis[index] - _analysis[index + tau];
                sum += delta * delta;
            }
            _difference[tau] = (float)sum;
        }

        _cmnd[0] = 1f;
        float runningSum = 0f;
        for (int tau = 1; tau <= maximumTau; tau++)
        {
            runningSum += _difference[tau];
            _cmnd[tau] = runningSum > 1e-12f ? (_difference[tau] * tau / runningSum) : 1f;
        }

        int selectedTau = -1;
        for (int tau = minimumTau; tau < maximumTau; tau++)
        {
            if (_cmnd[tau] < YinThreshold)
            {
                while (tau + 1 <= maximumTau && _cmnd[tau + 1] < _cmnd[tau])
                {
                    tau++;
                }
                selectedTau = tau;
                break;
            }
        }

        if (selectedTau < 0)
        {
            float best = 1f;
            for (int tau = minimumTau; tau <= maximumTau; tau++)
            {
                if (_cmnd[tau] < best)
                {
                    best = _cmnd[tau];
                    selectedTau = tau;
                }
            }
        }

        if (selectedTau <= 1 || selectedTau >= maximumTau)
        {
            _smoothedFrequency = 0f;
            return TunerReading.NoSignal;
        }

        float confidence = 1f - _cmnd[selectedTau];
        if (!float.IsFinite(confidence) || confidence < 0.62f)
        {
            _smoothedFrequency = 0f;
            return TunerReading.NoSignal;
        }

        float refinedTau = ParabolicInterpolation(selectedTau);
        float frequency = _analysisRate / refinedTau;
        if (!float.IsFinite(frequency) || frequency < MinimumFrequency || frequency > MaximumFrequency)
        {
            _smoothedFrequency = 0f;
            return TunerReading.NoSignal;
        }

        frequency = StabilizeFrequency(frequency);
        return BuildReading(frequency, referenceAHz, confidence);
    }

    private void CopyAndDownsample()
    {
        int end = Volatile.Read(ref _writeIndex);
        int start = end - RawSamples;
        while (start < 0)
        {
            start += _ring.Length;
        }

        int ringIndex = start;
        for (int outputIndex = 0; outputIndex < AnalysisSamples; outputIndex++)
        {
            float sum = 0f;
            for (int part = 0; part < DownsampleFactor; part++)
            {
                sum += _ring[ringIndex];
                ringIndex++;
                if (ringIndex >= _ring.Length)
                {
                    ringIndex = 0;
                }
            }
            _analysis[outputIndex] = sum / DownsampleFactor;
        }
    }

    private float ParabolicInterpolation(int tau)
    {
        float left = _cmnd[tau - 1];
        float center = _cmnd[tau];
        float right = _cmnd[tau + 1];
        float denominator = left - (2f * center) + right;
        if (MathF.Abs(denominator) < 1e-9f)
        {
            return tau;
        }

        float offset = 0.5f * (left - right) / denominator;
        return tau + Math.Clamp(offset, -0.5f, 0.5f);
    }

    private float StabilizeFrequency(float frequency)
    {
        if (_smoothedFrequency <= 0f)
        {
            _smoothedFrequency = frequency;
            return frequency;
        }

        float centsDifference = 1200f * MathF.Log2(frequency / _smoothedFrequency);
        if (MathF.Abs(centsDifference) > 85f)
        {
            _smoothedFrequency = frequency;
        }
        else
        {
            _smoothedFrequency = (_smoothedFrequency * 0.68f) + (frequency * 0.32f);
        }

        return _smoothedFrequency;
    }

    private static TunerReading BuildReading(float frequency, float referenceAHz, float confidence)
    {
        float midiFloat = 69f + (12f * MathF.Log2(frequency / referenceAHz));
        int midiNote = (int)MathF.Round(midiFloat);
        float targetFrequency = referenceAHz * MathF.Pow(2f, (midiNote - 69) / 12f);
        float cents = 1200f * MathF.Log2(frequency / targetFrequency);

        string[] noteNames =
        {
            "Do", "Do sostenido", "Re", "Re sostenido", "Mi", "Fa",
            "Fa sostenido", "Sol", "Sol sostenido", "La", "La sostenido", "Si"
        };

        int noteIndex = ((midiNote % 12) + 12) % 12;
        int octave = (midiNote / 12) - 1;
        string noteName = noteNames[noteIndex];
        float absoluteCents = MathF.Abs(cents);

        TuningDirection direction;
        string description;
        if (absoluteCents <= 3f)
        {
            direction = TuningDirection.InTune;
            description = $"{noteName} {octave}, afinada. Desviación {absoluteCents:0} cents.";
        }
        else if (cents < 0f)
        {
            direction = TuningDirection.Flat;
            description = $"{noteName} {octave}, {absoluteCents:0} cents baja. Hay que subir la afinación.";
        }
        else
        {
            direction = TuningDirection.Sharp;
            description = $"{noteName} {octave}, {absoluteCents:0} cents alta. Hay que bajar la afinación.";
        }

        return new TunerReading
        {
            HasSignal = true,
            FrequencyHz = frequency,
            MidiNote = midiNote,
            NoteName = noteName,
            Octave = octave,
            Cents = cents,
            Confidence = confidence,
            Direction = direction,
            DisplayText = $"{description} Frecuencia {frequency:0.0} hercios."
        };
    }

    public void Reset()
    {
        Array.Clear(_ring);
        Volatile.Write(ref _writeIndex, 0);
        Volatile.Write(ref _latestReading, TunerReading.NoSignal);
        Interlocked.Exchange(ref _resetRequested, 1);
        _wake.Set();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _wake.Set();
        try
        {
            if (_worker.Join(1000))
            {
                _wake.Dispose();
            }
        }
        catch
        {
            // El hilo es de fondo; no bloqueamos el cierre si Windows ya está finalizando.
        }
    }
}
