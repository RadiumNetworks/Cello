using System;

namespace Cello.Tuning;

/// <summary>
/// Defines the standard cello tuning C2–G2–D3–A3 independently from the UI.
/// </summary>
public sealed record CelloString(string Name, int MidiNote, double Frequency)
{
    public static CelloString C { get; } = new("C2", 36, 65.41);
    public static CelloString G { get; } = new("G2", 43, 98.00);
    public static CelloString D { get; } = new("D3", 50, 146.83);
    public static CelloString A { get; } = new("A3", 57, 220.00);

    public static CelloString[] All { get; } = [C, G, D, A];

    public static CelloString NearestTo(double frequency)
    {
        CelloString nearest = All[0];
        double nearestDistance = double.MaxValue;

        foreach (CelloString candidate in All)
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

    public static CelloString FromMidiNote(int midiNote) => midiNote switch
    {
        36 => C,
        43 => G,
        50 => D,
        57 => A,
        _ => throw new ArgumentOutOfRangeException(nameof(midiNote))
    };
}
