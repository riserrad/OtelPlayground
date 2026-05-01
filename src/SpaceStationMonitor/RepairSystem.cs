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

    // BeginRepair starts a RepairAction Activity at slot-claim time and returns the
    // in-flight entry. The Activity stays open across cycle boundaries; CompleteRepair
    // (or the cancellation / shutdown path) is responsible for stopping it. RepairAction
    // is a ROOT span (parentContext: default) and Activity.Current is restored after the
    // Start so the in-flight Activity does not pollute the ambient context — otherwise
    // the next StationCycle would inherit RepairAction as its parent, inverting the
    // researcher AC-16 invariant that StationCycle is the cycle root and RepairAction
    // is linked-from-StationCycle, never parented-to-StationCycle.
    public InFlightRepair BeginRepair(Subsystem subsystem, int requested)
    {
        int cyclesRequired = Math.Clamp((int)Math.Ceiling((100 - subsystem.Health) / 33.0), 1, 3);

        // StartActivity inherits Activity.Current as parent even when an explicit
        // parentContext: default is supplied; null'ing Current first is the only
        // reliable way to force the new Activity to be a true root span. The
        // restore lives in finally so a throw from StartActivity or any SetTag
        // can't leak the null Current into the caller's ambient context.
        var previousCurrent = Activity.Current;
        Activity.Current = null;
        Activity? activity;
        try
        {
            activity = Telemetry.ActivitySource.StartActivity(
                "RepairAction",
                ActivityKind.Internal);
            activity?.SetTag("subsystem.name", subsystem.Name);
            activity?.SetTag("repair.requested", requested);
            activity?.SetTag("repair.cycles_required", cyclesRequired);
        }
        finally
        {
            Activity.Current = previousCurrent;
        }

        return new InFlightRepair(subsystem, requested, cyclesRequired, activity);
    }

    public RepairResult CompleteRepair(InFlightRepair entry)
    {
        var activity = entry.RepairAction;
        try
        {
            var result = Repair(entry.Subsystem, entry.Requested, tryConsumeQuota: null);
            activity?.SetTag("repair.applied", result.Applied);
            activity?.SetTag("repair.healthy", result.IsHealthy);
            if (!result.IsHealthy)
            {
                activity?.AddEvent(new ActivityEvent("RepairLeak",
                    tags: new ActivityTagsCollection
                    {
                        { "repair.delta", result.Requested - result.Applied }
                    }));
            }
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.AddEvent(new ActivityEvent("RepairFailed"));
            // Counter rides with the RepairFailed event so unexpected exceptions
            // (NRE, OOM, lifecycle) don't drop silently. Repair's inner catch already
            // increments on its own !ShouldRetryAfterFailure / quota-denied paths;
            // re-throws from there land here too, producing 2x on common-bail paths.
            // failure.layer="outer" tags this site so dashboards can dedupe via
            // `RepairsFailed{failure.layer != "outer"}` for unique-attempt counts.
            Telemetry.RepairsFailed.Add(1,
                new KeyValuePair<string, object?>("subsystem.name", entry.Subsystem.Name),
                new KeyValuePair<string, object?>("failure.layer", "outer"));
            return new RepairResult(
                SubsystemName: entry.Subsystem.Name,
                Requested: entry.Requested,
                Applied: 0,
                HealthBefore: entry.Subsystem.Health,
                HealthAfter: entry.Subsystem.Health,
                DisplayedAfter: entry.Subsystem.Health,
                IsHealthy: false);
        }
        finally
        {
            activity?.Stop();
        }
    }

    // tryConsumeQuota is called before each RETRY (not the original attempt, which
    // is gated upstream). When it returns false the retry loop bails with repairs.denied++
    // and the caught exception propagates.
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
