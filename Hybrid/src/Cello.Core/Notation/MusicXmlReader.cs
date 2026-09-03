using System.Diagnostics;
using System.Globalization;
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
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null };
        using var textReader = new StringReader(xml);
        using XmlReader xmlReader = XmlReader.Create(textReader, settings);
        XDocument document = XDocument.Load(xmlReader, LoadOptions.None);
        XElement root = document.Root ?? throw new FormatException("Die MusicXML-Datei enthält kein Wurzelelement.");

        if (root.Name.LocalName != "score-partwise")
        {
            throw new FormatException("Die ausgewählte Datei ist keine unterstützte MusicXML-Partitur.");
        }

        string title = DescendantValue(root, "work-title") ?? DescendantValue(root, "movement-title") ?? "Unbenannte Partitur";
        string composer = root.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "creator" &&
                string.Equals((string?)element.Attribute("type"), "composer", StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim() ?? "Unbekannt";
        string partName = DescendantValue(root, "part-name") ?? "Partitur";

        XElement? part = root.Elements().FirstOrDefault(element => element.Name.LocalName == "part");
        if (part is null)
        {
            throw new FormatException("Die MusicXML-Datei enthält keine lesbare Stimme.");
        }

        var tones = new List<RecordedTone>();
        var directives = new List<MusicXmlDirective>();
        int divisions = 1;
        double tempo = 120;
        double? scoreTempo = null;
        int measureCount = 0;
        int restCount = 0;
        int keyFifths = 0;
        string? keyMode = null;
        string beats = "4";
        string beatType = "4";
        bool isPizzicato = false;

        foreach (XElement measure in part.Elements().Where(element => element.Name.LocalName == "measure"))
        {
            measureCount++;
            int measureIndex = measureCount - 1;
            ReadDirectives(measure, measureIndex, directives);

            XElement? divisionsElement = measure.Descendants().FirstOrDefault(element => element.Name.LocalName == "divisions");
            if (TryParsePositiveInt(divisionsElement?.Value, out int parsedDivisions))
            {
                divisions = parsedDivisions;
            }

            XElement? keyElement = measure.Descendants().FirstOrDefault(element => element.Name.LocalName == "key");
            string? fifthsText = keyElement?.Elements().FirstOrDefault(element => element.Name.LocalName == "fifths")?.Value.Trim();
            if (int.TryParse(fifthsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedFifths))
            {
                keyFifths = Math.Clamp(parsedFifths, -7, 7);
            }
            keyMode = keyElement?.Elements().FirstOrDefault(element => element.Name.LocalName == "mode")?.Value.Trim() ?? keyMode;

            XElement? timeElement = measure.Descendants().FirstOrDefault(element => element.Name.LocalName == "time");
            beats = timeElement?.Elements().FirstOrDefault(element => element.Name.LocalName == "beats")?.Value.Trim() ?? beats;
            beatType = timeElement?.Elements().FirstOrDefault(element => element.Name.LocalName == "beat-type")?.Value.Trim() ?? beatType;

            XElement? soundElement = measure.Descendants().FirstOrDefault(element => element.Name.LocalName == "sound" && element.Attribute("tempo") is not null);
            bool hasSoundTempo = double.TryParse((string?)soundElement?.Attribute("tempo"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double parsedTempo) && parsedTempo > 0;
            if (hasSoundTempo)
            {
                tempo = parsedTempo;
                scoreTempo ??= parsedTempo;
            }
            else
            {
                XElement? perMinuteElement = measure.Descendants().FirstOrDefault(element => element.Name.LocalName == "per-minute");
                if (double.TryParse(perMinuteElement?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedTempo) && parsedTempo > 0)
                {
                    tempo = parsedTempo;
                    scoreTempo ??= parsedTempo;
                }
            }

            int toneIndexInMeasure = 0;
            foreach (XElement note in measure.Elements().Where(element => element.Name.LocalName == "note"))
            {
                int durationDivisions = TryParsePositiveInt(
                    note.Elements().FirstOrDefault(element => element.Name.LocalName == "duration")?.Value,
                    out int parsedDuration) ? parsedDuration : Math.Max(1, divisions / 2);

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

                isPizzicato = ApplyPizzicatoDirectives(directives, measureIndex, toneIndexInMeasure, isPizzicato);

                double frequency = 440 * Math.Pow(2, (midiNote - 69) / 12.0);
                PitchResult pitchResult = PitchResult.FromFrequency(frequency, 1, 0);
                double durationSeconds = durationDivisions / (double)divisions * 60 / tempo;
                long durationTicks = Math.Max(1, (long)Math.Round(durationSeconds * Stopwatch.Frequency));
                string? type = note.Elements().FirstOrDefault(element => element.Name.LocalName == "type")?.Value.Trim();
                NotationNoteValue noteValue = ParseNoteValue(type, durationDivisions, divisions);
                int dotCount = note.Elements().Count(element => element.Name.LocalName == "dot");
                bool tieStarts = HasType(note, "tie", "start") || HasNotationType(note, "tied", "start");
                bool tieStops = HasType(note, "tie", "stop") || HasNotationType(note, "tied", "stop");
                bool slurStarts = HasNotationType(note, "slur", "start");
                bool slurStops = HasNotationType(note, "slur", "stop");
                bool isStaccato = note.Descendants().Any(element => element.Name.LocalName == "staccato");
                bool isChordContinuation = note.Elements().Any(element => element.Name.LocalName == "chord");
                XElement? timeModification = note.Elements().FirstOrDefault(element => element.Name.LocalName == "time-modification");
                int tupletActualNotes = ReadPositiveChildValue(timeModification, "actual-notes");
                int tupletNormalNotes = ReadPositiveChildValue(timeModification, "normal-notes");
                bool tupletStarts = HasNotationType(note, "tuplet", "start");
                bool tupletStops = HasNotationType(note, "tuplet", "stop");
                NotationStemDirection stemDirection = ParseStemDirection(
                    note.Elements().FirstOrDefault(element => element.Name.LocalName == "stem")?.Value);
                IReadOnlyList<NotationBeam> beams = ParseBeams(note);
                string writtenNoteName = ReadWrittenNoteName(pitch);
                var tone = new RecordedTone(pitchResult, 0, noteValue, measureIndex, dotCount,
                    tieStarts, tieStops, slurStarts, slurStops, isStaccato,
                    tupletActualNotes, tupletNormalNotes, tupletStarts, tupletStops,
                    stemDirection, beams, writtenNoteName, isPizzicato, isChordContinuation);
                tone.Finish(durationTicks);
                tones.Add(tone);
                toneIndexInMeasure++;
            }

            isPizzicato = ApplyPizzicatoDirectives(directives, measureIndex, toneIndexInMeasure, isPizzicato);
        }

        return new MusicXmlScore(title, composer, partName, measureCount, restCount, tones,
            keyFifths, keyMode, beats, beatType, scoreTempo, directives);
    }

    private static void ReadDirectives(XElement measure, int measureIndex, ICollection<MusicXmlDirective> directives)
    {
        int tonesBefore = 0;
        foreach (XElement child in measure.Elements())
        {
            if (child.Name.LocalName == "note")
            {
                XElement? pitch = child.Elements().FirstOrDefault(element => element.Name.LocalName == "pitch");
                if (pitch is not null && TryReadMidiNote(pitch, out _))
                {
                    tonesBefore++;
                }
                continue;
            }

            if (child.Name.LocalName != "direction")
            {
                continue;
            }

            string placement = string.Equals((string?)child.Attribute("placement"), "above", StringComparison.OrdinalIgnoreCase)
                ? "above" : "below";
            bool hasWords = false;

            foreach (XElement directionType in child.Elements().Where(element => element.Name.LocalName == "direction-type"))
            {
                foreach (XElement directionElement in directionType.Elements())
                {
                    switch (directionElement.Name.LocalName)
                    {
                        case "dynamics":
                            foreach (XElement dynamicElement in directionElement.Elements())
                            {
                                string dynamicText = dynamicElement.Name.LocalName == "other-dynamics"
                                    ? dynamicElement.Value.Trim() : dynamicElement.Name.LocalName;
                                if (!string.IsNullOrWhiteSpace(dynamicText))
                                {
                                    directives.Add(new MusicXmlDirective(measureIndex, tonesBefore,
                                        MusicXmlDirectiveKind.Dynamic, dynamicText, placement));
                                }
                            }
                            break;
                        case "words":
                            string words = directionElement.Value.Trim();
                            if (!string.IsNullOrWhiteSpace(words))
                            {
                                hasWords = true;
                                directives.Add(new MusicXmlDirective(measureIndex, tonesBefore,
                                    MusicXmlDirectiveKind.Words, words, placement));
                            }
                            break;
                        case "wedge":
                            string wedgeType = ((string?)directionElement.Attribute("type"))?.Trim().ToLowerInvariant() ?? "stop";
                            string wedgeNumber = ((string?)directionElement.Attribute("number"))?.Trim() ?? "1";
                            directives.Add(new MusicXmlDirective(measureIndex, tonesBefore,
                                MusicXmlDirectiveKind.Wedge, wedgeType, placement, wedgeNumber));
                            break;
                    }
                }
            }

            XElement? sound = child.Elements().FirstOrDefault(element => element.Name.LocalName == "sound");
            string? pizzicato = ((string?)sound?.Attribute("pizzicato"))?.Trim();
            if (pizzicato is not null)
            {
                bool enabled = string.Equals(pizzicato, "yes", StringComparison.OrdinalIgnoreCase);
                directives.Add(new MusicXmlDirective(measureIndex, tonesBefore,
                    MusicXmlDirectiveKind.Pizzicato, enabled ? "yes" : "no", placement));
                if (!hasWords)
                {
                    directives.Add(new MusicXmlDirective(measureIndex, tonesBefore, MusicXmlDirectiveKind.Words,
                        enabled ? "pizz." : "arco", "above"));
                }
            }
        }
    }

    private static bool ApplyPizzicatoDirectives(
        IEnumerable<MusicXmlDirective> directives,
        int measureIndex,
        int toneIndex,
        bool currentValue)
    {
        foreach (MusicXmlDirective directive in directives.Where(directive =>
            directive.MeasureIndex == measureIndex &&
            directive.ToneIndex == toneIndex &&
            directive.Kind == MusicXmlDirectiveKind.Pizzicato))
        {
            currentValue = string.Equals(directive.Value, "yes", StringComparison.OrdinalIgnoreCase);
        }
        return currentValue;
    }

    private static bool HasType(XElement note, string localName, string type) =>
        note.Elements().Any(element => element.Name.LocalName == localName &&
            string.Equals((string?)element.Attribute("type"), type, StringComparison.OrdinalIgnoreCase));

    private static bool HasNotationType(XElement note, string localName, string type) =>
        note.Elements().Where(element => element.Name.LocalName == "notations").SelectMany(element => element.Descendants())
            .Any(element => element.Name.LocalName == localName &&
                string.Equals((string?)element.Attribute("type"), type, StringComparison.OrdinalIgnoreCase));

    private static int ReadPositiveChildValue(XElement? parent, string localName)
    {
        string? text = parent?.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value.Trim();
        return TryParsePositiveInt(text, out int value) ? value : 0;
    }

    private static NotationStemDirection ParseStemDirection(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "up" => NotationStemDirection.Up,
        "down" => NotationStemDirection.Down,
        _ => NotationStemDirection.Automatic
    };

    private static IReadOnlyList<NotationBeam> ParseBeams(XElement note)
    {
        var beams = new List<NotationBeam>();
        foreach (XElement element in note.Elements().Where(element => element.Name.LocalName == "beam"))
        {
            int number = TryParsePositiveInt((string?)element.Attribute("number"), out int parsedNumber)
                ? parsedNumber : 1;
            NotationBeamType? type = element.Value.Trim().ToLowerInvariant() switch
            {
                "begin" => NotationBeamType.Begin,
                "continue" => NotationBeamType.Continue,
                "end" => NotationBeamType.End,
                "forward hook" => NotationBeamType.ForwardHook,
                "backward hook" => NotationBeamType.BackwardHook,
                _ => null
            };
            if (type is not null)
            {
                beams.Add(new NotationBeam(number, type.Value));
            }
        }
        return beams;
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
            "32nd" => NotationNoteValue.ThirtySecond,
            "64th" => NotationNoteValue.SixtyFourth,
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
            >= 0.1875 => NotationNoteValue.Sixteenth,
            >= 0.09375 => NotationNoteValue.ThirtySecond,
            _ => NotationNoteValue.SixtyFourth
        };
    }

    private static string? DescendantValue(XElement root, string localName) =>
        root.Descendants().FirstOrDefault(element => element.Name.LocalName == localName)?.Value.Trim();

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
            "C" => 0, "D" => 2, "E" => 4, "F" => 5, "G" => 7, "A" => 9, "B" => 11, _ => -100
        };
        if (pitchClass < 0)
        {
            return false;
        }

        string? alterText = pitch.Elements().FirstOrDefault(element => element.Name.LocalName == "alter")?.Value.Trim();
        int alter = double.TryParse(alterText, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedAlter)
            ? (int)Math.Round(parsedAlter) : 0;
        midiNote = (octave + 1) * 12 + pitchClass + alter;
        return midiNote is >= 0 and <= 127;
    }

    private static string ReadWrittenNoteName(XElement pitch)
    {
        string step = pitch.Elements().FirstOrDefault(element => element.Name.LocalName == "step")?.Value.Trim().ToUpperInvariant() ?? "?";
        string octave = pitch.Elements().FirstOrDefault(element => element.Name.LocalName == "octave")?.Value.Trim() ?? string.Empty;
        string? alterText = pitch.Elements().FirstOrDefault(element => element.Name.LocalName == "alter")?.Value.Trim();
        int alter = double.TryParse(alterText, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedAlter)
            ? (int)Math.Round(parsedAlter) : 0;
        string accidental = alter switch
        {
            -2 => "♭♭",
            -1 => "♭",
            1 => "♯",
            2 => "𝄪",
            _ => string.Empty
        };
        return $"{step}{accidental}{octave}";
    }

    private static bool TryParsePositiveInt(string? text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
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
    double? TempoBpm,
    IReadOnlyList<MusicXmlDirective> Directives)
{
    public string KeySignatureText => KeyFifths switch
    {
        -7 => "7 ♭ (Ces-Dur / as-Moll)", -6 => "6 ♭ (Ges-Dur / es-Moll)",
        -5 => "5 ♭ (Des-Dur / b-Moll)", -4 => "4 ♭ (As-Dur / f-Moll)",
        -3 => "3 ♭ (Es-Dur / c-Moll)", -2 => "2 ♭ (B-Dur / g-Moll)",
        -1 => "1 ♭ (F-Dur / d-Moll)", 0 => "Keine Vorzeichen (C-Dur / a-Moll)",
        1 => "1 ♯ (G-Dur / e-Moll)", 2 => "2 ♯ (D-Dur / h-Moll)",
        3 => "3 ♯ (A-Dur / fis-Moll)", 4 => "4 ♯ (E-Dur / cis-Moll)",
        5 => "5 ♯ (H-Dur / gis-Moll)", 6 => "6 ♯ (Fis-Dur / dis-Moll)",
        _ => "7 ♯ (Cis-Dur / ais-Moll)"
    };

    public string TimeSignatureText => $"{Beats}/{BeatType}";
    public string TempoText => TempoBpm is double bpm
        ? $"{bpm.ToString("0.##", CultureInfo.CurrentCulture)} BPM" : "Tempo nicht angegeben";
}

public enum MusicXmlDirectiveKind
{
    Dynamic,
    Words,
    Wedge,
    Pizzicato
}

public sealed record MusicXmlDirective(
    int MeasureIndex,
    int ToneIndex,
    MusicXmlDirectiveKind Kind,
    string Value,
    string Placement,
    string Number = "1");
