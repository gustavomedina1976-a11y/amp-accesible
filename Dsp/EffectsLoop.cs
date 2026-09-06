using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Loop post gabinete. Phaser, flanger, ambos chorus y delay viven aquí; el retorno usa fundido y no duplica
/// la señal seca. La entrada del delay queda protegida contra picos del chorus.
/// </summary>
internal sealed class EffectsLoop
{
    private readonly PhaserEffect _phaser;
    private readonly FlangerEffect _flanger;
    private readonly ChorusEffect _chorus;
    private readonly AnalogChorusEffect _analogChorus;
    private readonly MicroPitchEffect _microPitch;
    private readonly RotarySpeakerEffect _rotary;
    private readonly TremoloEffect _tremolo;
    private readonly LoopDelayEffect _delay;

    private bool _enabled = true;
    private float _sendGain = 1f;
    private float _returnMix = 1f;
    private float _makeupGain = 1f;
    private float _inputEnvelope;
    private float _outputEnvelope;

    public EffectsLoop(int sampleRate)
    {
        _phaser = new PhaserEffect(sampleRate);
        _flanger = new FlangerEffect(sampleRate);
        _chorus = new ChorusEffect(sampleRate);
        _analogChorus = new AnalogChorusEffect(sampleRate);
        _microPitch = new MicroPitchEffect(sampleRate);
        _rotary = new RotarySpeakerEffect(sampleRate);
        _tremolo = new TremoloEffect(sampleRate);
        _delay = new LoopDelayEffect(sampleRate);
    }

    public int DelayAutomaticResetCount => _delay.AutomaticResetCount;

    public void Configure(
        bool loopEnabled,
        float sendPercent,
        float returnPercent,
        bool phaserEnabled,
        float phaserRateHz,
        float phaserDepthPercent,
        float phaserFeedbackPercent,
        float phaserMixPercent,
        bool flangerEnabled,
        FlangerCharacter flangerCharacter,
        float flangerRateHz,
        float flangerDepthPercent,
        float flangerFeedbackPercent,
        float flangerMixPercent,
        bool chorusEnabled,
        ChorusCharacter chorusCharacter,
        float chorusRateHz,
        float chorusDepthMs,
        float chorusMixPercent,
        bool analogChorusEnabled,
        float analogChorusRateHz,
        float analogChorusDepth,
        float analogChorusMixPercent,
        float analogChorusLow,
        float analogChorusHigh,
        bool microPitchEnabled,
        float microPitchDetuneCents,
        float microPitchDelayMs,
        float microPitchMixPercent,
        bool rotaryEnabled,
        bool rotaryFast,
        float rotaryRateHz,
        float rotaryDepthPercent,
        float rotaryMixPercent,
        bool tremoloEnabled,
        float tremoloRateHz,
        float tremoloDepthPercent,
        bool delayEnabled,
        DelayCharacter delayCharacter,
        float delayTimeMs,
        float delayFeedbackPercent,
        float delayMixPercent)
    {
        float newSendGain = Math.Clamp(sendPercent / 100f, 0f, 1f);
        float newReturnMix = Math.Clamp(returnPercent / 100f, 0f, 1f);
        bool wasBypassed = !_enabled || _sendGain <= 0f || _returnMix <= 0f;
        bool willBeBypassed = !loopEnabled || newSendGain <= 0f || newReturnMix <= 0f;
        bool bypassStateChanged = wasBypassed != willBeBypassed;

        _enabled = loopEnabled;
        _sendGain = newSendGain;
        _returnMix = newReturnMix;

        _phaser.Configure(phaserEnabled, phaserRateHz, phaserDepthPercent, phaserFeedbackPercent, phaserMixPercent);
        _flanger.Configure(flangerEnabled, flangerCharacter, flangerRateHz, flangerDepthPercent, flangerFeedbackPercent, flangerMixPercent);
        float effectiveRate = chorusCharacter == ChorusCharacter.Dimension ? MathF.Max(0.12f, chorusRateHz * 0.42f) : chorusRateHz;
        float effectiveDepth = chorusCharacter == ChorusCharacter.Dimension ? MathF.Max(2.5f, chorusDepthMs * 0.58f) : chorusDepthMs;
        float effectiveMix = chorusCharacter == ChorusCharacter.Dimension ? MathF.Min(72f, chorusMixPercent * 1.12f) : chorusMixPercent;
        _chorus.Configure(chorusEnabled, effectiveRate, effectiveDepth, effectiveMix);
        _analogChorus.Configure(analogChorusEnabled, analogChorusRateHz, analogChorusDepth,
            analogChorusMixPercent, analogChorusLow, analogChorusHigh);
        _microPitch.Configure(microPitchEnabled, microPitchDetuneCents, microPitchDelayMs, microPitchMixPercent);

        // Cuando cualquiera de los chorus trabaja con mezcla alta, el feedback del delay
        // se reduce suavemente para conservar margen y evitar acumulación de picos.
        // Esto conserva un chorus notorio sin volver inestable el delay.
        _rotary.Configure(rotaryEnabled, rotaryFast, rotaryRateHz, rotaryDepthPercent, rotaryMixPercent);
        _tremolo.Configure(tremoloEnabled, tremoloRateHz, tremoloDepthPercent);

        float requestedFeedback = Math.Clamp(delayFeedbackPercent, 0f, 60f);
        if (chorusEnabled || analogChorusEnabled)
        {
            float strongestMix = MathF.Max(chorusEnabled ? chorusMixPercent : 0f,
                analogChorusEnabled ? analogChorusMixPercent : 0f);
            float excess = Math.Clamp((strongestMix - 55f) / 45f, 0f, 1f);
            requestedFeedback *= 1f - (excess * 0.35f);
        }

        _delay.Configure(delayEnabled, delayCharacter, delayTimeMs, requestedFeedback, delayMixPercent);

        if (bypassStateChanged)
        {
            Reset();
        }
    }

    public void Process(float input, out float outputLeft, out float outputRight)
    {
        ProcessInternal(input, includeDelay: true, out outputLeft, out outputRight);
    }

    /// <summary>
    /// Ruta liviana para protección de carga. Mantiene el chorus y el carácter estéreo,
    /// pero omite temporalmente el delay. Así la simulación no parece apagarse.
    /// </summary>
    public void ProcessChorusOnly(float input, out float outputLeft, out float outputRight)
    {
        ProcessInternal(input, includeDelay: false, out outputLeft, out outputRight);
    }

    private void ProcessInternal(float input, bool includeDelay, out float outputLeft, out float outputRight)
    {
        input = Sanitize(input);
        if (!_enabled || _returnMix <= 0f || _sendGain <= 0f)
        {
            outputLeft = input;
            outputRight = input;
            return;
        }

        float sent = input * _sendGain;
        float phased = _phaser.Process(sent);
        float flanged = _flanger.Process(phased);
        _chorus.Process(flanged, out float loopLeft, out float loopRight);
        _analogChorus.Process(loopLeft, loopRight, out float analogLeft, out float analogRight);
        _microPitch.Process(analogLeft, analogRight, out float pitchLeft, out float pitchRight);
        _rotary.Process(pitchLeft, pitchRight, out float rotaryLeft, out float rotaryRight);
        _tremolo.ProcessStereo(rotaryLeft, rotaryRight, out loopLeft, out loopRight);

        // Normalización conjunta: conserva el estéreo y evita que el chorus entregue
        // al delay un pico por encima del margen seguro.
        float peak = MathF.Max(MathF.Abs(loopLeft), MathF.Abs(loopRight));
        if (peak > 0.94f)
        {
            float scale = 0.94f / peak;
            loopLeft *= scale;
            loopRight *= scale;
        }

        if (includeDelay)
        {
            try
            {
                _delay.Process(loopLeft, loopRight, out loopLeft, out loopRight);
            }
            catch
            {
                _delay.ResetAfterFault();
                loopLeft = sent;
                loopRight = sent;
            }
        }

        float dryMix = 1f - _returnMix;
        float mixedLeft = (input * dryMix) + (loopLeft * _returnMix);
        float mixedRight = (input * dryMix) + (loopRight * _returnMix);

        // Compensación por envolvente, no por muestra instantánea. La 2.38 comparaba
        // valores absolutos de una sola muestra y podía reaccionar de forma errática.
        // Esta versión compara niveles suavizados, conserva los ataques y sólo corrige
        // pérdidas sostenidas producidas por chorus, flanger, Leslie o delay.
        float inputLevel = MathF.Abs(input);
        float outputLevel = (MathF.Abs(mixedLeft) + MathF.Abs(mixedRight)) * 0.5f;
        float inputRate = inputLevel > _inputEnvelope ? 0.012f : 0.00055f;
        float outputRate = outputLevel > _outputEnvelope ? 0.012f : 0.00055f;
        _inputEnvelope += (inputLevel - _inputEnvelope) * inputRate;
        _outputEnvelope += (outputLevel - _outputEnvelope) * outputRate;

        float desiredMakeup = 1f;
        if (_inputEnvelope > 0.0015f && _outputEnvelope > 0.00045f)
        {
            desiredMakeup = Math.Clamp(_inputEnvelope / _outputEnvelope, 0.90f, 1.65f);
        }

        float makeupRate = desiredMakeup > _makeupGain ? 0.00075f : 0.00020f;
        _makeupGain += (desiredMakeup - _makeupGain) * makeupRate;
        _makeupGain = Math.Clamp(_makeupGain, 0.90f, 1.65f);

        outputLeft = Sanitize(mixedLeft * _makeupGain);
        outputRight = Sanitize(mixedRight * _makeupGain);
    }

    public void ResetDelay() => _delay.Reset();

    public void Reset()
    {
        _phaser.Reset();
        _flanger.Reset();
        _chorus.Reset();
        _analogChorus.Reset();
        _microPitch.Reset();
        _rotary.Reset();
        _tremolo.Reset();
        _delay.Reset();
        _makeupGain = 1f;
        _inputEnvelope = 0f;
        _outputEnvelope = 0f;
    }

    private static float Sanitize(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, -1.35f, 1.35f) : 0f;
}
