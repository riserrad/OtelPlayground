using SpaceStationMonitor.Sampling;

namespace SpaceStationMonitor;

public static class SplashDefaults
{
    public static (GameMode mode, Difficulty difficulty) Resolve(TestModeConfig config)
    {
        _ = config;
        return (GameMode.Learning, Difficulty.Normal);
    }
}
