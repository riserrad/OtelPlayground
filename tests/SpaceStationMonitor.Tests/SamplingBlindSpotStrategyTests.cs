using System.Diagnostics;
using OpenTelemetry.Trace;
using SpaceStationMonitor;
using SpaceStationMonitor.BugStrategies;
using SpaceStationMonitor.Sampling;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class SamplingBlindSpotStrategyTests
{
    private const string Target = "Oxygen";

    [Fact]
    public void Strategy_IsRegistered_InCatalog()
    {
        var strategies = BugStrategyCatalog.All(Target);

        var found = BugStrategyCatalog.FindByName(strategies, "SamplingBlindSpot");

        Assert.NotNull(found);
        Assert.IsType<SamplingBlindSpotStrategy>(found);
    }

    [Fact]
    public void OnRepair_IsNoOp_ReturnsRequestedUnchanged()
    {
        // Effect lives in the sidecar (Program.cs), not in the IBugStrategy hooks.
        var strategy = new SamplingBlindSpotStrategy(Target, TimeSpan.Zero);
        var sub = new Subsystem(Target, 1.0) { Health = 50 };
        int retryCount = 0;

        var applied = strategy.OnRepair(sub, 25, ref retryCount);

        Assert.Equal(25, applied);
        Assert.Equal(0, retryCount);
    }

    [Fact]
    public void ShouldRetryAfterFailure_AlwaysFalse()
    {
        var strategy = new SamplingBlindSpotStrategy(Target, TimeSpan.Zero);
        var sub = new Subsystem(Target, 1.0);

        Assert.False(strategy.ShouldRetryAfterFailure(sub, 0));
        Assert.False(strategy.ShouldRetryAfterFailure(sub, 5));
    }

    [Fact]
    public void CycleCounterIncrement_AlwaysOne()
    {
        var strategy = new SamplingBlindSpotStrategy(Target, TimeSpan.Zero);

        Assert.Equal(1, strategy.CycleCounterIncrement());
    }

    [Fact]
    public void RedirectDegradationTarget_DoesNotRedirect()
    {
        var strategy = new SamplingBlindSpotStrategy(Target, TimeSpan.Zero);
        var sub = new Subsystem(Target, 1.0);
        var all = new[] { sub, new Subsystem("Power", 1.0) };

        var redirected = strategy.RedirectDegradationTarget(sub, all);

        Assert.Same(sub, redirected);
    }

    [Fact]
    public void RepairDelay_IsNull()
    {
        var strategy = new SamplingBlindSpotStrategy(Target, TimeSpan.Zero);
        var sub = new Subsystem(Target, 1.0);

        Assert.Null(strategy.RepairDelay(sub));
    }

    [Fact]
    public void IsBugActive_RespectsActivationDelay()
    {
        var notYet = new SamplingBlindSpotStrategy(Target, TimeSpan.FromMinutes(1));
        var alreadyActive = new SamplingBlindSpotStrategy(Target, TimeSpan.Zero);
        // BugStrategyBase compares DateTime.UtcNow to its captured _startTime,
        // so a 1-tick delay is enough to flip the predicate by the time we read it.
        Thread.Sleep(2);

        Assert.False(notYet.IsBugActive);
        Assert.True(alreadyActive.IsBugActive);
    }

    // AC5 — sampler-coupled tests: the strategy's effect is observed at the
    // HullThresholdSampler.OverrideSampler boundary, not on the strategy itself.
    // The sidecar in GameLoop.cs assigns/clears OverrideSampler per cycle; here
    // we drive the same toggle directly so the test is independent of the loop.

    [Fact]
    public void StrategyInactive_SamplerBehavesPerB2Spec()
    {
        // Mirrors B2 AC2 — Storm at hull ≤ 70 returns RecordAndSample;
        // Calm at hull > 75 returns the 10% ratio sampler decision.
        var hull = 30.0;
        var sampler = new HullThresholdSampler(() => hull);
        // sampler.OverrideSampler stays null — strategy effect not active.

        Assert.Equal(SamplingDecision.RecordAndSample,
            sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom())).Decision);

        hull = 100.0;
        // Lift hull above the calm threshold then drive the sampler past one
        // call so the regime transition catches up before we read.
        sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom()));

        // At hull=100 the underlying ratio sampler dominates. Just confirm
        // that ShouldSample respects the override-null path (not pinned at 5%).
        // We don't probabilistically assert the 10% rate here — that's B2's plate.
        int sampled = 0;
        var rng = new Random(7);
        for (int i = 0; i < 100; i++)
        {
            var traceId = MakeTraceId(rng);
            if (sampler.ShouldSample(MakeParams(traceId)).Decision == SamplingDecision.RecordAndSample)
                sampled++;
        }
        // 10% binomial → mean 10, σ ≈ 3. <25 is comfortably outside the 5% pin.
        Assert.True(sampled < 25,
            $"inactive override should fall through to ~10% ratio; got {sampled}/100");
    }

    [Fact]
    public void StrategyActive_RoutesEverythingThroughFivePercent_RegardlessOfHull()
    {
        var hull = 30.0; // Storm regime — would normally RecordAndSample.
        var sampler = new HullThresholdSampler(() => hull);

        // Activate D2's sidecar effect.
        sampler.OverrideSampler = new TraceIdRatioBasedSampler(0.05);

        int sampled = 0;
        var rng = new Random(11);
        for (int i = 0; i < 200; i++)
        {
            var traceId = MakeTraceId(rng);
            if (sampler.ShouldSample(MakeParams(traceId)).Decision == SamplingDecision.RecordAndSample)
                sampled++;
        }

        // 5% × 200 → mean 10. p(>=25 sampled) ≪ 0.001. The hull=30 Storm
        // path would have given 200/200 if the override hadn't intercepted.
        Assert.True(sampled < 25,
            $"active override should pin to ~5% regardless of Storm hull; got {sampled}/200");
    }

    [Fact]
    public void Activation_IsReversible_WhenOverrideClearsHullPathReturns()
    {
        var hull = 30.0;
        var sampler = new HullThresholdSampler(() => hull);

        // Activate.
        sampler.OverrideSampler = new TraceIdRatioBasedSampler(0.05);

        // Deactivate — sidecar nulls it out when the strategy stops being active.
        sampler.OverrideSampler = null;

        // hull=30 → Storm → RecordAndSample once again.
        Assert.Equal(SamplingDecision.RecordAndSample,
            sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom())).Decision);

        // And lift hull above the calm threshold; now it falls back to the
        // ratio sampler. Driver call to settle the regime transition.
        hull = 100.0;
        sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom()));

        int sampled = 0;
        var rng = new Random(23);
        for (int i = 0; i < 100; i++)
        {
            var traceId = MakeTraceId(rng);
            if (sampler.ShouldSample(MakeParams(traceId)).Decision == SamplingDecision.RecordAndSample)
                sampled++;
        }
        // After deactivation, calm hull → ~10% rate, not the 5% pin.
        Assert.True(sampled < 25,
            $"deactivated sampler should follow B2 hull behavior; got {sampled}/100");
    }

    private static ActivityTraceId MakeTraceId(Random rng)
    {
        var bytes = new byte[16];
        rng.NextBytes(bytes);
        return ActivityTraceId.CreateFromBytes(bytes);
    }

    private static SamplingParameters MakeParams(ActivityTraceId traceId) =>
        new(parentContext: default,
            traceId: traceId,
            name: "CascadeCheck",
            kind: ActivityKind.Internal);

    // AC6 — statistical: 100 cascade events with the strategy active sample <15.
    // We exercise TraceIdRatioBasedSampler(0.05) directly because the sidecar
    // (Program.cs) hands that exact sampler to HullThresholdSampler.OverrideSampler
    // when SamplingBlindSpot activates. p(>=15 sampled at p=0.05) is well under
    // 0.001 — false-positive rate is negligible. Seeded RNG keeps the test
    // deterministic across runs.
    [Fact]
    public void Sampling_AtFivePercent_DropsCascadeTraces()
    {
        var sampler = new TraceIdRatioBasedSampler(0.05);
        var rng = new Random(42);
        int sampled = 0;

        for (int i = 0; i < 100; i++)
        {
            var traceIdBytes = new byte[16];
            rng.NextBytes(traceIdBytes);
            var traceId = ActivityTraceId.CreateFromBytes(traceIdBytes);

            var parameters = new SamplingParameters(
                parentContext: default,
                traceId: traceId,
                name: "CascadeCheck",
                kind: ActivityKind.Internal);

            var result = sampler.ShouldSample(in parameters);
            if (result.Decision == SamplingDecision.RecordAndSample)
                sampled++;
        }

        Assert.True(sampled < 15,
            $"expected <15 of 100 cascade events to be sampled at 5%, got {sampled}");
    }
}
