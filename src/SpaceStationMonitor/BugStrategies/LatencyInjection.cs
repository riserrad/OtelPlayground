namespace SpaceStationMonitor.BugStrategies;

public class LatencyInjectionStrategy : BugStrategyBase
{
    public LatencyInjectionStrategy(string bugTargetSubsystem, TimeSpan? activationDelay = null)
        : base(bugTargetSubsystem, activationDelay) { }

    public override string Name => "LatencyInjection";
}
