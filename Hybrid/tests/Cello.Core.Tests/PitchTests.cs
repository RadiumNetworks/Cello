namespace Cello.Core.Tests;

public sealed class PitchTests
{
    [Fact]
    public void PitchResult_MapsConcertA()
    {
        PitchResult pitch = PitchResult.FromFrequency(440, 0.95, 0.2);

        Assert.Equal(69, pitch.MidiNote);
        Assert.Equal("A4", pitch.NoteName);
        Assert.Equal(0, pitch.Cents, 6);
    }

    [Fact]
    public void PitchDetector_ReturnsNullForSilence()
    {
        var detector = new PitchDetector(48000, 2048);

        Assert.Null(detector.AddSamples(new short[2048]));
    }

    [Fact]
    public void PitchDetector_DetectsCelloAString()
    {
        const int sampleRate = 48000;
        var samples = new short[4096];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = (short)(12000 * Math.Sin(2 * Math.PI * 220 * index / sampleRate));
        }

        PitchResult? pitch = new PitchDetector(sampleRate).AddSamples(samples);

        Assert.NotNull(pitch);
        Assert.Equal(57, pitch.MidiNote);
        Assert.InRange(pitch.Frequency, 219, 221);
    }

    [Fact]
    public void PitchStabilizer_AppliesAndResetsHysteresis()
    {
        var stabilizer = new PitchStabilizer();
        PitchResult initial = PitchResult.FromFrequency(440, 1, 0.2);
        PitchResult nearBoundary = PitchResult.FromFrequency(452, 1, 0.2);

        Assert.Equal(69, stabilizer.Stabilize(initial).MidiNote);
        Assert.Equal(69, stabilizer.Stabilize(nearBoundary).MidiNote);

        stabilizer.Reset();

        Assert.Equal(69, stabilizer.Stabilize(nearBoundary).MidiNote);
    }
}
