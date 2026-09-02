using System;

namespace Cello.Tuning;

/// <summary>
/// Defines standard six-string guitar tuning from the lowest to highest string.
/// </summary>
public sealed record GuitarString(string Name, int MidiNote, double Frequency)
{
    public static GuitarString LowE { get; } = new("E2", 40, 82.41);
    public static GuitarString A { get; } = new("A2", 45, 110.00);
    public static GuitarString D { get; } = new("D3", 50, 146.83);
    public static GuitarString G { get; } = new("G3", 55, 196.00);
    public static GuitarString B { get; } = new("B3", 59, 246.94);
    public static GuitarString HighE { get; } = new("E4", 64, 329.63);

    public static GuitarString[] All { get; } = [LowE, A, D, G, B, HighE];

    public static GuitarString NearestTo(double frequency)
    {
        GuitarString nearest = All[0];
        double nearestDistance = double.MaxValue;

        foreach (GuitarString candidate in All)
        {
            double distance = Math.Abs(Math.Log2(frequency / candidate.Frequency));
            if (distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    public static GuitarString FromMidiNote(int midiNote) => midiNote switch
    {
        40 => LowE,
        45 => A,
        50 => D,
        55 => G,
        59 => B,
        64 => HighE,
        _ => throw new ArgumentOutOfRangeException(nameof(midiNote))
    };
}
