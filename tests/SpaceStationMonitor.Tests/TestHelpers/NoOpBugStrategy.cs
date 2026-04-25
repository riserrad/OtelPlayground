using SpaceStationMonitor.BugStrategies;

namespace SpaceStationMonitor.Tests.TestHelpers;

internal sealed class NoOpBugStrategy : BugStrategyBase
{
    public NoOpBugStrategy(string bugTargetSubsystem = "Oxygen")
        : base(bugTargetSubsystem, TimeSpan.FromDays(365)) { }

    public override string Name => "NoOp";
    public override bool IsBugActive => false;
}
