namespace SpaceStationMonitor;

public record CascadeResult(
    string SourceSubsystem,
    string[] AffectedSubsystems,
    bool Triggered
);

public class CascadeEngine
{
    public List<CascadeResult> CheckAndApplyCascades(Station station)
    {
        var results = new List<CascadeResult>();

        // Reset all cascade multipliers
        foreach (var sub in station.Subsystems)
            sub.CascadeMultiplier = 1.0;

        foreach (var sub in station.Subsystems)
        {
            if (!sub.IsCritical)
                continue;

            var affected = station.Subsystems
                .Where(s => s.Name != sub.Name)
                .ToArray();

            foreach (var target in affected)
                target.CascadeMultiplier += 0.5;

            results.Add(new CascadeResult(
                SourceSubsystem: sub.Name,
                AffectedSubsystems: affected.Select(a => a.Name).ToArray(),
                Triggered: true
            ));
        }

        return results;
    }
}
