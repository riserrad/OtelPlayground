using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class AttributeKeyDriftStrategyTests
{
    private const string Target = "Oxygen";

    private static AttributeKeyDriftStrategy Active(int cycle) => new(
        Target, activationDelay: TimeSpan.Zero, cycleProvider: () => cycle);

    private static AttributeKeyDriftStrategy Inactive(int cycle) => new(
        Target, activationDelay: TimeSpan.MaxValue, cycleProvider: () => cycle);

    private static KeyValuePair<string, object?>[] CanonicalSubsystemTags(string name) =>
        new[] { new KeyValuePair<string, object?>("subsystem.name", name) };

    [Fact]
    public void MutateTags_OnOddCycle_RenamesSubsystemName()
    {
        var strategy = Active(cycle: 1);

        var result = strategy
            .MutateTags("SubsystemTick", CanonicalSubsystemTags("Oxygen"))
            .ToArray();

        Assert.Single(result);
        Assert.Equal("subsystem", result[0].Key);
        Assert.Equal("Oxygen", result[0].Value);
    }

    [Fact]
    public void MutateTags_OnEvenCycle_IsPassthrough()
    {
        var strategy = Active(cycle: 2);

        var result = strategy
            .MutateTags("SubsystemTick", CanonicalSubsystemTags("Power"))
            .ToArray();

        Assert.Single(result);
        Assert.Equal("subsystem.name", result[0].Key);
        Assert.Equal("Power", result[0].Value);
    }

    [Fact]
    public void MutateTags_OnUntargetedInstrument_IsPassthrough()
    {
        // Odd cycle would normally drift, but this instrument is not in the targeted set.
        var strategy = Active(cycle: 1);

        var result = strategy
            .MutateTags("station.repairs.total", CanonicalSubsystemTags("Shields"))
            .ToArray();

        Assert.Single(result);
        Assert.Equal("subsystem.name", result[0].Key);
        Assert.Equal("Shields", result[0].Value);
    }

    [Fact]
    public void MutateTags_BeforeActivation_IsPassthrough()
    {
        // Even on a cycle that would drift, IsBugActive=false short-circuits.
        var strategy = Inactive(cycle: 1);

        var result = strategy
            .MutateTags("SubsystemTick", CanonicalSubsystemTags("Thermal"))
            .ToArray();

        Assert.Single(result);
        Assert.Equal("subsystem.name", result[0].Key);
        Assert.Equal("Thermal", result[0].Value);
    }

    [Fact]
    public void ObservedKeys_AfterMixedCycles_TracksBothVariants()
    {
        int cycle = 0;
        var strategy = new AttributeKeyDriftStrategy(
            Target, activationDelay: TimeSpan.Zero, cycleProvider: () => cycle);

        for (cycle = 1; cycle <= 6; cycle++)
        {
            // Drain the enumerable so RecordKey runs on pass-through cycles too.
            foreach (var _ in strategy.MutateTags("SubsystemTick", CanonicalSubsystemTags("Oxygen")))
            {
                // intentionally empty
            }
        }

        var keys = strategy.ObservedKeys;
        Assert.Equal(2, keys.Count);
        Assert.Contains("subsystem.name", keys);
        Assert.Contains("subsystem", keys);
    }

    [Fact]
    public void ObservedKeys_BeforeActivation_StaysEmpty()
    {
        int cycle = 0;
        var strategy = new AttributeKeyDriftStrategy(
            Target, activationDelay: TimeSpan.MaxValue, cycleProvider: () => cycle);

        for (cycle = 1; cycle <= 6; cycle++)
        {
            foreach (var _ in strategy.MutateTags("SubsystemTick", CanonicalSubsystemTags("Oxygen")))
            {
                // intentionally empty
            }
        }

        Assert.Empty(strategy.ObservedKeys);
    }

    [Fact]
    public void ObservedKeys_OnUntargetedInstrument_NotPopulated()
    {
        int cycle = 0;
        var strategy = new AttributeKeyDriftStrategy(
            Target, activationDelay: TimeSpan.Zero, cycleProvider: () => cycle);

        for (cycle = 1; cycle <= 6; cycle++)
        {
            foreach (var _ in strategy.MutateTags("station.repairs.total", CanonicalSubsystemTags("Oxygen")))
            {
                // intentionally empty
            }
        }

        Assert.Empty(strategy.ObservedKeys);
    }
}
