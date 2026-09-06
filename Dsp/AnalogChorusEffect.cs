namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Chorus analógico cálido de una voz modulada por canal, inspirado en el carácter
/// de pedales analógicos clásicos. Usa filtrado de la ruta húmeda y LFO desfasado
/// para dar anchura sin sumar asignaciones ni acceso a disco en el callback ASIO.
/// </summary>
internal sealed class AnalogChorusEffect
{
    private readonly int _sampleRate;
    private readonly float[] _bufferLeft;
    private readonly float[] _bufferRight;
    private readonly Biquad _wetLowLeft = new();
    private readonly Biquad _wetLowRight = new();
    private readonly Biquad _wetHighLeft = new();
    private readonly Biquad _wetHighRight = new();
    private int _writeIndex;
    private int _normalizationCounter;
    private bool _enabled;
    private float _rateHz = 0.65f;
    private float _depth = 6.5f;
    private float _targetMix = 0.42f;
    private float _currentMix;
    private float _sinPhase;
    private float _cosPhase = 1f;
    private float _sinIncrement;
    private float _cosIncrement = 1f;
    private readonly float _mixRampStep;
    private float _low = 5f;
    private float _high = 5f;

    public AnalogChorusEffect(int sampleRate)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        _bufferLeft = new float[(int)(_sampleRate * 0.045f) + 8];
        _bufferRight = new float[_bufferLeft.Length];
        _mixRampStep = 1f / MathF.Max(1f, _sampleRate * 0.020f);
        UpdateIncrement();
        UpdateTone();
    }

    public void Configure(bool enabled, float rateHz, float depth, float mixPercent, float low, float high)
    {
        float safeRate = float.IsFinite(rateHz) ? rateHz : 0.65f;
        float newRate = Math.Clamp(safeRate, 0.05f, 3f);
        if (MathF.Abs(newRate - _rateHz) > 0.0001f)
        {
            _rateHz = newRate;
            UpdateIncrement();
        }

        _enabled = enabled;
        _depth = Math.Clamp(float.IsFinite(depth) ? depth : 6.5f, 0f, 10f);
        _targetMix = enabled ? Math.Clamp((float.IsFinite(mixPercent) ? mixPercent : 42f) / 100f, 0f, 0.85f) : 0f;

        float newLow = Math.Clamp(float.IsFinite(low) ? low : 5f, 0f, 10f);
        float newHigh = Math.Clamp(float.IsFinite(high) ? high : 5f, 0f, 10f);
        if (MathF.Abs(newLow - _low) > 0.0001f || MathF.Abs(newHigh - _high) > 0.0001f)
        {
            _low = newLow;
            _high = newHigh;
            UpdateTone();
        }
    }

    public void Process(float inputLeft, float inputRight, out float left, out float right)
    {
        inputLeft = float.IsFinite(inputLeft) ? Math.Clamp(inputLeft, -1.25f, 1.25f) : 0f;
        inputRight = float.IsFinite(inputRight) ? Math.Clamp(inputRight, -1.25f, 1.25f) : 0f;
        _bufferLeft[_writeIndex] = inputLeft;
        _bufferRight[_writeIndex] = inputRight;
        AdvanceMix();

        if (_currentMix <= 0.0001f)
        {
            left = inputLeft;
            right = inputRight;
            Advance();
            return;
        }

        // El retardo base es corto, como en un chorus BBD. La profundidad de 0 a 10
        // se traduce a aproximadamente 0 a 5,8 ms de modulación adicional.
        float depthSamples = _sampleRate * ((_depth * 0.58f) / 1000f);
        float baseSamples = _sampleRate * 0.0105f;
        float lfoLeft = _sinPhase;
        float lfoRight = (-0.82f * _sinPhase) + (0.57f * _cosPhase);

        float wetLeft = ReadFractional(_bufferLeft, baseSamples + (lfoLeft * depthSamples));
        float wetRight = ReadFractional(_bufferRight, baseSamples + (lfoRight * depthSamples));

        // Banda húmeda más cálida: leve recorte de extremos y ecualización grave/aguda.
        wetLeft = _wetHighLeft.Process(_wetLowLeft.Process(wetLeft));
        wetRight = _wetHighRight.Process(_wetLowRight.Process(wetRight));

        float dryGain = 1f - (_currentMix * 0.38f);
        float wetGain = _currentMix * 1.48f;
        left = SoftLimit((inputLeft * dryGain) + (wetLeft * wetGain));
        right = SoftLimit((inputRight * dryGain) + (wetRight * wetGain));

        Advance();
    }

    private void UpdateTone()
    {
        // Centro en 5 = prácticamente neutro. Los extremos permiten recortar o realzar
        // suavemente la ruta húmeda sin convertir el efecto en un ecualizador agresivo.
        float lowDb = (_low - 5f) * 1.2f;
        float highDb = (_high - 5f) * 1.2f;
        _wetLowLeft.SetLowShelf(_sampleRate, 420f, lowDb);
        _wetLowRight.SetLowShelf(_sampleRate, 420f, lowDb);
        _wetHighLeft.SetHighShelf(_sampleRate, 3200f, highDb - 1.5f);
        _wetHighRight.SetHighShelf(_sampleRate, 3200f, highDb - 1.5f);
    }

    private void AdvanceMix()
    {
        float target = _enabled ? _targetMix : 0f;
        if (_currentMix < target)
        {
            _currentMix = MathF.Min(target, _currentMix + _mixRampStep);
        }
        else if (_currentMix > target)
        {
            _currentMix = MathF.Max(target, _currentMix - _mixRampStep);
        }
    }

    private void Advance()
    {
        _writeIndex++;
        if (_writeIndex >= _bufferLeft.Length) _writeIndex = 0;

        float sin = _sinPhase;
        float cos = _cosPhase;
        _sinPhase = (sin * _cosIncrement) + (cos * _sinIncrement);
        _cosPhase = (cos * _cosIncrement) - (sin * _sinIncrement);

        if (!float.IsFinite(_sinPhase) || !float.IsFinite(_cosPhase))
        {
            _sinPhase = 0f;
            _cosPhase = 1f;
            _normalizationCounter = 0;
            return;
        }

        if (++_normalizationCounter >= 4096)
        {
            float magnitude = MathF.Sqrt((_sinPhase * _sinPhase) + (_cosPhase * _cosPhase));
            if (magnitude > 0.0001f)
            {
                _sinPhase /= magnitude;
                _cosPhase /= magnitude;
            }
            else
            {
                _sinPhase = 0f;
                _cosPhase = 1f;
            }
            _normalizationCounter = 0;
        }
    }

    private void UpdateIncrement()
    {
        float increment = 2f * MathF.PI * _rateHz / _sampleRate;
        _sinIncrement = MathF.Sin(increment);
        _cosIncrement = MathF.Cos(increment);
    }

    private float ReadFractional(float[] buffer, float delaySamples)
    {
        if (!float.IsFinite(delaySamples)) delaySamples = 1f;
        delaySamples = Math.Clamp(delaySamples, 1f, buffer.Length - 3f);
        float readPosition = (_writeIndex - delaySamples) % buffer.Length;
        if (readPosition < 0f) readPosition += buffer.Length;
        if (!float.IsFinite(readPosition)) readPosition = _writeIndex;

        int indexA = (int)readPosition;
        if ((uint)indexA >= (uint)buffer.Length) indexA = _writeIndex;
        int indexB = indexA + 1;
        if (indexB >= buffer.Length) indexB = 0;
        float fraction = readPosition - indexA;
        if (!float.IsFinite(fraction) || fraction < 0f || fraction > 1f) fraction = 0f;
        return buffer[indexA] + ((buffer[indexB] - buffer[indexA]) * fraction);
    }

    private static float SoftLimit(float value)
    {
        if (!float.IsFinite(value)) return 0f;
        float x = Math.Clamp(value * 0.90f, -3f, 3f);
        float squared = x * x;
        float tanh = x * (27f + squared) / (27f + (9f * squared));
        return tanh / 0.7299213f;
    }

    public void Reset()
    {
        Array.Clear(_bufferLeft);
        Array.Clear(_bufferRight);
        _writeIndex = 0;
        _normalizationCounter = 0;
        _sinPhase = 0f;
        _cosPhase = 1f;
        _currentMix = 0f;
        _wetLowLeft.Reset();
        _wetLowRight.Reset();
        _wetHighLeft.Reset();
        _wetHighRight.Reset();
    }
}
