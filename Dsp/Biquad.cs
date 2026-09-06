namespace GDMAmpAccessible.Dsp;

internal sealed class Biquad
{
    private float _b0 = 1f;
    private float _b1;
    private float _b2;
    private float _a1;
    private float _a2;
    private float _z1;
    private float _z2;

    public float Process(float input)
    {
        input = FastDspMath.FlushDenormal(input);
        float output = (_b0 * input) + _z1;
        if (!float.IsFinite(output))
        {
            Reset();
            return 0f;
        }

        _z1 = FastDspMath.FlushDenormal((_b1 * input) - (_a1 * output) + _z2);
        _z2 = FastDspMath.FlushDenormal((_b2 * input) - (_a2 * output));
        return FastDspMath.FlushDenormal(output);
    }

    public void Reset()
    {
        _z1 = 0f;
        _z2 = 0f;
    }

    public void SetLowPass(int sampleRate, float frequency, float q = 0.70710678f)
    {
        frequency = Math.Clamp(frequency, 10f, sampleRate * 0.45f);
        float omega = 2f * MathF.PI * frequency / sampleRate;
        float cos = MathF.Cos(omega);
        float sin = MathF.Sin(omega);
        float alpha = sin / (2f * q);
        float b0 = (1f - cos) * 0.5f;
        float b1 = 1f - cos;
        float b2 = (1f - cos) * 0.5f;
        float a0 = 1f + alpha;
        float a1 = -2f * cos;
        float a2 = 1f - alpha;
        SetNormalized(b0, b1, b2, a0, a1, a2);
    }

    public void SetHighPass(int sampleRate, float frequency, float q = 0.70710678f)
    {
        frequency = Math.Clamp(frequency, 10f, sampleRate * 0.45f);
        float omega = 2f * MathF.PI * frequency / sampleRate;
        float cos = MathF.Cos(omega);
        float sin = MathF.Sin(omega);
        float alpha = sin / (2f * q);
        float b0 = (1f + cos) * 0.5f;
        float b1 = -(1f + cos);
        float b2 = (1f + cos) * 0.5f;
        float a0 = 1f + alpha;
        float a1 = -2f * cos;
        float a2 = 1f - alpha;
        SetNormalized(b0, b1, b2, a0, a1, a2);
    }

    public void SetPeak(int sampleRate, float frequency, float q, float gainDb)
    {
        frequency = Math.Clamp(frequency, 10f, sampleRate * 0.45f);
        float a = MathF.Pow(10f, gainDb / 40f);
        float omega = 2f * MathF.PI * frequency / sampleRate;
        float cos = MathF.Cos(omega);
        float sin = MathF.Sin(omega);
        float alpha = sin / (2f * Math.Max(0.1f, q));
        float b0 = 1f + (alpha * a);
        float b1 = -2f * cos;
        float b2 = 1f - (alpha * a);
        float a0 = 1f + (alpha / a);
        float a1 = -2f * cos;
        float a2 = 1f - (alpha / a);
        SetNormalized(b0, b1, b2, a0, a1, a2);
    }

    public void SetLowShelf(int sampleRate, float frequency, float gainDb)
    {
        SetShelf(sampleRate, frequency, gainDb, highShelf: false);
    }

    public void SetHighShelf(int sampleRate, float frequency, float gainDb)
    {
        SetShelf(sampleRate, frequency, gainDb, highShelf: true);
    }

    private void SetShelf(int sampleRate, float frequency, float gainDb, bool highShelf)
    {
        frequency = Math.Clamp(frequency, 10f, sampleRate * 0.45f);
        float a = MathF.Pow(10f, gainDb / 40f);
        float omega = 2f * MathF.PI * frequency / sampleRate;
        float cos = MathF.Cos(omega);
        float sin = MathF.Sin(omega);
        float alpha = sin / MathF.Sqrt(2f);
        float twoSqrtAAlpha = 2f * MathF.Sqrt(a) * alpha;

        float b0;
        float b1;
        float b2;
        float a0;
        float a1;
        float a2;

        if (highShelf)
        {
            b0 = a * ((a + 1f) + ((a - 1f) * cos) + twoSqrtAAlpha);
            b1 = -2f * a * ((a - 1f) + ((a + 1f) * cos));
            b2 = a * ((a + 1f) + ((a - 1f) * cos) - twoSqrtAAlpha);
            a0 = (a + 1f) - ((a - 1f) * cos) + twoSqrtAAlpha;
            a1 = 2f * ((a - 1f) - ((a + 1f) * cos));
            a2 = (a + 1f) - ((a - 1f) * cos) - twoSqrtAAlpha;
        }
        else
        {
            b0 = a * ((a + 1f) - ((a - 1f) * cos) + twoSqrtAAlpha);
            b1 = 2f * a * ((a - 1f) - ((a + 1f) * cos));
            b2 = a * ((a + 1f) - ((a - 1f) * cos) - twoSqrtAAlpha);
            a0 = (a + 1f) + ((a - 1f) * cos) + twoSqrtAAlpha;
            a1 = -2f * ((a - 1f) + ((a + 1f) * cos));
            a2 = (a + 1f) + ((a - 1f) * cos) - twoSqrtAAlpha;
        }

        SetNormalized(b0, b1, b2, a0, a1, a2);
    }

    private void SetNormalized(float b0, float b1, float b2, float a0, float a1, float a2)
    {
        float inverseA0 = 1f / a0;
        _b0 = b0 * inverseA0;
        _b1 = b1 * inverseA0;
        _b2 = b2 * inverseA0;
        _a1 = a1 * inverseA0;
        _a2 = a2 * inverseA0;
    }
}
