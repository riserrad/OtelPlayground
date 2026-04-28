namespace SpaceStationMonitor.BugStrategies;

/// <summary>
/// Pass-through bug strategy used by Just Playing mode. The bug never activates; all hooks
/// fall through to <see cref="BugStrategyBase"/> defaults so gameplay matches the no-bug baseline.
/// Not registered in <see cref="BugStrategyCatalog"/>; random picks must never land here.
/// </summary>
public sealed class NoOpBugStrategy : BugStrategyBase
{
    public NoOpBugStrategy(string bugTargetSubsystem)
        : base(bugTargetSubsystem, activationDelay: TimeSpan.MaxValue)
    {
    }

    public override string Name => "NoOp";

    public override bool IsBugActive => false;
}
