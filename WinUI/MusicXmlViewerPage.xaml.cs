using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Cello.Notation;
using Cello.Playback;
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
    private const double MissingTempoDurationBasisBpm = 120;
    private MusicXmlScore? _currentScore;
    private readonly DispatcherTimer _playbackTimer = new();
    private readonly Stopwatch _playbackClock = new();
    private readonly List<double> _toneEndTimes = [];
    private readonly MidiPlaybackService _midiPlayback = new();
    private double _elapsedPlaybackSeconds;
    private double _playbackRate = 1;
    private double? _manualTempoBpm;
    private int _playingToneIndex = -1;
    private readonly PracticeRangeState _practiceRange = new();
    private bool _isPlaying;

    public MusicXmlViewerPage()
    {
        InitializeComponent();
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(30);
        _playbackTimer.Tick += PlaybackTimer_Tick;
        ScoreNotation.ToneClicked += ScoreNotation_ToneClicked;
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
            _currentScore = null;
            ResetManualTempo();
            ResetPracticeRange(0);
            _currentScore = score;
            ResetPracticeRange(score.Tones.Count);

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
            ResetManualTempo();
            ResetPracticeRange(0);
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
        ManualTempoNumberBox.Visibility = score.TempoBpm is > 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        TempoSliderPanel.Visibility = Visibility.Visible;
        UpdatePlaybackAvailability();
        UpdateTempoDisplay();
    }

    private void ManualTempoNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        AccumulatePlaybackTime();
        _manualTempoBpm = double.IsNaN(args.NewValue) ? null : args.NewValue;
        UpdatePlaybackRate();

        if (_currentScore is not null)
        {
            UpdatePlaybackAvailability();
            UpdateTempoDisplay();
        }
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
        int playbackStart = GetPlaybackStartIndex();
        int playbackEnd = GetPlaybackEndIndex();
        if (_playingToneIndex < playbackStart || _playingToneIndex > playbackEnd)
        {
            SetPlaybackPosition(playbackStart);
            SetPlayingTone(playbackStart);
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
         int playbackEnd = GetPlaybackEndIndex();
        while (nextIndex >= 0 &&
             nextIndex <= playbackEnd &&
               elapsedSeconds >= _toneEndTimes[nextIndex])
        {
            nextIndex++;
        }

        if (nextIndex > playbackEnd)
        {
            if (_practiceRange.IsLooping)
            {
                int playbackStart = GetPlaybackStartIndex();
                SetPlaybackPosition(playbackStart);
                SetPlayingTone(playbackStart);
                _playbackClock.Restart();
            }
            else
            {
                MusicXmlScore completedScore = _currentScore;
                ResetPlayback();
                PreparePlayback(completedScore);
            }
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

    private void SetPlaybackPosition(int toneIndex)
    {
        _elapsedPlaybackSeconds = toneIndex > 0 ? _toneEndTimes[toneIndex - 1] : 0;
        _playingToneIndex = -1;
    }

    private int GetPlaybackStartIndex()
    {
        return _practiceRange.PlaybackStartIndex;
    }

    private int GetPlaybackEndIndex()
    {
        return _practiceRange.PlaybackEndIndex;
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
        UpdatePlaybackRate();
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
        double? sourceTempo = GetSourceTempo();
        if (sourceTempo is double tempo)
        {
            double effectiveTempo = tempo * TempoSlider.Value / 100;
            string effectiveTempoText = effectiveTempo.ToString("0.##", CultureInfo.CurrentCulture);
            PartNameText.Text = $"{_currentScore.PartName} · {effectiveTempoText} BPM";
            ToolTipService.SetToolTip(
                TempoSliderPanel,
                $"{percentage} des Grundtempos · {effectiveTempoText} BPM");
        }
        else
        {
            PartNameText.Text = $"{_currentScore.PartName} · {_currentScore.TempoText}";
        }
    }

    private double? GetSourceTempo()
    {
        return _currentScore?.TempoBpm is > 0
            ? _currentScore.TempoBpm
            : _manualTempoBpm is > 0
                ? _manualTempoBpm
                : null;
    }

    private void UpdatePlaybackRate()
    {
        double tempoFactor = _currentScore?.TempoBpm is > 0
            ? 1
            : (_manualTempoBpm ?? MissingTempoDurationBasisBpm) / MissingTempoDurationBasisBpm;
        _playbackRate = TempoSlider.Value / 100 * tempoFactor;
    }

    private void UpdatePlaybackAvailability()
    {
        bool hasTones = _currentScore?.Tones.Count > 0;
        double? sourceTempo = GetSourceTempo();
        bool canPlay = hasTones && sourceTempo is > 0;

        PlaybackButton.IsEnabled = canPlay;
        TempoSlider.IsEnabled = canPlay;
        ToolTipService.SetToolTip(
            PlaybackButton,
            canPlay
                ? $"Partitur mit {sourceTempo!.Value.ToString("0.##", CultureInfo.CurrentCulture)} BPM abspielen"
                : hasTones
                    ? "Bitte zuerst ein Tempo eingeben."
                    : "Die Partitur enthält keine abspielbaren Noten.");
    }

    private void ResetManualTempo()
    {
        _manualTempoBpm = null;
        ManualTempoNumberBox.Value = double.NaN;
        ManualTempoNumberBox.Visibility = Visibility.Collapsed;
    }

    private void ScoreNotation_ToneClicked(RecordedTone tone)
    {
        if (_currentScore is null)
        {
            return;
        }

        int toneIndex = -1;
        for (int index = 0; index < _currentScore.Tones.Count; index++)
        {
            if (ReferenceEquals(_currentScore.Tones[index], tone))
            {
                toneIndex = index;
                break;
            }
        }
        if (toneIndex < 0)
        {
            return;
        }

        if (_isPlaying)
        {
            PausePlayback();
        }

        _practiceRange.SelectTone(toneIndex);
        LoopToggleSwitch.IsOn = _practiceRange.IsLooping;

        SetPlaybackPosition(_practiceRange.PlaybackStartIndex);
        ScoreNotation.HighlightTone(null);
        UpdatePracticeRangeDisplay();
    }

    private void LoopToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        _practiceRange.SetLooping(LoopToggleSwitch.IsOn);
        UpdatePracticeRangeDisplay();
    }

    private void ClearPracticeRangeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            PausePlayback();
        }
        ResetPracticeRange(_currentScore?.Tones.Count ?? 0);
        _playingToneIndex = -1;
        _elapsedPlaybackSeconds = 0;
        ScoreNotation.HighlightTone(null);
    }

    private void UpdatePracticeRangeDisplay()
    {
        if (_currentScore is null || !_practiceRange.HasSelection)
        {
            PracticeRangeText.Text = "Startnote und anschließend Endnote in der Partitur anklicken.";
            ClearPracticeRangeButton.IsEnabled = false;
            LoopToggleSwitch.IsEnabled = false;
            ScoreNotation.HighlightPracticeRange(null, null);
            return;
        }

        RecordedTone startTone = _currentScore.Tones[_practiceRange.StartIndex!.Value];
        RecordedTone? endTone = _practiceRange.IsComplete
            ? _currentScore.Tones[_practiceRange.EndIndex!.Value]
            : null;
        string startText = DescribeTone(startTone);
        PracticeRangeText.Text = endTone is null
            ? $"Start: {startText} – jetzt Endnote anklicken."
            : $"{(LoopToggleSwitch.IsOn ? "Loop" : "Bereich")}: {startText} bis {DescribeTone(endTone)}";
        ClearPracticeRangeButton.IsEnabled = true;
        LoopToggleSwitch.IsEnabled = endTone is not null;
        ScoreNotation.HighlightPracticeRange(startTone, endTone);
    }

    private static string DescribeTone(RecordedTone tone)
    {
        return $"Takt {tone.MeasureIndex + 1}, {tone.Pitch.NoteName}";
    }

    private void ResetPracticeRange(int toneCount = 0)
    {
        _practiceRange.Reset(toneCount);
        LoopToggleSwitch.IsOn = false;
        UpdatePracticeRangeDisplay();
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
        ScoreNotation.ToneClicked -= ScoreNotation_ToneClicked;
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
            UpdatePracticeRangeDisplay();
            if (_playingToneIndex >= 0 && _playingToneIndex < _currentScore.Tones.Count)
            {
                ScoreNotation.HighlightTone(_currentScore.Tones[_playingToneIndex]);
            }
        }
    }
}
