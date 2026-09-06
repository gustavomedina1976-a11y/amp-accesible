using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Compresor musical de bajo costo pensado especialmente para sonidos limpios.
/// No asigna memoria ni recalcula filtros dentro de Process.
/// </summary>
internal sealed class CleanCompressor
{
    private readonly int _sampleRate;
    private bool _enabled;
    private float _envelope;
    private float _gain = 1f;
    private float _threshold = 0.18f;
    private float _ratio = 4f;
    private float _makeup = 1.15f;
    private float _attackCoeff;
    private float _releaseCoeff;
    private float _gainAttackCoeff;
    private float _gainReleaseCoeff;
    private float _mixCurrent;
    private float _mixTarget;
    private readonly float _mixRampStep;

    public CleanCompressor(int sampleRate)
    {
        _sampleRate = sampleRate;
        _mixRampStep = 1f / MathF.Max(1f, sampleRate * 0.012f);
        Configure(false, CompressorCharacter.StudioClean, 5f, 18f, 5f);
    }

    public void Configure(bool enabled, CompressorCharacter character, float sustain, float attackMs, float level)
    {
        _enabled = enabled;
        _mixTarget = enabled ? 1f : 0f;
        sustain = float.IsFinite(sustain) ? Math.Clamp(sustain, 0f, 10f) : 5f;
        attackMs = float.IsFinite(attackMs) ? Math.Clamp(attackMs, 2f, 80f) : 18f;
        level = float.IsFinite(level) ? Math.Clamp(level, 0f, 10f) : 5f;

        float s = sustain / 10f;
        _threshold = 0.34f - (s * 0.27f);          // 0.34 -> 0.07
        _ratio = 2.0f + (s * 4.5f);               // 2:1 -> 6.5:1
        float levelDb = (level - 5f) * 1.2f;       // +/- 6 dB
        float autoMakeup = 1f + (s * 0.18f);
        _makeup = MathF.Pow(10f, levelDb / 20f) * autoMakeup;

        _attackCoeff = TimeCoefficient(attackMs);
        _releaseCoeff = TimeCoefficient(145f + (s * 85f));
        _gainAttackCoeff = TimeCoefficient(MathF.Max(2f, attackMs * 0.70f));
        _gainReleaseCoeff = TimeCoefficient(110f + (s * 70f));
        switch(character)
        {
            case CompressorCharacter.Dyna: _ratio *= 1.35f; _threshold *= .82f; _makeup *= 1.06f; _releaseCoeff = TimeCoefficient(210f + s*100f); break;
            case CompressorCharacter.Optical: _ratio *= .72f; _attackCoeff = TimeCoefficient(MathF.Max(12f, attackMs*1.6f)); _releaseCoeff = TimeCoefficient(280f+s*130f); _makeup *= 1.02f; break;
            case CompressorCharacter.Sustainer: _ratio *= 1.65f; _threshold *= .68f; _releaseCoeff = TimeCoefficient(330f+s*170f); _makeup *= 1.10f; break;
        }
    }

    public float Process(float input)
    {
        input = Sanitize(input);
        AdvanceMix();
        if (_mixCurrent <= 0.0001f && !_enabled)
        {
            return input;
        }

        float detector = MathF.Abs(input);
        float envCoeff = detector > _envelope ? _attackCoeff : _releaseCoeff;
        _envelope = detector + (envCoeff * (_envelope - detector));
        if (!float.IsFinite(_envelope))
        {
            Reset();
            return input;
        }

        float targetGain = 1f;
        if (_envelope > _threshold && _envelope > 0.000001f)
        {
            float compressed = _threshold + ((_envelope - _threshold) / _ratio);
            targetGain = compressed / _envelope;
        }

        float gainCoeff = targetGain < _gain ? _gainAttackCoeff : _gainReleaseCoeff;
        _gain = targetGain + (gainCoeff * (_gain - targetGain));
        if (!float.IsFinite(_gain))
        {
            _gain = 1f;
        }

        float compressedOutput = SoftLimit(input * _gain * _makeup);
        return Sanitize((input * (1f - _mixCurrent)) + (compressedOutput * _mixCurrent));
    }

    private void AdvanceMix()
    {
        if (_mixCurrent < _mixTarget) _mixCurrent = MathF.Min(_mixTarget, _mixCurrent + _mixRampStep);
        else if (_mixCurrent > _mixTarget) _mixCurrent = MathF.Max(_mixTarget, _mixCurrent - _mixRampStep);
    }

    public void Reset()
    {
        _envelope = 0f;
        _gain = 1f;
        _mixCurrent = _enabled ? 1f : 0f;
        _mixTarget = _mixCurrent;
    }

    private float TimeCoefficient(float milliseconds)
    {
        float samples = MathF.Max(1f, milliseconds * 0.001f * _sampleRate);
        return MathF.Exp(-1f / samples);
    }

    private static float Sanitize(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, -1.25f, 1.25f) : 0f;

    private static float SoftLimit(float value)
    {
        if (!float.IsFinite(value)) return 0f;
        float x = Math.Clamp(value, -2.5f, 2.5f);
        float x2 = x * x;
        return Math.Clamp(x * (27f + x2) / (27f + (9f * x2)), -1.15f, 1.15f);
    }
}
