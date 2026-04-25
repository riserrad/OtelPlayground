namespace SpaceStationMonitor.BugStrategies;

public class RetryStormStrategy : BugStrategyBase
{
    private const int MaxRetries = 3;

    public RetryStormStrategy(string bugTargetSubsystem, TimeSpan? activationDelay = null)
        : base(bugTargetSubsystem, activationDelay) { }

    public override string Name => "RetryStorm";

    public override bool ShouldRetryAfterFailure(Subsystem sub, int retryCount)
    {
        return IsBugActive
            && sub.Name == BugTargetSubsystem
            && retryCount < MaxRetries;
    }
}
