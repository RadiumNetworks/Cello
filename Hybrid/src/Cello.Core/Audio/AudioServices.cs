namespace Cello.Audio;

/// <summary>
/// Publishes timestamped, immutable analysis snapshots independently from the
/// platform-specific capture mechanism.
/// </summary>
public interface IAudioAnalysisStream
{
    bool IsActive { get; }

    event EventHandler<MicrophoneAnalysisEventArgs>? AnalysisAvailable;
}

/// <summary>
/// Controls the lifecycle of a platform microphone capture session.
/// </summary>
public interface IMicrophoneCapture : IAudioAnalysisStream, IDisposable
{
    string? InputName { get; }

    AudioCaptureDiagnostics Diagnostics { get; }

    event EventHandler? ActivityChanged;

    Task StartAsync();

    void Stop();
}

public sealed record AudioCaptureDiagnostics(
    long CallbackCount,
    long AnalysisCount,
    long EstimatedDropoutCount,
    double AverageCallbackGapMilliseconds,
    double MaximumCallbackGapMilliseconds,
    double AverageAnalysisMilliseconds,
    double MaximumAnalysisMilliseconds,
    long AllocatedBytes,
    int Gen0Collections)
{
    public static AudioCaptureDiagnostics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Plays monophonic MIDI notes through a platform-specific output.
/// </summary>
public interface IMidiPlayback : IDisposable
{
    string? OutputName { get; }

    bool TryInitialize(out string? errorMessage);

    void PlayNote(int midiNote, bool pizzicato = false);

    void PlayNotes(IReadOnlyList<int> midiNotes, bool pizzicato = false);

    void StopNote();

    void StopAll();
}

public sealed class MicrophoneAnalysisEventArgs(
    AudioSignalSnapshot signal,
    PitchResult? pitch,
    long timestamp) : EventArgs
{
    public AudioSignalSnapshot Signal { get; } = signal;

    public PitchResult? Pitch { get; } = pitch;

    public long Timestamp { get; } = timestamp;
}
