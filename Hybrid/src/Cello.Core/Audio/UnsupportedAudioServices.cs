namespace Cello.Audio;

/// <summary>
/// Safe fallback for hosts that do not yet provide native microphone access.
/// </summary>
public sealed class UnsupportedMicrophoneCapture : IMicrophoneCapture
{
    public bool IsActive => false;
    public string? InputName => null;
    public AudioCaptureDiagnostics Diagnostics => AudioCaptureDiagnostics.Empty;

    public event EventHandler<MicrophoneAnalysisEventArgs>? AnalysisAvailable
    {
        add { }
        remove { }
    }

    public event EventHandler? ActivityChanged
    {
        add { }
        remove { }
    }

    public Task StartAsync() => Task.FromException(
        new PlatformNotSupportedException("Mikrofonanalyse ist auf dieser Plattform noch nicht verfügbar."));

    public void Stop() { }

    public void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// Safe fallback for hosts that do not yet provide native MIDI playback.
/// </summary>
public sealed class UnsupportedMidiPlayback : IMidiPlayback
{
    public string? OutputName => null;

    public bool TryInitialize(out string? errorMessage)
    {
        errorMessage = "MIDI-Wiedergabe ist auf dieser Plattform noch nicht verfügbar.";
        return false;
    }

    public void PlayNote(int midiNote, bool pizzicato = false) { }

    public void PlayNotes(IReadOnlyList<int> midiNotes, bool pizzicato = false) { }

    public void StopNote() { }

    public void StopAll() { }

    public void Dispose() => GC.SuppressFinalize(this);
}
