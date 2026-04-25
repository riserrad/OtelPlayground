namespace SpaceStationMonitor.BugStrategies;

public class SilentCounterCorruptionStrategy : BugStrategyBase
{
    private const double CorruptionRate = 0.15;

    private readonly Random _random;

    public SilentCounterCorruptionStrategy(string bugTargetSubsystem, TimeSpan? activationDelay = null, int? seed = null)
        : base(bugTargetSubsystem, activationDelay)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public override string Name => "SilentCounterCorruption";

    public override int CycleCounterIncrement()
    {
        if (!IsBugActive) return 1;
        return _random.NextDouble() < CorruptionRate ? 2 : 1;
    }
}
