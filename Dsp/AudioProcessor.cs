using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

internal sealed class AudioProcessor : IDisposable
{
    private readonly NoiseGate _noiseGate;
    private readonly CleanCompressor _compressor;
    private readonly AutoWahEffect _autoWah;
    private readonly Ds1DistortionPedal _ds1Distortion;
    private readonly DivineOverdrivePedal _divineOverdrive;
    private readonly Od1OverdrivePedal _od1Overdrive;
    private readonly FuzzPedal _fuzz;
    private readonly FiveBandEq _eq5;
    private readonly CleanBoostPedal _booster;
    private readonly OctaverEffect _octaver;
    private readonly ChorusEffect _inputChorus;
    private readonly AnalogChorusEffect _inputAnalogChorus;
    private readonly AmpModel[] _ampModels;
    private int _activeAmpIndex;
    private readonly EffectsLoop _effectsLoop;
    private readonly ReverbEffect _reverb;
    private readonly TunerAnalyzer _tunerAnalyzer;
    private readonly TunerToneGenerator _tunerTone;
    private readonly MetronomeGenerator _metronome;
    private readonly DrumMachineGenerator _drums;
    private readonly BassAccompanimentGenerator _backingBass;
    private readonly PianoAccompanimentGenerator _piano;
    private readonly VoiceProcessor _voiceProcessor;
    private readonly NamProcessor _namProcessor;
    private readonly float[] _namInput = new float[4096];
    private readonly float[] _namOutput = new float[4096];
    private readonly float[] _namDry = new float[4096];
    private readonly float[] _namVoice = new float[4096];
    private readonly float[] _namVoiceDuck = new float[4096];
    private long _namCalibrationSamplesRemaining;
    private double _namCalibrationSumSquares;
    private long _namCalibrationMeasuredSamples;
    private int _namCalibrationCompleted;
    private float _namCalibrationRmsDb = -120f;

    private DspParameters _pendingParameters = new();
    private DspParameters _appliedParameters = new();
    private bool _hasAppliedParameters;
    private int _appliedRevision = -1;
    private float _masterGain = 0.125f;
    private FirConvolver? _externalConvolver;
    private FirConvolver? _externalConvolverB;
    private readonly Biquad _cabHighPass;
    private readonly Biquad _cabLowPass;
    private int _delayResetRequested;
    private int _metronomeResetRequested;

    // 2.41.41: seguimiento musical del acompañamiento por guitarra. Se usa
    // histéresis + hold para no arrancar por ruido ni detenerse entre dos notas.
    private bool _accompanimentFollowLast;
    private bool _accompanimentGateActive;
    private int _accompanimentAttackCounter;
    private int _accompanimentSilenceCounter;
    private readonly int _accompanimentAttackRequiredSamples;
    private readonly int _accompanimentSilenceHoldSamples;
    private const float AccompanimentStartThreshold = 0.0045f; // aprox. -47 dBFS
    private const float AccompanimentStopThreshold = 0.0017f;  // aprox. -55 dBFS
    private bool _tunerEnabled;
    private bool _tunerMuteOutput = true;
    private bool _hardTunerBypass;
    private bool _simulationEnabled = true;
    private float _simulationMix = 1f;
    private float _simulationTarget = 1f;
    private readonly float _simulationRampStep;
    private readonly float _voiceDuckAttackStep;
    private readonly float _voiceDuckReleaseStep;
    private readonly int _voiceDuckHoldSamples;
    private int _voiceDuckHoldCounter;
    private float _voiceDuckGain = 1f;
    private const float VoiceDuckTarget = 0.5623413f; // -5 dB para Meet
    private const float MonitorDuckAmount = 0.45f; // aprox. -1,9 dB en el retorno local

    public AudioProcessor(int sampleRate)
    {
        SampleRate = sampleRate;
        _simulationRampStep = 1f / MathF.Max(1f, sampleRate * 0.012f);
        // 2.38.6: el ducking ya no sigue cada sílaba. Entra más suave, conserva
        // 220 ms de hold y vuelve lentamente para evitar la sensación de trémolo.
        _voiceDuckAttackStep = 1f - MathF.Exp(-1f / MathF.Max(1f, sampleRate * 0.055f));
        _voiceDuckReleaseStep = 1f - MathF.Exp(-1f / MathF.Max(1f, sampleRate * 0.650f));
        _voiceDuckHoldSamples = Math.Max(1, (int)(sampleRate * 0.220f));
        _accompanimentAttackRequiredSamples = Math.Max(16, (int)(sampleRate * 0.0012f));
        _accompanimentSilenceHoldSamples = Math.Max(1, (int)(sampleRate * 1.80f));
        _noiseGate = new NoiseGate(sampleRate);
        _compressor = new CleanCompressor(sampleRate);
        _autoWah = new AutoWahEffect(sampleRate);
        _ds1Distortion = new Ds1DistortionPedal(sampleRate);
        _divineOverdrive = new DivineOverdrivePedal(sampleRate);
        _od1Overdrive = new Od1OverdrivePedal(sampleRate);
        _fuzz = new FuzzPedal(sampleRate);
        _eq5 = new FiveBandEq(sampleRate);
        _booster = new CleanBoostPedal(sampleRate);
        _octaver = new OctaverEffect(sampleRate);
        _inputChorus = new ChorusEffect(sampleRate);
        _inputAnalogChorus = new AnalogChorusEffect(sampleRate);
        _ampModels = new[]
        {
            new AmpModel(sampleRate, AmpChannel.CleanTwin, 3f, 5f, 4f, 6f, 5f),
            new AmpModel(sampleRate, AmpChannel.CrunchBritish, 3f, 5f, 6f, 5.2f, 5f),
            new AmpModel(sampleRate, AmpChannel.LeadJcm800, 3f, 4.8f, 6.2f, 5f, 5.3f),
            new AmpModel(sampleRate, AmpChannel.CleanBoutique, 3f, 5.5f, 4.5f, 5.5f, 4.5f),
            new AmpModel(sampleRate, AmpChannel.CleanClassA, 3f, 4.5f, 5f, 6.2f, 5.5f),
            new AmpModel(sampleRate, AmpChannel.CrunchPlexi, 4f, 5f, 6.2f, 5.4f, 5.2f),
            new AmpModel(sampleRate, AmpChannel.CrunchClassA, 4f, 4.8f, 6f, 6f, 5.8f),
            new AmpModel(sampleRate, AmpChannel.LeadModern, 5f, 4.5f, 5.5f, 5.2f, 5.5f),
            new AmpModel(sampleRate, AmpChannel.LeadLegacy, 5f, 5f, 6.5f, 5f, 5.2f)
        };
        _activeAmpIndex = 0;
        _effectsLoop = new EffectsLoop(sampleRate);
        _reverb = new ReverbEffect(sampleRate);
        _tunerAnalyzer = new TunerAnalyzer(sampleRate);
        _tunerTone = new TunerToneGenerator(sampleRate);
        _metronome = new MetronomeGenerator(sampleRate);
        _drums = new DrumMachineGenerator(sampleRate);
        _backingBass = new BassAccompanimentGenerator(sampleRate);
        _piano = new PianoAccompanimentGenerator(sampleRate);
        _voiceProcessor = new VoiceProcessor(sampleRate);
        _namProcessor = new NamProcessor();
        _cabHighPass = new Biquad();
        _cabLowPass = new Biquad();
        _cabHighPass.SetHighPass(sampleRate, 20f);
        _cabLowPass.SetLowPass(sampleRate, Math.Min(20000f, sampleRate * 0.45f));
    }

    public int SampleRate { get; }
    public bool HasExternalImpulse => Volatile.Read(ref _externalConvolver) is not null;
    public bool HasExternalImpulseB => Volatile.Read(ref _externalConvolverB) is not null;
    public int DelayAutomaticResetCount => _effectsLoop.DelayAutomaticResetCount;
    public AmpChannel CurrentChannel => Volatile.Read(ref _appliedParameters).Channel;
    public bool VoiceOnlyMode => Volatile.Read(ref _pendingParameters).VoiceOnlyMode;
    public float VoiceProcessedPeak => _voiceProcessor.OutputPeak;
    public bool HasNamModel => _namProcessor.HasModel;

    // 2.41.4: el segundo procesador puede usarse como ruta de instrumento dedicada.
    // En ese caso no heredamos ningún fundido seco/simulado del uso histórico del
    // procesador principal: la salida queda 100 % en la cadena DSP.
    public void ForceSimulationFullyOn()
    {
        _simulationEnabled = true;
        _simulationTarget = 1f;
        _simulationMix = 1f;
    }
    public string? NamModelPath => _namProcessor.ModelPath;
    public string? NamLastError => _namProcessor.LastError;
    public float NamRecommendedInputDb => _namProcessor.RecommendedInputDb;
    public float NamRecommendedOutputDb => _namProcessor.RecommendedOutputDb;
    public float NamAppliedRecommendedOutputDb => _namProcessor.AppliedRecommendedOutputDb;
    public float NamSafetyOutputPadDb => NamProcessor.SafetyOutputPadDb;

    public void StartNamLevelCalibration(double seconds = 4.0)
    {
        Interlocked.Exchange(ref _namCalibrationCompleted, 0);
        Interlocked.Exchange(ref _namCalibrationMeasuredSamples, 0);
        _namCalibrationSumSquares = 0.0;
        Interlocked.Exchange(ref _namCalibrationSamplesRemaining, Math.Max(1, (long)(SampleRate * Math.Clamp(seconds, 1.0, 10.0))));
    }

    public bool TryConsumeNamLevelCalibration(out float rmsDb)
    {
        if (Interlocked.Exchange(ref _namCalibrationCompleted, 0) == 1)
        {
            rmsDb = _namCalibrationRmsDb;
            return true;
        }
        rmsDb = -120f;
        return false;
    }

    public bool IsNamNativeEngineAvailable(out string description) => NamProcessor.IsNativeEngineAvailable(out description);

    public NamModelInfo LoadNamModel(string path) => _namProcessor.Load(path, SampleRate);

    public void ClearNamModel() => _namProcessor.Clear();

    public void SetParameters(DspParameters parameters)
    {
        Volatile.Write(ref _pendingParameters, parameters);
    }

    public void RequestDelayReset()
    {
        Interlocked.Exchange(ref _delayResetRequested, 1);
    }

    public void RequestMetronomeReset()
    {
        Interlocked.Exchange(ref _metronomeResetRequested, 1);
    }


    public TunerReading GetTunerReading(float referenceAHz)
    {
        return _tunerAnalyzer.GetLatestReading(referenceAHz);
    }

    public void SetTunerGuideDirection(TuningDirection direction)
    {
        _tunerTone.SetDirection(direction);
    }

    public void PlayTunerReferenceTone(float frequencyHz)
    {
        _tunerTone.PlayReferenceTone(frequencyHz);
    }

    public int LoadImpulseResponse(string path)
    {
        float[] impulse = ImpulseResponseLoader.LoadMono(path, SampleRate);

        // La convolución directa se mantiene corta para que el hilo DSP pueda cumplir aun
        // con buffers ASIO pequeños. Los primeros 256 samples contienen la parte decisiva
        // de un IR de gabinete y evitan cargas excesivas en tiempo real.
        if (impulse.Length > 256)
        {
            Array.Resize(ref impulse, 256);
        }

        var convolver = new FirConvolver(impulse);
        Volatile.Write(ref _externalConvolver, convolver);
        return impulse.Length;
    }

    public void ClearImpulseResponse()
    {
        Volatile.Write(ref _externalConvolver, null);
    }

    public int LoadImpulseResponseB(string path)
    {
        float[] impulse = ImpulseResponseLoader.LoadMono(path, SampleRate);
        if (impulse.Length > 256) Array.Resize(ref impulse, 256);
        var convolver = new FirConvolver(impulse);
        Volatile.Write(ref _externalConvolverB, convolver);
        return impulse.Length;
    }

    public void ClearImpulseResponseB()
    {
        Volatile.Write(ref _externalConvolverB, null);
    }

    public void ProcessMonoToStereo(float[] input, float[] output, int frames)
    {
        ProcessMonoToStereo(input, null, output, null, null, frames);
    }

    public void ProcessMonoToStereo(float[] guitarInput, float[]? voiceInput, float[] output, int frames)
    {
        ProcessMonoToStereo(guitarInput, voiceInput, output, null, null, frames);
    }

    public void ProcessMonoToStereo(float[] guitarInput, float[]? voiceInput, float[] output, float[]? meetOutput, int frames)
    {
        ProcessMonoToStereo(guitarInput, voiceInput, output, meetOutput, null, frames);
    }

    /// <summary>
    /// Procesa guitarra y voz en dos mezclas: monitoreo local y, opcionalmente,
    /// una mezcla independiente destinada a un dispositivo virtual para Meet.
    /// loopCaptureOutput recibe exclusivamente la guitarra procesada, antes de sumar
    /// voz, metrónomo, batería, bajo y piano, para que el looper no duplique acompañamientos.
    /// </summary>
    public void ProcessMonoToStereo(float[] guitarInput, float[]? voiceInput, float[] output, float[]? meetOutput, float[]? loopCaptureOutput, int frames)
    {
        DspParameters parameters = Volatile.Read(ref _pendingParameters);
        if (parameters.Revision != _appliedRevision)
        {
            ApplyParameters(parameters);
        }

        if (Interlocked.Exchange(ref _delayResetRequested, 0) != 0)
        {
            _effectsLoop.ResetDelay();
        }
        if (Interlocked.Exchange(ref _metronomeResetRequested, 0) != 0)
        {
            _metronome.Reset();
            _drums.Reset();
            _backingBass.Reset();
            _piano.Reset();
        }

        bool hasVoice = voiceInput is not null && parameters.VoiceEnabled;
        if (parameters.VoiceOnlyMode)
        {
            ProcessVoiceOnly(voiceInput, output, meetOutput, loopCaptureOutput, frames, parameters, hasVoice);
            return;
        }

        // 2.40.19: ganancias de mezcla calculadas una sola vez por bloque.
        // Antes se repetían Math.Clamp cientos de veces por callback (una vez por muestra).
        // El resultado numérico y, por lo tanto, el sonido permanecen idénticos.
        float voiceMonitorGain = Math.Clamp(parameters.VoiceMonitorPercent / 100f, 0f, 1.5f);
        float meetGuitarGain = Math.Clamp(parameters.MeetGuitarPercent / 100f, 0f, 1.5f);
        float meetVoiceGain = Math.Clamp(parameters.MeetVoicePercent / 100f, 0f, 1.5f);

        if (_tunerEnabled)
        {
            _tunerAnalyzer.AddSamples(guitarInput, frames);
        }

        FirConvolver? convolver = Volatile.Read(ref _externalConvolver);
        FirConvolver? convolverB = Volatile.Read(ref _externalConvolverB);
        bool useExternalIr = parameters.ExternalIrEnabled && convolver is not null;
        bool useExternalIrB = parameters.ExternalIrBEnabled && convolverB is not null;
        AmpModel ampModel = _ampModels[Math.Clamp(_activeAmpIndex, 0, _ampModels.Length - 1)];

        // NAM reemplaza solamente la etapa de amplificador. Se procesa por bloque para
        // evitar una llamada P/Invoke por muestra. Si el motor/modelo no está disponible,
        // se conserva automáticamente la ruta de amplificador interno de siempre.
        if (!_hardTunerBypass && parameters.NamEnabled && _namProcessor.HasModel && frames <= _namInput.Length)
        {
            ProcessNamPath(guitarInput, voiceInput, output, meetOutput, loopCaptureOutput, frames, parameters, ampModel,
                convolver, useExternalIr, convolverB, useExternalIrB, hasVoice,
                voiceMonitorGain, meetGuitarGain, meetVoiceGain);
            return;
        }

        for (int frame = 0; frame < frames; frame++)
        {
            float voice = hasVoice ? _voiceProcessor.Process(voiceInput![frame]) : 0f;
            float meetGuitarDuck = UpdateVoiceDuck(hasVoice && _voiceProcessor.SpeechDetected);
            float monitorGuitarDuck = MonitorVoiceDuck(meetGuitarDuck);
            float dryInput = SanitizeInput(guitarInput[frame]);

            // Bypass verdadero del ampli al afinar, pero la voz sigue disponible para Meet.
            if (_hardTunerBypass)
            {
                float baseDryGuitar = _tunerMuteOutput ? 0f : OutputLimiter(dryInput) * _masterGain;
                float dryGuitar = baseDryGuitar * monitorGuitarDuck;
                float tunerTone = _tunerTone.Process();
                float tunerMetronome = _metronome.Process();
                ProcessFollowedAccompaniment(dryInput, parameters, out float tunerDrums, out float tunerBass, out float tunerPiano);
                float tunerMonitorVoice = voice * voiceMonitorGain;
                float mixed = FinalOutputLimiter(dryGuitar + tunerTone + tunerMetronome + tunerDrums + tunerBass + tunerPiano + tunerMonitorVoice);
                int tunerOutputIndex = frame * 2;
                WriteLoopCapture(loopCaptureOutput, tunerOutputIndex, baseDryGuitar, baseDryGuitar);
                output[tunerOutputIndex] = mixed;
                output[tunerOutputIndex + 1] = mixed;
                if (meetOutput is not null)
                {
                    float meet = FinalOutputLimiter(
                        (baseDryGuitar * meetGuitarDuck) * meetGuitarGain +
                        voice * meetVoiceGain);
                    meetOutput[tunerOutputIndex] = meet;
                    meetOutput[tunerOutputIndex + 1] = meet;
                }
                continue;
            }

            float simulationMix = AdvanceSimulationMix();
            bool needsSimulation = simulationMix > 0.0001f || _simulationTarget > 0f;

            float processedLeft = dryInput;
            float processedRight = dryInput;
            if (needsSimulation)
            {
                float x = ProcessPreEffects(dryInput, parameters.PreEffectOrder, parameters.Eq5Placement);
                x = ampModel.ProcessAmplifier(x);
                x = ProcessCabinet(x, ampModel, convolver, useExternalIr, convolverB, useExternalIrB, parameters);
                if (parameters.Eq5Placement == EqPlacement.AfterAmp) x = _eq5.Process(x);

                _effectsLoop.Process(x, out processedLeft, out processedRight);
                _reverb.Process(processedLeft, processedRight, out processedLeft, out processedRight);
            }

            float dryMix = 1f - simulationMix;
            float left = (dryInput * dryMix) + (processedLeft * simulationMix);
            float right = (dryInput * dryMix) + (processedRight * simulationMix);

            // Se conserva exactamente el orden de ganancia/limitador de la 2.13.1
            // para no cambiar el sonido de guitarra ya aprobado.
            left = OutputLimiter(left) * _masterGain;
            right = OutputLimiter(right) * _masterGain;

            int outputIndex = frame * 2;
            WriteLoopCapture(loopCaptureOutput, outputIndex, left, right);

            float guitarLeft = left * monitorGuitarDuck;
            float guitarRight = right * monitorGuitarDuck;
            float meetGuitarLeft = left * meetGuitarDuck;
            float meetGuitarRight = right * meetGuitarDuck;
            float tunerToneNormal = _tunerTone.Process();
            float metronome = _metronome.Process();
            ProcessFollowedAccompaniment(dryInput, parameters, out float drums, out float backingBass, out float piano);
            float monitorVoice = voice * voiceMonitorGain;
            left = FinalOutputLimiter(guitarLeft + tunerToneNormal + metronome + drums + backingBass + piano + monitorVoice);
            right = FinalOutputLimiter(guitarRight + tunerToneNormal + metronome + drums + backingBass + piano + monitorVoice);

            output[outputIndex] = left;
            output[outputIndex + 1] = right;
            if (meetOutput is not null)
            {
                meetOutput[outputIndex] = FinalOutputLimiter(meetGuitarLeft * meetGuitarGain + voice * meetVoiceGain);
                meetOutput[outputIndex + 1] = FinalOutputLimiter(meetGuitarRight * meetGuitarGain + voice * meetVoiceGain);
            }
        }
    }

    private void ProcessVoiceOnly(float[]? voiceInput, float[] output, float[]? meetOutput,
        float[]? loopCaptureOutput, int frames, DspParameters parameters, bool hasVoice)
    {
        float monitorLevel = Math.Clamp(parameters.VoiceMonitorPercent / 100f, 0f, 1.5f);
        float sendLevel = Math.Clamp(parameters.MeetVoicePercent / 100f, 0f, 1.5f);
        for (int frame = 0; frame < frames; frame++)
        {
            float voice = hasVoice ? _voiceProcessor.Process(voiceInput![frame]) : 0f;
            int outputIndex = frame * 2;
            float monitor = FinalOutputLimiter(voice * monitorLevel);
            output[outputIndex] = monitor;
            output[outputIndex + 1] = monitor;
            WriteLoopCapture(loopCaptureOutput, outputIndex, 0f, 0f);
            if (meetOutput is not null)
            {
                float send = FinalOutputLimiter(voice * sendLevel);
                meetOutput[outputIndex] = send;
                meetOutput[outputIndex + 1] = send;
            }
        }
    }

    private void ProcessNamPath(
        float[] guitarInput,
        float[]? voiceInput,
        float[] output,
        float[]? meetOutput,
        float[]? loopCaptureOutput,
        int frames,
        DspParameters parameters,
        AmpModel ampModel,
        FirConvolver? convolver,
        bool useExternalIr,
        FirConvolver? convolverB,
        bool useExternalIrB,
        bool hasVoice,
        float voiceMonitorGain,
        float meetGuitarGain,
        float meetVoiceGain)
    {
        // Primera pasada: cadena previa al amplificador. La voz se procesa en paralelo
        // y se almacena para mezclarla al final exactamente en el mismo frame.
        for (int frame = 0; frame < frames; frame++)
        {
            float dryInput = SanitizeInput(guitarInput[frame]);
            _namDry[frame] = dryInput;
            _namVoice[frame] = hasVoice ? _voiceProcessor.Process(voiceInput![frame]) : 0f;
            _namVoiceDuck[frame] = UpdateVoiceDuck(hasVoice && _voiceProcessor.SpeechDetected);

            float x = ProcessPreEffects(dryInput, parameters.PreEffectOrder, parameters.Eq5Placement);
            _namInput[frame] = x;
        }

        _namProcessor.Process(_namInput, _namOutput, frames, parameters.NamInputTrimDb, parameters.NamOutputTrimDb,
            parameters.NamAutoLevelEnabled ? parameters.NamAutoLevelDb : 0f);

        long calibrationRemaining = Volatile.Read(ref _namCalibrationSamplesRemaining);
        if (calibrationRemaining > 0)
        {
            int count = (int)Math.Min(frames, calibrationRemaining);
            double sum = 0.0;
            for (int i = 0; i < count; i++)
            {
                float v = _namOutput[i];
                if (float.IsFinite(v)) sum += v * v;
            }
            _namCalibrationSumSquares += sum;
            Interlocked.Add(ref _namCalibrationMeasuredSamples, count);
            long left = Interlocked.Add(ref _namCalibrationSamplesRemaining, -count);
            if (left <= 0)
            {
                long n = Math.Max(1, Volatile.Read(ref _namCalibrationMeasuredSamples));
                double rms = Math.Sqrt(_namCalibrationSumSquares / n);
                _namCalibrationRmsDb = rms > 1e-9 ? (float)(20.0 * Math.Log10(rms)) : -120f;
                Interlocked.Exchange(ref _namCalibrationCompleted, 1);
            }
        }

        // Segunda pasada: gabinete opcional, loop, reverb, master y mezcla de voz.
        for (int frame = 0; frame < frames; frame++)
        {
            float dryInput = _namDry[frame];
            float simulationMix = AdvanceSimulationMix();
            float processedLeft = dryInput;
            float processedRight = dryInput;

            if (simulationMix > 0.0001f || _simulationTarget > 0f)
            {
                float x = _namOutput[frame];
                if (!parameters.NamIncludesCabinet)
                {
                    x = ProcessCabinet(x, ampModel, convolver, useExternalIr, convolverB, useExternalIrB, parameters);
                }
                if (parameters.Eq5Placement == EqPlacement.AfterAmp) x = _eq5.Process(x);

                _effectsLoop.Process(x, out processedLeft, out processedRight);
                _reverb.Process(processedLeft, processedRight, out processedLeft, out processedRight);
            }

            float dryMix = 1f - simulationMix;
            float left = (dryInput * dryMix) + (processedLeft * simulationMix);
            float right = (dryInput * dryMix) + (processedRight * simulationMix);
            left = OutputLimiter(left) * _masterGain;
            right = OutputLimiter(right) * _masterGain;

            int outputIndex = frame * 2;
            WriteLoopCapture(loopCaptureOutput, outputIndex, left, right);

            float meetGuitarDuck = _namVoiceDuck[frame];
            float monitorGuitarDuck = MonitorVoiceDuck(meetGuitarDuck);
            float guitarLeft = left * monitorGuitarDuck;
            float guitarRight = right * monitorGuitarDuck;
            float meetGuitarLeft = left * meetGuitarDuck;
            float meetGuitarRight = right * meetGuitarDuck;
            float tunerTone = _tunerTone.Process();
            float metronome = _metronome.Process();
            ProcessFollowedAccompaniment(dryInput, parameters, out float drums, out float backingBass, out float piano);
            float voice = _namVoice[frame];
            float monitorVoice = voice * voiceMonitorGain;
            left = FinalOutputLimiter(guitarLeft + tunerTone + metronome + drums + backingBass + piano + monitorVoice);
            right = FinalOutputLimiter(guitarRight + tunerTone + metronome + drums + backingBass + piano + monitorVoice);

            output[outputIndex] = left;
            output[outputIndex + 1] = right;
            if (meetOutput is not null)
            {
                meetOutput[outputIndex] = FinalOutputLimiter(meetGuitarLeft * meetGuitarGain + voice * meetVoiceGain);
                meetOutput[outputIndex + 1] = FinalOutputLimiter(meetGuitarRight * meetGuitarGain + voice * meetVoiceGain);
            }
        }
    }


    private void ProcessFollowedAccompaniment(float guitarSample, DspParameters parameters,
        out float drums, out float bass, out float piano)
    {
        bool play = ShouldPlayFollowedAccompaniment(guitarSample, parameters.AccompanimentFollowGuitar);
        if (play)
        {
            drums = _drums.Process();
            bass = _backingBass.Process();
            piano = _piano.Process();
        }
        else
        {
            drums = 0f;
            bass = 0f;
            piano = 0f;
        }
    }

    private bool ShouldPlayFollowedAccompaniment(float guitarSample, bool followGuitar)
    {
        if (followGuitar != _accompanimentFollowLast)
        {
            _accompanimentFollowLast = followGuitar;
            _accompanimentGateActive = false;
            _accompanimentAttackCounter = 0;
            _accompanimentSilenceCounter = 0;
            _drums.Reset();
            _backingBass.Reset();
            _piano.Reset();
        }

        if (!followGuitar) return true;

        float level = MathF.Abs(float.IsFinite(guitarSample) ? guitarSample : 0f);
        if (!_accompanimentGateActive)
        {
            if (level >= AccompanimentStartThreshold)
                _accompanimentAttackCounter++;
            else if (_accompanimentAttackCounter > 0)
                _accompanimentAttackCounter--;

            if (_accompanimentAttackCounter >= _accompanimentAttackRequiredSamples)
            {
                _accompanimentGateActive = true;
                _accompanimentAttackCounter = 0;
                _accompanimentSilenceCounter = 0;
                // El primer ataque de guitarra define el tiempo 1.
                _drums.Reset();
                _backingBass.Reset();
                _piano.Reset();
            }
        }
        else
        {
            if (level >= AccompanimentStopThreshold)
            {
                _accompanimentSilenceCounter = 0;
            }
            else if (++_accompanimentSilenceCounter >= _accompanimentSilenceHoldSamples)
            {
                _accompanimentGateActive = false;
                _accompanimentSilenceCounter = 0;
                _accompanimentAttackCounter = 0;
                _drums.Reset();
                _backingBass.Reset();
                _piano.Reset();
            }
        }

        return _accompanimentGateActive;
    }

    private static void WriteLoopCapture(float[]? capture, int outputIndex, float left, float right)
    {
        if (capture is null || outputIndex < 0 || outputIndex + 1 >= capture.Length) return;
        capture[outputIndex] = float.IsFinite(left) ? left : 0f;
        capture[outputIndex + 1] = float.IsFinite(right) ? right : 0f;
    }

    private float ProcessInputChorusesMono(float input)
    {
        _inputChorus.Process(input, out float chorusLeft, out float chorusRight);
        float x = (chorusLeft + chorusRight) * 0.5f;
        _inputAnalogChorus.Process(x, x, out float analogLeft, out float analogRight);
        return (analogLeft + analogRight) * 0.5f;
    }

    private float ProcessCabinet(float input, AmpModel ampModel, FirConvolver? convolverA, bool useExternalIrA,
        FirConvolver? convolverB, bool useExternalIrB, DspParameters parameters)
    {
        float a = useExternalIrA && convolverA is not null
            ? convolverA.Process(input)
            : ampModel.ProcessBuiltInCabinet(input);

        float mixed = a;
        if (useExternalIrB && convolverB is not null)
        {
            float b = convolverB.Process(input);
            if (parameters.IrBPhaseInvert) b = -b;
            float mixB = Math.Clamp(parameters.IrMixPercent / 100f, 0f, 1f);
            mixed = (a * (1f - mixB)) + (b * mixB);
        }

        if (parameters.IrLowCutHz > 20.1f) mixed = _cabHighPass.Process(mixed);
        if (parameters.IrHighCutHz < 19999f) mixed = _cabLowPass.Process(mixed);
        return mixed;
    }

    private float ProcessPreEffects(float input, PreEffectSlot[]? order, EqPlacement eqPlacement)
    {
        float x = input;
        PreEffectSlot[] chain = order is { Length: > 0 } ? order : new[]
        {
            PreEffectSlot.Booster, PreEffectSlot.Compressor, PreEffectSlot.AutoWah,
            PreEffectSlot.Gate, PreEffectSlot.Octaver, PreEffectSlot.Eq5, PreEffectSlot.Od1,
            PreEffectSlot.Divine, PreEffectSlot.Ds1, PreEffectSlot.Fuzz, PreEffectSlot.Chorus
        };

        foreach (PreEffectSlot slot in chain)
        {
            x = slot switch
            {
                PreEffectSlot.Booster => _booster.Process(x),
                PreEffectSlot.Compressor => _compressor.Process(x),
                PreEffectSlot.AutoWah => _autoWah.Process(x, x),
                PreEffectSlot.Gate => _noiseGate.Process(x),
                PreEffectSlot.Octaver => _octaver.Process(x),
                PreEffectSlot.Eq5 when eqPlacement == EqPlacement.BeforeAmp => _eq5.Process(x),
                PreEffectSlot.Od1 => _od1Overdrive.Process(x),
                PreEffectSlot.Divine => _divineOverdrive.Process(x),
                PreEffectSlot.Ds1 => _ds1Distortion.Process(x),
                PreEffectSlot.Fuzz => _fuzz.Process(x),
                PreEffectSlot.Chorus => ProcessInputChorusesMono(x),
                _ => x
            };
        }
        return x;
    }

    private float AdvanceSimulationMix()
    {
        if (_simulationMix < _simulationTarget)
        {
            _simulationMix = MathF.Min(_simulationTarget, _simulationMix + _simulationRampStep);
        }
        else if (_simulationMix > _simulationTarget)
        {
            _simulationMix = MathF.Max(_simulationTarget, _simulationMix - _simulationRampStep);
        }
        return _simulationMix;
    }


    private void ApplyParameters(DspParameters parameters)
    {
        DspParameters previous = _appliedParameters;
        bool first = !_hasAppliedParameters;

        if (first ||
            previous.GateEnabled != parameters.GateEnabled ||
            Changed(previous.GateThresholdDb, parameters.GateThresholdDb) ||
            Changed(previous.GateReleaseMs, parameters.GateReleaseMs))
        {
            _noiseGate.Configure(parameters.GateEnabled, parameters.GateThresholdDb, parameters.GateReleaseMs);
        }

        bool compressorChanged = first ||
            previous.CompressorEnabled != parameters.CompressorEnabled || previous.CompressorCharacter != parameters.CompressorCharacter ||
            Changed(previous.CompressorSustain, parameters.CompressorSustain) ||
            Changed(previous.CompressorAttackMs, parameters.CompressorAttackMs) ||
            Changed(previous.CompressorLevel, parameters.CompressorLevel);
        if (compressorChanged)
        {
            if (!first && !previous.CompressorEnabled && parameters.CompressorEnabled)
            {
                _compressor.Reset();
            }
            _compressor.Configure(parameters.CompressorEnabled, parameters.CompressorCharacter, parameters.CompressorSustain,
                parameters.CompressorAttackMs, parameters.CompressorLevel);
        }

        bool autoWahChanged = first ||
            previous.AutoWahEnabled != parameters.AutoWahEnabled ||
            previous.AutoWahMode != parameters.AutoWahMode ||
            previous.AutoWahCharacter != parameters.AutoWahCharacter ||
            Changed(previous.AutoWahSensitivity, parameters.AutoWahSensitivity) ||
            Changed(previous.AutoWahRange, parameters.AutoWahRange) ||
            Changed(previous.AutoWahResonance, parameters.AutoWahResonance) ||
            Changed(previous.AutoWahManualPositionPercent, parameters.AutoWahManualPositionPercent);
        if (autoWahChanged)
        {
            if (!first && !previous.AutoWahEnabled && parameters.AutoWahEnabled)
            {
                _autoWah.Reset();
            }
            _autoWah.Configure(parameters.AutoWahEnabled, parameters.AutoWahMode, parameters.AutoWahCharacter,
                parameters.AutoWahSensitivity, parameters.AutoWahRange, parameters.AutoWahResonance,
                parameters.AutoWahManualPositionPercent);
        }

        bool ds1Changed = first ||
            previous.OverdriveEnabled != parameters.OverdriveEnabled || previous.DistortionCharacter != parameters.DistortionCharacter ||
            Changed(previous.OverdriveGain, parameters.OverdriveGain) ||
            Changed(previous.OverdriveTone, parameters.OverdriveTone) ||
            Changed(previous.OverdriveLevel, parameters.OverdriveLevel);
        if (ds1Changed)
        {
            if (!first && !previous.OverdriveEnabled && parameters.OverdriveEnabled)
            {
                _ds1Distortion.Reset();
            }

            _ds1Distortion.Configure(parameters.OverdriveEnabled, parameters.DistortionCharacter, parameters.OverdriveGain,
                parameters.OverdriveTone, parameters.OverdriveLevel);
        }

        bool ts9Changed = first ||
            previous.Ts9Enabled != parameters.Ts9Enabled ||
            previous.DriveCharacter != parameters.DriveCharacter ||
            Changed(previous.Ts9Gain, parameters.Ts9Gain) ||
            Changed(previous.Ts9Tone, parameters.Ts9Tone) ||
            Changed(previous.Ts9Level, parameters.Ts9Level);
        if (ts9Changed)
        {
            if (!first && !previous.Ts9Enabled && parameters.Ts9Enabled)
            {
                _divineOverdrive.Reset();
            }

            _divineOverdrive.Configure(parameters.Ts9Enabled, parameters.DriveCharacter, parameters.Ts9Gain,
                parameters.Ts9Tone, parameters.Ts9Level);
        }

        bool od1Changed = first ||
            previous.Od1Enabled != parameters.Od1Enabled ||
            previous.Od1Character != parameters.Od1Character ||
            Changed(previous.Od1Drive, parameters.Od1Drive) ||
            Changed(previous.Od1Tone, parameters.Od1Tone) ||
            Changed(previous.Od1Level, parameters.Od1Level);
        if (od1Changed)
        {
            if (!first && !previous.Od1Enabled && parameters.Od1Enabled) _od1Overdrive.Reset();
            _od1Overdrive.Configure(parameters.Od1Enabled, parameters.Od1Character, parameters.Od1Drive, parameters.Od1Tone, parameters.Od1Level);
        }

        bool fuzzChanged = first ||
            previous.FuzzEnabled != parameters.FuzzEnabled || previous.FuzzCharacter != parameters.FuzzCharacter ||
            Changed(previous.FuzzGain, parameters.FuzzGain) || Changed(previous.FuzzTone, parameters.FuzzTone) ||
            Changed(previous.FuzzLevel, parameters.FuzzLevel);
        if (fuzzChanged)
        {
            if (!first && !previous.FuzzEnabled && parameters.FuzzEnabled) _fuzz.Reset();
            _fuzz.Configure(parameters.FuzzEnabled, parameters.FuzzCharacter, parameters.FuzzGain, parameters.FuzzTone, parameters.FuzzLevel);
        }

        bool eq5Changed = first ||
            previous.Eq5Enabled != parameters.Eq5Enabled || previous.Eq5Placement != parameters.Eq5Placement ||
            Changed(previous.Eq5Band100Db, parameters.Eq5Band100Db) || Changed(previous.Eq5Band250Db, parameters.Eq5Band250Db) ||
            Changed(previous.Eq5Band800Db, parameters.Eq5Band800Db) || Changed(previous.Eq5Band2500Db, parameters.Eq5Band2500Db) ||
            Changed(previous.Eq5Band6400Db, parameters.Eq5Band6400Db) || Changed(previous.Eq5OutputDb, parameters.Eq5OutputDb);
        if (eq5Changed)
        {
            if (!first && (!previous.Eq5Enabled && parameters.Eq5Enabled || previous.Eq5Placement != parameters.Eq5Placement)) _eq5.Reset();
            _eq5.Configure(parameters.Eq5Enabled, parameters.Eq5Band100Db, parameters.Eq5Band250Db, parameters.Eq5Band800Db,
                parameters.Eq5Band2500Db, parameters.Eq5Band6400Db, parameters.Eq5OutputDb);
        }

        bool boosterChanged = first ||
            previous.BoosterEnabled != parameters.BoosterEnabled || previous.BoosterCharacter != parameters.BoosterCharacter ||
            Changed(previous.BoosterDb, parameters.BoosterDb);
        if (boosterChanged)
        {
            if (!first && !previous.BoosterEnabled && parameters.BoosterEnabled)
            {
                _booster.Reset();
            }

            _booster.Configure(parameters.BoosterEnabled, parameters.BoosterCharacter, parameters.BoosterDb);
        }

        bool octaverChanged = first ||
            previous.OctaverEnabled != parameters.OctaverEnabled ||
            previous.OctaverCharacter != parameters.OctaverCharacter ||
            Changed(previous.OctaverDryPercent, parameters.OctaverDryPercent) ||
            Changed(previous.OctaverDownPercent, parameters.OctaverDownPercent) ||
            Changed(previous.OctaverUpPercent, parameters.OctaverUpPercent) ||
            Changed(previous.OctaverTonePercent, parameters.OctaverTonePercent) ||
            Changed(previous.OctaverLevelPercent, parameters.OctaverLevelPercent);
        if (octaverChanged)
        {
            _octaver.Configure(
                parameters.OctaverEnabled,
                parameters.OctaverCharacter,
                parameters.OctaverDryPercent,
                parameters.OctaverDownPercent,
                parameters.OctaverUpPercent,
                parameters.OctaverTonePercent,
                parameters.OctaverLevelPercent);
        }

        if (first || Changed(previous.IrLowCutHz, parameters.IrLowCutHz))
        {
            _cabHighPass.SetHighPass(SampleRate, parameters.IrLowCutHz);
            _cabHighPass.Reset();
        }
        if (first || Changed(previous.IrHighCutHz, parameters.IrHighCutHz))
        {
            _cabLowPass.SetLowPass(SampleRate, parameters.IrHighCutHz);
            _cabLowPass.Reset();
        }

        // Los pedales cambian mediante sus fundidos internos. No se apartan chorus,
        // delay ni reverb al activarlos: ese bypass temporal podía parecer un corte.

        // Los tres amplificadores permanecen precargados. Cambiar de canal sólo cambia
        // el índice activo: no recalcula filtros dentro del callback ASIO.
        if (first || Changed(previous.Gain, parameters.Gain))
        {
            foreach (AmpModel model in _ampModels)
            {
                model.SetGain(parameters.Gain);
            }
        }

        if (first ||
            Changed(previous.CleanBass, parameters.CleanBass) ||
            Changed(previous.CleanMiddle, parameters.CleanMiddle) ||
            Changed(previous.CleanTreble, parameters.CleanTreble) ||
            Changed(previous.CleanPresence, parameters.CleanPresence))
        {
            _ampModels[(int)AmpChannel.CleanTwin].Configure(AmpChannel.CleanTwin, parameters.Gain,
                parameters.CleanBass, parameters.CleanMiddle, parameters.CleanTreble, parameters.CleanPresence);
        }

        if (first ||
            Changed(previous.CrunchBass, parameters.CrunchBass) ||
            Changed(previous.CrunchMiddle, parameters.CrunchMiddle) ||
            Changed(previous.CrunchTreble, parameters.CrunchTreble) ||
            Changed(previous.CrunchPresence, parameters.CrunchPresence))
        {
            _ampModels[(int)AmpChannel.CrunchBritish].Configure(AmpChannel.CrunchBritish, parameters.Gain,
                parameters.CrunchBass, parameters.CrunchMiddle, parameters.CrunchTreble, parameters.CrunchPresence);
        }

        if (first ||
            Changed(previous.LeadBass, parameters.LeadBass) ||
            Changed(previous.LeadMiddle, parameters.LeadMiddle) ||
            Changed(previous.LeadTreble, parameters.LeadTreble) ||
            Changed(previous.LeadPresence, parameters.LeadPresence))
        {
            _ampModels[(int)AmpChannel.LeadJcm800].Configure(AmpChannel.LeadJcm800, parameters.Gain,
                parameters.LeadBass, parameters.LeadMiddle, parameters.LeadTreble, parameters.LeadPresence);
        }

        if (first || Changed(previous.Gain, parameters.Gain))
        {
            _ampModels[(int)AmpChannel.CleanBoutique].Configure(AmpChannel.CleanBoutique, parameters.Gain,5.5f,4.5f,5.5f,4.5f);
            _ampModels[(int)AmpChannel.CleanClassA].Configure(AmpChannel.CleanClassA, parameters.Gain,4.5f,5f,6.2f,5.5f);
            _ampModels[(int)AmpChannel.CrunchPlexi].Configure(AmpChannel.CrunchPlexi, parameters.Gain,5f,6.2f,5.4f,5.2f);
            _ampModels[(int)AmpChannel.CrunchClassA].Configure(AmpChannel.CrunchClassA, parameters.Gain,4.8f,6f,6f,5.8f);
            _ampModels[(int)AmpChannel.LeadModern].Configure(AmpChannel.LeadModern, parameters.Gain,4.5f,5.5f,5.2f,5.5f);
            _ampModels[(int)AmpChannel.LeadLegacy].Configure(AmpChannel.LeadLegacy, parameters.Gain,5f,6.5f,5f,5.2f);
        }

        if (first || previous.Channel != parameters.Channel)
        {
            _activeAmpIndex = Math.Clamp((int)parameters.Channel, 0, _ampModels.Length - 1);
        }

        // Aplicar siempre la ganancia y EQ visibles al amplificador realmente activo.
        // En versiones anteriores Bass/Middle/Treble/Presence existían en DspParameters,
        // pero AudioProcessor nunca los utilizaba. Eso anulaba gran parte del contraste
        // entre presets y hacía que distintos bancos sonaran casi iguales.
        if (first ||
            previous.Channel != parameters.Channel ||
            Changed(previous.Gain, parameters.Gain) ||
            Changed(previous.Bass, parameters.Bass) ||
            Changed(previous.Middle, parameters.Middle) ||
            Changed(previous.Treble, parameters.Treble) ||
            Changed(previous.Presence, parameters.Presence))
        {
            _ampModels[_activeAmpIndex].Configure(
                parameters.Channel,
                parameters.Gain,
                parameters.Bass,
                parameters.Middle,
                parameters.Treble,
                parameters.Presence);
        }

        if (first || previous.ChorusEnabled != parameters.ChorusEnabled ||
            previous.ChorusPlacement != parameters.ChorusPlacement ||
            previous.ChorusCharacter != parameters.ChorusCharacter ||
            Changed(previous.ChorusRateHz, parameters.ChorusRateHz) ||
            Changed(previous.ChorusDepthMs, parameters.ChorusDepthMs) ||
            Changed(previous.ChorusMixPercent, parameters.ChorusMixPercent))
        {
            bool inputChorusEnabled = parameters.ChorusEnabled && parameters.ChorusPlacement == ChorusPlacement.Input;
            float effectiveRate = parameters.ChorusCharacter == ChorusCharacter.Dimension ? MathF.Max(0.12f, parameters.ChorusRateHz * 0.42f) : parameters.ChorusRateHz;
            float effectiveDepth = parameters.ChorusCharacter == ChorusCharacter.Dimension ? MathF.Max(2.5f, parameters.ChorusDepthMs * 0.58f) : parameters.ChorusDepthMs;
            float effectiveMix = parameters.ChorusCharacter == ChorusCharacter.Dimension ? MathF.Min(72f, parameters.ChorusMixPercent * 1.12f) : parameters.ChorusMixPercent;
            _inputChorus.Configure(inputChorusEnabled, effectiveRate, effectiveDepth, effectiveMix);
        }

        if (first || previous.AnalogChorusEnabled != parameters.AnalogChorusEnabled ||
            previous.AnalogChorusPlacement != parameters.AnalogChorusPlacement ||
            Changed(previous.AnalogChorusRateHz, parameters.AnalogChorusRateHz) ||
            Changed(previous.AnalogChorusDepth, parameters.AnalogChorusDepth) ||
            Changed(previous.AnalogChorusMixPercent, parameters.AnalogChorusMixPercent) ||
            Changed(previous.AnalogChorusLow, parameters.AnalogChorusLow) ||
            Changed(previous.AnalogChorusHigh, parameters.AnalogChorusHigh))
        {
            bool inputAnalogEnabled = parameters.AnalogChorusEnabled && parameters.AnalogChorusPlacement == ChorusPlacement.Input;
            _inputAnalogChorus.Configure(inputAnalogEnabled, parameters.AnalogChorusRateHz, parameters.AnalogChorusDepth,
                parameters.AnalogChorusMixPercent, parameters.AnalogChorusLow, parameters.AnalogChorusHigh);
        }

        if (first ||
            previous.FxLoopEnabled != parameters.FxLoopEnabled ||
            Changed(previous.FxLoopSendPercent, parameters.FxLoopSendPercent) ||
            Changed(previous.FxLoopReturnPercent, parameters.FxLoopReturnPercent) ||
            previous.PhaserEnabled != parameters.PhaserEnabled ||
            Changed(previous.PhaserRateHz, parameters.PhaserRateHz) ||
            Changed(previous.PhaserDepthPercent, parameters.PhaserDepthPercent) ||
            Changed(previous.PhaserFeedbackPercent, parameters.PhaserFeedbackPercent) ||
            Changed(previous.PhaserMixPercent, parameters.PhaserMixPercent) ||
            previous.FlangerEnabled != parameters.FlangerEnabled || previous.FlangerCharacter != parameters.FlangerCharacter ||
            Changed(previous.FlangerRateHz, parameters.FlangerRateHz) ||
            Changed(previous.FlangerDepthPercent, parameters.FlangerDepthPercent) ||
            Changed(previous.FlangerFeedbackPercent, parameters.FlangerFeedbackPercent) ||
            Changed(previous.FlangerMixPercent, parameters.FlangerMixPercent) ||
            previous.ChorusEnabled != parameters.ChorusEnabled || previous.ChorusPlacement != parameters.ChorusPlacement ||
            previous.ChorusCharacter != parameters.ChorusCharacter ||
            Changed(previous.ChorusRateHz, parameters.ChorusRateHz) ||
            Changed(previous.ChorusDepthMs, parameters.ChorusDepthMs) ||
            Changed(previous.ChorusMixPercent, parameters.ChorusMixPercent) ||
            previous.AnalogChorusEnabled != parameters.AnalogChorusEnabled ||
            previous.AnalogChorusPlacement != parameters.AnalogChorusPlacement ||
            Changed(previous.AnalogChorusRateHz, parameters.AnalogChorusRateHz) ||
            Changed(previous.AnalogChorusDepth, parameters.AnalogChorusDepth) ||
            Changed(previous.AnalogChorusMixPercent, parameters.AnalogChorusMixPercent) ||
            Changed(previous.AnalogChorusLow, parameters.AnalogChorusLow) ||
            Changed(previous.AnalogChorusHigh, parameters.AnalogChorusHigh) ||
            previous.MicroPitchEnabled != parameters.MicroPitchEnabled ||
            Changed(previous.MicroPitchDetuneCents, parameters.MicroPitchDetuneCents) ||
            Changed(previous.MicroPitchDelayMs, parameters.MicroPitchDelayMs) ||
            Changed(previous.MicroPitchMixPercent, parameters.MicroPitchMixPercent) ||
            previous.RotaryEnabled != parameters.RotaryEnabled || previous.RotaryFast != parameters.RotaryFast || Changed(previous.RotaryRateHz, parameters.RotaryRateHz) ||
            Changed(previous.RotaryDepthPercent, parameters.RotaryDepthPercent) || Changed(previous.RotaryMixPercent, parameters.RotaryMixPercent) ||
            previous.TremoloEnabled != parameters.TremoloEnabled ||
            Changed(previous.TremoloRateHz, parameters.TremoloRateHz) ||
            Changed(previous.TremoloDepthPercent, parameters.TremoloDepthPercent) ||
            previous.DelayEnabled != parameters.DelayEnabled ||
            previous.DelayCharacter != parameters.DelayCharacter ||
            Changed(previous.DelayTimeMs, parameters.DelayTimeMs) ||
            Changed(previous.DelayFeedbackPercent, parameters.DelayFeedbackPercent) ||
            Changed(previous.DelayMixPercent, parameters.DelayMixPercent))
        {
            _effectsLoop.Configure(
                parameters.FxLoopEnabled,
                parameters.FxLoopSendPercent,
                parameters.FxLoopReturnPercent,
                parameters.PhaserEnabled,
                parameters.PhaserRateHz,
                parameters.PhaserDepthPercent,
                parameters.PhaserFeedbackPercent,
                parameters.PhaserMixPercent,
                parameters.FlangerEnabled,
                parameters.FlangerCharacter,
                parameters.FlangerRateHz,
                parameters.FlangerDepthPercent,
                parameters.FlangerFeedbackPercent,
                parameters.FlangerMixPercent,
                parameters.ChorusEnabled && parameters.ChorusPlacement == ChorusPlacement.Loop,
                parameters.ChorusCharacter,
                parameters.ChorusRateHz,
                parameters.ChorusDepthMs,
                parameters.ChorusMixPercent,
                parameters.AnalogChorusEnabled && parameters.AnalogChorusPlacement == ChorusPlacement.Loop,
                parameters.AnalogChorusRateHz,
                parameters.AnalogChorusDepth,
                parameters.AnalogChorusMixPercent,
                parameters.AnalogChorusLow,
                parameters.AnalogChorusHigh,
                parameters.MicroPitchEnabled,
                parameters.MicroPitchDetuneCents,
                parameters.MicroPitchDelayMs,
                parameters.MicroPitchMixPercent,
                parameters.RotaryEnabled,
                parameters.RotaryFast,
                parameters.RotaryRateHz,
                parameters.RotaryDepthPercent,
                parameters.RotaryMixPercent,
                parameters.TremoloEnabled,
                parameters.TremoloRateHz,
                parameters.TremoloDepthPercent,
                parameters.DelayEnabled,
                parameters.DelayCharacter,
                parameters.DelayTimeMs,
                parameters.DelayFeedbackPercent,
                parameters.DelayMixPercent);
        }

        if (first ||
            previous.ReverbEnabled != parameters.ReverbEnabled ||
            previous.ReverbCharacter != parameters.ReverbCharacter ||
            Changed(previous.ReverbMixPercent, parameters.ReverbMixPercent) ||
            Changed(previous.ReverbDecayPercent, parameters.ReverbDecayPercent) ||
            Changed(previous.ReverbTonePercent, parameters.ReverbTonePercent) ||
            Changed(previous.ReverbPreDelayMs, parameters.ReverbPreDelayMs) ||
            Changed(previous.ReverbDampingPercent, parameters.ReverbDampingPercent) ||
            Changed(previous.ReverbDiffusionPercent, parameters.ReverbDiffusionPercent))
        {
            _reverb.Configure(parameters.ReverbEnabled, parameters.ReverbCharacter, parameters.ReverbMixPercent,
                parameters.ReverbDecayPercent, parameters.ReverbTonePercent,
                parameters.ReverbPreDelayMs, parameters.ReverbDampingPercent, parameters.ReverbDiffusionPercent);
        }

        bool voiceChanged = first ||
            previous.VoiceEnabled != parameters.VoiceEnabled ||
            previous.VoiceSuppressorEnabled != parameters.VoiceSuppressorEnabled ||
            Changed(previous.VoiceThresholdDb, parameters.VoiceThresholdDb) ||
            Changed(previous.VoiceReductionDb, parameters.VoiceReductionDb) ||
            Changed(previous.VoiceReleaseMs, parameters.VoiceReleaseMs) ||
            Changed(previous.VoiceHighPassHz, parameters.VoiceHighPassHz) ||
            Changed(previous.VoiceBassDb, parameters.VoiceBassDb) ||
            Changed(previous.VoiceMidDb, parameters.VoiceMidDb) ||
            Changed(previous.VoiceTrebleDb, parameters.VoiceTrebleDb) ||
            Changed(previous.VoiceLevelPercent, parameters.VoiceLevelPercent);
        if (voiceChanged)
        {
            if (!first && !previous.VoiceEnabled && parameters.VoiceEnabled)
            {
                _voiceProcessor.Reset();
            }
            _voiceProcessor.Configure(parameters.VoiceEnabled, parameters.VoiceSuppressorEnabled,
                parameters.VoiceThresholdDb, parameters.VoiceReductionDb, parameters.VoiceReleaseMs,
                parameters.VoiceHighPassHz, parameters.VoiceBassDb, parameters.VoiceMidDb,
                parameters.VoiceTrebleDb, parameters.VoiceLevelPercent);
            if (!parameters.VoiceEnabled)
            {
                _voiceDuckGain = 1f;
                _voiceDuckHoldCounter = 0;
            }
        }

        if (parameters.SimulationEnabled != _simulationEnabled)
        {
            _simulationEnabled = parameters.SimulationEnabled;
            _simulationTarget = _simulationEnabled ? 1f : 0f;
            _effectsLoop.Reset();
            _reverb.Reset();
        }

        bool newHardTunerBypass = parameters.TunerEnabled;
        if (newHardTunerBypass != _hardTunerBypass)
        {
            _effectsLoop.ResetDelay();
        }

        bool tunerChanged = first ||
            previous.TunerEnabled != parameters.TunerEnabled ||
            previous.TunerMuteOutput != parameters.TunerMuteOutput ||
            previous.TunerSoundGuideEnabled != parameters.TunerSoundGuideEnabled ||
            Changed(previous.TunerReferenceAHz, parameters.TunerReferenceAHz) ||
            Changed(previous.TunerGuideVolumePercent, parameters.TunerGuideVolumePercent);
        if (tunerChanged)
        {
            _tunerEnabled = parameters.TunerEnabled;
            _tunerMuteOutput = parameters.TunerMuteOutput;
            _hardTunerBypass = newHardTunerBypass;
            _tunerAnalyzer.SetEnabled(parameters.TunerEnabled, parameters.TunerReferenceAHz);
            _tunerTone.Configure(parameters.TunerEnabled && parameters.TunerSoundGuideEnabled,
                parameters.TunerGuideVolumePercent);
            if (!parameters.TunerEnabled)
            {
                _tunerTone.SetDirection(TuningDirection.NoSignal);
            }
        }

        bool metronomeChanged = first ||
            previous.MetronomeEnabled != parameters.MetronomeEnabled ||
            Changed(previous.MetronomeBpm, parameters.MetronomeBpm) ||
            previous.MetronomeBeatsPerBar != parameters.MetronomeBeatsPerBar ||
            previous.MetronomeAccentFirstBeat != parameters.MetronomeAccentFirstBeat ||
            Changed(previous.MetronomeVolumePercent, parameters.MetronomeVolumePercent);
        if (metronomeChanged)
        {
            _metronome.Configure(parameters.MetronomeEnabled, parameters.MetronomeBpm,
                parameters.MetronomeBeatsPerBar, parameters.MetronomeAccentFirstBeat,
                parameters.MetronomeVolumePercent);
        }

        bool drumsChanged = first ||
            previous.DrumsEnabled != parameters.DrumsEnabled ||
            Changed(previous.MetronomeBpm, parameters.MetronomeBpm) ||
            previous.DrumPattern != parameters.DrumPattern ||
            Changed(previous.DrumVolumePercent, parameters.DrumVolumePercent);
        if (drumsChanged)
        {
            _drums.Configure(parameters.DrumsEnabled, parameters.MetronomeBpm,
                parameters.DrumPattern, parameters.DrumVolumePercent);
        }

        bool backingBassChanged = first ||
            previous.BackingBassEnabled != parameters.BackingBassEnabled ||
            Changed(previous.MetronomeBpm, parameters.MetronomeBpm) ||
            previous.DrumPattern != parameters.DrumPattern ||
            previous.BackingBassKey != parameters.BackingBassKey ||
            previous.BackingBassMinor != parameters.BackingBassMinor ||
            previous.BackingBassLine != parameters.BackingBassLine ||
            Changed(previous.BackingBassVolumePercent, parameters.BackingBassVolumePercent);
        if (backingBassChanged)
        {
            _backingBass.Configure(parameters.BackingBassEnabled, parameters.MetronomeBpm,
                parameters.DrumPattern, parameters.BackingBassVolumePercent,
                parameters.BackingBassKey, parameters.BackingBassMinor, parameters.BackingBassLine);
        }

        bool pianoChanged = first ||
            previous.PianoEnabled != parameters.PianoEnabled ||
            Changed(previous.MetronomeBpm, parameters.MetronomeBpm) ||
            previous.MetronomeBeatsPerBar != parameters.MetronomeBeatsPerBar ||
            previous.DrumPattern != parameters.DrumPattern ||
            previous.PianoSound != parameters.PianoSound ||
            previous.PianoKey != parameters.PianoKey ||
            previous.PianoProgression != parameters.PianoProgression ||
            previous.PianoCustomProgressionPack1 != parameters.PianoCustomProgressionPack1 ||
            previous.PianoCustomProgressionPack2 != parameters.PianoCustomProgressionPack2 ||
            previous.PianoCustomProgressionPack3 != parameters.PianoCustomProgressionPack3 ||
            previous.PianoCustomProgressionPack4 != parameters.PianoCustomProgressionPack4 ||
            previous.PianoCustomProgressionPack5 != parameters.PianoCustomProgressionPack5 ||
            previous.PianoCustomProgressionPack6 != parameters.PianoCustomProgressionPack6 ||
            previous.PianoCustomProgressionPack7 != parameters.PianoCustomProgressionPack7 ||
            previous.PianoCustomProgressionPack8 != parameters.PianoCustomProgressionPack8 ||
            previous.PianoCustomProgressionCount != parameters.PianoCustomProgressionCount ||
            previous.PianoCustomProgressionHash != parameters.PianoCustomProgressionHash ||
            previous.PianoStyle != parameters.PianoStyle ||
            Changed(previous.PianoVolumePercent, parameters.PianoVolumePercent);
        if (pianoChanged)
        {
            _piano.Configure(parameters.PianoEnabled, parameters.MetronomeBpm, parameters.MetronomeBeatsPerBar,
                parameters.DrumPattern, parameters.PianoVolumePercent, parameters.PianoKey,
                parameters.PianoSound, parameters.PianoProgression, parameters.PianoStyle,
                parameters.PianoCustomProgressionPack1, parameters.PianoCustomProgressionPack2,
                parameters.PianoCustomProgressionPack3, parameters.PianoCustomProgressionPack4,
                parameters.PianoCustomProgressionPack5, parameters.PianoCustomProgressionPack6,
                parameters.PianoCustomProgressionPack7, parameters.PianoCustomProgressionPack8,
                parameters.PianoCustomProgressionCount, parameters.PianoCustomProgressionSequence,
                parameters.PianoCustomProgressionHash);
        }

        if (first || Changed(previous.OutputPercent, parameters.OutputPercent))
        {
            float outputPercent = Math.Clamp(parameters.OutputPercent, 0f, 100f);

            // La escala anterior multiplicaba por 0,50 y dejaba los bancos de fábrica
            // aproximadamente 13 dB por debajo de un nivel útil. La nueva curva conserva
            // el control 0-100, entrega volumen real desde valores medios y mantiene un
            // techo seguro. En 2.38.3 el limitador final queda fijado a -1 dBFS
            // y la guitarra se atenúa suavemente 5 dB únicamente mientras hay voz.
            _masterGain = Math.Clamp((outputPercent / 100f) * 2.10f, 0f, 1.50f);
        }

        _appliedParameters = parameters;
        _hasAppliedParameters = true;
        _appliedRevision = parameters.Revision;
    }

    private static bool Changed(float left, float right) => MathF.Abs(left - right) > 0.0001f;

    private static float SanitizeInput(float value)
    {
        return float.IsFinite(value) ? Math.Clamp(value, -1.25f, 1.25f) : 0f;
    }

    private float UpdateVoiceDuck(bool speechDetected)
    {
        if (speechDetected)
        {
            _voiceDuckHoldCounter = _voiceDuckHoldSamples;
        }
        else if (_voiceDuckHoldCounter > 0)
        {
            _voiceDuckHoldCounter--;
            speechDetected = true;
        }

        float target = speechDetected ? VoiceDuckTarget : 1f;
        float step = target < _voiceDuckGain ? _voiceDuckAttackStep : _voiceDuckReleaseStep;
        _voiceDuckGain += (target - _voiceDuckGain) * step;
        if (!float.IsFinite(_voiceDuckGain)) _voiceDuckGain = 1f;
        return Math.Clamp(_voiceDuckGain, VoiceDuckTarget, 1f);
    }

    private static float MonitorVoiceDuck(float meetDuckGain)
    {
        // El retorno local sólo recibe una fracción del ducking. Así la guitarra no
        // parece tremolar en auriculares, mientras Meet conserva la separación de 5 dB.
        float amount = (1f - Math.Clamp(meetDuckGain, VoiceDuckTarget, 1f)) * MonitorDuckAmount;
        return Math.Clamp(1f - amount, 0.78f, 1f);
    }

    private static float OutputLimiter(float value)
    {
        if (!float.IsFinite(value))
        {
            return 0f;
        }

        const float threshold = 0.90f;
        float magnitude = MathF.Abs(value);
        if (magnitude <= threshold)
        {
            return value;
        }

        float excess = magnitude - threshold;
        float compressed = threshold + ((1f - threshold) * (excess / ((1f - threshold) + excess)));
        compressed = MathF.Min(compressed, 0.999f);
        return value < 0f ? -compressed : compressed;
    }

    private static float FinalOutputLimiter(float value)
    {
        if (!float.IsFinite(value))
        {
            return 0f;
        }

        const float threshold = 0.84f;
        const float ceiling = 0.8912509f; // -1 dBFS
        float magnitude = MathF.Abs(value);
        if (magnitude <= threshold)
        {
            return value;
        }

        float excess = magnitude - threshold;
        float range = ceiling - threshold;
        float compressed = threshold + (range * (excess / (range + excess)));
        compressed = MathF.Min(compressed, ceiling);
        return value < 0f ? -compressed : compressed;
    }

    /// <summary>
    /// Precalienta silenciosamente las rutas DSP antes de iniciar ASIO.
    /// La finalidad es que la primera activación de un canal o efecto no tenga que
    /// compilar/inicializar código dentro del callback de audio en tiempo real.
    /// </summary>
    public void WarmUp(int blockFrames)
    {
        int frames = Math.Clamp(blockFrames, 64, 512);
        var input = new float[frames];
        var voiceInput = new float[frames];
        var output = new float[frames * 2];
        DspParameters original = Volatile.Read(ref _pendingParameters);

        // Señal pequeña y segura: obliga a recorrer filtros y etapas no lineales sin
        // generar una salida audible porque ASIO todavía no está reproduciendo.
        for (int i = 0; i < frames; i++)
        {
            input[i] = 0.012f * MathF.Sin(2f * MathF.PI * 110f * i / SampleRate);
            voiceInput[i] = 0.008f * MathF.Sin(2f * MathF.PI * 220f * i / SampleRate);
        }

        DspParameters[] profiles =
        {
            original with
            {
                Revision = int.MinValue + 101, SimulationEnabled = true, VoiceEnabled = true, VoiceSuppressorEnabled = true, TunerEnabled = false,
                ExternalIrEnabled = false, GateEnabled = true, CompressorEnabled = true, AutoWahEnabled = true, OverdriveEnabled = true,
                Ts9Enabled = true, Od1Enabled = true, FuzzEnabled = true, Eq5Enabled = true, BoosterEnabled = true, FxLoopEnabled = true, PhaserEnabled = true, FlangerEnabled = true,
                ChorusEnabled = true, DelayEnabled = true, DelayCharacter = DelayCharacter.DigitalClean,
                ReverbEnabled = true, Channel = AmpChannel.CleanTwin, Gain = 4.5f
            },
            original with
            {
                Revision = int.MinValue + 102, SimulationEnabled = true, VoiceEnabled = true, VoiceSuppressorEnabled = true, TunerEnabled = false,
                ExternalIrEnabled = false, GateEnabled = true, CompressorEnabled = true, AutoWahEnabled = true, OverdriveEnabled = true,
                Ts9Enabled = true, Od1Enabled = true, FuzzEnabled = true, Eq5Enabled = true, BoosterEnabled = true, FxLoopEnabled = true, PhaserEnabled = true, FlangerEnabled = true,
                ChorusEnabled = true, DelayEnabled = true, DelayCharacter = DelayCharacter.AnalogDark,
                ReverbEnabled = true, Channel = AmpChannel.CrunchBritish, Gain = 5.0f
            },
            original with
            {
                Revision = int.MinValue + 103, SimulationEnabled = true, VoiceEnabled = true, VoiceSuppressorEnabled = true, TunerEnabled = false,
                ExternalIrEnabled = false, GateEnabled = true, CompressorEnabled = true, AutoWahEnabled = true, OverdriveEnabled = true,
                Ts9Enabled = true, Od1Enabled = true, FuzzEnabled = true, Eq5Enabled = true, BoosterEnabled = true, FxLoopEnabled = true, PhaserEnabled = true, FlangerEnabled = true,
                ChorusEnabled = true, DelayEnabled = true, DelayCharacter = DelayCharacter.DigitalClean,
                ReverbEnabled = true, Channel = AmpChannel.LeadJcm800, Gain = 5.5f
            }
        };

        try
        {
            foreach (DspParameters profile in profiles)
            {
                Volatile.Write(ref _pendingParameters, profile);
                // Varias pasadas favorecen que el runtime termine la compilación escalonada
                // de las rutas calientes antes de que empiece el reloj estricto de ASIO.
                for (int pass = 0; pass < 48; pass++)
                {
                    ProcessMonoToStereo(input, voiceInput, output, frames);
                }
                Reset();
            }

            // Da una pequeña ventana al compilador JIT en segundo plano para publicar
            // las versiones optimizadas de los métodos calentados.
            Thread.Sleep(60);
        }
        finally
        {
            Volatile.Write(ref _pendingParameters, original);
            _hasAppliedParameters = false;
            _appliedRevision = -1;
            ApplyParameters(original);
            Reset();
        }
    }

    public void Reset()
    {
        _noiseGate.Reset();
        _compressor.Reset();
        _autoWah.Reset();
        _ds1Distortion.Reset();
        _divineOverdrive.Reset();
        _od1Overdrive.Reset();
        _fuzz.Reset();
        _eq5.Reset();
        _booster.Reset();
        _inputChorus.Reset();
        _inputAnalogChorus.Reset();
        foreach (AmpModel model in _ampModels) model.Reset();
        Volatile.Read(ref _externalConvolver)?.Reset();
        _effectsLoop.Reset();
        _reverb.Reset();
        _tunerAnalyzer.Reset();
        _tunerTone.Reset();
        _metronome.Reset();
        _drums.Reset();
        _backingBass.Reset();
        _piano.Reset();
        _voiceProcessor.Reset();
        _voiceDuckGain = 1f;
        _voiceDuckHoldCounter = 0;
        _simulationMix = _simulationEnabled ? 1f : 0f;
        _simulationTarget = _simulationMix;
    }

    public void Dispose()
    {
        _namProcessor.Dispose();
        _tunerAnalyzer.Dispose();
    }
}
