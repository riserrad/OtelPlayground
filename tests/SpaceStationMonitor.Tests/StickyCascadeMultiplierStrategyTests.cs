using SpaceStationMonitor;
using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class StickyCascadeMultiplierStrategyTests
{
    [Fact]
    public void Cascade_StrategyActive_KeepsMultipliersElevatedAcrossTicks()
    {
        var station = new Station();
        station.Subsystems[0].Health = 20; // Oxygen critical
        var strategy = new StickyCascadeMultiplierStrategy("Oxygen", TimeSpan.Zero);
        var engine = new CascadeEngine(strategy);

        engine.CheckAndApplyCascades(station, isBugActive: false);
        Assert.Equal(1.5, station.Subsystems[1].CascadeMultiplier); // Power elevated

        // Heal Oxygen so it's no longer critical — without sticky bug, cascade would reset.
        station.Subsystems[0].Health = 100;

        engine.CheckAndApplyCascades(station, isBugActive: false);

        // Sticky bug prevents the reset: Power stays elevated.
        Assert.Equal(1.5, station.Subsystems[1].CascadeMultiplier);
    }

    [Fact]
    public void Cascade_NoOpStrategy_ResetsMultipliersWhenSourceRecovers()
    {
        var station = new Station();
        station.Subsystems[0].Health = 20; // Oxygen critical
        var strategy = new NoOpBugStrategy("Oxygen");
        var engine = new CascadeEngine(strategy);

        engine.CheckAndApplyCascades(station, isBugActive: false);
        Assert.Equal(1.5, station.Subsystems[1].CascadeMultiplier);

        station.Subsystems[0].Health = 100;

        engine.CheckAndApplyCascades(station, isBugActive: false);

        Assert.Equal(1.0, station.Subsystems[1].CascadeMultiplier);
    }

    [Fact]
    public void ShouldResetCascadeMultipliers_StrategyActive_ReturnsFalse()
    {
        var strategy = new StickyCascadeMultiplierStrategy("Oxygen", TimeSpan.Zero);

        Assert.False(strategy.ShouldResetCascadeMultipliers());
    }

    [Fact]
    public void ShouldResetCascadeMultipliers_StrategyInactive_ReturnsTrue()
    {
        var strategy = new StickyCascadeMultiplierStrategy("Oxygen", TimeSpan.FromDays(365));

        Assert.True(strategy.ShouldResetCascadeMultipliers());
    }
}
