namespace GDMAmpAccessible.Models;

public enum TuningDirection
{
    NoSignal = 0,
    Flat = 1,
    InTune = 2,
    Sharp = 3
}

public sealed record TunerReading
{
    public static readonly TunerReading NoSignal = new()
    {
        HasSignal = false,
        Direction = TuningDirection.NoSignal,
        DisplayText = "Sin señal estable. Toque una cuerda y déjela sonar."
    };

    public bool HasSignal { get; init; }
    public float FrequencyHz { get; init; }
    public int MidiNote { get; init; }
    public string NoteName { get; init; } = string.Empty;
    public int Octave { get; init; }
    public float Cents { get; init; }
    public float Confidence { get; init; }
    public TuningDirection Direction { get; init; }
    public string DisplayText { get; init; } = string.Empty;
}
