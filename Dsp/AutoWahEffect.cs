using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Wah previo al amplificador con dos formas de movimiento: seguidor de envolvente
/// y posición manual preparada para pedal de expresión MIDI. Las tres voces comparten
/// un SVF estable, pero cambian rango, curva, resonancia y mezcla de banda/low/high.
/// No realiza asignaciones ni trigonometría dentro del callback ASIO.
/// </summary>
internal sealed class AutoWahEffect
{
    private const int TableSize = 256;
    private const float TableMinHz = 140f;
    private const float TableMaxHz = 4400f;

    private readonly int _sampleRate;
    private readonly float[] _frequencyCoefficients = new float[TableSize];
    private bool _enabled;
    private AutoWahMode _mode = AutoWahMode.Dynamic;
    private AutoWahCharacter _character = AutoWahCharacter.Classic;
    private float _sensitivity = 5f;
    private float _range = 6f;
    private float _resonance = 6f;
    private float _envelope;
    private float _referencePeak;
    private float _low;
    private float _band;
    private float _attackCoeff;
    private float _releaseCoeff;
    private float _referenceReleaseCoeff;
    private int _minTableIndex;
    private int _maxTableIndex;
    private float _damping = 0.72f;
    private float _wetGain = 1.65f;
    private float _mixCurrent;
    private float _mixTarget;
    private readonly float _mixRampStep;
    private float _manualPositionCurrent = 0.5f;
    private float _manualPositionTarget = 0.5f;
    private readonly float _positionRampStep;

    public AutoWahEffect(int sampleRate)
    {
        _sampleRate = sampleRate;
        _mixRampStep = 1f / MathF.Max(1f, sampleRate * 0.015f);
        _positionRampStep = 1f / MathF.Max(1f, sampleRate * 0.010f);
        for (int i = 0; i < TableSize; i++)
        {
            float t = i / (float)(TableSize - 1);
            float frequency = TableMinHz * MathF.Pow(TableMaxHz / TableMinHz, t);
            _frequencyCoefficients[i] = 2f * MathF.Sin(MathF.PI * frequency / sampleRate);
        }

        _attackCoeff = TimeCoefficient(2.5f);
        _releaseCoeff = TimeCoefficient(115f);
        _referenceReleaseCoeff = TimeCoefficient(700f);
        Configure(false, AutoWahMode.Dynamic, AutoWahCharacter.Classic, 5f, 6f, 6f, 50f);
    }

    public void Configure(bool enabled, AutoWahMode mode, AutoWahCharacter character,
        float sensitivity, float range, float resonance, float manualPositionPercent)
    {
        _enabled = enabled;
        _mixTarget = enabled ? 1f : 0f;
        _mode = Enum.IsDefined(typeof(AutoWahMode), mode) ? mode : AutoWahMode.Dynamic;
        _character = Enum.IsDefined(typeof(AutoWahCharacter), character) ? character : AutoWahCharacter.Classic;
        _sensitivity = float.IsFinite(sensitivity) ? Math.Clamp(sensitivity, 0f, 10f) : 5f;
        _range = float.IsFinite(range) ? Math.Clamp(range, 0f, 10f) : 6f;
        _resonance = float.IsFinite(resonance) ? Math.Clamp(resonance, 0f, 10f) : 6f;
        _manualPositionTarget = float.IsFinite(manualPositionPercent)
            ? Math.Clamp(manualPositionPercent / 100f, 0f, 1f)
            : 0.5f;

        float minHz;
        float maxHz;
        float resonanceAmount = _resonance / 10f;
        switch (_character)
        {
            case AutoWahCharacter.VaiBadHorsie:
                // Voz inspirada en Bad Horsie: mantiene cuerpo en talón, pero abre antes
                // hacia medios/agudos para evitar que el barrido quede excesivamente grave.
                minHz = 235f + (_range * 14f);      // 235 .. 375 Hz
                maxHz = 1750f + (_range * 260f);   // 1750 .. 4350 Hz
                _damping = 1.12f - (resonanceAmount * 0.82f);
                _wetGain = 1.48f + (resonanceAmount * 0.78f);
                _attackCoeff = TimeCoefficient(2.6f);
                _releaseCoeff = TimeCoefficient(132f);
                break;

            case AutoWahCharacter.SatrianiBigBad:
                // Voz más adelantada y agresiva, con medios vocales y leve empuje de solo.
                minHz = 255f + (_range * 15f);      // 255 .. 405 Hz
                maxHz = 1850f + (_range * 245f);   // 1850 .. 4300 Hz
                _damping = 1.03f - (resonanceAmount * 0.83f);
                _wetGain = 1.58f + (resonanceAmount * 0.92f);
                _attackCoeff = TimeCoefficient(2.0f);
                _releaseCoeff = TimeCoefficient(125f);
                break;

            default:
                minHz = 230f + (_range * 14f);
                maxHz = 1450f + (_range * 245f);
                _damping = 1.16f - (resonanceAmount * 0.96f);
                _wetGain = 1.45f + (resonanceAmount * 0.95f);
                _attackCoeff = TimeCoefficient(2.5f);
                _releaseCoeff = TimeCoefficient(115f);
                break;
        }

        _damping = Math.Clamp(_damping, 0.20f, 1.16f);
        _minTableIndex = FrequencyToIndex(minHz);
        _maxTableIndex = Math.Max(_minTableIndex + 1, FrequencyToIndex(maxHz));
    }

    public float Process(float input, float detectorInput)
    {
        input = SanitizeAudio(input);
        detectorInput = SanitizeDetector(detectorInput);
        AdvanceMix();
        AdvanceManualPosition();
        if (_mixCurrent <= 0.0001f && !_enabled) return input;

        float movement = _mode == AutoWahMode.ManualExpression
            ? _manualPositionCurrent
            : CalculateEnvelopeMovement(detectorInput);

        movement = _character switch
        {
            AutoWahCharacter.VaiBadHorsie => OpenVaiSweep(movement),
            AutoWahCharacter.SatrianiBigBad => MathF.Sqrt(Math.Clamp(movement, 0f, 1f)),
            _ => movement
        };

        int tableIndex = _minTableIndex + (int)((_maxTableIndex - _minTableIndex) * movement);
        tableIndex = Math.Clamp(tableIndex, 0, TableSize - 1);
        float f = _frequencyCoefficients[tableIndex];

        float high = input - _low - (_damping * _band);
        _band += f * high;
        _low += f * _band;

        if (!float.IsFinite(_band) || !float.IsFinite(_low) || MathF.Abs(_band) > 8f || MathF.Abs(_low) > 8f)
        {
            ResetFilterState();
            return input;
        }

        float effected = _character switch
        {
            AutoWahCharacter.VaiBadHorsie =>
                SoftLimit((input * 0.075f) + (((_band * 1.08f) + (high * 0.065f) - (_low * 0.035f)) * _wetGain * 1.02f)),
            AutoWahCharacter.SatrianiBigBad =>
                SoftLimit((input * 0.055f) + (((_band * 1.18f) + (high * 0.20f) - (_low * 0.08f)) * _wetGain * 1.06f)),
            _ => SoftLimit((input * 0.08f) + (_band * _wetGain * 1.12f))
        };

        return SanitizeAudio((input * (1f - _mixCurrent)) + (effected * _mixCurrent));
    }

    public float Process(float input) => Process(input, input);

    private float CalculateEnvelopeMovement(float detectorInput)
    {
        float detector = MathF.Abs(detectorInput);
        float envCoeff = detector > _envelope ? _attackCoeff : _releaseCoeff;
        _envelope = detector + (envCoeff * (_envelope - detector));

        if (detector >= _referencePeak) _referencePeak = detector;
        else _referencePeak = detector + (_referenceReleaseCoeff * (_referencePeak - detector));

        if (!float.IsFinite(_envelope) || !float.IsFinite(_referencePeak))
        {
            ResetDetectorState();
            return 0f;
        }

        float reference = MathF.Max(_referencePeak, 0.012f);
        float relative = Math.Clamp(_envelope / reference, 0f, 1.15f);
        float sens = _sensitivity / 10f;
        float absoluteThreshold = 0.070f - (sens * 0.062f);
        float absoluteSpan = 0.22f - (sens * 0.11f);
        float absoluteDrive = Math.Clamp((_envelope - absoluteThreshold) / MathF.Max(0.025f, absoluteSpan), 0f, 1f);
        float relativeThreshold = 0.52f - (sens * 0.34f);
        float relativeMovement = Math.Clamp((relative - relativeThreshold) / MathF.Max(0.15f, 1f - relativeThreshold), 0f, 1f);
        return Math.Clamp(absoluteDrive * relativeMovement, 0f, 1f);
    }

    private void AdvanceMix()
    {
        if (_mixCurrent < _mixTarget) _mixCurrent = MathF.Min(_mixTarget, _mixCurrent + _mixRampStep);
        else if (_mixCurrent > _mixTarget) _mixCurrent = MathF.Max(_mixTarget, _mixCurrent - _mixRampStep);
    }

    private void AdvanceManualPosition()
    {
        if (_manualPositionCurrent < _manualPositionTarget)
            _manualPositionCurrent = MathF.Min(_manualPositionTarget, _manualPositionCurrent + _positionRampStep);
        else if (_manualPositionCurrent > _manualPositionTarget)
            _manualPositionCurrent = MathF.Max(_manualPositionTarget, _manualPositionCurrent - _positionRampStep);
    }

    public void Reset()
    {
        ResetFilterState();
        _manualPositionCurrent = _manualPositionTarget;
        _mixCurrent = _enabled ? 1f : 0f;
        _mixTarget = _mixCurrent;
    }

    private void ResetFilterState()
    {
        ResetDetectorState();
        _low = 0f;
        _band = 0f;
    }

    private void ResetDetectorState()
    {
        _envelope = 0f;
        _referencePeak = 0f;
    }

    private int FrequencyToIndex(float frequency)
    {
        frequency = Math.Clamp(frequency, TableMinHz, TableMaxHz);
        float normalized = MathF.Log(frequency / TableMinHz) / MathF.Log(TableMaxHz / TableMinHz);
        return Math.Clamp((int)MathF.Round(normalized * (TableSize - 1)), 0, TableSize - 1);
    }

    private float TimeCoefficient(float milliseconds)
    {
        float samples = MathF.Max(1f, milliseconds * 0.001f * _sampleRate);
        return MathF.Exp(-1f / samples);
    }

    private static float OpenVaiSweep(float value)
    {
        float x = Math.Clamp(value, 0f, 1f);
        // Curva de apertura temprana: conserva 0 y 1, pero lleva antes el pedal
        // a la zona vocal sin llegar a la apertura agresiva del carácter Satriani.
        return x * (1.35f - (0.35f * x));
    }

    private static float SanitizeAudio(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, -1.25f, 1.25f) : 0f;

    private static float SanitizeDetector(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, -2.5f, 2.5f) : 0f;

    private static float SoftLimit(float value)
    {
        if (!float.IsFinite(value)) return 0f;
        float x = Math.Clamp(value, -2.5f, 2.5f);
        float x2 = x * x;
        return Math.Clamp(x * (27f + x2) / (27f + (9f * x2)), -1.15f, 1.15f);
    }
}
