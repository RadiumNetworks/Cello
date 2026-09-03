using Cello.Playback;

namespace Cello.Core.Tests;

public sealed class PracticeRangeStateTests
{
    [Fact]
    public void EmptyRangeUsesSequenceBoundaries()
    {
        var range = new PracticeRangeState();
        range.Reset(5);

        Assert.Equal(0, range.PlaybackStartIndex);
        Assert.Equal(4, range.PlaybackEndIndex);
        Assert.False(range.IsComplete);
    }

    [Fact]
    public void ReverseSelectionIsNormalized()
    {
        var range = new PracticeRangeState();
        range.Reset(8);

        range.SelectTone(6);
        range.SelectTone(2);

        Assert.Equal(2, range.StartIndex);
        Assert.Equal(6, range.EndIndex);
        Assert.True(range.IsComplete);
    }

    [Fact]
    public void LoopRequiresCompleteRangeAndClearResetsState()
    {
        var range = new PracticeRangeState();
        range.Reset(4);
        range.SelectTone(1);
        range.SetLooping(true);
        Assert.False(range.IsLooping);

        range.SelectTone(3);
        range.SetLooping(true);
        Assert.True(range.IsLooping);

        range.Clear();
        Assert.False(range.HasSelection);
        Assert.False(range.IsLooping);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void SelectionOutsideToneCountIsRejected(int index)
    {
        var range = new PracticeRangeState();
        range.Reset(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => range.SelectTone(index));
    }
}
