using System.Diagnostics;
using System.Diagnostics.Metrics;
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
    public void HandleCancelRepair_DrivesProductionPath_EmitsExpectedTelemetry()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        long repairsFailedCount = 0;
        string? capturedSubsystem = null;
        string? capturedReason = null;
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == Telemetry.MeterName
                    && instrument.Name == "station.repairs.failed")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
        {
            Interlocked.Add(ref repairsFailedCount, measurement);
            foreach (var tag in tags)
            {
                if (tag.Key == "subsystem.name") capturedSubsystem = tag.Value as string;
                else if (tag.Key == "cancellation.reason") capturedReason = tag.Value as string;
            }
        });
        meterListener.Start();

        var (loop, station, _) = BuildLoop(maxCycles: 1);
        var sub = station.Subsystems[0];
        sub.Health = 50;
        var entry = new RepairSystem(new NoOpBugStrategy("Oxygen")).BeginRepair(sub, requested: 20);
        Assert.True(station.ActiveRepairs.TryStart(entry));

        // Drive the production HandleCancelRepair path so any regression in the
        // AddEvent / SetStatus / Stop / Telemetry.RepairsFailed.Add chain surfaces
        // through this test. Replaying those calls inline (the prior test shape)
        // verified the assertion targets but not the production code path.
        loop.HandleCancelRepair();

        Assert.NotNull(entry.RepairAction);
        Assert.Equal(ActivityStatusCode.Error, entry.RepairAction!.Status);
        Assert.Equal("cancelled", entry.RepairAction.StatusDescription);
        Assert.True(entry.RepairAction.Duration > TimeSpan.Zero);

        var cancelEvent = entry.RepairAction.Events.FirstOrDefault(e => e.Name == "RepairCancelled");
        Assert.NotEqual(default, cancelEvent);
        var reason = cancelEvent.Tags.FirstOrDefault(t => t.Key == "cancellation.reason").Value;
        Assert.Equal("player_cancel", reason);

        Assert.Equal(0, station.ActiveRepairs.InFlightCount);

        meterListener.RecordObservableInstruments();
        Assert.Equal(1, repairsFailedCount);
        Assert.Equal("Oxygen", capturedSubsystem);
        Assert.Equal("player_cancel", capturedReason);
    }

    [Fact]
    public void HandleCancelRepair_NoInFlightOnSelected_NoTelemetryEmitted()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        long repairsFailedCount = 0;
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == Telemetry.MeterName
                    && instrument.Name == "station.repairs.failed")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
            Interlocked.Add(ref repairsFailedCount, measurement));
        meterListener.Start();

        var (loop, station, _) = BuildLoop(maxCycles: 1);
        Assert.Equal(0, station.ActiveRepairs.InFlightCount);

        loop.HandleCancelRepair();

        Assert.Equal(0, repairsFailedCount);
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
