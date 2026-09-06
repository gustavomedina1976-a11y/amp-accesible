using System.Globalization;
using System.Text;
using GDMAmpAccessible.Models;
using GDMAmpAccessible;

namespace GDMAmpAccessible.Audio;

/// <summary>
/// Mantiene telemetría reciente en memoria y sólo escribe al disco al detenerse el audio
/// o cuando se detecta una falla. Así el diagnóstico no agrega E/S periódica a la sesión.
/// Desde 2.10.9 cada incidente continuo genera un único archivo.
/// </summary>
internal sealed class AudioDiagnosticsLog
{
    private const int Capacity = 720; // 6 minutos con una muestra cada 500 ms.
    private readonly AudioTelemetry[] _ring = new AudioTelemetry[Capacity];
    private int _next;
    private int _count;
    private string _sessionDescription = "Sesión sin iniciar";
    private DateTime _sessionStart;
    private string? _lastSavedPath;
    private int _lastIncidentSequence = -1;
    private string? _lastIncidentPath;

    public string? LastSavedPath => _lastSavedPath;

    public void BeginSession(string driver, int inputIndex, int requestedBuffer, int actualBuffer)
    {
        _next = 0;
        _count = 0;
        _sessionStart = DateTime.Now;
        _sessionDescription = $"Driver={driver}; entrada={inputIndex + 1}; buffer solicitado={requestedBuffer}; buffer efectivo inicial={actualBuffer}";
        _lastSavedPath = null;
        _lastIncidentSequence = -1;
        _lastIncidentPath = null;
    }

    public void Capture(AudioEngine engine, AmpChannel channel, int effectFlags)
    {
        _ring[_next] = new AudioTelemetry(
            DateTime.Now,
            engine.CallbackCount,
            engine.CallbackAgeMilliseconds,
            engine.LastDspLoadPercent,
            engine.MaxDspLoadPercent,
            engine.MaxDspLoadClean,
            engine.MaxDspLoadCrunch,
            engine.MaxDspLoadLead,
            engine.OutputUnderrunCount,
            engine.TotalAudioErrorCount,
            engine.BufferErrorCount,
            engine.InputReadErrorCount,
            engine.DspErrorCount,
            engine.OutputWriteErrorCount,
            engine.DriverResetRequestCount,
            engine.BufferChangeCount,
            engine.ActualBufferSize,
            GC.GetTotalMemory(false),
            Environment.WorkingSet,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            channel,
            effectFlags);

        _next = (_next + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    /// <summary>
    /// Guarda una sola vez cada incidente de audio. Las llamadas repetidas con el mismo
    /// número devuelven el mismo archivo en vez de crear copias por segundo.
    /// </summary>
    public string SaveIncident(int sequence, string reason, string? detail = null)
    {
        if (sequence == _lastIncidentSequence && !string.IsNullOrWhiteSpace(_lastIncidentPath))
        {
            return _lastIncidentPath;
        }

        string path = SaveCore(reason, detail);
        _lastIncidentSequence = sequence;
        _lastIncidentPath = path;
        return path;
    }

    public string Save(string reason, string? detail = null) => SaveCore(reason, detail);

    private string SaveCore(string reason, string? detail)
    {
        string basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GDM Amp Accessible", "Diagnosticos");
        Directory.CreateDirectory(basePath);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        string path = Path.Combine(basePath, $"audio_{stamp}.txt");
        var text = new StringBuilder(49152);
        text.AppendLine($"Amp Accessible {AppInfo.Version} - diagnóstico por etapas, voz y micrófono virtual seguro");
        text.AppendLine($"Inicio de sesión: {_sessionStart:yyyy-MM-dd HH:mm:ss.fff}");
        text.AppendLine($"Fin/registro: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        text.AppendLine($"Motivo: {reason}");
        text.AppendLine($"Sesión: {_sessionDescription}");
        if (!string.IsNullOrWhiteSpace(detail))
        {
            text.AppendLine("Detalle del primer error del incidente:");
            text.AppendLine(detail);
        }
        text.AppendLine();
        text.AppendLine("Hora | callbacks | edad_callback_ms | carga_actual | carga_max | max_clean | max_crunch | max_lead | plazos_excedidos | errores_total | err_buffer | err_entrada | err_dsp | err_salida | resets_driver | cambios_buffer | buffer_efectivo | GC_MB | proceso_MB | GC0 | GC1 | GC2 | canal | efectos");

        int start = (_next - _count + Capacity) % Capacity;
        for (int i = 0; i < _count; i++)
        {
            AudioTelemetry t = _ring[(start + i) % Capacity];
            text.Append(t.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(t.CallbackCount).Append(" | ")
                .Append(t.CallbackAgeMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(t.LastLoad).Append(" | ")
                .Append(t.MaxLoad).Append(" | ")
                .Append(t.MaxClean).Append(" | ")
                .Append(t.MaxCrunch).Append(" | ")
                .Append(t.MaxLead).Append(" | ")
                .Append(t.Underruns).Append(" | ")
                .Append(t.TotalErrors).Append(" | ")
                .Append(t.BufferErrors).Append(" | ")
                .Append(t.InputErrors).Append(" | ")
                .Append(t.DspErrors).Append(" | ")
                .Append(t.OutputErrors).Append(" | ")
                .Append(t.DriverResets).Append(" | ")
                .Append(t.BufferChanges).Append(" | ")
                .Append(t.BufferSize).Append(" | ")
                .Append((t.GcBytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(" | ")
                .Append((t.WorkingSetBytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(t.Gc0).Append(" | ")
                .Append(t.Gc1).Append(" | ")
                .Append(t.Gc2).Append(" | ")
                .Append(t.Channel).Append(" | ")
                .Append(DescribeEffects(t.EffectFlags))
                .AppendLine();
        }

        File.WriteAllText(path, text.ToString(), Encoding.UTF8);
        _lastSavedPath = path;
        return path;
    }

    private static string DescribeEffects(int flags)
    {
        if (flags == 0) return "ninguno";
        var names = new List<string>(14);
        if ((flags & 1) != 0) names.Add("DS1");
        if ((flags & 2) != 0) names.Add("Overdrive");
        if ((flags & 4) != 0) names.Add("Booster");
        if ((flags & 8) != 0) names.Add("Chorus");
        if ((flags & 16) != 0) names.Add("Delay");
        if ((flags & 32) != 0) names.Add("Reverb");
        if ((flags & 64) != 0) names.Add("Gate");
        if ((flags & 128) != 0) names.Add("Afinador");
        if ((flags & 256) != 0) names.Add("Compresor");
        if ((flags & 512) != 0) names.Add("AutoWah");
        if ((flags & 1024) != 0) names.Add("Phaser");
        if ((flags & 2048) != 0) names.Add("Flanger");
        if ((flags & 4096) != 0) names.Add("VozCanal1");
        if ((flags & 8192) != 0) names.Add("SupresorVoz");
        if ((flags & 16384) != 0) names.Add("NAM");
        if ((flags & 32768) != 0) names.Add("ChorusMXR");
        if ((flags & 65536) != 0) names.Add("MicroPitch80s");
        return string.Join(',', names);
    }

    private readonly record struct AudioTelemetry(
        DateTime Time,
        long CallbackCount,
        double CallbackAgeMilliseconds,
        int LastLoad,
        int MaxLoad,
        int MaxClean,
        int MaxCrunch,
        int MaxLead,
        int Underruns,
        int TotalErrors,
        int BufferErrors,
        int InputErrors,
        int DspErrors,
        int OutputErrors,
        int DriverResets,
        int BufferChanges,
        int BufferSize,
        long GcBytes,
        long WorkingSetBytes,
        int Gc0,
        int Gc1,
        int Gc2,
        AmpChannel Channel,
        int EffectFlags);
}
