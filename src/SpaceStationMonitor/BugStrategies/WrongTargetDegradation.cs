namespace SpaceStationMonitor.BugStrategies;

public class WrongTargetDegradationStrategy : BugStrategyBase
{
    public WrongTargetDegradationStrategy(string bugTargetSubsystem, TimeSpan? activationDelay = null)
        : base(bugTargetSubsystem, activationDelay) { }

    public override string Name => "WrongTargetDegradation";
}
