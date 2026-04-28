using System.Diagnostics;
using OpenTelemetry.Trace;
using SpaceStationMonitor.Sampling;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class RulesBasedSamplerTests
{
    [Theory]
    [InlineData("RepairAction")]
    [InlineData("StationEvent")]
    [InlineData("CascadeCheck")]
    public void NamedHighSignalSpan_AlwaysRecorded(string name)
    {
        var sampler = new RulesBasedSampler();

        // Try multiple TraceIds; the high-signal names should ignore TraceId hash entirely.
        for (int i = 0; i < 20; i++)
        {
            var result = sampler.ShouldSample(MakeParams(name, ActivityTraceId.CreateRandom()));
            Assert.Equal(SamplingDecision.RecordAndSample, result.Decision);
        }
    }

    [Fact]
    public void SubsystemTick_FallsThroughToTickRatio()
    {
        var sampler = new RulesBasedSampler();
        var reference = new TraceIdRatioBasedSampler(0.10);

        // Pick a TraceId the reference ratio sampler drops; assert ours drops the same one.
        ActivityTraceId droppedId = default;
        for (int i = 0; i < 200; i++)
        {
            var candidate = ActivityTraceId.CreateRandom();
            if (reference.ShouldSample(MakeParams("SubsystemTick", candidate)).Decision == SamplingDecision.Drop)
            {
                droppedId = candidate;
                break;
            }
        }
        Assert.NotEqual(default, droppedId);

        var actual = sampler.ShouldSample(MakeParams("SubsystemTick", droppedId));

        Assert.Equal(SamplingDecision.Drop, actual.Decision);
    }

    [Fact]
    public void DefaultName_FallsThroughToDefaultRatio()
    {
        var sampler = new RulesBasedSampler();
        var reference = new TraceIdRatioBasedSampler(0.25);

        // Pick a TraceId the 25% reference drops; ours should drop the same one for an unnamed span.
        ActivityTraceId droppedId = default;
        for (int i = 0; i < 200; i++)
        {
            var candidate = ActivityTraceId.CreateRandom();
            if (reference.ShouldSample(MakeParams("StationCycle", candidate)).Decision == SamplingDecision.Drop)
            {
                droppedId = candidate;
                break;
            }
        }
        Assert.NotEqual(default, droppedId);

        var actual = sampler.ShouldSample(MakeParams("StationCycle", droppedId));

        Assert.Equal(SamplingDecision.Drop, actual.Decision);
    }

    [Fact]
    public void Description_IsNonEmpty()
    {
        var sampler = new RulesBasedSampler();
        Assert.False(string.IsNullOrEmpty(sampler.Description));
    }

    private static SamplingParameters MakeParams(string name, ActivityTraceId traceId)
        => new SamplingParameters(
            parentContext: default,
            traceId: traceId,
            name: name,
            kind: ActivityKind.Internal,
            tags: null,
            links: null);
}
