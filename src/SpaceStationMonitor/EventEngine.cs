namespace SpaceStationMonitor;

public enum StationEventType
{
    SolarFlare,
    Micrometeorite,
    PowerSurge
}

public enum EventSeverity
{
    Minor,
    Moderate,
    Severe
}

public record StationEvent(
    StationEventType Type,
    EventSeverity Severity,
    string AffectedSubsystem,
    double HealthImpact
);

public class EventEngine
{
    private readonly Random _random = new();
    private readonly double _eventChanceMultiplier;
    private static readonly string[] SubsystemNames = ["Oxygen", "Power", "Shields", "Thermal"];

    public EventEngine(double eventChanceMultiplier = 1.0)
    {
        _eventChanceMultiplier = eventChanceMultiplier;
    }

    public StationEvent? TryGenerateEvent(bool isBugActive)
    {
        // Pre-bug: ~20% per cycle, uniform severity.
        // Post-bug: ~45% per cycle, biased toward Moderate/Severe (20/40/40).
        // Difficulty scales the chance via the multiplier; clamp to [0,1] so Expert can't overshoot.
        double baseChance = isBugActive ? 0.45 : 0.2;
        double eventChance = Math.Clamp(baseChance * _eventChanceMultiplier, 0.0, 1.0);
        if (_random.NextDouble() > eventChance)
            return null;

        var type = (StationEventType)_random.Next(3);
        EventSeverity severity;
        if (isBugActive)
        {
            double roll = _random.NextDouble();
            severity = roll < 0.20 ? EventSeverity.Minor
                     : roll < 0.60 ? EventSeverity.Moderate
                                   : EventSeverity.Severe;
        }
        else
        {
            severity = (EventSeverity)_random.Next(3);
        }

        var targetSubsystem = type switch
        {
            StationEventType.SolarFlare => "Shields",
            StationEventType.PowerSurge => "Power",
            StationEventType.Micrometeorite => SubsystemNames[_random.Next(4)],
            _ => SubsystemNames[_random.Next(4)]
        };

        var impact = severity switch
        {
            EventSeverity.Minor => 5.0 + _random.NextDouble() * 5,
            EventSeverity.Moderate => 10.0 + _random.NextDouble() * 10,
            EventSeverity.Severe => 20.0 + _random.NextDouble() * 15,
            _ => 10.0
        };

        return new StationEvent(type, severity, targetSubsystem, impact);
    }

    public void ApplyEvent(StationEvent stationEvent, Station station)
    {
        var sub = station.Subsystems.FirstOrDefault(s => s.Name == stationEvent.AffectedSubsystem);
        if (sub != null)
        {
            sub.Health = Math.Max(0, sub.Health - stationEvent.HealthImpact);
        }
    }
}
