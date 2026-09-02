using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace Cello.Notation;

/// <summary>
/// Creates standards-based MusicXML independently from the visual notation
/// renderer, so either component can be replaced without affecting audio code.
/// </summary>
public static class MusicXmlExporter
{
    private const int DivisionsPerQuarter = 4;
    private const int DivisionsPerMeasure = DivisionsPerQuarter * 4;
    private const double SixteenthNoteMillisecondsAt120Bpm = 125;

    public static string CreateSingleNoteScore(PitchResult pitch)
    {
        (string step, int alter, int octave) = GetMusicXmlPitch(pitch.MidiNote);
        XNamespace ns = "http://www.musicxml.org/ns/musicxml";

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("score-partwise", "-//Recordare//DTD MusicXML 4.0 Partwise//EN", "http://www.musicxml.org/dtds/partwise.dtd", null),
            new XElement(ns + "score-partwise",
                new XAttribute("version", "4.0"),
                new XElement(ns + "work",
                    new XElement(ns + "work-title", "Erkannter Celloton")),
                new XElement(ns + "identification",
                    new XElement(ns + "encoding",
                        new XElement(ns + "software", "Cello"),
                        new XElement(ns + "encoding-date", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))),
                new XElement(ns + "part-list",
                    new XElement(ns + "score-part",
                        new XAttribute("id", "P1"),
                        new XElement(ns + "part-name", "Violoncello"),
                        new XElement(ns + "score-instrument",
                            new XAttribute("id", "P1-I1"),
                            new XElement(ns + "instrument-name", "Violoncello")),
                        new XElement(ns + "midi-instrument",
                            new XAttribute("id", "P1-I1"),
                            new XElement(ns + "midi-channel", "1"),
                            new XElement(ns + "midi-program", "43")))),
                new XElement(ns + "part",
                    new XAttribute("id", "P1"),
                    new XElement(ns + "measure",
                        new XAttribute("number", "1"),
                        new XElement(ns + "attributes",
                            new XElement(ns + "divisions", "1"),
                            new XElement(ns + "key", new XElement(ns + "fifths", "0")),
                            new XElement(ns + "time",
                                new XElement(ns + "beats", "4"),
                                new XElement(ns + "beat-type", "4")),
                            new XElement(ns + "clef",
                                new XElement(ns + "sign", pitch.MidiNote >= 60 ? "G" : "F"),
                                new XElement(ns + "line", pitch.MidiNote >= 60 ? "2" : "4"))),
                        new XElement(ns + "note",
                            new XElement(ns + "pitch",
                                new XElement(ns + "step", step),
                                alter == 0 ? null : new XElement(ns + "alter", alter.ToString(CultureInfo.InvariantCulture)),
                                new XElement(ns + "octave", octave.ToString(CultureInfo.InvariantCulture))),
                            new XElement(ns + "duration", "4"),
                            new XElement(ns + "type", "whole"))))));

        return document.ToString();
    }

    public static string CreateRecordedScore(IReadOnlyList<RecordedTone> tones)
    {
        XNamespace ns = "http://www.musicxml.org/ns/musicxml";
        var measures = new List<XElement>();
        int measureNumber = 1;
        int usedDivisions = 0;
        XElement currentMeasure = CreateMeasure(ns, measureNumber, tones.Count == 0 || tones[0].Pitch.MidiNote < 60);
        measures.Add(currentMeasure);

        foreach (RecordedTone tone in tones)
        {
            int remainingToneDivisions = Math.Max(
                1,
                (int)Math.Round(tone.Duration.TotalMilliseconds / SixteenthNoteMillisecondsAt120Bpm));
            bool hasPreviousFragment = false;

            while (remainingToneDivisions > 0)
            {
                if (usedDivisions == DivisionsPerMeasure)
                {
                    currentMeasure = CreateMeasure(ns, ++measureNumber, null);
                    measures.Add(currentMeasure);
                    usedDivisions = 0;
                }

                int roomInMeasure = DivisionsPerMeasure - usedDivisions;
                int fragmentDivisions = LargestStandardDuration(Math.Min(remainingToneDivisions, roomInMeasure));
                int remainingAfterFragment = remainingToneDivisions - fragmentDivisions;
                bool tieStarts = remainingAfterFragment > 0;

                currentMeasure.Add(CreateRecordedNote(
                    ns,
                    tone.Pitch.MidiNote,
                    fragmentDivisions,
                    hasPreviousFragment,
                    tieStarts));

                usedDivisions += fragmentDivisions;
                remainingToneDivisions = remainingAfterFragment;
                hasPreviousFragment = true;
            }
        }

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("score-partwise", "-//Recordare//DTD MusicXML 4.0 Partwise//EN", "http://www.musicxml.org/dtds/partwise.dtd", null),
            new XElement(ns + "score-partwise",
                new XAttribute("version", "4.0"),
                new XElement(ns + "work", new XElement(ns + "work-title", "Fortlaufende Tonaufnahme")),
                new XElement(ns + "identification",
                    new XElement(ns + "encoding",
                        new XElement(ns + "software", "Cello"),
                        new XElement(ns + "encoding-date", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))),
                new XElement(ns + "part-list",
                    new XElement(ns + "score-part",
                        new XAttribute("id", "P1"),
                        new XElement(ns + "part-name", "Aufgenommene Töne"),
                        new XElement(ns + "score-instrument",
                            new XAttribute("id", "P1-I1"),
                            new XElement(ns + "instrument-name", "Violoncello")),
                        new XElement(ns + "midi-instrument",
                            new XAttribute("id", "P1-I1"),
                            new XElement(ns + "midi-channel", "1"),
                            new XElement(ns + "midi-program", "43")))),
                new XElement(ns + "part", new XAttribute("id", "P1"), measures)));

        return document.ToString();
    }

    private static XElement CreateMeasure(XNamespace ns, int number, bool? useBassClef)
    {
        var measure = new XElement(ns + "measure", new XAttribute("number", number));
        if (useBassClef is not null)
        {
            measure.Add(
                new XElement(ns + "attributes",
                    new XElement(ns + "divisions", DivisionsPerQuarter),
                    new XElement(ns + "key", new XElement(ns + "fifths", "0")),
                    new XElement(ns + "time",
                        new XElement(ns + "beats", "4"),
                        new XElement(ns + "beat-type", "4")),
                    new XElement(ns + "clef",
                        new XElement(ns + "sign", useBassClef.Value ? "F" : "G"),
                        new XElement(ns + "line", useBassClef.Value ? "4" : "2"))),
                new XElement(ns + "direction",
                    new XAttribute("placement", "above"),
                    new XElement(ns + "direction-type",
                        new XElement(ns + "metronome",
                            new XElement(ns + "beat-unit", "quarter"),
                            new XElement(ns + "per-minute", "120"))),
                    new XElement(ns + "sound", new XAttribute("tempo", "120"))));
        }

        return measure;
    }

    private static XElement CreateRecordedNote(
        XNamespace ns,
        int midiNote,
        int duration,
        bool tieStops,
        bool tieStarts)
    {
        (string step, int alter, int octave) = GetMusicXmlPitch(midiNote);
        var note = new XElement(ns + "note",
            new XElement(ns + "pitch",
                new XElement(ns + "step", step),
                alter == 0 ? null : new XElement(ns + "alter", alter.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "octave", octave.ToString(CultureInfo.InvariantCulture))),
            tieStops ? new XElement(ns + "tie", new XAttribute("type", "stop")) : null,
            tieStarts ? new XElement(ns + "tie", new XAttribute("type", "start")) : null,
            new XElement(ns + "duration", duration),
            new XElement(ns + "type", GetNoteType(duration)));

        if (tieStops || tieStarts)
        {
            note.Add(new XElement(ns + "notations",
                tieStops ? new XElement(ns + "tied", new XAttribute("type", "stop")) : null,
                tieStarts ? new XElement(ns + "tied", new XAttribute("type", "start")) : null));
        }

        return note;
    }

    private static int LargestStandardDuration(int maximum)
    {
        return maximum switch
        {
            >= 16 => 16,
            >= 8 => 8,
            >= 4 => 4,
            >= 2 => 2,
            _ => 1
        };
    }

    private static string GetNoteType(int duration)
    {
        return duration switch
        {
            16 => "whole",
            8 => "half",
            4 => "quarter",
            2 => "eighth",
            _ => "16th"
        };
    }

    private static (string Step, int Alter, int Octave) GetMusicXmlPitch(int midiNote)
    {
        int pitchClass = ((midiNote % 12) + 12) % 12;
        int octave = midiNote / 12 - 1;
        return pitchClass switch
        {
            0 => ("C", 0, octave),
            1 => ("C", 1, octave),
            2 => ("D", 0, octave),
            3 => ("D", 1, octave),
            4 => ("E", 0, octave),
            5 => ("F", 0, octave),
            6 => ("F", 1, octave),
            7 => ("G", 0, octave),
            8 => ("G", 1, octave),
            9 => ("A", 0, octave),
            10 => ("A", 1, octave),
            _ => ("B", 0, octave)
        };
    }
}
