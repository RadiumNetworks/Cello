using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Cello.Notation;

/// <summary>
/// Reads common score metadata and a monophonic pitch preview from MusicXML.
/// External DTD resources are deliberately disabled while the document type
/// declaration itself remains supported.
/// </summary>
public static class MusicXmlReader
{
    public static MusicXmlScore Read(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null
        };

        using var textReader = new StringReader(xml);
        using XmlReader xmlReader = XmlReader.Create(textReader, settings);
        XDocument document = XDocument.Load(xmlReader, LoadOptions.None);
        XElement root = document.Root ?? throw new FormatException("Die MusicXML-Datei enthält kein Wurzelelement.");

        if (root.Name.LocalName != "score-partwise")
        {
            throw new FormatException("Die ausgewählte Datei ist keine unterstützte MusicXML-Partitur.");
        }

        string title = DescendantValue(root, "work-title")
            ?? DescendantValue(root, "movement-title")
            ?? "Unbenannte Partitur";
        string composer = root.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "creator" &&
                string.Equals((string?)element.Attribute("type"), "composer", StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim()
            ?? "Unbekannt";
        string partName = DescendantValue(root, "part-name") ?? "Partitur";

        XElement? part = root.Elements().FirstOrDefault(element => element.Name.LocalName == "part");
        if (part is null)
        {
            throw new FormatException("Die MusicXML-Datei enthält keine lesbare Stimme.");
        }

        var tones = new List<RecordedTone>();
        int divisions = 1;
        double tempo = 120;
        double? scoreTempo = null;
        int measureCount = 0;
        int restCount = 0;
        int keyFifths = 0;
        string? keyMode = null;
        string beats = "4";
        string beatType = "4";

        foreach (XElement measure in part.Elements().Where(element => element.Name.LocalName == "measure"))
        {
            measureCount++;

            XElement? divisionsElement = measure.Descendants().FirstOrDefault(element => element.Name.LocalName == "divisions");
            if (TryParsePositiveInt(divisionsElement?.Value, out int parsedDivisions))
            {
                divisions = parsedDivisions;
            }

            XElement? keyElement = measure.Descendants().FirstOrDefault(element => element.Name.LocalName == "key");
            string? fifthsText = keyElement?.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "fifths")
                ?.Value.Trim();
            if (int.TryParse(fifthsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedFifths))
            {
                keyFifths = Math.Clamp(parsedFifths, -7, 7);
            }
            keyMode = keyElement?.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "mode")
                ?.Value.Trim()
                ?? keyMode;

            XElement? timeElement = measure.Descendants().FirstOrDefault(element => element.Name.LocalName == "time");
            beats = timeElement?.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "beats")
                ?.Value.Trim()
                ?? beats;
            beatType = timeElement?.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "beat-type")
                ?.Value.Trim()
                ?? beatType;

            XElement? soundElement = measure.Descendants().FirstOrDefault(element =>
                element.Name.LocalName == "sound" && element.Attribute("tempo") is not null);
            bool hasSoundTempo = double.TryParse(
                (string?)soundElement?.Attribute("tempo"),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsedTempo) && parsedTempo > 0;
            if (hasSoundTempo)
            {
                tempo = parsedTempo;
                scoreTempo ??= parsedTempo;
            }
            else
            {
                XElement? perMinuteElement = measure.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "per-minute");
                if (double.TryParse(
                    perMinuteElement?.Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out parsedTempo) && parsedTempo > 0)
                {
                    tempo = parsedTempo;
                    scoreTempo ??= parsedTempo;
                }
            }

            foreach (XElement note in measure.Elements().Where(element => element.Name.LocalName == "note"))
            {
                int durationDivisions = TryParsePositiveInt(
                    note.Elements().FirstOrDefault(element => element.Name.LocalName == "duration")?.Value,
                    out int parsedDuration)
                    ? parsedDuration
                    : Math.Max(1, divisions / 2);

                if (note.Elements().Any(element => element.Name.LocalName == "rest"))
                {
                    restCount++;
                    continue;
                }

                XElement? pitch = note.Elements().FirstOrDefault(element => element.Name.LocalName == "pitch");
                if (pitch is null || !TryReadMidiNote(pitch, out int midiNote))
                {
                    continue;
                }

                double frequency = 440 * Math.Pow(2, (midiNote - 69) / 12.0);
                PitchResult pitchResult = PitchResult.FromFrequency(frequency, 1, 0);
                double durationSeconds = durationDivisions / (double)divisions * 60 / tempo;
                long durationTicks = Math.Max(1, (long)Math.Round(durationSeconds * Stopwatch.Frequency));
                string? type = note.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "type")
                    ?.Value.Trim();
                NotationNoteValue noteValue = ParseNoteValue(type, durationDivisions, divisions);
                int dotCount = note.Elements().Count(element => element.Name.LocalName == "dot");
                bool tieStarts = HasType(note, "tie", "start") || HasNotationType(note, "tied", "start");
                bool tieStops = HasType(note, "tie", "stop") || HasNotationType(note, "tied", "stop");
                bool slurStarts = HasNotationType(note, "slur", "start");
                bool slurStops = HasNotationType(note, "slur", "stop");
                bool isStaccato = note.Descendants().Any(element => element.Name.LocalName == "staccato");
                XElement? timeModification = note.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "time-modification");
                int tupletActualNotes = ReadPositiveChildValue(timeModification, "actual-notes");
                int tupletNormalNotes = ReadPositiveChildValue(timeModification, "normal-notes");
                bool tupletStarts = HasNotationType(note, "tuplet", "start");
                bool tupletStops = HasNotationType(note, "tuplet", "stop");
                var tone = new RecordedTone(
                    pitchResult,
                    0,
                    noteValue,
                    measureCount - 1,
                    dotCount,
                    tieStarts,
                    tieStops,
                    slurStarts,
                    slurStops,
                    isStaccato,
                    tupletActualNotes,
                    tupletNormalNotes,
                    tupletStarts,
                    tupletStops);
                tone.Finish(durationTicks);
                tones.Add(tone);
            }
        }

        return new MusicXmlScore(
            title,
            composer,
            partName,
            measureCount,
            restCount,
            tones,
            keyFifths,
            keyMode,
            beats,
            beatType,
            scoreTempo);
    }

    private static bool HasType(XElement note, string localName, string type)
    {
        return note.Elements().Any(element =>
            element.Name.LocalName == localName &&
            string.Equals((string?)element.Attribute("type"), type, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasNotationType(XElement note, string localName, string type)
    {
        return note.Elements()
            .Where(element => element.Name.LocalName == "notations")
            .SelectMany(element => element.Descendants())
            .Any(element =>
                element.Name.LocalName == localName &&
                string.Equals((string?)element.Attribute("type"), type, StringComparison.OrdinalIgnoreCase));
    }

    private static int ReadPositiveChildValue(XElement? parent, string localName)
    {
        string? text = parent?.Elements()
            .FirstOrDefault(element => element.Name.LocalName == localName)
            ?.Value.Trim();
        return TryParsePositiveInt(text, out int value) ? value : 0;
    }

    private static NotationNoteValue ParseNoteValue(string? type, int duration, int divisions)
    {
        NotationNoteValue explicitValue = type?.ToLowerInvariant() switch
        {
            "whole" => NotationNoteValue.Whole,
            "half" => NotationNoteValue.Half,
            "quarter" => NotationNoteValue.Quarter,
            "eighth" => NotationNoteValue.Eighth,
            "16th" => NotationNoteValue.Sixteenth,
            _ => NotationNoteValue.Automatic
        };
        if (explicitValue != NotationNoteValue.Automatic)
        {
            return explicitValue;
        }

        double quarterNotes = duration / (double)Math.Max(1, divisions);
        return quarterNotes switch
        {
            >= 3 => NotationNoteValue.Whole,
            >= 1.5 => NotationNoteValue.Half,
            >= 0.75 => NotationNoteValue.Quarter,
            >= 0.375 => NotationNoteValue.Eighth,
            _ => NotationNoteValue.Sixteenth
        };
    }

    private static string? DescendantValue(XElement root, string localName)
    {
        return root.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == localName)
            ?.Value.Trim();
    }

    private static bool TryReadMidiNote(XElement pitch, out int midiNote)
    {
        midiNote = 0;
        string? step = pitch.Elements().FirstOrDefault(element => element.Name.LocalName == "step")?.Value.Trim();
        string? octaveText = pitch.Elements().FirstOrDefault(element => element.Name.LocalName == "octave")?.Value.Trim();
        if (!int.TryParse(octaveText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int octave))
        {
            return false;
        }

        int pitchClass = step?.ToUpperInvariant() switch
        {
            "C" => 0,
            "D" => 2,
            "E" => 4,
            "F" => 5,
            "G" => 7,
            "A" => 9,
            "B" => 11,
            _ => -100
        };
        if (pitchClass < 0)
        {
            return false;
        }

        string? alterText = pitch.Elements().FirstOrDefault(element => element.Name.LocalName == "alter")?.Value.Trim();
        int alter = double.TryParse(alterText, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedAlter)
            ? (int)Math.Round(parsedAlter)
            : 0;
        midiNote = (octave + 1) * 12 + pitchClass + alter;
        return midiNote is >= 0 and <= 127;
    }

    private static bool TryParsePositiveInt(string? text, out int value)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
    }
}

public sealed record MusicXmlScore(
    string Title,
    string Composer,
    string PartName,
    int MeasureCount,
    int RestCount,
    IReadOnlyList<RecordedTone> Tones,
    int KeyFifths,
    string? KeyMode,
    string Beats,
    string BeatType,
    double? TempoBpm)
{
    public string KeySignatureText => KeyFifths switch
    {
        -7 => "7 ♭ (Ces-Dur / as-Moll)",
        -6 => "6 ♭ (Ges-Dur / es-Moll)",
        -5 => "5 ♭ (Des-Dur / b-Moll)",
        -4 => "4 ♭ (As-Dur / f-Moll)",
        -3 => "3 ♭ (Es-Dur / c-Moll)",
        -2 => "2 ♭ (B-Dur / g-Moll)",
        -1 => "1 ♭ (F-Dur / d-Moll)",
        0 => "Keine Vorzeichen (C-Dur / a-Moll)",
        1 => "1 ♯ (G-Dur / e-Moll)",
        2 => "2 ♯ (D-Dur / h-Moll)",
        3 => "3 ♯ (A-Dur / fis-Moll)",
        4 => "4 ♯ (E-Dur / cis-Moll)",
        5 => "5 ♯ (H-Dur / gis-Moll)",
        6 => "6 ♯ (Fis-Dur / dis-Moll)",
        _ => "7 ♯ (Cis-Dur / ais-Moll)"
    };

    public string TimeSignatureText => $"{Beats}/{BeatType}";

    public string TempoText => TempoBpm is double bpm
        ? $"{bpm.ToString("0.##", CultureInfo.CurrentCulture)} BPM"
        : "Tempo nicht angegeben";
}
