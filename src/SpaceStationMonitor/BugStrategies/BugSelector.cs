namespace SpaceStationMonitor.BugStrategies;

public static class BugSelector
{
    // env is a pluggable lookup (Environment.GetEnvironmentVariable in prod,
    // a dict-backed function in tests) so callers don't have to mutate
    // process-wide state to exercise this logic.
    //
    // BUG_STRATEGY_SEED drives the RNG up-front, so both the subsystem target
    // pick AND the strategy pick are deterministic when the seed is set.
    // BUG_STRATEGY overrides the random strategy pick (the target is still
    // seed-driven).
    public static (string target, IBugStrategy strategy) Select(
        Func<string, string?> env,
        string[] subsystemNames,
        TimeSpan? activationDelay = null)
    {
        var seedEnv = env("BUG_STRATEGY_SEED");
        var rng = int.TryParse(seedEnv, out var seed) ? new Random(seed) : new Random();

        var target = subsystemNames[rng.Next(subsystemNames.Length)];
        var strategies = BugStrategyCatalog.All(target, activationDelay);

        var forcedName = env("BUG_STRATEGY");
        if (!string.IsNullOrWhiteSpace(forcedName))
        {
            var forced = BugStrategyCatalog.FindByName(strategies, forcedName)
                ?? throw new InvalidOperationException(
                    $"Unknown BUG_STRATEGY '{forcedName}'. Valid names: "
                    + string.Join(", ", strategies.Select(s => s.Name)));
            return (target, forced);
        }

        return (target, strategies[rng.Next(strategies.Length)]);
    }
}
