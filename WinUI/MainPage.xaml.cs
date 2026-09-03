using System;
using System.Diagnostics;
using Cello.Audio;
using Cello.Notation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Cello;

/// <summary>
/// The main content page displayed inside the application window.
/// Add your UI logic, event handlers, and data binding here.
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly PitchStabilizer _pitchStabilizer = new();
    private PitchResult? _currentPitch;
    private long _lastPitchTimestamp;

    private static readonly long PitchHoldTicks = Stopwatch.Frequency * 450 / 1000;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        App.Microphone.AnalysisAvailable += Microphone_AnalysisAvailable;
        App.Microphone.ActivityChanged += Microphone_ActivityChanged;
        SetMicrophoneStatus(
            App.Microphone.IsActive
                ? "Das Mikrofon ist aktiv. Spiele einen einzelnen, gehaltenen Ton."
                : "Das Mikrofon kann oben im App-Rahmen aktiviert werden.",
            App.Microphone.IsActive ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        App.Microphone.AnalysisAvailable -= Microphone_AnalysisAvailable;
        App.Microphone.ActivityChanged -= Microphone_ActivityChanged;
    }

    private void Microphone_ActivityChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (App.Microphone.IsActive)
            {
                SetMicrophoneStatus("Das Mikrofon ist aktiv. Spiele einen einzelnen, gehaltenen Ton.", InfoBarSeverity.Success);
            }
            else
            {
                _pitchStabilizer.Reset();
                ResetPitchDisplay();
                ResetSignalDisplay();
                SetMicrophoneStatus("Das Mikrofon kann oben im App-Rahmen aktiviert werden.", InfoBarSeverity.Informational);
            }
        });
    }

    private void Microphone_AnalysisAvailable(object? sender, MicrophoneAnalysisEventArgs e)
    {
        PitchResult? pitch = e.Pitch is null ? null : _pitchStabilizer.Stabilize(e.Pitch);
        if (e.Pitch is not null)
        {
            _lastPitchTimestamp = e.Timestamp;
        }

        bool clearStalePitch = e.Pitch is null && e.Timestamp - _lastPitchTimestamp >= PitchHoldTicks;
        if (clearStalePitch)
        {
            _pitchStabilizer.Reset();
        }

        DispatcherQueue.TryEnqueue(() => DisplayAnalysis(e.Signal, pitch, clearStalePitch));
    }

    private void DisplayPitch(PitchResult pitch)
    {
        _currentPitch = pitch;
        DetectedNoteText.Text = pitch.NoteName;
        FrequencyText.Text = $"{pitch.Frequency:F1} Hz";
        TuningText.Text = pitch.Cents switch
        {
            < -5 => $"{Math.Abs(pitch.Cents):F0} Cent zu tief",
            > 5 => $"{pitch.Cents:F0} Cent zu hoch",
            _ => "Ton ist gestimmt"
        };
        ConfidenceBar.Value = pitch.Confidence * 100;
        StaffNotation.MidiNote = pitch.MidiNote;
        ExportMusicXmlButton.IsEnabled = true;
    }

    private void ResetPitchDisplay()
    {
        _currentPitch = null;
        DetectedNoteText.Text = "–";
        FrequencyText.Text = "Spiele einen einzelnen Ton";
        TuningText.Text = string.Empty;
        ConfidenceBar.Value = 0;
        StaffNotation.MidiNote = null;
        ExportMusicXmlButton.IsEnabled = false;
    }

    private void ResetSignalDisplay()
    {
        VolumeBar.Value = 0;
        VolumeText.Text = "−∞ dBFS";
        PeakText.Text = "Peak: −∞ dBFS";
        AudioSpectrum.UpdateSpectrum(AudioSignalSnapshot.Empty.Spectrum);
        AnalysisStatus.Severity = InfoBarSeverity.Informational;
        AnalysisStatus.Message = "Aktiviere das Mikrofon, um das Eingangssignal zu prüfen.";
    }

    private void DisplayAnalysis(AudioSignalSnapshot signal, PitchResult? pitch, bool clearStalePitch)
    {
        VolumeBar.Value = Math.Clamp(signal.RmsDbFs + 60, 0, 60);
        VolumeText.Text = signal.RmsDbFs <= -100 ? "−∞ dBFS" : $"{signal.RmsDbFs:F1} dBFS";
        PeakText.Text = signal.PeakDbFs <= -100 ? "Peak: −∞ dBFS" : $"Peak: {signal.PeakDbFs:F1} dBFS";
        AudioSpectrum.UpdateSpectrum(signal.Spectrum, signal.DominantBandIndex, signal.DominantFrequencyHz);

        if (pitch is not null)
        {
            DisplayPitch(pitch);
            AnalysisStatus.Severity = InfoBarSeverity.Success;
            AnalysisStatus.Message = $"Stabile Grundfrequenz erkannt ({pitch.Confidence:P0} Sicherheit).";
        }
        else if (signal.IsClipping)
        {
            AnalysisStatus.Severity = InfoBarSeverity.Error;
            AnalysisStatus.Message = "Eingang übersteuert. Mikrofonpegel oder Abstand reduzieren.";
        }
        else if (signal.IsTooQuiet)
        {
            AnalysisStatus.Severity = InfoBarSeverity.Warning;
            AnalysisStatus.Message = "Signal ist zu leise für eine zuverlässige Tonhöhenerkennung (unter −54 dBFS).";
        }
        else
        {
            AnalysisStatus.Severity = InfoBarSeverity.Warning;
            AnalysisStatus.Message = "Pegel ausreichend, aber keine stabile einzelne Grundfrequenz erkannt. Ton länger und gleichmäßiger halten.";
        }

        if (clearStalePitch)
        {
            ResetPitchDisplay();
        }
    }

    private async void ExportMusicXmlButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPitch is not PitchResult pitch)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedFileName = $"Cello-{pitch.NoteName.Replace('♯', '#')}"
        };
        picker.FileTypeChoices.Add("MusicXML", [".musicxml"]);

        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await FileIO.WriteTextAsync(file, MusicXmlExporter.CreateSingleNoteScore(pitch));
        SetMicrophoneStatus($"{pitch.NoteName} wurde als MusicXML exportiert.", InfoBarSeverity.Success);
    }

    private void SetMicrophoneStatus(string message, InfoBarSeverity severity)
    {
        MicrophoneStatus.Message = message;
        MicrophoneStatus.Severity = severity;
    }
}
