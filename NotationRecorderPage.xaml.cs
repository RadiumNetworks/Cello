using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Cello.Audio;
using Cello.Notation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Cello;

/// <summary>
/// Records the shared pitch stream as a growing score and keeps an optional
/// MusicXML file synchronized with the current recording.
/// </summary>
public sealed partial class NotationRecorderPage : Page
{
    private static readonly long PitchHoldTicks = Stopwatch.Frequency * 450 / 1000;
    private static readonly long AutoSaveIntervalTicks = Stopwatch.Frequency * 750 / 1000;

    private readonly List<RecordedTone> _tones = [];
    private readonly PitchStabilizer _pitchStabilizer = new();
    private RecordedTone? _activeTone;
    private StorageFile? _saveFile;
    private int? _candidateMidiNote;
    private PitchResult? _candidatePitch;
    private long _candidateStartTimestamp;
    private long _lastPitchTimestamp;
    private long _lastAutoSaveTimestamp;
    private int _candidateHits;
    private bool _isRecording;
    private bool _isSaving;
    private bool _saveRequested;

    public NotationRecorderPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        Loaded += NotationRecorderPage_Loaded;
        Unloaded += NotationRecorderPage_Unloaded;
    }

    private void NotationRecorderPage_Loaded(object sender, RoutedEventArgs e)
    {
        App.Microphone.AnalysisAvailable += Microphone_AnalysisAvailable;
        App.Microphone.ActivityChanged += Microphone_ActivityChanged;
        UpdateMicrophoneStatus();
        RefreshNotation(false);
    }

    private void NotationRecorderPage_Unloaded(object sender, RoutedEventArgs e)
    {
        App.Microphone.AnalysisAvailable -= Microphone_AnalysisAvailable;
        App.Microphone.ActivityChanged -= Microphone_ActivityChanged;
        StopRecording();
    }

    private void Microphone_ActivityChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!App.Microphone.IsActive && _isRecording)
            {
                StopRecording();
            }

            UpdateMicrophoneStatus();
        });
    }

    private void Microphone_AnalysisAvailable(object? sender, MicrophoneAnalysisEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => ProcessAnalysis(e.Pitch, e.Timestamp));
    }

    private void ProcessAnalysis(PitchResult? pitch, long timestamp)
    {
        pitch = pitch is null ? null : _pitchStabilizer.Stabilize(pitch);
        if (pitch is not null)
        {
            CurrentPitchText.Text = $"{pitch.NoteName} · {pitch.Frequency:F1} Hz";
            _lastPitchTimestamp = timestamp;
        }
        else if (_lastPitchTimestamp == 0 || timestamp - _lastPitchTimestamp >= PitchHoldTicks)
        {
            CurrentPitchText.Text = "–";
            _pitchStabilizer.Reset();
        }

        if (!_isRecording)
        {
            return;
        }

        if (pitch is null)
        {
            ResetCandidate();
            if (_activeTone is not null && timestamp - _lastPitchTimestamp >= PitchHoldTicks)
            {
                _activeTone.Finish(_lastPitchTimestamp);
                _activeTone = null;
                RefreshNotation(false);
                QueueAutoSave();
            }
            return;
        }

        if (_activeTone?.Pitch.MidiNote == pitch.MidiNote)
        {
            _activeTone.Update(pitch, timestamp);
            ResetCandidate();
            if (timestamp - _lastAutoSaveTimestamp >= AutoSaveIntervalTicks)
            {
                _lastAutoSaveTimestamp = timestamp;
                QueueAutoSave();
            }
            return;
        }

        if (_candidateMidiNote == pitch.MidiNote)
        {
            _candidateHits++;
            _candidatePitch = pitch;
        }
        else
        {
            _candidateMidiNote = pitch.MidiNote;
            _candidatePitch = pitch;
            _candidateStartTimestamp = timestamp;
            _candidateHits = 1;
        }

        // Requiring two consecutive analyses suppresses isolated pitch glitches.
        if (_candidateHits < 2 || _candidatePitch is null)
        {
            return;
        }

        _activeTone?.Finish(_candidateStartTimestamp);
        _activeTone = new RecordedTone(_candidatePitch, _candidateStartTimestamp);
        _activeTone.Update(pitch, timestamp);
        _tones.Add(_activeTone);
        ResetCandidate();
        _lastAutoSaveTimestamp = timestamp;
        RefreshNotation(true);
        QueueAutoSave();
    }

    private void StartRecordingButton_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Microphone.IsActive)
        {
            RecordingStatus.Severity = InfoBarSeverity.Warning;
            RecordingStatus.Message = "Aktiviere zuerst das Mikrofon oben im App-Rahmen.";
            return;
        }

        _isRecording = true;
        _pitchStabilizer.Reset();
        _lastPitchTimestamp = 0;
        ResetCandidate();
        StartRecordingButton.IsEnabled = false;
        StopRecordingButton.IsEnabled = true;
        RecordingStatus.Severity = InfoBarSeverity.Success;
        RecordingStatus.Message = _saveFile is null
            ? "Aufnahme läuft. Wähle eine MusicXML-Datei, um zusätzlich fortlaufend zu speichern."
            : "Aufnahme und fortlaufende MusicXML-Speicherung laufen.";
    }

    private void StopRecordingButton_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
        RecordingStatus.Severity = InfoBarSeverity.Informational;
        RecordingStatus.Message = "Aufnahme beendet. Die bisherige Notation bleibt erhalten.";
    }

    private void StopRecording()
    {
        if (!_isRecording)
        {
            return;
        }

        _activeTone?.Finish(_lastPitchTimestamp > 0 ? _lastPitchTimestamp : Stopwatch.GetTimestamp());
        _activeTone = null;
        _isRecording = false;
        _pitchStabilizer.Reset();
        ResetCandidate();
        StartRecordingButton.IsEnabled = true;
        StopRecordingButton.IsEnabled = false;
        RefreshNotation(false);
        QueueAutoSave();
    }

    private async void ChooseFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = $"Cello-Aufnahme-{DateTime.Now:yyyyMMdd-HHmmss}"
        };
        picker.FileTypeChoices.Add("MusicXML", [".musicxml"]);

        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        _saveFile = file;
        SaveFileText.Text = $"Fortlaufende Speicherung: {file.Path}";
        QueueAutoSave();
        RecordingStatus.Severity = InfoBarSeverity.Success;
        RecordingStatus.Message = _isRecording
            ? "Die Aufnahme wird jetzt fortlaufend in der MusicXML-Datei gespeichert."
            : "Speicherdatei festgelegt. Änderungen an der Notation werden automatisch gespeichert.";
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _tones.Clear();
        _activeTone = null;
        _pitchStabilizer.Reset();
        _lastPitchTimestamp = 0;
        ResetCandidate();
        RefreshNotation(false);
        QueueAutoSave();
        RecordingStatus.Severity = InfoBarSeverity.Informational;
        RecordingStatus.Message = _isRecording
            ? "Notation gelöscht. Die laufende Aufnahme beginnt mit dem nächsten stabilen Ton neu."
            : "Notation und Inhalt der gewählten MusicXML-Datei wurden zurückgesetzt.";
    }

    private void RefreshNotation(bool scrollToEnd)
    {
        ContinuousNotation.UpdateNotes(_tones);
        ToneCountText.Text = _tones.Count == 1 ? "1 Ton" : $"{_tones.Count} Töne";
        ClearButton.IsEnabled = _tones.Count > 0;

        if (scrollToEnd)
        {
            ContinuousNotation.UpdateLayout();
            NotationScrollViewer.ChangeView(NotationScrollViewer.ScrollableWidth, null, null, true);
        }
    }

    private void ResetCandidate()
    {
        _candidateMidiNote = null;
        _candidatePitch = null;
        _candidateStartTimestamp = 0;
        _candidateHits = 0;
    }

    private void UpdateMicrophoneStatus()
    {
        if (App.Microphone.IsActive)
        {
            RecordingStatus.Severity = InfoBarSeverity.Success;
            RecordingStatus.Message = _isRecording
                ? "Aufnahme läuft. Spiele einzelne, klar getrennte Töne."
                : "Mikrofon aktiv. Starte die Aufnahme, um Töne fortlaufend zu notieren.";
        }
        else
        {
            CurrentPitchText.Text = "–";
            RecordingStatus.Severity = InfoBarSeverity.Informational;
            RecordingStatus.Message = "Aktiviere das Mikrofon oben im App-Rahmen und starte anschließend die Aufnahme.";
        }
    }

    private void QueueAutoSave()
    {
        if (_saveFile is null)
        {
            return;
        }

        _saveRequested = true;
        if (!_isSaving)
        {
            _ = SavePendingChangesAsync();
        }
    }

    private async Task SavePendingChangesAsync()
    {
        _isSaving = true;
        try
        {
            while (_saveRequested && _saveFile is not null)
            {
                _saveRequested = false;
                string musicXml = MusicXmlExporter.CreateRecordedScore(_tones);
                await FileIO.WriteTextAsync(_saveFile, musicXml);
            }

            if (_saveFile is not null)
            {
                SaveFileText.Text = $"Fortlaufend gespeichert: {_saveFile.Path} · {DateTime.Now:T}";
            }
        }
        catch (Exception ex)
        {
            RecordingStatus.Severity = InfoBarSeverity.Error;
            RecordingStatus.Message = $"MusicXML konnte nicht gespeichert werden: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
            if (_saveRequested)
            {
                _ = SavePendingChangesAsync();
            }
        }
    }
}
