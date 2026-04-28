using SpaceStationMonitor;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class DifficultyTuningTests
{
    [Fact]
    public void For_Tutorial_ReturnsExpectedTuning()
    {
        Assert.Equal(new DifficultyTuning(4, 0.7, 0.5), DifficultyTunings.For(Difficulty.Tutorial));
    }

    [Fact]
    public void For_Normal_ReturnsExpectedTuning()
    {
        Assert.Equal(new DifficultyTuning(3, 1.0, 1.0), DifficultyTunings.For(Difficulty.Normal));
    }

    [Fact]
    public void For_Hard_ReturnsExpectedTuning()
    {
        Assert.Equal(new DifficultyTuning(2, 1.3, 1.3), DifficultyTunings.For(Difficulty.Hard));
    }

    [Fact]
    public void For_Expert_ReturnsExpectedTuning()
    {
        Assert.Equal(new DifficultyTuning(1, 1.7, 1.6), DifficultyTunings.For(Difficulty.Expert));
    }

    [Fact]
    public void For_UnknownEnumValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DifficultyTunings.For((Difficulty)999));
    }

    [Fact]
    public void RepairsPerCycle_DescendsTutorialToExpert()
    {
        Assert.True(
            DifficultyTunings.For(Difficulty.Tutorial).RepairsPerCycle
            > DifficultyTunings.For(Difficulty.Normal).RepairsPerCycle
            && DifficultyTunings.For(Difficulty.Normal).RepairsPerCycle
            > DifficultyTunings.For(Difficulty.Hard).RepairsPerCycle
            && DifficultyTunings.For(Difficulty.Hard).RepairsPerCycle
            > DifficultyTunings.For(Difficulty.Expert).RepairsPerCycle);
    }

    [Fact]
    public void DegradationMultiplier_AscendsTutorialToExpert()
    {
        Assert.True(
            DifficultyTunings.For(Difficulty.Tutorial).DegradationMultiplier
            < DifficultyTunings.For(Difficulty.Normal).DegradationMultiplier
            && DifficultyTunings.For(Difficulty.Normal).DegradationMultiplier
            < DifficultyTunings.For(Difficulty.Hard).DegradationMultiplier
            && DifficultyTunings.For(Difficulty.Hard).DegradationMultiplier
            < DifficultyTunings.For(Difficulty.Expert).DegradationMultiplier);
    }

    [Fact]
    public void EventChanceMultiplier_AscendsTutorialToExpert()
    {
        Assert.True(
            DifficultyTunings.For(Difficulty.Tutorial).EventChanceMultiplier
            < DifficultyTunings.For(Difficulty.Normal).EventChanceMultiplier
            && DifficultyTunings.For(Difficulty.Normal).EventChanceMultiplier
            < DifficultyTunings.For(Difficulty.Hard).EventChanceMultiplier
            && DifficultyTunings.For(Difficulty.Hard).EventChanceMultiplier
            < DifficultyTunings.For(Difficulty.Expert).EventChanceMultiplier);
    }
}
