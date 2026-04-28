using SpaceStationMonitor.Sampling;

namespace SpaceStationMonitor;

public static class SplashKeys
{
    public static GameMode? TryParseMode(char keyChar) =>
        char.ToUpperInvariant(keyChar) switch
        {
            '1' => GameMode.JustPlaying,
            '2' => GameMode.Learning,
            _ => null,
        };

    public static Difficulty? TryParseDifficulty(char keyChar) =>
        char.ToUpperInvariant(keyChar) switch
        {
            '1' => Difficulty.Tutorial,
            '2' => Difficulty.Normal,
            '3' => Difficulty.Hard,
            '4' => Difficulty.Expert,
            _ => null,
        };

    public static bool IsQuit(char keyChar) =>
        char.ToUpperInvariant(keyChar) == 'Q';
}
