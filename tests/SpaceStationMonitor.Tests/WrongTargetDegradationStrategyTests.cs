using SpaceStationMonitor;
using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class WrongTargetDegradationStrategyTests
{
    [Fact]
    public void RedirectDegradationTarget_ActiveAndTargetMatches_ReturnsNextSubsystem()
    {
        var station = new Station();
        var strategy = new WrongTargetDegradationStrategy("Oxygen", TimeSpan.Zero);
        var oxygen = station.Subsystems[0];

        var redirected = strategy.RedirectDegradationTarget(oxygen, station.Subsystems);

        Assert.Equal("Power", redirected.Name);
    }

    [Fact]
    public void RedirectDegradationTarget_ActiveAndTargetIsLast_WrapsToFirst()
    {
        var station = new Station();
        var strategy = new WrongTargetDegradationStrategy("Thermal", TimeSpan.Zero);
        var thermal = station.Subsystems[3];

        var redirected = strategy.RedirectDegradationTarget(thermal, station.Subsystems);

        Assert.Equal("Oxygen", redirected.Name);
    }

    [Fact]
    public void RedirectDegradationTarget_ActiveButRequestIsNotTarget_NoRedirect()
    {
        var station = new Station();
        var strategy = new WrongTargetDegradationStrategy("Oxygen", TimeSpan.Zero);
        var power = station.Subsystems[1];

        var redirected = strategy.RedirectDegradationTarget(power, station.Subsystems);

        Assert.Same(power, redirected);
    }

    [Fact]
    public void RedirectDegradationTarget_StrategyInactive_NoRedirectEvenOnTarget()
    {
        var station = new Station();
        var strategy = new WrongTargetDegradationStrategy("Oxygen", TimeSpan.FromDays(365));
        var oxygen = station.Subsystems[0];

        var redirected = strategy.RedirectDegradationTarget(oxygen, station.Subsystems);

        Assert.Same(oxygen, redirected);
    }
}
