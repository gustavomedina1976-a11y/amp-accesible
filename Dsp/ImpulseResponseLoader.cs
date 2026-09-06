using NAudio.Wave;

namespace GDMAmpAccessible.Dsp;

internal static class ImpulseResponseLoader
{
    public const int MaximumImpulseSamples = 512;

    public static float[] LoadMono(string path, int targetSampleRate)
    {
        using var reader = new AudioFileReader(path);
        int channels = reader.WaveFormat.Channels;
        int sourceSampleRate = reader.WaveFormat.SampleRate;
        int maximumSourceFrames = Math.Max(sourceSampleRate * 5, MaximumImpulseSamples * 4);
        var mono = new List<float>(Math.Min(maximumSourceFrames, sourceSampleRate));
        var buffer = new float[4096 * channels];

        while (mono.Count < maximumSourceFrames)
        {
            int read = reader.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            int frames = read / channels;
            for (int frame = 0; frame < frames && mono.Count < maximumSourceFrames; frame++)
            {
                float sum = 0f;
                int baseIndex = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    sum += buffer[baseIndex + channel];
                }
                mono.Add(sum / channels);
            }
        }

        if (mono.Count == 0)
        {
            throw new InvalidDataException("El archivo no contiene audio utilizable.");
        }

        float[] samples = mono.ToArray();
        if (sourceSampleRate != targetSampleRate)
        {
            samples = LinearResample(samples, sourceSampleRate, targetSampleRate);
        }

        float peak = samples.Max(value => MathF.Abs(value));
        if (peak < 0.0000001f)
        {
            throw new InvalidDataException("El archivo IR está en silencio.");
        }

        float startThreshold = peak * 0.001f;
        int first = 0;
        while (first < samples.Length && MathF.Abs(samples[first]) < startThreshold)
        {
            first++;
        }
        first = Math.Max(0, first - 16);

        int available = samples.Length - first;
        int length = Math.Min(MaximumImpulseSamples, available);
        if (length < 32)
        {
            throw new InvalidDataException("El IR es demasiado corto.");
        }

        var impulse = new float[length];
        Array.Copy(samples, first, impulse, 0, length);

        float mean = impulse.Average();
        for (int i = 0; i < impulse.Length; i++)
        {
            impulse[i] -= mean;
        }

        int fadeLength = Math.Min(96, impulse.Length / 4);
        for (int i = 0; i < fadeLength; i++)
        {
            float factor = 1f - ((i + 1f) / fadeLength);
            int index = impulse.Length - fadeLength + i;
            impulse[index] *= factor;
        }

        double energy = 0.0;
        foreach (float sample in impulse)
        {
            energy += sample * sample;
        }

        float rootEnergy = (float)Math.Sqrt(Math.Max(energy, 0.0000000001));
        float scale = Math.Clamp(0.72f / rootEnergy, 0.08f, 4f);
        for (int i = 0; i < impulse.Length; i++)
        {
            impulse[i] *= scale;
        }

        return impulse;
    }

    private static float[] LinearResample(float[] source, int sourceRate, int targetRate)
    {
        int targetLength = Math.Max(1, (int)Math.Round(source.Length * (double)targetRate / sourceRate));
        var result = new float[targetLength];
        double ratio = (double)sourceRate / targetRate;

        for (int i = 0; i < targetLength; i++)
        {
            double sourcePosition = i * ratio;
            int indexA = Math.Min((int)sourcePosition, source.Length - 1);
            int indexB = Math.Min(indexA + 1, source.Length - 1);
            float fraction = (float)(sourcePosition - indexA);
            result[i] = source[indexA] + ((source[indexB] - source[indexA]) * fraction);
        }

        return result;
    }
}
