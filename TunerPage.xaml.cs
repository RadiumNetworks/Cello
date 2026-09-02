using System;
using Cello.Audio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cello.Tuning;

/// <summary>
/// Microphone-driven tuner for the four strings of a standard cello.
/// NAudio capture remains local to this page; tuning definitions are isolated
/// in CelloString.cs and pitch analysis remains in PitchDetector.cs.
/// </summary>
public sealed partial class TunerPage : Page
{
    private CelloString _selectedString = CelloString.C;

    public TunerPage()
    {
        InitializeComponent();
        Loaded += TunerPage_Loaded;
        Unloaded += TunerPage_Unloaded;
    }

    private void StringButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string midiText } || !int.TryParse(midiText, out int midiNote))
        {
            return;
        }

        _selectedString = CelloString.FromMidiNote(midiNote);
        ResetTuningDisplay();
    }

    private void TunerPage_Loaded(object sender, RoutedEventArgs e)
    {
        App.Microphone.AnalysisAvailable += Microphone_AnalysisAvailable;
        App.Microphone.ActivityChanged += Microphone_ActivityChanged;
        UpdateMicrophoneStatus();
    }

    private void Microphone_AnalysisAvailable(object? sender, MicrophoneAnalysisEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => DisplayTuning(e.Pitch));
    }

    private void Microphone_ActivityChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateMicrophoneStatus);
    }

    private void DisplayTuning(PitchResult? pitch)
    {
        if (pitch is null)
        {
            TunerStatus.Severity = InfoBarSeverity.Warning;
            TunerStatus.Message = "Kein stabiler Ton erkannt. Saite länger oder etwas lauter streichen.";
            return;
        }

        SelectNearestString(pitch.Frequency);
        double cents = 1200 * Math.Log2(pitch.Frequency / _selectedString.Frequency);
        DetectedFrequencyText.Text = $"{pitch.Frequency:F2} Hz · Ziel {_selectedString.Frequency:F2} Hz";
        TuningSlider.Value = Math.Clamp(cents, -50, 50);
        CentsText.Text = cents switch
        {
            < -1 => $"{Math.Abs(cents):F1} Cent zu tief",
            > 1 => $"{cents:F1} Cent zu hoch",
            _ => "0 Cent · gestimmt"
        };

        if (Math.Abs(cents) <= 5)
        {
            TunerStatus.Severity = InfoBarSeverity.Success;
            TunerStatus.Message = $"Die {_selectedString.Name}-Saite ist gestimmt.";
        }
        else if (Math.Abs(cents) > 100)
        {
            TunerStatus.Severity = InfoBarSeverity.Error;
            TunerStatus.Message = $"Der erkannte Ton liegt weit von {_selectedString.Name} entfernt. Prüfe die gewählte Saite.";
        }
        else
        {
            TunerStatus.Severity = InfoBarSeverity.Warning;
            TunerStatus.Message = cents < 0 ? "Saite vorsichtig höher stimmen." : "Saite vorsichtig tiefer stimmen.";
        }
    }

    private void SelectNearestString(double frequency)
    {
        _selectedString = CelloString.NearestTo(frequency);
        CStringButton.IsChecked = _selectedString == CelloString.C;
        GStringButton.IsChecked = _selectedString == CelloString.G;
        DStringButton.IsChecked = _selectedString == CelloString.D;
        AStringButton.IsChecked = _selectedString == CelloString.A;
        TargetStringText.Text = _selectedString.Name;
    }

    private void ResetTuningDisplay()
    {
        TargetStringText.Text = _selectedString.Name;
        DetectedFrequencyText.Text = $"Ziel: {_selectedString.Frequency:F2} Hz";
        TuningSlider.Value = 0;
        CentsText.Text = "– Cent";

        if (App.Microphone.IsActive)
        {
            TunerStatus.Severity = InfoBarSeverity.Informational;
            TunerStatus.Message = $"Streiche die {_selectedString.Name}-Saite einzeln und gleichmäßig.";
        }
    }

    private void TunerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        App.Microphone.AnalysisAvailable -= Microphone_AnalysisAvailable;
        App.Microphone.ActivityChanged -= Microphone_ActivityChanged;
    }

    private void UpdateMicrophoneStatus()
    {
        ResetTuningDisplay();
        if (!App.Microphone.IsActive)
        {
            TunerStatus.Severity = InfoBarSeverity.Informational;
            TunerStatus.Message = "Aktiviere das Mikrofon oben im App-Rahmen.";
        }
    }
}
