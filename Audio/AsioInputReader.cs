using NAudio.Wave;
using NAudio.Wave.Asio;

namespace GDMAmpAccessible.Audio;

/// <summary>
/// Lee exclusivamente un buffer físico de entrada ASIO y lo convierte a mono float.
/// No intercala ni suma las demás entradas de la interfaz.
/// </summary>
internal static class AsioInputReader
{
    public static unsafe void ReadMono(AsioAudioAvailableEventArgs args, int inputBufferIndex,
        float[] destination, int frames)
    {
        if (inputBufferIndex < 0 || inputBufferIndex >= args.InputBuffers.Length)
        {
            throw new InvalidOperationException(
                $"La entrada ASIO solicitada {inputBufferIndex + 1} no está disponible. " +
                $"El controlador entregó {args.InputBuffers.Length} entrada(s).");
        }

        if (destination.Length < frames)
        {
            throw new ArgumentException("El buffer mono es demasiado pequeño.", nameof(destination));
        }

        IntPtr source = args.InputBuffers[inputBufferIndex];
        switch (args.AsioSampleType)
        {
            case AsioSampleType.Float32LSB:
            {
                float* samples = (float*)source.ToPointer();
                for (int i = 0; i < frames; i++)
                {
                    destination[i] = Sanitize(samples[i]);
                }
                break;
            }
            case AsioSampleType.Int32LSB:
            {
                int* samples = (int*)source.ToPointer();
                const float scale = 1f / 2147483648f;
                for (int i = 0; i < frames; i++)
                {
                    destination[i] = samples[i] * scale;
                }
                break;
            }
            case AsioSampleType.Int24LSB:
            {
                byte* samples = (byte*)source.ToPointer();
                const float scale = 1f / 8388608f;
                for (int i = 0; i < frames; i++)
                {
                    byte* p = samples + (i * 3);
                    int value = p[0] | (p[1] << 8) | ((sbyte)p[2] << 16);
                    destination[i] = value * scale;
                }
                break;
            }
            case AsioSampleType.Int16LSB:
            {
                short* samples = (short*)source.ToPointer();
                const float scale = 1f / 32768f;
                for (int i = 0; i < frames; i++)
                {
                    destination[i] = samples[i] * scale;
                }
                break;
            }
            default:
                throw new NotSupportedException(
                    $"Formato ASIO no soportado para la entrada: {args.AsioSampleType}.");
        }
    }

    private static float Sanitize(float value)
    {
        return float.IsFinite(value) ? Math.Clamp(value, -1.25f, 1.25f) : 0f;
    }
}
