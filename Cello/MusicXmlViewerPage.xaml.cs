using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Cello.Notation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Cello;

/// <summary>
/// Opens MusicXML files through the Windows picker and displays a simplified
/// score preview.
/// </summary>
public sealed partial class MusicXmlViewerPage : Page
{
    private MusicXmlScore? _currentScore;
    private readonly DispatcherTimer _playbackTimer = new();
    private readonly Stopwatch _playbackClock = new();
    private readonly List<double> _toneEndTimes = [];
    private readonly MidiPlaybackService _midiPlayback = new();
    private double _elapsedPlaybackSeconds;
    private double _playbackRate = 1;
    private int _playingToneIndex = -1;
    private bool _isPlaying;

    public MusicXmlViewerPage()
    {
        InitializeComponent();
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(30);
        _playbackTimer.Tick += PlaybackTimer_Tick;
        Unloaded += MusicXmlViewerPage_Unloaded;
    }

    private async void ChooseFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add(".musicxml");
        picker.FileTypeFilter.Add(".xml");

        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await LoadFileAsync(file);
    }

    private async System.Threading.Tasks.Task LoadFileAsync(StorageFile file)
    {
        try
        {
            string xml = await FileIO.ReadTextAsync(file);
            MusicXmlScore score = MusicXmlReader.Read(xml);
            ResetPlayback();
            _currentScore = score;

            FilePathTextBox.Text = file.Path;
            ScoreTitleText.Text = score.Title;
            ComposerText.Text = $"Komposition: {score.Composer}";
            PartNameText.Text = $"{score.PartName} · {score.TempoText}";
            ScoreSummaryText.Text = $"{score.MeasureCount} Takte · {score.Tones.Count} Töne · {score.TimeSignatureText} · {score.KeySignatureText}";
            ScoreNotation.UpdateScore(
                score,
                ColorByPitchCheckBox.IsChecked == true,
                ShowPitchLabelsCheckBox.IsChecked == true);
            PreparePlayback(score, resetTempo: true);
        }
        catch (Exception ex)
        {
            ResetPlayback();
            _currentScore = null;
            ScoreTitleText.Text = "–";
            ComposerText.Text = "–";
            PartNameText.Text = "–";
            ScoreSummaryText.Text = "–";
            ScoreNotation.UpdateScore(
                null,
                ColorByPitchCheckBox.IsChecked == true,
                ShowPitchLabelsCheckBox.IsChecked == true);

            ContentDialog errorDialog = new()
            {
                Title = "MusicXML konnte nicht geöffnet werden",
                Content = ex.Message,
                CloseButtonText = "Schließen",
                XamlRoot = XamlRoot
            };
            await errorDialog.ShowAsync();
        }
    }

    private void PreparePlayback(MusicXmlScore score, bool resetTempo = false)
    {
        _toneEndTimes.Clear();
        double elapsedSeconds = 0;
        foreach (RecordedTone tone in score.Tones)
        {
            elapsedSeconds += Math.Max(0.03, tone.Duration.TotalSeconds);
            _toneEndTimes.Add(elapsedSeconds);
        }

        if (resetTempo)
        {
            _playbackRate = 1;
            TempoSlider.Value = 100;
        }

        PlaybackButton.Visibility = Visibility.Visible;
        PlaybackButton.IsEnabled = score.TempoBpm is > 0 && score.Tones.Count > 0;
        TempoSliderPanel.Visibility = Visibility.Visible;
        TempoSlider.IsEnabled = PlaybackButton.IsEnabled;
        UpdateTempoDisplay();
        ToolTipService.SetToolTip(
            PlaybackButton,
            PlaybackButton.IsEnabled
                ? $"Partitur mit {score.TempoText} abspielen"
                : "Die Partitur enthält keine abspielbaren Noten oder keine Tempoangabe.");
    }

    private async void PlaybackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentScore is null || _toneEndTimes.Count == 0)
        {
            return;
        }

        if (_isPlaying)
        {
            PausePlayback();
            return;
        }

        if (!_midiPlayback.TryInitialize(out string? midiError))
        {
            ContentDialog errorDialog = new()
            {
                Title = "Keine MIDI-Ausgabe verfügbar",
                Content = midiError,
                CloseButtonText = "Schließen",
                XamlRoot = XamlRoot
            };
            await errorDialog.ShowAsync();
            return;
        }

        _isPlaying = true;
        PlaybackIcon.Glyph = "\uE769";
        ToolTipService.SetToolTip(PlaybackButton, "Wiedergabe pausieren");
        if (_playingToneIndex < 0)
        {
            SetPlayingTone(0);
        }
        else
        {
            _midiPlayback.PlayNote(_currentScore.Tones[_playingToneIndex].Pitch.MidiNote);
        }
        _playbackClock.Restart();
        _playbackTimer.Start();
    }

    private void PlaybackTimer_Tick(object? sender, object e)
    {
        if (_currentScore is null)
        {
            ResetPlayback();
            return;
        }

        AccumulatePlaybackTime();
        double elapsedSeconds = _elapsedPlaybackSeconds;
        int nextIndex = _playingToneIndex;
        while (nextIndex >= 0 &&
               nextIndex < _toneEndTimes.Count &&
               elapsedSeconds >= _toneEndTimes[nextIndex])
        {
            nextIndex++;
        }

        if (nextIndex >= _toneEndTimes.Count)
        {
            MusicXmlScore completedScore = _currentScore;
            ResetPlayback();
            PreparePlayback(completedScore);
        }
        else if (nextIndex != _playingToneIndex)
        {
            SetPlayingTone(nextIndex);
        }
    }

    private void SetPlayingTone(int toneIndex)
    {
        if (_currentScore is null || toneIndex < 0 || toneIndex >= _currentScore.Tones.Count)
        {
            return;
        }

        int previousSystem = _playingToneIndex >= 0
            ? _currentScore.Tones[_playingToneIndex].MeasureIndex / 4
            : -1;
        RecordedTone tone = _currentScore.Tones[toneIndex];
        _playingToneIndex = toneIndex;
        ScoreNotation.HighlightTone(tone);
        _midiPlayback.PlayNote(tone.Pitch.MidiNote);

        int currentSystem = tone.MeasureIndex / 4;
        if (currentSystem != previousSystem)
        {
            double offset = Math.Max(
                0,
                MusicXmlScoreControl.GetSystemTopForMeasure(tone.MeasureIndex) - 30);
            ScoreScrollViewer.ChangeView(null, offset, null, disableAnimation: false);
        }
    }

    private void PausePlayback()
    {
        AccumulatePlaybackTime();
        _playbackClock.Reset();
        _playbackTimer.Stop();
        _isPlaying = false;
        _midiPlayback.StopNote();
        PlaybackIcon.Glyph = "\uE768";
        ToolTipService.SetToolTip(PlaybackButton, "Wiedergabe fortsetzen");
    }

    private void AccumulatePlaybackTime()
    {
        if (!_isPlaying || !_playbackClock.IsRunning)
        {
            return;
        }

        _elapsedPlaybackSeconds += _playbackClock.Elapsed.TotalSeconds * _playbackRate;
        _playbackClock.Restart();
    }

    private void TempoSlider_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        AccumulatePlaybackTime();
        _playbackRate = e.NewValue / 100;
        if (_currentScore is not null)
        {
            UpdateTempoDisplay();
        }
    }

    private void UpdateTempoDisplay()
    {
        if (_currentScore is null)
        {
            return;
        }

        string percentage = $"{TempoSlider.Value:0} %";
        TempoValueText.Text = percentage;
        if (_currentScore.TempoBpm is double sourceTempo)
        {
            double effectiveTempo = sourceTempo * _playbackRate;
            string effectiveTempoText = effectiveTempo.ToString("0.##", CultureInfo.CurrentCulture);
            PartNameText.Text = $"{_currentScore.PartName} · {effectiveTempoText} BPM";
            ToolTipService.SetToolTip(
                TempoSliderPanel,
                $"{percentage} des Originaltempos · {effectiveTempoText} BPM");
        }
        else
        {
            PartNameText.Text = $"{_currentScore.PartName} · {_currentScore.TempoText}";
        }
    }

    private void ResetPlayback()
    {
        _playbackTimer.Stop();
        _playbackClock.Reset();
        _elapsedPlaybackSeconds = 0;
        _playingToneIndex = -1;
        _isPlaying = false;
        _toneEndTimes.Clear();
        _midiPlayback.StopAll();
        ScoreNotation.HighlightTone(null);
        PlaybackIcon.Glyph = "\uE768";
        PlaybackButton.IsEnabled = false;
        PlaybackButton.Visibility = Visibility.Collapsed;
        TempoSlider.IsEnabled = false;
        TempoSliderPanel.Visibility = Visibility.Collapsed;
    }

    private void MusicXmlViewerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ResetPlayback();
        _midiPlayback.Dispose();
    }

    private void ColorByPitchCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateDisplayOptions();
    }

    private void DisplayOptionsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateDisplayOptions();
    }

    private void UpdateDisplayOptions()
    {
        if (_currentScore is not null)
        {
            ScoreNotation.UpdateScore(
                _currentScore,
                ColorByPitchCheckBox.IsChecked == true,
                ShowPitchLabelsCheckBox.IsChecked == true);
            if (_playingToneIndex >= 0 && _playingToneIndex < _currentScore.Tones.Count)
            {
                ScoreNotation.HighlightTone(_currentScore.Tones[_playingToneIndex]);
            }
        }
    }
}
