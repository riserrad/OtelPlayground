using SpaceStationMonitor;
using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class RepairSystemTests
{
    private static RepairSystem WithLeakyRepair(string target, TimeSpan? activationDelay = null) =>
        new(new LeakyRepairStrategy(target, activationDelay));

    [Fact]
    public void Repair_WhenBugNotActive_AppliesFullAmount()
    {
        // Bug delay far in future — bug will not be active
        var repair = WithLeakyRepair("Oxygen", TimeSpan.FromHours(1));
        var sub = new Subsystem("Oxygen", 1.0);
        sub.Health = 50;

        var result = repair.Repair(sub, 20);

        Assert.Equal(20, result.Applied);
        Assert.Equal(20, result.Requested);
        Assert.True(result.IsHealthy);
        Assert.Equal(70, sub.Health);
    }

    [Fact]
    public void Repair_WhenBugActive_AppliesReducedAmount()
    {
        // Bug activates immediately
        var repair = WithLeakyRepair("Oxygen", TimeSpan.Zero);
        var sub = new Subsystem("Oxygen", 1.0);
        sub.Health = 50;

        var result = repair.Repair(sub, 20);

        Assert.True(result.Applied < result.Requested,
            $"Expected applied ({result.Applied}) < requested ({result.Requested})");
        Assert.False(result.IsHealthy);
    }

    [Fact]
    public void Repair_WhenBugActive_OnlyAffectsTargetSubsystem()
    {
        var repair = WithLeakyRepair("Oxygen", TimeSpan.Zero);
        var power = new Subsystem("Power", 1.0);
        power.Health = 50;

        var result = repair.Repair(power, 20);

        Assert.Equal(20, result.Applied);
        Assert.True(result.IsHealthy);
    }

    [Fact]
    public void Repair_DisplayedAfter_ShowsExpectedValue()
    {
        var repair = WithLeakyRepair("Oxygen", TimeSpan.Zero);
        var sub = new Subsystem("Oxygen", 1.0);
        sub.Health = 50;

        var result = repair.Repair(sub, 20);

        // Display should show what SHOULD have happened (the lie)
        Assert.Equal(70, result.DisplayedAfter);
        // But actual health is less
        Assert.True(result.HealthAfter < 70);
    }

    [Fact]
    public void Repair_CapsHealthAt100()
    {
        var repair = WithLeakyRepair("Oxygen", TimeSpan.FromHours(1));
        var sub = new Subsystem("Oxygen", 1.0);
        sub.Health = 95;

        var result = repair.Repair(sub, 20);

        Assert.Equal(100, sub.Health);
    }

    [Fact]
    public void GetRepairAmount_ReturnsBetween15And25()
    {
        var repair = WithLeakyRepair("Oxygen");

        for (int i = 0; i < 100; i++)
        {
            var amount = repair.GetRepairAmount();
            Assert.InRange(amount, 15, 25);
        }
    }
}
