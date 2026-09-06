namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Batería de acompañamiento sintetizada en tiempo real para práctica.
/// Mantiene el callback ASIO libre de archivos, timers y asignaciones. La versión 2.41.52
/// profundiza la humanización sin mover el reloj musical: además de microtiming y dinámica,
/// cada golpe cambia ligeramente de parche, bordona, resonancia, apertura y amortiguación.
/// 2.41.57 conserva los platillos cálidos de la versión anterior y agrega fraseo más humano:
/// variaciones sutiles de bombo entre compases, respiración dinámica y fills alternados de
/// caja y toms. Conserva la ganancia 1.45, el tempo y la sincronización exacta con F12.
/// </summary>
internal sealed class DrumMachineGenerator
{
    private readonly int _sampleRate;

    private bool _enabled;
    private float _bpm = 80f;
    private int _pattern;
    private float _volume = 0.35f;

    private double _samplesPerStep;
    private double _samplesUntilNextStep;
    private int _stepIndex;
    private int _stepsPerBar = 16;
    private int _barIndex;

    private int _kickRemaining;
    private int _kickAge;
    private int _kickTotal;
    private double _kickPhase;
    private double _kickSubPhase;
    private float _kickVelocity;
    private float _kickPitchScale = 1f;
    private float _kickBodyMix = 0.82f;
    private float _kickSubMix = 0.18f;
    private float _kickClickMix = 0.10f;
    private float _kickDamping = 6.7f;
    private float _kickSubFrequency = 43f;

    private int _snareRemaining;
    private int _snareAge;
    private int _snareTotal;
    private double _snarePhaseA;
    private double _snarePhaseB;
    private double _snarePhaseC;
    private double _snareWirePhase;
    private float _snarePreviousNoise;
    private float _snareVelocity;
    private float _snarePitchScale = 1f;
    private float _snareBodyFrequencyA = 184f;
    private float _snareBodyFrequencyB = 326f;
    private float _snareBodyMix = 1f;
    private float _snareWireMix = 1f;
    private float _snareDamping = 8.2f;
    private bool _snareGhost;

    private int _hatRemaining;
    private int _hatAge;
    private int _hatTotalSamples;
    private float _hatPreviousNoise;
    private double _hatMetalPhaseA;
    private double _hatMetalPhaseB;
    private double _hatMetalPhaseC;
    private float _hatVelocity;
    private float _hatPitchScale = 1f;
    private float _hatBrightness = 1f;
    private float _hatNoiseMix = 0.74f;
    private float _hatMetalMix = 0.18f;
    private float _hatDamping = 8.0f;
    private float _hatMetalFrequencyA = 6120f;
    private float _hatMetalFrequencyB = 8430f;

    private int _crashRemaining;
    private int _crashAge;
    private int _crashTotal;
    private float _crashPreviousNoise;
    private float _crashFastNoise;
    private float _crashSlowNoise;
    private float _crashBodyNoise;
    private double _crashPhaseA;
    private double _crashPhaseB;
    private double _crashPhaseC;
    private float _crashVelocity;
    private float _crashPitchScale = 1f;

    // Ride dedicado, afinado desde 2.41.56: menos ping aislado,
    // más cuerpo de bronce y una baqueta redondeada.
    private int _rideRemaining;
    private int _rideAge;
    private int _rideTotal;
    private float _ridePreviousNoise;
    private float _rideFastNoise;
    private float _rideSlowNoise;
    private float _rideBodyNoise;
    private double _ridePhaseA;
    private double _ridePhaseB;
    private double _ridePhaseC;
    private float _rideVelocity;
    private float _ridePitchScale = 1f;
    private float _rideBrightness = 1f;

    // Voz de tom para fills cortos. Un único tom cambia de afinación en cada golpe,
    // como un baterista recorriendo tom alto, medio y piso sin sumar voces artificiales.
    private int _tomRemaining;
    private int _tomAge;
    private int _tomTotal;
    private double _tomPhaseA;
    private double _tomPhaseB;
    private float _tomPreviousNoise;
    private float _tomVelocity;
    private float _tomBaseFrequency = 125f;
    private float _tomPitchScale = 1f;
    private float _tomDamping = 5.8f;

    // Room acústica corta de batería. Buffer fijo: se crea una vez y no asigna
    // memoria dentro del callback ASIO. Tres reflexiones tempranas unen el kit.
    private readonly float[] _roomBuffer;
    private readonly int _roomTapA;
    private readonly int _roomTapB;
    private readonly int _roomTapC;
    private int _roomIndex;
    private float _roomDamped;

    private uint _noiseState = 0x13579BDFu;

    public DrumMachineGenerator(int sampleRate)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        _roomBuffer = new float[Math.Max(256, (int)(_sampleRate * 0.042f))];
        _roomTapA = Math.Max(1, (int)(_sampleRate * 0.0097f));
        _roomTapB = Math.Max(1, (int)(_sampleRate * 0.0183f));
        _roomTapC = Math.Max(1, (int)(_sampleRate * 0.0311f));
        RecalculateTiming();
        Reset();
    }

    public void Configure(bool enabled, float bpm, int pattern, float volumePercent)
    {
        bpm = Math.Clamp(float.IsFinite(bpm) ? bpm : 80f, 40f, 240f);
        pattern = Math.Clamp(pattern, 0, 5);
        volumePercent = Math.Clamp(float.IsFinite(volumePercent) ? volumePercent : 35f, 0f, 100f);

        bool timingChanged = MathF.Abs(_bpm - bpm) > 0.0001f || _pattern != pattern;
        bool enabledChanged = _enabled != enabled;

        _enabled = enabled;
        _bpm = bpm;
        _pattern = pattern;
        _volume = volumePercent / 100f;

        if (timingChanged) RecalculateTiming();
        if (enabledChanged || timingChanged) Reset();
    }

    public float Process()
    {
        if (!_enabled) return 0f;

        if (_samplesUntilNextStep <= 0.0)
        {
            TriggerStep(_stepIndex);
            _stepIndex++;
            if (_stepIndex >= Math.Max(1, _stepsPerBar))
            {
                _stepIndex = 0;
                _barIndex++;
            }
            _samplesUntilNextStep += _samplesPerStep;
        }
        _samplesUntilNextStep -= 1.0;

        float sample = ProcessKick() + ProcessSnare() + ProcessHat() + ProcessCrash() + ProcessRide() + ProcessTom();
        float room = ProcessRoom(sample);
        // Room deliberadamente baja: debe sentirse como aire de micrófonos, no como reverb.
        // 2.41.53: se corrige únicamente la ganancia final del bus de batería.
        // +5,6 dB aprox. respecto de 2.41.52: 50-55 % queda cerca del nivel que antes
        // exigía 100 %, sin alterar dinámica, humanización, patrones ni sincronización.
        return Math.Clamp((sample + room * 0.13f) * _volume * 1.45f, -0.94f, 0.94f);
    }

    public void Reset()
    {
        _samplesUntilNextStep = 0.0;
        _stepIndex = 0;
        _barIndex = 0;
        _kickRemaining = _snareRemaining = _hatRemaining = _crashRemaining = _rideRemaining = _tomRemaining = 0;
        _kickAge = _snareAge = _hatAge = _crashAge = _rideAge = _tomAge = 0;
        _kickPhase = _kickSubPhase = 0.0;
        _snarePhaseA = _snarePhaseB = _snarePhaseC = _snareWirePhase = 0.0;
        _hatMetalPhaseA = _hatMetalPhaseB = _hatMetalPhaseC = 0.0;
        _crashPhaseA = _crashPhaseB = _crashPhaseC = 0.0;
        _ridePhaseA = _ridePhaseB = _ridePhaseC = 0.0;
        _tomPhaseA = _tomPhaseB = 0.0;
        _hatPreviousNoise = _snarePreviousNoise = _crashPreviousNoise = _ridePreviousNoise = _tomPreviousNoise = 0f;
        _crashFastNoise = _crashSlowNoise = _crashBodyNoise = 0f;
        _rideFastNoise = _rideSlowNoise = _rideBodyNoise = 0f;
        _snareGhost = false;
        _roomIndex = 0;
        _roomDamped = 0f;
        Array.Clear(_roomBuffer, 0, _roomBuffer.Length);
    }

    private void RecalculateTiming()
    {
        double samplesPerQuarter = _sampleRate * 60.0 / Math.Max(1.0, _bpm);
        if (_pattern == 4)
        {
            _stepsPerBar = 12;
            _samplesPerStep = samplesPerQuarter / 3.0;
        }
        else if (_pattern == 5)
        {
            _stepsPerBar = 12;
            _samplesPerStep = samplesPerQuarter / 2.0;
        }
        else
        {
            _stepsPerBar = 16;
            _samplesPerStep = samplesPerQuarter / 4.0;
        }
    }

    private void TriggerStep(int step)
    {
        bool kick = false;
        bool snare = false;
        bool hat = false;
        bool openHat = false;
        bool crash = false;
        bool ride = false;
        bool ghostSnare = false;
        int tomKind = 0; // 1 alto, 2 medio, 3 piso.
        float tomVelocity = 0.62f;
        float kickVelocity = 0.88f;
        float snareVelocity = 0.92f;
        float hatVelocity = (step & 1) == 0 ? 0.66f : 0.48f;
        float rideVelocity = 0.46f;

        switch (_pattern)
        {
            case 0: // Rock 4/4
                // El tercer bombo alterna entre anticipación y empuje final; el pulso base no cambia.
                kick = step == 0 || step == 8
                    || (((_barIndex & 1) == 0 && step == 10) || ((_barIndex & 1) != 0 && step == 14));
                kickVelocity = step == 0 ? 1f : step == 8 ? 0.90f : 0.70f;
                snare = step is 4 or 12;
                // En el tercer compás del ciclo el ride reemplaza al hi-hat y abre el kit.
                ride = (_barIndex % 4) == 2 && (step % 2) == 0;
                rideVelocity = step is 0 or 8 ? 0.57f : 0.45f;
                hat = !ride && (step % 2) == 0;
                openHat = !ride && step == 14;
                crash = step == 0 && (_barIndex % 2 == 0);
                break;

            case 1: // Worship 4/4: más aire y dinámica.
                kick = step == 0 || step == 8
                    || (((_barIndex & 1) == 0 && step == 7) || ((_barIndex & 1) != 0 && step == 14));
                kickVelocity = step == 0 ? 0.95f : step == 8 ? 0.86f : 0.67f;
                snare = step is 4 or 12;
                snareVelocity = 0.82f;
                // Ride de wash en los compases 3 y 4 del ciclo, típico de crecimiento worship.
                ride = (_barIndex % 4) >= 2 && (step % 2) == 0;
                rideVelocity = step is 0 or 8 ? 0.52f : 0.40f;
                hat = !ride && (step % 2) == 0;
                hatVelocity = step is 0 or 8 ? 0.62f : 0.48f;
                openHat = !ride && step == 14;
                // Crash cada dos compases y un acento algo mayor al iniciar el ciclo de cuatro.
                crash = step == 0 && (_barIndex % 2 == 0);
                break;

            case 2: // Pop 4/4
                kick = step == 0 || step == 8
                    || (((_barIndex & 1) == 0 && (step == 6 || step == 11))
                        || ((_barIndex & 1) != 0 && (step == 7 || step == 14)));
                kickVelocity = step is 0 or 8 ? 0.96f : 0.70f;
                snare = step is 4 or 12;
                ride = (_barIndex % 4) == 2 && (step is 0 or 4 or 8 or 12);
                rideVelocity = step is 0 or 8 ? 0.52f : 0.43f;
                hat = !ride && (step % 2) == 0;
                openHat = !ride && step == 14;
                crash = step == 0 && (_barIndex % 2 == 0);
                break;

            case 3: // Balada 4/4
                kick = step is 0 or 8;
                kickVelocity = step == 0 ? 0.84f : 0.72f;
                snare = step is 4 or 12;
                snareVelocity = 0.70f;
                ride = (_barIndex % 4) == 2 && (step is 0 or 4 or 8 or 12);
                rideVelocity = 0.36f;
                hat = !ride && (step % 2) == 0;
                hatVelocity = 0.42f;
                crash = step == 0 && (_barIndex % 4 == 0);
                break;

            case 4: // Blues shuffle
                kick = step is 0 or 6 or 8;
                kickVelocity = step == 0 ? 0.92f : 0.74f;
                snare = step is 3 or 9;
                snareVelocity = 0.88f;
                ride = (_barIndex & 1) != 0 && (step is 0 or 2 or 3 or 5 or 6 or 8 or 9 or 11);
                rideVelocity = step is 0 or 6 ? 0.54f : 0.42f;
                hat = !ride && (step is 0 or 2 or 3 or 5 or 6 or 8 or 9 or 11);
                hatVelocity = step is 0 or 6 ? 0.66f : 0.48f;
                openHat = !ride && step == 11;
                crash = step == 0 && (_barIndex % 4 == 0);
                break;

            case 5: // Worship 6/8
                kick = step is 0 or 8;
                kickVelocity = step == 0 ? 0.94f : 0.72f;
                snare = step == 6;
                snareVelocity = 0.80f;
                ride = (_barIndex % 4) >= 2 && (step % 2) == 0;
                rideVelocity = step is 0 or 6 ? 0.49f : 0.38f;
                hat = !ride && (step % 2) == 0;
                hatVelocity = step is 0 or 6 ? 0.58f : 0.44f;
                openHat = !ride && step == 10;
                crash = step == 0 && (_barIndex % 2 == 0);
                break;
        }

        // Fraseo de baterista: ghost notes regulares, fill corto de caja al cuarto compás
        // y recorrido de toms sólo al octavo. Así la base respira sin rellenar cada vuelta.
        bool tomFillBar = (_barIndex % 8) == 7;
        bool snareFillBar = (_barIndex % 8) == 3;
        if (_pattern <= 3)
        {
            if ((step == 3 && (_barIndex & 1) != 0) || (step == 11 && (_barIndex & 1) == 0))
                ghostSnare = !snare;

            if (tomFillBar && step is 13 or 14 or 15)
            {
                tomKind = step == 13 ? 1 : step == 14 ? 2 : 3;
                tomVelocity = step == 13 ? 0.56f : step == 14 ? 0.62f : 0.72f;
                snare = false;
                ghostSnare = false;
                if (step >= 14) hat = false;
            }
            else if (snareFillBar && step is 13 or 14 or 15)             {                 snare = true;                 ghostSnare = false;                 float rollScale = _pattern == 3 ? 0.90f : _pattern == 1 ? 0.96f : 1.0f;                 snareVelocity = (step == 13 ? 0.53f : step == 14 ? 0.70f : 0.88f) * rollScale;                 if (step >= 14)                 {                     hat = false;                     ride = false;                 }             }
        }
        else
        {
            if (step == 5 && (_barIndex & 1) != 0) ghostSnare = !snare;

            if (tomFillBar && step is 9 or 10 or 11)
            {
                tomKind = step == 9 ? 1 : step == 10 ? 2 : 3;
                tomVelocity = step == 9 ? 0.54f : step == 10 ? 0.61f : 0.70f;
                snare = false;
                ghostSnare = false;
                if (step >= 10) hat = false;
            }
            else if (snareFillBar &&                      ((_pattern == 4 && (step is 10 or 11)) ||                       (_pattern == 5 && (step is 9 or 10 or 11))))             {                 snare = true;                 ghostSnare = false;                 snareVelocity = _pattern == 4                     ? (step == 10 ? 0.65f : 0.86f)                     : (step == 9 ? 0.49f : step == 10 ? 0.66f : 0.84f);                 hat = false;                 ride = false;             }
        }

        // Crescendo/relajación microscópicos dentro de cada grupo de cuatro compases.
        // Son variaciones de ejecución, no automatización perceptible de volumen.
        float phraseBreath = 0.982f + (_barIndex % 4) * 0.010f;
        kickVelocity *= phraseBreath;
        snareVelocity *= phraseBreath;
        hatVelocity *= 0.992f + (_barIndex % 4) * 0.006f;
        rideVelocity *= 0.988f + (_barIndex % 4) * 0.007f;

        // En un golpe de crash de tiempo 1 no sumamos además el ride; el crash ocupa ese espacio
        // y el ride entra desde el golpe siguiente, como en una interpretación real.
        if (crash) ride = false;

        // El bombo queda casi clavado; caja y platos pueden caer apenas detrás.
        // Los retardos son de pocos milisegundos y por eso la banda sigue arrancando
        // exactamente junta desde el tiempo 1 cuando F12 detecta la guitarra.
        // 2.41.52: el reloj sigue fijo, pero el timbre de cada pieza respira como un kit real.
        // El hi-hat alterna mano/acento y caja/hat pueden quedar apenas detrás de la grilla.
        if (hat && !openHat)
        {
            float handAccent = (step & 3) == 0 ? 1.04f : (step & 1) == 0 ? 0.96f : 0.90f;
            hatVelocity *= handAccent;
        }
        if (kick) StartKick(kickVelocity * Humanize(0.070f), MicroDelaySamples(0.75f));
        if (snare) StartSnare(snareVelocity * Humanize(0.095f), false, MicroDelaySamples(2.8f));
        else if (ghostSnare) StartSnare(0.23f * Humanize(0.16f), true, MicroDelaySamples(3.3f));
        if (hat) StartHat(openHat, hatVelocity * Humanize(0.13f), MicroDelaySamples(4.0f));
        if (ride) StartRide(rideVelocity * Humanize(0.085f), MicroDelaySamples(2.1f));
        if (tomKind != 0) StartTom(tomKind, tomVelocity * Humanize(0.075f), MicroDelaySamples(2.4f));
        if (crash)
        {
            float crashAccent = (_barIndex % 4) == 0 ? 0.62f : 0.52f;
            StartCrash(crashAccent * Humanize(0.075f), MicroDelaySamples(1.4f));
        }
    }

    private float Humanize(float amount) => 1f + (NextNoise() * amount);

    private int MicroDelaySamples(float maxMilliseconds)
    {
        float normalized = (NextNoise() + 1f) * 0.5f;
        return Math.Max(0, (int)(_sampleRate * (maxMilliseconds * 0.001f) * normalized));
    }

    private void StartKick(float velocity, int delaySamples = 0)
    {
        // Un parche real cambia levemente según dónde y con qué fuerza pega el mazo.
        float length = 0.172f + NextNoise() * 0.016f;
        _kickTotal = Math.Max(1, (int)(_sampleRate * Math.Clamp(length, 0.145f, 0.205f)));
        _kickRemaining = _kickTotal;
        _kickAge = -Math.Max(0, delaySamples);
        _kickPhase = (NextNoise() + 1.0) * 0.035;
        _kickSubPhase = (NextNoise() + 1.0) * 0.025;
        _kickVelocity = Math.Clamp(velocity, 0.40f, 1.1f);
        _kickPitchScale = 1f + NextNoise() * 0.028f;
        _kickBodyMix = Math.Clamp(0.78f + NextNoise() * 0.055f, 0.69f, 0.86f);
        _kickSubMix = Math.Clamp(0.15f + NextNoise() * 0.045f, 0.08f, 0.22f);
        _kickClickMix = Math.Clamp(0.070f + NextNoise() * 0.035f + velocity * 0.025f, 0.035f, 0.14f);
        _kickDamping = Math.Clamp(6.15f + NextNoise() * 0.72f, 5.2f, 7.2f);
        _kickSubFrequency = Math.Clamp(41.0f + NextNoise() * 2.4f, 37f, 46f);
    }

    private void StartSnare(float velocity, bool ghost = false, int delaySamples = 0)
    {
        float nominal = ghost ? 0.073f : 0.158f;
        float variation = ghost ? 0.012f : 0.022f;
        _snareTotal = Math.Max(1, (int)(_sampleRate * Math.Clamp(nominal + NextNoise() * variation, ghost ? 0.052f : 0.118f, ghost ? 0.095f : 0.205f)));
        _snareRemaining = _snareTotal;
        _snareAge = -Math.Max(0, delaySamples);
        _snarePhaseA = (NextNoise() + 1.0) * 0.23;
        _snarePhaseB = (NextNoise() + 1.0) * 0.17;
        _snarePhaseC = (NextNoise() + 1.0) * 0.11;
        _snareWirePhase = (NextNoise() + 1.0) * 0.29;
        _snarePreviousNoise = NextNoise() * 0.08f;
        _snareVelocity = Math.Clamp(velocity, ghost ? 0.10f : 0.32f, 1.1f);
        _snarePitchScale = 1f + NextNoise() * (ghost ? 0.055f : 0.034f);
        _snareBodyFrequencyA = 176f + NextNoise() * 14f;
        _snareBodyFrequencyB = 315f + NextNoise() * 28f;
        _snareBodyMix = Math.Clamp((ghost ? 0.52f : 0.92f) + NextNoise() * 0.13f, 0.35f, 1.08f);
        _snareWireMix = Math.Clamp((ghost ? 0.50f : 0.88f) + NextNoise() * 0.16f, 0.28f, 1.08f);
        _snareDamping = Math.Clamp((ghost ? 10.2f : 7.55f) + NextNoise() * 0.95f, ghost ? 8.6f : 6.1f, ghost ? 12.0f : 9.2f);
        _snareGhost = ghost;
    }

    private void StartHat(bool open, float velocity, int delaySamples = 0)
    {
        float nominal = open ? 0.205f : 0.052f;
        float spread = open ? 0.050f : 0.014f;
        _hatTotalSamples = Math.Max(1, (int)(_sampleRate * Math.Clamp(nominal + NextNoise() * spread, open ? 0.13f : 0.030f, open ? 0.29f : 0.078f)));
        _hatRemaining = _hatTotalSamples;
        _hatAge = -Math.Max(0, delaySamples);
        _hatPreviousNoise = NextNoise() * 0.12f;
        _hatMetalPhaseA = (NextNoise() + 1.0) * 0.31;
        _hatMetalPhaseB = (NextNoise() + 1.0) * 0.19;
        _hatMetalPhaseC = (NextNoise() + 1.0) * 0.13;
        _hatVelocity = Math.Clamp(velocity, 0.20f, 1.0f);
        _hatPitchScale = 1f + NextNoise() * 0.050f;
        _hatBrightness = Math.Clamp(0.84f + NextNoise() * 0.20f + (open ? 0.03f : 0f), 0.58f, 1.12f);
        _hatNoiseMix = Math.Clamp((open ? 0.78f : 0.70f) + NextNoise() * 0.10f, 0.56f, 0.90f);
        _hatMetalMix = Math.Clamp((open ? 0.16f : 0.13f) + NextNoise() * 0.055f, 0.07f, 0.23f);
        _hatDamping = Math.Clamp((open ? 4.7f : 8.5f) + NextNoise() * (open ? 0.8f : 1.2f), open ? 3.6f : 6.5f, open ? 6.0f : 10.8f);
        _hatMetalFrequencyA = 5850f + NextNoise() * 520f;
        _hatMetalFrequencyB = 8170f + NextNoise() * 720f;
    }

    private void StartCrash(float velocity, int delaySamples = 0)
    {
        // Crash más oscuro y abierto: la cola gana aire sin depender de tonos agudos fijos.
        float duration = 0.97f + NextNoise() * 0.09f;
        _crashTotal = Math.Max(1, (int)(_sampleRate * Math.Clamp(duration, 0.82f, 1.10f)));
        _crashRemaining = _crashTotal;
        _crashAge = -Math.Max(0, delaySamples);
        _crashPreviousNoise = 0f;
        _crashFastNoise = NextNoise() * 0.020f;
        _crashSlowNoise = _crashFastNoise;
        _crashBodyNoise = _crashSlowNoise;
        _crashPhaseA = (NextNoise() + 1.0) * 0.22;
        _crashPhaseB = (NextNoise() + 1.0) * 0.18;
        _crashPhaseC = (NextNoise() + 1.0) * 0.12;
        _crashVelocity = Math.Clamp(velocity, 0.2f, 0.9f);
        _crashPitchScale = 1f + NextNoise() * 0.012f;
    }

    private void StartRide(float velocity, int delaySamples = 0)
    {
        // Ride dark/warm: baqueta integrada al cuerpo y wash de bronce dominante.
        float duration = 0.56f + NextNoise() * 0.075f;
        _rideTotal = Math.Max(1, (int)(_sampleRate * Math.Clamp(duration, 0.44f, 0.68f)));
        _rideRemaining = _rideTotal;
        _rideAge = -Math.Max(0, delaySamples);
        _ridePreviousNoise = NextNoise() * 0.020f;
        _rideFastNoise = NextNoise() * 0.020f;
        _rideSlowNoise = _rideFastNoise;
        _rideBodyNoise = _rideSlowNoise;
        _ridePhaseA = (NextNoise() + 1.0) * 0.21;
        _ridePhaseB = (NextNoise() + 1.0) * 0.17;
        _ridePhaseC = (NextNoise() + 1.0) * 0.11;
        _rideVelocity = Math.Clamp(velocity, 0.20f, 0.72f);
        _ridePitchScale = 1f + NextNoise() * 0.008f;
        _rideBrightness = Math.Clamp(0.66f + NextNoise() * 0.070f, 0.56f, 0.76f);
    }

    private void StartTom(int kind, float velocity, int delaySamples = 0)
    {
        float nominal = kind == 1 ? 0.235f : kind == 2 ? 0.275f : 0.335f;
        float duration = nominal + NextNoise() * 0.022f;
        _tomTotal = Math.Max(1, (int)(_sampleRate * Math.Clamp(duration, 0.18f, 0.39f)));
        _tomRemaining = _tomTotal;
        _tomAge = -Math.Max(0, delaySamples);
        _tomPhaseA = (NextNoise() + 1.0) * 0.16;
        _tomPhaseB = (NextNoise() + 1.0) * 0.12;
        _tomPreviousNoise = NextNoise() * 0.04f;
        _tomVelocity = Math.Clamp(velocity, 0.22f, 0.92f);
        _tomBaseFrequency = kind == 1 ? 176f : kind == 2 ? 132f : 92f;
        _tomBaseFrequency += NextNoise() * (kind == 3 ? 4.0f : 6.5f);
        _tomPitchScale = 1f + NextNoise() * 0.022f;
        _tomDamping = Math.Clamp((kind == 3 ? 4.75f : kind == 2 ? 5.25f : 5.75f) + NextNoise() * 0.45f, 4.1f, 6.4f);
    }

    private float ProcessKick()
    {
        if (_kickRemaining <= 0) return 0f;
        if (_kickAge < 0) { _kickAge++; return 0f; }
        float progress = Math.Clamp(_kickAge / (float)Math.Max(1, _kickTotal), 0f, 1f);
        float envelope = MathF.Exp(-_kickDamping * progress);
        float frequency = (47f + 94f * MathF.Exp(-12.5f * progress)) * _kickPitchScale;
        _kickPhase += 2.0 * Math.PI * frequency / _sampleRate;
        _kickSubPhase += 2.0 * Math.PI * _kickSubFrequency / _sampleRate;
        float membrane = MathF.Sin((float)_kickPhase);
        float body = (membrane * _kickBodyMix) + (MathF.Sin((float)_kickSubPhase) * _kickSubMix);
        // El pequeño segundo modo de parche evita el "boop" senoidal idéntico.
        body += MathF.Sin((float)(_kickPhase * 1.47)) * 0.055f * (1f - progress);
        float click = _kickAge < Math.Max(1, _sampleRate / 760) ? NextNoise() * _kickClickMix : 0f;
        _kickAge++;
        _kickRemaining--;
        return (body + click) * envelope * 0.78f * _kickVelocity;
    }

    private float ProcessSnare()
    {
        if (_snareRemaining <= 0) return 0f;
        if (_snareAge < 0) { _snareAge++; return 0f; }
        float progress = Math.Clamp(_snareAge / (float)Math.Max(1, _snareTotal), 0f, 1f);
        float envelope = MathF.Exp(-_snareDamping * progress);
        float rawNoise = NextNoise();
        float brightNoise = rawNoise - (_snarePreviousNoise * (0.66f + 0.08f * progress));
        _snarePreviousNoise = rawNoise;
        _snarePhaseA += 2.0 * Math.PI * _snareBodyFrequencyA * _snarePitchScale / _sampleRate;
        _snarePhaseB += 2.0 * Math.PI * _snareBodyFrequencyB * _snarePitchScale / _sampleRate;
        _snarePhaseC += 2.0 * Math.PI * (_snareBodyFrequencyA * 2.63f) * _snarePitchScale / _sampleRate;
        _snareWirePhase += 2.0 * Math.PI * 73.0 / _sampleRate;
        float bodyScale = _snareGhost ? 0.52f : 1f;
        float wireScale = _snareGhost ? 0.56f : 1f;
        float body = ((MathF.Sin((float)_snarePhaseA) * 0.22f)
            + (MathF.Sin((float)_snarePhaseB) * 0.085f)
            + (MathF.Sin((float)_snarePhaseC) * 0.038f * (1f - progress))) * bodyScale * _snareBodyMix;
        float initialCrack = MathF.Max(0f, 1f - progress * 7.0f);
        float wireFlutter = 0.92f + 0.08f * MathF.Sin((float)_snareWirePhase);
        float wires = brightNoise * (0.58f + 0.22f * initialCrack + 0.10f * (1f - progress)) * wireFlutter * wireScale * _snareWireMix;
        float stick = _snareAge < Math.Max(1, _sampleRate / 950)
            ? NextNoise() * 0.085f * (1f - _snareAge / (float)Math.Max(1, _sampleRate / 950))
            : 0f;
        _snareAge++;
        _snareRemaining--;
        return (body + wires + stick) * envelope * 0.46f * _snareVelocity;
    }

    private float ProcessHat()
    {
        if (_hatRemaining <= 0) return 0f;
        if (_hatAge < 0) { _hatAge++; return 0f; }
        float progress = Math.Clamp(_hatAge / (float)Math.Max(1, _hatTotalSamples), 0f, 1f);
        float envelope = MathF.Exp(-_hatDamping * progress);
        float noise = NextNoise();
        float high = noise - (_hatPreviousNoise * (0.82f + 0.08f * _hatBrightness));
        _hatPreviousNoise = noise;
        _hatMetalPhaseA += 2.0 * Math.PI * _hatMetalFrequencyA * _hatPitchScale / _sampleRate;
        _hatMetalPhaseB += 2.0 * Math.PI * _hatMetalFrequencyB * _hatPitchScale / _sampleRate;
        _hatMetalPhaseC += 2.0 * Math.PI * (_hatMetalFrequencyA * 1.73f) * (2f - _hatPitchScale) / _sampleRate;
        float metal = (MathF.Sin((float)_hatMetalPhaseA)
            + (0.52f * MathF.Sin((float)_hatMetalPhaseB))
            + (0.31f * MathF.Sin((float)_hatMetalPhaseC))) * _hatMetalMix * _hatBrightness;
        float stick = _hatAge < Math.Max(1, _sampleRate / 1450) ? NextNoise() * 0.085f * _hatBrightness : 0f;
        _hatAge++;
        _hatRemaining--;
        return ((high * _hatNoiseMix) + metal + stick) * envelope * 0.14f * _hatVelocity;
    }

    private float ProcessCrash()
    {
        if (_crashRemaining <= 0) return 0f;
        if (_crashAge < 0) { _crashAge++; return 0f; }
        float progress = Math.Clamp(_crashAge / (float)Math.Max(1, _crashTotal), 0f, 1f);
        float washEnvelope = MathF.Exp(-3.10f * progress);
        float bodyEnvelope = MathF.Exp(-5.15f * progress);

        float noise = NextNoise();
        // Tres seguidores generan aire, cuerpo medio y masa grave. La suma es ancha,
        // no una colección de picos agudos fijos.
        _crashFastNoise += (noise - _crashFastNoise) * 0.43f;
        _crashSlowNoise += (_crashFastNoise - _crashSlowNoise) * 0.078f;
        _crashBodyNoise += (_crashSlowNoise - _crashBodyNoise) * 0.024f;
        float air = _crashFastNoise - _crashSlowNoise;
        float bronzeBand = _crashSlowNoise - _crashBodyNoise;
        float broadWash = bronzeBand * 0.88f + air * 0.24f;
        _crashPreviousNoise = noise;

        // Dos resonancias bajas con modulación lenta dan forma de bronce sin "nota de lata".
        _crashPhaseA += 2.0 * Math.PI * 690.0 * _crashPitchScale / _sampleRate;
        _crashPhaseB += 2.0 * Math.PI * 1125.0 * (2f - _crashPitchScale) / _sampleRate;
        _crashPhaseC += 2.0 * Math.PI * 79.0 / _sampleRate;
        float modulation = MathF.Sin((float)_crashPhaseC);
        float bronzeBody = MathF.Sin((float)(_crashPhaseA + modulation * 0.28f)) * 0.030f
            + MathF.Sin((float)(_crashPhaseB - modulation * 0.19f)) * 0.016f;

        _crashAge++;
        _crashRemaining--;
        return ((broadWash * washEnvelope * 0.86f) + (bronzeBody * bodyEnvelope))
            * 0.300f * _crashVelocity;
    }

    private float ProcessRide()
    {
        if (_rideRemaining <= 0) return 0f;
        if (_rideAge < 0) { _rideAge++; return 0f; }

        float progress = Math.Clamp(_rideAge / (float)Math.Max(1, _rideTotal), 0f, 1f);
        float bodyEnvelope = MathF.Exp(-7.0f * progress);
        float washEnvelope = MathF.Exp(-2.95f * progress);

        float noise = NextNoise();
        _rideFastNoise += (noise - _rideFastNoise) * 0.40f;
        _rideSlowNoise += (_rideFastNoise - _rideSlowNoise) * 0.073f;
        _rideBodyNoise += (_rideSlowNoise - _rideBodyNoise) * 0.020f;
        float air = _rideFastNoise - _rideSlowNoise;
        float bronzeBand = _rideSlowNoise - _rideBodyNoise;
        float warmWash = bronzeBand * 0.92f + air * 0.18f;
        _ridePreviousNoise = noise;

        // Ping bajo y levemente modulado: identifica la baqueta sin sobresalir como chapa.
        _ridePhaseA += 2.0 * Math.PI * 735.0 * _ridePitchScale / _sampleRate;
        _ridePhaseB += 2.0 * Math.PI * 1185.0 * (2f - _ridePitchScale) / _sampleRate;
        _ridePhaseC += 2.0 * Math.PI * 83.0 / _sampleRate;
        float modulation = MathF.Sin((float)_ridePhaseC);
        float bellBody = MathF.Sin((float)(_ridePhaseA + modulation * 0.34f)) * 0.036f
            + MathF.Sin((float)(_ridePhaseB - modulation * 0.22f)) * 0.018f;

        int stickLength = Math.Max(1, _sampleRate / 920);
        float stickEnvelope = _rideAge < stickLength ? 1f - _rideAge / (float)stickLength : 0f;
        float stick = stickEnvelope > 0f
            ? (NextNoise() * 0.014f + MathF.Sin((float)_ridePhaseA) * 0.018f) * stickEnvelope
            : 0f;
        float wash = warmWash * 0.58f * _rideBrightness;

        _rideAge++;
        _rideRemaining--;
        return ((bellBody * _rideBrightness + stick) * bodyEnvelope + wash * washEnvelope)
            * 0.600f * _rideVelocity;
    }

    private float ProcessTom()
    {
        if (_tomRemaining <= 0) return 0f;
        if (_tomAge < 0) { _tomAge++; return 0f; }

        float progress = Math.Clamp(_tomAge / (float)Math.Max(1, _tomTotal), 0f, 1f);
        float envelope = MathF.Exp(-_tomDamping * progress);
        // Ligera caída de afinación del parche: evita el tom senoidal estático.
        float frequency = _tomBaseFrequency * _tomPitchScale * (1f + 0.16f * MathF.Exp(-10.5f * progress));
        _tomPhaseA += 2.0 * Math.PI * frequency / _sampleRate;
        _tomPhaseB += 2.0 * Math.PI * (frequency * 1.56f) / _sampleRate;

        float membrane = MathF.Sin((float)_tomPhaseA) * 0.79f
            + MathF.Sin((float)_tomPhaseB) * 0.18f * (1f - progress * 0.45f);
        float rawNoise = NextNoise();
        float attackNoise = rawNoise - _tomPreviousNoise * 0.62f;
        _tomPreviousNoise = rawNoise;
        float attackEnvelope = MathF.Max(0f, 1f - progress * 22f);
        float stick = attackNoise * 0.18f * attackEnvelope;

        _tomAge++;
        _tomRemaining--;
        return (membrane + stick) * envelope * 0.58f * _tomVelocity;
    }

    private float ProcessRoom(float input)
    {
        int length = _roomBuffer.Length;
        int a = _roomIndex - _roomTapA; if (a < 0) a += length;
        int b = _roomIndex - _roomTapB; if (b < 0) b += length;
        int c = _roomIndex - _roomTapC; if (c < 0) c += length;

        float early = _roomBuffer[a] * 0.50f + _roomBuffer[b] * 0.31f + _roomBuffer[c] * 0.19f;
        // La amortiguación simula absorción de una sala pequeña y evita una cola metálica.
        _roomDamped += (early - _roomDamped) * 0.055f;
        _roomBuffer[_roomIndex] = Math.Clamp(input * 0.62f + _roomDamped * 0.16f, -1f, 1f);
        _roomIndex++;
        if (_roomIndex >= length) _roomIndex = 0;
        return early * 0.72f + _roomDamped * 0.28f;
    }

    private float NextNoise()
    {
        _noiseState ^= _noiseState << 13;
        _noiseState ^= _noiseState >> 17;
        _noiseState ^= _noiseState << 5;
        return ((_noiseState & 0x00FFFFFFu) / 8388607.5f) - 1f;
    }
}
