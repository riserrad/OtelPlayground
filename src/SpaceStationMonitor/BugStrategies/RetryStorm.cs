namespace SpaceStationMonitor.BugStrategies;

public class RetryStormStrategy : BugStrategyBase
{
    public RetryStormStrategy(string bugTargetSubsystem, TimeSpan? activationDelay = null)
        : base(bugTargetSubsystem, activationDelay) { }

    public override string Name => "RetryStorm";
}
