namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Metrónomo muy liviano para el hilo ASIO. No crea timers, buffers ni objetos dentro
/// del callback: genera un click corto y lo mezcla directamente con la salida estéreo.
/// </summary>
internal sealed class MetronomeGenerator
{
    private readonly int _sampleRate;
    private readonly int _clickLengthSamples;

    private bool _enabled;
    private float _bpm = 80f;
    private int _beatsPerBar = 4;
    private bool _accentFirstBeat = true;
    private float _volume = 0.25f;

    private double _samplesPerBeat;
    private double _samplesUntilNextBeat;
    private int _beatIndex;
    private int _clickSamplesRemaining;
    private int _clickSampleIndex;
    private float _clickFrequency;

    public MetronomeGenerator(int sampleRate)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        _clickLengthSamples = Math.Max(32, (int)(_sampleRate * 0.026f));
        RecalculateTiming();
        Reset();
    }

    public void Configure(bool enabled, float bpm, int beatsPerBar, bool accentFirstBeat, float volumePercent)
    {
        bpm = Math.Clamp(float.IsFinite(bpm) ? bpm : 80f, 40f, 240f);
        beatsPerBar = Math.Clamp(beatsPerBar, 2, 12);
        volumePercent = Math.Clamp(float.IsFinite(volumePercent) ? volumePercent : 25f, 0f, 100f);

        bool timingChanged = MathF.Abs(_bpm - bpm) > 0.0001f || _beatsPerBar != beatsPerBar;
        bool enabledChanged = _enabled != enabled;

        _enabled = enabled;
        _bpm = bpm;
        _beatsPerBar = beatsPerBar;
        _accentFirstBeat = accentFirstBeat;
        _volume = volumePercent / 100f;

        if (timingChanged)
        {
            RecalculateTiming();
        }

        if (enabledChanged || timingChanged)
        {
            Reset();
        }
    }

    public float Process()
    {
        if (!_enabled || _volume <= 0f)
        {
            return 0f;
        }

        if (_samplesUntilNextBeat <= 0.0)
        {
            StartClick();
            _samplesUntilNextBeat += _samplesPerBeat;
        }
        _samplesUntilNextBeat -= 1.0;

        if (_clickSamplesRemaining <= 0)
        {
            return 0f;
        }

        float progress = _clickSampleIndex / (float)_clickLengthSamples;
        float envelope = 1f - Math.Clamp(progress, 0f, 1f);
        envelope *= envelope;
        float phase = 2f * MathF.PI * _clickFrequency * _clickSampleIndex / _sampleRate;
        float sample = MathF.Sin(phase) * envelope * _volume * 0.42f;

        _clickSampleIndex++;
        _clickSamplesRemaining--;
        return sample;
    }

    public void Reset()
    {
        _samplesUntilNextBeat = 0.0;
        _beatIndex = 0;
        _clickSamplesRemaining = 0;
        _clickSampleIndex = 0;
    }

    private void RecalculateTiming()
    {
        _samplesPerBeat = _sampleRate * 60.0 / Math.Max(1.0, _bpm);
    }

    private void StartClick()
    {
        bool accented = _accentFirstBeat && _beatIndex == 0;
        _clickFrequency = accented ? 1320f : 880f;
        _clickSamplesRemaining = _clickLengthSamples;
        _clickSampleIndex = 0;
        _beatIndex = (_beatIndex + 1) % Math.Max(1, _beatsPerBar);
    }
}
