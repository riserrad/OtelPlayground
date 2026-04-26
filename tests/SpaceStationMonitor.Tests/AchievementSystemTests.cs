using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceStationMonitor.Achievements;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class AchievementSystemTests
{
    private static readonly ILogger Logger = NullLogger.Instance;

    private static (AchievementSystem sys, Station station, GameDisplay display) NewSystem()
        => (new AchievementSystem(), new Station(), new GameDisplay());

    [Fact]
    public void FirstRepair_FiresAfterFirstRepair()
    {
        var (sys, station, display) = NewSystem();
        station.RecordRepair(50.0);

        sys.CheckAndFire(station, null, Logger, display);

        Assert.Contains("FirstRepair", sys.UnlockedNames);
    }

    [Fact]
    public void Centurion_FiresAt100Cycles()
    {
        var (sys, station, display) = NewSystem();
        for (int i = 0; i < 100; i++) station.StartNewCycle(isBugActive: false);

        sys.CheckAndFire(station, null, Logger, display);

        Assert.Contains("Centurion", sys.UnlockedNames);
    }

    [Fact]
    public void Centurion_DoesNotFireBelow100()
    {
        var (sys, station, display) = NewSystem();
        for (int i = 0; i < 99; i++) station.StartNewCycle(isBugActive: false);

        sys.CheckAndFire(station, null, Logger, display);

        Assert.DoesNotContain("Centurion", sys.UnlockedNames);
    }

    [Fact]
    public void CascadeVeteran_FiresAt5Cascades()
    {
        var (sys, station, display) = NewSystem();
        station.CascadeCount = 5;

        sys.CheckAndFire(station, null, Logger, display);

        Assert.Contains("CascadeVeteran", sys.UnlockedNames);
    }

    [Fact]
    public void Surgeon_FiresWhenLast20Average90()
    {
        var (sys, station, display) = NewSystem();
        for (int i = 0; i < 20; i++) station.RecordRepair(90.0);

        sys.CheckAndFire(station, null, Logger, display);

        Assert.Contains("Surgeon", sys.UnlockedNames);
    }

    [Fact]
    public void Surgeon_DoesNotFireBelow90Mean()
    {
        var (sys, station, display) = NewSystem();
        for (int i = 0; i < 20; i++) station.RecordRepair(80.0);

        sys.CheckAndFire(station, null, Logger, display);

        Assert.DoesNotContain("Surgeon", sys.UnlockedNames);
    }

    [Fact]
    public void Surgeon_DoesNotFireWithFewerThan20Samples()
    {
        var (sys, station, display) = NewSystem();
        for (int i = 0; i < 19; i++) station.RecordRepair(95.0);

        sys.CheckAndFire(station, null, Logger, display);

        Assert.DoesNotContain("Surgeon", sys.UnlockedNames);
    }

    [Fact]
    public void EmptyTank_FiresAfter10CyclesWithExhaustedEmergencyPower()
    {
        var (sys, station, display) = NewSystem();
        Assert.True(station.UseEmergencyPower());
        Assert.True(station.UseEmergencyPower());
        Assert.True(station.UseEmergencyPower());
        Assert.Equal(0, station.EmergencyPowerRemaining);

        for (int i = 0; i < 10; i++) station.StartNewCycle(isBugActive: false);

        sys.CheckAndFire(station, null, Logger, display);

        Assert.Contains("EmptyTank", sys.UnlockedNames);
    }

    [Fact]
    public void EmptyTank_DoesNotFireWhenEmergencyPowerStillAvailable()
    {
        var (sys, station, display) = NewSystem();
        for (int i = 0; i < 50; i++) station.StartNewCycle(isBugActive: false);

        sys.CheckAndFire(station, null, Logger, display);

        Assert.DoesNotContain("EmptyTank", sys.UnlockedNames);
    }

    [Fact]
    public void IronHull_FiresAfter50ConsecutiveHealthyCycles()
    {
        var (sys, station, display) = NewSystem();
        for (int i = 0; i < 50; i++)
        {
            station.StartNewCycle(isBugActive: false);
            station.EndCycle();
        }

        sys.CheckAndFire(station, null, Logger, display);

        Assert.Contains("IronHull", sys.UnlockedNames);
    }

    [Fact]
    public void IronHull_StreakResetsWhenHullDropsBelow80()
    {
        var (_, station, _) = NewSystem();
        for (int i = 0; i < 30; i++)
        {
            station.StartNewCycle(isBugActive: false);
            station.EndCycle();
        }
        Assert.Equal(30, station.IronHullStreak);

        // Tank one subsystem so hull = 75 (below 80).
        station.Subsystems[0].Health = 0;
        station.StartNewCycle(isBugActive: false);
        station.EndCycle();

        Assert.Equal(0, station.IronHullStreak);
    }

    [Fact]
    public void SolarSurvivor_FiresWhenSolarFlareSurvivedWithoutCriticalDip()
    {
        var (sys, station, display) = NewSystem();
        station.StartNewCycle(isBugActive: false);
        station.RecordSolarFlare();
        station.EndCycle();

        sys.CheckAndFire(station, null, Logger, display);

        Assert.Contains("SolarSurvivor", sys.UnlockedNames);
    }

    [Fact]
    public void SolarSurvivor_DoesNotFireWhenSubsystemDroppedBelow30()
    {
        var (sys, station, display) = NewSystem();
        station.StartNewCycle(isBugActive: false);
        station.RecordSolarFlare();
        station.Subsystems[0].Health = 25;
        station.EndCycle();

        sys.CheckAndFire(station, null, Logger, display);

        Assert.DoesNotContain("SolarSurvivor", sys.UnlockedNames);
    }

    [Fact]
    public void Achievement_FiresOnlyOncePerSession()
    {
        var (sys, station, display) = NewSystem();
        station.RecordRepair(50.0);

        sys.CheckAndFire(station, null, Logger, display);
        var firstCount = sys.UnlockedNames.Count;

        sys.CheckAndFire(station, null, Logger, display);

        Assert.Equal(firstCount, sys.UnlockedNames.Count);
    }

    [Fact]
    public void NoAchievementsFire_OnFreshStation()
    {
        var (sys, station, display) = NewSystem();

        sys.CheckAndFire(station, null, Logger, display);

        Assert.Empty(sys.UnlockedNames);
    }
}
