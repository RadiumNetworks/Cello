using System;

namespace Cello;

/// <summary>
/// Estimates the fundamental frequency of a monophonic signal with the YIN
/// difference function. YIN is more reliable for overtone-rich cello notes
/// than selecting the strongest FFT bin, which is often a harmonic.
/// </summary>
public sealed class PitchDetector
{
    private const double YinThreshold = 0.15;
    private const double SilenceThreshold = 0.002;

    private readonly int _sampleRate;
    private readonly float[] _samples;
    private readonly double[] _difference;
    private readonly int _minimumLag;
    private readonly int _maximumLag;
    private int _sampleCount;

    public PitchDetector(int sampleRate, int windowSize = 4096, double minimumFrequency = 55, double maximumFrequency = 1200)
    {
        _sampleRate = sampleRate;
        _samples = new float[windowSize];
        _minimumLag = Math.Max(2, (int)(sampleRate / maximumFrequency));
        _maximumLag = Math.Min(windowSize / 2, (int)(sampleRate / minimumFrequency));
        _difference = new double[_maximumLag + 1];
    }

    public PitchResult? AddSamples(ReadOnlySpan<short> input, bool analyze = true)
    {
        if (input.Length >= _samples.Length)
        {
            input = input[^_samples.Length..];
            _sampleCount = 0;
        }
        else if (_sampleCount + input.Length > _samples.Length)
        {
            int samplesToDiscard = _sampleCount + input.Length - _samples.Length;
            _samples.AsSpan(samplesToDiscard, _sampleCount - samplesToDiscard).CopyTo(_samples);
            _sampleCount -= samplesToDiscard;
        }

        foreach (short value in input)
        {
            _samples[_sampleCount++] = value / 32768f;
        }

        if (_sampleCount < _samples.Length || !analyze)
        {
            return null;
        }

        return DetectPitch();
    }

    private PitchResult? DetectPitch()
    {
        double energy = 0;
        double mean = 0;

        foreach (float sample in _samples)
        {
            mean += sample;
        }

        mean /= _samples.Length;

        foreach (float sample in _samples)
        {
            double centered = sample - mean;
            energy += centered * centered;
        }

        double rms = Math.Sqrt(energy / _samples.Length);
        if (rms < SilenceThreshold)
        {
            return null;
        }

        Array.Clear(_difference);
        int comparisonLength = _samples.Length - _maximumLag;

        for (int lag = 1; lag <= _maximumLag; lag++)
        {
            double sum = 0;
            for (int i = 0; i < comparisonLength; i++)
            {
                double delta = _samples[i] - _samples[i + lag];
                sum += delta * delta;
            }

            _difference[lag] = sum;
        }

        double cumulative = 0;

        for (int lag = 1; lag <= _maximumLag; lag++)
        {
            cumulative += _difference[lag];
            _difference[lag] = cumulative == 0 ? 1 : _difference[lag] * lag / cumulative;
        }

        int selectedLag = -1;

        for (int lag = _minimumLag; lag <= _maximumLag; lag++)
        {
            if (lag >= _minimumLag && _difference[lag] < YinThreshold)
            {
                while (lag + 1 <= _maximumLag && _difference[lag + 1] < _difference[lag])
                {
                    lag++;
                }

                selectedLag = lag;
                break;
            }
        }

        if (selectedLag < 0)
        {
            return null;
        }

        double refinedLag = RefineLag(selectedLag);
        double frequency = _sampleRate / refinedLag;
        double confidence = Math.Clamp(1 - _difference[selectedLag], 0, 1);

        if (frequency is < 55 or > 1200 || confidence < 0.75)
        {
            return null;
        }

        return PitchResult.FromFrequency(frequency, confidence, rms);
    }

    private double RefineLag(int lag)
    {
        if (lag <= 1 || lag >= _maximumLag)
        {
            return lag;
        }

        double left = _difference[lag - 1];
        double center = _difference[lag];
        double right = _difference[lag + 1];
        double denominator = 2 * (2 * center - right - left);

        return Math.Abs(denominator) < double.Epsilon
            ? lag
            : lag + (right - left) / denominator;
    }
}

public sealed record PitchResult(
    string NoteName,
    int MidiNote,
    double Frequency,
    double Cents,
    double Confidence,
    double Rms)
{
    private static readonly string[] NoteNames =
    [
        "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "H"
    ];

    public static PitchResult FromFrequency(double frequency, double confidence, double rms)
    {
        double midiValue = 69 + 12 * Math.Log2(frequency / 440.0);
        int midiNote = (int)Math.Round(midiValue);
        return CreateForReferenceNote(frequency, confidence, rms, midiNote);
    }

    public PitchResult WithReferenceMidiNote(int midiNote)
    {
        return CreateForReferenceNote(Frequency, Confidence, Rms, midiNote);
    }

    private static PitchResult CreateForReferenceNote(double frequency, double confidence, double rms, int midiNote)
    {
        int noteIndex = ((midiNote % 12) + 12) % 12;
        int octave = midiNote / 12 - 1;
        double referenceFrequency = 440 * Math.Pow(2, (midiNote - 69) / 12.0);
        double cents = 1200 * Math.Log2(frequency / referenceFrequency);

        return new PitchResult(
            $"{NoteNames[noteIndex]}{octave}",
            midiNote,
            frequency,
            cents,
            confidence,
            rms);
    }
}
