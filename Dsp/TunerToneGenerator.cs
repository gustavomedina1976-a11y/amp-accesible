using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Guía sonora accesible: tono grave si la cuerda está baja, tono agudo si está alta
/// y dos pulsos si está afinada. También reproduce tonos de referencia para guitarra.
/// </summary>
internal sealed class TunerToneGenerator
{
    private readonly int _sampleRate;
    private bool _guideEnabled;
    private float _volume;
    private int _directionValue;
    private long _guideCounter;
    private double _phase;
    private int _referenceSamplesRemaining;
    private int _referenceSamplesTotal;
    private float _referenceFrequency;

    public TunerToneGenerator(int sampleRate)
    {
        _sampleRate = sampleRate;
    }

    public void Configure(bool guideEnabled, float volumePercent)
    {
        _guideEnabled = guideEnabled;
        _volume = Math.Clamp(volumePercent, 0f, 100f) / 100f * 0.30f;
    }

    public void SetDirection(TuningDirection direction)
    {
        int value = (int)direction;
        if (Volatile.Read(ref _directionValue) != value)
        {
            Volatile.Write(ref _directionValue, value);
            _guideCounter = 0;
            _phase = 0.0;
        }
    }

    public void PlayReferenceTone(float frequencyHz, float durationSeconds = 5.0f)
    {
        _referenceFrequency = Math.Clamp(frequencyHz, 60f, 1200f);
        _referenceSamplesTotal = Math.Max(1, (int)(_sampleRate * Math.Clamp(durationSeconds, 0.2f, 8f)));
        Interlocked.Exchange(ref _referenceSamplesRemaining, _referenceSamplesTotal);
        _phase = 0.0;
    }

    public float Process()
    {
        int remaining = Volatile.Read(ref _referenceSamplesRemaining);
        if (remaining > 0)
        {
            float value = GenerateReferenceTone(remaining);
            Interlocked.Decrement(ref _referenceSamplesRemaining);
            return value;
        }

        TuningDirection direction = (TuningDirection)Volatile.Read(ref _directionValue);
        if (!_guideEnabled || direction == TuningDirection.NoSignal || _volume <= 0f)
        {
            return 0f;
        }

        const float cycleSeconds = 0.82f;
        int cycleSamples = Math.Max(1, (int)(_sampleRate * cycleSeconds));
        int position = (int)(_guideCounter % cycleSamples);
        _guideCounter++;

        float frequency;
        int beepStart;
        int beepLength;
        bool active;

        switch (direction)
        {
            case TuningDirection.Flat:
                frequency = 330f;
                beepStart = 0;
                beepLength = (int)(_sampleRate * 0.11f);
                active = position >= beepStart && position < beepStart + beepLength;
                break;
            case TuningDirection.Sharp:
                frequency = 880f;
                beepStart = 0;
                beepLength = (int)(_sampleRate * 0.11f);
                active = position >= beepStart && position < beepStart + beepLength;
                break;
            case TuningDirection.InTune:
                frequency = 660f;
                beepLength = (int)(_sampleRate * 0.075f);
                int secondStart = (int)(_sampleRate * 0.15f);
                active = position < beepLength || (position >= secondStart && position < secondStart + beepLength);
                beepStart = position < beepLength ? 0 : secondStart;
                break;
            default:
                return 0f;
        }

        if (!active)
        {
            return 0f;
        }

        int localPosition = position - beepStart;
        float envelope = MathF.Sin(MathF.PI * localPosition / Math.Max(1, beepLength));
        return NextSine(frequency) * envelope * _volume;
    }

    private float GenerateReferenceTone(int remaining)
    {
        int elapsed = _referenceSamplesTotal - remaining;
        int fadeSamples = Math.Max(1, (int)(_sampleRate * 0.025f));
        float envelope = 1f;
        if (elapsed < fadeSamples)
        {
            envelope = elapsed / (float)fadeSamples;
        }
        else if (remaining < fadeSamples)
        {
            envelope = remaining / (float)fadeSamples;
        }

        return NextSine(_referenceFrequency) * envelope * _volume;
    }

    private float NextSine(float frequency)
    {
        float sample = (float)Math.Sin(_phase);
        _phase += 2.0 * Math.PI * frequency / _sampleRate;
        if (_phase >= 2.0 * Math.PI)
        {
            _phase -= 2.0 * Math.PI;
        }
        return sample;
    }

    public void Reset()
    {
        Volatile.Write(ref _directionValue, (int)TuningDirection.NoSignal);
        _guideCounter = 0;
        _phase = 0.0;
        Interlocked.Exchange(ref _referenceSamplesRemaining, 0);
    }
}
