using SpaceStationMonitor;
using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class LatencyInjectionStrategyTests
{
    private static Subsystem TargetSub() => new("Oxygen", 1.0) { Health = 50 };
    private static Subsystem OtherSub() => new("Power", 1.0) { Health = 50 };

    [Fact]
    public void RepairDelay_WhenBugNotActive_ReturnsNull()
    {
        var strategy = new LatencyInjectionStrategy("Oxygen", TimeSpan.FromHours(1));

        var delay = strategy.RepairDelay(TargetSub());

        Assert.Null(delay);
    }

    [Fact]
    public void RepairDelay_OnNonTargetSubsystem_ReturnsNull()
    {
        var strategy = new LatencyInjectionStrategy("Oxygen", TimeSpan.Zero);

        var delay = strategy.RepairDelay(OtherSub());

        Assert.Null(delay);
    }

    [Fact]
    public void RepairDelay_GrowsAcrossInvocationsOnBugTarget()
    {
        var strategy = new LatencyInjectionStrategy("Oxygen", TimeSpan.Zero);
        var sub = TargetSub();

        var first = strategy.RepairDelay(sub);
        var second = strategy.RepairDelay(sub);
        var fifth = Enumerable.Range(0, 3).Select(_ => strategy.RepairDelay(sub)).Last();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(fifth);
        Assert.True(second > first, $"expected second ({second}) > first ({first})");
        Assert.True(fifth > second, $"expected fifth ({fifth}) > second ({second})");
    }

    [Fact]
    public void RepairDelay_NthCall_ExceedsThreshold()
    {
        // With 50ms step, the 10th invocation on bug target should exceed 400ms.
        var strategy = new LatencyInjectionStrategy("Oxygen", TimeSpan.Zero);
        var sub = TargetSub();

        TimeSpan? delay = null;
        for (int i = 0; i < 10; i++)
            delay = strategy.RepairDelay(sub);

        Assert.NotNull(delay);
        Assert.True(delay.Value.TotalMilliseconds > 400,
            $"expected delay ({delay.Value.TotalMilliseconds}ms) > 400ms after 10 invocations");
    }

    [Fact]
    public void RepairDelay_IsCapped()
    {
        // Cap is 2000ms; after many invocations the delay should plateau at 2s.
        var strategy = new LatencyInjectionStrategy("Oxygen", TimeSpan.Zero);
        var sub = TargetSub();

        TimeSpan? delay = null;
        for (int i = 0; i < 100; i++)
            delay = strategy.RepairDelay(sub);

        Assert.NotNull(delay);
        Assert.True(delay.Value.TotalMilliseconds <= 2000,
            $"expected delay ({delay.Value.TotalMilliseconds}ms) capped at 2000ms");
    }

    [Fact]
    public void NonTargetInvocations_DoNotAdvanceCounterForTarget()
    {
        // Invocations on non-target subsystems must not grow the delay for the target.
        var strategy = new LatencyInjectionStrategy("Oxygen", TimeSpan.Zero);

        for (int i = 0; i < 20; i++)
            Assert.Null(strategy.RepairDelay(OtherSub()));

        var firstTargetDelay = strategy.RepairDelay(TargetSub());

        Assert.NotNull(firstTargetDelay);
        Assert.Equal(50, firstTargetDelay.Value.TotalMilliseconds);
    }

    [Fact]
    public void Repair_EffectivenessIs100Percent_RepairApplied()
    {
        // Effectiveness must remain 100% — the bug only slows repairs, never leaks them.
        var strategy = new LatencyInjectionStrategy("Oxygen", TimeSpan.Zero);
        var repair = new RepairSystem(strategy);
        var sub = TargetSub();

        var result = repair.Repair(sub, 20);

        Assert.Equal(20, result.Applied);
        Assert.Equal(20, result.Requested);
        Assert.True(result.IsHealthy);
    }
}
