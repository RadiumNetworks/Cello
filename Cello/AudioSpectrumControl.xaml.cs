using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Cello.Audio;

/// <summary>
/// Renders normalized spectrum bands without depending on the analyzer or an
/// external charting package.
/// </summary>
public sealed partial class AudioSpectrumControl : UserControl
{
    private IReadOnlyList<double> _levels = AudioSignalSnapshot.Empty.Spectrum;
    private int _dominantBandIndex = -1;

    public AudioSpectrumControl()
    {
        InitializeComponent();
    }

    public void UpdateSpectrum(IReadOnlyList<double> levels, int dominantBandIndex = -1, double dominantFrequencyHz = 0)
    {
        _levels = levels;
        _dominantBandIndex = dominantBandIndex;
        DominantFrequencyText.Text = dominantBandIndex >= 0
            ? $"Dominante Frequenz: {dominantFrequencyHz:0.0} Hz"
            : "Dominante Frequenz: –";
        DrawSpectrum();
    }

    private void SpectrumCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawSpectrum();
    }

    private void DrawSpectrum()
    {
        SpectrumCanvas.Children.Clear();
        if (_levels.Count == 0 || SpectrumCanvas.ActualWidth <= 0 || SpectrumCanvas.ActualHeight <= 0)
        {
            return;
        }

        double slotWidth = SpectrumCanvas.ActualWidth / _levels.Count;
        double barWidth = Math.Max(3, slotWidth - 5);
        var brush = new SolidColorBrush(Colors.DodgerBlue);
        var dominantBrush = new SolidColorBrush(Colors.Orange);

        for (int i = 0; i < _levels.Count; i++)
        {
            double height = Math.Max(2, Math.Clamp(_levels[i], 0, 1) * SpectrumCanvas.ActualHeight);
            var bar = new Rectangle
            {
                Width = barWidth,
                Height = height,
                Fill = i == _dominantBandIndex ? dominantBrush : brush,
                RadiusX = 2,
                RadiusY = 2
            };

            Canvas.SetLeft(bar, i * slotWidth + (slotWidth - barWidth) / 2);
            Canvas.SetTop(bar, SpectrumCanvas.ActualHeight - height);
            SpectrumCanvas.Children.Add(bar);
        }
    }
}
