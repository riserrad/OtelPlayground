using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class BugStrategyCatalogTests
{
    [Theory]
    [InlineData("leakyrepair")]
    [InlineData("LEAKYREPAIR")]
    [InlineData("LeakyRepair")]
    [InlineData("lEaKyRePaIr")]
    public void FindByName_IsCaseInsensitive(string query)
    {
        var strategies = BugStrategyCatalog.All("Oxygen");

        var found = BugStrategyCatalog.FindByName(strategies, query);

        Assert.NotNull(found);
        Assert.IsType<LeakyRepairStrategy>(found);
    }

    [Fact]
    public void FindByName_ReturnsNullForUnknown()
    {
        var strategies = BugStrategyCatalog.All("Oxygen");

        Assert.Null(BugStrategyCatalog.FindByName(strategies, "NoSuchStrategy"));
    }

    [Fact]
    public void FindByName_FindsEachStrategyByExactName()
    {
        var strategies = BugStrategyCatalog.All("Oxygen");

        foreach (var s in strategies)
        {
            var found = BugStrategyCatalog.FindByName(strategies, s.Name);
            Assert.Same(s, found);
        }
    }

    [Fact]
    public void All_WithDelayOverride_PassesOverrideToAllStrategies()
    {
        // 100ms delay + 200ms wait crosses the activation threshold for every strategy.
        var strategies = BugStrategyCatalog.All("Oxygen", TimeSpan.FromMilliseconds(100));

        Thread.Sleep(200);

        foreach (var s in strategies)
        {
            Assert.True(s.IsBugActive,
                $"strategy '{s.Name}' should be active 200ms after construction with 100ms override");
        }
    }

    [Fact]
    public void All_WithNullDelay_UsesDefault()
    {
        // Null override falls through to BugStrategyBase's 2-minute production default;
        // none of the strategies should report active immediately after construction.
        var strategies = BugStrategyCatalog.All("Oxygen", activationDelay: null);

        foreach (var s in strategies)
        {
            Assert.False(s.IsBugActive,
                $"strategy '{s.Name}' should not be active immediately when delay is left at the default");
        }
    }
}
