using System.Diagnostics;

namespace Cello.Notation;

public enum NotationNoteValue
{
    Automatic,
    Whole,
    Half,
    Quarter,
    Eighth,
    Sixteenth,
    ThirtySecond,
    SixtyFourth
}

public enum NotationStemDirection
{
    Automatic,
    Up,
    Down
}

public enum NotationBeamType
{
    Begin,
    Continue,
    End,
    ForwardHook,
    BackwardHook
}

public sealed record NotationBeam(int Number, NotationBeamType Type);

/// <summary>
/// A detected musical tone together with its measured recording duration.
/// </summary>
public sealed class RecordedTone
{
    public RecordedTone(
        PitchResult pitch,
        long startTimestamp,
        NotationNoteValue noteValue = NotationNoteValue.Automatic,
        int measureIndex = 0,
        int dotCount = 0,
        bool tieStarts = false,
        bool tieStops = false,
        bool slurStarts = false,
        bool slurStops = false,
        bool isStaccato = false,
        int tupletActualNotes = 0,
        int tupletNormalNotes = 0,
        bool tupletStarts = false,
        bool tupletStops = false,
        NotationStemDirection stemDirection = NotationStemDirection.Automatic,
        IReadOnlyList<NotationBeam>? beams = null,
        string? writtenNoteName = null,
        bool isPizzicato = false,
        bool isChordContinuation = false)
    {
        Pitch = pitch;
        StartTimestamp = startTimestamp;
        EndTimestamp = startTimestamp;
        NoteValue = noteValue;
        MeasureIndex = measureIndex;
        DotCount = dotCount;
        TieStarts = tieStarts;
        TieStops = tieStops;
        SlurStarts = slurStarts;
        SlurStops = slurStops;
        IsStaccato = isStaccato;
        TupletActualNotes = tupletActualNotes;
        TupletNormalNotes = tupletNormalNotes;
        TupletStarts = tupletStarts;
        TupletStops = tupletStops;
        StemDirection = stemDirection;
        Beams = beams ?? Array.Empty<NotationBeam>();
        WrittenNoteName = writtenNoteName;
        IsPizzicato = isPizzicato;
        IsChordContinuation = isChordContinuation;
    }

    public PitchResult Pitch { get; private set; }
    public long StartTimestamp { get; }
    public long EndTimestamp { get; private set; }
    public NotationNoteValue NoteValue { get; }
    public int MeasureIndex { get; }
    public int DotCount { get; }
    public bool TieStarts { get; }
    public bool TieStops { get; }
    public bool SlurStarts { get; }
    public bool SlurStops { get; }
    public bool IsStaccato { get; }
    public int TupletActualNotes { get; }
    public int TupletNormalNotes { get; }
    public bool TupletStarts { get; }
    public bool TupletStops { get; }
    public NotationStemDirection StemDirection { get; }
    public IReadOnlyList<NotationBeam> Beams { get; }
    public string? WrittenNoteName { get; }
    public bool IsPizzicato { get; }
    public bool IsChordContinuation { get; }
    public string DisplayNoteName => WrittenNoteName ?? Pitch.NoteName;

    public TimeSpan Duration => TimeSpan.FromSeconds(
        Math.Max(0, EndTimestamp - StartTimestamp) / (double)Stopwatch.Frequency);

    public string DurationText => $"{Math.Max(0.08, Duration.TotalSeconds):F1} s";

    public void Update(PitchResult pitch, long timestamp)
    {
        Pitch = pitch;
        EndTimestamp = Math.Max(EndTimestamp, timestamp);
    }

    public void Finish(long timestamp)
    {
        EndTimestamp = Math.Max(EndTimestamp, timestamp);
    }
}
