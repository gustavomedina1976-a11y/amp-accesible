using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Distorsión original inspirada en el carácter general de un Boss DS-1:
/// ataque definido, clipping simétrico tipo diodo, control de tono de dos ramas
/// y nivel de salida. No copia un circuito ni utiliza código del fabricante.
/// </summary>
internal sealed class Ds1DistortionPedal
{
    private readonly int _sampleRate;
    private readonly Biquad _inputHighPass = new();
    private readonly Biquad _preEmphasis = new();
    private readonly Biquad _postLowPass = new();
    private readonly Biquad _toneLow = new();
    private readonly Biquad _toneHigh = new();
    private readonly Biquad _outputHighPass = new();

    private bool _enabled;
    private float _currentMix;
    private float _targetMix;
    private readonly float _mixStep;
    private float _drive = 4.2f;
    private float _tone = 0.45f;
    private float _level = 0.72f;
    private float _dcInput;
    private float _dcOutput;

    public Ds1DistortionPedal(int sampleRate)
    {
        _sampleRate = sampleRate;
        _mixStep = 1f / MathF.Max(1f, sampleRate * 0.012f);
        Configure(false, DistortionCharacter.Ds1, 4.0f, 4.5f, 5.0f);
    }

    public void Configure(bool enabled, DistortionCharacter character, float distortion, float tone, float level)
    {
        _enabled = enabled;
        _targetMix = enabled ? 1f : 0f;
        distortion = Math.Clamp(distortion, 0f, 10f);
        tone = Math.Clamp(tone, 0f, 10f);
        level = Math.Clamp(level, 0f, 10f);

        float normalizedDrive = distortion / 10f;
        _drive = 1.35f + (MathF.Pow(normalizedDrive, 1.35f) * 12.0f);
        _tone = tone / 10f;
        _level = 0.10f + ((level / 10f) * 1.28f);

        // Graves suficientemente completos para guitarra, con énfasis previo de medios
        // que ayuda a que el recorte de diodos permanezca definido y no se vuelva fuzz.
        _inputHighPass.SetHighPass(_sampleRate, 72f, 0.72f);
        _preEmphasis.SetPeak(_sampleRate, 1180f, 0.78f, 2.0f + (normalizedDrive * 3.0f));
        _postLowPass.SetLowPass(_sampleRate, 7200f, 0.70f);

        // Control de tono estilo mezcla de rama grave y rama aguda. En el centro existe
        // un leve hueco de medios característico, sin hacerlo excesivamente filoso.
        _toneLow.SetLowPass(_sampleRate, 1050f, 0.66f);
        _toneHigh.SetHighPass(_sampleRate, 1180f, 0.66f);
        switch(character)
        {
            case DistortionCharacter.Guvnor: _drive *= 1.12f; _preEmphasis.SetPeak(_sampleRate, 720f,.78f,4.5f); _postLowPass.SetLowPass(_sampleRate,6200f,.70f); break;
            case DistortionCharacter.Rat: _drive *= 1.55f; _preEmphasis.SetPeak(_sampleRate,1050f,.75f,2.5f); _postLowPass.SetLowPass(_sampleRate,4800f + tone*260f,.70f); break;
            case DistortionCharacter.Modern: _drive *= 1.35f; _inputHighPass.SetHighPass(_sampleRate,105f,.72f); _preEmphasis.SetPeak(_sampleRate,1450f,.82f,3.8f); _postLowPass.SetLowPass(_sampleRate,6000f,.70f); break;
        }
        _outputHighPass.SetHighPass(_sampleRate, 48f, 0.72f);
    }

    public float Process(float input)
    {
        if (!float.IsFinite(input)) input = 0f;

        float mix = AdvanceMix();
        if (!_enabled && mix <= 0f) return input;

        float x = _inputHighPass.Process(input);
        x = _preEmphasis.Process(x);

        float rawClipped = DiodeLikeClip(x * _drive);
        float clipped = FastDspMath.FlushDenormal(rawClipped - _dcInput + (0.9965f * _dcOutput));
        _dcInput = rawClipped;
        _dcOutput = clipped;
        clipped = _postLowPass.Process(clipped);

        float low = _toneLow.Process(clipped);
        float high = _toneHigh.Process(clipped);
        float lowGain = 1.10f - (_tone * 0.62f);
        float highGain = 0.30f + (_tone * 1.02f);
        float shaped = (low * lowGain) + (high * highGain);
        shaped = _outputHighPass.Process(shaped);

        float processed = shaped * _level;
        processed = processed / (1f + (0.10f * MathF.Abs(processed)));
        return FastDspMath.FlushDenormal((input * (1f - mix)) + (processed * mix));
    }

    private float AdvanceMix()
    {
        if (_currentMix < _targetMix)
            _currentMix = MathF.Min(_targetMix, _currentMix + _mixStep);
        else if (_currentMix > _targetMix)
            _currentMix = MathF.Max(_targetMix, _currentMix - _mixStep);
        return _currentMix;
    }

    private static float DiodeLikeClip(float input)
    {
        // Rodilla firme, simétrica y acotada. Evita funciones trascendentes en tiempo real.
        float x = Math.Clamp(input, -8f, 8f);
        float sign = x < 0f ? -1f : 1f;
        float magnitude = MathF.Abs(x);
        const float knee = 0.62f;
        if (magnitude <= knee) return x;

        float excess = magnitude - knee;
        float compressed = knee + (excess / (1f + (3.6f * excess)));
        return sign * compressed;
    }

    public void Reset()
    {
        _inputHighPass.Reset();
        _preEmphasis.Reset();
        _postLowPass.Reset();
        _toneLow.Reset();
        _toneHigh.Reset();
        _outputHighPass.Reset();
        _dcInput = 0f;
        _dcOutput = 0f;
        _currentMix = _enabled ? 0f : _targetMix;
    }
}
