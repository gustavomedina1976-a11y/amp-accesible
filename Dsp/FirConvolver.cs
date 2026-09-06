using System.Numerics;

namespace GDMAmpAccessible.Dsp;

internal sealed class FirConvolver
{
    private readonly float[] _reversedImpulse;
    private readonly float[] _history;
    private readonly int _length;
    private int _index;

    public FirConvolver(float[] impulse)
    {
        if (impulse.Length == 0)
        {
            throw new ArgumentException("La respuesta impulsional está vacía.", nameof(impulse));
        }

        _length = impulse.Length;
        _reversedImpulse = new float[_length];
        _history = new float[_length * 2];

        for (int i = 0; i < _length; i++)
        {
            _reversedImpulse[i] = impulse[_length - 1 - i];
        }
    }

    public int Length => _length;

    public void Reset()
    {
        Array.Clear(_history);
        _index = 0;
    }

    public float Process(float input)
    {
        _history[_index] = input;
        _history[_index + _length] = input;

        int start = _index + 1;
        int vectorSize = Vector<float>.Count;
        int i = 0;
        Vector<float> vectorSum = Vector<float>.Zero;

        for (; i <= _length - vectorSize; i += vectorSize)
        {
            var historyVector = new Vector<float>(_history, start + i);
            var impulseVector = new Vector<float>(_reversedImpulse, i);
            vectorSum += historyVector * impulseVector;
        }

        float sum = 0f;
        for (int lane = 0; lane < vectorSize; lane++)
        {
            sum += vectorSum[lane];
        }

        for (; i < _length; i++)
        {
            sum += _history[start + i] * _reversedImpulse[i];
        }

        _index++;
        if (_index >= _length)
        {
            _index = 0;
        }

        return sum;
    }
}
