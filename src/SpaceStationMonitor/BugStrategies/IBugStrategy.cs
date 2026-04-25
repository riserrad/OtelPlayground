namespace SpaceStationMonitor.BugStrategies;

public interface IBugStrategy
{
    string Name { get; }
    string BugTargetSubsystem { get; }
    bool IsBugActive { get; }

    int OnRepair(Subsystem sub, int requested, ref int retryCount);
    bool ShouldRetryAfterFailure(Subsystem sub, int retryCount);
    int CycleCounterIncrement();
    bool ShouldResetCascadeMultipliers();
    Subsystem RedirectDegradationTarget(Subsystem requested, IReadOnlyList<Subsystem> all);
    TimeSpan? RepairDelay(Subsystem sub);
}

public abstract class BugStrategyBase : IBugStrategy
{
    private readonly DateTime _startTime = DateTime.UtcNow;
    private readonly TimeSpan _activationDelay;

    protected BugStrategyBase(string bugTargetSubsystem, TimeSpan? activationDelay = null)
    {
        BugTargetSubsystem = bugTargetSubsystem;
        _activationDelay = activationDelay ?? TimeSpan.FromMinutes(2);
    }

    public abstract string Name { get; }
    public string BugTargetSubsystem { get; }
    public virtual bool IsBugActive => DateTime.UtcNow - _startTime > _activationDelay;

    public virtual int OnRepair(Subsystem sub, int requested, ref int retryCount) => requested;
    public virtual bool ShouldRetryAfterFailure(Subsystem sub, int retryCount) => false;
    public virtual int CycleCounterIncrement() => 1;
    public virtual bool ShouldResetCascadeMultipliers() => true;
    public virtual Subsystem RedirectDegradationTarget(Subsystem requested, IReadOnlyList<Subsystem> all) => requested;
    public virtual TimeSpan? RepairDelay(Subsystem sub) => null;
}
