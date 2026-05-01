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
        station.StartNewCycle(isBugActive: false);
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
        station.StartNewCycle(isBugActive: false);
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

    [Fact]
    public void NewStation_StartsWith3FreeSlots()
    {
        var station = new Station();

        Assert.Equal(3, station.ActiveRepairs.AvailableSlots);
        Assert.Equal(0, station.ActiveRepairs.InFlightCount);
    }

    [Fact]
    public void Station_RespectsCustomConcurrentRepairs()
    {
        var station = new Station(concurrentRepairs: 1);

        Assert.Equal(1, station.ActiveRepairs.AvailableSlots);

        var entry = new InFlightRepair(station.Subsystems[0], Requested: 20, CyclesRemaining: 1, RepairAction: null);
        Assert.True(station.ActiveRepairs.TryStart(entry));
        Assert.Equal(0, station.ActiveRepairs.AvailableSlots);
        Assert.Equal(1, station.ActiveRepairs.InFlightCount);

        station.StartNewCycle(isBugActive: false);
        // Slot count is concurrent, not per-cycle: starting a new cycle does not free slots.
        Assert.Equal(0, station.ActiveRepairs.AvailableSlots);
    }

    [Fact]
    public void DegradeSubsystem_RespectsDegradationMultiplier()
    {
        // Mean ratio is ~3.14, not 4. Variance is additive (offset +0.4), so it
        // dilutes the multiplicative effect. Asymmetric window reflects per-run noise.
        var stationLow = new Station(concurrentRepairs: 3, degradationMultiplier: 0.5);
        var stationHigh = new Station(concurrentRepairs: 3, degradationMultiplier: 2.0);
        stationLow.StartNewCycle(isBugActive: false);
        stationHigh.StartNewCycle(isBugActive: false);

        const int n = 20;
        var subLow = stationLow.Subsystems[0];
        var subHigh = stationHigh.Subsystems[0];
        var beforeLow = subLow.Health;
        var beforeHigh = subHigh.Health;

        for (int i = 0; i < n; i++)
        {
            stationLow.DegradeSubsystem(subLow);
            stationHigh.DegradeSubsystem(subHigh);
        }

        var dropLow = beforeLow - subLow.Health;
        var dropHigh = beforeHigh - subHigh.Health;
        Assert.True(dropLow > 0, $"low-mult drop should be positive, got {dropLow}");
        var ratio = dropHigh / dropLow;
        Assert.InRange(ratio, 2.5, 5.0);
    }

    [Fact]
    public void Station_DefaultDegradationMultiplier_PreservesNoArgBehavior()
    {
        var stationNoArg = new Station();
        var stationExplicit = new Station(concurrentRepairs: 3, degradationMultiplier: 1.0);

        Assert.Equal(stationExplicit.ActiveRepairs.AvailableSlots, stationNoArg.ActiveRepairs.AvailableSlots);
        Assert.Equal(stationExplicit.EmergencyPowerRemaining, stationNoArg.EmergencyPowerRemaining);
        Assert.Equal(stationExplicit.HullIntegrity, stationNoArg.HullIntegrity);
        Assert.Equal(stationExplicit.Subsystems.Length, stationNoArg.Subsystems.Length);

        stationNoArg.StartNewCycle(isBugActive: false);
        stationExplicit.StartNewCycle(isBugActive: false);

        const int n = 20;
        var subNoArg = stationNoArg.Subsystems[0];
        var subExplicit = stationExplicit.Subsystems[0];

        for (int i = 0; i < n; i++)
        {
            stationNoArg.DegradeSubsystem(subNoArg);
            stationExplicit.DegradeSubsystem(subExplicit);
        }

        var dropNoArg = 100 - subNoArg.Health;
        var dropExplicit = 100 - subExplicit.Health;
        Assert.True(dropNoArg > 0);
        Assert.True(dropExplicit > 0);
        Assert.True(Math.Abs(dropNoArg - dropExplicit) <= 20,
            $"no-arg ctor and explicit (3, 1.0) ctor should produce comparable degradation modulo variance; got noarg={dropNoArg}, explicit={dropExplicit}");
    }
}
