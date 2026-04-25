namespace SpaceStationMonitor.BugStrategies;

public class SilentCounterCorruptionStrategy : BugStrategyBase
{
    public SilentCounterCorruptionStrategy(string bugTargetSubsystem, TimeSpan? activationDelay = null)
        : base(bugTargetSubsystem, activationDelay) { }

    public override string Name => "SilentCounterCorruption";
}
