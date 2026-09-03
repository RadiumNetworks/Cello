namespace Cello.Audio;

/// <summary>
/// Computes metering and a compact logarithmic spectrum independently from
/// microphone capture, pitch detection, and UI rendering.
/// </summary>
public sealed class AudioSignalAnalyzer
{
    private static readonly double[] BandFrequencies =
    [
        65, 82, 110, 147, 196, 262, 349, 440,
        587, 784, 1047, 1397, 1865, 2489, 3322, 4435
    ];

    private readonly int _sampleRate;
    private readonly float[] _window = new float[2048];
    private int _sampleCount;

    public AudioSignalAnalyzer(int sampleRate)
    {
        _sampleRate = sampleRate;
    }

    public void AddSamples(ReadOnlySpan<short> samples)
    {
        if (samples.Length >= _window.Length)
        {
            samples = samples[^_window.Length..];
            _sampleCount = 0;
        }
        else if (_sampleCount + samples.Length > _window.Length)
        {
            int discard = _sampleCount + samples.Length - _window.Length;
            _window.AsSpan(discard, _sampleCount - discard).CopyTo(_window);
            _sampleCount -= discard;
        }

        foreach (short sample in samples)
        {
            _window[_sampleCount++] = sample / 32768f;
        }
    }

    public AudioSignalSnapshot Analyze()
    {
        if (_sampleCount == 0)
        {
            return AudioSignalSnapshot.Empty;
        }

        double sumSquares = 0;
        double peak = 0;
        ReadOnlySpan<float> samples = _window.AsSpan(0, _sampleCount);

        foreach (float sample in samples)
        {
            double absolute = Math.Abs(sample);
            peak = Math.Max(peak, absolute);
            sumSquares += sample * sample;
        }

        double rms = Math.Sqrt(sumSquares / samples.Length);
        var bands = new double[BandFrequencies.Length];
        int dominantBandIndex = -1;
        double dominantLevel = 0;
        for (int i = 0; i < BandFrequencies.Length; i++)
        {
            bands[i] = CalculateBandLevel(samples, BandFrequencies[i]);
            if (bands[i] > dominantLevel)
            {
                dominantLevel = bands[i];
                dominantBandIndex = i;
            }
        }

        double dominantFrequency = dominantBandIndex >= 0
            ? RefineDominantFrequency(samples, dominantBandIndex)
            : 0;

        return new AudioSignalSnapshot(
            ToDbFs(rms),
            ToDbFs(peak),
            peak >= 0.98,
            bands,
            dominantBandIndex,
            dominantFrequency);
    }

    private double RefineDominantFrequency(ReadOnlySpan<float> samples, int bandIndex)
    {
        double center = BandFrequencies[bandIndex];
        double lower = bandIndex == 0
            ? center / Math.Sqrt(BandFrequencies[1] / center)
            : Math.Sqrt(BandFrequencies[bandIndex - 1] * center);
        double upper = bandIndex == BandFrequencies.Length - 1
            ? center * Math.Sqrt(center / BandFrequencies[^2])
            : Math.Sqrt(center * BandFrequencies[bandIndex + 1]);

        const int steps = 12;
        double strongestFrequency = center;
        double strongestLevel = double.MinValue;
        for (int step = 0; step <= steps; step++)
        {
            double frequency = lower + (upper - lower) * step / steps;
            double level = CalculateBandLevel(samples, frequency);
            if (level > strongestLevel)
            {
                strongestLevel = level;
                strongestFrequency = frequency;
            }
        }

        return strongestFrequency;
    }

    private double CalculateBandLevel(ReadOnlySpan<float> samples, double frequency)
    {
        double coefficient = 2 * Math.Cos(2 * Math.PI * frequency / _sampleRate);
        double previous = 0;
        double previousPrevious = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            double window = samples.Length == 1
                ? 1
                : 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (samples.Length - 1));
            double current = samples[i] * window + coefficient * previous - previousPrevious;
            previousPrevious = previous;
            previous = current;
        }

        double power = previousPrevious * previousPrevious + previous * previous - coefficient * previous * previousPrevious;
        double magnitude = 2 * Math.Sqrt(Math.Max(0, power)) / samples.Length;
        return Math.Clamp((ToDbFs(magnitude) + 72) / 72, 0, 1);
    }

    private static double ToDbFs(double amplitude)
    {
        return amplitude <= 0.000001 ? -120 : 20 * Math.Log10(amplitude);
    }
}

public sealed record AudioSignalSnapshot(
    double RmsDbFs,
    double PeakDbFs,
    bool IsClipping,
    IReadOnlyList<double> Spectrum,
    int DominantBandIndex,
    double DominantFrequencyHz)
{
    public static AudioSignalSnapshot Empty { get; } = new(-120, -120, false, new double[16], -1, 0);

    public bool IsTooQuiet => RmsDbFs < -54;
}
