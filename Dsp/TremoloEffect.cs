namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Trémolo estéreo con un único avance de fase por frame. Conserva la imagen L/R de los
/// efectos anteriores y evita que procesar dos canales duplique accidentalmente la velocidad.
/// </summary>
internal sealed class TremoloEffect
{
    private readonly int _sampleRate;
    private bool _enabled;
    private float _phase;
    private float _increment;
    private float _depth;

    public TremoloEffect(int sampleRate)
    {
        _sampleRate = sampleRate;
        Configure(false, 4f, 50f);
    }

    public void Configure(bool enabled, float rateHz, float depthPercent)
    {
        _enabled = enabled;
        _increment = 2f * MathF.PI * Math.Clamp(rateHz, .1f, 12f) / _sampleRate;
        _depth = Math.Clamp(depthPercent / 100f, 0f, .95f);
    }

    public void ProcessStereo(float inputLeft, float inputRight, out float outputLeft, out float outputRight)
    {
        if (!_enabled)
        {
            outputLeft = inputLeft;
            outputRight = inputRight;
            return;
        }

        float lfo = .5f + (.5f * MathF.Sin(_phase));
        float gain = (1f - _depth) + (_depth * lfo);
        outputLeft = inputLeft * gain;
        outputRight = inputRight * gain;

        _phase += _increment;
        if (_phase > 2f * MathF.PI) _phase -= 2f * MathF.PI;
    }

    public void Reset() => _phase = 0f;
}
