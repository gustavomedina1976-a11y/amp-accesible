using NAudio.Wave;

namespace GDMAmpAccessible.Audio;

/// <summary>
/// Define el formato estéreo y entrega silencio como respaldo al iniciar ASIO.
/// Durante el modo dúplex, el bloque procesado se escribe directamente en los buffers nativos.
/// </summary>
internal sealed class RealtimeWaveProvider : IWaveProvider
{
    private readonly int _blockAlign;
    private float[]? _samples;
    private int _availableBytes;

    public RealtimeWaveProvider(int sampleRate, int channels)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _blockAlign = WaveFormat.BlockAlign;
    }

    public WaveFormat WaveFormat { get; }

    public void SetSamples(float[] samples, int frames)
    {
        _samples = samples;
        _availableBytes = checked(frames * _blockAlign);
    }

    public void SetSilence(int frames)
    {
        _samples = null;
        _availableBytes = checked(frames * _blockAlign);
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        int bytesToWrite = Math.Min(count, _availableBytes);
        float[]? samples = _samples;

        if (samples is null)
        {
            Array.Clear(buffer, offset, bytesToWrite);
        }
        else
        {
            int sourceBytes = Math.Min(bytesToWrite, samples.Length * sizeof(float));
            Buffer.BlockCopy(samples, 0, buffer, offset, sourceBytes);
            if (sourceBytes < bytesToWrite)
            {
                Array.Clear(buffer, offset + sourceBytes, bytesToWrite - sourceBytes);
            }
        }

        if (bytesToWrite < count)
        {
            Array.Clear(buffer, offset + bytesToWrite, count - bytesToWrite);
        }

        return count;
    }
}
