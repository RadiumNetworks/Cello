using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Cello.Notation;

/// <summary>
/// Lightweight WinUI staff renderer kept independent from audio capture and
/// pitch detection. It uses bass clef for lower cello notes and treble clef
/// for higher notes.
/// </summary>
public sealed partial class StaffNotationControl : UserControl
{
    private const double StaffLeft = 54;
    private const double StaffRight = 282;
    private const double StaffTop = 44;
    private const double LineSpacing = 16;
    private const double NoteX = 178;

    public static readonly DependencyProperty MidiNoteProperty = DependencyProperty.Register(
        nameof(MidiNote),
        typeof(int?),
        typeof(StaffNotationControl),
        new PropertyMetadata(null, OnMidiNoteChanged));

    public StaffNotationControl()
    {
        InitializeComponent();
        DrawNotation();
    }

    public int? MidiNote
    {
        get => (int?)GetValue(MidiNoteProperty);
        set => SetValue(MidiNoteProperty, value);
    }

    private static void OnMidiNoteChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((StaffNotationControl)sender).DrawNotation();
    }

    private void DrawNotation()
    {
        NotationCanvas.Children.Clear();
        DrawStaff();

        if (MidiNote is not int midiNote)
        {
            DrawPlaceholder();
            return;
        }

        bool useTrebleClef = midiNote >= 60;
        DrawClef(useTrebleClef);
        DrawNote(midiNote, useTrebleClef);
    }

    private void DrawStaff()
    {
        for (int line = 0; line < 5; line++)
        {
            double y = StaffTop + line * LineSpacing;
            AddLine(StaffLeft, y, StaffRight, y, 1.25);
        }
    }

    private void DrawClef(bool treble)
    {
        var clef = new TextBlock
        {
            Text = treble ? "𝄞" : "𝄢",
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = treble ? 62 : 52,
            Foreground = new SolidColorBrush(Colors.Black)
        };

        Canvas.SetLeft(clef, 66);
        Canvas.SetTop(clef, treble ? 26 : 40);
        NotationCanvas.Children.Add(clef);
    }

    private void DrawNote(int midiNote, bool treble)
    {
        (int diatonicIndex, bool sharp) = GetDiatonicPosition(midiNote);
        int bottomLineIndex = treble ? 30 : 18; // E4 in treble, G2 in bass.
        double bottomLineY = StaffTop + 4 * LineSpacing;
        double noteY = bottomLineY - (diatonicIndex - bottomLineIndex) * (LineSpacing / 2);

        DrawLedgerLines(noteY, bottomLineY);

        var noteHead = new Ellipse
        {
            Width = 20,
            Height = 14,
            Fill = new SolidColorBrush(Colors.Black),
            RenderTransform = new RotateTransform
            {
                Angle = -18,
                CenterX = 10,
                CenterY = 7
            }
        };
        Canvas.SetLeft(noteHead, NoteX - 10);
        Canvas.SetTop(noteHead, noteY - 7);
        NotationCanvas.Children.Add(noteHead);

        bool stemDown = noteY < StaffTop + 2 * LineSpacing;
        double stemX = stemDown ? NoteX - 9 : NoteX + 9;
        double stemEndY = stemDown ? noteY + 46 : noteY - 46;
        AddLine(stemX, noteY, stemX, stemEndY, 2);

        if (sharp)
        {
            var accidental = new TextBlock
            {
                Text = "♯",
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 30,
                Foreground = new SolidColorBrush(Colors.Black)
            };
            Canvas.SetLeft(accidental, NoteX - 43);
            Canvas.SetTop(accidental, noteY - 22);
            NotationCanvas.Children.Add(accidental);
        }
    }

    private void DrawLedgerLines(double noteY, double bottomLineY)
    {
        if (noteY < StaffTop)
        {
            for (double y = StaffTop - LineSpacing; y >= noteY - 1; y -= LineSpacing)
            {
                AddLine(NoteX - 18, y, NoteX + 18, y, 1.25);
            }
        }
        else if (noteY > bottomLineY)
        {
            for (double y = bottomLineY + LineSpacing; y <= noteY + 1; y += LineSpacing)
            {
                AddLine(NoteX - 18, y, NoteX + 18, y, 1.25);
            }
        }
    }

    private void DrawPlaceholder()
    {
        var placeholder = new TextBlock
        {
            Text = "Noch kein Ton erkannt",
            Foreground = new SolidColorBrush(Colors.Gray),
            FontSize = 14
        };
        Canvas.SetLeft(placeholder, 132);
        Canvas.SetTop(placeholder, 119);
        NotationCanvas.Children.Add(placeholder);
    }

    private void AddLine(double x1, double y1, double x2, double y2, double thickness)
    {
        NotationCanvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = new SolidColorBrush(Colors.Black),
            StrokeThickness = thickness
        });
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
        bool sharp = pitchClass is 1 or 3 or 6 or 8 or 10;
        return (octave * 7 + scaleStep, sharp);
    }
}
