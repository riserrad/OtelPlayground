namespace SpaceStationMonitor.BugStrategies;

public class LatencyInjectionStrategy : BugStrategyBase
{
    private const int StepMs = 50;
    private const int MaxDelayMs = 2000;

    private int _invocations;

    public LatencyInjectionStrategy(string bugTargetSubsystem, TimeSpan? activationDelay = null)
        : base(bugTargetSubsystem, activationDelay) { }

    public override string Name => "LatencyInjection";

    public override TimeSpan? RepairDelay(Subsystem sub)
    {
        if (!IsBugActive || sub.Name != BugTargetSubsystem)
            return null;

        _invocations++;
        int delayMs = Math.Min(StepMs * _invocations, MaxDelayMs);
        return TimeSpan.FromMilliseconds(delayMs);
    }
}
