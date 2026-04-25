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
}
