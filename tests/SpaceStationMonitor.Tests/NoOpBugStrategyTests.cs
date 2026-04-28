using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class NoOpBugStrategyTests
{
    [Fact]
    public void Name_IsNoOp()
    {
        var strategy = new NoOpBugStrategy("Oxygen");
        Assert.Equal("NoOp", strategy.Name);
    }

    [Fact]
    public void IsBugActive_AlwaysFalse()
    {
        var strategy = new NoOpBugStrategy("Oxygen");
        Assert.False(strategy.IsBugActive);
    }

    [Fact]
    public void OnRepair_PassesThrough()
    {
        var strategy = new NoOpBugStrategy("Oxygen");
        var sub = new Subsystem("Oxygen", baseDegradationRate: 1.0);
        int retries = 0;
        Assert.Equal(42, strategy.OnRepair(sub, requested: 42, ref retries));
    }

    [Fact]
    public void RedirectDegradationTarget_ReturnsRequested()
    {
        var strategy = new NoOpBugStrategy("Oxygen");
        var sub = new Subsystem("Oxygen", baseDegradationRate: 1.0);
        var all = new[] { sub, new Subsystem("Power", 1.0) };
        Assert.Same(sub, strategy.RedirectDegradationTarget(sub, all));
    }

    [Fact]
    public void NotRegisteredInCatalog()
    {
        var strategies = BugStrategyCatalog.All("Oxygen");
        Assert.DoesNotContain(strategies, s => s.Name == "NoOp");
    }
}
