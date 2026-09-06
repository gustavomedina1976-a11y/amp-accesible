namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Phaser de cuatro etapas all-pass, estable y sin asignaciones en tiempo real.
/// </summary>
internal sealed class PhaserEffect
{
    private const int TableSize = 256;
    private readonly int _sampleRate;
    private readonly float[] _allPassCoefficients = new float[TableSize];
    private readonly float[] _state = new float[4];
    private bool _enabled;
    private float _rateHz = 0.55f;
    private float _depth = 0.68f;
    private float _feedback = 0.18f;
    private float _mixTarget = 0.45f;
    private float _mixCurrent;
    private readonly float _mixRampStep;
    private float _sinPhase;
    private float _cosPhase = 1f;
    private float _sinIncrement;
    private float _cosIncrement = 1f;
    private float _feedbackState;
    private int _normalizationCounter;

    public PhaserEffect(int sampleRate)
    {
        _sampleRate = sampleRate;
        _mixRampStep = 1f / MathF.Max(1f, sampleRate * 0.018f);
        for (int i = 0; i < TableSize; i++)
        {
            float t = i / (float)(TableSize - 1);
            float frequency = 180f * MathF.Pow(1900f / 180f, t);
            float k = MathF.Tan(MathF.PI * frequency / sampleRate);
            _allPassCoefficients[i] = Math.Clamp((1f - k) / (1f + k), -0.98f, 0.98f);
        }
        UpdateIncrement();
    }

    public void Configure(bool enabled, float rateHz, float depthPercent, float feedbackPercent, float mixPercent)
    {
        bool wasEnabled = _enabled;
        _enabled = enabled;
        if (!wasEnabled && enabled)
        {
            ResetFilterState();
        }
        float safeRate = float.IsFinite(rateHz) ? rateHz : 0.55f;
        float newRate = Math.Clamp(safeRate, 0.05f, 4f);
        if (MathF.Abs(newRate - _rateHz) > 0.0001f)
        {
            _rateHz = newRate;
            UpdateIncrement();
        }

        _depth = Math.Clamp((float.IsFinite(depthPercent) ? depthPercent : 68f) / 100f, 0f, 1f);
        _feedback = Math.Clamp((float.IsFinite(feedbackPercent) ? feedbackPercent : 18f) / 100f, 0f, 0.72f);
        _mixTarget = enabled
            ? Math.Clamp((float.IsFinite(mixPercent) ? mixPercent : 45f) / 100f, 0f, 0.85f)
            : 0f;
    }

    public float Process(float input)
    {
        input = Sanitize(input);
        AdvanceMix();

        if (_mixCurrent <= 0.0001f)
        {
            AdvanceLfo();
            return input;
        }

        float lfo = (_sinPhase + 1f) * 0.5f;
        float sweep = 0.48f + ((lfo - 0.5f) * _depth);
        sweep = Math.Clamp(sweep, 0f, 1f);
        int tableIndex = Math.Clamp((int)(sweep * (TableSize - 1)), 0, TableSize - 1);
        float coefficient = _allPassCoefficients[tableIndex];

        float x = input + (_feedbackState * _feedback);
        for (int stage = 0; stage < _state.Length; stage++)
        {
            float y = (-coefficient * x) + _state[stage];
            _state[stage] = x + (coefficient * y);
            x = y;
        }

        if (!float.IsFinite(x) || MathF.Abs(x) > 8f)
        {
            ResetFilterState();
            AdvanceLfo();
            return input;
        }

        _feedbackState = x;
        float output = (input * (1f - _mixCurrent)) + (x * _mixCurrent);
        AdvanceLfo();
        return Sanitize(output);
    }

    public void Reset()
    {
        ResetFilterState();
        _mixCurrent = 0f;
        _sinPhase = 0f;
        _cosPhase = 1f;
        _normalizationCounter = 0;
    }

    private void ResetFilterState()
    {
        Array.Clear(_state);
        _feedbackState = 0f;
    }

    private void AdvanceMix()
    {
        float target = _enabled ? _mixTarget : 0f;
        if (_mixCurrent < target) _mixCurrent = MathF.Min(target, _mixCurrent + _mixRampStep);
        else if (_mixCurrent > target) _mixCurrent = MathF.Max(target, _mixCurrent - _mixRampStep);
    }

    private void AdvanceLfo()
    {
        float sin = _sinPhase;
        float cos = _cosPhase;
        _sinPhase = (sin * _cosIncrement) + (cos * _sinIncrement);
        _cosPhase = (cos * _cosIncrement) - (sin * _sinIncrement);

        if (!float.IsFinite(_sinPhase) || !float.IsFinite(_cosPhase))
        {
            _sinPhase = 0f;
            _cosPhase = 1f;
            _normalizationCounter = 0;
            return;
        }

        if (++_normalizationCounter >= 4096)
        {
            float magnitude = MathF.Sqrt((_sinPhase * _sinPhase) + (_cosPhase * _cosPhase));
            if (magnitude > 0.0001f && float.IsFinite(magnitude))
            {
                _sinPhase /= magnitude;
                _cosPhase /= magnitude;
            }
            else
            {
                _sinPhase = 0f;
                _cosPhase = 1f;
            }
            _normalizationCounter = 0;
        }
    }

    private void UpdateIncrement()
    {
        float increment = 2f * MathF.PI * _rateHz / _sampleRate;
        _sinIncrement = MathF.Sin(increment);
        _cosIncrement = MathF.Cos(increment);
    }

    private static float Sanitize(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, -1.35f, 1.35f) : 0f;
}
