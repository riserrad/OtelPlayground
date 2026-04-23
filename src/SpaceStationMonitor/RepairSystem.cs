namespace SpaceStationMonitor;

public class RepairSystem
{
    private readonly Random _random = new();
    private readonly DateTime _startTime = DateTime.UtcNow;
    private readonly TimeSpan _bugActivationDelay;
    private readonly string _bugTargetSubsystem;
    private int _leakyRepairCount;

    public RepairSystem(string bugTargetSubsystem, TimeSpan? bugActivationDelay = null)
    {
        _bugActivationDelay = bugActivationDelay ?? TimeSpan.FromMinutes(2);
        _bugTargetSubsystem = bugTargetSubsystem;
    }

    public string BugTargetSubsystem => _bugTargetSubsystem;
    public bool IsBugActive => DateTime.UtcNow - _startTime > _bugActivationDelay;

    public RepairResult Repair(Subsystem subsystem, int requested)
    {
        // ┌─────────────────────────────────────────────────────────────────┐
        // │ BUG: Repair leak — to fix, just uncomment the FIX line below.  │
        // │ The fix overrides the buggy value. One line change!            │
        // └─────────────────────────────────────────────────────────────────┘
        int applied;
        if (IsBugActive && subsystem.Name == _bugTargetSubsystem)
        {
            applied = CalculateLeakyRepair(requested);
        }
        else
        {
            applied = requested;
        }

        // ┌─────────────────────────────────────────────────────────────────┐
        // │ FIX: Uncomment the line below to override the buggy value.     │
        // └─────────────────────────────────────────────────────────────────┘
        // applied = requested;

        double healthBefore = subsystem.Health;
        subsystem.Health = Math.Min(100, subsystem.Health + applied);
        double healthAfter = subsystem.Health;

        // The display shows what SHOULD have happened (the lie)
        double displayedAfter = Math.Min(100, healthBefore + requested);

        return new RepairResult(
            SubsystemName: subsystem.Name,
            Requested: requested,
            Applied: applied,
            HealthBefore: healthBefore,
            HealthAfter: healthAfter,
            DisplayedAfter: displayedAfter,
            IsHealthy: applied == requested
        );
    }

    private int CalculateLeakyRepair(int requested)
    {
        _leakyRepairCount++;

        // 10% chance of hard zero, but only after 2+ leaky repairs
        if (_leakyRepairCount > 2 && _random.NextDouble() < 0.1)
        {
            // This simulates a critical failure in the repair system, causing no repair to be applied.
            throw new GeneralSpaceStationException("Repair system failure: zero repair applied!");
        }

        // Leaky: apply only 15-22% of requested
        double leakFactor = 0.15 + (_random.NextDouble() * 0.07);
        return (int)(requested * leakFactor);
    }

    public int GetRepairAmount() => _random.Next(15, 26);
}

public record RepairResult(
    string SubsystemName,
    int Requested,
    int Applied,
    double HealthBefore,
    double HealthAfter,
    double DisplayedAfter,
    bool IsHealthy
);
