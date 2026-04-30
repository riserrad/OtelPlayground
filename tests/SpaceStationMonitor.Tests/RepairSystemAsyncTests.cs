using System.Diagnostics;
using SpaceStationMonitor;
using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

[Collection("ActivityListener-bound")]
public class RepairSystemAsyncTests
{
    private static ActivityListener StartListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public void BeginRepair_StartsActivity_WithExpectedTags()
    {
        using var listener = StartListener();
        var strategy = new NoOpBugStrategy("Oxygen");
        var repair = new RepairSystem(strategy);
        var sub = new Subsystem("Oxygen", 1.0) { Health = 50 };

        var entry = repair.BeginRepair(sub, requested: 20);

        Assert.NotNull(entry.RepairAction);
        var tags = entry.RepairAction!.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("Oxygen", tags["subsystem.name"]);
        Assert.Equal(20, tags["repair.requested"]);
        Assert.Equal(entry.CyclesRemaining, tags["repair.cycles_required"]);

        entry.RepairAction.Stop();
    }

    [Theory]
    [InlineData(100.0, 1)]
    [InlineData(99.0, 1)]
    [InlineData(67.0, 1)]
    [InlineData(66.9, 2)]
    [InlineData(34.0, 2)]
    [InlineData(33.9, 3)]
    [InlineData(1.0, 3)]
    public void BeginRepair_RollsCyclesBetween1And3(double health, int expectedCycles)
    {
        using var listener = StartListener();
        var strategy = new NoOpBugStrategy("Oxygen");
        var repair = new RepairSystem(strategy);
        var sub = new Subsystem("Oxygen", 1.0) { Health = health };

        var entry = repair.BeginRepair(sub, requested: 20);

        Assert.Equal(expectedCycles, entry.CyclesRemaining);
        Assert.InRange(entry.CyclesRemaining, 1, 3);

        entry.RepairAction?.Stop();
    }

    [Fact]
    public void CompleteRepair_AppliesHealth_AndStopsActivity()
    {
        using var listener = StartListener();
        var strategy = new NoOpBugStrategy("Oxygen");
        var repair = new RepairSystem(strategy);
        var sub = new Subsystem("Oxygen", 1.0) { Health = 50 };

        var entry = repair.BeginRepair(sub, requested: 20);
        Assert.NotNull(entry.RepairAction);
        Assert.False(entry.RepairAction!.Duration > TimeSpan.Zero);

        var result = repair.CompleteRepair(entry);

        Assert.Equal(20, result.Applied);
        Assert.True(result.IsHealthy);
        Assert.Equal(70.0, sub.Health);
        Assert.True(entry.RepairAction.Duration > TimeSpan.Zero);

        var tags = entry.RepairAction.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal(20, tags["repair.applied"]);
        Assert.Equal(true, tags["repair.healthy"]);
    }

    [Fact]
    public void CompleteRepair_OnLeak_SetsRepairHealthyFalse()
    {
        using var listener = StartListener();
        var strategy = new LeakyRepairStrategy("Oxygen", TimeSpan.Zero);
        // BugStrategyBase.IsBugActive checks elapsed since construction; sleep so any
        // positive elapsed time clears the threshold deterministically.
        Thread.Sleep(5);

        var repair = new RepairSystem(strategy);
        var sub = new Subsystem("Oxygen", 1.0) { Health = 50 };

        var entry = repair.BeginRepair(sub, requested: 20);
        var result = repair.CompleteRepair(entry);

        Assert.False(result.IsHealthy);
        Assert.True(result.Applied < result.Requested);

        var tags = entry.RepairAction!.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal(false, tags["repair.healthy"]);
        var leakEvent = entry.RepairAction.Events.FirstOrDefault(e => e.Name == "RepairLeak");
        Assert.NotEqual(default, leakEvent);
    }
}
