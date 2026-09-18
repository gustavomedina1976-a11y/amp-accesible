using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Reverb estéreo de baja latencia con perfiles acústicos claramente diferenciados.
/// La cola húmeda trabaja a media frecuencia para conservar margen DSP en Modo Dos Guitarras.
/// Cada carácter modifica reflexiones tempranas, densidad, predelay, ancho, color y cola.
/// No realiza asignaciones dentro del callback de audio.
/// </summary>
internal sealed class ReverbEffect
{
    private readonly CombFilter[] _leftCombs;
    private readonly CombFilter[] _rightCombs;
    private readonly AllPassFilter[] _leftAllPasses;
    private readonly AllPassFilter[] _rightAllPasses;
    private readonly float[] _preDelayLeft;
    private readonly float[] _preDelayRight;

    private int _preDelayWriteLeft;
    private int _preDelayWriteRight;
    private int _preDelaySamplesLeft;
    private int _preDelaySamplesRight;
    private int _earlyTapSamples1;
    private int _earlyTapSamples2;
    private int _earlyTapSamples3;
    private readonly int _internalRate;
    private readonly int _sampleRate;

    // Al cambiar de carácter, la cola anterior baja durante unos milisegundos,
    // el tanque se limpia y el nuevo carácter entra con un fundido corto.
    // Así no se mezclan dos reverbs ni aparecen clics al recorrer el selector.
    private ReverbTransitionStage _transitionStage;
    private int _transitionSamplesRemaining;
    private int _transitionSamplesTotal;
    private bool _pendingConfiguration;
    private ReverbCharacter _pendingCharacter;
    private float _pendingMixPercent;
    private float _pendingDecayPercent;
    private float _pendingTonePercent;
    private float _pendingPreDelayMs;
    private float _pendingDampingPercent;
    private float _pendingDiffusionPercent;

    private float _diffusionAmount = 0.60f;
    private int _activeCombs = 6;
    private int _allPassPasses = 2;
    private float _earlyReflectionGain = 0.20f;
    private float _wetOutputGain = 1.0f;
    private float _stereoWidth = 1.0f;
    private float _tailSmoothing;
    private float _springAmount;
    private float _plateSheen;
    private float _shimmerAmount;

    private bool _enabled;
    private ReverbCharacter _character = ReverbCharacter.Spring;
    private float _mix;
    private bool _halfRatePhase;
    private float _pendingInputLeft;
    private float _pendingInputRight;
    private float _heldWetLeft;
    private float _heldWetRight;

    // Estados pequeños de coloración. Evitan buffers/objetos adicionales en tiempo real.
    private float _tailSmoothLeft;
    private float _tailSmoothRight;
    private float _previousWetLeft;
    private float _previousWetRight;
    private float _shimmerDc;
    private readonly SpringTank _springTankLeft;
    private readonly SpringTank _springTankRight;

    public ReverbEffect(int sampleRate)
    {
        // La cola húmeda se calcula a media frecuencia. El audio seco y los demás módulos
        // permanecen a la frecuencia ASIO completa.
        _sampleRate = Math.Max(16000, sampleRate);
        int internalRate = Math.Max(8000, _sampleRate / 2);
        _internalRate = internalRate;
        float scale = internalRate / 44100f;

        int[] combLengths = { 1116, 1188, 1277, 1356, 1422, 1491 };
        _leftCombs = combLengths
            .Select(length => new CombFilter(ScaleLength(length, scale)))
            .ToArray();
        _rightCombs = combLengths
            .Select(length => new CombFilter(ScaleLength(length + 23, scale)))
            .ToArray();

        // Spring reutiliza tres buffers comb. Las rutas son excluyentes y el
        // cambio de tipo conserva ResetTank antes de reutilizar estos buffers.
        _springTankLeft = new SpringTank(_internalRate, _leftCombs, 0.992f);
        _springTankRight = new SpringTank(_internalRate, _rightCombs, 1.011f);

        int[] allPassLengths = { 556, 441, 341 };
        _leftAllPasses = allPassLengths
            .Select(length => new AllPassFilter(ScaleLength(length, scale)))
            .ToArray();
        _rightAllPasses = allPassLengths
            .Select(length => new AllPassFilter(ScaleLength(length + 17, scale)))
            .ToArray();

        int maxPreDelayLength = Math.Max(64, (int)(internalRate * 0.180f) + 8);
        _preDelayLeft = new float[maxPreDelayLength];
        _preDelayRight = new float[maxPreDelayLength];

        Configure(false, ReverbCharacter.Spring, 24f, 48f, 55f, 18f, 45f, 60f);
    }

    public void Configure(bool enabled, ReverbCharacter character, float mixPercent, float decayPercent, float tonePercent,
        float preDelayMs, float dampingPercent, float diffusionPercent)
    {
        bool wasEnabled = _enabled;

        if (!enabled)
        {
            CancelTransition();
            ApplyConfiguration(false, character, mixPercent, decayPercent, tonePercent,
                preDelayMs, dampingPercent, diffusionPercent);
            ResetTank();
            return;
        }

        if (!wasEnabled)
        {
            CancelTransition();
            ApplyConfiguration(true, character, mixPercent, decayPercent, tonePercent,
                preDelayMs, dampingPercent, diffusionPercent);
            ResetTank();
            StartFadeIn();
            return;
        }

        if (_character != character)
        {
            StorePendingConfiguration(character, mixPercent, decayPercent, tonePercent,
                preDelayMs, dampingPercent, diffusionPercent);

            // Si la reverb no aporta señal húmeda, no hay cola audible que fundir.
            if (_mix <= 0.0001f || mixPercent <= 0.01f)
            {
                ApplyPendingConfiguration(startWithFadeIn: false);
                return;
            }

            StartFadeOut();
            return;
        }

        // Si el usuario vuelve al carácter actual antes de terminar el fundido,
        // se descarta el cambio pendiente y se recupera suavemente la cola vigente.
        if (_transitionStage == ReverbTransitionStage.FadeOut && _pendingConfiguration)
        {
            _pendingConfiguration = false;
            StartFadeIn();
        }

        ApplyConfiguration(true, character, mixPercent, decayPercent, tonePercent,
            preDelayMs, dampingPercent, diffusionPercent);
    }

    private void StorePendingConfiguration(ReverbCharacter character, float mixPercent, float decayPercent,
        float tonePercent, float preDelayMs, float dampingPercent, float diffusionPercent)
    {
        _pendingCharacter = character;
        _pendingMixPercent = mixPercent;
        _pendingDecayPercent = decayPercent;
        _pendingTonePercent = tonePercent;
        _pendingPreDelayMs = preDelayMs;
        _pendingDampingPercent = dampingPercent;
        _pendingDiffusionPercent = diffusionPercent;
        _pendingConfiguration = true;
    }

    private void StartFadeOut()
    {
        _transitionStage = ReverbTransitionStage.FadeOut;
        _transitionSamplesTotal = Math.Max(32, (int)(_sampleRate * 0.012f));
        _transitionSamplesRemaining = _transitionSamplesTotal;
    }

    private void StartFadeIn()
    {
        _transitionStage = ReverbTransitionStage.FadeIn;
        _transitionSamplesTotal = Math.Max(32, (int)(_sampleRate * 0.018f));
        _transitionSamplesRemaining = _transitionSamplesTotal;
    }

    private void CancelTransition()
    {
        _transitionStage = ReverbTransitionStage.None;
        _transitionSamplesRemaining = 0;
        _transitionSamplesTotal = 0;
        _pendingConfiguration = false;
    }

    private void ApplyPendingConfiguration(bool startWithFadeIn = true)
    {
        if (!_pendingConfiguration)
        {
            CancelTransition();
            return;
        }

        ReverbCharacter character = _pendingCharacter;
        float mixPercent = _pendingMixPercent;
        float decayPercent = _pendingDecayPercent;
        float tonePercent = _pendingTonePercent;
        float preDelayMs = _pendingPreDelayMs;
        float dampingPercent = _pendingDampingPercent;
        float diffusionPercent = _pendingDiffusionPercent;
        _pendingConfiguration = false;

        ApplyConfiguration(true, character, mixPercent, decayPercent, tonePercent,
            preDelayMs, dampingPercent, diffusionPercent);
        ResetTank();

        if (startWithFadeIn) StartFadeIn();
        else
        {
            _transitionStage = ReverbTransitionStage.None;
            _transitionSamplesRemaining = 0;
            _transitionSamplesTotal = 0;
        }
    }

    private void ApplyConfiguration(bool enabled, ReverbCharacter character, float mixPercent, float decayPercent, float tonePercent,
        float preDelayMs, float dampingPercent, float diffusionPercent)
    {
        _enabled = enabled;
        _character = character;
        _mix = Math.Clamp(mixPercent / 100f, 0f, 0.85f);

        float decay = Math.Clamp(decayPercent / 100f, 0f, 1f);
        float tone = Math.Clamp(tonePercent / 100f, 0f, 1f);
        float userDamping = Math.Clamp(dampingPercent / 100f, 0f, 1f);
        float userDiffusion = Math.Clamp(diffusionPercent / 100f, 0.05f, 1f);

        float feedbackBase;
        float feedbackRange;
        float damping;
        float effectivePreDelay;
        float earlyTap1Ms;
        float earlyTap2Ms;
        float earlyTap3Ms;

        // Perfiles deliberadamente separados. Antes de 2.41.10 todos compartían casi
        // el mismo tanque y sólo variaban algunos coeficientes, por eso se parecían demasiado.
        switch (character)
        {
            case ReverbCharacter.Room:
                feedbackBase = 0.46f; feedbackRange = 0.22f;
                damping = 0.56f - tone * 0.28f + userDamping * 0.10f;
                _diffusionAmount = Math.Clamp(userDiffusion * 0.52f, 0.20f, 0.55f);
                _activeCombs = 4; _allPassPasses = 1;
                _earlyReflectionGain = 0.72f; _wetOutputGain = 0.86f; _stereoWidth = 0.72f;
                _tailSmoothing = 0.10f; _springAmount = 0f; _plateSheen = 0f; _shimmerAmount = 0f;
                effectivePreDelay = Math.Clamp(preDelayMs * 0.35f, 3f, 16f);
                earlyTap1Ms = 4f; earlyTap2Ms = 9f; earlyTap3Ms = 15f;
                break;

            case ReverbCharacter.Plate:
                feedbackBase = 0.70f; feedbackRange = 0.18f;
                damping = 0.28f - tone * 0.15f + userDamping * 0.08f;
                _diffusionAmount = MathF.Max(userDiffusion, 0.82f);
                _activeCombs = 6; _allPassPasses = 3;
                _earlyReflectionGain = 0.08f; _wetOutputGain = 1.14f; _stereoWidth = 1.04f;
                _tailSmoothing = 0.03f; _springAmount = 0f; _plateSheen = 0.23f; _shimmerAmount = 0f;
                effectivePreDelay = Math.Clamp(preDelayMs * 0.80f, 10f, 35f);
                earlyTap1Ms = 12f; earlyTap2Ms = 20f; earlyTap3Ms = 31f;
                break;

            case ReverbCharacter.Hall:
                feedbackBase = 0.76f; feedbackRange = 0.18f;
                damping = 0.46f - tone * 0.22f + userDamping * 0.16f;
                _diffusionAmount = MathF.Max(userDiffusion, 0.74f);
                _activeCombs = 6; _allPassPasses = 2;
                _earlyReflectionGain = 0.22f; _wetOutputGain = 1.08f; _stereoWidth = 1.12f;
                _tailSmoothing = 0.18f; _springAmount = 0f; _plateSheen = 0f; _shimmerAmount = 0f;
                effectivePreDelay = Math.Clamp(preDelayMs * 1.50f, 24f, 65f);
                earlyTap1Ms = 16f; earlyTap2Ms = 29f; earlyTap3Ms = 43f;
                break;

            case ReverbCharacter.Church:
                // Iglesia: la nota directa queda separada de una cola oscura y solemne.
                // El predelay y las reflexiones más tardías la distinguen del Hall.
                feedbackBase = 0.855f; feedbackRange = 0.115f;
                damping = 0.72f - tone * 0.12f + userDamping * 0.15f;
                _diffusionAmount = MathF.Max(userDiffusion, 0.90f);
                _activeCombs = 6; _allPassPasses = 3;
                _earlyReflectionGain = 0.12f; _wetOutputGain = 1.12f; _stereoWidth = 1.14f;
                _tailSmoothing = 0.34f; _springAmount = 0f; _plateSheen = 0f; _shimmerAmount = 0f;
                effectivePreDelay = Math.Clamp(preDelayMs * 3.20f, 58f, 122f);
                earlyTap1Ms = 31f; earlyTap2Ms = 55f; earlyTap3Ms = 84f;
                break;

            case ReverbCharacter.Shimmer:
                // Shimmer: la excitación tipo octava y el brillo de la cola se elevan
                // lo suficiente para reconocerse incluso con mezcla moderada.
                feedbackBase = 0.83f; feedbackRange = 0.14f;
                damping = 0.20f - tone * 0.10f + userDamping * 0.06f;
                _diffusionAmount = MathF.Max(userDiffusion, 0.88f);
                _activeCombs = 6; _allPassPasses = 3;
                _earlyReflectionGain = 0.08f; _wetOutputGain = 1.18f; _stereoWidth = 1.38f;
                _tailSmoothing = 0.04f; _springAmount = 0f; _plateSheen = 0.18f; _shimmerAmount = 0.62f;
                effectivePreDelay = Math.Clamp(preDelayMs * 2.20f, 35f, 88f);
                earlyTap1Ms = 18f; earlyTap2Ms = 34f; earlyTap3Ms = 52f;
                break;

            case ReverbCharacter.Cathedral:
                // Catedral: máxima profundidad, difusión y apertura estéreo. Se mantiene
                // menos amortiguada que Church para que la enorme cola no se confunda con ella.
                feedbackBase = 0.915f; feedbackRange = 0.065f;
                damping = 0.59f - tone * 0.14f + userDamping * 0.18f;
                _diffusionAmount = MathF.Max(userDiffusion, 0.98f);
                _activeCombs = 6; _allPassPasses = 3;
                _earlyReflectionGain = 0.06f; _wetOutputGain = 1.24f; _stereoWidth = 1.55f;
                _tailSmoothing = 0.44f; _springAmount = 0f; _plateSheen = 0f; _shimmerAmount = 0f;
                effectivePreDelay = Math.Clamp(preDelayMs * 4.50f, 82f, 165f);
                earlyTap1Ms = 42f; earlyTap2Ms = 76f; earlyTap3Ms = 116f;
                break;

            default: // Spring
                _springTankLeft.Configure(decay, tone, userDamping, userDiffusion);
                _springTankRight.Configure(decay, tone, userDamping, userDiffusion);
                // 2.41.83: referencia real del tanque del Triple Channel. El primer
                // rebote llega casi inmediato, la cola queda menos difusa y más metálica.
                feedbackBase = 0.64f; feedbackRange = 0.20f;
                damping = 0.45f - tone * 0.20f + userDamping * 0.08f;
                _diffusionAmount = Math.Clamp(userDiffusion * 0.30f, 0.10f, 0.34f);
                _activeCombs = 5; _allPassPasses = 1;
                _earlyReflectionGain = 0.34f; _wetOutputGain = 1.02f; _stereoWidth = 0.46f;
                _tailSmoothing = 0.025f; _springAmount = 0.86f; _plateSheen = 0f; _shimmerAmount = 0f;
                effectivePreDelay = Math.Clamp(preDelayMs * 0.10f, 0.5f, 4f);
                earlyTap1Ms = 7f; earlyTap2Ms = 16f; earlyTap3Ms = 31f;
                break;
        }

        float feedback = Math.Clamp(feedbackBase + decay * feedbackRange, 0f, 0.982f);
        damping = Math.Clamp(damping, 0.05f, 0.82f);

        foreach (CombFilter comb in _leftCombs) comb.Configure(feedback, damping);
        foreach (CombFilter comb in _rightCombs) comb.Configure(feedback, damping);

        if (preDelayMs <= 0.1f)
        {
            effectivePreDelay = character switch
            {
                ReverbCharacter.Room => 6f,
                ReverbCharacter.Plate => 14f,
                ReverbCharacter.Hall => 28f,
                ReverbCharacter.Church => 64f,
                ReverbCharacter.Shimmer => 42f,
                ReverbCharacter.Cathedral => 88f,
                _ => 1f
            };
        }

        _preDelaySamplesLeft = ToDelaySamples(effectivePreDelay, _preDelayLeft.Length);
        _preDelaySamplesRight = Math.Clamp(_preDelaySamplesLeft + Math.Max(1, _internalRate / 850), 1, _preDelayRight.Length - 2);
        _earlyTapSamples1 = ToDelaySamples(earlyTap1Ms, _preDelayLeft.Length);
        _earlyTapSamples2 = ToDelaySamples(earlyTap2Ms, _preDelayLeft.Length);
        _earlyTapSamples3 = ToDelaySamples(earlyTap3Ms, _preDelayLeft.Length);

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

        if (!_halfRatePhase)
        {
            _pendingInputLeft = inputLeft;
            _pendingInputRight = inputRight;
            _halfRatePhase = true;
            float heldTransitionWetLeft = _heldWetLeft;
            float heldTransitionWetRight = _heldWetRight;
            ApplyTransitionGain(ref heldTransitionWetLeft, ref heldTransitionWetRight);
            outputLeft = Sanitize(inputLeft + (heldTransitionWetLeft * _mix));
            outputRight = Sanitize(inputRight + (heldTransitionWetRight * _mix));
            return;
        }

        _halfRatePhase = false;
        float downLeft = (_pendingInputLeft + inputLeft) * 0.5f;
        float downRight = (_pendingInputRight + inputRight) * 0.5f;

        float preLeft = ProcessPreDelay(_preDelayLeft, downLeft, ref _preDelayWriteLeft, _preDelaySamplesLeft);
        float preRight = ProcessPreDelay(_preDelayRight, downRight, ref _preDelayWriteRight, _preDelaySamplesRight);

        // Tres reflexiones tempranas con relaciones y polaridades distintas por canal.
        float earlyLeft =
            ReadDelayTap(_preDelayLeft, _preDelayWriteLeft, _earlyTapSamples1) * 0.52f +
            ReadDelayTap(_preDelayLeft, _preDelayWriteLeft, _earlyTapSamples2) * 0.30f +
            ReadDelayTap(_preDelayLeft, _preDelayWriteLeft, _earlyTapSamples3) * 0.18f;
        float earlyRight =
            ReadDelayTap(_preDelayRight, _preDelayWriteRight, _earlyTapSamples1 + 2) * 0.50f -
            ReadDelayTap(_preDelayRight, _preDelayWriteRight, _earlyTapSamples2 + 3) * 0.28f +
            ReadDelayTap(_preDelayRight, _preDelayWriteRight, _earlyTapSamples3 + 5) * 0.20f;

        float wetLeft = 0f;
        float wetRight = 0f;
        if (_springAmount > 0.001f)
        {
            wetLeft = _springTankLeft.Process(preLeft, earlyLeft);
            wetRight = _springTankRight.Process(preRight, earlyRight);
        }
        else
        {
            float excitationGain = _character switch
            {
                ReverbCharacter.Room => 0.20f,
                ReverbCharacter.Spring => 0.30f,
                ReverbCharacter.Plate => 0.34f,
                ReverbCharacter.Hall => 0.38f,
                ReverbCharacter.Church => 0.41f,
                ReverbCharacter.Shimmer => 0.42f,
                ReverbCharacter.Cathedral => 0.46f,
                _ => 0.34f
            };

            float monoPre = (preLeft + preRight) * 0.5f;
            float monoExcitation = monoPre * (excitationGain * 2f);

            // Shimmer económico: la rectificación de onda completa genera una componente de
            // octava; se elimina su continua con un seguidor lento antes de excitar la cola.
            if (_shimmerAmount > 0.001f)
            {
                float rectified = MathF.Abs(monoPre);
                _shimmerDc += (rectified - _shimmerDc) * 0.008f;
                float octaveLike = rectified - _shimmerDc;
                monoExcitation += octaveLike * _shimmerAmount;
            }

            int combCount = Math.Clamp(_activeCombs, 1, _leftCombs.Length);
            for (int index = 0; index < combCount; index++)
            {
                float alternating = (index & 1) == 0 ? monoExcitation : -monoExcitation;
                float spread = 0.24f + (_diffusionAmount * 0.38f);
                float direct = _character switch
                {
                    ReverbCharacter.Spring => 0.56f,
                    ReverbCharacter.Room => 0.44f,
                    ReverbCharacter.Plate => 0.20f,
                    _ => 0.28f
                };
                wetLeft += _leftCombs[index].Process((preLeft * direct) + (alternating * spread));
                wetRight += _rightCombs[index].Process((preRight * direct) - (alternating * spread));
            }

            float wetScale = _character switch
            {
                ReverbCharacter.Room => 0.16f,
                ReverbCharacter.Spring => 0.18f,
                ReverbCharacter.Plate => 0.18f,
                ReverbCharacter.Hall => 0.19f,
                ReverbCharacter.Church => 0.20f,
                ReverbCharacter.Shimmer => 0.205f,
                ReverbCharacter.Cathedral => 0.22f,
                _ => 0.18f
            };
            wetLeft *= wetScale;
            wetRight *= wetScale;

            // Room se reconoce especialmente por las primeras reflexiones; en los ambientes
            // grandes éstas quedan más atrás respecto de la cola difusa.
            wetLeft += earlyLeft * _earlyReflectionGain;
            wetRight += earlyRight * _earlyReflectionGain;

            int passes = Math.Clamp(_allPassPasses, 1, _leftAllPasses.Length);
            for (int i = 0; i < passes; i++)
            {
                wetLeft = _leftAllPasses[i].Process(wetLeft);
                wetRight = _rightAllPasses[i].Process(wetRight);
            }

        }

        if (_plateSheen > 0.001f)
        {
            float edgeLeft = wetLeft - _previousWetLeft;
            float edgeRight = wetRight - _previousWetRight;
            _previousWetLeft = wetLeft;
            _previousWetRight = wetRight;
            wetLeft += edgeLeft * _plateSheen;
            wetRight += edgeRight * _plateSheen;
        }
        else
        {
            _previousWetLeft = wetLeft;
            _previousWetRight = wetRight;
        }

        if (_tailSmoothing > 0.001f)
        {
            _tailSmoothLeft = (wetLeft * (1f - _tailSmoothing)) + (_tailSmoothLeft * _tailSmoothing);
            _tailSmoothRight = (wetRight * (1f - _tailSmoothing)) + (_tailSmoothRight * _tailSmoothing);
            wetLeft = _tailSmoothLeft;
            wetRight = _tailSmoothRight;
        }
        else
        {
            _tailSmoothLeft = wetLeft;
            _tailSmoothRight = wetRight;
        }

        // Ancho Mid/Side. Spring y Room permanecen más centrados; los ambientes grandes
        // y Shimmer envuelven más sin alterar la señal seca.
        float mid = (wetLeft + wetRight) * 0.5f;
        float side = (wetLeft - wetRight) * 0.5f * _stereoWidth;
        wetLeft = (mid + side) * _wetOutputGain;
        wetRight = (mid - side) * _wetOutputGain;

        if (!float.IsFinite(wetLeft) || !float.IsFinite(wetRight))
        {
            Reset();
            wetLeft = 0f;
            wetRight = 0f;
        }

        _heldWetLeft = Sanitize(wetLeft);
        _heldWetRight = Sanitize(wetRight);
        float transitionWetLeft = _heldWetLeft;
        float transitionWetRight = _heldWetRight;
        ApplyTransitionGain(ref transitionWetLeft, ref transitionWetRight);
        outputLeft = Sanitize(inputLeft + (transitionWetLeft * _mix));
        outputRight = Sanitize(inputRight + (transitionWetRight * _mix));
    }

    private void ApplyTransitionGain(ref float wetLeft, ref float wetRight)
    {
        if (_transitionStage == ReverbTransitionStage.None || _transitionSamplesTotal <= 0) return;

        if (_transitionStage == ReverbTransitionStage.FadeOut)
        {
            float remaining = Math.Clamp(_transitionSamplesRemaining / (float)_transitionSamplesTotal, 0f, 1f);
            float gain = remaining * remaining * (3f - (2f * remaining));
            wetLeft *= gain;
            wetRight *= gain;

            _transitionSamplesRemaining--;
            if (_transitionSamplesRemaining <= 0)
            {
                wetLeft = 0f;
                wetRight = 0f;
                ApplyPendingConfiguration();
            }
            return;
        }

        float progress = 1f - Math.Clamp(_transitionSamplesRemaining / (float)_transitionSamplesTotal, 0f, 1f);
        float fadeInGain = progress * progress * (3f - (2f * progress));
        wetLeft *= fadeInGain;
        wetRight *= fadeInGain;

        _transitionSamplesRemaining--;
        if (_transitionSamplesRemaining <= 0)
        {
            _transitionStage = ReverbTransitionStage.None;
            _transitionSamplesRemaining = 0;
            _transitionSamplesTotal = 0;
        }
    }

    public void Reset()
    {
        CancelTransition();
        ResetTank();
    }

    private void ResetTank()
    {
        foreach (CombFilter comb in _leftCombs) comb.Reset();
        foreach (CombFilter comb in _rightCombs) comb.Reset();
        foreach (AllPassFilter allPass in _leftAllPasses) allPass.Reset();
        foreach (AllPassFilter allPass in _rightAllPasses) allPass.Reset();

        Array.Clear(_preDelayLeft);
        Array.Clear(_preDelayRight);
        _preDelayWriteLeft = 0;
        _preDelayWriteRight = 0;
        _halfRatePhase = false;
        _pendingInputLeft = 0f;
        _pendingInputRight = 0f;
        _heldWetLeft = 0f;
        _heldWetRight = 0f;
        _tailSmoothLeft = 0f;
        _tailSmoothRight = 0f;
        _previousWetLeft = 0f;
        _previousWetRight = 0f;
        _shimmerDc = 0f;
        _springTankLeft.Reset();
        _springTankRight.Reset();
    }

    private int ToDelaySamples(float delayMs, int bufferLength) =>
        Math.Clamp((int)(_internalRate * MathF.Max(0.1f, delayMs) / 1000f), 1, bufferLength - 2);

    private static float ProcessPreDelay(float[] buffer, float input, ref int writeIndex, int delaySamples)
    {
        int readIndex = writeIndex - Math.Clamp(delaySamples, 1, buffer.Length - 2);
        while (readIndex < 0) readIndex += buffer.Length;
        float output = FastDspMath.FlushDenormal(buffer[readIndex]);
        buffer[writeIndex] = FastDspMath.FlushDenormal(input);
        writeIndex++;
        if (writeIndex >= buffer.Length) writeIndex = 0;
        return output;
    }

    private static float ReadDelayTap(float[] buffer, int writeIndex, int delaySamples)
    {
        int readIndex = writeIndex - Math.Clamp(delaySamples, 1, buffer.Length - 2);
        while (readIndex < 0) readIndex += buffer.Length;
        while (readIndex >= buffer.Length) readIndex -= buffer.Length;
        return FastDspMath.FlushDenormal(buffer[readIndex]);
    }

    // 2.41.73: dispersion en tres recorridos inarmonicos, sin tonos sinteticos.
    // Solo se asignan estados pequenos en el constructor; se reutilizan los delays.
    private sealed class SpringTank
    {
        private readonly SpringPath[] _paths;
        private readonly float[] _earlyPhase = new float[6];
        private readonly float _highPass, _fastAttack, _fastRelease, _slowAttack, _slowRelease;
        private readonly int _cooldownLength;
        private float _inputLow, _earlyLow, _fast, _slow, _attack;
        private int _cooldown;
        private uint _strike = 0x6d2b79f5;
        private float _phaseVariation;

        public SpringTank(int rate, CombFilter[] combs, float tuning)
        {
            _highPass = Coefficient(135f, rate);
            _fastAttack = TimeCoefficient(0.0013f, rate);
            _fastRelease = TimeCoefficient(0.030f, rate);
            _slowAttack = TimeCoefficient(0.022f, rate);
            _slowRelease = TimeCoefficient(0.145f, rate);
            _cooldownLength = Math.Max(1, (int)(rate * 0.060f));
            _paths = new[] {
                // Tres recorridos deliberadamente desiguales, como un tanque de tres resortes.
                new SpringPath(rate, combs[0].SpringBuffer, 0.54f * tuning, 0.90f),
                new SpringPath(rate, combs[2].SpringBuffer, 0.68f * tuning, 1.03f),
                new SpringPath(rate, combs[5].SpringBuffer, 0.80f * tuning, 0.82f) };
        }

        public void Configure(float decay, float tone, float damping, float diffusion)
        {
            foreach (var path in _paths) path.Configure(decay, tone, damping, diffusion);
        }

        public float Process(float input, float early)
        {
            _inputLow = FastDspMath.FlushDenormal(_inputLow + (input - _inputLow) * _highPass);
            float body = input - _inputLow;
            float level = MathF.Abs(body);
            _fast += (level - _fast) * (level > _fast ? _fastAttack : _fastRelease);
            _slow += (level - _slow) * (level > _slow ? _slowAttack : _slowRelease);
            float onset = Math.Clamp((_fast - _slow * 1.18f - 0.001f) * 8f, 0f, 1f);
            _attack = FastDspMath.FlushDenormal(_attack + (onset - _attack) * _fastRelease);
            if (_cooldown > 0) _cooldown--;
            if (onset > 0.08f && _cooldown == 0)
            {
                // Variacion minima por ataque; sin LFO ni cambios de longitud.
                _strike ^= _strike << 13; _strike ^= _strike >> 17; _strike ^= _strike << 5;
                _phaseVariation = ((_strike & 1023) / 1023f - 0.5f) * 0.008f;
                _cooldown = _cooldownLength;
            }
            float drive = FastDspMath.SoftClip(body * (0.52f + _attack * 0.28f));
            float tail = _paths[0].Process(drive, _phaseVariation * _attack) * 1.04f
                - _paths[1].Process(drive, -_phaseVariation * _attack) * 0.76f
                + _paths[2].Process(drive, _phaseVariation * _attack) * 0.56f;

            _earlyLow = FastDspMath.FlushDenormal(_earlyLow + (early - _earlyLow) * _highPass);
            float reflection = early - _earlyLow;
            for (int i = 0; i < _earlyPhase.Length; i++)
            {
                float output = _earlyPhase[i] - 0.63f * reflection;
                _earlyPhase[i] = FastDspMath.FlushDenormal(reflection + 0.63f * output);
                reflection = output;
            }
            // El primer retorno se dispersa para evitar un eco slapback separado.
            return tail * 0.58f + reflection * (0.62f + _attack * 0.16f);
        }

        public void Reset()
        {
            _inputLow = _earlyLow = _fast = _slow = _attack = _phaseVariation = 0f;
            _cooldown = 0;
            _strike = 0x6d2b79f5;
            Array.Clear(_earlyPhase);
            foreach (var path in _paths) path.Reset();
        }

        private static float Coefficient(float hz, int rate) => 1f - MathF.Exp(-2f * MathF.PI * hz / rate);
        private static float TimeCoefficient(float seconds, int rate) => 1f - MathF.Exp(-1f / (seconds * rate));
    }

    private sealed class SpringPath
    {
        private readonly float[] _delay, _dispersion;
        private readonly float _dispersionBase, _lossScale;
        private readonly int _rate;
        private int _index;
        private float _feedback, _lowPass, _loss, _dispersionCoefficient;

        public SpringPath(int rate, float[] delay, float dispersion, float lossScale)
        {
            _rate = rate;
            _delay = delay;
            _dispersion = new float[Math.Clamp((int)MathF.Round(24f * rate / 24000f), 4, 48)];
            _dispersionBase = dispersion;
            _lossScale = lossScale;
        }

        public void Configure(float decay, float tone, float damping, float diffusion)
        {
            // La toma real del Mi grave conserva cola de resorte durante alrededor de
            // 3 a 4 segundos con el ajuste medio. Decay 48 % queda cerca de 3,5 s.
            float decaySeconds = 1.00f + decay * 5.20f;
            _feedback = MathF.Pow(0.001f, _delay.Length / (_rate * decaySeconds));
            float cutoff = (2100f + tone * 3200f - damping * 1200f) * _lossScale;
            _loss = 1f - MathF.Exp(-2f * MathF.PI * cutoff / _rate);
            _dispersionCoefficient = _dispersionBase + (diffusion - 0.5f) * 0.045f;
        }

        public float Process(float input, float variation)
        {
            float dispersed = _delay[_index];
            float coefficient = _dispersionCoefficient + variation;
            for (int i = 0; i < _dispersion.Length; i++)
            {
                float output = _dispersion[i] - coefficient * dispersed;
                _dispersion[i] = FastDspMath.FlushDenormal(dispersed + coefficient * output);
                dispersed = output;
            }
            // Los agudos pierden energia en cada vuelta, los medios conservan cuerpo.
            _lowPass = FastDspMath.FlushDenormal(_lowPass + (dispersed - _lowPass) * _loss);
            _delay[_index] = FastDspMath.FlushDenormal(input + _lowPass * _feedback);
            if (++_index == _delay.Length) _index = 0;
            return _lowPass;
        }

        public void Reset()
        {
            // ResetTank ya limpia el buffer compartido con CombFilter.
            Array.Clear(_dispersion);
            _index = 0;
            _lowPass = 0f;
        }
    }

    private static int ScaleLength(int length, float scale) =>
        Math.Max(8, (int)MathF.Round(length * scale));

    private static float Sanitize(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, -2f, 2f) : 0f;

    private enum ReverbTransitionStage
    {
        None,
        FadeOut,
        FadeIn
    }

    private sealed class CombFilter
    {
        private readonly float[] _buffer;
        private int _index;
        private float _feedback;
        private float _damping;
        private float _filterState;

        public float[] SpringBuffer => _buffer;

        public CombFilter(int length)
        {
            _buffer = new float[length];
        }

        public void Configure(float feedback, float damping)
        {
            _feedback = Math.Clamp(feedback, 0f, 0.982f);
            _damping = Math.Clamp(damping, 0.05f, 0.80f);
        }

        public float Process(float input)
        {
            float delayed = FastDspMath.FlushDenormal(_buffer[_index]);
            _filterState = FastDspMath.FlushDenormal(
                (delayed * (1f - _damping)) + (_filterState * _damping));

            float next = FastDspMath.FlushDenormal(input + (_filterState * _feedback));
            _buffer[_index] = FastDspMath.SoftClip(next * 0.92f);

            _index++;
            if (_index >= _buffer.Length) _index = 0;
            return FastDspMath.FlushDenormal(delayed);
        }

        public void Reset()
        {
            Array.Clear(_buffer);
            _index = 0;
            _filterState = 0f;
        }
    }

    private sealed class AllPassFilter
    {
        private readonly float[] _buffer;
        private int _index;

        public AllPassFilter(int length)
        {
            _buffer = new float[length];
        }

        public float Process(float input)
        {
            const float feedback = 0.52f;
            float buffered = FastDspMath.FlushDenormal(_buffer[_index]);
            float output = FastDspMath.FlushDenormal(buffered - input);
            _buffer[_index] = FastDspMath.FlushDenormal(input + (buffered * feedback));

            _index++;
            if (_index >= _buffer.Length) _index = 0;
            return FastDspMath.FlushDenormal(output);
        }

        public void Reset()
        {
            Array.Clear(_buffer);
            _index = 0;
        }
    }
}
