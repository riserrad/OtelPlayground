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
    private double _difficultyMultiplier = 1.0;

    public Subsystem[] Subsystems { get; } =
    [
        new("Oxygen", 2.0),
        new("Power", 1.5),
        new("Shields", 3.0),
        new("Thermal", 1.8)
    ];

    public int EmergencyPowerRemaining { get; private set; } = 3;
    public int CycleCount { get; private set; }
    public DateTime StartTime { get; } = DateTime.UtcNow;

    public double HullIntegrity =>
        Subsystems.Average(s => Math.Max(0, s.Health));

    public void StartNewCycle()
    {
        CycleCount++;
        _difficultyMultiplier = 1.0 + (CycleCount * 0.02);
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
