using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Familia de overdrives para el bloque OD-1 / OCD. Los dos primeros modos
/// conservan el voicing OD-1 ya existente. Los modos OCD son una aproximación
/// DSP original al carácter general LP/HP, sin copiar un circuito comercial.
/// </summary>
internal sealed class Od1OverdrivePedal
{
    private readonly int _sampleRate;
    private readonly Biquad _inputHighPass = new();
    private readonly Biquad _midVoice = new();
    private readonly Biquad _postLowPass = new();
    private readonly Biquad _toneShelf = new();
    private readonly Biquad _outputHighPass = new();
    private readonly float _mixStep;

    private bool _enabled;
    private float _currentMix;
    private float _targetMix;
    private float _drive = 2.2f;
    private float _level = 0.78f;
    private Od1Character _character = Od1Character.Vintage1977;
    private float _dcInput;
    private float _dcOutput;

    public Od1OverdrivePedal(int sampleRate)
    {
        _sampleRate = sampleRate;
        _mixStep = 1f / MathF.Max(1f, sampleRate * 0.014f);
        Configure(false, Od1Character.Vintage1977, 3.5f, 5f, 5f);
    }

    public void Configure(bool enabled, Od1Character character, float drive, float tone, float level)
    {
        _enabled = enabled;
        _character = character;
        _targetMix = enabled ? 1f : 0f;
        drive = Math.Clamp(drive, 0f, 10f);
        tone = Math.Clamp(tone, 0f, 10f);
        level = Math.Clamp(level, 0f, 10f);

        float normalizedDrive = drive / 10f;
        float toneDb = (tone - 5f);
        _level = 0.18f + (level / 10f) * 1.22f;

        switch (character)
        {
            case Od1Character.Late4558:
                _drive = 1.18f + MathF.Pow(normalizedDrive, 1.30f) * 5.75f;
                _inputHighPass.SetHighPass(_sampleRate, 122f, 0.72f);
                _midVoice.SetPeak(_sampleRate, 900f, 0.80f, 2.2f + normalizedDrive * 1.7f);
                _postLowPass.SetLowPass(_sampleRate, 7600f, 0.70f);
                _toneShelf.SetHighShelf(_sampleRate, 2500f, -0.15f);
                _outputHighPass.SetHighPass(_sampleRate, 58f, 0.72f);
                break;

            case Od1Character.OcdLp:
                // LP: cuerpo más lleno, agudo redondeado y respuesta dinámica.
                _drive = 1.05f + MathF.Pow(normalizedDrive, 1.18f) * 8.2f;
                _level = 0.16f + (level / 10f) * 1.36f;
                _inputHighPass.SetHighPass(_sampleRate, 68f, 0.72f);
                _midVoice.SetPeak(_sampleRate, 720f, 0.78f, 0.8f + normalizedDrive * 0.8f);
                _postLowPass.SetLowPass(_sampleRate, 6900f, 0.70f);
                _toneShelf.SetHighShelf(_sampleRate, 2100f, toneDb * 1.45f);
                _outputHighPass.SetHighPass(_sampleRate, 48f, 0.72f);
                break;

            case Od1Character.OcdHp:
                // HP: graves más firmes, más presencia y un poco más de empuje.
                _drive = 1.15f + MathF.Pow(normalizedDrive, 1.16f) * 9.0f;
                _level = 0.17f + (level / 10f) * 1.42f;
                _inputHighPass.SetHighPass(_sampleRate, 92f, 0.72f);
                _midVoice.SetPeak(_sampleRate, 850f, 0.80f, 1.4f + normalizedDrive * 0.9f);
                _postLowPass.SetLowPass(_sampleRate, 7900f, 0.70f);
                _toneShelf.SetHighShelf(_sampleRate, 1950f, 0.6f + toneDb * 1.55f);
                _outputHighPass.SetHighPass(_sampleRate, 54f, 0.72f);
                break;

            default:
                // Vintage 1977: cálido, redondo y con medios presentes.
                _drive = 1.15f + MathF.Pow(normalizedDrive, 1.35f) * 5.4f;
                _inputHighPass.SetHighPass(_sampleRate, 105f, 0.72f);
                _midVoice.SetPeak(_sampleRate, 820f, 0.78f, 2.8f + normalizedDrive * 2.0f);
                _postLowPass.SetLowPass(_sampleRate, 6900f, 0.70f);
                _toneShelf.SetHighShelf(_sampleRate, 2300f, -0.8f);
                _outputHighPass.SetHighPass(_sampleRate, 52f, 0.72f);
                break;
        }
    }

    public float Process(float input)
    {
        if (!float.IsFinite(input)) input = 0f;
        float mix = AdvanceMix();
        if (!_enabled && mix <= 0f) return input;

        float x = _inputHighPass.Process(input);
        x = _midVoice.Process(x);
        float raw = _character switch
        {
            Od1Character.Late4558 => AsymmetricLateClip(x * _drive),
            Od1Character.OcdLp => OcdDynamicClip(x * _drive, 0.84f),
            Od1Character.OcdHp => OcdDynamicClip(x * _drive, 0.94f),
            _ => AsymmetricWarmClip(x * _drive)
        };

        float blocked = FastDspMath.FlushDenormal(raw - _dcInput + (0.9968f * _dcOutput));
        _dcInput = raw;
        _dcOutput = blocked;

        float shaped = _postLowPass.Process(blocked);
        shaped = _toneShelf.Process(shaped);
        shaped = _outputHighPass.Process(shaped);
        float processed = shaped * _level;
        float limiter = _character is Od1Character.OcdLp or Od1Character.OcdHp ? 0.055f : 0.08f;
        processed /= 1f + (limiter * MathF.Abs(processed));

        return FastDspMath.FlushDenormal((input * (1f - mix)) + (processed * mix));
    }

    private float AdvanceMix()
    {
        if (_currentMix < _targetMix) _currentMix = MathF.Min(_targetMix, _currentMix + _mixStep);
        else if (_currentMix > _targetMix) _currentMix = MathF.Max(_targetMix, _currentMix - _mixStep);
        return _currentMix;
    }

    private static float AsymmetricWarmClip(float input)
    {
        float x = Math.Clamp(input, -6f, 6f);
        if (x >= 0f) return x / (1f + 0.72f * x);
        float m = -x;
        return -(m / (1f + 0.46f * m));
    }

    private static float AsymmetricLateClip(float input)
    {
        float x = Math.Clamp(input, -6f, 6f);
        if (x >= 0f) return x / (1f + 0.66f * x);
        float m = -x;
        return -(m / (1f + 0.50f * m));
    }

    private static float OcdDynamicClip(float input, float hardness)
    {
        float x = Math.Clamp(input, -8f, 8f);
        float abs = MathF.Abs(x);
        float curved = x / (1f + (hardness * 0.34f * abs));
        // Mantiene ataque y dinámica con un techo suave para evitar picos inestables.
        return MathF.Tanh(curved * (1.15f + hardness * 0.28f));
    }

    public void Reset()
    {
        _inputHighPass.Reset();
        _midVoice.Reset();
        _postLowPass.Reset();
        _toneShelf.Reset();
        _outputHighPass.Reset();
        _dcInput = 0f;
        _dcOutput = 0f;
        _currentMix = _enabled ? 0f : _targetMix;
    }
}
