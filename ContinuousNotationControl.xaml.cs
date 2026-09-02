using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Cello.Notation;

/// <summary>
/// Draws an expanding, horizontally scrollable sequence of detected notes.
/// </summary>
public sealed partial class ContinuousNotationControl : UserControl
{
    private const double StaffLeft = 28;
    private const double StaffTop = 48;
    private const double LineSpacing = 16;
    private const double NoteSpacing = 66;
    private const double FirstNoteX = 130;

    public ContinuousNotationControl()
    {
        InitializeComponent();
        UpdateNotes([]);
    }

    public void UpdateNotes(IReadOnlyList<RecordedTone> tones, bool colorByPitch = false)
    {
        NotationCanvas.Children.Clear();

        double width = Math.Max(760, FirstNoteX + tones.Count * NoteSpacing + 50);
        NotationCanvas.Width = width;
        DrawStaff(width - 24);

        if (tones.Count == 0)
        {
            DrawClef(false, 42);
            var placeholder = new TextBlock
            {
                Text = "Die aufgenommenen Töne erscheinen hier fortlaufend.",
                Foreground = new SolidColorBrush(Colors.Gray),
                FontSize = 15
            };
            Canvas.SetLeft(placeholder, 125);
            Canvas.SetTop(placeholder, 145);
            NotationCanvas.Children.Add(placeholder);
            return;
        }

        bool? previousTreble = null;
        for (int index = 0; index < tones.Count; index++)
        {
            RecordedTone tone = tones[index];
            bool treble = tone.Pitch.MidiNote >= 60;
            double noteX = FirstNoteX + index * NoteSpacing;

            if (previousTreble != treble)
            {
                DrawClef(treble, Math.Max(38, noteX - 76));
                previousTreble = treble;
            }

            DrawNote(tone, noteX, treble, colorByPitch);

            if ((index + 1) % 4 == 0)
            {
                AddLine(noteX + 32, StaffTop, noteX + 32, StaffTop + 4 * LineSpacing, 1.5);
            }
        }
    }

    private void DrawStaff(double right)
    {
        for (int line = 0; line < 5; line++)
        {
            double y = StaffTop + line * LineSpacing;
            AddLine(StaffLeft, y, right, y, 1.15);
        }
    }

    private void DrawClef(bool treble, double x)
    {
        var clef = new TextBlock
        {
            Text = treble ? "𝄞" : "𝄢",
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = treble ? 58 : 49,
            Foreground = new SolidColorBrush(Colors.Black)
        };
        Canvas.SetLeft(clef, x);
        Canvas.SetTop(clef, treble ? 30 : 44);
        NotationCanvas.Children.Add(clef);
    }

    private void DrawNote(RecordedTone tone, double noteX, bool treble, bool colorByPitch)
    {
        (int diatonicIndex, bool sharp) = GetDiatonicPosition(tone.Pitch.MidiNote);
        int bottomLineIndex = treble ? 30 : 18;
        double bottomLineY = StaffTop + 4 * LineSpacing;
        double noteY = bottomLineY - (diatonicIndex - bottomLineIndex) * (LineSpacing / 2);

        DrawLedgerLines(noteX, noteY, bottomLineY);

        NotationNoteValue noteValue = ResolveNoteValue(tone);
        Color noteColor = colorByPitch ? GetPitchColor(tone.Pitch.MidiNote) : Colors.Black;
        var noteHead = new Ellipse
        {
            Width = 19,
            Height = 13,
            Fill = noteValue is NotationNoteValue.Whole or NotationNoteValue.Half
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(noteColor),
            Stroke = new SolidColorBrush(noteColor),
            StrokeThickness = 2,
            RenderTransform = new RotateTransform
            {
                Angle = -18,
                CenterX = 9.5,
                CenterY = 6.5
            }
        };
        Canvas.SetLeft(noteHead, noteX - 9.5);
        Canvas.SetTop(noteHead, noteY - 6.5);
        NotationCanvas.Children.Add(noteHead);

        if (noteValue != NotationNoteValue.Whole)
        {
            bool stemDown = noteY < StaffTop + 2 * LineSpacing;
            double stemX = stemDown ? noteX - 8.5 : noteX + 8.5;
            double stemEndY = stemDown ? noteY + 42 : noteY - 42;
            AddLine(stemX, noteY, stemX, stemEndY, 1.8, noteColor);

            int flagCount = noteValue switch
            {
                NotationNoteValue.Eighth => 1,
                NotationNoteValue.Sixteenth => 2,
                _ => 0
            };
            for (int flag = 0; flag < flagCount; flag++)
            {
                double flagY = stemDown ? stemEndY - flag * 9 : stemEndY + flag * 9;
                var flagPath = new TextBlock
                {
                    Text = stemDown ? "◜" : "◝",
                    FontSize = 22,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(noteColor)
                };
                Canvas.SetLeft(flagPath, stemDown ? stemX - 1 : stemX - 14);
                Canvas.SetTop(flagPath, stemDown ? flagY - 9 : flagY - 15);
                NotationCanvas.Children.Add(flagPath);
            }
        }

        if (sharp)
        {
            var accidental = new TextBlock
            {
                Text = "♯",
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 25,
                Foreground = new SolidColorBrush(Colors.Black)
            };
            Canvas.SetLeft(accidental, noteX - 34);
            Canvas.SetTop(accidental, noteY - 18);
            NotationCanvas.Children.Add(accidental);
        }

        var label = new TextBlock
        {
            Text = tone.Pitch.NoteName,
            Foreground = new SolidColorBrush(Colors.Black),
            FontSize = 13
        };
        Canvas.SetLeft(label, noteX - 15);
        Canvas.SetTop(label, 148);
        NotationCanvas.Children.Add(label);
    }

    private void DrawLedgerLines(double noteX, double noteY, double bottomLineY)
    {
        if (noteY < StaffTop)
        {
            for (double y = StaffTop - LineSpacing; y >= noteY - 1; y -= LineSpacing)
            {
                AddLine(noteX - 16, y, noteX + 16, y, 1.15);
            }
        }
        else if (noteY > bottomLineY)
        {
            for (double y = bottomLineY + LineSpacing; y <= noteY + 1; y += LineSpacing)
            {
                AddLine(noteX - 16, y, noteX + 16, y, 1.15);
            }
        }
    }

    private void AddLine(
        double x1,
        double y1,
        double x2,
        double y2,
        double thickness,
        Color? color = null)
    {
        NotationCanvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = new SolidColorBrush(color ?? Colors.Black),
            StrokeThickness = thickness
        });
    }

    private static NotationNoteValue ResolveNoteValue(RecordedTone tone)
    {
        if (tone.NoteValue != NotationNoteValue.Automatic)
        {
            return tone.NoteValue;
        }

        return tone.Duration.TotalSeconds switch
        {
            >= 1.75 => NotationNoteValue.Whole,
            >= 0.75 => NotationNoteValue.Half,
            >= 0.375 => NotationNoteValue.Quarter,
            >= 0.1875 => NotationNoteValue.Eighth,
            _ => NotationNoteValue.Sixteenth
        };
    }

    private static Color GetPitchColor(int midiNote)
    {
        int pitchClass = ((midiNote % 12) + 12) % 12;
        return pitchClass switch
        {
            0 or 1 => ColorHelper.FromArgb(255, 226, 28, 72),   // C: red
            2 or 3 => ColorHelper.FromArgb(255, 249, 157, 28), // D: orange
            4 => Colors.Black,                                 // E: black for contrast
            5 or 6 => ColorHelper.FromArgb(255, 98, 188, 71),  // F: light green
            7 or 8 => ColorHelper.FromArgb(255, 0, 122, 92),   // G: dark green
            9 or 10 => ColorHelper.FromArgb(255, 78, 103, 200),// A: blue
            _ => ColorHelper.FromArgb(255, 207, 62, 150)       // B: pink
        };
    }

    private static (int DiatonicIndex, bool Sharp) GetDiatonicPosition(int midiNote)
    {
        int octave = midiNote / 12 - 1;
        int pitchClass = ((midiNote % 12) + 12) % 12;
        int scaleStep = pitchClass switch
        {
            0 or 1 => 0,
            2 or 3 => 1,
            4 => 2,
            5 or 6 => 3,
            7 or 8 => 4,
            9 or 10 => 5,
            _ => 6
        };
        return (octave * 7 + scaleStep, pitchClass is 1 or 3 or 6 or 8 or 10);
    }
}
