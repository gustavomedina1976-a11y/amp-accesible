namespace GDMAmpAccessible.Dsp;

internal sealed class NoiseGate
{
    private readonly int _sampleRate;
    private float _thresholdLinear = 0.001f;
    private float _releaseCoefficient = 0.999f;
    private float _envelope;
    private float _gain = 1f;
    private int _holdSamples;
    private int _holdCounter;
    private bool _enabled = true;

    public NoiseGate(int sampleRate)
    {
        _sampleRate = sampleRate;
        Configure(true, -58f, 180f);
    }

    public void Configure(bool enabled, float thresholdDb, float releaseMs)
    {
        _enabled = enabled;
        _thresholdLinear = MathF.Pow(10f, thresholdDb / 20f);
        float releaseSeconds = Math.Max(0.01f, releaseMs / 1000f);
        _releaseCoefficient = MathF.Exp(-1f / (_sampleRate * releaseSeconds));
        _holdSamples = (int)(_sampleRate * 0.025f);
    }

    public float Process(float input)
    {
        if (!_enabled)
        {
            _gain += (1f - _gain) * 0.02f;
            return input * _gain;
        }

        float level = MathF.Abs(input);
        float attackCoefficient = 0.25f;
        float envelopeCoefficient = level > _envelope ? attackCoefficient : 0.0025f;
        _envelope += (level - _envelope) * envelopeCoefficient;

        bool shouldOpen = _envelope >= _thresholdLinear;
        if (shouldOpen)
        {
            _holdCounter = _holdSamples;
        }
        else if (_holdCounter > 0)
        {
            _holdCounter--;
            shouldOpen = true;
        }

        if (shouldOpen)
        {
            _gain += (1f - _gain) * 0.08f;
        }
        else
        {
            _gain *= _releaseCoefficient;
            if (_gain < 0.00001f)
            {
                _gain = 0f;
            }
        }

        return input * _gain;
    }

    public void Reset()
    {
        _envelope = 0f;
        _gain = 1f;
        _holdCounter = 0;
    }
}
