namespace SpaceStationMonitor.BugStrategies;

public static class BugStrategyCatalog
{
    public static IBugStrategy[] All(string bugTarget) =>
    [
        new LeakyRepairStrategy(bugTarget),
        new LatencyInjectionStrategy(bugTarget),
        new SilentCounterCorruptionStrategy(bugTarget),
        new StickyCascadeMultiplierStrategy(bugTarget),
        new WrongTargetDegradationStrategy(bugTarget),
        new RetryStormStrategy(bugTarget),
    ];

    public static IBugStrategy? FindByName(IEnumerable<IBugStrategy> strategies, string name)
    {
        foreach (var s in strategies)
        {
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                return s;
        }
        return null;
    }
}
