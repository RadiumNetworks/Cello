using Cello.Audio;

namespace Cello.Hybrid.Shared.Services;

/// <summary>
/// Bridges native audio events to Razor at a bounded update rate and retains a
/// short note history for the dashboard notation preview.
/// </summary>
public sealed class AudioDashboardService : IDisposable
{
    private static readonly TimeSpan UiInterval = TimeSpan.FromMilliseconds(1000d / 30);
    private readonly IMicrophoneCapture _microphone;
    private readonly IMidiPlayback _midi;
    private readonly object _sync = new();
    private readonly Timer _uiTimer;
    private readonly List<PitchResult> _recentPitches = [];
    private MicrophoneAnalysisEventArgs? _latestAnalysis;
    private int? _lastRecordedMidiNote;
    private bool _hasPendingUpdate;
    private bool _disposed;

    public AudioDashboardService(IMicrophoneCapture microphone, IMidiPlayback midi)
    {
        _microphone = microphone;
        _midi = midi;
        _microphone.AnalysisAvailable += Microphone_AnalysisAvailable;
        _microphone.ActivityChanged += Microphone_ActivityChanged;
        _uiTimer = new Timer(PublishPendingUpdate, null, UiInterval, UiInterval);
    }

    public event EventHandler? Updated;

    public bool IsActive => _microphone.IsActive;

    public string? InputName => _microphone.InputName;

    public AudioCaptureDiagnostics Diagnostics => _microphone.Diagnostics;

    public string? ErrorMessage { get; private set; }

    public MicrophoneAnalysisEventArgs? LatestAnalysis
    {
        get { lock (_sync) return _latestAnalysis; }
    }

    public IReadOnlyList<PitchResult> RecentPitches
    {
        get { lock (_sync) return _recentPitches.ToArray(); }
    }

    public async Task ToggleMicrophoneAsync()
    {
        ErrorMessage = null;
        try
        {
            if (_microphone.IsActive)
            {
                _microphone.Stop();
            }
            else
            {
                await _microphone.StartAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        QueueUpdate();
    }

    public void PlayDetectedNote()
    {
        PitchResult? pitch = LatestAnalysis?.Pitch;
        if (pitch is null)
        {
            ErrorMessage = "Für die Wiedergabe muss zuerst ein Ton erkannt werden.";
            QueueUpdate();
            return;
        }

        if (!_midi.TryInitialize(out string? error))
        {
            ErrorMessage = error;
            QueueUpdate();
            return;
        }

        ErrorMessage = null;
        _midi.PlayNote(pitch.MidiNote);
        QueueUpdate();
    }

    public void StopNote() => _midi.StopNote();

    public void ClearHistory()
    {
        lock (_sync)
        {
            _recentPitches.Clear();
            _lastRecordedMidiNote = null;
        }
        QueueUpdate();
    }

    private void Microphone_AnalysisAvailable(object? sender, MicrophoneAnalysisEventArgs e)
    {
        lock (_sync)
        {
            _latestAnalysis = e;
            if (e.Pitch is { } pitch && pitch.MidiNote != _lastRecordedMidiNote)
            {
                _recentPitches.Add(pitch);
                if (_recentPitches.Count > 8)
                {
                    _recentPitches.RemoveAt(0);
                }
                _lastRecordedMidiNote = pitch.MidiNote;
            }
            else if (e.Pitch is null)
            {
                _lastRecordedMidiNote = null;
            }
        }
        QueueUpdate();
    }

    private void Microphone_ActivityChanged(object? sender, EventArgs e) => QueueUpdate();

    private void QueueUpdate()
    {
        lock (_sync) _hasPendingUpdate = true;
    }

    private void PublishPendingUpdate(object? state)
    {
        bool publish;
        lock (_sync)
        {
            publish = _hasPendingUpdate;
            _hasPendingUpdate = false;
        }
        if (publish) Updated?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _uiTimer.Dispose();
        _microphone.AnalysisAvailable -= Microphone_AnalysisAvailable;
        _microphone.ActivityChanged -= Microphone_ActivityChanged;
        _midi.StopNote();
        GC.SuppressFinalize(this);
    }
}
