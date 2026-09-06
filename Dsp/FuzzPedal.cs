using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Fuzz original con dos caracteres: uno redondo y dinámico de inspiración vintage,
/// y otro grueso, comprimido y sostenido. No replica circuitos comerciales específicos.
/// </summary>
internal sealed class FuzzPedal
{
    private readonly int _sampleRate;
    private readonly Biquad _inputHighPass = new();
    private readonly Biquad _preVoice = new();
    private readonly Biquad _lowBranch = new();
    private readonly Biquad _highBranch = new();
    private readonly Biquad _postLowPass = new();
    private readonly Biquad _outputHighPass = new();
    private readonly float _mixStep;

    private bool _enabled;
    private FuzzCharacter _character;
    private float _currentMix;
    private float _targetMix;
    private float _gain = 6f;
    private float _tone = 0.5f;
    private float _level = 0.7f;
    private float _lastInput;
    private float _lastOutput;

    public FuzzPedal(int sampleRate)
    {
        _sampleRate = sampleRate;
        _mixStep = 1f / MathF.Max(1f, sampleRate * 0.014f);
        Configure(false, FuzzCharacter.FuzzFace, 5f, 5f, 5f);
    }

    public void Configure(bool enabled, FuzzCharacter character, float gain, float tone, float level)
    {
        _enabled = enabled;
        _targetMix = enabled ? 1f : 0f;
        _character = character;
        gain = Math.Clamp(gain, 0f, 10f);
        tone = Math.Clamp(tone, 0f, 10f);
        level = Math.Clamp(level, 0f, 10f);
        float n = gain / 10f;
        _gain = 2.0f + MathF.Pow(n, 1.25f) * 18f;
        _tone = tone / 10f;
        _level = 0.12f + (level / 10f) * 1.18f;

        if (character == FuzzCharacter.BigMuff)
        {
            _inputHighPass.SetHighPass(_sampleRate, 58f, 0.70f);
            _preVoice.SetPeak(_sampleRate, 650f, 0.72f, 2.2f);
            _lowBranch.SetLowPass(_sampleRate, 1250f, 0.68f);
            _highBranch.SetHighPass(_sampleRate, 1050f, 0.68f);
            _postLowPass.SetLowPass(_sampleRate, 5600f, 0.70f);
        }
        else
        {
            _inputHighPass.SetHighPass(_sampleRate, 72f, 0.70f);
            _preVoice.SetPeak(_sampleRate, 520f, 0.78f, 1.2f);
            _lowBranch.SetLowPass(_sampleRate, 1650f, 0.70f);
            _highBranch.SetHighPass(_sampleRate, 1450f, 0.70f);
            _postLowPass.SetLowPass(_sampleRate, 7200f, 0.70f);
        }
        _outputHighPass.SetHighPass(_sampleRate, 45f, 0.72f);
    }

    public float Process(float input)
    {
        if (!float.IsFinite(input)) input = 0f;
        float mix = AdvanceMix();
        if (!_enabled && mix <= 0f) return input;

        float x = _inputHighPass.Process(input);
        x = _preVoice.Process(x);
        float clipped = _character == FuzzCharacter.BigMuff
            ? ThickClip(x * (_gain * 1.12f))
            : VintageClip(x * _gain);

        float blocked = FastDspMath.FlushDenormal(clipped - _lastInput + 0.9962f * _lastOutput);
        _lastInput = clipped;
        _lastOutput = blocked;

        float low = _lowBranch.Process(blocked);
        float high = _highBranch.Process(blocked);
        float shaped;
        if (_character == FuzzCharacter.BigMuff)
        {
            // Barrido de tono ancho: grave grueso a brillo cortante.
            shaped = low * (1.15f - 0.85f * _tone) + high * (0.22f + 1.05f * _tone);
        }
        else
        {
            // Más medios y respuesta a la dinámica.
            shaped = low * (0.88f - 0.30f * _tone) + high * (0.32f + 0.62f * _tone);
        }
        shaped = _postLowPass.Process(shaped);
        shaped = _outputHighPass.Process(shaped);
        float processed = shaped * _level;
        processed /= 1f + 0.12f * MathF.Abs(processed);
        return FastDspMath.FlushDenormal((input * (1f - mix)) + (processed * mix));
    }

    private float AdvanceMix()
    {
        if (_currentMix < _targetMix) _currentMix = MathF.Min(_targetMix, _currentMix + _mixStep);
        else if (_currentMix > _targetMix) _currentMix = MathF.Max(_targetMix, _currentMix - _mixStep);
        return _currentMix;
    }

    private static float VintageClip(float input)
    {
        float x = Math.Clamp(input, -8f, 8f);
        float m = MathF.Abs(x);
        float y = m / (1f + 0.62f * m + 0.10f * m * m);
        // Leve asimetría para sensación de transistor vintage.
        if (x < 0f) y *= 0.90f;
        return x < 0f ? -y : y;
    }

    private static float ThickClip(float input)
    {
        float x = Math.Clamp(input, -10f, 10f);
        float m = MathF.Abs(x);
        float y = 1f - 1f / (1f + 1.65f * m + 0.45f * m * m);
        return x < 0f ? -y : y;
    }

    public void Reset()
    {
        _inputHighPass.Reset();
        _preVoice.Reset();
        _lowBranch.Reset();
        _highBranch.Reset();
        _postLowPass.Reset();
        _outputHighPass.Reset();
        _lastInput = 0f;
        _lastOutput = 0f;
        _currentMix = _enabled ? 0f : _targetMix;
    }
}
