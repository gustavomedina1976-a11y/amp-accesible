namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Rotary speaker estéreo: rotor grave + bocina aguda con aceleración gradual Slow/Fast.
/// Conserva ambos canales de entrada para no colapsar a mono el chorus, flanger o delay
/// que llegan antes del Leslie en el loop.
/// </summary>
internal sealed class RotarySpeakerEffect
{
    private readonly int _sampleRate;
    private bool _enabled;
    private bool _fast;
    private float _syncRateHz;
    private float _phaseHorn;
    private float _phaseDrum;
    private float _speedHorn;
    private float _speedDrum;
    private float _mix = .45f;
    private float _depth = .7f;

    private readonly Biquad _lowLeft = new();
    private readonly Biquad _lowRight = new();
    private readonly Biquad _highLeft = new();
    private readonly Biquad _highRight = new();

    public RotarySpeakerEffect(int sampleRate)
    {
        _sampleRate = sampleRate;
        ConfigureFilters();
        Reset();
    }

    public void Configure(bool enabled, bool fast, float syncRateHz, float depthPercent, float mixPercent)
    {
        _enabled = enabled;
        _fast = fast;
        _syncRateHz = Math.Clamp(syncRateHz, 0f, 8f);
        _depth = Math.Clamp(depthPercent / 100f, 0f, 1f);
        _mix = Math.Clamp(mixPercent / 100f, 0f, .90f);
    }

    public void Process(float inputLeft, float inputRight, out float left, out float right)
    {
        inputLeft = Sanitize(inputLeft);
        inputRight = Sanitize(inputRight);

        // Punto clave de 2.38.1: cuando Leslie está apagado se conservan L y R.
        // La versión anterior duplicaba sólo el canal izquierdo y anulaba el estéreo.
        if (!_enabled)
        {
            left = inputLeft;
            right = inputRight;
            return;
        }

        float targetHorn = _syncRateHz > 0f ? _syncRateHz : (_fast ? 6.4f : .82f);
        float targetDrum = _syncRateHz > 0f ? MathF.Max(.2f, _syncRateHz * .82f) : (_fast ? 5.2f : .68f);
        float slew = _fast ? .00012f : .000055f;
        _speedHorn += (targetHorn - _speedHorn) * slew;
        _speedDrum += (targetDrum - _speedDrum) * slew;
        _phaseHorn = Advance(_phaseHorn, _speedHorn);
        _phaseDrum = Advance(_phaseDrum, _speedDrum);

        float hiLeft = _highLeft.Process(inputLeft);
        float hiRight = _highRight.Process(inputRight);
        float loLeft = _lowLeft.Process(inputLeft);
        float loRight = _lowRight.Process(inputRight);

        float hornPan = MathF.Sin(_phaseHorn) * .38f * _depth;
        float drumPanLeft = MathF.Sin(_phaseDrum) * .20f * _depth;
        float drumPanRight = MathF.Sin(_phaseDrum + 2.2f) * .20f * _depth;

        float wetLeft = (hiLeft * (1f + hornPan)) + (loLeft * (1f + drumPanLeft));
        float wetRight = (hiRight * (1f - hornPan)) + (loRight * (1f + drumPanRight));

        // 2.38.7: incluso con una fuente mono, la bocina y el rotor deben entregar
        // movimiento L/R perceptible. Un cross-feed muy pequeño y en oposición evita
        // que la suma seca vuelva a esconder el Leslie en la grabación estéreo.
        float stereoSpread = 0.055f * _depth;
        float spreadLeft = wetLeft + ((wetLeft - wetRight) * stereoSpread);
        float spreadRight = wetRight + ((wetRight - wetLeft) * stereoSpread);

        left = Sanitize((inputLeft * (1f - _mix)) + (spreadLeft * _mix));
        right = Sanitize((inputRight * (1f - _mix)) + (spreadRight * _mix));
    }

    private void ConfigureFilters()
    {
        _lowLeft.SetLowPass(_sampleRate, 800f, .7f);
        _lowRight.SetLowPass(_sampleRate, 800f, .7f);
        _highLeft.SetHighPass(_sampleRate, 700f, .7f);
        _highRight.SetHighPass(_sampleRate, 700f, .7f);
    }

    private float Advance(float phase, float hz)
    {
        phase += 2f * MathF.PI * hz / _sampleRate;
        return phase > 2f * MathF.PI ? phase - 2f * MathF.PI : phase;
    }

    public void Reset()
    {
        _phaseHorn = 0f;
        _phaseDrum = 0f;
        _speedHorn = .82f;
        _speedDrum = .68f;
        _lowLeft.Reset();
        _lowRight.Reset();
        _highLeft.Reset();
        _highRight.Reset();
    }

    private static float Sanitize(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, -1.35f, 1.35f) : 0f;
}
