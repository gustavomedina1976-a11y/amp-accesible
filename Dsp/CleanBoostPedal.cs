using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Booster limpio inspirado en el concepto de un CAE/MXR MC401: una sola ganancia,
/// ancho de banda amplio y saturación de seguridad muy suave al final del recorrido.
/// </summary>
internal sealed class CleanBoostPedal
{
    private readonly int _sampleRate;
    private readonly Biquad _highPass = new();
    private readonly Biquad _lowPass = new();
    private bool _enabled;
    private float _currentMix;
    private float _targetMix;
    private readonly float _mixStep;
    private float _linearGain = 1f;

    public CleanBoostPedal(int sampleRate)
    {
        _sampleRate = sampleRate;
        _mixStep = 1f / MathF.Max(1f, sampleRate * 0.010f);
        Configure(false, BoosterCharacter.CaeLineDriver, 6f);
    }

    public void Configure(bool enabled, BoosterCharacter character, float boostDb)
    {
        _enabled = enabled;
        _targetMix = enabled ? 1f : 0f;
        boostDb = Math.Clamp(boostDb, 0f, 20f);
        _linearGain = MathF.Pow(10f, boostDb / 20f);
        switch (character)
        {
            case BoosterCharacter.EpStyle:
                _highPass.SetHighPass(_sampleRate, 48f, 0.72f);
                _lowPass.SetLowPass(_sampleRate, 12500f, 0.70f);
                _linearGain *= 1.05f;
                break;
            case BoosterCharacter.TrebleBooster:
                _highPass.SetHighPass(_sampleRate, 180f, 0.72f);
                _lowPass.SetLowPass(_sampleRate, 9800f, 0.70f);
                _linearGain *= 1.10f;
                break;
            default:
                _highPass.SetHighPass(_sampleRate, 35f, 0.72f);
                _lowPass.SetLowPass(_sampleRate, 17800f, 0.70f);
                break;
        }
    }

    public float Process(float input)
    {
        if (!float.IsFinite(input))
        {
            input = 0f;
        }

        float mix = AdvanceMix();
        if (!_enabled && mix <= 0f)
        {
            return input;
        }

        float output = _lowPass.Process(_highPass.Process(input)) * _linearGain;

        // Evita valores extremos antes de entrar al amplificador, sin convertir el booster
        // en un pedal de distorsión cuando se usa en niveles moderados.
        float magnitude = MathF.Abs(output);
        float processed;
        if (magnitude <= 1.15f)
        {
            processed = output;
        }
        else
        {
            float excess = magnitude - 1.15f;
            float limited = 1.15f + (excess / (1f + excess));
            processed = output < 0f ? -limited : limited;
        }

        return FastDspMath.FlushDenormal((input * (1f - mix)) + (processed * mix));
    }

    private float AdvanceMix()
    {
        if (_currentMix < _targetMix)
        {
            _currentMix = MathF.Min(_targetMix, _currentMix + _mixStep);
        }
        else if (_currentMix > _targetMix)
        {
            _currentMix = MathF.Max(_targetMix, _currentMix - _mixStep);
        }
        return _currentMix;
    }

    public void Reset()
    {
        _highPass.Reset();
        _lowPass.Reset();
        _currentMix = _enabled ? 0f : _targetMix;
    }
}
