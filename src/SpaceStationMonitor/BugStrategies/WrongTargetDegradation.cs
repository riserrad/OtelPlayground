namespace SpaceStationMonitor.BugStrategies;

public class WrongTargetDegradationStrategy : BugStrategyBase
{
    public WrongTargetDegradationStrategy(string bugTargetSubsystem, TimeSpan? activationDelay = null)
        : base(bugTargetSubsystem, activationDelay) { }

    public override string Name => "WrongTargetDegradation";

    public override Subsystem RedirectDegradationTarget(Subsystem requested, IReadOnlyList<Subsystem> all)
    {
        if (!IsBugActive) return requested;
        if (requested.Name != BugTargetSubsystem) return requested;

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Name == requested.Name)
                return all[(i + 1) % all.Count];
        }

        return requested;
    }
}
