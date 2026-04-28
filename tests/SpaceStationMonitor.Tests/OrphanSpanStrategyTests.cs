using System.Diagnostics;
using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class OrphanSpanStrategyTests
{
    [Fact]
    public void OverrideStationCycleParent_BeforeActivation_ReturnsNull()
    {
        // Activation delay sits in the future, so IsBugActive == false on the first call.
        var strategy = new OrphanSpanStrategy(
            bugTarget: "Oxygen",
            activationDelay: TimeSpan.FromHours(1),
            cycleProvider: () => 3);

        Assert.Null(strategy.OverrideStationCycleParent());
        Assert.Equal(0, strategy.InjectedCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    public void OverrideStationCycleParent_OnNonNthCycle_ReturnsNull(int cycle)
    {
        var strategy = new OrphanSpanStrategy(
            bugTarget: "Oxygen",
            activationDelay: TimeSpan.Zero,
            cycleProvider: () => cycle);

        Assert.Null(strategy.OverrideStationCycleParent());
        Assert.Equal(0, strategy.InjectedCount);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(9)]
    public void OverrideStationCycleParent_OnNthCycle_ReturnsRemoteContext(int cycle)
    {
        var strategy = new OrphanSpanStrategy(
            bugTarget: "Oxygen",
            activationDelay: TimeSpan.Zero,
            cycleProvider: () => cycle);

        var ctx = strategy.OverrideStationCycleParent();

        Assert.NotNull(ctx);
        Assert.True(ctx!.Value.IsRemote);
        Assert.NotEqual(default, ctx.Value.SpanId);
        Assert.NotEqual(default, ctx.Value.TraceId);
        Assert.True(ctx.Value.TraceFlags.HasFlag(ActivityTraceFlags.Recorded));
    }

    [Fact]
    public void OverrideStationCycleParent_IncrementsInjectedCount()
    {
        var cycle = 0;
        var strategy = new OrphanSpanStrategy(
            bugTarget: "Oxygen",
            activationDelay: TimeSpan.Zero,
            cycleProvider: () => cycle);

        for (cycle = 1; cycle <= 9; cycle++)
            strategy.OverrideStationCycleParent();

        // Cycles 3, 6, 9 are the injection cycles.
        Assert.Equal(3, strategy.InjectedCount);
    }
}
