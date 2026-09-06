using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Octavador monofónico orientado a guitarra.
/// La octava arriba usa rectificación con bloqueo de continua.
/// La octava abajo usa división por dos con seguimiento filtrado e histéresis,
/// para evitar dobles disparos y mejorar la estabilidad en notas sostenidas.
/// </summary>
internal sealed class OctaverEffect
{
    private bool _enabled;
    private OctaverCharacter _character;
    private float _dry, _down, _up, _tone, _level;

    private float _previousInput;
    private float _trackingState;
    private bool _subArmed;
    private float _subPolarity = 1f;
    private float _envelope;
    private float _upDcInput;
    private float _upDcOutput;
    private float _subSmooth;
    private float _subSmooth2;
    private float _toneState;
    private int _samplesSinceSubToggle;

    // 2.38.7: estados exclusivos del modo Organ. El objetivo es que F25 deje de
    // sonar como guitarra + octavas: se suaviza el ataque de púa, se comprime la
    // envolvente y se forman armónicos tipo drawbar antes del Leslie.
    private float _organEnvelope;
    private float _organKeyEnvelope;
    private float _organGain = 1f;
    private float _organBodyState;
    private float _organBodyState2;
    private float _organUpperState;
    private float _organSubState;
    private float _organHarmonicDcInput;
    private float _organHarmonicDcOutput;

    public OctaverEffect(int sampleRate)
    {
        SampleRate = Math.Max(8000, sampleRate);
    }

    private int SampleRate { get; }

    public void Configure(bool enabled, OctaverCharacter character, float dryPercent, float downPercent,
        float upPercent, float tonePercent, float levelPercent)
    {
        bool changedMode = _enabled != enabled || _character != character;

        _enabled = enabled;
        _character = character;
        _dry = Math.Clamp(dryPercent / 100f, 0f, 1f);
        _down = Math.Clamp(downPercent / 100f, 0f, 1f);
        _up = Math.Clamp(upPercent / 100f, 0f, 1f);
        _tone = Math.Clamp(tonePercent / 100f, 0f, 1f);
        _level = Math.Clamp(levelPercent / 100f, 0f, 1.25f);

        if (changedMode)
        {
            Reset();
        }
    }

    public float Process(float input)
    {
        if (!_enabled) return input;
        if (!float.IsFinite(input)) input = 0f;

        // Envolvente rápida al ataque y más lenta al release.
        float magnitude = MathF.Abs(input);
        float envCoeff = magnitude > _envelope ? 0.13f : 0.008f;
        _envelope += (magnitude - _envelope) * envCoeff;

        // Seguimiento grave: se filtra la entrada antes de detectar cruces.
        // La histéresis evita que armónicos y ruido hagan conmutar varias veces
        // dentro del mismo ciclo, que era la causa del suboctavador poco audible.
        float trackingCoeff = 0.075f + (_tone * 0.055f);
        _trackingState += (input - _trackingState) * trackingCoeff;

        float threshold = Math.Clamp(_envelope * 0.055f, 0.0015f, 0.018f);
        _samplesSinceSubToggle++;
        int minToggleSamples = Math.Max(8, (int)(SampleRate * 0.00070f));
        bool trackingReliable = _envelope > 0.0022f;
        if (trackingReliable && _trackingState < -threshold)
        {
            _subArmed = true;
        }
        else if (trackingReliable && _subArmed && _trackingState > threshold && _samplesSinceSubToggle >= minToggleSamples)
        {
            _subPolarity = -_subPolarity;
            _subArmed = false;
            _samplesSinceSubToggle = 0;
        }

        _previousInput = input;

        float rawSub = _subPolarity * _envelope * 1.72f;
        float subCoeff1 = 0.055f + (_tone * 0.085f);
        float subCoeff2 = 0.085f + (_tone * 0.105f);
        _subSmooth += (rawSub - _subSmooth) * subCoeff1;
        _subSmooth2 += (_subSmooth - _subSmooth2) * subCoeff2;
        float octaveDown = FastDspMath.SoftClip(((_subSmooth * 0.42f) + (_subSmooth2 * 1.18f)) * 1.18f);

        // Octava arriba clásica: rectificación completa duplica la periodicidad.
        float rectified = MathF.Abs(input) * 1.85f;
        float dc = rectified - _upDcInput + (0.992f * _upDcOutput);
        _upDcInput = rectified;
        _upDcOutput = dc;
        float octaveUp = FastDspMath.SoftClip(dc * (1.15f + _tone * 0.45f));

        // F25 usa exclusivamente el modo Organ. Desde 2.38.7 ya no mezcla
        // simplemente guitarra seca + octavas: genera un cuerpo sostenido tipo
        // drawbar y entrega al Leslie una señal con mucho menos ataque de púa.
        if (_character == OctaverCharacter.Organ)
        {
            return ProcessOrganVoice(input, octaveDown, octaveUp);
        }

        float wet = _character switch
        {
            OctaverCharacter.Down => octaveDown * _down,
            OctaverCharacter.Up => octaveUp * _up,
            OctaverCharacter.Dual => (octaveDown * _down * 0.82f) + (octaveUp * _up * 0.82f),
            OctaverCharacter.Sub => octaveDown * _down * 1.18f,
            _ => 0f
        };

        float result = ((input * _dry) + wet) * _level;
        return FastDspMath.SoftClip(result);
    }


    private float ProcessOrganVoice(float input, float octaveDown, float octaveUp)
    {
        // Envolvente de nota: ataque moderado y release largo. Esto conserva
        // acordes y notas ligadas, pero borra buena parte del golpe de púa.
        float magnitude = MathF.Abs(input);
        float envRate = magnitude > _organEnvelope ? 0.020f : 0.00032f;
        _organEnvelope += (magnitude - _organEnvelope) * envRate;

        // Envolvente de tecla algo más lenta. Se usa sólo sobre la parte húmeda
        // para que el comienzo recuerde más a una tecla/rueda tonal que a una cuerda.
        float keyTarget = Math.Clamp(_organEnvelope * 10.5f, 0f, 1f);
        float keyRate = keyTarget > _organKeyEnvelope ? 0.0036f : 0.00024f;
        _organKeyEnvelope += (keyTarget - _organKeyEnvelope) * keyRate;

        // Compresión/sustain interna. A medida que la cuerda decae se recupera
        // ganancia de forma lenta, evitando que el órgano se apague de inmediato.
        float desiredGain = Math.Clamp(0.115f / MathF.Max(0.018f, _organEnvelope), 0.78f, 3.20f);
        float gainRate = desiredGain < _organGain ? 0.010f : 0.00085f;
        _organGain += (desiredGain - _organGain) * gainRate;

        float driven = FastDspMath.SoftClip(input * _organGain * 1.52f);

        // Dos polos simples quitan el click/transitorio de púa sin volver opaco
        // el registro. Tone desplaza suavemente el brillo de los drawbars.
        float bodyCoeff = 0.115f + (_tone * 0.095f);
        _organBodyState += (driven - _organBodyState) * bodyCoeff;
        _organBodyState2 += (_organBodyState - _organBodyState2) * (bodyCoeff * 0.72f);
        float body = _organBodyState2;

        // Octava superior e inferior suavizadas. En lugar de dominarlas, se usan
        // como registros 4' y 16' alrededor de un cuerpo 8' predominante.
        _organUpperState += (octaveUp - _organUpperState) * (0.10f + _tone * 0.08f);
        _organSubState += (octaveDown - _organSubState) * (0.055f + _tone * 0.045f);

        // Armónico impar suave, equivalente a abrir parcialmente otro drawbar.
        // Se elimina la continua para que no genere golpeteos al pasar por Leslie.
        float harmonicRaw = FastDspMath.SoftClip(body * 2.35f) - (body * 0.70f);
        float harmonic = harmonicRaw - _organHarmonicDcInput + (0.995f * _organHarmonicDcOutput);
        _organHarmonicDcInput = harmonicRaw;
        _organHarmonicDcOutput = harmonic;

        float fundamental = body * (0.82f + (_dry * 0.18f));
        float drawbar16 = _organSubState * _down * 0.34f;
        float drawbar4 = _organUpperState * _up * 0.40f;
        float drawbarHarmonic = harmonic * (0.11f + _tone * 0.09f);

        float organ = (fundamental + drawbar16 + drawbar4 + drawbarHarmonic) * _organKeyEnvelope;

        // Apenas una sombra de señal directa mantiene definición de acordes,
        // pero ya no deja que la guitarra seca sea la protagonista.
        float direct = input * _dry * 0.10f;
        float result = (direct + organ * 1.14f) * _level;
        return FastDspMath.SoftClip(result);
    }

    public void Reset()
    {
        _previousInput = 0f;
        _trackingState = 0f;
        _subArmed = false;
        _subPolarity = 1f;
        _envelope = 0f;
        _upDcInput = 0f;
        _upDcOutput = 0f;
        _subSmooth = 0f;
        _subSmooth2 = 0f;
        _toneState = 0f;
        _samplesSinceSubToggle = 0;
        _organEnvelope = 0f;
        _organKeyEnvelope = 0f;
        _organGain = 1f;
        _organBodyState = 0f;
        _organBodyState2 = 0f;
        _organUpperState = 0f;
        _organSubState = 0f;
        _organHarmonicDcInput = 0f;
        _organHarmonicDcOutput = 0f;
    }
}
