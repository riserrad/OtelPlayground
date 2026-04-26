using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SpaceStationMonitor.Achievements;

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

    public IReadOnlyCollection<string> UnlockedNames => _unlocked;

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
