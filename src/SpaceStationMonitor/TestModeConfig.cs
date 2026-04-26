namespace SpaceStationMonitor;

/// <summary>
/// Snapshot of the QA-driven test-mode environment variables, parsed once at startup.
/// </summary>
/// <param name="TestMode">
/// When true, <c>Program.cs</c> bypasses the splash gate and <see cref="GameLoop"/>
/// skips keyboard polling, replacing it with a pure <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
/// so the game runs autonomously. Sourced from the <c>TEST_MODE</c> env var.
/// </param>
/// <param name="MaxCycles">
/// Optional cycle budget. When set to a positive integer, <see cref="GameLoop"/> exits cleanly
/// after that many cycles complete (game-over screen still renders). Sourced from <c>TEST_MAX_CYCLES</c>.
/// </param>
/// <param name="TickInterval">
/// Optional deterministic per-cycle wait. Overrides the default 4–8 s pre-bug / 2–4 s post-bug
/// random interval so QA can drive the loop at a known cadence. Sourced from <c>TEST_TICK_MS</c>.
/// </param>
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
