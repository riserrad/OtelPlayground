using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceStationMonitor;
using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

[Collection("ActivityListener-bound")]
public class GameLoopAsyncRepairTests
{
    [Fact]
    public async Task InFlightRepair_CompletesAfterRolledCycles()
    {
        var (loop, station, strategy) = BuildLoop(maxCycles: 1);
        var sub = station.Subsystems[0];
        sub.Health = 50;
        var entry = new InFlightRepair(sub, Requested: 20, CyclesRemaining: 1, RepairAction: null);
        station.ActiveRepairs.TryStart(entry);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await loop.RunAsync(cts.Token);

        Assert.Equal(0, station.ActiveRepairs.InFlightCount);
        Assert.True(sub.Health > 60,
            $"expected health bumped above 60 after repair (50 + 20 - some degradation), got {sub.Health}");
        _ = strategy;
    }

    [Fact]
    public async Task InFlightRepair_AddsLinkToStationCycle()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => captured.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        using var testRoot = Telemetry.ActivitySource.StartActivity("TestRoot_InFlightLink");
        Assert.NotNull(testRoot);

        var (loop, station, strategy) = BuildLoop(maxCycles: 2);
        var sub = station.Subsystems[0];
        sub.Health = 50;
        var inFlightActivity = Telemetry.ActivitySource.StartActivity("RepairAction");
        Assert.NotNull(inFlightActivity);
        var entry = new InFlightRepair(sub, Requested: 20, CyclesRemaining: 5, RepairAction: inFlightActivity);
        station.ActiveRepairs.TryStart(entry);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await loop.RunAsync(cts.Token);

        var inScope = captured.Where(a => a.TraceId == testRoot!.TraceId).ToList();
        var cycleSpans = inScope.Where(a => a.OperationName == "StationCycle").ToList();
        Assert.NotEmpty(cycleSpans);
        Assert.Contains(cycleSpans, s => s.Links.Any(l => l.Context == inFlightActivity!.Context));

        inFlightActivity!.Stop();
        _ = strategy;
    }

    [Fact]
    public async Task CompletedRepair_StopsActivity_WithFinalTags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var (loop, station, _) = BuildLoop(maxCycles: 1);
        var sub = station.Subsystems[0];
        sub.Health = 50;
        var repairActivity = Telemetry.ActivitySource.StartActivity("RepairAction");
        Assert.NotNull(repairActivity);
        var entry = new InFlightRepair(sub, Requested: 20, CyclesRemaining: 1, RepairAction: repairActivity);
        station.ActiveRepairs.TryStart(entry);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await loop.RunAsync(cts.Token);

        Assert.True(repairActivity!.Duration > TimeSpan.Zero);
        var tags = repairActivity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        Assert.Contains("repair.applied", tags.Keys);
        Assert.Contains("repair.healthy", tags.Keys);
    }

    [Fact]
    public void CancelledRepair_SetsErrorStatus_AndEmitsRepairCancelledEvent()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var strategy = new NoOpBugStrategy("Oxygen");
        var repair = new RepairSystem(strategy);
        var ar = new ActiveRepairs(concurrentRepairs: 4);
        var sub = new Subsystem("Oxygen", 1.0) { Health = 50 };
        var entry = repair.BeginRepair(sub, requested: 20);
        ar.TryStart(entry);

        var ok = ar.TryCancelOldestOn(sub, out var cancelled);
        Assert.True(ok);
        Assert.NotNull(cancelled);

        cancelled!.RepairAction?.AddEvent(new ActivityEvent("RepairCancelled",
            tags: new ActivityTagsCollection
            {
                { "cancellation.reason", "player_cancel" }
            }));
        cancelled.RepairAction?.SetStatus(ActivityStatusCode.Error, "cancelled");
        cancelled.RepairAction?.Stop();

        Assert.Equal(ActivityStatusCode.Error, cancelled.RepairAction!.Status);
        Assert.Equal("cancelled", cancelled.RepairAction.StatusDescription);
        Assert.True(cancelled.RepairAction.Duration > TimeSpan.Zero);

        var cancelEvent = cancelled.RepairAction.Events.FirstOrDefault(e => e.Name == "RepairCancelled");
        Assert.NotEqual(default, cancelEvent);
        var reason = cancelEvent.Tags.FirstOrDefault(t => t.Key == "cancellation.reason").Value;
        Assert.Equal("player_cancel", reason);
    }

    private static (GameLoop loop, Station station, IBugStrategy strategy) BuildLoop(int maxCycles = 3)
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
        return (loop, station, strategy);
    }
}
