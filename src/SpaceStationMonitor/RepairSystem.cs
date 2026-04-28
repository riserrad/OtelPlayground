using System.Diagnostics;
using SpaceStationMonitor.BugStrategies;

namespace SpaceStationMonitor;

public class RepairSystem
{
    private readonly Random _random = new();
    private readonly IBugStrategy _strategy;

    public RepairSystem(IBugStrategy strategy)
    {
        _strategy = strategy;
    }

    public IBugStrategy Strategy => _strategy;
    public string BugTargetSubsystem => _strategy.BugTargetSubsystem;
    public bool IsBugActive => _strategy.IsBugActive;
    public string BugStrategyName => _strategy.Name;

    // tryConsumeQuota is called before each RETRY (not the original attempt, which
    // is gated upstream by HandleRepair). When it returns false the retry loop
    // bails with repairs.denied++ and the caught exception propagates.
    public RepairResult Repair(Subsystem subsystem, int requested, Func<bool>? tryConsumeQuota = null)
    {
        var delay = _strategy.RepairDelay(subsystem);
        if (delay.HasValue && delay.Value > TimeSpan.Zero)
            Thread.Sleep(delay.Value);

        int retryCount = 0;
        int applied;
        var subsystemTag = new KeyValuePair<string, object?>("subsystem.name", subsystem.Name);

        while (true)
        {
            // Counted per attempt on purpose: retries inflate this counter against
            // the single user-initiated repair below (the RetryStorm counter-ratio bug).
            Telemetry.RepairsTotal.Add(1, subsystemTag);

            try
            {
                applied = _strategy.OnRepair(subsystem, requested, ref retryCount);
                EmitAttemptEvent(retryCount + 1, "success");
                break;
            }
            catch
            {
                EmitAttemptEvent(retryCount + 1, "failure");

                if (!_strategy.ShouldRetryAfterFailure(subsystem, retryCount))
                {
                    Telemetry.RepairsFailed.Add(1, subsystemTag);
                    throw;
                }

                // About to retry: quota, if wired, has to cover this new attempt.
                if (tryConsumeQuota != null && !tryConsumeQuota())
                {
                    EmitAttemptEvent(retryCount + 2, "denied");
                    Telemetry.RepairsDenied.Add(1, subsystemTag);
                    Telemetry.RepairsFailed.Add(1, subsystemTag);
                    throw;
                }

                retryCount++;
            }
        }

        double healthBefore = subsystem.Health;
        subsystem.Health = Math.Min(100, subsystem.Health + applied);
        double healthAfter = subsystem.Health;

        // The display shows what SHOULD have happened (the lie).
        double displayedAfter = Math.Min(100, healthBefore + requested);

        return new RepairResult(
            SubsystemName: subsystem.Name,
            Requested: requested,
            Applied: applied,
            HealthBefore: healthBefore,
            HealthAfter: healthAfter,
            DisplayedAfter: displayedAfter,
            IsHealthy: applied == requested
        );
    }

    public int GetRepairAmount() => _random.Next(15, 26);

    private static void EmitAttemptEvent(int attemptNumber, string outcome)
    {
        Activity.Current?.AddEvent(new ActivityEvent(
            "repair.attempt",
            tags: new ActivityTagsCollection
            {
                { "attempt.number", attemptNumber },
                { "attempt.outcome", outcome },
            }));
    }
}

public record RepairResult(
    string SubsystemName,
    int Requested,
    int Applied,
    double HealthBefore,
    double HealthAfter,
    double DisplayedAfter,
    bool IsHealthy
);
