using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Overdrive original inspirado en el carácter general del Maza Divine:
/// ganancia baja/media, medios cálidos, compresión moderada y claridad.
/// No reproduce ni copia un circuito comercial específico.
/// </summary>
internal sealed class DivineOverdrivePedal
{
    private readonly int _sampleRate;
    private readonly Biquad _inputHighPass = new();
    private readonly Biquad _lowMidVoice = new();
    private readonly Biquad _clarityVoice = new();
    private readonly Biquad _postLowPass = new();
    private readonly Biquad _toneShelf = new();
    private readonly Biquad _outputHighPass = new();

    private bool _enabled;
    private float _currentMix;
    private float _targetMix;
    private readonly float _mixStep;
    private float _drive = 1.25f;
    private float _level = 0.8f;
    private float _cleanBlend = 0.34f;
    private float _compressionAmount = 0.16f;
    private float _envelope;
    private float _lastClipInput;
    private float _lastClipOutput;

    public DivineOverdrivePedal(int sampleRate)
    {
        _sampleRate = sampleRate;
        _mixStep = 1f / MathF.Max(1f, sampleRate * 0.014f);
        Configure(false, DriveCharacter.Ts808, 3f, 5f, 5f);
    }

    public void Configure(bool enabled, DriveCharacter character, float gain, float tone, float level)
    {
        _enabled = enabled;
        _targetMix = enabled ? 1f : 0f;
        gain = Math.Clamp(gain, 0f, 10f);
        tone = Math.Clamp(tone, 0f, 10f);
        level = Math.Clamp(level, 0f, 10f);

        float normalizedGain = gain / 10f;
        _drive = 1.05f + (MathF.Pow(normalizedGain, 1.55f) * 4.65f);
        _level = 0.18f + ((level / 10f) * 1.20f);
        _cleanBlend = 0.42f - (normalizedGain * 0.20f);
        _compressionAmount = 0.10f + (normalizedGain * 0.20f);

        // Cuatro voces originales inspiradas en familias clásicas, sin copiar circuitos.
        switch (character)
        {
            case DriveCharacter.Ts9:
                _drive *= 1.12f; _cleanBlend = 0.16f;
                _inputHighPass.SetHighPass(_sampleRate, 145f, 0.72f);
                _lowMidVoice.SetPeak(_sampleRate, 760f, 0.78f, 4.4f);
                _clarityVoice.SetPeak(_sampleRate, 1850f, 0.9f, 1.1f);
                _postLowPass.SetLowPass(_sampleRate, 6100f, 0.70f); break;
            case DriveCharacter.Klon:
                _drive *= 0.86f; _cleanBlend = 0.52f;
                _inputHighPass.SetHighPass(_sampleRate, 70f, 0.72f);
                _lowMidVoice.SetPeak(_sampleRate, 950f, 0.82f, 1.5f);
                _clarityVoice.SetPeak(_sampleRate, 2200f, 0.9f, 1.5f);
                _postLowPass.SetLowPass(_sampleRate, 8200f, 0.70f); break;
            case DriveCharacter.Marshall:
                _drive *= 1.55f; _cleanBlend = 0.08f; _compressionAmount += 0.08f;
                _inputHighPass.SetHighPass(_sampleRate, 95f, 0.72f);
                _lowMidVoice.SetPeak(_sampleRate, 680f, 0.72f, 3.2f);
                _clarityVoice.SetPeak(_sampleRate, 1650f, 0.82f, 2.8f);
                _postLowPass.SetLowPass(_sampleRate, 5600f, 0.70f); break;
            case DriveCharacter.Dod250:
                // Overdrive/preamp seco y abierto: menos compresión y menos realce de medios.
                _drive *= 1.28f; _cleanBlend = 0.10f; _compressionAmount *= 0.48f;
                _inputHighPass.SetHighPass(_sampleRate, 76f, 0.72f);
                _lowMidVoice.SetPeak(_sampleRate, 520f, 0.78f, 1.0f);
                _clarityVoice.SetPeak(_sampleRate, 2050f, 0.86f, 1.9f);
                _postLowPass.SetLowPass(_sampleRate, 7600f, 0.70f); break;
            default: // TS808: más redondo y suave
                _cleanBlend = 0.20f;
                _inputHighPass.SetHighPass(_sampleRate, 125f, 0.72f);
                _lowMidVoice.SetPeak(_sampleRate, 720f, 0.76f, 4.0f);
                _clarityVoice.SetPeak(_sampleRate, 1500f, 0.86f, 0.8f);
                _postLowPass.SetLowPass(_sampleRate, 5900f, 0.70f); break;
        }
        _toneShelf.SetHighShelf(_sampleRate, 2400f, (tone - 5f) * 1.35f);
        _outputHighPass.SetHighPass(_sampleRate, 52f, 0.72f);
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

        float voiced = _inputHighPass.Process(input);
        voiced = _lowMidVoice.Process(voiced);
        voiced = _clarityVoice.Process(voiced);

        // Curva impar y redondeada: evita la rectificación que puede exagerar la octava.
        float clipped = WarmSymmetricClip(voiced * _drive);

        float dcBlocked = FastDspMath.FlushDenormal(
            clipped - _lastClipInput + (0.9968f * _lastClipOutput));
        _lastClipInput = FastDspMath.FlushDenormal(clipped);
        _lastClipOutput = dcBlocked;

        float shaped = _postLowPass.Process(dcBlocked);
        shaped = _toneShelf.Process(shaped);
        shaped = _outputHighPass.Process(shaped);

        float envelopeTarget = MathF.Min(2f, MathF.Abs(shaped));
        float coefficient = envelopeTarget > _envelope ? 0.010f : 0.0009f;
        _envelope = FastDspMath.FlushDenormal(
            _envelope + ((envelopeTarget - _envelope) * coefficient));
        float compressionGain = 1f / (1f + (_envelope * _compressionAmount));

        float warm = shaped * compressionGain;
        float processed = ((input * _cleanBlend) + (warm * (1f - _cleanBlend))) * _level;
        processed /= 1f + (0.10f * MathF.Abs(processed));

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

    private static float WarmSymmetricClip(float input)
    {
        float x = Math.Clamp(input, -4f, 4f);
        float magnitude = MathF.Abs(x);
        return x / (1f + (0.30f * magnitude) + (0.055f * x * x));
    }

    public void Reset()
    {
        _inputHighPass.Reset();
        _lowMidVoice.Reset();
        _clarityVoice.Reset();
        _postLowPass.Reset();
        _toneShelf.Reset();
        _outputHighPass.Reset();
        _envelope = 0f;
        _lastClipInput = 0f;
        _lastClipOutput = 0f;
        _currentMix = _enabled ? 0f : _targetMix;
    }
}
