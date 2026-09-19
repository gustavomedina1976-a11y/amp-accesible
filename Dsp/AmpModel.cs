using GDMAmpAccessible.Models;

namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Modelo de amplificador con etapas de preamplificador suaves, polarización asimétrica,
/// filtrado entre etapas, sag y sobremuestreo 2x. La intención es conservar ataque y dinámica
/// y evitar el recorte duro y excesivo que puede sonar a fuzz.
/// </summary>
internal sealed class AmpModel
{
    private readonly int _sampleRate;
    private readonly int _oversampledRate;
    private readonly Biquad _inputHighPass = new();
    private readonly Biquad _preVoice = new();
    private readonly Biquad _stage1HighPass = new();
    private readonly Biquad _stage1LowPass = new();
    private readonly Biquad _stage2HighPass = new();
    private readonly Biquad _stage2LowPass = new();
    private readonly Biquad _stage3LowPass = new();
    private readonly Biquad _bass = new();
    private readonly Biquad _middle = new();
    private readonly Biquad _treble = new();
    private readonly Biquad _presence = new();
    private readonly Biquad _postLowPass = new();
    private readonly Biquad _cabHighPass = new();
    private readonly Biquad _cabLowPass = new();
    private readonly Biquad _cabResonance = new();

    private AmpChannel _channel;
    private float _gainNormalized;
    private float _makeup;
    private float _lastOversampleInput;
    private float _sagEnvelope;
    private float _stage1DcInput;
    private float _stage1DcOutput;
    private float _stage2DcInput;
    private float _stage2DcOutput;
    private float _stage3DcInput;
    private float _stage3DcOutput;
    private float _leadPowerDcInput;
    private float _leadPowerDcOutput;
    private float _leadPowerEnvelope;
    private float _lastDcInput;
    private float _lastDcOutput;

    public AmpModel(int sampleRate)
        : this(sampleRate, AmpChannel.CleanTwin, 3f, 5f, 4f, 6f, 5f)
    {
    }

    public AmpModel(int sampleRate, AmpChannel channel, float gain, float bass, float middle, float treble, float presence)
    {
        _sampleRate = sampleRate;
        _oversampledRate = sampleRate * 2;
        Configure(channel, gain, bass, middle, treble, presence);
    }

    /// <summary>
    /// La ganancia no necesita recalcular ningún filtro. Se actualiza por separado para
    /// que cambiar de canal no ejecute trigonometría ni diseño de biquads dentro de ASIO.
    /// </summary>
    public void SetGain(float gain)
    {
        _gainNormalized = Math.Clamp(gain, 0f, 10f) / 10f;
    }

    public void Configure(AmpChannel channel, float gain, float bass, float middle, float treble, float presence)
    {
        _channel = channel;
        SetGain(gain);

        float bassDb = (Math.Clamp(bass, 0f, 10f) - 5f) * 2.4f;
        float middleDb = (Math.Clamp(middle, 0f, 10f) - 5f) * 2.2f;
        float trebleDb = (Math.Clamp(treble, 0f, 10f) - 5f) * 2.4f;
        float presenceDb = (Math.Clamp(presence, 0f, 10f) - 5f) * 2.0f;

        float baseMidDb;
        float highPass;
        float lowPass;
        float midFrequency;
        float preVoiceDb;
        float stageHighPass;
        float stage1LowPass;
        float stage2LowPass;
        float stage3LowPass;

        switch (channel)
        {
            case AmpChannel.CleanTwin:
                baseMidDb = -2.2f;
                highPass = 52f;
                lowPass = 9000f;
                midFrequency = 700f;
                preVoiceDb = -0.8f;
                stageHighPass = 58f;
                stage1LowPass = 12500f;
                stage2LowPass = 10500f;
                stage3LowPass = 9500f;
                _makeup = 0.95f;
                break;
            case AmpChannel.CrunchBritish:
                baseMidDb = 2.4f;
                highPass = 68f;
                lowPass = 7200f;
                midFrequency = 830f;
                preVoiceDb = 2.2f;
                stageHighPass = 94f;
                stage1LowPass = 9200f;
                stage2LowPass = 7200f;
                stage3LowPass = 6500f;
                _makeup = 0.66f;
                break;
            case AmpChannel.CleanBoutique:
                // Lone Star Style: limpio grande y valvular, con más cuerpo y medios
                // que el Twin, agudos dulces y una ruptura progresiva al subir Gain.
                baseMidDb = 2.25f;
                highPass = 46f;
                lowPass = 7900f;
                midFrequency = 610f;
                preVoiceDb = 1.75f;
                stageHighPass = 54f;
                stage1LowPass = 10800f;
                stage2LowPass = 8300f;
                stage3LowPass = 7600f;
                _makeup = 0.94f;
                break;
            case AmpChannel.CleanClassA:
                baseMidDb=1.6f; highPass=62f; lowPass=8200f; midFrequency=900f; preVoiceDb=1.4f; stageHighPass=72f; stage1LowPass=10800f; stage2LowPass=8600f; stage3LowPass=7600f; _makeup=.82f; break;
            case AmpChannel.CrunchPlexi:
                baseMidDb=3.2f; highPass=72f; lowPass=6800f; midFrequency=760f; preVoiceDb=2.8f; stageHighPass=88f; stage1LowPass=8800f; stage2LowPass=6900f; stage3LowPass=6100f; _makeup=.68f; break;
            case AmpChannel.CrunchClassA:
                baseMidDb=2.6f; highPass=76f; lowPass=7000f; midFrequency=1050f; preVoiceDb=2.4f; stageHighPass=92f; stage1LowPass=9000f; stage2LowPass=7100f; stage3LowPass=6300f; _makeup=.66f; break;
            case AmpChannel.LeadModern:
                baseMidDb=2.8f; highPass=88f; lowPass=5700f; midFrequency=950f; preVoiceDb=2.8f; stageHighPass=105f; stage1LowPass=7600f; stage2LowPass=5700f; stage3LowPass=4700f; _makeup=.60f; break;
            case AmpChannel.LeadLegacy:
                baseMidDb=4.8f; highPass=64f; lowPass=6100f; midFrequency=780f; preVoiceDb=2.2f; stageHighPass=82f; stage1LowPass=8300f; stage2LowPass=6200f; stage3LowPass=5100f; _makeup=.64f; break;
            case AmpChannel.LeadTripleChannel:
                // Classic aprobado en 2.41.80: se conserva byte por byte en sus valores.
                baseMidDb=1.4f; highPass=58f; lowPass=6250f; midFrequency=720f; preVoiceDb=1.2f;
                stageHighPass=86f; stage1LowPass=9000f; stage2LowPass=6800f; stage3LowPass=5550f; _makeup=.62f; break;
            case AmpChannel.LeadTripleChannelModern:
                // 2.41.83: la toma real mostro mas ataque y medios-altos que Classic,
                // aun con menor nivel. Se aprietan graves sin aumentar la compresion.
                baseMidDb=1.8f; highPass=66f; lowPass=6350f; midFrequency=930f; preVoiceDb=2.7f;
                stageHighPass=96f; stage1LowPass=9000f; stage2LowPass=6700f; stage3LowPass=5400f; _makeup=.55f; break;
            case AmpChannel.TripleChannelClean:
                // Limpio grande y con cuerpo, guiado por la toma real: mantiene headroom
                // pero refuerza medios-graves y el empuje de la 6L6 sin volverlo oscuro.
                baseMidDb=1.4f; highPass=46f; lowPass=8500f; midFrequency=690f; preVoiceDb=1.35f;
                stageHighPass=54f; stage1LowPass=11600f; stage2LowPass=9000f; stage3LowPass=8000f; _makeup=.92f; break;
            case AmpChannel.TripleChannelCleanCrunch:
                // Modo Crunch del canal limpio: densidad armonica en medios-altos sin
                // llegar a la compresion ni a la ganancia de Classic.
                baseMidDb=1.8f; highPass=58f; lowPass=8100f; midFrequency=980f; preVoiceDb=1.9f;
                stageHighPass=76f; stage1LowPass=10400f; stage2LowPass=8000f; stage3LowPass=7000f; _makeup=.73f; break;
            default:
                // Canal 3 revoceado como lead valvular cálido y cantado. Conserva
                // definición pero evita el pico agresivo de medios-altos del JCM800 anterior.
                baseMidDb = 4.0f;
                highPass = 66f;
                lowPass = 5900f;
                midFrequency = 820f;
                preVoiceDb = 2.0f;
                stageHighPass = 86f;
                stage1LowPass = 8200f;
                stage2LowPass = 6100f;
                stage3LowPass = 5000f;
                _makeup = 0.64f;
                break;
        }

        _inputHighPass.SetHighPass(_sampleRate, highPass);
        float preVoiceFrequency = channel switch
        {
            AmpChannel.CleanTwin => 760f,
            AmpChannel.CrunchBritish => 980f,
            AmpChannel.CleanBoutique => 620f, AmpChannel.CleanClassA => 1100f, AmpChannel.CrunchPlexi => 850f, AmpChannel.CrunchClassA => 1150f, AmpChannel.LeadModern => 1050f, AmpChannel.LeadLegacy => 800f, AmpChannel.LeadTripleChannel => 720f, AmpChannel.LeadTripleChannelModern => 1020f, AmpChannel.TripleChannelClean => 720f, AmpChannel.TripleChannelCleanCrunch => 1050f,
            _ => 860f
        };
        _preVoice.SetPeak(_sampleRate, preVoiceFrequency, 0.78f, preVoiceDb);

        _stage1HighPass.SetHighPass(_oversampledRate, stageHighPass, 0.72f);
        _stage1LowPass.SetLowPass(_oversampledRate, stage1LowPass, 0.72f);
        _stage2HighPass.SetHighPass(_oversampledRate, Math.Max(58f, stageHighPass * 0.78f), 0.72f);
        _stage2LowPass.SetLowPass(_oversampledRate, stage2LowPass, 0.72f);
        _stage3LowPass.SetLowPass(_oversampledRate, stage3LowPass, 0.72f);

        _bass.SetLowShelf(_sampleRate, 130f, bassDb);
        _middle.SetPeak(_sampleRate, midFrequency, 0.75f, middleDb + baseMidDb);
        _treble.SetHighShelf(_sampleRate, 2400f, trebleDb);
        _presence.SetPeak(_sampleRate, 3900f, 0.8f, presenceDb);
        _postLowPass.SetLowPass(_sampleRate, lowPass);

        float cabinetHighPass = channel switch
        {
            AmpChannel.CleanTwin => 72f,
            AmpChannel.CrunchBritish => 82f,
            AmpChannel.LeadTripleChannel => 72f,
            AmpChannel.LeadTripleChannelModern => 76f,
            AmpChannel.TripleChannelClean => 68f,
            AmpChannel.TripleChannelCleanCrunch => 74f,
            _ => 76f
        };
        float cabinetLowPass = channel switch
        {
            AmpChannel.CleanTwin => 6500f,
            AmpChannel.CrunchBritish => 5900f,
            AmpChannel.LeadTripleChannel => 6100f,
            AmpChannel.LeadTripleChannelModern => 6150f,
            AmpChannel.TripleChannelClean => 6700f,
            AmpChannel.TripleChannelCleanCrunch => 6400f,
            _ => 5600f
        };
        float cabinetResonanceFrequency = channel switch
        {
            AmpChannel.CleanTwin => 105f,
            AmpChannel.LeadTripleChannel => 108f,
            AmpChannel.LeadTripleChannelModern => 114f,
            AmpChannel.TripleChannelClean => 100f,
            AmpChannel.TripleChannelCleanCrunch => 108f,
            _ => 118f
        };
        float cabinetResonanceDb = channel switch
        {
            AmpChannel.CleanTwin => 2.0f,
            AmpChannel.CrunchBritish => 3.0f,
            AmpChannel.LeadTripleChannel => 4.1f,
            AmpChannel.LeadTripleChannelModern => 3.6f,
            AmpChannel.TripleChannelClean => 3.0f,
            AmpChannel.TripleChannelCleanCrunch => 3.0f,
            _ => 3.3f
        };
        _cabHighPass.SetHighPass(_sampleRate, cabinetHighPass, 0.8f);
        _cabLowPass.SetLowPass(_sampleRate, cabinetLowPass, 0.7f);
        _cabResonance.SetPeak(_sampleRate, cabinetResonanceFrequency, 1.0f, cabinetResonanceDb);
    }

    public float ProcessAmplifier(float input)
    {
        float x = _inputHighPass.Process(float.IsFinite(input) ? input : 0f);
        x = _preVoice.Process(x);

        // Sobremuestreo 2x mediante punto medio. Las etapas internas y sus filtros
        // trabajan a 96 kHz para reducir aliasing y aspereza en los canales saturados.
        float midpoint = (_lastOversampleInput + x) * 0.5f;
        float first = ProcessTubeChain(midpoint);
        float second = ProcessTubeChain(x);
        _lastOversampleInput = x;
        x = (first + second) * 0.5f;

        x = _bass.Process(x);
        x = _middle.Process(x);
        x = _treble.Process(x);
        x = _presence.Process(x);
        x = _postLowPass.Process(x);
        x = DcBlock(x);
        return x * _makeup;
    }

    private float ProcessTubeChain(float input)
    {
        float envelopeTarget = MathF.Min(1.5f, MathF.Abs(input) * 2.2f);
        float envelopeCoefficient = envelopeTarget > _sagEnvelope ? 0.008f : 0.00055f;
        _sagEnvelope = FastDspMath.FlushDenormal(
            _sagEnvelope + ((envelopeTarget - _sagEnvelope) * envelopeCoefficient));
        float sagAmount = _channel switch
        {
            AmpChannel.CleanTwin => 0.10f,
            AmpChannel.CleanBoutique => 0.145f,
            AmpChannel.CleanClassA => 0.12f,
            AmpChannel.CrunchBritish or AmpChannel.CrunchPlexi or AmpChannel.CrunchClassA => 0.26f,
            AmpChannel.LeadTripleChannel => 0.235f,
            AmpChannel.LeadTripleChannelModern => 0.205f,
            AmpChannel.TripleChannelClean => 0.105f,
            AmpChannel.TripleChannelCleanCrunch => 0.175f,
            _ => 0.19f
        };
        float sag = 1f / (1f + (_sagEnvelope * sagAmount));

        float x = _stage1HighPass.Process(input);
        switch (_channel)
        {
            case AmpChannel.CleanTwin:
            case AmpChannel.CleanBoutique:
            case AmpChannel.CleanClassA:
            case AmpChannel.TripleChannelClean:
            {
                float drive1;
                float bias;
                float softness;
                float blendStart;
                float blendScale;
                float stage2Drive;

                if (_channel == AmpChannel.TripleChannelClean)
                {
                    // La referencia real tiene la mayor cresta dinamica de las cuatro
                    // tomas. La segunda etapa entra tarde y en poca proporcion.
                    drive1 = 0.92f + (_gainNormalized * 1.05f);
                    bias = 0.090f;
                    softness = 1.045f;
                    blendStart = 0.58f;
                    blendScale = 1.15f;
                    stage2Drive = 0.50f;
                }
                else if (_channel == AmpChannel.CleanBoutique)
                {
                    // Más cuerpo y compresión valvular; conserva headroom a Gain bajo.
                    drive1 = 1.02f + (_gainNormalized * 1.28f);
                    bias = 0.115f;
                    softness = 1.16f;
                    blendStart = 0.42f;
                    blendScale = 1.48f;
                    stage2Drive = 0.78f;
                }
                else if (_channel == AmpChannel.CleanClassA)
                {
                    drive1 = 1.00f + (_gainNormalized * 1.18f);
                    bias = 0.105f;
                    softness = 1.10f;
                    blendStart = 0.46f;
                    blendScale = 1.42f;
                    stage2Drive = 0.68f;
                }
                else
                {
                    drive1 = 0.95f + (_gainNormalized * 1.05f);
                    bias = 0.09f;
                    softness = 1.08f;
                    blendStart = 0.52f;
                    blendScale = 1.35f;
                    stage2Drive = 0.58f;
                }

                float stage1 = TriodeStage(x * drive1, bias, softness);
                stage1 = StageDcBlock(stage1, ref _stage1DcInput, ref _stage1DcOutput);
                stage1 = _stage1LowPass.Process(stage1);

                float maxBlend = _channel == AmpChannel.CleanBoutique ? 0.58f : _channel == AmpChannel.TripleChannelClean ? 0.34f : 0.48f;
                float blend = Math.Clamp((_gainNormalized - blendStart) * blendScale, 0f, maxBlend);
                float stage2 = TriodeStage(_stage2HighPass.Process(stage1) * (1.02f + _gainNormalized * stage2Drive),
                    _channel == AmpChannel.CleanBoutique ? -0.060f : _channel == AmpChannel.TripleChannelClean ? -0.035f : -0.045f,
                    _channel == AmpChannel.CleanBoutique ? 1.10f : _channel == AmpChannel.TripleChannelClean ? 1.055f : 1.02f);
                stage2 = StageDcBlock(stage2, ref _stage2DcInput, ref _stage2DcOutput);
                stage2 = _stage2LowPass.Process(stage2);
                return ((stage1 * (1f - blend)) + (stage2 * blend)) * sag;
            }
            case AmpChannel.TripleChannelCleanCrunch:
            {
                // Crunch del canal limpio: dos etapas principales y una tercera suave.
                // Se busca compresion armonica, no la saturacion cerrada del canal Classic.
                float drive1 = 1.05f + (_gainNormalized * 1.85f);
                float stage1 = TriodeStageOpen(x * drive1, 0.115f, 1.045f);
                stage1 = StageDcBlock(stage1, ref _stage1DcInput, ref _stage1DcOutput);
                stage1 = _stage1LowPass.Process(stage1);

                float drive2 = 1.02f + (_gainNormalized * 1.45f);
                float stage2 = TriodeStage(_stage2HighPass.Process(stage1) * drive2, -0.078f, 1.035f);
                stage2 = StageDcBlock(stage2, ref _stage2DcInput, ref _stage2DcOutput);
                stage2 = _stage2LowPass.Process(stage2);

                float drive3 = 0.92f + (_gainNormalized * 0.72f);
                float stage3 = TriodeStageOpen(stage2 * drive3, 0.040f, 1.012f);
                stage3 = StageDcBlock(stage3, ref _stage3DcInput, ref _stage3DcOutput);
                stage3 = _stage3LowPass.Process(stage3);

                return ((stage1 * 0.18f) + (stage2 * 0.58f) + (stage3 * 0.24f)) * sag;
            }
            case AmpChannel.CrunchBritish:
            case AmpChannel.CrunchPlexi:
            case AmpChannel.CrunchClassA:
            {
                float drive1 = 1.18f + (_gainNormalized * 2.55f);
                float stage1 = TriodeStage(x * drive1, 0.115f, 1.12f);
                stage1 = StageDcBlock(stage1, ref _stage1DcInput, ref _stage1DcOutput);
                stage1 = _stage1LowPass.Process(stage1);

                float drive2 = 1.05f + (_gainNormalized * 1.70f);
                float stage2 = TriodeStage(_stage2HighPass.Process(stage1) * drive2, -0.075f, 1.08f);
                stage2 = StageDcBlock(stage2, ref _stage2DcInput, ref _stage2DcOutput);
                stage2 = _stage2LowPass.Process(stage2);

                // Un poco de señal de la primera etapa conserva ataque y evita la compresión tipo fuzz.
                return ((stage1 * 0.22f) + (stage2 * 0.78f)) * sag;
            }
            case AmpChannel.LeadTripleChannel:
            {
                // Tres etapas de previo asimetricas, con menos compresion por etapa que
                // LeadModern. Se conserva parte de las etapas tempranas para que la pua
                // siga teniendo relieve aun con bastante ganancia.
                float drive1 = 1.12f + (_gainNormalized * 2.18f);
                float stage1 = TriodeStageOpen(x * drive1, 0.132f, 1.045f);
                stage1 = StageDcBlock(stage1, ref _stage1DcInput, ref _stage1DcOutput);
                stage1 = _stage1LowPass.Process(stage1);

                float drive2 = 1.06f + (_gainNormalized * 1.82f);
                float stage2 = TriodeStageOpen(_stage2HighPass.Process(stage1) * drive2, -0.108f, 1.035f);
                stage2 = StageDcBlock(stage2, ref _stage2DcInput, ref _stage2DcOutput);
                stage2 = _stage2LowPass.Process(stage2);

                float drive3 = 1.00f + (_gainNormalized * 1.24f);
                float stage3 = TriodeStageOpen(stage2 * drive3, 0.058f, 1.018f);
                stage3 = StageDcBlock(stage3, ref _stage3DcInput, ref _stage3DcOutput);
                stage3 = _stage3LowPass.Process(stage3);

                float preamp = (stage1 * 0.10f) + (stage2 * 0.34f) + (stage3 * 0.56f);
                float power = ProcessLeadPowerStage(preamp);
                return ((preamp * 0.44f) + (power * 0.56f)) * sag;
            }
            case AmpChannel.LeadTripleChannelModern:
            {
                // Referencia real: mas saturacion que Classic, pero tambien mas cresta
                // dinamica. El drive adicional queda en previo y se reduce el aplastamiento
                // de potencia para conservar la pua y el contenido de 1 a 2 kHz.
                float drive1 = 1.16f + (_gainNormalized * 2.34f);
                float stage1 = TriodeStageOpen(x * drive1, 0.136f, 1.048f);
                stage1 = StageDcBlock(stage1, ref _stage1DcInput, ref _stage1DcOutput);
                stage1 = _stage1LowPass.Process(stage1);

                float drive2 = 1.13f + (_gainNormalized * 2.30f);
                // 2.41.85: mas asimetria y curvatura, sin aumentar el drive nominal.
                // Facilita la aparicion de armonicos artificiales conservando el ataque.
                float stage2 = TriodeStageOpen(_stage2HighPass.Process(stage1) * drive2, -0.128f, 1.052f);
                stage2 = StageDcBlock(stage2, ref _stage2DcInput, ref _stage2DcOutput);
                stage2 = _stage2LowPass.Process(stage2);

                float drive3 = 1.06f + (_gainNormalized * 1.61f);
                // Polarizacion opuesta a la etapa 2 para enriquecer pares e impares
                // sin sumar un generador de pitch ni un excitador artificial.
                float stage3 = TriodeStageOpen(stage2 * drive3, 0.078f, 1.034f);
                stage3 = StageDcBlock(stage3, ref _stage3DcInput, ref _stage3DcOutput);
                stage3 = _stage3LowPass.Process(stage3);

                // Un poco mas de etapa 2 deja salir antes el contenido armonico de la pua,
                // mientras la etapa 3 sigue aportando sustain. La potencia queda intacta.
                float preamp = (stage1 * 0.10f) + (stage2 * 0.37f) + (stage3 * 0.53f);
                float power = ProcessLeadPowerStage(preamp);
                return ((preamp * 0.48f) + (power * 0.52f)) * sag;
            }
            default:
            {
                // Lead de alta ganancia más valvular: menos drive por etapa, más mezcla
                // entre etapas y una saturación final suave tipo etapa de potencia. Esto
                // produce sustain sin convertir el ataque en un recorte plano o áspero.
                float drive1 = 1.20f + (_gainNormalized * 2.45f);
                float stage1 = TriodeStage(x * drive1, 0.145f, 1.08f);
                stage1 = StageDcBlock(stage1, ref _stage1DcInput, ref _stage1DcOutput);
                stage1 = _stage1LowPass.Process(stage1);

                float drive2 = 1.10f + (_gainNormalized * 1.92f);
                float stage2 = TriodeStage(_stage2HighPass.Process(stage1) * drive2, -0.095f, 1.06f);
                stage2 = StageDcBlock(stage2, ref _stage2DcInput, ref _stage2DcOutput);
                stage2 = _stage2LowPass.Process(stage2);

                float drive3 = 1.00f + (_gainNormalized * 1.16f);
                float stage3 = TriodeStage(stage2 * drive3, 0.068f, 1.025f);
                stage3 = StageDcBlock(stage3, ref _stage3DcInput, ref _stage3DcOutput);
                stage3 = _stage3LowPass.Process(stage3);

                // Mantener una porción de etapas previas devuelve articulación y armónicos
                // pares. La última etapa aporta la compresión cálida y el sustain cantado.
                float preamp = (stage1 * 0.08f) + (stage2 * 0.27f) + (stage3 * 0.65f);
                float power = ProcessLeadPowerStage(preamp);
                return ((preamp * 0.34f) + (power * 0.66f)) * sag;
            }
        }
    }


    private float ProcessLeadPowerStage(float input)
    {
        // Compresion dependiente de envolvente, similar a una etapa de potencia empujada.
        // Triple Channel usa una respuesta 6L6 algo mas abierta: mas golpe y menos
        // aplastamiento sostenido que LeadModern, sin perder el sag.
        bool tripleClassic = _channel == AmpChannel.LeadTripleChannel;
        bool tripleModern = _channel == AmpChannel.LeadTripleChannelModern;
        float targetScale = tripleClassic ? 1.62f : tripleModern ? 1.60f : 1.75f;
        float target = MathF.Min(1.5f, MathF.Abs(input) * targetScale);
        float attack = tripleClassic ? 0.0029f : tripleModern ? 0.0027f : 0.0035f;
        float release = tripleClassic ? 0.00027f : tripleModern ? 0.00025f : 0.00032f;
        float coefficient = target > _leadPowerEnvelope ? attack : release;
        _leadPowerEnvelope = FastDspMath.FlushDenormal(
            _leadPowerEnvelope + ((target - _leadPowerEnvelope) * coefficient));

        float dynamicDrive = tripleClassic
            ? 1.16f + (_gainNormalized * 0.50f)
            : tripleModern
                ? 1.20f + (_gainNormalized * 0.64f)
                : 1.10f + (_gainNormalized * 0.42f);
        float compressionAmount = tripleClassic ? 0.082f : tripleModern ? 0.064f : 0.11f;
        float compression = 1f / (1f + (_leadPowerEnvelope * compressionAmount));
        float driven = input * dynamicDrive * compression;
        float saturated = tripleClassic
            ? TriodeStageOpen(driven, 0.028f, 1.010f)
            : tripleModern
                ? TriodeStageOpen(driven, 0.031f, 1.010f)
                : TriodeStage(driven, 0.035f, 1.015f);
        return StageDcBlock(saturated, ref _leadPowerDcInput, ref _leadPowerDcOutput);
    }

    private static float TriodeStage(float input, float bias, float curvature)
    {
        float biasedInput = (input + bias) * curvature;
        float idle = FastDspMath.SoftClip(bias * curvature);
        float output = FastDspMath.SoftClip(biasedInput) - idle;

        // Compresión gradual de los extremos, no recorte plano. Mantiene la pendiente
        // cerca de cero y genera armónicos pares por la polarización asimétrica.
        float correction = 1f + (0.16f * MathF.Abs(output));
        return output / correction;
    }

    private static float TriodeStageOpen(float input, float bias, float curvature)
    {
        float biasedInput = (input + bias) * curvature;
        float idle = FastDspMath.SoftClip(bias * curvature);
        float output = FastDspMath.SoftClip(biasedInput) - idle;

        // Menos correccion que TriodeStage: conserva mas pendiente y dinamica.
        float correction = 1f + (0.105f * MathF.Abs(output));
        return output / correction;
    }

    private static float StageDcBlock(float input, ref float lastInput, ref float lastOutput)
    {
        float output = FastDspMath.FlushDenormal(input - lastInput + (0.9975f * lastOutput));
        lastInput = FastDspMath.FlushDenormal(input);
        lastOutput = output;
        return output;
    }

    public float ProcessBuiltInCabinet(float input)
    {
        float x = _cabHighPass.Process(input);
        x = _cabResonance.Process(x);
        x = _cabLowPass.Process(x);
        return x * 0.92f;
    }

    private float DcBlock(float input)
    {
        float output = FastDspMath.FlushDenormal(input - _lastDcInput + (0.995f * _lastDcOutput));
        _lastDcInput = FastDspMath.FlushDenormal(input);
        _lastDcOutput = output;
        return output;
    }

    public void Reset()
    {
        _inputHighPass.Reset();
        _preVoice.Reset();
        _stage1HighPass.Reset();
        _stage1LowPass.Reset();
        _stage2HighPass.Reset();
        _stage2LowPass.Reset();
        _stage3LowPass.Reset();
        _bass.Reset();
        _middle.Reset();
        _treble.Reset();
        _presence.Reset();
        _postLowPass.Reset();
        _cabHighPass.Reset();
        _cabLowPass.Reset();
        _cabResonance.Reset();
        _lastOversampleInput = 0f;
        _sagEnvelope = 0f;
        _stage1DcInput = 0f;
        _stage1DcOutput = 0f;
        _stage2DcInput = 0f;
        _stage2DcOutput = 0f;
        _stage3DcInput = 0f;
        _stage3DcOutput = 0f;
        _leadPowerDcInput = 0f;
        _leadPowerDcOutput = 0f;
        _leadPowerEnvelope = 0f;
        _lastDcInput = 0f;
        _lastDcOutput = 0f;
    }
}
