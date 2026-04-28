namespace SpaceStationMonitor;

public sealed record DifficultyTuning(
    int RepairsPerCycle,
    double DegradationMultiplier,
    double EventChanceMultiplier);

public static class DifficultyTunings
{
    public static DifficultyTuning For(Difficulty d) => d switch
    {
        Difficulty.Tutorial => new DifficultyTuning(4, 0.7, 0.5),
        Difficulty.Normal => new DifficultyTuning(3, 1.0, 1.0),
        Difficulty.Hard => new DifficultyTuning(2, 1.3, 1.3),
        Difficulty.Expert => new DifficultyTuning(1, 1.7, 1.6),
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };
}
