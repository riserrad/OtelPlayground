using System.Diagnostics;
using OpenTelemetry.Trace;
using SpaceStationMonitor.Sampling;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class HullThresholdSamplerTests
{
    [Fact]
    public void ShouldSample_AtFullHealth_ReturnsRatioBased()
    {
        var sampler = new HullThresholdSampler(() => 100.0);
        var reference = new TraceIdRatioBasedSampler(0.10);

        // Find a TraceId the reference 10% sampler drops; assert ours drops the same one.
        ActivityTraceId droppedId = default;
        bool found = false;
        for (int i = 0; i < 200; i++)
        {
            var candidate = ActivityTraceId.CreateRandom();
            if (reference.ShouldSample(MakeParams(candidate)).Decision == SamplingDecision.Drop)
            {
                droppedId = candidate;
                found = true;
                break;
            }
        }
        Assert.True(found, "Could not find a dropped TraceId in 200 tries");

        var actual = sampler.ShouldSample(MakeParams(droppedId));

        Assert.Equal(SamplingDecision.Drop, actual.Decision);
        Assert.Equal(SamplingRegime.Calm, sampler.CurrentRegime);
    }

    [Fact]
    public void ShouldSample_AtStormThreshold_ReturnsRecord()
    {
        var sampler = new HullThresholdSampler(() => 70.0);

        var result = sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom()));

        Assert.Equal(SamplingDecision.RecordAndSample, result.Decision);
        Assert.Equal(SamplingRegime.Storm, sampler.CurrentRegime);
    }

    [Fact]
    public void ShouldSample_BelowThreshold_ReturnsRecord()
    {
        var sampler = new HullThresholdSampler(() => 30.0);

        // Every TraceId should RecordAndSample at this hull, regardless of TraceId hash.
        for (int i = 0; i < 20; i++)
        {
            var result = sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom()));
            Assert.Equal(SamplingDecision.RecordAndSample, result.Decision);
        }
    }

    [Fact]
    public void ParentBased_ChildInheritsParentDecision()
    {
        // Hull at 30% means HullThresholdSampler would normally RecordAndSample.
        // ParentBased wraps it and respects the parent's TraceFlags.
        var sampler = new HullThresholdSampler(() => 30.0);
        var parentBased = new ParentBasedSampler(sampler);

        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var droppedParent = new ActivityContext(traceId, spanId, ActivityTraceFlags.None, isRemote: false);

        var p = new SamplingParameters(droppedParent, traceId, "child", ActivityKind.Internal, null, null);
        var result = parentBased.ShouldSample(p);

        // Parent didn't record → child shouldn't record either.
        Assert.NotEqual(SamplingDecision.RecordAndSample, result.Decision);
    }

    [Fact]
    public void Hysteresis_DoesNotFlipUntilUpperBound()
    {
        double hull = 60.0;
        var sampler = new HullThresholdSampler(() => hull);

        // Start at hull = 60: ≤ 70 → Storm
        sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom()));
        Assert.Equal(SamplingRegime.Storm, sampler.CurrentRegime);

        // Raise to 73 (dead-band 70 < hull ≤ 75): stays Storm
        hull = 73.0;
        sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom()));
        Assert.Equal(SamplingRegime.Storm, sampler.CurrentRegime);

        // Raise to 76 (above 75): flips to Calm
        hull = 76.0;
        sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom()));
        Assert.Equal(SamplingRegime.Calm, sampler.CurrentRegime);

        // Drop to 71 (dead-band): stays Calm
        hull = 71.0;
        sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom()));
        Assert.Equal(SamplingRegime.Calm, sampler.CurrentRegime);

        // Drop to 69 (below 70): flips to Storm
        hull = 69.0;
        sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom()));
        Assert.Equal(SamplingRegime.Storm, sampler.CurrentRegime);
    }

    [Fact]
    public void OverrideSampler_TakesPriorityOverHull()
    {
        var sampler = new HullThresholdSampler(() => 30.0);

        // Sanity: without override at hull=30, decisions are RecordAndSample.
        Assert.Equal(SamplingDecision.RecordAndSample,
            sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom())).Decision);

        // Override to a 0% sampler — drops everything regardless of hull.
        sampler.OverrideSampler = new TraceIdRatioBasedSampler(0.0);
        for (int i = 0; i < 10; i++)
        {
            var r = sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom()));
            Assert.Equal(SamplingDecision.Drop, r.Decision);
        }

        // Clearing override restores hull-driven behavior.
        sampler.OverrideSampler = null;
        Assert.Equal(SamplingDecision.RecordAndSample,
            sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom())).Decision);
    }

    // RISK-1: while OverrideSampler is set, the badge must read Calm regardless
    // of hull — that's the visible-but-easy-to-miss D2 SamplingBlindSpot teaching
    // beat (counters say cascades happened; badge says Calm; player digs in).
    // The underlying _currentRegime stays frozen; clearing the override resumes
    // hull-driven transitions cleanly.
    [Fact]
    public void CurrentRegime_ForcesCalm_WhenOverrideSamplerSet()
    {
        double hull = 60.0; // Storm baseline.
        var sampler = new HullThresholdSampler(() => hull);
        Assert.Equal(SamplingRegime.Storm, sampler.CurrentRegime);

        // Phase 1: hull=60 + override set → Calm.
        sampler.OverrideSampler = new TraceIdRatioBasedSampler(0.05);
        Assert.Equal(SamplingRegime.Calm, sampler.CurrentRegime);

        // Phase 2: hull=90 + override still set → Calm.
        hull = 90.0;
        Assert.Equal(SamplingRegime.Calm, sampler.CurrentRegime);

        // Phase 3: clear override + hull=90 → Calm via hull path.
        // Drive ShouldSample once so UpdateRegime() catches up to the new hull.
        sampler.OverrideSampler = null;
        sampler.ShouldSample(MakeParams(ActivityTraceId.CreateRandom()));
        Assert.Equal(SamplingRegime.Calm, sampler.CurrentRegime);
    }

    private static SamplingParameters MakeParams(ActivityTraceId traceId)
        => new SamplingParameters(
            parentContext: default,
            traceId: traceId,
            name: "test",
            kind: ActivityKind.Internal,
            tags: null,
            links: null);
}
