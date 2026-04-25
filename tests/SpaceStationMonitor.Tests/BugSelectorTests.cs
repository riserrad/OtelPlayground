using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class BugSelectorTests
{
    private static readonly string[] SubsystemNames = ["Oxygen", "Power", "Shields", "Thermal"];

    private static Func<string, string?> EnvFrom(params (string key, string? value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.key, p => p.value);
        return key => dict.TryGetValue(key, out var v) ? v : null;
    }

    [Fact]
    public void SameSeed_ProducesSameTargetAndStrategy()
    {
        var env = EnvFrom(("BUG_STRATEGY_SEED", "42"));

        var first = BugSelector.Select(env, SubsystemNames);
        var second = BugSelector.Select(env, SubsystemNames);

        Assert.Equal(first.target, second.target);
        Assert.Equal(first.strategy.Name, second.strategy.Name);
    }

    [Fact]
    public void DifferentSeeds_CanProduceDifferentPicks()
    {
        // Not a guarantee that seeds 1 and 2 differ, but across these four
        // seeds at least two of the resulting (target, strategy) pairs must
        // differ — otherwise the seeding doesn't actually change anything.
        var picks = new[] { "1", "2", "3", "4" }
            .Select(s => BugSelector.Select(EnvFrom(("BUG_STRATEGY_SEED", s)), SubsystemNames))
            .Select(p => (p.target, p.strategy.Name))
            .ToHashSet();

        Assert.True(picks.Count > 1, "expected varied picks across different seeds");
    }

    [Fact]
    public void ExplicitBugStrategy_OverridesSeededPick()
    {
        // Even with a seed that would randomly pick something else, BUG_STRATEGY
        // wins. The seed still controls the target (the strategy can only be
        // instantiated against a target the catalog knows about).
        var env = EnvFrom(
            ("BUG_STRATEGY_SEED", "42"),
            ("BUG_STRATEGY", "LeakyRepair"));

        var (_, strategy) = BugSelector.Select(env, SubsystemNames);

        Assert.Equal("LeakyRepair", strategy.Name);
        Assert.IsType<LeakyRepairStrategy>(strategy);
    }

    [Fact]
    public void ExplicitBugStrategy_IsCaseInsensitive()
    {
        var env = EnvFrom(("BUG_STRATEGY", "retrystorm"));

        var (_, strategy) = BugSelector.Select(env, SubsystemNames);

        Assert.Equal("RetryStorm", strategy.Name);
    }

    [Fact]
    public void UnknownBugStrategy_ThrowsInvalidOperationWithValidNames()
    {
        var env = EnvFrom(("BUG_STRATEGY", "garbage"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            BugSelector.Select(env, SubsystemNames));

        Assert.Contains("garbage", ex.Message);
        // At least one real strategy name should appear in the list.
        Assert.Contains("LeakyRepair", ex.Message);
    }

    [Fact]
    public void NoEnv_PicksValidTargetAndStrategy()
    {
        var env = EnvFrom();

        var (target, strategy) = BugSelector.Select(env, SubsystemNames);

        Assert.Contains(target, SubsystemNames);
        Assert.NotNull(strategy);
        Assert.Equal(target, strategy.BugTargetSubsystem);
    }
}
