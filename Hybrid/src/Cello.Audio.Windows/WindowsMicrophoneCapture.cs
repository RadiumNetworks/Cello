using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Cello.Audio.Windows;

/// <summary>
/// Captures 48 kHz, 16-bit mono PCM from the default Windows input device and
/// publishes analysis snapshots at no more than roughly 13.3 Hz.
/// </summary>
public sealed class WindowsMicrophoneCapture : IMicrophoneCapture
{
    private const int BufferMilliseconds = 30;
    private static readonly long AnalysisIntervalTicks = Stopwatch.Frequency * 75 / 1000;

    private WasapiRecorder? _recorder;
    private PitchDetector? _pitchDetector;
    private AudioSignalAnalyzer? _signalAnalyzer;
    private long _lastAnalysisTimestamp;
    private long _lastCallbackTimestamp;
    private long _callbackCount;
    private long _analysisCount;
    private long _estimatedDropoutCount;
    private long _callbackGapTicks;
    private long _maximumCallbackGapTicks;
    private long _analysisElapsedTicks;
    private long _maximumAnalysisElapsedTicks;
    private long _allocatedBytesAtStart;
    private int _gen0CollectionsAtStart;

    public bool IsActive => _recorder is not null;
    public string? InputName => _recorder?.DeviceFriendlyName;

    public AudioCaptureDiagnostics Diagnostics
    {
        get
        {
            long callbacks = Interlocked.Read(ref _callbackCount);
            long analyses = Interlocked.Read(ref _analysisCount);
            return new AudioCaptureDiagnostics(
                callbacks,
                analyses,
                Interlocked.Read(ref _estimatedDropoutCount),
                callbacks > 1 ? ToMilliseconds(Interlocked.Read(ref _callbackGapTicks)) / (callbacks - 1) : 0,
                ToMilliseconds(Interlocked.Read(ref _maximumCallbackGapTicks)),
                analyses > 0 ? ToMilliseconds(Interlocked.Read(ref _analysisElapsedTicks)) / analyses : 0,
                ToMilliseconds(Interlocked.Read(ref _maximumAnalysisElapsedTicks)),
                Math.Max(0, GC.GetTotalAllocatedBytes(false) - Interlocked.Read(ref _allocatedBytesAtStart)),
                Math.Max(0, GC.CollectionCount(0) - Volatile.Read(ref _gen0CollectionsAtStart)));
        }
    }

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
            .WithBufferLength(BufferMilliseconds)
            .WithDefaultDeviceStreamRouting()
            .BuildAsync();

        _pitchDetector = new PitchDetector(recorder.WaveFormat.SampleRate);
        _signalAnalyzer = new AudioSignalAnalyzer(recorder.WaveFormat.SampleRate);
        _lastAnalysisTimestamp = 0;
        ResetDiagnostics();
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
        long callbackStarted = Stopwatch.GetTimestamp();
        long previousCallback = Interlocked.Exchange(ref _lastCallbackTimestamp, callbackStarted);
        if (previousCallback != 0)
        {
            long gap = callbackStarted - previousCallback;
            Interlocked.Add(ref _callbackGapTicks, gap);
            UpdateMaximum(ref _maximumCallbackGapTicks, gap);
            if (ToMilliseconds(gap) > BufferMilliseconds * 2.5)
            {
                Interlocked.Increment(ref _estimatedDropoutCount);
            }
        }
        Interlocked.Increment(ref _callbackCount);

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

        long elapsed = Stopwatch.GetTimestamp() - callbackStarted;
        Interlocked.Increment(ref _analysisCount);
        Interlocked.Add(ref _analysisElapsedTicks, elapsed);
        UpdateMaximum(ref _maximumAnalysisElapsedTicks, elapsed);
    }

    private void ResetDiagnostics()
    {
        _lastCallbackTimestamp = 0;
        _callbackCount = 0;
        _analysisCount = 0;
        _estimatedDropoutCount = 0;
        _callbackGapTicks = 0;
        _maximumCallbackGapTicks = 0;
        _analysisElapsedTicks = 0;
        _maximumAnalysisElapsedTicks = 0;
        _allocatedBytesAtStart = GC.GetTotalAllocatedBytes(false);
        _gen0CollectionsAtStart = GC.CollectionCount(0);
    }

    private static double ToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

    private static void UpdateMaximum(ref long target, long value)
    {
        long current;
        while (value > (current = Volatile.Read(ref target)) &&
               Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
