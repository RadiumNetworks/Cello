using Cello.Audio;

namespace Cello.Core.Tests;

public sealed class AudioSignalAnalyzerTests
{
    [Fact]
    public void Analyze_EmptyInputReturnsEmptySnapshot()
    {
        AudioSignalSnapshot snapshot = new AudioSignalAnalyzer(48000).Analyze();

        Assert.Equal(-120, snapshot.RmsDbFs);
        Assert.Equal(16, snapshot.Spectrum.Count);
        Assert.Equal(-1, snapshot.DominantBandIndex);
    }

    [Fact]
    public void Analyze_FullScaleInputReportsClipping()
    {
        var analyzer = new AudioSignalAnalyzer(48000);
        analyzer.AddSamples(Enumerable.Repeat(short.MaxValue, 2048).ToArray());

        AudioSignalSnapshot snapshot = analyzer.Analyze();

        Assert.True(snapshot.IsClipping);
        Assert.InRange(snapshot.PeakDbFs, -0.01, 0);
    }
}
