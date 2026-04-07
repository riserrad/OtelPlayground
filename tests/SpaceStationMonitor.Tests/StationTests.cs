using SpaceStationMonitor;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class StationTests
{
    [Fact]
    public void NewStation_AllSubsystemsStartAt100Health()
    {
        var station = new Station();

        foreach (var sub in station.Subsystems)
            Assert.Equal(100.0, sub.Health);
    }

    [Fact]
    public void HullIntegrity_IsAverageOfSubsystemHealth()
    {
        var station = new Station();
        station.Subsystems[0].Health = 80;
        station.Subsystems[1].Health = 60;
        station.Subsystems[2].Health = 40;
        station.Subsystems[3].Health = 20;

        Assert.Equal(50.0, station.HullIntegrity);
    }

    [Fact]
    public void DegradeSubsystem_ReducesHealth()
    {
        var station = new Station();
        station.StartNewCycle();
        var sub = station.Subsystems[0];
        var before = sub.Health;

        // Degrade multiple times to account for random variance
        for (int i = 0; i < 10; i++)
            station.DegradeSubsystem(sub);

        Assert.True(sub.Health < before, "Health should decrease after degradation");
    }

    [Fact]
    public void DegradeSubsystem_HealthNeverBelowZero()
    {
        var station = new Station();
        station.StartNewCycle();
        var sub = station.Subsystems[0];

        for (int i = 0; i < 1000; i++)
            station.DegradeSubsystem(sub);

        Assert.True(sub.Health >= 0, "Health should never go below 0");
    }

    [Fact]
    public void UseEmergencyPower_AddsHealthToAllSubsystems()
    {
        var station = new Station();
        foreach (var sub in station.Subsystems)
            sub.Health = 50;

        var result = station.UseEmergencyPower();

        Assert.True(result);
        foreach (var sub in station.Subsystems)
            Assert.Equal(60, sub.Health);
    }

    [Fact]
    public void UseEmergencyPower_LimitedUses()
    {
        var station = new Station();

        Assert.True(station.UseEmergencyPower());
        Assert.True(station.UseEmergencyPower());
        Assert.True(station.UseEmergencyPower());
        Assert.False(station.UseEmergencyPower()); // 4th attempt fails
    }

    [Fact]
    public void UseEmergencyPower_CapsHealthAt100()
    {
        var station = new Station();
        // All at 100 already
        station.UseEmergencyPower();

        foreach (var sub in station.Subsystems)
            Assert.Equal(100, sub.Health);
    }

    [Fact]
    public void Subsystem_IsCritical_WhenBelow30()
    {
        var sub = new Subsystem("Test", 1.0);
        sub.Health = 29;
        Assert.True(sub.IsCritical);

        sub.Health = 30;
        Assert.False(sub.IsCritical);
    }
}
