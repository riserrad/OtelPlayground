using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SpaceStationMonitor.Achievements;

/// <summary>
/// Tracks which achievements have unlocked during the current session and fires each at most once.
/// </summary>
public class AchievementSystem
{
    private readonly HashSet<string> _unlocked = new();
    private readonly IReadOnlyList<Achievement> _achievements;

    public AchievementSystem()
    {
        _achievements = new List<Achievement>
        {
            new("FirstRepair",
                "Complete your first repair.",
                s => s.RepairsTotalThisSession >= 1),

            new("Centurion",
                "Survive 100 cycles.",
                s => s.CycleCount >= 100),

            new("CascadeVeteran",
                "Survive 5 cascade failures.",
                s => s.CascadeCount >= 5),

            new("Surgeon",
                "Average 90% effectiveness across the last 20 repairs.",
                s => s.RecentRepairEffectiveness.Count >= 20
                     && s.RecentRepairEffectiveness.Average() >= 90.0),

            new("EmptyTank",
                "Survive 10 cycles after emergency power runs out.",
                s => s.EmergencyPowerRemaining == 0
                     && s.CyclesAfterEmergencyExhausted >= 10),

            new("IronHull",
                "Hold hull integrity ≥ 80% for 50 consecutive cycles.",
                s => s.IronHullStreak >= 50),

            new("SolarSurvivor",
                "Weather a solar flare without any subsystem dropping below 30%.",
                s => s.SolarFlareThisCycle && s.MinSubsystemStayedAbove30),
        };
    }

    /// <summary>
    /// Names of achievements unlocked during the current session.
    /// </summary>
    public IReadOnlyCollection<string> UnlockedNames => _unlocked;

    /// <summary>
    /// Evaluates each achievement against the given station state. Newly satisfied achievements are
    /// marked unlocked, increment the <c>station.achievements.unlocked</c> counter, attach an
    /// <c>AchievementUnlocked</c> span event to <paramref name="cycleSpan"/> when it is non-null,
    /// push their name onto the display toast, and log at Information. Already-unlocked
    /// achievements are skipped.
    /// </summary>
    /// <param name="station">Source of state for predicate evaluation.</param>
    /// <param name="cycleSpan">Optional active span to receive the unlock event.</param>
    /// <param name="logger">Logger for the unlock notification.</param>
    /// <param name="display">Display surface that renders the achievement toast.</param>
    public void CheckAndFire(Station station, Activity? cycleSpan, ILogger logger, GameDisplay display)
    {
        foreach (var achievement in _achievements)
        {
            if (_unlocked.Contains(achievement.Name)) continue;
            if (!achievement.Predicate(station)) continue;

            _unlocked.Add(achievement.Name);

            var nameTag = new KeyValuePair<string, object?>("achievement.name", achievement.Name);
            Telemetry.AchievementsUnlocked.Add(1, nameTag);

            cycleSpan?.AddEvent(new ActivityEvent("AchievementUnlocked",
                tags: new ActivityTagsCollection { { "achievement.name", achievement.Name } }));

            display.SetAchievement(achievement.Name);

            logger.LogInformation("Achievement unlocked: {Name} — {Description}",
                achievement.Name, achievement.Description);
        }
    }
}
