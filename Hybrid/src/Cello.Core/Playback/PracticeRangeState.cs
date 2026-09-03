namespace Cello.Playback;

/// <summary>
/// Owns the platform-neutral selection and loop state for a bounded sequence
/// of playable tones.
/// </summary>
public sealed class PracticeRangeState
{
    public int ToneCount { get; private set; }

    public int? StartIndex { get; private set; }

    public int? EndIndex { get; private set; }

    public bool IsLooping { get; private set; }

    public bool HasSelection => StartIndex.HasValue;

    public bool IsComplete => StartIndex.HasValue && EndIndex >= StartIndex;

    public int PlaybackStartIndex => StartIndex ?? 0;

    public int PlaybackEndIndex => EndIndex ?? Math.Max(0, ToneCount - 1);

    public void Reset(int toneCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(toneCount);
        ToneCount = toneCount;
        Clear();
    }

    public void SelectTone(int toneIndex)
    {
        if (toneIndex < 0 || toneIndex >= ToneCount)
        {
            throw new ArgumentOutOfRangeException(nameof(toneIndex));
        }

        if (!StartIndex.HasValue || IsComplete)
        {
            StartIndex = toneIndex;
            EndIndex = null;
            IsLooping = false;
            return;
        }

        EndIndex = toneIndex;
        if (EndIndex < StartIndex)
        {
            (StartIndex, EndIndex) = (EndIndex, StartIndex);
        }
    }

    public void SetLooping(bool isLooping)
    {
        IsLooping = isLooping && IsComplete;
    }

    public void Clear()
    {
        StartIndex = null;
        EndIndex = null;
        IsLooping = false;
    }
}
