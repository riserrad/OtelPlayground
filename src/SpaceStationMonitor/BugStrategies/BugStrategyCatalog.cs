namespace SpaceStationMonitor.BugStrategies;

public static class BugStrategyCatalog
{
    // activationDelay = null lets each strategy use its BugStrategyBase default
    // (currently 2 minutes). A non-null value overrides every strategy in the run
    // uniformly — used by the BUG_ACTIVATION_DELAY_MS env-var path so QA / CI can
    // exercise post-bug behavior without sitting through the production-tuned wait.
    public static IBugStrategy[] All(
        string bugTarget,
        TimeSpan? activationDelay = null,
        Func<int>? cycleProvider = null) =>
    [
        new LeakyRepairStrategy(bugTarget, activationDelay),
        new LatencyInjectionStrategy(bugTarget, activationDelay),
        new SilentCounterCorruptionStrategy(bugTarget, activationDelay),
        new StickyCascadeMultiplierStrategy(bugTarget, activationDelay),
        new WrongTargetDegradationStrategy(bugTarget, activationDelay),
        new RetryStormStrategy(bugTarget, activationDelay),
        new SamplingBlindSpotStrategy(bugTarget, activationDelay),
        new OrphanSpanStrategy(bugTarget, activationDelay, cycleProvider),
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
