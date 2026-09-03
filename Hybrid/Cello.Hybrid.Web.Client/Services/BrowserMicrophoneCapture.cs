using System.Diagnostics;
using System.Runtime.InteropServices;
using Cello.Audio;
using Microsoft.JSInterop;

namespace Cello.Hybrid.Web.Client.Services;

public sealed class BrowserMicrophoneCapture(IJSRuntime jsRuntime) : IMicrophoneCapture
{
    private static readonly long AnalysisIntervalTicks = Stopwatch.Frequency * 75 / 1000;
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private IJSObjectReference? _module;
    private DotNetObjectReference<BrowserMicrophoneCapture>? _reference;
    private PitchDetector? _pitchDetector;
    private AudioSignalAnalyzer? _signalAnalyzer;
    private short[] _sampleBuffer = [];
    private long _lastAnalysisTimestamp;
    private long _lastCallbackTimestamp;
    private long _callbackCount;
    private long _analysisCount;
    private long _dropoutCount;
    private long _callbackGapTicks;
    private long _maximumCallbackGapTicks;
    private long _analysisElapsedTicks;
    private long _maximumAnalysisElapsedTicks;
    private bool _disposed;

    public bool IsActive { get; private set; }
    public string? InputName { get; private set; }

    public AudioCaptureDiagnostics Diagnostics
    {
        get
        {
            long callbacks = Interlocked.Read(ref _callbackCount);
            long analyses = Interlocked.Read(ref _analysisCount);
            return new(
                callbacks,
                analyses,
                Interlocked.Read(ref _dropoutCount),
                callbacks > 1 ? ToMilliseconds(Interlocked.Read(ref _callbackGapTicks)) / (callbacks - 1) : 0,
                ToMilliseconds(Interlocked.Read(ref _maximumCallbackGapTicks)),
                analyses > 0 ? ToMilliseconds(Interlocked.Read(ref _analysisElapsedTicks)) / analyses : 0,
                ToMilliseconds(Interlocked.Read(ref _maximumAnalysisElapsedTicks)),
                0,
                0);
        }
    }

    public event EventHandler<MicrophoneAnalysisEventArgs>? AnalysisAvailable;
    public event EventHandler? ActivityChanged;

    public async Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsActive) return;

        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./browserAudio.js");
        _reference ??= DotNetObjectReference.Create(this);
        BrowserAudioStartInfo info = await _module.InvokeAsync<BrowserAudioStartInfo>("startMicrophone", _reference);

        _pitchDetector = new PitchDetector(info.SampleRate, windowSize: 2048);
        _signalAnalyzer = new AudioSignalAnalyzer(info.SampleRate);
        _lastAnalysisTimestamp = 0;
        ResetDiagnostics();
        InputName = string.IsNullOrWhiteSpace(info.DeviceName) ? "Browser-Mikrofon" : info.DeviceName;
        IsActive = true;
        ActivityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (!IsActive) return;

        IsActive = false;
        InputName = null;
        _pitchDetector = null;
        _signalAnalyzer = null;
        if (_module is not null)
        {
            _ = _module.InvokeVoidAsync("stopMicrophone");
        }
        ActivityChanged?.Invoke(this, EventArgs.Empty);
    }

    [JSInvokable]
    public void ReceiveAudio(byte[] sampleBytes, int sampleRate)
    {
        if (!IsActive || _pitchDetector is null || _signalAnalyzer is null) return;
        if (sampleBytes.Length % sizeof(float) != 0) return;

        long started = Stopwatch.GetTimestamp();
        ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(sampleBytes);
        RecordCallback(started, samples.Length * 1000d / sampleRate);
        if (_sampleBuffer.Length != samples.Length) _sampleBuffer = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            _sampleBuffer[i] = (short)Math.Round(Math.Clamp(samples[i], -1, 1) * short.MaxValue);
        }

        _signalAnalyzer.AddSamples(_sampleBuffer);
        bool analyze = started - _lastAnalysisTimestamp >= AnalysisIntervalTicks;
        PitchResult? pitch = _pitchDetector.AddSamples(_sampleBuffer, analyze);
        if (!analyze) return;

        _lastAnalysisTimestamp = started;
        AudioSignalSnapshot signal = _signalAnalyzer.Analyze();
        AnalysisAvailable?.Invoke(this, new MicrophoneAnalysisEventArgs(signal, pitch, started));
        long elapsed = Stopwatch.GetTimestamp() - started;
        Interlocked.Increment(ref _analysisCount);
        Interlocked.Add(ref _analysisElapsedTicks, elapsed);
        UpdateMaximum(ref _maximumAnalysisElapsedTicks, elapsed);
    }

    [JSInvokable]
    public void CaptureEnded()
    {
        if (!IsActive) return;
        IsActive = false;
        InputName = null;
        ActivityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RecordCallback(long timestamp, double expectedMilliseconds)
    {
        long previous = Interlocked.Exchange(ref _lastCallbackTimestamp, timestamp);
        if (previous != 0)
        {
            long gap = timestamp - previous;
            Interlocked.Add(ref _callbackGapTicks, gap);
            UpdateMaximum(ref _maximumCallbackGapTicks, gap);
            if (ToMilliseconds(gap) > expectedMilliseconds * 2.5) Interlocked.Increment(ref _dropoutCount);
        }
        Interlocked.Increment(ref _callbackCount);
    }

    private void ResetDiagnostics()
    {
        _lastCallbackTimestamp = 0;
        _callbackCount = 0;
        _analysisCount = 0;
        _dropoutCount = 0;
        _callbackGapTicks = 0;
        _maximumCallbackGapTicks = 0;
        _analysisElapsedTicks = 0;
        _maximumAnalysisElapsedTicks = 0;
    }

    private static double ToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

    private static void UpdateMaximum(ref long target, long value)
    {
        long current;
        while (value > (current = Volatile.Read(ref target)) &&
               Interlocked.CompareExchange(ref target, value, current) != current) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _reference?.Dispose();
        if (_module is not null) _ = _module.DisposeAsync();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed record BrowserAudioStartInfo(int SampleRate, string? DeviceName);
}