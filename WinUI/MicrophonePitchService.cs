using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Cello.Audio;

/// <summary>
/// Owns the single application-wide microphone stream. Pages subscribe to
/// analysis results but never open or close the audio device themselves.
/// </summary>
public sealed class MicrophonePitchService : IMicrophoneCapture
{
    private static readonly long AnalysisIntervalTicks = Stopwatch.Frequency * 75 / 1000;

    private WasapiRecorder? _recorder;
    private PitchDetector? _pitchDetector;
    private AudioSignalAnalyzer? _signalAnalyzer;
    private long _lastAnalysisTimestamp;

    public bool IsActive => _recorder is not null;

    public event EventHandler<MicrophoneAnalysisEventArgs>? AnalysisAvailable;
    public event EventHandler? ActivityChanged;

    public async Task StartAsync()
    {
        if (IsActive)
        {
            return;
        }

        var recorder = await new WasapiRecorderBuilder()
            .WithFormat(new WaveFormat(48000, 16, 1))
            .WithBufferLength(30)
            .WithDefaultDeviceStreamRouting()
            .BuildAsync();

        _pitchDetector = new PitchDetector(recorder.WaveFormat.SampleRate);
        _signalAnalyzer = new AudioSignalAnalyzer(recorder.WaveFormat.SampleRate);
        _lastAnalysisTimestamp = 0;

        recorder.DataAvailable += (buffer, _, _, _) => ProcessAudio(buffer);
        _recorder = recorder;

        try
        {
            recorder.StartRecording();
            ActivityChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            recorder.Dispose();
            _recorder = null;
            _pitchDetector = null;
            _signalAnalyzer = null;
            throw;
        }
    }

    public void Stop()
    {
        WasapiRecorder? recorder = _recorder;
        _recorder = null;

        if (recorder is not null)
        {
            recorder.StopRecording();
            recorder.Dispose();
        }

        _pitchDetector = null;
        _signalAnalyzer = null;

        if (recorder is not null)
        {
            ActivityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ProcessAudio(ReadOnlySpan<byte> buffer)
    {
        ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(buffer);
        _signalAnalyzer?.AddSamples(samples);

        long now = Stopwatch.GetTimestamp();
        bool analyzeNow = now - _lastAnalysisTimestamp >= AnalysisIntervalTicks;
        PitchResult? pitch = _pitchDetector?.AddSamples(samples, analyzeNow);

        if (!analyzeNow || _signalAnalyzer is null)
        {
            return;
        }

        _lastAnalysisTimestamp = now;
        AudioSignalSnapshot signal = _signalAnalyzer.Analyze();
        AnalysisAvailable?.Invoke(this, new MicrophoneAnalysisEventArgs(signal, pitch, now));
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
