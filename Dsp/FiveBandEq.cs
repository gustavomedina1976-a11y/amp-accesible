namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Ecualizador gráfico de cinco bandas para guitarra. Las frecuencias son fijas y los
/// cinco controles trabajan en dB. Incluye nivel general de salida y rampa de bypass.
/// </summary>
internal sealed class FiveBandEq
{
    private readonly int _sampleRate;
    private readonly Biquad[] _bands = { new(), new(), new(), new(), new() };
    private readonly float _mixStep;
    private bool _enabled;
    private float _currentMix;
    private float _targetMix;
    private float _outputGain = 1f;

    private static readonly float[] Frequencies = { 100f, 250f, 800f, 2500f, 6400f };
    private static readonly float[] Q = { 0.75f, 0.85f, 0.90f, 0.92f, 0.78f };

    public FiveBandEq(int sampleRate)
    {
        _sampleRate = sampleRate;
        _mixStep = 1f / MathF.Max(1f, sampleRate * 0.012f);
        Configure(false, 0f, 0f, 0f, 0f, 0f, 0f);
    }

    public void Configure(bool enabled, float band100, float band250, float band800,
        float band2500, float band6400, float outputDb)
    {
        _enabled = enabled;
        _targetMix = enabled ? 1f : 0f;
        float[] gains =
        {
            Math.Clamp(band100, -12f, 12f), Math.Clamp(band250, -12f, 12f),
            Math.Clamp(band800, -12f, 12f), Math.Clamp(band2500, -12f, 12f),
            Math.Clamp(band6400, -12f, 12f)
        };
        for (int i = 0; i < _bands.Length; i++)
            _bands[i].SetPeak(_sampleRate, Frequencies[i], Q[i], gains[i]);
        _outputGain = MathF.Pow(10f, Math.Clamp(outputDb, -12f, 12f) / 20f);
    }

    public float Process(float input)
    {
        if (!float.IsFinite(input)) input = 0f;
        float mix = AdvanceMix();
        if (!_enabled && mix <= 0f) return input;
        float x = input;
        for (int i = 0; i < _bands.Length; i++) x = _bands[i].Process(x);
        x *= _outputGain;
        x /= 1f + 0.035f * MathF.Abs(x);
        return FastDspMath.FlushDenormal(input * (1f - mix) + x * mix);
    }

    private float AdvanceMix()
    {
        if (_currentMix < _targetMix) _currentMix = MathF.Min(_targetMix, _currentMix + _mixStep);
        else if (_currentMix > _targetMix) _currentMix = MathF.Max(_targetMix, _currentMix - _mixStep);
        return _currentMix;
    }

    public void Reset()
    {
        foreach (Biquad band in _bands) band.Reset();
        _currentMix = _enabled ? 0f : _targetMix;
    }
}
