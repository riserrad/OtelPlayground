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
}
