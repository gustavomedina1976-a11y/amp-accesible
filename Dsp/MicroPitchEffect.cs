namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Ensanchador estéreo tipo Harmonizer/MicroPitch de fines de los 70/80.
/// Genera dos voces con pequeños desplazamientos de afinación opuestos y retardos
/// cortos diferentes. El pitch se obtiene con dos cabezales de lectura cruzados por voz,
/// sin asignaciones dentro del callback ASIO y sin el barrido periódico de un chorus.
/// </summary>
internal sealed class MicroPitchEffect
{
    private readonly float[] _buffer;
    private readonly int _sampleRate;
    private int _writeIndex;
    private bool _enabled;
    private float _mix = 0.38f;
    private float _detuneCents = 9f;
    private float _baseDelayMs = 10f;
    private double _phaseDown;
    private double _phaseUp = 0.37;
    private double _incDown;
    private double _incUp;
    private double _sweepSamples;

    public MicroPitchEffect(int sampleRate)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        _buffer = new float[Math.Max(2048, (int)(_sampleRate * 0.090f) + 8)];
    }

    public void Configure(bool enabled, float detuneCents, float delayMs, float mixPercent)
    {
        bool changed = _enabled != enabled;
        _enabled = enabled;
        _detuneCents = Math.Clamp(float.IsFinite(detuneCents) ? detuneCents : 9f, 2f, 24f);
        _baseDelayMs = Math.Clamp(float.IsFinite(delayMs) ? delayMs : 10f, 3f, 24f);
        _mix = Math.Clamp(float.IsFinite(mixPercent) ? mixPercent / 100f : 0.38f, 0f, 0.75f);
        _sweepSamples = Math.Max(96.0, _sampleRate * 0.026);
        double downRatio = Math.Pow(2.0, -_detuneCents / 1200.0);
        double upRatio = Math.Pow(2.0, _detuneCents / 1200.0);
        _incDown = Math.Max(1e-8, (1.0 - downRatio) / _sweepSamples);
        _incUp = Math.Max(1e-8, (upRatio - 1.0) / _sweepSamples);
        if (changed) Reset();
    }

    public void Process(float inputLeft, float inputRight, out float outputLeft, out float outputRight)
    {
        float mid = Sanitize((inputLeft + inputRight) * 0.5f);
        _buffer[_writeIndex] = mid;

        if (!_enabled || _mix <= 0f)
        {
            outputLeft = inputLeft;
            outputRight = inputRight;
            AdvanceWrite();
            return;
        }

        _phaseDown += _incDown;
        _phaseUp += _incUp;
        if (_phaseDown >= 1.0) _phaseDown -= 1.0;
        if (_phaseUp >= 1.0) _phaseUp -= 1.0;

        double baseLeft = _sampleRate * (_baseDelayMs * 0.001);
        double baseRight = _sampleRate * ((_baseDelayMs + 7.0f) * 0.001);
        float wetLeft = ReadPitchVoice(_phaseDown, baseLeft, _sweepSamples, increasingDelay: true);
        float wetRight = ReadPitchVoice(_phaseUp, baseRight, _sweepSamples, increasingDelay: false);

        // El dry queda en su canal original; las voces microafinadas abren los extremos.
        // Un pequeño cruce evita un centro hueco y mantiene compatibilidad mono razonable.
        float wetL = wetLeft * 0.92f + wetRight * 0.08f;
        float wetR = wetRight * 0.92f + wetLeft * 0.08f;
        float dry = 1f - _mix;
        outputLeft = Sanitize(inputLeft * dry + wetL * _mix);
        outputRight = Sanitize(inputRight * dry + wetR * _mix);
        AdvanceWrite();
    }

    private float ReadPitchVoice(double phase, double baseDelay, double sweep, bool increasingDelay)
    {
        double p1 = phase;
        double p2 = phase + 0.5;
        if (p2 >= 1.0) p2 -= 1.0;
        float w1 = TriangleWindow(p1);
        float w2 = TriangleWindow(p2);
        double d1 = baseDelay + (increasingDelay ? p1 : 1.0 - p1) * sweep;
        double d2 = baseDelay + (increasingDelay ? p2 : 1.0 - p2) * sweep;
        return ReadFractional(d1) * w1 + ReadFractional(d2) * w2;
    }

    private float ReadFractional(double delaySamples)
    {
        double pos = _writeIndex - delaySamples;
        while (pos < 0) pos += _buffer.Length;
        while (pos >= _buffer.Length) pos -= _buffer.Length;
        int i0 = (int)pos;
        int i1 = i0 + 1;
        if (i1 >= _buffer.Length) i1 = 0;
        float frac = (float)(pos - i0);
        return _buffer[i0] + (_buffer[i1] - _buffer[i0]) * frac;
    }

    private static float TriangleWindow(double phase)
    {
        float p = (float)phase;
        return 1f - MathF.Abs((2f * p) - 1f);
    }

    private void AdvanceWrite()
    {
        _writeIndex++;
        if (_writeIndex >= _buffer.Length) _writeIndex = 0;
    }

    public void Reset()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _writeIndex = 0;
        _phaseDown = 0.0;
        _phaseUp = 0.37;
    }

    private static float Sanitize(float x) => float.IsFinite(x) ? Math.Clamp(x, -1.25f, 1.25f) : 0f;
}
