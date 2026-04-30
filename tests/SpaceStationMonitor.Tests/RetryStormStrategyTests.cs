using System.Diagnostics;
using System.Diagnostics.Metrics;
using SpaceStationMonitor;
using SpaceStationMonitor.BugStrategies;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class RetryStormStrategyTests
{
    // Unique subsystem name keeps this test's metric observations isolated from
    // any parallel tests that repair "Oxygen" et al.
    private const string TestSubsystem = "RetryStormTest_Target";

    [Fact]
    public void HardZeroFailure_InflatesRepairsTotal_ButRepairsFailedOnlyIncrementsOnce()
    {
        int totalAttempts = 0;
        int failedCount = 0;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == Telemetry.MeterName)
                    l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
        {
            if (!TagMatches(tags, "subsystem.name", TestSubsystem)) return;
            if (inst.Name == "station.repairs.total")
                Interlocked.Add(ref totalAttempts, (int)measurement);
            else if (inst.Name == "station.repairs.failed")
                Interlocked.Add(ref failedCount, (int)measurement);
        });
        listener.Start();

        var strategy = new AlwaysFailingRetryStorm();
        var repair = new RepairSystem(strategy);
        var sub = new Subsystem(TestSubsystem, 1.0) { Health = 50 };

        Assert.Throws<GeneralSpaceStationException>(() => repair.Repair(sub, 20));

        // Force the listener to flush any buffered measurements before reading counts.
        listener.Dispose();

        // RetryStorm retries up to 3 times: 1 original attempt + 3 retries = 4 total.
        // Failed increments exactly once, at the point retries are exhausted.
        Assert.Equal(4, totalAttempts);
        Assert.Equal(1, failedCount);
    }

    [Fact]
    public void ProductionClass_InflatesRepairsTotal()
    {
        // Drive the prod RetryStormStrategy (not the AlwaysFailingRetryStorm
        // test subclass) and verify its probabilistic OnRepair surfaces the
        // same counter-ratio bug. Seeded so the test is deterministic.
        int totalAttempts = 0;
        int failedCount = 0;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == Telemetry.MeterName)
                    l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
        {
            if (!TagMatches(tags, "subsystem.name", TestSubsystem)) return;
            if (inst.Name == "station.repairs.total")
                Interlocked.Add(ref totalAttempts, (int)measurement);
            else if (inst.Name == "station.repairs.failed")
                Interlocked.Add(ref failedCount, (int)measurement);
        });
        listener.Start();

        var strategy = new RetryStormStrategy(TestSubsystem, TimeSpan.Zero, seed: 1);
        var repair = new RepairSystem(strategy);
        var sub = new Subsystem(TestSubsystem, 1.0) { Health = 50 };

        for (int i = 0; i < 50; i++)
        {
            try { repair.Repair(sub, 20); }
            catch (GeneralSpaceStationException) { /* expected occasionally */ }
            sub.Health = 50; // reset to avoid capping
        }

        listener.Dispose();

        Assert.True(totalAttempts > 50,
            $"expected retries to inflate total above the 50-call baseline, got {totalAttempts}");
        Assert.True(failedCount > 0,
            $"expected at least one exhausted-retries failure in 50 calls, got {failedCount}");
    }

    [Fact]
    public void Retries_EmitRepairAttemptEventsOnParentActivity()
    {
        using var testSource = new ActivitySource("test.retrystorm.attempts");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s == testSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var parent = testSource.StartActivity("TestRepairParent");
        Assert.NotNull(parent);

        var strategy = new AlwaysFailingRetryStorm();
        var repair = new RepairSystem(strategy);
        var sub = new Subsystem(TestSubsystem, 1.0) { Health = 50 };

        Assert.Throws<GeneralSpaceStationException>(() => repair.Repair(sub, 20));

        var attemptEvents = parent.Events.Where(e => e.Name == "repair.attempt").ToList();
        Assert.True(attemptEvents.Count > 1,
            $"expected >1 repair.attempt events, got {attemptEvents.Count}");

        // Each event carries attempt.number and attempt.outcome.
        foreach (var ev in attemptEvents)
        {
            var tags = ev.Tags.ToDictionary(t => t.Key, t => t.Value);
            Assert.True(tags.ContainsKey("attempt.number"));
            Assert.True(tags.ContainsKey("attempt.outcome"));
        }
    }

    [Fact]
    public void Retries_DeniedByQuota_IncrementRepairsDenied()
    {
        int deniedCount = 0;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == Telemetry.MeterName)
                    l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
        {
            if (!TagMatches(tags, "subsystem.name", TestSubsystem)) return;
            if (inst.Name == "station.repairs.denied")
                Interlocked.Add(ref deniedCount, (int)measurement);
        });
        listener.Start();

        // Single retry budget: AlwaysFailingRetryStorm fails on every attempt, so the
        // retry loop should consume the budget and trip the denied counter.
        int retriesRemaining = 1;
        bool TryConsumeRetry()
        {
            if (retriesRemaining <= 0) return false;
            retriesRemaining--;
            return true;
        }

        var strategy = new AlwaysFailingRetryStorm();
        var repair = new RepairSystem(strategy);
        var sub = new Subsystem(TestSubsystem, 1.0) { Health = 50 };

        Assert.Throws<GeneralSpaceStationException>(() =>
            repair.Repair(sub, 20, TryConsumeRetry));

        listener.Dispose();

        Assert.True(deniedCount > 0,
            $"expected quota exhaustion to trip repairs.denied, got {deniedCount}");
    }

    [Fact]
    public void NonBugTargetSubsystem_DoesNotRetry()
    {
        // RetryStorm only retries on the bug target. A different subsystem
        // goes through the happy path.
        var strategy = new RetryStormStrategy("SomeOtherSubsystem", TimeSpan.Zero);
        var repair = new RepairSystem(strategy);
        var sub = new Subsystem("UnrelatedSubsystem_" + Guid.NewGuid().ToString("N")[..6], 1.0)
        {
            Health = 50,
        };

        var result = repair.Repair(sub, 20);

        Assert.Equal(20, result.Applied);
        Assert.True(result.IsHealthy);
    }

    [Fact]
    public void ShouldRetryAfterFailure_StopsAtMaxRetries()
    {
        var strategy = new RetryStormStrategy("Oxygen", TimeSpan.Zero);
        var sub = new Subsystem("Oxygen", 1.0);

        Assert.True(strategy.ShouldRetryAfterFailure(sub, 0));
        Assert.True(strategy.ShouldRetryAfterFailure(sub, 2));
        Assert.False(strategy.ShouldRetryAfterFailure(sub, 3));
        Assert.False(strategy.ShouldRetryAfterFailure(sub, 10));
    }

    [Fact]
    public void ShouldRetryAfterFailure_OnlyForBugTarget()
    {
        var strategy = new RetryStormStrategy("Oxygen", TimeSpan.Zero);
        var other = new Subsystem("Power", 1.0);

        Assert.False(strategy.ShouldRetryAfterFailure(other, 0));
    }

    private class AlwaysFailingRetryStorm : RetryStormStrategy
    {
        public AlwaysFailingRetryStorm()
            : base(TestSubsystem, TimeSpan.Zero) { }

        public override int OnRepair(Subsystem sub, int requested, ref int retryCount)
        {
            if (IsBugActive && sub.Name == BugTargetSubsystem)
                throw new GeneralSpaceStationException("forced test failure");
            return requested;
        }
    }

    private static bool TagMatches(
        ReadOnlySpan<KeyValuePair<string, object?>> tags, string key, string value)
    {
        foreach (var kv in tags)
        {
            if (kv.Key == key && kv.Value?.ToString() == value) return true;
        }
        return false;
    }
}
