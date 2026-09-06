namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Chorus estéreo de dos voces por canal. Está pensado para sonar más evidente que la
/// versión anterior usando menos lecturas de memoria, para reducir carga DSP.
/// </summary>
internal sealed class ChorusEffect
{
    private readonly int _sampleRate;
    private readonly float[] _buffer;
    private readonly Biquad _wetToneLeft = new();
    private readonly Biquad _wetToneRight = new();
    private int _writeIndex;
    private int _normalizationCounter;
    private bool _enabled;
    private float _rateHz = 0.8f;
    private float _depthMs = 7.5f;
    private float _targetMix = 0.45f;
    private float _currentMix;
    private readonly float _mixRampStep;
    private float _sinPhase;
    private float _cosPhase = 1f;
    private float _sinIncrement;
    private float _cosIncrement = 1f;

    private static readonly float Sin180 = MathF.Sin(MathF.PI);
    private static readonly float Cos180 = MathF.Cos(MathF.PI);
    private static readonly float Sin112 = MathF.Sin(1.12f);
    private static readonly float Cos112 = MathF.Cos(1.12f);
    private static readonly float Sin428 = MathF.Sin(4.28f);
    private static readonly float Cos428 = MathF.Cos(4.28f);
    private static readonly float SoftLimitNormalization = FastTanh(0.88f);

    public ChorusEffect(int sampleRate)
    {
        _sampleRate = sampleRate;
        _buffer = new float[(int)(sampleRate * 0.050f) + 8];
        _mixRampStep = 1f / MathF.Max(1f, sampleRate * 0.018f);
        _wetToneLeft.SetLowPass(sampleRate, 6500f, 0.70f);
        _wetToneRight.SetLowPass(sampleRate, 6500f, 0.70f);
        UpdateIncrement();
    }

    public void Configure(bool enabled, float rateHz, float depthMs, float mixPercent)
    {
        _enabled = enabled;
        // Los parámetros llegan desde la interfaz/escenas mientras el callback ASIO está
        // corriendo. Un float no finito no debe entrar nunca al oscilador, porque terminaría
        // convirtiéndose en una posición NaN del buffer circular.
        float safeRate = float.IsFinite(rateHz) ? rateHz : 0.8f;
        float newRate = Math.Clamp(safeRate, 0.1f, 5f);
        if (MathF.Abs(newRate - _rateHz) > 0.0001f)
        {
            _rateHz = newRate;
            UpdateIncrement();
        }

        float safeDepth = float.IsFinite(depthMs) ? depthMs : 7.5f;
        float safeMix = float.IsFinite(mixPercent) ? mixPercent : 45f;
        _depthMs = Math.Clamp(safeDepth, 0f, 11f);
        _targetMix = enabled ? Math.Clamp(safeMix / 100f, 0f, 0.90f) : 0f;
    }

    public void Process(float input, out float left, out float right)
    {
        input = float.IsFinite(input) ? Math.Clamp(input, -1.25f, 1.25f) : 0f;
        _buffer[_writeIndex] = input;
        AdvanceMix();

        if (_currentMix <= 0.0001f)
        {
            left = input;
            right = input;
            Advance();
            return;
        }

        float depthSamples = _sampleRate * (_depthMs / 1000f);
        float sin = _sinPhase;
        float cos = _cosPhase;

        // Voz principal: modulación opuesta entre izquierda y derecha. Esto produce una
        // sensación más clara de chorus sin necesidad de seis taps como en la versión previa.
        float tapL1 = ReadFractional((_sampleRate * 0.0082f) + (sin * depthSamples));
        float tapR1 = ReadFractional((_sampleRate * 0.0082f) +
            (SinWithOffset(sin, cos, Sin180, Cos180) * depthSamples));

        // Segunda voz más suave y desfasada para dar cuerpo de ensemble sin borrar el ataque.
        float tapL2 = ReadFractional((_sampleRate * 0.0146f) +
            (SinWithOffset(sin, cos, Sin112, Cos112) * depthSamples * 0.62f));
        float tapR2 = ReadFractional((_sampleRate * 0.0146f) +
            (SinWithOffset(sin, cos, Sin428, Cos428) * depthSamples * 0.62f));

        float wetLeft = _wetToneLeft.Process((tapL1 * 0.76f) + (tapL2 * 0.24f));
        float wetRight = _wetToneRight.Process((tapR1 * 0.76f) + (tapR2 * 0.24f));

        // La ruta húmeda tiene más presencia que antes. La señal seca se conserva para no
        // perder definición ni generar una sensación de vibrato puro.
        float dryGain = 1f - (_currentMix * 0.52f);
        float wetGain = _currentMix * 1.78f;
        left = SoftLimit((input * dryGain) + (wetLeft * wetGain));
        right = SoftLimit((input * dryGain) + (wetRight * wetGain));

        Advance();
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
        if (_writeIndex >= _buffer.Length)
        {
            _writeIndex = 0;
        }

        float sin = _sinPhase;
        float cos = _cosPhase;
        _sinPhase = (sin * _cosIncrement) + (cos * _sinIncrement);
        _cosPhase = (cos * _cosIncrement) - (sin * _sinIncrement);

        // Autorreparación del oscilador. En condiciones normales no entra nunca aquí y por
        // tanto no modifica el sonido. Evita que un NaN ocasional termine en un índice inválido
        // dentro de ReadFractional y silencie todos los callbacks posteriores.
        if (!float.IsFinite(_sinPhase) || !float.IsFinite(_cosPhase))
        {
            _sinPhase = 0f;
            _cosPhase = 1f;
            _normalizationCounter = 0;
            return;
        }

        _normalizationCounter++;
        if (_normalizationCounter >= 4096)
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

    private static float SinWithOffset(float sin, float cos, float sinOffset, float cosOffset) =>
        (sin * cosOffset) + (cos * sinOffset);

    private float ReadFractional(float delaySamples)
    {
        // Math.Clamp conserva NaN. Si un valor no finito llega hasta aquí, convertirlo a int
        // puede producir un índice inválido. El valor de respaldo sólo se usa ante un estado
        // anómalo; con parámetros normales la ruta matemática y el sonido permanecen iguales.
        if (!float.IsFinite(delaySamples))
        {
            delaySamples = 1f;
        }

        delaySamples = Math.Clamp(delaySamples, 1f, _buffer.Length - 3f);
        float readPosition = _writeIndex - delaySamples;

        // Envolver explícitamente en ambas direcciones. Esto hace al lector seguro aun si una
        // futura modificación permite retardos mayores que una vuelta completa del buffer.
        readPosition %= _buffer.Length;
        if (readPosition < 0f)
        {
            readPosition += _buffer.Length;
        }
        if (!float.IsFinite(readPosition))
        {
            readPosition = _writeIndex;
        }

        int indexA = (int)readPosition;
        if ((uint)indexA >= (uint)_buffer.Length)
        {
            indexA = _writeIndex;
            if ((uint)indexA >= (uint)_buffer.Length)
            {
                indexA = 0;
            }
            readPosition = indexA;
        }

        int indexB = indexA + 1;
        if (indexB >= _buffer.Length)
        {
            indexB = 0;
        }

        float fraction = readPosition - indexA;
        if (!float.IsFinite(fraction) || fraction < 0f || fraction > 1f)
        {
            fraction = 0f;
        }

        return _buffer[indexA] + ((_buffer[indexB] - _buffer[indexA]) * fraction);
    }

    private static float SoftLimit(float value)
    {
        if (!float.IsFinite(value))
        {
            return 0f;
        }

        return FastTanh(value * 0.88f) / SoftLimitNormalization;
    }

    private static float FastTanh(float value)
    {
        float x = Math.Clamp(value, -3f, 3f);
        float squared = x * x;
        return x * (27f + squared) / (27f + (9f * squared));
    }

    public void Reset()
    {
        Array.Clear(_buffer);
        _writeIndex = 0;
        _normalizationCounter = 0;
        _sinPhase = 0f;
        _cosPhase = 1f;
        _currentMix = 0f;
        _wetToneLeft.Reset();
        _wetToneRight.Reset();
    }
}
