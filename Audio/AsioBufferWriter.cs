using NAudio.Wave;
using NAudio.Wave.Asio;

namespace GDMAmpAccessible.Audio;

/// <summary>
/// Escribe el bloque procesado directamente en los buffers nativos del controlador ASIO.
/// Esto evita colas intermedias y, sobre todo, evita que se repitan bloques antiguos.
/// </summary>
internal static class AsioBufferWriter
{
    public static unsafe void WriteStereo(AsioAudioAvailableEventArgs args, float[] interleaved, int frames)
    {
        if (args.OutputBuffers.Length < 2)
        {
            throw new InvalidOperationException(
                $"El controlador ASIO expuso {args.OutputBuffers.Length} salida(s); se necesitan al menos 2.");
        }

        if (interleaved.Length < frames * 2)
        {
            throw new ArgumentException("El bloque estéreo es más pequeño que la cantidad de muestras solicitada.",
                nameof(interleaved));
        }

        switch (args.AsioSampleType)
        {
            case AsioSampleType.Float32LSB:
                WriteFloat32(args.OutputBuffers[0], args.OutputBuffers[1], interleaved, frames);
                break;
            case AsioSampleType.Int16LSB:
                WriteInt16(args.OutputBuffers[0], args.OutputBuffers[1], interleaved, frames);
                break;
            case AsioSampleType.Int24LSB:
                WriteInt24(args.OutputBuffers[0], args.OutputBuffers[1], interleaved, frames);
                break;
            case AsioSampleType.Int32LSB:
                WriteInt32(args.OutputBuffers[0], args.OutputBuffers[1], interleaved, frames, 2147483647.0);
                break;
            default:
                throw new NotSupportedException(
                    $"Formato ASIO no soportado para salida directa: {args.AsioSampleType}.");
        }

        args.WrittenToOutputBuffers = true;
    }

    public static unsafe void ClearOutputs(AsioAudioAvailableEventArgs args)
    {
        int frames = args.SamplesPerBuffer;
        foreach (IntPtr buffer in args.OutputBuffers)
        {
            switch (args.AsioSampleType)
            {
                case AsioSampleType.Int16LSB:
                    new Span<short>(buffer.ToPointer(), frames).Clear();
                    break;
                case AsioSampleType.Int24LSB:
                    new Span<byte>(buffer.ToPointer(), frames * 3).Clear();
                    break;
                default:
                    new Span<int>(buffer.ToPointer(), frames).Clear();
                    break;
            }
        }

        args.WrittenToOutputBuffers = true;
    }

    private static unsafe void WriteFloat32(IntPtr leftBuffer, IntPtr rightBuffer, float[] source, int frames)
    {
        float* left = (float*)leftBuffer.ToPointer();
        float* right = (float*)rightBuffer.ToPointer();
        for (int frame = 0; frame < frames; frame++)
        {
            left[frame] = Sanitize(source[frame * 2]);
            right[frame] = Sanitize(source[(frame * 2) + 1]);
        }
    }


    private static unsafe void WriteInt16(IntPtr leftBuffer, IntPtr rightBuffer, float[] source, int frames)
    {
        short* left = (short*)leftBuffer.ToPointer();
        short* right = (short*)rightBuffer.ToPointer();
        for (int frame = 0; frame < frames; frame++)
        {
            left[frame] = (short)(Sanitize(source[frame * 2]) * 32767f);
            right[frame] = (short)(Sanitize(source[(frame * 2) + 1]) * 32767f);
        }
    }

    private static unsafe void WriteInt24(IntPtr leftBuffer, IntPtr rightBuffer, float[] source, int frames)
    {
        byte* left = (byte*)leftBuffer.ToPointer();
        byte* right = (byte*)rightBuffer.ToPointer();
        for (int frame = 0; frame < frames; frame++)
        {
            WriteInt24Sample(left + (frame * 3), (int)(Sanitize(source[frame * 2]) * 8388607f));
            WriteInt24Sample(right + (frame * 3),
                (int)(Sanitize(source[(frame * 2) + 1]) * 8388607f));
        }
    }

    private static unsafe void WriteInt32(IntPtr leftBuffer, IntPtr rightBuffer, float[] source, int frames,
        double scale)
    {
        int* left = (int*)leftBuffer.ToPointer();
        int* right = (int*)rightBuffer.ToPointer();
        for (int frame = 0; frame < frames; frame++)
        {
            left[frame] = (int)(Sanitize(source[frame * 2]) * scale);
            right[frame] = (int)(Sanitize(source[(frame * 2) + 1]) * scale);
        }
    }

    private static unsafe void WriteInt24Sample(byte* destination, int value)
    {
        destination[0] = (byte)(value & 0xFF);
        destination[1] = (byte)((value >> 8) & 0xFF);
        destination[2] = (byte)((value >> 16) & 0xFF);
    }

    private static float Sanitize(float value)
    {
        if (!float.IsFinite(value))
        {
            return 0f;
        }

        return Math.Clamp(value, -1f, 1f);
    }
}
