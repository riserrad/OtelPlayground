namespace SpaceStationMonitor;

public sealed record TestModeConfig(bool TestMode, int? MaxCycles, TimeSpan? TickInterval)
{
    public static readonly TestModeConfig Off = new(false, null, null);

    public static TestModeConfig FromEnvironment(Func<string, string?> getEnv)
    {
        bool testMode = !string.IsNullOrEmpty(getEnv("TEST_MODE"));
        int? maxCycles = int.TryParse(getEnv("TEST_MAX_CYCLES"), out var n) && n > 0 ? n : null;
        TimeSpan? tickInterval = int.TryParse(getEnv("TEST_TICK_MS"), out var ms) && ms > 0
            ? TimeSpan.FromMilliseconds(ms)
            : null;
        return new TestModeConfig(testMode, maxCycles, tickInterval);
    }
}
