namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Bajo de acompañamiento sintetizado en tiempo real. Comparte BPM y patrón con la
/// batería, pero mantiene encendido y volumen independientes. No usa archivos,
/// MIDI, timers ni asignaciones dentro del callback ASIO.
/// </summary>
internal sealed class BassAccompanimentGenerator
{
    private static readonly int[] MajorLine = { 0, 7, 9, 7, 4, 7, 9, 11 };
    private static readonly int[] MinorLine = { 0, 7, 8, 7, 3, 5, 7, 10 };

    private readonly int _sampleRate;
    private readonly float _filterAlpha;

    private bool _enabled;
    private float _bpm = 80f;
    private int _pattern = 1;
    private float _volume = 0.28f;
    private int _key;
    private bool _minor;
    private int _lineMode = 1;

    private double _samplesPerStep;
    private double _samplesUntilNextStep;
    private int _stepsPerBar = 16;
    private int _stepIndex;
    private int _barIndex;
    private int _noteEventIndex;

    private int _noteRemaining;
    private int _noteAge;
    private int _noteTotal;
    private double _phase1;
    private double _phase2;
    private double _phase3;
    private double _phase4;
    private float _frequency;
    private float _filterState;
    private float _bodyState;
    private float _bodyResonanceState;
    private float _noiseToneState;
    private float _velocity;
    private float _noteDetuneRatio = 1f;
    private float _noteBrightness = 1f;
    private float _harmonic2Gain = 0.14f;
    private float _harmonic3Gain = 0.055f;
    private float _harmonic4Gain = 0.025f;
    private bool _slapPop;
    private bool _slapGhost;
    private uint _noiseState = 0x6D2B79F5u;

    public BassAccompanimentGenerator(int sampleRate)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        // Un polo suave alrededor de 1,2 kHz conserva el cuerpo y quita brillo sintético.
        _filterAlpha = Math.Clamp((float)(2.0 * Math.PI * 1200.0 / _sampleRate), 0.01f, 0.35f);
        RecalculateTiming();
        Reset();
    }

    public void Configure(bool enabled, float bpm, int pattern, float volumePercent,
        int key, bool minor, int lineMode)
    {
        bpm = Math.Clamp(float.IsFinite(bpm) ? bpm : 80f, 40f, 240f);
        pattern = Math.Clamp(pattern, 0, 5);
        volumePercent = Math.Clamp(float.IsFinite(volumePercent) ? volumePercent : 28f, 0f, 100f);
        key = Math.Clamp(key, 0, 11);
        lineMode = Math.Clamp(lineMode, 0, 4);

        bool timingChanged = MathF.Abs(_bpm - bpm) > 0.0001f || _pattern != pattern;
        bool musicalChanged = _key != key || _minor != minor || _lineMode != lineMode;
        bool enabledChanged = _enabled != enabled;

        _enabled = enabled;
        _bpm = bpm;
        _pattern = pattern;
        _volume = volumePercent / 100f;
        _key = key;
        _minor = minor;
        _lineMode = lineMode;

        if (timingChanged) RecalculateTiming();
        if (enabledChanged || timingChanged || musicalChanged) Reset();
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
                _noteEventIndex = 0;
            }
            _samplesUntilNextStep += _samplesPerStep;
        }
        _samplesUntilNextStep -= 1.0;

        float lineGain = _lineMode == 4 ? 1.24f : 0.80f;
        return Math.Clamp(ProcessNote() * _volume * lineGain, -0.88f, 0.88f);
    }

    public void Reset()
    {
        _samplesUntilNextStep = 0.0;
        _stepIndex = 0;
        _barIndex = 0;
        _noteEventIndex = 0;
        _noteRemaining = 0;
        _noteAge = 0;
        _phase1 = _phase2 = _phase3 = _phase4 = 0.0;
        _filterState = 0f;
        _bodyState = 0f;
        _bodyResonanceState = 0f;
        _noiseToneState = 0f;
        _slapPop = false;
        _slapGhost = false;
    }

    private void RecalculateTiming()
    {
        double samplesPerQuarter = _sampleRate * 60.0 / Math.Max(1.0, _bpm);
        if (_pattern == 4)
        {
            // Shuffle: 12 subdivisiones ternarias por compás.
            _stepsPerBar = 12;
            _samplesPerStep = samplesPerQuarter / 3.0;
        }
        else if (_pattern == 5)
        {
            // Worship 6/8: 12 corcheas por el ciclo de dos pulsos grandes.
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
        // 2.41.47: Slap contundente. Usa silencios, golpes de pulgar y pops en octava
        // para que el acompañamiento tenga movimiento sin convertirse en un solo largo.
        if (_lineMode == 4)
        {
            TriggerSlapStep(step);
            return;
        }

        _slapPop = false;
        _slapGhost = false;
        bool trigger;
        float durationSteps;
        float velocity;

        switch (_pattern)
        {
            case 0: // Rock: negras firmes, pickup opcional al final.
                trigger = step is 0 or 4 or 8 or 12 || (_lineMode == 3 && step == 14);
                durationSteps = step == 14 ? 1.6f : 3.7f;
                velocity = step is 0 or 8 ? 1.0f : 0.88f;
                break;

            case 1: // Worship: más espacio y anticipación suave.
                trigger = step is 0 or 8 || (_lineMode != 0 && step == 14);
                durationSteps = step == 14 ? 1.7f : 7.4f;
                velocity = step == 0 ? 0.94f : step == 8 ? 0.84f : 0.67f;
                break;

            case 2: // Pop: corcheas parejas.
                trigger = (step % 2) == 0;
                durationSteps = 1.75f;
                velocity = step is 0 or 8 ? 0.96f : 0.76f;
                break;

            case 3: // Balada: blancas largas.
                trigger = step is 0 or 8;
                durationSteps = 7.6f;
                velocity = step == 0 ? 0.88f : 0.76f;
                break;

            case 4: // Blues shuffle: negras sobre subdivisión ternaria.
                trigger = step is 0 or 3 or 6 or 9 || (_lineMode == 3 && step == 11);
                durationSteps = step == 11 ? 0.9f : 2.7f;
                velocity = step is 0 or 6 ? 0.94f : 0.80f;
                break;

            case 5: // Worship 6/8: dos pulsos grandes y pickup opcional.
                trigger = step is 0 or 6 || (_lineMode != 0 && step == 10);
                durationSteps = step == 10 ? 1.6f : 5.5f;
                velocity = step == 0 ? 0.92f : step == 6 ? 0.80f : 0.64f;
                break;

            default:
                trigger = false;
                durationSteps = 1f;
                velocity = 0.8f;
                break;
        }

        if (!trigger) return;

        int semitoneOffset = SelectPitchOffset(_noteEventIndex++);
        StartNote(semitoneOffset, durationSteps, velocity);
    }

    private void TriggerSlapStep(int step)
    {
        bool ternary = _pattern is 4 or 5;
        bool trigger;
        bool pop;
        bool ghost;
        float durationSteps;
        float velocity;

        if (ternary)
        {
            // 12 subdivisiones: thumb, pops y dos notas muertas. Las ghost notes son
            // parte importante del lenguaje slap y rompen la sensación de secuenciador.
            trigger = step is 0 or 2 or 3 or 5 or 6 or 8 or 9 or 11;
            pop = step is 2 or 5 or 9 or 11;
            ghost = step is 3 or 8;
            durationSteps = ghost ? 0.18f : pop ? 0.28f : (step is 0 or 6 ? 0.68f : 0.44f);
            velocity = ghost ? 0.72f : step is 0 or 6 ? 1.13f : pop ? 1.20f : 0.94f;
        }
        else
        {
            // 16 subdivisiones: thumb en pulsos, ghost notes y pops de octava.
            trigger = step is 0 or 2 or 3 or 6 or 7 or 8 or 10 or 11 or 14 or 15;
            pop = step is 3 or 7 or 11 or 15;
            ghost = step is 2 or 10;
            durationSteps = ghost ? 0.17f : pop ? 0.26f : (step is 0 or 8 ? 0.66f : 0.42f);
            velocity = ghost ? 0.70f : step is 0 or 8 ? 1.13f : pop ? 1.22f : 0.95f;
        }

        if (!trigger) return;

        int eventIndex = _noteEventIndex++;
        int semitoneOffset;
        if (ghost)
        {
            semitoneOffset = 0;
        }
        else if (pop)
        {
            // Pops de octava/quinta alta; alternan para evitar monotonía.
            semitoneOffset = (eventIndex & 1) == 0 ? 12 : 19;
        }
        else
        {
            // Thumb: raíz, quinta y ocasional aproximación melódica consonante.
            semitoneOffset = (eventIndex % 4) switch
            {
                0 => 0,
                1 => 7,
                2 => 0,
                _ => _minor ? 3 : 4
            };
        }

        _slapPop = pop;
        _slapGhost = ghost;
        StartNote(semitoneOffset, durationSteps, velocity);
    }

    private int SelectPitchOffset(int eventIndex)
    {
        return _lineMode switch
        {
            0 => 0, // Raíz
            1 => (eventIndex & 1) == 0 ? 0 : 7, // Raíz - quinta
            2 => (eventIndex & 1) == 0 ? 0 : 12, // Octavas
            _ => SelectMelodicOffset(eventIndex)
        };
    }

    private int SelectMelodicOffset(int eventIndex)
    {
        // Frase corta y consonante. El tercer grado cambia entre mayor y menor.
        int[] sequence = _minor ? MinorLine : MajorLine;
        return sequence[eventIndex % sequence.Length];
    }

    private void StartNote(int semitoneOffset, float durationSteps, float velocity)
    {
        int rootMidi = 36 + _key; // C2 como referencia.
        if (rootMidi > 40) rootMidi -= 12; // F a B bajan una octava para conservar registro de bajo.
        int midi = Math.Clamp(rootMidi + semitoneOffset, 24, 55);
        _frequency = 440f * MathF.Pow(2f, (midi - 69) / 12f);

        _noteTotal = Math.Max(1, (int)(_samplesPerStep * Math.Max(0.4f, durationSteps)));
        _noteRemaining = _noteTotal;
        _noteAge = 0;
        // Una cuerda real no reinicia siempre con la misma fase. Variar el punto de
        // arranque evita el carácter de oscilador idéntico nota tras nota.
        float phaseSeed = (NextNoise() + 1f) * 0.5f;
        _phase1 = phaseSeed;
        _phase2 = (phaseSeed * 0.57 + 0.19) % 1.0;
        _phase3 = (phaseSeed * 0.31 + 0.43) % 1.0;
        _phase4 = (phaseSeed * 0.79 + 0.11) % 1.0;

        // 2.41.48: pequeñas variaciones de ejecución por nota. La posición virtual
        // del dedo/pulgar cambia qué armónicos recoge la pastilla y evita que cada
        // nota sea una copia idéntica de la anterior. Se calcula sólo al disparar
        // la nota, nunca por muestra.
        float articulation = (NextNoise() + 1f) * 0.5f;
        float pickupPosition = _lineMode == 4
            ? 0.105f + (articulation * 0.075f)
            : 0.18f + (articulation * 0.12f);
        float detuneCents = NextNoise() * (_lineMode == 4 ? 1.45f : 0.72f);
        _noteDetuneRatio = MathF.Pow(2f, detuneCents / 1200f);
        _noteBrightness = 0.86f + (articulation * 0.28f);
        _harmonic2Gain = 0.075f + (0.18f * MathF.Abs(MathF.Sin(2f * MathF.PI * pickupPosition)));
        _harmonic3Gain = 0.020f + (0.115f * MathF.Abs(MathF.Sin(3f * MathF.PI * pickupPosition)));
        _harmonic4Gain = 0.010f + (0.070f * MathF.Abs(MathF.Sin(4f * MathF.PI * pickupPosition)));

        _bodyState = 0f;
        _bodyResonanceState = 0f;
        _noiseToneState = 0f;
        float humanVelocity = 0.965f + (((NextNoise() + 1f) * 0.5f) * 0.07f);
        _velocity = (_lineMode == 4
            ? Math.Clamp(velocity, 0.35f, 1.28f)
            : Math.Clamp(velocity, 0.35f, 1.05f)) * humanVelocity;
    }

    private float ProcessNote()
    {
        if (_noteRemaining <= 0) return 0f;

        float progress = Math.Clamp(_noteAge / (float)Math.Max(1, _noteTotal), 0f, 1f);
        float remaining = 1f - progress;
        float attackMs = _lineMode == 4
            ? (_slapGhost ? 0.08f : _slapPop ? 0.12f : 0.20f)
            : 2.6f;
        float attack = Math.Clamp(_noteAge / (float)Math.Max(1, (int)(_sampleRate * attackMs / 1000f)), 0f, 1f);
        float envelope = _lineMode == 4
            ? attack * remaining * remaining * remaining * (0.68f + (0.32f * remaining))
            : attack * remaining * (0.50f + (0.50f * remaining));

        // Leve caída inicial de afinación por tensión de cuerda y una desviación
        // microscópica distinta por nota. El oído la percibe como ejecución real,
        // no como desafinación.
        float bendWindow = MathF.Max(0f, 1f - (_noteAge / (float)Math.Max(1, (int)(_sampleRate * 0.030f))));
        float bendRatio = 1f + bendWindow * (_lineMode == 4
            ? (_slapPop ? 0.0085f : _slapGhost ? 0.0030f : 0.0050f)
            : 0.0012f);
        double increment = (_frequency * _noteDetuneRatio * bendRatio) / _sampleRate;
        _phase1 += increment;
        _phase2 += increment * 2.003;
        _phase3 += increment * 3.011;
        _phase4 += increment * 4.026;
        if (_phase1 >= 1.0) _phase1 -= Math.Floor(_phase1);
        if (_phase2 >= 1.0) _phase2 -= Math.Floor(_phase2);
        if (_phase3 >= 1.0) _phase3 -= Math.Floor(_phase3);
        if (_phase4 >= 1.0) _phase4 -= Math.Floor(_phase4);

        float s1 = FastSine(_phase1);
        float s2 = FastSine(_phase2);
        float s3 = FastSine(_phase3);
        float s4 = FastSine(_phase4);

        // En una cuerda real los parciales altos mueren antes que la fundamental.
        // Esta evolución espectral es la corrección principal al carácter sintético.
        float h2Life = 0.24f + (0.76f * remaining);
        float h3Life = 0.10f + (0.90f * remaining * remaining);
        float h4Life = remaining * remaining * (0.35f + (0.65f * remaining));
        float pluck = MathF.Max(0f, 1f - progress * (_lineMode == 4 ? 18f : 8f));
        pluck *= pluck;

        float raw;
        if (_lineMode == 4)
        {
            if (_slapGhost)
            {
                // Nota muerta: casi no hay fundamental sostenida; manda la cuerda
                // contra el traste, con una sombra de afinación para conservar groove.
                raw = (s1 * 0.12f) + (s2 * 0.10f * h2Life) + (s3 * 0.07f * h3Life);
            }
            else if (_slapPop)
            {
                raw = (s1 * 0.43f)
                    + (s2 * (_harmonic2Gain + (0.12f * pluck)) * h2Life)
                    + (s3 * (_harmonic3Gain + (0.10f * pluck)) * h3Life)
                    + (s4 * (_harmonic4Gain + (0.075f * pluck)) * h4Life);
            }
            else
            {
                raw = (s1 * 0.69f)
                    + (s2 * (_harmonic2Gain + (0.055f * pluck)) * h2Life)
                    + (s3 * (_harmonic3Gain + (0.040f * pluck)) * h3Life)
                    + (s4 * _harmonic4Gain * h4Life);
            }
        }
        else
        {
            raw = (s1 * 0.84f)
                + (s2 * (_harmonic2Gain + (0.045f * pluck)) * h2Life)
                + (s3 * (_harmonic3Gain + (0.025f * pluck)) * h3Life)
                + (s4 * _harmonic4Gain * h4Life);
        }

        // Excitación mecánica coloreada. Separamos una componente de alta frecuencia
        // (dedo/traste) de otra más opaca (contacto con la cuerda), evitando ruido
        // blanco puro que también delataba el sintetizador.
        float transientSeconds = _lineMode == 4
            ? (_slapGhost ? 0.014f : _slapPop ? 0.021f : 0.016f)
            : 0.010f;
        int transientSamples = Math.Max(1, (int)(_sampleRate * transientSeconds));
        if (_noteAge < transientSamples)
        {
            float pluckEnvelope = 1f - (_noteAge / (float)transientSamples);
            pluckEnvelope *= pluckEnvelope;
            float noise = NextNoise();
            _noiseToneState += 0.20f * (noise - _noiseToneState);
            float highNoise = noise - _noiseToneState;
            float mechanical = (highNoise * 0.78f) + (_noiseToneState * 0.22f);
            float noiseAmount = _lineMode == 4
                ? (_slapGhost ? 0.92f : _slapPop ? 0.62f : 0.38f)
                : 0.050f;
            raw += mechanical * noiseAmount * pluckEnvelope;
            if (_lineMode == 4 && !_slapGhost)
            {
                float click = ((_noteAge & 1) == 0 ? 1f : -1f) * (_slapPop ? 0.46f : 0.27f);
                raw += click * pluckEnvelope;
            }
        }

        // Menos saturación fija que en 2.41.47: el carácter debe venir de la cuerda
        // y el ataque, no de un soft-clip constante.
        float drive = _lineMode == 4
            ? (_slapGhost ? 1.20f : _slapPop ? 1.72f : 1.48f)
            : 1.03f;
        raw = FastDspMath.SoftClip(raw * drive);

        // Filtro dinámico: brillante al ataque y progresivamente más oscuro.
        float spectralLife = 0.34f + (0.66f * remaining);
        float activeFilterAlpha = _lineMode == 4
            ? Math.Clamp(_filterAlpha * (2.25f + (1.25f * spectralLife)) * _noteBrightness, 0.05f, 0.58f)
            : Math.Clamp(_filterAlpha * (0.52f + (0.82f * spectralLife)) * _noteBrightness, 0.025f, 0.30f);
        _filterState += activeFilterAlpha * (raw - _filterState);

        // Dos escalas lentas de cuerpo simulan la transferencia de energía de cuerda
        // a instrumento/caja y evitan que quede una senoide desnuda en la cola.
        _bodyState += 0.016f * (_filterState - _bodyState);
        float bodyDrive = _filterState - _bodyState;
        _bodyResonanceState += 0.0075f * (bodyDrive - _bodyResonanceState);
        float bodyMix = (_filterState * 0.86f) + (_bodyState * 0.10f) + (_bodyResonanceState * 0.04f);

        _noteAge++;
        _noteRemaining--;
        if (_lineMode == 4)
        {
            float brightMix = _slapGhost ? 0.72f : _slapPop ? 0.76f : 0.61f;
            float slap = (bodyMix * (1f - brightMix)) + (raw * brightMix);
            float articulationGain = _slapGhost ? 0.86f : _slapPop ? 1.20f : 1.10f;
            return slap * envelope * _velocity * articulationGain;
        }
        return bodyMix * envelope * _velocity * 0.76f;
    }

    private static float FastSine(double phase)
    {
        // Aproximación parabólica con corrección: suficientemente suave para un bajo
        // filtrado y mucho más barata que tres llamadas a Sin por muestra.
        float x = (float)(phase * 2.0 - 1.0); // -1 a +1 equivale a -pi a +pi.
        float y = (4f * x) - (4f * x * MathF.Abs(x));
        return (0.225f * ((y * MathF.Abs(y)) - y)) + y;
    }

    private float NextNoise()
    {
        uint x = _noiseState;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _noiseState = x;
        return ((x & 0x00FFFFFFu) / 8388607.5f) - 1f;
    }
}
