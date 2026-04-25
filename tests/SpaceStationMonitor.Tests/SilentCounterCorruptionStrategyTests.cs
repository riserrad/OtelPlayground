using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class SilentCounterCorruptionStrategyTests
{
    [Fact]
    public void CycleCounterIncrement_WhenBugNotActive_AlwaysReturnsOne()
    {
        var strategy = new SilentCounterCorruptionStrategy("Oxygen", TimeSpan.FromHours(1), seed: 42);

        for (int i = 0; i < 200; i++)
            Assert.Equal(1, strategy.CycleCounterIncrement());
    }

    [Fact]
    public void CycleCounterIncrement_WhenBugActive_ProducesTwoAtLeastOnce()
    {
        // Seeded so the test is deterministic; with rate=0.15 over 200 invocations the
        // probability of zero twos is ~2e-14, so this is a structural guarantee.
        var strategy = new SilentCounterCorruptionStrategy("Oxygen", TimeSpan.Zero, seed: 42);

        var values = Enumerable.Range(0, 200)
            .Select(_ => strategy.CycleCounterIncrement())
            .ToArray();

        Assert.Contains(2, values);
        Assert.All(values, v => Assert.InRange(v, 1, 2));
    }

    [Fact]
    public void CycleCounterIncrement_WhenBugActive_CorruptionRateRoughly10To25Percent()
    {
        // Seeded so the test is deterministic. Target rate is 15%, assertion allows 10–25%.
        var strategy = new SilentCounterCorruptionStrategy("Oxygen", TimeSpan.Zero, seed: 42);

        int corrupted = 0;
        for (int i = 0; i < 200; i++)
        {
            if (strategy.CycleCounterIncrement() == 2) corrupted++;
        }

        double rate = corrupted / 200.0;
        Assert.InRange(rate, 0.10, 0.25);
    }

    [Fact]
    public void CycleCounterIncrement_SameSeed_ProducesSameSequence()
    {
        var a = new SilentCounterCorruptionStrategy("Oxygen", TimeSpan.Zero, seed: 12345);
        var b = new SilentCounterCorruptionStrategy("Oxygen", TimeSpan.Zero, seed: 12345);

        var seqA = Enumerable.Range(0, 100).Select(_ => a.CycleCounterIncrement()).ToArray();
        var seqB = Enumerable.Range(0, 100).Select(_ => b.CycleCounterIncrement()).ToArray();

        Assert.Equal(seqA, seqB);
    }
}
