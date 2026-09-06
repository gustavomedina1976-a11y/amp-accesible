namespace GDMAmpAccessible.Dsp;

/// <summary>
/// Funciones de saturación de costo bajo para el hilo de audio. La aproximación racional
/// mantiene una curva suave similar a tanh sin invocar funciones trascendentes por muestra.
/// </summary>
internal static class FastDspMath
{
    private const float DenormalThreshold = 1.0e-20f;

    public static float FlushDenormal(float value)
    {
        if (!float.IsFinite(value) || MathF.Abs(value) < DenormalThreshold)
        {
            return 0f;
        }

        return value;
    }

    public static float SoftClip(float value)
    {
        if (!float.IsFinite(value))
        {
            return 0f;
        }

        float x = Math.Clamp(value, -3f, 3f);
        float squared = x * x;
        return FlushDenormal(x * (27f + squared) / (27f + (9f * squared)));
    }
}
