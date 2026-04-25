namespace SpaceStationMonitor.BugStrategies;

public class StickyCascadeMultiplierStrategy : BugStrategyBase
{
    public StickyCascadeMultiplierStrategy(string bugTargetSubsystem, TimeSpan? activationDelay = null)
        : base(bugTargetSubsystem, activationDelay) { }

    public override string Name => "StickyCascadeMultiplier";
}
