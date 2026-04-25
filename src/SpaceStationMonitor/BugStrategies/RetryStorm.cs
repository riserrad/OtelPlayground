namespace SpaceStationMonitor.BugStrategies;

public class RetryStormStrategy : BugStrategyBase
{
    private const int MaxRetries = 3;
    private const double ThrowProbability = 0.35;

    private readonly Random _random;

    public RetryStormStrategy(string bugTargetSubsystem, TimeSpan? activationDelay = null, int? seed = null)
        : base(bugTargetSubsystem, activationDelay)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public override string Name => "RetryStorm";

    public override int OnRepair(Subsystem sub, int requested, ref int retryCount)
    {
        if (!IsBugActive || sub.Name != BugTargetSubsystem)
            return requested;

        if (_random.NextDouble() < ThrowProbability)
            throw new GeneralSpaceStationException("RetryStorm: repair attempt failed");

        return requested;
    }

    public override bool ShouldRetryAfterFailure(Subsystem sub, int retryCount)
    {
        return IsBugActive
            && sub.Name == BugTargetSubsystem
            && retryCount < MaxRetries;
    }
}
