using System.Diagnostics;
using System.Text;

namespace GDMAmpAccessible.Audio;

// Contadores preasignados: sin E/S, locks ni asignaciones en el callback.
internal sealed class RecorderDiagnostics
{
    private readonly int _rate, _channels;
    private readonly long[] _finite, _nonzero, _pcmNonzero, _invalid, _clipped;
    private readonly double[] _peak, _squares;
    private readonly DateTime _start = DateTime.UtcNow;
    private readonly long _startTick = Stopwatch.GetTimestamp();
    private long _calls, _frames, _firstTick, _lastTick, _maxGap;
    public string ContextStart = "", ContextEnd = "";
    public bool Drained, Automatic;
    private int _dspMaximum;
    private long _dspDeadlines;
    public int DspMaximum => Volatile.Read(ref _dspMaximum);
    public long DspDeadlines => Interlocked.Read(ref _dspDeadlines);
    public void ObserveDsp(int load)
    {
        if (load > _dspMaximum) Volatile.Write(ref _dspMaximum, load);
        if (load >= 100) Interlocked.Increment(ref _dspDeadlines);
    }
    // Se consulta fuera del callback. NaN/Infinity se contabilizan aparte.
    public double MaximumAbsoluteReceivedPeak
    {
        get
        {
            double peak = 0;
            for (int channel = 0; channel < _channels; channel++)
                peak = Math.Max(peak, Volatile.Read(ref _peak[channel]));
            return peak;
        }
    }

    public RecorderDiagnostics(int rate, int channels)
    {
        _rate = rate; _channels = channels;
        _finite = new long[channels]; _nonzero = new long[channels];
        _pcmNonzero = new long[channels]; _invalid = new long[channels];
        _clipped = new long[channels]; _peak = new double[channels]; _squares = new double[channels];
    }
    public void Callback(int frames)
    {
        long now = Stopwatch.GetTimestamp();
        if (_calls == 0) _firstTick = now;
        else _maxGap = Math.Max(_maxGap, now - _lastTick);
        _lastTick = now; _calls++; _frames += frames;
    }
    public void Sample(float value, short pcm, int channel, bool finite)
    {
        if (!finite) { _invalid[channel]++; return; }
        _finite[channel]++;
        if (value != 0) _nonzero[channel]++;
        if (pcm != 0) _pcmNonzero[channel]++;
        float absolute = Math.Abs(value);
        if (absolute > 1) _clipped[channel]++;
        _peak[channel] = Math.Max(_peak[channel], absolute);
        _squares[channel] += (double)value * value;
    }
    public string Verify(string path, short[] buffer, int count)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        long expected = 44L + count * 2L;
        bool header = Encoding.ASCII.GetString(reader.ReadBytes(4)) == "RIFF";
        header &= reader.ReadInt32() == expected - 8;
        header &= Encoding.ASCII.GetString(reader.ReadBytes(8)) == "WAVEfmt ";
        header &= reader.ReadInt32() == 16;
        header &= reader.ReadInt16() == 1;
        header &= reader.ReadInt16() == _channels;
        header &= reader.ReadInt32() == _rate;
        header &= reader.ReadInt32() == _rate * _channels * 2;
        header &= reader.ReadInt16() == _channels * 2;
        header &= reader.ReadInt16() == 16;
        header &= Encoding.ASCII.GetString(reader.ReadBytes(4)) == "data";
        header &= reader.ReadInt32() == count * 2;
        long different = 0, nonzero = 0;
        for (int i = 0; i < count; i++)
        {
            short pcm = reader.ReadInt16();
            if (pcm != buffer[i]) different++;
            if (pcm != 0) nonzero++;
        }
        return $"WAV cerrado y releido: cabecera valida={header}; bytes esperados={expected}; bytes reales={stream.Length}; muestras diferentes de memoria={different}; muestras no cero en disco={nonzero}.";
    }
    public string Report(int count, string result)
    {
        var text = new StringBuilder();
        text.AppendLine($"Amp Accessible {AppInfo.Version}; {AppInfo.BuildName}");
        text.AppendLine($"Inicio UTC={_start:O}; fin UTC={DateTime.UtcNow:O}; parada automatica={Automatic}; callback finalizado antes de guardar={Drained}");
        text.AppendLine("Punto de captura: mezcla interna post-looper, antes del master. PCM16 intercalado.");
        text.AppendLine($"Inicio motor: {ContextStart}\nFin motor: {ContextEnd}");
        text.AppendLine($"DSP maximo durante grabacion={DspMaximum} %; plazos excedidos durante grabacion={DspDeadlines}");
        text.AppendLine($"Frecuencia={_rate}; canales={_channels}; llamadas Capture={_calls}; frames solicitados={_frames}; muestras guardadas={count}; segundos WAV={count / (double)(_rate * _channels):F6}");
        double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        text.AppendLine($"Espera primer Capture ms={(_calls == 0 ? -1 : Ms(_firstTick - _startTick)):F3}; mayor intervalo ms={Ms(_maxGap):F3}; tiempo desde ultimo Capture ms={(_calls == 0 ? -1 : Ms(Stopwatch.GetTimestamp() - _lastTick)):F3}");
        for (int c = 0; c < _channels; c++)
        {
            double rms = _finite[c] == 0 ? 0 : Math.Sqrt(_squares[c] / _finite[c]);
            text.AppendLine($"Canal {c + 1}: pico float={_peak[c]:G9}; RMS={rms:G9}; finitas={_finite[c]}; float no cero={_nonzero[c]}; PCM16 no cero={_pcmNonzero[c]}; NaN/Infinity={_invalid[c]}; fuera de rango +/-1={_clipped[c]}");
        }
        text.AppendLine(_calls == 0 ? "Resultado: no se recibieron llamadas Capture durante la toma." :
            count == 0 ? "Resultado: Capture fue llamado pero no se almacenaron muestras." :
            _pcmNonzero.Sum() == 0 ? "Resultado: PCM16 completamente en silencio. Revisar float no cero (cuantizacion) y NaN/Infinity (sustitucion por cero)." :
            "Resultado: PCM16 contiene senal; comparar verificacion del archivo y niveles por canal.");
        if (!Drained) text.AppendLine("Advertencia: vencio la espera de 100 ms del callback; la captura y el guardado pueden solaparse. Mediciones no concluyentes.");
        text.AppendLine(result);
        return text.ToString();
    }
}
