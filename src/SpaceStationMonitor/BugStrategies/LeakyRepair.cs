namespace SpaceStationMonitor.BugStrategies;

public class LeakyRepairStrategy : BugStrategyBase
{
    private readonly Random _random = new();
    private int _leakyRepairCount;

    public LeakyRepairStrategy(string bugTargetSubsystem, TimeSpan? activationDelay = null)
        : base(bugTargetSubsystem, activationDelay) { }

    public override string Name => "LeakyRepair";

    public override int OnRepair(Subsystem sub, int requested, ref int retryCount)
    {
        if (!IsBugActive || sub.Name != BugTargetSubsystem)
            return requested;

        _leakyRepairCount++;

        // 10% chance of hard zero, but only after 2+ leaky repairs.
        if (_leakyRepairCount > 2 && _random.NextDouble() < 0.1)
            throw new GeneralSpaceStationException("Repair system failure: zero repair applied!");

        // Leaky: apply only 15-22% of requested.
        double leakFactor = 0.15 + (_random.NextDouble() * 0.07);
        return (int)(requested * leakFactor);
    }
}
