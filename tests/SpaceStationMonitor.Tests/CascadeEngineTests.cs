using SpaceStationMonitor;
using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class CascadeEngineTests
{
    [Fact]
    public void CheckCascades_NoCriticalSystems_NoCascades()
    {
        var station = new Station();
        var engine = new CascadeEngine(new NoOpBugStrategy("Oxygen"));

        var results = engine.CheckAndApplyCascades(station, isBugActive: false);

        Assert.Empty(results);
        foreach (var sub in station.Subsystems)
            Assert.Equal(1.0, sub.CascadeMultiplier);
    }

    [Fact]
    public void CheckCascades_CriticalSystem_TriggersForOthers()
    {
        var station = new Station();
        station.Subsystems[0].Health = 20; // Oxygen goes critical
        var engine = new CascadeEngine(new NoOpBugStrategy("Oxygen"));

        var results = engine.CheckAndApplyCascades(station, isBugActive: false);

        Assert.Single(results);
        Assert.Equal("Oxygen", results[0].SourceSubsystem);
        Assert.True(results[0].Triggered);
        Assert.Equal(3, results[0].AffectedSubsystems.Length);

        // Other subsystems should have increased cascade multiplier
        Assert.Equal(1.0, station.Subsystems[0].CascadeMultiplier); // source not affected by own cascade
        Assert.Equal(1.5, station.Subsystems[1].CascadeMultiplier);
        Assert.Equal(1.5, station.Subsystems[2].CascadeMultiplier);
        Assert.Equal(1.5, station.Subsystems[3].CascadeMultiplier);
    }

    [Fact]
    public void CheckCascades_MultipleCritical_StacksMultipliers()
    {
        var station = new Station();
        station.Subsystems[0].Health = 20; // Oxygen critical
        station.Subsystems[2].Health = 15; // Shields critical
        var engine = new CascadeEngine(new NoOpBugStrategy("Oxygen"));

        var results = engine.CheckAndApplyCascades(station, isBugActive: false);

        Assert.Equal(2, results.Count);

        // Power and Thermal affected by both cascades: 1.0 + 0.5 + 0.5 = 2.0
        Assert.Equal(1.5, station.Subsystems[0].CascadeMultiplier); // Oxygen: from Shields cascade only
        Assert.Equal(2.0, station.Subsystems[1].CascadeMultiplier); // Power: from both
        Assert.Equal(1.5, station.Subsystems[2].CascadeMultiplier); // Shields: from Oxygen cascade only
        Assert.Equal(2.0, station.Subsystems[3].CascadeMultiplier); // Thermal: from both
    }
}
