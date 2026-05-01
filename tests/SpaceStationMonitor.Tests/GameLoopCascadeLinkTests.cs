using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceStationMonitor;
using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

[Collection("ActivityListener-bound")]
public class GameLoopCascadeLinkTests
{
    [Fact]
    public async Task CascadeCheck_LinksToSourceSubsystemTick()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => captured.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        // Wrap the cycle in a unique parent so we can scope captured activities to this
        // test's trace and ignore activities from parallel test runs.
        using var testRoot = Telemetry.ActivitySource.StartActivity("TestRoot_CascadeLink");
        Assert.NotNull(testRoot);

        var (loop, station) = BuildLoop(maxCycles: 1);
        station.Subsystems[0].Health = 10;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await loop.RunAsync(cts.Token);

        var inScope = captured.Where(a => a.TraceId == testRoot!.TraceId).ToList();
        var cascadeSpans = inScope.Where(a => a.OperationName == "CascadeCheck").ToList();
        Assert.NotEmpty(cascadeSpans);

        var tickSpansBySub = inScope
            .Where(a => a.OperationName == "SubsystemTick")
            .GroupBy(a => a.GetTagItem("subsystem.name") as string ?? "")
            .ToDictionary(g => g.Key, g => g.ToList());

        bool atLeastOneLinked = false;
        foreach (var cascade in cascadeSpans)
        {
            var source = cascade.GetTagItem("source.subsystem") as string;
            Assert.NotNull(source);
            if (tickSpansBySub.TryGetValue(source!, out var sourceTicks))
            {
                if (cascade.Links.Any(l => sourceTicks.Any(t => t.Context == l.Context)))
                    atLeastOneLinked = true;
            }
        }
        Assert.True(atLeastOneLinked, "expected at least one CascadeCheck linked to its source SubsystemTick");
    }

    [Fact]
    public async Task CascadeCheck_LinksToSourceSubsystemTick_UnderWrongTargetDegradation()
    {
        // Drive the divergent path where sub.Name != actualTarget.Name. Under
        // WrongTargetDegradationStrategy with target=Oxygen, the Oxygen tick gets
        // redirected to Power. The dict must be keyed by sub.Name so the cascade
        // engine's lookup of cascade.SourceSubsystem ("Oxygen") finds Oxygen's tick.
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => captured.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        using var testRoot = Telemetry.ActivitySource.StartActivity("TestRoot_WrongTarget");
        Assert.NotNull(testRoot);

        var strategy = new WrongTargetDegradationStrategy("Oxygen", TimeSpan.Zero);
        // BugStrategyBase activation predicate is strict-greater-than; sleep so any
        // positive elapsed time clears the threshold deterministically.
        while (!strategy.IsBugActive)
            Thread.Sleep(2);
        Assert.True(strategy.IsBugActive);

        var station = new Station();
        var repairSystem = new RepairSystem(strategy);
        var eventEngine = new EventEngine(eventChanceMultiplier: 0.0);
        var cascadeEngine = new CascadeEngine(strategy);
        var display = new GameDisplay();
        var random = new Random(42);
        var config = new TestModeConfig(TestMode: true, MaxCycles: 1, TickInterval: TimeSpan.FromMilliseconds(10));

        var loop = new GameLoop(station, repairSystem, eventEngine, cascadeEngine,
            display, random, NullLogger.Instance, strategy, config);

        // Force a cascade on Oxygen (the source of the redirected tick).
        station.Subsystems[0].Health = 10;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await loop.RunAsync(cts.Token);

        var inScope = captured.Where(a => a.TraceId == testRoot!.TraceId).ToList();
        var cascadeSpans = inScope.Where(a => a.OperationName == "CascadeCheck").ToList();
        Assert.NotEmpty(cascadeSpans);

        var oxygenSourcedCascades = cascadeSpans
            .Where(c => (c.GetTagItem("source.subsystem") as string) == "Oxygen")
            .ToList();
        Assert.NotEmpty(oxygenSourcedCascades);

        // Oxygen's tick activity has subsystem.name="Power" because of the redirect's
        // tag set. After the dict-key fix, tickActivities["Oxygen"] still points at
        // that Power-tagged tick — so the link binds.
        var redirectedTicks = inScope
            .Where(a => a.OperationName == "SubsystemTick"
                && (a.GetTagItem("subsystem.name") as string) == "Power")
            .ToList();
        Assert.NotEmpty(redirectedTicks);

        bool linked = oxygenSourcedCascades.Any(c =>
            c.Links.Any(l => redirectedTicks.Any(t => t.Context == l.Context)));
        Assert.True(linked,
            "expected the Oxygen-sourced CascadeCheck to link back to the redirected SubsystemTick");
    }

    [Fact]
    public async Task CascadeCheck_WhenSourceTickSampledOut_HasNoLink()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Name == "SubsystemTick"
                    ? ActivitySamplingResult.None
                    : ActivitySamplingResult.AllData,
            ActivityStopped = a => captured.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        using var testRoot = Telemetry.ActivitySource.StartActivity("TestRoot_CascadeNoLink");
        Assert.NotNull(testRoot);

        var (loop, station) = BuildLoop(maxCycles: 1);
        station.Subsystems[0].Health = 10;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await loop.RunAsync(cts.Token);

        var inScope = captured.Where(a => a.TraceId == testRoot!.TraceId).ToList();
        var cascadeSpans = inScope.Where(a => a.OperationName == "CascadeCheck").ToList();
        Assert.NotEmpty(cascadeSpans);
        foreach (var cascade in cascadeSpans)
            Assert.Empty(cascade.Links);
    }

    private static (GameLoop loop, Station station) BuildLoop(int maxCycles = 1)
    {
        var station = new Station();
        var strategy = new NoOpBugStrategy("Oxygen");
        var repairSystem = new RepairSystem(strategy);
        var eventEngine = new EventEngine(eventChanceMultiplier: 0.0);
        var cascadeEngine = new CascadeEngine(strategy);
        var display = new GameDisplay();
        var random = new Random(42);
        var config = new TestModeConfig(TestMode: true, MaxCycles: maxCycles, TickInterval: TimeSpan.FromMilliseconds(10));

        var loop = new GameLoop(station, repairSystem, eventEngine, cascadeEngine,
            display, random, NullLogger.Instance, strategy, config);
        return (loop, station);
    }
}
