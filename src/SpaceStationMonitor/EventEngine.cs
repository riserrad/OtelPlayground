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
    private static readonly string[] SubsystemNames = ["Oxygen", "Power", "Shields", "Thermal"];

    public StationEvent? TryGenerateEvent()
    {
        // ~20% chance per cycle
        if (_random.NextDouble() > 0.2)
            return null;

        var type = (StationEventType)_random.Next(3);
        var severity = (EventSeverity)_random.Next(3);

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
