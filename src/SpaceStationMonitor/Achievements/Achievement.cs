namespace SpaceStationMonitor.Achievements;

public sealed record Achievement(string Name, string Description, Func<Station, bool> Predicate);
