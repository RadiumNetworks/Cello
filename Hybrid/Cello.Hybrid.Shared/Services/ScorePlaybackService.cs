using System.Diagnostics;
using Cello.Audio;
using Cello.Notation;
using Cello.Playback;

namespace Cello.Hybrid.Shared.Services;

/// <summary>
/// Owns an imported MusicXML score, practice-range selection and timed MIDI
/// playback independently from the Razor component lifecycle.
/// </summary>
public sealed class ScorePlaybackService : IDisposable
{
    private readonly IMidiPlayback _midi;
    private readonly object _sync = new();
    private readonly PracticeRangeState _range = new();
    private readonly Stopwatch _clock = new();
    private readonly Timer _timer;
    private readonly List<double> _toneEndTimes = [];
    private double _elapsedSeconds;
    private int _playingToneIndex = -1;
    private bool _isPlaying;
    private bool _disposed;

    public ScorePlaybackService(IMidiPlayback midi)
    {
        _midi = midi;
        _timer = new Timer(Tick, null, TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30));
    }

    public event EventHandler? Updated;

    public MusicXmlScore? Score { get; private set; }
    public string? FileName { get; private set; }
    public string? ErrorMessage { get; private set; }
    public double TempoPercent { get; private set; } = 100;
    public bool IsPlaying { get { lock (_sync) return _isPlaying; } }
    public int PlayingToneIndex { get { lock (_sync) return _playingToneIndex; } }
    public int? RangeStartIndex { get { lock (_sync) return _range.StartIndex; } }
    public int? RangeEndIndex { get { lock (_sync) return _range.EndIndex; } }
    public bool IsRangeComplete { get { lock (_sync) return _range.IsComplete; } }
    public bool IsLooping { get { lock (_sync) return _range.IsLooping; } }

    public async Task LoadAsync(Stream stream, string fileName)
    {
        try
        {
            using var reader = new StreamReader(stream);
            string xml = await reader.ReadToEndAsync();
            MusicXmlScore score = MusicXmlReader.Read(xml);

            lock (_sync)
            {
                StopCore();
                Score = score;
                FileName = fileName;
                ErrorMessage = score.Tones.Count == 0 ? "Die Partitur enthält keine abspielbaren Noten." : null;
                TempoPercent = 100;
                _range.Reset(score.Tones.Count);
                BuildTimeline(score);
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or System.Xml.XmlException)
        {
            lock (_sync)
            {
                StopCore();
                Score = null;
                FileName = null;
                ErrorMessage = $"MusicXML konnte nicht geladen werden: {ex.Message}";
                _range.Reset(0);
                _toneEndTimes.Clear();
            }
        }

        NotifyUpdated();
    }

    public void TogglePlayback()
    {
        lock (_sync)
        {
            if (_isPlaying)
            {
                AccumulateTime();
                _isPlaying = false;
                _clock.Reset();
                _midi.StopNote();
            }
            else if (Score is { Tones.Count: > 0 } score)
            {
                if (!_midi.TryInitialize(out string? error))
                {
                    ErrorMessage = error;
                }
                else
                {
                    ErrorMessage = null;
                    int start = _playingToneIndex >= _range.PlaybackStartIndex && _playingToneIndex <= _range.PlaybackEndIndex
                        ? _playingToneIndex
                        : _range.PlaybackStartIndex;
                    SetPositionCore(start);
                    _isPlaying = true;
                    PlayTone(score.Tones[start]);
                    _clock.Restart();
                }
            }
        }
        NotifyUpdated();
    }

    public void Stop()
    {
        lock (_sync) StopCore();
        NotifyUpdated();
    }

    public void ReportError(string message)
    {
        lock (_sync) ErrorMessage = message;
        NotifyUpdated();
    }

    public void SelectTone(int toneIndex)
    {
        lock (_sync)
        {
            if (Score is null || toneIndex < 0 || toneIndex >= Score.Tones.Count) return;
            StopCore();
            _range.SelectTone(toneIndex);
        }
        NotifyUpdated();
    }

    public void ClearRange()
    {
        lock (_sync)
        {
            StopCore();
            _range.Clear();
        }
        NotifyUpdated();
    }

    public void SetLooping(bool enabled)
    {
        lock (_sync) _range.SetLooping(enabled);
        NotifyUpdated();
    }

    public void SetTempo(double percent)
    {
        lock (_sync)
        {
            AccumulateTime();
            TempoPercent = Math.Clamp(percent, 25, 250);
        }
        NotifyUpdated();
    }

    private void Tick(object? state)
    {
        bool changed = false;
        lock (_sync)
        {
            if (!_isPlaying || Score is null || _playingToneIndex < 0) return;
            AccumulateTime();
            int previous = _playingToneIndex;
            int end = _range.PlaybackEndIndex;

            while (_playingToneIndex <= end && _elapsedSeconds >= _toneEndTimes[_playingToneIndex])
            {
                _playingToneIndex++;
            }

            if (_playingToneIndex > end)
            {
                if (_range.IsLooping)
                {
                    SetPositionCore(_range.PlaybackStartIndex);
                    PlayTone(Score.Tones[_playingToneIndex]);
                    _clock.Restart();
                }
                else
                {
                    StopCore();
                }
                changed = true;
            }
            else if (_playingToneIndex != previous)
            {
                PlayTone(Score.Tones[_playingToneIndex]);
                changed = true;
            }
        }
        if (changed) NotifyUpdated();
    }

    private void BuildTimeline(MusicXmlScore score)
    {
        _toneEndTimes.Clear();
        double elapsed = 0;
        for (int index = 0; index < score.Tones.Count; index++)
        {
            RecordedTone tone = score.Tones[index];
            if (!tone.IsChordContinuation)
            {
                elapsed += Math.Max(0.03, tone.Duration.TotalSeconds);
            }
            _toneEndTimes.Add(elapsed);
        }
    }

    private void AccumulateTime()
    {
        if (!_clock.IsRunning) return;
        _elapsedSeconds += _clock.Elapsed.TotalSeconds * TempoPercent / 100;
        _clock.Restart();
    }

    private void SetPositionCore(int toneIndex)
    {
        if (Score is not null)
        {
            while (toneIndex > 0 && Score.Tones[toneIndex].IsChordContinuation) toneIndex--;
        }
        _playingToneIndex = toneIndex;
        _elapsedSeconds = toneIndex > 0 ? _toneEndTimes[toneIndex - 1] : 0;
    }

    private void PlayTone(RecordedTone tone)
    {
        if (Score is null) return;
        int start = _playingToneIndex;
        while (start > 0 && Score.Tones[start].IsChordContinuation) start--;
        int end = start + 1;
        while (end < Score.Tones.Count && Score.Tones[end].IsChordContinuation) end++;
        int[] notes = Score.Tones.Skip(start).Take(end - start).Select(item => item.Pitch.MidiNote).ToArray();
        _midi.PlayNotes(notes, Score.Tones[start].IsPizzicato);
    }

    private void StopCore()
    {
        _isPlaying = false;
        _playingToneIndex = -1;
        _elapsedSeconds = 0;
        _clock.Reset();
        _midi.StopAll();
    }

    private void NotifyUpdated() => Updated?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        lock (_sync) StopCore();
        GC.SuppressFinalize(this);
    }
}
