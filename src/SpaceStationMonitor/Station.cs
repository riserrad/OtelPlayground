using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SpaceStationMonitor.Tests")]

namespace SpaceStationMonitor;

public class Subsystem
{
    public string Name { get; }
    public double Health { get; set; }
    public double BaseDegradationRate { get; }
    public double CascadeMultiplier { get; set; } = 1.0;

    public Subsystem(string name, double baseDegradationRate)
    {
        Name = name;
        Health = 100.0;
        BaseDegradationRate = baseDegradationRate;
    }

    public bool IsCritical => Health < 30.0;
    public bool IsDown => Health <= 0.0;
}

public class Station
{
    private readonly Random _random = new();
    private readonly int _repairsPerCycle;
    private double _difficultyMultiplier = 1.0;
    private int _postBugCycles;

    public Station(int repairsPerCycle = 3)
    {
        _repairsPerCycle = repairsPerCycle;
        RepairsRemainingThisCycle = repairsPerCycle;
    }

    public Subsystem[] Subsystems { get; } =
    [
        new("Oxygen", 2.0),
        new("Power", 1.5),
        new("Shields", 3.0),
        new("Thermal", 1.8)
    ];

    public int EmergencyPowerRemaining { get; private set; } = 3;
    public int RepairsRemainingThisCycle { get; private set; }
    public int CycleCount { get; private set; }
    public DateTime StartTime { get; } = DateTime.UtcNow;

    public int RepairsTotalThisSession { get; private set; }
    public Queue<double> RecentRepairEffectiveness { get; } = new();
    public int IronHullStreak { get; private set; }
    public bool SolarFlareThisCycle { get; private set; }
    public bool MinSubsystemStayedAbove30 { get; private set; }
    public int CyclesAfterEmergencyExhausted { get; private set; }

    public int CascadeCount { get; internal set; }

    public int Score => 10 * CycleCount + 5 * RepairsTotalThisSession - 50 * CascadeCount;

    public double HullIntegrity =>
        Subsystems.Average(s => Math.Max(0, s.Health));

    public void StartNewCycle(bool isBugActive)
    {
        CycleCount++;
        RepairsRemainingThisCycle = _repairsPerCycle;
        SolarFlareThisCycle = false;

        if (EmergencyPowerRemaining == 0)
            CyclesAfterEmergencyExhausted++;
        else
            CyclesAfterEmergencyExhausted = 0;

        // Pre-bug: flat 1.0 (fun, winnable baseline).
        // Post-bug: step to 1.5x on activation, then +0.04 per cycle (compounding collapse).
        if (isBugActive)
        {
            _postBugCycles++;
            _difficultyMultiplier = 1.5 + (_postBugCycles * 0.04);
        }
        else
        {
            _difficultyMultiplier = 1.0;
        }
    }

    public void RecordRepair(double effectivenessPercent)
    {
        RepairsTotalThisSession++;
        RecentRepairEffectiveness.Enqueue(effectivenessPercent);
        while (RecentRepairEffectiveness.Count > 20)
            RecentRepairEffectiveness.Dequeue();
    }

    public void RecordSolarFlare() => SolarFlareThisCycle = true;

    public void EndCycle()
    {
        if (HullIntegrity >= 80) IronHullStreak++;
        else IronHullStreak = 0;

        MinSubsystemStayedAbove30 = Subsystems.All(s => s.Health >= 30);
    }

    public bool TryConsumeRepair()
    {
        if (RepairsRemainingThisCycle <= 0) return false;
        RepairsRemainingThisCycle--;
        return true;
    }

    public void DegradeSubsystem(Subsystem subsystem)
    {
        var variance = (_random.NextDouble() - 0.3) * 2.0;
        var degradation = subsystem.BaseDegradationRate
            * subsystem.CascadeMultiplier
            * _difficultyMultiplier
            + variance;
        subsystem.Health = Math.Max(0, subsystem.Health - Math.Max(0, degradation));
    }

    public bool UseEmergencyPower()
    {
        if (EmergencyPowerRemaining <= 0)
            return false;

        EmergencyPowerRemaining--;
        foreach (var sub in Subsystems)
            sub.Health = Math.Min(100, sub.Health + 10);
        return true;
    }
}
