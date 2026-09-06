namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Cadena mono independiente para el micrófono de la entrada 1.
/// Usa un expander/gate suave (no un corte duro) y EQ de voz. Todo el estado
/// se mantiene preasignado para no reservar memoria dentro del callback ASIO.
/// </summary>
internal sealed class VoiceProcessor
{
    private readonly int _sampleRate;
    private readonly Biquad _highPass = new();
    private readonly Biquad _lowShelf = new();
    private readonly Biquad _midPeak = new();
    private readonly Biquad _highShelf = new();

    private bool _enabled;
    private bool _suppressorEnabled = true;
    private float _thresholdLinear;
    private float _closedGain;
    private float _releaseCoefficient;
    private float _envelope;
    private float _gateGain = 1f;
    private int _holdSamples;
    private int _holdCounter;
    private float _levelGain = 1f;
    private float _speechGain = 1f;
    private float _compressorEnvelope;
    private float _compressorGain = 1f;
    private float _noiseFloor = 0.0005f;
    private bool _speechDetected;
    // 2.41.30: pico retenido de la voz ya procesada para distinguir
    // captura fisica de problemas de supresor/EQ/ruteo.
    private float _outputPeak;

    public bool SpeechDetected => _speechDetected;
    public float OutputPeak => Volatile.Read(ref _outputPeak);

    public VoiceProcessor(int sampleRate)
    {
        _sampleRate = sampleRate;
        Configure(false, true, -48f, 30f, 220f, 90f, 0f, 0f, 0f, 100f);
    }

    public void Configure(bool enabled, bool suppressorEnabled, float thresholdDb,
        float reductionDb, float releaseMs, float highPassHz,
        float bassDb, float midDb, float trebleDb, float levelPercent)
    {
        _enabled = enabled;
        _suppressorEnabled = suppressorEnabled;

        thresholdDb = Math.Clamp(thresholdDb, -75f, -20f);
        reductionDb = Math.Clamp(reductionDb, 0f, 60f);
        releaseMs = Math.Clamp(releaseMs, 40f, 1000f);
        highPassHz = Math.Clamp(highPassHz, 50f, 180f);
        bassDb = Math.Clamp(bassDb, -12f, 12f);
        midDb = Math.Clamp(midDb, -12f, 12f);
        trebleDb = Math.Clamp(trebleDb, -12f, 12f);
        levelPercent = Math.Clamp(levelPercent, 0f, 150f);

        _thresholdLinear = MathF.Pow(10f, thresholdDb / 20f);
        _closedGain = MathF.Pow(10f, -reductionDb / 20f);
        float releaseSeconds = MathF.Max(0.04f, releaseMs / 1000f);
        // El valor elegido representa aproximadamente el tiempo hasta quedar a 1%
        // de la distancia respecto de la atenuación final.
        _releaseCoefficient = MathF.Exp(MathF.Log(0.01f) / (_sampleRate * releaseSeconds));
        _holdSamples = (int)(_sampleRate * 0.085f); // evita cortar finales de palabras
        _levelGain = levelPercent / 100f;

        _highPass.SetHighPass(_sampleRate, highPassHz);
        _lowShelf.SetLowShelf(_sampleRate, 160f, bassDb);
        _midPeak.SetPeak(_sampleRate, 1400f, 0.85f, midDb);
        _highShelf.SetHighShelf(_sampleRate, 4800f, trebleDb);
    }

    public float Process(float input)
    {
        if (!_enabled)
        {
            _speechDetected = false;
            return 0f;
        }

        float x = float.IsFinite(input) ? Math.Clamp(input, -1.25f, 1.25f) : 0f;
        x = _highPass.Process(x);

        if (_suppressorEnabled)
        {
            float level = MathF.Abs(x);
            // Ataque muy rápido para no perder consonantes; caída del detector más lenta.
            float envelopeCoeff = level > _envelope ? 0.22f : 0.0018f;
            _envelope += (level - _envelope) * envelopeCoeff;
            if (!float.IsFinite(_envelope)) _envelope = 0f;

            bool open = _envelope >= _thresholdLinear;
            if (open)
            {
                _holdCounter = _holdSamples;
            }
            else if (_holdCounter > 0)
            {
                _holdCounter--;
                open = true;
            }

            if (open)
            {
                // ~3 ms de apertura a 48 kHz, suficientemente rápida para voz.
                _gateGain += (1f - _gateGain) * 0.018f;
            }
            else
            {
                // Cierra gradualmente hasta la reducción elegida, nunca con un click duro.
                _gateGain = _closedGain + ((_gateGain - _closedGain) * _releaseCoefficient);
            }

            if (!float.IsFinite(_gateGain)) _gateGain = 1f;
            _gateGain = Math.Clamp(_gateGain, _closedGain, 1f);
            x *= _gateGain;
        }
        else
        {
            _gateGain += (1f - _gateGain) * 0.02f;
            x *= _gateGain;
        }

        x = _lowShelf.Process(x);
        x = _midPeak.Process(x);
        x = _highShelf.Process(x);

        // Ganancia automática sólo cuando hay voz claramente por encima del ruido.
        // El objetivo es elevar una voz baja sin amplificar el ambiente durante silencios.
        float absVoice = MathF.Abs(x);
        if (_envelope < _thresholdLinear * 0.72f)
        {
            _noiseFloor += (absVoice - _noiseFloor) * 0.00035f;
        }
        _noiseFloor = Math.Clamp(_noiseFloor, 0.00005f, 0.08f);

        bool speechPresent = _envelope >= MathF.Max(_thresholdLinear * 0.82f, _noiseFloor * 2.8f);
        _speechDetected = speechPresent;
        float desiredSpeechGain = 1f;
        if (speechPresent && _envelope > 0.0001f)
        {
            const float targetEnvelope = 0.18f; // aproximadamente -15 dBFS antes de compresión
            desiredSpeechGain = Math.Clamp(targetEnvelope / _envelope, 1f, 6.0f);
        }

        // Ataque más rápido para que no se pierda el comienzo de las frases.
        // La liberación es deliberadamente lenta para evitar que el nivel respire.
        float gainRate = desiredSpeechGain > _speechGain ? 0.0048f : 0.00010f;
        _speechGain += (desiredSpeechGain - _speechGain) * gainRate;
        if (!speechPresent)
        {
            // No reiniciar la ganancia de golpe entre palabras: conserva naturalidad
            // y evita que cada frase vuelva a comenzar demasiado baja.
            _speechGain += (1f - _speechGain) * 0.00008f;
        }
        _speechGain = Math.Clamp(_speechGain, 1f, 6.0f);
        x *= _speechGain;

        // Compresor de voz más uniforme, con relación aproximada 2,5:1.
        // La ganancia también se suaviza para que no aplaste sílabas ni bombee.
        float compressorLevel = MathF.Abs(x);
        float detectorRate = compressorLevel > _compressorEnvelope ? 0.10f : 0.0012f;
        _compressorEnvelope += (compressorLevel - _compressorEnvelope) * detectorRate;
        const float compressorThreshold = 0.14f;
        float desiredCompressorGain = 1f;
        if (_compressorEnvelope > compressorThreshold)
        {
            float over = _compressorEnvelope / compressorThreshold;
            float compressedOver = MathF.Pow(over, 1f / 2.5f);
            desiredCompressorGain = Math.Clamp(compressedOver / over, 0.32f, 1f);
        }
        float compressorGainRate = desiredCompressorGain < _compressorGain ? 0.025f : 0.0007f;
        _compressorGain += (desiredCompressorGain - _compressorGain) * compressorGainRate;
        _compressorGain = Math.Clamp(_compressorGain, 0.32f, 1f);
        x *= _compressorGain;

        // Makeup posterior a la compresión. En 2.38.3 se agregan aproximadamente
        // 2 dB respecto de 2.38.2, sin aumentar el piso de ruido durante silencios.
        x *= 1.82f * _levelGain;

        if (!float.IsFinite(x))
        {
            Reset();
            return 0f;
        }

        // Limitador suave final de voz antes de mezclar con la guitarra.
        float magnitude = MathF.Abs(x);
        if (magnitude > 0.92f)
        {
            float excess = magnitude - 0.92f;
            float limited = 0.92f + (0.079f * (excess / (0.079f + excess)));
            x = x < 0f ? -limited : limited;
        }
        float outputMagnitude = MathF.Abs(x);
        if (outputMagnitude > Volatile.Read(ref _outputPeak))
            Volatile.Write(ref _outputPeak, outputMagnitude);
        return x;
    }

    public void Reset()
    {
        _highPass.Reset();
        _lowShelf.Reset();
        _midPeak.Reset();
        _highShelf.Reset();
        _envelope = 0f;
        _gateGain = 1f;
        _holdCounter = 0;
        _speechGain = 1f;
        _compressorEnvelope = 0f;
        _compressorGain = 1f;
        _noiseFloor = 0.0005f;
        _speechDetected = false;
        Volatile.Write(ref _outputPeak, 0f);
    }
}
