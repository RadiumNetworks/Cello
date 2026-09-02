using System;

namespace Cello;

/// <summary>
/// Adds note-boundary hysteresis for analysis and transcription displays.
/// The tuner pages intentionally continue to use the unmodified detector result.
/// </summary>
public sealed class PitchStabilizer(double boundaryHysteresisCents = 15)
{
    private readonly double _switchBoundaryCents = 50 + Math.Max(0, boundaryHysteresisCents);
    private int? _currentMidiNote;

    public PitchResult Stabilize(PitchResult detectedPitch)
    {
        double measuredMidiNote = detectedPitch.MidiNote + detectedPitch.Cents / 100;

        if (_currentMidiNote is null)
        {
            _currentMidiNote = detectedPitch.MidiNote;
        }
        else
        {
            double distanceFromCurrentNoteCents = (measuredMidiNote - _currentMidiNote.Value) * 100;
            if (Math.Abs(distanceFromCurrentNoteCents) > _switchBoundaryCents)
            {
                _currentMidiNote = (int)Math.Round(measuredMidiNote);
            }
        }

        return detectedPitch.WithReferenceMidiNote(_currentMidiNote.Value);
    }

    public void Reset()
    {
        _currentMidiNote = null;
    }
}
