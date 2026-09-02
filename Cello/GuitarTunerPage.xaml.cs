using System;
using Cello.Audio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cello.Tuning;

/// <summary>
/// Microphone tuner for standard six-string guitar tuning E2–A2–D3–G3–B3–E4.
/// </summary>
public sealed partial class GuitarTunerPage : Page
{
    private GuitarString _selectedString = GuitarString.LowE;

    public GuitarTunerPage()
    {
        InitializeComponent();
        Loaded += GuitarTunerPage_Loaded;
        Unloaded += GuitarTunerPage_Unloaded;
    }

    private void StringButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string midiText } || !int.TryParse(midiText, out int midiNote))
        {
            return;
        }

        _selectedString = GuitarString.FromMidiNote(midiNote);
        ResetTuningDisplay();
    }

    private void GuitarTunerPage_Loaded(object sender, RoutedEventArgs e)
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
            TunerStatus.Message = "Kein stabiler Ton erkannt. Saite einzeln anschlagen und ausklingen lassen.";
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
        _selectedString = GuitarString.NearestTo(frequency);
        LowEStringButton.IsChecked = _selectedString == GuitarString.LowE;
        AStringButton.IsChecked = _selectedString == GuitarString.A;
        DStringButton.IsChecked = _selectedString == GuitarString.D;
        GStringButton.IsChecked = _selectedString == GuitarString.G;
        BStringButton.IsChecked = _selectedString == GuitarString.B;
        HighEStringButton.IsChecked = _selectedString == GuitarString.HighE;
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
            ShowPlayingInstruction();
        }
    }

    private void ShowPlayingInstruction()
    {
        TunerStatus.Severity = InfoBarSeverity.Informational;
        TunerStatus.Message = $"Schlage die {_selectedString.Name}-Saite einzeln an und lasse sie ausklingen.";
    }

    private void GuitarTunerPage_Unloaded(object sender, RoutedEventArgs e)
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
