namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Piano de acompañamiento sintetizado en tiempo real. Comparte el reloj musical
/// con metrónomo/batería/bajo, pero mantiene sonido, progresión, estilo, tonalidad
/// y volumen independientes. No asigna memoria ni crea objetos dentro del callback ASIO.
/// </summary>
internal sealed class PianoAccompanimentGenerator
{
    private struct Voice
    {
        public bool Active;
        public float Frequency;
        public float Velocity;
        public int Age;
        public int Total;
        public double Phase1;
        public double Phase2;
        public double Phase3;
        public double Phase4;
        public double Phase5;
        public double Phase6;
        public float Filter;
        public float Soundboard;
        public float Amp1, Amp2, Amp3, Amp4, Amp5, Amp6;
        public float Decay1, Decay2, Decay3, Decay4, Decay5, Decay6;
        public uint NoiseState;
    }

    // Progresiones de cuatro acordes expresadas como raíz relativa y calidad.
    // Calidad: false = mayor, true = menor.
    private static readonly int[,] Roots =
    {
        { 0, 7, 9, 5 },   // I - V - vi - IV
        { 9, 5, 0, 7 },   // vi - IV - I - V
        { 0, 5, 7, 5 },   // I - IV - V - IV
        { 0, 9, 5, 7 },   // I - vi - IV - V
        { 0, 8, 3, 10 }   // i - VI - III - VII
    };

    private static readonly int[] ArpOrder = { 0, 1, 2, 3, 2, 1, 0, 2 };

    private static readonly bool[,] MinorQuality =
    {
        { false, false, true,  false },
        { true,  false, false, false },
        { false, false, false, false },
        { false, true,  false, false },
        { true,  false, false, false }
    };

    // Cuando la tonalidad seleccionada es menor (índices 12 a 23), las mismas
    // progresiones se reinterpretan sobre la escala menor natural. De esta forma
    // elegir, por ejemplo, La menor cambia realmente las raíces y calidades de
    // los acordes; no es sólo una etiqueta distinta sobre una progresión mayor.
    private static readonly int[,] MinorModeRoots =
    {
        { 0, 7, 8, 5 },   // i - v - VI - iv
        { 8, 5, 0, 7 },   // VI - iv - i - v
        { 0, 5, 7, 5 },   // i - iv - v - iv
        { 0, 8, 5, 7 },   // i - VI - iv - v
        { 0, 8, 3, 10 }   // i - VI - III - VII
    };

    private static readonly bool[,] MinorModeQuality =
    {
        { true,  true,  false, true  },
        { false, true,  true,  true  },
        { true,  true,  true,  true  },
        { true,  false, true,  true  },
        { true,  false, false, false }
    };

    private readonly int _sampleRate;
    private readonly Voice[] _voices = new Voice[10];
    private bool _enabled;
    private float _bpm = 80f;
    private int _beatsPerBar = 4;
    private int _drumPattern = 1;
    private float _volume = 0.24f;
    private int _key = 7;
    private int _sound;
    private int _progression;
    private ulong _customPack1;
    private ulong _customPack2;
    private ulong _customPack3;
    private ulong _customPack4;
    private ulong _customPack5;
    private ulong _customPack6;
    private ulong _customPack7;
    private ulong _customPack8;
    private int _customCount;
    private int _customTotalBars;
    private ushort[] _customSequence = Array.Empty<ushort>();
    private ulong _customSequenceHash;
    private int _style = 3;
    private double _samplesPerStep;
    private double _samplesUntilNextStep;
    private int _stepsPerBar = 16;
    private int _stepIndex;
    private int _barIndex;
    private int _arpIndex;

    public PianoAccompanimentGenerator(int sampleRate)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        RecalculateTiming();
        Reset();
    }

    public void Configure(bool enabled, float bpm, int beatsPerBar, int drumPattern,
        float volumePercent, int key, int sound, int progression, int style,
        ulong customPack1 = 0, ulong customPack2 = 0, ulong customPack3 = 0, ulong customPack4 = 0,
        ulong customPack5 = 0, ulong customPack6 = 0, ulong customPack7 = 0, ulong customPack8 = 0, int customCount = 0,
        ushort[]? customSequence = null, ulong customSequenceHash = 0)
    {
        bpm = Math.Clamp(float.IsFinite(bpm) ? bpm : 80f, 40f, 240f);
        beatsPerBar = beatsPerBar is 2 or 3 or 4 or 6 ? beatsPerBar : 4;
        drumPattern = Math.Clamp(drumPattern, 0, 5);
        volumePercent = Math.Clamp(float.IsFinite(volumePercent) ? volumePercent : 24f, 0f, 100f);
        key = Math.Clamp(key, 0, 23);
        sound = Math.Clamp(sound, 0, 5);
        progression = Math.Clamp(progression, 0, 5);
        style = Math.Clamp(style, 0, 8);

        bool timingChanged = MathF.Abs(_bpm - bpm) > 0.0001f || _beatsPerBar != beatsPerBar || _drumPattern != drumPattern;
        customSequence ??= Array.Empty<ushort>();
        customCount = Math.Clamp(customCount, 0, customSequence.Length > 0 ? customSequence.Length : 32);
        bool customChanged = _customPack1 != customPack1 || _customPack2 != customPack2 ||
            _customPack3 != customPack3 || _customPack4 != customPack4 ||
            _customPack5 != customPack5 || _customPack6 != customPack6 ||
            _customPack7 != customPack7 || _customPack8 != customPack8 || _customCount != customCount ||
            _customSequenceHash != customSequenceHash;
        bool musicalChanged = _key != key || _sound != sound || _progression != progression || _style != style || customChanged;
        bool enabledChanged = _enabled != enabled;

        _enabled = enabled;
        _bpm = bpm;
        _beatsPerBar = beatsPerBar;
        _drumPattern = drumPattern;
        _volume = volumePercent / 100f;
        _key = key;
        _sound = sound;
        _progression = progression;
        _customPack1 = customPack1;
        _customPack2 = customPack2;
        _customPack3 = customPack3;
        _customPack4 = customPack4;
        _customPack5 = customPack5;
        _customPack6 = customPack6;
        _customPack7 = customPack7;
        _customPack8 = customPack8;
        _customCount = customCount;
        if (customChanged)
        {
            _customSequence = customSequence;
            _customSequenceHash = customSequenceHash;
        }
        _customTotalBars = CalculateCustomTotalBars();
        _style = style;

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
                _arpIndex = 0;
            }
            _samplesUntilNextStep += _samplesPerStep;
        }
        _samplesUntilNextStep -= 1.0;

        float sum = 0f;
        for (int i = 0; i < _voices.Length; i++)
            sum += ProcessVoice(ref _voices[i]);

        // Techo prudente: el piano comparte bus con batería y bajo.
        return Math.Clamp(FastDspMath.SoftClip(sum * 0.76f) * _volume, -0.78f, 0.78f);
    }

    public void Reset()
    {
        _samplesUntilNextStep = 0.0;
        _stepIndex = 0;
        _barIndex = 0;
        _arpIndex = 0;
        Array.Clear(_voices, 0, _voices.Length);
    }

    private void RecalculateTiming()
    {
        double quarter = _sampleRate * 60.0 / Math.Max(1.0, _bpm);
        if (_drumPattern == 4) // shuffle ternario
        {
            _stepsPerBar = 12;
            _samplesPerStep = quarter / 3.0;
        }
        else if (_drumPattern == 5 || _beatsPerBar == 6) // 6/8
        {
            _stepsPerBar = 12;
            _samplesPerStep = quarter / 2.0;
        }
        else
        {
            _stepsPerBar = Math.Max(8, _beatsPerBar * 4);
            _samplesPerStep = quarter / 4.0;
        }
    }

    private void TriggerStep(int step)
    {
        int rootOffset;
        int quality; // 0 mayor, 1 menor, 2 disminuido.
        int seventh = 0; // 0 sin séptima; 1 menor; 2 mayor; 3 disminuida.
        if (_progression == 5 && _customCount > 0)
        {
            GetCustomChordForBar(_barIndex, out rootOffset, out quality, out seventh);
        }
        else
        {
            int chordIndex = _barIndex & 3;
            bool tonicMinor = _key >= 12;
            int safeProgression = Math.Clamp(_progression, 0, 4);
            rootOffset = tonicMinor
                ? MinorModeRoots[safeProgression, chordIndex]
                : Roots[safeProgression, chordIndex];
            bool minor = tonicMinor
                ? MinorModeQuality[safeProgression, chordIndex]
                : MinorQuality[safeProgression, chordIndex];
            quality = minor ? 1 : 0;
        }

        // 2.41.56: los estilos de piano ahora pueden acompañar los mismos géneros
        // que la batería. Los índices 0 a 3 se conservan para no alterar escenas viejas.
        // El índice 4 sigue automáticamente el patrón de batería seleccionado.
        int effectiveStyle = _style == 4
            ? _drumPattern switch
            {
                0 => 5, // Rock 4/4
                1 => 3, // Worship 4/4
                2 => 6, // Pop 4/4
                3 => 2, // Balada 4/4
                4 => 7, // Blues Shuffle
                5 => 8, // Worship 6/8
                _ => 3
            }
            : _style;

        switch (effectiveStyle)
        {
            case 0: // Acordes sostenidos.
                if (step == 0)
                    StartChord(rootOffset, quality, seventh, _stepsPerBar * 0.92f, 0.80f, worshipVoicing: false);
                break;

            case 1: // Arpegio de corcheas, ascendente/descendente.
                int stride = (_drumPattern == 4) ? 1 : 2;
                if ((step % stride) == 0)
                {
                    int chordTone = ArpOrder[_arpIndex++ & 7];
                    // 2.41.49: en piano acústico el arpegio conserva una cola de cuerda
                    // equivalente a un pedal de sustain moderado.
                    bool resonantGrand = _sound is 0 or 3;
                    bool organ = _sound is 4 or 5;
                    float arpDuration = resonantGrand
                        ? Math.Max(4.8f, stride * 3.10f)
                        : organ ? Math.Max(2.4f, stride * 1.80f)
                        : Math.Max(1.2f, stride * 1.35f);
                    float arpVelocity = resonantGrand ? 0.76f : organ ? 0.69f : 0.72f;
                    StartArpeggioNote(rootOffset, quality, seventh, chordTone, arpDuration, arpVelocity);
                }
                break;

            case 2: // Balada 4/4: acorde largo con respuesta suave en mitad de compás.
                if (step == 0)
                    StartChord(rootOffset, quality, seventh, _stepsPerBar * 0.78f, 0.76f, worshipVoicing: false);
                else if (step == _stepsPerBar / 2)
                    StartArpeggioNote(rootOffset, quality, seventh, 3, _stepsPerBar * 0.34f, 0.48f);
                break;

            case 3: // Worship 4/4: voicing abierto + add9, respiración larga.
                if (step == 0)
                    StartChord(rootOffset, quality, seventh, _stepsPerBar * 0.96f, 0.68f, worshipVoicing: true);
                else if (step == Math.Max(1, _stepsPerBar - 2))
                    StartArpeggioNote(rootOffset, quality, seventh, 3, 1.7f, 0.34f);
                break;

            case 5: // Rock 4/4: acordes firmes en negras y pickup de quinta.
            {
                int quarter = Math.Max(1, _stepsPerBar / 4);
                if ((step % quarter) == 0)
                {
                    float accent = step == 0 ? 0.88f : step == quarter * 2 ? 0.82f : 0.74f;
                    StartChord(rootOffset, quality, seventh, Math.Max(1.8f, quarter * 0.58f), accent, worshipVoicing: false);
                }
                else if (step == Math.Max(1, _stepsPerBar - 2))
                    StartArpeggioNote(rootOffset, quality, seventh, 2, 1.35f, 0.48f);
                break;
            }

            case 6: // Pop 4/4: pulsos claros y síncopa antes del cuarto tiempo.
            {
                int quarter = Math.Max(1, _stepsPerBar / 4);
                if (step == 0)
                    StartChord(rootOffset, quality, seventh, quarter * 1.35f, 0.78f, worshipVoicing: false);
                else if (step == quarter + Math.Max(1, quarter / 2) || step == quarter * 2)
                    StartChord(rootOffset, quality, seventh, quarter * 0.72f, 0.64f, worshipVoicing: false);
                else if (step == Math.Max(1, _stepsPerBar - 2))
                    StartArpeggioNote(rootOffset, quality, seventh, 3, 1.45f, 0.42f);
                break;
            }

            case 7: // Blues Shuffle: comping ternario sobre los tresillos del patrón.
            {
                int pulse = _stepsPerBar == 12 ? 3 : Math.Max(1, _stepsPerBar / 4);
                if ((step % pulse) == 0)
                    StartChord(rootOffset, quality, seventh, Math.Max(1.4f, pulse * 0.58f), step == 0 ? 0.80f : 0.66f, worshipVoicing: false);
                else if (_stepsPerBar == 12 && (step % pulse) == pulse - 1)
                    StartArpeggioNote(rootOffset, quality, seventh, 2, 1.15f, 0.38f);
                break;
            }

            case 8: // Worship 6/8: acorde abierto y respuestas en los dos grandes pulsos.
            {
                int halfBar = Math.Max(1, _stepsPerBar / 2);
                if (step == 0)
                    StartChord(rootOffset, quality, seventh, _stepsPerBar * 0.90f, 0.67f, worshipVoicing: true);
                else if (step == Math.Max(1, halfBar - 2) || step == Math.Max(1, _stepsPerBar - 2))
                    StartArpeggioNote(rootOffset, quality, seventh, step < halfBar ? 2 : 3, 2.2f, 0.38f);
                break;
            }

            default:
                if (step == 0)
                    StartChord(rootOffset, quality, seventh, _stepsPerBar * 0.90f, 0.72f, worshipVoicing: false);
                break;
        }
    }

    private void StartChord(int rootOffset, int quality, int seventh, float durationSteps, float velocity, bool worshipVoicing)
    {
        int root = RootMidi(rootOffset);
        int third = quality == 0 ? 4 : 3;
        int fifth = quality == 2 ? 6 : 7;
        int seventhInterval = seventh switch { 1 => 10, 2 => 11, 3 => 9, _ => -1 };
        if (worshipVoicing)
        {
            StartVoice(root, durationSteps, velocity * 0.86f);
            StartVoice(root + fifth, durationSteps, velocity * 0.94f);
            StartVoice(root + 12, durationSteps, velocity);
            StartVoice(root + 14, durationSteps, velocity * 0.72f); // add9
            StartVoice(root + 12 + third, durationSteps, velocity * 0.58f);
            if (seventhInterval >= 0)
                StartVoice(root + seventhInterval, durationSteps, velocity * 0.64f);
        }
        else
        {
            StartVoice(root, durationSteps, velocity * 0.92f);
            StartVoice(root + third, durationSteps, velocity * 0.84f);
            StartVoice(root + fifth, durationSteps, velocity * 0.90f);
            if (seventhInterval >= 0)
                StartVoice(root + seventhInterval, durationSteps, velocity * 0.76f);
            StartVoice(root + 12, durationSteps, velocity * 0.72f);
        }
    }

    private void StartArpeggioNote(int rootOffset, int quality, int seventh, int chordTone, float durationSteps, float velocity)
    {
        int root = RootMidi(rootOffset);
        int third = quality == 0 ? 4 : 3;
        int fifth = quality == 2 ? 6 : 7;
        int seventhInterval = seventh switch { 1 => 10, 2 => 11, 3 => 9, _ => -1 };
        int semitones = chordTone switch
        {
            0 => 0,
            1 => third,
            2 => fifth,
            3 when seventhInterval >= 0 => seventhInterval,
            _ => 12
        };
        StartVoice(root + semitones, durationSteps, velocity);
    }

    private int CalculateCustomTotalBars()
    {
        int total = 0;
        for (int i = 0; i < _customCount; i++)
        {
            ushort code = GetCustomCode(i);
            total += ((code >> 3) & 0x1F) + 1;
        }
        return Math.Max(1, total);
    }

    private ushort GetCustomCode(int index)
    {
        if ((uint)index >= (uint)_customCount) return 0;
        if (_customSequence.Length > index) return _customSequence[index];
        if ((uint)index >= 32u) return 0;
        int group = index >> 2;
        int shift = (index & 3) * 16;
        ulong pack = group switch
        {
            0 => _customPack1,
            1 => _customPack2,
            2 => _customPack3,
            3 => _customPack4,
            4 => _customPack5,
            5 => _customPack6,
            6 => _customPack7,
            _ => _customPack8
        };
        return (ushort)((pack >> shift) & 0xFFFFUL);
    }

    private void GetCustomChordForBar(int barIndex, out int rootOffset, out int quality, out int seventh)
    {
        // El patrón personalizado es circular. Los bloques repetidos ya llegan expandidos desde la interfaz; cada posición puede durar de 1 a 32 compases.
        int target = Math.Abs(barIndex) % Math.Max(1, _customTotalBars);
        int accumulated = 0;
        ushort selected = GetCustomCode(0);
        for (int i = 0; i < _customCount; i++)
        {
            ushort code = GetCustomCode(i);
            int bars = ((code >> 3) & 0x1F) + 1;
            selected = code;
            if (target < accumulated + bars) break;
            accumulated += bars;
        }

        int degree = selected & 0x7;
        quality = (selected >> 8) & 0x3;
        if (quality > 2) quality = 0;
        seventh = (selected >> 12) & 0x3;
        int accidentalCode = (selected >> 10) & 0x3;
        int accidental = accidentalCode == 1 ? -1 : accidentalCode == 2 ? 1 : 0;
        bool tonicMinor = _key >= 12;
        rootOffset = (tonicMinor ? MinorScaleOffset(degree) : MajorScaleOffset(degree)) + accidental;
    }

    private static int MajorScaleOffset(int degree) => degree switch
    {
        0 => 0, 1 => 2, 2 => 4, 3 => 5, 4 => 7, 5 => 9, _ => 11
    };

    private static int MinorScaleOffset(int degree) => degree switch
    {
        0 => 0, 1 => 2, 2 => 3, 3 => 5, 4 => 7, 5 => 8, _ => 10
    };

    private int RootMidi(int rootOffset)
    {
        int midi = 48 + (_key % 12) + rootOffset; // C3 como centro; índices 12-23 indican modo menor.
        while (midi > 59) midi -= 12;
        while (midi < 43) midi += 12;
        return midi;
    }

    private void StartVoice(int midi, float durationSteps, float velocity)
    {
        int slot = -1;
        int oldestAge = -1;
        for (int i = 0; i < _voices.Length; i++)
        {
            if (!_voices[i].Active) { slot = i; break; }
            if (_voices[i].Age > oldestAge) { oldestAge = _voices[i].Age; slot = i; }
        }
        if (slot < 0) return;

        ref Voice voice = ref _voices[slot];
        voice.Active = true;
        voice.Frequency = 440f * MathF.Pow(2f, (Math.Clamp(midi, 36, 84) - 69) / 12f);
        voice.Velocity = Math.Clamp(velocity, 0.15f, 1f);
        voice.Age = 0;
        voice.Total = Math.Max(1, (int)(_samplesPerStep * Math.Max(0.5f, durationSteps)));
        voice.Phase1 = voice.Phase2 = voice.Phase3 = voice.Phase4 = voice.Phase5 = voice.Phase6 = 0.0;
        voice.Filter = 0f;
        voice.Soundboard = 0f;
        voice.NoiseState = 0x9E3779B9u ^ (uint)(midi * 977 + slot * 131 + 17);
        float register = Math.Clamp((voice.Frequency - 65f) / 900f, 0f, 1f);
        if (_sound is 4 or 5) // Hammond Worship: drawbars sostenidos, sin caída de martillo de piano.
        {
            voice.Amp1 = 0.50f; // 16'
            voice.Amp2 = 1.00f; // 8'
            voice.Amp3 = 0.44f; // 5 1/3'
            voice.Amp4 = 0.70f; // 4'
            voice.Amp5 = 0.34f; // 2 2/3'
            voice.Amp6 = 0.26f; // 2'
            voice.Decay1 = voice.Decay2 = voice.Decay3 = voice.Decay4 = voice.Decay5 = voice.Decay6 = 1f;
        }
        else if (_sound == 3) // Concert Grand 2.41.52: un poco más de proyección y caja acústica.
        {
            voice.Amp1 = 1.0f;
            voice.Amp4 = 0.83f;
            voice.Amp2 = 0.86f;
            voice.Amp3 = 0.64f;
            voice.Amp5 = 0.41f;
            voice.Amp6 = 0.28f;
            voice.Decay1 = PerSampleDecay(4.65f - 1.55f * register);
            voice.Decay4 = PerSampleDecay(4.05f - 1.18f * register);
            voice.Decay2 = PerSampleDecay(2.18f - 0.45f * register);
            voice.Decay3 = PerSampleDecay(1.22f - 0.22f * register);
            voice.Decay5 = PerSampleDecay(0.76f - 0.10f * register);
            voice.Decay6 = PerSampleDecay(0.52f - 0.06f * register);
        }
        else
        {
            voice.Amp1 = 1.0f;
            voice.Amp4 = 0.72f;
            voice.Amp2 = 0.66f;
            voice.Amp3 = 0.44f;
            voice.Amp5 = 0.26f;
            voice.Amp6 = 0.15f;
            // Los armónicos de un piano acústico no caen todos juntos. Los altos mueren
            // rápido; la fundamental y la segunda cuerda quedan vibrando mucho más.
            voice.Decay1 = PerSampleDecay(4.2f - 1.7f * register);
            voice.Decay4 = PerSampleDecay(3.6f - 1.35f * register);
            voice.Decay2 = PerSampleDecay(1.85f - 0.55f * register);
            voice.Decay3 = PerSampleDecay(1.00f - 0.30f * register);
            voice.Decay5 = PerSampleDecay(0.58f - 0.15f * register);
            voice.Decay6 = PerSampleDecay(0.38f - 0.08f * register);
        }
    }

    private float ProcessVoice(ref Voice voice)
    {
        if (!voice.Active) return 0f;
        if (voice.Age >= voice.Total)
        {
            voice.Active = false;
            return 0f;
        }

        float progress = Math.Clamp(voice.Age / (float)Math.Max(1, voice.Total), 0f, 1f);
        float remaining = 1f - progress;
        int attackSamples = _sound switch
        {
            2 => Math.Max(1, (int)(_sampleRate * 0.0045f)),
            1 => Math.Max(1, (int)(_sampleRate * 0.011f)),
            3 => Math.Max(1, (int)(_sampleRate * 0.00070f)),
            4 or 5 => Math.Max(1, (int)(_sampleRate * 0.0032f)),
            _ => Math.Max(1, (int)(_sampleRate * 0.00085f))
        };
        float attack = Math.Clamp(voice.Age / (float)attackSamples, 0f, 1f);

        // Cada timbre usa una envolvente deliberadamente distinta. En 2.41.37
        // acústico y Rhodes compartían demasiada forma temporal y por eso al oído
        // parecían casi el mismo instrumento.
        float envelope = _sound switch
        {
            // Piano acústico 2.41.47: respuesta de grand clásico. Ataque inmediato
            // de martillo y caída más pianística, evitando el sustain casi lineal
            // que podía recordar a un piano eléctrico.
            0 => attack * (remaining < 0.12f ? remaining / 0.12f : 1f),
            // Rhodes: entrada más blanda y sustain notablemente más largo.
            1 => attack * remaining * (0.82f + 0.18f * remaining),
            // Worship alabanza: martillo suave de piano y una caída larga, no un pad.
            2 => attack * MathF.Sqrt(MathF.Max(0f, remaining)) * (0.78f + 0.22f * remaining),
            // Concert Grand: acústico brillante, ataque definido y cola natural.
            3 => attack * (remaining < 0.10f ? remaining / 0.10f : 1f),
            // Hammond: nivel sostenido mientras la tecla está activa y liberación corta.
            4 or 5 => attack * (remaining < 0.08f ? remaining / 0.08f : 1f),
            _ => attack
        };

        double inc = voice.Frequency / _sampleRate;
        if (_sound is 4 or 5)
        {
            // Drawbars de órgano: 16', 8', 5 1/3', 4', 2 2/3' y 2'.
            // Las relaciones no enteras son parte del carácter Hammond.
            voice.Phase1 += inc * 0.5;
            voice.Phase2 += inc;
            voice.Phase3 += inc * 1.5;
            voice.Phase4 += inc * 2.0;
            voice.Phase5 += inc * 3.0;
            voice.Phase6 += inc * 4.0;
        }
        else
        {
            voice.Phase1 += inc;
            // En el acústico las parciales se estiran apenas, como una cuerda real de piano.
            voice.Phase2 += inc * (_sound == 0 ? 2.0035 : (_sound == 3 ? 2.0060 : 2.0));
            voice.Phase3 += inc * (_sound == 0 ? 3.0120 : (_sound == 3 ? 3.0180 : (_sound == 1 ? 4.0 : 3.0)));
            // Segunda cuerda casi al unísono; Concert Grand abre apenas más la pareja.
            voice.Phase4 += inc * (_sound == 2 ? 1.0022 : (_sound == 3 ? 1.00068 : (_sound == 0 ? 1.00042 : 1.0)));
            if (_sound is 0 or 3)
            {
                voice.Phase5 += inc * (_sound == 3 ? 4.0360 : 4.0270);
                voice.Phase6 += inc * (_sound == 3 ? 5.0680 : 5.0520);
            }
        }
        if (voice.Phase1 >= 1.0) voice.Phase1 -= Math.Floor(voice.Phase1);
        if (voice.Phase2 >= 1.0) voice.Phase2 -= Math.Floor(voice.Phase2);
        if (voice.Phase3 >= 1.0) voice.Phase3 -= Math.Floor(voice.Phase3);
        if (voice.Phase4 >= 1.0) voice.Phase4 -= Math.Floor(voice.Phase4);
        if (voice.Phase5 >= 1.0) voice.Phase5 -= Math.Floor(voice.Phase5);
        if (voice.Phase6 >= 1.0) voice.Phase6 -= Math.Floor(voice.Phase6);

        float fundamental = FastSine(voice.Phase1);
        float second = FastSine(voice.Phase2);
        float upper = FastSine(voice.Phase3);
        float detuned = FastSine(voice.Phase4);
        float raw;
        float cutoffAlpha;

        if (_sound is 4 or 5) // Órgano Hammond Worship con Leslie lento/rápido.
        {
            float draw16 = fundamental;
            float draw8 = second;
            float draw513 = upper;
            float draw4 = detuned;
            float draw223 = FastSine(voice.Phase5);
            float draw2 = FastSine(voice.Phase6);

            // Preset de drawbars cálido para worship: 808635 aproximadamente, con
            // 3ra armónica/percussion suave sólo al comienzo y key click discreto.
            float percussion = MathF.Max(0f, 1f - progress * 5.8f);
            percussion *= percussion;
            float rawOrgan = draw16 * voice.Amp1 * 0.42f
                + draw8 * voice.Amp2 * 0.78f
                + draw513 * voice.Amp3 * 0.30f
                + draw4 * voice.Amp4 * 0.48f
                + draw223 * voice.Amp5 * (0.24f + 0.17f * percussion)
                + draw2 * voice.Amp6 * 0.18f;

            int clickSamples = Math.Max(1, (int)(_sampleRate * 0.0022f));
            if (voice.Age < clickSamples)
                rawOrgan += NextVoiceNoise(ref voice) * (1f - voice.Age / (float)clickSamples) * 0.045f;

            // Leslie: lento (chorale) o rápido (tremolo). En mono se simula con
            // modulación suave de amplitud/tono; evita un chorus evidente.
            float leslieHz = _sound == 5 ? 6.1f : 0.72f;
            float rotor = FastSine((voice.Age * leslieHz / _sampleRate) % 1.0);
            float tremoloDepth = _sound == 5 ? 0.20f : 0.075f;
            float rotorGain = 1f - tremoloDepth + tremoloDepth * (0.5f + 0.5f * rotor);
            raw = FastDspMath.SoftClip(rawOrgan * 1.30f) * rotorGain;
            cutoffAlpha = (_sound == 5 ? 0.30f : 0.25f) + 0.055f * rotor;
        }
        else if (_sound == 1) // Rhodes: cuerpo redondo, tine eléctrico y tremolo muy suave.
        {
            // La campana/tine es fuerte sólo al principio y cae rápido; el cuerpo
            // permanece oscuro y sostenido. Una modulación muy pequeña de amplitud
            // evita que el Rhodes sea simplemente otro piano de senoidales.
            float tine = MathF.Max(0f, 1f - progress * 8.5f);
            tine *= tine;
            float tremolo = 0.94f + 0.06f * FastSine((voice.Age * 4.6 / _sampleRate) % 1.0);
            float rounded = FastDspMath.SoftClip(fundamental * 1.22f);
            raw = (rounded * 0.88f + second * 0.055f + upper * 0.24f * tine) * tremolo;
            cutoffAlpha = 0.082f;
        }
        else if (_sound == 2) // Worship alabanza: grand cálido, abierto y sostenido.
        {
            // El ataque contiene un poco más de armónicos de martillo y luego
            // queda un cuerpo ancho formado por dos cuerdas casi al unísono.
            float hammer = MathF.Max(0f, 1f - progress * 18.0f);
            hammer *= hammer;
            float body = fundamental * 0.50f + detuned * 0.30f;
            raw = body
                + second * (0.115f + 0.065f * hammer)
                + upper * (0.045f + 0.10f * hammer);
            cutoffAlpha = 0.145f;
        }
        else if (_sound == 3) // Concert Grand: cola acústica con mayor proyección y brillo.
        {
            float register = Math.Clamp((voice.Frequency - 85f) / 800f, 0f, 1f);
            float lowRegister = 1f - register;
            int hammerSamples = Math.Max(1, (int)(_sampleRate * (0.0052f + 0.0018f * lowRegister)));
            float hammer = voice.Age < hammerSamples
                ? 1f - (voice.Age / (float)hammerSamples)
                : 0f;
            hammer *= hammer;

            float fourth = FastSine(voice.Phase5);
            float fifth = FastSine(voice.Phase6);
            raw = fundamental * voice.Amp1 * (0.64f + 0.07f * lowRegister)
                + detuned * voice.Amp4 * 0.30f
                + second * voice.Amp2 * (0.31f + 0.11f * hammer)
                + upper * voice.Amp3 * (0.22f + 0.16f * hammer)
                + fourth * voice.Amp5 * (0.16f + 0.11f * hammer)
                + fifth * voice.Amp6 * (0.11f + 0.085f * hammer);

            if (hammer > 0f)
            {
                float felt = NextVoiceNoise(ref voice);
                raw += felt * hammer * (0.125f + 0.085f * register) * (0.64f + 0.36f * voice.Velocity);
            }

            // Un poco más de tabla armónica y apertura para que el Grand se reconozca
            // inmediatamente frente al acústico clásico, sin llegar a un brillo metálico.
            voice.Soundboard += (raw - voice.Soundboard) * (0.015f + 0.007f * lowRegister);
            raw = raw * 0.855f + voice.Soundboard * 0.145f;
            raw = FastDspMath.SoftClip(raw * (1.14f + 0.14f * voice.Velocity));
            cutoffAlpha = 0.59f + register * 0.22f;

            voice.Amp1 *= voice.Decay1;
            voice.Amp4 *= voice.Decay4;
            voice.Amp2 *= voice.Decay2;
            voice.Amp3 *= voice.Decay3;
            voice.Amp5 *= voice.Decay5;
            voice.Amp6 *= voice.Decay6;
        }
        else // Acústico clásico: martillo, cuerdas estiradas y caída natural de parciales.
        {
            float register = Math.Clamp((voice.Frequency - 85f) / 800f, 0f, 1f);
            float lowRegister = 1f - register;

            // El martillo es un evento mecánico muy corto. Después queda la cuerda, no
            // un timbre brillante sostenido como en muchos pianos eléctricos.
            int hammerSamples = Math.Max(1, (int)(_sampleRate * (0.0065f + 0.0025f * lowRegister)));
            float hammer = voice.Age < hammerSamples
                ? 1f - (voice.Age / (float)hammerSamples)
                : 0f;
            hammer *= hammer;

            float fourth = FastSine(voice.Phase5);
            float fifth = FastSine(voice.Phase6);
            raw = fundamental * voice.Amp1 * (0.68f + 0.08f * lowRegister)
                + detuned * voice.Amp4 * 0.24f
                + second * voice.Amp2 * (0.25f + 0.06f * hammer)
                + upper * voice.Amp3 * (0.16f + 0.10f * hammer)
                + fourth * voice.Amp5 * (0.11f + 0.08f * hammer)
                + fifth * voice.Amp6 * (0.075f + 0.05f * hammer);

            if (hammer > 0f)
            {
                float felt = NextVoiceNoise(ref voice);
                raw += felt * hammer * (0.085f + 0.055f * register) * (0.65f + 0.35f * voice.Velocity);
            }

            // Resonancia de tabla armónica: sólo una sombra cálida del conjunto de
            // cuerdas, sin convertir el sonido en pad.
            voice.Soundboard += (raw - voice.Soundboard) * (0.010f + 0.006f * lowRegister);
            raw = raw * 0.90f + voice.Soundboard * 0.10f;
            raw = FastDspMath.SoftClip(raw * (1.12f + 0.12f * voice.Velocity));
            cutoffAlpha = 0.42f + register * 0.18f;

            voice.Amp1 *= voice.Decay1;
            voice.Amp4 *= voice.Decay4;
            voice.Amp2 *= voice.Decay2;
            voice.Amp3 *= voice.Decay3;
            voice.Amp5 *= voice.Decay5;
            voice.Amp6 *= voice.Decay6;
        }

        voice.Filter += cutoffAlpha * (raw - voice.Filter);
        voice.Age++;

        float outputGain = _sound switch
        {
            0 => 0.58f,
            1 => 0.50f,
            2 => 0.56f,
            3 => 0.60f,
            4 or 5 => 0.54f,
            _ => 0.58f
        };
        return voice.Filter * envelope * voice.Velocity * outputGain;
    }

    private float PerSampleDecay(float seconds)
    {
        seconds = Math.Clamp(seconds, 0.08f, 8f);
        // Para estos tiempos de caída, 1 - 1/(tau*Fs) aproxima muy bien e^-1/(tau*Fs)
        // y evita varias exponenciales durante el disparo de un acorde dentro del callback.
        return Math.Clamp(1f - (1f / (seconds * _sampleRate)), 0.90f, 0.9999999f);
    }

    private static float NextVoiceNoise(ref Voice voice)
    {
        uint x = voice.NoiseState;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        voice.NoiseState = x;
        return ((x & 0x00FFFFFFu) / 8388607.5f) - 1f;
    }

    private static float FastSine(double phase)
    {
        float x = (float)(phase * 2.0 - 1.0);
        float y = (4f * x) - (4f * x * MathF.Abs(x));
        return (0.225f * ((y * MathF.Abs(y)) - y)) + y;
    }
}
