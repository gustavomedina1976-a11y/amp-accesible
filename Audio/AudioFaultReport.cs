namespace GDMAmpAccessible.Audio;

/// <summary>
/// Informe de la primera falla de un incidente de audio. Se construye fuera del callback
/// para no agregar formateo de texto ni escritura en disco al hilo ASIO.
/// </summary>
internal sealed record AudioFaultReport(
    int Sequence,
    string UserMessage,
    string DiagnosticDetail);
