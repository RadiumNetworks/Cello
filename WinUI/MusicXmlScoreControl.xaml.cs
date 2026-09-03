using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Cello.Notation;

/// <summary>
/// Renders a MusicXML pitch preview in systems of exactly four measures.
/// </summary>
public sealed partial class MusicXmlScoreControl : UserControl
{
    private const int MeasuresPerSystem = 4;
    private const double CanvasWidth = 1130;
    private const double HorizontalMargin = 20;
    private const double FirstMeasureLeft = 190;
    private const double MeasureWidth = 230;
    private const double SystemHeight = 205;
    private const double FirstSystemTop = 78;
    private const double LineSpacing = 14;
    private readonly Dictionary<RecordedTone, Point> _notePositions = [];
    private Ellipse? _playbackHighlight;
    private Ellipse? _rangeStartHighlight;
    private Ellipse? _rangeEndHighlight;

    public event Action<RecordedTone>? ToneClicked;

    public MusicXmlScoreControl()
    {
        InitializeComponent();
        ScoreCanvas.PointerPressed += ScoreCanvas_PointerPressed;
        UpdateScore(null, false, false);
    }

    public void UpdateScore(MusicXmlScore? score, bool colorByPitch, bool showPitchLabels)
    {
        ScoreCanvas.Children.Clear();
        _notePositions.Clear();
        _playbackHighlight = null;
        _rangeStartHighlight = null;
        _rangeEndHighlight = null;

        if (score is null || score.MeasureCount == 0)
        {
            ScoreCanvas.Height = 190;
            DrawPlaceholder();
            return;
        }

        int systemCount = (score.MeasureCount + MeasuresPerSystem - 1) / MeasuresPerSystem;
        ScoreCanvas.Width = CanvasWidth;
        ScoreCanvas.Height = Math.Max(190, systemCount * SystemHeight + 20);

        ILookup<int, RecordedTone> tonesByMeasure = score.Tones.ToLookup(tone => tone.MeasureIndex);
        IReadOnlyList<WedgeSpan> wedgeSpans = CreateWedgeSpans(score.Directives, score.MeasureCount);
        for (int system = 0; system < systemCount; system++)
        {
            int firstMeasure = system * MeasuresPerSystem;
            double staffTop = FirstSystemTop + system * SystemHeight;
            IReadOnlyList<RecordedTone> systemTones = Enumerable.Range(firstMeasure, MeasuresPerSystem)
                .Where(index => index < score.MeasureCount)
                .SelectMany(index => tonesByMeasure[index])
                .ToList();
            bool treble = systemTones.Count > 0 && systemTones.Average(tone => tone.Pitch.MidiNote) >= 60;

            DrawStaff(staffTop);
            DrawClef(treble, staffTop);
            DrawKeySignature(score.KeyFifths, treble, staffTop);
            DrawTimeSignature(score.Beats, score.BeatType, score.KeyFifths, staffTop);
            var placements = new List<NotePlacement>();

            for (int position = 0; position < MeasuresPerSystem; position++)
            {
                int measureIndex = firstMeasure + position;
                if (measureIndex >= score.MeasureCount)
                {
                    break;
                }

                double measureLeft = FirstMeasureLeft + position * MeasureWidth;
                DrawMeasureNumber(measureIndex + 1, measureLeft, staffTop);
                DrawBarLine(measureLeft, staffTop, position == 0 ? 1.8 : 1.2);

                List<RecordedTone> tones = tonesByMeasure[measureIndex].ToList();
                double contentLeft = measureLeft + 14;
                double contentRight = measureLeft + MeasureWidth - 12;
                var measurePlacements = new List<NotePlacement>();
                List<BeamGroup> beamGroups = CreateBeamGroups(tones, staffTop, treble);
                for (int noteIndex = 0; noteIndex < tones.Count; noteIndex++)
                {
                    double fraction = (noteIndex + 0.5) / Math.Max(1, tones.Count);
                    double noteX = contentLeft + fraction * (contentRight - contentLeft);
                    BeamGroup? beamGroup = beamGroups.FirstOrDefault(group =>
                        noteIndex >= group.StartIndex && noteIndex <= group.EndIndex);
                    NotePlacement placement = DrawNote(
                        tones[noteIndex],
                        noteX,
                        staffTop,
                        treble,
                        colorByPitch,
                        showPitchLabels,
                        beamGroup?.StemDown,
                        beamGroup is not null);
                    _notePositions[tones[noteIndex]] = new Point(placement.X, placement.Y);
                    placements.Add(placement);
                    measurePlacements.Add(placement);
                }

                DrawBeamGroups(measurePlacements, beamGroups);
            }

            int measuresInSystem = Math.Min(MeasuresPerSystem, score.MeasureCount - firstMeasure);
            double systemRight = FirstMeasureLeft + measuresInSystem * MeasureWidth;
            DrawBarLine(systemRight, staffTop, 1.8);
            DrawDirectives(
                score.Directives,
                wedgeSpans,
                tonesByMeasure,
                firstMeasure,
                measuresInSystem,
                staffTop,
                systemRight);
            DrawNotationCurves(placements, staffTop, systemRight);
            DrawTuplets(placements, staffTop, systemRight);
        }
    }

    public void HighlightTone(RecordedTone? tone)
    {
        if (_playbackHighlight is not null)
        {
            ScoreCanvas.Children.Remove(_playbackHighlight);
            _playbackHighlight = null;
        }

        if (tone is null || !_notePositions.TryGetValue(tone, out Point position))
        {
            return;
        }

        _playbackHighlight = new Ellipse
        {
            Width = 29,
            Height = 24,
            Fill = new SolidColorBrush(ColorHelper.FromArgb(80, 255, 185, 0)),
            Stroke = new SolidColorBrush(ColorHelper.FromArgb(255, 232, 126, 0)),
            StrokeThickness = 2.5
        };
        Canvas.SetLeft(_playbackHighlight, position.X - 14.5);
        Canvas.SetTop(_playbackHighlight, position.Y - 12);
        ScoreCanvas.Children.Add(_playbackHighlight);
    }

    public void HighlightPracticeRange(RecordedTone? startTone, RecordedTone? endTone)
    {
        RemoveRangeHighlight(ref _rangeStartHighlight);
        RemoveRangeHighlight(ref _rangeEndHighlight);

        _rangeStartHighlight = CreateRangeHighlight(
            startTone,
            29,
            ColorHelper.FromArgb(255, 16, 124, 16));
        _rangeEndHighlight = CreateRangeHighlight(
            endTone,
            startTone == endTone ? 37 : 29,
            ColorHelper.FromArgb(255, 196, 43, 28));
    }

    private void ScoreCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Point pointerPosition = e.GetCurrentPoint(ScoreCanvas).Position;
        RecordedTone? tone = _notePositions
            .Where(entry =>
                Math.Abs(entry.Value.X - pointerPosition.X) <= 18 &&
                Math.Abs(entry.Value.Y - pointerPosition.Y) <= 18)
            .OrderBy(entry =>
                Math.Pow(entry.Value.X - pointerPosition.X, 2) +
                Math.Pow(entry.Value.Y - pointerPosition.Y, 2))
            .Select(entry => entry.Key)
            .FirstOrDefault();
        if (tone is null)
        {
            return;
        }

        e.Handled = true;
        ToneClicked?.Invoke(tone);
    }

    private Ellipse? CreateRangeHighlight(
        RecordedTone? tone,
        double size,
        Color color)
    {
        if (tone is null || !_notePositions.TryGetValue(tone, out Point position))
        {
            return null;
        }

        var highlight = new Ellipse
        {
            Width = size,
            Height = size - 5,
            Fill = new SolidColorBrush(ColorHelper.FromArgb(25, color.R, color.G, color.B)),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2.5,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(highlight, position.X - size / 2);
        Canvas.SetTop(highlight, position.Y - (size - 5) / 2);
        ScoreCanvas.Children.Add(highlight);
        return highlight;
    }

    private void RemoveRangeHighlight(ref Ellipse? highlight)
    {
        if (highlight is not null)
        {
            ScoreCanvas.Children.Remove(highlight);
            highlight = null;
        }
    }

    public static double GetSystemTopForMeasure(int measureIndex)
    {
        int systemIndex = Math.Max(0, measureIndex) / MeasuresPerSystem;
        return FirstSystemTop + systemIndex * SystemHeight;
    }

    private void DrawPlaceholder()
    {
        var text = new TextBlock
        {
            Text = "Noch keine MusicXML-Partitur geladen.",
            Foreground = new SolidColorBrush(Colors.Gray),
            FontSize = 15
        };
        Canvas.SetLeft(text, 330);
        Canvas.SetTop(text, 82);
        ScoreCanvas.Children.Add(text);
    }

    private void DrawStaff(double staffTop)
    {
        for (int line = 0; line < 5; line++)
        {
            double y = staffTop + line * LineSpacing;
            AddLine(HorizontalMargin, y, CanvasWidth - HorizontalMargin, y, 1.1);
        }
    }

    private void DrawClef(bool treble, double staffTop)
    {
        var clef = new TextBlock
        {
            Text = treble ? "𝄞" : "𝄢",
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = treble ? 52 : 44,
            Foreground = new SolidColorBrush(Colors.Black)
        };
        Canvas.SetLeft(clef, HorizontalMargin + 9);
        Canvas.SetTop(clef, treble ? staffTop - 16 : staffTop - 3);
        ScoreCanvas.Children.Add(clef);
    }

    private void DrawKeySignature(int fifths, bool treble, double staffTop)
    {
        int count = Math.Min(7, Math.Abs(fifths));
        if (count == 0)
        {
            return;
        }

        int[] sharpSteps = treble ? [0, 3, -1, 2, 5, 1, 4] : [2, 5, 1, 4, 0, 3, -1];
        int[] flatSteps = treble ? [4, 1, 5, 2, 6, 3, 7] : [6, 3, 7, 4, 8, 5, 9];
        int[] positions = fifths > 0 ? sharpSteps : flatSteps;

        for (int index = 0; index < count; index++)
        {
            var accidental = new TextBlock
            {
                Text = fifths > 0 ? "♯" : "♭",
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = fifths > 0 ? 20 : 23,
                Foreground = new SolidColorBrush(Colors.Black)
            };
            Canvas.SetLeft(accidental, HorizontalMargin + 50 + index * 10);
            Canvas.SetTop(accidental, staffTop + positions[index] * (LineSpacing / 2) - 15);
            ScoreCanvas.Children.Add(accidental);
        }
    }

    private void DrawTimeSignature(string beats, string beatType, int keyFifths, double staffTop)
    {
        double x = HorizontalMargin + 55 + Math.Min(7, Math.Abs(keyFifths)) * 10;
        var numerator = CreateTimeSignatureNumber(beats);
        var denominator = CreateTimeSignatureNumber(beatType);
        Canvas.SetLeft(numerator, x);
        Canvas.SetTop(numerator, staffTop - 5);
        Canvas.SetLeft(denominator, x);
        Canvas.SetTop(denominator, staffTop + 23);
        ScoreCanvas.Children.Add(numerator);
        ScoreCanvas.Children.Add(denominator);
    }

    private static TextBlock CreateTimeSignatureNumber(string value)
    {
        return new TextBlock
        {
            Text = value,
            Width = 32,
            TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
            FontSize = 25,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.Black)
        };
    }

    private void DrawMeasureNumber(int number, double measureLeft, double staffTop)
    {
        var label = new TextBlock
        {
            Text = number.ToString(),
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.DimGray)
        };
        Canvas.SetLeft(label, measureLeft + 4);
        Canvas.SetTop(label, staffTop - 24);
        ScoreCanvas.Children.Add(label);
    }

    private void DrawBarLine(double x, double staffTop, double thickness)
    {
        AddLine(x, staffTop, x, staffTop + 4 * LineSpacing, thickness);
    }

    private void DrawDirectives(
        IReadOnlyList<MusicXmlDirective> directives,
        IReadOnlyList<WedgeSpan> wedgeSpans,
        ILookup<int, RecordedTone> tonesByMeasure,
        int firstMeasure,
        int measuresInSystem,
        double staffTop,
        double systemRight)
    {
        int lastMeasure = firstMeasure + measuresInSystem - 1;
        var textRows = new Dictionary<(int Measure, int Tone, string Placement), int>();

        foreach (MusicXmlDirective directive in directives.Where(directive =>
            directive.Kind != MusicXmlDirectiveKind.Wedge &&
            directive.MeasureIndex >= firstMeasure &&
            directive.MeasureIndex <= lastMeasure))
        {
            double x = GetDirectiveX(directive, tonesByMeasure, firstMeasure);
            var rowKey = (directive.MeasureIndex, directive.ToneIndex, directive.Placement);
            int row = textRows.TryGetValue(rowKey, out int existingRows) ? existingRows : 0;
            textRows[rowKey] = row + 1;

            bool isDynamic = directive.Kind == MusicXmlDirectiveKind.Dynamic;
            bool above = directive.Placement == "above";
            var label = new TextBlock
            {
                Text = directive.Value,
                FontSize = isDynamic ? 17 : 13,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                FontWeight = isDynamic
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = new SolidColorBrush(Colors.Black)
            };
            Canvas.SetLeft(label, x - (isDynamic ? 7 : 3));
            Canvas.SetTop(label, above
                ? staffTop - 51 - row * 18
                : staffTop + 65 + row * 19);
            ScoreCanvas.Children.Add(label);
        }

        foreach (WedgeSpan span in wedgeSpans.Where(span =>
            span.Start.MeasureIndex <= lastMeasure && span.End.MeasureIndex >= firstMeasure))
        {
            double left = span.Start.MeasureIndex >= firstMeasure
                ? GetDirectiveX(span.Start, tonesByMeasure, firstMeasure)
                : FirstMeasureLeft + 8;
            double right = span.End.MeasureIndex <= lastMeasure
                ? GetDirectiveX(span.End, tonesByMeasure, firstMeasure)
                : systemRight - 8;
            if (right - left < 10)
            {
                right = Math.Min(systemRight - 4, left + 18);
            }

            DrawWedge(left, right, staffTop + 88, span.Type == "crescendo");
        }
    }

    private static double GetDirectiveX(
        MusicXmlDirective directive,
        ILookup<int, RecordedTone> tonesByMeasure,
        int firstMeasure)
    {
        int position = directive.MeasureIndex - firstMeasure;
        double measureLeft = FirstMeasureLeft + position * MeasureWidth;
        double contentLeft = measureLeft + 14;
        double contentRight = measureLeft + MeasureWidth - 12;
        int toneCount = tonesByMeasure[directive.MeasureIndex].Count();
        if (toneCount == 0)
        {
            return contentLeft;
        }

        double fraction = Math.Clamp(directive.ToneIndex / (double)toneCount, 0, 1);
        return contentLeft + fraction * (contentRight - contentLeft);
    }

    private void DrawWedge(double left, double right, double y, bool crescendo)
    {
        const double NarrowHalfHeight = 1;
        const double WideHalfHeight = 7;
        double leftHalfHeight = crescendo ? NarrowHalfHeight : WideHalfHeight;
        double rightHalfHeight = crescendo ? WideHalfHeight : NarrowHalfHeight;
        AddLine(left, y - leftHalfHeight, right, y - rightHalfHeight, 1.3);
        AddLine(left, y + leftHalfHeight, right, y + rightHalfHeight, 1.3);
    }

    private static IReadOnlyList<WedgeSpan> CreateWedgeSpans(
        IReadOnlyList<MusicXmlDirective> directives,
        int measureCount)
    {
        var spans = new List<WedgeSpan>();
        var pending = new Dictionary<string, MusicXmlDirective>();
        foreach (MusicXmlDirective directive in directives.Where(directive =>
            directive.Kind == MusicXmlDirectiveKind.Wedge))
        {
            if (directive.Value is "crescendo" or "diminuendo")
            {
                pending[directive.Number] = directive;
            }
            else if (directive.Value == "stop" && pending.Remove(directive.Number, out MusicXmlDirective? start))
            {
                spans.Add(new WedgeSpan(start, directive, start.Value));
            }
        }

        foreach (MusicXmlDirective start in pending.Values)
        {
            var end = new MusicXmlDirective(
                Math.Max(0, measureCount - 1),
                int.MaxValue,
                MusicXmlDirectiveKind.Wedge,
                "stop",
                start.Placement,
                start.Number);
            spans.Add(new WedgeSpan(start, end, start.Value));
        }

        return spans;
    }

    private NotePlacement DrawNote(
        RecordedTone tone,
        double noteX,
        double staffTop,
        bool treble,
        bool colorByPitch,
        bool showPitchLabels,
        bool? stemDownOverride,
        bool isBeamed)
    {
        (int diatonicIndex, bool sharp) = GetDiatonicPosition(tone.Pitch.MidiNote);
        int bottomLineIndex = treble ? 30 : 18;
        double bottomLineY = staffTop + 4 * LineSpacing;
        double noteY = bottomLineY - (diatonicIndex - bottomLineIndex) * (LineSpacing / 2);
        DrawLedgerLines(noteX, noteY, staffTop, bottomLineY);

        Color color = colorByPitch ? GetPitchColor(tone.Pitch.MidiNote) : Colors.Black;
        bool hollow = tone.NoteValue is NotationNoteValue.Whole or NotationNoteValue.Half;
        var noteHead = new Ellipse
        {
            Width = 17,
            Height = 12,
            Fill = new SolidColorBrush(hollow ? Colors.White : color),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2,
            RenderTransform = new RotateTransform
            {
                Angle = -18,
                CenterX = 8.5,
                CenterY = 6
            }
        };
        Canvas.SetLeft(noteHead, noteX - 8.5);
        Canvas.SetTop(noteHead, noteY - 6);
        ScoreCanvas.Children.Add(noteHead);

        bool stemDown = stemDownOverride ?? noteY < staffTop + 2 * LineSpacing;
        double stemX = stemDown ? noteX - 7.5 : noteX + 7.5;
        double stemEndY = stemDown ? noteY + 36 : noteY - 36;
        if (tone.NoteValue != NotationNoteValue.Whole)
        {
            if (!isBeamed)
            {
                AddLine(stemX, noteY, stemX, stemEndY, 1.6, color);
            }

            int flags = isBeamed ? 0 : tone.NoteValue switch
            {
                NotationNoteValue.Eighth => 1,
                NotationNoteValue.Sixteenth => 2,
                _ => 0
            };
            for (int flag = 0; flag < flags; flag++)
            {
                var flagText = new TextBlock
                {
                    Text = stemDown ? "◜" : "◝",
                    FontSize = 20,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(color)
                };
                double flagY = stemDown ? stemEndY - flag * 8 : stemEndY + flag * 8;
                Canvas.SetLeft(flagText, stemDown ? stemX - 1 : stemX - 13);
                Canvas.SetTop(flagText, stemDown ? flagY - 8 : flagY - 14);
                ScoreCanvas.Children.Add(flagText);
            }
        }

        for (int dot = 0; dot < tone.DotCount; dot++)
        {
            var augmentationDot = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = new SolidColorBrush(color)
            };
            Canvas.SetLeft(augmentationDot, noteX + 12 + dot * 7);
            Canvas.SetTop(augmentationDot, noteY - 2);
            ScoreCanvas.Children.Add(augmentationDot);
        }

        if (tone.IsStaccato)
        {
            var staccatoDot = new Ellipse
            {
                Width = 4.5,
                Height = 4.5,
                Fill = new SolidColorBrush(color)
            };
            Canvas.SetLeft(staccatoDot, noteX - 2.25);
            Canvas.SetTop(staccatoDot, stemDown ? noteY - 15 : noteY + 11);
            ScoreCanvas.Children.Add(staccatoDot);
        }

        if (sharp)
        {
            var accidental = new TextBlock
            {
                Text = "♯",
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 20,
                Foreground = new SolidColorBrush(Colors.Black)
            };
            Canvas.SetLeft(accidental, noteX - 26);
            Canvas.SetTop(accidental, noteY - 15);
            ScoreCanvas.Children.Add(accidental);
        }

        if (showPitchLabels)
        {
            var pitchLabel = new TextBlock
            {
                Text = GetPitchClassName(tone.Pitch.NoteName),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color)
            };
            Canvas.SetLeft(pitchLabel, noteX - 10);
            Canvas.SetTop(pitchLabel, staffTop + 4 * LineSpacing + 13);
            ScoreCanvas.Children.Add(pitchLabel);
        }

        return new NotePlacement(tone, noteX, noteY, stemDown, stemX, stemEndY);
    }

    private static string GetPitchClassName(string noteName)
    {
        return new string(noteName.TakeWhile(character => !char.IsDigit(character) && character != '-').ToArray());
    }

    private static List<BeamGroup> CreateBeamGroups(
        IReadOnlyList<RecordedTone> tones,
        double staffTop,
        bool treble)
    {
        var groups = new List<BeamGroup>();
        int index = 0;

        while (index < tones.Count)
        {
            NotationNoteValue value = tones[index].NoteValue;
            if (value is not (NotationNoteValue.Eighth or NotationNoteValue.Sixteenth))
            {
                index++;
                continue;
            }

            int runEnd = index;
            while (runEnd + 1 < tones.Count && tones[runEnd + 1].NoteValue == value)
            {
                runEnd++;
            }

            int groupStart = index;
            while (runEnd - groupStart + 1 >= 2)
            {
                int remaining = runEnd - groupStart + 1;
                int groupLength = Math.Min(4, remaining);
                if (remaining == 5)
                {
                    groupLength = 3;
                }

                int groupEnd = groupStart + groupLength - 1;
                double averageY = Enumerable.Range(groupStart, groupLength)
                    .Average(noteIndex => GetNoteY(tones[noteIndex].Pitch.MidiNote, staffTop, treble));
                bool stemDown = averageY < staffTop + 2 * LineSpacing;
                groups.Add(new BeamGroup(groupStart, groupEnd, stemDown, value));
                groupStart = groupEnd + 1;
            }

            index = runEnd + 1;
        }

        return groups;
    }

    private void DrawBeamGroups(
        IReadOnlyList<NotePlacement> placements,
        IReadOnlyList<BeamGroup> groups)
    {
        foreach (BeamGroup group in groups)
        {
            List<NotePlacement> notes = placements
                .Skip(group.StartIndex)
                .Take(group.EndIndex - group.StartIndex + 1)
                .ToList();
            if (notes.Count < 2)
            {
                continue;
            }

            NotePlacement first = notes[0];
            NotePlacement last = notes[^1];
            double horizontalDistance = last.StemX - first.StemX;
            double slope = Math.Abs(horizontalDistance) < double.Epsilon
                ? 0
                : (last.StemEndY - first.StemEndY) / horizontalDistance;

            // Preserve a visible stem between every note head and the beam.
            // Moving the complete beam keeps its slope unchanged: downward
            // stems move it down, upward stems move it up.
            const double MinimumVisibleStemLength = 18;
            double beamShift = 0;
            foreach (NotePlacement note in notes)
            {
                double unshiftedBeamY = first.StemEndY + (note.StemX - first.StemX) * slope;
                if (group.StemDown)
                {
                    beamShift = Math.Max(
                        beamShift,
                        note.Y + MinimumVisibleStemLength - unshiftedBeamY);
                }
                else
                {
                    beamShift = Math.Min(
                        beamShift,
                        note.Y - MinimumVisibleStemLength - unshiftedBeamY);
                }
            }

            for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
            {
                NotePlacement note = notes[noteIndex];
                // The first and last standard stem ends define the beam. Every
                // stem in between terminates exactly on that shifted straight line.
                double beamY = first.StemEndY +
                    (note.StemX - first.StemX) * slope +
                    beamShift;
                AddLine(note.StemX, note.Y, note.StemX, beamY, 1.8);
                notes[noteIndex] = note with { StemEndY = beamY };
            }

            DrawBeamLine(notes[0], notes[^1], 0);
            if (group.NoteValue == NotationNoteValue.Sixteenth)
            {
                DrawBeamLine(notes[0], notes[^1], group.StemDown ? -7 : 7);
            }
        }
    }

    private void DrawBeamLine(NotePlacement first, NotePlacement last, double yOffset)
    {
        AddLine(
            first.StemX,
            first.StemEndY + yOffset,
            last.StemX,
            last.StemEndY + yOffset,
            5);
    }

    private static double GetNoteY(int midiNote, double staffTop, bool treble)
    {
        (int diatonicIndex, _) = GetDiatonicPosition(midiNote);
        int bottomLineIndex = treble ? 30 : 18;
        return staffTop + 4 * LineSpacing - (diatonicIndex - bottomLineIndex) * (LineSpacing / 2);
    }

    private void DrawNotationCurves(
        IReadOnlyList<NotePlacement> placements,
        double staffTop,
        double systemRight)
    {
        NotePlacement? pendingTie = null;
        NotePlacement? pendingSlur = null;
        double continuationLeft = FirstMeasureLeft + 8;

        foreach (NotePlacement placement in placements)
        {
            if (placement.Tone.TieStops)
            {
                DrawCurve(
                    pendingTie?.X ?? continuationLeft,
                    pendingTie?.Y ?? placement.Y,
                    placement.X,
                    placement.Y,
                    below: true,
                    thickness: 1.6);
                pendingTie = null;
            }

            if (placement.Tone.TieStarts)
            {
                pendingTie = placement;
            }

            if (placement.Tone.SlurStops)
            {
                DrawCurve(
                    pendingSlur?.X ?? continuationLeft,
                    pendingSlur?.Y ?? placement.Y,
                    placement.X,
                    placement.Y,
                    below: false,
                    thickness: 1.4);
                pendingSlur = null;
            }

            if (placement.Tone.SlurStarts)
            {
                pendingSlur = placement;
            }
        }

        if (pendingTie is not null)
        {
            DrawCurve(pendingTie.X, pendingTie.Y, systemRight - 4, pendingTie.Y, below: true, thickness: 1.6);
        }

        if (pendingSlur is not null)
        {
            DrawCurve(pendingSlur.X, pendingSlur.Y, systemRight - 4, staffTop + LineSpacing, below: false, thickness: 1.4);
        }
    }

    private void DrawCurve(
        double startX,
        double startY,
        double endX,
        double endY,
        bool below,
        double thickness)
    {
        double noteOffset = below ? 8 : -8;
        double curveOffset = below ? 16 : -22;
        var figure = new PathFigure
        {
            StartPoint = new Point(startX + 7, startY + noteOffset)
        };
        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Point(
                startX + (endX - startX) * 0.3,
                (below ? Math.Max(startY, endY) : Math.Min(startY, endY)) + curveOffset),
            Point2 = new Point(
                startX + (endX - startX) * 0.7,
                (below ? Math.Max(startY, endY) : Math.Min(startY, endY)) + curveOffset),
            Point3 = new Point(endX - 7, endY + noteOffset)
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        ScoreCanvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(Colors.Black),
            StrokeThickness = thickness
        });
    }

    private void DrawTuplets(
        IReadOnlyList<NotePlacement> placements,
        double staffTop,
        double systemRight)
    {
        bool hasExplicitBoundaries = placements.Any(placement =>
            placement.Tone.TupletStarts || placement.Tone.TupletStops);

        if (hasExplicitBoundaries)
        {
            NotePlacement? pendingTuplet = null;
            foreach (NotePlacement placement in placements)
            {
                if (placement.Tone.TupletStarts && IsSupportedTuplet(placement.Tone.TupletActualNotes))
                {
                    pendingTuplet = placement;
                }

                if (placement.Tone.TupletStops)
                {
                    int actualNotes = pendingTuplet?.Tone.TupletActualNotes
                        ?? placement.Tone.TupletActualNotes;
                    DrawTupletBracket(
                        pendingTuplet?.X ?? FirstMeasureLeft + 8,
                        placement.X,
                        pendingTuplet?.Y ?? placement.Y,
                        placement.Y,
                        staffTop,
                        actualNotes);
                    pendingTuplet = null;
                }
            }

            if (pendingTuplet is not null)
            {
                DrawTupletBracket(
                    pendingTuplet.X,
                    systemRight - 4,
                    pendingTuplet.Y,
                    pendingTuplet.Y,
                    staffTop,
                    pendingTuplet.Tone.TupletActualNotes);
            }

            return;
        }

        // Some generators emit time-modification but omit notations/tuplet.
        // Group those notes by the declared number of actual notes.
        int index = 0;
        while (index < placements.Count)
        {
            NotePlacement first = placements[index];
            int actualNotes = first.Tone.TupletActualNotes;
            int normalNotes = first.Tone.TupletNormalNotes;
            if (!IsSupportedTuplet(actualNotes))
            {
                index++;
                continue;
            }

            int endIndex = index;
            while (endIndex + 1 < placements.Count &&
                   endIndex - index + 1 < actualNotes &&
                   placements[endIndex + 1].Tone.TupletActualNotes == actualNotes &&
                   placements[endIndex + 1].Tone.TupletNormalNotes == normalNotes)
            {
                endIndex++;
            }

            NotePlacement last = placements[endIndex];
            DrawTupletBracket(first.X, last.X, first.Y, last.Y, staffTop, actualNotes);
            index = endIndex + 1;
        }
    }

    private void DrawTupletBracket(
        double startX,
        double endX,
        double startNoteY,
        double endNoteY,
        double staffTop,
        int actualNotes)
    {
        if (!IsSupportedTuplet(actualNotes))
        {
            return;
        }

        double y = Math.Min(Math.Min(startNoteY, endNoteY) - 38, staffTop - 25);
        y = Math.Max(staffTop - 62, y);
        double left = Math.Min(startX, endX) - 7;
        double right = Math.Max(startX, endX) + 7;
        double center = (left + right) / 2;
        const double numberGap = 12;

        AddLine(left, y, center - numberGap, y, 2);
        AddLine(center + numberGap, y, right, y, 2);
        AddLine(left, y, left, y + 10, 2);
        AddLine(right, y, right, y + 10, 2);

        var number = new TextBlock
        {
            Text = actualNotes.ToString(),
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.Black)
        };
        Canvas.SetLeft(number, center - 5);
        Canvas.SetTop(number, y - 12);
        ScoreCanvas.Children.Add(number);
    }

    private static bool IsSupportedTuplet(int actualNotes)
    {
        return actualNotes is >= 2 and <= 9;
    }

    private void DrawLedgerLines(double noteX, double noteY, double staffTop, double bottomLineY)
    {
        if (noteY < staffTop)
        {
            for (double y = staffTop - LineSpacing; y >= noteY - 1; y -= LineSpacing)
            {
                AddLine(noteX - 14, y, noteX + 14, y, 1.1);
            }
        }
        else if (noteY > bottomLineY)
        {
            for (double y = bottomLineY + LineSpacing; y <= noteY + 1; y += LineSpacing)
            {
                AddLine(noteX - 14, y, noteX + 14, y, 1.1);
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
        ScoreCanvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = new SolidColorBrush(color ?? Colors.Black),
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
        return (octave * 7 + scaleStep, pitchClass is 1 or 3 or 6 or 8 or 10);
    }

    private static Color GetPitchColor(int midiNote)
    {
        int pitchClass = ((midiNote % 12) + 12) % 12;
        return pitchClass switch
        {
            0 or 1 => ColorHelper.FromArgb(255, 226, 28, 72),
            2 or 3 => ColorHelper.FromArgb(255, 249, 157, 28),
            4 => Colors.Black,
            5 or 6 => ColorHelper.FromArgb(255, 98, 188, 71),
            7 or 8 => ColorHelper.FromArgb(255, 0, 122, 92),
            9 or 10 => ColorHelper.FromArgb(255, 78, 103, 200),
            _ => ColorHelper.FromArgb(255, 207, 62, 150)
        };
    }

    private sealed record NotePlacement(
        RecordedTone Tone,
        double X,
        double Y,
        bool StemDown,
        double StemX,
        double StemEndY);

    private sealed record BeamGroup(
        int StartIndex,
        int EndIndex,
        bool StemDown,
        NotationNoteValue NoteValue);

    private sealed record WedgeSpan(
        MusicXmlDirective Start,
        MusicXmlDirective End,
        string Type);
}
