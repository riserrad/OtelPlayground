using SpaceStationMonitor;
using SpaceStationMonitor.Sampling;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class SplashKeysTests
{
    [Theory]
    [InlineData('1', GameMode.JustPlaying)]
    [InlineData('2', GameMode.Learning)]
    public void TryParseMode_RecognizedKeys(char key, GameMode expected)
    {
        Assert.Equal(expected, SplashKeys.TryParseMode(key));
    }

    [Theory]
    [InlineData('0')]
    [InlineData('3')]
    [InlineData('9')]
    [InlineData('A')]
    [InlineData(' ')]
    public void TryParseMode_UnrecognizedKeys_ReturnNull(char key)
    {
        Assert.Null(SplashKeys.TryParseMode(key));
    }

    [Theory]
    [InlineData('1', Difficulty.Tutorial)]
    [InlineData('2', Difficulty.Normal)]
    [InlineData('3', Difficulty.Hard)]
    [InlineData('4', Difficulty.Expert)]
    public void TryParseDifficulty_RecognizedKeys(char key, Difficulty expected)
    {
        Assert.Equal(expected, SplashKeys.TryParseDifficulty(key));
    }

    [Theory]
    [InlineData('0')]
    [InlineData('5')]
    [InlineData('9')]
    [InlineData('A')]
    [InlineData(' ')]
    public void TryParseDifficulty_UnrecognizedKeys_ReturnNull(char key)
    {
        Assert.Null(SplashKeys.TryParseDifficulty(key));
    }

    [Theory]
    [InlineData('q', true)]
    [InlineData('Q', true)]
    [InlineData('X', false)]
    [InlineData('1', false)]
    public void IsQuit_RecognizesUpperAndLower(char key, bool expected)
    {
        Assert.Equal(expected, SplashKeys.IsQuit(key));
    }
}
