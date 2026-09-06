using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Delay exclusivo para el loop. El procesamiento por muestra es de costo constante:
/// lectura, escritura y un filtro sencillo. No ejecuta análisis, raíces cuadradas ni
/// vigilancia de energía dentro del callback ASIO.
/// </summary>
internal sealed class LoopDelayEffect
{
    private const float SilenceThreshold = 1.0e-9f;
    private const float MaximumStoredSample = 1.05f;

    private readonly int _sampleRate;
    private readonly float[] _leftBuffer;
    private readonly float[] _rightBuffer;
    private readonly int[] _generationMarks;

    private int _generation = 1;
    private int _writeIndex;
    private int _delaySamples;
    private bool _enabled;
    private bool _analogMode;
    private bool _tapeMode;
    private bool _reverseMode;
    private int _reversePhase;
    private float _feedback;
    private float _mix;
    private float _feedbackFilterCoefficient = 0.46f;
    private float _feedbackLowPassLeft;
    private float _feedbackLowPassRight;
    private int _automaticResetCount;

    public LoopDelayEffect(int sampleRate)
    {
        _sampleRate = sampleRate;
        int maximumDelaySamples = sampleRate;
        _leftBuffer = new float[maximumDelaySamples + 2];
        _rightBuffer = new float[maximumDelaySamples + 2];
        _generationMarks = new int[maximumDelaySamples + 2];
        Configure(false, DelayCharacter.DigitalClean, 380f, 25f, 20f);
    }

    public int AutomaticResetCount => Volatile.Read(ref _automaticResetCount);

    public void Configure(bool enabled, DelayCharacter character, float timeMs, float feedbackPercent, float mixPercent)
    {
        int newDelaySamples = (int)MathF.Round(
            Math.Clamp(timeMs, 1f, 1000f) * _sampleRate / 1000f);
        newDelaySamples = Math.Clamp(newDelaySamples, 1, _leftBuffer.Length - 2);

        bool newAnalogMode = character == DelayCharacter.AnalogDark;
        bool newTapeMode = character == DelayCharacter.Tape;
        bool newReverseMode = character == DelayCharacter.Reverse;
        bool stateChanged = enabled != _enabled;
        bool timeChanged = newDelaySamples != _delaySamples;
        bool characterChanged = newAnalogMode != _analogMode || newTapeMode != _tapeMode || newReverseMode != _reverseMode;

        _enabled = enabled;
        _analogMode = newAnalogMode;
        _tapeMode = newTapeMode;
        _reverseMode = newReverseMode;
        _delaySamples = newDelaySamples;
        _feedback = Math.Clamp(feedbackPercent / 100f, 0f, 0.58f);
        _mix = Math.Clamp(mixPercent / 100f, 0f, 0.58f);
        _feedbackFilterCoefficient = _analogMode ? 0.095f : (_tapeMode ? 0.16f : 0.46f);

        if (stateChanged || timeChanged || characterChanged)
        {
            InvalidateStoredAudio();
        }
    }

    public void Process(float inputLeft, float inputRight, out float outputLeft, out float outputRight)
    {
        inputLeft = Sanitize(inputLeft);
        inputRight = Sanitize(inputRight);

        if (!_enabled || _mix <= 0f)
        {
            outputLeft = inputLeft;
            outputRight = inputRight;
            return;
        }

        int readIndex = _writeIndex - _delaySamples;
        if (readIndex < 0)
        {
            readIndex += _leftBuffer.Length;
        }

        bool sampleIsCurrent = _generationMarks[readIndex] == _generation;
        float delayedLeft = sampleIsCurrent ? Sanitize(_leftBuffer[readIndex]) : 0f;
        float delayedRight = sampleIsCurrent ? Sanitize(_rightBuffer[readIndex]) : 0f;

        _feedbackLowPassLeft += (delayedLeft - _feedbackLowPassLeft) * _feedbackFilterCoefficient;
        _feedbackLowPassRight += (delayedRight - _feedbackLowPassRight) * _feedbackFilterCoefficient;
        _feedbackLowPassLeft = FlushResidual(_feedbackLowPassLeft);
        _feedbackLowPassRight = FlushResidual(_feedbackLowPassRight);

        float repeatLeft = _analogMode ? AnalogColor(_feedbackLowPassLeft) : (_tapeMode ? TapeColor(_feedbackLowPassLeft, _reversePhase) : delayedLeft);
        float repeatRight = _analogMode ? AnalogColor(_feedbackLowPassRight) : (_tapeMode ? TapeColor(_feedbackLowPassRight, _reversePhase + 137) : delayedRight);
        if (_reverseMode)
        {
            // Ventana triangular invertida: carácter reverse ambiental sin asignaciones en tiempo real.
            float phase = (_reversePhase % Math.Max(2, _delaySamples)) / (float)Math.Max(2, _delaySamples);
            float envelope = phase < 0.5f ? phase * 2f : (1f - phase) * 2f;
            repeatLeft = -delayedLeft * envelope;
            repeatRight = -delayedRight * envelope;
        }
        _reversePhase++;
        float feedbackLeft = _analogMode ? repeatLeft : _feedbackLowPassLeft;
        float feedbackRight = _analogMode ? repeatRight : _feedbackLowPassRight;

        float storedLeft = inputLeft + (feedbackLeft * _feedback);
        float storedRight = inputRight + (feedbackRight * _feedback);

        _leftBuffer[_writeIndex] = Math.Clamp(Sanitize(storedLeft), -MaximumStoredSample, MaximumStoredSample);
        _rightBuffer[_writeIndex] = Math.Clamp(Sanitize(storedRight), -MaximumStoredSample, MaximumStoredSample);
        _generationMarks[_writeIndex] = _generation;

        float dry = 1f - _mix;
        outputLeft = Sanitize((inputLeft * dry) + (repeatLeft * _mix));
        outputRight = Sanitize((inputRight * dry) + (repeatRight * _mix));

        _writeIndex++;
        if (_writeIndex >= _leftBuffer.Length)
        {
            _writeIndex = 0;
        }
    }

    public void Reset() => InvalidateStoredAudio();

    public void ResetAfterFault()
    {
        Interlocked.Increment(ref _automaticResetCount);
        InvalidateStoredAudio();
    }

    private static float TapeColor(float value, int phase)
    {
        float wobble = 0.985f + 0.015f * MathF.Sin(phase * 0.00073f);
        return AnalogColor(value * wobble) * 0.96f;
    }

    private static float AnalogColor(float value)
    {
        // Aproximación racional de saturación suave: evita MathF.Tanh en cada muestra.
        float x = Math.Clamp(value * 1.12f, -2.5f, 2.5f);
        float squared = x * x;
        float curved = x * (27f + squared) / (27f + (9f * squared));
        return curved * 0.84f;
    }

    private void InvalidateStoredAudio()
    {
        _generation++;
        if (_generation == int.MaxValue)
        {
            Array.Clear(_generationMarks);
            _generation = 1;
        }

        _writeIndex = 0;
        _feedbackLowPassLeft = 0f;
        _feedbackLowPassRight = 0f;
        _reversePhase = 0;
    }

    private static float Sanitize(float value)
    {
        if (!float.IsFinite(value))
        {
            return 0f;
        }

        return FlushResidual(Math.Clamp(value, -1.35f, 1.35f));
    }

    private static float FlushResidual(float value) =>
        MathF.Abs(value) < SilenceThreshold ? 0f : value;
}
