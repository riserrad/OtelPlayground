using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SpaceStationMonitor;

public static class Telemetry
{
    public const string ServiceName = "SpaceStationMonitor";
    public const string ActivitySourceName = "SpaceStationMonitor";
    public const string MeterName = "SpaceStationMonitor";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    // Counters
    public static readonly Counter<long> RepairsTotal =
        Meter.CreateCounter<long>("station.repairs.total", description: "Total repair attempts");

    public static readonly Counter<long> RepairsFailed =
        Meter.CreateCounter<long>("station.repairs.failed", description: "Hard-zero repair failures");

    public static readonly Counter<long> RepairsDenied =
        Meter.CreateCounter<long>("station.repairs.denied",
            description: "Repair attempts blocked by no free repair slot, repair already in flight on subsystem, or retry-quota exhaustion");

    public static readonly Counter<long> CascadeFailures =
        Meter.CreateCounter<long>("station.cascade.failures", description: "Cascade failure events");

    public static readonly Counter<long> EventsTotal =
        Meter.CreateCounter<long>("station.events.total", description: "Random station events");

    public static readonly Counter<long> CyclesTotal =
        Meter.CreateCounter<long>("station.cycles.total", description: "Station cycles completed");

    // Histogram
    public static readonly Histogram<double> RepairEffectiveness =
        Meter.CreateHistogram<double>("station.repair.effectiveness", "percent",
            description: "Ratio of repair applied vs requested (100 = healthy)");

    // UpDownCounter
    public static readonly UpDownCounter<long> AchievementsUnlocked =
        Meter.CreateUpDownCounter<long>("station.achievements.unlocked",
            description: "Net achievements unlocked this session");

    // Note: ObservableGauges (station.subsystem.health, station.hull.integrity)
    // are registered in Program.cs because they need a reference to the Station instance.
}
