using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceStationMonitor.BugStrategies;
using SpaceStationMonitor.Tests.TestHelpers;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class TestModeTests
{
    [Fact]
    public void TestModeConfig_FromEnvironment_AllUnset_DefaultsToOff()
    {
        var config = TestModeConfig.FromEnvironment(_ => null);

        Assert.False(config.TestMode);
        Assert.Null(config.MaxCycles);
        Assert.Null(config.TickInterval);
    }

    [Fact]
    public void TestModeConfig_FromEnvironment_ParsesAllVars()
    {
        var env = new Dictionary<string, string?>
        {
            ["TEST_MODE"] = "1",
            ["TEST_MAX_CYCLES"] = "5",
            ["TEST_TICK_MS"] = "50",
        };
        var config = TestModeConfig.FromEnvironment(k => env.GetValueOrDefault(k));

        Assert.True(config.TestMode);
        Assert.Equal(5, config.MaxCycles);
        Assert.Equal(TimeSpan.FromMilliseconds(50), config.TickInterval);
    }

    [Fact]
    public void TestModeConfig_FromEnvironment_RejectsNonPositiveValues()
    {
        var env = new Dictionary<string, string?>
        {
            ["TEST_MAX_CYCLES"] = "0",
            ["TEST_TICK_MS"] = "-5",
        };
        var config = TestModeConfig.FromEnvironment(k => env.GetValueOrDefault(k));

        Assert.Null(config.MaxCycles);
        Assert.Null(config.TickInterval);
    }

    [Fact]
    public async Task TestMode_SkipsSplash_AndAutoRunsCycles()
    {
        var (loop, station) = BuildLoop(new TestModeConfig(
            TestMode: true, MaxCycles: 1, TickInterval: TimeSpan.FromMilliseconds(10)));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await loop.RunAsync(cts.Token);

        Assert.True(station.CycleCount >= 1, $"Expected ≥1 cycle, got {station.CycleCount}");
    }

    [Fact]
    public async Task TestMaxCycles_ExitsAfterNCycles()
    {
        var (loop, station) = BuildLoop(new TestModeConfig(
            TestMode: true, MaxCycles: 2, TickInterval: TimeSpan.FromMilliseconds(10)));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await loop.RunAsync(cts.Token);

        Assert.Equal(2, station.CycleCount);
    }

    [Fact]
    public async Task TestTickMs_OverridesRandomInterval()
    {
        var (loop, station) = BuildLoop(new TestModeConfig(
            TestMode: true, MaxCycles: 2, TickInterval: TimeSpan.FromMilliseconds(50)));

        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await loop.RunAsync(cts.Token);
        sw.Stop();

        Assert.Equal(2, station.CycleCount);
        Assert.True(sw.ElapsedMilliseconds < 200,
            $"Expected <200ms for 2 cycles at 50ms tick, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Q_KeyPress_TriggersGracefulShutdown()
    {
        // Regression test for the GameLoop refactor's Q-quit bug: PollInputAsync was cancelling
        // its own linked child token, which does NOT propagate to the parent shutdown token, so
        // the cycle loop kept running. The fix routes Q through an upstream onQuit callback that
        // Program.cs wires to its real shutdownCts.Cancel().
        // Q is pre-loaded so the very first PollInputAsync iteration picks it up deterministically.
        var keys = new Queue<char>(new[] { 'Q' });
        bool quitCalled = false;
        using var rootCts = new CancellationTokenSource();

        var station = new Station();
        var strategy = new NoOpBugStrategy();
        var repairSystem = new RepairSystem(strategy);
        var eventEngine = new EventEngine();
        var cascadeEngine = new CascadeEngine(strategy);
        var display = new GameDisplay();
        var random = new Random(42);

        var loop = new GameLoop(
            station, repairSystem, eventEngine, cascadeEngine, display, random,
            NullLogger.Instance, strategy,
            new TestModeConfig(TestMode: false, MaxCycles: null, TickInterval: null),
            onQuit: () =>
            {
                quitCalled = true;
                rootCts.Cancel();
            },
            keyReader: () => keys.Count > 0 ? keys.Dequeue() : (char?)null);

        // Safety guard so a regression doesn't hang the test forever.
        using var safety = CancellationTokenSource.CreateLinkedTokenSource(rootCts.Token);
        safety.CancelAfter(TimeSpan.FromSeconds(3));

        await loop.RunAsync(safety.Token);

        Assert.True(quitCalled, "onQuit callback should fire when 'Q' is read by the input loop");
        Assert.True(rootCts.IsCancellationRequested,
            "root shutdown CTS should be cancelled by the Q handler, not just a child linked token");
        Assert.Equal(0, station.CycleCount);
    }

    [Fact]
    public async Task TestMode_WithBugActivationDelay_StrategyActivatesWithinCycles()
    {
        // AC5 — BUG_ACTIVATION_DELAY_MS=100 + 32 cycles at 10ms tick crosses the
        // activation threshold by ~cycle 11. Drives the env-var path through
        // BugSelector → BugStrategyCatalog → strategy ctor end-to-end so the
        // wire-up is exercised, not just the catalog unit boundary. Seeded so the
        // strategy pick is deterministic across runs.
        var env = new Dictionary<string, string?> { ["BUG_STRATEGY_SEED"] = "1" };
        var (_, strategy) = BugSelector.Select(
            k => env.GetValueOrDefault(k),
            new[] { "Oxygen", "Power", "Shields", "Thermal" },
            TimeSpan.FromMilliseconds(100));

        Assert.False(strategy.IsBugActive);

        var (loop, station) = BuildLoopWithStrategy(strategy, new TestModeConfig(
            TestMode: true, MaxCycles: 32, TickInterval: TimeSpan.FromMilliseconds(10)));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await loop.RunAsync(cts.Token);

        Assert.True(strategy.IsBugActive,
            $"strategy '{strategy.Name}' should activate within 32×10ms cycles given 100ms delay");
    }

    private static (GameLoop loop, Station station) BuildLoop(TestModeConfig config) =>
        BuildLoopWithStrategy(new NoOpBugStrategy(), config);

    private static (GameLoop loop, Station station) BuildLoopWithStrategy(
        IBugStrategy strategy, TestModeConfig config)
    {
        var station = new Station();
        var repairSystem = new RepairSystem(strategy);
        var eventEngine = new EventEngine();
        var cascadeEngine = new CascadeEngine(strategy);
        var display = new GameDisplay();
        var random = new Random(42);
        ILogger logger = NullLogger.Instance;

        var loop = new GameLoop(station, repairSystem, eventEngine, cascadeEngine,
            display, random, logger, strategy, config);
        return (loop, station);
    }
}
