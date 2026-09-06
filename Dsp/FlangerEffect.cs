using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Flanger mono previo al chorus dentro del loop. Usa un buffer circular con lectura
/// fraccional defensiva y fundido de bypass para no introducir clics ni excepciones.
/// </summary>
internal sealed class FlangerEffect
{
    private readonly float[] _buffer;
    private readonly int _sampleRate;
    private int _writeIndex;
    private float _sinPhase;
    private float _cosPhase = 1f;
    private float _sinIncrement;
    private float _cosIncrement = 1f;
    private int _normalizationCounter;
    private float _rateHz = 0.35f;
    private float _depthPercent = 62f;
    private float _feedbackPercent = 28f;
    private float _mixPercent = 42f;
    private FlangerCharacter _character = FlangerCharacter.Classic;
    private bool _enabled;
    private float _mixCurrent;
    private float _mixTarget;
    private readonly float _mixRampStep;

    public FlangerEffect(int sampleRate)
    {
        _sampleRate = sampleRate;
        _buffer = new float[Math.Max(64, (int)(sampleRate * 0.030f) + 16)];
        _mixRampStep = 1f / MathF.Max(1f, sampleRate * 0.015f);
        UpdateIncrement();
    }

    public void Configure(bool enabled, FlangerCharacter character, float rateHz, float depthPercent, float feedbackPercent, float mixPercent)
    {
        _enabled = enabled;
        _character = character;
        _mixTarget = enabled ? 1f : 0f;
        float newRate = float.IsFinite(rateHz) ? Math.Clamp(rateHz, 0.05f, 5f) : 0.35f;
        if (MathF.Abs(newRate - _rateHz) > 0.0001f)
        {
            _rateHz = newRate;
            UpdateIncrement();
        }
        _depthPercent = float.IsFinite(depthPercent) ? Math.Clamp(depthPercent, 0f, 100f) : 62f;
        _feedbackPercent = float.IsFinite(feedbackPercent) ? Math.Clamp(feedbackPercent, -70f, 70f) : 28f;
        _mixPercent = float.IsFinite(mixPercent) ? Math.Clamp(mixPercent, 0f, 85f) : 42f;
    }

    public float Process(float input)
    {
        input = Sanitize(input);
        AdvanceMix();
        if (_mixCurrent <= 0.0001f && !_enabled)
        {
            return input;
        }

        if (!float.IsFinite(_sinPhase) || !float.IsFinite(_cosPhase))
        {
            _sinPhase = 0f;
            _cosPhase = 1f;
            _normalizationCounter = 0;
        }
        float lfo = 0.5f + (0.5f * _sinPhase);

        float depth = _depthPercent / 100f;
        float baseDelayMs = _character switch
        {
            FlangerCharacter.Jet => 0.45f,
            FlangerCharacter.TapeZero => 0.18f,
            _ => 0.75f
        };
        float sweepScale = _character switch
        {
            FlangerCharacter.Jet => 7.2f,
            FlangerCharacter.TapeZero => 3.6f,
            _ => 5.35f
        };
        float sweepMs = 0.35f + (depth * sweepScale);
        float delaySamples = (baseDelayMs + (lfo * sweepMs)) * 0.001f * _sampleRate;
        delaySamples = Math.Clamp(delaySamples, 1f, _buffer.Length - 3f);

        float delayed = ReadFractional(delaySamples);
        float feedback = _feedbackPercent / 100f;
        if (_character == FlangerCharacter.Jet) feedback = Math.Clamp(feedback * 1.22f, -0.78f, 0.78f);
        if (_character == FlangerCharacter.TapeZero) feedback *= 0.45f;
        float write = SoftLimit(input + (delayed * feedback));
        _buffer[_writeIndex] = write;
        _writeIndex++;
        if (_writeIndex >= _buffer.Length) _writeIndex = 0;
        AdvanceLfo();

        float wet = _mixPercent / 100f;
        float flanged = SoftLimit((input * (1f - wet)) + (delayed * wet));
        return Sanitize((input * (1f - _mixCurrent)) + (flanged * _mixCurrent));
    }

    private float ReadFractional(float delaySamples)
    {
        if (!float.IsFinite(delaySamples)) delaySamples = 1f;
        delaySamples = Math.Clamp(delaySamples, 1f, _buffer.Length - 3f);
        float readPosition = _writeIndex - delaySamples;
        while (readPosition < 0f) readPosition += _buffer.Length;
        while (readPosition >= _buffer.Length) readPosition -= _buffer.Length;

        int index0 = (int)MathF.Floor(readPosition);
        if ((uint)index0 >= (uint)_buffer.Length) index0 = 0;
        int index1 = index0 + 1;
        if (index1 >= _buffer.Length) index1 = 0;
        float fraction = readPosition - MathF.Floor(readPosition);
        return Sanitize(_buffer[index0] + ((_buffer[index1] - _buffer[index0]) * fraction));
    }

    private void AdvanceMix()
    {
        if (_mixCurrent < _mixTarget) _mixCurrent = MathF.Min(_mixTarget, _mixCurrent + _mixRampStep);
        else if (_mixCurrent > _mixTarget) _mixCurrent = MathF.Max(_mixTarget, _mixCurrent - _mixRampStep);
    }

    public void Reset()
    {
        Array.Clear(_buffer);
        _writeIndex = 0;
        _sinPhase = 0f;
        _cosPhase = 1f;
        _normalizationCounter = 0;
        _mixCurrent = _enabled ? 1f : 0f;
        _mixTarget = _mixCurrent;
    }

    private void AdvanceLfo()
    {
        float sin = _sinPhase;
        float cos = _cosPhase;
        _sinPhase = (sin * _cosIncrement) + (cos * _sinIncrement);
        _cosPhase = (cos * _cosIncrement) - (sin * _sinIncrement);

        if (++_normalizationCounter >= 4096)
        {
            float magnitude = MathF.Sqrt((_sinPhase * _sinPhase) + (_cosPhase * _cosPhase));
            if (magnitude > 0.0001f && float.IsFinite(magnitude))
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

    private static float Sanitize(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, -1.35f, 1.35f) : 0f;

    private static float SoftLimit(float value)
    {
        if (!float.IsFinite(value)) return 0f;
        float x = Math.Clamp(value, -2.5f, 2.5f);
        float x2 = x * x;
        return Math.Clamp(x * (27f + x2) / (27f + (9f * x2)), -1.2f, 1.2f);
    }
}
